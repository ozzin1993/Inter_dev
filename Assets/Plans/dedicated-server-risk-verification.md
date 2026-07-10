# Project Overview
- Game Title: Interflow (StrategyCore-based RTS)
- High-Level Concept: Сетевая RTS на Unity 6 + Netcode for GameObjects (NGO). Перенос с модели «host = игрок» на **выделенный сервер** (`NetworkManager.StartServer()`, без локального игрока): 2 клиента-игрока, сервер слот не занимает.
- Players: Networking multiplayer (2 клиента + dedicated server). Тестовый стенд — Multiplayer Play Mode (MPPM), сборка **с графикой** (не `-nographics`).
- Target Platform: StandaloneWindows64
- Render Pipeline: Built-in
- Версии: NGO **2.12.0**, MPPM 2.0.2, Unity 6000.4.7f1.
- NetworkManager (`[ProjectManager]`, сцена Menu) — подтверждено: **NetworkTopology = ClientServer**, **ConnectionApproval = True**, **EnableSceneManagement = True**, **PlayerPrefab = null**, UTP 127.0.0.1:7777, TickRate 30.

> Это **отчёт-верификация**: каждый пункт «ОТКРЫТЫХ РИСКОВ» проверен против фактического кода NGO/ассета (подтверждён/опровергнут), плюс предложены правки для подтверждённо-открытых рисков. Правки ассета применять только с разрешения (маркер `// [Interflow fix ...]`).

# Game Mechanics
## Core Gameplay Loop
Не меняется. Меняется только запуск сетевой сессии (dedicated server) и синхронизация списка игроков/привязки команд к локальному игроку.
## Controls and Input Methods
Не меняется (drag-select + Move/Attack через PlayerControl; нижние таблицы — командные кнопки Attack/Defence и центральная таблица способностей).

# UI
Меню/HUD ассета не редактируется. Нижние таблицы — рантайм-партиал `UIManager.BottomTables`.

# Key Asset & Context
Файлы и ключевые места, на которые опираются вердикты ниже:
- `Scripts/ServerBootstrap.cs` (наш) — `StartAsServer` (l.100-136), `TryAutoStartMatch` (l.148-158), `RebroadcastPlayerList` (l.162-168), `OnDestroy` (l.222-228).
- `Scripts/Menu/SlotManager.cs` — `SetCurrentPlayer` (l.503-508, без guard), `GetClientSlot` (l.479-482, `Array.IndexOf`→ -1), `OnGameStart` (l.21), `RandomizeSlotData` (l.161-188, сам шлёт `PlayerListSend(0,true)` на l.187), `InitializeSlotData` (l.107+, `playerID[]=-1`), `currentPlayer` (l.31, default **0**).
- `Scripts/Network/NetworkDataSync.cs` — `PlayerListSend` (l.1338-1363), `PlayerListClientRpc` `[Rpc(SendTo.SpecifiedInParams)]` (l.1366-1379), `PlayersListClientRpc` `[Rpc(SendTo.NotServer)]` (l.1382-1396, `SetCurrentPlayer(GetClientSlot(LocalClientId))` на l.1393). Ни один из двух RPC **не имеет guard `connectionStage==2`**.
- `Scripts/Menu/SceneHandler.cs` — `StartTheGame` (l.125-144): early-return (l.128), `HideUIDocument()` (l.133), `OnGameStart?.Invoke()` (l.135), `GameManager.GameStart()` (l.137); `SceneManager_OnSceneEvent`→`LoadEventCompleted`→`StartTheGame(true)` (l.~242-245).
- `Scripts/Network/NetworkConnectionHandler.cs` — `ClientConnected` (l.132-183), `ClientDisconnected` (l.188-237), `ConnectionApproval` (l.242-276), `PauseTheGame`/`ResumeTheGame` (l.93-125), `StartHost` lobby-ветка (l.309-315).
- `Scripts/Network/NetworkCommandSync.cs` — все командные RPC `[Rpc(SendTo.Server)]` с гейтом `owner == unit.owner || debugMode`, где `owner = GetClientSlot(rpcParams.Receive.SenderClientId)` (Move l.99-112, Attack l.120-140, и т.д.).
- `Scripts/MatchManager.cs` — `TeamWaveConfig.ownerPlayer` (l.24, **Inspector-поле, default 0**), `abilityCaster` (l.65), `centralAbilities` (l.70), `Team(int)` (l.231), `TeamIndexOfOwner` (l.424-428), спавн юнитов с `owner = cfg.ownerPlayer` (l.588, 825), `SendAttackCommand`/`SendDefenceCommand` (l.286+/337+; клиентская ветка шлёт per-unit RPC через NetworkCommandSync).
- `Scripts/Unit.cs` — `owner` (player index, l.21), `SetOwnership`: `team = playerTeam[owner]` (l.5407-5413).
- `Scripts/UI/UIManager.BottomTables.cs` (наш) — `CommandTeamForLocalPlayer` (l.525-539), `ExecuteBottomTableCommand` (l.511-519), `SyncCommandTeamToLocalPlayer` (l.543-553, в LateUpdate l.563), `ActivateAbilityCell` (l.408-448).
- `Scripts/Grid.cs` — `AssignToChunk`/`AssignToChunkInitial` с `Mathf.Clamp` (l.75-76, 93-94).

