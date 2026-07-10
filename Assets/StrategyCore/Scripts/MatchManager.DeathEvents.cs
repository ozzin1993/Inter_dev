using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============================= ХАБ СМЕРТЕЙ (партиал MatchManager) ==
    // Серверо-авторитетный центральный диспетчер смертей (правило 5). Подписывает Unit.OnDie КАЖДОГО нашего
    // юнита ОДИН раз при спавне и ретранслирует смерть серверным потребителям. Покрытие юнитов = там, где
    // инвочится OnUnitSpawned (волна + герой + призванные; башни — нет, у них свой путь HandleTowerDie).
    // Потребители в этом же партиале: B3 (килл-баунти по killerPlayer) и B4 (клич при добивании по killerUnit).
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

        // ---- B4: реестр «криеров» (клич при добивании) -----------------------------------------------------
        struct KillCrier { public OnKillCryPassive passive; public int level; }
        // killerUnit → его пассивка клича + уровень. Наполняется OnKillCryPassive.Unlock (сервер). Чистится на
        // смерти носителя (см. HandleUnitDeathForHub) — Unit.Die не зовёт Lock (ревью §4.1).
        readonly Dictionary<Unit, KillCrier> killCriers = new Dictionary<Unit, KillCrier>();

        /// <summary>Зарегистрировать носителя как «криера» (B4). Сервер-онли. Зовётся из OnKillCryPassive.Unlock.</summary>
        public void RegisterKiller(Unit unit, OnKillCryPassive cry, int level)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (unit == null || cry == null) return;
            killCriers[unit] = new KillCrier { passive = cry, level = level };
        }

        /// <summary>Снять регистрацию «криера» (B4). Зовётся из OnKillCryPassive.Lock.</summary>
        public void UnregisterKiller(Unit unit)
        {
            if (unit == null) return;
            killCriers.Remove(unit);
        }

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
            killCriers.Clear();
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

        // Смерть юнита (сервер): диспетч потребителям + клич (B4) + чистка реестра. Гейт isClient — OnDie летит
        // и на клиенте (DieClientRpc, calledByServer:false), а начисления/эффекты — только на сервере (правило 6).
        void HandleUnitDeathForHub(Unit unit, int killerPlayer, Unit killerUnit, bool rewards)
        {
            if (NetworkConnectionHandler.isClient) return;

            OnUnitDeathServer?.Invoke(unit, killerPlayer, killerUnit, rewards);

            // B4: если добивший — зарегистрированный «криер», клич союзникам в радиусе.
            if (killerUnit != null && killCriers.TryGetValue(killerUnit, out KillCrier crier) && crier.passive != null)
                crier.passive.TriggerCry(killerUnit, crier.level);

            // Чистка реестра: умерший юнит больше не «криер» (Unit.Die не зовёт Lock — ревью §4.1).
            killCriers.Remove(unit);
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
