using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Перенос эффекта на соседнюю цель: ударив врага, с шансом накладывает эффекторы
    /// ещё и на ближайшего к нему другого врага (микро-AoE через заражение).
    /// Потребитель: Тир 1 [Б] «Очищающий огонь» (горение перекидывается на соседа).
    ///
    /// Реализация: штатный хук атакующего <c>OnAfterDamageDealCallbacks</c> — в нём есть и цель,
    /// и набор эффекторов атаки. Бросок шанса делает только сервер (как штатные Crit/Evasion).
    /// </summary>
    public class EffectorSpread : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        [Header("Перенос эффекта на соседа")]
        [Tooltip("Шанс переноса при попадании, по уровням. 0.3 = 30%.")]
        [Range(0f, 1f)]
        public float[] spreadChance = new float[1] { 0.3f };

        [Tooltip("Радиус вокруг ПОРАЖЁННОЙ ЦЕЛИ, в котором ищется сосед для переноса.")]
        public float[] spreadRadius = new float[1] { 2f };

        [Tooltip("Сколько соседей задевает перенос. 1 — только ближайший.")]
        [Min(1)]
        public int neighbourCount = 1;

        [Tooltip("Переносить эффекторы АТАКИ носителя (те, что реально были наложены на цель). ВЫКЛ — переносить только список ниже.")]
        public bool spreadAttackEffectors = true;

        [Tooltip("Дополнительные эффекторы, накладываемые соседу. Пусто — только эффекторы атаки.")]
        public Effector[] extraEffectors;

        [Tooltip("Считать только прямые атаки. ВЫКЛ — перенос пойдёт и от урона способностей носителя.")]
        public bool onlyDirectAttack = true;

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
                        InterflowAbility.CallbackAdd(unit.OnAfterDamageDealCallbacks, this, level, SpreadApply);
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            InterflowAbility.CallbackRemove(unit.OnAfterDamageDealCallbacks, this, level);
        }

        void SpreadApply(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack,
                         DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level)
        {
            if (NetworkConnectionHandler.isClient) return; // бросок шанса — только сервер
            if (targetUnit == null || byUnit == null) return;
            if (onlyDirectAttack && !directAttack) return;

            float chance = LevelValue(spreadChance, level, 0f);
            if (chance <= 0f || Random.value >= chance) return;

            float searchRadius = LevelValue(spreadRadius, level, 0f);
            if (searchRadius <= 0f) return;

            Vector2 center = new Vector2(targetUnit.transform.position.x, targetUnit.transform.position.z);
            Unit[] neighbours = Utils.GetUnitsInRadius(center, searchRadius, byUnit.owner, unitSelector, -1, targetUnit);
            if (neighbours == null || neighbours.Length == 0) return;

            int applied = 0;
            for (int i = 0; i < neighbours.Length && applied < neighbourCount; i++)
            {
                Unit n = neighbours[i];
                if (n == null || n.dead || n == targetUnit) continue;

                if (spreadAttackEffectors && effectors != null && effectors.Length > 0) Effector.EffectorAdd(byUnit, n, effectors);
                if (extraEffectors != null && extraEffectors.Length > 0) Effector.EffectorAdd(byUnit, n, extraEffectors);

                InterflowDebug.Event("ПЕРЕНОС ОГНЯ: " + InterflowDebug.Name(byUnit) + " перекинул эффект с " +
                                     InterflowDebug.Name(targetUnit) + " на соседа " + InterflowDebug.Name(n));

                applied++;
            }

            if (applied > 0) RequestForceSync();
        }

    }
}
