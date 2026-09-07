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
            ValidateCompositeSkills(issues, units, factions);
            ValidateFactions(issues, factions);
            ValidateTechTiers(issues, factions);
            ValidateMatchScene(issues, factions);
            ValidateMisc(issues);

            lastRun = issues;
            return issues;
        }

        // ============ ФИЛЬТР «УМЕНИЯ И СОСТОЯНИЯ» (окно «Редактор умений», Artsiom 2026-08-16) ============

        // Кэш последнего фильтрованного прогона — отдельный от полного RunAll.
        static List<InterflowIssue> lastAbilityRun;

        /// <summary>Только правила умений и состояний. Полный прогон по проекту — RunAll (главное окно).</summary>
        public static List<InterflowIssue> RunAbilityScope()
        {
            var issues = new List<InterflowIssue>();
            var units = LoadAllUnitPrefabs();
            var factions = LoadAllFactions();

            ValidateAbilities(issues, units, factions);
            ValidateCompositeSkills(issues, units, factions);

            lastAbilityRun = issues;
            return issues;
        }

        /// <summary>
        /// Кэш контекста точечной проверки умения: собирается ОДИН раз на «Обновить и перепроверить»,
        /// чтобы правка каждого поля в конструкторе не перечитывала все префабы и фракции проекта заново.
        /// </summary>
        public sealed class SkillValidationContext
        {
            internal HashSet<Ability> panel;
            internal List<(Unit unit, string path)> units;
        }

        public static SkillValidationContext BuildSkillContext() => new SkillValidationContext
        {
            panel = CollectPanelAbilities(LoadAllFactions()),
            units = LoadAllUnitPrefabs()
        };

        /// <summary>Проверки одного умения по ГОТОВОМУ контексту — без пересканирования проекта.</summary>
        public static List<InterflowIssue> ValidateSkillAlone(CompositeSkill skill, SkillValidationContext ctx)
        {
            var issues = new List<InterflowIssue>();
            if (skill == null || ctx == null) return issues;
            ValidateSkill(issues, skill, ctx.panel, ctx.units);
            return issues;
        }

        /// <summary>Вкладка «Проверка» окна «Редактор умений»: только фильтр умений и состояний.</summary>
        public static VisualElement CreateAbilityScopeTabUI()
        {
            var root = new VisualElement { style = { marginTop = 6, marginLeft = 6, marginRight = 6 } };

            var runButton = new Button { text = "Проверить умения и состояния" };
            runButton.tooltip = "Гоняет только правила умений и состояний. " +
                                "Полная проверка проекта — вкладка «Валидатор» главного окна Interflow Editor.";
            var summary = new Label { style = { marginTop = 4, marginBottom = 4, unityFontStyleAndWeight = FontStyle.Bold } };
            var listRoot = new VisualElement();

            runButton.clicked += () =>
            {
                RunAbilityScope();
                Redraw(summary, listRoot, lastAbilityRun);
            };

            root.Add(runButton);
            root.Add(summary);
            root.Add(listRoot);

            if (lastAbilityRun != null) Redraw(summary, listRoot, lastAbilityRun);
            return root;
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

        /// <summary>Все узлы дерева технологий фракции (уровень + вариант + его специализации, по всем тирам).
        /// Единая точка обхода: эффекты открытия живут на узлах (FactionConfig.TechNode).</summary>
        static IEnumerable<TechNode> AllTechNodes(FactionConfig f)
        {
            if (f == null || f.techTiers == null) yield break;
            foreach (var tier in f.techTiers)
            {
                if (tier == null) continue;
                if (tier.levelUpgrade != null) yield return tier.levelUpgrade;
                foreach (var opt in new[] { tier.optionA, tier.optionB })
                {
                    if (opt == null) continue;
                    if (opt.node != null) yield return opt.node;
                    if (opt.specializationA != null) yield return opt.specializationA;
                    if (opt.specializationB != null) yield return opt.specializationB;
                }
            }
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
                if (f.waveUnits != null) foreach (var e in f.waveUnits) if (e != null) Add(e.unit, true);
                Add(f.centreTower); Add(f.defence1Tower); Add(f.defence2Tower);
                Add(f.heroPrefab);
                // Юниты, на которые ссылаются УЗЛЫ дерева технологий (открытие + свопы префабов).
                foreach (var node in AllTechNodes(f))
                {
                    if (node.unlockUnits != null) foreach (var u in node.unlockUnits) Add(u, true);
                    if (node.unitSwaps != null) foreach (var s in node.unitSwaps) { Add(s?.from, true); Add(s?.to, true); }
                    if (node.towerSwaps != null) foreach (var s in node.towerSwaps) Add(s?.to);
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

            // 7. xpReward у КОНТЕНТНЫХ юнитов и башен: это же число кормит и уровень героя, и опыт главного
            //    здания (MatchManager.Experience). Ноль — убийство не даёт ни того, ни другого. Проверяем только
            //    контент фракций (волна, башни, герой, узлы дерева): декоративные префабы опыта давать не обязаны.
            foreach (var (unit, _) in units)
            {
                if (!factionUnits.Contains(unit)) continue;
                if (unit.xpReward > 0) continue;
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"«{unit.name}»: xpReward = 0 — за его убийство не начислится ни опыт главного здания, ни опыт героя.",
                    "Unit.xpReward; MatchManager.Experience (опыт за убийство и за башню)", unit));
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

            // 3. Иконка UI-умений: centralAbilities фракций и умения героя рисуются из icon.
            var uiAbilities = new HashSet<Ability>();
            foreach (var f in factions)
            {
                if (f.centralAbilities != null) foreach (var a in f.centralAbilities) if (a != null) uiAbilities.Add(a);
                if (f.heroPrefab != null && f.heroPrefab.abilities != null)
                    foreach (var a in f.heroPrefab.abilities) if (a != null) uiAbilities.Add(a);
            }
            foreach (var a in uiAbilities)
            {
                if (a.icon == null || a.icon == null)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"У UI-умения «{a.name}» не задана иконка icon — в таблице будет пустая ячейка.",
                        "Гайд 03 часть 1 (иконка из icon)", a));

                // 4. Длительность 0 у умения из таблицы: если умение длящееся — оно молча «не работает»
                //    (исторический кейс FlameCloak_active). Info: мгновенным активкам длительность не нужна.
                if (a.duration == null || a.duration.Length == 0 || a.duration.All(d => d <= 0f))
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Info,
                        $"У UI-умения «{a.name}» duration = 0/пусто — если умение длящееся, задай длительность (иначе эффект не применится).",
                        "Гайд 03 часть 2 (кейс FlameCloak_active)", a));

            }

            // 5. AutoAbilityUser: КАЖДАЯ авто-способность задана и продублирована в abilities[] юнита
            //    (обязательное правило ассета: каст идёт по индексу в пуле способностей).
            //    Поле приватное [SerializeField] — читаем ШТАТНЫМ SerializedObject (не reflection-хак; правило 9 соблюдено).
            foreach (var (unit, _) in units)
            {
                var auto = unit.GetComponent<AutoAbilityUser>();
                if (auto == null) continue;

                // С 2026-08-04 авто-способностей может быть НЕСКОЛЬКО: читаем список, а не одно поле.
                // С 2026-08-08 элемент списка — сама ссылка на умение (конструктор скиллов), а не запись
                // с полем «ability»: настройки поиска убраны с компонента, источник истины — умение.
                var so = new SerializedObject(auto);
                var entries = so.FindProperty("autoAbilities");

                if (entries == null || entries.arraySize == 0)
                {
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"На юните «{unit.name}» висит AutoAbilityUser с пустым списком авто-умений — компонент ничего не делает.",
                        "Гайд 03 часть 3", unit));
                    continue;
                }

                bool anyAuto = false;
                for (int i = 0; i < entries.arraySize; i++)
                {
                    var ability = entries.GetArrayElementAtIndex(i).objectReferenceValue as Ability;

                    if (ability == null)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"На юните «{unit.name}» запись авто-умения №{i + 1} пуста — она ничего не делает.",
                            "Гайд 03 часть 3", unit));
                    else if (unit.abilities == null || !unit.abilities.Contains(ability))
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"У юнита «{unit.name}» авто-умение «{ability.name}» НЕ добавлена в список Abilities юнита — авто-каст не сработает.",
                            "Гайд 03 часть 3 (двойная запись обязательна)", unit));
                    else
                        anyAuto = true;
                }

                // 6. Мана — шкала готовности авто-умения (блок Б5, целевая модель §9): умение готово, когда мана
                //    носителя заполнена целиком, срабатывание забирает всю. Без максимума маны или без её
                //    восстановления умение не сработает никогда — ошибка контента (AutoAbilityUser.TryCastEntry).
                if (anyAuto)
                {
                    if (unit.maxMana <= 0f)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"У юнита «{unit.name}» есть авто-умение, но максимум маны (maxMana) = 0 — мана никогда не заполнится, авто-каст не сработает.",
                            "AutoAbilityUser (готовность = мана заполнена целиком); целевая модель §9", unit));
                    if (unit.manaRegen <= 0f)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"У юнита «{unit.name}» есть авто-умение, но восстановление маны (manaRegen) = 0 — после первого срабатывания мана не вернётся, авто-каст замолчит.",
                            "AutoAbilityUser (срабатывание забирает всю ману); целевая модель §9", unit));
                }
            }
        }

        // ======================== БЛОК «КОНСТРУКТОР СКИЛЛОВ» ========================
        // Правила — План «Универсальная система способностей» §5.2. Числовых порогов не вводим (правило 7):
        // ловим только такие настройки, при которых блок гарантированно не сработает или сработает не так, как видит ГД.

        static void ValidateCompositeSkills(List<InterflowIssue> issues, List<(Unit unit, string path)> units, List<FactionConfig> factions)
        {
            var skills = new List<CompositeSkill>();
            foreach (string guid in AssetDatabase.FindAssets("t:CompositeSkill"))
            {
                var s = AssetDatabase.LoadAssetAtPath<CompositeSkill>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null) skills.Add(s);
            }
            if (skills.Count == 0) return;

            // Скиллы, подключённые к панели ГЗ или к герою: там нажимается кнопка, а панель
            // активирует только AbilityType.Active (UIManager.BottomTables → ActivateAbilityCell).
            var panelAbilities = CollectPanelAbilities(factions);

            foreach (var skill in skills)
                ValidateSkill(issues, skill, panelAbilities, units);
        }


        /// <summary>
        /// Скиллы, подключённые к панели ГЗ или к герою: там нажимается кнопка, а панель
        /// активирует только AbilityType.Active.
        /// </summary>
        static HashSet<Ability> CollectPanelAbilities(List<FactionConfig> factions)
        {
            var panelAbilities = new HashSet<Ability>();
            foreach (var f in factions)
            {
                if (f.centralAbilities != null)
                    foreach (var a in f.centralAbilities) if (a != null) panelAbilities.Add(a);
                if (f.heroPrefab != null && f.heroPrefab.abilities != null)
                    foreach (var a in f.heroPrefab.abilities) if (a != null) panelAbilities.Add(a);
                CollectNodeAbilities(f, panelAbilities);
            }
            return panelAbilities;
        }

        /// <summary>
        /// Проверки ОДНОГО скилла конструктора. Вынесено из цикла, чтобы вкладка «Конструктор скиллов»
        /// могла проверять скилл по месту, в момент правки, а не гонять весь проект (правило 5:
        /// правила живут в одном месте, вкладка их не дублирует).
        /// </summary>
        internal static void ValidateSkill(List<InterflowIssue> issues, CompositeSkill skill,
                                          HashSet<Ability> panelAbilities, List<(Unit unit, string path)> units)
        {
            if (skill == null) return;
                string n = skill.name;

                // --- 1. Урон без типа урона — запись молча не сработает. ---
                if (skill.damage != null && skill.damage.enabled)
                {
                    if (skill.damage.entries == null || skill.damage.entries.Length == 0)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"Умение «{n}»: блок урона включён, но записей нет — блок ничего не делает.",
                            "План §5.2", skill));
                    else
                        for (int i = 0; i < skill.damage.entries.Length; i++)
                        {
                            var e = skill.damage.entries[i];
                            if (e == null) continue;
                            bool hasAmount = e.amount != null && e.amount.Any(v => v > 0f);
                            if (hasAmount && e.damageType == null)
                                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                                    $"Умение «{n}»: в записи урона №{i + 1} задано число, но не задан тип урона — урона не будет.",
                                    "План §5.2", skill));
                        }
                }

                // --- 1.1. Селектор целей не заполнен — целей всегда ноль. ---
                // UnitSelector — struct: по умолчанию все флаги false, а IsUnitCompatible при этом всегда даёт false.
                // В режимах «на себя» и «вся команда» селектор не участвует — там пустой это норма.
                // Блокам 9–11 (призыв, зона, серверный сервис) набор целей не нужен: они исполняются
                // ОДИН раз за каст вне цикла по целям. Скиллу, где включены только они, селектор не нужен
                // ровно так же, как и radius (см. правило 8) — иначе это ложная ошибка на рабочем контенте.
                if (skill.targetMode != SkillTargetMode.Self && skill.targetMode != SkillTargetMode.WholeTeam
                    && HasPerTargetBlock(skill)
                    && !skill.unitSelector.AnySelectors())
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"Умение «{n}»: не заполнен селектор целей (кто может быть целью) — целей всегда будет ноль, " +
                        "но откат и стоимость спишутся, а VFX и звук проиграются. " +
                        "Отметь хотя бы отношение (свой/союзник/враг) и тип цели.",
                        "UnitSelector.IsUnitCompatible", skill));

                // --- 1.3. Настройки стратегии изменены, но режим цели их не читает. ---
                // Стратегия и её параметры участвуют ТОЛЬКО в режимах «умный выбор»
                // (предикат CompositeSkill.PicksTargetByStrategy — тот же, что читают рантайм и вкладка).
                // В остальных режимах изменённые значения выглядят настройкой, но ни на что не влияют.
                // Вкладка такие поля прячет — но в ДАННЫХ они остаются, поэтому правило нужно отдельно.
                if (!skill.PicksTargetByStrategy)
                {
                    var changed = ChangedStrategyFields(skill);
                    if (changed.Count > 0)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Info,
                            $"Умение «{n}»: режим цели «{InterflowEditorUI.EnumLabel(typeof(SkillTargetMode), skill.targetMode.ToString())}» " +
                            $"не использует стратегию выбора цели, а её настройки изменены ({string.Join(", ", changed)}) — " +
                            "они ни на что не влияют. Не ошибка, но и не настройка: либо верни значения по умолчанию, " +
                            "либо смени режим на «умный выбор».",
                            "CompositeSkill.PicksTargetByStrategy", skill));
                }

                // --- 1.2. Аура и детонация бафа без типа урона — молча не сработают. ---
                if (skill.buff != null && skill.buff.enabled)
                {
                    if (skill.buff.auraDamagePerSecond != null && skill.buff.auraDamagePerSecond.Any(v => v > 0f)
                        && skill.buff.auraDamageType == null)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"Умение «{n}»: у бафа задан урон ауры, но не задан тип урона ауры — урона не будет. " +
                            "Баф при этом повиснет со значком и визуалом, будто работает.",
                            "SkillBuff.OnTick", skill));

                    if (skill.buff.detonateOnDeath
                        && skill.buff.detonationDamage != null && skill.buff.detonationDamage.Any(v => v > 0f)
                        && skill.buff.detonationDamageType == null)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"Умение «{n}»: включена детонация при смерти носителя и задан урон взрыва, " +
                            "но не задан тип урона взрыва — взрыва не будет.",
                            "SkillBuff (детонация)", skill));
                }

                // --- 2. Доставка снарядом ---
                if (skill.delivery == SkillDelivery.Projectile)
                {
                    if (skill.projectilePrefab == null)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"Умение «{n}»: доставка снарядом, но префаб снаряда не задан — умение будет бить мгновенно.",
                            "План §5.2", skill));

                    if (skill.targetMode != SkillTargetMode.SmartUnit)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"Умение «{n}»: доставка снарядом работает только с режимом цели «Умный выбор юнита». " +
                            "В остальных режимах штатный снаряд бьёт лишь по площади, а площадного режима у снаряда умения нет — урона не будет.",
                            "Projectile.Update / Projectile.Damage", skill));

                    if (!skill.projectileFollowsTarget)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"Умение «{n}»: у снаряда выключено самонаведение — штатный снаряд полетит в мировой ноль и урона не нанесёт. Включи самонаведение.",
                            "Projectile.InternalSpawn (targetPosition не задаётся)", skill));

                    // Снаряд уносит только урон и оглушение — остальное срабатывает сразу в момент каста.
                    var notCarried = new List<string>();
                    if (skill.effectors != null && skill.effectors.enabled) notCarried.Add("состояния");
                    if (skill.statusEffector != null) notCarried.Add("значок состояния");
                    if (skill.heal != null && skill.heal.enabled) notCarried.Add("лечение");
                    if (skill.buff != null && skill.buff.enabled) notCarried.Add("баф");
                    if (skill.shield != null && skill.shield.enabled) notCarried.Add("щит");
                    if (skill.blind != null && skill.blind.enabled) notCarried.Add("ослепление");
                    if (notCarried.Count > 0)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"Умение «{n}»: снаряд переносит только урон и оглушение. Блоки [{string.Join(", ", notCarried)}] сработают сразу при касте, а не при попадании.",
                            "План §4.3 (ограничение v1)", skill));

                    if (skill.damage != null && skill.damage.enabled && skill.damage.entries != null
                        && skill.damage.entries.Count(e => e != null && e.damageType != null && e.amount != null && e.amount.Any(v => v > 0f)) > 1)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"Умение «{n}»: у снаряда одно поле урона — улетит только ПЕРВАЯ запись урона, остальные пропадут.",
                            "План §4.3 (ограничение v1)", skill));
                }

                // --- 3. Призыв без префаба ---
                if (skill.summon != null && skill.summon.enabled
                    && skill.summon.mode != SkillSummonMode.LastWave && skill.summon.prefab == null)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"Умение «{n}»: блок призыва включён, но префаб юнита не задан — призыва не будет.",
                        "План §5.2", skill));

                // --- 4. Зона: префаб без компонента ---
                if (skill.groundZone != null && skill.groundZone.enabled)
                {
                    if (skill.groundZone.zonePrefab == null)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"Умение «{n}»: блок зоны включён, но префаб зоны не задан.",
                            "План §5.2", skill));
                    else if (skill.groundZone.zonePrefab.GetComponent<GroundDamageZone>() == null)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"Умение «{n}»: на префабе зоны «{skill.groundZone.zonePrefab.name}» нет компонента GroundDamageZone — зона не будет действовать.",
                            "План §5.2", skill));

                    // 4а. Аура на время (блок Б7): зона идёт за кастером — ставится одна, в его позиции;
                    // количество, разброс и смещение при этом не применяются, о чём честно предупреждаем (правило 9).
                    if (skill.groundZone.followCaster
                        && (skill.groundZone.zoneCount > 1 || skill.groundZone.forwardOffset != 0f))
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"Умение «{n}»: зона идёт за кастером, а заданы количество зон больше одной или смещение вперёд — они не применяются.",
                            "SkillGroundZoneBlock.followCaster (тултип)", skill));
                }

                // --- 5. Баф с нулевой длительностью ---
                if (skill.buff != null && skill.buff.enabled
                    && (skill.buff.duration == null || !skill.buff.duration.Any(v => v > 0f)))
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"Умение «{n}»: блок бафа включён, но длительность 0/пусто — баф не наложится (исторический кейс FlameCloak_active).",
                        "План §5.2", skill));

                // --- 6. Скилл панели без галки «Скилл кнопки» ---
                if (panelAbilities.Contains(skill) && skill.type != AbilityType.Active)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"Умение «{n}» подключён к панели ГЗ/героя, но его тип — {skill.type}. Панель активирует только Active: включи галку «Умение по кнопке».",
                        "UIManager.BottomTables → ActivateAbilityCell", skill));

                // --- 7. Стратегия «текущая цель атаки» у каста с кнопки ---
                if (skill.buttonCast && skill.PicksTargetByStrategy
                    && skill.TargetStrategy == SkillTargetStrategy.CurrentAttackTarget)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"Умение «{n}»: стратегия «текущая цель атаки» у умения по кнопке никогда не найдёт цель: кастер панели не атакует.",
                        "План §4.3", skill));

                // --- 8. Радиус там, где он обязателен ---
                // Радиус нужен только тому, кто СОБИРАЕТ ЦЕЛИ. Блоки 9–11 (призыв, зона на земле,
                // серверный сервис) исполняются один раз за каст ВНЕ цикла по целям
                // (CompositeSkill.ApplyEffects), поэтому скиллу, где включены только они,
                // набор целей не нужен и radius ни на что не влияет.
                bool areaMode = skill.targetMode == SkillTargetMode.AreaAroundSelf
                                || skill.targetMode == SkillTargetMode.Cone
                                || skill.targetMode == SkillTargetMode.SmartPoint;
                bool needsRadius = areaMode && HasPerTargetBlock(skill);

                // Стратегия «скопление врагов» меряет плотность в этом же radius; при нуле она молча
                // вырождается в случайный выбор — ГД получит не то поведение и без единого сообщения.
                if (!needsRadius && skill.PicksTargetByStrategy
                    && skill.TargetStrategy == SkillTargetStrategy.Cluster
                    && (skill.radius == null || !skill.radius.Any(v => v > 0f)))
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"Умение «{n}»: стратегия «скопление» считает плотность в radius, а он пуст/0 — цель будет выбираться случайно.",
                        "SkillTargeting.DensestCluster", skill));
                if (needsRadius && (skill.radius == null || !skill.radius.Any(v => v > 0f)))
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"Умение «{n}»: режим цели требует radius больше нуля, а он пуст/0 — целей не будет никогда.",
                        "План §5.2", skill));

                // --- 9. Значок состояния ---
                if (skill.statusEffector != null)
                {
                    if (skill.statusEffector.stacks)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"Умение «{n}»: у состояния-значка «{skill.statusEffector.name}» включён Stacks (накопление) — значок в панели не появится.",
                            "Effector.EffectorAdd → OnStatusUpdate (только нестакающие)", skill));

                    if (skill.statusEffector.icon == null)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"Умение «{n}»: у состояния-значка «{skill.statusEffector.name}» не задана иконка — игрок ничего не увидит.",
                            "План §5.2", skill));
                }

                // --- 9.1 Числа записей блока эффекторов (с 2026-08-02 сила и длительность живут в умении) ---
                if (skill.effectors != null && skill.effectors.enabled && skill.effectors.records != null)
                {
                    for (int ri = 0; ri < skill.effectors.records.Length; ri++)
                    {
                        var rec = skill.effectors.records[ri];

                        if (rec == null || rec.effector == null)
                        {
                            issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                                $"Умение «{n}»: в блоке состояний запись №{ri + 1} без ассета состояния — запись ничего не сделает.",
                                "CompositeSkill.ApplyEffectors (пустые записи пропускаются)", skill));
                            continue;
                        }

                        string en = rec.effector.name;

                        // Минус ломает формат сохранения: записи там разделяются дефисом.
                        if (rec.power != null && rec.power.Any(x => x < 0f))
                            issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                                $"Умение «{n}»: у состояния «{en}» отрицательный множитель силы. Запрещено: разделитель записей " +
                                "в формате сохранения — дефис, минус сломает сейв и восстановление юнита.",
                                "SaveManager.UnitData (строка effectors)", skill));

                        if (rec.duration != null && rec.duration.Any(x => x < 0f))
                            issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                                $"Умение «{n}»: у состояния «{en}» отрицательная длительность. Пусто или 0 — «брать из ассета»; " +
                                "минус ломает сейв тем же дефисом-разделителем.",
                                "SaveManager.UnitData (строка effectors)", skill));

                        // Множитель масштабирует ровно две вещи: пассивные изменения статов и урон в секунду.
                        bool scalable = rec.effector.passiveEffectsOn || rec.effector.damageAmount != 0;
                        bool powerSet = rec.power != null && rec.power.Any(x => x > 0f && Mathf.Abs(x - 1f) > 0.0001f);
                        if (powerSet && !scalable)
                            issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                                $"Умение «{n}»: у состояния «{en}» задан множитель силы, но масштабировать нечего — " +
                                "пассивные изменения выключены и урона в секунду нет.",
                                "Effector.EffectorAdd (множитель идёт в passiveEffects и damageAmount)", skill));

                        if (rec.effector.permanent && rec.duration != null && rec.duration.Any(x => x > 0f))
                            issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                                $"Умение «{n}»: у состояния «{en}» включён Permanent — заданная длительность будет проигнорирована.",
                                "Effector.EffectorAdd (у бессрочных durationOverride не применяется)", skill));
                    }
                }

                // Хвост старого формата проверяется ВНЕ цикла: у немигрированного ассета records пуст,
                // цикл не исполняется ни разу — проверка внутри промолчала бы ровно там, где нужна.
                // Полностью мигрированный хвост НЕ трогаем: он оставлен намеренно, как страховка отката.
                if (skill.effectors != null && skill.effectors.effectors != null && skill.effectors.effectors.Length > 0
                    && (skill.effectors.records == null || skill.effectors.records.Length < skill.effectors.effectors.Length))
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"Умение «{n}»: устаревший массив состояний ({skill.effectors.effectors.Length} шт.) не перенесён в записи " +
                        $"({(skill.effectors.records == null ? 0 : skill.effectors.records.Length)} шт.) — эти состояния больше НЕ накладываются.",
                        "SkillEffectorsBlock.records (миграция 2026-08-02)", skill));

                // --- 10. Включённые блоки без единого эффекта ---
                foreach (string empty in EmptyEnabledBlocks(skill))
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"Умение «{n}»: блок «{empty}» включён, но все его значения нулевые/пустые — он ничего не делает.",
                        "План §5.2", skill));

                // 10а (разовые блоки в режиме «аура») снято блоком Б7 (2026-09-05): режима «аура» у умений нет.

                // --- 10б. Щит без срока: ядро считает 0 как «без таймера», то есть щит бессрочный ---
                if (skill.shield != null && skill.shield.enabled
                    && (skill.shield.duration == null || !skill.shield.duration.Any(v => v > 0f)))
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"Умение «{n}»: у щита не задана длительность — он станет БЕССРОЧНЫМ и сойдёт только при пробитии или смерти носителя.",
                        "AbsorbShield (duration ≤ 0 = без таймера)", skill));

                // --- 11. Визуал замаха при нулевом времени каста ---
                if (skill.castVFX != null && (skill.castTime == null || !skill.castTime.Any(v => v > 0f)))
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"Умение «{n}»: задан визуал каста, но castTime = 0 — замах и удар совпадут в один кадр.",
                        "План §5.2", skill));

                // --- 12. Каст с кнопки с умным выбором: клиент не воспроизведёт выбор цели ---
                if (skill.buttonCast && skill.PicksTargetByStrategy
                    && (skill.impactVFX != null || skill.delivery == SkillDelivery.Projectile))
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"Умение «{n}»: цель выбирает стратегия на СЕРВЕРЕ — клиент не увидит визуала попадания и снаряда. " +
                        "Значки состояний и визуал бафа до клиента доедут: их сервер шлёт отдельным сообщением.",
                        "CompositeSkill.Execute (ранний выход клиента)", skill));

                // --- 13. Сокет задан, а на носителе нет CharacterSockets ---
                if (skill.spawnSocket != SkillSocketType.None && (skill.castVFX != null || skill.delivery == SkillDelivery.Projectile))
                {
                    foreach (var (unit, _) in units)
                    {
                        if (unit.abilities == null || !unit.abilities.Contains(skill)) continue;
                        if (unit.GetComponent<CharacterSockets>() != null) continue;

                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"Умение «{n}» стреляет из точки привязки, но на носителе «{unit.name}» нет компонента CharacterSockets — всё пойдёт из центра объекта.",
                            "План §5.2", unit));
                    }
                }
        }

        /// <summary>
        /// Проверки ВСЕХ скиллов конструктора за один сбор контекста: вкладке нужны и точки статуса в списке,
        /// и сообщения по выбранному скиллу. Отдельный вызов на каждый скилл перечитывал бы все префабы проекта.
        /// </summary>
        public static Dictionary<CompositeSkill, List<InterflowIssue>> ValidateAllSkills()
        {
            var map = new Dictionary<CompositeSkill, List<InterflowIssue>>();
            var factions = LoadAllFactions();
            var units = LoadAllUnitPrefabs();
            var panel = CollectPanelAbilities(factions);

            foreach (string guid in AssetDatabase.FindAssets("t:CompositeSkill"))
            {
                var s = AssetDatabase.LoadAssetAtPath<CompositeSkill>(AssetDatabase.GUIDToAssetPath(guid));
                if (s == null) continue;
                var list = new List<InterflowIssue>();
                ValidateSkill(list, s, panel, units);
                map[s] = list;
            }
            return map;
        }

        /// <summary>Проверки одного скилла со сбором контекста «с нуля» — точка входа для вкладки.</summary>
        public static List<InterflowIssue> ValidateSkillAlone(CompositeSkill skill)
        {
            var issues = new List<InterflowIssue>();
            if (skill == null) return issues;
            ValidateSkill(issues, skill, CollectPanelAbilities(LoadAllFactions()), LoadAllUnitPrefabs());
            return issues;
        }
        /// <summary>Умения, которые открываются узлами дерева технологий — они тоже попадают в панель ГЗ.</summary>
        static void CollectNodeAbilities(FactionConfig f, HashSet<Ability> into)
        {
            if (f == null || f.techTiers == null) return;

            foreach (var tier in f.techTiers)
            {
                if (tier == null) continue;
                AddNode(tier.levelUpgrade, into);
                AddOption(tier.optionA, into);
                AddOption(tier.optionB, into);
            }
        }

        static void AddOption(TechBigOption option, HashSet<Ability> into)
        {
            if (option == null) return;
            AddNode(option.node, into);
            AddNode(option.specializationA, into);
            AddNode(option.specializationB, into);

            // Герой, который открывается этой веткой, приносит свои умения в панель героя.
            if (option.heroPrefab != null && option.heroPrefab.abilities != null)
                foreach (var a in option.heroPrefab.abilities) if (a != null) into.Add(a);
        }

        static void AddNode(TechNode node, HashSet<Ability> into)
        {
            if (node == null || node.unlockAbilities == null) return;
            foreach (var u in node.unlockAbilities) if (u != null && u.ability != null) into.Add(u.ability);
        }

        // Эталон значений по умолчанию.
        // Сначала здесь был `ScriptableObject.CreateInstance<CompositeSkill>()` — красивее, но НЕЛЬЗЯ:
        // `Ability.OnEnable` на свежем экземпляре бросает NullReferenceException (гвард написан как
        // `if (abilityName == null || cooldown.Length == 0)` — первая часть всегда false, вторая падает).
        // Unity льёт это в консоль и не пробрасывает вызывающему — try/catch не спасает.
        //
        // Цена решения, честно: если дефолт в CompositeSkill поменяют, эти числа надо поменять руками.
        // Последствие рассинхрона мягкое: правило Info сработает лишний раз или промолчит.
        // Источник — объявления полей в `CompositeSkill.cs`, блок [Header("Цель")].
        const SkillTargetStrategy DEF_STRATEGY = SkillTargetStrategy.Nearest;
        const SkillSearchOrigin DEF_ORIGIN = SkillSearchOrigin.Caster;
        const bool DEF_USE_CURRENT_HP = false;
        const float DEF_HP_THRESHOLD = 0.3f;

        /// <summary>Какие настройки стратегии отличаются от значений по умолчанию (для правила 1.3).</summary>
        static List<string> ChangedStrategyFields(CompositeSkill s)
        {
            var changed = new List<string>();
            if (s == null) return changed;

            if (s.targetStrategy != DEF_STRATEGY) changed.Add("как выбрать одну цель");
            if (s.searchOrigin != DEF_ORIGIN) changed.Add("откуда считать «ближайшего»");
            if (s.targetCategories != null && s.targetCategories.Length > 0) changed.Add("селектор ролей");
            if (s.strategyUseCurrentHealth != DEF_USE_CURRENT_HP) changed.Add("мерить текущее ХП");
            if (!Mathf.Approximately(s.strategyHpThreshold, DEF_HP_THRESHOLD)) changed.Add("порог ХП");

            return changed;
        }

        /// <summary>
        /// Есть ли хотя бы один блок, который применяется К ЦЕЛИ (блоки 2..8 в CompositeSkill.ApplyEffects:
        /// урон, контроль, эффекторы, лечение, баф, щит, ослепление). Значок состояния считается тоже:
        /// CompositeSkill.EffectorsForTargets добавляет его целям даже при выключенном блоке эффекторов.
        /// Блоки 9–11 (призыв, зона, серверный сервис) исполняются вне цикла по целям — им цели не нужны.
        /// </summary>
        static bool HasPerTargetBlock(CompositeSkill s) =>
               (s.pull != null && s.pull.enabled)
            || (s.damage != null && s.damage.enabled)
            || (s.drain != null && s.drain.enabled)
            || (s.status != null && s.status.enabled)
            || (s.effectors != null && s.effectors.enabled)
            || s.statusEffector != null
            || (s.heal != null && s.heal.enabled)
            || (s.mana != null && s.mana.enabled)
            || (s.buff != null && s.buff.enabled)
            || (s.shield != null && s.shield.enabled)
            || (s.blind != null && s.blind.enabled)
            || (s.morph != null && s.morph.enabled)
            || (s.ownership != null && s.ownership.enabled)
            || (s.secondary != null && s.secondary.enabled);

        /// <summary>Имена включённых блоков, у которых все числа нулевые или ссылки пусты.</summary>
        static IEnumerable<string> EmptyEnabledBlocks(CompositeSkill s)
        {
            bool Any(float[] a) => a != null && a.Any(v => v > 0f);

            if (s.selfCost != null && s.selfCost.enabled && !Any(s.selfCost.flatHp) && !Any(s.selfCost.percentOfCurrentHp))
                yield return "стоимость в здоровье";

            if (s.status != null && s.status.enabled
                && !Any(s.status.stunSeconds) && !Any(s.status.disarmSeconds) && !Any(s.status.muteSeconds))
                yield return "контроль";

            if (s.effectors != null && s.effectors.enabled
                && (s.effectors.records == null || s.effectors.records.All(r => r == null || r.effector == null)))
                yield return "состояния";

            if (s.heal != null && s.heal.enabled && !Any(s.heal.flat) && !Any(s.heal.percentOfMaxHp))
                yield return "лечение";

            if (s.shield != null && s.shield.enabled && !Any(s.shield.flat) && !Any(s.shield.percentOfMaxHp))
                yield return "щит";

            // Режим «дубль последней волны» состав отряда берёт из самой волны: count и prefab
            // ему не нужны по определению — CompositeSkill.ApplySummon их в MatchManager.SummonLastWave
            // даже не передаёт. Правило 3 выше этот режим уже исключает — здесь так же.
            if (s.summon != null && s.summon.enabled && s.summon.mode != SkillSummonMode.LastWave
                && !Any(s.summon.count))
                yield return "призыв";

            // Баф с длительностью, но вообще без эффектов — висит значком и не делает ничего.
            if (s.buff != null && s.buff.enabled && Any(s.buff.duration)
                && !Any(s.buff.auraDamagePerSecond) && !Any(s.buff.healPerSecond) && !Any(s.buff.healPercentOfMaxPerSecond)
                && !Any(s.buff.selfBurnPerSecond) && !s.buff.controlImmunity && !s.buff.detonateOnDeath
                && (s.buff.incomingDamageMultiplier == null || !s.buff.incomingDamageMultiplier.Any(v => v != 1f)))
                yield return "длящийся баф";

            if (s.blind != null && s.blind.enabled && (!Any(s.blind.chance) || !Any(s.blind.duration)))
                yield return "ослепление";

            if (s.delegateService != null && s.delegateService.enabled && s.delegateService.service == SkillServerService.None)
                yield return "серверный сервис";

            if (s.drain != null && s.drain.enabled
                && !Any(s.drain.flat) && !Any(s.drain.percentOfMaxHp) && !Any(s.drain.percentOfCurrentHp))
                yield return "высасывание ХП";

            if (s.mana != null && s.mana.enabled && !Any(s.mana.flat) && !Any(s.mana.percentOfMaxMana))
                yield return "восстановление маны";

            if (s.morph != null && s.morph.enabled && (s.morph.shapeUnit == null || !Any(s.morph.duration)))
                yield return "подмена облика";

            if (s.secondary != null && s.secondary.enabled
                && (s.secondary.radius <= 0f
                    || (!Any(s.secondary.healFlat) && (s.secondary.effectors == null || s.secondary.effectors.Length == 0))))
                yield return "вторичные цели";
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

                // 2. Ячейки панели умений ГЗ (один ряд из MatchManager.CentralAbilitySlotCount ячеек).
                //    Стартовые умения занимают ячейки по порядку списка; узлы дерева — по своему полю slot.
                //    Занятая дважды ячейка = в матче покажется только первое умение (см. PlaceCentralAbility).
                int slotCount = MatchManager.CentralAbilitySlotCount;
                var slotOwner = new Dictionary<int, string>();   // ячейка → кто её уже занял (для текста ошибки)

                void Occupy(int slot, Ability ab, string where)
                {
                    if (ab == null) return;
                    if (slot < 0 || slot >= slotCount)
                    {
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"Фракция «{f.name}»: умение «{ab.name}» ({where}) метит в ячейку {slot}, а ячеек всего {slotCount} (0..{slotCount - 1}) — в панели не появится.",
                            "FactionConfig.AbilityUnlock.slot ↔ MatchManager.CentralAbilitySlotCount", f));
                        return;
                    }
                    if (slotOwner.TryGetValue(slot, out string taken))
                    {
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"Фракция «{f.name}»: ячейка {slot} панели умений ГЗ занята дважды — «{taken}» и «{ab.name}» ({where}). В матче покажется только первое, второе умение потеряется.",
                            "FactionConfig.AbilityUnlock.slot; MatchManager.PlaceCentralAbility", f));
                        return;
                    }
                    slotOwner[slot] = ab.name;
                }

                if (f.centralAbilities != null)
                {
                    if (f.centralAbilities.Count > slotCount)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                            $"Фракция «{f.name}»: стартовых умений ГЗ {f.centralAbilities.Count}, а ячеек в панели {slotCount} — лишние в матч не попадут.",
                            "FactionConfig.centralAbilities (порядок = номер ячейки)", f));
                    for (int i = 0; i < f.centralAbilities.Count; i++)
                        Occupy(i, f.centralAbilities[i], "стартовое, позиция в centralAbilities");
                }

                foreach (var node in AllTechNodes(f))
                {
                    if (node.unlockAbilities == null) continue;
                    string nodeName = node.technology != null ? node.technology.name : "узел без технологии";
                    foreach (var e in node.unlockAbilities)
                    {
                        if (e == null) continue;
                        if (e.ability == null)
                        {
                            issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                                $"Фракция «{f.name}»: у узла «{nodeName}» в списке открываемых умений пустая запись — узел ничего не откроет.",
                                "FactionConfig.TechNode.unlockAbilities", f));
                            continue;
                        }
                        if (node.technology == null)
                            issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                                $"Фракция «{f.name}»: узел без технологии открывает умение «{e.ability.name}» — купить такой узел нельзя, умение недостижимо.",
                                "FactionConfig.TechNode.technology", f));
                        Occupy(e.slot, e.ability, $"узел «{nodeName}»");
                    }
                }


                // 6. Герой несёт LevelingUnit (иначе уровни и улучшения умений героя не работают). maxLevel ≥ порога — в блоке «Матч/сцена».
                if (f.heroPrefab != null && f.heroPrefab.GetComponent<LevelingUnit>() == null)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"У героя фракции «{f.name}» нет компонента LevelingUnit — опыт, уровни и улучшения умений героя работать не будут.",
                        "Гайд 06; FactionConfig.heroPrefab (тултип: уровни через LevelingUnit)", f.heroPrefab));
            }
        }

        // ======================== БЛОК «ТЕХНОЛОГИИ (тиры)» (Технологии 2.0) ========================
        // Структурные проверки дерева тиров FactionConfig.techTiers (правило 7 — только структура/ссылки, без порогов).
        // Источник правил: План_Технологии_2.0 §6; [[concepts/tech-tiers-redesign]]. Пустой тир — ожидаемое состояние
        // (дизайнер ещё не заполнил) → Info, не ошибка; частично заполненный тир → ошибки по пропущенным узлам.

        static void ValidateTechTiers(List<InterflowIssue> issues, List<FactionConfig> factions)
        {
            const string src = "План_Технологии_2.0 §6; tech-tiers-redesign";

            foreach (var f in factions)
            {
                if (f.techTiers == null) continue;

                var heroTiers = new List<int>();                              // тиры, где вариант открывает героя (правило 5)
                var techToNodes = new Dictionary<Technology, List<string>>(); // одна Technology → узлы, где встречается (правило 3)

                for (int t = 0; t < f.techTiers.Length; t++)
                {
                    var tier = f.techTiers[t];
                    if (tier == null) continue;
                    int tierNo = t + 1;

                    // Все узлы тира с русской подписью пути (для сообщений). optionA/optionB — [Serializable], защищаемся от null.
                    var nodes = new List<(TechNode node, string label)>
                    {
                        (tier.levelUpgrade,             "улучшение уровня"),
                        (tier.optionA?.node,            "вариант А"),
                        (tier.optionA?.specializationA, "вариант А · спец. А"),
                        (tier.optionA?.specializationB, "вариант А · спец. Б"),
                        (tier.optionB?.node,            "вариант Б"),
                        (tier.optionB?.specializationA, "вариант Б · спец. А"),
                        (tier.optionB?.specializationB, "вариант Б · спец. Б"),
                    };

                    bool anyHero = (tier.optionA != null && tier.optionA.heroPrefab != null)
                                || (tier.optionB != null && tier.optionB.heroPrefab != null);

                    // Правило 1 — полнота. Пустой тир (ни одной Technology и нет героя) — норма (Info); иначе пропуски → Error.
                    int filled = nodes.Count(n => n.node != null && n.node.technology != null);
                    if (filled == 0 && !anyHero)
                    {
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Info,
                            $"Фракция «{f.name}»: тир {tierNo} ещё не заполнен (все узлы пусты) — норма, если дизайнер до него не дошёл.",
                            src, f));
                    }
                    else
                    {
                        foreach (var (node, label) in nodes)
                            if (node == null || node.technology == null)
                                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                                    $"Фракция «{f.name}», тир {tierNo}: не задана Technology у узла «{label}» — тир неполный (обязательны уровень, оба варианта и обе их специализации).",
                                    src, f));
                    }

                    if (anyHero) heroTiers.Add(tierNo);

                    // Правила 2 (Resources), 4 (цены ≥ 0) и сбор для правила 3 — по каждому заданному узлу.
                    foreach (var (node, label) in nodes)
                    {
                        if (node == null) continue;

                        // Правило 4 — цена не отрицательна (ResourceWrapper.value — int).
                        if (node.cost != null)
                            foreach (var c in node.cost)
                                if (c != null && c.value < 0)
                                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                                        $"Фракция «{f.name}», тир {tierNo}, узел «{label}»: отрицательная цена ({c.value}) у ресурса «{(c.type != null ? c.type.name : "?")}» — цена не может быть меньше 0.",
                                        src, f));

                        var tech = node.technology;
                        if (tech == null) continue;

                        // Правило 2 — Technology лежит в Resources/Technology (требование ядра: загрузка/гейтинг по id).
                        string path = AssetDatabase.GetAssetPath(tech);
                        if (!InResourcesSubfolder(path, "Technology"))
                            issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                                $"Фракция «{f.name}», тир {tierNo}, узел «{label}»: Technology «{tech.name}» вне Resources/Technology ({path}) — ядро её не загрузит.",
                                src, tech));

                        // Сбор для правила 3 (дубли между узлами).
                        if (!techToNodes.TryGetValue(tech, out var list)) techToNodes[tech] = list = new List<string>();
                        list.Add($"тир {tierNo} · {label}");
                    }
                }

                // Правило 3 — одна Technology не должна повторяться в разных узлах одной фракции.
                foreach (var pair in techToNodes.Where(p => p.Value.Count > 1))
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"Фракция «{f.name}»: Technology «{pair.Key.name}» назначена нескольким узлам ({string.Join("; ", pair.Value)}) — узлы должны ссылаться на разные технологии.",
                        src, pair.Key));

                // Правило 5 — героев достижимо ≤ 1: heroPrefab в разных тирах → можно открыть >1 героя за матч.
                if (heroTiers.Count > 1)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"Фракция «{f.name}»: префаб героя (heroPrefab) задан в нескольких тирах ({string.Join(", ", heroTiers.Select(n => "тир " + n))}) — за матч открывается только один герой. Оставь героя в вариантах одного тира.",
                        src, f));
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

            // 3б. Числа опыта главного здания (MatchManager.Experience): без порога уровень не растёт вовсе,
            //     а неположительный период за центр означал бы тик каждый кадр (в корутине он поднят до секунды).
            int xpPerLevel = so.FindProperty("experiencePerLevel")?.intValue ?? 0;
            if (xpPerLevel <= 0)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"MatchManager.experiencePerLevel = {xpPerLevel} — порог опыта на уровень не задан, уровень главного здания не поднимется никогда.",
                    "MatchManager.Experience.AddExperience (порог ≤ 0 → выход)", mm));

            float centrePeriod = so.FindProperty("centreHoldPeriod")?.floatValue ?? 0f;
            if (centrePeriod <= 0f)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"MatchManager.centreHoldPeriod = {centrePeriod} — период начисления опыта за центр должен быть больше нуля (корутина подставит 1 секунду).",
                    "MatchManager.Experience.CentreHoldExperienceLoop", mm));

            // 4. Достижимый уровень ГЗ (предел = число тиров) vs таблицы уровня (решение Artsiom 2026-07-24).
            //    Подписчики клампят индекс (Clamp(level−1, 0, len−1)) — ошибки не будет, но выше последней записи
            //    прогрессия молча замирает; предупреждаем, чтобы дизайнер дозаполнил таблицы. Пустая таблица —
            //    фича не используется, не ворним. Прежняя проверка «лимит Лидерства = пул×3» удалена: поле
            //    waveLeadershipPool снесено Волной 2.0 (лимит лидерства задаёт дизайнер напрямую в GameResources).
            int mbStart = so.FindProperty("startMainBuildingLevel")?.intValue ?? 0;
            var mbStats = so.FindProperty("mainBuildingStatsByLevel");
            int mbStatsLen = (mbStats != null && mbStats.isArray) ? mbStats.arraySize : 0;
            foreach (var f in factions)
            {
                int tiers = (f != null && f.techTiers != null) ? f.techTiers.Length : 0;
                if (tiers == 0)
                {
                    // Предел уровня ГЗ = числу тиров (MatchManager.Experience.AddExperience). Ноль тиров —
                    // уровень не поднимется никогда, сколько бы опыта команда ни набрала.
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"У фракции «{(f != null ? f.name : "?")}» дерево тиров пусто — предел уровня главного здания равен нулю, уровень не поднимется никогда (опыт будет копиться впустую).",
                        "MatchManager.TechTierCount ↔ MatchManager.Experience (предел уровня)", f));
                    continue;
                }
                // Достижимый максимум уровня: старт задаётся в Inspector, а рост опытом упирается в число тиров
                // (MatchManager.Experience.AddExperience: mainBuildingLevel < TechTierCount). Прежняя формула
                // «старт + тиры» отвечала модели «покупка узла = +1 уровень» и после перехода на опыт завышала предел.
                int maxLevel = Mathf.Max(mbStart, tiers);

                if (mbStatsLen > 0 && mbStatsLen < maxLevel)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"Статов ГЗ (mainBuildingStatsByLevel: {mbStatsLen}) меньше достижимого уровня {maxLevel} (старт {mbStart}, предел по числу тиров «{f.name}» — {tiers}) — выше уровня {mbStatsLen} статы замрут на последней записи.",
                        "MatchManager.mainBuildingStatsByLevel ↔ FactionConfig.techTiers", mm));
                if (f.mainBuildingShapesByLevel != null && f.mainBuildingShapesByLevel.Length > 0 && f.mainBuildingShapesByLevel.Length < maxLevel)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"Обликов замка (mainBuildingShapesByLevel: {f.mainBuildingShapesByLevel.Length}) у «{f.name}» меньше достижимого уровня {maxLevel} — выше облик перестанет меняться.",
                        "FactionConfig.mainBuildingShapesByLevel ↔ techTiers", f));
                if (f.soulsResource != null && f.soulsPerMinuteByMbLevel != null && f.soulsPerMinuteByMbLevel.Length > 0 && f.soulsPerMinuteByMbLevel.Length < maxLevel)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"Генерация душ (soulsPerMinuteByMbLevel: {f.soulsPerMinuteByMbLevel.Length}) у «{f.name}» меньше достижимого уровня {maxLevel} — выше генерация замрёт на последней записи.",
                        "FactionConfig.soulsPerMinuteByMbLevel ↔ techTiers", f));
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

            // 6. Темп волн: согласованность интервала и окон (иначе рассинхрон таймера и фактической волны).
            //    Сравниваются заданные поля между собой (не выдуманные пороги, правило 7).
            float firstDelay = so.FindProperty("firstWaveDelay")?.floatValue ?? 0f;
            float interval   = so.FindProperty("waveInterval")?.floatValue ?? 0f;
            float warn       = so.FindProperty("warnSeconds")?.floatValue ?? 0f;
            float lockS      = so.FindProperty("lockSeconds")?.floatValue ?? 0f;

            if (interval <= 0f)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"MatchManager.waveInterval = {interval:0.##} ≤ 0 — интервал между волнами должен быть положительным.",
                    "MatchManager.waveInterval (интервал > 0)", mm));
            if (firstDelay < 0f)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"MatchManager.firstWaveDelay = {firstDelay:0.##} < 0 — задержка первой волны не может быть отрицательной.",
                    "MatchManager.firstWaveDelay", mm));
            if (lockS < 0f)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"MatchManager.lockSeconds = {lockS:0.##} < 0 — окно блокировки не может быть отрицательным.",
                    "MatchManager.lockSeconds", mm));
            if (warn < lockS)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"MatchManager.warnSeconds ({warn:0.##}) < lockSeconds ({lockS:0.##}) — окно предупреждения должно быть больше окна блокировки (предупреждение идёт раньше блокировки).",
                    "MatchManager.warnSeconds/lockSeconds (warn > lock)", mm));
            if (interval > 0f && warn > interval)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"MatchManager.warnSeconds ({warn:0.##}) > waveInterval ({interval:0.##}) — окно предупреждения не помещается в интервал; предупреждение вырождается в момент прошлой волны (темп сохранится, окно сжато).",
                    "MatchManager.warnSeconds ↔ waveInterval", mm));
            if (firstDelay > 0f && warn > firstDelay)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"MatchManager.warnSeconds ({warn:0.##}) > firstWaveDelay ({firstDelay:0.##}) — перед первой волной окно предупреждения сжимается.",
                    "MatchManager.warnSeconds ↔ firstWaveDelay", mm));
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
                Redraw(summary, listRoot, lastRun);
            };

            root.Add(runButton);
            root.Add(summary);
            root.Add(listRoot);

            // Если прогон уже был в этой сессии окна — показать кэш вместо пустоты.
            if (lastRun != null) Redraw(summary, listRoot, lastRun);
            return root;
        }

        static void Redraw(Label summary, VisualElement listRoot, List<InterflowIssue> data)
        {
            listRoot.Clear();
            if (data == null) return;

            int errors = data.Count(i => i.severity == InterflowIssueSeverity.Error);
            int warnings = data.Count(i => i.severity == InterflowIssueSeverity.Warning);
            int infos = data.Count(i => i.severity == InterflowIssueSeverity.Info);
            summary.text = $"Ошибок: {errors}   Предупреждений: {warnings}   Инфо: {infos}";

            // Сортировка: ошибки → предупреждения → инфо.
            foreach (var issue in data.OrderBy(i => i.severity))
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
