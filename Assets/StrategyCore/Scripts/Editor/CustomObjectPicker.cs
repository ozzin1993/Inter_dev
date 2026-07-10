using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;
using System.Collections.Generic;
using System;

namespace StrategyCore
{
    public class CustomObjectPicker : EditorWindow
    {
        private VisualElement root;
        private ListView listView;
        private System.Action<GameObject> onObjectSelected;
        private static bool isVFXLine;

        public static void ShowPicker(System.Action<GameObject> onSelect, bool VFXLine)
        {
            isVFXLine = VFXLine;
            string windowName = (VFXLine) ? "Select a VFX Line" : "Select a Projectile";
            var window = GetWindow<CustomObjectPicker>(windowName);
            window.onObjectSelected = onSelect;
            window.Show();
        }

        private void OnEnable()
        {
            root = rootVisualElement;
            listView = new ListView();
            listView.style.flexGrow = 1;

            // Get assets from Resources folder
            string[] guids = AssetDatabase.FindAssets("t:GameObject", null);
            List<GameObject> validObjects = new List<GameObject>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                // Check if the GameObject has a Projectile component
                if (prefab != null)
                {
                    if (isVFXLine)
                    {
                        if (prefab.GetComponent<VFXLine>() != null) validObjects.Add(prefab);

                    }
                    else
                    {
                        if (prefab.GetComponent<Projectile>() != null) validObjects.Add(prefab);
                    }
                }
            }

            Func<VisualElement> makeItem = () => SCEditor.m_ListEntryTemplate.CloneTree();

            Action<VisualElement, int> bindItem = (e, i) =>
            {
                e.Q<Label>("ItemName").text = validObjects[i].name + ".asset";
                e.Q<Label>("ItemType").text = "";
                e.Q<Label>("ItemDescription").text = "";

                SCEditor.LoadPreview(e.Q<VisualElement>("ItemIcon"), validObjects[i].gameObject);
            };

            listView = new ListView(validObjects, SCEditor.m_ItemHeight, makeItem, bindItem);
            listView.selectionType = SelectionType.Single;
            listView.style.height = validObjects.Count * (SCEditor.m_ItemHeight + 5);

            // listView.itemsSource = validObjects;
            // listView.makeItem = () => new Label();
            // listView.bindItem = (element, index) =>
            // {
            //     (element as Label).text = validObjects[index].name;
            // };

            listView.selectionChanged += selection =>
            {
                if (selection.Count() > 0)
                {
                    onObjectSelected?.Invoke(selection.First() as GameObject);
                    Close();
                }
            };

            root.Add(listView);
        }
    }
}