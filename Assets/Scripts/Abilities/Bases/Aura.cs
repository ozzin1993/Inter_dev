using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Asset menu
    // [CreateAssetMenu(fileName = "Aura", menuName = "StrategyCore/Abilities/Aura")]
    public class Aura : Ability
    {
        // Auras are not mana/resource checked - use Toggle type for that.
        // Level 0 means first level. If you want to damage based on level do not forget to add 1. Example: (damage * (level + 1)
        public override AbilityType type { get { return AbilityType.Aura; } } // Specify type

        // This function is called when aura is unlocked - we must add it to every frame abilities
        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            unit.AddEveryFrameAbility(this, -1, isItem, level);
        }

        // This function is called when aura is locked - we must remove from every frame abilities
        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            unit.RemoveEveryFrameIfExists(this, isItem, level);
        }

        // This function is called every GameManager.tick
        // You can get the tick delta time - GameManager.currentDeltaTime
        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            // Get units in radius
            Unit[] units = Utils.GetUnitsInRadius(new Vector2(castingUnit.transform.position.x, castingUnit.transform.position.z), radius[level], castingUnit.owner, unitSelector);

            for (int i = 0; i < units.Length; i++)
            {
                // Do something with affected units
                // Effector.EffectorAdd(units[i], EFFECTOR, castingUnit, unit.owner);
            }
        }
    }
}
