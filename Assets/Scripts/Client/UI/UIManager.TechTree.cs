using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>
    /// Партиал UIManager: ПАНЕЛЬ ТЕХНОЛОГИЙ (Технологии 2.0) — горизонтальная лента тиров под мобильный ландшафт.
    /// Строится в C# (как BottomTables/CornerTables), InGame.uxml не правится (правило 1).
    /// Раскладка: низкая широкая панель у нижней кромки; тиры — колонки в горизонтальном скролле, по вертикали
    /// тир умещается целиком (вертикального скролла нет). Число тиров берётся из данных (MatchManager.TechTierCount),
    /// «6» нигде не зашито. Масштаб под экраны — штатный SCPanelSettings (Scale With Screen Size, опорное 1920×1080).
    /// Все размеры и подписи — в Inspector (правила 3, 4).
    ///
    /// Состояние панели строится ОТ ДАННЫХ (read-предикаты MatchManager.TechTiers), а не от локального клика:
    /// покупка серверо-авторитетна, панель перерисовывается по факту серверной синхронизации.
    ///
    /// Тексты узлов — из штатных полей Technology (displayName/description) через геттеры MatchManager.
    /// Названия тиров, названия путей и роли данными не заведены — заглушки в Inspector (решение Artsiom 2026-07-25).
    ///
    /// Шаги 2–4: каркас, наполнение тира из данных, состояния узлов классами USS.
    /// Карточка выбора и проводка к RPC — следующие шаги.
    /// </summary>
    public partial class UIManager : MonoBehaviour
    {
        [Header("Панель технологий (лента тиров)")]
        [Tooltip("Заголовок в шапке панели.")]
        [SerializeField] string techTreeTitle = "ТЕХНОЛОГИИ";

        [Tooltip("Ресурс, счётчик которого показывается в шапке панели (обычно «Золото»). Пусто — счётчик не показывать.")]
        [SerializeField] Resource techTreeHeaderResource;

        [Tooltip("Подпись счётчика уровня главного здания в шапке панели.")]
        [SerializeField] string techTreeLevelCaption = "Ур.ГЗ";

        [Tooltip("Подпись кнопки закрытия панели.")]
        [SerializeField] string techTreeCloseCaption = "✕";

        [Tooltip("Высота панели технологий, px (при опорном разрешении 1920×1080).")]
        [SerializeField] int techTreePanelHeight = 620;

        [Tooltip("Отступ панели от боковых кромок экрана, px.")]
        [SerializeField] int techTreePanelSideMargin = 24;

        [Tooltip("Отступ панели от нижней кромки экрана, px.")]
        [SerializeField] int techTreePanelBottomMargin = 24;

        [Tooltip("Высота шапки панели, px.")]
        [SerializeField] int techTreeHeaderHeight = 64;

        [Tooltip("Ширина колонки одного тира, px. При опорном 1920 значение около 940 даёт примерно два тира на экран.")]
        [SerializeField] int techTreeTierWidth = 940;

        [Tooltip("Зазор между колонками тиров, px.")]
        [SerializeField] int techTreeTierGap = 12;

        [Tooltip("Подпись перед номером тира в заголовке колонки.")]
        [SerializeField] string techTreeTierCaption = "ТИР";

        [Tooltip("Названия тиров (заглушки). Индекс = номер тира, считая с нуля. Пусто или короче списка тиров — " +
                 "показывается только номер тира.")]
        [SerializeField] string[] techTreeTierTitles;

        [Tooltip("Подпись статуса тира: все ступени тира пройдены.")]
        [SerializeField] string techTreeStatusCompleted = "Завершён";

        [Tooltip("Подпись статуса тира: тир открыт для покупок.")]
        [SerializeField] string techTreeStatusAvailable = "Доступен";

        [Tooltip("Подпись статуса тира: тир закрыт, пока не завершён предыдущий.")]
        [SerializeField] string techTreeStatusLocked = "Закрыт";

        [Header("Панель технологий — ступень «улучшение уровня»")]
        [Tooltip("Подпись перед номером уровня в строке улучшения уровня тира (например «Ур.»).")]
        [SerializeField] string techTreeLevelRowCaption = "Ур.";

        [Tooltip("Подпись эффекта улучшения уровня (что даёт покупка). Например «Ур.ГЗ +1».")]
        [SerializeField] string techTreeLevelRowEffect = "Ур.ГЗ +1";

        [Tooltip("Отметка на уже купленной ступени.")]
        [SerializeField] string techTreeMarkDone = "✓";

        [Tooltip("Подпись доступной к покупке ступени уровня.")]
        [SerializeField] string techTreeMarkAvailable = "доступно";

        [Header("Панель технологий — пути и узлы")]
        [Tooltip("Названия двух путей тира (заглушки): индекс 0 — путь А, индекс 1 — путь Б.")]
        [SerializeField] string[] techTreePathTitles = { "Путь А", "Путь Б" };

        [Tooltip("Роль юнитов тира (заглушка), одна на тир. Индекс = номер тира, считая с нуля. " +
                 "Пусто или короче списка тиров — строка роли не показывается.")]
        [SerializeField] string[] techTreeTierRoles;

        [Tooltip("Размер иконки узла-юнита в колонке тира, px.")]
        [SerializeField] int techTreeNodeIconSize = 44;

        [Tooltip("Подсказка на доступном узле-юните (открывает карточку выбора).")]
        [SerializeField] string techTreeHintChoose = "Тап — выбрать";

        [Tooltip("Подсказка на купленном узле, у которого специализация ещё не выбрана.")]
        [SerializeField] string techTreeHintChooseSpec = "Выберите специализацию";

        [Tooltip("Подпись на пути, закрытом навсегда выбором противоположного пути.")]
        [SerializeField] string techTreeHintPathClosed = "Путь закрыт навсегда";

        [Header("Панель технологий — карточка выбора")]
        [Tooltip("Подпись над списком специализаций в карточке.")]
        [SerializeField] string techTreeCardSpecCaption = "СПЕЦИАЛИЗАЦИЯ — ВЫБЕРИТЕ ОДНУ (1 ИЗ 2)";

        [Tooltip("Предупреждение о необратимости выбора. {0} — название противоположного пути, {1} — имя его юнита. " +
                 "Пусто — плашку не показывать.")]
        [SerializeField] string techTreeCardWarningFormat =
            "Выбор необратим: {0} — {1} и вторая специализация закроются до конца матча.";

        [Tooltip("Подпись кнопки отказа в карточке.")]
        [SerializeField] string techTreeCardCancelCaption = "Отмена";

        [Tooltip("Подпись кнопки подтверждения в карточке.")]
        [SerializeField] string techTreeCardConfirmCaption = "Взять";

        [Tooltip("Ширина карточки выбора, px (при опорном разрешении 1920×1080).")]
        [SerializeField] int techTreeCardWidth = 900;

        [Tooltip("Размер иконки юнита в шапке карточки, px.")]
        [SerializeField] int techTreeCardIconSize = 52;

        VisualElement techTreeRoot;        // оверлей на весь экран (прозрачен для кликов мимо панели)
        VisualElement techTreePanel;       // сама панель (скрыта до открытия)
        VisualElement techTreeBody;        // контейнер колонок-тиров внутри горизонтального скролла
        Label techTreeResourceValue;       // значение ресурса в шапке
        Label techTreeLevelValue;          // значение уровня ГЗ в шапке

        VisualElement techTreeCardOverlay;     // открытая карточка выбора (null — закрыта)
        VisualElement[] techTreeCardSpecRows;  // строки двух специализаций в карточке
        Button techTreeCardConfirm;            // кнопка «Взять» (неактивна, пока не выбрана специализация)
        int techTreeCardTier = -1;             // координаты узла, по которому открыта карточка
        int techTreeCardOption = -1;
        int techTreeCardSpec = -1;             // выбранная специализация; -1 — не выбрана

        /// <summary>Открыта ли панель технологий (для внешней проводки — кнопки/тогла).</summary>
        public bool IsTechTreePanelOpen => techTreePanel != null && techTreePanel.style.display == DisplayStyle.Flex;

        // Вызывается из InitCornerTables() — там root/uiDocument уже готовы, а ядровой UIManager.Start() не правится (правило 1).
        void InitTechTreePanel()
        {
            if (techTreeRoot != null) return;              // уже построено
            if (uiDocument == null) return;
            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;

            techTreeRoot = new VisualElement { name = "TechTreeRoot" };
            techTreeRoot.style.position = Position.Absolute;
            techTreeRoot.style.left = 0;
            techTreeRoot.style.right = 0;
            techTreeRoot.style.top = 0;
            techTreeRoot.style.bottom = 0;
            techTreeRoot.pickingMode = PickingMode.Ignore;  // клики мимо панели уходят в игру
            root.Add(techTreeRoot);

            techTreePanel = new VisualElement { name = "TechTreePanel" };
            techTreePanel.AddToClassList("techTreePanel");
            techTreePanel.style.position = Position.Absolute;
            techTreePanel.style.left = techTreePanelSideMargin;
            techTreePanel.style.right = techTreePanelSideMargin;
            techTreePanel.style.bottom = techTreePanelBottomMargin;
            techTreePanel.style.height = techTreePanelHeight;
            techTreePanel.style.display = DisplayStyle.None;
            techTreeRoot.Add(techTreePanel);

            techTreePanel.Add(BuildTechTreeHeader());

            // Лента тиров: горизонтальный скролл, по вертикали содержимое умещается целиком.
            ScrollView scroll = new ScrollView(ScrollViewMode.Horizontal) { name = "TechTreeScroll" };
            scroll.style.flexGrow = 1;
            techTreeBody = scroll.contentContainer;
            techTreeBody.style.flexDirection = FlexDirection.Row;
            techTreePanel.Add(scroll);
        }

        // Шапка панели: заголовок, счётчик ресурса, счётчик уровня ГЗ, кнопка закрытия.
        VisualElement BuildTechTreeHeader()
        {
            VisualElement header = new VisualElement { name = "TechTreeHeader" };
            header.AddToClassList("techTreeHeader");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.height = techTreeHeaderHeight;

            Label title = new Label(techTreeTitle);
            title.AddToClassList("techTreeTitle");
            title.style.flexGrow = 1;
            header.Add(title);

            if (techTreeHeaderResource != null)
            {
                VisualElement resourceBadge = new VisualElement();
                resourceBadge.AddToClassList("techTreeBadge");
                resourceBadge.style.flexDirection = FlexDirection.Row;
                resourceBadge.style.alignItems = Align.Center;

                if (techTreeHeaderResource.icon != null)
                {
                    VisualElement icon = new VisualElement();
                    icon.AddToClassList("techTreeBadgeIcon");
                    icon.style.backgroundImage = techTreeHeaderResource.icon;
                    resourceBadge.Add(icon);
                }
                resourceBadge.Add(new Label(techTreeHeaderResource.displayName));
                techTreeResourceValue = new Label(string.Empty);
                techTreeResourceValue.AddToClassList("techTreeBadgeValue");
                resourceBadge.Add(techTreeResourceValue);
                header.Add(resourceBadge);
            }

            VisualElement levelBadge = new VisualElement();
            levelBadge.AddToClassList("techTreeBadge");
            levelBadge.style.flexDirection = FlexDirection.Row;
            levelBadge.style.alignItems = Align.Center;
            levelBadge.Add(new Label(techTreeLevelCaption));
            techTreeLevelValue = new Label(string.Empty);
            techTreeLevelValue.AddToClassList("techTreeBadgeValue");
            levelBadge.Add(techTreeLevelValue);
            header.Add(levelBadge);

            Button close = new Button(() => ShowTechTreePanel(false)) { text = techTreeCloseCaption };
            close.AddToClassList("techTreeClose");
            header.Add(close);

            return header;
        }

        /// <summary>Показать или скрыть панель технологий. При показе панель перерисовывается актуальным состоянием.</summary>
        public void ShowTechTreePanel(bool show)
        {
            if (techTreePanel == null) return;
            if (show) RenderTechTree();
            techTreePanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Переключить видимость панели технологий (для угловой кнопки).</summary>
        public void ToggleTechTreePanel() => ShowTechTreePanel(!IsTechTreePanelOpen);

        // Полная перерисовка ленты по текущим данным команды локального игрока.
        void RenderTechTree()
        {
            if (techTreeBody == null) return;
            techTreeBody.Clear();

            MatchManager mm = MatchManager.instance;
            if (mm == null) return;
            int team = CommandTeamForLocalPlayer();

            RenderTechTreeHeaderValues(mm, team);

            int tiers = mm.TechTierCount(team);            // число тиров — из данных, не зашито
            for (int tier = 0; tier < tiers; tier++)
                techTreeBody.Add(BuildTierColumn(mm, team, tier));
        }

        // Значения счётчиков шапки (ресурс и уровень ГЗ) на момент перерисовки.
        void RenderTechTreeHeaderValues(MatchManager mm, int team)
        {
            if (techTreeLevelValue != null) techTreeLevelValue.text = mm.MainBuildingLevel(team).ToString();
            if (techTreeResourceValue == null) return;

            TeamWaveConfig cfg = mm.Team(team);
            int amount = cfg != null ? TechTreeResourceAmount(cfg.ownerPlayer) : -1;
            techTreeResourceValue.text = amount >= 0 ? amount.ToString() : string.Empty;
        }

        // Текущее количество ресурса шапки у игрока (штатный GameResources; индексация — как в CheckAmount).
        // -1 — ресурс не найден или данные ещё не готовы.
        int TechTreeResourceAmount(int player)
        {
            GameResources gr = GameResources.instance;
            if (gr == null || techTreeHeaderResource == null) return -1;
            if (gr.gameResources == null || gr.playerResources == null || player < 0) return -1;

            for (int i = 0; i < gr.gameResources.Length; i++)
            {
                if (gr.gameResources[i] == null || gr.gameResources[i].type != techTreeHeaderResource) continue;
                int index = i + player * gr.gameResources.Length;
                if (index < 0 || index >= gr.playerResources.Length) return -1;
                return gr.playerResources[index];
            }
            return -1;
        }

        // ======================== КОЛОНКА ТИРА ========================

        // Колонка одного тира: заголовок со статусом, строка улучшения уровня, два блока путей.
        VisualElement BuildTierColumn(MatchManager mm, int team, int tier)
        {
            VisualElement column = new VisualElement();
            column.AddToClassList("techTierColumn");
            column.style.width = techTreeTierWidth;
            column.style.marginRight = techTreeTierGap;

            bool completed = mm.TierCompleted(team, tier);
            bool available = tier == 0 || mm.TierCompleted(team, tier - 1);

            // Решение Artsiom (2026-07-25): ещё не открытые тиры притемняются, чтобы фокус оставался на активном.
            if (!completed && !available) column.AddToClassList("techTierDimmed");

            column.Add(BuildTierHead(mm, team, tier, completed, available));
            column.Add(BuildTierLevelRow(mm, team, tier));

            // Пути тира: А и Б. Индексы option совпадают с серверными (0=A, 1=B).
            for (int option = 0; option < 2; option++)
                column.Add(BuildPathBlock(mm, team, tier, option));

            return column;
        }

        // Заголовок колонки: номер и название тира + бейдж статуса.
        VisualElement BuildTierHead(MatchManager mm, int team, int tier, bool completed, bool available)
        {
            VisualElement head = new VisualElement();
            head.AddToClassList("techTierHead");
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;

            VisualElement titleBox = new VisualElement();
            titleBox.style.flexGrow = 1;

            Label caption = new Label($"{techTreeTierCaption} {tier + 1}");
            caption.AddToClassList("techTierCaption");
            titleBox.Add(caption);

            string tierTitle = TechTreeTierTitle(tier);
            if (!string.IsNullOrEmpty(tierTitle))
            {
                Label name = new Label(tierTitle);
                name.AddToClassList("techTierName");
                titleBox.Add(name);
            }
            head.Add(titleBox);

            Label status = new Label(completed ? techTreeStatusCompleted
                                               : available ? techTreeStatusAvailable
                                                           : techTreeStatusLocked);
            status.AddToClassList("techTierStatus");
            status.AddToClassList(completed ? "techTierStatusCompleted"
                                            : available ? "techTierStatusAvailable"
                                                        : "techTierStatusLocked");
            head.Add(status);

            return head;
        }

        // Ступень 1 — улучшение уровня тира: подпись «Ур. N · <эффект>» и отметка состояния.
        // Покупается прямым тапом (без карточки) — проводка на шаге 6.
        VisualElement BuildTierLevelRow(MatchManager mm, int team, int tier)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("techLevelRow");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            bool unlocked = mm.IsTierLevelUnlocked(team, tier);
            bool unlockable = mm.IsTierLevelUnlockable(team, tier);

            if (unlocked) row.AddToClassList("activeAbility");        // штатный класс «куплено» (зелёная рамка)
            else if (!unlockable) row.AddToClassList("locked");       // штатный класс «недоступно»

            Label caption = new Label($"{techTreeLevelRowCaption} {tier + 1}");
            caption.AddToClassList("techLevelCaption");
            row.Add(caption);

            if (!string.IsNullOrEmpty(techTreeLevelRowEffect))
            {
                Label effect = new Label(techTreeLevelRowEffect);
                effect.AddToClassList("techLevelEffect");
                effect.style.flexGrow = 1;
                row.Add(effect);
            }

            Label mark = new Label(unlocked ? techTreeMarkDone : unlockable ? techTreeMarkAvailable : string.Empty);
            mark.AddToClassList("techLevelMark");
            row.Add(mark);

            // Улучшение уровня покупается прямым тапом (карточки у него нет). Недоступные строки не подписываем —
            // повторный тап по купленному узлу ничего не шлёт (сервер всё равно валидирует).
            if (unlockable)
            {
                int captured = tier;
                row.RegisterCallback<ClickEvent>(_ => BuyTierLevel(captured));
            }
            return row;
        }

        // Блок одного пути тира: заголовок пути + узел-юнит с состоянием.
        VisualElement BuildPathBlock(MatchManager mm, int team, int tier, int option)
        {
            VisualElement block = new VisualElement();
            block.AddToClassList("techPathBlock");
            block.AddToClassList(option == 0 ? "techPathA" : "techPathB");   // акцент пути (цвет полосы)

            bool unlocked = mm.IsBigOptionUnlocked(team, tier, option);
            bool unlockable = mm.IsBigOptionUnlockable(team, tier, option);
            bool closedForever = !unlocked && mm.IsBigOptionUnlocked(team, tier, option == 0 ? 1 : 0);

            if (unlocked) block.AddToClassList("activeAbility");    // штатный класс «куплено» (зелёная рамка)
            else if (closedForever) block.AddToClassList("locked"); // штатный класс «закрыто»

            Label pathTitle = new Label(TechTreePathTitle(option));
            pathTitle.AddToClassList("techPathTitle");
            block.Add(pathTitle);

            if (!mm.BigOptionExists(team, tier, option))
            {
                // Узел не настроен в данных — показываем только заголовок пути, чтобы расхождение было видно.
                return block;
            }

            VisualElement node = new VisualElement();
            node.AddToClassList("techNode");
            node.style.flexDirection = FlexDirection.Row;

            Texture2D icon = mm.BigOptionIcon(team, tier, option);
            VisualElement iconBox = new VisualElement();
            iconBox.AddToClassList("techNodeIcon");
            iconBox.style.width = techTreeNodeIconSize;
            iconBox.style.height = techTreeNodeIconSize;
            if (icon != null) iconBox.style.backgroundImage = icon;

            // Специализация зафиксирована — ставим отметку прямо в квадратике большой технологии
            // (решение Artsiom 2026-07-25), а не только в строке состояния под именем юнита.
            if (unlocked && SelectedSpecIndex(mm, team, tier, option) >= 0)
            {
                Label iconMark = new Label(techTreeMarkDone);
                iconMark.AddToClassList("techNodeIconMark");
                iconBox.Add(iconMark);
            }
            node.Add(iconBox);

            VisualElement textBox = new VisualElement();
            textBox.style.flexGrow = 1;

            Label name = new Label(mm.BigOptionTitle(team, tier, option));
            name.AddToClassList("techNodeName");
            textBox.Add(name);

            string role = TechTreeTierRole(tier);
            if (!string.IsNullOrEmpty(role))
            {
                Label roleLabel = new Label(role);
                roleLabel.AddToClassList("techNodeRole");
                textBox.Add(roleLabel);
            }

            Label hint = new Label(PathHintText(mm, team, tier, option, unlocked, unlockable, closedForever));
            hint.AddToClassList("techNodeHint");
            hint.AddToClassList(closedForever ? "techNodeHintClosed"
                                              : unlocked ? "techNodeHintDone"
                                                         : "techNodeHintChoose");
            textBox.Add(hint);

            node.Add(textBox);
            block.Add(node);

            // Тап по доступному юниту открывает карточку выбора специализации.
            if (unlockable)
            {
                int capturedTier = tier;
                int capturedOption = option;
                node.RegisterCallback<ClickEvent>(_ => OpenTechChoiceCard(capturedTier, capturedOption));
            }
            return block;
        }

        // Строка состояния под именем юнита: выбранная специализация / приглашение выбрать / путь закрыт.
        string PathHintText(MatchManager mm, int team, int tier, int option, bool unlocked, bool unlockable, bool closedForever)
        {
            if (closedForever) return techTreeHintPathClosed;

            if (unlocked)
            {
                // Куплен вариант: показываем выбранную специализацию, иначе зовём её выбрать.
                int spec = SelectedSpecIndex(mm, team, tier, option);
                return spec >= 0
                    ? $"{techTreeMarkDone} {mm.SpecTitle(team, tier, option, spec)}"
                    : techTreeHintChooseSpec;
            }

            return unlockable ? techTreeHintChoose : string.Empty;
        }

        // Индекс зафиксированной специализации варианта (0=A, 1=B) или -1, если выбор ещё не сделан.
        int SelectedSpecIndex(MatchManager mm, int team, int tier, int option)
        {
            for (int spec = 0; spec < 2; spec++)
                if (mm.IsSpecUnlocked(team, tier, option, spec)) return spec;
            return -1;
        }

        // ======================== КАРТОЧКА ВЫБОРА СПЕЦИАЛИЗАЦИИ ========================

        // Карточка — модальный оверлей поверх панели: описание юнита, две специализации (1 из 2),
        // предупреждение о необратимости, «Отмена» / «Взять». Открывается тапом по доступному узлу-юниту.
        // Подтверждение уходит на сервер одним вызовом (вариант + специализация) — проводка на шаге 6.

        /// <summary>Открыть карточку выбора для узла-юнита (tier, option). Ничего не делает, если узел недоступен.</summary>
        public void OpenTechChoiceCard(int tier, int option)
        {
            MatchManager mm = MatchManager.instance;
            if (mm == null || techTreeRoot == null) return;
            int team = CommandTeamForLocalPlayer();
            if (!mm.IsBigOptionUnlockable(team, tier, option)) return;   // сервер тоже валидирует

            CloseTechChoiceCard();
            techTreeCardTier = tier;
            techTreeCardOption = option;
            techTreeCardSpec = -1;                                        // выбор пуст, «Взять» неактивна

            techTreeCardOverlay = BuildTechChoiceCard(mm, team, tier, option);
            techTreeRoot.Add(techTreeCardOverlay);
        }

        /// <summary>Закрыть карточку выбора, если она открыта.</summary>
        public void CloseTechChoiceCard()
        {
            if (techTreeCardOverlay == null) return;
            techTreeCardOverlay.RemoveFromHierarchy();
            techTreeCardOverlay = null;
            techTreeCardSpecRows = null;
            techTreeCardConfirm = null;
            techTreeCardTier = -1;
            techTreeCardOption = -1;
            techTreeCardSpec = -1;
        }

        // Затемняющий оверлей + сама карточка по центру.
        VisualElement BuildTechChoiceCard(MatchManager mm, int team, int tier, int option)
        {
            VisualElement overlay = new VisualElement { name = "TechChoiceOverlay" };
            overlay.AddToClassList("techCardOverlay");
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0;
            overlay.style.right = 0;
            overlay.style.top = 0;
            overlay.style.bottom = 0;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            // Оверлей ловит клики: пока карточка открыта, панель под ней не нажимается.

            VisualElement card = new VisualElement { name = "TechChoiceCard" };
            card.AddToClassList("techCard");
            card.style.width = techTreeCardWidth;
            overlay.Add(card);

            card.Add(BuildTechCardHead(mm, team, tier, option));

            Label name = new Label(mm.BigOptionTitle(team, tier, option));
            name.AddToClassList("techCardName");
            card.Add(name);

            string description = mm.BigOptionDescription(team, tier, option);
            if (!string.IsNullOrEmpty(description))
            {
                Label descriptionLabel = new Label(description);
                descriptionLabel.AddToClassList("techCardDescription");
                card.Add(descriptionLabel);
            }

            Label specCaption = new Label(techTreeCardSpecCaption);
            specCaption.AddToClassList("techCardSpecCaption");
            card.Add(specCaption);

            techTreeCardSpecRows = new VisualElement[2];
            for (int spec = 0; spec < 2; spec++)
            {
                techTreeCardSpecRows[spec] = BuildTechCardSpecRow(mm, team, tier, option, spec);
                card.Add(techTreeCardSpecRows[spec]);
            }

            string warning = TechCardWarningText(mm, team, tier, option);
            if (!string.IsNullOrEmpty(warning))
            {
                Label warningLabel = new Label(warning);
                warningLabel.AddToClassList("techCardWarning");
                card.Add(warningLabel);
            }

            card.Add(BuildTechCardButtons());
            return overlay;
        }

        // Шапка карточки: иконка юнита, бейдж пути, роль, крестик.
        VisualElement BuildTechCardHead(MatchManager mm, int team, int tier, int option)
        {
            VisualElement head = new VisualElement();
            head.AddToClassList("techCardHead");
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;

            Texture2D icon = mm.BigOptionIcon(team, tier, option);
            VisualElement iconBox = new VisualElement();
            iconBox.AddToClassList("techNodeIcon");
            iconBox.style.width = techTreeCardIconSize;
            iconBox.style.height = techTreeCardIconSize;
            if (icon != null) iconBox.style.backgroundImage = icon;
            head.Add(iconBox);

            Label path = new Label(TechTreePathTitle(option));
            path.AddToClassList("techCardPathBadge");
            path.AddToClassList(option == 0 ? "techPathA" : "techPathB");
            head.Add(path);

            string role = TechTreeTierRole(tier);
            if (!string.IsNullOrEmpty(role))
            {
                Label roleLabel = new Label(role);
                roleLabel.AddToClassList("techNodeRole");
                roleLabel.style.flexGrow = 1;
                head.Add(roleLabel);
            }

            Button close = new Button(CloseTechChoiceCard) { text = techTreeCloseCaption };
            close.AddToClassList("techTreeClose");
            head.Add(close);
            return head;
        }

        // Строка одной специализации: маркер выбора, название, описание. Клик выбирает одну из двух.
        VisualElement BuildTechCardSpecRow(MatchManager mm, int team, int tier, int option, int spec)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("techCardSpecRow");
            row.style.flexDirection = FlexDirection.Row;

            VisualElement marker = new VisualElement();
            marker.AddToClassList("techCardSpecMarker");
            row.Add(marker);

            VisualElement textBox = new VisualElement();
            textBox.style.flexGrow = 1;

            Label title = new Label(mm.SpecTitle(team, tier, option, spec));
            title.AddToClassList("techCardSpecName");
            textBox.Add(title);

            string description = mm.SpecDescription(team, tier, option, spec);
            if (!string.IsNullOrEmpty(description))
            {
                Label descriptionLabel = new Label(description);
                descriptionLabel.AddToClassList("techCardSpecDescription");
                textBox.Add(descriptionLabel);
            }
            row.Add(textBox);

            // Узел специализации не настроен в данных — строку показываем, но выбрать её нельзя.
            if (!mm.SpecExists(team, tier, option, spec))
            {
                row.AddToClassList("locked");
                return row;
            }

            int captured = spec;
            row.RegisterCallback<ClickEvent>(_ => SelectTechCardSpec(captured));
            return row;
        }

        // Кнопки карточки: «Отмена» и «Взять» (вторая неактивна, пока специализация не выбрана).
        VisualElement BuildTechCardButtons()
        {
            VisualElement buttons = new VisualElement();
            buttons.AddToClassList("techCardButtons");
            buttons.style.flexDirection = FlexDirection.Row;

            Button cancel = new Button(CloseTechChoiceCard) { text = techTreeCardCancelCaption };
            cancel.AddToClassList("techCardCancel");
            cancel.style.flexGrow = 1;
            buttons.Add(cancel);

            techTreeCardConfirm = new Button(ConfirmTechChoice) { text = techTreeCardConfirmCaption };
            techTreeCardConfirm.AddToClassList("techCardConfirm");
            techTreeCardConfirm.style.flexGrow = 1;
            techTreeCardConfirm.SetEnabled(false);          // выбор ещё не сделан
            buttons.Add(techTreeCardConfirm);
            return buttons;
        }

        // Выбор специализации в карточке (локальный, до подтверждения ничего не покупается).
        void SelectTechCardSpec(int spec)
        {
            if (techTreeCardSpecRows == null) return;
            techTreeCardSpec = spec;
            for (int i = 0; i < techTreeCardSpecRows.Length; i++)
            {
                if (techTreeCardSpecRows[i] == null) continue;
                if (i == spec) techTreeCardSpecRows[i].AddToClassList("activeAbility");   // штатный класс «выбрано»
                else techTreeCardSpecRows[i].RemoveFromClassList("activeAbility");
            }
            if (techTreeCardConfirm != null) techTreeCardConfirm.SetEnabled(true);
        }

        // Текст предупреждения о необратимости: что именно закроется при подтверждении.
        string TechCardWarningText(MatchManager mm, int team, int tier, int option)
        {
            if (string.IsNullOrEmpty(techTreeCardWarningFormat)) return string.Empty;
            int other = option == 0 ? 1 : 0;
            try
            {
                return string.Format(techTreeCardWarningFormat, TechTreePathTitle(other), mm.BigOptionTitle(team, tier, other));
            }
            catch (System.FormatException e)
            {
                // Формат задаётся в Inspector руками: лишняя фигурная скобка не должна ронять открытие карточки.
                Debug.LogWarning($"[UIManager] Некорректный формат предупреждения карточки технологий: {e.Message}. Показан текст как есть.");
                return techTreeCardWarningFormat;
            }
        }

        // ======================== ПРОВОДКА К СЕРВЕРУ (правило 6) ========================

        // Покупка ступени «улучшение уровня» тира: хост зовёт MatchManager напрямую, клиент шлёт запрос.
        // Локально состояние НЕ меняем — панель перерисуется по факту серверной синхронизации (OnTechUnlock).
        void BuyTierLevel(int tier)
        {
            MatchManager mm = MatchManager.instance;
            if (mm == null) return;
            int team = CommandTeamForLocalPlayer();
            if (!mm.IsTierLevelUnlockable(team, tier)) return;   // сервер валидирует повторно

            if (NetworkConnectionHandler.isClient)
            {
                if (NetworkDataSync.instance != null)
                    NetworkDataSync.instance.UnlockTechTierServerRpc(team, tier, (int)TechTierStep.Level, 0, 0);
            }
            else
            {
                mm.TryUnlockTierStep(team, tier, (int)TechTierStep.Level, 0, 0);
            }
        }

        // Подтверждение карточки: вариант и специализация покупаются ОДНИМ серверным вызовом (атомарно),
        // иначе при обрыве между двумя покупками противоположный путь закрылся бы навсегда без специализации.
        void ConfirmTechChoice()
        {
            if (techTreeCardTier < 0 || techTreeCardOption < 0 || techTreeCardSpec < 0) return;

            MatchManager mm = MatchManager.instance;
            if (mm == null) return;
            int team = CommandTeamForLocalPlayer();
            int tier = techTreeCardTier, option = techTreeCardOption, spec = techTreeCardSpec;

            CloseTechChoiceCard();   // идемпотентность: повторно нажать «Взять» по той же карточке уже нельзя

            if (NetworkConnectionHandler.isClient)
            {
                if (NetworkDataSync.instance != null)
                    NetworkDataSync.instance.UnlockTechBigWithSpecServerRpc(team, tier, option, spec);
            }
            else
            {
                mm.TryUnlockBigOptionWithSpec(team, tier, option, spec);
            }
        }

        /// <summary>
        /// Перерисовать панель по актуальным данным. Зовётся из подписок UIManager.CornerTables
        /// (OnTechUnlock/OnTechLock, смена уровня ГЗ) — своих подписок панель не заводит, поэтому
        /// и отписываться ей не нужно.
        /// </summary>
        public void RefreshTechTree()
        {
            CloseStaleTechChoiceCard();
            if (!IsTechTreePanelOpen) return;
            RenderTechTree();
        }

        // Штатное OnTechUnlock приходит без аргументов и на покупки ОБЕИХ команд, поэтому закрываем карточку
        // не по самому факту события, а только если её узел больше недоступен. Иначе покупка противника
        // закрывала бы открытую карточку под пальцем (найдено ревью 2026-07-25).
        void CloseStaleTechChoiceCard()
        {
            if (techTreeCardOverlay == null) return;

            MatchManager mm = MatchManager.instance;
            if (mm != null && mm.IsBigOptionUnlockable(CommandTeamForLocalPlayer(), techTreeCardTier, techTreeCardOption)) return;
            CloseTechChoiceCard();
        }

        // Название тира из списка-заглушки Inspector. Пусто, если названия нет.
        string TechTreeTierTitle(int tier)
        {
            if (techTreeTierTitles == null || tier < 0 || tier >= techTreeTierTitles.Length) return string.Empty;
            return techTreeTierTitles[tier];
        }

        // Роль юнитов тира из списка-заглушки Inspector (одна на тир). Пусто, если роли нет.
        string TechTreeTierRole(int tier)
        {
            if (techTreeTierRoles == null || tier < 0 || tier >= techTreeTierRoles.Length) return string.Empty;
            return techTreeTierRoles[tier];
        }

        // Название пути (0=А, 1=Б) из заглушек Inspector. Пусто, если не задано.
        string TechTreePathTitle(int option)
        {
            if (techTreePathTitles == null || option < 0 || option >= techTreePathTitles.Length) return string.Empty;
            return techTreePathTitles[option];
        }
    }
}
