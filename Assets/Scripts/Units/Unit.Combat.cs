// [Interflow fix 2026-06-27] Все подсказки [Tooltip] в этом файле локализованы на русский (правка ассета, разрешена Artsiom; только текст Tooltip). Оригинал EN: _BACKUP_TOOLTIPS/Scripts/Unit.cs. Реестр: wiki concepts/asset-fork-debt.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Unit.Combat.cs — урон и смерть (DAMAGE + DIE). Вырезано 1:1 из Unit.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class Unit
    {
        // ============================= DAMAGE ==============================================================================

        /// <summary>
        /// Deals damage to the target.
        /// </summary>
        /// <param name="targetUnit">Unit to attack. Can be null.</param>
        /// <param name="amount">Damage amount.</param>
        /// <param name="damageType">Damage type.</param>
        /// <param name="directAttack">Direct attacks will trigger attack modifications of the unit(splash) and will try to add attack effectors.</param>
        /// <param name="attackPosition">Can be zero, it is used only for when unit is attacking the ground.</param>
        public void DealDamage(Unit targetUnit, float amount, DamageType damageType, bool directAttack, Vector3 attackPosition, Projectile sourceProjectile = null)
        {
            // Deal damage
            float damageDealt = 0;
            if (targetUnit != null)
            {
                targetUnit.GetDamage(amount, damageType, this.owner, this, directAttack, out damageDealt);
                if(directAttack)Effector.EffectorAdd(this, targetUnit, attackEffectors);
            }

            // 3. After damage callbacks
            foreach (var c in OnAfterDamageDealCallbacks)
            {
                c.Callback(targetUnit, attackPosition, attackEffectors, amount, directAttack, damageType, this, sourceProjectile, owner, c.Level);
            }

            // Splash logic - get all units in radius, damage them accordingly. Only if it is not projectile type attack - it is handled by Projectile.cs
            if (directAttack && isSplash && (melee || attackType == AttackType.Continuous))
            {
                Vector2 splashInitialPosition;
                Unit[] units;
                if (targetUnit == null)
                {
                    splashInitialPosition = new Vector2(attackPosition.x, attackPosition.z);
                    units = Utils.GetUnitsInRadius(splashInitialPosition, splashRadius, owner, splashUnitSelector);

                }
                else
                {
                    splashInitialPosition = new Vector2(targetUnit.transform.position.x, targetUnit.transform.position.z);
                    units = Utils.GetUnitsInRadius(splashInitialPosition, splashRadius, owner, splashUnitSelector, -1, targetUnit);
                }

                for (int i = 0; i < units.Length; i++)
                {
                    // Damage unit based on distance if there is a splashReduction
                    float damageAmount = amount;
                    if (splashReduction != 1)
                    {
                        float dist = Vector2.Distance(new Vector2(units[i].transform.position.x, units[i].transform.position.z), splashInitialPosition);
                        damageAmount = (amount * (1 - (dist / splashRadius) * (1 - splashReduction)));
                    }

                    units[i].GetDamage(damageAmount, damageType, this.owner, this, directAttack, out float _);
                    Effector.EffectorAdd(this, units[i], attackEffectors);
                }
            }

            OnDamageDeal?.Invoke(damageDealt);
        }

        /// <summary>
        /// Gets damage from a specified source in N seconds.
        /// </summary>
        /// <param name="owner">Player that deals the damage, can be -1.</param>
        /// <param name="unitWhoDamages">Unit that deals the damage, can be null.</param>
        /// <param name="time">The amount of time till the damage.</param>
        /// <param name="damage">Damage amount.</param>
        /// <param name="dmgType">Damage type.</param>
        public void GetDamageIn(int owner, Unit unitWhoDamages, float time, float damage, DamageType dmgType)
        {
            GameManager.instance.damageInList.Add((owner, unitWhoDamages, this, damage, dmgType));
            GameManager.instance.damageInTime.Add(time);
        }

        /// <summary>
        /// Gets damage from a specified source.
        /// </summary>
        /// <param name="amount">Damage amount.</param>
        /// <param name="damageType">Damage type.</param>
        /// <param name="attackingPlayer">Player that deals the damage, can be -1.</param>
        /// <param name="attackingUnit">Unit that deals the damage, can be null.</param>
        /// <param name="directAttack">Direct attacks will provoke the target and units around. Direct attacks are type of attacks performed by the attackingUnit itself rather than by the ability or effector.</param>
        /// <param name="damageDealt">Outputs the final damage dealt taking into account armor type and possible Evasion(ability).</param>
        /// <returns></returns>
        public bool GetDamage(float amount, DamageType damageType, int attackingPlayer, Unit attackingUnit, bool directAttack, out float damageDealt)
        {
            // If unit dies at the same frame multiple times, we only count death once
            if (dead)
            {
                damageDealt = 0;
                return false;
            }

            // Provoke the units including self when attacked
            if (directAttack)
            {
                // When we get damage we see if attacking unit is visible, if true we go to attack it if certain conditions are true
                if (!NetworkConnectionHandler.isClient)
                {
                    if (attackingUnit != null && attackingUnit.team != team && attackingUnit.IsVisible(team))
                    {
                        // If not fow visible temporarily reveal the tile
                        if (!FogOfWar.instance.IsVisible(attackingUnit.FoWCell, team)) FogOfWar.instance.TemporalReveal(team, attackingUnit.FoWCell);

                        // Gather all ally units, including own
                        Unit[] allyUnits = Utils.GetUnitsInRadius(new Vector2(transform.position.x, transform.position.z), reactionRange, owner, new UnitSelector(true, true, false, true, false, false, false, true, true, true, false, true));
                        for (int i = 0; i < allyUnits.Length; i++)
                        {
                            // Target should be different
                            if (allyUnits[i].target != attackingUnit)
                            {
                                if ((allyUnits[i].unitState == UnitStates.Idle || allyUnits[i].unitState == UnitStates.AttackMove) && !allyUnits[i].doNotLookForTargets && UnitSelector.IsUnitCompatible(allyUnits[i].owner, attackingUnit, allyUnits[i].attackUnitSelector))
                                {
                                    // Unit does not have target - idling in place or moving to target position
                                    if (allyUnits[i].target == null)
                                    {
                                        allyUnits[i].target = attackingUnit;
                                        allyUnits[i].target.OnReferenceChange += allyUnits[i].TargetReferenceChange;
                                        allyUnits[i].isTargetGround = false;
                                        if (allyUnits[i].attackType == AttackType.Continuous) allyUnits[i].attackCooldown = 0;
                                        if (allyUnits[i].unitState == UnitStates.Idle)
                                        {
                                            allyUnits[i].targetPosition = new Vector2(attackingUnit.transform.position.x, attackingUnit.transform.position.z);
                                            if (allyUnits[i].initialPosition == Vector2.zero) allyUnits[i].initialPosition = new Vector2(allyUnits[i].transform.position.x, allyUnits[i].transform.position.z);
                                        }
                                    }
                                    // Unit has target and it is a building or it can not attack back - prioritize attacking unit, set it as target
                                    else if (allyUnits[i].target.unitType == UnitType.Building || !allyUnits[i].target.canAttack)
                                    {
                                        allyUnits[i].target.OnReferenceChange -= allyUnits[i].TargetReferenceChange;
                                        allyUnits[i].target = attackingUnit;
                                        allyUnits[i].target.OnReferenceChange += allyUnits[i].TargetReferenceChange;
                                        allyUnits[i].isTargetGround = false;
                                        if (allyUnits[i].attackType == AttackType.Continuous) allyUnits[i].attackCooldown = 0;
                                        if (allyUnits[i].unitState == UnitStates.Idle) allyUnits[i].targetPosition = new Vector2(attackingUnit.transform.position.x, attackingUnit.transform.position.z);
                                    }
                                    // Directly attacked unit is not actively attacking, has target that is not a building and can attack
                                    else if (allyUnits[i] == this && firstAttack)
                                    {
                                        // Check for distance of current target and the attacking unit, go for closer one
                                        float distanceToTarget = Vector2.Distance(new Vector2(allyUnits[i].transform.position.x, allyUnits[i].transform.position.z), new Vector2(allyUnits[i].target.transform.position.x, allyUnits[i].target.transform.position.z));
                                        float distanceToAttackingUnit = Vector2.Distance(new Vector2(allyUnits[i].transform.position.x, allyUnits[i].transform.position.z), new Vector2(attackingUnit.transform.position.x, attackingUnit.transform.position.z));

                                        if (distanceToAttackingUnit < distanceToTarget)
                                        {
                                            allyUnits[i].target.OnReferenceChange -= allyUnits[i].TargetReferenceChange;
                                            allyUnits[i].target = attackingUnit;
                                            allyUnits[i].target.OnReferenceChange += allyUnits[i].TargetReferenceChange;
                                            allyUnits[i].isTargetGround = false;
                                            if (allyUnits[i].attackType == AttackType.Continuous) allyUnits[i].attackCooldown = 0;
                                            if (allyUnits[i].unitState == UnitStates.Idle) allyUnits[i].targetPosition = new Vector2(attackingUnit.transform.position.x, attackingUnit.transform.position.z);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // For evasion or damage reduction on chance skills
            // Lowest acquired damage is selected

            // [Interflow fix 2026-07-24 combat-hub] Модификаторы входящего урона, которые знают АТАКУЮЩЕГО и ТИП урона
            // (промах ослеплённого, уязвимость к типу урона, снижение урона). Штатный колбэк ниже не передаёт
            // ни атакующего, ни тип. Считаем ДО штатных колбэков, чтобы промах не «съедал» поглощающий щит,
            // а щит поглощал уже итоговую величину. Логика целиком в нашем InterflowCombat.cs.
            amount = InterflowCombat.ModifyIncomingDamage(this, attackingUnit, damageType, amount, directAttack);

            float finalDamage = amount;
            foreach (var c in OnBeforeGetDamageCallbacks)
            {
                float damageChanged = c.Callback(this, c.Level, amount, directAttack);
                if (damageChanged < finalDamage) finalDamage = damageChanged;
                else if (finalDamage >= amount && damageChanged > finalDamage) finalDamage = damageChanged;
            }
            amount = finalDamage;

            // [Interflow fix 2026-07-24 combat-hub] Пробитие брони: атакующий игнорирует долю защиты цели.
            // Штатной точки для этого нет, а хук жертвы не знает, кто бьёт.
            float effectiveArmor = damageType.ignoresArmor ? 0f : InterflowCombat.EffectiveArmor(this, attackingUnit, armor);

            // Final damage amount based on damage and armor type
            damageDealt = amount * GameManager.instance.damageToArmor[armorType.index * GameManager.instance.DTAWidth + damageType.index] * (1 - ((0.06f * effectiveArmor) / (1 + 0.06f * effectiveArmor)));

            // Checks and Get damage
            if (damageDealt < 0) damageDealt = 0;

            if (damageDealt > health) damageDealt = health;

            // Successful direct hits include killing blows; misses grant no resource.
            if(!NetworkConnectionHandler.isClient&&damageDealt>0){if(attackingUnit){var resource=attackingUnit.GetComponent<CombatResource>();if(resource)resource.Hit(this,damageDealt,directAttack);}var ownResource=GetComponent<CombatResource>();if(ownResource)ownResource.Threatened(attackingUnit);}
            bool died = ChangeHP(-damageDealt);
            DamageLinkGroup.NotifyDamage(this, damageDealt);
            if (died) Die(attackingPlayer, attackingUnit);
            else if (hasHitAnim && directAttack && FoWVisible) animator.CrossFade("hit", crossFadeTime, 0, 0f); //animator.Play("hit", 0, 0.01f);

            // [Interflow fix 2026-07-24 combat-hub] Реакции на получение урона (контрудар, ответная заморозка).
            // Только если юнит выжил: посмертные эффекты живут отдельно, в DeathEffects (OnDie).
            if (!died) InterflowCombat.NotifyDamaged(this, attackingUnit, damageType, damageDealt, directAttack);

            return died;
        }

        // ============================= DIE ==============================================================================

        /// <summary>
        /// Call this to kill or destroy units.
        /// </summary>
        /// <param name="playerThatKills">Player that gets rewards for kill. Can be -1.</param>
        /// <param name="unitThatKills">Unit that gets rewards for kill. Can be null.</param>
        /// <param name="rewards">Should the rewards for killing be given.</param>
        /// <param name="calledByServer">Units don`t die unless server says so. When false clients will be able to destroy the unit locally and the server will not sync the death.</param>
        /// <param name="destroy">When destroyed no sound, animation and VFX is played.</param>
        public void Die(int playerThatKills = -1, Unit unitThatKills = null, bool rewards = true, bool calledByServer = true, bool destroy = false)
        {
            if (calledByServer && NetworkConnectionHandler.isClient) return; // Units don`t die unless server says so
            if (dead) return; // Anti-error if unit should die at the same frame twice
            dead = true;

            OnReferenceChange?.Invoke(null); // If someone was targeting this unit, we null the target for them.
            OnDie?.Invoke(this, playerThatKills, unitThatKills, rewards);

            if (!firstAttack) AttackStop();
            if (target != null) target.OnReferenceChange -= TargetReferenceChange;
            OnDie -= GameManager.instance.OnSpecificUnitDie;
            CommandSoundDestroy();

            // End if using an ability
            EndActiveAbility(true, false);

            // Add XP to surrounding units (if ON)
            if (rewards) Utils.HandleRewardGain(this, playerThatKills, unitThatKills);

            // If processes exists we should cancel them before dying
            if (canProcess)
            {
                int i = 0;
                while (activeProcess[i] != null)
                {
                    CancelProcess(i);
                    i++;
                }
            }

            // If completed unit - lock teck, produced limited resources are taken back
            if (!isBeingBuilt)
            {
                // When this unit dies we should lock the tech that this unit unlocks. If there are other units of this type, tech will not be locked
                TechnologyManager.instance.LockTeck(this);

                // Production and Costs
                if (resourceProduced != null)
                {
                    for (int i = 0; i < resourceProduced.Length; i++)
                    {
                        // [Interflow fix 2026-08-01 limited-res-sync] серверный учёт + рассылка (см. Unit.Init).
                        if (resourceProduced[i].type.limited) GameResources.instance.ChangeLimit(owner, resourceProduced[i], true, true);
                        // For regular resource types we do not take them away
                    }
                }
            }

            // Costs
            if (resourceCost != null)
            {
                for (int i = 0; i < resourceCost.Length; i++)
                {
                    // [Interflow fix 2026-08-01 limited-res-sync] возврат лидерства на смерти — тоже серверный (с рассылкой).
                    if (resourceCost[i].type.limited) GameResources.instance.ChangeAmount(owner, resourceCost[i], 1, false, true); // We decrease the limited resource usage
                                                                                                                   // For regular resource types we do not add them back
                }
            }

            // Inventory: Drop items in inventory - Only host
            OnFollowReach -= PickUpItem;
            if (!NetworkConnectionHandler.isClient)
            {
                for (int i = 0; i < InventorySize; i++)
                {
                    if (items[i] != null && items[i].dropOnDeath)
                    {
                        if (ItemDropped.Spawn(items[i], itemCharges[i], GetAbilityCooldown(i, true), transform.position, unitRadius))
                        {
                            // Why remove if unit is dead anyway?
                            // RemoveItem(i, true, true);
                        }
                    }
                }
            }

            // If not disabled already
            if (gameObject.activeSelf)
            {
                // Remove from cell info
                FogOfWar.instance.CellRemove(this);
                Grid.RemoveFromChunk(this);

                // Remove from selection
                Presentation.Selection?.RemoveFromSelection(this);

                // View Blocker
                if (viewBlocker || singleCellViewBlocker) FogOfWar.instance.UnitViewBlockCalculate(this, true);

                // Unsubscribe from events this unit was subscribed to
                Unsubscribe();
            }

            GameManager.instance.OnTeamChange -= TeamChanged;
            GameManager.instance.Tick -= CooldownCalculate;
            if (hpSync) HPSyncFalse();
            if (mpSync) MPSyncFalse();
            if (xpSync) XPSyncFalse();

            // Destroy invisibility replicate
            if (invisibilityReplica != null) Destroy(invisibilityReplica.gameObject);

            // Send die trigger to clients
            if (calledByServer && NetworkManager.Singleton.IsServer) NetworkDataSync.instance.DieTriggerSend(netID, playerThatKills, unitThatKills, rewards, destroy);
            // Remove from net ID collection
            SlotManager.instance.RemoveNetID(netID, this);

            // We play out animations, create static object only if unit is to die, not to be destroyed
            if (!destroy)
            {
                // If only visible to player
                if (FogOfWar.instance.IsVisible(FoWCell, SlotManager.instance.currentTeam))
                {
                    // Play Sound/Effects/Animation
                    if (deathSound.Length > 0) Presentation.Audio?.PlaySoundClip(deathSound, this.transform, 1);
                    // [Interflow 2026-08-01 server-opt] VFX смерти на дедике не спавним (рендера нет).
                    if (dieVFX && !Utils.Headless) Instantiate(dieVFX, this.transform.position, Quaternion.identity);
                    AnimatorSetBool(AnimationState.Reset, true);

                    // Animation and Destroy
                    if (deathAnimationCount != 0)
                    {
                        string deathAnimName = "death" + UnityEngine.Random.Range(0, deathAnimationCount);

                        // Play random death animation
                        animator.CrossFade(deathAnimName, crossFadeTime, 0, 0f); // animator.Play(deathAnimName, 0, 0.01f);

                        // Remove components
                        if (GetComponent<NavMeshAgent>()) Destroy(GetComponent<NavMeshAgent>());
                        if (GetComponent<NavMeshObstacle>()) Destroy(GetComponent<NavMeshObstacle>());
                        if (GetComponent<BoxCollider>()) Destroy(GetComponent<BoxCollider>());
                        if (GetComponent<CapsuleCollider>()) Destroy(GetComponent<CapsuleCollider>());
                        foreach (Transform child in transform)
                        {
                            if (child.name == "MiniMapIcon(Clone)" || child.name == "HealthBar(Clone)" || child.name == "VFXHolder") Destroy(child.gameObject);
                        }
                        Utils.DestroyUnitDependents(this); // [Interflow fix 2026-08-01 require-component-die] иначе Destroy(this) отклоняется из-за [RequireComponent(typeof(Unit))] (DeathEffects и др.) и Unit остаётся на трупе
                        Destroy(this);

                        // Destroy renderer of this object in N seconds
                        Destroy(gameObject, deathAnimationLength);
                        return;
                    }
                }
                else if (staticCopy)
                {
                    // If not visible and static copy exists, we add reveal and destroy to static copy
                    var temp = staticCopy.gameObject.AddComponent<VFXEnabler>();
                    temp.destroyUponDiscovery = true;
                    // We also enable colliders
                    if (staticCopy.GetComponent<BoxCollider>()) staticCopy.GetComponent<BoxCollider>().enabled = true;
                    else staticCopy.GetComponent<CapsuleCollider>().enabled = true;
                }
            }

            // No animation for death, destroy immediately
            Destroy(gameObject);
        }

    }
}
