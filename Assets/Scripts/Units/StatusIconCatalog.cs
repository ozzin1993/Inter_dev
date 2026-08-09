using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Каталог иконок статусов ядра + настройки шкалы статусов над полоской здоровья.
    /// Контент и числа — в ассете (правило 3): Resources/Catalogs/StatusIconCatalog.asset,
    /// редактируется в Interflow Editor → «Справочники». Читают его единый перечислитель
    /// значков (UnitStatusIcons) и шкала (UnitStatusIconsBar).
    /// </summary>
    [CreateAssetMenu(fileName = "StatusIconCatalog", menuName = "StrategyCore/Interflow/Каталог иконок статусов")]
    public class StatusIconCatalog : ScriptableObject
    {
        [Header("Иконки статусов ядра")]
        [Tooltip("Оглушение (стан)")]
        public Texture2D stunIcon;

        [Tooltip("Немота (нельзя использовать умения)")]
        public Texture2D mutedIcon;

        [Tooltip("Безоружие (нельзя атаковать)")]
        public Texture2D disarmedIcon;

        [Tooltip("Полиморф (превращение). Внимание: у клиента поле полиморфа ядром не реплицируется — иконка видна только хосту.")]
        public Texture2D polymorphedIcon;

        [Tooltip("Слепота (промахи атак, BlindDebuff)")]
        public Texture2D blindIcon;

        [Header("Шкала статусов над полоской здоровья")]
        [Tooltip("Материал-шаблон иконки-квада (текстура подставляется через MaterialPropertyBlock, свойство _Main). Пусто — материал создастся в рантайме на шейдере LookAtCamera.")]
        public Material iconMaterial;

        [Tooltip("Размер иконки, метры мира")]
        public float iconSize = 0.45f;

        [Tooltip("Зазор между иконками, м")]
        public float iconGap = 0.08f;

        [Tooltip("Вертикальный отступ ряда над полоской здоровья, м")]
        public float barOffsetY = 0.25f;

        // ======================== ЗАГРУЗКА ========================

        static StatusIconCatalog loaded;
        static bool loadTried;

        /// <summary>Каталог из Resources. null — ассета нет (иконки статусов ядра показываться не будут).</summary>
        public static StatusIconCatalog Get()
        {
            if (!loadTried)
            {
                loadTried = true;
                loaded = Resources.Load<StatusIconCatalog>("Catalogs/StatusIconCatalog");
                if (loaded == null)
                    Debug.LogWarning("[StatusIconCatalog] Ассет Resources/Catalogs/StatusIconCatalog не найден — " +
                                     "иконки статусов ядра в шкале и панели показываться не будут.");
            }
            return loaded;
        }
    }
}
