using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;

namespace StrategyCore
{
    [CustomEditor(typeof(Unit))]
    public class UnitEditor : Editor
    {
        private bool showHiddenProperties = false; // Toggle only for the editor
        private string[] hiddenProperties = { "icon", "horizontalPart", "verticalPart", "viewBlocker", "singleCellViewBlocker", "dieVFX", "attackEffectors", "launchSite", "launchVFX",
                                            "readySound", "moveSound", "clickSound", "deathSound", "attackCommandSound", "attackStartSound", "attackEndSound", "weaponSound", 
                                            "abilities", "isGround", "isWater", "isAir" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Disable the script field (usually the first property)
            SerializedProperty scriptProperty = serializedObject.FindProperty("m_Script");
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(scriptProperty);
            EditorGUI.EndDisabledGroup();

            // Create a styled toggle with a tooltip
            GUIStyle toggleStyle = new GUIStyle(EditorStyles.toggle)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                fixedHeight = 20
            };
            GUIContent toggleContent = new GUIContent("Show Non-Sync Properties", "Non-sync properties are not saved and synced over the network if modified in the inspector, you should change them only for prefabs via StrategyCore Editor. Changing them in inspector for debugging is acceptable, but they will not be saved or synced.");

            // Iterate through all properties
            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;

            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                // Skip the script field (since it's already drawn)
                if (property.name == "m_Script") continue;

                // Add properties
                if (showHiddenProperties || !hiddenProperties.Contains(property.name))
                {
                    EditorGUILayout.PropertyField(property, true);
                }

                if (property.name == "owner")
                {
                    // Add a button to toggle hidden properties
                    GUILayout.Space(10);
                    showHiddenProperties = EditorGUILayout.Toggle(toggleContent, showHiddenProperties, toggleStyle);
                    GUILayout.Space(10); // Add spacing after toggle
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
