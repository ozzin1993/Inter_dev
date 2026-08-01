using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StrategyCore
{
    public class EffectorIDAttribute : PropertyAttribute { }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(EffectorIDAttribute))]
    public class EffectorIDDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            GUI.enabled = false;
            Effector thisObj = (Effector)property.serializedObject.targetObject;
            if (property.intValue == 0 || DupliacteID(property.intValue, thisObj))
            {
                int uniqueID = GetUniqueID(thisObj);
                if (uniqueID != -1) property.intValue = uniqueID;
            }
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true;
        }

        public int GetUniqueID(Effector thisObj)
        {
            string[] thisObjs = AssetDatabase.FindAssets("t:Effector", null);

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
                    Effector obj = (Effector)AssetDatabase.LoadAssetAtPath(guid, typeof(Effector));

                    if (newID == obj.id && obj != thisObj)
                    {
                        // Change only for duplicate
                        bool isThisDuplicate = Utils.GetTheDuplicate(obj.name, thisObj.name);

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

        public bool DupliacteID(int id, Effector thisObj)
        {
            string[] thisObjs = AssetDatabase.FindAssets("t:Effector", null);

            foreach (string o in thisObjs)
            {
                string guid = AssetDatabase.GUIDToAssetPath(o);
                Effector obj = (Effector)AssetDatabase.LoadAssetAtPath(guid, typeof(Effector));

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