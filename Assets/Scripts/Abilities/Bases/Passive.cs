using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    public class Passive : Ability
    {
        public override AbilityType type { get { return AbilityType.Passive; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("Passive Effects for each level")]
        public AbilityPassiveEffects[] passiveEffects;

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            passiveEffects[level].AddEffect(unit);

            // Лог включения — только у пассивок, открываемых технологией (специализации юнитов).
            // Безтеховые ядро разблокирует заново на каждое открытие любой технологии — был бы спам.
            if (HasTechGate(level))
                InterflowDebug.Event("ПАССИВКА «" + PassiveLogName() + "» включена у " + InterflowDebug.Name(unit));
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            passiveEffects[level].RemoveEffect(unit);

            if (HasTechGate(level))
                InterflowDebug.Event("ПАССИВКА «" + PassiveLogName() + "» снята у " + InterflowDebug.Name(unit));
        }

        /// <summary>Есть ли у пассивки требование технологии на этом уровне (то есть это специализация).</summary>
        bool HasTechGate(int level)
        {
            if (requiredTech == null || requiredTech.Length == 0) return false;

            int i = Mathf.Clamp(level, 0, requiredTech.Length - 1);
            return requiredTech[i] != null && requiredTech[i].data != null && requiredTech[i].data.Length > 0;
        }

        /// <summary>Отображаемое имя для лога: русское имя способности, иначе имя ассета.</summary>
        string PassiveLogName()
        {
            return (abilityName != null && abilityName.Length > 0 && !string.IsNullOrEmpty(abilityName[0]))
                   ? abilityName[0] : name;
        }
    }
}
