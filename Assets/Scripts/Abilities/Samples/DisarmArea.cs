using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Asset menu
    public class DisarmArea : Ability
    {
        public override AbilityType type { get { return AbilityType.Area; } } // Specify type

        [Header("Ability specific")]
        public float[] disarmTime;

        public override void Use(Unit castingUnit, int castingPlayer, int level, Vector3 location)
        {
            // Get units in radius
            Unit[] units = Utils.GetUnitsInRadius(new Vector2(location.x, location.z), InterflowAbility.LevelValue(radius, level), castingUnit.owner, unitSelector);

            for (int i = 0; i < units.Length; i++)
            {
                units[i].Disarm(InterflowAbility.LevelValue(disarmTime, level), castingUnit, castingPlayer);
            }
        }
    }
}
