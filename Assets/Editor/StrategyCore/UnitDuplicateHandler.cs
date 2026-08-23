using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StrategyCore
{
    public class UnitDuplicateHandler : AssetPostprocessor
    {
        // This method will be called when a Unit is imported or modified
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (string asset in importedAssets)
            {
                // Check if the imported asset is a prefab
                if (asset.EndsWith(".prefab"))
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                    if (prefab != null)
                    {
                        Unit unit = prefab.GetComponent<Unit>();
                        if (unit != null)
                        {
                            // Create hashset of existing values
                            HashSet<int> unitTypeIDs = new HashSet<int>();
                            GameObject[] existingUnits = Resources.LoadAll<GameObject>("UnitPrefabs");

                            foreach (GameObject o in existingUnits)
                            {
                                Unit u = o.GetComponent<Unit>();
                                if (u != null && u != unit)
                                {
                                    unitTypeIDs.Add(u.unitTypeID);
                                }
                            }

                            // Duplicate or null
                            if (unit.unitTypeID == 0 || unitTypeIDs.Contains(unit.unitTypeID))
                            {
                                // [Interflow 2026-08-17 ревью] Свободный id детерминированно (максимум + 1) вместо Random.Range
                                int newID = 1;
                                foreach (int existing in unitTypeIDs) if (existing >= newID) newID = existing + 1;

                                // [Interflow 2026-08-17 ревью] Сохранение префаба отложено из постпроцессора импорта:
                                // SavePrefabAsset внутри OnPostprocessAllAssets каскадит повторные импорты (предупреждение документации Unity)
                                UnityEditor.EditorApplication.delayCall += () =>
                                {
                                    if (unit == null || prefab == null) return;
                                    unit.unitTypeID = newID;
                                    PrefabUtility.SavePrefabAsset(prefab);
                                };
                            }
                        }
                    }
                }
            }
        }
    }
}
