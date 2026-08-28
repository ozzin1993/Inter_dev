using UnityEngine;
using Unity.Netcode;
using System;

namespace StrategyCore
{
    public class NetworkCommandSync : NetworkBehaviour
    {
        public static NetworkCommandSync Instance { get; private set; }

        void Awake()
        {
            if (Instance == null) Instance = this;
        }

        // For sending commads from clients to server

        // COMMANDS --------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // STOP DISTANCE FOR FOLLOW CALCULATE ON UNIT?

        // IDLE --------------

        // Local player will send to server command
        public void IdleCommandSend(Unit unit) { IdleCommandServerRpc(unit.netID); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void IdleCommandServerRpc(UInt16 netID, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                unit.Idle(true);
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (IdleCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (IdleCommandSend NetworkCommandSync)");
            }
        }

        // HOLD --------------

        // Local player will send to server command
        public void HoldCommandSend(Unit unit) { HoldCommandServerRpc(unit.netID); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void HoldCommandServerRpc(UInt16 netID, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                unit.Hold(true);
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (HoldCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (HoldCommandSend NetworkCommandSync)");
            }
        }

        // FOLLOW --------------

        // Local player will send to server command and play the command on local Unit
        public void FollowCommandSend(Unit unit, Unit followUnit, float stopDistance = 0, bool embark = false) { FollowCommandServerRpc(unit.netID, followUnit.netID, stopDistance, embark); }

        // Server will receive command from the player
        [Rpc(SendTo.Server)]
        public void FollowCommandServerRpc(UInt16 netID, UInt16 targetUnitID, float stopDistance, bool embark, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(targetUnitID, out Unit targetUnit))
                {
                    unit.Follow(targetUnit, stopDistance, embark, true);
                }
                else
                {
                    Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + targetUnitID + "! (FollowCommandSend NetworkCommandSync)");
                }
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (FollowCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (FollowCommandSend NetworkCommandSync)");
            }
        }

        // MOVE --------------

        // Local player will send to server command and play the command on local Unit
        public void MoveCommandSend(Unit unit, Vector2 destination, float stopDistance = 0) { MoveCommandServerRpc(unit.netID, destination, stopDistance); }
        public void MoveCommandSend(Unit unit, Vector3 destination, float stopDistance = 0) { MoveCommandServerRpc(unit.netID, new Vector2(destination.x, destination.z), stopDistance); }

        // Server will receive command from the player
        [Rpc(SendTo.Server)]
        public void MoveCommandServerRpc(UInt16 netID, Vector2 destination, float stopDistance, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                unit.Move(destination, stopDistance, true);
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (MoveCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (MoveCommandSend NetworkCommandSync)");
            }
        }

        // ATTACK --------------

        // Local player will send to server command
        public void AttackCommandSend(Unit unit, Unit attackUnit) { AttackCommandServerRpc(unit.netID, attackUnit.netID); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void AttackCommandServerRpc(UInt16 netID, UInt16 targetUnitID, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(targetUnitID, out Unit targetUnit))
                {
                    unit.AttackVerify(targetUnit, true);
                }
                else
                {
                    Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + targetUnitID + "! (AttackCommandSend NetworkCommandSync)");
                }
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (AttackCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (AttackCommandSend NetworkCommandSync)");
            }
        }

        // ATTACK POSITION --------------

        // Local player will send to server command
        public void AttackPositionCommandSend(Unit unit, Vector2 attackPosition) { AttackPositionCommandServerRpc(unit.netID, attackPosition); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void AttackPositionCommandServerRpc(UInt16 netID, Vector2 attackPosition, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                unit.Attack(attackPosition, true);
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (AttackPositionCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (AttackPositionCommandSend NetworkCommandSync)");
            }
        }

        // ATTACK MOVE --------------

        // Local player will send to server command
        public void AttackMoveCommandSend(Unit unit, Vector2 attackPosition) { AttackMoveCommandServerRpc(unit.netID, attackPosition); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void AttackMoveCommandServerRpc(UInt16 netID, Vector2 attackPosition, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                unit.AttackMove(attackPosition, true);
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (AttackMoveCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (AttackMoveCommandSend NetworkCommandSync)");
            }
        }

        // USE ABILITY ITEM --------------

        // Local player will send to server command
        public void UseAbilityCommandSend(Unit unit, int abilityIndex, bool isItem, Unit target, Vector3 position)
        {
            if (target) UseAbilityUnitCommandServerRpc(unit.netID, abilityIndex, isItem, target.netID);
            else if (position != Vector3.zero) UseAbilityPositionCommandServerRpc(unit.netID, abilityIndex, isItem, position);
            else UseAbilityCommandServerRpc(unit.netID, abilityIndex, isItem);
        }

        // FOR NO TARGETABLE ABILITY
        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void UseAbilityCommandServerRpc(UInt16 netID, int abilityIndex, bool isItem, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                bool success = unit.UseAbilityItem(abilityIndex, isItem, null, Vector3.zero, true);
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (UseAbilityCommandSend NoTarget&Location NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (UseAbilityCommandSend NoTarget&Location NetworkCommandSync)");
            }
        }

        // FOR UNITS
        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void UseAbilityUnitCommandServerRpc(UInt16 netID, int abilityIndex, bool isItem, UInt16 targetUnitID, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(targetUnitID, out Unit targetUnit))
                {
                    bool success = unit.UseAbilityItem(abilityIndex, isItem, targetUnit, Vector3.zero, true);
                }
                else
                {
                    Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + targetUnitID + "! (UseAbilityCommandSend Target NetworkCommandSync)");
                }
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (UseAbilityCommandSend Target NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (UseAbilityCommandSend Target NetworkCommandSync)");
            }
        }

