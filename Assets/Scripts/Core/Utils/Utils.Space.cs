using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Utils.Space.cs — трансформы/награды/рамка выделения/окружность. Вырезано 1:1 из Utils.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public static partial class Utils
    {
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
            var v1 = MainCamera.ScreenToViewportPoint(screenPosition1);
            var v2 = MainCamera.ScreenToViewportPoint(screenPosition2);
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
            min.z = MainCamera.nearClipPlane;
            max.z = MainCamera.farClipPlane;
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

    }
}
