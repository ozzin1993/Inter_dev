using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Utils.Units.cs — юниты (UNITS + UNIT COMPONENTS). Вырезано 1:1 из Utils.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public static partial class Utils
    {
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

        // [Interflow fix 2026-08-01 require-component-die] Кэш «тип компонента → требует ли Unit через [RequireComponent]».
        static readonly Dictionary<Type, bool> requiresUnitCache = new Dictionary<Type, bool>();

        /// <summary>
        /// [Interflow fix 2026-08-01 require-component-die] Удаляет с юнита все компоненты, зависящие от Unit
        /// через [RequireComponent(typeof(Unit))] (DeathEffects, AutoAbilityUser, LineCompositionAura, SoulHarvest
        /// и будущие). Вызывать ПЕРЕД Destroy(компонента Unit): иначе Unity в рантайме отказывает
        /// («Can't remove Unit (Script) because ... depends on it») и Unit остаётся жить на трупе.
        /// </summary>
        public static void DestroyUnitDependents(Unit unit)
        {
            MonoBehaviour[] list = unit.GetComponents<MonoBehaviour>();
            for (int i = 0; i < list.Length; i++)
            {
                MonoBehaviour mb = list[i];
                if (mb == null || mb == unit) continue;
                Type t = mb.GetType();
                if (!requiresUnitCache.TryGetValue(t, out bool requires))
                {
                    requires = false;
                    object[] attrs = t.GetCustomAttributes(typeof(RequireComponent), true);
                    for (int a = 0; a < attrs.Length; a++)
                    {
                        RequireComponent rc = (RequireComponent)attrs[a];
                        if ((rc.m_Type0 != null && rc.m_Type0.IsAssignableFrom(typeof(Unit)))
                            || (rc.m_Type1 != null && rc.m_Type1.IsAssignableFrom(typeof(Unit)))
                            || (rc.m_Type2 != null && rc.m_Type2.IsAssignableFrom(typeof(Unit))))
                        { requires = true; break; }
                    }
                    requiresUnitCache[t] = requires;
                }
                if (requires) GameManager.Destroy(mb);
            }
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
            DestroyUnitDependents(unit); // [Interflow fix 2026-08-01 require-component-die] снять [RequireComponent(Unit)]-компоненты, иначе Unit не удалится
            if (unit.GetComponent<Unit>()) GameManager.Destroy(unit.GetComponent<Unit>());

            foreach (Transform child in unit.transform)
            {
                if (child.name == "MiniMapIcon(Clone)" || child.name == "HealthBar(Clone)" || child.name == "VFXHolder") GameManager.Destroy(child.gameObject);
            }
        }

    }
}
