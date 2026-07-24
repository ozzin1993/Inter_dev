using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Партиал MatchManager: апгрейды контента (техи/уровень ГЗ). Эффективные «доступные юниты» волны и
    // видимые центральные способности — ПРОИЗВОДНОЕ от FactionConfig.contentUnlockRules + штатного TechTree
    // + нашего mainBuildingLevel. Идемпотентный полный пересчёт (правило 8). Детерминированно на всех пирах →
    // клиент считает для UI без доп. RPC. Реальные эффекты (свопы префабов, отстройка) — сервер, шаги 3/4.
    // См. План_Апгрейды_контента_фракций.md.
    public partial class MatchManager : MonoBehaviour
    {
        /// <summary>Контент команды (доступные юниты/видимые способности ГЗ) изменился — для перерисовки UI.</summary>
        public event System.Action<int> OnTeamContentChanged;

        // Производное эффективное состояние (пересчитывается из источника, не накапливается).
        readonly List<Unit>[] effectiveAvailableUnits = new List<Unit>[2] { new List<Unit>(), new List<Unit>() };
        readonly List<Ability>[] visibleCentral = new List<Ability>[2] { new List<Ability>(), new List<Ability>() };
        readonly Dictionary<Unit, Unit>[] effectiveUnitSwaps = new Dictionary<Unit, Unit>[2]
            { new Dictionary<Unit, Unit>(), new Dictionary<Unit, Unit>() };
        // Активные подмены префабов башен по ключу точки (pointKey → целевой префаб). Шаги 3б/4.
        readonly Dictionary<PointKey, Unit>[] effectiveTowerSwaps = new Dictionary<PointKey, Unit>[2]
            { new Dictionary<PointKey, Unit>(), new Dictionary<PointKey, Unit>() };

        // Выполнено ли условие разблокировки для команды (теха И/ИЛИ уровень ГЗ).
        bool IsRequirementMet(int team, UnlockRequirement req)
        {
            if (req == null) return true;
            TeamWaveConfig cfg = Team(team);
            if (cfg == null) return false;
            bool techOk = req.tech == null || TechUnlockedSafe(req.tech, cfg.ownerPlayer);
            bool gzOk   = mainBuildingLevel[team] >= req.mainBuildingLevel;
            return techOk && gzOk;
        }

        // Полный пересчёт эффективного контента команды из её FactionConfig (идемпотентно).
        // Правила применяются ПО ПОРЯДКУ массива (Q1: при конфликте add/remove побеждает более позднее правило).
        void RecomputeUnlockedContent(int team)
        {
            if (team != 0 && team != 1) return;
            FactionConfig faction = ResolveFaction(Team(team).ownerPlayer);

            List<Unit> units = effectiveAvailableUnits[team];
            List<Ability> central = visibleCentral[team];
            Dictionary<Unit, Unit> swaps = effectiveUnitSwaps[team];
            Dictionary<PointKey, Unit> tswaps = effectiveTowerSwaps[team];
            units.Clear();
            central.Clear();
            swaps.Clear();
            tswaps.Clear();

            if (faction == null) { OnTeamContentChanged?.Invoke(team); return; }

            // База: доступные-для-пометки юниты из единого списка (роль Available); техи добавляют поверх ниже.
            if (faction.waveUnits != null)
                foreach (WaveUnitEntry e in faction.waveUnits)
                    if (e != null && e.role == WaveUnitRole.Available && e.unit != null && !units.Contains(e.unit)) units.Add(e.unit);
            if (faction.centralAbilities != null)
                foreach (Ability a in faction.centralAbilities) if (a != null && !central.Contains(a)) central.Add(a);

            // Активные правила (последовательно)
            if (faction.contentUnlockRules != null)
                foreach (ContentUnlockRule rule in faction.contentUnlockRules)
                {
                    if (rule == null) continue;
                    bool active = IsRequirementMet(team, rule.requirement);
                    if (!active) continue;
                    if (rule.addAvailableUnits != null)
                        foreach (Unit u in rule.addAvailableUnits) if (u != null && !units.Contains(u)) units.Add(u);
                    if (rule.removeAvailableUnits != null)
                        foreach (Unit u in rule.removeAvailableUnits) units.Remove(u);
                    if (rule.unitSwaps != null)
                        foreach (UnitSwap s in rule.unitSwaps)
                            if (s != null && s.from != null && s.to != null) swaps[s.from] = s.to;
                    if (rule.towerSwaps != null)
                        foreach (TowerSwap s in rule.towerSwaps)
                            if (s != null && s.pointKey != PointKey.None && s.to != null)
                                tswaps[s.pointKey] = s.to;
                    if (rule.showCentralAbilities != null)
                        foreach (Ability a in rule.showCentralAbilities) if (a != null && !central.Contains(a)) central.Add(a);
                    if (rule.hideCentralAbilities != null)
                        foreach (Ability a in rule.hideCentralAbilities) central.Remove(a);
                }

            // Волна 2.0: чистка пометок (сервер): снять авто/разовые типов, ушедших из доступных (с возвратом резерва).
            if (!NetworkConnectionHandler.isClient) PruneMarks(team);

            // Ретро-замена живых башен (сервер): башни чьи префабы изменились от towerSwaps (шаг 4).
            if (!NetworkConnectionHandler.isClient) RetroReplaceTowers(team);

            OnTeamContentChanged?.Invoke(team);
        }

        /// <summary>Доступные для добавления в волну юниты команды (производное). Для UI и валидации (E).</summary>
        public IReadOnlyList<Unit> AvailableWaveUnits(int team) =>
            (team == 0 || team == 1) ? effectiveAvailableUnits[team] : (IReadOnlyList<Unit>)System.Array.Empty<Unit>();

        /// <summary>Видимые в таблице ГЗ центральные способности команды (производное, упакованно).</summary>
        public IReadOnlyList<Ability> VisibleCentralAbilities(int team) =>
            (team == 0 || team == 1) ? visibleCentral[team] : (IReadOnlyList<Ability>)System.Array.Empty<Ability>();

        // Доступен ли юнит для добавления в волну (валидация). Набор пуст (не настроен) → доступно всё (бэк-совместимость).
        public bool IsUnitAvailable(int team, Unit prefab)
        {
            if (team != 0 && team != 1 || prefab == null) return false;
            List<Unit> avail = effectiveAvailableUnits[team];
            return avail.Count == 0 || avail.Contains(prefab);
        }

        // Проекция базового префаба волны через активные unitSwaps (from→to, цепочки). Свопов нет → префаб как есть.
        public Unit ResolveSwap(int team, Unit baseUnit)
        {
            if (team != 0 && team != 1 || baseUnit == null) return baseUnit;
            Dictionary<Unit, Unit> map = effectiveUnitSwaps[team];
            Unit cur = baseUnit;
            int guard = 0;
            while (map.TryGetValue(cur, out Unit next) && next != null && next != cur && guard++ < 16)
                cur = next;
            return cur;
        }

        // Проекция расового базового префаба башни через активные towerSwaps команды (pointKey → to).
        // Своп не найден → возвращает baseRacialPrefab без изменений.
        Unit ResolveTowerSwap(int team, PointKey pointKey, Unit baseRacialPrefab)
        {
            if (team != 0 && team != 1 || pointKey == PointKey.None) return baseRacialPrefab;
            if (effectiveTowerSwaps[team].TryGetValue(pointKey, out Unit swapped) && swapped != null)
                return swapped;
            return baseRacialPrefab;
        }

        // Сервер: ретроактивно заменить живые башни команды, у которых целевой префаб изменился.
        // Идемпотентно: пропускает башни уже с нужным префабом (spawnedPrefabByConfig).
        void RetroReplaceTowers(int team)
        {
            if (rebuildablePoints == null) return;
            int[] pTeam = SlotManager.instance?.playerTeam;
            if (pTeam == null) return;

            foreach (PointTowerConfig cfg in rebuildablePoints)
            {
                if (cfg == null || cfg.pointKey == PointKey.None) continue;
                if (cfg.point == null || !cfg.point.TowerAlive) continue;

                // Найти живую башню точки (обратный поиск в hookedTowers).
                Unit liveT = null;
                foreach (var kv in hookedTowers)
                    if (kv.Value == cfg) { liveT = kv.Key; break; }
                if (liveT == null) continue;

                // Принадлежит ли башня нужной команде?
                if (liveT.owner < 0 || liveT.owner >= pTeam.Length) continue;
                if (pTeam[liveT.owner] != team) continue;

                // Целевой префаб (расовый + своп).
                Unit racialBase = ResolveRacialTowerPrefab(liveT.owner, cfg);
                if (racialBase == null) continue;
                Unit targetPrefab = ResolveTowerSwap(team, cfg.pointKey, racialBase);

                // Идемпотентность: уже нужный префаб → пропустить.
                if (spawnedPrefabByConfig.TryGetValue(cfg, out Unit spawnedPrefab) && spawnedPrefab == targetPrefab)
                    continue;

                // Ретро-замена: отписать → снять с POI → убить (без смены владения) → заспавнить новую.
                Debug.Log($"[MatchManager] Ретро-замена башни {liveT.name} на точке {cfg.point.name} " +
                          $"(key={cfg.pointKey}, новый prefab={targetPrefab.name}).");
                liveT.OnDie -= HandleTowerDie;
                hookedTowers.Remove(liveT);
                spawnedPrefabByConfig.Remove(cfg);
                cfg.point.ClearTower();

                Vector3 pos  = liveT.transform.position;
                float   rotY = liveT.transform.eulerAngles.y;
                int     owner = liveT.owner;

                // Убиваем старую башню штатным Unit.Die без наград (rewards=false), серверно (подписка уже снята → HandleTowerDie не сработает).
                liveT.Die(owner, null, false, true, false);

                StartCoroutine(SpawnTowerForPoint(cfg, pos, rotY, owner, cfg.rebuildDelay)); // §4.4: префаб резолвится при спавне
            }
        }

        // Делегаты подписки на разблокировку/блок техов (на команду) — храним для отписки.
        readonly System.Action[] techRecomputeHandlers = new System.Action[2];
        bool contentTriggersWired;

        // Подписка на триггеры контента + начальный пересчёт. По OnGameStart (всё готово: TechnologyManager
        // инициализирован, ownerPlayer известен). На всех пирах — клиент пересчитывает для UI.
        void WireContentTriggers()
        {
            if (contentTriggersWired) return;
            contentTriggersWired = true;

            TechnologyManager tm = TechnologyManager.instance;
            for (int team = 0; team < 2; team++)
            {
                int t = team;
                techRecomputeHandlers[t] = () => RecomputeUnlockedContent(t);
                int player = Team(t) != null ? Team(t).ownerPlayer : -1;
                if (tm != null && tm.OnTechUnlock != null && player >= 0 && player < tm.OnTechUnlock.Length)
                {
                    tm.OnTechUnlock[player] += techRecomputeHandlers[t];
                    if (tm.OnTechLock != null && player < tm.OnTechLock.Length)
                        tm.OnTechLock[player] += techRecomputeHandlers[t];
                }
            }
            OnMainBuildingLevelChanged += RecomputeUnlockedContent;

            // Стартовый уровень ГЗ — ДО начального пересчёта, чтобы гейт техов и контент увидели стартовое значение.
            InitStartingMainBuildingLevel();

            RecomputeUnlockedContent(0);
            RecomputeUnlockedContent(1);
            // Начальный спавн башен — после пересчёта, чтобы effectiveTowerSwaps уже был актуален.
            if (!NetworkConnectionHandler.isClient) SpawnInitialTowers();
        }

        // Отписка при уничтожении менеджера.
        void UnwireContentTriggers()
        {
            TechnologyManager tm = TechnologyManager.instance;
            for (int team = 0; team < 2; team++)
            {
                if (techRecomputeHandlers[team] == null) continue;
                int player = Team(team) != null ? Team(team).ownerPlayer : -1;
                if (tm != null && tm.OnTechUnlock != null && player >= 0 && player < tm.OnTechUnlock.Length)
                {
                    tm.OnTechUnlock[player] -= techRecomputeHandlers[team];
                    if (tm.OnTechLock != null && player < tm.OnTechLock.Length)
                        tm.OnTechLock[player] -= techRecomputeHandlers[team];
                }
                techRecomputeHandlers[team] = null;
            }
            OnMainBuildingLevelChanged -= RecomputeUnlockedContent;
            contentTriggersWired = false;
        }
    }
}
