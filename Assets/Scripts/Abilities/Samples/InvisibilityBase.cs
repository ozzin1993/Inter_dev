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
            // Эффектор — общей выборкой по уровням (Б8): нет строки — последняя заполненная. Пустой массив — умение не срабатывает.
            Effector effector = InterflowAbility.LevelItem(invisilibtyEffector, level);
            if (effector == null)
            {
                Debug.LogWarning($"[InvisibilityBase] {name}: эффектор невидимости не заполнен — пропуск");
                return;
            }
            Effector.EffectorAdd(castingUnit, effector, null, castingUnit.owner);
        }
    }
}
