# Implementation-prep: пункты 1, 2 и серверный strip-presentation

Дата: 2026-06-21. Статус: подготовка к implementation-плану (код НЕ менялся).
Рамки: правила 1 (не трогать ядро ассета), 5 (единый источник), 6 (серверо-авторитет), 8 (критичность).

> ⚠️ Ограничение сессии: Grep/Glob/bash были недоступны (sandbox не примонтирован), `D:\` читался только точными путями через Read. Прочитана часть кода; нечитанное вынесено в раздел «Не дочитано». Сцена `Artsiom.unity` бинарная → значения `ownerPlayer` статикой не достаются.

---

## A. Что подтверждено в КОДЕ этой сессией (факты)

Путь скриптов: `Assets/StrategyCore/Scripts/` (физически `D:\Interflow\Interflow\Assets\StrategyCore\Scripts\`).

1. **MatchManager.AssignCasterAbilities** (`MatchManager.cs:194-200`): `cfg.abilityCaster.abilities = cfg.centralAbilities.ToArray()` (или `new Ability[0]`). Зовётся в `Awake` для teamA/teamB (`:188-189`) на всех пирах. → массив `abilities[]` кастера детерминирован из Inspector, индексы каста совпадают на сервере и клиенте.
2. **MatchManager.Start** (`:210-226`): `if (isClient) return;` → `ValidateSetup()` (`:214`) → подписка башен → `StartCoroutine(WaveLoop())`. Точка для расширения валидации — серверная, до старта волн.
3. **Ability.id** (`Abilities/Ability.cs:26`): `[AbilityID] public int id;`. Атрибут `[AbilityID]` ⇒ id назначается инструментом/дровером (аналог `unitTypeID` с «Assign Unit IDs»). Это стабильный ключ для каста по идентичности. **Проверено (`CustomDrawers/AbilityDrawer.cs`):** `[AbilityID]` — read-only дровер; при `id==0` или дубле зовёт `GetUniqueID` → случайный `[1..99999]` со сверкой по всем `t:Ability` (AssetDatabase). Назначение ЛЕНИВОЕ (в момент отрисовки инспектора ассета); отдельной тулзы «Assign Ability IDs» нет (в отличие от `unitTypeID`). Тот же дровер у VFXLine/Technology/Projectile/Effector/Damage/Armor. ⚠️ Риск: ассет, чей инспектор ни разу не открывали, может иметь `id==0` → ломает каст по id. Митигейшн: в `ValidateSetup` проверить, что у всех `centralAbilities` `id!=0` и нет дублей.
4. **Unit.Initialize** (`Unit.cs:451-568`) — граница sim/презентация (важно для strip):
   - Healthbar создаётся ЗДЕСЬ (`:467-472`), по `team != currentTeam && team != NeutralPassive` → `healthBarEnemy`, иначе `healthBar`. **НЕ в `CalculateVisuals`** — поправка к prep-плану.
   - MiniMapIcon (`:474-477`), VFXHolder (`:479-484`) — тоже в Initialize.
   - `AddColliders()` (`:499`), затем `CalculateVisuals()` (`:502` — «renderers + per-unit material + player/overlay color»), затем `matBlock/SetPlayerColor/ResetOverlayColor` (`:505-507`).
   - `unitRadius`/`unitHeight` нужны симуляции: melee/attackRange (`:490-493`), NavMeshAgent.radius/height (`:543-544`).
5. **SlotManager.SetCurrentPlayer** (`SlotManager.cs:503-508`): БЕЗ guard — `currentPlayer=slot; currentTeam=playerTeam[slot]; currentName=playerName[slot]`. При `slot=-1` → IndexOutOfRange. **Risk #2 подтверждён.** Цепь резолва: `slot → playerTeam[slot] = currentTeam`.

---

## B. Пункт 1 — единый валидируемый резолвер идентичности

**Decision (где живёт):** метод/свойства на `MatchManager` — он синглтон на всех пирах, уже держит `Team(index)`, `TeamIndexOfOwner`, `ownerPlayer`. Наш партиал `MatchManager.Identity.cs` (не плодить компонент, правило 5). Альтернатива (статик-хелпер) — хуже: дублирует доступ к синглтонам.

**Свести в один источник (правило 5):** `UIManager.BottomTables.CommandTeamForLocalPlayer`, `WaveBuilderUI.LocalPlayerTeam`, дамп `NetworkFlowLogger.LogOwnershipState` — все три зовут резолвер.

**Контракт:**
- Маппинг: `slot ↔ SlotManager.playerTeam[slot]` (команда ассета) ↔ `MatchManager` teamIndex 0/1 через `ownerPlayer` (= слот). НЕ путать `playerTeam` (ассет) и teamIndex 0/1.
- Готовность: пока `currentPlayer` не ре-синкнут на клиенте — возвращать «не готово» (-1), НЕ дефолтить в 0 (иначе ранний UI берёт чужую команду).
- Guard `-1` держать в резолвере (наша сторона), `SetCurrentPlayer` НЕ вызывать с -1 → ассет (SlotManager) не трогаем (правило 1). Правку самого `SetCurrentPlayer` (Risk #2) делать только если потребуется отдельно и с разрешения.

**Валидация — расширить `ValidateSetup` (`MatchManager.cs`, зовётся в Start):**
- `ownerPlayer ∈ {0,1}` и `teamA.ownerPlayer != teamB.ownerPlayer`.
- кастеры заданы и различны; `abilityCaster.owner == ownerPlayer`.
- `centralAbilities` не пусты (если так задумано).
- При провале: `Debug.LogError` + НЕ запускать `WaveLoop` (блок старта матча).

**Файлы:** новый `MatchManager.Identity.cs` (партиал) + расширение `ValidateSetup`; правки-вызовы в `UIManager.BottomTables` (партиал), `WaveBuilderUI`, `NetworkFlowLogger`.

**Open:** N команд/игроков (сейчас жёстко 2/1) — оставить MVP-2, не обобщать без указания. `ownerPlayer` 0/1 — проверить в Inspector (бинарь).

---

## C. Пункт 2 — request-слой «клиент просит — сервер исполняет» + каст по идентичности

**Образец паттерна:** `NetworkDataSync.TeamCommand.cs` — `TeamCommandServerRpc(teamIndex, action)` с owner-гейтом `GetClientSlot(sender) == Team.ownerPlayer`. Наши партиалы `WaveComposition.cs` / `PointSync.cs` — тот же стиль.

**Decision (общий слой):** обобщить TeamCommand / WaveEdit / каст в ОДИН паттерн — `teamIndex` + owner-гейт в одном месте (правило 5), а не дубль на каждую фичу.

**Decision (каст — id vs index):**
- Index уже работает: `AssignCasterAbilities` делает массивы байт-в-байт одинаковыми на пирах (подтверждено в A.1). Текущая NRE `CheckAbilityItemRequirements` — следствие рассинхрона массивов; при детерминированном Assign её быть не должно.
- **Рекомендация: id-на-проводе.** Клиент шлёт `Ability.id` → сервер мапит `id→index` в `caster.abilities[]` и зовёт штатный `UseAbilityItem(index)`. Убирает требование «массивы идентичны» как инвариант надёжности. Ядро `Unit.Ability` НЕ трогаем — меняем только наши RPC/UI (формат на проводе).
- Резолв `id→index` обязан использовать ту же логику глобального индекса, что `Utils.GetAbilityByIndex`/`GetAbilityIndex` (контейнеры) — иначе рассинхрон.

**Файлы:** новый партиал `NetworkDataSync.AbilityRequest.cs` (по образцу TeamCommand); правка `UIManager.BottomTables.ActivateAbilityCell` (слать id вместо/вместе с index).

**Open / зависит от «Не дочитано»:** тело `UseAbilityItem`, `CheckAbilityItemRequirements`, `GetAbilityIndex/GetAbilityByIndex`; уникальность `Ability.id` (дровер `[AbilityID]`).

---

## D. Strip-presentation на сервере (headless)

**Граница sim vs презентация (по прочитанному `Unit.Initialize`):**

| Презентация (глушить на headless) | Симуляция (НЕ трогать) |
|---|---|
| Healthbar (`Unit.cs:467-472`) | `unitRadius`/`unitHeight` (melee `:490-493`, agent `:543-544`) |
| MiniMapIcon (`:474-477`) | Colliders `AddColliders()` (`:499`) |
| VFXHolder (`:479-484`) | NavMeshAgent/Obstacle (`:533-567`) |
| renderers/matBlock/player+overlay color (`CalculateVisuals` @ `:502`, `:505-507`) | `AttackSelectorInitialize` (`:487`) |
| animator, launchVFX/ParticleSystem | |

**⚠️ Критично (поправки prep-плана):**
- Healthbar/minimap/vfxholder создаются в `Initialize` (ассет), НЕ в нашем коде и НЕ в `CalculateVisuals`. Значит «не создавать» = правка ассета; «снести после Initialize» = наш код (предпочтительно, правило 1).
- `CalculateVisuals` (@ `:502`) по вики **домножает `unitRadius`/`unitHeight` на lossyScale** и **не идемпотентен** (дублирует `attackVFXLine`). Это sim-побочка ВНУТРИ презентационного метода → **нельзя слепо скипать весь `CalculateVisuals`**. Нужна точная граница из тела метода (не дочитано).
- `ParticleSystem` симулируется на CPU без рендера, `Animator` evaluate вхолостую — главные кандидаты на Stop+disable.
- На сервере `currentTeam = -1` → ветка healthbar даёт лишний enemy-healthbar.

**Хук (a/b/в):**
- (a) `ServerPresentationStripper.Strip(unit)` после `Unit.Spawn` в нашем коде — покрывает волны/башни/призывы (наш контент), НЕ покрывает `ConstructionUnit`/faction-spawn.
- (b) компонент-маркер на префабе, self-strip под `IsHeadlessServer` — покрывает ВСЕ спавны, 0 правок ассета, но вешать на префабы; реально = «снести презентацию ПОСЛЕ Initialize» (healthbar и пр. создаются в Initialize по OnGameStart, позже Awake).
- (в) правка `Unit.SpawnInternal` под `IsHeadlessServer` — один хук, но правка ассета (разрешение).
- **Рекомендация: (b)**, гейт `ServerBootstrap.IsHeadlessServer` (уже есть). Снос после Initialize безопаснее «не-создания» и не идемпотентность-зависим; не перевызывать `CalculateVisuals`.

**Замер (обязательно):** профайлинг сервера до/после на headless-сценарии с N юнитами — иначе ускорение недоказуемо. Подготовить сценарий массового спавна.

---

## E. Не дочитано этой сессии (вход для следующей)

Причина: Grep/Glob/bash недоступны; Read — только точные пути; часть путей не угадана (напр. `Unit.Ability.cs` не открылся по `Scripts/Unit.Ability.cs` — уточнить расположение партиала).

- `Unit.cs` — тело `CalculateVisuals` (точная граница lossyScale/renderers/animator/attackVFXLine). КРИТИЧНО для strip.
- `Unit.Ability.cs` — `UseAbilityItem` (~l.54), `CheckAbilityItemRequirements` (~l.1001), `InitializeAbilities` (~l.567).
- `Utils` — `GetAbilityIndex`/`GetAbilityByIndex` (глобальный индекс с контейнерами).
- `MatchManager` — тела `TeamIndexOfOwner` (~l.426), `ValidateSetup` (~l.1205).
- `NetworkCommandSync.cs` — per-unit ServerRpc + owner-гейт (образец).
- Дровер `[AbilityID]` — уникальность/назначение `id`.

---

## F. Порядок и открытые вопросы

**Порядок (как в prep-плане):** 1 (фундамент) → 2 (использует резолвер+гейт) → strip (независимо, параллельно). Force Text + транслитерация логов `MatchManager` — гигиена, до/параллельно.

**Открытые вопросы (НЕ додумывал — спросить/проверить в Unity):**
- ✅ `teamA/teamB.ownerPlayer` = 0/1 — ПОДТВЕРЖДЕНО (пользователь, 2026-06-21).
- ✅ `Ability.id` уникальны — ПОДТВЕРЖДЕНО (`AbilityDrawer` авто-дедуп), но лениво → добавить guard `id!=0`/без дублей в `ValidateSetup` (см. C).
- `teamUnitSets` для команды клиента — один набор или разные?
- Force Text для `Artsiom.unity` — включать (для ревью конфига и диффов)?
- Каст: финально id-на-проводе (рекоменд.) или оставить index?
- Хук strip: финально (b)?

---

## G. Дочитано (поиск заработал) + вердикт готовности

> Поправка: Grep/Glob работают по host-путям — раньше был неверный путь (`D:\Interflow\Assets` вместо `D:\Interflow\Interflow\Assets`), а не «sandbox недоступен». Раздел E закрыт.

**Дочитанные тела:**
- `CalculateVisuals` (`Unit.cs:725-834+`): renderers/meshRenderers (730-747); `attackVFXLine` для continuous-дальников (763) — НЕидемпотентный дубль; **`unitRadius/unitHeight *= lossyScale.x` (778-779) — СИМ, лежит В СЕРЕДИНЕ метода**; animator (784-834) — считает длины idle/attack/death и подписывает `RandomIdleAnimation` на `Tick` (797). ⇒ **strip нельзя свести к «скип CalculateVisuals»**: масштабирование радиуса обязано выполниться, а длины attack/death-анимаций могут использоваться боевым таймингом сервера (проверить `animationAttackDelay`/`attackAnimationLength`).
- `TeamIndexOfOwner` (`MatchManager.cs:406-411`): `ownerPlayer==player → 0/1, иначе -1`. Уже корректный owner→teamIndex (НЕ дефолтит в 0). Резолвер п1 строится на нём.
- `ValidateSetup` (`:1187-1222`): сейчас только warning'и (spawnPoint/lane/ресурсы/rebuildablePoints/specificUnitsDead), БЕЗ проверки ownerPlayer/кастеров/`Ability.id`. Место для guard'ов п1/п2 подтверждено. Строки латиницей — это и есть «дотранслитерировать логи».
- `UseAbilityItem`/`AddProcess` (`:4465+`): на клиенте шлёт `abilityIndex` через `NetworkCommandSync` (ядро, индексный провод), на сервере индексирует `abilities[abilityIndex]`. ⇒ для id-на-проводе НЕ маршрутизировать через клиентский `UseAbilityItem`; наш ServerRpc несёт `id` → сервер мапит `id→index` по СВОЕМУ `caster.abilities` → `UseAbilityItem(index)` на сервере. Индекс всегда валиден → NRE `CheckAbilityItemRequirements` снимается.
- Каст уже частично реализован: `UIManager.BottomTables.cs:436-447` (`GetAbilityIndex` + обход бага ассета обратной сверкой `GetAbilityByIndex`).

**Вердикт готовности:**
- Пункт 1 (резолвер на `TeamIndexOfOwner` + guard'ы в `ValidateSetup`) — ✅ ГОТОВ к implementation-плану.
- Пункт 2 (общий request-слой + каст id-на-проводе) — ✅ ГОТОВ (путь подтверждён, NRE устраняется).
- Strip-presentation — 🟡 граница известна точно, но НЕ чистый 80/20: нужно 1 дизайн-решение (что и когда рвём) + проверить зависимость боевого тайминга сервера от длин анимаций. До этого хук (b) сам по себе недостаточен.
