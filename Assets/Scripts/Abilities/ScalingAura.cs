using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Аура, сила которой растёт по мере потери здоровья носителем.
    /// Потребитель: Тир 6 [Б] «Вдохновение мучеников» (чем сильнее ранен Палач, тем выше урон союзной армии вокруг).
    ///
    /// Почему ступенями: эффектор — это ассет с фиксированными числами, менять его в рантайме нельзя
    /// (он общий для всех). Поэтому дизайнер задаёт несколько ступеней «порог потерянного ХП → свой эффектор»,
    /// и аура раздаёт эффектор той ступени, которая сейчас достигнута. Ступеней может быть сколько угодно.
    /// </summary>
    public class ScalingAura : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        /// <summary>Ступень ауры: с какой доли потерянного ХП включается и какие эффекторы раздаёт.</summary>
        [Serializable]
        public class Step
        {
            [Tooltip("Доля ПОТЕРЯННОГО здоровья носителя, начиная с которой действует эта ступень. 0.25 = когда потеряно 25% ХП.")]
            [Range(0f, 1f)]
            public float missingHpFrom = 0.25f;

            [Tooltip("Эффекторы этой ступени (например +10% урона). Длительность эффектора — короткая: он обновляется каждый тик.")]
            public Effector[] effectors;
        }

        [Header("Аура от потери здоровья")]
        [Tooltip("Ступени по возрастанию порога. Действует последняя ступень, чей порог достигнут. " +
                 "Пример для «до +30% урона»: 0.25 → +10%, 0.5 → +20%, 0.75 → +30%.")]
        public Step[] steps;

        [Tooltip("Раздавать эффекторы и самому носителю. ВЫКЛ — только союзникам вокруг.")]
        public bool includeSelf = false;

        readonly Dictionary<Unit, int> levels = new Dictionary<Unit, int>();
        readonly List<Unit> tickBuffer = new List<Unit>();
        bool tickWired;

        public override void Init()
        {
            base.Init();

            levels.Clear();
            if (tickWired && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            tickWired = false;
        }

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null || levels.ContainsKey(unit)) return;

            levels[unit] = level;

            if (!tickWired && GameManager.instance != null)
            {
                GameManager.instance.Tick += OnTick;
                tickWired = true;
            }
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;

            levels.Remove(unit);

            if (levels.Count == 0 && tickWired && GameManager.instance != null)
            {
                GameManager.instance.Tick -= OnTick;
                tickWired = false;
            }
        }

        void OnTick()
        {
            if (NetworkConnectionHandler.isClient) return; // раздача эффектов — сервер (правило 6)
            if (levels.Count == 0 || steps == null || steps.Length == 0) return;

            tickBuffer.Clear();
            tickBuffer.AddRange(levels.Keys);

            for (int i = 0; i < tickBuffer.Count; i++)
            {
                Unit u = tickBuffer[i];
                if (u == null || u.dead)
                {
                    levels.Remove(u);
                    continue;
                }

                if (u.maxHealth <= 0f) continue;

                float missing = Mathf.Clamp01(1f - u.health / u.maxHealth);
                Effector[] active = PickStep(missing);
                if (active == null || active.Length == 0) continue;

                int level = levels.TryGetValue(u, out int l) ? l : 0;
                float r = LevelValue(radius, level, 0f);
                if (r <= 0f) continue;

                Vector2 pos = new Vector2(u.transform.position.x, u.transform.position.z);
                Unit[] allies = Utils.GetUnitsInRadius(pos, r, u.owner, unitSelector, -1, includeSelf ? null : u);
                if (allies == null) continue;

                for (int a = 0; a < allies.Length; a++)
                {
                    Unit ally = allies[a];
                    if (ally == null || ally.dead) continue;

                    Effector.EffectorAdd(u, ally, active);
                }
            }
        }

        /// <summary>Эффекторы последней ступени, чей порог достигнут. Ни одной — null.</summary>
        Effector[] PickStep(float missingHp)
        {
            Effector[] result = null;
            float bestThreshold = -1f;

            for (int i = 0; i < steps.Length; i++)
            {
                Step s = steps[i];
                if (s == null || s.effectors == null || s.effectors.Length == 0) continue;
                if (missingHp < s.missingHpFrom) continue;

                if (s.missingHpFrom > bestThreshold)
                {
                    bestThreshold = s.missingHpFrom;
                    result = s.effectors;
                }
            }

            return result;
        }

    }
}
