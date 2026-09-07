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

            // Волна 2.0 — единый список юнитов волны (роль Базовый/Доступный + count) + базовый доход.
            rightPanel.Add(Hint("Состав волны: каждая запись — юнит + роль + число копий. Роль Базовый — выходит каждую " +
                                "волну бесплатно; Доступный — игрок помечает [Авто]/[Разовый] за золото (технологии открывают " +
                                "доп. юнитов поверх, у них по 1 копии). Доход волны — таблица waveIncomeByLevel: " +
                                "золото, начисляемое в момент призыва волны, ИНДЕКС = УРОВЕНЬ ГЗ (элемент 0 — уровень 0). " +
                                "У таблицы душ индексация другая (элемент 0 = уровень 1) — не перепутай. Таблица должна " +
                                "быть неубывающей; пустая — доход 0."));
            AddSection(so, "Состав волны (Волна 2.0)", "waveUnits", "waveIncomeByLevel");
            rightPanel.Add(Hint("Умения главного здания: панель — ОДИН ряд из трёх ячеек. Здесь задаются только " +
                                "СТАРТОВЫЕ умения (доступны с начала матча), порядок = номер ячейки: первое — левая, " +
                                "второе — средняя, третье — правая; больше трёх класть некуда. Умения, которые открываются " +
                                "по ходу матча, задаются на узле дерева технологий (блок «Что открывает узел» → умение + " +
                                "номер ячейки) — там же, где узел открывает юнитов и подменяет префабы."));
            AddSection(so, "Стартовые умения главного здания", "centralAbilities");
            AddSection(so, "Герой", "heroPrefab");

            // Технологии (тиры) — дерево «Технологии 2.0»: тир = улучшение уровня → большой выбор А/Б → специализация 1 из 2.
            // Один PropertyField рисует всю вложенную структуру (фолдауты тиров, кнопки +/− тира) с тултипами из FactionConfig.
            // Technology-ассеты создаются во вкладке «Справочники» (решение §10 — пикер + Hint, без per-cell создания).
            rightPanel.Add(Hint("Технологии (тиры): каждый тир = улучшение уровня → большой выбор А/Б → специализация 1 из 2 " +
                                "(невыбранные альтернативы блокируются навсегда в матче). Узлы ссылаются на Technology из " +
                                "Resources/Technology — создавай их во вкладке «Справочники»; иконка и цена задаются на узле. " +
                                "Префаб героя (heroPrefab) у варианта — только если вариант открывает героя. " +
                                "Число тиров = размер массива techTiers (кнопки +/−). " +
                                "ЧТО ОТКРЫВАЕТ узел — задаётся на самом узле: юниты волны (unlockUnits), умения ГЗ " +
                                "(умение + номер ячейки), подмена префабов юнитов и башен. Отдельного списка правил " +
                                "с условиями больше нет: условие всегда одно — узел куплен."));
            AddSection(so, "Технологии (тиры)", "techTiers");

            AddSection(so, "Башни по типу точки", "centreTower", "defence1Tower", "defence2Tower");
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
            if (f.waveUnits != null)
                foreach (var e in f.waveUnits) if (e != null) yield return e.unit;
            yield return f.centreTower;
            yield return f.defence1Tower;
            yield return f.defence2Tower;
            yield return f.heroPrefab;
        }

        static bool InResources(string assetPath, string subfolder) =>
            !string.IsNullOrEmpty(assetPath) && assetPath.Replace('\\', '/').Contains("/Resources/" + subfolder + "/");

        // ======================== ТЕХНОЛОГИИ (тиры) ========================
        // Секция дерева технологий (тиры, «Технологии 2.0») рисуется в RebuildRightPanel через AddSection("techTiers")
        // — один PropertyField на всю вложенную структуру (решение §10: пикер + Hint, без per-cell создания;
        // Technology-ассеты создаются во вкладке «Справочники»). Прежняя рядная сетка «ветка × уровень» снесена
        // при переходе на тиры (2026-07-21).

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
