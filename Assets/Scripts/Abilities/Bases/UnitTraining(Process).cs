using System;
using Unity.VisualScripting;
using UnityEngine;

namespace StrategyCore
{
    public class UnitTraining : Ability
    {
        public override AbilityType type { get { return AbilityType.Process; } } // Specify type

        // Before proceeding with the ability, we must ensure several parameters are set correctly
        public override void Init() // Called in awake method of GameManager. Awake of scriptableObject is broken.
        {
            base.Init(); // Run Ability class Awake function.

            if (unitToTrain == null) return;

            // We must make sure that unitToTrain.Length equal to the cost.Length. Length meaning the maximum level of the ability.
            cost = new MultiLevel<ResourceWrapper>[unitToTrain.Length];

            // If unit we are about to train requires limited resource(Food) we must add that resource to the cost of this ability to make sure we are not going to exceed the limit
            for (int l = 0; l < unitToTrain.Length; l++)
            {
                if (unitToTrain[l].resourceCost != null)
                {
                    cost[l] = new MultiLevel<ResourceWrapper>();
                    cost[l].data = new ResourceWrapper[unitToTrain[l].resourceCost.Length];

                    for (int i = 0; i < unitToTrain[l].resourceCost.Length; i++)
                    {
                        if (!costForSingleUnit || unitToTrain[l].resourceCost[i].type.limited)
                        {
                            // Count based cost || limited resource
                            cost[l].data[i] = new ResourceWrapper(unitToTrain[l].resourceCost[i].type, unitToTrain[l].resourceCost[i].value * unitCount[l]);
                        }
                        else
                        {
                            // Single unit cost
                            cost[l].data[i] = new ResourceWrapper(unitToTrain[l].resourceCost[i]);  
                        }
                    }
                }
            }
        }

        // ABILITY STARTS HERE ------------------------------------------------------------------------------------------------------------------------------------------------------------------

        [Header("Ability specific")]
        [Tooltip("Which unit is going to be trained")]
        public Unit[] unitToTrain;
        [Tooltip("How many units should be spawned")]
        public int[] unitCount;
        [Tooltip("Cost of this ability is set automatically by Init(). Should the cost for non-limited resources refelect the unit count of the ability")]
        public bool costForSingleUnit;

        // No Waypoint
        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            UseInternal(castingUnit, level, Vector2.zero, null);
        }

        // Waypoint at location
        public override void Use(Unit castingUnit, int castingPlayer, int level, Vector3 location)
        {
            UseInternal(castingUnit, level, new Vector2(location.x, location.z), null);
        }

        // Waypoint on unit
        public override void Use(Unit castingUnit, int castingPlayer, int level, Unit unit)
        {
            UseInternal(castingUnit, level, Vector2.zero, unit);
        }

        private void UseInternal(Unit castingUnit, int level, Vector2 position, Unit unit)
        {
            // When process ends this function is called
            if (NetworkConnectionHandler.isClient) return;

            // We must spawn a new unit around the castUnit
            for (int i = 0; i < unitCount[level]; i++)
            {
                Unit spawnedUnit = Unit.Spawn(unitToTrain[level], castingUnit.transform.position, 0, castingUnit.owner, castingUnit.unitRadius);

                if (spawnedUnit) spawnedUnit.SetWaypointDirect(unit, position);
            }

            // For every limited resource cost we should retract them, since we added them when we started to process
            if (unitToTrain[level].resourceCost != null)
            {
                for (int i = 0; i < unitToTrain[level].resourceCost.Length; i++)
                {
                    if (unitToTrain[level].resourceCost[i].type.limited)
                    {
                        GameResources.instance.ChangeAmount(castingUnit.owner, unitToTrain[level].resourceCost[i], unitCount[level], false, true); // This will decrease the limited resource usage
                    }
                }
            }
        }
    }
}
