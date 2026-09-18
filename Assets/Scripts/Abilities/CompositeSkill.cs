using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>Как скилл выбирает, на кого он действует.</summary>
    public enum SkillTargetMode
    {
        [InspectorName("На себя")]                 Self,
        [InspectorName("Вся команда")]             WholeTeam,
        [InspectorName("Область вокруг кастера")]  AreaAroundSelf,
        [InspectorName("Конус перед кастером")]    Cone,
        [InspectorName("Умный выбор юнита")]       SmartUnit,
        [InspectorName("Умный выбор точки")]       SmartPoint
    }

    /// <summary>Чем скилл доставляется до цели.</summary>
    public enum SkillDelivery
    {
        [InspectorName("Мгновенно")] Instant,
        [InspectorName("Снарядом")]  Projectile
    }

    /// <summary>От какой точки стратегия отсчитывает «ближайшего» при касте с кнопки.</summary>
    public enum SkillSearchOrigin
    {
        [InspectorName("От кастера")]              Caster,
        [InspectorName("От вражеской точки линии")] EnemyLanePoint
    }

    /// <summary>
    /// КОНСТРУКТОР СКИЛЛА: способность, которая собирается в редакторе из готовых блоков,
    /// без написания нового скрипта. Живёт на штатных рельсах Ability — поэтому автоматически
    /// получает КД, ману, требования, панель умений, сеть и автокаст.
    ///
    /// Как это работает по шагам:
    /// 1. Клик по кнопке или автокаст → штатный Unit.UseAbilityItem (проверяет КД, ману, немоту).
    /// 2. Сервер ждёт castTime и играет штатную анимацию «cast» — момент срабатывания задаётся
    ///    именно временем каста, а не событиями аниматора (на выделенном сервере аниматор выключен).
    /// 3. Сервер рассылает AbilityUseClientRpc → Use() выполняется НА ВСЕХ ПИРАХ.
    /// 4. Прицел, потом серверный гейт, потом блоки эффектов. ВИЗУАЛА В Use() НЕТ (с 2026-08-06):
    ///    сервер публикует факт SkillFired, а рисует его единственный клиентский презентер.
    ///
    /// Прямого прицеливания игроком в игре нет: цель либо подставляет автокаст юнита,
    /// либо выбирает стратегия внутри самого скилла (для кастов с кнопки замка и героя).
    /// </summary>
    public partial class CompositeSkill : InterflowAbility
    {
        // ==================================================================== ЦЕЛЬ ==

        // Ось «когда срабатывает» (SkillTrigger) СНЕСЕНА блоком Б7 (2026-09-05, целевая модель §9): переключатель
        // ушёл в Б6, режим «аура» — в пассивки (CompositePassive, блок 8); каждое умение — разовый каст по нажатию
        // или автокасту. Аура на время — этот же разовый каст с зоной, идущей за носителем (блок 10, «зона идёт за кастером»).

        [Header("Цель")]
        [Tooltip("Как выбираются цели. «Умный выбор» означает, что цель подбирает стратегия ниже, а не игрок.")]
        public SkillTargetMode targetMode = SkillTargetMode.AreaAroundSelf;

        [Tooltip("Умение по кнопке (замок или герой). ВКЛ — умение всегда становится типа Active, " +
                 "то есть срабатывает сразу по нажатию, а цель ищет стратегия внутри самого умения. " +
                 "ВЫКЛ — умение для автокаста юнитом: цель ему подставляет компонент автокаста.")]
        public bool buttonCast;

        [Tooltip("Стратегия выбора цели для режимов «умный выбор». Её же читает автокаст юнита — " +
                 "настройка на самом юните в этом случае не используется.")]
        public SkillTargetStrategy targetStrategy = SkillTargetStrategy.Nearest;

        [Tooltip("Только для умения по кнопке: от какой точки стратегия отсчитывает «ближайшего» — " +
                 "от самого кастера или от вражеской точки линии (для умений замка обычно второе). " +
                 "На дальность не влияет: она всегда мерится от кастера по castRange.")]
        public SkillSearchOrigin searchOrigin = SkillSearchOrigin.Caster;

        [Tooltip("Для стратегии «наибольший запас ХП»: мерить текущее здоровье вместо максимального.")]
        public bool strategyUseCurrentHealth;

        [Tooltip("Для стратегии «раненый ниже порога»: доля здоровья, ниже которой юнит считается целью " +
                 "(0.3 — только те, у кого меньше тридцати процентов).")]
        [Range(0f, 1f)]
        public float strategyHpThreshold = 0.3f;

        // Поле autoCastSelfHpBelow снято 2026-09-16: условие по своему здоровью переехало в блок 21
        // (SkillCastConditionsBlock.casterHpBelow) и теперь работает не только у автокаста, но и у кнопки.
        // Миграции не было: во всех 13 ассетах с этим ключом стояло 0 (греп по Resources/Ability).

        [Tooltip("ПРЕДПОЧТЕНИЕ ПРИ ВЫБОРЕ ЦЕЛИ: состояние, которого у цели быть не должно. Есть среди кандидатов " +
                 "цель без него — умение выберет её, даже если стратегия указала бы на другую. Работает только " +
                 "в режимах «умный выбор» — и при автоприменении, и при применении с кнопки.\n\n" +
                 "Со стратегией «текущая цель атаки кастера» кандидатов нет — цель выбрана боем; там настройка " +
                 "только запрещает применение, и только при варианте «не применять умение». " +
                 "Выключено — стратегия работает как раньше.")]
        public SkillTargetAvoidState avoidTargetState = SkillTargetAvoidState.None;

        [Tooltip("Только для варианта «указанное состояние»: какой именно ассет состояния искать на цели.")]
        public Effector avoidTargetEffector;

        [Tooltip("Когда все кандидаты уже под этим состоянием: выбрать цель как обычно (настройка работает " +
                 "приоритетом) или не применять умение вовсе.\n\n" +
                 "ОТКАТ при отказе сохраняется только у автоприменения с НЕНУЛЕВОЙ дальностью: без дальности " +
                 "и при применении с кнопки цель ищется уже внутри применения, и откат к тому моменту потрачен.")]
        public SkillNoFreeTargetFallback avoidNoFreeTarget = SkillNoFreeTargetFallback.PickAnyway;

        [Header("Селектор ролей")]
        [Tooltip("ВТОРОЙ СЕЛЕКТОР УМЕНИЯ, парный к «свой/союзник/враг». Боевые роли, с которыми умение " +
                 "вообще работает: и при выборе цели стратегией, и при сборе целей в области, и во всех блоках. " +
                 "Пусто — роль не ограничивает, годятся все.")]
        public Unit.UnitCategory[] targetCategories;

        [Tooltip("ТРЕТИЙ СЕЛЕКТОР УМЕНИЯ: конкретные заготовки юнитов, которые вообще могут быть целью. " +
                 "Сравнение идёт по идентификатору типа юнита (unitTypeID), а не по ссылке на префаб. " +
                 "Работает вместе с ролями по «И»: цель обязана пройти оба списка. " +
                 "Пусто — заготовка не ограничивает, годятся все.")]
        public Unit[] targetPrefabs;

        [Tooltip("ЦЕЛЬ ТОЛЬКО ВПЕРЕДИ: полный угол сектора перед кастером в градусах (120 — это ±60° " +
                 "от взгляда). Цель вне сектора не берётся, доворота ради каста не делается. " +
                 "0 или 360 и больше — фильтра нет. Читается ТОЛЬКО в режимах «умный выбор»: " +
                 "у области и конуса направление задаёт сам режим.")]
        [Min(0)]
        public float targetFrontAngle;

        [Header("Область со своим селектором")]
        [Tooltip("ОБЛАСТЬ НАБИРАЕТСЯ ОТДЕЛЬНЫМ СЕЛЕКТОРОМ: центр выбирается как обычно (селектор, роли, " +
                 "заготовки), а вот КОГО задевает область вокруг него — решает селектор ниже, без ролей " +
                 "и заготовок. Например: целиться во врага, а лечить своих вокруг него. " +
                 "Читается ТОЛЬКО в режиме «умный выбор точки».")]
        public bool independentAreaTargets;

        [Tooltip("Кого задевает область при включённой настройке выше. Лимит целей, «кого оставить» " +
                 "и «включать кастера» работают как обычно.")]
        public UnitSelector areaSelector;

        [Header("Ограничение количества")]
        [Tooltip("Максимум целей за каст. 0 — без ограничения.")]
        [Min(0)]
        public int maxTargets;

        [Tooltip("Кого оставлять, когда целей больше лимита: ближайших к точке приложения или случайных.")]
        public SkillTargeting.MultiPick multiPick = SkillTargeting.MultiPick.Nearest;

        [Tooltip("Включать в цели самого кастера (для лечения и бафов по области). " +
                 "В режиме «Вся команда» не работает: там список берётся из боевых юнитов команды, " +
                 "а замок, башни и не подчиняющиеся приказам призванные в него не входят.")]
        public bool includeSelf;

        [Tooltip("Полный угол конуса перед кастером в градусах (90 — это ±45° от взгляда). " +
                 "Работает только в режиме «конус». Значение 360 и больше — круг без учёта направления.")]
        public float coneAngle = 90f;

        [Tooltip("Важно ли направление кастера. ВКЛ — умение бьёт туда, куда юнит смотрит, и перед " +
                 "применением юнит доворачивается к цели (каст ждёт доворота). Нужно конусу; " +
                 "большинству умений — нет, они от направления не зависят. " +
                 "Если целью служит текущая цель атаки, юнит уже развёрнут к ней боем — доворот просто не понадобится.")]
        public bool directionMatters;

        // Базовые поля Ability, которые здесь работают:
        //   radius   — радиус области / конуса / зоны вокруг выбранной точки, а для стратегии
        //                     «скопление врагов» — ещё и радиус, в котором считается плотность;
        //   castRange— дальность: для автокаста это дистанция подхода юнита к цели, для каста
        //                     с кнопки — радиус поиска цели вокруг КАСТЕРА (0 или пусто — без ограничения);
        //   castTime — задержка срабатывания (момент удара), под неё подгоняется анимация;
        //   unitSelector    — кто вообще может быть целью (свой/союзник/враг + тип передвижения);
        //   cooldown/duration — как обычно (цены в мане у умений нет — Б5, 2026-09-04).

        // ================================================================ ДОСТАВКА ==

        [Header("Доставка")]
        [Tooltip("Мгновенно — эффекты применяются сразу. Снарядом — вылетает снаряд, который несёт " +
                 "урон, состояния и оглушение; остальные блоки при этом срабатывают сразу в момент каста.")]
        public SkillDelivery delivery = SkillDelivery.Instant;

        [Tooltip("Префаб снаряда. Обязателен при доставке снарядом.")]
        public Projectile projectilePrefab;

        [Tooltip("Снаряд летит за целью (самонаведение). ВЫКЛЮЧАТЬ НЕЛЬЗЯ: штатный снаряд без самонаведения " +
                 "наносит урон только по площади, а площадного режима у снаряда умения нет — цель просто не получит урона. " +
                 "Оставлено как поле, чтобы значение было видно; валидатор ругается на ВЫКЛ.")]
        public bool projectileFollowsTarget = true;

        [Tooltip("Снаряд считается ПРЯМОЙ АТАКОЙ: провокация, состояния атаки, промах, проки и шкала носителя. " +
                 "По умолчанию выкл. — снаряд умения бьёт как способность и ничего из перечисленного не задевает. " +
                 "Единственный источник признака прямой атаки у снаряда умения; у вложенного умения включать нельзя.")]
        public bool projectileDirectAttack;

        // ============================================================= ПРЕЗЕНТАЦИЯ ==

        [Header("Презентация")]
        // [Interflow 2026-09-17, шаг 4 слияния] Десять плоских полей презентации (spawnSocket, localOffset,
        // castVFX, castVfxLifetime, impactVFX, impactVfxLifetime, castSound, impactSound, soundVolume,
        // procAnimationState) сведены в ОДИН набор EventPresentation (решение Artsiom 26 от 17.09.2026):
        // у умения и у блока-хозяина форма набора одна, проигрыватель у клиента один.
        // Ассеты переписаны скриптом scripts/migrate_skill_presentation.py (значения перенесены один в один).
        // FormerlySerializedAs здесь НЕ помогает: атрибут работает на уровне имени поля, а не пути, и смену
        // вложенности плоского поля во вложенное не переносит.
        [Tooltip("Визуал каста: играется в момент срабатывания умения. Точка привязки — точка НА КАСТЕРЕ " +
                 "(компонент CharacterSockets на префабе; нет компонента — центр объекта); из неё же вылетает снаряд. " +
                 "Пусто — умение ничего не показывает.")]
        public EventPresentation presentation = new EventPresentation();

        [Tooltip("Состояние-значок: вешается на каждую цель, чтобы игрок видел иконку в панели состояний. " +
                 "Значок показывается только у ненакапливаемых состояний (Stacks выключен) с заданной иконкой.")]
        public Effector statusEffector;

        // =================================================================== БЛОКИ ==

        [Header("Блок 1 — стоимость в здоровье")]
        public SkillSelfCostBlock selfCost = new SkillSelfCostBlock();

        [Header("Блок 2 — рывок цели к кастеру")]
        public SkillPullBlock pull = new SkillPullBlock();

        [Header("Блок 3 — урон")]
        public SkillDamageBlock damage = new SkillDamageBlock();

        [Header("Блок 4 — высасывание ХП")]
        public SkillDrainBlock drain = new SkillDrainBlock();

        [Header("Блок 5 — контроль")]
        public SkillStatusBlock status = new SkillStatusBlock();

        [Header("Блок 6 — состояния")]
        public SkillEffectorsBlock effectors = new SkillEffectorsBlock();

        [Header("Блок 7 — лечение")]
        public SkillHealBlock heal = new SkillHealBlock();

        [Header("Блок 8 — восстановление маны")]
        public SkillManaBlock mana = new SkillManaBlock();

        [Header("Блок 9 — длящийся баф")]
        public SkillBuffBlock buff = new SkillBuffBlock();

        [Header("Блок 10 — щит")]
        public SkillShieldBlock shield = new SkillShieldBlock();

        [Header("Блок 11 — ослепление")]
        public SkillBlindBlock blind = new SkillBlindBlock();

        [Header("Блок 12 — подмена облика")]
        public SkillMorphBlock morph = new SkillMorphBlock();

        [Header("Блок 13 — смена владельца")]
        public SkillOwnershipBlock ownership = new SkillOwnershipBlock();

        [Header("Блок 14 — вторичные цели вокруг основной")]
        public SkillSecondaryBlock secondary = new SkillSecondaryBlock();

        [Header("Блок 15 — отбросить цели")]
        public SkillKnockbackBlock knockback = new SkillKnockbackBlock();

        [Header("Блок 16 — призыв")]
        public SkillSummonBlock summon = new SkillSummonBlock();

        [Header("Блок 17 — зона на земле")]
        public SkillGroundZoneBlock groundZone = new SkillGroundZoneBlock();

        [Header("Блок 18 — перемещение кастера")]
        public SkillCasterMoveBlock casterMove = new SkillCasterMoveBlock();

        [Header("Блок 19 — серверный сервис")]
        public SkillDelegateBlock delegateService = new SkillDelegateBlock();

        [Header("Блок 20 — шкала")]
        public SkillGaugeBlock gaugeBlock = new SkillGaugeBlock();

        [Header("Блок 21 — условия каста")]
        public SkillCastConditionsBlock castConditions = new SkillCastConditionsBlock();

        [Header("Блок 22 — прилёт снаряда")]
        public SkillProjectileImpactBlock projectileImpact = new SkillProjectileImpactBlock();

        // ============================================================ ВЫЧИСЛЯЕМЫЙ ТИП ==

        /// <summary>
        /// Тип способности не задаётся руками, а следует из режима цели (прецедент — GroundZoneAbility).
        /// Режим срабатывания в вычислении больше не участвует — ось снесена блоком Б7 (2026-09-05).
        /// Скилл кнопки всегда Active: панель умений замка и героя принимает только его.
        /// «Умный выбор» без кнопки даёт Unit/Location только ради штатного подхода и доворота юнита
        /// к цели — саму цель всё равно подставляет автокаст, а не игрок.
        /// </summary>
        public override AbilityType type
        {
            get
            {
                if (buttonCast) return AbilityType.Active;

                switch (targetMode)
                {
                    case SkillTargetMode.SmartUnit:  return AbilityType.Unit;
                    case SkillTargetMode.SmartPoint: return AbilityType.Location;
                    default:                         return AbilityType.Active;
                }
            }
        }

        /// <summary>Режимы, где цель подбирает стратегия, а не игрок.</summary>
        public bool PicksTargetByStrategy =>
            targetMode == SkillTargetMode.SmartUnit || targetMode == SkillTargetMode.SmartPoint;

        /// <summary>Стратегия выбора цели этого скилла — её читает и компонент автокаста.</summary>
        public SkillTargetStrategy TargetStrategy => targetStrategy;

        // ==================================================================== ПРОВЕРКА ==

        public override bool Check(Unit castingUnit, int castingPlayer, int level) => CheckCommon(castingUnit, level);
        /// <summary>
        /// Названная цель проходит ПОЛНЫЙ отбор умения: селектор принадлежности плюс боевые роли.
        /// Раньше явная цель принималась как есть: набор целей применял к ней только роли
        /// (CompositeSkill.Targets.CollectTargets, режим «умный выбор юнита»), а селектор «свой/союзник/враг»
        /// не применялся вовсе — лечащее умение ложилось на врага или на здание по присланному номеру.
        /// Отбор уже написан и работает у автокаста, здесь зовётся тот же IsEligibleTarget.
        /// На клиенте решение не принимается — штатная конвенция Ability.Check (см. CheckCommon).
        /// </summary>
        public override bool Check(Unit castingUnit, int castingPlayer, int level, Unit unit)
        {
            if (!IsClientPeer && unit != null && !IsEligibleTarget(unit, castingPlayer, castingUnit)) return false;

            return CheckCommon(castingUnit, level);
        }
        public override bool Check(Unit castingUnit, int castingPlayer, int level, Vector3 location) => CheckCommon(castingUnit, level);

        /// <summary>
        /// Единственная проверка перед кастом — не убьёт ли кастера собственная стоимость в здоровье.
        /// На клиенте всегда true: решение принимает сервер (штатная конвенция Ability.Check).
        /// </summary>
        bool CheckCommon(Unit castingUnit, int level)
        {
            if (IsClientPeer) return true;
            if (selfCost == null || !selfCost.enabled || !selfCost.blockIfLethal) return true;
            if (castingUnit == null) return true;

            float cost = InterflowAbility.LevelValue(selfCost.flatHp, level)
                       + InterflowAbility.LevelValue(selfCost.percentOfCurrentHp, level) * castingUnit.health;

            return castingUnit.health - cost > 0f; // строго: стоимость не должна добить кастера
        }





        // ========================================================== СБРОС СОСТОЯНИЯ ==

        /// <summary>Ассет живёт между Play-сессиями — сбрасываем всё, что накопилось в рантайме.</summary>
        protected override void ResetRuntimeState()
        {
            cachedEffectorSet = null;
            effectorSetBuilt = false;

            // [Interflow 2026-09-18] Снимки способа атаки носителей облика (блок 12, copyAttackMode).
            // Принцип Artsiom 17.09.2026: каждый матч изолирован — пер-юнит хранилище чистится здесь же,
            // иначе ссылки на юнитов прошлого матча дожили бы до следующего.
            ClearAttackModeSnapshots();
        }


    }
}
