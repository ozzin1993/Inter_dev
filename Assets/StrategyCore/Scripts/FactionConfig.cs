using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============================= КОНФИГ ФРАКЦИИ (РАСЫ) ==
    // ScriptableObject — расоспецифичный КОНТЕНТ матча (волны/умения/техи/башни) данными (Inspector), не кодом.
    // Идентичность расы (имя + стартовые юниты UnitsForSpawn) живёт в GameManager.factionData (FactionData),
    // которое ссылается на этот конфиг полем config. MatchManager на старте берёт конфиг по ссылке
    // (factionData[playerFaction].config) и заполняет runtime-копию TeamWaveConfig.
    //
    // Сюда НЕ кладётся: имя/иконка (на FactionData), UnitsForSpawn (встроенный старт-спавн GameManager),
    // ссылки на объекты сцены (abilityCaster/spawnGrid/spawnPoint — остаются per-side в TeamWaveConfig).

    // ============================= АПГРЕЙДЫ КОНТЕНТА (техи / уровень ГЗ) ==
    // Данные правил «условие → изменения контента». Применяет сервер (MatchManager.RecomputeUnlockedContent,
    // идемпотентный пересчёт из источника). См. План_Апгрейды_контента_фракций.md.

    /// <summary>Условие разблокировки: теха И/ИЛИ уровень ГЗ (оба заданы → AND).</summary>
    [Serializable]
    public class UnlockRequirement
    {
        [Tooltip("Нужна разблокированная технология (Resources/Technology). Пусто — по техе не гейтить.")]
        public Technology tech;
        [Tooltip("Нужен уровень главного здания ≥ N. 0 — по уровню ГЗ не гейтить.")]
        public int mainBuildingLevel;
    }

    /// <summary>Подмена префаба юнита волны (будущие спавны).</summary>
    [Serializable]
    public class UnitSwap
    {
        [Tooltip("Базовый префаб (что заменяем).")]
        public Unit from;
        [Tooltip("Целевой префаб (на что).")]
        public Unit to;
    }

    /// <summary>Подмена префаба башни по типу точки (ретроактивно; зависит от pointKey — шаг 3б).</summary>
    [Serializable]
    public class TowerSwap
    {
        [Tooltip("Тип перестраиваемой точки — выбор из списка (как в rebuildablePoints).")]
        public PointKey pointKey = PointKey.None;
        [Tooltip("Целевой префаб башни.")]
        public Unit to;
    }

    /// <summary>Правило «условие → изменения контента». Заполнять только нужные эффекты, остальные пустыми.</summary>
    [Serializable]
    public class ContentUnlockRule
    {
        [Tooltip("КОГДА срабатывает: теха и/или уровень ГЗ (оба заданы → AND).")]
        public UnlockRequirement requirement;

        [Header("Доступные юниты волны")]
        [Tooltip("Открыть юнитов — добавить в доступные для волны.")]
        public Unit[] addAvailableUnits;
        [Tooltip("Убрать юнитов из доступных для волны.")]
        public Unit[] removeAvailableUnits;

        [Header("Подмена префабов")]
        [Tooltip("Подмена префабов юнитов волны (from→to), будущие спавны.")]
        public UnitSwap[] unitSwaps;
        [Tooltip("Подмена префабов башен по типу точки (ретроактивно). Зависит от pointKey (шаг 3б).")]
        public TowerSwap[] towerSwaps;

        [Header("Способности ГЗ (видимость в таблице)")]
        [Tooltip("Показать центр.способности в таблице ГЗ (они уже на кастере; правило — видимость).")]
        public Ability[] showCentralAbilities;
        [Tooltip("Скрыть центр.способности из таблицы ГЗ (для замены: старую скрыть, новую показать).")]
        public Ability[] hideCentralAbilities;
    }

    /// <summary>
    /// Модификатор стоимости найма (кирпич B28). Пока триггер-тех разблокирован у команды — цена юнитов
    /// фракции по указанному ресурсу умножается на (1 − percentReduction/100). Значение — данными (правило 3).
    /// Область — ВСЕ юниты фракции (вариант «по категории» — расширение N5). Проекция в единую точку цены
    /// (MatchManager.EffectiveResourceCost): UI и списание согласованы.
    /// </summary>
    [Serializable]
    public class CostModifierRule
    {
        [Tooltip("Технология-триггер (Resources/Technology). Пока разблокирована у команды — модификатор активен. Пусто — правило неактивно.")]
        public Technology triggerTech;
        [Tooltip("Ресурс, к цене которого применяется скидка (обычно «Золото»). Пусто — правило игнорируется.")]
        public Resource resource;
        [Range(0f, 100f)]
        [Tooltip("Скидка в процентах от базовой цены (25 = −25%). Несколько активных правил по одному ресурсу перемножаются.")]
        public float percentReduction = 0f;
    }

    /// <summary>Группа взаимоисключающих технологий (C3 «Без Союза»): купив ОДИН тех группы, остальные
    /// блокируются навсегда. Обёртка нужна, т.к. Unity не сериализует зубчатый Technology[][].</summary>
    [Serializable]
    public class TechGroup
    {
        [Tooltip("Технологии одной взаимоисключающей группы (например [Путь А, Путь Б]). Разблокировка одной " +
                 "навсегда блокирует остальные (проверка по штатному TechTree, без мутации). Лежат в Resources/Technology.")]
        public Technology[] techs;
    }

    /// <summary>Один узел дерева технологий (тиры): технология + иконка панели + цена.</summary>
    [Serializable]
    public class TechNode
    {
        [Tooltip("Технология, разблокируемая этим узлом (лежит в Resources/Technology). Пусто — узел ничего не разблокирует.")]
        public Technology technology;
        [Tooltip("Иконка узла для панели технологий (у Technology нет своего поля иконки).")]
        public Texture2D icon;
        [Tooltip("Цена узла (обычно золото). Сейчас пусто/0 — открытие бесплатное. Несколько ресурсов — несколько элементов массива.")]
        public ResourceWrapper[] cost;
    }

    /// <summary>Вариант «большого улучшения» тира: сам узел + две взаимоисключающие специализации + опционально префаб героя.</summary>
    [Serializable]
    public class TechBigOption
    {
        [Tooltip("Узел самого большого выбора (открывает юнита / умение / героя — эффект задаётся контентом через contentUnlockRules или heroPrefab).")]
        public TechNode node;
        [Tooltip("Специализация А выбранного варианта (подвыбор 1 из 2; выбирается сразу после большого выбора).")]
        public TechNode specializationA;
        [Tooltip("Специализация Б выбранного варианта (подвыбор 1 из 2). Невыбранная специализация блокируется навсегда в матче.")]
        public TechNode specializationB;
        [Tooltip("Если вариант открывает ГЕРОЯ — его префаб (Unit). При покупке варианта сервер пропишет его в heroUnlockTech/heroPrefab команды. Пусто — вариант не про героя.")]
        public Unit heroPrefab;
    }

    /// <summary>Один тир дерева технологий: ступень уровня + два больших варианта (А/Б), каждый со своими специализациями.</summary>
    [Serializable]
    public class TechTier
    {
        [Tooltip("Ступень 1 — «улучшение уровня технологий». Её покупка поднимает уровень ГЗ на 1 и открывает большие варианты этого тира.")]
        public TechNode levelUpgrade;
        [Tooltip("Ступень 2 — большой выбор, вариант А (+ его две специализации).")]
        public TechBigOption optionA;
        [Tooltip("Ступень 2 — большой выбор, вариант Б (+ его две специализации). Невыбранный вариант блокируется навсегда в матче.")]
        public TechBigOption optionB;
    }

    // ============================= ВОЛНА 2.0 — ЕДИНЫЙ СПИСОК ЮНИТОВ ==
    // Объединяет прежние basicWaveUnits/availableWaveUnits/waveUnitCounts в один список записей
    // {юнит; роль; count}. Роли взаимоисключающие (решение Artsiom 2026-07-24).

    /// <summary>Роль юнита в волне (Волна 2.0). Роли взаимоисключающие: юнит либо Базовый, либо Доступный.</summary>
    public enum WaveUnitRole
    {
        Basic,      // Базовый: выходит КАЖДУЮ волну всегда и бесплатно
        Available,  // Доступный: игрок помечает [Авто]/[Разовый] за золото; техи открывают/убирают поверх
    }

    /// <summary>Одна запись единого списка юнитов волны (Волна 2.0): тип + роль + число копий.
    /// Заменил прежние basicWaveUnits/availableWaveUnits/waveUnitCounts.</summary>
    [Serializable]
    public class WaveUnitEntry
    {
        [Tooltip("Тип юнита (префаб).")]
        public Unit unit;
        [Tooltip("Роль в волне: Базовый — выходит каждую волну бесплатно; Доступный — игрок помечает [Авто]/[Разовый] за золото.")]
        public WaveUnitRole role = WaveUnitRole.Available;
        [Min(1)]
        [Tooltip("Сколько копий этого типа выходит в волне. По умолчанию 1.")]
        public int count = 1;
    }

    [CreateAssetMenu(fileName = "FactionConfig", menuName = "StrategyCore/Faction Config")]
    public class FactionConfig : ScriptableObject
    {
        [Header("Состав волны (Волна 2.0)")]
        [Tooltip("Единый список юнитов волны: каждая запись — юнит + роль (Базовый/Доступный) + число копий. " +
                 "Роль Базовый — выходит каждую волну бесплатно; Доступный — игрок помечает [Авто]/[Разовый] за золото " +
                 "(технологии открывают доп. юнитов поверх через contentUnlockRules, у них по 1 копии).")]
        public WaveUnitEntry[] waveUnits;

        [Tooltip("Стартовый базовый доход золота за волну (начисляется в момент призыва волны). По ходу матча " +
                 "может меняться, но не ниже суммы автопризыва (гарантия в коде). Значение задаёт дизайнер.")]
        public int baseWaveIncome;

        [Header("Способности центральной таблицы")]
        [Tooltip("Способности центральной нижней таблицы расы по порядку. На старте MatchManager пропишет их " +
                 "в abilities[] объекта-кастера команды (отдельный невидимый юнит сцены, ссылка TeamWaveConfig.abilityCaster). " +
                 "Иконка — из Ability.icon[0]. Поддержаны Active-способности.")]
        public List<Ability> centralAbilities = new List<Ability>();

        [Header("Герой")]
        [Tooltip("Префаб героя расы (Unit). Призывается кнопкой вне волны; его abilities[] показываются в таблице умений героя. " +
                 "Умения открываются по уровню (Ability.requiredLevel); опыт/уровни — компонент LevelingUnit на префабе. " +
                 "MatchManager резолвит это поле в TeamWaveConfig на старте (как centralAbilities).")]
        public Unit heroPrefab;

        [Tooltip("Технология, открывающая призыв героя (лежит в Resources/Technology). Пока не разблокирована — " +
                 "призыв героя отклоняется сервером. Пусто — герой доступен без гейта по теху. " +
                 "Разблокируется узлом дерева ГЗ «Открытие Героя».")]
        public Technology heroUnlockTech;

        [Header("Технологии / облик ГЗ")]
        [Tooltip("Дерево технологий (тиры). Каждый тир = улучшение уровня → большой выбор 1 из 2 → специализация 1 из 2 " +
                 "(невыбранные альтернативы блокируются навсегда в матче). Длина массива = число тиров (сейчас 5), меняется " +
                 "контентом без кода. Тир N+1 доступен только когда тир N завершён (куплены уровень, вариант и его специализация).")]
        public TechTier[] techTiers;

        [Tooltip("Облик главного здания по уровням ГЗ (Unit-префаб на уровень; index 0 = уровень 1). При апгрейде ГЗ " +
                 "замок меняет визуал на префаб достигнутого уровня штатным ReplaceRenderers (тот же объект, без " +
                 "пересоздания). Пустой элемент/пусто — уровень без смены облика (остаётся текущий). Стартовый облик — " +
                 "базовый префаб замка. Меняется только визуал; статы — отдельно (mainBuildingStatsByLevel).")]
        public Unit[] mainBuildingShapesByLevel;

        [Header("Башни по типу точки")]
        [Tooltip("Башня для ЦЕНТРАЛЬНОЙ точки (pointKey \"Centre\"). Null → точка захватывается без отстройки.")]
        public Unit centreTower;
        [Tooltip("Башня для ЗАЩИТНОЙ точки 1 (pointKey \"Defence1\"). Null → не строить.")]
        public Unit defence1Tower;
        [Tooltip("Башня для ЗАЩИТНОЙ точки 2 (pointKey \"Defence2\"). Null → не строить.")]
        public Unit defence2Tower;

        [Header("Апгрейды контента (техи / уровень ГЗ)")]
        [Tooltip("Правила: при выполнении условия — открыть/убрать доступных юнитов, подменить префабы юнитов/башен, " +
                 "показать/скрыть способности ГЗ. Пересчёт серверный, идемпотентный (RecomputeUnlockedContent).")]
        public ContentUnlockRule[] contentUnlockRules;

        [Header("Модификаторы стоимости найма (B28)")]
        [Tooltip("Скидки на стоимость найма юнитов фракции по разблокированным техам (кирпич B28). " +
                 "Пример: узел «Мертвым не нужны деньги» → −25% золота на всех юнитов Нежити. " +
                 "Значения — данными, без хардкода; активны, пока триггер-тех разблокирован (как Required Tech). " +
                 "Проекция в единую точку цены — MatchManager.EffectiveResourceCost.")]
        public CostModifierRule[] costModifiers;

        [Header("Могилки")]
        [Tooltip("Префаб могилки этой расы (обычный префаб, НЕ Unit). Спавнится в точке смерти юнита с leavesGrave=ВКЛ. " +
                 "Null → могилка не создаётся. Не должен карвить навмеш / блокировать путь.")]
        public GameObject gravePrefab;
        [Tooltip("Время жизни могилки, сек. По истечении могилка исчезает. Значение — placeholder, задать в Inspector.")]
        public float graveLifetime = 30f;

        [Header("Ресурс «Души» (только Нежить)")]
        [Tooltip("Ресурс «Души» этой расы (Resources/Resource/Души.asset). У Людей/Орков/Союза — ПУСТО: " +
                 "система душ неактивна (генерации/начисления нет). Заполнено только у Нежити. Должен быть " +
                 "зарегистрирован в GameResources.gameResources[] сцены, иначе начисление — no-op. Кап банка — в самом ассете (maxLimit).")]
        public Resource soulsResource;

        [Tooltip("Пассивная генерация: душ В МИНУТУ по уровню ГЗ (индекс 0 = уровень 1). По дизайн-доку 15/20/30/40/60. " +
                 "Сервер начисляет 1 душу раз в 60/скорость секунд (ур.1 → 1 душа в 4 c). Значение на уровне 0 → генерации нет. " +
                 "Действует только если задан soulsResource. [БАЛАНС — Влад]")]
        public float[] soulsPerMinuteByMbLevel = new float[] { 15f, 20f, 30f, 40f, 60f };

        [Tooltip("Души за убийство вражеского юнита по его тиру (Unit.tier; индекс 0 = тир 1). По дизайн-доку 1/2/4/8/15. " +
                 "Тир вне диапазона клампится к границам массива. Действует только если задан soulsResource. [БАЛАНС — Влад]")]
        public int[] soulsPerTier = new int[] { 1, 2, 4, 8, 15 };

        [Header("Ветки Душ (Нежить) — N4")]
        [Tooltip("Ветки развития Нежити за Души: 5 веток × 3 тира × выбор опции А/Б. Покупка узла разблокирует его " +
                 "Technology штатным TechnologyManager.UnlockTech. Серверные правила (MatchManager.BranchTechs): " +
                 "лимит занятых веток (soulBranchLimit), взаимоисключение А/Б внутри тира, тир N+1 требует купленный " +
                 "тир N той же ветки. У не-Нежити — ПУСТО: система веток неактивна. Заполняется в Faction_Nezhit.")]
        public SoulBranch[] soulBranches;

        [Tooltip("Максимум веток Душ, в которых команда может купить хотя бы один узел за матч (дизайн: «2 ветки из 5»). " +
                 "Первая покупка в ветке «занимает» её; при достигнутом лимите узлы в НОВЫХ (ещё не занятых) ветках " +
                 "блокируются, а уже занятые остаются доступны. 0 — без лимита. Действует только если задан soulBranches.")]
        public int soulBranchLimit = 2;

        [Header("Территория Скверны (Нежить, B30) — N4")]
        [Tooltip("Стартовый радиус зоны Скверны вокруг каждого источника (точки/ГЗ во владении Нежити), мировые единицы. " +
                 "0 — зона стартует нулевой (проявится по мере роста). Активна только у Нежити (есть soulsResource). [БАЛАНС — Влад]")]
        public float skvernaStartRadius = 0f;

        [Tooltip("Прирост радиуса Скверны в секунду (сервер, тик). 0 — радиус не растёт. Множитель «Быстрая Гниль» (эффект N5) " +
                 "умножает этот прирост. Активна только у Нежити. [БАЛАНС — Влад]")]
        public float skvernaGrowthPerSecond = 0f;

        [Tooltip("Кап радиуса Скверны на источник (макс.), мировые единицы. 0 — без капа (растёт неограниченно). " +
                 "Активна только у Нежити. [БАЛАНС — Влад]")]
        public float skvernaMaxRadius = 0f;

        [Header("Взаимоисключение техов (C3 «Без Союза»)")]
        [Tooltip("Группы взаимоисключающих технологий (пути А/Б). В каждой группе игрок может разблокировать " +
                 "ТОЛЬКО ОДИН тех — остальные блокируются навсегда. Пример: [[Путь А, Путь Б]]. Пусто — механика неактивна.")]
        public TechGroup[] exclusiveTechGroups;

        [Tooltip("Технологии «ветки синергии Союза», блокируемые при выборе пути (как только разблокирован любой " +
                 "тех из exclusiveTechGroups). Список — в Inspector; дефолт-предложение A5 (Решения_Ждут.md) не подтверждён Владом. Пусто — неактивно.")]
        public Technology[] lockedBySouzlessTechs;
    }
}
