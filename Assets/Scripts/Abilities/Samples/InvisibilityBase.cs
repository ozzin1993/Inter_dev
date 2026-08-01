using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.UI.GridLayoutGroup;

namespace StrategyCore
{
    [CreateAssetMenu(fileName = "Invisibility", menuName = "StrategyCore/Abilities/Invisibility")]
    public class InvisibilityBase : Ability
    {
        // Makes the casting unit invisible, it adds the Effector that makes the unit invisible

        public override AbilityType type { get { return AbilityType.Active; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("Choose invisbility effector, for each level of ability")]
        public Effector[] invisilibtyEffector;

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            Effector.EffectorAdd(castingUnit, invisilibtyEffector[level], null, castingUnit.owner);
        }
    }
}
