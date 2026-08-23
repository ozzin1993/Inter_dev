using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace StrategyCore
{
    [InitializeOnLoad]
    public static class DuplicateWatcher
    {
        static DuplicateWatcher()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode: // Entered Play Mode

                    break;

                case PlayModeStateChange.ExitingPlayMode: // Exiting Play Mode

                    break;

                case PlayModeStateChange.EnteredEditMode: // Returned to Edit Mode

                    break;

                case PlayModeStateChange.ExitingEditMode: // About to Enter Play Mode
                    // [Interflow fix 2026-08-01 sceditor-dissolve] Блок вкладки сцен SCEditor удалён — окно растворено в Interflow Editor.

                    AssignUniqueNetID();
                    NavmeshGenerate();
                    break;
            }
        }

        // [Interflow fix 2026-08-01 sceditor-dissolve] Перенесено из SCEditor.cs (окно растворено): пересборка NavMesh сцены.
        public static bool NavmeshDataSet(GameObject gameManager, GameObject navmeshHolder)
        {
            // Also we must rearrange and rebuild the navmesh (Unity issue, will throw a lot of log warnings)
            if (navmeshHolder != null)
            {
                Transform waterNav = navmeshHolder.transform.Find("WaterNavmesh");
                Transform groundNav = navmeshHolder.transform.Find("GroundNavmesh");
                Transform airNav = navmeshHolder.transform.Find("AirNavmesh");
                Transform InvisibilityNav = navmeshHolder.transform.Find("InvisibilityNavmesh");
                GameManager gm = gameManager.GetComponent<GameManager>();
                Grid gridComponent = gameManager.GetComponent<Grid>();

                // Similar code is in GameManager Navmesh initialization
                if (gm != null && waterNav != null && groundNav != null && airNav != null && InvisibilityNav != null)
                {
                    // Position navmesh in the center of the map and set the boundaries
                    NavMeshSurface groundNavmesh = groundNav.GetComponent<NavMeshSurface>();
                    NavMeshSurface waterNavmesh = waterNav.GetComponent<NavMeshSurface>();

                    groundNavmesh.center = new Vector3(gridComponent.width * 0.5f, 0, gridComponent.height * 0.5f);
                    groundNavmesh.size = new Vector3(gridComponent.width - 0.5f, Utils.raycastPointY, gridComponent.height - 0.5f);

                    waterNavmesh.center = groundNavmesh.center;
                    waterNavmesh.size = groundNavmesh.size;

                    groundNavmesh.BuildNavMesh();
                    waterNavmesh.BuildNavMesh();

                    EditorUtility.SetDirty(groundNavmesh);
                    EditorUtility.SetDirty(waterNavmesh);

                    // Air navmesh surface
                    Utils.airOffsetX = gridComponent.width * 1.5f;
                    NavMeshSurface airNavmesh = airNav.GetComponent<NavMeshSurface>();
                    airNavmesh.transform.position = new Vector3(Utils.airOffsetX, 0, 0);
                    airNavmesh.center = groundNavmesh.center;
                    airNavmesh.size = groundNavmesh.size;

                    BoxCollider box = airNavmesh.GetComponent<BoxCollider>();
                    box.center = airNavmesh.center;
                    box.size = new Vector3(airNavmesh.size.x, 0.01f, airNavmesh.size.z);
                    EditorUtility.SetDirty(box);

                    airNavmesh.BuildNavMesh();
                    EditorUtility.SetDirty(airNavmesh);

                    // Invisibility Navmesh
                    Utils.invisibilityOffsetY = gridComponent.height * 1.5f;
                    NavMeshSurface invisNav = InvisibilityNav.GetComponent<NavMeshSurface>();
                    if (gm.gameIncludesInvisible)
                    {
                        invisNav.gameObject.SetActive(true);
                        invisNav.transform.position = new Vector3(0, 0, 0);
                        invisNav.center = groundNavmesh.center;
                        invisNav.size = groundNavmesh.size;
                        invisNav.BuildNavMesh();
                        invisNav.transform.position = new Vector3(0, 0, Utils.invisibilityOffsetY);
                    }
                    else invisNav.gameObject.SetActive(false);
                    EditorUtility.SetDirty(invisNav);
                }
                else
                {
                    Debug.LogWarning("Where are your Ground, Water, Air and Invisibility navmeshes + GameManager component?");
                    return false;
                }
            }
            else
            {
                Debug.LogWarning("You scene has no 'Navmesh', is it intentional?");
                return false;
            }

            return true;
        }

        // Assigns to in-scene units` unique net ids
        public static void AssignUniqueNetID()
        {
            HashSet<UInt16> unitNetID = new HashSet<UInt16>(); // networkID hashset

            // [Interflow 2026-08-17 ревью] Ищем сразу компоненты Unit (было: перебор ВСЕХ объектов сцены с GetComponent на каждом)
            foreach (Unit unit in GameObject.FindObjectsByType<Unit>(FindObjectsSortMode.None))
            {
                if (unit.netID == 0 || unitNetID.Contains(unit.netID))
                {
                    UInt16 netID = (UInt16)UnityEngine.Random.Range(1, 65535);
                    while (unitNetID.Contains(netID))
                    {
                        netID = (UInt16)UnityEngine.Random.Range(1, 65535);
                    }
                    unit.netID = netID;
                    EditorUtility.SetDirty(unit);
                }
                unitNetID.Add(unit.netID);
            }
        }

        public static void NavmeshGenerate()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            GameObject[] rootObjects = activeScene.GetRootGameObjects();

            GameObject selectedGM = null;
            GameObject navmeshHolder = null;

            foreach (GameObject obj in rootObjects)
            {
                if (obj.name == "GameManager") selectedGM = obj;
                else if (obj.name == "Navmesh") navmeshHolder = obj;

                if (navmeshHolder != null && selectedGM != null) break;
            }

            if (navmeshHolder != null && selectedGM != null)
                NavmeshDataSet(selectedGM, navmeshHolder); // [Interflow fix 2026-08-01] был SCEditor.NavmeshDataSet
        }
    }
}