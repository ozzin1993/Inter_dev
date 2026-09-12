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
            AbilityFacts.Granted(this, unit, level);   // [2026-09-10] показ срабатывания — см. AbilityFacts
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            
        }
    }
}
