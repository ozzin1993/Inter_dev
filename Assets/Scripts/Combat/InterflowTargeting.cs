using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Interflow: выбор НОВОЙ цели атаки с учётом приоритета категорий юнитов.
    /// Вклинивается в штатный выбор цели в Unit.cs (4 точки) через [Interflow fix].
    /// Базовая ветка = штатный Utils.GetClosestUnit — «ближайшего» не переизобретаем (правило 2).
    /// Применяется только там, где ассет выбирает новую цель → уже начатый бой не прерывается.
    /// Источник приоритета: сначала собственный Unit.targetPriority юнита, иначе командный дефолт
    /// (MatchManager.TargetPriority, per-team). Ядро ассета не правится этим файлом (правило 1).
    /// </summary>
    public static class InterflowTargeting
    {
        /// <summary>
        /// Ближайший враг из первой непустой приоритетной категории в радиусе; если приоритета нет
        /// или в его категориях никого — штатный ближайший (Utils.GetClosestUnit). Контракт (радиус,
        /// selector, FoWVisible, метрика дистанции) полностью повторяет GetClosestUnit — для юнитов без
        /// приоритета поведение идентично штатному (регресс-нейтрально).
        /// </summary>
        /// <param name="unit">Юнит, выбирающий цель (источник позиции/радиуса/владельца/команды).</param>
        /// <param name="radius">Радиус поиска (штатный из точки вызова: reactionRange/attackRange).</param>
        /// <param name="selector">Штатный фильтр совместимости цели (свой/союзник/враг + тип передвижения).</param>
        /// <param name="FoWVisible">Учитывать туман войны (как GetClosestUnit): невидимые цели пропускаются.</param>
        public static Unit PickWithPriority(Unit unit, float radius, UnitSelector selector, bool FoWVisible)
        {
            if (unit == null) return null;

            // 1) Эффективный список категорий: собственный список юнита перебивает командный дефолт.
            Unit.UnitCategory[] priority =
                (unit.targetPriority != null && unit.targetPriority.Length > 0)
                    ? unit.targetPriority
                    : (MatchManager.Instance != null
                        ? MatchManager.Instance.GetTeamTargetPriorityForOwner(unit.owner)
                        : null);

            // 2) Приоритета нет → штатный ближайший (базовая ветка; правило 2).
            if (priority == null || priority.Length == 0)
                return Utils.GetClosestUnit(unit, radius, selector, FoWVisible);

            // 3) Кандидаты штатно: тот же радиус/selector/самоисключение, что и у GetClosestUnit.
            //    FoW у GetUnitsInRadius НЕ фильтруется — докручиваем вручную штатным FogOfWar.IsVisible,
            //    чтобы «видимость/туман сохраняются» так же, как в GetClosestUnit(FoWVisible=true).
            Vector2 pos = new Vector2(unit.transform.position.x, unit.transform.position.z);
            Unit[] candidates = Utils.GetUnitsInRadius(pos, radius, unit.owner, selector, -1, unit);

            // 4) Идём по категориям по порядку; первая, где есть кандидат — вернуть ближайшего из неё.
            for (int c = 0; c < priority.Length; c++)
            {
                Unit.UnitCategory cat = priority[c];
                Unit best = null;
                float bestDist = float.MaxValue;

                for (int i = 0; i < candidates.Length; i++)
                {
                    Unit u = candidates[i];
                    if (u == null) continue;
                    if (u.unitCategory != cat) continue;
                    // Тот же фильтр тумана, что в GetClosestUnit (иначе приоритет «видел бы» сквозь туман).
                    if (FoWVisible && !FogOfWar.Instance.IsVisible(u.FoWCell, unit.team)) continue;
                    // Та же метрика дистанции, что в GetClosestUnit («ближайший»).
                    float dist = Vector2.Distance(
                                     new Vector2(u.transform.position.x, u.transform.position.z), pos)
                                 - u.unitRadius;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = u;
                    }
                }

                if (best != null) return best;
            }

            // 5) Ни в одной приоритетной категории никого — штатный ближайший (fallback; правило 2).
            return Utils.GetClosestUnit(unit, radius, selector, FoWVisible);
        }
    }
}
