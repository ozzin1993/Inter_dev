using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============================= INTERFLOW EDITOR — ВАЛИДАТОР (фаза E1, шаги 2–3) ==
    // Все структурные проверки контента в ОДНОМ файле (правило 5). Источник каждого правила —
    // гайд/код проекта (указан в поле source и в комментарии). Числовых порогов не вводим (правило 7):
    // только структура и ссылки. Ничего не меняет — только читает и показывает.
    // Шаг 2: блоки «Юниты», «Умения». Шаг 3: «Фракции», «Матч/сцена», «Прочее».
    // Блоки контент-ассетов (Юниты/Умения/Фракции/Прочее) читают AssetDatabase. Блок «Матч/сцена»
    // читает ОБЪЕКТЫ ОТКРЫТОЙ сцены (FindObjectOfType + штатный SerializedObject для приватных
    // [SerializeField]) — если сцена матча не открыта, блок сообщает об этом и пропускается (бинарная сцена, §10 плана).

    /// <summary>Серьёзность найденной проблемы.</summary>
    public enum InterflowIssueSeverity { Error, Warning, Info }

    /// <summary>Одна находка валидатора: серьёзность, русское сообщение, источник правила, объект для перехода.</summary>
    public class InterflowIssue
    {
        public InterflowIssueSeverity severity;
        public string message;          // сообщение по-русски
        public string source;           // источник правила (гайд/файл кода) — показывается в тултипе строки
        public Object target;           // объект для Ping/выделения (может быть null)

        public InterflowIssue(InterflowIssueSeverity severity, string message, string source, Object target)
        {
            this.severity = severity; this.message = message; this.source = source; this.target = target;
        }
    }

    public static class InterflowValidator
    {
        // Кэш последнего прогона — чтобы список не пропадал при переключении вкладок окна.
        static List<InterflowIssue> lastRun;

        // ======================== ЗАПУСК ВСЕХ ПРОВЕРОК ========================

        public static List<InterflowIssue> RunAll()
        {
            var issues = new List<InterflowIssue>();

            // Собранные один раз данные передаются в блоки (не сканировать проект повторно).
            var units = LoadAllUnitPrefabs();
            var factions = LoadAllFactions();

            ValidateUnits(issues, units, factions);
            ValidateAbilities(issues, units, factions);
            ValidateFactions(issues, factions);
            ValidateMatchScene(issues, factions);
            ValidateMisc(issues);

            lastRun = issues;
            return issues;
        }

        // ======================== СБОР ДАННЫХ ========================

        /// <summary>Все префабы проекта с компонентом Unit (паттерн SCEditor.LoadUnits) + их пути.</summary>
        static List<(Unit unit, string path)> LoadAllUnitPrefabs()
        {
            var result = new List<(Unit, string)>();
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var unit = go.GetComponent<Unit>();
                if (unit != null) result.Add((unit, path));
            }
            return result;
        }

        static List<FactionConfig> LoadAllFactions()
        {
            var result = new List<FactionConfig>();
            foreach (string guid in AssetDatabase.FindAssets("t:FactionConfig"))
            {
                var cfg = AssetDatabase.LoadAssetAtPath<FactionConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (cfg != null) result.Add(cfg);
            }
            return result;
        }

        // Лежит ли ассет в какой-либо папке Resources/<подпапка>/... (так грузит Resources.LoadAll).
        static bool InResourcesSubfolder(string assetPath, string subfolder)
        {
            return assetPath.Replace('\\', '/').Contains("/Resources/" + subfolder + "/");
        }

        /// <summary>Юниты, на которые ссылается контент фракций (обязаны лежать в Resources/UnitPrefabs).</summary>
        static HashSet<Unit> CollectFactionUnits(List<FactionConfig> factions, HashSet<Unit> waveOnly = null)
        {
            var all = new HashSet<Unit>();
            void Add(Unit u, bool isWave = false)
            {
                if (u == null) return;
                all.Add(u);
                if (isWave) waveOnly?.Add(u);
            }

            foreach (var f in factions)
            {
                if (f.waveComposition != null) foreach (var w in f.waveComposition) Add(w?.unitToSpawn, true);
                if (f.availableWaveUnits != null) foreach (var u in f.availableWaveUnits) Add(u, true);
                Add(f.centreTower); Add(f.defence1Tower); Add(f.defence2Tower);
                Add(f.heroPrefab);
                if (f.contentUnlockRules != null)
                    foreach (var r in f.contentUnlockRules)
                    {
                        if (r == null) continue;
                        if (r.addAvailableUnits != null) foreach (var u in r.addAvailableUnits) Add(u, true);
                        if (r.removeAvailableUnits != null) foreach (var u in r.removeAvailableUnits) Add(u, true);
                        if (r.unitSwaps != null) foreach (var s in r.unitSwaps) { Add(s?.from, true); Add(s?.to, true); }
                        if (r.towerSwaps != null) foreach (var s in r.towerSwaps) Add(s?.to);
                    }
            }
            return all;
        }

        // ======================== БЛОК «ЮНИТЫ» (шаг 2) ========================

        static void ValidateUnits(List<InterflowIssue> issues, List<(Unit unit, string path)> units, List<FactionConfig> factions)
        {
            var waveUnits = new HashSet<Unit>();
            var factionUnits = CollectFactionUnits(factions, waveUnits);

            // Юниты в Resources/UnitPrefabs — участвуют в LoadAll и в проверке уникальности ID.
            var inResources = units.Where(u => InResourcesSubfolder(u.path, "UnitPrefabs")).ToList();

            // 1. Юнит из контента фракции обязан лежать в Resources/UnitPrefabs (GameManager.cs:244 LoadAll).
            foreach (var (unit, path) in units)
            {
                bool inRes = InResourcesSubfolder(path, "UnitPrefabs");
                if (factionUnits.Contains(unit) && !inRes)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"Юнит «{unit.name}» используется фракцией, но лежит ВНЕ Resources/UnitPrefabs — игра его не загрузит ({path}).",
                        "Гайд 04 шаг 1; GameManager.cs:244 (Resources.LoadAll)", unit));
                else if (!inRes)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Info,
                        $"Префаб с Unit вне Resources/UnitPrefabs: «{unit.name}» ({path}) — в игру не загрузится (ок, если это не игровой юнит).",
                        "Гайд 04 шаг 1", unit));
            }

            // 2. unitTypeID: ≠ 0 и уникален среди Resources/UnitPrefabs
            //    (GameManager.cs:248 кладёт в словарь по ID и молча пропускает дубль).
            var byId = new Dictionary<int, List<(Unit unit, string path)>>();
            foreach (var e in inResources)
            {
                if (!byId.TryGetValue(e.unit.unitTypeID, out var list)) byId[e.unit.unitTypeID] = list = new List<(Unit, string)>();
                list.Add(e);
            }
            foreach (var pair in byId)
            {
                if (pair.Key == 0)
                    foreach (var (unit, _) in pair.Value)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"unitTypeID = 0 у юнита «{unit.name}» — ID не назначен (обычно чинится реимпортом: UnitDuplicateHandler).",
                            "UnitDuplicateHandler.cs; GameManager.cs:248", unit));
                else if (pair.Value.Count > 1)
                    foreach (var (unit, path) in pair.Value)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"Дубль unitTypeID={pair.Key}: «{unit.name}» ({path}) — GameManager молча пропустит одного из юнитов (спавн по сети сломается).",
                            "GameManager.cs:248 (ContainsKey → skip)", unit));
            }

            // 3. Корень префаба: без Renderer и без вращения (визуал — на дочернем объекте).
            foreach (var (unit, _) in units)
            {
                if (unit.GetComponent<Renderer>() != null)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"У корня юнита «{unit.name}» есть Renderer — визуал должен быть на дочернем объекте (ломает ориентацию/попадания).",
                        "Гайд 04 шаг 1; Readme «Создание юнита»", unit));
                if (unit.transform.rotation != Quaternion.identity)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"Корень юнита «{unit.name}» повёрнут (rotation ≠ identity) — корень должен быть без вращения.",
                        "Readme «Создание юнита»", unit));
            }

            // 4. Цены юнитов волны: resourceCost пустой → экономика волн не сможет ограничивать состав.
            foreach (var unit in waveUnits)
            {
                if (unit == null) continue;
                if (unit.resourceCost == null || unit.resourceCost.Length == 0)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"У юнита волны «{unit.name}» не задан resourceCost (цена/лидерство) — юнит бесплатный и не занимает кап.",
                        "Гайд 02 шаг 3 (единый источник цен)", unit));
            }

            // 5–6. Несовместимости атаки (гайд 04 шаг 3).
            foreach (var (unit, _) in units)
            {
                bool bounce = unit.bounceCount != 0;
                if (unit.melee && (unit.multiTarget || bounce))
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"«{unit.name}»: MultiTarget/Bounce включены при melee — эти модификации только для дальнего боя.",
                        "Гайд 04 шаг 3", unit));
                if (unit.attackType == AttackType.Continuous && unit.multiTarget && bounce)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"«{unit.name}»: MultiTarget и Bounce одновременно при Continuous-атаке несовместимы — оставь что-то одно.",
                        "Гайд 04 шаг 3 (⚠ несовместимость)", unit));
            }
        }

        // ======================== БЛОК «УМЕНИЯ» (шаг 2) ========================

        static void ValidateAbilities(List<InterflowIssue> issues, List<(Unit unit, string path)> units, List<FactionConfig> factions)
        {
            // Все Ability проекта (t: находит и наследников — наши кастомные классы тоже).
            var abilities = new List<(Ability ability, string path)>();
            foreach (string guid in AssetDatabase.FindAssets("t:Ability"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var a = AssetDatabase.LoadAssetAtPath<Ability>(path);
                if (a != null) abilities.Add((a, path));
            }

            // 1. Ability вне Resources/Ability — GameManager не загрузит (gameAbilities).
            foreach (var (a, path) in abilities)
                if (!InResourcesSubfolder(path, "Ability"))
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"Умение «{a.name}» вне Resources/Ability ({path}) — не попадёт в gameAbilities (каст по id не найдёт его).",
                        "Гайд 03; GameManager.cs:208", a));

            // 2. Дубль Ability.id среди Resources/Ability = ArgumentException на старте игры
            //    (GameManager.cs:211 — Dictionary.Add без проверки ключа).
            var byId = new Dictionary<int, List<Ability>>();
            foreach (var (a, path) in abilities.Where(x => InResourcesSubfolder(x.path, "Ability")))
            {
                if (!byId.TryGetValue(a.id, out var list)) byId[a.id] = list = new List<Ability>();
                list.Add(a);
            }
            foreach (var pair in byId.Where(p => p.Value.Count > 1))
                foreach (var a in pair.Value)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"Дубль Ability.id={pair.Key}: «{a.name}» — игра УПАДЁТ на старте (Dictionary.Add без проверки).",
                        "GameManager.cs:211", a));

            // 3. Иконка UI-умений: centralAbilities фракций и умения героя рисуются из icon[0].
            var uiAbilities = new HashSet<Ability>();
            foreach (var f in factions)
            {
                if (f.centralAbilities != null) foreach (var a in f.centralAbilities) if (a != null) uiAbilities.Add(a);
                if (f.heroPrefab != null && f.heroPrefab.abilities != null)
                    foreach (var a in f.heroPrefab.abilities) if (a != null) uiAbilities.Add(a);
            }
            foreach (var a in uiAbilities)
            {
                if (a.icon == null || a.icon.Length == 0 || a.icon[0] == null)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"У UI-умения «{a.name}» не задана иконка icon[0] — в таблице будет пустая ячейка.",
                        "Гайд 03 часть 1 (иконка из icon[0])", a));

                // 4. Длительность 0 у умения из таблицы: если умение длящееся — оно молча «не работает»
                //    (исторический кейс FlameCloak_active). Info: мгновенным активкам длительность не нужна.
                if (a.duration == null || a.duration.Length == 0 || a.duration.All(d => d <= 0f))
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Info,
                        $"У UI-умения «{a.name}» duration = 0/пусто — если умение длящееся, задай длительность (иначе эффект не применится).",
                        "Гайд 03 часть 2 (кейс FlameCloak_active)", a));

                // 6. Согласованность уровней: requiredLevel[] по уровням против maxLevels.
                if (a.maxLevels > 0 && a.requiredLevel != null && a.requiredLevel.Length > 0 && a.requiredLevel.Length != a.maxLevels)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"У умения «{a.name}» длина requiredLevel[] ({a.requiredLevel.Length}) ≠ maxLevels ({a.maxLevels}).",
                        "Гайд 06 шаг 1 (уровни умений героя)", a));
            }

            // 5. AutoAbilityUser: autoAbility задан и продублирован в abilities[] юнита (обязательное правило ассета).
            //    Поле приватное [SerializeField] — читаем ШТАТНЫМ SerializedObject (не reflection-хак; правило 9 соблюдено).
            foreach (var (unit, _) in units)
            {
                var auto = unit.GetComponent<AutoAbilityUser>();
                if (auto == null) continue;

                var so = new SerializedObject(auto);
                var ability = so.FindProperty("autoAbility")?.objectReferenceValue as Ability;

                if (ability == null)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"На юните «{unit.name}» висит AutoAbilityUser без autoAbility — компонент ничего не делает.",
                        "Гайд 03 часть 3", unit));
                else if (unit.abilities == null || !unit.abilities.Contains(ability))
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"У юнита «{unit.name}» autoAbility «{ability.name}» НЕ добавлена в список Abilities юнита — авто-каст не сработает.",
                        "Гайд 03 часть 3 (двойная запись обязательна)", unit));
            }
        }

        // ======================== БЛОК «ФРАКЦИИ» (шаг 3) ========================

        static void ValidateFactions(List<InterflowIssue> issues, List<FactionConfig> factions)
        {
            foreach (var f in factions)
            {
                // 1. Башни точек: null допустим осознанно (центр без отстройки / защита не строится) → предупреждение.
                if (f.centreTower == null)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"У фракции «{f.name}» не задана centreTower — центральная точка будет захватываться без отстройки (если так задумано — игнорируй).",
                        "FactionConfig.centreTower (тултип)", f));
                if (f.defence1Tower == null)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"У фракции «{f.name}» не задана defence1Tower — защитная точка 1 не будет строиться.",
                        "FactionConfig.defence1Tower (тултип)", f));
                if (f.defence2Tower == null)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"У фракции «{f.name}» не задана defence2Tower — защитная точка 2 не будет строиться.",
                        "FactionConfig.defence2Tower (тултип)", f));

                // 2. show/hideCentralAbilities правил контента ⊆ centralAbilities (нельзя показать/скрыть то, чего нет в таблице).
                var central = new HashSet<Ability>();
                if (f.centralAbilities != null) foreach (var a in f.centralAbilities) if (a != null) central.Add(a);
                if (f.contentUnlockRules != null)
                    foreach (var r in f.contentUnlockRules)
                    {
                        if (r == null) continue;
                        void CheckSubset(Ability[] arr)
                        {
                            if (arr == null) return;
                            foreach (var a in arr)
                                if (a != null && !central.Contains(a))
                                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                                        $"Фракция «{f.name}»: правило контента ссылается на способность «{a.name}», которой нет в centralAbilities — показывать/скрывать нечего.",
                                        "FactionConfig.ContentUnlockRule.show/hideCentralAbilities", f));
                        }
                        CheckSubset(r.showCentralAbilities);
                        CheckSubset(r.hideCentralAbilities);
                    }

                // 3. Техи веток: собрать множество техов из techBranches и проверить «дыры».
                var branchTechs = new HashSet<Technology>();
                if (f.techBranches != null)
                    foreach (var br in f.techBranches)
                    {
                        if (br?.levels == null) continue;
                        foreach (var cell in br.levels)
                            if (cell != null && cell.technology != null) branchTechs.Add(cell.technology);

                        // Дыра: заполненный уровень выше пустого недостижим (гейт: предыдущий уровень должен быть разблокирован).
                        bool emptyAbove = false;
                        for (int i = 0; i < br.levels.Length; i++)
                        {
                            bool empty = br.levels[i] == null || br.levels[i].technology == null;
                            if (empty) { emptyAbove = true; continue; }
                            if (emptyAbove)
                            {
                                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                                    $"Фракция «{f.name}»: в ветке техов уровень {i + 1} заполнен, но выше есть пустой — узел недостижим (ветка обрывается до него).",
                                    "MatchManager.TechUpgrade (гейт по предыдущему уровню ветки)", f));
                                break;
                            }
                        }
                    }

                // 4. mainBuildingLevelTechs: не должны входить в ветки и обязаны лежать в Resources/Technology (пустой = уровень без техи, ок).
                if (f.mainBuildingLevelTechs != null)
                    foreach (var t in f.mainBuildingLevelTechs)
                    {
                        if (t == null) continue;
                        if (branchTechs.Contains(t))
                            issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                                $"Фракция «{f.name}»: теха уровня ГЗ «{t.name}» также присутствует в techBranches — убери её из веток (эти техи скрыты из угловой таблицы).",
                                "FactionConfig.mainBuildingLevelTechs (тултип: НЕ добавлять в ветки)", t));
                        if (!InResourcesSubfolder(AssetDatabase.GetAssetPath(t), "Technology"))
                            issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                                $"Фракция «{f.name}»: теха уровня ГЗ «{t.name}» вне Resources/Technology — TechnologyManager её не найдёт.",
                                "FactionConfig.mainBuildingLevelTechs (тултип: лежать в Resources/Technology)", t));
                    }

                // 5. Стоимости улучшения ГЗ заданы (структурно). Точная длина ↔ число уровней — вынесено (правило 7, см. лог/диф).
                if (f.mainBuildingUpgradeCosts == null || f.mainBuildingUpgradeCosts.Length == 0)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"У фракции «{f.name}» не заданы mainBuildingUpgradeCosts — улучшение ГЗ будет без стоимости.",
                        "FactionConfig.mainBuildingUpgradeCosts (тултип)", f));

                // 6. Герой несёт LevelingUnit (иначе уровни/умения героя по уровню не работают). maxLevel ≥ порога — в блоке «Матч/сцена».
                if (f.heroPrefab != null && f.heroPrefab.GetComponent<LevelingUnit>() == null)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"У героя фракции «{f.name}» нет компонента LevelingUnit — опыт/уровни и умения по уровню работать не будут.",
                        "Гайд 06; FactionConfig.heroPrefab (тултип: уровни через LevelingUnit)", f.heroPrefab));
            }
        }

        // ======================== БЛОК «МАТЧ / СЦЕНА» (шаг 3) ========================
        // Читает объекты ОТКРЫТОЙ сцены. Приватные [SerializeField] — через штатный SerializedObject (правило 9).

        static void ValidateMatchScene(List<InterflowIssue> issues, List<FactionConfig> factions)
        {
            var mm = Object.FindObjectOfType<MatchManager>();
            if (mm == null)
            {
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Info,
                    "Проверки матча/сцены пропущены: в открытой сцене нет MatchManager. Открой сцену матча и запусти повторно (сцена бинарная — статикой не читается).",
                    "План §10 (бинарная сцена)", null));
                return;
            }

            var so = new SerializedObject(mm);

            // 1. Lane назначена; порядок Lane.points ↔ laneIndex (массив задаёт таргетинг).
            var lane = so.FindProperty("lane")?.objectReferenceValue as Lane;
            if (lane == null)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    "MatchManager.lane не назначена — волны и команды без цели (волна пропускается).",
                    "MatchManager.ValidateSetup; MatchManager.lane", mm));
            else
            {
                var pts = new SerializedObject(lane).FindProperty("points");
                if (pts != null && pts.isArray)
                {
                    int prev = int.MinValue;
                    var seen = new HashSet<int>();
                    for (int i = 0; i < pts.arraySize; i++)
                    {
                        var poi = pts.GetArrayElementAtIndex(i).objectReferenceValue as PointOfInterest;
                        if (poi == null)
                        {
                            issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                                $"Lane.points[{i}] пуст — все точки линии должны быть назначены.",
                                "Lane.points (тултип: все элементы назначены)", lane));
                            continue;
                        }
                        if (!seen.Add(poi.laneIndex))
                            issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                                $"Точка «{poi.name}»: дубль laneIndex={poi.laneIndex} — индексы должны быть уникальны в пределах линии.",
                                "PointOfInterest.laneIndex (тултип: уникальны в Lane)", poi));
                        if (poi.laneIndex < prev)
                            issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                                $"Порядок Lane.points не по возрастанию laneIndex у «{poi.name}» (laneIndex={poi.laneIndex} после {prev}) — порядок массива задаёт таргетинг.",
                                "Lane.points ↔ PointOfInterest.laneIndex", poi));
                        prev = poi.laneIndex;
                    }
                }
            }

            // 2. rebuildablePoints: pointKey задан, не замок, rebuildDelay > 0, rebuildOnCapture только у центра.
            var rp = so.FindProperty("rebuildablePoints");
            if (rp != null && rp.isArray)
                for (int i = 0; i < rp.arraySize; i++)
                {
                    var el = rp.GetArrayElementAtIndex(i);
                    var poi = el.FindPropertyRelative("point")?.objectReferenceValue as PointOfInterest;
                    int keyIdx = el.FindPropertyRelative("pointKey")?.enumValueIndex ?? 0;   // 0 = None
                    bool rebuildOnCapture = el.FindPropertyRelative("rebuildOnCapture")?.boolValue ?? false;
                    float delay = el.FindPropertyRelative("rebuildDelay")?.floatValue ?? 0f;
                    Object tgt = poi != null ? (Object)poi : mm;

                    if (poi == null)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"rebuildablePoints[{i}]: точка (point) не назначена — элемент ничего не строит.",
                            "MatchManager.ValidateSetup", mm));
                    else if (poi.type == PointOfInterest.PointType.Castle)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"rebuildablePoints[{i}]: точка «{poi.name}» — замок (Castle). Замки не перестраиваются, убери из списка.",
                            "MatchManager.ValidateSetup (замки не добавлять)", poi));

                    if (keyIdx == 0)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"rebuildablePoints[{i}] ({(poi != null ? poi.name : "?")}): pointKey не задан (None) — башня строиться не будет (резолв префаба по ключу невозможен).",
                            "MatchManager.ValidateSetup; PointKey.None", tgt));
                    if (delay <= 0f)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"rebuildablePoints[{i}] ({(poi != null ? poi.name : "?")}): rebuildDelay ≤ 0 — тело башни может не успеть уйти до отстройки.",
                            "MatchManager.PointTowerConfig.rebuildDelay (тултип: > 0)", tgt));
                    if (rebuildOnCapture && keyIdx != 1)   // 1 = Centre
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"rebuildablePoints[{i}] ({(poi != null ? poi.name : "?")}): rebuildOnCapture включён не для центральной точки — по канону только центр отстраивается после захвата.",
                            "Гайд 05; PointTowerConfig.rebuildOnCapture (тултип: только центр)", tgt));
                }

            // 3. Ссылки на ресурсы экономики волн (публичные свойства — правило 9).
            if (mm.GoldResource == null)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    "MatchManager.goldResource не назначен — списание золота за волну будет пропущено.",
                    "MatchManager.ValidateSetup; MatchManager.GoldResource", mm));
            if (mm.LeadershipResource == null)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    "MatchManager.leadershipResource не назначен — проверки капа лидерства будут пропущены.",
                    "MatchManager.ValidateSetup; MatchManager.LeadershipResource", mm));

            // 4. Соглашение проекта: лимит Лидерства в GameResources = пул волны × 3. ЕДИНСТВЕННАЯ проверка с числом (×3 из тултипа) — правило 7, подтвердить/убрать.
            var gr = Object.FindObjectOfType<GameResources>();
            var lead = mm.LeadershipResource;
            if (gr != null && gr.gameResources != null && lead != null)
            {
                int pool = so.FindProperty("waveLeadershipPool")?.intValue ?? 0;
                foreach (var w in gr.gameResources)
                {
                    if (w == null || w.type != lead) continue;
                    if (pool > 0 && w.value != pool * 3)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"Лимит Лидерства в GameResources ({w.value}) ≠ пул волны×3 ({pool * 3}) — соглашение проекта (тултип leadershipResource).",
                            "MatchManager.leadershipResource/waveLeadershipPool (тултип: пул×3)", gr));
                    break;
                }
            }

            // 5. commandGroup кнопок slotPanels в диапазоне MatchManager.commandGroups.
            int groupCount = 1;
            var cg = so.FindProperty("commandGroups");
            if (cg != null && cg.isArray && cg.arraySize > 0) groupCount = cg.arraySize;
            var ui = Object.FindObjectOfType<UIManager>();
            if (ui != null)
            {
                var panels = new SerializedObject(ui).FindProperty("slotPanels");
                if (panels != null && panels.isArray)
                    for (int i = 0; i < panels.arraySize; i++)
                    {
                        var slots = panels.GetArrayElementAtIndex(i).FindPropertyRelative("slots");
                        if (slots == null || !slots.isArray) continue;
                        for (int j = 0; j < slots.arraySize; j++)
                        {
                            int gv = slots.GetArrayElementAtIndex(j).FindPropertyRelative("commandGroup")?.intValue ?? 0;
                            if (gv < 0 || gv >= groupCount)
                                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                                    $"slotPanels[{i}].slots[{j}]: commandGroup={gv} вне диапазона рядов [0..{groupCount - 1}] (MatchManager.commandGroups).",
                                    "UIManager.slotPanels ↔ MatchManager.commandGroups", ui));
                        }
                    }
            }

            // 6. Герой: LevelingUnit.maxLevel ≥ макс. порога уровня ГЗ (heroLevelFloorByMainBuildingLevel на MatchManager).
            int maxFloor = 0;
            var floor = so.FindProperty("heroLevelFloorByMainBuildingLevel");
            if (floor != null && floor.isArray)
                for (int i = 0; i < floor.arraySize; i++)
                    maxFloor = Mathf.Max(maxFloor, floor.GetArrayElementAtIndex(i).intValue);
            if (maxFloor > 0)
                foreach (var f in factions)
                {
                    if (f?.heroPrefab == null) continue;
                    var lu = f.heroPrefab.GetComponent<LevelingUnit>();
                    if (lu != null && lu.maxLevel < maxFloor)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"Герой фракции «{f.name}»: LevelingUnit.maxLevel={lu.maxLevel} < макс. порога уровня ГЗ ({maxFloor}) — герой не дорастёт до пола уровня.",
                            "MatchManager.Hero.heroLevelFloorByMainBuildingLevel (тултип)", f.heroPrefab));
                }
        }

        // ======================== БЛОК «ПРОЧЕЕ» (шаг 3) ========================

        static void ValidateMisc(List<InterflowIssue> issues)
        {
            // 1. Technology.id уникален среди Resources/Technology (гейтинг/разблокировка по id).
            var techById = new Dictionary<int, List<Technology>>();
            foreach (string guid in AssetDatabase.FindAssets("t:Technology"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!InResourcesSubfolder(path, "Technology")) continue;
                var t = AssetDatabase.LoadAssetAtPath<Technology>(path);
                if (t == null) continue;
                if (!techById.TryGetValue(t.id, out var list)) techById[t.id] = list = new List<Technology>();
                list.Add(t);
            }
            foreach (var pair in techById.Where(p => p.Value.Count > 1))
                foreach (var t in pair.Value)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"Дубль Technology.id={pair.Key}: «{t.name}» — разблокировка/гейтинг техов станет неоднозначным.",
                        "Technology.id; TechnologyManager", t));

            // 2. Дубль ассета DefaultNetworkPrefabs (к NetworkManager подключён один; второй — кандидат на удаление, §3.4).
            var dnp = AssetDatabase.FindAssets("DefaultNetworkPrefabs");
            if (dnp.Length > 1)
            {
                var first = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(dnp[0]));
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"Найдено ассетов DefaultNetworkPrefabs: {dnp.Length}. К NetworkManager подключён один — проверь, какой, второй кандидат на удаление.",
                    "План §3.4", first));
            }

            // 3. Орфан-скрипт UnitWaveSpawner (компонент упразднён, свёрнут в MatchManager). В норме лежит вне Assets — Unity его не видит.
            if (AssetDatabase.FindAssets("UnitWaveSpawner t:MonoScript").Length > 0)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Info,
                    "В Assets найден скрипт UnitWaveSpawner — компонент упразднён (свёрнут в MatchManager). Проверь, не орфан ли (§3.4).",
                    "Устаревшее §1.1; План §3.4", null));
        }

        // ======================== UI ВКЛАДКИ «ВАЛИДАТОР» ========================

        public static VisualElement CreateTabUI()
        {
            var root = new VisualElement { style = { marginTop = 6, marginLeft = 6, marginRight = 6 } };

            var runButton = new Button { text = "Проверить всё" };
            var summary = new Label { style = { marginTop = 4, marginBottom = 4, unityFontStyleAndWeight = FontStyle.Bold } };
            var listRoot = new VisualElement();

            runButton.clicked += () =>
            {
                RunAll();
                Redraw(summary, listRoot);
            };

            root.Add(runButton);
            root.Add(summary);
            root.Add(listRoot);

            // Если прогон уже был в этой сессии окна — показать кэш вместо пустоты.
            if (lastRun != null) Redraw(summary, listRoot);
            return root;
        }

        static void Redraw(Label summary, VisualElement listRoot)
        {
            listRoot.Clear();
            if (lastRun == null) return;

            int errors = lastRun.Count(i => i.severity == InterflowIssueSeverity.Error);
            int warnings = lastRun.Count(i => i.severity == InterflowIssueSeverity.Warning);
            int infos = lastRun.Count(i => i.severity == InterflowIssueSeverity.Info);
            summary.text = $"Ошибок: {errors}   Предупреждений: {warnings}   Инфо: {infos}";

            // Сортировка: ошибки → предупреждения → инфо.
            foreach (var issue in lastRun.OrderBy(i => i.severity))
                listRoot.Add(CreateIssueRow(issue));
        }

        static VisualElement CreateIssueRow(InterflowIssue issue)
        {
            var row = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, marginBottom = 2, alignItems = Align.FlexStart },
                tooltip = "Источник правила: " + issue.source
            };

            var badge = new Label(BadgeText(issue.severity))
            {
                style =
                {
                    width = 90, flexShrink = 0, unityFontStyleAndWeight = FontStyle.Bold,
                    color = BadgeColor(issue.severity)
                }
            };

            var text = new Label(issue.message) { style = { whiteSpace = WhiteSpace.Normal, flexGrow = 1 } };

            row.Add(badge);
            row.Add(text);

            if (issue.target != null)
            {
                var ping = new Button(() =>
                {
                    Selection.activeObject = issue.target;
                    EditorGUIUtility.PingObject(issue.target);
                })
                { text = "Показать", style = { flexShrink = 0 } };
                row.Add(ping);
            }

            return row;
        }

        static string BadgeText(InterflowIssueSeverity s) => s switch
        {
            InterflowIssueSeverity.Error => "ОШИБКА",
            InterflowIssueSeverity.Warning => "ВНИМАНИЕ",
            _ => "ИНФО"
        };

        static Color BadgeColor(InterflowIssueSeverity s) => s switch
        {
            InterflowIssueSeverity.Error => new Color(0.95f, 0.33f, 0.31f),
            InterflowIssueSeverity.Warning => new Color(0.98f, 0.75f, 0.18f),
            _ => new Color(0.55f, 0.75f, 0.98f)
        };
    }
}
