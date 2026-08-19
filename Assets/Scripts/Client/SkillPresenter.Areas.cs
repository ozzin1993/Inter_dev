using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow 2026-08-06] Презентер: ОБЛАСТИ ДЕЙСТВИЯ и КРУГИ АУР.
    ///
    /// Область показывается с начала замаха и до конца действия, видна всем игрокам (с гейтом тумана).
    /// Геометрию берём из ассета умения — по сети она не ходит: `targetMode`, `radius[уровень]`, `coneAngle`
    /// одинаковы на всех пирах, а позиция и поворот берутся у кастера локально.
    ///
    /// Круг — штатная декаль-проектор (`ReferenceManager.rangeProjector` + `ProjectorHelper.SetSize`),
    /// ничего нового не заводим (правило 2). Сектор конуса штатного средства не имеет — строим
    /// процедурный меш; материал для него задаётся в настройках, выдумывать его в коде нельзя (правило 3).
    /// </summary>
    public partial class SkillPresenter
    {
        /// <summary>Живая область на экране.</summary>
        class AreaView
        {
            public Transform obj;      // объект декали или меша
            public Unit anchor;        // юнит, за которым область следует; null — область стоит в точке
            public Vector3 point;      // точка для областей без носителя (гейт тумана считается по ней)
            public float remaining;    // сколько ещё жить, сек; отрицательное — до отмены снаружи
            public bool facesAnchor;   // область направленная (конус) — каждый кадр разворачивается по взгляду носителя
        }

        // Область замаха — по одной на кастера: юнит одновременно кастует одно умение.
        readonly Dictionary<Unit, AreaView> castAreas = new Dictionary<Unit, AreaView>();

        // Области, доживающие свой срок после срабатывания (длительность умения либо затухание).
        readonly List<AreaView> timedAreas = new List<AreaView>();

        // Постоянные круги аур: по одному на юнита с аурой.
        readonly Dictionary<Unit, AreaView> auraAreas = new Dictionary<Unit, AreaView>();

        bool coneMaterialWarned;   // о недостающем материале говорим один раз, а не каждый каст

        // ============================== ЗАМАХ ==============================

        /// <summary>Каст начался: показать область, если умение областное.</summary>
        void HandleCastStarted(Unit caster, int abilityID, Unit targetUnit, Vector3 targetPoint)
        {
            HandleCastStopped(caster);   // предыдущая область того же кастера, если осталась

            CompositeSkill skill = ResolveSkill(abilityID);
            if (skill == null || caster == null) return;

            AreaView view = CreateAreaForCast(skill, caster);
            if (view == null) return;

            view.remaining = -1f;        // висит до срабатывания или прерывания
            castAreas[caster] = view;
        }

        /// <summary>Каст прерван: погасить область немедленно.</summary>
        void HandleCastStopped(Unit caster)
        {
            if (caster == null) return;
            if (!castAreas.TryGetValue(caster, out AreaView view)) return;

            DestroyArea(view);
            castAreas.Remove(caster);
        }

        // ============================== СРАБАТЫВАНИЕ ==============================

        /// <summary>
        /// Умение сработало: область замаха доживает свой срок, а у умений «умный выбор точки»
        /// область только сейчас и появляется — до срабатывания точки не существует.
        /// </summary>
        void AreaOnSkillFired(CompositeSkill skill, Unit caster, int level, Vector3 aimPoint)
        {
            float life = AreaLifeAfterFire(skill, level);

            // Область замаха: снимаем с учёта каста и отдаём таймеру.
            if (caster != null && castAreas.TryGetValue(caster, out AreaView view))
            {
                castAreas.Remove(caster);
                if (life <= 0f) { DestroyArea(view); }
                else { view.remaining = life; timedAreas.Add(view); }
                return;
            }

            // Замаха не было (мгновенный каст) либо область привязана к точке — создаём сейчас.
            // При нулевом времени жизни не создаём вовсе: мгновенное умение гасит область в момент
            // срабатывания, и вспышка на один кадр была бы мусором.
            if (life <= 0f) return;

            AreaView fresh = CreateAreaAfterFire(skill, caster, level, aimPoint);
            if (fresh == null) return;

            fresh.remaining = life;
            timedAreas.Add(fresh);
        }

        /// <summary>Сколько область живёт после срабатывания: длительность умения либо затухание из настроек.</summary>
        float AreaLifeAfterFire(CompositeSkill skill, int level)
        {
            float duration = InterflowAbility.LevelValue(skill.duration, level);
            return duration > 0f ? duration : Settings.instantAreaFadeSeconds;
        }

        // ============================== СОЗДАНИЕ ==============================

        // Область на замахе: она всегда привязана к кастеру — точки приложения ещё нет.
        AreaView CreateAreaForCast(CompositeSkill skill, Unit caster)
        {
            int level = ResolveLevel(caster, skill);
            float radius = InterflowAbility.LevelValue(skill.radius, level);
            if (radius <= 0f) return null;

            switch (skill.targetMode)
            {
                case SkillTargetMode.AreaAroundSelf: return NewCircle(radius, caster, caster.transform.position);
                case SkillTargetMode.Cone:           return NewCone(radius, skill.coneAngle, caster);
                default:                             return null;   // на себя, вся команда, умный выбор юнита — области нет
            }
        }

        // Область после срабатывания: у «умного выбора точки» она стоит в точке, у остальных — у кастера.
        AreaView CreateAreaAfterFire(CompositeSkill skill, Unit caster, int level, Vector3 aimPoint)
        {
            float radius = InterflowAbility.LevelValue(skill.radius, level);
            if (radius <= 0f) return null;

            switch (skill.targetMode)
            {
                case SkillTargetMode.SmartPoint:     return NewCircle(radius, null, aimPoint);
                case SkillTargetMode.AreaAroundSelf: return caster != null ? NewCircle(radius, caster, caster.transform.position) : null;
                case SkillTargetMode.Cone:           return caster != null ? NewCone(radius, skill.coneAngle, caster) : null;
                default:                             return null;
            }
        }

        // Круг — штатная декаль-проектор. Родитель-кастер даёт слежение за ним даром.
        AreaView NewCircle(float radius, Unit anchor, Vector3 point)
        {
            if (ReferenceManager.instance == null || ReferenceManager.instance.rangeProjector == null) return null;

            Transform t = Instantiate(ReferenceManager.instance.rangeProjector);
            t.position = point;
            if (anchor != null) t.SetParent(anchor.transform, true);

            ProjectorHelper.SetSize(t, radius);

            return new AreaView { obj = t, anchor = anchor, point = point };
        }

        // Сектор конуса: штатной формы нет, строим меш.
        // Родитель — корень юнита (позиция и масштаб), а НАПРАВЛЕНИЕ ставится каждый кадр в UpdateAreas
        // по Unit.LookDirection: корень юнита не вращается, весь поворот движок пишет в horizontalPart
        // (Unit.Transform.cs). Прежний localRotation = identity давал меш, намертво смотрящий в одну
        // сторону мира — та же ошибка, что была в наборе целей у конуса.
        AreaView NewCone(float radius, float angleDegrees, Unit anchor)
        {
            if (anchor == null) return null;

            Material material = Settings.coneMaterial;
            if (material == null)
            {
                if (!coneMaterialWarned)
                {
                    coneMaterialWarned = true;
                    Debug.LogWarning("[SkillPresenter] Не задан материал сектора конуса — конусные области не показываются. " +
                                     "Заполните поле в ассете настроек презентации умений (Resources/" +
                                     SkillPresentationSettings.ResourcePath + ").");
                }
                return null;
            }

            GameObject go = new GameObject("SkillConeArea");
            go.transform.SetParent(anchor.transform, false);
            go.transform.localPosition = new Vector3(0f, Settings.groundLift, 0f);

            go.AddComponent<MeshFilter>().sharedMesh = GetConeMesh(radius, angleDegrees, Settings.coneSegments);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            AreaView view = new AreaView { obj = go.transform, anchor = anchor, point = anchor.transform.position, facesAnchor = true };
            FaceAnchor(view);   // первый кадр — сразу в нужную сторону, без мелькания
            return view;
        }

        // Кеш мешей сектора по ключу (радиус, угол, сегменты). Меш — самостоятельный объект Unity:
        // Destroy игрового объекта его НЕ удаляет, без кеша каждый показ конуса тёк памятью (ревью, блок «баги»).
        static readonly System.Collections.Generic.Dictionary<(float radius, float angle, int segments), Mesh> coneMeshCache = new();

        // Достаёт меш из кеша или строит и кеширует. Уничтоженный извне меш перестраивается.
        static Mesh GetConeMesh(float radius, float angleDegrees, int segments)
        {
            var key = (radius, angleDegrees, segments);
            if (!coneMeshCache.TryGetValue(key, out Mesh mesh) || mesh == null)
            {
                mesh = BuildConeMesh(radius, angleDegrees, segments);
                coneMeshCache[key] = mesh;
            }
            return mesh;
        }

        /// <summary>
        /// Плоский веер в локальных осях: остриё в нуле, дуга радиуса radius симметрично вокруг направления «вперёд».
        /// Угол 360 и больше даёт полный круг — тот же случай, что и в наборе целей у самого умения.
        /// </summary>
        static Mesh BuildConeMesh(float radius, float angleDegrees, int segments)
        {
            angleDegrees = Mathf.Clamp(angleDegrees, 1f, 360f);
            segments = Mathf.Max(3, segments);

            Vector3[] vertices = new Vector3[segments + 2];
            vertices[0] = Vector3.zero;

            float half = angleDegrees * 0.5f;
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(-half, half, (float)i / segments);
                vertices[i + 1] = new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
            }

            int[] triangles = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }

            Mesh mesh = new Mesh { name = "SkillConeArea" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ============================== КРУГИ АУР ==============================

        /// <summary>
        /// Юнит инициализирован: если среди его умений есть помеченные «показывать круг радиуса» —
        /// завести постоянную окружность. Событие приходит уже после инициализации умений,
        /// поэтому уровни и блокировки читать можно.
        /// </summary>
        void HandleUnitReady(Unit unit)
        {
            if (unit == null || unit.dead || auraAreas.ContainsKey(unit)) return;

            float radius = RadiusCircleSize(unit);
            if (radius <= 0f) return;

            AreaView view = NewCircle(radius, unit, unit.transform.position);
            if (view != null) auraAreas[unit] = view;
        }

        /// <summary>Юнит убран с этого пира: снять его круг.</summary>
        void HandleUnitGone(Unit unit)
        {
            if (unit == null || !auraAreas.TryGetValue(unit, out AreaView view)) return;

            DestroyArea(view);
            auraAreas.Remove(unit);
        }

        /// <summary>
        /// Радиус постоянной окружности юнита: наибольший среди его умений с признаком
        /// «показывать круг радиуса». Умений с таким признаком может быть несколько —
        /// рисуем одну окружность по самой широкой, иначе кольца наложились бы друг на друга.
        /// </summary>
        static float RadiusCircleSize(Unit unit)
        {
            if (unit.abilities == null) return 0f;

            float best = 0f;
            for (int i = 0; i < unit.abilities.Length; i++)
            {
                Ability ability = unit.abilities[i];
                if (ability == null || !ability.showRadiusCircle) continue;

                float radius = InterflowAbility.LevelValue(ability.radius, ResolveLevel(unit, ability));
                if (radius > best) best = radius;
            }
            return best;
        }

        // ============================== ЖИЗНЬ ==============================

        void UpdateAreas()
        {
            float dt = Time.deltaTime;

            // Таймеры областей после срабатывания.
            for (int i = timedAreas.Count - 1; i >= 0; i--)
            {
                timedAreas[i].remaining -= dt;
                if (timedAreas[i].remaining > 0f) continue;

                DestroyArea(timedAreas[i]);
                timedAreas.RemoveAt(i);
            }

            // Туман войны: декаль не MeshRenderer, штатное скрытие юнита её не гасит — гасим сами.
            // Заодно доворачиваем направленные области: родитель-корень юнита не вращается.
            foreach (KeyValuePair<Unit, AreaView> pair in castAreas) { ApplyFog(pair.Value); FaceAnchor(pair.Value); }
            foreach (KeyValuePair<Unit, AreaView> pair in auraAreas) { ApplyFog(pair.Value); FaceAnchor(pair.Value); }
            for (int i = 0; i < timedAreas.Count; i++) { ApplyFog(timedAreas[i]); FaceAnchor(timedAreas[i]); }
        }

        /// <summary>
        /// Развернуть направленную область по взгляду носителя. Направление берём у юнита
        /// (Unit.LookDirection) — единственный источник правды: корневой transform юнита не вращается,
        /// поворот живёт на horizontalPart с компенсацией начального разворота модели.
        /// </summary>
        static void FaceAnchor(AreaView view)
        {
            if (view == null || !view.facesAnchor || view.obj == null || view.anchor == null) return;

            view.obj.rotation = Quaternion.LookRotation(view.anchor.LookDirection, Vector3.up);
        }

        static void ApplyFog(AreaView view)
        {
            if (view == null || view.obj == null) return;

            bool visible = view.anchor != null
                ? view.anchor.FoWVisible
                : InterflowAbility.VisibleForLocalViewer(view.point);

            if (view.obj.gameObject.activeSelf != visible) view.obj.gameObject.SetActive(visible);
        }

        static void DestroyArea(AreaView view)
        {
            if (view != null && view.obj != null) Destroy(view.obj.gameObject);
        }

        /// <summary>Снять все области — при смене сцены объекты уже уничтожены вместе с ней.</summary>
        void ClearAreas()
        {
            castAreas.Clear();
            timedAreas.Clear();
            auraAreas.Clear();
        }

        // ============================== ОБЩЕЕ ==============================

        /// <summary>Уровень, на котором юнит владеет умением. Не нашли — нулевой.</summary>
        static int ResolveLevel(Unit unit, Ability ability)
        {
            if (unit == null || unit.abilities == null || ability == null) return 0;

            for (int i = 0; i < unit.abilities.Length; i++)
            {
                if (unit.abilities[i] != ability) continue;
                return (unit.abilityLevel != null && i < unit.abilityLevel.Length) ? Mathf.Max(0, unit.abilityLevel[i]) : 0;
            }
            return 0;
        }
    }
}
