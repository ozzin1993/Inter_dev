// [Interflow fix 2026-06-27] Все подсказки [Tooltip] в этом файле локализованы на русский (правка ассета, разрешена Artsiom; только текст Tooltip). Оригинал EN: _BACKUP_TOOLTIPS/Scripts/Unit.cs. Реестр: wiki concepts/asset-fork-debt.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    public partial class Unit : MonoBehaviour
    {
#if UNITY_EDITOR
        [ReadOnly]
#endif
        [Tooltip("Указывает тип юнита. Задаётся в префабе. Не назначать и не менять.")]
        public int unitTypeID;
        [Tooltip("Уникальный ID этого экземпляра. В префабах должен быть 0, юнитам на сцене netID назначается автоматически.\nНе задавать вручную, если не уверены.")]
        public UInt16 netID;

        // Ownership
        [Tooltip("Индекс игрока-владельца юнита. 12 — Neutral Passive, 13 — Neutral Active. Не менять владельца напрямую из скрипта.\nТолько в инспекторе или через метод SetOwnership().")]
        public int owner;
        [HideInInspector] public int team { get; private set; } = -1;// Which team owns this unit

        [Header("Text")]
        [Tooltip("Отображаемое имя юнита")]
        public string unitName;
        [Tooltip("Портрет (изображение) юнита")]
        public Texture2D icon;
        [TextArea(5, 10)]
        [Tooltip("Описание, которое показывается при наведении мыши на имя юнита")]
        public string description;

        [Header("General Parameters")]
        [Tooltip("Можно ли выделить этот юнит. Если выключено — его смогут взять в цель только юниты, способные его атаковать")]
        public bool isSelectable = true;
        [Tooltip("Тип юнита")]
        public UnitType unitType;

        [Tooltip("Может ли юнит размещаться и/или ходить по земле")]
        public bool isGround = true;
        [Tooltip("Может ли юнит размещаться и/или передвигаться по воде")]
        public bool isWater = false;
        [Tooltip("Может ли юнит размещаться/перемещаться в любой точке игровой зоны; его обзор не перекрывается ViewBlockers тумана войны и перепадами высот")]
        public bool isAir = false;

        [Header("Visuals")]
        [Tooltip("Задаёт размер юнита; определяет размер для: NavMeshAgent, CapsuleCollider, круга выделения")]
        public float unitRadius = 0.5f;
        [Tooltip("Задаёт высоту юнита; определяет размеры для: высоты CapsuleCollider, способностей/снарядов, нацеленных в центр юнита")]
        public float unitHeight = 1f;
        [Tooltip("Время перехода анимации для каста и случайных idle-анимаций")]
        public float crossFadeTime = 0.1f;
        [Tooltip("Объект, который получает горизонтальный поворот. Обычно это дочерний меш юнита, может быть и сам юнит")]
        public Transform horizontalPart;
        [Tooltip("Объект, который получает вертикальный поворот; может быть и сам юнит")]
        public Transform verticalPart;
        private Quaternion horizontalPartForward; // Used by horizontal part to properly calculate forward rotation
        private bool horizontalPartSet = false; // to know if quaternion horizontalPartFoward was set

        [Header("Behaviour")]
        [Tooltip("По умолчанию атакующие юниты ищут цели в простое. Включение запрещает автоатаку — только по команде")]
        public bool doNotLookForTargets = false;
        [Tooltip("Задаёт дальность обзора юнита.\nОт неё зависит часть поведения, например прекращение преследования врага. При атаке по команде преследование не прекращается, пока цель жива и видима")]
        public int visionRange = 5;
        [Tooltip("Простаивающие юниты атакуют, только когда враг входит в этот радиус, а не в дальность обзора")]
        public int reactionRange = 5;
        [Tooltip("Блокирует ли объект обзор в тумане войны. У ViewBlockers должен быть BoxCollider. Движущихся ViewBlockers быть не может.")]
        public bool viewBlocker = false;
        [Tooltip("Если включено, объект блокирует только одну ячейку тумана войны. BoxCollider не нужен. Движущихся ViewBlockers быть не может.")]
        public bool singleCellViewBlocker = false;
        [Tooltip("Сколько слотов занимает этот юнит в TransportUnit")]
        public int transportWeight = 1;
        [Tooltip("Юниты с более высоким приоритетом ставятся в передний центр строя при управлении группой.")]
        public int formationPriority = 0;

        [Header("Health")]
        [Tooltip("Максимальный запас здоровья юнита")]
        public float maxHealth;
        [Tooltip("Текущее здоровье юнита")]
        public float health; // Current health points
        [Tooltip("Восстановление здоровья в секунду")]
        public float healthRegen = 0; // HP/sec regeneration

        [Header("Mana")]
        [Tooltip("Максимальный запас маны юнита")]
        public float maxMana;
        [Tooltip("Текущая мана юнита")]
        public float mana;
        [Tooltip("Восстановление маны в секунду")]
        public float manaRegen = 0;

        [Header("Armor")]
        [Tooltip("Если включено, юнит нельзя ранить, если это явно не разрешено входящим уроном")]
        public bool isInvulnerable = false;
        [Tooltip("Текущая броня юнита; чем выше значение, тем меньше входящий урон")]
        public float armor;
        [Tooltip("Влияет на снижение урона в зависимости от типа урона атаки")]
        public ArmorType armorType;

        [Header("Movement")]
        [Tooltip("Подвижный юнит или стационарный")]
        public bool canMove = false;
        [Tooltip("Скорость передвижения юнита")]
        public float moveSpeed = 5f;
        [Tooltip("Как быстро юнит набирает свою скорость передвижения")]
        public float acceleration = 100f;
        [Tooltip("Скорость поворота в градусах в секунду; используется для движения и наведения")]
        public float turnSpeed = 360;
        [Tooltip("Скорость передвижения, при которой проигрывается анимация юнита. Можно задать 0. Если задано, скорость анимации движения масштабируется со скоростью передвижения юнита")]
        public float animationMoveSpeed = 0;

        [Header("Creation")]
        [Tooltip("Открывать ли TechTree при создании этого юнита")]
        public Technology unlockTech;
        [Tooltip("Производит ли юнит ресурсы при создании. Standard-ресурсы производятся один раз, Limited-ресурсы доступны, пока юнит жив")]
        public ResourceWrapper[] resourceProduced;
        [Tooltip("Стоимость юнита для производства. Способности Construction и UnitTraining берут стоимость отсюда. Также Limited-ресурсы расходуются, пока юнит жив")]
        public ResourceWrapper[] resourceCost;
        [Header("Death")]
        [Tooltip("Сколько XP получат другие юниты при убийстве этого юнита. Способ распределения XP задаётся в GameManager")]
        public int xpReward;
        [Tooltip("Ресурсы, выдаваемые убийце. Награду получает только игрок-убийца")]
        public ResourceWrapper[] resourceReward;
        [Tooltip("Этот Transform создаётся при смерти юнита. VFX должен сам уничтожиться после проигрывания")]
        public Transform dieVFX;

        [Header("Attack Parameters")]
        [Tooltip("Задаёт, какие юниты может атаковать этот юнит. Можно снять все галочки — тогда юнит не сможет атаковать")]
        public UnitSelector attackUnitSelector;
        [HideInInspector] public bool canAttack; // Defined by attackUnitSelector
        [HideInInspector] public UnitSelector searchUnitSelector; // Used internally to search for enemies to attack. Defined partially by attackUnitSelector
        [HideInInspector] public UnitSelector splashUnitSelector; // Used internally to do splash damage. Defined partially by attackUnitSelector
        [Tooltip("Юнит ближнего или дальнего боя")]
        public bool melee = true;
        [Tooltip("Задаёт схему атаки юнита")]
        public AttackType attackType;
        [Tooltip("Задаёт дальность атаки юнита. Игнорируется для ближнего боя. Если меньше радиуса юнита — дальность автоматически становится ближней")]
        public float attackRange = 1f; // Not used if unit is melee
        [Tooltip("Задаёт тип урона юнита")]
        public DamageType damageType;
        [Tooltip("Урон за одну атаку")]
        public float attackDamage = 10;
        [Tooltip("Скорость атаки юнита. Избегайте слишком низких значений, например 0.1f")]
        public float attackSpeed = 1f;
        [Tooltip("Effectors, применяемые к атакованной цели")]
        public Effector[] attackEffectors;

        [Header("Attack Technical")]
        [Tooltip("Снаряд для дальнобойных юнитов. Может быть Projectile или VFXLine")]
        public GameObject projectileGO; // Can be object projectile or Particle/Line Renderer for continuous attack types

        [HideInInspector] public Projectile projectileVFX; // Reference to projectile component of projectileGO
        [HideInInspector] public VFXLine attackVFXLine; // If continuous attack type, we store reference to instantiated VFX
        [HideInInspector] public Transform attackSoundRef; // If continuous attack type, we store loop sound for further deletion

        [HideInInspector] public bool netCD; // Meaning we do an attack immediately and then set attack cooldown to provided value
        [HideInInspector] public float currentAttackSpeed = 0; // We check against this value to deal the damage

        private bool isAttackAnimationPlaying = false; // To properly sync attack dealing damage and attack animation
        [HideInInspector] public float attackCooldown = 0;
        [HideInInspector] public int currentAttackCount = 0; // Defines which periodic attack number currently active. For continius attack it indicates that attack is being currently performed
        [HideInInspector] public bool firstAttack = true; // If this first attack of unit (for network)
        private bool attackTimerUpdate = false; // Used to know if we update the attack timer outside the AttackUpdate()

        [Tooltip("Время между началом анимации атаки и моментом, когда юнит наносит урон. 0 = 0%, 1 = 100%.")]
        public float animationAttackDelay = 0; // If unit has animation and it takes some time to reach actual attack, that delay should be defined here.
        [HideInInspector] public float currentAnimAttackDelay; // Attack animation damage deal delay based on attack speed and animationAttackDelay. In seconds
        private int attackAnimationsCount; // For storing how many attack animations this unit has
        private float attackAnimationLength; // For changing attack animation speed multiplier based on attack speed
        private float currentAttackAnimLength; // Depending on attack speed this attack animation length will be changed

        [Tooltip("Может быть Null. Откуда запускаются снаряды.\nДля типа атаки Periodic число точек запуска должно совпадать с числом периодических атак")]
        public Transform[] launchSite;
        [Tooltip("Может быть Null. При запуске снаряда этот визуальный эффект проигрывается в точке запуска (если задана).\nДля типа атаки Periodic число VFX должно совпадать с числом периодических атак")]
        public ParticleSystem[] launchVFX;

        [Header("Periodic Attack Parameters")] // Periodic attacks are attacks that wait every X seconds (attack speed) and attack several times with a specified delay.
        [Tooltip("Если включено, анимации атаки проигрываются по порядку от attack0 до attackN, иначе — случайно. При включении число анимаций атаки должно совпадать с Periodic Attack Count")]
        public bool periodicSequential = true;
        [Tooltip("Задержка между периодическими атаками")]
        public float periodicAttackDelay;
        [Tooltip("Общее число атак за цикл")]
        public int periodicAttackCount;

        [Header("Attack Modifications")]

        [Header("Splash")]
        [Tooltip("Включает урон Splash — атака задевает врагов вокруг цели")]
        public bool isSplash = false;
        [Tooltip("Радиус вокруг цели, который тоже получает урон")]
        public float splashRadius;
        [Tooltip("Получают ли более далёкие юниты меньше урона. 1 — без снижения. 0.9 — самый дальний юнит получает 90% урона")]
        public float splashReduction = 1;
        [Tooltip("Только для дальнобойных юнитов со снарядами. Если включено, снаряд следует за целью; иначе летит в позицию цели на момент атаки. Без Splash неотслеживающий снаряд не сможет нанести урон цели")]
        public bool projectileFollowTarget = true;

        [Header("MultiTarget (Ranged only)")]
        [Tooltip("Позволяет юниту атаковать несколько целей одновременно в пределах дальности атаки")]
        public bool multiTarget = false;
        [Tooltip("Число дополнительных целей, которые может атаковать юнит")]
        public int multiTargetCount = 0;
        [HideInInspector] public Unit[] additionalTargets; // Targets currently acquired for multitarget

        [Header("Bounce (Ranged only)")]
        [Tooltip("Сколько раз Projectile/VFXLine может отскакивать (Bounce) к ближайшим юнитам")]
        public int bounceCount = 0;
        [Tooltip("Максимальная дистанция одного отскока (Bounce)")]
        public float bounceRange;
        [Tooltip("Уменьшается ли урон с каждым отскоком. 1 — без снижения, 0.9 — каждая следующая цель получает 90% предыдущего урона")]
        public float bounceReduction = 1;

        // Processes - Procecss can be unit training or an upgrade that takes time to finish, it will appear in the center of screen showing what is currently being processed.
        [Header("Processing")] // Max amount of processes is GameManager.maxProcessCount. Can be any number, but requires changes to the Processes UI and parameters in UIHandler.
        [HideInInspector] public bool canProcess = false; // If unit does not have any processing skills, this parameter will stay false
        [HideInInspector] public Ability[] activeProcess = null;
        [HideInInspector] public int[] processLevel; // For each process that exists in this unit we should also store the level of it when the process was added. To calculate the costs
        [HideInInspector] public float currentProcessTimer;

        [Header("Sound")]
        [Tooltip("Случайный звук при обучении юнита")]
        public AudioClip[] readySound;
        [Tooltip("Случайный звук при команде на движение")]
        public AudioClip[] moveSound;
        [Tooltip("Случайный звук при выделении юнита")]
        public AudioClip[] clickSound;
        [Tooltip("Случайный звук при смерти юнита")]
        public AudioClip[] deathSound;
        [Tooltip("Случайный звук при команде на атаку")]
        public AudioClip[] attackCommandSound;
        [Tooltip("Случайный звук в начале атаки")]
        public AudioClip[] attackStartSound;
        [Tooltip("Случайный звук при прекращении атаки (не в конце каждой атаки). Полезно для типа атаки Continuous")]
        public AudioClip[] attackEndSound;
        [Tooltip("Случайный звук в момент атаки; для дальнобойных — звук запуска снаряда; для Continuous — зацикленный звук атаки")]
        public WeaponSound weaponSound;

        // Sound technical
        [HideInInspector] public Transform commandSound; // Curently being played command sound
        [HideInInspector] public float commandSoundTime; // Time left for current command sound

        [Header("Technical")]
        public PassiveAppliedEffects passiveEffects = new PassiveAppliedEffects(); // Passive abilities will add their percentage change influences to this wrapper. Theiy will be calculated at the end of the frame if were triggered
        // Static objects will not Update(). If once static object it should always stay a static object, do not change any corresponding parameters
        [HideInInspector] public bool staticObject = false; // Objects belong to Neutral Passive that do not attack, move, use abilities, have inventory
        // Unit states
        [HideInInspector] public Unit target; // If target is set, unit will reach it and attack
        [HideInInspector] public Vector2 targetPosition; // For attackMove state. Also for target ground position
        [HideInInspector] public bool isTargetGround; // The last visible position of the target
        [HideInInspector] public Vector2 initialPosition; // For idle state of the unit
        [HideInInspector] public bool isBeingBuilt; // For buildings (ConstructionUnit.cs). Indicates that this building either is being constructed or upgraded
        [HideInInspector] public float currentActionTime; // Used ability duration and cast time, idle random animation
        [HideInInspector] public bool disabled; // If this unit is disabled
        // Stunned
        [HideInInspector] public bool stunned; // When unit gets stunned it can`t do anything till stun wears off
        [HideInInspector] public float stunTime; // Used by stun
        Transform stunnedVFX; // Stunned VFX reference
        // Muted
        [HideInInspector] public bool muted; // When unit is muted, it can not cast any abilities
        [HideInInspector] public float currentMuteTime;
        Transform mutedVFX; // Muted VFX reference
        // Disarmed - can attack used as bool to know if disarmed
        [HideInInspector] public bool disarmed; // If this unit is currently disarmed
        [HideInInspector] public float currentDisarmTime;
        Transform disarmedVFX; // Disarmed VFX reference
        // Polymorph
        [HideInInspector] public bool polymorphed; // If this unit is currently polymorphed.
        [HideInInspector] public float polymorphTime; // Current remaining time of the polymorph.
        [HideInInspector] public float polymorphTotalTime; // Current total time of the polymorph.
        [HideInInspector] public Ability polymorphAbility; // Which ability caused this unit to polymorph.
        [HideInInspector] public int polymorphLvl; // Level of the polymorph ability
        [HideInInspector] public Unit polymorphShape; // Which unit`s shape is currently being used
        Transform[] pmLaunchSite; // We want to store reference to original launch positions
        Transform pmVerticalPart; // We want to store reference to original vertical part
        Transform pmHorizontallPart; // We want to store reference to original horizontal part
        Quaternion pmHorizontalForward; // We want to store reference to original horizontal forward rotation
        [HideInInspector] public Unit currentMainShape; // Which unit`s shape is currently main renderer - for save manager

        // Shop
        [HideInInspector] public bool isShop = false; // Can this unit sell items. If this unit has any items in abilities, it becomes a shop. Set automatically in InitializeAbilities()
        [HideInInspector] public Unit shopUnit; // Unit that is currently shopping

        // Technical
        public bool dead { get; private set; } = false;
        [HideInInspector] public UnitStates unitState = UnitStates.Idle;
        [HideInInspector] public Coordinate currentCell = new Coordinate(-1, -1); // Current cell location of this unit
        [HideInInspector] public bool isMoving = false;
        private bool embarkFollow = false; // if this is true, upon reach the following unit we embark it (if possible)
        private bool renderersOn = true; // Are renderers currently turned on

        // Waypoint
        [HideInInspector] public bool isWaypoint; // Can place waypoints
        [HideInInspector] public Vector2 waypointLocation;
        [HideInInspector] public Unit waypointUnit;

        // Animation
        [HideInInspector] public float animationBlendingIndex; // This is current animation blending index set for this unit
        private int idleAnimationCount; // Amount of idle animations in the unit. Unit idle animations must have events that call Unit.RandomizeIdleAnimation()
        private float idleRandomTime; // Time specific for this unit to play different idle animation
        private int deathAnimationCount; // Amount of death animations
        private float deathAnimationLength;
        [HideInInspector] public bool hasHitAnim; // Does this unit have hit animation 
        [HideInInspector] public bool hasCastAnim; // Does this unit have ability cast animation 

        // Effectors
        public List<EffectorHolder> effectors = new List<EffectorHolder>(); // List of currently held effectors

        // FoW
        [HideInInspector] public Coordinate FoWCell;
        [HideInInspector] public Transform staticCopy;
        [HideInInspector] public bool FoWVisible; // If this unit currently FoW visible to current team, only enemy units (not the same team) are taken into account

        // Component References
        [HideInInspector] public NavMeshAgent agent;
        [HideInInspector] public NavMeshObstacle obstacle;
        [HideInInspector] public Animator animator;
        private AnimationState currentAnimatorBoolState;

        [HideInInspector] public ResourceUnit resourceUnit; // If this resourceCollection script is attached to this unit, it means this unit handles resource in one way or another
        [HideInInspector] public ConstructionUnit constructionUnit; // If this BuildingConstruction script is attached to this unit, it means this unit is a constructible building or builder
        [HideInInspector] public AttributeUnit attributeUnit; // If this unit has attributes
        [HideInInspector] public LevelingUnit levelingUnit; // If this unit has levels
        [HideInInspector] public TransportUnit transportUnit; // If this unit can transport units
        [HideInInspector] public LifetimeUnit lifetimeUnit; // If this unit has limited lifespan

        Transform vfxHolder; // For holding aura, stun and other effects that should be shown or hidden with unit
        Transform selectionCircle; // Selection Circle when unit is selected

        // Make unit static or dynamic - Making a unit an agent after turning off obstacle component must wait at least 2 frames to avoid jumping/sudden movement
        int waitTwoUpdates = 0;
        [HideInInspector] public Vector3 currentDestination;
        [HideInInspector] public float stopDistance = 0; // Navmesh stopping distance
        // Movement technical
        private Transform airReplica; // Replicated gameobject that this unit copies the position from, if air

        // Actions
        public List<DamageModifyCallback> OnDamageDealModifyCallbacks = new();
        public List<BeforeDamageDealCallback> OnBeforeDamageDealCallbacks = new();
        public List<AfterDamageDealCallback> OnAfterDamageDealCallbacks = new();

        public List<DamageModifyCallback> OnBeforeGetDamageCallbacks = new();

        public Action<float> OnDamageDeal; // When this unit deals the damage. Parameter: Damage dealt.
        public Action<Unit, int, Unit, bool> OnDie; // When this unit dies - Unit that dies, Player that kills, Unit that kills, are Rewards granted
        public Action OnFollowReach; // When this unit reaches followed unit
        public Action OnPositionReach; // When this unit reaches move position
        public Action<bool> OnCommand; // Unit was commanded to do anything (Follow, attack, hold, stop, move). Bool is true if command was issued by the player directly.
        public Action OnStatusUpdate; // Called when effectors is added or removed
        public Action OnProcessUpdate; // Called when process is finished

        public Action OnHPChange; // UI will subscribe to this to track the change of the parameter
        public Action OnMPChange; // UI will subscribe to this to track the change of the parameter
        public Action OnCharacteristicsChange; // General characteristics change - Damage/Armor/Speed/Characteristics

        public Action OnRedrawAbilityView; // Called when we should redraw the ability view

        public Action WaypointUpdate; // If waypoint was updated (For unit trainers)

        public Action<Unit> OnReferenceChange; // When unit is disabled/upgraded we must replace the target reference on other units

        // Material
        [HideInInspector] public GameObject mainRenderer; // Root object of this unit that has all the meshes
        private List<Transform> renderers; // Renderers such as: healthbar, minimapicon, selection circles
        private List<Renderer> meshRenderers; // Only mesh renderers of this unit
        private MaterialPropertyBlock matBlock;

        bool overlayColorOneFrame = false;

        // Sync/Spawn/Initialize
        [HideInInspector] public bool hpSync = false; // if this unit`s hp currently to be sent to clients
        [HideInInspector] public bool mpSync = false; // mp
        [HideInInspector] public bool xpSync = false; // xp
        [HideInInspector] public bool positionsSent = true; // Edge-case. within tick if we add and remove to position sync
        [HideInInspector] public bool removeFromPosSync = false;

        [HideInInspector] public bool spawned;
        [HideInInspector] public bool initialized;

        // Start is called before the first frame update
        void Start()
        {
            // For save and midgame connection type, for in-scene units we assign the netIDs. We need netIDs to remove the units in Scene Reset.
            if (NetworkConnectionHandler.instance.connectionStage == 1 || NetworkConnectionHandler.instance.connectionStage == 2)
            {
                SlotManager.instance.AssignNetID(this);
                return;
            }

            // When standard connection type, for in-scene units initialization happens after all players finished loading the scene
            if (!SlotManager.instance.gameOn)
            {
                SlotManager.instance.OnGameStart += StartCallback;
                return;
            }

            if (netID == 0)
                Debug.LogError("InScene placed object has no netID attached. Should not happen! Resave the problematic scene to assign the netID. Object name: " + name);

            if (!initialized) Initialize();
            // Waypoint
            GoToWaypoint();
        }

        // Fired when game starts after loading necessary data
        public void StartCallback()
        {
            SlotManager.instance.OnGameStart -= StartCallback;
            if (!initialized) Initialize();
            // Waypoint
            GoToWaypoint();
        }

        // Update is called once per frame
        void Update()
        {
            if (!SlotManager.instance.gameOn) return;

            if (!staticObject)
            {
                if (!stunned && !isBeingBuilt)
                {
                    // Update unit behaviour
                    StateUpdate();

                    // Update unit behaviour if client
                    NetworkStateUpdate();

                    // Process handling
                    HandleProcesses();

                    // Making unit dynamic and static. For navmesh.
                    MakeAgent_WaitTwoFrames();
                }

                // Attack timer update
                if (attackTimerUpdate)
                {
                    attackCooldown -= Time.deltaTime;
                    if (attackCooldown < 0) attackTimerUpdate = false;
                }
            }

            // Overlay color - reset back one frame after
            HandleOverlayColor();
        }

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
            if (unitType != UnitType.StaticDestructible && unitType != UnitType.Item && unitType != UnitType.Tree)
            {
                if (team != SlotManager.instance.currentTeam && team != (int)Teams.NeutralPassive) Instantiate(ReferenceManager.instance.healthBarEnemy, this.transform).name = "HealthBar(Clone)";
                else Instantiate(ReferenceManager.instance.healthBar, this.transform);
            }

            // Minimap icon
            var minimapIcon = transform.Find("MiniMapIcon");
            if (minimapIcon == null) minimapIcon = Instantiate(ReferenceManager.instance.miniMapIcon, this.transform);
            minimapIcon.GetComponent<SpriteRenderer>().color = SlotManager.instance.playerColors[owner];

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
                if (netID == 52529) Debug.Log(mainRenderer.gameObject.name);
                if (netID == 52529) Debug.Log(renderer.gameObject.name);
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
                if (!initialized && idleAnimationCount > 1)
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
                        if (LookAt(activeAbilityUnit.transform.position) && !activeAbility.dontTurn && !playCast)
                        {
                            Vector3 horizontalPartDir = Quaternion.Euler(horizontalPart.rotation.eulerAngles - horizontalPartForward.eulerAngles) * Vector3.forward;
                            float dot = Vector3.Dot(horizontalPartDir.normalized, (new Vector3(activeAbilityUnit.transform.position.x, 0, activeAbilityUnit.transform.position.z) - new Vector3(horizontalPart.position.x, 0, horizontalPart.position.z)).normalized);
                            if (dot < 0.97f) return;
                        }
                    }
                    else if (activeAbilityLocation != Vector3.zero)
                    {
                        if (LookAt(activeAbilityLocation) && !activeAbility.dontTurn && !playCast)
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
                        if (hasCastAnim && FoWVisible) animator.CrossFade("cast", crossFadeTime, 0, 0f);
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
                        LookAt(activeAbilityUnit.transform.position);
                    }
                    else if (activeAbilityLocation != Vector3.zero)
                    {
                        activeAbility.Use(this, this.owner, activeAbilityLevel, activeAbilityLocation, ref activeAbilityVFX);
                        LookAt(activeAbilityLocation);
                    }
                    else activeAbility.Use(this, this.owner, activeAbilityLevel, ref activeAbilityVFX);
                }
                // Rotation update
                else if (activeAbilityCastTime != 0) // We use cast time as indicator that cast is currently being performed
                {
                    if (activeAbility.dontTurn)
                    {
                        playCast = true;
                        if (hasCastAnim) animator.CrossFade("cast", crossFadeTime, 0, 0f);
                    }

                    // Casting an ability, rotate towards the point
                    if (activeAbilityUnit)
                    {
                        if (LookAt(activeAbilityUnit.transform.position) && !playCast)
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
                        if (LookAt(activeAbilityLocation) && !playCast)
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

            if (currentAttackCount == 0)
            {
                if (attackType != AttackType.Continuous)
                {
                    // To properly sync attack animation with the actual moment we deal damage, we play animation earlier depending on animationAttackDelay parameter
                    if (attackCooldown <= 0)
                    //if (!isAttackAnimationPlaying && (attackCooldown >= attackSpeed - currentAnimAttackDelay || (attackSpeed < currentAnimAttackDelay && attackCooldown > 0))) // If AS < AD then we start playing the animation as soon as possible
                    {
                        // Play attack start sound
                        if (attackStartSound.Length > 0) SoundFXManager.instance.PlaySoundClip(attackStartSound, this.transform, 1, SoundFXManager.instance.fxGroup);

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
                        if (attackStartSound.Length > 0) SoundFXManager.instance.PlaySoundClip(attackStartSound, this.transform, 1, SoundFXManager.instance.fxGroup);

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
                            if (target != null) { if (weaponSound.weaponSound[target.armorType.index].audioClips != null) attackSoundRef = SoundFXManager.instance.PlayLoopSoundClip(weaponSound.weaponSound[target.armorType.index].audioClips, this.transform, 1, SoundFXManager.instance.fxGroup); } // Unit target
                            else if (weaponSound.groundHitClips != null && weaponSound.groundHitClips.Length > 0) SoundFXManager.instance.PlayLoopSoundClip(weaponSound.groundHitClips, this.transform, 1, SoundFXManager.instance.fxGroup); // Ground target
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
                        if (attackStartSound.Length > 0) SoundFXManager.instance.PlaySoundClip(attackStartSound, this.transform, 1, SoundFXManager.instance.fxGroup);
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
                if (target != null) { if (weaponSound.weaponSound[target.armorType.index].audioClips != null) SoundFXManager.instance.PlaySoundClip(weaponSound.weaponSound[target.armorType.index].audioClips, this.transform, 1, SoundFXManager.instance.fxGroup); } // Unit target
                else if (weaponSound.groundHitClips != null && weaponSound.groundHitClips.Length > 0) SoundFXManager.instance.PlaySoundClip(weaponSound.groundHitClips, this.transform, 1, SoundFXManager.instance.fxGroup); // Ground target
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
            if (attackEndSound.Length > 0) SoundFXManager.instance.PlaySoundClip(attackEndSound, this.transform, 1, SoundFXManager.instance.fxGroup);

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
                        NetworkDataSync.instance.TargetAcquired(this.netID, targetPosition, attackCooldown);
                    }
                    else
                    {
                        NetworkDataSync.instance.TargetAcquired(this.netID, target.netID, attackCooldown);
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
                if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.TargetLost(this.netID);
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
                NetworkCommandSync.instance.IdleCommandSend(this);
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
                if (NetworkDataSync.instance) NetworkDataSync.instance.PlayIdleAnim(netID);
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
                NetworkCommandSync.instance.HoldCommandSend(this);
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
                NetworkCommandSync.instance.FollowCommandSend(this, u, stopDist, embark);
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
                NetworkCommandSync.instance.MoveCommandSend(this, position, stopDist);
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
                NetworkCommandSync.instance.MoveCommandSend(this, position, stopDist);
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
                NetworkCommandSync.instance.AttackCommandSend(this, u);
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
                NetworkCommandSync.instance.AttackPositionCommandSend(this, targetPosition);
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
            else UIManager.instance.ShowNotifyMsg("Can`t attack this unit!", owner, true);
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
                NetworkCommandSync.instance.AttackMoveCommandSend(this, position);
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

        // ============================= DAMAGE ==============================================================================

        /// <summary>
        /// Deals damage to the target.
        /// </summary>
        /// <param name="targetUnit">Unit to attack. Can be null.</param>
        /// <param name="amount">Damage amount.</param>
        /// <param name="damageType">Damage type.</param>
        /// <param name="directAttack">Direct attacks will trigger attack modifications of the unit(splash) and will try to add attack effectors.</param>
        /// <param name="attackPosition">Can be zero, it is used only for when unit is attacking the ground.</param>
        public void DealDamage(Unit targetUnit, float amount, DamageType damageType, bool directAttack, Vector3 attackPosition)
        {
            // Deal damage
            float damageDealt = 0;
            if (targetUnit != null)
            {
                targetUnit.GetDamage(amount, damageType, this.owner, this, directAttack, out damageDealt);
                Effector.EffectorAdd(this, targetUnit, attackEffectors);
            }

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

            float finalDamage = amount;
            foreach (var c in OnBeforeGetDamageCallbacks)
            {
                float damageChanged = c.Callback(this, c.Level, amount, directAttack);
                if (damageChanged < finalDamage) finalDamage = damageChanged;
                else if (finalDamage >= amount && damageChanged > finalDamage) finalDamage = damageChanged;
            }
            amount = finalDamage;

            // Final damage amount based on damage and armor type
            damageDealt = amount * GameManager.instance.damageToArmor[armorType.index * GameManager.instance.DTAWidth + damageType.index] * (1 - ((0.06f * armor) / (1 + 0.06f * armor)));

            // Checks and Get damage
            if (damageDealt < 0) damageDealt = 0;

            if (damageDealt > health) damageDealt = health;

            bool died = ChangeHP(-damageDealt);
            if (died) Die(attackingPlayer, attackingUnit);
            else if (hasHitAnim && directAttack && FoWVisible) animator.CrossFade("hit", crossFadeTime, 0, 0f); //animator.Play("hit", 0, 0.01f);

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
                        if (resourceProduced[i].type.limited) GameResources.instance.ChangeLimit(owner, resourceProduced[i], true);
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
                PlayerControl.instance.RemoveFromSelection(this);

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
                    if (deathSound.Length > 0) SoundFXManager.instance.PlaySoundClip(deathSound, this.transform, 1, SoundFXManager.instance.fxGroup);
                    if (dieVFX) Instantiate(dieVFX, this.transform.position, Quaternion.identity);
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

        // ============================= PARAMETERS ==============================================================================

        /// <summary>
        /// Modifies the unit's maximum HP and adjusts current HP proportionally.
        /// </summary>
        /// <param name="amount">The value to adjust the maximum HP by (positive or negative).</param>
        public void ChangeMaxHP(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            maxHealth = ((maxHealth / passiveEffects.healthChange) + amount) * passiveEffects.healthChange;

            if (maxHealth <= 0) health = 0;
            else health = (health / (maxHealth - amount)) * maxHealth;

            OnHPChange?.Invoke();
        }

        /// <summary>
        /// Modifies the unit's maximum HP and adjusts current HP proportionally.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the maximum HP by (positive or negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeMaxHP(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.healthChange /= (1 - percentage);
                maxHealth /= (1 - percentage);
                health /= (1 - percentage);
            }
            else
            {
                passiveEffects.healthChange *= (1 + percentage);
                maxHealth *= (1 + percentage);
                health *= (1 + percentage);
            }

            OnHPChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s current HP. Returns true if unit dies. If negative change, use GetDamage(), to properly kill the unit and do anything else that might be necessary.
        /// </summary>
        /// <param name="amount">HP change amount.</param>
        /// <param name="noSync">Should server sync the HP change with the clients.</param>
        /// <returns>Did you unit die.</returns>
        public bool ChangeHP(float amount, bool noSync = false)
        {
            health += amount;

            if (health > maxHealth)
            {
                health = maxHealth;
                OnHPChange?.Invoke();
            }
            else if (health < 0.05) // 0.05 is just a float error eliminator 
            {
                health = 0;
                OnHPChange?.Invoke();
                return true;
            }
            else
            {
                OnHPChange?.Invoke();
            }

            // Sync with clients
            if (!noSync && NetworkManager.Singleton.IsServer && !hpSync)
            {
                NetworkDataSync.instance.hpChangedUnits.Add(netID);
                hpSync = true;
                NetworkDataSync.instance.onHPCleared += HPSyncFalse;
            }

            return false;
        }

        /// <summary>
        /// Sets the unit`s current HP to value.
        /// </summary>
        /// <param name="value">New current HP value.</param>
        public void SetHP(float value)
        {
            health = value;

            OnHPChange?.Invoke();

            // Sync with clients
            if (NetworkManager.Singleton.IsServer && !hpSync)
            {
                NetworkDataSync.instance.hpChangedUnits.Add(netID);
                hpSync = true;
                NetworkDataSync.instance.onHPCleared += HPSyncFalse;
            }
        }

        /// <summary>
        /// Changes the unit`s HP regeneration. HP regeneration can go negative.
        /// </summary>
        /// <param name="amount">The value to adjust the HP regeneration by (Positive or Negative).</param>
        public void ChangeHealthRegen(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            healthRegen = ((healthRegen / passiveEffects.healthRegenChange) + amount) * passiveEffects.healthRegenChange;

            OnHPChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s HP regeneration percentage wise. HP regeneration can go negative.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the HP regeneration by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeHealthRegen(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.healthRegenChange /= (1 - percentage);
                healthRegen /= (1 - percentage);
            }
            else
            {
                passiveEffects.healthRegenChange *= (1 + percentage);
                healthRegen *= (1 + percentage);
            }

            OnHPChange?.Invoke();
        }

        // ------ MP ------

        /// <summary>
        /// Modifies the unit's maximum MP and adjusts current MP proportionally.
        /// </summary>
        /// <param name="amount">The value to adjust the maximum MP by (positive or negative).</param>
        public void ChangeMaxMP(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            maxMana = ((maxMana / passiveEffects.manaChange) + amount) * passiveEffects.manaChange;

            if (maxMana <= 0) mana = 0;
            else mana = (mana / (maxMana - amount)) * maxMana;

            OnMPChange?.Invoke();
        }

        /// <summary>
        /// Modifies the unit's maximum MP and adjusts current MP proportionally.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the maximum MP by (positive or negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeMaxMP(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.manaChange /= (1 - percentage);
                maxMana /= (1 - percentage);
                mana /= (1 - percentage);
            }
            else
            {
                passiveEffects.manaChange *= (1 + percentage);
                maxMana *= (1 + percentage);
                mana *= (1 + percentage);
            }

            OnMPChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s current MP.
        /// </summary>
        /// <param name="amount">MP change amount.</param>
        /// <param name="noSync">Should server sync the MP change with the clients.</param>
        public void ChangeMP(float amount, bool noSync = false)
        {
            mana += amount;
            if (mana > maxMana) mana = maxMana;
            else if (mana < 0) mana = 0;
            OnMPChange?.Invoke();

            // Sync with clients
            if (!noSync && NetworkManager.Singleton.IsServer && !mpSync)
            {
                NetworkDataSync.instance.mpChangedUnits.Add(netID);
                mpSync = true;
                NetworkDataSync.instance.onMPCleared += MPSyncFalse;
            }
        }

        /// <summary>
        /// Sets the unit`s current MP to value.
        /// </summary>
        /// <param name="value">New current MP value.</param>
        public void SetMP(float value)
        {
            mana = value;

            OnMPChange?.Invoke();

            // Sync with clients
            if (NetworkManager.Singleton.IsServer && !mpSync)
            {
                NetworkDataSync.instance.mpChangedUnits.Add(netID);
                mpSync = true;
                NetworkDataSync.instance.onMPCleared += MPSyncFalse;
            }
        }

        /// <summary>
        /// Changes the unit`s MP regeneration. MP regeneration can go negative.
        /// </summary>
        /// <param name="amount">The value to adjust the MP regeneration by (Positive or Negative).</param>
        public void ChangeManaRegen(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            manaRegen = ((manaRegen / passiveEffects.manaRegenChange) + amount) * passiveEffects.manaRegenChange;

            OnMPChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s MP regeneration percentage wise. MP regeneration can go negative.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the MP regeneration by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeManaRegen(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.manaRegenChange /= (1 - percentage);
                manaRegen /= (1 - percentage);
            }
            else
            {
                passiveEffects.manaRegenChange *= (1 + percentage);
                manaRegen *= (1 + percentage);
            }

            OnMPChange?.Invoke();
        }

        // ------ ATTACK ------

        /// <summary>
        /// Changes the unit`s attack damage.
        /// </summary>
        /// <param name="amount">The value to adjust the attack damage by (Positive or Negative).</param>
        public void ChangeDamage(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            attackDamage = ((attackDamage / passiveEffects.damageChange) + amount) * passiveEffects.damageChange;

            // if (attackDamage < 0) attackDamage = 0; // Damage cant be lower than 0, Uncomment if needed

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s attack damage percentage wise.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the attack damage by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeDamage(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.damageChange /= (1 - percentage);
                attackDamage /= (1 - percentage);
            }
            else
            {
                passiveEffects.damageChange *= (1 + percentage);
                attackDamage *= (1 + percentage);
            }

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s attack range.
        /// </summary>
        /// <param name="amount">The value to adjust the attack range by (Positive or Negative).</param>
        public void ChangeAttackRange(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            attackRange = ((attackRange / passiveEffects.attackRangeChange) + amount) * passiveEffects.attackRangeChange;

            if (attackRange > unitRadius + Utils.stopDistanceOffset) melee = false;
            else
            {
                melee = true;
                attackRange = unitRadius + Utils.stopDistanceOffset;
            }

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s attack range percentage wise.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the attack range by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeAttackRange(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.attackRangeChange /= (1 - percentage); ;
                attackRange /= (1 - percentage);
            }
            else
            {
                passiveEffects.attackRangeChange *= (1 + percentage);
                attackRange *= (1 + percentage);
            }

            if (attackRange > unitRadius) melee = false;
            else
            {
                melee = true;
                attackRange = unitRadius + Utils.stopDistanceOffset;
            }
            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s attack speed. Final attack speed value should not be lower than 0, preferably it should not go below ~0.1.
        /// </summary>
        /// <param name="amount">The value to adjust the attack speed by (Positive or Negative).</param>
        public void ChangeAttackSpeed(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            attackSpeed = ((attackSpeed / passiveEffects.attackSpeedChange) + amount) * passiveEffects.attackSpeedChange;

            // if (attackSpeed < 0) attackSpeed = 0;

            ChangeAttackAnimationSpeed();

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s attack speed percentage wise. Final attack speed value should not be lower than 0, preferably it should not go below ~0.1.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the attack speed by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeAttackSpeed(float percentage, bool percentageChange)
        {
            // Only attack speed is calculated this way, to prevent reaching 0 attackSpeed
            if (percentage < 0)
            {
                passiveEffects.attackSpeedChange *= (1 - percentage);
                attackSpeed *= (1 - percentage);
            }
            else
            {
                passiveEffects.attackSpeedChange /= (1 + percentage);
                attackSpeed /= (1 + percentage);
            }

            ChangeAttackAnimationSpeed();

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the damage type of the unit.
        /// </summary>
        /// <param name="type">New damage type.</param>
        public void ChangeDamageType(DamageType type)
        {
            damageType = type;

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the attack selector of the unit.
        /// </summary>
        /// <param name="attackSelectorNew">New attack selector.</param>
        public void ChangeAttackSelector(UnitSelector attackSelectorNew)
        {
            attackUnitSelector = attackSelectorNew;
            AttackSelectorInitialize();

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the Multitarget attack modificator`s parameters.
        /// </summary>
        /// <param name="multitargetOn">Should multitarget be on.</param>
        /// <param name="multiCount">How many additional units beside the main one should this unit attack.</param>
        public void ChangeMultitarget(bool multitargetOn, int multiCount)
        {
            multiTarget = multitargetOn;
            multiTargetCount = multiCount;
            additionalTargets = new Unit[multiTargetCount + 1];
            if (!melee && attackType == AttackType.Continuous && projectileGO.GetComponent<VFXLine>())
            {
                // If attack type is continuous we instantiate attackVFX at launchSite(s)
                attackVFXLine = VFXLine.CreateVFX(projectileGO.GetComponent<VFXLine>(), this);
            }
        }

        /// <summary>
        /// Changes the Splash attack modificator`s parameters.
        /// </summary>
        /// <param name="splashOn">Should splash be on.</param>
        /// <param name="radius">Radius of the splash.</param>
        /// <param name="reduction">Reduction of the damage.</param>
        /// <param name="followTarget">For ranged only. Should the projectile follow the target.</param>
        public void ChangeSplash(bool splashOn, float radius, float reduction, bool followTarget)
        {
            isSplash = splashOn;
            splashRadius = radius;
            splashReduction = reduction;
            projectileFollowTarget = followTarget;
        }

        /// <summary>
        /// Changes the Bounce attack modificator`s parameters.
        /// </summary>
        /// <param name="count">How many times the bounce should happen. 0 means no bounce modificator.</param>
        /// <param name="range">Range of the bounce.</param>
        /// <param name="reducton">Damage reduction for each consequent target.</param>
        public void ChangeBounce(int count, float range, float reducton)
        {
            bounceCount = count;
            bounceRange = range;
            bounceReduction = reducton;
            if (!melee && attackType == AttackType.Continuous && projectileGO.GetComponent<VFXLine>())
            {
                // If attack type is continuous we instantiate attackVFX at launchSite(s)
                attackVFXLine = VFXLine.CreateVFX(projectileGO.GetComponent<VFXLine>(), this);
            }
        }

        // ------ ARMOR ------

        /// <summary>
        /// Changes the unit`s armor points.
        /// </summary>
        /// <param name="amount">The value to adjust the armor points by (Positive or Negative).</param>
        public void ChangeArmor(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            armor = ((armor / passiveEffects.armorChange) + amount) * passiveEffects.armorChange;

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s armor points percentage wise.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the armor points by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeArmor(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.armorChange /= (1 - percentage);
                armor /= (1 - percentage);
            }
            else
            {
                passiveEffects.armorChange *= (1 + percentage);
                armor *= (1 + percentage);
            }

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the armor type of the unit.
        /// </summary>
        /// <param name="type">New armor type.</param>
        public void ChangeArmorType(ArmorType type)
        {
            armorType = type;

            OnCharacteristicsChange?.Invoke();
        }

        // ------ MOVE ------

        /// <summary>
        /// Changes the unit`s movement speed.
        /// </summary>
        /// <param name="amount">The value to adjust the movement speed by (Positive or Negative).</param>
        public void ChangeMoveSpeed(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            moveSpeed = ((moveSpeed / passiveEffects.moveSpeedChange) + amount) * passiveEffects.moveSpeedChange;

            if (moveSpeed < 0) agent.speed = 0;
            else agent.speed = moveSpeed;
            if (invisibleAgent) invisibleAgent.speed = agent.speed;

            ChangeMoveSpeedAnimationSpeed();

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s movement speed percentage wise.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the movement speed by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeMoveSpeed(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.moveSpeedChange /= (1 - percentage);
                moveSpeed /= (1 - percentage);
            }
            else
            {
                passiveEffects.moveSpeedChange *= (1 + percentage);
                moveSpeed *= (1 + percentage);
            }

            if (moveSpeed < 0) agent.speed = 0;
            else agent.speed = moveSpeed;
            if (invisibleAgent) invisibleAgent.speed = agent.speed;

            ChangeMoveSpeedAnimationSpeed();

            OnCharacteristicsChange?.Invoke();
        }

        // ------ OTHER ------

        /// <summary>
        /// Changes the unit`s XP reward.
        /// </summary>
        /// <param name="amount">The value to adjust the XP reward by (Positive or Negative).</param>
        public void ChangeXpReward(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            xpReward = (int)(((xpReward / passiveEffects.xpRewardChange) + amount) * passiveEffects.xpRewardChange);
        }

        /// <summary>
        /// Changes the unit`s XP reward percentage wise.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the XP reward by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeXpReward(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.xpRewardChange /= (1 - percentage);
                xpReward = (int)(xpReward / (1 - percentage));
            }
            else
            {
                passiveEffects.xpRewardChange *= (1 + percentage);
                xpReward = (int)(xpReward * (1 + percentage));
            }
        }

        /// <summary>
        /// Changes the unit`s vision range.
        /// </summary>
        /// <param name="amount">The value to adjust the vision range by (Positive or Negative).</param>
        public void ChangeVisionRange(int visionRange)
        {
            if (this.visionRange == visionRange) return;
            FogOfWar.instance.CellRemove(this);
            this.visionRange += visionRange;
            if (visionRange < 0) visionRange = 1;
            else if (visionRange > Utils.maxVisionRange) visionRange = Utils.maxVisionRange;
            FogOfWar.instance.CellAssignment(this, true);
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
                // If was not previously stunned we create VFX
                if (stunnedVFX == null)
                {
                    stunnedVFX = Instantiate(ReferenceManager.instance.stunnnedVFX, vfxHolder);
                    stunnedVFX.transform.localPosition = new Vector3(0, unitHeight, 0);
                }

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
                if (stunnedVFX == null)
                {
                    // Show that unit is stunned
                    stunnedVFX = Instantiate(ReferenceManager.instance.stunnnedVFX, vfxHolder);
                    stunnedVFX.transform.localPosition = new Vector3(0, unitHeight, 0);
                }

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
                // If was not previously muted we create VFX
                if (mutedVFX == null)
                {
                    mutedVFX = Instantiate(ReferenceManager.instance.mutedVFX, vfxHolder);
                    mutedVFX.transform.localPosition = new Vector3(0, unitHeight, 0);
                }

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
                if (mutedVFX == null)
                {
                    // Show that unit is muted
                    mutedVFX = Instantiate(ReferenceManager.instance.mutedVFX, vfxHolder);
                    mutedVFX.transform.localPosition = new Vector3(0, unitHeight, 0);
                }
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
                // If was not previously muted we create VFX
                if (disarmedVFX == null)
                {
                    disarmedVFX = Instantiate(ReferenceManager.instance.disarmedVFX, vfxHolder);
                    disarmedVFX.transform.localPosition = new Vector3(0, unitHeight, 0);
                }

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
                if (disarmedVFX == null)
                {
                    // Show that unit is muted
                    disarmedVFX = Instantiate(ReferenceManager.instance.disarmedVFX, vfxHolder);
                    disarmedVFX.transform.localPosition = new Vector3(0, unitHeight, 0);
                }
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

            ReplaceRenderers(shapeUnit, false);
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
                    FogOfWar.instance.CellAssignment(this);

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
                            NetworkDataSync.instance.removeSyncList.Add(netID);
                            removeFromPosSync = true;
                        }
                        else
                        {
                            NetworkDataSync.instance.positionSyncList.Remove(netID);
                            NetworkDataSync.instance.removeSyncList.Add(netID);
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
                        NetworkDataSync.instance.positionSyncList.Add(netID);
                        positionsSent = false;
                    }
                }
            }
        }

        // ============================= VFX ==============================================================================

        /// <summary>
        /// Adds VFX element to VFX Holder.
        /// </summary>
        /// <param name="vfx">VFX to add.</param>
        /// <param name="aboveHead">Should VFX be above head.</param>
        /// <param name="unitCentre">Should VFX be at the unit`s centre.</param>
        /// <returns></returns>
        public VFXReferencer AddVFX(VFXReferencer vfx, bool aboveHead = false, bool unitCentre = false)
        {
            VFXReferencer temp = Instantiate(vfx, vfxHolder);
            if (unitCentre) temp.transform.localPosition += new Vector3(0, unitHeight * 0.5f, 0);
            else if (aboveHead) temp.transform.localPosition += new Vector3(0, unitHeight + 0.1f, 0);

            return temp;
        }

        /// <summary>
        /// Removes VFX element from unit`s VFX Holder.
        /// </summary>
        /// <param name="vfx">VFX to remove.</param>
        public void RemoveVFX(VFXReferencer vfx)
        {
            VFXReferencer[] elements = vfxHolder.GetComponentsInChildren<VFXReferencer>();
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].id == vfx.id)
                {
                    Destroy(elements[i].gameObject);
                    return;
                }
            }
        }

        // ============================= PROCESSES ==============================================================================

        /// <summary>
        /// Every update calculates the timer for processes, upon completion finishes the process and goes for the next one.
        /// </summary>
        void HandleProcesses()
        {
            if (canProcess)
            {
                // If there are processes
                if (activeProcess[0] != null)
                {
                    // If new process, set timer
                    if (currentProcessTimer == -1)
                    {
                        currentProcessTimer = activeProcess[0].castTime[processLevel[0]];
                    }

                    // Ongoing process, reduce timer
                    currentProcessTimer -= Time.deltaTime;

                    // Process finished
                    if (currentProcessTimer <= 0)
                    {
                        // Only server can finish the process
                        if (NetworkConnectionHandler.isClient) return;

                        // Process finished, do something
                        if (activeProcess[0] is UnitTraining)
                        {
                            if (waypointLocation != Vector2.zero) activeProcess[0].Use(this, this.owner, processLevel[0], new Vector3(waypointLocation.x, 0, waypointLocation.y));
                            else if (waypointUnit != null) activeProcess[0].Use(this, this.owner, processLevel[0], waypointUnit);
                            else activeProcess[0].Use(this, this.owner, processLevel[0]);
                        }
                        else
                        {
                            activeProcess[0].Use(this, this.owner, processLevel[0]);
                        }

                        ProcessMoveForward(0); // Move next process forward
                        OnProcessUpdate?.Invoke();

                        if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.FinishProcess(this);
                    }
                }
            }
        }

        /// <summary>
        /// Move next process forward, if it exists. Can be used to remove process at particular index.
        /// </summary>
        /// <param name="startIndex">Process index that is no longer there. For example: 0 means process 1 will take its place.</param>
        public void ProcessMoveForward(int startIndex)
        {
            if (startIndex == 0) currentProcessTimer = -1; // Timer Reset

            while (startIndex < GameManager.maxProcessCount - 1)
            {
                if (activeProcess[startIndex + 1] != null)
                {
                    activeProcess[startIndex] = activeProcess[startIndex + 1];
                    activeProcess[startIndex + 1] = null;
                    processLevel[startIndex] = processLevel[startIndex + 1];
                    processLevel[startIndex + 1] = 0;
                    startIndex++;
                }
                else
                {
                    // No processes to move forward
                    activeProcess[startIndex] = null;
                    processLevel[startIndex] = 0;
                    break;
                }
            }

            OnRedrawAbilityView?.Invoke();
        }

        /// <summary>
        /// Cancels process at specified index.
        /// </summary>
        /// <param name="index">Index of the process.</param>
        /// <returns></returns>
        public bool CancelProcess(int index)
        {
            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.instance.CancelProcessCommandSend(this, index);
                return false;
            }

            if (activeProcess[index] != null)
            {
                // If process in a research, cancel its processing status
                if (activeProcess[index] is Research)
                {
                    Research research = (Research)activeProcess[index];
                    TechnologyManager.instance.TechFinishedProcessing(research.unlockTech[processLevel[index]], owner);
                }

                // Return the cost
                if (activeProcess[index].cost.Length > processLevel[index]) GameResources.instance.ChangeAmount(owner, activeProcess[index].cost[processLevel[index]].data, 1, false, true);

                ProcessMoveForward(index);

                OnProcessUpdate?.Invoke();

                // Send to clients
                if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.CancelProcess(this, index);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Adds the process.
        /// </summary>
        /// <param name="abilityIndex">Global index of ability in the ability pool of the unit. You can get it via Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is the process an item?</param>
        /// <returns></returns>
        public bool AddProcess(int abilityIndex, bool isItem)
        {
            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.instance.AddProcessCommandSend(this, abilityIndex, isItem);
                return false;
            }

            if (canProcess)
            {
                Ability currentProcess = (isItem) ? items[abilityIndex] : abilities[abilityIndex]; // Utils.GetAbilityByIndexName(this, abilityPath, out int abilityIndex);
                int currentProcessLevel = (isItem) ? 0 : abilityLevel[abilityIndex];

                // Check the costs
                if (CheckAbilityItemRequirements(owner, abilityIndex, isItem)) return false;

                // If upgrade we do some checks
                if (currentProcess is Research)
                {
                    Research upgradeAbility = (Research)currentProcess;

                    // If this upgrade is already being processed do not allow to process it twice
                    if (upgradeAbility.unlockTech.Length > currentProcessLevel && !TechnologyManager.instance.IsTechBeingProcessed(upgradeAbility.unlockTech[currentProcessLevel], owner))
                    {
                        // Set upgrade status to being processed, will make sure this upgrade can not be used twice
                        TechnologyManager.instance.TechBeingProcessed(upgradeAbility.unlockTech[currentProcessLevel], owner);
                    }
                    else
                    {
                        if (upgradeAbility.abilityName.Length > currentProcessLevel) UIManager.instance.ShowNotifyMsg(upgradeAbility.abilityName[currentProcessLevel] + " is already being researched", owner, true);
                        else UIManager.instance.ShowNotifyMsg(upgradeAbility.abilityName[upgradeAbility.abilityName.Length - 1] + " is already being researched", owner, true);

                        return false;
                    }
                }

                // Get process count
                int index = 0;
                while (activeProcess[index] != null)
                {
                    index++;

                    if (index == GameManager.maxProcessCount)
                    {
                        // No more processes can be added
                        return false;
                    }
                }

                // Add process
                activeProcess[index] = currentProcess;
                processLevel[index] = currentProcessLevel;

                // Subtract costs
                SubtractAbilityItemCost(owner, abilityIndex, isItem);

                // Item charge decrease
                if (isItem) ItemChargesChange(abilityIndex);

                OnProcessUpdate?.Invoke();
                OnRedrawAbilityView?.Invoke();

                // Send to clients
                if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.AddProcess(this, index, abilityIndex, isItem);
                return true;
            }
            else
            {
                UIManager.instance.ShowNotifyMsg("Can`t add process, this unit does not support it. Tick canProcess box in the unit parameters", owner, true);
                return false;
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
                NetworkCommandSync.instance.SetWaypointCommandSend(this, unit);
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
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.WaypointSet(this, waypointUnit, waypointLocation);
        }

        /// <summary>
        /// Command to set the position as the waypoint for the building.
        /// </summary>
        /// <param name="position">Waypoint position.</param>
        public void SetWaypoint(Vector2 position)
        {
            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.instance.SetWaypointCommandSend(this, position);
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
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.WaypointSet(this, waypointUnit, waypointLocation);
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

            ReferenceManager.instance.ShowWaypoint(waypointLocation, waypointUnit);
        }

        // ============================= MATERIALS AND RENDERERS ==============================================================================

        /// <summary>
        /// Sets unit`s player color in MaterialPropertyBlock
        /// </summary>
        void SetPlayerColor()
        {
            matBlock.SetColor("_PlayerColor", SlotManager.instance.playerColors[owner]);

            foreach (var renderer in meshRenderers)
            {
                renderer.SetPropertyBlock(matBlock);
            }
        }

        /// <summary>
        /// Sets the Overlay color. It is used for coloring the whole unit with particular color. For example when it is affected by certain ability during the cast.
        /// </summary>
        /// <param name="color">Overlay color.</param>
        /// <param name="oneFrame">Should the overlay color be set only for one frame.</param>
        public void SetOverlayColor(Color color, bool oneFrame = true)
        {
            matBlock.SetColor("_OverlayColor", color);

            foreach (var renderer in meshRenderers)
            {
                renderer.SetPropertyBlock(matBlock);
            }

            if (oneFrame) overlayColorOneFrame = true;
        }

        /// <summary>
        /// Resets overlay color.
        /// </summary>
        void ResetOverlayColor()
        {
            if (isInvisible) matBlock.SetColor("_OverlayColor", StateColors.Invisibility);
            else matBlock.SetColor("_OverlayColor", StateColors.Default);

            foreach (var renderer in meshRenderers)
            {
                renderer.SetPropertyBlock(matBlock);
            }
        }

        /// <summary>
        /// Overlay color resets itself after every frame.
        /// </summary>
        void HandleOverlayColor()
        {
            if (overlayColorOneFrame)
            {
                ResetOverlayColor();
                overlayColorOneFrame = false;
            }
        }

        /// <summary>
        /// Hides the unit by disabling its renderers. Creates static copy if necessary.
        /// </summary>
        public void HideRenderers()
        {
            if (!renderersOn) return;

            // Create static copy if unit is staticDestructible
            CreateStaticCopy();

            // Remove from selection
            PlayerControl.instance.RemoveFromSelection(this);

            for (int i = 0; i < renderers.Count; i++) renderers[i].gameObject.SetActive(false);
            for (int i = 0; i < meshRenderers.Count; i++)
            {
                if (netID == 52529) Debug.Log("mesh false " + meshRenderers[i].gameObject.name);
                meshRenderers[i].enabled = false;
            }

            renderersOn = false;
        }

        /// <summary>
        /// Shows the unit by enabling its renderers. Destroys static copy if necessary.
        /// </summary>
        public void ShowRenderers()
        {
            if (renderersOn) return;
            // If show renderers called while unit is not visible by the current team, ignore it
            if (!IsVisible(SlotManager.instance.currentTeam)) return;

            DestroyStaticCopy();

            for (int i = 0; i < renderers.Count; i++) renderers[i].gameObject.SetActive(true);
            for (int i = 0; i < meshRenderers.Count; i++) meshRenderers[i].enabled = true;

            if (animator)
            {
                if (isBeingBuilt)
                {
                    AnimatorSetBool(AnimationState.Idle, false);
                    if (animator && constructionUnit.constructionAnimExists) animator.Play("construction", 0, constructionUnit.currentConstructionPercentage);
                }
                else AnimatorSetBool(currentAnimatorBoolState, true);
                animator.SetFloat("blendingIndex", animationBlendingIndex);
            }

            renderersOn = true;
        }

        /// <summary>
        /// Copies the visuals from another unit. Call idle before replacing! Make sure that new unit`s radius and colliders are same sized! Only height is copied as part of visuals.
        /// </summary>
        /// <param name="refUnit">Unit to copy the visuals from.</param>
        /// <param name="replacePermanently">Should the original visuals be replaced completely.</param>
        public void ReplaceRenderers(Unit refUnit, bool replacePermanently)
        {
            // Instantiate new unit
            Unit newUnit = Instantiate(refUnit, transform.position, Quaternion.identity);
            newUnit.transform.rotation = transform.rotation; // Copy this unit`s rotation
                                                             // Copy main renderer
            GameObject newMainRenderer = newUnit.transform.GetChild(0).gameObject;
            newMainRenderer.transform.parent = transform;
            newMainRenderer.transform.localPosition = new Vector3(0, 0, 0);
            newMainRenderer.transform.SetSiblingIndex(1);
            // Destroy created new unit
            DestroyImmediate(newUnit.gameObject);

            // Renderer set
            if (replacePermanently)
            {
                DestroyImmediate(mainRenderer);
                currentMainShape = refUnit;
            }
            else
            {
                if (mainRenderer.transform.GetSiblingIndex() == 0)
                {
                    // Currently in original form
                    mainRenderer.SetActive(false);
                }
                else
                {
                    // Currently polymorphed
                    DestroyImmediate(mainRenderer);
                }
            }

            mainRenderer = newMainRenderer;

            // ====== Save Unit Data ======
            pmVerticalPart = verticalPart;
            pmHorizontallPart = horizontalPart;
            pmHorizontalForward = horizontalPartForward;
            pmLaunchSite = launchSite;

            // ====== Copy Unit Data ======
            // Height
            unitHeight = newUnit.unitHeight;
            // VerticalPart
            verticalPart = newUnit.verticalPart;
            // HorizontalPart
            float yRot = (horizontalPart != transform) ? GetUnitRotation() : 0; // Get current Y rotation with old horizontal part
            horizontalPart = newUnit.horizontalPart;
            horizontalPartSet = false;
            if (yRot != 0) SetUnitRotation(yRot); // If equals to 0, we already set rotation when newUnit was instantiated 

            // Sound
            readySound = newUnit.readySound;
            moveSound = newUnit.moveSound;
            clickSound = newUnit.clickSound;
            deathSound = newUnit.deathSound;
            attackCommandSound = newUnit.attackCommandSound;
            attackStartSound = newUnit.attackStartSound;
            attackEndSound = newUnit.attackEndSound;
            weaponSound = newUnit.weaponSound;
            // Animation
            animationMoveSpeed = newUnit.animationMoveSpeed;
            animationAttackDelay = newUnit.animationAttackDelay;
            // DieVFX
            dieVFX = newUnit.dieVFX;
            // Launch
            launchSite = newUnit.launchSite;
            launchVFX = newUnit.launchVFX;
            // Projectile
            if (newUnit.projectileGO != null && ((attackType == AttackType.Continuous && newUnit.attackType == AttackType.Continuous) || (attackType != AttackType.Continuous && newUnit.attackType != AttackType.Continuous)))
            {
                // We copy projectile from reference only if attack types match and reference projectile exists
                projectileGO = newUnit.projectileGO;
            }

            // Renderers and Colors
            CalculateVisuals();
            SetPlayerColor();

            // Set animation state
            if (isBeingBuilt)
            {
                AnimatorSetBool(AnimationState.Idle, false);
                if (animator && constructionUnit.constructionAnimExists) animator.Play("construction", 0, constructionUnit.currentConstructionPercentage);
            }
            else AnimatorSetBool(currentAnimatorBoolState, true);
            if (animator) animator.SetFloat("blendingIndex", animationBlendingIndex);

            // We must set active false only after the Animator.HasState had been run
            if (!FoWVisible) for (int i = 0; i < meshRenderers.Count; i++) meshRenderers[i].enabled = false;
        }

        /// <summary>
        /// Restores the visual representation of the unit to its original.
        /// </summary>
        public void RestoreRenderers()
        {
            // For horizontal part
            float yRot = (horizontalPart != transform) ? GetUnitRotation() : 0; // Get current Y rotation with old horizontal part

            // Replace
            GameObject ogRenderer = transform.GetChild(0).gameObject;
            if (ogRenderer != mainRenderer) DestroyImmediate(mainRenderer);
            mainRenderer = ogRenderer;
            mainRenderer.SetActive(true);

            // ====== Copy Unit Data ======
            var ogUnit = GameManager.instance.gameUnits[unitTypeID];
            // Height
            unitHeight = ogUnit.unitHeight;
            // VerticalPart
            verticalPart = pmVerticalPart;
            // HorizontalPart
            horizontalPart = pmHorizontallPart;
            horizontalPartForward = pmHorizontalForward;
            horizontalPartSet = true;
            if (yRot != 0) SetUnitRotation(yRot); // If equals to 0, we already set rotation when newUnit was instantiated 

            // Sound
            readySound = ogUnit.readySound;
            moveSound = ogUnit.moveSound;
            clickSound = ogUnit.clickSound;
            deathSound = ogUnit.deathSound;
            attackCommandSound = ogUnit.attackCommandSound;
            attackStartSound = ogUnit.attackStartSound;
            attackEndSound = ogUnit.attackEndSound;
            weaponSound = ogUnit.weaponSound;
            // Animation
            animationMoveSpeed = ogUnit.animationMoveSpeed;
            animationAttackDelay = ogUnit.animationAttackDelay;
            // DieVFX
            dieVFX = ogUnit.dieVFX;
            // Launch
            launchSite = pmLaunchSite;
            launchVFX = ogUnit.launchVFX;
            // Projectile
            if (ogUnit.projectileGO != null && ((attackType == AttackType.Continuous && ogUnit.attackType == AttackType.Continuous) || (attackType != AttackType.Continuous && ogUnit.attackType != AttackType.Continuous)))
            {
                // We copy projectile from reference only if attack types match and reference projectile exists
                projectileGO = ogUnit.projectileGO;
            }

            // Renderers and Colors
            CalculateVisuals();
            SetPlayerColor();

            // Set animation state
            if (isBeingBuilt)
            {
                AnimatorSetBool(AnimationState.Idle, false);
                if (animator && constructionUnit.constructionAnimExists) animator.Play("construction", 0, constructionUnit.currentConstructionPercentage);
            }
            else AnimatorSetBool(currentAnimatorBoolState, true);
            if (animator) animator.SetFloat("blendingIndex", animationBlendingIndex);

            // We must set active false only after the Animator.HasState had been run
            if (!FoWVisible) for (int i = 0; i < meshRenderers.Count; i++) meshRenderers[i].enabled = false;
        }

        // ============================= ANIMATIONS ==============================================================================

        /// <summary>
        /// Changes the attack animation speed depending on the current attack speed.
        /// </summary>
        /// <param name="periodic">Are changing the animation speed based on periodic attack speed.</param>
        public void ChangeAttackAnimationSpeed(bool periodic = false)
        {
            if (animator && attackAnimationsCount != 0)
            {
                if (periodic)
                {
                    if (periodicAttackDelay < attackAnimationLength)
                    {
                        float animSpeed = attackAnimationLength / periodicAttackDelay;
                        animator.SetFloat("attackspeed", animSpeed);
                        currentAttackAnimLength = periodicAttackDelay; // attackAnimationLength * animSpeed;
                    }
                    else
                    {
                        animator.SetFloat("attackspeed", 1);
                        currentAttackAnimLength = attackAnimationLength;
                    }
                }
                else
                {
                    if (attackSpeed < attackAnimationLength)
                    {
                        float animSpeed = attackAnimationLength / attackSpeed;
                        animator.SetFloat("attackspeed", animSpeed);
                        currentAttackAnimLength = attackSpeed; // attackAnimationLength * animSpeed;
                    }
                    else
                    {
                        animator.SetFloat("attackspeed", 1);
                        currentAttackAnimLength = attackAnimationLength;
                    }
                }

                if (animationAttackDelay > 1) animationAttackDelay = 1;
                currentAnimAttackDelay = currentAttackAnimLength * animationAttackDelay - crossFadeTime;
                if (currentAnimAttackDelay < 0) currentAnimAttackDelay = 0;
            }
        }

        /// <summary>
        /// Changes the movement animation speed depending on the current movespeed.
        /// </summary>
        public void ChangeMoveSpeedAnimationSpeed()
        {
            if (animator && animationMoveSpeed != 0)
            {
                animator.SetFloat("movespeed", moveSpeed / animationMoveSpeed);
            }
        }

        /// <summary>
        /// Randomly plays different idle animation if they exist.
        /// </summary>
        public void RandomIdleAnimation()
        {
            if (unitState != UnitStates.Idle) return;
            if (constructionUnit && constructionUnit.isWorking) return;

            // We use currentActionTime to save memory instead of creating a new variable
            if (FoWVisible && (!NetworkConnectionHandler.isClient || (!activeAbilityInUse && activeAbilityCastTime == 0))) // For clients we should also check the isWorking
            {
                if (!stunned && !isMoving && target == null && !isBeingBuilt)
                {
                    currentActionTime += GameManager.instance.currentDeltaTime;

                    if (currentActionTime > idleRandomTime && !dead)
                    {
                        currentActionTime = 0;
                        animator.CrossFade("idle" + UnityEngine.Random.Range(0, idleAnimationCount), crossFadeTime, 0, 0f);
                    }
                }
            }
        }

        /// <summary>
        /// Changes the bool parameter for the loop animations of the animator.
        /// </summary>
        /// <param name="state">Animation state to change.</param>
        /// <param name="boolState">Bool parameters of the animation state.</param>
        /// <param name="setIdle">Should the idle state be set to the opposite of the boolState.</param>
        public void AnimatorSetBool(AnimationState state, bool boolState, bool setIdle = true)
        {
            if (animator)
            {
                if (state == AnimationState.Reset)
                {
                    // Reset
                    animator.SetBool("idle", true);
                    animator.SetBool("walk", false);
                    animator.SetBool("attack", false);
                    animator.SetBool("building", false);
                    animator.SetBool("idleReady", false);
                    animator.SetBool("casting", false);

                    currentAnimatorBoolState = AnimationState.Idle;
                }
                else if (state == AnimationState.Idle)
                {
                    // Idle
                    animator.SetBool("idle", boolState);
                    currentAnimatorBoolState = state;
                }
                else
                {
                    if (state == AnimationState.IdleReady)
                    {
                        // Idle Ready
                        animator.SetBool("idleReady", boolState);
                    }
                    else if (state == AnimationState.Walk)
                    {
                        // Walk
                        animator.SetBool("walk", boolState);
                    }
                    else if (state == AnimationState.Casting)
                    {
                        // Casting
                        animator.SetBool("casting", boolState);
                    }
                    else if (state == AnimationState.ContinuousAttack)
                    {
                        // Continuous attack
                        animator.SetBool("attack", boolState);
                    }
                    else if (state == AnimationState.Building)
                    {
                        // Building
                        animator.SetBool("building", boolState);
                    }

                    // Idle
                    if (setIdle)
                    {
                        if (boolState)
                        {
                            animator.SetBool("idle", false);
                            currentAnimatorBoolState = state;
                        }
                        else
                        {
                            animator.SetBool("idle", true);
                            currentAnimatorBoolState = AnimationState.Idle;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Sets the animation blending index.
        /// </summary>
        /// <param name="blendingIndex">Animation blend index.</param>
        public void SetAnimationBlendingIndex(float blendingIndex, bool sync = true)
        {
            if (animator) animator.SetFloat("blendingIndex", blendingIndex);
            animationBlendingIndex = blendingIndex;

            // Sync with clients
            if (sync && NetworkManager.Singleton.IsServer) NetworkDataSync.instance.AnimationPrefixClientRpc(netID, animationBlendingIndex);
        }

        // ============================= EFFECTORS ==============================================================================

        /// <summary>
        /// Called every GameManager.Tick to update the state of effectors.
        /// </summary>
        private void HandleEffectors()
        {
            // Reverse iteration because list might be modified
            for (int i = effectors.Count - 1; i >= 0; i--)
            {
                Effector.EffectorUpdate(effectors[i], this);
            }
        }

        // ============================= PROJECTORS ==============================================================================

        /// <summary>
        /// Enables the selection circle, if does not exist creates one
        /// </summary>
        public void ProjectorsEnable()
        {
            if (selectionCircle)
            {
                // Activate
                selectionCircle.gameObject.SetActive(true);
            }
            else
            {
                // Create
                SpriteRenderer sr = Instantiate(ReferenceManager.instance.selectionRenderer, transform);
                selectionCircle = sr.transform;
                // Sprite
                if (unitRadius > Utils.largeSelectorSize) sr.sprite = ReferenceManager.instance.selectionLarge;
                else if (unitRadius > Utils.mediumSelectorSize) sr.sprite = ReferenceManager.instance.selectionMedium;
                else sr.sprite = ReferenceManager.instance.selectionSmall;
                // Color
                if (SlotManager.instance.currentTeam != team && team != (int)Teams.NeutralPassive) sr.color = ReferenceManager.instance.selectionEnemyColor;
                else sr.color = ReferenceManager.instance.selectionAllyColor;
                // Scale
                selectionCircle.localScale = new Vector3(unitRadius / selectionCircle.lossyScale.x, unitRadius / selectionCircle.lossyScale.y, 1);
                // Position
                selectionCircle.localPosition += new Vector3(0, 0.1f, 0);
            }
        }

        /// <summary>
        /// Disables the selection circle
        /// </summary>
        public void ProjectorsDisable()
        {
            if (selectionCircle) selectionCircle.gameObject.SetActive(false);
        }

        // ============================= SOUND ==============================================================================

        /// <summary>
        /// Every game tick rate updates the timer
        /// </summary>
        public void CommandSoundTimerUpdate()
        {
            commandSoundTime -= GameManager.instance.currentDeltaTime;

            if (commandSoundTime <= 0)
            {
                // Command sound is finished
                CommandSoundDestroy();
            }
        }

        private void CommandSoundDestroy()
        {
            if (commandSound) Destroy(commandSound.gameObject);
            commandSoundTime = 0;
            GameManager.instance.Tick -= CommandSoundTimerUpdate;
        }

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
                if (Physics.BoxCast(new Vector3(position.x, Utils.raycastPointY, position.z), boxCollider.size * 0.5f * transform.localScale.x, Vector3.down, out _, transform.rotation, Utils.raycastPointY * 5f, LayerMask.GetMask("Default"))) // Check against units/buildings/objects
                {
                    return true;
                }
            }
            else
            {
                // Capsule Collider
                if (Physics.SphereCast(new Vector3(position.x, Utils.raycastPointY, position.z), unitRadius, Vector3.down, out _, Utils.raycastPointY * 5f, LayerMask.GetMask("Default"))) // Check against units/buildings/objects
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
            PlayerControl.instance.RemoveFromSelection(this);

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
                if (PlayerControl.instance.activeUnit != null)
                {
                    UIManager.instance.UnsubscribeToUnit(PlayerControl.instance.activeUnit);
                }

                // Healthbar
                if (newTeam != team && unitType != UnitType.StaticDestructible && unitType != UnitType.Item && unitType != UnitType.Tree)
                {
                    var healthBar = transform.Find("HealthBar(Clone)");
                    if (healthBar) { healthBar.SetParent(null); GameManager.Destroy(healthBar.gameObject); }
                    if (newTeam != SlotManager.instance.currentTeam && newTeam != (int)Teams.NeutralPassive) Instantiate(ReferenceManager.instance.healthBarEnemy, this.transform).name = "HealthBar(Clone)";
                    else Instantiate(ReferenceManager.instance.healthBar, this.transform);
                }

                // Minimap icon
                var minimapIcon = transform.Find("MiniMapIcon(Clone)");
                if (minimapIcon == null) minimapIcon = Instantiate(ReferenceManager.instance.miniMapIcon, this.transform);
                minimapIcon.GetComponent<SpriteRenderer>().color = SlotManager.instance.playerColors[newOwner];

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
                            if (resourceProduced[i].type.limited) GameResources.instance.ChangeLimit(owner, resourceProduced[i], true);
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
                if (PlayerControl.instance.IsSelected(this)) ProjectorsEnable();

                // Initial ability lock and level calculation 
                AllAbilityLockLevelsCalculate();

                // UI Refresh
                if (PlayerControl.instance.activeUnit != null)
                {
                    UIManager.instance.SubscribeToUnit();
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
            if (unit.readySound.Length > 0) SoundFXManager.instance.PlayCommandSound(unit, unit.readySound, 1, true);

            // Network sync
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.UnitSpawn(unit.unitTypeID, position, rotation, owner, unit.netID);
            return unit;
        }
    }
}
