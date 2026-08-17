using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============================= РЕДАКТОР УМЕНИЙ — ОТДЕЛЬНОЕ ОКНО (решение Artsiom 2026-08-16) ==
    // Всё, что связано с умениями, живёт в этом окне: боевые умения (конструктор), пассивные умения,
    // производство и состояния (ассеты Effector). Вкладки — ТЕ ЖЕ фабрики, что раньше стояли в главном
    // окне Interflow Editor (правило 5: код вкладок общий, окно — только рамка).
    // «Проверка» — фильтр валидатора: гоняет ТОЛЬКО правила умений и состояний;
    // полный прогон по проекту остаётся во вкладке «Валидатор» главного окна.
    public class InterflowAbilityEditorWindow : EditorWindow
    {
        List<(string title, Func<VisualElement> createContent)> tabs;

        Toolbar toolbar;
        VisualElement content;
        readonly List<ToolbarToggle> tabToggles = new List<ToolbarToggle>();
        int activeTab = -1;

        // Рядом с Interflow Editor в меню Tools (решение Artsiom 2026-08-16): соседние приоритеты
        // держат оба пункта одной группой, а не в подменю Tools/Interflow с операциями сборки.
        [MenuItem("Tools/Редактор умений", false, 101)]
        public static void Open()
        {
            GetWindow<InterflowAbilityEditorWindow>("Редактор умений");
        }

        void CreateGUI()
        {
            tabs = new List<(string, Func<VisualElement>)>
            {
                ("Боевые умения",    InterflowSkillBuilderTab.CreateTabUI),
                ("Пассивные умения", InterflowPassivesTab.CreateTabUI),
                ("Производство",     InterflowProductionTab.CreateTabUI),
                ("Состояния",        InterflowAbilitiesTab.CreateTabUI),
                ("Проверка",         InterflowValidator.CreateAbilityScopeTabUI),
            };

            toolbar = new Toolbar();
            content = new VisualElement { style = { flexGrow = 1 } };

            var scroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            scroll.Add(content);

            rootVisualElement.Add(toolbar);
            rootVisualElement.Add(scroll);

            for (int i = 0; i < tabs.Count; i++)
            {
                int index = i; // замыкание на копию
                var toggle = new ToolbarToggle { text = tabs[i].title };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) SelectTab(index);
                    else if (activeTab == index) toggle.SetValueWithoutNotify(true); // активную не выключаем кликом
                });
                tabToggles.Add(toggle);
                toolbar.Add(toggle);
            }

            if (tabs.Count > 0) SelectTab(0);
        }

        void SelectTab(int index)
        {
            if (index == activeTab) { tabToggles[index].SetValueWithoutNotify(true); return; }

            activeTab = index;
            for (int i = 0; i < tabToggles.Count; i++)
                tabToggles[i].SetValueWithoutNotify(i == index);

            content.Clear();
            content.Add(tabs[index].createContent());
        }
    }
}
