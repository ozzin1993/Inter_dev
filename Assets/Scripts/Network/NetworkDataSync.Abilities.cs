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
    }
}
