using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============================= INTERFLOW EDITOR — ВКЛАДКА «ЮНИТЫ» (фаза E4, шаги 1–2) ==
    // Список префабов с компонентом Unit + фильтры (тип / категория / раса-потребитель) + редактор всех
    // блоков Unit (сворачиваемые фолды по [Header] — общий InterflowEditorUI) + секция доп. компонентов.
    // Шаг 2: визард «Создать юнита» (копия эталона), «Дублировать как», «Где используется».
    // Правило 1: ассет и SCEditor не правим — только чтение/запись сериализованных полей через SerializedObject
    // и штатные PrefabUtility/AssetDatabase. Правило 5: часть окна Interflow Editor; общая UI-логика — InterflowEditorUI.
    public static class InterflowUnitsTab
    {
        // ---- Состояние на сессию окна ----
        static List<Unit> allUnits = new List<Unit>();     // все юниты проекта (скан как SCEditor.LoadUnits)
        static List<Unit> shownUnits = new List<Unit>();   // после применения фильтров
        static Unit selected;

        const string ALL = "Все";
        static string typeFilter = ALL;       // значение — имя UnitType или ALL
        static string categoryFilter = ALL;   // значение — имя Unit.UnitCategory или ALL
        static string raceFilter = ALL;       // значение — имя ассета FactionConfig или ALL

        // Визард «Создать юнита» (шаг 2).
        static GameObject wizardTemplate;     // выбранный эталон (дефолт — из настроек)
        static string wizardName = "";        // имя нового юнита (для диалога сохранения)
        static bool wizardInit;               // подтянут ли дефолт эталона из настроек

        // Кэши на сессию окна (§9 промта: скан по всем ассетам дорог — кэшируем; сброс в RefreshUnitList).
        static Dictionary<string, HashSet<Unit>> raceUnitsCache;         // раса → её юниты (фильтр)
        static Dictionary<string, List<Ability>> usageAbilitiesCache;    // путь юнита → умения, ссылающиеся на него

        static VisualElement listContainer;   // левый список (перестраивается)
        static VisualElement rightPanel;       // правый редактор (перестраивается)
        static Label countLabel;

        // Доп. компоненты юнита — те же, что предлагает SCEditor.ComponentsButton, + наш AutoAbilityUser.
        static readonly (Type type, string title)[] OptionalComponents =
        {
            (typeof(AutoAbilityUser),  "AutoAbilityUser — авто-каст способности"),
            (typeof(LevelingUnit),     "LevelingUnit — уровни и опыт"),
            (typeof(ConstructionUnit), "ConstructionUnit — постройка/здание"),
            (typeof(ResourceUnit),     "ResourceUnit — ресурсы"),
            (typeof(TransportUnit),    "TransportUnit — транспорт"),
            (typeof(LifetimeUnit),     "LifetimeUnit — время жизни"),
            (typeof(AttributeUnit),    "AttributeUnit — атрибуты"),
        };

        // ======================== ТОЧКА ВХОДА ВКЛАДКИ ========================

        public static VisualElement CreateTabUI()
        {
            RefreshUnitList();
            ApplyFilters();
            if (selected == null || !allUnits.Contains(selected))
                selected = shownUnits.Count > 0 ? shownUnits[0] : null;

            var root = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, flexGrow = 1, minHeight = 500, marginTop = 6, marginLeft = 6, marginRight = 6 }
            };

            // ----- Левая колонка: визард + фильтры + список -----
            var left = new VisualElement { style = { width = 300, flexShrink = 0, marginRight = 8 } };
            left.Add(BuildWizard());
            left.Add(new Button(() => { RefreshUnitList(); ApplyFilters(); RebuildList(); RebuildRightPanel(); }) { text = "Обновить список" });
            left.Add(BuildFilters());
            countLabel = new Label { style = { marginTop = 2, marginBottom = 2, color = new Color(0.7f, 0.72f, 0.75f) } };
            left.Add(countLabel);

            var listScroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1, marginTop = 2 } };
            listContainer = new VisualElement();
            listScroll.Add(listContainer);
            left.Add(listScroll);

            // ----- Правая колонка: редактор -----
            var rightScroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rightPanel = new VisualElement();
            rightScroll.Add(rightPanel);

            root.Add(left);
            root.Add(rightScroll);

            RebuildList();
            RebuildRightPanel();
            return root;
        }

        // ======================== ВИЗАРД «СОЗДАТЬ ЮНИТА» (копия эталона) ========================

        static VisualElement BuildWizard()
        {
            var foldout = new Foldout { text = "Создать юнита (копия эталона)", value = false, style = { marginBottom = 4 } };

            if (!wizardInit)
            {
                wizardTemplate = InterflowEditorSettings.GetOrCreate().unitTemplatePrefab; // дефолт эталона из настроек (правило 3)
                wizardInit = true;
            }

            var templateField = new ObjectField("Эталон (Unit-префаб)") { objectType = typeof(GameObject), value = wizardTemplate };
            templateField.RegisterValueChangedCallback(e => wizardTemplate = e.newValue as GameObject);
            foldout.Add(templateField);

            var nameField = new TextField("Имя юнита") { value = wizardName };
            nameField.RegisterValueChangedCallback(e => wizardName = e.newValue);
            foldout.Add(nameField);

            foldout.Add(new Button(() => CreateUnitByCopy(wizardTemplate, wizardName)) { text = "Создать" });
            foldout.Add(new Label("Новый юнит = копия эталона (наследует меш и поля). Путь/имя файла — в диалоге сохранения; " +
                                  "unitTypeID переуникализируется. Остальные поля правь в редакторе справа.")
                { style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.7f, 0.72f, 0.75f), marginTop = 2 } });

            return foldout;
        }

        // Копия префаба-эталона (визард) ИЛИ выбранного юнита («Дублировать как») → уникальный unitTypeID → выбрать.
        static void CreateUnitByCopy(GameObject template, string suggestedName)
        {
            if (template == null) { EditorUtility.DisplayDialog("Создать юнита", "Не выбран эталонный префаб.", "OK"); return; }
            if (template.GetComponent<Unit>() == null) { EditorUtility.DisplayDialog("Создать юнита", "Эталон не содержит компонент Unit.", "OK"); return; }

            string srcPath = AssetDatabase.GetAssetPath(template);
            if (string.IsNullOrEmpty(srcPath)) { EditorUtility.DisplayDialog("Создать юнита", "Эталон не является ассетом-префабом.", "OK"); return; }

            var settings = InterflowEditorSettings.GetOrCreate();
            InterflowEditorUI.EnsureFolder(settings.unitPrefabCreateFolder);

            string defaultName = string.IsNullOrEmpty(suggestedName) ? "NewUnit" : suggestedName;
            string dstPath = EditorUtility.SaveFilePanelInProject(
                "Создать юнита (копия эталона)", defaultName, "prefab",
                "Имя нового юнит-префаба (по конвенции — в Resources/UnitPrefabs)", settings.unitPrefabCreateFolder);
            if (string.IsNullOrEmpty(dstPath)) return;

            if (!AssetDatabase.CopyAsset(srcPath, dstPath))
            {
                Debug.LogWarning($"[InterflowUnitsTab] Не удалось скопировать '{srcPath}' → '{dstPath}'.");
                return;
            }

            // Детерминированно-уникальный unitTypeID (сверка по Resources/UnitPrefabs, как UnitDuplicateHandler;
            // id уникален → постпроцессор его не перетрёт — §9). Правим ассет через LoadPrefabContents.
            var root = PrefabUtility.LoadPrefabContents(dstPath);
            var u = root.GetComponent<Unit>();
            if (u != null) u.unitTypeID = NextFreeUnitId(dstPath);
            PrefabUtility.SaveAsPrefabAsset(root, dstPath);
            PrefabUtility.UnloadPrefabContents(root);

            RefreshUnitList();
            ApplyFilters();
            var newGO = AssetDatabase.LoadAssetAtPath<GameObject>(dstPath);
            selected = newGO != null ? newGO.GetComponent<Unit>() : null;
            RebuildList();
            RebuildRightPanel();
            if (newGO != null) EditorGUIUtility.PingObject(newGO);
        }

        // Свободный unitTypeID: сверка по Resources/UnitPrefabs — тот же набор, что у UnitDuplicateHandler (Resources.LoadAll).
        static int NextFreeUnitId(string excludeAssetPath)
        {
            var used = new HashSet<int>();
            foreach (var go in Resources.LoadAll<GameObject>("UnitPrefabs"))
            {
                if (go == null) continue;
                if (AssetDatabase.GetAssetPath(go) == excludeAssetPath) continue;
                var u = go.GetComponent<Unit>();
                if (u != null) used.Add(u.unitTypeID);
            }
            int id = 1;
            while (used.Contains(id)) id++;
            return id;
        }

        // ======================== ФИЛЬТРЫ ========================

        static VisualElement BuildFilters()
        {
            var box = new VisualElement { style = { marginTop = 4 } };

            // Тип — UnitType (имена типа ассета; английские по необходимости — правило 4).
            box.Add(MakeDropdown("Тип", BuildChoices(Enum.GetNames(typeof(UnitType))), typeFilter, null,
                v => { typeFilter = v; ApplyFilters(); RebuildList(); }));

            // Категория — Unit.UnitCategory (метки по-русски через [InspectorName]).
            box.Add(MakeDropdown("Категория", BuildChoices(Enum.GetNames(typeof(Unit.UnitCategory))), categoryFilter,
                s => s == ALL ? ALL : InterflowEditorUI.EnumLabel(typeof(Unit.UnitCategory), s),
                v => { categoryFilter = v; ApplyFilters(); RebuildList(); }));

            // Использует раса — FactionConfig (обратные ссылки).
            box.Add(MakeDropdown("Использует раса", BuildChoices(RaceNames().ToArray()), raceFilter, null,
                v => { raceFilter = v; ApplyFilters(); RebuildList(); }));

            return box;
        }

        static List<string> BuildChoices(string[] values)
        {
            var list = new List<string> { ALL };
            list.AddRange(values);
            return list;
        }

        static DropdownField MakeDropdown(string label, List<string> choices, string current, Func<string, string> format, Action<string> onChange)
        {
            int idx = Mathf.Max(0, choices.IndexOf(current));
            var dd = new DropdownField(label, choices, idx);
            if (format != null)
            {
                dd.formatListItemCallback = format;
                dd.formatSelectedValueCallback = format;
            }
            dd.RegisterValueChangedCallback(e => onChange(e.newValue));
            return dd;
        }

        // ======================== СКАН / ПРИМЕНЕНИЕ ФИЛЬТРОВ ========================

        // Скан всех префабов проекта с компонентом Unit — как SCEditor.LoadUnits (t:GameObject + GetComponent<Unit>).
        static void RefreshUnitList()
        {
            allUnits = AssetDatabase.FindAssets("t:GameObject")
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(go => go != null && go.GetComponent<Unit>() != null)
                .Select(go => go.GetComponent<Unit>())
                .OrderBy(u => u.name)
                .ToList();

            raceUnitsCache = null;        // сбросить кэши обратных ссылок (список мог измениться)
            usageAbilitiesCache = null;
        }

        static void ApplyFilters()
        {
            // Самокоррекция устаревших значений (раса могла исчезнуть после «Обновить»).
            if (raceFilter != ALL && !RaceNames().Contains(raceFilter)) raceFilter = ALL;

            IEnumerable<Unit> q = allUnits;

            if (typeFilter != ALL)
                q = q.Where(u => u.unitType.ToString() == typeFilter);

            if (categoryFilter != ALL)
                q = q.Where(u => u.unitCategory.ToString() == categoryFilter);

            if (raceFilter != ALL)
            {
                var set = RaceUnits(raceFilter);
                q = q.Where(u => set.Contains(u));
            }

            shownUnits = q.ToList();
        }

        // ======================== ЛЕВЫЙ СПИСОК ========================

        static void RebuildList()
        {
            if (listContainer == null) return;
            listContainer.Clear();

            if (countLabel != null)
                countLabel.text = $"Показано {shownUnits.Count} из {allUnits.Count}";

            foreach (var u in shownUnits)
            {
                var unit = u;
                string name = string.IsNullOrEmpty(u.unitName) ? u.name : u.unitName;
                var row = new Button(() => { selected = unit; RebuildList(); RebuildRightPanel(); })
                {
                    text = $"{name}  ·  {u.unitType}",
                    style = { unityTextAlign = TextAnchor.MiddleLeft, marginBottom = 1 }
                };
                if (unit == selected)
                {
                    row.style.unityFontStyleAndWeight = FontStyle.Bold;
                    row.style.borderLeftWidth = 3;
                    row.style.borderLeftColor = new Color(0.35f, 0.6f, 0.95f);
                }
                listContainer.Add(row);
            }

            if (shownUnits.Count == 0)
                listContainer.Add(new Label("Юнитов по текущим фильтрам нет.")
                    { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, color = new Color(0.7f, 0.7f, 0.7f) } });
        }

        // ======================== ПРАВЫЙ РЕДАКТОР ========================

        static void RebuildRightPanel()
        {
            if (rightPanel == null) return;
            rightPanel.Clear();

            if (selected == null)
            {
                rightPanel.Add(new Label("Выбери юнита слева или создай визардом.") { style = { marginTop = 6, whiteSpace = WhiteSpace.Normal } });
                return;
            }

            string path = AssetDatabase.GetAssetPath(selected);
            string name = string.IsNullOrEmpty(selected.unitName) ? selected.name : selected.unitName;

            // Заголовок: имя + «Обновить» + «Показать» + «Дублировать как».
            var headRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 } };
            headRow.Add(new Label(name) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, flexGrow = 1 } });
            headRow.Add(new Button(RebuildRightPanel) { text = "Обновить", tooltip = "Пересобрать редактор из текущего состояния префаба" });
            headRow.Add(new Button(() => { if (selected != null) EditorGUIUtility.PingObject(selected.gameObject); }) { text = "Показать" });
            headRow.Add(new Button(() => CreateUnitByCopy(selected.gameObject, selected.name + "_Copy")) { text = "Дублировать как" });
            rightPanel.Add(headRow);
            rightPanel.Add(new Label(path) { style = { color = new Color(0.6f, 0.6f, 0.6f), marginBottom = 6, whiteSpace = WhiteSpace.Normal } });

            // «Где используется» (обратные ссылки: фракции/волны + умения; сцены — вкладка «Чистка»).
            AddUsageSection(selected);

            // --- Поля Unit по блокам (сворачиваемые фолды по [Header], все видимые поля, включая Interflow-партиал) ---
            // UnitEditor (CustomEditor ассета) прячет часть полей за тумблером — здесь показываем всё,
            // т.к. это правка ПРЕФАБА (именно её ассет и рекомендует делать через редактор).
            rightPanel.Add(new Label("Поля юнита (Unit + Interflow):") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4, marginBottom = 2 } });
            var so = new SerializedObject(selected);
            rightPanel.Add(InterflowEditorUI.BuildGroupedFields(so));

            // --- Секция доп. компонентов ---
            AddComponentsSection(path);
        }

        static void AddComponentsSection(string prefabPath)
        {
            var box = new VisualElement { style = { marginTop = 8 } };
            box.Add(new Label("Доп. компоненты:") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 2 } });

            var go = selected.gameObject;
            foreach (var (type, title) in OptionalComponents)
            {
                var comp = go.GetComponent(type);
                var foldout = new Foldout { text = title, value = comp != null, style = { marginBottom = 2 } };

                if (comp != null)
                {
                    var cso = new SerializedObject(comp);
                    foldout.Add(InterflowEditorUI.BuildGroupedFields(cso));  // поля компонента тоже по блокам [Header]
                    foldout.Add(new Button(() => RemoveComponent(prefabPath, type)) { text = "Удалить компонент", style = { marginTop = 2 } });
                }
                else
                {
                    foldout.Add(new Button(() => AddComponentToPrefab(prefabPath, type)) { text = $"Добавить {type.Name}" });
                }

                box.Add(foldout);
            }

            rightPanel.Add(box);
        }

        // ======================== «ГДЕ ИСПОЛЬЗУЕТСЯ» ========================

        static void AddUsageSection(Unit unit)
        {
            var foldout = new Foldout { text = "Где используется", value = false, style = { marginBottom = 4 } };

            string unitPath = AssetDatabase.GetAssetPath(unit);
            int found = 0;

            foreach (var f in AllFactions())
            {
                var roles = FactionRolesOf(f, unit);
                if (roles.Count == 0) continue;
                found++;
                var fac = f;
                foldout.Add(UsageRow($"Фракция «{f.name}»: {string.Join(", ", roles)}", () => EditorGUIUtility.PingObject(fac)));
            }

            foreach (var a in AbilitiesUsing(unitPath))
            {
                found++;
                var ab = a;
                foldout.Add(UsageRow($"Умение «{a.name}» ссылается на юнита", () => EditorGUIUtility.PingObject(ab)));
            }

            if (found == 0)
                foldout.Add(new Label("Ссылок во фракциях и умениях не найдено (сцены здесь не сканируются — см. вкладку «Чистка»).")
                    { style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.7f, 0.7f, 0.7f) } });

            rightPanel.Add(foldout);
        }

        static VisualElement UsageRow(string text, Action ping)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 1 } };
            row.Add(new Label("● " + text) { style = { whiteSpace = WhiteSpace.Normal, flexGrow = 1 } });
            row.Add(new Button(ping) { text = "→", tooltip = "Показать в Project" });
            return row;
        }

        // Роли, в которых фракция ссылается на юнита (волна/доступные/башни/герой).
        static List<string> FactionRolesOf(FactionConfig f, Unit unit)
        {
            var roles = new List<string>();
            if (f.waveComposition != null && f.waveComposition.Any(w => w != null && w.unitToSpawn == unit)) roles.Add("волна");
            if (f.availableWaveUnits != null && f.availableWaveUnits.Contains(unit)) roles.Add("доступные волны");
            if (f.centreTower == unit) roles.Add("башня-центр");
            if (f.defence1Tower == unit) roles.Add("башня об.1");
            if (f.defence2Tower == unit) roles.Add("башня об.2");
            if (f.heroPrefab == unit) roles.Add("герой");
            return roles;
        }

        static List<Ability> AbilitiesUsing(string unitPath)
        {
            BuildAbilityUsageCache();
            return usageAbilitiesCache.TryGetValue(unitPath, out var l) ? l : new List<Ability>();
        }

        // Реверс-ссылки умений на юнитов через AssetDatabase.GetDependencies (прямые зависимости ассета умения).
        static void BuildAbilityUsageCache()
        {
            if (usageAbilitiesCache != null) return;
            usageAbilitiesCache = new Dictionary<string, List<Ability>>();

            var unitPaths = new HashSet<string>(allUnits.Select(u => AssetDatabase.GetAssetPath(u)));

            foreach (var a in AllAbilities())
            {
                string aPath = AssetDatabase.GetAssetPath(a);
                foreach (var dep in AssetDatabase.GetDependencies(aPath, false))
                {
                    if (!unitPaths.Contains(dep)) continue;
                    if (!usageAbilitiesCache.TryGetValue(dep, out var list)) { list = new List<Ability>(); usageAbilitiesCache[dep] = list; }
                    if (!list.Contains(a)) list.Add(a);
                }
            }
        }

        static IEnumerable<Ability> AllAbilities() =>
            AssetDatabase.FindAssets("t:Ability")
                .Select(g => AssetDatabase.LoadAssetAtPath<Ability>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null);

        // ======================== ДОП. КОМПОНЕНТЫ: ДОБАВИТЬ / УДАЛИТЬ ========================

        // Добавить/удалить компонент на АССЕТЕ префаба штатным PrefabUtility (правим ассет, не экземпляр сцены — §9).
        static void AddComponentToPrefab(string prefabPath, Type type)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root.GetComponent(type) == null) root.AddComponent(type);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            ReloadSelected(prefabPath);
            RebuildRightPanel();
        }

        static void RemoveComponent(string prefabPath, Type type)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            var comp = root.GetComponent(type);
            if (comp != null) UnityEngine.Object.DestroyImmediate(comp, true);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            ReloadSelected(prefabPath);
            RebuildRightPanel();
        }

        // Перечитать выбранный юнит по пути (после SaveAsPrefabAsset прежняя ссылка на компонент может устареть).
        static void ReloadSelected(string prefabPath)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            selected = go != null ? go.GetComponent<Unit>() : null;
            RefreshUnitList();
            ApplyFilters();
            RebuildList();
        }

        // ======================== ОБРАТНЫЕ ССЫЛКИ (раса → юниты, для фильтра) ========================

        static HashSet<Unit> RaceUnits(string raceName)
        {
            BuildRaceCache();
            return raceUnitsCache.TryGetValue(raceName, out var s) ? s : new HashSet<Unit>();
        }

        static void BuildRaceCache()
        {
            if (raceUnitsCache != null) return;
            raceUnitsCache = new Dictionary<string, HashSet<Unit>>();

            foreach (var f in AllFactions())
            {
                if (!raceUnitsCache.TryGetValue(f.name, out var set))
                {
                    set = new HashSet<Unit>();
                    raceUnitsCache[f.name] = set;
                }
                foreach (var u in FactionUnits(f))
                    if (u != null) set.Add(u);
            }
        }

        // Игровые юниты, на которые ссылается фракция (те же поля, что и в InterflowFactionTab.FactionUnits).
        static IEnumerable<Unit> FactionUnits(FactionConfig f)
        {
            if (f.waveComposition != null)
                foreach (var w in f.waveComposition) if (w != null) yield return w.unitToSpawn;
            if (f.availableWaveUnits != null)
                foreach (var u in f.availableWaveUnits) yield return u;
            yield return f.centreTower;
            yield return f.defence1Tower;
            yield return f.defence2Tower;
            yield return f.heroPrefab;
        }

        static IEnumerable<FactionConfig> AllFactions() =>
            AssetDatabase.FindAssets("t:FactionConfig")
                .Select(g => AssetDatabase.LoadAssetAtPath<FactionConfig>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(f => f != null);

        static List<string> RaceNames() =>
            AllFactions().Select(f => f.name).Distinct().OrderBy(n => n).ToList();
    }
}
