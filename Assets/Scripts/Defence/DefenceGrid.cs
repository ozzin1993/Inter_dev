using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Сетка слотов перед точкой интереса (башня/замок). Вешается на отдельный объект, связанный со
    /// своей <see cref="PointOfInterest"/>. Даёт мировую позицию слота по (приоритет, индекс).
    /// Раскладка общая для всех сеток — из единого <see cref="DefenceGridConfig"/> (слот №N означает
    /// одно и то же место везде). Ассет StrategyCore не трогается.
    ///
    /// Ориентация («перёд» = к врагу) берётся от ТЕКУЩЕГО владельца точки по оси линии
    /// (−<see cref="Lane.OwnSideAxis"/>): при захвате врагом раскладка автоматически разворачивается на
    /// другую сторону (один объект обслуживает обе команды, корректно и для нейтральной точки — Центра).
    /// Центр строя по ширине — сама точка; передний ряд впереди на <c>forwardOffset</c>. Позиция объекта
    /// на слоты НЕ влияет (только организационно/как запасное направление); гизмо рисует реальные места.
    /// MatchManager выбирает активную сетку = сетка передовой своей точки (логическая активность).
    /// </summary>
    public class DefenceGrid : MonoBehaviour
    {
        [Header("Связи")]
        [Tooltip("Точка интереса, перед которой стоит сетка. Владелец точки задаёт сторону (к врагу).")]
        [SerializeField] PointOfInterest point;

        [Tooltip("Общий конфиг раскладки (один ассет на ВСЕ сетки): радиус слота, слотов в ряду, рядов на приоритет, число приоритетов.")]
        [SerializeField] DefenceGridConfig config;

        [Tooltip("Линия — для направления строя к врагу (−OwnSideAxis владельца). Если пусто — найдётся автоматически.")]
        [SerializeField] Lane lane;

        [Header("Положение")]
        [Tooltip("Расстояние переднего ряда ПЕРЕД точкой вдоль направления к врагу. Двигает сетку вперёд/назад.")]
        [SerializeField] float forwardOffset = 2f;

        [Header("Ориентация")]
        [Tooltip("Ручная ориентация: «перёд» строя = поворот ЭТОГО объекта (его forward в плоскости XZ), а НЕ авто к врагу по линии. При захвате точки строй не разворачивается автоматически — направление задаёшь поворотом объекта в сцене (гизмо обновляется сразу).")]
        [SerializeField] bool manualFacing = false;

        [Header("Гизмо")]
        [Tooltip("Рисовать слоты в окне Scene (только редактор, в игре не видно).")]
        [SerializeField] bool drawGizmos = true;

        // ======================== РЕЕСТР ========================
        // Статический список всех сеток — MatchManager находит сетку по точке без порядка инициализации.
        static readonly List<DefenceGrid> all = new List<DefenceGrid>();

        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDisable() { all.Remove(this); }

        /// <summary>Точка, к которой привязана сетка.</summary>
        public PointOfInterest Point => point;

        /// <summary>Найти сетку, привязанную к указанной точке. null — если такой нет.</summary>
        public static DefenceGrid For(PointOfInterest poi)
        {
            if (poi == null) return null;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].point == poi) return all[i];
            return null;
        }

        // ======================== РАСЧЁТ ========================

        /// <summary>Мировая позиция слота (приоритет 1.., индекс заполнения 0..) с учётом владельца точки.</summary>
        public Vector3 SlotWorld(int priority, int index)
        {
            if (!Resolve(out Vector2 fwd, out Vector2 right, out Vector2 row0, out float step))
                return transform.position;

            int spr = Mathf.Max(1, config.slotsPerRow);
            int rpp = Mathf.Max(1, config.rowsPerPriority);

            int rowInBand = index / spr;                       // ряд внутри приоритета (0..rpp-1)
            int colIdx    = index % spr;                       // позиция в ряду
            int globalRow = (Mathf.Max(1, priority) - 1) * rpp + rowInBand; // приоритет 1 → ряд 0 (перёд)
            int col = ColSeq(colIdx);                          // от центра ряда наружу

            Vector2 p = row0 - fwd * (globalRow * step) + right * (col * step);
            float y = point != null ? point.transform.position.y : transform.position.y;
            return new Vector3(p.x, y, p.y);
        }

        /// <summary>Поворот (yaw, градусы) лицом к врагу текущего владельца — для Unit.Spawn.</summary>
        public float Facing()
        {
            if (!Resolve(out Vector2 fwd, out _, out _, out _)) return 0f;
            return Mathf.Atan2(fwd.x, fwd.y) * Mathf.Rad2Deg;
        }

        // Базис сетки: forward (к врагу владельца по оси линии), right, центр переднего ряда (row0), шаг.
        // false — если нет точки/конфига.
        bool Resolve(out Vector2 fwd, out Vector2 right, out Vector2 row0, out float step)
        {
            fwd = Vector2.up; right = Vector2.right; row0 = Vector2.zero; step = 1f;
            if (point == null || config == null) return false;

            // Владелец: в игре — текущий; в редакторе (не Play) — начальный (teamAffiliation).
            int owner = Application.isPlaying ? point.CurrentTeam : (int)point.teamAffiliation;

            // Направление к врагу = минус оси к своему замку. Ручной режим / нейтральный владелец / нет линии → по transform объекта.
            Vector2 dir = Vector2.zero;
            if (!manualFacing)
            {
                Lane ln = ResolveLane();
                dir = (ln != null && owner >= 0) ? ln.OwnSideAxis(owner) : Vector2.zero;
            }
            if (dir.sqrMagnitude > 0.0001f)
            {
                fwd = (-dir).normalized;
            }
            else
            {
                // Ручной режим ИЛИ нет линии/владельца → направление по transform этого объекта (поворот в сцене).
                Vector3 f = transform.forward;
                fwd = new Vector2(f.x, f.z);
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector2.up;
                fwd.Normalize();
            }

            right = new Vector2(fwd.y, -fwd.x);
            row0 = point.Position2D + fwd * forwardOffset;
            step = config.SlotStep;
            return true;
        }

        // Линия из поля или, если пусто, найденная в сцене (кэш). Работает и в редакторе (для гизмо).
        Lane cachedLane;
        Lane ResolveLane()
        {
            if (lane != null) return lane;
            if (cachedLane == null) cachedLane = FindFirstObjectByType<Lane>();
            return cachedLane;
        }

        // Центрированная последовательность колонок: 0, +1, -1, +2, -2, ... (от центра ряда наружу).
        static int ColSeq(int i)
        {
            if (i <= 0) return 0;
            int mag = (i + 1) / 2;
            return (i % 2 == 1) ? mag : -mag;
        }

        // ======================== ГИЗМО (только редактор) ========================
        void OnDrawGizmos()
        {
            if (!drawGizmos || config == null || point == null) return;

            int spr = Mathf.Max(1, config.slotsPerRow);
            int rpp = Mathf.Max(1, config.rowsPerPriority);
            int prio = Mathf.Max(1, config.priorities);
            float r = config.slotRadius;

            for (int p = 1; p <= prio; p++)
            {
                // Цвет по приоритету: передний (1) — краснее, дальний — синее.
                float t = prio > 1 ? (p - 1f) / (prio - 1f) : 0f;
                Gizmos.color = Color.Lerp(new Color(1f, 0.3f, 0.3f, 0.9f), new Color(0.3f, 0.5f, 1f, 0.9f), t);
                int capacity = spr * rpp;
                for (int idx = 0; idx < capacity; idx++)
                    Gizmos.DrawWireSphere(SlotWorld(p, idx), r);
            }
        }
    }
}
