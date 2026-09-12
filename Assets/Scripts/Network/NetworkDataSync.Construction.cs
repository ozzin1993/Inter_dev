using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;
using System.Text;

namespace StrategyCore
{
    // NetworkDataSync.Construction.cs — стройка/ремонт/транспорт/спавн. Вырезано 1:1 из NetworkDataSync.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class NetworkDataSync
    {

        // Синхронизация теневых кастеров (два RPC) снесена блоком Б6 (2026-09-04) вместе с умениями-каналами.

        // ANIMATION BLENDING SYNC ------------------------------------------

        // Change animation blending index for a specified unit
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        public void AnimationPrefixClientRpc(UInt16 netID, float blendingIndex)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                unit.SetAnimationBlendingIndex(blendingIndex);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (AnimationPrefix NetworkDataSync)");
            }
        }

        // CONSTRUCTION ----------------------------------------------

        // WORKER START WORKING -------

        // Server send: Worker starts working
        public void WorkerStartConstructing(Unit building, Unit worker)
        {
            WorkerStartConstructingClientRpc(building.netID, worker.netID);
        }

        // Client receive: Worker starts working
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void WorkerStartConstructingClientRpc(UInt16 buildingNetID, UInt16 workerNetID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(buildingNetID, out Unit building))
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(workerNetID, out Unit worker))
                {
                    if (building.constructionUnit && worker.constructionUnit)
                    {
                        worker.constructionUnit.StartTheConstruction(building);
                    }
                    else
                    {
                        Debug.LogError("Desync! Worker(" + workerNetID + ") or Building(" + buildingNetID + ") does not have constructionUnit set! (WorkerStartConstructing NetworkDataSync)");
                    }
                }
                else
                {
                    Debug.LogError("Desync! Unit netID:" + workerNetID + " should exist on client, but does not! (WorkerStartConstructing NetworkDataSync)");
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + buildingNetID + " should exist on client, but does not! (WorkerStartConstructing NetworkDataSync)");
            }
        }

        // WORKER STOP WORKING -------

        // Server send: Worker stops working
        public void WorkerStopConstructing(Unit building, Unit worker)
        {
            WorkerStopConstructingClientRpc(building.netID, worker.netID);
        }

        // Client receive: Worker stops working
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void WorkerStopConstructingClientRpc(UInt16 buildingNetID, UInt16 workerNetID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(workerNetID, out Unit worker))
            {
                if (worker.constructionUnit)
                {
                    worker.constructionUnit.StopTheConstruction(false);
                }
                else
                {
                    Debug.LogError("Desync! Worker(" + workerNetID + ") or Building(" + buildingNetID + ") does not have constructionUnit set! (WorkerStopConstructing NetworkDataSync)");
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + workerNetID + " should exist on client, but does not! (WorkerStopConstructing NetworkDataSync)");
            }
        }

        // WORKER START REPAIRING ---------------

        // Server: Worker starts repairing
        public void WorkerStartTheRepairs(Unit worker, Unit building)
        {
            WorkerStartTheRepairsClientRpc(building.netID, worker.netID);
        }

        // Client: Worker starts repairing
        // [Interflow fix 2026-09-09 repair-id-order] Порядок параметров приведён к порядку отправителя
        // (:110-112 шлёт building.netID, worker.netID) и к образцу согласованной пары стройки (:39-46).
        // Прежние имена были переставлены, из-за чего на клиенте «ремонтировало» рабочего зданием.
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void WorkerStartTheRepairsClientRpc(UInt16 buildingNetID, UInt16 workerNetID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(buildingNetID, out Unit building))
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(workerNetID, out Unit worker))
                {
                    if (building.constructionUnit && worker.constructionUnit)
                    {
                        worker.constructionUnit.StartTheRepairs(building);
                    }
                    else
                    {
                        Debug.LogError("Desync! Worker(" + workerNetID + ") or Building(" + buildingNetID + ") does not have constructionUnit set! (WorkerStartTheRepairs NetworkDataSync)");
                    }
                }
                else
                {
                    Debug.LogError("Desync! Unit netID:" + workerNetID + " should exist on client, but does not! (WorkerStartTheRepairs NetworkDataSync)");
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + buildingNetID + " should exist on client, but does not! (WorkerStartTheRepairs NetworkDataSync)");
            }
        }

        // WORKER STOP REPAIRING ---------------

        // Server: Worker starts repairing
        public void WorkerStopTheRepairs(Unit worker)
        {
            WorkerStopTheRepairsClientRpc(worker.netID);
        }

        // Client: Worker starts repairing
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void WorkerStopTheRepairsClientRpc(UInt16 workerNetID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(workerNetID, out Unit worker))
            {
                if (worker.constructionUnit)
                {
                    worker.constructionUnit.StopTheRepairs(false);
                }
                else
                {
                    Debug.LogError("Desync! Worker(" + workerNetID + ") does not have constructionUnit set! (WorkerStopTheRepairs NetworkDataSync)");
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + workerNetID + " should exist on client, but does not! (WorkerStopTheRepairs NetworkDataSync)");
            }
        }

        // BUILDING FINISH SYNC -------

        // Server: send trigger
        public void BuildingFinished(Unit building)
        {
            BuildingFinishedClientRpc(building.netID);
        }

        // Client: receive finished trigger
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void BuildingFinishedClientRpc(UInt16 buildingNetID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(buildingNetID, out Unit building))
            {
                if (building.constructionUnit)
                {
                    building.constructionUnit.FinishConstruction();
                }
                else
                {
                    Debug.LogError("Desync! Building(" + buildingNetID + ") does not have constructionUnit set! (BuildingFinished NetworkDataSync)");
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + buildingNetID + " should exist on client, but does not! (BuildingFinished NetworkDataSync)");
            }
        }

        // WORKER STATE RESET -------

        // Server send: Worker clear state
        public void ResetConstructionState(Unit worker, bool limitedOnly)
        {
            // Only for connected players and not server
            if (SlotManager.Instance.slotType[worker.owner] != SlotType.Player || SlotManager.Instance.playerID[worker.owner] == 0 || SlotManager.Instance.playerID[worker.owner] == -1) return;

            ResetConstructionStateClientRpc(worker.netID, limitedOnly, RpcTarget.Single((ulong)SlotManager.Instance.playerID[worker.owner], RpcTargetUse.Temp));
        }

        // Client receive: Worker clear state
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ResetConstructionStateClientRpc(UInt16 workerNetID, bool limitedOnly, RpcParams rpcParams)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(workerNetID, out Unit worker))
            {
                if (worker.constructionUnit)
                {
                    if (limitedOnly) worker.constructionUnit.ResetConstructionStatesReached();
                    else worker.constructionUnit.ResetConstructionStates(false);
                }
                else
                {
                    Debug.LogError("Desync! Building(" + workerNetID + ") does not have constructionUnit set! (ResetConstructionState NetworkDataSync)");
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + workerNetID + " should exist on client, but does not! (ResetConstructionState NetworkDataSync)");
            }
        }

        // WORKER SHADOWBUILDING REMOVE -------

        // Server send: Worker remove shadowbuilding
        public void WorkerResetShadowBuilding(Unit worker)
        {
            // Only for connected players and not server
            if (SlotManager.Instance.slotType[worker.owner] != SlotType.Player || SlotManager.Instance.playerID[worker.owner] == 0 || SlotManager.Instance.playerID[worker.owner] == -1) return;

            WorkerResetShadowBuildingClientRpc(worker.netID, RpcTarget.Single((ulong)SlotManager.Instance.playerID[worker.owner], RpcTargetUse.Temp));
        }

        // Client receive: Worker clear state
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void WorkerResetShadowBuildingClientRpc(UInt16 workerNetID, RpcParams rpcParams)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(workerNetID, out Unit worker))
            {
                if (worker.constructionUnit)
                {
                    if (worker.constructionUnit.shadowBuilding) Destroy(worker.constructionUnit.shadowBuilding.gameObject);
                }
                else
                {
                    Debug.LogError("Desync! Worker(" + workerNetID + ") does not have constructionUnit set! (WorkerResetShadowBuilding NetworkDataSync)");
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + workerNetID + " should exist on client, but does not! (WorkerResetShadowBuilding NetworkDataSync)");
            }
        }

        // CONSTRUCTION CANCELLED -------

        // Server: cancel the building upgrade
        public void ConstructionCancel(UInt16 upgradeBuildingNetID)
        {
            ConstructionCancelClientRpc(upgradeBuildingNetID);
        }

        // Client: cancel the building upgrade
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void ConstructionCancelClientRpc(UInt16 upgradeBuildingNetID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(upgradeBuildingNetID, out Unit upgradeBuilding))
            {
                if (upgradeBuilding.constructionUnit)
                {
                    upgradeBuilding.constructionUnit.CancelConstruction(true);
                }
                else
                {
                    Debug.LogError("Desync! Building(" + upgradeBuildingNetID + ") does not have constructionUnit set! (UpgradeCancel NetworkDataSync)");
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + upgradeBuildingNetID + " should exist on client, but does not! (UpgradeCancel NetworkDataSync)");
            }
        }

        // TRANSPORT SYNC  ------------------------------------------

        // EMBARK -----

        // Server send trigger
        public void EmbarkSync(Unit transportUnit, Unit embarkedUnit)
        {
            EmbarkClientRpc(transportUnit.netID, embarkedUnit.netID);
        }

        // Client receive info about who to embark
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void EmbarkClientRpc(UInt16 transportUnitID, UInt16 embarkedUnitID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(transportUnitID, out Unit transportUnit))
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(embarkedUnitID, out Unit embarkedUnit))
                {
                    transportUnit.transportUnit.Embark(embarkedUnit);
                }
                else
                {
                    Debug.LogError("Desync! Unit netID:" + embarkedUnitID + " should exist on client, but does not! (EmbarkSync NetworkDataSync)");
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + transportUnitID + " should exist on client, but does not! (EmbarkSync NetworkDataSync)");
            }
        }

        // DISEMBARK -----

        // Server send trigger
        public void DisembarkSync(Unit transportUnit, int index, Vector3 location)
        {
            DisembarkClientRpc(transportUnit.netID, index, location);
        }

        // Client receive info about who to disembark
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void DisembarkClientRpc(UInt16 transportUnitID, int index, Vector3 location)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(transportUnitID, out Unit transportUnit))
            {
                transportUnit.transportUnit.DisembarkInternal(index, location);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + transportUnitID + " should exist on client, but does not! (DisembarkSync Index NetworkDataSync)");
            }
        }

        // Server send trigger
        public void DisembarkSync(Unit transportUnit, int unitsOut, Vector3[] locations)
        {
            DisembarkClientRpc(transportUnit.netID, unitsOut, locations);
        }

        // Client receive info about who to disembark
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void DisembarkClientRpc(UInt16 transportUnitID, int unitsOut, Vector3[] locations)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(transportUnitID, out Unit transportUnit))
            {
                transportUnit.transportUnit.DisembarkInternal(unitsOut, locations);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + transportUnitID + " should exist on client, but does not! (DisembarkSync unitsOut NetworkDataSync)");
            }
        }

        // UNIT SPAWN --------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Server send spawn info
        public void UnitSpawn(int typeID, Vector3 position, float rotation, int owner, UInt16 netID = 0)
        {
            UnitSpawnClientRpc(typeID, position, rotation, owner, netID);
        }

        // Client receive spawn info
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void UnitSpawnClientRpc(int typeID, Vector3 position, float rotation, int owner, UInt16 netID = 0)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            // Get unit type to spawn
            if (GameManager.Instance.gameUnits.TryGetValue(typeID, out Unit unitRef))
            {
                Unit.SpawnInternal(unitRef, position, rotation, owner, netID);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + typeID + " should exist on client, but does not! (UnitSpawn NetworkDataSync)");
            }
        }

        // Server send item spawn info
        public void ItemDroppedSpawn(int abilityID, int itemCharges, float itemcd, Vector3 position, UInt16 netID)
        {
            ItemDroppedSpawnClientRpc(abilityID, itemCharges, itemcd, position, netID);
        }

        // Client receive item  spawn info
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void ItemDroppedSpawnClientRpc(int abilityID, int itemCharges, float itemcd, Vector3 position, UInt16 netID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (GameManager.Instance.gameAbilities.TryGetValue(abilityID, out Ability item))
            {
                ItemDropped.SpawnInternal(item, itemCharges, itemcd, position, netID);
            }
            else
            {
                Debug.LogError("Desync! Ability ID:" + abilityID + " should exist on client, but does not! (ItemDroppedSpawn NetworkDataSync)");
            }
        }
    }
}
