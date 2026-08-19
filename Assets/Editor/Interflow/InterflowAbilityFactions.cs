using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============ РЕДАКТОР УМЕНИЙ — ФРАКЦИЯ УМЕНИЯ (единый источник, правило 5) ==
    // Задача Artsiom 2026-08-17: разбор умений по фракциям, чтобы в списке отображались не все сразу.
    //
    // Ability не хранит фракцию как игровой факт — это фракция ссылается на умения, а не наоборот.
    // Поэтому принадлежность считается двумя путями (решения Artsiom 2026-08-17):
    //   1. РУЧНАЯ метка — поле Ability.editorFactions на самом ассете. Заполнена — она и есть ответ,
    //      автоподсчёт при этом НЕ смотрим (приоритет у ручного).
    //   2. АВТО по обратным ссылкам, когда метка пуста:
    //      • FactionConfig.centralAbilities — стартовые умения главного здания;
    //      • unlockAbilities узлов дерева технологий (ступень уровня, оба больших варианта, специализации);
    //      • умения ЮНИТОВ фракции: abilities[] префаба + AutoAbilityUser.autoAbilities.
    //
    // Юниты фракции здесь считаются ШИРЕ, чем в фильтре «Использует раса» вкладки «Юниты»
    // (решение Artsiom 2026-08-17): к waveUnits, трём башням и heroPrefab добавлены юниты из дерева
    // технологий — unlockUnits, unitSwaps.to, towerSwaps.to и heroPrefab больших вариантов.
    // Расхождение двух вкладок намеренное; вкладку «Юниты» не трогаем.
    //
    // Сцены не сканируются (как и в InterflowAbilityUsage) — для них есть вкладка «Чистка — отчёт».
    // Кэш живёт на сессию окна; после создания/удаления/переименования ассетов зовите InvalidateCache().
    public static class InterflowAbilityFactions
    {
        public const string ALL  = "Все";
        public const string NONE = "Без фракции";

        static readonly Color DIM = new Color(0.65f, 0.65f, 0.65f);

        static Dictionary<Ability, HashSet<string>> autoCache;   // умение → имена фракций (только авто)
        static List<string> factionNames;

        /// <summary>Сбросить кэш — звать после создания, удаления и переименования ассетов.</summary>
        public static void InvalidateCache() { autoCache = null; factionNames = null; }

        // ======================== ДАННЫЕ ========================

        /// <summary>Имена ассетов FactionConfig по алфавиту.</summary>
        public static List<string> FactionNames()
        {
            if (factionNames == null)
                factionNames = InterflowAbilityUsage.AllFactions()
                    .Select(f => f.name).Distinct().OrderBy(n => n).ToList();

            return factionNames;
        }

        /// <summary>Пункты выпадающего фильтра: «Все» + каждая фракция + «Без фракции».</summary>
        public static List<string> FilterChoices()
        {
            var list = new List<string> { ALL };
            list.AddRange(FactionNames());
            list.Add(NONE);
            return list;
        }

        /// <summary>Ручная метка умения (имена фракций). Пустой набор — метки нет.</summary>
        public static HashSet<string> Manual(Ability ability)
        {
            var set = new HashSet<string>();
            if (ability == null || ability.editorFactions == null) return set;

            foreach (var f in ability.editorFactions)
                if (f != null) set.Add(f.name);

            return set;
        }

        /// <summary>Фракции, вычисленные по ссылкам (без учёта ручной метки).</summary>
        public static HashSet<string> Auto(Ability ability)
        {
            Build();
            return ability != null && autoCache.TryGetValue(ability, out var s)
                ? new HashSet<string>(s) : new HashSet<string>();
        }

        /// <summary>Итоговые фракции умения: ручная метка, если она есть, иначе — авто по ссылкам.</summary>
        public static HashSet<string> Of(Ability ability)
        {
            var manual = Manual(ability);
            return manual.Count > 0 ? manual : Auto(ability);
        }

        /// <summary>Проходит ли умение через выбранный пункт фильтра.</summary>
        public static bool Matches(Ability ability, string filter)
        {
            if (string.IsNullOrEmpty(filter) || filter == ALL) return true;

            var set = Of(ability);
            return filter == NONE ? set.Count == 0 : set.Contains(filter);
        }

        /// <summary>Самокоррекция значения фильтра: фракция могла исчезнуть после «Обновить».</summary>
        public static string Correct(string filter)
            => filter == ALL || filter == NONE || FactionNames().Contains(filter) ? filter : ALL;

        // ======================== ГОТОВЫЕ ЭЛЕМЕНТЫ UI ========================

        /// <summary>Выпадающий фильтр «Фракция» — вставляется в левую колонку вкладки.</summary>
        public static DropdownField FilterDropdown(string current, Action<string> onChange)
        {
            var choices = FilterChoices();
            var dd = new DropdownField("Фракция", choices, Mathf.Max(0, choices.IndexOf(Correct(current))))
            {
                tooltip = "Показывать умения только выбранной фракции. Фракция берётся из ручной метки умения, " +
                          "а если она пуста — считается по ссылкам: стартовые умения главного здания, узлы дерева " +
                          "технологий и умения юнитов фракции. «Без фракции» — ни метки, ни ссылок."
            };
            dd.RegisterValueChangedCallback(e => onChange(e.newValue));
            return dd;
        }

        /// <summary>Блок «Фракции» правой панели: что посчиталось по ссылкам + поле ручной метки.</summary>
        public static VisualElement Section(SerializedObject so, Ability ability, Action onChanged)
        {
            var box = new VisualElement
            {
                style = { marginTop = 6, paddingTop = 5, paddingBottom = 5, paddingLeft = 8, paddingRight = 8,
                          backgroundColor = new Color(0.23f, 0.24f, 0.25f),
                          borderTopLeftRadius = 3, borderTopRightRadius = 3,
                          borderBottomLeftRadius = 3, borderBottomRightRadius = 3 }
            };
            box.Add(new Label("Фракции") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 3 } });

            var auto = Auto(ability);
            var manual = Manual(ability);
            string autoText = auto.Count > 0
                ? "По ссылкам: " + string.Join(", ", auto.OrderBy(n => n))
                : "По ссылкам фракций не нашлось.";
            if (manual.Count > 0) autoText += "   ·   сейчас действует ручная метка";

            box.Add(new Label(autoText)
            {
                style = { whiteSpace = WhiteSpace.Normal, color = DIM, marginBottom = 3 },
                tooltip = "Автоподсчёт: стартовые умения главного здания, узлы дерева технологий и умения юнитов " +
                          "фракции (включая юнитов, открываемых деревом). Заполненная ручная метка перекрывает его."
            });

            var prop = so.FindProperty("editorFactions");
            if (prop != null)
            {
                var pf = InterflowEditorUI.MakeField(prop, InterflowEditorUI.FieldLabel("editorFactions"), true);
                pf.RegisterValueChangeCallback(_ => onChanged?.Invoke());
                box.Add(pf);
            }

            return box;
        }

        // ======================== СБОРКА КЭША ========================

        static void Build()
        {
            if (autoCache != null) return;
            autoCache = new Dictionary<Ability, HashSet<string>>();

            // Юнит → фракции, которые на него ссылаются. Собирается сначала: у одного юнита может быть
            // несколько фракций-владельцев, и его умения должны попасть в каждую.
            var unitFactions = new Dictionary<Unit, HashSet<string>>();

            foreach (var f in InterflowAbilityUsage.AllFactions())
            {
                // 1. Прямые ссылки фракции на умения.
                if (f.centralAbilities != null)
                    foreach (var ab in f.centralAbilities) AddAbility(ab, f.name);

                foreach (var node in Nodes(f))
                    if (node.unlockAbilities != null)
                        foreach (var e in node.unlockAbilities)
                            if (e != null) AddAbility(e.ability, f.name);

                // 2. Юниты фракции.
                foreach (var u in FactionUnits(f))
                {
                    if (u == null) continue;
                    if (!unitFactions.TryGetValue(u, out var set)) { set = new HashSet<string>(); unitFactions[u] = set; }
                    set.Add(f.name);
                }
            }

            // 3. Умения юнитов фракции: список abilities[] префаба + список автокаста.
            foreach (var kv in unitFactions)
            {
                foreach (var ab in InterflowAbilityUsage.ReadAbilityArray(kv.Key, "abilities"))
                    foreach (var faction in kv.Value) AddAbility(ab, faction);

                var aau = kv.Key.gameObject.GetComponent<AutoAbilityUser>();
                if (aau == null) continue;

                foreach (var ab in InterflowAbilityUsage.ReadAbilityArray(aau, "autoAbilities"))
                    foreach (var faction in kv.Value) AddAbility(ab, faction);
            }
        }

        static void AddAbility(Ability ability, string faction)
        {
            if (ability == null) return;
            if (!autoCache.TryGetValue(ability, out var set)) { set = new HashSet<string>(); autoCache[ability] = set; }
            set.Add(faction);
        }

        /// <summary>Все узлы дерева технологий фракции: ступень уровня, оба варианта и их специализации.</summary>
        static IEnumerable<TechNode> Nodes(FactionConfig f)
        {
            if (f.techTiers == null) yield break;

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

        /// <summary>Юниты фракции: волна, башни, герой + всё, что открывает или подменяет дерево технологий.</summary>
        static IEnumerable<Unit> FactionUnits(FactionConfig f)
        {
            if (f.waveUnits != null)
                foreach (var e in f.waveUnits) if (e != null) yield return e.unit;

            yield return f.centreTower;
            yield return f.defence1Tower;
            yield return f.defence2Tower;
            yield return f.heroPrefab;

            if (f.techTiers != null)
                foreach (var tier in f.techTiers)
                {
                    if (tier == null) continue;
                    foreach (var opt in new[] { tier.optionA, tier.optionB })
                        if (opt != null) yield return opt.heroPrefab;
                }

            foreach (var node in Nodes(f))
            {
                if (node.unlockUnits != null)
                    foreach (var u in node.unlockUnits) yield return u;

                if (node.unitSwaps != null)
                    foreach (var s in node.unitSwaps) if (s != null) yield return s.to;

                if (node.towerSwaps != null)
                    foreach (var s in node.towerSwaps) if (s != null) yield return s.to;
            }
        }
    }
}
