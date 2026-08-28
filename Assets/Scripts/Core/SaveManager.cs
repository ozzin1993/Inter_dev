using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using SimpleJSON;

namespace StrategyCore
{
    // Save and Load game state

    public partial class SaveManager : MonoBehaviour // [Interflow fix 2026-08-01 partial-split] класс разрезан на partial-файлы (задача №11)
    {
        public static SaveManager Instance { get; private set; }

        public static string sceneUnitData; // Unit data that we must load after every client finishes loading the scene

        public static int noDataNumber = -1725; // Arbitrary data that indicates there is not data
        public static string delimiter = "\u2561";
        public static string delSecondary = "~";
        public static string delThird = "^";
        public static string savePath = Application.persistentDataPath + "/saves/";
        public static string saveExtension = ".save";

        // PLAYER DATA (SLOT MANAGER) ----------------------------------------------------------------------------------------------------------------------------------------------------

        public static string SavePlayerData()
        {
            string content = JsonHelper.ToJson<SlotType>(SlotManager.Instance.slotType);
            content += delSecondary;
            content += JsonHelper.ToJson<int>(SlotManager.Instance.playerID);
            content += delSecondary;
            content += JsonHelper.ToJson<int>(SlotManager.Instance.playerTeam);
            content += delSecondary;
            content += JsonHelper.ToJson<string>(SlotManager.Instance.playerName);
            content += delSecondary;
            content += JsonHelper.ToJson<int>(SlotManager.Instance.playerPosition);
            content += delSecondary;
            content += JsonHelper.ToJson<int>(SlotManager.Instance.playerFaction);
            content += delSecondary;
            content += JsonHelper.ToJson<bool>(SlotManager.Instance.playerLost);

            return content;
        }

        public static void LoadPlayerData(string playerData)
        {
            string[] playerDataSplit = playerData.Split(delSecondary, StringSplitOptions.RemoveEmptyEntries);
            if (playerDataSplit.Length < 7)
            {
                Debug.LogError("[SaveManager] Блок игроков сейва повреждён: частей " + playerDataSplit.Length + " из 7 — применение пропущено");
                return;
            }

            SlotType[] slotTypes = JsonHelper.FromJson<SlotType>(playerDataSplit[0]);
            int[] playerIDs = JsonHelper.FromJson<int>(playerDataSplit[1]);
            int[] playerTeams = JsonHelper.FromJson<int>(playerDataSplit[2]);
            string[] playerNames = JsonHelper.FromJson<string>(playerDataSplit[3]);
            int[] playerPositions = JsonHelper.FromJson<int>(playerDataSplit[4]);
            int[] playerFactions = JsonHelper.FromJson<int>(playerDataSplit[5]);
            bool[] playerLosts = JsonHelper.FromJson<bool>(playerDataSplit[6]);

            // We only copy teams, factions, positions and player losts + bots
            SlotManager.Instance.playerTeam = playerTeams;
            SlotManager.Instance.playerPosition = playerPositions;
            SlotManager.Instance.playerFaction = playerFactions;
            SlotManager.Instance.playerLost = playerLosts;

            // For factions and positions we must add 1 index, since 0 is occupied by Random option
            for (int i = 0; i < playerPositions.Length; i++)
            {
                playerPositions[i]++;
                playerFactions[i]++;
            }

            // Copy bots
            for (int i = 0; i < slotTypes.Length; i++)
            {
                if (slotTypes[i] != SlotType.Player && slotTypes[i] != SlotType.Empty)
                {
                    SlotManager.Instance.slotType[i] = slotTypes[i];
                    SlotManager.Instance.playerID[i] = playerIDs[i];
                    SlotManager.Instance.playerName[i] = playerNames[i];
                }
            }

            // Assign arrays - not used
            // SlotManager.Instance.slotType = slotTypes;
            // SlotManager.Instance.playerID = playerIDs;
            // SlotManager.Instance.playerName = playerNames;
        }

        // TECHNOLOGY ----------------------------------------------------------------------------------------------------------------------------------------------------

        // player units must be added with units
        // tech being processed - must be added with processes of units

        public static string SaveTechnology()
        {
            // List of technology that is unlocked for all possible players
            string unlockedTechList = "";

            for (int p = 0; p < Enum.GetNames(typeof(Players)).Length; p++)
            {
                List<int> unlockedTech = new List<int>();
                List<int> beingProcessedTech = new List<int>();

                // Tech unlocked
                foreach (var kvp in TechnologyManager.Instance.TechTree[p])
                {
                    if (kvp.Value == true)
                    {
                        unlockedTech.Add(kvp.Key.id);
                    }
                }

                // Tech being Processed
                foreach (var kvp in TechnologyManager.Instance.TechTreeProcessing[p])
                {
                    if (kvp.Value == true)
                    {
                        beingProcessedTech.Add(kvp.Key.id);
                    }
                }

                unlockedTechList += JsonHelper.ToJson(unlockedTech.ToArray());
                unlockedTechList += delThird + JsonHelper.ToJson(beingProcessedTech.ToArray());
                unlockedTechList += delSecondary;
            }

            return unlockedTechList;
        }

