using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============================= ЭКОНОМИКА И ПОМЕТКИ ВОЛНЫ 2.0 (партиал MatchManager) ==
    // Модель пометок вместо количеств (редизайн 2026-07-20 + амендмент count 2026-07-23):
    //   • базовые юниты (waveUnits с ролью Basic) выходят КАЖДУЮ волну всегда и бесплатно;
    //   • игрок помечает ТИПЫ (роль Available) на автопризыв (каждую волну, в пределах дохода) или разовый призыв (одна волна, с резервом золота);
    //   • число копий на тип — единый источник cfg.waveUnits (дефолт 1), см. WaveCountOf.
    // Цена — EffectiveResourceCost (B28), НЕ сырой PrefabResourceCost. Серверо-авторитетно (правило 6): пометки
    // меняет только сервер (RPC WaveMarkServerRpc → NetworkDataSync.WaveMarks) и рассылает клиентам (BroadcastMarks).
    // Ассет StrategyCore не трогаем (правило 1).
    public partial class MatchManager
    {
        // Стоимость одного префаба по конкретному ресурсу (штатный Unit.resourceCost). 0 — если ресурс не указан.
        // База для EffectiveResourceCost (B28). public — единый источник цены для UI и списания.
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

        // Число копий типа в волне: из cfg.waveUnits (дефолт 1). Единый источник count для юнитов волны (тех-открытые — 1).
        public int WaveCountOf(int team, Unit unit)
        {
            TeamWaveConfig cfg = Team(team);
            return WaveCountOf(cfg, unit);
        }

        int WaveCountOf(TeamWaveConfig cfg, Unit unit)
        {
            if (cfg == null || unit == null) return 1;
            WaveUnitEntry[] arr = cfg.waveUnits;
            if (arr != null)
                for (int i = 0; i < arr.Length; i++)
                    if (arr[i] != null && arr[i].unit == unit) return Mathf.Max(1, arr[i].count);
            return 1;
        }

        // Префаб по стабильному unitTypeID (штатный GameManager.gameUnits). null — не найден.
        Unit ResolveUnitById(int unitTypeID)
        {
            if (GameManager.Instance == null || GameManager.Instance.gameUnits == null) return null;
            return GameManager.Instance.gameUnits.TryGetValue(unitTypeID, out Unit u) ? u : null;
        }

        // Базовый ли это юнит команды (роль Basic в waveUnits — выходит всегда, не помечается).
        bool IsBasicUnit(TeamWaveConfig cfg, Unit unit)
        {
            if (cfg == null || cfg.waveUnits == null || unit == null) return false;
            for (int i = 0; i < cfg.waveUnits.Length; i++)
                if (cfg.waveUnits[i] != null && cfg.waveUnits[i].role == WaveUnitRole.Basic && cfg.waveUnits[i].unit == unit) return true;
            return false;
        }

        // Σ цены (по ресурсу) автопризыва команды: Σ EffectiveResourceCost×count по помеченным авто.
        int AutoResourceSum(int team, Resource resource)
        {
            TeamWaveConfig cfg = Team(team);
            if (cfg == null || cfg.autoSummon == null || resource == null) return 0;
            int sum = 0;
            foreach (int id in cfg.autoSummon)
            {
                Unit u = ResolveUnitById(id);
                if (u == null) continue;
                sum += EffectiveResourceCost(team, u, resource) * WaveCountOf(cfg, u);
            }
            return sum;
        }

        // Σ лидерства ВСЕЙ волны (базовые + авто + разовые) ×count — для прогноза окон t−15/t−5.
        int WaveLeadershipForecast(int team)
        {
            TeamWaveConfig cfg = Team(team);
            if (cfg == null || leadershipResource == null) return 0;
            int sum = 0;
            if (cfg.waveUnits != null)
                foreach (WaveUnitEntry e in cfg.waveUnits)
                    if (e != null && e.role == WaveUnitRole.Basic && e.unit != null)
                        sum += EffectiveResourceCost(team, e.unit, leadershipResource) * WaveCountOf(cfg, e.unit);
            if (cfg.autoSummon != null)
                foreach (int id in cfg.autoSummon)
                {
                    Unit u = ResolveUnitById(id);
                    if (u != null) sum += EffectiveResourceCost(team, u, leadershipResource) * WaveCountOf(cfg, u);
                }
            if (cfg.oneShot != null)
                foreach (int id in cfg.oneShot.Keys)
                {
                    Unit u = ResolveUnitById(id);
                    if (u != null) sum += EffectiveResourceCost(team, u, leadershipResource) * WaveCountOf(cfg, u);
                }
            return sum;
        }

        // ======================== ДОХОД ВОЛНЫ (по уровню ГЗ) ========================

        /// <summary>
        /// Базовый доход золота команды СЕЙЧАС: значение из таблицы фракции по текущему уровню ГЗ.
        /// Единственный источник дохода (целевая модель 2026-08-21). Читателей два, оба серверные:
        /// начисление в момент призыва волны (MatchManager.Waves) и предел суммы автопризыва (TrySetAuto).
        /// Состояние не хранится — значение вычисляется по требованию, поэтому рост уровня ГЗ поднимает
        /// и доход, и предел сам собой — без подписки на смену уровня.
        /// ИНДЕКСАЦИЯ: индекс = уровень ГЗ, элемент 0 = уровень 0 — в ОТЛИЧИЕ от таблицы душ
        /// (SoulsRatePerMinute: там элемент 0 = уровень 1). Уровень выше последней записи клампится на неё.
        /// Пустая или незаданная таблица — 0 (у Орков доход 0, как и было).
        /// </summary>
        public int CurrentWaveIncome(int team)
        {
            TeamWaveConfig cfg = Team(team);
            int[] arr = cfg != null ? cfg.waveIncomeByLevel : null;
            if (arr == null || arr.Length == 0) return 0;
            int idx = Mathf.Clamp(MainBuildingLevel(team), 0, arr.Length - 1);
            return arr[idx];
        }

        // ======================== ПОМЕТКИ (сервер, идемпотентно) ========================

        /// <summary>
        /// Сервер: пометить тип на АВТОПРИЗЫВ (каждую волну). Отказ, если недоступен/базовый/уже помечен/
        /// лок активен/сумма автопризыва (цена×count) превысит базовый доход. true — успех.
        /// </summary>
        public bool TrySetAuto(int team, int unitTypeID)
        {
            if (NetworkConnectionHandler.isClient) return false; // пометки — только сервер (правило 6)
            TeamWaveConfig cfg = Team(team);
            if (cfg == null || cfg.compositionLocked) return false;
            Unit u = ResolveUnitById(unitTypeID);
            if (u == null || !IsUnitAvailable(team, u) || IsBasicUnit(cfg, u)) return false;
            if (cfg.autoSummon.Contains(unitTypeID) || cfg.oneShot.ContainsKey(unitTypeID)) return false; // «одна пометка на тип»

            int addGold = EffectiveResourceCost(team, u, goldResource) * WaveCountOf(cfg, u);
            if (AutoResourceSum(team, goldResource) + addGold > CurrentWaveIncome(team))
            {
                return false;
            }

            cfg.autoSummon.Add(unitTypeID);
            NotifyMarksChanged(team);
            return true;
        }

        /// <summary>
        /// Сервер: пометить тип на РАЗОВЫЙ призыв (одна волна) с резервом золота. Отказ, если недоступен/базовый/
        /// уже помечен/лок/не хватает золота ИЛИ лидерства. Успех → золото зарезервировано (списано в «карман»).
        /// </summary>
        public bool TrySetOneShot(int team, int unitTypeID)
        {
            if (NetworkConnectionHandler.isClient) return false;
            TeamWaveConfig cfg = Team(team);
            if (cfg == null || cfg.compositionLocked) return false;
            Unit u = ResolveUnitById(unitTypeID);
            if (u == null || !IsUnitAvailable(team, u) || IsBasicUnit(cfg, u)) return false;
            if (cfg.autoSummon.Contains(unitTypeID) || cfg.oneShot.ContainsKey(unitTypeID)) return false;

            int gold = EffectiveResourceCost(team, u, goldResource) * WaveCountOf(cfg, u);
            int lead = EffectiveResourceCost(team, u, leadershipResource) * WaveCountOf(cfg, u);

            // Золото: хватает на резерв.
            if (goldResource != null && gold > 0 &&
                !GameResources.Instance.CheckAmount(cfg.ownerPlayer, new ResourceWrapper(goldResource, gold)))
            {
                return false;
            }

            // Лидерство: прогноз всей волны + этот разовый ≤ кап (штатная семантика limited: занято живыми + X ≤ лимит).
            if (leadershipResource != null && lead > 0 &&
                !GameResources.Instance.CheckAmount(cfg.ownerPlayer, new ResourceWrapper(leadershipResource, WaveLeadershipForecast(team) + lead)))
            {
                return false;
            }

            // Резерв = фактическое списание золота в «карман» (заморозка): в кошельке денег нет → потратить нельзя.
            if (goldResource != null && gold > 0)
                GameResources.Instance.ChangeAmount(cfg.ownerPlayer, new ResourceWrapper(goldResource, gold), 1, true, true);
            cfg.oneShot[unitTypeID] = gold;
            NotifyMarksChanged(team);
            return true;
        }

        /// <summary>
        /// Сервер: снять любую пометку типа. Авто → снять. Разовый → снять + разморозить (вернуть зарезервированное золото).
        /// Отказ при активном локе. true — если что-то сняли.
        /// </summary>
        public bool TryClearMark(int team, int unitTypeID)
        {
            if (NetworkConnectionHandler.isClient) return false;
            TeamWaveConfig cfg = Team(team);
            if (cfg == null || cfg.compositionLocked) return false;

            if (cfg.autoSummon.Remove(unitTypeID)) { NotifyMarksChanged(team); return true; }

            if (cfg.oneShot.TryGetValue(unitTypeID, out int reserved))
            {
                cfg.oneShot.Remove(unitTypeID);
                if (goldResource != null && reserved > 0)
                    GameResources.Instance.ChangeAmount(cfg.ownerPlayer, new ResourceWrapper(goldResource, reserved), 1, false, true); // разморозка (возврат)
                NotifyMarksChanged(team);
                return true;
            }
            return false;
        }

        /// <summary>Состояние пометки типа для UI: 0 — нет, 1 — автопризыв, 2 — разовый.</summary>
        public int MarkState(int team, Unit prefab)
        {
            TeamWaveConfig cfg = Team(team);
            if (cfg == null || prefab == null) return 0;
            int id = prefab.unitTypeID;
            if (cfg.autoSummon != null && cfg.autoSummon.Contains(id)) return 1;
            if (cfg.oneShot != null && cfg.oneShot.ContainsKey(id)) return 2;
            return 0;
        }

        // Сервер: снять пометки типов, которых больше нет в доступных (после RecomputeUnlockedContent).
        // Авто — просто снять; разовый — снять + вернуть резерв. Набор доступных пуст → не чистим (бэк-совместимость).
        void PruneMarks(int team)
        {
            TeamWaveConfig cfg = Team(team);
            if (cfg == null) return;
            List<Unit> avail = effectiveAvailableUnits[team];
            if (avail == null || avail.Count == 0) return;

            bool changed = false;

            List<int> autoGone = new List<int>();
            foreach (int id in cfg.autoSummon)
            {
                Unit u = ResolveUnitById(id);
                if (u == null || !avail.Contains(u)) autoGone.Add(id);
            }
            foreach (int id in autoGone) { cfg.autoSummon.Remove(id); changed = true; }

            List<int> oneGone = new List<int>();
            foreach (int id in cfg.oneShot.Keys)
            {
                Unit u = ResolveUnitById(id);
                if (u == null || !avail.Contains(u)) oneGone.Add(id);
            }
            foreach (int id in oneGone)
            {
                int reserved = cfg.oneShot[id];
                cfg.oneShot.Remove(id);
                if (goldResource != null && reserved > 0)
                    GameResources.Instance.ChangeAmount(cfg.ownerPlayer, new ResourceWrapper(goldResource, reserved), 1, false, true);
                changed = true;
            }

            if (changed) NotifyMarksChanged(team);
        }

        // ======================== СИНХРОНИЗАЦИЯ ПОМЕТОК ========================

        // Уведомить UI (событие) + разослать пометки клиентам. Единая точка после любой правки пометок.
        void NotifyMarksChanged(int team)
        {
            OnWaveMarksChanged?.Invoke(team);
            BroadcastMarks(team);
        }

        // Сервер → клиенты: текущие пометки команды (авто-типы + разовые-типы) для отрисовки выделения.
        void BroadcastMarks(int team)
        {
            if (NetworkConnectionHandler.isClient || NetworkDataSync.Instance == null) return;
            TeamWaveConfig cfg = Team(team);
            if (cfg == null) return;

            int[] autoIds = new int[cfg.autoSummon.Count];
            cfg.autoSummon.CopyTo(autoIds);
            int[] oneIds = new int[cfg.oneShot.Count];
            cfg.oneShot.Keys.CopyTo(oneIds, 0);
            NetworkDataSync.Instance.WaveMarksSend(team, autoIds, oneIds);
        }

        /// <summary>
        /// Клиент: применить присланные сервером пометки команды. Резерв золота на клиенте не нужен (только факт
        /// пометки для UI) → oneShot заполняется нулями. На сервере не вызывается.
        /// </summary>
        public void ApplyMarks(int team, int[] autoIds, int[] oneShotIds)
        {
            TeamWaveConfig cfg = Team(team);
            if (cfg == null) return;
            cfg.autoSummon = new HashSet<int>(autoIds ?? Array.Empty<int>());
            cfg.oneShot = new Dictionary<int, int>();
            if (oneShotIds != null)
                foreach (int id in oneShotIds) cfg.oneShot[id] = 0;
            OnWaveMarksChanged?.Invoke(team);
        }

        /// <summary>Клиент: поднять событие предупреждения о превышении лидерства (из WaveOverflowWarnClientRpc).</summary>
        public void RaiseOverflowWarningClient(int team) => OnWaveOverflowWarning?.Invoke(team);
    }
}
