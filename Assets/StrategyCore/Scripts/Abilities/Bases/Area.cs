using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Asset menu
    // [CreateAssetMenu(fileName = "Area", menuName = "StrategyCore/Abilities/Area")]
    public class Area : Ability
    {
        // What should happen when this ability is used
        // Level 0 means first level. If you want to damage based on level do not forget to add 1. Example: (damage * (level + 1)
        public override AbilityType type { get { return AbilityType.Area; } } // Specify type

        public override void Use(Unit castingUnit, int castingPlayer, int level, Vector3 position)
        {
            // Get all units inside casted area
            foreach (var unit in Utils.GetUnitsInRadius(new Vector2(position.x, position.z), radius[level], castingPlayer, unitSelector))
            {
                Debug.Log(unit.unitName + " was affected by this ability");
                // Do something to these units, damage for example
            }
        }
    }
}
