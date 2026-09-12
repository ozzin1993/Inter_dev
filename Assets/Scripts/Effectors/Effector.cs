using UnityEngine;

namespace StrategyCore
{
    // Effectors are temporary effects that modify a unit�s parameters, either buffing or nerfing them. They can be applied through attacks or abilities and are commonly used in auras.
    // Effectors appear in the unit�s Status UI and last for a specified duration.

    /// <summary>
    /// Категория состояния — адрес, по которому носителя находят сопротивления и слабости
    /// (решения Artsiom 29.08–03.09.2026, каталог утверждён по чтению 18 ассетов и умений).
    /// Категории контроля: сопротивление режет ВРЕМЯ наложения; числовые: сопротивление режет СИЛУ.
    /// Тег ставится ЯВНО у каждого ассета, из признаков контроля не выводится; одно состояние — одна категория.
    /// Добавить категорию дёшево (новое значение в конец), переименовать использованную — дорого
    /// (значение сериализовано в ассетах числом).
    /// </summary>
    public enum EffectorCategory
    {
        [InspectorName("Нет")]                  None,
        [InspectorName("Оглушение")]            Stun,
        [InspectorName("Немота")]               Mute,
        [InspectorName("Безоружие")]            Disarm,
        [InspectorName("Слепота")]              Blind,
        [InspectorName("Замедление движения")]  MoveSlow,
        [InspectorName("Замедление атаки")]     AttackSlow,
        [InspectorName("Периодический урон")]   DamageOverTime
    }

    public class Effector : ScriptableObject
    {
        [EffectorID]
        public int id;

        [Header("Text")]
        public string displayName;
        public Texture2D icon;
        [TextArea(5, 10)]
        public string description;

        [Space(10)]
        [Tooltip("This VFX transform will be added as a child of the unit`s effects holder")]
        public VFXReferencer VFX;
        [Tooltip("Should the VFX appear above the unit`s head")]
        public bool aboveHead;

        [Header("Effects")]
        [Tooltip("Should this effector be permanent and have no duration")]
        public bool permanent = false;
        [Tooltip("After how many seconds this effector should wear off. For aura effectors it should be twice the gameManagerTickRate (0.2s)")]
        public float duration = 0.2f;
        [Tooltip("When the effector stacks, its effects will increase each time it is applied. \nFor example, the Damage Over Time effect caused by an attack will damage twice as much if the unit was attacked twice. \nWhen false, a similar effector can be added only once by the same team")]
        public bool stacks = false; // Abilities that stack are not shown in status

        [Header("Damage Over Time")]
        [Tooltip("How much damage per second is applied to the unit")]
        public int damageAmount;
        [Tooltip("Damage type that is used in damage over time")]
        public DamageType damageType;

        [Header("Invisibility")]
        [Tooltip("Should this unit upon acquiring this effector become invisible")]
        public bool makeInvisible;
        [Tooltip("Carrier of this effector will be revealed by the team that applied the effect")]
        public bool revealInvisible;

        [Header("Passive changes")]
        [Tooltip("Turns on passive effects of the effector")]
        public bool passiveEffectsOn = false;
        [Tooltip("What unit parameters should change when this effector is applied")]
        public AbilityPassiveEffects passiveEffects;

        // [Interflow fix 2026-08-29 heal-through-receiver] Шаг 3 схемы «пакет и приёмник»: множитель
        // получаемого лечения. ТОЛЬКО ДАННЫЕ — поведения здесь нет, считает и применяет их приёмник
        // (Units/UnitReceiver.Heal.cs). Два поля, а не одно: числом 1 нельзя отличить «поставил единицу
        // руками» от «не трогал», а валидатору это различие нужно (решение Artsiom 30.08.2026).
        [Header("Получаемое лечение")]
        [Tooltip("Включает множитель получаемого лечения. Выключено — состояние на лечение носителя не влияет")]
        public bool healReceivedMultiplierOn = false;
        [Tooltip("Во сколько раз меняется лечение, которое получает носитель: 0,9 — лечения на 10 % меньше, " +
                 "1,25 — на четверть больше. Ноль означает «лечение не проходит»; отрицательное значение зажимается в ноль. " +
                 "Учитывается только при включённой галке выше. " +
                 "Силой наложения НЕ масштабируется — берётся ровно это число. " +
                 "Если на юните несколько таких состояний, действует ОДНО: самое сильное ослабление, " +
                 "а когда ослаблений нет — самое сильное усиление. Множители не перемножаются")]
        public float healReceivedMultiplier = 1f;

