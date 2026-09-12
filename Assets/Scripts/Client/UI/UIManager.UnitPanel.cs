using Camera_TopDownNS;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // UIManager.UnitPanel.cs — панель юнита (подписка/статы/дескрипторы юнита). Вырезано 1:1 из UIManager.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class UIManager
    {

        // When active unit changes subscribe to that unit parameter changes and show icon/abilities
        public void SubscribeToUnit()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            ResetUnitUI();

            // AbilityView and Inventory
            abilityView.style.display = DisplayStyle.Flex;
            ShowCommands();
            AbilityInventoryDisplay();

            pc.activeUnit.OnRedrawAbilityView += AbilityInventoryDisplay;
            pc.activeUnit.OnInventoryChange += InventoryDisplay;
            GameManager.instance.Tick += CooldownTimerUpdate;

            // Subscribe AbilityDisplay to Technology Lock and Unlock. 
            TechnologyManager.instance.OnTechUnlock[pc.activeUnit.owner] += AbilityInventoryDisplay;
            TechnologyManager.instance.OnTechLock[pc.activeUnit.owner] += AbilityInventoryDisplay;

            // Process
            if (pc.activeUnit.team == SlotManager.instance.currentTeam)
            {
                // Processing unit
                if (pc.activeUnit.canProcess)
                {
                    ProcessDisplay();
                    //ShowProcesses();
                    pc.activeUnit.OnProcessUpdate += ProcessDisplay;
                }
                // Transport Unit
                if (pc.activeUnit.transportUnit)
                {
                    TransportDisplay();
                    pc.activeUnit.transportUnit.transportChange += TransportDisplay;
                }
            }

            // Hero ability leveling
            if (pc.activeUnit.levelingUnit && pc.activeUnit.levelingUnit.abilityPoints != 0)
            {
                ShowLevelButton();
            }

            // Status
            DisplayStatusTab();
            pc.activeUnit.OnStatusUpdate += DisplayStatusTab;

            // Unit Box
            UnitBoxDisplay();

            HealthDisplay();
            ManaDisplay();
            XpDisplay();

            // Subscribe to changes
            pc.activeUnit.OnHPChange += HealthDisplay;
            pc.activeUnit.OnMPChange += ManaDisplay;
            if (pc.activeUnit.levelingUnit) pc.activeUnit.levelingUnit.OnXPChange += XpDisplay;

            // Unit stats
            DisplayUnitStats();

            pc.activeUnit.OnCharacteristicsChange += UpdateDamageInfo;
            pc.activeUnit.OnCharacteristicsChange += UpdateArmorInfo;

            // LifeTime
            if (pc.activeUnit.lifetimeUnit)
            {
                lifetimeBar.style.display = DisplayStyle.Flex;
                GameManager.instance.Tick += LifetimeUpdate;
            }
            else if (pc.activeUnit.isBeingBuilt)
            {
                lifetimeBar.style.display = DisplayStyle.Flex;
                GameManager.instance.Tick += ConstructionUpdate;
            }
            else if (pc.activeUnit.polymorphed)
            {
                lifetimeBar.style.display = DisplayStyle.Flex;
                GameManager.instance.Tick += PolymorphUpdate;
            }
            else lifetimeBar.style.display = DisplayStyle.None;

            // Hotkey
            PlayerControl.coreInput.Main.AnyKey.performed += AnyKeyPressed;

            // Waypoint
            pc.activeUnit.ShowWaypoint();
            pc.activeUnit.WaypointUpdate += pc.activeUnit.ShowWaypoint;

            // Debug
            // GameManager.instance.Tick += SetUnitInfo;
        }

        // Active unit is no longer there, unsubscribe and hide the icon/abilities
        public void UnsubscribeToUnit(Unit unit)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            // Subscribe AbilityDisplay to Technology Lock and Unlock.
            pc.activeUnit.OnRedrawAbilityView -= AbilityInventoryDisplay;
            pc.activeUnit.OnInventoryChange -= InventoryDisplay;
            TechnologyManager.instance.OnTechUnlock[pc.activeUnit.owner] -= AbilityInventoryDisplay;
            TechnologyManager.instance.OnTechLock[pc.activeUnit.owner] -= AbilityInventoryDisplay;

            // Cooldown
            GameManager.instance.Tick -= CooldownTimerUpdate;
            cooldownElements.Clear();
            cooldownTimers.Clear();
            cooldownIndex.Clear();

            pc.activeUnit.OnStatusUpdate -= DisplayStatusTab;

            pc.activeUnit.OnHPChange -= HealthDisplay;
            pc.activeUnit.OnMPChange -= ManaDisplay;
            if (pc.activeUnit.levelingUnit) pc.activeUnit.levelingUnit.OnXPChange -= XpDisplay;

            if (pc.activeUnit.canProcess) pc.activeUnit.OnProcessUpdate -= ProcessDisplay;
            if (pc.activeUnit.transportUnit) pc.activeUnit.transportUnit.transportChange -= TransportDisplay;

            // Unit stats
            //DisplayUnitStats();
            //HideCommands();

            pc.activeUnit.OnCharacteristicsChange -= UpdateDamageInfo;
            pc.activeUnit.OnCharacteristicsChange -= UpdateArmorInfo;

            // LifeTime
            if (pc.activeUnit.lifetimeUnit) GameManager.instance.Tick -= LifetimeUpdate;
            else if (pc.activeUnit.constructionUnit) GameManager.instance.Tick -= ConstructionUpdate;
            GameManager.instance.Tick -= PolymorphUpdate;

            //HideStatusTab();

            //RemoveIconDisplay();
            //RemoveUnitName();
            //HideProcesses();
            ResetUnitUI();
            //HideLevelButton(true);

            // Hotkey
            PlayerControl.coreInput.Main.AnyKey.performed -= AnyKeyPressed;

            // Waypoint
            ReferenceManager.instance.HideWaypoint();
            pc.activeUnit.WaypointUpdate -= pc.activeUnit.ShowWaypoint;

            // Debug
            // GameManager.instance.Tick -= SetUnitInfo;
        }

        // Redraw the active unit info by resubscribing to it
        public void Resubscribe()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            if (pc.activeUnit != null)
            {
                UnsubscribeToUnit(pc.activeUnit);
                SubscribeToUnit();
            }
        }

        public void AbilityInventoryDisplay()
        {
            if (pc.activeUnit != null)
            {
                RedrawAbilityView();
                InventoryDisplay();
            }
        }

        // Clears the UI as if no unit is selected
        public void ResetUnitUI()
        {
            //HideReturnButton();

            // Ability view
            // abilityView.style.display = DisplayStyle.None;
            RebuildAbilityView();

            // Inventory box
            RemoveInventoryDisplay();

            // UnitBox
            unitBox.style.display = DisplayStyle.None;

            // RemoveHealthDisplay();
            // RemoveManaDisplay();
            // RemoveIconDisplay();
            // RemoveXpDisplay();

            HideProcesses();
            HideTransport();
            HideDescriptor();

            HideStatusTab();
            // HideUnitStats();

            HideLevelButton(true);
            HideCommands();

            openContainersIndex.Clear();
        }

        // Health display
        private void HealthDisplay()
        {
            healthBar.title = Mathf.Ceil(pc.activeUnit.health) + " / " + Mathf.Ceil(pc.activeUnit.maxHealth);
            healthBar.value = pc.activeUnit.health / pc.activeUnit.maxHealth;
            healthRegen.text = (pc.activeUnit.healthRegen > 0) ? "+" + pc.activeUnit.healthRegen.ToString("0.0") : pc.activeUnit.healthRegen.ToString("0.0");
        }

        private void RemoveHealthDisplay()
        {
            healthBar.title = "";
            healthBar.value = 0;
            healthRegen.text = "";
        }

        // Mana display
        private void ManaDisplay()
        {
            if (pc.activeUnit.maxMana != 0)
            {
                manaBar.style.display = DisplayStyle.Flex;
                string resourceLabel="";foreach(var ability in pc.activeUnit.abilities)if(ability is CompositePassive passive&&passive.combatResource!=null&&passive.combatResource.enabled){resourceLabel=passive.combatResource.resourceName+" ";break;}
                manaBar.title = resourceLabel + Mathf.Ceil(pc.activeUnit.mana) + " / " + Mathf.Ceil(pc.activeUnit.maxMana);
                manaBar.value = pc.activeUnit.mana / pc.activeUnit.maxMana;
                manaRegen.text = (pc.activeUnit.manaRegen > 0) ? "+" + pc.activeUnit.manaRegen.ToString("0.0") : pc.activeUnit.manaRegen.ToString("0.0");
            }
            else
            {
                manaBar.style.display = DisplayStyle.None;
            }
        }

        // Unit icon display
        private void UnitBoxDisplay()
        {
            unitBox.style.display = DisplayStyle.Flex;

            unitName.text = pc.activeUnit.unitName;
            unitIcon.style.backgroundImage = pc.activeUnit.icon;
        }

        private void IconDisplay()
        {
            unitIcon.style.backgroundImage = pc.activeUnit.icon;
        }

        private void RemoveIconDisplay()
        {
            unitIcon.style.backgroundImage = null;
        }

        private void UnitNameDisplay()
        {
            unitName.style.display = DisplayStyle.Flex;
            unitName.text = pc.activeUnit.unitName;
        }

        private void RemoveUnitName()
        {
            unitName.style.display = DisplayStyle.None;
        }

        // When hovering over unit name we show its description
        private void UnitDescriptor(MouseEnterEvent evt)
        {
            string description = "";
            if (pc.activeUnit.description != "") description = pc.activeUnit.description;

            // Move speed
            if (pc.activeUnit.canMove)
            {
                if (description.Length > 0) description += "\n";
                description += "Movement speed: " + pc.activeUnit.moveSpeed;
            }

            // Resource unit
            if (pc.activeUnit.resourceUnit)
            {
                // Collectible
                if (pc.activeUnit.resourceUnit.isCollectible)
                {
                    string collectible = "";
                    for (int i = 0; i < pc.activeUnit.resourceUnit.collectibleResources.Length; i++)
                    {
                        collectible += "\n" + pc.activeUnit.resourceUnit.collectibleResources[i].type.displayName + ": " + pc.activeUnit.resourceUnit.collectibleResources[i].value;
                    }
                    if (collectible.Length > 0) description += "\n\nCollectible resources:" + collectible;
                }
                // Collector
                else if (pc.activeUnit.resourceUnit.isCollector)
                {
                    string collector = "";
                    for (int i = 0; i < pc.activeUnit.resourceUnit.currentHeldResources.Count; i++)
                    {
                        collector += "\n" + pc.activeUnit.resourceUnit.currentHeldResources[i].type.displayName + ": " + pc.activeUnit.resourceUnit.currentHeldResources[i].value;

                        int collectorResourceIndex = ResourceUnit.GetResourceIndex(pc.activeUnit.resourceUnit.currentHeldResources[i].type, pc.activeUnit.resourceUnit.collectorResources);
                        if (collectorResourceIndex != -1)
                        {
                            collector += "\\" + pc.activeUnit.resourceUnit.collectorResources[collectorResourceIndex].value;
                        }
                    }
                    if (collector.Length > 0) description += "\n\nCurrent resources:" + collector;
                }
            }

            // Leveling unit
            if (pc.activeUnit.levelingUnit)
            {
                FillDescriptor(pc.activeUnit.unitName, description, pc.activeUnit.levelingUnit.level, pc.activeUnit.levelingUnit.maxLevel);
            }
            else
            {
                FillDescriptor(pc.activeUnit.unitName, description);
            }
        }

        // XP display
        private void XpDisplay()
        {
            if (pc.activeUnit.levelingUnit && pc.activeUnit.levelingUnit.maxLevel != 1)
            {
                XpBar.style.display = DisplayStyle.Flex;
                XpBar.title = pc.activeUnit.levelingUnit.level.ToString();
                XpBar.value = (float)(pc.activeUnit.levelingUnit.currentExp - pc.activeUnit.levelingUnit.previousExpRequired) / (pc.activeUnit.levelingUnit.expRequired - pc.activeUnit.levelingUnit.previousExpRequired);

                // Ability Points
                if (pc.activeUnit.levelingUnit.abilityPoints != 0)
                {
                    ShowLevelButton();
                }
            }
            else
            {
                XpBar.style.display = DisplayStyle.None;
            }
        }

        private void RemoveXpDisplay()
        {
            XpBar.style.display = DisplayStyle.None;
        }

        private void LifetimeUpdate()
        {
            if (pc.activeUnit != null)
            {
                lifetimeBar.title = Mathf.Ceil(pc.activeUnit.lifetimeUnit.currentLifeSpan) + "s";
                lifetimeBar.value = pc.activeUnit.lifetimeUnit.currentLifeSpan / pc.activeUnit.lifetimeUnit.lifespan;
            }
        }

        private void ConstructionUpdate()
        {
            if (pc.activeUnit != null)
            {
                if (pc.activeUnit.constructionUnit.upgradeBuildingRef)
                {
                    // Upgrade time
                    lifetimeBar.title = (pc.activeUnit.constructionUnit.upgradeTime * (1 - pc.activeUnit.constructionUnit.currentConstructionPercentage)).ToString("F1") + "s";
                }
                else
                {
                    // Construction time
                    lifetimeBar.title = (pc.activeUnit.constructionUnit.constructionTime * (1 - pc.activeUnit.constructionUnit.currentConstructionPercentage)).ToString("F1") + "s";
                }

                lifetimeBar.value = pc.activeUnit.constructionUnit.currentConstructionPercentage;
            }
        }

        private void PolymorphUpdate()
        {
            if (pc.activeUnit != null)
            {
                if (pc.activeUnit.polymorphed)
                {
                    lifetimeBar.value = pc.activeUnit.polymorphTime / pc.activeUnit.polymorphTotalTime;
                    lifetimeBar.title = (pc.activeUnit.polymorphTime).ToString("F1") + "s";
                }
                else
                {
                    lifetimeBar.style.display = DisplayStyle.None;
                    GameManager.instance.Tick -= PolymorphUpdate;
                }
            }
        }

        private void DisplayUnitStats()
        {
            // Damage
            if (pc.activeUnit.canAttack)
            {
                attackElement.style.display = DisplayStyle.Flex;
                UpdateDamageInfo();
            }
            else
            {
                attackElement.style.display = DisplayStyle.None;
            }

            // Armor
            UpdateArmorInfo();

            // Attributes
            if (pc.activeUnit.attributeUnit) attributesElement.style.display = DisplayStyle.Flex;
            else attributesElement.style.display = DisplayStyle.None;
        }

        private void HideUnitStats()
        {
            attackElement.style.display = DisplayStyle.None;
            armorElement.style.display = DisplayStyle.None;
            attributesElement.style.display = DisplayStyle.None;
            XpBar.style.display = DisplayStyle.None;
            unitName.style.display = DisplayStyle.None;
        }

        private void ShowCommands()
        {
            // Check if unit belongs to the current player
            if (!SlotManager.instance.debugMode && pc.activeUnit.owner != SlotManager.instance.currentPlayer) return;

            // Construction cancel button
            if (pc.activeUnit.isBeingBuilt)
            {
                ShowCancelButton();
                return;
            }

            if (pc.activeUnit.canAttack)
            {
                attackCommand.style.display = DisplayStyle.Flex;
            }
            // Skip move command - not enough space in UI
            // if (pc.activeUnit.canMove)
            // {
            //     moveCommand.style.display = DisplayStyle.Flex;
            //     holdCommand.style.display = DisplayStyle.Flex;
            // }
            stopCommand.style.display = DisplayStyle.Flex;
            holdCommand.style.display = DisplayStyle.Flex;

            if (pc.activeUnit.isShop) shopButton.style.display = DisplayStyle.Flex;

            //CommandUpdate();
            // Subscribe
            //if (pc.activeUnit) pc.activeUnit.OnCommand += CommandUpdate;
        }

        private void HideCommands()
        {
            moveCommand.style.display = DisplayStyle.None;
            stopCommand.style.display = DisplayStyle.None;
            holdCommand.style.display = DisplayStyle.None;
            attackCommand.style.display = DisplayStyle.None;

            shopButton.style.display = DisplayStyle.None;
            returnButton.style.display = DisplayStyle.None;
            levelButton.style.display = DisplayStyle.None;
            cancelButton.style.display = DisplayStyle.None;

            // Unsub
            //if (pc.activeUnit) pc.activeUnit.OnCommand -= CommandUpdate;
        }

        // To display the state of the unit
        private void CommandUpdate()
        {
            if (pc.activeUnit.unitState == UnitStates.Hold) holdCommand.AddToClassList("commandButtonActive");
            else holdCommand.RemoveFromClassList("commandButtonActive");

            if (pc.activeUnit.unitState == UnitStates.Attack) attackCommand.AddToClassList("commandButtonActive");
            else attackCommand.RemoveFromClassList("commandButtonActive");
        }

        private void UpdateDamageInfo()
        {
            attackValue.text = pc.activeUnit.attackDamage.ToString("F0");
        }

        private void UpdateArmorInfo()
        {
            // Armor
            armorElement.style.display = DisplayStyle.Flex;
            armorValue.text = pc.activeUnit.armor.ToString();

            // Invulnerability
            if (pc.activeUnit.isInvulnerable) invulnerable.style.display = DisplayStyle.Flex;
            else invulnerable.style.display = DisplayStyle.None;
        }

        private void DamageDescriptor(MouseEnterEvent evt)
        {
            if (pc.activeUnit)
            {
                if (!pc.activeUnit.damageType)
                {
                    Debug.LogWarning("Damage type of " + pc.activeUnit.unitName + " is not set!");
                    return;
                }

                if (pc.activeUnit.melee)
                {
                    FillDescriptor("Type: " + pc.activeUnit.damageType.displayName + "",
                                   "Attack Speed: " + pc.activeUnit.attackSpeed.ToString("F2") + "\n" + pc.activeUnit.damageType.description,
                                   "Melee");
                }
                else
                {
                    FillDescriptor("Type: " + pc.activeUnit.damageType.displayName + "",
                                   "Attack Speed: " + pc.activeUnit.attackSpeed.ToString("F2") + "\n" + pc.activeUnit.damageType.description,
                                   "Range: " + pc.activeUnit.attackRange);
                }

                // Range projector
                if (!pc.activeUnit.melee) pc.CreateRangeProjector();
            }
        }

        private void ArmorDescriptor(MouseEnterEvent evt)
        {
            if (pc.activeUnit)
            {
                if (!pc.activeUnit.armorType)
                {
                    Debug.LogWarning("Armor type of " + pc.activeUnit.unitName + " is not set!");
                    return;
                }

                FillDescriptor("Type: " + pc.activeUnit.armorType.displayName, "Current damage reduction: " + (100 * ((0.06f * pc.activeUnit.armor) / (1 + 0.06f * pc.activeUnit.armor))).ToString("F2") + "%\n" + pc.activeUnit.armorType.description);
            }
        }

        private void XPDescriptor(MouseEnterEvent evt)
        {
            if (pc.activeUnit)
            {
                FillDescriptor(
                "Level " + pc.activeUnit.levelingUnit.level + " / " + pc.activeUnit.levelingUnit.maxLevel,
                "Level can be obtained by killing enemy units",
                "XP: " + pc.activeUnit.levelingUnit.currentExp + " / " + pc.activeUnit.levelingUnit.expRequired
                );
            }
        }

        private void AttributeDescriptor(MouseEnterEvent evt)
        {
            FillDescriptor("Main attribute: " + pc.activeUnit.attributeUnit.mainAttribute.displayName, "");

            for (int i = 0; i < pc.activeUnit.attributeUnit.unitAttributes.Length; i++)
            {
                if (i != 0) descriptorDescription.text += "\n";
                descriptorDescription.text += pc.activeUnit.attributeUnit.unitAttributes[i].attribute.displayName + ": " + pc.activeUnit.attributeUnit.unitAttributes[i].value;
            }
        }
    }
}
