using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // CompositeSkill.ApplyStatus.cs — контроль, состояния, лечение, баф, ослепление (блоки 3-6, 8). Вырезано 1:1 из CompositeSkill.Blocks.cs (разрезка на partial-ы, правило 22).
    public partial class CompositeSkill
    {
        // -------------------------------------------------------------- 3. КОНТРОЛЬ --
        void ApplyStatus(Unit castingUnit, int castingPlayer, int level, Unit target, bool stunCarriedByProjectile)
        {
            if (status == null || !status.enabled) return;

            // Оглушение переносится снарядом (штатное поле stunTime) — мгновенно его не вешаем.
            // Объявление вынесено из ветки только ради строки лога: при доставке снарядом
            // LevelValue по-прежнему НЕ зовётся, в логе стоит ноль и отдельная пометка.
            float stun = 0f;
            if (!stunCarriedByProjectile)
            {
                stun = LevelValue(status.stunSeconds, level);
                if (stun > 0f) target.Stun(stun, castingUnit, castingPlayer);
            }

            float disarm = LevelValue(status.disarmSeconds, level);
            if (disarm > 0f) target.Disarm(disarm, castingUnit, castingPlayer);

            float mute = LevelValue(status.muteSeconds, level);
            if (mute > 0f) target.Mute(mute, castingUnit, castingPlayer);

            if (InterflowDebug.FullOn) LogStatus(target, stun, disarm, mute, stunCarriedByProjectile);
        }

        // ------------------------------------------------------------- 4. ЭФФЕКТОРЫ --
        // Ассет эффектора говорит ЧТО происходит, умение — СКОЛЬКО и КАК ДОЛГО (ADR-006 §5.2).
        // Здесь же вешается эффектор-значок состояния: он тоже эффектор, только своих чисел не имеет.
        void ApplyEffectors(int castingPlayer, int level, Unit target)
        {
            int applied = 0; // сколько записей реально легло на цель — нужно строке лога

            if (effectors != null && effectors.enabled && effectors.records != null)
            {
                for (int i = 0; i < effectors.records.Length; i++)
                {
                    SkillEffectorRecord r = effectors.records[i];
                    if (r == null || r.effector == null) continue;

                    // unitOwner = null: тот же владелец, что был у прежнего массивного вызова
                    // Effector.EffectorAdd(castingPlayer, target, set) — поведение не меняется.
                    Effector.EffectorAdd(target, r.effector, null, castingPlayer, 0f,
                                         RecordPower(r, level), RecordDuration(r, level));
                    applied++;
                }
            }

            if (statusEffector != null)
                Effector.EffectorAdd(target, statusEffector, null, castingPlayer);

            if (InterflowDebug.FullOn) LogEffectors(target, level, applied);
        }

        /// <summary>Множитель силы записи на уровне. Пусто или 0 — как в ассете (множитель 1).</summary>
        /// Выборка с КЛАМПОМ к последнему элементу (LevelValue) — семантика по умолчанию
        /// для нового кода, см. InterflowAbility.
        public static float RecordPower(SkillEffectorRecord r, int level)
        {
            if (r == null) return 1f;
            float v = LevelValue(r.power, level);
            return v > 0f ? v : 1f;
        }

        /// <summary>Длительность записи на уровне, секунды. Пусто или 0 — «не переопределять» (−1).</summary>
        public static float RecordDuration(SkillEffectorRecord r, int level)
        {
            if (r == null) return -1f;
            float v = LevelValue(r.duration, level);
            return v > 0f ? v : -1f;
        }

        // ---------------------------------------------------------------- 5. ЛЕЧЕНИЕ --
        void ApplyHeal(int level, Unit target)
        {
            if (heal == null || !heal.enabled) return;

            float amount = LevelValue(heal.flat, level);
            float percent = LevelValue(heal.percentOfMaxHp, level);
            if (percent > 0f) amount += percent / 100f * target.maxHealth; // проценты целым числом: 25 = 25%

            if (amount > 0f) target.ChangeHP(amount); // сам клампит до максимума и синкает клиентам
        }

        // -------------------------------------------------------------------- 6. БАФ --
        void ApplyBuff(Unit castingUnit, int level, Unit target)
        {
            if (buff == null || !buff.enabled) return;
            if (LevelValue(buff.duration, level) <= 0f) return;

            SkillBuff.Apply(target, this, level, castingUnit);
        }
        // ------------------------------------------------------------- 8. ОСЛЕПЛЕНИЕ --
        void ApplyBlind(Unit castingUnit, int castingPlayer, int level, Unit target)
        {
            if (blind == null || !blind.enabled) return;

            float chance = LevelValue(blind.chance, level);
            float duration = LevelValue(blind.duration, level);
            // [Interflow fix 2026-09-03 control-as-effectors] Слепота стала состоянием: воронка Unit.Blind
            // вместо снесённого компонента BlindDebuff. Поля блока (шанс, длительность) не изменились.
            if (chance > 0f && duration > 0f) target.Blind(chance, duration, castingUnit, castingPlayer);

            if (InterflowDebug.FullOn) LogBlind(target, chance, duration);
        }
    }
}