        // FOR POSITIONS
        [Rpc(SendTo.Server)]
        public void UseAbilityPositionCommandServerRpc(UInt16 netID, int abilityIndex, bool isItem, Vector3 position, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                bool success = unit.UseAbilityItem(abilityIndex, isItem, null, position, true);
                
                if (!success)
                {
                    Ability currentAbility = Utils.GetAbilityByIndex(unit, abilityIndex);
                    if (currentAbility is Construction)
                    {
                        // If construction, we remove shadowBuilding on client
                        NetworkDataSync.Instance.WorkerResetShadowBuilding(unit);
                    }
                }
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (UseAbilityCommandSend Location NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (UseAbilityCommandSend Location NetworkCommandSync)");
            }
        }

        // ABILITY LEVEL UP --------------

        // Local player will send to server command
        public void LevelUpAbility(Unit unit, Ability ability, int abilityIndex) { LevelUpAbilityServerRpc(unit.netID, ability.id, abilityIndex); }

        // Server will receive command from the player and try to level up the ability
        [Rpc(SendTo.Server)]
        public void LevelUpAbilityServerRpc(UInt16 netID, int abilityID, int abilityIndex, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                if (unit.levelingUnit)
                {
                    unit.LevelUpAbilityCommand(GameManager.Instance.gameAbilities[abilityID], abilityIndex);
                }
                else Debug.LogError("Desync! Client " + owner + " send a command to Unit without component netID:" + netID + "! (LevelUpAbility NetworkCommandSync)");
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (LevelUpAbility NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (LevelUpAbility NetworkCommandSync)");
            }
        }

        // PROCESSES --------------

        // ADD PROCESS ---------

