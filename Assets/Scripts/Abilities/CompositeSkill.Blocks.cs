using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{

    // =====================================================================================
    // Исполнитель блоков. Единственная точка, где блоки применяются, — порядок фиксирован здесь.
    // Вызывается ТОЛЬКО на сервере (гейт стоит в CompositeSkill.Execute).
    // =====================================================================================
    public partial class CompositeSkill
    {
        /// <summary>
        /// Применить включённые блоки в фиксированном порядке:
        /// стоимость → урон → контроль → эффекторы → лечение → баф → щит → ослепление →
        /// призыв → зона → серверный сервис.
        /// Цель, погибшую от урона этого же каста, дальше не обрабатываем (паттерн EffectorArea).
        /// </summary>
        /// Вызывается ТОЛЬКО на сервере: клиент до этого места не доходит. Значки состояний и VFX
        /// уезжают клиенту отдельным сообщением (CompositeSkill.SendPresentation) — геймплейного
        /// состояния у клиента не появляется вовсе.
        /// <param name="skipProjectileCarried">
        /// true, когда доставка идёт снарядом: урон, контроль-оглушение и эффекторы уже переданы снаряду
        /// и мгновенно применяться не должны.
        /// </param>
        void ApplyEffects(Unit castingUnit, int castingPlayer, int level, List<Unit> targets,
                          Vector3 origin, bool skipProjectileCarried)
        {
            // ---------- 1. Стоимость в здоровье кастера ----------
            if (selfCost != null && selfCost.enabled && castingUnit != null)
            {
                float pct = LevelValue(selfCost.percentOfCurrentHp, level);
                float flat = LevelValue(selfCost.flatHp, level);

                // Списание через PayHealth: штатный ChangeHP только зажимает здоровье в ноль и не убивает,
                // поэтому кастер с выключенным «Запретить каст, если стоимость добьёт» оставался жив с нулём ХП.
                if (pct > 0f) PercentHpCost.PayFromCaster(castingUnit, pct);
                if (flat > 0f) PayHealth(castingUnit, flat);
            }

            // ---------- 2..15. Блоки по каждой цели (порядок фиксирован) ----------
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    Unit t = targets[i];
                    if (t == null || t.dead) continue;

                    // Рывок идёт ПЕРВЫМ: не притянулась — цель выпадает из каста целиком
                    // (решение Artsiom 2026-08-06), КД и мана при этом списаны штатно.
                    if (!ApplyPull(castingUnit, t)) continue;

                    // Урон запоминаем: из него блок вторичных целей берёт долю на лечение.
                    float baseDamageToTarget = skipProjectileCarried
                        ? 0f
                        : ApplyDamage(castingUnit, castingPlayer, level, t, origin);
                    if (t.dead) continue; // погиб от этого же урона — дальше по нему не работаем

                    ApplyDrain(castingUnit, level, t);
                    if (t.dead) continue; // высасывание добило — дальше по нему не работаем

                    ApplyStatus(castingUnit, castingPlayer, level, t, skipProjectileCarried);
                    ApplyEffectors(castingPlayer, level, t); // эффекторы снарядом не переносятся — вешаем сами
                    ApplyHeal(level, t);
                    ApplyMana(level, t);
                    ApplyBuff(castingUnit, level, t);
                    ApplyShield(castingPlayer, level, t);
                    ApplyBlind(castingUnit, castingPlayer, level, t);
                    ApplyMorph(castingUnit, castingPlayer, level, t);
                    ApplyOwnership(castingUnit, t);          // после всех эффектов: меняет сторону цели
                    ApplySecondary(castingPlayer, level, t, baseDamageToTarget); // своя выборка вокруг этой цели
                    ApplyKnockback(castingUnit, level, t);   // последним: сдвигает цель, всё позиционное уже сработало
                }
            }

            // ---------- 16. Призыв ----------
            if (summon != null && summon.enabled) ApplySummon(castingUnit, castingPlayer, level);

            // ---------- 17. Зона на земле ----------
            if (groundZone != null && groundZone.enabled) ApplyGroundZone(castingUnit, castingPlayer, level, origin);

            // ---------- 18. Перемещение кастера ----------
            ApplyCasterMove(castingUnit, origin);

            // ---------- 19. Серверный сервис ----------
            if (delegateService != null && delegateService.enabled) ApplyDelegate(castingUnit, castingPlayer, origin);
        }

        // ------------------------------------------------------------------ 2. УРОН --
        /// <returns>
        /// Сумма урона, ЗАПИСАННОГО в блоке для этой цели (по всем сработавшим записям), — до брони,
        /// сопротивлений и щитов. Нужна блоку вторичных целей: он лечит долей от неё.
        /// Фактически прошедший урон здесь не считается — так же вёл себя класс HolyFire, чьё поведение
        /// блок повторяет; смена на фактический молча изменила бы силу лечения на бронированных целях.
        /// </returns>
        float ApplyDamage(Unit castingUnit, int castingPlayer, int level, Unit target, Vector3 origin)
        {
            if (damage == null || !damage.enabled || damage.entries == null) return 0f;
            if (target.dead) return 0f;

            // [Interflow fix 2026-09-04 damage-full-packet] Шаг 4 схемы «пакет и приёмник»: ВСЕ записи блока —
            // одним пакетом на цель. Приёмник разбирает записи по порядку и останавливается, если цель погибла
            // (остаток пакета не применяется, §4 схемы) — прежний выход «target.dead → return» внутри цикла
            // переехал туда. Фильтр «кому достаётся запись» остаётся у отправителя (§3 схемы: цели выбирает
            // отправитель). Буфер записей переиспользуется между кастами — выделение только на массив
            // остатка внутри пакета, и только когда записей больше одной.
            if (damageRecordBuffer == null || damageRecordBuffer.Length < damage.entries.Length)
                damageRecordBuffer = new DamageRecord[damage.entries.Length];

            int count = 0;
            float dealt = 0f;

            for (int e = 0; e < damage.entries.Length; e++)
            {
                SkillDamageEntry entry = damage.entries[e];
                if (entry == null || entry.damageType == null) continue;

                float amount = LevelValue(entry.amount, level);
                if (amount <= 0f) continue;

                // Кого именно задевает ЭТА запись — решает штатный предикат селектора,
                // у которого подменены только флаги свой/союзник/враг.
                if (!UnitSelector.IsUnitCompatible(castingPlayer, target, RelationSelector(entry.targets))) continue;

                damageRecordBuffer[count++] = new DamageRecord(amount, entry.damageType);
                dealt += amount;
            }

            if (count == 0) return 0f;

            // false: способность, не прямая атака — состояния автоатаки кастера НЕ вешаются (решение Р7),
            // провокации и промаха бьющего нет.
            DamagePacket packet = DamagePacket.Create(damageRecordBuffer, count, castingPlayer, castingUnit, false, this);

            if (castingUnit != null)
                castingUnit.DealDamage(target, in packet, origin);   // колбэки «после удара» и OnDamageDeal кастера
            else
                target.GetDamage(in packet, out float _);

            return dealt;
        }

        /// <summary>Буфер записей блока «Урон» — переиспользуется между кастами, чтобы не выделять на каждый.</summary>
        DamageRecord[] damageRecordBuffer;

        /// <summary>
        /// Селектор ТОЛЬКО для проверки отношения «свой/союзник/враг». Типовые флаги
        /// (юнит/здание/земля/воздух и пр.) включены все осознанно: набор целей УЖЕ прошёл через
        /// unitSelector скилла при сборе, а в режимах «на себя»/«вся команда» селектор вообще не участвует
        /// и ГД его не заполняет — с копией флагов такой скилл молча не наносил бы урона.
        /// </summary>
        static UnitSelector RelationSelector(SkillDamageTargets relation)
        {
            bool own  = relation != SkillDamageTargets.Enemies;
            bool ally = relation != SkillDamageTargets.Enemies;
            bool foe  = relation != SkillDamageTargets.Allies;

            return new UnitSelector(own, ally, foe, true, true, true, true, true, true, true, true, true);
        }




    }
}
