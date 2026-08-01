using System;
using UnityEngine;

namespace StrategyCore
{
    // Attack modifications are done with these structs. In order.

    // 1. Called to modify the damage, by attacker before the damage deal, by defender before receiving the damage
    // Used by: Crit - to modify the outgoing damage
    // Used by: Evasion - to modify the incoming damage
    public struct DamageModifyCallback
    {
        // Parameters: Unit that calls the function, Level of the ability, Damage amount, If damage was caused by direct attack. Return: Damage Changed
        public Func<Unit, int, float, bool, float> Callback;
        public Ability Ability;
        public int Level;
    }

    // 2. Called right after damage is modified by DamageModifiyCallback. Right before the moment of attack. For ranged units - when projectile is launched.
    // Used by: Multitarget
    public struct BeforeDamageDealCallback
    {
        // Parameters: Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, DamageType damageType, Unit byUnit, int byOwner, int level
        public Action<Unit, Vector3, Effector[], float, DamageType, Unit, int, int> Callback;
        public Ability Ability;
        public int Level;
    }

    // 3. Right after the target is damaged
    // Used by abilities - Basher: stuns the active target, Splash, Bounce
    public struct AfterDamageDealCallback
    {
        // Parameters: Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack, DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level
        public Action<Unit, Vector3, Effector[], float, bool, DamageType, Unit, Projectile, int, int> Callback;
        public Ability Ability;
        public int Level;
    }


}