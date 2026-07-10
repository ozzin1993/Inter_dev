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

        public EffectorHolder(Effector effector, Unit unitOwner, int owner)
        {
            this.effector = effector;
            this.unitOwner = unitOwner;
            this.owner = owner;
        }
    }
}
