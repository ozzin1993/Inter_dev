using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow 2026-08-17, ревью «долг редактора»] Единый дровер уникальных id вместо семи копий.
    /// Было: каждый дровер на КАЖДЫЙ кадр инспектора сканировал проект (FindAssets + Load), а Projectile/VFXLine —
    /// все префабы проекта дважды; новый id выдавался Random.Range(1, 99999) с риском коллизий.
    /// Стало: каталог «объект → id» кешируется и пересобирается только при изменении проекта
    /// (EditorApplication.projectChanged); свободный id — максимум существующих + 1, детерминированно.
    /// Поведение ассета сохранено: поле только для чтения; id == 0 или id-дубликат (копия ассета) → выдать свободный;
    /// оригинал при дубле не меняется — копию определяет Utils.GetTheDuplicate по имени.
    /// </summary>
    public abstract class InterflowIdDrawerBase : PropertyDrawer
    {
        static readonly Dictionary<string, List<(UnityEngine.Object obj, int id)>> catalogs =
            new Dictionary<string, List<(UnityEngine.Object obj, int id)>>();

        static InterflowIdDrawerBase()
        {
            EditorApplication.projectChanged += catalogs.Clear;
        }

        /// <summary>Ключ кеша (уникален на тип каталога).</summary>
        protected abstract string CatalogKey { get; }

        /// <summary>Полная пересборка каталога — вызывается только при устаревшем кеше.</summary>
        protected abstract List<(UnityEngine.Object obj, int id)> BuildCatalog();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            UnityEngine.Object self = property.serializedObject.targetObject;
            int id = property.intValue;

            if (id == 0 || HasForeignDuplicate(id, self))
            {
                int fresh = ResolveUniqueId(id, self);
                if (fresh != -1) property.intValue = fresh;
            }

            using (new EditorGUI.DisabledScope(true))
                EditorGUI.PropertyField(position, property, label, true);
        }

        List<(UnityEngine.Object obj, int id)> Catalog()
        {
            if (!catalogs.TryGetValue(CatalogKey, out var c) || c == null)
            {
                c = BuildCatalog();
                catalogs[CatalogKey] = c;
            }
            return c;
        }

        bool HasForeignDuplicate(int id, UnityEngine.Object self)
        {
            foreach (var e in Catalog())
                if (e.id == id && e.obj != self && e.obj != null) return true;
            return false;
        }

        // Свободный id; -1 — менять нельзя (мы оригинал, новый id получит копия в своём инспекторе)
        int ResolveUniqueId(int currentId, UnityEngine.Object self)
        {
            var cat = Catalog();

            if (currentId != 0)
            {
                foreach (var e in cat)
                    if (e.id == currentId && e.obj != self && e.obj != null)
                        if (!Utils.GetTheDuplicate(e.obj.name, self.name)) return -1;
            }

            int max = 0;
            foreach (var e in cat) if (e.id > max) max = e.id;
            int fresh = Mathf.Max(1, max + 1);

            cat.Add((self, fresh)); // сразу учтём себя — кеш не успел пересобраться
            return fresh;
        }

        // Каталог ScriptableObject-ассетов по фильтру поиска
        protected static List<(UnityEngine.Object obj, int id)> CollectAssets<T>(string filter, Func<T, int> idOf) where T : UnityEngine.Object
        {
            var list = new List<(UnityEngine.Object obj, int id)>();
            foreach (string guid in AssetDatabase.FindAssets(filter, null))
            {
                T o = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (o != null) list.Add((o, idOf(o)));
            }
            return list;
        }

        // Каталог компонентов на префабах (Projectile, VFXLine): один полный проход вместо прохода на каждый кадр
        protected static List<(UnityEngine.Object obj, int id)> CollectPrefabComponents<T>(Func<T, int> idOf) where T : Component
        {
            var list = new List<(UnityEngine.Object obj, int id)>();
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", null))
            {
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go == null) continue;
                T c = go.GetComponent<T>();
                if (c != null) list.Add((c, idOf(c)));
            }
            return list;
        }
    }

    [CustomPropertyDrawer(typeof(AbilityIDAttribute))]
    public class AbilityIDDrawer : InterflowIdDrawerBase
    {
        protected override string CatalogKey => "t:Ability";
        protected override List<(UnityEngine.Object obj, int id)> BuildCatalog() => CollectAssets<Ability>("t:Ability", a => a.id);
    }

    [CustomPropertyDrawer(typeof(ArmorTypeIDAttribute))]
    public class ArmorTypeIDDrawer : InterflowIdDrawerBase
    {
        protected override string CatalogKey => "t:ArmorType";
        protected override List<(UnityEngine.Object obj, int id)> BuildCatalog() => CollectAssets<ArmorType>("t:ArmorType", a => a.id);
    }

    [CustomPropertyDrawer(typeof(DamageTypeIDAttribute))]
    public class DamageTypeIDDrawer : InterflowIdDrawerBase
    {
        protected override string CatalogKey => "t:DamageType";
        protected override List<(UnityEngine.Object obj, int id)> BuildCatalog() => CollectAssets<DamageType>("t:DamageType", a => a.id);
    }

    [CustomPropertyDrawer(typeof(EffectorIDAttribute))]
    public class EffectorIDDrawer : InterflowIdDrawerBase
    {
        protected override string CatalogKey => "t:Effector";
        protected override List<(UnityEngine.Object obj, int id)> BuildCatalog() => CollectAssets<Effector>("t:Effector", e => e.id);
    }

    [CustomPropertyDrawer(typeof(TechnologyIDAttribute))]
    public class TechnologyIDDrawer : InterflowIdDrawerBase
    {
        protected override string CatalogKey => "t:Technology";
        protected override List<(UnityEngine.Object obj, int id)> BuildCatalog() => CollectAssets<Technology>("t:Technology", t => t.id);
    }

    [CustomPropertyDrawer(typeof(ProjectileIDAttribute))]
    public class ProjectileIDDrawer : InterflowIdDrawerBase
    {
        protected override string CatalogKey => "prefab:Projectile";
        protected override List<(UnityEngine.Object obj, int id)> BuildCatalog() => CollectPrefabComponents<Projectile>(p => p.id);
    }

    [CustomPropertyDrawer(typeof(VFXLineIDAttribute))]
    public class VFXLineIDDrawer : InterflowIdDrawerBase
    {
        protected override string CatalogKey => "prefab:VFXLine";
        protected override List<(UnityEngine.Object obj, int id)> BuildCatalog() => CollectPrefabComponents<VFXLine>(v => v.id);
    }
}
