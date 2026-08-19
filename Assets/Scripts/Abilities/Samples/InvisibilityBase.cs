using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.UI.GridLayoutGroup;

namespace StrategyCore
{
    public class InvisibilityBase : Ability
    {
        // Makes the casting unit invisible, it adds the Effector that makes the unit invisible

        public override AbilityType type { get { return AbilityType.Active; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("Choose invisbility effector, for each level of ability")]
        public Effector[] invisilibtyEffector;

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            // Защита от контент-ошибки: эффектор для уровня не заполнен — умение не срабатывает (блок «баги и корректность»)
            if (invisilibtyEffector == null || level < 0 || level >= invisilibtyEffector.Length || invisilibtyEffector[level] == null)
            {
                Debug.LogWarning($"[InvisibilityBase] {name}: эффектор невидимости для уровня {level} не заполнен — пропуск");
                return;
            }
            Effector.EffectorAdd(castingUnit, invisilibtyEffector[level], null, castingUnit.owner);
        }
    }
}
