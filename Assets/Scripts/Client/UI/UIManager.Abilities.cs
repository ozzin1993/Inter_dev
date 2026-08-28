using Camera_TopDownNS;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // UIManager.Abilities.cs — панель способностей (Redraw/Display/Use/кулдауны). Вырезано 1:1 из UIManager.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class UIManager
    {

        // ABILITIES ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // To show current state of ability view call this
        public void RedrawAbilityView()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            RebuildAbilityView();
            HideDescriptor(); // Descriptor is called hidden because when abilityView is changed, descriptor might get stuck when the ability is removed

            if (openContainersIndex.Count == 0)
            {
                // Main ability view
                AbilityDisplay(pc.activeUnit.abilities, pc.activeUnit.abilities);
                openContainersIndex.Clear();
            }
            else
            {
                // Last open container ability view
                Container container = (Container)Utils.GetAbilityByIndexName(pc.activeUnit, openContainersIndex[openContainersIndex.Count - 1].ToString(), out int abilityIndex);
                AbilityDisplay(container.abilities, pc.activeUnit.abilities);
            }
        }

        // Ability view ScrollView element (parent of the ability elements) should be rebuilt completely because of the bug that does not change its size dynamically
        public void RebuildAbilityView()
        {
            var parent = abilityScrollView.parent;
            abilityScrollView.UnregisterCallback<ClickEvent>(AbilityHandler);
            parent.Remove(abilityScrollView);

            abilityScrollView = new ScrollView();
            abilityScrollView.AddToClassList("AbilityScrollView");
            abilityScrollView.name = "ScrollView";
            abilityScrollView.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            abilityScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            parent.Add(abilityScrollView);

            //abilityView = abilityScrollView; // abilityScrollView.Query("unity-content-container").First();
            abilityScrollView.RegisterCallback<ClickEvent>(AbilityHandler);

            // Fill with Empty elements
            for (int i = 0; i < 16; i++)
            {
                EmptyElementCreate(abilityScrollView, i);
            }
        }

        // Add ability elements to AbilityView
        // pathPrefix is used for the naming of the ability elements that should indicate how to reach said ability in case of containerAbility usage
        // abilities - abilities that are to be displayed now. mainAbilities - unit`s main abilities parameter. In case of containers abilities will be abilities of the container, and mainAbilities will be abilities of the unit that has that container.
        // [Interflow fix 2026-08-01 client-data] Иконка уровня с защитой от пустого массива: после миграции
        // данные возвращает регидратор, но если он не отработал (нет таблицы) — раньше icon.Length-1 давал -1
        // и icon[-1] РОНЯЛ всю панель юнита IndexOutOfRange. Теперь рисуем без иконки, панель живёт.
        static Texture2D IconAt(Texture2D[] icons, int level)
            => (icons != null && icons.Length > 0) ? icons[Mathf.Clamp(level, 0, icons.Length - 1)] : null;

        public void AbilityDisplay(Ability[] abilities, Ability[] mainAbilities, string pathPrefix = "")
        {
            abilityScrollView.Clear();

            // Abilities
            int nextEmptySlot = 0;
            int totalSlotSize = 16; // 4x4. 4 columns 4 rows.
            List<int> slotToAbilityIndex = new List<int>(); // Slot to unit.abilities[] index
            List<int> slotToGlobalAbilityIndex = new List<int>();// Currently viewed abilities by slots to ability index
            slotToAbilityIndex.Populate(-1, totalSlotSize); // -1 means this slot is empty.
            slotToGlobalAbilityIndex.Populate(-1, totalSlotSize); // -1 means this slot is empty.

            // Cooldown reset
            if (cooldownElements.Count > 0)
            {
                cooldownElements.Clear();
                cooldownTimers.Clear();
                cooldownIndex.Clear();
            }

            // Ability arrangement in UI
            for (int i = 0; i < abilities.Length; i++)
            {
                int abilityIndex = Utils.GetAbilityIndex(mainAbilities, abilities[i]);

                // If does not belong to player, we show only items and containers containing items
                if (pc.activeUnit.isBeingBuilt) continue;
                if (pc.activeUnit.owner != SlotManager.Instance.currentPlayer && !SlotManager.Instance.debugMode)
                {
                    // Only ally team
                    if (SlotManager.Instance.IsAlly(pc.activeUnit.owner, SlotManager.Instance.currentPlayer))
                    {
                        if (abilities[i].type == AbilityType.Container)
                        {
                            Container container = (Container)abilities[i];
                            if (!container.isShop) continue;
                        }
                        else if (abilities[i].isItem == false) continue;
                    }
                    else continue;
                }

                // If this ability is a research and already was learnt, skip it
                if (abilities[i] is Research)
                {
                    Research research = (Research)abilities[i];

                    if (research.unlockTech.Length > 0 && TechnologyManager.Instance.isUnlocked(research.unlockTech[research.unlockTech.Length - 1], pc.activeUnit.owner))
                    {
                        continue;
                    }
                }

                // When leveling ability, show only heroLevelable abilities that are not max level AND containers
                if (isLeveling == true)
                {
                    if (abilities[i].type != AbilityType.Container)
                    {
                        if (!abilities[i].heroLevelable || pc.activeUnit.abilityLevel[abilityIndex] + 1 >= abilities[i].maxLevels)
                        {
                            continue;
                        }
                    }
                }
                else
                {
                    // If ability level is -1, it means it is levelable ability that was not learnt, skip it
                    if (pc.activeUnit.abilityLevel[abilityIndex] == -1)
                    {
                        continue;
                    }
                }

                // If slot number is higher than current slotSize, increase slot size.
                if (abilities[i].slotNumber >= totalSlotSize)
                {
                    int newSlotSize = (int)(Mathf.Ceil((abilities[i].slotNumber + 1f) / 4f) * 4f); // Round up to be divisible by 4. 4 Abilities per row.
                    slotToAbilityIndex.Populate(-1, newSlotSize - totalSlotSize);
                    slotToGlobalAbilityIndex.Populate(-1, newSlotSize - totalSlotSize);
                    totalSlotSize = newSlotSize;
                }

                // If slot number is -1, it means we should display the ability sequentially. Otherwise slotToAbilityIndex should point to the index of the ability of the unit
                if (abilities[i].slotNumber == -1)
                {
                    // Find the next empty slot
                    while (nextEmptySlot < slotToAbilityIndex.Count && slotToAbilityIndex[nextEmptySlot] != -1)
                    {
                        nextEmptySlot++;
                    }

                    // Ensure we expand the list if we've run out of space
                    if (nextEmptySlot >= slotToAbilityIndex.Count)
                    {
                        slotToAbilityIndex.Populate(-1, 4); // Add 4 new slots
                        slotToGlobalAbilityIndex.Populate(-1, 4);
                        totalSlotSize += 4;
                    }

                    // Assign the ability to the next available slot
                    slotToAbilityIndex[nextEmptySlot] = i;
                    slotToGlobalAbilityIndex[nextEmptySlot] = abilityIndex;

                    // Increment nextEmptySlot for the next sequentially added ability
                    nextEmptySlot++;
                }
                else
                {
                    // If slot is empty add the ability index
                    if (slotToAbilityIndex[abilities[i].slotNumber] == -1)
                    {
                        slotToAbilityIndex[abilities[i].slotNumber] = i;
                        slotToGlobalAbilityIndex[abilities[i].slotNumber] = abilityIndex;
                    }
                    // If slot is not empty, put ability at the current slot to the empty slot, put current ability at current slot
                    else
                    {
                        // Save the ability currently in the slot
                        int abilityAtSlot = slotToAbilityIndex[abilities[i].slotNumber];
                        int abilityAtSlotGlobal = slotToGlobalAbilityIndex[abilities[i].slotNumber];

                        // Assign the new ability to the current slot
                        slotToAbilityIndex[abilities[i].slotNumber] = i;
                        slotToGlobalAbilityIndex[abilities[i].slotNumber] = abilityIndex;

                        // Find the next empty slot for the displaced ability
                        while (nextEmptySlot < slotToAbilityIndex.Count && slotToAbilityIndex[nextEmptySlot] != -1)
                        {
                            nextEmptySlot++;
                        }

                        // Ensure we expand the list if we've run out of space
                        if (nextEmptySlot >= slotToAbilityIndex.Count)
                        {
                            slotToAbilityIndex.Populate(-1, 4); // Add 4 new slots
                            slotToGlobalAbilityIndex.Populate(-1, 4);
                            totalSlotSize += 4;
                        }

                        // Move the displaced ability to the next available slot
                        slotToAbilityIndex[nextEmptySlot] = abilityAtSlot;
                        slotToGlobalAbilityIndex[nextEmptySlot] = abilityAtSlotGlobal;

                        // Increment nextEmptySlot for the next sequentially added ability
                        nextEmptySlot++;
                    }
                }
            }

            // Add UI elements according to slotToAbility arrangement
            for (int i = 0; i < slotToAbilityIndex.Count; i++)
            {
                if (slotToAbilityIndex[i] == -1)
                {
                    // Empty slot
                    // Display empty 
                    EmptyElementCreate(abilityScrollView, i);
                }
                else
                {
                    // unit.abilities[slotToAbilityIndex[i]] - the ability at the current slot
                    GroupBox element;
                    int iconLevel;
                    if (abilities[slotToAbilityIndex[i]].icon.Length > pc.activeUnit.abilityLevel[slotToGlobalAbilityIndex[i]]) iconLevel = pc.activeUnit.abilityLevel[slotToGlobalAbilityIndex[i]];
                    else iconLevel = abilities[slotToAbilityIndex[i]].icon.Length - 1;
                    if (iconLevel == -1) iconLevel = 0;

                    // Display ability
                    if (((isLeveling || !abilities[slotToAbilityIndex[i]].heroLevelable) && pc.activeUnit.abilityLocked[slotToGlobalAbilityIndex[i]])
                        || (abilities[slotToAbilityIndex[i]] is UpgradeBuilding && (pc.activeUnit.activeProcess[0] != null || (pc.activeUnit.transportUnit && pc.activeUnit.transportUnit.units.Count > 0))))
                    {
                        // Ability is locked
                        element = ElementCreate(abilityScrollView, slotToGlobalAbilityIndex[i], IconAt(abilities[slotToAbilityIndex[i]].icon, iconLevel), true, false, pathPrefix);
                    }
                    else
                    {
                        // If this ability is of Research type and is being processed currently, should be locked
                        if (abilities[slotToAbilityIndex[i]] is Research)
                        {
                            Research research = (Research)abilities[slotToAbilityIndex[i]];

                            if (research.unlockTech.Length > pc.activeUnit.abilityLevel[slotToGlobalAbilityIndex[i]] && TechnologyManager.Instance.IsTechBeingProcessed(research.unlockTech[pc.activeUnit.abilityLevel[slotToGlobalAbilityIndex[i]]], pc.activeUnit.owner))
                            {
                                // Locked
                                element = ElementCreate(abilityScrollView, slotToGlobalAbilityIndex[i], IconAt(abilities[slotToAbilityIndex[i]].icon, iconLevel), true, false, pathPrefix);
                            }
                            else
                            {
                                // Unlocked
                                element = ElementCreate(abilityScrollView, slotToGlobalAbilityIndex[i], IconAt(abilities[slotToAbilityIndex[i]].icon, iconLevel), false, false, pathPrefix);
                            }
                        }
                        else
                        {
                            // Ability is unlocked
                            element = ElementCreate(abilityScrollView, slotToGlobalAbilityIndex[i], IconAt(abilities[slotToAbilityIndex[i]].icon, iconLevel), false, false, pathPrefix);
                        }
                    }

                    // If toggle is active, add indicator
                    if (abilities[slotToAbilityIndex[i]].type == AbilityType.Toggle && pc.activeUnit.IndexOfEveryFrameAbility(slotToGlobalAbilityIndex[i], false) != -1)
                    {
                        element.AddToClassList("activeAbility");
                    }

                    // If ability has cooldown, show it
                    if (!isLeveling)
                    {
                        int abilityCooldownIndex = pc.activeUnit.GetAbilityCooldownIndex(slotToGlobalAbilityIndex[i], false);
                        if (abilityCooldownIndex != -1) AddCooldownElement(element, abilityCooldownIndex);
                    }
                }
            }
        }

        public void InventoryDisplay()
        {
            if (pc.activeUnit.InventorySize > 0)
            {
                // Show and Refresh UI
                inventoryUI.style.display = DisplayStyle.Flex;
                inventoryView.Clear();

                // Display items
                for (int i = 0; i < GameManager.maxInventorySize; i++)
                {
                    GroupBox element = null;

                    if (i < pc.activeUnit.InventorySize)
                    {
                        if (pc.activeUnit.items[i] == null)
                        {
                            // Empty slot
                            // Display empty 
                            EmptyElementCreate(inventoryView, i, true);
                        }
                        else
                        {
                            // unit.items[slotToItemIndex[i]] - the item at the current slot
                            // Display item
                            element = ElementCreate(inventoryView, i, IconAt(pc.activeUnit.items[i].icon, 0), false, true);
                        }
                    }
                    else
                    {
                        // Unavailable slot
                        EmptyElementCreate(inventoryView, -1, true, true);
                    }

                    if (element != null)
                    {
                        // If toggle is active, add indicator
                        Ability itemAbility = (Ability)pc.activeUnit.items[i];
                        if (itemAbility.type == AbilityType.Toggle && pc.activeUnit.IndexOfEveryFrameAbility(itemAbility, true) != -1)
                        {
                            element.AddToClassList("activeAbility");
                        }

                        // If ability has cooldown, show it
                        int abilityCooldownIndex = pc.activeUnit.GetAbilityCooldownIndex(i, true);
                        if (abilityCooldownIndex != -1)
                        {
                            AddCooldownElement(element, abilityCooldownIndex);
                        }

                        // Charges
                        if (pc.activeUnit.itemCharges[i] > 0)
                        {
                            Label charges = new Label();
                            charges.AddToClassList("charges");
                            charges.text = pc.activeUnit.itemCharges[i].ToString();
                            element.Add(charges);
                        }
                    }
                }
            }

            // We update cooldown timers to properly show currently added timers since the inventory display is called after the ability display
            CooldownTimerUpdate();
        }

        private void RemoveInventoryDisplay()
        {
            inventoryUI.style.display = DisplayStyle.None;
        }

        // Creates ability or item UI element. pathPrefix is used in container abilities, to find which ability exactly is being utilized.
        private GroupBox ElementCreate(VisualElement parent, int abilityIndex, Texture2D iconImage, bool locked = false, bool item = false, string pathPrefix = "") // Ability or inventory element creator
        {
            GroupBox button = new GroupBox();
            button.name = pathPrefix + abilityIndex.ToString();
            if (item)
            {
                button.AddToClassList("ItemButton");
                button.RegisterCallback<MouseEnterEvent>(InventoryDescriptor);
                button.RegisterCallback<MouseLeaveEvent>(HideDescriptor);

                button.style.backgroundImage = iconImage;
            }
            else
            {
                button.AddToClassList("AbilityButton");
                button.RegisterCallback<MouseEnterEvent>(AbilityDescriptor);
                button.RegisterCallback<MouseLeaveEvent>(HideDescriptor);
                if (locked) button.AddToClassList("emptySlot");

                GroupBox icon = new GroupBox();
                icon.name = pathPrefix + abilityIndex.ToString();
                icon.style.backgroundImage = iconImage;
                icon.AddToClassList("AbilityButtonIcon");
                if (locked) icon.AddToClassList("locked");

                button.Add(icon);
            }
            parent.Add(button);

            return button;
        }

        private void EmptyElementCreate(VisualElement parent, int abilityIndex, bool item = false, bool unavailableSlot = false) // Ability or inventory element creator
        {
            GroupBox button = new GroupBox();
            if (item)
            {
                button.name = abilityIndex.ToString();
                button.AddToClassList("ItemButton");

                if (unavailableSlot) button.AddToClassList("emptySlot");
            }
            else
            {
                button.name = "null";
                button.AddToClassList("AbilityButton");
            }

            parent.Add(button);
        }

        // Handles clicks of abilities
        void AbilityHandler(ClickEvent evt)
        {
            VisualElement clickedElement = evt.target as VisualElement;

            if (clickedElement.name != "null" && !clickedElement.ClassListContains("locked"))
            {
                Ability clickedAbility = Utils.GetAbilityByIndexName(pc.activeUnit, clickedElement.name, out int abilityIndex);

                if (clickedAbility != null)
                {
                    UseAbility(clickedAbility, abilityIndex, false);
                }
            }
        }

        // Uses the given available ability by the active unit
        private void UseAbility(Ability clickedAbility, int abilityIndex, bool isItem)
        {
            // Handle ability control

            // When leveling any click on ability will cause its levelup
            if (isLeveling && !isItem)
            {
                if (clickedAbility.heroLevelable)
                {
                    if (pc.activeUnit.LevelUpAbilityCommand(clickedAbility, abilityIndex))
                    {
                        // Successful ability level increase
                        ShowLevelButton();
                        RedrawAbilityView();
                        if (pc.activeUnit.levelingUnit.abilityPoints == 0)
                        {
                            HideLevelButton(true);
                        }
                    }
                }
                else if (clickedAbility.type == AbilityType.Container)
                {
                    // Add container index, redraw ability view and show return button
                    openContainersIndex.Add(abilityIndex);
                    RedrawAbilityView();
                    ShowReturnButton();
                }
            }
            else
            {
                // Check if ability unlocked - abilities inside containers will inherit the lock state from container itself
                if (isItem || clickedAbility.heroLevelable || !pc.activeUnit.abilityLocked[abilityIndex])
                {
                    // Check if is cooldown ok
                    if (!pc.activeUnit.IsCooldownGood(abilityIndex, isItem)) return;

                    // Container
                    if (!isItem && clickedAbility.type == AbilityType.Container)
                    {
                        // Add container index, redraw ability view and show return button
                        openContainersIndex.Add(abilityIndex);
                        RedrawAbilityView();
                        ShowReturnButton();
                    }

                    // Item 
                    else if (!isItem && clickedAbility.isItem)
                    {
                        pc.activeUnit.BuyItem(abilityIndex);
                    }

                    // Area ability
                    else if (clickedAbility.type == AbilityType.Area)
                    {
                        int lvl = (isItem) ? 0 : pc.activeUnit.abilityLevel[abilityIndex];
                        pc.ChangeMode(PCMode.Area, clickedAbility.radius[lvl], abilityIndex, isItem, clickedAbility);
                        ShowCancelButton();
                    }

                    // Location ability
                    else if (clickedAbility.type == AbilityType.Location)
                    {
                        pc.ChangeMode(PCMode.Position, 0, abilityIndex, isItem, clickedAbility);
                        ShowCancelButton();
                    }

                    // Unit ability
                    else if (clickedAbility.type == AbilityType.Unit)
                    {
                        pc.ChangeMode(PCMode.Unit, 0, abilityIndex, isItem, clickedAbility);
                        ShowCancelButton();
                    }

                    // Processes
                    else if (clickedAbility.type == AbilityType.Process)
                    {
                        if (clickedAbility is Research)
                        {
                            // Research
                            if (pc.activeUnit.AddProcess(abilityIndex, isItem))
                            {
                                // Redraw ability display
                                RedrawAbilityView();
                            }
                        }
                        else
                        {
                            // Process
                            pc.activeUnit.AddProcess(abilityIndex, isItem);
                        }
                    }

                    // Construction
                    else if (clickedAbility.type == AbilityType.Construction)
                    {
                        int lvl = (isItem) ? 0 : pc.activeUnit.abilityLevel[abilityIndex];
                        pc.ChangeMode(PCMode.Placement, clickedAbility.radius[lvl], abilityIndex, isItem, clickedAbility);
                        ShowCancelButton();
                    }

                    // Toggle
                    else if (clickedAbility.type == AbilityType.Toggle)
                    {
                        pc.activeUnit.UseAbilityItem(abilityIndex, isItem, null, Vector3.zero, true);
                    }

                    // Active
                    else if (clickedAbility.type == AbilityType.Active)
                    {
                        // Last check for UpgradeBuilding
                        if (clickedAbility is UpgradeBuilding && (pc.activeUnit.activeProcess[0] != null || (pc.activeUnit.transportUnit && pc.activeUnit.transportUnit.units.Count > 0))) return;

                        pc.activeUnit.UseAbilityItem(abilityIndex, isItem, null, Vector3.zero, true);
                    }
                }
                else
                {
                    ShowNotifyMsg("This ability is locked", pc.activeUnit.owner, false);
                }
            }
        }

        // COOLDOWNS
        List<VisualElement> cooldownElements = new List<VisualElement>();
        List<Label> cooldownTimers = new List<Label>();
        List<int> cooldownIndex = new List<int>();

        private void CooldownTimerUpdate()
        {
            for (int i = 0; i < cooldownIndex.Count; i++)
            {
                int abilityIndex = cooldownIndex[i];
                if (pc.activeUnit.cooldownAbilityIsItem[abilityIndex])
                {
                    cooldownElements[i].style.scale = new StyleScale(new Vector2(1, pc.activeUnit.cooldownAbility[abilityIndex] / pc.activeUnit.items[pc.activeUnit.cooldownAbilityIndex[abilityIndex]].cooldown[0]));
                }
                else
                {
                    cooldownElements[i].style.scale = new StyleScale(new Vector2(1, pc.activeUnit.cooldownAbility[abilityIndex] / pc.activeUnit.abilities[pc.activeUnit.cooldownAbilityIndex[abilityIndex]].cooldown[pc.activeUnit.abilityLevel[pc.activeUnit.cooldownAbilityIndex[abilityIndex]]]));
                }
                cooldownTimers[i].text = pc.activeUnit.cooldownAbility[abilityIndex].ToString("F2");
            }
        }

        private void AddCooldownElement(VisualElement parent, int abilityCooldownIndex)
        {
            // Cooldown element
            VisualElement cooldownElement = new VisualElement();
            cooldownElement.pickingMode = PickingMode.Ignore;
            cooldownElement.AddToClassList("buttonCD");
            parent.Add(cooldownElement);
            cooldownElements.Add(cooldownElement);

            // Cooldown timer
            Label cdTimer = new Label();
            cdTimer.pickingMode = PickingMode.Ignore;
            cdTimer.AddToClassList("process-timer");
            parent.Add(cdTimer);
            cooldownTimers.Add(cdTimer);

            // Index
            cooldownIndex.Add(abilityCooldownIndex);
        }

        void AbilityDescriptor(MouseEnterEvent evt)
        {
            VisualElement hoveredElement = evt.target as VisualElement;

            if (hoveredElement.name != "null")
            {
                Ability hoveredAbility = Utils.GetAbilityByIndexName(pc.activeUnit, hoveredElement.name, out int abilityIndex);

                if (hoveredAbility != null)
                {
                    // Name set
                    if (pc.activeUnit.abilityLevel[abilityIndex] == -1) descriptorName.text = hoveredAbility.abilityName[0];
                    else if (hoveredAbility.abilityName.Length > pc.activeUnit.abilityLevel[abilityIndex]) descriptorName.text = hoveredAbility.abilityName[pc.activeUnit.abilityLevel[abilityIndex]];
                    else descriptorName.text = hoveredAbility.abilityName[hoveredAbility.abilityName.Length - 1];

                    // Hotkey
                    int elemIndex = abilityScrollView.IndexOf(hoveredElement);
                    if (elemIndex != -1 && hoveredAbility.type != AbilityType.Aura && hoveredAbility.type != AbilityType.Passive)
                    {
                        descriptorHotkey.style.display = DisplayStyle.Flex;

                        if (elemIndex == 0) descriptorHotkey.text = "[Q]";
                        else if (elemIndex == 1) descriptorHotkey.text = "[W]";
                        else if (elemIndex == 2) descriptorHotkey.text = "[E]";
                        else if (elemIndex == 3) descriptorHotkey.text = "[R]";

                        else if (elemIndex == 4) descriptorHotkey.text = "[Z]";
                        else if (elemIndex == 5) descriptorHotkey.text = "[X]";
                        else if (elemIndex == 6) descriptorHotkey.text = "[C]";
                        else if (elemIndex == 7) descriptorHotkey.text = "[V]";

                        else if (elemIndex == 8) descriptorHotkey.text = "[T]";
                        else if (elemIndex == 9) descriptorHotkey.text = "[Y]";
                        else if (elemIndex == 10) descriptorHotkey.text = "[U]";
                        else if (elemIndex == 11) descriptorHotkey.text = "[I]";

                        else if (elemIndex == 12) descriptorHotkey.text = "[B]";
                        else if (elemIndex == 13) descriptorHotkey.text = "[N]";
                        else if (elemIndex == 14) descriptorHotkey.text = "[M]";
                        else if (elemIndex == 15) descriptorHotkey.text = "[G]";

                        else descriptorHotkey.style.display = DisplayStyle.None;
                    }
                    else
                    {
                        descriptorHotkey.style.display = DisplayStyle.None;
                    }

                    // If we are learning an ability we show the next level parameters
                    int abilityLevel = pc.activeUnit.abilityLevel[abilityIndex];
                    if (isLeveling) abilityLevel += 1;

                    // Indicate that this is an item
                    if (hoveredAbility.isItem)
                    {
                        descriptorLevel.style.display = DisplayStyle.Flex;
                        descriptorLevel.text = "[Item]";
                    }
                    // Level
                    else if (hoveredAbility.maxLevels > 0)
                    {
                        descriptorLevel.style.display = DisplayStyle.Flex;
                        descriptorLevel.text = "Level " + (abilityLevel + 1) + "/" + hoveredAbility.maxLevels;
                    }
                    else descriptorLevel.style.display = DisplayStyle.None;

                    // Lock state
                    if (((isLeveling || !hoveredAbility.heroLevelable) && pc.activeUnit.abilityLocked[abilityIndex]))
                    {
                        descriptorLockbox.style.display = DisplayStyle.Flex;
                        descriptorLockNames.Clear();

                        // Display tech that is required for this ability
                        // Level requirements
                        if (hoveredAbility.requiredLevel.Length > abilityLevel)
                        {
                            LockNameCreate("Level " + hoveredAbility.requiredLevel[abilityLevel], "Gain more XP to increase your level.");
                        }
                        // Tech requirements
                        if (hoveredAbility.requiredTech.Length > abilityLevel)
                        {
                            for (int i = 0; i < hoveredAbility.requiredTech[abilityLevel].data.Length; i++)
                            {
                                if (!TechnologyManager.Instance.isUnlocked(hoveredAbility.requiredTech[abilityLevel].data[i], pc.activeUnit.owner))
                                {
                                    // Not unlocked, display the name of the tech
                                    LockNameCreate(hoveredAbility.requiredTech[abilityLevel].data[i].displayName, hoveredAbility.requiredTech[abilityLevel].data[i].description);
                                }
                            }
                        }
                    }
                    else
                    {
                        descriptorLockbox.style.display = DisplayStyle.None;
                    }

                    // Cost
                    descriptorUsageCost.Clear();
                    descriptorResourceCost.Clear();
                    descriptorCostBox.style.display = DisplayStyle.None;

                    if (hoveredAbility.manaCost.Length > abilityLevel && hoveredAbility.manaCost[abilityLevel] != 0)
                    {
                        // Mana
                        CostElementCreate(hoveredAbility.manaCost[abilityLevel], true, false);
                        descriptorCostBox.style.display = DisplayStyle.Flex;
                    }
                    if (hoveredAbility.manaCostPerSecond.Length > abilityLevel && hoveredAbility.manaCostPerSecond[abilityLevel] != 0)
                    {
                        // Mana cost per second
                        CostElementCreate(hoveredAbility.manaCostPerSecond[abilityLevel], true, false, null, true);
                        descriptorCostBox.style.display = DisplayStyle.Flex;
                    }

                    if (hoveredAbility.cooldown.Length > abilityLevel && hoveredAbility.cooldown[abilityLevel] != 0)
                    {
                        // Cooldown
                        CostElementCreate(hoveredAbility.cooldown[abilityLevel], false, true);
                        descriptorCostBox.style.display = DisplayStyle.Flex;
                    }

                    // Resource cost
                    if (hoveredAbility.cost.Length > abilityLevel)
                    {
                        for (int i = 0; i < hoveredAbility.cost[abilityLevel].data.Length; i++)
                        {
                            CostElementCreate(hoveredAbility.cost[abilityLevel].data[i].value, false, false, hoveredAbility.cost[abilityLevel].data[i].type.icon);
                            descriptorCostBox.style.display = DisplayStyle.Flex;
                        }
                    }

                    // Description
                    if (pc.activeUnit.abilityLevel[abilityIndex] == -1) descriptorDescription.text = hoveredAbility.description[0];
                    else if (hoveredAbility.description.Length > pc.activeUnit.abilityLevel[abilityIndex]) descriptorDescription.text = hoveredAbility.description[pc.activeUnit.abilityLevel[abilityIndex]];
                    else descriptorDescription.text = hoveredAbility.description[hoveredAbility.abilityName.Length - 1];

                    // Bottom Descriptor
                    descriptorBottomText.style.display = DisplayStyle.Flex;
                    descriptorBottomText.text = "";
                    if (hoveredAbility.castTime.Length > abilityLevel && hoveredAbility.type != AbilityType.Aura && hoveredAbility.type != AbilityType.Passive)
                    {
                        descriptorBottomText.text += "CAST TIME: " + hoveredAbility.castTime[abilityLevel] + "s";
                    }
                    if (hoveredAbility.castRange.Length > abilityLevel && hoveredAbility.type != AbilityType.Aura && hoveredAbility.type != AbilityType.Passive)
                    {
                        if (descriptorBottomText.text != "") descriptorBottomText.text += "\n";
                        descriptorBottomText.text += "CAST RANGE: " + hoveredAbility.castRange[abilityLevel];
                    }
                    if (hoveredAbility.duration.Length > abilityLevel && hoveredAbility.type != AbilityType.Aura && hoveredAbility.type != AbilityType.Passive)
                    {
                        if (descriptorBottomText.text != "") descriptorBottomText.text += "\n";
                        descriptorBottomText.text += "DURATION: " + hoveredAbility.duration[abilityLevel];
                    }
                    if (hoveredAbility.radius.Length > abilityLevel && hoveredAbility.radius[abilityLevel] != 0)
                    {
                        if (descriptorBottomText.text != "") descriptorBottomText.text += "\n";
                        descriptorBottomText.text += "RADIUS: " + hoveredAbility.radius[abilityLevel];
                    }
                    if (descriptorBottomText.text != "") descriptorBottomText.text += "\n";
                    descriptorBottomText.text += "TYPE: " + hoveredAbility.type;

                    if (hoveredAbility.continuous) descriptorBottomText.text += " / CONTINUOUS";

                    descriptor.style.display = DisplayStyle.Flex;
                }
            }
        }
    }
}
