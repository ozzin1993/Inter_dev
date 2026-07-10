using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ===== ВОЛНЫ (партиал MatchManager) =====
    // Цикл и спавн волн (WaveLoop/SpawnWave): экономика-гейт, спавн в сетке замка, авто-команда. Серверо-авторитетно (правило 6).
    public partial class MatchManager
    {
        // ======================== ВОЛНЫ ========================

        IEnumerator WaveLoop()
        {
            yield return new WaitUntil(() => SlotManager.instance != null && SlotManager.instance.gameOn);
            Debug.Log("[MatchManager] Матч начался — запускаем волны.");

            // [Interflow fix 2026-06-26] Диагностика: дамп связки player↔team на старте (рассинхрон teamIndex vs playerTeam).
            {
                var sm = SlotManager.instance;
                int o0 = teamA != null ? teamA.ownerPlayer : -1;
                int o1 = teamB != null ? teamB.ownerPlayer : -1;
                string pt = (sm != null && sm.playerTeam != null) ? string.Join(",", sm.playerTeam) : "null";
                int chk0 = (sm != null && sm.playerTeam != null && o0 >= 0 && o0 < sm.playerTeam.Length) ? sm.playerTeam[o0] : -1;
                int chk1 = (sm != null && sm.playerTeam != null && o1 >= 0 && o1 < sm.playerTeam.Length) ? sm.playerTeam[o1] : -1;
                Debug.Log($"[MatchManager] КАРТА КОМАНД: team0(teamA).ownerPlayer={o0}, team1(teamB).ownerPlayer={o1}; " +
                          $"playerTeam[slot]=[{pt}]; СВЕРКА: playerTeam[team0.owner]={chk0} (ожид 0), playerTeam[team1.owner]={chk1} (ожид 1)" +
                          $"{((chk0 != 0 || chk1 != 1) ? "  >>> РАССИНХРОН team vs playerTeam <<<" : "")}.");
            }

            if (firstWaveDelay > 0f)
            {
                Debug.Log($"[MatchManager] Ожидаем первую волну {firstWaveDelay} сек.");
                yield return new WaitForSeconds(firstWaveDelay);
            }

            int waveNumber = 1;
            while (true)
            {
                Debug.Log($"[MatchManager] Волна #{waveNumber} — спавним обе команды.");
                StartCoroutine(SpawnWave(teamA, 0));
                StartCoroutine(SpawnWave(teamB, 1));
                waveNumber++;
                yield return new WaitForSeconds(waveInterval);
            }
        }

        IEnumerator SpawnWave(TeamWaveConfig cfg, int teamIndex)
        {
            if (cfg == null) yield break;
            if (cfg.spawnPoint == null)
            {
                Debug.LogWarning($"[MatchManager] Команда {teamIndex}: spawnPoint не назначен.");
                yield break;
            }
            if (lane == null)
            {
                Debug.LogWarning($"[MatchManager] Команда {teamIndex}: Lane не назначена.");
                yield break;
            }
            if (cfg.waveComposition == null || cfg.waveComposition.Length == 0)
                yield break;

            // ── Экономика волны (серверная проверка перед спавном) ──
            // Все проверки ДО спавна и ДО любых списаний. При провале любой — волна пропускается
            // целиком (ждём следующий интервал). Цены берутся из штатного Unit.resourceCost префабов.
            int leadershipSum = WaveResourceSum(teamIndex, cfg.waveComposition, leadershipResource);
            int goldSum       = WaveResourceSum(teamIndex, cfg.waveComposition, goldResource);

            // 1) Бюджет состава ≤ пул волны (страховка — UI не должен такое допускать).
            if (leadershipSum > waveLeadershipPool)
            {
                Debug.LogWarning($"[MatchManager] Команда {teamIndex}: состав требует {leadershipSum} лидерства " +
                                 $"при пуле волны {waveLeadershipPool} — волна пропущена.");
                yield break;
            }

            // 2) Лидерство волны влезает в остаток капа карты (часть лимита уже занята живыми юнитами).
            if (leadershipResource != null &&
                !GameResources.instance.CheckAmount(cfg.ownerPlayer, new ResourceWrapper(leadershipResource, leadershipSum)))
            {
                Debug.LogWarning($"[MatchManager] Команда {teamIndex}: лидерство волны ({leadershipSum}) " +
                                 "не влезает в кап карты — волна пропущена.");
                yield break;
            }

            // 3) Золота хватает на весь состав.
            if (goldResource != null &&
                !GameResources.instance.CheckAmount(cfg.ownerPlayer, new ResourceWrapper(goldResource, goldSum)))
            {
                Debug.LogWarning($"[MatchManager] Команда {teamIndex}: не хватает золота ({goldSum}) на волну — волна пропущена.");
                yield break;
            }

            // 4) Списываем золото за всю волну одним вызовом. Для standard decrease=true вычитает;
            //    calledByServer=true → штатный авто-синк клиентам (ResourceSendAdd).
            if (goldResource != null && goldSum > 0)
            {
                Debug.Log($"[MatchManager] Команда {teamIndex}: ChangeAmount — игрок={cfg.ownerPlayer}, " +
                          $"ресурс={goldResource.displayName}, сумма={goldSum}, multiplier=1, decrease=true, calledByServer=true.");
                GameResources.instance.ChangeAmount(cfg.ownerPlayer, new ResourceWrapper(goldResource, goldSum), 1, true, true);
            }

            // Единый источник цели — AttackTarget (тот же путь, что у команд UI). Lane гарантированно задана (см. проверку выше).
            Func<Vector2> targetDelegate = () => AttackTarget(cfg.ownerPlayer);
            Vector2 attackTarget = targetDelegate();

            List<Unit> waveUnits = new List<Unit>();
            // Счётчик фактически заспавненных по префабам — для снимка последней волны («Призыв к Оружию»).
            Dictionary<Unit, int> spawnedCounts = new Dictionary<Unit, int>();
            string attackTargetName = POIName(AttackTargetPOI(cfg.ownerPlayer));
            Debug.Log($"[MatchManager] Спавн волны команды {teamIndex} (player={cfg.ownerPlayer}): " +
                      $"цель={attackTargetName}, точка спавна={cfg.spawnPoint.position}.");

            foreach (WaveEntry entry in cfg.waveComposition)
            {
                if (entry == null || entry.unitToSpawn == null) continue;
                // Апгрейды: проекция базового префаба через активные unitSwaps (from→to). Свопов нет → префаб как есть.
                Unit toSpawn = ResolveSwap(teamIndex, entry.unitToSpawn);
                for (int i = 0; i < entry.count; i++)
                {
                    // Спавн в слоте сетки замка (строй), если spawnGrid задан; иначе — старый спавн в точке.
                    Unit spawned = SpawnSlotPose(cfg, teamIndex, toSpawn.formationPriority, out Vector3 slotPos, out float slotYaw)
                        ? Unit.Spawn(toSpawn, slotPos, slotYaw, cfg.ownerPlayer, 0f)
                        : Unit.Spawn(toSpawn, cfg.spawnPoint.position, 0f, cfg.ownerPlayer, 0f);
                    if (spawned != null)
                    {
                        waveUnits.Add(spawned);
                        // Учёт фактически заспавненного типа (для снимка последней волны).
                        spawnedCounts.TryGetValue(toSpawn, out int prev);
                        spawnedCounts[toSpawn] = prev + 1;
                        // Добавляем в список команды; удалим при гибели через OnDie.
                        teamUnits[teamIndex].Add(spawned);
                        AssignSlot(teamIndex, spawned); // закрепляем слот строя (индекс совпадает с позой спавна)
                        int capturedIndex = teamIndex; // захват для лямбды
                        spawned.OnDie += (u, _, _, _) =>
                        {
                            teamUnits[capturedIndex].Remove(u);
                            OnDefenceUnitDied(capturedIndex, u); // компакция слота (крайний → в дыру)
                            Debug.Log($"[MatchManager] Юнит погиб: {u.name} (команда {capturedIndex}, player={u.owner}).");
                        };
                        // Разовый AttackMove только если у РЯДА юнита нет активного режима и FormationMarch не используется.
                        if (!useFormationMarch && CurrentCommandForUnit(teamIndex, spawned) == BottomTableAction.None)
                            spawned.AttackMove(attackTarget);
                        try { OnUnitSpawned?.Invoke(teamIndex, spawned); }
                        catch (Exception e) { Debug.LogError($"[MatchManager] OnUnitSpawned: {e.Message}"); }
                    }
                    else
                    {
                        Debug.LogWarning($"[MatchManager] Не удалось заспавнить {toSpawn.name} " +
                                         $"для команды {teamIndex} (точка занята или ошибка).");
                    }

                    if (cfg.spawnDelay > 0f) yield return new WaitForSeconds(cfg.spawnDelay);
                }
            }
            Debug.Log($"[MatchManager] Волна команды {teamIndex}: заспавнено {waveUnits.Count} юнитов.");

            // Снимок ФАКТИЧЕСКИ заспавненной волны для «Призыва к Оружию». Обновляем только если что-то
            // создано (пустую/пропущенную волну не запоминаем — прежний снимок остаётся актуальным).
            if (spawnedCounts.Count > 0)
            {
                List<WaveEntry> snapshot = new List<WaveEntry>();
                foreach (KeyValuePair<Unit, int> kv in spawnedCounts)
                    snapshot.Add(new WaveEntry { unitToSpawn = kv.Key, count = kv.Value });
                lastWaveComposition[teamIndex] = snapshot;
            }

            // При спавне волны переотдаём команду ПО РЯДАМ (currentCommand[team][group]):
            //  Атака — ряд перестраивается целиком (SendAttackCommand, включая новых своего класса);
            //  Защита — в слоты двигаем только НОВЫХ юнитов этого ряда (стоящие не трогаем).
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

            // Новые юниты, чей ряд без активной команды (None) или чей класс вне всех рядов — стандартный марш.
            // Строй тегируется рядом юнита (CommandGroupOfUnit): позже команда этого ряда штатно остановит его
            // через StopFormationsForOwnerGroup. Юниты вне рядов идут отдельным строем (-1).
            if (useFormationMarch)
            {
                Dictionary<int, List<Unit>> byGroup = new Dictionary<int, List<Unit>>();
                for (int i = 0; i < waveUnits.Count; i++)
                {
                    Unit wu = waveUnits[i];
                    if (wu == null || wu.dead) continue;
                    if (CurrentCommandForUnit(teamIndex, wu) != BottomTableAction.None) continue; // ряд юнита уже командует
                    int g = CommandGroupOfUnit(wu); // -1 — класс вне всех рядов
                    if (!byGroup.TryGetValue(g, out List<Unit> bucket)) { bucket = new List<Unit>(); byGroup[g] = bucket; }
                    bucket.Add(wu);
                }
                foreach (KeyValuePair<int, List<Unit>> kv in byGroup)
                    FormationMarch.Begin(
                        kv.Value, attackTarget, cfg.ownerPlayer,
                        detectionRadius, regroupDebounce, formationPollInterval, arrivalDistance,
                        slotSpacing, targetDelegate, kv.Key);
            }
        }

    }
}
