// [Interflow fix 2026-06-27] Все подсказки [Tooltip] в этом файле локализованы на русский (правка ассета, разрешена Artsiom; только текст Tooltip). Оригинал EN: _BACKUP_TOOLTIPS/Scripts/Unit.cs. Реестр: wiki concepts/asset-fork-debt.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Unit.Lifecycle.cs — жизненный цикл (COLLIDERS/FOW/DISABLE/OWNERSHIP/SPAWN). Вырезано 1:1 из Unit.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class Unit
    {
        // ============================= COLLIDERS ==============================================================================

        /// <summary>
        /// Creates necessary colliders for this unit
        /// </summary>
        public void AddColliders()
        {
            // Add Collider
            if (GetComponent<BoxCollider>() == null && GetComponent<CapsuleCollider>() == null)
            {
                CapsuleCollider capsuleCollider = gameObject.AddComponent<CapsuleCollider>();
                capsuleCollider.radius = unitRadius;
                capsuleCollider.height = unitHeight;
                capsuleCollider.center = new Vector3(0, unitHeight * 0.5f, 0);
            }
        }

        /// <summary>
        /// Returns if this unit will intersect other units at the specified location.
        /// </summary>
        /// <returns></returns>
        public bool CollisionCheck(Vector3 position)
        {
            // Check the intersections with units
            if (Utils.GetIntersectedUnit(new Vector2(position.x, position.z), unitRadius, null, !isAir)) return true;

            // Collider tests
            BoxCollider boxCollider = GetComponent<BoxCollider>();
            if (boxCollider)
            {
                // Box Collider
                if (Physics.BoxCast(new Vector3(position.x, Utils.raycastPointY, position.z), boxCollider.size * 0.5f * transform.localScale.x, Vector3.down, out _, transform.rotation, Utils.raycastPointY * 5f, Utils.defaultMask)) // Check against units/buildings/objects
                {
                    return true;
                }
            }
            else
            {
                // Capsule Collider
                if (Physics.SphereCast(new Vector3(position.x, Utils.raycastPointY, position.z), unitRadius, Vector3.down, out _, Utils.raycastPointY * 5f, Utils.defaultMask)) // Check against units/buildings/objects
                {
                    return true;
                }
            }

            return false;
        }

        // ============================= FOW ==============================================================================

        /// <summary>
        /// Creates the static copy of the unit.
        /// </summary>
        public void CreateStaticCopy()
        {
            if (unitType == UnitType.Tree || unitType == UnitType.StaticDestructible || (GameManager.instance.showBuildingsInFow && unitType == UnitType.Building))
            {
                // Enable
                if (staticCopy)
                {
                    staticCopy.gameObject.SetActive(true);
                    return;
                }

                // Create
                Unit tempUnit = GameObject.Instantiate(this);
                Utils.UnitRemoveComponents(tempUnit);
                // Disable colliders, we enabled them only if unit dies
                if (tempUnit.GetComponent<BoxCollider>()) tempUnit.GetComponent<BoxCollider>().enabled = false;
                else tempUnit.GetComponent<CapsuleCollider>().enabled = false;

                staticCopy = tempUnit.transform;
                staticCopy.position = transform.position;
                staticCopy.rotation = transform.rotation;
                staticCopy.tag = "StaticDestructible";

                GameManager.instance.Tick += UpdateStaticPosition;
            }
        }

        /// <summary>
        /// Destroys the static copy of the unit.
        /// </summary>
        public void DestroyStaticCopy()
        {
            if (staticCopy) staticCopy.gameObject.SetActive(false);
        }

        /// <summary>
        /// When unit has obstacle component it will change its positions if intersecting the ground after 2 frames or so. We fix the position of static object.
        /// </summary>
        public void UpdateStaticPosition()
        {
            staticCopy.position = transform.position;
            staticCopy.rotation = transform.rotation;
            GameManager.instance.Tick -= UpdateStaticPosition;
        }

        // ============================= DISABLE/ENABLE ==============================================================================

        /// <summary>
        /// Removes unit from the play area, but does not kill it. Assumes it is always called by the local player, not server.
        /// </summary>
        public void Disable()
        {
            // End if using an ability, attacking etc
            EndActiveAbility(true, false);
            Idle();
            CommandSoundDestroy();

            // Remove from cell info
            FogOfWar.instance.CellRemove(this);
            Grid.RemoveFromChunk(this);

            // Remove from selection
            Presentation.Selection?.RemoveFromSelection(this);

            // View Blocker
            if (viewBlocker || singleCellViewBlocker) FogOfWar.instance.UnitViewBlockCalculate(this, true);

            // Unsub
            Unsubscribe();

            // Reference change
            OnReferenceChange?.Invoke(null);

            gameObject.SetActive(false);
            disabled = true;
        }

        /// <summary>
        /// Adds the unit back into the play area. The unit must be in a Disable() state.
        /// </summary>
        public void Enable()
        {
            if (isAir)
            {
                airReplica.position = new Vector3(transform.position.x + Utils.airOffsetX, 0, transform.position.z);
                transform.position = new Vector3(transform.position.x, Utils.airUnitElevation, transform.position.z);
            }

            // Initial assign of the unit to the grid
            FoWCell = FogOfWar.instance.CellAssignment(this, true);
            Grid.AssignToChunkInitial(this);

            // View Blocker
            if (viewBlocker || singleCellViewBlocker) FogOfWar.instance.UnitViewBlockCalculate(this);

            // Set active
            gameObject.SetActive(true);

            // Sub
            Subscribe();
            disabled = false;
        }

        /// <summary>
        /// Unsubscribes from certain Events this unit was subscribed to.
        /// </summary>
        public void Unsubscribe()
        {
            // State updaters
            GameManager.instance.Tick -= StunUpdate;
            GameManager.instance.Tick -= DisarmUpdate;
            GameManager.instance.Tick -= MuteUpdate;
            GameManager.instance.Tick -= PolymorphUpdate;

            // Command sound
            if (commandSound != null) Destroy(commandSound.gameObject);
            GameManager.instance.Tick -= CommandSoundTimerUpdate;

            TechnologyManager.instance.OnTechUnlock[owner] -= AllAbilityLockLevelsCalculate;

            if (idleRandomTime != 0) GameManager.instance.Tick -= RandomIdleAnimation;
            GameManager.instance.Tick -= HandleEffectors;
            GameManager.instance.Tick -= HandleEveryFrameAbilities;

            if (staticCopy) GameManager.instance.Tick -= UpdateStaticPosition;
        }

        /// <summary>
        /// Subscribes back to certain events.
        /// </summary>
        private void Subscribe()
        {
            TechnologyManager.instance.OnTechUnlock[owner] += AllAbilityLockLevelsCalculate;

            if (idleRandomTime != 0) GameManager.instance.Tick += RandomIdleAnimation;
            GameManager.instance.Tick += HandleEffectors;
            GameManager.instance.Tick += HandleEveryFrameAbilities;
        }

        /// <summary>
        /// What should happen when team of the current player changes.
        /// </summary>
        public void TeamChanged()
        {
            // Healthbar colors change?
            if (SlotManager.instance.currentTeam == team) FoWVisible = true;
        }

        /// <summary>
        /// Network: after the data is sent we clear the flags.
        /// </summary>
        public void HPSyncFalse()
        {
            hpSync = false;
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.onHPCleared -= HPSyncFalse;
        }

        /// <summary>
        /// Network: after the data is sent we clear the flags.
        /// </summary>
        public void MPSyncFalse()
        {
            mpSync = false;
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.onMPCleared -= MPSyncFalse;
        }

        /// <summary>
        /// Network: after the data is sent we clear the flags.
        /// </summary>
        public void XPSyncFalse()
        {
            xpSync = false;
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.onXPCleared -= XPSyncFalse;
        }

        // ============================= OWNERSHIP ==============================================================================

        /// <summary>
        /// Sets the new owner for this unit. For server: Set initial ownership of the unit.
        /// </summary>
        /// <param name="newOwner">New owner of the unit.</param>
        public void SetOwnership(int newOwner)
        {
            // Initial set
            if (team == -1)
            {
                owner = newOwner;
                team = SlotManager.instance.playerTeam[newOwner];

                // Start will set all necessary parameters
            }
            // Change the ownership
            else if (owner != newOwner)
            {
                GameManager.instance.RemoveUnitCount(this);
                int newTeam = SlotManager.instance.playerTeam[newOwner];

                // UI Reset
                if (Presentation.Selection?.ActiveUnit != null)
                {
                    Presentation.UI?.UnsubscribeToUnit(Presentation.Selection.ActiveUnit);
                }

                // Healthbar
                // [Interflow 2026-08-01 server-opt] На дедике баров нет — блок пропускаем.
                if (!Utils.Headless && newTeam != team && unitType != UnitType.StaticDestructible && unitType != UnitType.Item && unitType != UnitType.Tree)
                {
                    var healthBar = transform.Find("HealthBar(Clone)");
                    if (healthBar) { healthBar.SetParent(null); GameManager.Destroy(healthBar.gameObject); }
                    if (newTeam != SlotManager.instance.currentTeam && newTeam != (int)Teams.NeutralPassive) Instantiate(ReferenceManager.instance.healthBarEnemy, this.transform).name = "HealthBar(Clone)";
                    else Instantiate(ReferenceManager.instance.healthBar, this.transform);
                }

                // Minimap icon
                // [Interflow 2026-08-01 server-opt] На дедике иконку не создаём (миникарты нет).
                if (!Utils.Headless)
                {
                    var minimapIcon = transform.Find("MiniMapIcon(Clone)");
                    if (minimapIcon == null) minimapIcon = Instantiate(ReferenceManager.instance.miniMapIcon, this.transform);
                    // [Interflow fix 2026-08-01 grid-headless] гейт стрипнутого SpriteRenderer (как в Initialize).
                    SpriteRenderer minimapIconSR = minimapIcon.GetComponent<SpriteRenderer>();
                    if (minimapIconSR != null) minimapIconSR.color = SlotManager.instance.playerColors[newOwner];
                }

                // FoW Cell assignment
                if (newTeam != team)
                {
                    // Remove from previous team
                    FogOfWar.instance.CellRemove(this);

                    // Add new team
                    team = newTeam;
                    if (FogOfWar.instance.TurnOff) FoWVisible = true;
                    else
                    {
                        FoWCell = FogOfWar.instance.CellAssignment(this, true);
                        if (SlotManager.instance.currentTeam == team) FoWVisible = true;
                    }
                }

                // Below for owner != newOwner
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
                        if (resourceCost[i].type.limited) GameResources.instance.ChangeAmount(owner, resourceCost[i]); // We decrease the limited resource usage
                                                                                                                       // For regular resource types we do not add them back
                    }
                }

                // TechTree subscribe to it
                TechnologyManager.instance.OnTechUnlock[owner] -= AllAbilityLockLevelsCalculate; // When new tech is unlocked, check if there are any abilities to unlock
                TechnologyManager.instance.OnTechUnlock[newOwner] += AllAbilityLockLevelsCalculate;

                // Below data for new ownership
                owner = newOwner;
                GameManager.instance.AddUnitCount(this);
                if (!isBeingBuilt)
                {
                    // Unlock TechTree when this unit is created, if there is something to unlock
                    TechnologyManager.instance.UnlockTech(this);

                    // Resource Production when unit is created. For limited resource it increases the limits
                    if (resourceProduced != null)
                    {
                        for (int i = 0; i < resourceProduced.Length; i++)
                        {
                            if (resourceProduced[i].type.limited) GameResources.instance.ChangeLimit(owner, resourceProduced[i]);
                            else GameResources.instance.ChangeAmount(owner, resourceProduced[i]);
                        }
                    }
                }

                // Resource cost: only For limited resource, it increases the usage of it
                if (resourceCost != null)
                {
                    for (int i = 0; i < resourceCost.Length; i++)
                    {
                        if (resourceCost[i].type.limited) GameResources.instance.ChangeAmount(owner, resourceCost[i], 1, true); // We decrease the resource
                    }
                }

                // Colors
                SetPlayerColor();
                // ResetOverlayColor(); Not needed?

                // Projectors
                if (selectionCircle) DestroyImmediate(selectionCircle.gameObject);
                if (Presentation.Selection?.IsSelected(this) ?? false) ProjectorsEnable();

                // Initial ability lock and level calculation 
                AllAbilityLockLevelsCalculate();

                // UI Refresh
                if (Presentation.Selection?.ActiveUnit != null)
                {
                    Presentation.UI?.SubscribeToUnit();
                }

                // Healthbar child was destroyed and recreated — rebuild the renderers list so
                // HideRenderers/ShowRenderers don't hold a reference to the destroyed child.
                renderers = new List<Transform>();
                for (int i = 0; i < transform.childCount; i++)
                {
                    Transform child = transform.GetChild(i);
                    if (i == 0 || child == selectionCircle || child.gameObject == mainRenderer) continue;
                    renderers.Add(child);
                }
            }
        }

        // ============================= SPAWN ==============================================================================

        /// <summary>
        /// Spawns the new unit at specified position if possible.
        /// </summary>
        /// <param name="unitRef">What kind of unit should be spawned.</param>
        /// <param name="position">Position for spawn.</param>
        /// <param name="rotation">Rotatin of the unit.</param>
        /// <param name="owner">Owner of the unit.</param>
        /// <param name="positionR">If position is occuped the radius at the position to check for available spots.</param>
        /// <returns>Spawned units, null if spawn was not possible.</returns>
        public static Unit Spawn(Unit unitRef, Vector3 position, float rotation, int owner, float positionR = 0)
        {
            if (NetworkConnectionHandler.isClient) return null;

            positionR = (positionR == 0) ? unitRef.unitRadius : positionR;
            position = Utils.CircleCheck(new Vector2(position.x, position.z), positionR, unitRef.unitRadius, unitRef.isGround, unitRef.isWater, unitRef.isAir);
            if (position == Vector3.zero) return null;

            return SpawnInternal(unitRef, position, rotation, owner);
        }

        /// <summary>
        /// Spawns the new unit at specified position if possible.
        /// </summary>
        /// <param name="typeID">What type of unit should be spawned.</param>
        /// <param name="position">Position for spawn.</param>
        /// <param name="rotation">Rotatin of the unit.</param>
        /// <param name="owner">Owner of the unit.</param>
        /// <param name="positionR">If position is occuped the radius at the position to check for available spots.</param>
        /// <returns>Spawned units, null if spawn was not possible.</returns>
        public static Unit Spawn(int typeID, Vector3 position, float rotation, int owner, float positionR = 0)
        {
            if (NetworkConnectionHandler.isClient) return null;

            // Get unit type to spawn
            if (GameManager.instance.gameUnits.TryGetValue(typeID, out Unit unitRef))
            {
                positionR = (positionR == 0) ? unitRef.unitRadius : positionR;
                position = Utils.CircleCheck(new Vector2(position.x, position.z), positionR, unitRef.unitRadius, unitRef.isGround, unitRef.isWater, unitRef.isAir);
                if (position == Vector3.zero) return null;

                return SpawnInternal(unitRef, position, rotation, owner);
            }
            else
            {
                Debug.Log("Unit type ID " + typeID + " not found!");
                return null;
            }
        }

        /// <summary>
        /// Instantiates the unit at specified position without any checks.
        /// </summary>
        /// <param name="unitRef">What kind of unit should be spawned.</param>
        /// <param name="position">Position for spawn.</param>
        /// <param name="rotation">Rotatin of the unit.</param>
        /// <param name="owner">Owner of the unit.</param>
        /// <param name="netID">Should new netID be calculated or set to provided one.</param>
        /// <returns>Instantiated unit.</returns>
        public static Unit SpawnInternal(Unit unitRef, Vector3 position, float rotation, int owner, UInt16 netID = 0)
        {
            // Instantiate
            Unit unit = Instantiate(unitRef, position, Quaternion.identity);
            unit.SetUnitRotation(rotation);
            unit.SetOwnership(owner);
            SlotManager.instance.AssignNetID(unit, netID);
            unit.AddColliders();
            unit.Initialize();

            // Play the sound
            if (unit.readySound.Length > 0) Presentation.Audio?.PlayCommandSound(unit, unit.readySound, 1, true);

            // Network sync
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.UnitSpawn(unit.unitTypeID, position, rotation, owner, unit.netID);
            return unit;
        }
    }
}
