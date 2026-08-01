using Camera_TopDownNS;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // UIManager.Inventory.cs — инвентарь (клики/перетаскивание/меню). Вырезано 1:1 из UIManager.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class UIManager
    {

        // INVENTORY -----------------------------------------------------------------------------------------------------------------------------------------------------------------

        void InventoryHandler(PointerUpEvent evt)
        {
            if (pc.activeUnit != null && pc.activeUnit.owner != SlotManager.instance.currentPlayer && !SlotManager.instance.debugMode) return;

            VisualElement clickedElement = evt.target as VisualElement;

            // Names of the UI button are the index of the items in the inventory
            if (int.TryParse(clickedElement.name, out int itemIndex))
            {
                if (isDraggingItem)
                {
                    if (itemIndex == -1 || itemIndex == currentItemIndex)
                    {
                        pc.ChangeMode(PCMode.Default);
                        //ItemDragCancel();
                        return;
                    }

                    // Replace with selected item, item that is being dragged
                    pc.activeUnit.SwapItem(currentItemIndex, itemIndex);

                    pc.ChangeMode(PCMode.Default);
                    //ItemDragCancel();
                }
                else
                {
                    if (itemIndex == -1) return;

                    if (evt.button == 0)
                    {
                        // Left click - Item use
                        if (pc.activeUnit.items[itemIndex] != null) UseAbility(pc.activeUnit.items[itemIndex], itemIndex, true);
                    }
                    else if (evt.button == 1)
                    {
                        // Right click - Inventory Menu
                        if (pc.activeUnit.items[itemIndex] != null)
                        {
                            currentItemIndex = itemIndex;

                            // ItemDragStart(itemIndex);
                            Vector2 mousePositionCorrected = CursorToUIposition();

                            inventoryMenu.style.left = mousePositionCorrected.x;
                            inventoryMenu.style.top = mousePositionCorrected.y;

                            inventoryMenu.style.display = DisplayStyle.Flex;
                            PlayerControl.coreInput.Main.Select.performed += InventoryMenuHide;
                            PlayerControl.coreInput.Main.Command.performed += InventoryMenuHide;
                            PlayerControl.coreInput.Main.Cancel.performed += InventoryMenuHide;
                        }
                    }
                }
            }
        }

        void InventoryDescriptor(MouseEnterEvent evt)
        {
            VisualElement hoveredElement = evt.target as VisualElement;

            if (hoveredElement.name != "null")
            {
                // Names of the buttons are the index of the items of the unit
                if (int.TryParse(hoveredElement.name, out int index))
                {
                    descriptorName.text = pc.activeUnit.items[index].abilityName[0];
                    descriptorDescription.text = pc.activeUnit.items[index].description[0];
                    descriptorLevel.style.display = DisplayStyle.None;
                    descriptorHotkey.style.display = DisplayStyle.None;
                    descriptorLockbox.style.display = DisplayStyle.None;

                    // Cost
                    descriptorUsageCost.Clear();
                    descriptorResourceCost.Clear();
                    descriptorCostBox.style.display = DisplayStyle.None;
                    if (pc.activeUnit.items[index].manaCost.Length > 0 && pc.activeUnit.items[index].manaCost[0] != 0)
                    {
                        // Mana
                        CostElementCreate(pc.activeUnit.items[index].manaCost[0], true, false);
                        descriptorCostBox.style.display = DisplayStyle.Flex;
                    }
                    if (pc.activeUnit.items[index].manaCostPerSecond.Length > 0 && pc.activeUnit.items[index].manaCostPerSecond[0] != 0)
                    {
                        // Mana per second
                        CostElementCreate(pc.activeUnit.items[index].manaCostPerSecond[0], true, false, null, true);
                        descriptorCostBox.style.display = DisplayStyle.Flex;
                    }

                    if (pc.activeUnit.items[index].cooldown.Length > 0 && pc.activeUnit.items[index].cooldown[0] != 0)
                    {
                        // Cooldown
                        CostElementCreate(pc.activeUnit.items[index].cooldown[0], false, true);
                        descriptorCostBox.style.display = DisplayStyle.Flex;
                    }

                    // Resource cost
                    if (pc.activeUnit.items[index].cost.Length > 0)
                    {
                        for (int i = 0; i < pc.activeUnit.items[index].cost[0].data.Length; i++)
                        {
                            CostElementCreate(pc.activeUnit.items[index].cost[0].data[i].value * GameManager.instance.sellPriceReduction, false, false, pc.activeUnit.items[index].cost[0].data[i].type.icon);
                            descriptorCostBox.style.display = DisplayStyle.Flex;
                        }
                    }

                    // Bottom Descriptor
                    descriptorBottomText.style.display = DisplayStyle.Flex;
                    descriptorBottomText.text = "";
                    if (pc.activeUnit.items[index].castTime.Length > 0)
                    {
                        descriptorBottomText.text += "CAST TIME: " + pc.activeUnit.items[index].castTime[0] + "s";
                    }
                    if (pc.activeUnit.items[index].castRange.Length > 0)
                    {
                        descriptorBottomText.text += "\nCAST RANGE: " + pc.activeUnit.items[index].castRange[0];
                    }
                    if (pc.activeUnit.items[index].duration.Length > 0)
                    {
                        descriptorBottomText.text += "\nDURATION: " + pc.activeUnit.items[index].duration[0];
                    }
                    descriptorBottomText.text += "\nTYPE: " + pc.activeUnit.items[index].type;

                    if (pc.activeUnit.items[index].continuous) descriptorBottomText.text += " / CONTINUOUS";

                    descriptor.style.display = DisplayStyle.Flex;
                }
            }
        }

        void InventoryDragStart(InputAction.CallbackContext context)
        {
            if (pc.activeUnit != null && pc.activeUnit.owner != SlotManager.instance.currentPlayer && !SlotManager.instance.debugMode) return;

            // Check if inventory item was clicked
            Vector2 mousePositionCorrected = CursorToUIposition();
            VisualElement picked = uiDocument.rootVisualElement.panel.Pick(mousePositionCorrected);

            // Check if the cursor is over inventory
            if (picked != null && (picked.parent != null && picked.parent.name == "Items"))
            {
                // Names of the UI button are the index of the items in the inventory
                if (int.TryParse(picked.name, out int itemIndex))
                {
                    if (itemIndex == -1) return;
                    if (pc.activeUnit.items[itemIndex] == null) return;

                    ItemDragStart(itemIndex);

                    inventoryDragIcon.style.display = DisplayStyle.Flex;
                    inventoryDragIcon.style.backgroundImage = picked.style.backgroundImage;
                }
            }
        }

        void InventoryDragUpdate()
        {
            Vector2 mousePositionCorrected = CursorToUIposition();

            inventoryDragIcon.style.left = mousePositionCorrected.x;
            inventoryDragIcon.style.top = mousePositionCorrected.y;
        }

        // When we release the mouse button during the drag this function is called. If we did not release on inventory items, we should cancel the drag.
        // Handling release on inventory items is done in InventoryHandler(PointerUpEvent evt)
        void InventoryDragFinish()
        {
            // If cursor is not over UI we try dropping the item
            if (!pc.IsOverUI(Camera_TopDown.instance.GetCursorPosition()))
            {
                Unit unit = Utils.GetUnitAtCursor();
                if (unit == null)
                {
                    // Drop item at position
                    Vector3 point = Utils.TerrainScreenRaycast(Camera_TopDown.instance.GetCursorPosition());
                    if (point != Vector3.zero)
                    {
                        pc.activeUnit.DropItem(currentItemIndex, point);
                    }
                    else
                    {
                        ShowNotifyMsg("Can`t drop item at this location");
                    }
                }
                else
                {
                    // Drop item on unit
                    pc.activeUnit.DropItem(currentItemIndex, unit);
                }

                // if (!pc.activeUnit.DropItem(currentItemIndex))
                // {
                //     ShowNotifyMsg("Can`t drop item at the unit location");
                // }
            }
            else
            {
                // Check if inventoryMenu was clicked
                Vector2 mousePositionCorrected = CursorToUIposition();
                VisualElement picked = uiDocument.rootVisualElement.panel.Pick(mousePositionCorrected);

                if (picked != null && (picked.parent != null && picked.parent.name == "Items")) return;
            }

            pc.ChangeMode(PCMode.Default);
            //ItemDragCancel();
        }

        // If inventory menu does not fit into the screen we change its Y position
        void InventoryMenuReposition(GeometryChangedEvent evt)
        {
            if (uiDocument.rootVisualElement.worldBound.size.y < inventoryMenu.resolvedStyle.top + inventoryMenu.resolvedStyle.height)
            {
                inventoryMenu.style.top = inventoryMenu.resolvedStyle.top - inventoryMenu.resolvedStyle.height;
            }
        }

        // We hide inventoryMenu if any mouse buttons were clicked, we do this after 1 frame so inventory menu click has time to process its logic
        void InventoryMenuHide(InputAction.CallbackContext context)
        {
            // Check if inventoryMenu was clicked
            Vector2 mousePositionCorrected = CursorToUIposition();
            VisualElement picked = uiDocument.rootVisualElement.panel.Pick(mousePositionCorrected);

            if (picked != null && (picked.name == "sellItemButton" || picked.name == "dropItemButton")) return;

            inventoryMenu.style.display = DisplayStyle.None;
            PlayerControl.coreInput.Main.Select.performed -= InventoryMenuHide;
            PlayerControl.coreInput.Main.Command.performed -= InventoryMenuHide;
            PlayerControl.coreInput.Main.Cancel.performed -= InventoryMenuHide;
        }

        void inventoryMenuClick(PointerUpEvent evt, int actionType)
        {
            // 0 is drop
            if (actionType == 0)
            {
                pc.activeUnit.DropItem(currentItemIndex);
            }
            // 1 is sell
            else if (actionType == 1)
            {
                pc.activeUnit.SellItem(currentItemIndex);
            }

            currentItemIndex = 0;
            inventoryMenu.style.display = DisplayStyle.None;
            PlayerControl.coreInput.Main.Select.performed -= InventoryMenuHide;
            PlayerControl.coreInput.Main.Command.performed -= InventoryMenuHide;
            PlayerControl.coreInput.Main.Cancel.performed -= InventoryMenuHide;
        }

        // Hotkey ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

    }
}
