using UnityEngine;

namespace StrategyCore
{
    // Active-умение героя «Удар Синода»: удар молотом по КОНУСУ перед героем — физ. урон всем врагам + оглушение.
    // Кастуется из таблицы умений героя (кастер = герой). Урон/стан — штатные Unit.DealDamage / Unit.Stun.
    // Конуса в ассете нет → фильтр по углу от forward героя (наш код). Серверо-авторитетно (правило 6). Ассет не тронут.
    [CreateAssetMenu(fileName = "SynodStrikeActive", menuName = "StrategyCore/Abilities/SynodStrikeActive")]
    public class SynodStrikeActive : Ability
    {
        public override AbilityType type { get { return AbilityType.Active; } }

        [Header("Ability specific")]
        [Tooltip("Физический урон всем врагам в конусе. По ТЗ — 45.")]
        public float damage = 45f;

        [Tooltip("Тип урона (по ТЗ — физический). Ассет из Resources/DamageType.")]
        public DamageType damageType;

        [Tooltip("Длительность оглушения врагов, сек. По ТЗ — 1.2.")]
        public float stunSeconds = 1.2f;

        [Tooltip("Полный угол конуса перед героем, градусы (90 = ±45° от взгляда). ≥360 — круг без учёта направления.")]
        public float coneAngle = 90f;

        // База Ability: radius[level] — дальность конуса; cooldown[level] — КД (по ТЗ 9с);
        // unitSelector — кого бьём (враги; наземные/воздушные по нужде). castTime/duration не используются.

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            if (NetworkConnectionHandler.isClient) return; // урон/стан — только сервер (правило 6)
            if (castingUnit == null) return;
            if (damageType == null)
            {
                Debug.LogWarning("[SynodStrike] Не задан damageType в ассете умения — удар пропущен. Назначь DamageType (напр. физический) в Inspector.");
                return;
            }

            float reach = (radius != null && radius.Length > 0) ? radius[Mathf.Clamp(level, 0, radius.Length - 1)] : 0f;
            if (reach <= 0f) { Debug.LogWarning("[SynodStrike] radius (дальность конуса) не задан — удар пропущен."); return; }

            Vector3 origin = castingUnit.transform.position;
            Unit[] targets = Utils.GetUnitsInRadius(new Vector2(origin.x, origin.z), reach, castingUnit.owner, unitSelector);
            if (targets == null) return;

            Vector3 forward = castingUnit.transform.forward; forward.y = 0f;
            float halfAngle = coneAngle * 0.5f;
            bool fullCircle = coneAngle >= 360f;

            int hitCount = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                Unit t = targets[i];
                if (t == null || t.dead) continue;

                if (!fullCircle)
                {
                    Vector3 dir = t.transform.position - origin; dir.y = 0f;
                    if (dir.sqrMagnitude > 0.0001f && Vector3.Angle(forward, dir) > halfAngle) continue; // вне конуса
                }

                castingUnit.DealDamage(t, damage, damageType, false, origin); // false: это способность, не прямая атака
                if (stunSeconds > 0f) t.Stun(stunSeconds);
                hitCount++;
            }

            Debug.Log($"[SynodStrike] player={castingPlayer}: конус {coneAngle}°, r={reach}, задето {hitCount}.");
        }
    }
}
