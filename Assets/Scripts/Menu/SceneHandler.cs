using System;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEditor;
using System.IO;

namespace StrategyCore
{
    public class SceneHandler : MonoBehaviour
    {
        // Scene data fetching: we must load custom json file to correctly set team/player data.
#if UNITY_EDITOR
        private void OnValidate()
        {
            if (sceneData == null) return;
            for (int i = 0; i < sceneData.Length; i++)
            {
                if (sceneData[i].sceneAsset != null)
                {
                    sceneData[i].sceneName = sceneData[i].sceneAsset.name;
                    string filePath = AssetDatabase.GetAssetPath(sceneData[i].sceneAsset).Replace(".unity", "") + "/TeamData.json";

                    if (File.Exists(filePath))
                    {
                        string content = System.IO.File.ReadAllText(filePath);
                        string[] parts = content.Split(new string[] { SaveManager.delimiter }, StringSplitOptions.None);

                        TeamsAndPlayers[] tp = JsonHelper.FromJson<TeamsAndPlayers>(parts[0]);
                        bool chooseTeams = (parts[1] == "1") ? true : false;
                        string[] factions = JsonHelper.FromJson<string>(parts[2]);
                        int spawnPointCount = int.Parse(parts[3]);

                        sceneData[i].chooseTeams = chooseTeams;
                        sceneData[i].teamsAndPlayers = tp;
                        sceneData[i].factions = factions;
                        sceneData[i].spawnPointCount = spawnPointCount;
                    }
                }
            }
        }
#endif

        public static SceneHandler instance;

        [Tooltip("For GameScene set to itself. For MenuScene set the game scenes of your game.")]
        public SceneData[] sceneData;
        [HideInInspector] public int sceneIndex; // Active scene index
        private Scene m_LoadedScene;

        [HideInInspector] public string saveFileName = ""; // When assigned, after scene load, we will try to load it
        [HideInInspector] public string saveSceneData = ""; // For clients, server sends data

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;

