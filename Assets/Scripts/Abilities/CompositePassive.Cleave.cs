using UnityEngine;

namespace StrategyCore
{
    // ==== КОНСТРУКТОР ПАССИВКИ: БЛОК 11 «РАССЕЧЕНИЕ» (правило 22 — партиал по фиче) ====
    // Семья «рассечение», проект Документы/Механики/Рассечение_Проект.md, решения Artsiom 59–63
    // от 18.09.2026. Три ассета семьи: Haldor_CourageSplash, OgreGladiator_CrushingHammer, WarWolf_Cleave.
    //
    // ЧТО ДЕЛАЕТ: когда носитель наносит урон цели, соседи получают ДОЛЮ этого же урона.
    // Круг стоит либо на цели удара, либо на самом носителе — по флагу.
    //
    // ПОЧЕМУ ОТДЕЛЬНЫЙ БЛОК, А НЕ КИРПИЧ РЕАКЦИИ 5 (решение 59 = ветка «б»): кирпич наследовал бы
    // ВЕСЬ гейт реакции 5 — откат, шанс, счёт «каждый N-й» и фильтры цели (OnHitGatePassed). Рассечению
    // ничего этого не нужно, и отката с шансом у него нет и не заводится (правило 7).
    // ПРИНЯТАЯ ЦЕНА решения 59: у одного ассета появляется ВТОРАЯ подписка на тот же штатный хук
    // Unit.OnAfterDamageDealCallbacks и дубль проверок «носитель жив / цель жива».
    //
    // ПОЧЕМУ ЭТО ПОТРЕБОВАЛО СТРОКИ В InterflowAbility: штатный CallbackAdd считает подписку занятой
    // по паре (умение, уровень) и вторую запись того же ассета молча отбрасывает. Заведена пара
    // CallbackAddDistinct / CallbackRemoveDistinct по ТРОЙКЕ (умение, уровень, обработчик).
    // CompositePassive.OnHitRuntime.cs при этом НЕ ТРОНУТ (решение 63): реакция 5 как подписывалась
    // старым CallbackAdd и снималась старым CallbackRemove, так и подписывается.
    // ИНВАРИАНТ ПОРЯДКА: UnwireCleave зовётся в Lock ПЕРВЫМ среди снятий — снятие по паре у соседей
    // берёт первую попавшуюся запись и иначе унесло бы чужую.
    //
    // УСЛОВИЕ ПО ЗДОРОВЬЮ НОСИТЕЛЯ (решение 60): берётся ГОТОВЫЙ тиковый механизм семьи «здоровье
    // носителя» — перечисление PassiveBlockCondition, порог, чистые BlockActive и GateStep
    // (CompositePassive.HealthGate.cs). Своего второго вида условия по здоровью здесь НЕТ.
    // Тиковый переход и есть «выдать / снять блок»: Grant подписывает на хук, Revoke отписывает.
    // ПРИНЯТАЯ ЦЕНА: включение и выключение с задержкой до одного тика (0,1 с).
    //
    // УСЛОВИЕ ПО СОСТОЯНИЮ НОСИТЕЛЯ (решение 60) читается В МОМЕНТ УДАРА, до расчёта: это условие
    // мгновенное, тик ему не нужен, а спрашивать его по тику значило бы завести второй механизм.
    //
    // УЛУЧШЕНИЕ ТЕХНОЛОГИЕЙ (решение 61): ссылка на технологию и улучшенные радиус и доля ПОЛНЫМИ
    // числами внутри блока — как у вампиризма реакции 5 (решение 45). Второй ассет пассивки
    // с требуемой технологией ЗАПРЕЩЁН: у пассивки замки считаются по каждому ассету отдельно,
    // базовый ассет без requiredTech открыт всегда (Units/Unit.AbilityLocks.cs), и после открытия
    // технологии рассечение срабатывало бы ДВАЖДЫ.
    //
    // ВСЁ СЕРВЕРНОЕ (правило 6): обработчик удара выходит на клиенте первой строкой. Подписка при этом
    // ставится на обоих пирах — Unit.AbilityLocks зовёт Unlock без клиентского гейта, как и у реакции 5.
    //
    // ПЕР-ЮНИТ ХРАНИЛИЩЕ: своего словаря блок не заводит — состояние подписки живёт двумя полями
    // в Carrier (CompositePassive.cs), а весь словарь carriers чистится в Init() на старте матча
    // (принцип Artsiom 17.09 «каждый матч изолирован»). Второе хранилище было бы вторым местом
    // для той же правды (правило 5).

