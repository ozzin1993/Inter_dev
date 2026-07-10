using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>
    /// Клиентский UI матчмейкинга в меню (задача A2).
    ///
    /// Кнопка «Найти игру» объявлена в Menu.uxml (правка ассета по разрешению Artsiom 2026-07-05,
    /// маркер [Interflow fix] в UXML — реестр форк-долга concepts/asset-fork-debt). Этот компонент
    /// подключает к ней логику: клик → MatchmakingClient.FindMatch(имя игрока), показ статуса поиска
    /// и кнопки «Отмена». Транзиентные элементы (статус, «Отмена») создаются в рантайме — чтобы не
    /// расширять правку ассета сверх разрешённого (правила 2, 7).
    ///
    /// Чисто клиентский компонент — серверных вызовов нет (правило 6).
    /// Кладётся в сцену Menu на отдельный GameObject (рядом с MatchmakingClient).
    /// </summary>
    public class MatchmakingMenuUI : MonoBehaviour
    {
        [Header("Матчмейкинг")]
        [Tooltip("Компонент клиентского поиска игры. Перетащи сюда объект с MatchmakingClient (обычно этот же).")]
        [SerializeField] private MatchmakingClient matchmakingClient;

        [Header("Тексты")]
        [Tooltip("Текст кнопки отмены поиска.")]
        [SerializeField] private string cancelButtonText = "ОТМЕНА";

        [Header("Режим разработчика")]
        [Tooltip("Показывать элементы прямого подключения (HOST, IP/PORT, LOAD, чат лобби) — только для тестов. " +
                 "При false игрок видит лишь «Найти игру»; JOIN остаётся видимым, кнопкой SERVER управляет ServerBootstrap. " +
                 "По умолчанию true, чтобы уже настроенные сцены не меняли поведение до осознанного выключения.")]
        [SerializeField] private bool developerMode = true;

        // Найденные/созданные элементы меню (после готовности UIDocument).
        private VisualElement menuButtons;   // контейнер кнопок меню
        private VisualElement findButton;     // кнопка «Найти игру» из Menu.uxml
        private Label statusLabel;            // текст статуса поиска (создаётся в рантайме)
        private VisualElement cancelButton;   // кнопка «Отмена» (создаётся в рантайме)

        private bool wired;         // обработчики/подписки установлены
        private bool searchingUi;   // меню в режиме поиска (показаны статус + «Отмена»)

        private void Start()
        {
            StartCoroutine(WireWhenReady());
        }

        // Ждём готовности меню (UIManagerMenu строит UIDocument в своём Start), затем находим кнопку
        // «Найти игру» и подключаем логику. Тайминг/поиск контейнера — как в ServerBootstrap (§9 промта:
        // UI готов не сразу, имена элементов проверяем по факту).
        private IEnumerator WireWhenReady()
        {
            float waited = 0f;
            while (menuButtons == null)
            {
                yield return null;
                waited += Time.unscaledDeltaTime;
                if (UIManagerMenu.instance == null || UIManagerMenu.instance.UIDocument == null) continue;
                VisualElement root = UIManagerMenu.instance.UIDocument.rootVisualElement;
                if (root == null) continue;
                VisualElement menu = root.Q("Menu");
                menuButtons = menu != null ? menu.Q("MenuButtons") : null;
                if (menuButtons == null && waited > 5f)
                {
                    Debug.LogWarning("[MatchmakingMenuUI] Меню есть, но контейнер 'Menu/MenuButtons' не найден (>5с). " +
                                     "Проверь имена элементов в Menu.uxml.");
                    waited = 0f;
                }
            }

            Wire();
        }

        private void Wire()
        {
            if (wired) return;

            findButton = menuButtons.Q("FindGame");
            if (findButton == null)
            {
                Debug.LogError("[MatchmakingMenuUI] Кнопка 'FindGame' не найдена в меню (ожидалась в Menu.uxml). " +
                               "Логика поиска не подключена.");
                return;
            }
            if (matchmakingClient == null)
                Debug.LogError("[MatchmakingMenuUI] Не назначен MatchmakingClient (Inspector) — поиск не заработает, " +
                               "при клике будет сообщение об ошибке.");

            // Текст статуса поиска (скрыт по умолчанию) — Label в теме меню; ставим сразу после «Найти игру».
            // Идемпотентность (правило 8): если элемент уже есть (персистентный UIDocument) — переиспользуем.
            statusLabel = menuButtons.Q<Label>("FindStatus");
            if (statusLabel == null)
            {
                statusLabel = new Label(string.Empty) { name = "FindStatus" };
                statusLabel.AddToClassList("buttonColors");
                statusLabel.style.display = DisplayStyle.None;
                menuButtons.Insert(menuButtons.IndexOf(findButton) + 1, statusLabel);
            }

            // Кнопка «Отмена» (скрыта по умолчанию) — те же классы стилей, что у остальных кнопок меню.
            cancelButton = menuButtons.Q("FindCancel");
            if (cancelButton == null)
            {
                Label cancel = new Label(cancelButtonText) { name = "FindCancel" };
                cancel.AddToClassList("buttonColors");
                cancel.AddToClassList("menuButton");
                cancel.style.display = DisplayStyle.None;
                cancelButton = cancel;
                menuButtons.Insert(menuButtons.IndexOf(statusLabel) + 1, cancelButton);
            }

            findButton.RegisterCallback<ClickEvent>(OnFindClicked);
            cancelButton.RegisterCallback<ClickEvent>(OnCancelClicked);
            if (matchmakingClient != null) matchmakingClient.OnStatusChanged += OnStatusChanged;

            wired = true;

            // Режим разработчика (промт A2b): элементы прямого подключения нужны только для тестов.
            // При выключенном developerMode прячем их — игрок видит только «Найти игру». JOIN не прячем,
            // кнопку SERVER (инжект ServerBootstrap) отсюда не трогаем (решение Artsiom 2026-07-06).
            // Скрытие одноразовое: UIManagerMenu при переходах меню/лобби переключает контейнеры
            // (MenuButtons/Lobby), но display отдельных элементов внутри них не сбрасывает — скрытие держится.
            if (!developerMode) HideDeveloperElements();
        }

        // Клик по «Найти игру»: гейт повторных кликов по IsSearching (правило 8), имя игрока читаем
        // тем же путём, что штатный JoinButton, и запускаем поиск.
        private void OnFindClicked(ClickEvent evt)
        {
            if (matchmakingClient == null)
            {
                SetStatus("Ошибка: MatchmakingClient не назначен.");
                return;
            }
            if (matchmakingClient.IsSearching)
            {
                Debug.Log("[MatchmakingMenuUI] Поиск уже идёт — повторный клик проигнорирован.");
                return;
            }

            ApplySearchingUi(true);                       // «Поиск…» придёт событием OnStatusChanged
            matchmakingClient.FindMatch(ReadPlayerName());
        }

        // Клик по «Отмена»: отменяем поиск (если идёт) и возвращаем меню в исходное состояние.
        private void OnCancelClicked(ClickEvent evt)
        {
            if (matchmakingClient != null) matchmakingClient.CancelMatchmaking();
            ApplySearchingUi(false);
        }

        // Статус из MatchmakingClient (тексты уже человекочитаемые, русские) — отображаем, пока UI в режиме
        // поиска. После «Отмены» поздние события (напр. «Отменено») не всплывают поверх исходного меню.
        private void OnStatusChanged(string status)
        {
            if (!searchingUi) return;
            SetStatus(status);
        }

        // Имя игрока — тем же путём, что штатный JoinButton (UIManagerMenu.cs): поле PlayerName в Menu.
        private string ReadPlayerName()
        {
            TextField nameField = UIManagerMenu.instance.UIDocument.rootVisualElement.Q("Menu").Q("PlayerName") as TextField;
            return nameField != null ? nameField.value : string.Empty;
        }

        private void SetStatus(string text)
        {
            if (statusLabel == null) return;
            statusLabel.text = text;
            statusLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // Переключение вида: поиск идёт (спрятать «Найти игру», показать «Отмена») либо исходное состояние.
        private void ApplySearchingUi(bool searching)
        {
            searchingUi = searching;
            if (findButton != null) findButton.style.display = searching ? DisplayStyle.None : DisplayStyle.Flex;
            if (cancelButton != null) cancelButton.style.display = searching ? DisplayStyle.Flex : DisplayStyle.None;
            if (!searching) SetStatus(string.Empty);   // исходное: спрятать статус
        }

        // Скрытие элементов прямого подключения при developerMode=false (промт A2b).
        // Набор согласован с Artsiom 2026-07-06: HOST, IP/PORT (контейнер ClientInputs), LOAD и чат лобби.
        private void HideDeveloperElements()
        {
            HideElement(menuButtons.Q("HostGame"), "MenuButtons/HostGame");
            HideElement(menuButtons.Q("ClientInputs"), "MenuButtons/ClientInputs (IP/PORT)");
            HideElement(menuButtons.Q("Load"), "MenuButtons/Load");

            // Чат — в экране Lobby, не в MenuButtons: прячем контейнер целиком (ChatBox + MsgInput).
            VisualElement lobby = UIManagerMenu.instance != null && UIManagerMenu.instance.UIDocument != null
                ? UIManagerMenu.instance.UIDocument.rootVisualElement.Q("Lobby")
                : null;
            HideElement(lobby != null ? lobby.Q("Chat") : null, "Lobby/Chat");
        }

        // Прячем элемент через display=None. Если не найден — предупреждаем, ошибку не глушим (правила 8, 9).
        private void HideElement(VisualElement element, string path)
        {
            if (element == null)
            {
                Debug.LogWarning("[MatchmakingMenuUI] developerMode=false: элемент '" + path +
                                 "' не найден — не спрятан. Проверь имена в Menu.uxml.");
                return;
            }
            element.style.display = DisplayStyle.None;
        }

        private void OnDestroy()
        {
            // Отписки (правило 8): событие может прийти при выходе из сцены во время поиска.
            if (matchmakingClient != null) matchmakingClient.OnStatusChanged -= OnStatusChanged;
            if (findButton != null) findButton.UnregisterCallback<ClickEvent>(OnFindClicked);
            if (cancelButton != null) cancelButton.UnregisterCallback<ClickEvent>(OnCancelClicked);
        }
    }
}
