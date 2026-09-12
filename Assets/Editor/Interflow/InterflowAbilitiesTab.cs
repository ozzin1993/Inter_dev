using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============================= INTERFLOW EDITOR — ВКЛАДКА «ЭФФЕКТОРЫ» ==
    // Была «Умения и эффекторы». С 2026-08-09 (решение Artsiom) умения отсюда УБРАНЫ целиком:
    // все поля умения живут в своих конструкторах — «Конструктор скиллов» (боевые),
    // «Пассивные умения» (пассивки), «Производство» (постройки, обучение, исследования).
    // Здесь остался ровно эффектор: список, создание, удаление, «кто использует», поля.
    // Причина — задвоение Д1 из [[concepts/abilities-effectors-passives-map]]: один и тот же
    // скилл редактировался в двух вкладках, 234 элемента и 22 фолда на объект.
    //
    // ИМЯ ТИПА УСТАРЕЛО: класс всё ещё зовётся InterflowAbilitiesTab, хотя ведает эффекторами.
    // Переименование файла и типа задевает .meta и GUID — отдельный шаг, по слову Artsiom.
    //
    // Правило 5: часть окна Interflow Editor, общие механизмы — в InterflowEditorUI,
    // InterflowAbilityUsage, InterflowAbilityCreate. Правило 4: UI по-русски.
    public static class InterflowAbilitiesTab
    {
        static List<Effector> effectors = new List<Effector>();
        static Effector selected;

        // Реверс-ссылки на эффекторы: ключ — путь ассета. Кэш умений живёт в общем InterflowAbilityUsage.
        static Dictionary<string, List<InterflowAbilityUsage.Rec>> effectorUsageCache;

        static VisualElement listContainer, rightPanel;
        static Label countLabel;

        static readonly Color DIM = new Color(0.65f, 0.65f, 0.65f);

        // ======================== ТОЧКА ВХОДА ВКЛАДКИ ========================

        public static VisualElement CreateTabUI()
        {
            RefreshList();
            if (selected == null || !effectors.Contains(selected)) selected = effectors.FirstOrDefault();

            var root = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, flexGrow = 1, minHeight = 500,
                          marginTop = 6, marginLeft = 6, marginRight = 6 }
            };

            var left = new VisualElement { style = { width = 300, flexShrink = 0, marginRight = 8 } };

            var create = new Button(CreateEffector) { text = "Создать эффектор" };
            create.style.unityFontStyleAndWeight = FontStyle.Bold;
            create.tooltip = "Эффектор — это СОСТОЯНИЕ на юните: длительность, тик, значок в панели. " +
                             "Разовые эффекты (урон, лечение, призыв) собираются блоками умения, а не здесь.";
            left.Add(create);

            left.Add(new Button(() => { RefreshList(); RebuildList(); RebuildRightPanel(); }) { text = "Обновить список" });

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
            RebuildRightPanel();
            return root;
        }

        // ======================== СОЗДАНИЕ И УДАЛЕНИЕ ========================

        static void CreateEffector()
        {
            var settings = InterflowEditorSettings.GetOrCreate();
            InterflowEditorUI.EnsureFolder(settings.effectorCreateFolder);

            string dstPath = EditorUtility.SaveFilePanelInProject(
                "Создать эффектор", "Effector", "asset", "Имя нового эффектора", settings.effectorCreateFolder);
            if (string.IsNullOrEmpty(dstPath)) return;

            var eff = ScriptableObject.CreateInstance<Effector>();
            eff.id = InterflowEditorUI.NextFreeId("t:Effector", e => ((Effector)e).id);   // уникальный Effector.id
            eff.displayName = Path.GetFileNameWithoutExtension(dstPath);

            AssetDatabase.CreateAsset(eff, dstPath);
            AssetDatabase.SaveAssets();

            RefreshList();
            selected = eff;
            RebuildList();
            RebuildRightPanel();
            EditorGUIUtility.PingObject(eff);
        }

        static void DeleteSelected()
        {
            if (selected == null) return;

            string path = AssetDatabase.GetAssetPath(selected);
            if (!EditorUtility.DisplayDialog("Удалить", $"Удалить эффектор?\n{path}", "Удалить", "Отмена")) return;

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();

            RefreshList();
            selected = effectors.FirstOrDefault();
            RebuildList();
            RebuildRightPanel();
        }

        // ======================== СПИСОК ========================

        static void RefreshList()
        {
            effectors = AssetDatabase.FindAssets("t:Effector")
                .Select(g => AssetDatabase.LoadAssetAtPath<Effector>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(e => e != null).OrderBy(e => e.name).ToList();

            effectorUsageCache = null;
            InterflowAbilityUsage.InvalidateCache();   // умения могли поменять ссылки на эффекторы
        }

        static void RebuildList()
        {
            if (listContainer == null) return;
            listContainer.Clear();

            if (countLabel != null) countLabel.text = $"Эффекторов: {effectors.Count}";

            if (effectors.Count == 0)
            {
                listContainer.Add(new Label("Эффекторов нет. Жми «Создать эффектор» выше.")
                    { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, color = DIM } });
                return;
            }

            foreach (var e in effectors) listContainer.Add(ListRow(e));
        }

        static Button ListRow(Effector e)
        {
            var target = e;
            var row = new Button(() => { selected = target; RebuildList(); RebuildRightPanel(); })
            {
                text = EffectorName(e),
                style = { unityTextAlign = TextAnchor.MiddleLeft, marginBottom = 1 }
            };
            if (target == selected)
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

            if (selected == null)
            {
                rightPanel.Add(new Label("Выбери эффектор слева или создай.")
                    { style = { marginTop = 6, whiteSpace = WhiteSpace.Normal } });
                return;
            }

            var headRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 } };
            headRow.Add(new Label(EffectorName(selected)) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, flexGrow = 1 } });
            headRow.Add(new Button(RebuildRightPanel) { text = "Обновить" });
            headRow.Add(new Button(() => EditorGUIUtility.PingObject(selected)) { text = "Показать" });
            headRow.Add(new Button(DeleteSelected) { text = "Удалить" });
            rightPanel.Add(headRow);

            rightPanel.Add(new Label(AssetDatabase.GetAssetPath(selected))
                { style = { color = new Color(0.6f, 0.6f, 0.6f), marginBottom = 6, whiteSpace = WhiteSpace.Normal } });

            AddVisibilityBadge();
            AddUsageSection();

            rightPanel.Add(new Label("Поля эффектора:")
                { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4, marginBottom = 2 } });
            rightPanel.Add(InterflowEditorUI.BuildGroupedFields(new SerializedObject(selected)));

            rightPanel.Add(new Label("Сила и длительность наложения задаются НЕ здесь, а в блоке «Эффекторы» " +
                                     "того умения, которое его вешает. Ассет отвечает только за то, ЧТО происходит.")
                { style = { whiteSpace = WhiteSpace.Normal, color = DIM, fontSize = 10, marginTop = 6 } });
        }

        /// <summary>Бейдж «виден ли значок в панели состояний» — главная ловушка настройки эффектора.</summary>
        static void AddVisibilityBadge()
        {
            bool visible = (!selected.stacks || selected.maxStacks>0) && selected.icon != null;
            string text = visible
                ? "Значок виден в панели состояний"
                : selected.stacks
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
            badge.tooltip = "Значок показывается при заданной иконке: для обычного эффектора либо ограниченных стаков (Max Stacks > 0).";
            rightPanel.Add(badge);
        }

        // ======================== «КТО ИСПОЛЬЗУЕТ» ========================

        static void AddUsageSection()
        {
            var foldout = new Foldout { text = "Кто использует", value = false, style = { marginBottom = 4 } };

            var recs = EffectorUsage(AssetDatabase.GetAssetPath(selected));
            if (recs.Count == 0)
                foldout.Add(new Label("Ссылок не найдено (сцены здесь не сканируются — см. вкладку «Чистка»).")
                    { style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.7f, 0.7f, 0.7f) } });
            else
                foreach (var r in recs) foldout.Add(InterflowAbilityUsage.Row(r));

            rightPanel.Add(foldout);
        }

        // Реверс-ссылки через зависимости юнитов и умений (attackEffectors, поля умений).
        static List<InterflowAbilityUsage.Rec> EffectorUsage(string effPath)
        {
            BuildEffectorUsageCache();
            return effectorUsageCache.TryGetValue(effPath, out var l) ? l : new List<InterflowAbilityUsage.Rec>();
        }

        static void BuildEffectorUsageCache()
        {
            if (effectorUsageCache != null) return;
            effectorUsageCache = new Dictionary<string, List<InterflowAbilityUsage.Rec>>();

            var effPaths = new HashSet<string>(effectors.Select(e => AssetDatabase.GetAssetPath(e)));

            void Index(string ownerPath, string label, Action ping)
            {
                foreach (var dep in AssetDatabase.GetDependencies(ownerPath, false))
                {
                    if (!effPaths.Contains(dep)) continue;
                    if (!effectorUsageCache.TryGetValue(dep, out var list))
                    {
                        list = new List<InterflowAbilityUsage.Rec>();
                        effectorUsageCache[dep] = list;
                    }
                    list.Add(new InterflowAbilityUsage.Rec { text = label, ping = ping, warn = false });
                }
            }

            foreach (var go in InterflowAbilityUsage.AllUnitGOs())
            {
                var unit = go.GetComponent<Unit>();
                string uname = unit != null && !string.IsNullOrEmpty(unit.unitName) ? unit.unitName : go.name;
                var gref = go;
                Index(AssetDatabase.GetAssetPath(go), $"Юнит «{uname}» ссылается на эффектор",
                    () => EditorGUIUtility.PingObject(gref));
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Ability"))
            {
                var a = AssetDatabase.LoadAssetAtPath<Ability>(AssetDatabase.GUIDToAssetPath(guid));
                if (a == null) continue;

                var aref = a;
                Index(AssetDatabase.GetAssetPath(a),
                    $"Умение «{InterflowAbilityUsage.AbilityName(a)}» ссылается на эффектор",
                    () => EditorGUIUtility.PingObject(aref));
            }
        }

        // ======================== ХЕЛПЕРЫ ========================

        static string EffectorName(Effector e) => !string.IsNullOrEmpty(e.displayName) ? e.displayName : e.name;
    }
}
