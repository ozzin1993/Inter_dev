using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Interflow-партиал MatchManager: ряды кнопок Атака/Защита с фильтром по классу юнита (Unit.unitCategory).
    /// Каждый ряд кнопок = «группа команд» (индекс). Ряд задаёт НАБОР классов, которыми он командует.
    /// Кнопка (BottomTableButton.commandGroup) ссылается на индекс ряда. Ядро ассета не правится (правило 1).
    /// Единый источник наборов классов — здесь (commandGroups); кнопки хранят только индекс ряда.
    /// </summary>
    public partial class MatchManager
    {
        [Serializable]
        public class CommandGroupConfig
        {
            [Tooltip("Имя ряда — только для читаемости в Inspector (напр. «Ближний бой», «Стрелки»). На логику не влияет.")]
            public string name;

            [Tooltip("Классы юнитов (Unit.unitCategory), которыми командует этот ряд кнопок Атака/Защита. " +
                     "Юнит попадает под команду ряда, если его класс есть в наборе. Пустой набор — без фильтра (все классы).")]
            public Unit.UnitCategory[] categories;
        }

        [Header("Ряды кнопок Атака/Защита (по классам)")]
        [Tooltip("Ряды кнопок Атака/Защита. Индекс записи = BottomTableButton.commandGroup. Каждый ряд задаёт набор " +
                 "классов юнитов, которыми он командует. Пусто — один общий ряд без фильтра (командует всеми боевыми). " +
                 "Пересечение наборов не рекомендуется: при спавне новый юнит наследует команду ряда с бОльшим индексом.")]
        [SerializeField] CommandGroupConfig[] commandGroups;

        // Число рядов (минимум 1: пустой конфиг = один общий ряд без фильтра = прежнее поведение).
        int CommandGroupCount => (commandGroups != null && commandGroups.Length > 0) ? commandGroups.Length : 1;

        // Валиден ли индекс ряда.
        bool IsValidCommandGroup(int group) => group >= 0 && group < CommandGroupCount;

        // Входит ли класс юнита в набор ряда. Пустой конфиг ИЛИ пустой набор ряда = «все классы» (без фильтра).
        bool CommandGroupMatches(int group, Unit u)
        {
            if (u == null) return false;
            if (commandGroups == null || commandGroups.Length == 0) return true; // нет конфига — один общий ряд
            if (group < 0 || group >= commandGroups.Length) return false;
            Unit.UnitCategory[] cats = commandGroups[group].categories;
            if (cats == null || cats.Length == 0) return true;                    // пустой набор — без фильтра
            for (int i = 0; i < cats.Length; i++) if (cats[i] == u.unitCategory) return true;
            return false;
        }

        // Ряд, к которому относится юнит для НАСЛЕДОВАНИЯ команды (память). Пересечение → ряд с бОльшим индексом.
        // -1 — если класс юнита не покрыт ни одним рядом (кнопки его не трогают, идёт по дефолту MatchManager).
        int CommandGroupOfUnit(Unit u)
        {
            if (u == null) return -1;
            if (commandGroups == null || commandGroups.Length == 0) return 0;     // нет конфига — один общий ряд
            for (int g = commandGroups.Length - 1; g >= 0; g--)
                if (CommandGroupMatches(g, u)) return g;
            return -1;
        }

        // Отфильтровать юнитов по классам ряда (для команды конкретного ряда).
        List<Unit> FilterByCommandGroup(List<Unit> units, int group)
        {
            List<Unit> result = new List<Unit>(units.Count);
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u != null && !u.dead && CommandGroupMatches(group, u)) result.Add(u);
            }
            return result;
        }

        // Текущий режим команды (Атака/Защита/None) для РЯДА, к которому относится юнит. Безопасно к null/границам.
        // None — если класс юнита не покрыт ни одним рядом или память ещё не инициализирована.
        BottomTableAction CurrentCommandForUnit(int team, Unit u)
        {
            if (u == null) return BottomTableAction.None;
            int g = CommandGroupOfUnit(u);
            if (g < 0 || !IsValidCommandGroup(g)) return BottomTableAction.None;
            if (currentCommand == null || team < 0 || team >= currentCommand.Length || currentCommand[team] == null) return BottomTableAction.None;
            if (g >= currentCommand[team].Length) return BottomTableAction.None;
            return currentCommand[team][g];
        }
    }
}
