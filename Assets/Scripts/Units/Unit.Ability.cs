using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    // This script is responsible for handling of the unit`s abilities
    public partial class Unit
    {
        [Header("Abilities")]
        [Tooltip("Abilities of this unit, you must define all abilities here. You will not be able to change them during the game.")]
        public Ability[] abilities;
        [Tooltip("Can be left empty; only for HeroLevelable abilities.\nFor Hero-Levelable abilities, this sets the initial level, but the ability must also meet its requirements (if provided).\nNon-Hero-Levelable abilities start at level 0 and automatically level up when requirements are met.\r\nAn ability level of -1 means the ability is levelable but has not been learned yet.")]
        public int[] abilityLevel;

        [HideInInspector] public bool[] abilityLocked; // Defined automatically based on the technologies available for the player

        [HideInInspector] public List<Ability> everyFrameAbilities = new List<Ability>();  // Index of Aura, Toggle abilities that are played every frame
        [HideInInspector] public List<int> everyFrameAbilityIndex = new List<int>(); // Index of Aura, Toggle abilities that are played every frame
        [HideInInspector] public List<bool> everyFrameAbilityIsItem = new List<bool>(); // Defines if everyFrameAbility[i] and item

        [HideInInspector] public List<float> cooldownAbility = new List<float>(); // Current cooldown of an ability
        [HideInInspector] public List<int> cooldownAbilityIndex = new List<int>(); // abilityCDIndex[index] = reference to ability[]
        [HideInInspector] public List<bool> cooldownAbilityIsItem = new List<bool>(); // If ability at index is item

        // Active ability technical variables. Active ability is an continous ability that is performed over time, for example draining the HP over period of time.
        [HideInInspector] public Ability activeAbility; // Continuous ability that is being used by this unit
        [HideInInspector] public int activeAbilityLevel; // Level of the ability when it was started
        [HideInInspector] public int activeAbilityIndex; // Index of continuous ability that is being used by this unit
        [HideInInspector] public bool activeAbilityItem; // If current active ability is an item. DO NOT FORGET TO CHANGE THE INDEX IF ITEM MOVES ITS SLOT
        [HideInInspector] public Unit activeAbilityUnit; // If active ability requires unit
        [HideInInspector] public Vector3 activeAbilityLocation; // If active ability requires location
        [HideInInspector] public bool activeAbilityInUse; // If active ability is being used right now
        [HideInInspector] public float activeAbilityRange; // Range of active ability
        [HideInInspector] public float activeAbilityCastTime; // Cast timme of active ability
        [HideInInspector] public float activeAbilityDuration; // If this active ability has a duration
        [HideInInspector] public VFXReferencer activeAbilityVFX; // VFX of actively being used ability. Set by abilities` Activate/Deactivate

        private bool playCast = false; // To know if we are currently casting an ability
        private bool sendToClients = false; // To know if we triggered on clients the start of the cast
        [HideInInspector] public bool canCast; // Simple indicator set automatically in Initialize to know if this unit has any abilities

        // ============================= ABILITY USE =============================

        /// <summary>
        /// Command for the unit to cast an ability or an item.
        /// </summary>
        /// <param name="abilityIndex">Global index of ability in the ability pool of the unit. You can get it by Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is this in item?</param>
        /// <param name="unit">Target unit, can be null.</param>
        /// <param name="location">Target location, can be Vector3.zero.</param>
        public bool UseAbilityItem(int abilityIndex, bool isItem, Unit unit, Vector3 location, bool issuedByPlayer = false)
        {
            if (dead || muted) return false;
            if (isBeingBuilt) return false;

            // Clients send the command to the server
            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.instance.UseAbilityCommandSend(this, abilityIndex, isItem, unit, location);
                return true;
            }

            if (isItem && items[abilityIndex] == null) return false;
            if (CheckAbilityItemRequirements(owner, abilityIndex, isItem)) return false;
            if (!IsCooldownGood(abilityIndex, isItem)) return false;

            Ability currentAbility;
            int currentLevel;
            if (isItem)
            {
                currentAbility = (Ability)items[abilityIndex];
                currentLevel = 0;
            }
            else
            {
                currentAbility = Utils.GetAbilityByIndex(this, abilityIndex);
                currentLevel = abilityLevel[abilityIndex];
            }

            // Reject invalid composite targets before interrupting the current command.
            if (currentAbility is CompositeSkill composite
                && !(unit != null ? composite.Check(this, owner, currentLevel, unit)
                                  : composite.Check(this, owner, currentLevel))) return false;

            if (!firstAttack) AttackStop();
            OnCommand?.Invoke(issuedByPlayer);

            // Set active ability parameters
            activeAbility = currentAbility;
            activeAbilityIndex = abilityIndex;
            activeAbilityItem = isItem;
            activeAbilityLevel = currentLevel;
            activeAbilityLocation = location;
            activeAbilityUnit = unit;
            activeAbilityRange = (currentAbility.castRange.Length > currentLevel && currentAbility.castRange[currentLevel] != 0) ? currentAbility.castRange[currentLevel] : 0;
            activeAbilityCastTime = (currentAbility.castTime.Length > currentLevel && currentAbility.castTime[currentLevel] != 0) ? currentAbility.castTime[currentLevel] : 0;
            activeAbilityDuration = (currentAbility.duration.Length > currentLevel && currentAbility.duration[currentLevel] != 0) ? currentAbility.duration[currentLevel] : 0;
            activeAbilityDuration += activeAbilityCastTime;

            // If has cast range, cast time or continuous we change the state
            if (currentAbility.castRange.Length > currentLevel && currentAbility.castRange[currentLevel] != 0 ||
               (currentAbility.castTime.Length > currentLevel && currentAbility.castTime[currentLevel] != 0 ||
               (currentAbility.continuous)))
            {
                unitState = UnitStates.AbilityCasting;
                OnCommand += ResetAbilityState;
                if (activeAbilityUnit) activeAbilityUnit.OnReferenceChange += AbilityUnitReferenceChange;
                MakeAgent(false);
                currentActionTime = 0; // If stunned we do not use ability, but store information that is going to be used after the stun ends
            }
            // Otherwise we just use ability and do not stop the current action (Moving)
            else
            {
                // Use ability immediately
                UseAbilityImmediately(currentAbility, currentLevel, abilityIndex, isItem, unit, location, false);
            }

            return true;
        }

        /// <summary>
        /// Uses given ability immediately, last method called when user commmands to cast an ability.
        /// </summary>
        /// <param name="ability">Ability to use.</param>
        /// <param name="abilityLevel">Level of the ability.</param>
        /// <param name="abilityIndex">Global index of ability in the ability pool of the unit. You can get it by Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is this an item?</param>
        /// <param name="abilityTarget">Target unit of the ability, can be null.</param>
        /// <param name="abilityLocation">Target location of the ability, can be Vector3.zero.</param>
        /// <param name="interrupt">Should cast interrupt the unit? When true will call Idle() after cast of non-continuous ability.</param>
        /// <param name="shadowCasterID">If ability is eligible creates shadowcaster with this ID, used by clients. ID is received from the server.</param>
        public void UseAbilityImmediately(Ability ability, int abilityLevel, int abilityIndex, bool isItem, Unit abilityTarget, Vector3 abilityLocation, bool interrupt = false, int shadowCasterID = -1)
        {
            // Do the last custom check of the ability before using it
            bool customCheck = true;
            if (abilityTarget) customCheck = ability.Check(this, this.owner, abilityLevel, abilityTarget);
            else if (abilityLocation != Vector3.zero) customCheck = ability.Check(this, this.owner, abilityLevel, abilityLocation);
            else customCheck = ability.Check(this, this.owner, abilityLevel);
            if (!customCheck)
            {
                // A late check can fail after the wind-up (for example, an HP cost).
                // Release the cast state so the unit can accept its next command.
                if (interrupt && !NetworkConnectionHandler.isClient) Idle();
                return;
            }

            // If server/offline we generate unique shadow caster ID
            if (!NetworkConnectionHandler.isClient && ability.continuous && !ability.interruptible)
            {
                shadowCasterID = ShadowCaster.GetUniqueID();
            }

            // When server activates any ability, we send data to clients
            if (NetworkManager.Singleton.IsServer)
            {
                NetworkDataSync.instance.AbilityUseSend(this, ability, abilityLevel, abilityIndex, isItem, abilityTarget, abilityLocation, interrupt, shadowCasterID);
            }

            // if interrupt we make unit visible
            if (interrupt && isInvisible) SetInvisibility(false);

            // Ability initiate
            if (ability.continuous)
            {
                if (ability.interruptible)
                {
                    // If continuous we just set the active ability in use, it will be now handled by HandleEveryFrameAbilities()
                    activeAbilityInUse = true;
                    activeAbility = ability;
                    activeAbilityLevel = abilityLevel;
                    activeAbilityUnit = abilityTarget;
                    activeAbilityLocation = abilityLocation;
                    if (!interrupt && activeAbilityUnit)
                    {
                        activeAbilityUnit.OnReferenceChange += AbilityUnitReferenceChange; // If interrupt is true - we already subscribed in AbilityUse()
                    }

                    if (activeAbilityUnit) activeAbility.Activate(this, this.owner, abilityLevel, activeAbilityUnit, ref activeAbilityVFX);
                    else if (activeAbilityLocation != Vector3.zero) activeAbility.Activate(this, this.owner, abilityLevel, activeAbilityLocation, ref activeAbilityVFX);
                    else activeAbility.Activate(this, this.owner, abilityLevel, ref activeAbilityVFX);

                    currentActionTime = 0;

                    AnimatorSetBool(AnimationState.Casting, true);
                }
                else
                {
                    // Uninterruptible, continuous ability
                    float abilityRange = (ability.castRange.Length > abilityLevel && ability.castRange[abilityLevel] != 0) ? ability.castRange[abilityLevel] : 0;
                    float abilityDuration = (ability.duration.Length > abilityLevel && ability.duration[abilityLevel] != 0) ? ability.duration[abilityLevel] : 0;

                    // We spawn shadowcaster
                    ShadowCaster sc = ShadowCaster.Spawn(shadowCasterID, this, this.owner, ability, abilityLevel, abilityTarget, abilityLocation, abilityRange, abilityDuration);

                    // Activate
                    if (abilityTarget) ability.Activate(this, this.owner, abilityLevel, abilityTarget, ref sc.activeAbilityVFX);
                    else if (abilityLocation != Vector3.zero) ability.Activate(this, this.owner, abilityLevel, abilityLocation, ref sc.activeAbilityVFX);
                    else ability.Activate(this, this.owner, abilityLevel, ref sc.activeAbilityVFX);

                    // Reset the states after cast
                    EndActiveAbility(true, false);
                    if (!NetworkConnectionHandler.isClient) Idle();
                }
            }
            else
            {
                if (ability.type == AbilityType.Toggle) UseToggleAbility_Internal(ability, abilityIndex, isItem);
                else if (abilityTarget) ability.Use(this, this.owner, abilityLevel, abilityTarget);
                else if (abilityLocation != Vector3.zero) ability.Use(this, this.owner, abilityLevel, abilityLocation);
                else ability.Use(this, this.owner, abilityLevel);

                // Reset the states after cast
                EndActiveAbility(true, false);
                // if interrupt we Idle() after cast
                if (interrupt && !NetworkConnectionHandler.isClient) Idle();
            }

            // Cooldown
            if (ability.cooldown.Length > abilityLevel && ability.cooldown[abilityLevel] != 0) ChangeAbilityCooldown(ability.cooldown[abilityLevel] * (isItem ? 1f : SpellCastModifiers.Cooldown(this)), abilityIndex, isItem);
            // Subtract costs
            SubtractAbilityItemCost(owner, abilityIndex, isItem);
            // Item charge decrease
            if (isItem) ItemChargesChange(abilityIndex);
        }

        /// <summary>
        /// For internal usage only. It adds the ability to every frame abilities.
        /// </summary>
        /// <param name="toggleAbility">Toggle ability.</param>
        /// <param name="abilityIndex">Global index of ability in the ability pool of the unit. You can get it by Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is this ability an item?</param>
        public void UseToggleAbility_Internal(Ability toggleAbility, int abilityIndex, bool isItem)
        {
            // If toggle active, turn it off
            int indexOf = IndexOfEveryFrameAbility(abilityIndex, isItem);
            int level = (isItem) ? 0 : abilityLevel[abilityIndex];
            if (indexOf != -1)
            {
                RemoveEveryFrameIfExists(abilityIndex, isItem, level, indexOf);
            }
            else
            {
                AddEveryFrameAbility(toggleAbility, abilityIndex, isItem, level);
            }
            OnRedrawAbilityView?.Invoke();
        }

        // ============================= EVERY FRAME ABILITY =============================

        /// <summary>
        /// Adds everyframe ability - used by auras and toggles.
        /// </summary>
        /// <param name="ability">Ability that should be activated and added.</param>
        /// <param name="abilityIndex">Global index of ability in the ability pool of the unit. You can get it by Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is this ability an item?</param>
        /// <param name="abilityLvl">Level of the ability.</param>
        public void AddEveryFrameAbility(Ability ability, int abilityIndex, bool isItem, int abilityLvl)
        {
            // When ability index is -1, we should find the it ourselves
            if (abilityIndex == -1 && !isItem)
            {
                abilityIndex = Utils.GetAbilityIndex(abilities, ability);
                if (abilityIndex == -1)
                {
                    Debug.LogWarning("Ability " + ability.abilityName[0] + " tries to add to everyFrameAbilities, but is not part of the ability pool of the unit " + unitName);
                    return;
                }
            }

            // Activate and add
            ability.Activate(this, this.owner, abilityLvl);

            everyFrameAbilities.Add(ability);
            everyFrameAbilityIndex.Add(abilityIndex);
            everyFrameAbilityIsItem.Add(isItem);
        }

        /// <summary>
        /// Removes everyframe ability, if it has been added.
        /// </summary>
        /// <param name="ability">Ability to be removed.</param>
        /// <param name="isItem">Is this ability an item?</param>
        /// <param name="abilityLvl">Level of the ability.</param>
        /// <param name="everyFrameIndex">Index of the ability in everyFrameAbilities list, if not provided it is calculated automatically.</param>
        public void RemoveEveryFrameIfExists(Ability ability, bool isItem, int abilityLvl, int everyFrameIndex = -1)
        {
            int indexOf = (everyFrameIndex == -1) ? IndexOfEveryFrameAbility(ability, isItem) : everyFrameIndex;

            if (indexOf != -1)
            {
                everyFrameAbilities[indexOf].Deactivate(this, this.owner, abilityLvl);

                everyFrameAbilities.RemoveAt(indexOf);
                everyFrameAbilityIndex.RemoveAt(indexOf);
                everyFrameAbilityIsItem.RemoveAt(indexOf);

                OnRedrawAbilityView?.Invoke();
            }
        }

        /// <summary>
        /// Removes everyframe ability by its index, if it has been added.
        /// </summary>
        /// <param name="abilityIndex">>Global index of ability in the ability pool of the unit. You can get it with Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is this ability an item?</param>
        /// <param name="abilityLvl">Level of the ability.</param>
        /// <param name="everyFrameIndex">Index of the ability in everyFrameAbilities list, if not provided it is calculated automatically.</param>
        public void RemoveEveryFrameIfExists(int abilityIndex, bool isItem, int abilityLvl, int everyFrameIndex = -1)
        {
            int indexOf = (everyFrameIndex == -1) ? IndexOfEveryFrameAbility(abilityIndex, isItem) : everyFrameIndex;
            if (indexOf != -1)
            {
                everyFrameAbilities[indexOf].Deactivate(this, this.owner, abilityLvl);

                everyFrameAbilities.RemoveAt(indexOf);
                everyFrameAbilityIndex.RemoveAt(indexOf);
                everyFrameAbilityIsItem.RemoveAt(indexOf);

                OnRedrawAbilityView?.Invoke();
            }
        }

        /// <summary>
        /// Returns index of the ability in everyFrameAbilities list. Returns -1 if not found.
        /// </summary>
        /// <param name="ability">Ability to search.</param>
        /// <param name="isItem">Is it an item?</param>
        /// <returns></returns>
        public int IndexOfEveryFrameAbility(Ability ability, bool isItem)
        {
            for (int i = 0; i < everyFrameAbilityIndex.Count; i++)
            {
                if (everyFrameAbilities[i] == ability && everyFrameAbilityIsItem[i] == isItem)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Returns index of the ability in everyFrameAbilities list. Returns -1 if not found.
        /// </summary>
        /// <param name="abilityIndex">Global ability index. You can get it with Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is it an item?</param>
        /// <returns></returns>
        public int IndexOfEveryFrameAbility(int abilityIndex, bool isItem)
        {
            for (int i = 0; i < everyFrameAbilityIndex.Count; i++)
            {
                if (everyFrameAbilityIndex[i] == abilityIndex && everyFrameAbilityIsItem[i] == isItem)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Every gameManager.Tick runs the logic for all everyFrame abilities. Also handles health and mana regeneration.
        /// </summary>
        public void HandleEveryFrameAbilities()
        {
            // MP/HP regeneration
            if (healthRegen != 0) ChangeHP(healthRegen * GameManager.instance.currentDeltaTime, true);
            if (manaRegen != 0) ChangeMP(manaRegen * GameManager.instance.currentDeltaTime, true);

            // Auras and Toggle abilities
            for (int i = everyFrameAbilityIndex.Count - 1; i >= 0; i--)
            {
                // No mana and resource check for auras
                if (everyFrameAbilities[i].type == AbilityType.Aura)
                {
                    // Aura is unlocked, call Use()
                    if (everyFrameAbilityIsItem[i])
                    {
                        // Item - always 0 level
                        everyFrameAbilities[i].Use(this, this.owner, 0);
                    }
                    else
                    {
                        // Ability
                        everyFrameAbilities[i].Use(this, this.owner, abilityLevel[everyFrameAbilityIndex[i]]);
                    }
                }
                else if (everyFrameAbilities[i].type == AbilityType.Toggle)
                {
                    // Do Mana check and subtract the mana cost
                    float manaCost = 0;

                    if (everyFrameAbilityIsItem[i] && items[everyFrameAbilityIndex[i]].manaCostPerSecond.Length != 0) manaCost = items[everyFrameAbilityIndex[i]].manaCostPerSecond[0] * GameManager.instance.currentDeltaTime;
                    else if (everyFrameAbilities[i].manaCostPerSecond.Length > abilityLevel[everyFrameAbilityIndex[i]]) manaCost = everyFrameAbilities[i].manaCostPerSecond[abilityLevel[everyFrameAbilityIndex[i]]] * GameManager.instance.currentDeltaTime;

                    if (mana < manaCost)
                    {
                        // Ability can not be used
                        everyFrameAbilities.RemoveAt(i);
                        everyFrameAbilityIndex.RemoveAt(i);
                        everyFrameAbilityIsItem.RemoveAt(i);
                    }
                    else
                    {
                        // Ability can be used, call Use()
                        if (manaCost != 0) ChangeMP(-manaCost);
                        everyFrameAbilities[i].Use(this, this.owner, abilityLevel[everyFrameAbilityIndex[i]]);
                    }
                }
            }
        }

        // ============================= LEVEL UP =============================

        /// <summary>
        /// Command used to increase the level of an ability. Unit needs to be Levelling Unit and have abilityPoints.  
        /// </summary>
        /// <param name="abilityIndex">Global ability index. You can get it with Utils.GetAbilityIndex.</param>
        /// <returns></returns>
        public bool LevelUpAbilityCommand(Ability ability, int abilityIndex)
        {
            // If client, send command to server
            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.instance.LevelUpAbility(this, ability, abilityIndex);
                return false;
            }

            // If leveling unit, ability not locked and still has not reached the maximum level
            if (levelingUnit && !abilityLocked[abilityIndex] && abilityLevel[abilityIndex] < abilities[abilityIndex].maxLevels - 1 && levelingUnit.abilityPoints > 0)
            {
                LevelUpAbility(ability, abilityIndex);

                // If level up successful server will inform clients that ability level has changed
                if (NetworkManager.Singleton.IsServer)
                {
                    NetworkDataSync.instance.LevelUpAbilitySend(this, ability, abilityIndex);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Increases the level of an ability. Assumes unit is Levelling Unit and has abilityPoints.
        /// </summary>
        /// <param name="abilityIndex">Global ability index. You can get it with Utils.GetAbilityIndex.</param>
        public void LevelUpAbility(Ability ability, int abilityIndex)
        {
            abilityLevel[abilityIndex]++; // Increase ability level
            levelingUnit.abilityPoints--; // Decrease ability points

            AllAbilityLockLevelsCalculate();

            ability.Unlock(this, this.owner, abilityLevel[abilityIndex]); // Unlock the ability, we just learnt it
        }

        // ============================= COOLDOWN =============================

        /// <summary>
        /// Adds cooldown or changes existing one to specified value.
        /// </summary>
        /// <param name="cooldown">New cooldown value for the ability.</param>
        /// <param name="abilityIndex">Global ability index. You can get it with Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is it an item?</param>
        /// <param name="noRedraw">Should not redraw the ability view?</param>
        public void ChangeAbilityCooldown(float cooldown, int abilityIndex, bool isItem, bool noRedraw = false)
        {
            // Check if cooldown already exists
            int cooldownIndex = GetAbilityCooldownIndex(abilityIndex, isItem);
            if (cooldownIndex != -1)
            {
                // Change existing cooldown to a new one
                if (cooldown < 0)
                {
                    // Remove cooldown
                    cooldownAbility.RemoveAt(cooldownIndex);
                    cooldownAbilityIndex.RemoveAt(cooldownIndex);
                    cooldownAbilityIsItem.RemoveAt(cooldownIndex);
                }
                else
                {
                    cooldownAbility[cooldownIndex] = cooldown;
                }
                if (!noRedraw) OnRedrawAbilityView?.Invoke();
                return;
            }
            else if (cooldown > 0)
            {
                // Add new cooldown
                cooldownAbility.Add(cooldown);
                cooldownAbilityIndex.Add(abilityIndex);
                cooldownAbilityIsItem.Add(isItem);
                if (!noRedraw) OnRedrawAbilityView?.Invoke();
            }
        }

        /// <summary>
        /// Returns true if no cooldown on ability, false if there is. Displays the message indicating that cooldown is not over.
        /// </summary>
        /// <param name="abilityIndex">Global ability index. You can get it with Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is it an item?</param>
        /// <returns></returns>
        public bool IsCooldownGood(int abilityIndex, bool isItem)
        {
            for (int i = 0; i < cooldownAbilityIndex.Count; i++)
            {
                if (cooldownAbilityIndex[i] == abilityIndex && cooldownAbilityIsItem[i] == isItem)
                {
                    if (this == Presentation.Selection?.ActiveUnit && (SlotManager.instance.debugMode || owner == SlotManager.instance.currentPlayer)) Presentation.NotifyMsg("Wait until the cooldown is over.");
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Returns ability`s current cooldown, if none returns 0.
        /// </summary>
        /// <param name="abilityIndex">Global ability index. You can get it with Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is it an item?</param>
        /// <returns>Cooldown of the ability</returns>
        public float GetAbilityCooldown(int abilityIndex, bool isItem)
        {
            for (int i = 0; i < cooldownAbilityIndex.Count; i++)
            {
                if (cooldownAbilityIndex[i] == abilityIndex && cooldownAbilityIsItem[i] == isItem)
                {
                    return cooldownAbility[i];
                }
            }
            return 0;
        }

        /// <summary>
        /// Returns ability`s current cooldown index, if does not exist returns -1;
        /// </summary>
        /// <param name="abilityIndex">Global ability index. You can get it with Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is it an item?</param>
        /// <returns></returns>
        public int GetAbilityCooldownIndex(int abilityIndex, bool isItem)
        {
            for (int i = 0; i < cooldownAbilityIndex.Count; i++)
            {
                if (cooldownAbilityIndex[i] == abilityIndex && cooldownAbilityIsItem[i] == isItem)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Every gameManager.Tick calculates cooldown for eligble abilities.
        /// </summary>
        public void CooldownCalculate()
        {
            for (int i = 0; i < cooldownAbility.Count; i++)
            {
                cooldownAbility[i] -= GameManager.instance.currentDeltaTime;
                if (cooldownAbility[i] <= 0)
                {
                    // Cooldown end
                    cooldownAbility.RemoveAt(i);
                    cooldownAbilityIndex.RemoveAt(i);
                    cooldownAbilityIsItem.RemoveAt(i);
                    OnRedrawAbilityView?.Invoke();
                }
            }
        }

        // ============================= INITIALIZATION =============================

        /// <summary>
        /// Initializes all necessary data for unit`s abilities.
        /// </summary>
        public void InitializeAbilities()
        {
            // Abilties
            if (abilities.Length > 0)
            {
                // Resize the abilityLevel & abilityLocked array. Abilities exist, but ability levels were not specified. 
                if (abilityLevel.Length < abilities.Length)
                {
                    Array.Resize(ref abilityLevel, abilities.Length);
                }

                // Ability locks
                abilityLocked = new bool[abilities.Length];

                // Containers - Increase abilityLevel and abilityLocked size by the amount of abilities inside containers
                RecursiveIncrease(abilities);

                void RecursiveIncrease(Ability[] recursiveAbilities)
                {
                    for (int i = 0; i < recursiveAbilities.Length; i++)
                    {
                        if (recursiveAbilities[i].type == AbilityType.Container)
                        {
                            Container container = (Container)recursiveAbilities[i];

                            RecursiveIncrease(container.abilities);
                            Array.Resize(ref abilityLevel, abilityLevel.Length + container.abilities.Length);
                            Array.Resize(ref abilityLocked, abilityLocked.Length + container.abilities.Length);
                        }
                    }
                }

                // Initially all abilities are locked, then they unlocked if requirements are met
                for (int i = 0; i < abilityLocked.Length; i++) abilityLocked[i] = true;

                // For each ability that is hero levelable set level to be -1. Which means it is yet to be learnt.
                int abilityGlobalIndex = -1;
                RecursiveLevelSet(abilities);

                void RecursiveLevelSet(Ability[] recursiveAbilities)
                {
                    for (int i = 0; i < recursiveAbilities.Length; i++)
                    {
                        abilityGlobalIndex++;

                        if (recursiveAbilities[i].heroLevelable == true)
                        {
                            abilityLevel[abilityGlobalIndex] = -1; // It is yet to be learnt
                        }

                        if (recursiveAbilities[i].type == AbilityType.Container)
                        {
                            Container container = (Container)recursiveAbilities[i];
                            RecursiveLevelSet(container.abilities);
                        }

                        // If canProcess and if isShop
                        if (recursiveAbilities[i].type == AbilityType.Process || recursiveAbilities[i] is UpgradeBuilding)
                        {
                            canProcess = true;
                        }
                        // Check if unit training
                        if (recursiveAbilities[i] is UnitTraining && !canMove) isWaypoint = true;
                        // IsShop
                        if (recursiveAbilities[i].isItem) isShop = true;
                    }
                }

                // Initial ability lock and level calculation
                AllAbilityLockLevelsCalculate();
            }
        }

        /// <summary>
        /// Checks, unlocks and levels of unit`s abilities that meet requirements. Abilities that are heroLevelable are ignored.
        /// Hero levelable abilities can be leveled up only via ability points. If it is a container all abilities inside of it will acquire level and lock of the container.
        /// </summary>
        public void AllAbilityLockLevelsCalculate()
        {
            if (abilities != null)
            {
                RecursiveLockLevelCalculate(abilities);
            }
        }

        /// <summary>
        /// Calculates levels and locks of abilities and containers inside given abilityArray. For non heroLevelable abilities it will set ability level to maxium possible based on requirements.
        /// Hero levelable abilities can be leveled up only via ability points. If it is a container all abilities inside of it will acquire level and lock of the container.
        /// </summary>
        /// <param name="abilityArray">Abilities to process.</param>
        private void RecursiveLockLevelCalculate(Ability[] abilityArray)
        {
            for (int i = 0; i < abilityArray.Length; i++)
            {
                int abilityIndex = Utils.GetAbilityIndex(abilities, abilityArray[i]);

                // If ability is heroLevelable set Locks only, if it is also a container set all abilities` level inside of it to be the level of container.
                if (abilityArray[i].heroLevelable)
                {
                    // Lock state of the current ability
                    if (abilityArray[i].requiredTech.Length > 0 || abilityArray[i].requiredLevel.Length > 0)
                    {
                        // We need to check the next level of ability when learning the ability <<<------------------------------------------------------
                        // ability locked should refer to the next level of the ability if hero levelable
                        int lvl = abilityLevel[abilityIndex] + 1;

                        //if (abilityLevel[abilityIndex] != -1 && TechnologyManager.instance.isUnlocked(abilityArray[i].requiredTech[abilityLevel[abilityIndex]].data, owner) && levelingUnit?.level >= abilityArray[i].requiredLevel[abilityLevel[abilityIndex]])
                        if ((abilityArray[i].requiredTech.Length <= lvl || TechnologyManager.instance.isUnlocked(abilityArray[i].requiredTech[lvl].data, owner)) // Technology check
                            && (levelingUnit == null || abilityArray[i].requiredLevel.Length <= lvl || levelingUnit.level >= abilityArray[i].requiredLevel[lvl])) // Level check
                        {
                            // Ability is unlocked
                            abilityLocked[abilityIndex] = false;
                            // HeroLevelable should not call Unlock method of the ability - we call it when we learn the ability

                            // If it is container, set all abilities level inside of it to the same level as container then check lock state
                            if (abilityArray[i].type == AbilityType.Container)
                            {
                                Container container = (Container)abilityArray[i];
                                SetContainerAbilitiesLevel(container.abilities, abilityLevel[abilityIndex]);

                                // Set locks
                                CalculateAbilityLocks(container.abilities);
                            }
                        }
                        else
                        {
                            // Ability is locked
                            abilityLocked[abilityIndex] = true;

                            // HeroLevelable should call Lock method of the ability only if this ability was learnt previously
                            // if (abilityLevel[abilityIndex] != -1)
                            // {
                            //     RemoveEveryFrameIfExists(abilityIndex, false, abilityLevel[abilityIndex]);
                            //     if (!abilityArray[i].isItem) abilityArray[i].Lock(this, this.owner, abilityLevel[abilityIndex]);
                            // }

                            // If container lock all abilities inside of it
                            if (abilityArray[i].type == AbilityType.Container)
                            {
                                Container container = (Container)abilityArray[i];
                                SetContainerAbilitiesLock(container.abilities, true);
                            }
                        }
                    }
                    else
                    {
                        // Ability is unlocked
                        abilityLocked[abilityIndex] = false;
                        // HeroLevelable should not call Unlock method of the ability

                        // If it is container, set all abilities level inside of it to the same level as container then check lock state
                        if (abilityArray[i].type == AbilityType.Container)
                        {
                            Container container = (Container)abilityArray[i];
                            SetContainerAbilitiesLevel(container.abilities, abilityLevel[abilityIndex]);

                            // Set locks
                            CalculateAbilityLocks(container.abilities);
                        }
                    }
                }
                else // Not heroLevelable
                {
                    if (abilityArray[i].requiredTech.Length > 0 || abilityArray[i].requiredLevel.Length > 0)
                    {
                        // For each requirement level check if requirements are met, if so unlock and levelup
                        int requirementLength = (abilityArray[i].requiredTech.Length > abilityArray[i].requiredLevel.Length) ? abilityArray[i].requiredTech.Length : abilityArray[i].requiredLevel.Length;

                        for (int l = abilityLevel[abilityIndex]; l < requirementLength; l++)
                        {
                            if ((abilityArray[i].requiredTech.Length <= l || TechnologyManager.instance.isUnlocked(abilityArray[i].requiredTech[l].data, owner)) // Requirement tech check
                            && (levelingUnit == null || abilityArray[i].requiredLevel.Length <= l || levelingUnit.level >= abilityArray[i].requiredLevel[abilityLevel[abilityIndex]])) // Level requirement check
                            {
                                // We call unlock. If the same level as current level we call unlock if it was locked before
                                if (!abilityArray[i].isItem && !(abilityLocked[abilityIndex] == false && l == abilityLevel[abilityIndex]))
                                {
                                    // We lock previous level if previous level exists
                                    if (l > 0)
                                    {
                                        RemoveEveryFrameIfExists(abilityIndex, false, l - 1);
                                        if (!abilityArray[i].isItem) abilityArray[i].Lock(this, this.owner, l - 1);
                                    }

                                    abilityArray[i].Unlock(this, this.owner, l);
                                }

                                abilityLocked[abilityIndex] = false;
                                abilityLevel[abilityIndex] = l;
                            }
                            else
                            {
                                // If level is not the same as abilityLevel, we were testing the next level of the ability
                                if (l == abilityLevel[abilityIndex])
                                {
                                    // We call lock only if previously was unlocked
                                    if (abilityLocked[abilityIndex] == false)
                                    {
                                        RemoveEveryFrameIfExists(abilityIndex, false, abilityLevel[abilityIndex]);
                                        if (!abilityArray[i].isItem) abilityArray[i].Lock(this, this.owner, abilityLevel[abilityIndex]);
                                    }

                                    abilityLocked[abilityIndex] = true;
                                }
                                break; // No need to check next levels, this level is locked
                            }
                        }
                    }
                    else
                    {
                        // If ability does not have requirements we unlock it immediately, and set it to max level?
                        abilityLocked[abilityIndex] = false;
                        if (!abilityArray[i].isItem) abilityArray[i].Unlock(this, this.owner, abilityLevel[abilityIndex]);
                    }

                    // If it is container, check ability requirements inside of it
                    if (abilityArray[i].type == AbilityType.Container)
                    {
                        Container container = (Container)abilityArray[i];
                        RecursiveLockLevelCalculate(container.abilities);
                    }
                }
            }
        }

        /// <summary>
        /// Calculates locks of given abilities and if container of abilities inside of it.
        /// </summary>
        /// <param name="abilityArray">Abilities to process.</param>
        private void CalculateAbilityLocks(Ability[] abilityArray)
        {
            for (int i = 0; i < abilityArray.Length; i++)
            {
                int abilityIndex = Utils.GetAbilityIndex(abilities, abilityArray[i]);

                // Calculate lock state at the current level only
                if (abilityArray[i].requiredTech.Length > 0)
                {
                    if (TechnologyManager.instance.isUnlocked(abilityArray[i].requiredTech[abilityLevel[abilityIndex]].data, owner))
                    {
                        abilityLocked[abilityIndex] = false;
                    }
                    else
                    {
                        abilityLocked[abilityIndex] = true;
                    }
                }

                if (abilityArray[i].type == AbilityType.Container)
                {
                    Container container = (Container)abilityArray[i];
                    CalculateAbilityLocks(container.abilities);
                }
            }
        }

        /// <summary>
        /// Sets the level of abilities inside the container to specified value.
        /// </summary>
        /// <param name="abilityArray">Abilities to process(Abilities inside the container).</param>
        /// <param name="level">Desired level for abilities.</param>
        private void SetContainerAbilitiesLevel(Ability[] abilityArray, int level)
        {
            for (int i = 0; i < abilityArray.Length; i++)
            {
                int abilityIndex = Utils.GetAbilityIndex(abilities, abilityArray[i]);

                // If ability level is not higher than max level
                if (abilityArray[i].maxLevels - 1 > level)
                {
                    abilityLevel[abilityIndex] = level;
                }
                else abilityLevel[abilityIndex] = abilityArray[i].maxLevels - 1;

                if (abilityArray[i].type == AbilityType.Container)
                {
                    Container container = (Container)abilityArray[i];
                    SetContainerAbilitiesLevel(container.abilities, level);
                }
            }
        }

        /// <summary>
        /// Sets the lock of abilities inside the container.
        /// </summary>
        /// <param name="abilityArray">Abilities to process(Abilities inside the container).</param>
        /// <param name="locked">Should abilities be locked?</param>
        private void SetContainerAbilitiesLock(Ability[] abilityArray, bool locked)
        {
            for (int i = 0; i < abilityArray.Length; i++)
            {
                int abilityIndex = Utils.GetAbilityIndex(abilities, abilityArray[i]);

                abilityLocked[abilityIndex] = locked;

                if (abilityArray[i].type == AbilityType.Container)
                {
                    Container container = (Container)abilityArray[i];
                    CalculateAbilityLocks(container.abilities);
                }
            }
        }

        // ============================= ACTIVE ABILITY =============================

        /// <summary>
        /// Sets the state to actively cast the given ability.
        /// </summary>
        /// <param name="ability">Ability currently being active.</param>
        /// <param name="abilityLevel">Level of the ability.</param>
        /// <param name="targetUnit">Target unit, can be null.</param>
        /// <param name="targetLocation">Target location, can be Vector3.zero.</param>
        /// <param name="actionTime">Current action time. Ability is considered actively being casted while this parameter is less than activeAbilityDuration.</param>
        public void SetActiveAbility(Ability ability, int abilityLevel, Unit targetUnit, Vector3 targetLocation, float actionTime)
        {
            // Set parameters
            if (!NetworkConnectionHandler.isClient)
            {
                unitState = UnitStates.AbilityCasting;
                OnCommand += ResetAbilityState;
            }
            activeAbilityInUse = true;
            activeAbility = ability;
            activeAbilityLevel = abilityLevel;
            activeAbilityUnit = targetUnit;
            if (activeAbilityUnit != null) activeAbilityUnit.OnReferenceChange += AbilityUnitReferenceChange;
            activeAbilityLocation = targetLocation;
            activeAbilityRange = (ability.castRange.Length > abilityLevel && ability.castRange[abilityLevel] != 0) ? ability.castRange[abilityLevel] : 0;
            activeAbilityCastTime = (ability.castTime.Length > abilityLevel && ability.castTime[abilityLevel] != 0) ? ability.castTime[abilityLevel] : 0;
            activeAbilityDuration = (ability.duration.Length > abilityLevel && ability.duration[abilityLevel] != 0) ? ability.duration[abilityLevel] : 0;
            activeAbilityDuration += activeAbilityCastTime;
            currentActionTime = actionTime;

            AnimatorSetBool(AnimationState.Casting, true);

            // Initiate active ability
            if (activeAbilityUnit) activeAbility.Activate(this, this.owner, abilityLevel, activeAbilityUnit, ref activeAbilityVFX);
            else if (activeAbilityLocation != Vector3.zero) activeAbility.Activate(this, this.owner, abilityLevel, activeAbilityLocation, ref activeAbilityVFX);
            else activeAbility.Activate(this, this.owner, abilityLevel, ref activeAbilityVFX);
        }

        /// <summary>
        /// If active ability requires targetUnit we subscribe to reference changes of the target unit.
        /// </summary>
        /// <param name="newActive">New reference for activeAbilityUnit. Can be null.</param>
        public void AbilityUnitReferenceChange(Unit newActive)
        {
            activeAbilityUnit.OnReferenceChange -= AbilityUnitReferenceChange;

            // End the ability cast if null
            if (newActive == null)
            {
                // Only server can stop the ability
                if (!NetworkConnectionHandler.isClient)
                {
                    Idle();
                }
                return;
            }
            // New reference
            else
            {
                if (target != null && target == activeAbilityUnit) target = newActive;
                activeAbilityUnit = newActive;
                activeAbilityUnit.OnReferenceChange += AbilityUnitReferenceChange;
            }
        }

        /// <summary>
        /// Resets the ability state and syncs with the clients.
        /// </summary>
        public void ResetAbilityState(bool issuedByPlayer)
        {
            EndActiveAbility(true, true);
        }

        /// <summary>
        /// Ends active ability, optionally resetting ability state.
        /// </summary>
        /// <param name="reset">Should reset ability state?</param>
        /// <param name="calledByServer">Is command called by the server? When false does not sync with the clients, it means it should also be called by the clients locally.</param>
        public void EndActiveAbility(bool reset, bool calledByServer)
        {
            // End Active ability
            if (activeAbilityInUse)
            {
                // Active ability end
                if (activeAbilityUnit) activeAbility.Deactivate(this, this.owner, activeAbilityLevel, activeAbilityUnit, ref activeAbilityVFX);
                else if (activeAbilityLocation != Vector3.zero) activeAbility.Deactivate(this, this.owner, activeAbilityLevel, activeAbilityLocation, ref activeAbilityVFX);
                else activeAbility.Deactivate(this, this.owner, activeAbilityLevel, ref activeAbilityVFX);

                // Add cooldown
                if (activeAbility.cooldown.Length > activeAbilityLevel && activeAbility.cooldown[activeAbilityLevel] != 0) ChangeAbilityCooldown(activeAbility.cooldown[activeAbilityLevel], activeAbilityIndex, activeAbilityItem);

                AnimatorSetBool(AnimationState.Casting, false);
            }

            // Reset the state
            if (reset)
            {
                OnCommand -= ResetAbilityState;
                if (activeAbilityUnit != null) activeAbilityUnit.OnReferenceChange -= AbilityUnitReferenceChange;

                activeAbilityInUse = false;
                activeAbility = null;
                activeAbilityIndex = -1;
                activeAbilityLevel = 0;
                activeAbilityUnit = null;
                activeAbilityLocation = Vector3.zero;
                activeAbilityRange = 0;
                activeAbilityCastTime = 0;
                activeAbilityDuration = 0;
                currentActionTime = 0;

                playCast = false;
                sendToClients = false;
            }

            // If server send signal to clients
            if (calledByServer && NetworkManager.Singleton.IsServer)
            {
                NetworkDataSync.instance.AbilityStopSend(this);
            }
        }

        // ============================= UTILS =============================

        /// <summary>
        /// Returns true if requirements(lock, mana cost, resource cost) for the given ability are not met.
        /// </summary>
        /// <param name="player">Which player to check for resources.</param>
        /// <param name="abilityIndex">Global index of ability in the ability pool of the unit. You can get it by Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is it an item?</param>
        /// <param name="currentAbility">Optional parameter, when set ability index is not used to find the ability. Performance wise faster.</param>
        /// <returns></returns>
        private bool CheckAbilityItemRequirements(int player, int abilityIndex, bool isItem, Ability currentAbility = null)
        {
            if (isItem)
            {
                // No lock states for items and no multi-levels
                // No resource check for items when using them

                // Check if enough mana to use
                if (items[abilityIndex].manaCost.Length != 0 && items[abilityIndex].manaCost[0] > mana)
                {
                    Presentation.NotifyMsg("Not enough mana", owner, true);
                    return true;
                }

                // No resource check for items
                // if (items[abilityIndex].cost.Length != 0)
                // {
                //     for (int i = 0; i < items[abilityIndex].cost[0].data.Length; i++)
                //     {
                //         if (!GameResources.instance.CheckAmount(unitOwner, items[abilityIndex].cost[0].data[i]))
                //         {
                //             Presentation.NotifyMsg("Not enough " + items[abilityIndex].cost[0].data[i].type.name);
                //             return true;
                //         }
                //     }
                // }
            }
            else
            {
                if (currentAbility == null) currentAbility = Utils.GetAbilityByIndex(this, abilityIndex);

                // Check if ability is locked only when it is not herolevelable
                if (!currentAbility.heroLevelable && abilityLocked[abilityIndex]) return true;

                // Check if enough mana to use
                if (currentAbility.manaCost.Length > abilityLevel[abilityIndex] && currentAbility.manaCost[abilityLevel[abilityIndex]] > mana)
                {
                    Presentation.NotifyMsg("Not enough mana", owner, true);
                    return true;
                }

                // Check resources
                if (currentAbility.cost.Length > abilityLevel[abilityIndex])
                {
                    for (int i = 0; i < currentAbility.cost[abilityLevel[abilityIndex]].data.Length; i++)
                    {
                        if (!GameResources.instance.CheckAmount(player, currentAbility.cost[abilityLevel[abilityIndex]].data[i]))
                        {
                            Presentation.NotifyMsg("Not enough " + currentAbility.cost[abilityLevel[abilityIndex]].data[i].type.displayName, owner, true);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Subtracts costs of an ability or an item.
        /// </summary>
        /// <param name="player">Which player to subtract resources from.</param>
        /// <param name="abilityIndex">Global index of ability in the ability pool of the unit. You can get it by Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is it an item?</param>
        public void SubtractAbilityItemCost(int player, int abilityIndex, bool isItem)
        {
            if (isItem)
            {
                // No lock states for items and no multi-levels
                // No resource subtraction for items

                // Subtract mana
                if (items[abilityIndex].manaCost.Length != 0) ChangeMP(-items[abilityIndex].manaCost[0]);

                // No resource subtraction
                // if (items[abilityIndex].cost.Length != 0)
                // {
                //     for (int i = 0; i < items[abilityIndex].cost[0].data.Length; i++)
                //     {
                //         GameResources.instance.ChangeAmount(unitOwner, items[abilityIndex].cost[0].data[i], 1, true);
                //     }
                // }
            }
            else
            {
                Ability currentAbility = Utils.GetAbilityByIndex(this, abilityIndex);

                // Subtract mana               
                if (currentAbility.manaCost.Length > abilityLevel[abilityIndex]) ChangeMP(-currentAbility.manaCost[abilityLevel[abilityIndex]]);

                // Subtract resources
                if (currentAbility.cost.Length > abilityLevel[abilityIndex])
                {
                    for (int i = 0; i < currentAbility.cost[abilityLevel[abilityIndex]].data.Length; i++)
                    {
                        GameResources.instance.ChangeAmount(player, currentAbility.cost[abilityLevel[abilityIndex]].data[i], 1, true, true);
                    }
                }
            }
        }
    }
}
