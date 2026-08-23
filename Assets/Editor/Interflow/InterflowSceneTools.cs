using UnityEditor;
using UnityEngine;

namespace StrategyCore
{
    // [Interflow 2026-08-17, ревью «долг редактора»] Ручная пересборка навмешей сцены.
    // Автоматическая пересборка убрана из каждого сохранения сцены (SceneCustomData) и осталась
    // на входе в Play Mode (DuplicateWatcher) и здесь.
    public static class InterflowSceneTools
    {
        [MenuItem("Tools/Interflow/Пересобрать навмеши сцены")]
        public static void RebuildSceneNavmeshes()
        {
            DuplicateWatcher.NavmeshGenerate();
            Debug.Log("[Interflow] Навмеши сцены пересобраны вручную (Tools → Interflow).");
        }
    }
}
