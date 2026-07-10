using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;
using System.Text;

namespace StrategyCore
{
    // For syncing various game data from server to clients

    public partial class NetworkDataSync : NetworkBehaviour // [Interflow fix 2026-07-09] partial для наших расширений NetworkDataSync.*.cs
    {
        public static NetworkDataSync instance;

        // Sending and Receiving scene data
        private const int CHUNK_SIZE = 1000; // Save file will be divided into chunks to not overflow the buffer
        private Dictionary<int, byte[]> receivedChunks = new Dictionary<int, byte[]>();
        private int totalChunksExpected = -1;

        // Position sync
        public List<UInt16> directPositionSyncList = new List<UInt16>(); // List of networkIDs to sync their position directly
        public List<UInt16> positionSyncList = new List<UInt16>(); // List of networkIDs to sync their position
        public List<UInt16> removeSyncList = new List<UInt16>(); // List of networkIDs that will be sent to clients, indicating previously send position was their last position

        // MSG/Minimap ping
        public int currentPingCount; // To prevernt spamming
        public int maxPingPerTick = 3; // Spam prevention

        // HP/MP/XP Sync
        public Action onHPCleared; // Units that have their hp changed subscribe to this to clear their ID added flag
        public Action onMPCleared; // Units that have their mp changed subscribe to this to clear their ID added flag
        public Action onXPCleared; // Units that have their xp changed subscribe to this to clear their ID added flag

        // Server
        int tickCount = 0;

        void Awake()
        {
            if (instance == null) instance = this;
        }

        public override void OnNetworkSpawn()
        {
            NetworkManager.NetworkTickSystem.Tick += ProjectWideTick;
            if (SlotManager.instance.gameStarted == GameState.Started) NetworkManager.NetworkTickSystem.Tick += Tick;
            base.OnNetworkSpawn();
        }

        // ProjectWide Tick
        public void ProjectWideTick()
        {
            currentPingCount = 0;
        }

        // InGame Tick
        public void Tick()
        {
            if (!SlotManager.instance.gameOn) return;

            if (IsServer)
            {
                tickCount++;
                SyncPositionSend();
                SyncDirectPositionSend();
                ResourceSend();
                XPChangeSend();

                if (tickCount == 10)
                {
                    tickCount = 0;
                    HPChangeSend();
                    MPChangeSend();
                }
            }
        }

        // Forces the server to sync with clients by sending all the data 
        public void ForceSend()
        {
            tickCount = 0;
            SyncPositionSend();
            SyncDirectPositionSend();
            ResourceSend();
            XPChangeSend();
            HPChangeSend();
            MPChangeSend();
        }

        // Forces the game to sync MP and HP at the next tick
        public void ForceSync()
        {
            tickCount = 10;
        }

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
                TechnologyManager.instance.OnTechUnlock[player]?.Invoke();

                if (tech.shared)
                {
                    int[] allies = SlotManager.instance.GetPlayerAllies(player);

                    for (int i = 0; i < allies.Length; i++)
                    {
                        if (!TechnologyManager.instance.TechTree[allies[i]][tech])
                        {
                            TechnologyManager.instance.TechTree[allies[i]][tech] = unlocked;
                            // Send a message previously unknown tech was unlocked
                            TechnologyManager.instance.OnTechUnlock[allies[i]]?.Invoke();
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning("Desync on technology with an id " + techID + " (" + tech.displayName + "). It does not exist on player " + SlotManager.instance.currentPlayer);
            }
        }

        // PROCESS - Added / Removed -----------------------------------------------------------------------------------------------------------------------------------
        // Process level is fetched locally, should fetch it from server?

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

        // RESOURCES --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
        public List<Vector3Int> resourceChanged = new List<Vector3Int>(); // [0] = player index, [1] = resource index (GameResources.instance.gameResources), [2] = resource amount

        // Server adds the data through this function
        public void ResourceSendAdd(int player, int resourceType, int resourceAmount)
        {
            // Only for connected players and not server
            if (SlotManager.instance.slotType[player] != SlotType.Player || SlotManager.instance.playerID[player] == 0 || SlotManager.instance.playerID[player] == -1) return;

            bool newResourceEntry = true;
            for (int i = 0; i < resourceChanged.Count; i++)
            {
                if (resourceChanged[i].x == player && resourceChanged[i].y == resourceType)
                {
                    resourceChanged[i] = new Vector3Int(player, resourceType, resourceChanged[i].z + resourceAmount);
                    newResourceEntry = false;
                    break;
                }
            }

            if (newResourceEntry) resourceChanged.Add(new Vector3Int(player, resourceType, resourceAmount));
        }

        // Every tick Server sends to corresponding players resource amounts
        private void ResourceSend()
        {
            if (resourceChanged.Count > 0)
            {
                for (int i = 0; i < resourceChanged.Count; i++)
                {
                    // Only for connected players and not server
                    if (SlotManager.instance.slotType[resourceChanged[i].x] != SlotType.Player || SlotManager.instance.playerID[resourceChanged[i].x] == 0 || SlotManager.instance.playerID[resourceChanged[i].x] == -1) continue;

                    ResourceChangeClientRpc(new Vector2Int(resourceChanged[i].y, GameResources.instance.playerResources[resourceChanged[i].y + resourceChanged[i].x * GameResources.instance.gameResources.Length]), RpcTarget.Single((ulong)SlotManager.instance.playerID[resourceChanged[i].x], RpcTargetUse.Temp));
                }

                resourceChanged.Clear();
            }
        }

        // Changes the resource amount on the clients
        [Rpc(SendTo.SpecifiedInParams)]
        private void ResourceChangeClientRpc(Vector2Int resourceAmount, RpcParams rpcParams)
        {
            if (GameResources.instance.gameResources[resourceAmount.x].type.limited)
            {
                // Limited only max value is changed, current value is changed locally
                GameResources.instance.playerResourceLimits[resourceAmount.x + SlotManager.instance.currentPlayer * GameResources.instance.gameResources.Length] = resourceAmount.y;
            }
            else
            {
                GameResources.instance.playerResources[resourceAmount.x + SlotManager.instance.currentPlayer * GameResources.instance.gameResources.Length] = resourceAmount.y;
            }
            UIManager.instance.UpdateResourceTab(resourceAmount.x);
        }

        // UNIT DIE --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Units should not die until server says so
        // Server send trigger
        public void DieTriggerSend(UInt16 unitID, int killingPlayer, Unit killingUnit, bool rewards, bool destroy)
        {
            UInt16 killingUnitID = (killingUnit == null) ? (UInt16)0 : killingUnit.netID;
            DieClientRpc(unitID, killingPlayer, killingUnitID, rewards, destroy);
        }

        [Rpc(SendTo.NotServer)]
        private void DieClientRpc(UInt16 netID, int killingPlayer, UInt16 killingUnitID, bool rewards, bool destroy)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                if (killingUnitID == 0)
                {
                    // Killing player
                    unit.Die(killingPlayer, null, rewards, false, destroy);
                }
                else
                {
                    // Killing unit
                    if (SlotManager.instance.unitNetID.TryGetValue(killingUnitID, out Unit killingUnit))
                    {
                        unit.Die(killingPlayer, killingUnit, rewards, false, destroy);
                    }
                    else
                    {
                        Debug.LogError("Desync! Unit netID:" + killingUnitID + " should exist on client, but does not! (DieTriggerSend NetworkDataSync)");
                    }
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (DieTriggerSend NetworkDataSync)");
            }
        }

