using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Ведёт группу юнитов к цели единым строем (квадратная сетка, formationPriority — в передний ряд).
    /// Раскладка вычисляется самостоятельно: центр строя — финальная цель, ориентация — от центроида к цели.
    /// При наличии targetDelegate пересчитывает цель каждый тик — реагирует на захват/потерю башен.
    ///
    /// Логика (только сервер/хост):
    ///  - при старте и при смене цели — выдаёт AttackMove на слоты;
    ///  - бой при контакте — штатный (AttackMove сам находит врага и распускает строй);
    ///  - после боя (нет врага в radиусе centroid в течение regroupDebounce) пересобирает строй;
    ///  - НЕ уничтожается при прибытии к цели — держит юнитов от дрейфа к замку;
    ///  - самоуничтожается только когда не осталось живых юнитов или вызван Stop().
    /// </summary>
    public class FormationMarch : MonoBehaviour
    {
        // ============================= STATE ==
        private readonly List<Unit> units = new List<Unit>();
        private Vector2 target;
        private int ownerPlayer;
        private int commandGroup = -1; // Ряд команды (MatchManager.commandGroups), которому служит строй. -1 — вне рядов (дефолт-марш).

        private float detectionRadius;
        private float regroupDebounce;
        private float pollInterval;
        private float arrivalDistance;
        private float slotSpacing;

        // Если задан — пересчитывает цель каждый тик; позволяет реагировать на захват башен
        private Func<Vector2> targetDelegate;

        private UnitSelector enemySelector;
        private bool inCombat;
        private float peaceTimer;
        private float pollTimer;
        private bool initialized;

        // ============================= РЕЕСТР (перф) ==
        // Активные формации — чтобы не сканировать сцену FindObjectsByType на каждую команду.
        // Регистрация на всю жизнь объекта: Awake → добавить, OnDestroy → убрать.
        private static readonly List<FormationMarch> activeFormations = new List<FormationMarch>();
        /// <summary>Активные формации (только чтение). Замена FindObjectsByType&lt;FormationMarch&gt;.</summary>
        public static IReadOnlyList<FormationMarch> ActiveFormations => activeFormations;

        private void Awake() { activeFormations.Add(this); }
        private void OnDestroy() { activeFormations.Remove(this); }

        // ============================= ENTRY POINT ==
        /// <summary>
        /// Создаёт объект-менеджер строя для одной волны и запускает марш.
        /// </summary>
        public static FormationMarch Begin(
            List<Unit> waveUnits,
            Vector2 target,
            int ownerPlayer,
            float detectionRadius,
            float regroupDebounce,
            float pollInterval,
            float arrivalDistance,
            float slotSpacing = 2f,
            Func<Vector2> targetDelegate = null,
            int commandGroup = -1)
        {
            if (NetworkConnectionHandler.isClient) return null;
            if (waveUnits == null || waveUnits.Count == 0) return null;

            GameObject go = new GameObject("FormationMarch");
            FormationMarch fm = go.AddComponent<FormationMarch>();

            fm.target = target;
            fm.ownerPlayer = ownerPlayer;
            fm.commandGroup = commandGroup;
            fm.detectionRadius = Mathf.Max(0.1f, detectionRadius);
            fm.regroupDebounce = Mathf.Max(0f, regroupDebounce);
            fm.pollInterval = Mathf.Max(0.05f, pollInterval);
            fm.arrivalDistance = Mathf.Max(0.1f, arrivalDistance);
            fm.slotSpacing = Mathf.Max(0.1f, slotSpacing);
            fm.targetDelegate = targetDelegate;

            fm.units.Clear();
            for (int i = 0; i < waveUnits.Count; i++)
                if (waveUnits[i] != null) fm.units.Add(waveUnits[i]);

            // Враги владельца группы: Enemy + (Unit|Building), любая среда передвижения.
            fm.enemySelector = new UnitSelector(
                false, false, true,   // Own, Ally, Enemy
                true, true, false, false, // Unit, Building, StaticDestructible, Tree
                true, true, true,     // Ground, Water, Air
                false, false);        // includeInvisible, includeInvulnerable

            fm.initialized = true;
            fm.IssueFormation();
            return fm;
        }

        // ============================= LIFECYCLE ==
        void Update()
        {
            if (!initialized) return;
            if (NetworkConnectionHandler.isClient) return;

            pollTimer += Time.deltaTime;
            if (pollTimer < pollInterval) return;
            pollTimer = 0f;

            // Пересчитываем цель из делегата (реагируем на захват/потерю башен)
            bool targetChanged = false;
            if (targetDelegate != null)
            {
                Vector2 newTarget = targetDelegate();
                if (newTarget != Vector2.zero && Vector2.Distance(newTarget, target) > arrivalDistance)
                {
                    target = newTarget;
                    targetChanged = true;
                }
            }

            Prune();
            if (units.Count == 0)
            {
                Destroy(gameObject);
                return;
            }

            Vector2 centroid = Centroid();
            Unit enemy = Utils.GetClosestUnit(centroid, detectionRadius, ownerPlayer, enemySelector);

            if (enemy != null)
            {
                inCombat = true;
                peaceTimer = 0f;
                return;
            }

            // Врагов рядом нет
            if (inCombat)
            {
                peaceTimer += pollInterval;
                if (peaceTimer >= regroupDebounce)
                {
                    IssueFormation();
                    inCombat = false;
                    peaceTimer = 0f;
                }
                return;
            }

            // Мирный режим: переиздаём строй ТОЛЬКО при смене цели (захват/потеря точек).
            // Пока цель та же — юниты сами идут к ранее выданным слотам; повторный AttackMove не шлём,
            // иначе каждый такт: target=null + AttackStop() + SetDestination() (Unit.AttackMove) → рывок.
            // Пересборка после боя — отдельной веткой выше (inCombat → regroupDebounce).
            // НЕ уничтожаемся по прибытии — держим позицию, чтобы юниты не уходили к замку.
            if (targetChanged)
                IssueFormation();
        }

        // ============================= FORMATION ==
        private void IssueFormation()
        {
            Prune();
            if (units.Count == 0) return;

            // Хост/клиент: штатная раскладка ассета — квадрат с учётом unitRadius каждого юнита,
            // центрированный на цели. ComputeFormation возвращает позиции по входному индексу units.
            if (PlayerControl.instance != null)
            {
                List<Vector2> dests = PlayerControl.instance.ComputeFormation(
                    units, new Vector3(target.x, 0f, target.y));
                for (int i = 0; i < units.Count && i < dests.Count; i++)
                    if (units[i] != null) units[i].AttackMove(dests[i]);
                return;
            }

            // Фоллбэк (выделенный сервер без PlayerControl): ручная квадратная раскладка.
            IssueFormationManual();
        }

        // Ручная раскладка — фоллбэк, когда PlayerControl недоступен (headless-сервер).
        private void IssueFormationManual()
        {
            Prune();
            if (units.Count == 0) return;

            int n = units.Count;

            // Центроид текущих позиций
            Vector2 centroid = Centroid();

            // Ориентация строя: вперёд — от центроида к цели
            Vector2 forward = target - centroid;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector2.up;
            forward.Normalize();
            Vector2 right = new Vector2(forward.y, -forward.x);

            // Сортировка: высокий formationPriority — вперёд и в центр; при равенстве — по netID
            List<(Unit unit, int idx)> sorted = new List<(Unit, int)>(n);
            for (int i = 0; i < n; i++)
                if (units[i] != null) sorted.Add((units[i], i));

            sorted.Sort((a, b) =>
            {
                int cmp = b.unit.formationPriority.CompareTo(a.unit.formationPriority);
                return cmp != 0 ? cmp : a.unit.netID.CompareTo(b.unit.netID);
            });

            // Квадратная сетка, центрированная на цели
            int cols = Mathf.CeilToInt(Mathf.Sqrt(sorted.Count));
            int rows = Mathf.CeilToInt((float)sorted.Count / cols);

            for (int i = 0; i < sorted.Count; i++)
            {
                int row = i / cols;
                int col = i % cols;

                // Количество юнитов в этом ряду (последний ряд может быть неполным)
                int unitsInRow = (row == rows - 1) ? sorted.Count - row * cols : cols;

                // Горизонтальное центрирование в пределах ряда
                float colOffset = col - (unitsInRow - 1) * 0.5f;

                // Вертикальное: ряд 0 — передний (ближайший к цели)
                float rowOffset = -(row - (rows - 1) * 0.5f);

                Vector2 slot = target
                    + right   * (colOffset * slotSpacing)
                    + forward * (rowOffset * slotSpacing);

                sorted[i].unit.AttackMove(slot);
            }
        }

        // ============================= PUBLIC API ==

        /// <summary>Индекс игрока-владельца этой формации (для фильтрации в UnitGroupCommandUI).</summary>
        public int OwnerPlayer => ownerPlayer;

        /// <summary>Ряд команды (MatchManager.commandGroups), которому служит этот строй. -1 — вне рядов (дефолт-марш).</summary>
        public int CommandGroup => commandGroup;

        /// <summary>Немедленно останавливает формацию и уничтожает объект-менеджер.</summary>
        public void Stop() { Destroy(gameObject); }

        // ============================= HELPERS ==
        private void Prune()
        {
            for (int i = units.Count - 1; i >= 0; i--)
                if (units[i] == null || units[i].dead) units.RemoveAt(i);
        }

        private Vector2 Centroid()
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i] == null) continue;
                Vector3 p = units[i].transform.position;
                sum += new Vector2(p.x, p.z);
                n++;
            }
            return n > 0 ? sum / n : target;
        }
    }
}
