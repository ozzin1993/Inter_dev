using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // ============================= ОБЛИК ГЛАВНОГО ЗДАНИЯ ПО УРОВНЮ (партиал MatchManager) ==
    // При смене уровня ГЗ (штатное событие OnMainBuildingLevelChanged) меняет ОБЛИК замка на префаб уровня
    // штатным Unit.ReplaceRenderers(shape, replacePermanently:true) — на ТОМ ЖЕ объекте (NetID цел → условие
    // победы specificUnitsDead и ссылки не рвутся; это НЕ пересоздание через UpgradeBuilding). Ассет не трогаем (правило 1).
    // Меняется только визуал/звук/анимации; статы — MainBuildingStats; габариты коллайдера — отдельно (шаг 4b).
    // Детерминированно на всех пирах (визуал нужен всем; событие приходит и хосту, и клиенту — MatchManager.TechUpgrade.cs).
    public partial class MatchManager
    {
        // Уровень, чей облик уже применён к замку команды (для дельт). База = стартовый уровень ГЗ.
        readonly int[] appliedShapeLevel = new int[2];

        // Подписка на смену уровня ГЗ. Вызываются из Awake/OnDestroy MatchManager (как MainBuildingStatsWire).
        void MainBuildingShapeWire()
        {
            appliedShapeLevel[0] = appliedShapeLevel[1] = Mathf.Max(1, startMainBuildingLevel);
            OnMainBuildingLevelChanged += ApplyMainBuildingShape;
        }

        void MainBuildingShapeUnwire()
        {
            OnMainBuildingLevelChanged -= ApplyMainBuildingShape;
        }

        // Сменить облик замка команды на префаб достигнутого уровня. Стартовый облик = базовый префаб замка
        // (событие на старте намеренно не дёргается — как у статов). Идемпотентно: тот же уровень — no-op.
        void ApplyMainBuildingShape(int team)
        {
            if (team < 0 || team > 1) return;
            TeamWaveConfig cfg = Team(team);
            if (cfg == null || cfg.mainBuilding == null) return;
            if (cfg.mainBuildingShapesByLevel == null || cfg.mainBuildingShapesByLevel.Length == 0) return;

            int newLevel = MainBuildingLevel(team);
            if (newLevel == appliedShapeLevel[team]) return;

            Unit shape = ShapeForLevel(cfg, newLevel);
            appliedShapeLevel[team] = newLevel;      // фиксируем уровень (даже если для него нет облика)
            if (shape == null) return;               // уровень без облика — оставляем текущий визуал

            cfg.mainBuilding.ReplaceRenderers(shape, true);   // постоянная смена облика (тот же объект/NetID)
            FitColliderToShape(cfg.mainBuilding, shape);       // §10.6: подгон габаритов коллайдера/препятствия под облик
            Debug.Log($"[MatchManager] Облик ГЗ команды {team}: уровень {newLevel} → префаб '{shape.name}'.");
        }

        // §10.6: подгоняет габариты замка под префаб облика. ReplaceRenderers меняет визуал (+unitHeight), но НЕ
        // unitRadius/коллайдер/NavMeshObstacle. Копируем размеры из компонентов префаба облика (Capsule/Box) + пересобираем
        // NavMeshObstacle (carve). ВНИМАНИЕ (правило 19): не проверено в Unity — тип коллайдера здания и carve-пересборку сверить.
        void FitColliderToShape(Unit building, Unit shape)
        {
            if (building == null || shape == null) return;

            building.unitRadius = shape.unitRadius; // unitHeight ReplaceRenderers копирует сам

            CapsuleCollider srcCap = shape.GetComponent<CapsuleCollider>();
            CapsuleCollider dstCap = building.GetComponent<CapsuleCollider>();
            if (srcCap != null && dstCap != null)
            {
                dstCap.radius = srcCap.radius;
                dstCap.height = srcCap.height;
                dstCap.center = srcCap.center;
            }

            BoxCollider srcBox = shape.GetComponent<BoxCollider>();
            BoxCollider dstBox = building.GetComponent<BoxCollider>();
            if (srcBox != null && dstBox != null)
            {
                dstBox.size = srcBox.size;
                dstBox.center = srcBox.center;
            }

            // NavMeshObstacle (обход юнитами нового силуэта): пересобрать под актуальный коллайдер.
            NavMeshObstacle obs = building.GetComponent<NavMeshObstacle>();
            if (obs != null)
            {
                if (dstBox != null) { obs.shape = NavMeshObstacleShape.Box; obs.size = dstBox.size; obs.center = dstBox.center; }
                else if (dstCap != null) { obs.shape = NavMeshObstacleShape.Capsule; obs.radius = dstCap.radius; obs.height = dstCap.height; obs.center = dstCap.center; }
            }
        }

        // Префаб облика для уровня ГЗ (1-based). Клампится в границы массива.
        Unit ShapeForLevel(TeamWaveConfig cfg, int level)
        {
            int idx = Mathf.Clamp(level - 1, 0, cfg.mainBuildingShapesByLevel.Length - 1);
            return cfg.mainBuildingShapesByLevel[idx];
        }
    }
}
