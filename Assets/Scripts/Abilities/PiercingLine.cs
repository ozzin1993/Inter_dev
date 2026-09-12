using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Пронзающий выстрел: снаряд поражает всех на прямой между стрелком и целью
    /// (и, при желании, продолжает за целью).
    /// Потребитель: Тир 4 [Б] Баллиста «Гнев Ордена» — гарпуны прошивают цели по прямой.
    ///
    /// В ассете такой механики нет: <c>Projectile</c> проверяет попадание один раз в конце траектории,
    /// а сплеш и рикошет бьют по кругу и по цепочке, но не по линии.
    /// Реализация без правки ядра: штатный хук атакующего <c>OnAfterDamageDealCallbacks</c> —
    /// зная стрелка и цель, считаем коридор и добираем остальных. Визуал снаряда остаётся штатным.
    /// </summary>
    public class PiercingLine : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        [Header("Пронзание по прямой")]
        [Tooltip("Ширина коридора поражения в метрах. Цель считается задетой, если её центр ближе половины этой ширины к линии выстрела.")]
        public float corridorWidth = 1.5f;

        [Tooltip("На сколько метров линия продолжается ЗА основную цель. 0 — только до неё.")]
        public float extraDistanceBeyondTarget = 0f;

        [Tooltip("Урон задетым по линии как доля от урона основного удара, по уровням. 1 = столько же, 0.5 = половина.")]
        public float[] damagePercent = new float[1] { 1f };

        [Tooltip("Максимум дополнительно задетых целей. 0 — без ограничения.")]
        [Min(0)]
        public int maxExtraTargets = 0;

        [Tooltip("Накладывать задетым эффекторы атаки носителя (как обычной цели).")]
        public bool applyAttackEffectors = true;

        [Tooltip("Считать только прямые атаки. ВЫКЛ — пронзание пойдёт и от урона способностей.")]
        public bool onlyDirectAttack = true;

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
                        InterflowAbility.CallbackAdd(unit.OnAfterDamageDealCallbacks, this, level, PierceApply);
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            InterflowAbility.CallbackRemove(unit.OnAfterDamageDealCallbacks, this, level);
        }

        void PierceApply(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack,
                         DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level)
        {
            if (NetworkConnectionHandler.isClient) return; // правило 6
            if (byUnit == null) return;
            if (onlyDirectAttack && !directAttack) return;

            Vector3 endPoint = targetUnit != null ? targetUnit.transform.position : targetPosition;

            Vector2 from = new Vector2(byUnit.transform.position.x, byUnit.transform.position.z);
            Vector2 to = new Vector2(endPoint.x, endPoint.z);
            Vector2 dir = to - from;

            float length = dir.magnitude;
            if (length <= 0.01f) return;

            dir /= length;
            length += Mathf.Max(0f, extraDistanceBeyondTarget);

            // Кандидатов ищем в круге, накрывающем всю линию, затем отсеиваем по коридору
            Vector2 center = from + dir * (length * 0.5f);
            float searchRadius = length * 0.5f + corridorWidth;

            Unit[] candidates = Utils.GetUnitsInRadius(center, searchRadius, byUnit.owner, unitSelector, -1, targetUnit);
            if (candidates == null || candidates.Length == 0) return;

            float bonusDamage = dmg * LevelValue(damagePercent, level, 1f);
            if (bonusDamage <= 0f) return;

            float halfWidth = corridorWidth * 0.5f;
            int hit = 0;

            for (int i = 0; i < candidates.Length; i++)
            {
                Unit u = candidates[i];
                if (u == null || u.dead || u == targetUnit || u == byUnit) continue;

                Vector2 p = new Vector2(u.transform.position.x, u.transform.position.z) - from;

                float along = Vector2.Dot(p, dir);          // проекция на линию выстрела
                if (along < 0f || along > length) continue;  // позади стрелка или дальше конца линии

                float offset = Mathf.Abs(p.x * dir.y - p.y * dir.x); // расстояние до линии
                if (offset > halfWidth) continue;

                DamagePacket packet = DamagePacket.Create(bonusDamage, damageType, byOwner, byUnit, false, this);   // [Interflow fix 2026-09-04 damage-full-packet] пакет одной записи
                u.GetDamage(in packet, out float _);
                if (applyAttackEffectors && effectors != null && effectors.Length > 0) Effector.EffectorAdd(byUnit, u, effectors);

                hit++;
                if (maxExtraTargets > 0 && hit >= maxExtraTargets) break;
            }

            if (hit > 0) AbilityFacts.Proc(this, byUnit);   // [2026-09-10] показ срабатывания — см. AbilityFacts
            if (hit > 0) RequestForceSync();
        }

    }
}
