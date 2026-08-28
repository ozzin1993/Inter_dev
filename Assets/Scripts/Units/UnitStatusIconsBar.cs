using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Шкала статусов: ряд иконок-квадов над полоской здоровья юнита (мир, вариант А — как HealthBar).
    /// Решения Artsiom 2026-08-05: показывать всем юнитам со статусом; ряд удлиняется без лимита;
    /// порядок — по наложению; остаток длительности не показывается.
    ///
    /// Чистая презентация (правило 6): состав значков даёт ЕДИНЫЙ перечислитель UnitStatusIcons —
    /// тот же, что кормит Status-таб панели. Обновление — по событию Unit.OnStatusUpdate (эффекторы,
    /// присланные статусы) плюс дешёвый опрос статусов ядра раз в тик (5-битовый слепок): стан/немоту/
    /// безоружие ядро меняет без события. Настройки (размер, зазор, отступ, материал) — в ассете
    /// StatusIconCatalog (правило 3). Добавляется ядром в Unit.Initialize рядом с HealthBar;
    /// на выделенном сервере не создаётся (Utils.Headless — показывать некому).
    /// </summary>
    public class UnitStatusIconsBar : MonoBehaviour
    {
        Unit unit;
        Transform root;                    // контейнер ряда (создаётся при первом статусе)
        Material material;                 // общий материал квадов (шаблон из каталога)
        MaterialPropertyBlock mpb;
        bool subscribed;
        int lastCoreHash;                  // слепок статусов ядра с прошлого тика
        bool dirty;                        // OnStatusUpdate пришёл — пересобрать на ближайшем тике
        bool hasIcons;                     // в ряду есть значки (состав — отдельно от видимости юнита)
        Transform coreHealthBar;           // штатная полоска HealthBar(Clone) — зеркало видимости юнита

        readonly List<UnitStatusIcons.Entry> buffer = new List<UnitStatusIcons.Entry>();
        readonly List<MeshRenderer> quads = new List<MeshRenderer>();

        static Mesh quadMesh;                                              // один меш на все шкалы
        int texProp = -1;                                                  // текстурное свойство материала (резолвится по его шейдеру)

        // Кандидаты имени текстурного свойства: URP/Unlit, граф LookAtCamera, легаси-шейдеры.
        static readonly int PropBaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int PropMain = Shader.PropertyToID("_Main");
        static readonly int PropMainTex = Shader.PropertyToID("_MainTex");

        void Start()
        {
            unit = GetComponent<Unit>();
            if (unit == null || Utils.Headless) { enabled = false; return; }

            mpb = new MaterialPropertyBlock();

            unit.OnStatusUpdate += MarkDirty;
            unit.OnDie += OnUnitDie;

            if (GameManager.Instance != null)
            {
                GameManager.Instance.Tick += OnTick;
                subscribed = true;
            }
        }

        void MarkDirty() => dirty = true;

        // Две обязанности каждый кадр:
        // 1) Видимость (решение Artsiom 2026-08-06): ряд прячется вместе со штатной полоской
        //    HealthBar(Clone) — её гасит/включает само ядро в HideRenderers/ShowRenderers (туман
        //    войны, невидимость). Зеркалим её активность вместо дублирования предикатов
        //    (FogOfWar.IsVisible + Unit.IsVisible) — один источник истины, без правок ядра.
        //    При смене команды ядро пересоздаёт полоску — ссылка обновляется по Find.
        // 2) Разворот ряда к камере — скриптом, а не шейдером: вершинная математика графа ядра
        //    LookAtCamera рассчитана на меши его штатных VFX — квады шкалы с ним не отрисовываются
        //    (проверено вживую 2026-08-06, тесты на штатном Quad и своём меше с тангенсами).
        void LateUpdate()
        {
            if (root == null || !hasIcons) return;

            bool shown = UnitShownByCore();
            if (root.gameObject.activeSelf != shown) root.gameObject.SetActive(shown);
            if (!shown) return;

            Camera cam = Utils.MainCamera;
            if (cam != null) root.rotation = cam.transform.rotation;
        }

        /// <summary>Показывает ли ядро юнита локальному игроку (по активности штатной полоски).</summary>
        bool UnitShownByCore()
        {
            if (coreHealthBar == null) coreHealthBar = transform.Find("HealthBar(Clone)");
            // Полоски нет (момент пересоздания/особый юнит) — считаем видимым, как до этой правки.
            return coreHealthBar == null || coreHealthBar.gameObject.activeSelf;
        }

        void OnUnitDie(Unit unitThatDies, int playerKiller, Unit unitKiller, bool rewards)
        {
            HideAll();
            Unsubscribe();
        }

        void OnDestroy() => Unsubscribe();

        void Unsubscribe()
        {
            if (unit != null)
            {
                unit.OnStatusUpdate -= MarkDirty;
                unit.OnDie -= OnUnitDie;
            }
            if (subscribed && GameManager.Instance != null) GameManager.Instance.Tick -= OnTick;
            subscribed = false;
        }

        // ================================================================ ТИК ==

        void OnTick()
        {
            if (unit == null || unit.dead) { HideAll(); Unsubscribe(); return; }

            // Статусы ядра события не дают — ловим их дешёвым слепком раз в тик (0.1 с).
            int coreHash = UnitStatusIcons.CoreStatusHash(unit);
            if (coreHash != lastCoreHash) { lastCoreHash = coreHash; dirty = true; }

            if (!dirty) return;
            dirty = false;
            Rebuild();
        }

        // ============================================================ ПОСТРОЕНИЕ ==

        void Rebuild()
        {
            int count = UnitStatusIcons.Collect(unit, buffer);
            hasIcons = count > 0;
            if (count == 0) { HideAll(); return; }

            StatusIconCatalog cat = StatusIconCatalog.Get();
            float size = cat != null ? cat.iconSize : 0.45f;
            float gap = cat != null ? cat.iconGap : 0.08f;
            float offsetY = cat != null ? cat.barOffsetY : 0.25f;

            EnsureRoot(offsetY);
            EnsureMaterial(cat);
            EnsureQuads(count, size);

            // Ряд удлиняется без лимита (решение §9.4), центрируется над юнитом.
            float step = size + gap;
            float x0 = -0.5f * step * (count - 1);
            for (int i = 0; i < count; i++)
            {
                MeshRenderer quad = quads[i];
                quad.gameObject.SetActive(true);
                quad.transform.localPosition = new Vector3(x0 + i * step, 0f, 0f);

                quad.GetPropertyBlock(mpb);
                mpb.SetTexture(texProp, buffer[i].icon);
                quad.SetPropertyBlock(mpb);
            }
            for (int i = count; i < quads.Count; i++) quads[i].gameObject.SetActive(false);

            // Не раскрывать ряд, пока ядро прячет юнита (состав при этом обновлён — при появлении
            // из тумана LateUpdate просто включит готовый ряд).
            root.gameObject.SetActive(UnitShownByCore());
        }

        void HideAll()
        {
            hasIcons = false;
            if (root != null) root.gameObject.SetActive(false);
        }

        void EnsureRoot(float offsetY)
        {
            if (root != null) return;

            root = new GameObject("StatusIconsBar").transform;
            root.SetParent(transform, false);
            // Та же высота, что у полоски здоровья (HealthBar.Start), плюс отступ ряда над ней.
            root.localPosition = new Vector3(0f, unit.unitHeight * 1.3f + offsetY, 0f);
        }

        void EnsureMaterial(StatusIconCatalog cat)
        {
            if (material != null) return;

            if (cat != null && cat.iconMaterial != null) material = cat.iconMaterial;
            else
            {
                // Фолбэк без каталога: URP/Unlit с прозрачностью (граф ядра LookAtCamera шкале
                // не подходит — см. комментарий у LateUpdate).
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = 3000;
            }

            // Имя текстурного свойства зависит от шейдера назначенного в каталоге материала.
            texProp = material.HasProperty(PropBaseMap) ? PropBaseMap
                    : material.HasProperty(PropMain) ? PropMain
                    : PropMainTex;
        }

        void EnsureQuads(int count, float size)
        {
            while (quads.Count < count)
            {
                GameObject go = new GameObject("StatusIcon");
                go.transform.SetParent(root, false);

                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = GetQuadMesh();

                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

                quads.Add(mr);
            }

            for (int i = 0; i < quads.Count; i++)
                quads[i].transform.localScale = new Vector3(size, size, 1f);
        }

        // Квад 1×1 в плоскости XY, пивот в центре (штатного примитива без коллайдера нет — строим один раз).
        static Mesh GetQuadMesh()
        {
            if (quadMesh != null) return quadMesh;

            quadMesh = new Mesh { name = "StatusIconQuad" };
            quadMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
            };
            quadMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            quadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quadMesh.RecalculateNormals();
            quadMesh.RecalculateTangents(); // граф-шейдерам с тангенс-базисом без них достаётся мусор
            quadMesh.RecalculateBounds();
            return quadMesh;
        }
    }
}
