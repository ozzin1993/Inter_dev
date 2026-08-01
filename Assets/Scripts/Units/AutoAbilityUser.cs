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
    ///
    /// Сами алгоритмы выбора цели вынесены в общий <see cref="SkillTargeting"/> (правило 5) — их же
    /// переиспользует конструктор скиллов. Здесь остались только настройки на юните и диспетчеризация;
    /// поведение стратегий не менялось.
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
            CurrentAttackTarget,
            [InspectorName("Ближайший союзник заданной категории")]
            AllyOfCategory,
            [InspectorName("Раненый союзник ниже порога ХП")]
            WoundedAllyBelowThreshold
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

        [Tooltip("Только для стратегии 'Ближайший союзник заданной категории': какую категорию прикрывать " +
                 "(например Танк — для щита Мага Льда). Категория читается из Unit.unitCategory.")]
        [SerializeField] private Unit.UnitCategory allyCategory = Unit.UnitCategory.Tank;

        [Tooltip("Только для стратегии 'Раненый союзник ниже порога ХП': доля здоровья, ниже которой союзник считается целью " +
                 "(0.3 = каст только по тем, у кого меньше 30% ХП). Если таких нет — каста не будет.")]
        [Range(0f, 1f)]
        [SerializeField] private float allyHpThreshold = 0.3f;

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

            // Уровень способности у ЭТОГО юнита — нужен стратегии скилла (например радиус скопления).
            int level = (unit.abilityLevel != null && idx < unit.abilityLevel.Length) ? unit.abilityLevel[idx] : 0;

            Unit[] candidates = SkillTargeting.Candidates(unit, searchRadius, candidateSelector);
            Unit target = PickTarget(candidates, level);

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
        // Алгоритмы живут в SkillTargeting (правило 5); здесь — только подстановка настроек юнита.
        private Unit PickTarget(Unit[] candidates, int level)
        {
            // Скилл-конструктор с режимом «умный выбор» несёт стратегию В СЕБЕ: тогда настройка
            // на юните не используется — один и тот же скилл должен вести себя одинаково на всех носителях.
            // Прочие способности продолжают работать по стратегии этого компонента (поведение как раньше).
            if (autoAbility is CompositeSkill skill && skill.PicksTargetByStrategy)
            {
                if (skill.TargetStrategy == SkillTargetStrategy.CurrentAttackTarget)
                    return SkillTargeting.CurrentAttackTarget(unit);

                if (candidates == null || candidates.Length == 0) return null;

                // Кандидатов набрал СВОЙ селектор компонента, а фильтры цели живут в скилле.
                // Без этого отсева лечащий скилл на юните с настройками по умолчанию (кандидаты = враги)
                // спокойно выбрал бы противника.
                List<Unit> eligible = new List<Unit>(candidates.Length);
                for (int i = 0; i < candidates.Length; i++)
                    if (skill.IsEligibleTarget(candidates[i], unit.owner)) eligible.Add(candidates[i]);
                if (eligible.Count == 0) return null;

                return SkillTargeting.Pick(skill.TargetStrategy, eligible.ToArray(), unit,
                                           skill.TargetingOptions(level, transform.position));
            }

            // «Текущая цель атаки юнита»: цель берётся не из кандидатов в searchRadius, а из штатного
            // поля Unit.target; дальность каста проверит сам UseAbilityItem (castRange). Мёртвая/пустая
            // цель → null (каст не идёт). Не зависит от кандидатов в searchRadius (DoD «кастует по unit.target»).
            if (targetingStrategy == TargetingStrategy.CurrentAttackTarget)
                return SkillTargeting.CurrentAttackTarget(unit);

            if (candidates == null || candidates.Length == 0) return null;

            Vector3 origin = transform.position;

            switch (targetingStrategy)
            {
                case TargetingStrategy.MostWoundedAlly:
                    return SkillTargeting.MostWounded(candidates, unit);

                case TargetingStrategy.RandomEnemyWithTypePriority:
                    return SkillTargeting.RandomWithCategoryPriority(candidates, unit, priorityCategory);

                case TargetingStrategy.NearestEnemy:
                    // «Первого зашедшего в радиус» обеспечивает тик: как только враг входит
                    // в радиус, он становится ближайшим и по нему идёт каст.
                    return SkillTargeting.Nearest(candidates, unit, origin);

                case TargetingStrategy.AllyAhead:
                    return SkillTargeting.AllyAhead(candidates, unit, allyAheadMode == AllyAheadMode.MostAdvanced);

                case TargetingStrategy.StrongestEnemy:
                    return SkillTargeting.Strongest(candidates, unit, strengthMetric == StrengthMetric.CurrentHealth);

                case TargetingStrategy.AllyOfCategory:
                    return SkillTargeting.NearestOfCategory(candidates, unit, origin, allyCategory);

                case TargetingStrategy.WoundedAllyBelowThreshold:
                    return SkillTargeting.MostWoundedBelowThreshold(candidates, unit, allyHpThreshold);
            }

            return null;
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
