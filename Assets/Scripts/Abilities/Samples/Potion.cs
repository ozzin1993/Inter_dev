using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Asset menu
    [CreateAssetMenu(fileName = "Potion", menuName = "StrategyCore/Abilities/Potion")]
    public class Potion : Ability
    {
        // Level 0 means first level. If you want to damage based on level do not forget to add 1. Example: (damage * (level + 1)
        public override AbilityType type { get { return AbilityType.Active; } } // Specify type

        [Header("Ability specific")]
        public float health;
        public float mana;

        // This function is called when the ability is clicked
        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            if (health != 0) castingUnit.ChangeHP(health);
            if (mana != 0) castingUnit.ChangeMP(mana);
        }
    }
}
