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

    // 4. Снаряд долетел. Поднимается на ВСЕХ пирах после урона и разлёта, до уничтожения снаряда;
    //    при рикошете — на каждый прилёт. Гейт клиента — у подписчиков, а не у подъёмника.
    //    Отличие от AfterDamageDealCallback: событие есть и когда цели не стало (прилёт в землю),
    //    и когда урон никого не принял, — именно ради «места, куда попало» оно и заведено.
    //    [Interflow 2026-09-16 слияние с Сашей, семья Б] Блок 22 умения и реакция 5 пассивки.
    public struct ProjectileImpactCallback
    {
        // Parameters: Vector3 точка прилёта, Unit цель (null — земля), Unit стрелок (null — погиб в полёте),
        // int владелец снаряда, bool directAttack, Projectile сам снаряд, int level
        public Action<Vector3, Unit, Unit, int, bool, Projectile, int> Callback;
        public Ability Ability;
        public int Level;
    }
}