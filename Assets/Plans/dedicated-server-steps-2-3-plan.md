# План: Шаги 2 и 3 (dedicated server)

Продолжение `dedicated-server-risk-verification.md`. Здесь детализированы **Шаг 2** (риск №1г — конфиг `ownerPlayer`) и **Шаг 3** (риск №5 — настоящий headless `-nographics`). Шаг 1 (guard в `SlotManager.SetCurrentPlayer`) — отдельно.

Правила проекта: ассет не трогать без прямого разрешения (правило 1, маркер `// [Interflow fix YYYY-MM-DD]`); ничего не добавлять без указания (правило 7); максимум встроенных средств (правило 2).

---

## Шаг 2 — конфиг `ownerPlayer` команд (риск №1г)

### Результат локализации (2026-06-20, ИСПРАВЛЕНО)
- **Поправка:** ранее ошибочно написал «MatchManager не найден» — `Artsiom.unity` сохранён в БИНАРНОМ формате, и мой grep по ASCII-GUID/YAML-токенам давал ложный 0. Фактически `MatchManager` — это GameObject в `Artsiom.unity` (подтверждено `strings`: `StrategyCore.MatchManager`, `teamA/teamB/ownerPlayer`, `uiManager`). Компонент размещён, конфиг существует.
- `teamA` / `teamB` — `[SerializeField] TeamWaveConfig` (l.101/103), `ownerPlayer` default **0** (l.24). `TeamIndexOfOwner` (l.426-427): teamA при `ownerPlayer==player`, иначе teamB. **Если оба `ownerPlayer` равны (напр. оба 0) — teamB недостижим, команда client2 режется** (та самая регрессия чинимого бага) — поэтому значения нужно проверить.
- **Чего НЕ удалось проверить статикой:** конкретные значения `teamA.ownerPlayer`/`teamB.ownerPlayer` — бинарная сцена не читается grep'ом. Нужен Inspector (или временно Force Text для `Artsiom`).

### Открытый вопрос (нужен ответ/проверка)
**Какие значения `ownerPlayer` стоят у teamA/teamB в `Artsiom`?** Должны быть 0 и 1 (и совпадать со `SlotManager.playerTeam` / `teamAffiliation` замков). Это единственное, что осталось от риска №1г.

### Действия
1. **Editor (пользователь):** открыть `MatchManager` в `Artsiom`, убедиться `teamA.ownerPlayer = 0`, `teamB.ownerPlayer = 1`.
2. **Код, опционально, требует подтверждения:** расширить существующий `MatchManager.ValidateSetup()` (l.1205, уже зовётся из `Start` l.214, server-only) проверкой: `ownerPlayer` обеих команд ∈ {0,1} и не равны → иначе `Debug.LogError`. Превращает «тихий отбой команды» в явный сигнал. Новых точек входа не плодим (правило 5), числа/диапазон — без хардкода.

---

## Шаг 3 — настоящий headless `-nographics` (риск №5) — ТРЕБУЕТ РАЗРЕШЕНИЯ НА ПРАВКУ АССЕТА

Это правки кода StrategyCore (`NetworkConnectionHandler`, `SceneHandler` + клиентские синглтоны). По правилу 1 — только с прямого разрешения, маркер `// [Interflow fix YYYY-MM-DD]`. **Делать только если нужен реальный headless** (сейчас тест с графикой работает, см. риск №5/№3 отчёта).

### Места под null-guard (проверено по коду 2026-06-20)

| Файл | Строки | Вызов | Текущее состояние |
|------|--------|-------|-------------------|
| NetworkConnectionHandler | 140-141 | `UIManagerMenu.instance.ShowMenuLobby/FillPlayerList` (ветка Menu, для всех) | без guard |
| NetworkConnectionHandler | 202-203 | `UIManagerMenu.instance.ShowUIDocument/ShowMenuLobby(0)` (локальный дисконнект) | без guard |
| NetworkConnectionHandler | 219-220 | `AddChatServerMsg` (UIManagerMenu/UIManager.instance) | guard только от `-1` (имя), НЕ от null-instance |
| NetworkConnectionHandler | 227 | `UIManagerMenu.instance.FillPlayerList` | без guard |
| NetworkConnectionHandler | 98-99 | `PauseTheGame`: `UIManagerMenu.instance.ShowUIDocument/ShowMenuLobby(2)` | без guard |
| NetworkConnectionHandler | 123 | `ResumeTheGame`: `UIManagerMenu.instance.HideUIDocument` | без guard |
| SceneHandler | 133 | `UIManagerMenu.instance.HideUIDocument()` — идёт ДО `OnGameStart.Invoke()` (l.135) | без guard — **критично**: NRE срывает ре-синк (риск №3) |
| SceneHandler | 137 | `GameManager.instance.GameStart()` | проверить null/headless-ветку |

Примечание: chat-msg в `ClientConnected` уже закрыт (`// [Interflow fix 2026-06-20]`, l.166-181); в `ClientDisconnected` (l.219) закрыт только от `-1`, не от null-UI.

### Клиентские синглтоны (по `dedicated-server-audit`)
`PlayerControl`, `UIManager` (~21 no-op член), `Camera_TopDown` (только guard инициализации), `SoundFXManager` (~5 членов). Их `Awake`/`Start` в batchmode поднимут Input System / UI Toolkit / VFX → нужны guard'ы инициализации (ставить `instance`, графику пропускать). `ComputeFormation` серверу уже не нужен — `FormationMarch` перешёл на статический layout.

### Подход (выбор пользователя — не решаю сам)
- **(A)** Точечные null-guard'ы в перечисленных местах. Минимум правок, но «латание дыр».
- **(B)** Серверный режим у 4 синглтонов (instance есть, методы no-op на сервере). Ближе к аудиту, больше работы, чище.

### Порядок и проверка
1. Сначала самое критичное для запуска: `SceneHandler.StartTheGame` l.133 (иначе `OnGameStart` не сработает → нет ре-синка → команды режутся).
2. Затем connect/disconnect/pause guards.
3. Затем синглтоны (инициализация в batchmode).
4. **Тест:** запуск `-server -nographics`, 2 клиента подключаются → автостарт; нет NRE из перечисленных мест; в логах клиентов `GetClientSlot ∈ {0,1}`, `currentPlayer == GetClientSlot`, команды/каст не отбиваются (как в разделе Verification отчёта).

---

## Связь с нововведениями Unity (2026-06-20)
- Headless-сборка — штатный **Dedicated Server build target / build profiles** (Unity 6.4, проект на 6000.4.7f1).
- `PlayerPrefab=null` теперь штатно поддержан (фикс в NGO 2.x; проект на 2.12.0).
- **Online-хостинг headless-сервера: Unity Multiplay свёрнут (31.03.2026).** Это за рамками Шагов 2/3 (сейчас прямой IP+port), но влияет на будущий «выход в интернет»: вместо Unity Multiplay — Sessions API (Relay/Lobby живы) либо сторонний хостинг.
