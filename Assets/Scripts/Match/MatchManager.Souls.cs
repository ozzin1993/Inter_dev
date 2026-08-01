using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============================= РЕСУРС «ДУШИ» (партиал MatchManager) ==
    // Ресурс Нежити «Души» (N1): (а) пассивная серверная генерация по уровню ГЗ; (б) начисление за убийства
    // по тиру жертвы (потребитель хаба смертей B3); (в) единая точка AddSouls(team, amount, source, victim)
    // с реестром подключаемых модификаторов (техи веток N4/N5 регистрируют их — здесь только интерфейс,
    // без потребителей). Серверо-авторитетно (правило 6): начисление только на сервере, синк — штатный
    // GameResources.ChangeAmount (ResourceSendAdd). Ассет StrategyCore не трогаем (правило 1). Один партиал
    // (правило 5), всё в Inspector (правило 3).
    //
    // Кап банка держим здесь: для Standard-ресурса ядро (GameResources.ChangeAmount) НЕ клампит по maxLimit
    // (кламп есть только у Limited в ChangeLimit) — поэтому AddSouls читает soulsResource.maxLimit и не даёт
    // превысить кап (по N1 §5 «иначе клампить в AddSouls»).
    public partial class MatchManager
    {
        /// <summary>Источник начисления душ (для модификаторов N4/N5 и диагностики).</summary>
        public enum SoulSource { Generation, Kill, Ability }

        /// <summary>
        /// Модификатор начисления душ. Регистрируется техами веток (N4/N5), применяется в AddSouls к базовой
        /// сумме до клампа по капу. Контекст: команда-получатель, источник, жертва (null для генерации),
        /// текущая сумма. Возвращает изменённую сумму (например: ×2-шанс, +50% за героев, +2 за своих, ×2-всё).
        /// </summary>
        public delegate int SoulsModifier(int team, SoulSource source, Unit victim, int amount);
        readonly List<SoulsModifier> soulsModifiers = new List<SoulsModifier>();

        /// <summary>Зарегистрировать модификатор начисления душ (идемпотентно). Зовут техи веток (N4/N5).</summary>
        public void RegisterSoulsModifier(SoulsModifier mod)
        {
            if (mod != null && !soulsModifiers.Contains(mod)) soulsModifiers.Add(mod);
        }

        /// <summary>Снять модификатор начисления душ.</summary>
        public void UnregisterSoulsModifier(SoulsModifier mod) => soulsModifiers.Remove(mod);

        /// <summary>Число душ команды изменилось после начисления (параметр — индекс команды 0=A, 1=B). Для UI/потребителей.</summary>
        public event Action<int> OnSoulsChanged;

        [Header("Ресурс «Души» — генерация")]
        [SerializeField, Tooltip("Шаг серверного тика генерации душ, сек. Тик набирает время и начисляет 1 душу, " +
            "когда накоплен интервал уровня ГЗ (60 / скорость-в-минуту). Мельче шаг — точнее темп по дизайн-доку " +
            "(ур.1 → 1 душа в 4 c). Значение — placeholder, задать в Inspector. [БАЛАНС — Влад]")]
        float soulsTickStep = 0.5f;

        // Накопитель времени генерации на команду (сек). Копится, пока система активна и скорость > 0;
        // при достижении интервала уровня ГЗ конвертируется в +1 душу (может дать несколько за тик).
        readonly float[] soulsGenAccum = new float[2];

        // ---- Проводка (из Awake/OnDestroy MatchManager.cs) ----
        // isClient в Awake не проверяем (может быть ещё не определён) — как DeathEventsWire/GravesWire;
        // серверность гарантируют рантайм-обработчики (правило 6).
        void SoulsWire()
        {
            OnUnitDeathServer += HandleSoulsOnKill; // начисление за убийства — потребитель хаба смертей (B3)
        }

        void SoulsUnwire()
        {
            OnUnitDeathServer -= HandleSoulsOnKill;
            soulsModifiers.Clear();
        }

        // Активна ли система душ у команды: у расы задан ресурс «Души» (Нежить). У Людей/Орков/Союза — null → неактивна.
        bool SoulsActive(TeamWaveConfig cfg) => cfg != null && cfg.soulsResource != null;

        // ======================== ГЕНЕРАЦИЯ ========================

        /// <summary>
        /// Серверная корутина генерации душ. Ждёт старта матча (как WaveLoop/PassiveIncomeLoop), затем каждые
        /// soulsTickStep секунд начисляет накопленные души обеим командам. Запускается из Start() (только сервер).
        /// Для неактивных рас (нет soulsResource) — тик пустой.
        /// </summary>
        IEnumerator SoulsGenerationLoop()
        {
            yield return new WaitUntil(() => SlotManager.instance != null && SlotManager.instance.gameOn);

            WaitForSeconds wait = new WaitForSeconds(soulsTickStep > 0f ? soulsTickStep : 0.5f);
            while (true)
            {
                yield return wait;
                TickSoulsGeneration(0, teamA);
                TickSoulsGeneration(1, teamB);
            }
        }

        // Тик генерации для одной команды: копит время, конвертирует в целые души по интервалу уровня ГЗ.
        void TickSoulsGeneration(int team, TeamWaveConfig cfg)
        {
            if (NetworkConnectionHandler.isClient) return;         // страховка (корутина серверная)
            if (!SoulsActive(cfg)) return;                         // раса без душ — генерации нет

            float rate = SoulsRatePerMinute(cfg, MainBuildingLevel(team)); // душ в минуту по уровню ГЗ
            if (rate <= 0f) return;                                // уровень без генерации
            float interval = 60f / rate;                           // секунд на 1 душу (ур.1 15/мин → 4 c)

            soulsGenAccum[team] += (soulsTickStep > 0f ? soulsTickStep : 0.5f);
            int grant = 0;
            while (soulsGenAccum[team] >= interval) { soulsGenAccum[team] -= interval; grant++; }
            if (grant > 0) AddSouls(team, grant, SoulSource.Generation, null);
        }

        // Скорость генерации (душ/мин) для уровня ГЗ (1-based) из конфига расы. Индекс клампится в границы массива.
        float SoulsRatePerMinute(TeamWaveConfig cfg, int mbLevel)
        {
            float[] arr = cfg != null ? cfg.soulsPerMinuteByMbLevel : null;
            if (arr == null || arr.Length == 0) return 0f;
            int idx = Mathf.Clamp(mbLevel - 1, 0, arr.Length - 1);
            return arr[idx];
        }

        // ======================== НАЧИСЛЕНИЕ ЗА УБИЙСТВА ========================

        // Потребитель хаба смертей (OnUnitDeathServer): убил вражеского юнита игрок команды-Нежити → +души по тиру.
        // Гейт isClient — событие уже серверное (страховка). Смерть без киллера (лайфтайм/чума/суицид) не платит (§9 дефолт).
        void HandleSoulsOnKill(Unit victim, int killerPlayer, Unit killerUnit, bool rewards)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (victim == null || killerPlayer < 0) return;        // нет киллера-игрока → без душ
            int killerTeam = TeamIndexOfOwner(killerPlayer);
            if (killerTeam < 0) return;                            // киллер вне команд (нейтрал/среда)
            if (killerTeam == victim.team) return;                 // friendly-fire → без душ
            TeamWaveConfig cfg = Team(killerTeam);
            if (!SoulsActive(cfg)) return;                         // платит только раса с ресурсом душ (Нежить)

            int tier = Mathf.Max(1, victim.tier);                  // маппинг по Unit.tier; «Тир 5 / Гибриды» = tier 5 (§9 пометка)
            int baseAmount = SoulsForTier(cfg, tier);
            if (baseAmount <= 0) return;
            AddSouls(killerTeam, baseAmount, SoulSource.Kill, victim);
        }

        // Базовые души за тир жертвы (1-based) из конфига расы. Индекс клампится в границы массива.
        int SoulsForTier(TeamWaveConfig cfg, int tier)
        {
            int[] arr = cfg != null ? cfg.soulsPerTier : null;
            if (arr == null || arr.Length == 0) return 0;
            int idx = Mathf.Clamp(tier - 1, 0, arr.Length - 1);
            return arr[idx];
        }

        // ======================== ЕДИНАЯ ТОЧКА НАЧИСЛЕНИЯ ========================

        /// <summary>
        /// Единая точка начисления душ команде (сервер-онли). Применяет зарегистрированные модификаторы к базовой
        /// сумме, клампит по капу банка (soulsResource.maxLimit; 0 = без капа), начисляет штатным
        /// GameResources.ChangeAmount (Standard: decrease=false добавляет; calledByServer=true — серверо-авторит.
        /// + авто-синк клиентам) и поднимает OnSoulsChanged. Потребители трат (N4/N6/N8/N9) списывают души
        /// штатной оплатой resourceCost, не через этот метод.
        /// </summary>
        public void AddSouls(int team, int baseAmount, SoulSource source, Unit victim = null)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (team < 0 || team > 1) return;
            TeamWaveConfig cfg = Team(team);
            if (!SoulsActive(cfg)) return;
            if (GameResources.instance == null) return;

            // Модификаторы веток (N4/N5): каждый может изменить сумму. Пустой реестр → сумма без изменений.
            int amount = baseAmount;
            for (int i = 0; i < soulsModifiers.Count; i++)
            {
                SoulsModifier m = soulsModifiers[i];
                if (m != null) amount = m(team, source, victim, amount);
            }
            if (amount <= 0) return;

            int owner = cfg.ownerPlayer;
            ResourceWrapper probe = new ResourceWrapper(cfg.soulsResource, amount);
            int rid = GameResources.instance.GetResourceID(probe);
            if (rid < 0) return;                                   // «Души» не зарегистрированы в GameResources сцены — no-op

            int[] pool = GameResources.instance.playerResources;
            int flatIdx = rid + owner * GameResources.instance.gameResources.Length;
            if (pool == null || flatIdx < 0 || flatIdx >= pool.Length) return;

            // Кламп по капу банка (Standard-ресурс ядро не клампит — держим кап здесь; N1 §5).
            int cap = cfg.soulsResource.maxLimit;                  // 0 = безлимитно
            if (cap > 0)
            {
                int headroom = cap - pool[flatIdx];
                if (headroom <= 0) return;                         // банк полон
                if (amount > headroom) amount = headroom;
            }

            GameResources.instance.ChangeAmount(owner, new ResourceWrapper(cfg.soulsResource, amount), 1, false, true);
            OnSoulsChanged?.Invoke(team);
        }
    }
}
