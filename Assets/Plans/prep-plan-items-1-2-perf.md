# Prep-план: подготовка к реализации пунктов 1, 2 и серверного strip-presentation

Это **план для плана** (discovery/scoping): что прочитать, на что смотреть, какие решения принять и в каком порядке — ПЕРЕД написанием implementation-плана. Сам код здесь не пишется.

## Рамки (правила проекта)
- Правило 1 — ассет-ядро (Unit.cs, NetworkConnectionHandler и т.п.) не трогать без прямого разрешения; расширения — наши скрипты / partial.
- Правило 6 — серверо-авторитет; визуал допустим локально на клиенте.
- Правило 3 — всё в Inspector; правило 5 — единый источник истины; правило 8 — критичность/побочки.
- Правило 11 — **обязательно прочитать док StrategyCore** (Google Doc) по абилкам/юнитам/сети. Вики-ингест: `sources/strategycore-readme`.
- ⚠️ Сцена `Artsiom.unity` — **бинарная**: конфиг (ownerPlayer/кастеры/centralAbilities/teamUnitSets) не виден в гит-диффах и статикой. До работы с конфигом рассмотреть Force Text (см. «Остальные запросы»).

---

## Пункт 1 — единый валидируемый резолвер идентичности (игрок↔слот↔команда↔owner↔кастер)

