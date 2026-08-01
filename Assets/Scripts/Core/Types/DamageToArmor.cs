using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // DamageToArmor wrapper is a simple wrapper for holding ArmorType:Value pair

    [System.Serializable]
    public class DamageToArmor
    {
        public ArmorType type;
        public float value;
    }

    public class DamageToArmorWrapper
    {
        public DamageType damageType;
        public ArmorType armorType;

        public DamageToArmorWrapper(DamageType _damageType, ArmorType _armorType)
        {
            damageType = _damageType;
            armorType = _armorType;
        }
    }
}

