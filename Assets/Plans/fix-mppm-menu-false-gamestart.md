# Project Overview

- **Game Title:** Interflow (StrategyCore)
- **High-Level Concept:** Сетевая RTS/стратегия на Unity Netcode for GameObjects (NGO) с лобби, выбором команд/фракций/точек спавна и загрузкой игровой сцены.
- **Players:** Networking multiplayer (host + clients), тестируется через Multiplayer Play Mode (MPPM).
- **Inspiration / Reference Games:** Классические RTS с лобби (Warcraft III-подобная схема слотов/команд).
- **Tone / Art Direction:** N/A для данной задачи.
- **Target Platform:** StandaloneWindows64.
- **Screen Orientation / Resolution:** N/A (Landscape, десктоп).
- **Render Pipeline:** Built-in (по настройкам проекта; URP установлен, но не активен).

# Проблема (Bug Report)

При входе в Play открываются 2 окна (main editor + virtual player MPPM), у обоих показано меню.
**Ожидаемое поведение:** каждое окно независимо показывает своё лобби ТОЛЬКО после клика своей кнопки (нажал Host в окне 1 → лобби открывается в окне 1; нажал Join в окне 2 → лобби в окне 2). Старт игры — только по кнопке Start в лобби хоста.

**Фактическое поведение:** при клике Host/Client в одном окне:
- в этом окне «автоматически запускается игра» (меню скрывается, состояние переходит в Started);
- во втором окне пропадает меню и остаётся пустая сцена без UI.

# Анализ первопричины (Root Cause)

Подтверждённые факты (из чтения кода и инспекции сцены):
- `NetworkManager` (`EnableSceneManagement = True`, `ConnectionApproval = True`) и ВСЕ менеджеры (`SlotManager`, `SceneHandler`, `NetworkConnectionHandler`, `UIManagerMenu`, `UIDocument`, `UnityTransport`) висят на одном объекте `ProjectManager` **внутри сцены `Menu`**.
- `SlotManager.gameStarted` сериализован как `Menu` (т.е. стартуем из лобби — это корректный путь).
- В `NetworkConnectionHandler.StartHost()` при старте из лобби вызывается:
  - `NetworkManager.Singleton.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive)`
  - `NetworkManager.Singleton.SceneManager.ActiveSceneSynchronizationEnabled = true`
- `VerifySceneBeforeLoading` нигде не задан → **сцена `Menu` (активная, где живёт NetworkManager) попадает в набор синхронизируемых сетевых сцен**.

Механизм бага:
1. Когда клиент подключается, NGO синхронизирует ему активную сцену `Menu`, что генерирует событие сцены.
2. `SceneHandler.SceneManager_OnSceneEvent` (зарегистрирован в Start Host/Client) на `SceneEventType.LoadEventCompleted` **безусловно** вызывает `StartTheGame(true)`:
   ```csharp
   else if (sceneEvent.SceneEventType == SceneEventType.LoadEventCompleted)
   {
       // Everyone finished loading
       StartTheGame(true);   // ← НЕТ проверки, что загружена именно игровая сцена
   }
   ```
3. `StartTheGame()` выполняет `HideUIDocument()` (меню пропадает), `SetGameState(GameState.Started)` и `GameManager.GameStart()` (ложный «старт игры»), хотя игровая сцена из `sceneData` не загружалась.

Итог: синхронизация служебной сцены `Menu` ошибочно трактуется как «все загрузили игровую сцену».

Доп. наблюдение (вторичный риск, не первопричина): в Build Settings много дубликатов `New EmptyScene 1.unity` и нет некоторых игровых сцен — на текущий баг не влияет, но стоит зафиксировать как замечание.

# Стратегия исправления

Двухэтапно, по запросу пользователя: **сначала диагностика (подтверждение), затем точечный фикс.**

Точечный фикс состоит из двух взаимодополняющих защит:
- **A. Исключить служебные сцены из сетевой синхронизации** через `NetworkManager.Singleton.SceneManager.VerifySceneBeforeLoading`, чтобы сцена `Menu` не синхронизировалась клиентам.
- **B. Сделать обработчик событий сцены строгим** — реагировать на `Load`/`LoadComplete`/`LoadEventCompleted` только для игровой сцены из `sceneData[sceneIndex].sceneName`, игнорируя любые другие (включая `Menu`).

Защита B критична сама по себе (даже если A по какой-то причине не отфильтрует), а A устраняет лишний сетевой трафик/побочные эффекты синхронизации меню.

# Key Asset & Context

Файлы, затрагиваемые планом:
- `Assets/StrategyCore/Scripts/Menu/SceneHandler.cs`
  - `SceneManager_OnSceneEvent(SceneEvent)` — добавить фильтрацию по имени игровой сцены.
  - Возможно добавить приватный метод `VerifyScene(int sceneIndex, string sceneName)` и регистрацию `VerifySceneBeforeLoading`.
- `Assets/StrategyCore/Scripts/Network/NetworkConnectionHandler.cs`
  - В `StartHost()`/`StartClient()` — назначить `NetworkManager.Singleton.SceneManager.VerifySceneBeforeLoading` (после `StartHost/StartClient`, когда `SceneManager` доступен).

