using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;
using System.Text;

namespace StrategyCore
{
    // NetworkDataSync.Abilities.cs — способности/статусы (каст/стоп/левелап/стан...). Вырезано 1:1 из NetworkDataSync.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class NetworkDataSync
    {

        // ABILITY USE --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // ABILITY CAST -----

        // Server send cast signal
        public void AbilityCastStartSend(Unit castingUnit, Ability activeAbility, Unit abilityTarget, Vector3 abilityLocation)
        {
            if (abilityTarget) AbilityCastStartClientRpc(castingUnit.netID, activeAbility.id, abilityTarget.netID);
            else AbilityCastStartClientRpc(castingUnit.netID, activeAbility.id, abilityLocation);

            // Презентация: сообщение уходит только клиентам (SendTo.NotServer), поэтому ХОСТУ поднимаем
            // факт локально — иначе область действия была бы видна на клиенте и не видна на хосте.
            SkillPresentationEvents.RaiseCastStarted(castingUnit, activeAbility.id, abilityTarget,
                                                    abilityTarget != null ? abilityTarget.transform.position : abilityLocation);
        }

        // Client receives command to cast an ability
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void AbilityCastStartClientRpc(UInt16 castingUnitID, int abilityID, UInt16 targetID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(castingUnitID, out Unit castingUnit))
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(targetID, out Unit targetUnit))
                {
                    castingUnit.activeAbilityCastTime = 1;
                    castingUnit.activeAbilityUnit = targetUnit;
                    castingUnit.activeAbilityUnit.OnReferenceChange += castingUnit.AbilityUnitReferenceChange;
                    castingUnit.activeAbility = GameManager.Instance.gameAbilities[abilityID];

                    // Презентация: показать область действия с начала замаха. Сам приёмник ничего не рисует.
                    SkillPresentationEvents.RaiseCastStarted(castingUnit, abilityID, targetUnit, targetUnit.transform.position);
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
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void AbilityCastStartClientRpc(UInt16 castingUnitID, int abilityID, Vector3 abilityLocation)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(castingUnitID, out Unit castingUnit))
            {
                castingUnit.activeAbilityCastTime = 1;
                castingUnit.activeAbilityLocation = abilityLocation;
                castingUnit.activeAbility = GameManager.Instance.gameAbilities[abilityID];

                // Презентация: показать область действия с начала замаха.
                SkillPresentationEvents.RaiseCastStarted(castingUnit, abilityID, null, abilityLocation);
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

            // Презентация: то же, что и у начала каста — хосту факт поднимаем локально.
            SkillPresentationEvents.RaiseCastStopped(castingUnit);
        }