        // [Interflow fix 2026-09-03 control-as-effectors] Контроль стал СОСТОЯНИЕМ (решение Artsiom
        // 30.08.2026): «юнит оглушён» = «на нём висит хоть один эффектор с признаком оглушения».
        // Собственных таймеров у контроля больше нет — время отсчитывает жизненный цикл этого
        // наложения, поэтому диспел состояния снимает и контроль, а несколько наложений держат
        // контроль до истечения последнего.
        // ТОЛЬКО ДАННЫЕ — поведения здесь нет: факты собирает и переходы применяет
        // Units/Unit.Control.cs (правило: ассет говорит ЧТО, а не КАК).
        [Header("Контроль")]
        [Tooltip("Оглушает носителя: он замирает и ничего не делает, пока держится это состояние")]
        public bool stuns = false;

        [Tooltip("Накладывает немоту: носитель не может применять умения, пока держится это состояние")]
        public bool mutes = false;

        [Tooltip("Обезоруживает носителя: он не может атаковать, пока держится это состояние")]
        public bool disarms = false;

        [Tooltip("Ослепляет носителя: он с шансом промахивается прямыми атаками, пока держится это состояние")]
        public bool blinds = false;

        [Range(0f, 1f)]
        [Tooltip("Шанс промаха при ослеплении: 0 — никогда, 1 — всегда мимо. Действующий шанс — это число, " +
                 "умноженное на силу наложения. Учитывается только при включённом признаке «Ослепляет». " +
                 "Если ослеплений на носителе несколько, действует НАИБОЛЬШИЙ из шансов — они не складываются")]
        public float blindMissChance = 0f;

        // [Interflow fix 2026-09-03 status-resistances] Сопротивления и слабости к категориям состояний
        // (решения Artsiom 29.08–03.09.2026). ТОЛЬКО ДАННЫЕ — сам ассет ничего не считает: вклады держит
        // компонент Units/UnitResistances.cs, применяет приёмник Units/UnitReceiver.Statuses.cs
        // при наложении (правило: ассет говорит ЧТО, а не КАК).
        [Header("Категория состояния")]
        [Tooltip("К какой категории относится состояние — по ней его находят сопротивления и слабости носителя. " +
                 "Одно состояние — одна категория: ассет с эффектами двух категорий делится на два ассета. " +
                 "«Нет» — сопротивления это состояние не задевают. " +
                 "Категории контроля (оглушение, немота, безоружие, слепота): сопротивление режет ВРЕМЯ наложения; " +
                 "числовые (замедления, периодический урон): сопротивление режет СИЛУ. " +
                 "Сопротивление 100 % и больше — состояние не накладывается вовсе. " +
                 "Тег ставится явно, из признаков контроля не выводится")]
        public EffectorCategory category = EffectorCategory.None;

        // Technical
        // private Unit thisUnit; // Current holder of the effector
        // [HideInInspector] public Unit unitOwner; // Which player`s effector is this. If damaging one this player will be seen as a killer
        // [HideInInspector] public int owner = -1; // Which player`s effector is this. If damaging one this player will be seen as a killer
        // float currentTime;
        // 
        // // currentChanges
        // private int currentDamageChange; // Holds current percentage damage change

