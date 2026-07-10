# Чек-лист: сборка и прогон выделенного сервера + 2 клиента

Финальная валидация подхода Б (headless-сервер). Код закрыт; ниже — только то, что делается в Unity/рантайме.

> **Ключевой принцип:** флаг `ServerBootstrap.IsHeadlessServer => Application.isBatchMode`.
> В **editor (включая MPPM) `isBatchMode == false`** → гейты НЕ срабатывают (сервер «с графикой»).
> Реальный headless-путь проверяется **только** в билде с `-batchmode -nographics`.

---

## Этап 0. Предусловия (один раз)

- [ ] Unity 6000.4.7f1 открывает проект без ошибок компиляции (Console чист).
- [ ] Unity Hub → Installs → 6000.4.7f1 → Add Modules → установлен **Dedicated Server (Windows)**.
- [ ] Пакеты на месте (manifest): MPPM 2.0.2, Netcode for GameObjects 2.12.0, Multiplayer Center.
- [ ] В `Build Profiles` Scene List первой стоит `Assets/StrategyCore/Scenes/Menu.unity` (сервер стартует в меню).

---

## Этап A. Проверка сцены в Inspector (бинарный Artsiom.unity)

Статикой бинарную сцену не читаем — только глазами.

- [ ] Открыть матч-сцену `Artsiom.unity`.
- [ ] Выбрать `MatchManager` → подтвердить `teamA.ownerPlayer = 0`.
- [ ] Подтвердить `teamB.ownerPlayer = 1`.
- [ ] Подтвердить, что ссылка `uiManager` назначена (не None).
- [ ] (Опц.) Если значения иные — это рассинхрон команд; исправить до прогона.

---

## Этап B. Быстрый прогон логики в Editor + MPPM (с графикой)

Проверяет netcode-логику и старт матча. **Гейты тут не активны** (это нормально).

- [ ] Window → Multiplayer Play Mode → включить 2 Virtual Players (Player 2, Player 3).
- [ ] Одному Virtual Player в поле **Arguments** прописать `-server` (воспроизводит `StartServer` без локального игрока).
- [ ] Нажать Play (main editor + 2 virtual players = 3 инстанса).
- [ ] Сервер-инстанс: в Console лог `[ServerBootstrap] Сервер запущен (StartServer)`.
- [ ] Два клиента: Join Game → подключились.
- [ ] При 2 клиентах: лог `Набралось игроков (2) — старт матча`.
- [ ] После загрузки сцены: лог `Список игроков ре-разослан клиентам (OnGameStart)`.
- [ ] У клиентов корректные слот/команда; команды юнитам и каст абилок работают.
- [ ] Крит показывает «CRT!» (релай FloatingText дошёл).

---

## Этап C. (Опц.) Прогон гейтов в Editor без полной сборки

Только если хочешь дёшево отловить headless-NRE до билда. **Требует временной правки ассета — откатить после теста.**

- [ ] Временно расширить флаг в `ServerBootstrap.cs:47`:
      `=> Application.isBatchMode || System.Environment.GetCommandLineArgs().Contains("-server");`
      (нужен `using System.Linq;`).
- [ ] Virtual Player с аргументом `-server` теперь идёт по headless-ветке.
- [ ] Прогнать Этап B повторно; убедиться, что гейты отрабатывают, нет NRE на сервер-инстансе.
- [ ] **ОТКАТИТЬ правку флага** (иначе клиент с `-server` тоже уйдёт в no-op).

---

## Этап D. Dedicated-сборка сервера (п.3)

- [ ] File → Build Profiles → новый профиль, платформа **Dedicated Server → Windows**.
- [ ] Scene List: `Menu.unity` первой, затем `Artsiom.unity`.
- [ ] Build → получить `Server.exe` (server subtarget, без графики).
- [ ] (Клиенты) Отдельный профиль **Windows → Player** → `Client.exe`. Либо клиентов гонять из editor.

---

## Этап E. Запуск сервера + 2 клиента (п.4)

- [ ] Сервер: `Server.exe -batchmode -nographics -logFile server.log`
      (`autoStartInBatchmode=true` → стартует сам; `requiredPlayers=2`).
- [ ] Клиент 1: `Client.exe` → Join на адрес сервера (по умолч. 127.0.0.1).
- [ ] Клиент 2: `Client.exe` → Join.
- [ ] Сервер сам стартовал матч после 2-го клиента.

---

## Этап F. Проверка логов и рантайма

В `server.log` **НЕ должно быть NRE** из:

- [ ] `PlayerControl.Update`
- [ ] `UIManager.Start` / `UIManager.Update`
- [ ] `SoundFXManager.*`
- [ ] `Camera_TopDown.*`
- [ ] `FloatingText.Spawn` (при крите)
- [ ] `ShowNotifyMsg`

Рантайм watch-point — `GameManager.GameStart` (строки 464-465) на сервере:

- [ ] Подтвердить `SlotManager.currentPlayer == 0` (иначе IndexOutOfRange по `playerPosition[]`).
- [ ] Подтвердить `camera_TopDown != null` (сериализованное поле / `GameObject.Find("CameraRig")`; иначе NRE на стр. 465 независимо от currentPlayer).

Функциональность матча:

- [ ] Матч идёт на сервере без исключений в логе.
- [ ] У обоих клиентов: команды юнитам, каст абилок, ресурсы — работают.
- [ ] Крит «CRT!» виден у клиентов (релай дошёл, локальный визуал на сервере пропущен).

Невидимое статикой (отлов только на прогоне):

- [ ] Нет NRE в событиях/корутинах на серверном пути.
- [ ] FoW не падает на работе с текстурами/RenderTexture при `-nographics`.

---

## Если упало — куда смотреть

- NRE из гейченого метода → проверить, что метод реально под `if (ServerBootstrap.IsHeadlessServer) return;` (читать файл напрямую, **не grep** — индекс grep в проекте устаревший/рассинхрон).
- IndexOutOfRange в `GameStart` → `currentPlayer != 0` на сервере (сервер занял слот?).
- NRE на стр. 465 при currentPlayer==0 → `camera_TopDown` не назначен в сцене / нет `CameraRig`.
- Клиенты без слотов/команд → ре-синк `PlayerListSend` не дошёл (см. `RebroadcastPlayerList`, лог OnGameStart).
- Новый блокер из меню → ещё один синглтон/метод не гейчен (sweep по `Camera.main`, UI Toolkit, релай-then-визуал).
