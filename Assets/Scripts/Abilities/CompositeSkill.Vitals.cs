using UnityEngine;

namespace StrategyCore
{
    // ================== КОНСТРУКТОР СКИЛЛА: БЛОКИ ЖИЗНЕННЫХ РЕСУРСОВ (правило 22 — партиал по фиче) ==
    // Высасывание ХП (у цели отнять, кастеру отдать) и восстановление маны.

    /// <summary>4. Высасывание ХП: снять у цели напрямую и отдать долю кастеру.</summary>
    [System.Serializable]
    public class SkillDrainBlock
    {
        [Tooltip("Включить блок: умение вытягивает из цели здоровье.")]
        public bool enabled;

        [Tooltip("Сколько ХП снять числом, по уровням. Складывается с процентами ниже.")]
        public float[] flat;

        [Tooltip("Сколько ХП снять долей от МАКСИМАЛЬНОГО здоровья цели, по уровням. 0.1 — десять процентов максимума.")]
        public float[] percentOfMaxHp;

        [Tooltip("Сколько ХП снять долей от ТЕКУЩЕГО здоровья цели, по уровням. 0.1 — десять процентов остатка.")]
        public float[] percentOfCurrentHp;

        [Tooltip("Какую долю ФАКТИЧЕСКИ снятого получает кастер, по уровням. " +
                 "1 — всё, 0.5 — половина, 0 — не лечит. Снятое меньше запрошенного, если у цели осталось мало ХП.")]
        public float[] casterHealMultiplier;
    }

    /// <summary>8. Восстановление маны целям.</summary>
    [System.Serializable]
    public class SkillManaBlock
    {
        [Tooltip("Включить блок: умение восстанавливает ману.")]
        public bool enabled;

        [Tooltip("Сколько маны вернуть числом, по уровням.")]
        public float[] flat;

        [Tooltip("Сколько маны вернуть долей от МАКСИМАЛЬНОГО запаса цели, по уровням. 0.25 — четверть запаса.")]
        public float[] percentOfMaxMana;
    }

    public partial class CompositeSkill
    {
        /// <summary>
        /// Высасывание ХП. Снятие ПРЯМОЕ, мимо брони и реакций на урон (решение Artsiom 2026-08-06):
        /// броня, множители типов урона, щиты и вампиризм в расчёте не участвуют.
        /// Через <see cref="InterflowAbility.PayHealth"/> — он же добивает цель штатной смертью
        /// и записывает убийство кастеру (награда владельцу).
        /// </summary>
        void ApplyDrain(Unit castingUnit, int level, Unit target)
        {
            if (drain == null || !drain.enabled || target == null || target.dead) return;

            float amount = LevelValue(drain.flat, level)
                         + target.maxHealth * LevelValue(drain.percentOfMaxHp, level)
                         + target.health    * LevelValue(drain.percentOfCurrentHp, level);
            if (amount <= 0f) return;

            // Фактически снятое: у цели может остаться меньше, чем просим, — лечим кастера по факту.
            float taken = Mathf.Min(amount, target.health);
            PayHealth(target, amount, castingUnit);

            if (castingUnit == null || castingUnit.dead) return;

            float multiplier = LevelValue(drain.casterHealMultiplier, level);
            if (multiplier <= 0f || taken <= 0f) return;

            castingUnit.ChangeHP(taken * multiplier);
        }

        /// <summary>Восстановление маны цели. Штатный ChangeMP сам зажимает по максимуму.</summary>
        void ApplyMana(int level, Unit target)
        {
            if (mana == null || !mana.enabled || target == null || target.dead) return;

            float amount = LevelValue(mana.flat, level)
                         + target.maxMana * LevelValue(mana.percentOfMaxMana, level);
            if (amount <= 0f) return;

            target.ChangeMP(amount);
        }
    }
}