        // HP MP XP --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
        public List<UInt16> hpChangedUnits = new List<UInt16>();
        public List<UInt16> mpChangedUnits = new List<UInt16>();
        public List<UInt16> xpChangedUnits = new List<UInt16>(); // XP sent when units levels up

        // HP -----
        // Every N ticks server gathers units that have changed HP and send info to clients
        private void HPChangeSend()
        {
            if (hpChangedUnits.Count > 0)
            {
                float[] hpAmount = new float[hpChangedUnits.Count];

                for (int i = 0; i < hpChangedUnits.Count; i++)
                {
                    if (SlotManager.instance.unitNetID.TryGetValue(hpChangedUnits[i], out Unit unit))
                    {
                        hpAmount[i] = unit.health;
                    }
                }

                HPChangeClientRpc(hpChangedUnits.ToArray(), hpAmount);
                hpChangedUnits.Clear();
                onHPCleared?.Invoke();
            }
        }

        [Rpc(SendTo.NotServer)]
        private void HPChangeClientRpc(UInt16[] unitID, float[] unitHealth)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            for (int i = 0; i < unitID.Length; i++)
            {
                if (SlotManager.instance.unitNetID.TryGetValue(unitID[i], out Unit unit))
                {
                    unit.SetHP(unitHealth[i]);
                }
                else
                {
                    Debug.LogError("Desync! Unit netID:" + unitID[i] + " should exist on client, but does not! (HPChangeSend NetworkDataSync)");
                }
            }
        }

        // MP -----
        // Every N ticks server gathers units that have changed HP and send info to clients
        private void MPChangeSend()
        {
            if (mpChangedUnits.Count > 0)
            {
                float[] mpAmount = new float[mpChangedUnits.Count];

                for (int i = 0; i < mpChangedUnits.Count; i++)
                {
                    if (SlotManager.instance.unitNetID.TryGetValue(mpChangedUnits[i], out Unit unit))
                    {
                        mpAmount[i] = unit.mana;
                    }
                }

                MPChangeClientRpc(mpChangedUnits.ToArray(), mpAmount);
                mpChangedUnits.Clear();
            }
        }

