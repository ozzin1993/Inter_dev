using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace StrategyCore
{
    public class SCChildPicker : EditorWindow
    {
        private GameObject parentObject;
        private VisualElement root;
        private ListView listView;
        private System.Action<GameObject> onObjectSelected;
        private Vector2 scrollPosition;

       // private static Unit refUnit;
       // private static bool horizontal;
       // private static Projectile refProjectile;
     

        public static void ShowPicker(System.Action<GameObject> onSelect, GameObject parent)
        {
            // parentObject = parent;
            // refUnit = unit;
            // horizontal = isHorizontal;
            // refProjectile = projectile;

            if (parent == null)
            {
                Debug.LogWarning("No parent object selected.");
                return;
            }

            SCChildPicker window = GetWindow<SCChildPicker>(true, "Select Child Object");
            window.parentObject = parent;
            window.minSize = new Vector2(300, 400);
            window.onObjectSelected = onSelect;
            window.ShowPopup();
        }

        private void OnGUI()
        {
            if (parentObject == null)
            {
                EditorGUILayout.LabelField("No Parent Object Selected", EditorStyles.boldLabel);
                return;
            }

            EditorGUILayout.LabelField("Select a Child Object:", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawChildObjects(parentObject.transform, 0);

            EditorGUILayout.EndScrollView();
        }

        private void DrawChildObjects(Transform parent, int depth)
        {
            foreach (Transform child in parent)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(depth * 20); // Indentation for hierarchy

                if (GUILayout.Button(child.name, EditorStyles.miniButton))
                {
                    onObjectSelected?.Invoke(child.gameObject);


                    // if (refUnit != null)
                    // {
                    //     if (horizontal) refUnit.horizontalPart = child;
                    //     else refUnit.verticalPart = child;
                    //     EditorUtility.SetDirty(refUnit);
                    // }
                    // else
                    // {
                    //     refProjectile.renderObject = child.gameObject;
                    //     EditorUtility.SetDirty(refProjectile);
                    // }

                    AssetDatabase.SaveAssets(); 
                    Close();
                }

                EditorGUILayout.EndHorizontal();

                // Recursively draw child objects
                if (child.childCount > 0)
                {
                    DrawChildObjects(child, depth + 1);
                }
            }
        }
    }
}
