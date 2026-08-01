using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    [CreateAssetMenu(fileName = "Technology", menuName = "StrategyCore/Technology/Create")]

    public class Technology : ScriptableObject
    {
        // Technology that is unlocked by Upgrade abilities
        [TechnologyID]
        public int id;

        public string displayName;
        [TextArea(5, 10)]
        public string description;

        [Tooltip("Is this technology shared between allies. If true only one player needs to research it to unlock for the team")]
        public bool shared;
    }
}
