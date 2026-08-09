using UnityEngine;

namespace StrategyCore
{
    public class Wololo : Ability
    {
        // Changes the owner of the target
        // Level 0 means first level. If you want to damage based on level do not forget to add 1. Example: (damage * (level + 1)

        public override AbilityType type { get { return AbilityType.Unit; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("Audio clip played for this ability.")]
        public AudioClip audioClip;

        public override void Use(Unit castingUnit, int castingPlayer, int level, Unit unit)
        {
            // Change ownership of the unit
            unit.SetOwnership(castingUnit.owner);
            // Idle the unit and change reference to null, to make sure we are not continuing the previous action
            unit.Idle();
            unit.OnReferenceChange?.Invoke(null);
            if (audioClip != null) Presentation.Audio?.PlaySoundClip(audioClip, unit.transform, 1);
        }
    }
}
