using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============ INTERFLOW EDITOR — ВКЛАДКА «ПРОИЗВОДСТВО» (С2) ==
    // Постройки, обучение юнитов, исследования и категории панели — это тоже Ability,
    // но по смыслу они не боевые умения: у них другие поля, а радиус, селектор целей и стратегия
    // выбора цели им не нужны вовсе. Отдельная вкладка показывает их так, как о них думает
    // геймдизайнер: ЧТО производится → ЦЕНА и требования → подпись и иконка.
    //
    // Состав группы считает общий InterflowAbilityGroups (правило 5) — та же классификация,
    // что и в группах списка вкладки «Умения и эффекторы» и в шагах создания.
    //
    // Правило 7: НИЧЕГО не прячем безвозвратно. Поля, которые не попали в первые три секции,
    // лежат в свёрнутом блоке «Прочие поля» — редактировать можно всё, но шум не мешает.
    public static class InterflowProductionTab
    {
        static List<Ability> items = new List<Ability>();
        static Ability selected;

        static VisualElement listContainer, rightPanel;
        static Label countLabel;

        static readonly Color DIM = new Color(0.65f, 0.65f, 0.65f);

        // Поля, которые для производственного умения относятся к цене и требованиям.
        static readonly string[] PRICE_FIELDS = { "cost", "requiredTech", "requiredLevel", "castTime", "cooldown" };

        // Поля подписи: то, что игрок видит в панели.
        static readonly string[] TEXT_FIELDS = { "id", "abilityName", "description", "icon", "slotNumber" };

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

            // Создание переехало сюда из «Умений и эффекторов» (решение Artsiom 2026-08-09).
            // Здесь выбор класса ОСТАЁТСЯ, в отличие от боевых и пассивок: постройка, обучение,
            // исследование и категория панели — разные сущности, общего конструктора у них нет.
            left.Add(BuildCreateFold());

            left.Add(new Button(() => { InterflowAbilityUsage.InvalidateCache(); Refresh(); RebuildList(); RebuildRight(); })
                { text = "Обновить список" });
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

        // ======================== СОЗДАНИЕ И УДАЛЕНИЕ ========================

        static bool createOpen;

        /// <summary>Карточки классов производства: постройка, обучение, исследование, апгрейд, категория.</summary>
        static VisualElement BuildCreateFold()
        {
            var fold = new Foldout { text = "Создать", value = createOpen };
            fold.RegisterValueChangedCallback(e => { if (e.target == fold) createOpen = e.newValue; });

            var types = InterflowAbilityCreate.TypesOfGroup(InterflowAbilityGroups.Group.Production);
            if (types.Count == 0)
            {
                fold.Add(new Label("Классов производства не найдено.")
                    { style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.85f, 0.7f, 0.4f) } });
                return fold;
            }

            foreach (var t in types)
                fold.Add(InterflowAbilityCreate.TypeCard(t, created =>
                {
                    Refresh();
                    selected = created;
                    RebuildList();
                    RebuildRight();
                    EditorGUIUtility.PingObject(created);
                }, "Создать элемент производства"));

            // Таблица русских подписей классов переехала сюда вместе с созданием: она нужна там,
            // где классы вообще показываются, а это теперь только производство.
            var seed = new Button(InterflowAbilityCreate.SeedTypeLabels) { text = "Завести строки в таблице подписей" };
            seed.tooltip = "Добавляет в настройки редактора ПУСТЫЕ строки под каждый класс умения, " +
                           "чтобы русские названия и пояснения можно было вписать в инспекторе. Сам ничего не придумывает.";
            fold.Add(seed);

            return fold;
        }

        static void DeleteSelected()
        {
            if (!InterflowAbilityCreate.Delete(selected, "Удалить этот элемент производства?")) return;

            Refresh();
            selected = items.FirstOrDefault();
            RebuildList();
            RebuildRight();
        }

        // ======================== ДАННЫЕ ========================

        static void Refresh()
        {
            items = AssetDatabase.FindAssets("t:Ability")
                .Select(g => AssetDatabase.LoadAssetAtPath<Ability>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null && InterflowAbilityGroups.Of(a) == InterflowAbilityGroups.Group.Production)
                .OrderBy(a => a.name).ToList();
        }

        static string Title(Ability a)
            => a.abilityName != null && a.abilityName.Length > 0 && !string.IsNullOrEmpty(a.abilityName[0])
                ? a.abilityName[0] : a.name;

        // ======================== СПИСОК ========================

        static void RebuildList()
        {
            if (listContainer == null) return;
            listContainer.Clear();

            if (countLabel != null) countLabel.text = $"Производственных умений: {items.Count}";

            if (items.Count == 0)
            {
                listContainer.Add(new Label("Постройки, обучение и исследования не найдены. Жми «Создать» выше.")
                    { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, color = DIM } });
                return;
            }

            // Внутри производства делим по классу умения — у постройки, обучения и категории разный смысл.
            foreach (var byClass in items.GroupBy(a => a.GetType().Name).OrderBy(g => g.Key))
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
                rightPanel.Add(new Label("Выбери элемент слева.") { style = { marginTop = 6 } });
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

            AbilityType at;
            try { at = selected.type; } catch { at = AbilityType.Null; }
            rightPanel.Add(new Label($"id {selected.id}   ·   {selected.GetType().Name}   ·   {AssetDatabase.GetAssetPath(selected)}")
                { style = { color = DIM, marginBottom = 2, whiteSpace = WhiteSpace.Normal } });
            rightPanel.Add(new Label("Получится: " + InterflowAbilityGroups.AbilityTypeRu(at))
                { style = { color = DIM, marginBottom = 6, whiteSpace = WhiteSpace.Normal } });

            // Распределяем поля по трём осмысленным секциям, остаток — в свёрнутый блок.
            var own = OwnFieldNames(selected.GetType());
            var used = new HashSet<string>();

            var ownBox = Section("Что производится");
            var priceBox = Section("Цена и требования");
            var textBox = Section("Подпись и иконка");
            var restFold = new Foldout
            {
                text = "Прочие поля", value = false, style = { marginTop = 8 },
                tooltip = "Радиус, селектор целей и стратегия выбора цели производственным умениям не нужны — " +
                          "они лежат здесь и трогать их не требуется."
            };

            var it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script") continue;

                VisualElement target;
                if (own.Contains(it.name)) target = ownBox;
                else if (PRICE_FIELDS.Contains(it.name)) target = priceBox;
                else if (TEXT_FIELDS.Contains(it.name)) target = textBox;
                else target = restFold;

                target.Add(InterflowEditorUI.MakeField(it, InterflowEditorUI.FieldLabel(it.name), true));
                used.Add(it.name);
            }

            AddIfNotEmpty(ownBox);
            AddIfNotEmpty(priceBox);
            AddIfNotEmpty(textBox);
            rightPanel.Add(restFold);


            rightPanel.Bind(so);
        }

        /// <summary>Имена сериализованных полей, объявленных САМИМ классом (а не унаследованных от Ability).</summary>
        static HashSet<string> OwnFieldNames(Type t)
        {
            var set = new HashSet<string>();
            if (t == null) return set;

            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
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

        // Секция без единого поля не показывается: у Container, например, нет цены.
        static void AddIfNotEmpty(VisualElement section)
        {
            if (section.childCount > 1) rightPanel.Add(section);
        }
    }
}
