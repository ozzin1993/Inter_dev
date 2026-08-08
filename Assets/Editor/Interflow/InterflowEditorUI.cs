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
            // Цель
            { "targetMode",               "Кого задевает" },
            { "buttonCast",               "Скилл кнопки (замок или герой)" },
            { "targetStrategy",           "Как выбрать одну цель" },
            { "searchOrigin",             "Откуда считать «ближайшего»" },
            { "strategyCategory",         "Боевая роль для стратегии" },
            { "strategyUseCurrentHealth", "Мерить текущее ХП, не максимальное" },
            { "strategyHpThreshold",      "Порог ХП (доля от максимума)" },
            // Фильтры целей
            { "targetCategories",         "Только эти боевые роли (пусто — любые)" },
            { "onlyMelee",                "Только ближний бой" },
            { "maxTargets",               "Максимум целей (0 — без лимита)" },
            { "multiPick",                "Кого оставить при лимите" },
            { "includeSelf",              "Включать самого кастера" },
            { "coneAngle",                "Угол конуса, градусы" },
            { "radius",                   "Радиус по уровням" },
            { "castRange",                "Дальность каста по уровням" },
            { "unitSelector",             "Кто вообще может быть целью" },
            // Доставка
            { "delivery",                 "Доставка" },
            { "projectilePrefab",         "Префаб снаряда" },
            { "projectileFollowsTarget",  "Снаряд самонаводится" },
            // Шкала каста и презентация
            { "castTime",                 "Время каста по уровням, с" },
            { "cooldown",                 "Откат по уровням, с" },
            { "manaCost",                 "Стоимость маны по уровням" },
            { "duration",                 "Длительность по уровням, с" },
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
            { "isItem",                   "Это предмет, а не умение" },
            { "useUponPickUp",            "Применить сразу при подборе" },
            { "dropOnDeath",              "Выпадает при смерти носителя" },
            { "charges",                  "Заряды (0 — без зарядов)" },
            { "maxLevels",                "Максимум уровней" },
            { "heroLevelable",            "Уровень качается прокачкой юнита" },
            { "dontTurn",                 "Не поворачивать кастера к цели" },
            { "continuous",               "Длящееся: активно, пока хватает маны и длительности" },
            { "interruptible",            "Кастера можно прервать" },
            { "requiresCastingUnit",      "Нужен активно кастующий юнит" },
            { "manaCostPerSecond",        "Мана в секунду по уровням" },
            { "cost",                     "Цена в ресурсах" },
            { "requiredTech",             "Требуемые технологии по уровням" },
            { "requiredLevel",            "Требуемый уровень юнита по уровням" },

            // Собственные поля производственных типов (вкладка «Производство»), тоже по тултипам ядра.
            { "building",                 "Что строится (по уровням)" },
            { "unitToTrain",              "Кого обучать" },
            { "unitCount",                "Сколько юнитов появится" },
            { "costForSingleUnit",        "Цена указана за одного юнита" },
            { "unlockTech",               "Что открывает: уровень 1 → первый элемент" },
            { "upgradeUnit",              "Во что апгрейдится здание" },
            { "upgradeTime",              "Время апгрейда, с" },
            { "transformUnit",            "Чей вид принимает кастер" },
            { "transformTime",            "Длительность превращения по уровням" },
            { "transformSound",           "Звук превращения" },
            { "transformVFX",             "Визуал превращения" },
            { "passiveEffects",           "Пассивные эффекты на время превращения" }
        };

        /// <summary>Русская подпись поля; null — перевода нет, пусть Unity рисует свою.</summary>
        public static string FieldLabel(string fieldName)
            => fieldName != null && FIELD_LABELS.TryGetValue(fieldName, out var s) ? s : null;

        /// <summary>
        /// PropertyField с явной подписью и необязательным подавлением декораторов ([Header]/[Space]).
        /// label = null — подпись по умолчанию (из имени поля).
        /// </summary>
        public static PropertyField MakeField(SerializedProperty prop, string label, bool suppressDecorators)
        {
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
    }
}