    /// <summary>
    /// Блок 11. Рассечение: удар носителя задевает соседей долей этого же урона.
    /// ЭТО НЕ РАЗЛЁТ (блок 6): разлёт правит параметры штатного взрыва автоатаки юнита, считает
    /// величину со спадом по дистанции и настраиваемой выборки не имеет.
    /// </summary>
    [System.Serializable]
    public class PassiveCleaveBlock
    {
        [Tooltip("Включить блок: когда носитель наносит урон цели, соседи получают долю этого же урона. " +
                 "Отката, шанса и счёта «каждый N-й» у рассечения нет — оно срабатывает на каждом ударе, " +
                 "прошедшем условия ниже.")]
        public bool enabled;

        [Tooltip("Центр круга — САМ НОСИТЕЛЬ, а не цель удара. ВКЛ — задеты все вокруг носителя, " +
                 "включая саму цель удара (она получит и основной урон, и долю рассечения). " +
                 "ВЫКЛ — круг стоит на цели удара, и сама цель из выборки исключается: свой урон она уже получила.")]
        public bool centerOnCaster;

        [Tooltip("Радиус круга, метры. 0 или меньше — блок ничего не делает.")]
        public float radius;

        [Tooltip("Доля от урона ОСНОВНОГО удара, которую получает каждый задетый сосед. " +
                 "1 — столько же, 0.3 — тридцать процентов. 0 или меньше — блок ничего не делает. " +
                 "Спада по дистанции нет: доля плоская, в отличие от разлёта.")]
        public float damageFraction;

        [Tooltip("Кого задевает рассечение. Обычно враг + юнит + земля + вода. " +
                 "Ни одного флага — выборка вернёт пусто и блок промолчит.")]
        public UnitSelector selector;

        [Tooltip("Требуемое состояние НА НОСИТЕЛЕ: без него рассечение не срабатывает. " +
                 "Проверяется в момент удара, сравнением по номеру ассета. Пусто — условия нет.")]
        public Effector requiredCasterEffector;

        [Tooltip("Когда блок работает. «Пока здоровья меньше доли» — рассечение подключается и отключается " +
                 "штатным тиком 0,1 с, поэтому переход идёт с задержкой до одного тика.")]
        public PassiveBlockCondition carrierCondition = PassiveBlockCondition.Always;

        [Tooltip("Порог для условия «пока здоровья меньше доли»: 0.5 — рассечение работает, пока здоровья " +
                 "носителя СТРОГО меньше половины максимума. 0 — условия по здоровью нет.")]
        [Range(0f, 1f)]
        public float carrierHpBelow;

        [Tooltip("Технология, которая УЛУЧШАЕТ рассечение. Пусто — улучшения нет, работают числа выше. " +
                 "Открыта у владельца носителя — вместо радиуса и доли выше берутся улучшенные ниже. " +
                 "ВАЖНО: второй ассет пассивки с требуемой технологией для этого НЕ заводится — " +
                 "замки пассивок считаются по каждому ассету отдельно, оба оказались бы открыты, " +
                 "и рассечение сработало бы дважды.")]
        public Technology upgradeTechnology;

        [Tooltip("Улучшенный радиус, ПОЛНЫМ числом, а не прибавкой: 2 — два метра. " +
                 "Читается вместо обычного радиуса, когда технология выше открыта у владельца носителя.")]
        public float upgradedRadius;

        [Tooltip("Улучшенная доля урона, ПОЛНЫМ числом, а не прибавкой: 0.6 — шестьдесят процентов. " +
                 "Читается вместо обычной доли, когда технология выше открыта у владельца носителя.")]
        public float upgradedDamageFraction;

