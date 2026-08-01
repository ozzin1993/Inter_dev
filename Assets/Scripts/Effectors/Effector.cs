using UnityEngine;

namespace StrategyCore
{
    // Effectors are temporary effects that modify a unit’s parameters, either buffing or nerfing them. They can be applied through attacks or abilities and are commonly used in auras.
    // Effectors appear in the unit’s Status UI and last for a specified duration.

    [CreateAssetMenu(fileName = "Effector", menuName = "StrategyCore/Effectors/Create")]
    public class Effector : ScriptableObject
    {
        [EffectorID]
        public int id;

        [Header("Text")]
        public string displayName;
        public Texture2D icon;
        [TextArea(5, 10)]
        public string description;

        [Space(10)]
        [Tooltip("This VFX transform will be added as a child of the unit`s effects holder")]
        public VFXReferencer VFX;
        [Tooltip("Should the VFX appear above the unit`s head")]
        public bool aboveHead;

        [Header("Effects")]
        [Tooltip("Should this effector be permanent and have no duration")]
        public bool permanent = false;
        [Tooltip("After how many seconds this effector should wear off. For aura effectors it should be twice the gameManagerTickRate (0.2s)")]
        public float duration = 0.2f;
        [Tooltip("When the effector stacks, its effects will increase each time it is applied. \nFor example, the Damage Over Time effect caused by an attack will damage twice as much if the unit was attacked twice. \nWhen false, a similar effector can be added only once by the same team")]
        public bool stacks = false; // Abilities that stack are not shown in status

        [Header("Damage Over Time")]
        [Tooltip("How much damage per second is applied to the unit")]
        public int damageAmount;
        [Tooltip("Damage type that is used in damage over time")]
        public DamageType damageType;

        [Header("Invisibility")]
        [Tooltip("Should this unit upon acquiring this effector become invisible")]
        public bool makeInvisible;
        [Tooltip("Carrier of this effector will be revealed by the team that applied the effect")]
        public bool revealInvisible;

        [Header("Passive changes")]
        [Tooltip("Turns on passive effects of the effector")]
        public bool passiveEffectsOn = false;
        [Tooltip("What unit parameters should change when this effector is applied")]
        public AbilityPassiveEffects passiveEffects;

        // Technical
        // private Unit thisUnit; // Current holder of the effector
        // [HideInInspector] public Unit unitOwner; // Which player`s effector is this. If damaging one this player will be seen as a killer
        // [HideInInspector] public int owner = -1; // Which player`s effector is this. If damaging one this player will be seen as a killer
        // float currentTime;
        // 
        // // currentChanges
        // private int currentDamageChange; // Holds current percentage damage change

        // This function is called by the unit that holds this effector currently
        public static void EffectorUpdate(EffectorHolder EH, Unit unitHolder)
        {
            // if (unitHolder == null) return; // Means unit was destroyed

            // Damage
            if (EH.effector.damageAmount != 0)
            {
                unitHolder.GetDamage(EH.effector.damageAmount * GameManager.instance.currentDeltaTime, EH.effector.damageType, EH.owner, EH.unitOwner, false, out float _);
            }

            // If it is a permanent effector, we do not handle removal logic
            if (EH.effector.permanent) return;

            EH.currentTime += GameManager.instance.currentDeltaTime;

            // REMOVAL OF EFFECTOR
            if (EH.currentTime > EH.effector.duration)
            {
                // Remove the passive effects
                if (EH.effector.passiveEffectsOn) EH.effector.passiveEffects.RemoveEffect(unitHolder);

                // Remove tha effector
                bool invisibilityAbilityPresent = false;

                for (int i = 0; i < unitHolder.effectors.Count; i++)
                {
                    // Remove the effector
                    if (unitHolder.effectors[i] == EH && SlotManager.instance.playerTeam[unitHolder.effectors[i].owner] == SlotManager.instance.playerTeam[EH.owner])
                    {
                        if (EH.effector.VFX != null) unitHolder.RemoveVFX(EH.effector.VFX);
                        unitHolder.effectors.RemoveAt(i);
                    }
                    else
                    {
                        // Check if other abilities are making this unit invisible
                        if (unitHolder.effectors[i].effector.makeInvisible) invisibilityAbilityPresent = true;
                    }
                }

                // Remove invisibility only if there is no other effectors making this unit invisible
                if (!invisibilityAbilityPresent && EH.effector.makeInvisible) unitHolder.SetInvisibility(false);

                // Remove visibility of the unit
                if (EH.effector.revealInvisible) unitHolder.CanBeSeen(false, EH.owner);

                if (!EH.effector.stacks) unitHolder.OnStatusUpdate?.Invoke();
            }
        }

