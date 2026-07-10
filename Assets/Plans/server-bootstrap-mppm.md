# Обзор проекта
- Название игры: Interflow (RTS/стратегия на базе StrategyCore)
- Высокоуровневая концепция: Сетевая стратегия на Unity Netcode for GameObjects; матч на 2 игроков через лобби (Host/Join).
- Игроки: Сетевой мультиплеер (2 игрока), тестирование через Multiplayer Play Mode (MPPM).
- Источники вдохновения / Референсные игры: —
- Тон / Художественное направление: —
- Целевая платформа: StandaloneWindows64
- Ориентация экрана / Разрешение: Альбомная (десктоп)
- Графический конвейер (Render Pipeline): Built-in

# Игровые механики
## Основной игровой цикл
Не меняется. Меняется только **способ запуска сетевой сессии** для удобного и детерминированного теста в MPPM.

## Управление и методы ввода
Не меняется. Добавляется автоматический выбор роли (Host/Client) по MPPM-тегам инстанса + ручная кнопка в меню как фолбэк.

# Интерфейс (UI)
Меню (`Assets/StrategyCore/UI/Menu.uxml`) не редактируется (правило «не трогать ассет»). Кнопка ручного запуска инжектится в рантайме в контейнер `Menu > MenuButtons`, рядом с `JoinGame`, в стиле `buttonColors menuButton`.

# Решение по архитектуре
**Модель: Host + Client (родной режим ассета).**
- `host` (player0) = сервер И игрок (слот 0) — именно так ассет и спроектирован (`GetClientSlot(0)`, `currentPlayer=0`).
- `client` (player1) = подключается к `127.0.0.1:7777`.
- Отдельный «чистый» сервер (`StartServer`) НЕ используется — он конфликтует со slot-логикой ассета и был причиной ошибки у player1.

**Запуск: автоматически по MPPM-тегам** (`Unity.Multiplayer.Playmode.CurrentPlayer.ReadOnlyTags()`), чтобы убрать ручные клики и гонку за порт 7777.

# Ключевые ассеты и контекст
- `Assets/StrategyCore/Scripts/ServerBootstrap.cs` — ПОЛНОСТЬЮ переписывается (роль теперь «AutoNetworkLauncher»).
- Используемые публичные API ассета (без правок ассета):
  - `NetworkConnectionHandler.instance.StartHost(string name)` — родной запуск хоста из лобби.
  - `NetworkConnectionHandler.instance.StartClient(string name, string address, string port)` — родной запуск клиента.
  - `SlotManager.instance.gameStarted` (`GameState.Menu`), `SlotManager.instance.slotType` + `SlotType.Player`, `SlotManager.instance.RandomizeSlotData()`.
  - `SceneHandler.instance.LoadScene()`.
  - `UIManagerMenu.instance.UIDocument` + UXML-имена `Menu/MenuButtons/JoinGame/PlayerName/IpInput/PortInput`.
- MPPM API (только в редакторе, работает внутри virtual players, т.к. это editor-инстансы):
  - `using Unity.Multiplayer.Playmode;`
  - `string[] tags = CurrentPlayer.ReadOnlyTags();`
  - Весь MPPM-код обернуть в `#if UNITY_EDITOR`.
- Теги, которые нужно проставить в **Window > Multiplayer Play Mode**:
  - Main Editor (player0): тег `host`
  - Virtual Player 2 (player1): тег `client`
  - (тег `server` со скриншота при этой модели не нужен)

# Шаги реализации