Ключевые сигнатуры (для справки):
- `public delegate bool VerifySceneBeforeLoadingDelegateHandler(int sceneIndex, string sceneName, LoadSceneMode loadSceneMode);`
- `NetworkSceneManager.VerifySceneBeforeLoading = (int idx, string name, LoadSceneMode mode) => { ... };`
- Целевое имя игровой сцены: `SceneHandler.instance.sceneData[SceneHandler.instance.sceneIndex].sceneName`.

# Implementation Steps

## Step 1 — Диагностика: подтвердить, что StartTheGame вызывается от синхронизации сцены Menu
- **Description:** Временно добавить подробные `Debug.Log` в `SceneHandler.SceneManager_OnSceneEvent` (логировать `SceneEventType`, `sceneEvent.SceneName`/`Scene.name`, `ClientId`, `LocalClientId`) и в начало `StartTheGame` (логировать стек вызова и текущее `gameStarted`, `sceneData[sceneIndex].sceneName`). Запустить Play с MPPM, нажать Host в одном окне, Join в другом, собрать логи Console через инструмент чтения логов.
- **Цель проверки:** Увидеть в логах, что `LoadEventCompleted` приходит для сцены `Menu` и приводит к `StartTheGame`, у обоих/одного окна — это финально подтверждает первопричину.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** No

## Step 2 — Защита B: строгая фильтрация событий сцены по имени игровой сцены
- **Description:** В `SceneManager_OnSceneEvent` (`SceneHandler.cs`) для веток `Load`, `LoadComplete`, `LoadEventCompleted` добавить условие, что `sceneEvent.SceneName` (или `sceneEvent.Scene.name`) совпадает с `sceneData[sceneIndex].sceneName`. Любые события для других сцен (в т.ч. `Menu`) — игнорировать. Это предотвращает ложный вызов `StartTheGame`.
- **Цель проверки:** После фикса синхронизация `Menu` больше не триггерит `StartTheGame`; меню/лобби остаются на месте у второго окна.
- **Assigned role:** developer
- **Dependencies:** Step 1
- **Parallelizable:** No

## Step 3 — Защита A: исключить служебные сцены из сетевой синхронизации
- **Description:** Назначить `NetworkManager.Singleton.SceneManager.VerifySceneBeforeLoading` так, чтобы синхронизировались только игровые сцены (имя совпадает с одной из `sceneData[*].sceneName`), а `Menu` и прочие служебные — нет. Регистрацию выполнить в `NetworkConnectionHandler.StartHost()` и `StartClient()` сразу после успешного `StartHost/StartClient` (там же, где сейчас регистрируется `OnSceneEvent`). Учесть, что `SetClientSynchronizationMode`/`ActiveSceneSynchronizationEnabled` уже выставлены — проверить совместимость с `ActiveSceneSynchronizationEnabled = true` (возможно, потребуется выключить авто-синхронизацию активной сцены, если она настойчиво тянет `Menu`).
- **Цель проверки:** Клиент при подключении не получает сцену `Menu`; в логах нет событий загрузки `Menu`.
- **Assigned role:** developer
- **Dependencies:** Step 1
- **Parallelizable:** No (изменяет связанную сетевую логику; делать после Step 2)

## Step 4 — Убрать диагностические логи
- **Description:** Удалить/закомментировать временные `Debug.Log` из Step 1, оставив код чистым.
- **Assigned role:** developer
- **Dependencies:** Step 2, Step 3
- **Parallelizable:** No

## Step 5 (опционально) — Привести в порядок Build Settings
- **Description:** Зафиксировать в отчёте дубликаты `New EmptyScene 1.unity` и отсутствие нужных игровых сцен в Build Settings. По согласованию с пользователем — удалить дубликаты и добавить актуальные игровые сцены. На основной баг не влияет, поэтому вынесено отдельно.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** Yes

# Verification & Testing

Ручная проверка (главный сценарий, через MPPM):
1. Нажать Play → открываются 2 окна, в обоих показано меню.
2. В окне 1 нажать **Host** → **только в окне 1** открывается лобби. Окно 2 продолжает показывать меню (НЕ пустую сцену).
3. В окне 2 нажать **Join** → **в окне 2** открывается лобби; в окне 1 (хост) список игроков обновляется и показывает подключившегося.
4. Игра НЕ стартует автоматически — только по кнопке **Start** в лобби хоста.
5. По нажатию **Start** в окне хоста — у всех корректно грузится игровая сцена из `sceneData`, меню скрывается, `gameStarted = Started`.

Логи/edge cases:
- В Console нет событий загрузки сцены `Menu` через NGO (после Step 3).
- `StartTheGame` вызывается ровно один раз и только при загрузке настоящей игровой сцены (проверить логом в Step 1 до удаления).
- Disconnect клиента из лобби возвращает его в меню (`ShowMenuLobby(0)`) без побочных эффектов.
- Повторный Host после Shutdown работает (нет «залипшего» состояния).

# Notes / Risks
- Изменения только в C# (`SceneHandler.cs`, `NetworkConnectionHandler.cs`); сцены/префабы не перестраиваются.
- Если `ActiveSceneSynchronizationEnabled = true` продолжит тянуть активную сцену `Menu` даже с `VerifySceneBeforeLoading`, рассмотреть отключение `ActiveSceneSynchronizationEnabled` для лобби-фазы (защита B всё равно предотвратит ложный старт).
- Долгосрочно рекомендуется вынести `NetworkManager` и менеджеры в отдельную bootstrap-сцену, которая никогда не синхронизируется (вне рамок этого фикса).
