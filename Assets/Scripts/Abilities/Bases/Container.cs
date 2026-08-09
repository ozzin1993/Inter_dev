using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    public class Container : Ability
    {
        public override AbilityType type { get { return AbilityType.Container; } } // Specify type

        // Before proceeding with the ability, we must ensure several parameters are set correctly
        public override void Init() // Called in awake method of GameManager. Awake of scriptableObject is broken.
        {
            base.Init(); // Run Ability class Awake function.

            // We should know if this container contains item abilities. If true then we set isShop of the ability to true
            isShop = RecursiveShopCheck(abilities);

            bool RecursiveShopCheck(Ability[] recursiveAbilities)
            {
                for (int i = 0; i < recursiveAbilities.Length; i++)
                {
                    if (recursiveAbilities[i].isItem)
                    {
                        return true;
                    }
                    else if (recursiveAbilities[i].type == AbilityType.Container)
                    {
                        Container container = (Container)recursiveAbilities[i];

                        if (RecursiveShopCheck(container.abilities)) return true;
                    }
                }
                return false;
            }
        }

        // ABILITY ------------------------------------------------------------------------------------------------------------------------

        public Ability[] abilities;
        [HideInInspector] public bool isShop; // If it contains any item abilities we should know about it
    }
}

