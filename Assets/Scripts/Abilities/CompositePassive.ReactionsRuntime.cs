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
    //
    // [Interflow 2026-09-09 passive-facts] Каждое срабатывание, кроме ухода от удара, поднимает
    // разовый факт боя — надпись над юнитом и строку в ленте (партиал CompositePassive.Facts.cs).
    // Уход от удара своего факта НЕ получает намеренно: приёмник делает один общий бросок и не
    // различает уход жертвы и промах бьющего (решение Artsiom Р4 от 03.09.2026), поэтому у обоих
    // случаев одна надпись «Мимо» — она уже шлётся штатной причиной HitMissed.
    public partial class CompositePassive
    {
        /// <summary>Что выдано носителю по оси реакций. Снимаем ровно то, что выдали.</summary>
        class ReactionState
        {
            public int level;

            public InterflowCombat.IncomingRule incomingRule;      // снижение и уход от удара
            public InterflowCombat.DamagedHandler damagedHandler;  // ответный удар
            public InterflowCombat.HitMissedHandler hitMissedHandler; // ответ при «удар не достиг цели» [Interflow fix 2026-09-04 damage-full-packet]
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

            if (st.incomingRule == null && st.damagedHandler == null && st.hitMissedHandler == null &&
                st.dieHandler == null && st.hpHandler == null && !IsKillEnabled())
            {
                if (InterflowDebug.FullOn)
                    LogReactionsNotWired(unit, "включённые реакции не дали ни одной подписки: числа нейтральны или блоки пусты");
                return;
            }

            reactionCarriers[unit] = st;

            if (InterflowDebug.FullOn) LogReactionsWired(unit, st, level);

            if (NeedsReactionTick()) WireReactionTick();
        }

        /// <summary>Умение закрылось — снимаем ровно выданное.</summary>
        void UnwireReactions(Unit unit)
        {
            if (unit == null || !reactionCarriers.TryGetValue(unit, out ReactionState st)) return;

            if (st.incomingRule != null) InterflowCombat.IncomingRuleRemove(unit, st.incomingRule);
            if (st.damagedHandler != null) InterflowCombat.DamagedListenerRemove(unit, st.damagedHandler);
            if (st.hitMissedHandler != null) InterflowCombat.HitMissedListenerRemove(unit, st.hitMissedHandler);
            if (st.dieHandler != null) unit.OnDie -= st.dieHandler;
            if (st.hpHandler != null) unit.OnHPChange -= st.hpHandler;

            reactionCarriers.Remove(unit);

            if (InterflowDebug.FullOn) LogReactionsUnwired(unit, st, "закрытие умения");

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

            float evade = LevelValue(onDamaged.evadeChance, level);
            float mult = LevelValue(onDamaged.incomingMultiplier, level, 1f);
            float flat = LevelValue(onDamaged.flatBlock, level);

            if (mult <= 0f) mult = 1f;                       // пусто или ноль в поле множителя — «не менять»

            if (evade <= 0f && flat <= 0f && Mathf.Approximately(mult, 1f))
            {
                if (InterflowDebug.FullOn)
                    LogReactionSkipped(1, unit, "правило входящего урона",
                                       "числа нейтральны: множитель 1, шанс ухода 0, вычет числом 0");
                return;
            }

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
                frontAngle = onDamaged.frontAngle,
                source = this            // диагностика: чьё это правило (решение Artsiom 07.09.2026)
            };

            // Связка «удар не достиг цели → ударил в ответ»: подписываем на событие приёмника «удар не достиг цели»
            // (решение Artsiom Р4, 03.09.2026 — уведомления «по правилу» больше нет). [Interflow fix 2026-09-04 damage-full-packet]
            // Обычный ответ (WireCounter) в этом режиме не подписывается — иначе был бы двойной удар.
            if (onDamaged.counterEnabled && onDamaged.counterOnlyOnEvade)
            {
                int lvl = level;
                InterflowCombat.HitMissedHandler h = (v, a) => CounterstrikeOnEvade(v, a, lvl);

                st.hitMissedHandler = h;
                InterflowCombat.HitMissedListenerAdd(unit, h);

                if (InterflowDebug.FullOn) LogCounterWired(unit, true);
            }

            st.incomingRule = InterflowCombat.IncomingRuleAdd(unit, rule);

            if (InterflowDebug.FullOn) LogIncomingRuleWired(unit, rule);
        }

        /// <summary>Ответный удар по кругу. Живёт на уведомлении «урон получен» — там известен бьющий.</summary>
        void WireCounter(Unit unit, ReactionState st, int level)
        {
            if (onDamaged == null || !onDamaged.enabled || !onDamaged.counterEnabled) return;

            // Режим «ответ только при уходе»: живёт на уведомлении ухода (см. WireIncomingRule),
            // а не на «урон получен» — увернувшийся урона не получает, и сюда бы он не попал.
            if (onDamaged.counterOnlyOnEvade) return;

            int lvl = level;
            InterflowCombat.DamagedHandler h = (victim, attacker, damageType, damageDealt, directAttack) =>
                Counterstrike(victim, directAttack, lvl);

            st.damagedHandler = h;
            InterflowCombat.DamagedListenerAdd(unit, h);

            if (InterflowDebug.FullOn) LogCounterWired(unit, false);
        }

        void Counterstrike(Unit victim, bool directAttack, int level)
        {
            if (NetworkConnectionHandler.isClient) return;   // не игровой отказ — молчим

            if (victim == null || victim.dead || victim.stunned)
            {
                if (InterflowDebug.FullOn && victim != null && CounterLogged())
                    LogReactionSkipped(1, victim, "ответный удар", victim.dead ? "носитель мёртв" : "носитель оглушён");
                return;
            }

            if (onDamaged == null || !onDamaged.enabled || !onDamaged.counterEnabled) return;   // выключено — молчим

            if (onDamaged.onlyDirectAttack && !directAttack)
            {
                if (InterflowDebug.FullOn) LogReactionSkipped(1, victim, "ответный удар", "удар не прямая атака");
                return;
            }

            if (!reactionCarriers.TryGetValue(victim, out ReactionState st))
            {
                if (InterflowDebug.FullOn) LogReactionSkipped(1, victim, "ответный удар", "носитель не числится в списке реакций");
                return;
            }

            // Откат проверяем по величине И ставим ДО удара: два носителя ответа рядом иначе
            // отвечали бы друг другу рекурсивно. Ноль тоже защищает — запись живёт до тика.
            if (st.counterCooldownLeft > 0f)
            {
                if (InterflowDebug.FullOn)
                    LogReactionSkipped(1, victim, "ответный удар", "идёт откат, осталось " + st.counterCooldownLeft.ToString("0.#") + " сек");
                return;
            }

            float radiusValue = LevelValue(onDamaged.counterRadius, level);
            if (radiusValue <= 0f)
            {
                if (InterflowDebug.FullOn) LogReactionSkipped(1, victim, "ответный удар", "радиус ответа на уровне " + level + " равен нулю");
                return;
            }

            float damage = LevelValue(onDamaged.counterFlatDamage, level)
                         + victim.attackDamage * LevelValue(onDamaged.counterPercentOfAttack, level);

            bool hasEffectors = onDamaged.counterEffectors != null && onDamaged.counterEffectors.Length > 0;
            if (damage <= 0f && !hasEffectors)
            {
                if (InterflowDebug.FullOn) LogReactionSkipped(1, victim, "ответный удар", "нечем отвечать: урон ноль и состояний нет");
                return;
            }

            DamageType dt = onDamaged.counterDamageType != null ? onDamaged.counterDamageType : victim.damageType;
            if (dt == null && damage > 0f)
            {
                if (InterflowDebug.FullOn) LogReactionSkipped(1, victim, "ответный удар", "тип урона ответа неизвестен");
                return;
            }

            st.counterCooldownLeft = Mathf.Max(onDamaged.counterCooldown, 0.01f);

            Vector2 center = new Vector2(victim.transform.position.x, victim.transform.position.z);
            Unit[] targets = Utils.GetUnitsInRadius(center, radiusValue, victim.owner, onDamaged.counterSelector, -1, victim);
            if (targets == null)
            {
                if (InterflowDebug.FullOn) LogReactionSkipped(1, victim, "ответный удар", "в радиусе нет целей");
                return;
            }

            int hit = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null || targets[i].dead) continue;

                // Не прямая атака: чужой ответный удар на наш ответ не срабатывает.
                if (damage > 0f)
                {
                    DamagePacket packet = DamagePacket.Create(damage, dt, victim.owner, victim, false, this);   // [Interflow fix 2026-09-04 damage-full-packet] пакет одной записи
                    targets[i].GetDamage(in packet, out float _);
                }
                if (hasEffectors) Effector.EffectorAdd(victim, targets[i], onDamaged.counterEffectors);

                hit++;
                if (onDamaged.counterMaxTargets > 0 && hit >= onDamaged.counterMaxTargets) break;
            }

            if (hit > 0)
            {
                InterflowDebug.Verbose("ОТВЕТНЫЙ УДАР: " + InterflowDebug.Name(victim) + " задел " + hit +
                                       " целей на " + damage.ToString("0.#") + " урона");

                // [Interflow 2026-09-09 passive-facts] Надпись над ОТВЕТИВШИМ: число — урон одной цели.
                Fact(victim, BattleFactReason.PassiveCounter, damage);

                if (InterflowDebug.FullOn) LogCounterFired(victim, hit, damage, dt, onDamaged.counterEffectors);
                RequestForceSync();
            }
            else if (InterflowDebug.FullOn)
            {
                LogReactionSkipped(1, victim, "ответный удар", "в радиусе нет живых целей");
            }
        }

        /// <summary>
        /// Ответный удар в режиме «удар не достиг цели → ударил в ответ». Живёт на событии приёмника
        /// «удар не достиг цели» (InterflowCombat.HitMissedListenerAdd): срабатывает и когда носитель увернулся,
        /// и когда бьющий промахнулся из-за ослепления — приёмник делает один общий бросок и не различает, чей
        /// вклад сработал (решение Artsiom Р4, 03.09.2026). Бьёт по самому бьющему, если тот известен, жив
        /// и в радиусе ответа; по кругу не бьёт. Откат общий с обычным ответом (counterCooldownLeft) — тик
        /// откатов уже покрывает этот режим (NeedsReactionTick).
        /// </summary>
        void CounterstrikeOnEvade(Unit victim, Unit attacker, int level)
        {
            if (NetworkConnectionHandler.isClient) return;   // не игровой отказ — молчим

            if (victim == null || victim.dead || victim.stunned)
            {
                if (InterflowDebug.FullOn && victim != null && CounterLogged())
                    LogReactionSkipped(1, victim, "встречный удар при уходе", victim.dead ? "носитель мёртв" : "носитель оглушён");
                return;
            }

            if (onDamaged == null || !onDamaged.enabled || !onDamaged.counterEnabled || !onDamaged.counterOnlyOnEvade) return;   // выключено — молчим

            if (attacker == null || attacker.dead)
            {
                if (InterflowDebug.FullOn) LogReactionSkipped(1, victim, "встречный удар при уходе", "бьющий неизвестен или мёртв");
                return;
            }

            if (!reactionCarriers.TryGetValue(victim, out ReactionState st))
            {
                if (InterflowDebug.FullOn) LogReactionSkipped(1, victim, "встречный удар при уходе", "носитель не числится в списке реакций");
                return;
            }

            if (st.counterCooldownLeft > 0f)
            {
                if (InterflowDebug.FullOn)
                    LogReactionSkipped(1, victim, "встречный удар при уходе", "идёт откат, осталось " + st.counterCooldownLeft.ToString("0.#") + " сек");
                return;
            }

            float radiusValue = LevelValue(onDamaged.counterRadius, level);
            if (radiusValue <= 0f)
            {
                if (InterflowDebug.FullOn) LogReactionSkipped(1, victim, "встречный удар при уходе", "радиус ответа на уровне " + level + " равен нулю");
                return;
            }

            // Бьющий дальше радиуса ответа (например дальний стрелок) — встречного удара нет.
            Vector3 delta = attacker.transform.position - victim.transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > radiusValue * radiusValue)
            {
                if (InterflowDebug.FullOn)
                    LogReactionSkipped(1, victim, "встречный удар при уходе", "бьющий дальше радиуса ответа " + radiusValue.ToString("0.#"));
                return;
            }

            float damage = LevelValue(onDamaged.counterFlatDamage, level)
                         + victim.attackDamage * LevelValue(onDamaged.counterPercentOfAttack, level);

            bool hasEffectors = onDamaged.counterEffectors != null && onDamaged.counterEffectors.Length > 0;
            if (damage <= 0f && !hasEffectors)
            {
                if (InterflowDebug.FullOn) LogReactionSkipped(1, victim, "встречный удар при уходе", "нечем отвечать: урон ноль и состояний нет");
                return;
            }

            DamageType dt = onDamaged.counterDamageType != null ? onDamaged.counterDamageType : victim.damageType;
            if (dt == null && damage > 0f)
            {
                if (InterflowDebug.FullOn) LogReactionSkipped(1, victim, "встречный удар при уходе", "тип урона ответа неизвестен");
                return;
            }

            st.counterCooldownLeft = Mathf.Max(onDamaged.counterCooldown, 0.01f);

            // Не прямая атака: чужой ответный удар на наш ответ не срабатывает.
            if (damage > 0f)
            {
                DamagePacket packet = DamagePacket.Create(damage, dt, victim.owner, victim, false, this);   // [Interflow fix 2026-09-04 damage-full-packet] пакет одной записи
                attacker.GetDamage(in packet, out float _);
            }
            if (hasEffectors) Effector.EffectorAdd(victim, attacker, onDamaged.counterEffectors);

            InterflowDebug.Verbose("ВСТРЕЧНЫЙ УДАР ПРИ УХОДЕ: " + InterflowDebug.Name(victim) + " ответил " +
                                   InterflowDebug.Name(attacker) + " на " + damage.ToString("0.#") + " урона");

            // [Interflow 2026-09-09 passive-facts] Надпись над ОТВЕТИВШИМ. Отдельная причина, а не общая
            // с ответом по кругу: при разборе надо видеть, какой из двух режимов ответа сработал.
            Fact(victim, BattleFactReason.PassiveCounterOnEvade, damage);

            if (InterflowDebug.FullOn) LogCounterOnEvadeFired(victim, attacker, damage, dt, onDamaged.counterEffectors);
            RequestForceSync();
        }

        /// <summary>Включён ли ответный удар в ассете — чтобы выключенный блок молчал в отказных строках.</summary>
        bool CounterLogged() { return onDamaged != null && onDamaged.enabled && onDamaged.counterEnabled; }

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
            float radiusValue = LevelValue(onDeath.radius, level);

            float enemyDamage = LevelValue(onDeath.enemyDamage, level);
            float healFlat = LevelValue(onDeath.allyHealFlat, level);
            float healPercent = LevelValue(onDeath.allyHealPercentOfMaxHp, level);

            // Локальные счётчики только ради итоговой строки лога: расчёт ими не пользуется.
            int enemiesHit = 0;
            int healedCount = 0;

            // 1) Урон врагам. Носитель мёртв, поэтому бьём от его владельца, а не от него самого:
            //    так урон засчитывается команде и не тянет за собой цепочку реакций мертвеца.
            if (enemyDamage > 0f && onDeath.enemyDamageType == null)
                Debug.LogWarning("[Реакция «погиб»] Урон врагам задан, но не задан тип урона — урон не нанесён.");

            if (enemyDamage > 0f && onDeath.enemyDamageType != null && radiusValue > 0f)
            {
                Unit[] enemies = Utils.GetUnitsInRadius(center, radiusValue, owner, onDeath.enemySelector, -1, unit);
                if (enemies != null)
                {
                    for (int i = 0; i < enemies.Length; i++)
                        if (enemies[i] != null && !enemies[i].dead)
                        {
                            DamagePacket packet = DamagePacket.Create(enemyDamage, onDeath.enemyDamageType, owner, null, false, this);   // [Interflow fix 2026-09-04 damage-full-packet] пакет одной записи
                            enemies[i].GetDamage(in packet, out float _);
                            enemiesHit++;
                        }

                    if (enemiesHit > 0)
                        InterflowDebug.Verbose("ВЗРЫВ ПРИ ГИБЕЛИ: " + InterflowDebug.Name(unit) + " задел " +
                                               enemiesHit + " целей на " + enemyDamage.ToString("0.#") + " урона");
                }
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
                        if (nearest != null)
                        {
                            HealUnit(nearest, healFlat, healPercent);
                            healedCount = 1;

                            InterflowDebug.Verbose("ЛЕЧЕНИЕ ПРИ ГИБЕЛИ: " + InterflowDebug.Name(nearest) + " получил +" +
                                                   (healFlat + healPercent * nearest.maxHealth).ToString("0.#") + " здоровья");
                        }
                    }
                    else
                    {
                        for (int i = 0; i < allies.Length; i++)
                            if (allies[i] != null && !allies[i].dead) { HealUnit(allies[i], healFlat, healPercent); healedCount++; }

                        if (healedCount > 0)
                            InterflowDebug.Verbose("ЛЕЧЕНИЕ ПРИ ГИБЕЛИ: вылечено союзников: " + healedCount);
                    }
                }
            }

            // 3) Горящее пятно на земле.
            if (onDeath.zonePrefab != null)
            {
                GameObject zoneGo = UnityEngine.Object.Instantiate(onDeath.zonePrefab, deathPos, Quaternion.identity);
                GroundDamageZone zone = zoneGo.GetComponent<GroundDamageZone>();
                if (zone != null) { zone.SetOwner(owner); zone.SetSource(this); }
                else Debug.LogWarning("[Реакция «погиб»] В заготовке пятна нет компонента GroundDamageZone — пятно не работает.");
            }

            // 4) Призыв от места гибели. Лимит на носителя не нужен — носителя уже нет.
            if (onDeath.summonPrefab != null && onDeath.summonCount > 0 && MatchManager.Instance != null)
                MatchManager.Instance.SummonFromUnit(unit, onDeath.summonPrefab, onDeath.summonCount, onDeath.summonLifetime,
                    onDeath.summonObeyCommands, onDeath.summonCommand, 0, false, onDeath.summonSpawnSpread);

            InterflowDebug.Event("РЕАКЦИЯ «ПОГИБ» у " + InterflowDebug.Name(unit) + ": сработала");

            // [Interflow 2026-09-09 passive-facts] Надпись В ТОЧКЕ ГИБЕЛИ, а не над носителем:
            // носитель уже мёртв, его netID снят с учёта, и сообщение «про юнита» до клиента не дошло бы.
            // Число — сколько врагов задето взрывом.
            FactAt(deathPos, BattleFactReason.PassiveDeathBurst, enemiesHit);

            if (InterflowDebug.FullOn)
                LogDeathFired(unit, radiusValue, enemiesHit, enemyDamage, onDeath.enemyDamageType,
                              healedCount, healFlat, healPercent, onDeath.zonePrefab != null,
                              onDeath.summonPrefab != null && MatchManager.Instance != null ? onDeath.summonCount : 0);
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
            if (killHubWired || MatchManager.Instance == null) return;

            MatchManager.Instance.OnUnitDeathServer += HandleKillForReactions;
            killHubWired = true;
        }

        void UnwireKillHub()
        {
            if (!killHubWired) return;

            if (MatchManager.Instance != null) MatchManager.Instance.OnUnitDeathServer -= HandleKillForReactions;
            killHubWired = false;
        }

        void HandleKillForReactions(Unit victim, int killerPlayer, Unit killerUnit, bool rewards)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (onKill == null || !onKill.enabled) return;
            if (killerUnit == null || killerUnit.dead) return;
            // Событие хаба приходит на КАЖДУЮ смерть в матче: если добивший не наш носитель,
            // это не отказ нашей реакции, а чужое событие — молчим.
            if (!reactionCarriers.TryGetValue(killerUnit, out ReactionState st)) return;

            if (victim == killerUnit)
            {
                if (InterflowDebug.FullOn) LogReactionSkipped(3, killerUnit, "добивание", "носитель числится собственным убийцей");
                return;                                             // сам себя не «добивал»
            }

            int level = st.level;

            // Лечение добившему.
            float flat = LevelValue(onKill.killerHealFlat, level);
            float percent = LevelValue(onKill.killerHealPercentOfMaxHp, level);
            if (flat > 0f || percent > 0f) HealUnit(killerUnit, flat, percent);

            // Клич союзникам вокруг.
            bool hasEffectors = onKill.cryEffectors != null && onKill.cryEffectors.Length > 0;
            float radiusValue = LevelValue(onKill.cryRadius, level);

            int cried = 0;   // локальный счётчик только ради строки лога

            if (hasEffectors && radiusValue > 0f)
            {
                Vector3 pos = killerUnit.transform.position;
                Unit[] allies = Utils.GetUnitsInRadius(new Vector2(pos.x, pos.z), radiusValue, killerUnit.owner,
                                                       onKill.crySelector, -1, onKill.includeSelf ? null : killerUnit);
                if (allies != null)
                    for (int i = 0; i < allies.Length; i++)
                        if (allies[i] != null && !allies[i].dead)
                        {
                            Effector.EffectorAdd(killerUnit, allies[i], onKill.cryEffectors);
                            cried++;
                        }
            }

            // [Interflow 2026-09-09 passive-facts] Надпись над ДОБИВШИМ: число — сколько союзников задел клич.
            Fact(killerUnit, BattleFactReason.PassiveOnKill, cried);

            if (InterflowDebug.FullOn)
                LogKillFired(killerUnit, victim, flat + (percent > 0f ? killerUnit.maxHealth * percent : 0f),
                             cried, onKill.cryEffectors);

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

            if (unit.maxHealth <= 0f)
            {
                if (InterflowDebug.FullOn) LogReactionSkipped(4, unit, "порог здоровья", "максимальное здоровье равно нулю");
                return;
            }

            float hpFraction = unit.health / unit.maxHealth;   // локальная: решение и строка лога по ОДНОМУ значению
            bool below = hpFraction < onHpBelow.threshold;

            // Здоровье выше порога — это норма, а не отказ: событие изменения здоровья приходит
            // на каждый удар по носителю, и строка здесь забила бы лог не хуже тика (правило промта
            // про горячие пути). Пишем только случаи «ниже порога, но не сработало».
            if (!below) { st.hpBelowLatched = false; return; }   // поднялся выше порога — снова взведён

            if (st.hpBelowLatched)
            {
                if (InterflowDebug.FullOn)
                    LogReactionSkipped(4, unit, "порог здоровья", "уже сработала и заперта до подъёма выше порога");
                return;
            }

            if (st.hpCooldownLeft > 0f)
            {
                if (InterflowDebug.FullOn)
                    LogReactionSkipped(4, unit, "порог здоровья", "идёт откат, осталось " + st.hpCooldownLeft.ToString("0.#") + " сек");
                return;
            }

            st.hpBelowLatched = true;
            st.hpCooldownLeft = onHpBelow.cooldown;

            int level = st.level;

            if (onHpBelow.selfEffectors != null && onHpBelow.selfEffectors.Length > 0)
                Effector.EffectorAdd(unit, unit, onHpBelow.selfEffectors);

            bool hasAllyEffectors = onHpBelow.allyEffectors != null && onHpBelow.allyEffectors.Length > 0;
            float radiusValue = LevelValue(onHpBelow.allyRadius, level);

            int helped = 0;   // локальный счётчик только ради строки лога

            if (hasAllyEffectors && radiusValue > 0f)
            {
                Vector3 pos = unit.transform.position;
                Unit[] allies = Utils.GetUnitsInRadius(new Vector2(pos.x, pos.z), radiusValue, unit.owner,
                                                       onHpBelow.allySelector, -1, unit);
                if (allies != null)
                    for (int i = 0; i < allies.Length; i++)
                        if (allies[i] != null && !allies[i].dead)
                        {
                            Effector.EffectorAdd(unit, allies[i], onHpBelow.allyEffectors);
                            helped++;
                        }
            }

            InterflowDebug.Event("РЕАКЦИЯ «ЗДОРОВЬЕ НИЖЕ ПОРОГА» у " + InterflowDebug.Name(unit) + ": сработала");

            // [Interflow 2026-09-09 passive-facts] Надпись над носителем: число — доля здоровья в процентах
            // на момент срабатывания. Сразу видно, на каком пороге реакция ушла.
            Fact(unit, BattleFactReason.PassiveHpBelow, hpFraction * 100f);

            if (InterflowDebug.FullOn)
                LogHpBelowFired(unit, onHpBelow.threshold, hpFraction, onHpBelow.selfEffectors, helped);

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
            if (reactionTickWired || GameManager.Instance == null) return;

            GameManager.Instance.Tick += ReactionTick;
            reactionTickWired = true;
        }

        void UnwireReactionTick()
        {
            if (!reactionTickWired) { return; }

            if (GameManager.Instance != null) GameManager.Instance.Tick -= ReactionTick;
            reactionTickWired = false;
        }

        /// <summary>Штатный тик 0.1 с: крутим откаты и чистим мёртвых носителей (Unit.Die не зовёт Lock).</summary>
        void ReactionTick()
        {
            if (GameManager.Instance == null || reactionCarriers.Count == 0) return;

            float dt = GameManager.Instance.currentDeltaTime;

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

                // Второй путь снятия. Он и UnwireReactions исключают друг друга: оба убирают ключ
                // из reactionCarriers, поэтому строка «снято» на носителя пишется ровно один раз.
                if (InterflowDebug.FullOn) LogReactionsUnwired(unit, st, "смерть носителя");
            }

            reactionCarriers.Remove(unit);
        }
    }
}
