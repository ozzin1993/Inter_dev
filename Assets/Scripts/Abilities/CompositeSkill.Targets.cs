using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StrategyCore
{
    // CompositeSkill.Targets.cs — набор целей и стратегия выбора. Вырезано 1:1 из CompositeSkill.cs (разрезка на partial-ы, правило 22).
    public partial class CompositeSkill
    {
        // =================================================================== ЦЕЛИ ==

        /// <summary>
        /// Набор целей по режиму. Списки локальные, а не поля ассета: один SO обслуживает всех носителей,
        /// а блоки внутри каста могут цепочкой спровоцировать чужую реакцию — общий буфер такая вложенность бы испортила.
        /// </summary>
        List<Unit> CollectTargets(Unit castingUnit, int castingPlayer, int level, Unit aimUnit, Vector3 aimPoint)
        {
            List<Unit> result = new List<Unit>();
            List<Unit> gathered = new List<Unit>();

            float area = LevelValue(radius, level);

            switch (targetMode)
            {
                case SkillTargetMode.Self:
                    // На себя фильтры и лимит не применяются: цель ровно одна и она задана режимом.
                    if (castingUnit != null && !castingUnit.dead) result.Add(castingUnit);
                    return result;

                case SkillTargetMode.SmartUnit:
                    // Одна цель — лимит не нужен.
                    if (aimUnit != null && !aimUnit.dead && PassesFilters(aimUnit)) result.Add(aimUnit);
                    return result;

                case SkillTargetMode.WholeTeam:
                {
                    MatchManager mm = MatchManager.Instance;
                    if (mm == null) return result;

                    List<Unit> team = mm.GetCommandUnitsForPlayer(castingPlayer);
                    for (int i = 0; i < team.Count; i++) AddCandidate(team[i], castingUnit, gathered);
                    break;
                }

                case SkillTargetMode.AreaAroundSelf:
                case SkillTargetMode.Cone:
                    // Выборка вынесена целиком: её же зовёт условие «есть цель в области» (блок 21).
                    GatherAroundCaster(castingUnit, castingPlayer, level, gathered);
                    break;

                case SkillTargetMode.SmartPoint:
                {
                    if (area <= 0f) return result;

                    // Область со своим селектором (А4): центр уже выбран обычным путём (селектор,
                    // роли, заготовки), а КОГО задевает область вокруг него — решает areaSelector,
                    // и роли с заготовками к этому набору НЕ применяются. Лимит, «кого оставить»
                    // и «включать кастера» работают как раньше.
                    bool ownAreaSelector = independentAreaTargets;
                    UnitSelector selector = ownAreaSelector ? areaSelector : unitSelector;

                    Unit[] found = Utils.GetUnitsInRadius(new Vector2(aimPoint.x, aimPoint.z), area, castingPlayer,
                                                          selector, -1, includeSelf ? null : castingUnit);
                    if (found == null) return result;

                    for (int i = 0; i < found.Length; i++)
                        AddCandidate(found[i], castingUnit, gathered, !ownAreaSelector);
                    break;
                }
            }

            SkillTargeting.TakeTargets(gathered, aimPoint, maxTargets, multiPick, result);
            return result;
        }

        /// <param name="applyFilters">
        /// Применять ли роли и заготовки. Ложь — только у набора области со своим селектором (А4):
        /// там фильтры остались у выбора ЦЕНТРА, а не у тех, кого область задевает.
        /// </param>
        void AddCandidate(Unit u, Unit castingUnit, List<Unit> into, bool applyFilters = true)
        {
            if (!CandidateAllowed(u, castingUnit, applyFilters)) return;

            into.Add(u);
        }

        /// <summary>Годится ли юнит в набор целей. Вынесено из AddCandidate ради проверки «есть ли кому».</summary>
        bool CandidateAllowed(Unit u, Unit castingUnit, bool applyFilters = true)
        {
            if (u == null || u.dead) return false;
            if (!includeSelf && u == castingUnit) return false;
            if (applyFilters && !PassesFilters(u)) return false;

            return true;
        }

        /// <summary>
        /// ЕДИНСТВЕННАЯ выборка области и конуса вокруг кастера: её зовёт и набор целей
        /// (CollectTargets), и условие «есть цель в области» блока 21 (CompositeSkill.Conditions.cs).
        /// Две копии выборки неизбежно разъехались бы, и условие перестало бы совпадать с кастом.
        ///
        /// Направление конуса — текущий взгляд юнита (Unit.LookDirection), отсечение — общая
        /// формула SkillTargeting.InFront, та же, что у фильтра targetFrontAngle.
        /// </summary>
        /// <param name="into">Куда складывать прошедших. null — нужен только ФАКТ наличия цели.</param>
        /// <returns>Есть ли хотя бы один прошедший кандидат.</returns>
        bool GatherAroundCaster(Unit castingUnit, int castingPlayer, int level, List<Unit> into)
        {
            float area = LevelValue(radius, level);
            if (castingUnit == null || area <= 0f) return false;

            Vector3 origin = castingUnit.transform.position;
            Unit[] found = Utils.GetUnitsInRadius(new Vector2(origin.x, origin.z), area, castingPlayer,
                                                  unitSelector, -1, includeSelf ? null : castingUnit);
            if (found == null) return false;

            bool any = false;
            for (int i = 0; i < found.Length; i++)
            {
                Unit u = found[i];
                if (targetMode == SkillTargetMode.Cone && !SkillTargeting.InFront(castingUnit, u, coneAngle)) continue;
                if (!CandidateAllowed(u, castingUnit)) continue;

                any = true;
                if (into == null) return true;   // спрашивали только факт — дальше перебирать незачем
                into.Add(u);
            }

            return any;
        }

        /// <summary>
        /// Может ли этот юнит быть целью скилла: штатный селектор, фильтры скилла и сектор «впереди».
        /// Нужна автокасту — он ищет кандидатов по своему селектору, а стратегию берёт из скилла,
        /// поэтому без этой проверки лечащий скилл мог бы выбрать врага.
        ///
        /// ЕДИНСТВЕННАЯ точка отсева по ролям, заготовкам и «впереди» на всех трёх путях выбора цели:
        /// автокаст по текущей цели боя, автокаст стратегией и каст с кнопки. Кастер нужен для сектора
        /// «впереди» — без него угол считать не от чего.
        /// </summary>
        public bool IsEligibleTarget(Unit u, int castingPlayer, Unit caster)
        {
            if (u == null || u.dead) return false;
            if (!UnitSelector.IsUnitCompatible(castingPlayer, u, unitSelector)) return false;
            if (!PassesFilters(u)) return false;

            return SkillTargeting.InFront(caster, u, targetFrontAngle);
        }

        /// <summary>
        /// Второй и третий селекторы умения — боевые роли и конкретные заготовки. Работают по «И»,
        /// каждый пустой список пропускает всех.
        /// </summary>
        bool PassesFilters(Unit u)
        {
            if (u == null) return false;

            return CategoryAllowed(u, targetCategories) && PrefabAllowed(u, targetPrefabs);
        }

        /// <summary>
        /// Проходит ли юнит по фильтру заготовок. Сравнение по unitTypeID, а не по ссылке на префаб:
        /// в бою живёт экземпляр, а в поле лежит заготовка. Пустой список — фильтра нет, проходят все
        /// (та же семантика, что у CategoryAllowed). Пустые элементы списка пропускаются.
        /// </summary>
        public static bool PrefabAllowed(Unit unit, Unit[] prefabs)
        {
            if (prefabs == null || prefabs.Length == 0) return true;
            if (unit == null) return false;

            for (int i = 0; i < prefabs.Length; i++)
                if (prefabs[i] != null && prefabs[i].unitTypeID == unit.unitTypeID) return true;

            return false;
        }

        // ============================================================== СТРАТЕГИЯ ==

        /// <summary>Настройки стратегии для общего исполнителя. Точка отсчёта — параметр origin.</summary>
        public SkillTargeting.Options TargetingOptions(int level, Vector3 origin)
        {
            return new SkillTargeting.Options
            {
                useCurrentHealth = strategyUseCurrentHealth,
                hpThreshold = strategyHpThreshold,
                clusterRadius = LevelValue(radius, level),
                clusterSelector = unitSelector,
                origin = origin
            };
        }

        /// <summary>Выбор цели стратегией при касте с кнопки. Только сервер.</summary>
        Unit PickByStrategy(Unit castingUnit, int castingPlayer, int level)
        {
            Vector3 origin = SearchOriginPoint(castingUnit, castingPlayer);
            Unit[] candidates = ButtonCastCandidates(castingUnit, castingPlayer, level);

            // Предпочтение по состоянию цели (решение Artsiom 11.09.2026). Набор здесь свой, собранный
            // этим же вызовом, — правка на месте ничего чужого не задевает.
            if (!SkillTargeting.PreferWithoutState(candidates, avoidTargetState, avoidTargetEffector)
                && avoidNoFreeTarget == SkillNoFreeTargetFallback.Skip)
                return null;

            Unit picked = SkillTargeting.Pick(targetStrategy, candidates, castingUnit, TargetingOptions(level, origin));

            // Стратегия среди свободных никого не признала годным — возвращаем убранных и пробуем
            // по полному набору (решение Artsiom 11.09.2026).
            if (picked == null && avoidNoFreeTarget == SkillNoFreeTargetFallback.PickAnyway)
            {
                SkillTargeting.RestoreFiltered(candidates);
                picked = SkillTargeting.Pick(targetStrategy, candidates, castingUnit, TargetingOptions(level, origin));
            }

            // Стратегия «текущая цель атаки» берёт цель боя мимо набора кандидатов — её закрывает
            // только эта проверка.
            if (avoidNoFreeTarget == SkillNoFreeTargetFallback.Skip
                && SkillTargeting.HasAvoidedState(picked, avoidTargetState, avoidTargetEffector))
                return null;

            return picked;
        }

        /// <summary>Точка, от которой стратегия отсчитывает «ближайшего».</summary>
        Vector3 SearchOriginPoint(Unit castingUnit, int castingPlayer)
        {
            Vector3 casterPos = castingUnit != null ? castingUnit.transform.position : Vector3.zero;
            if (searchOrigin == SkillSearchOrigin.Caster || MatchManager.Instance == null) return casterPos;

            Vector2 point = MatchManager.Instance.AttackTarget(castingPlayer);
            if (point == Vector2.zero) return casterPos; // направления нет — считаем от кастера

            return new Vector3(point.x, casterPos.y, point.y);
        }

        /// <summary>
        /// Кандидаты для каста с кнопки: списки команд матча, отфильтрованные штатным предикатом
        /// селектора. Какие команды брать, решают флаги «свой/союзник/враг» самого селектора.
        ///
        /// Дальность задаёт штатный <c>castRange</c> и мерится ВСЕГДА ОТ КАСТЕРА (решение Artsiom
        /// 2026-08-06), независимо от «точки отсчёта»: та влияет только на то, откуда стратегия считает
        /// «ближайшего». Пустой или нулевой castRange — без ограничения (так настроены семь из девяти
        /// живых ассетов, их поведение не меняется).
        /// </summary>
        Unit[] ButtonCastCandidates(Unit castingUnit, int castingPlayer, int level)
        {
            MatchManager mm = MatchManager.Instance;
            if (mm == null) return null;

            // Ноль/пусто — дальность не ограничена. Сравниваем квадраты: корень не нужен.
            float range = LevelValue(castRange, level);
            float rangeSqr = range > 0f ? range * range : 0f;
            Vector3 casterPos = castingUnit != null ? castingUnit.transform.position : Vector3.zero;

            List<Unit> pool = new List<Unit>();

            if (unitSelector.isOwn || unitSelector.isAlly)
                pool.AddRange(mm.GetCommandUnitsForPlayer(castingPlayer));

            if (unitSelector.isEnemy)
            {
                int team = TeamIndexOfPlayer(castingPlayer);
                TeamWaveConfig enemy = team >= 0 ? mm.Team(1 - team) : null;
                if (enemy != null) pool.AddRange(mm.GetCommandUnitsForPlayer(enemy.ownerPlayer));
            }

            List<Unit> result = new List<Unit>(pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                Unit u = pool[i];
                if (u == castingUnit) continue;
                if (!IsEligibleTarget(u, castingPlayer, castingUnit)) continue;   // один отбор на все три пути
                if (rangeSqr > 0f && castingUnit != null
                    && (u.transform.position - casterPos).sqrMagnitude > rangeSqr) continue; // вне дальности каста

                result.Add(u);
            }

            return result.ToArray();
        }
    }
}
