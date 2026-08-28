using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Авто-применение умений юнитом. Компонент СВОЕГО поиска цели не ведёт: и условие срабатывания,
    /// и отбор целей живут в самом умении (конструктор скиллов) — решение Artsiom 2026-08-08.
    /// До этого настройки поиска стояли дважды, на юните и в умении, и расходились между собой.
    ///
    /// Здесь осталось ровно три вещи: список умений (порядок = приоритет), гейт по откату
    /// и возврат к команде после каста. Серверо-авторитетно — на клиенте бездействует (правило 6).
    /// Тик — штатный GameManager.Tick (0.1 с); ману, muted и требования проверяет сам UseAbilityItem.
    /// </summary>
    // Без [RequireComponent(typeof(Unit))]: ассет снимает Unit через Utils.UnitRemoveComponents
    // (апгрейд/трансформация/статик-копия), а зависимость блокировала бы удаление Unit.
    // Unit берём через GetComponent в Awake; если его нет — компонент бездействует (гарды).
    public class AutoAbilityUser : MonoBehaviour
    {
        [Header("Авто-умения")]
        [Tooltip("Умения, которые юнит применяет сам. ПОРЯДОК = ПРИОРИТЕТ: за один тик применяется ПЕРВОЕ " +
                 "готовое, остальные ждут следующего тика (0.1 с). Два каста за тик невозможны: второй прервал бы первый.\n\n" +
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

            // Гейт по откату ДО того, как умение начнёт искать цель: поиск — самая дорогая часть тика,
            // а на откате он всё равно пропал бы впустую.
            // Берём GetAbilityCooldown, а НЕ IsCooldownGood: у второго побочный эффект — сообщение
            // игроку «Wait until the cooldown is over», когда юнит выделен (Unit.Ability.cs:500).
            if (unit.GetAbilityCooldown(idx, false) > 0f) return false;

            // Уровень умения у ЭТОГО юнита — нужен стратегии и дальности.
            int level = (unit.abilityLevel != null && idx < unit.abilityLevel.Length)
                        ? Mathf.Max(0, unit.abilityLevel[idx]) : 0;

            // Условие срабатывания и цель определяет само умение.
            if (!skill.AutoCastReady(unit, level, out Unit target)) return false;

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
