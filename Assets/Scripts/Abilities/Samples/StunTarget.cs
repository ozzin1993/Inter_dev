using UnityEngine;

namespace StrategyCore
{
    public class StunTarget : Ability
    {
        // Stuns the target for specified period of time
        // Level 0 means first level. If you want to damage based on level do not forget to add 1. Example: (damage * (level + 1)

        public override AbilityType type { get { return AbilityType.Unit; } } // Specify type

        [Header("Ability specific")]
        public AudioClip launchSound;
        public Projectile projectile;

        public float[] stunDamage;
        public DamageType[] dmgType;
        public float[] stunTime;

        public override void Use(Unit castingUnit, int castingPlayer, int level, Unit unit)
        {
            // Тип урона — общей выборкой по уровням (Б8): нет строки — последняя заполненная. Пустой массив — умение не срабатывает.
            DamageType levelDamageType = InterflowAbility.LevelItem(dmgType, level);
            if (levelDamageType == null)
            {
                Debug.LogWarning($"[StunTarget] {name}: тип урона не заполнен — пропуск");
                return;
            }
            // Play sound
            if (launchSound != null) Presentation.Audio?.PlaySoundClip(launchSound, castingUnit.transform, 1);
            // Spawn projectile and set stun time
            Projectile proj = Projectile.Spawn(castingUnit.owner, castingUnit, projectile, castingUnit.transform.position + new Vector3(0, castingUnit.unitHeight * 0.5f, 0), Quaternion.LookRotation(unit.transform.position - castingUnit.transform.position), unit, false, InterflowAbility.LevelValue(stunDamage, level), levelDamageType, sourceAbility: this);
            proj.stunTime = InterflowAbility.LevelValue(stunTime, level);
        }
    }
}
