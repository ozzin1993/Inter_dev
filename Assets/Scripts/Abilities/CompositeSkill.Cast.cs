using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StrategyCore
{
    // CompositeSkill.Cast.cs — тело каста и снаряд (Use, Execute, SpawnProjectile). Вырезано 1:1 из CompositeSkill.cs (разрезка на partial-ы, правило 22).
    public partial class CompositeSkill
    {
        // ======================================================================== КАСТ ==

        public override void Use(Unit castingUnit, int castingPlayer, int level)
            => Execute(castingUnit, castingPlayer, level, null, Vector3.zero, false);

        public override void Use(Unit castingUnit, int castingPlayer, int level, Unit unit)
            => Execute(castingUnit, castingPlayer, level, unit,
                       unit != null ? unit.transform.position
                                    : (castingUnit != null ? castingUnit.transform.position : Vector3.zero), true);

        public override void Use(Unit castingUnit, int castingPlayer, int level, Vector3 location)
            => Execute(castingUnit, castingPlayer, level, null, location, true);

        /// <summary>
        /// Тело каста. Выполняется НА ВСЕХ ПИРАХ (ассет рассылает Use через AbilityUseClientRpc),
        /// но ВИЗУАЛА ЗДЕСЬ БОЛЬШЕ НЕТ: с 2026-08-06 презентацию исполняет единый клиентский презентер
        /// по факту SkillFired. Здесь остаются прицел, снаряд, серверный гейт и блоки эффектов.
        /// </summary>
        void Execute(Unit castingUnit, int castingPlayer, int level, Unit explicitTarget, Vector3 explicitLocation, bool hasExplicitAim)
        {
            // ---- Презентация каста ЗДЕСЬ НЕ ИГРАЕТСЯ (перенесена в презентер, 2026-08-06) ----
            // Раньше визуал и звук замаха запускались тут на каждом пире. Теперь их исполняет
            // SkillPresenter по факту SkillFired — иначе на хосте и клиенте получился бы дубль.

            // ---- Прицел ----
            Vector3 casterPos = castingUnit != null ? castingUnit.transform.position : explicitLocation;
            Unit aimUnit = null;
            Vector3 aimPoint = casterPos;

            if (PicksTargetByStrategy)
            {
                if (hasExplicitAim)
                {
                    // Цель/точку подставил автокаст юнита — она пришла в RPC и одинакова на всех пирах.
                    aimUnit = explicitTarget;
                    aimPoint = explicitTarget != null ? explicitTarget.transform.position : explicitLocation;
                }
                else
                {
                    // Каст с кнопки: цель ищет стратегия. Воспроизвести этот выбор на клиенте нельзя —
                    // у него другой набор юнитов и свой генератор случайных чисел. Поэтому дальше только сервер.
                    // Визуал клиент при этом больше не теряет: цель и точку ему привезёт SkillFired (2026-08-06),
                    // по нему презентер покажет и попадание, и визуальную копию снаряда.
                    if (IsClientPeer)
                    {
                        if (InterflowDebug.FullOn)
                            LogCastClientStop("цель выбирает стратегия, на клиенте её выбор не воспроизвести");
                        return;
                    }

                    Unit picked = PickByStrategy(castingUnit, castingPlayer, level);
                    if (picked == null)
                    {
                        if (InterflowDebug.FullOn) LogCastNoTarget();
                        return; // подходящей цели нет — каст проходит впустую, откат тратится штатно
                    }

                    if (targetMode == SkillTargetMode.SmartUnit) aimUnit = picked;
                    aimPoint = picked.transform.position;

                    if (InterflowDebug.FullOn) LogCastAim(castingUnit, picked);
                }
            }

            // ---- Снаряд ----
            // Визуал и звук попадания сюда не входят: их играет презентер по факту SkillFired.
            // Сам снаряд с 2026-08-06 создаётся ТОЛЬКО на сервере: он несёт урон, а значит геймплей
            // (правило 6). Чистому клиенту презентер спавнит визуальную копию с нулевым уроном.

            // Доставка снарядом возможна ТОЛЬКО по конкретному юниту и только с самонаведением:
            // штатный снаряд без цели-юнита наносит урон исключительно по площади, а площадного
            // режима у снаряда скилла нет (см. Projectile.Update / Projectile.Damage). Остальные
            // сочетания — ошибка настройки, её ловит валидатор; здесь бьём мгновенно и говорим об этом.
            bool viaProjectile = delivery == SkillDelivery.Projectile && projectilePrefab != null
                                 && castingUnit != null && aimUnit != null && projectileFollowsTarget;

            if (delivery == SkillDelivery.Projectile && !viaProjectile && IsServerPeer)
                Debug.LogWarning($"[{name}] Доставка снарядом невозможна при этих настройках " +
                                 "(нужны цель-юнит, живой кастер, префаб снаряда и включённое самонаведение) — " +
                                 "умение сработало мгновенно.");

            if (viaProjectile && IsServerPeer) SpawnProjectile(castingUnit, castingPlayer, level, aimUnit);

            if (InterflowDebug.FullOn) LogCastDelivery(viaProjectile);

            // ---- Серверный гейт: всё, что меняет состояние мира, — ниже (правило 6) ----
            // Клиент до сюда не доходит: цели он не считает и эффекторов себе не накладывает.
            // Значки состояний и VFX ему пришлёт сервер отдельным сообщением (SendPresentation).
            if (IsClientPeer)
            {
                if (InterflowDebug.FullOn) LogCastClientStop(null);
                return;
            }

            // Факт срабатывания для презентации: цель и точка уже ФАКТИЧЕСКИЕ (вычислены выше сервером).
            // Публикуется до блоков эффектов — визуал не должен зависеть от того, выжила ли цель.
            // Use зовётся один раз за каст (режим «аура», где он шёл каждый тик, снесён блоком Б7).
            EmitSkillFired(castingUnit, this, level, aimUnit, aimPoint);

            List<Unit> targets = CollectTargets(castingUnit, castingPlayer, level, aimUnit, aimPoint);

            if (InterflowDebug.FullOn) LogCastTargets(level, targets.Count);

            // Лог срабатывания (сюда доходит только сервер).
            if (InterflowDebug.VerboseOn)
            {
                string skillName = (abilityName != null && abilityName.Length > 0 && !string.IsNullOrEmpty(abilityName[0]))
                                   ? abilityName[0] : name;
                string targetText = aimUnit != null
                    ? " по " + InterflowDebug.Name(aimUnit)
                    : (targets.Count > 0 ? ", целей: " + targets.Count : ", целей нет");
                InterflowDebug.Verbose("СКИЛЛ «" + skillName + "»: кастует " +
                                       (castingUnit != null ? InterflowDebug.Name(castingUnit) : "объект без юнита") + targetText);
            }

            ApplyEffects(castingUnit, castingPlayer, level, targets, aimPoint, viaProjectile);

            SendPresentation(targets, level);
            RequestForceSync(); // один раз после всей пачки изменений
        }
        // ================================================================== СНАРЯД ==

        /// <summary>
        /// Спавн штатного снаряда. Снаряд НЕ сетевой объект — его симулирует каждый пир у себя,
        /// поэтому спавним до серверного гейта. Снаряд несёт только то, что умеют его штатные поля:
        /// урон (первая запись блока урона), эффекторы и оглушение.
        /// </summary>
        void SpawnProjectile(Unit castingUnit, int castingPlayer, int level, Unit aimUnit)
        {
            Transform socket = ResolveSocket(castingUnit, spawnSocket);
            Vector3 spawnPos = SocketPosition(castingUnit, spawnSocket, localOffset);
            Quaternion spawnRot = socket != null ? socket.rotation : Quaternion.identity;

            DamageType dmgType;
            float dmg = FirstProjectileDamage(level, out dmgType);

            Projectile spawned = Projectile.Spawn(castingPlayer, castingUnit, projectilePrefab, spawnPos, spawnRot,
                                                  aimUnit, false, dmg, dmgType, true, sourceAbility: this);   // умение-источник для диагностики очереди пакетов (решение Artsiom 05.09.2026)
            if (spawned == null) return;

            // Снаряд уносит только урон и оглушение. Эффекторы ему не отдаём осознанно: ядро применяет
            // Projectile.attackEffectors лишь когда кастер погиб, а при живом кастере накладывает
            // эффекторы ЕГО автоатаки — эффекторы скилла так бы просто потерялись. Поэтому их
            // накладывает сам скилл в момент каста (см. ApplyEffectors).
            spawned.stunTime = (status != null && status.enabled) ? LevelValue(status.stunSeconds, level) : 0f;
        }

        /// <summary>
        /// Урон, который уносит снаряд: первая непустая запись блока урона. У штатного снаряда одно поле
        /// урона и один тип — остальные записи снарядом не переносятся (валидатор предупреждает).
        /// </summary>
        /// <summary>
        /// Тип урона, который уносит снаряд. Нужен клиентской презентации: визуальная копия снаряда
        /// летит с нулевым уроном, но штатный снаряд по прилёте всё равно обращается к типу урона.
        /// </summary>
        public DamageType ProjectileDamageType(int level)
        {
            FirstProjectileDamage(level, out DamageType damageType);
            return damageType;
        }

        float FirstProjectileDamage(int level, out DamageType damageType)
        {
            damageType = null;
            if (damage == null || !damage.enabled || damage.entries == null) return 0f;

            for (int i = 0; i < damage.entries.Length; i++)
            {
                SkillDamageEntry e = damage.entries[i];
                if (e == null || e.damageType == null) continue;

                float amount = LevelValue(e.amount, level);
                if (amount <= 0f) continue;

                damageType = e.damageType;
                return amount;
            }

            return 0f;
        }
    }
}
