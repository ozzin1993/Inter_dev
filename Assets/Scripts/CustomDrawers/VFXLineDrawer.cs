using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StrategyCore
{
    public class VFXLineIDAttribute : PropertyAttribute { }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(VFXLineIDAttribute))]
    public class VFXLineIDDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            GUI.enabled = false;
            VFXLine thisObj = (VFXLine)property.serializedObject.targetObject;
            if (property.intValue == 0 || DupliacteID(property.intValue, thisObj))
            {
                int uniqueID = GetUniqueID(thisObj);
                if (uniqueID != -1) property.intValue = uniqueID;
            }
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true;
        }

        public int GetUniqueID(VFXLine thisLine)
        {
            string[] thisObjs = AssetDatabase.FindAssets("t:GameObject", null);
            List<VFXLine> VFXLines = new List<VFXLine>();

            foreach (string guid in thisObjs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                // Check if the GameObject has a VFXLine component
                if (prefab != null)
                {
                    if (prefab.GetComponent<VFXLine>() != null) VFXLines.Add(prefab.GetComponent<VFXLine>());
                }
            }

            int newID;
            if (thisLine.id == 0) newID = Random.Range(1, 99999);
            else newID = thisLine.id;

            bool duplicate = true;
            while (duplicate)
            {
                duplicate = false;

                foreach (VFXLine line in VFXLines)
                {
                    if (newID == line.id && line != thisLine)
                    {
                        // Change only for duplicate
                        bool isThisDuplicate = Utils.GetTheDuplicate(line.name, thisLine.name);

                        if (isThisDuplicate)
                        {
                            newID = Random.Range(1, 99999);
                            duplicate = true;
                            break;
                        }
                        else
                        {
                            // End search, current object is original
                            return -1;
                        }
                    }
                }
            }

            return newID;
        }

        public bool DupliacteID(int id, VFXLine thisLine)
        {
            string[] thisObjs = AssetDatabase.FindAssets("t:GameObject", null);
            List<VFXLine> VFXLines = new List<VFXLine>();

            foreach (string guid in thisObjs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                // Check if the GameObject has a VFXLine component
                if (prefab != null)
                {
                    if (prefab.GetComponent<VFXLine>() != null) VFXLines.Add(prefab.GetComponent<VFXLine>());
                }
            }

            foreach (VFXLine line in VFXLines)
            {
                if (id == line.id && thisLine != line)
                {
                    return true;
                }
            }

            return false;
        }
    }
#endif
}