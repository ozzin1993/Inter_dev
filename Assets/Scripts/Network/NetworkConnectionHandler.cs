using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace StrategyCore
{
    public class NetworkConnectionHandler : MonoBehaviour, IStartupService
    {
        public static NetworkConnectionHandler Instance { get; private set; }

        private bool startupDone; // защита от повторного подъёма (стартовик сцены + собственный Awake)
        [HideInInspector] public static bool isClient = false; // Defines if the current machine is client

        [Tooltip("Reference to prefab of network handler")]
        public GameObject networkHandler;
        private GameObject m_networkHandler;

        // List of clients still loading the game. Used to track if all players loaded the game.
        public List<ulong> clientsLoading = new List<ulong>();
        public Action clientsListUpdated; // Called when clients list is updated
        public int connectionStage; // Which connection sync stage are we on; 0 - standard, 1 - sending the save data, 2 - joining midgame

        [HideInInspector] public bool exitingPlayerMode;

        void Awake() => Startup();

        /// <summary>
        /// Подъём службы (IStartupService). Идемпотентен: повторный вызов выходит сразу.
        /// </summary>
        public void Startup()
        {
            if (startupDone) return;
            startupDone = true;

            if (Instance == null)
            {
                Instance = this;
#if UNITY_EDITOR
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
            }
            else
            {
                // Remove the duplicate project manager
                Destroy(this.gameObject);
            }
        }

        void OnDestroy()
        {
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
        }

        void Start()
        {
            NetworkManager.Singleton.ConnectionApprovalCallback = ConnectionApproval;
            NetworkManager.Singleton.OnClientConnectedCallback += ClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += ClientDisconnected;
        }

        // ============================= CLIENT WAITING LIST ==============================================================================

        /// <summary>
        /// Adds all connected players to the waiting list.
        /// </summary>
        /// <param name="noServer">Should server also be added.</param>
        public void AddClientsToWaitingList(bool noServer = false)
        {
            for (int i = 0; i < SlotManager.Instance.playerID.Length; i++)
            {
                if (SlotManager.Instance.playerID[i] != -1)
                {
                    if (noServer && SlotManager.Instance.playerID[i] == 0) continue;
                    NetworkConnectionHandler.Instance.clientsLoading.Add((ulong)SlotManager.Instance.playerID[i]);
                }
            }
        }

        /// <summary>
        /// Removes the client from the waiting list.
        /// </summary>
        /// <param name="ClientID">Client`s network ID.</param>
        public void ClientsWaitingListRemove(ulong ClientID)
        {
            NetworkConnectionHandler.Instance.clientsLoading.Remove(ClientID);
            clientsListUpdated?.Invoke();
        }

        // ============================= PAUSE / RESUME ==============================================================================

        /// <summary>
        /// Pauses the game for everyone.
        /// </summary>
        public void PauseTheGame()
        {
            // [ДИАГ] ВРЕМЕННО. Снять после диагностики.
            Debug.Log($"[ДИАГ] PauseTheGame вызвана. IsServer={NetworkManager.Singleton.IsServer}, состояниеИгры={SlotManager.Instance.gameStarted}");
            // Pause the game
            Time.timeScale = 0;
            SlotManager.Instance.gameOn = false;
            Presentation.MenuUI?.ShowUIDocument();
            Presentation.MenuUI?.ShowMenuLobby(2);

            // Pause the game for clients
            if (NetworkManager.Singleton.IsServer)
            {
                NetworkDataSync.Instance.ForceSend();
                NetworkDataSync.Instance.PauseTheGameClientRpc();
            }
        }

        /// <summary>
        /// Resumes the game for everyone.
        /// </summary>
        /// <param name="clearSaves">Should remove temporary save data on the clients.</param>
        public void ResumeTheGame(bool clearSaves = false)
        {
            // Resume the game for clients
            if (NetworkManager.Singleton.IsServer)
            {
                NetworkDataSync.Instance.ResumeTheGameSend(clearSaves);
            }

            Time.timeScale = 1;
            NetworkConnectionHandler.Instance.connectionStage = 0;
            Presentation.MenuUI?.HideUIDocument();
            SlotManager.Instance.SetGameState(GameState.Started);
        }

        // ============================= CONNECT / DISCONNECT CALLBACKS ==============================================================================

        /// <summary>
        /// Callback when client connects. Called both on the server when any client, including self, connects and on clients when they themselves connect.
        /// </summary>
        private void ClientConnected(ulong clientId)
        {
            // [ДИАГ] ВРЕМЕННО. Разбор «второе окно висит на CONNECTING». Снять после диагностики.
            Debug.Log($"[ДИАГ] ClientConnected: clientId={clientId}, свой={clientId == NetworkManager.Singleton.LocalClientId}, IsServer={NetworkManager.Singleton.IsServer}, IsHost={NetworkManager.Singleton.IsHost}, IsClient={NetworkManager.Singleton.IsClient}, состояниеИгры={SlotManager.Instance.gameStarted}, MenuUI={Presentation.MenuUI != null}, MenuReady={Presentation.MenuUI?.MenuReady}");
            // Everyone: define if client
            if (!NetworkManager.Singleton.IsHost && NetworkManager.Singleton.IsClient) isClient = true;

            // Show lobby
            if (SlotManager.Instance.gameStarted == GameState.Menu)
            {
                // [Interflow fix 2026-06-26] null-guard: на выделенном сервере клиентского меню-UI может не быть (NRE из NGO HandleSessionOwnerEvent).
                if (Presentation.MenuUI != null)
                {
                    if (clientId == NetworkManager.Singleton.LocalClientId) Presentation.MenuUI?.ShowMenuLobby(1);
                    Presentation.MenuUI?.FillPlayerList();
                }
                NetworkConnectionHandler.Instance.connectionStage = 0; // We are not joining mid-game
            }

            // Server: 
            if (NetworkManager.Singleton.IsServer)
            {
                // Send player information
                if (clientId != NetworkManager.Singleton.LocalClientId)
                {
                    NetworkDataSync.Instance.PlayerListSend(clientId);

                    // Joining midgame, send scene information
                    if (SlotManager.Instance.gameStarted == GameState.Started)
                    {
                        NetworkDataSync.Instance.SendSceneData(clientId, true);

                        // Догнать опоздавшего живыми зонами на земле: их спавн он пропустил,
                        // а реестр держит сервер (MatchManager.GroundZones).
                        if (MatchManager.Instance != null) MatchManager.Instance.ResendGroundZonesTo(clientId);
                    }
                }
                // Else: set current player
                else
                {
                    SlotManager.Instance.SetCurrentPlayer(SlotManager.Instance.GetClientSlot(clientId));
                }

                // Chat msg send
                // [Interflow fix 2026-06-26] guard −1: при гонке/полном лобби GetClientSlot==-1 → playerName[-1] IndexOutOfRange.
                int connectedSlot = SlotManager.Instance.GetClientSlot(clientId);
                if (connectedSlot != -1)
                {
                    string msg = SlotManager.Instance.playerName[connectedSlot] + " connected to the game.";
                    // [Interflow 2026-08-01 ADR-005] Серверный чат — через хаб (релей + локальная отрисовка).
                    Presentation.ChatServerMsgAuto(msg);
                }
            }
        }

        /// <summary>
        /// Callback when client disconnects. Called both on the server when any client, including self, disconnects and on clients when they themselves connect.
        /// </summary>
        private void ClientDisconnected(ulong clientId)
        {
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                // Local client disconnected

                // CleanUp
                NetworkConnectionHandler.Instance.clientsListUpdated -= NetworkDataSync.Instance.ServerStartLoading;
                NetworkConnectionHandler.Instance.clientsListUpdated -= NetworkDataSync.Instance.ServerPlayersFinishedLoading;
                SceneHandler.Instance.UnloadScene();
                isClient = false;
                SlotManager.Instance.SetGameState(GameState.Menu);
                SlotManager.Instance.unitNetID = new Dictionary<UInt16, Unit>();
                // Menu
                // [Interflow fix 2026-06-26] null-guard: меню-UI может отсутствовать на сервере.
                if (Presentation.MenuUI != null)
                {
                    Presentation.MenuUI?.ShowUIDocument();
                    Presentation.MenuUI?.ShowMenuLobby(0);
                }
                // NetworkManager.Singleton.SceneManager.OnSceneEvent -= SceneHandler.Instance.SceneManager_OnSceneEvent;
            }
            else
            {
                // To prevent a bug that makes server trigger as client? when exiting the player mode
                if (exitingPlayerMode) return;

                // Chat msg send
                // [Interflow fix 2026-06-26] guard −1: GetClientSlot может вернуть -1 (слот уже снят/гонка) → playerName[-1] IndexOutOfRange.
                int disconnectedSlot = SlotManager.Instance.GetClientSlot(clientId);
                if (disconnectedSlot != -1)
                {
                    string msg = SlotManager.Instance.playerName[disconnectedSlot] + " disconnected from the game.";
                    // [Interflow 2026-08-01 ADR-005] Серверный чат — через хаб (релей + локальная отрисовка).
                    Presentation.ChatServerMsgAuto(msg);
                }

                // Server: Someone disconnected
                SlotManager.Instance.RemoveClientID(clientId);
                if (SlotManager.Instance.gameStarted == GameState.Menu)
                {
                    // Refresh lobby
                    // [Interflow fix 2026-06-26] null-guard: меню-UI может отсутствовать на сервере.
                    if (Presentation.MenuUI != null) Presentation.MenuUI?.FillPlayerList();
                }
                else if (SlotManager.Instance.gameStarted == GameState.Started)
                {
                    // Remove from the list
                    NetworkConnectionHandler.Instance.ClientsWaitingListRemove(clientId);
                }
                // Send player information to clients
                NetworkDataSync.Instance.PlayerListSend(clientId, true);
            }
        }

        /// <summary>
        /// Connection approval on the server when client tries to connect.
        /// </summary>
        private void ConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            // When loading, do not approve connections
            if (SlotManager.Instance.gameStarted == GameState.Loading)
            {
                response.Approved = false;
                response.Pending = false;
                return;
            }

            var clientName = System.Text.Encoding.UTF8.GetString(request.Payload);
            // Санация имени: символы-разделители формата сейва ломают разбор сейва и вход мидгейм-клиентов
            clientName = clientName.Replace("\u2561", "_").Replace("~", "_").Replace("^", "_").Trim();
            int slot = SlotManager.Instance.AddClientID(request.ClientNetworkId, clientName);
            if (slot == -1)
            {
                // Not approved
                response.Approved = false;
                response.Reason = "No available slots";
                Debug.Log("Not approved: No available slots. Tried to join " + clientName + ", " + request.ClientNetworkId);
            }
            else
            {
                // Approved
                response.Approved = true;
                Debug.Log("Approved: joined " + clientName + ", " + request.ClientNetworkId + " at slot " + slot);

                // If game has started, client is joining midgame. Pause the game. In Connection callback we will send the scene information
                if (SlotManager.Instance.gameStarted == GameState.Started)
                {
                    clientsLoading.Add(request.ClientNetworkId);
                    PauseTheGame();
                    NetworkConnectionHandler.Instance.clientsListUpdated += NetworkDataSync.Instance.ServerPlayersFinishedLoading;
                }
            }
            response.Pending = false;
        }

        // ============================= HOST / CLIENT ==============================================================================

        /// <summary>
        /// Start the game as a host.
        /// </summary>
        /// <param name="name">Host name.</param>
        public void StartHost(string name)
        {
            // Game already started, most likely hosting without a lobby.
            if (SlotManager.Instance.gameStarted == GameState.Started)
            {
                SlotManager.Instance.InitializeSlotData();
                NetworkManager.Singleton.StartHost();
                m_networkHandler = Instantiate(networkHandler);
                m_networkHandler.GetComponent<NetworkObject>().Spawn();
                NetworkManager.Singleton.SceneManager.OnSceneEvent += SceneHandler.Instance.SceneManager_OnSceneEvent;
                return;
            }

            // We are hosting from a lobby

            if (NetworkManager.Singleton.ShutdownInProgress || NetworkManager.Singleton.IsListening) return;
            if (!IsPortAvailable(NetworkManager.Singleton.GetComponent<UnityTransport>().ConnectionData.Port)) return; // Check port not implemented

            // UI connection menu
            Presentation.MenuUI?.ShowMenuLobby(2);
            // Preparation
            if (m_networkHandler) Destroy(m_networkHandler);
            SlotManager.Instance.InitializeSlotData();
            // Settings for host
            NetworkManager.Singleton.NetworkConfig.ConnectionData = System.Text.Encoding.ASCII.GetBytes(name); // Send host`s name
            NetworkManager.Singleton.StartHost();
            // Data
            m_networkHandler = Instantiate(networkHandler);
            m_networkHandler.GetComponent<NetworkObject>().Spawn();
            NetworkManager.Singleton.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive);
            NetworkManager.Singleton.SceneManager.ActiveSceneSynchronizationEnabled = true;
            NetworkManager.Singleton.SceneManager.OnSceneEvent += SceneHandler.Instance.SceneManager_OnSceneEvent;
        }

        /// <summary>
        /// Start the game as a client.
        /// </summary>
        /// <param name="name">Client name.</param>
        /// <param name="address">Connectiona address.</param>
        /// <param name="port">Connection port.</param>
        public void StartClient(string name, string address = null, string port = null)
        {
            // Game already started, most likely connecting without a lobby.
            if (SlotManager.Instance.gameStarted == GameState.Started)
            {
                NetworkManager.Singleton.StartClient();
                NetworkManager.Singleton.SceneManager.OnSceneEvent += SceneHandler.Instance.SceneManager_OnSceneEvent;
                return;
            }

            // We are connecting from a lobby

            if (NetworkManager.Singleton.ShutdownInProgress || NetworkManager.Singleton.IsListening) return;

            // Set address and port
            if (address != null)
            {
                var unityTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();

                if (unityTransport != null)
                {
                    unityTransport.SetConnectionData(
                        address,  // The IP address is a string
                        ushort.Parse(port) // The port number is an unsigned short
                    );
                }
                else
                {
                    Debug.LogError("Unity Transport is not attached to the NetworkManager.");
                }
            }

            // UI connection menu
            Presentation.MenuUI?.ShowMenuLobby(2);
            // Set name and start client
            NetworkManager.Singleton.NetworkConfig.ConnectionData = System.Text.Encoding.ASCII.GetBytes(name); // Send client`s name
            NetworkManager.Singleton.StartClient();
            NetworkManager.Singleton.SceneManager.OnSceneEvent += SceneHandler.Instance.SceneManager_OnSceneEvent;
            NetworkConnectionHandler.Instance.connectionStage = 2; // Assume we are joining midgame. Set to false in connected calback if we are in the lobby.
        }

        /// <summary>
        /// Shut down the connection on a local machine.
        /// </summary>
        public void Shutdown()
        {
            Presentation.MenuUI?.ShowMenuLobby(0);  // Show the menu
            NetworkManager.Singleton.Shutdown();
        }

        // ============================= UTILS ==============================================================================

        /// <summary>
        /// Not implemented. Should check port availability.
        /// </summary>
        private bool IsPortAvailable(int port)
        {
            // POTENTIALLY IMPLEMENT PORT AVAILABILITY CHECK
            return true;
        }

#if UNITY_EDITOR
        //To fight an error that happens when host exits the player mode with clients connected. Disconnect callback is being fired on behalf of the client
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                exitingPlayerMode = true;
            }
        }
#endif
    }
}