        [Tooltip("Визуал срабатывания рассечения: играется ОДИН раз за срабатывание, а не на каждой " +
                 "задетой цели. Точка события — цель удара, а при центре круга на носителе — сам носитель. " +
                 "Пусто — без визуала.")]
        public EventPresentation presentation = new EventPresentation();
    }

    public partial class CompositePassive
    {
        [Header("Блок 11 — рассечение")]
        public PassiveCleaveBlock cleave = new PassiveCleaveBlock();

        // ======================================================= ЧИСТЫЕ ФУНКЦИИ ==
        // Вынесены статикой ради тестов режима редактора: обработчик удара из теста недостижим
        // (нужны живой хук юнита, сетка поиска и приёмник урона). Тот же приём, что у семьи здоровья.

        /// <summary>Включён ли блок. Отдельным предикатом — читается из трёх мест.</summary>
        public static bool CleaveEnabled(PassiveCleaveBlock b)
        {
            return b != null && b.enabled;
        }

        /// <summary>
        /// Должно ли рассечение быть подключено у носителя с таким здоровьем. Условие по здоровью
        /// считается ГОТОВОЙ <see cref="BlockActive"/> семьи «здоровье носителя» (решение 60),
        /// своей копии порога здесь нет (правило 1).
        /// </summary>
        public static bool CleaveWanted(PassiveCleaveBlock b, float carrierHealth, float carrierMaxHealth)
        {
            if (!CleaveEnabled(b)) return false;

            return BlockActive(b.carrierCondition, b.carrierHpBelow, carrierHealth, carrierMaxHealth);
        }

        /// <summary>
        /// Открыто ли улучшение технологией. Пустая ссылка — улучшения НЕТ ВОВСЕ, и безопасная обёртка
        /// здесь не зовётся намеренно: она отвечает «условия нет» (true) на пустую ссылку, и улучшенные
        /// числа применялись бы всегда. Та же развилка, что у вампиризма (OnHitHealPercent).
        /// Зовётся только с сервера — весь путь блока выходит на клиенте первой строкой.
        /// </summary>
        public static bool CleaveUpgraded(PassiveCleaveBlock b, int owner)
        {
            if (b == null || b.upgradeTechnology == null) return false;

            return InterflowCombat.IsTechUnlockedSafe(b.upgradeTechnology, owner);
        }

        /// <summary>Радиус этого срабатывания: улучшенный ПОЛНЫМ числом либо обычный.</summary>
        public static float CleaveRadius(PassiveCleaveBlock b, bool upgraded)
        {
            if (b == null) return 0f;

            return upgraded ? b.upgradedRadius : b.radius;
        }

        /// <summary>Доля от урона основного удара для этого срабатывания: улучшенная либо обычная.</summary>
        public static float CleaveFraction(PassiveCleaveBlock b, bool upgraded)
        {
            if (b == null) return 0f;

            return upgraded ? b.upgradedDamageFraction : b.damageFraction;
        }

        /// <summary>
        /// Годятся ли числа: нулевой или отрицательный радиус и такая же доля означают,
        /// что блок включён и не делает ничего. Ровно это ловит правило проверки Р52.
        /// </summary>
        public static bool CleaveNumbersUsable(float radius, float fraction)
        {
            return radius > 0f && fraction > 0f;
        }

        /// <summary>
        /// Центр круга на плоскости: носитель или точка удара. Высота не участвует — вся выборка
        /// в проекте плоская (Utils.GetUnitsInRadius принимает Vector2).
        /// </summary>
        public static Vector2 CleaveCenter(bool centerOnCaster, Vector3 carrierPosition, Vector3 targetPosition)
        {
            Vector3 p = centerOnCaster ? carrierPosition : targetPosition;

            return new Vector2(p.x, p.z);
        }

        /// <summary>
        /// Кого выборка обязана пропустить ЗАРАНЕЕ (параметр excludeUnit у Utils.GetUnitsInRadius).
        /// Основная цель исключается ТОЛЬКО когда круг стоит на ней: свой урон она уже получила.
        /// При центре на носителе цель — обычный сосед, и рассечение её задевает.
        /// </summary>
        public static Unit CleaveExcluded(bool centerOnCaster, Unit mainTarget)
        {
            return centerOnCaster ? null : mainTarget;
        }

