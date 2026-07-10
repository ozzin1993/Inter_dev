using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>
    /// Партиал UIManager: две угловые таблицы (UI Toolkit, строятся в C# как BottomTables — InGame.uxml не правится).
    /// Левый верхний угол — кнопка-иконка → таблица технологий (ветки × уровни, 3×3). Правый верхний угол —
    /// кнопка-иконка → столбец кнопок улучшения главного здания. Команда — локального игрока (как BottomTables).
    /// Состояния ячеек штатными классами .activeAbility (открыто/достигнуто) и .locked (недоступно). Действия
    /// серверо-авторитетны: хост зовёт MatchManager напрямую, клиент шлёт запрос через NetworkDataSync. Данные и
    /// гейтинг — из MatchManager (единый источник). Размеры/иконки/отступы — в Inspector (без хардкода).
    /// </summary>
    public partial class UIManager : MonoBehaviour
    {
        [Header("Угловые таблицы (технологии / улучшение ГЗ)")]
        [Tooltip("Иконка кнопки технологий (левый верхний угол).")]
        [SerializeField] Texture2D techButtonIcon;
        [Tooltip("Иконка кнопки улучшения главного здания (правый верхний угол).")]
        [SerializeField] Texture2D upgradeButtonIcon;
        [Tooltip("Размер угловой кнопки, px.")]
        [SerializeField] int cornerButtonSize = 64;
        [Tooltip("Горизонтальный отступ кнопок/таблиц от краёв экрана, px.")]
        [SerializeField] int cornerTablesSideOffset = 10;
        [Tooltip("Вертикальный отступ от верхней кромки экрана, px.")]
        [SerializeField] int cornerTablesTopOffset = 10;
        [Tooltip("Доп. отступ ПРАВОЙ кнопки сверху: правый верх занят панелью ресурсов — опусти кнопку ниже неё, px.")]
        [SerializeField] int upgradeButtonTopOffset = 130;
        [Tooltip("Зазор между угловой кнопкой и её таблицей, px.")]
        [SerializeField] int cornerPanelGap = 6;

        VisualElement cornerTablesRoot;     // оверлей (прозрачен для кликов, кроме кнопок/таблиц)
        VisualElement techCornerPanel;      // панель технологий (скрыта до нажатия)
        VisualElement techGrid;             // сетка ячеек технологий
        VisualElement upgradeCornerPanel;   // панель улучшения ГЗ (скрыта до нажатия)
        VisualElement upgradeGrid;          // столбец кнопок улучшения

        int[] subscribedTechPlayers;        // слоты игроков, на OnTechUnlock/OnTechLock которых подписались (для отписки)

        // Вызывается из UIManager.Start() после InitBottomTables().
        void InitCornerTables()
        {
            if (cornerTablesRoot != null) return;     // уже построено
            if (uiDocument == null) return;
            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;

            cornerTablesRoot = new VisualElement { name = "CornerTables" };
            cornerTablesRoot.style.position = Position.Absolute;
            cornerTablesRoot.style.left = 0;
            cornerTablesRoot.style.right = 0;
            cornerTablesRoot.style.top = 0;
            cornerTablesRoot.style.bottom = 0;
            cornerTablesRoot.pickingMode = PickingMode.Ignore; // не перехватывает клики мимо своих кнопок/таблиц
            root.Add(cornerTablesRoot);

            // --- Левый верхний угол: технологии ---
            VisualElement techButton = BuildCornerButton(techButtonIcon);
            techButton.style.left = cornerTablesSideOffset;
            techButton.style.top = cornerTablesTopOffset;
            techButton.RegisterCallback<ClickEvent>(_ => ToggleCornerPanel(techCornerPanel, true));
            cornerTablesRoot.Add(techButton);

            techCornerPanel = BuildCornerPanel();
            techCornerPanel.style.left = cornerTablesSideOffset;
            techCornerPanel.style.top = cornerTablesTopOffset + cornerButtonSize + cornerPanelGap;
            techGrid = new VisualElement { name = "TechGrid" };
            techGrid.style.flexDirection = FlexDirection.Row;
            techGrid.style.flexWrap = Wrap.Wrap;
            techGrid.RegisterCallback<ClickEvent>(OnTechCellClick);
            techCornerPanel.Add(techGrid);
            cornerTablesRoot.Add(techCornerPanel);

            // --- Правый верхний угол: улучшение главного здания ---
            VisualElement upgradeButton = BuildCornerButton(upgradeButtonIcon);
            upgradeButton.style.right = cornerTablesSideOffset;
            upgradeButton.style.top = cornerTablesTopOffset + upgradeButtonTopOffset;
            upgradeButton.RegisterCallback<ClickEvent>(_ => ToggleCornerPanel(upgradeCornerPanel, false));
            cornerTablesRoot.Add(upgradeButton);

            upgradeCornerPanel = BuildCornerPanel();
            upgradeCornerPanel.style.right = cornerTablesSideOffset;
            upgradeCornerPanel.style.top = cornerTablesTopOffset + upgradeButtonTopOffset + cornerButtonSize + cornerPanelGap;
            upgradeGrid = new VisualElement { name = "UpgradeGrid" };
            upgradeGrid.style.flexDirection = FlexDirection.Column;
            upgradeGrid.RegisterCallback<ClickEvent>(OnUpgradeCellClick);
            upgradeCornerPanel.Add(upgradeGrid);
            cornerTablesRoot.Add(upgradeCornerPanel);

            SubscribeCornerEvents();

            // [Переделка UI, ADR-001] Панели-сетки строим здесь, а не в ядровом UIManager.Start(),
            // чтобы не добавлять новую правку в ассет (правило 1). root/uiDocument к этому моменту готовы.
            InitSlotPanels();
            InitHeroSummonButton();   // кнопка призыва героя (UIManager.HeroUI.cs)
            InitBranchPanel();        // панель веток Душ (UIManager.BranchPanel.cs) // N4
        }

        // Кнопка-иконка в стиле ячейки способности (.AbilityButton), абсолютно позиционирована.
        VisualElement BuildCornerButton(Texture2D icon)
        {
            GroupBox btn = new GroupBox();
            btn.AddToClassList("AbilityButton");
            btn.style.position = Position.Absolute;
            btn.style.width = cornerButtonSize;
            btn.style.height = cornerButtonSize;

            GroupBox iconElement = new GroupBox();
            iconElement.AddToClassList("AbilityButtonIcon");
            if (icon != null) iconElement.style.backgroundImage = icon;
            btn.Add(iconElement);
            return btn;
        }

        // Пустая абсолютно позиционированная панель, скрытая до нажатия.
        VisualElement BuildCornerPanel()
        {
            VisualElement panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.display = DisplayStyle.None;
            return panel;
        }

        // Переключить видимость панели; при открытии — перерисовать актуальным состоянием. Тоглы независимы.
        void ToggleCornerPanel(VisualElement panel, bool isTech)
        {
            if (panel == null) return;
            bool show = panel.style.display != DisplayStyle.Flex;
            if (show)
            {
                if (isTech) RenderTechPanel();
                else RenderUpgradePanel();
                panel.style.display = DisplayStyle.Flex;
            }
            else
            {
                panel.style.display = DisplayStyle.None;
            }
        }

        // --- Технологии ---

        void RenderTechPanel()
        {
            if (techGrid == null) return;
            techGrid.Clear();

            MatchManager mm = MatchManager.instance;
            if (mm == null) return;
            int team = CommandTeamForLocalPlayer();

            int branches = mm.TechBranchCount(team);
            int levels = mm.TechLevelCount(team);
            techGrid.style.width = Mathf.Max(1, branches) * bottomTableCellFootprint;  // ширина ячейки — единый источник из BottomTables

            // Ряды = уровни (верхний ряд = уровень 1), столбцы = ветки.
            for (int level = 0; level < levels; level++)
                for (int branch = 0; branch < branches; branch++)
                    techGrid.Add(BuildTechCell(mm, team, branch, level));
        }

        GroupBox BuildTechCell(MatchManager mm, int team, int branch, int level)
        {
            GroupBox cell = new GroupBox();
            cell.AddToClassList("AbilityButton");
            cell.userData = new Vector2Int(branch, level);

            GroupBox iconElement = new GroupBox();
            iconElement.AddToClassList("AbilityButtonIcon");
            Texture2D tex = mm.TechIcon(team, branch, level);
            if (tex != null) iconElement.style.backgroundImage = tex;
            cell.Add(iconElement);

            if (!mm.TechExists(team, branch, level))
                cell.AddToClassList("locked");              // пустой узел — недоступно
            else if (mm.IsTechUnlocked(team, branch, level))
                cell.AddToClassList("activeAbility");       // открыто
            else if (!mm.IsTechUnlockable(team, branch, level))
                cell.AddToClassList("locked");              // недоступно (гейт)
            // иначе — доступно для разблокировки (обычный вид)

            return cell;
        }

        void OnTechCellClick(ClickEvent evt)
        {
            VisualElement cell = ResolveCornerCell(evt);
            if (cell == null || !(cell.userData is Vector2Int coord)) return;

            MatchManager mm = MatchManager.instance;
            if (mm == null) return;
            int team = CommandTeamForLocalPlayer();
            if (!mm.IsTechUnlockable(team, coord.x, coord.y)) return; // недоступные — молча игнор (сервер тоже валидирует)

            if (NetworkConnectionHandler.isClient)
            {
                if (NetworkDataSync.instance != null) NetworkDataSync.instance.UnlockTechServerRpc(team, coord.x, coord.y);
            }
            else
            {
                mm.TryUnlockTech(team, coord.x, coord.y);
            }
        }

        // --- Улучшение главного здания ---

        void RenderUpgradePanel()
        {
            if (upgradeGrid == null) return;
            upgradeGrid.Clear();

            MatchManager mm = MatchManager.instance;
            if (mm == null) return;
            int team = CommandTeamForLocalPlayer();

            int max = mm.MainBuildingMaxLevel(team);
            int cur = mm.MainBuildingLevel(team);
            upgradeGrid.style.width = bottomTableCellFootprint;

            for (int level = 0; level < max; level++)
                upgradeGrid.Add(BuildUpgradeCell(level, cur));
        }

        GroupBox BuildUpgradeCell(int level, int currentLevel)
        {
            GroupBox cell = new GroupBox();
            cell.AddToClassList("AbilityButton");
            cell.userData = level;
            cell.style.justifyContent = Justify.Center;
            cell.style.alignItems = Align.Center;

            Label label = new Label("Ур. " + (level + 1));
            label.style.fontSize = bottomCellFontSize;     // переиспользуем стиль из BottomTables (единый источник)
            label.style.color = bottomCellTextColor;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            cell.Add(label);

            if (currentLevel > level) cell.AddToClassList("activeAbility");  // уровень достигнут
            else if (currentLevel < level) cell.AddToClassList("locked");     // ещё недоступен
            // currentLevel == level → следующий доступный (обычный вид)

            return cell;
        }

        void OnUpgradeCellClick(ClickEvent evt)
        {
            VisualElement cell = ResolveCornerCell(evt);
            if (cell == null || !(cell.userData is int level)) return;

            MatchManager mm = MatchManager.instance;
            if (mm == null) return;
            int team = CommandTeamForLocalPlayer();
            if (level != mm.MainBuildingLevel(team)) return; // кликабелен только следующий уровень

            if (NetworkConnectionHandler.isClient)
            {
                if (NetworkDataSync.instance != null) NetworkDataSync.instance.UpgradeMainBuildingServerRpc(team, level);
            }
            else
            {
                mm.TryUpgradeMainBuilding(team, level);
            }
        }

        // --- Общее ---

        // Поднимается от цели клика до самой ячейки (.AbilityButton).
        VisualElement ResolveCornerCell(ClickEvent evt)
        {
            VisualElement target = evt.target as VisualElement;
            while (target != null && !target.ClassListContains("AbilityButton")) target = target.parent;
            return target;
        }

        // Подписка: открытые таблицы должны обновляться при изменении уровня ГЗ (наше событие) и разблокировке
        // технологий (штатное TechnologyManager.OnTechUnlock/OnTechLock, срабатывает и у хоста, и у клиента).
        void SubscribeCornerEvents()
        {
            MatchManager mm = MatchManager.instance;
            if (mm != null) mm.OnMainBuildingLevelChanged += OnMainBuildingLevelChangedHandler;
            if (mm != null) mm.OnTeamContentChanged += OnTeamContentChangedHandler;   // апгрейды: видимый набор способностей ГЗ
            if (mm != null) mm.OnHeroChanged += OnHeroChangedHandler;                  // кнопка призыва: дизейбл при живом герое
            if (mm != null) mm.OnSoulsChanged += OnSoulsChangedHandler;                // перерисовка панели веток по Душам // N4

            List<int> players = new List<int>();
            TechnologyManager tm = TechnologyManager.instance;
            if (mm != null && tm != null && tm.OnTechUnlock != null)
            {
                for (int t = 0; t < 2; t++)
                {
                    TeamWaveConfig cfg = mm.Team(t);
                    if (cfg == null) continue;
                    int p = cfg.ownerPlayer;
                    if (p >= 0 && p < tm.OnTechUnlock.Length && !players.Contains(p))
                    {
                        tm.OnTechUnlock[p] += OnTechChangedHandler;
                        if (tm.OnTechLock != null && p < tm.OnTechLock.Length) tm.OnTechLock[p] += OnTechChangedHandler;
                        players.Add(p);
                    }
                }
            }
            subscribedTechPlayers = players.ToArray();
        }

        void OnMainBuildingLevelChangedHandler(int team)
        {
            if (cornerTablesRoot == null) return;
            if (team != CommandTeamForLocalPlayer()) return;
            // Уровень ГЗ влияет и на правую таблицу, и на гейт технологий.
            RenderUpgradePanel();
            RenderTechPanel();
        }

        void OnTechChangedHandler()
        {
            if (cornerTablesRoot == null) return;
            RenderTechPanel();
            RenderBranchPanel();   // покупка опции ветки идёт через OnTechUnlock // N4
        }

        // Апгрейды контента: изменился видимый набор способностей ГЗ → перерисовать центральную таблицу (только своя команда).
        void OnTeamContentChangedHandler(int team)
        {
            if (team != CommandTeamForLocalPlayer()) return;
            RefreshAbilityTable();      // старая центральная ветка (мигрирует)
            RefreshAbilitySlots();      // новая панель-сетка умений (шаг 4)
        }

        void OnDestroy()
        {
            MatchManager mm = MatchManager.instance;
            if (mm != null) mm.OnMainBuildingLevelChanged -= OnMainBuildingLevelChangedHandler;
            if (mm != null) mm.OnTeamContentChanged -= OnTeamContentChangedHandler;
            if (mm != null) mm.OnHeroChanged -= OnHeroChangedHandler;
            if (mm != null) mm.OnSoulsChanged -= OnSoulsChangedHandler;   // // N4

            TechnologyManager tm = TechnologyManager.instance;
            if (tm != null && subscribedTechPlayers != null)
            {
                for (int i = 0; i < subscribedTechPlayers.Length; i++)
                {
                    int p = subscribedTechPlayers[i];
                    if (tm.OnTechUnlock != null && p >= 0 && p < tm.OnTechUnlock.Length) tm.OnTechUnlock[p] -= OnTechChangedHandler;
                    if (tm.OnTechLock != null && p >= 0 && p < tm.OnTechLock.Length) tm.OnTechLock[p] -= OnTechChangedHandler;
                }
            }
        }
    }
}
