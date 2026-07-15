using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============================= ПРИЗЫВ ОТ НОСИТЕЛЯ (партиал MatchManager) — кирпич B16 ==
    // Серверный призыв юнитов В ПОЗИЦИИ НОСИТЕЛЯ (в отличие от призыва замка «Врата»/«Призыв к Оружию»).
    // Единый вход SummonFromUnit: спавн prefab × count у носителя, reuse ConfigureSummonedUnit (маркер SummonedUnit,
    //   net-zero лидерство, штатный LifetimeUnit при lifetime > 0, регистрация/OnDie/команда, покрытие хаба смертей).
    // Лимит одновременно живых — per-НОСИТЕЛЬ. Опция killSummonsOnOwnerDeath — смерть носителя добивает призванных.
    // Триггеры (период / «в бою» / по абилке) — СНАРУЖИ: SummonAbility (SO) + штатный AutoAbilityUser (правило 5, reuse).
    // Серверо-авторитетно (правило 6). Новый partial-файл — ассет StrategyCore не трогается (правило 1).
    public partial class MatchManager
    {
        // Учёт призванных по носителю: живые (лимит per-носитель) + опция добить при смерти носителя + флаг подписки.
        class CasterSummons
        {
            public readonly List<Unit> live = new List<Unit>();
            public bool killOnOwnerDeath;
            public bool ownerDeathHooked;
        }

        // Носитель → его призванные. Запись снимается при смерти носителя (OnCasterDied).
        readonly Dictionary<Unit, CasterSummons> summonsByCaster = new Dictionary<Unit, CasterSummons>();

        /// <summary>
        /// Сервер: призвать prefab × count в позиции носителя caster для его команды. Reuse ConfigureSummonedUnit
        /// (net-zero лидерство; LifetimeUnit при lifetime &gt; 0; маркер/команда/покрытие хаба смертей — правило 5).
        /// Лимит одновременно живых на носителя — maxAlivePerCaster (0 = без лимита). killSummonsOnOwnerDeath —
        /// смерть носителя добивает его призванных (дефолт false: живут дальше / по своему времени). spawnSpread
        /// разносит точки спавна при count &gt; 1. Возвращает число фактически заспавненных. Антирекурсию
        /// (призванный не призывает) держит вызывающая SummonAbility — метод универсален (его же зовёт B15 DeathEffects).
        /// </summary>
        public int SummonFromUnit(Unit caster, Unit prefab, int count, float lifetime,
                                  bool obeyCommands, BottomTableAction command,
                                  int maxAlivePerCaster, bool killSummonsOnOwnerDeath, float spawnSpread)
        {
            if (NetworkConnectionHandler.isClient) return 0;      // спавн/жизнь/команды — только сервер (правило 6)
            if (caster == null) return 0;                          // нет носителя — нет позиции призыва
            if (prefab == null) { Debug.LogWarning("[Призыв от носителя] Не задан префаб — призыв пропущен."); return 0; }
            if (count <= 0)     { Debug.LogWarning($"[Призыв от носителя] count={count} ≤ 0 — призыв пропущен."); return 0; }

            int teamIndex = TeamIndexOfOwner(caster.owner);
            if (teamIndex < 0) { Debug.LogWarning($"[Призыв от носителя] Игрок {caster.owner} не владеет командой — пропуск."); return 0; }
            if (Team(teamIndex) == null) { Debug.LogWarning($"[Призыв от носителя] Команда {teamIndex} без конфига — пропуск."); return 0; }

            // Лимит живых per-носитель: чистим мёртвые ссылки, считаем свободные слоты (0 = безлимит).
            CasterSummons cs = SummonsFor(caster);
            if (killSummonsOnOwnerDeath) cs.killOnOwnerDeath = true;   // если хоть один вызов просит — добивать
            SummonPruneDead(cs.live);
            int free = (maxAlivePerCaster > 0) ? Mathf.Max(0, maxAlivePerCaster - cs.live.Count) : count;
            int toSpawn = Mathf.Min(count, free);
            if (toSpawn <= 0)
            {
                Debug.Log($"[Призыв от носителя] Лимит живых достигнут (носитель '{caster.name}': {cs.live.Count}/{maxAlivePerCaster}) — пропуск.");
                return 0;
            }

            Vector3 pos = caster.transform.position;
            float yaw = caster.transform.eulerAngles.y;
            int ownerPlayer = caster.owner;

            int spawned = 0;
            for (int i = 0; i < toSpawn; i++)
            {
                Unit u = Unit.Spawn(prefab, pos, yaw, ownerPlayer, spawnSpread);
                if (u == null) { Debug.LogWarning("[Призыв от носителя] Не удалось заспавнить юнита (точка занята?)."); continue; }

                // Единый путь настройки призванного (правило 5): маркер, net-zero лидерство, время, команда, хаб смертей.
                ConfigureSummonedUnit(u, teamIndex, ownerPlayer, obeyCommands, command, lifetime);

                // Учёт живых per-носитель + снятие по смерти призванного (доп. к штатной подписке ConfigureSummonedUnit).
                cs.live.Add(u);
                u.OnDie += (du, _, _, _) => cs.live.Remove(du);
                spawned++;
            }

            // Смерть носителя: снять учёт (и, по опции, добить призванных). Подписка — однократно на носителя.
            // НЕ подписываем мёртвого (B15 посмертный призыв: caster уже в своём OnDie — подписка опоздает).
            if (spawned > 0 && !caster.dead && !cs.ownerDeathHooked)
            {
                cs.ownerDeathHooked = true;
                Unit capturedCaster = caster;
                caster.OnDie += (du, _, _, _) => OnCasterDied(capturedCaster);
            }

            // Мёртвый носитель OnCasterDied не получит — снять его учёт сразу (иначе вечная запись в словаре).
            // Призванные живут дальше: их снятие идёт через собственный OnDie (замыкание на cs.live уцелело).
            if (caster.dead) summonsByCaster.Remove(caster);

            Debug.Log($"[Призыв от носителя] Носитель '{caster.name}' (player={ownerPlayer}): призвано {spawned}/{count}, лимит {maxAlivePerCaster}, время {lifetime}.");
            return spawned;
        }

        // Носитель умер: по опции добить его живых призванных штатным Die; снять учёт (призыв от мёртвого невозможен).
        void OnCasterDied(Unit caster)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (caster == null || !summonsByCaster.TryGetValue(caster, out CasterSummons cs)) return;
            if (cs.killOnOwnerDeath)
                for (int i = cs.live.Count - 1; i >= 0; i--)
                    if (cs.live[i] != null && !cs.live[i].dead) cs.live[i].Die(-1, null, false);
            summonsByCaster.Remove(caster);
        }

        // Учёт носителя (создаёт при первом обращении).
        CasterSummons SummonsFor(Unit caster)
        {
            if (!summonsByCaster.TryGetValue(caster, out CasterSummons cs))
            {
                cs = new CasterSummons();
                summonsByCaster[caster] = cs;
            }
            return cs;
        }

        // Убрать null/мёртвые ссылки (честный подсчёт лимита живых).
        static void SummonPruneDead(List<Unit> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] == null || list[i].dead) list.RemoveAt(i);
        }
    }
}
