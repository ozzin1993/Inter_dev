using UnityEngine;

namespace StrategyCore
{
    // ===== МОДИФИКАТОРЫ СТОИМОСТИ НАЙМА (кирпич B28, партиал MatchManager) =====
    // Единая точка ЭФФЕКТИВНОЙ цены юнита с учётом активных модификаторов расы (FactionConfig.costModifiers,
    // резолв в TeamWaveConfig). Модификатор активен, пока триггер-тех разблокирован у команды (штатный TechTree
    // через TechUnlockedSafe — «Required-условие», без своего состояния/подписок → идемпотентно, правило 8).
    // Через EffectiveResourceCost проходят ВСЕ места цены (сумма волны, UI, списание) — игрок видит и платит
    // одно и то же (риск §9 промта снят). Значение скидки — данными (FactionConfig), без хардкода (правило 3).
    // Списание — серверное (SpawnWave); сам расчёт цены детерминирован и одинаков на всех пирах. Ассет не трогаем (правило 1).
    public partial class MatchManager
    {
        /// <summary>
        /// Эффективная цена префаба по ресурсу для команды с учётом активных модификаторов найма (B28).
        /// База — штатный PrefabResourceCost (Unit.resourceCost). Множитель — произведение (1 − скидка%)
        /// активных правил этого ресурса. Округление до целого (цены — int), не ниже нуля.
        /// </summary>
        public int EffectiveResourceCost(int team, Unit prefab, Resource resource)
        {
            int baseCost = PrefabResourceCost(prefab, resource);
            if (baseCost == 0 || resource == null) return baseCost;

            float factor = CostFactor(team, resource);
            if (factor == 1f) return baseCost;
            return Mathf.Max(0, Mathf.RoundToInt(baseCost * factor));
        }

        // Множитель цены команды по ресурсу: произведение (1 − percentReduction/100) активных правил
        // (триггер-тех разблокирован). Нет правил/конфига — 1 (цена без изменения).
        float CostFactor(int team, Resource resource)
        {
            TeamWaveConfig cfg = Team(team);
            if (cfg == null || cfg.costModifiers == null || resource == null) return 1f;

            float factor = 1f;
            for (int i = 0; i < cfg.costModifiers.Length; i++)
            {
                CostModifierRule rule = cfg.costModifiers[i];
                if (rule == null || rule.resource != resource || rule.triggerTech == null) continue;
                if (!TechUnlockedSafe(rule.triggerTech, cfg.ownerPlayer)) continue; // активен только при разблокированном техе
                factor *= (1f - Mathf.Clamp(rule.percentReduction, 0f, 100f) / 100f);
            }
            return factor;
        }
    }
}
