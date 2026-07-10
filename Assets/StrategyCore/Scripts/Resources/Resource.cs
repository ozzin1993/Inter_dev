using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    [CreateAssetMenu(fileName = "Resource", menuName = "StrategyCore/Resources/Create")]
    public class Resource : ScriptableObject
    {
        [Header("Text")]
        public string displayName;
        public Texture2D icon;
        [Tooltip("Resource color. Currently used for coloring created floating texts when resources are unloaded.")]
        public Color resourceColor;
        [TextArea(5, 10)]
        public string description;

        [Header("Limited")]
        [Tooltip("Is this resource limited. For example, food. Every unit while it is alive will take some food and when limit is reached you will not be able to train new units")]
        public bool limited;
        [Tooltip("Maximum limit of this resource. If limited. Can be 0(no limit)")]
        public int maxLimit;
    }
}
