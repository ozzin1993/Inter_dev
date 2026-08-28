using Camera_TopDownNS;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // UIManagerMenu.Lobby.cs — список игроков лобби и сейвов. Вырезано 1:1 из UIManagerMenu.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class UIManagerMenu
    {
        public void FillPlayerList()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            playerList.Clear();

            if (SceneHandler.Instance.sceneData == null || SceneHandler.Instance.sceneIndex >= SceneHandler.Instance.sceneData.Length) return;
            SceneData sceneData = SceneHandler.Instance.sceneData[SceneHandler.Instance.sceneIndex];
            int currentPlayerIndex = 0;

            for (int t = 0; t < sceneData.teamsAndPlayers.Length; t++)
            {
                // Add team name
                Label teamname = new Label();
                teamname.name = "TeamName" + t;
                teamname.text = sceneData.teamsAndPlayers[t].teamName;
                teamname.AddToClassList("TeamName");
                if (t != 0) teamname.style.marginTop = 15;
                playerList.Add(teamname);

                for (int i = 0; i < sceneData.teamsAndPlayers[t].players.Length; i++)
                {
                    if (!sceneData.teamsAndPlayers[t].players[i].dontShow)
                    {
                        if (SlotManager.Instance.slotType[currentPlayerIndex] == SlotType.Player || SlotManager.Instance.slotType[currentPlayerIndex] == SlotType.Bot)
                        {
                            // Bot or Player
                            // bool currentPlayer = (!NetworkConnectionHandler.isClient || SlotManager.Instance.currentPlayer == currentPlayerIndex);
                            // int team = (sceneData.chooseTeams && currentPlayer) ? SlotManager.Instance.playerTeam[currentPlayerIndex] : -1;
                            string spawnPos = null;
                            if (sceneData.spawnPointCount != 0)
                            {
                                if (SlotManager.Instance.playerPosition[currentPlayerIndex] == 0) spawnPos = "Random";
                                else spawnPos = (SlotManager.Instance.playerPosition[currentPlayerIndex] - 1).ToString();
                            }
                            string faction = null;
                            if (sceneData.factions != null && sceneData.factions.Length > 0)
                            {
                                if (SlotManager.Instance.playerFaction[currentPlayerIndex] == 0) faction = "Random";
                                else faction = sceneData.factions[SlotManager.Instance.playerFaction[currentPlayerIndex] - 1];
                            }
                            ListEntryAdd(i, currentPlayerIndex.ToString(), SlotManager.Instance.playerName[currentPlayerIndex], SlotManager.Instance.playerTeam[currentPlayerIndex], faction, spawnPos, playerList, false);
                        }
                        else
                        {
                            // Empty
                            ListEntryAdd(i, currentPlayerIndex.ToString(), null, -1, null, null, playerList, true);
                        }
                    }
                    currentPlayerIndex++;
                }
            }
        }

        public void ListEntryAdd(int styleIndex, string name, string text, int team, string faction, string spawnPos, VisualElement parent, bool empty = false)
        {
            if (empty)
            {
                // Add empty slots
                Label emptySlot = new Label();
                emptySlot.text = "EMPTY SLOT";
                emptySlot.AddToClassList("PlayerSlot");
                emptySlot.AddToClassList("emptySlot");
                if (styleIndex != 0) emptySlot.style.marginTop = 8;
                parent.Add(emptySlot);
            }
            else
            {
                // Entry
                GroupBox entry = new GroupBox();
                entry.AddToClassList("ListEntry");
                entry.name = name.ToString();
                if (styleIndex != 0) entry.style.marginTop = 8;
                parent.Add(entry);

                // NameBox
                GroupBox nameBox = new GroupBox();
                nameBox.AddToClassList("ItemButton");
                nameBox.AddToClassList("SlotClass");
                nameBox.name = "Name";
                entry.Add(nameBox);

                Label label = new Label();
                label.text = text;
                label.name = "Name";
                label.pickingMode = PickingMode.Ignore;
                nameBox.Add(label);

                // Factions
                if (faction != null)
                {
                    Label factionLbl = new Label();
                    factionLbl.AddToClassList("ItemButton");
                    factionLbl.AddToClassList("SlotClass");
                    factionLbl.AddToClassList("SlotExtraButton");
                    factionLbl.name = "faction" + styleIndex;
                    factionLbl.text = "Faction: " + faction;
                    entry.Add(factionLbl);
                }

                // Spawn positions
                if (spawnPos != null)
                {
                    Label spawnPosLbl = new Label();
                    spawnPosLbl.AddToClassList("ItemButton");
                    spawnPosLbl.AddToClassList("SlotClass");
                    spawnPosLbl.AddToClassList("SlotExtraButton");
                    spawnPosLbl.name = "spawn" + styleIndex;
                    spawnPosLbl.text = "Spawn Position: " + spawnPos;
                    entry.Add(spawnPosLbl);
                }

                // Team selection
                Label teamLabel = new Label();
                teamLabel.AddToClassList("ItemButton");
                teamLabel.AddToClassList("SlotClass");
                teamLabel.AddToClassList("SlotExtraButton");
                teamLabel.name = "team" + styleIndex;
                teamLabel.text = "Team: " + team;
                entry.Add(teamLabel);

                // Remove button / Disconnect
                if (!NetworkConnectionHandler.isClient)
                {
                    GroupBox removeBox = new GroupBox();
                    removeBox.name = "Remove";
                    removeBox.AddToClassList("ItemButton");
                    removeBox.AddToClassList("SlotClass");
                    removeBox.AddToClassList("RemoveButton");
                    entry.Add(removeBox);
                }
            }
        }

        public void SaveListEntryAdd(int styleIndex, string name, string text, VisualElement parent)
        {
            // Entry
            GroupBox entry = new GroupBox();
            entry.AddToClassList("ListEntry");
            entry.name = name.ToString();
            if (styleIndex != 0) entry.style.marginTop = 8;
            parent.Add(entry);

            // NameBox
            GroupBox nameBox = new GroupBox();
            nameBox.AddToClassList("ItemButton");
            nameBox.AddToClassList("SlotClass");
            nameBox.name = "Name";
            entry.Add(nameBox);

            Label label = new Label();
            label.text = text;
            label.name = "Name";
            label.pickingMode = PickingMode.Ignore;
            nameBox.Add(label);

            // Load
            Label teamLabel = new Label();
            teamLabel.AddToClassList("ItemButton");
            teamLabel.AddToClassList("SlotClass");
            teamLabel.AddToClassList("SlotExtraButton");
            teamLabel.name = "Load";
            teamLabel.text = "Load";
            entry.Add(teamLabel);

            // Remove
            GroupBox removeBox = new GroupBox();
            removeBox.name = "Remove";
            removeBox.AddToClassList("ItemButton");
            removeBox.AddToClassList("SlotClass");
            removeBox.AddToClassList("RemoveButton");
            entry.Add(removeBox);
        }
    }
}