        public static void LoadTechnology(string unlockedTechList)
        {
            string[] playerTechListSTR = unlockedTechList.Split(delSecondary, StringSplitOptions.RemoveEmptyEntries);

            // Update ability levels
            for (int i = 0; i < playerTechListSTR.Length; i++)
            {
                string[] playerTechs = playerTechListSTR[i].Split(delThird, StringSplitOptions.RemoveEmptyEntries);

                int[] playerTechList = JsonHelper.FromJson<int>(playerTechs[0]);
                int[] playerTechProcessingList = JsonHelper.FromJson<int>(playerTechs[1]);

                if (playerTechList.Length == 0 && playerTechProcessingList.Length == 0)
                {
                    // No data for this player
                    continue;
                }

                // Unlock tech
                var techTree = TechnologyManager.Instance.TechTree[i];
                var keys = techTree.Keys.ToList(); // Create a list of keys

                for (int j = 0; j < keys.Count; j++)
                {
                    var key = keys[j];
                    bool unlocked = Array.Exists(playerTechList, x => x == key.id);
                    techTree[key] = unlocked; // Update the dictionary value
                }

                // Tech being processed
                techTree = TechnologyManager.Instance.TechTreeProcessing[i];
                keys = techTree.Keys.ToList(); // Create a list of keys

                for (int j = 0; j < keys.Count; j++)
                {
                    var key = keys[j];
                    bool unlocked = Array.Exists(playerTechProcessingList, x => x == key.id);
                    techTree[key] = unlocked; // Update the dictionary value
                }

                TechnologyManager.Instance.OnTechUnlock[i]?.Invoke();
            }
        }

        // RESOURCES ----------------------------------------------------------------------------------------------------------------------------------------------------

        public static string SaveResources()
        {
            string content = JsonHelper.ToJson(GameResources.Instance.playerResources);
            content += delSecondary;
            content += JsonHelper.ToJson(GameResources.Instance.playerResourceLimits);

            return content;
        }

        public static void LoadResources(string resources)
        {
            int delimiterIndex = resources.IndexOf(delSecondary);

            if (delimiterIndex != -1)
            {
                GameResources.Instance.playerResources = JsonHelper.FromJson<int>(resources.Substring(0, delimiterIndex));
                GameResources.Instance.playerResourceLimits = JsonHelper.FromJson<int>(resources.Substring(delimiterIndex + delSecondary.Length));
            }
            else
            {
                Debug.LogWarning("Failed to load resources!");
            }

            Presentation.UI?.RefreshResourceTab();
        }

        // SHADOWCASTERS ----------------------------------------------------------------------------------------------------------------------------------------------------

        public static string SaveShadowcasters()
        {
            ShadowCasterSyncData[] shadowCasterData = new ShadowCasterSyncData[GameManager.Instance.shadowCasters.Count];

            for (int i = 0; i < GameManager.Instance.shadowCasters.Count; i++)
            {
                shadowCasterData[i] = new ShadowCasterSyncData(
                                                            (GameManager.Instance.shadowCasters[i].thisUnit == null) ? (UInt16)0 : GameManager.Instance.shadowCasters[i].thisUnit.netID, // Owner 0 == null,
                                                            GameManager.Instance.shadowCasters[i].castingPlayer,
                                                            GameManager.Instance.shadowCasters[i].id,
                                                            GameManager.Instance.shadowCasters[i].activeAbility.id,
                                                            GameManager.Instance.shadowCasters[i].activeAbilityLevel,
                                                            (GameManager.Instance.shadowCasters[i].activeAbilityUnit == null) ? (UInt16)0 : GameManager.Instance.shadowCasters[i].activeAbilityUnit.netID, // Target 0 == null
                                                            GameManager.Instance.shadowCasters[i].activeAbilityLocation,
                                                            GameManager.Instance.shadowCasters[i].activeAbilityRange,
                                                            GameManager.Instance.shadowCasters[i].activeAbilityDuration - GameManager.Instance.shadowCasters[i].currentTime
                                                            );
            }

            return JsonHelper.ToJson(shadowCasterData);
        }

        public static void LoadShadowcasters(string shadowCasters)
        {
            ShadowCasterSyncData[] shadowCasterData = JsonHelper.FromJson<ShadowCasterSyncData>(shadowCasters);

            if (shadowCasterData != null)
            {
                for (int i = 0; i < shadowCasterData.Length; i++)
                {
                    ShadowCaster.Spawn(shadowCasterData[i]);
                }
            }
        }

        // UNIT DATA ----------------------------------------------------------------------------------------------------------------------------------------------------

    }
}
