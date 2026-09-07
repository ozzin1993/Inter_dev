using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    public class Crit : Ability
    {
        // Multiplies the damage when the unit attacks the target

        public override AbilityType type { get { return AbilityType.Passive; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("Chance of critical attack. 0 = 0%, 1 = 100%")]
        public float[] critChance;

        [Tooltip("Multiplier of the damage. 0 = 0%, 1 = 100%")]
        public float[] critMultiplier;

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            // Add to callbacks
            InterflowAbility.CallbackAdd(unit.OnDamageDealModifyCallbacks, this, level, CritApply);
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            // Remove from callbacks
            InterflowAbility.CallbackRemove(unit.OnDamageDealModifyCallbacks, this, level);
        }

        public float CritApply(Unit unit, int level, float dmg, bool directAttack)
        {
            // If an attack is not caused by unit attacking, but by ability/effector we do not apply critical hit
            if (!directAttack) return dmg;

            // Random chance to apply a critical hit
            if (!NetworkConnectionHandler.isClient)
            {
                // Only server should apply chance ability
                if (Random.value < InterflowAbility.LevelValue(critChance, level))
                {
                    // Check if target is eligible
                    if (!UnitSelector.IsUnitCompatible(unit.owner, unit.target, unitSelector)) return dmg;

                    if (unit.target)
                    {
                        // Create floating text
                        FloatingText.Spawn(-1, unit.target.transform.position + new Vector3(0, unit.target.unitHeight, 0), "CRT!", Color.red, true);

                        // Set the HP sync for this tick
                        if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.ForceSync();
                    }
                    else
                    {
                        // Target is ground
                        // Create floating text
                        FloatingText.Spawn(-1, unit.targetPosition, "CRT!", Color.red, true);
                    }

                    // Apply critical hit multiplier
                    return dmg * InterflowAbility.LevelValue(critMultiplier, level);
                }
            }

            return dmg;
        }
    }
}
