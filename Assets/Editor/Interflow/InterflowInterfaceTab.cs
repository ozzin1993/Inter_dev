using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============================= INTERFLOW EDITOR — ВКЛАДКА «ИНТЕРФЕЙС» ==
    // Иконки и раскладка игрового HUD в одном месте: угловые кнопки (техи, ветки Душ, волна), окно настройки
    // волны и панели-сетки со своими ячейками. Поля живут на объекте ОТКРЫТОЙ СЦЕНЫ (UIManager) —
    // это не ассеты, поэтому без открытой сцены матча вкладка показывает подсказку (как вкладка «Матч»).
    // Правки — штатно через SerializedObject + Bind (Undo + пометка сцены dirty). Ассет/SCEditor не трогаем
    // (правило 1). Правило 5: та же точка входа, что и остальные вкладки редактора.
    public static class InterflowInterfaceTab
    {
        public static VisualElement CreateTabUI()
        {
            var root = new VisualElement { style = { marginTop = 4, marginLeft = 4, marginRight = 4 } };
            var scroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            root.Add(scroll);

            var ui = Object.FindObjectOfType<UIManager>();

            if (ui == null)
            {
                scroll.Add(new HelpBox(
                    "В открытой сцене нет UIManager. Открой сцену матча — иконки интерфейса " +
                    "задаются на объектах сцены, а не в ассетах.",
                    HelpBoxMessageType.Info));
                return root;
            }

            // ---------- UIManager: угловые кнопки и панели-сетки ----------
            if (ui != null)
            {
                var so = new SerializedObject(ui);
                var box = new VisualElement();

                box.Add(Title("Угловые кнопки (левый верх)"));
                box.Add(Hint("Иконка кнопки технологий и кнопки веток Душ, плюс размеры и отступы столбца кнопок. " +
                             "Пустая иконка — кнопка нарисуется без картинки, кликаться будет по-прежнему."));
                box.Add(Field(so, "techButtonIcon",         "Иконка кнопки технологий"));
                box.Add(Field(so, "branchButtonIcon",       "Иконка кнопки веток Душ (Нежить)"));
                box.Add(Field(so, "cornerButtonSize",       "Размер угловой кнопки, px"));
                box.Add(Field(so, "cornerTablesSideOffset", "Отступ от края экрана, px"));
                box.Add(Field(so, "cornerTablesTopOffset",  "Отступ сверху, px"));
                box.Add(Field(so, "cornerPanelGap",         "Зазор кнопка → таблица, px"));

                box.Add(Title("Окно настройки волны (правый нижний угол)"));
                box.Add(Hint("Кнопка открывает панель волны у правой кромки экрана. Иконки пометок стоят в ряду " +
                             "каждого доступного юнита: автопризыв, разовый призыв, снять пометку."));
                box.Add(Field(so, "waveButtonIcon",   "Иконка кнопки волны"));
                box.Add(Field(so, "waveAutoIcon",     "Иконка «Автопризыв»"));
                box.Add(Field(so, "waveOneShotIcon",  "Иконка «Разовый призыв»"));
                box.Add(Field(so, "waveClearIcon",    "Иконка «Снять пометку»"));
                box.Add(Field(so, "wavePanelWidth",   "Ширина панели волны, px"));
                box.Add(Field(so, "wavePanelCellSize", "Размер ячейки ряда, px"));

                box.Add(Title("Панели-сетки и их ячейки"));
                box.Add(Hint("Каждая панель — фиксированная сетка rows×cols в точке экрана; ячейки назначаются " +
                             "ПОЗИЦИОННО (индекс = ряд×cols + столбец), пустая ячейка рисуется на своём месте. " +
                             "Панель «Умения» наполняется в матче кодом (умения главного здания), её ячейки " +
                             "из Inspector не берутся — важны только имя, привязка и размер сетки."));
                box.Add(Field(so, "slotPanels", "Панели-сетки"));

                scroll.Add(box);
                box.Bind(so);   // сохранение + Undo + пометка сцены dirty
            }

            // ---------- Осиротевшие поля сцены ----------
            scroll.Add(Title("Мусор в сохранённой сцене"));
            scroll.Add(Hint("Когда поле убирают из кода, его значение остаётся лежать в файле сцены и сбивает с толку " +
                            "при чтении. Unity выбрасывает такие поля при пересохранении сцены: загружая сцену, она " +
                            "оставляет только те поля, что есть в коде сейчас. Кнопка ниже помечает открытую сцену " +
                            "изменённой и сохраняет её — этого достаточно."));
            scroll.Add(new Button(ResaveOpenScene) { text = "Пересохранить открытую сцену" });

            return root;
        }

        // Пересохранение открытой сцены: чистит поля, которых больше нет в коде.
        static void ResaveOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Interflow Editor",
                    "Открытая сцена не сохранена на диск — сохрани её обычным способом (File → Save As), потом повтори.",
                    "Понятно");
                return;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            bool ok = EditorSceneManager.SaveScene(scene);
            Debug.Log(ok
                ? $"[Interflow Editor] Сцена «{scene.name}» пересохранена — поля, которых больше нет в коде, убраны."
                : $"[Interflow Editor] Сцену «{scene.name}» сохранить не удалось.");
        }

        // ======================== МЕЛОЧИ UI ========================

        static Label Title(string text) => new Label(text)
        {
            style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10, marginBottom = 4 }
        };

        static Label Hint(string text) => new Label(text)
        {
            style =
            {
                whiteSpace = WhiteSpace.Normal, marginBottom = 6,
                color = new Color(0.72f, 0.74f, 0.78f)
            }
        };

        // PropertyField по имени сериализованного поля. Поля нет (переименовали/снесли) — молча пропускаем,
        // чтобы вкладка не падала на чужой сцене.
        static VisualElement Field(SerializedObject so, string prop, string label)
        {
            var p = so.FindProperty(prop);
            if (p == null) return new VisualElement();
            var pf = new PropertyField(p, label);
            pf.style.marginBottom = 2;
            return pf;
        }
    }
}
