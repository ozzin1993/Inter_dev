using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // UIManager.BottomTableCommands.cs — команды Атака/Защита и подсветка режима. Вырезано 1:1 из UIManager.BottomTables.cs (разрезка на partial-ы, правило 22).
    public partial class UIManager
    {
        // --- Команды Атака/Защита ---

        // Делегирует выполнение команды РЯДА (commandGroup) в MatchManager. Вся логика формаций и юнитов — там.
        void ExecuteBottomTableCommand(BottomTableAction action, int commandGroup)
        {
            if (action == BottomTableAction.None) return;
            MatchManager mm = MatchManager.Instance;
            if (mm == null) return;
            int team = CommandTeamForLocalPlayer();
            if (NetworkConnectionHandler.isClient)
            {
                // Клиент: команда серверо-авторитетна — шлём намерение серверу (с рядом).
                if (NetworkDataSync.Instance != null) NetworkDataSync.Instance.TeamCommandServerRpc(team, (int)action, commandGroup);
            }
            else
            {
                if (action == BottomTableAction.Attack)        mm.SendAttackCommand(team, commandGroup);
                else if (action == BottomTableAction.Defence)  mm.SendDefenceCommand(team, commandGroup);
            }
        }

        /// <summary>
        /// Показать присланный сервером режим ряда (Атака/Защита) подсветкой соответствующей ячейки.
        /// Зовут хост локально и клиент по сети (NetworkDataSync.TeamCommand.cs) через фасад Presentation.UI.
        /// Только отрисовка: команду отсюда не отдаём — режим уже стоит на сервере (правило 6).
        /// Чужая команда игнорируется (игрок видит только свой режим). Таблицы ещё не построены — тихо выходим.
        /// </summary>
        public void ShowTeamCommand(int teamIndex, int groupIndex, BottomTableAction action)
        {
            if (action == BottomTableAction.None) return;           // «режим не выбран» подсветкой не отражаем
            if (teamIndex != CommandTeamForLocalPlayer()) return;   // режим чужой команды не показываем

            // Кнопки Атака/Защита живут в ДВУХ структурах: в старых нижних таблицах (ячейки лежат прямо в корне
            // таблицы) и в панелях-сетках (корень панели → ряд сетки → ячейка, см. UIManager.SlotPanels.cs).
            // Мигрированная таблица вообще не строится (IsBottomTableMigrated), и её корень остаётся null —
            // поэтому обходим обе структуры, а не одну.
            if (bottomTableContentRoots != null)
                for (int t = 0; t < bottomTableContentRoots.Length; t++)
                    HighlightCommandInContainer(bottomTableContentRoots[t], groupIndex, action);

            if (slotPanelRoots != null)
                for (int p = 0; p < slotPanelRoots.Length; p++)
                {
                    VisualElement panel = slotPanelRoots[p];
                    if (panel == null) continue;
                    for (int r = 0; r < panel.childCount; r++)          // дети панели — ряды сетки
                        HighlightCommandInContainer(panel.ElementAt(r), groupIndex, action);
                }
        }

        // Подсветить в ОДНОМ контейнере ячеек (нижняя таблица или ряд панели-сетки) кнопку ряда команд
        // groupIndex с действием action. Порядок ровно как у клика (OnBottomTableClick): подсветка снимается
        // только с ячеек того же ряда команд и только внутри этого контейнера. Нужной ячейки в контейнере нет —
        // контейнер не трогаем, иначе ряд погас бы, а подсвечивать было бы нечего.
        void HighlightCommandInContainer(VisualElement container, int groupIndex, BottomTableAction action)
        {
            if (container == null) return;

            VisualElement target = null;
            for (int i = 0; i < container.childCount; i++)
            {
                BottomTableButton b = container.ElementAt(i).userData as BottomTableButton;
                if (b != null && b.commandGroup == groupIndex && b.action == action) { target = container.ElementAt(i); break; }
            }
            if (target == null) return;

            for (int i = 0; i < container.childCount; i++)
            {
                VisualElement cell = container.ElementAt(i);
                BottomTableButton b = cell.userData as BottomTableButton;
                if (b != null && b.commandGroup == groupIndex) cell.RemoveFromClassList("activeAbility");
            }
            target.AddToClassList("activeAbility");
        }
    }
}
