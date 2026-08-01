using UnityEngine;

namespace StrategyCore
{
    [CreateAssetMenu(fileName = "Research", menuName = "StrategyCore/Abilities/Research")]
    public class Research : Ability
    {
        // This is a base class for upgrade abilities
        public override AbilityType type { get { return AbilityType.Process; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("Which technology to unlock. Ability level 1 unlocks the first element. Level 2 the second and so on")]
        public Technology[] unlockTech;

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            TechnologyManager.instance.UnlockTech(unlockTech[level], castingUnit.owner);
            TechnologyManager.instance.TechFinishedProcessing(unlockTech[level], castingUnit.owner);

            // Play research sound and show text
            Presentation.NotifyMsg("Research " + unlockTech[level].displayName + " completed!", castingUnit.owner, false);
            if (ReferenceManager.instance.researchComplete != null) Presentation.Audio?.PlayVoiceClip(ReferenceManager.instance.researchComplete, 1);
        }
    }
}
