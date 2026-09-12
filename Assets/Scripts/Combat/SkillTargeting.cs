using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Стратегия косвенного выбора цели скиллом. Прямого прицеливания игроком в игре НЕТ (решение 2026-08-01):
    /// цель всегда выбирает стратегия — либо компонент автокаста у юнита, либо сам скилл при касте с кнопки.
    /// Хранится в ассете скилла (CompositeSkill); исполняется <see cref="SkillTargeting"/>.
    ///
    /// ЗДЕСЬ ТОЛЬКО АЛГОРИТМ — «как выбираем». Кого выбираем, задают два независимых селектора умения:
    /// принадлежность (штатный <see cref="UnitSelector"/>) и боевые роли (массив ролей у скилла).
    /// Кандидаты приходят сюда уже отфильтрованными обоими, поэтому «враг»/«союзник»/«заданной категории»
    /// из названий убраны — раньше они были подписями, а не поведением (решение Artsiom 2026-08-06).
    ///
    /// НЕ путать с приоритетом цели АТАКИ (Unit.targetPriority / InterflowTargeting) — то отдельная система
    /// про то, кого юнит бьёт оружием, и она здесь не участвует.
    /// </summary>
    public enum SkillTargetStrategy
    {
        [InspectorName("Ближайший")]                   Nearest,
        [InspectorName("Самый раненый")]               MostWounded,
        [InspectorName("Раненый ниже порога ХП")]      WoundedBelowThreshold,
        [InspectorName("С наибольшим запасом ХП")]     Strongest,
        [InspectorName("Случайный")]                   RandomOne,
        [InspectorName("Скопление")]                   Cluster,
        [InspectorName("Текущая цель атаки кастера")]  CurrentAttackTarget
    }

    /// <summary>
    /// Состояние, которого умение ИЗБЕГАЕТ при выборе цели: есть среди кандидатов цель без него —
    /// выбирается она, даже если стратегия указала бы на другую (решение Artsiom 11.09.2026:
    /// щит технологии T3_B1, оглушение ледяной стрелы).
    ///
    /// Проверяется только при одиночном выборе цели — режимы «умный выбор юнита» и «умный выбор точки»,
    /// и при автоприменении, и при применении с кнопки. Набора целей по области, конусу и команде
    /// не касается: там цель не выбирается, а собирается (решение Artsiom 11.09.2026).
    ///
    /// Щит состоянием НЕ является (отдельный компонент <see cref="AbsorbShield"/>), контроль живёт
    /// флагами юнита, слепота — реестром шанса промаха. Поэтому это перечисление условий, а не список
    /// ассетов состояний; образец — TargetCondition в BonusDamageVsCondition.
    /// </summary>
    public enum SkillTargetAvoidState
    {
        [InspectorName("Выключено")]            None,
        [InspectorName("Поглощающий щит")]      Shielded,
        [InspectorName("Оглушение")]            Stunned,
        [InspectorName("Обезоруживание")]       Disarmed,
        [InspectorName("Немота")]               Muted,
        [InspectorName("Слепота")]              Blinded,
        [InspectorName("Указанное состояние")]  NamedEffector
    }

    /// <summary>Что делать, когда все кандидаты уже под избегаемым состоянием.</summary>
    public enum SkillNoFreeTargetFallback
    {
        [InspectorName("Выбрать цель как обычно")] PickAnyway,
        [InspectorName("Не применять умение")]     Skip
    }

    /// <summary>
    /// ЕДИНЫЙ исполнитель стратегий выбора цели (правило 5). Раньше алгоритмы жили приватно
    /// внутри AutoAbilityUser — теперь и он, и конструктор скиллов зовут отсюда.
    /// Выбор по плотности перенесён сюда из механики метеоритного дождя, НО сам MatchManager.MeteorStorm
    /// пока работает НА СВОЕЙ копии: его поведение трогать было нельзя (готовый контент). Дубль
    /// осознанный и зафиксирован в логе сессии — свести его отдельной задачей после прогона.
    ///
    /// Алгоритмы перенесены 1:1, включая мелочи: пропуск мёртвых и самого кастера, строгие сравнения
    /// (тайбрейк — первый найденный), поведение при пустом наборе кандидатов. Любое отклонение здесь
    /// молча поменяло бы поведение всего существующего контента с автокастом.
    ///
    /// Все методы — чистый выбор, без побочных эффектов; решение о касте принимает вызывающая сторона.
    /// </summary>
    public static class SkillTargeting
    {
        /// <summary>Как отбирать нескольких из найденных, когда целей больше лимита.</summary>
        public enum MultiPick
        {
            [InspectorName("Ближайшие к точке")] Nearest,
            [InspectorName("Случайные")]         Random
        }

        // ======================================================================== КАНДИДАТЫ ==

        /// <summary>
        /// Кандидаты вокруг юнита штатным поиском. Сам носитель исключается.
        /// Пригодность (свой/союзник/враг + тип передвижения) определяет штатный UnitSelector.
        /// </summary>
        public static Unit[] Candidates(Unit self, float searchRadius, UnitSelector selector)
        {
            if (self == null || searchRadius <= 0f) return null;

            Vector3 p = self.transform.position;
            return Utils.GetUnitsInRadius(new Vector2(p.x, p.z), searchRadius, self.owner, selector, -1, self);
        }

        /// <summary>Кандидаты вокруг произвольной точки (для кастов с кнопки — от вражеской точки линии).</summary>
        public static Unit[] CandidatesAt(Vector3 center, float searchRadius, int playerID, UnitSelector selector, Unit exclude)
        {
            if (searchRadius <= 0f) return null;

            return Utils.GetUnitsInRadius(new Vector2(center.x, center.z), searchRadius, playerID, selector, -1, exclude);
        }

        // ================================================================ ИЗБЕГАЕМОЕ СОСТОЯНИЕ ==

        /// <summary>
        /// Висит ли на юните состояние, которого умение избегает при выборе цели.
        /// Каждое условие читает свой штатный источник: щит — компонент поглощения, контроль — флаги
        /// юнита (их держит пересчёт по висящим состояниям), слепота — реестр шанса промаха.
        /// Реестр промаха заполняет только сервер, но и выбор цели серверный — расхождения нет.
        /// </summary>
        public static bool HasAvoidedState(Unit u, SkillTargetAvoidState avoided, Effector namedEffector)
        {
            if (u == null || avoided == SkillTargetAvoidState.None) return false;

            switch (avoided)
            {
                case SkillTargetAvoidState.Shielded: return AbsorbShield.IsActiveOn(u);
                case SkillTargetAvoidState.Stunned:  return u.stunned;
                case SkillTargetAvoidState.Disarmed: return u.disarmed;
                case SkillTargetAvoidState.Muted:    return u.muted;
                case SkillTargetAvoidState.Blinded:  return InterflowCombat.IsBlinded(u);

                case SkillTargetAvoidState.NamedEffector:
                    if (namedEffector == null) return false;

                    // Сравнение по номеру ассета — так же, как это делает ядро состояний (Effector.cs).
                    for (int i = 0; i < u.effectors.Count; i++)
                    {
                        EffectorHolder eh = u.effectors[i];
                        if (eh != null && eh.effector != null && eh.effector.id == namedEffector.id) return true;
                    }
                    return false;
            }

            return false;
        }

        // Кандидаты, убранные предпочтением: индекс в наборе и сам юнит. Списки переиспользуются между
        // вызовами — на тике автоприменения новый список был бы мусором каждые 0,1 секунды.
        // ИНВАРИАНТ: буфер живёт от PreferWithoutState до RestoreFiltered в одном и том же вызове;
        // вложенных вызовов по другому набору между ними быть не должно.
        static readonly List<int> filteredIndices = new List<int>();
        static readonly List<Unit> filteredUnits = new List<Unit>();

        /// <summary>
        /// Оставить в наборе только кандидатов БЕЗ избегаемого состояния. Набор правится НА МЕСТЕ,
        /// нового массива не создаётся: метод зовётся на тике у каждого носителя, а дырки все стратегии
        /// пропускают штатно — тем же приёмом отсеивает непригодных PickAutoCastTarget.
        ///
        /// Убранные складываются в буфер, чтобы их можно было вернуть: стратегия среди свободных может
        /// никого не признать годным (решение Artsiom 11.09.2026 — см. RestoreFiltered).
        ///
        /// Свободных нет — набор восстанавливается целиком и возвращается false: выбирать ли цель
        /// как обычно или не применять умение, решает вызывающая сторона по настройке умения.
        ///
        /// Мёртвые за свободную цель не считаются: иначе труп без состояния сошёл бы за свободную цель,
        /// стратегия его всё равно отбросила бы, и умение молча не сработало.
        /// </summary>
        public static bool PreferWithoutState(Unit[] candidates, SkillTargetAvoidState avoided, Effector namedEffector)
        {
            filteredIndices.Clear();
            filteredUnits.Clear();

            if (candidates == null || avoided == SkillTargetAvoidState.None) return true;

            // Один проход: признак считается по разу на кандидата, занятые сразу уходят в буфер.
            bool anyFree = false;
            for (int i = 0; i < candidates.Length; i++)
            {
                Unit c = candidates[i];
                if (c == null) continue;

                if (HasAvoidedState(c, avoided, namedEffector))
                {
                    filteredIndices.Add(i);
                    filteredUnits.Add(c);
                    candidates[i] = null;
                    continue;
                }

                if (!c.dead) anyFree = true;
            }

            if (!anyFree)
            {
                RestoreFiltered(candidates);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Вернуть в набор кандидатов, убранных предпочтением. Нужно, когда стратегия среди свободных
        /// никого не признала годным, а умение настроено выбирать цель как обычно: без возврата
        /// применение сгорело бы впустую (решение Artsiom 11.09.2026).
        /// </summary>
        public static void RestoreFiltered(Unit[] candidates)
        {
            if (candidates != null)
                for (int i = 0; i < filteredIndices.Count; i++)
                {
                    int idx = filteredIndices[i];
                    if (idx >= 0 && idx < candidates.Length) candidates[idx] = filteredUnits[i];
                }

            filteredIndices.Clear();
            filteredUnits.Clear();
        }

        // ========================================================================= СТРАТЕГИИ ==

        /// <summary>Ближайший к точке отсчёта. Тайбрейк — первый найденный.</summary>
        public static Unit Nearest(Unit[] candidates, Unit self, Vector3 origin)
        {
            if (candidates == null) return null;

            Unit best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < candidates.Length; i++)
            {
                Unit c = candidates[i];
                if (c == null || c.dead || c == self) continue;

                float sqr = (c.transform.position - origin).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = c; }
            }

            return best;
        }

        /// <summary>Ближайший юнит заданной боевой роли. Роли нет среди кандидатов — null (каст не идёт).</summary>
        /// <summary>
        /// Отсев кандидатов по боевым ролям. Пустой набор ролей — пропускает всех (селектор выключен).
        /// Единая точка: и скилл, и компонент автокаста фильтруют кандидатов только здесь.
        /// </summary>
        public static Unit[] FilterByCategories(Unit[] candidates, Unit.UnitCategory[] categories)
        {
            if (candidates == null) return null;
            // Пустой набор ролей роль не ограничивает — отдаём кандидатов как есть, без копии.
            if (categories == null || categories.Length == 0) return candidates;

            List<Unit> kept = new List<Unit>(candidates.Length);
            for (int i = 0; i < candidates.Length; i++)
            {
                Unit c = candidates[i];
                if (c == null) continue;

                for (int k = 0; k < categories.Length; k++)
                    if (categories[k] == c.unitCategory) { kept.Add(c); break; }
            }

            return kept.ToArray();
        }

        /// <summary>
        /// Самый раненый по ДОЛЕ здоровья. Одна функция обслуживает и «раненого союзника», и «раненого врага» —
        /// разница только в том, кого пропустил UnitSelector в кандидаты.
        /// </summary>
        public static Unit MostWounded(Unit[] candidates, Unit self)
        {
            if (candidates == null) return null;

            Unit best = null;
            float bestRatio = float.MaxValue;
            for (int i = 0; i < candidates.Length; i++)
            {
                Unit c = candidates[i];
                if (c == null || c.dead || c == self) continue;
                if (c.maxHealth <= 0f) continue;

                float ratio = c.health / c.maxHealth;
                if (ratio < bestRatio) { bestRatio = ratio; best = c; }
            }

            return best;
        }

        /// <summary>
        /// Самый раненый, но только среди тех, кто ниже порога доли ХП. В отличие от <see cref="MostWounded"/>
        /// по здоровым не кастует вовсе: никто не просел — цели нет, откат копится дальше.
        /// </summary>
        public static Unit MostWoundedBelowThreshold(Unit[] candidates, Unit self, float hpThreshold)
        {
            if (candidates == null) return null;

            Unit best = null;
            float bestRatio = float.MaxValue;
            for (int i = 0; i < candidates.Length; i++)
            {
                Unit c = candidates[i];
                if (c == null || c.dead || c == self) continue;
                if (c.maxHealth <= 0f) continue;

                float ratio = c.health / c.maxHealth;
                if (ratio >= hpThreshold) continue;

                if (ratio < bestRatio) { bestRatio = ratio; best = c; }
            }

            return best;
        }

        /// <summary>Наибольший запас ХП: по максимальному или по текущему здоровью. Тайбрейк — первый найденный.</summary>
        public static Unit Strongest(Unit[] candidates, Unit self, bool useCurrentHealth)
        {
            if (candidates == null) return null;

            Unit best = null;
            float bestVal = float.MinValue;
            for (int i = 0; i < candidates.Length; i++)
            {
                Unit c = candidates[i];
                if (c == null || c.dead || c == self) continue;

                float val = useCurrentHealth ? c.health : c.maxHealth;
                if (val > bestVal) { bestVal = val; best = c; }
            }

            return best;
        }

        /// <summary>
        /// Равномерно случайный из кандидатов. Сужение по ролям сюда уже применено селектором ролей
        /// умения — приоритета по категории больше нет (решение Artsiom 2026-08-06).
        /// </summary>
        public static Unit AnyRandom(Unit[] candidates, Unit self)
        {
            if (candidates == null) return null;

            List<Unit> pool = new List<Unit>();
            for (int i = 0; i < candidates.Length; i++)
            {
                Unit c = candidates[i];
                if (c == null || c.dead || c == self) continue;

                pool.Add(c);
            }

            if (pool.Count == 0) return null;

            return pool[Random.Range(0, pool.Count)];
        }

        /// <summary>
        /// Союзник «впереди» — в сторону вражеской точки линии (единый источник направления — MatchManager).
        /// mostAdvanced: самый продвинутый к точке; иначе — ближайший из тех, кто впереди носителя.
        /// </summary>
        public static Unit AllyAhead(Unit[] candidates, Unit self, bool mostAdvanced)
        {
            if (candidates == null || self == null || MatchManager.Instance == null) return null;

            Vector2 point = MatchManager.Instance.AttackTarget(self.owner);
            if (point == Vector2.zero) return null;                     // нет цели-направления → каста нет

            Vector3 selfPos = self.transform.position;
            Vector2 selfFlat = new Vector2(selfPos.x, selfPos.z);
            Vector2 toPoint = point - selfFlat;
            if (toPoint.sqrMagnitude < 0.0001f) return null;            // носитель уже на точке → каста нет
            Vector2 dir = toPoint.normalized;

            Unit best = null;
            if (mostAdvanced)
            {
                float bestDistToPoint = float.MaxValue;
                for (int i = 0; i < candidates.Length; i++)
                {
                    Unit c = candidates[i];
                    if (c == null || c.dead || c == self) continue;

                    Vector2 cp = new Vector2(c.transform.position.x, c.transform.position.z);
                    float d = (point - cp).sqrMagnitude;
                    if (d < bestDistToPoint) { bestDistToPoint = d; best = c; }
                }
            }
            else
            {
                float bestFwd = float.MaxValue;
                for (int i = 0; i < candidates.Length; i++)
                {
                    Unit c = candidates[i];
                    if (c == null || c.dead || c == self) continue;

                    Vector2 cp = new Vector2(c.transform.position.x, c.transform.position.z);
                    Vector2 toC = cp - selfFlat;
                    if (Vector2.Dot(toC, dir) <= 0f) continue;          // не впереди носителя
                    float d = toC.sqrMagnitude;
                    if (d < bestFwd) { bestFwd = d; best = c; }
                }
            }

            return best;
        }

        /// <summary>
        /// Текущая цель атаки кастера. Берётся из штатного Unit.target, а НЕ из набора кандидатов:
        /// дальность каста проверит сам UseAbilityItem по castRange. Мёртвая/пустая цель → null.
        /// </summary>
        public static Unit CurrentAttackTarget(Unit self)
        {
            return (self != null && self.target != null && !self.target.dead) ? self.target : null;
        }

        /// <summary>
        /// Юнит в самом плотном скоплении: вес кандидата — сколько подходящих юнитов стоит вокруг него
        /// в радиусе clusterRadius, выбор — взвешенно-случайный. Плотные участки бьются чаще, но серия
        /// кастов не вырождается в одну и ту же точку. Перенесено из механики метеоритного дождя.
        /// </summary>
        public static Unit DensestCluster(IList<Unit> candidates, int playerID, float clusterRadius, UnitSelector selector)
        {
            if (candidates == null || candidates.Count == 0) return null;

            List<Unit> alive = new List<Unit>();
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i] != null && !candidates[i].dead) alive.Add(candidates[i]);
            if (alive.Count == 0) return null;
            if (clusterRadius <= 0f) return alive[Random.Range(0, alive.Count)];

            float[] weights = new float[alive.Count];
            for (int i = 0; i < alive.Count; i++)
            {
                Vector3 p = alive[i].transform.position;
                Unit[] near = Utils.GetUnitsInRadius(new Vector2(p.x, p.z), clusterRadius, playerID, selector);
                weights[i] = (near != null && near.Length > 0) ? near.Length : 1f;
            }

            return alive[WeightedPick(weights)];
        }

        /// <summary>Взвешенно-случайный индекс. Сумма весов ≤ 0 — равномерный выбор.</summary>
        public static int WeightedPick(float[] weights)
        {
            if (weights == null || weights.Length == 0) return 0;

            float sum = 0f;
            for (int i = 0; i < weights.Length; i++) sum += weights[i];
            if (sum <= 0f) return Random.Range(0, weights.Length);

            float r = Random.value * sum;
            for (int i = 0; i < weights.Length; i++) { r -= weights[i]; if (r <= 0f) return i; }

            return weights.Length - 1;
        }

        // ========================================================================= ДИСПЕТЧЕР ==

        /// <summary>Настройки стратегий, которым нужны параметры сверх набора кандидатов.</summary>
        public struct Options
        {
            /// <summary>Для «наибольший запас ХП»: мерить текущее здоровье вместо максимального.</summary>
            public bool useCurrentHealth;
            /// <summary>Для «раненый союзник ниже порога»: доля ХП, ниже которой юнит считается целью.</summary>
            public float hpThreshold;
            /// <summary>Для «скопление врагов»: в каком радиусе вокруг кандидата считать соседей.</summary>
            public float clusterRadius;
            /// <summary>Для «скопление врагов»: кого считать соседями при подсчёте плотности.</summary>
            public UnitSelector clusterSelector;
            /// <summary>Точка отсчёта для стратегий «ближайший». По умолчанию — позиция кастера.</summary>
            public Vector3 origin;
        }

        /// <summary>
        /// Выбрать одну цель по стратегии скилла. Точка отсчёта для «ближайших» берётся из options.origin.
        /// Возвращает null, если подходящей цели нет — тогда каст просто не происходит.
        /// </summary>
        public static Unit Pick(SkillTargetStrategy strategy, Unit[] candidates, Unit self, Options options)
        {
            switch (strategy)
            {
                case SkillTargetStrategy.Nearest:
                    return Nearest(candidates, self, options.origin);

                case SkillTargetStrategy.MostWounded:
                    return MostWounded(candidates, self);

                case SkillTargetStrategy.WoundedBelowThreshold:
                    return MostWoundedBelowThreshold(candidates, self, options.hpThreshold);

                case SkillTargetStrategy.Strongest:
                    return Strongest(candidates, self, options.useCurrentHealth);

                case SkillTargetStrategy.RandomOne:
                    return AnyRandom(candidates, self);

                case SkillTargetStrategy.Cluster:
                    return DensestCluster(candidates, self != null ? self.owner : -1,
                                          options.clusterRadius, options.clusterSelector);

                case SkillTargetStrategy.CurrentAttackTarget:
                    return CurrentAttackTarget(self);
            }

            return null;
        }

        // ============================================================== НЕСКОЛЬКО ЦЕЛЕЙ ==

        /// <summary>
        /// Ограничить набор целей лимитом: либо ближайшие к origin, либо случайные.
        /// maxTargets ≤ 0 — лимита нет, набор переносится целиком. Исходный список не меняется.
        /// </summary>
        public static void TakeTargets(List<Unit> source, Vector3 origin, int maxTargets, MultiPick mode, List<Unit> into)
        {
            if (into == null) return;
            into.Clear();
            if (source == null || source.Count == 0) return;

            if (maxTargets <= 0 || source.Count <= maxTargets)
            {
                into.AddRange(source);
                return;
            }

            if (mode == MultiPick.Nearest)
            {
                // Копия: сортировка не должна портить порядок исходного набора.
                List<Unit> sorted = new List<Unit>(source);
                sorted.Sort((a, b) =>
                {
                    float da = a != null ? (a.transform.position - origin).sqrMagnitude : float.MaxValue;
                    float db = b != null ? (b.transform.position - origin).sqrMagnitude : float.MaxValue;
                    return da.CompareTo(db);
                });
                for (int i = 0; i < maxTargets; i++) into.Add(sorted[i]);
                return;
            }

            // Случайные без повторов: частичная тасовка индексов.
            List<int> indices = new List<int>(source.Count);
            for (int i = 0; i < source.Count; i++) indices.Add(i);
            for (int i = 0; i < maxTargets; i++)
            {
                int swap = Random.Range(i, indices.Count);
                (indices[i], indices[swap]) = (indices[swap], indices[i]);
                into.Add(source[indices[i]]);
            }
        }
    }
}
