using UnityEngine;

namespace StrategyCore
{
    // Transfers life from enemy unit to the casting unit

    public class LifeTransfer : Ability
    {
        // Transfers the health points from the target to the caster. Current implementation does not kill unit, since health regen is changed. See Use(...)

        public override AbilityType type { get { return AbilityType.Unit; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("VFX Line of the ability")]
        public VFXLine vfxLine;

        [Tooltip("Amount of life transferred, per second")]
        public float[] amount;

        public override void Activate(Unit castingUnit, int castingPlayer, int level, Unit unit, ref VFXReferencer vfxStorage)
        {
            VFXLine temp = VFXLine.CreateVFX(vfxLine, castingUnit, 1);
            vfxStorage = temp.GetComponent<VFXReferencer>();

            // Transfer HP
            if (unit) unit.ChangeHealthRegen(-amount[level]);
            if (castingUnit) castingUnit.ChangeHealthRegen(amount[level]);
        }

        public override void Deactivate(Unit castingUnit, int castingPlayer, int level, Unit unit, ref VFXReferencer vfxStorage)
        {
            // Destroy vfx element
            if (vfxStorage)
            {
                Destroy(vfxStorage.gameObject);
            }

            // Transfer HP
            if (unit) unit.ChangeHealthRegen(amount[level]);
            if (castingUnit) castingUnit.ChangeHealthRegen(-amount[level]);
        }

        public override void Use(Unit castingUnit, int castingPlayer, int level, Unit unit, ref VFXReferencer vfxStorage)
        {
            // Update vfx position
            vfxStorage.vfxLine.SetTarget(unit);

            // Transfer HP - current implementation does not kill unit, since health regen is changed. Use this instead if you want transfer of hp to kill
            // unit.ChangeHP(-amount[level] * Time.deltaTime, castingUnit);
            // castingUnit.ChangeHP(amount[level] * Time.deltaTime);
        }
    }
}
