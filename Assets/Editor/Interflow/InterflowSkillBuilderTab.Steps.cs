using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============ ВКЛАДКА «КОНСТРУКТОР СКИЛЛОВ» — ЦЕЛЬ, ШКАЛА КАСТА, ДВА СПИСКА ==
    // Партиал (правило 22). Здесь живёт всё, что рисует содержимое выбранного скилла:
    // адресация, шкала каста, списки «события» и «состояния», валидатор по месту, уровни.
    public static partial class InterflowSkillBuilderTab
    {
        // Держатели сообщений валидатора по блокам — чтобы перерисовывать их точечно,
        // не пересобирая панель целиком (иначе при правке поля терялся бы фокус ввода).
        static readonly Dictionary<string, VisualElement> issueHolders = new Dictionary<string, VisualElement>();

        // ======================== ПОДПИСЬ, ТРЕБОВАНИЯ, ПРОЧЕЕ ========================
        // Штатные поля Ability, которых у вкладки раньше не было: их правили в «Умениях и эффекторах».
        // По решению Artsiom 2026-08-09 все поля умения живут в конструкторе — старая вкладка
        // становится «Эффекторами». Правило 7: ничего не прячем безвозвратно, редкое — в свёрнутом фолде.

        /// <summary>Что игрок видит: имя, описание, иконка, ячейка панели. Плюс сам id.</summary>
        static void AddIdentitySection()
        {
            var so = new SerializedObject(selected);
            var box = Section("Подпись и иконка", new Color(0.24f, 0.24f, 0.26f));

            foreach (var f in new[] { "id", "abilityName", "description", "icon", "slotNumber" })
                AddField(box, so, f);

            box.Add(Hint("Название, описание и иконка — массивы по уровням: заполнен один элемент — " +
                         "он используется для всех уровней."));

            box.Bind(so);
            TrackEdits(box, so);
            rightPanel.Add(box);
        }

        /// <summary>Когда умение открывается и сколько стоит.</summary>
        static void AddRequirementsSection()
        {
            var so = new SerializedObject(selected);
            var box = Section("Условия открытия и цена", new Color(0.22f, 0.24f, 0.22f));

            foreach (var f in new[] { "requiredTech", "requiredLevel", "cost", "manaCostPerSecond" })
                AddField(box, so, f);

            box.Add(Hint("Пусто в требованиях — умение открыто сразу, как только попало юниту в abilities[]. " +
                         "Замки считает ядро, руками включать ничего не надо."));

            box.Bind(so);
            TrackEdits(box, so);
            rightPanel.Add(box);
        }

        /// <summary>Предмет, уровни и редкие флаги — свёрнуто: нужны единицам умений, но доступны.</summary>
        static void AddMiscFoldout()
        {
            var so = new SerializedObject(selected);
            var fold = new Foldout { text = "Прочие поля умения", value = false, style = { marginTop = 8 } };

            foreach (var f in new[] { "maxLevels", "heroLevelable",
                                      "isItem", "useUponPickUp", "dropOnDeath", "charges",
                                      "showRadiusCircle", "dontTurn",
                                      "continuous", "interruptible", "requiresCastingUnit" })
                AddField(fold, so, f);

            fold.Add(Hint("«Канал» — это штатный флаг continuous: умение держится, пока хватает маны. " +
                          "Переключатель и аура задаются полем «Срабатывание» выше, а не здесь."));

            fold.Bind(so);
            TrackEdits(fold, so);
            rightPanel.Add(fold);
        }

        // ======================== ЦЕЛЬ И ДОСТАВКА ========================

        static void AddAimSection()
        {
            var so = new SerializedObject(selected);
            var box = Section("Цель и доставка", new Color(0.24f, 0.24f, 0.26f));

            // Ц4: две строки простыми словами — кого заденет и что в этом режиме не читается.
            box.Add(AimExplanation());

            AddField(box, so, "trigger");
            AddField(box, so, "targetMode");
            AddField(box, so, "buttonCast");

            // Ц2: стратегия и её параметры читаются ТОЛЬКО в режимах «умный выбор».
            // Предикат берём из самого скилла (`PicksTargetByStrategy`), а не повторяем условие —
            // тот же предикат читают рантайм и автокаст (правила 2 и 5).
            if (selected.PicksTargetByStrategy)
                foreach (var f in new[] { "targetStrategy", "searchOrigin",
                                          "strategyUseCurrentHealth", "strategyHpThreshold" })
                    AddField(box, so, f);

            // Ц2: угол конуса читается только в режиме «конус» (CollectTargets: fullCircle).
            if (selected.targetMode == SkillTargetMode.Cone) AddField(box, so, "coneAngle");

            // Направление показываем всегда: от него зависит доворот кастера, а не только конус.
            AddField(box, so, "directionMatters");

            // Два селектора умения идут рядом: принадлежность (unitSelector) и боевые роли (targetCategories).
            foreach (var f in new[] { "unitSelector", "targetCategories", "maxTargets", "multiPick",
                                      "includeSelf", "radius", "castRange" })
                AddField(box, so, f);

            box.Add(Hint("Радиус и селектор целей нужны только тем блокам, что применяются К ЦЕЛИ. " +
                         "Призыв, зона и серверный сервис исполняются один раз за каст — им набор целей не требуется."));

            AddField(box, so, "delivery");

            // Ц2: поля снаряда нужны только при доставке снарядом.
            if (selected.delivery == SkillDelivery.Projectile)
            {
                AddField(box, so, "projectilePrefab");
                AddField(box, so, "projectileFollowsTarget");
                box.Add(Hint("Штатный снаряд несёт только урон и оглушение. Всё остальное срабатывает в момент каста, " +
                             "а не при попадании."));
            }

            box.Bind(so);
            TrackEdits(box, so);

            // Правка этих двух полей меняет СОСТАВ секции — панель пересобирается. Фокус терять не жалко:
            // оба — выпадающие списки, а не ввод числа. Пересборка отложена на кадр: нельзя сносить элементы
            // изнутри их же колбэка.
            RebuildOnChange(box, so, "targetMode");
            RebuildOnChange(box, so, "delivery");

            rightPanel.Add(box);
        }

        /// <summary>Ц4: что режим цели сделает и что в нём не читается. Сверено по CompositeSkill.CollectTargets.</summary>
        static VisualElement AimExplanation()
        {
            int lvl = previewLevel;
            float r = InterflowAbility.LevelValue(selected.radius, lvl);
            string self = selected.includeSelf ? "включая кастера" : "кроме кастера";
            string strategy = InterflowEditorUI.EnumLabel(typeof(SkillTargetStrategy), selected.targetStrategy.ToString());

            string what, unused;
            switch (selected.targetMode)
            {
                case SkillTargetMode.Self:
                    what = "Заденет только самого кастера.";
                    unused = "радиус, селектор целей, фильтры ролей, лимит целей, угол конуса";
                    break;

                case SkillTargetMode.WholeTeam:
                    what = "Заденет всех боевых юнитов своей команды, " + self +
                           ". Замок, башни и не подчиняющиеся приказам призванные в список не входят.";
                    unused = "радиус, селектор целей, угол конуса";
                    break;

                case SkillTargetMode.AreaAroundSelf:
                    what = r > 0f
                        ? "Заденет всех в радиусе " + r + " вокруг кастера, кто проходит селектор и фильтры, " + self + "."
                        : "Радиус ноль — целей НЕ БУДЕТ. Блоки по целям не сработают; призыв, зона и сервис — сработают.";
                    unused = "угол конуса";
                    break;

                case SkillTargetMode.Cone:
                    what = "Заденет всех в конусе " + selected.coneAngle + "° перед кастером в радиусе " + r + ", " + self + ".";
                    if (r <= 0f) what += " Радиус ноль — целей не будет.";
                    unused = "—";
                    break;

                case SkillTargetMode.SmartUnit:
                    what = "Заденет ОДНУ цель, выбранную стратегией «" + strategy + "».";
                    unused = "угол конуса, лимит целей; радиус нужен только стратегии «скопление врагов»";
                    break;

                default: // SmartPoint
                    what = r > 0f
                        ? "Стратегия «" + strategy + "» выбирает точку; заденет всех в радиусе " + r + " вокруг неё, " + self + "."
                        : "Радиус ноль — целей НЕ БУДЕТ.";
                    unused = "угол конуса";
                    break;
            }

            if (!selected.PicksTargetByStrategy) unused = "стратегия выбора цели, " + unused;
            if (selected.delivery != SkillDelivery.Projectile) unused += ", настройки снаряда";

            var box = new VisualElement
            {
                style = { marginBottom = 6, paddingTop = 4, paddingBottom = 4, paddingLeft = 7, paddingRight = 7,
                          backgroundColor = new Color(0.19f, 0.22f, 0.19f),
                          borderLeftWidth = 3, borderLeftColor = COL_OK }
            };
            box.Add(new Label(what) { style = { whiteSpace = WhiteSpace.Normal } });
            box.Add(new Label("Не используется в этом режиме: " + unused)
                { style = { whiteSpace = WhiteSpace.Normal, color = COL_DIM, fontSize = 10 } });
            return box;
        }

        // ======================== ШКАЛА КАСТА ========================

        static void AddCastTimeline()
        {
            var so = new SerializedObject(selected);
            var box = Section("Шкала каста и презентация", new Color(0.22f, 0.24f, 0.28f));

            int lvl = previewLevel;
            float castTime = InterflowAbility.LevelValue(selected.castTime, lvl);
            float cd = InterflowAbility.LevelValue(selected.cooldown, lvl);

            var strip = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6 } };
            strip.Add(Moment("0 с — замах",
                selected.castVFX != null ? selected.castVFX.name : "визуала нет",
                $"из точки «{InterflowEditorUI.EnumLabel(typeof(SkillSocketType), selected.spawnSocket.ToString())}», живёт {selected.castVfxLifetime} с"));
            strip.Add(Moment($"{castTime} с — срабатывание",
                selected.impactVFX != null ? selected.impactVFX.name : "визуала нет",
                $"в точке попадания, живёт {selected.impactVfxLifetime} с · здесь исполняются оба списка ниже"));
            strip.Add(Moment("после каста", $"откат {cd} с", "мана и откат списываются ядром"));
            box.Add(strip);

            if (castTime <= 0f && selected.castVFX != null)
                box.Add(Hint("Время каста ноль, а визуал замаха задан — замах и удар совпадут в одном кадре."));

            foreach (var f in new[] { "castTime", "cooldown", "manaCost", "duration",
                                      "spawnSocket", "localOffset",
                                      "castVFX", "castVfxLifetime", "impactVFX", "impactVfxLifetime",
                                      "castSound", "impactSound", "soundVolume" })
                AddField(box, so, f);

            box.Add(Hint("Точка привязки — это точка НА КАСТЕРЕ, поэтому она настраивается здесь, а не в эффекторе. " +
                         "Если у носителя нет компонента CharacterSockets, всё пойдёт из центра объекта."));

            box.Bind(so);
            TrackEdits(box, so);
            rightPanel.Add(box);
        }

        static VisualElement Moment(string when, string what, string note)
        {
            var v = new VisualElement
            {
                style = { flexGrow = 1, flexBasis = 0, marginRight = 4,
                          paddingTop = 5, paddingBottom = 5, paddingLeft = 7, paddingRight = 7,
                          backgroundColor = new Color(0.18f, 0.18f, 0.19f),
                          borderTopWidth = 2, borderTopColor = new Color(0.30f, 0.49f, 1f) }
            };
            v.Add(new Label(when) { style = { unityFontStyleAndWeight = FontStyle.Bold, whiteSpace = WhiteSpace.Normal } });
            v.Add(new Label(what) { style = { whiteSpace = WhiteSpace.Normal } });
            v.Add(new Label(note) { style = { whiteSpace = WhiteSpace.Normal, color = COL_DIM, fontSize = 10 } });
            return v;
        }

        // ======================== ДВА СПИСКА ========================

        static void AddStepLists()
        {
            issueHolders.Clear();
            AddKindList(StepKind.Event, "⚡  События",
                "происходят и заканчиваются — длительности у них нет");
            AddKindList(StepKind.State, "⏳  Состояния",
                "висят на цели во времени; значок в панели состояний даёт только эффектор");
        }

        static void AddKindList(StepKind kind, string title, string subtitle)
        {
            var so = new SerializedObject(selected);
            var blocks = BLOCKS.Where(b => b.kind == kind).OrderBy(b => b.order).ToArray();
            int on = blocks.Count(b => IsEnabled(b.field));

            Color accent = kind == StepKind.Event ? COL_EVENT : COL_STATE;

            var head = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 10 } };
            head.Add(new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13, color = accent } });
            head.Add(new Label($"   {subtitle}") { style = { color = COL_DIM, flexGrow = 1, whiteSpace = WhiteSpace.Normal } });
            head.Add(new Label($"включено {on} из {blocks.Length}") { style = { color = COL_DIM } });
            rightPanel.Add(head);

            var container = new VisualElement();
            foreach (var b in blocks) container.Add(BlockCard(b, so, accent));

            // Значок состояния — не блок, а отдельное поле; по смыслу это состояние.
            if (kind == StepKind.State) container.Add(StatusEffectorCard(so));

            container.Bind(so);
            TrackEdits(container, so);
            rightPanel.Add(container);
        }

        static VisualElement BlockCard(BlockDesc b, SerializedObject so, Color accent)
        {
            bool enabled = IsEnabled(b.field);

            var card = new VisualElement
            {
                style = { marginBottom = 4, borderLeftWidth = 3, borderLeftColor = enabled ? accent : new Color(0.32f, 0.32f, 0.32f),
                          backgroundColor = new Color(0.22f, 0.22f, 0.23f), paddingLeft = 6, paddingRight = 6,
                          paddingTop = 3, paddingBottom = 3 }
            };

            string where = b.perTarget ? "по каждой цели" : "один раз за каст";
            var fold = new Foldout
            {
                text = $"{b.order}.  {b.title}     ({where})" + (enabled ? "" : "   — выключен"),
                value = enabled
            };
            if (!enabled) fold.style.opacity = 0.6f;

            fold.Add(new Label(b.hint) { style = { whiteSpace = WhiteSpace.Normal, color = COL_DIM, marginBottom = 3 } });

            var prop = so.FindProperty(b.field);
            if (prop != null) fold.Add(new PropertyField(prop, ""));
            else fold.Add(new Label($"поле «{b.field}» не найдено") { style = { color = COL_WARN } });

            // Держатель сообщений валидатора этого блока — обновляется точечно при правке.
            var holder = new VisualElement();
            issueHolders[b.field] = holder;
            FillIssues(b, holder);
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
            var fold = new Foldout { text = "Значок состояния (эффектор)", value = selected.statusEffector != null };
            fold.Add(new Label("Умение своего значка не имеет: панель состояний собирается из эффекторов, " +
                               "поэтому носитель значка — отдельный эффектор.")
                { style = { whiteSpace = WhiteSpace.Normal, color = COL_DIM, marginBottom = 3 } });

            var prop = so.FindProperty("statusEffector");
            if (prop != null) fold.Add(new PropertyField(prop, "Эффектор-значок"));

            var eff = selected.statusEffector;
            if (eff != null)
            {
                bool visible = !eff.stacks && eff.icon != null;
                fold.Add(new Label(visible
                        ? "Значок будет виден в панели состояний"
                        : eff.stacks ? "Значок НЕ будет виден: у эффектора включён Stacks"
                                     : "Значок НЕ будет виден: у эффектора не задана иконка")
                    { style = { whiteSpace = WhiteSpace.Normal, color = visible ? COL_OK : COL_WARN } });
            }

            card.Add(fold);
            return card;
        }

        static void FillIssues(BlockDesc b, VisualElement holder)
        {
            holder.Clear();
            foreach (var i in IssuesOf(selected).Where(x => BlockFor(x) == b))
                holder.Add(IssueRow(i));
        }

        /// <summary>Включён ли блок: у всех блоков поле называется одинаково — enabled.</summary>
        static bool IsEnabled(string field)
        {
            var so = new SerializedObject(selected);
            var p = so.FindProperty(field);
            var e = p != null ? p.FindPropertyRelative("enabled") : null;
            return e != null && e.boolValue;
        }

        // ======================== ЧИСЛА УМЕНИЯ ========================

        static void AddLevelFooter()
        {
            // ПОЧИНКА 2026-08-09: этот блок остался в БЕЗУРОВНЕВОЙ редакции после отката уборки
            // уровней 07.08 — поля снова массивы, а строки печатали их как объекты («System.Single[]»).
            // Компилятор такое пропускает: интерполяция принимает любой тип. Возвращены выборка
            // по уровню и сам переключатель уровня.
            var box = Section("Числа для выбранного уровня", new Color(0.22f, 0.24f, 0.22f));

            int max = Mathf.Max(1, selected.maxLevels);
            var lvlField = new SliderInt("Уровень (с 1)", 1, max) { value = Mathf.Clamp(previewLevel + 1, 1, max) };
            lvlField.RegisterValueChangedCallback(e => { previewLevel = e.newValue - 1; RebuildRightPanel(); });
            box.Add(lvlField);

            int l = previewLevel;
            var lines = new List<string>
            {
                $"откат {InterflowAbility.LevelValue(selected.cooldown, l)} с",
                $"время каста {InterflowAbility.LevelValue(selected.castTime, l)} с",
                $"радиус {InterflowAbility.LevelValue(selected.radius, l)}",
                $"дальность каста {InterflowAbility.LevelValue(selected.castRange, l)}"
            };
            if (selected.damage != null && selected.damage.enabled && selected.damage.entries != null)
                for (int i = 0; i < selected.damage.entries.Length; i++)
                {
                    var e = selected.damage.entries[i];
                    if (e == null) continue;
                    lines.Add($"урон №{i + 1}: {InterflowAbility.LevelValue(e.amount, l)} " +
                              (e.damageType != null ? e.damageType.name : "БЕЗ ТИПА"));
                }
            if (selected.heal != null && selected.heal.enabled)
                lines.Add($"лечение {InterflowAbility.LevelValue(selected.heal.flat, l)} " +
                          $"(+{InterflowAbility.LevelValue(selected.heal.percentOfMaxHp, l)} % от макс. ХП)");
            if (selected.buff != null && selected.buff.enabled)
                lines.Add($"баф {InterflowAbility.LevelValue(selected.buff.duration, l)} с");
            if (selected.shield != null && selected.shield.enabled)
                lines.Add($"щит {InterflowAbility.LevelValue(selected.shield.flat, l)} " +
                          $"на {InterflowAbility.LevelValue(selected.shield.duration, l)} с");

            box.Add(new Label(string.Join("   ·   ", lines)) { style = { whiteSpace = WhiteSpace.Normal } });
            box.Add(Hint("Массивы по уровням короче нужного клампятся к последнему элементу — значение выше " +
                         "показано уже с учётом этого."));
            rightPanel.Add(box);
        }

        // ======================== ОБЩЕЕ ========================

        static VisualElement Section(string title, Color bg)
        {
            var box = new VisualElement
            {
                style = { marginTop = 8, marginBottom = 2, paddingTop = 6, paddingBottom = 6,
                          paddingLeft = 8, paddingRight = 8, backgroundColor = bg,
                          borderTopLeftRadius = 3, borderTopRightRadius = 3,
                          borderBottomLeftRadius = 3, borderBottomRightRadius = 3 }
            };
            box.Add(new Label(title)
                { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } });
            return box;
        }

        static Label Hint(string text) => new Label(text)
            { style = { whiteSpace = WhiteSpace.Normal, color = COL_DIM, fontSize = 10, marginTop = 3, marginBottom = 2 } };

        static void AddField(VisualElement parent, SerializedObject so, string field)
        {
            var p = so.FindProperty(field);
            if (p == null) return;

            // Ц1: подпись — из общего словаря (правило 5); null — остаётся подпись Unity.
            // Декоратор прячем только у cooldown: там [Header("Parameters")] базового Ability —
            // единственная английская надпись в этой вкладке, а перевести её — правка ядра.
            // Русские заголовки наших блоков («Цель», «Фильтры целей», «Доставка») остаются — они полезны.
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
        /// Валидатор по месту: любая правка поля перепроверяет ТОЛЬКО этот скилл и обновляет
        /// сообщения у блоков и точку статуса в списке. Панель целиком не пересобирается —
        /// иначе при вводе числа терялся бы фокус.
        /// </summary>
        static void TrackEdits(VisualElement scope, SerializedObject so)
        {
            scope.TrackSerializedObjectValue(so, _ =>
            {
                RevalidateSelected();
                foreach (var b in BLOCKS)
                    if (issueHolders.TryGetValue(b.field, out var holder) && holder != null)
                        FillIssues(b, holder);
                RebuildList();
            });
        }
    }
}
