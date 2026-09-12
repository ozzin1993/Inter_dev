using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>Действие ячейки нижней таблицы по клику.</summary>
    // [Interflow 2026-08-01 ADR-005] enum BottomTableAction перенесён в Scripts/Core/Types (тип используется симуляцией).

    /// <summary>Одна кнопка нижней таблицы: иконка + действие.</summary>
    [Serializable]
    public class BottomTableButton
    {
        [Tooltip("Иконка ячейки (как у способностей).")]
        public Texture2D icon;

        [Tooltip("Действие по клику: None — только подсветка; Attack/Defence — команда группе юнитов.")]
        public BottomTableAction action = BottomTableAction.None;

        [Tooltip("Ряд команд (индекс в MatchManager.commandGroups): какой набор классов юнитов затрагивает " +
                 "эта кнопка Атака/Защита. 0 — первый ряд. Для None игнорируется.")]
        public int commandGroup = 0;

        // Произвольное действие по клику (рантайм, не из Inspector). Если задано — выполняется вместо
        // логики Attack/Defence, подсветка не ставится (моментальная кнопка). Так внешние конструкторы
        // (напр. WaveBuilderUI) кладут свои кнопки в нижнюю таблицу, не зашивая их логику в UIManager.
        [NonSerialized] public Action onClick;

        // Текст ячейки (рантайм, не из Inspector): верхняя и нижняя строки. Если заданы — ячейка рисует
        // две строки текста вместо иконки. Используется внешними конструкторами (напр. WaveBuilderUI:
        // цена сверху, текущее кол-во в волне снизу).
        [NonSerialized] public string topText;
        [NonSerialized] public string bottomText;

        // Информационная ячейка: по клику нет ни подсветки, ни действия (только показ текста).
        [NonSerialized] public bool displayOnly;

        // Заблокированная ячейка (штатный класс .locked — серый вид): напр. умение героя, недоступное по уровню.
        [NonSerialized] public bool locked;
    }

    /// <summary>Содержимое одной таблицы (список кнопок слева-направо, с переносом).</summary>
    [Serializable]
    public class BottomTableContent
    {
        [Tooltip("Кнопки таблицы по порядку.")]
        public List<BottomTableButton> buttons = new List<BottomTableButton>();
    }

    /// <summary>
    /// Партиал-расширение UIManager: нижние таблицы HUD.
    /// Таблицы — контейнеры с flex-wrap (как штатный .AbilityScrollView), ячейки в стиле .AbilityButton.
    /// Содержимое настраивается в Inspector (tableContents) и/или меняется в рантайме через публичное API.
    /// Клик: подсветка штатным .activeAbility + действие ячейки (Attack/Defence — команда группе юнитов).
    /// Команды серверо-авторитетны: на сервере — прямой Unit.AttackMove (атака) / сетки слотов (защита); клиент шлёт запрос через NetworkDataSync.
    /// На слоте abilityTableSlot вместо общей таблицы строится отдельная ветка способностей (BottomAbilityTable):
    /// набор берётся из MatchManager.Team(commandTeamSlot).centralAbilities, иконка — из Ability.icon[0]. Сама Ability
    /// хранится в userData ячейки; клик активирует её серверо-авторитетно через фиксированного юнита-кастера
    /// команды (MatchManager.Team(slot).abilityCaster, штатный Unit.UseAbilityItem). Поддержаны Active-способности.
    /// Скрытие штатного HUD — здесь же (LateUpdate), т.к. компонент HudElementDisabler в сцене не привязан.
    /// Все размеры/количества/содержимое — в Inspector, без хардкода (правило «всё в Inspector»).
    /// </summary>
    public partial class UIManager : MonoBehaviour
    {
        [Header("Нижние таблицы")]
        [Tooltip("Сколько таблиц разместить в ряд по нижней кромке. У каждой свой источник данных.")]
        [SerializeField] int bottomTablesCount = 3;

        [Tooltip("Индексы СТАРЫХ нижних таблиц, которые НЕ строить — они мигрированы в панели-сетки (slotPanels). " +
                 "Напр. 0 = левая (атака/защита) уже в угловой панели 2×2. Когда сюда попадут все индексы — " +
                 "старый механизм BottomTables удаляется целиком (будущий шаг).")]
        [SerializeField] int[] migratedBottomTables;

        [Tooltip("Число столбцов в таблице. Строки добавляются автопереносом (flex-wrap). " +
                 "Отдельной таблице ширину можно переопределить в рантайме через SetBottomTableColumns.")]
        [SerializeField] int bottomTableColumns = 3;

        [Tooltip("Минимум ячеек в таблице: пустые добиваются, чтобы рамка была видна всегда. 9 = сетка 3×3.")]
        [SerializeField] int bottomTableMinCells = 9;

        [Tooltip("Ширина места под одну ячейку (кнопка + поля), px. Задаёт ширину таблицы под нужное число столбцов.")]
        [SerializeField] int bottomTableCellFootprint = 72;

        [Tooltip("Отступ таблиц от нижней кромки экрана, px.")]
        [SerializeField] int bottomTablesBottomOffset = 10;

        [Tooltip("Горизонтальный отступ контейнера таблиц от краёв экрана, px.")]
        [SerializeField] int bottomTablesSideOffset = 10;

        [Tooltip("Размер шрифта текста в ячейках цены/количества, px.")]
        [SerializeField] int bottomCellFontSize = 12;

        [Tooltip("Цвет текста в ячейках цены/количества.")]
        [SerializeField] Color bottomCellTextColor = Color.white;

        [Tooltip("Начальное содержимое таблиц. Индекс = порядок слева направо: элемент 0 — ЛЕВАЯ таблица. " +
                 "Для левой таблицы задай 2 кнопки: icon + action=Attack и icon + action=Defence. " +
                 "Слот abilityTableSlot этим массивом НЕ управляется — там отдельная ветка способностей.")]
        [SerializeField] BottomTableContent[] tableContents;

        [Tooltip("ФОЛЛБЭК-индекс команды (0=A, 1=B): применяется, только если команда игрока не резолвится " +
                 "автоматически (CommandTeamForLocalPlayer по ownerPlayer == currentPlayer).")]
        [SerializeField] int commandTeamSlot = 0;

        [Header("Способности (центральная таблица)")]
        [Tooltip("Позиция ветки способностей в нижнем ряду (0 = крайняя левая). При 3 таблицах 1 = центр. " +
                 "На этом слоте вместо общей таблицы строится отдельная ветка способностей. " +
                 "Вне диапазона [0..bottomTablesCount) — ветка не создаётся.")]
        [SerializeField] int abilityTableSlot = 1;

        [Tooltip("Имя панели умений главного здания (Center, rows=1, cols=3) — один ряд из трёх ячеек. " +
                 "Умения героя в этой панели больше не показываются: для них будет отдельная панель.")]
        [SerializeField] string abilitySlotPanelName = "Умения";

        int lastAbilitySlotsTeam = -1;   // команда, под которую наполнены слоты умений (переотрисовка при смене)

        // Набор способностей центральной таблицы больше НЕ хранится здесь — единый источник в
        // MatchManager.Team(commandTeamSlot).centralAbilities (см. CurrentTeamAbilities).

        [Header("Скрытие штатного HUD")]
        [Tooltip("ДИНАМИЧЕСКИЕ элементы: ядро ПЕРЕ-показывает их в рантайме при выборе юнита " +
                 "(RightArea/UnitBox/… в SubscribeToUnit). Их убираем из дерева ОДИН раз " +
                 "(RemoveFromHierarchy) — иначе разовый display=None не держится. Ядро дальше ставит " +
                 "display на detached-ссылке безвредно (после Init повторных Query по имени в ядре нет). " +
                 "Имена — из InGame.uxml, в коде не захардкожены.")]
        [SerializeField] string[] removedHudElements =
        {
            "RightArea",      // команды + сетка способностей (ядро показывает в SubscribeToUnit)
            "UnitBox",        // портрет/статы выбранного юнита (UnitBoxDisplay)
            "InventoryBox",   // инвентарь (InventoryDisplay)
            "Processes",      // очередь процессов (ProcessDisplay)
            "Transport",      // слоты перевозимых юнитов (TransportDisplay)
            "Status",         // иконки эффекторов (DisplayStatusTab)
            "Descriptor",     // тултип (ShowDescriptor по наведению)
        };

        [Tooltip("СТАТИЧНЫЕ элементы: ядро их НЕ пере-показывает — достаточно скрыть ОДИН раз " +
                 "(display=None). Имена — из InGame.uxml.")]
        [SerializeField] string[] hiddenHudElements =
        {
            "MiniMap",        // миникарта
            "TechPanel",      // панель технологий
            "TechTreeButton", // кнопка TECH
            "MessageBox",     // чат (вывод)
            "ChatBox",        // обёртка чата
            "Tips",           // подсказки/хоткеи
            "Debug",          // отладочная панель
        };

        VisualElement bottomTablesRoot;               // общий контейнер (всегда виден)
        VisualElement[] bottomTableContentRoots;      // контент-контейнер каждой таблицы (flex-wrap)
        List<BottomTableButton>[] bottomTableElements;// данные: текущие кнопки каждой таблицы
        VisualElement bottomAbilityTableRoot;         // отдельная ветка способностей (центральная таблица)

        bool hudVisibilityApplied;    // разовое применение видимости HUD (RemoveFromHierarchy + display=None)

        void InitBottomTables()
        {
            if (bottomTablesRoot != null) return;            // уже построено
            if (uiDocument == null) return;

            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;

            if (bottomTablesCount <= 0 || bottomTableColumns <= 0) return;

            // Контейнер растянут на всю ширину по нижней кромке; таблицы распределены равномерно.
            bottomTablesRoot = new VisualElement { name = "BottomTables" };
            bottomTablesRoot.style.position = Position.Absolute;
            bottomTablesRoot.style.left = bottomTablesSideOffset;
            bottomTablesRoot.style.right = bottomTablesSideOffset;
            bottomTablesRoot.style.bottom = bottomTablesBottomOffset;
            bottomTablesRoot.style.flexDirection = FlexDirection.Row;
            bottomTablesRoot.style.justifyContent = Justify.SpaceAround;
            bottomTablesRoot.style.alignItems = Align.FlexEnd;

            bottomTableContentRoots = new VisualElement[bottomTablesCount];
            bottomTableElements = new List<BottomTableButton>[bottomTablesCount];

            float tableWidth = bottomTableColumns * bottomTableCellFootprint;

            for (int i = 0; i < bottomTablesCount; i++)
            {
                // Мигрированные в панели-сетки (slotPanels) таблицы не строим — иначе дубль на экране.
                // Переходный механизм до полного удаления BottomTables (см. migratedBottomTables).
                if (IsBottomTableMigrated(i)) continue;

                // Слот способностей — отдельная ветка (свои данные/рендер), а не общая таблица.
                // generic-массивы на этом индексе остаются null: общий API его не трогает (IsValidBottomTable).
                if (i == abilityTableSlot)
                {
                    BuildAbilityTableBranch(tableWidth);
                    continue;
                }

                // Стартовое содержимое из Inspector (если задано для этого индекса).
                bottomTableElements[i] = new List<BottomTableButton>();
                if (tableContents != null && i < tableContents.Length
                    && tableContents[i] != null && tableContents[i].buttons != null)
                    bottomTableElements[i].AddRange(tableContents[i].buttons);

                // Таблица: ряд с переносом — ширина фиксирует число столбцов, строки растут вниз.
                VisualElement table = new VisualElement { name = "BottomTable" };
                table.style.flexDirection = FlexDirection.Row;
                table.style.flexWrap = Wrap.Wrap;
                table.style.width = tableWidth;
                // Подсветка/команда по клику — делегирование с контейнера (как штатный AbilityHandler).
                table.RegisterCallback<ClickEvent>(OnBottomTableClick);
                bottomTablesRoot.Add(table);
                bottomTableContentRoots[i] = table;

                RenderBottomTable(i);
            }

            root.Add(bottomTablesRoot);

            // Разовое применение видимости штатного HUD (вместо пер-кадрового LateUpdate) — root уже готов.
            ApplyHudVisibility(root);
        }

        // --- Публичное API управления содержимым (вызывать по мере изменений в матче) ---

        /// <summary>Полностью задать содержимое таблицы.</summary>
        public void SetBottomTableElements(int tableIndex, IEnumerable<BottomTableButton> buttons)
        {
            if (!IsValidBottomTable(tableIndex)) return;
            bottomTableElements[tableIndex].Clear();
            if (buttons != null) bottomTableElements[tableIndex].AddRange(buttons);
            RenderBottomTable(tableIndex);
        }

        /// <summary>Добавить кнопку в таблицу.</summary>
        public void AddBottomTableElement(int tableIndex, BottomTableButton button)
        {
            if (!IsValidBottomTable(tableIndex) || button == null) return;
            bottomTableElements[tableIndex].Add(button);
            RenderBottomTable(tableIndex);
        }

        /// <summary>Убрать кнопку из таблицы. Возвращает true, если кнопка была найдена.</summary>
        public bool RemoveBottomTableElement(int tableIndex, BottomTableButton button)
        {
            if (!IsValidBottomTable(tableIndex)) return false;
            bool removed = bottomTableElements[tableIndex].Remove(button);
            if (removed) RenderBottomTable(tableIndex);
            return removed;
        }

        /// <summary>Очистить таблицу (останутся только пустые ячейки до минимума).</summary>
        public void ClearBottomTable(int tableIndex)
        {
            if (!IsValidBottomTable(tableIndex)) return;
            bottomTableElements[tableIndex].Clear();
            RenderBottomTable(tableIndex);
        }

        bool IsValidBottomTable(int tableIndex)
        {
            // Слот способностей хранит null в generic-массиве — общий API его не обслуживает.
            return bottomTableElements != null
                && tableIndex >= 0 && tableIndex < bottomTableElements.Length
                && bottomTableElements[tableIndex] != null;
        }

        // Мигрирована ли старая нижняя таблица в панель-сетку (slotPanels) — такую не строим (переходный шаг).
        bool IsBottomTableMigrated(int index)
        {
            if (migratedBottomTables == null) return false;
            for (int i = 0; i < migratedBottomTables.Length; i++)
                if (migratedBottomTables[i] == index) return true;
            return false;
        }

        /// <summary>
        /// Задать число столбцов конкретной таблицы в рантайме (ширина = columns × footprint).
        /// Нужно внешним конструкторам, кладущим в ряд больше ячеек, чем общее число столбцов
        /// (напр. WaveBuilderUI: иконка/+/−/цена). Другие таблицы не затрагиваются.
        /// </summary>
        public void SetBottomTableColumns(int tableIndex, int columns)
        {
            if (bottomTableContentRoots == null) return;
            if (tableIndex < 0 || tableIndex >= bottomTableContentRoots.Length) return;
            if (columns <= 0) return;
            VisualElement table = bottomTableContentRoots[tableIndex];
            if (table == null) return;     // напр. слот способностей (своя ветка)
            table.style.width = columns * bottomTableCellFootprint;
        }

        /// <summary>Команда локального игрока (0=A, 1=B) — единый источник для внешних конструкторов кнопок.</summary>
        // [Interflow fix 2026-06-27] Единый источник истины — резолвер CommandTeamForLocalPlayer (а не статичное поле commandTeamSlot).
        // Раньше возвращалось поле=0 → у не-нулевого слота WaveBuilderUI/центральная таблица метили в чужую команду (волна 0 + owner-гейт режет).
        public int CommandTeamSlot => CommandTeamForLocalPlayer();

        /// <summary>
        /// Команда, которой управляет ЛОКАЛЬНЫЙ игрок: та, чей ownerPlayer == SlotManager.currentPlayer.
        /// Фоллбэк — Inspector commandTeamSlot (если не резолвится). Источник для нижних и угловых таблиц.
        /// </summary>
        int CommandTeamForLocalPlayer()
        {
            MatchManager mm = MatchManager.Instance;
            if (mm != null && SlotManager.Instance != null)
            {
                int p = SlotManager.Instance.currentPlayer;
                for (int i = 0; i < 2; i++)
                {
                    TeamWaveConfig cfg = mm.Team(i);
                    if (cfg != null && cfg.ownerPlayer == p) return i;
                }
            }
            return commandTeamSlot;
        }

        /// <summary>Готовы ли нижние таблицы (InitBottomTables отработал) — для отложенного наполнения извне.</summary>
        public bool BottomTablesReady => bottomTableElements != null;



        // Первичное наполнение и переотрисовка умений при смене команды локального игрока.
        // События смены команды у клиента нет (как в WaveBuilderUI) — отслеживаем сравнением int.
        // При неизменной команде работы нет (только сравнение), перерисовка — лишь по смене/видимости.
        void LateUpdate()
        {
            if (MatchManager.Instance == null || !SlotPanelsReady) return;
            int team = CommandTeamForLocalPlayer();
            if (team != lastAbilitySlotsTeam)
            {
                lastAbilitySlotsTeam = team;
                RefreshAbilitySlots();
            }
        }



    }
}
