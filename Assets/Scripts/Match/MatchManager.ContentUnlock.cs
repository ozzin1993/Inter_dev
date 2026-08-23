using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Партиал MatchManager: контент, открываемый УЗЛАМИ дерева технологий (Технологии 2.0).
    // Эффективные «доступные юниты» волны, умения ГЗ по ячейкам и свопы префабов — ПРОИЗВОДНОЕ от
    // купленных узлов (TechNode.unlock*/swaps) поверх стартового набора расы. Условие у эффекта одно:
    // узел куплен (штатный TechTree). Уровень ГЗ на состав контента больше НЕ влияет
    // (решение Artsiom 2026-07-24) — он остался только в статах/облике замка и генерации душ.
    // Идемпотентный полный пересчёт (правило 8). Детерминированно на всех пирах → клиент считает для UI
    // без доп. RPC. Реальные эффекты (свопы префабов, отстройка) — сервер.
    public partial class MatchManager : MonoBehaviour
    {
        /// <summary>Контент команды (доступные юниты/видимые способности ГЗ) изменился — для перерисовки UI.</summary>
        public event System.Action<int> OnTeamContentChanged;

        /// <summary>Сколько ячеек в панели умений главного здания (один ряд).</summary>
        public const int CentralAbilitySlotCount = 3;

        // Производное эффективное состояние (пересчитывается из источника, не накапливается).
        readonly List<Unit>[] effectiveAvailableUnits = new List<Unit>[2] { new List<Unit>(), new List<Unit>() };
        readonly List<Ability>[] visibleCentral = new List<Ability>[2] { new List<Ability>(), new List<Ability>() };
        // Умения ГЗ по ЯЧЕЙКАМ панели (позиция закреплена: пусто → ячейка пустая, соседи не сдвигаются).
        readonly Ability[][] centralBySlot = new Ability[2][]
            { new Ability[CentralAbilitySlotCount], new Ability[CentralAbilitySlotCount] };
        readonly Dictionary<Unit, Unit>[] effectiveUnitSwaps = new Dictionary<Unit, Unit>[2]
            { new Dictionary<Unit, Unit>(), new Dictionary<Unit, Unit>() };
        // Активные подмены префабов башен по ключу точки (pointKey → целевой префаб). Шаги 3б/4.
        readonly Dictionary<PointKey, Unit>[] effectiveTowerSwaps = new Dictionary<PointKey, Unit>[2]
            { new Dictionary<PointKey, Unit>(), new Dictionary<PointKey, Unit>() };

        // Полный пересчёт эффективного контента команды (идемпотентно): стартовый набор расы + эффекты
        // КУПЛЕННЫХ узлов дерева технологий. Источник — runtime-копия TeamWaveConfig (заполняется ApplyFaction).
        void RecomputeUnlockedContent(int team)
        {
            if (team != 0 && team != 1) return;
            TeamWaveConfig cfg = Team(team);

            List<Unit> units = effectiveAvailableUnits[team];
            List<Ability> central = visibleCentral[team];
            Ability[] slots = centralBySlot[team];
            Dictionary<Unit, Unit> swaps = effectiveUnitSwaps[team];
            Dictionary<PointKey, Unit> tswaps = effectiveTowerSwaps[team];
            units.Clear();
            central.Clear();
            swaps.Clear();
            tswaps.Clear();
            for (int i = 0; i < slots.Length; i++) slots[i] = null;

            if (cfg == null) { OnTeamContentChanged?.Invoke(team); return; }

            // База: доступные-для-пометки юниты из единого списка (роль Available); узлы добавляют поверх ниже.
            if (cfg.waveUnits != null)
                foreach (WaveUnitEntry e in cfg.waveUnits)
                    if (e != null && e.role == WaveUnitRole.Available && e.unit != null && !units.Contains(e.unit)) units.Add(e.unit);

            // База: СТАРТОВЫЕ умения ГЗ — порядок в списке = номер ячейки панели (0,1,2).
            if (cfg.centralAbilities != null)
                for (int i = 0; i < cfg.centralAbilities.Count && i < CentralAbilitySlotCount; i++)
                    PlaceCentralAbility(team, cfg.centralAbilities[i], i);

            // Эффекты купленных узлов дерева технологий (по тирам, ступени: уровень → вариант → специализация).
            if (cfg.techTiers != null)
                foreach (TechTier tier in cfg.techTiers)
                {
                    if (tier == null) continue;
                    ApplyNodeContent(team, tier.levelUpgrade);
                    ApplyOptionContent(team, tier.optionA);
                    ApplyOptionContent(team, tier.optionB);
                }

            // Волна 2.0: чистка пометок (сервер): снять авто/разовые типов, ушедших из доступных (с возвратом резерва).
            if (!NetworkConnectionHandler.isClient) PruneMarks(team);

            // Ретро-замена живых башен (сервер): башни чьи префабы изменились от towerSwaps (шаг 4).
            if (!NetworkConnectionHandler.isClient) RetroReplaceTowers(team);

            OnTeamContentChanged?.Invoke(team);
        }

        // Эффекты варианта тира: сам узел выбора + обе специализации (применится то, что куплено).
        void ApplyOptionContent(int team, TechBigOption option)
        {
            if (option == null) return;
            ApplyNodeContent(team, option.node);
            ApplyNodeContent(team, option.specializationA);
            ApplyNodeContent(team, option.specializationB);
        }

        // Эффекты ОДНОГО узла — только если он куплен (штатный TechTree, см. NodeUnlocked в MatchManager.TechTiers).
        void ApplyNodeContent(int team, TechNode node)
        {
            TeamWaveConfig cfg = Team(team);
            if (node == null || cfg == null) return;
            if (!NodeUnlocked(cfg.ownerPlayer, node)) return;

            if (node.unlockUnits != null)
                foreach (Unit u in node.unlockUnits)
                    if (u != null && !effectiveAvailableUnits[team].Contains(u)) effectiveAvailableUnits[team].Add(u);

            if (node.unlockAbilities != null)
                foreach (AbilityUnlock e in node.unlockAbilities)
                    if (e != null) PlaceCentralAbility(team, e.ability, e.slot);

            if (node.unitSwaps != null)
                foreach (UnitSwap sw in node.unitSwaps)
                    if (sw != null && sw.from != null && sw.to != null) effectiveUnitSwaps[team][sw.from] = sw.to;

            if (node.towerSwaps != null)
                foreach (TowerSwap sw in node.towerSwaps)
                    if (sw != null && sw.pointKey != PointKey.None && sw.to != null) effectiveTowerSwaps[team][sw.pointKey] = sw.to;
        }

        // Кладёт умение в ЯЧЕЙКУ панели ГЗ. Ячейка занята другим умением — пропуск с варнингом (первый занявший
        // остаётся; коллизию ловит валидатор Interflow Editor до матча). Слот вне диапазона — пропуск с варнингом.
        void PlaceCentralAbility(int team, Ability ability, int slot)
        {
            if (ability == null) return;
            Ability[] slots = centralBySlot[team];
            if (slot < 0 || slot >= slots.Length)
            {
                Debug.LogWarning($"[MatchManager] Умение ГЗ «{ability.name}» (команда {team}): ячейка {slot} вне диапазона " +
                                 $"0..{slots.Length - 1} — умение не показано. Поправь настройку узла/списка стартовых умений.");
                return;
            }
            if (slots[slot] != null)
            {
                if (slots[slot] != ability)
                    Debug.LogWarning($"[MatchManager] Умение ГЗ «{ability.name}» (команда {team}): ячейка {slot} уже занята " +
                                     $"умением «{slots[slot].name}» — показано первое. Разведи их по разным ячейкам.");
                return;
            }
            slots[slot] = ability;
            if (!visibleCentral[team].Contains(ability)) visibleCentral[team].Add(ability);
        }

        /// <summary>
        /// ПОЛНЫЙ набор умений ГЗ расы: стартовые + все, что открываются узлами дерева (без учёта покупки, без
        /// дублей). Нужен для кастера — он должен знать умение заранее (каст по Ability.id ищет его в abilities[]
        /// кастера). Видимость в панели решает пересчёт (RecomputeUnlockedContent), а не этот список.
        /// </summary>
        public static List<Ability> CollectAllCentralAbilities(TeamWaveConfig cfg)
        {
            List<Ability> res = new List<Ability>();
            if (cfg == null) return res;

            if (cfg.centralAbilities != null)
                foreach (Ability a in cfg.centralAbilities)
                    if (a != null && !res.Contains(a)) res.Add(a);

            if (cfg.techTiers != null)
                foreach (TechTier tier in cfg.techTiers)
                {
                    if (tier == null) continue;
                    CollectNodeAbilities(tier.levelUpgrade, res);
                    CollectOptionAbilities(tier.optionA, res);
                    CollectOptionAbilities(tier.optionB, res);
                }
            return res;
        }

        static void CollectOptionAbilities(TechBigOption option, List<Ability> res)
        {
            if (option == null) return;
            CollectNodeAbilities(option.node, res);
            CollectNodeAbilities(option.specializationA, res);
            CollectNodeAbilities(option.specializationB, res);
        }

        static void CollectNodeAbilities(TechNode node, List<Ability> res)
        {
            if (node == null || node.unlockAbilities == null) return;
            foreach (AbilityUnlock e in node.unlockAbilities)
                if (e != null && e.ability != null && !res.Contains(e.ability)) res.Add(e.ability);
        }

        /// <summary>Умение ГЗ в ЯЧЕЙКЕ панели (0..2) или null, если ячейка пуста. Для позиционной раскладки UI.</summary>
        public Ability CentralAbilityAtSlot(int team, int slot)
        {
            if (team != 0 && team != 1) return null;
            Ability[] slots = centralBySlot[team];
            return (slot >= 0 && slot < slots.Length) ? slots[slot] : null;
        }

        /// <summary>Доступные для добавления в волну юниты команды (производное). Для UI и валидации (E).</summary>
        public IReadOnlyList<Unit> AvailableWaveUnits(int team) =>
            (team == 0 || team == 1) ? effectiveAvailableUnits[team] : (IReadOnlyList<Unit>)System.Array.Empty<Unit>();

        /// <summary>Открытые умения ГЗ команды сплошным списком, без пустых ячеек (производное).
        /// Для позиционной раскладки панели бери CentralAbilityAtSlot.</summary>
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
            // На уровень ГЗ контент больше не подписан: состав открытого контента задают только узлы дерева
            // (решение Artsiom 2026-07-24). Уровень ГЗ по-прежнему растёт от покупки узла уровня — но влияет
            // лишь на статы/облик замка и генерацию душ, каждая из этих систем подписана на него сама.

            // Стартовый уровень ГЗ — ДО начального пересчёта, чтобы гейт техов увидел стартовое значение.
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
            contentTriggersWired = false;
        }
    }
}
