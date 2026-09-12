using UnityEngine;

namespace StrategyCore
{
    // Кирпич B1 — Вампиризм: носитель лечится на долю нанесённого им урона. Passive-SO по образцу Basher/Crit
    // (правило 2). Хук OnAfterDamageDeal — колбэк копируется в Projectile → покрывает и дальних. Серверо-авторит.
    // Ассет StrategyCore не трогаем (правило 1). Числа — только в Inspector (правило 3).
    public class LifestealPassive : InterflowAbility
    {
        public override AbilityType type => AbilityType.Passive;

        [Header("Вампиризм (B1)")]
        [Tooltip("Доля нанесённого урона, возвращаемая носителю в ХП, по уровням (0.1 = 10%). [БАЛАНС — Влад]")]
        public float[] healPercent;

        [Tooltip("Лечить только от прямых атак (не от урона способностей/эффекторов/сплэша). " +
                 "§6.1: выбор режима — в будущем нужны оба; стартовое значение — только прямые.")]
        public bool onlyDirectAttack = true;
        [Range(0,1)] public float casterHpBelow;
        public Technology upgradeTechnology;
        [Range(0,1)] public float upgradedHealPercent;
        public CompositeSkill healPresentation;

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;
            // Идемпотентность: не добавлять второй колбэк при повторном Unlock (приёмка B1).
            for (int i = 0; i < unit.OnAfterDamageDealCallbacks.Count; i++)
                if (unit.OnAfterDamageDealCallbacks[i].Ability == this && unit.OnAfterDamageDealCallbacks[i].Level == level) return;

            unit.OnAfterDamageDealCallbacks.Add(new AfterDamageDealCallback
            {
                Callback = LifestealApply,
                Ability = this,
                Level = level
            });
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            for (int i = 0; i < unit.OnAfterDamageDealCallbacks.Count; i++)
            {
                AfterDamageDealCallback c = unit.OnAfterDamageDealCallbacks[i];
                if (c.Ability == this && c.Level == level) { unit.OnAfterDamageDealCallbacks.RemoveAt(i); break; }
            }
        }

        // Лечение носителя (byUnit) на долю нанесённого урона. dmg — величина атаки (до брони; актуально для баланса,
        // пост-броневой урон в колбэк не приходит). ChangeHP клампит до maxHealth и синкает клиентам.
        void LifestealApply(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack,
                            DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level)
        {
            if (NetworkConnectionHandler.isClient) return;                                  // страховка (правило 6)
            if (onlyDirectAttack && !directAttack) return;                                  // §6.1: только прямые (по флагу)
            if (byUnit == null || byUnit.dead) return;
            if(casterHpBelow>0&&!SkillTargeting.IsBelowHealthThreshold(byUnit,casterHpBelow))return;                                                     // некого лечить
            if (unitSelector.AnySelectors() && !UnitSelector.IsUnitCompatible(byOwner, targetUnit, unitSelector)) return;
            if (healPercent == null || level >= healPercent.Length) return;

            float fraction=upgradeTechnology&&OptionalTechUnlocked(upgradeTechnology,byOwner)?upgradedHealPercent:healPercent[level];
            float heal = dmg * fraction;
            if (heal <= 0f) return;
            byUnit.ChangeHP(heal);
            if(healPresentation)EmitSkillFired(byUnit,healPresentation,level,byUnit,byUnit.transform.position);
        }
    }
}
