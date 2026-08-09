using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Asset menu
    public class Active : Ability
    {
        // Level 0 means first level. If you want to damage based on level do not forget to add 1. Example: (damage * (level + 1)
        public override AbilityType type { get { return AbilityType.Active; } } // Specify type

        // This function is called when the ability is clicked
        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            // Do something
        }
    }
}
