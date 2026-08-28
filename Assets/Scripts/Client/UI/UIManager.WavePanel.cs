using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>
    /// Партиал UIManager: ОКНО НАСТРОЙКИ ВОЛНЫ — вертикальная панель у правой кромки экрана,
    /// открывается кнопкой в правом нижнем углу (кнопка строится в UIManager.CornerTables.cs).
    /// Строится в C# (InGame.uxml не правится, правило 1), стили — блок .wavePanel* в SC_Style.uss.
    /// Заменяет временную панель WaveBuilderUI (удалена 2026-08-23).
    ///
    /// Содержимое (решения Artsiom 2026-08-23):
    ///   • шапка: заголовок, время до ближайшей волны, кнопка закрытия;
    ///   • блок «Ближайшая волна» — помеченные типы с числом копий (базовые НЕ показываются);
    ///   • список доступных юнитов — ряд «иконка + Авто + Разовый + Снять», без названия и без цены.
    /// Пометка показана штатной зелёной рамкой .activeAbility на выбранной кнопке ряда.
    /// Кнопки не гасятся при нехватке ресурсов — отказ приходит с сервера, пометка просто не меняется.
    ///
    /// Состояние строится ОТ ДАННЫХ (MatchManager.AvailableWaveUnits / MarkState / WaveCountOf):
    /// пометки серверо-авторитетны (правило 6), панель перерисовывается по факту синхронизации
    /// (событие MatchManager.OnWaveMarksChanged, подписка — в UIManager.CornerTables.cs).
    /// Все размеры, подписи и иконки — в Inspector (правила 3, 4).
    /// </summary>
    public partial class UIManager : MonoBehaviour
    {
        [Header("Панель волны (окно настройки)")]
        [Tooltip("Иконка кнопки открытия панели волны (правый нижний угол экрана).")]
        [SerializeField] Texture2D waveButtonIcon;

        [Tooltip("Заголовок в шапке панели волны.")]
        [SerializeField] string wavePanelTitle = "ВОЛНА";

        [Tooltip("Подпись кнопки закрытия панели волны.")]
        [SerializeField] string wavePanelCloseCaption = "✕";

        [Tooltip("Ширина панели волны, px (при опорном разрешении 1920×1080).")]
        [SerializeField] int wavePanelWidth = 690;

        [Tooltip("Высота шапки панели волны, px.")]
        [SerializeField] int wavePanelHeaderHeight = 64;

        [Tooltip("Формат строки времени до ближайшей волны в шапке. {0} — секунды. " +
                 "Пусто — время в шапке не показывать.")]
        [SerializeField] string wavePanelTimerFormat = "через {0} сек";

        [Tooltip("Подпись раздела состава ближайшей волны.")]
        [SerializeField] string wavePanelNextCaption = "БЛИЖАЙШАЯ ВОЛНА";

        [Tooltip("Подпись раздела списка доступных юнитов.")]
        [SerializeField] string wavePanelUnitsCaption = "ДОСТУПНЫЕ ЮНИТЫ";

        [Tooltip("Формат числа копий юнита в составе ближайшей волны. {0} — количество.")]
        [SerializeField] string wavePanelCountFormat = "×{0}";

        [Tooltip("Размер иконки юнита в составе ближайшей волны, px.")]
        [SerializeField] int wavePanelNextIconSize = 40;

        [Tooltip("Размер ячейки ряда доступного юнита (иконка и кнопки пометок), px.")]
        [SerializeField] int wavePanelCellSize = 68;

        [Header("Панель волны — иконки кнопок пометок")]
        [Tooltip("Иконка кнопки «Автопризыв» (тип выходит каждую волну).")]
        [SerializeField] Texture2D waveAutoIcon;

        [Tooltip("Иконка кнопки «Разовый призыв» (одна ближайшая волна, золото резервируется).")]
        [SerializeField] Texture2D waveOneShotIcon;

        [Tooltip("Иконка кнопки «Снять пометку».")]
        [SerializeField] Texture2D waveClearIcon;

        VisualElement waveRoot;          // оверлей на весь экран (клики мимо панели уходят в игру)
        VisualElement wavePanel;         // сама панель (скрыта до открытия)
        VisualElement waveNextBox;       // контейнер состава ближайшей волны
        VisualElement waveUnitList;      // контейнер рядов доступных юнитов
        Label wavePanelTimerLabel;       // время до волны в шапке

        /// <summary>Открыта ли панель волны (для кнопки-тогла).</summary>
        public bool IsWavePanelOpen => wavePanel != null && wavePanel.style.display == DisplayStyle.Flex;

        // Вызывается из InitCornerTables() — root/uiDocument там уже готовы, ядровой Start() не правится (правило 1).
        void InitWavePanel()
        {
            if (waveRoot != null) return;                 // уже построено
            if (uiDocument == null) return;
            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;

            waveRoot = new VisualElement { name = "WavePanelRoot" };
            waveRoot.style.position = Position.Absolute;
            waveRoot.style.left = 0;
            waveRoot.style.right = 0;
            waveRoot.style.top = 0;
            waveRoot.style.bottom = 0;
            waveRoot.pickingMode = PickingMode.Ignore;    // клики мимо панели уходят в игру
            root.Add(waveRoot);

            wavePanel = new VisualElement { name = "WavePanel" };
            wavePanel.AddToClassList("wavePanel");
            wavePanel.style.position = Position.Absolute;
            wavePanel.style.right = 0;
            wavePanel.style.top = 0;
            wavePanel.style.bottom = 0;
            wavePanel.style.width = wavePanelWidth;
            wavePanel.style.display = DisplayStyle.None;  // на старте матча закрыта (решение Artsiom)
            waveRoot.Add(wavePanel);

            wavePanel.Add(BuildWavePanelHeader());

            wavePanel.Add(BuildWaveSectionCaption(wavePanelNextCaption));
            waveNextBox = new VisualElement { name = "WaveNextBox" };
            waveNextBox.AddToClassList("waveNextBox");
            waveNextBox.style.flexDirection = FlexDirection.Row;
            waveNextBox.style.flexWrap = Wrap.Wrap;
            wavePanel.Add(waveNextBox);

            wavePanel.Add(BuildWaveSectionCaption(wavePanelUnitsCaption));
            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical) { name = "WaveUnitScroll" };
            scroll.style.flexGrow = 1;
            waveUnitList = scroll.contentContainer;
            waveUnitList.style.flexDirection = FlexDirection.Column;
            wavePanel.Add(scroll);
        }

        // Шапка панели: заголовок, время до волны, кнопка закрытия.
        VisualElement BuildWavePanelHeader()
        {
            VisualElement header = new VisualElement { name = "WavePanelHeader" };
            header.AddToClassList("wavePanelHeader");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.height = wavePanelHeaderHeight;

            Label title = new Label(wavePanelTitle);
            title.AddToClassList("wavePanelTitle");
            title.style.flexGrow = 1;
            header.Add(title);

            if (!string.IsNullOrEmpty(wavePanelTimerFormat))
            {
                VisualElement badge = new VisualElement();
                badge.AddToClassList("wavePanelBadge");
                badge.style.flexDirection = FlexDirection.Row;
                badge.style.alignItems = Align.Center;
                wavePanelTimerLabel = new Label(string.Empty);
                wavePanelTimerLabel.AddToClassList("wavePanelBadgeValue");
                badge.Add(wavePanelTimerLabel);
                header.Add(badge);
            }

            Button close = new Button(() => ShowWavePanel(false)) { text = wavePanelCloseCaption };
            close.AddToClassList("wavePanelClose");
            header.Add(close);

            return header;
        }

        // Подпись раздела внутри панели.
        VisualElement BuildWaveSectionCaption(string text)
        {
            Label caption = new Label(text);
            caption.AddToClassList("waveSectionCaption");
            return caption;
        }

        /// <summary>Показать или скрыть панель волны. При показе панель перерисовывается актуальным состоянием.</summary>
        public void ShowWavePanel(bool show)
        {
            if (wavePanel == null) return;
            if (show) RenderWavePanel();
            wavePanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Переключить видимость панели волны (для угловой кнопки).</summary>
        public void ToggleWavePanel() => ShowWavePanel(!IsWavePanelOpen);

        /// <summary>Перерисовать панель волны, если она открыта (для подписок на изменение пометок и контента).</summary>
        public void RefreshWavePanel()
        {
            if (IsWavePanelOpen) RenderWavePanel();
        }

        /// <summary>
        /// Обновить время до волны в шапке панели. Зовётся из ShowWaveTimer (UIManager.WaveTimer.cs),
        /// чтобы шапка шла тем же отсчётом, что и строка сверху. seconds &lt; 0 — очистить.
        /// </summary>
        void UpdateWavePanelTimer(int seconds)
        {
            if (wavePanelTimerLabel == null) return;
            wavePanelTimerLabel.text = seconds < 0 ? string.Empty : string.Format(wavePanelTimerFormat, seconds);
        }

        // Полная перерисовка по текущим данным команды локального игрока.
        void RenderWavePanel()
        {
            if (waveUnitList == null || waveNextBox == null) return;

            waveNextBox.Clear();
            waveUnitList.Clear();

            MatchManager mm = MatchManager.Instance;
            if (mm == null) return;
            int team = CommandTeamForLocalPlayer();

            System.Collections.Generic.IReadOnlyList<Unit> units = mm.AvailableWaveUnits(team);
            if (units == null) return;

            for (int i = 0; i < units.Count; i++)
            {
                Unit prefab = units[i];
                if (prefab == null) continue;

                int mark = mm.MarkState(team, prefab);
                if (mark != 0) waveNextBox.Add(BuildWaveNextItem(mm, team, prefab));
                waveUnitList.Add(BuildWaveUnitRow(mm, team, prefab, mark));
            }
        }

        // Элемент состава ближайшей волны: иконка юнита и число копий.
        VisualElement BuildWaveNextItem(MatchManager mm, int team, Unit prefab)
        {
            VisualElement item = new VisualElement();
            item.AddToClassList("waveNextItem");
            item.style.flexDirection = FlexDirection.Row;
            item.style.alignItems = Align.Center;

            VisualElement icon = new VisualElement();
            icon.AddToClassList("waveNextItemIcon");
            icon.style.width = wavePanelNextIconSize;
            icon.style.height = wavePanelNextIconSize;
            Texture2D texture = WaveUnitIcon(mm, team, prefab);
            if (texture != null) icon.style.backgroundImage = texture;
            item.Add(icon);

            Label count = new Label(string.Format(wavePanelCountFormat, mm.WaveCountOf(team, prefab)));
            count.AddToClassList("waveNextItemCount");
            item.Add(count);

            return item;
        }

        // Ряд доступного юнита: иконка и три кнопки пометки. Активная пометка — штатная зелёная рамка.
        VisualElement BuildWaveUnitRow(MatchManager mm, int team, Unit prefab, int mark)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("waveUnitRow");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            row.Add(BuildWaveCell(WaveUnitIcon(mm, team, prefab), null, false));
            row.Add(BuildWaveCell(waveAutoIcon,    () => SendWaveMark(team, prefab, 0), mark == 1));
            row.Add(BuildWaveCell(waveOneShotIcon, () => SendWaveMark(team, prefab, 1), mark == 2));
            row.Add(BuildWaveCell(waveClearIcon,   () => SendWaveMark(team, prefab, 2), false));

            return row;
        }

        // Ячейка ряда в стиле штатной кнопки способности. onClick == null — ячейка некликабельная (иконка юнита).
        VisualElement BuildWaveCell(Texture2D icon, Action onClick, bool active)
        {
            GroupBox cell = new GroupBox();
            cell.AddToClassList("AbilityButton");
            cell.style.width = wavePanelCellSize;
            cell.style.height = wavePanelCellSize;
            if (active) cell.AddToClassList("activeAbility");    // штатная зелёная рамка «выбрано»

            GroupBox iconElement = new GroupBox();
            iconElement.AddToClassList("AbilityButtonIcon");
            if (icon != null) iconElement.style.backgroundImage = icon;
            cell.Add(iconElement);

            if (onClick != null) cell.RegisterCallback<ClickEvent>(_ => onClick());
            else cell.pickingMode = PickingMode.Ignore;

            return cell;
        }

        // Иконка спроецированного юнита (unitSwaps): игрок видит то, что реально заспавнится.
        static Texture2D WaveUnitIcon(MatchManager mm, int team, Unit prefab)
        {
            Unit displayPrefab = mm != null ? mm.ResolveSwap(team, prefab) : prefab;
            return displayPrefab != null ? displayPrefab.icon : (prefab != null ? prefab.icon : null);
        }

        // op: 0 — авто, 1 — разовый, 2 — снять. Серверо-авторитетно (правило 6):
        // хост правит напрямую, клиент шлёт ServerRpc; панель перерисуется по OnWaveMarksChanged.
        void SendWaveMark(int team, Unit prefab, int op)
        {
            if (prefab == null) return;

            if (!NetworkConnectionHandler.isClient)
            {
                MatchManager mm = MatchManager.Instance;
                if (mm == null) return;
                if (op == 0) mm.TrySetAuto(team, prefab.unitTypeID);
                else if (op == 1) mm.TrySetOneShot(team, prefab.unitTypeID);
                else mm.TryClearMark(team, prefab.unitTypeID);
            }
            else if (NetworkDataSync.Instance != null)
            {
                NetworkDataSync.Instance.WaveMarkServerRpc(team, prefab.unitTypeID, op);
            }
        }
    }
}
