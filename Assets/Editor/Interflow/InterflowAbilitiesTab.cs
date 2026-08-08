using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============================= INTERFLOW EDITOR — ВКЛАДКА «УМЕНИЯ И ЭФФЕКТОРЫ» (E4, шаг 3) ==
    // Список Ability всех типов (скан по CreateAssetMenu, как SCEditor.GetAbilityTypes) + Effectors; создание в путь
    // из настроек с уникальным id; редактор полей по [Header]-блокам (общий InterflowEditorUI); «кто использует»
    // (юниты abilities[] и список авто-способностей AutoAbilityUser, фракции: стартовые centralAbilities + узлы
    // дерева) + предупреждение «авто-способность не в abilities[]». Правило 1: ассет/SCEditor не правим
    // (паттерн GetAbilityTypes воспроизведён, не вызываем SCEditor).
    // Правило 5: часть окна Interflow Editor. Правило 4: UI по-русски.
    public static class InterflowAbilitiesTab
    {
        enum Mode { Abilities, Effectors }
        static Mode mode = Mode.Abilities;

        static List<Ability> abilities = new List<Ability>();
        static List<Effector> effectors = new List<Effector>();
        static UnityEngine.Object selected;   // Ability или Effector

        // К1: создание в два шага — сначала группа, потом конкретный тип карточкой.
        static InterflowAbilityGroups.Group createGroup = InterflowAbilityGroups.Group.Combat;
        static bool createOpen;

        // С1: раскрытость групп списка между пересборками.
        static readonly Dictionary<InterflowAbilityGroups.Group, bool> groupOpen =
            new Dictionary<InterflowAbilityGroups.Group, bool>();

        static readonly InterflowAbilityGroups.Group[] GROUPS =
        {
            InterflowAbilityGroups.Group.Combat,
            InterflowAbilityGroups.Group.Passive,
            InterflowAbilityGroups.Group.Production
        };

        // Кэши «кто использует» на сессию окна (§9 промта).
        static Dictionary<Ability, List<UsageRec>> abilityUsageCache;
        static Dictionary<string, List<UsageRec>> effectorUsageCache; // ключ — путь ассета эффектора

        static VisualElement listContainer, rightPanel;
        static VisualElement toggleHolder, createHolder;   // держатели для пересборки при смене режима
        static Label countLabel;

        struct UsageRec { public string text; public Action ping; public bool warn; }

        // ======================== ТОЧКА ВХОДА ВКЛАДКИ ========================

        public static VisualElement CreateTabUI()
        {
            RefreshLists();
            if (!IsSelectedValid()) selected = FirstOfMode();

            var root = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, flexGrow = 1, minHeight = 500, marginTop = 6, marginLeft = 6, marginRight = 6 }
            };

            var left = new VisualElement { style = { width = 320, flexShrink = 0, marginRight = 8 } };
            toggleHolder = new VisualElement();
            toggleHolder.Add(BuildModeToggle());
            left.Add(toggleHolder);
            createHolder = new VisualElement();
            createHolder.Add(BuildCreateControls());
            left.Add(createHolder);
            left.Add(new Button(() => { RefreshLists(); RebuildList(); RebuildRightPanel(); }) { text = "Обновить список" });
            countLabel = new Label { style = { marginTop = 2, marginBottom = 2, color = new Color(0.7f, 0.72f, 0.75f) } };
            left.Add(countLabel);

            var listScroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1, marginTop = 2 } };
            listContainer = new VisualElement();
            listScroll.Add(listContainer);
            left.Add(listScroll);

            var rightScroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rightPanel = new VisualElement();
            rightScroll.Add(rightPanel);

            root.Add(left);
            root.Add(rightScroll);

            RebuildList();
            RebuildRightPanel();
            return root;
        }

        // ======================== ПЕРЕКЛЮЧАТЕЛЬ РЕЖИМА ========================

        static VisualElement BuildModeToggle()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };

            row.Add(ModeButton("Умения", Mode.Abilities));
            row.Add(ModeButton("Эффекторы", Mode.Effectors));
            return row;
        }

        static Button ModeButton(string text, Mode target)
        {
            var b = new Button(() =>
            {
                if (mode == target) return;
                mode = target;
                selected = FirstOfMode();
                // перестроить всю вкладку (кнопки создания зависят от режима) — проще пересоздать содержимое окна вкладки.
                RebuildAll();
            })
            { text = text, style = { flexGrow = 1 } };

            if (mode == target)
            {
                b.style.unityFontStyleAndWeight = FontStyle.Bold;
                b.style.backgroundColor = new Color(0.28f, 0.36f, 0.5f);
            }
            return b;
        }

        // Переключение режима меняет кнопки создания и список — пересобираем держатели на месте.
        static void RebuildAll()
        {
            if (toggleHolder != null) { toggleHolder.Clear(); toggleHolder.Add(BuildModeToggle()); }
            if (createHolder != null) { createHolder.Clear(); createHolder.Add(BuildCreateControls()); }
            RebuildList();
            RebuildRightPanel();
        }

        // ======================== СОЗДАНИЕ ========================

        static VisualElement BuildCreateControls()
        {
            var box = new VisualElement { style = { marginBottom = 4 } };

            if (mode != Mode.Abilities)
            {
                box.Add(new Button(CreateEffector) { text = "Создать эффектор" });
                return box;
            }

            var types = AbilityTypes();
            if (types.Count == 0)
            {
                box.Add(new Label("Типов умений с [CreateAssetMenu] не найдено.")
                    { style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.85f, 0.7f, 0.4f) } });
                return box;
            }

            // К1: шаг 1 — «что делаем». Группы те же, что и в списке (правило 5: одна классификация).
            var fold = new Foldout { text = "Создать умение", value = createOpen };
            fold.RegisterValueChangedCallback(e => { if (e.target == fold) createOpen = e.newValue; });

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };
            foreach (var g in GROUPS) row.Add(CreateGroupButton(g));
            fold.Add(row);
            fold.Add(new Label(InterflowAbilityGroups.Hint(createGroup))
                { style = { whiteSpace = WhiteSpace.Normal, color = DIM, fontSize = 10, marginBottom = 3 } });

            // К4: шаг 2 — карточки типов этой группы вместо плоского дропдауна на 51 пункт.
            var settings = InterflowEditorSettings.GetOrCreate();
            var inGroup = types.Values.Where(t => InterflowAbilityGroups.OfType(t) == createGroup)
                                      .OrderBy(t => CardTitle(t, settings)).ToList();

            if (createGroup == InterflowAbilityGroups.Group.Combat)
            {
                // Боевое умение делается ТОЛЬКО конструктором: тип — это набор параметров,
                // а не позиция в каталоге классов (решение Artsiom 2026-08-06). Выбор класса убран.
                var main = inGroup.FirstOrDefault(t => t.Name == "CompositeSkill");
                if (main != null)
                {
                    var create = new Button(() => CreateAbility(main)) { text = "Создать умение" };
                    create.style.unityFontStyleAndWeight = FontStyle.Bold;
                    create.tooltip = "Создаёт пустое умение и открывает его в конструкторе: там задаются " +
                                     "срабатывание (разовое, переключатель, аура), цель, селекторы и блоки эффектов.";
                    fold.Add(create);
                    fold.Add(new Label("Дальше — вкладка «Конструктор скиллов»: срабатывание, цель, блоки.")
                        { style = { whiteSpace = WhiteSpace.Normal, color = DIM, fontSize = 10, marginBottom = 3 } });
                }
                else
                {
                    fold.Add(new Label("Конструктор скиллов (CompositeSkill) не найден в сборке.")
                        { style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.90f, 0.33f, 0.29f) } });
                }
            }
            else
            {
                foreach (var t in inGroup) fold.Add(TypeCard(t, false, settings));
            }

            var seed = new Button(SeedTypeLabels) { text = "Завести строки в таблице подписей" };
            seed.tooltip = "Добавляет в настройки редактора ПУСТЫЕ строки под каждый тип умения, " +
                           "чтобы русские названия и пояснения можно было вписать в инспекторе. Сам ничего не придумывает.";
            fold.Add(seed);

            box.Add(fold);
            return box;
        }

        static readonly Color DIM = new Color(0.65f, 0.65f, 0.65f);

        static Button CreateGroupButton(InterflowAbilityGroups.Group g)
        {
            var b = new Button(() =>
            {
                createGroup = g;
                createOpen = true;
                if (createHolder != null) { createHolder.Clear(); createHolder.Add(BuildCreateControls()); }
            })
            { text = InterflowAbilityGroups.Title(g), style = { flexGrow = 1, fontSize = 10 } };

            if (createGroup == g)
            {
                b.style.unityFontStyleAndWeight = FontStyle.Bold;
                b.style.backgroundColor = new Color(0.28f, 0.36f, 0.5f);
            }
            return b;
        }

        /// <summary>К2(б): название типа из таблицы настроек; нет записи — пункт меню из кода класса, как раньше.</summary>
        static string CardTitle(Type t, InterflowEditorSettings settings)
        {
            string fromSettings = settings != null ? settings.AbilityTypeTitle(t.Name) : null;
            return !string.IsNullOrEmpty(fromSettings) ? fromSettings : MenuLabelOf(t);
        }

        static string MenuLabelOf(Type t)
        {
            var attr = t.GetCustomAttribute<CreateAssetMenuAttribute>();
            return attr != null ? attr.menuName.Replace("StrategyCore/Abilities/", "") : t.Name;
        }

        /// <summary>К4: карточка типа — название, что получится и (если заполнено) пояснение из настроек.</summary>
        static VisualElement TypeCard(Type t, bool recommended, InterflowEditorSettings settings)
        {
            var card = new VisualElement
            {
                style = { marginBottom = 3, paddingLeft = 5, paddingRight = 5, paddingTop = 3, paddingBottom = 3,
                          backgroundColor = new Color(0.22f, 0.22f, 0.23f), borderLeftWidth = 3,
                          borderLeftColor = recommended ? new Color(0.35f, 0.6f, 0.95f) : new Color(0.32f, 0.32f, 0.32f) }
            };

            var btn = new Button(() => CreateAbility(t))
            {
                text = (recommended ? "★  " : "") + CardTitle(t, settings),
                style = { unityTextAlign = TextAnchor.MiddleLeft, marginBottom = 1 }
            };
            if (recommended) btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.tooltip = "Класс: " + t.Name;
            card.Add(btn);

            // «Что получится» берётся из штатного Ability.type уже существующих ассетов этого класса.
            // Ассетов нет — честно пишем «неизвестно», а не угадываем.
            AbilityType at = InterflowAbilityGroups.ComputedTypeOf(t);
            card.Add(new Label(at == AbilityType.Null
                    ? "Тип пока неизвестен: ассетов этого типа в проекте ещё нет — определится после создания первого"
                    : "Получится: " + InterflowAbilityGroups.AbilityTypeRu(at))
                { style = { whiteSpace = WhiteSpace.Normal, color = DIM, fontSize = 10 } });

            string note = settings != null ? settings.AbilityTypeNote(t.Name) : null;
            if (!string.IsNullOrEmpty(note))
                card.Add(new Label(note) { style = { whiteSpace = WhiteSpace.Normal, fontSize = 10 } });

            return card;
        }

        /// <summary>
        /// Заводит в таблице подписей ПУСТЫЕ строки под типы, которых там ещё нет.
        /// Ничего не придумывает и не перезаписывает: тексты вписывает геймдизайнер.
        /// </summary>
        static void SeedTypeLabels()
        {
            var settings = InterflowEditorSettings.GetOrCreate();
            var so = new SerializedObject(settings);
            var arr = so.FindProperty("abilityTypeLabels");
            if (arr == null) return;

            var have = new HashSet<string>();
            for (int i = 0; i < arr.arraySize; i++)
                have.Add(arr.GetArrayElementAtIndex(i).FindPropertyRelative("className").stringValue);

            int added = 0;
            foreach (var t in AbilityTypes().Values.OrderBy(x => x.Name))
            {
                if (have.Contains(t.Name)) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                var e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                e.FindPropertyRelative("className").stringValue = t.Name;
                e.FindPropertyRelative("title").stringValue = "";
                e.FindPropertyRelative("note").stringValue = "";
                added++;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("Таблица подписей",
                $"Добавлено пустых строк: {added}. Названия и пояснения впиши в ассете настроек редактора.", "Ок");
            EditorGUIUtility.PingObject(settings);
        }

        // Типы Ability со [CreateAssetMenu] — воспроизводит паттерн SCEditor.GetAbilityTypes (покрывает кастомные классы).
        // Пустые базовые шаблоны ядра: наследоваться от них можно, но СОЗДАВАТЬ из них нечего —
        // они ничего не делают. В списке создания только мешают (решение Artsiom 2026-08-06).
        static readonly HashSet<string> ServiceTemplates = new HashSet<string>
        {
            "Active", "Location", "Area", "Toggle", "Aura", "Passive", "Process", "UnitAbility"
        };

        static Dictionary<string, Type> AbilityTypes()
        {
            var dict = new Dictionary<string, Type>();
            foreach (var type in AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()))
            {
                if (typeof(Ability).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    if (ServiceTemplates.Contains(type.Name)) continue;

                    var attr = type.GetCustomAttribute<CreateAssetMenuAttribute>();
                    if (attr != null)
                        dict[attr.menuName.Replace("StrategyCore/Abilities/", "")] = type;
                }
            }
            return dict;
        }

        static void CreateAbility(Type abilityType)
        {
            var settings = InterflowEditorSettings.GetOrCreate();
            InterflowEditorUI.EnsureFolder(settings.abilityCreateFolder);

            string dstPath = EditorUtility.SaveFilePanelInProject(
                "Создать умение", abilityType.Name, "asset", "Имя нового умения", settings.abilityCreateFolder);
            if (string.IsNullOrEmpty(dstPath)) return;

            var asset = ScriptableObject.CreateInstance(abilityType);
            var ability = (Ability)asset;
            ability.id = NextFreeId("t:Ability", a => ((Ability)a).id);   // уникальный Ability.id (drawer чинит лишь 0/дубль — не перетрёт)
            ability.abilityName = new[] { Path.GetFileNameWithoutExtension(dstPath) };
            AssetDatabase.CreateAsset(asset, dstPath);
            AssetDatabase.SaveAssets();

            RefreshLists();
            selected = asset;
            RebuildList();
            RebuildRightPanel();
            EditorGUIUtility.PingObject(asset);
        }

        static void CreateEffector()
        {
            var settings = InterflowEditorSettings.GetOrCreate();
            InterflowEditorUI.EnsureFolder(settings.effectorCreateFolder);

            string dstPath = EditorUtility.SaveFilePanelInProject(
                "Создать эффектор", "Effector", "asset", "Имя нового эффектора", settings.effectorCreateFolder);
            if (string.IsNullOrEmpty(dstPath)) return;

            var eff = ScriptableObject.CreateInstance<Effector>();
            eff.id = NextFreeId("t:Effector", e => ((Effector)e).id);   // уникальный Effector.id
            eff.displayName = Path.GetFileNameWithoutExtension(dstPath);
            AssetDatabase.CreateAsset(eff, dstPath);
            AssetDatabase.SaveAssets();

            RefreshLists();
            selected = eff;
            RebuildList();
            RebuildRightPanel();
            EditorGUIUtility.PingObject(eff);
        }

        // Наименьший свободный id среди ассетов filter (детерминированно; как NextFreeTechId в InterflowFactionTab).
        static int NextFreeId(string filter, Func<UnityEngine.Object, int> idOf)
        {
            var used = new HashSet<int>();
            foreach (var g in AssetDatabase.FindAssets(filter))
            {
                var o = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(g));
                if (o != null) used.Add(idOf(o));
            }
            int id = 1;
            while (used.Contains(id)) id++;
            return id;
        }

        // ======================== СПИСКИ ========================

        static void RefreshLists()
        {
            abilities = AssetDatabase.FindAssets("t:Ability")
                .Select(g => AssetDatabase.LoadAssetAtPath<Ability>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null).OrderBy(a => a.name).ToList();

            effectors = AssetDatabase.FindAssets("t:Effector")
                .Select(g => AssetDatabase.LoadAssetAtPath<Effector>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(e => e != null).OrderBy(e => e.name).ToList();

            abilityUsageCache = null;
            effectorUsageCache = null;

            // Карта «класс → тип» строится по ассетам — после создания/удаления её надо перестроить.
            InterflowAbilityGroups.InvalidateCache();
        }

        static void RebuildList()
        {
            if (listContainer == null) return;
            listContainer.Clear();

            int total = mode == Mode.Abilities ? abilities.Count : effectors.Count;
            if (countLabel != null) countLabel.text = $"Всего: {total}";

            if (mode == Mode.Abilities)
            {
                // С1: три группы по назначению вместо простыни на 94 кнопки. Классификация — общий
                // InterflowAbilityGroups (правило 5): считается из штатного Ability.type, ничего не сериализуется.
                foreach (var g in GROUPS)
                {
                    var inGroup = abilities.Where(a => InterflowAbilityGroups.Of(a) == g).ToList();
                    if (inGroup.Count == 0) continue;

                    var group = g;
                    var fold = new Foldout
                    {
                        text = $"{InterflowAbilityGroups.Title(g)}  ({inGroup.Count})",
                        value = !groupOpen.TryGetValue(g, out var open) || open,
                        tooltip = InterflowAbilityGroups.Hint(g)
                    };
                    fold.RegisterValueChangedCallback(ev => { if (ev.target == fold) groupOpen[group] = ev.newValue; });

                    foreach (var a in inGroup) fold.Add(ListRow(a, $"{AbilityName(a)}  ·  {a.type}"));
                    listContainer.Add(fold);
                }
            }
            else
                foreach (var e in effectors) listContainer.Add(ListRow(e, EffectorName(e)));

            if (total == 0)
                listContainer.Add(new Label("Пусто. Создай кнопкой выше.")
                    { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, color = new Color(0.7f, 0.7f, 0.7f) } });
        }

        static Button ListRow(UnityEngine.Object obj, string text)
        {
            var o = obj;
            var row = new Button(() => { selected = o; RebuildList(); RebuildRightPanel(); })
            {
                text = text,
                style = { unityTextAlign = TextAnchor.MiddleLeft, marginBottom = 1 }
            };
            if (o == selected)
            {
                row.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.style.borderLeftWidth = 3;
                row.style.borderLeftColor = new Color(0.35f, 0.6f, 0.95f);
            }
            return row;
        }

        // ======================== РЕДАКТОР ========================

        static void RebuildRightPanel()
        {
            if (rightPanel == null) return;
            rightPanel.Clear();

            if (!IsSelectedValid())
            {
                rightPanel.Add(new Label("Выбери элемент слева или создай.") { style = { marginTop = 6, whiteSpace = WhiteSpace.Normal } });
                return;
            }

            string path = AssetDatabase.GetAssetPath(selected);
            string title = selected is Ability a ? AbilityName(a) : EffectorName((Effector)selected);

            var headRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 } };
            headRow.Add(new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, flexGrow = 1 } });
            headRow.Add(new Button(RebuildRightPanel) { text = "Обновить" });
            headRow.Add(new Button(() => EditorGUIUtility.PingObject(selected)) { text = "Показать" });
            headRow.Add(new Button(DeleteSelected) { text = "Удалить" });
            rightPanel.Add(headRow);
            rightPanel.Add(new Label(path) { style = { color = new Color(0.6f, 0.6f, 0.6f), marginBottom = 6, whiteSpace = WhiteSpace.Normal } });

            AddSummaryCard();
            AddUsageSection();

            rightPanel.Add(new Label(selected is Ability ? "Поля умения:" : "Поля эффектора:")
                { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4, marginBottom = 2 } });
            rightPanel.Add(InterflowEditorUI.BuildGroupedFields(new SerializedObject(selected)));
        }

        // Карточка-сводка над полями: чтобы понять скилл, не пришлось раскрывать все блоки (План §5.1).
        // Для эффектора вместо сводки — бейдж «виден ли значок в панели состояний».
        static void AddSummaryCard()
        {
            if (selected is CompositeSkill skill)
            {
                var card = new Label(skill.BuildSummary())
                {
                    style =
                    {
                        whiteSpace = WhiteSpace.Normal,
                        marginBottom = 6, paddingTop = 4, paddingBottom = 4, paddingLeft = 6, paddingRight = 6,
                        backgroundColor = new Color(0.20f, 0.24f, 0.20f),
                        borderTopLeftRadius = 3, borderTopRightRadius = 3,
                        borderBottomLeftRadius = 3, borderBottomRightRadius = 3
                    }
                };
                card.tooltip = "Автосводка по включённым блокам скилла. Значения показаны для первого уровня.";
                rightPanel.Add(card);
                return;
            }

            if (selected is Effector eff)
            {
                bool visible = !eff.stacks && eff.icon != null;
                string text = visible
                    ? "Значок виден в панели состояний"
                    : eff.stacks
                        ? "Значок НЕ виден: включён Stacks (в панели показываются только нестакающие эффекторы)"
                        : "Значок НЕ виден: не задана иконка";

                var badge = new Label(text)
                {
                    style =
                    {
                        whiteSpace = WhiteSpace.Normal,
                        marginBottom = 6, paddingTop = 4, paddingBottom = 4, paddingLeft = 6, paddingRight = 6,
                        backgroundColor = visible ? new Color(0.20f, 0.24f, 0.20f) : new Color(0.28f, 0.22f, 0.18f),
                        borderTopLeftRadius = 3, borderTopRightRadius = 3,
                        borderBottomLeftRadius = 3, borderBottomRightRadius = 3
                    }
                };
                badge.tooltip = "Значок состояния рисуется только у эффекторов без Stacks и с заданной иконкой.";
                rightPanel.Add(badge);
            }
        }

        static void DeleteSelected()
        {
            if (!IsSelectedValid()) return;
            string path = AssetDatabase.GetAssetPath(selected);
            if (!EditorUtility.DisplayDialog("Удалить", $"Удалить ассет?\n{path}", "Удалить", "Отмена")) return;

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
            RefreshLists();
            selected = FirstOfMode();
            RebuildList();
            RebuildRightPanel();
        }

        // ======================== «ГДЕ ИСПОЛЬЗУЕТСЯ» ========================

        static void AddUsageSection()
        {
            var foldout = new Foldout { text = "Кто использует", value = false, style = { marginBottom = 4 } };

            List<UsageRec> recs = selected is Ability a ? AbilityUsage(a) : EffectorUsage(AssetDatabase.GetAssetPath(selected));

            if (recs.Count == 0)
                foldout.Add(new Label("Ссылок не найдено (сцены здесь не сканируются — см. вкладку «Чистка»).")
                    { style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.7f, 0.7f, 0.7f) } });
            else
                foreach (var r in recs) foldout.Add(UsageRow(r));

            rightPanel.Add(foldout);
        }

        static VisualElement UsageRow(UsageRec r)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 1 } };
            row.Add(new Label("● " + r.text)
            {
                style = { whiteSpace = WhiteSpace.Normal, flexGrow = 1, color = r.warn ? new Color(0.98f, 0.78f, 0.28f) : Color.white }
            });
            row.Add(new Button(r.ping) { text = "→", tooltip = "Показать в Project" });
            return row;
        }

        static List<UsageRec> AbilityUsage(Ability ability)
        {
            BuildAbilityUsageCache();
            return abilityUsageCache.TryGetValue(ability, out var l) ? l : new List<UsageRec>();
        }

        static void BuildAbilityUsageCache()
        {
            if (abilityUsageCache != null) return;
            abilityUsageCache = new Dictionary<Ability, List<UsageRec>>();

            // Юниты: abilities[] и список авто-способностей (+ предупреждение, если запись не в abilities[]).
            foreach (var go in AllUnitGOs())
            {
                var unit = go.GetComponent<Unit>();
                string uname = unit != null && !string.IsNullOrEmpty(unit.unitName) ? unit.unitName : go.name;
                var gref = go;

                var inList = ReadAbilityArray(unit, "abilities");
                foreach (var ab in inList)
                    if (ab != null) AddUsage(ab, $"Юнит «{uname}»: abilities[]", () => EditorGUIUtility.PingObject(gref), false);

                // С 2026-08-04 авто-способностей может быть несколько — перебираем весь список.
                // С 2026-08-08 элемент списка — сама ссылка на умение, а не запись с полем «ability»:
                // настройки поиска с компонента убраны, они живут в самом умении.
                var aau = go.GetComponent<AutoAbilityUser>();
                if (aau != null)
                {
                    var entries = new SerializedObject(aau).FindProperty("autoAbilities");
                    for (int i = 0; entries != null && i < entries.arraySize; i++)
                    {
                        if (!(entries.GetArrayElementAtIndex(i).objectReferenceValue is Ability auto)) continue;

                        bool present = inList.Contains(auto);
                        AddUsage(auto, $"Юнит «{uname}»: авто-способность №{i + 1}" + (present ? "" : "  ⚠ не в abilities[]"),
                            () => EditorGUIUtility.PingObject(gref), !present);
                    }
                }
            }

            // Фракции: стартовые умения ГЗ (centralAbilities) + умения, открываемые узлами дерева технологий.
            foreach (var f in AllFactions())
            {
                var fref = f;
                if (f.centralAbilities != null)
                    for (int i = 0; i < f.centralAbilities.Count; i++)
                    {
                        var ab = f.centralAbilities[i];
                        if (ab != null) AddUsage(ab, $"Фракция «{f.name}»: стартовое умение ГЗ, ячейка {i}", () => EditorGUIUtility.PingObject(fref), false);
                    }

                if (f.techTiers == null) continue;
                foreach (var tier in f.techTiers)
                {
                    if (tier == null) continue;
                    NodeUsage(tier.levelUpgrade, f, fref);
                    foreach (var opt in new[] { tier.optionA, tier.optionB })
                    {
                        if (opt == null) continue;
                        NodeUsage(opt.node, f, fref);
                        NodeUsage(opt.specializationA, f, fref);
                        NodeUsage(opt.specializationB, f, fref);
                    }
                }
            }
        }

        // Умения, открываемые одним узлом дерева: строка «фракция → узел → ячейка».
        static void NodeUsage(TechNode node, FactionConfig f, FactionConfig fref)
        {
            if (node == null || node.unlockAbilities == null) return;
            string nodeName = node.technology != null ? node.technology.name : "узел без технологии";
            foreach (var e in node.unlockAbilities)
                if (e != null && e.ability != null)
                    AddUsage(e.ability, $"Фракция «{f.name}»: открывает узел «{nodeName}», ячейка {e.slot}",
                        () => EditorGUIUtility.PingObject(fref), node.technology == null);
        }

        static void AddUsage(Ability key, string text, Action ping, bool warn)
        {
            if (!abilityUsageCache.TryGetValue(key, out var list)) { list = new List<UsageRec>(); abilityUsageCache[key] = list; }
            list.Add(new UsageRec { text = text, ping = ping, warn = warn });
        }

        // Эффекторы: реверс-ссылки через зависимости юнитов и умений (attackEffectors, поля умений).
        static List<UsageRec> EffectorUsage(string effPath)
        {
            BuildEffectorUsageCache();
            return effectorUsageCache.TryGetValue(effPath, out var l) ? l : new List<UsageRec>();
        }

        static void BuildEffectorUsageCache()
        {
            if (effectorUsageCache != null) return;
            effectorUsageCache = new Dictionary<string, List<UsageRec>>();

            var effPaths = new HashSet<string>(effectors.Select(e => AssetDatabase.GetAssetPath(e)));

            void Index(string ownerPath, string label, Action ping)
            {
                foreach (var dep in AssetDatabase.GetDependencies(ownerPath, false))
                {
                    if (!effPaths.Contains(dep)) continue;
                    if (!effectorUsageCache.TryGetValue(dep, out var list)) { list = new List<UsageRec>(); effectorUsageCache[dep] = list; }
                    list.Add(new UsageRec { text = label, ping = ping, warn = false });
                }
            }

            foreach (var go in AllUnitGOs())
            {
                var unit = go.GetComponent<Unit>();
                string uname = unit != null && !string.IsNullOrEmpty(unit.unitName) ? unit.unitName : go.name;
                var gref = go;
                Index(AssetDatabase.GetAssetPath(go), $"Юнит «{uname}» ссылается на эффектор", () => EditorGUIUtility.PingObject(gref));
            }
            foreach (var a in abilities)
            {
                var aref = a;
                Index(AssetDatabase.GetAssetPath(a), $"Умение «{AbilityName(a)}» ссылается на эффектор", () => EditorGUIUtility.PingObject(aref));
            }
        }

        // ======================== ЧТЕНИЕ ПОЛЕЙ / ХЕЛПЕРЫ ========================

        static List<Ability> ReadAbilityArray(UnityEngine.Object obj, string prop)
        {
            var list = new List<Ability>();
            if (obj == null) return list;
            var p = new SerializedObject(obj).FindProperty(prop);
            if (p != null && p.isArray)
                for (int i = 0; i < p.arraySize; i++)
                    list.Add(p.GetArrayElementAtIndex(i).objectReferenceValue as Ability);
            return list;
        }

        static UnityEngine.Object ReadObjectRef(UnityEngine.Object obj, string prop)
        {
            if (obj == null) return null;
            var p = new SerializedObject(obj).FindProperty(prop);
            return p != null ? p.objectReferenceValue : null;
        }

        static IEnumerable<GameObject> AllUnitGOs() =>
            AssetDatabase.FindAssets("t:GameObject")
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(go => go != null && go.GetComponent<Unit>() != null);

        static IEnumerable<FactionConfig> AllFactions() =>
            AssetDatabase.FindAssets("t:FactionConfig")
                .Select(g => AssetDatabase.LoadAssetAtPath<FactionConfig>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(f => f != null);

        static string AbilityName(Ability a) =>
            a.abilityName != null && a.abilityName.Length > 0 && !string.IsNullOrEmpty(a.abilityName[0]) ? a.abilityName[0] : a.name;

        static string EffectorName(Effector e) => !string.IsNullOrEmpty(e.displayName) ? e.displayName : e.name;

        static bool IsSelectedValid() =>
            selected != null && (mode == Mode.Abilities ? selected is Ability : selected is Effector);

        static UnityEngine.Object FirstOfMode() =>
            mode == Mode.Abilities ? (abilities.Count > 0 ? abilities[0] : (UnityEngine.Object)null)
                                   : (effectors.Count > 0 ? effectors[0] : null);
    }
}
