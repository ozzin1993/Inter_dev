using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace StrategyCore
{
    public class Basher : Ability
    {
        // Stuns the target when the unit attacks it

        public override AbilityType type { get { return AbilityType.Passive; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("Chance of bash attack. 0 = 0%, 1 = 100%")]
        public float[] bashChance;

        [Tooltip("If should multiply the damage")]
        public float[] bashMultiplier;

        [Tooltip("For how long the target should get stunned")]
        public float[] stunTime;

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            // Add to callbacks
            InterflowAbility.CallbackAdd(unit.OnAfterDamageDealCallbacks, this, level, BashApply);
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            // Remove from callbacks
            InterflowAbility.CallbackRemove(unit.OnAfterDamageDealCallbacks, this, level);
        }

        public void BashApply(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack, DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level)
        {
            // If an attack is not caused by unit attacking, but by ability/effector we do not apply bash
            if (!directAttack || !UnitSelector.IsUnitCompatible(byOwner, targetUnit, unitSelector)) return;

            // Random chance to apply a basher
            if (!NetworkConnectionHandler.isClient)
            {
                // Only server should apply chance ability
                if (Random.value < InterflowAbility.LevelValue(bashChance, level))
                {
                    targetUnit.target.Stun(InterflowAbility.LevelValue(stunTime, level), byUnit, byOwner);

                    // Apply damage multiplier — общая выборка по уровням (Б8): нет строки — последняя заполненная.
                    float bashMul = InterflowAbility.LevelValue(bashMultiplier, level);
                    if (bashMul > 0f)
                    {
                        DamagePacket packet = DamagePacket.Create(dmg * bashMul, damageType, byOwner, byUnit, true, this);   // [Interflow fix 2026-09-04 damage-full-packet] пакет одной записи
                        targetUnit.GetDamage(in packet, out _);
                        if (NetworkManager.Singleton.IsServer)
                        {
                            // Set the HP sync for this tick
                            NetworkDataSync.Instance.ForceSync();
                        }
                    }
                }
            }
        }
    }
}
