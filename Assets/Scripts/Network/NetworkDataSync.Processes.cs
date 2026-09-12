using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;
using System.Text;

namespace StrategyCore
{
    // NetworkDataSync.Processes.cs — технологии/процессы/предметы. Вырезано 1:1 из NetworkDataSync.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class NetworkDataSync
    {

        // TECHNOLOGY - Added / Removed -----------------------------------------------------------------------------------------------------------------------------------

        // LOCK/UNLOCK -----

        // Called by server to send the lock/unlock info to clients
        public void TechnologySync(int player, int techID, bool unlocked)
        {
            TechnologySyncClientRpc(player, techID, unlocked);
        }

        // Received by clients that should lock/unlock technology
        [Rpc(SendTo.NotServer)]
        private void TechnologySyncClientRpc(int player, int techID, bool unlocked)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            Technology tech = TechnologyManager.instance.GetTechByID(techID);
            if (tech != null)
            {
                TechnologyManager.instance.TechTree[player][tech] = unlocked;
                if (unlocked) TechnologyManager.instance.OnTechUnlock[player]?.Invoke();
                else TechnologyManager.instance.OnTechLock[player]?.Invoke();

                if (tech.shared)
                {
                    int[] allies = SlotManager.instance.GetPlayerAllies(player);

                    for (int i = 0; i < allies.Length; i++)
                    {
                        if (TechnologyManager.instance.TechTree[allies[i]][tech] != unlocked)
                        {
                            TechnologyManager.instance.TechTree[allies[i]][tech] = unlocked;
                            // Send a message previously unknown tech was unlocked
                            if (unlocked) TechnologyManager.instance.OnTechUnlock[allies[i]]?.Invoke();
                            else TechnologyManager.instance.OnTechLock[allies[i]]?.Invoke();
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning("Desync on technology with an id " + techID + ". It does not exist on player " + SlotManager.instance.currentPlayer);
            }
        }

        // PROCESS - Added / Removed -----------------------------------------------------------------------------------------------------------------------------------

        // ADD -----

        // Called by server to send the process information
        public void AddProcess(Unit unit, int processIndex, int abilityIndex, bool isItem)
        {
            AddProcessClientRpc(unit.netID, processIndex, abilityIndex, isItem);
        }

        // Add process on client
        [Rpc(SendTo.NotServer)]
        private void AddProcessClientRpc(UInt16 netID, int processIndex, int abilityIndex, bool isItem)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                // Add process
                Ability currentProcess = (isItem) ? unit.items[abilityIndex] : unit.abilities[abilityIndex];
                int currentProcessLevel = (isItem) ? 0 : unit.abilityLevel[abilityIndex];

                if (currentProcess is Research)
                {
                    Research upgradeAbility = (Research)currentProcess;
                    TechnologyManager.instance.TechBeingProcessed(upgradeAbility.unlockTech[currentProcessLevel], unit.owner);
                }

                unit.activeProcess[processIndex] = currentProcess;
                unit.processLevel[processIndex] = currentProcessLevel;

                // Subtract costs
                unit.SubtractAbilityItemCost(unit.owner, abilityIndex, isItem);

                // Item charge decrease
                if (isItem) unit.ItemChargesChange(abilityIndex);

                unit.OnProcessUpdate?.Invoke();
                unit.OnRedrawAbilityView?.Invoke();
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (AddProcess NetworkDataSync)");
            }
        }

        // CANCEL -----

        // Called by server to send the process information
        public void CancelProcess(Unit unit, int processIndex)
        {
            CancelProcessClientRpc(unit.netID, processIndex);
        }

        // Cancel process on client
        [Rpc(SendTo.NotServer)]
        private void CancelProcessClientRpc(UInt16 netID, int processIndex)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                if (unit.activeProcess[processIndex] is Research)
                {
                    Research research = (Research)unit.activeProcess[processIndex];
                    TechnologyManager.instance.TechFinishedProcessing(research.unlockTech[unit.processLevel[processIndex]], unit.owner);
                }

                unit.ProcessMoveForward(processIndex);

                unit.OnProcessUpdate?.Invoke();
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (CancelProcess NetworkDataSync)");
            }
        }

        // FINISH -----

        // Called by server to send the process information
        public void FinishProcess(Unit unit)
        {
            FinishProcessClientRpc(unit.netID);
        }

        // Finish process on client
        [Rpc(SendTo.NotServer)]
        private void FinishProcessClientRpc(UInt16 netID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                unit.activeProcess[0].Use(unit, unit.owner, unit.processLevel[0]);
                unit.ProcessMoveForward(0); // Move next process forward
                unit.OnProcessUpdate?.Invoke();
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (FinishProcess NetworkDataSync)");
            }
        }

        // INVENTORY - added / removed / changed / charge change -----------------------------------------------------------------------------------------------------------------------------------
        // charge change modified by abilit use, should not sync?

        // ADD -----
        // Called by server to add item to clients
        public void AddItem(Unit unit, int slotIndex, int abilityId, int charges, float cooldown)
        {
            AddItemClientRpc(unit.netID, slotIndex, abilityId, charges, cooldown);
        }

        [Rpc(SendTo.NotServer)]
        private void AddItemClientRpc(UInt16 netID, int slotIndex, int abilityId, int charges, float cooldown)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                Ability ability = GameManager.instance.gameAbilities.TryGetValue(abilityId, out Ability ab) ? ab : null;

                if (ab)
                {
                    unit.items[slotIndex] = ability;
                    if (charges > 0) unit.itemCharges[slotIndex] = charges;
                    if (unit.owner == SlotManager.instance.currentPlayer && cooldown > 0) unit.ChangeAbilityCooldown(cooldown, slotIndex, true);
                    ability.Unlock(unit, unit.owner, 0);
                    // OnInventoryChange?.Invoke(); // Due to how cooldown is calculated currently inventory change should also trigger abilityViewRedraw
                    unit.OnRedrawAbilityView?.Invoke();
                }
                else
                {
                    Debug.LogError("Desync! Could not find abilityID:" + abilityId + ", should exist on client, but does not! (AddItem NetworkDataSync)");
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (AddItem NetworkDataSync)");
            }
        }

        // REMOVE -----
        // Called by server to remove item on clients
        public void RemoveItem(Unit unit, int slotIndex)
        {
            RemoveItemClientRpc(unit.netID, slotIndex);
        }

        [Rpc(SendTo.NotServer)]
        private void RemoveItemClientRpc(UInt16 netID, int slotIndex)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                unit.RemoveItem(slotIndex);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (RemoveItem NetworkDataSync)");
            }
        }

        // SWAP -----
        // Called by server to swap items on clients
        public void SwapItem(Unit unit, int slotIndex, int slotIndex2)
        {
            SwapItemClientRpc(unit.netID, slotIndex, slotIndex2);
        }

        [Rpc(SendTo.NotServer)]
        private void SwapItemClientRpc(UInt16 netID, int slotIndex, int slotIndex2)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                unit.SwapItem(slotIndex, slotIndex2, false);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (SwapItem NetworkDataSync)");
            }
        }
    }
}
