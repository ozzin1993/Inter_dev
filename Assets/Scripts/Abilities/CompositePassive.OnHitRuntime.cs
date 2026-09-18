using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // === КОНСТРУКТОР ПАССИВКИ: ИСПОЛНЕНИЕ СОБЫТИЯ «ПОПАЛ ПО ЦЕЛИ» (правило 22 — партиал по фиче) ==
    // Поля живут в CompositePassive.OnHit.cs, здесь только подписка и исполнение.
    // Всё серверное (правило 6): урон, лечение, состояния, оглушение, отброс и броски случайных чисел.
    //
    // Точка подключения — штатный список атакующего Unit.OnAfterDamageDealCallbacks (правило 2).
    // Он копируется в снаряд, поэтому событие покрывает и дальнобойных. Регистрация идемпотентная,
    // по паре (умение, уровень) — готовые InterflowAbility.CallbackAdd / CallbackRemove.
    // Правок ядра ассета не потребовалось.
    //
    // Числа блока — скаляры (у обычных умений уровень один, решение Artsiom 2026-08-27), поэтому
    // параметр level из колбэка здесь используется ТОЛЬКО для снятия подписки, а не для выборки чисел.
    public partial class CompositePassive
    {
        /// <summary>
        /// Что блок помнит про носителя. НЕ чистится в Lock: улучшение технологии приходит парой
        /// Lock+Unlock, а счётчик «каждый N-й» обязан продолжить счёт (поведение EveryNthAttack).
        /// Весь словарь чистится в Init(), мёртвые ключи — PruneDead.
        /// </summary>
        class OnHitState
        {
            public int hits;
            public float cooldownLeft;
        }

        readonly UnitStateMap<OnHitState> onHitStates = new UnitStateMap<OnHitState>();
        readonly List<Unit> onHitBuffer = new List<Unit>();

        // Тик нужен только ради отката. Создаём лениво: инициализатор поля не может ссылаться на метод.
        TickHook onHitTickHook;
        TickHook OnHitTick { get { return onHitTickHook ?? (onHitTickHook = new TickHook(OnHitCooldownTick)); } }

        // ================================================================== ЖИЗНЬ ==

        /// <summary>Между матчами состояние не переносим (ассет переживает выход из Play).</summary>
        void ResetOnHit()
        {
            onHitStates.Clear();
            if (onHitTickHook != null) onHitTickHook.Unwire();
        }

        /// <summary>Умение открылось у юнита — подписываем блок на его попадания.</summary>
        void WireOnHit(Unit unit, int level)
        {
            if (unit == null || onHit == null || !onHit.enabled) return;

            // Счёт ударов существующему носителю НЕ сбрасываем: улучшение технологии не должно
            // обнулять «каждый N-й» (§6.1 EveryNthAttack).
            if (!onHitStates.Contains(unit)) onHitStates.Set(unit, new OnHitState());
            onHitStates.PruneDead();

            // [Interflow 2026-09-16 семья Б] ЛИБО удар, ЛИБО прилёт снаряда — не оба сразу.
            // Иначе у дальнего носителя один выстрел давал бы два прока: один на приём урона,
            // второй на прилёт снаряда. Инвариант: реакция 5 с флагом прилёта на удар НЕ подписана.
            if (onHit.procAtProjectileImpact) CallbackAdd(unit.OnProjectileImpactCallbacks, this, level, OnProjectileImpactApply);
            else CallbackAdd(unit.OnAfterDamageDealCallbacks, this, level, OnHitApply);

            if (onHit.cooldown > 0f) OnHitTick.Wire();
        }

        /// <summary>
        /// Умение закрылось — снимаем подписку. Счётчик и откат носителя остаются намеренно.
        /// Снимаем ИЗ ОБОИХ списков: флаг в ассете мог поменяться между подпиской и снятием,
        /// и висящая запись в другом списке пережила бы Lock. На чужом списке CallbackRemove молчит.
        /// </summary>
        void UnwireOnHit(Unit unit, int level)
        {
            if (unit == null) return;

            CallbackRemove(unit.OnAfterDamageDealCallbacks, this, level);
            CallbackRemove(unit.OnProjectileImpactCallbacks, this, level);
        }

        /// <summary>Штатный тик 0.1 с: крутим откаты и чистим мёртвых носителей (Unit.Die не зовёт Lock).</summary>
        void OnHitCooldownTick()
        {
            if (GameManager.Instance == null) return;

            if (onHitStates.Count == 0) { OnHitTick.Unwire(); return; }

            float dt = GameManager.Instance.currentDeltaTime;

            onHitStates.CopyKeysTo(onHitBuffer);
            for (int i = 0; i < onHitBuffer.Count; i++)
            {
                OnHitState st;
                if (!onHitStates.TryGet(onHitBuffer[i], out st)) continue;
                if (st.cooldownLeft > 0f) st.cooldownLeft = Mathf.Max(0f, st.cooldownLeft - dt);
            }

            onHitStates.PruneDead();
        }

        // ============================================================== СРАБАТЫВАНИЕ ==

        void OnHitApply(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack,
                        DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level)
        {
            if (IsClientPeer) return;                                        // правило 6; не игровой отказ — молчим
            if (onHit == null || !onHit.enabled) return;                     // выключенный блок молчит
            if (onHit.procAtProjectileImpact) return;                        // флаг поставили, а подписка на удар ещё жива

            if (byUnit == null || byUnit.dead)
            {
                if (InterflowDebug.FullOn && byUnit != null) LogOnHitSkipped(byUnit, targetUnit, "носитель мёртв");
                return;
            }

            if (targetUnit == null || targetUnit.dead)
            {
                if (InterflowDebug.FullOn) LogOnHitSkipped(byUnit, targetUnit, "цель мертва или её уже нет");
                return;
            }

            if (onHit.onlyDirectAttack && !directAttack)
            {
                if (InterflowDebug.FullOn) LogOnHitSkipped(byUnit, targetUnit, "удар не прямая атака, а блок ждёт только прямой");
                return;
            }

            if (onHit.onlyDamageType != null && damageType != onHit.onlyDamageType)
            {
                if (InterflowDebug.FullOn)
                    LogOnHitSkipped(byUnit, targetUnit, "тип урона " + TypeName(damageType) +
                                                        " не совпал с требуемым " + TypeName(onHit.onlyDamageType));
                return;
            }

            if (!OnHitTypeAllowed(targetUnit))
            {
                if (InterflowDebug.FullOn)
                    LogOnHitSkipped(byUnit, targetUnit, "тип цели " + targetUnit.unitType + " не в списке разрешённых");
                return;
            }

            if (!CategoryAllowed(targetUnit, onHit.onlyTargetCategories))
            {
                if (InterflowDebug.FullOn) LogOnHitSkipped(byUnit, targetUnit, "боевая роль цели не в списке разрешённых");
                return;
            }

            if (!OnHitConditionMet(targetUnit))
            {
                if (InterflowDebug.FullOn)
                    LogOnHitSkipped(byUnit, targetUnit, "состояние цели не подходит под условие «" +
                                                        OnHitConditionName(onHit.onlyTargetCondition) + "»");
                return;
            }

            if (!OnHitGatePassed(byUnit, targetUnit)) return;

            ApplyOnHitEffects(targetUnit, targetPosition, effectors, dmg, damageType, byUnit, byOwner, level);
        }

        /// <summary>
        /// Общий гейт срабатывания реакции 5: ОТКАТ -> СЧЁТ «каждый N-й» -> БРОСОК шанса -> постановка ОТКАТА.
        /// Порядок и поведение вынесены из OnHitApply один в один (2026-09-16, семья Б): входов теперь два —
        /// удар и прилёт снаряда, и правила у них обязаны быть одни (правило 5).
        ///
        /// Публичный: иначе порядок нечем проверить тестом режима редактора
        /// (NestedSkillTests.ГейтРеакции5_ПорядокСчётБросокОткат_НеИзменился).
        /// </summary>
        /// <param name="byUnit">Носитель: по нему ведутся счёт и откат.</param>
        /// <param name="target">Цель — только для строки лога; при прилёте в пустую землю её нет.</param>
        /// <returns>true — гейт пройден, кирпичи исполняются.</returns>
        public bool OnHitGatePassed(Unit byUnit, Unit target)
        {
            OnHitState state;
            if (!onHitStates.TryGet(byUnit, out state))
            {
                state = new OnHitState();
                onHitStates.Set(byUnit, state);
            }

            if (state.cooldownLeft > 0f)
            {
                if (InterflowDebug.FullOn)
                    LogOnHitSkipped(byUnit, target, "идёт откат блока, осталось " + state.cooldownLeft.ToString("0.#") + " сек");
                return false;
            }

            // «Каждый N-й» считает удары, ПРОШЕДШИЕ условия выше: иначе фильтр по типу или состоянию
            // цели сбивал бы счёт ударами, к которым блок отношения не имеет.
            if (onHit.everyNthHit > 1)
            {
                state.hits++;

                if (InterflowDebug.FullOn) LogOnHitCounter(byUnit, state.hits, onHit.everyNthHit, false);

                if (state.hits < onHit.everyNthHit) return false;

                if (InterflowDebug.FullOn) LogOnHitCounter(byUnit, state.hits, onHit.everyNthHit, true);

                state.hits = 0;
            }

            // Порядок фиксирован: сначала счёт «каждый N-й», потом бросок шанса. Не сработавший бросок
            // счёт НЕ возвращает — иначе при N=3 и шансе 0.5 блок ждал бы шестого удара вместо третьего.
            // Бросок делается ровно при тех же условиях, что и раньше (только когда шанс меньше единицы):
            // лишний вызов Random сдвинул бы поток случайных чисел сервера.
            if (onHit.chance < 1f)
            {
                float roll = Random.value;                                   // бросок только на сервере
                bool passed = roll < onHit.chance;

                if (InterflowDebug.FullOn) LogOnHitChance(byUnit, roll, onHit.chance, passed);

                if (!passed) return false;
            }

            if (onHit.cooldown > 0f)
            {
                state.cooldownLeft = onHit.cooldown;
                OnHitTick.Wire();

                if (InterflowDebug.FullOn) LogOnHitCooldown(byUnit, onHit.cooldown);
            }

            return true;
        }

        /// <summary>
        /// ВТОРОЙ ВХОД реакции 5: снаряд АТАКИ носителя долетел. Подписка идёт вместо удара, по флагу
        /// procAtProjectileImpact (см. WireOnHit). Цели может не быть вовсе — снаряд ушёл в пустую землю,
        /// ради этого случая событие и заведено. Фильтры по цели при её отсутствии пропускаются,
        /// тип урона и признак прямой атаки берутся у снаряда.
        /// </summary>
        void OnProjectileImpactApply(Vector3 point, Unit target, Unit byUnit, int byOwner,
                                     bool directAttack, Projectile projectile, int level)
        {
            if (IsClientPeer) return;                                        // правило 6; не игровой отказ — молчим
            if (onHit == null || !onHit.enabled) return;                     // выключенный блок молчит
            if (!onHit.procAtProjectileImpact) return;                       // флаг сняли, а подписка ещё жива

            if (byUnit == null || byUnit.dead)
            {
                if (InterflowDebug.FullOn && byUnit != null) LogOnHitSkipped(byUnit, target, "носитель мёртв");
                return;
            }

            // Цель могла погибнуть от самого удара этого снаряда — дальше блок работает так же,
            // как по пустой земле: цели нет.
            if (target != null && target.dead) target = null;

            if (onHit.onlyDirectAttack && !directAttack)
            {
                if (InterflowDebug.FullOn) LogOnHitSkipped(byUnit, target, "снаряд не прямая атака, а блок ждёт только прямой");
                return;
            }

            DamageType projectileDamageType = projectile != null ? projectile.damageType : null;
            if (onHit.onlyDamageType != null && projectileDamageType != onHit.onlyDamageType)
            {
                if (InterflowDebug.FullOn)
                    LogOnHitSkipped(byUnit, target, "тип урона снаряда " + TypeName(projectileDamageType) +
                                                    " не совпал с требуемым " + TypeName(onHit.onlyDamageType));
                return;
            }

            // Фильтры по цели считаются ТОЛЬКО когда цель есть: прилёт в землю ими не отсеивается.
            if (target != null)
            {
                if (!OnHitTypeAllowed(target))
                {
                    if (InterflowDebug.FullOn)
                        LogOnHitSkipped(byUnit, target, "тип цели " + target.unitType + " не в списке разрешённых");
                    return;
                }

                if (!CategoryAllowed(target, onHit.onlyTargetCategories))
                {
                    if (InterflowDebug.FullOn) LogOnHitSkipped(byUnit, target, "боевая роль цели не в списке разрешённых");
                    return;
                }

                if (!OnHitConditionMet(target))
                {
                    if (InterflowDebug.FullOn)
                        LogOnHitSkipped(byUnit, target, "состояние цели не подходит под условие «" +
                                                        OnHitConditionName(onHit.onlyTargetCondition) + "»");
                    return;
                }
            }

            if (!OnHitGatePassed(byUnit, target)) return;

            if (InterflowDebug.FullOn) LogOnHitImpact(byUnit, target, point);

            // Числа берём у самого снаряда: событие «долетел» величину снятого здоровья не несёт —
            // она уже разошлась по цели внутри Damage, и к прилёту в землю её нет вовсе.
            float projectileDamage = projectile != null ? projectile.damage : 0f;
            Effector[] projectileEffectors = projectile != null ? projectile.attackEffectors : null;

            ApplyOnHitEffects(target, point, projectileEffectors, projectileDamage, projectileDamageType, byUnit, byOwner, level);
        }

        /// <summary>
        /// Доля возврата вампиризма для ЭТОГО удара: условие по здоровью носителя плюс улучшение
        /// технологией внутри блока (решения Artsiom 41 и 45). 0 — вампиризма в этом ударе нет.
        ///
        /// Улучшение живёт ВНУТРИ блока намеренно: приём «второй ассет с requiredTech» на пассивке
        /// не работает — замки считаются по КАЖДОМУ ассету независимо (Units/Unit.AbilityLocks.cs),
        /// после открытия технологии открыты ОБА ассета, и доли сложились бы. У УМЕНИЙ приём остаётся
        /// в силе: там из списка выбирается первый открытый (Units/AutoAbilityUser.cs).
        ///
        /// Улучшенное число — ПОЛНОЕ, а не прибавка: в ассете читается «пятьдесят процентов»,
        /// а не «плюс двадцать к тридцати» (ветка Д1 отвергнута решением 45).
        ///
        /// Проверка технологии — штатной безопасной обёрткой над TechnologyManager: у неё есть гарды
        /// по нейтралам и по технологиям вне Resources. Зовётся только с сервера — весь путь реакции 5
        /// выходит на клиенте первой строкой.
        /// </summary>
        public static float OnHitHealPercent(PassiveOnHitBlock b, int owner, float carrierHealth, float carrierMaxHealth)
        {
            if (b == null) return 0f;

            if (!BlockActive(b.healCondition, b.healCarrierHpBelow, carrierHealth, carrierMaxHealth)) return 0f;

            // Технологии нет — условия улучшения нет вовсе. Проверять через обёртку нельзя:
            // она отвечает «условия нет» на пустую ссылку, и улучшенное число применялось бы всегда.
            if (b.healUpgradeTechnology == null) return b.healFromDamagePercent;

            return InterflowCombat.IsTechUnlockedSafe(b.healUpgradeTechnology, owner)
                   ? b.healFromDamagePercentUpgraded
                   : b.healFromDamagePercent;
        }

        bool OnHitTypeAllowed(Unit target)
        {
            if (onHit.onlyTargetTypes == null || onHit.onlyTargetTypes.Length == 0) return true;

            for (int i = 0; i < onHit.onlyTargetTypes.Length; i++)
                if (onHit.onlyTargetTypes[i] == target.unitType) return true;

            return false;
        }

        /// <summary>Русское название условия по цели — для строки отказа.</summary>
        static string OnHitConditionName(PassiveOnHitBlock.TargetCondition condition)
        {
            switch (condition)
            {
                case PassiveOnHitBlock.TargetCondition.NoAbsorbShield: return "у цели нет поглощающего щита";
                case PassiveOnHitBlock.TargetCondition.HasAbsorbShield: return "у цели есть поглощающий щит";
                case PassiveOnHitBlock.TargetCondition.Stunned: return "цель оглушена";
                case PassiveOnHitBlock.TargetCondition.BelowHpFraction: return "цель ранена ниже порога ХП";
            }

            return "без условия";
        }

        bool OnHitConditionMet(Unit target)
        {
            switch (onHit.onlyTargetCondition)
            {
                case PassiveOnHitBlock.TargetCondition.Any: return true;
                case PassiveOnHitBlock.TargetCondition.NoAbsorbShield: return !AbsorbShield.IsActiveOn(target);
                case PassiveOnHitBlock.TargetCondition.HasAbsorbShield: return AbsorbShield.IsActiveOn(target);
                case PassiveOnHitBlock.TargetCondition.Stunned: return target.stunned;
                case PassiveOnHitBlock.TargetCondition.BelowHpFraction:
                    return target.maxHealth > 0f && target.health / target.maxHealth < onHit.conditionHpFraction;
            }

            return false;
        }

        // ================================================================== ЭФФЕКТЫ ==

        // [Interflow 2026-09-10 passive-facts-2] Каждый сработавший кирпич пишет надпись над НОСИТЕЛЕМ:
        // до этого реакция 5 не показывала ничего вовсе, и проверить её в бою было нечем. Носитель, а не
        // цель, — потому что умение принадлежит ему, и так же устроены реакции 1–4. Номер кирпича едет
        // числом факта, слово берёт клиент из ассета настроек (правило 3). Кирпичи 7–9 пишут ЗАПУСК:
        // сколько целей реально задето, знают сами методы, и это их строки уровня «Подробно».
        /// <summary>
        /// Порядок фиксирован кодом; добавка урона идёт первой, потому что может добить цель.
        ///
        /// ЦЕЛИ МОЖЕТ НЕ БЫТЬ (2026-09-16, семья Б): второй вход — прилёт снаряда в пустую землю.
        /// Без цели из одиннадцати кирпичей работают ТОЛЬКО клич союзникам (9) и вложенное умение (11):
        /// всё остальное — про цель, и без неё это были бы неявные срабатывания «по воздуху».
        /// </summary>
        /// <param name="targetPosition">Точка приложения: место удара или точка прилёта снаряда.</param>
        /// <param name="level">Уровень ПАССИВКИ — с ним исполняется вложенное умение (кирпич 11).</param>
        void ApplyOnHitEffects(Unit target, Vector3 targetPosition, Effector[] attackEffectors, float dmg,
                               DamageType damageType, Unit byUnit, int byOwner, int level)
        {
            bool hasTarget = target != null;

            // [Interflow 2026-09-17] Хозяин набора «носитель попал по цели». Публикуется ДО кирпичей
            // по той же причине, что у каста умения: визуал не должен зависеть от того, выжила ли цель.
            EmitEventPresentation(this, (int)AbilityEventCode.PassiveOnHit, onHit.presentation,
                                  byUnit, level, target, targetPosition);

            // 1) Добавочный урон. Через GetDamage, а НЕ DealDamage: второй заново поднял бы это же
            //    событие и дал рекурсию (так же сделано в EveryNthAttack).
            //    Признак прямой атаки — false: основной удар уже спровоцировал цель и соседей,
            //    добавка провоцировать второй раз не должна.
            float extra = onHit.extraDamageFlat + dmg * onHit.extraDamageMultiplier;
            if (hasTarget && extra > 0f)
            {
                DamageType dt = onHit.extraDamageType != null ? onHit.extraDamageType : damageType;
                if (dt != null)
                {
                    DamagePacket packet = DamagePacket.Create(extra, dt, byOwner, byUnit, false, this);   // [Interflow fix 2026-09-04 damage-full-packet] пакет одной записи
                    target.GetDamage(in packet, out float _);
                    Fact(byUnit, BattleFactReason.PassiveOnHitBrick, 1);

                    if (InterflowDebug.FullOn)
                        LogOnHitEffect(byUnit, target, "1 добавочный урон",
                                       "урон=" + extra.ToString("0.#") + " | тип=" + TypeName(dt) +
                                       " | доля от удара=" + onHit.extraDamageMultiplier.ToString("0.##") +
                                       " | числом=" + onHit.extraDamageFlat.ToString("0.#"));
                }
                else Debug.LogWarning("[Реакция «попал по цели»] Добавочный урон задан, но тип урона неизвестен — урон не нанесён.");
            }

            bool alive = target != null && !target.dead;   // добавка могла добить цель

            // 2) Состояния на цель.
            if (alive && onHit.targetEffectors != null && onHit.targetEffectors.Length > 0)
            {
                Effector.EffectorAdd(byUnit, target, onHit.targetEffectors);
                Fact(byUnit, BattleFactReason.PassiveOnHitBrick, 2);

                if (InterflowDebug.FullOn)
                    LogOnHitEffect(byUnit, target, "2 состояния на цель", "состояния=" + EffectorNames(onHit.targetEffectors));
            }

            // 3) Уязвимость — обёртка над готовым правилом входящего урона.
            if (alive && onHit.vulnerabilityDuration > 0f && onHit.vulnerabilityMultiplier > 0f
                && !Mathf.Approximately(onHit.vulnerabilityMultiplier, 1f))
            {
                IncomingDamageModifier.Apply(target, onHit.vulnerabilityMultiplier, onHit.vulnerabilityDuration, this,
                                             onHit.vulnerabilityOnlyDamageType, onHit.vulnerabilityOnlyDirectIncoming);
                                             Fact(byUnit, BattleFactReason.PassiveOnHitBrick, 3);

                if (InterflowDebug.FullOn)
                    LogOnHitEffect(byUnit, target, "3 уязвимость",
                                   "множитель=" + onHit.vulnerabilityMultiplier.ToString("0.##") +
                                   " | длительность=" + onHit.vulnerabilityDuration.ToString("0.#") + " сек" +
                                   " | тип урона=" + TypeName(onHit.vulnerabilityOnlyDamageType) +
                                   " | только прямой входящий=" + Yes(onHit.vulnerabilityOnlyDirectIncoming));
            }

            // 4) Вампиризм. [Interflow fix 2026-09-09 hit-outcome] Считаем от ФАКТИЧЕСКИ СНЯТОГО здоровья:
            //    с 09.09.2026 бьющий передаёт колбэкам снятое, а не заявленный номинал пакета
            //    (Unit.DealDamage, решение Artsiom по §11.3 промта «Боевой конвейер» — принцип «доли
            //    считаются от снятого» главнее прежней семантики «от урона ДО брони»). ПРИНЯТАЯ ЦЕНА:
            //    возврат здоровья на бронированных целях стал меньше, чем был у LifestealPassive.
            //    Носитель мог погибнуть прямо здесь: добавка урона выше способна вызвать ответный удар цели.
            //    [Interflow 2026-09-17, решения Artsiom 41 и 45] Доля возврата теперь считается: у неё
            //    своё условие по здоровью НОСИТЕЛЯ и своё улучшение технологией внутри блока.
            //    Условие читается в момент удара — тик ему не нужен: вампиризм ничего не выдаёт
            //    и не снимает, он разовый.
            if (hasTarget && byUnit != null && !byUnit.dead)
            {
                float healPercent = OnHitHealPercent(onHit, byOwner, byUnit.health, byUnit.maxHealth);

                if (healPercent > 0f)
                {
                    float heal = dmg * healPercent;
                    if (heal > 0f)
                    {
                        byUnit.ChangeHP(heal);
                        Fact(byUnit, BattleFactReason.PassiveOnHitBrick, 4);

                        if (InterflowDebug.FullOn)
                            LogOnHitEffect(byUnit, target, "4 вампиризм",
                                           "возвращено=" + heal.ToString("0.#") +
                                           " | доля от урона=" + healPercent.ToString("0.##") +
                                           " | улучшено технологией=" + Yes(healPercent > onHit.healFromDamagePercent) +
                                           " | урон удара=" + dmg.ToString("0.#"));
                    }
                }
                else if (InterflowDebug.FullOn && onHit.healFromDamagePercent > 0f
                         && !BlockActive(onHit.healCondition, onHit.healCarrierHpBelow, byUnit.health, byUnit.maxHealth))
                {
                    LogOnHitEffect(byUnit, target, "4 вампиризм",
                                   "пропущен | причина=условие по здоровью носителя не выполнено" +
                                   " | порог=" + onHit.healCarrierHpBelow.ToString("0.##"));
                }
            }

            // 5) Оглушение. Штатный Unit.Stun сам уважает иммунитет к контролю.
            //    Бьём ЦЕЛЬ. Класс Basher оглушал targetUnit.target — цель цели; это его дефект,
            //    здесь он намеренно не воспроизводится.
            if (alive && onHit.stunSeconds > 0f)
            {
                target.Stun(onHit.stunSeconds, byUnit, byOwner);
                Fact(byUnit, BattleFactReason.PassiveOnHitBrick, 5);

                if (InterflowDebug.FullOn)
                    LogOnHitEffect(byUnit, target, "5 оглушение", "длительность=" + onHit.stunSeconds.ToString("0.#") + " сек");
            }

            // 6) Отброс от носителя.
            if (alive && onHit.knockbackDistance > 0f)
            {
                Knockback.Apply(target, byUnit.transform.position, onHit.knockbackDistance,
                                onHit.knockbackStunSeconds, onHit.knockbackRespectControlImmunity, byUnit, byOwner);
                                Fact(byUnit, BattleFactReason.PassiveOnHitBrick, 6);

                if (InterflowDebug.FullOn)
                    LogOnHitEffect(byUnit, target, "6 отброс",
                                   "дальность=" + onHit.knockbackDistance.ToString("0.#") +
                                   " | заморозка=" + onHit.knockbackStunSeconds.ToString("0.#") + " сек" +
                                   " | уважает иммунитет=" + Yes(onHit.knockbackRespectControlImmunity));
            }

            // 7) Коридор по линии удара.
            // Строки эффектов 7–9 пишутся ДО вызова и означают ЗАПУСК блока с этими числами:
            // сколько целей реально задето (и прошёл ли бросок переноса), знают сами методы —
            // это их существующие строки уровня «Подробно».
            if (hasTarget && onHit.lineWidth > 0f)
            {
                if (InterflowDebug.FullOn)
                    LogOnHitEffect(byUnit, target, "7 коридор по линии удара запущен",
                                   "ширина=" + onHit.lineWidth.ToString("0.#") +
                                   " | продолжение за цель=" + onHit.lineExtraDistance.ToString("0.#") +
                                   " | доля урона=" + onHit.lineDamagePercent.ToString("0.##") +
                                   " | максимум целей=" + (onHit.lineMaxTargets > 0 ? onHit.lineMaxTargets.ToString() : "без ограничения"));

                ApplyOnHitLine(target, targetPosition, attackEffectors, dmg, damageType, byUnit, byOwner);
                Fact(byUnit, BattleFactReason.PassiveOnHitBrick, 7);
            }

            // 8) Перенос состояний на соседа поражённой цели.
            if (hasTarget && onHit.spreadChance > 0f && onHit.spreadRadius > 0f)
            {
                if (InterflowDebug.FullOn)
                    LogOnHitEffect(byUnit, target, "8 перенос состояний на соседей запущен",
                                   "шанс=" + onHit.spreadChance.ToString("0.##") +
                                   " | радиус=" + onHit.spreadRadius.ToString("0.#") +
                                   " | соседей=" + onHit.spreadNeighbourCount +
                                   " | состояния атаки=" + Yes(onHit.spreadAttackEffectors) +
                                   " | дополнительно=" + EffectorNames(onHit.spreadExtraEffectors));

                ApplyOnHitSpread(target, attackEffectors, byUnit);
                Fact(byUnit, BattleFactReason.PassiveOnHitBrick, 8);
            }

            // 9) Клич союзникам вокруг носителя.
            if (onHit.cryRadius > 0f && onHit.cryEffectors != null && onHit.cryEffectors.Length > 0)
            {
                if (InterflowDebug.FullOn)
                    LogOnHitEffect(byUnit, target, "9 клич союзникам запущен",
                                   "радиус=" + onHit.cryRadius.ToString("0.#") +
                                   " | состояния=" + EffectorNames(onHit.cryEffectors) +
                                   " | тиры " + onHit.cryMinAllyTier + "–" + onHit.cryMaxAllyTier +
                                   " | себе тоже=" + Yes(onHit.cryIncludeSelf));

                ApplyOnHitCry(byUnit);
                Fact(byUnit, BattleFactReason.PassiveOnHitBrick, 9);
            }

            // 10) Глубокая рана — урон за передвижение, готовый механизм BleedOnMove.
            if (alive && onHit.bleedEnabled && onHit.bleedDuration > 0f)
            {
                DamageType bleedType = onHit.bleedDamageType != null ? onHit.bleedDamageType : damageType;
                if (bleedType != null)
                {
                    BleedOnMove.Apply(target, byUnit, byOwner, bleedType, onHit.bleedDamagePerMeter, onHit.bleedDuration, this);
                    Fact(byUnit, BattleFactReason.PassiveOnHitBrick, 10);

                    if (InterflowDebug.FullOn)
                        LogOnHitEffect(byUnit, target, "10 глубокая рана",
                                       "урон за метр=" + onHit.bleedDamagePerMeter.ToString("0.#") +
                                       " | длительность=" + onHit.bleedDuration.ToString("0.#") + " сек" +
                                       " | тип=" + TypeName(bleedType));
                }
            }

            // 11) Вложенное умение — ПОСЛЕДНИМ: добавка урона (1) и отброс (6) уже отработали, так что
            //     умение видит цель в том состоянии, в каком её оставили остальные кирпичи. Цель могла
            //     погибнуть здесь же — тогда умение исполняется по ТОЧКЕ, а не по цели.
            //     Откат, стоимость и замок вложенного не тратятся: это часть прока, а не отдельный каст
            //     (CompositeSkill.ExecuteNested). Глубина вложения и серверный гейт — там же.
            if (onHit.procSkill != null)
            {
                Unit aim = alive ? target : null;

                if (InterflowDebug.FullOn)
                    LogOnHitEffect(byUnit, aim, "11 прок → умение «" + onHit.procSkill.name + "»",
                                   "цель=" + (aim != null ? "есть" : "нет, исполняем по точке") +
                                   " | точка=" + targetPosition.ToString("0.#") +
                                   " | уровень=" + level);

                if (onHit.procSkill.ExecuteNested(byUnit, byOwner, level, aim, targetPosition))
                    Fact(byUnit, BattleFactReason.PassiveOnHitBrick, 11);
            }

            RequestForceSync();
        }

        /// <summary>Коридор от носителя через цель. Расчёт перенесён из PiercingLine один в один.</summary>
        void ApplyOnHitLine(Unit target, Vector3 targetPosition, Effector[] attackEffectors, float dmg,
                            DamageType damageType, Unit byUnit, int byOwner)
        {
            Vector3 endPoint = target != null ? target.transform.position : targetPosition;

            Vector2 from = new Vector2(byUnit.transform.position.x, byUnit.transform.position.z);
            Vector2 to = new Vector2(endPoint.x, endPoint.z);
            Vector2 dir = to - from;

            float length = dir.magnitude;
            if (length <= 0.01f) return;

            dir /= length;
            length += Mathf.Max(0f, onHit.lineExtraDistance);

            // Кандидатов ищем в круге, накрывающем весь коридор, затем отсеиваем по ширине.
            Vector2 center = from + dir * (length * 0.5f);
            float searchRadius = length * 0.5f + onHit.lineWidth;

            Unit[] candidates = Utils.GetUnitsInRadius(center, searchRadius, byUnit.owner, onHit.lineSelector, -1, target);
            if (candidates == null || candidates.Length == 0) return;

            float lineDamage = dmg * onHit.lineDamagePercent;
            float halfWidth = onHit.lineWidth * 0.5f;
            int hit = 0;

            for (int i = 0; i < candidates.Length; i++)
            {
                Unit u = candidates[i];
                if (u == null || u.dead || u == target || u == byUnit) continue;

                Vector2 p = new Vector2(u.transform.position.x, u.transform.position.z) - from;

                float along = Vector2.Dot(p, dir);                    // проекция на линию удара
                if (along < 0f || along > length) continue;           // позади носителя или за концом коридора

                float offset = Mathf.Abs(p.x * dir.y - p.y * dir.x);  // расстояние до линии
                if (offset > halfWidth) continue;

                if (lineDamage > 0f && damageType != null)
                {
                    DamagePacket packet = DamagePacket.Create(lineDamage, damageType, byOwner, byUnit, false, this);   // [Interflow fix 2026-09-04 damage-full-packet] пакет одной записи
                    u.GetDamage(in packet, out float _);
                }

                if (onHit.lineApplyAttackEffectors && attackEffectors != null && attackEffectors.Length > 0)
                    Effector.EffectorAdd(byUnit, u, attackEffectors);

                hit++;
                if (onHit.lineMaxTargets > 0 && hit >= onHit.lineMaxTargets) break;
            }

            if (hit > 0)
                InterflowDebug.Verbose("КОРИДОР («" + PassiveDisplayName() + "»): " + InterflowDebug.Name(byUnit) +
                                       " задел по линии целей: " + hit);
        }

        /// <summary>Перенос состояний на соседей поражённой цели. Перенесено из EffectorSpread.</summary>
        void ApplyOnHitSpread(Unit target, Effector[] attackEffectors, Unit byUnit)
        {
            if (target == null) return;
            if (Random.value >= onHit.spreadChance) return;           // бросок только на сервере

            bool hasAttack = onHit.spreadAttackEffectors && attackEffectors != null && attackEffectors.Length > 0;
            bool hasExtra = onHit.spreadExtraEffectors != null && onHit.spreadExtraEffectors.Length > 0;
            if (!hasAttack && !hasExtra) return;

            Vector3 pos = target.transform.position;
            Unit[] neighbours = Utils.GetUnitsInRadius(new Vector2(pos.x, pos.z), onHit.spreadRadius,
                                                       byUnit.owner, onHit.spreadSelector, -1, target);
            if (neighbours == null) return;

            int applied = 0;
            for (int i = 0; i < neighbours.Length && applied < onHit.spreadNeighbourCount; i++)
            {
                Unit n = neighbours[i];
                if (n == null || n.dead || n == target || n == byUnit) continue;

                if (hasAttack) Effector.EffectorAdd(byUnit, n, attackEffectors);
                if (hasExtra) Effector.EffectorAdd(byUnit, n, onHit.spreadExtraEffectors);

                applied++;
            }

            if (applied > 0)
                InterflowDebug.Verbose("ПЕРЕНОС («" + PassiveDisplayName() + "»): с " + InterflowDebug.Name(target) +
                                       " на соседей: " + applied);
        }

        /// <summary>Клич союзникам вокруг носителя с фильтром по тиру. Перенесено из OnCombatStartCry.</summary>
        void ApplyOnHitCry(Unit byUnit)
        {
            Vector3 pos = byUnit.transform.position;
            Unit[] allies = Utils.GetUnitsInRadius(new Vector2(pos.x, pos.z), onHit.cryRadius, byUnit.owner,
                                                   onHit.crySelector, -1, onHit.cryIncludeSelf ? null : byUnit);
            if (allies == null) return;

            int count = 0;
            for (int i = 0; i < allies.Length; i++)
            {
                Unit a = allies[i];
                if (a == null || a.dead) continue;
                if (a.tier < onHit.cryMinAllyTier || a.tier > onHit.cryMaxAllyTier) continue;

                Effector.EffectorAdd(byUnit, a, onHit.cryEffectors);
                count++;
            }

            if (count > 0)
                InterflowDebug.Verbose("КЛИЧ («" + PassiveDisplayName() + "»): " + InterflowDebug.Name(byUnit) +
                                       " задел союзников: " + count);
        }
    }
}
