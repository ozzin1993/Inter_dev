using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============================= INTERFLOW EDITOR — ОКНО (фаза E1) ==
    // Наш редактор контента (План_Interflow_Editor_и_Чистка.md §4). Ассет (SCEditor) не трогаем —
    // это ОТДЕЛЬНОЕ окно (правило 1). Одно окно с вкладками (правило 5): в E1 — «Валидатор» и
    // «Чистка — отчёт»; вкладка «Фракции» (E3) добавлена первой; «Юниты»/«Умения»/«Справочники» (E4)
    // добавятся позже регистрацией в BuildTabs(). UI — кодом на UI Toolkit (без UXML): нет зависимости от путей ассетов.
    public class InterflowEditorWindow : EditorWindow
    {
        // Реестр вкладок: заголовок + фабрика содержимого. Единственная точка расширения окна.
        List<(string title, Func<VisualElement> createContent)> tabs;

        Toolbar toolbar;                 // верхняя полоса с кнопками вкладок
        VisualElement content;           // контейнер содержимого активной вкладки
        readonly List<ToolbarToggle> tabToggles = new List<ToolbarToggle>();
        int activeTab = -1;

        [MenuItem("Tools/Interflow Editor")]
        public static void Open()
        {
            // Обычное dockable-окно; заголовок по-русски не даём — имя инструмента.
            GetWindow<InterflowEditorWindow>("Interflow Editor");
        }

        void CreateGUI()
        {
            BuildTabs();

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

        // Состав вкладок окна. E3/E4 добавляют свои строки сюда.
        void BuildTabs()
        {
            tabs = new List<(string, Func<VisualElement>)>
            {
                ("Фракции", InterflowFactionTab.CreateTabUI),
                ("Юниты", InterflowUnitsTab.CreateTabUI),
                ("Умения и эффекторы", InterflowAbilitiesTab.CreateTabUI),
                ("Справочники", InterflowCatalogsTab.CreateTabUI),
                ("Приоритет цели", InterflowTargetPriorityTab.CreateTabUI),
                ("Матч", InterflowMatchTab.CreateTabUI),
                ("Валидатор", InterflowValidator.CreateTabUI),
                ("Чистка — отчёт", InterflowUsageScanner.CreateTabUI),
            };
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
