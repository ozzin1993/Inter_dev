using UnityEngine;

namespace StrategyCore
{
    public class Hex : Ability
    {
        public override AbilityType type { get { return AbilityType.Unit; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("Visuals of this unit will be used to transform the caster.")]
        public Unit hexUnit;
        [Tooltip("Duration of hex.")]
        public float[] hexTime;

        public override void Use(Unit castingUnit, int castingPlayer, int level, Unit unit)
        {
            // No buildings - to not interfere with UpgradeBuilding
            unit.Idle();

            // Muted and Disarmed can be set directly, but must be manually turned off
            unit.disarmed = true;
            unit.muted = true;
            
            unit.Polymorph(this, level, InterflowAbility.LevelValueOrZero(hexTime, level), hexUnit);
        }

        public override void Deactivate(Unit castingUnit, int castingPlayer, int level)
        {
            // Allow to attack and cast
            castingUnit.disarmed = false;
            castingUnit.muted = false;

            // Return back to original shape
            castingUnit.RestoreRenderers();
        }
    }
}
