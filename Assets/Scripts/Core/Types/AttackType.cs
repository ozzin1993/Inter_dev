using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Defines type of attacks, not damage.
    // There are 3 types
    // Standard - Every X seconds (defined by attack speed) attack
    // Continuous - Waits x second (defined by attack speed) and attacks damaging the target every frame
    // Periodic - Every X seconds (defined by attack speed) attacks N amounts of time (defined by periodic count) with a delay between each attacks (defined by periodic delay)
    public enum AttackType
    {
        Standard,
        Continuous,
        Periodic
    }
}

