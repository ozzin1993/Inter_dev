using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ===== ВОЛНЫ 2.0 (партиал MatchManager) =====
    // Цикл с окнами t−warnSeconds (предупреждение о превышении лидерства) и t−lockSeconds (лок состава + вердикт
    // пропуска). Спавн волны: доход → списание автопризыва → состав из базовых + пометок (авто/разовые) ×count →
    // герой → снимок. Серверо-авторитетно (правило 6). Ядро ассета не трогаем (правило 1).
    public partial class MatchManager
    {
        // ======================== ЦИКЛ ВОЛН ========================

        IEnumerator WaveLoop()
        {
            yield return new WaitUntil(() => SlotManager.instance != null && SlotManager.instance.gameOn);
            // [Interflow fix 2026-08-01 limited-res-sync] Стартовый снимок лимитных ресурсов (лидерство) клиентам:
            // дальше идут дельты на каждое изменение, но исходное состояние нужно разослать один раз.
            if (NetworkDataSync.instance != null) NetworkDataSync.instance.LimitedResourceSyncAll();

            // [Interflow fix 2026-06-26] Диагностика: дамп связки player↔team на старте (рассинхрон teamIndex vs playerTeam).
            {
                var sm = SlotManager.instance;
                int o0 = teamA != null ? teamA.ownerPlayer : -1;
                int o1 = teamB != null ? teamB.ownerPlayer : -1;
                string pt = (sm != null && sm.playerTeam != null) ? string.Join(",", sm.playerTeam) : "null";
                int chk0 = (sm != null && sm.playerTeam != null && o0 >= 0 && o0 < sm.playerTeam.Length) ? sm.playerTeam[o0] : -1;
                int chk1 = (sm != null && sm.playerTeam != null && o1 >= 0 && o1 < sm.playerTeam.Length) ? sm.playerTeam[o1] : -1;
            }

            int waveNumber = 1;
            float totalWait = Mathf.Max(0f, firstWaveDelay);   // первая волна — через firstWaveDelay; далее — waveInterval
            ScheduleNextWave(totalWait);                       // зафиксировать момент первой волны для таймера (до запуска часов)
            StartWaveTimer();                                  // «часы» таймера до волны (сервер; раздача хост+клиент) — MatchManager.WaveTimer.cs
            while (true)
            {
                // Окна перед волной, устойчиво к любым значениям: моменты предупреждения/блокировки зажаты в [0, totalWait],
                // сумма трёх ожиданий = totalWait ВСЕГДА → волна ровно через интервал (совпадает с таймером до волны).
                // Если окно не помещается (напр. warnSeconds > интервала) — оно сжимается, но темп волны не сдвигается.
                float tWarn = Mathf.Clamp(totalWait - warnSeconds, 0f, totalWait);
                float tLock = Mathf.Clamp(totalWait - lockSeconds, 0f, totalWait);
                if (tLock < tWarn) tLock = tWarn;              // страховка порядка (warnSeconds < lockSeconds — валидатор предупредит)

                if (tWarn > 0f) yield return new WaitForSeconds(tWarn);

                // Момент предупреждения (за warnSeconds до волны): состав не влезает в лидерство (per team).
                WarnIfOverflow(0);
                WarnIfOverflow(1);

                float midWait = tLock - tWarn;
                if (midWait > 0f) yield return new WaitForSeconds(midWait);

                // Момент блокировки (за lockSeconds до волны): лок состава (пометки отклоняются) + вердикт.
                LockAndVerdict(0);
                LockAndVerdict(1);

                float postWait = totalWait - tLock;
                if (postWait > 0f) yield return new WaitForSeconds(postWait);

                // t0: спавн обеих команд.
                StartCoroutine(SpawnWave(teamA, 0));
                StartCoroutine(SpawnWave(teamB, 1));

                waveNumber++;
                totalWait = Mathf.Max(0f, waveInterval);
                ScheduleNextWave(totalWait);                   // зафиксировать момент следующей волны для таймера
            }
        }

        // t−warnSeconds: если вся волна (базовые + авто + разовые) не влезает в лидерство — предупредить игрока.
        void WarnIfOverflow(int team)
        {
            TeamWaveConfig cfg = Team(team);
            if (cfg == null || leadershipResource == null) return;
            int need = WaveLeadershipForecast(team);
            if (need <= 0) return;
            if (!GameResources.instance.CheckAmount(cfg.ownerPlayer, new ResourceWrapper(leadershipResource, need)))
            {
                OnWaveOverflowWarning?.Invoke(team);
                if (NetworkDataSync.instance != null) NetworkDataSync.instance.WaveOverflowWarn(team);
            }
        }

        // t−lockSeconds: заблокировать состав и вынести вердикт — влезает ли волна в лидерство (иначе пропуск целиком).
        void LockAndVerdict(int team)
        {
            TeamWaveConfig cfg = Team(team);
            if (cfg == null) return;
            cfg.compositionLocked = true;
            if (leadershipResource != null)
            {
                int need = WaveLeadershipForecast(team);
                cfg.waveSkipped = need > 0 &&
                    !GameResources.instance.CheckAmount(cfg.ownerPlayer, new ResourceWrapper(leadershipResource, need));
                if (cfg.waveSkipped)
                    Debug.LogWarning($"[MatchManager] Команда {team}: вердикт t−{lockSeconds:0} — волна не влезает ({need}), пропуск целиком.");
            }
        }

        // ======================== СПАВН ВОЛНЫ ========================

        IEnumerator SpawnWave(TeamWaveConfig cfg, int teamIndex)
        {
            if (cfg == null) yield break;

            // 1) Тик перерождения героя (тикает и на пропущенной волне — интервал есть волна).
            if (cfg.heroWavesToSkip > 0) cfg.heroWavesToSkip--;

            // 2) Вердикт пропуска (выставлен на t−lockSeconds): волна пропускается ЦЕЛИКОМ (базовые тоже), доход НЕ начисляется.
            if (cfg.waveSkipped)
            {
                if (overflowPolicy == WaveOverflowPolicy.CancelAndRefund) RefundOneShots(cfg);
                // CarryOneShots (дефолт): oneShot и резерв держатся на следующую волну.
                cfg.waveSkipped = false;
                cfg.compositionLocked = false;
                OnWaveMarksChanged?.Invoke(teamIndex);
                yield break;
            }

            if (cfg.spawnPoint == null || lane == null)
            {
                Debug.LogWarning($"[MatchManager] Команда {teamIndex}: нет spawnPoint/lane — волна пропущена.");
                cfg.compositionLocked = false;
                yield break;
            }

            // 3) Начислить базовый доход золота (в момент призыва волны).
            if (goldResource != null && cfg.baseIncome > 0)
                GameResources.instance.ChangeAmount(cfg.ownerPlayer, new ResourceWrapper(goldResource, cfg.baseIncome), 1, false, true);

            // 4) Списать автопризыв (Σ цена×count ≤ baseIncome — гарантировано пометками; списание ПОСЛЕ начисления → нехватка невозможна).
            int autoGold = AutoResourceSum(teamIndex, goldResource);
            if (goldResource != null && autoGold > 0)
                GameResources.instance.ChangeAmount(cfg.ownerPlayer, new ResourceWrapper(goldResource, autoGold), 1, true, true);

            // 5) Собрать состав: базовые ×count + авто ×count + разовые ×count (count — единый источник waveUnits).
            List<Unit> composition = BuildWaveComposition(cfg);

            Vector2 attackTarget = AttackTarget(cfg.ownerPlayer);
            List<Unit> waveUnits = new List<Unit>();
            Dictionary<Unit, int> spawnedCounts = new Dictionary<Unit, int>();

            foreach (Unit baseUnit in composition)
            {
                if (baseUnit == null) continue;
                Unit toSpawn = ResolveSwap(teamIndex, baseUnit); // проекция unitSwaps (from→to)
                Unit spawned = SpawnSlotPose(cfg, teamIndex, toSpawn.formationPriority, out Vector3 slotPos, out float slotYaw)
                    ? Unit.Spawn(toSpawn, slotPos, slotYaw, cfg.ownerPlayer, 0f)
                    : Unit.Spawn(toSpawn, cfg.spawnPoint.position, 0f, cfg.ownerPlayer, 0f);
                if (spawned != null)
                {
                    waveUnits.Add(spawned);
                    spawnedCounts.TryGetValue(toSpawn, out int prev);
                    spawnedCounts[toSpawn] = prev + 1;
                    teamUnits[teamIndex].Add(spawned);
                    AssignSlot(teamIndex, spawned);
                    int capturedIndex = teamIndex;
                    spawned.OnDie += (u, _, _, _) =>
                    {
                        teamUnits[capturedIndex].Remove(u);
                        OnDefenceUnitDied(capturedIndex, u); // компакция слота (крайний → в дыру)
                    };
                    // Разовый AttackMove новому юниту, если у его ряда нет активного режима (инвариант: без команды идёт к врагу).
                    if (CurrentCommandForUnit(teamIndex, spawned) == BottomTableAction.None)
                        spawned.AttackMove(attackTarget);
                    try { OnUnitSpawned?.Invoke(teamIndex, spawned); }
                    catch (Exception e) { Debug.LogError($"[MatchManager] OnUnitSpawned: {e.Message}"); }
                }
                else
                {
                    Debug.LogWarning($"[MatchManager] Не удалось заспавнить {toSpawn.name} для команды {teamIndex} (точка занята?).");
                }

                if (cfg.spawnDelay > 0f) yield return new WaitForSeconds(cfg.spawnDelay);
            }

            // 6) Герой: автоспавн с волной (открыт, не жив, не на паузе перерождения) — MatchManager.Hero.cs.
            TryAutoSpawnHero(teamIndex);

            // 7) Снимок ФАКТИЧЕСКОЙ волны для «Призыва к Оружию» + очистка «кармана» разовых (оплачено).
            if (spawnedCounts.Count > 0)
            {
                List<WaveEntry> snapshot = new List<WaveEntry>();
                foreach (KeyValuePair<Unit, int> kv in spawnedCounts)
                    snapshot.Add(new WaveEntry { unitToSpawn = kv.Key, count = kv.Value });
                lastWaveComposition[teamIndex] = snapshot;
            }
            cfg.oneShot.Clear();

            // Разлок состава (окно t−lock..t0 закрыто) + синк пометок клиентам.
            cfg.compositionLocked = false;
            NotifyMarksChanged(teamIndex);

            // Переотдача команды ПО РЯДАМ (как прежде): Атака — весь ряд; Защита — в слоты только новых.
            for (int g = 0; g < CommandGroupCount; g++)
            {
                if (currentCommand[teamIndex][g] == BottomTableAction.Attack)
                {
                    SendAttackCommand(teamIndex, g);
                }
                else if (currentCommand[teamIndex][g] == BottomTableAction.Defence)
                {
                    for (int i = 0; i < waveUnits.Count; i++)
                        if (CommandGroupMatches(g, waveUnits[i])) MoveToDefenceSlot(teamIndex, waveUnits[i]);
                }
            }
        }

        // Состав волны: базовые ×count + авто ×count + разовые ×count. Число копий — единый источник waveUnits (см. WaveCountOf).
        List<Unit> BuildWaveComposition(TeamWaveConfig cfg)
        {
            List<Unit> list = new List<Unit>();
            if (cfg == null) return list;

            if (cfg.waveUnits != null)
                foreach (WaveUnitEntry e in cfg.waveUnits)
                    if (e != null && e.role == WaveUnitRole.Basic) AddCopies(list, cfg, e.unit);

            if (cfg.autoSummon != null)
                foreach (int id in cfg.autoSummon)
                    AddCopies(list, cfg, ResolveUnitById(id));

            if (cfg.oneShot != null)
                foreach (int id in cfg.oneShot.Keys)
                    AddCopies(list, cfg, ResolveUnitById(id));

            return list;
        }

        void AddCopies(List<Unit> list, TeamWaveConfig cfg, Unit u)
        {
            if (u == null) return;
            int c = WaveCountOf(cfg, u);
            for (int i = 0; i < c; i++) list.Add(u);
        }

        // Вернуть зарезервированное золото по всем разовым и очистить карман (политика CancelAndRefund при пропуске).
        void RefundOneShots(TeamWaveConfig cfg)
        {
            if (cfg == null || cfg.oneShot == null) return;
            if (goldResource != null)
                foreach (KeyValuePair<int, int> kv in cfg.oneShot)
                    if (kv.Value > 0)
                        GameResources.instance.ChangeAmount(cfg.ownerPlayer, new ResourceWrapper(goldResource, kv.Value), 1, false, true);
            cfg.oneShot.Clear();
        }
    }
}
