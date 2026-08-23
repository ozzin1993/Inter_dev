using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>
    /// Партиал UIManager: таймер до следующей волны — строка сверху по центру игрового экрана.
    /// Строится в C# (InGame.uxml не правится, правило 1). Значение приходит извне: хост считает
    /// (MatchManager.WaveTimer.cs), клиент получает по сети (NetworkDataSync.WaveTimer.cs) — этот
    /// партиал ТОЛЬКО отображает. Оформление — в Inspector (правило 3).
    /// </summary>
    public partial class UIManager : MonoBehaviour
    {
        [Header("Таймер до волны")]
        [Tooltip("Цвет текста таймера до следующей волны.")]
        [SerializeField] Color waveTimerColor = Color.white;

        [Tooltip("Размер шрифта таймера, px.")]
        [SerializeField] int waveTimerFontSize = 28;

        [Tooltip("Отступ строки таймера от верхнего края экрана, px.")]
        [SerializeField] float waveTimerTopOffset = 12f;

        VisualElement waveTimerOverlay;   // полноэкранный оверлей (клики проходят сквозь)
        Label waveTimerLabel;             // строка отсчёта (создаётся один раз)

        // Вызывается из InitCornerTables() (наш партиал), чтобы не править ядровой Start() (правило 1).
        void InitWaveTimer()
        {
            if (waveTimerLabel != null) return;           // уже построено
            if (uiDocument == null) return;
            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;

            // Полноэкранный оверлей: строка прижата к верхней кромке по центру; клики мимо строки проходят.
            waveTimerOverlay = new VisualElement { name = "WaveTimerOverlay" };
            waveTimerOverlay.style.position = Position.Absolute;
            waveTimerOverlay.style.left = 0; waveTimerOverlay.style.right = 0;
            waveTimerOverlay.style.top = 0; waveTimerOverlay.style.bottom = 0;
            waveTimerOverlay.style.flexDirection = FlexDirection.Column;
            waveTimerOverlay.style.alignItems = Align.Center;             // центр по горизонтали
            waveTimerOverlay.style.justifyContent = Justify.FlexStart;    // прижать к верху
            waveTimerOverlay.pickingMode = PickingMode.Ignore;

            waveTimerLabel = new Label { name = "WaveTimer" };
            waveTimerLabel.pickingMode = PickingMode.Ignore;
            waveTimerLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            waveTimerLabel.style.color = waveTimerColor;
            waveTimerLabel.style.fontSize = waveTimerFontSize;
            waveTimerLabel.style.marginTop = waveTimerTopOffset;
            waveTimerLabel.style.display = DisplayStyle.None;             // до первого значения скрыт

            waveTimerOverlay.Add(waveTimerLabel);
            root.Add(waveTimerOverlay);
        }

        /// <summary>
        /// Показать/обновить отсчёт до следующей волны (в секундах). Зовут хост локально и клиент по RPC.
        /// seconds < 0 — спрятать. На выделенном сервере без UI (rootVisualElement нет) — тихо выходит.
        /// </summary>
        public void ShowWaveTimer(int seconds)
        {
            if (waveTimerLabel == null) InitWaveTimer();   // ленивое построение при раннем вызове
            if (waveTimerLabel == null) return;            // UI ещё/на сервере не готов

            UpdateWavePanelTimer(seconds);                 // тот же отсчёт в шапке окна настройки волны (UIManager.WavePanel.cs)

            if (seconds < 0)
            {
                waveTimerLabel.style.display = DisplayStyle.None;
                return;
            }
            waveTimerLabel.text = seconds.ToString();       // только секунды
            waveTimerLabel.style.display = DisplayStyle.Flex;
        }
    }
}
