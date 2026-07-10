# Project Overview
- **Game Title:** Interflow (RTS/стратегия на базе StrategyCore)
- **High-Level Concept:** Сетевая стратегия на Unity Netcode for GameObjects (NGO); матч на 2 игроков через лобби (Host/Join), плюс режим выделенного сервера.
- **Players:** Сетевой мультиплеер (2 игрока), тест через MPPM / отдельные сборки.
- **Inspiration / Reference Games:** —
- **Tone / Art Direction:** —
- **Target Platform:** StandaloneWindows64
- **Screen Orientation / Resolution:** Альбомная (десктоп)
- **Render Pipeline:** Built-in

# Game Mechanics
## Core Gameplay Loop
Не меняется. Меняется только UI запуска: кнопка **SERVER** становится **настоящим элементом меню** (как HOST и JOIN), а не рантайм-инъекцией.

## Controls and Input Methods
Меню на UI Toolkit (`Menu.uxml` + `UIManagerMenu.cs`). Клик по `ClickEvent` для каждого пункта меню. Новый пункт SERVER регистрируется так же, как `HostGame`/`JoinGame`.

# UI
Контейнер `Menu > MainMenu > MenuButtons` (в `Assets/StrategyCore/UI/Menu.uxml`):
```
[PlayerName  (TextField)]
[HOST        (HostGame)]
[JOIN        (JoinGame)]
[IP / PORT   (ClientInputs)]
[SERVER      (ServerGame)]   <-- НОВЫЙ пункт, в стиле buttonColors menuButton
[LOAD        (Load)]
```
Кнопка SERVER — `ui:Label` с теми же классами (`buttonColors menuButton`) и инлайн-стилем (ширина 250px, отступ сверху 25px), что HOST/JOIN, поэтому визуально и поведенчески идентична. Размещение — после блока `ClientInputs` (IP/PORT) и перед `LOAD`, чтобы сгруппировать сетевые действия.

# Key Asset & Context
**Файлы, которые будут изменены (3):**

1. `Assets/StrategyCore/UI/Menu.uxml` — добавить `Label name="ServerGame"`.
2. `Assets/StrategyCore/Scripts/Menu/UIManagerMenu.cs` — поле + регистрация + обработчик.
3. `Assets/StrategyCore/Scripts/ServerBootstrap.cs` — статический вход `LaunchServer()`; выключить рантайм-инъекцию.

**Контекст из существующего кода:**
- `UIManagerMenu.HostButton` (строки 95–100): образец обработчика — `chatBox.Clear()` + вызов сетевого запуска.
- `UIManagerMenu.Start()` (строки 56–66): образец регистрации кнопок через `UIDocument.rootVisualElement.Q("Menu").Q("...")`.
- `ServerBootstrap.StartAsServer()` (строки 82–136): существующая серверная логика (NGO `StartServer` + спавн `networkHandler` + автостарт матча + ре-синк). Её НЕ трогаем — только добавляем статический вход.
- `ServerBootstrap.instance` (строка 40): приватный статический инстанс, доступный из `LaunchServer()`.
- Образец строки UXML для копирования — строка 9 (`JoinGame`).

**Важное замечание о поведении (риск).**
Кнопка по этому плану вызывает `StartAsServer()` → `NetworkManager.StartServer()` (чистый выделенный сервер без локального игрока). В соседнем плане `Assets/Plans/server-bootstrap-mppm.md` зафиксировано, что чистый `StartServer` конфликтует со slot-логикой ассета (`GetClientSlot(0)`/`currentPlayer=0`) и был причиной ошибки у второго клиента. Реализую строго по запросу (рецепт Claude), но это отмечено в разделе Verification — проверить подключение второго клиента и консоль на ошибки слотов.

# Implementation Steps

### Step 1 — Добавить пункт SERVER в Menu.uxml
- **Description:** В `Assets/StrategyCore/UI/Menu.uxml`, внутри `MenuButtons`, после закрывающего тега `</ui:VisualElement>` блока `ClientInputs` (после строки 13) и перед `Load` (строка 14) вставить новый Label:
  ```xml
  <ui:Label tabindex="-1" text="SERVER" parse-escape-sequences="true" display-tooltip-when-elided="true" name="ServerGame" class="buttonColors menuButton" style="border-top-width: 2px; border-right-width: 2px; border-bottom-width: 2px; border-left-width: 2px; font-size: 25px; padding-top: 10px; padding-right: 10px; padding-bottom: 10px; padding-left: 10px; -unity-text-align: upper-center; -unity-font-style: bold; width: 250px; margin-top: 25px;" />
  ```
  Стиль скопирован один-в-один со строки 9 (`JoinGame`), имя `ServerGame`, текст `SERVER`.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** Yes (можно параллельно со Step 3)

