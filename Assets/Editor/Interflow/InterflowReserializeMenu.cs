using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StrategyCore
{
    // [Interflow 2026-08-01] Принудительная пересериализация выбранных ассетов штатным API.
    // Нужда: бинарные сцены (Artsiom и др.) не переходят в текст обычным сохранением даже при
    // Asset Serialization = Force Text; ForceReserializeAssets перезаписывает их в актуальном режиме.
    public static class InterflowReserializeMenu
    {
        [MenuItem("Tools/Interflow/Пересериализовать выбранные ассеты")]
        static void ReserializeSelection()
        {
            string[] paths = Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).ToArray();
            if (paths.Length == 0)
            {
                Debug.LogWarning("[Interflow] Ничего не выбрано в окне Project — выдели ассеты и повтори.");
                return;
            }
            AssetDatabase.ForceReserializeAssets(paths);
            AssetDatabase.SaveAssets();
            Debug.Log("[Interflow] Пересериализовано ассетов: " + paths.Length + "\n" + string.Join("\n", paths));
        }
    }
}
