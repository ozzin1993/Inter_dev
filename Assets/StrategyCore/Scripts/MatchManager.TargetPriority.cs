using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Interflow-партиал MatchManager: командный дефолт приоритета выбора цели атаки (по командам A/B).
    /// Юнит со своим Unit.targetPriority перебивает командный дефолт (см. InterflowTargeting.PickWithPriority).
    /// Правится в Inspector и через Interflow Editor. Сеттер серверо-авторитетный (правило 6) — задел под
    /// игровой UI смены приоритета (в объёме этого промта — только задел, без самого UI и без сетевого синка).
    /// MatchManager — наш код; ядро ассета не правится (правило 1).
    /// </summary>
    public partial class MatchManager
    {
        [Header("Приоритет цели атаки — командный дефолт (Interflow)")]
        [Tooltip("Дефолтный приоритет категорий целей для команды A (владелец = teamA.ownerPlayer). " +
                 "Упорядоченный список: юнит БЕЗ собственного targetPriority предпочитает ближайшего из первой " +
                 "непустой категории. Пусто — штатный выбор (ближайший).")]
        [SerializeField] Unit.UnitCategory[] teamTargetPriorityA;

        [Tooltip("Дефолтный приоритет категорий целей для команды B (владелец = teamB.ownerPlayer). " +
                 "Аналогично команде A.")]
        [SerializeField] Unit.UnitCategory[] teamTargetPriorityB;

        // Командный дефолт по индексу команды (0=A, 1=B). Иные индексы → null (дефолта нет).
        Unit.UnitCategory[] TeamTargetPriorityByIndex(int team)
        {
            if (team == 0) return teamTargetPriorityA;
            if (team == 1) return teamTargetPriorityB;
            return null;
        }

        /// <summary>
        /// Командный дефолт приоритета для игрока-владельца юнита (резолв команды — TeamIndexOfOwner).
        /// null/пусто — дефолта нет. Используется InterflowTargeting.PickWithPriority, когда у юнита нет
        /// собственного targetPriority. Только чтение — безопасно на любом пире.
        /// </summary>
        public Unit.UnitCategory[] GetTeamTargetPriorityForOwner(int player)
        {
            return TeamTargetPriorityByIndex(TeamIndexOfOwner(player));
        }

        /// <summary>
        /// Задать командный дефолт приоритета (0=A, 1=B). Серверо-авторитетно (правило 6): на клиенте — no-op.
        /// Задел под игровой UI смены приоритета; сетевой синк клиентам добавляется вместе с этим UI (вне промта).
        /// </summary>
        public void SetTeamTargetPriority(int team, Unit.UnitCategory[] categories)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (team == 0) teamTargetPriorityA = categories;
            else if (team == 1) teamTargetPriorityB = categories;
        }
    }
}
