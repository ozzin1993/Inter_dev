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

            float duration = InterflowAbility.LevelValue(hexTime, level);

            // [Interflow fix 2026-09-03 control-as-effectors] Немота и безоружие — служебные СОСТОЯНИЯ
            // на срок превращения, а не прямая запись флагов (решение Artsiom 03.09.2026): флаги контроля
            // выводятся только из списка состояний, прямую запись стёр бы первый же пересчёт.
            // Снимать их вручную больше не надо — истекают вместе с превращением.
            unit.Disarm(duration, castingUnit, castingPlayer);
            unit.Mute(duration, castingUnit, castingPlayer);

            unit.Polymorph(this, level, duration, hexUnit);
        }

        public override void Deactivate(Unit castingUnit, int castingPlayer, int level)
        {
            // [Interflow fix 2026-09-03 control-as-effectors] Ручное снятие немоты и безоружия убрано:
            // это служебные состояния, они истекают сами.

            // Return back to original shape
            castingUnit.RestoreRenderers();
        }
    }
}
