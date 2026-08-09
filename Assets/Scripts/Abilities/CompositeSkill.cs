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

        [Header("Срабатывание")]
        [Tooltip("КОГДА умение делает своё дело. Разовый каст — один раз по нажатию или автокасту. " +
                 "Переключатель — включается и выключается, блоки применяются каждый тик, пока включено. " +
                 "Аура — работает сама, без нажатия, каждый тик, пока умение открыто у юнита. " +
                 "Умение-канал (держится, пока хватает маны) собирается штатным флагом «continuous» ниже.")]
        public SkillTrigger trigger = SkillTrigger.Instant;

        [Header("Цель")]
        [Tooltip("Как выбираются цели. «Умный выбор» означает, что цель подбирает стратегия ниже, а не игрок.")]
        public SkillTargetMode targetMode = SkillTargetMode.AreaAroundSelf;

        [Tooltip("Скилл кнопки (умение замка или героя). ВКЛ — скилл всегда становится типа Active, " +
                 "то есть срабатывает сразу по нажатию, а цель ищет стратегия внутри самого скилла. " +
                 "ВЫКЛ — скилл для автокаста юнитом: цель ему подставляет компонент автокаста.")]
        public bool buttonCast;

        [Tooltip("Стратегия выбора цели для режимов «умный выбор». Её же читает автокаст юнита — " +
                 "настройка на самом юните в этом случае не используется.")]
        public SkillTargetStrategy targetStrategy = SkillTargetStrategy.Nearest;

        [Tooltip("Только для скилла кнопки: от какой точки стратегия отсчитывает «ближайшего» — " +
                 "от самого кастера или от вражеской точки линии (для умений замка обычно второе). " +
                 "На дальность не влияет: она всегда мерится от кастера по castRange.")]
        public SkillSearchOrigin searchOrigin = SkillSearchOrigin.Caster;

        [Tooltip("Для стратегии «наибольший запас ХП»: мерить текущее здоровье вместо максимального.")]
        public bool strategyUseCurrentHealth;

        [Tooltip("Для стратегии «раненый ниже порога»: доля здоровья, ниже которой юнит считается целью " +
                 "(0.3 — только те, у кого меньше тридцати процентов).")]
        [Range(0f, 1f)]
        public float strategyHpThreshold = 0.3f;

        [Header("Селектор ролей")]
        [Tooltip("ВТОРОЙ СЕЛЕКТОР УМЕНИЯ, парный к «свой/союзник/враг». Боевые роли, с которыми умение " +
                 "вообще работает: и при выборе цели стратегией, и при сборе целей в области, и во всех блоках. " +
                 "Пусто — роль не ограничивает, годятся все.")]
        public Unit.UnitCategory[] targetCategories;

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
        //   cooldown/manaCost/duration — как обычно.

        // ================================================================ ДОСТАВКА ==

        [Header("Доставка")]
        [Tooltip("Мгновенно — эффекты применяются сразу. Снарядом — вылетает снаряд, который несёт " +
                 "урон, эффекторы и оглушение; остальные блоки при этом срабатывают сразу в момент каста.")]
        public SkillDelivery delivery = SkillDelivery.Instant;

        [Tooltip("Префаб снаряда. Обязателен при доставке снарядом.")]
        public Projectile projectilePrefab;

        [Tooltip("Снаряд летит за целью (самонаведение). ВЫКЛЮЧАТЬ НЕЛЬЗЯ: штатный снаряд без самонаведения " +
                 "наносит урон только по площади, а площадного режима у снаряда скилла нет — цель просто не получит урона. " +
                 "Оставлено как поле, чтобы значение было видно; валидатор ругается на ВЫКЛ.")]
        public bool projectileFollowsTarget = true;

        // ============================================================= ПРЕЗЕНТАЦИЯ ==

        [Header("Презентация")]
        [Tooltip("Из какой точки модели кастера вылетает снаряд и играет визуал замаха. " +
                 "Точки настраиваются компонентом CharacterSockets на префабе. Нет компонента — берётся центр объекта.")]
        public SkillSocketType spawnSocket = SkillSocketType.RightHand;

        [Tooltip("Смещение от точки привязки в её собственных осях, метры. Например немного вперёд от ладони.")]
        public Vector3 localOffset;

        [Tooltip("Визуал в момент каста, из точки привязки. Пусто — без визуала.")]
        public VFXReferencer castVFX;

        [Tooltip("Через сколько секунд убрать визуал каста.")]
        public float castVfxLifetime = 2f;

        [Tooltip("Визуал в точке приложения (место попадания, центр области). Пусто — без визуала.")]
        public VFXReferencer impactVFX;

        [Tooltip("Через сколько секунд убрать визуал попадания.")]
        public float impactVfxLifetime = 2f;

        [Tooltip("Звук каста. Пусто — без звука.")]
        public AudioClip castSound;

        [Tooltip("Звук попадания. Пусто — без звука.")]
        public AudioClip impactSound;

        [Tooltip("Громкость звуков скилла, 0..1.")]
        [Range(0f, 1f)]
        public float soundVolume = 1f;

        [Tooltip("Имя стейта аниматора кастера, проигрываемого в момент срабатывания умения. Пусто — не проигрывать. " +
                 "Стейт заводится в контроллере юнита: строчными буквами, БЕЗ зацикливания и вне зарезервированных схем ядра " +
                 "(attack0…, death0…, idle0…, idleReady, cast, casting, hit, walk, building, construction) — иначе ядро спутает его со своими. " +
                 "Перебивает текущую анимацию, в том числе замах атаки.")]
        public string procAnimationState = "";

        [Tooltip("Эффектор-значок состояния: вешается на каждую цель, чтобы игрок видел иконку в панели состояний. " +
                 "Значок показывается только у НЕстакающихся эффекторов с заданной иконкой.")]
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

        [Header("Блок 6 — эффекторы")]
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

        [Header("Блок 15 — призыв")]
        public SkillSummonBlock summon = new SkillSummonBlock();

        [Header("Блок 16 — зона на земле")]
        public SkillGroundZoneBlock groundZone = new SkillGroundZoneBlock();

        [Header("Блок 17 — перемещение кастера")]
        public SkillCasterMoveBlock casterMove = new SkillCasterMoveBlock();

        [Header("Блок 18 — серверный сервис")]
        public SkillDelegateBlock delegateService = new SkillDelegateBlock();

        // ============================================================ ВЫЧИСЛЯЕМЫЙ ТИП ==

        /// <summary>
        /// Тип способности не задаётся руками, а следует из РЕЖИМА СРАБАТЫВАНИЯ и режима цели
        /// (прецедент — GroundZoneAbility). Переключатель и аура задают тип сами: ядро для них
        /// держит свой цикл (everyFrameAbilities), а цель эти режимы собирают вокруг носителя.
        /// Скилл кнопки всегда Active: панель умений замка и героя принимает только его.
        /// «Умный выбор» без кнопки даёт Unit/Location только ради штатного подхода и доворота юнита
        /// к цели — саму цель всё равно подставляет автокаст, а не игрок.
        /// </summary>
        public override AbilityType type
        {
            get
            {
                if (trigger == SkillTrigger.Toggle) return AbilityType.Toggle;
                if (trigger == SkillTrigger.Aura) return AbilityType.Aura;

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
        public override bool Check(Unit castingUnit, int castingPlayer, int level, Unit unit) => CheckCommon(castingUnit, level);
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

        // ======================================================================== КАСТ ==

        public override void Use(Unit castingUnit, int castingPlayer, int level)
            => Execute(castingUnit, castingPlayer, level, null, Vector3.zero, false);

        public override void Use(Unit castingUnit, int castingPlayer, int level, Unit unit)
            => Execute(castingUnit, castingPlayer, level, unit,
                       unit != null ? unit.transform.position
                                    : (castingUnit != null ? castingUnit.transform.position : Vector3.zero), true);

        public override void Use(Unit castingUnit, int castingPlayer, int level, Vector3 location)
            => Execute(castingUnit, castingPlayer, level, null, location, true);

        /// <summary>
        /// Тело каста. Выполняется НА ВСЕХ ПИРАХ (ассет рассылает Use через AbilityUseClientRpc),
        /// но ВИЗУАЛА ЗДЕСЬ БОЛЬШЕ НЕТ: с 2026-08-06 презентацию исполняет единый клиентский презентер
        /// по факту SkillFired. Здесь остаются прицел, снаряд, серверный гейт и блоки эффектов.
        /// </summary>
        void Execute(Unit castingUnit, int castingPlayer, int level, Unit explicitTarget, Vector3 explicitLocation, bool hasExplicitAim)
        {
            // ---- Презентация каста ЗДЕСЬ НЕ ИГРАЕТСЯ (перенесена в презентер, 2026-08-06) ----
            // Раньше визуал и звук замаха запускались тут на каждом пире. Теперь их исполняет
            // SkillPresenter по факту SkillFired — иначе на хосте и клиенте получился бы дубль.

            // ---- Прицел ----
            Vector3 casterPos = castingUnit != null ? castingUnit.transform.position : explicitLocation;
            Unit aimUnit = null;
            Vector3 aimPoint = casterPos;

            if (PicksTargetByStrategy)
            {
                if (hasExplicitAim)
                {
                    // Цель/точку подставил автокаст юнита — она пришла в RPC и одинакова на всех пирах.
                    aimUnit = explicitTarget;
                    aimPoint = explicitTarget != null ? explicitTarget.transform.position : explicitLocation;
                }
                else
                {
                    // Каст с кнопки: цель ищет стратегия. Воспроизвести этот выбор на клиенте нельзя —
                    // у него другой набор юнитов и свой генератор случайных чисел. Поэтому дальше только сервер.
                    // Визуал клиент при этом больше не теряет: цель и точку ему привезёт SkillFired (2026-08-06),
                    // по нему презентер покажет и попадание, и визуальную копию снаряда.
                    if (IsClientPeer) return;

                    Unit picked = PickByStrategy(castingUnit, castingPlayer, level);
                    if (picked == null) return; // подходящей цели нет — каст проходит впустую, откат тратится штатно

                    if (targetMode == SkillTargetMode.SmartUnit) aimUnit = picked;
                    aimPoint = picked.transform.position;
                }
            }

            // ---- Снаряд ----
            // Визуал и звук попадания сюда не входят: их играет презентер по факту SkillFired.
            // Сам снаряд с 2026-08-06 создаётся ТОЛЬКО на сервере: он несёт урон, а значит геймплей
            // (правило 6). Чистому клиенту презентер спавнит визуальную копию с нулевым уроном.

            // Доставка снарядом возможна ТОЛЬКО по конкретному юниту и только с самонаведением:
            // штатный снаряд без цели-юнита наносит урон исключительно по площади, а площадного
            // режима у снаряда скилла нет (см. Projectile.Update / Projectile.Damage). Остальные
            // сочетания — ошибка настройки, её ловит валидатор; здесь бьём мгновенно и говорим об этом.
            bool viaProjectile = delivery == SkillDelivery.Projectile && projectilePrefab != null
                                 && castingUnit != null && aimUnit != null && projectileFollowsTarget;

            if (delivery == SkillDelivery.Projectile && !viaProjectile && IsServerPeer)
                Debug.LogWarning($"[{name}] Доставка снарядом невозможна при этих настройках " +
                                 "(нужны цель-юнит, живой кастер, префаб снаряда и включённое самонаведение) — " +
                                 "скилл сработал мгновенно.");

            if (viaProjectile && IsServerPeer) SpawnProjectile(castingUnit, castingPlayer, level, aimUnit);

            // ---- Серверный гейт: всё, что меняет состояние мира, — ниже (правило 6) ----
            // Клиент до сюда не доходит: цели он не считает и эффекторов себе не накладывает.
            // Значки состояний и VFX ему пришлёт сервер отдельным сообщением (SendPresentation).
            if (IsClientPeer) return;

            // Факт срабатывания для презентации: цель и точка уже ФАКТИЧЕСКИЕ (вычислены выше сервером).
            // Публикуется до блоков эффектов — визуал не должен зависеть от того, выжила ли цель.
            // У переключателя и ауры Use зовётся КАЖДЫЙ ТИК: факт не публикуем, иначе презентер
            // проигрывал бы визуал и звук замаха по десять раз в секунду. Постоянный визуал таких
            // умений вешается эффектором или бафом.
            if (!IsEveryTick) EmitSkillFired(castingUnit, this, level, aimUnit, aimPoint);

            List<Unit> targets = CollectTargets(castingUnit, castingPlayer, level, aimUnit, aimPoint);
            ApplyEffects(castingUnit, castingPlayer, level, targets, aimPoint, viaProjectile);

            SendPresentation(targets, level);
            RequestForceSync(); // один раз после всей пачки изменений
        }

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
                    MatchManager mm = MatchManager.instance;
                    if (mm == null) return result;

                    List<Unit> team = mm.GetCommandUnitsForPlayer(castingPlayer);
                    for (int i = 0; i < team.Count; i++) AddCandidate(team[i], castingUnit, gathered);
                    break;
                }

                case SkillTargetMode.AreaAroundSelf:
                case SkillTargetMode.Cone:
                {
                    if (castingUnit == null || area <= 0f) return result;

                    Vector3 origin = castingUnit.transform.position;
                    Unit[] found = Utils.GetUnitsInRadius(new Vector2(origin.x, origin.z), area, castingPlayer,
                                                          unitSelector, -1, includeSelf ? null : castingUnit);
                    if (found == null) return result;

                    // Направление берём у юнита, а НЕ из transform.forward: корневой объект юнита
                    // не вращается, поворот живёт на horizontalPart (см. Unit.LookDirection).
                    Vector3 forward = castingUnit.LookDirection;
                    float halfAngle = coneAngle * 0.5f;
                    bool fullCircle = targetMode != SkillTargetMode.Cone || coneAngle >= 360f;

                    for (int i = 0; i < found.Length; i++)
                    {
                        Unit u = found[i];
                        if (!fullCircle && u != null)
                        {
                            Vector3 dir = u.transform.position - origin; dir.y = 0f;
                            if (dir.sqrMagnitude > 0.0001f && Vector3.Angle(forward, dir) > halfAngle) continue; // вне конуса
                        }
                        AddCandidate(u, castingUnit, gathered);
                    }
                    break;
                }

                case SkillTargetMode.SmartPoint:
                {
                    if (area <= 0f) return result;

                    Unit[] found = Utils.GetUnitsInRadius(new Vector2(aimPoint.x, aimPoint.z), area, castingPlayer,
                                                          unitSelector, -1, includeSelf ? null : castingUnit);
                    if (found == null) return result;

                    for (int i = 0; i < found.Length; i++) AddCandidate(found[i], castingUnit, gathered);
                    break;
                }
            }

            SkillTargeting.TakeTargets(gathered, aimPoint, maxTargets, multiPick, result);
            return result;
        }

        void AddCandidate(Unit u, Unit castingUnit, List<Unit> into)
        {
            if (u == null || u.dead) return;
            if (!includeSelf && u == castingUnit) return;
            if (!PassesFilters(u)) return;

            into.Add(u);
        }

        /// <summary>
        /// Может ли этот юнит быть целью скилла: штатный селектор плюс фильтры скилла.
        /// Нужна автокасту — он ищет кандидатов по своему селектору, а стратегию берёт из скилла,
        /// поэтому без этой проверки лечащий скилл мог бы выбрать врага.
        /// </summary>
        public bool IsEligibleTarget(Unit u, int castingPlayer)
        {
            if (u == null || u.dead) return false;
            if (!UnitSelector.IsUnitCompatible(castingPlayer, u, unitSelector)) return false;

            return PassesFilters(u);
        }

        /// <summary>Второй селектор умения — боевые роли. Пустой набор ролей пропускает всех.</summary>
        bool PassesFilters(Unit u)
        {
            if (u == null) return false;

            return CategoryAllowed(u, targetCategories);
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

            return SkillTargeting.Pick(targetStrategy, candidates, castingUnit, TargetingOptions(level, origin));
        }

        /// <summary>Точка, от которой стратегия отсчитывает «ближайшего».</summary>
        Vector3 SearchOriginPoint(Unit castingUnit, int castingPlayer)
        {
            Vector3 casterPos = castingUnit != null ? castingUnit.transform.position : Vector3.zero;
            if (searchOrigin == SkillSearchOrigin.Caster || MatchManager.instance == null) return casterPos;

            Vector2 point = MatchManager.instance.AttackTarget(castingPlayer);
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
            MatchManager mm = MatchManager.instance;
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
                if (u == null || u.dead || u == castingUnit) continue;
                if (!UnitSelector.IsUnitCompatible(castingPlayer, u, unitSelector)) continue;
                if (!PassesFilters(u)) continue;
                if (rangeSqr > 0f && castingUnit != null
                    && (u.transform.position - casterPos).sqrMagnitude > rangeSqr) continue; // вне дальности каста

                result.Add(u);
            }

            return result.ToArray();
        }

        // ================================================================== СНАРЯД ==

        // Кэш набора эффекторов «блок + значок состояния»: собирается один раз, дальше переиспользуется.
        Effector[] cachedEffectorSet;
        bool effectorSetBuilt;

        /// <summary>Эффекторы, которые получает цель: из блока эффекторов плюс отдельный значок состояния.</summary>
        public Effector[] EffectorsForTargets()
        {
            if (effectorSetBuilt) return cachedEffectorSet;

            List<Effector> list = new List<Effector>();
            if (effectors != null && effectors.enabled && effectors.records != null)
                for (int i = 0; i < effectors.records.Length; i++)
                    if (effectors.records[i] != null && effectors.records[i].effector != null)
                        list.Add(effectors.records[i].effector);

            if (statusEffector != null) list.Add(statusEffector);

            cachedEffectorSet = list.Count > 0 ? list.ToArray() : null;
            effectorSetBuilt = true;
            return cachedEffectorSet;
        }

        /// <summary>
        /// Спавн штатного снаряда. Снаряд НЕ сетевой объект — его симулирует каждый пир у себя,
        /// поэтому спавним до серверного гейта. Снаряд несёт только то, что умеют его штатные поля:
        /// урон (первая запись блока урона), эффекторы и оглушение.
        /// </summary>
        void SpawnProjectile(Unit castingUnit, int castingPlayer, int level, Unit aimUnit)
        {
            Transform socket = ResolveSocket(castingUnit, spawnSocket);
            Vector3 spawnPos = SocketPosition(castingUnit, spawnSocket, localOffset);
            Quaternion spawnRot = socket != null ? socket.rotation : Quaternion.identity;

            DamageType dmgType;
            float dmg = FirstProjectileDamage(level, out dmgType);

            Projectile spawned = Projectile.Spawn(castingPlayer, castingUnit, projectilePrefab, spawnPos, spawnRot,
                                                  aimUnit, false, dmg, dmgType, true);
            if (spawned == null) return;

            // Снаряд уносит только урон и оглушение. Эффекторы ему не отдаём осознанно: ядро применяет
            // Projectile.attackEffectors лишь когда кастер погиб, а при живом кастере накладывает
            // эффекторы ЕГО автоатаки — эффекторы скилла так бы просто потерялись. Поэтому их
            // накладывает сам скилл в момент каста (см. ApplyEffectors).
            spawned.stunTime = (status != null && status.enabled) ? LevelValue(status.stunSeconds, level) : 0f;
        }

        /// <summary>
        /// Урон, который уносит снаряд: первая непустая запись блока урона. У штатного снаряда одно поле
        /// урона и один тип — остальные записи снарядом не переносятся (валидатор предупреждает).
        /// </summary>
        /// <summary>
        /// Тип урона, который уносит снаряд. Нужен клиентской презентации: визуальная копия снаряда
        /// летит с нулевым уроном, но штатный снаряд по прилёте всё равно обращается к типу урона.
        /// </summary>
        public DamageType ProjectileDamageType(int level)
        {
            FirstProjectileDamage(level, out DamageType damageType);
            return damageType;
        }

        float FirstProjectileDamage(int level, out DamageType damageType)
        {
            damageType = null;
            if (damage == null || !damage.enabled || damage.entries == null) return 0f;

            for (int i = 0; i < damage.entries.Length; i++)
            {
                SkillDamageEntry e = damage.entries[i];
                if (e == null || e.damageType == null) continue;

                float amount = LevelValue(e.amount, level);
                if (amount <= 0f) continue;

                damageType = e.damageType;
                return amount;
            }

            return 0f;
        }

        // ========================================================== СБРОС СОСТОЯНИЯ ==

        /// <summary>Ассет живёт между Play-сессиями — сбрасываем всё, что накопилось в рантайме.</summary>
        protected override void ResetRuntimeState()
        {
            cachedEffectorSet = null;
            effectorSetBuilt = false;
        }

        // ============================================================ ПРЕЗЕНТАЦИЯ ЦЕЛЕЙ ==

        /// <summary>
        /// На сколько растягивать визуал бафа. Ноль — аура не настроена, значит визуал остаётся
        /// авторского размера. Считается и на сервере, и на клиенте (по уровню из сообщения).
        /// </summary>
        public float BuffVfxScaleRadius(int level)
        {
            if (buff == null || !buff.enabled) return 0f;

            float aura = LevelValue(buff.auraRadius, level);
            bool hasAura = aura > 0f || LevelValue(buff.auraDamagePerSecond, level) > 0f;

            return hasAura ? aura : 0f;
        }

        /// <summary>
        /// Сервер: показать VFX длящегося бафа задетым целям. Хосту рисуем напрямую (сообщение
        /// до него не доходит), клиентам уходит ОДНО сообщение на весь каст. Значки и VFX
        /// ЭФФЕКТОРОВ с 2026-08-05 шлёт ядро в точке наложения (единый канал статусов) —
        /// здесь только баф. Геймплейное состояние клиенту не передаётся.
        /// </summary>
        void SendPresentation(List<Unit> targets, int level)
        {
            if (targets == null || targets.Count == 0) return;

            float buffDuration = (buff != null && buff.enabled) ? LevelValue(buff.duration, level) : 0f;
            bool hasBuffVfx = buff != null && buff.enabled && buff.buffVFX != null && buffDuration > 0f;

            if (!hasBuffVfx) return;

            float auraRadius = BuffVfxScaleRadius(level);

            List<UInt16> netIDs = new List<UInt16>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                Unit t = targets[i];
                if (t == null || t.dead) continue;

                // Хост своего же сообщения не получает, но эффекторы у него УЖЕ настоящие (их наложил
                // сервер), поэтому значок и VFX эффектора он видит штатно — дублировать нельзя.
                // Не хватает ему только визуала бафа: SkillBuff теперь чисто геймплейный и ничего не рисует.
                if (hasBuffVfx) SkillVisualStatus.ShowBuffVfx(t, id, buff.buffVFX, buffDuration, auraRadius);

                netIDs.Add(t.netID);
            }

            if (netIDs.Count == 0 || NetworkDataSync.instance == null) return;

            // [2026-08-05 единый канал статусов] Значки и VFX эффекторов скилл больше НЕ шлёт сам:
            // их отправляет ядро в момент наложения (Effector.EffectorAdd → UnitStatusEffectorSend) —
            // одинаково для атак, аур и скиллов (решение Artsiom 2026-08-05). Здесь остался только
            // VFX длящегося бафа — он не эффектор и в ядре точки наложения не имеет.
            if (hasBuffVfx) NetworkDataSync.instance.SkillBuffVfxSend(netIDs.ToArray(), id, level, buffDuration);
        }

        // ================================================================== СВОДКА ==

        /// <summary>
        /// Человекочитаемая строка «что делает этот скилл» — её показывает вкладка «Умения»
        /// над полями, чтобы геймдизайнер понимал скилл, не раскрывая все блоки.
        /// </summary>
        public string BuildSummary()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("Цель: ").Append(TargetModeText());
            if (PicksTargetByStrategy) sb.Append(" (").Append(StrategyText()).Append(')');
            if (maxTargets > 0) sb.Append(", не больше ").Append(maxTargets);
            if (buttonCast) sb.Append(" • по кнопке");

            if (delivery == SkillDelivery.Projectile) sb.Append(" • снарядом");
            if (spawnSocket != SkillSocketType.None) sb.Append(" из точки «").Append(SocketText()).Append('»');

            if (damage != null && damage.enabled && damage.entries != null && damage.entries.Length > 0)
            {
                sb.Append(" • урон");
                for (int i = 0; i < damage.entries.Length; i++)
                {
                    SkillDamageEntry e = damage.entries[i];
                    if (e == null) continue;
                    sb.Append(i == 0 ? " " : ", ").Append(LevelValue(e.amount, 0).ToString("0.#"));
                    if (e.damageType != null) sb.Append(' ').Append(e.damageType.name);
                }
            }

            if (status != null && status.enabled)
            {
                float stun = LevelValue(status.stunSeconds, 0);
                float disarm = LevelValue(status.disarmSeconds, 0);
                float mute = LevelValue(status.muteSeconds, 0);
                if (stun > 0f) sb.Append(" • оглушение ").Append(stun.ToString("0.#")).Append(" с");
                if (disarm > 0f) sb.Append(" • обезоруживание ").Append(disarm.ToString("0.#")).Append(" с");
                if (mute > 0f) sb.Append(" • немота ").Append(mute.ToString("0.#")).Append(" с");
            }

            if (heal != null && heal.enabled)
            {
                float flat = LevelValue(heal.flat, 0);
                float pct = LevelValue(heal.percentOfMaxHp, 0);
                if (flat > 0f) sb.Append(" • лечение ").Append(flat.ToString("0.#"));
                if (pct > 0f) sb.Append(" • лечение ").Append(pct.ToString("0.#")).Append("% макс. ХП");
            }

            if (effectors != null && effectors.enabled && effectors.records != null)
            {
                int liveRecords = 0;
                for (int i = 0; i < effectors.records.Length; i++)
                    if (effectors.records[i] != null && effectors.records[i].effector != null) liveRecords++;

                if (liveRecords > 0) sb.Append(" • эффекторов: ").Append(liveRecords);
            }

            if (buff != null && buff.enabled)
                sb.Append(" • баф ").Append(LevelValue(buff.duration, 0).ToString("0.#")).Append(" с");

            if (shield != null && shield.enabled) sb.Append(" • щит");
            if (blind != null && blind.enabled) sb.Append(" • ослепление");
            if (selfCost != null && selfCost.enabled) sb.Append(" • стоит здоровья кастеру");
            if (summon != null && summon.enabled) sb.Append(" • призыв");
            if (groundZone != null && groundZone.enabled) sb.Append(" • зона на земле");
            if (delegateService != null && delegateService.enabled && delegateService.service != SkillServerService.None)
                sb.Append(" • сервис: ").Append(ServiceText());

            if (statusEffector != null) sb.Append(" • значок: ").Append(statusEffector.name);

            return sb.ToString();
        }

        string TargetModeText()
        {
            switch (targetMode)
            {
                case SkillTargetMode.Self:           return "на себя";
                case SkillTargetMode.WholeTeam:      return "вся команда";
                case SkillTargetMode.AreaAroundSelf: return "область вокруг кастера";
                case SkillTargetMode.Cone:           return $"конус {coneAngle:0}°";
                case SkillTargetMode.SmartUnit:      return "юнит";
                case SkillTargetMode.SmartPoint:     return "точка";
            }
            return targetMode.ToString();
        }

        string StrategyText()
        {
            switch (targetStrategy)
            {
                case SkillTargetStrategy.Nearest:               return "ближайший";
                case SkillTargetStrategy.MostWounded:           return "самый раненый";
                case SkillTargetStrategy.WoundedBelowThreshold: return "раненый ниже порога";
                case SkillTargetStrategy.Strongest:             return "наибольший запас ХП";
                case SkillTargetStrategy.RandomOne:             return "случайный";
                case SkillTargetStrategy.Cluster:               return "скопление";
                case SkillTargetStrategy.CurrentAttackTarget:   return "текущая цель атаки";
            }
            return targetStrategy.ToString();
        }

        string SocketText()
        {
            switch (spawnSocket)
            {
                case SkillSocketType.RightHand: return "правая рука";
                case SkillSocketType.LeftHand:  return "левая рука";
                case SkillSocketType.Weapon:    return "оружие";
                case SkillSocketType.Chest:     return "грудь";
                case SkillSocketType.Center:    return "центр";
                case SkillSocketType.Head:      return "голова";
                case SkillSocketType.Ground:    return "под ногами";
            }
            return "центр объекта";
        }

        string ServiceText()
        {
            switch (delegateService.service)
            {
                case SkillServerService.MeteorStorm:         return "метеоритный дождь";
                case SkillServerService.ResurrectFromGraves: return "подъём павших";
            }
            return "нет";
        }
    }
}
