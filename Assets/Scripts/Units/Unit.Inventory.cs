using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    // This script is responsible for handling of the unit`s items

    public partial class Unit
    {
        [Header("Inventory")]
        [Tooltip("How many items can this unit carry, non-dynamic. Maximum is defined by GameManager.cs:maxInventorySize.")]
        public int InventorySize;
        [Tooltip("Can b e left empty. Currently held items by the unit. If items have more entries than inventory size, they will be removed.")]
        public Ability[] items;
        [Tooltip("Can be left empty. For holding charges for items. > 0 means it has charges.")]
        public int[] itemCharges;

        public Action OnInventoryChange; // Called when item is moved, removed or added
        private int dropItemIndex;
        [HideInInspector] public bool inventoryInitialized = false; // If inventory has been initialized

        // ============================= INITIALIZATION ==============================================================================

        /// <summary>
        /// Initializes all necessary data for unit`s items.
        /// </summary>
        public void InitializeInventory()
        {
            // Inventory - Initiate items in the inventory
            if (InventorySize > 0 && inventoryInitialized == false)
            {
                // Increase the items array size if needed
                if (InventorySize > GameManager.maxInventorySize) InventorySize = GameManager.maxInventorySize;
                if (items.Length != InventorySize) Array.Resize(ref items, InventorySize);
                itemCharges = new int[InventorySize];

                // Initialize items
                for (int i = 0; i < InventorySize; i++)
                {
                    if (items[i] == null) continue;
                    Ability item = items[i];
                    items[i] = null;
                    AddItem(item, item.charges, 0, true);
                }

                OnFollowReach += PickUpItem;
                inventoryInitialized = true;
            }
        }

        // ============================= COMMANDS ==============================================================================

        /// <summary>
        /// Command to buy an item from a shop. Any item that is placed in Ability[] of the unit is considered to be purchasable.
        /// </summary>
        /// <param name="abilityIndex">Global ability index of the item. You can get it by Utils.GetAbilityIndex.</param>
        /// <param name="shoppingUnit">Unit that is making a purchase.</param>
        public void BuyItem(int abilityIndex, Unit shoppingUnit = null)
        {
            if (shoppingUnit == null) shoppingUnit = shopUnit;

            // If shop unit is null or too far, try to find one
            if (shoppingUnit == null || Vector2.Distance(new Vector2(shoppingUnit.transform.position.x, shoppingUnit.transform.position.z), new Vector2(transform.position.x, transform.position.z)) > GameManager.Instance.shopRadius)
            {
                shoppingUnit = Utils.GetClosestShoppingUnit(SlotManager.Instance.currentPlayer, this);

                if (shoppingUnit == null)
                {
                    Presentation.NotifyMsg("No unit nearby to buy this item", SlotManager.Instance.currentPlayer, false);
                    return;
                }
            }

            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.Instance.BuyItemCommandSend(this, shoppingUnit, abilityIndex);
                return;
            }

            if (CheckAbilityItemRequirements(shoppingUnit.owner, abilityIndex, false)) return;

            // We have a unit that is capable of buying the item
            Ability item = Utils.GetAbilityByIndex(this, abilityIndex);

            // Look for an empty slot
            int emptySlot = -1;
            for (int i = 0; i < shoppingUnit.InventorySize; i++)
            {
                if (shoppingUnit.items[i] == null)
                {
                    emptySlot = i;
                    break;
                }
            }

            if (emptySlot == -1)
            {
                // No available slots
                Presentation.NotifyMsg("No slots are available", shoppingUnit.owner, true);
                return;
            }
            else
            {
                shoppingUnit.AddItem(item, item.charges, 0);
            }

            SubtractAbilityItemCost(shoppingUnit.owner, abilityIndex, false);
        }

        /// <summary>
        /// Command to sell an item.
        /// </summary>
        /// <param name="itemIndex">Index of the item in the inventory.</param>
        /// <returns></returns>
        public bool SellItem(int itemIndex)
        {
            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.Instance.SellItemCommandSend(this, itemIndex);
                return false;
            }

            Unit shop = Utils.GetNearShop(new Vector2(transform.position.x, transform.position.z));
            if (shop)
            {
                if (items[itemIndex].cost.Length > 0)
                {
                    // Multi level items are not supported
                    for (int i = 0; i < items[itemIndex].cost[0].data.Length; i++)
                    {
                        // Change the price and add the amount to the player
                        GameResources.Instance.ChangeAmount(owner,
                            new ResourceWrapper(items[itemIndex].cost[0].data[i].type,
                                               (int)(items[itemIndex].cost[0].data[i].value * GameManager.Instance.sellPriceReduction)));
                    }
                }

                // Remove the item
                RemoveItem(itemIndex);
                return true;
            }

            Presentation.NotifyMsg("No shops nearby. Shopping radius is " + GameManager.Instance.shopRadius + " meters.", owner, true);
            return false;
        }

        /// <summary>
        /// Command to drop an item around the unit.
        /// </summary>
        /// <param name="itemIndex">Index of the item in the inventory.</param>
        /// <returns></returns>
        public bool DropItem(int itemIndex)
        {
            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.Instance.DropItemCommandSend(this, itemIndex);
                return false;
            }

            if (items[itemIndex] != null)
            {
                if (ItemDropped.Spawn(items[itemIndex], itemCharges[itemIndex], GetAbilityCooldown(itemIndex, true), transform.position, unitRadius))
                {
                    RemoveItem(itemIndex);
                    return true;
                }
                else Presentation.NotifyMsg("Can`t drop item here", owner, true);
            }
            return false;
        }

        /// <summary>
        /// Command to drop an item at specified position.
        /// </summary>
        /// <param name="itemIndex">Index of the item in the inventory.</param>
        /// <param name="position">Position to drop.</param>
        public void DropItem(int itemIndex, Vector3 position)
        {
            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.Instance.DropItemPositionCommandSend(this, itemIndex, position);
                return;
            }

            Move(position, ReferenceManager.Instance.itemPrefab.unitRadius);
            dropItemIndex = itemIndex;

            OnCommand += CancelDropItemMove;
            OnPositionReach += DropItemPointReached;
        }

        /// <summary>
        /// Command to drop an item at unit.
        /// </summary>
        /// <param name="itemIndex">Index of the item in the inventory.</param>
        /// <param name="unit">Unit to percieve the item.</param>
        public void DropItem(int itemIndex, Unit unit)
        {
            bool greenlight = false;

            if (unit.InventorySize > 0)
            {
                for (int i = 0; i < unit.items.Length; i++)
                {
                    if (unit.items[i] == null)
                    {
                        greenlight = true;
                        break;
                    }
                }
            }

            if (greenlight)
            {
                if (NetworkConnectionHandler.isClient)
                {
                    NetworkCommandSync.Instance.DropItemUnitCommandSend(this, itemIndex, unit);
                    return;
                }

                float distanceToTarget = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(unit.transform.position.x, unit.transform.position.z));
                if (distanceToTarget <= unitRadius + unit.unitRadius + Utils.stopDistanceOffset)
                {
                    // Already close enough to give the item
                    target = unit;
                    dropItemIndex = itemIndex;
                    DropItemUnitReached();
                    Idle();
                }
                else
                {
                    Follow(unit, unitRadius + unit.unitRadius + Utils.stopDistanceOffset);
                    dropItemIndex = itemIndex;

                    OnCommand += CancelDropItemMove;
                    OnFollowReach += DropItemUnitReached;
                }
            }
            else Presentation.NotifyMsg("This unit can`t accept the item!", owner, false);
        }

        // ============================= DROP AND PICK UP ==============================================================================

        /// <summary>
        /// Called when unit is commanded to drop at position and reaches the position. Triggered by OnPositionReach.
        /// </summary>
        public void DropItemPointReached()
        {
            if (items[dropItemIndex] != null)
            {
                Vector3 dropDestination;
                if (isAir) dropDestination = new Vector3(currentDestination.x - Utils.airOffsetX, currentDestination.y, currentDestination.z);
                else if (isInvisible) dropDestination = new Vector3(currentDestination.x, currentDestination.y, currentDestination.z - Utils.invisibilityOffsetY);
                else dropDestination = new Vector3(currentDestination.x, currentDestination.y, currentDestination.z);

                if (ItemDropped.Spawn(items[dropItemIndex], itemCharges[dropItemIndex], GetAbilityCooldown(dropItemIndex, true), dropDestination))
                {
                    RemoveItem(dropItemIndex);
                }
                else Presentation.NotifyMsg("Can`t drop item there!", owner, true);
            }
            CancelDropItemMove(false);
        }

        /// <summary>
        /// Called when unit is commanded to drop at a unit and reaches the unit. Triggered by OnFollowReach.
        /// </summary>
        public void DropItemUnitReached()
        {
            if (items[dropItemIndex] != null)
            {
                if (target.AddItem(items[dropItemIndex], itemCharges[dropItemIndex], GetAbilityCooldown(dropItemIndex, true)))
                {
                    RemoveItem(dropItemIndex);
                    Idle();
                }
                else Presentation.NotifyMsg("This unit can`t accept the item!", owner, true);
            }
            CancelDropItemMove(false);
        }

        /// <summary>
        /// Cancel the drop command.
        /// </summary>
        public void CancelDropItemMove(bool issuedByPlayer)
        {
            OnCommand -= CancelDropItemMove;
            OnPositionReach -= DropItemPointReached;
            OnFollowReach -= DropItemUnitReached;
            dropItemIndex = -1;
        }

        /// <summary>
        /// When the unit is commanded to follow the item and reaches it, this method is called to pick up the item.
        /// </summary>
        private void PickUpItem()
        {
            if (target.unitType == UnitType.Item)
            {
                ItemDropped pickedUpItem = target.GetComponent<ItemDropped>();
                if (AddItem(pickedUpItem.item, pickedUpItem.charges, pickedUpItem.cooldown))
                {
                    // Item was picked up, destroy item on the ground
                    target.Die(-1, null, false);
                }
            }
        }

        // ============================= UTILS ==============================================================================

        /// <summary>
        /// Adds an item to the inventory of the unit.
        /// </summary>
        /// <param name="item">Item to add.</param>
        /// <param name="charges">Does it have any charges.</param>
        /// <param name="cooldown">Does it have any cooldown.</param>
        /// <param name="skipSync">Should the server skip the syncing process with the clients.</param>
        /// <returns></returns>
        public bool AddItem(Ability item, int charges, float cooldown, bool skipSync = false)
        {
            // If use upon pickup, we use the item immediately
            if (InventorySize > 0 && item.useUponPickUp)
            {
                item.Use(this, this.owner, 0);
                if (charges == 1) return true;
                charges--;
            }

            for (int i = 0; i < InventorySize; i++)
            {
                if (items[i] == null)
                {
                    items[i] = item;
                    if (charges > 0) itemCharges[i] = charges;
                    if (cooldown > 0) ChangeAbilityCooldown(cooldown, i, true);
                    item.Unlock(this, this.owner, 0);
                    // OnInventoryChange?.Invoke(); // Due to how cooldown is calculated currently inventory change should also trigger abilityViewRedraw
                    OnRedrawAbilityView?.Invoke();

                    // Add on clients
                    if (!skipSync && NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.AddItem(this, i, item.id, charges, cooldown);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Remove an item from the inventory of the unit.
        /// </summary>
        /// <param name="itemIndex">Index of the item in the inventory.</param>
        /// <param name="noRedraw">Should ability view be redrawn.</param>
        /// <param name="skipSync">Should the server skip the syncing process with the clients.</param>
        public void RemoveItem(int itemIndex, bool noRedraw = false, bool skipSync = false)
        {
            items[itemIndex].Lock(this, this.owner, 0);
            // slot
            items[itemIndex] = null;
            // charges
            itemCharges[itemIndex] = 0;
            // cooldown
            for (int i = 0; i < cooldownAbilityIndex.Count; i++)
            {
                if (cooldownAbilityIndex[i] == itemIndex && cooldownAbilityIsItem[i] == true)
                {
                    cooldownAbility.RemoveAt(i);
                    cooldownAbilityIndex.RemoveAt(i);
                    cooldownAbilityIsItem.RemoveAt(i);
                    break;
                }
            }
            // Цикл аур предмета (everyFrameAbilities) снесён блоком Б7 (2026-09-05) — снимать нечего.
            // OnInventoryChange?.Invoke(); // Due to how cooldown is calculated currently inventory change should also trigger abilityViewRedraw
            if (!noRedraw) OnRedrawAbilityView?.Invoke();

            // Remove on clients
            if (!skipSync && NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.RemoveItem(this, itemIndex);
        }

        /// <summary>
        /// Swap the items in slots in the inventory.
        /// </summary>
        /// <param name="from">Item`s index in the inventory.</param>
        /// <param name="to">Second item`s index in the inventory.</param>
        /// <param name="commanded">Commanded means the swap command was issued by the player, when false we just swap items.</param>
        public void SwapItem(int from, int to, bool commanded = true)
        {
            if (commanded && NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.Instance.SwapItemCommandSend(this, from, to);
                return;
            }

            Ability draggedItem = items[from];
            int chargeCount = itemCharges[from];
            // slots
            items[from] = items[to];
            items[to] = draggedItem;
            // charges
            itemCharges[from] = itemCharges[to];
            itemCharges[to] = chargeCount;
            // cooldowns
            int swapCount = 0;
            for (int i = 0; i < cooldownAbilityIndex.Count; i++)
            {
                if (cooldownAbilityIndex[i] == from && cooldownAbilityIsItem[i] == true)
                {
                    swapCount++;
                    cooldownAbilityIndex[i] = to;
                }
                else if (cooldownAbilityIndex[i] == to && cooldownAbilityIsItem[i] == true)
                {
                    swapCount++;
                    cooldownAbilityIndex[i] = from;
                }
                if (swapCount == 2) break;
            }

            // OnInventoryChange?.Invoke(); // Due to how cooldown is calculated currently inventory change should also trigger abilityViewRedraw
            OnRedrawAbilityView?.Invoke();

            // Send info to clients
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.SwapItem(this, from, to);
        }

        /// <summary>
        /// Changes the charges of the specified item. By default will decrease by one.
        /// </summary>
        /// <param name="itemIndex">Index of the item in the inventory.</param>
        /// <param name="changeCount">Amount of charges to change.</param>
        public void ItemChargesChange(int itemIndex, int changeCount = -1)
        {
            // Item charges
            if (itemCharges[itemIndex] > 0)
            {
                itemCharges[itemIndex] = itemCharges[itemIndex] + changeCount;
                if (itemCharges[itemIndex] == 0)
                {
                    RemoveItem(itemIndex);
                }
                else OnRedrawAbilityView?.Invoke();
                // OnInventoryChange?.Invoke(); // Due to how cooldown is calculated currently inventory change should also trigger abilityViewRedraw
            }
        }


    }
}
