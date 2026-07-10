# Подход Б (серверный режим) — подготовка к реализации

Делает 4 клиентских синглтона `dedicated-server-aware`: `instance` ставится как обычно, но инициализация графики/ввода/UI/звука и все серверо-достижимые методы — под единым гейтом «headless-сервер» → no-op / safe-default. Ядро симуляции не трогаем. Правки **только** в этих 4 файлах ассета (+1 место под общий флаг) → **требует разрешения на правку ассета (правило 1)**, маркер `// [Interflow fix YYYY-MM-DD]`.

Все факты ниже сверены с кодом 2026-06-20.

---

## ✅ Реализовано (2026-06-20)

Подход Б применён по выбору пользователя. Флаг `ServerBootstrap.IsHeadlessServer => Application.isBatchMode` + **42 гейта** `if (ServerBootstrap.IsHeadlessServer) return;` (маркер `// [Interflow fix 2026-06-20]`):
- **SoundFXManager — 9** (все Play/Loop-методы; Loop → `return null`).
- **PlayerControl — 8** (`OnEnable`/`OnDisable`/`Start`/`Update` + `AddToSelection`/`RemoveFromSelection`/`IsSelected`→`false`/`Reset`).
- **Camera_TopDown — 4** (`OnEnable`/`Start`/`Update`/`OnDisable`).
- **UIManager — 21** (`Start`/`Update` + 19 серверо-достижимых методов).

Особый случай — `UIManager.ShowNotifyMsg(msg, player, calledByServer)`: гейт поставлен **после** релая `GameMsgSend` (перед UI-строками), чтобы сервер продолжал слать сообщения клиентам. `Awake` всех 4 классов не тронут (instance ставится). `ComputeFormation` оставлен настоящим (на сервере не зовётся).

Проверка: каждая правка применена через Edit (точное совпадение old_string → гарантия вставки). Bash-mount при верификации отставал от file-инструментов (известный рассинхрон, см. wiki) и недосчитывал маркеры к концу файлов — источник истины Read/Edit.

**НЕ сделано (вне правок кода):** компиляция в Unity, dedicated-сборка, прогон. Перед тестом — проверить `ownerPlayer` 0/1 в Inspector (Шаг 2).

### ✅ `UIManagerMenu` закрыт (2026-06-20)
По разрешению — 6 гейтов в `UIManagerMenu` (5-й клиентский синглтон): `Start` + `HideUIDocument`/`ShowUIDocument`/`ShowMenuLobby`/`FillPlayerList`/`AddChatServerMsg`. `Awake` (instance) не тронут. Теперь call-site в SceneHandler/NCH/SlotManager безопасны: `SceneHandler.StartTheGame:133 HideUIDocument()` → no-op → `OnGameStart.Invoke()` отрабатывает → ре-синк проходит.

### ✅ `FloatingText.Spawn` закрыт (sweep 2026-06-20)
Был блокер: `FloatingText.Spawn` (UI/FloatingText.cs:32) делает релай `FloatingTextSend` клиентам, **затем безусловно** `Utils.IsInView`→`Camera.main` (null на headless)→NRE; зовётся с сервера из `Crit.cs:65/74`. Закрыт surgical-гейтом ПОСЛЕ блока релая (`if (ServerBootstrap.IsHeadlessServer) return;` перед `Utils.IsInView`) — релай клиентам сохранён, локальный текст пропущен. На сервере объект FloatingText не создаётся → `Update` (cachedMainCamera) там не выполняется.

### Watch-point (не фикс): `GameManager.GameStart` l.464-465
`playerPosition[currentPlayer]` + `camera_TopDown.transform.position=...` идут ДО гейта `if(isClient)return`. На сервере не падает, ПОКА `currentPlayer==0` (сервер слот не берёт) и `camera_TopDown` назначен в сцене. Это присвоение transform, не `Camera.main`. Проверить в рантайме, что `currentPlayer` на сервере = 0 (иначе IndexOutOfRange).

