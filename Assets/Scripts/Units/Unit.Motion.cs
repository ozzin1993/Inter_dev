// [Interflow fix 2026-06-27] Все подсказки [Tooltip] в этом файле локализованы на русский (правка ассета, разрешена Artsiom; только текст Tooltip). Оригинал EN: _BACKUP_TOOLTIPS/Scripts/Unit.cs. Реестр: wiki concepts/asset-fork-debt.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Unit.Motion.cs — навигация (NAVMESH + WAYPOINT). Вырезано 1:1 из Unit.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class Unit
    {
        // ============================= NAVMESH ==============================================================================

        /// <summary>
        /// Sets the destination for the unit to go to.
        /// </summary>
        /// <param name="dest">Desired destination.</param>
        /// <param name="overrideCommand">Nulls the current target.</param>
        /// <param name="stopDist">At what distance from the destination unit should stop. 0 for positions, sum of two units if following a unit.</param>
        public void SetDestination(Vector2 dest, bool overrideCommand = false, float stopDist = 0)
        {
            // [Interflow fix 2026-06-26] При смерти юнита SetDestination может прийти, когда NavMeshAgent уже уничтожен/снят → NRE (set_stoppingDistance/SetDestination). Защитный выход.
            if (agent == null) return;
            if (canMove)
            {
                // Change destination
                if (isAir)
                {
                    currentDestination = new Vector3(dest.x + Utils.airOffsetX, 0, dest.y);
                    if (isMoving) agent.SetDestination(currentDestination);
                    else MakeAgent(true);
                }
                else
                {
                    // Calculate new destination
                    float terrainHeight = Utils.GetTerrainHeight(dest, Utils.terrainMaskVisuals);
                    if (terrainHeight == -9999f)
                    {
                        Debug.LogWarning("Invalid destination, should not happen!");
                        currentDestination = transform.position;
                    }
                    else
                    {
                        // Set destination
                        if (isInvisible && invisibleAgent)
                        {
                            currentDestination = new Vector3(dest.x, terrainHeight, dest.y + Utils.invisibilityOffsetY);
                            if (isMoving) invisibleAgent.SetDestination(currentDestination);
                            else MakeAgent(true);
                        }
                        else
                        {
                            currentDestination = new Vector3(dest.x, terrainHeight, dest.y);
                            if (isMoving) agent.SetDestination(currentDestination);
                            else MakeAgent(true);
                        }
                    }
                }

                if (overrideCommand)
                {
                    if (target != null) target.OnReferenceChange -= TargetReferenceChange;
                    target = null;
                }
                stopDistance = stopDist;
                agent.stoppingDistance = 0; // Always 0
            }
        }

        /// <summary>
        /// Sets the destination for the unit to go to.
        /// </summary>
        /// <param name="dest">Desired destination.</param>
        /// <param name="overrideCommand">Nulls the current target.</param>
        /// <param name="stopDist">At what distance from the destination unit should stop. 0 for positions, sum of two units if following a unit.</param>
        public void SetDestination(Vector3 dest, bool overrideCommand = false, float stopDist = 0)
        {
            if (canMove)
            {
                // Change destination
                if (isAir)
                {
                    currentDestination = new Vector3(dest.x + Utils.airOffsetX, dest.y, dest.z);
                    if (isMoving) agent.SetDestination(currentDestination);
                    else MakeAgent(true);
                }
                else if (isInvisible && invisibleAgent)
                {
                    currentDestination = new Vector3(dest.x, dest.y, dest.z + Utils.invisibilityOffsetY);
                    if (isMoving) invisibleAgent.SetDestination(currentDestination);
                    else MakeAgent(true);
                }
                else
                {
                    currentDestination = dest;
                    if (isMoving) agent.SetDestination(currentDestination);
                    else MakeAgent(true);
                }

                if (overrideCommand)
                {
                    target = null;
                }
                stopDistance = stopDist;
                agent.stoppingDistance = 0; // Always 0
            }
        }

        /// <summary>
        /// Makes the unit either dynamic or static.
        /// </summary>
        /// <param name="_dynamic">Should this unit be able to move.</param>
        private void MakeAgent(bool _dynamic)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (obstacle == null || agent == null) return; // [Interflow fix 2026-06-26] компоненты уничтожены при Die/Destroy — не лезть в MissingReference

            if (_dynamic)
            {
                if (!isMoving && waitTwoUpdates == 0)
                {
                    // Make this unit dynamic
                    obstacle.enabled = false;
                    waitTwoUpdates = 2;
                }
            }
            else
            {
                if (isMoving)
                {
                    // Grid data update
                    Grid.AssignToChunk(this);
                    FogOfWar.Instance.CellAssignment(this);

                    // We make this unit static
                    agent.enabled = false;
                    if (!isInvisible || !invisibleAgent) obstacle.enabled = true;
                    isMoving = false;

                    if (m_walkAnimationPlaying)
                    {
                        AnimatorSetBool(AnimationState.Walk, false);
                        m_walkAnimationPlaying = false;
                    }

                    if (NetworkManager.Singleton.IsServer)
                    {
                        if (!positionsSent)
                        {
                            // We do not remove, since we first have to send the position
                            NetworkDataSync.Instance.removeSyncList.Add(netID);
                            removeFromPosSync = true;
                        }
                        else
                        {
                            NetworkDataSync.Instance.positionSyncList.Remove(netID);
                            NetworkDataSync.Instance.removeSyncList.Add(netID);
                        }
                    }
                }
                if (waitTwoUpdates != 0) waitTwoUpdates = 0;
            }
        }

        /// <summary>
        /// Before making the unit dynamic we must wait 2 frame to avoid jumping. Called in Update().
        /// </summary>
        private void MakeAgent_WaitTwoFrames()
        {
            if (waitTwoUpdates != 0)
            {
                waitTwoUpdates -= 1;
                if (waitTwoUpdates == 0)
                {
                    if (!stunned) currentActionTime = 0;
                    isMoving = true;

                    if (isAir || !isInvisible || !invisibleAgent)
                    {
                        agent.enabled = true;
                        agent.SetDestination(currentDestination);
                    }
                    else
                    {
                        SetInvisiblePosition();
                        invisibleAgent.SetDestination(currentDestination);
                    }

                    if (!m_walkAnimationPlaying)
                    {
                        m_walkAnimationPlaying = true;
                        AnimatorSetBool(AnimationState.Walk, true);
                    }

                    if (NetworkManager.Singleton.IsServer)
                    {
                        NetworkDataSync.Instance.positionSyncList.Add(netID);
                        positionsSent = false;
                    }
                }
            }
        }

        // ============================= WAYPOINT ==============================================================================

        /// <summary>
        /// Command to set the unit as the waypoint for the building.
        /// </summary>
        /// <param name="unit">Waypoint unit.</param>
        public void SetWaypoint(Unit unit)
        {
            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.Instance.SetWaypointCommandSend(this, unit);
                return;
            }

            if (waypointUnit == unit)
            {
                // Same unit, remove waypoint
                waypointUnit = null;
                waypointLocation = Vector2.zero;
            }
            else
            {
                if (team == unit.team || unit.team == (int)Teams.NeutralPassive)
                {
                    // New unit
                    waypointUnit = unit;
                    waypointLocation = Vector2.zero;
                }
                else
                {
                    // Enemy unit, set position
                    waypointLocation = new Vector2(unit.transform.position.x, unit.transform.position.z);
                    waypointUnit = null;
                }
            }

            WaypointUpdate?.Invoke();
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.WaypointSet(this, waypointUnit, waypointLocation);
        }

        /// <summary>
        /// Command to set the position as the waypoint for the building.
        /// </summary>
        /// <param name="position">Waypoint position.</param>
        public void SetWaypoint(Vector2 position)
        {
            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.Instance.SetWaypointCommandSend(this, position);
                return;
            }

            if (waypointLocation != Vector2.zero)
            {
                // Check distance, and remove if necessary
                if (Vector3.Distance(waypointLocation, position) < 0.02f)
                {
                    waypointLocation = Vector2.zero;
                }
                else waypointLocation = position;
            }
            else waypointLocation = position;

            waypointUnit = null;

            WaypointUpdate?.Invoke();
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.WaypointSet(this, waypointUnit, waypointLocation);
        }

        /// <summary>
        /// Directly sets the waypoint, not synced.
        /// </summary>
        /// <param name="unit">Waypoint unit.</param>
        /// <param name="position">Waypoint position.</param>
        public void SetWaypointDirect(Unit unit, Vector2 position)
        {
            waypointUnit = unit;
            waypointLocation = position;

            WaypointUpdate?.Invoke();
        }

        /// <summary>
        /// For Units waypoints are follow / move points called at Start().
        /// </summary>
        public void GoToWaypoint()
        {
            if (!NetworkConnectionHandler.isClient)
            {
                if (canMove)
                {
                    if (waypointLocation != Vector2.zero)
                    {
                        if (doNotLookForTargets) Move(waypointLocation);
                        else AttackMove(waypointLocation);
                    }
                    else if (waypointUnit != null)
                    {
                        if (waypointUnit.team != team && waypointUnit.team != (int)Teams.NeutralPassive) AttackVerify(waypointUnit);
                        else Follow(waypointUnit);
                    }
                }
            }

            waypointLocation = Vector2.zero;
            waypointUnit = null;
        }

        /// <summary>
        /// Called when subscribed to active unit to show the waypoint
        /// </summary>
        public void ShowWaypoint()
        {
            if (!isWaypoint) return;

            ReferenceManager.Instance.ShowWaypoint(waypointLocation, waypointUnit);
        }

    }
}
