using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;
using System.Text;

namespace StrategyCore
{
    // NetworkDataSync.Scene.cs — сцены/чат/пинги (mid-game join). Вырезано 1:1 из NetworkDataSync.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class NetworkDataSync
    {

        // ----- MID GAME JOIN ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- 

        // Scene Data send to midgame connected client
        // Saves the scene data using savemanager and sends to a client
        // Порционная отправка и нумерация потоков (ревью, блок «баги и корректность»):
        // сотни reliable-сообщений в одном кадре переполняют очередь отправки транспорта на больших сейвах,
        // а общий буфер приёма без номера потока склеивал бы кусочки двух разных передач.
        [SerializeField, Tooltip("Сколько кусочков слепка сцены отправлять за кадр (защита очереди отправки)")]
        private int sceneChunksPerFrame = 8;

        private int sceneStreamCounter = 0;        // сервер: номер очередной передачи слепка
        private int currentStreamId = -1;          // клиент: какой поток сейчас собираем
        private int lastCompletedStreamId = -1;    // клиент: последний собранный поток (страховка от хвостов)

        public void SendSceneData(ulong clientID, bool midGame)
        {
            string sceneData = SaveManager.SaveToFile(true);
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(sceneData); // Convert to UTF-8 bytes
            StartCoroutine(SendChunksThrottled(utf8Bytes, ++sceneStreamCounter, clientID, true, midGame));
        }

        // Отправляет слепок кусочками по sceneChunksPerFrame за кадр; single=true — одному клиенту (мидгейм),
        // иначе — всем (рассылка сейва из лобби, NetworkDataSync.Lobby.cs).
        private System.Collections.IEnumerator SendChunksThrottled(byte[] utf8Bytes, int streamId, ulong clientID, bool single, bool midGame)
        {
            int totalChunks = Mathf.CeilToInt((float)utf8Bytes.Length / CHUNK_SIZE);
            for (int i = 0; i < totalChunks; i++)
            {
                int startIndex = i * CHUNK_SIZE;
                int length = Mathf.Min(CHUNK_SIZE, utf8Bytes.Length - startIndex);
                byte[] chunk = new byte[length];
                System.Array.Copy(utf8Bytes, startIndex, chunk, 0, length);

                if (single) ReceiveSceneDataClientRpc(chunk, i, totalChunks, streamId, midGame, RpcTarget.Single(clientID, RpcTargetUse.Temp));
                else ReceiveSceneDataClientRpc(chunk, i, totalChunks, streamId);

                if ((i + 1) % Mathf.Max(1, sceneChunksPerFrame) == 0) yield return null;
            }
        }

        // Client receive: Worker clear state
        // Receives save data from the server and loads it
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ReceiveSceneDataClientRpc(byte[] chunk, int index, int totalChunks, int streamId, bool isMidGame, RpcParams rpcParams)
        {
            NetworkConnectionHandler.instance.connectionStage = 2;
            ReceiveChunk(chunk, index, totalChunks, streamId);
        }

        // CLIENT RECEIVE CHUNKS OF SCENE DATA ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- 
        private void ReceiveChunk(byte[] chunk, int index, int totalChunks, int streamId)
        {
            if (streamId == lastCompletedStreamId) return; // хвост уже собранной передачи — игнор

            // Новая передача — старый недособранный буфер сбрасывается (изоляция потоков)
            if (streamId != currentStreamId)
            {
                receivedChunks.Clear();
                currentStreamId = streamId;
                totalChunksExpected = totalChunks;
            }

            receivedChunks[index] = chunk;

            if (receivedChunks.Count == totalChunksExpected)
            {
                lastCompletedStreamId = streamId;
                AssembleFullString();
            }
        }

        // FULL CHUNK DATA RECEIVED
        private void AssembleFullString()
        {
            List<byte> fullBytes = new List<byte>();

            for (int i = 0; i < totalChunksExpected; i++)
            {
                fullBytes.AddRange(receivedChunks[i]);
            }

            string finalString = Encoding.UTF8.GetString(fullBytes.ToArray());

            if (NetworkConnectionHandler.instance.connectionStage == 2)
            {
                // Mid Game
                SlotManager.instance.SetGameState(GameState.Started);
                GameManager.instance.StartCoroutine(SaveManager.LoadSave_Internal(finalString));
            }
            else
            {
                // Set save data, send info to the server that client has successfully acquired the data
                SceneHandler.instance.SceneStartedLoading();
                SceneHandler.instance.saveSceneData = finalString;
                SaveSceneDataServerRpc();
            }

            // Clear for next message
            receivedChunks.Clear();
            totalChunksExpected = -1;
        }

        // --- PAUSE/RESUME ---

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        public void PauseTheGameClientRpc()
        {
            NetworkConnectionHandler.instance.PauseTheGame();
        }

        public void ResumeTheGameSend(bool clearSaves = false, bool initializeUnitData = false)
        {
            NetworkDataSync.instance.ResumeTheGameClientRpc(clearSaves, initializeUnitData);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void ResumeTheGameClientRpc(bool clearSaves = false, bool initializeUnitData = false)
        {
            if (initializeUnitData)
            {
                // Tell units to initialize
                SlotManager.instance.OnGameStart?.Invoke();
                SaveManager.SetUnitData();
            }
            // Resume the game
            NetworkConnectionHandler.instance.ResumeTheGame();
            // Clear saves - on clients we should clear the saves after successful load to make sure they are not going to interfere down the line?
            if (clearSaves)
            {
                SceneHandler.instance.saveFileName = "";
                SceneHandler.instance.saveSceneData = "";
            }
        }

        // CHAT --------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Local player will send the message via this method. Host to clients, clients to server
        public void MsgSend(string message, bool allyChat)
        {
            // To prevent spamming
            currentPingCount++;
            if (currentPingCount > maxPingPerTick) return;

            // Send the data
            MsgSendServerRpc(message, allyChat);
        }

        [Rpc(SendTo.Server)]
        private void MsgSendServerRpc(string message, bool allyChat, RpcParams rpcParams = default)
        {
            int owner = SlotManager.instance.GetClientSlot(rpcParams.Receive.SenderClientId);

            if (!allyChat)
            {
                for (int i = 0; i < SlotManager.instance.slotType.Length; i++)
                {
                    // Only for connected players, and not self
                    if (SlotManager.instance.slotType[i] != SlotType.Player || i == owner) continue;

                    MsgSendClientRpc(owner, message, allyChat, RpcTarget.Single((ulong)SlotManager.instance.playerID[i], RpcTargetUse.Temp));
                }
            }
            else
            {
                int[] allies = SlotManager.instance.GetPlayerAllies(owner);

                for (int i = 0; i < allies.Length; i++)
                {
                    // Only for connected players
                    if (SlotManager.instance.slotType[allies[i]] != SlotType.Player) continue;

                    MsgSendClientRpc(owner, message, allyChat, RpcTarget.Single((ulong)SlotManager.instance.playerID[allies[i]], RpcTargetUse.Temp));
                }
            }
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void MsgSendClientRpc(int owner, string message, bool allyChat, RpcParams rpcParams)
        {
            if (SlotManager.instance.gameStarted == GameState.Menu) Presentation.MenuUI?.AddChatMsg(message, owner);
            else
            {
                Presentation.UI?.AddChatMsg(message, owner, allyChat);
                Presentation.UI?.ShowChatBox();
            }
        }

        // Server chat MSG
        public void ServerMsgSend(string message)
        {
            // Send the data
            ServerMsgClientRpc(message);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void ServerMsgClientRpc(string message)
        {
            if (SlotManager.instance.gameStarted == GameState.Menu) Presentation.MenuUI?.AddChatServerMsg(message);
            else Presentation.UI?.AddChatServerMsg(message);
        }

        // GAME MESSAGES --------------------------------------------------------------------------------------------------------------------------------------------------------------------

        public void GameMsgSend(string message, int player)
        {
            // Only for connected players
            if (SlotManager.instance.slotType[player] != SlotType.Player) return;

            GameMsgClientRpc(message, RpcTarget.Single((ulong)SlotManager.instance.playerID[player], RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void GameMsgClientRpc(string message, RpcParams rpcParams)
        {
            Presentation.NotifyMsg(message);
        }

        // MINIMAP PING --------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Local player will use this method to send information about the ping. Host to clients, clients to server
        public void MiniMapPingSend(int owner, Vector2 pos)
        {
            // To prevent spamming
            currentPingCount++;
            if (currentPingCount > maxPingPerTick) return;

            // Send the data
            MiniMapPingServerRpc(pos);
        }

        [Rpc(SendTo.Server)]
        private void MiniMapPingServerRpc(Vector2 pos, RpcParams rpcParams = default)
        {
            int owner = SlotManager.instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            int[] allies = SlotManager.instance.GetPlayerAllies(owner);

            for (int i = 0; i < allies.Length; i++)
            {
                // Only for connected players
                if (SlotManager.instance.slotType[allies[i]] != SlotType.Player) continue;

                MiniMapPingClientRpc(owner, pos, RpcTarget.Single((ulong)SlotManager.instance.playerID[allies[i]], RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void MiniMapPingClientRpc(int owner, Vector2 pos, RpcParams rpcParams)
        {
            Presentation.UI?.CreatePinger(owner, pos);
        }
    }
}
