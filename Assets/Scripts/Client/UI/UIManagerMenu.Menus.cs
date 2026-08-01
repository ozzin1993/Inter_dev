using Camera_TopDownNS;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // UIManagerMenu.Menus.cs — плавающее меню/экраны меню/показ-скрытие. Вырезано 1:1 из UIManagerMenu.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class UIManagerMenu
    {

        // FLOATING MENU -----------------------------------------------------------------------------------------------------------------------------------------------------------------------

        public void ShowFloatingMenu()
        {
            Vector2 mousePositionCorrected = CursorToUIposition();

            floatingMenu.style.left = mousePositionCorrected.x;
            floatingMenu.style.top = mousePositionCorrected.y;

            floatingMenu.style.display = DisplayStyle.Flex;

            UIDocument.rootVisualElement.RegisterCallback<PointerUpEvent>(FloatingMenuHide);
        }

        public void FloatingMenuHandler(ClickEvent evt)
        {
            VisualElement target = evt.target as VisualElement;

            // Team selection
            if (target.name.StartsWith("t"))
            {
                string[] player_team = target.name.Replace("t", "").Split("_");
                if (int.TryParse(player_team[0], out int playerSlot))
                {
                    if (int.TryParse(player_team[1], out int team))
                    {
                        if (NetworkConnectionHandler.isClient)
                        {
                            // Network
                            NetworkDataSync.instance.ChangeTeamToServerRpc(team);
                        }
                        else
                        {
                            // Single player or Server
                            SlotManager.instance.ChangeTeamTo((ulong)SlotManager.instance.playerID[playerSlot], team);
                        }
                    }
                }
            }
            else if (target.name.StartsWith("f"))
            {
                string[] player_team = target.name.Replace("f", "").Split("_");
                if (int.TryParse(player_team[0], out int playerSlot))
                {
                    if (int.TryParse(player_team[1], out int index))
                    {
                        if (NetworkConnectionHandler.isClient)
                        {
                            // Network
                            NetworkDataSync.instance.ChangeFactionToServerRpc(index);
                        }
                        else
                        {
                            // Single player or Server
                            SlotManager.instance.ChangeFactionTo((ulong)SlotManager.instance.playerID[playerSlot], index);
                        }
                    }
                }
            }
            else if (target.name.StartsWith("s"))
            {
                string[] player_team = target.name.Replace("s", "").Split("_");
                if (int.TryParse(player_team[0], out int playerSlot))
                {
                    if (int.TryParse(player_team[1], out int index))
                    {
                        if (NetworkConnectionHandler.isClient)
                        {
                            // Network
                            NetworkDataSync.instance.ChangeSpawnToServerRpc(index);
                        }
                        else
                        {
                            // Single player or Server
                            SlotManager.instance.ChangeSpawnPositionTo((ulong)SlotManager.instance.playerID[playerSlot], index);
                        }
                    }
                }
            }

            UIDocument.rootVisualElement.UnregisterCallback<PointerUpEvent>(FloatingMenuHide);
            floatingMenu.style.display = DisplayStyle.None;
        }

        public void FloatingMenuEntry(string name, string text)
        {
            Label entry = new Label();
            entry.name = name;
            entry.text = text;
            entry.AddToClassList("inventoryMenu");
            entry.AddToClassList("FloatingMenuEntry");
            floatingMenu.Add(entry);
        }

        public void FloatingMenuHide(PointerUpEvent evt)
        {
            VisualElement target = evt.target as VisualElement;

            if (target.ClassListContains("FloatingMenuEntry")) return;
            else
            {
                UIDocument.rootVisualElement.UnregisterCallback<PointerUpEvent>(FloatingMenuHide);
                floatingMenu.style.display = DisplayStyle.None;
            }
        }

        // LOAD GAME -----------------------------------------------------------------------------------------------------------------------------------------------------------------------

        void FillSaveList()
        {
            saveList.Clear();

            string[] files = SaveManager.GetAvailableSaveFiles();
            if (files != null)
            {
                for (int i = 0; i < files.Length; i++)
                {
                    SaveListEntryAdd(i, files[i], SaveManager.GetFileName(files[i]), saveList);
                }
            }
        }

        // Load button in the menu
        void LoadButton(ClickEvent evt)
        {
            FillSaveList();
            ShowMenuLobby(3);
        }

        // Go back to menu button
        void CancelButtonLoad(ClickEvent evt)
        {
            ShowMenuLobby(0);
        }

        // Save list - load / remove
        void SaveListHandler(ClickEvent evt)
        {
            VisualElement target = evt.target as VisualElement;

            // Load
            if (target.name.StartsWith("Load"))
            {
                Debug.Log("Loading " + target.parent.name);
                // NO TEAM CHANGE ?? WHEN LOADING A SCENE
                // Get the scene name
                string saveName = SaveManager.GetFileName(target.parent.name);
                int index = saveName.IndexOf("_");
                string sceneName = (index >= 0) ? saveName.Substring(0, index) : saveName;

                // Set active scene
                if (SceneHandler.instance.SetActiveScene(sceneName))
                {
                    // Start host
                    TextField tf = (TextField)UIDocument.rootVisualElement.Q("Menu").Q("PlayerName");
                    NetworkConnectionHandler.instance.StartHost(tf.value);

                    // Change the player data
                    SaveManager.LoadSlotManagerData(target.parent.name, true);

                    // Save file name
                    SceneHandler.instance.saveFileName = target.parent.name;

                    chatBox.Clear();
                }
            }
            // Delete
            else if (target.name.StartsWith("Remove"))
            {
                if (SaveManager.DeleteSaveFile(target.parent.name, true))
                {
                    FillSaveList();
                }
            }
        }

        // UTILS ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Hide UIDocument
        public void HideUIDocument()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            UIDocument.rootVisualElement.style.display = DisplayStyle.None;
        }

        public void ShowUIDocument()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            // InGame Menu
            if (SlotManager.instance.gameStarted == GameState.Started)
            {
                // Toggle
                if (UIDocument.rootVisualElement.style.display == DisplayStyle.Flex)
                {
                    HideUIDocument();
                }
                else
                {
                    ShowMenuLobby(4);
                    UIDocument.rootVisualElement.style.display = DisplayStyle.Flex;
                }
            }
            else
            {
                UIDocument.rootVisualElement.style.display = DisplayStyle.Flex;
            }
        }

        // Show Menu/Lobby
        public void ShowMenuLobby(int i)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            currentMenuIndex = i;
            if (i == 0) // Menu
            {
                UIDocument.rootVisualElement.Q("Menu").style.display = DisplayStyle.Flex;
                UIDocument.rootVisualElement.Q("Menu").Q("MenuButtons").style.display = DisplayStyle.Flex;
                UIDocument.rootVisualElement.Q("Menu").Q("Connecting").style.display = DisplayStyle.None;
                saveButton.style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Lobby").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("LoadMenu").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("WinMenu").style.display = DisplayStyle.None;
            }
            else if (i == 1) // Lobby
            {
                UIDocument.rootVisualElement.Q("Menu").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Lobby").style.display = DisplayStyle.Flex;
                if (!NetworkConnectionHandler.isClient) UIDocument.rootVisualElement.Q("Lobby").Q("StartGame").style.display = DisplayStyle.Flex;
                else UIDocument.rootVisualElement.Q("Lobby").Q("StartGame").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("LoadMenu").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("WinMenu").style.display = DisplayStyle.None;
            }
            else if (i == 2) // Connecting menu
            {
                UIDocument.rootVisualElement.Q("Menu").style.display = DisplayStyle.Flex;
                UIDocument.rootVisualElement.Q("Menu").Q("MenuButtons").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Menu").Q("Connecting").style.display = DisplayStyle.Flex;
                saveButton.style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Lobby").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("LoadMenu").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("WinMenu").style.display = DisplayStyle.None;
            }
            else if (i == 3) // Load menu 
            {
                UIDocument.rootVisualElement.Q("Menu").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Lobby").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("LoadMenu").style.display = DisplayStyle.Flex;
                UIDocument.rootVisualElement.Q("WinMenu").style.display = DisplayStyle.None;
            }
            else if (i == 4) // InGame Menu
            {
                UIDocument.rootVisualElement.Q("Menu").style.display = DisplayStyle.Flex;
                UIDocument.rootVisualElement.Q("Menu").Q("MenuButtons").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Menu").Q("Connecting").style.display = DisplayStyle.None;
                if (!NetworkConnectionHandler.isClient) saveButton.style.display = DisplayStyle.Flex;
                else saveButton.style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Lobby").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("LoadMenu").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("WinMenu").style.display = DisplayStyle.None;
            }
            else if (i == 5 || i == 6) // You won menu
            {
                UIDocument.rootVisualElement.Q("Menu").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Menu").Q("MenuButtons").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Menu").Q("Connecting").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("Lobby").style.display = DisplayStyle.None;
                UIDocument.rootVisualElement.Q("LoadMenu").style.display = DisplayStyle.None;

                VisualElement winMenu = UIDocument.rootVisualElement.Q("WinMenu");
                winMenu.style.display = DisplayStyle.Flex;

                Label lbl = (Label)winMenu.Q("Txt");
                if (i == 5)
                {
                    lbl.text = "YOU WON!";
                }
                else
                {
                    lbl.text = "YOU LOST!";
                }
                UIDocument.rootVisualElement.style.display = DisplayStyle.Flex;
            }
        }

        // Returns cursors position in UI space
        Vector2 CursorToUIposition()
        {
            Vector2 mousePosition = Mouse.current.position.ReadValue();
            Vector2 mousePositionCorrected = new Vector2(mousePosition.x + 10, Screen.height - mousePosition.y + 10);
            mousePositionCorrected = RuntimePanelUtils.ScreenToPanel(UIDocument.rootVisualElement.panel, mousePositionCorrected);
            return mousePositionCorrected;
        }
    }
}
