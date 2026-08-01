using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Общий конфиг раскладки сетки слотов (один ассет на ВСЕ сетки — гарантирует, что слот №N
    /// означает одно и то же место в каждой сетке, и юнит переходит на «тот же» слот у другой башни).
    /// На самой сетке (DefenceGrid) задаётся только её положение/смещение. Создаётся через
    /// Assets → Create → StrategyCore → Defence Grid Config.
    /// </summary>
    [CreateAssetMenu(fileName = "DefenceGridConfig", menuName = "StrategyCore/Defence Grid Config")]
    public class DefenceGridConfig : ScriptableObject
    {
        [Tooltip("Радиус слота. Шаг между центрами слотов = 2×радиус (слоты ставятся вплотную).")]
        [Min(0.01f)]
        public float slotRadius = 0.5f;

        [Tooltip("Сколько слотов в одном ряду.")]
        [Min(1)]
        public int slotsPerRow = 6;

        [Tooltip("Сколько рядов на один приоритет (1 или 2). Заполняется всё равно с первого ряда.")]
        [Range(1, 2)]
        public int rowsPerPriority = 1;

        [Tooltip("Количество приоритетов (линий). Итоговое число рядов = приоритеты × рядов_на_приоритет.")]
        [Min(1)]
        public int priorities = 4;

        /// <summary>Шаг между центрами слотов (диаметр).</summary>
        public float SlotStep => slotRadius * 2f;

        /// <summary>Полная ёмкость одной линии (приоритета): слотов в ряду × рядов на приоритет.</summary>
        public int BandCapacity => Mathf.Max(1, slotsPerRow) * Mathf.Max(1, rowsPerPriority);
    }
}