### (Историческая справка) Почему `UIManagerMenu` не входил в исходные 4
Подход Б закрыл 4 ВНУТРИигровых синглтона (match-сцена). Но сервер стартует в СЦЕНЕ МЕНЮ и через неё ведёт старт матча — там 5-й клиентский синглтон `UIManagerMenu`, он НЕ гейчен. На `-nographics` упадёт в:
- `UIManagerMenu.Start` (UI меню на старте сервера, l.49+);
- `SceneHandler.StartTheGame:133 HideUIDocument()` — ДО `OnGameStart.Invoke()` (l.135) → ре-синк не выполнится (главный блокер старта матча, Risk #3 отчёта);
- `SlotManager` (197/216/308/381/411/429/447 — ShowMenuLobby/FillPlayerList, дёргается из RandomizeSlotData на старте матча);
- `NCH.ClientConnected:140-141`, `PauseTheGame:98-99`, `ResumeTheGame:123`.
Это Risk #5/#3 отчёта (Шаг 3), подход Б их не покрывал. Чистый фикс (правило 5): гейтнуть сам `UIManagerMenu` тем же флагом — `Start` + `HideUIDocument`/`ShowUIDocument`/`ShowMenuLobby`/`FillPlayerList`/`AddChatServerMsg` (instance из `Awake` остаётся) → все call-site в SceneHandler/NCH/SlotManager станут безопасны автоматически. ~6 правок. ОЖИДАЕТ разрешения.

---

## Статус матч-сцены (исправлено 2026-06-20)

Ранее ошибочно счёл `Artsiom.unity` пустой — ошибка метода: **сцена сохранена в БИНАРНОМ формате** (`file` → data; Menu.unity — текст). Grep по YAML-токенам (`m_Script`, `--- !u!1`) и по ASCII-GUID на бинаре даёт 0 → ложные «пусто» и «MatchManager не найден».

На деле `Artsiom.unity` (357 КБ) содержит матч-сетап — подтверждено `strings`: GameObject `MatchManager` (`StrategyCore.MatchManager`, поля `teamA/teamB/ownerPlayer`, ссылка `uiManager`), башни `CannonTowerEnemy1/2`/`CannonTowerOwn2`, `TerrainPlane`, `lane`, точки. Сцена играется.

**Следствие:** 4 синглтона (через `GameManager.prefab` + `CameraRig.prefab` в сцене) на headless реально создаются и упадут → подход Б нужен и **тестируется** (headless-сборка + 2 клиента). Блокера «негде тестировать» НЕТ.

**Осталось от Шага 2:** значения `teamA.ownerPlayer`/`teamB.ownerPlayer` бинарный файл статикой не читается — проверить в Inspector (должны быть 0 и 1). Либо временно переключить сериализацию сцены в Force Text.

---

## Механизм детекции — выбрано: E (DRY-возврат)

Один общий флаг + ранний выход в начале каждого серверо-достижимого места. «Меньше точек входа» (правило 5): источник истины — ОДИН.

**Флаг (в нашем коде, разрешения НЕ требует):** в `ServerBootstrap.cs` добавить статик-геттер
```csharp
public static bool IsHeadlessServer =>
    !Application.isEditor                                  // в редакторе сервером не становимся (Host/Client/Server из меню)
    && (Application.isBatchMode
        || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null); // реальная Dedicated Server / -nographics
```
> Эволюция (2026-06-21): (1) `Application.isBatchMode` НЕ хватает — на Dedicated Server build `-batchmode` не передаётся (лог «Unknown arg»), `isBatchMode=false`. (2) `#if UNITY_SERVER` — ТОЖЕ неверно: это compile-time по активному build target, и в Editor с таргетом Dedicated Server он истинен даже для Play/MPPM-клиентов → они ложно считали себя сервером и дрались за порт 7777 (NRE). (3) Итог — РАНТАЙМ-проверка: `!isEditor && (isBatchMode || gfx==Null)`. На сервере лог показывал `Forcing GfxDevice: Null`. `ShouldAutoStart` тоже на `IsHeadlessServer`. Плюс защита в `StartAsServer`: при отказе `StartServer()` (порт занят) — ранний выход, без NRE-каскада.
`ServerBootstrap` — наш файл (ассет не тронут), `Application.isBatchMode` уже используется там (l.56/69). Геттер статический и без инстанса — доступен из `Awake` любого класса. Включается ТОЛЬКО на реальном headless-билде → текущий тест «сервер с графикой» (MPPM) не меняется, нулевой регресс.

**Гейт в каждом месте (правка ассета — ТРЕБУЕТ разрешения):**
```csharp
if (ServerBootstrap.IsHeadlessServer) return;   // [Interflow fix YYYY-MM-DD]
```
для методов с возвратом — `return null/false/default`.

Честно про «DRY»: единый — только флаг (его определение и смысл в одном месте). Сами строки `if (...) return;` всё равно стоят по-методно — в C# нет хелпера, который сделает `return` за вызывающего. DRY здесь = «одна правда о том, что такое сервер», а не «один guard на все методы».

Ниже в тексте «server» = `ServerBootstrap.IsHeadlessServer`.

**Если позже понадобится** покрыть и «dedicated-сервер с графикой» — меняется ТОЛЬКО тело геттера (напр. `Application.isBatchMode || serverStartedFlag`), все 30+ гейтов остаются как есть.

---

## По классам: как работает сейчас / что и где менять

### 1. SoundFXManager — `Audio/SoundFXManager.cs` (на `GameManager.prefab`)
**Сейчас:** `instance` в `Awake` (27-33, без графики). `Start` (35-38) — подписка на `GameManager.Tick` (безвредно на сервере). 9 публичных Play-методов: `Instantiate(soundFX)` + `Utils.IsInView` (нужна камера) + `FogOfWar.IsVisible(..., currentTeam)` (на сервере `currentTeam == -1`).
**Серверо-достижимо:** вызывается без `isClient`-гейта из `Unit.cs` (1932/1992/2024-2025/2143/2297-2298/2422…), `Projectile`, `ConstructionUnit`, абилок.
**Менять:** ранний `return` в начале каждого публичного метода под гейтом (Loop-версии → `return null`):
`PlayCommandSound` (49), `PlaySoundClip` (80, 101, 127, 151), `PlayLoopSoundClip` (180, 198, 219, 240).
`Awake`/`Start` не трогаем (`instance` нужен, `Tick` безвреден). `fxGroup`/`voiceGroup` остаются (передаются аргументом в no-op).

### 2. PlayerControl — `PlayerControl.cs` (на `GameManager.prefab`, рядом `UIManager`)
**Сейчас:** `Awake` (63-70) — `instance` + `coreInput = new StrategyCoreInput()`. `OnEnable` (71-82) — `GetComponent<UIManager>()`, `coreInput.Enable()` + подписки, `Camera_TopDown.onDoubleClick`. `Start` (96-112) — `Instantiate(moveVFX/moveAttackVFX)` + input-подписки + `UnitProjectorCreate()` (графика). `Update` (115+) — сразу `Camera_TopDown.instance.GetCursorPosition()` (камера), каждый кадр.
**Серверо-достижимые члены (из `Unit.cs` и др.):** `RemoveFromSelection` (1296; зовётся 3137/4725/5288…), `activeUnit` (поле, read 5424/5531 — `null` ок), `IsSelected` (1410; 5525), `AddToSelection` (1274), `Reset` (1855).
**Исправление аудита:** `ComputeFormation` (798) **больше НЕ серверо-достижим** — `FormationMarch` перешёл на статический `FormationLayout` (2026-06-19); реальных вызовов из server-кода нет (только комментарии в `FormationLayout.cs`/`MatchManager.cs:443`). → PlayerControl можно полностью no-op'ить, ничего «настоящего» сохранять не нужно.
**Менять:**
- `Awake`: оставить `instance=this` и `coreInput = new StrategyCoreInput()` (его статически читает `UIManager`; создание дёшево, без графики).
- `OnEnable`: `if (server) return;` в начале.
- `OnDisable` (83-93): `if (server) return;` в начале (иначе отписка от неподписанного; для чистоты).
- `Start`: `if (server) return;` (пропустить VFX/projector/подписки).
- `Update`: `if (server) return;` в самом начале (до l.118).
- Методы: ранний `return` в `AddToSelection`/`RemoveFromSelection`/`Reset`; `IsSelected` → `return false`. `activeUnit` — поле, на сервере `null`, не трогаем.

### 3. UIManager — `UI/UIManager.cs` (+ `.BottomTables`, `.TechPanel`; на `GameManager.prefab`)
**Сейчас:** `Awake` (115-121) — `instance`. `Start` (123+) — строит весь UI из `uiDocument.rootVisualElement.Query(...)` → NRE на сервере; присваивает `pc = PlayerControl.instance` (l.126). `Update` (280) — читает `pc.activeUnit` (l.282) и `PlayerControl.coreInput` (296) и `ViewRectUpdate()` (301). (LateUpdate в текущем коде не найден — раньше был для скрытия HUD; проверить при реализации.)
**Серверо-достижимо:** `UIManager.instance` зовётся из ~20 файлов (`Unit`, `NetworkDataSync`, абилки, `FogOfWar`, `GameManager`, `GameResources`, `SlotManager`, `NCH`, `SaveManager`, `Transport`…).
**Сигнатуры server-reachable методов (для no-op):**

| Метод | Строка | Возврат |
|-------|--------|---------|
| ShowNotifyMsg(string) / (string,int,bool) | 2786 / 2799 | void |
| UpdateResourceTab(int) / RefreshResourceTab() | 2754 / 2772 | void |
| AddChatServerMsg(string) / AddChatMsg(string,int,bool) / ShowChatBox() | 2030 / 2003 / 1968 | void |
| SubscribeToUnit() / UnsubscribeToUnit(Unit) / Resubscribe() | 331 / 424 / 479 | void |
| ShowLevelButton() / HideLevelButton(bool) / HideCancelButton() | 2354 / 2364 / 2301 | void |
| RedrawAbilityView() / ItemDragCancel() | 889 / 2552 | void |
| CreatePinger(int,Vector2) / WorldToMiniMapPing(float,float) / ResetMiniMap() | 2676 / 2692 / 2878 | void |
| CursorToUIposition() | 2885 | Vector2 (→ default) |

**Менять:**
- `Start` (123): `if (server) return;` в начале (весь UI-build и `pc=...` пропускаются; `instance` уже из `Awake`).
- `Update` (280): `if (server) return;` в начале (`pc` будет `null`, т.к. `Start` пропущен → иначе NRE).
- Каждый server-reachable метод: ранний `return` ПЕРЕД обращением к VisualElement-полям (они `null`, т.к. `Start` пропущен) → **обязательно по-методно**, не только `Start`.
- Методы `UIManagerMenu` (меню: `FillPlayerList`/`AddChatServerMsg`/`HideUIDocument`…) — часть headless-рисков №5; частично уже под null-guard (`// [Interflow fix 2026-06-19/20]`); полный список — в `dedicated-server-steps-2-3-plan.md`.

Самый объёмный класс, но изменения механические (≈20 однострочных guard'ов + `Start`/`Update`).

### 4. Camera_TopDown — `Universal Strategy Camera/Scripts/Camera_TopDown.cs` (на `CameraRig.prefab`)
**Сейчас:** `Awake` (173-178) — `new CameraControls()` + `instance`. `OnEnable` (179-196) — `InputSystem.AddDevice("VirtualMouse")` + `EnhancedTouchSupport`. `Start` (131-171) — настройка камеры + `InputSystem.onAnyButtonPress`. `Update` (205+) — ввод/камера. `OnDisable` (197-202) — `RemoveDevice(virtualMouse)`.
**Серверо-достижимых членов НЕТ (подтверждено):** `GetCursorPosition`/`GetUnitAtCursor`/`TerrainScreenRaycast` зовутся только из `PlayerControl`/`UIManager` (gated). `Utils.GetUnitAtCursor` (использует `Camera_TopDown.instance`, Utils.cs:1841) вызывается только из `PlayerControl`/`UIManager`/`Camera_TopDown` — не из server-кода.
**Менять — только guard'ы инициализации (членов реализовывать не надо):**
- `Awake`: оставить `instance` (и `new CameraControls()` — дёшево).
- `OnEnable`: `if (server) return;`.
- `Start`: `if (server) return;`.
- `Update`: `if (server) return;`.
- `OnDisable`: `if (server) return;` (`virtualMouse` может быть `null`, если `OnEnable` пропущен).

---

## Помимо 4 классов
- Общий флаг детекции (1 место) — см. выше.
- Шаг 2 (матч-сцена + `MatchManager` + `ownerPlayer`) — блокер теста, отдельно.
- Build target Dedicated Server / batchmode (Unity 6.4 build profiles) — для реального headless-билда.

## Проверка (после сборки матч-сцены)
`-server -nographics -batchmode`, 2 клиента → автостарт; при загрузке матч-сцены НЕТ NRE из `PlayerControl.Update` / `UIManager.Start`/`Update` / `SoundFXManager.*` / `Camera_TopDown.*`; матч идёт; команды/каст у клиентов работают (как в Verification-отчёте).

## Объём
4 файла, ≈30-35 однострочных guard'ов + 1 общий флаг. Механически — несколько часов. Плюс отлов рантайм-NRE в событиях/корутинах (статически не видны). Тест — headless-сборка + 2 клиента (сцена `Artsiom` собрана; см. «Статус матч-сцены»).
