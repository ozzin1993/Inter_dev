using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>
    /// Партиал UIManager: левая угловая таблица технологий (UI Toolkit, строится в C# как BottomTables — InGame.uxml не правится).
    /// Кнопка-иконка → ВРЕМЕННАЯ панель технологий по ТИРАМ (Технологии 2.0): на тир ряд ячеек [уровень] [вариант А]
    /// [вариант Б] и, после выбора варианта, [спец 1] [спец 2]. Красивый UI — отдельный этап. Команда — локального
    /// игрока (как BottomTables). Состояния ячеек штатными классами .activeAbility (куплено) и .locked (недоступно). Действия
    /// серверо-авторитетны: хост зовёт MatchManager напрямую, клиент шлёт запрос через NetworkDataSync. Данные и
    /// гейтинг — из MatchManager (единый источник). Размеры/иконки/отступы — в Inspector (без хардкода).
    /// </summary>
    public partial class UIManager : MonoBehaviour
    {
        [Header("Угловые таблицы (технологии)")]
        [Tooltip("Иконка кнопки технологий (левый верхний угол).")]
        [SerializeField] Texture2D techButtonIcon;
        [Tooltip("Размер угловой кнопки, px.")]
        [SerializeField] int cornerButtonSize = 64;
        [Tooltip("Горизонтальный отступ кнопок/таблиц от краёв экрана, px.")]
        [SerializeField] int cornerTablesSideOffset = 10;
        [Tooltip("Вертикальный отступ от верхней кромки экрана, px.")]
        [SerializeField] int cornerTablesTopOffset = 10;
        [Tooltip("Зазор между угловой кнопкой и её таблицей, px.")]
        [SerializeField] int cornerPanelGap = 6;

        VisualElement cornerTablesRoot;     // оверлей (прозрачен для кликов, кроме кнопок/таблиц)
        VisualElement techCornerPanel;      // панель технологий (скрыта до нажатия)
        VisualElement techGrid;             // сетка ячеек технологий

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
            techButton.RegisterCallback<ClickEvent>(_ => ToggleCornerPanel(techCornerPanel));
            cornerTablesRoot.Add(techButton);

            techCornerPanel = BuildCornerPanel();
            techCornerPanel.style.left = cornerTablesSideOffset;
            techCornerPanel.style.top = cornerTablesTopOffset + cornerButtonSize + cornerPanelGap;
            techGrid = new VisualElement { name = "TechGrid" };
            techGrid.style.flexDirection = FlexDirection.Column;   // вертикально по тирам; на тир — ряд ячеек
            techGrid.RegisterCallback<ClickEvent>(OnTierCellClick);
            techCornerPanel.Add(techGrid);
            cornerTablesRoot.Add(techCornerPanel);

            SubscribeCornerEvents();

            // [Переделка UI, ADR-001] Панели-сетки строим здесь, а не в ядровом UIManager.Start(),
            // чтобы не добавлять новую правку в ассет (правило 1). root/uiDocument к этому моменту готовы.
            InitSlotPanels();
            InitBranchPanel();        // панель веток Душ (UIManager.BranchPanel.cs) // N4
            InitWaveTimer();          // таймер до следующей волны сверху по центру (UIManager.WaveTimer.cs)
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

        // Переключить видимость панели технологий; при открытии — перерисовать актуальным состоянием.
        void ToggleCornerPanel(VisualElement panel)
        {
            if (panel == null) return;
            bool show = panel.style.display != DisplayStyle.Flex;
            if (show)
            {
                RenderTechPanel();
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

            int tiers = mm.TechTierCount(team);
            // Вертикально: один ряд на тир. Ширина панели — до 5 ячеек ([уровень] [А] [Б] [спец1] [спец2]).
            techGrid.style.width = 5 * bottomTableCellFootprint;   // ширина ячейки — единый источник из BottomTables

            for (int tier = 0; tier < tiers; tier++)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 1 } };

                // Ступень 1 — улучшение уровня.
                row.Add(BuildTierCell(mm, team, tier, (int)TechTierStep.Level, 0, 0));
                // Ступень 2 — большой выбор А/Б.
                row.Add(BuildTierCell(mm, team, tier, (int)TechTierStep.BigOption, 0, 0));
                row.Add(BuildTierCell(mm, team, tier, (int)TechTierStep.BigOption, 1, 0));
                // Ступень 3 — специализации КУПЛЕННОГО варианта (появляются сразу после выбора; §5).
                if (mm.IsBigOptionUnlocked(team, tier, 0))
                {
                    row.Add(BuildTierCell(mm, team, tier, (int)TechTierStep.Specialization, 0, 0));
                    row.Add(BuildTierCell(mm, team, tier, (int)TechTierStep.Specialization, 0, 1));
                }
                else if (mm.IsBigOptionUnlocked(team, tier, 1))
                {
                    row.Add(BuildTierCell(mm, team, tier, (int)TechTierStep.Specialization, 1, 0));
                    row.Add(BuildTierCell(mm, team, tier, (int)TechTierStep.Specialization, 1, 1));
                }

                techGrid.Add(row);
            }
        }

        // Ячейка узла тира: иконка + состояние. userData = координаты узла (тир/ступень/вариант/спец) для клика.
        GroupBox BuildTierCell(MatchManager mm, int team, int tier, int step, int option, int spec)
        {
            GroupBox cell = new GroupBox();
            cell.AddToClassList("AbilityButton");
            cell.userData = new TierCellRef { tier = tier, step = step, option = option, spec = spec };

            GroupBox iconElement = new GroupBox();
            iconElement.AddToClassList("AbilityButtonIcon");
            Texture2D tex = TierCellIcon(mm, team, tier, step, option, spec);
            if (tex != null) iconElement.style.backgroundImage = tex;
            cell.Add(iconElement);

            if (!TierCellExists(mm, team, tier, step, option, spec))
                cell.AddToClassList("locked");                 // пустой узел — недоступно
            else if (TierCellUnlocked(mm, team, tier, step, option, spec))
                cell.AddToClassList("activeAbility");          // куплено
            else if (!TierCellUnlockable(mm, team, tier, step, option, spec))
                cell.AddToClassList("locked");                 // недоступно / заблокировано эксклюзивом
            // иначе — доступно для покупки (обычный вид)

            return cell;
        }

        void OnTierCellClick(ClickEvent evt)
        {
            VisualElement cell = ResolveCornerCell(evt);
            if (cell == null || !(cell.userData is TierCellRef c)) return;

            MatchManager mm = MatchManager.instance;
            if (mm == null) return;
            int team = CommandTeamForLocalPlayer();
            if (!TierCellUnlockable(mm, team, c.tier, c.step, c.option, c.spec)) return; // недоступные — молча игнор (сервер тоже валидирует)

            if (NetworkConnectionHandler.isClient)
            {
                if (NetworkDataSync.instance != null)
                    NetworkDataSync.instance.UnlockTechTierServerRpc(team, c.tier, c.step, c.option, c.spec);
            }
            else
            {
                mm.TryUnlockTierStep(team, c.tier, c.step, c.option, c.spec);
            }
        }

        // Диспетчеры «ступень → метод MatchManager» (иконка/наличие/куплено/доступно) для ячейки тира.
        Texture2D TierCellIcon(MatchManager mm, int team, int tier, int step, int option, int spec) => (TechTierStep)step switch
        {
            TechTierStep.Level     => mm.TierLevelIcon(team, tier),
            TechTierStep.BigOption => mm.BigOptionIcon(team, tier, option),
            _                      => mm.SpecIcon(team, tier, option, spec),
        };

        bool TierCellExists(MatchManager mm, int team, int tier, int step, int option, int spec) => (TechTierStep)step switch
        {
            TechTierStep.Level     => mm.TierLevelExists(team, tier),
            TechTierStep.BigOption => mm.BigOptionExists(team, tier, option),
            _                      => mm.SpecExists(team, tier, option, spec),
        };

        bool TierCellUnlocked(MatchManager mm, int team, int tier, int step, int option, int spec) => (TechTierStep)step switch
        {
            TechTierStep.Level     => mm.IsTierLevelUnlocked(team, tier),
            TechTierStep.BigOption => mm.IsBigOptionUnlocked(team, tier, option),
            _                      => mm.IsSpecUnlocked(team, tier, option, spec),
        };

        bool TierCellUnlockable(MatchManager mm, int team, int tier, int step, int option, int spec) => (TechTierStep)step switch
        {
            TechTierStep.Level     => mm.IsTierLevelUnlockable(team, tier),
            TechTierStep.BigOption => mm.IsBigOptionUnlockable(team, tier, option),
            _                      => mm.IsSpecUnlockable(team, tier, option, spec),
        };

        // Координаты узла тира в userData ячейки (для обработчика клика).
        struct TierCellRef { public int tier, step, option, spec; }

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
            if (mm != null) mm.OnHeroChanged += OnHeroChangedHandler;                  // герой призван/погиб: перерисовка ряда его умений (HeroUI)
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
            // Уровень ГЗ влияет на гейт технологий (открывашки поднимают уровень).
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
