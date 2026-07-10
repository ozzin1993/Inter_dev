using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Helps for unit to keep track of changes done by effectors or passive abilities. For percentage parameter changes
    public class PassiveAppliedEffects
    {
        // List of currently applied percentage changes. 1 == 100%, means no change
        public float damageChange = 1;
        public float attackSpeedChange = 1;
        public float attackRangeChange = 1;

        public float armorChange = 1;
        public float moveSpeedChange = 1;

        public float healthChange = 1;
        public float healthRegenChange = 1;
        public float manaChange = 1;
        public float manaRegenChange = 1;

        public float xpRewardChange = 1;

        public List<AttributeWrapperFloat> attributeChange;

        // Returns true if passive effects are in effect
        public static bool CheckIfApplied(PassiveAppliedEffects pe)
        {
            if (pe.damageChange != 1) return true;
            if (pe.attackSpeedChange != 1) return true;
            if (pe.attackRangeChange != 1) return true;

            if (pe.armorChange != 1) return true;
            if (pe.moveSpeedChange != 1) return true;

            if (pe.healthChange != 1) return true;
            if (pe.healthRegenChange != 1) return true;
            if (pe.manaChange != 1) return true;
            if (pe.manaRegenChange != 1) return true;

            if (pe.xpRewardChange != 1) return true;

            // Attribute changes
            if (pe.attributeChange != null)
            {
                for (int i = 0; i < pe.attributeChange.Count; i++)
                {
                    if (pe.attributeChange[i].value != 1) return true;
                }
            }

            return false;
        }
    }

    // Class that can be reused when changes in parameters of the unit are needed. Used by passive abilities, attributes and effectors
    [System.Serializable]
    public class AbilityPassiveEffects
    {
        [Header("Attack change")]
        [Tooltip("Change of attack damage of the unit")]
        public float damageChange;
        [Tooltip("Change of attack speed of the unit, percentage wise. \n-0.5 = -50%, 1 = +100%. \nShould never be -1!")]
        public float damagePercentageChange = 0;
        [Tooltip("Change of attack speed of the unit")]
        public float attackSpeedChange;
        [Tooltip("Change of attack speed of the unit, percentage wise. \n-0.5 = -50%, 1 = +100%. \nShould never be -1!")]
        public float attackSpeedPercentageChange = 0;
        [Tooltip("Change of attack range of the unit")]
        public float attackRangeChange;
        [Tooltip("Change of attack range of the unit, percentage wise. \n-0.5 = -50%, 1 = +100%. \nShould never be -1!")]
        public float attackRangePercentageChange = 0;

        [Header("Armor change")]
        [Tooltip("Change in armor points of the unit")]
        public float armorChange;
        [Tooltip("Change in armor points of the unit, percentage wise. \n-0.5 = -50%, 1 = +100%. \nShould never be -1!")]
        public float armorPercentageChange = 0;

        [Header("Movement change")]
        [Tooltip("Change in move speed of the unit")]
        public float moveSpeedChange;
        [Tooltip("Change in move speed of the unit, percentage wise. \n-0.5 = -50%, 1 = +100%. \nShould never be -1!")]
        public float moveSpeedPercentageChange = 0;

        [Header("HP/MP change")]
        [Tooltip("Change in max health points of the unit")]
        public float healthChange;
        [Tooltip("Change in max health points of the unit, percentage wise. \n-0.5 = -50%, 1 = +100%. \nShould never be -1!")]
        public float healthPercentageChange = 0;
        [Tooltip("Change in regeneration of the HP")]
        public float healthRegenChange;
        [Tooltip("Change in regeneration of the HP, percentage wise. \n-0.5 = -50%, 1 = +100%. \nShould never be -1!")]
        public float healthRegenPercentageChange = 0;
        [Tooltip("Change in max mana points of the unit")]
        public float manaChange;
        [Tooltip("Change in max mana points of the unit, percentage wise. \n-0.5 = -50%, 1 = +100%. \nShould never be -1!")]
        public float manaPercentageChange = 0;
        [Tooltip("Change in regeneration of the MP")]
        public float manaRegenChange;
        [Tooltip("Change in regeneration of the MP, percentage wise. \n-0.5 = -50%, 1 = +100%. \nShould never be -1!")]
        public float manaRegenPercentageChange = 0;

        [Header("Other")]
        [Tooltip("Change in vision range of the unit")]
        public int visionChange;
        [Tooltip("Change in xp reward points for killing this unit")]
        public float xpRewardChange;
        [Tooltip("Change in xp reward points for killing this unit, percentage wise. \n-0.5 = -50%, 1 = +100%. \nShould never be -1!")]
        public float xpRewardPercentageChange = 0;

        [Header("Attribute changes")]
        [Tooltip("Changes the attributes of the unit if it has attributes")]
        public AttributeWrapper[] attributeChanges;
        [Tooltip("Change in armor points of the unit, percentage wise. \n-0.5 = -50%, 1 = +100%. \nShould never be -1!")]
        public AttributeWrapperFloat[] attributePercentageChange;

        // Adds the parameters to the unit
        // Multiplier and attributeON are used by AttributeUnit
        public void AddEffect(Unit unit, float multiplier = 1, bool attributeON = true)
        {
            // Absolute values
            if (damageChange != 0) unit.ChangeDamage(damageChange * multiplier);
            if (attackSpeedChange != 0) unit.ChangeAttackSpeed(attackSpeedChange * multiplier);
            if (attackRangeChange != 0) unit.ChangeAttackRange(attackRangeChange * multiplier);

            if (armorChange != 0) unit.ChangeArmor(armorChange * multiplier);

            if (moveSpeedChange != 0) unit.ChangeMoveSpeed(moveSpeedChange * multiplier);

            if (healthChange != 0) unit.ChangeMaxHP(healthChange * multiplier);
            if (healthRegenChange != 0) unit.ChangeHealthRegen(healthRegenChange * multiplier);
            if (manaChange != 0) unit.ChangeMaxMP(manaChange * multiplier);
            if (manaRegenChange != 0) unit.ChangeManaRegen(manaRegenChange * multiplier);

            if (visionChange != 0) unit.ChangeVisionRange(visionChange);
            if (xpRewardChange != 0) unit.ChangeXpReward(xpRewardChange * multiplier);

            // Percentages
            if (damagePercentageChange != 0) unit.ChangeDamage(damagePercentageChange * multiplier, false);
            if (attackSpeedPercentageChange != 0) unit.ChangeAttackSpeed(attackSpeedPercentageChange * multiplier, false);
            if (attackRangePercentageChange != 0) unit.ChangeAttackRange(attackRangePercentageChange * multiplier, false);

            if (armorPercentageChange != 0) unit.ChangeArmor(armorPercentageChange * multiplier, false);

            if (moveSpeedPercentageChange != 0) unit.ChangeMoveSpeed(moveSpeedPercentageChange * multiplier, false);

            if (healthPercentageChange != 0) unit.ChangeMaxHP(healthPercentageChange * multiplier, false);
            if (healthRegenPercentageChange != 0) unit.ChangeHealthRegen(healthRegenPercentageChange * multiplier, false);
            if (manaPercentageChange != 0) unit.ChangeMaxMP(manaPercentageChange * multiplier, false);
            if (manaRegenPercentageChange != 0) unit.ChangeManaRegen(manaRegenPercentageChange * multiplier, false);

            if (xpRewardPercentageChange != 0) unit.ChangeXpReward(xpRewardPercentageChange * multiplier, false);

            // Attributes
            if (attributeON && unit.attributeUnit)
            {
                // Absolutes
                if (attributeChanges != null)
                {
                    for (int i = 0; i < attributeChanges.Length; i++)
                    {
                        unit.attributeUnit.ChangeAttribute(attributeChanges[i].attribute, attributeChanges[i].value * multiplier);
                    }
                }

                // Percentages
                if (attributePercentageChange != null)
                {
                    for (int i = 0; i < attributePercentageChange.Length; i++)
                    {
                        bool attributeFound = false;
                        for (int l = 0; l < unit.passiveEffects.attributeChange.Count; l++)
                        {
                            if (attributePercentageChange[i].attribute == unit.passiveEffects.attributeChange[l].attribute)
                            {
                                unit.passiveEffects.attributeChange[l].value *= attributePercentageChange[i].value * multiplier;
                                unit.attributeUnit.ChangeAttribute(attributeChanges[i].attribute, attributeChanges[i].value * multiplier, false);

                                attributeFound = true;
                                break;
                            }
                        }

                        if (!attributeFound)
                        {
                            unit.passiveEffects.attributeChange.Add(new AttributeWrapperFloat(attributePercentageChange[i].attribute, attributePercentageChange[i].value * multiplier));
                        }
                    }
                }
            }
        }

        // Removes the parameters from the unit
        // Multiplier and attributeON are used by AttributeUnit
        public void RemoveEffect(Unit unit, float multiplier = 1, bool attributeON = true)
        {
            // Reverse absolute effect
            if (damageChange != 0) unit.ChangeDamage(-damageChange * multiplier);
            if (attackSpeedChange != 0) unit.ChangeAttackSpeed(-attackSpeedChange * multiplier);
            if (attackRangeChange != 0) unit.ChangeAttackRange(-attackRangeChange * multiplier);

            if (armorChange != 0) unit.ChangeArmor(-armorChange * multiplier);

            if (moveSpeedChange != 0) unit.ChangeMoveSpeed(-moveSpeedChange * multiplier);

            if (healthChange != 0) unit.ChangeMaxHP(-healthChange * multiplier);
            if (healthRegenChange != 0) unit.ChangeHealthRegen(-healthRegenChange * multiplier);
            if (manaChange != 0) unit.ChangeMaxMP(-manaChange * multiplier);
            if (manaRegenChange != 0) unit.ChangeManaRegen(-manaRegenChange * multiplier);

            if (visionChange != 0) unit.ChangeVisionRange(-visionChange);
            if (xpRewardChange != 0) unit.ChangeXpReward(-xpRewardChange * multiplier);

            // Reverse Percentage effect
            if (damagePercentageChange != 0) unit.ChangeDamage(-damagePercentageChange * multiplier, true);
            if (attackSpeedPercentageChange != 0) unit.ChangeAttackSpeed(-attackSpeedPercentageChange * multiplier, true);
            if (attackRangePercentageChange != 0) unit.ChangeAttackRange(-attackRangePercentageChange * multiplier, true);

            if (armorPercentageChange != 0) unit.ChangeArmor(-armorPercentageChange * multiplier, true);

            if (moveSpeedPercentageChange != 0) unit.ChangeMoveSpeed(-moveSpeedPercentageChange * multiplier, true);

            if (healthPercentageChange != 0) unit.ChangeMaxHP(-healthPercentageChange * multiplier, true);
            if (healthRegenPercentageChange != 0) unit.ChangeHealthRegen(-healthRegenPercentageChange * multiplier, true);
            if (manaPercentageChange != 0) unit.ChangeMaxMP(-manaPercentageChange * multiplier, true);
            if (manaRegenPercentageChange != 0) unit.ChangeManaRegen(-manaRegenPercentageChange * multiplier, true);

            if (xpRewardPercentageChange != 0) unit.ChangeXpReward(-xpRewardPercentageChange * multiplier, true);

            // Attributes
            if (attributeON && unit.attributeUnit)
            {
                // Absolutes
                if (attributeChanges != null)
                {
                    for (int i = 0; i < attributeChanges.Length; i++)
                    {
                        unit.attributeUnit.ChangeAttribute(attributeChanges[i].attribute, -attributeChanges[i].value * multiplier);
                    }
                }

                // Percentages
                if (attributePercentageChange != null)
                {
                    for (int i = 0; i < attributePercentageChange.Length; i++)
                    {
                        for (int l = 0; l < unit.passiveEffects.attributeChange.Count; l++)
                        {
                            if (attributePercentageChange[i].attribute == unit.passiveEffects.attributeChange[l].attribute)
                            {
                                unit.passiveEffects.attributeChange[l].value /= attributePercentageChange[i].value * multiplier;
                                unit.attributeUnit.ChangeAttribute(attributeChanges[i].attribute, -attributeChanges[i].value * multiplier, true);

                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
