using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ====== КОНСТРУКТОР ПАССИВКИ: ИСПОЛНЕНИЕ ОСИ «РЕАКЦИИ» (правило 22 — партиал по фиче) ==
    // Поля живут в CompositePassive.Reactions.cs, здесь только подписки и исполнение.
    // Всё серверное (правило 6): урон, лечение, состояния, призыв и броски случайных чисел.
    //
    // Точки подключения — уже существующие (правило 2), правок ассета не потребовалось:
    //   • снижение и уход от удара — правило входящего урона InterflowCombat.IncomingRule
    //     (зовётся из Unit.GetDamage ДО брони и знает бьющего — отсюда и «только спереди»);
    //   • ответный удар — InterflowCombat.DamagedListenerAdd (урон уже нанесён, бьющий известен);
    //   • гибель носителя — штатное Unit.OnDie ПРЯМО у носителя. Хаб смертей здесь намеренно НЕ
    //     используется: его покрытие ограничено путями спавна (башни в него не входят), а прямая
    //     подписка работает для любого носителя — так же, как это делал компонент DeathEffects;
    //   • добивание — событие хаба MatchManager.OnUnitDeathServer: узнать, что добил ИМЕННО наш
    //     носитель, можно только там, где видно жертву и убийцу одновременно;
    //   • порог здоровья — штатное Unit.OnHPChange.
    public partial class CompositePassive
    {
        /// <summary>Что выдано носителю по оси реакций. Снимаем ровно то, что выдали.</summary>
        class ReactionState
        {
            public int level;

            public InterflowCombat.IncomingRule incomingRule;      // снижение и уход от удара
            public InterflowCombat.DamagedHandler damagedHandler;  // ответный удар
            public Action<Unit, int, Unit, bool> dieHandler;       // гибель носителя
            public Action hpHandler;                               // порог здоровья

            public float counterCooldownLeft;
            public float hpCooldownLeft;
            public bool hpBelowLatched;                            // ниже порога уже сработали
        }

        readonly Dictionary<Unit, ReactionState> reactionCarriers = new Dictionary<Unit, ReactionState>();
        readonly List<Unit> reactionTickBuffer = new List<Unit>();
        bool reactionTickWired;
        bool killHubWired;

        // ============================================================== ЖИЗНЬ ==

        /// <summary>Между матчами состояние не переносим (ассет переживает выход из Play).</summary>
        void ResetReactions()
        {
            reactionCarriers.Clear();
            UnwireReactionTick();
            UnwireKillHub();
        }

        /// <summary>Умение открылось у юнита — подписываем включённые реакции.</summary>
        void WireReactions(Unit unit, int level)
        {
            if (unit == null || reactionCarriers.ContainsKey(unit)) return;
            if (!AnyReactionEnabled()) return;

            ReactionState st = new ReactionState { level = level };

            WireIncomingRule(unit, st, level);
            WireCounter(unit, st, level);
            WireDeath(unit, st);
            WireKill(unit, st);
            WireHpBelow(unit, st);

            if (st.incomingRule == null && st.damagedHandler == null &&
                st.dieHandler == null && st.hpHandler == null && !IsKillEnabled()) return;

            reactionCarriers[unit] = st;

            if (NeedsReactionTick()) WireReactionTick();
        }

        /// <summary>Умение закрылось — снимаем ровно выданное.</summary>
        void UnwireReactions(Unit unit)
        {
            if (unit == null || !reactionCarriers.TryGetValue(unit, out ReactionState st)) return;

            if (st.incomingRule != null) InterflowCombat.IncomingRuleRemove(unit, st.incomingRule);
            if (st.damagedHandler != null) InterflowCombat.DamagedListenerRemove(unit, st.damagedHandler);
            if (st.dieHandler != null) unit.OnDie -= st.dieHandler;
            if (st.hpHandler != null) unit.OnHPChange -= st.hpHandler;

            reactionCarriers.Remove(unit);

            if (reactionCarriers.Count == 0) { UnwireReactionTick(); UnwireKillHub(); }
        }

        bool AnyReactionEnabled()
        {
            return (onDamaged != null && onDamaged.enabled)
                || (onDeath != null && onDeath.enabled)
                || (onKill != null && onKill.enabled)
                || (onHpBelow != null && onHpBelow.enabled);
        }

        bool IsKillEnabled() { return onKill != null && onKill.enabled; }

        // ================================================ 1. НОСИТЕЛЯ УДАРИЛИ ==

        /// <summary>
        /// Снижение и уход от удара. Числа по уровням разворачиваем в момент открытия: уровень
        /// у носителя фиксирован, а правило читается на каждом входящем ударе — считать массив там
        /// было бы лишней работой на горячем пути.
        /// </summary>
        void WireIncomingRule(Unit unit, ReactionState st, int level)
        {
            if (onDamaged == null || !onDamaged.enabled) return;

            float evade = LevelValueOrZero(onDamaged.evadeChance, level);
            float mult = LevelValue(onDamaged.incomingMultiplier, level, 1f);
            float flat = LevelValueOrZero(onDamaged.flatBlock, level);

            if (mult <= 0f) mult = 1f;                       // пусто или ноль в поле множителя — «не менять»
            if (evade <= 0f && flat <= 0f && Mathf.Approximately(mult, 1f)) return;

            InterflowCombat.IncomingRule rule = new InterflowCombat.IncomingRule
            {
                multiplier = mult,
                onlyType = onDamaged.onlyDamageType,
                onlyDirectAttack = onDamaged.onlyDirectAttack,
                evadeChance = evade,
                flatBlock = flat,
                minDamage = onDamaged.minDamage,
                doubleBlockBelowHp = onDamaged.doubleBlockBelowHp,
                onlyFromFront = onDamaged.onlyFromFront,
                frontAngle = onDamaged.frontAngle
            };

            st.incomingRule = InterflowCombat.IncomingRuleAdd(unit, rule);
        }

        /// <summary>Ответный удар по кругу. Живёт на уведомлении «урон получен» — там известен бьющий.</summary>
        void WireCounter(Unit unit, ReactionState st, int level)
        {
            if (onDamaged == null || !onDamaged.enabled || !onDamaged.counterEnabled) return;

            int lvl = level;
            InterflowCombat.DamagedHandler h = (victim, attacker, damageType, damageDealt, directAttack) =>
                Counterstrike(victim, directAttack, lvl);

            st.damagedHandler = h;
            InterflowCombat.DamagedListenerAdd(unit, h);
        }

        void Counterstrike(Unit victim, bool directAttack, int level)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (victim == null || victim.dead || victim.stunned) return;
            if (onDamaged == null || !onDamaged.enabled || !onDamaged.counterEnabled) return;
            if (onDamaged.onlyDirectAttack && !directAttack) return;
            if (!reactionCarriers.TryGetValue(victim, out ReactionState st)) return;

            // Откат проверяем по величине И ставим ДО удара: два носителя ответа рядом иначе
            // отвечали бы друг другу рекурсивно. Ноль тоже защищает — запись живёт до тика.
            if (st.counterCooldownLeft > 0f) return;

            float radiusValue = LevelValueOrZero(onDamaged.counterRadius, level);
            if (radiusValue <= 0f) return;

            float damage = LevelValueOrZero(onDamaged.counterFlatDamage, level)
                         + victim.attackDamage * LevelValueOrZero(onDamaged.counterPercentOfAttack, level);

            bool hasEffectors = onDamaged.counterEffectors != null && onDamaged.counterEffectors.Length > 0;
            if (damage <= 0f && !hasEffectors) return;

            DamageType dt = onDamaged.counterDamageType != null ? onDamaged.counterDamageType : victim.damageType;
            if (dt == null && damage > 0f) return;

            st.counterCooldownLeft = Mathf.Max(onDamaged.counterCooldown, 0.01f);

            Vector2 center = new Vector2(victim.transform.position.x, victim.transform.position.z);
            Unit[] targets = Utils.GetUnitsInRadius(center, radiusValue, victim.owner, onDamaged.counterSelector, -1, victim);
            if (targets == null) return;

            int hit = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null || targets[i].dead) continue;

                // Не прямая атака: чужой ответный удар на наш ответ не срабатывает.
                if (damage > 0f) targets[i].GetDamage(damage, dt, victim.owner, victim, false, out float _);
                if (hasEffectors) Effector.EffectorAdd(victim, targets[i], onDamaged.counterEffectors);

                hit++;
                if (onDamaged.counterMaxTargets > 0 && hit >= onDamaged.counterMaxTargets) break;
            }

            if (hit > 0)
            {
                InterflowDebug.Verbose("ОТВЕТНЫЙ УДАР: " + InterflowDebug.Name(victim) + " задел " + hit +
                                       " целей на " + damage.ToString("0.#") + " урона");
                RequestForceSync();
            }
        }

        // ===================================================== 2. НОСИТЕЛЬ ПОГИБ ==

        void WireDeath(Unit unit, ReactionState st)
        {
            if (onDeath == null || !onDeath.enabled) return;

            int lvl = st.level;
            Action<Unit, int, Unit, bool> h = (dies, killerPlayer, killerUnit, rewards) => DeathBurst(dies, lvl);

            st.dieHandler = h;
            unit.OnDie += h;
        }

        /// <summary>
        /// Всё в месте гибели. Позицию и владельца фиксируем сразу: носитель уже мёртв, а его объект
        /// сносится отложенно — держаться за него дальше нельзя.
        /// </summary>
        void DeathBurst(Unit unit, int level)
        {
            if (NetworkConnectionHandler.isClient) return;   // OnDie летит и клиенту (DieClientRpc)
            if (unit == null || onDeath == null || !onDeath.enabled) return;

            Vector3 deathPos = unit.transform.position;
            Vector2 center = new Vector2(deathPos.x, deathPos.z);
            int owner = unit.owner;
            float radiusValue = LevelValueOrZero(onDeath.radius, level);

            float enemyDamage = LevelValueOrZero(onDeath.enemyDamage, level);
            float healFlat = LevelValueOrZero(onDeath.allyHealFlat, level);
            float healPercent = LevelValueOrZero(onDeath.allyHealPercentOfMaxHp, level);

            // 1) Урон врагам. Носитель мёртв, поэтому бьём от его владельца, а не от него самого:
            //    так урон засчитывается команде и не тянет за собой цепочку реакций мертвеца.
            if (enemyDamage > 0f && onDeath.enemyDamageType == null)
                Debug.LogWarning("[Реакция «погиб»] Урон врагам задан, но не задан тип урона — урон не нанесён.");

            if (enemyDamage > 0f && onDeath.enemyDamageType != null && radiusValue > 0f)
            {
                Unit[] enemies = Utils.GetUnitsInRadius(center, radiusValue, owner, onDeath.enemySelector, -1, unit);
                if (enemies != null)
                    for (int i = 0; i < enemies.Length; i++)
                        if (enemies[i] != null && !enemies[i].dead)
                            enemies[i].GetDamage(enemyDamage, onDeath.enemyDamageType, owner, null, false, out float _);
            }

            // 2) Лечение союзников: числом и долей от максимума КАЖДОЙ цели.
            if ((healFlat > 0f || healPercent > 0f) && radiusValue > 0f)
            {
                Unit[] allies = Utils.GetUnitsInRadius(center, radiusValue, owner, onDeath.allySelector, -1, unit);
                if (allies != null)
                {
                    if (onDeath.healOnlyNearest)
                    {
                        Unit nearest = NearestAlive(allies, deathPos);
                        if (nearest != null) HealUnit(nearest, healFlat, healPercent);
                    }
                    else
                    {
                        for (int i = 0; i < allies.Length; i++)
                            if (allies[i] != null && !allies[i].dead) HealUnit(allies[i], healFlat, healPercent);
                    }
                }
            }

            // 3) Горящее пятно на земле.
            if (onDeath.zonePrefab != null)
            {
                GameObject zoneGo = UnityEngine.Object.Instantiate(onDeath.zonePrefab, deathPos, Quaternion.identity);
                GroundDamageZone zone = zoneGo.GetComponent<GroundDamageZone>();
                if (zone != null) zone.SetOwner(owner);
                else Debug.LogWarning("[Реакция «погиб»] В заготовке пятна нет компонента GroundDamageZone — пятно не работает.");
            }

            // 4) Призыв от места гибели. Лимит на носителя не нужен — носителя уже нет.
            if (onDeath.summonPrefab != null && onDeath.summonCount > 0 && MatchManager.instance != null)
                MatchManager.instance.SummonFromUnit(unit, onDeath.summonPrefab, onDeath.summonCount, onDeath.summonLifetime,
                    onDeath.summonObeyCommands, onDeath.summonCommand, 0, false, onDeath.summonSpawnSpread);

            InterflowDebug.Event("РЕАКЦИЯ «ПОГИБ» у " + InterflowDebug.Name(unit) + ": сработала");
        }

        static Unit NearestAlive(Unit[] candidates, Vector3 from)
        {
            Unit best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < candidates.Length; i++)
            {
                Unit u = candidates[i];
                if (u == null || u.dead) continue;

                float sqr = (u.transform.position - from).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = u;
            }

            return best;
        }

        /// <summary>Разовое лечение числом и долей от максимума цели. ChangeHP сам зажимает по максимуму и синкает.</summary>
        static void HealUnit(Unit target, float flat, float percentOfMax)
        {
            float amount = flat + (percentOfMax > 0f ? target.maxHealth * percentOfMax : 0f);
            if (amount > 0f) target.ChangeHP(amount);
        }

        // ============================================ 3. НОСИТЕЛЬ КОГО-ТО УБИЛ ==

        /// <summary>
        /// Добивание видно только там, где одновременно известны жертва и убийца — в хабе смертей.
        /// Подписка на хаб одна на весь ассет, а не на носителя: событие всё равно общее.
        /// </summary>
        void WireKill(Unit unit, ReactionState st)
        {
            if (onKill == null || !onKill.enabled) return;
            if (killHubWired || MatchManager.instance == null) return;

            MatchManager.instance.OnUnitDeathServer += HandleKillForReactions;
            killHubWired = true;
        }

        void UnwireKillHub()
        {
            if (!killHubWired) return;

            if (MatchManager.instance != null) MatchManager.instance.OnUnitDeathServer -= HandleKillForReactions;
            killHubWired = false;
        }

        void HandleKillForReactions(Unit victim, int killerPlayer, Unit killerUnit, bool rewards)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (onKill == null || !onKill.enabled) return;
            if (killerUnit == null || killerUnit.dead) return;
            if (!reactionCarriers.TryGetValue(killerUnit, out ReactionState st)) return;
            if (victim == killerUnit) return;                       // сам себя не «добивал»

            int level = st.level;

            // Лечение добившему.
            float flat = LevelValueOrZero(onKill.killerHealFlat, level);
            float percent = LevelValueOrZero(onKill.killerHealPercentOfMaxHp, level);
            if (flat > 0f || percent > 0f) HealUnit(killerUnit, flat, percent);

            // Клич союзникам вокруг.
            bool hasEffectors = onKill.cryEffectors != null && onKill.cryEffectors.Length > 0;
            float radiusValue = LevelValueOrZero(onKill.cryRadius, level);

            if (hasEffectors && radiusValue > 0f)
            {
                Vector3 pos = killerUnit.transform.position;
                Unit[] allies = Utils.GetUnitsInRadius(new Vector2(pos.x, pos.z), radiusValue, killerUnit.owner,
                                                       onKill.crySelector, -1, onKill.includeSelf ? null : killerUnit);
                if (allies != null)
                    for (int i = 0; i < allies.Length; i++)
                        if (allies[i] != null && !allies[i].dead)
                            Effector.EffectorAdd(killerUnit, allies[i], onKill.cryEffectors);
            }

            RequestForceSync();
        }

        // ============================================== 4. ЗДОРОВЬЕ НИЖЕ ДОЛИ ==

        void WireHpBelow(Unit unit, ReactionState st)
        {
            if (onHpBelow == null || !onHpBelow.enabled) return;

            Unit u = unit;
            Action h = () => HpBelowCheck(u);

            st.hpHandler = h;
            unit.OnHPChange += h;
        }

        /// <summary>
        /// Срабатывает при пересечении порога ВНИЗ и запирается до тех пор, пока здоровье не поднимется
        /// обратно выше порога: иначе каждый следующий удар по раненому носителю запускал бы реакцию заново.
        /// </summary>
        void HpBelowCheck(Unit unit)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (unit == null || unit.dead || onHpBelow == null || !onHpBelow.enabled) return;
            if (!reactionCarriers.TryGetValue(unit, out ReactionState st)) return;
            if (unit.maxHealth <= 0f) return;

            bool below = unit.health / unit.maxHealth < onHpBelow.threshold;

            if (!below) { st.hpBelowLatched = false; return; }   // поднялся выше порога — снова взведён
            if (st.hpBelowLatched) return;
            if (st.hpCooldownLeft > 0f) return;

            st.hpBelowLatched = true;
            st.hpCooldownLeft = onHpBelow.cooldown;

            int level = st.level;

            if (onHpBelow.selfEffectors != null && onHpBelow.selfEffectors.Length > 0)
                Effector.EffectorAdd(unit, unit, onHpBelow.selfEffectors);

            bool hasAllyEffectors = onHpBelow.allyEffectors != null && onHpBelow.allyEffectors.Length > 0;
            float radiusValue = LevelValueOrZero(onHpBelow.allyRadius, level);

            if (hasAllyEffectors && radiusValue > 0f)
            {
                Vector3 pos = unit.transform.position;
                Unit[] allies = Utils.GetUnitsInRadius(new Vector2(pos.x, pos.z), radiusValue, unit.owner,
                                                       onHpBelow.allySelector, -1, unit);
                if (allies != null)
                    for (int i = 0; i < allies.Length; i++)
                        if (allies[i] != null && !allies[i].dead)
                            Effector.EffectorAdd(unit, allies[i], onHpBelow.allyEffectors);
            }

            InterflowDebug.Event("РЕАКЦИЯ «ЗДОРОВЬЕ НИЖЕ ПОРОГА» у " + InterflowDebug.Name(unit) + ": сработала");
            RequestForceSync();

            if (NeedsReactionTick()) WireReactionTick();
        }

        // ==================================================== ОТКАТЫ И УБОРКА ==

        bool NeedsReactionTick()
        {
            foreach (ReactionState st in reactionCarriers.Values)
                if (st.counterCooldownLeft > 0f || st.hpCooldownLeft > 0f) return true;

            // Ответный удар и порог здоровья заводят откаты в любой момент — тик нужен, пока они вообще включены.
            return (onDamaged != null && onDamaged.enabled && onDamaged.counterEnabled)
                || (onHpBelow != null && onHpBelow.enabled && onHpBelow.cooldown > 0f);
        }

        void WireReactionTick()
        {
            if (reactionTickWired || GameManager.instance == null) return;

            GameManager.instance.Tick += ReactionTick;
            reactionTickWired = true;
        }

        void UnwireReactionTick()
        {
            if (!reactionTickWired) { return; }

            if (GameManager.instance != null) GameManager.instance.Tick -= ReactionTick;
            reactionTickWired = false;
        }

        /// <summary>Штатный тик 0.1 с: крутим откаты и чистим мёртвых носителей (Unit.Die не зовёт Lock).</summary>
        void ReactionTick()
        {
            if (GameManager.instance == null || reactionCarriers.Count == 0) return;

            float dt = GameManager.instance.currentDeltaTime;

            reactionTickBuffer.Clear();
            reactionTickBuffer.AddRange(reactionCarriers.Keys);

            for (int i = 0; i < reactionTickBuffer.Count; i++)
            {
                Unit u = reactionTickBuffer[i];

                if (ReferenceEquals(u, null)) continue;
                if (u == null || u.dead) { UnwireReactionsDead(u); continue; }
                if (!reactionCarriers.TryGetValue(u, out ReactionState st)) continue;

                if (st.counterCooldownLeft > 0f) st.counterCooldownLeft = Mathf.Max(0f, st.counterCooldownLeft - dt);
                if (st.hpCooldownLeft > 0f) st.hpCooldownLeft = Mathf.Max(0f, st.hpCooldownLeft - dt);
            }

            if (reactionCarriers.Count == 0) { UnwireReactionTick(); UnwireKillHub(); }
        }

        /// <summary>
        /// Мёртвый носитель: подписки снимаем, но на сам объект больше не ссылаемся — он мог быть уже снесён.
        /// Правила входящего урона и слушатели чистятся в InterflowCombat по смерти носителя штатно.
        /// </summary>
        void UnwireReactionsDead(Unit unit)
        {
            // ReferenceEquals, а не ==: у снесённого объекта Unity оператор == даёт true, но ключ в словаре
            // лежит по ссылке и убрать его надо именно ею. Настоящий null ключом быть не может — не кладём.
            if (ReferenceEquals(unit, null)) return;

            if (reactionCarriers.TryGetValue(unit, out ReactionState st))
            {
                if (st.dieHandler != null) unit.OnDie -= st.dieHandler;
                if (st.hpHandler != null) unit.OnHPChange -= st.hpHandler;
            }

            reactionCarriers.Remove(unit);
        }
    }
}
