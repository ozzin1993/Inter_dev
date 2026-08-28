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
                    JsonHelper.ToJson<SlotType>(SlotManager.Instance.slotType),
                    JsonHelper.ToJson<int>(SlotManager.Instance.playerID),
                    JsonHelper.ToJson<int>(SlotManager.Instance.playerTeam),
                    JsonHelper.ToJson<string>(SlotManager.Instance.playerName),
                    JsonHelper.ToJson<int>(SlotManager.Instance.playerPosition),
                    JsonHelper.ToJson<int>(SlotManager.Instance.playerFaction),
                    JsonHelper.ToJson<bool>(SlotManager.Instance.playerLost));
            }
            else
            {
                PlayerListClientRpc(SlotManager.Instance.GetClientSlot(clientID),
                                    JsonHelper.ToJson<SlotType>(SlotManager.Instance.slotType),
                                    JsonHelper.ToJson<int>(SlotManager.Instance.playerID),
                                    JsonHelper.ToJson<int>(SlotManager.Instance.playerTeam),
                                    JsonHelper.ToJson<string>(SlotManager.Instance.playerName),
                                    JsonHelper.ToJson<int>(SlotManager.Instance.playerPosition),
                                    JsonHelper.ToJson<int>(SlotManager.Instance.playerFaction),
                                    JsonHelper.ToJson<bool>(SlotManager.Instance.playerLost),
                                    RpcTarget.Single(clientID, RpcTargetUse.Temp));
            }
        }

        // Client receives the list of players in the game
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void PlayerListClientRpc(int playerSlot, string slotType, string playerID, string playerTeam, string playerName, string playerPosition, string playerFaction, string playerLost, RpcParams rpcParams)
        {
            SlotManager.Instance.slotType = JsonHelper.FromJson<SlotType>(slotType);
            SlotManager.Instance.playerID = JsonHelper.FromJson<int>(playerID);
            SlotManager.Instance.playerTeam = JsonHelper.FromJson<int>(playerTeam);
            SlotManager.Instance.playerName = JsonHelper.FromJson<string>(playerName);
            SlotManager.Instance.playerPosition = JsonHelper.FromJson<int>(playerPosition);
            SlotManager.Instance.playerFaction = JsonHelper.FromJson<int>(playerFaction);
            SlotManager.Instance.playerLost = JsonHelper.FromJson<bool>(playerLost);
            SlotManager.Instance.SetCurrentPlayer(playerSlot);

            if (SlotManager.Instance.gameStarted == GameState.Menu) Presentation.MenuUI?.FillPlayerList();
        }

        // Clients receive the list of players in the game
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void PlayersListClientRpc(string slotType, string playerID, string playerTeam, string playerName, string playerPosition, string playerFaction, string playerLost)
        {
            SlotManager.Instance.slotType = JsonHelper.FromJson<SlotType>(slotType);
            SlotManager.Instance.playerID = JsonHelper.FromJson<int>(playerID);
            SlotManager.Instance.playerTeam = JsonHelper.FromJson<int>(playerTeam);
            SlotManager.Instance.playerName = JsonHelper.FromJson<string>(playerName);
            SlotManager.Instance.playerPosition = JsonHelper.FromJson<int>(playerPosition);
            SlotManager.Instance.playerFaction = JsonHelper.FromJson<int>(playerFaction);
            SlotManager.Instance.playerLost = JsonHelper.FromJson<bool>(playerLost);
            // Current player update if necessary
            SlotManager.Instance.SetCurrentPlayer(SlotManager.Instance.GetClientSlot(NetworkManager.LocalClientId));

            if (SlotManager.Instance.gameStarted == GameState.Menu) Presentation.MenuUI?.FillPlayerList();
        }

        // WIN LOSE -----

        // Server sends data to certain player that he won
        public void PlayerWinsSend(int player)
        {
            PlayerWinsClientRpc(RpcTarget.Single((ulong)SlotManager.Instance.playerID[player], RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void PlayerWinsClientRpc(RpcParams rpcParams)
        {
            SlotManager.Instance.PlayerWins();
        }

        // Server sends data to certain player that he lost
        public void PlayerLosesSend(int player)
        {
            PlayerLosesClientRpc(RpcTarget.Single((ulong)SlotManager.Instance.playerID[player], RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void PlayerLosesClientRpc(RpcParams rpcParams)
        {
            SlotManager.Instance.PlayerLoses();
        }

        // Servers sends data to certain player that his team lost
        public void TeamLosesSend(int player)
        {
            TeamLosesClientRpc(RpcTarget.Single((ulong)SlotManager.Instance.playerID[player], RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void TeamLosesClientRpc(RpcParams rpcParams)
        {
            SlotManager.Instance.TeamLoses();
        }

        // CLIENT SWAP TEAM -----

        // Server receive info
        [Rpc(SendTo.Server)]
        public void SwapTeamToServerRpc(int team, RpcParams rpcParams = default)
        {
            SlotManager.Instance.SwapTeamTo(rpcParams.Receive.SenderClientId, team);
        }

        // CLIENT CHANGE TEAM -----

        // Server receive info
        [Rpc(SendTo.Server)]
        public void ChangeTeamToServerRpc(int team, RpcParams rpcParams = default)
        {
            SlotManager.Instance.ChangeTeamTo(rpcParams.Receive.SenderClientId, team);
        }

        [Rpc(SendTo.Server)]
        public void ChangeSpawnToServerRpc(int index, RpcParams rpcParams = default)
        {
            SlotManager.Instance.ChangeSpawnPositionTo(rpcParams.Receive.SenderClientId, index);
        }

        [Rpc(SendTo.Server)]
        public void ChangeFactionToServerRpc(int index, RpcParams rpcParams = default)
        {
            SlotManager.Instance.ChangeFactionTo(rpcParams.Receive.SenderClientId, index);
        }

        // INGAME Connection Data Sync --------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // --- FINISHED LOADING SAVE DATA ---

        // Server receive info that client finished loading the save file
        [Rpc(SendTo.Server)]
        public void ClientFinishedLoadingSaveServerRpc(RpcParams rpcParams = default)
        {
            NetworkConnectionHandler.Instance.ClientsWaitingListRemove(rpcParams.Receive.SenderClientId);
        }

        public void ServerPlayersFinishedLoading()
        {
            // If everyone finished loading the game, start the game
            if (NetworkConnectionHandler.Instance.clientsLoading.Count == 0)
            {
                NetworkConnectionHandler.Instance.clientsListUpdated -= NetworkDataSync.Instance.ServerPlayersFinishedLoading;

                // Means we were loading the save file, initialize the units and start the game
                if (NetworkConnectionHandler.Instance.connectionStage == 1)
                {
                    // Sync
                    NetworkDataSync.Instance.ResumeTheGameSend(true, true);
                    // Tell units to initialize
                    SlotManager.Instance.OnGameStart?.Invoke();
                    SaveManager.SetUnitData();
                    // Start the game
                    SceneHandler.Instance.saveFileName = "";
                    SceneHandler.Instance.saveSceneData = "";
                    SceneHandler.Instance.StartTheGame(false);
                }
                else if (SceneHandler.Instance.saveFileName != "" || SceneHandler.Instance.saveSceneData != "")
                {
                    // Initial save file load finished

                    // Clear saves
                    SceneHandler.Instance.saveFileName = "";
                    SceneHandler.Instance.saveSceneData = "";

                    // After loading the scene, we wait for units to be instantiated
                    // NetworkConnectionHandler.Instance.connectionStage = 1;
                    // NetworkConnectionHandler.Instance.AddClientsToWaitingList();

                    // Sync
                    NetworkDataSync.Instance.ResumeTheGameSend(true, false);
                    // Start the game
                    SceneHandler.Instance.StartTheGame(false);
                }
                else
                {
                    // MidGame join
                    NetworkConnectionHandler.Instance.ResumeTheGame(true);
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
                NetworkConnectionHandler.Instance.clientsListUpdated += NetworkDataSync.Instance.ServerStartLoading;

                // Retrieve content
                string sceneData = System.IO.File.ReadAllText(fileName);

                // Порционная отправка всем клиентам (см. SendChunksThrottled в NetworkDataSync.Scene.cs)
                byte[] utf8Bytes = Encoding.UTF8.GetBytes(sceneData); // Convert to UTF-8 bytes
                StartCoroutine(SendChunksThrottled(utf8Bytes, ++sceneStreamCounter, 0UL, false, false));
            }
            else Debug.Log("No save file found! At " + fileName);
        }

        // All clients receive the save file
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void ReceiveSceneDataClientRpc(byte[] chunk, int index, int totalChunks, int streamId)
        {
            NetworkConnectionHandler.Instance.connectionStage = 1;
            ReceiveChunk(chunk, index, totalChunks, streamId);
        }

        // Server receive info that client has acquired save file data
        [Rpc(SendTo.Server)]
        public void SaveSceneDataServerRpc(RpcParams rpcParams = default)
        {
            NetworkConnectionHandler.Instance.ClientsWaitingListRemove(rpcParams.Receive.SenderClientId);
        }

        public void ServerStartLoading()
        {
            // If everyone got save file data, start loading the game
            if (NetworkConnectionHandler.Instance.clientsLoading.Count == 0)
            {
                NetworkConnectionHandler.Instance.clientsListUpdated -= NetworkDataSync.Instance.ServerStartLoading;

                NetworkConnectionHandler.Instance.AddClientsToWaitingList();
                NetworkConnectionHandler.Instance.clientsListUpdated += NetworkDataSync.Instance.ServerPlayersFinishedLoading;

                SceneHandler.Instance.LoadScene();
            }
        }
    }
}
