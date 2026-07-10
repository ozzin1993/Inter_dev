using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============================= INTERFLOW EDITOR — ВКЛАДКА «ПРИОРИТЕТ ЦЕЛИ» ==
    // Правка per-team КОМАНДНОГО дефолта приоритета выбора цели атаки на MatchManager ОТКРЫТОЙ сцены
    // (через SerializedObject; ассет/SCEditor не трогаем, правило 1). Юнитовый приоритет (Unit.targetPriority)
    // правится на префабе юнита во вкладке «Юниты»/в Inspector. UI — кодом на UI Toolkit (как всё окно).
    public static class InterflowTargetPriorityTab
    {
        public static VisualElement CreateTabUI()
        {
            var root = new VisualElement { style = { paddingTop = 4, paddingLeft = 4, paddingRight = 4 } };

            root.Add(new HelpBox(
                "Командный дефолт приоритета выбора цели атаки (команды A/B) на MatchManager текущей сцены. " +
                "Упорядоченный список категорий: юнит без собственного Unit.targetPriority предпочитает ближайшего " +
                "из первой непустой категории. Пусто — штатный выбор (ближайший). Юнит со своим списком перебивает командный дефолт.",
                HelpBoxMessageType.Info));

            var mm = UnityEngine.Object.FindAnyObjectByType<MatchManager>();
            if (mm == null)
            {
                root.Add(new HelpBox(
                    "MatchManager не найден в открытой сцене. Откройте матч-сцену с MatchManager и снова выберите эту вкладку.",
                    HelpBoxMessageType.Warning));
                return root;
            }

            var so = new SerializedObject(mm);
            var propA = so.FindProperty("teamTargetPriorityA");
            var propB = so.FindProperty("teamTargetPriorityB");

            if (propA == null || propB == null)
            {
                root.Add(new HelpBox(
                    "На MatchManager нет полей приоритета (teamTargetPriorityA/teamTargetPriorityB). " +
                    "Проверьте, что MatchManager.TargetPriority.cs скомпилирован без ошибок.",
                    HelpBoxMessageType.Warning));
                return root;
            }

            root.Add(new PropertyField(propA, "Команда A — приоритет категорий"));
            root.Add(new PropertyField(propB, "Команда B — приоритет категорий"));

            // Двусторонняя привязка: правки применяются к MatchManager сцены (Undo + пометка сцены грязной).
            root.Bind(so);
            return root;
        }
    }
}
