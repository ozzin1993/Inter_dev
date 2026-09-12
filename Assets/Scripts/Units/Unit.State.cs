// [Interflow fix 2026-06-27] Все подсказки [Tooltip] в этом файле локализованы на русский (правка ассета, разрешена Artsiom; только текст Tooltip). Оригинал EN: _BACKUP_TOOLTIPS/Scripts/Unit.cs. Реестр: wiki concepts/asset-fork-debt.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Unit.State.cs — машина состояний (STATE LOGIC + STATE SET). Вырезано 1:1 из Unit.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class Unit
    {
        // ============================= STATE LOGIC ==============================================================================

        /// <summary>
        /// State handler - server only.
        /// </summary>
        private void StateUpdate()
        {
            // Only for server
            if (NetworkConnectionHandler.isClient) return;

            // Certain operations are done when unit is moving
            if (isMoving)
            {
                float currentMagnitude;

                // We sync with air and invisible units if needed
                if (isAir)
                {
                    // Copy transform from air replica if air unit and is moving
                    transform.position = new Vector3(airReplica.position.x - Utils.airOffsetX, Utils.airUnitElevation, airReplica.position.z);
                    currentMagnitude = agent.velocity.magnitude;
                }
                if (!isAir && isInvisible)
                {
                    // Copy transform invisible unit
                    transform.position = new Vector3(invisibilityReplica.position.x, invisibilityReplica.position.y, invisibilityReplica.position.z - Utils.invisibilityOffsetY);
                    currentMagnitude = invisibleAgent.velocity.magnitude;
                }
                else
                {
                    currentMagnitude = agent.velocity.magnitude;
                }

                // Animation off if we are not moving
                if (GameManager.instance.tickThisFrame)
                {
                    if (currentMagnitude < 0.05f)
                    {
                        if (m_walkAnimationPlaying)
                        {
                            m_walkAnimationPlaying = false;
                            AnimatorSetBool(AnimationState.Walk, false);
                        }
                    }
                    else
                    {
                        if (!m_walkAnimationPlaying)
                        {
                            m_walkAnimationPlaying = true;
                            AnimatorSetBool(AnimationState.Walk, true);
                        }
                    }
                }

                // Move animation speed
                float currentSpeed = currentMagnitude / moveSpeed;
                if (currentSpeed < 0.5f)
                {
                    if (currentSpeed < 0.1f)
                    {
                        // Agent's destination sometimes does not update, fix it
                        if (GameManager.instance.tickThisFrame)
                        {
                            if (unitState == UnitStates.Move || unitState == UnitStates.AttackMove)
                            {
                                if (target == null)
                                {
                                    Vector3 destionationPos = (unitState == UnitStates.Move) ? currentDestination : new Vector3(targetPosition.x, 0, targetPosition.y);
                                    Vector3 agentPosition;

                                    if (isAir) agentPosition = agent.transform.position;
                                    else if (isInvisible && invisibleAgent) agentPosition = invisibleAgent.transform.position;
                                    else agentPosition = agent.transform.position;

                                    NavMeshHit hit;
                                    if (NavMesh.SamplePosition(destionationPos, out hit, 25f, NavMesh.AllAreas))
                                    {
                                        destionationPos = (hit.position != Vector3.zero) ? hit.position : destionationPos;

                                        NavMeshPath path = new NavMeshPath();
                                        bool foundPath = NavMesh.CalculatePath(agentPosition, destionationPos, NavMesh.AllAreas, path);

                                        if (path.status != NavMeshPathStatus.PathInvalid)
                                        {
                                            float length = 0f;
                                            if (path.corners.Length > 1)
                                            {
                                                for (int i = 0; i < path.corners.Length - 1; i++)
                                                {
                                                    length += Vector3.Distance(path.corners[i], path.corners[i + 1]);
                                                }
                                            }

                                            if (length < unitRadius * 2)
                                                Idle();
                                        }
                                    }

                                    if (unitState != UnitStates.Idle)
                                    {
                                        // Fix destination mismatch
                                        if (isAir || !isInvisible || !invisibleAgent)
                                        {
                                            if (Vector2.SqrMagnitude(new Vector2(agent.destination.x, agent.destination.z) - new Vector2(destionationPos.x, destionationPos.z)) > 0.01f)
                                            {
                                                agent.destination = destionationPos;
                                            }
                                        }
                                        else // Inivisible
                                        {
                                            if (Vector2.SqrMagnitude(new Vector2(invisibleAgent.destination.x, invisibleAgent.destination.z) - new Vector2(destionationPos.x, destionationPos.z)) > 0.01f)
                                                invisibleAgent.SetDestination(destionationPos);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Fix destination mismatch
                                if (isAir || !isInvisible || !invisibleAgent)
                                {
                                    if (Vector2.SqrMagnitude(new Vector2(agent.destination.x, agent.destination.z) - new Vector2(transform.position.x, transform.position.z)) < 0.01f)
                                        agent.SetDestination(currentDestination);
                                }
                                else // Inivisible
                                {
                                    if (Vector2.SqrMagnitude(new Vector2(invisibleAgent.destination.x, invisibleAgent.destination.z) - new Vector2(invisibleAgent.transform.position.x, invisibleAgent.transform.position.z)) < 0.01f)
                                        invisibleAgent.SetDestination(currentDestination);
                                }
                            }
                        }
                    }

                    currentSpeed = 0.5f;
                }

                if (animator)
                {
                    if (animationMoveSpeed != 0) animator.SetFloat("movespeed", currentSpeed / animationMoveSpeed);
                    else animator.SetFloat("movespeed", currentSpeed);
                }

                // When moving we must reset the child rotation of the unit
                ResetUnitRotation();

                // Update chunk and FoW info
                Grid.AssignToChunk(this);
                FogOfWar.instance.CellAssignment(this);
            }

            if (unitState == UnitStates.AbilityCasting)
            {
                // Check target visibility
                if (activeAbilityUnit)
                {
                    if (!FogOfWar.instance.IsVisible(activeAbilityUnit.FoWCell, team) || !activeAbilityUnit.IsVisible(team))
                    {
                        Idle();
                        return;
                    }
                }

                // Check distance - we check the distance only before playCast or for activeAbilityInUse
                if (activeAbilityRange != 0)
                {
                    bool distanceGood = true;
                    // When playcast is activated we should check the distance 1.5x
                    if (playCast && !activeAbilityInUse)
                    {
                        if ((activeAbilityUnit && (Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(activeAbilityUnit.transform.position.x, activeAbilityUnit.transform.position.z)) > activeAbilityRange * Utils.activeDistanceMultiplier))
                        || (activeAbilityLocation != Vector3.zero && (Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(activeAbilityLocation.x, activeAbilityLocation.z)) > activeAbilityRange * Utils.activeDistanceMultiplier)))
                        {
                            distanceGood = false;
                        }
                    }
                    else
                    {
                        if ((activeAbilityUnit && (Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(activeAbilityUnit.transform.position.x, activeAbilityUnit.transform.position.z)) > activeAbilityRange))
                        || (activeAbilityLocation != Vector3.zero && (Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(activeAbilityLocation.x, activeAbilityLocation.z)) > activeAbilityRange)))
                        {
                            distanceGood = false;
                        }
                    }

                    if (!distanceGood)
                    {
                        // We were actively using an ability, end it
                        if (activeAbilityInUse || !canMove) { Idle(); return; }

                        // Goal is too far
                        // Reset cast time and Follow unit or go to location
                        if (isMoving)
                        {
                            // Follow unit
                            if (activeAbilityUnit) FollowUpdate();
                        }
                        else
                        {
                            playCast = false; // Reset the bool about casting being played out
                            currentActionTime = 0;

                            if (sendToClients)
                            {
                                sendToClients = false;
                                if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.AbilityStopCastSend(this);
                            }

                            if (activeAbilityUnit)
                            {
                                // Set unit as target
                                target = activeAbilityUnit;
                                MakeAgent(true);
                            }
                            else
                            {
                                // Go To ability location
                                SetDestination(activeAbilityLocation);
                            }
                        }

                        return;
                    }
                }

                // Make sure we are stopped
                MakeAgent(false);

                // First time sending info to clients to start rotating towards target. When server starts to cast any ability, we send data to clients
                if (!sendToClients && NetworkManager.Singleton.IsServer)
                {
                    NetworkDataSync.instance.AbilityCastStartSend(this, activeAbility, activeAbilityUnit, activeAbilityLocation);
                    sendToClients = true;
                }

                // Check and Set Rotation
                if (horizontalPart)
                {
                    if (activeAbilityUnit)
                    {
                        if (!activeAbility.dontTurn && !playCast && LookAt(activeAbilityUnit.transform.position))
                        {
                            Vector3 horizontalPartDir = Quaternion.Euler(horizontalPart.rotation.eulerAngles - horizontalPartForward.eulerAngles) * Vector3.forward;
                            float dot = Vector3.Dot(horizontalPartDir.normalized, (new Vector3(activeAbilityUnit.transform.position.x, 0, activeAbilityUnit.transform.position.z) - new Vector3(horizontalPart.position.x, 0, horizontalPart.position.z)).normalized);
                            if (dot < 0.97f) return;
                        }
                    }
                    else if (activeAbilityLocation != Vector3.zero)
                    {
                        if (!activeAbility.dontTurn && !playCast && LookAt(activeAbilityLocation))
                        {
                            Vector3 horizontalPartDir = Quaternion.Euler(horizontalPart.rotation.eulerAngles - horizontalPartForward.eulerAngles) * Vector3.forward;
                            float dot = Vector3.Dot(horizontalPartDir.normalized, (new Vector3(activeAbilityLocation.x, 0, activeAbilityLocation.z) - new Vector3(horizontalPart.position.x, 0, horizontalPart.position.z)).normalized);
                            if (dot < 0.97f) return;
                        }
                    }
                }

                // First time casting, send info to clients and play animation
                if (playCast == false)
                {
                    // Animation
                    if (hasCastAnim && FoWVisible) animator.CrossFade("cast", crossFadeTime, 0, 0f);
                    playCast = true;
                }

                // Adjust time
                currentActionTime += Time.deltaTime;

                // Check cast time
                if (!activeAbilityInUse && activeAbilityCastTime != 0 && currentActionTime < activeAbilityCastTime)
                {
                    return;
                }

                // Time to use ability
                if (activeAbilityInUse)
                {
                    // Check the mana costs
                    float manaCost = 0;
                    if (activeAbility.manaCostPerSecond.Length > activeAbilityLevel) manaCost = activeAbility.manaCostPerSecond[activeAbilityLevel] * Time.deltaTime;

                    if (muted || mana < manaCost || (activeAbilityDuration != 0 && currentActionTime > activeAbilityDuration))
                    {
                        // Ability can not be used
                        Idle();
                        return;
                    }
                    else
                    {
                        // Ability can be used, Using actively
                        if (manaCost != 0) ChangeMP(-manaCost);

                        if (activeAbilityUnit) activeAbility.Use(this, this.owner, activeAbilityLevel, activeAbilityUnit, ref activeAbilityVFX);
                        else if (activeAbilityLocation != Vector3.zero) activeAbility.Use(this, this.owner, activeAbilityLevel, activeAbilityLocation, ref activeAbilityVFX);
                        else activeAbility.Use(this, this.owner, activeAbilityLevel, ref activeAbilityVFX);
                    }
                }
                else
                {
                    // Check the cost
                    if ((activeAbilityItem && items[activeAbilityIndex] == null) || CheckAbilityItemRequirements(owner, activeAbilityIndex, activeAbilityItem, activeAbility)) { Idle(); return; }

                    // Wait till mute wears off
                    if (!muted)
                    {
                        UseAbilityImmediately(activeAbility, activeAbilityLevel, activeAbilityIndex, activeAbilityItem, activeAbilityUnit, activeAbilityLocation, true);
                    }
                }
            }
            if (unitState == UnitStates.Idle && !doNotLookForTargets)
            {
                // In Idle state if can attack unit will be searching for enemy unit at reaction range
                // When target is out of the range or unit is too far from the initial place (reaction range x5) it will return to its origin location
                // If cant attack it will just stay in place

                // isTargetGround - when we are following the last known position of the target
                // targetPosition - last known position of the target
                // initialPosition - position at which the unit was standing before acquiring the new target

                if (!isInvisible && canAttack)
                {
                    if (isTargetGround)
                    {
                        // Substate: Going to last known target position
                        // Currently target not visible, we are going to the last known target position
                        // If along the way unit is attacked we then will go to attack that unit
                        if (isMoving)
                        {
                            if (target != null && FogOfWar.instance.IsVisible(target.FoWCell, team) && target.IsVisible(team))
                            {
                                // Target is visible, we go to hit it
                                isTargetGround = false;
                                targetPosition = new Vector2(target.transform.position.x, target.transform.position.z);
                            }
                            // Target is not visible or null
                            else if (agent.ReachedDestination(currentDestination, stopDistance, out bool successfulReach))
                            {
                                // Either reached, or could not reach the target position
                                // Search for enemy nearby, if not found return to initial position
                                isTargetGround = false;
                                if (target != null) target.OnReferenceChange -= TargetReferenceChange;

                                // Search for a new target
                                if (attackRange > reactionRange || !canMove) target = InterflowTargeting.PickWithPriority(this, attackRange, searchUnitSelector, true); // [Interflow fix 2026-07-10 target-priority]
                                else target = InterflowTargeting.PickWithPriority(this, reactionRange, searchUnitSelector, true); // [Interflow fix 2026-07-10 target-priority]

                                if (target != null)
                                {
                                    // New target found
                                    targetPosition = new Vector2(target.transform.position.x, target.transform.position.z);
                                    stopDistance = 0;
                                    target.OnReferenceChange += TargetReferenceChange;
                                }
                                else if (canMove && initialPosition != Vector2.zero)
                                {
                                    // No target found, Go back to initialPosition
                                    Move(initialPosition);
                                }
                            }
                        }
                    }
                    else if (target == null)
                    {
                        // Substate: Looking for a new target
                        if (!firstAttack) AttackStop();

                        // Search
                        if (attackRange > reactionRange || !canMove) target = InterflowTargeting.PickWithPriority(this, attackRange, searchUnitSelector, true); // [Interflow fix 2026-07-10 target-priority]
                        else target = InterflowTargeting.PickWithPriority(this, reactionRange, searchUnitSelector, true); // [Interflow fix 2026-07-10 target-priority]

                        if (target != null)
                        {
                            // New target found. Set initial position for return if enemy is too far
                            if (initialPosition == Vector2.zero) initialPosition = new Vector2(transform.position.x, transform.position.z);
                            targetPosition = new Vector2(target.transform.position.x, target.transform.position.z);
                            stopDistance = 0;
                            target.OnReferenceChange += TargetReferenceChange;
                        }
                        else if (canMove && initialPosition != Vector2.zero)
                        {
                            // No target, return to initial position if exists
                            Move(initialPosition);
                            return;
                        }
                    }
                    else
                    {
                        // Target not visible, Go to last known target position
                        if (!FogOfWar.instance.IsVisible(target.FoWCell, team) || !target.IsVisible(team))
                        {
                            if (!firstAttack) AttackStop();
                            if (canMove)
                            {
                                // If can move we do not null the target, we try to reach its position and attack it
                                SetDestination(targetPosition);
                                isTargetGround = true;
                            }
                            else
                            {
                                // When we can not move, not visible unit is no longer of interest, Null the target
                                target.OnReferenceChange -= TargetReferenceChange;
                                target = null;
                            }
                        }
                        else
                        {
                            // Target is visible
                            float distanceToTarget = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(target.transform.position.x, target.transform.position.z));
                            float distanceToOrigin = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), initialPosition);
                            targetPosition = new Vector2(target.transform.position.x, target.transform.position.z);

                            if (distanceToTarget > visionRange || distanceToOrigin > visionRange)
                            {
                                // Return to initial position, target or origin is too far
                                target.OnReferenceChange -= TargetReferenceChange;
                                target = null;
                                isTargetGround = false;
                                if (!firstAttack) AttackStop();
                                if (canMove)
                                {
                                    Move(initialPosition);
                                }

                            }
                            else if (!disarmed && AttackUpdate())
                            {
                                // Target is visible and at attack distance
                            }
                            else
                            {
                                // Target is not at attack distance, try to reach it
                                if (canMove)
                                {
                                    if (distanceToTarget >= target.unitRadius + attackRange)
                                    {
                                        // Enemy too far, follow him
                                        if (isMoving)
                                        {
                                            if (agent.ReachedDestination(currentDestination, stopDistance, out bool successfulReach))
                                            {
                                                if (!successfulReach) Move(initialPosition);
                                                else MakeAgent(false);
                                            }
                                            else FollowUpdate();
                                        }
                                        else
                                        {
                                            // We just entered the state, set the destination
                                            if (!firstAttack) AttackStop();
                                            SetDestination(target.transform.position, false, stopDistance);
                                        }
                                    }
                                }
                                else
                                {
                                    // This unit can not move. Null the target
                                    target.OnReferenceChange -= TargetReferenceChange;
                                    target = null;
                                    isTargetGround = false;
                                    if (!firstAttack) AttackStop();
                                }
                            }
                        }
                    }
                }
            }
            else if (unitState == UnitStates.Hold)
            {
                // In hold state unit will stay in place no matter what, and attack those who are at attack range
                if (!isInvisible && canAttack)
                {
                    if (target == null)
                    {
                        // If was actively attacking and the target has died, stop attacking
                        if (!firstAttack) AttackStop();

                        // Find new target
                        target = InterflowTargeting.PickWithPriority(this, attackRange, searchUnitSelector, true); // [Interflow fix 2026-07-10 target-priority]
                        if (target != null) target.OnReferenceChange += TargetReferenceChange;
                    }
                    else
                    {
                        if (!FogOfWar.instance.IsVisible(target.FoWCell, team) || !target.IsVisible(team))
                        {
                            // Target not visible
                            if (!firstAttack) AttackStop();

                            target.OnReferenceChange -= TargetReferenceChange;
                            target = null;
                        }
                        else
                        {
                            // Target is visible
                            if (!disarmed && AttackUpdate())
                            {
                                // Target is at attack distance
                            }
                            else
                            {
                                // Target is too far, null it
                                if (!firstAttack) AttackStop();

                                target.OnReferenceChange -= TargetReferenceChange;
                                target = null;
                            }
                        }
                    }
                }
            }
            else if (unitState == UnitStates.Move)
            {
                if (isMoving)
                {
                    if (isInvisible && invisibleAgent)
                    {
                        if (invisibleAgent.ReachedDestination(currentDestination, stopDistance, out bool successfulReach))
                        {
                            if (successfulReach)
                            {
                                OnPositionReach?.Invoke();
                                Idle();
                            }
                        }
                    }
                    else
                    {
                        if (agent.ReachedDestination(currentDestination, stopDistance, out bool successfulReach))
                        {
                            if (successfulReach)
                            {
                                OnPositionReach?.Invoke();
                                Idle();
                            }
                        }
                    }
                }
                else
                {
                    MakeAgent(true);
                }
            }
            else if (unitState == UnitStates.Follow)
            {
                // In follow state we try to reach friendly unit or static destructible. Following an enemy is impossible, it is done in the attack state

                if (canMove && target != null && target.IsVisible(team) && (target.unitType == UnitType.Tree || target.unitType == UnitType.StaticDestructible || FogOfWar.instance.IsVisible(target.FoWCell, team)))
                {
                    float distanceToTarget = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(target.transform.position.x, target.transform.position.z));

                    // Use reaction range
                    if (FogOfWar.instance.IsVisible(target.FoWCell, team) && ((stopDistance == 0 && (distanceToTarget < target.unitRadius + unitRadius + Utils.stopDistanceOffset || distanceToTarget < attackRange)) || distanceToTarget <= stopDistance))
                    {
                        if (!isMoving)
                        {
                            // Currently at target, rotate towards it
                            LookAt(target.transform.position);
                        }
                        else
                        {
                            // Reached target
                            OnFollowReach?.Invoke();
                            MakeAgent(false);
                            if (embarkFollow && !target.isBeingBuilt)
                            {
                                // Current unit is transport, embark followed unit
                                if (transportUnit && target.unitType == UnitType.Unit) transportUnit.Embark(target);
                                // Target is transport, embark this unit
                                else if (target.transportUnit && unitType == UnitType.Unit) target.transportUnit.Embark(this);
                            }
                        }
                    }
                    else
                    {
                        // Target not FoW visible or too far, follow it
                        if (!isMoving) MakeAgent(true);
                        FollowUpdate();
                    }
                }
                else
                {
                    Idle();
                }
            }
            else if (unitState == UnitStates.Attack)
            {
                // In an attack state we try to reach the target and then attack it, if target is dead or not visible we default to idle state
                // targetPosition - is the last known position of the target
                // initialPosition - indicates if target was visible or not visible when it became null
                // Attack the target
                if (!isTargetGround)
                {
                    // Target is either null or Target is not visible, we go to the last known target position
                    if (target == null)
                    {
                        if (!firstAttack) AttackStop();

                        // Go to the last known target position if we lost the target visibility while it was alive
                        if (initialPosition != Vector2.zero)
                        {
                            Move(targetPosition);
                        }
                        // Idle since target died while visible
                        else
                        {
                            Idle();
                        }
                    }
                    else
                    {
                        // Target exists, either visible or not visible
                        if (!FogOfWar.instance.IsVisible(target.FoWCell, team) || !target.IsVisible(team))
                        {
                            // Not visible: Stop the attack and go to the last known position
                            if (!firstAttack) AttackStop();
                            if (canMove && targetPosition != Vector2.zero)
                            {
                                SetDestination(targetPosition);
                                initialPosition = Vector2.one; // Inidicate that target is not visible

                                // Reach check: Only if position is fow visible
                                if (FogOfWar.instance.IsVisible(targetPosition, team))
                                {
                                    // Check if we have reached the position and if target either is null or not visible
                                    float distanceToTarget = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), targetPosition);

                                    if (melee && distanceToTarget < unitRadius + Utils.stopDistanceOffset || distanceToTarget < attackRange)
                                    {
                                        // No target
                                        Idle();
                                    }
                                }
                            }
                            else Idle();
                        }
                        else
                        {
                            // Target exists and is visible
                            // We try to reach and attack
                            targetPosition = new Vector2(target.transform.position.x, target.transform.position.z);
                            initialPosition = Vector2.zero;

                            if (!disarmed && AttackUpdate()) { }
                            else if (canMove)
                            {
                                // Target too far, follow it
                                if (!firstAttack) AttackStop();
                                if (!isMoving) MakeAgent(true);
                                FollowUpdate();
                            }
                            else
                            {
                                Idle();
                            }
                        }
                    }
                }
                // Attack the ground
                else // if (targetPosition != Vector2.zero)
                {
                    // Target position
                    float distanceToTarget = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), targetPosition);

                    if (FogOfWar.instance.IsVisible(targetPosition, team) && (melee && distanceToTarget < target.unitRadius + unitRadius + Utils.stopDistanceOffset || distanceToTarget < attackRange))
                    {
                        // Target reached
                        MakeAgent(false);
                        if (!disarmed) AttackUpdate();
                    }
                    else if (canMove)
                    {
                        // Target ground is too far, try to reach it
                        if (!firstAttack) AttackStop();
                        if (!isMoving)
                        {
                            MakeAgent(true);
                            SetDestination(targetPosition);
                        }
                    }
                    else
                    {
                        Idle();
                    }
                }
            }
            else if (unitState == UnitStates.AttackMove)
            {
                // In this state unit will move towards specified point and attack any unit within its reaction range along the way, if enemy unit goes too far returns to its main objective, to go to specified point
                // targetPositin - position towards which we must move and attack

                if (!canMove) Idle();

                if (target && !disarmed)
                {
                    float distanceToTarget = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(target.transform.position.x, target.transform.position.z));

                    // Go back to main objective
                    if (isInvisible || !FogOfWar.instance.IsVisible(target.FoWCell, team) || !target.IsVisible(team) || distanceToTarget > visionRange)
                    {
                        SetDestination(targetPosition, true);
                    }
                    // Implies that unit can attack, since idle follow is triggered only if unit can attack
                    else if (AttackUpdate()) { }
                    else
                    {
                        // Current target too far, try to reach it
                        if (!firstAttack) AttackStop();
                        if (!isMoving) MakeAgent(true);
                        FollowUpdate();
                    }
                }
                else
                {
                    // We were attacking the target, it has died. Go back to objective
                    if (firstAttack == false)
                    {
                        AttackStop();
                        SetDestination(targetPosition, true);
                    }

                    if (isMoving)
                    {
                        // We are moving towards the targetPoint

                        bool successfulReach;
                        if (isInvisible && invisibleAgent)
                        {
                            // Check if reached the destination
                            if (invisibleAgent.ReachedDestination(currentDestination, stopDistance, out successfulReach))
                            {
                                if (successfulReach)
                                {
                                    OnPositionReach?.Invoke();
                                    Idle();
                                }
                            }
                        }
                        else
                        {
                            // Search for enemies along the way
                            if (attackRange > reactionRange || !canMove) target = InterflowTargeting.PickWithPriority(this, attackRange, searchUnitSelector, true); // [Interflow fix 2026-07-10 target-priority]
                            else target = InterflowTargeting.PickWithPriority(this, reactionRange, searchUnitSelector, true); // [Interflow fix 2026-07-10 target-priority]

                            // New target found
                            if (target != null)
                            {
                                target.OnReferenceChange += TargetReferenceChange;
                            }

                            // Check if reached the destination
                            if (agent.ReachedDestination(currentDestination, stopDistance, out successfulReach))
                            {
                                if (successfulReach)
                                {
                                    OnPositionReach?.Invoke();
                                    Idle();
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// States handler - clients.
        /// </summary>
        private void NetworkStateUpdate()
        {
            if (NetworkConnectionHandler.isClient)
            {
                // Actively using ability
                if (activeAbilityInUse)
                {
                    // Check the mana costs
                    float manaCost = 0;
                    if (activeAbility.manaCostPerSecond.Length > activeAbilityLevel) manaCost = activeAbility.manaCostPerSecond[activeAbilityLevel] * Time.deltaTime;

                    // Ability can be used, Using actively
                    if (manaCost != 0) ChangeMP(-manaCost);

                    if (activeAbilityUnit)
                    {
                        activeAbility.Use(this, this.owner, activeAbilityLevel, activeAbilityUnit, ref activeAbilityVFX);
                        if (!activeAbility.dontTurn) LookAt(activeAbilityUnit.transform.position);
                    }
                    else if (activeAbilityLocation != Vector3.zero)
                    {
                        activeAbility.Use(this, this.owner, activeAbilityLevel, activeAbilityLocation, ref activeAbilityVFX);
                        if (!activeAbility.dontTurn) LookAt(activeAbilityLocation);
                    }
                    else activeAbility.Use(this, this.owner, activeAbilityLevel, ref activeAbilityVFX);
                }
                // Rotation update
                else if (activeAbilityCastTime != 0) // We use cast time as indicator that cast is currently being performed
                {
                    if (activeAbility.dontTurn && !playCast)
                    {
                        playCast = true;
                        if (hasCastAnim) animator.CrossFade("cast", crossFadeTime, 0, 0f);
                    }

                    // Casting an ability, rotate towards the point
                    if (activeAbilityUnit)
                    {
                        if (!activeAbility.dontTurn && !playCast && LookAt(activeAbilityUnit.transform.position))
                        {
                            Vector3 horizontalPartDir = Quaternion.Euler(horizontalPart.rotation.eulerAngles - horizontalPartForward.eulerAngles) * Vector3.forward;
                            float dot = Vector3.Dot(horizontalPartDir.normalized, (new Vector3(activeAbilityUnit.transform.position.x, 0, activeAbilityUnit.transform.position.z) - new Vector3(horizontalPart.position.x, 0, horizontalPart.position.z)).normalized);
                            if (dot > 0.97f)
                            {
                                playCast = true;
                                if (hasCastAnim) animator.CrossFade("cast", crossFadeTime, 0, 0f);
                            }
                        }
                    }
                    else if (activeAbilityLocation != Vector3.zero)
                    {
                        if (!activeAbility.dontTurn && !playCast && LookAt(activeAbilityLocation))
                        {
                            Vector3 horizontalPartDir = Quaternion.Euler(horizontalPart.rotation.eulerAngles - horizontalPartForward.eulerAngles) * Vector3.forward;
                            float dot = Vector3.Dot(horizontalPartDir.normalized, (new Vector3(activeAbilityLocation.x, 0, activeAbilityLocation.z) - new Vector3(horizontalPart.position.x, 0, horizontalPart.position.z)).normalized);
                            if (dot > 0.97f)
                            {
                                playCast = true;
                                if (hasCastAnim) animator.CrossFade("cast", crossFadeTime, 0, 0f);
                            }
                        }
                    }
                }

                // Position update
                PositionUpdateDirect();

                if (isMoving)
                {
                    // For movement animation speed
                    float distance = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), oldPos2D);
                    // Check if stuck
                    if (distance < 0.001f)
                    {
                        if (m_walkAnimationPlaying)
                        {
                            m_walkAnimationPlaying = false;
                            AnimatorSetBool(AnimationState.Walk, false);
                        }
                    }
                    else
                    {
                        if (!m_walkAnimationPlaying)
                        {
                            m_walkAnimationPlaying = true;
                            AnimatorSetBool(AnimationState.Walk, true);
                        }
                    }

                    // Move animation speed
                    if (animator)
                    {
                        float currentSpeed = distance / (moveSpeed * Time.deltaTime);
                        if (currentSpeed > 0.9f) currentSpeed = 1f;
                        else if (currentSpeed < 0.5f) currentSpeed = 0.5f;
                        if (animationMoveSpeed != 0) animator.SetFloat("movespeed", currentSpeed / animationMoveSpeed);
                        else animator.SetFloat("movespeed", currentSpeed);
                    }

                    // Update chunk and FoW info
                    Grid.AssignToChunk(this);
                    FogOfWar.instance.CellAssignment(this);
                }

                // Attack update
                if (target != null || targetPosition != Vector2.zero) AttackUpdate();
            }
        }

        /// <summary>
        /// Makes unit follow the target, called by the state handler every frame when necessary.
        /// </summary>
        private void FollowUpdate()
        {
            if (waitTwoUpdates == 0)
            {
                // Destination set
                //var offset = target.unitRadius * 0.5f * (transform.position - target.transform.position).normalized;
                Vector3 offset = Utils.stopDistanceOffset * (transform.position - target.transform.position).normalized;

                // Implies that agent is enabled

                if (isAir)
                {
                    currentDestination = target.transform.position + offset + new Vector3(Utils.airOffsetX, 0, 0);
                    agent.SetDestination(currentDestination);
                }
                else if (isInvisible && invisibleAgent)
                {
                    currentDestination = target.transform.position + offset + new Vector3(0, 0, Utils.invisibilityOffsetY);
                    invisibleAgent.SetDestination(currentDestination);
                }
                else
                {
                    currentDestination = target.transform.position + offset;
                    agent.SetDestination(currentDestination);
                }

                // if (isAir) agent.destination = target.transform.position + offset + new Vector3(Utils.airOffsetX, 0, 0);
                // else if (isInvisible && invisibleAgent) invisibleAgent.destination = target.transform.position + offset + new Vector3(0, 0, Utils.invisibilityOffsetY);
                // else agent.destination = target.transform.position + offset;
                // 
                // currentDestination = target.transform.position + offset;

                // SetDestination(target.transform.position + offset, false, stopDistance);
                // currentDestination = target.transform.position + offset;

                // Debug
                // Debug.DrawRay(target.transform.position + offset, Vector3.up);
            }
        }

        /// <summary>
        /// Makes unit attack the target, called by the state handler every frame when necessary.
        /// </summary>
        /// <returns></returns>
        private bool AttackUpdate()
        {
            attackTimerUpdate = false;
            // For the first attack we calculate tick when it will happen
            Vector3 attackPosition;

            if (NetworkConnectionHandler.isClient)
            {
                // For clients
                attackCooldown -= Time.deltaTime;

                if (targetPosition != Vector2.zero)
                {
                    attackPosition = new Vector3(targetPosition.x, Utils.GetTerrainHeight(targetPosition), targetPosition.y);
                    TargetAcquired(true);
                }
                else
                {
                    attackPosition = target.transform.position + new Vector3(0, target.unitHeight * 0.5f, 0);
                    TargetAcquired(false);
                }
            }
            else
            {
                // For offline/server

                // Range check
                bool isInRange;
                if (unitState == UnitStates.Attack && isTargetGround == true)
                {
                    float distanceToTarget = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), targetPosition);

                    // We have already started the attack, we must let it finish.
                    if (!firstAttack && attackCooldown > currentAttackSpeed - currentAttackAnimLength)
                    {
                        isInRange = (distanceToTarget < attackRange * Utils.activeDistanceMultiplier);
                    }
                    else isInRange = (distanceToTarget < attackRange);

                }
                else
                {
                    float distanceToTarget = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(target.transform.position.x, target.transform.position.z));

                    // We have already started the attack, we must let it finish.
                    if (!firstAttack && attackCooldown > currentAttackSpeed - currentAttackAnimLength)
                    {
                        isInRange = (distanceToTarget < (target.unitRadius + attackRange) * Utils.activeDistanceMultiplier);
                    }
                    else isInRange = (distanceToTarget < target.unitRadius + attackRange);
                }

                // Continuous
                if (attackType == AttackType.Continuous)
                {
                    // When continuous and is not in range we wait till tick to end attack update
                    if (!isInRange)
                    {
                        if (!firstAttack)
                        {
                            // We end an attack and start timer
                            MakeAgent(false);
                            AttackStop();
                            attackCooldown = -GameManager.tickRate;
                            return true;
                        }

                        attackCooldown += Time.deltaTime;
                        // Count ends, follow target
                        if (attackCooldown > 0)
                        {
                            return false;
                        }
                        else
                        {
                            return true;
                        }
                    }
                    else
                    {
                        // Target reached
                        // Reset cooldown
                        if (attackCooldown > 0) attackCooldown = 0;
                        // Update cooldown
                        attackCooldown -= Time.deltaTime;
                        // Stop  movement to  Perform an attack
                        MakeAgent(false);
                    }
                }
                // Standard
                else
                {
                    // For standard attack types
                    attackCooldown -= Time.deltaTime;
                    // currentAttackAnimLength
                    // If not first attack and still has cooldown we wait till attack animation ends
                    if (isInRange)
                    {
                        // Stop movement to Perform an attack
                        MakeAgent(false);
                    }
                    else if (!firstAttack && attackCooldown > currentAttackSpeed - currentAttackAnimLength)
                    {
                        // Outside the range, but still cooldown to wait before following
                        // Also means we will miss the shot
                        MakeAgent(false);
                        return true;
                    }
                    else
                    {
                        // We are not ending an attack and distance is too far, no attack update
                        return false;
                    }
                }

                if (unitState == UnitStates.Attack && isTargetGround == true)
                {
                    attackPosition = new Vector3(targetPosition.x, Utils.GetTerrainHeight(targetPosition), targetPosition.y);
                    TargetAcquired(true);
                }
                else
                {
                    attackPosition = target.transform.position + new Vector3(0, target.unitHeight * 0.5f, 0);
                    TargetAcquired(false);
                }
            }

            LookAt(attackPosition);
            if (!NetworkConnectionHandler.isClient && TryGetComponent<WeaponDeployment>(out var deployment)
                && !deployment.ReadyToFire()) return true;

            if (currentAttackCount == 0)
            {
                if (attackType != AttackType.Continuous)
                {
                    // To properly sync attack animation with the actual moment we deal damage, we play animation earlier depending on animationAttackDelay parameter
                    if (attackCooldown <= 0)
                    //if (!isAttackAnimationPlaying && (attackCooldown >= attackSpeed - currentAnimAttackDelay || (attackSpeed < currentAnimAttackDelay && attackCooldown > 0))) // If AS < AD then we start playing the animation as soon as possible
                    {
                        // Play attack start sound
                        if (attackStartSound.Length > 0) Presentation.Audio?.PlaySoundClip(attackStartSound, this.transform, 1);

                        // Play animation
                        if (attackType == AttackType.Standard || !periodicSequential)
                        {
                            // If standard random animation from available ones
                            int index = UnityEngine.Random.Range(0, attackAnimationsCount);
                            if (animator != null && attackAnimationsCount != 0 && FoWVisible) animator.CrossFade("attack" + index, crossFadeTime, 0, 0f); // animator.Play("attack" + index, 0, 0.05f);
                        }
                        else if (periodicSequential)
                        {
                            // If periodic sequential last attack animationm, we go from last to first
                            if (animator != null && attackAnimationsCount != 0 && FoWVisible) animator.CrossFade("attack" + (periodicAttackCount - 1), crossFadeTime, 0, 0f); // animator.Play("attack" + (periodicAttackCount - 1), 0, 0.05f);
                        }

                        isAttackAnimationPlaying = true;
                        if (isInvisible) SetInvisibility(false); // If invisible, make visible

                        // If net cd is true we already set currentAttackSpeed in TargetSet
                        if (!netCD)
                        {
                            if (attackType == AttackType.Standard) attackCooldown = currentAttackSpeed = attackSpeed;
                            else attackCooldown = currentAttackSpeed = periodicAttackDelay;
                        }
                        else
                        {
                            netCD = false;
                            attackCooldown = currentAttackSpeed;
                        }
                    }

                    // Main cooldown check
                    if (isAttackAnimationPlaying && attackCooldown < currentAttackSpeed - currentAnimAttackDelay)
                    //if (attackCooldown >= attackSpeed)
                    {
                        if (attackType == AttackType.Standard)
                        {
                            // Standard attack
                            AttackPlay(attackPosition);
                        }
                        else if (attackType == AttackType.Periodic)
                        {
                            // Periodic attack
                            currentAttackCount = periodicAttackCount - 1;
                            AttackPlay(attackPosition);
                            ChangeAttackAnimationSpeed(true); // periodicAttackDelay
                        }
                        //attackCooldown = 0;
                        isAttackAnimationPlaying = false;
                        if (isInvisible) SetInvisibility(false); // If invisible, make visible
                    }
                }
                else
                {
                    // Continuous

                    // To properly sync attack animation with the actual moment we deal damage, we play animation earlier depending on animationAttackDelay parameter
                    if (!isAttackAnimationPlaying && (attackCooldown <= -attackSpeed + currentAnimAttackDelay || attackSpeed < currentAnimAttackDelay)) // If AS < AD then we start playing the animation as soon as possible
                    {
                        // Play attack start sound
                        if (attackStartSound.Length > 0) Presentation.Audio?.PlaySoundClip(attackStartSound, this.transform, 1);

                        // Play animation
                        AnimatorSetBool(AnimationState.ContinuousAttack, true);

                        // Play VFX - with continuous play only when actual attack starts?
                        //if (attackVFXLine) attackVFXLine.SetTarget(attackPosition);


                        isAttackAnimationPlaying = true;
                        if (isInvisible) SetInvisibility(false);  // If invisible, make visible

                        // If net cd is true we already set currentAttackSpeed in TargetSet
                        if (!netCD)
                        {
                            currentAttackSpeed = attackSpeed;
                        }
                        else
                        {
                            netCD = false;
                            attackCooldown = currentAttackSpeed;
                        }
                    }

                    // Main cooldown check
                    if (attackCooldown < -(attackSpeed))
                    {
                        currentAttackCount = 1; // Means that we are actively attacking right now

                        // Play Loop Sound (Armor based or Ground) - Attack sound, audio clips must be defined
                        if (weaponSound != null)
                        {
                            if (target != null) { if (weaponSound.weaponSound[target.armorType.index].audioClips != null) attackSoundRef = Presentation.Audio?.PlayLoopSoundClip(weaponSound.weaponSound[target.armorType.index].audioClips, this.transform, 1); } // Unit target
                            else if (weaponSound.groundHitClips != null && weaponSound.groundHitClips.Length > 0) Presentation.Audio?.PlayLoopSoundClip(weaponSound.groundHitClips, this.transform, 1); // Ground target
                        }

                        if (multiTarget && (isTargetGround == false || unitState != UnitStates.Attack)) //targetPosition == Vector2.zero)
                        {
                            // MULTITARGET

                            // Check the distance to current targets
                            bool targetsUpdated = false;
                            additionalTargets[0] = target;
                            int targetsNeeded = 0;
                            for (int i = 1; i < additionalTargets.Length; i++)
                            {
                                if (additionalTargets[i] != null)
                                {
                                    if (Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(additionalTargets[i].transform.position.x, additionalTargets[i].transform.position.z)) - additionalTargets[i].unitRadius > attackRange)
                                    {
                                        additionalTargets[i] = null;
                                        targetsUpdated = true;
                                    }
                                    else targetsNeeded++;
                                }
                                else targetsNeeded++;
                            }

                            // Find new targets
                            if (targetsNeeded != 0)
                            {
                                Unit[] newTargets = Utils.GetClosestUnitsInRadius(new Vector2(transform.position.x, transform.position.z), attackRange, owner, searchUnitSelector, targetsNeeded, additionalTargets);

                                int targetsIndex = 1;
                                for (int i = 0; i < newTargets.Length; i++)
                                {
                                    if (newTargets[i] == null) continue;

                                    for (int z = targetsIndex; z < additionalTargets.Length; z++)
                                    {
                                        if (additionalTargets[z] == null)
                                        {
                                            additionalTargets[z] = newTargets[i];
                                            targetsUpdated = true;
                                            break;
                                        }
                                    }
                                }
                            }

                            // Sync additional targets
                            if (targetsUpdated && NetworkManager.Singleton.IsServer)
                            {
                                NetworkDataSync.instance.AdditionalTargetsSend(this, additionalTargets);
                            }

                            // Deal damage
                            for (int i = 0; i < additionalTargets.Length; i++)
                            {
                                if (additionalTargets[i] != null) DealDamage(additionalTargets[i], attackDamage * Time.deltaTime, damageType, true, additionalTargets[i].transform.position);
                            }
                            // Set VFX
                            if (attackVFXLine) attackVFXLine.SetTarget(additionalTargets, false);
                        }
                        else if (bounceCount != 0 && (isTargetGround == false || unitState != UnitStates.Attack)) // targetPosition == Vector2.zero)
                        {
                            // BOUNCE
                            List<Unit> targets = new List<Unit>();

                            // Main Target
                            targets.Add(target);
                            DealDamage(target, attackDamage * Time.deltaTime, damageType, true, attackPosition);

                            // Additional targets
                            var tempTarget = Utils.GetClosestUnit(new Vector2(attackPosition.x, attackPosition.z), bounceRange, owner, searchUnitSelector, target);
                            for (int i = 0; i < bounceCount; i++)
                            {
                                if (tempTarget == null) break;
                                else
                                {
                                    targets.Add(tempTarget);
                                    DealDamage(tempTarget, attackDamage * Time.deltaTime, damageType, true, tempTarget.transform.position);
                                    tempTarget = Utils.GetClosestUnit(new Vector2(tempTarget.transform.position.x, tempTarget.transform.position.z), bounceRange, owner, searchUnitSelector, tempTarget);
                                }
                            }
                            // Set VFX
                            if (attackVFXLine) attackVFXLine.SetTarget(targets, true);
                        }
                        else
                        {
                            // SINGLE TARGET
                            // Deal damage                                
                            DealDamage(target, attackDamage * Time.deltaTime, damageType, true, attackPosition);

                            // Set VFX
                            if (attackVFXLine)
                            {
                                attackVFXLine.SetTarget(attackPosition);
                                //if (targetPosition != Vector2.zero)
                                //if (isTargetGround == true) attackVFXLine.SetTarget(attackPosition);
                                //else attackVFXLine.SetTarget(target);
                            }
                        }

                        isAttackAnimationPlaying = false;
                        if (isInvisible) SetInvisibility(false); // If invisible, make visible
                    }
                    // While waiting for attack to start, we set the target - currently turned off. continuous should set vfx only when actual attack starts
                    // else if (isAttackAnimationPlaying && attackVFXLine) attackVFXLine.SetTarget(attackPosition);
                }
            }
            else
            {
                // Currently attack is being performed either periodic or continuous
                if (attackType == AttackType.Periodic)
                {
                    // To properly sync attack animation with the actual moment we deal damage, we play animation earlier depending on animationAttackDelay parameter
                    if (attackCooldown <= 0)
                    //if (!isAttackAnimationPlaying && (attackCooldown >= periodicAttackDelay - currentAnimAttackDelay || (periodicAttackDelay < currentAnimAttackDelay && attackCooldown > 0)))
                    {
                        // Play attack start sound
                        if (attackStartSound.Length > 0) Presentation.Audio?.PlaySoundClip(attackStartSound, this.transform, 1);
                        // Play animation
                        if (!periodicSequential)
                        {
                            // If standard random animation from available ones
                            int index = UnityEngine.Random.Range(0, attackAnimationsCount);
                            if (animator != null && attackAnimationsCount != 0 && FoWVisible) animator.CrossFade("attack" + index, crossFadeTime, 0, 0f); // animator.Play("attack" + index, 0, 0.05f);
                        }
                        else if (periodicSequential)
                        {
                            // If periodic sequential second attack animation, because first attack is already performed
                            if (animator != null && attackAnimationsCount != 0 && FoWVisible) animator.CrossFade("attack" + (currentAttackCount - 1), crossFadeTime, 0, 0f); // animator.Play("attack" + (currentAttackCount - 1), 0, 0.05f);
                        }

                        isAttackAnimationPlaying = true;
                        if (isInvisible) SetInvisibility(false); // If invisible, make visible

                        // If net cd is true we already set currentAttackSpeed in TargetSet
                        if (!netCD)
                        {
                            if (currentAttackCount == 1) attackCooldown = currentAttackSpeed = attackSpeed;
                            else attackCooldown = currentAttackSpeed = periodicAttackDelay;
                        }
                        else
                        {
                            netCD = false;
                            attackCooldown = currentAttackSpeed;
                        }
                    }

                    // Periodic. check attackDelay and perform an attack
                    if (isAttackAnimationPlaying && attackCooldown < currentAttackSpeed - currentAnimAttackDelay)
                    //if (isAttackAnimationPlaying && attackCooldown >= periodicAttackDelay)
                    {
                        currentAttackCount--;
                        AttackPlay(attackPosition);
                        isAttackAnimationPlaying = false;

                        // Reset animation spped to main attack speed
                        if (currentAttackCount == 0) ChangeAttackAnimationSpeed();
                    }
                }
                else
                {
                    // Continuous. Deal damage every frame while actively attacking
                    if (multiTarget && (isTargetGround == false || unitState != UnitStates.Attack)) //targetPosition == Vector2.zero)
                    {
                        // Multitarget

                        // Check the distance to current targets
                        bool targetsUpdated = false;
                        additionalTargets[0] = target;
                        int targetsNeeded = 0;
                        for (int i = 1; i < additionalTargets.Length; i++)
                        {
                            if (additionalTargets[i] != null)
                            {
                                if (Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(additionalTargets[i].transform.position.x, additionalTargets[i].transform.position.z)) - additionalTargets[i].unitRadius > attackRange)
                                {
                                    additionalTargets[i] = null;
                                    targetsUpdated = true;
                                }
                                else targetsNeeded++;
                            }
                            else targetsNeeded++;
                        }

                        // Find new targets
                        if (targetsNeeded != 0)
                        {
                            Unit[] newTargets = Utils.GetClosestUnitsInRadius(new Vector2(transform.position.x, transform.position.z), attackRange, owner, searchUnitSelector, targetsNeeded, additionalTargets);

                            int targetsIndex = 1;
                            for (int i = 0; i < newTargets.Length; i++)
                            {
                                if (newTargets[i] == null) continue;

                                for (int z = targetsIndex; z < additionalTargets.Length; z++)
                                {
                                    if (additionalTargets[z] == null)
                                    {
                                        additionalTargets[z] = newTargets[i];
                                        targetsUpdated = true;
                                        break;
                                    }
                                }
                            }
                        }

                        // Sync additional targets
                        if (targetsUpdated && NetworkManager.Singleton.IsServer)
                        {
                            NetworkDataSync.instance.AdditionalTargetsSend(this, additionalTargets);
                        }

                        // Deal damage
                        for (int i = 0; i < additionalTargets.Length; i++)
                        {
                            if (additionalTargets[i] != null) DealDamage(additionalTargets[i], attackDamage * Time.deltaTime, damageType, true, additionalTargets[i].transform.position);
                        }
                        // Set VFX
                        if (attackVFXLine) attackVFXLine.SetTarget(additionalTargets, false);
                    }
                    else if (bounceCount != 0 && (isTargetGround == false || unitState != UnitStates.Attack)) //targetPosition == Vector2.zero)
                    {
                        // Bounce
                        List<Unit> targets = new List<Unit>();

                        // Main Target
                        targets.Add(target);
                        DealDamage(target, attackDamage * Time.deltaTime, damageType, true, attackPosition);

                        // Additional targets
                        var tempTarget = Utils.GetClosestUnit(new Vector2(attackPosition.x, attackPosition.z), bounceRange, owner, searchUnitSelector, target);
                        for (int i = 0; i < bounceCount; i++)
                        {
                            if (tempTarget == null) break;
                            else
                            {
                                targets.Add(tempTarget);
                                DealDamage(tempTarget, attackDamage * Time.deltaTime, damageType, true, tempTarget.transform.position);
                                tempTarget = Utils.GetClosestUnit(new Vector2(tempTarget.transform.position.x, tempTarget.transform.position.z), bounceRange, owner, searchUnitSelector, tempTarget);
                            }
                        }
                        // Set VFX
                        if (attackVFXLine) attackVFXLine.SetTarget(targets, true);
                    }
                    else
                    {
                        // Set VFX
                        if (attackVFXLine)
                        {
                            attackVFXLine.SetTarget(attackPosition);
                            //if (targetPosition != Vector2.zero) attackVFXLine.SetTarget(attackPosition);
                            //else attackVFXLine.SetTarget(target);
                        }
                        // Deal damage
                        DealDamage(target, attackDamage * Time.deltaTime, damageType, true, attackPosition);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Performs an attack (Play animation, sound and Deal damage or Spawn projectile.
        /// </summary>
        /// <param name="attackPosition">Current target position of either the target or targetGround.</param>
        private void AttackPlay(Vector3 attackPosition)
        {
            // Play Sound (Armor based or Ground) - Attack sound, audio clips must be defined
            if (weaponSound != null)
            {
                if (target != null) { if (weaponSound.weaponSound[target.armorType.index].audioClips != null) Presentation.Audio?.PlaySoundClip(weaponSound.weaponSound[target.armorType.index].audioClips, this.transform, 1); } // Unit target
                else if (weaponSound.groundHitClips != null && weaponSound.groundHitClips.Length > 0) Presentation.Audio?.PlaySoundClip(weaponSound.groundHitClips, this.transform, 1); // Ground target
            }

            // Play VFX
            if (launchVFX.Length > 0) launchVFX[(attackType == AttackType.Standard) ? 0 : currentAttackCount].Play();

            // ---------- CALLBACKS ----------

            // 1. Damage modify callbacks
            float amount = attackDamage;
            float finalDamage = amount;
            foreach (var c in OnDamageDealModifyCallbacks)
            {
                float damageChanged = c.Callback(this, c.Level, amount, true);
                if (damageChanged > finalDamage) finalDamage = damageChanged; // For positive dmg change
                else if (finalDamage <= amount && damageChanged < finalDamage) finalDamage = damageChanged; // For negative dmg change
            }
            amount = finalDamage;

            // 2. Before damage deal callbacks
            foreach (var c in OnBeforeDamageDealCallbacks)
            {
                c.Callback(target, attackPosition, attackEffectors, amount, damageType, this, owner, c.Level);
            }

            // ---------- DAMAGE OR PROJECTILE ----------

            if (melee)
            {
                DealDamage(target, amount, damageType, true, attackPosition);
            }
            else
            {
                // Projectile spawn position and VFX play
                Vector3 spawnPosition;
                if (launchSite.Length > 0) spawnPosition = launchSite[(attackType == AttackType.Standard) ? 0 : currentAttackCount].position;
                else spawnPosition = transform.position;

                // Spawn projectile
                // If attacking the target or target position
                if (unitState == UnitStates.Attack && isTargetGround)
                {
                    // Target position
                    Projectile.SpawnAttack(this, projectileVFX, spawnPosition, Quaternion.identity, attackPosition, searchUnitSelector, splashUnitSelector, amount, true);
                }
                else
                {
                    // Target
                    Projectile.SpawnAttack(this, projectileVFX, spawnPosition, Quaternion.identity, target, searchUnitSelector, splashUnitSelector, amount, true);
                }

                // Multitarget
                if (multiTarget)
                {
                    // Check the distance to current targets
                    bool targetsUpdated = false;
                    additionalTargets[0] = target;
                    int targetsNeeded = 0;
                    for (int i = 1; i < additionalTargets.Length; i++)
                    {
                        if (additionalTargets[i] != null)
                        {
                            if (Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(additionalTargets[i].transform.position.x, additionalTargets[i].transform.position.z)) - additionalTargets[i].unitRadius > attackRange)
                            {
                                additionalTargets[i] = null;
                                targetsUpdated = true;
                            }
                            else targetsNeeded++;
                        }
                        else targetsNeeded++;
                    }

                    // Find new targets
                    if (targetsNeeded != 0)
                    {
                        Unit[] newTargets = Utils.GetClosestUnitsInRadius(new Vector2(transform.position.x, transform.position.z), attackRange, owner, searchUnitSelector, targetsNeeded, additionalTargets);

                        int targetsIndex = 1;
                        for (int i = 0; i < newTargets.Length; i++)
                        {
                            if (newTargets[i] == null) continue;

                            for (int z = targetsIndex; z < additionalTargets.Length; z++)
                            {
                                if (additionalTargets[z] == null)
                                {
                                    additionalTargets[z] = newTargets[i];
                                    targetsUpdated = true;
                                    break;
                                }
                            }
                        }
                    }

                    // Sync additional targets
                    if (targetsUpdated && NetworkManager.Singleton.IsServer)
                    {
                        NetworkDataSync.instance.AdditionalTargetsSend(this, additionalTargets);
                    }

                    // Launch projectiles
                    for (int i = 1; i < additionalTargets.Length; i++)
                    {
                        if (additionalTargets[i] != null)
                            Projectile.SpawnAttack(this, projectileVFX, spawnPosition, Quaternion.identity, additionalTargets[i], searchUnitSelector, splashUnitSelector, amount, true);
                    }
                }
            }
        }

        /// <summary>
        /// Resets attack cooldown and animation. Called when attack should be stopped.
        /// </summary>
        private void AttackStop()
        {
            TargetLost();
            currentAttackCount = 0;
            isAttackAnimationPlaying = false;
            ChangeAttackAnimationSpeed();

            // If multitarget, reset additional targets
            if (multiTarget) additionalTargets = new Unit[multiTargetCount + 1];

            // Play attack end sound
            if (attackEndSound.Length > 0) Presentation.Audio?.PlaySoundClip(attackEndSound, this.transform, 1);

            if (attackType == AttackType.Continuous)
            {
                // Stop loop sound
                if (attackSoundRef) Destroy(attackSoundRef.gameObject);
                // Stop animation
                AnimatorSetBool(AnimationState.ContinuousAttack, false);
                // Hide VFX
                if (attackVFXLine) attackVFXLine.Deactivate();
            }
            else
            {
                attackTimerUpdate = true;
                AnimatorSetBool(AnimationState.IdleReady, false);
            }
        }

        /// <summary>
        /// Changes the current state`s target. When target is set for this unit we must refer to the same unit when target`s reference changes.
        /// </summary>
        /// <param name="newTarget"></param>
        public void TargetReferenceChange(Unit newTarget)
        {
            // New Target set (can be null)
            // [Interflow fix 2026-06-27] null-guard: при Die цель уже могла стать null — ассет дёргал target.OnReferenceChange без проверки → NRE (Unit.cs:2448). Реестр: wiki concepts/asset-fork-debt.
            if (target != null) target.OnReferenceChange -= TargetReferenceChange;
            target = newTarget;
            if (target != null) target.OnReferenceChange += TargetReferenceChange;

            // Won't be called on clients, since states are server only
            else if (unitState == UnitStates.AttackMove) // Target is null and state is AttacMove
            {
                // If target dies before reaching it
                if (firstAttack == true) SetDestination(targetPosition, true);
            }
        }

        // ============================= STATE SET ==============================================================================

        /// <summary>
        /// Sets the invulnerability of the unit.
        /// </summary>
        /// <param name="invulnerability">Should unit be invulnerable.</param>
        public void IsInvulnerable(bool invulnerability)
        {
            isInvulnerable = invulnerability;

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Stuns the unit for a specified amount of time.
        /// </summary>
        /// <param name="time">Stun time.</param>
        public void Stun(float time)
        {
            if (staticObject) return;

            // [Interflow fix 2026-07-06 control-immunity] Иммунитет к контролю (Железный Приговор и будущие эффекты). Маркер ControlImmunity
            // на юните → стан не применяется. Проверка per-unit (только этот юнит), см. concepts/control-immunity.
            if (TryGetComponent<ControlImmunity>(out var __controlImmunity) && __controlImmunity.Active) return;

            if (stunTime == 0)
            {
                // [Interflow fix 2026-08-05 unit-status-sync] Одиночная станная анимация над головой
                // ОТКЛЮЧЕНА (решение Artsiom 2026-08-05): стан теперь показывает шкала статусов
                // над полоской здоровья (UnitStatusIconsBar). Снятие stunnedVFX ниже оставлено —
                // оно безопасно чистит null и прикроет старые экземпляры при откате этой правки.

                // Freeze the unit;
                if (activeAbilityInUse) Idle();
                else
                {
                    if (!firstAttack) AttackStop();
                    MakeAgent(false);
                    EndActiveAbility(false, true);
                }

                stunned = true;
                stunTime = time;
                GameManager.instance.Tick += StunUpdate;
            }
            else if (time > stunTime)
            {
                // Already stunned, and new stun time is more than stunTime left
                stunTime = time;
            }

            if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.StunSetSend(this, true);
        }

        /// <summary>
        /// Shows stun VFX. Called on the clients.
        /// </summary>
        /// <param name="state">Is unit currently stunned.</param>
        public void Stun(bool state)
        {
            if (state)
            {
                stunned = true;
                // [Interflow fix 2026-08-05 unit-status-sync] Одиночная станная анимация клиента
                // ОТКЛЮЧЕНА (решение Artsiom 2026-08-05) — стан показывает шкала статусов.

                // Turn off move animation
                if (m_walkAnimationPlaying)
                {
                    m_walkAnimationPlaying = false;
                    AnimatorSetBool(AnimationState.Walk, false);
                }

                // Construction animation
                if (constructionUnit && !constructionUnit.isBuilding && constructionUnit.isWorking)
                {
                    AnimatorSetBool(AnimationState.Building, false);
                }
            }
            else if (!state)
            {
                stunned = false;
                stunTime = 0;
                if (stunnedVFX != null) Destroy(stunnedVFX.gameObject);

                // Construction animation
                if (constructionUnit && !constructionUnit.isBuilding && constructionUnit.isWorking)
                {
                    AnimatorSetBool(AnimationState.Building, true);
                }
            }
        }

        /// <summary>
        /// Updates the stun timer every GameManager.Tick.
        /// </summary>
        private void StunUpdate()
        {
            stunTime -= GameManager.instance.currentDeltaTime;

            if (stunTime <= 0f)
            {
                stunned = false;
                stunTime = 0;
                GameManager.instance.Tick -= StunUpdate;

                // Ability cast
                if (unitState == UnitStates.AbilityCasting)
                {
                    currentActionTime = 0;
                    sendToClients = false;
                    playCast = false;
                }

                // Construction animation
                if (constructionUnit && !constructionUnit.isBuilding && constructionUnit.isWorking)
                {
                    AnimatorSetBool(AnimationState.Building, true);
                }

                if (stunnedVFX != null) Destroy(stunnedVFX.gameObject);
                if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.StunSetSend(this, false);
            }
        }

        /// <summary>
        /// Mutes the unit for a specified amount of time.
        /// </summary>
        /// <param name="time">Mute time.</param>
        public void Mute(float time)
        {
            if (staticObject) return;
            // If already muted, means currently permanently muted
            if (muted) return;

            if (currentMuteTime == 0)
            {
                // [Interflow fix 2026-08-05 unit-status-sync] Одиночная анимация немоты над головой
                // ОТКЛЮЧЕНА (решение Artsiom: «отключи все анимации, оставь только иконки») —
                // статус показывает шкала над полоской здоровья. Снятие mutedVFX ниже оставлено (чистит null безопасно).

                // Mute the unit
                if (activeAbilityInUse) Idle();
                else EndActiveAbility(false, true);

                muted = true;
                currentMuteTime = time;
                GameManager.instance.Tick += MuteUpdate;
            }
            else if (time > currentMuteTime)
            {
                // Already muted, and new mute time is larger than current one
                currentMuteTime = time;
            }

            if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.MuteSetSend(this, true);
        }

        /// <summary>
        /// Shows mute VFX. Called on the clients
        /// </summary>
        /// <param name="state">Is unit currently muted.</param>
        public void Mute(bool state)
        {
            if (state)
            {
                muted = true;
                // [Interflow fix 2026-08-05 unit-status-sync] Клиентская анимация немоты ОТКЛЮЧЕНА — статус показывает шкала.
            }
            else if (!state)
            {
                muted = false;
                currentMuteTime = 0;
                if (mutedVFX != null) Destroy(mutedVFX.gameObject);
            }
        }

        /// <summary>
        /// Updates the mute timer every GameManager.Tick.
        /// </summary>
        private void MuteUpdate()
        {
            currentMuteTime -= GameManager.instance.currentDeltaTime;

            if (currentMuteTime <= 0f)
            {
                muted = false;
                currentMuteTime = 0;
                GameManager.instance.Tick -= MuteUpdate;

                // Ability cast
                if (unitState == UnitStates.AbilityCasting)
                {
                    currentActionTime = 0;
                    sendToClients = false;
                    playCast = false;
                }

                if (mutedVFX != null) Destroy(mutedVFX.gameObject);
                if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.MuteSetSend(this, false);
            }
        }

        /// <summary>
        /// Disarms the unit for a specified amount of time.
        /// </summary>
        /// <param name="time">Is unit currently disarmed.</param>
        public void Disarm(float time)
        {
            if (!canAttack) return;
            // If already disarmed, means currently permanently disarmed
            if (disarmed) return;

            if (currentDisarmTime == 0)
            {
                // [Interflow fix 2026-08-05 unit-status-sync] Одиночная анимация безоружия над головой
                // ОТКЛЮЧЕНА (решение Artsiom) — статус показывает шкала над полоской здоровья.

                // Stop attack
                if (!firstAttack) AttackStop();

                disarmed = true;
                currentDisarmTime = time;
                GameManager.instance.Tick += DisarmUpdate;
            }
            else if (time > currentDisarmTime)
            {
                // Already disarmed, and new disarm time is larger than current one
                currentDisarmTime = time;
            }

            if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.DisarmSetSend(this, true);
        }

        /// <summary>
        /// Shows disarm VFX. Called on the clients
        /// </summary>
        /// <param name="state"></param>
        public void Disarm(bool state)
        {
            if (state)
            {
                disarmed = true;
                // [Interflow fix 2026-08-05 unit-status-sync] Клиентская анимация безоружия ОТКЛЮЧЕНА — статус показывает шкала.
            }
            else if (!state)
            {
                disarmed = false;
                currentDisarmTime = 0;
                if (disarmedVFX != null) Destroy(disarmedVFX.gameObject);
            }
        }

        /// <summary>
        /// Updates the disarm timer every GameManager.Tick.
        /// </summary>
        private void DisarmUpdate()
        {
            currentDisarmTime -= GameManager.instance.currentDeltaTime;

            if (currentDisarmTime <= 0f)
            {
                disarmed = false;
                currentDisarmTime = 0;
                GameManager.instance.Tick -= DisarmUpdate;

                if (disarmedVFX != null) Destroy(disarmedVFX.gameObject);
                if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.DisarmSetSend(this, false);
            }
        }

        /// <summary>
        /// Polymorphs the unit into another unit for a specified amount of time.
        /// </summary>
        /// <param name="ability">Ability that triggered the polymorph.</param>
        /// <param name="lvl">Level of the ability.</param>
        /// <param name="time">Duration of the polymorph.</param>
        /// <param name="shapeUnit">Desired shape for the unit.</param>
        public void Polymorph(Ability ability, int lvl, float time, Unit shapeUnit)
        {
            // Double polymorph is impossible, deactivate previous one
            if (polymorphed)
            {
                polymorphAbility.Deactivate(this, this.owner, polymorphLvl);
            }
            else
            {
                GameManager.instance.Tick += PolymorphUpdate;
            }

            polymorphed = true;
            polymorphTime = time;
            polymorphTotalTime = time;
            polymorphAbility = ability;
            polymorphLvl = lvl;
            polymorphShape = shapeUnit;

            if(ability is CompositeSkill shapeSkill&&shapeSkill.morph!=null&&shapeSkill.morph.copyAttackMode){var mode=GetComponent<MorphAttackMode>();if(!mode)mode=gameObject.AddComponent<MorphAttackMode>();mode.Apply(this,shapeUnit);}
            ReplaceRenderers(shapeUnit, false);
            if(Presentation.Selection?.ActiveUnit==this)Presentation.UI?.Resubscribe();
        }

        /// <summary>
        /// Updates the polymorph timer every GameManager.Tick.
        /// </summary>
        private void PolymorphUpdate()
        {
            polymorphTime -= GameManager.instance.currentDeltaTime;

            if (polymorphTime <= 0f)
            {
                polymorphed = false;
                polymorphTime = 0;
                polymorphAbility.Deactivate(this, this.owner, polymorphLvl);
                GameManager.instance.Tick -= PolymorphUpdate;
            }
        }

    }
}
