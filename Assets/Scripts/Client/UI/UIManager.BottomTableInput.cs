using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // UIManager.BottomTableInput.cs — клик по ячейке и активация умения. Вырезано 1:1 из UIManager.BottomTables.cs (разрезка на partial-ы, правило 22).
    public partial class UIManager
    {
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
                if (NetworkDataSync.Instance != null) NetworkDataSync.Instance.CastCentralAbilityServerRpc(team, ability.id);
            }
            else
            {
                if (MatchManager.Instance != null) MatchManager.Instance.CastCentralAbilityById(team, ability.id);
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
    }
}
