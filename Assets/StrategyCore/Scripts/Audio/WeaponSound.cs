using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Defines the hit sound for the unit depending on the armor type of the target

    [CreateAssetMenu(fileName = "WeaponSound", menuName = "StrategyCore/Audio/WeaponSound")]
    public class WeaponSound : ScriptableObject
    {
        [Tooltip("Define the hit sound for the unit depending on the armor type of the target")]
        public AttackToArmorSound[] attackToArmorSound;
        [Tooltip("Define the hit sound when ground is targeted.\nAlso used for armor types that were not specified in AttackToArmorSound")]
        public AudioClip[] groundHitClips;

        // This is rearranged AttackToArmorSound (Done in GameManager)
        [HideInInspector] public AttackToArmorSound[] weaponSound;
    }

    [System.Serializable]
    public class AttackToArmorSound
    {
        public ArmorType armorType;
        public AudioClip[] audioClips;
    }
}
