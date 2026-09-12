using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    public class EffectorAura : Ability
    {
        public override AbilityType type { get { return AbilityType.Aura; } }

        [Header("Effector Aura Settings")]
        [Tooltip("Effectors to apply to units in radius")]
        public Effector[] effectors;
        public bool originalFormOnly;
        [Tooltip("Короткий эффектор на самом источнике ауры, например купол границы.")] public Effector casterEffector;
        
        [Tooltip("VFX to show on the caster while aura is active")]
        public VFXReferencer auraVFX;

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            unit.AddEveryFrameAbility(this, -1, isItem, level);
            if (auraVFX != null)
            {
                unit.AddVFX(auraVFX, false, true);
            }
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            unit.RemoveEveryFrameIfExists(this, isItem, level);
            if (auraVFX != null)
            {
                unit.RemoveVFX(auraVFX);
            }
        }

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            if(!castingUnit||castingUnit.dead||(originalFormOnly&&castingUnit.polymorphed))return;
            if(casterEffector)Effector.EffectorAdd(castingUnit,casterEffector,castingUnit,castingPlayer);
            // Get units in radius
            Unit[] units = Utils.GetUnitsInRadius(new Vector2(castingUnit.transform.position.x, castingUnit.transform.position.z), radius[level], castingUnit.owner, unitSelector);

            for (int i = 0; i < units.Length; i++)
            {
                // Apply all effectors to each unit in range
                for (int j = 0; j < effectors.Length; j++)
                {
                    // Duration for aura effectors should be short as they are refreshed every tick
                    // Effector.EffectorAdd handles resetting duration if already present
                    Effector.EffectorAdd(units[i], effectors[j], castingUnit, castingUnit.owner);
                }
            }
        }
    }
}
