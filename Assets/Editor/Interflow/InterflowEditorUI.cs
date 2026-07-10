using System;
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
                currentBody.Add(MakeField(it, header != null));
            }

            container.Bind(so);
            return container;
        }

        static PropertyField MakeField(SerializedProperty prop, bool suppressDecorators)
        {
            var pf = new PropertyField(prop.Copy());
            if (suppressDecorators)
            {
                // Прячем контейнер декораторов (Header/Space) этого поля после первой раскладки — без правки самого поля.
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
