using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============ ВКЛАДКА «БОЕВЫЕ УМЕНИЯ» — МАСТЕР СОЗДАНИЯ (партиал, правило 22) ==
    // Решение Artsiom 2026-08-14/16 (макет v2, экран «Мастер создания»): типовое умение создаётся
    // заготовкой с предзаполненными полями, а не с нуля. Заготовка закрывает настройки, на которых
    // авто-умение «молча не работает»: пустой селектор целей, пустой откат, умение не в abilities[]
    // юнита, компонент автокаста без списка. Пустое умение — отдельной карточкой, привычный путь жив.
    public static partial class InterflowSkillBuilderTab
    {
        static bool wizardMode;
        static int wizardPreset;                 // индекс выбранной заготовки
        static GameObject wizardUnit;            // кому дать умение (префаб юнита); null — никому
        static bool wizardAuto = true;           // сделать авто-умением (двойная запись)

        public static void OpenWizard() { wizardMode = true; RebuildRightPanel(); }

        // ======================== ЗАГОТОВКИ ========================

        class Preset
        {
            public string title;
            public string what;                  // что получится, одной строкой
            public string prefill;               // что предзаполнено — показывается в карточке
            public Action<CompositeSkill, InterflowEditorSettings> apply; // null — пустое умение
        }

        static UnitSelector EnemyUnitsSelector() =>
            new UnitSelector(false, false, true, true, false, false, false, true, false, true, false, false);

        static UnitSelector FriendlyUnitsSelector() =>
            new UnitSelector(true, true, false, true, false, false, false, true, false, true, false, false);

        static readonly Preset[] PRESETS =
        {
            new Preset
            {
                title = "⚡ Урон ближайшему врагу — авто",
                what = "Юнит сам бьёт ближайшего врага в округе, как только откат готов.",
                prefill = "цель: умный выбор юнита · стратегия: ближайший · селектор: враги, юниты, земля и воздух · " +
                          "дальность поиска 6 · откат 8 с · блок «Урон» с типом из настроек редактора",
                apply = (s, set) =>
                {
                    s.targetMode = SkillTargetMode.SmartUnit;
                    s.targetStrategy = SkillTargetStrategy.Nearest;
                    s.unitSelector = EnemyUnitsSelector();
                    s.castRange = new float[] { 6f };
                    s.cooldown = new float[] { 8f };
                    s.damage.enabled = true;
                    s.damage.entries = new[] { new SkillDamageEntry { damageType = set != null ? set.defaultDamageType : null } };
                }
            },
            new Preset
            {
                title = "✚ Подлечить раненого союзника — авто",
                what = "Юнит сам лечит раненого союзника рядом (ниже половины здоровья).",
                prefill = "цель: умный выбор юнита · стратегия: раненый ниже порога 0.5 · селектор: свои и союзники · " +
                          "дальность 7 · откат 10 с · блок «Лечение»",
                apply = (s, set) =>
                {
                    s.targetMode = SkillTargetMode.SmartUnit;
                    s.targetStrategy = SkillTargetStrategy.WoundedBelowThreshold;
                    s.strategyHpThreshold = 0.5f;
                    s.unitSelector = FriendlyUnitsSelector();
                    s.castRange = new float[] { 7f };
                    s.cooldown = new float[] { 10f };
                    s.heal.enabled = true;
                }
            },
            new Preset
            {
                title = "🛡 Баф своей команды — по кнопке",
                what = "Кнопка замка или героя: задевает всю команду.",
                prefill = "цель: вся команда · умение по кнопке: вкл · откат 45 с · блок «Состояния» (пустая запись — заполнить)",
                apply = (s, set) =>
                {
                    s.targetMode = SkillTargetMode.WholeTeam;
                    s.buttonCast = true;
                    s.cooldown = new float[] { 45f };
                    s.effectors.enabled = true;
                    s.effectors.records = new[] { new SkillEffectorRecord() };
                }
            },
            new Preset
            {
                title = "☄ Урон по области — кнопка замка",
                what = "Стратегия находит скопление врагов и бьёт по площади.",
                prefill = "цель: умный выбор точки · стратегия: скопление · радиус 5 · умение по кнопке: вкл · " +
                          "точка отсчёта: вражеская линия · откат 30 с · блок «Урон» с типом из настроек",
                apply = (s, set) =>
                {
                    s.targetMode = SkillTargetMode.SmartPoint;
                    s.targetStrategy = SkillTargetStrategy.Cluster;
                    s.unitSelector = EnemyUnitsSelector();
                    s.radius = new float[] { 5f };
                    s.buttonCast = true;
                    s.searchOrigin = SkillSearchOrigin.EnemyLanePoint;
                    s.cooldown = new float[] { 30f };
                    s.damage.enabled = true;
                    s.damage.entries = new[] { new SkillDamageEntry { damageType = set != null ? set.defaultDamageType : null } };
                }
            },
            new Preset
            {
                title = "▢ Пустое умение",
                what = "Всё с нуля, как раньше: предзаполняются только уникальный id и имя по файлу.",
                prefill = "ничего не предзаполнено",
                apply = null
            },
        };

        // ======================== UI МАСТЕРА ========================

        static void BuildWizard(VisualElement panel)
        {
            panel.Add(new Label("Создание умения")
                { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 15, marginBottom = 4 } });

            // --- шаг 1: заготовка ---
            var step1 = Section("1 · С чего начать", new Color(0.20f, 0.20f, 0.22f),
                "Заготовка предзаполняет поля — дальше всё правится в конструкторе как обычно.");

            for (int i = 0; i < PRESETS.Length; i++)
            {
                int idx = i;
                var p = PRESETS[i];
                bool onCard = wizardPreset == i;

                var card = new VisualElement
                {
                    style = { marginBottom = 3, paddingLeft = 7, paddingRight = 7, paddingTop = 4, paddingBottom = 4,
                              backgroundColor = onCard ? new Color(0.17f, 0.21f, 0.28f) : new Color(0.22f, 0.22f, 0.23f),
                              borderLeftWidth = 3,
                              borderLeftColor = onCard ? new Color(0.35f, 0.6f, 0.95f) : new Color(0.32f, 0.32f, 0.32f) }
                };
                card.tooltip = "Предзаполнено: " + p.prefill;

                var btn = new Button(() => { wizardPreset = idx; RebuildRightPanel(); })
                {
                    text = p.title,
                    style = { unityTextAlign = TextAnchor.MiddleLeft, unityFontStyleAndWeight = onCard ? FontStyle.Bold : FontStyle.Normal }
                };
                card.Add(btn);
                card.Add(new Label(p.what) { style = { whiteSpace = WhiteSpace.Normal, color = COL_DIM, fontSize = 10 } });
                step1.Add(card);
            }
            panel.Add(step1);

            // --- шаг 2: кому дать ---
            var step2 = Section("2 · Кому дать умение (можно пропустить)", new Color(0.20f, 0.20f, 0.22f),
                "Умение встанет юниту сразу ДВОЙНОЙ записью: в abilities[] юнита и в список компонента автокаста. " +
                "Без второй записи авто-каст не работает — это самая частая причина «умение молчит». " +
                "Пропустил — раздать можно позже через «Кто использует» и вкладку «Юниты» главного окна.");

            var unitField = new UnityEditor.UIElements.ObjectField("Юнит (префаб)")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                value = wizardUnit
            };
            unitField.tooltip = "Префаб юнита из проекта. Пусто — умение создаётся без носителя.";
            unitField.RegisterValueChangedCallback(e =>
            {
                wizardUnit = e.newValue as GameObject;
                if (wizardUnit != null && wizardUnit.GetComponent<Unit>() == null)
                {
                    EditorUtility.DisplayDialog("Не юнит", "На этом префабе нет компонента Unit.", "OK");
                    unitField.SetValueWithoutNotify(null);
                    wizardUnit = null;
                }
            });
            step2.Add(unitField);

            var autoToggle = new UnityEngine.UIElements.Toggle("Сделать авто-умением (двойная запись сразу)") { value = wizardAuto };
            autoToggle.tooltip = "ВКЛ: умение попадёт и в abilities[] юнита, и в компонент автокаста " +
                                 "(нет компонента — добавится). Порядок в списке автокаста = приоритет. " +
                                 "ВЫКЛ: умение встанет только в abilities[] — для умений по кнопке.";
            autoToggle.RegisterValueChangedCallback(e => wizardAuto = e.newValue);
            step2.Add(autoToggle);
            panel.Add(step2);

            // --- шаг 3: создать ---
            var step3 = Section("3 · Создать", new Color(0.20f, 0.22f, 0.20f),
                "Файл и имя спросит диалог сохранения. Что останется заполнить вручную — подсветят проверки " +
                "прямо в карточке умения.");

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var create = new Button(CreateFromWizard) { text = "Создать и открыть в конструкторе" };
            create.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(create);
            row.Add(new Button(() => { wizardMode = false; RebuildRightPanel(); }) { text = "Отмена" });
            step3.Add(row);
            panel.Add(step3);
        }

        // ======================== СОЗДАНИЕ ========================

        static void CreateFromWizard()
        {
            var settings = InterflowEditorSettings.GetOrCreate();
            InterflowEditorUI.EnsureFolder(settings.abilityCreateFolder);

            string dstPath = EditorUtility.SaveFilePanelInProject(
                "Создать умение", "Skill_New", "asset", "Имя нового умения", settings.abilityCreateFolder);
            if (string.IsNullOrEmpty(dstPath)) return;

            var asset = ScriptableObject.CreateInstance<CompositeSkill>();
            asset.id = InterflowEditorUI.NextFreeId("t:Ability", a => ((Ability)a).id);
            asset.abilityName = new[] { System.IO.Path.GetFileNameWithoutExtension(dstPath) };

            var preset = PRESETS[Mathf.Clamp(wizardPreset, 0, PRESETS.Length - 1)];
            preset.apply?.Invoke(asset, settings);

            AssetDatabase.CreateAsset(asset, dstPath);
            AssetDatabase.SaveAssets();

            if (wizardUnit != null) GiveToUnit(wizardUnit, asset, wizardAuto);

            InterflowAbilityGroups.InvalidateCache();
            InterflowAbilityUsage.InvalidateCache();

            wizardMode = false;
            RefreshAll();
            selected = asset;
            RebuildList();
            RebuildRightPanel();
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>
        /// Двойная запись: умение в abilities[] юнита И (для авто) в список компонента автокаста.
        /// Всё через SerializedObject внутри prefab-контента (паттерн вкладки «Юниты»), с дубль-гвардом.
        /// </summary>
        static void GiveToUnit(GameObject unitPrefab, CompositeSkill skill, bool asAuto)
        {
            string prefabPath = AssetDatabase.GetAssetPath(unitPrefab);
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogWarning("[Мастер умений] Не найден путь префаба юнита — умение создано, но юниту не выдано.");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var unit = root.GetComponent<Unit>();
                if (unit == null)
                {
                    Debug.LogWarning($"[Мастер умений] На «{unitPrefab.name}» нет компонента Unit — умение юниту не выдано.");
                    return;
                }

                // 1) abilities[] юнита (дубль-гвард).
                var soUnit = new SerializedObject(unit);
                var abilities = soUnit.FindProperty("abilities");
                if (abilities != null && !ArrayContains(abilities, skill))
                {
                    abilities.InsertArrayElementAtIndex(abilities.arraySize);
                    abilities.GetArrayElementAtIndex(abilities.arraySize - 1).objectReferenceValue = skill;
                    soUnit.ApplyModifiedPropertiesWithoutUndo();
                }

                // 2) Список автокаста (компонент добавляется, если его нет).
                if (asAuto)
                {
                    var aau = root.GetComponent<AutoAbilityUser>();
                    if (aau == null) aau = root.AddComponent<AutoAbilityUser>();

                    var soAuto = new SerializedObject(aau);
                    var list = soAuto.FindProperty("autoAbilities");
                    if (list != null && !ArrayContains(list, skill))
                    {
                        list.InsertArrayElementAtIndex(list.arraySize);
                        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = skill;
                        soAuto.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static bool ArrayContains(SerializedProperty array, UnityEngine.Object value)
        {
            for (int i = 0; i < array.arraySize; i++)
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == value) return true;
            return false;
        }
    }
}