        /// <summary>
        /// Годится ли найденный юнит под удар рассечения. Носитель отсеивается ВСЕГДА — сам себя
        /// он не бьёт; основная цель — по тому же правилу, что и в <see cref="CleaveExcluded"/>.
        /// Проверка повторяет отсев выборки намеренно: тот же приём у коридора реакции 5,
        /// и он же закрывает случай, когда носитель попал в круг вокруг собственной цели.
        /// </summary>
        public static bool CleaveHits(Unit u, Unit carrier, Unit mainTarget, bool centerOnCaster)
        {
            if (u == null || u.dead) return false;
            if (u == carrier) return false;
            if (!centerOnCaster && u == mainTarget) return false;

            return true;
        }

        /// <summary>
        /// Пакет урона соседу. Признак прямой атаки — ВСЕГДА false, и это инвариант: с true
        /// пакет заново поднял бы Unit.OnAfterDamageDealCallbacks, на котором висит сам блок,
        /// и рассечение зарекурсило бы на собственных соседях. По той же причине урон идёт
        /// через Unit.GetDamage, а не через DealDamage.
        /// </summary>
        public static DamagePacket CleavePacket(float amount, DamageType damageType, int byOwner,
                                                Unit byUnit, Ability source)
        {
            return DamagePacket.Create(amount, damageType, byOwner, byUnit, false, source);
        }

        /// <summary>
        /// Висит ли на носителе требуемое состояние. Сравнение по номеру ассета — тем же готовым
        /// приёмом, что у выбора цели (Combat/SkillTargeting.cs): ссылки на ассет в наложении
        /// может не быть той же самой, а номер разошёлся по пирам и стабилен.
        /// Пустая ссылка — условия нет.
        /// </summary>
        public static bool CleaveCarrierStateMet(Unit carrier, Effector required)
        {
            if (required == null) return true;
            if (carrier == null || carrier.effectors == null) return false;

            for (int i = 0; i < carrier.effectors.Count; i++)
            {
                EffectorHolder eh = carrier.effectors[i];
                if (eh != null && eh.effector != null && eh.effector.id == required.id) return true;
            }

            return false;
        }

        // ======================================================= ЖИЗНЕННЫЙ ЦИКЛ ==

        /// <summary>
        /// Открытие умения у юнита: посчитать условие по здоровью ДО подписки и решить, нужен ли тик.
        /// Сама подписка ставится ниже, в <see cref="WireCleave"/>, — той же строкой, что и у соседей.
        /// </summary>
        void InitCleave(Unit unit, Carrier c)
        {
            if (unit == null || c == null || !CleaveEnabled(cleave)) return;

            // Тик нужен ТОЛЬКО ради условия, которое может смениться. Рассечение «Всегда»
            // не тикает вовсе — как и блоки 2 и 3 без условия.
            c.cleaveTick = cleave.carrierCondition == PassiveBlockCondition.WhileHpBelow;

            if (InterflowDebug.FullOn && c.cleaveTick)
                LogCleaveCondition(unit, CleaveWanted(cleave, unit.health, unit.maxHealth),
                                   cleave.carrierHpBelow, "открытие умения");
        }

        /// <summary>
        /// Подписать блок на попадания носителя. Это и есть «выдать блок 11»: другого способа
        /// его включить нет. Идемпотентно по флагу носителя.
        ///
        /// Уровень берётся ИЗ <c>Carrier</c>, а не параметром: подписку ставят два разных места —
        /// открытие умения и тик условия по здоровью, — и снимает её потом третье, закрытие умения.
        /// Один источник уровня на все три (правило 5) исключает случай «подписались одним номером,
        /// снимаем другим» и запись, висящую на юните до конца матча.
        /// </summary>
        void WireCleave(Unit unit, Carrier c)
        {
            if (unit == null || c == null || !CleaveEnabled(cleave)) return;
            if (c.cleaveWired) return;                                  // уже подписан — повторно не вешаем

            // Условие по здоровью — единственный источник правды о том, работает ли блок.
            // Считается здесь, а не запоминается отдельным флагом: второй флаг был бы вторым
            // местом для той же правды и разъехался бы с подпиской (правило 5).
            if (!CleaveWanted(cleave, unit.health, unit.maxHealth)) return;

            // Не CallbackAdd: на этом же списке у того же ассета может висеть реакция 5, и штатное
            // добавление по паре (умение, уровень) молча отказало бы во второй записи.
            CallbackAddDistinct(unit.OnAfterDamageDealCallbacks, this, c.level, OnCleaveApply);
            c.cleaveWired = true;

            if (InterflowDebug.FullOn) LogCleaveWire(unit, true);
        }

