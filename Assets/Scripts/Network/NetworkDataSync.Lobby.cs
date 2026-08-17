using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;
using System.Text;

namespace StrategyCore
{
    // NetworkDataSync.Lobby.cs — лобби (список игроков/победы/смена команд-фракций/загрузка). Вырезано 1:1 из NetworkDataSync.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class NetworkDataSync
    {

        // LOBBY DATA SYNC -----------------------------------------------------------------------------------------------------------------------------------------------------------------

        // PLAYER LIST -----

        // Server send the list of players in the game
        public void PlayerListSend(ulong clientID, bool allClients = false)
        {
            if (allClients)
            {
                PlayersListClientRpc(
                    JsonHelper.ToJson<SlotType>(SlotManager.instance.slotType),
                    JsonHelper.ToJson<int>(SlotManager.instance.playerID),
                    JsonHelper.ToJson<int>(SlotManager.instance.playerTeam),
                    JsonHelper.ToJson<string>(SlotManager.instance.playerName),
                    JsonHelper.ToJson<int>(SlotManager.instance.playerPosition),
                    JsonHelper.ToJson<int>(SlotManager.instance.playerFaction),
                    JsonHelper.ToJson<bool>(SlotManager.instance.playerLost));
            }
            else
            {
                PlayerListClientRpc(SlotManager.instance.GetClientSlot(clientID),
                                    JsonHelper.ToJson<SlotType>(SlotManager.instance.slotType),
                                    JsonHelper.ToJson<int>(SlotManager.instance.playerID),
                                    JsonHelper.ToJson<int>(SlotManager.instance.playerTeam),
                                    JsonHelper.ToJson<string>(SlotManager.instance.playerName),
                                    JsonHelper.ToJson<int>(SlotManager.instance.playerPosition),
                                    JsonHelper.ToJson<int>(SlotManager.instance.playerFaction),
                                    JsonHelper.ToJson<bool>(SlotManager.instance.playerLost),
                                    RpcTarget.Single(clientID, RpcTargetUse.Temp));
            }
        }

        // Client receives the list of players in the game
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void PlayerListClientRpc(int playerSlot, string slotType, string playerID, string playerTeam, string playerName, string playerPosition, string playerFaction, string playerLost, RpcParams rpcParams)
        {
            SlotManager.instance.slotType = JsonHelper.FromJson<SlotType>(slotType);
            SlotManager.instance.playerID = JsonHelper.FromJson<int>(playerID);
            SlotManager.instance.playerTeam = JsonHelper.FromJson<int>(playerTeam);
            SlotManager.instance.playerName = JsonHelper.FromJson<string>(playerName);
            SlotManager.instance.playerPosition = JsonHelper.FromJson<int>(playerPosition);
            SlotManager.instance.playerFaction = JsonHelper.FromJson<int>(playerFaction);
            SlotManager.instance.playerLost = JsonHelper.FromJson<bool>(playerLost);
            SlotManager.instance.SetCurrentPlayer(playerSlot);

            if (SlotManager.instance.gameStarted == GameState.Menu) Presentation.MenuUI?.FillPlayerList();
        }

        // Clients receive the list of players in the game
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void PlayersListClientRpc(string slotType, string playerID, string playerTeam, string playerName, string playerPosition, string playerFaction, string playerLost)
        {
            SlotManager.instance.slotType = JsonHelper.FromJson<SlotType>(slotType);
            SlotManager.instance.playerID = JsonHelper.FromJson<int>(playerID);
            SlotManager.instance.playerTeam = JsonHelper.FromJson<int>(playerTeam);
            SlotManager.instance.playerName = JsonHelper.FromJson<string>(playerName);
            SlotManager.instance.playerPosition = JsonHelper.FromJson<int>(playerPosition);
            SlotManager.instance.playerFaction = JsonHelper.FromJson<int>(playerFaction);
            SlotManager.instance.playerLost = JsonHelper.FromJson<bool>(playerLost);
            // Current player update if necessary
            SlotManager.instance.SetCurrentPlayer(SlotManager.instance.GetClientSlot(NetworkManager.LocalClientId));

            if (SlotManager.instance.gameStarted == GameState.Menu) Presentation.MenuUI?.FillPlayerList();
        }

        // WIN LOSE -----

        // Server sends data to certain player that he won
        public void PlayerWinsSend(int player)
        {
            PlayerWinsClientRpc(RpcTarget.Single((ulong)SlotManager.instance.playerID[player], RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void PlayerWinsClientRpc(RpcParams rpcParams)
        {
            SlotManager.instance.PlayerWins();
        }

        // Server sends data to certain player that he lost
        public void PlayerLosesSend(int player)
        {
            PlayerLosesClientRpc(RpcTarget.Single((ulong)SlotManager.instance.playerID[player], RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void PlayerLosesClientRpc(RpcParams rpcParams)
        {
            SlotManager.instance.PlayerLoses();
        }

        // Servers sends data to certain player that his team lost
        public void TeamLosesSend(int player)
        {
            TeamLosesClientRpc(RpcTarget.Single((ulong)SlotManager.instance.playerID[player], RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void TeamLosesClientRpc(RpcParams rpcParams)
        {
            SlotManager.instance.TeamLoses();
        }

        // CLIENT SWAP TEAM -----

        // Server receive info
        [Rpc(SendTo.Server)]
        public void SwapTeamToServerRpc(int team, RpcParams rpcParams = default)
        {
            SlotManager.instance.SwapTeamTo(rpcParams.Receive.SenderClientId, team);
        }

        // CLIENT CHANGE TEAM -----

        // Server receive info
        [Rpc(SendTo.Server)]
        public void ChangeTeamToServerRpc(int team, RpcParams rpcParams = default)
        {
            SlotManager.instance.ChangeTeamTo(rpcParams.Receive.SenderClientId, team);
        }

        [Rpc(SendTo.Server)]
        public void ChangeSpawnToServerRpc(int index, RpcParams rpcParams = default)
        {
            SlotManager.instance.ChangeSpawnPositionTo(rpcParams.Receive.SenderClientId, index);
        }

        [Rpc(SendTo.Server)]
        public void ChangeFactionToServerRpc(int index, RpcParams rpcParams = default)
        {
            SlotManager.instance.ChangeFactionTo(rpcParams.Receive.SenderClientId, index);
        }

        // INGAME Connection Data Sync --------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // --- FINISHED LOADING SAVE DATA ---

        // Server receive info that client finished loading the save file
        [Rpc(SendTo.Server)]
        public void ClientFinishedLoadingSaveServerRpc(RpcParams rpcParams = default)
        {
            NetworkConnectionHandler.instance.ClientsWaitingListRemove(rpcParams.Receive.SenderClientId);
        }

        public void ServerPlayersFinishedLoading()
        {
            // If everyone finished loading the game, start the game
            if (NetworkConnectionHandler.instance.clientsLoading.Count == 0)
            {
                NetworkConnectionHandler.instance.clientsListUpdated -= NetworkDataSync.instance.ServerPlayersFinishedLoading;

                // Means we were loading the save file, initialize the units and start the game
                if (NetworkConnectionHandler.instance.connectionStage == 1)
                {
                    // Sync
                    NetworkDataSync.instance.ResumeTheGameSend(true, true);
                    // Tell units to initialize
                    SlotManager.instance.OnGameStart?.Invoke();
                    SaveManager.SetUnitData();
                    // Start the game
                    SceneHandler.instance.saveFileName = "";
                    SceneHandler.instance.saveSceneData = "";
                    SceneHandler.instance.StartTheGame(false);
                }
                else if (SceneHandler.instance.saveFileName != "" || SceneHandler.instance.saveSceneData != "")
                {
                    // Initial save file load finished

                    // Clear saves
                    SceneHandler.instance.saveFileName = "";
                    SceneHandler.instance.saveSceneData = "";

                    // After loading the scene, we wait for units to be instantiated
                    // NetworkConnectionHandler.instance.connectionStage = 1;
                    // NetworkConnectionHandler.instance.AddClientsToWaitingList();

                    // Sync
                    NetworkDataSync.instance.ResumeTheGameSend(true, false);
                    // Start the game
                    SceneHandler.instance.StartTheGame(false);
                }
                else
                {
                    // MidGame join
                    NetworkConnectionHandler.instance.ResumeTheGame(true);
                }
            }
        }

        // --- SAVE FILE SEND ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- 

        // SAVE DATA AFTER LOADING THE SCENE

        // Server send save file data
        public void SendSaveSceneData(string fileName, bool isPath = false)
        {
            if (!isPath) fileName = SaveManager.savePath + fileName + SaveManager.saveExtension;
            if (System.IO.File.Exists(fileName))
            {
                // Subscribe to updates
                NetworkConnectionHandler.instance.clientsListUpdated += NetworkDataSync.instance.ServerStartLoading;

                // Retrieve content
                string sceneData = System.IO.File.ReadAllText(fileName);

                // Send by chunks to clients
                byte[] utf8Bytes = Encoding.UTF8.GetBytes(sceneData); // Convert to UTF-8 bytes
                int totalChunks = Mathf.CeilToInt((float)utf8Bytes.Length / CHUNK_SIZE);

                for (int i = 0; i < totalChunks; i++)
                {
                    int startIndex = i * CHUNK_SIZE;
                    int length = Mathf.Min(CHUNK_SIZE, utf8Bytes.Length - startIndex);
                    byte[] chunk = new byte[length];

                    System.Array.Copy(utf8Bytes, startIndex, chunk, 0, length);

                    ReceiveSceneDataClientRpc(chunk, i, totalChunks);
                }
            }
            else Debug.Log("No save file found! At " + fileName);
        }

        // All clients receive the save file
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void ReceiveSceneDataClientRpc(byte[] chunk, int index, int totalChunks)
        {
            NetworkConnectionHandler.instance.connectionStage = 1;
            ReceiveChunk(chunk, index, totalChunks);
        }

        // Server receive info that client has acquired save file data
        [Rpc(SendTo.Server)]
        public void SaveSceneDataServerRpc(RpcParams rpcParams = default)
        {
            NetworkConnectionHandler.instance.ClientsWaitingListRemove(rpcParams.Receive.SenderClientId);
        }

        public void ServerStartLoading()
        {
            // If everyone got save file data, start loading the game
            if (NetworkConnectionHandler.instance.clientsLoading.Count == 0)
            {
                NetworkConnectionHandler.instance.clientsListUpdated -= NetworkDataSync.instance.ServerStartLoading;

                NetworkConnectionHandler.instance.AddClientsToWaitingList();
                NetworkConnectionHandler.instance.clientsListUpdated += NetworkDataSync.instance.ServerPlayersFinishedLoading;

                SceneHandler.instance.LoadScene();
            }
        }
    }
}
