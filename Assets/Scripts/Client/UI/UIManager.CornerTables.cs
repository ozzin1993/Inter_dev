using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>
    /// Партиал UIManager: угловые кнопки-тоглы (UI Toolkit, строятся в C# как BottomTables — InGame.uxml не правится)
    /// и общая проводка подписок для угловых панелей. Кнопка технологий (левый верх) открывает панель Технологий 2.0
    /// — ленту тиров (UIManager.TechTree.cs). ВРЕМЕННАЯ сетка ячеек тиров СНЕСЕНА 2026-07-25 вместе со своим тоглом.
    /// Общие хелперы BuildCornerButton / BuildCornerPanel / ResolveCornerCell ОСТАВЛЕНЫ — их переиспользует
    /// панель веток Душ (UIManager.BranchPanel.cs). Команда — локального игрока (как BottomTables).
    /// Размеры/иконки/отступы — в Inspector (без хардкода).
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
            techButton.RegisterCallback<ClickEvent>(_ => ToggleTechTreePanel());   // панель технологий 2.0 (UIManager.TechTree.cs)
            cornerTablesRoot.Add(techButton);

            // --- Правый край, центр по вертикали: настройка волны ---
            VisualElement waveButton = BuildCornerButton(waveButtonIcon);
            waveButton.style.right = cornerTablesSideOffset;
            // Центр по вертикали: середина экрана минус половина высоты кнопки (кнопка позиционирована абсолютно).
            waveButton.style.top = Length.Percent(50f);
            waveButton.style.marginTop = -cornerButtonSize / 2f;
            waveButton.RegisterCallback<ClickEvent>(_ => ToggleWavePanel());       // окно настройки волны (UIManager.WavePanel.cs)
            cornerTablesRoot.Add(waveButton);

            SubscribeCornerEvents();

            // [Переделка UI, ADR-001] Панели-сетки строим здесь, а не в ядровом UIManager.Start(),
            // чтобы не добавлять новую правку в ассет (правило 1). root/uiDocument к этому моменту готовы.
            InitSlotPanels();
            InitBranchPanel();        // панель веток Душ (UIManager.BranchPanel.cs) // N4
            InitTechTreePanel();      // панель технологий: лента тиров (UIManager.TechTree.cs)
            InitWaveTimer();          // таймер до следующей волны сверху по центру (UIManager.WaveTimer.cs)
            InitWavePanel();          // окно настройки волны справа (UIManager.WavePanel.cs)
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
            MatchManager mm = MatchManager.Instance;
            if (mm != null) mm.OnMainBuildingLevelChanged += OnMainBuildingLevelChangedHandler;
            if (mm != null) mm.OnTeamContentChanged += OnTeamContentChangedHandler;   // апгрейды: видимый набор способностей ГЗ
            if (mm != null) mm.OnHeroChanged += OnHeroChangedHandler;                  // герой призван/погиб: перерисовка ряда его умений (HeroUI)
            if (mm != null) mm.OnSoulsChanged += OnSoulsChangedHandler;                // перерисовка панели веток по Душам // N4
            if (mm != null) mm.OnWaveMarksChanged += OnWaveMarksChangedHandler;        // пометки волны изменились: перерисовать панель волны

            List<int> players = new List<int>();
            TechnologyManager tm = TechnologyManager.Instance;
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
            // Уровень ГЗ показан в шапке панели технологий.
            RefreshTechTree();     // панель-лента (UIManager.TechTree.cs) — своих подписок не заводит
        }

        void OnTechChangedHandler()
        {
            if (cornerTablesRoot == null) return;
            RefreshTechTree();     // дерево изменилось — перерисовать ленту и закрыть устаревшую карточку
            RenderBranchPanel();   // покупка опции ветки идёт через OnTechUnlock // N4
        }

        // Апгрейды контента: изменился видимый набор способностей ГЗ → перерисовать центральную таблицу (только своя команда).
        void OnTeamContentChangedHandler(int team)
        {
            if (team != CommandTeamForLocalPlayer()) return;
            RefreshAbilityTable();      // старая центральная ветка (мигрирует)
            RefreshAbilitySlots();      // новая панель-сетка умений (шаг 4)
            RefreshWavePanel();         // набор доступных юнитов волны изменился (UIManager.WavePanel.cs)
        }

        // Пометки волны изменились (своя команда) → перерисовать открытое окно настройки волны.
        void OnWaveMarksChangedHandler(int team)
        {
            if (team != CommandTeamForLocalPlayer()) return;
            RefreshWavePanel();
        }

        void OnDestroy()
        {
            MatchManager mm = MatchManager.Instance;
            if (mm != null) mm.OnMainBuildingLevelChanged -= OnMainBuildingLevelChangedHandler;
            if (mm != null) mm.OnTeamContentChanged -= OnTeamContentChangedHandler;
            if (mm != null) mm.OnHeroChanged -= OnHeroChangedHandler;
            if (mm != null) mm.OnSoulsChanged -= OnSoulsChangedHandler;   // // N4
            if (mm != null) mm.OnWaveMarksChanged -= OnWaveMarksChangedHandler;

            TechnologyManager tm = TechnologyManager.Instance;
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