        // This function adds the effects of this effector to the unit
        public static void EffectorAdd(Unit unit, Effector effector, Unit unitOwner, int owner, float currentTime = 0)
        {
            // Just a check of duration, it should not be less than GameManager.everyFrameAbilityTickRate * 2
            if (effector.duration < GameManager.tickRate * 2) effector.duration = GameManager.tickRate * 2;
            // Make sure invisibility should not stack, will cause a bug
            if (effector.makeInvisible && effector.stacks) effector.stacks = false;

            if (!effector.stacks)
            {
                // If similar effector is already added to the unit by the same team, just reset the currentTime
                for (int i = 0; i < unit.effectors.Count; i++)
                {
                    if (unit.effectors[i].effector.id == effector.id && SlotManager.instance.playerTeam[unit.effectors[i].owner] == SlotManager.instance.playerTeam[owner])
                    {
                        unit.effectors[i].currentTime = 0;
                        return;
                    }
                }
            }

            // Add new effector to the unit
            EffectorHolder newEH = new EffectorHolder(effector, unitOwner, owner);
            newEH.currentTime = currentTime;
            unit.effectors.Add(newEH);
            if (newEH.effector.VFX != null) unit.AddVFX(newEH.effector.VFX, newEH.effector.aboveHead);

            // Add passive effects
            if (newEH.effector.passiveEffectsOn) newEH.effector.passiveEffects.AddEffect(unit);

            // Make Invisible
            if (newEH.effector.makeInvisible) unit.SetInvisibility(true);

            // If can be seen when invisible, it is used to not hide the renderers
            // Should be applied only by the current player`s team
            if (newEH.effector.revealInvisible) unit.CanBeSeen(true, owner);

            if (!newEH.effector.stacks) unit.OnStatusUpdate?.Invoke();
        }

        // Add Effector[] by unitOwner
        public static void EffectorAdd(Unit ownerUnit, Unit targetUnit, Effector[] effector)
        {
            for (int i = 0; i < effector.Length; i++)
            {
                EffectorAdd(targetUnit, effector[i], ownerUnit, ownerUnit.owner);
            }
        }

        // Add Effector[] by owner only
        public static void EffectorAdd(int owner, Unit targetUnit, Effector[] effector)
        {
            for (int i = 0; i < effector.Length; i++)
            {
                EffectorAdd(targetUnit, effector[i], null, owner);
            }
        }

        // Removes specified effector from a unit
        public static void EffectorRemove(Unit unitHolder, EffectorHolder EH)
        {
            // Remove the passive effects
            if (EH.effector.passiveEffectsOn) EH.effector.passiveEffects.RemoveEffect(unitHolder);

            // Remove tha effector
            bool invisibilityAbilityPresent = false;

            for (int i = 0; i < unitHolder.effectors.Count; i++)
            {
                // Remove the effector
                if (unitHolder.effectors[i] == EH && SlotManager.instance.playerTeam[unitHolder.effectors[i].owner] == SlotManager.instance.playerTeam[EH.owner])
                {
                    if (EH.effector.VFX != null) unitHolder.RemoveVFX(EH.effector.VFX);
                    unitHolder.effectors.RemoveAt(i);
                }
                else
                {
                    // Check if other abilities are making this unit invisible
                    if (unitHolder.effectors[i].effector.makeInvisible) invisibilityAbilityPresent = true;
                }
            }

            // Remove invisibility only if there is no other effectors making this unit invisible
            if (!invisibilityAbilityPresent && EH.effector.makeInvisible) unitHolder.SetInvisibility(false);

            // Remove visibility of the unit
            if (EH.effector.revealInvisible) unitHolder.CanBeSeen(false, EH.owner);

            if (!EH.effector.stacks) unitHolder.OnStatusUpdate?.Invoke();
        }

        // Returns the effector by its ID
        public static Effector GetEffectorByID(int effectorID)
        {
            if (GameManager.instance.gameEffectors.TryGetValue(effectorID, out Effector effector))
            {
                return effector;
            }

            return null;
        }
    }
}