---

# Верификация рисков (подтверждено / опровергнуто)

## Риск №1 — привязка `commandTeamSlot` к локальному игроку → **ПОДТВЕРЖДЕНО КОРРЕКТНО (с конфиг-оговоркой)**
- **(а) условие `Team(i).ownerPlayer == currentPlayer` для авторизации команды — КОРРЕКТНО.** Цепочка консистентна:
  1. `CommandTeamForLocalPlayer()` (BottomTables l.531-535) выбирает команду, где `ownerPlayer == currentPlayer`.
  2. `MatchManager.SendAttack/DefenceCommand` (клиентская ветка) фильтрует юниты `owner == ownerPlayer` и шлёт **per-unit** RPC через `NetworkCommandSync`.
  3. Сервер в RPC проверяет `GetClientSlot(senderClientId) == unit.owner` (NetworkCommandSync l.102-103).
  Итог: команда применяется ⇔ `GetClientSlot(senderClientId) == currentPlayer`. Это в точности совпадает с гейтом NetworkCommandSync.
  **Уточнение к формулировке риска:** `SendAttack/DefenceCommand` живут в `MatchManager`, а не в `NetworkCommandSync` (последний — только транспорт per-unit команд). На вердикт не влияет.
- **(б) `currentPlayer` к моменту клика — ВЕРНЫЙ только ПОСЛЕ ре-синка (зависит от риска №3).** До прихода слота `currentPlayer` = default **0**; `SyncCommandTeamToLocalPlayer()` в LateUpdate (l.563) сам перестроит таблицу, когда `currentPlayer` обновится. Подтверждено: зависимость от №3.
- **(в) перестройка таблицы способностей не ломает каст — КОРРЕКТНО.** `ActivateAbilityCell` кастует через `Team(commandTeamSlot).abilityCaster` + индекс в `caster.abilities`, серверо-авторитетно через `Unit.UseAbilityItem`→NetworkCommandSync UseAbility RPC (тот же гейт `owner==unit.owner`). Работает ⇔ `abilityCaster.owner == team.ownerPlayer == currentPlayer` (конфиг спавна кастера). Обход бага `Utils.GetAbilityIndex` присутствует (l.436-444) — ок.
- **(г) предположение «слот == ownerPlayer» (2 команды: 0 и 1) — ЭТО КОНФИГ-ИНВАРИАНТ, НЕ ЗАВ. КОДОМ.** `TeamWaveConfig.ownerPlayer` — Inspector-поле, default **0**, нигде в коде не присваивается из слота. Корректность всей схемы держится на: `teamA.ownerPlayer=0` И `teamB.ownerPlayer=1` И клиенты занимают слоты 0/1 (порядок коннекта, `AddClientID` — первый свободный). **Если оба `ownerPlayer` оставлены по умолчанию (0) — оба клиента резолвят team0, и client2 снова получает отбой (регрессия чинимого бага).**
  - ⚠️ **`MatchManager` не найден ни в одной `.unity`/`.prefab` под `Assets/StrategyCore`** — где и с какими значениями задаются `teamA.ownerPlayer`/`teamB.ownerPlayer`, нужно **локализовать и проверить вручную** (Шаг 2).

## Риск №2 — `SetCurrentPlayer` без guard на `-1` → **ПОДТВЕРЖДЕНО (нужен guard)**
`SlotManager.SetCurrentPlayer` (l.503-508) не защищён: `currentPlayer = slotIndex` (присвоит даже -1), затем `playerTeam[slotIndex]` → `IndexOutOfRangeException` при -1. Вызывается из `PlayerListClientRpc` (l.1376) и `PlayersListClientRpc` (l.1393). В штатном пост-ресинк потоке слот резолвится (нет -1), но при гонке (id ещё не в `playerID`) — краш, причём `currentPlayer` остаётся -1. **Рекомендуется guard (Шаг 1).** Примечание: «зависание на 0» — это потеря раннего send (default 0), а «-1/краш» — гонка; это разные симптомы.

