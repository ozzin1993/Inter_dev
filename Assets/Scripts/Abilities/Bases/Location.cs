using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    public class Location : Ability
    {
        // What should happen when this ability is used
        // Level 0 means first level. If you want to damage based on level do not forget to add 1. Example: (damage * (level + 1)
        public override AbilityType type { get { return AbilityType.Location; } } // Specify type

        public override void Use(Unit castingUnit, int castingPlayer, int level, Vector3 position)
        {
            // Do something at location
        }
    }
}
