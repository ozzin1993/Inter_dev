using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // UIManager.HudVisibility.cs — разовое скрытие штатного HUD. Вырезано 1:1 из UIManager.BottomTables.cs (разрезка на partial-ы, правило 22).
    public partial class UIManager
    {
        // --- Скрытие штатного HUD (разовое, вместо пер-кадрового LateUpdate) ---

        /// <summary>
        /// [Interflow fix 2026-09-09 hud-flash] ПРЕДВАРИТЕЛЬНОЕ скрытие: гасим штатный HUD в самом начале
        /// Start, до всех Query и подписок.
        ///
        /// Зачем. Основное скрытие (<see cref="ApplyHudVisibility"/>) идёт в КОНЦЕ Start — из InitBottomTables,
        /// и раньше идти не может: Start сначала разбирает эти же элементы по именам (RightArea, UnitBox,
        /// InventoryBox, Descriptor…) и вешает на них колбэки, а убранный из дерева элемент вернул бы null
        /// и уронил построение интерфейса. Пока Start не дошёл до конца, весь штатный HUD виден: подсказки,
        /// миникарта, «UNIT NAME», «INVENTORY», панель команд. А если Start до конца не доходит вовсе
        /// (упал по дороге, матч не поднялся), он так и остаётся на экране — этим и выглядела сцена полигона.
        ///
        /// Здесь элементы НЕ убираются из дерева, только гасятся: Query по именам дальше работает как прежде,
        /// а на экране их нет с первого кадра. Итоговое снятие делает ApplyHudVisibility, как и делало.
        /// </summary>
        void PreHideHud(VisualElement root)
        {
            if (root == null) return;

            HideByNames(root, removedHudElements);
            HideByNames(root, hiddenHudElements);
        }

        static void HideByNames(VisualElement root, string[] names)
        {
            if (names == null) return;

            for (int i = 0; i < names.Length; i++)
            {
                string elementName = names[i];
                if (string.IsNullOrEmpty(elementName)) continue;

                VisualElement ve = root.Q(elementName);
                if (ve != null) ve.style.display = DisplayStyle.None;
            }
        }

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
