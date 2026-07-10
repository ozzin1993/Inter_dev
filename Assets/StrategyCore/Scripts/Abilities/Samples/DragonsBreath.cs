using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    [CreateAssetMenu(fileName = "DragonsBreath", menuName = "StrategyCore/Abilities/Dragon`s Breath")]
    public class DragonsBreath : Ability
    {
        // Launches the wall of fire that damages units in cone shaped area
        // Level 0 means first level. If you want to damage based on level do not forget to add 1. Example: (damage * (level + 1)

        public override AbilityType type { get { return AbilityType.Location; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("VFX element of the ability")]
        public ParticleSystem vfx;
        [Space]
        [Tooltip("Damage for each level of the ability")]
        public float[] damage;
        [Tooltip("Damage type for each level of the ability")]
        public DamageType[] damageType;
        [Tooltip("Range of the ability for damaging enemies")]
        public float[] range;

        public override void Use(Unit castingUnit, int castingPlayer, int level, Vector3 position)
        {
            Vector3 dir = (position - castingUnit.transform.position).normalized;

            // Check if casting unit is visible
            if (FogOfWar.instance.IsVisible(castingUnit.FoWCell, SlotManager.instance.currentTeam) && vfx)
            {
                // Instantiate VFX
                ParticleSystem temp = Instantiate(vfx, castingUnit.transform.position, Quaternion.LookRotation(dir));
                var main = temp.main;
                main.startSpeed = range[level];
            }

            // Angle (35f) for correct visuals should match the angle of the VFX
            Unit[] enemies = Utils.GetUnitsInCone(new Vector2(castingUnit.transform.position.x, castingUnit.transform.position.z), new Vector2(dir.x, dir.z), 35f, range[level], castingUnit.owner, unitSelector);

            for (int i = 0; i < enemies.Length; i++)
            {
                // We deal damage in 0.1s
                if (castingUnit) enemies[i].GetDamageIn(castingUnit.owner, castingUnit, 0.1f, damage[level], damageType[level]);
                //castingUnit.DealDamage(enemies[i], damage[level], damageType[level], false, Vector3.zero);
            }
        }
    }
}
