using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Партиал MatchManager: общая логика призыва + спавн фикс. набора у вражеской точки («Врата Бездны» ГЗ орков).
    //   • ConfigureSummonedUnit — ЕДИНЫЙ per-unit хелпер (маркер, опц. время жизни, компенсация лидерства,
    //     регистрация + OnDie + команда). Используется и SummonLastWave («Призыв к Оружию»), и Вратами (правило 5).
    //   • SummonFixedSquadAtEnemyPoint — вход «Врат»: prefab × count у ближайшей вражеской точки, постоянные,
    //     фикс. Атака, лимит одновременно живых.
    // Серверо-авторитетно (правило 6). Новый partial-файл — ассет StrategyCore не трогается (правило 1).
    public partial class MatchManager
    {
        // Живые орки «Врат Бездны» по командам (для лимита одновременно живых). Индекс = teamIndex.
        readonly List<Unit>[] portalSummonedUnits = { new List<Unit>(), new List<Unit>() };

        /// <summary>
        /// Настроить уже заспавненного юнита как «призванного»: маркер SummonedUnit, (опц.) время жизни,
        /// компенсация Limited-ресурсов (лидерство → «бесплатно»), регистрация в нужном списке + OnDie + выдача
        /// команды. Единый источник истины для SummonLastWave (ополчение) и SummonFixedSquadAtEnemyPoint (Врата),
        /// правило 5. Вызывается только на сервере (внутри серверных методов призыва).
        ///   lifetime > 0  — временный (штатный LifetimeUnit); lifetime <= 0 — постоянный (LifetimeUnit не навешивается).
        ///   obeyCommands  — слушает ли общие приказы; если нет — держит фикс. команду command (Защита/Атака).
        /// </summary>
        void ConfigureSummonedUnit(Unit u, int teamIndex, int ownerPlayer,
                                   bool obeyCommands, BottomTableAction command, float lifetime)
        {
            if (u == null) return;

            // Маркер «призван»: слушает ли общие приказы; если нет — какую фикс. команду держит.
            SummonedUnit mark = u.GetComponent<SummonedUnit>();
            if (mark == null) mark = u.gameObject.AddComponent<SummonedUnit>();
            mark.obeyCommands = obeyCommands;
            mark.command = command;

            // Время жизни — штатный LifetimeUnit. lifetime <= 0 → постоянный юнит (Врата): компонент не навешиваем.
            if (lifetime > 0f) ApplySummonLifetime(u, lifetime);

            // «Бесплатно» по Limited-ресурсам (лидерство): компенсация до нуля (нетто-usage = 0 при жизни и смерти).
            CompensateSummonLimitedCost(u);

            int capt = teamIndex;
            if (obeyCommands)
            {
                // Слушает общие приказы → ведём как обычного юнита команды (в teamUnits), удаление по OnDie как у волны.
                teamUnits[teamIndex].Add(u);
                u.OnDie += (du, _, _, _) => { teamUnits[capt].Remove(du); };
                ReissueCurrentCommand(u); // подхватить текущий режим команды (None → idle)
            }
            else
            {
                // Не слушает общих приказов → отдельный список, фиксированная команда (Защита/Атака).
                summonedUnits[teamIndex].Add(u);
                AssignSlot(teamIndex, u);
                u.OnDie += (du, _, _, _) =>
                {
                    summonedUnits[capt].Remove(du);
                    OnDefenceUnitDied(capt, du); // компакция строя
                };
                if (command == BottomTableAction.Defence)
                {
                    if (!MoveToDefenceSlot(teamIndex, u))
                    {
                        Vector2 def = DefenceTarget(ownerPlayer);
                        if (def != Vector2.zero) u.Move(def); // фоллбэк: нет активной сетки
                    }
                }
                else
                {
                    IssueSummonedCommand(u, ownerPlayer, command);
                }
            }

            // Покрытие хаба смертей/могилок (§6.1: инвок OnUnitSpawned из наших путей спавна, кроме башен) — как
            // SpawnWave, чтобы DeathEventHub и Graves подписались на OnDie призванного. teamIndex/u уже валидны.
            try { OnUnitSpawned?.Invoke(teamIndex, u); }
            catch (System.Exception e) { Debug.LogError($"[MatchManager] OnUnitSpawned (summoned): {e.Message}"); }
        }

        /// <summary>
        /// Сервер: призвать фиксированный набор (prefab × count) у ближайшей вражеской точки для команды кастера.
        /// Постоянные (lifetime=0), «бесплатные», фикс. команда Атака (сразу AttackMove на точку). Соблюдается лимит
        /// maxAlivePerPlayer: спавнится не больше, чем свободно (0 = без лимита). Точка входа — GatesOfAbyssActive
        /// (логика централизована здесь, правило 5). Возвращает число фактически заспавненных.
        /// </summary>
        public int SummonFixedSquadAtEnemyPoint(int castingPlayer, Unit prefab, int count, int maxAlivePerPlayer,
                                                float spawnSpread, VFXReferencer vfx, AudioClip sound, float soundVolume)
        {
            if (NetworkConnectionHandler.isClient) return 0; // спавн/жизнь/команды — только сервер (правило 6)

            if (prefab == null) { Debug.LogWarning("[Врата Бездны] Не задан префаб орка — призыв пропущен."); return 0; }
            if (count <= 0)     { Debug.LogWarning($"[Врата Бездны] count={count} ≤ 0 — призыв пропущен."); return 0; }

            int teamIndex = TeamIndexOfOwner(castingPlayer);
            if (teamIndex < 0) { Debug.LogWarning($"[Врата Бездны] Игрок {castingPlayer} не владеет командой."); return 0; }

            TeamWaveConfig cfg = Team(teamIndex);
            if (cfg == null) { Debug.LogWarning($"[Врата Бездны] Команда {teamIndex} без конфига."); return 0; }

            // Ближайшая вражеская точка интереса (цель команды Атака). Нет точки → призыв пропускаем.
            PointOfInterest poi = AttackTargetPOI(castingPlayer);
            if (poi == null) { Debug.LogWarning($"[Врата Бездны] У команды {teamIndex} нет ближайшей вражеской точки — призыв пропущен."); return 0; }
            Transform spawnAt = poi.transform;

            // Лимит одновременно живых: чистим мёртвые ссылки, считаем свободные слоты (0 = безлимит).
            PortalPruneDead(portalSummonedUnits[teamIndex]);
            int free = (maxAlivePerPlayer > 0)
                ? Mathf.Max(0, maxAlivePerPlayer - portalSummonedUnits[teamIndex].Count)
                : count;
            int toSpawn = Mathf.Min(count, free);
            if (toSpawn <= 0)
            {
                Debug.Log($"[Врата Бездны] Лимит живых достигнут (team {teamIndex}: {portalSummonedUnits[teamIndex].Count}/{maxAlivePerPlayer}) — новых не добавляем.");
                return 0;
            }

            // Презентация призыва один раз в точке спавна (серверо-онли, как VFX CallToArms/FlameCloak).
            PlaySummonFx(spawnAt, vfx, sound, soundVolume);

            int spawned = 0;
            for (int i = 0; i < toSpawn; i++)
            {
                Unit u = Unit.Spawn(prefab, spawnAt.position, spawnAt.eulerAngles.y, cfg.ownerPlayer, spawnSpread);
                if (u == null) { Debug.LogWarning("[Врата Бездны] Не удалось заспавнить орка (точка занята?)."); continue; }

                // Общая настройка призванного: постоянный (lifetime=0), не слушает приказы, фикс. Атака.
                ConfigureSummonedUnit(u, teamIndex, cfg.ownerPlayer, false, BottomTableAction.Attack, 0f);

                // Учёт для лимита живых + снятие по смерти (доп. подписка к штатной из ConfigureSummonedUnit).
                int capt = teamIndex;
                portalSummonedUnits[teamIndex].Add(u);
                u.OnDie += (du, _, _, _) => portalSummonedUnits[capt].Remove(du);
                spawned++;
            }

            Debug.Log($"[Врата Бездны] Команда {teamIndex} (player={cfg.ownerPlayer}): призвано {spawned}/{count} орков у точки '{spawnAt.name}', лимит {maxAlivePerPlayer}.");
            return spawned;
        }

        // Убрать null/мёртвые ссылки из списка (честный подсчёт лимита живых).
        static void PortalPruneDead(List<Unit> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] == null || list[i].dead) list.RemoveAt(i);
        }
    }
}
