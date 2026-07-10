using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============================= INTERFLOW EDITOR — ВКЛАДКА «ФРАКЦИИ» (фаза E3) ==
    // Весь контент расы (FactionConfig) одним экраном + создание фракции-заготовки + связка со сценой
    // (GameManager.factionData — шаг 3). Правило 1: ассет (FactionConfig/GameManager/Player) НЕ правим —
    // работаем только чтением/записью сериализованных полей через SerializedObject (Undo/SetDirty).
    // Правило 5: часть окна Interflow Editor, отдельных окон не плодим. Правило 4: UI по-русски.
    // Разделы — PropertyField (дёшево, всегда актуально при смене полей конфига); ветки техов — кастомной
    // сеткой «ветка × уровень» + мост ГЗ. Ничего в рантайме/данных не добавляем — только редактируем (правило 7).
    public static class InterflowFactionTab
    {
        // ---- Состояние на сессию окна ----
        static List<FactionConfig> factions = new List<FactionConfig>();
        static FactionConfig selected;

        static VisualElement listContainer;   // левый список фракций (перестраивается)
        static VisualElement rightPanel;      // правая часть — разделы выбранной фракции (перестраивается)

        // ======================== ТОЧКА ВХОДА ВКЛАДКИ ========================

        public static VisualElement CreateTabUI()
        {
            RefreshFactionList();
            if (selected == null || !factions.Contains(selected))
                selected = factions.Count > 0 ? factions[0] : null;

            var root = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, flexGrow = 1, minHeight = 500,
                    marginTop = 6, marginLeft = 6, marginRight = 6
                }
            };

            // ----- Левая колонка: кнопки + список фракций -----
            var left = new VisualElement { style = { width = 240, flexShrink = 0, marginRight = 8 } };
            left.Add(new Button(CreateNewFaction) { text = "Новая фракция" });
            left.Add(new Button(() => { RefreshFactionList(); RebuildList(); }) { text = "Обновить список" });

            var listScroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1, marginTop = 4 } };
            listContainer = new VisualElement();
            listScroll.Add(listContainer);
            left.Add(listScroll);

            // ----- Правая колонка: разделы выбранной фракции -----
            var rightScroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rightPanel = new VisualElement();
            rightScroll.Add(rightPanel);

            root.Add(left);
            root.Add(rightScroll);

            RebuildList();
            RebuildRightPanel();
            return root;
        }

        // ======================== ЛЕВЫЙ СПИСОК ФРАКЦИЙ ========================

        static void RefreshFactionList()
        {
            factions = AssetDatabase.FindAssets("t:FactionConfig")
                .Select(g => AssetDatabase.LoadAssetAtPath<FactionConfig>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(f => f != null)
                .OrderBy(f => f.name)
                .ToList();
        }

        static void RebuildList()
        {
            if (listContainer == null) return;
            listContainer.Clear();

            foreach (var f in factions)
            {
                var fac = f;
                var row = new Button(() => { selected = fac; RebuildList(); RebuildRightPanel(); })
                {
                    text = f.name,
                    style = { unityTextAlign = TextAnchor.MiddleLeft, marginBottom = 1 }
                };
                if (fac == selected)
                {
                    row.style.unityFontStyleAndWeight = FontStyle.Bold;
                    row.style.borderLeftWidth = 3;
                    row.style.borderLeftColor = new Color(0.35f, 0.6f, 0.95f);
                }
                listContainer.Add(row);
            }

            if (factions.Count == 0)
                listContainer.Add(new Label("Фракций (FactionConfig) в проекте нет — создай кнопкой выше.")
                {
                    style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, color = new Color(0.7f, 0.7f, 0.7f) }
                });
        }

        // ======================== ПРАВАЯ ЧАСТЬ (РАЗДЕЛЫ) ========================

        static void RebuildRightPanel()
        {
            if (rightPanel == null) return;
            rightPanel.Clear();

            if (selected == null)
            {
                rightPanel.Add(new Label("Выбери фракцию слева или создай новую.")
                    { style = { marginTop = 6, whiteSpace = WhiteSpace.Normal } });
                return;
            }

            var so = new SerializedObject(selected);

            // Заголовок: имя ассета + путь + «Обновить» (пересобрать панель из текущего состояния ассета).
            var headRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 } };
            headRow.Add(new Label(selected.name) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, flexGrow = 1 } });
            headRow.Add(new Button(RebuildRightPanel) { text = "Обновить", tooltip = "Пересобрать сетку и проверки из текущего состояния ассета" });
            rightPanel.Add(headRow);
            rightPanel.Add(new Label(AssetDatabase.GetAssetPath(selected))
                { style = { color = new Color(0.6f, 0.6f, 0.6f), marginBottom = 6, whiteSpace = WhiteSpace.Normal } });

            // Подсказка про scene-bound поля (решение Artsiom 09.07, §10 — read-only, без редактирования здесь).
            rightPanel.Add(Hint("Поля сцены — abilityCaster, spawnGrid, spawnPoint, блок марша — задаются в MatchManager " +
                                "(TeamWaveConfig на команду), а не здесь: они привязаны к объектам открытой сцены. " +
                                "Здесь — только контент расы (FactionConfig)."));

            // Быстрые (inline) проверки §5: юнит вне Resources — красный; icon[0] пуст — жёлтый; герой без LevelingUnit — красный.
            AddStatusBlock(selected);

            // Разделы FactionConfig через PropertyField (foldout'ы повторяют [Header] конфига).
            AddSection(so, "Состав волны", "waveComposition");
            AddSection(so, "Доступные юниты волны (апгрейды)", "availableWaveUnits");
            AddSection(so, "Способности центральной таблицы", "centralAbilities");
            AddSection(so, "Герой", "heroPrefab");

            // Технологии — кастомная сетка «ветка × уровень» + мост ГЗ + «Создать тех».
            AddTechSection(so);

            AddSection(so, "Башни по типу точки", "centreTower", "defence1Tower", "defence2Tower");
            AddSection(so, "Апгрейды контента (техи / уровень ГЗ)", "contentUnlockRules");
            AddSection(so, "Могилки", "gravePrefab", "graveLifetime");

            // Привязка — ПОФОЛДЕРНО (каждый foldout биндится к своему SerializedObject внутри Add*-методов):
            // контент фракции (so) и реестр рас сцены (gso в AddSceneBlock) не делят один контекст биндинга,
            // без неоднозначности вложенных контекстов (правило 8). Правки идут с Undo.
            AddSceneBlock();
        }

        // Foldout с набором PropertyField по именам сериализованных полей.
        static void AddSection(SerializedObject so, string title, params string[] propNames)
        {
            var foldout = new Foldout { text = title, value = true, style = { marginBottom = 4 } };
            foreach (var name in propNames)
            {
                var prop = so.FindProperty(name);
                if (prop == null)
                {
                    foldout.Add(new Label($"[поле «{name}» не найдено — переименовано?]")
                        { style = { color = new Color(0.95f, 0.5f, 0.5f) } });
                    continue;
                }
                foldout.Add(new PropertyField(prop));
            }
            rightPanel.Add(foldout);
            foldout.Bind(so);   // привязка раздела к конфигу фракции (Undo)
        }

        // ======================== БЫСТРЫЕ ПРОВЕРКИ (inline-статусы §5) ========================
        // Замечания те же, что у валидатора E1 (переиспользуем логику, сам валидатор не трогаем — правило 7).

        static void AddStatusBlock(FactionConfig f)
        {
            var box = new VisualElement { style = { marginBottom = 8 } };
            box.Add(new Label("Быстрые проверки:") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 2 } });

            var issues = new List<(string msg, bool error)>();

            // 1. Юниты фракции вне Resources/UnitPrefabs → красный (игра не загрузит; GameManager.cs LoadAll).
            foreach (var u in FactionUnits(f))
            {
                if (u == null) continue;
                if (!InResources(AssetDatabase.GetAssetPath(u), "UnitPrefabs"))
                    issues.Add(($"Юнит «{u.name}» вне Resources/UnitPrefabs — игра его не загрузит.", true));
            }

            // 2. Способности центральной таблицы без иконки icon[0] → жёлтый (пустая ячейка в таблице).
            if (f.centralAbilities != null)
                foreach (var a in f.centralAbilities)
                    if (a != null && (a.icon == null || a.icon.Length == 0 || a.icon[0] == null))
                        issues.Add(($"Умение «{a.name}» без иконки icon[0] — в таблице ГЗ будет пустая ячейка.", false));

            // 3. Герой задан, но без LevelingUnit → красный (уровни/умения по уровню не работают).
            if (f.heroPrefab != null && f.heroPrefab.GetComponent<LevelingUnit>() == null)
                issues.Add(($"Герой «{f.heroPrefab.name}» без LevelingUnit — опыт/уровни и умения по уровню не работают.", true));

            if (issues.Count == 0)
                box.Add(new Label("замечаний нет") { style = { color = new Color(0.5f, 0.8f, 0.5f) } });
            else
                foreach (var (msg, error) in issues)
                    box.Add(new Label("● " + msg)
                    {
                        style =
                        {
                            whiteSpace = WhiteSpace.Normal,
                            color = error ? new Color(0.95f, 0.4f, 0.38f) : new Color(0.98f, 0.78f, 0.28f)
                        }
                    });

            rightPanel.Add(box);
        }

        // Игровые юниты, на которые ссылается фракция (для проверки Resources/UnitPrefabs).
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

        static bool InResources(string assetPath, string subfolder) =>
            !string.IsNullOrEmpty(assetPath) && assetPath.Replace('\\', '/').Contains("/Resources/" + subfolder + "/");

        // ======================== ТЕХНОЛОГИИ: СЕТКА + МОСТ ГЗ ========================

        static void AddTechSection(SerializedObject so)
        {
            var foldout = new Foldout { text = "Технологии / улучшение ГЗ", value = true, style = { marginBottom = 4 } };

            var branchesProp = so.FindProperty("techBranches");

            foldout.Add(new Label("Дерево техов (столбцы — ветки, ряды — уровни). Пустая ячейка → «+тех» или перетащи Technology.")
                { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 4, color = new Color(0.7f, 0.72f, 0.75f) } });

            if (branchesProp != null && branchesProp.arraySize > 0)
                foldout.Add(BuildTechGrid(so, branchesProp));
            else
                foldout.Add(new Label("Веток нет. Задай размер массива «Ветки (структура…)» ниже, затем жми «Обновить».")
                    { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 4, color = new Color(0.85f, 0.7f, 0.4f) } });

            // Полный редактор структуры/иконок/цен веток — обычным PropertyField (сетка выше правит только ссылку technology).
            foldout.Add(new PropertyField(branchesProp, "Ветки (структура, иконки, цены)"));

            // Стоимости улучшения ГЗ.
            foldout.Add(new PropertyField(so.FindProperty("mainBuildingUpgradeCosts")));

            // Мост «уровень ГЗ → тех».
            foldout.Add(Hint("Мост «уровень ГЗ → тех»: скрытые техи уровней ГЗ. НЕ добавляй их в ветки выше — они скрыты из " +
                             "угловой таблицы, их видит только Required Tech. Резолв по индексу: 0 → уровень 2, 1 → 3, 2 → 4, 3 → 5. " +
                             "Техи должны лежать в Resources/Technology."));
            foldout.Add(BuildBridge(so));

            rightPanel.Add(foldout);
            foldout.Bind(so);   // привязка сетки/моста/PropertyField'ов раздела к конфигу фракции (Undo)
        }

        static VisualElement BuildTechGrid(SerializedObject so, SerializedProperty branchesProp)
        {
            const float cellW = 160f;
            const float rowHeadW = 54f;

            int branchCount = branchesProp.arraySize;
            int levelCount = 0;
            for (int b = 0; b < branchCount; b++)
            {
                var levels = branchesProp.GetArrayElementAtIndex(b).FindPropertyRelative("levels");
                if (levels != null) levelCount = Mathf.Max(levelCount, levels.arraySize);
            }

            var grid = new VisualElement { style = { marginBottom = 6 } };

            // Заголовок столбцов.
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };
            header.Add(CellLabel("", rowHeadW));
            for (int b = 0; b < branchCount; b++)
                header.Add(CellLabel($"Ветка {b + 1}", cellW));
            grid.Add(header);

            if (levelCount == 0)
                grid.Add(new Label("В ветках нет уровней — задай длину «levels» в структуре ниже.")
                    { style = { color = new Color(0.85f, 0.7f, 0.4f), whiteSpace = WhiteSpace.Normal } });

            for (int l = 0; l < levelCount; l++)
            {
                var rowEl = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 1, alignItems = Align.Center } };
                rowEl.Add(CellLabel($"Ур. {l + 1}", rowHeadW));
                for (int b = 0; b < branchCount; b++)
                {
                    var levels = branchesProp.GetArrayElementAtIndex(b).FindPropertyRelative("levels");
                    if (levels == null || l >= levels.arraySize)
                    {
                        rowEl.Add(new VisualElement { style = { width = cellW, marginRight = 2 } }); // нет такого уровня в ветке
                        continue;
                    }
                    var techProp = levels.GetArrayElementAtIndex(l).FindPropertyRelative("technology");
                    rowEl.Add(BuildTechCell(techProp, cellW));
                }
                grid.Add(rowEl);
            }

            return grid;
        }

        // Ячейка сетки: ObjectField(Technology, bind) + «+тех» когда пусто.
        static VisualElement BuildTechCell(SerializedProperty techProp, float width)
        {
            var cell = new VisualElement { style = { width = width, marginRight = 2, flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var of = new ObjectField { objectType = typeof(Technology), bindingPath = techProp.propertyPath, style = { flexGrow = 1 } };
            cell.Add(of);
            if (techProp.objectReferenceValue == null)
            {
                var path = techProp.propertyPath;   // копия для замыкания
                cell.Add(new Button(() => CreateTechIntoPath(path)) { text = "+тех", tooltip = "Создать Technology и подставить в ячейку", style = { flexShrink = 0 } });
            }
            return cell;
        }

        static Label CellLabel(string text, float width) => new Label(text)
        {
            style =
            {
                width = width, flexShrink = 0, marginRight = 2,
                unityFontStyleAndWeight = FontStyle.Bold, unityTextAlign = TextAnchor.MiddleLeft
            }
        };

        // Некликабельная подсказка (приглушённый блок).
        static Label Hint(string text) => new Label(text)
        {
            style =
            {
                whiteSpace = WhiteSpace.Normal, marginBottom = 8,
                paddingTop = 4, paddingBottom = 4, paddingLeft = 6, paddingRight = 6,
                backgroundColor = new Color(0.22f, 0.24f, 0.28f), color = new Color(0.8f, 0.82f, 0.85f)
            }
        };

        static VisualElement BuildBridge(SerializedObject so)
        {
            var bridgeProp = so.FindProperty("mainBuildingLevelTechs");
            var box = new VisualElement { style = { marginBottom = 4 } };

            if (bridgeProp == null)
            {
                box.Add(new Label("[поле mainBuildingLevelTechs не найдено]") { style = { color = new Color(0.95f, 0.5f, 0.5f) } });
                return box;
            }

            for (int i = 0; i < bridgeProp.arraySize; i++)
            {
                var elemProp = bridgeProp.GetArrayElementAtIndex(i);
                var rowEl = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 1 } };
                rowEl.Add(CellLabel($"Уровень ГЗ {i + 2}", 110));
                var of = new ObjectField { objectType = typeof(Technology), bindingPath = elemProp.propertyPath, style = { flexGrow = 1 } };
                rowEl.Add(of);
                if (elemProp.objectReferenceValue == null)
                {
                    var path = elemProp.propertyPath;   // копия для замыкания
                    rowEl.Add(new Button(() => CreateTechIntoPath(path)) { text = "+тех", style = { flexShrink = 0 } });
                }
                box.Add(rowEl);
            }

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 2 } };
            buttons.Add(new Button(() =>
            {
                bridgeProp.arraySize++;
                bridgeProp.serializedObject.ApplyModifiedProperties();
                RebuildRightPanel();
            }) { text = "+ уровень ГЗ" });
            buttons.Add(new Button(() =>
            {
                if (bridgeProp.arraySize > 0)
                {
                    bridgeProp.arraySize--;
                    bridgeProp.serializedObject.ApplyModifiedProperties();
                    RebuildRightPanel();
                }
            }) { text = "– уровень ГЗ" });
            box.Add(buttons);

            return box;
        }

        // ======================== СОЗДАНИЕ TECHNOLOGY ИЗ ЯЧЕЙКИ/МОСТА ========================

        // path — propertyPath целевого objectReference поля (ячейка ветки или элемент моста) в ассете выбранной фракции.
        static void CreateTechIntoPath(string path)
        {
            if (selected == null) return;

            var settings = InterflowEditorSettings.GetOrCreate();
            EnsureFolder(settings.technologyCreateFolder);

            string assetPath = EditorUtility.SaveFilePanelInProject(
                "Создать технологию", "Technology", "asset",
                "Имя новой технологии (ляжет в Resources/Technology)", settings.technologyCreateFolder);
            if (string.IsNullOrEmpty(assetPath)) return;

            var tech = ScriptableObject.CreateInstance<Technology>();
            tech.id = NextFreeTechId();                 // уникальный id (сверка по всем t:Technology, как валидатор E1)
            AssetDatabase.CreateAsset(tech, assetPath); // ничего сверх Technology не заполняем (правило 7)
            AssetDatabase.SaveAssets();

            // Подставить в целевую ячейку через SerializedProperty с Undo.
            var so = new SerializedObject(selected);
            var prop = so.FindProperty(path);
            if (prop != null)
            {
                prop.objectReferenceValue = tech;
                so.ApplyModifiedProperties();           // регистрирует Undo
            }
            else
            {
                Debug.LogWarning($"[InterflowFactionTab] Не нашёл поле '{path}' у «{selected.name}» — тех создан, но не подставлен.");
            }

            RebuildRightPanel();
            EditorGUIUtility.PingObject(tech);
        }

        // Свободный Technology.id: сверка по ВСЕМ ассетам Technology проекта (как штатный TechnologyIDDrawer, но детерминированно).
        static int NextFreeTechId()
        {
            var used = new HashSet<int>();
            foreach (var g in AssetDatabase.FindAssets("t:Technology"))
            {
                var t = AssetDatabase.LoadAssetAtPath<Technology>(AssetDatabase.GUIDToAssetPath(g));
                if (t != null) used.Add(t.id);
            }
            int id = 1;
            while (used.Contains(id)) id++;
            return id;
        }

        // ======================== БЛОК «СЦЕНА» (GameManager.factionData) ========================

        static void AddSceneBlock()
        {
            var foldout = new Foldout { text = "Сцена: реестр рас (GameManager.factionData)", value = true, style = { marginTop = 8, marginBottom = 4 } };

            var gm = Object.FindObjectOfType<GameManager>();
            if (gm == null)
            {
                foldout.Add(new Label("В открытой сцене нет GameManager — открой сцену матча, чтобы увидеть и дополнить реестр рас.")
                    { style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.85f, 0.7f, 0.4f) } });
                rightPanel.Add(foldout);
                return;
            }

            var gso = new SerializedObject(gm);
            var fdProp = gso.FindProperty("factionData");
            if (fdProp == null)
            {
                foldout.Add(new Label("[поле factionData не найдено у GameManager]") { style = { color = new Color(0.95f, 0.5f, 0.5f) } });
                rightPanel.Add(foldout);
                return;
            }

            var addBtn = new Button(() => AddSelectedFactionToScene(gm)) { text = "Добавить расу в сцену" };
            addBtn.SetEnabled(selected != null);
            foldout.Add(addBtn);
            foldout.Add(new Label("Добавит выбранную фракцию: имя = имя ассета, config = ассет; UnitsForSpawn (старт-юниты) — руками. " +
                                  "Сцена помечается изменённой, сохранение — руками (Ctrl+S).")
                { style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.7f, 0.72f, 0.75f), marginBottom = 4 } });

            foldout.Add(new PropertyField(fdProp));
            rightPanel.Add(foldout);

            // Своя привязка (GameManager сцены), отдельно от привязки конфига фракции.
            foldout.Bind(gso);
        }

        static void AddSelectedFactionToScene(GameManager gm)
        {
            if (selected == null || gm == null) return;

            var gso = new SerializedObject(gm);
            var fdProp = gso.FindProperty("factionData");
            if (fdProp == null || !fdProp.isArray) return;

            int idx = fdProp.arraySize;
            fdProp.arraySize = idx + 1;
            var elem = fdProp.GetArrayElementAtIndex(idx);

            // Новый элемент копирует предыдущий (штатное поведение SerializedProperty при росте массива) —
            // заполняем нужное и ЧИСТИМ UnitsForSpawn, чтобы не продублировать старт-юниты соседней расы.
            var nameProp   = elem.FindPropertyRelative("factionName");
            var configProp = elem.FindPropertyRelative("config");
            var unitsProp  = elem.FindPropertyRelative("UnitsForSpawn");
            if (nameProp != null)   nameProp.stringValue = selected.name;         // имя = имя ассета (решение Artsiom 09.07)
            if (configProp != null) configProp.objectReferenceValue = selected;   // ссылка на конфиг расы
            if (unitsProp != null && unitsProp.isArray) unitsProp.arraySize = 0;  // старт-юниты — руками

            gso.ApplyModifiedProperties();                            // Undo
            EditorUtility.SetDirty(gm);
            EditorSceneManager.MarkSceneDirty(gm.gameObject.scene);   // сцена dirty; сохранение — руками
            RebuildRightPanel();
            Debug.Log($"[InterflowFactionTab] В factionData сцены добавлена раса «{selected.name}» (config задан; UnitsForSpawn — руками).");
        }

        // ======================== СОЗДАНИЕ ФРАКЦИИ-ЗАГОТОВКИ ========================

        static void CreateNewFaction()
        {
            var settings = InterflowEditorSettings.GetOrCreate();
            EnsureFolder(settings.factionCreateFolder);

            string path = EditorUtility.SaveFilePanelInProject(
                "Новая фракция (FactionConfig)", "FactionConfig", "asset",
                "Имя нового ассета фракции", settings.factionCreateFolder);
            if (string.IsNullOrEmpty(path)) return;

            var asset = ScriptableObject.CreateInstance<FactionConfig>();
            AssetDatabase.CreateAsset(asset, path);   // все разделы — дефолты SO (пустые)
            AssetDatabase.SaveAssets();

            RefreshFactionList();
            selected = asset;
            RebuildList();
            RebuildRightPanel();
            EditorGUIUtility.PingObject(asset);
        }

        // Создать папку (по частям), если её нет.
        static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return;

            var parts = folder.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets") return;
            string cur = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
