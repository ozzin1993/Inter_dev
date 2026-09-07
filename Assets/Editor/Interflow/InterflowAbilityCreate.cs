using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============ INTERFLOW EDITOR — СОЗДАНИЕ УМЕНИЙ (единый источник, правило 5) ==
    // Создание ассета умения переехало из «Умений и эффекторов» в конструкторы и в «Производство»
    // (решение Artsiom 2026-08-09). Чтобы не заводить три копии одного и того же — сканирование
    // типов, диалог сохранения, выдача уникального id и сброс кэшей живут здесь.
    //
    // Боевое умение и пассивка создаются БЕЗ выбора класса: у них по одному конструктору
    // (CompositeSkill и CompositePassive). Производству выбор класса нужен — постройка, обучение,
    // исследование и категория панели это разные сущности, конструктора у них нет.
    public static class InterflowAbilityCreate
    {
        // Пустые базовые шаблоны ядра: наследоваться от них можно, а создавать из них нечего.
        // `Passive` в этот набор НЕ входит (решение Artsiom 2026-08-09) — он меняет статы и держит ассеты.
        static readonly HashSet<string> ServiceTemplates = new HashSet<string>
        {
            "Active", "Location", "Area", "Process", "UnitAbility"   // «Aura» снесён блоком Б7 (2026-09-05)
        };

        /// <summary>
        /// Все неабстрактные наследники Ability, годные для создания.
        /// Отбор ИМЕННО по подтипу (2026-08-09): раньше признаком служило наличие [CreateAssetMenu],
        /// но атрибут снят со всех классов умений — параллельное меню Unity убрано, вход один
        /// (правило 20). Единственная точка отбора на весь редактор.
        /// </summary>
        public static IEnumerable<Type> AllTypes()
        {
            foreach (var type in AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()))
            {
                if (!typeof(Ability).IsAssignableFrom(type) || type.IsAbstract) continue;
                if (ServiceTemplates.Contains(type.Name)) continue;

                yield return type;
            }
        }

        /// <summary>Типы одной группы (боевые / пассивки / производство), отсортированные по подписи.</summary>
        public static List<Type> TypesOfGroup(InterflowAbilityGroups.Group group)
        {
            var settings = InterflowEditorSettings.GetOrCreate();
            return AllTypes()
                .Where(t => InterflowAbilityGroups.OfType(t) == group)
                .OrderBy(t => CardTitle(t, settings))
                .ToList();
        }

        /// <summary>
        /// Подпись типа: русская из таблицы настроек, иначе имя класса.
        /// Прежний фолбэк на текст пункта меню умер вместе с [CreateAssetMenu] — если подпись
        /// не заполнена, в карточке будет голое имя класса. Кнопка «Завести строки в таблице
        /// подписей» во вкладке «Производство» создаёт под них пустые строки.
        /// </summary>
        public static string CardTitle(Type t, InterflowEditorSettings settings)
        {
            string fromSettings = settings != null ? settings.AbilityTypeTitle(t.Name) : null;
            return !string.IsNullOrEmpty(fromSettings) ? fromSettings : t.Name;
        }

        /// <summary>
        /// Создать ассет умения: спросить путь, выдать наименьший свободный id, назвать по имени файла,
        /// сбросить кэши классификации и «кто использует». Возвращает созданный ассет или null (отмена).
        /// </summary>
        public static Ability Create(Type abilityType, string dialogTitle, string defaultFileName)
        {
            if (abilityType == null) return null;

            var settings = InterflowEditorSettings.GetOrCreate();
            InterflowEditorUI.EnsureFolder(settings.abilityCreateFolder);

            string dstPath = EditorUtility.SaveFilePanelInProject(
                dialogTitle, defaultFileName, "asset", "Имя нового ассета", settings.abilityCreateFolder);
            if (string.IsNullOrEmpty(dstPath)) return null;

            var ability = (Ability)ScriptableObject.CreateInstance(abilityType);
            ability.id = InterflowEditorUI.NextFreeId("t:Ability", a => ((Ability)a).id);
            ability.abilityName = new[] { Path.GetFileNameWithoutExtension(dstPath) };

            AssetDatabase.CreateAsset(ability, dstPath);
            AssetDatabase.SaveAssets();

            InvalidateCaches();
            return ability;
        }

        /// <summary>Удалить ассет с подтверждением. true — удалили.</summary>
        public static bool Delete(UnityEngine.Object asset, string question)
        {
            if (asset == null) return false;

            string path = AssetDatabase.GetAssetPath(asset);
            if (!EditorUtility.DisplayDialog("Удалить", $"{question}\n{path}", "Удалить", "Отмена")) return false;

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();

            InvalidateCaches();
            return true;
        }

        /// <summary>Карточка типа: кнопка создания, что получится, пояснение из настроек.</summary>
        public static VisualElement TypeCard(Type t, Action<Ability> onCreated, string dialogTitle)
        {
            var settings = InterflowEditorSettings.GetOrCreate();

            var card = new VisualElement
            {
                style = { marginBottom = 3, paddingLeft = 5, paddingRight = 5, paddingTop = 3, paddingBottom = 3,
                          backgroundColor = new Color(0.22f, 0.22f, 0.23f), borderLeftWidth = 3,
                          borderLeftColor = new Color(0.32f, 0.32f, 0.32f) }
            };

            var btn = new Button(() =>
            {
                var created = Create(t, dialogTitle, t.Name);
                if (created != null) onCreated?.Invoke(created);
            })
            {
                text = CardTitle(t, settings),
                style = { unityTextAlign = TextAnchor.MiddleLeft, marginBottom = 1 },
                tooltip = "Класс: " + t.Name
            };
            card.Add(btn);

            // «Что получится» берётся из штатного Ability.type уже существующих ассетов этого класса.
            // Ассетов нет — честно пишем «неизвестно», а не угадываем.
            AbilityType at = InterflowAbilityGroups.ComputedTypeOf(t);
            card.Add(new Label(at == AbilityType.Null
                    ? "Тип пока неизвестен: ассетов этого класса в проекте ещё нет — определится после создания первого"
                    : "Получится: " + InterflowAbilityGroups.AbilityTypeRu(at))
                { style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.65f, 0.65f, 0.65f), fontSize = 10 } });

            string note = settings != null ? settings.AbilityTypeNote(t.Name) : null;
            if (!string.IsNullOrEmpty(note))
                card.Add(new Label(note) { style = { whiteSpace = WhiteSpace.Normal, fontSize = 10 } });

            return card;
        }

        /// <summary>
        /// Завести в таблице подписей настроек ПУСТЫЕ строки под типы, которых там ещё нет.
        /// Ничего не придумывает и не перезаписывает: русские названия и пояснения впишет геймдизайнер.
        /// Переехало из «Умений и эффекторов» вместе с созданием.
        /// </summary>
        public static void SeedTypeLabels()
        {
            var settings = InterflowEditorSettings.GetOrCreate();
            var so = new SerializedObject(settings);
            var arr = so.FindProperty("abilityTypeLabels");
            if (arr == null) return;

            var have = new HashSet<string>();
            for (int i = 0; i < arr.arraySize; i++)
                have.Add(arr.GetArrayElementAtIndex(i).FindPropertyRelative("className").stringValue);

            int added = 0;
            foreach (var t in AllTypes().OrderBy(x => x.Name))
            {
                if (have.Contains(t.Name)) continue;

                arr.InsertArrayElementAtIndex(arr.arraySize);
                var e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                e.FindPropertyRelative("className").stringValue = t.Name;
                e.FindPropertyRelative("title").stringValue = "";
                e.FindPropertyRelative("note").stringValue = "";
                added++;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("Таблица подписей",
                $"Добавлено пустых строк: {added}. Названия и пояснения впиши в ассете настроек редактора.", "Ок");
            EditorGUIUtility.PingObject(settings);
        }

        static void InvalidateCaches()
        {
            InterflowAbilityGroups.InvalidateCache();
            InterflowAbilityUsage.InvalidateCache();
        }
    }
}
