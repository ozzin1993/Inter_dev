using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StrategyCore
{
    public class ProjectileIDAttribute : PropertyAttribute { }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(ProjectileIDAttribute))]
    public class ProjectileIDDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            GUI.enabled = false;
            Projectile thisObj = (Projectile)property.serializedObject.targetObject;
            if (property.intValue == 0 || DupliacteID(property.intValue, thisObj))
            {
                int uniqueID = GetUniqueID(thisObj);
                if (uniqueID != -1) property.intValue = uniqueID;
            }
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true;
        }

        public int GetUniqueID(Projectile thisProj)
        {
            string[] thisObjs = AssetDatabase.FindAssets("t:GameObject", null);
            List<Projectile> projectiles = new List<Projectile>();

            foreach (string guid in thisObjs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                // Check if the GameObject has a Projectile component
                if (prefab != null)
                {
                    if (prefab.GetComponent<Projectile>() != null) projectiles.Add(prefab.GetComponent<Projectile>());
                }
            }

            int newID;
            if (thisProj.id == 0) newID = Random.Range(1, 99999);
            else newID = thisProj.id;

            bool duplicate = true;
            while (duplicate)
            {
                duplicate = false;

                foreach (Projectile proj in projectiles)
                {
                    if (newID == proj.id && proj != thisProj)
                    {
                        // Change only for duplicate
                        bool isThisDuplicate = Utils.GetTheDuplicate(proj.name, thisProj.name);

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

        public bool DupliacteID(int id, Projectile projectile)
        {
            string[] thisObjs = AssetDatabase.FindAssets("t:GameObject", null);
            List<Projectile> projectiles = new List<Projectile>();

            foreach (string guid in thisObjs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                // Check if the GameObject has a Projectile component
                if (prefab != null)
                {
                    if (prefab.GetComponent<Projectile>() != null) projectiles.Add(prefab.GetComponent<Projectile>());
                }
            }

            foreach (Projectile proj in projectiles)
            {
                if (id == proj.id && projectile != proj)
                {
                    return true;
                }
            }

            return false;
        }
    }
#endif
}