using UnityEngine;

namespace StrategyCore
{
    public class UnitAbility : Ability
    {
        // What should happen when this ability is used
        // Level 0 means first level. If you want to damage based on level do not forget to add 1. Example: (damage * (level + 1)
        public override AbilityType type { get { return AbilityType.Unit; } } // Specify type

        public override void Use(Unit castingUnit, int castingPlayer, int level, Unit unit)
        {
            Debug.Log(unit.unitName + " was targeted by " + abilityName[0]);
            // Do something with the unit
        }
    }
}
