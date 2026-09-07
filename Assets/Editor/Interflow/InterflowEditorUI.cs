using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============================= INTERFLOW EDITOR — ОБЩИЕ UI-ХЕЛПЕРЫ (фаза E4) ==
    // Единый источник общей логики вкладок Interflow Editor (правило 5): группировка полей по [Header]-блокам
    // и создание папок. Используют вкладки «Юниты», «Умения и эффекторы», «Справочники» — без дублирования.
    public static class InterflowEditorUI
    {
        public const string DEFAULT_GROUP = "Основные";

        // Разбивает видимые поля SerializedObject на сворачиваемые фолды по [Header] (границы — рефлексией по типу
        // цели, не хардкод). targetType берём из самого объекта (партиалы/подклассы подхватываются). Привязка (Undo/
        // сохранение) — на возвращаемый контейнер.
        public static VisualElement BuildGroupedFields(SerializedObject so, string defaultTitle = DEFAULT_GROUP)
        {
            var container = new VisualElement();
            Type targetType = so.targetObject != null ? so.targetObject.GetType() : null;
            VisualElement currentBody = null;

            // Ц1: русские подписи применяем ТОЛЬКО к умениям. Словарь собран под поля Ability,
            // а имена вроде icon / cost / duration / radius есть и у юнитов, фракций и справочников —
            // там та же подпись была бы неверной. Остальные вкладки работают как раньше.
            bool translate = so.targetObject is Ability;

            var it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script") continue;

                string header = HeaderFor(targetType, it.name);   // не null → начало нового блока
                if (currentBody == null || header != null)
                {
                    var foldout = new Foldout { text = header ?? defaultTitle, value = true, style = { marginBottom = 2 } };
                    currentBody = new VisualElement();
                    foldout.Add(currentBody);
                    container.Add(foldout);
                }

                // suppressDecorators=true для поля-носителя [Header]: заголовок уже вынесен в шапку фолда,
                // иначе PropertyField нарисовал бы его повторно.
                currentBody.Add(MakeField(it, translate ? FieldLabel(it.name) : null, header != null));
            }

            container.Bind(so);
            return container;
        }

        // ======================== РУССКИЕ ПОДПИСИ ПОЛЕЙ (Ц1) ========================

        // Unity делает подпись из ИМЕНИ сериализованного поля, поэтому в UI видны английские
        // «Target Mode» / «Target Strategy», хотя [Header] и [Tooltip] у нас русские (правило 4).
        // Переименовать сами поля НЕЛЬЗЯ — слетит сериализация всех уже собранных ассетов,
        // поэтому подпись задаётся вторым аргументом PropertyField. Словарь живёт в коде, а не в ассете,
        // потому что ключи — имена полей класса: в ассете они молча разъехались бы с кодом.
        static readonly Dictionary<string, string> FIELD_LABELS = new Dictionary<string, string>
        {
            // Цель (ось «Когда срабатывает» снесена блоком Б7, 2026-09-05)
            { "targetMode",               "Кого задевает" },
            { "buttonCast",               "Умение по кнопке (замок или герой)" },
            { "targetStrategy",           "Алгоритм выбора одной цели" },
            { "searchOrigin",             "Откуда считать «ближайшего»" },
            { "strategyUseCurrentHealth", "Мерить текущее ХП, не максимальное" },
            { "strategyHpThreshold",      "Порог ХП (доля от максимума)" },
            // Селекторы: принадлежность и роли
            { "targetCategories",         "Селектор ролей (пусто — любые)" },
            { "maxTargets",               "Максимум целей (0 — без лимита)" },
            { "multiPick",                "Кого оставить при лимите" },
            { "includeSelf",              "Включать самого кастера" },
            { "coneAngle",                "Угол конуса, градусы" },
            { "directionMatters",         "Направление кастера важно (доворот к цели)" },
            { "radius",                   "Радиус" },
            { "castRange",                "Дальность каста" },
            { "unitSelector",             "Кто вообще может быть целью" },
            // Доставка
            { "delivery",                 "Доставка" },
            { "projectilePrefab",         "Префаб снаряда" },
            { "projectileFollowsTarget",  "Снаряд самонаводится" },
            // Шкала каста и презентация
            { "castTime",                 "Время каста, с" },
            { "cooldown",                 "Откат, с" },
            // «manaCost» снят вместе с полем (Б5, 2026-09-04): цены в мане у умений нет.
            { "duration",                 "Длительность, с" },
            { "spawnSocket",              "Точка на модели кастера" },
            { "localOffset",              "Смещение от точки, м" },
            { "castVFX",                  "Визуал замаха" },
            { "castVfxLifetime",          "Время жизни визуала замаха, с" },
            { "impactVFX",                "Визуал попадания" },
            { "impactVfxLifetime",        "Время жизни визуала попадания, с" },
            { "castSound",                "Звук каста" },
            { "impactSound",              "Звук попадания" },
            { "soundVolume",              "Громкость звуков, 0..1" },

            // Базовые поля Ability. Перевод сделан ПО АНГЛИЙСКИМ [Tooltip] самого ядра,
            // а не по догадке о смысле имени поля. Сами тултипы не трогаем — это была бы правка ядра.
            { "id",                       "Идентификатор (id)" },
            { "abilityName",              "Название по уровням" },
            { "description",              "Описание по уровням" },
            { "icon",                     "Иконка по уровням" },
            { "slotNumber",               "Ячейка в панели (−1 — по порядку)" },
            { "isItem",                   "Это предмет, а не умение (предметы вне скоупа игры)" },
            { "useUponPickUp",            "Применить сразу при подборе" },
            { "dropOnDeath",              "Выпадает при смерти носителя" },
            { "charges",                  "Заряды (0 — без зарядов)" },
            { "maxLevels",                "Максимум уровней" },
            { "heroLevelable",            "Уровень качается прокачкой юнита" },
            { "dontTurn",                 "Не поворачивать кастера к цели" },
            // «continuous», «interruptible», «requiresCastingUnit» и «manaCostPerSecond» сняты вместе с полями
            // (Б6, 2026-09-04): переключателей и умений-каналов в игре нет.
            { "cost",                     "Цена в ресурсах" },
            { "requiredTech",             "Требуемые технологии по уровням" },

            // Собственные поля производственных типов (вкладка «Производство»), тоже по тултипам ядра.
            { "building",                 "Что строится (по уровням)" },
            { "unitToTrain",              "Кого обучать" },
            { "unitCount",                "Сколько юнитов появится" },
            { "costForSingleUnit",        "Цена указана за одного юнита" },
            { "unlockTech",               "Что открывает: уровень 1 → первый элемент" },
            { "upgradeUnit",              "Во что апгрейдится здание" },
            { "upgradeTime",              "Время апгрейда, с" },
            { "transformUnit",            "Чей вид принимает кастер" },
            { "transformTime",            "Длительность превращения, с" },
            { "transformSound",           "Звук превращения" },
            { "transformVFX",             "Визуал превращения" },
            { "passiveEffects",           "Пассивные эффекты на время превращения" },
            // Организация контента (не игровое поле)
            { "editorFactions",           "Фракции (ручная метка)" }
        };

        /// <summary>Русская подпись поля; null — перевода нет, пусть Unity рисует свою.</summary>
        public static string FieldLabel(string fieldName)
            => fieldName != null && FIELD_LABELS.TryGetValue(fieldName, out var s) ? s : null;

        /// <summary>
        /// Поле с явной подписью и необязательным подавлением декораторов ([Header]/[Space]).
        /// label = null — подпись по умолчанию (из имени поля).
        /// У умений (Ability) числовые массивы «по уровням» рисуются НЕ списком Unity, а по правилу блока Б8
        /// (целевая модель §9, решение Artsiom 2026-09-06): одно поле «базовое» (элемент 0) плюс свёрнутый блок
        /// «по уровням» (элементы 1..N). Вложенные блоки конструктора и их списки записей раскрываются так же.
        /// </summary>
        public static VisualElement MakeField(SerializedProperty prop, string label, bool suppressDecorators)
        {
            if (prop.serializedObject.targetObject is Ability)
            {
                if (IsLeveledNumberArray(prop)) return MakeLeveledArray(prop.Copy(), label);
                if (IsStructWithLeveledArrays(prop)) return MakeStructFields(prop.Copy(), label);
                if (IsStructListWithLeveledArrays(prop)) return MakeStructList(prop.Copy(), label);
            }

            var pf = label == null ? new PropertyField(prop.Copy()) : new PropertyField(prop.Copy(), label);
            if (suppressDecorators)
            {
                void OnGeo(GeometryChangedEvent e)
                {
                    pf.UnregisterCallback<GeometryChangedEvent>(OnGeo);
                    var dec = pf.Q(className: "unity-decorator-drawers-container");
                    if (dec != null) dec.style.display = DisplayStyle.None;
                }
                pf.RegisterCallback<GeometryChangedEvent>(OnGeo);
            }
            return pf;
        }

        // ======================== ЗНАЧЕНИЯ ПО УРОВНЯМ: «БАЗОВОЕ + БЛОК» (Б8) ========================
        // Данные остаются массивами (решение Artsiom 2026-09-06): элемент 0 — базовое значение, остальные — блок по
        // уровням; выборка в игре — InterflowAbility.LevelValue (нет строки — последняя заполненная). Здесь только вид.

        static readonly Color COL_LEVELS_DIM = new Color(0.62f, 0.62f, 0.62f);

        /// <summary>Числовой массив по уровням: float[] или int[].</summary>
        static bool IsLeveledNumberArray(SerializedProperty prop)
            => prop.isArray && prop.propertyType == SerializedPropertyType.Generic
               && (prop.arrayElementType == "float" || prop.arrayElementType == "int");

        /// <summary>Вложенный блок (не массив), внутри которого есть хотя бы один числовой массив по уровням.</summary>
        static bool IsStructWithLeveledArrays(SerializedProperty prop)
            => !prop.isArray && prop.propertyType == SerializedPropertyType.Generic && ContainsLeveledArray(prop);

        /// <summary>Список записей (массив блоков), у элементов которого есть числовые массивы по уровням.</summary>
        static bool IsStructListWithLeveledArrays(SerializedProperty prop)
        {
            if (!prop.isArray || prop.propertyType != SerializedPropertyType.Generic) return false;
            if (prop.arrayElementType == "float" || prop.arrayElementType == "int" || prop.arrayElementType == "string") return false;
            if (prop.arrayElementType.StartsWith("PPtr")) return false;   // ссылки на ассеты — обычный список
            if (prop.arraySize == 0) return false;                        // пустой — нечего раскрывать, обычный список
            return ContainsLeveledArray(prop.GetArrayElementAtIndex(0));
        }

        static bool ContainsLeveledArray(SerializedProperty parent)
        {
            foreach (var child in Children(parent))
            {
                if (IsLeveledNumberArray(child)) return true;
                if (!child.isArray && child.propertyType == SerializedPropertyType.Generic && ContainsLeveledArray(child)) return true;
            }
            return false;
        }

        // Видимые дети блока (без захода в массивы: у них свои дети «size» и элементы).
        static IEnumerable<SerializedProperty> Children(SerializedProperty parent)
        {
            var end = parent.GetEndProperty();
            var child = parent.Copy();
            if (!child.NextVisible(true)) yield break;
            while (!SerializedProperty.EqualContents(child, end))
            {
                yield return child.Copy();
                if (!child.NextVisible(false)) break;
            }
        }

        static string LabelOf(SerializedProperty prop, string label)
            => label ?? FieldLabel(prop.name) ?? ObjectNames.NicifyVariableName(prop.name);

        /// <summary>
        /// Числовой массив по уровням: элемент 0 — поле «базовое», элементы 1..N — свёрнутый блок «по уровням»
        /// с кнопками добавить/убрать уровень. Пустой массив — кнопка «задать базовое».
        /// </summary>
        static VisualElement MakeLeveledArray(SerializedProperty prop, string label)
        {
            var root = new VisualElement { tooltip = prop.tooltip };
            string title = LabelOf(prop, label);
            SerializedObject so = prop.serializedObject;
            string path = prop.propertyPath;

            void Rebuild()
            {
                root.Clear();
                var arr = so.FindProperty(path);
                if (arr == null) return;

                if (arr.arraySize == 0)
                {
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                    row.Add(new Label(title) { style = { minWidth = 150, color = COL_LEVELS_DIM } });
                    row.Add(new Button(() => { arr.arraySize = 1; so.ApplyModifiedProperties(); Rebuild(); })
                        { text = "задать базовое", tooltip = "Массив пуст — умение работает как без этого параметра (0). Нажми, чтобы задать базовое значение." });
                    root.Add(row);
                    return;
                }

                // Базовое значение — элемент 0, под подписью самого поля.
                root.Add(new PropertyField(arr.GetArrayElementAtIndex(0), title) { tooltip = prop.tooltip });

                // Блок по уровням — элементы 1..N. Свёрнут, пока строк нет.
                int extra = arr.arraySize - 1;
                var fold = new Foldout
                {
                    text = extra > 0 ? $"По уровням: {extra}" : "По уровням: нет",
                    value = extra > 0,
                    style = { marginLeft = 12, marginBottom = 2 },
                    tooltip = "Значения для уровней 2, 3, … У умений и пассивок юнитов: нет строки для нужного уровня — берётся " +
                              "последняя заполненная, никогда ноль; блок пуст — умение всегда работает на базовом значении. " +
                              "Процессы (обучение, исследование, стройка) читают строку ровно своего уровня."
                };
                for (int i = 1; i < arr.arraySize; i++)
                    fold.Add(new PropertyField(arr.GetArrayElementAtIndex(i), $"Уровень {i + 1}"));

                var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 2 } };
                buttons.Add(new Button(() =>
                {
                    int n = arr.arraySize;
                    arr.arraySize = n + 1;
                    // Новая строка повторяет предыдущую — правило «последняя заполненная» делает это ожидаемым стартом.
                    var last = arr.GetArrayElementAtIndex(n - 1);
                    var added = arr.GetArrayElementAtIndex(n);
                    if (added.propertyType == SerializedPropertyType.Float) added.floatValue = last.floatValue;
                    else if (added.propertyType == SerializedPropertyType.Integer) added.intValue = last.intValue;
                    so.ApplyModifiedProperties();
                    Rebuild();
                }) { text = "+ уровень" });
                if (extra > 0)
                    buttons.Add(new Button(() => { arr.arraySize = arr.arraySize - 1; so.ApplyModifiedProperties(); Rebuild(); })
                        { text = "− последний" });
                fold.Add(buttons);
                root.Add(fold);

                root.Bind(so);
            }

            Rebuild();
            return root;
        }

        /// <summary>
        /// Вложенный блок: дети рисуются по тому же правилу (массивы по уровням — «базовое + блок»).
        /// label пустая — дети кладутся прямо в контейнер (блок конструктора уже сидит в своём фолде); иначе — свой фолд.
        /// </summary>
        static VisualElement MakeStructFields(SerializedProperty prop, string label)
        {
            VisualElement body;
            VisualElement result;
            if (string.IsNullOrEmpty(label))
            {
                body = new VisualElement();
                result = body;
            }
            else
            {
                var fold = new Foldout { text = LabelOf(prop, label), value = true, tooltip = prop.tooltip };
                body = fold;
                result = fold;
            }

            foreach (var child in Children(prop))
            {
                if (IsLeveledNumberArray(child)) body.Add(MakeLeveledArray(child, null));
                else if (IsStructWithLeveledArrays(child)) body.Add(MakeStructFields(child, LabelOf(child, null)));
                else if (IsStructListWithLeveledArrays(child)) body.Add(MakeStructList(child, LabelOf(child, null)));
                else body.Add(new PropertyField(child, LabelOf(child, null)));
            }
            return result;
        }

        /// <summary>
        /// Список записей (например записи урона): каждая запись — фолд с детьми по тому же правилу,
        /// внизу кнопки «добавить запись» / «убрать последнюю».
        /// </summary>
        static VisualElement MakeStructList(SerializedProperty prop, string label)
        {
            var fold = new Foldout { text = LabelOf(prop, label), value = true, tooltip = prop.tooltip };
            SerializedObject so = prop.serializedObject;
            string path = prop.propertyPath;

            void Rebuild()
            {
                fold.Clear();
                var arr = so.FindProperty(path);
                if (arr == null) return;

                for (int i = 0; i < arr.arraySize; i++)
                    fold.Add(MakeStructFields(arr.GetArrayElementAtIndex(i), $"Запись {i + 1}"));

                var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 2 } };
                buttons.Add(new Button(() => { arr.arraySize = arr.arraySize + 1; so.ApplyModifiedProperties(); Rebuild(); })
                    { text = "+ запись" });
                if (arr.arraySize > 0)
                    buttons.Add(new Button(() => { arr.arraySize = arr.arraySize - 1; so.ApplyModifiedProperties(); Rebuild(); })
                        { text = "− последняя" });
                fold.Add(buttons);

                fold.Bind(so);
            }

            Rebuild();
            return fold;
        }

        // Текст [Header] поля targetType по имени сериализованного поля; null — если заголовка нет.
        static string HeaderFor(Type targetType, string fieldName)
        {
            if (targetType == null) return null;
            var f = GetSerializedField(targetType, fieldName);
            if (f == null) return null;
            var headers = f.GetCustomAttributes(typeof(HeaderAttribute), true);
            return headers.Length > 0 ? ((HeaderAttribute)headers[0]).header : null;
        }

        // Поиск поля по имени вверх по иерархии (partial-поля и поля базовых классов — на одном/родительском типе).
        static FieldInfo GetSerializedField(Type type, string name)
        {
            for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject); t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        // Русская метка значения enum по [InspectorName]; иначе — имя значения.
        public static string EnumLabel(Type enumType, string valueName)
        {
            var members = enumType.GetMember(valueName);
            if (members.Length > 0)
            {
                var attr = members[0].GetCustomAttribute<InspectorNameAttribute>();
                if (attr != null) return attr.displayName;
            }
            return valueName;
        }

        // Создать папку (по частям), если её нет.
        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return;

            var parts = folder.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets") return;
            string cur = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        /// <summary>
        /// Наименьший свободный id среди ассетов фильтра. Детерминированно: одинаковый вход — одинаковый выход.
        /// Общий для всех вкладок (правило 5): раньше жил приватной копией в «Умениях и эффекторах»,
        /// теперь его же зовут «Пассивные умения» и «Производство».
        /// </summary>
        /// <param name="filter">Фильтр AssetDatabase, например «t:Ability» или «t:Effector».</param>
        /// <param name="idOf">Как достать id из найденного ассета.</param>
        public static int NextFreeId(string filter, Func<UnityEngine.Object, int> idOf)
        {
            var used = new HashSet<int>();
            foreach (var g in AssetDatabase.FindAssets(filter))
            {
                var o = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(g));
                if (o != null) used.Add(idOf(o));
            }

            int id = 1;
            while (used.Contains(id)) id++;

            return id;
        }
    }
}
