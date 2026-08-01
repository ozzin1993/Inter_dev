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
                                // Create new value
                                int newID = UnityEngine.Random.Range(1, 99999);
                                while (unitTypeIDs.Contains(newID))
                                {
                                    newID = UnityEngine.Random.Range(1, 99999);
                                }

                                // Assign
                                unit.unitTypeID = newID;
                                // Save the prefab asset
                                PrefabUtility.SavePrefabAsset(prefab);
                            }
                        }
                    }
                }
            }
        }
    }
}
