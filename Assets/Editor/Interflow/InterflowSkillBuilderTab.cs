using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============ INTERFLOW EDITOR — ВКЛАДКА «КОНСТРУКТОР СКИЛЛОВ» (ADR-006, вариант 1) ==
    // Одиннадцать блоков CompositeSkill показываются ДВУМЯ списками:
    //   ⚡ события   — у эффекта нет длительности: он происходит и заканчивается;
    //   ⏳ состояния — длительность есть: висит на цели во времени.
    // Правило для ГД одно: есть осмысленная длительность — состояние, нет — событие.
    //
    // Модель данных НЕ меняется: вкладка раскладывает существующие поля. Рантайм-рефактор
    // (слияние SkillBuff/AbsorbShield/ControlImmunity в Effector) поменяет то, что за экраном,
    // а не сам экран — поэтому вкладку можно делать до него.
    //
    // Правило 5: правила проверок не дублируются — вкладка зовёт InterflowValidator.
    // Правило 4: UI по-русски. Правило 22: разбита партиалами (.Steps.cs — списки и шкала каста).
    public static partial class InterflowSkillBuilderTab
    {
        // ======================== ОПИСАНИЕ БЛОКОВ — ЕДИНЫЙ ИСТОЧНИК ========================

        /// <summary>Чем блок является для геймдизайнера: разовым событием или длящимся состоянием.</summary>
        public enum StepKind { Event, State }

        /// <summary>
        /// Описание одного блока конструктора. Порядок здесь — РЕАЛЬНЫЙ порядок исполнения
        /// из CompositeSkill.ApplyEffects, а не порядок полей в инспекторе. Единственное место,
        /// где эта таблица живёт: и списки, и подписи, и привязка сообщений валидатора берутся отсюда.
        /// </summary>
        class BlockDesc
        {
            public int order;            // номер шага в ApplyEffects
            public string field;         // имя сериализованного поля CompositeSkill
            public string title;         // заголовок для ГД
            public StepKind kind;        // событие или состояние
            public bool perTarget;       // применяется к каждой цели (блоки 2..8) или один раз за каст (1, 9..11)
            public string hint;          // короткое пояснение под заголовком
            public string[] keys;        // ключевые слова для привязки сообщений валидатора к блоку
        }

        static readonly BlockDesc[] BLOCKS =
        {
            new BlockDesc { order = 1,  field = "selfCost",        title = "Стоимость в здоровье",     kind = StepKind.Event, perTarget = false,
                            hint = "списывается с кастера один раз за каст, до всего остального",
                            keys = new[]{ "стоимость в здоровье" } },
            new BlockDesc { order = 2,  field = "damage",          title = "Урон",                     kind = StepKind.Event, perTarget = true,
                            hint = "записей может быть несколько, у каждой свой тип урона и «кого задевает»",
                            keys = new[]{ "урона", "урон" } },
            new BlockDesc { order = 3,  field = "status",          title = "Контроль",                 kind = StepKind.Event, perTarget = true,
                            hint = "оглушение, обезоруживание, немота",
                            keys = new[]{ "контроль" } },
            new BlockDesc { order = 4,  field = "effectors",       title = "Эффекторы",                kind = StepKind.State, perTarget = true,
                            hint = "сила и длительность задаются ЗДЕСЬ, по уровням; ассет отвечает только за то, ЧТО происходит",
                            keys = new[]{ "эффектор" } },
            new BlockDesc { order = 5,  field = "heal",            title = "Лечение",                  kind = StepKind.Event, perTarget = true,
                            hint = "мгновенное; лечение во времени — это баф или эффектор",
                            keys = new[]{ "лечение" } },
            new BlockDesc { order = 6,  field = "buff",            title = "Длящийся баф",             kind = StepKind.State, perTarget = true,
                            hint = "то, чего штатный эффектор не умеет: аура вокруг носителя, множитель урона, иммунитет, детонация",
                            keys = new[]{ "баф", "ауры", "взрыв" } },
            new BlockDesc { order = 7,  field = "shield",          title = "Поглощающий щит",          kind = StepKind.State, perTarget = true,
                            hint = "снимается при пробитии или по таймеру",
                            keys = new[]{ "щит" } },
            new BlockDesc { order = 8,  field = "blind",           title = "Ослепление",               kind = StepKind.State, perTarget = true,
                            hint = "шанс промаха на время",
                            keys = new[]{ "ослеплени" } },
            new BlockDesc { order = 9,  field = "summon",          title = "Призыв",                   kind = StepKind.Event, perTarget = false,
                            hint = "исполняется один раз за каст — набор целей ему не нужен",
                            keys = new[]{ "призыв" } },
            new BlockDesc { order = 10, field = "groundZone",      title = "Зона на земле",            kind = StepKind.Event, perTarget = false,
                            hint = "исполняется один раз за каст; урон и эффекты живут на префабе зоны",
                            keys = new[]{ "зон" } },
            new BlockDesc { order = 11, field = "delegateService", title = "Серверный сервис",         kind = StepKind.Event, perTarget = false,
                            hint = "метеоритный дождь, подъём павших — процессы во времени на стороне матча",
                            keys = new[]{ "сервис" } },
        };

        // ======================== СОСТОЯНИЕ ВКЛАДКИ ========================

        static List<CompositeSkill> skills = new List<CompositeSkill>();
        static CompositeSkill selected;
        static int previewLevel;
        static string filter = "";

        static Dictionary<CompositeSkill, List<InterflowIssue>> issuesBySkill;

        static VisualElement listContainer, rightPanel;
        static Label countLabel;

        static readonly Color COL_EVENT = new Color(0.88f, 0.50f, 0.24f);
        static readonly Color COL_STATE = new Color(0.29f, 0.64f, 0.63f);
        static readonly Color COL_ERR   = new Color(0.90f, 0.33f, 0.29f);
        static readonly Color COL_WARN  = new Color(0.89f, 0.70f, 0.25f);
        static readonly Color COL_OK    = new Color(0.34f, 0.65f, 0.29f);
        static readonly Color COL_DIM   = new Color(0.62f, 0.62f, 0.62f);

        // ======================== ТОЧКА ВХОДА ========================

        public static VisualElement CreateTabUI()
        {
            RefreshAll();

            var root = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, flexGrow = 1, minHeight = 500,
                          marginTop = 6, marginLeft = 6, marginRight = 6 }
            };

            var left = new VisualElement { style = { width = 300, flexShrink = 0, marginRight = 8 } };

            var search = new TextField("Поиск") { value = filter };
            search.RegisterValueChangedCallback(e => { filter = e.newValue; RebuildList(); });
            left.Add(search);

            left.Add(new Button(() => { RefreshAll(); RebuildList(); RebuildRightPanel(); })
                { text = "Обновить и перепроверить" });

            countLabel = new Label { style = { marginTop = 2, marginBottom = 2, color = COL_DIM } };
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

        // ======================== ДАННЫЕ ========================

        static void RefreshAll()
        {
            skills = AssetDatabase.FindAssets("t:CompositeSkill")
                .Select(g => AssetDatabase.LoadAssetAtPath<CompositeSkill>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null).OrderBy(s => s.name).ToList();

            issuesBySkill = InterflowValidator.ValidateAllSkills();

            if (selected == null || !skills.Contains(selected)) selected = skills.FirstOrDefault();
        }

        /// <summary>Перепроверить ТОЛЬКО выбранный скилл — после правки поля, без перечитывания проекта.</summary>
        static void RevalidateSelected()
        {
            if (selected == null || issuesBySkill == null) return;
            issuesBySkill[selected] = InterflowValidator.ValidateSkillAlone(selected);
        }

        static List<InterflowIssue> IssuesOf(CompositeSkill s) =>
            s != null && issuesBySkill != null && issuesBySkill.TryGetValue(s, out var l)
                ? l : new List<InterflowIssue>();

        // ======================== СПИСОК СЛЕВА ========================

        static void RebuildList()
        {
            if (listContainer == null) return;
            listContainer.Clear();

            var shown = skills.Where(s => string.IsNullOrEmpty(filter)
                                          || s.name.ToLower().Contains(filter.ToLower())
                                          || SkillTitle(s).ToLower().Contains(filter.ToLower())).ToList();

            int err = skills.Count(s => IssuesOf(s).Any(i => i.severity == InterflowIssueSeverity.Error));
            if (countLabel != null)
                countLabel.text = $"Скиллов: {skills.Count}   ·   с ошибками: {err}";

            foreach (var s in shown) listContainer.Add(ListRow(s));

            if (shown.Count == 0)
                listContainer.Add(new Label("Ничего не найдено.")
                    { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, color = COL_DIM } });
        }

        static VisualElement ListRow(CompositeSkill s)
        {
            var issues = IssuesOf(s);
            Color dot = issues.Any(i => i.severity == InterflowIssueSeverity.Error) ? COL_ERR
                      : issues.Any(i => i.severity == InterflowIssueSeverity.Warning) ? COL_WARN
                      : COL_OK;

            var row = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 1 }
            };

            var mark = new VisualElement
            {
                style = { width = 8, height = 8, marginRight = 4, marginLeft = 2, backgroundColor = dot,
                          borderTopLeftRadius = 4, borderTopRightRadius = 4,
                          borderBottomLeftRadius = 4, borderBottomRightRadius = 4 }
            };
            mark.tooltip = issues.Count == 0 ? "Проверки чисты" : string.Join("\n", issues.Select(i => i.message));
            row.Add(mark);

            var target = s;
            var btn = new Button(() => { selected = target; RebuildList(); RebuildRightPanel(); })
            {
                text = SkillTitle(s),
                style = { unityTextAlign = TextAnchor.MiddleLeft, flexGrow = 1, marginBottom = 0 }
            };
            if (s == selected)
            {
                btn.style.unityFontStyleAndWeight = FontStyle.Bold;
                btn.style.borderLeftWidth = 3;
                btn.style.borderLeftColor = new Color(0.35f, 0.6f, 0.95f);
            }
            row.Add(btn);
            return row;
        }

        static string SkillTitle(CompositeSkill s) =>
            s.abilityName != null && s.abilityName.Length > 0 && !string.IsNullOrEmpty(s.abilityName[0])
                ? s.abilityName[0] : s.name;

        // ======================== ПРАВАЯ ПАНЕЛЬ ========================

        static void RebuildRightPanel()
        {
            if (rightPanel == null) return;
            rightPanel.Clear();

            if (selected == null)
            {
                rightPanel.Add(new Label("Скиллов конструктора в проекте нет. Создай умение типа «Конструктор скилла» во вкладке «Умения и эффекторы».")
                    { style = { whiteSpace = WhiteSpace.Normal, marginTop = 6 } });
                return;
            }

            AddHeader();
            AddSummaryCard();
            AddGeneralIssues();
            AddAimSection();
            AddCastTimeline();
            AddStepLists();
            AddLevelFooter();
        }

        static void AddHeader()
        {
            var head = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            head.Add(new Label(SkillTitle(selected))
                { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 15, flexGrow = 1 } });
            head.Add(new Button(() => EditorGUIUtility.PingObject(selected)) { text = "Показать" });
            head.Add(new Button(() => { RefreshAll(); RebuildList(); RebuildRightPanel(); }) { text = "Перепроверить" });
            rightPanel.Add(head);

            string type;
            try { type = selected.type.ToString(); } catch { type = "?"; }

            rightPanel.Add(new Label($"id {selected.id}   ·   тип: {type} (следует из режима цели, руками не задаётся)   ·   {AssetDatabase.GetAssetPath(selected)}")
                { style = { color = COL_DIM, marginBottom = 6, whiteSpace = WhiteSpace.Normal } });
        }

        static void AddSummaryCard()
        {
            var card = new Label(selected.BuildSummary())
            {
                style =
                {
                    whiteSpace = WhiteSpace.Normal, marginBottom = 6,
                    paddingTop = 5, paddingBottom = 5, paddingLeft = 7, paddingRight = 7,
                    backgroundColor = new Color(0.17f, 0.19f, 0.25f),
                    borderLeftWidth = 3, borderLeftColor = new Color(0.30f, 0.49f, 1f),
                    borderTopRightRadius = 3, borderBottomRightRadius = 3
                }
            };
            card.tooltip = "Автосводка по включённым блокам. Значения — для первого уровня.";
            rightPanel.Add(card);
        }

        /// <summary>Сообщения валидатора, которые не удалось привязать ни к одному блоку.</summary>
        static void AddGeneralIssues()
        {
            var rest = IssuesOf(selected).Where(i => BlockFor(i) == null).ToList();
            foreach (var i in rest) rightPanel.Add(IssueRow(i));
        }

        static VisualElement IssueRow(InterflowIssue issue)
        {
            Color c = issue.severity == InterflowIssueSeverity.Error ? COL_ERR
                    : issue.severity == InterflowIssueSeverity.Warning ? COL_WARN
                    : new Color(0.62f, 0.72f, 0.85f);
            string sign = issue.severity == InterflowIssueSeverity.Error ? "✕"
                        : issue.severity == InterflowIssueSeverity.Warning ? "!" : "i";

            var row = new Label($"{sign}  {issue.message}")
            {
                tooltip = "Источник правила: " + issue.source,
                style =
                {
                    whiteSpace = WhiteSpace.Normal, color = c,
                    marginBottom = 3, paddingTop = 3, paddingBottom = 3, paddingLeft = 6, paddingRight = 6,
                    backgroundColor = new Color(c.r * 0.16f, c.g * 0.16f, c.b * 0.16f),
                    borderLeftWidth = 2, borderLeftColor = c
                }
            };
            return row;
        }

        /// <summary>К какому блоку относится сообщение валидатора (по ключевым словам из таблицы).</summary>
        static BlockDesc BlockFor(InterflowIssue issue)
        {
            string m = issue.message.ToLower();
            foreach (var b in BLOCKS)
                foreach (var k in b.keys)
                    if (m.Contains(k)) return b;
            return null;
        }
    }
}
