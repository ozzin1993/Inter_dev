using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Utils.Physics.cs — NavMesh/коллайдеры/рейкасты/массивы/дебаг. Вырезано 1:1 из Utils.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public static partial class Utils
    {
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
                if (location.x < stopDistanceOffset || location.y < stopDistanceOffset || location.x > Grid.Instance.width - stopDistanceOffset || location.y > Grid.Instance.height - stopDistanceOffset) { }

                else
                {
                    // Determine the height at this point
                    float Y = (air) ? GetTerrainHeight(location, true, true) : GetTerrainHeight(location, ground, water);

                    if (Y != -9999f)
                    {
                        // SphereCast downward from below up
                        if (!Physics.SphereCast(new Vector3(location.x, Utils.raycastPointY, location.y), spawnR, Vector3.down, out _, Utils.raycastPointY * 5f, defaultMask))
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
                    if (pointOnCircle.x < stopDistanceOffset || pointOnCircle.y < stopDistanceOffset || pointOnCircle.x > Grid.Instance.width - stopDistanceOffset || pointOnCircle.y > Grid.Instance.height - stopDistanceOffset) continue;

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
                    if (Physics.SphereCast(new Vector3(pointOnCircle.x, Utils.raycastPointY, pointOnCircle.y), spawnR, Vector3.down, out _, Utils.raycastPointY * 5f, defaultMask))
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
            Vector3 screenPoint = MainCamera.WorldToViewportPoint(position);
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
            Ray ray = MainCamera.ScreenPointToRay(clickPosition);

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
            Ray ray = MainCamera.ScreenPointToRay(clickPosition);

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
            Ray ray = MainCamera.ScreenPointToRay(position);

            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, cursorRayDistance, terrainMaskVisuals)) // terrainMask
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
            Ray ray = MainCamera.ScreenPointToRay(position);

            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, cursorRayDistance, mask))
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
            Ray ray = MainCamera.ScreenPointToRay(Presentation.Camera?.GetCursorPosition() ?? Vector2.zero);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, cursorRayDistance))
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

    }
}
