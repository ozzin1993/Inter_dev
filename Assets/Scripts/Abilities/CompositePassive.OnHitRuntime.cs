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
            if (IsClientPeer) return;                                        // правило 6
            if (onHit == null || !onHit.enabled) return;
            if (byUnit == null || byUnit.dead) return;
            if (targetUnit == null || targetUnit.dead) return;

            if (onHit.onlyDirectAttack && !directAttack) return;
            if (onHit.onlyDamageType != null && damageType != onHit.onlyDamageType) return;
            if (!OnHitTypeAllowed(targetUnit)) return;
            if (!CategoryAllowed(targetUnit, onHit.onlyTargetCategories)) return;
            if (!OnHitConditionMet(targetUnit)) return;

            OnHitState state;
            if (!onHitStates.TryGet(byUnit, out state))
            {
                state = new OnHitState();
                onHitStates.Set(byUnit, state);
            }

            if (state.cooldownLeft > 0f) return;

            // «Каждый N-й» считает удары, ПРОШЕДШИЕ условия выше: иначе фильтр по типу или состоянию
            // цели сбивал бы счёт ударами, к которым блок отношения не имеет.
            if (onHit.everyNthHit > 1)
            {
                state.hits++;
                if (state.hits < onHit.everyNthHit) return;
                state.hits = 0;
            }

            // Порядок фиксирован: сначала счёт «каждый N-й», потом бросок шанса. Не сработавший бросок
            // счёт НЕ возвращает — иначе при N=3 и шансе 0.5 блок ждал бы шестого удара вместо третьего.
            if (onHit.chance < 1f && Random.value >= onHit.chance) return;   // бросок только на сервере

            if (onHit.cooldown > 0f)
            {
                state.cooldownLeft = onHit.cooldown;
                OnHitTick.Wire();
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
                if (dt != null) target.GetDamage(extra, dt, byOwner, byUnit, false, out float _);
                else Debug.LogWarning("[Реакция «попал по цели»] Добавочный урон задан, но тип урона неизвестен — урон не нанесён.");
            }

            bool alive = target != null && !target.dead;   // добавка могла добить цель

            // 2) Состояния на цель.
            if (alive && onHit.targetEffectors != null && onHit.targetEffectors.Length > 0)
                Effector.EffectorAdd(byUnit, target, onHit.targetEffectors);

            // 3) Уязвимость — обёртка над готовым правилом входящего урона.
            if (alive && onHit.vulnerabilityDuration > 0f && onHit.vulnerabilityMultiplier > 0f
                && !Mathf.Approximately(onHit.vulnerabilityMultiplier, 1f))
                IncomingDamageModifier.Apply(target, onHit.vulnerabilityMultiplier, onHit.vulnerabilityDuration,
                                             onHit.vulnerabilityOnlyDamageType, onHit.vulnerabilityOnlyDirectIncoming);

            // 4) Вампиризм. Считаем от урона ДО брони цели — семантика прежнего вампиризма (LifestealPassive),
            //    иначе поедет баланс на бронированных целях.
            //    Носитель мог погибнуть прямо здесь: добавка урона выше способна вызвать ответный удар цели.
            if (onHit.healFromDamagePercent > 0f && byUnit != null && !byUnit.dead)
            {
                float heal = dmg * onHit.healFromDamagePercent;
                if (heal > 0f) byUnit.ChangeHP(heal);
            }

            // 5) Оглушение. Штатный Unit.Stun сам уважает иммунитет к контролю.
            //    Бьём ЦЕЛЬ. Класс Basher оглушал targetUnit.target — цель цели; это его дефект,
            //    здесь он намеренно не воспроизводится.
            if (alive && onHit.stunSeconds > 0f) target.Stun(onHit.stunSeconds);

            // 6) Отброс от носителя.
            if (alive && onHit.knockbackDistance > 0f)
                Knockback.Apply(target, byUnit.transform.position, onHit.knockbackDistance,
                                onHit.knockbackStunSeconds, onHit.knockbackRespectControlImmunity);

            // 7) Коридор по линии удара.
            if (onHit.lineWidth > 0f)
                ApplyOnHitLine(target, targetPosition, attackEffectors, dmg, damageType, byUnit, byOwner);

            // 8) Перенос состояний на соседа поражённой цели.
            if (onHit.spreadChance > 0f && onHit.spreadRadius > 0f)
                ApplyOnHitSpread(target, attackEffectors, byUnit);

            // 9) Клич союзникам вокруг носителя.
            if (onHit.cryRadius > 0f && onHit.cryEffectors != null && onHit.cryEffectors.Length > 0)
                ApplyOnHitCry(byUnit);

            // 10) Глубокая рана — урон за передвижение, готовый механизм BleedOnMove.
            if (alive && onHit.bleedEnabled && onHit.bleedDuration > 0f)
            {
                DamageType bleedType = onHit.bleedDamageType != null ? onHit.bleedDamageType : damageType;
                if (bleedType != null)
                    BleedOnMove.Apply(target, byUnit, byOwner, bleedType, onHit.bleedDamagePerMeter, onHit.bleedDuration);
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
                    u.GetDamage(lineDamage, damageType, byOwner, byUnit, false, out float _);

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
