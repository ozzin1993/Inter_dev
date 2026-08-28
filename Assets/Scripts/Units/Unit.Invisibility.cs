using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // This script is responsible for handling invisibility of the unit

    public partial class Unit
    {
        // Invisibility
        [HideInInspector] public bool isInvisible = false; // If this unit is currently invisible
        [HideInInspector] public bool[] canBeSeen; // If can be seen by team, index is team index
        [HideInInspector] public int[] canBeSeenCount; // How many effectors are currently making this unit visible
        private Transform invisibilityReplica; // Replicated gameobject that exists in invisiblity navmesh world
        [HideInInspector] public NavMeshAgent invisibleAgent; // Reference to invisibility agent

        /// <summary>
        /// Sets the invisibility of this unit.
        /// </summary>
        /// <param name="invisibility">Invisible or not.</param>
        public void SetInvisibility(bool invisibility)
        {
            if (invisibility)
            {
                if (isInvisible) return;
                // Make invisible
                isInvisible = true;
                if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.InvisibilitySetSend(this, true);
                SetOverlayColor(StateColors.Invisibility, false);

                // Hide renderers if enemy team and can not be seen by current team
                if (!IsVisible(SlotManager.Instance.currentTeam)) HideRenderers();

                if (!NetworkConnectionHandler.isClient)
                {
                    // If agent is null, try to create it
                    if (invisibleAgent == null) CreateInvisibilityReplica();
                    // If agent was not created it means it is a static unit
                    if (invisibleAgent)
                    {
                        if (isMoving)
                        {
                            // Set rotation and position
                            SetInvisiblePosition();
                            // Turn off position update on this one
                            agent.enabled = false;
                            // If we are moving we set the destination on invisAgent
                            currentDestination = new Vector3(currentDestination.x, currentDestination.y, currentDestination.z + Utils.invisibilityOffsetY);
                            invisibleAgent.destination = currentDestination;
                        }
                        else
                        {
                            obstacle.enabled = false;
                        }
                    }

                    // If we are in an attack state, we no longer should do that
                    if (unitState == UnitStates.Attack) Idle();
                }
            }
            else
            {
                if (!isInvisible) return;
                // Make visible
                isInvisible = false;
                if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.InvisibilitySetSend(this, false);
                ResetOverlayColor();

                if (!NetworkConnectionHandler.isClient)
                {
                    if (isMoving)
                    {
                        currentDestination = new Vector3(currentDestination.x, currentDestination.y, currentDestination.z - Utils.invisibilityOffsetY);
                        transform.position = new Vector3(invisibilityReplica.position.x, invisibilityReplica.position.y, invisibilityReplica.position.z - Utils.invisibilityOffsetY);
                        // horizontalPart.rotation = invisibilityReplica.rotation * horizontalPartForward;
                        // Turn on position update on this one
                        agent.enabled = true;
                        // Set destination
                        agent.destination = currentDestination;
                        if (invisibleAgent) invisibleAgent.ResetPath();
                    }
                    else
                    {
                        obstacle.enabled = true;
                    }
                }

                // Remove all effectors that are making this unit invisible
                bool effectorRemoved = false;
                for (int i = effectors.Count - 1; i >= 0; i--)
                {
                    if (effectors[i].effector.makeInvisible)
                    {
                        effectorRemoved = true;
                        if (effectors[i].effector.VFX != null) RemoveVFX(effectors[i].effector.VFX);
                        effectors.RemoveAt(i);
                    }
                }
                if (effectorRemoved) OnStatusUpdate?.Invoke();

                if (FogOfWar.Instance.IsVisible(FoWCell, SlotManager.Instance.currentTeam)) ShowRenderers();
            }
        }

        /// <summary>
        /// Used to set if this unit visible to various teams. Sets the canBeSeenCount of this unit for the spcified team. 
        /// </summary>
        /// <param name="seen">Can this unit be seen?</param>
        /// <param name="teamIndex">Which team.</param>
        public void CanBeSeen(bool seen, int teamIndex)
        {
            if (seen)
            {
                // Make arrays if null
                if (canBeSeen.Length == 0)
                {
                    canBeSeen = new bool[Enum.GetNames(typeof(Teams)).Length];
                    canBeSeenCount = new int[Enum.GetNames(typeof(Teams)).Length];
                }

                // Set visibility
                canBeSeen[teamIndex] = true;
                canBeSeenCount[teamIndex]++;

                // This will determine if we show or hide renderers, only decided for the player`s team
                if (FogOfWar.Instance.IsVisible(FoWCell, SlotManager.Instance.currentTeam)) ShowRenderers();
            }
            else
            {
                canBeSeenCount[teamIndex]--;
                // No longer visible
                if (canBeSeenCount[teamIndex] == 0)
                {
                    canBeSeen[teamIndex] = false;
                    if (teamIndex == SlotManager.Instance.currentTeam && isInvisible)
                    {
                        HideRenderers();
                    }
                }
            }
        }

        /// <summary>
        /// Returns if this is unit visible to the specified team.
        /// </summary>
        /// <param name="teamIndex">Is unit visible to this team.</param>
        /// <returns></returns>
        public bool IsVisible(int teamIndex)
        {
            if (teamIndex == team) return true;
            if (!isInvisible) return true;
            else if (canBeSeen.Length != 0 && canBeSeen[teamIndex]) return true;
            else return false;
        }

        /// <summary>
        /// Creates the invisibility replica and agent. Only non-air units that can move have invisibility agent replica.
        /// </summary>
        /// <returns>If invisibility replica creation was successful.</returns>
        public bool CreateInvisibilityReplica()
        {
            if (unitType == UnitType.Unit && canMove && !isAir)
            {
                invisibilityReplica = new GameObject(unitName + "Invisibility").transform;
                invisibilityReplica.position = this.transform.position + new Vector3(0, 0, Utils.invisibilityOffsetY);

                invisibleAgent = invisibilityReplica.gameObject.AddComponent<NavMeshAgent>();
                invisibleAgent.radius = unitRadius;
                invisibleAgent.height = unitHeight;
                invisibleAgent.speed = moveSpeed;
                invisibleAgent.acceleration = acceleration;
                invisibleAgent.angularSpeed = turnSpeed;
                invisibleAgent.autoBraking = false;
                invisibleAgent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                invisibleAgent.updateRotation = false;

                return true;
            }
            return false;
        }

        /// <summary>
        /// Copies the unit`s position to its invisibility replica.
        /// </summary>
        public void SetInvisiblePosition()
        {
            invisibilityReplica.position = transform.position + new Vector3(0, 0, Utils.invisibilityOffsetY);
            invisibilityReplica.rotation = horizontalPart.rotation; // * Quaternion.Inverse(horizontalPartForward);
        }
    }
}
