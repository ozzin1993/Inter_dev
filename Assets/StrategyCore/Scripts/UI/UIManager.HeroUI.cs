using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>
    /// Партиал UIManager: UI героя. Пока — кнопка призыва (над волной); ряд умений героя — отдельный шаг.
    /// Строится в C# (InGame.uxml не правится, правило 1). Призыв серверо-авторитетно (правило 6):
    /// хост зовёт MatchManager.SummonHero напрямую, клиент — NetworkDataSync.SummonHeroServerRpc.
    /// Дизейбл (штатный класс .locked) при живом герое: команда одна. Всё в Inspector (правило 3).
    /// </summary>
    public partial class UIManager : MonoBehaviour
    {
        [Header("Кнопка призыва героя")]
        [Tooltip("Иконка кнопки призыва героя.")]
        [SerializeField] Texture2D heroSummonIcon;

        [Tooltip("Точка привязки кнопки на экране. Над волной — обычно BottomRight (выше поднимает offset.Y).")]
        [SerializeField] ScreenAnchor heroSummonAnchor = ScreenAnchor.BottomRight;

        [Tooltip("Отступ кнопки от края экрана, px. Y задаёт высоту над панелью волны (подбери, чтобы встала над ней).")]
        [SerializeField] Vector2 heroSummonOffset = new Vector2(10f, 350f);

        [Tooltip("Размер кнопки призыва, px.")]
        [SerializeField] int heroSummonButtonSize = 64;

        VisualElement heroSummonButton;   // сама кнопка (для дизейбла/обновления)

        // Вызывается из InitCornerTables() (наш партиал), чтобы не править ядровой Start() (правило 1).
        void InitHeroSummonButton()
        {
            if (heroSummonButton != null) return;         // уже построено
            if (uiDocument == null) return;
            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;

            // Полноэкранный оверлей + flex-позиционирование (как панели-сетки) — клики мимо кнопки проходят.
            VisualElement overlay = new VisualElement { name = "HeroSummonOverlay" };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0; overlay.style.right = 0;
            overlay.style.top = 0; overlay.style.bottom = 0;
            overlay.pickingMode = PickingMode.Ignore;
            ApplyAnchor(overlay, heroSummonAnchor);       // переиспользуем позиционирование панелей-сеток

            GroupBox btn = new GroupBox();
            btn.AddToClassList("AbilityButton");
            btn.style.width = heroSummonButtonSize;
            btn.style.height = heroSummonButtonSize;
            btn.style.marginLeft = heroSummonOffset.x; btn.style.marginRight = heroSummonOffset.x;
            btn.style.marginTop = heroSummonOffset.y; btn.style.marginBottom = heroSummonOffset.y;

            GroupBox iconElement = new GroupBox();
            iconElement.AddToClassList("AbilityButtonIcon");
            if (heroSummonIcon != null) iconElement.style.backgroundImage = heroSummonIcon;
            btn.Add(iconElement);

            btn.RegisterCallback<ClickEvent>(OnHeroSummonClick);
            overlay.Add(btn);
            root.Add(overlay);
            heroSummonButton = btn;

            RefreshHeroSummonButton();
        }

        // Клик по кнопке призыва: серверо-авторитетно (хост — напрямую, клиент — через RPC).
        void OnHeroSummonClick(ClickEvent evt)
        {
            MatchManager mm = MatchManager.instance;
            if (mm == null) return;
            int team = CommandTeamForLocalPlayer();

            if (mm.HeroAlive(team)) return;               // герой уже жив — призыв недоступен (кнопка серая)

            if (NetworkConnectionHandler.isClient)
            {
                if (NetworkDataSync.instance != null) NetworkDataSync.instance.SummonHeroServerRpc(team);
            }
            else
            {
                mm.SummonHero(team);
            }
        }

        /// <summary>
        /// Обновить вид кнопки призыва: дизейбл (штатный .locked) при живом герое команды локального игрока.
        /// Вызывается по событию OnHeroChanged (хост) и при смене команды (LateUpdate).
        /// Примечание: у клиента состояние героя (heroUnit) не синхронизируется — дизейбл достоверен у хоста;
        /// у клиента кнопка остаётся активной, но повторный призыв отклоняет серверный guard (SummonHero).
        /// </summary>
        public void RefreshHeroSummonButton()
        {
            if (heroSummonButton == null) return;
            MatchManager mm = MatchManager.instance;
            bool disabled = mm != null && mm.HeroAlive(CommandTeamForLocalPlayer());
            if (disabled) heroSummonButton.AddToClassList("locked");
            else heroSummonButton.RemoveFromClassList("locked");
        }

        // Реакция на изменение героя (сервер): призван/погиб → обновить кнопку (и, позже, ряд умений героя).
        void OnHeroChangedHandler(int team, Unit hero)
        {
            if (team != CommandTeamForLocalPlayer()) return;
            RefreshHeroSummonButton();
            RefreshAbilitySlots();   // умения героя (нижний ряд) появляются/исчезают с призывом/смертью
        }
    }
}