### Что прочитать
- Код: `SlotManager.cs` — `currentPlayer`, `slotType[]`, `playerID[]` (слот→networkID), `playerTeam[]` (слот→teamIndex ассета), `playerName[]`, `GetClientSlot`, `SetCurrentPlayer` (l.503, без guard — Risk #2), `RandomizeSlotData`, `AddClientID`.
- `MatchManager.cs` — `Team(int)`, `TeamIndexOfOwner` (l.426), `ownerPlayer`, `abilityCaster`, `ValidateSetup` (l.1205).
- `UIManager.BottomTables.cs` — `CommandTeamForLocalPlayer`, поле `commandTeamSlot`, `SyncCommandTeamToLocalPlayer`. `WaveBuilderUI.LocalPlayerTeam`. `NetworkFlowLogger.LogOwnershipState` (дамп владения F8).
- Док/вики: `services/networking-slot-scene-save`, `services/match-manager`, `Assets/Plans/dedicated-server-risk-verification.md` (Risk #1г, #2).

### На что обратить внимание (критично)
- **ДВА понятия «команда»:** `SlotManager.playerTeam[slot]` (команда ассета) ≠ `MatchManager` teamA/teamB index (0/1). Мост — `ownerPlayer` (= слот). Резолвер обязан их свести и не путать; большинство багов сессии — отсюда.
- `ownerPlayer` — конфиг-инвариант, нигде не валидируется (Risk #1г).
- `currentPlayer` на клиенте приходит ПОСЛЕ ре-синка (тайминг). Резолвер должен возвращать «не готово/-1», а не дефолтный 0 (иначе ранний UI берёт чужую команду).
- Уже **3 копии** логики резолва (`CommandTeamForLocalPlayer`, `WaveBuilderUI.LocalPlayerTeam`, дамп логгера) — свести в один источник (правило 5).

### Решения до плана
- Где живёт резолвер: статик-хелпер vs метод на `MatchManager` (он и так синглтон на всех пирах).
- Что валидирует `ValidateSetup` и когда (Start/OnGameStart) + поведение при провале: `LogError` + блок старта матча? (ownerPlayer ∈ {0,1} и различны; кастеры различны и `owner==ownerPlayer`; centralAbilities не пусты).
- Чинить ли заодно `SetCurrentPlayer` guard на -1 (Risk #2) — он смежный.

### Открытые вопросы
- N команд/игроков в будущем (сейчас жёстко 2/1) — резолвер обобщать или MVP-2.

---

## Пункт 2 — контракт «клиент просит — сервер исполняет» + каст по идентичности

### Что прочитать
- Код: `NetworkCommandSync.cs` (все per-unit ServerRpc + гейт `GetClientSlot(sender)==unit.owner`). Наши партиалы `NetworkDataSync.TeamCommand.cs` / `WaveComposition.cs` / `PointSync.cs` (образец паттерна).
- `Unit.Ability.cs` — `UseAbilityItem` (l.54, клиент→RPC l.60), `CheckAbilityItemRequirements` (l.1001), `InitializeAbilities` (l.567). `Utils.GetAbilityIndex` / `GetAbilityByIndex` (глобальный индекс с контейнерами).
- `Ability.cs` — **есть `public int id` (l.26)** — стабильный ключ. `UIManager.BottomTables.ActivateAbilityCell` (l.408). `MatchManager.AssignCasterAbilities` (l.194, Awake).
- Док/вики: `services/abilities`, `services/networking-data-sync`.

### На что обратить внимание
- `Ability.id` существует — но **проверить уникальность и как назначается** (есть ли editor-tool «Assign IDs», как у `unitTypeID`). Если надёжен — каст по `id`: клиент шлёт `id`, сервер мапит `id→index` в своём `caster.abilities` и зовёт `UseAbilityItem(index)`. Это убирает требование «массивы байт-в-байт одинаковы на пирах» (та NRE `CheckAbilityItemRequirements`).
- Каст-API ассета **индексный** → «по идентичности» = только формат на проводе; внутри сервер мапит `id→index`. Ядро `Unit.Ability` НЕ трогаем (правило 1) — меняем только наши RPC/UI.
- Глобальный индекс (контейнеры) в `GetAbilityByIndex` — резолв `id→index` должен использовать ту же логику.
- Единый «request layer»: обобщить `TeamCommand`/`WaveEdit`/каст в один паттерн (`teamIndex` + owner-гейт в ОДНОМ месте), не плодить дубль на каждую фичу.

### Решения до плана
- Каст: перейти на `id` ИЛИ оставить index, но гарантировать идентичность массивов через детерминированный `AssignCasterAbilities` (тайминг). Выбрать одно.
- Делать общий request-слой или точечно по фиче.
- Нужна ли валидация `Ability.id` (editor-tool / `ValidateSetup`).

---

## Перф — strip presentation на сервере при спавне (80/20 вместо полного split)

### Что прочитать
- Код: `Unit.cs` — `Spawn` (l.5559 by ref / l.5579 by typeID) → `SpawnInternal` (l.5608); **`CalculateVisuals`** (создаёт renderers/animator/healthbar — healthbar l.470-471, animator ~l.781). Поля `animator` (l.315), `launchVFX` (`ParticleSystem[]` l.175). `ReferenceManager` (healthBar-префабы).
- Места спавна: `MatchManager.SpawnWave`/`RebuildTower`/призывы; `ConstructionUnit`; `GameManager.GameStart` (faction spawn). (Чтобы понять, ВСЕ ли спавны покрываются нашим хуком.)
- Вики: `services/units` (CalculateVisuals, презентация), `concepts/dedicated-server-audit`.

### На что обратить внимание (критично — иначе сломаешь симуляцию)
- **`CalculateVisuals` мешает СИМ и ПРЕЗЕНТАЦИЮ:** `unitRadius`/`unitHeight` (нужны симуляции — бой/строй/радиусы!), список `renderers` (нужен `FoW.HideRenderers`) — это НЕ презентация. А `animator`, healthbar, партиклы — чистый визуал. **Нельзя слепо скипать весь `CalculateVisuals`** — выяснить точную границу. (Он ещё и не идемпотентен — был баг.)
- `ParticleSystem` **симулируется на CPU без рендера** → главный кандидат (Stop + disable/Destroy). `Animator` — evaluate вхолостую, тоже.
- Healthbar в `CalculateVisuals` создаётся по `currentTeam`; на сервере `currentTeam=-1` → лишний enemy-healthbar. Не создавать/глушить.
- **Где хук** (от этого зависит, нужна ли правка ассета):
  - (а) хелпер `ServerPresentationStripper.Strip(unit)`, зовём из НАШЕГО кода после `Unit.Spawn` (покрывает волны/башни/призывы — наш контент; ConstructionUnit и пр. — нет);
  - (б) компонент-маркер на префабе юнита, self-disable в `Awake` под `IsHeadlessServer` (покрывает ВСЕ спавны, без правки ассета, но вешать на префабы);
  - (в) правка `Unit.SpawnInternal` под `IsHeadlessServer` (один хук на всё, но это **правка ассета — нужно разрешение**).
- Гейт — `ServerBootstrap.IsHeadlessServer` (уже есть).

### Решения до плана
- Хук (а/б/в) — определяет объём правок ассета.
- Что глушить и КАК (disable component / Destroy / SetActive визуального корня).
- **Замер:** профайлинг сервера до/после на сценарии с N юнитами — иначе «увеличит ли перф» недоказуемо. Подготовить такой сценарий.

---

## Остальные мои запросы (из сессии — учесть в общем плане)
- Дотранслитерировать логи `MatchManager` (~46 строк) — промт готов; применить нашим Edit или ИИ Unity.
- **Force Text** сериализация сцен (Artsiom бинарный) — нужно для ревью конфига (пункт 1) и диффов.
- `teamUnitSets` для команды клиента (один набор vs разные) — открыт.
- Подтвердить `ownerPlayer` 0/1 в Inspector — зависимость пунктов 1/2.
- Headless: реальный прогон 2 клиентов, отлов рантайм-NRE в событиях/корутинах.

## Порядок и зависимости
1. **Пункт 1** — фундамент (от резолвера/валидации зависят 2 и часть багов). Первым.
2. **Пункт 2** — после 1 (использует резолвер + owner-гейт).
3. **Перф** — независимый трек, можно параллельно (низкий риск).
4. **Force Text + транслитерация логов** — гигиена, до/параллельно.

## Критерии готовности к написанию implementation-плана
- Прочитан StrategyCore-док (абилки/юниты/сеть) — правило 11.
- Подтверждены: `ownerPlayer` 0/1; уникальность/назначение `Ability.id`; точная граница «сим vs презентация» в `CalculateVisuals`.
- Приняты decision-points каждого пункта (где резолвер; id vs index; где хук strip).
- Желательно: сцена в Force Text — конфиг виден глазами.