        /// <summary>
        /// Снять подписку — «снять блок 11». Снятие идёт по ТРОЙКЕ (умение, уровень, обработчик):
        /// снятие по паре унесло бы подписку реакции 5 того же ассета.
        ///
        /// В <c>Lock</c> зовётся ПЕРВЫМ среди снятий: соседи снимают по паре и взяли бы первую
        /// попавшуюся запись — в том числе эту.
        /// </summary>
        void UnwireCleave(Unit unit, Carrier c)
        {
            if (unit == null || c == null || !c.cleaveWired) return;

            CallbackRemoveDistinct(unit.OnAfterDamageDealCallbacks, this, c.level, OnCleaveApply);
            c.cleaveWired = false;

            if (InterflowDebug.FullOn) LogCleaveWire(unit, false);
        }

        /// <summary>
        /// Один тик условия по здоровью для блока 11. ТОЛЬКО СЕРВЕР: зовётся из <c>OnTick</c>,
        /// а тот выходит на клиенте первой строкой.
        ///
        /// Работа делается только на ПЕРЕХОДЕ — шаг считает та же чистая <see cref="GateStep"/>,
        /// что и у блоков 2 и 3. Повторная подписка была бы безвредна (она идемпотентна), но
        /// повторное снятие каждым тиком гасило бы рассечение у здорового носителя без нужды.
        /// </summary>
        void TickCleave(Unit unit, Carrier c)
        {
            if (!CleaveEnabled(cleave)) return;
            if (cleave.carrierCondition != PassiveBlockCondition.WhileHpBelow) return;   // «Всегда» пересчитывать нечего

            bool want = CleaveWanted(cleave, unit.health, unit.maxHealth);

            HealthGateStep step = GateStep(c.cleaveWired, want);
            if (step == HealthGateStep.None) return;

            if (step == HealthGateStep.Grant) WireCleave(unit, c);
            else UnwireCleave(unit, c);

            if (InterflowDebug.FullOn) LogCleaveCondition(unit, want, cleave.carrierHpBelow, "тик");
        }

        // ============================================================ СРАБАТЫВАНИЕ ==

        /// <summary>
        /// Носитель нанёс урон цели. Подпись задана штатной структурой <c>AfterDamageDealCallback</c>.
        /// Отката, счёта и броска случайных чисел у рассечения нет (решение 59) — сразу условия и расчёт.
        /// </summary>
        /// <param name="targetPosition">Точка удара: позиция цели или место попадания.</param>
        /// <param name="dmg">Урон ОСНОВНОГО удара — от него считается доля соседям.</param>
        void OnCleaveApply(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack,
                           DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level)
        {
            if (IsClientPeer) return;                       // правило 6; не игровой отказ — молчим
            if (!CleaveEnabled(cleave)) return;             // блок выключили в ассете, а подписка ещё жива

            if (byUnit == null || byUnit.dead)
            {
                if (InterflowDebug.FullOn && byUnit != null) LogCleaveSkipped(byUnit, targetUnit, "носитель мёртв");
                return;
            }

            // Рассечение — про соседей ЦЕЛИ или про круг вокруг носителя В МОМЕНТ удара по цели.
            // Без цели (удар по земле, прилёт в пустоту) события «попал по цели» не было.
            if (targetUnit == null || targetUnit.dead)
            {
                if (InterflowDebug.FullOn) LogCleaveSkipped(byUnit, targetUnit, "цель мертва или её уже нет");
                return;
            }

            if (dmg <= 0f || damageType == null)
            {
                if (InterflowDebug.FullOn)
                    LogCleaveSkipped(byUnit, targetUnit, "у основного удара нет ни величины, ни типа урона");
                return;
            }

            // Условие по состоянию носителя — ДО расчёта (решение 60).
            if (!CleaveCarrierStateMet(byUnit, cleave.requiredCasterEffector))
            {
                if (InterflowDebug.FullOn)
                    LogCleaveSkipped(byUnit, targetUnit, "на носителе нет требуемого состояния «" +
                                                         cleave.requiredCasterEffector.name + "»");
                return;
            }

            bool upgraded = CleaveUpgraded(cleave, byOwner);
            float radius = CleaveRadius(cleave, upgraded);
            float fraction = CleaveFraction(cleave, upgraded);

            if (!CleaveNumbersUsable(radius, fraction))
            {
                if (InterflowDebug.FullOn)
                    LogCleaveSkipped(byUnit, targetUnit, "радиус=" + N(radius) + " и доля=" + N2(fraction) +
                                                         " — задевать нечем");
                return;
            }

            ApplyCleave(targetUnit, targetPosition, dmg, radius, fraction, upgraded, damageType, byUnit, byOwner, level);
        }

