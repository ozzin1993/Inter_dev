using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StrategyCore
{
    /// <summary>
    /// Централизованный боевой хаб Interflow (правило 5 — одна точка входа вместо россыпи правок ядра).
    ///
    /// Зачем нужен: штатные колбэки ассета не передают часть данных —
    /// у хука жертвы (<see cref="Unit.OnBeforeGetDamageCallbacks"/>) НЕТ атакующего и типа урона,
    /// у хука атакующего (<see cref="Unit.OnDamageDealModifyCallbacks"/>) нет цели.
    /// Поэтому в <c>Unit.GetDamage</c> вставлены ТРИ вызова этого класса (маркер
    /// <c>[Interflow fix 2026-07-24 combat-hub]</c>), а вся логика живёт здесь, в нашем коде.
    ///
    /// Потребители: пробитие брони (Т1А1, Т4Б1), уязвимость «Приговор» (Т6Б2),
    /// снижение входящего урона (Т3Б2), контрудар (Т2А2), ответная заморозка (Т3А1).
    ///
    /// Серверо-авторитетность: расчётные функции детерминированы и работают на обоих пирах
    /// (иначе клиент увидит другой урон), а уведомление о полученном уроне —
    /// только на сервере (там живут реакции, спавнящие эффекты).
    /// </summary>
    public static class InterflowCombat
    {
        // ============================= ПРОБИТИЕ БРОНИ ==

        /// <summary>Доля игнорируемой брони цели по атакующему юниту (0..1). Складывается несколькими источниками.</summary>
        static readonly Dictionary<Unit, float> armorPierceByAttacker = new Dictionary<Unit, float>();

        /// <summary>Зарегистрировать пробитие брони у атакующего (доля 0..1). Вызывает Unlock способности.</summary>
        public static void ArmorPierceAdd(Unit attacker, float fraction)
        {
            if (attacker == null || fraction <= 0f) return;

            armorPierceByAttacker.TryGetValue(attacker, out float current);
            armorPierceByAttacker[attacker] = Mathf.Clamp01(current + fraction);
            WatchDeath(attacker);
        }

        /// <summary>Снять ранее зарегистрированное пробитие брони. Вызывает Lock способности.</summary>
        public static void ArmorPierceRemove(Unit attacker, float fraction)
        {
            if (attacker == null || fraction <= 0f) return;
            if (!armorPierceByAttacker.TryGetValue(attacker, out float current)) return;

            float left = Mathf.Clamp01(current - fraction);
            if (left <= 0f) armorPierceByAttacker.Remove(attacker);
            else armorPierceByAttacker[attacker] = left;
        }

        /// <summary>
        /// Технология изучена у игрока. Обёртка над штатным TechnologyManager с гардами:
        /// ассетный isUnlocked обращается к словарю напрямую и падает на нейтралах (owner −1)
        /// и на технологиях вне Resources.
        /// </summary>
        public static bool IsTechUnlockedSafe(Technology tech, int owner)
        {
            if (tech == null) return true;                 // условия нет
            if (owner < 0) return false;                   // нейтралы технологий не изучают
            if (TechnologyManager.Instance == null) return false;

            try
            {
                return TechnologyManager.Instance.isUnlocked(tech, owner);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[InterflowCombat] Технология '" + tech.name + "' не найдена в дереве игрока " + owner +
                                 " (лежит ли она в Resources/Technology?). Условие считаем невыполненным. " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Пробитие брони бьющего, доля 0..1. Шаг 4 схемы «пакет и приёмник» (§13): пробитие ЕДЕТ В ПАКЕТЕ —
        /// его читает отправитель при сборке пакета (<see cref="DamagePacket.Create"/>), а приёмник считает
        /// броню по числу из пакета и по бьющему ничего не ищет. Бьющего нет (состояние, зона, отложенный урон) → 0.
        /// </summary>
        public static float ArmorPierceOf(Unit attacker)
        {
            if (attacker == null || armorPierceByAttacker.Count == 0) return 0f;
            return armorPierceByAttacker.TryGetValue(attacker, out float pierce) ? Mathf.Clamp01(pierce) : 0f;
        }

        // ============================= ПРОМАХ (ОСЛЕПЛЕНИЕ) ==

        /// <summary>Шанс промаха по атакующему юниту (0..1). Ослеплённый бьёт мимо.</summary>
        static readonly Dictionary<Unit, float> missChanceByAttacker = new Dictionary<Unit, float>();

        /// <summary>
        /// Задать шанс промаха атакующему. С шага 4 (решение Artsiom Р3, 03.09.2026) писатель ОДИН —
        /// <c>Unit.RecalculateControl</c>, который уже сложил шансы всех висящих ослеплений (сумма, не наибольшее)
        /// и перед записью зовёт <see cref="MissChanceClear"/>; здесь значение просто кладётся.
        /// </summary>
        public static void MissChanceSet(Unit attacker, float chance)
        {
            if (attacker == null) return;

            chance = Mathf.Clamp01(chance);
            if (chance <= 0f) { missChanceByAttacker.Remove(attacker); return; }

            missChanceByAttacker[attacker] = chance;
            WatchDeath(attacker);
        }

        /// <summary>
        /// Шанс промаха бьющего, 0..1. Шаг 4 (§13 схемы): промах ЕДЕТ В ПАКЕТЕ — читает отправитель при сборке
        /// (<see cref="DamagePacket.Create"/>), приёмник складывает его с шансами ухода жертвы в один бросок (Р3).
        /// </summary>
        public static float MissChanceOf(Unit attacker)
        {
            if (attacker == null || missChanceByAttacker.Count == 0) return 0f;
            return missChanceByAttacker.TryGetValue(attacker, out float miss) ? miss : 0f;
        }

        /// <summary>Снять шанс промаха.</summary>
        public static void MissChanceClear(Unit attacker)
        {
            if (attacker == null) return;

            missChanceByAttacker.Remove(attacker);
        }

        /// <summary>Есть ли на юните ослепление (для UI/проверок).</summary>
        public static bool IsBlinded(Unit attacker)
        {
            return attacker != null && missChanceByAttacker.ContainsKey(attacker);
        }

        // ============================= МОДИФИКАТОРЫ ВХОДЯЩЕГО УРОНА ==

        /// <summary>Правило изменения входящего урона на конкретном юните (уязвимость или защита).</summary>
        public class IncomingRule
        {
            /// <summary>Множитель урона: 1.4 = +40% входящего, 0.5 = −50% входящего.</summary>
            public float multiplier = 1f;
            /// <summary>Тип урона, к которому применяется правило. Пусто — любой тип.</summary>
            public DamageType onlyType;
            /// <summary>Применять только к прямым атакам юнитов (не к эффекторам/зонам).</summary>
            public bool onlyDirectAttack;

            // ---- Добавлено 2026-08-17 под ось «реакции» конструктора пассивок ----
            // Правило перестало быть только множителем: сюда переехали механики трёх снесённых классов
            // (Evasion, FlatDamageBlock и защитная половина Counterattack). Значения по умолчанию
            // нейтральны, поэтому старые потребители (уязвимость с атак, баф и щит умения) не меняются.

            /// <summary>Шанс полностью уйти от удара, 0..1. 0 — не уклоняться. Бросок делает только сервер.</summary>
            public float evadeChance;

            /// <summary>Сколько единиц вычесть из удара ПОСЛЕ множителя. 0 — не вычитать.</summary>
            public float flatBlock;

            /// <summary>Нижняя граница урона после вычета (обычно 1 — удар нельзя обнулить совсем).</summary>
            public float minDamage;

            /// <summary>Доля текущего здоровья, ниже которой вычет удваивается. 0 — без удвоения.</summary>
            public float doubleBlockBelowHp;

            /// <summary>Применять правило только к ударам спереди. Бьющий неизвестен — правило не применяется.</summary>
            public bool onlyFromFront;

            /// <summary>Полный угол сектора «спереди» в градусах (90 — по 45 в каждую сторону от взгляда).</summary>
            public float frontAngle = 90f;

            /// <summary>
            /// Умение-источник правила — ТОЛЬКО для диагностики (решение Artsiom 07.09.2026), на расчёт не влияет.
            /// null — источник без ассета.
            ///
            /// ВНИМАНИЕ: одинаковые правила (тот же множитель и тот же тип) НЕ заводятся дважды —
            /// второй владелец присоединяется к уже существующему правилу (IncomingDamageModifier.AddRule,
            /// счётчик holders). Поэтому здесь стоит ПЕРВЫЙ выдавший источник, а не все сразу.
            /// </summary>
            public Ability source;

            // [Interflow fix 2026-09-04 damage-full-packet] Уведомление onEvaded «по правилу» СНЕСЕНО (решение
            // Artsiom Р4, 03.09.2026): при одном общем броске приёмник не знает, чей вклад сработал. Вместо него —
            // событие приёмника «удар не достиг цели» (HitMissedListenerAdd / NotifyHitMissed ниже).
        }

        /// <summary>
        /// Пришёл ли удар спереди: сравниваем направление на бьющего с тем, куда смотрит жертва.
        /// Взгляд берём у юнита, а не у корневого объекта: корень не вращается, поворот живёт
        /// на horizontalPart (см. Unit.LookDirection).
        /// </summary>
        public static bool IsHitFromFront(Unit victim, Unit attacker, float fullAngle)
        {
            if (victim == null || attacker == null) return false;      // бьющий неизвестен — считаем, что не спереди
            if (fullAngle >= 360f) return true;

            Vector3 toAttacker = attacker.transform.position - victim.transform.position;
            toAttacker.y = 0f;
            if (toAttacker.sqrMagnitude < 0.0001f) return true;        // стоят в одной точке — считаем фронтальным

            Vector3 look = victim.LookDirection;
            look.y = 0f;
            if (look.sqrMagnitude < 0.0001f) return false;

            return Vector3.Angle(look.normalized, toAttacker.normalized) <= fullAngle * 0.5f;
        }

        static readonly Dictionary<Unit, List<IncomingRule>> incomingByVictim = new Dictionary<Unit, List<IncomingRule>>();

        /// <summary>Навесить правило на юнита. Возвращает ссылку — её же передавать в <see cref="IncomingRuleRemove"/>.</summary>
        public static IncomingRule IncomingRuleAdd(Unit victim, IncomingRule rule)
        {
            if (victim == null || rule == null) return null;

            if (!incomingByVictim.TryGetValue(victim, out var list))
            {
                list = new List<IncomingRule>();
                incomingByVictim[victim] = list;
            }

            list.Add(rule);
            WatchDeath(victim);
            return rule;
        }

        /// <summary>Снять ранее навешенное правило.</summary>
        public static void IncomingRuleRemove(Unit victim, IncomingRule rule)
        {
            if (victim == null || rule == null) return;
            if (!incomingByVictim.TryGetValue(victim, out var list)) return;

            list.Remove(rule);
            if (list.Count == 0) incomingByVictim.Remove(victim);
        }

        /// <summary>
        /// Суммарный шанс «удар не достиг цели» со стороны ЖЕРТВЫ: сумма <see cref="IncomingRule.evadeChance"/>
        /// всех её правил, прошедших свои условия («только прямые», «только тип», «только спереди»).
        /// Шаг 4 (решение Artsiom Р3, 03.09.2026): приёмник складывает это число с шансом промаха бьющего
        /// из пакета, зажимает до единицы и делает ОДИН бросок. Бросков здесь нет — только сумма.
        /// Условие «только тип» при пакете с несколькими записями считается выполненным, если тип
        /// совпадает ХОТЬ С ОДНОЙ записью (техническое решение сессии; в контенте таких правил нет).
        /// </summary>
        public static float HitAvoidChance(Unit victim, in DamagePacket packet)
        {
            if (victim == null || incomingByVictim.Count == 0) return 0f;
            if (!incomingByVictim.TryGetValue(victim, out var list)) return 0f;

            float sum = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                IncomingRule r = list[i];
                if (r == null || r.evadeChance <= 0f) continue;
                if (r.onlyDirectAttack && !packet.directAttack) continue;
                if (r.onlyType != null && !PacketHasType(in packet, r.onlyType)) continue;
                if (r.onlyFromFront && !IsHitFromFront(victim, packet.attackingUnit, r.frontAngle)) continue;

                sum += r.evadeChance;
            }

            return sum;
        }

        static bool PacketHasType(in DamagePacket packet, DamageType type)
        {
            for (int i = 0; i < packet.RecordCount; i++)
                if (packet.Record(i).damageType == type) return true;
            return false;
        }

        /// <summary>
        /// Изменение входящего урона правилами жертвы: множитель и вычет числом. Вызывается приёмником
        /// (<c>UnitReceiver.Receive</c>) для каждой записи пакета ДО штатных колбэков и ДО формулы брони.
        /// Бросков промаха и ухода здесь с шага 4 НЕТ — они сведены в один бросок приёмника
        /// (<see cref="HitAvoidChance"/>, решение Р3).
        /// </summary>
        public static float ModifyIncomingDamage(Unit victim, Unit attacker, DamageType damageType, float amount, bool directAttack)
        {
            if (victim == null || incomingByVictim.Count == 0) return amount;
            if (!incomingByVictim.TryGetValue(victim, out var list)) return amount;

            for (int i = 0; i < list.Count; i++)
            {
                IncomingRule r = list[i];
                if (r == null) continue;
                if (r.onlyDirectAttack && !directAttack) continue;
                if (r.onlyType != null && r.onlyType != damageType) continue;
                if (r.onlyFromFront && !IsHitFromFront(victim, attacker, r.frontAngle)) continue;

                // Порядок фиксирован: множитель → вычет числом → нижняя граница.
                float beforeRule = amount;   // для лога изменения
                amount *= r.multiplier;

                if (r.flatBlock > 0f)
                {
                    float block = r.flatBlock;

                    if (r.doubleBlockBelowHp > 0f && victim.maxHealth > 0f &&
                        victim.health / victim.maxHealth < r.doubleBlockBelowHp) block *= 2f;

                    amount = Mathf.Max(r.minDamage, amount - block);
                }

                // Каждое сработавшее правило пишем отдельно: у жертвы их может быть несколько
                // (снижение «Стены щитов» и чужая уязвимость), и важно видеть вклад каждого.
                if (InterflowDebug.VerboseOn && !Mathf.Approximately(beforeRule, amount))
                    InterflowDebug.Verbose("ВХОДЯЩИЙ УРОН ИЗМЕНЁН у " + InterflowDebug.Name(victim) + ": " +
                                           beforeRule.ToString("0.#") + " → " + amount.ToString("0.#") +
                                           (r.multiplier != 1f ? " (×" + r.multiplier.ToString("0.##") + ")" : "") +
                                           " | правило=" + (r.source != null ? r.source.name : "без ассета"));
            }

            return amount;
        }

        // ============================= УВЕДОМЛЕНИЕ О ПОЛУЧЕННОМ УРОНЕ ==

        /// <summary>Слушатель: (жертва, атакующий (может быть null), тип урона, нанесённый урон, прямая ли атака).</summary>
        public delegate void DamagedHandler(Unit victim, Unit attacker, DamageType damageType, float damageDealt, bool directAttack);

        static readonly Dictionary<Unit, List<DamagedHandler>> damagedListeners = new Dictionary<Unit, List<DamagedHandler>>();

        /// <summary>Подписать реакцию на получение урона этим юнитом (контрудар, ответная заморозка).</summary>
        public static void DamagedListenerAdd(Unit victim, DamagedHandler handler)
        {
            if (victim == null || handler == null) return;

            if (!damagedListeners.TryGetValue(victim, out var list))
            {
                list = new List<DamagedHandler>();
                damagedListeners[victim] = list;
            }

            list.Add(handler);
            WatchDeath(victim);
        }

        /// <summary>Отписать реакцию.</summary>
        public static void DamagedListenerRemove(Unit victim, DamagedHandler handler)
        {
            if (victim == null || handler == null) return;
            if (!damagedListeners.TryGetValue(victim, out var list)) return;

            list.Remove(handler);
            if (list.Count == 0) damagedListeners.Remove(victim);
        }

        // ============================= СОБЫТИЕ «УДАР НЕ ДОСТИГ ЦЕЛИ» ==
        // [Interflow fix 2026-09-04 damage-full-packet] Решение Artsiom Р4 (03.09.2026): при одном общем броске
        // промаха и ухода приёмник не разбирает, чей вклад сработал, поэтому уведомление «по правилу» снесено,
        // а вместо него — одно событие приёмника на жертву. Приходит и тогда, когда мимо ушёл сам бьющий из-за
        // ослепления. Потребитель — связка «удар не достиг — ударил в ответ» конструктора пассивок.

        /// <summary>Слушатель: (жертва, бьющий — может быть null, если удар не от юнита).</summary>
        public delegate void HitMissedHandler(Unit victim, Unit attacker);

        static readonly Dictionary<Unit, List<HitMissedHandler>> hitMissedListeners = new Dictionary<Unit, List<HitMissedHandler>>();

        /// <summary>Подписать реакцию на «удар не достиг этого юнита».</summary>
        public static void HitMissedListenerAdd(Unit victim, HitMissedHandler handler)
        {
            if (victim == null || handler == null) return;

            if (!hitMissedListeners.TryGetValue(victim, out var list))
            {
                list = new List<HitMissedHandler>();
                hitMissedListeners[victim] = list;
            }

            list.Add(handler);
            WatchDeath(victim);
        }

        /// <summary>Отписать реакцию.</summary>
        public static void HitMissedListenerRemove(Unit victim, HitMissedHandler handler)
        {
            if (victim == null || handler == null) return;
            if (!hitMissedListeners.TryGetValue(victim, out var list)) return;

            list.Remove(handler);
            if (list.Count == 0) hitMissedListeners.Remove(victim);
        }

        /// <summary>Уведомление «удар не достиг цели». Зовёт приёмник после проигранного броска, только сервер.</summary>
        public static void NotifyHitMissed(Unit victim, Unit attacker)
        {
            if (NetworkConnectionHandler.isClient || victim == null) return;
            if (hitMissedListeners.Count == 0) return;
            if (!hitMissedListeners.TryGetValue(victim, out var list)) return;

            // Копия списка: обработчик может отписать себя или соседа прямо во время вызова.
            HitMissedHandler[] snapshot = list.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                HitMissedHandler h = snapshot[i];
                if (h != null) h(victim, attacker);
            }
        }

        /// <summary>
        /// Уведомление о фактически полученном уроне. Вызывается приёмником (<c>UnitReceiver.Receive</c>) ПОСЛЕ снятия ХП.
        /// Только сервер (реакции наносят урон и накладывают эффекты — правило 6).
        /// </summary>
        // Жертвы, у которых поглощающий щит съел часть урона на текущем ударе.
        // Нужно, чтобы отличить «щит принял удар» (реакция должна сработать)
        // от промаха, уклонения и нулевой брони (реакции быть не должно).
        static readonly HashSet<Unit> absorbedThisHit = new HashSet<Unit>();

        /// <summary>Вызывает AbsorbShield, когда реально поглотил часть урона.</summary>
        /// <summary>Лог поглощения щитом — вызывается из AbsorbShield при подробном уровне.</summary>
        public static void MarkAbsorbed(Unit victim)
        {
            if (victim != null) absorbedThisHit.Add(victim);
        }

        /// <summary>
        /// Поглотил ли щит часть урона в ТЕКУЩЕМ ударе по этой жертве. Чтение БЕЗ гашения: флаг гасит
        /// <see cref="NotifyDamaged"/>, и до её вызова признак жив. Нужен приёмнику, чтобы поднять факт
        /// презентации «щит поглотил» (§15 схемы, решение Artsiom Р1 от 07.09.2026).
        /// </summary>
        public static bool AbsorbedThisHit(Unit victim)
        {
            return victim != null && absorbedThisHit.Contains(victim);
        }

        /// <param name="damageDealt">сколько ХП реально снято (0, если щит всё поглотил)</param>
        public static void NotifyDamaged(Unit victim, Unit attacker, DamageType damageType, float damageDealt, bool directAttack)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (victim == null) { absorbedThisHit.Clear(); return; }

            bool absorbed = absorbedThisHit.Remove(victim);      // читаем и сразу гасим флаг этого удара
            if (damageDealt <= 0f && !absorbed)
            {
                InterflowDebug.Verbose("УДАР НЕ ПРОШЁЛ: " + InterflowDebug.Name(attacker) + " → " +
                                       InterflowDebug.Name(victim) + " (промах, уклонение или иммунитет)");
                return;                                          // реакций на несостоявшийся удар нет
            }

            if (absorbed && damageDealt <= 0f)
                InterflowDebug.Verbose("ЩИТ ПОГЛОТИЛ удар целиком: " + InterflowDebug.Name(victim) +
                                       " (бил " + InterflowDebug.Name(attacker) + ")");
            if (damagedListeners.Count == 0) return;
            if (!damagedListeners.TryGetValue(victim, out var list)) return;

            // Копия списка: обработчик может отписать себя или соседа прямо во время вызова
            DamagedHandler[] snapshot = list.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                DamagedHandler h = snapshot[i];
                if (h != null) h(victim, attacker, damageType, damageDealt, directAttack);
            }
        }

        // ============================= ЧИСТКА ==

        static readonly HashSet<Unit> watched = new HashSet<Unit>();

        /// <summary>Подписка на смерть юнита, чтобы не копить записи по уничтоженным объектам.</summary>
        static void WatchDeath(Unit unit)
        {
            if (unit == null || watched.Contains(unit)) return;

            watched.Add(unit);
            unit.OnDie += ForgetOnDie;
        }

        static void ForgetOnDie(Unit unit, int killerPlayer, Unit killerUnit, bool rewards)
        {
            Forget(unit);
        }

        /// <summary>Убрать все записи по юниту.</summary>
        public static void Forget(Unit unit)
        {
            if (unit == null) return;

            armorPierceByAttacker.Remove(unit);
            missChanceByAttacker.Remove(unit);
            incomingByVictim.Remove(unit);
            damagedListeners.Remove(unit);
            hitMissedListeners.Remove(unit);
            absorbedThisHit.Remove(unit);   // иначе ссылка на уничтоженный объект доживёт до ResetAll

            if (watched.Remove(unit)) unit.OnDie -= ForgetOnDie;
        }

        /// <summary>
        /// Автосброс на смене сцены и при перезагрузке домена скриптов. Статические словари живут дольше сцены,
        /// иначе в следующем матче остались бы ссылки на уничтоженные объекты.
        /// Подписка тут, а не в MatchManager, чтобы не трогать чужие файлы.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void WireSceneReset()
        {
            ResetAll();

            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        static void OnSceneUnloaded(Scene scene)
        {
            ResetAll();
        }

        /// <summary>
        /// Полный сброс. Статические словари переживают перезагрузку сцены,
        /// иначе в следующем матче останутся ссылки на уничтоженные объекты.
        /// </summary>
        public static void ResetAll()
        {
            foreach (Unit u in watched)
            {
                if (u != null) u.OnDie -= ForgetOnDie;
            }

            watched.Clear();
            armorPierceByAttacker.Clear();
            missChanceByAttacker.Clear();
            incomingByVictim.Clear();
            damagedListeners.Clear();
            hitMissedListeners.Clear();
            absorbedThisHit.Clear();
        }
    }
}
