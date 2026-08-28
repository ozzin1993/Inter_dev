using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============================= INTERFLOW EDITOR — ВКЛАДКА «МАТЧ» ==
    // Настройка темпа волн через окно Interflow Editor (правило 5: та же точка доступа к сцене, что и валидатор).
    // Поля живут на MatchManager (единый источник для игры И для таймера до волны, MatchManager.WaveTimer.cs);
    // вкладка — удобный доступ к ним из открытой сцены. Ассет/SCEditor не трогаем (правило 1). Правки — штатно
    // через SerializedObject + Bind (Undo + пометка сцены dirty). Согласованность значений проверяет вкладка
    // «Валидатор» (InterflowValidator.ValidateMatchScene).
    public static class InterflowMatchTab
    {
        public static VisualElement CreateTabUI()
        {
            var root = new VisualElement { style = { marginTop = 4, marginLeft = 4, marginRight = 4 } };

            var mm = Object.FindObjectOfType<MatchManager>();
            if (mm == null)
            {
                root.Add(new HelpBox(
                    "В открытой сцене нет MatchManager. Открой сцену матча — темп волн задаётся на объекте MatchManager.",
                    HelpBoxMessageType.Info));
                return root;
            }

            var so = new SerializedObject(mm);

            root.Add(new Label("Темп волн") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } });
            root.Add(new Label("Объект MatchManager текущей сцены. Значения — единый источник и для волн, и для таймера сверху экрана.")
                { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 6 } });

            root.Add(Field(so, "firstWaveDelay", "Задержка первой волны, сек"));
            root.Add(Field(so, "waveInterval",   "Интервал между волнами, сек"));
            root.Add(Field(so, "warnSeconds",    "Окно предупреждения до волны, сек"));
            root.Add(Field(so, "lockSeconds",    "Окно блокировки состава до волны, сек"));

            root.Add(new HelpBox(
                "Согласованность: интервал ≥ окна предупреждения, а предупреждение > блокировки. Иначе окно не " +
                "помещается в интервал и темп искажается — подробности на вкладке «Валидатор».",
                HelpBoxMessageType.None));

            root.Add(new Label("Опыт главного здания") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 12, marginBottom = 4 } });
            root.Add(new Label("Уровень замка набирается опытом и открывает очередной тир дерева технологий. " +
                               "Покупка узла тира уровень не поднимает. Предел уровня — число тиров фракции.")
                { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 6 } });

            root.Add(Field(so, "startMainBuildingLevel",          "Стартовый уровень замка"));
            root.Add(Field(so, "experiencePerLevel",              "Опыта на один уровень"));
            root.Add(Field(so, "centreHoldExperience",            "Опыт за владение центром (за период)"));
            root.Add(Field(so, "centreHoldPeriod",                "Период начисления за центр, сек"));
            root.Add(Field(so, "attackStanceExperiencePerSecond", "Опыт за секунду в режиме «Атака»"));

            root.Add(new HelpBox(
                "Опыт за убийство и за уничтоженную башню берётся из поля «xpReward» самой жертвы (вкладка " +
                "«Юниты»), а не отсюда — это то же число, которым кормится уровень героя. Нули порога и периода " +
                "проверяет вкладка «Валидатор».",
                HelpBoxMessageType.None));

            root.Bind(so);   // сохранение + Undo + пометка сцены dirty
            return root;
        }

        // PropertyField по приватному [SerializeField] с русской подписью; тултип подтянется из [Tooltip] поля.
        static PropertyField Field(SerializedObject so, string prop, string label)
        {
            var p = so.FindProperty(prop);
            var pf = new PropertyField(p, label);
            pf.style.marginBottom = 2;
            return pf;
        }
    }
}
