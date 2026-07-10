using UnityEditor;
using UnityEngine;

namespace StrategyCore
{
    // ============================= INTERFLOW EDITOR — НАСТРОЙКИ (фаза E3) ==
    // Пути создания ассетов держим в SO-настройке (правило 3 — не хардкодить пути в коде).
    // Один ассет на проект: find-or-create в служебной папке инструмента (Assets/Editor/Interflow).
    // Editor-only (UnityEditor) — компилируется в editor-сборку, в билд не попадает (правило 6 не затрагивается).
    public class InterflowEditorSettings : ScriptableObject
    {
        [Tooltip("Папка для новых ассетов FactionConfig (кнопка «Новая фракция»). Дефолт — конвенция проекта.")]
        public string factionCreateFolder = "Assets/StrategyCore/Factions";

        [Tooltip("Папка для новых ассетов Technology (кнопка «Создать тех»). Должна быть внутри Resources/Technology — " +
                 "иначе штатный TechnologyManager (Resources.LoadAll) её не увидит.")]
        public string technologyCreateFolder = "Assets/StrategyCore/Resources/Technology";

        [Tooltip("Папка по умолчанию для новых юнит-префабов (визард «Создать юнита»). Должна быть внутри Resources/UnitPrefabs — " +
                 "иначе игра (Resources.LoadAll(«UnitPrefabs»)) юнит не найдёт. В визарде путь можно сменить.")]
        public string unitPrefabCreateFolder = "Assets/StrategyCore/Resources/UnitPrefabs";

        [Tooltip("Эталонный Unit-префаб по умолчанию для визарда «Создать юнита» (новый юнит — копия эталона). " +
                 "В визарде можно выбрать другой; можно оставить пустым и указывать каждый раз.")]
        public GameObject unitTemplatePrefab;

        [Tooltip("Папка по умолчанию для новых умений (визард «Создать умение»). Дефолт — конвенция Resources/Ability.")]
        public string abilityCreateFolder = "Assets/StrategyCore/Resources/Ability";

        [Tooltip("Папка по умолчанию для новых эффекторов. Дефолт — конвенция Resources/Effectors.")]
        public string effectorCreateFolder = "Assets/StrategyCore/Resources/Effectors";

        [Tooltip("Папка по умолчанию для новых ресурсов (Resource).")]
        public string resourceCreateFolder = "Assets/StrategyCore/Resources/Resource";
        [Tooltip("Папка по умолчанию для новых типов брони (ArmorType).")]
        public string armorTypeCreateFolder = "Assets/StrategyCore/Resources/ArmorTypes";
        [Tooltip("Папка по умолчанию для новых типов урона (DamageType).")]
        public string damageTypeCreateFolder = "Assets/StrategyCore/Resources/DamageTypes";
        [Tooltip("Папка по умолчанию для новых звуков оружия (WeaponSound).")]
        public string weaponSoundCreateFolder = "Assets/StrategyCore/Resources/WeaponSound";
        [Tooltip("Папка по умолчанию для новых снарядов (Projectile — префаб с компонентом).")]
        public string projectileCreateFolder = "Assets/StrategyCore/Resources/Projectiles";

        // Путь по умолчанию для самого ассета настроек (папка инструмента E1 уже существует).
        const string SettingsAssetPath = "Assets/Editor/Interflow/InterflowEditorSettings.asset";

        /// <summary>Найти ассет настроек в проекте или создать его в папке инструмента.</summary>
        public static InterflowEditorSettings GetOrCreate()
        {
            var guids = AssetDatabase.FindAssets("t:InterflowEditorSettings");
            if (guids.Length > 0)
            {
                var found = AssetDatabase.LoadAssetAtPath<InterflowEditorSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (found != null) return found;
            }

            var settings = CreateInstance<InterflowEditorSettings>();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }
    }
}
