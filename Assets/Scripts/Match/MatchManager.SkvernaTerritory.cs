using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============================= ТЕРРИТОРИЯ СКВЕРНЫ (B30, партиал MatchManager) ==
    // Зона Скверны Нежити = набор кругов вокруг ИСТОЧНИКОВ (ГЗ + точки линии во владении команды-Нежити).
    // Радиус каждого источника растёт со временем (сервер, тик), кап — по конфигу; множитель «Быстрая Гниль»
    // (эффект N5) ускоряет рост. Серверо-авторитетно (правило 6): реестр и рост — только на сервере. Клиенту
    // IsOnSkverna не нужен (потребители серверные); для визуала — событие OnSkvernaChanged (сам визуал/декаль —
    // отложенная точка, здесь НЕ делается). Чистая математика расстояний (XZ), без коллайдеров/физики (§9).
    // Гейт активности — soulsResource расы (как система Душ): у не-Нежити источников нет, зона пуста.
    public partial class MatchManager
    {
        [Header("Территория Скверны (B30)")]
        [SerializeField, Tooltip("Шаг серверного тика роста радиуса Скверны, сек. Мельче — плавнее рост. " +
            "Значение — placeholder, задать в Inspector. [БАЛАНС — Влад]")]
        float skvernaTickStep = 0.5f;

        // Текущий радиус каждого источника команды (0=A,1=B). Ключ — Transform источника (ГЗ/точка).
        // Источник появляется (ГЗ / точка захвачена Нежитью) → стартовый радиус; исчезает (точка потеряна) → удаляется.
        readonly Dictionary<Transform, float>[] skvernaSourceRadius = new Dictionary<Transform, float>[2]
            { new Dictionary<Transform, float>(), new Dictionary<Transform, float>() };

        // Множитель скорости роста на команду («Быстрая Гниль» — эффект N5 ставит ×2). Дефолт 1 (без ускорения).
        readonly float[] skvernaGrowthMultiplier = new float[2] { 1f, 1f };

        // Переиспользуемые буферы (без аллокаций на тик).
        readonly List<Transform> skvernaSourceBuffer = new List<Transform>();
        readonly List<Transform> skvernaRemoveBuffer = new List<Transform>();

        /// <summary>Территория Скверны команды изменилась (0=A,1=B) — точка подключения клиентского визуала (декаль/проектор). Визуал/синк отложены.</summary>
        public event Action<int> OnSkvernaChanged;

        /// <summary>N5: множитель скорости роста Скверны команды (тех «Быстрая Гниль» → 2). В N4 не вызывается (дефолт 1).</summary>
        public void SetSkvernaGrowthMultiplier(int team, float multiplier)
        {
            if (team != 0 && team != 1) return;
            skvernaGrowthMultiplier[team] = Mathf.Max(0f, multiplier);
        }

        // ======================== РОСТ (сервер, тик) ========================

        /// <summary>Серверная корутина роста радиуса Скверны. Ждёт старта матча (как SoulsGenerationLoop), затем
        /// каждые skvernaTickStep секунд обновляет источники и растит радиусы обеим командам. Для не-Нежити — no-op.</summary>
        IEnumerator SkvernaGrowthLoop()
        {
            yield return new WaitUntil(() => SlotManager.Instance != null && SlotManager.Instance.gameOn);

            WaitForSeconds wait = new WaitForSeconds(skvernaTickStep > 0f ? skvernaTickStep : 0.5f);
            while (true)
            {
                yield return wait;
                TickSkverna(0, teamA);
                TickSkverna(1, teamB);
            }
        }

        void TickSkverna(int team, TeamWaveConfig cfg)
        {
            if (NetworkConnectionHandler.isClient) return;        // страховка (корутина серверная)
            if (!SoulsActive(cfg)) return;                        // Скверна — механика Нежити (гейт по soulsResource)

            FactionConfig faction = ResolveFaction(cfg.ownerPlayer);
            if (faction == null) return;

            float step   = skvernaTickStep > 0f ? skvernaTickStep : 0.5f;
            float start  = faction.skvernaStartRadius;
            float growth = faction.skvernaGrowthPerSecond * skvernaGrowthMultiplier[team];
            float cap    = faction.skvernaMaxRadius;              // 0 = без капа

            // Актуальный набор источников команды (ГЗ + точки во владении Нежити).
            CollectSkvernaSources(team, cfg, skvernaSourceBuffer);
            Dictionary<Transform, float> radii = skvernaSourceRadius[team];

            // Убрать исчезнувшие источники (потерянные точки).
            skvernaRemoveBuffer.Clear();
            foreach (var kv in radii)
                if (kv.Key == null || !skvernaSourceBuffer.Contains(kv.Key)) skvernaRemoveBuffer.Add(kv.Key);
            for (int i = 0; i < skvernaRemoveBuffer.Count; i++) radii.Remove(skvernaRemoveBuffer[i]);

            // Добавить новые (стартовый радиус) и вырастить существующие (до капа).
            for (int i = 0; i < skvernaSourceBuffer.Count; i++)
            {
                Transform src = skvernaSourceBuffer[i];
                if (src == null) continue;
                float r = radii.TryGetValue(src, out float cur) ? cur : start;
                r += growth * step;
                if (cap > 0f && r > cap) r = cap;
                radii[src] = r;
            }

            OnSkvernaChanged?.Invoke(team);   // визуал — отложенная точка (в N4 подписчиков нет)
        }

        // Источники Скверны команды: ГЗ (mainBuilding) + точки линии во владении команды (Defence/Centre из
        // rebuildablePoints). Замки-как-точки навигации (Lane.points приватен) не перебираем — ГЗ покрывает замок команды.
        void CollectSkvernaSources(int team, TeamWaveConfig cfg, List<Transform> into)
        {
            into.Clear();
            if (cfg == null) return;

            if (cfg.mainBuilding != null) into.Add(cfg.mainBuilding.transform);

            int teamValue = (SlotManager.Instance != null && cfg.ownerPlayer >= 0
                             && cfg.ownerPlayer < SlotManager.Instance.playerTeam.Length)
                            ? SlotManager.Instance.playerTeam[cfg.ownerPlayer] : -1;

            if (rebuildablePoints != null && teamValue >= 0)
                foreach (PointTowerConfig pc in rebuildablePoints)
                    if (pc != null && pc.point != null && pc.point.CurrentTeam == teamValue)
                        into.Add(pc.point.transform);
        }

        // ======================== ЗАПРОС ТЕРРИТОРИИ ========================

        /// <summary>На территории Скверны ли позиция (в зоне любого источника любой команды). Математика XZ, без физики.</summary>
        public bool IsOnSkverna(Vector3 position)
        {
            for (int team = 0; team < 2; team++)
            {
                Dictionary<Transform, float> radii = skvernaSourceRadius[team];
                foreach (var kv in radii)
                {
                    if (kv.Key == null || kv.Value <= 0f) continue;
                    float dx = kv.Key.position.x - position.x;
                    float dz = kv.Key.position.z - position.z;
                    if (dx * dx + dz * dz <= kv.Value * kv.Value) return true;
                }
            }
            return false;
        }

        /// <summary>На территории Скверны ли юнит.</summary>
        public bool IsOnSkverna(Unit unit) => unit != null && IsOnSkverna(unit.transform.position);
    }
}
