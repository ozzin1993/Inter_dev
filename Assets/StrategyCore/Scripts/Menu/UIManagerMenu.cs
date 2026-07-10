using Camera_TopDownNS;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StrategyCore
{
    public class UIManagerMenu : MonoBehaviour
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
            // [Interflow fix 2026-06-26 путь1] Релей серверо-авторитетен (правило 6) и идёт РАНЬШЕ презентации (как ShowNotifyMsg/FloatingText):
            // сервер всегда рассылает серверное сообщение клиентам, в т.ч. на headless. На клиенте IsServer=false → дубля/петли нет.
            if (NetworkDataSync.instance && NetworkManager.Singleton.IsServer) NetworkDataSync.instance.ServerMsgSend(msg);

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
        public void FillPlayerList()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            playerList.Clear();

            if (SceneHandler.instance.sceneData == null || SceneHandler.instance.sceneIndex >= SceneHandler.instance.sceneData.Length) return;
            SceneData sceneData = SceneHandler.instance.sceneData[SceneHandler.instance.sceneIndex];
            int currentPlayerIndex = 0;

            for (int t = 0; t < sceneData.teamsAndPlayers.Length; t++)
            {
                // Add team name
                Label teamname = new Label();
                teamname.name = "TeamName" + t;
                teamname.text = sceneData.teamsAndPlayers[t].teamName;
                teamname.AddToClassList("TeamName");
                if (t != 0) teamname.style.marginTop = 15;
                playerList.Add(teamname);

                for (int i = 0; i < sceneData.teamsAndPlayers[t].players.Length; i++)
                {
                    if (!sceneData.teamsAndPlayers[t].players[i].dontShow)
                    {
                        if (SlotManager.instance.slotType[currentPlayerIndex] == SlotType.Player || SlotManager.instance.slotType[currentPlayerIndex] == SlotType.Bot)
                        {
                            // Bot or Player
                            // bool currentPlayer = (!NetworkConnectionHandler.isClient || SlotManager.instance.currentPlayer == currentPlayerIndex);
                            // int team = (sceneData.chooseTeams && currentPlayer) ? SlotManager.instance.playerTeam[currentPlayerIndex] : -1;
                            string spawnPos = null;
                            if (sceneData.spawnPointCount != 0)
                            {
                                if (SlotManager.instance.playerPosition[currentPlayerIndex] == 0) spawnPos = "Random";
                                else spawnPos = (SlotManager.instance.playerPosition[currentPlayerIndex] - 1).ToString();
                            }
                            string faction = null;
                            if (sceneData.factions != null && sceneData.factions.Length > 0)
                            {
                                if (SlotManager.instance.playerFaction[currentPlayerIndex] == 0) faction = "Random";
                                else faction = sceneData.factions[SlotManager.instance.playerFaction[currentPlayerIndex] - 1];
                            }
                            ListEntryAdd(i, currentPlayerIndex.ToString(), SlotManager.instance.playerName[currentPlayerIndex], SlotManager.instance.playerTeam[currentPlayerIndex], faction, spawnPos, playerList, false);
                        }
                        else
                        {
                            // Empty
                            ListEntryAdd(i, currentPlayerIndex.ToString(), null, -1, null, null, playerList, true);
                        }
                    }
                    currentPlayerIndex++;
                }
            }
        }

        public void ListEntryAdd(int styleIndex, string name, string text, int team, string faction, string spawnPos, VisualElement parent, bool empty = false)
        {
            if (empty)
            {
                // Add empty slots
                Label emptySlot = new Label();
                emptySlot.text = "EMPTY SLOT";
                emptySlot.AddToClassList("PlayerSlot");
                emptySlot.AddToClassList("emptySlot");
                if (styleIndex != 0) emptySlot.style.marginTop = 8;
                parent.Add(emptySlot);
            }
            else
            {
                // Entry
                GroupBox entry = new GroupBox();
                entry.AddToClassList("ListEntry");
                entry.name = name.ToString();
                if (styleIndex != 0) entry.style.marginTop = 8;
                parent.Add(entry);

                // NameBox
                GroupBox nameBox = new GroupBox();
                nameBox.AddToClassList("ItemButton");
                nameBox.AddToClassList("SlotClass");
                nameBox.name = "Name";
                entry.Add(nameBox);

                Label label = new Label();
                label.text = text;
                label.name = "Name";
                label.pickingMode = PickingMode.Ignore;
                nameBox.Add(label);

                // Factions
                if (faction != null)
                {
                    Label factionLbl = new Label();
                    factionLbl.AddToClassList("ItemButton");
                    factionLbl.AddToClassList("SlotClass");
                    factionLbl.AddToClassList("SlotExtraButton");
                    factionLbl.name = "faction" + styleIndex;
                    factionLbl.text = "Faction: " + faction;
                    entry.Add(factionLbl);
                }

                // Spawn positions
                if (spawnPos != null)
                {
                    Label spawnPosLbl = new Label();
                    spawnPosLbl.AddToClassList("ItemButton");
                    spawnPosLbl.AddToClassList("SlotClass");
                    spawnPosLbl.AddToClassList("SlotExtraButton");
                    spawnPosLbl.name = "spawn" + styleIndex;
                    spawnPosLbl.text = "Spawn Position: " + spawnPos;
                    entry.Add(spawnPosLbl);
                }

                // Team selection
                Label teamLabel = new Label();
                teamLabel.AddToClassList("ItemButton");
                teamLabel.AddToClassList("SlotClass");
                teamLabel.AddToClassList("SlotExtraButton");
                teamLabel.name = "team" + styleIndex;
                teamLabel.text = "Team: " + team;
                entry.Add(teamLabel);

                // Remove button / Disconnect
                if (!NetworkConnectionHandler.isClient)
                {
                    GroupBox removeBox = new GroupBox();
                    removeBox.name = "Remove";
                    removeBox.AddToClassList("ItemButton");
                    removeBox.AddToClassList("SlotClass");
                    removeBox.AddToClassList("RemoveButton");
                    entry.Add(removeBox);
                }
            }
        }

        public void SaveListEntryAdd(int styleIndex, string name, string text, VisualElement parent)
        {
            // Entry
            GroupBox entry = new GroupBox();
            entry.AddToClassList("ListEntry");
            entry.name = name.ToString();
            if (styleIndex != 0) entry.style.marginTop = 8;
            parent.Add(entry);

            // NameBox
            GroupBox nameBox = new GroupBox();
            nameBox.AddToClassList("ItemButton");
            nameBox.AddToClassList("SlotClass");
            nameBox.name = "Name";
            entry.Add(nameBox);

            Label label = new Label();
            label.text = text;
            label.name = "Name";
            label.pickingMode = PickingMode.Ignore;
            nameBox.Add(label);

            // Load
            Label teamLabel = new Label();
            teamLabel.AddToClassList("ItemButton");
            teamLabel.AddToClassList("SlotClass");
            teamLabel.AddToClassList("SlotExtraButton");
            teamLabel.name = "Load";
            teamLabel.text = "Load";
            entry.Add(teamLabel);

            // Remove
            GroupBox removeBox = new GroupBox();
            removeBox.name = "Remove";
            removeBox.AddToClassList("ItemButton");
            removeBox.AddToClassList("SlotClass");
            removeBox.AddToClassList("RemoveButton");
            entry.Add(removeBox);
        }

        // FLOATING MENU -----------------------------------------------------------------------------------------------------------------------------------------------------------------------

        public void ShowFloatingMenu()
        {
            Vector2 mousePositionCorrected = CursorToUIposition();

            floatingMenu.style.left = mousePositionCorrected.x;
            floatingMenu.style.top = mousePositionCorrected.y;

            floatingMenu.style.display = DisplayStyle.Flex;

            UIDocument.rootVisualElement.RegisterCallback<PointerUpEvent>(FloatingMenuHide);
        }

        public void FloatingMenuHandler(ClickEvent evt)
        {
            VisualElement target = evt.target as VisualElement;

            // Team selection
            if (target.name.StartsWith("t"))
            {
                string[] player_team = target.name.Replace("t", "").Split("_");
                if (int.TryParse(player_team[0], out int playerSlot))
                {
                    if (int.TryParse(player_team[1], out int team))
                    {
                        if (NetworkConnectionHandler.isClient)
                        {
                            // Network
                            NetworkDataSync.instance.ChangeTeamToServerRpc(team);
                        }
                        else
                        {
                            // Single player or Server
                            SlotManager.instance.ChangeTeamTo((ulong)SlotManager.instance.playerID[playerSlot], team);
                        }
                    }
                }
            }
            else if (target.name.StartsWith("f"))
            {
                string[] player_team = target.name.Replace("f", "").Split("_");
                if (int.TryParse(player_team[0], out int playerSlot))
                {
                    if (int.TryParse(player_team[1], out int index))
                    {
                        if (NetworkConnectionHandler.isClient)
                        {
                            // Network
                            NetworkDataSync.instance.ChangeFactionToServerRpc(index);
                        }
                        else
                        {
                            // Single player or Server
                            SlotManager.instance.ChangeFactionTo((ulong)SlotManager.instance.playerID[playerSlot], index);
                        }
                    }
                }
            }
            else if (target.name.StartsWith("s"))
            {
                string[] player_team = target.name.Replace("s", "").Split("_");
                if (int.TryParse(player_team[0], out int playerSlot))
                {
                    if (int.TryParse(player_team[1], out int index))
                    {
                        if (NetworkConnectionHandler.isClient)
                        {
                            // Network
                            NetworkDataSync.instance.ChangeSpawnToServerRpc(index);
                        }
                        else
                        {
                            // Single player or Server
                            SlotManager.instance.ChangeSpawnPositionTo((ulong)SlotManager.instance.playerID[playerSlot], index);
                        }
                    }
                }
            }

            UIDocument.rootVisualElement.UnregisterCallback<PointerUpEvent>(FloatingMenuHide);
            floatingMenu.style.display = DisplayStyle.None;
        }

        public void FloatingMenuEntry(string name, string text)
        {
            Label entry = new Label();
            entry.name = name;
            entry.text = text;
            entry.AddToClassList("inventoryMenu");
            entry.AddToClassList("FloatingMenuEntry");
            floatingMenu.Add(entry);
        }

        public void FloatingMenuHide(PointerUpEvent evt)
        {
            VisualElement target = evt.target as VisualElement;

            if (target.ClassListContains("FloatingMenuEntry")) return;
            else
            {
                UIDocument.rootVisualElement.UnregisterCallback<PointerUpEvent>(FloatingMenuHide);
                floatingMenu.style.display = DisplayStyle.None;
            }
        }

        // LOAD GAME -----------------------------------------------------------------------------------------------------------------------------------------------------------------------

        void FillSaveList()
        {
            saveList.Clear();

            string[] files = SaveManager.GetAvailableSaveFiles();
            if (files != null)
            {
                for (int i = 0; i < files.Length; i++)
                {
                    SaveListEntryAdd(i, files[i], SaveManager.GetFileName(files[i]), saveList);
                }
            }
        }

        // Load button in the menu
        void LoadButton(ClickEvent evt)
        {
            FillSaveList();
            ShowMenuLobby(3);
        }

        // Go back to menu button
        void CancelButtonLoad(ClickEvent evt)
        {
            ShowMenuLobby(0);
        }

        // Save list - load / remove
        void SaveListHandler(ClickEvent evt)
        {
            VisualElement target = evt.target as VisualElement;

            // Load
            if (target.name.StartsWith("Load"))
            {
                Debug.Log("Loading " + target.parent.name);
                // NO TEAM CHANGE ?? WHEN LOADING A SCENE
                // Get the scene name
                string saveName = SaveManager.GetFileName(target.parent.name);
                int index = saveName.IndexOf("_");
                string sceneName = (index >= 0) ? saveName.Substring(0, index) : saveName;

                // Set active scene
                if (SceneHandler.instance.SetActiveScene(sceneName))
                {
                    // Start host
                    TextField tf = (TextField)UIDocument.rootVisualElement.Q("Menu").Q("PlayerName");
                    NetworkConnectionHandler.instance.StartHost(tf.value);

                    // Change the player data
                    SaveManager.LoadSlotManagerData(target.parent.name, true);

                    // Save file name
                    SceneHandler.instance.saveFileName = target.parent.name;

                    chatBox.Clear();
                }
            }
            // Delete
            else if (target.name.StartsWith("Remove"))
            {
                if (SaveManager.DeleteSaveFile(target.parent.name, true))
                {
                    FillSaveList();
                }
            }
        }

        // UTILS ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Hide UIDocument
        public void HideUIDocument()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            UIDocument.rootVisualElement.style.display = DisplayStyle.None;
        }

        public void ShowUIDocument()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            // InGame Menu
            if (SlotManager.instance.gameStarted == GameState.Started)
            {
                // Toggle
                if (UIDocument.rootVisualElement.style.display == DisplayStyle.Flex)
                {
                    HideUIDocument();
                }
                else
                {
                    ShowMenuLobby(4);
                    UIDocument.rootVisualElement.style.display = DisplayStyle.Flex;
                }
            }
            else
            {
                UIDocument.rootVisualElement.style.display = DisplayStyle.Flex;
            }
        }

        // Show Menu/Lobby
        public void ShowMenuLobby(int i)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            currentMenuIndex = i;
            if (i == 0) // Menu
            {
                UIDocument.rootVisualElement.Q("Menu").style.display = DisplayStyle.Flex;
                UIDocument.rootVisualElement.Q("Menu").Q("MenuButtons").style.display = DisplayStyle.Flex;
                UIDocument.rootVisualElement.Q("Menu").Q("Connecting").style.display = DisplayStyle.None;
                saveButton.style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Lobby").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("LoadMenu").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("WinMenu").style.display = DisplayStyle.None;
            }
            else if (i == 1) // Lobby
            {
                UIDocument.rootVisualElement.Q("Menu").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Lobby").style.display = DisplayStyle.Flex;
                if (!NetworkConnectionHandler.isClient) UIDocument.rootVisualElement.Q("Lobby").Q("StartGame").style.display = DisplayStyle.Flex;
                else UIDocument.rootVisualElement.Q("Lobby").Q("StartGame").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("LoadMenu").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("WinMenu").style.display = DisplayStyle.None;
            }
            else if (i == 2) // Connecting menu
            {
                UIDocument.rootVisualElement.Q("Menu").style.display = DisplayStyle.Flex;
                UIDocument.rootVisualElement.Q("Menu").Q("MenuButtons").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Menu").Q("Connecting").style.display = DisplayStyle.Flex;
                saveButton.style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Lobby").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("LoadMenu").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("WinMenu").style.display = DisplayStyle.None;
            }
            else if (i == 3) // Load menu 
            {
                UIDocument.rootVisualElement.Q("Menu").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Lobby").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("LoadMenu").style.display = DisplayStyle.Flex;
                UIDocument.rootVisualElement.Q("WinMenu").style.display = DisplayStyle.None;
            }
            else if (i == 4) // InGame Menu
            {
                UIDocument.rootVisualElement.Q("Menu").style.display = DisplayStyle.Flex;
                UIDocument.rootVisualElement.Q("Menu").Q("MenuButtons").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Menu").Q("Connecting").style.display = DisplayStyle.None;
                if (!NetworkConnectionHandler.isClient) saveButton.style.display = DisplayStyle.Flex;
                else saveButton.style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Lobby").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("LoadMenu").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("WinMenu").style.display = DisplayStyle.None;
            }
            else if (i == 5 || i == 6) // You won menu
            {
                UIDocument.rootVisualElement.Q("Menu").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Menu").Q("MenuButtons").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Menu").Q("Connecting").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Lobby").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("LoadMenu").style.display = DisplayStyle.None;

                VisualElement winMenu = UIDocument.rootVisualElement.Q("WinMenu");
                winMenu.style.display = DisplayStyle.Flex;

                Label lbl = (Label)winMenu.Q("Txt");
                if (i == 5)
                {
                    lbl.text = "YOU WON!";
                }
                else
                {
                    lbl.text = "YOU LOST!";
                }
                UIDocument.rootVisualElement.style.display = DisplayStyle.Flex;
            }
        }

        // Returns cursors position in UI space
        Vector2 CursorToUIposition()
        {
            Vector2 mousePosition = Mouse.current.position.ReadValue();
            Vector2 mousePositionCorrected = new Vector2(mousePosition.x + 10, Screen.height - mousePosition.y + 10);
            mousePositionCorrected = RuntimePanelUtils.ScreenToPanel(UIDocument.rootVisualElement.panel, mousePositionCorrected);
            return mousePositionCorrected;
        }
    }
}