        // This function is called by the unit that holds this effector currently
        public static void EffectorUpdate(EffectorHolder EH, Unit unitHolder)
        {
            // if (unitHolder == null) return; // Means unit was destroyed

            // Damage
            if (EH.effector.damageAmount != 0)
            {
                // [Interflow fix 2026-08-02 effector-unify] Урон в секунду масштабируется множителем силы наложения.
                // Умение-источник: null — урон в секунду идёт от состояния, а не от умения (решение Artsiom 05.09.2026).
                DamagePacket packet = DamagePacket.Create(EH.effector.damageAmount * EH.powerMultiplier * GameManager.Instance.currentDeltaTime, EH.effector.damageType, EH.owner, EH.unitOwner, false, null, null, true);   // [Interflow fix 2026-09-04 damage-full-packet] пакет одной записи   // [Interflow 2026-09-11] periodic: тик по времени — в ленту идёт свёрнутой строкой, не строкой на тик
                unitHolder.GetDamage(in packet, out float _);
            }

            // If it is a permanent effector, we do not handle removal logic
            if (EH.effector.permanent) return;

            EH.currentTime += GameManager.Instance.currentDeltaTime;

            // REMOVAL OF EFFECTOR
            // [Interflow fix 2026-08-02 effector-unify] Длительность берётся у наложения, а не у общего ассета.
            if (EH.currentTime > EH.duration)
            {
                // Истечение по времени — самый частый способ, каким состояние уходит с юнита.
                // Идёт мимо EffectorRemove (тот про досрочное снятие), поэтому строка своя.
                if (InterflowDebug.FullOn)
                    InterflowDebug.Full("СОСТОЯНИЕ СНЯТО: " + InterflowDebug.Name(unitHolder) +
                                        " | состояние=" + (EH.effector != null ? EH.effector.name : "нет") +
                                        " | причина=истекло по времени" +
                                        " | прошло=" + EH.currentTime.ToString("0.#") +
                                        " из " + EH.duration.ToString("0.#") + " сек" +
                                        " | сила=" + EH.powerMultiplier.ToString("0.##"));

                // Remove the passive effects
                // Снимаем ТЕМ ЖЕ множителем, каким накладывали, иначе статы юнита уплывут.
                if (EH.effector.passiveEffectsOn) EH.effector.passiveEffects.RemoveEffect(unitHolder, EH.powerMultiplier);

                // Remove tha effector
                bool invisibilityAbilityPresent = false;

                for (int i = 0; i < unitHolder.effectors.Count; i++)
                {
                    // Remove the effector
                    if (unitHolder.effectors[i] == EH && SlotManager.Instance.playerTeam[unitHolder.effectors[i].owner] == SlotManager.Instance.playerTeam[EH.owner])
                    {
                        if (EH.effector.VFX != null) unitHolder.RemoveVFX(EH.effector.VFX);
                        unitHolder.effectors.RemoveAt(i);
                    }
                    else
                    {
                        // Check if other abilities are making this unit invisible
                        if (unitHolder.effectors[i].effector.makeInvisible) invisibilityAbilityPresent = true;
                    }
                }

                // Remove invisibility only if there is no other effectors making this unit invisible
                if (!invisibilityAbilityPresent && EH.effector.makeInvisible) unitHolder.SetInvisibility(false);

                // Remove visibility of the unit
                if (EH.effector.revealInvisible) unitHolder.CanBeSeen(false, EH.owner);

                if (!EH.stacks) unitHolder.OnStatusUpdate?.Invoke();

                // [Interflow fix 2026-09-03 control-as-effectors] Состояние ушло из списка — пересобрать
                // контроль. Именно этот вызов заменил таймеры StunUpdate/MuteUpdate/DisarmUpdate:
                // время контроля кончается тогда, когда истекает последнее держащее его наложение.
                unitHolder.RecalculateControl();

                // [Interflow fix 2026-08-05 unit-status-sync] Эффектор истёк — сообщить клиентам «снят»
                // (фикс §8.6), но только если на юните не осталось других наложений того же эффектора
                // (разная сила/длительность сосуществуют — значок ещё заслужен).
                if ((EH.effector.icon != null || EH.effector.VFX != null) && NetworkDataSync.Instance != null
                    && !HasEffectorWithId(unitHolder, EH.effector.id))
                    NetworkDataSync.Instance.UnitStatusEffectorRemoveSend(unitHolder, EH.effector.id);
            }
        }

