// [Interflow fix 2026-06-27] Все подсказки [Tooltip] в этом файле локализованы на русский (правка ассета, разрешена Artsiom; только текст Tooltip). Оригинал EN: _BACKUP_TOOLTIPS/Scripts/Unit.cs. Реестр: wiki concepts/asset-fork-debt.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Unit.Init.cs — инициализация (Initialize/компоненты/визуал). Вырезано 1:1 из Unit.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class Unit
    {
        // ============================= INITIALIZE ==============================================================================

        /// <summary>
        /// Initializes all the necessary parameters for this unit.
        /// </summary>
        public void Initialize()
        {
            // Unit Type ID check
            if (unitTypeID == 0) Debug.LogError("Unit " + unitName + " does not have UnitTypeID! If it is placed in the scene object, you must create prefab of it, delete it, in the menu choose Assign Unit IDs, and then place the prefab!");

            // Set Ownership/Team
            if (unitType == UnitType.Item) SetOwnership((int)Players.NeutralPassive);
            else SetOwnership(owner);
            GameManager.instance.OnTeamChange += TeamChanged;

            // Unique custom NetID
            SlotManager.instance.AssignNetID(this);

            // Subsctibe to winning conditions (specific units should die)
            GameManager.instance.SubscribeToSpecificWinningConditions(this);

            // Healthbar
            // [Interflow 2026-08-01 server-opt] На дедике бар не создаём вовсе (раньше спавнилась пустышка).
            if (!Utils.Headless && unitType != UnitType.StaticDestructible && unitType != UnitType.Item && unitType != UnitType.Tree)
            {
                if (team != SlotManager.instance.currentTeam && team != (int)Teams.NeutralPassive) Instantiate(ReferenceManager.instance.healthBarEnemy, this.transform).name = "HealthBar(Clone)";
                else Instantiate(ReferenceManager.instance.healthBar, this.transform);

                // [Interflow fix 2026-08-05 unit-status-sync] Шкала статусов (ряд иконок над полоской
                // здоровья) — та же конвенция, что у бара: только не на дедике и не для статики.
                gameObject.AddComponent<UnitStatusIconsBar>();
            }

            // Minimap icon
            // [Interflow 2026-08-01 server-opt] На дедике иконку не создаём (миникарты нет).
            if (!Utils.Headless)
            {
                var minimapIcon = transform.Find("MiniMapIcon");
                if (minimapIcon == null) minimapIcon = Instantiate(ReferenceManager.instance.miniMapIcon, this.transform);
                // [Interflow fix 2026-08-01 grid-headless] SpriteRenderer может быть вырезан Roles-стрипом — гейт вместо NRE.
                SpriteRenderer minimapIconSR = minimapIcon.GetComponent<SpriteRenderer>();
                if (minimapIconSR != null) minimapIconSR.color = SlotManager.instance.playerColors[owner];
            }

            // VFX Holder - for auras and stun efects
            vfxHolder = new GameObject().transform;
            vfxHolder.name = "VFXHolder";
            vfxHolder.parent = this.transform;
            vfxHolder.localScale = new Vector3(1, 1, 1);
            vfxHolder.localPosition = new Vector3(0, 0.1f, 0);

            // Attack selector and melee
            AttackSelectorInitialize();
            additionalTargets = new Unit[multiTargetCount + 1];

            if (melee || attackRange <= unitRadius)
            {
                melee = true;
                attackRange = unitRadius + Utils.stopDistanceOffset;
            }

            // =================================== VISUALS ===================================

            // Collider 
            AddColliders();

            // Assign renderers. Per unit material property. Player color and Overlay Color
            CalculateVisuals();

            // Colors
            matBlock = new MaterialPropertyBlock();
            SetPlayerColor();
            ResetOverlayColor();

            // =================================== PARAMETERS ===================================

            // Building can not move
            if (UnitType.Building == unitType) canMove = false;

            // Vision limit
            if (visionRange > Utils.maxVisionRange) visionRange = Utils.maxVisionRange;

            // Navmesh
            if (unitType != UnitType.Item)
            {
                GameObject go = gameObject;

                if (unitType != UnitType.Building)
                {
                    // If air unit we make a copy of it for air space
                    if (isAir)
                    {
                        go = new GameObject(unitName + "Air");
                        airReplica = go.transform;
                        airReplica.position = new Vector3(transform.position.x + Utils.airOffsetX, 0, transform.position.z);
                        transform.position = new Vector3(transform.position.x, Utils.airUnitElevation, transform.position.z);
                    }

                    // Navmesh agent
                    if (isAir || GetComponent<NavMeshAgent>() == null)
                    {
                        agent = go.AddComponent<NavMeshAgent>();

                        // Set agent parameters
                        if (isWater) agent.agentTypeID = GameManager.instance.agentTypes[1];
                    }
                    else agent = GetComponent<NavMeshAgent>();

                    agent.radius = unitRadius;
                    agent.height = unitHeight;
                    agent.speed = moveSpeed;
                    agent.acceleration = acceleration;
                    agent.angularSpeed = turnSpeed;
                    agent.autoBraking = false;
                    agent.updateRotation = false;

                    agent.enabled = false;
                }

                // NavMeshObstacle - Always should be present
                if (isAir || GetComponent<NavMeshObstacle>() == null)
                {
                    obstacle = go.AddComponent<NavMeshObstacle>();

                    if (GetComponent<BoxCollider>() != null)
                    {
                        // If box collider exists in the object, we adjust obstacle parameters to match it
                        BoxCollider boxCollider = GetComponent<BoxCollider>();

                        obstacle.shape = NavMeshObstacleShape.Box;
                        obstacle.center = boxCollider.center;
                        obstacle.size = boxCollider.size;
                    }
                    else
                    {
                        // Set obstacle parameters
                        obstacle.shape = NavMeshObstacleShape.Capsule;
                        obstacle.radius = unitRadius; // To avoid jumping, it is safer to add 0.2f
                        obstacle.height = unitHeight;
                    }

                    obstacle.carving = true; // Carving and its parameters are hardcoded. You can change them here.
                    obstacle.carvingMoveThreshold = 0.1f;
                    obstacle.carvingTimeToStationary = 0.01f;
                    obstacle.carveOnlyStationary = false;
                }
                else obstacle = GetComponent<NavMeshObstacle>();
            }

            // FoW Cell assignment
            if (FogOfWar.instance.TurnOff) FoWVisible = true;
            else
            {
                FoWCell = FogOfWar.instance.CellAssignment(this, true);
                if (SlotManager.instance.currentTeam == team) FoWVisible = true;

                // View Blocker
                if (viewBlocker || singleCellViewBlocker) FogOfWar.instance.UnitViewBlockCalculate(this);
            }

            // Grid assignment
            Grid.AssignToChunkInitial(this);

            // Invisibilty navmesh clone - for static objects
            if (GameManager.instance.gameIncludesInvisible)
            {
                // Clone only for static objects
                if (unitType != UnitType.Item && unitType != UnitType.Unit && !canMove)
                {
                    invisibilityReplica = new GameObject(unitName + "Invisibility").transform;
                    invisibilityReplica.position = new Vector3(transform.position.x, 0, transform.position.z + Utils.invisibilityOffsetY);
                    Utils.CopyObstacleComponent(invisibilityReplica, obstacle);
                }
            }

            // =================================== COMPONENTS ===================================

            // Resource collection handling
            if (GetComponent<ResourceUnit>())
            {
                resourceUnit = GetComponent<ResourceUnit>();
                resourceUnit.Initialize();
            }

            // Building construction handling
            if (GetComponent<ConstructionUnit>())
            {
                constructionUnit = GetComponent<ConstructionUnit>();
                constructionUnit.Initialize();
            }

            // Attribute unit
            if (GetComponent<AttributeUnit>())
            {
                attributeUnit = GetComponent<AttributeUnit>();
                attributeUnit.Initialize();
            }

            // Leveling unit
            if (GetComponent<LevelingUnit>())
            {
                levelingUnit = GetComponent<LevelingUnit>();
                levelingUnit.Initialize();
            }
            // Lifetime Unit
            if (GetComponent<LifetimeUnit>())
            {
                lifetimeUnit = GetComponent<LifetimeUnit>();
                lifetimeUnit.Initialize();
            }

            // Transport unit
            if (transportWeight == 0) transportWeight = 1;
            if (GetComponent<TransportUnit>())
            {
                transportUnit = GetComponent<TransportUnit>();
                transportUnit.Initialize();

                // Add takein takout abilities
                Array.Resize(ref abilities, abilities.Length + 2);
                abilities[abilities.Length - 1] = ReferenceManager.instance.takeInTransport;
                abilities[abilities.Length - 2] = ReferenceManager.instance.takeOutTransport;
            }

            // Armor
            if (armorType == null) armorType = ReferenceManager.instance.standardArmorType;
            // Attack 
            if (damageType == null) damageType = ReferenceManager.instance.standardDamageType;

            // Initialize Abilities and Inventory
            InitializeAbilities();
            InitializeInventory();

            // Processes initialisation
            if (canProcess)
            {
                activeProcess = new Ability[GameManager.maxProcessCount];
                processLevel = new int[GameManager.maxProcessCount];
                currentProcessTimer = -1; // -1 means no current processes
            }

            // Презентация: умения уже инициализированы, значит их можно читать — например, чтобы
            // завести постоянный круг радиуса ауры. Раньше этой точки уровни и блокировки ещё не проставлены.
            SkillPresentationEvents.RaiseUnitReady(this);

            // Define static object
            if (!canMove && !canAttack && abilities.Length == 0 && InventorySize == 0 && owner == (int)Players.NeutralPassive) staticObject = true;

            // Is it being constructed or not && Subscribe to tick of gameManager
            GameManager.instance.Tick += HandleEffectors;
            if (constructionUnit == null || (!constructionUnit.isBuilding || constructionUnit.completed)) // Add the following only if this building is not being built
            {
                if (!staticObject)
                {
                    GameManager.instance.Tick += HandleEveryFrameAbilities;
                    GameManager.instance.Tick += CooldownCalculate;
                }
            }
            else isBeingBuilt = true;

            if (!isBeingBuilt)
            {
                // Unlock TechTree when this unit is created, if there is something to unlock
                TechnologyManager.instance.UnlockTech(this);

                // Resource Production when unit is created. For limited resource it increases the limits
                if (resourceProduced != null)
                {
                    for (int i = 0; i < resourceProduced.Length; i++)
                    {
                        // [Interflow fix 2026-08-01 limited-res-sync] calledByServer: true — учёт ведёт СЕРВЕР и рассылает
                        // клиентам (раньше каждый пир считал сам и расходился на выделенном сервере).
                        if (resourceProduced[i].type.limited) GameResources.instance.ChangeLimit(owner, resourceProduced[i], false, true);
                        else GameResources.instance.ChangeAmount(owner, resourceProduced[i], 1, false, true);
                    }
                }
            }

            // Resource cost: only For limited resource, it increases the usage of it
            if (resourceCost != null)
            {
                for (int i = 0; i < resourceCost.Length; i++)
                {
                    // [Interflow fix 2026-08-01 limited-res-sync] calledByServer: true — расход лидерства считает и рассылает сервер.
                    if (resourceCost[i].type.limited) GameResources.instance.ChangeAmount(owner, resourceCost[i], 1, true, true); // We decrease the resource
                }
            }

            // TechTree subscribe to it
            if (!staticObject) TechnologyManager.instance.OnTechUnlock[owner] += AllAbilityLockLevelsCalculate; // When new tech is unlocked, check if there are any abilities to unlock

            initialized = true;
        }

        /// <summary>
        /// When visual representation of unit changes this should be called. Assigns renderers, mesh renderers, animation data and other paremeters.
        /// </summary>
        private void CalculateVisuals()
        {
            // Main renderer
            if (mainRenderer == null) mainRenderer = transform.GetChild(0).gameObject;

            // Renderers
            renderers = new List<Transform>();
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (i == 0 || child == selectionCircle || child.gameObject == mainRenderer) continue;
                renderers.Add(child);
            }

            // Mesh Renderers
            meshRenderers = new List<Renderer>();
            if (mainRenderer.GetComponent<Renderer>()) meshRenderers.Add(mainRenderer.GetComponent<Renderer>());
            foreach (var renderer in mainRenderer.GetComponentsInChildren<Renderer>())
            {
                meshRenderers.Add(renderer);
            }

            // Projectile/ContinuousVFX
            if (canAttack && !melee)
            {
                // Assign default projectile/continuousVFX
                if (projectileGO == null)
                {
                    Debug.LogWarning("Unit " + unitName + " is ranged, but projectile is not set! Set it!");
                    if (attackType == AttackType.Continuous) projectileGO = ReferenceManager.instance.defaultContinuousVFX.gameObject;
                    else projectileGO = ReferenceManager.instance.defaultProjectile.gameObject;
                }
                // Assign internal variables for projectile/continuousVFX
                if (attackType == AttackType.Continuous)
                {
                    // If attack type is continuous we instantiate attackVFX at launchSite(s)
                    if (projectileGO.GetComponent<VFXLine>()) attackVFXLine = VFXLine.CreateVFX(projectileGO.GetComponent<VFXLine>(), this);
                    else Debug.LogWarning("Unit " + unitName + " has continuous attack, but assigned projectile does not have VFXLine component!");
                }
                else projectileVFX = projectileGO.GetComponent<Projectile>();
            }

            // Horizontal part was not set
            if (!horizontalPartSet && horizontalPart != null)
            {
                if (horizontalPart != transform) horizontalPartForward = horizontalPart.rotation;
                else horizontalPartForward = Quaternion.Euler(0, 0, 0);
                horizontalPartSet = true;
            }

            // UniformScaling only. If object is scaled, NavMesh radius will be scaled too. We should adjust unitRadius for proper calculations.
            unitRadius = unitRadius * transform.lossyScale.x;
            unitHeight = unitHeight * transform.lossyScale.x;

            // Animator 
            if (mainRenderer != null)
            {
                animator = mainRenderer.GetComponent<Animator>();
            }

            if (animator)
            {
                // Idle animation
                idleAnimationCount = 0;
                while (animator.HasState(0, Animator.StringToHash("idle" + idleAnimationCount))) idleAnimationCount++;

                // If more than one idle animation we subscribe to gameManager tick and play idle randomly
                // [Interflow 2026-08-01 server-opt] Случайные idle-кроссфейды — визуал, на дедике не подписываемся.
                if (!initialized && idleAnimationCount > 1 && !Utils.Headless)
                {
                    idleRandomTime = UnityEngine.Random.Range(8f, 13f);
                    GameManager.instance.Tick += RandomIdleAnimation;
                }

                // Attack animation
                attackAnimationsCount = 0;
                if (canAttack)
                {
                    // Attack animation count. Only first layer is checked. For standard or non-sequential periodic type of attacks.
                    while (animator.HasState(0, Animator.StringToHash("attack" + attackAnimationsCount))) attackAnimationsCount++;
                }

                // Death animation
                deathAnimationCount = 0;
                while (animator.HasState(0, Animator.StringToHash("death" + deathAnimationCount))) deathAnimationCount++;
                if (deathAnimationCount == 0) Debug.LogWarning(unitName + " does not have any death animation. Death animations must end with _death0, _death1 etc");

                // Define the length of animations
                bool attackFound = (attackAnimationsCount != 0) ? false : true;
                bool deathFound = (deathAnimationCount != 0) ? false : true;
                if (!attackFound || !deathFound)
                {
                    // Calculate attack animation length
                    AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
                    for (int i = 0; i < clips.Length; i++)
                    {
                        if (!attackFound)
                        {
                            if (clips[i].name.EndsWith("attack0"))
                            {
                                attackAnimationLength = clips[i].length;
                                attackFound = true;
                            }
                        }
                        else if (!deathFound)
                        {
                            if (clips[i].name.EndsWith("death0"))
                            {
                                deathAnimationLength = clips[i].length;
                                deathFound = true;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                // Hit animation
                hasHitAnim = animator.HasState(0, Animator.StringToHash("hit"));

                // Cast animation
                hasCastAnim = animator.HasState(0, Animator.StringToHash("cast"));

                // [Interflow 2026-08-01 server-opt] На дедике скелетную анимацию выключаем: тайминги атак/смерти
                // уже вычислены из ДЛИН клипов выше, состояние аниматора сим-логика не читает (grep:
                // GetCurrentAnimatorStateInfo/IsInTransition вне Client = 0). Крупнейшая экономия CPU сервера.
                if (Utils.Headless) animator.enabled = false;
            }

            // Animation
            ChangeMoveSpeedAnimationSpeed();
            ChangeAttackAnimationSpeed();
        }

        /// <summary>
        /// Initializes all the necessary parameters for the attack logic of this unit.
        /// </summary>
        public void AttackSelectorInitialize()
        {
            canAttack = attackUnitSelector.AnySelectors();
            if (canAttack)
            {
                attackUnitSelector.includeInvisible = false; // Direct invisible attack are not allowed
                searchUnitSelector = new UnitSelector(false, false, true, attackUnitSelector.isUnit, attackUnitSelector.isBuilding, false, false, attackUnitSelector.isGround, attackUnitSelector.isWater, attackUnitSelector.isAir, false, attackUnitSelector.includeInvulnerable);
                splashUnitSelector = new UnitSelector(false, false, true, attackUnitSelector.isUnit, attackUnitSelector.isBuilding, attackUnitSelector.isStaticDestructible, attackUnitSelector.isTree, attackUnitSelector.isGround, attackUnitSelector.isWater, attackUnitSelector.isAir, true, attackUnitSelector.includeInvulnerable);
            }
        }

    }
}
