using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Бонус урона по цели в определённом СОСТОЯНИИ: без поглощающего щита, оглушённой, раненой и т.п.
    /// Потребитель: Тир 5 [Б] Флагеллант-Всадник — «бонус урона по целям без щитов».
    ///
    /// Что считать «щитом», в дизайне не задано, поэтому условие выбирается в Inspector,
    /// а не зашито в код: сейчас поддержано состояние «нет активного поглощающего щита»
    /// (кирпич B17 <see cref="AbsorbShield"/>) и «цель оглушена». Другие условия добавляются полем,
    /// а не новым скриптом.
    /// </summary>
    public class BonusDamageVsCondition : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        /// <summary>Условие на цели, при котором действует бонус.</summary>
        public enum TargetCondition
        {
            [InspectorName("У цели НЕТ поглощающего щита")] NoAbsorbShield,
            [InspectorName("У цели ЕСТЬ поглощающий щит")] HasAbsorbShield,
            [InspectorName("Цель оглушена")] Stunned,
            [InspectorName("Цель ранена ниже порога ХП")] BelowHpFraction
        }

        [Header("Бонус по состоянию цели")]
        [Tooltip("Какое состояние цели включает бонус.")]
        public TargetCondition condition = TargetCondition.NoAbsorbShield;

        [Tooltip("Порог доли ХП для условия «ранена ниже порога». 0.5 = ниже половины здоровья.")]
        [Range(0f, 1f)]
        public float hpFraction = 0.5f;

        [Tooltip("Во сколько раз больше урона при выполненном условии, по уровням. 1.25 = +25%.")]
        public float[] damageMultiplier = new float[1] { 1.25f };

        [Tooltip("Считать только прямые атаки.")]
        public bool onlyDirectAttack = true;

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
                        InterflowAbility.CallbackAdd(unit.OnAfterDamageDealCallbacks, this, level, BonusApply);
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            InterflowAbility.CallbackRemove(unit.OnAfterDamageDealCallbacks, this, level);
        }

        void BonusApply(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack,
                        DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level)
        {
            if (NetworkConnectionHandler.isClient) return; // правило 6
            if (targetUnit == null || byUnit == null) return;
            if (onlyDirectAttack && !directAttack) return;
            if (!ConditionMet(targetUnit)) return;

            float mult = LevelValue(damageMultiplier, level, 1f);
            if (mult <= 1f) return;

            float extra = dmg * (mult - 1f);
            targetUnit.GetDamage(extra, damageType, byOwner, byUnit, false, out float _);

            RequestForceSync();
        }

        bool ConditionMet(Unit target)
        {
            switch (condition)
            {
                case TargetCondition.NoAbsorbShield: return !AbsorbShield.IsActiveOn(target);
                case TargetCondition.HasAbsorbShield: return AbsorbShield.IsActiveOn(target);
                case TargetCondition.Stunned: return target.stunned;
                case TargetCondition.BelowHpFraction: return target.maxHealth > 0f && (target.health / target.maxHealth) < hpFraction;
            }

            return false;
        }

    }
}
