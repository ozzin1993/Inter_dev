using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

namespace StrategyCore
{
    public class TransportTakeIn : Ability
    {
        public override AbilityType type { get { return AbilityType.Unit; } } // Specify type

        public override void Use(Unit castingUnit, int castingPlayer, int level, Unit unit)
        {
            if (castingUnit == unit && castingUnit.owner == SlotManager.instance.currentPlayer)
            {
                Presentation.NotifyMsg("Can not choose itself!", castingUnit.owner, false);
                return;
            }

            // Only if not a client
            if (!NetworkConnectionHandler.isClient)
            {
                castingUnit.transportUnit.currentUnitToTake = unit;
                if (!castingUnit.Follow(unit, castingUnit.unitRadius + unit.unitRadius + Utils.stopDistanceOffset))
                {
                    // If still to reach the destination we attach trigger reset
                    castingUnit.OnFollowReach += castingUnit.transportUnit.Embark;
                    castingUnit.OnCommand += castingUnit.transportUnit.TriggerReset;
                }
                else
                {
                    // Already at position
                    castingUnit.transportUnit.Embark();
                }
            }
        }
    }
}

