using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

namespace StrategyCore
{
    public class Construction : Ability
    {
        // Construction should not have cast time, cast range or be continuous
        public override AbilityType type { get { return AbilityType.Construction; } } // Specify type

        [Header("Ability specific")]
        [Tooltip(" Which unit to build, for each level of the ability")]
        public Unit[] building;

        // Override the cost of the buildings
        public override void Init() // Called in awake method of GameManager. Awake of scriptableObject is broken.
        {
            base.Init(); // Run Ability class Awake function.

            if (building == null) return;

            // We must make sure that unitToTrain.Length equal to the cost.Length. Length meaning the maximum level of the ability.
            cost = new MultiLevel<ResourceWrapper>[building.Length];

            // If unit we are about to train requires limited resource(Food) we must add that resource to the cost of this ability to make sure we are not going to exceed the limit
            for (int l = 0; l < building.Length; l++)
            {
                if (building[l].resourceCost != null)
                {
                    cost[l] = new MultiLevel<ResourceWrapper>();
                    cost[l].data = new ResourceWrapper[building[l].resourceCost.Length];

                    for (int i = 0; i < building[l].resourceCost.Length; i++)
                    {
                        cost[l].data[i] = new ResourceWrapper(building[l].resourceCost[i]);
                    }
                }
            }
        }

        public override void Use(Unit castingUnit, int castingPlayer, int level, Vector3 position)
        {
            // Placement validity is checked on clients, upon receiving command to build should also check on server?
            if (NetworkConnectionHandler.isClient)
            {
                castingUnit.constructionUnit.constructionRef = building[level]; // Set construction building reference on builder
                return;
            }

            // Server
            if (building.Length > level)
            {
                castingUnit.constructionUnit.buildingPosition = position; // Set the position for a new building
                castingUnit.constructionUnit.constructionRef = building[level]; // Set construction building reference on builder
                if (castingUnit.Move(position, building[level].unitRadius + castingUnit.unitRadius + Utils.stopDistanceOffset))
                {
                    // We already reached the destination
                    castingUnit.constructionUnit.ConstructionPositionReached();
                }
                else
                {
                    // Still to reach
                    castingUnit.OnPositionReach += castingUnit.constructionUnit.ConstructionPositionReached; // When reaches the construction site start building
                    castingUnit.OnCommand += castingUnit.constructionUnit.ResetConstructionStates; // if interrupted, stop the construction
                }
            }
            else { Debug.LogWarning("Construction is trying to reach building at level " + level + ", but the building unit is not set"); }
        }
    }
}

