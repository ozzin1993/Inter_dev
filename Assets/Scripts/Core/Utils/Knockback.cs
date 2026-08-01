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
        /// уважать иммунитет к контролю (B12) для отброса (заморозку Unit.Stun гейтит сам ассет). Только сервер.</summary>
        public static void Apply(Unit target, Vector3 sourcePos, float distance, float stunTime, bool respectControlImmunity)
        {
            if (NetworkConnectionHandler.isClient) return;                 // отброс/стан — только сервер (правило 6)
            if (target == null || target.dead) return;
            if (!target.canMove || target.agent == null) return;           // неподвижных/строения/без агента не двигаем

            // Иммунитет к контролю (B12): по флагу пропустить отброс (заморозку Stun гейтит ассет всегда).
            if (respectControlImmunity && target.TryGetComponent<ControlImmunity>(out ControlImmunity ci) && ci.Active)
                return;

            Vector3 tp = target.transform.position;
            Vector3 dir = tp - sourcePos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = target.transform.forward;   // позиции совпали → толкаем «вперёд»
            dir.Normalize();

            Vector3 desired = tp + dir * distance;
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, NavSampleMaxDistance, NavMesh.AllAreas))
                target.agent.Warp(hit.position);                          // валидная точка на меше; иначе — не двигаем (§6)

            if (stunTime > 0f) target.Stun(stunTime);                     // Unit.Stun сам уважает ControlImmunity
        }
    }
}
