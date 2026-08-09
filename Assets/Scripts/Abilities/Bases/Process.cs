using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace StrategyCore
{
    // Asset menu
    public class Process : Ability
    {
        // This is a base class for process abilities
        // CAST TIME parameter - for processes it is amount of time needed to process
        public override AbilityType type { get { return AbilityType.Process; } } // Specify type

        // This function is called once when process when process is finished
        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            
        }
    }
}
