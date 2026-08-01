using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;
using System.Text;

namespace StrategyCore
{
    // NetworkDataSync.Targets.cs — цели/анимации/плавающий текст/вейпоинты. Вырезано 1:1 из NetworkDataSync.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class NetworkDataSync
    {

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
