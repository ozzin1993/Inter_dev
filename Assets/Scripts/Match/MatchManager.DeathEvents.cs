using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============================= ХАБ СМЕРТЕЙ (партиал MatchManager) ==
    // Серверо-авторитетный центральный диспетчер смертей (правило 5). Подписывает Unit.OnDie КАЖДОГО нашего
    // юнита ОДИН раз при спавне и ретранслирует смерть серверным потребителям. Покрытие юнитов = там, где
    // инвочится OnUnitSpawned (волна + герой + призванные; башни — нет, у них свой путь HandleTowerDie).
    // Потребитель в этом же партиале: B3 (килл-баунти по killerPlayer). Клич при добивании (был B4) с 2026-08-17
    // живёт в оси «реакции» конструктора пассивок — она подписывается на OnUnitDeathServer как обычный слушатель,
    // и отдельного реестра «криеров» здесь больше нет (правило 5: одна точка входа вместо двух).
    // Ассет StrategyCore не трогаем (правило 1).
    public partial class MatchManager
    {
        // Событие смерти для серверных потребителей. Сигнатура повторяет Unit.OnDie (Unit.cs:344):
        // (жертва, killerPlayer, killerUnit, rewards). Инвочится только на сервере.
        public event Action<Unit, int, Unit, bool> OnUnitDeathServer;

        // ---- B3: килл-баунти -------------------------------------------------------------------------------
        [Header("Килл-баунти (кирпич B3)")]
        [SerializeField, Tooltip("Золото команде добившего за убийство вражеского ЮНИТА (0 = выкл). [БАЛАНС — Влад]. " +
            "В Фазе 4 включается техом «Грабёж». Структуры до хаба не доходят (покрытие §6.1) → платится только за юнитов. " +
            "Начисляется ПОВЕРХ штатного resourceReward жертвы.")]
        int bountyGold = 0;

        // ---- Временная диагностика десинка смерти (2026-07-25) ------------------------------------------
        [Header("Диагностика (временно)")]
        [SerializeField, Tooltip("Логировать на сервере случаи, когда в момент рассылки смерти убийца уже мёртв или снят с сетевого реестра. " +
            "Нужно, чтобы подтвердить причину ошибки клиента «Desync! KILLER unit netID:...». Выключить после разбора.")]
        bool diagKillerDesync = false;

        // ---- Проводка (Awake/OnDestroy) --------------------------------------------------------------------
        // isClient в Awake не проверяем (может быть ещё не определён) — как GravesWire/HeroWire; серверность
        // гарантируют рантайм-хендлеры (правило 6).
        void DeathEventsWire()
        {
            OnUnitSpawned += HandleUnitSpawnedForHub;   // при спавне юнита — подписка на его смерть
            OnUnitDeathServer += HandleBountyOnDeath;   // B3 — потребитель события
        }

        void DeathEventsUnwire()
        {
            OnUnitSpawned -= HandleUnitSpawnedForHub;
            OnUnitDeathServer -= HandleBountyOnDeath;
        }

        // При спавне юнита (сервер): подписать его смерть на центральный хендлер РОВНО один раз.
        // Идемпотентность (-= перед +=): повторный OnUnitSpawned для юнита не задвоит подписку.
        void HandleUnitSpawnedForHub(int teamIndex, Unit unit)
        {
            if (NetworkConnectionHandler.isClient) return;      // OnUnitSpawned серверный; страховка (правило 6)
            if (unit == null) return;
            unit.OnDie -= HandleUnitDeathForHub;
            unit.OnDie += HandleUnitDeathForHub;
        }

        // Смерть юнита (сервер): диспетч потребителям. Гейт isClient — OnDie летит
        // и на клиенте (DieClientRpc, calledByServer:false), а начисления/эффекты — только на сервере (правило 6).
        void HandleUnitDeathForHub(Unit unit, int killerPlayer, Unit killerUnit, bool rewards)
        {
            if (NetworkConnectionHandler.isClient) return;

            // Диагностика (временно): Unit.Die инвочит OnDie ДО рассылки DieTriggerSend и ДО RemoveNetID,
            // поэтому здесь видно ровно то состояние убийцы, с которым сервер отправит смерть клиентам.
            if (diagKillerDesync && killerUnit != null)
            {
                bool inRegistry = SlotManager.instance.unitNetID.TryGetValue(killerUnit.netID, out Unit registered) && registered == killerUnit;
                if (killerUnit.dead || !inRegistry)
                {
                    Debug.LogWarning($"[Диагностика смерти] Кадр {Time.frameCount}: жертва netID:{unit.netID} ({unit.name}) — " +
                                     $"убийца netID:{killerUnit.netID} ({killerUnit.name}) уже недоступен клиенту " +
                                     $"(dead={killerUnit.dead}, в реестре={inRegistry}). Клиент получит ошибку KILLER-десинка.");
                }
            }

            OnUnitDeathServer?.Invoke(unit, killerPlayer, killerUnit, rewards);
        }

        // ---- B3: начисление баунти (потребитель OnUnitDeathServer) ------------------------------------------
        void HandleBountyOnDeath(Unit victim, int killerPlayer, Unit killerUnit, bool rewards)
        {
            if (NetworkConnectionHandler.isClient) return;              // страховка (событие уже серверное)
            if (bountyGold <= 0) return;                              // выключено
            if (killerPlayer < 0 || victim == null) return;            // нет киллера (лайфтайм/суицид) → без баунти
            int killerTeam = TeamIndexOfOwner(killerPlayer);
            if (killerTeam < 0) return;                                // киллер вне команд (нейтрал/среда)
            if (killerTeam == victim.team) return;                     // friendly-fire → без баунти

            Resource gold = GoldResource;
            if (gold == null) return;
            // +золото команде киллера: decrease:false = начислить; calledByServer:true = серверо-авторит. + авто-синк.
            GameResources.instance.ChangeAmount(killerPlayer, new ResourceWrapper(gold, bountyGold), 1, false, true);
        }
    }
}
