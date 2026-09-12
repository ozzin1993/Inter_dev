// [Interflow fix 2026-06-27] Все подсказки [Tooltip] в этом файле локализованы на русский (правка ассета, разрешена Artsiom; только текст Tooltip). Оригинал EN: _BACKUP_TOOLTIPS/Scripts/Unit.cs. Реестр: wiki concepts/asset-fork-debt.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Unit.Commands.cs — сетевые команды (NETWORK SYNC + COMMANDS). Вырезано 1:1 из Unit.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class Unit
    {
        // ============================= NETWORK ATTACK SYNC ==============================================================================

        /// <summary>
        /// If it is a first attack notifies clients and sets the IdleReady.
        /// </summary>
        /// <param name="targetGround">Is target ground.</param>
        private void TargetAcquired(bool targetGround)
        {
            if (firstAttack)
            {
                firstAttack = false;

                if (attackType == AttackType.Continuous) { if (!NetworkConnectionHandler.isClient) attackCooldown = 0; }
                else AnimatorSetBool(AnimationState.IdleReady, true);

                if (NetworkManager.Singleton.IsServer)
                {
                    if (targetGround)
                    {
                        NetworkDataSync.Instance.TargetAcquired(this.netID, targetPosition, attackCooldown);
                    }
                    else
                    {
                        NetworkDataSync.Instance.TargetAcquired(this.netID, target.netID, attackCooldown);
                    }
                }
            }
        }

        /// <summary>
        /// Notifies clients that this unit stopped attacking.
        /// </summary>
        private void TargetLost()
        {
            if (!firstAttack)
            {
                firstAttack = true;
                if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.TargetLost(this.netID);
            }
        }

        // ============================= NETWORK TARGET SET ==============================================================================

        /// <summary>
        /// Sets the unit target with custom cooldown and attack count. Called by network handler to sync the attack with the server.
        /// </summary>
        /// <param name="netTarget">Target unit.</param>
        /// <param name="attackTime">Current attack cooldown.</param>
        /// <param name="attackCount">Current attack count.</param>
        /// <param name="immediateAttack">Should the attack be made immediately and the next attack cooldown set to attackTime (which is supposed to sync the attack time on client and server).</param>
        public void TargetSet(Unit netTarget, float attackTime, int attackCount = 0, bool immediateAttack = false)
        {
            if (target) target.OnReferenceChange -= TargetReferenceChange;
            target = netTarget;
            target.OnReferenceChange += TargetReferenceChange;
            targetPosition = Vector2.zero;
            attackCooldown = attackTime;
            currentAttackCount = attackCount;
            if (immediateAttack)
            {
                netCD = true;
                attackCooldown = 0;
                currentAttackSpeed = attackTime;
            }
            else
            {
                netCD = false;
                attackCooldown = attackTime;
            }
            AnimatorSetBool(AnimationState.Walk, false); // Walk anim off
            AnimatorSetBool(AnimationState.IdleReady, true);
        }

        /// <summary>
        /// Sets the ground target with custom cooldown and attack count. Called by network handler to sync the attack with the server.
        /// </summary>
        /// <param name="targetGround">Target ground.</param>
        /// <param name="attackTime">Current attack cooldown.</param>
        /// <param name="attackCount">Current attack count.</param>
        /// <param name="immediateAttack">Should the attack be made immediately and the next attack cooldown set to attackTime (which is supposed to sync the attack time on client and server).</param>
        public void TargetSet(Vector2 targetGround, float attackTime, int attackCount = 0, bool immediateAttack = false)
        {
            if (target) target.OnReferenceChange -= TargetReferenceChange;
            target = null;
            targetPosition = targetGround;
            currentAttackCount = attackCount;
            if (immediateAttack)
            {
                netCD = true;
                attackCooldown = 0;
                currentAttackSpeed = attackTime;
            }
            else
            {
                netCD = false;
                attackCooldown = attackTime;
            }
            AnimatorSetBool(AnimationState.Walk, false); // Walk anim off
            AnimatorSetBool(AnimationState.IdleReady, true);
        }

        /// <summary>
        /// Sets the target to null. For clients. Triggered by TargetLost() on the server.
        /// </summary>
        /// <param name="Null"></param>
        public void TargetSet(bool Null)
        {
            if (target) target.OnReferenceChange -= TargetReferenceChange;
            target = null;
            targetPosition = Vector2.zero;
            AttackStop();
        }

        // ============================= COMMANDS ==============================================================================

        /// <summary>
        /// Commands the unit to stop its current action and idle in place.
        /// </summary>
        /// <param name="issuedByPlayer">Is this command issued by the player directly.</param>
        public void Idle(bool issuedByPlayer = false)
        {
            if (NetworkConnectionHandler.isClient)
            {
                // Отправка снята: приёмник закрыт — прямого управления юнитами нет (решение Artsiom 09.09).
                // Клиент выходит здесь как и раньше; локально команда состояния не меняла, а сервер
                // те же вызовы делает у себя сам (ConstructionUnit, ResourceUnit и менеджеры матча).
                return;
            }

            unitState = UnitStates.Idle;
            if (target != null) target.OnReferenceChange -= TargetReferenceChange;
            target = null;
            initialPosition = Vector2.zero;
            targetPosition = Vector2.zero;
            isTargetGround = false;
            if (!firstAttack) AttackStop();
            if (!isMoving && issuedByPlayer && animator)
            {
                animator.CrossFade("idle0", crossFadeTime, 0, 0f);
                if (NetworkDataSync.Instance) NetworkDataSync.Instance.PlayIdleAnim(netID);
            }
            MakeAgent(false);

            OnCommand?.Invoke(issuedByPlayer);
        }

        /// <summary>
        /// Commands the unit to hold its position and attack enemies at attack range.
        /// </summary>
        /// <param name="issuedByPlayer">Is this command issued by the player directly.</param>
        public void Hold(bool issuedByPlayer = false)
        {
            if (NetworkConnectionHandler.isClient)
            {
                // Отправка снята: приёмник закрыт — прямого управления юнитами нет (решение Artsiom 09.09).
                // Клиент выходит здесь как и раньше; локально команда состояния не меняла, а сервер
                // те же вызовы делает у себя сам (ConstructionUnit, ResourceUnit и менеджеры матча).
                return;
            }

            unitState = UnitStates.Hold;
            if (target != null) target.OnReferenceChange -= TargetReferenceChange;
            target = null;
            if (!firstAttack) AttackStop();
            MakeAgent(false);

            OnCommand?.Invoke(issuedByPlayer);
        }

        /// <summary>
        /// Commands the unit to follow the non-enemy unit.
        /// </summary>
        /// <param name="u">Unit to follow.</param>
        /// <param name="stopDist">At what distance from the target we should stop.</param>
        /// <param name="embark">If transport unit should we enter/take in the target upon reaching it.</param>
        /// <param name="issuedByPlayer">Is this command issued by the player directly.</param>
        /// <returns>Returns true if already reached the follow unit.</returns>
        public bool Follow(Unit u, float stopDist = 0, bool embark = false, bool issuedByPlayer = false)
        {
            if (u.isBeingBuilt) embark = false;

            if (NetworkConnectionHandler.isClient)
            {
                // Отправка снята: приёмник закрыт — прямого управления юнитами нет (решение Artsiom 09.09).
                // Клиент выходит здесь как и раньше; локально команда состояния не меняла, а сервер
                // те же вызовы делает у себя сам (ConstructionUnit, ResourceUnit и менеджеры матча).
                return false;
            }

            unitState = UnitStates.Follow;
            if (target != null) target.OnReferenceChange -= TargetReferenceChange;
            target = u;
            target.OnReferenceChange += TargetReferenceChange;
            if (!firstAttack) AttackStop();

            // Stop distance for item pickup
            if (u.unitType == UnitType.Item || embark) stopDistance = target.unitRadius + unitRadius + Utils.stopDistanceOffset;
            else stopDistance = stopDist;

            // For following a transport
            embarkFollow = embark;

            // Launch command
            OnCommand?.Invoke(issuedByPlayer);

            // Initial position check
            float distanceToTarget = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(target.transform.position.x, target.transform.position.z));

            if ((stopDistance == 0 && (distanceToTarget < target.unitRadius + unitRadius + Utils.stopDistanceOffset || distanceToTarget < attackRange)) || distanceToTarget <= stopDistance)
            {
                // Reached target;
                OnFollowReach?.Invoke();
                MakeAgent(false);
                if (embarkFollow && !target.isBeingBuilt)
                {
                    // Current unit is transport, embark followed unit
                    if (transportUnit && target.unitType == UnitType.Unit) transportUnit.Embark(target);
                    // Target is transport, embark this unit
                    else if (target.transportUnit && unitType == UnitType.Unit) target.transportUnit.Embark(this);
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Commands the unit to move to specified destionation.
        /// </summary>
        /// <param name="position">Position to move.</param>
        /// <param name="stopDist">At what distance from the destination we should stop.</param>
        /// <param name="issuedByPlayer">Is this command issued by the player directly.</param>
        /// <returns>Returns true if already at the destination.</returns>
        public bool Move(Vector2 position, float stopDist = 0, bool issuedByPlayer = false)
        {
            if (NetworkConnectionHandler.isClient)
            {
                // Отправка снята: приёмник закрыт — прямого управления юнитами нет (решение Artsiom 09.09).
                // Клиент выходит здесь как и раньше; локально команда состояния не меняла, а сервер
                // те же вызовы делает у себя сам (ConstructionUnit, ResourceUnit и менеджеры матча).
                return false;
            }

            if (target != null) target.OnReferenceChange -= TargetReferenceChange;
            target = null;
            initialPosition = Vector2.zero; // Idle state will acquire a new initial position
            targetPosition = Vector2.zero;
            if (!firstAttack) AttackStop();

            // Initial reach check
            float distanceToDestination = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), position);
            if (distanceToDestination < Utils.stopDistanceOffset || distanceToDestination < stopDist)
            {
                OnPositionReach?.Invoke();
                Idle();
                return true;
            }
            else
            {
                unitState = UnitStates.Move;
                SetDestination(position, true, stopDist);
            }

            OnCommand?.Invoke(issuedByPlayer);
            return false;
        }

        /// <summary>
        /// Commands the unit to move to specified destionation.
        /// </summary>
        /// <param name="position">Position to move.</param>
        /// <param name="stopDist">At what distance from the destination we should stop.</param>
        /// <param name="issuedByPlayer">Is this command issued by the player directly.</param>
        /// <returns>Returns true if already at the destination.</returns>
        public bool Move(Vector3 position, float stopDist = 0, bool issuedByPlayer = false)
        {
            if (NetworkConnectionHandler.isClient)
            {
                // Отправка снята: приёмник закрыт — прямого управления юнитами нет (решение Artsiom 09.09).
                // Клиент выходит здесь как и раньше; локально команда состояния не меняла, а сервер
                // те же вызовы делает у себя сам (ConstructionUnit, ResourceUnit и менеджеры матча).
                return false;
            }

            if (target != null) target.OnReferenceChange -= TargetReferenceChange;
            target = null;
            initialPosition = Vector2.zero; // Idle state will acquire a new initial position
            targetPosition = Vector2.zero;
            if (!firstAttack) AttackStop();

            // Initial reach check
            float distanceToDestination = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(position.x, position.z));
            if (distanceToDestination < Utils.stopDistanceOffset || distanceToDestination < stopDist)
            {
                OnPositionReach?.Invoke();
                Idle();
                return true;
            }
            else
            {
                unitState = UnitStates.Move;
                SetDestination(position, true, stopDist);
            }

            OnCommand?.Invoke(issuedByPlayer);
            return false;
        }

        /// <summary>
        /// Commands the unit to attack the target.
        /// </summary>
        /// <param name="u">Unit to attack.</param>
        /// <param name="issuedByPlayer">Is this command issued by the player directly.</param>
        public void Attack(Unit u, bool issuedByPlayer = false)
        {
            if (NetworkConnectionHandler.isClient)
            {
                // Отправка снята: приёмник закрыт — прямого управления юнитами нет (решение Artsiom 09.09).
                // Клиент выходит здесь как и раньше; локально команда состояния не меняла, а сервер
                // те же вызовы делает у себя сам (ConstructionUnit, ResourceUnit и менеджеры матча).
                return;
            }

            // If the same unit that is being attacked right now, ignore
            if (unitState == UnitStates.Attack && target == u) return;

            unitState = UnitStates.Attack;
            if (target != null) target.OnReferenceChange -= TargetReferenceChange;
            target = u;
            target.OnReferenceChange += TargetReferenceChange;
            targetPosition = new Vector2(target.transform.position.x, target.transform.position.z);
            isTargetGround = false;
            if (!firstAttack) AttackStop();

            OnCommand?.Invoke(issuedByPlayer);
        }

        /// <summary>
        /// Commands the unit to attack the ground.
        /// </summary>
        /// <param name="targetPosition">Ground position to attack.</param>
        /// <param name="issuedByPlayer">Is this command issued by the player directly.</param>
        public void Attack(Vector2 targetPosition, bool issuedByPlayer = false)
        {
            if (NetworkConnectionHandler.isClient)
            {
                // Отправка снята: приёмник закрыт — прямого управления юнитами нет (решение Artsiom 09.09).
                // Клиент выходит здесь как и раньше; локально команда состояния не меняла, а сервер
                // те же вызовы делает у себя сам (ConstructionUnit, ResourceUnit и менеджеры матча).
                return;
            }

            unitState = UnitStates.Attack;
            if (target != null) target.OnReferenceChange -= TargetReferenceChange;
            target = null;
            this.targetPosition = targetPosition;
            isTargetGround = true;
            if (!firstAttack) AttackStop();

            OnCommand?.Invoke(issuedByPlayer);
        }

        /// <summary>
        /// Checks if the unit can attack the target and commands to attack.
        /// </summary>
        /// <param name="u">Unit to attack.</param>
        /// <param name="issuedByPlayer">Is this command issued by the player directly.</param>
        public void AttackVerify(Unit u, bool issuedByPlayer = false)
        {
            // If the same unit that is being attacked right now, ignore
            if (unitState == UnitStates.Attack && target == u) return;

            if (UnitSelector.IsUnitCompatible(owner, u, attackUnitSelector))
            {
                unitState = UnitStates.Attack;
                if (target != null) target.OnReferenceChange -= TargetReferenceChange;
                target = u;
                target.OnReferenceChange += TargetReferenceChange;
                targetPosition = new Vector2(target.transform.position.x, target.transform.position.z);
                isTargetGround = false;
                if (!firstAttack) AttackStop();

                OnCommand?.Invoke(issuedByPlayer);
            }
            else Presentation.NotifyMsg("Can`t attack this unit!", owner, true);
        }

        /// <summary>
        /// Commands the units to move and attack the units on its way.
        /// </summary>
        /// <param name="position">Position to move.</param>
        /// <param name="issuedByPlayer">Is this command issued by the player directly.</param>
        public void AttackMove(Vector2 position, bool issuedByPlayer = false)
        {
            if (NetworkConnectionHandler.isClient)
            {
                // Отправка снята: приёмник закрыт — прямого управления юнитами нет (решение Artsiom 09.09).
                // Клиент выходит здесь как и раньше; локально команда состояния не меняла, а сервер
                // те же вызовы делает у себя сам (ConstructionUnit, ResourceUnit и менеджеры матча).
                return;
            }

            unitState = UnitStates.AttackMove;
            if (target != null) target.OnReferenceChange -= TargetReferenceChange; // Needed?
            target = null; // Needed?
            targetPosition = position;
            isTargetGround = false;
            if (!firstAttack) AttackStop();
            SetDestination(position, true);

            OnCommand?.Invoke(issuedByPlayer);
        }

    }
}