### Step 2 — Зарегистрировать и обработать кнопку в UIManagerMenu.cs
- **Description:** В `Assets/StrategyCore/Scripts/Menu/UIManagerMenu.cs`:
  1. В блоке `// Menu` (рядом со строками 18–22) добавить поле:
     ```csharp
     VisualElement serverButton;
     ```
  2. В `Start()` в секции `// ---------- Menu ----------` (после регистрации `joinButton`, строка 60) добавить:
     ```csharp
     serverButton = UIDocument.rootVisualElement.Q("Menu").Q("ServerGame");
     serverButton.RegisterCallback<ClickEvent>(ServerButton);
     ```
  3. В секции `// MENU ---` (рядом с `HostButton`/`JoinButton`, после строки 109) добавить обработчик:
     ```csharp
     void ServerButton(ClickEvent evt)
     {
         chatBox.Clear();
         ServerBootstrap.LaunchServer();
     }
     ```
- **Assigned role:** developer
- **Dependencies:** Step 1 (имя `ServerGame` должно существовать в UXML), Step 3 (метод `ServerBootstrap.LaunchServer` должен существовать)
- **Parallelizable:** No

### Step 3 — Добавить статический вход LaunchServer() в ServerBootstrap.cs
- **Description:** В `Assets/StrategyCore/Scripts/ServerBootstrap.cs`:
  1. Добавить публичный статический метод (рядом с `StartAsServer`, после строки 136):
     ```csharp
     /// <summary>Статический вход для меню: поднять выделенный сервер.</summary>
     public static void LaunchServer()
     {
         if (instance != null) instance.StartAsServer();
         else Debug.LogError("[ServerBootstrap] Нет инстанса ServerBootstrap в сцене.");
     }
     ```
  2. Поскольку SERVER теперь настоящая кнопка меню, рантайм-инъекция больше не нужна. Изменить дефолт поля `injectMenuButton` на `false` (строка 32):
     ```csharp
     [SerializeField] private bool injectMenuButton = false;
     ```
     Логика `InjectButtonWhenReady()` остаётся в коде как фолбэк, но по умолчанию не вызывается.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** Yes (можно параллельно со Step 1)

### Step 4 — Отключить рантайм-инъекцию на компоненте в сцене
- **Description:** В `Assets/StrategyCore/Scenes/Menu.unity` на GameObject `ProjectManager` (компонент `ServerBootstrap`) выставить сериализованное поле `injectMenuButton = false` (изменение дефолта в Step 3 НЕ перезапишет уже сохранённое в сцене значение `true`). Это предотвратит дубль кнопки (рантайм-инъекция + UXML-кнопка одновременно). Сохранить сцену.
- **Assigned role:** developer
- **Dependencies:** Step 3
- **Parallelizable:** No

# Verification & Testing
1. **Компиляция:** проект собирается без ошибок; нет конфликтов имён `ServerButton`/`serverButton`.
2. **Меню:** запустить Play в сцене `Menu.unity`. В главном меню виден пункт **SERVER** в стиле HOST/JOIN, между блоком IP/PORT и LOAD; ровно один экземпляр (нет дубля от рантайм-инъекции).
3. **Клик:** по клику на SERVER в консоли — лог `[ServerBootstrap] Сервер запущен (StartServer)...` и чат очищается.
4. **Нет инстанса:** если убрать `ServerBootstrap` со сцены — по клику в консоли ошибка `Нет инстанса ServerBootstrap в сцене.` (graceful, без креша).
5. **Граничный случай / риск слотов:** подключить второго клиента (Join к `127.0.0.1:7777`). Проверить консоль клиента на ошибки слота/`GetClientSlot` — это известный риск чистого `StartServer` (см. `server-bootstrap-mppm.md`). Если ошибки появятся — отдельным решением переключить кнопку на Host-модель или вернуться к авто-лаунчеру из соседнего плана.
6. **Регрессия HOST/JOIN:** убедиться, что штатные HOST и JOIN работают как раньше.