        // Local player will send to server command
        public void AddProcessCommandSend(Unit unit, int abilityIndex, bool isItem) { AddProcessCommandServerRpc(unit.netID, abilityIndex, isItem); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void AddProcessCommandServerRpc(UInt16 netID, int abilityIndex, bool isItem, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                unit.AddProcess(abilityIndex, isItem);
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (AddProcessCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (AddProcessCommandSend NetworkCommandSync)");
            }
        }

        // CANCEL PROCESS ---------

        // Local player will send to server command
        public void CancelProcessCommandSend(Unit unit, int index) { CancelProcessCommandServerRpc(unit.netID, index); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void CancelProcessCommandServerRpc(UInt16 netID, int index, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                unit.CancelProcess(index);
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (CancelProcessCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (CancelProcessCommandSend NetworkCommandSync)");
            }
        }

        // INVENTORY --------------

        // BUY ITEM --------------

        // Local player will send to server command
        public void BuyItemCommandSend(Unit shopUnit, Unit shoppingUnit, int abilityIndex) { BuyItemCommandServerRpc(shopUnit.netID, shoppingUnit.netID, abilityIndex); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void BuyItemCommandServerRpc(UInt16 netID, UInt16 targetUnitID, int abilityIndex, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(targetUnitID, out Unit targetUnit))
                {
                    unit.BuyItem(abilityIndex, targetUnit);
                }
                else
                {
                    Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + targetUnitID + "! (BuyItemCommandSend NetworkCommandSync)");
                }
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (BuyItemCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (BuyItemCommandSend NetworkCommandSync)");
            }
        }

        // SELL ITEM --------------

        // Local player will send to server command
        public void SellItemCommandSend(Unit unit, int itemIndex) { SellItemCommandServerRpc(unit.netID, itemIndex); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void SellItemCommandServerRpc(UInt16 netID, int itemIndex, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                unit.SellItem(itemIndex);
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (SellItemCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (SellItemCommandSend NetworkCommandSync)");
            }
        }

        // DROP ITEM --------------

        // DROP AROUND UNIT

        // Local player will send to server command
        public void DropItemCommandSend(Unit unit, int itemIndex) { DropItemCommandServerRpc(unit.netID, itemIndex); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void DropItemCommandServerRpc(UInt16 netID, int itemIndex, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                unit.DropItem(itemIndex);
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (DropItemCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (DropItemCommandSend NetworkCommandSync)");
            }
        }

        // DROP AT POSITION

        // Local player will send to server command
        public void DropItemPositionCommandSend(Unit unit, int itemIndex, Vector3 position) { DropItemPositionCommandServerRpc(unit.netID, itemIndex, position); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void DropItemPositionCommandServerRpc(UInt16 netID, int itemIndex, Vector3 position, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                unit.DropItem(itemIndex, position);
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (DropItemPositionCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (DropItemPositionCommandSend NetworkCommandSync)");
            }
        }

        // DROP AT UNIT

        // Local player will send to server command
        public void DropItemUnitCommandSend(Unit unit, int itemIndex, Unit atUnit) { DropItemUnitCommandServerRpc(unit.netID, itemIndex, atUnit.netID); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void DropItemUnitCommandServerRpc(UInt16 netID, int itemIndex, UInt16 targetUnitID, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(targetUnitID, out Unit targetUnit))
                {
                    unit.DropItem(itemIndex, targetUnit);
                }
                else
                {
                    Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + targetUnitID + "! (DropItemUnitCommandSend NetworkCommandSync)");
                }
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (DropItemUnitCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (DropItemUnitCommandSend NetworkCommandSync)");
            }
        }

        // SWAP ITEM --------------

        // Local player will send to server command
        public void SwapItemCommandSend(Unit unit, int itemIndex, int itemIndex2) { SwapItemCommandServerRpc(unit.netID, itemIndex, itemIndex2); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void SwapItemCommandServerRpc(UInt16 netID, int itemIndex, int itemIndex2, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                unit.SwapItem(itemIndex, itemIndex2);
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (SwapItemCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (SwapItemCommandSend NetworkCommandSync)");
            }
        }

        // CONSTRUCTION CANCEL --------------

        // Local player will send to server command
        public void ConstructionCancelSend(Unit unit) { ConstructionCancelServerRpc(unit.netID); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void ConstructionCancelServerRpc(UInt16 netID, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                if (unit.constructionUnit)
                {
                    unit.constructionUnit.CancelConstruction(false);
                }
                else Debug.LogError("Desync! Client " + owner + " send a command to Unit without component netID:" + netID + "! (ConstructionCancelSend NetworkCommandSync)");
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (ConstructionCancelSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (ConstructionCancelSend NetworkCommandSync)");
            }
        }

        // TRANSPORT DISEMBARK --------------

        // Local player will send to server command to disembark by clicking the icon in the UI
        public void DisembarkCommandSend(Unit unit, int index) { DisembarkCommandServerRpc(unit.netID, index); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void DisembarkCommandServerRpc(UInt16 netID, int index, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                if (unit.transportUnit)
                {
                    unit.transportUnit.Disembark(index, unit.transform.position);
                }
                else Debug.LogError("Desync! Client " + owner + " send a command to Unit without component netID:" + netID + "! (DisembarkCommandSend NetworkCommandSync)");
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (DisembarkCommandSend NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (DisembarkCommandSend NetworkCommandSync)");
            }
        }

        // SET WAYPOINT --------------

        // Local player will send to server command to set the waypoint on unit
        public void SetWaypointCommandSend(Unit unit, Unit waypointUnit) { SetWaypointCommandServerRpc(unit.netID, waypointUnit.netID, Vector2.zero); }

        // Local player will send to server command to set the waypoint on location
        public void SetWaypointCommandSend(Unit unit, Vector2 position) { SetWaypointCommandServerRpc(unit.netID, 0, position); }

        // Server will receive command from the player and play the command on local Unit
        [Rpc(SendTo.Server)]
        public void SetWaypointCommandServerRpc(UInt16 netID, UInt16 waypointUnitNetID, Vector2 waypointLocation, RpcParams rpcParams = default)
        {
            int owner = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) && (owner == unit.owner || SlotManager.Instance.debugMode))
            {
                if (waypointUnitNetID != 0)
                {
                    // Unit
                    if (SlotManager.Instance.unitNetID.TryGetValue(waypointUnitNetID, out Unit waypointUnit))
                    {
                        unit.SetWaypoint(waypointUnit);
                    }
                    else Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + waypointUnit + "! (SetWaypoint NetworkCommandSync)");
                }
                else
                {
                    // Location
                    unit.SetWaypoint(waypointLocation);
                }
            }
            else
            {
                if (unit == null) Debug.LogWarning("Desync? Client " + owner + " send a command to a nonexistent Unit netID:" + netID + "! (SetWaypoint NetworkCommandSync)");
                else if (owner != unit.owner) Debug.LogWarning("Client " + owner + " sends a command to Unit netID:" + netID + " that does not belong to the client! (SetWaypoint NetworkCommandSync)");
            }
        }
    }
}
