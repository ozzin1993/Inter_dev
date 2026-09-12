using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    // Unit.AbilityCooldown.cs — откаты умений (COOLDOWN). Вырезано 1:1 из Unit.Ability.cs (разрезка на partial-ы, правило 22).
    public partial class Unit
    {
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
    }
}
