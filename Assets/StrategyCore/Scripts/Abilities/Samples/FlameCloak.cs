using UnityEngine;

namespace StrategyCore
{
    [CreateAssetMenu(fileName = "FlameCloak", menuName = "StrategyCore/Abilities/FlameCloak")]
    public class FlameCloak : Ability
    {
        // Deals damage to units around the caster, based on UnitSelector
        // Level 0 means first level. If you want to damage based on level do not forget to add 1. Example: (damage * (level + 1)

        public override AbilityType type { get { return AbilityType.Toggle; } } // Specify type

        [Header("Ability specific")]
        public float[] damagePerSecond;
        public DamageType damageType;
        public VFXReferencer VFX;

        public override void Activate(Unit castingUnit, int castingPlayer, int level)
        {
            if (VFX != null)
            {
                VFXReferencer vfx = castingUnit.AddVFX(VFX, false, true);
                vfx.transform.SetGlobalScale(new Vector3(castingUnit.unitRadius * 2f + radius[level], castingUnit.unitHeight, castingUnit.unitRadius * 2f + radius[level]));
            }
        }

        public override void Deactivate(Unit castingUnit, int castingPlayer, int level)
        {
            if (VFX != null)
            {
                castingUnit.RemoveVFX(VFX);
            }
        }

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            // This ability gets units in radius and damages them.

            // Get units in radius
            Unit[] units = Utils.GetUnitsInRadius(new Vector2(castingUnit.transform.position.x, castingUnit.transform.position.z), castingUnit.unitRadius + radius[level], castingUnit.owner, unitSelector, -1, castingUnit);

            for (int i = 0; i < units.Length; i++)
            {
                castingUnit.DealDamage(units[i], damagePerSecond[level] * GameManager.instance.currentDeltaTime, damageType, false, Vector3.zero);
            }
        }
    }
}
