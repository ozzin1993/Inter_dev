using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    [CreateAssetMenu(fileName = "DamageType", menuName = "StrategyCore/DamageType/Create")]
    public class DamageType : ScriptableObject
    {
        [HideInInspector] public int index; // Index from 0 to N

        [DamageTypeID]
        public int id;

        [Tooltip("Урон этого типа обходит числовую броню. Таблица типов брони и модификаторы урона продолжают работать.")]
        public bool ignoresArmor;

        [Header("Text")]
        public string displayName;
        public Texture2D icon;
        [TextArea(5, 10)]
        public string description;

        [Tooltip("Define how much damage is dealt against various armor types. Normalized: 1 = 100%. By default is 1 against all type of armors")]
        [Header("Effectivness")]
        public DamageToArmor[] damageEffectivness;
    }
}
