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
        [Min(0),Tooltip("0 — телепорт; больше нуля — плавный рывок с эффектами по прибытии.")] public float travelSeconds;
        [Min(0)] public float leapHeight;
        [Tooltip("Optional animator state held during travel.")] public string travelAnimationState;
        [Min(0),Tooltip("Пройти дальше выбранной цели на это расстояние.")] public float passThroughDistance;
        public CompositeSkill trailSkill;
        [Min(.3f)] public float trailSpacing=1;
    }

    [System.Serializable]
    public class SkillKnockbackBlock
    {
        public bool enabled;
        [Tooltip("Дальность отбрасывания в метрах.")] public float distance=1.5f;
        [Tooltip("Максимальный тир отбрасываемой цели; 0 — любой.")] public int maxTier;
        [Tooltip("Разрешённые роли; пусто — любые.")] public Unit.UnitCategory[] categories;
        public bool respectControlImmunity=true;
        public bool alongCasterFacing;
        [Min(0),Tooltip("0 preserves instant displacement; positive values move the target over this duration.")] public float travelSeconds;
        public Unit[] targetPrefabs;
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
        void ApplyKnockback(Unit target,Vector3 origin,Unit caster)
        {
            if(knockback==null||!knockback.enabled||target==null||target.dead)return;
            if(knockback.maxTier>0&&target.tier>knockback.maxTier)return;
            if(!CategoryAllowed(target,knockback.categories))return;
            if(knockback.targetPrefabs!=null&&knockback.targetPrefabs.Length>0&&!System.Array.Exists(knockback.targetPrefabs,p=>p&&p.unitTypeID==target.unitTypeID))return;
            if(knockback.alongCasterFacing&&caster)origin=target.transform.position-caster.LookDirection;
            if(knockback.travelSeconds>0){
                Vector3 direction=target.transform.position-origin;direction.y=0;
                if(direction.sqrMagnitude<.0001f)direction=target.LookDirection;
                SkillDisplacement.Begin(target,target.transform.position+direction.normalized*knockback.distance,knockback.travelSeconds,knockback.respectControlImmunity);
            }else Knockback.Apply(target,origin,knockback.distance,0,knockback.respectControlImmunity);
        }
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
