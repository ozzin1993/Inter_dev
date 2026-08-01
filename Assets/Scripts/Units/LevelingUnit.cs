using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace StrategyCore
{
    // Defines a leveling unit that can gain XP and level up to learn new abilities, increase its parameters and attributes.
    // With clients we sync only when unit levels up

    public class LevelingUnit : MonoBehaviour
    {
        [Header("Effects")]
        [Tooltip("Sound that plays when the unit levels up")]
        public AudioClip sound;
        [Tooltip("Visual effect that is played when the unit levels up")]
        public VFXReferencer VFX;

        [Header("Leveling")]
        [Tooltip("Current level of the unit")]
        public int level = 1;
        [Tooltip("Maximum possible level of the unit. Leave at 1 if this unit needs no leveling")]
        public int maxLevel = 1;
        [Tooltip("Experience required to go from first level to second")]
        public int expRequired = 500;
        [Tooltip("How much more experience will be required to level up after each level. It multiplies the expRequired")]
        public float expDifficultyIncrease = 1.15f;

        [Header("Parameter changes")]
        [Tooltip("Defines what unit parameters should also change when the unit levels up")]
        public AbilityPassiveEffects passiveEffects;

        // Technical
        private Unit thisUnit;
        private int baseXP;
        [HideInInspector] public int currentExp = 0;
        [HideInInspector] public int abilityPoints = 0; // Points that are gathered when unit levels up and can be used to learn or level up ability
        [HideInInspector] public int previousExpRequired = 0;

        public Action OnXPChange;

        /// <summary>
        /// Initializes the necessary leveling parameters and sets the starting level.
        /// </summary>
        public void Initialize()
        {
            thisUnit = GetComponent<Unit>();

            // Initial levelup calculation
            baseXP = expRequired;
            if (level > 1)
            {
                for (int i = 0; i < level - 1; i++)
                {
                    LevelUp(true);
                }
            }

            // If melee no attack range change
            if (thisUnit.melee)
            {
                passiveEffects.attackRangeChange = 0;
                passiveEffects.attackRangePercentageChange = 0;
            }
        }

        /// <summary>
        /// Change the XP of the unit. Used to increase xp when units are killed.
        /// </summary>
        /// <param name="amount">XP change amount.</param>
        /// <param name="sync">Should server sync with clients.</param>
        /// <returns>XP that was not used when max level is reached, otherwise the xp amount given.</returns>
        public int ChangeExp(int amount, bool sync = true)
        {
            int leftXp = amount; // XP that was not used

            if (level < maxLevel)
            {
                currentExp += amount;

                if (!NetworkConnectionHandler.isClient)
                {
                    while (currentExp >= expRequired)
                    {
                        LevelUp(false, true, sync);
                        if (level < maxLevel)
                        {
                            previousExpRequired = expRequired;
                            expRequired += (int)(baseXP * expDifficultyIncrease);
                        }
                        else
                        {
                            // Max level
                            leftXp = currentExp - expRequired;
                            currentExp = expRequired;
                            break;
                        }
                    }
                }

                OnXPChange?.Invoke();
                return leftXp;
            }
            else
            {
                return leftXp;
            }
        }

        /// <summary>
        /// Sets the XP to the particular value. Note: We can only increase from the current xp to the given xp.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="sync">Should server sync with clients.</param>
        /// <returns>XP that was not used when max level is reached, otherwise the xp amount given.</returns>
        public int SetExp(int value, bool sync = true)
        {
            return ChangeExp(value - currentExp, sync);
        }

        /// <summary>
        /// Sets the level and xp directly.
        /// </summary>
        /// <param name="lvl">Level of the unit.</param>
        /// <param name="exp">XP of the unit.</param>
        /// <param name="points">Ability points of the unit.</param>
        /// <param name="levelUp">Should this unit level up on clients.</param>
        /// <param name="sync">Should server sync with clients.</param>
        /// <param name="noVFX">When true does not play level up sound and VFX.</param>
        public void SetLevel(int lvl, int exp, int points, bool levelUp = false, bool sync = true, bool noVFX = false)
        {    
            while (level < lvl)
            {
                LevelUp(noVFX, levelUp, sync);
                if (level < maxLevel)
                {
                    previousExpRequired = expRequired;
                    expRequired += (int)(baseXP * expDifficultyIncrease);
                }
                else
                {
                    // Max level
                    currentExp = expRequired;
                    break;
                }
            }
          
            currentExp = exp;
            abilityPoints = points;
            OnXPChange?.Invoke();
        }

        /// <summary>
        /// What happens when units level up.
        /// </summary>
        /// <param name="noVFX">When true does not play level up sound and VFX.</param>
        /// <param name="levelUp">Should this unit level up on clients.</param>
        /// <param name="sync">Should server sync with clients.</param>
        private void LevelUp(bool noVFX = false, bool levelUp = false, bool sync = true)
        {
            // Only can be called by server
            if (!levelUp) return;

            passiveEffects.AddEffect(thisUnit);

            // Levelable abilities
            abilityPoints++;
            level++;

            // Recalculate ability lock states
            thisUnit.AllAbilityLockLevelsCalculate();
            thisUnit.OnRedrawAbilityView?.Invoke();

            if (!noVFX && thisUnit.FoWVisible)
            {
                // Effects
                if (sound) Presentation.Audio?.PlaySoundClip(sound, this.transform, 1);
                if (VFX) thisUnit.AddVFX(VFX);
            }

            // Sync with clients
            if (sync && NetworkManager.Singleton.IsServer && !thisUnit.xpSync)
            {
                thisUnit.xpSync = true;
                NetworkDataSync.instance.xpChangedUnits.Add(thisUnit.netID);
                NetworkDataSync.instance.onXPCleared += thisUnit.XPSyncFalse;
            }
        }
    }
}
