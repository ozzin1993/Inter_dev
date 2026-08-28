using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Кирпич B9 — нокбэк: мгновенно отбросить цель от источника на distance (юниты Unity) вдоль луча.
    // Статик-хелпер (переиспользуют B2 и абилки, правило 5). Путь: точка = pos + dir*distance → NavMesh.SamplePosition
    // → agent.Warp; опц. Stun. ТОЛЬКО сервер (правило 6); позиция расходится штатным синком NetworkDataSync.
    // ⚠️ Синк Warp-позиции и воздушные (airReplica) — проверить в Unity (непроверяемо в песочнице). Ассет не трогаем (правило 1).
    public static class Knockback
    {
        // Насколько далеко искать валидную точку NavMesh около расчётной (не выкинуть за меш).
        const float NavSampleMaxDistance = 2f;

        /// <summary>Отбросить target от sourcePos на distance; опц. заморозка stunTime. respectControlImmunity —
        /// уважать иммунитет к контролю (B12) для отброса (заморозку гейтит приёмник, куда уходит Unit.Stun). Только сервер.</summary>
        public static void Apply(Unit target, Vector3 sourcePos, float distance, float stunTime, bool respectControlImmunity)
        {
            if (NetworkConnectionHandler.isClient) return;                 // отброс/стан — только сервер (правило 6)
            if (target == null || target.dead) return;
            if (!target.canMove || target.agent == null) return;           // неподвижных/строения/без агента не двигаем

            // Иммунитет к контролю (B12): по флагу пропустить отброс (заморозку Stun гейтит приёмник всегда).
            // Шаг 1 схемы «пакет и приёмник» (§3.1): спрашиваем приёмник цели, а не ищем компонент
            // на каждый отброс — у приёмника найденный ControlImmunity закеширован.
            // Флаг проверяется ПЕРВЫМ: при false приёмник не запрашивается и не создаётся.
            // Проверки выше уже отсеяли мёртвых, поэтому обращение к приёмнику здесь безопасно.
            if (respectControlImmunity && target.ReceiverEnsure().ControlImmune)
                return;

            Vector3 tp = target.transform.position;
            Vector3 dir = tp - sourcePos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = target.transform.forward;   // позиции совпали → толкаем «вперёд»
            dir.Normalize();

            Vector3 desired = tp + dir * distance;
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, NavSampleMaxDistance, NavMesh.AllAreas))
                target.agent.Warp(hit.position);                          // валидная точка на меше; иначе — не двигаем (§6)

            if (stunTime > 0f) target.Stun(stunTime);                     // Unit.Stun уходит в тот же приёмник и там спрашивает иммунитет
        }

        /// <summary>
        /// Переставить target В ЗАДАННУЮ точку (рывок к кастеру, телепорт). Возвращает true, только если
        /// юнит действительно сдвинулся: неподвижные, без агента, с иммунитетом к контролю и случай
        /// «нет валидной точки на NavMesh» дают false — вызывающая сторона решает, что делать дальше.
        /// Только сервер (правило 6); позиция расходится штатным синком NetworkDataSync.
        /// </summary>
        public static bool MoveTo(Unit target, Vector3 destination, bool respectControlImmunity)
        {
            if (NetworkConnectionHandler.isClient) return false;
            if (target == null || target.dead) return false;
            if (!target.canMove || target.agent == null) return false;

            // Тот же вопрос приёмнику, что и в Apply (§3.1 схемы): флаг — первым, мёртвые отсеяны выше.
            if (respectControlImmunity && target.ReceiverEnsure().ControlImmune)
                return false;

            if (!NavMesh.SamplePosition(destination, out NavMeshHit hit, NavSampleMaxDistance, NavMesh.AllAreas))
                return false;

            target.agent.Warp(hit.position);
            return true;
        }

        /// <summary>
        /// Точка вплотную к кастеру со стороны цели: от кастера в сторону цели на сумму габаритов
        /// обоих агентов плюс заданный зазор. Радиус берём у NavMeshAgent — это тот же габарит,
        /// которым навигация разводит юнитов, поэтому притянутый не окажется внутри кастера.
        /// </summary>
        public static Vector3 PointNextTo(Unit caster, Unit target, float gap)
        {
            if (caster == null) return target != null ? target.transform.position : Vector3.zero;

            Vector3 cp = caster.transform.position;
            if (target == null) return cp;

            Vector3 dir = target.transform.position - cp;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = caster.transform.forward;  // стоят в одной точке
            dir.Normalize();

            float casterRadius = caster.agent != null ? caster.agent.radius : 0f;
            float targetRadius = target.agent != null ? target.agent.radius : 0f;

            return cp + dir * (casterRadius + targetRadius + Mathf.Max(0f, gap));
        }
    }
}