        /// <summary>
        /// Расчёт рассечения. Урон соседу — штатным <c>Unit.GetDamage</c> пакетом одной записи;
        /// <c>DealDamage</c> здесь ЗАПРЕЩЁН: он заново поднял бы тот же хук и дал рекурсию
        /// (та же причина, что у добавочного урона и коридора реакции 5).
        ///
        /// Набор визуала публикуется ОДИН раз за срабатывание и ДО обхода целей (решение 62) —
        /// по той же причине, по которой это делает реакция 5: показ не должен зависеть от того,
        /// кого нашла выборка и кто из найденных выжил.
        /// </summary>
        void ApplyCleave(Unit target, Vector3 targetPosition, float dmg, float radius, float fraction, bool upgraded,
                         DamageType damageType, Unit byUnit, int byOwner, int level)
        {
            Vector3 carrierPosition = byUnit.transform.position;

            // Точка события: цель удара, а при центре круга на носителе — сам носитель (решение 62).
            Unit aimUnit = cleave.centerOnCaster ? byUnit : target;
            Vector3 aimPoint = cleave.centerOnCaster ? carrierPosition : targetPosition;

            EmitEventPresentation(this, (int)AbilityEventCode.PassiveCleave, cleave.presentation,
                                  byUnit, level, aimUnit, aimPoint);

            Vector2 center = CleaveCenter(cleave.centerOnCaster, carrierPosition, targetPosition);

            Unit[] candidates = Utils.GetUnitsInRadius(center, radius, byUnit.owner, cleave.selector, -1,
                                                       CleaveExcluded(cleave.centerOnCaster, target));

            if (candidates == null || candidates.Length == 0)
            {
                if (InterflowDebug.FullOn) LogCleaveSkipped(byUnit, target, "в круге никого нет");
                return;
            }

            float neighbourDamage = dmg * fraction;
            int hit = 0;

            for (int i = 0; i < candidates.Length; i++)
            {
                Unit u = candidates[i];
                if (!CleaveHits(u, byUnit, target, cleave.centerOnCaster)) continue;

                DamagePacket packet = CleavePacket(neighbourDamage, damageType, byOwner, byUnit, this);
                u.GetDamage(in packet, out float _);

                hit++;
            }

            if (InterflowDebug.FullOn)
                LogCleaveRun(byUnit, target, radius, fraction, upgraded, neighbourDamage, hit);

            if (hit > 0)
            {
                InterflowDebug.Verbose("РАССЕЧЕНИЕ («" + PassiveDisplayName() + "»): " + InterflowDebug.Name(byUnit) +
                                       " задел соседей: " + hit);

                // Досрочная синхронизация — только когда состояние мира реально изменилось.
                // Пустой круг не меняет ничего, и гнать из-за него внеочередной пакет незачем.
                RequestForceSync();
            }
        }
    }
}
