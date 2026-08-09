using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    public class Evasion : Ability
    {
        // Allows the unit to evade the upcoming direct attacks receiving no damage. DirectAttack means the damage was caused by the enemy unit`s attack, not by ability or effector.

        public override AbilityType type { get { return AbilityType.Passive; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("Chance of evasion. 0 = 0%, 1 = 100%")]
        public float[] chance;

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            // Add to callbacks
            unit.OnBeforeGetDamageCallbacks.Add(new DamageModifyCallback
            {
                Callback = EvasionApply,
                Ability = this,
                Level = level
            });
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            // Remove from callbacks
            for (int i = 0; i < unit.OnBeforeGetDamageCallbacks.Count; i++)
            {
                var c = unit.OnBeforeGetDamageCallbacks[i];
                if (c.Ability == this && c.Level == level)
                {
                    unit.OnBeforeGetDamageCallbacks.RemoveAt(i);
                    break;
                }
            }
        }

        public float EvasionApply(Unit unit, int level, float dmg, bool directAttack)
        {
            // If an attack is not caused by unit attacking, but by ability/effector we do not evade it
            if (!directAttack) return dmg;

            // Random chance to evade an attack
            if (!NetworkConnectionHandler.isClient)
            {
                // Only server should apply chance ability
                if (Random.value < chance[level])
                {
                    return 0;
                }
            }

            return dmg;
        }
    }
}
