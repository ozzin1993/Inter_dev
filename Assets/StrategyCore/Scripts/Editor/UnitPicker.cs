using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;
using System.Collections.Generic;
using System;

namespace StrategyCore
{
    public class UnitPicker : EditorWindow
    {
        private VisualElement root;
        private ListView listView;
        private System.Action<Unit> onObjectSelected;

        public static void ShowPicker(System.Action<Unit> onSelect)
        {
            string windowName = "Select a unit";
            var window = GetWindow<UnitPicker>(windowName);
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
            List<Unit> validObjects = new List<Unit>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                // Check if the GameObject has a Projectile component
                if (prefab != null)
                {
                    Unit unit = prefab.GetComponent<Unit>();
                    if (unit != null) validObjects.Add(unit);
                }
            }

            Func<VisualElement> makeItem = () => SCEditor.m_ListEntryTemplate.CloneTree();

            Action<VisualElement, int> bindItem = (e, i) =>
            {
                e.Q<Label>("ItemName").text = validObjects[i].unitName;
                e.Q<Label>("ItemType").text = validObjects[i].unitType.ToString();
                e.Q<Label>("ItemDescription").text = validObjects[i].name + ".asset";

                if (validObjects[i].icon) e.Q<VisualElement>("ItemIcon").style.backgroundImage = validObjects[i].icon;
                else SCEditor.LoadPreview(e.Q<VisualElement>("ItemIcon"), validObjects[i].gameObject);
            };

            listView = new ListView(validObjects, SCEditor.m_ItemHeight, makeItem, bindItem);
            listView.selectionType = SelectionType.Single;
            listView.style.height = validObjects.Count * (SCEditor.m_ItemHeight + 5);

           // listView.itemsSource = validObjects;
           // listView.makeItem = () => new Label();
           // listView.bindItem = (element, index) =>
           // {
           //     (element as Label).text = validObjects[index].unitName;
           // };

            listView.selectionChanged += selection =>
            {
                if (selection.Count() > 0)
                {
                    onObjectSelected?.Invoke(selection.First() as Unit);
                    Close();
                }
            };

            root.Add(listView);
        }
    }
}