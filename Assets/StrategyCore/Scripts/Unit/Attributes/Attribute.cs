using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace StrategyCore
{
    [CreateAssetMenu(fileName = "Attribute", menuName = "StrategyCore/Attribute/Create")]

    public class Attribute : ScriptableObject
    {
        [Header("Text")]
        public string displayName;
        public Texture2D icon;
        [TextArea(5, 10)]
        public string description;

        [Header("Attribute Parameters")]
        public AbilityPassiveEffects passiveEffects;

        // Change unit parameters based on attribute change. Called by attribute unit at the start and when it levels up or changes the value of its attribute.
        public void ChangeUnitParameters(Unit unit, float oldValue, float newValue, bool mainAttribute = false)
        {
            // Attributes contribute with additive scaling, so we must first remove the effect and then add it back with the new value

            // Remove previous percentage increase
            passiveEffects.RemoveEffect(unit, oldValue, true);
            // Add new percentage increase
            passiveEffects.AddEffect(unit, newValue, true);

            // MAIN ATTRIBUTE: In this example it changes the damage
            if (mainAttribute) unit.ChangeDamage(newValue - oldValue);
        }
    }
}
