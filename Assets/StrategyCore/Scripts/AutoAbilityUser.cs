using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Авто-применение способности юнитом: поиск цели по выбранной стратегии и каст
    /// штатным Unit.UseAbilityItem. Серверо-авторитетно — на клиенте бездействует (правило 6).
    /// Тик — на штатном GameManager.Tick (0.1 с); КД, ману, muted и требования проверяет
    /// сам UseAbilityItem, своих проверок не дублируем (правило 2).
    /// Класс/категория юнита и фильтры приказов — поля Unit (unitCategory, respondsTo…; перенесены из UnitClass в Unit.cs, план §4.3).
    /// После каста с прерыванием юнит возвращается к команде через MatchManager.ReissueCurrentCommand.
    /// </summary>
    // Без [RequireComponent(typeof(Unit))]: ассет снимает Unit через Utils.UnitRemoveComponents
    // (апгрейд/трансформация/статик-копия), а зависимость блокировала бы удаление Unit.
    // Unit берём через GetComponent в Awake; если его нет — компонент бездействует (гарды).
    public class AutoAbilityUser : MonoBehaviour
    {
        // Стратегия выбора цели. Один тип на юнит, задаётся в инспекторе.
        public enum TargetingStrategy
        {
            [InspectorName("Самый раненый союзник в радиусе")]
            MostWoundedAlly,
            [InspectorName("Случайный враг в радиусе (с приоритетом по категории)")]
            RandomEnemyWithTypePriority,
            [InspectorName("Ближайший враг в радиусе")]
            NearestEnemy,
            [InspectorName("Союзник впереди (в сторону вражеской точки)")]
            AllyAhead,
            [InspectorName("Сильнейший враг в радиусе (по метрике)")]
            StrongestEnemy,
            [InspectorName("Текущая цель атаки юнита")]
            CurrentAttackTarget
        }

        // Метрика «силы» для StrongestEnemy (B6). Стат «урон» — не простое public-поле Unit → отложен.
        public enum StrengthMetric
        {
            [InspectorName("Макс. ХП")] MaxHealth,
            [InspectorName("Текущее ХП")] CurrentHealth
        }

        // Как выбирать «союзника впереди» (B6).
        public enum AllyAheadMode
        {
            [InspectorName("Самый продвинутый к точке")] MostAdvanced,
            [InspectorName("Ближайший впереди носителя")] NearestAhead
        }

        [Header("Целеискание")]
        [Tooltip("Стратегия выбора цели для авто-каста.")]
        [SerializeField] private TargetingStrategy targetingStrategy = TargetingStrategy.RandomEnemyWithTypePriority;

        [Tooltip("Радиус поиска цели, мировые единицы.")]
        [SerializeField] private float searchRadius = 8f;

        [Tooltip("Кого считать кандидатами на цель (свой/союзник/враг + тип передвижения). " +
                 "Стратегия выбирает уже среди них. Для 'раненый союзник' — поставьте союзник+свой; для врага — враг.")]
        [SerializeField] private UnitSelector candidateSelector =
            new UnitSelector(false, false, true, true, false, false, false, true, true, true, false, false);

        [Tooltip("Только для стратегии 'Случайный враг': какую категорию врага предпочитать. " +
                 "Категория читается напрямую из Unit.unitCategory.")]
        [SerializeField] private Unit.UnitCategory priorityCategory = Unit.UnitCategory.Mage;

        [Tooltip("Для стратегии 'Сильнейший враг': по какому стату мерить силу (макс./текущее ХП).")]
        [SerializeField] private StrengthMetric strengthMetric = StrengthMetric.MaxHealth;

        [Tooltip("Для стратегии 'Союзник впереди': самый продвинутый к вражеской точке или ближайший впереди носителя.")]
        [SerializeField] private AllyAheadMode allyAheadMode = AllyAheadMode.MostAdvanced;

        [Header("Способность")]
        [Tooltip("Способность для авто-каста. Должна также присутствовать в списке Abilities этого юнита " +
                 "(каст идёт по индексу в пуле). Тип (Active/Unit/Area/Location) определяется автоматически.")]
        [SerializeField] private Ability autoAbility;

        // --- внутреннее состояние ---
        private Unit unit;
        private bool subscribed;
        private bool abilityResolved;
        private int cachedAbilityIndex = -1;
        private UnitStates prevState = UnitStates.Idle;

        private void Awake()
        {
            unit = GetComponent<Unit>();
        }

        private void OnEnable() => TrySubscribe();
        private void Start() => TrySubscribe();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();

        private void TrySubscribe()
        {
            if (subscribed) return;
            if (GameManager.instance == null) return;
            GameManager.instance.Tick += OnTick;
            prevState = unit != null ? unit.unitState : UnitStates.Idle;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
        }

        // Штатный тик ассета (0.1 с). Решение о цели — только на сервере (правило 6).
        private void OnTick()
        {
            if (NetworkConnectionHandler.isClient) return;
            if (unit == null || unit.dead) return;

            // 1) Возврат к команде после завершения каста (переход AbilityCasting → не-каст).
            //    Мгновенные способности (без castRange/castTime) в AbilityCasting не входят,
            //    движение не прерывают — для них этот блок не срабатывает, что и нужно.
            if (prevState == UnitStates.AbilityCasting && unit.unitState != UnitStates.AbilityCasting)
            {
                if (MatchManager.instance != null)
                    MatchManager.instance.ReissueCurrentCommand(unit);
            }

            // 2) Попытка авто-каста. Если КД не готов / нет маны / muted — UseAbilityItem вернёт false.
            TryAutoCast();

            prevState = unit.unitState;
        }

        private void TryAutoCast()
        {
            if (autoAbility == null) return;
            if (unit.unitState == UnitStates.AbilityCasting) return; // уже кастует

            int idx = ResolveAbilityIndex();
            if (idx < 0) return;

            Vector2 pos = new Vector2(transform.position.x, transform.position.z);
            Unit[] candidates = Utils.GetUnitsInRadius(pos, searchRadius, unit.owner, candidateSelector, -1, unit);
            Unit target = PickTarget(candidates);

            switch (autoAbility.type)
            {
                case AbilityType.Unit:
                    if (target == null) return;
                    unit.UseAbilityItem(idx, false, target, Vector3.zero);
                    break;
                case AbilityType.Area:
                case AbilityType.Location:
                    if (target == null) return;
                    unit.UseAbilityItem(idx, false, null, target.transform.position);
                    break;
                case AbilityType.Active:
                    // Active не имеет цели — кастуем при наличии подходящего кандидата в радиусе.
                    if (target == null) return;
                    unit.UseAbilityItem(idx, false, null, Vector3.zero);
                    break;
                // Прочие типы (Toggle/Aura/Passive/Process/...) — вне объёма авто-каста.
            }
        }

        // Выбор цели среди кандидатов по стратегии. null — если подходящих нет.
        private Unit PickTarget(Unit[] candidates)
        {
            // «Текущая цель атаки юнита»: цель берётся не из кандидатов в searchRadius, а из штатного
            // поля Unit.target; дальность каста проверит сам UseAbilityItem (castRange). Мёртвая/пустая
            // цель → null (каст не идёт). Не зависит от кандидатов в searchRadius (DoD «кастует по unit.target»).
            if (targetingStrategy == TargetingStrategy.CurrentAttackTarget)
                return (unit != null && unit.target != null && !unit.target.dead) ? unit.target : null;

            if (candidates == null || candidates.Length == 0) return null;

            switch (targetingStrategy)
            {
                case TargetingStrategy.MostWoundedAlly:
                {
                    Unit best = null;
                    float bestRatio = float.MaxValue;
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        Unit c = candidates[i];
                        if (c == null || c.dead || c == unit) continue;
                        if (c.maxHealth <= 0f) continue;
                        float ratio = c.health / c.maxHealth;
                        if (ratio < bestRatio)
                        {
                            bestRatio = ratio;
                            best = c;
                        }
                    }
                    return best;
                }
                case TargetingStrategy.RandomEnemyWithTypePriority:
                {
                    // Приоритетная подгруппа — кандидаты нужной категории (Unit.unitCategory).
                    // Пусто — берём всех валидных.
                    List<Unit> all = new List<Unit>();
                    List<Unit> priority = new List<Unit>();
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        Unit c = candidates[i];
                        if (c == null || c.dead || c == unit) continue;
                        all.Add(c);
                        if (c.unitCategory == priorityCategory)
                            priority.Add(c);
                    }
                    List<Unit> pool = priority.Count > 0 ? priority : all;
                    if (pool.Count == 0) return null;
                    return pool[Random.Range(0, pool.Count)];
                }
                case TargetingStrategy.NearestEnemy:
                {
                    // Ближайший к нам кандидат. «Первого зашедшего в радиус» обеспечивает тик:
                    // как только враг входит в радиус, он становится ближайшим и кастуется.
                    Unit best = null;
                    float bestSqr = float.MaxValue;
                    Vector3 self = transform.position;
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        Unit c = candidates[i];
                        if (c == null || c.dead || c == unit) continue;
                        float sqr = (c.transform.position - self).sqrMagnitude;
                        if (sqr < bestSqr)
                        {
                            bestSqr = sqr;
                            best = c;
                        }
                    }
                    return best;
                }
                case TargetingStrategy.AllyAhead:
                    return PickAllyAhead(candidates);
                case TargetingStrategy.StrongestEnemy:
                    return PickStrongestEnemy(candidates);
            }
            return null;
        }

        // B6 «Союзник впереди»: выбор союзника в сторону вражеской точки (Lane/AttackTarget, единый источник).
        private Unit PickAllyAhead(Unit[] candidates)
        {
            if (MatchManager.instance == null || unit == null) return null;
            Vector2 point = MatchManager.instance.AttackTarget(unit.owner); // цель по линии (вражеская точка)
            if (point == Vector2.zero) return null;                        // нет цели-направления → каста нет
            Vector2 self = new Vector2(transform.position.x, transform.position.z);
            Vector2 toPoint = point - self;
            if (toPoint.sqrMagnitude < 0.0001f) return null;               // носитель на точке → каста нет
            Vector2 dir = toPoint.normalized;

            Unit best = null;
            if (allyAheadMode == AllyAheadMode.MostAdvanced)
            {
                float bestDistToPoint = float.MaxValue;
                for (int i = 0; i < candidates.Length; i++)
                {
                    Unit c = candidates[i];
                    if (c == null || c.dead || c == unit) continue;
                    Vector2 cp = new Vector2(c.transform.position.x, c.transform.position.z);
                    float d = (point - cp).sqrMagnitude;
                    if (d < bestDistToPoint) { bestDistToPoint = d; best = c; }
                }
            }
            else // NearestAhead
            {
                float bestFwd = float.MaxValue;
                for (int i = 0; i < candidates.Length; i++)
                {
                    Unit c = candidates[i];
                    if (c == null || c.dead || c == unit) continue;
                    Vector2 cp = new Vector2(c.transform.position.x, c.transform.position.z);
                    Vector2 toC = cp - self;
                    if (Vector2.Dot(toC, dir) <= 0f) continue;             // не впереди носителя
                    float d = toC.sqrMagnitude;
                    if (d < bestFwd) { bestFwd = d; best = c; }
                }
            }
            return best;
        }

        // B6 «Сильнейший враг»: макс. значение метрики (макс./текущее ХП). Тайбрейк — первый найденный (выбор серверный).
        private Unit PickStrongestEnemy(Unit[] candidates)
        {
            Unit best = null;
            float bestVal = float.MinValue;
            for (int i = 0; i < candidates.Length; i++)
            {
                Unit c = candidates[i];
                if (c == null || c.dead || c == unit) continue;
                float val = strengthMetric == StrengthMetric.CurrentHealth ? c.health : c.maxHealth;
                if (val > bestVal) { bestVal = val; best = c; }
            }
            return best;
        }

        // Индекс способности в пуле юнита (кэш). Обход бага Utils.GetAbilityIndex
        // (при «не найдено» возвращает не -1) кросс-проверкой GetAbilityByIndex — как в UIManager.BottomTables.
        private int ResolveAbilityIndex()
        {
            // [CR1 fix 2026-07-10] Кэш валиден, пока по индексу всё ещё лежит наша способность.
            // Пул мог измениться (тех-анлок/трансформация сдвигают индексы) → перерезолвим.
            // «Не найдено» (-1) не перерешаем каждый тик (иначе спам-лог/лишний перебор).
            if (abilityResolved)
            {
                if (cachedAbilityIndex < 0) return -1;
                if (unit != null && Utils.GetAbilityByIndex(unit, cachedAbilityIndex) == autoAbility)
                    return cachedAbilityIndex;
                // индекс устарел — перерезолвим ниже
            }
            abilityResolved = true;
            cachedAbilityIndex = -1;
            if (autoAbility == null || unit.abilities == null) return -1;

            int idx = Utils.GetAbilityIndex(unit.abilities, autoAbility);
            if (idx >= 0 && Utils.GetAbilityByIndex(unit, idx) == autoAbility)
                cachedAbilityIndex = idx;
            else
                Debug.LogWarning($"[AutoAbilityUser] У '{unit.unitName}' способность '{autoAbility.name}' " +
                                 "не найдена в списке Abilities — авто-каст отключён.");
            return cachedAbilityIndex;
        }
    }
}
