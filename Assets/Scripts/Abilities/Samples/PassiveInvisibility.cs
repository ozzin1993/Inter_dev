using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    public class PassiveInvisibility : Ability
    {
        public override AbilityType type { get { return AbilityType.Passive; } } // Specify type

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            unit.SetInvisibility(true);
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            
        }
    }
}