                // Initialize slot data is dependant on SceneHandler, so we call it here
                if (SlotManager.instance == null) GameObject.Find("ProjectManager").GetComponent<SlotManager>().InstanceSet();
                if (SlotManager.instance.gameStarted == GameState.Started) // We are not coming from lobby
                {
                    SlotManager.instance.InitializeSlotData();
                    SlotManager.instance.AddClientID(0, SlotManager.instance.currentName);
                    SlotManager.instance.gameOn = true;
                    GameObject.Find("GameManager").GetComponent<GameManager>().gameStartCall = true;
                }
                SlotManager.instance.SetCurrentPlayer(SlotManager.instance.currentPlayer);
            }
            else
            {
                // Remove the duplicate project manager
                Destroy(this.gameObject);
            }
        }

        // ============================= GAME EVENTS ==============================================================================

        /// <summary>
        /// EVERYONE: Callback when scene starts loading.
        /// </summary>
        public void SceneStartedLoading()
        {
            SlotManager.instance.SetGameState(GameState.Loading);
            if (Presentation.MenuUI != null) Presentation.MenuUI?.ShowMenuLobby(2);
        }

        /// <summary>
        /// EVERYONE: Callback when scene is finished loading.
        /// </summary>
        /// <param name="loadedScene"></param>
        private void SceneFinishedLoading(Scene loadedScene)
        {
            m_LoadedScene = loadedScene;
            if (sceneData == null || sceneIndex >= sceneData.Length) return;

            // Pause till everybody else loads the scene, hide the UI
            if (sceneData[sceneIndex].sceneName == loadedScene.name)
            {
                // if (SlotManager.instance.gameStarted == GameState.Started) return;  // CHANGE THE LOGIC HERE, ON CLIENTS ALREADY LOADED SCENE, WHAT SHOULD HAPPEN

                // Initialize the scene
                SceneManager.SetActiveScene(m_LoadedScene);
                Time.timeScale = 0f;

                // If we should load the game before starting the game
                // For server: FileName
                if (saveFileName != "")
                {
                    SaveManager.LoadSaveFile(saveFileName, true);
                }
                // For clients: SceneData
                else if (saveSceneData != "")
                {
                    GameManager.instance.StartCoroutine(SaveManager.LoadSave_Internal(saveSceneData));
                }
            }
        }

        /// <summary>
        /// EVERYONE: Callback when all players finished loading the scene.
        /// </summary>
        /// <param name="firstLoad">When we enter the scene for the first time, false when we load the game.</param>
        public void StartTheGame(bool firstLoad)
        {
            // [ДИАГ] ВРЕМЕННО. Снять после диагностики.
            Debug.Log($"[ДИАГ] StartTheGame(firstLoad={firstLoad}): saveFileName='{saveFileName}', длинаSaveSceneData={(saveSceneData == null ? -1 : saveSceneData.Length)}, clientsLoading={NetworkConnectionHandler.instance.clientsLoading.Count}");
            // Still have a save data to load
            if (saveFileName != "" || saveSceneData != "" || NetworkConnectionHandler.instance.clientsLoading.Count != 0) return;

            // Everyone:
            NetworkConnectionHandler.instance.connectionStage = 0;
            Time.timeScale = 1f;
            Presentation.MenuUI?.HideUIDocument();
            SlotManager.instance.SetGameState(GameState.Started);
            SlotManager.instance.OnGameStart?.Invoke();

            if (firstLoad) GameManager.instance.GameStart();

            // Server:
            if (NetworkManager.Singleton.IsServer)
            {
                // Идемпотентность: при входе «игра уже идёт» Tick уже подписан в OnNetworkSpawn — не дублируем
                NetworkManager.Singleton.NetworkTickSystem.Tick -= NetworkDataSync.instance.Tick;
                NetworkManager.Singleton.NetworkTickSystem.Tick += NetworkDataSync.instance.Tick;
            }
        }

        // ============================= COMMANDS ==============================================================================

        /// <summary>
        /// Sets the active scene by its index in sceneData. After setting it you can start loading the scene.
        /// </summary>
        /// <param name="index">Index of the scene in sceneData.</param>
        /// <returns>If active scene set was successful.</returns>
        public bool SetActiveScene(int index)
        {
            if (index >= sceneData.Length) return false;

            sceneIndex = index;
            return true;
        }

        /// <summary>
        /// Sets the active scene by its name. Scene must be added to sceneData. After setting it you can start loading the scene.
        /// </summary>
        /// <param name="sceneName">Name of the scene.</param>
        /// <returns>If active scene set was successful.</returns>
        public bool SetActiveScene(string sceneName)
        {
            for (int i = 0; i < sceneData.Length; i++)
            {
                if (sceneData[i].sceneName == sceneName)
                {
                    sceneIndex = i;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Server: Load the active scene.
        /// </summary>
        public void LoadScene()
        {
            if (sceneData == null || sceneIndex >= sceneData.Length) return;

            if (NetworkManager.Singleton.IsServer && !string.IsNullOrEmpty(sceneData[sceneIndex].sceneName))
            {
                var status = NetworkManager.Singleton.SceneManager.LoadScene(sceneData[sceneIndex].sceneName, LoadSceneMode.Additive);
                CheckStatus(status);
            }
        }

        /// <summary>
        /// Everyone: Unload the active scene.
        /// </summary>
        /// <param name="multiPlayer">When multiplayer only server can call this method to unload a scene.</param>
        public void UnloadScene(bool multiPlayer = false)
        {
            // Check if Scene is valid
            if (!m_LoadedScene.IsValid() || !m_LoadedScene.isLoaded) return;

            // Single Player
            if (!multiPlayer)
            {
                SceneManager.UnloadSceneAsync(m_LoadedScene);
                return;
            }

            // Network - Assure only the server calls it
            if (!NetworkManager.Singleton.IsServer) return;
            // Unload the scene
            var status = NetworkManager.Singleton.SceneManager.UnloadScene(m_LoadedScene);
            CheckStatus(status, false);
        }

        // ============================= SCENE EVENTS ==============================================================================

        /// <summary>
        /// Handles the processing of OnSceneEvent changes.
        /// </summary>
        /// <param name="sceneEvent">SceneEvent.</param>
        public void SceneManager_OnSceneEvent(SceneEvent sceneEvent)
        {
            // [ДИАГ] ВРЕМЕННО. Снять после диагностики.
            Debug.Log($"[ДИАГ] СобытиеСцены: тип={sceneEvent.SceneEventType}, сцена={sceneEvent.SceneName}, clientId={sceneEvent.ClientId}, свой={sceneEvent.ClientId == NetworkManager.Singleton.LocalClientId}, IsServer={NetworkManager.Singleton.IsServer}");
            var clientOrServer = sceneEvent.ClientId == NetworkManager.ServerClientId ? "server" : "client";

            if (sceneEvent.SceneEventType == SceneEventType.Load)
            {
                if (sceneEvent.ClientId == NetworkManager.Singleton.LocalClientId)
                {
                    // Local: Started loading
                    SceneStartedLoading();
                }
            }
            else if (sceneEvent.SceneEventType == SceneEventType.LoadComplete)
            {
                if (sceneEvent.ClientId == NetworkManager.Singleton.LocalClientId)
                {
                    // Local: Finished scene loading
                    SceneFinishedLoading(sceneEvent.Scene);
                }
            }
            else if (sceneEvent.SceneEventType == SceneEventType.LoadEventCompleted)
            {
                // Everyone finished loading
                StartTheGame(true);
            }
            else if (sceneEvent.SceneEventType == SceneEventType.UnloadComplete)
            {
                if (sceneEvent.ClientId == NetworkManager.Singleton.LocalClientId)
                {
                    // Local: Finished unloading
                }
            }
            else if (sceneEvent.SceneEventType == SceneEventType.UnloadEventCompleted)
            {
                // Everyone finished unloading
                if (sceneEvent.ClientsThatTimedOut.Count > 0)
                {
                    Debug.LogWarning($"Unload event timed out for the following client " + $"identifiers:({sceneEvent.ClientsThatTimedOut})");
                }
            }
        }

        /// <summary>
        /// Checks if scene is loaded.
        /// </summary>
        public bool SceneIsLoaded
        {
            get
            {
                if (m_LoadedScene.IsValid() && m_LoadedScene.isLoaded)
                {
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Warns about State of the scene load.
        /// </summary>
        /// <param name="status"></param>
        /// <param name="isLoading"></param>
        private void CheckStatus(SceneEventProgressStatus status, bool isLoading = true)
        {
            if (sceneData == null || sceneIndex >= sceneData.Length) return;

            var sceneEventAction = isLoading ? "load" : "unload";
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogWarning($"Failed to {sceneEventAction} {sceneData[sceneIndex].sceneName} with" +
                    $" a {nameof(SceneEventProgressStatus)}: {status}");
            }
        }
    }

    [Serializable]
    public class SceneData
    {
#if UNITY_EDITOR
        public SceneAsset sceneAsset;
#endif
        [HideInInspector] public string sceneName;
        [HideInInspector] public bool chooseTeams;
        [HideInInspector] public TeamsAndPlayers[] teamsAndPlayers;
        [HideInInspector] public string[] factions;
        [HideInInspector] public int spawnPointCount;
    }
}