using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    // Unit.AbilityLocks.cs — замки и уровни (INITIALIZATION). Вырезано 1:1 из Unit.Ability.cs (разрезка на partial-ы, правило 22).
    public partial class Unit
    {
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
    }
}
