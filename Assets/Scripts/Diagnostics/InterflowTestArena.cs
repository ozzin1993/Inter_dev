using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
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
    ///   2) поднимает хост, если сеть не слушает (галка в Inspector) — без запущенного хоста нет объекта
    ///      NetworkDataSync, и весь канал статусов молчит: не показываются надписи боя, величина щита
    ///      и значки состояний;
    ///   3) поднимает событие старта матча при прямом запуске — SceneHandler ставит gameOn напрямую
    ///      и OnGameStart НЕ поднимает, из-за чего не считается открытый контент, не выставляется
    ///      стартовый уровень главного здания, не спавнятся начальные башни точек и не цепляется хук могилок;
    ///   4) включает режим отладки и паузу волн (значения — в Inspector);
    ///   5) призывает стартовый набор, если он задан.
    ///
    /// Всё, что меняет мир, идёт через MatchManager (правило 5) и только на сервере (правило 6).
    /// </summary>
    /// <remarks>
    /// Порядок выполнения задан явно: обработчик сети назначает свой колбэк одобрения подключения
    /// в Start (NetworkConnectionHandler.Start), а подъём хоста идёт из Start этого компонента.
    /// Порядок Start между объектами сцены Unity не определяет, поэтому компонент отодвинут в конец
    /// атрибутом порядка выполнения (документация Unity, DefaultExecutionOrder) — иначе хост мог
    /// подняться раньше назначения колбэка, и слот игрока не занялся бы.
    /// </remarks>
    [DefaultExecutionOrder(100)]
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

        [Header("Сеть")]
        [Tooltip("Поднимать хост при прямом запуске сцены. Без запущенного хоста молчит весь канал статусов: " +
                 "не показываются надписи боя, величина щита на полоске здоровья и значки состояний.")]
        [SerializeField] bool startHost = true;
        [Tooltip("Имя хоста в слоте игрока, латиницей: имя едет в одобрение подключения кодировкой ASCII. " +
                 "Пусто — берётся текущее имя игрока сцены.")]
        [SerializeField] string hostName = "";
        [Tooltip("Сколько портов подряд перебрать, если штатный занят. Порт прошлого запуска редактор " +
                 "освобождает не сразу, и повторный Play иначе молча остаётся без сети.")]
        [Range(1, 50)]
        [SerializeField] int portSearchRange = 20;

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

            // 2) Хост. Признак прямого запуска вычисляем ДО подъёма: после него сеть уже слушает.
            //    Поднимаем раньше события старта, чтобы стартовые рассылки контента шли по готовому каналу.
            bool directRun = NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
            if (directRun && startHost) StartHostHere(sm);

            // 3) Событие старта матча. При прямом запуске сцены его никто не поднимает (сетевой путь идёт через
            //    SceneHandler.StartTheGame), поэтому без него не отрабатывает MatchManager.WireContentTriggers.
            //    Условие — признак ПРЯМОГО запуска, а не текущее состояние сети: после подъёма хоста сеть слушает,
            //    но штатный путь события всё равно не пройдёт.
            if (directRun) sm.OnGameStart?.Invoke();

            // 4) Режим.
            sm.debugMode = enableDebugMode;
            mm.testWavesPaused = pauseWaves;

            Debug.Log($"[Полигон] Готов. Расы: A = {NameOf(factionA)}, B = {NameOf(factionB)}. " +
                      (pauseWaves ? "Волны на паузе." : "Волны идут."));

            // 5) Стартовый набор.
            SpawnStartup(mm);
        }

        /// <summary>
        /// Поднять хост штатным путём NetworkConnectionHandler (правило 1). Ветка «hosting without a lobby»:
        /// состояние матча в сцене полигона уже Started, слоты подняты SceneHandler, префаб сетевого
        /// обработчика назначен — отдельной сборки сцене не требуется.
        ///
        /// Зачем: без запущенного хоста нет объекта NetworkDataSync, а весь канал статусов стоит за гейтом
        /// ServerCanSend. Он отсекает сообщение ДО локального подъёма факта у себя, поэтому на полигоне молчали
        /// надписи боя (BattleFactReason), величина щита на полоске здоровья и значки состояний.
        /// </summary>
        void StartHostHere(SlotManager sm)
        {
            NetworkConnectionHandler handler = NetworkConnectionHandler.Instance;
            if (handler == null)
            {
                Debug.LogWarning("[Полигон] На сцене нет NetworkConnectionHandler — хост не поднят: " +
                                 "надписи боя, величина щита и значки состояний работать не будут.");
                return;
            }

            // [Interflow fix 2026-09-09 arena-port] Порт освобождаем ДО подъёма. Транспорт садится на
            // фиксированный порт (по умолчанию 7777), а сокет предыдущего запуска редактор держит связанным
            // ещё какое-то время после выхода из Play. Штатная ветка «без лобби» отказ привязки не возвращает,
            // поэтому повторный Play молча оставался вообще без сети: NetworkDataSync не создавался и канал
            // статусов молчал. Замер 09.09.2026: без этой правки открытие технологий стороне A давало
            // 0 из 42, с ней — 42 из 42. Симптом выглядел как поломка технологий, хотя ломался подъём хоста.
            EnsureFreePort();

            // Имя уходит в слот игрока: StartHost в этой ветке заново собирает слоты (InitializeSlotData),
            // и слот занимает одобрение подключения именем, которое мы передали.
            string playerName = string.IsNullOrWhiteSpace(hostName) ? sm.currentName : hostName;
            handler.StartHost(playerName);

            // Отказ подъёма (занятый порт, живая сеть) ветка «без лобби» возвращает молча — судим по факту.
            bool listening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (listening) Debug.Log($"[Полигон] Хост поднят, имя игрока «{playerName}», порт {CurrentPort()}.");
            else Debug.LogWarning("[Полигон] Хост НЕ поднялся (порт занят или транспорт не готов) — " +
                                  "надписи боя, величина щита и значки состояний работать не будут.");
        }

        /// <summary>
        /// Отодвинуть транспорт на свободный порт, если штатный занят. Полигон — одиночный хост без внешних
        /// подключений, номер порта для него безразличен, поэтому перебираем соседние вместо того, чтобы
        /// падать. Если свободного рядом нет, оставляем как было: пусть отказ будет виден штатным путём.
        /// </summary>
        void EnsureFreePort()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm.NetworkConfig == null) return;

            UnityTransport transport = nm.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport == null) return;   // другой транспорт — не наше дело

            // Проверяем ровно тот адрес, на который сядет транспорт: связать 0.0.0.0 удаётся
            // и тогда, когда 127.0.0.1 уже занят, и проверка молча пропускала занятый порт.
            string listen = transport.ConnectionData.ServerListenAddress;
            ushort port = transport.ConnectionData.Port;
            if (IsPortFree(listen, port)) return;

            for (int step = 1; step <= portSearchRange; step++)
            {
                int candidate = port + step;
                if (candidate > ushort.MaxValue) break;
                if (!IsPortFree(listen, (ushort)candidate)) continue;

                transport.SetConnectionData(transport.ConnectionData.Address, (ushort)candidate,
                                            transport.ConnectionData.ServerListenAddress);
                Debug.LogWarning($"[Полигон] Порт {port} занят (сокет прошлого запуска) — хост поднимается на {candidate}.");
                return;
            }

            Debug.LogWarning($"[Полигон] Свободного порта в диапазоне {port}–{port + portSearchRange} нет — " +
                             "хост, скорее всего, не поднимется.");
        }

        /// <summary>
        /// Свободен ли порт НА ТОМ ЖЕ адресе, где его займёт транспорт. Адрес обязателен:
        /// на Windows связывание 0.0.0.0:порт проходит и при занятом 127.0.0.1:порт, поэтому
        /// проверка «по любому адресу» считала занятый порт свободным и хост всё равно падал.
        /// </summary>
        static bool IsPortFree(string address, ushort port)
        {
            IPAddress ip;
            if (string.IsNullOrEmpty(address) || !IPAddress.TryParse(address, out ip)) ip = IPAddress.Any;

            try
            {
                using (UdpClient probe = new UdpClient(new IPEndPoint(ip, port))) { }
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        /// <summary>Порт, на котором сейчас настроен транспорт — только для строки лога.</summary>
        static string CurrentPort()
        {
            NetworkManager nm = NetworkManager.Singleton;
            UnityTransport transport = nm != null && nm.NetworkConfig != null
                                     ? nm.NetworkConfig.NetworkTransport as UnityTransport : null;
            return transport != null ? transport.ConnectionData.Port.ToString() : "неизвестен";
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