### Шаг 1 — Переписать ServerBootstrap.cs (роль: авто-лаунчер Host/Client)
- **Описание:** Заменить содержимое `Assets/StrategyCore/Scripts/ServerBootstrap.cs`:
  - Поля инспектора: `autoLaunchByTags` (bool, default true), `hostTag` ("host"), `clientTag` ("client"), `connectAddress` ("127.0.0.1"), `connectPort` ("7777"), `playerName` ("MPPM"), `requiredPlayers` (int, 2), `injectMenuButton` (bool, true), `menuButtonText` ("SERVER"→"AUTO HOST").
  - В `Start()`: дождаться готовности меню (`UIManagerMenu.instance.UIDocument != null` и `rootVisualElement` готов) через корутину; затем:
    - Прочитать MPPM-теги (`#if UNITY_EDITOR`).
    - Если тег == `hostTag` → `NetworkConnectionHandler.instance.StartHost(playerName)`.
    - Если тег == `clientTag` → `NetworkConnectionHandler.instance.StartClient(playerName, connectAddress, connectPort)`.
    - Если тегов нет / не editor → ничего не запускать, только инжектить кнопку (фолбэк).
  - Автостарт матча: подписка на `NetworkManager.Singleton.OnClientConnectedCallback`; только на сервере (`IsServer`), при `gameStarted==GameState.Menu` и `CountPlayerSlots()>=requiredPlayers` → `RandomizeSlotData()` + `SceneHandler.instance.LoadScene()` (повтор логики штатного `StartButton`). Гард `matchStarting`.
  - **Удалить** прямой `NetworkManager.StartServer()` и ручной `Instantiate(networkHandler)` — теперь это делает штатный `StartHost`.
  - `OnDestroy`: отписка от `OnClientConnectedCallback`.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** No

### Step 2 — Чинить инжект кнопки в меню (надёжный фолбэк)
- **Description:** В корутине инжекта:
  - Ждать не только `UIDocument != null`, но и непустой `rootVisualElement` + наличие `MenuButtons` (как сейчас), плюс защититься geometry-callback'ом, если контейнер ещё не построен.
  - Клонировать стиль существующей кнопки `JoinGame` (классы `buttonColors menuButton`, тот же inline-стиль ширины), чтобы кнопка гарантированно была видимой (текущая `Label` без width могла схлопнуться).
  - Идемпотентность: если `MenuButtons.Q("ServerGame") != null` — выходим.
  - По клику кнопки: запускать Host (`StartHost`) — для standalone-теста без MPPM.
- **Назначенная роль:** developer
- **Зависимости:** Шаг 1
- **Параллелизуемость:** Нет

### Шаг 3 — Проставить MPPM-теги и проверить Connection Approval
- **Описание:**
  - Открыть **Window > Multiplayer Play Mode**, задать тег `host` для Main Editor (player0) и `client` для Virtual Player 2 (player1).
  - Проверить, что на `ProjectManager > NetworkManager` включён **Connection Approval** (нужно, т.к. `NetworkConnectionHandler.ConnectionApproval` заполняет слоты — иначе `CountPlayerSlots()` не сработает).
  - (Опционально по согласованию) скриптом проставить теги через MPPM editor API.
- **Назначенная роль:** developer
- **Зависимости:** Шаг 1
- **Параллелизуемость:** Да (можно параллельно с Шагом 2)

### Шаг 4 — Обновить значения компонента ServerBootstrap в сцене
- **Описание:** В `Assets/StrategyCore/Scenes/Menu.unity` на `ProjectManager` обновить сериализованные поля под новый набор (`autoLaunchByTags=true`, `hostTag=host`, `clientTag=client`, `requiredPlayers=2`, и т.д.). Сохранить сцену.
- **Назначенная роль:** developer
- **Зависимости:** Шаг 1
- **Параллелизуемость:** Нет

# Проверка и тестирование
1. **Компиляция:** проект компилируется без ошибок; `#if UNITY_EDITOR` вокруг MPPM-кода не ломает плеер-сборку.
2. **MPPM-запуск:** запустить Play. Ожидание:
   - player0 (тег `host`) автоматически уходит в лобби как Host (без кликов).
   - player1 (тег `client`) автоматически коннектится к `127.0.0.1:7777` и появляется в списке игроков.
   - Как только в лобби 2 игрока — сервер сам грузит сцену и стартует матч.
   - В консоли player1 — нет ошибок про слот/`GetClientSlot`.
3. **Кнопка-фолбэк:** запустить обычный Play без MPPM-тегов → в меню видна кнопка `AUTO HOST`, по клику поднимается Host.
4. **Идемпотентность:** повторный вход в меню не плодит дубли кнопки.
5. **Граничный случай:** если второй клиент отвалился до старта — матч не стартует (счётчик < requiredPlayers).
