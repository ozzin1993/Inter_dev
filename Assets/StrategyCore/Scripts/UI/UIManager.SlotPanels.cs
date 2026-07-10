using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>Точка привязки панели-сетки на экране.</summary>
    public enum ScreenAnchor
    {
        BottomLeft, BottomCenter, BottomRight,
        TopLeft, TopCenter, TopRight,
        Center,
    }

    /// <summary>
    /// Конфиг одной панели-сетки: фиксированная сетка rows×cols в точке экрана.
    /// Слоты назначаются ПОЗИЦИОННО: индекс = ряд*cols + столбец. Пустой слот (нет иконки/действия/
    /// клика/текста) рисуется штатной пустой ячейкой на своей позиции — соседи не сдвигаются.
    /// </summary>
    [Serializable]
    public class SlotPanelConfig
    {
        [Tooltip("Имя панели — для читаемости и поиска через API (напр. \"Атака/Защита\", \"Умения ГЗ\").")]
        public string name;

        [Tooltip("Точка привязки панели на экране (угол/центр).")]
        public ScreenAnchor anchor = ScreenAnchor.BottomLeft;

        [Tooltip("Число рядов сетки (фиксировано).")]
        public int rows = 2;

        [Tooltip("Число столбцов сетки (фиксировано).")]
        public int cols = 2;

        [Tooltip("Отступ панели от края экрана: X — по горизонтали, Y — по вертикали, px.")]
        public Vector2 offset = new Vector2(10f, 10f);

        [Tooltip("Кнопки-слоты ПОЗИЦИОННО: индекс = ряд*cols + столбец. " +
                 "Пустой элемент или список короче сетки → пустая ячейка на своей позиции.")]
        public List<BottomTableButton> slots = new List<BottomTableButton>();
    }

    /// <summary>
    /// Партиал UIManager: панели-сетки с фиксированными слотами в произвольной точке экрана (ADR-001).
    /// Строятся в C# (InGame.uxml не правится, правило 1). Ячейки — штатные .AbilityButton, пустые —
    /// EmptyElementCreate, клик — общий OnBottomTableClick (переиспользование логики Attack/Defence/
    /// onClick/способностей — правило 5). Всё в Inspector (правило 3).
    /// Пока СОСУЩЕСТВУЕТ со старыми BottomTables: блоки мигрируют в шагах 2–5 плана переделки UI,
    /// после чего старый механизм упраздняется. Подключение без правки ассета — InitSlotPanels()
    /// вызывается из нашего InitCornerTables(), а НЕ из ядрового UIManager.Start().
    /// </summary>
    public partial class UIManager : MonoBehaviour
    {
        [Header("Панели-сетки (сетка-в-точке)")]
        [Tooltip("Панели с фиксированными слотами: атака/защита (2×2), умения ГЗ/Героя (по 3), волна.")]
        [SerializeField] SlotPanelConfig[] slotPanels;

        VisualElement[] slotPanelRoots;                 // панель-колонка каждой сетки (по индексу slotPanels)
        List<BottomTableButton>[] slotPanelElements;    // текущее содержимое (для рендера/обновления через API)

        // Вызывается из InitCornerTables() (наш партиал), чтобы не добавлять правку в ядровой Start() (правило 1).
        void InitSlotPanels()
        {
            if (slotPanelRoots != null) return;          // уже построено
            if (uiDocument == null) return;
            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;

            int count = slotPanels != null ? slotPanels.Length : 0;
            slotPanelRoots = new VisualElement[count];
            slotPanelElements = new List<BottomTableButton>[count];

            for (int i = 0; i < count; i++)
            {
                SlotPanelConfig cfg = slotPanels[i];
                if (cfg == null || cfg.rows <= 0 || cfg.cols <= 0) continue;

                // Оверлей на весь экран (клики проходят мимо пустого места) — задаёт позицию панели через flex.
                VisualElement overlay = new VisualElement { name = "SlotPanelOverlay" + i };
                overlay.style.position = Position.Absolute;
                overlay.style.left = 0; overlay.style.right = 0;
                overlay.style.top = 0; overlay.style.bottom = 0;
                overlay.pickingMode = PickingMode.Ignore;   // сам оверлей клики не перехватывает (как cornerTablesRoot)
                ApplyAnchor(overlay, cfg.anchor);

                // Панель — колонка рядов; отступ от края через margin.
                VisualElement panel = new VisualElement
                {
                    name = string.IsNullOrEmpty(cfg.name) ? "SlotPanel" + i : cfg.name
                };
                panel.style.flexDirection = FlexDirection.Column;
                panel.style.marginLeft = cfg.offset.x; panel.style.marginRight = cfg.offset.x;
                panel.style.marginTop = cfg.offset.y; panel.style.marginBottom = cfg.offset.y;

                overlay.Add(panel);
                root.Add(overlay);
                slotPanelRoots[i] = panel;

                // Стартовое содержимое из Inspector (позиционно).
                slotPanelElements[i] = new List<BottomTableButton>();
                if (cfg.slots != null) slotPanelElements[i].AddRange(cfg.slots);

                RenderSlotPanel(i);
            }
        }

        // Позиция панели внутри полноэкранного оверлея: justifyContent — вертикаль, alignItems — горизонталь.
        void ApplyAnchor(VisualElement overlay, ScreenAnchor anchor)
        {
            Justify v; Align h;
            switch (anchor)
            {
                case ScreenAnchor.BottomLeft:   v = Justify.FlexEnd;   h = Align.FlexStart; break;
                case ScreenAnchor.BottomCenter: v = Justify.FlexEnd;   h = Align.Center;    break;
                case ScreenAnchor.BottomRight:  v = Justify.FlexEnd;   h = Align.FlexEnd;   break;
                case ScreenAnchor.TopLeft:      v = Justify.FlexStart; h = Align.FlexStart; break;
                case ScreenAnchor.TopCenter:    v = Justify.FlexStart; h = Align.Center;    break;
                case ScreenAnchor.TopRight:     v = Justify.FlexStart; h = Align.FlexEnd;   break;
                default:                        v = Justify.Center;    h = Align.Center;    break; // Center
            }
            overlay.style.justifyContent = v;
            overlay.style.alignItems = h;
        }

        // Перестраивает сетку панели из её текущего списка слотов (позиционно). Ряды — явные контейнеры (Р1б).
        void RenderSlotPanel(int index)
        {
            if (slotPanelRoots == null || index < 0 || index >= slotPanelRoots.Length) return;
            VisualElement panel = slotPanelRoots[index];
            if (panel == null) return;

            SlotPanelConfig cfg = slotPanels[index];
            List<BottomTableButton> elements = slotPanelElements[index];

            panel.Clear();

            for (int r = 0; r < cfg.rows; r++)
            {
                VisualElement rowEl = new VisualElement { name = "SlotRow" + r };
                rowEl.style.flexDirection = FlexDirection.Row;
                // Клик — общий обработчик (подсветка/команда/каст). Делегирование с ряда, как у BottomTables.
                rowEl.RegisterCallback<ClickEvent>(OnBottomTableClick);

                for (int c = 0; c < cfg.cols; c++)
                {
                    int idx = r * cfg.cols + c;
                    BottomTableButton btn = (elements != null && idx < elements.Count) ? elements[idx] : null;
                    if (IsSlotFilled(btn)) rowEl.Add(BuildBottomCell(btn));   // наш конструктор ячейки (BottomTables.cs)
                    else EmptyElementCreate(rowEl, idx);                      // штатная пустая ячейка ассета
                }

                panel.Add(rowEl);
            }
        }

        // Слот занят, если у кнопки есть иконка, действие, произвольный клик, текст или флаг displayOnly.
        static bool IsSlotFilled(BottomTableButton btn)
        {
            if (btn == null) return false;
            return btn.icon != null
                || btn.action != BottomTableAction.None
                || btn.onClick != null
                || btn.displayOnly
                || !string.IsNullOrEmpty(btn.topText)
                || !string.IsNullOrEmpty(btn.bottomText);
        }

        // --- Публичное API (для наполнения волной/умениями на шагах миграции) ---

        /// <summary>Готовы ли панели-сетки (InitSlotPanels отработал) — для отложенного наполнения извне.</summary>
        public bool SlotPanelsReady => slotPanelRoots != null;

        /// <summary>Индекс панели по имени (SlotPanelConfig.name из Inspector) или -1, если не найдена.</summary>
        public int SlotPanelIndexByName(string panelName)
        {
            if (slotPanels == null || string.IsNullOrEmpty(panelName)) return -1;
            for (int i = 0; i < slotPanels.Length; i++)
                if (slotPanels[i] != null && slotPanels[i].name == panelName) return i;
            return -1;
        }

        /// <summary>Полностью задать содержимое панели (позиционно) и перерисовать.</summary>
        public void SetSlotPanelButtons(int index, IEnumerable<BottomTableButton> buttons)
        {
            if (slotPanelElements == null || index < 0 || index >= slotPanelElements.Length) return;
            if (slotPanelElements[index] == null) slotPanelElements[index] = new List<BottomTableButton>();
            slotPanelElements[index].Clear();
            if (buttons != null) slotPanelElements[index].AddRange(buttons);
            RenderSlotPanel(index);
        }
    }
}
