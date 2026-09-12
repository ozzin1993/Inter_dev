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

        // Статы по уровню — общей выборкой (Б8): массив короче уровня — последняя заполненная строка, и в Unlock, и в Lock,
        // поэтому снимается ровно то, что выдано.
        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            AbilityPassiveEffects effects = InterflowAbility.LevelItem(passiveEffects, level);
            if (effects != null) effects.AddEffect(unit);
            AbilityFacts.Granted(this, unit, level);   // [2026-09-10] показ срабатывания — см. AbilityFacts

            // Лог включения — только у пассивок, открываемых технологией (специализации юнитов).
            // Безтеховые ядро разблокирует заново на каждое открытие любой технологии — был бы спам.
            if (HasTechGate(level))
                InterflowDebug.Event("ПАССИВКА «" + PassiveLogName() + "» включена у " + InterflowDebug.Name(unit));

            if (InterflowDebug.FullOn)
                InterflowDebug.Full("ПАССИВКА «" + PassiveLogName() + "» СТАТЫ ВЫДАНЫ | носитель=" + InterflowDebug.Name(unit) +
                                    " | уровень=" + level +
                                    " | характеристики=" + CompositePassive.StatsText(effects));
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            AbilityPassiveEffects effects = InterflowAbility.LevelItem(passiveEffects, level);
            if (effects != null) effects.RemoveEffect(unit);
            AbilityFacts.Revoked(this, unit, level);   // [2026-09-10] показ срабатывания — см. AbilityFacts

            if (HasTechGate(level))
                InterflowDebug.Event("ПАССИВКА «" + PassiveLogName() + "» снята у " + InterflowDebug.Name(unit));

            if (InterflowDebug.FullOn)
                InterflowDebug.Full("ПАССИВКА «" + PassiveLogName() + "» СТАТЫ СНЯТЫ | носитель=" + InterflowDebug.Name(unit) +
                                    " | уровень=" + level +
                                    " | характеристики=" + CompositePassive.StatsText(effects));
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
