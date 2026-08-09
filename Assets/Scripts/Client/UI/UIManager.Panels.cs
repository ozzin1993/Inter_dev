using Camera_TopDownNS;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // UIManager.Panels.cs — кнопки команд + транспорт/процессы/статусы. Вырезано 1:1 из UIManager.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class UIManager
    {

        // Buttons ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        void StopButton()
        {
            for (int i = 0; i < pc.selectedUnits.Count; i++)
            {
                if (pc.selectedUnits[i].owner == SlotManager.instance.currentPlayer || SlotManager.instance.debugMode)
                {
                    if (!pc.selectedUnits[i].isBeingBuilt)
                    {
                        pc.selectedUnits[i].Idle(true);
                    }
                }
            }
        }

        void HoldButton()
        {
            for (int i = 0; i < pc.selectedUnits.Count; i++)
            {
                if (pc.selectedUnits[i].owner == SlotManager.instance.currentPlayer || SlotManager.instance.debugMode)
                {
                    if (!pc.selectedUnits[i].isBeingBuilt)
                    {
                        pc.selectedUnits[i].Hold(true);
                    }
                }
            }
        }

        void AttackMoveButton()
        {
            pc.ChangeMode(PCMode.AttackMove);
            ShowCancelButton();
        }

        // When you are casting a spell it will cancel that action and go back to the default state
        void CancelButton(ClickEvent evt)
        {
            // PlayerControl mode reset
            pc.ChangeMode(PCMode.Default);

            RedrawAbilityView();

            // If a building being built, cancel the construction
            if (pc.activeUnit.isBeingBuilt)
            {
                pc.activeUnit.constructionUnit.CancelConstruction(false);
            }
        }

        public void ShowCancelButton()
        {
            cancelButton.style.display = DisplayStyle.Flex;
        }

        public void HideCancelButton()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            cancelButton.style.display = DisplayStyle.None;
        }

        // Return button, will reset ability view
        void ReturnButton(ClickEvent evt)
        {
            // Remove last container opened container
            if (openContainersIndex.Count != 0)
            {
                openContainersIndex.RemoveAt(openContainersIndex.Count - 1);
                RedrawAbilityView();
            }

            if (openContainersIndex.Count == 0) HideReturnButton();
        }

        public void ShowReturnButton()
        {
            returnButton.style.display = DisplayStyle.Flex;
        }

        public void HideReturnButton()
        {
            returnButton.style.display = DisplayStyle.None;
        }

        // Level button, will allow to choose skill to learn or level up
        void LevelButton(ClickEvent evt)
        {
            if (!pc.activeUnit.levelingUnit || pc.activeUnit.levelingUnit.abilityPoints == 0) return;

            if (isLeveling) LevellingToggle(false);
            else LevellingToggle(true);

            RedrawAbilityView();
        }

        public void LevellingToggle(bool on)
        {
            if (on)
            {
                levelButton.RemoveFromClassList("commandButtonActive");
                isLeveling = true;
            }
            else
            {
                levelButton.AddToClassList("commandButtonActive");
                isLeveling = false;
            }
        }

        public void ShowLevelButton()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            // Check if unit belongs to the current player
            if (!SlotManager.instance.debugMode && pc.activeUnit.owner != SlotManager.instance.currentPlayer) return;

            Label levelLabel = (Label)levelButton.ElementAt(1);
            levelLabel.text = pc.activeUnit.levelingUnit.abilityPoints.ToString();
            levelButton.style.display = DisplayStyle.Flex;
        }

        public void HideLevelButton(bool levelingOff = false)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            if (levelingOff) LevellingToggle(false);
            levelButton.style.display = DisplayStyle.None;
        }

        public void ShopButton(ClickEvent evt)
        {
            if (!pc.activeUnit.isShop) return;
            pc.ChangeMode(PCMode.ShopUnit); // Shop unit change
            ShowCancelButton();
        }

        // Transport ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        public void TransportDisplay()
        {
            if (pc.activeUnit.transportUnit && pc.activeUnit.transportUnit.units.Count > 0)
            {
                processesUI.style.display = DisplayStyle.None;
                transportUI.style.display = DisplayStyle.Flex;
                transportUI.Clear();

                for (int i = 0; i < pc.activeUnit.transportUnit.units.Count; i++)
                {
                    for (int c = 0; c < pc.activeUnit.transportUnit.units[i].transportWeight; c++)
                    {
                        // Main display
                        if (c == 0) ProcessElementCreate(transportUI, false, i, pc.activeUnit.transportUnit.units[i].icon, -1);
                        // Display of transport weight
                        else ProcessElementCreate(transportUI, true, i, pc.activeUnit.transportUnit.units[i].icon);
                    }
                }

                // Add empty slots
                for (int c = 0; c < pc.activeUnit.transportUnit.capacity - pc.activeUnit.transportUnit.currentCapacity; c++)
                {
                    ProcessElementCreate(transportUI, true);
                }
            }
            else
            {
                transportUI.style.display = DisplayStyle.None;

                if (pc.activeUnit.canProcess && pc.activeUnit.activeProcess[0] != null && processesUI.style.display == DisplayStyle.None) ProcessDisplay();
            }
        }

        // When process is clicked in processUI, it is canceled
        private void TransportClick(ClickEvent evt)
        {
            VisualElement clickedElement = evt.target as VisualElement;

            // Name is the index of the process
            if (int.TryParse(clickedElement.name, out int index))
            {
                if (pc.activeUnit.owner == SlotManager.instance.currentPlayer || SlotManager.instance.debugMode)
                {
                    pc.activeUnit.transportUnit.Disembark(index, pc.activeUnit.transform.position);
                }
                else
                {
                    ShowNotifyMsg("Very funny! This unit is not yours to control!");
                }
            }
        }

        public void HideTransport()
        {
            transportUI.style.display = DisplayStyle.None;
        }

        // Processes ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // If unit has any processes it will show Processes UI
        public void ProcessDisplay()
        {
            if (pc.activeUnit.transportUnit && pc.activeUnit.transportUnit.units.Count > 0) return;

            transportUI.style.display = DisplayStyle.None;
            processesUI.style.display = DisplayStyle.Flex;
            processesUI.Clear();

            for (int i = 0; i < GameManager.maxProcessCount; i++)
            {
                if (pc.activeUnit.activeProcess[i] != null)
                {
                    // [Interflow fix 2026-08-01 client-data] IconAt клампует уровень и терпит пустой массив (см. UIManager.Abilities).
                    ProcessElementCreate(processesUI, false, i, IconAt(pc.activeUnit.activeProcess[i].icon, pc.activeUnit.processLevel[i]), pc.activeUnit.activeProcess[i].castTime[pc.activeUnit.processLevel[i]]);
                }
                else
                {
                    ProcessElementCreate(processesUI, true);
                }
            }
        }

        // When process is clicked in processUI, it is canceled
        private void ProcessClick(ClickEvent evt)
        {
            VisualElement clickedElement = evt.target as VisualElement;

            // Name is the index of the process
            if (int.TryParse(clickedElement.name, out int index))
            {
                if (pc.activeUnit.owner == SlotManager.instance.currentPlayer)
                {
                    if (pc.activeUnit.CancelProcess(index))
                    {
                        RedrawAbilityView();
                    }
                }
                else
                {
                    ShowNotifyMsg("Very funny! This unit is not yours to control!");
                }
            }
        }

        private void ProcessTimerUpdate()
        {
            if (pc.activeUnit.activeProcess[0] != null && processesUI.style.display == DisplayStyle.Flex)
            {
                // Process exists
                processTimer = (Label)processesUI.ElementAt(0).ElementAt(1);
                processTimer.text = pc.activeUnit.currentProcessTimer.ToString("F2");
            }
        }

        public void ShowProcesses()
        {
            processesUI.style.display = DisplayStyle.Flex;
        }

        public void HideProcesses()
        {
            processesUI.style.display = DisplayStyle.None;
        }

        void ProcessElementCreate(VisualElement parent, bool empty, int index = 0, Texture2D icon2D = null, float timer = 0)
        {
            if (empty)
            {
                GroupBox button = new GroupBox();
                button.AddToClassList("ItemButton");
                button.AddToClassList("emptySlot");
                if (icon2D != null)
                {
                    button.name = index.ToString();
                    button.style.backgroundImage = icon2D;
                }
                else button.name = "empty";
                parent.Add(button);
            }
            else
            {
                GroupBox button = new GroupBox();
                button.AddToClassList("ItemButton");
                button.name = index.ToString();
                parent.Add(button);

                GroupBox icon = new GroupBox();
                icon.AddToClassList("AbilityButtonIcon");
                icon.style.backgroundImage = icon2D;
                icon.pickingMode = PickingMode.Ignore;
                button.Add(icon);

                if (timer != -1)
                {
                    Label timerLabel = new Label();
                    timerLabel.AddToClassList("process-timer");
                    timerLabel.text = timer.ToString("F2");
                    timerLabel.pickingMode = PickingMode.Ignore;
                    button.Add(timerLabel);
                }
            }
        }

        // Inventory ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        public void ItemDragStart(int index)
        {
            currentItemIndex = index;
            isDraggingItem = true;
            pc.ChangeMode(PCMode.DragDrop);
            ShowCancelButton();
        }

        public void ItemDragCancel()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            inventoryDragIcon.style.display = DisplayStyle.None;
            isDraggingItem = false;
        }

        // EFFECTORS ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        private bool viewingEffectorDescriptor = false; // To make sure we hide the descriptor if player was hovering over the effector when it was removed

        public void DisplayStatusTab()
        {
            statusWindow.Clear();
            statusWindow.style.display = DisplayStyle.Flex;

            if (viewingEffectorDescriptor)
            {
                HideDescriptor();
                viewingEffectorDescriptor = false;
            }

            // [Interflow fix 2026-08-05 unit-status-sync] Состав значков считает ЕДИНЫЙ перечислитель
            // UnitStatusIcons (он же кормит шкалу над полоской здоровья) — панель и шкала не расходятся.
            // Заменяет два цикла (локальные эффекторы + SkillVisualStatus от 2026-08-01): теперь тут же
            // видны статусы ядра (стан/немота/безоружие) и флаги (слепота) из каталога иконок.
            List<UnitStatusIcons.Entry> statusIcons = new List<UnitStatusIcons.Entry>();
            UnitStatusIcons.Collect(pc.activeUnit, statusIcons);
            for (int i = 0; i < statusIcons.Count; i++)
                StatusEffectorCreate(statusIcons[i].key, statusIcons[i].icon);
        }

        public void HideStatusTab()
        {
            statusWindow.style.display = DisplayStyle.None;
        }

        void StatusEffectorCreate(int effectorID, Texture2D icon)
        {
            GroupBox button = new GroupBox();
            button.name = effectorID.ToString();

            button.AddToClassList("statusElement");
            button.RegisterCallback<MouseEnterEvent>(EffectorDescriptor);
            button.RegisterCallback<MouseLeaveEvent>(HideDescriptor);

            button.style.backgroundImage = icon;

            statusWindow.Add(button);
        }

        void EffectorDescriptor(MouseEnterEvent evt)
        {
            VisualElement hoveredElement = evt.target as VisualElement;

            // Names of the groupBox are the index of the resourceTypes
            if (int.TryParse(hoveredElement.name, out int index))
            {
                Effector hoveredEffector = Effector.GetEffectorByID(index);
                if (hoveredEffector)
                {
                    FillDescriptor(hoveredEffector.displayName, hoveredEffector.description);
                    viewingEffectorDescriptor = true;
                }
            }
        }
    }
}
