using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============================= INTERFLOW EDITOR — СКАН ССЫЛОК (фаза E1, шаг 4) ==
    // Вкладка «Чистка — отчёт»: для кандидатов чистки (§3 плана) считает ОБРАТНЫЕ ссылки штатным
    // AssetDatabase.GetDependencies (правило 2). Граф сериализации читает и БИНАРНУЮ сцену — открывать
    // сцены не нужно (решение Artsiom 09.07). Контент, грузимый по СТРОКЕ (Resources.Load("имя")), граф
    // не видит — такие имена ищем отдельно и помечаем (§9). E1 только ЧИТАЕТ: ничего не удаляет (правило 7).
    // Пути — не хардкод логики: это дефолты полей UI, пользователь правит в окне (правило 3).
    public static class InterflowUsageScanner
    {
        /// <summary>Кандидат чистки и кто на него ссылается извне audit-папок.</summary>
        public class UsageEntry
        {
            public string path;
            public string name;
            public string type;
            public List<string> referrers = new List<string>();
            public bool nameLoaded;                 // имя встречается в Resources.Load("...") наших скриптов
            public UnityEngine.Object asset;        // для Ping/выделения
        }

        // Кэш последнего скана — список не пропадает при переключении вкладок окна.
        static List<UsageEntry> lastScan;

        // ======================== ДЕФОЛТЫ ПОЛЕЙ UI (правило 3) ========================

        public static string[] DefaultAuditFolders()
        {
            // Демо-дерево Resources, где живут кандидаты §3.1–3.3.
            return new[] { "Assets/StrategyCore/Scenes/DemoFiles/Resources" };
        }

        // §3.4 — мусор вне графа ассетов (файлы/папки). Пути относительно корня репозитория (папка отчёта).
        public static string[] DefaultJunkPaths()
        {
            return new[]
            {
                "Interflow/Assets.7z",
                "Interflow/UnitWaveSpawner.cs",
                "Interflow/Assets/_Recovery",
                "Interflow/Assets/Scenes/SampleScene.unity",
                "Interflow/Assets/TutorialInfo",
                "Interflow/Assets/Resources/PerformanceTestRunInfo.json",
                "Interflow/Assets/Plans",
                "Interflow/Assets/StrategyCore/AnimatorSamples",
                "Interflow/Assets/StrategyCore/_BACKUP_TOOLTIPS",
                "Interflow/Assets/DefaultNetworkPrefabs.asset",
                "Interflow/Assets/StrategyCore/Prefabs/DefaultNetworkPrefabs.asset",
            };
        }

        public static string DefaultOutputFolder()
        {
            // Корень репозитория: на уровень выше Unity-проекта (…/Interflow(repo)/Interflow(unity)/Assets).
            var proj = Directory.GetParent(Application.dataPath);   // …/Interflow(unity)
            var repo = proj?.Parent;                                // …/Interflow(repo)
            return repo != null ? repo.FullName : (proj != null ? proj.FullName : Application.dataPath);
        }

        // ======================== СКАН ОБРАТНЫХ ССЫЛОК ========================

        public static List<UsageEntry> Scan(string[] auditFolders)
        {
            var folders = (auditFolders ?? new string[0])
                .Select(f => f?.Replace('\\', '/').TrimEnd('/'))
                .Where(f => !string.IsNullOrEmpty(f))
                .ToArray();

            bool UnderAudit(string p)
            {
                p = p.Replace('\\', '/');
                foreach (var f in folders) if (p == f || p.StartsWith(f + "/")) return true;
                return false;
            }

            // Кандидаты: все ассеты под audit-папками (не сами папки).
            var byPath = new Dictionary<string, UsageEntry>();
            foreach (var p in AssetDatabase.GetAllAssetPaths())
            {
                if (!p.StartsWith("Assets/")) continue;
                if (!UnderAudit(p)) continue;
                if (AssetDatabase.IsValidFolder(p)) continue;
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
                byPath[p] = new UsageEntry
                {
                    path = p,
                    name = Path.GetFileNameWithoutExtension(p),
                    type = obj != null ? obj.GetType().Name : "?",
                    asset = obj
                };
            }

            // Обратные ссылки: только ВНЕШНИЕ референты (вне audit-папок) — это и есть вопрос безопасности удаления.
            // Прямые зависимости (recursive=false), чтобы список «кто ссылается» был точечным.
            foreach (var referrer in AssetDatabase.GetAllAssetPaths())
            {
                if (!referrer.StartsWith("Assets/")) continue;
                if (AssetDatabase.IsValidFolder(referrer)) continue;
                if (UnderAudit(referrer)) continue;
                foreach (var dep in AssetDatabase.GetDependencies(referrer, false))
                {
                    if (dep == referrer) continue;
                    if (byPath.TryGetValue(dep, out var e)) e.referrers.Add(referrer);
                }
            }

            // Строковые Resources.Load — пометить кандидатов, чьё имя грузится по строке (§9: возможен ложный «не используется»).
            var loadNames = CollectResourcesLoadNames();
            foreach (var e in byPath.Values) e.nameLoaded = loadNames.Contains(e.name);

            lastScan = byPath.Values.OrderBy(e => e.referrers.Count).ThenBy(e => e.path).ToList();
            return lastScan;
        }

        // Строковые аргументы Resources.Load / Resources.LoadAll в скриптах проекта.
        static HashSet<string> CollectResourcesLoadNames()
        {
            var names = new HashSet<string>();
            var rxGeneric = new Regex("Resources\\.(Load|LoadAll)\\s*<[^>]*>\\s*\\(\\s*\"([^\"]*)\"", RegexOptions.Compiled);
            var rxPlain = new Regex("Resources\\.(Load|LoadAll)\\s*\\(\\s*\"([^\"]*)\"", RegexOptions.Compiled);
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.EndsWith(".cs")) continue;
                string text;
                try { text = File.ReadAllText(p); } catch { continue; }
                foreach (Match m in rxGeneric.Matches(text)) AddLeaf(names, m.Groups[2].Value);
                foreach (Match m in rxPlain.Matches(text)) AddLeaf(names, m.Groups[2].Value);
            }
            return names;
        }

        // Из "UnitPrefabs/Foo" берём листовое имя "Foo" (Resources.Load грузит по относительному пути без расширения).
        static void AddLeaf(HashSet<string> set, string arg)
        {
            if (string.IsNullOrEmpty(arg)) return;
            var leaf = arg.Replace('\\', '/');
            int i = leaf.LastIndexOf('/');
            if (i >= 0) leaf = leaf.Substring(i + 1);
            if (leaf.Length > 0) set.Add(leaf);
        }

        // ======================== ЭКСПОРТ ОТЧЁТА .md ========================

        public static string ExportReport(List<UsageEntry> entries, string[] auditFolders, string[] junkPaths, string outputFolder)
        {
            var sb = new StringBuilder();
            string date = DateTime.Now.ToString("yyyy-MM-dd");

            sb.AppendLine("# Отчёт E1 — обратные ссылки (кандидаты чистки §3)");
            sb.AppendLine();
            sb.AppendLine($"**Дата:** {date}. **Механизм:** штатный `AssetDatabase.GetDependencies` (граф сериализации; читает и БИНАРНУЮ сцену — открытие не требуется).");
            sb.AppendLine("Учитываются ВНЕШНИЕ ссылки (референты вне audit-папок) — это вопрос безопасности удаления. " +
                          "«Строк.Load» = имя ассета встречается в `Resources.Load(\"...\")` наших скриптов (граф этого не видит — проверить вручную, §9).");
            sb.AppendLine();
            sb.AppendLine("**Audit-папки:** " + string.Join(", ", auditFolders ?? new string[0]));
            sb.AppendLine();

            int unused = entries.Count(e => e.referrers.Count == 0);
            sb.AppendLine($"Всего кандидатов: {entries.Count}. Без внешних ссылок (кандидаты на удаление): {unused}.");
            sb.AppendLine();
            sb.AppendLine("| Ассет | Тип | Путь | Ссылок | Кто ссылается | Строк.Load | Решение (Да/Нет) |");
            sb.AppendLine("|---|---|---|---:|---|:---:|:---:|");
            foreach (var e in entries)
            {
                string who = e.referrers.Count == 0 ? "—" : string.Join("<br>", e.referrers.Take(8));
                if (e.referrers.Count > 8) who += $"<br>…(+{e.referrers.Count - 8})";
                string load = e.nameLoaded ? "⚠ да" : "";
                sb.AppendLine($"| {e.name} | {e.type} | {e.path} | {e.referrers.Count} | {who} | {load} | |");
            }
            sb.AppendLine();

            // §3.4 — файлы/папки вне графа ассетов: граф ссылок к ним неприменим, только существование.
            sb.AppendLine("## §3.4 — мусор вне графа ассетов (проверка существования; ссылки графом не видны)");
            sb.AppendLine();
            sb.AppendLine("| Путь | Существует | Решение (Да/Нет) |");
            sb.AppendLine("|---|:---:|:---:|");
            foreach (var jp in junkPaths ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(jp)) continue;
                string full = Path.IsPathRooted(jp) ? jp : Path.Combine(outputFolder, jp);
                bool exists = File.Exists(full) || Directory.Exists(full);
                sb.AppendLine($"| {jp} | {(exists ? "да" : "нет")} | |");
            }
            sb.AppendLine();
            sb.AppendLine("> E1 только читает. Разметку «Решение (Да/Нет)» ведёт Artsiom; исполнение удаления — фаза E2 (git-чекпойнт до, прогон в Unity после).");

            Directory.CreateDirectory(outputFolder);
            string file = Path.Combine(outputFolder, $"Отчёт_E1_Ссылки_{date}.md");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
            return file;
        }

        // ======================== UI ВКЛАДКИ «ЧИСТКА — ОТЧЁТ» ========================

        public static VisualElement CreateTabUI()
        {
            var root = new VisualElement { style = { marginTop = 6, marginLeft = 6, marginRight = 6 } };

            var info = new Label("Обратные ссылки на кандидатов чистки (§3). Только ОТЧЁТ — ничего не удаляет (E1, правило 7). " +
                                 "Механизм: AssetDatabase.GetDependencies (читает и бинарную сцену). Скан может занять время на большом проекте.")
            { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 4 } };

            var auditField = new TextField("Audit-папки (через ;)") { value = string.Join(";", DefaultAuditFolders()) };
            auditField.style.marginBottom = 2;
            var junkField = new TextField("§3.4 пути (через ;)") { value = string.Join(";", DefaultJunkPaths()), multiline = true };
            junkField.style.marginBottom = 2;
            var outField = new TextField("Папка отчёта") { value = DefaultOutputFolder() };
            outField.style.marginBottom = 4;

            var scanBtn = new Button { text = "Сканировать" };
            var exportBtn = new Button { text = "Экспорт .md" };
            exportBtn.SetEnabled(false);

            var summary = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4, marginBottom = 4, whiteSpace = WhiteSpace.Normal } };
            var listRoot = new VisualElement();

            string[] Split(string s) => (s ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

            scanBtn.clicked += () =>
            {
                try
                {
                    EditorUtility.DisplayProgressBar("Interflow Editor", "Скан обратных ссылок (GetDependencies)…", 0.5f);
                    Scan(Split(auditField.value));
                }
                finally { EditorUtility.ClearProgressBar(); }
                exportBtn.SetEnabled(lastScan != null && lastScan.Count > 0);
                RedrawScan(summary, listRoot);
            };

            exportBtn.clicked += () =>
            {
                if (lastScan == null) return;
                string path = ExportReport(lastScan, Split(auditField.value), Split(junkField.value), outField.value);
                summary.text = "Отчёт записан: " + path;
                EditorUtility.RevealInFinder(path);
            };

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            buttons.Add(scanBtn);
            buttons.Add(exportBtn);

            root.Add(info);
            root.Add(auditField);
            root.Add(junkField);
            root.Add(outField);
            root.Add(buttons);
            root.Add(summary);
            root.Add(listRoot);

            // Если скан уже был в этой сессии окна — показать кэш.
            if (lastScan != null) RedrawScan(summary, listRoot);
            return root;
        }

        static void RedrawScan(Label summary, VisualElement listRoot)
        {
            listRoot.Clear();
            if (lastScan == null) return;

            int unused = lastScan.Count(e => e.referrers.Count == 0);
            summary.text = $"Кандидатов: {lastScan.Count}   Без внешних ссылок: {unused}";

            foreach (var e in lastScan)
                listRoot.Add(CreateRow(e));
        }

        static VisualElement CreateRow(UsageEntry e)
        {
            var row = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, marginBottom = 2, alignItems = Align.FlexStart }
            };

            var count = new Label(e.referrers.Count.ToString())
            {
                style =
                {
                    width = 28, flexShrink = 0, unityFontStyleAndWeight = FontStyle.Bold,
                    color = e.referrers.Count == 0 ? new Color(0.95f, 0.33f, 0.31f) : new Color(0.55f, 0.75f, 0.98f)
                }
            };

            string txt = $"{e.name} [{e.type}]" + (e.nameLoaded ? "   ⚠ строковый Load" : "") + "\n" +
                         (e.referrers.Count == 0 ? "нет внешних ссылок" : string.Join(", ", e.referrers.Take(5)));
            var label = new Label(txt) { style = { whiteSpace = WhiteSpace.Normal, flexGrow = 1 } };

            row.Add(count);
            row.Add(label);

            if (e.asset != null)
            {
                var ping = new Button(() =>
                {
                    Selection.activeObject = e.asset;
                    EditorGUIUtility.PingObject(e.asset);
                })
                { text = "Показать", style = { flexShrink = 0 } };
                row.Add(ping);
            }

            return row;
        }
    }
}
