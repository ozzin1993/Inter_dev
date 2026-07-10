using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using StrategyCore;

namespace StrategyCore
{
    // Defines a unit that has additional attributes that can change its parameters.

    public class AttributeUnit : MonoBehaviour
    {
        [Tooltip("Main attribute of the unit has specific behaviour, it increases the attack damage of the unit. To change this behaviour you have to modify the script Attribute.cs:ChangeUnitParameters(...)")]
        public Attribute mainAttribute;
        [Tooltip("Attributes of the unit and their initial values")]
        public AttributeWrapper[] unitAttributes;

        // Technical
        private Unit thisUnit;

        // Called by Initialize() of thisUnit
        public void Initialize()
        {
            thisUnit = GetComponent<Unit>();

            for (int i = 0; i < unitAttributes.Length; i++)
            {
                bool main = false;
                if (mainAttribute == unitAttributes[i].attribute) main = true;
                unitAttributes[i].attribute.ChangeUnitParameters(thisUnit, 0, unitAttributes[i].value, main);
            }
        }

        // Change specified attribute by value
        public void ChangeAttribute(Attribute attribute, float amount)
        {
            for (int i = 0; i < unitAttributes.Length; i++)
            {
                if (attribute == unitAttributes[i].attribute)
                {
                    // If main
                    bool main = false;
                    if (mainAttribute == unitAttributes[i].attribute) main = true;

                    // Value calculation
                    float oldValue = unitAttributes[i].value;
                    unitAttributes[i].value += amount;

                    // Influence calculation
                    unitAttributes[i].attribute.ChangeUnitParameters(thisUnit, oldValue, unitAttributes[i].value, main);

                    break;
                }
            }
        }

        // Change specified attribute by percentage
        public void ChangeAttribute(Attribute attribute, float percentage, bool percentageChange)
        {
            for (int i = 0; i < unitAttributes.Length; i++)
            {
                if (attribute == unitAttributes[i].attribute)
                {
                    // If main
                    bool main = false;
                    if (mainAttribute == unitAttributes[i].attribute) main = true;

                    // Value calculation
                    float oldValue = unitAttributes[i].value;
                    if (percentage < 0)
                    {
                        unitAttributes[i].value /= (1 - percentage);
                    }
                    else
                    {
                        unitAttributes[i].value *= (1 + percentage);
                    }

                    // Influence calculation
                    unitAttributes[i].attribute.ChangeUnitParameters(thisUnit, oldValue, unitAttributes[i].value, main);

                    break;
                }
            }
        }
    }
}
