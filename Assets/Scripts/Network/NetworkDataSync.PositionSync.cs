using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;
using System.Text;

namespace StrategyCore
{
    // NetworkDataSync.PositionSync.cs — синк позиций (прямой и тиковый). Вырезано 1:1 из NetworkDataSync.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class NetworkDataSync
    {

        // Контракт канала позиций (ревью, блок «баги и корректность»):
        // — прямой канал пакует координату в UInt16 как x*100 → предел карты 655.34 по каждой оси;
        // — маркер «юнит доехал, это последняя позиция» — отрицательный X (карта обязана целиком лежать
        //   в положительных координатах; предел и положительность проверяются в Grid.Initialize).
        public const float MaxSyncableCoordinate = 655.34f;
        public const float LastPositionMarker = -1f;

        // POSITION SYNC --------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // ------ DIRECT POSITION SET ------

        // Server send unit position
        public void SetPositionDirect(Unit unit)
        {
            NetworkDataSync.Instance.directPositionSyncList.Add(unit.netID);
        }

        // Sends positions of units, on clients they are set directly without interpolation
        private void SyncDirectPositionSend()
        {
            if (directPositionSyncList.Count == 0) return;

            // Create position array for directPositionSyncList
            UInt16[] posX = new UInt16[directPositionSyncList.Count];
            UInt16[] posY = new UInt16[directPositionSyncList.Count];

            // Send position list
            for (int i = 0; i < directPositionSyncList.Count; i++)
            {
                Vector3 p = SlotManager.Instance.unitNetID[directPositionSyncList[i]].transform.position;
                // Кламп в предел UInt16-упаковки: координата вне карты не должна молча заворачиваться в случайную
                posX[i] = (UInt16)(Mathf.Clamp(p.x, 0f, MaxSyncableCoordinate) * 100f);
                posY[i] = (UInt16)(Mathf.Clamp(p.z, 0f, MaxSyncableCoordinate) * 100f);
            }

            NetworkDataSync.Instance.SetPositionDirectClientRpc(directPositionSyncList.ToArray(), posX, posY);

            directPositionSyncList.Clear();
        }

        // Client receive info about unit position
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void SetPositionDirectClientRpc(UInt16[] netID, UInt16[] posX, UInt16[] posY)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            for (int i = 0; i < netID.Length; i++)
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(netID[i], out Unit unit))
                {
                    // Stop interpolation to previous position
                    unit.isMoving = false;
                    unit.AnimatorSetBool(AnimationState.Walk, false);
                    // Rotate
                    unit.LookAtInstant(new Vector2(((float)posX[i]) * 0.01f, ((float)posY[i]) * 0.01f));
                    // Position
                    unit.SetPosition(new Vector2(((float)posX[i]) * 0.01f, ((float)posY[i]) * 0.01f));
                    // Update the grid data
                    Grid.AssignToChunk(unit);
                    FogOfWar.Instance.CellAssignment(unit);
                }
                else
                {
                    Debug.LogError("Desync! Unit netID:" + netID[i] + " should exist on client, but does not! (PositionSet NetworkDataSync)");
                }
            }
        }

        // ------ POSITION SYNC ------

        // Send position of units from the server
        private void SyncPositionSend()
        {
            if (positionSyncList.Count == 0 && removeSyncList.Count == 0)
            {
                return;
            }

            List<UInt16> positionSentReset = new List<UInt16>();

            // Create position array for positionSyncList and indication that the position sent before was last with removeSyncList
            float[] posX = new float[positionSyncList.Count + removeSyncList.Count];
            float[] posY = new float[positionSyncList.Count + removeSyncList.Count];

            // Send position list
            for (int i = 0; i < positionSyncList.Count; i++)
            {
                Unit unit = SlotManager.Instance.unitNetID[positionSyncList[i]];
                posX[i] = (float)unit.transform.position.x; // (UInt16)( * 100f); //BitConverter.ToUInt16(BitConverter.GetBytes(unitNetID[syncList[i]].transform.position.x), 0);
                posY[i] = (float)unit.transform.position.z; // (UInt16)(SlotManager.Instance.unitNetID[positionSyncList[i]].transform.position.z * 100f); //BitConverter.ToUInt16(BitConverter.GetBytes(unitNetID[syncList[i]].transform.position.z), 0);

                unit.positionsSent = true;
                if (unit.removeFromPosSync)
                {
                    positionSentReset.Add(unit.netID);
                    unit.removeFromPosSync = false;
                }
            }

            // Send removal List
            for (int i = 0; i < removeSyncList.Count; i++)
            {
                posX[positionSyncList.Count + i] = LastPositionMarker; // маркер «последняя позиция» (см. контракт выше)
                posY[positionSyncList.Count + i] = LastPositionMarker;
            }

            // Send the information. We combine both lists into an array and send to the clients
            var sendArray = new UInt16[positionSyncList.Count + removeSyncList.Count];
            positionSyncList.ToArray().CopyTo(sendArray, 0);
            removeSyncList.ToArray().CopyTo(sendArray, positionSyncList.Count);

            // Remove from position sync
            for (int i = 0; i < positionSentReset.Count; i++)
            {
                NetworkDataSync.Instance.positionSyncList.Remove(positionSentReset[i]);
            }

            SyncPositionClientRpc(sendArray, posX, posY, NetworkManager.ServerTime.TimeAsFloat);

            removeSyncList.Clear();
        }

        // Sync the position of units on clients
        //[Rpc(SendTo.NotServer)]

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Reliable, InvokePermission = RpcInvokePermission.Server)]
        private void SyncPositionClientRpc(UInt16[] id, float[] posX, float[] posY, float serverTime)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            for (int i = 0; i < id.Length; i++)
            {
                // unitNetID[id[i]].PositionSetDirect(new Vector2(((float)posX[i]) * 0.01f, ((float)posY[i]) * 0.01f), avgPing); 
                // SlotManager.Instance.unitNetID[id[i]].PositionSetDirect(new Vector2((float)posX[i], (float)posY[i]), serverTime);

                // Latency is for smooth interpolation of the position on the client
                float latencySec = NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId) * 0.0005f; // 0.001f to convert s to ms, 0.5f to get only half ot the round trip ping
                float tickRate = (float)(1f / NetworkManager.NetworkTickSystem.TickRate);
                //SlotManager.Instance.unitNetID[id[i]].PositionSetDirect(new Vector2(((float)posX[i]) * 0.01f, ((float)posY[i]) * 0.01f), latencySec);

                // SlotManager.Instance.unitNetID[id[i]].PositionSetDirect(new Vector2((float)posX[i], (float)posY[i]), tickRate);

                if (latencySec > tickRate) SlotManager.Instance.unitNetID[id[i]].PositionSetDirect(new Vector2(posX[i], posY[i]), latencySec);
                else SlotManager.Instance.unitNetID[id[i]].PositionSetDirect(new Vector2(posX[i], posY[i]), tickRate);
            }
        }
    }
}