        // Client receives command to cast an ability
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void AbilityStopCastClientRpc(UInt16 castingUnitID)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(castingUnitID, out Unit castingUnit))
            {
                castingUnit.activeAbilityCastTime = 0;
                castingUnit.activeAbilityLocation = Vector3.zero;
                if (castingUnit.activeAbilityUnit != null) castingUnit.activeAbilityUnit.OnReferenceChange -= castingUnit.AbilityUnitReferenceChange;
                castingUnit.activeAbilityUnit = null;
                castingUnit.activeAbility = null;

                // Презентация: погасить область действия немедленно.
                SkillPresentationEvents.RaiseCastStopped(castingUnit);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + castingUnitID + " should exist on client, but does not! (AbilityStopCastSend NetworkDataSync)");
            }
        }

        // ABILITY USE -------

        // Server send cast signal
        public void AbilityUseSend(Unit castingUnit, Ability ability, int abilityLevel, int abilityIndex, bool isItem, Unit abilityTarget, Vector3 abilityLocation, bool interrupt)
        {
            // 0 == null. [Interflow fix 2026-08-23 status-send-gate] Цель, умершая в этом же
            // вызове (локальная ссылка не обнуляется подпиской OnReferenceChange), шлётся как 0 —
            // приёмник штатно кастует без цели; раньше клиент печатал «Desync!» и НЕ выполнял каст.
            UInt16 targetID = StillRegistered(abilityTarget) ? abilityTarget.netID : (UInt16)0;
            AbilityUseClientRpc(castingUnit.netID, ability.id, abilityLevel, abilityIndex, isItem, targetID, abilityLocation, interrupt);
        }

        // Client receives command to immediately cast an ability
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void AbilityUseClientRpc(UInt16 castingUnitID, int abilityID, int abilityLevel, int abilityIndex, bool isItem, UInt16 targetID, Vector3 location, bool interrupt, RpcParams rpcParams = default)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(castingUnitID, out Unit castingUnit))
            {
                Unit targetUnit = null;
                if (targetID != 0)
                {
                    if (!SlotManager.Instance.unitNetID.TryGetValue(targetID, out targetUnit))
                    {
                        Debug.LogError("Desync! Unit netID:" + targetID + " should exist on client, but does not! (AbilityUseSend NetworkDataSync)");
                        return;
                    }
                }

                castingUnit.activeAbilityCastTime = 0;
                castingUnit.UseAbilityImmediately(GameManager.Instance.gameAbilities[abilityID], abilityLevel, abilityIndex, isItem, targetUnit, location, interrupt);
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
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void AbilityStopClientRpc(UInt16 castingUnitID, RpcParams rpcParams = default)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(castingUnitID, out Unit castingUnit))
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
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void LevelUpAbilityClientRpc(UInt16 netID, int abilityID, int abilityIndex)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                unit.LevelUpAbility(GameManager.Instance.gameAbilities[abilityID], abilityIndex);

                if (Presentation.Selection?.ActiveUnit == unit)
                {
                    if ((Presentation.UI?.IsLeveling ?? false))
                    {
                        // Successful ability level increase
                        Presentation.UI?.ShowLevelButton();
                        Presentation.UI?.RedrawAbilityView();
                        if (unit.levelingUnit.abilityPoints == 0)
                        {
                            Presentation.UI?.HideLevelButton(true);
                        }
                    }
                }
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (LevelUpAbilitySend NetworkDataSync)");
            }
        }

        // Сервер: поставить умению юнита КОНКРЕТНЫЙ уровень (не инкремент) — восстановление улучшений героя
        // после смерти (блок Б9). Клиент строит уровни из префаба и о восстановлении иначе не узнал бы.
        public void AbilityLevelSetSend(Unit unit, int abilityIndex, int level)
        {
            if (!ServerCanSend() || unit == null) return;
            AbilityLevelSetClientRpc(unit.netID, abilityIndex, level);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void AbilityLevelSetClientRpc(UInt16 netID, int abilityIndex, int level)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance != null && NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (!SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit) || unit == null)
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (AbilityLevelSetSend NetworkDataSync)");
                return;
            }

            if (unit.abilityLevel == null || abilityIndex < 0 || abilityIndex >= unit.abilityLevel.Length) return;

            unit.abilityLevel[abilityIndex] = level;   // зеркало для тултипов, значков и фильтра прокачки; логика — на сервере

            if (Presentation.Selection?.ActiveUnit == unit) Presentation.UI?.RedrawAbilityView();
        }

        // INVISIBILITY SYNC ------------------------------------------
        // Sync invisibility state of the unit

        // Server send info about invisibility state
        public void InvisibilitySetSend(Unit unit, bool state)
        {
            InvisibilitySetClientRpc(unit.netID, state);
        }

        // Client receive info about invisibility state
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void InvisibilitySetClientRpc(UInt16 netID, bool state)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit))
            {
                unit.SetInvisibility(state);
            }
            else
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (InvisibilitySetSend NetworkDataSync)");
            }
        }

        // [Interflow fix 2026-09-03 control-as-effectors] StunSetSend / MuteSetSend / DisarmSetSend
        // вместе с приёмными ClientRpc СНЕСЕНЫ. Контроль стал состоянием-эффектором, а у служебных
        // состояний есть значок — значит их уже везёт клиенту ЕДИНЫЙ канал статусов
        // (NetworkDataSync.UnitStatus). Клиент разворачивает ассет по id и выводит флаги контроля
        // из его признаков (Units/Unit.Control.cs). Второй канал про то же самое не нужен.
    }
}
