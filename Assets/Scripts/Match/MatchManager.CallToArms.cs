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
            if (u == null || lifetime <= 0f) return;

            // [Interflow fix 2026-08-01 summon-lifetime] Было: при уже существующем LifetimeUnit — ранний выход,
            // и заданное способностью время НЕ применялось (юнит жил по значению префаба). Теперь переиспользуем
            // компонент: выставляем срок и переинициализируем.
            LifetimeUnit lt = u.GetComponent<LifetimeUnit>();
            if (lt == null) lt = u.gameObject.AddComponent<LifetimeUnit>();
            lt.lifespan = lifetime;
            lt.currentLifeSpan = lifetime;

            if (GameManager.instance != null) lt.Initialize();
            else Debug.LogWarning("[MatchManager] Призыв к Оружию: GameManager.instance == null — LifetimeUnit не инициализирован.");

            // [Interflow fix 2026-08-01 summon-lifetime] Серверный таймер-гарант. Наблюдалось (прогон 2026-08-01):
            // призванные жили дольше заданного времени. Первопричину по коду воспроизвести не удалось (штатный
            // LifetimeUnit тикает от GameManager.Tick), поэтому дублируем срок независимым таймером: кто сработает
            // первым — тот и убьёт (Unit.Die идемпотентен, `if (dead) return`). Небольшой запас, чтобы не
            // конкурировать со штатным путём и не искажать диагностику.
            StartCoroutine(SummonLifetimeWatchdog(u, lifetime + 1f));
        }

        // Страховка срока жизни призванного: ждём срок и, если юнит всё ещё жив, убиваем сами (сервер).
        IEnumerator SummonLifetimeWatchdog(Unit u, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (u == null || u.dead) yield break;                       // умер штатно — страховка не нужна
            if (NetworkConnectionHandler.isClient) yield break;          // убивает только сервер (правило 6)
            Debug.LogWarning($"[MatchManager] Страховка срока жизни: '{u.unitName}' (netID {u.netID}) прожил дольше {seconds - 1f:0.#} с — убиваю принудительно (штатный LifetimeUnit не сработал).");
            u.Die(-1, null, false);
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
            if (sound != null)
                Presentation.Audio?.PlaySoundClip(sound, at, soundVolume, false);
        }

    }
}