## Риск №3 — ре-синк `PlayerListSend(0,true)` на `OnGameStart` → **ПОДТВЕРЖДЕНО (работает на сборке с графикой)**
- **`OnGameStart` срабатывает на dedicated-сервере — ДА.** `SceneHandler.StartTheGame` early-return (l.128) НЕ срабатывает в штатном автостарте: `saveFileName`/`saveSceneData` пусты, `clientsLoading.Count == 0`. `OnGameStart?.Invoke()` (l.135) вызывается через `LoadEventCompleted`→`StartTheGame(true)`.
- **`PlayersListClientRpc` доезжает и применяется на клиентах — ДА.** `[Rpc(SendTo.NotServer)]`; сначала перезаписывает `playerID[]`, затем `SetCurrentPlayer(GetClientSlot(LocalClientId))` (l.1385-1393) — слот резолвится из свежего массива. **У этих двух RPC НЕТ guard `connectionStage==2`** (в отличие от геймплейных), поэтому они применяются всегда. `connectionStage` к этому моменту сброшен в 0 в `StartTheGame` l.131 (выполняется и на клиенте) — в дампе ожидать **0**, не 2.
- **Тайминг — ОК.** `LoadEventCompleted` = все клиенты догрузили сцену → клиентский `networkHandler` (NetworkDataSync) уже заспавнен.
- ⚠️ **ОГОВОРКА (связь с №5):** `StartTheGame` l.133 `UIManagerMenu.instance.HideUIDocument()` выполняется **раньше** l.135. На сборке с графикой синглтон есть → ок. На настоящем `-nographics` это NRE **до** `OnGameStart?.Invoke()` → ре-синк не выполнится.

## Риск №4 — прямые команды `PlayerControl` зависят от `currentPlayer` → **ПОДТВЕРЖДЕНО**
Гейты `PlayerControl` l.212/245 (`owner != currentPlayer && !debugMode → continue`) и l.322/517 (`owner == currentPlayer || debugMode → proceed`); `currentPlayer` читается «вживую» (без кэша). После ре-синка (№3) `currentPlayer` верный → drag-select + Move/Attack у клиента работают. Та же зависимость, что №1(б)/№3.

## Риск №5 — Headless (`-nographics`) НЕ закрыт → **ПОДТВЕРЖДЕНО ОТКРЫТО**
Сейчас (с графикой) — ок. Для настоящего `-nographics` на серверном пути исполняются НЕзащищённые клиентские синглтоны:
- `NCH.ClientConnected` l.138-142 — `UIManagerMenu.instance.FillPlayerList()` в `GameState.Menu` (без guard).
- `NCH.ClientDisconnected` l.219-220, 227 — `AddChatServerMsg`/`FillPlayerList` защищены только от `-1`, **не** от null-UI.
- `NCH.PauseTheGame` l.98-99 / `ResumeTheGame` l.123 — без guard; достижимы через midgame-join (`ConnectionApproval` l.271).
- `SceneHandler.StartTheGame` l.133 (`HideUIDocument`), l.137 (`GameManager.GameStart()`).
- Плюс синглтоны `UIManager.*`, `PlayerControl`, `Camera_TopDown`, `SoundFXManager`.
Это не падает сейчас (синглтоны присутствуют на сборке с графикой), но блокирует `-nographics` (Шаг 3, опционально).

## Риск №6 — Регрессия Host-режима → **ОПРОВЕРГНУТО (регрессии не ожидается)**
- **NCH guards** — чисто аддитивные проверки на `-1`/null. В Host-режиме `GetClientSlot` валиден и UI присутствует → guard'ы no-op, поведение идентично (имя «Unknown» подставляется только при невалидном слоте, чего у хоста не бывает).
- **Grid clamp** — на host/сервере позиции в границах → `Mathf.Clamp` no-op.
- Рекомендация: всё равно прогнать быстрый smoke-тест Host+лобби (Шаг 4).

## Риск №7 — NGO 2.x `StartServer` / SessionOwner → **ПОДТВЕРЖДЕНО ОК**
- NGO **2.12.0**, **NetworkTopology = ClientServer** (проверено в конфиге NetworkManager). В ClientServer сервер всегда является session owner; **доп. настройка distributed authority / session ownership НЕ требуется**. `StartServer()` — штатная точка входа выделенного сервера. `ConnectionApproval=True`, `EnableSceneManagement=True`, `PlayerPrefab=null` — всё согласовано с моделью «сервер без игрока».
- NRE из `NetworkSceneManager.HandleSessionOwnerEvent → InvokeOnClientConnectedCallback` был **не** проблемой конфигурации NGO, а падением внутри ассетного `ClientConnected` (необработанные UI/slot-доступы), которое NGO вызывает в этом колбэке. Для chat-пути connect уже исправлено. Подписки на `OnClientConnectedCallback`/`OnSceneEvent` на чистом сервере корректны и поддержаны.
- `ServerBootstrap.StartAsServer` (l.100-120) в точности повторяет lobby-ветку `StartHost` (NCH l.309-315), подставляя `StartServer()` — корректно.

