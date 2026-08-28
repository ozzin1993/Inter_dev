using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ===== ОТСТРОЙКА БАШЕН + СЕТЕВАЯ СИНХРОНИЗАЦИЯ ВЛАДЕНИЯ (партиал MatchManager) =====
    // Старт/отстройка башен и флип владельца точки; серверо-авторитетно (правило 6). Синх владения — BroadcastPointTeam → ApplyPointTeam.
    public partial class MatchManager
    {
        // ======================== ОТСТРОЙКА БАШЕН ========================

        // Текущие подписки: башня → конфиг её точки. По событию OnDie находим точку.
        readonly Dictionary<Unit, PointTowerConfig> hookedTowers = new Dictionary<Unit, PointTowerConfig>();

        // Последний заспавненный префаб на конфиг точки — для идемпотентной ретро-замены (шаг 4).
        readonly Dictionary<PointTowerConfig, Unit> spawnedPrefabByConfig = new Dictionary<PointTowerConfig, Unit>();

        // Подписка на смерть башни конкретной точки.
        void HookTower(Unit tower, PointTowerConfig cfg)
        {
            if (tower == null || cfg == null) return;
            hookedTowers[tower] = cfg;
            tower.OnDie += HandleTowerDie;
        }

        // Башня погибла → точка переходит сопернику, тело снимается, через задержку отстраивается новая.
        void HandleTowerDie(Unit unit, int playerThatKills, Unit unitThatKills, bool rewards)
        {
            unit.OnDie -= HandleTowerDie;
            if (!hookedTowers.TryGetValue(unit, out PointTowerConfig cfg)) return;
            hookedTowers.Remove(unit);
            spawnedPrefabByConfig.Remove(cfg);

            PointOfInterest poi = cfg.point;
            if (poi == null) return;

            // Команда захвата = соперник текущего владельца. Для нейтральной точки (центр на старте)
            // соперника нет → штатная атрибуция убийцы (playerThatKills → unitThatKills.team).
            int newTeam = ResolveCaptureTeam(poi.CurrentTeam, playerThatKills, unitThatKills);

            // Сразу меняем владельца и снимаем башню (тело уничтожит штатный OnDie ассета).
            poi.SetTeam(newTeam);
            poi.ClearTower();

            // Серверо-авторитетно: разослать новое владение клиентам (только живой захват, без позднего входа).
            BroadcastPointTeam(poi, newTeam);

            // Опыт ГЗ за уничтоженную башню — команде захватчика (той же, что получила точку). Величина —
            // штатное поле xpReward самой башни (правило 1, как и за юнита): через хаб смертей башни не
            // проходят, поэтому начисляем здесь. Начисляем ДО раннего выхода по rebuildOnCapture ниже —
            // опыт дают все башни, не только центральная.
            // newTeam — команда в терминах SlotManager.playerTeam, а AddExperience ждёт индекс конфига
            // (0=A, 1=B), поэтому переводим через игрока-владельца. Нейтральный/неизвестный владелец даёт −1
            // и начисление пропускается.
            int captureTeamIndex = TeamIndexOfOwner(FindPlayerByTeam(newTeam));
            if (captureTeamIndex >= 0 && unit.xpReward > 0)
                AddExperience(captureTeamIndex, unit.xpReward, ExperienceSource.Tower);

            // Фронт сместился — обновить хранимые точки и переотдать текущие команды обеим командам.
            RefreshTargetPoints();

            for (int i = 0; i < 2; i++)
                for (int g = 0; g < CommandGroupCount; g++)
                {
                    if (currentCommand[i][g] == BottomTableAction.Attack)        SendAttackCommand(i, g);
                    else if (currentCommand[i][g] == BottomTableAction.Defence)  SendDefenceCommand(i, g);
                }

            // Ретаргет юнитов «без команды» (ряд в None или класс вне всех рядов) на новую вражескую точку.
            // Раньше их перенацеливал делегат цели FormationMarch (опрос); теперь — явно по событию захвата,
            // иначе дефолт-юниты застывают у взятой точки до следующей волны. Цели нет (Vector2.zero) → no-op.
            for (int i = 0; i < 2; i++)
            {
                TeamWaveConfig teamCfg = Team(i);
                if (teamCfg == null) continue;
                Vector2 noneTarget = AttackTarget(teamCfg.ownerPlayer);
                if (noneTarget == Vector2.zero) continue;
                List<Unit> units = teamUnits[i];
                for (int j = 0; j < units.Count; j++)
                {
                    Unit teamUnit = units[j];
                    if (teamUnit == null || teamUnit.dead) continue;
                    if (CurrentCommandForUnit(i, teamUnit) != BottomTableAction.None) continue;
                    teamUnit.AttackMove(noneTarget);
                }
            }

            // Призванные из отдельного списка (не слушающие общих приказов) переотдают свою команду
            // (Защита → новая передовая своя точка, Атака → новая вражеская). Слушающие общие — уже в teamUnits
            // и покрыты переотдачей команд выше.
            ReissueSummonedCommand();

            // Отстройка только для точек с rebuildOnCapture = true (центральная). Защитные — не отстраиваем.
            if (!cfg.rebuildOnCapture) return;

            // Резолвим префаб башни для захватившей команды. null → только захват точки без отстройки.
            int capturePlayer = FindPlayerByTeam(newTeam);
            if (capturePlayer < 0) capturePlayer = unit.owner;

            Unit racialPrefab = ResolveRacialTowerPrefab(capturePlayer, cfg);
            if (racialPrefab == null) return;

            // §4.4: своп здесь НЕ резолвим — целевой префаб определит SpawnTowerForPoint после задержки.
            StartCoroutine(SpawnTowerForPoint(cfg, unit.transform.position,
                                              unit.transform.eulerAngles.y, capturePlayer, cfg.rebuildDelay));
        }

        // Возвращает префаб башни расы владельца для данного типа точки (по pointKey).
        // Ищет именованное поле в FactionConfig: centreTower, defence1Tower, defence2Tower.
        // null → не строить (поле не заполнено или ключ неизвестен).
        Unit ResolveRacialTowerPrefab(int player, PointTowerConfig cfg)
        {
            if (cfg.pointKey == PointKey.None) return null;

            FactionConfig faction = ResolveFaction(player);
            if (faction == null)
            {
                Debug.LogWarning($"[MatchManager] ResolveRacialTowerPrefab: нет FactionConfig для player={player}. Отстройка отменена.");
                return null;
            }

            Unit prefab;
            switch (cfg.pointKey)
            {
                case PointKey.Centre:   prefab = faction.centreTower;   break;
                case PointKey.Defence1: prefab = faction.defence1Tower; break;
                case PointKey.Defence2: prefab = faction.defence2Tower; break;
                default:
                    Debug.LogWarning($"[MatchManager] Неизвестный pointKey='{cfg.pointKey}' для player={player}. Отстройка отменена.");
                    return null;
            }

            if (prefab == null)
                Debug.LogWarning($"[MatchManager] FactionConfig.{cfg.pointKey}Tower не задан для player={player}. Отстройка отменена.");
            return prefab;
        }

        // Общий путь спавна/переспавна башни точки (отстройка после захвата, ретро-замена, старт).
        // §4.4: целевой префаб резолвится ЗДЕСЬ, ПОСЛЕ задержки — актуальный на момент спавна
        // (техсвоп, купленный в окне отстройки, не теряется). delay — явный, не из cfg (для ретро можно переопределить).
        IEnumerator SpawnTowerForPoint(PointTowerConfig cfg, Vector3 position, float rotationY,
                                       int player, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);

            Unit prefab;
            if (player == (int)Players.NeutralActive || player == (int)Players.NeutralPassive)
            {
                // Нейтральный владелец: фиксированный префаб точки (neutralTower), расовый резолв не применим.
                prefab = cfg.neutralTower;
                if (prefab == null) yield break;
            }
            else
            {
                // Резолв на момент спавна: раса владельца + актуальный своп команды.
                // null → не строить (варнинги печатает сам ResolveRacialTowerPrefab).
                Unit racialPrefab = ResolveRacialTowerPrefab(player, cfg);
                if (racialPrefab == null) yield break;

                int[] pTeam = SlotManager.Instance != null ? SlotManager.Instance.playerTeam : null;
                int team = (pTeam != null && player >= 0 && player < pTeam.Length) ? pTeam[player] : -1;
                prefab = ResolveTowerSwap(team, cfg.pointKey, racialPrefab);
            }

            Unit tower = Unit.Spawn(prefab, position, rotationY, player);
            if (tower == null)
            {
                Debug.LogError($"[MatchManager] Не удалось заспавнить башню для точки {cfg.point?.name} (точка занята?).");
                yield break;
            }

            cfg.point.SetTower(tower);
            HookTower(tower, cfg);
            spawnedPrefabByConfig[cfg] = prefab; // запоминаем для идемпотентности ретро-замены
        }

        // Сервер: спавн начальных башен для всех точек в rebuildablePoints.
        // Вызывается из WireContentTriggers ПОСЛЕ RecomputeUnlockedContent — effectiveTowerSwaps уже актуален.
        void SpawnInitialTowers()
        {
            if (NetworkConnectionHandler.isClient) return;
            if (rebuildablePoints == null) return;

            foreach (PointTowerConfig cfg in rebuildablePoints)
            {
                if (cfg == null || cfg.point == null) continue;

                int initialTeam = cfg.point.CurrentTeam;
                int player = FindPlayerByTeam(initialTeam);
                if (player < 0)
                {
                    // Нейтральная точка (обычно центр): стартовая башня — cfg.neutralTower от штатного
                    // владельца Players.NeutralActive (враждебен всем → можно атаковать и захватить, правило 2).
                    if (cfg.neutralTower == null)
                    {
                        Debug.LogWarning($"[MatchManager] SpawnInitialTowers: точка {cfg.point.name} нейтральна " +
                                         $"(team={initialTeam}), а neutralTower не задан — стартует без башни.");
                        continue;
                    }
                    player = (int)Players.NeutralActive;
                }
                else if (ResolveRacialTowerPrefab(player, cfg) == null) continue; // нет префаба у расы → не строить (варнинг внутри)

                Vector3 pos  = cfg.point.transform.position;
                float   rotY = cfg.point.transform.eulerAngles.y;
                StartCoroutine(SpawnTowerForPoint(cfg, pos, rotY, player, 0f));
            }
        }

        // Соперник текущего владельца (модель 2 команд). Нейтральный/неизвестный владелец → атрибуция убийцы.
        int ResolveCaptureTeam(int currentTeam, int playerThatKills, Unit unitThatKills)
        {
            int opponent = OpponentTeam(currentTeam);
            if (opponent >= 0) return opponent;

            int[] teams = SlotManager.Instance.playerTeam;
            if (playerThatKills >= 0 && playerThatKills < teams.Length) return teams[playerThatKills];
            if (unitThatKills != null) return unitThatKills.team;
            return currentTeam;
        }

        // Команда-соперник среди двух участников матча (teamA/teamB). -1, если соперника нет
        // (владелец нейтрален или не входит в участников).
        int OpponentTeam(int team)
        {
            if (team < 0) return -1;
            int[] pt = SlotManager.Instance.playerTeam;
            int tA = (teamA != null && teamA.ownerPlayer >= 0 && teamA.ownerPlayer < pt.Length) ? pt[teamA.ownerPlayer] : -1;
            int tB = (teamB != null && teamB.ownerPlayer >= 0 && teamB.ownerPlayer < pt.Length) ? pt[teamB.ownerPlayer] : -1;
            if (team == tA && tB >= 0) return tB;
            if (team == tB && tA >= 0) return tA;
            return -1;
        }

        static int FindPlayerByTeam(int team)
        {
            int[] teams = SlotManager.Instance.playerTeam;
            for (int i = 0; i < teams.Length; i++)
                if (teams[i] == team) return i;
            return -1;
        }

        // ======================== СЕТЕВАЯ СИНХРОНИЗАЦИЯ ВЛАДЕНИЯ ========================

        // Сервер: разослать клиентам нового владельца точки (по индексу в Lane).
        void BroadcastPointTeam(PointOfInterest poi, int team)
        {
            if (lane == null || NetworkDataSync.Instance == null) return;
            int index = lane.IndexOf(poi);
            if (index < 0) return;
            NetworkDataSync.Instance.PointTeamSend(index, team);
        }

        /// <summary>Клиент: применить владельца точки по индексу (приходит сетевым RPC). Единый вход для Lane.</summary>
        public void ApplyPointTeam(int index, int team)
        {
            if (lane != null) lane.SetPointTeam(index, team);
        }

    }
}
