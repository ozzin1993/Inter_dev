using Camera_TopDownNS;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    public static class Utils
    {
        // Might want to adjust based on your levels
        public static int maxVisionRange = 15; // Maximum FoW vision range unit can have
        public static float airUnitElevation = 5f; // How high from the grouond air units must be

        // Defaults
        public static float agentTypeRadius; // Set by GameManager.cs based on navigation agent type radius
        public static float stopDistanceOffset = 0.1f; // Stop distance should be slightly bigger than the radius of two units.
        public static float raycastPointY = 10f; // We do raycasts from this point down, no playable area should be above this point. We do ceratin raycasts from below the ground and go up. This is point below the ground. No playabale area should be below this point.
        public static float maxSlope = 0.5f; // If difference between points more than this value it will be considered non-walkable
        public static int maxCircleChecks = 6; // When dropping an item or training a unit we check for empty place around the cast unit, maximum check distance is defined here.

        public static float minimapPingDuration = 3f; // Duration of ping on the minimap

        public static float coneAngle = 45f; // The half-angle of the cone in degrees for cone-shaped unit return

        public static float levelHeightOffset = 0.1f; // Difference between different elevation levels. Example, if 1 then ground at the height of 1 up to 2 will be second level
                                                      // For positive level height we subtract it, for negative we add it. For example point at 1m, levelHeight height at 1m, we consider the point to be second level since 1m - levelHeightOffset is lower than the point at the map.

        public static float searchRadius = 15; // Radius that will be used to search for storages, collectibles automatically. For collectible multiplier is 0.65.

        // When attacking or casting an ability,
        // if a unit already starts the action we allow it to finish unless the distance is more than = N * this Multiplier
        public static float activeDistanceMultiplier = 1.5f; 

        // Projeectors
        public static float largeSelectorSize = 1.25f; // Any selector bigger than this size will be LargeSelector
        public static float mediumSelectorSize = 0.45f; // Any selector bigger than this size will be MediumSelector

        // Set by GameManager
        public static float airOffsetX; // Set by gameManager in the beginning of the game, offset of air navmesh surface in X axis
        public static float invisibilityOffsetY; // Set by gameManager in the beginning of the game, offset of invisibility navmesh surface in Y axis

        public static int terrainMaskVisuals; // Terrain mask including visual terrain data. Set in Awake of FoW
        public static int terrainMask; // Terrain mask for raycasts. Set in Awake of FoW
        public static int groundMask; // Ground mask for raycasts. Set in Awake of FoW
        public static int waterMask; // WAter mask for raycasts. Set in Awake of FoW

        // For grid pathfinding - Not used in this asset
        public static float pathPointDistance = 0.05f * 0.05f; // We decide if we have reached the pathPoint by checking Squared distance and direction.sqrMagnitude

        // Cache the camera for performance reasons
        public static Camera cachedMainCamera;

        // ============================= UNITS =================================================================

        /// <summary>
        /// Returns how many grid cells given radius covers.
        /// </summary>
        /// <param name="radius">Radius to check.</param>
        /// <returns></returns>
        public static int GetGridRadius(float radius)
        {
            return (int)MathF.Ceiling(radius / Grid.instance.chunkSize);
        }

        /// <summary>
        /// Returns closest unit in radius to the given unit that meet selector requirements. 
        /// </summary>
        /// <param name="unit">Unit that is used for search</param>
        /// <param name="radius">Radius for search</param>
        /// <param name="selector">Selector parameters that define what kind of unit should be returned</param>
        /// <param name="FoWVisible">Optional. If should return units that are visible to the player (Owner of the unit)</param>
        /// <returns></returns>
        public static Unit GetClosestUnit(Unit unit, float radius, UnitSelector selector, bool FoWVisible = false) // By unit
        {
            Unit closestUnit = null;
            float closestUnitDist = 99999;

            // Neighbouring cells
            int gridRadius = GetGridRadius(radius);

            Coordinate newCoord = unit.currentCell;
            newCoord.x -= gridRadius;
            int refY = newCoord.y - gridRadius;

            for (int i = 0; i < gridRadius * 2 + 1; i++)
            {
                newCoord.y = refY;
                if (newCoord.x < 0 || newCoord.x > Grid.chunkCountX - 1)
                {
                    newCoord.x++;
                    continue;
                }

                for (int q = 0; q < gridRadius * 2 + 1; q++)
                {
                    if (newCoord.y < 0 || newCoord.y > Grid.chunkCountY - 1)
                    {
                        newCoord.y++;
                        continue;
                    }

                    // Get current cell index
                    int cellIndex = newCoord.x + newCoord.y * Grid.chunkCountX;

                    // Action
                    foreach (Unit u in Grid.chunkUnits[cellIndex])
                    {
                        if (u == unit) continue;
                        if (FoWVisible && !FogOfWar.instance.IsVisible(u.FoWCell, unit.team)) continue;

                        // Checks - Owned, Enemy or Ally. Unit, building or destructible static. Air unit or ground.
                        if (UnitSelector.IsUnitCompatible(unit.owner, u, selector))
                        {
                            float dist = Vector2.Distance(new Vector2(u.transform.position.x, u.transform.position.z), new Vector2(unit.transform.position.x, unit.transform.position.z)) - u.unitRadius;
                            if (dist < radius)
                            {
                                if (dist < closestUnitDist)
                                {
                                    closestUnit = u;
                                    closestUnitDist = dist;
                                }
                            }
                        }
                    }
                    newCoord.y++;
                }
                newCoord.x++;
            }

            return closestUnit;
        }

        /// <summary>
        /// Returns closest unit in radius from the given position that meet selector requirements. 
        /// </summary>
        /// <param name="position">Position to be used in search</param>
        /// <param name="radius">Radius for search</param>
        /// <param name="selector">Selector parameters that define what kind of unit should be returned</param>
        /// <returns></returns>
        public static Unit GetClosestUnit(Vector2 position, float radius, int playerID, UnitSelector selector, Unit excludeUnit = null) // By position
        {
            Unit closestUnit = null;
            float closestUnitDist = 99999;

            // Neighbouring cells
            int gridRadius = GetGridRadius(radius);

            Coordinate newCoord = Coordinate.GetChunkByPosition(position);
            newCoord.x -= gridRadius;
            int refY = newCoord.y - gridRadius;

            for (int i = 0; i < gridRadius * 2 + 1; i++)
            {
                newCoord.y = refY;
                if (newCoord.x < 0 || newCoord.x > Grid.chunkCountX - 1)
                {
                    newCoord.x++;
                    continue;
                }

                for (int q = 0; q < gridRadius * 2 + 1; q++)
                {
                    if (newCoord.y < 0 || newCoord.y > Grid.chunkCountY - 1)
                    {
                        newCoord.y++;
                        continue;
                    }

                    // Get current cell index
                    int cellIndex = newCoord.x + newCoord.y * Grid.chunkCountX;

                    // Action
                    foreach (Unit u in Grid.chunkUnits[cellIndex])
                    {
                        if (u == excludeUnit) continue;

                        // Checks - Owned, Enemy or Ally. Unit, building or destructible static. Air unit or ground.
                        if (UnitSelector.IsUnitCompatible(playerID, u, selector))
                        {
                            float dist = Vector2.Distance(new Vector2(u.transform.position.x, u.transform.position.z), position) - u.unitRadius;
                            if (dist < radius)
                            {
                                if (dist < closestUnitDist)
                                {
                                    closestUnit = u;
                                    closestUnitDist = dist;
                                }
                            }
                        }
                    }
                    newCoord.y++;
                }
                newCoord.x++;
            }

            return closestUnit;
        }

        /// <summary>
        /// Returns list of all units at the position.
        /// </summary>
        /// <param name="position">Position to check.</param>
        /// <param name="radius">Radius at the position.</param>
        /// <returns></returns>
        public static Unit[] GetAllUnitsInRadius(Vector2 position, float radius)
        {
            List<Unit> units = new List<Unit>();

            // Neighbouring cells
            int gridRadius = GetGridRadius(radius);

            Coordinate newCoord = Coordinate.GetChunkByPosition(position);
            newCoord.x -= gridRadius;
            int refY = newCoord.y - gridRadius;

            for (int i = 0; i < gridRadius * 2 + 1; i++)
            {
                newCoord.y = refY;
                if (newCoord.x < 0 || newCoord.x > Grid.chunkCountX - 1)
                {
                    newCoord.x++;
                    continue;
                }

                for (int q = 0; q < gridRadius * 2 + 1; q++)
                {
                    if (newCoord.y < 0 || newCoord.y > Grid.chunkCountY - 1)
                    {
                        newCoord.y++;
                        continue;
                    }

                    // Get current cell index
                    int cellIndex = newCoord.x + newCoord.y * Grid.chunkCountX;

                    // Action
                    foreach (Unit u in Grid.chunkUnits[cellIndex])
                    {
                        float dist = Vector2.Distance(new Vector2(u.transform.position.x, u.transform.position.z), position);
                        if (dist - u.unitRadius < radius)
                        {
                            units.Add(u);
                        }
                    }
                    newCoord.y++;
                }
                newCoord.x++;
            }

            return units.ToArray();
        }

        /// <summary>
        /// Returns list of all units at the position that are optionally ground, water or air.
        /// </summary>
        /// <param name="position">Position to check.</param>
        /// <param name="radius">Radius at the position.</param>
        /// <param name="ground">Should include ground units.</param>
        /// <param name="water">Should include water units.</param>
        /// <param name="air">Should include air units.</param>
        /// <returns></returns>
        public static Unit[] GetAllUnitsInRadius(Vector2 position, float radius, bool ground, bool water, bool air)
        {
            List<Unit> units = new List<Unit>();

            // Neighbouring cells
            int gridRadius = GetGridRadius(radius);

            Coordinate newCoord = Coordinate.GetChunkByPosition(position);
            newCoord.x -= gridRadius;
            int refY = newCoord.y - gridRadius;

            for (int i = 0; i < gridRadius * 2 + 1; i++)
            {
                newCoord.y = refY;
                if (newCoord.x < 0 || newCoord.x > Grid.chunkCountX - 1)
                {
                    newCoord.x++;
                    continue;
                }

                for (int q = 0; q < gridRadius * 2 + 1; q++)
                {
                    if (newCoord.y < 0 || newCoord.y > Grid.chunkCountY - 1)
                    {
                        newCoord.y++;
                        continue;
                    }

                    // Get current cell index
                    int cellIndex = newCoord.x + newCoord.y * Grid.chunkCountX;

                    // Action
                    foreach (Unit u in Grid.chunkUnits[cellIndex])
                    {
                        if ((ground && u.isGround) || (water && u.isWater) || (air && u.isAir))
                        {
                            float dist = Vector2.Distance(new Vector2(u.transform.position.x, u.transform.position.z), position);
                            if (dist - u.unitRadius < radius)
                            {
                                units.Add(u);
                            }
                        }
                    }
                    newCoord.y++;
                }
                newCoord.x++;
            }

            return units.ToArray();
        }

        // Returns resource unit that meets criteria.
        // resourceType - what kind of resource type we looking for
        // type - what kind of resource unit we looking for
        //        0 - storage
        //        1 - collectible. Collectible ownership check is not implemented, implied the collectibles are units that are neutral and do not belong to anyone.
        //        2 - collector
        // We check the distance against distancePosition

        public static Unit GetClosestResourceUnit(int playerID, Vector2 distancePosition, Vector2 position, float radius, Resource resourceType, int unitType, Unit excludeUnit = null) // By position
        {
            Unit closestUnit = null;
            float closestUnitDist = 99999;

            // Neighbouring cells
            int gridRadius = GetGridRadius(radius);

            Coordinate newCoord = Coordinate.GetChunkByPosition(position);
            newCoord.x -= gridRadius;
            int refY = newCoord.y - gridRadius;

            for (int i = 0; i < gridRadius * 2 + 1; i++)
            {
                newCoord.y = refY;
                if (newCoord.x < 0 || newCoord.x > Grid.chunkCountX - 1)
                {
                    newCoord.x++;
                    continue;
                }

                for (int q = 0; q < gridRadius * 2 + 1; q++)
                {
                    if (newCoord.y < 0 || newCoord.y > Grid.chunkCountY - 1)
                    {
                        newCoord.y++;
                        continue;
                    }

                    // Get current cell index
                    int cellIndex = newCoord.x + newCoord.y * Grid.chunkCountX;

                    // Action
                    foreach (Unit u in Grid.chunkUnits[cellIndex])
                    {
                        if (u == excludeUnit) continue;

                        // Check if unit is being constructed. If unit is being upgraded it will pass this check
                        if (u.constructionUnit && !u.constructionUnit.completed) continue;

                        if (u.resourceUnit)
                        {
                            int resourceIndex = -1;

                            if (unitType == 0) resourceIndex = 0;
                            else if (unitType == 1) resourceIndex = ResourceUnit.GetResourceIndex(resourceType, u.resourceUnit.collectibleResources);
                            else resourceIndex = ResourceUnit.GetResourceIndex(resourceType, u.resourceUnit.collectorResources);

                            if (resourceIndex != -1 && (
                                (unitType == 0 && u.resourceUnit.isStorage && (u.owner == playerID || u.owner == (int)Players.NeutralPassive)) // If found unit isStorage and belongs to player or neutral passive
                                || (unitType == 1 && u.resourceUnit.isCollectible && u.resourceUnit.collectibleResources[resourceIndex].value > 0) // If found unit isCollectible and has resources left
                                || (unitType == 2 && u.resourceUnit.isCollector))) // If found unit any collector
                            {
                                float dist = Vector2.Distance(new Vector2(u.transform.position.x, u.transform.position.z), distancePosition) - u.unitRadius;
                                if (dist < radius)
                                {
                                    if (dist < closestUnitDist)
                                    {
                                        closestUnit = u;
                                        closestUnitDist = dist;
                                    }
                                }
                            }
                        }
                    }
                    newCoord.y++;
                }
                newCoord.x++;
            }

            return closestUnit;
        }

        /// <summary>
        /// Return units in radius that meet selector parameters.
        /// </summary>
        /// <param name="position">Position to be used in search</param>
        /// <param name="radius">Radius from the position</param>
        /// <param name="playerID">Player that is making the call to get units in radius</param>
        /// <param name="selector">Selector parameters that define what kind of units should be returned</param>
        /// <param name="unitCount">Optional parameter that dictates how many units should be returned</param>
        /// <param name="mustIncludeExcludeUnit">Optional parameter if excludeUnit must be in the returned Unit[] array, used for multitarget</param>
        public static Unit[] GetUnitsInRadius(Vector2 position, float radius, int playerID, UnitSelector selector, int unitCount = -1, Unit excludeUnit = null, bool mustIncludeExcludeUnit = false) // By position
        {
            List<Unit> units = new List<Unit>();
            if (mustIncludeExcludeUnit) units.Add(excludeUnit);

            // Neighbouring cells
            int gridRadius = GetGridRadius(radius);

            Coordinate newCoord = Coordinate.GetChunkByPosition(position);
            newCoord.x -= gridRadius;
            int refY = newCoord.y - gridRadius;

            for (int i = 0; i < gridRadius * 2 + 1; i++)
            {
                newCoord.y = refY;
                if (newCoord.x < 0 || newCoord.x > Grid.chunkCountX - 1)
                {
                    newCoord.x++;
                    continue;
                }

                for (int q = 0; q < gridRadius * 2 + 1; q++)
                {
                    if (newCoord.y < 0 || newCoord.y > Grid.chunkCountY - 1)
                    {
                        newCoord.y++;
                        continue;
                    }

                    // Get current cell index
                    int cellIndex = newCoord.x + newCoord.y * Grid.chunkCountX;

                    // Action
                    foreach (Unit u in Grid.chunkUnits[cellIndex])
                    {
                        if (u == excludeUnit) continue;

                        // Checks - Owned, Enemy or Ally. Unit, building or destructible static. Air unit or ground.
                        if (UnitSelector.IsUnitCompatible(playerID, u, selector))
                        {
                            float dist = Vector2.Distance(new Vector2(u.transform.position.x, u.transform.position.z), position);
                            if (dist - u.unitRadius < radius)
                            {
                                if (unitCount != -1)
                                {
                                    units.Add(u);

                                    if (units.Count >= unitCount) return units.ToArray();
                                }
                                else
                                {
                                    units.Add(u);
                                }
                            }
                        }
                    }
                    newCoord.y++;
                }
                newCoord.x++;
            }

            return units.ToArray();
        }

        /// <summary>
        /// Returns closest units to the position based on radius. Returned array may have null entries.
        /// </summary>
        /// <param name="position">Position to be used in search.</param>
        /// <param name="radius">Radius from the position.</param>
        /// <param name="playerID">Player that is making the call.</param>
        /// <param name="selector">Selector parameters that define what kind of units should be returned.</param>
        /// <param name="unitCount">How many units should be returned.</param>
        /// <param name="excludeUnits">What units should be excluded.</param>
        public static Unit[] GetClosestUnitsInRadius(Vector2 position, float radius, int playerID, UnitSelector selector, int unitCount, Unit[] excludeUnits = null)
        {
            Unit[] units = new Unit[unitCount];
            float[] distances = new float[unitCount];;

            // Neighbouring cells
            int gridRadius = GetGridRadius(radius);

            Coordinate newCoord = Coordinate.GetChunkByPosition(position);
            newCoord.x -= gridRadius;
            int refY = newCoord.y - gridRadius;

            for (int i = 0; i < gridRadius * 2 + 1; i++)
            {
                newCoord.y = refY;
                if (newCoord.x < 0 || newCoord.x > Grid.chunkCountX - 1)
                {
                    newCoord.x++;
                    continue;
                }

                for (int q = 0; q < gridRadius * 2 + 1; q++)
                {
                    if (newCoord.y < 0 || newCoord.y > Grid.chunkCountY - 1)
                    {
                        newCoord.y++;
                        continue;
                    }

                    // Get current cell index
                    int cellIndex = newCoord.x + newCoord.y * Grid.chunkCountX;

                    // Action
                    foreach (Unit u in Grid.chunkUnits[cellIndex])
                    {
                        if (excludeUnits != null)
                        {
                            bool excluded = false;
                            for (int z = 0; z < excludeUnits.Length; z++)
                            {
                                if (u == excludeUnits[z])
                                {
                                    excluded = true;
                                    break;
                                }
                            }
                            if (excluded) continue;
                        }

                        // Checks - Owned, Enemy or Ally. Unit, building or destructible static. Air unit or ground.
                        if (UnitSelector.IsUnitCompatible(playerID, u, selector))
                        {
                            float dist = Vector2.Distance(new Vector2(u.transform.position.x, u.transform.position.z), position);
                            if (dist - u.unitRadius < radius)
                            {
                                // Find insertion point
                                int insertIndex = unitCount - 1;
                                for (int z = unitCount - 2; z >= 0; z--) // Start from second-to-last
                                {
                                    if (units[z] != null && distances[z] > dist)
                                    {
                                        insertIndex = z; // Found where dist fits
                                    }
                                    else
                                    {
                                        break; // Stop if we find a smaller distance
                                    }
                                }

                                // Shift elements if needed
                                if (units[insertIndex] != null)
                                {
                                    for (int o = unitCount - 1; o > insertIndex; o--)
                                    {
                                        units[o] = units[o - 1];
                                        distances[o] = distances[o - 1];
                                    }
                                }

                                // Insert new unit
                                units[insertIndex] = u;
                                distances[insertIndex] = dist;
                            }
                        }
                    }
                    newCoord.y++;
                }
                newCoord.x++;
            }

            return units;
        }

        /// <summary>
        /// Returns shop unit around the position at a GameManager.instance.shopRadius.
        /// </summary>
        /// <param name="position">Position to check.</param>
        /// <returns></returns>
        public static Unit GetNearShop(Vector2 position) // By position
        {
            float radius = GameManager.instance.shopRadius;

            // Neighbouring cells
            int gridRadius = GetGridRadius(radius);

            Coordinate newCoord = Coordinate.GetChunkByPosition(position);
            newCoord.x -= gridRadius;
            int refY = newCoord.y - gridRadius;

            for (int i = 0; i < gridRadius * 2 + 1; i++)
            {
                newCoord.y = refY;
                if (newCoord.x < 0 || newCoord.x > Grid.chunkCountX - 1)
                {
                    newCoord.x++;
                    continue;
                }

                for (int q = 0; q < gridRadius * 2 + 1; q++)
                {
                    if (newCoord.y < 0 || newCoord.y > Grid.chunkCountY - 1)
                    {
                        newCoord.y++;
                        continue;
                    }

                    // Get current cell index
                    int cellIndex = newCoord.x + newCoord.y * Grid.chunkCountX;

                    // Action
                    foreach (Unit u in Grid.chunkUnits[cellIndex])
                    {
                        // Checks - Owned, Enemy or Ally. Unit, building or destructible static. Air unit or ground.
                        if (u.isShop)
                        {
                            float dist = Vector2.Distance(new Vector2(u.transform.position.x, u.transform.position.z), position);
                            if (dist - u.unitRadius < radius)
                            {
                                return u;
                            }
                        }
                    }
                    newCoord.y++;
                }
                newCoord.x++;
            }

            return null;
        }

        /// <summary>
        /// Returns the unit that is capable of shopping.
        /// </summary>
        /// <param name="playerID">Units of this player will be returned.</param>
        /// <param name="unit">Shop unit.</param>
        /// <returns></returns>
        public static Unit GetClosestShoppingUnit(int playerID, Unit unit) // By Unit. Self excluded
        {
            float radius = GameManager.instance.shopRadius;
            Vector2 position = new Vector2(unit.transform.position.x, unit.transform.position.z);

            Unit closestUnit = null;
            float closestUnitDist = 99999;

            // Neighbouring cells
            int gridRadius = GetGridRadius(radius);

            Coordinate newCoord = Coordinate.GetChunkByPosition(position);
            newCoord.x -= gridRadius;
            int refY = newCoord.y - gridRadius;

            for (int i = 0; i < gridRadius * 2 + 1; i++)
            {
                newCoord.y = refY;
                if (newCoord.x < 0 || newCoord.x > Grid.chunkCountX - 1)
                {
                    newCoord.x++;
                    continue;
                }

                for (int q = 0; q < gridRadius * 2 + 1; q++)
                {
                    if (newCoord.y < 0 || newCoord.y > Grid.chunkCountY - 1)
                    {
                        newCoord.y++;
                        continue;
                    }

                    // Get current cell index
                    int cellIndex = newCoord.x + newCoord.y * Grid.chunkCountX;

                    // Action
                    foreach (Unit u in Grid.chunkUnits[cellIndex])
                    {
                        // Check if unit is being constructed. If unit is being upgraded it will pass this check
                        if (u.constructionUnit && !u.constructionUnit.completed) continue;

                        if (u.owner == playerID && u.InventorySize > 0 && unit != u)
                        {
                            float dist = Vector2.Distance(new Vector2(u.transform.position.x, u.transform.position.z), position) - u.unitRadius;
                            if (dist < radius)
                            {
                                if (dist < closestUnitDist)
                                {
                                    closestUnit = u;
                                    closestUnitDist = dist;
                                }
                            }
                        }
                    }
                    newCoord.y++;
                }
                newCoord.x++;
            }

            return closestUnit;
        }

        // Returns unit that has given position inside its radius
        // ignoreAir - ignores air units
        public static Unit GetIntersectedUnit(Vector3 position, float radius, Unit exludeUnit = null, bool ignoreAir = false)
        {
            return GetIntersectedUnit(new Vector2(position.x, position.z), radius, exludeUnit, ignoreAir);
        }

        public static Unit GetIntersectedUnit(Vector2 position, float radius, Unit exludeUnit = null, bool ignoreAir = false)
        {
            // Neighbouring cells
            int gridRadius = GetGridRadius(radius);

            Coordinate newCoord = Coordinate.GetChunkByPosition(position);
            newCoord.x -= gridRadius;
            int refY = newCoord.y - gridRadius;

            for (int i = 0; i < gridRadius * 2 + 1; i++)
            {
                newCoord.y = refY;
                if (newCoord.x < 0 || newCoord.x > Grid.chunkCountX - 1)
                {
                    newCoord.x++;
                    continue;
                }

                for (int q = 0; q < gridRadius * 2 + 1; q++)
                {
                    if (newCoord.y < 0 || newCoord.y > Grid.chunkCountY - 1)
                    {
                        newCoord.y++;
                        continue;
                    }

                    // Get current cell index
                    int cellIndex = newCoord.x + newCoord.y * Grid.chunkCountX;

                    // Action
                    foreach (Unit u in Grid.chunkUnits[cellIndex])
                    {
                        if (ignoreAir && u.isAir) continue;
                        if (u == exludeUnit) continue;

                        float dist = Vector2.Distance(new Vector2(u.transform.position.x, u.transform.position.z), position);
                        if (dist - u.unitRadius < radius)
                        {
                            return u;
                        }
                    }
                    newCoord.y++;
                }
                newCoord.x++;
            }

            return null;
        }

        // Returns units that fit the unitSelector in a cone shaped area with specified paramters
        // Position - origin of the cone
        // coneDirection - direction of the cone
        // angle - The half-angle of the cone in degrees
        // radius - maximum range of the cone
        // Sample usage - GetUnitsInCone(coneOrigin, coneDirection, coneAngle, coneRange, etc...))
        public static Unit[] GetUnitsInCone(Vector2 position, Vector2 coneDirection, float angle, float radius, int playerID, UnitSelector selector, int unitCount = -1, Unit excludeUnit = null, bool mustIncludeExcludeUnit = false) // By position
        {
            List<Unit> units = new List<Unit>();
            if (mustIncludeExcludeUnit) units.Add(excludeUnit);

            // Neighbouring cells
            int gridRadius = GetGridRadius(radius);

            Coordinate newCoord = Coordinate.GetChunkByPosition(position);
            newCoord.x -= gridRadius;
            int refY = newCoord.y - gridRadius;

            for (int i = 0; i < gridRadius * 2 + 1; i++)
            {
                newCoord.y = refY;
                if (newCoord.x < 0 || newCoord.x > Grid.chunkCountX - 1)
                {
                    newCoord.x++;
                    continue;
                }

                for (int q = 0; q < gridRadius * 2 + 1; q++)
                {
                    if (newCoord.y < 0 || newCoord.y > Grid.chunkCountY - 1)
                    {
                        newCoord.y++;
                        continue;
                    }

                    // Get current cell index
                    int cellIndex = newCoord.x + newCoord.y * Grid.chunkCountX;

                    // Action
                    foreach (Unit u in Grid.chunkUnits[cellIndex])
                    {
                        if (u == excludeUnit) continue;

                        // Checks - Owned, Enemy or Ally. Unit, building or destructible static. Air unit or ground.
                        if (UnitSelector.IsUnitCompatible(playerID, u, selector))
                        {
                            // Check if unit is in the cone

                            // Calculate the direction from the cone's origin to the target
                            Vector2 directionToTarget = new Vector2(u.transform.position.x, u.transform.position.z) - position;

                            // Check the distance to the target
                            float distanceToTarget = directionToTarget.magnitude;
                            if (distanceToTarget - u.unitRadius > radius)
                            {
                                continue; // Target is out of range
                            }

                            // Normalize the direction vectors
                            directionToTarget.Normalize();
                            coneDirection.Normalize();

                            // Calculate the angle between the cone's forward direction and the direction to the target
                            float dotProduct = Vector2.Dot(coneDirection, directionToTarget);
                            float angleToTarget = Mathf.Acos(dotProduct) * Mathf.Rad2Deg; // Convert from radians to degrees

                            // Check if the angle to the target is within the cone's half-angle
                            if (angleToTarget <= angle)
                            {
                                if (unitCount != -1)
                                {
                                    units.Add(u);

                                    if (units.Count >= unitCount) return units.ToArray();
                                }
                                else
                                {
                                    units.Add(u);
                                }
                            }
                        }
                    }
                    newCoord.y++;
                }
                newCoord.x++;
            }

            return units.ToArray();
        }

        // ============================= UNIT COMPONENTS =================================================================

        /// <summary>
        /// Copies the properties of a <see cref="NavMeshObstacle"/> component to another object.
        /// </summary>
        /// <param name="obj">The target transform to which the obstacle component will be added.</param>
        /// <param name="refObstacle">The reference <see cref="NavMeshObstacle"/> component whose properties will be copied.</param>
        public static void CopyObstacleComponent(Transform obj, NavMeshObstacle refObstacle)
        {
            NavMeshObstacle obstacle = obj.gameObject.AddComponent<NavMeshObstacle>();

            obstacle.shape = refObstacle.shape;
            obstacle.center = refObstacle.center;
            obstacle.size = refObstacle.size;
            obstacle.radius = refObstacle.radius;
            obstacle.height = refObstacle.height;

            obstacle.carving = refObstacle.carving;
            obstacle.carvingMoveThreshold = refObstacle.carvingMoveThreshold;
            obstacle.carvingTimeToStationary = refObstacle.carvingTimeToStationary;
            obstacle.carveOnlyStationary = refObstacle.carveOnlyStationary;
        }

        /// <summary>
        /// Removes all game-related components from a unit, including attributes, leveling, navigation, and network components.
        /// </summary>
        /// <param name="unit">The <see cref="Unit"/> from which components will be removed.</param>
        public static void UnitRemoveComponents(Unit unit)
        {
            if (unit.GetComponent<AttributeUnit>()) GameManager.Destroy(unit.GetComponent<AttributeUnit>());
            if (unit.GetComponent<ConstructionUnit>()) GameManager.Destroy(unit.GetComponent<ConstructionUnit>());
            if (unit.GetComponent<LevelingUnit>()) GameManager.Destroy(unit.GetComponent<LevelingUnit>());
            if (unit.GetComponent<LifetimeUnit>()) GameManager.Destroy(unit.GetComponent<LifetimeUnit>());
            if (unit.GetComponent<ResourceUnit>()) GameManager.Destroy(unit.GetComponent<ResourceUnit>());
            if (unit.GetComponent<TransportUnit>()) GameManager.Destroy(unit.GetComponent<TransportUnit>());
            if (unit.GetComponent<NavMeshAgent>()) GameManager.Destroy(unit.GetComponent<NavMeshAgent>());
            if (unit.GetComponent<NavMeshObstacle>()) GameManager.Destroy(unit.GetComponent<NavMeshObstacle>());
            if (unit.GetComponent<NetworkObject>()) GameManager.Destroy(unit.GetComponent<NetworkObject>());
            // [Interflow] свои поведенческие компоненты — снять вместе с Unit (апгрейд/трансформация/статик-копия)
            if (unit.GetComponent<AutoAbilityUser>()) GameManager.Destroy(unit.GetComponent<AutoAbilityUser>());
            if (unit.GetComponent<FlameCloakBuff>()) GameManager.Destroy(unit.GetComponent<FlameCloakBuff>());
            if (unit.GetComponent<Unit>()) GameManager.Destroy(unit.GetComponent<Unit>());

            foreach (Transform child in unit.transform)
            {
                if (child.name == "MiniMapIcon(Clone)" || child.name == "HealthBar(Clone)" || child.name == "VFXHolder") GameManager.Destroy(child.gameObject);
            }
        }

        // ============================= TRANSFORM =================================================================

        /// <summary>
        /// Determines the direction of rotation between two quaternions.
        /// Returns true if rotating right or no rotation is needed.
        /// </summary>
        /// <param name="currentRotation">The current rotation.</param>
        /// <param name="targetRotation">The target rotation.</param>
        /// <returns>True if rotation is right or no rotation needed, false if rotation is left.</returns>
        public static bool GetRotationDirection(Quaternion currentRotation, Quaternion targetRotation)
        {
            // Convert rotations to Euler angles for comparison
            Vector3 currentEuler = currentRotation.eulerAngles;
            Vector3 targetEuler = targetRotation.eulerAngles;

            // Calculate the shortest angle difference for each axis
            float angleDifference = Mathf.DeltaAngle(currentEuler.y, targetEuler.y); // Y-axis for horizontal rotation

            if (angleDifference > 0) return false; // Positive difference means rotating left (counterclockwise)
            else return true;

            // else if (angleDifference < 0)
            //     return "Right"; // Negative difference means rotating right (clockwise)
            // else
            //     return "None"; // No rotation (already aligned)
        }

        /// <summary>
        /// Rotates from the current rotation towards the target rotation with a specified step size.
        /// Allows manually controlling whether the rotation goes clockwise or counterclockwise.
        /// </summary>
        /// <param name="current">The current rotation.</param>
        /// <param name="target">The target rotation.</param>
        /// <param name="step">The step size to rotate by.</param>
        /// <param name="rotateRight">If true, rotates clockwise; otherwise, rotates counterclockwise.</param>
        /// <returns>The new rotation after applying the step.</returns>
        public static Quaternion RotateTowardsWithDirection(Quaternion current, Quaternion target, float step, bool rotateRight)
        {
            // Calculate the shortest angle between the current and target rotations
            float angle = Quaternion.Angle(current, target);

            if (angle <= 0.001f) // Close enough to target
                return target;

            // Convert the quaternions to Euler angles to determine direction
            Vector3 currentEuler = current.eulerAngles;
            Vector3 targetEuler = target.eulerAngles;

            // Normalize angles to [0, 360)
            currentEuler = NormalizeEulerAngles(currentEuler);
            targetEuler = NormalizeEulerAngles(targetEuler);

            // Choose direction (manual control instead of shortest path)
            Vector3 newEuler;
            if (rotateRight)
            {
                newEuler = new Vector3(
                    Mathf.MoveTowardsAngle(currentEuler.x, targetEuler.x, step),
                    Mathf.MoveTowardsAngle(currentEuler.y, targetEuler.y + (currentEuler.y > targetEuler.y ? 360 : 0), step),
                    Mathf.MoveTowardsAngle(currentEuler.z, targetEuler.z, step)
                );
            }
            else
            {
                newEuler = new Vector3(
                    Mathf.MoveTowardsAngle(currentEuler.x, targetEuler.x, step),
                    Mathf.MoveTowardsAngle(currentEuler.y, targetEuler.y - (currentEuler.y < targetEuler.y ? 360 : 0), step),
                    Mathf.MoveTowardsAngle(currentEuler.z, targetEuler.z, step)
                );
            }

            return Quaternion.Euler(newEuler);
        }

        /// <summary>
        /// Normalizes Euler angles to the range [0, 360).
        /// </summary>
        /// <param name="euler">Euler angles to normalize.</param>
        /// <returns>Normalized Euler angles.</returns>
        public static Vector3 NormalizeEulerAngles(Vector3 euler)
        {
            euler.x = euler.x % 360;
            if (euler.x < 0) euler.x += 360;

            euler.y = euler.y % 360;
            if (euler.y < 0) euler.y += 360;

            euler.z = euler.z % 360;
            if (euler.z < 0) euler.z += 360;

            return euler;
        }

        /// <summary>
        /// Moves a point towards a target position at a given speed.
        /// Ensures the movement does not overshoot the target.
        /// </summary>
        /// <param name="initialPosition">The starting position.</param>
        /// <param name="targetPosition">The target position.</param>
        /// <param name="speed">The movement speed per update.</param>
        /// <returns>The new position after movement.</returns>
        public static Vector2 PointTowards(Vector2 initialPosition, Vector2 targetPosition, float speed)
        {
            // Calculate the direction to the target
            Vector2 direction = targetPosition - initialPosition;

            // Normalize the direction to ensure consistent movement speed
            Vector2 normalizedDirection = direction.normalized;

            // Calculate the movement step
            Vector2 step = normalizedDirection * speed;

            // Ensure we don't overshoot the target
            if (step.magnitude > direction.magnitude)
            {
                return targetPosition; // Snap to the target
            }
            else
            {
                // Move the object
                return initialPosition + step;
            }
        }

        /// <summary>
        /// Checks if an observer is looking directly at a target.
        /// Returns the dot product between the observer's forward direction and the direction to the target.
        /// Closer to 1 means direct alignment, closer to -1 means opposite direction.
        /// </summary>
        /// <param name="observer">The transform of the observing object.</param>
        /// <param name="target">The world position of the target.</param>
        /// <returns>Dot product indicating alignment (1 = fully aligned, -1 = opposite direction).</returns>
        public static float IsLookingAtTarget(Transform observer, Vector3 target)
        {
            // Direction from the observer to the target
            Vector3 directionToTarget = (target - observer.position).normalized;

            // Calculate the dot product
            float dotProduct = Vector3.Dot(observer.forward, directionToTarget);

            // Check if the dot product is greater than or equal to the threshold
            return dotProduct;
        }

        /// <summary>
        /// Checks if a child object, taking into account the parent's rotation, is looking at a target.
        /// Returns the dot product between the child's adjusted forward direction and the direction to the target.
        /// </summary>
        /// <param name="parent">The transform of the parent object.</param>
        /// <param name="child">The transform of the child object.</param>
        /// <param name="target">The world position of the target.</param>
        /// <returns>Dot product indicating alignment (1 = fully aligned, -1 = opposite direction).</returns>
        public static float IsChildLookingAtTarget(Transform parent, Transform child, Vector3 target)
        {
            // Calculate the child's "true forward" direction
            Quaternion combinedRotation = parent.rotation * child.localRotation;
            Vector3 childTrueForward = combinedRotation * Vector3.forward;

            // Direction from the child to the target
            Vector3 directionToTarget = (target - child.position).normalized;

            // Calculate the dot product
            float dotProduct = Vector3.Dot(childTrueForward, directionToTarget);

            return dotProduct;
        }

        // ============================= REWARDS =================================================================

        /// <summary>
        /// This function is called when a unit dies, it handles the rewards logic.
        /// </summary>
        /// <param name="killedUnit">Unit that is killed.</param>
        /// <param name="playerThatKills">Player that killed the unit.</param>
        /// <param name="unitThatKills">The unit that performed the kill, can be null.</param>
        public static void HandleRewardGain(Unit killedUnit, int playerThatKills, Unit unitThatKills)
        {
            if (unitThatKills) HandleRewardGain(killedUnit, unitThatKills);
            else
            {
                SpreadRewardXP(killedUnit);
                if (playerThatKills != -1) HandleRewardGain(killedUnit, playerThatKills);
            }
        }

        /// <summary>
        /// Handles the reward distribution when Unit that performs the kill is defined.
        /// </summary>
        /// <param name="killedUnit">Unit that is killed.</param>
        /// <param name="unitThatKills">The unit that performed the kill.</param>
        private static void HandleRewardGain(Unit killedUnit, Unit unitThatKills)
        {
            // Spread rewards
            int spreadXP = SpreadRewardXP(killedUnit, unitThatKills);

            if (unitThatKills != null)
            {
                float killRewardFactor = (killedUnit.team == unitThatKills.team) ? GameManager.instance.allyKillReward : 1;

                // Reward the killing unit
                if (unitThatKills.levelingUnit)
                {
                    unitThatKills.levelingUnit.ChangeExp((int)(spreadXP * killRewardFactor));
                }

                // Resource reward. Only the one that kills gets the resources
                for (int i = 0; i < killedUnit.resourceReward.Length; i++)
                {
                    GameResources.instance.ChangeAmount(unitThatKills.owner, killedUnit.resourceReward[i], killRewardFactor, false, true);
                }
            }
        }

        /// <summary>
        /// Handles the reward distribution when only the player that performs the kill is defined.
        /// </summary>
        /// <param name="killedUnit">Unit that is killed.</param>
        /// <param name="playerID">The player that performed the kill.</param>
        private static void HandleRewardGain(Unit killedUnit, int playerID)
        {
            // Resource reward. Only the one that kills gets the resources
            float killRewardFactor = (killedUnit.team == SlotManager.instance.playerTeam[playerID]) ? GameManager.instance.allyKillReward : 1;
            for (int i = 0; i < killedUnit.resourceReward.Length; i++)
            {
                GameResources.instance.ChangeAmount(playerID, killedUnit.resourceReward[i], killRewardFactor, false, true);
            }
        }

        /// <summary>
        /// Handles the spread XP reward distribution
        /// </summary>
        /// <param name="killedUnit">Unit that is killed.</param>
        /// <param name="excludeUnit">Unit that should be excluded from XP distribution.</param>
        /// <returns>The amount of xp that has been rewarded.</returns>
        private static int SpreadRewardXP(Unit killedUnit, Unit excludeUnit = null)
        {
            int xp = killedUnit.xpReward;
            if (GameManager.instance.spreadEXP)
            {
                // We obtan all units in radius of killedUnit that are enemies 
                UnitSelector enemySelector = new UnitSelector(false, false, true, true, true, true, true, true, true, true, true, true);
                Unit[] enemyUnits = Utils.GetUnitsInRadius(new Vector2(killedUnit.transform.position.x, killedUnit.transform.position.z), GameManager.instance.spreadRange, killedUnit.owner, enemySelector, -1, killedUnit);

                if (GameManager.instance.equalEXP)
                {
                    // Equal XP
                    for (int i = 0; i < enemyUnits.Length; i++)
                    {
                        if (enemyUnits[i].levelingUnit && enemyUnits[i] != excludeUnit)
                        {
                            enemyUnits[i].levelingUnit.ChangeExp(xp);
                        }
                    }
                }
                else
                {
                    // Divided XP - we should divide XP only between units that can gain XP, rest will be ignored

                    // Get units that can level
                    List<Unit> levelableUnits = new List<Unit>();
                    for (int i = 0; i < enemyUnits.Length; i++)
                    {
                        // We check if this unit can gain XP
                        if (enemyUnits[i].levelingUnit && enemyUnits[i] != excludeUnit) levelableUnits.Add(enemyUnits[i]);
                    }

                    if (levelableUnits.Count > 0)
                    {
                        xp = (excludeUnit != null && excludeUnit.levelingUnit) ? killedUnit.xpReward / (levelableUnits.Count + 1) : killedUnit.xpReward / levelableUnits.Count;
                        for (int i = 0; i < levelableUnits.Count; i++)
                        {
                            levelableUnits[i].levelingUnit.ChangeExp(xp);
                        }
                    }
                }
            }
            return xp;
        }

        // ============================= SELECTION RECT =================================================================

        private static Texture2D _whiteTexture; // Selection rectangle fill texture

        /// <summary>
        /// Returns the selection rectangle fill texture, creates one if does not exist.
        /// </summary>
        private static Texture2D WhiteTexture
        {
            get
            {
                if (_whiteTexture == null)
                {
                    _whiteTexture = new Texture2D(1, 1);
                    _whiteTexture.SetPixel(0, 0, Color.white);
                    _whiteTexture.Apply();
                }

                return _whiteTexture;
            }
        }

        /// <summary>
        /// Draws the selection rectangle`s fill texture.
        /// </summary>
        /// <param name="rect">Dimensions of the selection rectangle.</param>
        /// <param name="color">Fill color.</param>
        public static void DrawScreenRect(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, WhiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>
        /// Draws the selection rectangle`s borders.
        /// </summary>
        /// <param name="rect">Dimensions of the selection rectangle.</param>
        /// <param name="thickness">Border thickness.</param>
        /// <param name="color">Border color.</param>
        public static void DrawScreenRectBorder(Rect rect, float thickness, Color color)
        {
            // Top
            Utils.DrawScreenRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
            // Left
            Utils.DrawScreenRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
            // Right
            Utils.DrawScreenRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
            // Bottom
            Utils.DrawScreenRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
        }

        /// <summary>
        /// Returns the rectangle from two screen positions.
        /// </summary>
        /// <param name="screenPosition1">First screen position.</param>
        /// <param name="screenPosition2">Second screen position.</param>
        /// <returns></returns>
        public static Rect GetScreenRect(Vector2 screenPosition1, Vector2 screenPosition2)
        {
            // Move origin from bottom left to top left
            screenPosition1.y = Screen.height - screenPosition1.y;
            screenPosition2.y = Screen.height - screenPosition2.y;
            // Calculate corners
            var topLeft = Vector2.Min(screenPosition1, screenPosition2);
            var bottomRight = Vector2.Max(screenPosition1, screenPosition2);
            // Create Rect
            return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
        }

        public static Bounds GetViewportBounds(Camera camera, Vector3 screenPosition1, Vector3 screenPosition2)
        {
            var v1 = Camera.main.ScreenToViewportPoint(screenPosition1);
            var v2 = Camera.main.ScreenToViewportPoint(screenPosition2);
            var min = Vector3.Min(v1, v2);
            var max = Vector3.Max(v1, v2);
            min.z = camera.nearClipPlane;
            max.z = camera.farClipPlane;

            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        public static Bounds GetSelectionBounds(Vector3 dragStart, Vector3 dragCurrent)
        {
            Vector3 min = Vector3.Min(dragStart, dragCurrent);
            Vector3 max = Vector3.Max(dragStart, dragCurrent);
            min.z = Camera.main.nearClipPlane;
            max.z = Camera.main.farClipPlane;
            Bounds selectionBounds = new Bounds();
            selectionBounds.SetMinMax(min, max);
            return selectionBounds;
        }

        // When selection is over, pyramid shape is created and all units checked if any inside of the pyramid shape. Pyramid shape represents frustum view, this is only for 3D. 
        // For 2D, orthoghonal camera, simple box usage is okay
        // Camera position is point A, floor plane corners are in clockwise order
        public static bool PyramidCheck(Vector3 pointPosition, Vector3[] corners)
        {
            Vector3 pointA = cachedMainCamera.transform.position;

            for (int i = 0; i < 4; i++)
            {
                Vector3 planeNormal;
                if (i == 3)
                {
                    planeNormal = Vector3.Cross(corners[0] - pointA, corners[i] - pointA);
                }
                else
                {
                    planeNormal = Vector3.Cross(corners[i + 1] - pointA, corners[i] - pointA);
                }
                Vector3 directionVector = Vector3.Normalize(pointPosition - pointA);

                float DOT = Vector3.Dot(directionVector, planeNormal);

                // If any plane dot value is negative, point is outside of the pyramid
                if (DOT < 0)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool PyramidCheck(Vector3 pointPosition, Vector3[] corners, float radius)
        {
            Vector3 pointA = cachedMainCamera.transform.position;

            for (int i = 0; i < 4; i++)
            {
                Vector3 planeNormal;
                Vector3 edge1, edge2;

                if (i == 3)
                {
                    edge1 = corners[0] - pointA;
                    edge2 = corners[i] - pointA;
                }
                else
                {
                    edge1 = corners[i + 1] - pointA;
                    edge2 = corners[i] - pointA;
                }

                // Compute plane normal
                planeNormal = Vector3.Cross(edge1, edge2).normalized;

                // Compute the distance from the point to the plane
                float distanceToPlane = Vector3.Dot((pointPosition - pointA), planeNormal);

                // If the point is outside by more than the radius, return false
                if (distanceToPlane < -radius)
                {
                    return false;
                }
            }

            return true; // If none of the planes exclude the circle, it is inside
        }

        // Sorts 4 points in clockwise order
        public static Vector3[] SortVectorsClockwiseXZ(Vector3[] vectors)
        {
            // Step 1: Calculate the centroid using only X and Z
            Vector2 centroidXZ = Vector2.zero;
            foreach (Vector3 v in vectors)
            {
                centroidXZ += new Vector2(v.x, v.z); // Use X and Z as 2D coordinates
            }
            centroidXZ /= 4f;

            // Step 2: Create a list of points with their angles in XZ plane
            List<(Vector3 point, float angle)> pointsWithAngles = new List<(Vector3, float)>();
            for (int i = 0; i < vectors.Length; i++)
            {
                // Translate point relative to centroid in XZ plane
                Vector2 translated = new Vector2(vectors[i].x - centroidXZ.x, vectors[i].z - centroidXZ.y);

                // Compute angle in XZ plane (Z as "Y" in 2D)
                float angle = Mathf.Atan2(translated.y, translated.x); // Radians, -π to π
                pointsWithAngles.Add((vectors[i], angle));
            }

            // Step 3: Sort by angle in descending order (clockwise)
            pointsWithAngles.Sort((a, b) => b.angle.CompareTo(a.angle));

            // Step 4: Extract sorted points
            Vector3[] sortedVectors = new Vector3[4];
            for (int i = 0; i < pointsWithAngles.Count; i++)
            {
                sortedVectors[i] = pointsWithAngles[i].point;
            }

            return sortedVectors;
        }

        // ============================= POINT ON A CIRCLE =================================================================

        /// <summary>
        /// Calculates a point on the edge of a circle given a radius and an angle.
        /// </summary>
        /// <param name="radius">Radius of the circle.</param>
        /// <param name="angleInDegrees">The angle used to determine the point's position.</param>
        /// <param name="origin">Center points.</param>
        /// <returns>A <see cref="Vector2"/> representing the point on the circle's edge.</returns>
        public static Vector2 PointOnCircle(float radius, float angleInDegrees, Vector2 origin)
        {
            // Convert from degrees to radians via multiplication by PI/180        
            float x = (float)(radius * Math.Cos(angleInDegrees * Math.PI / 180F)) + origin.x;
            float y = (float)(radius * Math.Sin(angleInDegrees * Math.PI / 180F)) + origin.y;

            return new Vector2(x, y);
        }

        /// <summary>
        /// Calculates a point on the edge of a circle given a radius and a factor.
        /// </summary>
        /// <param name="origin">Center point.</param>
        /// <param name="radius">Radius of the circle.</param>
        /// <param name="factor">Factor from 0 to 1.</param>
        /// <returns>A <see cref="Vector2"/> representing the point on the circle's edge.</returns>
        public static Vector2 PointOnCircle(Vector2 origin, float radius, float factor)
        {
            Vector2 point;
            float angle = 2 * Mathf.PI * factor;
            point.x = origin.x + radius * MathF.Cos(angle);
            point.y = origin.y + radius * MathF.Sin(angle);

            return point;
        }

        // ============================= NAVMESH =================================================================

        /// <summary>
        /// Determines whether the NavMeshAgent has successfully reached its destination.
        /// </summary>
        /// <param name="navMeshAgent">The NavMeshAgent being checked.</param>
        /// <param name="currentDestination">The target destination of the agent.</param>
        /// <param name="stopDistance">The stopping distance threshold for considering the destination reached.</param>
        /// <param name="reached">Outputs <c>true</c> if the destination was successfully reached, otherwise <c>false</c>.</param>
        /// <returns>
        /// <c>true</c> if the agent has reached its destination or stopped attempting to reach it; otherwise, <c>false</c>.
        /// </returns>
        public static bool ReachedDestination(this NavMeshAgent navMeshAgent, Vector3 currentDestination, float stopDistance, out bool reached)
        {
            // Early check
            float distanceToDestination = Vector2.Distance(new Vector2(navMeshAgent.transform.position.x, navMeshAgent.transform.position.z), new Vector2(currentDestination.x, currentDestination.z));
            if (distanceToDestination < Utils.stopDistanceOffset || distanceToDestination < stopDistance)
            {
                reached = true;
                return true;
            }

            if (!navMeshAgent.pathPending)
            {
                if (!navMeshAgent.hasPath)
                {
                    if (navMeshAgent.remainingDistance <= Utils.stopDistanceOffset) // navMeshAgent.stoppingDistance
                    {
                        if (distanceToDestination < Utils.stopDistanceOffset) reached = true; // Position reached
                        else reached = false; // Could not reach the position

                        return true;
                    }
                }
            }
            reached = false;
            return false;
        }

        // ============================= COLLIDERS =================================================================

        /// <summary>
        /// Checks if the ground is too steep for placement by casting multiple downward raycasts along a circular area.
        /// </summary>
        /// <param name="location">The center location where placement is being checked.</param>
        /// <param name="radius">The radius of the check area.</param>
        /// <returns>True if the placement is possible (ground is not too steep), otherwise false.</returns>
        public static bool SlopeCheck(Vector2 location, float radius)
        {
            const int checkPoints = 6; // Number of rays around the circle
            float angleStep = 1f / checkPoints; // Convert to fraction for consistent spacing
            Vector3? prevPoint = null; // Nullable to track previous point
            RaycastHit hit;

            for (int i = 0; i < checkPoints; i++)
            {
                Vector2 pointOnCircle = PointOnCircle(location, radius, i * angleStep);
                Vector3 rayOrigin = new Vector3(pointOnCircle.x, raycastPointY, pointOnCircle.y);

                if (Physics.Raycast(rayOrigin, Vector3.down, out hit, raycastPointY * 5f, terrainMask))
                {
                    if (prevPoint.HasValue)
                    {
                        // Compare height difference with max slope
                        if (Mathf.Abs(prevPoint.Value.y - hit.point.y) > maxSlope)
                        {
                            return false; // Too steep
                        }
                    }
                    prevPoint = hit.point; // Store for next comparison
                }
                else
                {
                    return false; // No terrain detected under a point
                }
            }

            return true;
        }

        /// <summary>
        /// Checks for open space around a location for unit spawning, training, or item drops.
        /// </summary>
        /// <param name="location">The center location.</param>
        /// <param name="locationR">The radius around the center to check.</param>
        /// <param name="spawnR">The spawn radius of the object being placed.</param>
        /// <param name="ground">Whether the object can be placed on the ground.</param>
        /// <param name="water">Whether the object can be placed on water.</param>
        /// <param name="air">Whether the object can be placed in the air.</param>
        /// <param name="circleChecks">Number of radial iterations to check (default: `Utils.maxCircleChecks`).</param>
        /// <returns>A valid `Vector3` position or `Vector3.zero` if no valid space is found.</returns>
        public static Vector3 CircleCheck(Vector2 location, float locationR, float spawnR, bool ground = true, bool water = true, bool air = true, int circleChecks = -1)
        {
            if (circleChecks == -1) circleChecks = Utils.maxCircleChecks;

            // Check the location itself first. If unit is not air, we do not check air units.
            if (GetIntersectedUnit(location, spawnR, null, !air) == null)
            {
                // Boundary check
                if (location.x < stopDistanceOffset || location.y < stopDistanceOffset || location.x > Grid.instance.width - stopDistanceOffset || location.y > Grid.instance.height - stopDistanceOffset) { }

                else
                {
                    // Determine the height at this point
                    float Y = (air) ? GetTerrainHeight(location, true, true) : GetTerrainHeight(location, ground, water);

                    if (Y != -9999f)
                    {
                        // SphereCast downward from below up
                        if (!Physics.SphereCast(new Vector3(location.x, Utils.raycastPointY, location.y), spawnR, Vector3.down, out _, Utils.raycastPointY * 5f, LayerMask.GetMask("Default")))
                        {
                            return new Vector3(location.x, Y, location.y);
                        }
                    }
                }
            }

            // Check around the location
            float increment = spawnR + stopDistanceOffset; // Adjust increment based on spawn radius
            for (int c = 0; c < circleChecks; c++)
            {
                float currentRadius = locationR + increment * (c + 1);
                float unitRlength = 2 * Mathf.PI * currentRadius;

                // Avoid division by zero and ensure aroundCount is at least 1
                int aroundCount = Mathf.Max(1, (int)(unitRlength / (spawnR * 2)));

                for (int i = 0; i < aroundCount; i++)
                {
                    Vector2 pointOnCircle = Utils.PointOnCircle(location, currentRadius, 1f / aroundCount * i);

                    // Boundary check
                    if (pointOnCircle.x < stopDistanceOffset || pointOnCircle.y < stopDistanceOffset || pointOnCircle.x > Grid.instance.width - stopDistanceOffset || pointOnCircle.y > Grid.instance.height - stopDistanceOffset) continue;

                    // Determine the height at this point
                    float Y = (air) ? GetTerrainHeight(pointOnCircle, true, true) : GetTerrainHeight(pointOnCircle, ground, water);

                    if (Y == -9999f) continue;

                    // If unit is not air, we do not check air units.
                    if (GetIntersectedUnit(pointOnCircle, spawnR, null, !air) != null)
                    {
                        // Hit a unit, continue checking
                        continue;
                    }

                    // SphereCast downward from above the point
                    if (Physics.SphereCast(new Vector3(pointOnCircle.x, Utils.raycastPointY, pointOnCircle.y), spawnR, Vector3.down, out _, Utils.raycastPointY * 5f, LayerMask.GetMask("Default")))
                    {
                        // Hit an object, continue checking
                        continue;
                    }

                    return new Vector3(pointOnCircle.x, Y, pointOnCircle.y);
                }
            }

            // No valid position found
            return Vector3.zero;
        }

        /// <summary>
        /// Sets the global scale of a transform, ensuring it retains the correct world size.
        /// </summary>
        /// <param name="transform">The target transform.</param>
        /// <param name="globalScale">The desired global scale.</param>
        public static void SetGlobalScale(this Transform transform, Vector3 globalScale)
        {
            transform.localScale = Vector3.one;
            transform.localScale = new Vector3(globalScale.x / transform.lossyScale.x, globalScale.y / transform.lossyScale.y, globalScale.z / transform.lossyScale.z);
        }

        /// <summary>
        /// Checks if a world position is within the viewport of the main camera.
        /// </summary>
        /// <param name="position">World position to check.</param>
        /// <returns>True if the position is visible; otherwise, false.</returns>
        public static bool IsInView(Vector3 position)
        {
            if (cachedMainCamera == null) cachedMainCamera = Camera.main;

            Vector3 screenPoint = Camera.main.WorldToViewportPoint(position);
            return screenPoint.z > 0 && screenPoint.x > 0 && screenPoint.x < 1 && screenPoint.y > 0 && screenPoint.y < 1;
        }

        // ============================= RAYCASTS =================================================================

        /// <summary>
        /// Casts a ray from the camera into a plane and returns the point of intersection.
        /// </summary>
        /// <param name="clickPosition">The screen-space position where the ray is cast from.</param>
        /// <param name="centerOfScreen"> If <c>true</c>, the ray is cast from the center of the screen instead of <paramref name="clickPosition"/>.</param>
        /// <returns>
        /// A <see cref="Vector3"/> representing the intersection point with the plane, 
        /// or <c>Vector3.zero</c> if no intersection occurs.
        /// </returns>
        public static Vector3 PlaneRayCast(Vector3 clickPosition, bool centerOfScreen = false)
        {
            // Plane based on If 3D or 2D
            Plane plane = new Plane(Vector3.up, Vector3.zero);
            Ray ray = Camera.main.ScreenPointToRay(clickPosition);

            float entry;
            if (plane.Raycast(ray, out entry))
            {
                return ray.GetPoint(entry);
            }
            else
            {
                return Vector3.zero;
            }
        }

        /// <summary>
        /// Casts a ray from the camera into a horizontal plane at a specified position and returns the intersection point.
        /// </summary>
        /// <param name="clickPosition">The screen-space position where the ray is cast from.</param>
        /// <param name="planePosition">The world-space position defining the height of the plane.</param>
        /// <returns>
        /// A <see cref="Vector3"/> representing the intersection point with the plane, 
        /// or <c>Vector3.zero</c> if no intersection occurs.
        /// </returns>
        public static Vector3 PlaneRayCast(Vector3 clickPosition, Vector3 planePosition)
        {
            // Plane based on If 3D or 2D
            Plane plane = new Plane(Vector3.up, planePosition);
            Ray ray = Camera.main.ScreenPointToRay(clickPosition);

            float entry;
            if (plane.Raycast(ray, out entry))
            {
                return ray.GetPoint(entry);
            }
            else
            {
                return Vector3.zero;
            }
        }

        /// <summary>
        /// Gets the terrain height at a given world position, checking against all terrain types, including visual terrain objects.
        /// </summary>
        /// <param name="position">The 2D world position (X, Y) where the height is queried.</param>
        /// <returns>
        /// The height (Y-coordinate) of the terrain at the given position. 
        /// Returns <c>-9999f</c> if no terrain is detected.
        /// </returns>
        public static float GetTerrainHeightAbsolute(Vector2 position)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(position.x, Utils.raycastPointY, position.y), Vector3.down, out hit, Utils.raycastPointY * 5f, terrainMaskVisuals)) return hit.point.y;
            return -9999f;
        }

        /// <summary>
        /// Retrieves the terrain height (Y-axis) at a given position on the map using a raycast.
        /// </summary>
        /// <param name="position">The 2D world position (X, Y) where the terrain height is queried.</param>
        /// <param name="ground">If <c>true</c>, the function checks for ground terrain.</param>
        /// <param name="water">If <c>true</c>, the function checks for water surfaces.</param>
        /// <returns>
        /// The height (Y-coordinate) of the terrain at the given position.
        /// Returns <c>-9999f</c> if no valid terrain was detected.
        /// </returns>
        public static float GetTerrainHeight(Vector2 position, bool ground = true, bool water = true)
        {
            RaycastHit hit;

            if (ground && water)
            {
                if (Physics.Raycast(new Vector3(position.x, Utils.raycastPointY, position.y), Vector3.down, out hit, Utils.raycastPointY * 5f, terrainMask)) return hit.point.y;
            }
            else if (ground)
            {
                if (Physics.Raycast(new Vector3(position.x, Utils.raycastPointY, position.y), Vector3.down, out hit, Utils.raycastPointY * 5f, groundMask)) return hit.point.y;
            }
            else
            {
                if (Physics.Raycast(new Vector3(position.x, Utils.raycastPointY, position.y), Vector3.down, out hit, Utils.raycastPointY * 5f, waterMask)) return hit.point.y;
            }

            return -9999f;
        }

        /// <summary>
        /// Determines the terrain height (Y-axis) at a given position on the map using a raycast.
        /// </summary>
        /// <param name="position">The 2D world position (X, Y) where the terrain height is queried.</param>
        /// <param name="ground">If <c>true</c>, the function checks for ground terrain.</param>
        /// <param name="water">If <c>true</c>, the function checks for water surfaces.</param>
        /// <param name="height">Outputs the height (Y-coordinate) of the terrain if a valid surface is detected.</param>
        /// <returns><c>true</c> if terrain height was successfully found, otherwise <c>false</c>.</returns>
        public static bool GetTerrainHeight(Vector2 position, bool ground, bool water, out float height)
        {
            RaycastHit hit;

            if (ground && water)
            {
                if (Physics.Raycast(new Vector3(position.x, Utils.raycastPointY, position.y), Vector3.down, out hit, Utils.raycastPointY * 5f, terrainMask))
                {
                    height = hit.point.y;
                    return true;
                }
            }
            else if (ground)
            {
                if (Physics.Raycast(new Vector3(position.x, Utils.raycastPointY, position.y), Vector3.down, out hit, Utils.raycastPointY * 5f, groundMask))
                {
                    height = hit.point.y;
                    return true;
                }
            }
            else
            {
                if (Physics.Raycast(new Vector3(position.x, Utils.raycastPointY, position.y), Vector3.down, out hit, Utils.raycastPointY * 5f, waterMask))
                {
                    height = hit.point.y;
                    return true;
                }
            }
            height = 0;
            return false;
        }

        /// <summary>
        /// Retrieves the terrain height (Y-axis) at a given position using a raycast.
        /// </summary>
        /// <param name="position">The 2D world position (X, Y) where the terrain height is queried.</param>
        /// <param name="mask">The layer mask used to determine which surfaces should be checked.</param>
        /// <returns>
        /// The height (Y-coordinate) of the terrain at the given position.
        /// Returns <c>-9999f</c> if no valid terrain was detected.
        /// </returns>
        public static float GetTerrainHeight(Vector2 position, int mask)
        {
            RaycastHit hit;

            if (Physics.Raycast(new Vector3(position.x, Utils.raycastPointY, position.y), Vector3.down, out hit, Utils.raycastPointY * 5f, mask)) return hit.point.y;

            return -9999f;
        }

        /// <summary>
        /// Determines the type of terrain at a given position using raycasting.
        /// </summary>
        /// <param name="position">The 2D world position (X, Y) where the terrain type is queried.</param>
        /// <returns>
        /// An integer representing the terrain type:
        /// <list type="bullet">
        /// <item><description><c>-1</c> = No terrain detected.</description></item>
        /// <item><description><c>0</c> = Ground terrain.</description></item>
        /// <item><description><c>1</c> = Shallow water (both ground and water detected).</description></item>
        /// <item><description><c>2</c> = Deep water (only water detected).</description></item>
        /// </list>
        /// </returns>
        public static int GetTerrainType(Vector2 position)
        {
            int type = -1;
            if (Physics.Raycast(new Vector3(position.x, Utils.raycastPointY, position.y), Vector3.down, out _, Utils.raycastPointY * 5f, groundMask)) type = 0;
            if (Physics.Raycast(new Vector3(position.x, Utils.raycastPointY, position.y), Vector3.down, out _, Utils.raycastPointY * 5f, waterMask))
            {
                if (type == 0) type = 1;
                else type = 2;
            }
            return type;
        }

        /// <summary>
        /// Performs a raycast from the screen position to detect terrain and returns the hit point.
        /// </summary>
        /// <param name="position">The screen position (X, Y) from which the raycast originates.</param>
        /// <returns>
        /// The world position (X, Y, Z) where the raycast intersects with the terrain.
        /// Returns <c>Vector3.zero</c> if no valid terrain was detected.
        /// </returns>
        public static Vector3 TerrainScreenRaycast(Vector2 position)
        {
            Ray ray = Camera.main.ScreenPointToRay(position);

            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, PlayerControl.rayDistance, terrainMaskVisuals)) // terrainMask
            {
                return hit.point;
            }

            return Vector3.zero;
        }

        /// <summary>
        /// Performs a raycast from the screen position to detect terrain and returns the hit point.
        /// </summary>
        /// <param name="position">The screen position (X, Y) from which the raycast originates.</param>
        /// <param name="mask">The layer mask used to filter which objects the raycast can hit.</param>
        /// <returns>
        /// The world position (X, Y, Z) where the raycast intersects with the terrain.
        /// Returns <c>Vector3.zero</c> if no valid terrain was detected.
        /// </returns>
        public static Vector3 TerrainScreenRaycast(Vector2 position, int mask)
        {
            Ray ray = Camera.main.ScreenPointToRay(position);

            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, PlayerControl.rayDistance, mask))
            {
                return hit.point;
            }

            return Vector3.zero;
        }

        /// <summary>
        /// Performs a raycast from a given world position downward to detect terrain and returns the hit point.
        /// </summary>
        /// <param name="position">The world position (X, Z) from which the raycast originates.</param>
        /// <param name="mask">The layer mask used to filter which objects the raycast can hit.</param>
        /// <returns>
        /// The world position (X, Y, Z) where the raycast intersects with the terrain.
        /// Returns <c>Vector3.zero</c> if no valid terrain was detected.
        /// </returns>
        public static Vector3 TerrainRaycastByPosition(Vector2 position, int mask)
        {
            RaycastHit hit;

            if (Physics.Raycast(new Vector3(position.x, Utils.raycastPointY, position.y), Vector3.down, out hit, Utils.raycastPointY * 5f, mask)) return hit.point;

            return Vector3.zero;
        }

        /// <summary>
        /// Performs a raycast from the cursor position and returns the first detected unit.
        /// </summary>
        /// <returns>
        /// The <see cref="Unit"/> component of the object hit by the raycast.
        /// Returns <c>null</c> if no unit was found at the cursor position.
        /// </returns>
        public static Unit GetUnitAtCursor()
        {
            Ray ray = Camera.main.ScreenPointToRay(Camera_TopDown.instance.GetCursorPosition());
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, PlayerControl.rayDistance))
            {
                Unit selectedUnit = hit.transform.GetComponent<Unit>();
                if (selectedUnit)
                {
                    return selectedUnit;
                }
            }
            return null;
        }

        // ============================= ARRAY POPULATION =================================================================

        /// <summary>
        /// Populates a <see cref="List{T}"/> with a specified value for a given count.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to populate.</param>
        /// <param name="val">The value to insert.</param>
        /// <param name="count">The number of times to insert the value.</param>
        public static void Populate<T>(this List<T> list, T val, int count)
        {
            for (int i = 0; i < count; i++)
            {
                list.Add(val);
            }
        }

        /// <summary>
        /// Populates a <see cref="HashSet{T}"/> with a specified value for a given count.
        /// Note: Since <see cref="HashSet{T}"/> only stores unique values, fewer than <paramref name="count"/> elements may be added.
        /// </summary>
        /// <typeparam name="T">The type of elements in the hash set.</typeparam>
        /// <param name="list">The hash set to populate.</param>
        /// <param name="val">The value to insert.</param>
        /// <param name="count">The number of times to attempt inserting the value.</param>
        public static void Populate<T>(this HashSet<T> list, T val, int count)
        {
            for (int i = 0; i < count; i++)
            {
                list.Add(val);
            }
        }

        /// <summary>
        /// Populates an array with a specified value for a given count.
        /// </summary>
        /// <typeparam name="T">The type of elements in the array.</typeparam>
        /// <param name="list">The array to populate.</param>
        /// <param name="val">The value to assign.</param>
        /// <param name="count">The number of elements to assign the value to. Must not exceed the array length.</param>
        public static void Populate<T>(this T[] list, T val, int count)
        {
            for (int i = 0; i < count; i++)
            {
                list[i] = (val);
            }
        }

        // ============================= DEBUG =================================================================

        /// <summary>
        /// Draws a debug circle in the scene view using <see cref="Debug.DrawLine"/>.
        /// </summary>
        /// <param name="center">The center position of the circle.</param>
        /// <param name="radius">The radius of the circle.</param>
        /// <param name="color">The color of the circle.</param>
        public static void DrawCircle(Vector3 center, float radius, Color color)
        {
            Vector3 prevPos = center + new Vector3(radius, 0, 0);
            for (int i = 0; i < 30; i++)
            {
                float angle = (float)(i + 1) / 30.0f * Mathf.PI * 2.0f;
                Vector3 newPos = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                Debug.DrawLine(prevPos, newPos, color);
                prevPos = newPos;
            }
        }

        /// <summary>
        /// Draws a debug wireframe box in the scene view using <see cref="Debug.DrawLine"/>.
        /// </summary>
        /// <param name="pos">The world position of the box.</param>
        /// <param name="rot">The rotation of the box.</param>
        /// <param name="scale">The scale of the box.</param>
        /// <param name="c">The color of the box lines.</param>
        public static void DrawBox(Vector3 pos, Quaternion rot, Vector3 scale, Color c)
        {
            // create matrix
            Matrix4x4 m = new Matrix4x4();
            m.SetTRS(pos, rot, scale);

            var point1 = m.MultiplyPoint(new Vector3(-0.5f, -0.5f, 0.5f));
            var point2 = m.MultiplyPoint(new Vector3(0.5f, -0.5f, 0.5f));
            var point3 = m.MultiplyPoint(new Vector3(0.5f, -0.5f, -0.5f));
            var point4 = m.MultiplyPoint(new Vector3(-0.5f, -0.5f, -0.5f));

            var point5 = m.MultiplyPoint(new Vector3(-0.5f, 0.5f, 0.5f));
            var point6 = m.MultiplyPoint(new Vector3(0.5f, 0.5f, 0.5f));
            var point7 = m.MultiplyPoint(new Vector3(0.5f, 0.5f, -0.5f));
            var point8 = m.MultiplyPoint(new Vector3(-0.5f, 0.5f, -0.5f));

            Debug.DrawLine(point1, point2, c);
            Debug.DrawLine(point2, point3, c);
            Debug.DrawLine(point3, point4, c);
            Debug.DrawLine(point4, point1, c);

            Debug.DrawLine(point5, point6, c);
            Debug.DrawLine(point6, point7, c);
            Debug.DrawLine(point7, point8, c);
            Debug.DrawLine(point8, point5, c);

            Debug.DrawLine(point1, point5, c);
            Debug.DrawLine(point2, point6, c);
            Debug.DrawLine(point3, point7, c);
            Debug.DrawLine(point4, point8, c);
        }

        // ============================= ABILITIES =================================================================

        /// <summary>
        /// Retrieves an ability by its hierarchical name.
        /// The name should be formatted as "1-5-6", representing unit.abilities[1].abilities[5].abilities[6].
        /// </summary>
        /// <param name="unit">The unit containing abilities.</param>
        /// <param name="name">The hierarchical name of the ability (e.g., "1-5-6").</param>
        /// <param name="abilityIndex">Outputs the global ability index.</param>
        /// <returns>The requested ability if found, otherwise null.</returns>
        public static Ability GetAbilityByName(Unit unit, string name, out int abilityIndex)
        {
            Ability ability;

            string[] names = name.Split('-');

            if (names.Length > 0)
            {
                if (int.TryParse(names[0], out int initialIndex))
                {
                    ability = unit.abilities[initialIndex];

                    for (int i = 1; i < names.Length; i++)
                    {
                        Container ac = (Container)ability;

                        if (int.TryParse(names[i], out int index))
                        {
                            ability = ac.abilities[index];
                        }
                        else
                        {
                            abilityIndex = 0;
                            return null;
                        }
                    }

                    abilityIndex = initialIndex;
                    return ability;
                }
            }

            abilityIndex = 0;
            return null;
        }

        /// <summary>
        /// Retrieves an ability by its global index.
        /// </summary>
        /// <param name="unit">The unit containing abilities.</param>
        /// <param name="name">The global index of the ability as a string.</param>
        /// <param name="abilityIndex">Outputs the global index of the ability if found, otherwise -1.</param>
        /// <returns>The requested ability if found, otherwise null.</returns>
        public static Ability GetAbilityByIndexName(Unit unit, string name, out int abilityIndex)
        {
            // Try to parse the name
            if (int.TryParse(name, out int abilityGlobalIndex))
            {
                int currentIndexCount = -1;
                abilityIndex = abilityGlobalIndex;
                return RecursiveAbilitySearch(unit.abilities, abilityGlobalIndex, ref currentIndexCount);
            }
            else
            {
                abilityIndex = -1;
                return null;
            }
        }

        /// <summary>
        /// Retrieves an ability from a unit by its global index.
        /// </summary>
        /// <param name="unit">The unit containing abilities.</param>
        /// <param name="index">The global index of the ability.</param>
        /// <returns>The ability if found, otherwise null.</returns>
        public static Ability GetAbilityByIndex(Unit unit, int index)
        {
            int currentIndexCount = -1;
            return RecursiveAbilitySearch(unit.abilities, index, ref currentIndexCount);
        }

        /// <summary>
        /// Recursively searches through all abilities and ability containers to find an ability by its global index.
        /// </summary>
        /// <param name="recursiveAbilities">The current ability list being searched.</param>
        /// <param name="abilityIndex">The target global index of the ability.</param>
        /// <param name="currentIndexCount">A reference counter tracking the current traversal index.</param>
        /// <returns>The ability if found, otherwise null.</returns>
        private static Ability RecursiveAbilitySearch(Ability[] recursiveAbilities, int abilityIndex, ref int currentIndexCount)
        {
            for (int i = 0; i < recursiveAbilities.Length; i++)
            {
                currentIndexCount = currentIndexCount + 1;
                if (currentIndexCount == abilityIndex)
                {
                    // Ability found return ability
                    return recursiveAbilities[i];
                }
                else if (recursiveAbilities[i].type == AbilityType.Container)
                {
                    Container container = (Container)recursiveAbilities[i];

                    Ability abilityFound = RecursiveAbilitySearch(container.abilities, abilityIndex, ref currentIndexCount);
                    if (abilityFound != null)
                    {
                        return abilityFound;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Retrieves the global index of an ability.
        /// </summary>
        /// <param name="abilities">The root ability list to search within.</param>
        /// <param name="abilityToSearch">The ability to locate.</param>
        /// <returns>The global index of the ability, or -1 if not found.</returns>
        public static int GetAbilityIndex(Ability[] abilities, Ability abilityToSearch)
        {
            int currentIndexCount = -1;
            bool abilityFound = RecursiveAbilityIndexSearch(abilities);

            return currentIndexCount;

            bool RecursiveAbilityIndexSearch(Ability[] recursiveAbilities)
            {
                for (int i = 0; i < recursiveAbilities.Length; i++)
                {
                    currentIndexCount++;
                    if (abilityToSearch == recursiveAbilities[i])
                    {
                        // Ability found return abilityIndex
                        return true;
                    }
                    else if (recursiveAbilities[i].type == AbilityType.Container)
                    {
                        Container container = (Container)recursiveAbilities[i];
                        if (RecursiveAbilityIndexSearch(container.abilities))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        // ============================= GEOMETRY =================================================================

        /// <summary>
        /// Retrieves the four corner points of a 2D rectangle derived from a 3D BoxCollider.
        /// The rectangle is oriented counterclockwise and can be expanded outward.
        /// </summary>
        /// <param name="GO">The Transform of the GameObject containing the BoxCollider.</param>
        /// <param name="expand">Optional expansion distance applied outward from the center.</param>
        /// <returns>An array of 4 Vector2 points representing the rectangle in 2D space.</returns>
        public static Vector2[] GetRectangleFromCollider(Transform GO, float expand = 0)
        {
            Vector3[] vertices = new Vector3[8];

            BoxCollider b = GO.GetComponent<BoxCollider>();
            vertices[0] = GO.transform.TransformPoint(b.center + new Vector3(-b.size.x, b.size.y, -b.size.z) * 0.5f);
            vertices[1] = GO.transform.TransformPoint(b.center + new Vector3(-b.size.x, b.size.y, b.size.z) * 0.5f);
            vertices[2] = GO.transform.TransformPoint(b.center + new Vector3(b.size.x, b.size.y, b.size.z) * 0.5f);
            vertices[3] = GO.transform.TransformPoint(b.center + new Vector3(b.size.x, b.size.y, -b.size.z) * 0.5f);

            vertices[4] = GO.transform.TransformPoint(b.center + new Vector3(-b.size.x, -b.size.y, -b.size.z) * 0.5f);
            vertices[5] = GO.transform.TransformPoint(b.center + new Vector3(b.size.x, -b.size.y, -b.size.z) * 0.5f);
            vertices[6] = GO.transform.TransformPoint(b.center + new Vector3(b.size.x, -b.size.y, b.size.z) * 0.5f);
            vertices[7] = GO.transform.TransformPoint(b.center + new Vector3(-b.size.x, -b.size.y, b.size.z) * 0.5f);

            // Convert to 2D rectangle
            Vector2[] rectABCD = new Vector2[4];
            rectABCD[3] = new Vector2(vertices[0][0], vertices[0][2]);
            rectABCD[2] = new Vector2(vertices[1][0], vertices[1][2]);
            rectABCD[1] = new Vector2(vertices[2][0], vertices[2][2]);
            rectABCD[0] = new Vector2(vertices[3][0], vertices[3][2]);

            if (expand == 0) return rectABCD;
            else
            {
                // Expand 4 vtx in opposite to the center of the rectangle
                Vector2[] expandedRect = new Vector2[4];

                Vector2 center = (rectABCD[0] + rectABCD[1] + rectABCD[2] + rectABCD[3]) / 4;
                for (int i = 0; i < rectABCD.Length; i++)
                {
                    expandedRect[i] = (rectABCD[i] - center).normalized * expand + rectABCD[i];
                }

                return expandedRect;
            }
        }

        /// <summary>
        /// Determines if a given point is inside a rectangle defined by four corner points.
        /// The rectangle is assumed to be axis-aligned, and the order of points must be counterclockwise.
        /// </summary>
        /// <param name="m">The point to check.</param>
        /// <param name="ABCD_CounterClockwise">An array of four Vector2 points representing the rectangle in counterclockwise order.</param>
        /// <returns>True if the point is inside the rectangle, otherwise false.</returns>
        /// <remarks>
        /// This method is based on the vector projection test:
        /// 0 ≤ dot(AB, AM) ≤ dot(AB, AB) && 0 ≤ dot(AD, AM) ≤ dot(AD, AD)
        /// It does NOT work for arbitrary quadrilateral shapes.
        /// </remarks>
        public static bool IsPointInsideRect(Vector2 m, Vector2[] ABCD_CounterClockwise)
        {
            Vector2[] ABCD = new Vector2[4];
            ABCD[0] = ABCD_CounterClockwise[3];
            ABCD[1] = ABCD_CounterClockwise[2];
            ABCD[2] = ABCD_CounterClockwise[1];
            ABCD[3] = ABCD_CounterClockwise[0];

            Vector2 AB = ABCD[1] - ABCD[0];
            Vector2 AD = ABCD[3] - ABCD[0];
            Vector2 AM = m - ABCD[0];
            float dotAMAB = Vector2.Dot(AM, AB);
            float dotABAB = Vector2.Dot(AB, AB);
            float dotAMAD = Vector2.Dot(AM, AD);
            float dotADAD = Vector2.Dot(AD, AD);
            return 0 <= dotAMAB && dotAMAB <= dotABAB && 0 <= dotAMAD && dotAMAD <= dotADAD;
        }

        /// <summary>
        /// Determines whether a circle with a given center and radius is inside or intersects a quadrilateral.
        /// </summary>
        /// <param name="m">The center of the circle.</param>
        /// <param name="radius">The radius of the circle.</param>
        /// <param name="ABCD">An array of four Vector2 points representing the quadrilateral's vertices.</param>
        /// <returns>True if the circle is inside or intersects the quadrilateral, otherwise false.</returns>
        /// <remarks>
        /// The method first checks if the circle's center is inside the quadrilateral using <c>IsPointInQuadrilateral</c>.
        /// If not, it checks if the circle intersects any of the quadrilateral's edges using <c>IsCircleIntersectingEdge</c>.
        /// This works for arbitrary four-vertex polygons, and the vertex order is not important.
        /// </remarks>
        public static bool IsCircleInQuadrilateral(Vector2 m, float radius, Vector2[] ABCD)
        {
            // Ensure ABCD contains exactly 4 vertices
            if (ABCD.Length != 4)
            {
                Debug.LogError("ABCD must contain exactly 4 vertices.");
                return false;
            }

            // Check if the center is inside the quadrilateral
            bool isCenterInside = IsPointInQuadrilateral(m, ABCD);
            if (isCenterInside)
            {
                return true;
            }

            // Check if the circle intersects any of the quadrilateral's edges
            for (int i = 0; i < 4; i++)
            {
                Vector2 p1 = ABCD[i];
                Vector2 p2 = ABCD[(i + 1) % 4];

                if (IsCircleIntersectingEdge(m, radius, p1, p2))
                {
                    return true;
                }
            }

            // The circle is neither inside the quadrilateral nor intersecting any edges
            return false;
        }

        /// <summary>
        /// Determines whether a point is inside a quadrilateral.
        /// </summary>
        /// <param name="m">The point to check.</param>
        /// <param name="ABCD">An array of four Vector2 points representing the quadrilateral's vertices.</param>
        /// <returns>True if the point is inside the quadrilateral, otherwise false.</returns>
        /// <remarks>
        /// This method splits the quadrilateral into two triangles:
        /// - Triangle 1: Formed by the first three vertices (A, B, C)
        /// - Triangle 2: Formed by the first, third, and fourth vertices (A, C, D)
        ///
        /// The point is considered inside the quadrilateral if it is inside either of these triangles.
        /// It assumes the quadrilateral is convex and its vertices are ordered correctly.
        /// </remarks>
        private static bool IsPointInQuadrilateral(Vector2 m, Vector2[] ABCD)
        {
            // Triangle 1: A, B, C
            bool inTriangle1 = IsPointInTriangle(m, ABCD[0], ABCD[1], ABCD[2]);

            // Triangle 2: A, C, D
            bool inTriangle2 = IsPointInTriangle(m, ABCD[0], ABCD[2], ABCD[3]);

            return inTriangle1 || inTriangle2;
        }

        /// <summary>
        /// Determines whether a point is inside a triangle using the barycentric coordinate method.
        /// </summary>
        /// <param name="p">The point to check.</param>
        /// <param name="a">The first vertex of the triangle.</param>
        /// <param name="b">The second vertex of the triangle.</param>
        /// <param name="c">The third vertex of the triangle.</param>
        /// <returns>True if the point is inside the triangle (including edges), otherwise false.</returns>
        /// <remarks>
        /// The function calculates barycentric coordinates (u, v) to determine if the point lies within the triangle.
        /// The point is inside if: 
        /// - u >= 0
        /// - v >= 0
        /// - u + v <= 1
        ///
        /// This method is computationally efficient as it avoids costly cross products and division inside loops.
        /// It assumes that the triangle is well-formed (non-degenerate).
        /// </remarks>
        private static bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = c - a;
            Vector2 v1 = b - a;
            Vector2 v2 = p - a;

            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);

            float invDenom = 1 / (dot00 * dot11 - dot01 * dot01);
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;

            return (u >= 0) && (v >= 0) && (u + v <= 1);
        }

        /// <summary>
        /// Determines whether a circle intersects a line segment.
        /// </summary>
        /// <param name="center">The center of the circle.</param>
        /// <param name="radius">The radius of the circle.</param>
        /// <param name="p1">The first endpoint of the line segment.</param>
        /// <param name="p2">The second endpoint of the line segment.</param>
        /// <returns>True if the circle intersects the line segment, otherwise false.</returns>
        /// <remarks>
        /// This method works by projecting the circle's center onto the edge and clamping the projection within the segment.
        /// Then, it finds the closest point on the segment to the circle's center and checks if the distance is within the radius.
        /// </remarks>
        private static bool IsCircleIntersectingEdge(Vector2 center, float radius, Vector2 p1, Vector2 p2)
        {
            // Vector from p1 to p2
            Vector2 edge = p2 - p1;

            // Vector from p1 to center
            Vector2 centerToP1 = center - p1;

            // Project centerToP1 onto the edge vector
            float projection = Vector2.Dot(centerToP1, edge.normalized);

            // Clamp the projection to the edge length
            float clampedProjection = Mathf.Clamp(projection, 0, edge.magnitude);

            // Closest point on the edge to the circle's center
            Vector2 closestPoint = p1 + edge.normalized * clampedProjection;

            // Distance from the circle's center to the closest point on the edge
            float distanceToEdge = Vector2.Distance(center, closestPoint);

            // Check if this distance is less than or equal to the radius
            return distanceToEdge <= radius;
        }

        /// <summary>
        /// Determines whether two convex polygons (represented by their vertices) are intersecting.
        /// </summary>
        /// <param name="a">The first polygon's vertices in counterclockwise or clockwise order.</param>
        /// <param name="b">The second polygon's vertices in counterclockwise or clockwise order.</param>
        /// <returns>True if the polygons intersect, otherwise false.</returns>
        /// <remarks>
        /// This method uses the **Separating Axis Theorem (SAT)** to determine if two polygons overlap.
        /// If there exists an axis along which the projections of both polygons do not overlap, the polygons are not intersecting.
        /// The method iterates through the edges of both polygons, projects them onto a perpendicular axis, and checks for separation.
        /// If no separating axis is found, the polygons intersect.
        /// </remarks>
        public static bool IsPolygonsIntersecting(Vector2[] a, Vector2[] b)
        {
            foreach (var polygon in new[] { a, b })
            {
                for (int i1 = 0; i1 < polygon.Length; i1++)
                {
                    int i2 = (i1 + 1) % polygon.Length;
                    var p1 = polygon[i1];
                    var p2 = polygon[i2];

                    var normal = new Vector2(p2.y - p1.y, p1.x - p2.x);

                    double? minA = null, maxA = null;
                    foreach (var p in a)
                    {
                        var projected = normal.x * p.x + normal.y * p.y;
                        if (minA == null || projected < minA)
                            minA = projected;
                        if (maxA == null || projected > maxA)
                            maxA = projected;
                    }

                    double? minB = null, maxB = null;
                    foreach (var p in b)
                    {
                        var projected = normal.x * p.x + normal.y * p.y;
                        if (minB == null || projected < minB)
                            minB = projected;
                        if (maxB == null || projected > maxB)
                            maxB = projected;
                    }

                    if (maxA < minB || maxB < minA)
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Computes the grid cells that a straight line passes through using Bresenham's Line Algorithm.
        /// </summary>
        /// <param name="start">The starting cell (inclusive).</param>
        /// <param name="end">The ending cell (inclusive).</param>
        /// <returns>A list of grid cells (Vector2Int) that the line passes through.</returns>
        /// <remarks>
        /// This implementation of Bresenham's algorithm ensures that every cell intersected by the line is included.
        /// It works for any direction and handles both shallow and steep slopes correctly.
        /// </remarks>
        public static List<Vector2Int> GetLineCells(Vector2Int start, Vector2Int end)
        {
            List<Vector2Int> intersectedCells = new List<Vector2Int>();

            // Calculate delta values
            int deltaX = Mathf.Abs(end.x - start.x);
            int deltaY = Mathf.Abs(end.y - start.y);

            // Determine the step direction for x and y
            int stepX = start.x < end.x ? 1 : -1;
            int stepY = start.y < end.y ? 1 : -1;

            // Bresenham's algorithm initialization
            int error = deltaX - deltaY;

            int currentX = start.x;
            int currentY = start.y;

            // Add the starting cell
            intersectedCells.Add(new Vector2Int(currentX, currentY));

            // Loop through all the points until you reach the end
            while (currentX != end.x || currentY != end.y)
            {
                // Calculate error to decide whether to move in x or y
                int error2 = 2 * error;

                if (error2 > -deltaY)
                {
                    error -= deltaY;
                    currentX += stepX;
                }

                if (error2 < deltaX)
                {
                    error += deltaX;
                    currentY += stepY;
                }

                // Add the current cell to the list
                intersectedCells.Add(new Vector2Int(currentX, currentY));
            }

            return intersectedCells;
        }

        /// <summary>
        /// Computes the grid cells that a straight line passes through using Bresenham's Line Algorithm.
        /// </summary>
        /// <param name="start">The starting grid cell.</param>
        /// <param name="end">The ending grid cell.</param>
        /// <returns>A list of Vector2Int representing the grid cells the line crosses.</returns>
        /// <remarks>
        /// This version efficiently handles all line slopes, including vertical, horizontal, and diagonal lines.
        /// </remarks>
        public static List<Vector2Int> GetCellsByLine(Vector2Int start, Vector2Int end)
        {
            List<Vector2Int> cells = new List<Vector2Int>();

            int x = start.x;
            int y = start.y;
            int x2 = end.x;
            int y2 = end.y;

            int w = x2 - x;
            int h = y2 - y;

            int dx1 = 0, dy1 = 0, dx2 = 0, dy2 = 0;

            // Determine step direction for dx1, dy1, dx2, dy2
            if (w < 0) dx1 = -1; else if (w > 0) dx1 = 1;
            if (h < 0) dy1 = -1; else if (h > 0) dy1 = 1;
            if (w < 0) dx2 = -1; else if (w > 0) dx2 = 1;

            int longest = Mathf.Abs(w);
            int shortest = Mathf.Abs(h);

            // Swap if height is greater than width
            if (longest <= shortest)
            {
                longest = Mathf.Abs(h);
                shortest = Mathf.Abs(w);
                if (h < 0) dy2 = -1; else if (h > 0) dy2 = 1;
                dx2 = 0;
            }

            int numerator = longest >> 1;

            // Loop through the line and add the intersected cells
            for (int i = 0; i <= longest; i++)
            {
                cells.Add(new Vector2Int(x, y));  // Add current cell
                numerator += shortest;
                if (numerator >= longest)
                {
                    numerator -= longest;
                    x += dx1;
                    y += dy1;
                }
                else
                {
                    x += dx2;
                    y += dy2;
                }
            }

            return cells;
        }

        // ============================= SCRIPTABLE OBJECTS UNIQUE IDs =================================================================

        /// <summary>
        /// Determines if the second name is a duplicate of the first name.
        /// </summary>
        /// <param name="name1">The first name to compare.</param>
        /// <param name="name2">The second name to compare.</param>
        /// <returns>True if name2 is a duplicate of name1, otherwise false.</returns>
        public static bool GetTheDuplicate(string name1, string name2)
        {
            int number1 = GetNumberFromName(name1);
            int number2 = GetNumberFromName(name2);

            // If one has no number, consider it the base name
            if (number1 == -1 && number2 != -1)
            {
                return true; // name2 is duplicate
            }
            else if (number1 != -1 && number2 == -1)
            {
                return false; // name1 is duplicate
            }
            else
            {
                // Compare the numbers
                if (number1 > number2)
                {
                    return false; // name1 is duplicate
                }
                else
                {
                    return true; // name2 is duplicate
                }
            }
        }

        /// <summary>
        /// Extracts the base name without any trailing numbers.
        /// </summary>
        /// <param name="name">The full name which may contain a number at the end.</param>
        /// <returns>The base name without the trailing number.</returns>
        public static string GetBaseName(string name)
        {
            int lastSpaceIndex = name.LastIndexOf(' ');
            if (lastSpaceIndex != -1 && int.TryParse(name.Substring(lastSpaceIndex + 1), out _))
            {
                return name.Substring(0, lastSpaceIndex);
            }
            return name; // Return the whole name if there's no number
        }

        /// <summary>
        /// Extracts the number at the end of a name, if present.
        /// </summary>
        /// <param name="name">The full name which may contain a trailing number.</param>
        /// <returns>The extracted number or -1 if no number is found.</returns>
        public static int GetNumberFromName(string name)
        {
            int lastSpaceIndex = name.LastIndexOf(' ');
            if (lastSpaceIndex != -1)
            {
                string numberPart = name.Substring(lastSpaceIndex + 1);
                if (int.TryParse(numberPart, out int result))
                {
                    return result;
                }
            }
            return -1; // Return -1 if there's no number at the end
        }
    }
}

