using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // UIManager.BottomTablesRender.cs — отрисовка ячеек общей таблицы. Вырезано 1:1 из UIManager.BottomTables.cs (разрезка на partial-ы, правило 22).
    public partial class UIManager
    {
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
    }
}
