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
                    if (SCEditor.instance != null)
                    {
                        if (SCEditor.instance.m_activeCatIndex == SCEditorCats.Scenes)
                        {
                            SCEditor.instance.CloseSceneWithSave(SCEditor.instance.selectedScene);
                        }
                    }

                    AssignUniqueNetID();
                    NavmeshGenerate();
                    break;
            }
        }

        // Assigns to in-scene units` unique net ids
        public static void AssignUniqueNetID()
        {
            HashSet<UInt16> unitNetID = new HashSet<UInt16>(); // networkID hashset

            foreach (GameObject obj in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                Unit unit = obj.GetComponent<Unit>();
                if (unit != null)
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
                SCEditor.NavmeshDataSet(selectedGM, navmeshHolder);
        }
    }
}