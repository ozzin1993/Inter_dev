using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============================= ЭКОНОМИКА ВОЛН (партиал MatchManager) ==
    // Стоимость и состав волны. Цена юнита — из штатного Unit.resourceCost (единый источник, без дублей).
    // Серверо-авторитетно (правило 6): TryAddWaveUnit/TryRemoveWaveUnit меняют состав только на сервере
    // и рассылают клиентам (BroadcastWaveComposition); ApplyWaveComposition — приём состава на клиенте.
    // Ассет StrategyCore не трогаем (правило 1).
    public partial class MatchManager
    {
        // Стоимость одного префаба по конкретному ресурсу (читается из штатного Unit.resourceCost).
        // 0 — если ресурс в стоимости юнита не указан. Цены живут только в префабе, здесь не дублируются.
        // public — чтобы UI конструктора волны читал цену той же логикой (единый источник).
        public static int PrefabResourceCost(Unit prefab, Resource resource)
        {
            if (prefab == null || resource == null || prefab.resourceCost == null) return 0;
            for (int i = 0; i < prefab.resourceCost.Length; i++)
            {
                ResourceWrapper rw = prefab.resourceCost[i];
                if (rw != null && rw.type == resource) return rw.value;
            }
            return 0;
        }

        // Суммарная стоимость состава волны по ресурсу для команды (кол-во × эффективная цена с учётом модификаторов B28).
        // Инстанс-метод (не static): эффективная цена per-team — единый источник для UI, спавна и списания.
        int WaveResourceSum(int team, WaveEntry[] composition, Resource resource)
        {
            if (composition == null || resource == null) return 0;
            int sum = 0;
            for (int i = 0; i < composition.Length; i++)
            {
                WaveEntry e = composition[i];
                if (e == null || e.unitToSpawn == null || e.count <= 0) continue;
                sum += EffectiveResourceCost(team, e.unitToSpawn, resource) * e.count;
            }
            return sum;
        }

        /// <summary>
        /// Текущее количество юнитов данного типа в составе волны команды (0, если типа нет).
        /// Единый источник для UI (читается из того же waveComposition, что и спавн).
        /// </summary>
        public int WaveUnitCount(int teamIndex, Unit prefab)
        {
            if (prefab == null) return 0;
            TeamWaveConfig cfg = Team(teamIndex);
            WaveEntry[] comp = cfg != null ? cfg.waveComposition : null;
            if (comp == null) return 0;
            for (int i = 0; i < comp.Length; i++)
                if (comp[i] != null && comp[i].unitToSpawn == prefab) return comp[i].count;
            return 0;
        }

        /// <summary>
        /// Сервер: добавить один юнит указанного типа в состав волны команды (+1 к существующему типу
        /// или новая запись). Отказ, если суммарное лидерство состава превысит пул волны.
        /// Возвращает true при успешном изменении.
        /// </summary>
        public bool TryAddWaveUnit(int teamIndex, Unit prefab)
        {
            if (NetworkConnectionHandler.isClient) return false; // правка состава — только сервер
            if (prefab == null) return false;
            TeamWaveConfig cfg = Team(teamIndex);
            if (cfg == null) return false;

            // Валидация (E): добавлять можно только доступных юнитов (если набор доступных настроен).
            if (!IsUnitAvailable(teamIndex, prefab))
            {
                Debug.Log($"[MatchManager] Команда {teamIndex}: {prefab.name} не в доступных юнитах — добавление отклонено.");
                return false;
            }

            // Проверка пула: суммарное лидерство с учётом добавляемого юнита (эффективная цена — B28; лидерство на будущее N5).
            int projected = WaveResourceSum(teamIndex, cfg.waveComposition, leadershipResource)
                            + EffectiveResourceCost(teamIndex, prefab, leadershipResource);
            if (projected > waveLeadershipPool)
            {
                Debug.Log($"[MatchManager] Команда {teamIndex}: добавление {prefab.name} превысит пул лидерства " +
                          $"({projected} > {waveLeadershipPool}) — отклонено.");
                return false;
            }

            List<WaveEntry> list = new List<WaveEntry>(cfg.waveComposition ?? Array.Empty<WaveEntry>());
            WaveEntry existing = list.Find(e => e != null && e.unitToSpawn == prefab);
            if (existing != null) existing.count++;
            else list.Add(new WaveEntry { unitToSpawn = prefab, count = 1 });
            cfg.waveComposition = list.ToArray();

            OnWaveCompositionChanged?.Invoke(teamIndex);
            BroadcastWaveComposition(teamIndex);
            return true;
        }

        /// <summary>
        /// Сервер: убрать один юнит указанного типа из состава волны команды (−1; при 0 запись удаляется).
        /// Возвращает true при успешном изменении.
        /// </summary>
        public bool TryRemoveWaveUnit(int teamIndex, Unit prefab)
        {
            if (NetworkConnectionHandler.isClient) return false;
            if (prefab == null) return false;
            TeamWaveConfig cfg = Team(teamIndex);
            if (cfg == null || cfg.waveComposition == null) return false;

            List<WaveEntry> list = new List<WaveEntry>(cfg.waveComposition);
            WaveEntry existing = list.Find(e => e != null && e.unitToSpawn == prefab);
            if (existing == null) return false;

            existing.count--;
            if (existing.count <= 0) list.Remove(existing);
            cfg.waveComposition = list.ToArray();

            OnWaveCompositionChanged?.Invoke(teamIndex);
            BroadcastWaveComposition(teamIndex);
            return true;
        }

        /// <summary>
        /// Сервер: текущий состав команды как параллельные массивы (typeID + count) для сетевой рассылки.
        /// Формирование данных — единая точка здесь, в MatchManager.
        /// </summary>
        public void GetWaveCompositionData(int teamIndex, out int[] typeIDs, out int[] counts)
        {
            TeamWaveConfig cfg = Team(teamIndex);
            WaveEntry[] comp = cfg != null ? cfg.waveComposition : null;
            int n = comp != null ? comp.Length : 0;
            typeIDs = new int[n];
            counts  = new int[n];
            for (int i = 0; i < n; i++)
            {
                WaveEntry e = comp[i];
                typeIDs[i] = (e != null && e.unitToSpawn != null) ? e.unitToSpawn.unitTypeID : 0;
                counts[i]  = (e != null) ? e.count : 0;
            }
        }

        /// <summary>
        /// Клиент: применить присланный сервером состав волны команды. Пишем прямо в waveComposition —
        /// это единый источник, который читает UI конструктора (на сервере и на клиенте одинаково).
        /// Префабы резолвятся штатно через GameManager.gameUnits по typeID. На сервере не вызывается.
        /// </summary>
        public void ApplyWaveComposition(int teamIndex, int[] typeIDs, int[] counts)
        {
            TeamWaveConfig cfg = Team(teamIndex);
            if (cfg == null || typeIDs == null || counts == null) return;

            List<WaveEntry> list = new List<WaveEntry>();
            int n = Mathf.Min(typeIDs.Length, counts.Length);
            for (int i = 0; i < n; i++)
            {
                if (counts[i] <= 0) continue;
                if (GameManager.instance != null &&
                    GameManager.instance.gameUnits.TryGetValue(typeIDs[i], out Unit prefab) && prefab != null)
                    list.Add(new WaveEntry { unitToSpawn = prefab, count = counts[i] });
            }
            cfg.waveComposition = list.ToArray();

            OnWaveCompositionChanged?.Invoke(teamIndex);
        }

        // Сервер: разослать актуальный состав команды клиентам после каждого изменения.
        // Единая точка вызова (из Try*WaveUnit). Сам RPC — NetworkDataSync.WaveComposition.cs.
        void BroadcastWaveComposition(int teamIndex)
        {
            if (NetworkDataSync.instance == null) return;
            GetWaveCompositionData(teamIndex, out int[] typeIDs, out int[] counts);
            NetworkDataSync.instance.WaveCompositionSend(teamIndex, typeIDs, counts);
        }
    }
}
