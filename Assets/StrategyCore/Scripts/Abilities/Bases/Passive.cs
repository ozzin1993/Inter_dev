using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    [CreateAssetMenu(fileName = "Passive", menuName = "StrategyCore/Abilities/Passive")]
    public class Passive : Ability
    {
        public override AbilityType type { get { return AbilityType.Passive; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("Passive Effects for each level")]
        public AbilityPassiveEffects[] passiveEffects;

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            passiveEffects[level].AddEffect(unit);
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            passiveEffects[level].RemoveEffect(unit);
        }
    }
}
