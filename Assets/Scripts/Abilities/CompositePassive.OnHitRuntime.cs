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

            CallbackAdd(unit.OnAfterDamageDealCallbacks, this, level, OnHitApply);

            if (onHit.cooldown > 0f) OnHitTick.Wire();
        }

        /// <summary>Умение закрылось — снимаем подписку. Счётчик и откат носителя остаются намеренно.</summary>
        void UnwireOnHit(Unit unit, int level)
        {
            if (unit == null) return;

            CallbackRemove(unit.OnAfterDamageDealCallbacks, this, level);
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

            OnHitState state;
            if (!onHitStates.TryGet(byUnit, out state))
            {
                state = new OnHitState();
                onHitStates.Set(byUnit, state);
            }

            if (state.cooldownLeft > 0f)
            {
                if (InterflowDebug.FullOn)
                    LogOnHitSkipped(byUnit, targetUnit, "идёт откат блока, осталось " + state.cooldownLeft.ToString("0.#") + " сек");
                return;
            }

            // «Каждый N-й» считает удары, ПРОШЕДШИЕ условия выше: иначе фильтр по типу или состоянию
            // цели сбивал бы счёт ударами, к которым блок отношения не имеет.
            if (onHit.everyNthHit > 1)
            {
                state.hits++;

                if (InterflowDebug.FullOn) LogOnHitCounter(byUnit, state.hits, onHit.everyNthHit, false);

                if (state.hits < onHit.everyNthHit) return;

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

                if (!passed) return;
            }

            if (onHit.cooldown > 0f)
            {
                state.cooldownLeft = onHit.cooldown;
                OnHitTick.Wire();

                if (InterflowDebug.FullOn) LogOnHitCooldown(byUnit, onHit.cooldown);
            }

            ApplyOnHitEffects(targetUnit, targetPosition, effectors, dmg, damageType, byUnit, byOwner);
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
        /// <summary>Порядок фиксирован кодом; добавка урона идёт первой, потому что может добить цель.</summary>
        void ApplyOnHitEffects(Unit target, Vector3 targetPosition, Effector[] attackEffectors, float dmg,
                               DamageType damageType, Unit byUnit, int byOwner)
        {
            // 1) Добавочный урон. Через GetDamage, а НЕ DealDamage: второй заново поднял бы это же
            //    событие и дал рекурсию (так же сделано в EveryNthAttack).
            //    Признак прямой атаки — false: основной удар уже спровоцировал цель и соседей,
            //    добавка провоцировать второй раз не должна.
            float extra = onHit.extraDamageFlat + dmg * onHit.extraDamageMultiplier;
            if (extra > 0f)
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
            if (onHit.healFromDamagePercent > 0f && byUnit != null && !byUnit.dead)
            {
                float heal = dmg * onHit.healFromDamagePercent;
                if (heal > 0f)
                {
                    byUnit.ChangeHP(heal);
                    Fact(byUnit, BattleFactReason.PassiveOnHitBrick, 4);

                    if (InterflowDebug.FullOn)
                        LogOnHitEffect(byUnit, target, "4 вампиризм",
                                       "возвращено=" + heal.ToString("0.#") +
                                       " | доля от урона=" + onHit.healFromDamagePercent.ToString("0.##") +
                                       " | урон удара=" + dmg.ToString("0.#"));
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
            if (onHit.lineWidth > 0f)
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
            if (onHit.spreadChance > 0f && onHit.spreadRadius > 0f)
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
