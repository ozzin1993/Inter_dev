using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ==== КОНСТРУКТОР ПАССИВКИ: БЛОК 9 — ШКАЛА НОСИТЕЛЯ (правило 22 — партиал по фиче) ====
    // Шкала (gauge) — ресурс, копящийся от попаданий, убийств и таймера боя. Тратится умением
    // (блок 20 умения, SkillGaugeBlock). Логика целиком серверная (правило 6): клиент получает
    // готовое значение по каналу NetworkDataSync.Gauge.
    //
    // Точки подключения — уже существующие (правило 2):
    //   • попадание — Unit.OnAfterDamageDealCallbacks (как реакция 5);
    //   • убийство — MatchManager.OnUnitDeathServer (как реакция 3);
    //   • таймер боя — TickHook (как откат реакции 5).
    //
    // Числа блока — скаляры (у обычных умений уровень один, решение Artsiom 2026-08-27).

    [Serializable]
    public class PassiveGaugeBlock
    {
        [Tooltip("Включить блок: носитель копит шкалу.")]
        public bool enabled;

        [Tooltip("Максимальная шкала носителя")]
        public float maxGauge;

        [Tooltip("Прирост шкалы за попадание (прямой удар)")]
        public float hitGain;

        [Tooltip("Прирост шкалы за убийство")]
        public float killGain;

        [Tooltip("Прирост шкалы в секунду (в бою)")]
        public float tickGain;

        [Tooltip("Память боя: секунды без попаданий до остановки тика")]
        public float combatMemory;

        [Tooltip("Только дальние юниты копят (melee == false)")]
        public bool onlyRanged;

        [Tooltip("Не копить в облике (polymorphed == true)")]
        public bool notWhileMorphed;
    }

    public partial class CompositePassive
    {
        [Header("Блок 9 — шкала носителя")]
        public PassiveGaugeBlock gaugeBlock = new PassiveGaugeBlock();

        // ====================================================== СОСТОЯНИЕ ==

        class GaugeState
        {
            public float combatLeft;
        }

        readonly UnitStateMap<GaugeState> gaugeStates = new UnitStateMap<GaugeState>();
        readonly List<Unit> gaugeTickBuffer = new List<Unit>();

        TickHook gaugeTickHook;
        TickHook GaugeTickHook { get { return gaugeTickHook ?? (gaugeTickHook = new TickHook(GaugeTickUpdate)); } }

        bool gaugeKillHubWired;

        // ============================================ ЧИСТЫЕ ФУНКЦИИ (тесты) ==

        public static float GaugeHitGain(float current, float max, float gain)
        {
            return Mathf.Min(current + gain, max);
        }

        public static float GaugeTickGain(float current, float max, float gain, float dt)
        {
            return Mathf.Min(current + gain * dt, max);
        }

        /// <summary>
        /// Что даёт ОДИН удар носителя: сколько прибавить к шкале и продлевать ли таймер боя.
        /// Все гейты удара живут здесь — в тестируемом месте, а не в обработчике (приёмка «Веры», п.2).
        ///
        /// Порядок гейтов важен: первые четыре — «удара для шкалы не было вовсе», они не продлевают
        /// бой. Дальше бой считается идущим: урон умения по врагу таймер продлевает, но прироста
        /// за удар не даёт (прирост — только за ПРЯМУЮ атаку).
        /// </summary>
        /// <param name="carrierMelee">Ближний ли носитель — для гейта «только дальним».</param>
        /// <param name="polymorphed">В облике ли носитель — для гейта «не копить в облике».</param>
        /// <param name="enemy">Цель другой команды. Считает вызывающий штатным селектором.</param>
        /// <param name="extendCombat">Продлевать ли память боя (тик начисляет только в бою).</param>
        public static float GaugeHitDecision(PassiveGaugeBlock b, bool directAttack, bool carrierMelee,
                                             bool polymorphed, float dmg, bool enemy, out bool extendCombat)
        {
            extendCombat = false;

            if (b == null || !b.enabled) return 0f;
            if (dmg <= 0f) return 0f;
            if (!enemy) return 0f;
            if (b.notWhileMorphed && polymorphed) return 0f;

            // Бой идёт: дальше отказы касаются только прироста, но не таймера.
            extendCombat = true;

            if (!directAttack) return 0f;                 // урон умения продлевает бой, но не копит
            if (b.onlyRanged && carrierMelee) return 0f;  // «только дальним» — гейт удара, не подписки

            return b.hitGain;
        }

        // ======================================================= ЖИЗНЕННЫЙ ЦИКЛ ==

        void ResetGauge()
        {
            gaugeStates.Clear();
            if (gaugeTickHook != null) gaugeTickHook.Unwire();
            UnwireGaugeKillHub();
        }

        void ApplyGauge(Unit unit, Carrier c)
        {
            if (gaugeBlock == null || !gaugeBlock.enabled) return;
            if (gaugeBlock.maxGauge <= 0f) return;

            unit.SetMaxGauge(gaugeBlock.maxGauge);
            unit.SetGauge(0f);
            c.gauge = true;
        }

        void RemoveGauge(Unit unit, Carrier c)
        {
            if (!c.gauge) return;
            c.gauge = false;
        }

        void WireGauge(Unit unit, int level)
        {
            if (gaugeBlock == null || !gaugeBlock.enabled) return;

            // «Только дальним» НЕ гейт подключения (приёмка «Веры», п.2): ближний носитель
            // подписывается, состояние заводится, убийства и тик боя работают — не даётся только
            // прирост за удар. Раньше выход отсюда снимал у ближнего носителя все начисления сразу.
            if (!gaugeStates.Contains(unit)) gaugeStates.Set(unit, new GaugeState());
            gaugeStates.PruneDead();

            if (gaugeBlock.hitGain > 0f)
                CallbackAdd(unit.OnAfterDamageDealCallbacks, this, level, GaugeOnHit);

            if (gaugeBlock.killGain > 0f)
                WireGaugeKillHub();

            if (gaugeBlock.tickGain > 0f)
                GaugeTickHook.Wire();
        }

        void UnwireGauge(Unit unit, int level)
        {
            if (unit == null) return;

            if (gaugeBlock != null && gaugeBlock.hitGain > 0f)
                CallbackRemove(unit.OnAfterDamageDealCallbacks, this, level);
        }

        void WireGaugeKillHub()
        {
            if (gaugeKillHubWired || MatchManager.Instance == null) return;
            MatchManager.Instance.OnUnitDeathServer += GaugeOnKill;
            gaugeKillHubWired = true;
        }

        void UnwireGaugeKillHub()
        {
            if (!gaugeKillHubWired) return;
            if (MatchManager.Instance != null) MatchManager.Instance.OnUnitDeathServer -= GaugeOnKill;
            gaugeKillHubWired = false;
        }

        // ======================================================= ПОПАДАНИЕ ==

        /// <summary>
        /// «Враг» для шкалы — цель другой команды. Считается ШТАТНЫМ селектором (правило 1, 9),
        /// а не своим сравнением команд: разъедься оно с ядром, шкала копилась бы по нейтралам.
        /// Флаги: только «враг», любой тип цели и любой способ передвижения, невидимые и неуязвимые
        /// включены — фильтровать цель шкале нечем, ей важен только факт «ударил чужого».
        /// «Дерево» и «разрушаемый объект» выключены СОЗНАТЕЛЬНО: в штатной проверке эти два флага
        /// уводят в ветку, которая принадлежность вообще не смотрит (UnitSelector.IsUnitCompatible).
        /// </summary>
        static readonly UnitSelector GaugeEnemySelector =
            new UnitSelector(false, false, true, true, true, false, false, true, true, true, true, true);

        void GaugeOnHit(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack,
                        DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level)
        {
            if (IsClientPeer) return;
            if (byUnit == null || byUnit.dead) return;

            bool enemy = targetUnit != null
                         && UnitSelector.IsUnitCompatible(byOwner, targetUnit, GaugeEnemySelector);

            bool extendCombat;
            float gain = GaugeHitDecision(gaugeBlock, directAttack, byUnit.melee, byUnit.polymorphed,
                                          dmg, enemy, out extendCombat);

            if (extendCombat)
            {
                GaugeState state;
                if (!gaugeStates.TryGet(byUnit, out state))
                {
                    state = new GaugeState();
                    gaugeStates.Set(byUnit, state);
                }

                state.combatLeft = gaugeBlock.combatMemory;
                if (gaugeBlock.tickGain > 0f) GaugeTickHook.Wire();
            }

            // Зажим по максимуму делает сам ChangeGauge (Unit.Gauge.SetGauge) — GaugeHitGain здесь не нужен.
            if (gain > 0f) byUnit.ChangeGauge(gain);
        }

        // ======================================================= УБИЙСТВО ==

        void GaugeOnKill(Unit victim, int killerPlayer, Unit killerUnit, bool rewards)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (gaugeBlock == null || !gaugeBlock.enabled) return;
            if (killerUnit == null || killerUnit.dead) return;
            if (!carriers.TryGetValue(killerUnit, out Carrier c)) return;
            if (!c.gauge) return;
            if (gaugeBlock.notWhileMorphed && killerUnit.polymorphed) return;
            if (victim == killerUnit) return;

            killerUnit.ChangeGauge(gaugeBlock.killGain);
        }

        // ======================================================= ТИК БОЕВОГО ТАЙМЕРА ==

        void GaugeTickUpdate()
        {
            if (GameManager.Instance == null) return;
            if (gaugeStates.Count == 0) { GaugeTickHook.Unwire(); return; }

            float dt = GameManager.Instance.currentDeltaTime;

            gaugeStates.CopyKeysTo(gaugeTickBuffer);
            for (int i = 0; i < gaugeTickBuffer.Count; i++)
            {
                GaugeState state;
                if (!gaugeStates.TryGet(gaugeTickBuffer[i], out state)) continue;

                if (state.combatLeft > 0f)
                {
                    state.combatLeft -= dt;

                    Unit unit = gaugeTickBuffer[i];
                    if (unit != null && !unit.dead && gaugeBlock.tickGain > 0f)
                    {
                        if (!(gaugeBlock.notWhileMorphed && unit.polymorphed))
                            unit.ChangeGauge(gaugeBlock.tickGain * dt);
                    }
                }
            }

            gaugeStates.PruneDead();
        }
    }
}
