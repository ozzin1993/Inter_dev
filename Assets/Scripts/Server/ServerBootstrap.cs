using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>
    /// Запуск проекта в режиме ВЫДЕЛЕННОГО СЕРВЕРА (NGO StartServer) без локального игрока.
    /// Оба реальных игрока — клиенты; сервер только хостит и не занимает игровой слот.
    ///
    /// Не трогает код ассета (правило 1): использует только публичные API —
    /// `NetworkConnectionHandler.networkHandler` (префаб), `NetworkManager` (NGO),
    /// `SlotManager`, `SceneHandler`. Поток Host/Join из меню остаётся без изменений.
    /// </summary>
    public partial class ServerBootstrap : MonoBehaviour
    {
        [Header("Автозапуск сервера")]
        [Tooltip("Автоматически стартовать сервер при запуске настоящим headless-сервером (выделенная сборка).")]
        [SerializeField] private bool autoStartInBatchmode = true;
        [Tooltip("Аргумент командной строки, который тоже включает запуск сервера (помимо headless).")]
        [SerializeField] private string serverArg = "-server";

        [Header("Сеть (порт прослушивания)")]
        [Tooltip("Порт по умолчанию, если аргумент порта в запуске не передан. " +
                 "Совпадает с текущим портом UnityTransport у ProjectManager (7777).")]
        [SerializeField] private ushort defaultPort = 7777;
        [Tooltip("Аргумент командной строки с портом прослушивания: <арг> N (например, -port 7788). " +
                 "Передаётся allocator'ом при аллокации процесса.")]
        [SerializeField] private string portArg = "-port";
        [Tooltip("Адрес прослушивания сервера. 0.0.0.0 — слушать все сетевые интерфейсы " +
                 "(нужно, чтобы клиенты подключались извне / с VPS).")]
        [SerializeField] private string listenAddress = "0.0.0.0";

        [Tooltip("Ограничение кадров сервера (кадров/сек). Без лимита headless-цикл крутится на максимуме и жжёт ядро CPU впустую. 30 согласуется с сетевым тиком NGO.")]
        [SerializeField] private int serverFrameRate = 30;

        [Header("Кнопка в меню")]
        [Tooltip("Добавлять кнопку запуска сервера в главное меню (для ручного запуска/тестов). " +
                 "В headless не нужна — там сервер стартует сам.")]
        [SerializeField] private bool injectMenuButton = true;
        [Tooltip("Текст кнопки запуска сервера в меню.")]
        [SerializeField] private string menuButtonText = "SERVER";

        [Header("Автостарт матча")]
        [Tooltip("Сколько игровых слотов (SlotType.Player) заполнить клиентами для автостарта матча. " +
                 "Должно совпадать с правилом очереди Matchmaker (сейчас 2).")]
        [SerializeField] private int requiredPlayers = 2;
        [Tooltip("Секунд обратного отсчёта в лобби перед стартом матча (время выбрать команду). 0 — старт сразу.")]
        [SerializeField] private int startCountdownSeconds = 15;

        private bool serverStarted;
        private bool matchStarting;

        /// <summary>
        /// Признак того, что процесс — НАСТОЯЩИЙ headless-сервер. Определяется в РАНТАЙМЕ:
        /// не редактор И (batchmode ИЛИ нет графического устройства). Единый источник истины —
        /// на этот флаг завязаны гейты подхода Б в клиентских классах. Менять детекцию только здесь.
        /// [Interflow fix 2026-06-21] Рантайм вместо #if UNITY_SERVER: символ UNITY_SERVER истинен и в
        /// редакторе при target Dedicated Server → MPPM-инстансы ложно стартовали сервер и дрались за порт.
        /// </summary>
        public static bool IsHeadlessServer =>
            !Application.isEditor &&
            (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null);

        private void Start()
        {
            // [S2 2026-07-05] Режим lifecycle (аргумент -allocator): сервер не стартует сразу,
            // а ждёт аллокацию поллингом /my-allocation. Нет аргумента → обычное поведение (T1).
            if (TryEnterLifecycleMode()) return;

            bool auto = ShouldAutoStart();
            Debug.Log($"[ServerBootstrap] Start: headless={IsHeadlessServer}, gfx={SystemInfo.graphicsDeviceType}, " +
                      $"batchmode={Application.isBatchMode}, автозапуск={auto}, injectMenuButton={injectMenuButton}.");
            if (auto)
                StartAsServer();
            else if (injectMenuButton)
                StartCoroutine(InjectButtonWhenReady());
            else
                Debug.Log("[ServerBootstrap] Ни автозапуск, ни кнопка не включены — компонент простаивает.");
        }

        // Запускать сервер автоматически: настоящий headless-сервер (выделенная сборка) или аргумент CLI.
        private bool ShouldAutoStart()
        {
            if (autoStartInBatchmode && IsHeadlessServer) return true;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == serverArg) return true;
            return false;
        }

        // Разобрать порт прослушивания из аргумента запуска (portArg N).
        // Возвращает false ТОЛЬКО если аргумент передан, но значение невалидно (тогда сервер не поднимаем).
        // Аргумент не передан → true, port = defaultPort.
        private bool TryResolveListenPort(out ushort port)
        {
            port = defaultPort;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != portArg) continue;

                if (i + 1 >= args.Length)
                {
                    Debug.LogError($"[ServerBootstrap] Аргумент {portArg} передан без значения порта. Сервер не запущен.");
                    return false;
                }

                string raw = args[i + 1];
                if (!int.TryParse(raw, out int parsed) || parsed < 1 || parsed > 65535)
                {
                    Debug.LogError($"[ServerBootstrap] Некорректный порт \"{raw}\" в {portArg} (нужно целое 1..65535). Сервер не запущен.");
                    return false;
                }

                port = (ushort)parsed;
                Debug.Log($"[ServerBootstrap] Порт из аргумента {portArg}: {port}.");
                return true;
            }

            Debug.Log($"[ServerBootstrap] Аргумент {portArg} не передан — дефолтный порт {port}.");
            return true;
        }

        // ===================== ЗАПУСК СЕРВЕРА =====================

        /// <summary>
        /// Поднять выделенный сервер. По аналогии со штатным NetworkConnectionHandler.StartHost
        /// (ветка из лобби), но через NGO StartServer и без локального игрока/лобби.
        /// </summary>
        public void StartAsServer()
        {
            if (serverStarted) return;
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[ServerBootstrap] Нет NetworkManager — сервер не запущен.");
                return;
            }
            if (NetworkManager.Singleton.IsListening)
            {
                Debug.LogWarning("[ServerBootstrap] Сеть уже запущена (Host/Client/Server) — пропуск.");
                return;
            }
            if (NetworkConnectionHandler.Instance == null || SlotManager.Instance == null || SceneHandler.Instance == null)
            {
                Debug.LogError("[ServerBootstrap] Не найдены менеджеры (NCH/SlotManager/SceneHandler).");
                return;
            }

            // [S1 2026-07-04] Порт прослушивания: из аргумента запуска (portArg N), иначе defaultPort.
            // Слушаем listenAddress (0.0.0.0 = все интерфейсы). Строго ДО StartServer(): после инициализации
            // транспорта SetConnectionData уже не применится (риск §9 промта). Невалидный порт — не стартуем.
            if (!TryResolveListenPort(out ushort listenPort))
                return; // значение порта невалидно — упасть громко лучше, чем слушать не тот порт (контракт §6)

            UnityTransport unityTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (unityTransport == null)
            {
                Debug.LogError("[ServerBootstrap] UnityTransport не привязан к NetworkManager — порт не задать, сервер не запущен.");
                return;
            }
            unityTransport.SetConnectionData(listenAddress, listenPort, listenAddress);
            Debug.Log($"[ServerBootstrap] Порт прослушивания: {listenAddress}:{listenPort}.");

            // [Interflow 2026-08-01 server-opt] Лимит кадров: без него дедик крутит пустой цикл на 100% ядра.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = serverFrameRate;
            Debug.Log($"[ServerBootstrap] Лимит кадров сервера: {serverFrameRate} к/с.");

            // Подготовка слотов и запуск сервера (без игрока).
            SlotManager.Instance.InitializeSlotData();

            // [Interflow fix 2026-06-21] Проверка успеха StartServer. При занятом порте StartServer вернёт
            // false; без этого guard'а дальше шёл NRE-каскад (спавн handler / SceneManager == null).
            if (!NetworkManager.Singleton.StartServer())
            {
                Debug.LogError("[ServerBootstrap] StartServer не удался (порт занят?). Сервер не запущен.");
                return;
            }

            // Спавн сетевого обработчика (NetworkDataSync) — тот же префаб, что и у StartHost.
            GameObject handlerPrefab = NetworkConnectionHandler.Instance.networkHandler;
            if (handlerPrefab != null)
            {
                GameObject handler = Instantiate(handlerPrefab);
                handler.GetComponent<NetworkObject>().Spawn();
            }
            else
            {
                Debug.LogError("[ServerBootstrap] networkHandler не задан в NetworkConnectionHandler — синк не поднимется.");
            }

            // Синхронизация сцен клиентам — как в штатном StartHost.
            NetworkManager.Singleton.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive);
            NetworkManager.Singleton.SceneManager.ActiveSceneSynchronizationEnabled = true;
            NetworkManager.Singleton.SceneManager.OnSceneEvent += SceneHandler.Instance.SceneManager_OnSceneEvent;

            // Считаем подключения клиентов для автостарта матча.
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

            serverStarted = true;
            Debug.Log($"[ServerBootstrap] Сервер запущен (StartServer). Ожидание игроков: {requiredPlayers}.");
        }

        // ===================== АВТОСТАРТ МАТЧА =====================

        private void OnClientConnected(ulong clientId)
        {
            if (!NetworkManager.Singleton.IsServer) return; // только сервер (правило 6)
            TryAutoStartMatch();
        }

        // Стартовать матч, когда набралось нужное число игроков. По аналогии со штатным
        // UIManagerMenu.StartButton (ветка без сейва): RandomizeSlotData + LoadScene.
        private void TryAutoStartMatch()
        {
            if (matchStarting) return;
            if (SlotManager.Instance.gameStarted != GameState.Menu) return; // матч ещё не идёт
            if (CountPlayerSlots() < requiredPlayers) return;

            matchStarting = true;
            Debug.Log($"[ServerBootstrap] Набралось игроков ({requiredPlayers}) — отсчёт {startCountdownSeconds}с до старта.");
            StartCoroutine(StartMatchCountdown());
        }

        // Видимый обратный отсчёт перед стартом (рассылается клиентам через NetworkDataSync).
        // Отменяется, если игрок вышел (упало ниже requiredPlayers) или матч уже идёт.
        private IEnumerator StartMatchCountdown()
        {
            for (int s = startCountdownSeconds; s > 0; s--)
            {
                if (CountPlayerSlots() < requiredPlayers || SlotManager.Instance.gameStarted != GameState.Menu)
                {
                    if (NetworkDataSync.Instance != null) NetworkDataSync.Instance.MatchCountdownSend(0); // спрятать
                    Debug.Log("[ServerBootstrap] Отсчёт отменён — недостаточно игроков.");
                    matchStarting = false;
                    yield break;
                }
                if (NetworkDataSync.Instance != null) NetworkDataSync.Instance.MatchCountdownSend(s);
                yield return new WaitForSeconds(1f);
            }

            if (NetworkDataSync.Instance != null) NetworkDataSync.Instance.MatchCountdownSend(0); // спрятать
            Debug.Log("[ServerBootstrap] Отсчёт завершён — старт матча.");
            SlotManager.Instance.RandomizeSlotData();
            SceneHandler.Instance.LoadScene();
        }

        // Сколько игровых слотов заняты живыми игроками (не боты, не нейтралы).
        private int CountPlayerSlots()
        {
            int n = 0;
            SlotType[] slots = SlotManager.Instance.slotType;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == SlotType.Player) n++;
            return n;
        }

        // ===================== КНОПКА В МЕНЮ (рантайм, без правок Menu.uxml/UIManagerMenu) =====================

        private IEnumerator InjectButtonWhenReady()
        {
            // [Interflow 2026-08-01 ADR-005] Вёрстка кнопки перенесена в клиентский мост (Presentation.MenuUI):
            // симуляция UI-типов не знает; мост вернёт true, когда меню готово и кнопка вставлена (или уже есть).
            Debug.Log("[ServerBootstrap] Жду готовности меню для инъекции кнопки SERVER...");
            while (!(Presentation.MenuUI?.TryAddMenuButton("ServerGame", menuButtonText, "JoinGame", () => StartAsServer()) ?? false))
                yield return null;
            Debug.Log("[ServerBootstrap] Кнопка SERVER добавлена в меню.");
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }
}
