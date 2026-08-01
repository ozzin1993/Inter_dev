using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Interflow-партиал Unit: класс (роль) юнита и реакция на общие приказы команды.
    /// Наш файл — сериализуемые поля добавляются на Unit БЕЗ правки ассета (правило 1, без форк-долга).
    /// Перенос из временного UnitClass.cs (план §4.3). В Inspector поля видны внизу компонента Unit.
    /// Используется AutoAbilityUser (приоритет цели) и MatchManager.DefenceSlots (фильтр кнопок Атака/Защита).
    /// </summary>
    public partial class Unit
    {
        /// <summary>Боевая роль юнита.</summary>
        public enum UnitCategory
        {
            [InspectorName("Боец")]    Fighter,
            [InspectorName("Танк")]    Tank,
            [InspectorName("Стрелок")] Shooter,
            [InspectorName("Маг")]     Mage,
            [InspectorName("Герой")]   Hero
        }

        [Header("Класс (Interflow)")]
        [Tooltip("Боевая роль юнита. Используется AutoAbilityUser для приоритета цели и кнопками Атака/Защита для фильтрации.")]
        public UnitCategory unitCategory = UnitCategory.Fighter;

        [Header("Приоритет цели атаки (Interflow)")]
        [Tooltip("Упорядоченный список категорий целей. При выборе НОВОЙ цели юнит предпочитает ближайшего из первой непустой категории (сначала элемент [0], затем [1] и т.д.). " +
                 "Пусто — берётся командный дефолт из MatchManager; если и он пуст — штатный выбор (ближайший). Уже начатый бой не прерывается.")]
        public UnitCategory[] targetPriority;

        [Header("Реакция на общие приказы (Interflow)")]
        [Tooltip("Реагирует на кнопку АТАКА (весь отряд в атаку). ВЫКЛ — юнит игнорирует этот приказ.")]
        public bool respondsToAttackCommand = true;
        [Tooltip("Реагирует на кнопку ЗАЩИТА (весь отряд в защиту). ВЫКЛ — юнит игнорирует этот приказ.")]
        public bool respondsToDefenceCommand = true;

        [Header("Могилка (Interflow)")]
        [Tooltip("Оставляет ли юнит могилку при смерти. ВЫКЛ — могилка не создаётся. Дефолт ВЫКЛ (задаётся на префабе юнита).")]
        public bool leavesGrave = false;
        [Tooltip("Тир юнита (например, 1–5). Копируется в могилку для будущих механик (воскрешение/поиск).")]
        public int tier = 1;
    }
}
