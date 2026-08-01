using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    [CreateAssetMenu(fileName = "FlameRing", menuName = "StrategyCore/Abilities/Flame Ring")]
    public class FlameRing : Ability
    {
        // Deals damage to units in the specified Area, based on UnitSelector
        // Level 0 means first level. If you want to damage based on level do not forget to add 1. Example: (damage * (level + 1)

        public override AbilityType type { get { return AbilityType.Area; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("VFX element of the ability")]
        public VFXReferencer VFX;

        [Space]
        [Tooltip("Damage per second for each level of the ability")]
        public float[] dps;
        [Tooltip("Damage type for each level of the ability")]
        public DamageType[] damageType;

        public override void Activate(Unit castingUnit, int castingPlayer, int level, Vector3 position, ref VFXReferencer vfxStorage)
        {
            // Initiate vfx element
            if (VFX)
            {
                // Scale, rotation must be set according to the VFX used
                vfxStorage = Instantiate(VFX, position + new Vector3(0, 0.1f, 0), Quaternion.identity);
                vfxStorage.transform.SetGlobalScale(new Vector3(radius[level], radius[level], radius[level]));

                Projector proj = vfxStorage.GetComponent<Projector>();
                proj.nearClipPlane = -radius[level];
                proj.farClipPlane = radius[level];
                proj.orthographicSize = radius[level];
            }
        }

        public override void Deactivate(Unit castingUnit, int castingPlayer, int level, Vector3 position, ref VFXReferencer vfxStorage)
        {
            // Destroy vfx element
            if (vfxStorage)
            {
                Destroy(vfxStorage.gameObject);
            }
        }

        public override void Use(Unit castingUnit, int castingPlayer, int level, Vector3 position, ref VFXReferencer vfxStorage)
        {
            // Get all units inside casted area
            foreach (var unit in Utils.GetUnitsInRadius(new Vector2(position.x, position.z), radius[level], castingUnit.owner, unitSelector))
            {
                castingUnit.DealDamage(unit, dps[level] * Time.deltaTime, damageType[level], false, Vector3.zero);
            }
        }
    }
}
