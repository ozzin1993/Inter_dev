using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>
    /// Партиал UIManager: уровень главного здания и накопленный опыт — строка сверху по центру, под таймером
    /// волны. Строится в C# (InGame.uxml не правится, правило 1). Значение приходит извне: сервер считает
    /// (MatchManager.Experience.cs), клиент получает по сети (NetworkDataSync.TechUpgrade.cs) — этот партиал
    /// ТОЛЬКО отображает. Оформление — в Inspector (правило 3). Образец — UIManager.WaveTimer.cs.
    ///
    /// ВИД ВРЕМЕННЫЙ: раскладка и оформление элемента не заданы (решение Artsiom 2026-08-28 — «вид позже»),
    /// поэтому здесь минимальный рабочий вариант: одна строка текста.
    /// </summary>
    public partial class UIManager : MonoBehaviour
    {
        [Header("Уровень и опыт главного здания")]
        [Tooltip("Цвет текста строки уровня и опыта главного здания.")]
        [SerializeField] Color mainBuildingProgressColor = Color.white;

        [Tooltip("Размер шрифта строки уровня и опыта, px.")]
        [SerializeField] int mainBuildingProgressFontSize = 20;

        [Tooltip("Отступ строки уровня и опыта от верхнего края экрана, px. Держать ниже таймера волны, " +
                 "иначе строки наложатся друг на друга.")]
        [SerializeField] float mainBuildingProgressTopOffset = 48f;

        VisualElement mainBuildingProgressOverlay;   // полноэкранный оверлей (клики проходят сквозь)
        Label mainBuildingProgressLabel;             // строка прогресса (создаётся один раз)

        // Ленивое построение при первом показе — как у таймера волны, чтобы не править чужой Start (правило 1).
        void InitMainBuildingProgress()
        {
            if (mainBuildingProgressLabel != null) return;           // уже построено
            if (uiDocument == null) return;
            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;

            // Полноэкранный оверлей: строка прижата к верхней кромке по центру; клики мимо строки проходят.
            mainBuildingProgressOverlay = new VisualElement { name = "MainBuildingProgressOverlay" };
            mainBuildingProgressOverlay.style.position = Position.Absolute;
            mainBuildingProgressOverlay.style.left = 0; mainBuildingProgressOverlay.style.right = 0;
            mainBuildingProgressOverlay.style.top = 0; mainBuildingProgressOverlay.style.bottom = 0;
            mainBuildingProgressOverlay.style.flexDirection = FlexDirection.Column;
            mainBuildingProgressOverlay.style.alignItems = Align.Center;             // центр по горизонтали
            mainBuildingProgressOverlay.style.justifyContent = Justify.FlexStart;    // прижать к верху
            mainBuildingProgressOverlay.pickingMode = PickingMode.Ignore;

            mainBuildingProgressLabel = new Label { name = "MainBuildingProgress" };
            mainBuildingProgressLabel.pickingMode = PickingMode.Ignore;
            mainBuildingProgressLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            mainBuildingProgressLabel.style.color = mainBuildingProgressColor;
            mainBuildingProgressLabel.style.fontSize = mainBuildingProgressFontSize;
            mainBuildingProgressLabel.style.marginTop = mainBuildingProgressTopOffset;
            mainBuildingProgressLabel.style.display = DisplayStyle.None;             // до первого значения скрыт

            mainBuildingProgressOverlay.Add(mainBuildingProgressLabel);
            root.Add(mainBuildingProgressOverlay);
        }

        /// <summary>
        /// Показать уровень главного здания и накопленный опыт. Зовут сервер локально и клиент по сети через
        /// фасад Presentation.UI. Показывается только СВОЯ команда — режим и прогресс чужой игроку не нужны.
        /// На выделенном сервере без интерфейса (rootVisualElement нет) — тихо выходит.
        /// </summary>
        public void ShowMainBuildingProgress(int teamIndex, int level, int experience, int experiencePerLevel)
        {
            if (teamIndex != CommandTeamForLocalPlayer()) return;    // прогресс чужой команды не показываем
            if (mainBuildingProgressLabel == null) InitMainBuildingProgress();
            if (mainBuildingProgressLabel == null) return;           // интерфейс ещё/на сервере не готов

            mainBuildingProgressLabel.text = experiencePerLevel > 0
                ? $"Замок: уровень {level} · опыт {experience}/{experiencePerLevel}"
                : $"Замок: уровень {level} · опыт {experience}";
            mainBuildingProgressLabel.style.display = DisplayStyle.Flex;
        }
    }
}
