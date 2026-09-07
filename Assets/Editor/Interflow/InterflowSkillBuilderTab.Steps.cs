using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============ ВКЛАДКА «БОЕВЫЕ УМЕНИЯ» — ЧИПЫ, ЛЕНТА, СЕКЦИИ ==
    // Партиал (правило 22). Здесь живёт содержимое карточки выбранного умения:
    // полоса чипов состава, лента исполнения, карточки включённых блоков, секции полей.
    // Подсказки — ТОЛЬКО в tooltip (решение Artsiom 2026-08-16): наводишь курсор — видишь пояснение.
    public static partial class InterflowSkillBuilderTab
    {
        // Держатели сообщений валидатора по блокам — перерисовываются точечно при правке,
        // не пересобирая панель (иначе терялся бы фокус ввода). Ключ "statusEffector" — карточка значка.
        static readonly Dictionary<string, VisualElement> issueHolders = new Dictionary<string, VisualElement>();

        // ======================== ПОЛОСА ЧИПОВ «СОСТАВ УМЕНИЯ» ========================

        /// <summary>
        /// Все 18 блоков одной полосой. Чип — переключатель поля «enabled» блока: включение в один клик,
        /// без разворачивания. Развёрнутые карточки ниже — только у включённых; выключенные всегда здесь.
        /// </summary>
        static void AddComposerChips(SerializedObject so)
        {
            int on = BLOCKS.Count(b => IsEnabled(so, b.field));

            var box = Section($"Состав умения — включено {on} из {BLOCKS.Length}",
                new Color(0.20f, 0.20f, 0.22f),
                "Клик по чипу включает или выключает блок. Карточки ниже разворачиваются только у включённых — " +
                "выключенные не занимают экран, но всегда остаются в этой полосе.");

            var strip = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };

            foreach (var b in BLOCKS)
            {
                var block = b;
                bool enabled = IsEnabled(so, b.field);
                Color accent = b.kind == StepKind.Event ? COL_EVENT : COL_STATE;

                var chip = new UnityEngine.UIElements.Toggle { value = enabled, text = $"{b.order}. {b.title}" };
                chip.tooltip = $"{b.hint}\n\n{(b.perTarget ? "По каждой цели" : "Один раз за каст")} · " +
                               (b.kind == StepKind.Event ? "событие (без длительности)" : "длящееся (висит во времени)");
                chip.style.marginRight = 6;
                chip.style.marginBottom = 2;
                chip.style.opacity = enabled ? 1f : 0.55f;
                chip.style.borderBottomWidth = 2;
                chip.style.borderBottomColor = enabled ? accent : new Color(0.3f, 0.3f, 0.3f);

                chip.RegisterValueChangedCallback(e =>
                {
                    var p = so.FindProperty(block.field)?.FindPropertyRelative("enabled");
                    if (p == null) return;
                    p.boolValue = e.newValue;
                    so.ApplyModifiedProperties();
                    // Состав панели меняется — пересборка отложена на кадр: нельзя сносить элементы
                    // изнутри их же колбэка.
                    rightPanel.schedule.Execute(RebuildRightPanel);
                });

                strip.Add(chip);
            }

            box.Add(strip);
            rightPanel.Add(box);
        }

        // ======================== ЛЕНТА ИСПОЛНЕНИЯ ========================

        /// <summary>
        /// Что и когда произойдёт: шапка — замах → срабатывание → после каста; ниже — включённые блоки
        /// в порядке ApplyEffects, разложенные на «по каждой цели» и «один раз за каст».
        /// </summary>
        static void BuildRibbon()
        {
            if (ribbonHolder == null || selected == null) return;
            ribbonHolder.Clear();

            int lvl = previewLevel;
            float castTime = InterflowAbility.LevelValue(selected.castTime, lvl);
            float cd = InterflowAbility.LevelValue(selected.cooldown, lvl);

            var box = Section("Лента исполнения", new Color(0.19f, 0.20f, 0.24f),
                "Порядок строк — реальный порядок исполнения блоков (ApplyEffects). " +
                "Числа — для выбранного уровня.");

            // --- шапка: три момента каста ---
            var strip = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 5 } };
            strip.Add(Moment("0 с — замах",
                selected.castVFX != null ? selected.castVFX.name : "визуала нет",
                "Визуал замаха играет из точки привязки на кастере. Настройки — в «Шкале каста и презентации»."));
            strip.Add(Moment($"{castTime} с — срабатывание",
                selected.impactVFX != null ? selected.impactVFX.name : "визуала нет",
                "В этот момент исполняются все блоки ниже. Момент задаёт castTime, не анимация."));
            strip.Add(Moment("после каста", $"откат {cd} с",
                "Откат ставит ядро. Цены в мане у умения нет (Б5): у авто-умения срабатывание забирает всю ману носителя."));
            box.Add(strip);

            if (castTime <= 0f && selected.castVFX != null)
                box.Add(IssueRow(new InterflowIssue(InterflowIssueSeverity.Info,
                    "Время каста ноль — замах и удар совпадут в одном кадре.", "лента", null)));

            // --- дорожки ---
            var enabledBlocks = BLOCKS.Where(b => IsEnabledRaw(b.field)).OrderBy(b => b.order).ToList();

            AddLane(box, "По каждой цели — в порядке исполнения",
                enabledBlocks.Where(b => b.perTarget));
            AddLane(box, "Один раз за каст — набор целей не нужен",
                enabledBlocks.Where(b => !b.perTarget));

            if (enabledBlocks.Count == 0)
                box.Add(new Label("Ни один блок не включён — умение ничего не сделает. Включи блоки в полосе состава выше.")
                    { style = { whiteSpace = WhiteSpace.Normal, color = COL_WARN } });

            ribbonHolder.Add(box);
        }

        static void AddLane(VisualElement box, string title, IEnumerable<BlockDesc> blocks)
        {
            var list = blocks.ToList();
            if (list.Count == 0) return;

            box.Add(new Label(title.ToUpper())
                { style = { fontSize = 10, color = new Color(0.55f, 0.55f, 0.55f), marginTop = 4, marginBottom = 1 } });

            foreach (var b in list)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 1 } };
                Color accent = b.kind == StepKind.Event ? COL_EVENT : COL_STATE;

                var no = new Label(b.order.ToString())
                {
                    style = { width = 20, unityTextAlign = TextAnchor.MiddleCenter, color = accent,
                              unityFontStyleAndWeight = FontStyle.Bold, flexShrink = 0 }
                };
                row.Add(no);
                row.Add(new Label(b.title + RibbonValueText(b)) { style = { whiteSpace = WhiteSpace.Normal, flexGrow = 1 } });
                row.Add(new Label(b.kind == StepKind.Event ? "⚡ событие" : "⏳ длящееся")
                    { style = { color = accent, fontSize = 10, flexShrink = 0 } });
                box.Add(row);
            }
        }

        /// <summary>Короткий итог блока для ленты — главные числа выбранного уровня.</summary>
        static string RibbonValueText(BlockDesc b)
        {
            int l = previewLevel;
            var s = selected;
            try
            {
                switch (b.field)
                {
                    case "selfCost": return $" — {InterflowAbility.LevelValue(s.selfCost.flatHp, l):0.#} ХП";
                    case "damage":
                        if (s.damage.entries == null || s.damage.entries.Length == 0) return " — записей нет";
                        return " — " + string.Join(", ", s.damage.entries.Where(e => e != null).Select(e =>
                            $"{InterflowAbility.LevelValue(e.amount, l):0.#} {(e.damageType != null ? e.damageType.name : "БЕЗ ТИПА")}"));
                    case "drain": return $" — {InterflowAbility.LevelValue(s.drain.flat, l):0.#}";
                    case "status":
                    {
                        var parts = new List<string>();
                        float st = InterflowAbility.LevelValue(s.status.stunSeconds, l);
                        float di = InterflowAbility.LevelValue(s.status.disarmSeconds, l);
                        float mu = InterflowAbility.LevelValue(s.status.muteSeconds, l);
                        if (st > 0) parts.Add($"оглушение {st:0.#} с");
                        if (di > 0) parts.Add($"обезоруживание {di:0.#} с");
                        if (mu > 0) parts.Add($"немота {mu:0.#} с");
                        return parts.Count > 0 ? " — " + string.Join(", ", parts) : " — всё по нулям";
                    }
                    case "effectors":
                    {
                        int n = s.effectors.records?.Count(r => r != null && r.effector != null) ?? 0;
                        return n == 0 ? " — записей нет" : $" — записей: {n}";
                    }
                    case "heal": return $" — {InterflowAbility.LevelValue(s.heal.flat, l):0.#}" +
                        (InterflowAbility.LevelValue(s.heal.percentOfMaxHp, l) > 0 ? $" + {InterflowAbility.LevelValue(s.heal.percentOfMaxHp, l):0.#}% макс. ХП" : "");
                    case "buff": return $" — {InterflowAbility.LevelValue(s.buff.duration, l):0.#} с";
                    case "shield": return $" — {InterflowAbility.LevelValue(s.shield.flat, l):0.#} на {InterflowAbility.LevelValue(s.shield.duration, l):0.#} с";
                    case "blind": return $" — шанс {InterflowAbility.LevelValue(s.blind.chance, l):0.##}, {InterflowAbility.LevelValue(s.blind.duration, l):0.#} с";
                    case "summon": return s.summon.prefab != null ? $" — {s.summon.prefab.name}" : " — префаб не задан";
                    case "groundZone": return s.groundZone.zonePrefab != null ? $" — {s.groundZone.zonePrefab.name}" : " — префаб не задан";
                    case "delegateService": return " — " + InterflowEditorUI.EnumLabel(typeof(SkillServerService), s.delegateService.service.ToString());
                }
            }
            catch { /* поле переименовали — лента не должна ронять вкладку, покажем без чисел */ }
            return "";
        }

        static VisualElement Moment(string when, string what, string tooltip)
        {
            var v = new VisualElement
            {
                style = { flexGrow = 1, flexBasis = 0, marginRight = 4,
                          paddingTop = 5, paddingBottom = 5, paddingLeft = 7, paddingRight = 7,
                          backgroundColor = new Color(0.18f, 0.18f, 0.19f),
                          borderTopWidth = 2, borderTopColor = new Color(0.30f, 0.49f, 1f) },
                tooltip = tooltip
            };
            v.Add(new Label(when) { style = { unityFontStyleAndWeight = FontStyle.Bold, whiteSpace = WhiteSpace.Normal } });
            v.Add(new Label(what) { style = { whiteSpace = WhiteSpace.Normal, color = COL_DIM } });
            return v;
        }

        // ======================== КАРТОЧКИ ВКЛЮЧЁННЫХ БЛОКОВ ========================

        static void AddBlockCards(SerializedObject so)
        {
            issueHolders.Clear();

            foreach (var b in BLOCKS.Where(x => IsEnabled(so, x.field)).OrderBy(x => x.order))
                rightPanel.Add(BlockCard(b, so));

            rightPanel.Add(StatusEffectorCard(so));
        }

        static VisualElement BlockCard(BlockDesc b, SerializedObject so)
        {
            Color accent = b.kind == StepKind.Event ? COL_EVENT : COL_STATE;

            var card = new VisualElement
            {
                style = { marginBottom = 4, borderLeftWidth = 3, borderLeftColor = accent,
                          backgroundColor = new Color(0.22f, 0.22f, 0.23f), paddingLeft = 6, paddingRight = 6,
                          paddingTop = 3, paddingBottom = 3 }
            };

            string where = b.perTarget ? "по каждой цели" : "один раз за каст";
            var fold = new Foldout
            {
                text = $"{b.order}.  {b.title}     ({where} · {(b.kind == StepKind.Event ? "⚡ событие" : "⏳ длящееся")})",
                value = true,
                tooltip = b.hint
            };

            var prop = so.FindProperty(b.field);
            // Блок рисуется общим хелпером: массивы по уровням внутри — «базовое + блок» (Б8), остальное — как у Unity.
            if (prop != null) fold.Add(InterflowEditorUI.MakeField(prop, "", true));
            else fold.Add(new Label($"поле «{b.field}» не найдено") { style = { color = COL_WARN } });

            var holder = new VisualElement();
            issueHolders[b.field] = holder;
            FillIssues(b.field, holder);
            fold.Add(holder);

            card.Add(fold);
            return card;
        }

        static VisualElement StatusEffectorCard(SerializedObject so)
        {
            var card = new VisualElement
            {
                style = { marginBottom = 4, borderLeftWidth = 3, borderLeftColor = COL_STATE,
                          backgroundColor = new Color(0.22f, 0.22f, 0.23f), paddingLeft = 6, paddingRight = 6,
                          paddingTop = 3, paddingBottom = 3 }
            };
            var fold = new Foldout
            {
                text = "Значок состояния",
                value = selected.statusEffector != null,
                tooltip = "Умение своего значка не имеет: панель над юнитом собирается из состояний, " +
                          "поэтому носитель значка — отдельное состояние (ассет Effector). " +
                          "Значок виден только у ненакапливаемых состояний с заданной иконкой."
            };

            var prop = so.FindProperty("statusEffector");
            if (prop != null) fold.Add(new PropertyField(prop, "Состояние-значок"));

            var eff = selected.statusEffector;
            if (eff != null)
            {
                bool visible = !eff.stacks && eff.icon != null;
                fold.Add(new Label(visible
                        ? "Значок будет виден в панели состояний"
                        : eff.stacks ? "Значок НЕ будет виден: у состояния включён Stacks (накопление)"
                                     : "Значок НЕ будет виден: у состояния не задана иконка")
                    { style = { whiteSpace = WhiteSpace.Normal, color = visible ? COL_OK : COL_WARN } });
            }

            var holder = new VisualElement();
            issueHolders["statusEffector"] = holder;
            FillIssues("statusEffector", holder);
            fold.Add(holder);

            card.Add(fold);
            return card;
        }

        static void FillIssues(string field, VisualElement holder)
        {
            holder.Clear();
            if (field == "statusEffector")
            {
                foreach (var i in IssuesOf(selected).Where(IsBadgeIssue))
                    holder.Add(IssueRow(i));
                return;
            }

            foreach (var i in IssuesOf(selected).Where(x => !IsBadgeIssue(x) && BlockFor(x)?.field == field))
                holder.Add(IssueRow(i));
        }

        /// <summary>Включён ли блок — через ОБЩИЙ SerializedObject карточки (а не свой на каждый вызов).</summary>
        static bool IsEnabled(SerializedObject so, string field)
        {
            var e = so.FindProperty(field)?.FindPropertyRelative("enabled");
            return e != null && e.boolValue;
        }

        // Для ленты: SerializedObject недоступен (перерисовка вне пересборки панели) — читаем напрямую.
        static bool IsEnabledRaw(string field)
        {
            var so = new SerializedObject(selected);
            return IsEnabled(so, field);
        }

        // ======================== ЦЕЛЬ И ДОСТАВКА ========================

        // Показывать ли поля, которые текущий режим не читает (решение 14.08: по умолчанию скрыты,
        // возврат — кнопкой «показать»; правило 7 соблюдено — безвозвратно ничего не прячется).
        static bool showHiddenAim;

        static void AddAimSection(SerializedObject so)
        {
            var box = Section("Цель и доставка", new Color(0.24f, 0.24f, 0.26f),
                "Радиус и селектор целей нужны только блокам, которые применяются К ЦЕЛИ. " +
                "Призыв, зона и серверный сервис исполняются один раз за каст — им набор целей не требуется.");

            box.Add(AimExplanation());

            // Поле «Когда срабатывает» (trigger) снесено блоком Б7 (2026-09-05): каждое умение — разовый каст.
            AddField(box, so, "buttonCast");
            AddField(box, so, "targetMode");

            var hidden = new List<string>();

            // Стратегия и её параметры читаются ТОЛЬКО в режимах «умный выбор» (предикат — из самого умения).
            AddField(box, so, "autoCastSelfHpBelow");   // условие по своему здоровью — работает во всех режимах цели

            var strategyFields = new[] { "targetStrategy", "searchOrigin", "strategyUseCurrentHealth", "strategyHpThreshold" };
            if (selected.PicksTargetByStrategy) foreach (var f in strategyFields) AddField(box, so, f);
            else hidden.AddRange(strategyFields);

            if (selected.targetMode == SkillTargetMode.Cone) AddField(box, so, "coneAngle");
            else hidden.Add("coneAngle");

            AddField(box, so, "directionMatters");

            foreach (var f in new[] { "unitSelector", "targetCategories", "maxTargets", "multiPick",
                                      "includeSelf", "radius", "castRange" })
                AddField(box, so, f);

            AddField(box, so, "delivery");

            var projectileFields = new[] { "projectilePrefab", "projectileFollowsTarget" };
            if (selected.delivery == SkillDelivery.Projectile) foreach (var f in projectileFields) AddField(box, so, f);
            else hidden.AddRange(projectileFields);

            // Строка возврата скрытых полей.
            if (hidden.Count > 0)
            {
                if (showHiddenAim)
                {
                    box.Add(new Label("— скрытые поля этого режима (ядро их сейчас не читает) —")
                        { style = { color = COL_DIM, fontSize = 10, marginTop = 4 } });
                    foreach (var f in hidden)
                    {
                        var pf = InterflowEditorUI.MakeField(so.FindProperty(f), InterflowEditorUI.FieldLabel(f), false);
                        pf.style.opacity = 0.5f;
                        box.Add(pf);
                    }
                }

                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 3 } };
                row.Add(new Label($"Скрыто полей, которые этот режим не читает: {hidden.Count}")
                    { style = { color = COL_DIM, fontSize = 10, flexGrow = 1 } });
                var toggleBtn = new Button(() => { showHiddenAim = !showHiddenAim; RebuildRightPanel(); })
                    { text = showHiddenAim ? "скрыть" : "показать" };
                toggleBtn.tooltip = "Поля не удаляются и не теряют значений — они просто не читаются в текущем режиме.";
                row.Add(toggleBtn);
                box.Add(row);
            }

            // Правка этих полей меняет СОСТАВ секции — пересобрать панель на следующем кадре.
            RebuildOnChange(box, so, "targetMode");
            RebuildOnChange(box, so, "delivery");

            rightPanel.Add(box);
        }

        /// <summary>Сводка режима: что умение сделает. Сверено по CompositeSkill.CollectTargets.</summary>
        static VisualElement AimExplanation()
        {
            int lvl = previewLevel;
            float r = InterflowAbility.LevelValue(selected.radius, lvl);
            string self = selected.includeSelf ? "включая кастера" : "кроме кастера";
            string strategy = InterflowEditorUI.EnumLabel(typeof(SkillTargetStrategy), selected.targetStrategy.ToString());

            string what;
            switch (selected.targetMode)
            {
                case SkillTargetMode.Self:
                    what = "Заденет только самого кастера.";
                    break;

                case SkillTargetMode.WholeTeam:
                    what = "Заденет всех боевых юнитов своей команды, " + self +
                           ". Замок, башни и не подчиняющиеся приказам призванные в список не входят.";
                    break;

                case SkillTargetMode.AreaAroundSelf:
                    what = r > 0f
                        ? "Заденет всех в радиусе " + r + " вокруг кастера, кто проходит селектор и фильтры, " + self + "."
                        : "Радиус ноль — целей НЕ БУДЕТ. Блоки по целям не сработают; призыв, зона и сервис — сработают.";
                    break;

                case SkillTargetMode.Cone:
                    what = "Заденет всех в конусе " + selected.coneAngle + "° перед кастером в радиусе " + r + ", " + self + ".";
                    if (r <= 0f) what += " Радиус ноль — целей не будет.";
                    break;

                case SkillTargetMode.SmartUnit:
                    what = "Заденет ОДНУ цель, выбранную стратегией «" + strategy + "».";
                    break;

                default: // SmartPoint
                    what = r > 0f
                        ? "Стратегия «" + strategy + "» выбирает точку; заденет всех в радиусе " + r + " вокруг неё, " + self + "."
                        : "Радиус ноль — целей НЕ БУДЕТ.";
                    break;
            }

            var box = new VisualElement
            {
                style = { marginBottom = 6, paddingTop = 4, paddingBottom = 4, paddingLeft = 7, paddingRight = 7,
                          backgroundColor = new Color(0.19f, 0.22f, 0.19f),
                          borderLeftWidth = 3, borderLeftColor = COL_OK }
            };
            box.Add(new Label(what) { style = { whiteSpace = WhiteSpace.Normal } });
            return box;
        }

        // ======================== СЕКЦИИ ПОЛЕЙ ========================

        /// <summary>Что игрок видит: имя, описание, иконка, ячейка панели. Плюс сам id.</summary>
        static void AddIdentitySection(SerializedObject so)
        {
            var box = Section("Подпись и иконка", new Color(0.24f, 0.24f, 0.26f),
                "Название, описание и иконка — массивы по уровням: заполнен один элемент — " +
                "он используется для всех уровней.");

            foreach (var f in new[] { "id", "abilityName", "description", "icon", "slotNumber" })
                AddField(box, so, f);

            rightPanel.Add(box);
        }

        /// <summary>Шкала каста и презентация: тайминги, точки привязки, визуал, звук, анимация.</summary>
        static void AddPresentationSection(SerializedObject so)
        {
            var fold = new Foldout
            {
                text = "Шкала каста и презентация",
                value = false,
                style = { marginTop = 6 },
                tooltip = "Тайминги каста и всё, что видно и слышно. Точка привязки — точка НА КАСТЕРЕ " +
                          "(компонент CharacterSockets на префабе; нет компонента — центр объекта). " +
                          "Моменты замаха и срабатывания показаны в шапке ленты исполнения выше."
            };

            foreach (var f in new[] { "castTime", "cooldown", "duration",
                                      "spawnSocket", "localOffset",
                                      "castVFX", "castVfxLifetime", "impactVFX", "impactVfxLifetime",
                                      "castSound", "impactSound", "soundVolume", "procAnimationState" })
                AddField(fold, so, f);

            rightPanel.Add(fold);
        }

        /// <summary>Когда умение открывается и сколько стоит.</summary>
        static void AddRequirementsSection(SerializedObject so)
        {
            var box = Section("Условия открытия и цена", new Color(0.22f, 0.24f, 0.22f),
                "Пусто в требованиях — умение открыто сразу, как только попало юниту в abilities[]. " +
                "Замки считает ядро, руками включать ничего не надо.");

            foreach (var f in new[] { "requiredTech", "cost" })   // requiredLevel снесён блоком Б8
                AddField(box, so, f);

            rightPanel.Add(box);
        }

        /// <summary>Предмет и редкие флаги — свёрнуто: нужны единицам умений, но доступны (правило 7).
        /// Предметы в игре не используются (блок Б12, целевая модель §13) — поля оставлены, но не развиваются.</summary>
        static void AddMiscFoldout(SerializedObject so)
        {
            var fold = new Foldout
            {
                text = "Прочие поля умения",
                value = false,
                style = { marginTop = 6 },
                tooltip = "Предмет, заряды и редкие флаги. ПРЕДМЕТЫ И ИНВЕНТАРЬ В ИГРЕ НЕ ИСПОЛЬЗУЮТСЯ (целевая модель §13): " +
                          "поля работают, но их не развивают и в проверках контента не учитывают — заполнять не нужно. " +
                          "Переключателей, умений-каналов (Б6) и режима «аура» (Б7) больше нет: каждое умение — разовый каст, " +
                          "длительность — полем «Длительность»."
            };

            foreach (var f in new[] { "maxLevels", "heroLevelable",
                                      "isItem", "useUponPickUp", "dropOnDeath", "charges",
                                      "showRadiusCircle", "dontTurn" })
                AddField(fold, so, f);

            rightPanel.Add(fold);
        }

        // ======================== ОБЩЕЕ ========================

        static VisualElement Section(string title, Color bg, string tooltip)
        {
            var box = new VisualElement
            {
                style = { marginTop = 8, marginBottom = 2, paddingTop = 6, paddingBottom = 6,
                          paddingLeft = 8, paddingRight = 8, backgroundColor = bg,
                          borderTopLeftRadius = 3, borderTopRightRadius = 3,
                          borderBottomLeftRadius = 3, borderBottomRightRadius = 3 }
            };
            var head = new Label(title)
                { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } };
            if (!string.IsNullOrEmpty(tooltip)) head.tooltip = tooltip;
            box.Add(head);
            return box;
        }

        static void AddField(VisualElement parent, SerializedObject so, string field)
        {
            var p = so.FindProperty(field);
            if (p == null) return;

            // Ц1: подпись — из общего словаря (правило 5); null — остаётся подпись Unity.
            // Декоратор прячем только у cooldown: там [Header("Parameters")] базового Ability —
            // единственная английская надпись в этой вкладке, а перевести её — правка ядра.
            parent.Add(InterflowEditorUI.MakeField(p, InterflowEditorUI.FieldLabel(field), field == "cooldown"));
        }

        /// <summary>
        /// Поле, правка которого меняет состав панели: пересобрать НА СЛЕДУЮЩЕМ кадре.
        /// Прямой вызов из колбэка снёс бы элемент, внутри которого мы сейчас находимся.
        /// </summary>
        static void RebuildOnChange(VisualElement scope, SerializedObject so, string field)
        {
            var p = so.FindProperty(field);
            if (p == null || rightPanel == null) return;
            scope.TrackPropertyValue(p, _ => rightPanel.schedule.Execute(RebuildRightPanel));
        }

        /// <summary>
        /// ЕДИНСТВЕННАЯ подписка на правки карточки (раньше их было семь, и каждая гоняла полную
        /// перепроверку с чтением всего проекта). Правка любого поля: точечная перепроверка по кэшу
        /// контекста, обновление сообщений у блоков, сводки, бейджа, ленты и точки в списке.
        /// Сама панель не пересобирается — фокус ввода не теряется.
        /// </summary>
        static void TrackEditsOnce(SerializedObject so)
        {
            // Трекер — на СВЕЖЕМ дочернем элементе: rightPanel живёт между пересборками,
            // и повторный TrackSerializedObjectValue на нём с новым SerializedObject кидает
            // NotSupportedException «only one serializedObject at a time» (ловилось кликом по чипу).
            var tracker = new VisualElement();
            rightPanel.Add(tracker);
            tracker.TrackSerializedObjectValue(so, _ =>
            {
                RevalidateSelected();

                foreach (var kv in issueHolders)
                    if (kv.Value != null) FillIssues(kv.Key, kv.Value);

                FillGeneralIssues();
                if (summaryLabel != null) summaryLabel.text = selected != null ? selected.BuildSummary() : "";
                UpdateBadge();
                BuildRibbon();
                RebuildList();
            });
        }
    }
}
