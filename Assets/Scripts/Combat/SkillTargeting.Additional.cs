using UnityEngine;

namespace StrategyCore
{
    public static partial class SkillTargeting
    {
        /// <summary>Оценка обычной атаки: урон / интервал атак. Пассивки и броня жертвы не прогнозируются.</summary>
        public static Unit HighestDps(Unit[] candidates, Unit self)
        {
            if (candidates == null) return null;
            Unit best = null;
            float bestDamage = -1f;
            foreach (Unit candidate in candidates)
            {
                if (candidate == null || candidate == self || candidate.dead) continue;
                float damage = candidate.canAttack && candidate.attackSpeed > 0f
                    ? Mathf.Max(0f, candidate.attackDamage) / candidate.attackSpeed : 0f;
                if (damage > bestDamage) { bestDamage = damage; best = candidate; }
            }
            return best;
        }

        /// <summary>Ближайший из живых кандидатов строго ниже порога здоровья.</summary>
        public static Unit NearestBelowThreshold(Unit[] candidates, Unit self, Vector3 origin, float threshold)
        {
            if (candidates == null) return null;
            Unit best = null;
            float bestDistance = float.MaxValue;
            foreach (Unit candidate in candidates)
            {
                if (candidate == null || candidate == self || candidate.dead || candidate.maxHealth <= 0f) continue;
                if (!SkillCastConditionsBlock.HpAllowed(candidate.health, candidate.maxHealth, threshold)) continue;
                float distance = (candidate.transform.position - origin).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = candidate; }
            }
            return best;
        }
    }
}
