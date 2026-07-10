using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Asset menu
    // [CreateAssetMenu(fileName = "Toggle", menuName = "StrategyCore/Abilities/Toggle")]
    public class Toggle : Ability
    {
        // Level 0 means first level. If you want to damage based on level do not forget to add 1. Example: (damage * (level + 1)
        public override AbilityType type { get { return AbilityType.Toggle; } } // Specify type

        // This function is called when toggle ability is active
        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            // This ability gets units in radius and damages them.

            // Get units in radius
            Unit[] units = Utils.GetUnitsInRadius(new Vector2(castingUnit.transform.position.x, castingUnit.transform.position.z), radius[level], castingUnit.owner, unitSelector);

            for (int i = 0; i < units.Length; i++)
            {
                // Do something with affected units
                // If you want to deal damage check FlameRing.cs
            }
        }

        // This function called once when toggle ability activated
        public override void Activate(Unit castingUnit, int castingPlayer, int level)
        {

        }
        
        // This function called once when toggle ability deactivated
        public override void Deactivate(Unit castingUnit, int castingPlayer, int level)
        {

        }
    }
}
