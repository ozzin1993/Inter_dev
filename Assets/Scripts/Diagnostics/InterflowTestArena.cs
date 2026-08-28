using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow 2026-08-24] Загрузчик сцены тестового полигона (TestArena): готовит сцену к прямому запуску
    /// (Play прямо на сцене, без лобби и без сети) и отдаёт панели общие данные — точки призыва, центр и каталог
    /// юнитов. Сам ничего не рисует: панель — отдельный клиентский компонент InterflowTestArenaUI.
    ///
    /// Что делает при старте:
    ///   1) назначает сторонам расы ЯВНЫМИ ассетами (штатный резолв при прямом запуске дал бы обеим одну расу);
    ///   2) поднимает событие старта матча, если сеть не слушает — при прямом запуске SceneHandler ставит
    ///      gameOn напрямую и OnGameStart НЕ поднимает, из-за чего не считается открытый контент, не выставляется
    ///      стартовый уровень главного здания, не спавнятся начальные башни точек и не цепляется хук могилок;
    ///   3) включает режим отладки и паузу волн (значения — в Inspector);
    ///   4) призывает стартовый набор, если он задан.
    ///
    /// Всё, что меняет мир, идёт через MatchManager (правило 5) и только на сервере (правило 6).
    /// </summary>
    public class InterflowTestArena : MonoBehaviour
    {
        /// <summary>Стартовый набор: кого и сколько призвать сразу при запуске сцены.</summary>
        [Serializable]
        public class StartupUnit
        {
            [Tooltip("Префаб юнита. Пусто — запись пропускается.")]
            public Unit prefab;
            [Tooltip("Сколько копий призвать.")]
            public int count = 1;
            [Tooltip("За какую сторону: 0 — A, 1 — B.")]
            [Range(0, 1)] public int side = 0;
        }

        [Header("Расы сторон")]
        [Tooltip("Раса стороны A. Назначается явно, минуя разыгрывание слотов: при прямом запуске сцены " +
                 "штатный резолв выдал бы обеим сторонам одну и ту же расу.")]
        [SerializeField] FactionConfig factionA;
        [Tooltip("Раса стороны B.")]
        [SerializeField] FactionConfig factionB;

        [Header("Точки")]
        [Tooltip("Точка призыва стороны A. Требование к месту: ровная площадка с коллайдером земли, внутри границ игровой зоны.")]
        [SerializeField] Transform spawnA;
        [Tooltip("Точка призыва стороны B.")]
        [SerializeField] Transform spawnB;
        [Tooltip("Центр полигона (необязательно). Пусто — берётся середина между точками призыва сторон.")]
        [SerializeField] Transform centerPoint;

        [Header("Режим")]
        [Tooltip("Включить режим отладки: управление доступно за обе стороны, проверки владения обходятся.")]
        [SerializeField] bool enableDebugMode = true;
        [Tooltip("Поставить волны на паузу сразу при запуске: на полигоне волны мешают замеру.")]
        [SerializeField] bool pauseWaves = true;
        [Tooltip("Разброс призыва по умолчанию: радиус, в котором ищется свободное место под каждого юнита.")]
        [SerializeField] float defaultSpread = 6f;

        [Header("Стартовый набор")]
        [Tooltip("Кого призвать сразу при запуске сцены. Пусто — сцена открывается без юнитов.")]
        [SerializeField] StartupUnit[] startupUnits = new StartupUnit[0];

        /// <summary>Единственный экземпляр на сцене — точка входа для панели.</summary>
        public static InterflowTestArena Instance { get; private set; }

        List<Unit> catalogAll;   // кэш каталога префабов (Resources/UnitPrefabs)

        /// <summary>Разброс призыва по умолчанию (панель берёт его как стартовое значение поля).</summary>
        public float DefaultSpread => defaultSpread;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            SlotManager sm = SlotManager.Instance;
            MatchManager mm = MatchManager.Instance;
            if (sm == null || mm == null)
            {
                Debug.LogWarning("[Полигон] Нет SlotManager или MatchManager на сцене — полигон не запущен.");
                return;
            }

            // 1) Расы сторон явными ассетами (ДО подъёма события старта: открытый контент считается по дереву расы).
            ApplySide(mm, 0, factionA);
            ApplySide(mm, 1, factionB);

            // 2) Событие старта матча. При прямом запуске сцены его никто не поднимает (сетевой путь идёт через
            //    SceneHandler.StartTheGame), поэтому без него не отрабатывает MatchManager.WireContentTriggers.
            //    Поднимаем только когда сеть не слушает — в сетевом матче это сделает штатный путь.
            bool networkListening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (!networkListening) sm.OnGameStart?.Invoke();

            // 3) Режим.
            sm.debugMode = enableDebugMode;
            mm.testWavesPaused = pauseWaves;

            Debug.Log($"[Полигон] Готов. Расы: A = {NameOf(factionA)}, B = {NameOf(factionB)}. " +
                      (pauseWaves ? "Волны на паузе." : "Волны идут."));

            // 4) Стартовый набор.
            SpawnStartup(mm);
        }

        // Назначить расу стороне и перестроить умения кастера, если он уже проинициализировался.
        void ApplySide(MatchManager mm, int teamIndex, FactionConfig faction)
        {
            if (faction == null)
            {
                Debug.LogWarning($"[Полигон] Не задана раса стороны {MatchManager.TestSideName(teamIndex)} — сторона останется на расе по умолчанию.");
                return;
            }

            mm.TestApplyFaction(teamIndex, faction);

            // Кастер центральных умений при прямом запуске успевает проинициализироваться в своём Start раньше
            // нашего (порядок Start между объектами сцены не определён). Тогда массивы уровней и замков собраны
            // по СТАРОМУ списку умений — пересобираем их под новый список расы штатным методом ассета.
            Unit caster = CasterOf(mm, teamIndex);
            if (caster != null && caster.initialized)
            {
                caster.InitializeAbilities();
                caster.AllAbilityLockLevelsCalculate();
            }
        }

        static Unit CasterOf(MatchManager mm, int teamIndex)
        {
            TeamWaveConfig cfg = mm.Team(teamIndex);
            return cfg != null ? cfg.abilityCaster : null;
        }

        static string NameOf(FactionConfig faction) => faction != null ? faction.name : "не задана";

        void SpawnStartup(MatchManager mm)
        {
            if (startupUnits == null) return;
            for (int i = 0; i < startupUnits.Length; i++)
            {
                StartupUnit entry = startupUnits[i];
                if (entry == null || entry.prefab == null || entry.count <= 0) continue;
                mm.TestSpawnMany(entry.prefab, entry.count, SpawnPositionOf(entry.side), entry.side,
                                 defaultSpread, TestArenaOrder.AsInGame);
            }
        }

        // ======================== ДАННЫЕ ДЛЯ ПАНЕЛИ ========================

        /// <summary>Точка призыва стороны: заданный маркер, иначе точка спавна волн этой стороны.</summary>
        public Vector3 SpawnPositionOf(int teamIndex)
        {
            Transform marker = teamIndex == 0 ? spawnA : spawnB;
            if (marker != null) return marker.position;

            MatchManager mm = MatchManager.Instance;
            TeamWaveConfig cfg = mm != null ? mm.Team(teamIndex) : null;
            if (cfg != null && cfg.spawnPoint != null) return cfg.spawnPoint.position;

            Debug.LogWarning($"[Полигон] У стороны {MatchManager.TestSideName(teamIndex)} нет ни маркера призыва, ни точки спавна волн.");
            return Vector3.zero;
        }

        /// <summary>Центр полигона: заданный маркер, иначе середина между точками призыва сторон.</summary>
        public Vector3 CenterPosition =>
            centerPoint != null ? centerPoint.position : (SpawnPositionOf(0) + SpawnPositionOf(1)) * 0.5f;

        /// <summary>
        /// Каталог префабов из Resources/UnitPrefabs — весь, независимо от расы. includeBuildings = false
        /// оставляет только боевых юнитов (UnitType.Unit). Список кэшируется при первом обращении.
        /// </summary>
        public List<Unit> Catalog(bool includeBuildings)
        {
            if (catalogAll == null)
            {
                Unit[] loaded = Resources.LoadAll<Unit>("UnitPrefabs");
                catalogAll = new List<Unit>(loaded);
                catalogAll.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            }

            if (includeBuildings) return catalogAll;

            List<Unit> onlyUnits = new List<Unit>();
            for (int i = 0; i < catalogAll.Count; i++)
                if (catalogAll[i] != null && catalogAll[i].unitType == UnitType.Unit) onlyUnits.Add(catalogAll[i]);
            return onlyUnits;
        }
    }
}
