using System.IO;
using UnityEditor;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow fix 2026-09-03 control-as-effectors] Создание четырёх СЛУЖЕБНЫХ состояний контроля —
    /// «Оглушение», «Немота», «Безоружие», «Слепота».
    ///
    /// Зачем скрипт, а не руками: контроль перестал быть отдельным механизмом и стал состоянием
    /// (решение Artsiom 30.08.2026). Воронки <c>Unit.Stun</c>/<c>Mute</c>/<c>Disarm</c>/<c>Blind</c>
    /// накладывают эти ассеты с переопределённой длительностью, а ссылки на них берут из справочника
    /// <see cref="StatusIconCatalog"/>. Ассеты содержательные, но чисто служебные: у них нет ни статов,
    /// ни урона — только признак контроля и значок, поэтому заводить их вкладкой редактора вручную
    /// значило бы четыре раза повторить одно и то же и рискнуть опечаткой в признаке.
    ///
    /// Правило 20 соблюдено: id выдаётся автоматически (<c>InterflowEditorUI.NextFreeId</c>), руками
    /// не задаётся. Значки берутся из того же справочника — это те самые спрайты, которыми шкала
    /// показывала контроль до перестройки.
    ///
    /// Пункт ИДЕМПОТЕНТЕН: уже назначенные в справочнике ассеты не пересоздаются и не перезаписываются.
    /// </summary>
    public static class InterflowControlEffectorsMenu
    {
        const string CatalogResourcePath = "Catalogs/StatusIconCatalog";

        [MenuItem("Tools/Interflow/Создать служебные состояния контроля")]
        public static void CreateControlEffectors()
        {
            StatusIconCatalog catalog = Resources.Load<StatusIconCatalog>(CatalogResourcePath);
            if (catalog == null)
            {
                EditorUtility.DisplayDialog("Служебные состояния контроля",
                    "Не найден справочник Resources/" + CatalogResourcePath + ".asset.\n" +
                    "Без него воронкам контроля неоткуда взять ссылки на состояния.", "Понятно");
                return;
            }

            var settings = InterflowEditorSettings.GetOrCreate();
            string folder = settings.effectorCreateFolder;
            InterflowEditorUI.EnsureFolder(folder);

            SerializedObject so = new SerializedObject(catalog);
            int created = 0;

            created += Ensure(so, folder, "stunEffector", "Eff_Control_Stun", "Оглушение",
                              "stuns", catalog.stunIcon,
                              "Юнит замирает и ничего не делает, пока держится это состояние.");

            created += Ensure(so, folder, "muteEffector", "Eff_Control_Mute", "Немота",
                              "mutes", catalog.mutedIcon,
                              "Юнит не может применять умения, пока держится это состояние.");

            created += Ensure(so, folder, "disarmEffector", "Eff_Control_Disarm", "Безоружие",
                              "disarms", catalog.disarmedIcon,
                              "Юнит не может атаковать, пока держится это состояние.");

            created += Ensure(so, folder, "blindEffector", "Eff_Control_Blind", "Слепота",
                              "blinds", catalog.blindIcon,
                              "Юнит промахивается прямыми атаками. Фактический шанс промаха задаёт " +
                              "сила наложения: собственный шанс этого состояния равен 1.");

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Служебные состояния контроля] Создано новых: " + created +
                      ". Уже назначенные в справочнике не трогались. Справочник: " +
                      AssetDatabase.GetAssetPath(catalog));
        }

        /// <summary>
        /// Создать одно служебное состояние и прописать его в справочник, если поле ещё пустое.
        /// Возвращает 1, если ассет был создан, иначе 0.
        /// </summary>
        static int Ensure(SerializedObject catalogSo, string folder, string fieldName, string assetName,
                          string displayName, string controlFlagField, Texture2D icon, string description)
        {
            SerializedProperty field = catalogSo.FindProperty(fieldName);
            if (field == null)
            {
                Debug.LogError("[Служебные состояния контроля] В справочнике нет поля " + fieldName + ".");
                return 0;
            }

            if (field.objectReferenceValue != null) return 0;   // уже назначено — не трогаем

            string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, assetName + ".asset"));

            Effector effector = ScriptableObject.CreateInstance<Effector>();
            effector.id = InterflowEditorUI.NextFreeId("t:Effector", e => ((Effector)e).id);   // правило 20: id — авто
            effector.displayName = displayName;
            effector.description = description;
            effector.icon = icon;

            // Признак контроля. Через SerializedObject, а не присваиванием поля: имя признака приходит
            // параметром, и так его нельзя разойтись с тем, что читает пересчёт.
            SerializedObject effectorSo = new SerializedObject(effector);
            SerializedProperty flag = effectorSo.FindProperty(controlFlagField);
            if (flag == null)
            {
                Debug.LogError("[Служебные состояния контроля] У Effector нет признака " + controlFlagField + ".");
                Object.DestroyImmediate(effector);
                return 0;
            }
            flag.boolValue = true;

            // У слепоты собственный шанс промаха равен единице: фактический шанс приносит сила наложения
            // (воронка Unit.Blind кладёт в неё запрошенный шанс).
            if (controlFlagField == "blinds")
            {
                SerializedProperty chance = effectorSo.FindProperty("blindMissChance");
                if (chance != null) chance.floatValue = 1f;
            }
            effectorSo.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(effector, path);

            field.objectReferenceValue = effector;

            if (icon == null)
                Debug.LogWarning("[Служебные состояния контроля] У «" + displayName + "» нет значка: " +
                                 "соответствующий спрайт в справочнике не назначен. Без значка состояние " +
                                 "НЕ уедет клиенту единым каналом статусов — контроль будет виден только хосту.");

            return 1;
        }
    }
}
