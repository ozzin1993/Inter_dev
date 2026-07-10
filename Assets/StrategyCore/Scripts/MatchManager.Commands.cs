using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ===== КОМАНДЫ ЮНИТАМ + LANE-ТАРГЕТИНГ (партиал MatchManager) =====
    // Команды Атака/Защита по рядам, хранимые цели/режим, единый Lane-таргетинг, имена точек. Серверо-авторитетно (правило 6).
    public partial class MatchManager
    {
        // ======================== ХРАНИМЫЕ ЦЕЛИ И РЕЖИМ КОМАНДЫ ========================

        // Текущие точки атаки и защиты для каждой команды (0=A, 1=B). Обновляются через RefreshTargetPoints.
        Vector2[] currentAttackPoint  = new Vector2[2];
        Vector2[] currentDefencePoint = new Vector2[2];

        // Текущий режим команды для каждой команды И РЯДА: currentCommand[team][group]. None — режим не выбран.
        // Размерность рядов = CommandGroupCount (см. MatchManager.CommandGroups.cs). Инициализация — в Awake.
        BottomTableAction[][] currentCommand;

        // ======================== LANE-ТАРГЕТИНГ (единый источник) ========================

        /// <summary>Цель атаки для игрока: следующая вражеская точка линии. Vector2.zero — если линии/цели нет.</summary>
        public Vector2 AttackTarget(int player)
        {
            if (lane == null) return Vector2.zero;
            return lane.NextAttackTarget(TeamIndexOfOwner(player));
        }

        /// <summary>Цель обороны для игрока: самая передовая своя точка линии. Vector2.zero — если своих точек нет.</summary>
        public Vector2 DefenceTarget(int player)
        {
            if (lane == null) return Vector2.zero;
            return lane.FrontmostOwnedPoint(TeamIndexOfOwner(player));
        }

        // ======================== ВСПОМОГАТЕЛЬНЫЕ: ИМЕНА ТОЧЕК ДЛЯ ЛОГОВ ========================

        // PointOfInterest текущей цели атаки для игрока. null — если Lane не задана или нет цели.
        PointOfInterest AttackTargetPOI(int player)
        {
            if (lane == null) return null;
            return lane.NextAttackTargetPoint(TeamIndexOfOwner(player));
        }

        // PointOfInterest текущей цели защиты для игрока. null — если Lane не задана или нет цели.
        PointOfInterest DefenceTargetPOI(int player)
        {
            if (lane == null) return null;
            return lane.FrontmostOwnedPointPoint(TeamIndexOfOwner(player));
        }

        // Читаемое имя точки для лога. null → "нет".
        static string POIName(PointOfInterest poi) => poi != null ? poi.name : "нет";

        // ======================== КОМАНДЫ ЮНИТАМ ========================

        /// <summary>
        /// Отправить в атаку живых боевых юнитов команды из указанного РЯДА (groupIndex) — тех, чей класс
        /// (Unit.unitCategory) входит в набор ряда (commandGroups). Цель — следующая вражеская точка линии.
        /// Запоминает режим ряда: при следующем спавне волны или гибели башни команда ряда переотдаётся автоматически.
        /// </summary>
        public void SendAttackCommand(int teamIndex, int groupIndex)
        {
            TeamWaveConfig cfg = Team(teamIndex);
            if (cfg == null) return;
            if (!IsValidCommandGroup(groupIndex)) { Debug.LogWarning($"[MatchManager] АТАКА: неверный ряд {groupIndex} (всего рядов {CommandGroupCount})."); return; }
            int player = cfg.ownerPlayer;
            if (NetworkConnectionHandler.isClient) return; // команда серверо-авторитетна; клиент шлёт TeamCommandServerRpc

            PointOfInterest attackPOI  = AttackTargetPOI(player);
            Vector2 newAttackPoint = attackPOI != null ? attackPOI.Position2D : Vector2.zero;
            if (newAttackPoint != currentAttackPoint[teamIndex])
                Debug.Log($"[MatchManager] Точка атаки команды {teamIndex} изменилась → {POIName(attackPOI)}.");
            currentAttackPoint[teamIndex] = newAttackPoint;
            currentCommand[teamIndex][groupIndex] = BottomTableAction.Attack;

            // Фильтр: только юниты классов этого ряда (commandGroups), затем — реагирующие на приказ АТАКА
            // (AutoAbilityUser.RespondsToAttackCommand).
            List<Unit> units = FilterByCommandGroup(GetGroupUnits(teamIndex), groupIndex);
            List<Unit> attackUnits = FilterByCommand(units, isAttack: true);
            Debug.Log($"[MatchManager] АТАКА: команда {teamIndex} ряд {groupIndex} (player={player}), " +
                      $"юнитов={attackUnits.Count}/{units.Count}, цель={POIName(attackPOI)}, " +
                      $"режим={( NetworkConnectionHandler.isClient ? "клиент(AttackMove)" : "сервер(FormationMarch)" )}.");
            if (attackUnits.Count == 0) return;

            Vector2 target = currentAttackPoint[teamIndex];
            if (target == Vector2.zero) return;

            Func<Vector2> targetDelegate = lane != null ? () => AttackTarget(player) : (Func<Vector2>)null;
            StopFormationsForOwnerGroup(player, groupIndex);
            FormationMarch.Begin(
                attackUnits, target, player,
                detectionRadius, regroupDebounce, formationPollInterval, arrivalDistance,
                slotSpacing, targetDelegate, groupIndex);
        }

        /// <summary>
        /// Отправить в режим Hold (стоят в своих слотах, атакуют в радиусе) живых боевых юнитов команды из
        /// указанного РЯДА (groupIndex) — тех, чей класс входит в набор ряда (commandGroups).
        /// Запоминает режим ряда: при следующем спавне волны или гибели башни команда ряда переотдаётся автоматически.
        /// </summary>
        public void SendDefenceCommand(int teamIndex, int groupIndex)
        {
            TeamWaveConfig cfg = Team(teamIndex);
            if (cfg == null) return;
            if (!IsValidCommandGroup(groupIndex)) { Debug.LogWarning($"[MatchManager] ЗАЩИТА: неверный ряд {groupIndex} (всего рядов {CommandGroupCount})."); return; }
            int player = cfg.ownerPlayer;
            if (NetworkConnectionHandler.isClient) return; // команда серверо-авторитетна; клиент шлёт TeamCommandServerRpc

            PointOfInterest defencePOI  = DefenceTargetPOI(player);
            Vector2 newDefencePoint = defencePOI != null ? defencePOI.Position2D : Vector2.zero;
            if (newDefencePoint != currentDefencePoint[teamIndex])
                Debug.Log($"[MatchManager] Точка защиты команды {teamIndex} изменилась → {POIName(defencePOI)}.");
            currentDefencePoint[teamIndex] = newDefencePoint;
            currentCommand[teamIndex][groupIndex] = BottomTableAction.Defence;

            // Фильтр: только юниты классов этого ряда (commandGroups).
            List<Unit> units = FilterByCommandGroup(GetGroupUnits(teamIndex), groupIndex);
            Debug.Log($"[MatchManager] ЗАЩИТА: команда {teamIndex} ряд {groupIndex} (player={player}), " +
                      $"юнитов={units.Count}, точка={POIName(defencePOI)}.");
            if (units.Count == 0) return;

            Vector2 target = currentDefencePoint[teamIndex];
            if (target == Vector2.zero) return;

            StopFormationsForOwnerGroup(player, groupIndex);
            // Защита через сетки слотов: каждый живой юнит ряда идёт в СВОЙ слот активной сетки.
            IssueDefenceAll(teamIndex, groupIndex);
        }

        /// <summary>
        /// Переотдать одному юниту текущую команду его команды (Атака/Защита).
        /// Нужна для возврата юнита к маршу/обороне после авто-каста (см. AutoAbilityUser).
        /// Серверо-авторитетно. Цель берётся «вживую» из Lane (единый источник истины),
        /// поэтому актуальна даже после смены владельца точек. Если у команды нет активного
        /// режима (None) или цели нет — ничего не делает.
        /// </summary>
        public void ReissueCurrentCommand(Unit unit)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (unit == null || unit.dead) return;

            int teamIndex = TeamIndexOfOwner(unit.owner);
            if (teamIndex < 0) return;

            int group = CommandGroupOfUnit(unit);
            if (group < 0 || !IsValidCommandGroup(group)) return; // класс юнита не покрыт ни одним рядом — переотдавать нечего

            int player = Team(teamIndex).ownerPlayer;
            switch (currentCommand[teamIndex][group])
            {
                case BottomTableAction.Attack:
                    if (!RespondsToCommand(unit, isAttack: true)) break; // не реагирует на атаку
                    Vector2 atk = AttackTarget(player);
                    if (atk != Vector2.zero) unit.AttackMove(atk);
                    break;
                case BottomTableAction.Defence:
                    if (!RespondsToCommand(unit, isAttack: false)) break; // не реагирует на защиту
                    // В СВОЙ слот сетки (как IssueDefenceAll), учитывая Hold/Idle, а НЕ в саму точку —
                    // иначе после авто-каста (AutoAbilityUser) юнит уходит на точку и строй кучкуется.
                    if (!MoveToDefenceSlot(teamIndex, unit))
                    {
                        Vector2 def = DefenceTarget(player); // фоллбэк: нет сетки у точки — к точке, как раньше
                        if (def != Vector2.zero) unit.Move(def);
                    }
                    break;
                // None — переотдавать нечего.
            }
        }

        // Индекс команды (0=A, 1=B) по игроку-владельцу. -1, если ни одна команда не владеет этим игроком.
        int TeamIndexOfOwner(int player)
        {
            if (teamA != null && teamA.ownerPlayer == player) return 0;
            if (teamB != null && teamB.ownerPlayer == player) return 1;
            return -1;
        }

        /// <summary>
        /// Боевые юниты команды игрока — ТОТ ЖЕ список, что используется для команд Атака/Защита
        /// (GetGroupUnits). Пусто, если игрок не владеет ни одной командой.
        /// </summary>
        public List<Unit> GetCommandUnitsForPlayer(int player)
        {
            int teamIndex = TeamIndexOfOwner(player);
            if (teamIndex < 0) return new List<Unit>();
            return GetGroupUnits(teamIndex);
        }

        // Динамический порог прибытия группы: запас из конфига (arrivalDistance) + полуразмер
        // строя ComputeFormation (√N × макс. unitRadius). Считается один раз при выдаче команды.
        static float GroupArrivalThreshold(List<Unit> units, float margin)
        {
            int n = 0;
            float maxRadius = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u == null || u.dead) continue;
                n++;
                if (u.unitRadius > maxRadius) maxRadius = u.unitRadius;
            }
            if (n == 0) return margin;
            return margin + Mathf.Sqrt(n) * maxRadius;
        }

        // Корутина: ждёт, пока все живые юниты команды не окажутся рядом с точкой,
        // затем выдаёт им Hold. Самоотключается, если все юниты погибли до прибытия.
        IEnumerator HoldOnArrival(int teamIndex, List<Unit> units, Vector2 target, float arrivalThreshold)
        {
            while (true)
            {
                yield return new WaitForSeconds(0.2f);

                bool anyAlive = false;
                bool anyFar   = false;
                for (int i = 0; i < units.Count; i++)
                {
                    Unit u = units[i];
                    if (u == null || u.dead) continue;
                    anyAlive = true;
                    Vector3 pos = u.transform.position;
                    if (Vector2.Distance(new Vector2(pos.x, pos.z), target) > arrivalThreshold)
                        anyFar = true;
                }

                if (!anyAlive)   // все погибли — холд не нужен
                {
                    defenceHoldCoroutines[teamIndex] = null;
                    yield break;
                }
                if (!anyFar) break; // все добрались до точки
            }

            defenceHoldCoroutines[teamIndex] = null;
            Debug.Log($"[MatchManager] ХОЛД: юниты команды {teamIndex} прибыли в точку защиты, занимаем холд.");
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u != null && !u.dead) u.Hold();
            }
        }

        // Живые боевые юниты команды. Сервер — из teamUnits; клиент — FindObjectsByType.
        List<Unit> GetGroupUnits(int teamIndex)
        {
            if (!NetworkConnectionHandler.isClient)
                return new List<Unit>(teamUnits[teamIndex]);

            int player = Team(teamIndex).ownerPlayer;
            List<Unit> result = new List<Unit>();
            Unit[] all = UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Unit u = all[i];
                if (u == null || u.dead || u.owner != player || u.unitType != UnitType.Unit) continue;
                // Призванных, НЕ слушающих общих приказов, исключаем. Слушающие (obeyCommands) — как обычные юниты.
                SummonedUnit s = u.GetComponent<SummonedUnit>();
                if (s != null && !s.obeyCommands) continue;
                result.Add(u);
            }
            return result;
        }

        // Обновить хранимые точки атаки и защиты для обеих команд.
        void RefreshTargetPoints()
        {
            for (int i = 0; i < 2; i++)
            {
                TeamWaveConfig cfg = Team(i);
                if (cfg == null) continue;
                int player = cfg.ownerPlayer;

                PointOfInterest newAttackPOI  = AttackTargetPOI(player);
                PointOfInterest newDefencePOI = DefenceTargetPOI(player);
                Vector2 newAttack  = newAttackPOI  != null ? newAttackPOI.Position2D  : Vector2.zero;
                Vector2 newDefence = newDefencePOI != null ? newDefencePOI.Position2D : Vector2.zero;

                if (newAttack  != currentAttackPoint[i])
                    Debug.Log($"[MatchManager] RefreshTargetPoints: точка атаки команды {i} " +
                              $"→ {POIName(newAttackPOI)}.");
                if (newDefence != currentDefencePoint[i])
                    Debug.Log($"[MatchManager] RefreshTargetPoints: точка защиты команды {i} " +
                              $"→ {POIName(newDefencePOI)}.");

                currentAttackPoint[i]  = newAttack;
                currentDefencePoint[i] = newDefence;
            }
        }

        // Остановить FormationMarch указанного владельца И РЯДА (commandGroup), чтобы не конфликтовали с новой
        // командой этого ряда. Строи ДРУГИХ рядов не трогаются — ряды независимы.
        void StopFormationsForOwnerGroup(int player, int group)
        {
            if (NetworkConnectionHandler.isClient) return;
            // Реестр активных формаций (FormationMarch.ActiveFormations) вместо FindObjectsByType — без скана сцены.
            // Stop()→Destroy отложен (OnDestroy правит реестр в конце кадра), поэтому идём с конца.
            IReadOnlyList<FormationMarch> all = FormationMarch.ActiveFormations;
            for (int i = all.Count - 1; i >= 0; i--)
                if (all[i] != null && all[i].OwnerPlayer == player && all[i].CommandGroup == group)
                    all[i].Stop();
        }

    }
}