        // This function adds the effects of this effector to the unit
        /// <summary>Накладывает эффектор на юнита.</summary>
        /// <param name="powerMultiplier">Множитель силы наложения: масштабирует пассивные изменения статов
        /// и урон в секунду. 1 — ровно то, что записано в ассете.</param>
        /// <param name="durationOverride">Длительность этого наложения, секунды. Значение ≤ 0 — брать из ассета.
        /// У бессрочных эффекторов игнорируется.</param>
        /// <param name="restoring">Восстановление сохранённого состояния: приёмник пропустит проверку
        /// иммунитета к контролю. Боевой код это НЕ ставит — только загрузка сохранения.</param>
        public static void EffectorAdd(Unit unit, Effector effector, Unit unitOwner, int owner, float currentTime = 0,
                                       float powerMultiplier = 1f, float durationOverride = -1f, bool restoring = false)
        {
            // [Interflow fix 2026-08-06 no-effectors-on-buildings] Решение Artsiom: на здания эффекты
            // не вешаются. Заодно закрывает краш ядра: slow-эффектор звал ChangeMoveSpeed, а у зданий
            // нет NavMeshAgent — EffectorAdd обрывался исключением на полпути (воспроизведено 2026-08-06).
            if (unit.unitType == UnitType.Building) return;

            // [Interflow fix 2026-08-23 no-effectors-on-dead] Решение Artsiom: на мёртвых эффекты
            // не вешаются. Удар, убивший цель, тут же вешал на неё эффекторы атаки (GetDamage убивает
            // синхронно, Die уже снял netID из реестра на всех пирах) — клиентам уходило сообщение
            // канала статусов по снятому netID: «Desync! Unit netID… should exist on client».
            // Одна точка покрывает все боевые вызовы, включая будущие.
            // Разбор: Документы/Аудиты/Анализ_Рассинхрон_Состояние_На_Трупе.md.
            if (unit.dead) return;

            // Шаг 2 схемы «пакет и приёмник» (§11.2, §12): воронка собирает пакет и отдаёт его
            // приёмнику. Вся приёмная часть — расчёт длительности и стакинга, слипание одинаковых
            // наложений, создание держателя, применение и отправка статуса — переехала в
            // UnitReceiver.Statuses.cs один в один.
            // Проверки выше остались здесь по решению Artsiom 28.08.2026: это отсев «есть ли кому
            // адресовать пакет», и он обязан отработать ДО обращения к приёмнику — дойти до приёмника
            // значит тронуть игровой объект (TryGetComponent, при первом обращении AddComponent),
            // а на трупе и на здании этого делать нельзя. Так же устроены шаг 0 (Unit.Combat.cs:127)
            // и шаг 1 (Unit.State.cs). Сигнатура и значения по умолчанию не изменились — ни одно
            // из мест вызова не трогается, включая массивные перегрузки ниже.
            EffectorPacket packet = new EffectorPacket(effector, unitOwner, owner, currentTime, powerMultiplier, durationOverride, restoring);

            // Загрузка сохранения молчит (решение Artsiom 07.09.2026): она возвращает уже посчитанные
            // при сохранении силу и длительность, и десятки строк разом ничего не объясняют.
            if (InterflowDebug.FullOn && !restoring)
                InterflowDebug.Full("ПАКЕТ СОСТОЯНИЕ: → " + InterflowDebug.Name(unit) +
                                    " | состояние=" + (effector != null ? effector.name : "нет") +
                                    " | категория=" + (effector != null ? effector.category.ToString() : "нет") +
                                    " | от=" + (unitOwner != null ? InterflowDebug.Name(unitOwner) : "игрок " + owner) +
                                    " | сила=" + powerMultiplier.ToString("0.##") +
                                    " | длительность=" + (durationOverride > 0f ? durationOverride.ToString("0.#") + " сек" : "из ассета"));

            unit.ReceiverEnsure().Receive(in packet);
        }

        // Add Effector[] by unitOwner
        public static void EffectorAdd(Unit ownerUnit, Unit targetUnit, Effector[] effector)
        {
            for (int i = 0; i < effector.Length; i++)
            {
                EffectorAdd(targetUnit, effector[i], ownerUnit, ownerUnit.owner);
            }
        }

