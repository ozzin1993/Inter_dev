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
            MatchManager mm = MatchManager.instance;
            if (mm != null && SlotManager.instance != null)
            {
                int p = SlotManager.instance.currentPlayer;
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

        // Перестраивает ячейки таблицы из её текущего списка (как штатный RebuildAbilityView).
        void RenderBottomTable(int tableIndex)
        {
            if (bottomTableContentRoots == null) return;
            if (tableIndex < 0 || tableIndex >= bottomTableContentRoots.Length) return;

            VisualElement table = bottomTableContentRoots[tableIndex];
            if (table == null) return;

            table.Clear();

            List<BottomTableButton> elements = bottomTableElements[tableIndex];

            // Занятые ячейки (с иконками и действием).
            for (int i = 0; i < elements.Count; i++)
                table.Add(BuildBottomCell(elements[i]));

            // Пустые ячейки до минимума — штатным методом ассета (рамка видна всегда).
            for (int i = elements.Count; i < bottomTableMinCells; i++)
                EmptyElementCreate(table, i);
        }

        // Ячейка в стиле кнопки способности. Действие/данные хранятся в userData для обработки клика.
        // Если у кнопки задан текст (topText/bottomText) — рисуем две строки вместо иконки.
        GroupBox BuildBottomCell(BottomTableButton button)
        {
            GroupBox cell = new GroupBox();
            cell.AddToClassList("AbilityButton");
            if (button != null && button.locked) cell.AddToClassList("locked");   // недоступно (серый вид)
            cell.userData = button;   // вся кнопка: действие Attack/Defence, произвольный onClick или текст

            bool hasText = button != null
                && (!string.IsNullOrEmpty(button.topText) || !string.IsNullOrEmpty(button.bottomText));

            if (hasText)
            {
                // Текстовая ячейка: верхняя строка (цена) над нижней (кол-во). По центру.
                cell.style.justifyContent = Justify.Center;
                cell.style.alignItems = Align.Center;

                Label topLabel = new Label(button.topText ?? string.Empty);
                Label bottomLabel = new Label(button.bottomText ?? string.Empty);
                StyleBottomCellLabel(topLabel);
                StyleBottomCellLabel(bottomLabel);
                cell.Add(topLabel);
                cell.Add(bottomLabel);
            }
            else
            {
                GroupBox iconElement = new GroupBox();
                iconElement.AddToClassList("AbilityButtonIcon");
                if (button != null && button.icon != null)
                    iconElement.style.backgroundImage = button.icon;   // Texture2D → StyleBackground
                cell.Add(iconElement);
            }

            return cell;
        }

        // Единый стиль строк текстовой ячейки (размер/цвет — из Inspector, без хардкода).
        void StyleBottomCellLabel(Label label)
        {
            label.style.fontSize = bottomCellFontSize;
            label.style.color = bottomCellTextColor;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
        }

        // --- Ветка способностей (центральная таблица) ---

        // Строит отдельный контейнер способностей и ставит его в нужную позицию нижнего ряда.
        // Тот же стиль/механика, что у общих таблиц (flex-wrap, .AbilityButton), но свои данные/рендер.
        void BuildAbilityTableBranch(float tableWidth)
        {
            VisualElement table = new VisualElement { name = "BottomAbilityTable" };
            table.style.flexDirection = FlexDirection.Row;
            table.style.flexWrap = Wrap.Wrap;
            table.style.width = tableWidth;
            // Клик — тот же обработчик: у Ability-ячеек userData не BottomTableButton, поэтому только подсветка.
            table.RegisterCallback<ClickEvent>(OnBottomTableClick);
            bottomTablesRoot.Add(table);
            bottomAbilityTableRoot = table;

            RenderAbilityTable();
        }

        // Перестраивает ячейки центральной таблицы из набора способностей текущей команды (commandTeamSlot).
        void RenderAbilityTable()
        {
            if (bottomAbilityTableRoot == null) return;

            bottomAbilityTableRoot.Clear();

            IReadOnlyList<Ability> abilities = CurrentTeamAbilities();
            int count = abilities != null ? abilities.Count : 0;

            // Занятые ячейки — иконки способностей.
            for (int i = 0; i < count; i++)
                bottomAbilityTableRoot.Add(BuildAbilityCell(abilities[i]));

            // Пустые ячейки до минимума — штатным методом ассета (рамка видна всегда).
            for (int i = count; i < bottomTableMinCells; i++)
                EmptyElementCreate(bottomAbilityTableRoot, i);
        }

        // Видимые способности ГЗ команды локального игрока — производное VisibleCentralAbilities(team).
        // Все способности на кастере с Awake; правила апгрейдов лишь показывают/скрывают (каст — по Ability.id).
        IReadOnlyList<Ability> CurrentTeamAbilities()
        {
            MatchManager mm = MatchManager.instance;
            if (mm == null) return null;
            return mm.VisibleCentralAbilities(CommandTeamForLocalPlayer());
        }

        // Ячейка способности в стиле кнопки. Сама Ability хранится в userData (для будущей активации);
        // сейчас клик по ней даёт только подсветку (см. OnBottomTableClick).
        GroupBox BuildAbilityCell(Ability ability)
        {
            GroupBox cell = new GroupBox();
            cell.AddToClassList("AbilityButton");
            cell.userData = ability;

            GroupBox iconElement = new GroupBox();
            iconElement.AddToClassList("AbilityButtonIcon");
            if (ability != null && ability.icon != null && ability.icon.Length > 0 && ability.icon[0] != null)
                iconElement.style.backgroundImage = ability.icon[0];   // Texture2D → StyleBackground
            cell.Add(iconElement);

            return cell;
        }

        /// <summary>Перерисовать центральную таблицу способностей (напр. после смены команды игрока).</summary>
        public void RefreshAbilityTable()
        {
            RenderAbilityTable();
        }

        // --- Умения в панели-сетке (переделка UI, шаг 4; нижний ряд — Герой, шаг 5) ---

        /// <summary>
        /// Наполняет панель умений главного здания (1×3). Ячейка закреплена за умением: не открытое
        /// (или не настроенное) умение → пустая ячейка на своём месте, соседи не сдвигаются.
        /// Умения героя здесь больше не строятся — под них будет отдельная панель.
        /// </summary>
        public void RefreshAbilitySlots()
        {
            if (!SlotPanelsReady) return;
            int panel = SlotPanelIndexByName(abilitySlotPanelName);
            if (panel < 0) return;                       // панель не настроена в Inspector — тихо

            SetSlotPanelButtons(panel, BuildCentralAbilitySlots(CommandTeamForLocalPlayer()));
        }

        // Ячейки умений ГЗ: что лежит в ячейке — знает MatchManager (стартовые умения расы + открытые узлами
        // дерева). Пустая ячейка → null. Каст переиспользует ActivateAbilityCell (серверо-авторитетно).
        List<BottomTableButton> BuildCentralAbilitySlots(int team)
        {
            List<BottomTableButton> res = new List<BottomTableButton>();
            MatchManager mm = MatchManager.instance;

            for (int slot = 0; slot < MatchManager.CentralAbilitySlotCount; slot++)
            {
                Ability ab = mm != null ? mm.CentralAbilityAtSlot(team, slot) : null;
                if (ab == null) { res.Add(null); continue; }   // пустая ячейка на своём месте

                Ability captured = ab;   // фиксируем для замыкания onClick
                Texture2D tex = (ab.icon != null && ab.icon.Length > 0) ? ab.icon[0] : null;
                res.Add(new BottomTableButton { icon = tex, onClick = () => ActivateAbilityCell(captured) });
            }
            return res;
        }

        // ВНИМАНИЕ: методы ряда героя ниже сейчас НИКЕМ не вызываются — ряд убран из панели умений ГЗ
        // (решение Artsiom 2026-07-24: панель ГЗ = 1×3). Код оставлен как заготовка под отдельную панель героя.
        //
        // Слоты умений Героя (нижний ряд): показываем ТОЛЬКО когда герой призван/жив (реш. Artsiom 1б).
        // Набор — способности живого героя (хост) или префаба расы (клиент/фоллбэк). Только Active (D3).
        // Недоступные по уровню — .locked (реш. Artsiom 2а): точно на хосте (знает уровень); у клиента
        // уровень героя не синкается → показываем активными, недоступный каст отклонит сервер (UseAbilityItem).
        List<BottomTableButton> BuildHeroAbilitySlots(int team, int count)
        {
            List<BottomTableButton> res = new List<BottomTableButton>();
            MatchManager mm = MatchManager.instance;

            if (mm == null || !mm.HeroAlive(team))              // 1б: до призыва / после смерти — пусто
            {
                for (int i = 0; i < count; i++) res.Add(null);
                return res;
            }

            IReadOnlyList<Ability> all = HeroAbilitySet(team);
            for (int i = 0; i < count; i++)
            {
                Ability ab = (all != null && i < all.Count) ? all[i] : null;
                if (ab != null && ab.type == AbilityType.Active)
                {
                    Ability captured = ab;   // фиксируем для замыкания onClick
                    Texture2D tex = (ab.icon != null && ab.icon.Length > 0) ? ab.icon[0] : null;
                    res.Add(new BottomTableButton
                    {
                        icon = tex,
                        onClick = () => CastHeroAbilityCell(team, captured.id),
                        locked = !HeroAbilityUnlocked(team, ab),
                    });
                }
                else
                {
                    res.Add(null);   // пустой слот на позиции (не-Active / нет умения)
                }
            }
            return res;
        }

        // Набор умений героя: живой герой (хост) или префаб расы (клиент/фоллбэк — набор детерминирован).
        IReadOnlyList<Ability> HeroAbilitySet(int team)
        {
            MatchManager mm = MatchManager.instance;
            if (mm == null) return null;
            Unit hero = mm.HeroUnit(team);
            if (hero != null && hero.abilities != null) return hero.abilities;              // хост: реальный герой
            TeamWaveConfig cfg = mm.Team(team);
            return cfg != null && cfg.heroPrefab != null ? cfg.heroPrefab.abilities : null; // фоллбэк: префаб расы
        }

        // Доступно ли умение героя по уровню. Хост знает уровень героя; клиент — нет (не синкается) → считаем доступным.
        bool HeroAbilityUnlocked(int team, Ability ab)
        {
            if (ab == null || ab.requiredLevel == null || ab.requiredLevel.Length == 0) return true;
            MatchManager mm = MatchManager.instance;
            int heroLevel;
            if (NetworkConnectionHandler.isClient)
            {
                heroLevel = mm != null ? mm.HeroLevelClient(team) : 0;          // клиент: синхронное зеркало уровня
            }
            else
            {
                Unit hero = mm != null ? mm.HeroUnit(team) : null;
                LevelingUnit lvl = hero != null ? hero.GetComponent<LevelingUnit>() : null;
                heroLevel = lvl != null ? lvl.level : 0;
            }
            return heroLevel >= ab.requiredLevel[0];                            // приближение: порог 1-го уровня умения
        }

        // Каст умения героя из таблицы: серверо-авторитетно (хост — напрямую, клиент — через RPC по Ability.id).
        void CastHeroAbilityCell(int team, int abilityId)
        {
            if (NetworkConnectionHandler.isClient)
            {
                if (NetworkDataSync.instance != null) NetworkDataSync.instance.CastHeroAbilityServerRpc(team, abilityId);
            }
            else
            {
                if (MatchManager.instance != null) MatchManager.instance.CastHeroAbilityById(team, abilityId);
            }
        }

        // Первичное наполнение и переотрисовка умений при смене команды локального игрока.
        // События смены команды у клиента нет (как в WaveBuilderUI) — отслеживаем сравнением int.
        // При неизменной команде работы нет (только сравнение), перерисовка — лишь по смене/видимости.
        void LateUpdate()
        {
            if (MatchManager.instance == null || !SlotPanelsReady) return;
            int team = CommandTeamForLocalPlayer();
            if (team != lastAbilitySlotsTeam)
            {
                lastAbilitySlotsTeam = team;
                RefreshAbilitySlots();
            }
        }

        // Активация способности из центральной таблицы фиксированным юнитом-кастером команды.
        // Серверо-авторитетно: штатный Unit.UseAbilityItem сам отправит команду серверу на клиенте
        // (NetworkConnectionHandler.isClient → NetworkCommandSync), на сервере исполнит (правило 6).
        // Поддержаны только Active-способности (без выбора цели на карте).
        void ActivateAbilityCell(Ability ability)
        {
            if (ability == null) return;
            int team = CommandTeamForLocalPlayer();
            Debug.Log($"[UIManager] Клик по способности: '{ability.name}', тип={ability.type}, команда={team}.");

            if (ability.type != AbilityType.Active)            // таргетные/прочие — вне текущего объёма
            {
                Debug.LogWarning($"[UIManager] '{ability.name}': тип {ability.type} ≠ Active — каст из таблицы не поддержан.");
                return;
            }

            // Каст ПО Ability.id (стабильный ключ) — снимает зависимость от идентичности массивов abilities на пирах.
            // Серверо-авторитетно: хост кастует напрямую, клиент шлёт запрос серверу (валидация — в MatchManager).
            if (NetworkConnectionHandler.isClient)
            {
                if (NetworkDataSync.instance != null) NetworkDataSync.instance.CastCentralAbilityServerRpc(team, ability.id);
            }
            else
            {
                if (MatchManager.instance != null) MatchManager.instance.CastCentralAbilityById(team, ability.id);
            }
        }

        // Клик по таблице: подсветка штатным .activeAbility + действие ячейки (если задано).
        // Делегирование с контейнера. Повторный клик снимает выделение. Выбор — по одной на таблицу.
        void OnBottomTableClick(ClickEvent evt)
        {
            VisualElement target = evt.target as VisualElement;
            if (target == null) return;

            // Поднимаемся до самой ячейки (.AbilityButton).
            VisualElement cell = target;
            while (cell != null && !cell.ClassListContains("AbilityButton"))
                cell = cell.parent;
            if (cell == null) return;
            Debug.Log($"[UIManager] Клик по ячейке нижней таблицы зафиксирован (userData={(cell.userData != null ? cell.userData.GetType().Name : "null")}).");

            BottomTableButton button = cell.userData as BottomTableButton;

            // Информационная ячейка (цена/кол-во) — ни подсветки, ни действия.
            if (button != null && button.displayOnly) return;

            // Заблокированная ячейка (.locked, напр. умение героя не по уровню) — клик игнорируется.
            if (button != null && button.locked) return;

            // Произвольное действие (моментальная кнопка, напр. конструктор волны) — выполнить и выйти,
            // подсветку не трогаем.
            if (button != null && button.onClick != null) { button.onClick(); return; }

            // Ячейка способности (userData = Ability): серверо-авторитетный каст по клику.
            // Моментальная кнопка — без залипающей подсветки (как onClick выше).
            Ability clickedAbility = cell.userData as Ability;
            if (clickedAbility != null) { ActivateAbilityCell(clickedAbility); return; }

            VisualElement table = cell.parent;
            if (table == null) return;

            bool wasActive = cell.ClassListContains("activeAbility");

            // Снять подсветку только с ячеек ЭТОГО РЯДА (commandGroup) — ряды независимы (кнопки другого ряда не гаснут).
            int clickedGroup = button != null ? button.commandGroup : 0;
            for (int i = 0; i < table.childCount; i++)
            {
                VisualElement sib = table.ElementAt(i);
                BottomTableButton sb = sib.userData as BottomTableButton;
                int sg = sb != null ? sb.commandGroup : 0;
                if (sg == clickedGroup) sib.RemoveFromClassList("activeAbility");
            }

            // Переключить выделение кликнутой ячейки.
            if (!wasActive) cell.AddToClassList("activeAbility");

            BottomTableAction action = button != null ? button.action : BottomTableAction.None;
            if (action != BottomTableAction.None)
            {
                if (!wasActive)
                {
                    // Выбор командной ячейки: делегируем в MatchManager (с рядом кнопки).
                    ExecuteBottomTableCommand(action, button.commandGroup);
                }
                // Повторный клик (снятие) — подсветка гаснет выше; команда на сервере не отменяется.
            }
        }

        // --- Команды Атака/Защита ---

        // Делегирует выполнение команды РЯДА (commandGroup) в MatchManager. Вся логика формаций и юнитов — там.
        void ExecuteBottomTableCommand(BottomTableAction action, int commandGroup)
        {
            if (action == BottomTableAction.None) return;
            MatchManager mm = MatchManager.instance;
            if (mm == null) return;
            int team = CommandTeamForLocalPlayer();
            if (NetworkConnectionHandler.isClient)
            {
                // Клиент: команда серверо-авторитетна — шлём намерение серверу (с рядом).
                if (NetworkDataSync.instance != null) NetworkDataSync.instance.TeamCommandServerRpc(team, (int)action, commandGroup);
            }
            else
            {
                if (action == BottomTableAction.Attack)        mm.SendAttackCommand(team, commandGroup);
                else if (action == BottomTableAction.Defence)  mm.SendDefenceCommand(team, commandGroup);
            }
        }

        // --- Скрытие штатного HUD (разовое, вместо пер-кадрового LateUpdate) ---

        // Применяется ОДИН раз из InitBottomTables (root уже построен ядром).
        // Динамические элементы (ядро пере-показывает при выборе юнита) убираем из дерева — тогда
        // повторный display=Flex ядра приходится на detached-ссылку и на экране не виден. NRE нет:
        // после Init ядро работает по сохранённым ссылкам, повторных Query по имени в ядре нет (проверено).
        // Статичные — просто display=None (ядро их не пере-показывает).
        void ApplyHudVisibility(VisualElement root)
        {
            if (hudVisibilityApplied) return;
            if (root == null) return;

            if (removedHudElements != null)
                for (int i = 0; i < removedHudElements.Length; i++)
                {
                    string elementName = removedHudElements[i];
                    if (string.IsNullOrEmpty(elementName)) continue;
                    VisualElement ve = root.Q(elementName);
                    if (ve != null) ve.RemoveFromHierarchy();
                }

            if (hiddenHudElements != null)
                for (int i = 0; i < hiddenHudElements.Length; i++)
                {
                    string elementName = hiddenHudElements[i];
                    if (string.IsNullOrEmpty(elementName)) continue;
                    VisualElement ve = root.Q(elementName);
                    if (ve != null) ve.style.display = DisplayStyle.None;
                }

            hudVisibilityApplied = true;
        }
    }
}
