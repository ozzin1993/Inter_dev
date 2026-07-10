using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Asset menu
    [CreateAssetMenu(fileName = "Transformation", menuName = "StrategyCore/Abilities/Transformation")]
    public class Transformation : Ability
    {
        public override AbilityType type { get { return AbilityType.Active; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("Visuals of this unit will be used to transform the caster.")]
        public Unit transformUnit;
        [Tooltip("Duration of transformation.")]
        public float[] transformTime;
        [Tooltip("Sound effect.")]
        public AudioClip transformSound;
        [Tooltip("Visual effect.")]
        public VFXReferencer transformVFX;
        [Tooltip("Passive effects during transform.")]
        public AbilityPassiveEffects[] passiveEffects;

        // This function is called before the Use() to run custom requirements scheck
        public override bool Check(Unit castingUnit, int castingPlayer, int level)
        {
            // On clients we forcefully use the ability, on the server we check and send to clients if usable
            if (NetworkConnectionHandler.isClient) return true;

            // We should not transform when the unit is already transformed
            if (castingUnit.polymorphed)
            {
                UIManager.instance.ShowNotifyMsg("Unit can not currently transform!", castingUnit.owner, true);
                return false;
            }
            return true;
        }

        // This function is called when the ability is clicked
        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            // We should not transform when the unit is already transformed
            if (castingUnit.polymorphed) return;

            // Add effects - since we are changing the range, it is important to add effects first for projectile to be set up correctly.
            passiveEffects[level].AddEffect(castingUnit);
            // Change shape
            castingUnit.Polymorph(this, level, transformTime[level], transformUnit);

            // Reselect to display the timer
            if (PlayerControl.instance.activeUnit == castingUnit) UIManager.instance.Resubscribe();
            // Play sound, SoundFXManager takes care of visibility check
            if (transformSound != null) SoundFXManager.instance.PlaySoundClip(transformSound, castingUnit.transform, 1, SoundFXManager.instance.fxGroup);
            // Play VFX only if the caster is visible
            if (transformVFX != null && castingUnit.FoWVisible) castingUnit.AddVFX(transformVFX);
        }

        public override void Deactivate(Unit castingUnit, int castingPlayer, int level)
        {
            // Remove effects
            passiveEffects[level].RemoveEffect(castingUnit);
            // Return back to original shape
            castingUnit.RestoreRenderers();
        }
    }
}
