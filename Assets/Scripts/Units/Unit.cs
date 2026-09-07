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
        // [Interflow fix 2026-09-03 control-as-effectors] Поля времени (stunTime, currentMuteTime,
        // currentDisarmTime) СНЕСЕНЫ: контроль стал состоянием-эффектором, и время отсчитывает
        // жизненный цикл наложения (Units/Unit.Control.cs). Флаги ниже — производные от списка
        // состояний, их выставляет только RecalculateControl. Руками не присваивать.
        [HideInInspector] public bool stunned; // When unit gets stunned it can`t do anything till stun wears off
        Transform stunnedVFX; // Stunned VFX reference
        // Muted
        [HideInInspector] public bool muted; // When unit is muted, it can not cast any abilities
        Transform mutedVFX; // Muted VFX reference
        // Disarmed - can attack used as bool to know if disarmed
        [HideInInspector] public bool disarmed; // If this unit is currently disarmed
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
            if (NetworkConnectionHandler.Instance.connectionStage == 1 || NetworkConnectionHandler.Instance.connectionStage == 2)
            {
                SlotManager.Instance.AssignNetID(this);
                return;
            }

            // When standard connection type, for in-scene units initialization happens after all players finished loading the scene
            if (!SlotManager.Instance.gameOn)
            {
                SlotManager.Instance.OnGameStart += StartCallback;
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
            SlotManager.Instance.OnGameStart -= StartCallback;
            if (!initialized) Initialize();
            // Waypoint
            GoToWaypoint();
        }

        // Update is called once per frame
        void Update()
        {
            if (!SlotManager.Instance.gameOn) return;

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

    }
}
