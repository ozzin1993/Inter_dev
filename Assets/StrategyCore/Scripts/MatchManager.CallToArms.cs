using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ===== «ПРИЗЫВ К ОРУЖИЮ» (партиал MatchManager) =====
    // Повтор последней волны как временное ополчение (SummonLastWave) + состояние. Серверо-авторитетно (правило 6). Есть временный диаг-код.
    public partial class MatchManager
    {
        // ======================== «ПРИЗЫВ К ОРУЖИЮ» ========================
        // Снимок состава ПОСЛЕДНЕЙ фактически заспавненной волны каждой команды (prefab → count).
        // Обновляется в SpawnWave только при успешном спавне. Источник для способности CallToArmsActive.
        readonly List<WaveEntry>[] lastWaveComposition = new List<WaveEntry>[2] { new List<WaveEntry>(), new List<WaveEntry>() };

        // Призванные способностью юниты (временное ополчение) каждой команды. НЕ входят в teamUnits,
        // поэтому не попадают под команды Атака/Защита. Удаляются по OnDie. Используются для переотдачи
        // команды при потере точки (ReissueSummonedCommand). Только для не слушающих общих приказов.
        readonly List<Unit>[] summonedUnits = new List<Unit>[2] { new List<Unit>(), new List<Unit>() };

        // [ВРЕМЕННАЯ ДИАГНОСТИКА CallToArms] Наблюдение за таймерами жизни призванных (баг «не умирают по таймеру»).
        // Удалить целиком вместе с SummonLifetimeDiagTrack/SummonLifetimeDiagTick после локализации бага.
        readonly List<LifetimeUnit> summonDiag = new List<LifetimeUnit>();
        float summonDiagTimer;
        bool  summonDiagSubscribed;

        // ======================== «ПРИЗЫВ К ОРУЖИЮ» (повторная волна ополчения) ========================

        /// <summary>
        /// Сервер: призвать дубль последней фактически заспавненной волны команды кастера вне очереди.
        /// Призванные юниты: живут lifetime сек (LifetimeUnit), не тратят лидерство/золото (компенсация),
        /// не получают обычных команд (маркер SummonedUnit), обороняют передовую свою точку (DefenceTarget).
        /// Точка входа способности CallToArmsActive (вся логика централизована здесь, правило 5).
        /// </summary>
        public void SummonLastWave(int castingPlayer, float lifetime,
                                   bool obeyCommands, BottomTableAction defaultCommand,
                                   VFXReferencer vfx, AudioClip sound, float soundVolume)
        {
            if (NetworkConnectionHandler.isClient) return; // спавн/жизнь/команды — только сервер (правило 6)

            int teamIndex = TeamIndexOfOwner(castingPlayer);
            if (teamIndex < 0) { Debug.LogWarning($"[MatchManager] Призыв к Оружию: игрок {castingPlayer} не владеет командой."); return; }

            TeamWaveConfig cfg = Team(teamIndex);
            if (cfg == null || cfg.spawnPoint == null)
            { Debug.LogWarning($"[MatchManager] Призыв к Оружию: команда {teamIndex} без конфига/точки спавна."); return; }

            List<WaveEntry> comp = lastWaveComposition[teamIndex];
            if (comp == null || comp.Count == 0)
            { Debug.Log($"[MatchManager] Призыв к Оружию: у команды {teamIndex} ещё не было волн — призыв пропущен."); return; }

            // Презентация один раз в точке спавна. Серверо-онли (как и весь каст Active) — на чистых клиентах
            // не видна/не слышна, как и VFX FlameCloakActive (полная сетевая синхронизация — отдельная задача).
            PlaySummonFx(cfg.spawnPoint, vfx, sound, soundVolume);

            int summoned = 0;
            foreach (WaveEntry entry in comp)
            {
                if (entry == null || entry.unitToSpawn == null) continue;
                for (int i = 0; i < entry.count; i++)
                {
                    Unit u = Unit.Spawn(entry.unitToSpawn, cfg.spawnPoint.position, 0f, cfg.ownerPlayer, 0f);
                    if (u == null)
                    {
                        Debug.LogWarning($"[MatchManager] Призыв к Оружию: не удалось заспавнить {entry.unitToSpawn.name} (точка занята?).");
                        continue;
                    }

                    // Настройка призванного (маркер, время жизни, компенсация лидерства, регистрация, команда)
                    // — единый хелпер, общий с «Вратами Бездны» (см. MatchManager.SummonFixed.cs, правило 5).
                    ConfigureSummonedUnit(u, teamIndex, cfg.ownerPlayer, obeyCommands, defaultCommand, lifetime);

                    summoned++;
                }
            }
            Debug.Log($"[MatchManager] Призыв к Оружию: команда {teamIndex} (player={cfg.ownerPlayer}), призвано {summoned} юнитов, " +
                      $"жизнь {lifetime} с, общие приказы={(obeyCommands ? "слушают" : "нет (" + defaultCommand + ")")}.");
        }

        // Навесить штатный LifetimeUnit на призванного. Префабы волны его не имеют, поэтому Unit.Initialize
        // его не проинициализировал → вызываем Initialize() вручную (подпишет Tick и OnDie). Если компонент
        // уже был на префабе — ассет проинициализировал сам, не трогаем (во избежание двойной подписки).
        void ApplySummonLifetime(Unit u, float lifetime)
        {
            // [ВРЕМЕННАЯ ДИАГНОСТИКА CallToArms] Ранее тихие выходы теперь логируются.
            if (u == null) { Debug.LogWarning("[CallToArms ДИАГНОСТИКА] ApplySummonLifetime: u == null — время жизни не назначено."); return; }
            if (lifetime <= 0f) { Debug.LogWarning($"[CallToArms ДИАГНОСТИКА] {u.name}: lifetime={lifetime} ≤ 0 — время жизни не назначено."); return; }

            LifetimeUnit existing = u.GetComponent<LifetimeUnit>();
            if (existing != null) // уже инициализирован ассетом (компонент был на префабе)
            {
                Debug.Log($"[CallToArms ДИАГНОСТИКА] {u.name} #{existing.GetInstanceID()}: LifetimeUnit уже был на префабе, " +
                          $"lifespan={existing.lifespan}, currentLifeSpan={existing.currentLifeSpan} — новый не добавляется.");
                SummonLifetimeDiagTrack(u, existing);
                return;
            }

            LifetimeUnit lt = u.gameObject.AddComponent<LifetimeUnit>();
            lt.lifespan = lifetime;
            if (GameManager.instance != null) lt.Initialize();
            else { Debug.LogWarning("[MatchManager] Призыв к Оружию: GameManager.instance == null — LifetimeUnit не инициализирован."); return; }
            Debug.Log($"[CallToArms ДИАГНОСТИКА] {u.name} #{lt.GetInstanceID()} (netID={u.netID}): LifetimeUnit добавлен и инициализирован, " +
                      $"currentLifeSpan={lt.currentLifeSpan}.");
            SummonLifetimeDiagTrack(u, lt);
        }

        // [ВРЕМЕННАЯ ДИАГНОСТИКА CallToArms] Взять призванного под наблюдение: лог факта смерти + периодический лог таймера.
        void SummonLifetimeDiagTrack(Unit u, LifetimeUnit lt)
        {
            summonDiag.Add(lt);
            u.OnDie += (du, killer, killerUnit, rewards) =>
                Debug.Log($"[CallToArms ДИАГНОСТИКА] {du.name} (netID={du.netID}) умер: killerPlayer={killer}, rewards={rewards} " +
                          "(смерть по таймеру выглядит так: killerPlayer=-1, rewards=False).");
            if (!summonDiagSubscribed && GameManager.instance != null)
            {
                GameManager.instance.Tick += SummonLifetimeDiagTick;
                summonDiagSubscribed = true;
            }
        }

        // [ВРЕМЕННАЯ ДИАГНОСТИКА CallToArms] Раз в ~10 с логирует таймеры живых призванных.
        // ВАЖНО: метод сам работает от GameManager.Tick — если после призыва этих логов нет ВООБЩЕ,
        // значит Tick не тикает, и это и есть причина «бессмертия» (LifetimeUnit тоже живёт от Tick).
        void SummonLifetimeDiagTick()
        {
            summonDiagTimer += GameManager.instance.currentDeltaTime;
            if (summonDiagTimer < 10f) return; // интервал зашит осознанно: код временный, удаляется целиком (не Inspector)
            summonDiagTimer = 0f;
            for (int i = summonDiag.Count - 1; i >= 0; i--)
            {
                LifetimeUnit lt = summonDiag[i];
                if (lt == null) { summonDiag.RemoveAt(i); continue; } // объект юнита уничтожен — снимаем с наблюдения
                Debug.Log($"[CallToArms ДИАГНОСТИКА] {lt.name} #{lt.GetInstanceID()}: currentLifeSpan={lt.currentLifeSpan:F1} из {lt.lifespan}.");
            }
        }

        // «Бесплатность» призыва по Limited-ресурсам (лидерство): ассет в Unit.Initialize занял ресурс
        // (decrease=true → usage += value). Сразу возвращаем (decrease=false → usage −= value): нетто-ноль при жизни.
        // На смерти ассет вернёт ресурс ещё раз (Unit.Die) → подписываемся и зеркалим (повторно занимаем),
        // чтобы суммарный usage не изменился ни при жизни, ни при смерти. Серверо-авторитетно (calledByServer=true).
        void CompensateSummonLimitedCost(Unit u)
        {
            if (u == null || u.resourceCost == null || GameResources.instance == null) return;

            ResourceWrapper[] costs = u.resourceCost;
            int owner = u.owner;

            for (int i = 0; i < costs.Length; i++)
            {
                ResourceWrapper rw = costs[i];
                if (rw == null || rw.type == null || !rw.type.limited) continue;
                GameResources.instance.ChangeAmount(owner, rw, 1, false, true); // вернуть занятое ассетом при спавне
            }

            u.OnDie += (du, _, _, _) =>
            {
                if (GameResources.instance == null) return;
                for (int i = 0; i < costs.Length; i++)
                {
                    ResourceWrapper rw = costs[i];
                    if (rw == null || rw.type == null || !rw.type.limited) continue;
                    GameResources.instance.ChangeAmount(owner, rw, 1, true, true); // отменить возврат ассета на смерти
                }
            };
        }

        // Выдать призванному (не слушающему общих приказов) его фиксированную команду:
        // Attack → AttackMove на следующую вражескую точку, иначе Defence → Move на передовую свою точку.
        void IssueSummonedCommand(Unit u, int player, BottomTableAction command)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (u == null || u.dead) return;

            if (command == BottomTableAction.Attack)
            {
                Vector2 atk = AttackTarget(player);
                if (atk != Vector2.zero) u.AttackMove(atk);
            }
            else // Defence (и любой иной режим трактуем как оборону)
            {
                Vector2 def = DefenceTarget(player);
                if (def != Vector2.zero) u.Move(def);
            }
        }

        // Переотдать команду всем живым призванным из отдельного списка (фронт сместился при потере точки →
        // Attack/Defence пересчитают цель вживую). Каждому — его собственная фиксированная команда (маркер).
        void ReissueSummonedCommand()
        {
            if (NetworkConnectionHandler.isClient) return;
            for (int t = 0; t < 2; t++)
            {
                TeamWaveConfig cfg = Team(t);
                if (cfg == null) continue;
                List<Unit> list = summonedUnits[t];
                for (int i = 0; i < list.Count; i++)
                {
                    Unit u = list[i];
                    if (u == null || u.dead) continue;
                    SummonedUnit mark = u.GetComponent<SummonedUnit>();
                    BottomTableAction cmd = mark != null ? mark.command : BottomTableAction.Defence;
                    // Защита → в слот активной сетки (фронт мог смениться); Атака → как раньше.
                    if (cmd == BottomTableAction.Defence)
                    {
                        if (!MoveToDefenceSlot(t, u))
                        {
                            Vector2 def = DefenceTarget(cfg.ownerPlayer);
                            if (def != Vector2.zero) u.Move(def);
                        }
                    }
                    else IssueSummonedCommand(u, cfg.ownerPlayer, cmd);
                }
            }
        }

        // Презентация призыва (VFX + звук) один раз в точке спавна. Оба элемента опциональны.
        void PlaySummonFx(Transform at, VFXReferencer vfx, AudioClip sound, float soundVolume)
        {
            if (at == null) return;
            // VFX: спавним экземпляр в точке спавна. По конвенции ассета VFX сам себя уничтожает после проигрывания.
            if (vfx != null) Instantiate(vfx.gameObject, at.position, at.rotation);
            // Звук: штатный SoundFXManager. mixerGroup не задаём (группа по умолчанию).
            if (sound != null && SoundFXManager.instance != null)
                SoundFXManager.instance.PlaySoundClip(sound, at, soundVolume, null);
        }

    }
}
