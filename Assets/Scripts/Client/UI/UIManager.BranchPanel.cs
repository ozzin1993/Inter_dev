using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>
    /// Партиал UIManager: угловая панель веток Душ Нежити (N4). Третья кнопка-тогл под кнопкой техов (левый верх).
    /// Панель: 5 колонок-веток × 3 ряда-тира, в каждой ячейке две под-кнопки А/Б с иконкой и ценой в Душах.
    /// Состояния штатными классами .activeAbility (куплено) / .locked (недоступно/не хватает Душ) / обычный (доступно).
    /// Серверо-авторитетно: хост зовёт MatchManager напрямую, клиент шлёт UnlockSoulOptionServerRpc. Данные/гейтинг —
    /// из MatchManager.BranchTechs (единый источник). Построение/подписки проводятся из UIManager.CornerTables.cs.
    /// InGame.uxml не правится (строится в C#, как остальные угловые таблицы). Иконка кнопки — в Inspector.
    /// </summary>
    public partial class UIManager : MonoBehaviour
    {
        [Header("Панель веток Душ (Нежить) — N4")]
        [Tooltip("Иконка угловой кнопки веток Душ (третья кнопка, под кнопкой техов).")]
        [SerializeField] Texture2D branchButtonIcon;

        VisualElement branchCornerPanel;   // панель веток (скрыта до нажатия)
        VisualElement branchGrid;          // контейнер колонок-веток

        // Вызывается из InitCornerTables() ПОСЛЕ создания cornerTablesRoot (проводка // N4).
        void InitBranchPanel()
        {
            if (cornerTablesRoot == null || branchCornerPanel != null) return;

            // Кнопка веток — под кнопкой техов (левый верхний столбец кнопок).
            VisualElement branchButton = BuildCornerButton(branchButtonIcon);
            branchButton.style.left = cornerTablesSideOffset;
            branchButton.style.top = cornerTablesTopOffset + cornerButtonSize + cornerPanelGap;
            branchButton.RegisterCallback<ClickEvent>(_ => ToggleBranchPanel());
            cornerTablesRoot.Add(branchButton);

            // Панель — правее столбца кнопок, чтобы не наезжать на панель техов.
            branchCornerPanel = BuildCornerPanel();
            branchCornerPanel.style.left = cornerTablesSideOffset + cornerButtonSize + cornerPanelGap;
            branchCornerPanel.style.top = cornerTablesTopOffset + cornerButtonSize + cornerPanelGap;
            branchGrid = new VisualElement { name = "BranchGrid" };
            branchGrid.style.flexDirection = FlexDirection.Row;   // колонки-ветки
            branchGrid.RegisterCallback<ClickEvent>(OnSoulCellClick);
            branchCornerPanel.Add(branchGrid);
            cornerTablesRoot.Add(branchCornerPanel);
        }

        // Свой тогл (общий ToggleCornerPanel завязан на техи/ГЗ): при открытии — перерисовать актуальным состоянием.
        void ToggleBranchPanel()
        {
            if (branchCornerPanel == null) return;
            bool show = branchCornerPanel.style.display != DisplayStyle.Flex;
            if (show) { RenderBranchPanel(); branchCornerPanel.style.display = DisplayStyle.Flex; }
            else branchCornerPanel.style.display = DisplayStyle.None;
        }

        void RenderBranchPanel()
        {
            if (branchGrid == null) return;
            branchGrid.Clear();

            MatchManager mm = MatchManager.Instance;
            if (mm == null) return;
            int team = CommandTeamForLocalPlayer();

            int branches = mm.SoulBranchCount(team);
            for (int b = 0; b < branches; b++)
            {
                VisualElement col = new VisualElement();
                col.style.flexDirection = FlexDirection.Column;
                int tiers = mm.SoulBranchTierCount(team, b);
                for (int t = 0; t < tiers; t++)
                {
                    VisualElement tierRow = new VisualElement();
                    tierRow.style.flexDirection = FlexDirection.Row;
                    tierRow.Add(BuildSoulCell(mm, team, b, t, 0));   // опция А
                    tierRow.Add(BuildSoulCell(mm, team, b, t, 1));   // опция Б
                    col.Add(tierRow);
                }
                branchGrid.Add(col);
            }
        }

        GroupBox BuildSoulCell(MatchManager mm, int team, int branch, int tier, int option)
        {
            GroupBox cell = new GroupBox();
            cell.AddToClassList("AbilityButton");
            cell.userData = new Vector3Int(branch, tier, option);

            GroupBox iconElement = new GroupBox();
            iconElement.AddToClassList("AbilityButtonIcon");
            Texture2D tex = mm.SoulOptionIcon(team, branch, tier, option);
            if (tex != null) iconElement.style.backgroundImage = tex;
            cell.Add(iconElement);

            // Цена в Душах (первый элемент стоимости).
            ResourceWrapper[] cost = mm.SoulOptionCost(team, branch, tier, option);
            if (cost != null && cost.Length > 0 && cost[0] != null)
            {
                Label price = new Label(cost[0].value.ToString());
                price.style.fontSize = bottomCellFontSize;      // единый стиль из BottomTables
                price.style.color = bottomCellTextColor;
                price.style.unityTextAlign = TextAnchor.LowerCenter;
                cell.Add(price);
            }

            // Состояния (в порядке приоритета).
            if (!mm.SoulOptionExists(team, branch, tier, option))
                cell.AddToClassList("locked");                       // пустой узел
            else if (mm.IsSoulOptionUnlocked(team, branch, tier, option))
                cell.AddToClassList("activeAbility");                // куплено
            else if (!mm.IsSoulOptionUnlockable(team, branch, tier, option))
                cell.AddToClassList("locked");                       // недоступно (лимит / чужая опция / порядок тиров)
            else if (!mm.CanAffordSoulOption(team, branch, tier, option))
                cell.AddToClassList("locked");                       // не хватает Душ
            // иначе — доступно (обычный вид)

            return cell;
        }

        void OnSoulCellClick(ClickEvent evt)
        {
            VisualElement cell = ResolveCornerCell(evt);
            if (cell == null || !(cell.userData is Vector3Int c)) return;

            MatchManager mm = MatchManager.Instance;
            if (mm == null) return;
            int team = CommandTeamForLocalPlayer();
            if (!mm.IsSoulOptionUnlockable(team, c.x, c.y, c.z)) return;   // недоступно — молча игнор (сервер тоже валидирует)
            if (!mm.CanAffordSoulOption(team, c.x, c.y, c.z)) return;      // не хватает Душ — игнор

            if (NetworkConnectionHandler.isClient)
            {
                if (NetworkDataSync.Instance != null)
                    NetworkDataSync.Instance.UnlockSoulOptionServerRpc(team, c.x, c.y, c.z);
            }
            else
            {
                mm.TryUnlockSoulOption(team, c.x, c.y, c.z);
            }
        }

        // Перерисовка панели веток при изменении Душ (только если панель открыта — событие частое). Проводится из
        // UIManager.CornerTables.SubscribeCornerEvents/OnDestroy (// N4). Перерисовку по OnTechUnlock/OnTechLock
        // дёргает существующий OnTechChangedHandler (тоже проводка // N4).
        void OnSoulsChangedHandler(int team)
        {
            if (branchCornerPanel == null || branchCornerPanel.style.display != DisplayStyle.Flex) return;
            if (team != CommandTeamForLocalPlayer()) return;
            RenderBranchPanel();
        }
    }
}
