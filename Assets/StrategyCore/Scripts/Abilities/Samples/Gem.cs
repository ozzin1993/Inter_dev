using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.UI.GridLayoutGroup;

namespace StrategyCore
{
    [CreateAssetMenu(fileName = "Gem", menuName = "StrategyCore/Abilities/Gem(See invisible units)")]
    public class Gem : Ability
    {
        // Allows the carrier of this ability to see invisible units, based on UnitSelector

        public override AbilityType type { get { return AbilityType.Aura; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("Effector that makes the unit seen even if invisible, when applied")]
        public Effector canBeSeeEffector;

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            unit.AddEveryFrameAbility(this, -1, isItem, level);
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            unit.RemoveEveryFrameIfExists(this, isItem, level);
        }

        // Aura`s use is called by the ability holder unit, we should gather units around and add effectors based on selector
        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            // Get units in radius
            Unit[] units = Utils.GetUnitsInRadius(new Vector2(castingUnit.transform.position.x, castingUnit.transform.position.z), castingUnit.visionRange, castingUnit.owner, unitSelector);

            for (int i = 0; i < units.Length; i++)
            {
                // For performance reasons you might want to uncomment the code below
                // It is commented because we do not want to following unit to stop following when the target becomes invisible.
                // Since reveal is added to all units, it will not become invisible. If we would add to only invisible it would be not visible till the effector is added which is once per GameManager.Tick
                // if (!units[i].isInvisible) continue;
                Effector.EffectorAdd(units[i], canBeSeeEffector, null, castingUnit.owner);
            }
        }
    }
}