---

# Implementation Steps
> Применять только после одобрения. Правки ассета (Шаги 1, 3) — с разрешения пользователя, маркер `// [Interflow fix YYYY-MM-DD]`.

### Step 1 — Guard на `-1` в `SlotManager.SetCurrentPlayer` (риск №2)
- **Description:** В `Assets/StrategyCore/Scripts/Menu/SlotManager.cs` (l.503-508) добавить ранний выход/санитайз: если `slotIndex < 0 || slotIndex >= playerTeam.Length` → `Debug.LogWarning`, оставить `currentPlayer` прежним (или явно не трогать `currentTeam/currentName`), `return`. Маркер `// [Interflow fix]`. Это устраняет краш при гонке в `PlayerListClientRpc`/`PlayersListClientRpc`.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** Yes

### Step 2 — Локализовать и проверить конфиг `MatchManager` (риск №1г)
- **Description:** Найти, где создаётся/настраивается `MatchManager` (компонент не найден в сценах/префабах под `Assets/StrategyCore` — проверить игровую сцену матча, грузимую `SceneHandler.LoadScene()` по `sceneData[sceneIndex].sceneName`, и/или рантайм-инициализацию). Подтвердить `teamA.ownerPlayer == 0` и `teamB.ownerPlayer == 1`. Опционально: добавить в `ServerBootstrap`/`NetworkFlowLogger` стартовую валидацию (лог-ошибку, если `ownerPlayer` команд не {0,1} или совпадают) — это превратит «тихий отбой команды» в явный диагностируемый сигнал.
- **Assigned role:** explorer (локализация) → developer (валидация, если нужна)
- **Dependencies:** None
- **Parallelizable:** Yes

### Step 3 — (Опционально) Закрыть `-nographics`: guard'ы клиентских синглтонов (риск №5)
- **Description:** Обернуть в null-guard на dedicated-сервере: `NCH.ClientConnected` l.138-142 (`UIManagerMenu.instance.FillPlayerList`), `NCH.ClientDisconnected` l.219-220 и l.227, `NCH.PauseTheGame` l.98-99 / `ResumeTheGame` l.123, `SceneHandler.StartTheGame` l.133 (`HideUIDocument`) и l.137 (`GameManager.GameStart()`); плюс заглушки/guard'ы для `UIManager`, `PlayerControl`, `Camera_TopDown`, `SoundFXManager`. Каждое место — маркер `// [Interflow fix]`. **Делать только если требуется реальный headless-запуск** (сейчас тест с графикой работает).
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** Yes

### Step 4 — Регрессионный smoke-тест Host-режима (риск №6)
- **Description:** Прогнать обычный Host+Join (без dedicated): чат подключения/отключения, заполнение лобби, обычная игра, юниты не выходят за сетку. Подтвердить, что NCH guards и Grid clamp не изменили поведение.
- **Assigned role:** developer (ручной тест)
- **Dependencies:** None
- **Parallelizable:** Yes

# Verification & Testing
1. **Компиляция** без ошибок после правок.
2. **Dedicated (MPPM, с графикой):** сервер стартует, 2 клиента подключаются → автостарт матча; в дампе `NetworkFlowLogger` (F8) на клиентах: `GetClientSlot(localId)` ∈ {0,1}, `currentPlayer == GetClientSlot`, `connectionStage == 0`, `Team(currentPlayer).ownerPlayer == currentPlayer`.
3. **Команды/способности:** client1 командует только своей командой, client2 — своей; per-unit RPC не отбиваются (нет в логах `"...does not belong to the client!"`); каст из центральной таблицы проходит у обоих.
4. **Прямое управление:** drag-select + Move/Attack у обоих клиентов (риск №4).
5. **Риск №2:** искусственная гонка (ре-синк до заполнения `playerID`) больше не валит `IndexOutOfRangeException`.
6. **Риск №6:** Host-режим — без регрессий (чат/лобби/игра/границы сетки).
7. **(Если делался Step 3)** запуск `-nographics`/`-server` без NRE из перечисленных мест.
