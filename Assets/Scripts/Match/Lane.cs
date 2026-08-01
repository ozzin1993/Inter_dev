using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Контроллер линии. Хранит упорядоченный список точек интереса и выбирает цели для атаки
    /// и обороны по БЛИЗОСТИ К СЕРЕДИНЕ СПИСКА (индексу в массиве), а не по физическому расстоянию.
    /// Атака — ближайшая к центру точка, НЕ принадлежащая команде.
    /// Защита — ближайшая к центру точка, ПРИНАДЛЕЖАЩАЯ команде.
    /// </summary>
    public class Lane : MonoBehaviour
    {
        [Header("Точки линии")]
        [Tooltip("Массив точек интереса в порядке от замка A до замка B (слева направо). " +
                 "Порядок элементов задаёт близость к центру списка — физическое расстояние между точками игнорируется. " +
                 "Рекомендуемый порядок: [CastleA, Defence1A, Defence2A, Centre, Defence2B, Defence1B, CastleB]. " +
                 "Все элементы должны быть назначены.")]
        [SerializeField] PointOfInterest[] points;

        // ======================== ПУБЛИЧНЫЕ МЕТОДЫ ========================

        /// <summary>
        /// Цель атаки: ближайшая к центру списка точка, НЕ принадлежащая указанной команде.
        /// Возвращает Vector2.zero, если такой точки нет.
        /// </summary>
        /// <param name="team">Команда атакующих (Unit.team / SlotManager.playerTeam[ownerPlayer]).</param>
        public Vector2 NextAttackTarget(int team)
        {
            PointOfInterest p = PickPoint(team, wantOwned: false);
            return p != null ? p.Position2D : Vector2.zero;
        }

        /// <summary>
        /// Цель обороны: ближайшая к центру списка точка, ПРИНАДЛЕЖАЩАЯ указанной команде.
        /// Возвращает Vector2.zero, если своих точек нет.
        /// </summary>
        /// <param name="team">Команда обороняющихся (Unit.team / SlotManager.playerTeam[ownerPlayer]).</param>
        public Vector2 FrontmostOwnedPoint(int team)
        {
            PointOfInterest p = PickPoint(team, wantOwned: true);
            return p != null ? p.Position2D : Vector2.zero;
        }

        /// <summary>PointOfInterest следующей цели атаки. null — если нет.</summary>
        public PointOfInterest NextAttackTargetPoint(int team)   => PickPoint(team, wantOwned: false);

        /// <summary>PointOfInterest ближайшей точки защиты. null — если нет.</summary>
        public PointOfInterest FrontmostOwnedPointPoint(int team) => PickPoint(team, wantOwned: true);

        /// <summary>
        /// Единичная ось «к своей стороне» команды вдоль линии (XZ). −OwnSideAxis смотрит к врагу.
        /// Используется сетками защиты (DefenceGrid) для разворота строя лицом к врагу текущего владельца.
        /// Сторона определяется по teamAffiliation крайних точек линии (замков-концов).
        /// Vector2.zero — если линия не задана или сторону определить нельзя.
        /// </summary>
        public Vector2 OwnSideAxis(int team)
        {
            if (points == null || points.Length < 2) return Vector2.zero;
            PointOfInterest a = points[0];
            PointOfInterest b = points[points.Length - 1];
            if (a == null || b == null) return Vector2.zero;

            Vector2 toA = a.Position2D - b.Position2D; // от конца B к концу A
            if (toA.sqrMagnitude < 1e-6f) return Vector2.zero;
            toA.Normalize();

            if ((int)a.teamAffiliation == team) return toA;   // своя сторона — конец A
            if ((int)b.teamAffiliation == team) return -toA;  // своя сторона — конец B

            for (int i = 0; i < points.Length; i++)
            {
                PointOfInterest pt = points[i];
                if (pt == null || (int)pt.teamAffiliation != team) continue;
                return (i * 2 < points.Length) ? toA : -toA;
            }
            return Vector2.zero;
        }

        /// <summary>Индекс точки в массиве points (для сетевой синхронизации владения). -1, если не найдена.</summary>
        public int IndexOf(PointOfInterest point)
        {
            if (points == null) return -1;
            for (int i = 0; i < points.Length; i++)
                if (points[i] == point) return i;
            return -1;
        }

        /// <summary>Применить владельца к точке по индексу (вызывает клиент по сетевому RPC).</summary>
        public void SetPointTeam(int index, int team)
        {
            if (points == null || index < 0 || index >= points.Length || points[index] == null) return;
            points[index].SetTeam(team);
        }

        // ======================== ДИАГНОСТИКА ========================

        /// <summary>Отладка: список состояний всех точек для проверки таргетинга.</summary>
        public System.Collections.Generic.IEnumerable<string> DebugPoints(int team)
        {
            if (points == null) yield break;
            for (int i = 0; i < points.Length; i++)
            {
                PointOfInterest p = points[i];
                if (p == null) { yield return $"[{i}]=null"; continue; }
                yield return $"[{i}]{p.name}(ID={p.GetInstanceID()}) " +
                             $"team={p.CurrentTeam} towerAlive={p.TowerAlive} type={p.type} " +
                             $"ownedByAttacker={p.IsOwnedByTeam(team)}";
            }
        }

        // ======================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ========================

        // Выбор точки по близости к середине массива (индекс (N-1)/2).
        // wantOwned=false — кандидаты, НЕ принадлежащие команде (атака); true — принадлежащие (защита).
        // Ничья (равная дистанция по разные стороны) решается по teamAffiliation:
        // для атаки приоритет точкам на стороне врага, для защиты — на своей стороне.
        // Возвращает PointOfInterest (или null, если нет подходящей точки).
        private PointOfInterest PickPoint(int team, bool wantOwned)
        {
            if (points == null || points.Length == 0) return null;

            float centre   = (points.Length - 1) * 0.5f;
            int   best     = -1;
            float bestDist = float.MaxValue;
            int   bestSide = -1;

            for (int i = 0; i < points.Length; i++)
            {
                PointOfInterest p = points[i];
                if (p == null) continue;
                if (p.IsOwnedByTeam(team) != wantOwned) continue;
                // Точки без живой башни пропускаем — Defence-башня захвачена и выбыла.
                // Castle и Centre не трогаем: Castle — конечная цель (может не иметь unitRef в POI),
                // Centre — перестраивается и может быть временно без башни.
                if (!p.TowerAlive
                    && p.type != PointOfInterest.PointType.Centre
                    && p.type != PointOfInterest.PointType.Castle) continue;

                float dist = Mathf.Abs(i - centre);
                int   side = SidePreference(p, team, wantOwned);

                bool closer    = dist < bestDist - 0.0001f;
                bool tieBetter = Mathf.Abs(dist - bestDist) <= 0.0001f && side > bestSide;
                if (closer || tieBetter)
                {
                    bestDist = dist;
                    bestSide = side;
                    best     = i;
                }
            }

            return best >= 0 ? points[best] : null;
        }

        // Приоритет стороны при равной дистанции:
        // атака  — точка структурно на стороне врага (teamAffiliation задана и != своя команда);
        // защита — точка структурно на своей стороне (teamAffiliation == своя команда).
        private static int SidePreference(PointOfInterest p, int team, bool wantOwned)
        {
            int aff = (int)p.teamAffiliation;
            if (wantOwned)
                return aff == team ? 1 : 0;
            return (aff >= 0 && aff != team) ? 1 : 0;
        }
    }
}
