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
        /// <param name="sourceAbility">Умение-источник для диагностики очереди пакетов (решение Artsiom 05.09.2026);
        /// null — автоатака (все вызовы из Unit.State.cs). Неавтоатачные вызывающие обязаны назвать своё умение.</param>
        public void DealDamage(Unit targetUnit, float amount, DamageType damageType, bool directAttack, Vector3 attackPosition, Ability sourceAbility = null)
        {
            // Одна запись урона — пакет собирается фабрикой: пробитие и промах бьющего кладутся здесь,
            // состояния автоатаки едут в пакете и вешаются приёмником только при прямой атаке (Р7).
            DamagePacket packet = DamagePacket.Create(amount, damageType, this.owner, this, directAttack, sourceAbility, attackEffectors);
            DealDamage(targetUnit, in packet, attackPosition);
        }

        /// <summary>
        /// [Interflow fix 2026-09-04 damage-full-packet] Сторона бьющего при ПОЛНОМ пакете (шаг 4 схемы, решение
        /// Artsiom Р7, 03.09.2026). Цели уходит пакет; соседей по разлёту находит бьющий и шлёт по пакету каждому
        /// (те же состояния атаки); у бьющего остаются колбэки «после удара» и <see cref="OnDamageDeal"/>.
        /// Отдельного наложения состояний атаки здесь больше нет — их вешает приёмник, и только при прямой атаке:
        /// урон умения «от имени юнита» состояний автоатаки больше не вешает.
        /// Клиент (решение Р1 В): пакет цели уходит только ради анимации удара — приёмник у клиента ничего
        /// не считает; колбэки, разлёт и OnDamageDeal у клиента не исполняются.
        /// </summary>
        /// <param name="packet">Пакет к основной цели; для соседей по разлёту из него берутся источник и состояния.</param>
        public void DealDamage(Unit targetUnit, in DamagePacket packet, Vector3 attackPosition)
        {
            if (NetworkConnectionHandler.isClient)
            {
                if (targetUnit != null) targetUnit.GetDamage(in packet, out float _);
                return;
            }

            // Deal damage
            float damageDealt = 0;
            if (targetUnit != null) targetUnit.GetDamage(in packet, out damageDealt);

            float amount = packet.TotalAmount;
            DamageType damageType = packet.damageType;
            bool directAttack = packet.directAttack;

            // 3. After damage callbacks
            foreach (var c in OnAfterDamageDealCallbacks)
            {
                c.Callback(targetUnit, attackPosition, attackEffectors, amount, directAttack, damageType, this, null, owner, c.Level);
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

                    // Сосед получает свой пакет: тот же источник, признак и состояния атаки, своя величина.
                    DamagePacket neighbour = packet;
                    neighbour.amount = damageAmount;
                    neighbour.moreRecords = null;
                    units[i].GetDamage(in neighbour, out float _);
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
        /// <param name="sourceAbility">Умение-источник для диагностики очереди пакетов (решение Artsiom 05.09.2026); null — без умения.</param>
        public void GetDamageIn(int owner, Unit unitWhoDamages, float time, float damage, DamageType dmgType, Ability sourceAbility)
        {
            GameManager.Instance.damageInList.Add((owner, unitWhoDamages, this, damage, dmgType, sourceAbility));
            GameManager.Instance.damageInTime.Add(time);
        }

        // ================= ПРИЁМНИК ==============================================================
        // Приёмник этого юнита — единственная дверь внутрь юнита (схема «пакет и приёмник», §1, §4).
        // Ссылка кешируется: искать компонент на каждый удар нельзя, это горячий путь
        // (руководство Unity, programming best practices). Поле живёт ровно столько же, сколько юнит.
        UnitReceiver receiver;

        /// <summary>
        /// Приёмник этого юнита: при первом обращении навешивается и привязывается к юниту.
        /// Идемпотентно — второго приёмника на юните не появится. Префабы юнитов не правятся,
        /// навешивание кодом — принятый в проекте приём (ControlImmunityPassive.cs:40, BleedOnMove.cs:44).
        ///
        /// Публичен с шага 1 схемы «пакет и приёмник» (решение Artsiom 28.08.2026): <c>Knockback</c>
        /// спрашивает приёмник цели про иммунитет к контролю вместо собственного поиска компонента.
        /// Зовущая сторона обязана сначала убедиться, что юнит жив: обращение создаёт компонент
        /// на игровом объекте, и на трупе этого делать нельзя.
        /// </summary>
        public UnitReceiver ReceiverEnsure()
        {
            if (receiver != null) return receiver;

            if (!TryGetComponent(out receiver)) receiver = gameObject.AddComponent<UnitReceiver>();
            receiver.Init(this);

            return receiver;
        }

        /// <summary>
        /// Воронка приёма урона: ЕДИНСТВЕННЫЙ вход для любого урона (шаг 4 схемы «пакет и приёмник», решение
        /// Artsiom Р8, 03.09.2026: все источники строят один и тот же полный пакет — <see cref="DamagePacket.Create"/>).
        /// Здесь: отсев мёртвого, провокация (как есть — Р6 отложено), дальше пакет уходит в очередь и приёмник
        /// (<see cref="UnitReceiver.Dispatch"/>). Старая сигнатура с шестью аргументами снесена.
        /// </summary>
        /// <param name="packet">Полный пакет: записи урона, источник, признак прямой атаки, состояния атаки, пробитие и промах бьющего.</param>
        /// <param name="damageDealt">Сколько здоровья реально снято. Для пакета, ушедшего в очередь (вызов из реакции), — 0.</param>
        /// <returns>Погиб ли юнит от этого пакета (для отложенного пакета — false).</returns>
        public bool GetDamage(in DamagePacket packet, out float damageDealt)
        {
            // If unit dies at the same frame multiple times, we only count death once
            if (dead)
            {
                damageDealt = 0;
                return false;
            }

            Unit attackingUnit = packet.attackingUnit;

            // Состав пакета — ОДНА точка на весь урон: через эту воронку проходит каждый принятый пакет
            // ровно один раз (шаг 4 схемы). Строка собирается только на полном уровне: путь горячий —
            // сюда заходят автоатаки, урон в секунду состояний, ауры, зоны и реакции.
            if (InterflowDebug.FullOn)
                InterflowDebug.Full("ПАКЕТ УРОН: → " + InterflowDebug.Name(this) +
                                    " | умение=" + (packet.sourceAbility != null ? packet.sourceAbility.name : "нет") +
                                    " | от=" + InterflowDebug.Name(attackingUnit) + " (игрок " + packet.attackingPlayer + ")" +
                                    " | прямая=" + (packet.directAttack ? "да" : "нет") +
                                    " | записей=" + packet.RecordCount + ": " + PacketRecordsText(in packet) +
                                    " | состояния атаки=" + PacketEffectorsText(packet.attackEffectors) +
                                    " | пробитие=" + packet.armorPierce.ToString("0.##") +
                                    " | промах=" + packet.missChance.ToString("0.##") +
                                    " | поколение=" + packet.generation);

            // Provoke the units including self when attacked
            if (packet.directAttack)
            {
                // When we get damage we see if attacking unit is visible, if true we go to attack it if certain conditions are true
                if (!NetworkConnectionHandler.isClient)
                {
                    if (attackingUnit != null && attackingUnit.team != team && attackingUnit.IsVisible(team))
                    {
                        // If not fow visible temporarily reveal the tile
                        if (!FogOfWar.Instance.IsVisible(attackingUnit.FoWCell, team)) FogOfWar.Instance.TemporalReveal(team, attackingUnit.FoWCell);

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

            // Приёмная часть — в UnitReceiver (нулевой шаг), очередь — UnitReceiver.Queue.cs (шаг 4).
            // Провокация выше в приёмник НЕ переносится: решение по ней отложено (Р6, 03.09.2026).
            return UnitReceiver.Dispatch(this, in packet, out damageDealt);
        }

        /// <summary>
        /// Записи урона пакета строкой — для лога полного уровня. Зовётся ТОЛЬКО из-под гейта уровня:
        /// перебирает записи и склеивает строку, а воронка урона — горячий путь.
        /// </summary>
        static string PacketRecordsText(in DamagePacket packet)
        {
            string text = null;

            for (int i = 0; i < packet.RecordCount; i++)
            {
                DamageRecord record = packet.Record(i);
                string one = record.amount.ToString("0.#") +
                             " (" + (record.damageType != null ? record.damageType.name : "без типа") + ")";

                text = text == null ? one : text + ", " + one;
            }

            return text ?? "нет";
        }

        /// <summary>
        /// Имена состояний атаки из пакета строкой — для лога полного уровня. Зовётся ТОЛЬКО из-под гейта.
        /// </summary>
        static string PacketEffectorsText(Effector[] effectors)
        {
            if (effectors == null || effectors.Length == 0) return "нет";

            string text = null;

            for (int i = 0; i < effectors.Length; i++)
            {
                if (effectors[i] == null) continue;

                text = text == null ? effectors[i].name : text + ", " + effectors[i].name;
            }

            return text ?? "нет";
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
            OnDie -= GameManager.Instance.OnSpecificUnitDie;
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
                TechnologyManager.Instance.LockTeck(this);

                // Production and Costs
                if (resourceProduced != null)
                {
                    for (int i = 0; i < resourceProduced.Length; i++)
                    {
                        // [Interflow fix 2026-08-01 limited-res-sync] серверный учёт + рассылка (см. Unit.Init).
                        if (resourceProduced[i].type.limited) GameResources.Instance.ChangeLimit(owner, resourceProduced[i], true, true);
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
                    if (resourceCost[i].type.limited) GameResources.Instance.ChangeAmount(owner, resourceCost[i], 1, false, true); // We decrease the limited resource usage
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
                FogOfWar.Instance.CellRemove(this);
                Grid.RemoveFromChunk(this);

                // Remove from selection
                Presentation.Selection?.RemoveFromSelection(this);

                // View Blocker
                if (viewBlocker || singleCellViewBlocker) FogOfWar.Instance.UnitViewBlockCalculate(this, true);

                // Unsubscribe from events this unit was subscribed to
                Unsubscribe();
            }

            GameManager.Instance.OnTeamChange -= TeamChanged;
            GameManager.Instance.Tick -= CooldownCalculate;
            if (hpSync) HPSyncFalse();
            if (mpSync) MPSyncFalse();
            if (xpSync) XPSyncFalse();

            // Destroy invisibility replicate
            if (invisibilityReplica != null) Destroy(invisibilityReplica.gameObject);

            // Send die trigger to clients
            if (calledByServer && NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.DieTriggerSend(netID, playerThatKills, unitThatKills, rewards, destroy);
            // Remove from net ID collection
            SlotManager.Instance.RemoveNetID(netID, this);

            // We play out animations, create static object only if unit is to die, not to be destroyed
            if (!destroy)
            {
                // If only visible to player
                if (FogOfWar.Instance.IsVisible(FoWCell, SlotManager.Instance.currentTeam))
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
