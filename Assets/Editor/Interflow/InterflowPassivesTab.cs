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
    // ============ INTERFLOW EDITOR — ВКЛАДКА «ПАССИВНЫЕ УМЕНИЯ» (задача Artsiom 2026-08-05) ==
    // Пассивки — это тоже Ability, но по смыслу они не кастуются: работают сами, без нажатия,
    // без каста, без маны. Во вкладке «Умения и эффекторы» их поля тонут среди каст-полей.
    // Отдельная вкладка показывает пассивку так, как о ней думает геймдизайнер:
    // ЧТО она делает → УСЛОВИЯ открытия и уровни → подпись и иконка.
    //
    // Зеркало вкладки «Производство» (InterflowProductionTab, С2): та же структура, другой фильтр.
    // Состав группы считает общий InterflowAbilityGroups (правило 5) — та же классификация,
    // что в группах списка «Умений», в фильтре «Производства» и в шагах создания.
    //
    // Правило 7: НИЧЕГО не прячем безвозвратно. Поля вне трёх секций лежат в свёрнутом блоке
    // «Прочие поля» — редактировать можно всё, но шум не мешает.
    public static class InterflowPassivesTab
    {
        static List<Ability> items = new List<Ability>();
        static Ability selected;
        static string factionFilter = InterflowAbilityFactions.ALL;   // разбор по фракциям (задача Artsiom 2026-08-17)

        static VisualElement listContainer, rightPanel;
        static Label countLabel;

        static readonly Color DIM = new Color(0.65f, 0.65f, 0.65f);

        // Условия, при которых пассивка открывается, и её уровни.
        static readonly string[] CONDITION_FIELDS = { "requiredTech", "requiredLevel", "maxLevels" };

        // Поля подписи: то, что игрок видит в панели.
        static readonly string[] TEXT_FIELDS = { "id", "abilityName", "description", "icon" };

        // ======================== ТОЧКА ВХОДА ========================

        public static VisualElement CreateTabUI()
        {
            Refresh();
            if (selected == null || !items.Contains(selected)) selected = items.FirstOrDefault();

            var root = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, flexGrow = 1, minHeight = 500,
                          marginTop = 6, marginLeft = 6, marginRight = 6 }
            };

            var left = new VisualElement { style = { width = 300, flexShrink = 0, marginRight = 8 } };

            // Создание — ОДНОЙ кнопкой, без выбора класса (решение Artsiom 2026-08-09): новая пассивка
            // собирается конструктором `CompositePassive`, а не выбирается из 25 классов-кирпичей.
            // Старые классы остаются жить — на них висят существующие ассеты, они открываются
            // в этой же вкладке обычным редактором полей.
            var create = new Button(CreatePassive) { text = "Создать пассивку" };
            create.style.unityFontStyleAndWeight = FontStyle.Bold;
            create.tooltip = "Создаёт пустую пассивку-конструктор и открывает её здесь: " +
                             "дальше включаются нужные свойства — характеристики, иммунитеты, аура и прочее.";
            left.Add(create);

            left.Add(new Button(() => { InterflowAbilityUsage.InvalidateCache(); InterflowAbilityFactions.InvalidateCache();
                                        Refresh(); RebuildList(); RebuildRight(); })
                { text = "Обновить список" });

            // Фильтр по фракции — общий элемент редактора умений (правило 5).
            left.Add(InterflowAbilityFactions.FilterDropdown(factionFilter,
                v => { factionFilter = v; RebuildList(); }));
            countLabel = new Label { style = { marginTop = 2, marginBottom = 2, color = DIM } };
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
            RebuildRight();
            return root;
        }

        // ======================== ДАННЫЕ ========================

        static void Refresh()
        {
            items = AssetDatabase.FindAssets("t:Ability")
                .Select(g => AssetDatabase.LoadAssetAtPath<Ability>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null && InterflowAbilityGroups.Of(a) == InterflowAbilityGroups.Group.Passive)
                .OrderBy(a => a.name).ToList();

            // Фракция могла исчезнуть вместе с ассетом — иначе фильтр молча показывал бы пусто.
            factionFilter = InterflowAbilityFactions.Correct(factionFilter);
        }

        // ======================== СОЗДАНИЕ И УДАЛЕНИЕ ========================

        /// <summary>
        /// Новая пассивка — всегда `CompositePassive` (конструктор). Путь и уникальный id — как у умений
        /// в «Умениях и эффекторах»: папка из настроек редактора, id через общий `NextFreeId` (правило 5).
        /// Тип берём напрямую, а не сканом по [CreateAssetMenu]: у конструктора его нет намеренно.
        /// </summary>
        static void CreatePassive()
        {
            var settings = InterflowEditorSettings.GetOrCreate();
            InterflowEditorUI.EnsureFolder(settings.abilityCreateFolder);

            string dstPath = EditorUtility.SaveFilePanelInProject(
                "Создать пассивку", "Passive_New", "asset", "Имя новой пассивки", settings.abilityCreateFolder);
            if (string.IsNullOrEmpty(dstPath)) return;

            var asset = ScriptableObject.CreateInstance<CompositePassive>();
            asset.id = InterflowEditorUI.NextFreeId("t:Ability", a => ((Ability)a).id);
            asset.abilityName = new[] { Path.GetFileNameWithoutExtension(dstPath) };

            AssetDatabase.CreateAsset(asset, dstPath);
            AssetDatabase.SaveAssets();

            // Классификация группы считается по уже существующим ассетам — после создания карту сбрасываем,
            // иначе новый класс останется «неизвестным» до перезапуска окна.
            InterflowAbilityGroups.InvalidateCache();
            InterflowAbilityUsage.InvalidateCache();
            InterflowAbilityFactions.InvalidateCache();

            Refresh();
            selected = asset;
            RebuildList();
            RebuildRight();
            EditorGUIUtility.PingObject(asset);
        }

        static void DeleteSelected()
        {
            if (selected == null) return;

            string path = AssetDatabase.GetAssetPath(selected);
            if (!EditorUtility.DisplayDialog("Удалить", $"Удалить пассивку?\n{path}", "Удалить", "Отмена")) return;

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
            InterflowAbilityGroups.InvalidateCache();
            InterflowAbilityUsage.InvalidateCache();
            InterflowAbilityFactions.InvalidateCache();

            Refresh();
            selected = items.FirstOrDefault();
            RebuildList();
            RebuildRight();
        }

        static string Title(Ability a)
            => a.abilityName != null && a.abilityName.Length > 0 && !string.IsNullOrEmpty(a.abilityName[0])
                ? a.abilityName[0] : a.name;

        // ======================== СПИСОК ========================

        static void RebuildList()
        {
            if (listContainer == null) return;
            listContainer.Clear();

            var shown = items.Where(a => InterflowAbilityFactions.Matches(a, factionFilter)).ToList();

            if (countLabel != null)
                countLabel.text = shown.Count == items.Count
                    ? $"Пассивных умений: {items.Count}"
                    : $"Пассивных умений: {shown.Count} из {items.Count}";

            if (items.Count == 0)
            {
                listContainer.Add(new Label("Пассивных умений нет. Жми «Создать пассивку» выше.")
                    { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, color = DIM } });
                return;
            }

            if (shown.Count == 0)
            {
                listContainer.Add(new Label("По этой фракции пассивных умений нет.")
                    { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, color = DIM } });
                return;
            }

            // Внутри пассивок делим по классу-кирпичу — у «каждой N-й атаки» и «ярости от нехватки ХП» разный смысл.
            foreach (var byClass in shown.GroupBy(a => a.GetType().Name).OrderBy(g => g.Key))
            {
                var fold = new Foldout { text = $"{byClass.Key}  ({byClass.Count()})", value = true };
                foreach (var a in byClass) fold.Add(ListRow(a));
                listContainer.Add(fold);
            }
        }

        static Button ListRow(Ability a)
        {
            var target = a;
            var b = new Button(() => { selected = target; RebuildList(); RebuildRight(); })
            {
                text = Title(a),
                style = { unityTextAlign = TextAnchor.MiddleLeft, marginBottom = 1 }
            };
            if (a == selected)
            {
                b.style.unityFontStyleAndWeight = FontStyle.Bold;
                b.style.borderLeftWidth = 3;
                b.style.borderLeftColor = new Color(0.35f, 0.6f, 0.95f);
            }
            return b;
        }

        // ======================== ПРАВАЯ ПАНЕЛЬ ========================

        static void RebuildRight()
        {
            if (rightPanel == null) return;
            rightPanel.Clear();

            if (selected == null)
            {
                rightPanel.Add(new Label("Выбери пассивку слева.") { style = { marginTop = 6 } });
                return;
            }

            var so = new SerializedObject(selected);

            var head = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            head.Add(new Label(Title(selected))
                { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 15, flexGrow = 1 } });
            var sel = selected;
            head.Add(new Button(() => EditorGUIUtility.PingObject(sel)) { text = "Показать" });
            head.Add(new Button(DeleteSelected) { text = "Удалить" });
            rightPanel.Add(head);

            rightPanel.Add(new Label($"id {selected.id}   ·   {selected.GetType().Name}   ·   {AssetDatabase.GetAssetPath(selected)}")
                { style = { color = DIM, marginBottom = 6, whiteSpace = WhiteSpace.Normal },
                  tooltip = "Пассивное умение: работает само, без нажатия и без каста." });

            rightPanel.Add(InterflowAbilityUsage.Section(selected));   // общий блок, правило 5
            rightPanel.Add(InterflowAbilityFactions.Section(so, selected, RebuildList));

            // Распределяем поля по трём осмысленным секциям, остаток — в свёрнутый блок.
            var own = OwnFieldNames(selected.GetType());
            var ownBox = Section("Что делает пассивка");
            var reactBox = Section("Реакции — что происходит по событию");
            var condBox = Section("Условия открытия и уровни");
            var textBox = Section("Подпись и иконка");
            var restFold = new Foldout
            {
                text = "Прочие поля", value = false, style = { marginTop = 8 },
                tooltip = "Каст, откат, мана, дальность и цена пассивным умениям не нужны — они лежат здесь " +
                          "и трогать их не требуется. Носителю пассивное умение назначается во вкладке «Юниты» " +
                          "главного окна (блок «Пассивные умения»)."
            };

            var it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script") continue;
                if (it.name == "editorFactions") continue;   // показано блоком «Фракции» выше

                VisualElement target;
                if (REACTION_FIELDS.Contains(it.name)) target = reactBox;
                else if (own.Contains(it.name)) target = ownBox;
                else if (CONDITION_FIELDS.Contains(it.name)) target = condBox;
                else if (TEXT_FIELDS.Contains(it.name)) target = textBox;
                else target = restFold;

                target.Add(InterflowEditorUI.MakeField(it, InterflowEditorUI.FieldLabel(it.name), true));
            }

            AddIfNotEmpty(ownBox);
            AddIfNotEmpty(reactBox);
            AddIfNotEmpty(condBox);
            AddIfNotEmpty(textBox);
            rightPanel.Add(restFold);


            rightPanel.Bind(so);
        }

        /// <summary>
        /// Ось «реакции» конструктора пассивок: показывается СВОЕЙ секцией, отдельно от свойств
        /// (решение Artsiom 2026-08-17). Свойства отвечают на вопрос «какой носитель»,
        /// реакции — «что произойдёт, когда случится событие»; мешать их в одном списке нельзя.
        /// </summary>
        static readonly HashSet<string> REACTION_FIELDS = new HashSet<string>
        {
            "onDamaged", "onDeath", "onKill", "onHpBelow"
        };

        /// <summary>Имена сериализованных полей, объявленных самим классом-кирпичом (а не унаследованных от Ability).</summary>
        static HashSet<string> OwnFieldNames(Type t)
        {
            var set = new HashSet<string>();
            // Поднимаемся от конкретного класса до Ability (не включая): InterflowAbility — общая база
            // без сериализуемых полей, но если они там появятся, то тоже относятся к «что делает».
            for (var cur = t; cur != null && cur != typeof(Ability) && cur != typeof(ScriptableObject); cur = cur.BaseType)
                foreach (var f in cur.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (f.IsPublic || f.GetCustomAttribute<SerializeField>() != null) set.Add(f.Name);

            return set;
        }

        static VisualElement Section(string title)
        {
            var box = new VisualElement
            {
                style = { marginTop = 6, paddingTop = 5, paddingBottom = 5, paddingLeft = 8, paddingRight = 8,
                          backgroundColor = new Color(0.23f, 0.24f, 0.25f),
                          borderTopLeftRadius = 3, borderTopRightRadius = 3,
                          borderBottomLeftRadius = 3, borderBottomRightRadius = 3 }
            };
            box.Add(new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 3 } });
            return box;
        }

        // Секция без единого поля не показывается.
        static void AddIfNotEmpty(VisualElement section)
        {
            if (section.childCount > 1) rightPanel.Add(section);
        }
    }
}
