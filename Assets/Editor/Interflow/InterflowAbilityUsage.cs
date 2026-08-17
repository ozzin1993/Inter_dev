using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============ INTERFLOW EDITOR — «КТО ИСПОЛЬЗУЕТ» (единый источник, правило 5) ==
    // Реверс-ссылки на умение: какие юниты его носят, какие фракции дают стартовым умением ГЗ
    // и какие узлы дерева технологий его открывают. Раньше это жило приватно во вкладке
    // «Умения и эффекторы»; по решению Artsiom 2026-08-09 фолд нужен и в конструкторах,
    // поэтому вынесен сюда — одна реализация на три вкладки.
    //
    // Сцены здесь НЕ сканируются: для них есть вкладка «Чистка — отчёт» (InterflowUsageScanner).
    // Кэш живёт на сессию окна; после создания или удаления ассетов зовите InvalidateCache().
    public static class InterflowAbilityUsage
    {
        /// <summary>Одна строка «кто использует»: текст, переход к объекту, признак предупреждения.</summary>
        public struct Rec
        {
            public string text;
            public Action ping;
            public bool warn;
        }

        static Dictionary<Ability, List<Rec>> cache;

        /// <summary>Сбросить кэш — звать после создания, удаления и переименования ассетов.</summary>
        public static void InvalidateCache() => cache = null;

        /// <summary>Записи «кто использует» для умения. Пустой список, если ссылок нет.</summary>
        public static List<Rec> Of(Ability ability)
        {
            Build();
            return ability != null && cache.TryGetValue(ability, out var l) ? l : new List<Rec>();
        }

        // ======================== ГОТОВЫЙ БЛОК UI ========================

        /// <summary>Свёрнутый фолд «Кто использует» — вставляется в правую панель любой вкладки.</summary>
        public static VisualElement Section(Ability ability)
        {
            var foldout = new Foldout { text = "Кто использует", value = false, style = { marginBottom = 4 } };

            var recs = Of(ability);
            if (recs.Count == 0)
                foldout.Add(new Label("Ссылок не найдено (сцены здесь не сканируются — см. вкладку «Чистка»).")
                    { style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.7f, 0.7f, 0.7f) } });
            else
                foreach (var r in recs) foldout.Add(Row(r));

            return foldout;
        }

        /// <summary>Одна строка списка. Публичная — её же рисует список эффекторов.</summary>
        public static VisualElement Row(Rec r)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 1 } };
            row.Add(new Label("● " + r.text)
            {
                style = { whiteSpace = WhiteSpace.Normal, flexGrow = 1, color = r.warn ? new Color(0.98f, 0.78f, 0.28f) : Color.white }
            });
            row.Add(new Button(r.ping) { text = "→", tooltip = "Показать в Project" });
            return row;
        }

        // ======================== СБОРКА КЭША ========================

        static void Build()
        {
            if (cache != null) return;
            cache = new Dictionary<Ability, List<Rec>>();

            // Юниты: abilities[] и список авто-умений (+ предупреждение, если умения нет в abilities[]).
            foreach (var go in AllUnitGOs())
            {
                var unit = go.GetComponent<Unit>();
                string uname = unit != null && !string.IsNullOrEmpty(unit.unitName) ? unit.unitName : go.name;
                var gref = go;

                var inList = ReadAbilityArray(unit, "abilities");
                foreach (var ab in inList)
                    if (ab != null) Add(ab, $"Юнит «{uname}»: abilities[]", () => EditorGUIUtility.PingObject(gref), false);

                // С 2026-08-08 элемент списка автокаста — сама ссылка на умение, а не запись с полем «ability»:
                // настройки поиска с компонента убраны, они живут в самом умении.
                var aau = go.GetComponent<AutoAbilityUser>();
                if (aau == null) continue;

                var entries = new SerializedObject(aau).FindProperty("autoAbilities");
                for (int i = 0; entries != null && i < entries.arraySize; i++)
                {
                    if (!(entries.GetArrayElementAtIndex(i).objectReferenceValue is Ability auto)) continue;

                    bool present = inList.Contains(auto);
                    Add(auto, $"Юнит «{uname}»: авто-умение №{i + 1}" + (present ? "" : "  ⚠ не в abilities[]"),
                        () => EditorGUIUtility.PingObject(gref), !present);
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
                        if (ab != null)
                            Add(ab, $"Фракция «{f.name}»: стартовое умение ГЗ, ячейка {i}",
                                () => EditorGUIUtility.PingObject(fref), false);
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
                    Add(e.ability, $"Фракция «{f.name}»: открывает узел «{nodeName}», ячейка {e.slot}",
                        () => EditorGUIUtility.PingObject(fref), node.technology == null);
        }

        static void Add(Ability key, string text, Action ping, bool warn)
        {
            if (!cache.TryGetValue(key, out var list)) { list = new List<Rec>(); cache[key] = list; }
            list.Add(new Rec { text = text, ping = ping, warn = warn });
        }

        // ======================== ОБЩИЕ ЧИТАЛКИ ========================
        // Публичные: их же зовёт кэш эффекторов во вкладке «Эффекторы» (правило 5).

        public static List<Ability> ReadAbilityArray(UnityEngine.Object obj, string prop)
        {
            var list = new List<Ability>();
            if (obj == null) return list;

            var p = new SerializedObject(obj).FindProperty(prop);
            if (p != null && p.isArray)
                for (int i = 0; i < p.arraySize; i++)
                    list.Add(p.GetArrayElementAtIndex(i).objectReferenceValue as Ability);

            return list;
        }

        public static IEnumerable<GameObject> AllUnitGOs() =>
            AssetDatabase.FindAssets("t:GameObject")
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(go => go != null && go.GetComponent<Unit>() != null);

        public static IEnumerable<FactionConfig> AllFactions() =>
            AssetDatabase.FindAssets("t:FactionConfig")
                .Select(g => AssetDatabase.LoadAssetAtPath<FactionConfig>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(f => f != null);

        public static string AbilityName(Ability a) =>
            a != null && a.abilityName != null && a.abilityName.Length > 0 && !string.IsNullOrEmpty(a.abilityName[0])
                ? a.abilityName[0] : (a != null ? a.name : "");
    }
}
