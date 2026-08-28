using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;
using System.Text;

namespace StrategyCore
{
    // NetworkDataSync.Economy.cs — ресурсы/смерть/HP-MP-XP. Вырезано 1:1 из NetworkDataSync.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class NetworkDataSync
    {

        // RESOURCES --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
        public List<Vector3Int> resourceChanged = new List<Vector3Int>(); // [0] = player index, [1] = resource index (GameResources.Instance.gameResources), [2] = resource amount

        // Server adds the data through this function
        public void ResourceSendAdd(int player, int resourceType, int resourceAmount)
        {
            // Only for connected players and not server
            if (SlotManager.Instance.slotType[player] != SlotType.Player || SlotManager.Instance.playerID[player] == 0 || SlotManager.Instance.playerID[player] == -1) return;

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
                    if (SlotManager.Instance.slotType[resourceChanged[i].x] != SlotType.Player || SlotManager.Instance.playerID[resourceChanged[i].x] == 0 || SlotManager.Instance.playerID[resourceChanged[i].x] == -1) continue;

                    ResourceChangeClientRpc(new Vector2Int(resourceChanged[i].y, GameResources.Instance.playerResources[resourceChanged[i].y + resourceChanged[i].x * GameResources.Instance.gameResources.Length]), RpcTarget.Single((ulong)SlotManager.Instance.playerID[resourceChanged[i].x], RpcTargetUse.Temp));
                }

                resourceChanged.Clear();
            }
        }

        // Changes the resource amount on the clients
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ResourceChangeClientRpc(Vector2Int resourceAmount, RpcParams rpcParams)
        {
            if (GameResources.Instance.gameResources[resourceAmount.x].type.limited)
            {
                // Limited only max value is changed, current value is changed locally
                GameResources.Instance.playerResourceLimits[resourceAmount.x + SlotManager.Instance.currentPlayer * GameResources.Instance.gameResources.Length] = resourceAmount.y;
            }
            else
            {
                GameResources.Instance.playerResources[resourceAmount.x + SlotManager.Instance.currentPlayer * GameResources.Instance.gameResources.Length] = resourceAmount.y;
            }
            Presentation.UI?.UpdateResourceTab(resourceAmount.x);
        }

        // UNIT DIE --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Units should not die until server says so
        // Server send trigger
        public void DieTriggerSend(UInt16 unitID, int killingPlayer, Unit killingUnit, bool rewards, bool destroy)
        {
            // [Interflow fix 2026-08-23 dead-killer-ref] Убийца мог умереть раньше жертвы в этом же
            // кадре (взаимное убийство, снаряд умершего стрелка): его netID уже снят из реестра на
            // всех пирах, и клиент печатал «Desync! KILLER unit netID…». Ссылка необязательна — шлём 0
            // (штатная ветка «убийца-игрок без юнита», награды идут по killingPlayer). Тот же принцип,
            // что в SkillFiredSend (status-send-gate); StillRegistered — из партиала UnitStatus.
            UInt16 killingUnitID = StillRegistered(killingUnit) ? killingUnit.netID : (UInt16)0;
            DieClientRpc(unitID, killingPlayer, killingUnitID, rewards, destroy);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void DieClientRpc(UInt16 netID, int killingPlayer, UInt16 killingUnitID, bool rewards, bool destroy)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                if (killingUnitID == 0)
                {
                    // Killing player
                    unit.Die(killingPlayer, null, rewards, false, destroy);
                }
                else
                {
                    // Killing unit
                    if (SlotManager.Instance.unitNetID.TryGetValue(killingUnitID, out Unit killingUnit))
                    {
                        unit.Die(killingPlayer, killingUnit, rewards, false, destroy);
                    }
                    else
                    {
                        // [Interflow fix 2026-07-25 desync-die-msg] Не найден УБИЙЦА. Обе ветки печатали одинаковый текст — по логу нельзя было понять, кого не хватает. Печатаем оба netID.
                        Debug.LogError("Desync! KILLER unit netID:" + killingUnitID + " should exist on client, but does not! (victim netID:" + netID + ") (DieTriggerSend NetworkDataSync)");
                    }
                }
            }
            else
            {
                // [Interflow fix 2026-07-25 desync-die-msg] Не найдена ЖЕРТВА. Обе ветки печатали одинаковый текст — по логу нельзя было понять, кого не хватает. Печатаем оба netID.
                Debug.LogError("Desync! VICTIM unit netID:" + netID + " should exist on client, but does not! (killer netID:" + killingUnitID + ") (DieTriggerSend NetworkDataSync)");
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
                    if (SlotManager.Instance.unitNetID.TryGetValue(hpChangedUnits[i], out Unit unit))
                    {
                        hpAmount[i] = unit.health;
                    }
                }

                HPChangeClientRpc(hpChangedUnits.ToArray(), hpAmount);
                hpChangedUnits.Clear();
                onHPCleared?.Invoke();
            }
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void HPChangeClientRpc(UInt16[] unitID, float[] unitHealth)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            for (int i = 0; i < unitID.Length; i++)
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(unitID[i], out Unit unit))
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
                    if (SlotManager.Instance.unitNetID.TryGetValue(mpChangedUnits[i], out Unit unit))
                    {
                        mpAmount[i] = unit.mana;
                    }
                }

                MPChangeClientRpc(mpChangedUnits.ToArray(), mpAmount);
                mpChangedUnits.Clear();
            }
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void MPChangeClientRpc(UInt16[] unitID, float[] unitMana)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            for (int i = 0; i < unitID.Length; i++)
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(unitID[i], out Unit unit))
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
                    if (SlotManager.Instance.unitNetID.TryGetValue(xpChangedUnits[i], out Unit unit))
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

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void XPChangeClientRpc(UInt16[] unitID, int[] unitXp, int[] lvl, int[] abilityPoints)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            for (int i = 0; i < unitID.Length; i++)
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(unitID[i], out Unit unit))
                {
                    unit.levelingUnit.SetLevel(lvl[i], unitXp[i], abilityPoints[i], true, false, false);
                }
                else
                {
                    Debug.LogError("Desync! Unit netID:" + unitID[i] + " should exist on client, but does not! (XPChangeSend NetworkDataSync)");
                }
            }
        }
    }
}
