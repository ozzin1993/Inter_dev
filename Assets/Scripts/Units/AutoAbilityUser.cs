using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Авто-применение умений юнитом. Компонент СВОЕГО поиска цели не ведёт: и условие срабатывания,
    /// и отбор целей живут в самом умении (конструктор скиллов) — решение Artsiom 2026-08-08.
    /// До этого настройки поиска стояли дважды, на юните и в умении, и расходились между собой.
    ///
    /// Здесь осталось ровно три вещи: список умений (порядок = приоритет; в каждый момент активно одно —
    /// первое открытое технологией, решение Artsiom 2026-09-04), гейт по мане и возврат к команде после каста.
    /// Серверо-авторитетно — на клиенте бездействует (правило 6). Тик — штатный GameManager.Tick (0.1 с).
    ///
    /// Мана — шкала готовности авто-умения (целевая модель §9, блок Б5, 2026-09-04): умение готово, когда мана
    /// носителя заполнена целиком; срабатывание забирает всю ману (списывает ядро по факту срабатывания —
    /// Unit.SubtractAbilityItemCost, через IsAutoAbility); откат не учитывается — компонент снимает его
    /// штатным ChangeAbilityCooldown(−1) перед кастом. muted и требования (замок, ресурсы) проверяет сам UseAbilityItem.
    /// </summary>
    // Без [RequireComponent(typeof(Unit))]: ассет снимает Unit через Utils.UnitRemoveComponents
    // (апгрейд/трансформация/статик-копия), а зависимость блокировала бы удаление Unit.
    // Unit берём через GetComponent в Awake; если его нет — компонент бездействует (гарды).
    public class AutoAbilityUser : MonoBehaviour
    {
        [Header("Авто-умения")]
        [Tooltip("Умения, которые юнит применяет сам. За тик применяется ПЕРВОЕ по списку умение, открытое " +
                 "технологией и нашедшее цель; несколько записей — это альтернативные специализации, " +
                 "которые техой открываются по одной, так что фактически работает одно.\n\n" +
                 "Готовность — мана юнита заполнена целиком; срабатывание забирает всю ману; откат умения не учитывается. " +
                 "Период = максимум маны / восстановление маны юнита. Без маны или без её восстановления умение " +
                 "не сработает никогда (валидатор считает это ошибкой).\n\n" +
                 "Каждое умение обязано также стоять в списке Abilities этого юнита — применение идёт по индексу в пуле.\n\n" +
                 "Кого бить, где искать и когда срабатывать настраивается В САМОМ УМЕНИИ (Interflow Editor), " +
                 "здесь этих настроек нет намеренно: иначе одно и то же умение вело бы себя по-разному на разных юнитах.")]
        [SerializeField] private CompositeSkill[] autoAbilities = new CompositeSkill[0];

        // ---------------------------------------------------------------------------------------
        // Настройки поиска (радиус, селектор кандидатов, боевые роли, стратегия и её параметры)
        // УДАЛЕНЫ 2026-08-08 вместе с классом-записью AutoAbilityEntry: источник истины — умение.
        // Обычные Ability (не конструктор) компонент больше не принимает: три класса, проходивших
        // по типу (ShieldAlly, HolyFire, CorruptionBurstActive), переводятся в конструктор.

        /// <summary>
        /// Список авто-умений для чтения снаружи (панель тестового полигона показывает, что юнит кастует сам).
        /// Отдаётся сам массив, без копии: состав задаётся в Inspector и в рантайме не меняется.
        /// </summary>
        public CompositeSkill[] AutoAbilities => autoAbilities;

        /// <summary>
        /// Это умение — авто-умение носителя? Спрашивает ядро в точке списания цены (Unit.SubtractAbilityItemCost):
        /// срабатывание авто-умения забирает всю ману носителя по факту срабатывания, прерванный каст ману не тратит.
        /// </summary>
        public bool IsAutoAbility(Ability ability)
        {
            if (ability == null || autoAbilities == null) return false;
            for (int i = 0; i < autoAbilities.Length; i++)
                if (autoAbilities[i] == ability) return true;
            return false;
        }

        /// <summary>
        /// Пауза авто-применения у ЭТОГО юнита (переключатель «Авто-применение» на полигоне).
        /// Не сериализуется: в обычном матче всегда false, поведение не меняется.
        /// </summary>
        [System.NonSerialized] public bool testAutoCastSuspended;

        // --- внутреннее состояние ---
        private Unit unit;
        private bool subscribed;
        private UnitStates prevState = UnitStates.Idle;

        // Рантайм-кэш индексов в пуле умений юнита — по элементу на каждое умение, не сериализуется.
        private int[] cachedIndex;
        private bool[] resolved;

        private void Awake()
        {
            unit = GetComponent<Unit>();
            ResetCache();
        }

        private void ResetCache()
        {
            int count = autoAbilities != null ? autoAbilities.Length : 0;
            cachedIndex = new int[count];
            resolved = new bool[count];
            for (int i = 0; i < count; i++) cachedIndex[i] = -1;
        }

        private void OnEnable() => TrySubscribe();
        private void Start() => TrySubscribe();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();

        private void TrySubscribe()
        {
            if (subscribed) return;
            if (GameManager.Instance == null) return;
            GameManager.Instance.Tick += OnTick;
            prevState = unit != null ? unit.unitState : UnitStates.Idle;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (GameManager.Instance != null) GameManager.Instance.Tick -= OnTick;
            subscribed = false;
        }

        // Штатный тик ассета (0.1 с). Решение о касте — только на сервере (правило 6).
        private void OnTick()
        {
            if (NetworkConnectionHandler.isClient) return;
            if (unit == null || unit.dead) return;

            // 1) Возврат к команде после завершения каста (переход AbilityCasting → не-каст).
            //    Мгновенные умения (без castRange/castTime) в AbilityCasting не входят,
            //    движение не прерывают — для них этот блок не срабатывает, что и нужно.
            if (prevState == UnitStates.AbilityCasting && unit.unitState != UnitStates.AbilityCasting)
            {
                if (MatchManager.Instance != null)
                    MatchManager.Instance.ReissueCurrentCommand(unit);
            }

            // 2) Попытка авто-каста.
            TryAutoCast();

            prevState = unit.unitState;
        }

        private void TryAutoCast()
        {
            if (testAutoCastSuspended) return;                       // пауза авто-применения (тестовый полигон)
            if (unit.unitState == UnitStates.AbilityCasting) return; // уже кастует
            if (autoAbilities == null) return;

            // Кэш мог не совпасть по длине, если список поменяли в Inspector в режиме игры.
            if (resolved == null || resolved.Length != autoAbilities.Length) ResetCache();

            // Порядок = приоритет. За тик применяем ОДНО умение: второй каст в том же тике
            // всё равно прервал бы первый (юнит уходит в AbilityCasting).
            for (int i = 0; i < autoAbilities.Length; i++)
                if (TryCastEntry(i)) return;
        }

        /// <summary>Попытаться применить одно умение из списка. true — каст состоялся.</summary>
        private bool TryCastEntry(int entry)
        {
            CompositeSkill skill = autoAbilities[entry];
            if (skill == null) return false;

            int idx = ResolveAbilityIndex(entry);
            if (idx < 0) return false;

            // Закрытое технологией умение пропускаем ДО поиска цели: в списке лежат альтернативные специализации,
            // открытые техой по одной (решение Artsiom 2026-09-04), а замкнутому ядро всё равно откажет
            // (Unit.CheckAbilityItemRequirements). Условие то же, что у ядра: умение «за очки героя» замок не держит.
            if (!skill.heroLevelable && unit.abilityLocked != null && idx < unit.abilityLocked.Length && unit.abilityLocked[idx])
                return false;

            // Готовность — мана носителя заполнена целиком (целевая модель §9). Без максимума маны умение
            // не готово никогда: иначе «0 >= 0» стреляло бы каждый тик. Откат здесь не смотрим намеренно —
            // он у авто-умения не учитывается (снимается ниже, перед самым кастом).
            if (unit.maxMana <= 0f || unit.mana < unit.maxMana) return false;

            // Уровень умения у ЭТОГО юнита — нужен стратегии и дальности.
            int level = (unit.abilityLevel != null && idx < unit.abilityLevel.Length)
                        ? Mathf.Max(0, unit.abilityLevel[idx]) : 0;

            // Условие срабатывания и цель определяет само умение.
            if (!skill.AutoCastReady(unit, level, out Unit target)) return false;

            // Откат у авто-умения не учитывается: ядро ставит его после каждого срабатывания
            // (Unit.UseAbilityImmediately) и отказало бы в касте (IsCooldownGood) — снимаем штатным
            // ChangeAbilityCooldown(−1). Только здесь, когда каст действительно состоится.
            unit.ChangeAbilityCooldown(-1f, idx, false);

            // Умение цель не подставило — либо она ему не нужна («на себя», «вся команда», «область»,
            // «конус»), либо оно ищет её само в момент каста (умное умение без заданной дальности).
            // Ни цели, ни точки не передаём намеренно: точка включила бы штатную проверку дистанции
            // с подходом к цели (Unit.State.cs:176).
            if (target == null) return unit.UseAbilityItem(idx, false, null, Vector3.zero);

            // Тип умения вычисляется из его режима цели (CompositeSkill.type).
            switch (skill.type)
            {
                case AbilityType.Unit:
                    return unit.UseAbilityItem(idx, false, target, Vector3.zero);

                case AbilityType.Location:
                    return unit.UseAbilityItem(idx, false, null, target.transform.position);

                case AbilityType.Active:
                    return unit.UseAbilityItem(idx, false, null, Vector3.zero);
            }

            return false;
        }

        // Индекс умения в пуле юнита (кэш). Обход бага Utils.GetAbilityIndex
        // (при «не найдено» возвращает не -1) кросс-проверкой GetAbilityByIndex — как в UIManager.BottomTables.
        private int ResolveAbilityIndex(int entry)
        {
            CompositeSkill skill = autoAbilities[entry];

            // [CR1 fix 2026-07-10] Кэш валиден, пока по индексу всё ещё лежит наше умение.
            // Пул мог измениться (тех-анлок/трансформация сдвигают индексы) → перерезолвим.
            // «Не найдено» (-1) не перерешаем каждый тик (иначе спам-лог/лишний перебор).
            if (resolved[entry])
            {
                if (cachedIndex[entry] < 0) return -1;
                if (unit != null && Utils.GetAbilityByIndex(unit, cachedIndex[entry]) == skill)
                    return cachedIndex[entry];
                // индекс устарел — перерезолвим ниже
            }

            resolved[entry] = true;
            cachedIndex[entry] = -1;
            if (skill == null || unit.abilities == null) return -1;

            int idx = Utils.GetAbilityIndex(unit.abilities, skill);
            if (idx >= 0 && Utils.GetAbilityByIndex(unit, idx) == skill)
                cachedIndex[entry] = idx;
            else
                Debug.LogWarning($"[AutoAbilityUser] У '{unit.unitName}' умение '{skill.name}' " +
                                 "не найдено в списке Abilities — авто-применение этого умения отключено.");

            return cachedIndex[entry];
        }
    }
}
