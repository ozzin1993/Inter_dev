using System.Globalization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using SimpleJSON;

namespace StrategyCore
{
    // SaveManager.UnitData.cs — сериализация юнитов (Save/Load/SetUnitData). Вырезано 1:1 из SaveManager.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class SaveManager
    {
        public static string SaveUnitData()
        {
            JSONObject unitData = new JSONObject();

            int u = 0;
            foreach (KeyValuePair<UInt16, Unit> unit in SlotManager.instance.unitNetID)
            {
                var newUnitData = new JSONObject();

                // By comparing original prefab`s values we decide if the parameters should be synced

                Unit refUnit = GameManager.instance.gameUnits[unit.Value.unitTypeID];

                // NetID
                newUnitData["netID"] = unit.Value.netID;
                // TypeID
                newUnitData["unitTypeID"] = unit.Value.unitTypeID;
                // Transform
                newUnitData["position"] = unit.Value.transform.position;
                newUnitData["rotation"] = unit.Value.GetUnitRotation();
                // Owner
                newUnitData["owner"] = unit.Value.owner;
                // Disabled state
                newUnitData["disabled"] = unit.Value.disabled;

                // If not static object
                if (!unit.Value.staticObject) newUnitData["DO"] = true;
                if (refUnit.animationBlendingIndex != unit.Value.animationBlendingIndex) newUnitData["animationBlendingIndex"] = unit.Value.animationBlendingIndex;

                // Text
                if (refUnit.unitName != unit.Value.unitName) newUnitData["unitName"] = unit.Value.unitName;
                if (refUnit.description != unit.Value.description) newUnitData["description"] = unit.Value.description;
                // no icon

                // General Parameters
                if (refUnit.isSelectable != unit.Value.isSelectable) newUnitData["isSelectable"] = unit.Value.isSelectable;
                if (refUnit.unitType != unit.Value.unitType) newUnitData["unitType"] = (int)unit.Value.unitType;
                if (refUnit.isGround != unit.Value.isGround) newUnitData["isGround"] = unit.Value.isGround;
                if (refUnit.isWater != unit.Value.isWater) newUnitData["isWater"] = unit.Value.isWater;
                if (refUnit.isAir != unit.Value.isAir) newUnitData["isAir"] = unit.Value.isAir;

                // Visuals
                if (refUnit.unitRadius != unit.Value.unitRadius) newUnitData["unitRadius"] = unit.Value.unitRadius;
                if (refUnit.unitHeight != unit.Value.unitHeight) newUnitData["unitHeight"] = unit.Value.unitHeight;
                if (refUnit.crossFadeTime != unit.Value.crossFadeTime) newUnitData["crossFadeTime"] = unit.Value.crossFadeTime;
                // no horizntal vertical part

                // Behaviour
                if (refUnit.doNotLookForTargets != unit.Value.doNotLookForTargets) newUnitData["doNotLookForTargets"] = unit.Value.doNotLookForTargets;
                if (refUnit.visionRange != unit.Value.visionRange) newUnitData["visionRange"] = unit.Value.visionRange;
                if (refUnit.reactionRange != unit.Value.reactionRange) newUnitData["reactionRange"] = unit.Value.reactionRange;
                if (refUnit.transportWeight != unit.Value.transportWeight) newUnitData["transportWeight"] = unit.Value.transportWeight;
                // NO veiwblocker singlecell viewblocker

                // HP
                if (refUnit.health != unit.Value.health) newUnitData["health"] = unit.Value.health;
                if (refUnit.maxHealth != unit.Value.maxHealth) newUnitData["maxHealth"] = unit.Value.maxHealth;
                if (refUnit.healthRegen != unit.Value.healthRegen) newUnitData["healthRegen"] = unit.Value.healthRegen;
                // MP
                if (refUnit.mana != unit.Value.mana) newUnitData["mana"] = unit.Value.mana;
                if (refUnit.maxMana != unit.Value.maxMana) newUnitData["maxMana"] = unit.Value.maxMana;
                if (refUnit.manaRegen != unit.Value.manaRegen) newUnitData["manaRegen"] = unit.Value.manaRegen;
                // Armor
                if (refUnit.armor != unit.Value.armor) newUnitData["armor"] = unit.Value.armor;
                if (refUnit.armorType != null && refUnit.armorType.id != unit.Value.armorType.id) newUnitData["armorType"] = unit.Value.armorType.id;
                if (refUnit.isInvulnerable != unit.Value.isInvulnerable) newUnitData["isInvulnerable"] = unit.Value.isInvulnerable;

                // CanMove
                if (refUnit.canMove != unit.Value.canMove) newUnitData["canMove"] = unit.Value.canMove;
                if (refUnit.moveSpeed != unit.Value.moveSpeed) newUnitData["moveSpeed"] = unit.Value.moveSpeed;
                if (refUnit.acceleration != unit.Value.acceleration) newUnitData["acceleration"] = unit.Value.acceleration;
                if (refUnit.turnSpeed != unit.Value.turnSpeed) newUnitData["turnSpeed"] = unit.Value.turnSpeed;
                if (refUnit.animationMoveSpeed != unit.Value.animationMoveSpeed) newUnitData["animationMoveSpeed"] = unit.Value.animationMoveSpeed;

                // Creation
                if (refUnit.unlockTech != unit.Value.unlockTech) newUnitData["unlockTech"] = unit.Value.unlockTech.id;
                // check production change
                if (refUnit.resourceProduced.Length == unit.Value.resourceProduced.Length)
                {
                    bool change = false;
                    for (int i = 0; i < unit.Value.resourceProduced.Length; i++)
                    {
                        if (refUnit.resourceProduced[i].type != unit.Value.resourceProduced[i].type || refUnit.resourceProduced[i].value != unit.Value.resourceProduced[i].value)
                        {
                            change = true;
                            break;
                        }
                    }

                    if (change) newUnitData["resourceProduced"] = JsonHelper.ToJson(ResourceWrapper.IDArray(unit.Value.resourceProduced));
                }
                else newUnitData["resourceProduced"] = JsonHelper.ToJson(ResourceWrapper.IDArray(unit.Value.resourceProduced));
                // check resource cost change
                if (refUnit.resourceCost.Length == unit.Value.resourceCost.Length)
                {
                    bool change = false;
                    for (int i = 0; i < unit.Value.resourceCost.Length; i++)
                    {
                        if (refUnit.resourceCost[i].type != unit.Value.resourceCost[i].type || refUnit.resourceCost[i].value != unit.Value.resourceCost[i].value)
                        {
                            change = true;
                            break;
                        }
                    }

                    if (change) newUnitData["resourceCost"] = JsonHelper.ToJson(ResourceWrapper.IDArray(unit.Value.resourceCost));
                }
                else newUnitData["resourceCost"] = JsonHelper.ToJson(ResourceWrapper.IDArray(unit.Value.resourceCost));

                // Death
                if (refUnit.xpReward != unit.Value.xpReward) newUnitData["xpReward"] = unit.Value.xpReward;
                // check resource reward change
                if (refUnit.resourceReward.Length == unit.Value.resourceReward.Length)
                {
                    bool change = false;
                    for (int i = 0; i < unit.Value.resourceReward.Length; i++)
                    {
                        if (refUnit.resourceReward[i].type != unit.Value.resourceReward[i].type || refUnit.resourceReward[i].value != unit.Value.resourceReward[i].value)
                        {
                            change = true;
                            break;
                        }
                    }

                    if (change) newUnitData["resourceReward"] = JsonHelper.ToJson(ResourceWrapper.IDArray(unit.Value.resourceReward));
                }
                else newUnitData["resourceReward"] = JsonHelper.ToJson(ResourceWrapper.IDArray(unit.Value.resourceReward));
                // no die vfx

                // Attack
                newUnitData["attackUnitSelector"] = JsonUtility.ToJson(unit.Value.attackUnitSelector);
                if (refUnit.melee != unit.Value.melee) newUnitData["melee"] = unit.Value.melee;
                if (refUnit.attackType != unit.Value.attackType) newUnitData["attackType"] = (int)unit.Value.attackType;
                if (refUnit.attackRange != unit.Value.attackRange) newUnitData["attackRange"] = unit.Value.attackRange;
                if (refUnit.attackDamage != unit.Value.attackDamage) newUnitData["attackDamage"] = unit.Value.attackDamage;
                if (refUnit.attackSpeed != unit.Value.attackSpeed) newUnitData["attackSpeed"] = unit.Value.attackSpeed;
                if (refUnit.damageType != null && refUnit.damageType != unit.Value.damageType) newUnitData["damageType"] = unit.Value.damageType.id;
                // no attack effectors

                // Attack technical
                if (refUnit.projectileGO != unit.Value.projectileGO)
                {
                    // VFXLINE
                    if (unit.Value.attackType == AttackType.Continuous) newUnitData["projectileVFX"] = unit.Value.projectileGO.GetComponent<VFXLine>().id;
                    // PROJECTILE
                    else newUnitData["projectileID"] = unit.Value.projectileGO.GetComponent<Projectile>().id;
                }
                if (refUnit.animationAttackDelay != unit.Value.animationAttackDelay) newUnitData["animationAttackDelay"] = unit.Value.animationAttackDelay;
                // no launch site
                // no launch VFX

                // Periodic attack parameters
                if (refUnit.periodicSequential != unit.Value.periodicSequential) newUnitData["periodicSequential"] = unit.Value.periodicSequential;
                if (refUnit.periodicAttackCount != unit.Value.periodicAttackCount) newUnitData["periodicAttackCount"] = unit.Value.periodicAttackCount;
                if (refUnit.periodicAttackDelay != unit.Value.periodicAttackDelay) newUnitData["periodicAttackDelay"] = unit.Value.periodicAttackDelay;

                // Attack modifiers
                if (refUnit.isSplash != unit.Value.isSplash) newUnitData["isSplash"] = unit.Value.isSplash;
                if (refUnit.splashRadius != unit.Value.splashRadius) newUnitData["splashRadius"] = unit.Value.splashRadius;
                if (refUnit.splashReduction != unit.Value.splashReduction) newUnitData["splashReduction"] = unit.Value.splashReduction;
                if (refUnit.projectileFollowTarget != unit.Value.projectileFollowTarget) newUnitData["projectileFollowTarget"] = unit.Value.projectileFollowTarget;

                if (refUnit.multiTarget != unit.Value.multiTarget) newUnitData["multiTarget"] = unit.Value.multiTarget;
                if (refUnit.multiTargetCount != unit.Value.multiTargetCount) newUnitData["multiTargetCount"] = unit.Value.multiTargetCount;

                if (refUnit.bounceCount != unit.Value.bounceCount) newUnitData["bounceCount"] = unit.Value.bounceCount;
                if (refUnit.bounceRange != unit.Value.bounceRange) newUnitData["bounceRange"] = unit.Value.bounceRange;
                if (refUnit.bounceReduction != unit.Value.bounceReduction) newUnitData["bounceReduction"] = unit.Value.bounceReduction;

                // No sound save

                // --- PASSIVE APPLIED EFFECTS ---
                if (PassiveAppliedEffects.CheckIfApplied(unit.Value.passiveEffects))
                {
                    newUnitData["passiveEffects"] = JsonUtility.ToJson(unit.Value.passiveEffects);
                }

                // Stun state
                if (unit.Value.stunned)
                {
                    newUnitData["stunTime"] = unit.Value.stunTime;
                }

                // Muted state
                if (unit.Value.muted)
                {
                    newUnitData["currentMuteTime"] = unit.Value.currentMuteTime;
                }

                // Disarm state
                if (unit.Value.disarmed)
                {
                    newUnitData["currentDisarmTime"] = unit.Value.currentDisarmTime;
                }

                // Polymorph state
                if (unit.Value.polymorphed)
                {
                    newUnitData["polymorphTime"] = unit.Value.polymorphTime;
                    newUnitData["polymorphAbility"] = unit.Value.polymorphAbility.id;
                    newUnitData["polymorphLvl"] = unit.Value.polymorphLvl;
                    newUnitData["polymorphShape"] = unit.Value.polymorphShape.unitTypeID;
                }

                // If main renderer is different from original one
                if (unit.Value.currentMainShape != null && unit.Value.currentMainShape.unitTypeID != unit.Value.unitTypeID)
                {
                    newUnitData["currentMainShape"] = unit.Value.currentMainShape.unitTypeID;
                }

                // Levelling unit
                if (unit.Value.levelingUnit)
                {
                    newUnitData["levelingUnit"] = true;

                    newUnitData["xp"] = unit.Value.levelingUnit.currentExp;
                    newUnitData["level"] = unit.Value.levelingUnit.level;
                    newUnitData["abilityPoints"] = unit.Value.levelingUnit.abilityPoints;

                    int abilityGlobalIndex = -1;
                    string levelAbilityIndex = "", abilityLevels = "";
                    RecursiveLevelExtract(unit.Value, unit.Value.abilities, ref abilityGlobalIndex, ref levelAbilityIndex, ref abilityLevels);
                    newUnitData["levelAbilityIndex"] = levelAbilityIndex;
                    newUnitData["abilityLevels"] = abilityLevels;
                }

                // Lifetime
                if (unit.Value.lifetimeUnit)
                {
                    newUnitData["lifetime"] = unit.Value.lifetimeUnit.currentLifeSpan;
                }

                // ProcessingUnit
                if (unit.Value.activeProcess != null && unit.Value.activeProcess.Length > 0 && unit.Value.activeProcess[0] != null)
                {
                    newUnitData["processingUnit"] = true;

                    newUnitData["processes"] = "";
                    newUnitData["processLevels"] = "";
                    // Traverse processes
                    for (int i = 0; i < unit.Value.activeProcess.Length; i++)
                    {
                        if (unit.Value.activeProcess[i] == null) break;
                        newUnitData["processes"] += "-" + unit.Value.activeProcess[i].id;
                        newUnitData["processLevels"] += "-" + unit.Value.processLevel[i];
                    }
                }

                // Ability cooldowns
                if (unit.Value.cooldownAbility.Count != 0)
                {
                    string cdAbility = "";
                    string cdIndex = "";

                    // Traverse ability cooldowns
                    for (int i = 0; i < unit.Value.cooldownAbility.Count; i++)
                    {
                        if (unit.Value.cooldownAbilityIsItem[i]) continue; // Item cds Handled in inventory
                        cdAbility += "-" + unit.Value.cooldownAbility[i].ToString("F1", CultureInfo.InvariantCulture);
                        cdIndex += "-" + unit.Value.cooldownAbilityIndex[i];
                    }

                    if (cdAbility != "")
                    {
                        newUnitData["CDAbility"] = cdAbility;
                        newUnitData["CDAbilityIndex"] = cdIndex;
                    }
                }

                // Inventory
                newUnitData["InventorySize"] = unit.Value.InventorySize;
                if (unit.Value.InventorySize != 0)
                {
                    string slotIndex = "";
                    string abilityId = "";
                    string charges = "";
                    string itemCD = "";

                    // Traverse items
                    for (int i = 0; i < unit.Value.InventorySize; i++)
                    {
                        if (unit.Value.items[i] == null) continue;
                        slotIndex += "-" + i;
                        abilityId += "-" + unit.Value.items[i].id;
                        charges += "-" + unit.Value.itemCharges[i];
                        itemCD += "-" + unit.Value.GetAbilityCooldown(i, true).ToString("F1", CultureInfo.InvariantCulture);
                    }

                    if (slotIndex != "")
                    {
                        newUnitData["slotIndex"] = "";
                        newUnitData["abilityId"] = "";
                        newUnitData["charges"] = "";
                        newUnitData["itemCD"] = "";
                    }
                }

                // Every Frame Abilities (Toggle)
                if (unit.Value.everyFrameAbilities.Count > 0)
                {
                    string toggleAbilities = "";
                    string toggleIndex = "";
                    string toggleIsItem = "";
                    for (int i = 0; i < unit.Value.everyFrameAbilities.Count; i++)
                    {
                        if (unit.Value.everyFrameAbilities[i].type == AbilityType.Toggle)
                        {
                            toggleAbilities += "-" + unit.Value.everyFrameAbilities[i].id;
                            toggleIndex += "-" + unit.Value.everyFrameAbilityIndex[i];
                            toggleIsItem += "-" + unit.Value.everyFrameAbilityIsItem[i];
                        }
                    }

                    if (toggleAbilities != "")
                    {
                        newUnitData["toggleAbilities"] = toggleAbilities;
                        newUnitData["toggleIndex"] = toggleIndex;
                        newUnitData["toggleIsItem"] = toggleIsItem;
                    }
                }

                // Transport Unit 
                if (unit.Value.transportUnit && unit.Value.transportUnit.units.Count != 0)
                {
                    newUnitData["transportUnitID"] = "";

                    // Traverse transported units
                    for (int i = 0; i < unit.Value.transportUnit.units.Count; i++)
                    {
                        newUnitData["transportUnitID"] += "-" + unit.Value.transportUnit.units[i].netID;
                    }
                }

                // Construction Unit - If commanded to building, but not reached yet, the data is not saved!
                if (unit.Value.constructionUnit)
                {
                    if (unit.Value.constructionUnit.upgradeBuildingRef)
                    {
                        // Upgrade building
                        newUnitData["upgradeBuilding"] = unit.Value.constructionUnit.upgradeBuildingRef.unitTypeID;
                        newUnitData["upgradeTime"] = unit.Value.constructionUnit.upgradeTime - (unit.Value.constructionUnit.currentConstructionPercentage * unit.Value.constructionUnit.upgradeTime);
                        if (unit.Value.constructionUnit.upgradeCost != null) newUnitData["upgradeCost"] = JsonHelper.ToJson(ResourceWrapper.IDArray(unit.Value.constructionUnit.upgradeCost));
                    }
                    else
                    {
                        // Construction or Worker parameters
                        if (unit.Value.constructionUnit.buildingObj != null) newUnitData["buildingRef"] = unit.Value.constructionUnit.buildingObj.netID; // If worker is working on a building
                        if (unit.Value.isBeingBuilt) newUnitData["currentConstructionPercentage"] = unit.Value.constructionUnit.currentConstructionPercentage;
                    }

                    // Add the data
                    if (unit.Value.constructionUnit.buildingObj != null || unit.Value.isBeingBuilt || unit.Value.constructionUnit.upgradeBuildingRef)
                    {
                        newUnitData["constructionUnit"] = true;
                    }
                }

                // Resource Unit
                if (unit.Value.resourceUnit)
                {
                    newUnitData["resourceUnit"] = true;

                    if (unit.Value.resourceUnit.isCollecting)
                    {
                        if (unit.Value.resourceUnit.isCollector) newUnitData["currentWaitTime"] = unit.Value.resourceUnit.currentWaitTime; // For collector it is currentWaitTime; For collectible - it means isCollecting is true
                        else newUnitData["currentWaitTime"] = 1;
                    }

                    if (unit.Value.resourceUnit.storageObj != null) newUnitData["storageRef"] = unit.Value.resourceUnit.storageObj.netID; // current storage unit that this collector brings resources to
                    if (unit.Value.resourceUnit.collectibleObj != null) newUnitData["collectibleRef"] = unit.Value.resourceUnit.collectibleObj.netID; // current collectible unit that this collector takes resources from  

                    if (unit.Value.resourceUnit.isCollector && unit.Value.resourceUnit.currentHeldResources.Count != 0)
                    {
                        // Collector
                        newUnitData["currentHeldResources"] = "";
                        for (int i = 0; i < unit.Value.resourceUnit.currentHeldResources.Count; i++)
                        {
                            // The amount of resources at the hands of the collector / available resources at collectible
                            newUnitData["currentHeldResources"] += "-" + GameResources.instance.GetResourceID(unit.Value.resourceUnit.currentHeldResources[i]) + ":" + unit.Value.resourceUnit.currentHeldResources[i].value;
                        }
                    }
                    else if (unit.Value.resourceUnit.collectibleResources.Length != 0)
                    {
                        // Collectible
                        newUnitData["currentHeldResources"] = "";
                        for (int i = 0; i < unit.Value.resourceUnit.collectibleResources.Length; i++)
                        {
                            // The amount of resources at the hands of the collector / available resources at collectible
                            newUnitData["currentHeldResources"] += "-" + GameResources.instance.GetResourceID(unit.Value.resourceUnit.collectibleResources[i]) + ":" + unit.Value.resourceUnit.collectibleResources[i].value;
                        }
                    }
                }

                // Effectors
                if (unit.Value.effectors.Count != 0)
                {
                    newUnitData["effectors"] = "";
                    newUnitData["effectorOwner"] = "";

                    for (int i = 0; i < unit.Value.effectors.Count; i++)
                    {
                        // [Interflow fix 2026-08-02 effector-unify] Сила и фактическая длительность наложения:
                        // без них восстановленный эффектор вернулся бы с базовыми числами ассета.
                        // Формат: id:время:сила:длительность (старые записи — только id:время, читаются по-прежнему).
                        newUnitData["effectors"] += "-" + unit.Value.effectors[i].effector.id + ":" + unit.Value.effectors[i].currentTime.ToString("F2", CultureInfo.InvariantCulture)
                                                  + ":" + unit.Value.effectors[i].powerMultiplier.ToString("F3", CultureInfo.InvariantCulture)
                                                  + ":" + unit.Value.effectors[i].duration.ToString("F3", CultureInfo.InvariantCulture);
                        if (unit.Value.effectors[i].unitOwner == null)
                        {
                            newUnitData["effectorOwner"] += "-_" + unit.Value.effectors[i].owner;
                        }
                        else
                        {
                            newUnitData["effectorOwner"] += "-" + unit.Value.effectors[i].unitOwner.netID;
                        }
                    }
                }

                // Drop Item --------------------------------
                if (unit.Value.unitType == UnitType.Item)
                {
                    ItemDropped itd = unit.Value.GetComponent<ItemDropped>();

                    newUnitData["dropID"] = itd.item.id;
                    newUnitData["dropCharges"] = itd.charges;
                    newUnitData["dropCD"] = itd.cooldown;
                }

                // Main State data --------------------------------
                newUnitData["stateIndex"] = (int)unit.Value.unitState; // Idle move to origin
                // For idle initial position, Move current destination, Attack target location, AttackMove target location
                if (unit.Value.unitState == UnitStates.Idle)
                {
                    if (unit.Value.target != null)
                    {
                        newUnitData["initialPosition"] = unit.Value.initialPosition;
                        newUnitData["targetPosition"] = unit.Value.targetPosition;
                    }
                }
                else if (unit.Value.unitState == UnitStates.Move)
                {
                    newUnitData["stopDistance"] = unit.Value.stopDistance;
                    if (unit.Value.isAir) newUnitData["targetPosition"] = new Vector2(unit.Value.currentDestination.x - Utils.airOffsetX, unit.Value.currentDestination.z);
                    else if (unit.Value.isInvisible && unit.Value.invisibleAgent) newUnitData["targetPosition"] = new Vector2(unit.Value.currentDestination.x, unit.Value.currentDestination.z - Utils.invisibilityOffsetY);
                    else newUnitData["targetPosition"] = new Vector2(unit.Value.currentDestination.x, unit.Value.currentDestination.z);
                }
                else if (unit.Value.unitState == UnitStates.Attack && unit.Value.isTargetGround)
                {
                    newUnitData["targetPosition"] = unit.Value.targetPosition;
                }
                else if (unit.Value.unitState == UnitStates.AttackMove)
                {
                    newUnitData["targetPosition"] = unit.Value.targetPosition;
                }
                if (unit.Value.target != null) newUnitData["stateTargetID"] = unit.Value.target.netID; // For Attack target, for Follow target

                // Activea attack data --------------------------------
                if (!unit.Value.firstAttack)
                {
                    newUnitData["firstAttack"] = false; // Indicate that in active attack state
                    newUnitData["currentAttackCount"] = unit.Value.currentAttackCount;
                    newUnitData["attackCooldown"] = unit.Value.attackCooldown;
                    newUnitData["currentAttackSpeed"] = unit.Value.currentAttackSpeed;
                    newUnitData["netCD"] = unit.Value.netCD;

                    if (unit.Value.multiTarget)
                    {
                        string additionalTargets = "";

                        for (int i = 0; i < unit.Value.additionalTargets.Length; i++)
                        {
                            if (unit.Value.additionalTargets[i] == null) additionalTargets += "-0";
                            else additionalTargets += "-" + unit.Value.additionalTargets[i].netID;
                        }

                        newUnitData["additionalTargets"] = additionalTargets;
                    }
                }

                // Active ability --------------------------------
                if (unit.Value.activeAbilityInUse)
                {
                    var activeAbility = new JSONObject();

                    newUnitData["abilityID"] = unit.Value.activeAbility.id;
                    newUnitData["abilityLevel"] = unit.Value.activeAbilityLevel;
                    if (unit.Value.activeAbilityUnit != null) newUnitData["targetID"] = unit.Value.activeAbilityUnit.netID;
                    newUnitData["targetLocation"] = unit.Value.activeAbilityLocation;
                    newUnitData["currentActionTime"] = unit.Value.currentActionTime;

                    newUnitData["activeAbility"].Add(activeAbility);
                }

                // Add json
                unitData[u.ToString()].Add(newUnitData);
                u++;
            }

            return unitData.ToString();
        }

        public static void LoadUnitData(string unitDataJson)
        {
            JSONNode unitDatas = JSON.Parse(unitDataJson);
            Unit[] units = new Unit[unitDatas.Count];

            // Assign netID in first iteration
            int u = 0;
            foreach (JSONNode node in unitDatas)
            {
                JSONNode unitData = node[0];

                // Instantiate units by their type
                units[u] = Instantiate(GameManager.instance.gameUnits[unitData["unitTypeID"].AsInt], unitData["position"], Quaternion.identity);
                units[u].SetOwnership(unitData["owner"].AsInt);
                SlotManager.instance.AssignNetID(units[u], unitData["netID"].AsUShort);
                SlotManager.instance.OnGameStart += units[u].StartCallback;

                // --- DROP ITEM ---
                if (unitData["dropID"] != null)
                {
                    ItemDropped itd = units[u].GetComponent<ItemDropped>();
                    itd.item = GameManager.instance.gameAbilities[unitData["dropID"]];
                    itd.charges = unitData["dropCharges"];
                    itd.cooldown = unitData["dropCD"];
                }

                // --- CONSTRUCTION UNIT ---
                units[u].constructionUnit = units[u].GetComponent<ConstructionUnit>();
                if (unitData["constructionUnit"] != null && unitData["currentConstructionPercentage"] != null)
                {
                    // Under construction
                    units[u].constructionUnit.completed = false;
                    units[u].isBeingBuilt = true;
                    units[u].constructionUnit.currentConstructionPercentage = unitData["currentConstructionPercentage"];
                }
                else if (units[u].constructionUnit)
                {
                    // Finished
                    units[u].constructionUnit.completed = true;
                    units[u].isBeingBuilt = false;
                }

                u++;
            }

            if (NetworkConnectionHandler.instance.connectionStage == 2)
            {
                // Joining mid-game: Clients only
                SlotManager.instance.OnGameStart?.Invoke();
                SetUnitData(unitDatas);
            }
            else
            {
                // If loading the save before game start
                SaveManager.sceneUnitData = unitDataJson;
            }
        }

        // This data is set after units are initialized
        public static void SetUnitData(JSONNode unitDatas = null)
        {
            if (unitDatas == null) unitDatas = JSON.Parse(SaveManager.sceneUnitData);

            // Assign all corresponding data in second iteration
            foreach (JSONNode node in unitDatas)
            {
                JSONNode unitData = node[0];
                Unit u = SlotManager.instance.unitNetID[unitData["netID"].AsUShort];

                // Disabled state
                if (unitData["disabled"] == true) u.Disable();

                // Transform - Set rotation
                u.SetUnitRotation(unitData["rotation"].AsFloat);

                // Levelling unit
                if (unitData["levelingUnit"] != null)
                {
                    u.levelingUnit = u.GetComponent<LevelingUnit>();
                    u.levelingUnit.SetLevel(unitData["level"], unitData["xp"], unitData["abilityPoints"], true, false, true);

                    // Ability level decode
                    string indices = unitData["levelAbilityIndex"];
                    string levels = unitData["abilityLevels"];

                    string[] index = indices.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    string[] level = levels.Split('-', StringSplitOptions.RemoveEmptyEntries);

                    // Update ability levels
                    for (int i = 0; i < index.Length; i++)
                    {
                        u.abilityLevel[int.Parse(index[i])] = int.Parse(level[i]);
                    }
                }

                // Lifetime
                if (unitData["lifetime"] != null)
                {
                    u.lifetimeUnit = u.GetComponent<LifetimeUnit>();
                    u.lifetimeUnit.SetLifetime(unitData["lifetime"].AsFloat);
                }

                // Processes
                if (unitData["processingUnit"] != null)
                {
                    string processes = unitData["processes"];
                    string levels = unitData["processLevels"];
                    // Traverse processes
                    string[] process = processes.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    string[] level = levels.Split('-', StringSplitOptions.RemoveEmptyEntries);

                    for (int i = 0; i < process.Length; i++)
                    {
                        // Add process
                        u.activeProcess[i] = GameManager.instance.gameAbilities[int.Parse(process[i])];
                        u.processLevel[i] = int.Parse(level[i]);
                    }

                    u.OnProcessUpdate?.Invoke();
                }

                // --- ABILITY COOLDOWNS ---
                u.cooldownAbility.Clear();
                u.cooldownAbilityIndex.Clear();
                u.cooldownAbilityIsItem.Clear();
                if (unitData["CDAbility"] != null)
                {
                    string cds = unitData["CDAbility"];
                    string indices = unitData["CDAbilityIndex"];

                    string[] cd = cds.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    string[] index = indices.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    // Add cooldowns
                    for (int i = 0; i < index.Length; i++)
                    {
                        u.ChangeAbilityCooldown(float.Parse(cd[i], CultureInfo.InvariantCulture), int.Parse(index[i]), false, true);
                    }
                }
                u.OnRedrawAbilityView?.Invoke();

                // --- INVENTORY ---

                // Match inventory size
                // Inventory has been initialized, remove items if needed
                if (u.items.Length > unitData["InventorySize"])
                {
                    // Inventory is more than needed, shrink it and remove items from shrinked slots
                    for (int i = unitData["InventorySize"]; i < u.items.Length; i++)
                    {
                        if (u.items[i] != null) u.RemoveItem(i, true);
                    }
                }
                // Resize inventory
                u.InventorySize = unitData["InventorySize"];
                if (u.items.Length != unitData["InventorySize"]) Array.Resize(ref u.items, unitData["InventorySize"]);
                // Match item charges size
                u.itemCharges = new int[unitData["InventorySize"]];

                // Handle items
                if (unitData["slotIndex"] != null)
                {
                    string slotIndex = unitData["slotIndex"];
                    string abilityId = unitData["abilityId"];
                    string charge = unitData["charges"];
                    string itemCD = unitData["itemCD"];
                    // Traverse inventory
                    string[] slots = slotIndex.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    string[] items = abilityId.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    string[] charges = charge.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    string[] cooldown = itemCD.Split('-', StringSplitOptions.RemoveEmptyEntries);

                    // Add items
                    int slot = 0; // current slot index
                    for (int i = 0; i < unitData["InventorySize"]; i++)
                    {
                        // Current slot should be empty, but if it is not we remove the item
                        if (slot >= slots.Length || int.Parse(slots[slot]) != i)
                        {
                            if (u.items[i] != null)
                            {
                                if (u.inventoryInitialized) u.RemoveItem(i, true, true);
                                else u.items[i] = null;
                            }
                            continue;
                        }

                        Ability itemAtSlot = GameManager.instance.gameAbilities[int.Parse(items[slot])];

                        // Check if item is the same
                        if (u.items[i] != itemAtSlot)
                        {
                            // Remove existing item
                            if (u.inventoryInitialized && u.items[i] != null) u.RemoveItem(i, true, true);
                            // Add new item
                            u.items[i] = itemAtSlot;
                            itemAtSlot.Unlock(u, u.owner, 0);
                        }

                        // Change charges and cooldown
                        u.itemCharges[i] = int.Parse(charges[slot]);
                        if (float.Parse(cooldown[slot], CultureInfo.InvariantCulture) > 0) u.ChangeAbilityCooldown(float.Parse(cooldown[slot], CultureInfo.InvariantCulture), i, true, true);
                        else if (u.inventoryInitialized) u.ChangeAbilityCooldown(-1, i, true, true);

                        slot++;
                    }

                    u.inventoryInitialized = true;
                    u.OnRedrawAbilityView?.Invoke();
                }
                else if (unitData["InventorySize"] > 0)
                {
                    // Remove all items
                    for (int i = 0; i < unitData["InventorySize"]; i++)
                    {
                        if (u.items[i] != null)
                        {
                            if (u.inventoryInitialized) u.RemoveItem(i, true, true);
                            else u.items[i] = null;
                        }
                    }
                }

                // --- EVERY FRAME ABILITIES (TOGGLE) ---
                if (unitData["toggleAbilities"] != null)
                {
                    string toggleAbilities = unitData["toggleAbilities"];
                    string toggleIndex = unitData["toggleIndex"];
                    string toggleIsItem = unitData["toggleIsItem"];

                    string[] ability = toggleAbilities.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    string[] index = toggleIndex.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    string[] isItem = toggleIsItem.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    // Add Toggle Abilities
                    for (int i = 0; i < index.Length; i++)
                    {
                        Ability toggleAbility = GameManager.instance.gameAbilities[int.Parse(ability[i])];
                        u.UseToggleAbility_Internal(toggleAbility, int.Parse(index[i]), bool.Parse(isItem[i]));
                    }
                }

                // --- TRANSPORT UNIT ---
                if (unitData["transportUnitID"] != null)
                {
                    string transportUnitID = unitData["transportUnitID"];

                    u.transportUnit = u.GetComponent<TransportUnit>();
                    string[] index = transportUnitID.Split('-', StringSplitOptions.RemoveEmptyEntries);

                    // Add transported units
                    for (int i = 0; i < index.Length; i++)
                    {
                        ushort transportedUnitID = Convert.ToUInt16(index[i]);
                        Unit transportedUnit = (transportedUnitID != 0) ? SlotManager.instance.unitNetID[transportedUnitID] : null;
                        if (transportedUnit == null) continue; // Means something is wrong, transported unit does not exist on client

                        u.transportUnit.units.Add(transportedUnit);
                        u.transportUnit.currentCapacity += transportedUnit.transportWeight;
                    }
                    u.transportUnit.transportChange?.Invoke();
                }

                // --- CONSTRUCTION UNIT ---
                if (unitData["constructionUnit"] != null)
                {
                    u.constructionUnit = u.GetComponent<ConstructionUnit>();

                    if (unitData["upgradeBuilding"] != null)
                    {
                        // UpgradeBuilding
                        ResourceWrapper[] cost = null;
                        if (unitData["upgradeCost"] != null) cost = ResourceWrapper.TypeArray(JsonHelper.FromJson<ResourceWrapperID>(unitData["upgradeCost"]));

                        u.constructionUnit.UpgradeBuilding(GameManager.instance.gameUnits[unitData["upgradeBuilding"]], unitData["upgradeTime"], cost);
                    }
                    else
                    {
                        // Builder
                        if (unitData["buildingRef"] != null)
                        {
                            // Builder starts working
                            u.constructionUnit.buildingObj = SlotManager.instance.unitNetID[unitData["buildingRef"].AsUShort];
                            u.constructionUnit.buildingObj.constructionUnit.buildersWorking++;
                            u.constructionUnit.isWorking = true;
                            u.AnimatorSetBool(AnimationState.Building, true);
                        }

                        // Completion
                        if (unitData["currentConstructionPercentage"] != null)
                        {
                            // Under construction
                            u.constructionUnit.completed = false;
                            u.isBeingBuilt = true;
                            u.constructionUnit.currentConstructionPercentage = unitData["currentConstructionPercentage"];
                            u.AnimatorSetBool(AnimationState.Idle, false);
                            if (u.animator && u.constructionUnit.constructionAnimExists) u.animator.Play("construction", 0, u.constructionUnit.currentConstructionPercentage);
                        }
                        else
                        {
                            // Finished
                            u.constructionUnit.completed = true;
                            u.isBeingBuilt = false;
                        }
                    }
                }

                // --- RESOURCE UNIT ---
                if (unitData["resourceUnit"] != null)
                {
                    u.resourceUnit = u.GetComponent<ResourceUnit>();

                    if (unitData["currentWaitTime"] == null) u.resourceUnit.isCollecting = false;
                    else
                    {
                        if (u.resourceUnit.isCollector) u.resourceUnit.currentWaitTime = unitData["currentWaitTime"];
                        u.resourceUnit.isCollecting = true;
                    }

                    if (unitData["storageRef"] != null) u.resourceUnit.storageObj = SlotManager.instance.unitNetID[unitData["storageRef"].AsUShort]; // current storage unit that this collector brings resources to
                    if (unitData["collectibleRef"] != null)
                    {
                        u.resourceUnit.collectibleObj = SlotManager.instance.unitNetID[unitData["collectibleRef"].AsUShort]; // current collectible unit that this collector takes resources from
                        if (!NetworkConnectionHandler.isClient) u.resourceUnit.collectibleObj.OnDie += u.resourceUnit.CollectibleDied;
                    }

                    if (unitData["currentHeldResources"] != null)
                    {
                        string resources = unitData["currentHeldResources"];
                        string[] currentHeldResources = resources.Split('-', StringSplitOptions.RemoveEmptyEntries);

                        if (currentHeldResources[0] != "")
                        {
                            for (int i = 0; i < currentHeldResources.Length; i++)
                            {
                                // The amount of resources at the hands of the collector / available resources at collectible
                                string[] typeValue = currentHeldResources[i].Split(':');

                                if (u.resourceUnit.isCollector)
                                {
                                    u.resourceUnit.AddCurrentHeldResources(GameResources.instance.gameResources[int.Parse(typeValue[0])].type, int.Parse(typeValue[1])); // NETWORK - resources must be cleared first
                                }
                                else
                                {
                                    int resourceIndex = u.resourceUnit.GetResourceIndex(GameResources.instance.gameResources[int.Parse(typeValue[0])].type);
                                    if (resourceIndex == -1) Debug.LogWarning("Desync issue! Resource is not found in the scene. Probably it was changed. Collectible: " + u.unitName);
                                    else
                                    {
                                        u.resourceUnit.collectibleResources[resourceIndex].value = int.Parse(typeValue[1]);
                                    }
                                }
                            }
                        }
                    }
                }

                // --- EFFECTORS ---
                if (unitData["effectors"] != null)
                {
                    string effectors = unitData["effectors"];
                    string effectorOwner = unitData["effectorOwner"];

                    string[] eff = effectors.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    string[] owner = effectorOwner.Split('-', StringSplitOptions.RemoveEmptyEntries);

                    // Add cooldowns
                    for (int i = 0; i < eff.Length; i++)
                    {
                        string[] idCurrentTime = eff[i].Split(':');

                        // [Interflow fix 2026-08-02 effector-unify] Сила и длительность появились 2026-08-02.
                        // В записях до этой даты их нет — тогда берём то, что в ассете (как и было).
                        float savedPower = idCurrentTime.Length > 2 ? float.Parse(idCurrentTime[2], CultureInfo.InvariantCulture) : 1f;
                        float savedDuration = idCurrentTime.Length > 3 ? float.Parse(idCurrentTime[3], CultureInfo.InvariantCulture) : -1f;

                        // If starts with "_" no owner unit
                        if (owner[i][0] == '_')
                        {
                            // no owner unit
                            Effector.EffectorAdd(u, GameManager.instance.gameEffectors[int.Parse(idCurrentTime[0])], null, int.Parse(owner[i].Substring(1)), float.Parse(idCurrentTime[1], CultureInfo.InvariantCulture), savedPower, savedDuration);
                        }
                        else
                        {
                            // owner unit
                            Unit ownerUnit = SlotManager.instance.unitNetID[UInt16.Parse(owner[i])];
                            Effector.EffectorAdd(u, GameManager.instance.gameEffectors[int.Parse(idCurrentTime[0])], ownerUnit, ownerUnit.owner, float.Parse(idCurrentTime[1], CultureInfo.InvariantCulture), savedPower, savedDuration);
                        }
                    }
                }

                // --- STATE ---
                Unit targetUnit = (unitData["stateTargetID"] != null) ? SlotManager.instance.unitNetID[unitData["stateTargetID"].AsUShort] : null;
                Vector2 targetPosition = (unitData["targetPosition"] != null) ? unitData["targetPosition"] : Vector2.zero;

                if ((UnitStates)unitData["stateIndex"].AsInt == UnitStates.Idle)
                {
                    u.unitState = UnitStates.Idle;
                    bool doNotLookForTargets = (unitData["doNotLookForTargets"] != null) ? unitData["doNotLookForTargets"].AsBool : u.doNotLookForTargets;
                    if (!doNotLookForTargets)
                    {
                        Vector2 initialPosition = (unitData["initialPosition"] != null) ? unitData["initialPosition"] : Vector2.zero;

                        if (unitData["firstAttack"] != null)
                        {
                            // Actively attacking
                            u.target = targetUnit;
                            u.target.OnReferenceChange += u.TargetReferenceChange;
                            if (!NetworkConnectionHandler.isClient)
                            {
                                if (targetPosition != Vector2.zero) u.targetPosition = targetPosition;
                                if (initialPosition != Vector2.zero) u.initialPosition = initialPosition;
                                else u.initialPosition = new Vector2(u.transform.position.x, u.transform.position.z);
                            }
                        }
                        else
                        {
                            // If initial position exists
                            if (!NetworkConnectionHandler.isClient)
                            {
                                if (targetUnit != null)
                                {
                                    // Following target, set it
                                    u.target = targetUnit;
                                    u.target.OnReferenceChange += u.TargetReferenceChange;
                                    if (targetPosition != Vector2.zero) u.targetPosition = targetPosition;
                                    else u.targetPosition = new Vector2(targetUnit.transform.position.x, targetUnit.transform.position.z);
                                }
                                else if (initialPosition != Vector2.zero)
                                {
                                    // No target, go to initial position if exists
                                    u.Move(initialPosition);
                                }
                            }
                        }
                    }
                }
                else if ((UnitStates)unitData["stateIndex"].AsInt == UnitStates.Hold)
                {
                    // HOLD - For server only
                    if (!NetworkConnectionHandler.isClient) u.unitState = UnitStates.Hold;
                }
                else if ((UnitStates)unitData["stateIndex"].AsInt == UnitStates.Follow)
                {
                    // FOLLOW - For server only
                    if (!NetworkConnectionHandler.isClient)
                    {
                        u.unitState = UnitStates.Follow;
                        u.target = targetUnit;
                        u.target.OnReferenceChange += u.TargetReferenceChange;
                    }
                }
                else if ((UnitStates)unitData["stateIndex"].AsInt == UnitStates.Move)
                {
                    // MOVE - For server only No stop distance
                    if (!NetworkConnectionHandler.isClient)
                    {
                        u.unitState = UnitStates.Move;
                        u.SetDestination(targetPosition, true, unitData["stopDistance"]);
                    }
                }
                else if ((UnitStates)unitData["stateIndex"].AsInt == UnitStates.Attack)
                {
                    // ATTACK
                    if (unitData["firstAttack"] != null)
                    {
                        // We are actively attacking the target, set the params
                        if (!NetworkConnectionHandler.isClient)
                        {
                            // Server
                            u.unitState = UnitStates.Attack;
                            u.target = targetUnit;
                            if (targetUnit != null) u.target.OnReferenceChange += u.TargetReferenceChange;
                            if (targetPosition != Vector2.zero)
                            {
                                u.target = null;
                                u.isTargetGround = true;
                                u.targetPosition = targetPosition;
                            }
                        }
                        else
                        {
                            // Clients
                            u.target = targetUnit;
                            if (targetUnit != null) u.target.OnReferenceChange += u.TargetReferenceChange;
                            if (targetPosition != Vector2.zero) u.targetPosition = targetPosition;
                        }
                    }
                    else
                    {
                        // We are still to reach the target, just set the command. Only for server
                        if (!NetworkConnectionHandler.isClient)
                        {
                            if (targetUnit) u.Attack(targetUnit);
                            else u.Attack(targetPosition);
                        }
                    }
                }
                else if ((UnitStates)unitData["stateIndex"].AsInt == UnitStates.AttackMove)
                {
                    // ATTACK MOVE
                    if (unitData["firstAttack"] != null)
                    {
                        // We are actively attacking the target, set the params 
                        u.target = targetUnit;
                        u.target.OnReferenceChange += u.TargetReferenceChange;

                        if (!NetworkConnectionHandler.isClient)
                        {
                            u.unitState = UnitStates.AttackMove;
                            u.targetPosition = targetPosition;
                            u.isTargetGround = false;
                        }
                    }
                    else if (!NetworkConnectionHandler.isClient)
                    {
                        // We are still to reach the target, just set the command. Only for server
                        u.AttackMove(targetPosition);
                    }
                }

                // --- ACTIVE ATTACK ---
                bool targetsInitialized = false;
                if (unitData["firstAttack"] != null)
                {
                    u.currentAttackCount = unitData["currentAttackCount"];
                    u.attackCooldown = unitData["attackCooldown"];
                    u.currentAttackSpeed = unitData["currentAttackSpeed"];
                    u.netCD = unitData["netCD"];

                    if (unitData["additionalTargets"] != null)
                    {
                        targetsInitialized = true;
                        string targetsJson = unitData["additionalTargets"];
                        string[] targets = targetsJson.Split('-', StringSplitOptions.RemoveEmptyEntries);

                        u.additionalTargets = new Unit[targets.Length];
                        for (int i = 0; i < targets.Length; i++)
                        {
                            ushort netID = Convert.ToUInt16(targets[i]);

                            if (netID != 0)
                            {
                                u.additionalTargets[i] = SlotManager.instance.unitNetID[netID];
                            }
                        }
                    }
                }

                // --- ACTIVE ABILITY ---
                if (unitData["activeAbility"] != null)
                {
                    // Set active ability
                    u.SetActiveAbility(
                        GameManager.instance.gameAbilities[unitData["abilityID"]],
                        unitData["abilityLevel"],
                        (unitData["targetID"] != null) ? SlotManager.instance.unitNetID[unitData["targetID"].AsUShort] : null,
                        unitData["targetLocation"],
                        unitData["currentActionTime"]);
                }

                // --- STUN STATE ---
                if (unitData["stunTime"] != null)
                {
                    u.Stun(unitData["stunTime"].AsFloat);
                }

                // --- MUTED STATE ---
                if (unitData["currentMuteTime"] != null)
                {
                    float muteTime = unitData["currentMuteTime"].AsFloat;

                    if (muteTime == 0) u.muted = true;
                    else u.Mute(muteTime);
                }

                // --- DISARM STATE ---
                if (unitData["currentDisarmTime"] != null)
                {
                    float disarmTime = unitData["currentDisarmTime"].AsFloat;

                    if (disarmTime == 0) u.disarmed = true;
                    else u.Disarm(disarmTime);
                }

                // --- POLYMORPH STATE ---
                if (unitData["currentMainShape"] != null)
                {
                    u.ReplaceRenderers(GameManager.instance.gameUnits[unitData["currentMainShape"]], true);
                }

                if (unitData["polymorphTime"] != null)
                {
                    u.Polymorph(GameManager.instance.gameAbilities[unitData["polymorphAbility"]], unitData["polymorphLvl"], unitData["polymorphTime"], GameManager.instance.gameUnits[unitData["polymorphShape"]]);
                }

                // --- INVULNERABILITY STATE ---
                if (unitData["isInvulnerable"] != null) u.isInvulnerable = unitData["isInvulnerable"];
                else u.isInvulnerable = false;

                // --- PASSIVE APPLIED EFFECTS ---
                if (unitData["passiveEffects"] != null)
                {
                    u.passiveEffects = JsonUtility.FromJson<PassiveAppliedEffects>(unitData["passiveEffects"]);
                }

                //  --- PARAMETERS---

                // If not static object
                if (unitData["DO"] == null) u.staticObject = true;

                // Text
                if (unitData["unitName"] != null) u.unitName = unitData["unitName"];
                if (unitData["description"] != null) u.description = unitData["description"];
                // no icon

                // General Parameters
                if (unitData["isSelectable"] != null) u.isSelectable = unitData["isSelectable"];
                if (unitData["unitType"] != null) u.unitType = (UnitType)((int)unitData["unitType"]);
                if (unitData["isGround"] != null) u.isGround = unitData["isGround"];
                if (unitData["isWater"] != null) u.isWater = unitData["isWater"];
                if (unitData["isAir"] != null) u.isAir = unitData["isAir"];

                // Visuals
                if (unitData["unitRadius"] != null) u.unitRadius = unitData["unitRadius"];
                if (unitData["unitHeight"] != null) u.unitHeight = unitData["unitHeight"];
                if (unitData["crossFadeTime"] != null) u.crossFadeTime = unitData["crossFadeTime"];
                // no horizntal vertical part

                // Behaviour
                if (unitData["doNotLookForTargets"] != null) u.doNotLookForTargets = unitData["doNotLookForTargets"];
                if (unitData["visionRange"] != null) u.visionRange = unitData["visionRange"];
                if (unitData["reactionRange"] != null) u.reactionRange = unitData["reactionRange"];
                if (unitData["transportWeight"] != null) u.transportWeight = unitData["transportWeight"];
                // NO veiwblocker singlecell viewblocker

                // HP
                if (unitData["health"] != null) u.health = unitData["health"];
                if (unitData["maxHealth"] != null) u.maxHealth = unitData["maxHealth"];
                if (unitData["healthRegen"] != null) u.healthRegen = unitData["healthRegen"];
                // MP
                if (unitData["mana"] != null) u.mana = unitData["mana"];
                if (unitData["maxMana"] != null) u.maxMana = unitData["maxMana"];
                if (unitData["manaRegen"] != null) u.manaRegen = unitData["manaRegen"];
                // Armor
                if (unitData["armor"] != null) u.armor = unitData["armor"];
                if (unitData["armorType"] != null) u.armorType = GameManager.instance.armorTypes[unitData["armorType"]];
                if (unitData["isInvulnerable"] != null) u.isInvulnerable = unitData["isInvulnerable"];

                // CanMove
                if (unitData["canMove"] != null) u.canMove = unitData["canMove"];
                if (unitData["moveSpeed"] != null) u.moveSpeed = unitData["moveSpeed"];
                if (unitData["acceleration"] != null) u.acceleration = unitData["acceleration"];
                if (unitData["turnSpeed"] != null) u.turnSpeed = unitData["turnSpeed"];
                if (unitData["animationMoveSpeed"] != null) u.animationMoveSpeed = unitData["animationMoveSpeed"];
                u.ChangeMoveSpeedAnimationSpeed();

                // Creation
                if (unitData["unlockTech"] != null) u.unlockTech = TechnologyManager.instance.GetTechByID(unitData["unlockTech"]);
                if (unitData["resourceProduced"] != null) u.resourceProduced = ResourceWrapper.TypeArray(JsonHelper.FromJson<ResourceWrapperID>(unitData["resourceProduced"]));
                if (unitData["resourceCost"] != null) u.resourceCost = ResourceWrapper.TypeArray(JsonHelper.FromJson<ResourceWrapperID>(unitData["resourceCost"]));

                // Death
                if (unitData["xpReward"] != null) u.xpReward = unitData["xpReward"];
                if (unitData["resourceReward"] != null) u.resourceReward = ResourceWrapper.TypeArray(JsonHelper.FromJson<ResourceWrapperID>(unitData["resourceReward"]));
                // no die vfx

                // Attack
                if (unitData["attackUnitSelector"] != null) u.attackUnitSelector = JsonUtility.FromJson<UnitSelector>(unitData["attackUnitSelector"]);
                if (unitData["melee"] != null) u.melee = unitData["melee"];
                if (unitData["attackType"] != null) u.attackType = (AttackType)((int)unitData["attackType"]);
                if (unitData["attackRange"] != null) u.attackRange = unitData["attackRange"];
                if (unitData["attackDamage"] != null) u.attackDamage = unitData["attackDamage"];
                if (unitData["attackSpeed"] != null) u.attackSpeed = unitData["attackSpeed"];
                if (unitData["damageType"] != null) u.damageType = GameManager.instance.damageTypes[unitData["damageType"].AsInt];
                // no attack effectors

                // Attack technical
                if (unitData["projectileVFX"] != null) u.projectileGO = GameManager.instance.gameVFXLines[unitData["projectileVFX"]].gameObject;
                else if (unitData["projectileID"] != null) u.projectileGO = GameManager.instance.gameProjectiles[unitData["projectileID"]].gameObject;
                if (unitData["animationAttackDelay"] != null) u.animationAttackDelay = unitData["animationAttackDelay"];
                // no launch site
                // no launch VFX

                // Periodic attack parameters
                if (unitData["periodicSequential"] != null) u.periodicSequential = unitData["periodicSequential"];
                if (unitData["periodicAttackCount"] != null) u.periodicAttackCount = unitData["periodicAttackCount"];
                if (unitData["periodicAttackDelay"] != null) u.periodicAttackDelay = unitData["periodicAttackDelay"];

                // Attack modifiers
                if (unitData["isSplash"] != null) u.isSplash = unitData["isSplash"];
                if (unitData["splashRadius"] != null) u.splashRadius = unitData["splashRadius"];
                if (unitData["splashReduction"] != null) u.splashReduction = unitData["splashReduction"];
                if (unitData["projectileFollowTarget"] != null) u.projectileFollowTarget = unitData["projectileFollowTarget"];

                if (unitData["multiTarget"] != null) u.multiTarget = unitData["multiTarget"];
                if (unitData["multiTargetCount"] != null) u.multiTargetCount = unitData["multiTargetCount"];

                if (unitData["bounceCount"] != null) u.bounceCount = unitData["bounceCount"];
                if (unitData["bounceRange"] != null) u.bounceRange = unitData["bounceRange"];
                if (unitData["bounceReduction"] != null) u.bounceReduction = unitData["bounceReduction"];

                // Initialize attack
                u.AttackSelectorInitialize();
                if (!targetsInitialized && u.multiTarget) u.additionalTargets = new Unit[u.multiTargetCount + 1];

                if (u.canAttack && !u.melee)
                {
                    // Assign default projectile/continuousVFX
                    if (u.projectileGO == null)
                    {
                        if (u.attackType == AttackType.Continuous) u.projectileGO = ReferenceManager.instance.defaultContinuousVFX.gameObject;
                        else u.projectileGO = ReferenceManager.instance.defaultProjectile.gameObject;
                    }
                    // Assign internal variables for projectile/continuousVFX
                    if (u.attackType == AttackType.Continuous)
                    {
                        // If attack type is continuous we instantiate attackVFX at launchSite(s)
                        if (u.projectileGO.GetComponent<VFXLine>())
                        {
                            if (u.attackVFXLine != null) DestroyImmediate(u.attackVFXLine.gameObject);
                            u.attackVFXLine = VFXLine.CreateVFX(u.projectileGO.GetComponent<VFXLine>(), u);
                        }
                    }
                    else u.projectileVFX = u.projectileGO.GetComponent<Projectile>();
                }

                u.ChangeAttackAnimationSpeed();

                // No sound save

                if (unitData["animationBlendingIndex"] != null) u.SetAnimationBlendingIndex(unitData["animationBlendingIndex"]);

            }
        }
    }
}
