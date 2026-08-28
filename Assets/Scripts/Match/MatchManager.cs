using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============================= WAVE ENTRY ==
    [Serializable]
    public class WaveEntry
    {
        public Unit unitToSpawn;
        [Tooltip("Количество юнитов данного типа в волне")]
        public int count = 1;
    }

    // Волна 2.0: политика при пропуске волны (не влезла в лидерство) — судьба разовых пометок.
    public enum WaveOverflowPolicy
    {
        CarryOneShots,     // перенести разовые (резерв держится) на следующую волну
        CancelAndRefund,   // отменить разовые с разморозкой (вернуть золото)
    }

    // ============================= TEAM CONFIG ==
    // Полный конфиг одной команды. Раньше был отдельным компонентом UnitWaveSpawner —
    // свёрнут сюда, чтобы уменьшить число объектов/точек входа. Всё настраивается в Inspector.
    [Serializable]
    public class TeamWaveConfig
    {
        [Tooltip("Индекс игрока-владельца юнитов этой команды (A обычно 0, B обычно 1).")]
        public int ownerPlayer = 0;


        [Header("Спавн")]
        [Tooltip("Сетка слотов ЗАМКА этой команды (DefenceGrid): волна спавнится СРАЗУ в слотах строя " +
                 "лицом к врагу. Если пусто — фоллбэк: спавн в точке спавна (spawnPoint).")]
        public DefenceGrid spawnGrid;
        [Tooltip("Фоллбэк-точка спавна (действует только при пустом spawnGrid).")]
        public Transform spawnPoint;
        [Tooltip("Фоллбэк: задержка между спавном юнитов внутри волны, сек. 0 — одновременно (риск NavMesh overlap). Действует только при пустом spawnGrid.")]
        public float spawnDelay = 0f;

        [HideInInspector] public TechTier[] techTiers;                // дерево технологий (тиры); резолв из FactionConfig ГЛУБОКИМ клоном (MatchManager.Factions.CloneTechTiers)
        [HideInInspector] public Unit[] mainBuildingShapesByLevel;    // облик замка по уровням ГЗ (резолв из FactionConfig); смена — MatchManager.MainBuildingShape.cs

        [Header("Главное здание")]
        [Tooltip("Главное здание (замок) команды: носитель HP/брони по уровню ГЗ (MatchManager.MainBuildingStats) и цель " +
                 "смены облика по уровню (MatchManager.MainBuildingShape). Владелец — ownerPlayer; на нём же условие победы " +
                 "(specificUnitsDead) и визуал. Способности сюда НЕ прописываются — кастер вынесен в отдельный объект (abilityCaster).")]
        public Unit mainBuilding;

        [Header("Способности")]
        [Tooltip("Объект-кастер центральных умений команды: отдельный НЕВИДИМЫЙ и НЕВЫБИРАЕМЫЙ юнит сцены " +
                 "(развязан от замка — замок можно менять/апгрейдить). Владелец = ownerPlayer. Способности — из " +
                 "FactionConfig расы (centralAbilities); MatchManager пропишет их в abilities[] на старте (AssignCasterAbilities). " +
                 "Мана по уровню ГЗ — MatchManager.MainBuildingStats. SCEditor не нужен.")]
        public Unit abilityCaster;

        [HideInInspector] public List<Ability> centralAbilities = new List<Ability>();

        // Runtime-копия префаба героя расы (резолвится в ApplyFaction из FactionConfig.heroPrefab). Скрыто из Inspector.
        [HideInInspector] public Unit heroPrefab;

        // Runtime-копия теха-гейта призыва героя (резолвится в ApplyFaction из FactionConfig.heroUnlockTech). Скрыто из Inspector.
        [HideInInspector] public Technology heroUnlockTech;

        // Runtime-копия модификаторов стоимости найма (B28; резолв из FactionConfig.costModifiers). Скрыто из Inspector.
        [HideInInspector] public CostModifierRule[] costModifiers;

        // Runtime-копии ресурса душ Нежити (N1; резолв из FactionConfig в ApplyFaction). Скрыто из Inspector.
        [HideInInspector] public Resource soulsResource;               // null у не-Нежити → система душ неактивна
        [HideInInspector] public float[] soulsPerMinuteByMbLevel;      // генерация душ/мин по уровню ГЗ
        [HideInInspector] public int[] soulsPerTier;                   // души за убийство по тиру жертвы

        // ── Волна 2.0 (runtime; резолв из FactionConfig в ApplyFaction) ──
        [HideInInspector] public WaveUnitEntry[] waveUnits;            // единый список юнитов волны (роль + count); резолв из FactionConfig
        [HideInInspector] public int baseWaveIncome;                   // стартовый базовый доход волны (из FactionConfig)

        // Пометки состава и окна (сервер, runtime — не сериализуются). autoSummon: типы на автопризыв (каждую волну);
        // oneShot: unitTypeID → зарезервированное золото (цена×count) разового призыва на ближайшую волну.
        [NonSerialized] public HashSet<int> autoSummon = new HashSet<int>();
        [NonSerialized] public Dictionary<int, int> oneShot = new Dictionary<int, int>();
        [NonSerialized] public int baseIncome;                         // текущий базовый доход (стартует из baseWaveIncome)
        [NonSerialized] public bool compositionLocked;                 // окно лока t−5..t0 — пометки отклоняются
        [NonSerialized] public bool waveSkipped;                       // вердикт t−5: волна пропускается целиком
        [NonSerialized] public int heroWavesToSkip;                    // сколько волн герой ещё пропускает (перерождение)
    }

    // ============================= POINT TOWER CONFIG ==
    // Конфиг одной перестраиваемой точки. Префаб башни берётся из FactionConfig расы владельца по pointKey —
    // у игроков разные точки (разные POI), но один и тот же FactionConfig знает все типы.
    // Добавлять: Centre, Defence1, Defence2. Замки НЕ добавлять (у замка башни нет).
    // Тип перестраиваемой точки. Enum вместо строки — выбор из списка в Inspector, без опечаток.
    // None = «не выбрано»: точка не резолвится (варнинг в ValidateSetup, башня не строится).
    public enum PointKey { None = 0, Centre, Defence1, Defence2 }

    [Serializable]
    public class PointTowerConfig
    {
        [Tooltip("Точка линии с башней (Centre, Defence1, Defence2). Замки НЕ добавлять.")]
        public PointOfInterest point;
        [Tooltip("Тип точки — выбор из списка (None = не выбрано, башня строиться не будет). " +
                 "Определяет поле FactionConfig (centreTower/defence1Tower/defence2Tower) и сопоставление с TowerSwap.pointKey.")]
        public PointKey pointKey = PointKey.None;
        [Tooltip("Нейтральная стартовая башня для НЕЙТРАЛЬНОЙ точки (обычно центр). Спавнится на старте " +
                 "с владельцем Neutral Active (штатный нейтрал ассета: враждебен всем — башню можно атаковать " +
                 "и захватить). Пусто — нейтральная точка стартует без башни.")]
        public Unit neutralTower;
        [Tooltip("Отстраивать новую башню после захвата. ВКЛ — только для центральной точки.")]
        public bool rebuildOnCapture = false;
        [Tooltip("Задержка спавна башни после захвата/старта, сек. > 0 чтобы тело ушло.")]
        public float rebuildDelay = 1f;
    }

    // ============================= MANAGER ==
    /// <summary>
    /// Единый серверный оркестратор матча (MVP: 2 команды A/B, одна линия). Берёт на себя
    /// максимум: спавн волн обеих команд, темп, Lane-таргетинг, отстройку башен точек. Спавнеры как
    /// отдельные компоненты упразднены — их конфиг свёрнут в поля teamA/teamB (Inspector).
    ///
    /// Победа НЕ дублируется — штатная GameManager.specificUnitsDead (NetID баз + winningTeam).
    /// Перестраиваемые башни точек тоже управляются отсюда: подписка на OnDie стартовых башен,
    /// при гибели точка переходит сопернику и через задержку отстраивается новая (модель «смерть + отстройка»).
    /// </summary>
    public partial class MatchManager : MonoBehaviour
    {
        [Header("Команды")]
        [Tooltip("Команда A (обычно игрок 0).")]
        [SerializeField] TeamWaveConfig teamA;
        [Tooltip("Команда B (обычно игрок 1).")]
        [SerializeField] TeamWaveConfig teamB;

        [Header("Линия")]
        [Tooltip("Линия точек интереса — единственный источник цели волн/команд (динамически). Обязательна: без неё волна пропускается.")]
        [SerializeField] Lane lane;

        [Header("Точки с отстройкой башен")]
        [Tooltip("Перестраиваемые точки линии (Centre/Defence1/Defence2). Для каждой — POI, pointKey, " +
                 "rebuildOnCapture (только у центра) и задержка. Префабы башен — в FactionConfig расы " +
                 "(centreTower/defence1Tower/defence2Tower). Замки сюда НЕ добавлять: они не перестраиваются. " +
                 "Начальный владелец каждой точки задаётся в самой PointOfInterest (teamAffiliation). " +
                 "Стартовые башни спавнятся автоматически (SpawnInitialTowers) — pre-placed башни в сцене не нужны.")]
        [SerializeField] PointTowerConfig[] rebuildablePoints;

        [Header("Темп волн")]
        [Tooltip("Задержка до первой волны после старта матча, сек.")]
        [SerializeField] float firstWaveDelay = 5f;
        [Tooltip("Интервал между волнами, сек. Единый для обеих команд.")]
        [SerializeField] float waveInterval = 30f;

        [Header("Экономика волн")]
        [Tooltip("Ассет ресурса «Золото» (standard). Списывается за всю волну перед спавном; " +
                 "награда за убийство врага начисляется штатно через Unit.resourceReward.")]
        [SerializeField] Resource goldResource;
        [Tooltip("Ассет ресурса «Лидерство» (limited). Кап карты = лимит этого ресурса в GameResources " +
                 "(пользователь задаёт его как пул волны × 3). Живые юниты занимают лидерство сами через Unit.resourceCost.")]
        [SerializeField] Resource leadershipResource;

        [Header("Волна 2.0 — окна / политика / герой")]
        [Tooltip("За сколько секунд до волны показать предупреждение о превышении лидерства. Должно быть > lockSeconds и < waveInterval.")]
        [SerializeField] float warnSeconds = 15f;
        [Tooltip("За сколько секунд до волны заблокировать состав (лок) и вынести вердикт пропуска. Должно быть < warnSeconds.")]
        [SerializeField] float lockSeconds = 5f;
        [Tooltip("При пропуске волны: перенести разовые пометки на следующую волну или отменить с разморозкой золота.")]
        [SerializeField] WaveOverflowPolicy overflowPolicy = WaveOverflowPolicy.CarryOneShots;
        [Tooltip("Сколько волн герой пропускает после смерти (2 → выходит с 3-й волны).")]
        [SerializeField] int heroRespawnWavesSkipped = 2;

        /// <summary>Ассет ресурса «Золото» (для UI: читать цену из prefab.resourceCost). Единый источник ссылки.</summary>
        public Resource GoldResource => goldResource;
        /// <summary>Ассет ресурса «Лидерство» (для UI: читать цену из prefab.resourceCost). Единый источник ссылки.</summary>
        public Resource LeadershipResource => leadershipResource;

        /// <summary>Единственный экземпляр. Единый источник Lane-таргетинга и спавна для волн и UI-команд.</summary>
        public static MatchManager Instance { get; private set; }

        /// <summary>Вызывается после спавна каждого юнита волны. Параметры: индекс команды (0=A, 1=B), юнит.</summary>
        public event Action<int, Unit> OnUnitSpawned;

        /// <summary>Пометки состава волны команды изменились (параметр — индекс команды 0=A, 1=B). Для перерисовки UI.</summary>
        public event Action<int> OnWaveMarksChanged;

        /// <summary>За warnSeconds до волны: состав не влезает в лидерство (параметр — индекс команды). Для UI-предупреждения.</summary>
        public event Action<int> OnWaveOverflowWarning;

        // ======================== СПИСОК ЮНИТОВ ========================
        // Живые боевые юниты (UnitType.Unit) каждой команды. Обновляются при спавне волны и OnDie.
        // На клиенте не обновляются (спавн серверный) — GetGroupUnits на клиенте использует FindObjectsByType.
        List<Unit>[] teamUnits;

        // ======================== LIFECYCLE ========================

        void Awake()
        {
            // Доступен и на клиенте (UI-команды читают цели/состав через него); сами волны — только сервер.
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            teamUnits = new List<Unit>[2] { new List<Unit>(), new List<Unit>() };

            // Режим команды по каждому ряду (см. MatchManager.CommandGroups.cs). Стартовый режим обеих команд —
            // Защита (целевая модель 2026-08-21: один из двух режимов выбран всегда, на старте — Защита).
            // Юнитов на старте нет, саму команду рассылать не нужно — достаточно состояния: первая волна
            // унаследует режим ряда при спавне (MatchManager.Waves.cs, переотдача по рядам).
            int groupCount = CommandGroupCount;
            currentCommand = new BottomTableAction[2][];
            for (int t = 0; t < 2; t++)
            {
                currentCommand[t] = new BottomTableAction[groupCount];
                for (int g = 0; g < groupCount; g++) currentCommand[t][g] = BottomTableAction.Defence;
            }

            // Раса стороны: ДО AssignCasterAbilities заполняем runtime-копию TeamWaveConfig контентом
            // фракции (по ссылке GameManager.factionData[playerFaction].config). Детерминированно — см. ApplyFactions.
            ApplyFactions();

            // Способности центральной таблицы: прописываем список centralAbilities в abilities[] кастера
            // (отдельный невидимый объект сцены) ДО его инициализации (InitializeAbilities по OnGameStart, позже Awake).
            // На всех пирах — конфиг детерминирован, индексы каста совпадут (правило 6 не применяется, это конфиг).
            AssignCasterAbilities(teamA);
            AssignCasterAbilities(teamB);

            // Апгрейды контента: подписка на триггеры + начальный пересчёт — по OnGameStart. См. MatchManager.ContentUnlock.cs.
            if (SlotManager.Instance != null) SlotManager.Instance.OnGameStart += WireContentTriggers;

            MainBuildingStatsWire(); // Статы ГЗ по уровню: подписка на OnMainBuildingLevelChanged. Отписка — в OnDestroy.
            MainBuildingShapeWire(); // Облик ГЗ по уровню: подписка на OnMainBuildingLevelChanged. Отписка — в OnDestroy.
            // Волна 2.0: WaveDefaults (состав по уровню ГЗ) упразднён — состав = базовые + пометки.

            // Могилки: подписка на спавн юнитов (хук смерти помеченных). Отписка — в OnDestroy (GravesUnwire). См. MatchManager.Graves.cs.
            GravesWire();

            // Хаб смертей: подписка на спавн (per-unit OnDie → диспетч B3/B4). Отписка — в OnDestroy (DeathEventsUnwire). См. MatchManager.DeathEvents.cs.
            DeathEventsWire();

            // Ресурс «Души» (Нежить): подписка на хаб смертей для начисления за убийства. Отписка — в OnDestroy (SoulsUnwire). См. MatchManager.Souls.cs.
            SoulsWire();
        }

        // Прописывает способности центральной таблицы в abilities[] кастера команды (если оба заданы).
        // Набор ПОЛНЫЙ: стартовые умения расы + все, что открываются узлами дерева технологий — кастер должен
        // знать их с самого начала (каст идёт по Ability.id через abilities[] кастера, см. CastCentralAbilityById).
        // Что из них показано игроку — решает панель (MatchManager.ContentUnlock: куплен узел или нет).
        // Кулдауны/уровни построит штатная Unit.InitializeAbilities при инициализации кастера (правило 2).
        void AssignCasterAbilities(TeamWaveConfig cfg)
        {
            if (cfg == null || cfg.abilityCaster == null) return;
            cfg.abilityCaster.abilities = CollectAllCentralAbilities(cfg).ToArray();
        }

        void OnDestroy()
        {
            foreach (Unit tower in hookedTowers.Keys)
                if (tower != null) tower.OnDie -= HandleTowerDie;
            hookedTowers.Clear();
            if (SlotManager.Instance != null) SlotManager.Instance.OnGameStart -= WireContentTriggers;
            UnwireContentTriggers();
            MainBuildingStatsUnwire(); // Статы ГЗ по уровню: отписка (см. MainBuildingStatsWire / MatchManager.MainBuildingStats.cs).
            MainBuildingShapeUnwire(); // Облик ГЗ по уровню: отписка (см. MainBuildingShapeWire / MatchManager.MainBuildingShape.cs).
            GravesUnwire(); // Могилки: отписка (см. GravesWire / MatchManager.Graves.cs).
            DeathEventsUnwire(); // Хаб смертей: отписка (см. DeathEventsWire / MatchManager.DeathEvents.cs).
            SoulsUnwire(); // Ресурс «Души»: отписка от хаба + очистка модификаторов (см. SoulsWire / MatchManager.Souls.cs).
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (NetworkConnectionHandler.isClient) return;

            ValidateSetup();

            // Начальные башни спавнятся через SpawnInitialTowers() в WireContentTriggers (по OnGameStart),
            // чтобы effectiveTowerSwaps был уже вычислен. Pre-placed башни в сцене не нужны.

            StartCoroutine(WaveLoop());
            // Стартовые режимы рядов (Защита) — разово клиентам после gameOn (см. MatchManager.Commands.cs).
            StartCoroutine(BroadcastStartCommandsLoop());
            // Периодические источники опыта ГЗ (серверо-авторитетно; см. MatchManager.Experience.cs).
            StartCoroutine(CentreHoldExperienceLoop());
            StartCoroutine(AttackStanceExperienceLoop());
            // «Часы» опыта: раз в секунду досылают клиентам изменившееся значение (см. MatchManager.Experience.cs).
            StartCoroutine(ExperienceSyncLoop());
            // Пассивный доход золота (серверо-авторитетно; см. MatchManager.PassiveIncome.cs).
            StartCoroutine(PassiveIncomeLoop());
            // Генерация душ Нежити по уровню ГЗ (серверо-авторитетно; см. MatchManager.Souls.cs). Для не-Нежити тик пустой.
            StartCoroutine(SoulsGenerationLoop());
            // Рост территории Скверны Нежити (серверо-авторитетно; см. MatchManager.SkvernaTerritory.cs). Для не-Нежити тик пустой.
            StartCoroutine(SkvernaGrowthLoop());   // // N4
        }

        // ======================== ПРОВЕРКИ ========================

        void ValidateSetup()
        {
            if (teamA == null || teamA.spawnPoint == null || teamB == null || teamB.spawnPoint == null)
                Debug.LogWarning("[MatchManager] Не настроены spawnPoint обеих команд.");

            if (lane == null)
                Debug.LogWarning("[MatchManager] Lane не назначена — волны/команды без цели (пропускаются).");

            if (goldResource == null || leadershipResource == null)
                Debug.LogWarning("[MatchManager] Не назначены ссылки на ресурсы экономики волн " +
                                 "(goldResource/leadershipResource) — проверки капа и списание золота будут пропущены.");

            if (rebuildablePoints != null)
            {
                foreach (PointTowerConfig cfg in rebuildablePoints)
                {
                    if (cfg == null || cfg.point == null)
                    {
                        Debug.LogWarning("[MatchManager] В rebuildablePoints есть пустой элемент или не назначена точка.");
                        continue;
                    }
                    if (cfg.point.type == PointOfInterest.PointType.Castle)
                        Debug.LogWarning($"[MatchManager] Точка {cfg.point.name} — замок (Castle). Замки не перестраиваются, уберите её из rebuildablePoints.");
                    if (cfg.pointKey == PointKey.None)
                        Debug.LogWarning($"[MatchManager] Точка {cfg.point.name}: pointKey не задан — башня строиться не будет (резолв префаба по ключу невозможен).");
                }
            }

            // Победа — штатная (GameManager.specificUnitsDead). Менеджер её не реализует.
            if (GameManager.Instance != null &&
                (GameManager.Instance.specificUnitsDead == null || GameManager.Instance.specificUnitsDead.Length == 0))
                Debug.LogWarning("[MatchManager] GameManager.specificUnitsDead не настроен — " +
                                 "победа по уничтожению базы не сработает. Задайте NetID баз и winningTeam в Inspector у GameManager.");
        }
    }
}
