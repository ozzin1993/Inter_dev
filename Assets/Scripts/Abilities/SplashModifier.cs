using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Изменение параметров сплеша (площадного урона) носителя.
    /// Потребитель: Тир 4 [А] «Осколочные снаряды» (+40% радиуса взрыва).
    ///
    /// Реализация: штатный <c>Unit.ChangeSplash</c>. Исходные значения запоминаются,
    /// чтобы при блокировке способности вернуть их точно (а не «вычесть обратно»).
    /// </summary>
    public class SplashModifier : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        [Header("Изменение сплеша")]
        [Tooltip("Прибавка к радиусу взрыва в процентах. 0.4 = +40%. Отрицательное значение уменьшает.")]
        public float radiusPercentChange = 0.4f;

        [Tooltip("Прибавка к радиусу взрыва в метрах (применяется после процентной).")]
        public float radiusFlatChange = 0f;

        [Tooltip("Новый спад урона к краю зоны (splashReduction): 1 = урон одинаков по всей зоне, 0.5 = на краю вдвое меньше. −1 — не менять.")]
        public float newSplashReduction = -1f;

        [Tooltip("Включить сплеш, если у юнита его не было. ВЫКЛ — способность ничего не делает для юнита без сплеша.")]
        public bool enableSplashIfDisabled = false;

        // Исходные параметры сплеша, чтобы вернуть их при Lock
        class SplashState
        {
            public bool isSplash;
            public float radius;
            public float reduction;
            public bool followTarget;
        }

        readonly Dictionary<Unit, SplashState> saved = new Dictionary<Unit, SplashState>();

        public override void Init()
        {
            base.Init();
            saved.Clear();
        }

        // Unit.Die не зовёт Lock — мёртвые ключи убираем лениво, при очередной разблокировке
        void PruneDead()
        {
            if (saved.Count == 0) return;

            List<Unit> dead = null;
            foreach (Unit u in saved.Keys)
            {
                if (u == null || u.dead) (dead ?? (dead = new List<Unit>())).Add(u);
            }

            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) saved.Remove(dead[i]);
        }

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            PruneDead();

            if (unit == null || saved.ContainsKey(unit)) return;
            if (!unit.isSplash && !enableSplashIfDisabled) return;

            saved[unit] = new SplashState
            {
                isSplash = unit.isSplash,
                radius = unit.splashRadius,
                reduction = unit.splashReduction,
                followTarget = unit.projectileFollowTarget
            };

            float newRadius = unit.splashRadius * (1f + radiusPercentChange) + radiusFlatChange;
            if (newRadius < 0f) newRadius = 0f;

            float reduction = newSplashReduction >= 0f ? newSplashReduction : unit.splashReduction;

            unit.ChangeSplash(true, newRadius, reduction, unit.projectileFollowTarget);
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;
            if (!saved.TryGetValue(unit, out SplashState state)) return;

            unit.ChangeSplash(state.isSplash, state.radius, state.reduction, state.followTarget);
            saved.Remove(unit);
        }
    }
}
