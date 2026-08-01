using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace StrategyCore
{
    // This script is responsible for manipulation of position, rotation and scale values of the unit
    public partial class Unit
    {
        private bool m_walkAnimationPlaying = false;

        // ============================= NETWORK POSITION UPDATE =============================

        private float bufferPositions = 2;

        private Queue<Vector2> networkPosition = new Queue<Vector2>();
        private Queue<float> networkTimings = new Queue<float>();

        private Vector2 endPosition;
        private Vector2 networkDirection;
        private float lerpSpeed;

        [HideInInspector] public bool lastPos = false;
        [HideInInspector] public bool reached = true;

        Vector2 oldPos2D;

        public void PositionSetDirect(Vector2 netPos, float lat)
        {
            oldPos2D = new Vector2(transform.position.x, transform.position.z);
            networkDirection = (netPos - oldPos2D).normalized;

            if (netPos.x < 0) // Means we are at the last position
            {
                lastPos = true;

                lerpSpeed = Vector2.Distance(oldPos2D, endPosition) / lat;
            }
            else
            {
                lastPos = false;
                endPosition = netPos;

                lerpSpeed = Vector2.Distance(oldPos2D, netPos) / lat;
            }

            // For the first position, we set the animation to true
            if (!isMoving && !stunned)
            {
                AnimatorSetBool(AnimationState.Walk, true);
                m_walkAnimationPlaying = true;
            }
            isMoving = true;
        }

        private void PositionUpdateDirect()
        {
            if (!isMoving) return;
            oldPos2D = new Vector2(transform.position.x, transform.position.z);

            if (lastPos)
            {
                Vector2 pos2D = oldPos2D + ((endPosition - oldPos2D).normalized * lerpSpeed * Time.deltaTime);

                if (isAir) transform.position = transform.position = new Vector3(pos2D.x, Utils.airUnitElevation, pos2D.y);
                else transform.position = new Vector3(pos2D.x, Utils.GetTerrainHeight(pos2D), pos2D.y);

                if ((endPosition - oldPos2D).sqrMagnitude < 0.01f)
                {
                    reached = true;
                    isMoving = false;

                    // We should not rewrite current animation, if being played
                    if (currentAnimatorBoolState != AnimationState.Walk && (currentAnimatorBoolState != AnimationState.Idle || playCast))
                        AnimatorSetBool(AnimationState.Walk, false, false);
                    else
                        AnimatorSetBool(AnimationState.Walk, false);
                    m_walkAnimationPlaying = false;
                }
            }
            else
            {
                // Move in direction
                Vector2 pos2D = oldPos2D + (networkDirection * lerpSpeed * Time.deltaTime);

                if (isAir) transform.position = new Vector3(pos2D.x, Utils.airUnitElevation, pos2D.y);
                else transform.position = new Vector3(pos2D.x, Utils.GetTerrainHeight(pos2D), pos2D.y);

                // Calculate rotation
                if ((new Vector2(horizontalPart.position.x, horizontalPart.position.z) - oldPos2D).sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(new Vector3(horizontalPart.position.x, 0, horizontalPart.position.z) - new Vector3(oldPos2D.x, 0, oldPos2D.y));
                    horizontalPart.rotation = Quaternion.RotateTowards(
                            horizontalPart.rotation, // Current rotation
                            Quaternion.Euler(0, targetRotation.eulerAngles.y, 0) * horizontalPartForward, // Target rotation
                            turnSpeed * Time.deltaTime // Rotation step per frame
                        );
                }
            }
        }

        // Different method of smoothing for networkposition sync. Not used currently.

        public void PositionSetDirectInterpolated(Vector2 netPos, float serverTime)
        {
            // Means we are at the last position
            if (netPos.x > 65534f) return;

            networkPosition.Enqueue(netPos);
            networkTimings.Enqueue(serverTime);

            if (networkPosition.Count == bufferPositions)
            {
                isMoving = true;
                if (!m_walkAnimationPlaying && target == null && targetPosition == Vector2.zero)
                {
                    m_walkAnimationPlaying = true;
                    AnimatorSetBool(AnimationState.Walk, true);
                }
            }

            // Remove older entries if they are no longer needed
            while (networkTimings.Count > 1 && networkTimings.Peek() < serverTime)
            {
                networkPosition.Dequeue();
                networkTimings.Dequeue();
            }
        }

        private void PositionUpdateDirectInterpolated()
        {
            if (!isMoving) return;

            Vector3 oldPos = transform.position;

            // Get the current time for interpolation
            float currentTime = NetworkManager.Singleton.ServerTime.TimeAsFloat;

            // Interpolate and smooth the position
            Vector2 interpolatedPosition = InterpolatePosition(currentTime);
            Vector2 smoothedPosition = SmoothPosition(
                new Vector2(transform.position.x, transform.position.z),
                interpolatedPosition,
                Time.deltaTime
            );

            // Apply the smoothed position to the transform
            transform.position = new Vector3(
                smoothedPosition.x,
                Utils.GetTerrainHeight(smoothedPosition),
                smoothedPosition.y
            );

            // Calculate rotation
            if (transform.position.x != oldPos.x || transform.position.z != oldPos.z)
            {
                Quaternion targetRotation = Quaternion.LookRotation(new Vector3(horizontalPart.position.x, 0, horizontalPart.position.z) - new Vector3(oldPos.x, 0, oldPos.z));
                horizontalPart.rotation = Quaternion.RotateTowards(
                        horizontalPart.rotation, // Current rotation
                        Quaternion.Euler(0, targetRotation.eulerAngles.y, 0) * horizontalPartForward, // Target rotation
                        turnSpeed * Time.deltaTime // Rotation step per frame
                    );
            }

            // Check if movement to the next position is complete
            if (networkPosition.Count > 0 &&
                (interpolatedPosition - smoothedPosition).sqrMagnitude < 0.01f)
            {
                networkPosition.Dequeue();
                networkTimings.Dequeue();

                if (networkPosition.Count == 0)
                {
                    isMoving = false;
                    m_walkAnimationPlaying = false;
                    AnimatorSetBool(AnimationState.Walk, false);
                }
            }
        }

        private Vector2 SmoothPosition(Vector2 currentInterpolatedPosition, Vector2 targetPosition, float deltaTime)
        {
            const float smoothingFactor = 0.1f; // Adjust this to control the smoothing effect
            return Vector2.Lerp(currentInterpolatedPosition, targetPosition, deltaTime / smoothingFactor);
        }

        private Vector2 InterpolatePosition(float currentServerTime)
        {
            // Ensure we have at least two points to interpolate
            if (networkPosition.Count < 2)
            {
                return networkPosition.Count == 1 ? networkPosition.Peek() : Vector2.zero;
            }

            // Convert the queue to an array for easier indexed access
            Vector2[] positionArray = networkPosition.ToArray();
            float[] timingArray = networkTimings.ToArray();

            // Find the closest points to the current time
            for (int i = 0; i < timingArray.Length - 1; i++)
            {
                if (currentServerTime >= timingArray[i] && currentServerTime <= timingArray[i + 1])
                {
                    // Calculate interpolation factor
                    float t = (float)((currentServerTime - timingArray[i]) / (timingArray[i + 1] - timingArray[i]));
                    return Vector2.Lerp(positionArray[i], positionArray[i + 1], t);
                }
            }

            // If currentTime is outside the buffer range, return the last position (extrapolate or clamp)
            return positionArray[^1];
        }

        Vector2 CubicLerp(Vector2 a, Vector2 b, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return (1.0f - t3) * a + 3.0f * t2 * b - 2.0f * t3 * Vector2.Lerp(a, b, 0.5f);
        }

        // ============================= POSITION =============================

        /// <summary>
        /// Sets the position of this unit based on Vector2 location. Checks the height of the terrain below. Returns false if not possible to set the position.
        /// </summary>
        /// <param name="position">Desired position for the unit.</param>
        /// <returns></returns>
        public bool SetPosition(Vector2 position)
        {
            if (isAir)
            {
                transform.position = new Vector3(position.x, Utils.airUnitElevation, position.y);
                return true;
            }
            else
            {
                if (Utils.GetTerrainHeight(position, isGround, isWater, out float Y))
                {
                    transform.position = new Vector3(position.x, Y, position.y);
                    return true;
                }
            }
            return false;
        }

        // ============================= ROTATION =============================

        /// <summary>
        /// Sets unit`s rotation towards position at turn speed. Returns false if rotation not possible (LookPosition == position).
        /// </summary>
        /// <param name="targetPosition"></param>
        /// <returns></returns>
        public bool LookAt(Vector3 targetPosition)
        {
            Quaternion targetRotation = Quaternion.identity;

            // Calculate rotation
            if (verticalPart != null)
            {
                Vector3 direction = targetPosition - verticalPart.position;
                if (direction != Vector3.zero) targetRotation = Quaternion.LookRotation(direction);
                else return false;
            }
            else if (horizontalPart != null)
            {
                Vector3 direction = new Vector3(targetPosition.x, 0, targetPosition.z) - new Vector3(horizontalPart.position.x, 0, horizontalPart.position.z);
                if (direction != Vector3.zero) targetRotation = Quaternion.LookRotation(direction);
                else return false;
            }

            // Rotate vertical part towards the target at the specified speed
            if (verticalPart != null)
            {
                verticalPart.localRotation = Quaternion.RotateTowards(
                    verticalPart.localRotation, // Current rotation
                    Quaternion.Euler(targetRotation.eulerAngles.x, 0, 0), // Target rotation
                    turnSpeed * Time.deltaTime // Rotation step per frame
                );
            }

            // Rotate horizontal part towards the target at the specified speed
            if (horizontalPart != null)
            {
                horizontalPart.rotation = Quaternion.RotateTowards(
                    horizontalPart.rotation, // Current rotation
                    Quaternion.Euler(0, targetRotation.eulerAngles.y, 0) * horizontalPartForward, // Target rotation
                    turnSpeed * Time.deltaTime // Rotation step per frame
                );
            }

            return true;
        }

        /// <summary>
        /// Sets the rotation of this unit instantly. Returns false if rotation is not possible (LookPosition == position).
        /// </summary>
        /// <param name="targetPosition">Position this unit should look at.</param>
        /// <returns></returns>
        public bool LookAtInstant(Vector2 targetPosition)
        {
            return LookAtInstant(new Vector3(targetPosition.x, transform.position.y, targetPosition.y));
        }

        /// <summary>
        /// Sets the rotation of this unit instantly. Returns false if rotation is not possible (LookPosition == position).
        /// </summary>
        /// <param name="targetPosition">Position this unit should look at.</param>
        /// <returns></returns>
        public bool LookAtInstant(Vector3 targetPosition)
        {
            Quaternion targetRotation = Quaternion.identity;

            // Calculate rotation
            if (verticalPart != null)
            {
                Vector3 direction = targetPosition - verticalPart.position;
                if (direction != Vector3.zero) targetRotation = Quaternion.LookRotation(direction);
                else return false;
            }
            else if (horizontalPart != null)
            {
                Vector3 direction = new Vector3(targetPosition.x, 0, targetPosition.z) - new Vector3(horizontalPart.position.x, 0, horizontalPart.position.z);
                if (direction != Vector3.zero) targetRotation = Quaternion.LookRotation(direction);
                else return false;
            }

            // Set rotation
            if (verticalPart != null) verticalPart.localRotation = Quaternion.Euler(targetRotation.eulerAngles.x, 0, 0);
            if (horizontalPart != null) horizontalPart.rotation = Quaternion.Euler(0, targetRotation.eulerAngles.y, 0) * horizontalPartForward;

            return true;
        }

        /// <summary>
        /// Resets the unit`s rotation to its initial values.
        /// </summary>
        private void ResetUnitRotation()
        {
            // Reset vertical part rotation at the specified speed
            if (verticalPart != null)
            {
                verticalPart.localRotation = Quaternion.RotateTowards(
                    verticalPart.localRotation, // Current rotation
                    Quaternion.Euler(0, 0, 0), // Target rotation
                    turnSpeed * Time.deltaTime // Rotation step per frame
                );
            }

            // Reset horizontal part rotation at the specified speed
            if (horizontalPart != null)
            {
                // Reset values, when rotation is manual, are agent`s steeringTarget
                Vector3 dir;
                if (isAir) dir = new Vector3(agent.steeringTarget.x, 0, agent.steeringTarget.z) - new Vector3(horizontalPart.transform.position.x + Utils.airOffsetX, 0, horizontalPart.transform.position.z);
                else if (isInvisible) dir = new Vector3(invisibleAgent.steeringTarget.x, 0, invisibleAgent.steeringTarget.z) - new Vector3(horizontalPart.transform.position.x, 0, horizontalPart.transform.position.z + Utils.invisibilityOffsetY);
                else dir = new Vector3(agent.steeringTarget.x, 0, agent.steeringTarget.z) - new Vector3(horizontalPart.transform.position.x, 0, horizontalPart.transform.position.z);

                if (dir == Vector3.zero) return;

                Quaternion targetRotation = Quaternion.LookRotation(dir.normalized);
                horizontalPart.rotation = Quaternion.RotateTowards(
                    horizontalPart.rotation, // Current rotation
                    Quaternion.Euler(0, targetRotation.eulerAngles.y, 0) * horizontalPartForward, // Target rotation
                    turnSpeed * Time.deltaTime // Rotation step per frame
                );
            }

            return;
        }

        /// <summary>
        /// Sets the unit`s horizontal rotation(Y axis). Every unit should be set up so that horizontal rotation is in Y axis. Will not set the rotation if horizontalPart is not defined.
        /// </summary>
        /// <param name="value">Desired rotation of the unit in Y axis.</param>
        public void SetUnitRotation(float value)
        {
            // Horizontal part must be set
            if (horizontalPart == null) return;

            // Initialize if not initialized
            if (!horizontalPartSet)
            {
                if (horizontalPart != transform) horizontalPartForward = horizontalPart.rotation;
                else horizontalPartForward = Quaternion.Euler(0, 0, 0);
                horizontalPartSet = true;
            }

            // Set the rotation
            horizontalPart.rotation = Quaternion.Euler(0, value, 0) * horizontalPartForward;
        }

        /// <summary>
        /// Returns the unit`s horizontal rotation(Y axis). Returns 0 if horizontalPart is not defined.
        /// </summary>
        /// <returns></returns>
        public float GetUnitRotation()
        {
            if (horizontalPart != null)
            {
                Quaternion originalRotation = horizontalPart.rotation * Quaternion.Inverse(horizontalPartForward);
                return originalRotation.eulerAngles.y;
            }
            else return 0;
        }
    }
}
