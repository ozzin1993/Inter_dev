using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============================= МОГИЛКИ (партиал MatchManager) ==
    // Серверо-авторитетно (правило 6; ADR-002 Вариант А). Сервер ловит смерть помеченных юнитов (leavesGrave),
    // собирает GraveData, ведёт реестр активных могилок (единый источник, правило 5) и спавнит визуал-префаб
    // (gravePrefab расы владельца). Время жизни — свой отсчёт по GameManager.Tick (как LifetimeUnit).
    // Клиентский визуал — партиалом NetworkDataSync.Graves (шаг 5). Ассет StrategyCore не трогаем (правило 1).
    public partial class MatchManager
    {
        // Runtime-запись активной могилки (сервер): данные + бухгалтерия жизни/визуала.
        class GraveRecord
        {
            public GraveData data;      // информация об умершем (в реестр)
            public int graveId;         // адрес для сетевого деспавна визуала (шаг 5)
            public float remaining;     // остаток времени жизни, сек
            public GameObject visual;   // локальный визуал на этом пире (хост); null — не спавнили
        }

        // Реестр активных могилок (единый источник, правило 5). Заполняется только на сервере.
        readonly List<GraveRecord> graves = new List<GraveRecord>();
        int nextGraveId = 1;            // возрастающий id могилки
        bool gravesTickHooked = false;

        // Клиентские визуалы могилок (graveId → объект). Заполняется по RPC на клиенте; сервер использует graves.
        readonly Dictionary<int, GameObject> clientGraves = new Dictionary<int, GameObject>();

        // Подписка (вызывается из Awake MatchManager, рядом с HeroWire). isClient не проверяем в Awake
        // (может быть ещё не определён) — как HeroWire; серверность гарантируют рантайм-хендлеры.
        void GravesWire()
        {
            OnUnitSpawned += HandleUnitSpawnedForGrave;                     // подписка на смерть — при спавне юнита
            if (SlotManager.Instance != null) SlotManager.Instance.OnGameStart += GravesHookTick;
        }

        // Отписка (вызывается из OnDestroy MatchManager, рядом с HeroUnwire).
        void GravesUnwire()
        {
            OnUnitSpawned -= HandleUnitSpawnedForGrave;
            if (SlotManager.Instance != null) SlotManager.Instance.OnGameStart -= GravesHookTick;
            if (gravesTickHooked && GameManager.Instance != null) { GameManager.Instance.Tick -= GravesTick; gravesTickHooked = false; }
        }

        // Тик времени жизни подключаем по старту игры (GameManager.Instance к Awake ещё может не быть). Только сервер.
        void GravesHookTick()
        {
            if (NetworkConnectionHandler.isClient) return;                 // отсчёт времени — только сервер (правило 6)
            if (GameManager.Instance != null && !gravesTickHooked) { GameManager.Instance.Tick += GravesTick; gravesTickHooked = true; }
        }

        // При спавне юнита (сервер): если он помечен — подписаться на его смерть.
        void HandleUnitSpawnedForGrave(int teamIndex, Unit unit)
        {
            if (NetworkConnectionHandler.isClient) return;                 // OnUnitSpawned серверный; страховка
            if (unit != null && unit.leavesGrave) unit.OnDie += HandleUnitDeathForGrave;
        }

        // Смерть помеченного юнита (сервер): создать могилку в точке смерти.
        void HandleUnitDeathForGrave(Unit unit, int killerPlayer, Unit killerUnit, bool rewards)
        {
            if (NetworkConnectionHandler.isClient) return;                 // спавн могилки — только сервер (правило 6)
            if (unit == null || !unit.leavesGrave) return;

            FactionConfig faction = ResolveFaction(unit.owner);
            if (faction == null || faction.gravePrefab == null) return;    // префаб расы не задан → могилки нет

            GraveData data = new GraveData
            {
                unitTypeID = unit.unitTypeID,
                owner = unit.owner,
                team = unit.team,
                unitCategory = unit.unitCategory,
                tier = unit.tier,
                position = unit.transform.position
            };

            GraveRecord rec = new GraveRecord { data = data, graveId = nextGraveId++, remaining = faction.graveLifetime };
            graves.Add(rec);

            rec.visual = SpawnGraveVisual(data, faction.gravePrefab);      // визуал на этом пире (хост)

            // Рассылка визуала клиентам (ADR-002 Вариант А). SendTo.NotServer — хост не дублируется.
            if (NetworkDataSync.Instance != null)
                NetworkDataSync.Instance.GraveSpawnClientRpc(rec.graveId, data.unitTypeID, data.owner, data.team, (int)data.unitCategory, data.tier, data.position);
        }

        // Локальный спавн визуал-префаба могилки. Обычный Instantiate (не Unit, не NGO) — не карвит навмеш.
        GameObject SpawnGraveVisual(GraveData data, GameObject prefab)
        {
            if (prefab == null) return null;
            GameObject go = Instantiate(prefab, data.position, Quaternion.identity);
            GraveMarker marker = go.GetComponent<GraveMarker>();
            if (marker != null) marker.SetData(data);
            return go;
        }

        // Тик времени жизни (сервер): истёкшие могилки — снять. currentDeltaTime валиден внутри Tick (сброс после Invoke).
        void GravesTick()
        {
            if (graves.Count == 0) return;
            float dt = GameManager.Instance != null ? GameManager.Instance.currentDeltaTime : 0f;
            for (int i = graves.Count - 1; i >= 0; i--)
            {
                graves[i].remaining -= dt;
                if (graves[i].remaining <= 0f) RemoveGraveAt(i);
            }
        }

        // Снять могилку (сервер): клиентам деспавн + локальный визуал + запись реестра.
        void RemoveGraveAt(int index)
        {
            if (index < 0 || index >= graves.Count) return;
            if (NetworkDataSync.Instance != null) NetworkDataSync.Instance.GraveDespawnClientRpc(graves[index].graveId);
            if (graves[index].visual != null) Destroy(graves[index].visual);
            graves.RemoveAt(index);
        }

        // Клиент: заспавнить визуал могилки по данным сервера (RPC). Реестр/время жизни — на сервере.
        public void ClientSpawnGrave(int graveId, GraveData data)
        {
            if (data == null) return;
            FactionConfig faction = ResolveFaction(data.owner);
            if (faction == null || faction.gravePrefab == null) return;
            GameObject go = SpawnGraveVisual(data, faction.gravePrefab);
            if (go != null) clientGraves[graveId] = go;
        }

        // Клиент: убрать визуал могилки по id (RPC).
        public void ClientDespawnGrave(int graveId)
        {
            if (clientGraves.TryGetValue(graveId, out GameObject go))
            {
                if (go != null) Destroy(go);
                clientGraves.Remove(graveId);
            }
        }
    }
}
