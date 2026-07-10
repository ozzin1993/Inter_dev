using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============================= INTERFLOW EDITOR — ВКЛАДКА «СПРАВОЧНИКИ» (E4, шаг 4) ==
    // Лёгкий CRUD 6 типов: Technology, Resource, ArmorType, DamageType, WeaponSound (ScriptableObject) и Projectile
    // (компонент на префабе). Создание — в путь из настроек; авто-id для типов с полем id (Technology/ArmorType/
    // DamageType/Projectile), у Resource/WeaponSound id нет. Редактор — общий InterflowEditorUI (фолды по [Header]).
    // Правило 1: ассет/SCEditor не правим (штатные AssetDatabase/PrefabUtility). Правило 5: часть окна. Правило 4: RU.
    public static class InterflowCatalogsTab
    {
        class Catalog
        {
            public string label;
            public Type type;
            public bool isPrefab;   // Projectile — компонент на префабе (иначе ScriptableObject)
            public Func<InterflowEditorSettings, string> folder;
            public Catalog(string label, Type type, bool isPrefab, Func<InterflowEditorSettings, string> folder)
            { this.label = label; this.type = type; this.isPrefab = isPrefab; this.folder = folder; }
        }

        static readonly Catalog[] Catalogs =
        {
            new Catalog("Технологии (Technology)",   typeof(Technology),  false, s => s.technologyCreateFolder),
            new Catalog("Ресурсы (Resource)",        typeof(Resource),    false, s => s.resourceCreateFolder),
            new Catalog("Броня (ArmorType)",         typeof(ArmorType),   false, s => s.armorTypeCreateFolder),
            new Catalog("Урон (DamageType)",         typeof(DamageType),  false, s => s.damageTypeCreateFolder),
            new Catalog("Звук оружия (WeaponSound)", typeof(WeaponSound), false, s => s.weaponSoundCreateFolder),
            new Catalog("Снаряды (Projectile)",      typeof(Projectile),  true,  s => s.projectileCreateFolder),
        };

        static int catIndex;
        static List<UnityEngine.Object> items = new List<UnityEngine.Object>();
        static UnityEngine.Object selected;

        static VisualElement listContainer, rightPanel, createHolder;
        static Label countLabel;

        static Catalog Cat => Catalogs[catIndex];

        // ======================== ТОЧКА ВХОДА ВКЛАДКИ ========================

        public static VisualElement CreateTabUI()
        {
            RefreshList();
            if (selected == null || !items.Contains(selected)) selected = items.Count > 0 ? items[0] : null;

            var root = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, flexGrow = 1, minHeight = 500, marginTop = 6, marginLeft = 6, marginRight = 6 }
            };

            var left = new VisualElement { style = { width = 300, flexShrink = 0, marginRight = 8 } };

            var catNames = Catalogs.Select(c => c.label).ToList();
            var catDd = new DropdownField("Справочник", catNames, catIndex);
            catDd.RegisterValueChangedCallback(e =>
            {
                catIndex = Mathf.Max(0, catNames.IndexOf(e.newValue));
                RefreshList();
                selected = items.Count > 0 ? items[0] : null;
                RebuildCreate();
                RebuildList();
                RebuildRightPanel();
            });
            left.Add(catDd);

            createHolder = new VisualElement();
            createHolder.Add(BuildCreateButton());
            left.Add(createHolder);

            left.Add(new Button(() => { RefreshList(); RebuildList(); RebuildRightPanel(); }) { text = "Обновить список" });
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

        static void RebuildCreate()
        {
            if (createHolder == null) return;
            createHolder.Clear();
            createHolder.Add(BuildCreateButton());
        }

        static VisualElement BuildCreateButton() => new Button(CreateItem) { text = $"Создать: {Cat.label}" };

        // ======================== СПИСОК ========================

        static void RefreshList() => items = LoadCatalogItems(Cat).OrderBy(o => o.name).ToList();

        static List<UnityEngine.Object> LoadCatalogItems(Catalog cat)
        {
            var result = new List<UnityEngine.Object>();
            if (cat.isPrefab)
            {
                foreach (var g in AssetDatabase.FindAssets("t:GameObject"))
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                    if (go == null) continue;
                    var comp = go.GetComponent(cat.type);
                    if (comp != null) result.Add(comp);
                }
            }
            else
            {
                foreach (var g in AssetDatabase.FindAssets($"t:{cat.type.Name}"))
                {
                    var o = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(g), cat.type);
                    if (o != null) result.Add(o);
                }
            }
            return result;
        }

        static void RebuildList()
        {
            if (listContainer == null) return;
            listContainer.Clear();
            if (countLabel != null) countLabel.text = $"Всего: {items.Count}";

            foreach (var o in items)
            {
                var obj = o;
                var row = new Button(() => { selected = obj; RebuildList(); RebuildRightPanel(); })
                {
                    text = ItemName(o),
                    style = { unityTextAlign = TextAnchor.MiddleLeft, marginBottom = 1 }
                };
                if (obj == selected)
                {
                    row.style.unityFontStyleAndWeight = FontStyle.Bold;
                    row.style.borderLeftWidth = 3;
                    row.style.borderLeftColor = new Color(0.35f, 0.6f, 0.95f);
                }
                listContainer.Add(row);
            }

            if (items.Count == 0)
                listContainer.Add(new Label("Пусто. Создай кнопкой выше.")
                    { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, color = new Color(0.7f, 0.7f, 0.7f) } });
        }

        // ======================== РЕДАКТОР ========================

        static void RebuildRightPanel()
        {
            if (rightPanel == null) return;
            rightPanel.Clear();

            if (selected == null)
            {
                rightPanel.Add(new Label("Выбери элемент слева или создай.") { style = { marginTop = 6, whiteSpace = WhiteSpace.Normal } });
                return;
            }

            string path = AssetDatabase.GetAssetPath(selected);

            var headRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 } };
            headRow.Add(new Label(ItemName(selected)) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, flexGrow = 1 } });
            headRow.Add(new Button(RebuildRightPanel) { text = "Обновить" });
            headRow.Add(new Button(() => EditorGUIUtility.PingObject(selected)) { text = "Показать" });
            headRow.Add(new Button(DeleteSelected) { text = "Удалить" });
            rightPanel.Add(headRow);
            rightPanel.Add(new Label(path) { style = { color = new Color(0.6f, 0.6f, 0.6f), marginBottom = 6, whiteSpace = WhiteSpace.Normal } });

            rightPanel.Add(new Label("Поля:") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 2 } });
            rightPanel.Add(InterflowEditorUI.BuildGroupedFields(new SerializedObject(selected)));
        }

        static void DeleteSelected()
        {
            if (selected == null) return;
            string path = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(path)) return;
            if (!EditorUtility.DisplayDialog("Удалить", $"Удалить ассет?\n{path}", "Удалить", "Отмена")) return;

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
            RefreshList();
            selected = items.Count > 0 ? items[0] : null;
            RebuildList();
            RebuildRightPanel();
        }

        // ======================== СОЗДАНИЕ ========================

        static void CreateItem()
        {
            var cat = Cat;
            var settings = InterflowEditorSettings.GetOrCreate();
            string folder = cat.folder(settings);
            InterflowEditorUI.EnsureFolder(folder);

            string ext = cat.isPrefab ? "prefab" : "asset";
            string dst = EditorUtility.SaveFilePanelInProject($"Создать: {cat.label}", cat.type.Name, ext, "Имя нового ассета", folder);
            if (string.IsNullOrEmpty(dst)) return;

            string niceName = Path.GetFileNameWithoutExtension(dst);
            UnityEngine.Object created;

            if (cat.isPrefab)
            {
                // Projectile — компонент на префабе: создаём временный GO, вешаем компонент, сохраняем префаб, GO удаляем.
                var go = new GameObject(niceName);
                var comp = go.AddComponent(cat.type);
                SetUniqueId(comp, cat);
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, dst);
                UnityEngine.Object.DestroyImmediate(go);
                created = prefab != null ? prefab.GetComponent(cat.type) : null;
            }
            else
            {
                var so = ScriptableObject.CreateInstance(cat.type);
                SetUniqueId(so, cat);
                SetDisplayNameIfAny(so, niceName);
                AssetDatabase.CreateAsset(so, dst);
                created = so;
            }

            AssetDatabase.SaveAssets();
            RefreshList();
            selected = created;
            RebuildList();
            RebuildRightPanel();
            if (created != null) EditorGUIUtility.PingObject(created);
        }

        // Уникальный id для типов с полем id (Technology/ArmorType/DamageType/Projectile); для Resource/WeaponSound — no-op.
        // Штатный [*ID]-drawer чинит лишь 0/дубль на отрисовке → детерминированный уникальный id он не перетрёт.
        static void SetUniqueId(UnityEngine.Object obj, Catalog cat)
        {
            var so = new SerializedObject(obj);
            var idProp = so.FindProperty("id");
            if (idProp == null || idProp.propertyType != SerializedPropertyType.Integer) return;
            idProp.intValue = NextFreeId(cat);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static int NextFreeId(Catalog cat)
        {
            var used = new HashSet<int>();
            foreach (var o in LoadCatalogItems(cat))
            {
                var p = new SerializedObject(o).FindProperty("id");
                if (p != null && p.propertyType == SerializedPropertyType.Integer) used.Add(p.intValue);
            }
            int id = 1;
            while (used.Contains(id)) id++;
            return id;
        }

        static void SetDisplayNameIfAny(UnityEngine.Object obj, string name)
        {
            var so = new SerializedObject(obj);
            var p = so.FindProperty("displayName");
            if (p != null && p.propertyType == SerializedPropertyType.String)
            {
                p.stringValue = name;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ======================== ХЕЛПЕРЫ ========================

        static string ItemName(UnityEngine.Object o)
        {
            var p = new SerializedObject(o).FindProperty("displayName");
            if (p != null && p.propertyType == SerializedPropertyType.String && !string.IsNullOrEmpty(p.stringValue))
                return p.stringValue;
            return o.name;
        }
    }
}
