using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StrategyCore
{
    /// <summary>
    /// Диагностический логгер сетевого/сценового потока. НЕ трогает ассет — подписывается только
    /// на публичные события NGO и наблюдает за `SlotManager.gameStarted`. Помогает увидеть, на каком
    /// шаге встаёт подключение/старт сцены (например, почему у клиента не стартует игра).
    ///
    /// Логирует: запуск сервера/клиента, подключения/отключения, ВСЕ фазы события сцены
    /// (Load/LoadComplete/LoadEventCompleted/Unload…) с именем сцены и clientId, смену состояния игры.
    /// Класс роли (HOST/SERVER/CLIENT) пишется в префиксе. Повесить на объект в сцене меню
    /// (на каждом инстансе MPPM/билда). Снять перед релизом.
    /// </summary>
    public class NetworkFlowLogger : MonoBehaviour
    {
        [Tooltip("Включить вывод логов потока в Console.")]
        [SerializeField] private bool logEnabled = true;
        [Tooltip("Печатать дамп владения (слот/owner/команды/юниты) по клавише F8.")]
        [SerializeField] private bool dumpOnF8 = true;

        private static NetworkFlowLogger instance;
        private bool callbacksHooked;
        private bool sceneHooked;
        private bool gameStartHooked;
        private GameState lastState = (GameState)(-1);

        private void Awake()
        {
            // Переживаем переход меню→игровая сцена (иначе дамп при старте матча теряется).
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            HookManagerCallbacks();
            HookGameStart();
            Log("NetworkFlowLogger активен.");
        }

        private void Update()
        {
            // На случай, если NetworkManager/SlotManager появились/инициализировались позже Start.
            HookManagerCallbacks();
            HookGameStart();
            TryHookSceneEvents();
            LogStateTransition();

            if (dumpOnF8 && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
                LogOwnershipState();
        }

        private void HookManagerCallbacks()
        {
            if (callbacksHooked) return;
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null) return;

            nm.OnServerStarted += OnServerStarted;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnect;
            callbacksHooked = true;
        }

        // Штатное событие старта матча (срабатывает на КАЖДОМ пире после загрузки сцены) —
        // самый надёжный момент логировать команду/owner клиента.
        private void HookGameStart()
        {
            if (gameStartHooked || SlotManager.Instance == null) return;
            SlotManager.Instance.OnGameStart += OnMatchStarted;
            gameStartHooked = true;
        }

        private void OnMatchStarted()
        {
            Log("=== Матч загружен (SlotManager.OnGameStart) ===");
            LogOwnershipState();
        }

        // SceneManager доступен только после старта сети — подписываемся, как только появился.
        private void TryHookSceneEvents()
        {
            if (sceneHooked) return;
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || nm.SceneManager == null) return;

            nm.SceneManager.OnSceneEvent += OnSceneEvent;
            sceneHooked = true;
            Log("Подписка на SceneManager.OnSceneEvent выполнена.");
        }

        private void LogStateTransition()
        {
            if (SlotManager.Instance == null) return;
            GameState s = SlotManager.Instance.gameStarted;
            if (s != lastState)
            {
                Log($"gameStarted: {lastState} -> {s}");
                lastState = s;
                if (s == GameState.Started) LogOwnershipState(); // на старте матча — полный дамп владения
            }
        }

        // ===================== ОБРАБОТЧИКИ =====================

        private void OnServerStarted() => Log("OnServerStarted (сеть поднята как сервер/хост).");

        private void OnClientConnected(ulong clientId)
        {
            Log($"OnClientConnected: clientId={clientId} (local={LocalId()}).");
            LogOwnershipState(); // на каждом подключении — кто в каком слоте
        }

        private void OnClientDisconnect(ulong clientId)
            => Log($"OnClientDisconnect: clientId={clientId}.");

        private void OnSceneEvent(SceneEvent e)
        {
            Log($"SceneEvent: {e.SceneEventType} | сцена='{e.SceneName}' | clientId={e.ClientId} | local={LocalId()}.");
        }

        // ===================== ДАМП ВЛАДЕНИЯ (слот/owner/команды/юниты) =====================

        // Печатает картину владения на ЭТОМ пире: его слот, currentPlayer, таблицу слотов,
        // ownerPlayer команд MatchManager и количество юнитов по владельцу. Нужен, чтобы понять,
        // почему команды клиента режутся (NetworkCommandSync применяет только если слот == unit.owner).
        private void LogOwnershipState()
        {
            SlotManager sm = SlotManager.Instance;
            if (sm == null) { Log("Дамп владения: SlotManager отсутствует."); return; }

            NetworkManager nm = NetworkManager.Singleton;
            bool listening = nm != null && nm.IsListening;
            ulong localId = listening ? nm.LocalClientId : 0;
            int mySlot = listening ? sm.GetClientSlot(localId) : -1;

            int myTeam = (mySlot >= 0 && sm.playerTeam != null && mySlot < sm.playerTeam.Length) ? sm.playerTeam[mySlot] : -1;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"=== Дамп владения ({Role()}) ===");
            string stage = NetworkConnectionHandler.Instance != null ? NetworkConnectionHandler.Instance.connectionStage.ToString() : "?";
            sb.AppendLine($"LocalClientId={localId}; мой слот (GetClientSlot)={mySlot}; currentPlayer={sm.currentPlayer}; connectionStage={stage} (2=mid-game, RPC игнорятся).");
            sb.AppendLine($"Я: имя='{sm.currentName}', слот={mySlot}, currentTeam={sm.currentTeam}, команда слота={myTeam}. " +
                          $"Командовать смогу юнитами с owner=={mySlot} (NetworkCommandSync проверяет слот==owner).");

            if (MatchManager.Instance != null)
            {
                TeamWaveConfig a = MatchManager.Instance.Team(0);
                TeamWaveConfig b = MatchManager.Instance.Team(1);
                string ao = a != null ? a.ownerPlayer.ToString() : "-";
                string bo = b != null ? b.ownerPlayer.ToString() : "-";
                sb.AppendLine($"MatchManager: teamA.ownerPlayer={ao}; teamB.ownerPlayer={bo}.");
            }

            if (sm.slotType != null)
                for (int i = 0; i < sm.slotType.Length; i++)
                    sb.AppendLine($"  slot[{i}]: type={sm.slotType[i]}, playerID={sm.playerID[i]}, team={sm.playerTeam[i]}.");

            // Юниты по владельцу (чтобы видеть, какой owner у команды, которой пытается рулить клиент).
            Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
            Dictionary<int, int> byOwner = new Dictionary<int, int>();
            for (int i = 0; i < units.Length; i++)
            {
                if (units[i] == null) continue;
                int o = units[i].owner;
                byOwner[o] = byOwner.TryGetValue(o, out int c) ? c + 1 : 1;
            }
            foreach (KeyValuePair<int, int> kv in byOwner)
                sb.AppendLine($"  юнитов с owner={kv.Key}: {kv.Value}.");

            sb.AppendLine($"Вывод: команда клиента применится только если 'мой слот' == owner юнита (или debugMode).");
            Debug.Log(sb.ToString());
        }

        // ===================== УТИЛИТЫ =====================

        private string Role()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null) return "NONE";
            if (nm.IsHost) return "HOST";
            if (nm.IsServer) return "SERVER";
            if (nm.IsClient) return "CLIENT";
            return "OFFLINE";
        }

        private string LocalId()
        {
            NetworkManager nm = NetworkManager.Singleton;
            return (nm != null && nm.IsListening) ? nm.LocalClientId.ToString() : "-";
        }

        private void Log(string msg)
        {
            if (logEnabled) Debug.Log($"[NetFlow {Role()}] {msg}");
        }

        private void OnDestroy()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnServerStarted -= OnServerStarted;
                nm.OnClientConnectedCallback -= OnClientConnected;
                nm.OnClientDisconnectCallback -= OnClientDisconnect;
                if (nm.SceneManager != null) nm.SceneManager.OnSceneEvent -= OnSceneEvent;
            }
            if (gameStartHooked && SlotManager.Instance != null)
                SlotManager.Instance.OnGameStart -= OnMatchStarted;
        }
    }
}
