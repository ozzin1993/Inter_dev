using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Attribute wrapper is a simple wrapper for holding Attribute:Value pair

    [System.Serializable]
    public class AttributeWrapper
    {
        public Attribute attribute;
        public float value;
    }

    // Attribute wrapper is a simple wrapper for holding Attribute:Value pair, in float for percentages
    [System.Serializable]
    public class AttributeWrapperFloat
    {
        public Attribute attribute;
        public float value = 1;

        public AttributeWrapperFloat(Attribute attribute, float value = 1)
        {
            this.attribute = attribute;
            this.value = value;
        }
    }
}
