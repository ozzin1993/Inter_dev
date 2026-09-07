using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Приёмник юнита — единственная дверь внутрь юнита (схема «пакет и приёмник», §1, §4).
    /// Компонент на юните: принимает пакет и исполняет цепочку обработки.
    ///
    /// НУЛЕВОЙ ШАГ СХЕМЫ (§16.2) перенёс сюда тело <c>Unit.GetDamage</c> один в один; ШАГ 4 (04.09.2026,
    /// решения Artsiom 03.09.2026) задал порядок заново: полный пакет, серверная граница, неуязвимость,
    /// один бросок «удар не достиг цели», состояния атаки из пакета, очередь реакций
    /// (<c>UnitReceiver.Queue.cs</c>). Щит по-прежнему подпиской до брони, из подписок берётся не строго
    /// наименьшее, объём щита тратится даже когда его результат не выбран — оставлено как есть (Р2 А).
    ///
    /// Смысл переезда: порядок обработки урона теперь задан в ОДНОМ месте и действует для всех
    /// источников сразу, включая те, что пакет собирать не научатся (эффекторы, ауры, зоны, реакции).
    ///
    /// Клиентский запрет ЕСТЬ с шага 4 (разведка §15.1, решение Artsiom Р1 В): у клиента приёмник только
    /// играет анимацию удара по попаданию автоатаки и ничего не считает.
    ///
    /// ШАГ 1 СХЕМЫ (контроль) РАСТВОРЁН 2026-09-03: контроль стал состоянием-эффектором, отдельного
    /// пакета контроля больше нет. Решение «пускать ли» живёт в приёме НАЛОЖЕНИЯ СОСТОЯНИЯ
    /// (<c>UnitReceiver.Statuses.cs</c>), исполнение — в <c>Units/Unit.Control.cs</c>.
    ///
    /// Настроек у приёмника на этом шаге нет, поэтому нет и сериализуемых полей.
    /// </summary>
    public partial class UnitReceiver : MonoBehaviour
    {
        /// <summary>
        /// Свой юнит. Кешируется один раз при навешивании (<see cref="Init"/>), а не ищется на каждый
        /// удар: руководство Unity прямо требует не звать поиск компонента в горячем пути.
        /// </summary>
        Unit unit;

        /// <summary>
        /// Привязать приёмник к своему юниту. Зовётся из <c>Unit.GetDamage</c> в момент навешивания.
        /// Повторный вызов безвреден — кладётся та же ссылка (образец: <c>AbsorbShield.Init</c>).
        /// </summary>
        public void Init(Unit owner)
        {
            unit = owner;
        }

        /// <summary>
        /// Принять ПОЛНЫЙ пакет урона (шаг 4 схемы «пакет и приёмник», решения Artsiom 03.09.2026 —
        /// `Документы/Дизайн/Развилка_Урон_Полным_Пакетом.md`). Порядок задан здесь ОДИН раз и действует
        /// для всех источников. Возвращает «юнит погиб от этого пакета»; <paramref name="damageDealt"/> —
        /// сколько здоровья реально снято всеми записями.
        ///
        /// Порядок: клиент → только анимация (Р1 В) · неуязвимость (Р9) · один бросок «удар не достиг цели»
        /// из суммы промаха бьющего и ухода жертвы (Р3, Р4) · записи урона по порядку: правила входящего,
        /// подписки (щит — как есть, до брони, Р2 А), броня с пробитием из пакета, таблица типов, зажимы,
        /// здоровье; погиб — остаток пакета не применяется · состояния атаки бьющего живому при прямой
        /// атаке (Р7) · анимация удара у хоста · реакции (их пакеты — в очередь, Р5).
        /// Зовётся только через <see cref="Dispatch"/> (очередь) из воронки <c>Unit.GetDamage(in DamagePacket)</c>.
        /// </summary>
        public bool Receive(in DamagePacket p, out float damageDealt)
        {
            damageDealt = 0f;

            // 1. Клиент (решение Artsiom Р1 В, 03.09.2026): приёмник строго серверный, у клиента остаётся
            //    ТОЛЬКО анимация удара по попаданию автоатаки — без расчёта урона, без состояний, без реакций.
            //    Полоска здоровья у клиента меняется по синку сервера (NetworkDataSync.Economy.cs, абсолютное
            //    здоровье). До шага 4 клиент считал автоатаки локально (AttackPlay — общий код обоих пиров).
            if (NetworkConnectionHandler.isClient)
            {
                if (p.directAttack && unit.hasHitAnim && unit.FoWVisible) unit.animator.CrossFade("hit", unit.crossFadeTime, 0, 0f);
                return false;
            }

            // 1а. Правило частоты фактов презентации (§15, решение Artsiom Р5 от 07.09.2026): надпись
            //     полагается только обычной атаке по откату. Прямая атака — единственный вид пакета, где
            //     она бывает: у урона в секунду от состояний, аур, зон, умений и реакций directAttack = false
            //     (Effectors/Effector.cs, CompositePassive.Properties.cs, GroundDamageZone.cs). Остаётся
            //     отсечь луч: он тоже прямая атака, но бьёт каждый кадр, и признака «луч» в пакете нет —
            //     смотрим схему атаки бьющего (правило 7: новых полей в пакет не заводим).
            bool showFacts = p.directAttack && NotContinuous(p.attackingUnit);

            // 2. Неуязвимость (решение Р9): единственная проверка на все источники, включая те, что минуют
            //    выбор целей (урон в секунду, ауры, зоны, реакции, отложенный урон). До шага 4 флаг в приёме
            //    урона не читался вовсе (§2.3 схемы). Факт презентации — §15, точка 1.
            if (unit.isInvulnerable)
            {
                // Строка собирается только при подробном уровне: неуязвимый манекен под уроном в секунду
                // и аурами проходит здесь каждый тик, а Verbose проверяет уровень уже ВНУТРИ себя.
                if (InterflowDebug.VerboseOn)
                    InterflowDebug.Verbose("НЕУЯЗВИМ: " + InterflowDebug.Name(unit) + " — урон от " +
                                           InterflowDebug.Name(p.attackingUnit) + " не принят");
                if (showFacts) RaiseBattleFact(BattleFactReason.Invulnerable);
                return false;
            }

            // 3. Один бросок «удар не достиг цели» (решение Р3): шанс промаха бьющего из пакета (только прямая
            //    атака) плюс сумма шансов ухода правил жертвы, зажим до единицы. Чей вклад сработал — не
            //    разбирается (§13 схемы): событие одно на жертву (решение Р4), приходит и при промахе бьющего.
            float avoid = (p.directAttack ? p.missChance : 0f) + InterflowCombat.HitAvoidChance(unit, in p);
            if (avoid > 0f && UnityEngine.Random.value < Mathf.Clamp01(avoid))
            {
                InterflowDebug.Verbose("УДАР НЕ ДОСТИГ ЦЕЛИ: " + InterflowDebug.Name(p.attackingUnit) + " → " +
                                       InterflowDebug.Name(unit) + " (суммарный шанс " + (Mathf.Clamp01(avoid) * 100f).ToString("0") + "%)");
                // Факт презентации поднимаем ДО уведомления: у события «удар не достиг цели» есть
                // подписчики со встречным ударом, и их пакеты не должны стоять между фактом и отправкой.
                if (showFacts) RaiseBattleFact(BattleFactReason.HitMissed);
                InterflowCombat.NotifyHitMissed(unit, p.attackingUnit);
                return false;
            }

            // 4. Записи урона по порядку. Тело одной записи — прежняя цепочка нулевого шага без изменений
            //    чисел (правила входящего → подписки → броня → таблица → зажимы → здоровье), кроме двух
            //    принятых сдвигов: бросков в правилах больше нет (см. шаг 3), а пробитие берётся из пакета
            //    (§13 схемы), а не из реестра по бьющему. Погиб — остаток пакета не применяется (§4 схемы).
            bool died = false;
            int count = p.RecordCount;
            for (int i = 0; i < count; i++)
            {
                DamageRecord record = p.Record(i);
                if (record.damageType == null) continue;   // запись без типа (пустой пакет умения) — считать нечем
                float amount = record.amount;

                // [Interflow fix 2026-07-24 combat-hub] Правила входящего жертвы: множители и вычеты числом.
                // Считаем ДО штатных колбэков, чтобы щит поглощал уже итоговую величину.
                amount = InterflowCombat.ModifyIncomingDamage(unit, p.attackingUnit, record.damageType, amount, p.directAttack);

                // Перебор подписок скопирован дословно с нулевого шага, включая вторую ветку: она допускает
                // РОСТ урона, когда снижения не было. Это не «взять наименьшее» — упрощать нельзя.
                // Каждый подписчик получает одно и то же входное число, а не результат предыдущего.
                // Щит живёт здесь — до таблицы типов и до брони (решение Artsiom Р2 А: как есть).
                float finalDamage = amount;
                foreach (var c in unit.OnBeforeGetDamageCallbacks)
                {
                    float damageChanged = c.Callback(unit, c.Level, amount, p.directAttack);
                    if (damageChanged < finalDamage) finalDamage = damageChanged;
                    else if (finalDamage >= amount && damageChanged > finalDamage) finalDamage = damageChanged;
                }
                amount = finalDamage;

                // Пробитие брони — из пакета (кладёт отправитель из реестра, §13 схемы).
                float effectiveArmor = unit.armor * (1f - Mathf.Clamp01(p.armorPierce));
                if (InterflowDebug.VerboseOn && p.armorPierce > 0f && unit.armor > 0f)
                    InterflowDebug.Verbose("ПРОБИТИЕ применено: " + InterflowDebug.Name(p.attackingUnit) + " бьёт " +
                                           InterflowDebug.Name(unit) + " — броня " + unit.armor.ToString("0.#") +
                                           " → " + effectiveArmor.ToString("0.#"));

                // Итог по таблице «тип брони × тип урона» и броне
                float dealt = amount * GameManager.Instance.damageToArmor[unit.armorType.index * GameManager.Instance.DTAWidth + record.damageType.index] * (1 - ((0.06f * effectiveArmor) / (1 + 0.06f * effectiveArmor)));

                if (dealt < 0) dealt = 0;
                if (dealt > unit.health) dealt = unit.health;

                damageDealt += dealt;
                died = unit.ChangeHP(-dealt);

                // Число урона (§15, точка 4). За ЗАПИСЬ и строго ДО Die: смерть снимает netID из реестра,
                // а отправка сообщения требует, чтобы юнит в нём ещё числился (NetworkDataSync.CanSendAbout).
                // Подъём после цикла не случился бы вовсе — при смерти цикл обрывается и Receive выходит ниже.
                if (showFacts) RaiseBattleFact(BattleFactReason.DamageDealt, dealt);

                if (died)
                {
                    unit.Die(p.attackingPlayer, p.attackingUnit);
                    break;   // остаток пакета не применяется
                }
            }

            if (died) return true;

            // 5. Состояния атаки бьющего (решение Р7): только при прямой атаке, после урона, только живому.
            //    До шага 4 их вешал бьющий сам (Unit.DealDamage) и без проверки признака — урон умения
            //    «от имени юнита» тоже вешал состояния его автоатаки.
            if (p.directAttack && p.attackEffectors != null && p.attackEffectors.Length > 0)
            {
                if (p.attackingUnit != null) Effector.EffectorAdd(p.attackingUnit, unit, p.attackEffectors);
                else Effector.EffectorAdd(p.attackingPlayer, unit, p.attackEffectors);
            }

            // 6. Анимация удара у хоста — как до шага 4.
            if (unit.hasHitAnim && p.directAttack && unit.FoWVisible) unit.animator.CrossFade("hit", unit.crossFadeTime, 0, 0f);

            // 6а. Щит поглотил часть или весь урон этого пакета (§15, точка 3). Признак ставится на ЖЕРТВУ,
            //     а не на запись (InterflowCombat.absorbedThisHit), и гасится в NotifyDamaged — поднимаем
            //     ДО него и ОДИН раз на пакет: цикл подписок лежит внутри цикла записей, подъём там дал бы
            //     надпись на каждую запись. Погиб — сюда не доходим (выход выше), и это принято.
            if (showFacts && InterflowCombat.AbsorbedThisHit(unit)) RaiseBattleFact(BattleFactReason.ShieldAbsorbed);

            // 7. [Interflow fix 2026-07-24 combat-hub] Реакции на получение урона (контрудар, ответная заморозка).
            //    Их пакеты уходят в очередь (Dispatch: обработка уже идёт) и разбираются после этого пакета.
            //    Тип урона для слушателей — первой записи, как у одиночного удара.
            InterflowCombat.NotifyDamaged(unit, p.attackingUnit, p.damageType, damageDealt, p.directAttack);

            return false;
        }

        // ================= КОНТРОЛЬ ==============================================================
        // Шаг 1 схемы. Приёмник РЕШАЕТ, применяется ли контроль; исполняет юнит.
        // Порядок проверок сохранён дословно, с одной принятой сменой поведения (решение Artsiom
        // 28.08.2026): иммунитет к контролю теперь спрашивается для ВСЕХ ТРЁХ видов, а не только
        // у оглушения. Немота и обезоруживание начнут отскакивать от иммунных.
        //
        // Отсев «есть ли кому адресовать пакет» (staticObject / dead / !canAttack) остался в воронках
        // Unit.State.cs (решение Artsiom 28.08.2026): дойти до приёмника — значит тронуть игровой
        // объект (TryGetComponent, при первом обращении AddComponent), а на трупе этого делать нельзя.
        // Так же устроен шаг 0: dead проверяется в обёртке GetDamage (Unit.Combat.cs), а не здесь.

        /// <summary>
        /// Компонент иммунитета к контролю, если он на юните есть. Кешируется ЛЕНИВО и только когда
        /// НАЙДЕН: компонент навешивается динамически по ходу матча (SkillBuff.cs:96,
        /// CompositePassive.Properties.cs:181, ControlImmunityPassive.cs:40), поэтому запоминать
        /// «его нет» нельзя — он может появиться позже. Найденный не уничтожается никогда
        /// (Remove() лишь уменьшает счётчик источников), поэтому повторный поиск после находки не нужен.
        /// </summary>
        ControlImmunity controlImmunity;

        /// <summary>
        /// Иммунен ли юнит к контролю прямо сейчас. Спрашивают и приёмник, и <see cref="Knockback"/>
        /// (отброс и рывок) — вместо собственного поиска компонента на каждый вызов, как требует §3.1 схемы.
        /// </summary>
        public bool ControlImmune
        {
            get
            {
                if (controlImmunity == null) TryGetComponent(out controlImmunity);

                return controlImmunity != null && controlImmunity.Active;
            }
        }

        // [Interflow fix 2026-09-03 control-as-effectors] Receive(in ControlPacket) СНЕСЁН вместе
        // с пакетом: контроль перестал быть отдельным механизмом и стал состоянием. Решение
        // «пускать ли контроль» переехало в наложение состояния — UnitReceiver.Statuses.cs,
        // где иммунитет отбивает наложение целиком. Исполнение — Units/Unit.Control.cs.
        // ControlImmune остался: его по-прежнему спрашивают наложение состояний и Knockback.

        // ================= СОПРОТИВЛЕНИЯ ========================================================
        // [Interflow fix 2026-09-03 status-resistances] Сопротивления и слабости к категориям состояний.
        // Приёмник спрашивает компонент по механике (§4 схемы) — тем же приёмом, что иммунитет выше.

        /// <summary>
        /// Носитель сопротивлений, если он на юните есть. Кешируется ЛЕНИВО и только когда НАЙДЕН —
        /// по той же причине, что <see cref="controlImmunity"/>: компонент навешивают пассивки
        /// и бафы по ходу матча (первый Add), «его нет» запоминать нельзя. Найденный не уничтожается
        /// (Remove лишь снимает вклады), поэтому повторный поиск после находки не нужен.
        /// </summary>
        UnitResistances resistances;

        /// <summary>
        /// Действующее сопротивление категории в долях: положительное режет состояние, отрицательное
        /// (слабость) усиливает, 0 — вкладов нет. Спрашивает наложение состояний
        /// (<c>UnitReceiver.Statuses.cs</c>) на каждый пакет — без поиска компонента в горячем пути.
        /// </summary>
        public float Resistance(EffectorCategory category)
        {
            if (category == EffectorCategory.None) return 0f;
            if (resistances == null) TryGetComponent(out resistances);

            return resistances != null ? resistances.Effective(category) : 0f;
        }

        // ================= ФАКТЫ ПРЕЗЕНТАЦИИ ====================================================
        // [Interflow 2026-09-07 §15] Приёмник поднимает разовый ФАКТ «по юниту произошло вот что»
        // и ПРИЧИНУ; про надписи, слова и цвета боевой код не знает ничего (решение Artsiom Р6).
        // Рисует клиентская сборка. Точки подъёма: неуязвимость, удар не достиг цели, щит поглотил
        // и число урона — здесь; отказы наложения состояний — в UnitReceiver.Statuses.cs.

        /// <summary>
        /// Не непрерывная ли атака у источника. Правило частоты §15 (решение Artsiom Р5 от 07.09.2026):
        /// луч бьёт и вешает состояния КАЖДЫЙ КАДР, надписей он не даёт. Признака «луч» в пакете нет
        /// и заводить его нельзя (правило 7) — спрашиваем схему атаки самого источника.
        /// Источника нет (зона, отложенный урон) — считаем, что это не луч.
        /// </summary>
        static bool NotContinuous(Unit source)
        {
            return source == null || source.attackType != AttackType.Continuous;
        }

        /// <summary>
        /// Поднять разовый факт боя по своему юниту. Идёт через сетевой хаб: он шлёт сообщение клиентам
        /// И поднимает факт локально у хоста (<c>NetworkDataSync.UnitBattleFactSend</c>) — сообщение
        /// канала до сервера не доходит. Хаба нет (сеть не поднята) — факта нет, как у величины щита
        /// и у статусов состояний.
        /// </summary>
        void RaiseBattleFact(BattleFactReason reason, float value = 0f)
        {
            if (NetworkDataSync.Instance != null) NetworkDataSync.Instance.UnitBattleFactSend(unit, reason, value);
        }
    }
}
