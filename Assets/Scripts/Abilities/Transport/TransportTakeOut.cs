using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    public class TransportTakeOut : Ability
    {
        public override AbilityType type { get { return AbilityType.Location; } } // Specify type

        public override void Use(Unit castingUnit, int castingPlayer, int level, Vector3 location)
        {
            if (castingUnit.transportUnit.units.Count == 0)
            {
                Presentation.NotifyMsg("No one to disembark!", castingUnit.owner, false);
                return;
            }

            // Only if not a client
            if (!NetworkConnectionHandler.isClient)
            {
                castingUnit.transportUnit.currentLocationToDisembark = location;
                if (!castingUnit.Move(location, castingUnit.transportUnit.units[castingUnit.transportUnit.units.Count - 1].unitRadius + castingUnit.unitRadius + Utils.stopDistanceOffset))
                {
                    // If still to reach the destination we attach trigger reset
                    castingUnit.OnPositionReach += castingUnit.transportUnit.Disembark;
                    castingUnit.OnCommand += castingUnit.transportUnit.TriggerReset;
                }
                else
                {
                    // Already at position
                    castingUnit.transportUnit.Disembark();
                }
            }
        }
    }
}

