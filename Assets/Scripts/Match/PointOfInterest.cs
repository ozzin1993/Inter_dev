using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Точка интереса на линии: замок, оборонительная башня или центральная башня.
    /// Хранит СОБСТВЕННОЕ состояние точки: текущую команду-владельца, жива ли башня и привязанный юнит.
    /// Переходы владельца ведёт MatchManager (серверо-авторитетно) — сама точка только хранит данные.
    /// </summary>
    public class PointOfInterest : MonoBehaviour
    {
        /// <summary>Тип точки на линии.</summary>
        public enum PointType
        {
            Castle,    // Замок команды — конечная цель врага
            Defence1,  // Первая оборонительная башня (ближе к замку)
            Defence2,  // Вторая оборонительная башня (ближе к центру)
            Centre     // Центральная нейтральная башня
        }

        /// <summary>Начальная принадлежность точки. Значение должно совпадать со SlotManager.playerTeam соответствующего игрока.</summary>
        public enum InitialTeam
        {
            TeamA    =  0,  // Команда A (типично — игрок 0)
            TeamB    =  1,  // Команда B (типично — игрок 1)
            Neutral  = -1   // Нейтральная (например, центральная башня на старте)
        }

        [Header("Тип и принадлежность")]
        [Tooltip("Тип этой точки интереса: Castle — замок команды (конечная цель), " +
                 "Defence1 — первая башня ближе к замку, Defence2 — вторая ближе к центру, " +
                 "Centre — центральная нейтральная башня.")]
        [SerializeField] public PointType type;

        [Tooltip("Источник истины НАЧАЛЬНОГО владельца точки: TeamA = 0, TeamB = 1, Neutral = −1. " +
                 "При старте записывается в текущего владельца (CurrentTeam). " +
                 "Значение должно совпадать с SlotManager.playerTeam[ownerPlayerIndex] для соответствующей команды. " +
                 "Также используется как сторона точки при разрешении ничьей в Lane.")]
        [SerializeField] public InitialTeam teamAffiliation = InitialTeam.Neutral;

        [Header("Позиция в линии")]
        [Tooltip("Индекс позиции в линии слева направо: 0 — крайняя левая точка (замок A), " +
                 "возрастает к замку B. Определяет порядок выбора целей Lane; физическое расстояние игнорируется. " +
                 "Значения должны быть уникальны в пределах одной Lane. Рекомендуемый диапазон: 0–10.")]
        [Range(0, 10)]
        [SerializeField] public int laneIndex;

        [Header("Привязанный юнит")]
        [Tooltip("Рантайм-ссылка на живую башню/замок точки: MatchManager заполняет её при спавне (SetTower) " +
                 "и снимает при гибели (ClearTower). Стартовые башни спавнятся менеджером — в сцене оставлять " +
                 "ПУСТЫМ (кроме замков — они pre-placed). Нейтральному центру задай neutralTower в rebuildablePoints.")]
        [SerializeField] public Unit unitRef;

        // ======================== РАНТАЙМ-СОСТОЯНИЕ (источник истины владения) ========================

        // Текущая команда-владелец. Меняется только через SetTeam (захват ведёт MatchManager).
        int currentTeam;
        // Жива ли башня на точке. false — точка захвачена, но башня ещё не отстроена.
        bool towerAlive;

        void Awake()
        {
            // Начальное состояние из Inspector: владелец = teamAffiliation, башня жива если задана стартовая.
            currentTeam = (int)teamAffiliation;
            towerAlive  = unitRef != null;
        }

        // ======================== СВОЙСТВА ========================

        /// <summary>Текущая команда-владелец точки.</summary>
        public int CurrentTeam => currentTeam;

        /// <summary>Принадлежит ли точка указанной команде.</summary>
        public bool IsOwnedByTeam(int team) => currentTeam == team;

        /// <summary>Жива ли башня на точке (false в окне между гибелью и отстройкой).</summary>
        public bool TowerAlive => towerAlive;

        /// <summary>Позиция точки в плоскости XZ для передачи в Unit.Move / Unit.AttackMove.</summary>
        public Vector2 Position2D => new Vector2(transform.position.x, transform.position.z);

        // ======================== МУТАЦИИ (вызывает MatchManager) ========================

        /// <summary>Сменить команду-владельца точки (при захвате).</summary>
        public void SetTeam(int team) => currentTeam = team;

        /// <summary>Привязать отстроенную башню к точке (башня снова жива).</summary>
        public void SetTower(Unit tower)
        {
            unitRef    = tower;
            towerAlive = tower != null;
        }

        /// <summary>Снять башню с точки при её гибели (тело уничтожает штатный OnDie ассета).</summary>
        public void ClearTower()
        {
            unitRef    = null;
            towerAlive = false;
        }
    }
}
