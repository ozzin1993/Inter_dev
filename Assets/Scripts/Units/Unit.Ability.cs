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
        [Tooltip("Можно оставить пустым: пустые места — уровень 0.\nДля улучшаемых умений героя (Hero Levelable) задаёт НАЧАЛЬНЫЙ уровень; умение всё равно должно пройти свои требования.\nОстальные умения начинают с нуля и поднимаются сами, когда требования выполнены.\nУровень −1 («умение не выучено») снесён блоком Б9 (2026-09-06): открытое технологией умение сразу работает на базовых значениях.")]
        public int[] abilityLevel;

        [HideInInspector] public bool[] abilityLocked; // Defined automatically based on the technologies available for the player

        // Списки everyFrameAbilities/-Index/-IsItem (цикл аур) СНЕСЕНЫ блоком Б7 (2026-09-05): постоянные ауры —
        // пассивные умения с радиусом (CompositePassive, блок 8), режима «аура» у умений больше нет (целевая модель §9, §15).
        // В префабах юнитов старые поля остаются в YAML до пересохранения — Unity их молча отбрасывает.

        [HideInInspector] public List<float> cooldownAbility = new List<float>(); // Current cooldown of an ability
        [HideInInspector] public List<int> cooldownAbilityIndex = new List<int>(); // abilityCDIndex[index] = reference to ability[]
        [HideInInspector] public List<bool> cooldownAbilityIsItem = new List<bool>(); // If ability at index is item

        // Active ability technical variables: умение, которое юнит сейчас кастует (подход к цели, время замаха).
        // «Длящихся» умений (каналов) больше нет — снесены блоком Б6 (2026-09-04).
        [HideInInspector] public Ability activeAbility; // Ability that is being cast by this unit
        [HideInInspector] public int activeAbilityLevel; // Level of the ability when it was started
        [HideInInspector] public int activeAbilityIndex; // Index of the ability that is being cast by this unit
        [HideInInspector] public bool activeAbilityItem; // If current active ability is an item. DO NOT FORGET TO CHANGE THE INDEX IF ITEM MOVES ITS SLOT
        [HideInInspector] public Unit activeAbilityUnit; // If active ability requires unit
        [HideInInspector] public Vector3 activeAbilityLocation; // If active ability requires location
        // Флаг «умение исполняется прямо сейчас» (activeAbilityInUse) СНЕСЁН блоком Б6 (2026-09-04):
        // его включал только канал, а после сноса каналов — единственный оставшийся писатель
        // SetActiveAbility из загрузки сохранения, который восстанавливал неверный индекс умения.
        [HideInInspector] public float activeAbilityRange; // Range of active ability
        [HideInInspector] public float activeAbilityCastTime; // Cast timme of active ability
        [HideInInspector] public float activeAbilityDuration; // If this active ability has a duration
        [HideInInspector] public VFXReferencer activeAbilityVFX; // VFX of actively being used ability. Set by abilities` Activate/Deactivate

        private bool playCast = false; // To know if we are currently casting an ability
        private bool sendToClients = false; // To know if we triggered on clients the start of the cast

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
            if (muted) return false;
            if (isBeingBuilt) return false;

            // Clients send the command to the server
            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.Instance.UseAbilityCommandSend(this, abilityIndex, isItem, unit, location);
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

            if (!firstAttack) AttackStop();
            OnCommand?.Invoke(issuedByPlayer);

            // Set active ability parameters
            activeAbility = currentAbility;
            activeAbilityIndex = abilityIndex;
            activeAbilityItem = isItem;
            activeAbilityLevel = currentLevel;
            activeAbilityLocation = location;
            activeAbilityUnit = unit;
            // Дальность, время каста и длительность — общей выборкой по уровням (Б8): нет строки — последняя заполненная
            // (раньше уровень выше длины массива давал 0: каст без подхода и без замаха).
            activeAbilityRange = InterflowAbility.LevelValue(currentAbility.castRange, currentLevel);
            activeAbilityCastTime = InterflowAbility.LevelValue(currentAbility.castTime, currentLevel);
            activeAbilityDuration = InterflowAbility.LevelValue(currentAbility.duration, currentLevel);
            activeAbilityDuration += activeAbilityCastTime;

            // If has cast range or cast time we change the state (умений-каналов больше нет — блок Б6)
            if (activeAbilityRange != 0 || activeAbilityCastTime != 0)
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
        /// <param name="interrupt">Should cast interrupt the unit? When true will call Idle() after cast.</param>
        public void UseAbilityImmediately(Ability ability, int abilityLevel, int abilityIndex, bool isItem, Unit abilityTarget, Vector3 abilityLocation, bool interrupt = false)
        {
            // Do the last custom check of the ability before using it
            bool customCheck = true;
            if (abilityTarget) customCheck = ability.Check(this, this.owner, abilityLevel, abilityTarget);
            else if (abilityLocation != Vector3.zero) customCheck = ability.Check(this, this.owner, abilityLevel, abilityLocation);
            else customCheck = ability.Check(this, this.owner, abilityLevel);
            if (customCheck == false) return;

            // When server activates any ability, we send data to clients
            if (NetworkManager.Singleton.IsServer)
            {
                NetworkDataSync.Instance.AbilityUseSend(this, ability, abilityLevel, abilityIndex, isItem, abilityTarget, abilityLocation, interrupt);
            }

            // if interrupt we make unit visible
            if (interrupt && isInvisible) SetInvisibility(false);

            // Ability initiate. Умения-каналы (continuous) и их теневой кастер снесены блоком Б6 (2026-09-04):
            // каст всегда разовый — применили и вышли.
            if (abilityTarget) ability.Use(this, this.owner, abilityLevel, abilityTarget);
            else if (abilityLocation != Vector3.zero) ability.Use(this, this.owner, abilityLevel, abilityLocation);
            else ability.Use(this, this.owner, abilityLevel);

            // Reset the states after cast
            EndActiveAbility(true, false);
            // if interrupt we Idle() after cast
            if (interrupt && !NetworkConnectionHandler.isClient) Idle();

            // Cooldown
            // Откат — общей выборкой по уровням (Б8): нет строки — последняя заполненная (раньше уровень выше длины гасил откат).
            float levelCooldown = InterflowAbility.LevelValue(ability.cooldown, abilityLevel);
            if (levelCooldown != 0) ChangeAbilityCooldown(levelCooldown, abilityIndex, isItem);
            // Subtract costs
            SubtractAbilityItemCost(owner, abilityIndex, isItem);
            // Item charge decrease
            if (isItem) ItemChargesChange(abilityIndex);
        }

        // ============================= EVERY FRAME (регенерация) =============================

        // Переключатели снесены блоком Б6 (2026-09-04), ауры — блоком Б7 (2026-09-05): методов регистрации
        // умений в цикле юнита (AddEveryFrameAbility, RemoveEveryFrameIfExists, IndexOfEveryFrameAbility) больше нет.
        // Каждый тик у юнита остаётся только регенерация здоровья и маны.

        /// <summary>
        /// Every gameManager.Tick: health and mana regeneration.
        /// Имя осталось от цикла умений «каждый кадр»: переключатели снесены блоком Б6 (2026-09-04),
        /// ауры — блоком Б7 (2026-09-05); подписка на тик живёт в Unit.Init/Lifecycle и ConstructionUnit.
        /// </summary>
        public void HandleEveryFrameAbilities()
        {
            // MP/HP regeneration
            if (healthRegen != 0) ChangeHP(healthRegen * GameManager.Instance.currentDeltaTime, true);
            if (manaRegen != 0) ChangeMP(manaRegen * GameManager.Instance.currentDeltaTime, true);
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
                NetworkCommandSync.Instance.LevelUpAbility(this, ability, abilityIndex);
                return false;
            }

            // If leveling unit, ability not locked and still has not reached the maximum level
            if (levelingUnit && !abilityLocked[abilityIndex] && abilityLevel[abilityIndex] < abilities[abilityIndex].maxLevels - 1 && levelingUnit.abilityPoints > 0)
            {
                LevelUpAbility(ability, abilityIndex);

                // If level up successful server will inform clients that ability level has changed
                if (NetworkManager.Singleton.IsServer)
                {
                    NetworkDataSync.Instance.LevelUpAbilitySend(this, ability, abilityIndex);
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
            // Снимаем умение на СТАРОМ уровне и выдаём на новом (Б9): пассивка держит выданное состояние по уровню,
            // а её Unlock идемпотентен — без снятия она осталась бы на прежних числах.
            if (!ability.isItem) ability.Lock(this, this.owner, abilityLevel[abilityIndex]);

            abilityLevel[abilityIndex]++; // Increase ability level
            levelingUnit.abilityPoints--; // Decrease ability points

            AllAbilityLockLevelsCalculate();

            if (!ability.isItem) ability.Unlock(this, this.owner, abilityLevel[abilityIndex]); // Выдаём на новом уровне
        }

        /// <summary>
        /// Сервер: поставить улучшаемому умению героя сохранённый уровень (восстановление после смерти, блок Б9).
        /// Очки не тратит и пределов не проверяет — уровень уже был оплачен до гибели. Снимает старое состояние и выдаёт новое.
        /// </summary>
        /// <param name="abilityIndex">Глобальный индекс умения в пуле юнита.</param>
        /// <param name="level">Сохранённый уровень умения.</param>
        public void RestoreAbilityLevel(int abilityIndex, int level)
        {
            if (NetworkConnectionHandler.isClient) return;                       // состояние мира — сервер (правило 6)
            if (abilities == null || abilityIndex < 0 || abilityIndex >= abilityLevel.Length) return;
            if (level <= abilityLevel[abilityIndex]) return;                     // не ниже текущего: восстановление только вверх

            Ability ability = (abilityIndex < abilities.Length) ? abilities[abilityIndex] : null;
            if (ability == null || ability.isItem) return;

            ability.Lock(this, this.owner, abilityLevel[abilityIndex]);
            abilityLevel[abilityIndex] = level;

            AllAbilityLockLevelsCalculate();

            ability.Unlock(this, this.owner, level);

            // Клиент строит уровни умений из префаба и о восстановлении сам не узнал бы: его тултипы, значки и фильтр
            // прокачки разошлись бы с сервером навсегда (правило 6).
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && NetworkDataSync.Instance != null)
                NetworkDataSync.Instance.AbilityLevelSetSend(this, abilityIndex, level);
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
                    if (this == Presentation.Selection?.ActiveUnit && (SlotManager.Instance.debugMode || owner == SlotManager.Instance.currentPlayer)) Presentation.NotifyMsg("Wait until the cooldown is over.");
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
                cooldownAbility[i] -= GameManager.Instance.currentDeltaTime;
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

                // Уровень −1 у улучшаемых умений СНЕСЁН блоком Б9 (2026-09-06, целевая модель §8, решение Artsiom):
                // умение, открытое технологией, сразу работает на БАЗОВЫХ значениях (уровень 0) и видно в панели;
                // очко героя только улучшает (0 → 1 → 2). Раньше −1 значило «не выучено»: умение не работало и не показывалось.
                RecursiveFlagsSet(abilities);

                void RecursiveFlagsSet(Ability[] recursiveAbilities)
                {
                    for (int i = 0; i < recursiveAbilities.Length; i++)
                    {
                        if (recursiveAbilities[i].type == AbilityType.Container)
                        {
                            Container container = (Container)recursiveAbilities[i];
                            RecursiveFlagsSet(container.abilities);
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
                    // Lock state of the current ability. Открытие по уровню юнита (requiredLevel) снесено блоком Б8:
                    // замок зависит только от технологии.
                    if (abilityArray[i].requiredTech.Length > 0)
                    {
                        // Технология проверяется по ТЕКУЩЕМУ уровню умения (Б9): замок означает «умение недоступно»,
                        // а не «нельзя выучить следующий уровень» — учить больше нечего, очко только улучшает.
                        int lvl = abilityLevel[abilityIndex];

                        if (abilityArray[i].requiredTech.Length <= lvl || TechnologyManager.Instance.isUnlocked(abilityArray[i].requiredTech[lvl].data, owner)) // Technology check
                        {
                            // Ability is unlocked. Выдаём умение ровно в момент открытия (был заперт → открыт): иначе пассивка
                            // улучшаемого умения не включилась бы вовсе, а повторный вызов на каждое открытие технологии удвоил бы статы.
                            if (abilityLocked[abilityIndex] && !abilityArray[i].isItem)
                                abilityArray[i].Unlock(this, this.owner, abilityLevel[abilityIndex]);

                            abilityLocked[abilityIndex] = false;

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
                            // Технологию потеряли — снимаем ровно то, что выдали при открытии (парная выдаче строка выше,
                            // блок Б9): без этого статы и аура улучшаемого умения остались бы на юните навсегда,
                            // а повторное открытие выдало бы их второй раз.
                            if (!abilityLocked[abilityIndex] && !abilityArray[i].isItem)
                                abilityArray[i].Lock(this, this.owner, abilityLevel[abilityIndex]);

                            abilityLocked[abilityIndex] = true;

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
                        // Требований нет — умение доступно сразу и работает на базовых значениях (Б9). Выдаём тем же
                        // переходом «был заперт → открыт», что и ветка с технологией: иначе пассивка улучшаемого умения
                        // без требуемой технологии не включилась бы вовсе.
                        if (abilityLocked[abilityIndex] && !abilityArray[i].isItem)
                            abilityArray[i].Unlock(this, this.owner, abilityLevel[abilityIndex]);

                        abilityLocked[abilityIndex] = false;

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
                    if (abilityArray[i].requiredTech.Length > 0)
                    {
                        // For each requirement level check if requirements are met, if so unlock and levelup.
                        // Требование по уровню юнита (requiredLevel) снесено блоком Б8 — длина требований = длина списка технологий.
                        int requirementLength = abilityArray[i].requiredTech.Length;

                        for (int l = abilityLevel[abilityIndex]; l < requirementLength; l++)
                        {
                            if (TechnologyManager.Instance.isUnlocked(abilityArray[i].requiredTech[l].data, owner)) // Requirement tech check (l < requiredTech.Length по условию цикла)
                            {
                                // We call unlock. If the same level as current level we call unlock if it was locked before
                                if (!abilityArray[i].isItem && !(abilityLocked[abilityIndex] == false && l == abilityLevel[abilityIndex]))
                                {
                                    // We lock previous level if previous level exists
                                    if (l > 0)
                                    {
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
                    if (TechnologyManager.Instance.isUnlocked(abilityArray[i].requiredTech[abilityLevel[abilityIndex]].data, owner))
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

                // If ability level is not higher than max level. Ниже нуля не опускаем: уровень −1 снесён блоком Б9,
                // а maxLevels = 0 (умение без уровней) дал бы именно его.
                if (abilityArray[i].maxLevels - 1 > level)
                {
                    abilityLevel[abilityIndex] = level;
                }
                else abilityLevel[abilityIndex] = Mathf.Max(0, abilityArray[i].maxLevels - 1);

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

        // Метод SetActiveAbility (восстановление «активно исполняемого» умения из сохранения) СНЕСЁН
        // блоком Б6 (2026-09-04): он обслуживал только умения-каналы. Секция сохранения, которая его
        // звала, снесена там же — восстанавливать больше нечего.

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
            // Ветка завершения «активно исполняемого» умения (Deactivate + откат по обрыву) снесена блоком Б6
            // (2026-09-04) вместе с каналами: разовый каст ставит откат сам, в UseAbilityImmediately.

            // Reset the state
            if (reset)
            {
                OnCommand -= ResetAbilityState;
                if (activeAbilityUnit != null) activeAbilityUnit.OnReferenceChange -= AbilityUnitReferenceChange;

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
                NetworkDataSync.Instance.AbilityStopSend(this);
            }
        }

        // ============================= UTILS =============================

        /// <summary>
        /// Returns true if requirements (lock, resource cost) for the given ability are not met.
        /// Цены в мане у умений нет (блок Б5, 2026-09-04): мана — шкала готовности авто-умения, см. AutoAbilityUser.
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
                // No mana check: цена в мане снята (Б5)

                // No resource check for items
                // if (items[abilityIndex].cost.Length != 0)
                // {
                //     for (int i = 0; i < items[abilityIndex].cost[0].data.Length; i++)
                //     {
                //         if (!GameResources.Instance.CheckAmount(unitOwner, items[abilityIndex].cost[0].data[i]))
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

                // No mana check: цена в мане снята (Б5)

                // Check resources — цена по уровню общей выборкой (Б8): нет строки — последняя заполненная
                // (раньше уровень выше длины массива делал каст бесплатным).
                var levelCost = InterflowAbility.LevelItem(currentAbility.cost, abilityLevel[abilityIndex]);
                if (levelCost != null && levelCost.data != null)
                {
                    for (int i = 0; i < levelCost.data.Length; i++)
                    {
                        if (!GameResources.Instance.CheckAmount(player, levelCost.data[i]))
                        {
                            Presentation.NotifyMsg("Not enough " + levelCost.data[i].type.displayName, owner, true);
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
                // No mana subtraction: цена в мане снята (Б5)

                // No resource subtraction
                // if (items[abilityIndex].cost.Length != 0)
                // {
                //     for (int i = 0; i < items[abilityIndex].cost[0].data.Length; i++)
                //     {
                //         GameResources.Instance.ChangeAmount(unitOwner, items[abilityIndex].cost[0].data[i], 1, true);
                //     }
                // }
            }
            else
            {
                Ability currentAbility = Utils.GetAbilityByIndex(this, abilityIndex);

                // Цены в мане у умения нет (Б5, целевая модель §9). Мана — шкала готовности авто-умения: его
                // срабатывание забирает ВСЮ ману носителя. Именно здесь, а не в компоненте: это точка, где ядро
                // списывало цену ПО ФАКТУ срабатывания — прерванный каст ману не тратит (решение Artsiom 2026-09-04).
                // На клиенте SetMP только локальный (без синка), сервер пришлёт своё значение штатным каналом маны.
                if (TryGetComponent(out AutoAbilityUser autoUser) && autoUser.IsAutoAbility(currentAbility))
                    SetMP(0f);

                // Subtract resources — та же выборка по уровню, что в проверке (Б8).
                var levelCost = InterflowAbility.LevelItem(currentAbility.cost, abilityLevel[abilityIndex]);
                if (levelCost != null && levelCost.data != null)
                {
                    for (int i = 0; i < levelCost.data.Length; i++)
                    {
                        GameResources.Instance.ChangeAmount(player, levelCost.data[i], 1, true, true);
                    }
                }
            }
        }
    }
}
