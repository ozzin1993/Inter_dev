using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============ РЕДАКТОР УМЕНИЙ — ВКЛАДКА «БОЕВЫЕ УМЕНИЯ» (бывший «Конструктор скиллов») ==
    // Редизайн 2026-08-16 (решения Artsiom, макет interflow_editor_redesign v2):
    //   • полоса чипов «Состав умения»: 18 блоков включаются одним кликом, развёрнуты только включённые;
    //   • лента исполнения: «по каждой цели» и «один раз за каст» в реальном порядке ApplyEffects,
    //     шапка ленты — замах → срабатывание → откат;
    //   • переключатель уровня — наверху, а не в подвале;
    //   • ОДИН SerializedObject и ОДНА подписка на правки вместо семи + кэш контекста проверок
    //     (раньше каждое нажатие клавиши перечитывало весь проект семь раз);
    //   • подсказки — только в tooltip (по наведению), статических пояснений нет.
    //
    // Правило 5: правила проверок не дублируются — вкладка зовёт InterflowValidator.
    // Правило 4: UI по-русски. Правило 22: партиалы (.Steps.cs — секции и лента, .Wizard.cs — мастер).
    public static partial class InterflowSkillBuilderTab
    {
        // Уровень, для которого правая панель показывает числа (0-базный).
        static int previewLevel;

        // ======================== ОПИСАНИЕ БЛОКОВ — ЕДИНЫЙ ИСТОЧНИК ========================

        /// <summary>Чем блок является для геймдизайнера: разовым событием или длящимся во времени.</summary>
        public enum StepKind { Event, State }

        /// <summary>
        /// Описание одного блока умения. Порядок здесь — РЕАЛЬНЫЙ порядок исполнения
        /// из CompositeSkill.ApplyEffects. Единственное место, где эта таблица живёт:
        /// чипы, лента, карточки и привязка сообщений валидатора берутся отсюда.
        /// </summary>
        class BlockDesc
        {
            public int order;            // номер шага в ApplyEffects
            public string field;         // имя сериализованного поля CompositeSkill
            public string title;         // заголовок для ГД
            public StepKind kind;        // событие или длящееся
            public bool perTarget;       // применяется к каждой цели или один раз за каст
            public string hint;          // пояснение — уходит в tooltip чипа и карточки
        }

        static readonly BlockDesc[] BLOCKS =
        {
            new BlockDesc { order = 1,  field = "selfCost",        title = "Стоимость в здоровье",     kind = StepKind.Event, perTarget = false,
                            hint = "списывается с кастера один раз за каст, до всего остального" },
            new BlockDesc { order = 2,  field = "pull",            title = "Рывок цели к кастеру",     kind = StepKind.Event, perTarget = true,
                            hint = "идёт первым: не притянулась (иммунитет, неподвижная, нет места) — цель выпадает из каста целиком" },
            new BlockDesc { order = 3,  field = "damage",          title = "Урон",                     kind = StepKind.Event, perTarget = true,
                            hint = "записей может быть несколько, у каждой свой тип урона и «кого задевает»" },
            new BlockDesc { order = 4,  field = "drain",           title = "Высасывание ХП",           kind = StepKind.Event, perTarget = true,
                            hint = "снимает здоровье НАПРЯМУЮ, мимо брони и щитов; долю снятого получает кастер" },
            new BlockDesc { order = 5,  field = "status",          title = "Контроль",                 kind = StepKind.Event, perTarget = true,
                            hint = "оглушение, обезоруживание, немота" },
            new BlockDesc { order = 6,  field = "effectors",       title = "Состояния",                kind = StepKind.State, perTarget = true,
                            hint = "сила и длительность задаются ЗДЕСЬ, по уровням; ассет состояния отвечает только за то, ЧТО происходит" },
            new BlockDesc { order = 7,  field = "heal",            title = "Лечение",                  kind = StepKind.Event, perTarget = true,
                            hint = "мгновенное; лечение во времени — это длящийся баф или состояние" },
            new BlockDesc { order = 8,  field = "mana",            title = "Восстановление маны",      kind = StepKind.Event, perTarget = true,
                            hint = "числом и долей от максимального запаса цели" },
            new BlockDesc { order = 9,  field = "buff",            title = "Длящийся баф",             kind = StepKind.State, perTarget = true,
                            hint = "то, чего штатное состояние не умеет: аура вокруг носителя, множитель урона, иммунитет, детонация" },
            new BlockDesc { order = 10, field = "shield",          title = "Поглощающий щит",          kind = StepKind.State, perTarget = true,
                            hint = "снимается при пробитии или по таймеру; умеет отвечать бьющим и давать вспышку при пробитии" },
            new BlockDesc { order = 11, field = "blind",           title = "Ослепление",               kind = StepKind.State, perTarget = true,
                            hint = "шанс промаха на время" },
            new BlockDesc { order = 12, field = "morph",           title = "Подмена облика",           kind = StepKind.State, perTarget = true,
                            hint = "цель принимает вид другого юнита на время; статы меняются только заданными пассивными эффектами" },
            new BlockDesc { order = 13, field = "ownership",       title = "Смена владельца",          kind = StepKind.Event, perTarget = true,
                            hint = "цель навсегда переходит к владельцу кастера; идёт после всех эффектов" },
            new BlockDesc { order = 14, field = "secondary",       title = "Вторичные цели",           kind = StepKind.Event, perTarget = true,
                            hint = "своя выборка ВОКРУГ каждой основной цели — например вылечить тех, кто её бьёт" },
            new BlockDesc { order = 15, field = "knockback",       title = "Отбросить цели",           kind = StepKind.Event, perTarget = true,
                            hint = "идёт последним по каждой цели: всё позиционное (вторичные цели, зона, визуал) считается до сдвига" },
            new BlockDesc { order = 16, field = "summon",          title = "Призыв",                   kind = StepKind.Event, perTarget = false,
                            hint = "исполняется один раз за каст — набор целей ему не нужен" },
            new BlockDesc { order = 17, field = "groundZone",      title = "Зона на земле",            kind = StepKind.Event, perTarget = false,
                            hint = "исполняется один раз за каст; урон и эффекты живут на префабе зоны" },
            new BlockDesc { order = 18, field = "casterMove",      title = "Перемещение кастера",      kind = StepKind.Event, perTarget = false,
                            hint = "кастер переносится в точку приложения — телепорт, рывок к месту" },
            new BlockDesc { order = 19, field = "delegateService", title = "Серверный сервис",         kind = StepKind.Event, perTarget = false,
                            hint = "метеоритный дождь, подъём павших — процессы во времени на стороне матча" },
        };

        // ======================== СОСТОЯНИЕ ВКЛАДКИ ========================

        static List<CompositeSkill> skills = new List<CompositeSkill>();
        static CompositeSkill selected;
        static string filter = "";
        static string factionFilter = InterflowAbilityFactions.ALL;   // разбор по фракциям (задача Artsiom 2026-08-17)

        static Dictionary<CompositeSkill, List<InterflowIssue>> issuesBySkill;

        // Кэш контекста точечной проверки: собирается ОДИН раз в RefreshAll, а не на каждое нажатие
        // клавиши (раньше каждая правка поля заново сканировала все префабы и фракции — семь раз).
        static InterflowValidator.SkillValidationContext validationContext;

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

            // Создание — через мастер заготовок (.Wizard.cs): типовое умение в пару кликов,
            // пустое — карточкой «Пустое умение» там же.
            var create = new Button(OpenWizard) { text = "Создать умение" };
            create.style.unityFontStyleAndWeight = FontStyle.Bold;
            create.tooltip = "Открывает мастер создания: заготовки типовых умений (урон ближайшему врагу, " +
                             "лечение, баф команды, зона) с предзаполненными полями — или пустое умение с нуля. " +
                             "Там же умение можно сразу дать юниту двойной записью (abilities[] + автокаст).";
            left.Add(create);

            var search = new TextField("Поиск") { value = filter };
            search.RegisterValueChangedCallback(e => { filter = e.newValue; RebuildList(); });
            left.Add(search);

            // Фильтр по фракции — общий элемент редактора умений (правило 5).
            left.Add(InterflowAbilityFactions.FilterDropdown(factionFilter,
                v => { factionFilter = v; RebuildList(); }));

            left.Add(new Button(() => { RefreshAll(); RebuildList(); RebuildRightPanel(); })
                { text = "Обновить и перепроверить",
                  tooltip = "Перечитать список умений с диска и прогнать проверки заново." });

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

        // ======================== УДАЛЕНИЕ ========================

        static void DeleteSelected()
        {
            if (selected == null) return;

            string path = AssetDatabase.GetAssetPath(selected);
            if (!EditorUtility.DisplayDialog("Удалить", $"Удалить умение?\n{path}", "Удалить", "Отмена")) return;

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();

            InterflowAbilityGroups.InvalidateCache();
            InterflowAbilityUsage.InvalidateCache();

            RefreshAll();
            RebuildList();
            RebuildRightPanel();
        }

        // ======================== ДАННЫЕ ========================

        static void RefreshAll()
        {
            InterflowAbilityUsage.InvalidateCache();
            InterflowAbilityFactions.InvalidateCache();

            skills = AssetDatabase.FindAssets("t:CompositeSkill")
                .Select(g => AssetDatabase.LoadAssetAtPath<CompositeSkill>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null).OrderBy(s => s.name).ToList();

            issuesBySkill = InterflowValidator.ValidateAllSkills();
            validationContext = InterflowValidator.BuildSkillContext(); // кэш для точечных перепроверок

            if (selected == null || !skills.Contains(selected)) selected = skills.FirstOrDefault();

            // Фракция могла исчезнуть вместе с ассетом — иначе фильтр молча показывал бы пусто.
            factionFilter = InterflowAbilityFactions.Correct(factionFilter);
        }

        /// <summary>Перепроверить ТОЛЬКО выбранное умение — по кэшу контекста, без перечитывания проекта.</summary>
        static void RevalidateSelected()
        {
            if (selected == null || issuesBySkill == null) return;
            issuesBySkill[selected] = InterflowValidator.ValidateSkillAlone(selected, validationContext);
        }

        static List<InterflowIssue> IssuesOf(CompositeSkill s) =>
            s != null && issuesBySkill != null && issuesBySkill.TryGetValue(s, out var l)
                ? l : new List<InterflowIssue>();

        // ======================== СПИСОК СЛЕВА ========================

        static void RebuildList()
        {
            if (listContainer == null) return;
            listContainer.Clear();

            var shown = skills.Where(s => InterflowAbilityFactions.Matches(s, factionFilter))
                              .Where(s => string.IsNullOrEmpty(filter)
                                          || s.name.ToLower().Contains(filter.ToLower())
                                          || SkillTitle(s).ToLower().Contains(filter.ToLower())).ToList();

            int err = skills.Count(s => IssuesOf(s).Any(i => i.severity == InterflowIssueSeverity.Error));
            if (countLabel != null)
                countLabel.text = $"Умений: {skills.Count}   ·   с ошибками: {err}";

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
            var btn = new Button(() => { selected = target; wizardMode = false; RebuildList(); RebuildRightPanel(); })
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

        // Живые элементы карточки — обновляются точечно при правке поля, без пересборки панели
        // (пересборка теряла бы фокус ввода).
        static Label summaryLabel;
        static Label badgeLabel;
        static VisualElement ribbonHolder;

        static void RebuildRightPanel()
        {
            if (rightPanel == null) return;
            rightPanel.Unbind();   // снять прежние привязки самого контейнера перед новым Bind
            rightPanel.Clear();
            summaryLabel = null; badgeLabel = null; ribbonHolder = null;

            if (wizardMode) { BuildWizard(rightPanel); return; }

            if (selected == null)
            {
                rightPanel.Add(new Label("Боевых умений в проекте нет. Нажми «Создать умение» слева.")
                    { style = { whiteSpace = WhiteSpace.Normal, marginTop = 6 } });
                return;
            }

            // ОДИН SerializedObject на всю карточку: все секции биндятся к нему,
            // подписка на правки тоже одна (см. TrackEditsOnce ниже).
            var so = new SerializedObject(selected);

            AddHeader();
            AddLevelBar();
            AddSummaryCard();
            rightPanel.Add(InterflowAbilityUsage.Section(selected));   // общий блок, правило 5
            rightPanel.Add(InterflowAbilityFactions.Section(so, selected, RebuildList));
            AddGeneralIssues();
            AddComposerChips(so);
            ribbonHolder = new VisualElement();
            rightPanel.Add(ribbonHolder);
            BuildRibbon();
            AddAimSection(so);
            AddBlockCards(so);
            AddIdentitySection(so);
            AddPresentationSection(so);
            AddRequirementsSection(so);
            AddMiscFoldout(so);

            rightPanel.Bind(so);
            TrackEditsOnce(so);
        }

        static void AddHeader()
        {
            var head = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            head.Add(new Label(SkillTitle(selected))
                { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 15, flexGrow = 1 } });

            badgeLabel = new Label { style = { marginRight = 6 } };
            badgeLabel.tooltip = "Итог проверок этого умения. Сообщения — под составом и в карточках блоков.";
            UpdateBadge();
            head.Add(badgeLabel);

            head.Add(new Button(() => EditorGUIUtility.PingObject(selected)) { text = "Показать" });
            head.Add(new Button(() => { RefreshAll(); RebuildList(); RebuildRightPanel(); }) { text = "Перепроверить" });
            head.Add(new Button(DeleteSelected) { text = "Удалить" });
            rightPanel.Add(head);

            string type;
            try { type = selected.type.ToString(); } catch { type = "?"; }

            rightPanel.Add(new Label($"id {selected.id}   ·   тип: {type} (следует из срабатывания и режима цели, руками не задаётся)   ·   {AssetDatabase.GetAssetPath(selected)}")
                { style = { color = COL_DIM, marginBottom = 4, whiteSpace = WhiteSpace.Normal } });
        }

        static void UpdateBadge()
        {
            if (badgeLabel == null || selected == null) return;
            var issues = IssuesOf(selected);
            int e = issues.Count(i => i.severity == InterflowIssueSeverity.Error);
            int w = issues.Count(i => i.severity == InterflowIssueSeverity.Warning);
            badgeLabel.text = e == 0 && w == 0 ? "✓ чисто" : $"✕ {e}   ! {w}";
            badgeLabel.style.color = e > 0 ? COL_ERR : w > 0 ? COL_WARN : COL_OK;
        }

        /// <summary>Переключатель уровня — НАВЕРХУ: он управляет всеми числами карточки ниже.</summary>
        static void AddLevelBar()
        {
            int max = Mathf.Max(1, selected.maxLevels);
            if (previewLevel >= max) previewLevel = max - 1;
            if (max <= 1) return; // один уровень — переключать нечего

            var bar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 3 } };
            bar.Add(new Label("Показывать числа для уровня:")
                { style = { color = COL_DIM, marginRight = 4 },
                  tooltip = "Все числа карточки (сводка, лента, «→ итог» у массивов) считаются для этого уровня." });

            for (int l = 0; l < max; l++)
            {
                int lvl = l;
                var b = new Button(() => { previewLevel = lvl; RebuildRightPanel(); }) { text = (l + 1).ToString() };
                b.style.width = 26;
                if (l == previewLevel)
                {
                    b.style.unityFontStyleAndWeight = FontStyle.Bold;
                    b.style.borderBottomWidth = 2;
                    b.style.borderBottomColor = new Color(0.35f, 0.6f, 0.95f);
                }
                bar.Add(b);
            }
            bar.Add(new Label($"из maxLevels = {max}") { style = { color = COL_DIM, marginLeft = 4, fontSize = 10 } });
            rightPanel.Add(bar);
        }

        static void AddSummaryCard()
        {
            summaryLabel = new Label(selected.BuildSummary())
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
            summaryLabel.tooltip = "Автосводка по включённым блокам. Значения — для первого уровня.";
            rightPanel.Add(summaryLabel);
        }

        /// <summary>Сообщения валидатора, которые не привязались ни к блоку, ни к значку.</summary>
        static VisualElement generalIssuesHolder;

        static void AddGeneralIssues()
        {
            generalIssuesHolder = new VisualElement();
            FillGeneralIssues();
            rightPanel.Add(generalIssuesHolder);
        }

        static void FillGeneralIssues()
        {
            if (generalIssuesHolder == null) return;
            generalIssuesHolder.Clear();
            foreach (var i in IssuesOf(selected).Where(x => BlockFor(x) == null && !IsBadgeIssue(x)))
                generalIssuesHolder.Add(IssueRow(i));
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

        // ======================== ПРИВЯЗКА СООБЩЕНИЙ К БЛОКАМ ========================

        // Порядок проверки ключей ЗАДАН ЯВНО, от частного к общему: раньше ключ «урон» блока 3
        // перехватывал сообщения про урон ауры бафа, а сообщения значка падали в блок состояний.
        static readonly (string key, string field)[] ISSUE_ROUTES =
        {
            ("значк",       null),              // состояние-значок — своя карточка (holder "statusEffector")
            ("баф",         "buff"),
            ("ауры",        "buff"),
            ("взрыв",       "buff"),
            ("щит",         "shield"),
            ("ослеплени",   "blind"),
            ("отброс",      "knockback"),
            ("оттолкн",     "knockback"),
            ("нокбэк",      "knockback"),
            ("рывок",       "pull"),
            ("притяг",      "pull"),
            ("высасыв",     "drain"),
            ("контрол",     "status"),
            ("оглушен",     "status"),
            ("состояни",    "effectors"),
            ("лечени",      "heal"),
            ("маны",        "mana"),
            ("мана",        "mana"),
            ("облик",       "morph"),
            ("владел",      "ownership"),
            ("вторичн",     "secondary"),
            ("призыв",      "summon"),
            ("зон",         "groundZone"),
            ("перемещен",   "casterMove"),
            ("телепорт",    "casterMove"),
            ("сервис",      "delegateService"),
            ("стоимость в здоровье", "selfCost"),
            ("урон",        "damage"),
            ("урона",       "damage"),
        };

        /// <summary>К какому блоку относится сообщение валидатора; null — общая зона или значок.</summary>
        static BlockDesc BlockFor(InterflowIssue issue)
        {
            string m = issue.message.ToLower();
            foreach (var (key, field) in ISSUE_ROUTES)
                if (m.Contains(key))
                    return field == null ? null : BLOCKS.FirstOrDefault(b => b.field == field);
            return null;
        }

        /// <summary>Сообщения про состояние-значок — показываются в его карточке, не в общей зоне.</summary>
        static bool IsBadgeIssue(InterflowIssue issue) => issue.message.ToLower().Contains("значк");
    }
}
