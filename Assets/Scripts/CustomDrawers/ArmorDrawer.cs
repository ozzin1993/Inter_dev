using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StrategyCore
{
    public class ArmorTypeIDAttribute : PropertyAttribute { }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(ArmorTypeIDAttribute))]
    public class ArmorTypeIDDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            GUI.enabled = false;
            ArmorType thisObj = (ArmorType)property.serializedObject.targetObject;
            if (property.intValue == 0 || DupliacteID(property.intValue, thisObj))
            {
                int uniqueID = GetUniqueID(thisObj);
                if (uniqueID != -1) property.intValue = uniqueID;
            }
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true;
        }

        public int GetUniqueID(ArmorType thisObj)
        {
            string[] thisObjs = AssetDatabase.FindAssets("t:ArmorType", null);

            int newID;
            if (thisObj.id == 0) newID = Random.Range(1, 99999);
            else newID = thisObj.id;

            bool duplicate = true;
            while (duplicate)
            {
                duplicate = false;
                foreach (string o in thisObjs)
                {
                    string guid = AssetDatabase.GUIDToAssetPath(o);
                    ArmorType obj = (ArmorType)AssetDatabase.LoadAssetAtPath(guid, typeof(ArmorType));

                    if (newID == obj.id && obj != thisObj)
                    {
                        // Change only for duplicate
                        bool isThisDuplicate = Utils.GetTheDuplicate(obj.displayName, thisObj.displayName);

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

        public bool DupliacteID(int id, ArmorType thisObj)
        {
            string[] thisObjs = AssetDatabase.FindAssets("t:ArmorType", null);

            foreach (string o in thisObjs)
            {
                string guid = AssetDatabase.GUIDToAssetPath(o);
                ArmorType obj = (ArmorType)AssetDatabase.LoadAssetAtPath(guid, typeof(ArmorType));

                if (id == obj.id && obj != thisObj)
                {
                    return true;
                }
            }

            return false;
        }
    }
#endif
}