        // Add Effector[] by owner only
        public static void EffectorAdd(int owner, Unit targetUnit, Effector[] effector)
        {
            for (int i = 0; i < effector.Length; i++)
            {
                EffectorAdd(targetUnit, effector[i], null, owner);
            }
        }

        // Removes specified effector from a unit
        public static void EffectorRemove(Unit unitHolder, EffectorHolder EH)
        {
            // Досрочное снятие (диспел). Истечение по времени идёт другим путём — внутри EffectorUpdate.
            if (InterflowDebug.FullOn)
                InterflowDebug.Full("СОСТОЯНИЕ СНЯТО: " + InterflowDebug.Name(unitHolder) +
                                    " | состояние=" + (EH != null && EH.effector != null ? EH.effector.name : "нет") +
                                    " | причина=снято досрочно" +
                                    " | прошло=" + (EH != null ? EH.currentTime.ToString("0.#") : "?") +
                                    " из " + (EH != null ? EH.duration.ToString("0.#") : "?") + " сек");

            // Remove the passive effects
            // [Interflow fix 2026-08-02 effector-unify] Снимаем тем же множителем, каким накладывали.
            if (EH.effector.passiveEffectsOn) EH.effector.passiveEffects.RemoveEffect(unitHolder, EH.powerMultiplier);

            // Remove tha effector
            bool invisibilityAbilityPresent = false;

            for (int i = 0; i < unitHolder.effectors.Count; i++)
            {
                // Remove the effector
                if (unitHolder.effectors[i] == EH && SlotManager.Instance.playerTeam[unitHolder.effectors[i].owner] == SlotManager.Instance.playerTeam[EH.owner])
                {
                    if (EH.effector.VFX != null) unitHolder.RemoveVFX(EH.effector.VFX);
                    unitHolder.effectors.RemoveAt(i);
                }
                else
                {
                    // Check if other abilities are making this unit invisible
                    if (unitHolder.effectors[i].effector.makeInvisible) invisibilityAbilityPresent = true;
                }
            }

            // Remove invisibility only if there is no other effectors making this unit invisible
            if (!invisibilityAbilityPresent && EH.effector.makeInvisible) unitHolder.SetInvisibility(false);

            // Remove visibility of the unit
            if (EH.effector.revealInvisible) unitHolder.CanBeSeen(false, EH.owner);

            if (!EH.stacks) unitHolder.OnStatusUpdate?.Invoke();

            // [Interflow fix 2026-09-03 control-as-effectors] Диспел снял состояние — пересобрать контроль.
            // Отсюда и берётся принятое поведение «диспел снимает оглушение»: отдельного кода для этого нет.
            unitHolder.RecalculateControl();

            // [Interflow fix 2026-08-05 unit-status-sync] Досрочное снятие (диспел) — сообщить клиентам
            // «снят» (фикс §8.6: раньше досрочное снятие и permanent-эффекторы висели у клиента вечно).
            if ((EH.effector.icon != null || EH.effector.VFX != null) && NetworkDataSync.Instance != null
                && !HasEffectorWithId(unitHolder, EH.effector.id))
                NetworkDataSync.Instance.UnitStatusEffectorRemoveSend(unitHolder, EH.effector.id);
        }

        // [Interflow fix 2026-08-05 unit-status-sync] Остались ли на юните наложения эффектора с этим id
        // (для решения, можно ли слать клиентам «статус снят»).
        private static bool HasEffectorWithId(Unit unit, int effectorId)
        {
            for (int i = 0; i < unit.effectors.Count; i++)
                if (unit.effectors[i].effector != null && unit.effectors[i].effector.id == effectorId) return true;
            return false;
        }

        // Returns the effector by its ID
        public static Effector GetEffectorByID(int effectorID)
        {
            if (GameManager.Instance.gameEffectors.TryGetValue(effectorID, out Effector effector))
            {
                return effector;
            }

            return null;
        }
    }
}
