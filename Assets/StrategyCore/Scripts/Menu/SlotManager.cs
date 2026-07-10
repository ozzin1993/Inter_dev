using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    public enum GameState
    {
        Menu,
        Loading,
        Started
    }

    public class SlotManager : MonoBehaviour
    {
        public static SlotManager instance;

        [HideInInspector] public bool gameOn = false; // Only for Unit Update check, for checking the game state use variable below!
        public GameState gameStarted = GameState.Menu; // If game has been started, or are we still in the lobby
        public Action OnGameStart; // Fired when all loading operations are finished, to initialize all units 

        [Tooltip("In Debug mode you can control any unit and use cheats")]
        public bool debugMode = false;

        // NET ID
        public Dictionary<UInt16, Unit> unitNetID = new Dictionary<UInt16, Unit>(); // networkID to Unit referencer

        [Header("PLAYER DATA")] // This data will change depending on Technical data that is presented below (slotType, playerID, playerTeam, playerName)
        [Tooltip("Which player currently is playing")]
        public int currentPlayer = 0;
        public string currentName = "Player"; // Store current player`s name
        public int currentTeam = -1; // Stores current team
        // TODO below
        public int spawnIndex = 0;
        public int factionIndex = 0;

        // Player Colors
        public Color[] playerColors = new Color[14] {
            new Color(255f / 255f, 3f / 255f, 3f / 255f),    // Red
            new Color(0f / 255f, 66f / 255f, 255f / 255f),   // Blue 
            new Color(27f / 255f, 231f / 255f, 186f / 255f), // Teal
            new Color(254f / 255f, 252f / 255f, 0f / 255f),  // Yellow
            new Color(254f / 255f, 137f / 255f, 13f / 255f), // Orange
            new Color(33f / 255f, 191f / 255f, 0f / 255f),   // Green
            new Color(228f / 255f, 92f / 255f, 175f / 255f), // Pink
            new Color(148f / 255f, 150f / 255f, 150f / 255f),// Gray
            new Color(126f / 255f, 191f / 255f, 241f / 255f),// Light blue
            new Color(236f / 255f, 206f / 255f, 135f / 255f),// Wheat
            new Color(247f / 255f, 165f / 255f, 139f / 255f),// Peach
            new Color(191f / 255f, 255f / 255f, 129f / 255f),// Mint
            new Color(165f / 255f, 111f / 255f, 52f / 255f), // Peanut - neutral passive
            new Color(46f / 255f, 45f / 255f, 46f / 255f)    // Black - neutral active
        };

        // Technical
        [Tooltip("[slot index] = 0 empty, 1 player, 2 bot")]
        [HideInInspector] public SlotType[] slotType = new SlotType[Enum.GetNames(typeof(Players)).Length]; // Stores if slot at index is 0 empty, 1 player, 2 bot
        [Tooltip("[slot index] = player network id")]
        [HideInInspector] public int[] playerID = new int[Enum.GetNames(typeof(Players)).Length]; // Stores the network id for players
        [Tooltip("[slot index] = team index.")]
        [HideInInspector] public int[] playerTeam = new int[Enum.GetNames(typeof(Players)).Length]; // Stores which player belongs to which team: Index is slot index, value is TeamIndex.
        [Tooltip("[slot index] = player name.")]
        [HideInInspector] public string[] playerName = new string[Enum.GetNames(typeof(Players)).Length]; // Stores player names. Index is slot index.

        [HideInInspector] public int[] playerPosition = new int[Enum.GetNames(typeof(Players)).Length]; // 0 means random, so the first index is 1
        [HideInInspector] public int[] playerFaction = new int[Enum.GetNames(typeof(Players)).Length]; // 0 means random, so the first index is 1
        [HideInInspector] public bool[] playerLost = new bool[Enum.GetNames(typeof(Players)).Length];

        private bool instanceSet = false;

        void Awake()
        {
            InstanceSet();
        }

        // ============================= INITIALIZE ==============================================================================

        /// <summary>
        /// Sets an instance of the slot manager, can be called also by other scripts.
        /// </summary>
        public void InstanceSet()
        {
            if (instance == null)
            {
                // Directly playing the scene
                instance = this;
                instanceSet = true;

                // Enable EventSystem
                var es = FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include);

                if (es != null) es.gameObject.SetActive(true);
                else Debug.LogWarning("Are you sure you have EventSystem in your scene?");
            }
            else if (instanceSet == false)
            {
                // Coming from menu
                // Remove the duplicate project manager
                Destroy(this.gameObject);
            }
        }

        /// <summary>
        /// Initializes player = team relations depending on the scene data of the active scene.
        /// </summary>
        public void InitializeSlotData()
        {
            slotType = new SlotType[Enum.GetNames(typeof(Players)).Length];
            playerID = new int[Enum.GetNames(typeof(Players)).Length];
            playerTeam = new int[Enum.GetNames(typeof(Players)).Length];
            playerName = new string[Enum.GetNames(typeof(Players)).Length];
            playerPosition = new int[Enum.GetNames(typeof(Players)).Length];
            playerFaction = new int[Enum.GetNames(typeof(Players)).Length];
            playerLost = new bool[Enum.GetNames(typeof(Players)).Length];

            // Default player ID values
            for (int i = 0; i < playerID.Length; i++) playerID[i] = -1;

            if (SceneHandler.instance.sceneData == null || SceneHandler.instance.sceneIndex >= SceneHandler.instance.sceneData.Length) return;
            SceneData sceneData = SceneHandler.instance.sceneData[SceneHandler.instance.sceneIndex];
            int currentPlayerIndex = 0;

            // User defined data
            for (int t = 0; t < sceneData.teamsAndPlayers.Length; t++)
            {
                for (int i = 0; i < sceneData.teamsAndPlayers[t].players.Length; i++)
                {
                    // Bot data
                    if (sceneData.teamsAndPlayers[t].players[i].isBot)
                    {
                        if (sceneData.teamsAndPlayers[t].players[i].dontShow) SlotManager.instance.slotType[currentPlayerIndex] = SlotType.BotHidden;
                        else SlotManager.instance.slotType[currentPlayerIndex] = SlotType.Bot;
                        SlotManager.instance.playerName[currentPlayerIndex] = sceneData.teamsAndPlayers[t].players[i].botName;
                    }

                    if (sceneData.chooseTeams) SlotManager.instance.playerTeam[currentPlayerIndex] = currentPlayerIndex;
                    else SlotManager.instance.playerTeam[currentPlayerIndex] = t;
                    SlotManager.instance.playerName[i] = "Player " + i;
                    currentPlayerIndex++;
                }
            }

            // Fill the rest of the player-team relations
            SlotManager.instance.playerTeam[(int)Players.NeutralPassive] = (int)Teams.NeutralPassive;
            SlotManager.instance.playerName[(int)Players.NeutralPassive] = "Neutral Passive";
            SlotManager.instance.playerTeam[(int)Players.NeutralActive] = (int)Teams.NeutralActive;
            SlotManager.instance.playerName[(int)Players.NeutralActive] = "Neutral Active";

            for (int i = currentPlayerIndex; i < Enum.GetNames(typeof(Players)).Length - 2; i++)
            {
                // Assign names
                SlotManager.instance.playerName[i] = "Player " + i;

                // Assign teams
                SlotManager.instance.playerTeam[i] = i;
            }
        }

        // Before the start of the game, if we need to randomize factions/spawn positions we do it here
        public void RandomizeSlotData()
        {
            SceneData sceneData = SceneHandler.instance.sceneData[SceneHandler.instance.sceneIndex];
            for (int i = 0; i < SlotManager.instance.slotType.Length; i++)
            {
                if (SlotManager.instance.slotType[i] != SlotType.Empty)
                {
                    // Randomize the faction for this player, or reduce the index by one since 0 is random
                    if (sceneData.factions != null && sceneData.factions.Length > 0)
                    {
                        if (SlotManager.instance.playerFaction[i] == 0)
                        {
                            SlotManager.instance.playerFaction[i] = UnityEngine.Random.Range(0, sceneData.factions.Length);
                        }
                        else SlotManager.instance.playerFaction[i]--;
                    }

                    // Randomize spawn position if needed, or reduce the index by one since 0 is random
                    if (sceneData.spawnPointCount > 0)
                    {
                        if (SlotManager.instance.playerPosition[i] == 0)
                            SlotManager.instance.playerPosition[i] = UnityEngine.Random.Range(0, sceneData.spawnPointCount);
                        else SlotManager.instance.playerPosition[i]--;
                    }
                }
            }
            if (NetworkDataSync.instance) NetworkDataSync.instance.PlayerListSend(0, true);
        }

        // ============================= WIN LOSE ==============================================================================

        // Called when current player wins
        public void PlayerWins()
        {
            if (UIManagerMenu.instance)
            {
                UIManagerMenu.instance.ShowMenuLobby(5);
            }
            else
            {
                Debug.Log("You won the game!");
            }
        }

        // Called when current player loses
        public void PlayerLoses()
        {
            UIManager.instance.ShowNotifyMsg("You lost, but your team is still playing!");
        }

        // Game is over for the current player, no playing teammates left
        public void TeamLoses()
        {
            if (UIManagerMenu.instance)
            {
                UIManagerMenu.instance.ShowMenuLobby(6);
            }
            else
            {
                Debug.Log("You lost the game!");
            }
        }

        // ============================= COMMANDS ==============================================================================

        /// <summary>
        /// Sets the current game state.
        /// </summary>
        /// <param name="gameState">Desired game state.</param>
        public void SetGameState(GameState gameState)
        {
            gameStarted = gameState;
            if (gameState == GameState.Started) gameOn = true;
            else gameOn = false;
        }

        /// <summary>
        /// Adds a new client to the first empty slot. Returns index of the player`s slot.
        /// </summary>
        /// <param name="clientID">Client`s network ID.</param>
        /// <param name="playerName">Player`s name.</param>
        /// <returns>Index of the player`s slot. -1 if no slots are available.</returns>
        public int AddClientID(ulong clientID, string playerName)
        {
            if (SceneHandler.instance.sceneData == null || SceneHandler.instance.sceneIndex >= SceneHandler.instance.sceneData.Length) return -1;
            SceneData sceneData = SceneHandler.instance.sceneData[SceneHandler.instance.sceneIndex];

            // Find empty slot
            int currentPlayerIndex = 0;
            for (int t = 0; t < sceneData.teamsAndPlayers.Length; t++)
            {
                for (int i = 0; i < sceneData.teamsAndPlayers[t].players.Length; i++)
                {
                    if (SlotManager.instance.slotType[currentPlayerIndex] == SlotType.Empty)
                    {
                        SlotManager.instance.slotType[currentPlayerIndex] = SlotType.Player;
                        SlotManager.instance.playerID[currentPlayerIndex] = (int)clientID;
                        SlotManager.instance.playerName[currentPlayerIndex] = playerName;

                        return currentPlayerIndex;
                    }
                    currentPlayerIndex++;
                }
            }

            return -1; // No slots available
        }

        /// <summary>
        /// Clears client`s slot data. Called when client is disconnected.
        /// </summary>
        /// <param name="clientID">Client`s network ID.</param>
        public void RemoveClientID(ulong clientID)
        {
            for (int i = 0; i < SlotManager.instance.playerID.Length; i++)
            {
                if (SlotManager.instance.playerID[i] == (int)clientID && SlotManager.instance.slotType[i] == SlotType.Player)
                {
                    SlotManager.instance.slotType[i] = SlotType.Empty;
                    SlotManager.instance.playerID[i] = -1;
                    SlotManager.instance.playerName[i] = "";
                    return;
                }
            }
        }

        /// <summary>
        /// Disconnects the player/bot at specified slot.
        /// </summary>
        /// <param name="slot">Slot index.</param>
        public void DisconnectPlayer(int slot)
        {
            if (NetworkManager.Singleton.IsServer) // Server
            {
                if (slot != SlotManager.instance.currentPlayer) // Not host
                {
                    if (SlotManager.instance.slotType[slot] == SlotType.Player)
                    {
                        NetworkManager.Singleton.DisconnectClient((ulong)SlotManager.instance.playerID[slot]);
                        NetworkDataSync.instance.PlayerListSend(0, true);
                    }
                    else if (SlotManager.instance.slotType[slot] != SlotType.Empty)
                    {
                        SlotManager.instance.slotType[slot] = SlotType.Empty;
                        SlotManager.instance.playerID[slot] = -1;
                        SlotManager.instance.playerName[slot] = "";

                        UIManagerMenu.instance.FillPlayerList();
                        if (NetworkDataSync.instance) NetworkDataSync.instance.PlayerListSend(0, true);
                    }
                }
            }
        }

        /// <summary>
        /// Swaps the slot of the client to the available slot at given team.
        /// </summary>
        /// <param name="clientID">Client`s network ID.</param>
        /// <param name="team">Team index.</param>
        public void SwapTeamTo(ulong clientID, int team)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            // Fetch current scene data
            if (SceneHandler.instance.sceneData == null || SceneHandler.instance.sceneIndex >= SceneHandler.instance.sceneData.Length) return;
            SceneData sceneData = SceneHandler.instance.sceneData[SceneHandler.instance.sceneIndex];

            if (sceneData.chooseTeams || team >= sceneData.teamsAndPlayers.Length) return;
            if (sceneData.teamsAndPlayers[team].players == null || sceneData.teamsAndPlayers[team].players.Length == 0) return;

            int currentSlot = SlotManager.instance.GetClientSlot(clientID);

            // Calculate previousTeamSlots
            int previousTeamSlots = 0;
            for (int t = 0; t < sceneData.teamsAndPlayers.Length; t++)
            {
                if (t == team) break;
                previousTeamSlots += sceneData.teamsAndPlayers[t].players.Length;
            }

            // Starting index
            int startingIndex = 0;
            if (team == SlotManager.instance.playerTeam[currentSlot])
            {
                startingIndex = (currentSlot - previousTeamSlots) + 1;
            }

            // Find empty slot within desired team
            bool loopCompleted = false;
            for (int i = startingIndex; true; i++)
            // for (int i = startingIndex + 1; i < sceneData.teamsAndPlayers[team].players.Length + 1; i++)
            {
                // Go to index 0 if last slot reached
                if (i == sceneData.teamsAndPlayers[team].players.Length)
                {
                    i = 0; // Wrap around
                    loopCompleted = true;
                }

                // Check if all slots were tested
                if (i == startingIndex && loopCompleted)
                {
                    break; // Unsuccessfull, no empty slot found
                }

                // Check if slot is empty
                if (SlotManager.instance.slotType[previousTeamSlots + i] == SlotType.Empty)
                {
                    // Swap data to new slot
                    SlotManager.instance.slotType[previousTeamSlots + i] = SlotManager.instance.slotType[currentSlot];
                    SlotManager.instance.playerID[previousTeamSlots + i] = SlotManager.instance.playerID[currentSlot];
                    SlotManager.instance.playerName[previousTeamSlots + i] = SlotManager.instance.playerName[currentSlot];

                    // Clean previous slot
                    SlotManager.instance.slotType[currentSlot] = SlotType.Empty;
                    SlotManager.instance.playerID[currentSlot] = -1;
                    SlotManager.instance.playerName[currentSlot] = "";

                    // Server current player update if necessary
                    SlotManager.instance.SetCurrentPlayer(SlotManager.instance.GetClientSlot(0));

                    UIManagerMenu.instance.FillPlayerList();
                    if (NetworkDataSync.instance) NetworkDataSync.instance.PlayerListSend(0, true);

                    break;
                }
            }
        }

        /// <summary>
        /// Changes the team of the specified slot. Can be called only if chooseTeams is on.
        /// </summary>
        /// <param name="clientID">Client`s network ID.</param>
        /// <param name="team">New team index.</param>
        /// <returns></returns>
        public bool ChangeTeamTo(ulong clientID, int team)
        {
            // Fetch current scene data
            if (SceneHandler.instance.sceneData == null || SceneHandler.instance.sceneIndex >= SceneHandler.instance.sceneData.Length) return false;
            SceneData sceneData = SceneHandler.instance.sceneData[SceneHandler.instance.sceneIndex];

            if (sceneData.chooseTeams)
            {
                int slot = SlotManager.instance.GetClientSlot(clientID);
                if (SlotManager.instance.playerTeam[slot] != team)
                {
                    SlotManager.instance.playerTeam[slot] = team;

                    // Server current player update if necessary
                    SlotManager.instance.SetCurrentPlayer(SlotManager.instance.GetClientSlot(0));

                    UIManagerMenu.instance.FillPlayerList();
                    if (NetworkDataSync.instance) NetworkDataSync.instance.PlayerListSend(0, true);
                    return true;
                }
            }
            return false;
        }

        public bool ChangeSpawnPositionTo(ulong clientID, int posIndex)
        {
            // Fetch current scene data
            if (SceneHandler.instance.sceneData == null || SceneHandler.instance.sceneIndex >= SceneHandler.instance.sceneData.Length) return false;
            SceneData sceneData = SceneHandler.instance.sceneData[SceneHandler.instance.sceneIndex];

            int slot = SlotManager.instance.GetClientSlot(clientID);
            if (SlotManager.instance.playerPosition[slot] != posIndex)
            {
                SlotManager.instance.playerPosition[slot] = posIndex;
                UIManagerMenu.instance.FillPlayerList();
                if (NetworkDataSync.instance) NetworkDataSync.instance.PlayerListSend(0, true);
                return true;
            }

            return false;
        }

        public bool ChangeFactionTo(ulong clientID, int faction)
        {
            // Fetch current scene data
            if (SceneHandler.instance.sceneData == null || SceneHandler.instance.sceneIndex >= SceneHandler.instance.sceneData.Length) return false;
            SceneData sceneData = SceneHandler.instance.sceneData[SceneHandler.instance.sceneIndex];

            int slot = SlotManager.instance.GetClientSlot(clientID);
            if (SlotManager.instance.playerFaction[slot] != faction)
            {
                SlotManager.instance.playerFaction[slot] = faction;
                UIManagerMenu.instance.FillPlayerList();
                if (NetworkDataSync.instance) NetworkDataSync.instance.PlayerListSend(0, true);
                return true;
            }

            return false;
        }

        // ============================= UTILS ==============================================================================

        /// <summary>
        /// Returns total number of players for the active scene.
        /// </summary>
        public int GetTotalNumberOfPlayers()
        {
            // Fetch current scene data
            if (SceneHandler.instance.sceneData == null || SceneHandler.instance.sceneIndex >= SceneHandler.instance.sceneData.Length) return -1;
            SceneData sceneData = SceneHandler.instance.sceneData[SceneHandler.instance.sceneIndex];

            int totalPlayers = 0;
            for (int t = 0; t < sceneData.teamsAndPlayers.Length; t++)
            {
                totalPlayers += sceneData.teamsAndPlayers[t].players.Length;
            }

            return totalPlayers;
        }

        /// <summary>
        /// Returns slot index of the given client.
        /// </summary>
        /// <param name="clientID">Client`s network ID.</param>
        public int GetClientSlot(ulong clientID)
        {
            return Array.IndexOf(playerID, (int)clientID);
        }

        /// <summary>
        /// Adds a bot at specified slot.
        /// </summary>
        /// <param name="slotIndex">Slot index.</param>
        public void AddBot(int slotIndex)
        {
            // Check if not adding a bot on top of the host
            if (SlotManager.instance.currentPlayer == slotIndex) return;

            // Kick existing player if there is

            // Add a bot
            SlotManager.instance.slotType[slotIndex] = SlotType.Bot;
        }

        /// <summary>
        /// Sets the current player index, team and name.
        /// </summary>
        /// <param name="slotIndex">Player index.</param>
        public void SetCurrentPlayer(int slotIndex)
        {
            // [Interflow fix 2026-06-26] guard -1/вне диапазона: GetClientSlot мог вернуть -1 (рассинхрон playerID) →
            // playerTeam[-1] IndexOutOfRange + затирание корректного currentPlayer на -1. Из-за этого у 2-го клиента
            // команда локального игрока не резолвилась (CommandTeamForLocalPlayer → fallback 0) → волна/способности уходили на team 0.
            if (slotIndex < 0 || playerTeam == null || slotIndex >= playerTeam.Length) return;
            currentPlayer = slotIndex;
            currentTeam = SlotManager.instance.playerTeam[slotIndex];
            currentName = SlotManager.instance.playerName[slotIndex];
        }

        /// <summary>
        /// Returns list of allies of the player, excludes NeutralPassive.
        /// </summary>
        /// <param name="player">Slot index.</param>
        /// <param name="includePlayer">Should include the player.</param>
        /// <returns>List of allies` index.</returns>
        public int[] GetPlayerAllies(int player, bool includePlayer = false)
        {
            List<int> allies = new List<int>();
            if (includePlayer) allies.Add(player);

            for (int i = 0; i < SlotManager.instance.playerTeam.Length; i++)
            {
                if (player != i)
                {
                    if (SlotManager.instance.playerTeam[player] == SlotManager.instance.playerTeam[i])
                    {
                        allies.Add(i);
                    }
                }
            }

            return allies.ToArray();
        }

        /// <summary>
        /// Returns true If player is an ally to another player. Includes neutral passive as an ally for everyone.
        /// </summary>
        /// <param name="player1">First slot index.</param>
        /// <param name="player2">Second slot index</param>
        public bool IsAlly(int player1, int player2)
        {
            if (player1 == (int)Players.NeutralPassive || player2 == (int)Players.NeutralPassive) return true;
            if (SlotManager.instance.playerTeam[player1] == SlotManager.instance.playerTeam[player2]) return true;
            return false;
        }

        // ============================= netID ==============================================================================

        /// <summary>
        /// For server: When unit is spawned this method is called to acquire unique net ID.
        /// </summary>
        /// <param name="unit">Spawned unit.</param>
        /// <param name="netID">Optional, desired netID for the unit.</param>
        public void AssignNetID(Unit unit, UInt16 netID = 0)
        {
            if (unit.spawned) return;

            // Network Spawned / Save Manager
            if (netID != 0)
            {
                if (unit.netID != 0) RemoveNetID(unit.netID, unit);
                unit.netID = netID;
                SlotManager.instance.unitNetID.Add(unit.netID, unit);
            }
            // Dynamicly spawned - Server only
            else if (unit.netID == 0)
            {
                netID = (UInt16)UnityEngine.Random.Range(1, 65535);
                while (SlotManager.instance.unitNetID.ContainsKey(netID))
                {
                    netID = (UInt16)UnityEngine.Random.Range(1, 65535);
                }
                unit.netID = netID;
                SlotManager.instance.unitNetID.Add(unit.netID, unit);
            }
            // In scene placed
            else
            {
                SlotManager.instance.unitNetID.Add(unit.netID, unit);
            }

            unit.spawned = true;
            GameManager.instance.AddUnitCount(unit);
        }

        /// <summary>
        /// For everybody: When unit dies this method is called to remove netID from the lists.
        /// </summary>
        /// <param name="netID"></param>
        /// <param name="unit"></param>
        public void RemoveNetID(UInt16 netID, Unit unit)
        {
            // If it does not contain key, most likely we are coming from saved file and not yet initialized the scene. Skip it.
            if (!SlotManager.instance.unitNetID.ContainsKey(netID)) return;

            // If unit with the same id exists on the clients it probably means that this unit should die this tick, it is already dead on the server and it has been rewritten for new unit
            if (SlotManager.instance.unitNetID[netID] == unit)
            {
                SlotManager.instance.unitNetID.Remove(netID);

                if (NetworkManager.Singleton.IsServer)
                {
                    // Remove from sync lists
                    NetworkDataSync.instance.positionSyncList.Remove(netID);
                    NetworkDataSync.instance.removeSyncList.Remove(netID);
                    NetworkDataSync.instance.directPositionSyncList.Remove(netID);
                    NetworkDataSync.instance.hpChangedUnits.Remove(netID);
                    NetworkDataSync.instance.mpChangedUnits.Remove(netID);
                    NetworkDataSync.instance.xpChangedUnits.Remove(netID);
                }
            }

            GameManager.instance.RemoveUnitCount(unit);
        }
    }
}
