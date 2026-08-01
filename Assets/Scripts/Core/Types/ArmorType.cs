using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    [CreateAssetMenu(fileName = "ArmorType", menuName = "StrategyCore/ArmorType/Create")]
    public class ArmorType : ScriptableObject
    {
        [HideInInspector] public int index; // Index from 0 to N

        [ArmorTypeID]
        public int id;

        [Header("Text")]
        public string displayName;
        public Texture2D icon;
        [TextArea(5, 10)]
        public string description;
    }
}

