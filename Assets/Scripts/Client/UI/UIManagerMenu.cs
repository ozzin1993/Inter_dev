using Camera_TopDownNS;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StrategyCore
{
    public partial class UIManagerMenu : MonoBehaviour // [Interflow fix 2026-08-01 partial-split] класс разрезан на partial-файлы (задача №11)
    {
        public static UIManagerMenu instance;
        [HideInInspector] public UIDocument UIDocument;
        [HideInInspector] public int currentMenuIndex;

        // Menu
        VisualElement hostButton;
        VisualElement joinButton;
        VisualElement loadButton;
        VisualElement quitButton;
        VisualElement saveButton;

        // Lobby
        VisualElement playerList;
        VisualElement startButton;
        VisualElement cancelButton;

        // Load Menu
        VisualElement cancelButtonLoad;
        VisualElement saveList;

        // Chat
        TextField msgInput;
        ScrollView chatBox;

        // FloatingMenu
        VisualElement floatingMenu;

        // [Interflow fix 2026-06-26 путь1] Готовность меню-UI: становится true в КОНЦЕ Start (после построения rootVisualElement).
        // На headless-сервере Start выходит по init-гейту раньше → остаётся false, серверо-достижимые методы no-op по своему состоянию.
        private bool presentationReady;

        void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
        }

        void Start()
        {
            if (ServerBootstrap.IsHeadlessServer) return;   // [Interflow fix 2026-06-20]
            UIDocument = GetComponent<UIDocument>();

            // ---------- Floating Menu ----------
            floatingMenu = UIDocument.rootVisualElement.Q("FloatingMenu");
            floatingMenu.RegisterCallback<ClickEvent>(FloatingMenuHandler);

            // ---------- Menu ----------
            hostButton = UIDocument.rootVisualElement.Q("Menu").Q("HostGame");
            hostButton.RegisterCallback<ClickEvent>(HostButton);
            joinButton = UIDocument.rootVisualElement.Q("Menu").Q("JoinGame");
            joinButton.RegisterCallback<ClickEvent>(JoinButton);
            loadButton = UIDocument.rootVisualElement.Q("Menu").Q("Load");
            loadButton.RegisterCallback<ClickEvent>(LoadButton);
            quitButton = UIDocument.rootVisualElement.Q("Menu").Q("Quit");
            quitButton.RegisterCallback<ClickEvent>(QuitButton);
            saveButton = UIDocument.rootVisualElement.Q("Menu").Q("SaveButton");
            saveButton.RegisterCallback<ClickEvent>(SaveButton);

            // ---------- Lobby ----------
            playerList = UIDocument.rootVisualElement.Q("Lobby").Q("Players").Q("Background");
            playerList.RegisterCallback<ClickEvent>(PlayerListHandler);

            // Buttons
            startButton = UIDocument.rootVisualElement.Q("Lobby").Q("StartGame");
            startButton.RegisterCallback<ClickEvent>(StartButton);
            cancelButton = UIDocument.rootVisualElement.Q("Lobby").Q("Cancel");
            cancelButton.RegisterCallback<ClickEvent>(CancelButton);

            // ---------- LOAD ----------
            saveList = UIDocument.rootVisualElement.Q("LoadMenu").Q("SaveList");
            saveList.RegisterCallback<ClickEvent>(SaveListHandler);
            cancelButtonLoad = UIDocument.rootVisualElement.Q("LoadMenu").Q("Cancel");
            cancelButtonLoad.RegisterCallback<ClickEvent>(CancelButtonLoad);

            // ---------- WIN MENU ----------
            UIDocument.rootVisualElement.Q("WinMenu").RegisterCallback<ClickEvent>(WinMenuHandler);

            // ----- MSG and Chatbox -----
            chatBox = (ScrollView)UIDocument.rootVisualElement.Q("Chat").Q("ChatBox");
            msgInput = (TextField)UIDocument.rootVisualElement.Q("Chat").Q("MsgInput");
            msgInput.RegisterCallback<KeyDownEvent>(ChatOnKeyDown, TrickleDown.TrickleDown);

            // [Interflow fix 2026-06-26 путь1] Меню-UI построено — презентация готова. На сервере сюда не доходим (init-гейт в начале Start).
            presentationReady = true;
        }

        // MENU -----------------------------------------------------------------------------------------------------------------------------------------------------------------------

        void HostButton(ClickEvent evt)
        {
            chatBox.Clear();
            TextField tf = (TextField)UIDocument.rootVisualElement.Q("Menu").Q("PlayerName");
            NetworkConnectionHandler.instance.StartHost(tf.value);
        }

        void JoinButton(ClickEvent evt)
        {
            chatBox.Clear();
            TextField ip = (TextField)UIDocument.rootVisualElement.Q("Menu").Q("IpInput");
            TextField port = (TextField)UIDocument.rootVisualElement.Q("Menu").Q("PortInput");
            TextField tf = (TextField)UIDocument.rootVisualElement.Q("Menu").Q("PlayerName");
            NetworkConnectionHandler.instance.StartClient(tf.value, ip.value, port.value);
        }

        void SaveButton(ClickEvent evt)
        {
            SaveManager.SaveToFile();
        }

        void QuitButton(ClickEvent evt)
        {
            if (currentMenuIndex == 0) Application.Quit();
            if (NetworkManager.Singleton.IsListening) NetworkConnectionHandler.instance.Shutdown();
        }

        // LOBBY -----------------------------------------------------------------------------------------------------------------------------------------------------------------------

        void PlayerListHandler(ClickEvent evt)
        {
            VisualElement target = evt.target as VisualElement;
            bool loadingSaveFile = SceneHandler.instance.saveFileName != "";

            // Disconnect
            if (target.name.StartsWith("Remove"))
            {
                if (int.TryParse(target.parent.name, out int playerSlot))
                {
                    SlotManager.instance.DisconnectPlayer(playerSlot);
                }
            }
            else if (loadingSaveFile == false)
            {
                // TeamName
                if (target.name.StartsWith("TeamName"))
                {
                    if (int.TryParse(target.name.Replace("TeamName", ""), out int teamIndex))
                    {
                        if (NetworkConnectionHandler.isClient)
                        {
                            // Network
                            NetworkDataSync.instance.SwapTeamToServerRpc(teamIndex);
                        }
                        else
                        {
                            // Single player or Server
                            SlotManager.instance.SwapTeamTo((ulong)SlotManager.instance.playerID[SlotManager.instance.currentPlayer], teamIndex);
                        }
                    }
                }
                // Team Menu selection
                else if (target.name.StartsWith("team"))
                {
                    if (int.TryParse(target.parent.name, out int playerSlot))
                    {
                        if (playerSlot == SlotManager.instance.currentPlayer || !NetworkConnectionHandler.isClient)
                        {
                            // Fill possible teams
                            floatingMenu.Clear();

                            for (int i = 0; i < SlotManager.instance.GetTotalNumberOfPlayers(); i++)
                            {
                                FloatingMenuEntry("t" + playerSlot + "_" + i, "Team:" + i);
                            }
                            ShowFloatingMenu();
                        }
                    }
                }
                // Faction
                else if (target.name.StartsWith("faction"))
                {
                    if (int.TryParse(target.parent.name, out int playerSlot))
                    {
                        if (playerSlot == SlotManager.instance.currentPlayer || !NetworkConnectionHandler.isClient)
                        {
                            // Fill possible teams
                            floatingMenu.Clear();
                            SceneData sceneData = SceneHandler.instance.sceneData[SceneHandler.instance.sceneIndex];
                            FloatingMenuEntry("f" + playerSlot + "_0", "Random");
                            for (int i = 0; i < sceneData.factions.Length; i++)
                            {
                                FloatingMenuEntry("f" + playerSlot + "_" + (i + 1), sceneData.factions[i]);
                            }
                            ShowFloatingMenu();
                        }
                    }
                }
                // Spawn position
                else if (target.name.StartsWith("spawn"))
                {
                    if (int.TryParse(target.parent.name, out int playerSlot))
                    {
                        if (playerSlot == SlotManager.instance.currentPlayer || !NetworkConnectionHandler.isClient)
                        {
                            // Fill possible teams
                            floatingMenu.Clear();
                            SceneData sceneData = SceneHandler.instance.sceneData[SceneHandler.instance.sceneIndex];
                            FloatingMenuEntry("s" + playerSlot + "_0", "Random");
                            for (int i = 0; i < sceneData.spawnPointCount; i++)
                            {
                                FloatingMenuEntry("s" + playerSlot + "_" + (i + 1), "Spawn: " + i);
                            }
                            ShowFloatingMenu();
                        }
                    }
                }
            }
        }

        // Start button
        void StartButton(ClickEvent evt)
        {
            if (NetworkConnectionHandler.isClient) return;

            SlotManager.instance.RandomizeSlotData();
            // Before loading the scene (starting the game) we must send the save data if it exists
            if (SceneHandler.instance.saveFileName != "")
            {
                NetworkConnectionHandler.instance.connectionStage = 1;
                SceneHandler.instance.SceneStartedLoading();

                // Add all clients to the waiting list
                NetworkConnectionHandler.instance.AddClientsToWaitingList(true);
                // No one to send, start the game
                if (NetworkConnectionHandler.instance.clientsLoading.Count == 0)
                {
                    NetworkConnectionHandler.instance.clientsListUpdated += NetworkDataSync.instance.ServerPlayersFinishedLoading;
                    SceneHandler.instance.LoadScene();
                }
                // Send Data to clients
                else NetworkDataSync.instance.SendSaveSceneData(SceneHandler.instance.saveFileName, true);
            }
            else
            {
                // No save data, start immediately
                SceneHandler.instance.LoadScene();
            }
        }

        // Cancel button
        void CancelButton(ClickEvent evt)
        {
            SceneHandler.instance.saveFileName = "";
            NetworkConnectionHandler.instance.Shutdown();
        }

        // WIN MENU -----------------------------------------------------------------------------------------------------------------------------------------------------------------------

        void WinMenuHandler(ClickEvent evt)
        {
            VisualElement target = evt.target as VisualElement;

            if (target.name.StartsWith("Continue"))
            {
                HideUIDocument();
            }
            else if (target.name.StartsWith("Quit"))
            {
                if (NetworkManager.Singleton.IsListening) NetworkConnectionHandler.instance.Shutdown();
            }
        }

        // CHAT ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        public void ChatOnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter || evt.character == '\n')
            {
                // Prevent the default behavior
                evt.StopPropagation();

                string msg = msgInput.value.Trim();
                if (msg != "")
                {
                    msg = msg.Length > msgInput.maxLength ? msg.Substring(0, msgInput.maxLength) : msg;
                    AddChatMsg(msgInput.value.Trim(), SlotManager.instance.currentPlayer);
                    // Send info to other players
                    if (NetworkDataSync.instance) NetworkDataSync.instance.MsgSend(msg, false);
                }

                // Clear and Focus
                msgInput.value = string.Empty;
                // StartCoroutine(ChatFocus());
            }
        }

        // Add message to chatbox
        public void AddChatMsg(string msg, int owner)
        {
            VisualElement wrapper1 = new VisualElement();
            wrapper1.style.flexDirection = FlexDirection.Row;
            chatBox.Add(wrapper1);

            Label playerName = new Label();
            playerName.text = SlotManager.instance.playerName[owner] + ": ";
            playerName.style.color = SlotManager.instance.playerColors[owner];
            wrapper1.Add(playerName);

            VisualElement wrapper2 = new VisualElement();
            wrapper1.Add(wrapper2);

            Label msgLabel = new Label();
            msgLabel.text = msg;
            wrapper2.Add(msgLabel);

            if (chatBox.childCount > 40)
            {
                chatBox.RemoveAt(0);
            }

            // Cheats
            if (SlotManager.instance.debugMode) Cheats.MsgAdded(msg, owner);
            // Scroll to bottom
            StartCoroutine(ChatScrollToBottom());
        }

        // Add message to chatbox
        public void AddChatServerMsg(string msg)
        {
            // [Interflow fix 2026-08-01 ADR-005] Серверный релей перенесён в Presentation.MenuChatServerMsg/ChatServerMsgAuto (хаб) — метод стал чисто локальной отрисовкой.

            // [Interflow fix 2026-06-26 путь1] Дальше — только локальная презентация чата; на сервере UI нет (presentationReady=false).
            if (!presentationReady) return;
            VisualElement wrapper1 = new VisualElement();
            wrapper1.style.flexDirection = FlexDirection.Row;
            chatBox.Add(wrapper1);

            Label playerName = new Label();
            playerName.text = "Server: ";
            playerName.style.color = Color.gray;
            wrapper1.Add(playerName);

            VisualElement wrapper2 = new VisualElement();
            wrapper1.Add(wrapper2);

            Label msgLabel = new Label();
            msgLabel.text = msg;
            wrapper2.Add(msgLabel);

            if (chatBox.childCount > 40)
            {
                chatBox.RemoveAt(0);
            }

            // Scroll to bottom
            StartCoroutine(ChatScrollToBottom());
        }

        // Focus on msgInput with a 1 frame delay
        IEnumerator ChatFocus()
        {
            // Wait for 1 frame
            yield return null;
            msgInput.Blur();
            msgInput.Focus();
        }

        // Chat scroll to bottom with a 1 frame delay
        IEnumerator ChatScrollToBottom()
        {
            // Wait for 1 frame
            yield return null;
            chatBox.verticalScroller.value = chatBox.verticalScroller.highValue;
        }

        // LOBBY - PLAYER LIST ------------------------------------------------------------------------------------------

        // Initializes the player list
    }
}
