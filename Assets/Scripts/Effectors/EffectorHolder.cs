using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.Rendering.DebugUI;

// Holds the effectors that are currently applied to the unit
namespace StrategyCore
{
    public class EffectorHolder
    {
        public Effector effector; // Reference to the effector

        public Unit unitOwner; // Which player`s effector is this. If damaging one this player will be seen as a killer
        public int owner = -1; // Which player`s effector is this. If damaging one this player will be seen as a killer
        public float currentTime;

        // [Interflow fix 2026-08-02 effector-unify]
        // Фактические параметры ЭТОГО наложения. Раньше EffectorAdd правил их прямо в ассете,
        // общем для всех носителей. Теперь ассет говорит ЧТО делает эффект, а умение — СКОЛЬКО:
        // силу и длительность оно передаёт снаружи, и они живут здесь, у каждого наложения свои.

        /// <summary>Фактическая длительность этого наложения, секунды. Не меньше двух тиков GameManager.</summary>
        public float duration;

        /// <summary>Фактический стакинг этого наложения. У эффекторов с невидимостью всегда выключен.</summary>
        public bool stacks;

        /// <summary>Множитель силы: масштабирует пассивные изменения статов и урон в секунду. 1 — как в ассете.</summary>
        public float powerMultiplier = 1f;

        // Наложение ровно тем, что записано в ассете
        public EffectorHolder(Effector effector, Unit unitOwner, int owner)
            : this(effector, unitOwner, owner, effector.duration, effector.stacks, 1f) { }

        public EffectorHolder(Effector effector, Unit unitOwner, int owner,
                              float duration, bool stacks, float powerMultiplier)
        {
            this.effector = effector;
            this.unitOwner = unitOwner;
            this.owner = owner;
            this.duration = duration;
            this.stacks = stacks;
            this.powerMultiplier = powerMultiplier;
        }
    }
}