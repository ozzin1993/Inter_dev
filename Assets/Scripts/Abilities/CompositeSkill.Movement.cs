using UnityEngine;

namespace StrategyCore
{
    // ================== КОНСТРУКТОР СКИЛЛА: БЛОКИ ПЕРЕМЕЩЕНИЯ (правило 22 — партиал по фиче) ==
    // Два блока: рывок ЦЕЛИ к кастеру и перемещение САМОГО КАСТЕРА в точку приложения.
    // Оба идут через общий хелпер Knockback (правило 2: перемещение по NavMesh уже реализовано там).

    /// <summary>2. Рывок цели вплотную к кастеру.</summary>
    [System.Serializable]
    public class SkillPullBlock
    {
        [Tooltip("Включить блок: цель мгновенно перемещается вплотную к кастеру.")]
        public bool enabled;

        [Tooltip("Зазор между габаритами кастера и цели, метры. 0 — впритык. " +
                 "Габариты берутся у NavMeshAgent обоих, поэтому цель не окажется внутри кастера.")]
        public float gap;
    }

    /// <summary>17. Перемещение кастера в точку приложения (телепорт).</summary>
    [System.Serializable]
    public class SkillCasterMoveBlock
    {
        [Tooltip("Включить блок: кастер мгновенно переносится в точку приложения умения. " +
                 "Точку задаёт режим цели: «умный выбор точки» — выбранное место, «умный выбор юнита» — позиция цели.")]
        public bool enabled;

        [Tooltip("Уважать иммунитет к контролю у самого кастера. " +
                 "ВЫКЛ — кастер перемещается даже под иммунитетом (это его собственное умение, а не контроль извне).")]
        public bool respectControlImmunity;
    }

    public partial class CompositeSkill
    {
        /// <summary>
        /// Рывок цели к кастеру. Возвращает false, если цель не сдвинулась: иммунитет к контролю,
        /// неподвижный юнит или здание, нет валидной точки на NavMesh.
        ///
        /// Решение Artsiom 2026-08-06: несостоявшийся рывок ОТСЕКАЕТ цель от остальных блоков этого каста —
        /// умение считается исполненным (откат и мана списаны штатно), но по этой цели не делает ничего.
        /// </summary>
        bool ApplyPull(Unit castingUnit, Unit target)
        {
            if (pull == null || !pull.enabled) return true;   // блок выключен — цель обрабатываем как обычно
            if (castingUnit == null || target == null) return false;

            Vector3 point = Knockback.PointNextTo(castingUnit, target, pull.gap);

            return Knockback.MoveTo(target, point, true);     // иммунитет к контролю спасает от рывка
        }

        /// <summary>
        /// Перемещение кастера в точку приложения. Один раз за каст, целей не касается.
        /// Неудача (нет точки на NavMesh, кастер неподвижен) молча пропускается: остальные блоки уже отработали.
        /// </summary>
        void ApplyCasterMove(Unit castingUnit, Vector3 point)
        {
            if (casterMove == null || !casterMove.enabled) return;
            if (castingUnit == null) return;

            Knockback.MoveTo(castingUnit, point, casterMove.respectControlImmunity);
        }
    }
}