        [Rpc(SendTo.NotServer)]
        private void MPChangeClientRpc(UInt16[] unitID, float[] unitMana)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            for (int i = 0; i < unitID.Length; i++)
            {
                if (SlotManager.instance.unitNetID.TryGetValue(unitID[i], out Unit unit))
                {
                    unit.SetMP(unitMana[i]);
                }
                else
                {
                    Debug.LogError("Desync! Unit netID:" + unitID[i] + " should exist on client, but does not! (MPChangeSend NetworkDataSync)");
                }
            }
        }

        // XP -----
        // Every N ticks server gathers units that have changed HP and send info to clients
        private void XPChangeSend()
        {
            if (xpChangedUnits.Count > 0)
            {
                int[] xpAmount = new int[xpChangedUnits.Count];
                int[] lvl = new int[xpChangedUnits.Count];
                int[] abilPoints = new int[xpChangedUnits.Count];

                for (int i = 0; i < xpChangedUnits.Count; i++)
                {
                    if (SlotManager.instance.unitNetID.TryGetValue(xpChangedUnits[i], out Unit unit))
                    {
                        xpAmount[i] = unit.levelingUnit.currentExp;
                        lvl[i] = unit.levelingUnit.level;
                        abilPoints[i] = unit.levelingUnit.abilityPoints;
                    }
                }

                XPChangeClientRpc(xpChangedUnits.ToArray(), xpAmount, lvl, abilPoints);
                xpChangedUnits.Clear();
            }

            onXPCleared?.Invoke();
        }

        [Rpc(SendTo.NotServer)]
        private void XPChangeClientRpc(UInt16[] unitID, int[] unitXp, int[] lvl, int[] abilityPoints)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            for (int i = 0; i < unitID.Length; i++)
            {
                if (SlotManager.instance.unitNetID.TryGetValue(unitID[i], out Unit unit))
                {
                    unit.levelingUnit.SetLevel(lvl[i], unitXp[i], abilityPoints[i], true, false, false);
                }
                else
                {
                    Debug.LogError("Desync! Unit netID:" + unitID[i] + " should exist on client, but does not! (XPChangeSend NetworkDataSync)");
                }
            }
        }

        // ABILITY USE --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // ABILITY CAST -----

        // Server send cast signal
        public void AbilityCastStartSend(Unit castingUnit, Ability activeAbility, Unit abilityTarget, Vector3 abilityLocation)
        {
            if (abilityTarget) AbilityCastStartClientRpc(castingUnit.netID, activeAbility.id, abilityTarget.netID);
            else AbilityCastStartClientRpc(castingUnit.netID, activeAbility.id, abilityLocation);
        }

        // Client receives command to cast an ability
        [Rpc(SendTo.NotServer)]
        private void AbilityCastStartClientRpc(UInt16 castingUnitID, int abilityID, UInt16 targetID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(castingUnitID, out Unit castingUnit))
            {
                if (SlotManager.instance.unitNetID.TryGetValue(targetID, out Unit targetUnit))
                {
                    castingUnit.activeAbilityCastTime = 1;
                    castingUnit.activeAbilityUnit = targetUnit;
                    castingUnit.activeAbilityUnit.OnReferenceChange += castingUnit.AbilityUnitReferenceChange;
                    castingUnit.activeAbility = GameManager.instance.gameAbilities[abilityID];
                }
                else
                {
                    Debug.LogError("Desync! Unit netID:" + targetID + " should exist on client, but does not! (AbilityCastUnit NetworkDataSync)");
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + castingUnitID + " should exist on client, but does not! (AbilityCastUnit NetworkDataSync)");
            }
        }

        // Client receives command to cast an ability
        [Rpc(SendTo.NotServer)]
        private void AbilityCastStartClientRpc(UInt16 castingUnitID, int abilityID, Vector3 abilityLocation)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(castingUnitID, out Unit castingUnit))
            {
                castingUnit.activeAbilityCastTime = 1;
                castingUnit.activeAbilityLocation = abilityLocation;
                castingUnit.activeAbility = GameManager.instance.gameAbilities[abilityID];
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + castingUnitID + " should exist on client, but does not! (AbilityCastLocation NetworkDataSync)");
            }
        }

        // ABILITY CAST STOP -------

        // Server send cast stop signal
        public void AbilityStopCastSend(Unit castingUnit)
        {
            AbilityStopCastClientRpc(castingUnit.netID);
        }

        // Client receives command to cast an ability
        [Rpc(SendTo.NotServer)]
        private void AbilityStopCastClientRpc(UInt16 castingUnitID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(castingUnitID, out Unit castingUnit))
            {
                castingUnit.activeAbilityCastTime = 0;
                castingUnit.activeAbilityLocation = Vector3.zero;
                if (castingUnit.activeAbilityUnit != null) castingUnit.activeAbilityUnit.OnReferenceChange -= castingUnit.AbilityUnitReferenceChange;
                castingUnit.activeAbilityUnit = null;
                castingUnit.activeAbility = null;
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + castingUnitID + " should exist on client, but does not! (AbilityStopCastSend NetworkDataSync)");
            }
        }

        // ABILITY USE -------

        // Server send cast signal
        public void AbilityUseSend(Unit castingUnit, Ability ability, int abilityLevel, int abilityIndex, bool isItem, Unit abilityTarget, Vector3 abilityLocation, bool interrupt, int shadowCasterID)
        {
            // 0 == null
            UInt16 targetID = (abilityTarget == null) ? (UInt16)0 : abilityTarget.netID;
            AbilityUseClientRpc(castingUnit.netID, ability.id, abilityLevel, abilityIndex, isItem, targetID, abilityLocation, interrupt, shadowCasterID);
        }

        // Client receives command to immediately cast an ability
        [Rpc(SendTo.NotServer)]
        private void AbilityUseClientRpc(UInt16 castingUnitID, int abilityID, int abilityLevel, int abilityIndex, bool isItem, UInt16 targetID, Vector3 location, bool interrupt, int shadowCasterID, RpcParams rpcParams = default)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(castingUnitID, out Unit castingUnit))
            {
                Unit targetUnit = null;
                if (targetID != 0)
                {
                    if (!SlotManager.instance.unitNetID.TryGetValue(targetID, out targetUnit))
                    {
                        Debug.LogError("Desync! Unit netID:" + targetID + " should exist on client, but does not! (AbilityUseSend NetworkDataSync)");
                        return;
                    }
                }

                castingUnit.activeAbilityCastTime = 0;
                castingUnit.UseAbilityImmediately(GameManager.instance.gameAbilities[abilityID], abilityLevel, abilityIndex, isItem, targetUnit, location, interrupt, shadowCasterID);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + castingUnitID + " should exist on client, but does not! (AbilityUseSend NetworkDataSync)");
            }
        }

        // ACTIVE ABILITY STOP -------

        // Server send signal to stop ability casting
        public void AbilityStopSend(Unit castingUnit)
        {
            AbilityStopClientRpc(castingUnit.netID);
        }

        // Client receives command to stop ability casting
        [Rpc(SendTo.NotServer)]
        private void AbilityStopClientRpc(UInt16 castingUnitID, RpcParams rpcParams = default)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(castingUnitID, out Unit castingUnit))
            {
                castingUnit.EndActiveAbility(true, false);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + castingUnitID + " should exist on client, but does not! (AbilityStopSend NetworkDataSync)");
            }
        }

        // ABILITY LEVEL UP -------

        // Server send levelup info
        public void LevelUpAbilitySend(Unit unit, Ability ability, int abilityIndex)
        {
            LevelUpAbilityClientRpc(unit.netID, ability.id, abilityIndex);
        }

        // Client receives command to level up ability
        [Rpc(SendTo.NotServer)]
        private void LevelUpAbilityClientRpc(UInt16 netID, int abilityID, int abilityIndex)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                unit.LevelUpAbility(GameManager.instance.gameAbilities[abilityID], abilityIndex);

                if (PlayerControl.instance.activeUnit == unit)
                {
                    if (UIManager.instance.isLeveling)
                    {
                        // Successful ability level increase
                        UIManager.instance.ShowLevelButton();
                        UIManager.instance.RedrawAbilityView();
                        if (unit.levelingUnit.abilityPoints == 0)
                        {
                            UIManager.instance.HideLevelButton(true);
                        }
                    }
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (LevelUpAbilitySend NetworkDataSync)");
            }
        }

        // INVISIBILITY SYNC ------------------------------------------
        // Sync invisibility state of the unit

        // Server send info about invisibility state
        public void InvisibilitySetSend(Unit unit, bool state)
        {
            InvisibilitySetClientRpc(unit.netID, state);
        }

        // Client receive info about invisibility state
        [Rpc(SendTo.NotServer)]
        private void InvisibilitySetClientRpc(UInt16 netID, bool state)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                unit.SetInvisibility(state);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (InvisibilitySetSend NetworkDataSync)");
            }
        }

        // STUN SYNC ------------------------------------------
        // Sync stun state of the unit

        // Server send info about stun state
        public void StunSetSend(Unit unit, bool state)
        {
            StunSetClientRpc(unit.netID, state);
        }

        // Client receive info about stun state
        [Rpc(SendTo.NotServer)]
        private void StunSetClientRpc(UInt16 netID, bool state)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                unit.Stun(state);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (StunSetSend NetworkDataSync)");
            }
        }

        // MUTE SYNC ------------------------------------------

        // Server send info about state
        public void MuteSetSend(Unit unit, bool state)
        {
            MuteSetClientRpc(unit.netID, state);
        }

        // Client receive info about state
        [Rpc(SendTo.NotServer)]
        private void MuteSetClientRpc(UInt16 netID, bool state)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                unit.Mute(state);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (MuteSetSend NetworkDataSync)");
            }
        }

        // DISARM SYNC ------------------------------------------

        // Server send info about state
        public void DisarmSetSend(Unit unit, bool state)
        {
            DisarmSetClientRpc(unit.netID, state);
        }

        // Client receive info about state
        [Rpc(SendTo.NotServer)]
        private void DisarmSetClientRpc(UInt16 netID, bool state)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                unit.Disarm(state);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (DisarmSetClientRpc NetworkDataSync)");
            }
        }

        // SHADOWCASTER SYNC ------------------------------------------

        // Only for syncing manually spawned shadowcasters
        [Rpc(SendTo.NotServer)]
        public void ShadowCasterSpawnClientRpc(int shadowCasterID, UInt16 castingUnitID, int abilityID, int abilityLevel, UInt16 targetID, Vector3 targetPosition, float range, float duration)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(castingUnitID, out Unit castingUnit))
            {
                Unit targetUnit = null;
                if (targetID != 0)
                {
                    if (!SlotManager.instance.unitNetID.TryGetValue(targetID, out targetUnit))
                    {
                        Debug.LogError("Desync! Unit netID:" + targetID + " should exist on client, but does not! (ShadowCasterSpawn NetworkDataSync)");
                        return;
                    }
                }

                ShadowCaster sc = ShadowCaster.Spawn(shadowCasterID, castingUnit, castingUnit.owner, GameManager.instance.gameAbilities[abilityID], abilityLevel, targetUnit, targetPosition, range, duration);

                // Activate
                if (targetUnit) GameManager.instance.gameAbilities[abilityID].Activate(castingUnit, castingUnit.owner, abilityLevel, targetUnit, ref sc.activeAbilityVFX);
                else if (targetPosition != Vector3.zero) GameManager.instance.gameAbilities[abilityID].Activate(castingUnit, castingUnit.owner, abilityLevel, targetPosition, ref sc.activeAbilityVFX);
                else GameManager.instance.gameAbilities[abilityID].Activate(castingUnit, castingUnit.owner, abilityLevel, ref sc.activeAbilityVFX);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + castingUnitID + " should exist on client, but does not! (ShadowCasterSpawn NetworkDataSync)");
            }
        }

        // Client receive removal trigger
        [Rpc(SendTo.NotServer)]
        public void ShadowCasterRemoveClientRpc(int shadowcasterID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            // Remove from shadowcaster tracker
            int index = GameManager.instance.shadowCasterIDs.IndexOf(shadowcasterID);
            if (index != -1)
            {
                GameManager.instance.shadowCasters[index].Remove();
            }
        }

        // ANIMATION BLENDING SYNC ------------------------------------------

        // Change animation blending index for a specified unit
        [Rpc(SendTo.NotServer)]
        public void AnimationPrefixClientRpc(UInt16 netID, float blendingIndex)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
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
        [Rpc(SendTo.NotServer)]
        private void WorkerStartConstructingClientRpc(UInt16 buildingNetID, UInt16 workerNetID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(buildingNetID, out Unit building))
            {
                if (SlotManager.instance.unitNetID.TryGetValue(workerNetID, out Unit worker))
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
        [Rpc(SendTo.NotServer)]
        private void WorkerStopConstructingClientRpc(UInt16 buildingNetID, UInt16 workerNetID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(workerNetID, out Unit worker))
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
        [Rpc(SendTo.NotServer)]
        private void WorkerStartTheRepairsClientRpc(UInt16 workerNetID, UInt16 buildingNetID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(buildingNetID, out Unit building))
            {
                if (SlotManager.instance.unitNetID.TryGetValue(workerNetID, out Unit worker))
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
        [Rpc(SendTo.NotServer)]
        private void WorkerStopTheRepairsClientRpc(UInt16 workerNetID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(workerNetID, out Unit worker))
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
        [Rpc(SendTo.NotServer)]
        public void BuildingFinishedClientRpc(UInt16 buildingNetID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(buildingNetID, out Unit building))
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
            if (SlotManager.instance.slotType[worker.owner] != SlotType.Player || SlotManager.instance.playerID[worker.owner] == 0 || SlotManager.instance.playerID[worker.owner] == -1) return;

            ResetConstructionStateClientRpc(worker.netID, limitedOnly, RpcTarget.Single((ulong)SlotManager.instance.playerID[worker.owner], RpcTargetUse.Temp));
        }

        // Client receive: Worker clear state
        [Rpc(SendTo.SpecifiedInParams)]
        private void ResetConstructionStateClientRpc(UInt16 workerNetID, bool limitedOnly, RpcParams rpcParams)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(workerNetID, out Unit worker))
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
            if (SlotManager.instance.slotType[worker.owner] != SlotType.Player || SlotManager.instance.playerID[worker.owner] == 0 || SlotManager.instance.playerID[worker.owner] == -1) return;

            WorkerResetShadowBuildingClientRpc(worker.netID, RpcTarget.Single((ulong)SlotManager.instance.playerID[worker.owner], RpcTargetUse.Temp));
        }

        // Client receive: Worker clear state
        [Rpc(SendTo.SpecifiedInParams)]
        private void WorkerResetShadowBuildingClientRpc(UInt16 workerNetID, RpcParams rpcParams)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(workerNetID, out Unit worker))
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
        [Rpc(SendTo.NotServer)]
        public void ConstructionCancelClientRpc(UInt16 upgradeBuildingNetID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(upgradeBuildingNetID, out Unit upgradeBuilding))
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
        [Rpc(SendTo.NotServer)]
        private void EmbarkClientRpc(UInt16 transportUnitID, UInt16 embarkedUnitID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(transportUnitID, out Unit transportUnit))
            {
                if (SlotManager.instance.unitNetID.TryGetValue(embarkedUnitID, out Unit embarkedUnit))
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
        [Rpc(SendTo.NotServer)]
        private void DisembarkClientRpc(UInt16 transportUnitID, int index, Vector3 location)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(transportUnitID, out Unit transportUnit))
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
        [Rpc(SendTo.NotServer)]
        private void DisembarkClientRpc(UInt16 transportUnitID, int unitsOut, Vector3[] locations)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(transportUnitID, out Unit transportUnit))
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
        [Rpc(SendTo.NotServer)]
        private void UnitSpawnClientRpc(int typeID, Vector3 position, float rotation, int owner, UInt16 netID = 0)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            // Get unit type to spawn
            if (GameManager.instance.gameUnits.TryGetValue(typeID, out Unit unitRef))
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
        [Rpc(SendTo.NotServer)]
        private void ItemDroppedSpawnClientRpc(int abilityID, int itemCharges, float itemcd, Vector3 position, UInt16 netID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (GameManager.instance.gameAbilities.TryGetValue(abilityID, out Ability item))
            {
                ItemDropped.SpawnInternal(item, itemCharges, itemcd, position, netID);
            }
            else
            {
                Debug.LogError("Desync! Ability ID:" + abilityID + " should exist on client, but does not! (ItemDroppedSpawn NetworkDataSync)");
            }
        }

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
        [Rpc(SendTo.SpecifiedInParams)]
        public void PlayerListClientRpc(int playerSlot, string slotType, string playerID, string playerTeam, string playerName, string playerPosition, string playerFaction, string playerLost, RpcParams rpcParams)
        {
            SlotManager.instance.slotType = JsonHelper.FromJson<SlotType>(slotType);
            SlotManager.instance.playerID = JsonHelper.FromJson<int>(playerID);
            SlotManager.instance.playerTeam = JsonHelper.FromJson<int>(playerTeam);
            SlotManager.instance.playerName = JsonHelper.FromJson<string>(playerName);
            SlotManager.instance.playerPosition = JsonHelper.FromJson<int>(playerPosition);
            SlotManager.instance.playerFaction = JsonHelper.FromJson<int>(playerFaction);
            SlotManager.instance.playerLost = JsonHelper.FromJson<bool>(playerLost);
            SlotManager.instance.SetCurrentPlayer(playerSlot);

            if (SlotManager.instance.gameStarted == GameState.Menu) UIManagerMenu.instance.FillPlayerList();
        }

        // Clients receive the list of players in the game
        [Rpc(SendTo.NotServer)]
        public void PlayersListClientRpc(string slotType, string playerID, string playerTeam, string playerName, string playerPosition, string playerFaction, string playerLost)
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

            if (SlotManager.instance.gameStarted == GameState.Menu) UIManagerMenu.instance.FillPlayerList();
        }

        // WIN LOSE -----

        // Server sends data to certain player that he won
        public void PlayerWinsSend(int player)
        {
            PlayerWinsClientRpc(RpcTarget.Single((ulong)SlotManager.instance.playerID[player], RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        public void PlayerWinsClientRpc(RpcParams rpcParams)
        {
            SlotManager.instance.PlayerWins();
        }

        // Server sends data to certain player that he lost
        public void PlayerLosesSend(int player)
        {
            PlayerLosesClientRpc(RpcTarget.Single((ulong)SlotManager.instance.playerID[player], RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        public void PlayerLosesClientRpc(RpcParams rpcParams)
        {
            SlotManager.instance.PlayerLoses();
        }

        // Servers sends data to certain player that his team lost
        public void TeamLosesSend(int player)
        {
            TeamLosesClientRpc(RpcTarget.Single((ulong)SlotManager.instance.playerID[player], RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        public void TeamLosesClientRpc(RpcParams rpcParams)
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
        [Rpc(SendTo.NotServer)]
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

        // ----- MID GAME JOIN ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- 

        // Scene Data send to midgame connected client
        // Saves the scene data using savemanager and sends to a client
        public void SendSceneData(ulong clientID, bool midGame)
        {
            string sceneData = SaveManager.SaveToFile(true);

            byte[] utf8Bytes = Encoding.UTF8.GetBytes(sceneData); // Convert to UTF-8 bytes
            int totalChunks = Mathf.CeilToInt((float)utf8Bytes.Length / CHUNK_SIZE);

            for (int i = 0; i < totalChunks; i++)
            {
                int startIndex = i * CHUNK_SIZE;
                int length = Mathf.Min(CHUNK_SIZE, utf8Bytes.Length - startIndex);
                byte[] chunk = new byte[length];

                System.Array.Copy(utf8Bytes, startIndex, chunk, 0, length);

                ReceiveSceneDataClientRpc(chunk, i, totalChunks, midGame, RpcTarget.Single(clientID, RpcTargetUse.Temp));
            }
        }

        // Client receive: Worker clear state
        // Receives save data from the server and loads it
        [Rpc(SendTo.SpecifiedInParams)]
        private void ReceiveSceneDataClientRpc(byte[] chunk, int index, int totalChunks, bool isMidGame, RpcParams rpcParams)
        {
            NetworkConnectionHandler.instance.connectionStage = 2;
            ReceiveChunk(chunk, index, totalChunks);
        }

        // CLIENT RECEIVE CHUNKS OF SCENE DATA ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- ----- 
        private void ReceiveChunk(byte[] chunk, int index, int totalChunks)
        {
            receivedChunks[index] = chunk;

            if (totalChunksExpected == -1)
            {
                totalChunksExpected = totalChunks;
            }

            if (receivedChunks.Count == totalChunksExpected)
            {
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

        [Rpc(SendTo.NotServer)]
        public void PauseTheGameClientRpc()
        {
            NetworkConnectionHandler.instance.PauseTheGame();
        }

        public void ResumeTheGameSend(bool clearSaves = false, bool initializeUnitData = false)
        {
            NetworkDataSync.instance.ResumeTheGameClientRpc(clearSaves, initializeUnitData);
        }

        [Rpc(SendTo.NotServer)]
        public void ResumeTheGameClientRpc(bool clearSaves = false, bool initializeUnitData = false)
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

        [Rpc(SendTo.SpecifiedInParams)]
        private void MsgSendClientRpc(int owner, string message, bool allyChat, RpcParams rpcParams)
        {
            if (SlotManager.instance.gameStarted == GameState.Menu) UIManagerMenu.instance.AddChatMsg(message, owner);
            else
            {
                UIManager.instance.AddChatMsg(message, owner, allyChat);
                UIManager.instance.ShowChatBox();
            }
        }

        // Server chat MSG
        public void ServerMsgSend(string message)
        {
            // Send the data
            ServerMsgClientRpc(message);
        }

        [Rpc(SendTo.NotServer)]
        private void ServerMsgClientRpc(string message)
        {
            if (SlotManager.instance.gameStarted == GameState.Menu) UIManagerMenu.instance.AddChatServerMsg(message);
            else UIManager.instance.AddChatServerMsg(message);
        }

        // GAME MESSAGES --------------------------------------------------------------------------------------------------------------------------------------------------------------------

        public void GameMsgSend(string message, int player)
        {
            // Only for connected players
            if (SlotManager.instance.slotType[player] != SlotType.Player) return;

            GameMsgClientRpc(message, RpcTarget.Single((ulong)SlotManager.instance.playerID[player], RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        public void GameMsgClientRpc(string message, RpcParams rpcParams)
        {
            UIManager.instance.ShowNotifyMsg(message);
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

        [Rpc(SendTo.SpecifiedInParams)]
        private void MiniMapPingClientRpc(int owner, Vector2 pos, RpcParams rpcParams)
        {
            UIManager.instance.CreatePinger(owner, pos);
        }

        // POSITION SYNC --------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // ------ DIRECT POSITION SET ------

        // Server send unit position
        public void SetPositionDirect(Unit unit)
        {
            NetworkDataSync.instance.directPositionSyncList.Add(unit.netID);
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
                posX[i] = (UInt16)(SlotManager.instance.unitNetID[directPositionSyncList[i]].transform.position.x * 100f); //BitConverter.ToUInt16(BitConverter.GetBytes(unitNetID[syncList[i]].transform.position.x), 0);
                posY[i] = (UInt16)(SlotManager.instance.unitNetID[directPositionSyncList[i]].transform.position.z * 100f); //BitConverter.ToUInt16(BitConverter.GetBytes(unitNetID[syncList[i]].transform.position.z), 0);
            }

            NetworkDataSync.instance.SetPositionDirectClientRpc(directPositionSyncList.ToArray(), posX, posY);

            directPositionSyncList.Clear();
        }

        // Client receive info about unit position
        [Rpc(SendTo.NotServer)]
        private void SetPositionDirectClientRpc(UInt16[] netID, UInt16[] posX, UInt16[] posY)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            for (int i = 0; i < netID.Length; i++)
            {
                if (SlotManager.instance.unitNetID.TryGetValue(netID[i], out Unit unit))
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
                    FogOfWar.instance.CellAssignment(unit);
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
                Unit unit = SlotManager.instance.unitNetID[positionSyncList[i]];
                posX[i] = (float)unit.transform.position.x; // (UInt16)( * 100f); //BitConverter.ToUInt16(BitConverter.GetBytes(unitNetID[syncList[i]].transform.position.x), 0);
                posY[i] = (float)unit.transform.position.z; // (UInt16)(SlotManager.instance.unitNetID[positionSyncList[i]].transform.position.z * 100f); //BitConverter.ToUInt16(BitConverter.GetBytes(unitNetID[syncList[i]].transform.position.z), 0);

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
                posX[positionSyncList.Count + i] = -1f; // Max of UInt16 (Not used)
                posY[positionSyncList.Count + i] = -1f; // Indication that it is removal position
            }

            // Send the information. We combine both lists into an array and send to the clients
            var sendArray = new UInt16[positionSyncList.Count + removeSyncList.Count];
            positionSyncList.ToArray().CopyTo(sendArray, 0);
            removeSyncList.ToArray().CopyTo(sendArray, positionSyncList.Count);

            // Remove from position sync
            for (int i = 0; i < positionSentReset.Count; i++)
            {
                NetworkDataSync.instance.positionSyncList.Remove(positionSentReset[i]);
            }

            SyncPositionClientRpc(sendArray, posX, posY, NetworkManager.ServerTime.TimeAsFloat);

            removeSyncList.Clear();
        }

        // Sync the position of units on clients
        //[Rpc(SendTo.NotServer)]

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Reliable)]
        private void SyncPositionClientRpc(UInt16[] id, float[] posX, float[] posY, float serverTime)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            for (int i = 0; i < id.Length; i++)
            {
                // unitNetID[id[i]].PositionSetDirect(new Vector2(((float)posX[i]) * 0.01f, ((float)posY[i]) * 0.01f), avgPing); 
                // SlotManager.instance.unitNetID[id[i]].PositionSetDirect(new Vector2((float)posX[i], (float)posY[i]), serverTime);

                // Latency is for smooth interpolation of the position on the client
                float latencySec = NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId) * 0.0005f; // 0.001f to convert s to ms, 0.5f to get only half ot the round trip ping
                float tickRate = (float)(1f / NetworkManager.NetworkTickSystem.TickRate);
                //SlotManager.instance.unitNetID[id[i]].PositionSetDirect(new Vector2(((float)posX[i]) * 0.01f, ((float)posY[i]) * 0.01f), latencySec);

                // SlotManager.instance.unitNetID[id[i]].PositionSetDirect(new Vector2((float)posX[i], (float)posY[i]), tickRate);

                if (latencySec > tickRate) SlotManager.instance.unitNetID[id[i]].PositionSetDirect(new Vector2(posX[i], posY[i]), latencySec);
                else SlotManager.instance.unitNetID[id[i]].PositionSetDirect(new Vector2(posX[i], posY[i]), tickRate);
            }
        }

        // ATTACK SYNC --------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // TARGET ACQUIRED -----

        // On the server unit acquires target, we then send to all clients this info including acquisition time for proper attack sync
        public void TargetAcquired(UInt16 netID, UInt16 targetNetID, float cd)
        {
            TargetAcquiredClientRpc(netID, targetNetID, NetworkManager.ServerTime.TimeAsFloat, cd);
        }

        public void TargetAcquired(UInt16 netID, Vector2 targetGround, float cd)
        {
            TargetAcquiredClientRpc(netID, targetGround, NetworkManager.ServerTime.TimeAsFloat, cd);
        }

        // Clients receive target and calculate the cooldown of the initial attack to sync properly with the server
        [Rpc(SendTo.NotServer)]
        private void TargetAcquiredClientRpc(UInt16 netID, UInt16 targetNetID, float startTime, float cd)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                if (SlotManager.instance.unitNetID.TryGetValue(targetNetID, out Unit target))
                {
                    TargetAcquiredInternal(unit, target, Vector2.zero, startTime, cd);
                }
                else
                {
                    Debug.LogError("Desync! Unit targetNetID:" + targetNetID + " should exist on client, but does not! (TargetAcquired NetworkDataSync)");
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (TargetAcquired NetworkDataSync)");
            }
        }

        [Rpc(SendTo.NotServer)]
        private void TargetAcquiredClientRpc(UInt16 netID, Vector2 targetGround, float startTime, float cd)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                TargetAcquiredInternal(unit, null, targetGround, startTime, cd);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (TargetAcquired NetworkDataSync)");
            }
        }

        private void TargetAcquiredInternal(Unit unit, Unit target, Vector2 targetGround, float startTime, float cd)
        {
            bool isTargetGround = (targetGround == Vector2.zero) ? false : true;

            float timeDiff = NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId) * 0.000525f; // 0.001f * 0.5f * 1.05f; // 0.001f = convert to seconds, 0.5f = only from server to client, 1.05f = take into account jitter
            float nextAttack = (cd <= 0) ? unit.attackSpeed : cd; // Time till next attack, if cd is 0 it is going to be a attack speed
            //nextAttack -= unit.currentAnimAttackDelay; // Adjust nextAttack time for animationAttackDelay

            // NetCD means we do an attack immediately and then set the cooldown to provided value
            bool netCD = (unit.attackType != AttackType.Continuous && (cd <= 0 || cd <= timeDiff)) ? true : false;

            // If attack start time is higher than ping, it means attack will happen in the future, we just change attack cooldown
            if (nextAttack >= timeDiff)
            {
                float tillNextAttack = nextAttack - timeDiff;
                if (unit.attackType == AttackType.Continuous) tillNextAttack = -timeDiff; // For continuous - attack starts at -unit.attackSpeed

                if (isTargetGround) unit.TargetSet(targetGround, tillNextAttack, 0, netCD);
                else unit.TargetSet(target, tillNextAttack, 0, netCD);
            }
            // Attack already happened, we got the message late, sync properly
            else
            {
                if (unit.attackType == AttackType.Continuous)
                {
                    // For continius attack we start the attack immediately 
                    if (isTargetGround) unit.TargetSet(targetGround, -unit.attackSpeed + unit.currentAnimAttackDelay);
                    else unit.TargetSet(target, -unit.attackSpeed + unit.currentAnimAttackDelay);
                }
                else if (unit.attackType == AttackType.Standard)
                {
                    // Stardard attack type
                    float diff = timeDiff - nextAttack;
                    float reminder = diff % unit.attackSpeed;
                    float tillNextAttack = unit.attackSpeed - reminder;

                    if (isTargetGround) unit.TargetSet(targetGround, tillNextAttack, 0, netCD);
                    else unit.TargetSet(target, tillNextAttack, 0, netCD);
                }
                else
                {
                    // Periodic attack type
                    // Check how many attack cycles we missed
                    float diff = timeDiff - nextAttack;
                    float totalAttackTime = unit.attackSpeed + (unit.periodicAttackCount - 1) * unit.periodicAttackDelay;
                    float reminder = diff % totalAttackTime;
                    float tillNextAttack = totalAttackTime - reminder;

                    if (tillNextAttack >= unit.currentAnimAttackDelay && tillNextAttack <= unit.attackSpeed)
                    {
                        // Yet to make a main attack, set it
                        if (isTargetGround) unit.TargetSet(targetGround, tillNextAttack, 0, netCD);
                        else unit.TargetSet(target, tillNextAttack, 0, netCD);
                    }
                    else
                    {
                        // Performing periodic attack
                        float attacksPassed = reminder / unit.periodicAttackDelay;
                        float cooldownSinceLastAttack = (attacksPassed - Mathf.Floor(attacksPassed)) * unit.periodicAttackDelay;
                        float tillNextPeriodicAttack = unit.periodicAttackDelay - cooldownSinceLastAttack;
                        int attackCount = (int)(unit.periodicAttackCount - Mathf.Ceil(attacksPassed)); // Ceil beacause 1 for main attack and additional for current periodic attack

                        if (attackCount <= 0)
                        {
                            // We have missed last periodic attack, do main attack
                            if (isTargetGround) unit.TargetSet(targetGround, tillNextAttack, 0, netCD);
                            else unit.TargetSet(target, tillNextAttack, 0, netCD);
                        }
                        else
                        {
                            // Sync with the next periodic attack
                            if (isTargetGround) unit.TargetSet(targetGround, tillNextPeriodicAttack, attackCount, netCD);
                            else unit.TargetSet(target, tillNextPeriodicAttack, attackCount, netCD);
                        }
                    }
                }
            }
        }

        // TARGET LOST -----

        // Server unit lost the target, send info to clients
        public void TargetLost(UInt16 netID)
        {
            TargetLostClientRpc(netID);
        }

        // Clients receive target lose info and set to client units
        [Rpc(SendTo.NotServer)]
        private void TargetLostClientRpc(UInt16 netID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                unit.TargetSet(false);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (TargetLost NetworkDataSync)");
            }
        }

        // ADDITIONAL TARGET SET ----- FOR MULTITARGET

        // Server unit lost the target, send info to clients
        public void AdditionalTargetsSend(Unit unit, Unit[] targets)
        {
            UInt16[] targetIDs = new UInt16[targets.Length];

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null) targetIDs[i] = targets[i].netID;
                else targetIDs[i] = 0;
            }

            AdditionalTargetsClientRpc(unit.netID, targetIDs);
        }

        // Clients receive target lose info and set to client units
        [Rpc(SendTo.NotServer)]
        private void AdditionalTargetsClientRpc(UInt16 netID, UInt16[] netIDs)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                Unit[] targets = new Unit[netIDs.Length];

                for (int i = 0; i < netIDs.Length; i++)
                {
                    if (netIDs[i] == 0) continue;

                    if (SlotManager.instance.unitNetID.TryGetValue(netIDs[i], out Unit target))
                    {
                        targets[i] = target;
                    }
                    else
                    {
                        Debug.LogError("Desync! Unit netID:" + netIDs[i] + " should exist on client, but does not! (AdditionalTargetsSend NetworkDataSync)");
                    }
                }

                unit.additionalTargets = targets;
            }
        }

        // ANIMATION RESET ------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Send info on the server
        public void PlayIdleAnim(UInt16 netID)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            PlayIdleAnimClientRpc(netID);
        }

        // Clients receive trigger to play idle animation
        [Rpc(SendTo.NotServer)]
        private void PlayIdleAnimClientRpc(UInt16 netID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                if (!unit.isMoving && unit.animator)
                    unit.animator.CrossFade("idle0", unit.crossFadeTime, 0, 0.01f);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (PlayIdleAnim NetworkDataSync)");
            }
        }

        // FLOATING TEXT --------------------------------------------------------------------------------------------------------------------------------------------------------------------

        public void FloatingTextSend(int player, Vector3 position, string text, Color color)
        {
            if (player == -1)
            {
                FloatingTextAllClientRpc(position, text, color);
            }
            else
            {
                // Only for connected players and not server
                if (SlotManager.instance.slotType[player] != SlotType.Player || SlotManager.instance.playerID[player] == 0 || SlotManager.instance.playerID[player] == -1) return;

                FloatingTextClientRpc(position, text, color, RpcTarget.Single((ulong)SlotManager.instance.playerID[player], RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void FloatingTextClientRpc(Vector3 position, string text, Color color, RpcParams rpcParams)
        {
            FloatingText.Spawn(-1, position, text, color, false);
        }

        [Rpc(SendTo.NotServer)]
        private void FloatingTextAllClientRpc(Vector3 position, string text, Color color)
        {
            FloatingText.Spawn(-1, position, text, color, false);
        }

        // WAYPOINT SYNC --------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Server send the waypoint information
        public void WaypointSet(Unit unit, Unit waypointUnit, Vector2 waypointLocation)
        {
            // Only for connected players and not server
            if (SlotManager.instance.slotType[unit.owner] != SlotType.Player || SlotManager.instance.playerID[unit.owner] == 0 || SlotManager.instance.playerID[unit.owner] == -1) return;

            UInt16 wayNetID = (ushort)((waypointUnit == null) ? 0 : waypointUnit.netID);

            WaypointSetClientRpc(unit.netID, wayNetID, waypointLocation, RpcTarget.Single((ulong)SlotManager.instance.playerID[unit.owner], RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void WaypointSetClientRpc(UInt16 netID, UInt16 wayNetID, Vector2 position, RpcParams rpcParams)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                if (wayNetID == 0 && position == Vector2.zero)
                {
                    // Null
                    unit.SetWaypointDirect(null, Vector2.zero);
                }
                else if (position != Vector2.zero)
                {
                    // Position
                    unit.SetWaypointDirect(null, position);
                }
                else
                {
                    if (SlotManager.instance.unitNetID.TryGetValue(wayNetID, out Unit wayUnit))
                    {
                        // Unit
                        unit.SetWaypointDirect(wayUnit, Vector2.zero);
                    }
                    else
                    {
                        Debug.LogError("Desync! Unit netID:" + wayNetID + " should exist on client, but does not! (WaypointSet NetworkDataSync)");
                    }
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (WaypointSet NetworkDataSync)");
            }
        }
    }
}
