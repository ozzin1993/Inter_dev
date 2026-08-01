using System;
using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace StrategyCore
{
    // ShadowCaster casts uninterruptible, continuous ability
    public class ShadowCaster : MonoBehaviour
    {
        public int id;
        public Unit thisUnit;
        public int castingPlayer;

        // Active ability technical variables. Active ability is an continous ability that is performed over time, for example draining the HP over period of time.
        [HideInInspector] public Ability activeAbility; // Continuous ability that is being used by this unit
        [HideInInspector] public int activeAbilityLevel; // Level of the active ability
        [HideInInspector] public Unit activeAbilityUnit; // If active ability requires unit
        [HideInInspector] public Vector3 activeAbilityLocation; // If active ability requires location
        [HideInInspector] public float activeAbilityRange; // Range of active ability
        [HideInInspector] public float activeAbilityDuration; // If this active ability has a duration

        [HideInInspector] public VFXReferencer activeAbilityVFX; // VFX of actively being used ability. Set by abilities` Activate/Deactivate

        public float currentTime;
        private bool requiresTargetUnit; // If this shadowcaster needs target unit to exist

        private void Update()
        {
            // Unit existence cheeck
            if ((activeAbility.requiresCastingUnit && thisUnit == null) || (requiresTargetUnit && activeAbilityUnit == null)) Remove();

            // Distance check
            bool distanceGood = true;
            if (activeAbilityRange != 0)
            {
                if (thisUnit == null) distanceGood = false;
                else if (activeAbilityUnit) distanceGood = (Vector2.Distance(new Vector2(thisUnit.transform.position.x, thisUnit.transform.position.z), new Vector2(activeAbilityUnit.transform.position.x, activeAbilityUnit.transform.position.z)) > activeAbilityRange) ? false : true;
                else if (activeAbilityLocation != Vector3.zero) distanceGood = (Vector2.Distance(new Vector2(thisUnit.transform.position.x, thisUnit.transform.position.z), new Vector2(activeAbilityLocation.x, activeAbilityLocation.z)) > activeAbilityRange) ? false : true;
            }

            // Mana costs
            float manaCost = 0;
            if (activeAbility.manaCostPerSecond.Length > activeAbilityLevel) manaCost = activeAbility.manaCostPerSecond[activeAbilityLevel] * Time.deltaTime;

            currentTime += Time.deltaTime;
            // Distance and manacost check only on server. Duration check both on server and client
            if ((!NetworkConnectionHandler.isClient && (!distanceGood || (manaCost != 0 && thisUnit && thisUnit.mana < manaCost))) || (activeAbilityDuration != 0 && currentTime > activeAbilityDuration))
            {
                // Ability can not be used
                Remove();
            }
            else
            {
                // Ability can be used, Using actively
                if (manaCost != 0) thisUnit.ChangeMP(-manaCost);

                if (activeAbilityUnit)
                {
                    activeAbility.Use(thisUnit, castingPlayer, activeAbilityLevel, activeAbilityUnit, ref activeAbilityVFX);
                }
                else if (activeAbilityLocation != Vector3.zero)
                {
                    activeAbility.Use(thisUnit, castingPlayer, activeAbilityLevel, activeAbilityLocation, ref activeAbilityVFX);
                }
                else
                {
                    activeAbility.Use(thisUnit, castingPlayer, activeAbilityLevel, ref activeAbilityVFX);
                }
            }
        }

        public static ShadowCaster Spawn(int id, Unit castingUnit, int castingPlayer, Ability ability, int level, Unit targetUnit, Vector3 targetLocation, float range, float duration)
        {
            GameObject obj = new GameObject("ShadowCaster");
            ShadowCaster sc = obj.AddComponent<ShadowCaster>();

            // Data
            sc.castingPlayer = castingPlayer;
            sc.thisUnit = castingUnit;
            if (castingUnit != null) sc.thisUnit.OnReferenceChange += sc.ThisUnitRefChange;
            sc.id = id;

            sc.activeAbility = ability;
            sc.activeAbilityLevel = level;
            sc.activeAbilityUnit = targetUnit;
            if (targetUnit != null)
            {
                sc.activeAbilityUnit.OnReferenceChange += sc.AbilityUnitRefChange;
                sc.requiresTargetUnit = true;
            }
            sc.activeAbilityLocation = targetLocation;
            sc.activeAbilityRange = range;
            sc.activeAbilityDuration = duration;

            // Add to shadowcaster tracker
            GameManager.instance.shadowCasters.Add(sc);
            GameManager.instance.shadowCasterIDs.Add(sc.id);

            return sc;
        }

        public static ShadowCaster Spawn(ShadowCasterSyncData scData)
        {
            // Prepare data
            Unit ownerUnit = (SlotManager.instance.unitNetID[scData.thisUnitID]) ? SlotManager.instance.unitNetID[scData.thisUnitID] : null;
            Unit targetUnit = (SlotManager.instance.unitNetID[scData.targetUnitID]) ? SlotManager.instance.unitNetID[scData.targetUnitID] : null;

            GameObject obj = new GameObject("ShadowCaster");
            ShadowCaster sc = obj.AddComponent<ShadowCaster>();

            // Data
            sc.castingPlayer = scData.castingPlayer;
            sc.thisUnit = ownerUnit;
            if (ownerUnit != null) sc.thisUnit.OnReferenceChange += sc.ThisUnitRefChange;
            sc.id = scData.shadowCasterID;

            sc.activeAbility = GameManager.instance.gameAbilities[scData.abilityID];
            sc.activeAbilityLevel = scData.abilityLevel;
            sc.activeAbilityUnit = targetUnit;
            if (targetUnit != null)
            {
                sc.activeAbilityUnit.OnReferenceChange += sc.AbilityUnitRefChange;
                sc.requiresTargetUnit = true;
            }
            sc.activeAbilityLocation = scData.targetLocation;
            sc.activeAbilityRange = scData.range;
            sc.activeAbilityDuration = scData.duration;

            // Add to shadowcaster tracker
            GameManager.instance.shadowCasters.Add(sc);
            GameManager.instance.shadowCasterIDs.Add(sc.id);

            return sc;
        }

        // Destroys shadowcaster
        public void Remove()
        {
            // Remove from shadowcaster tracker
            int index = GameManager.instance.shadowCasterIDs.IndexOf(id);
            if (index != -1)
            {
                GameManager.instance.shadowCasters.RemoveAt(index);
                GameManager.instance.shadowCasterIDs.RemoveAt(index);
            }

            // Events clear
            if (thisUnit != null) thisUnit.OnReferenceChange -= ThisUnitRefChange;
            if (activeAbilityUnit != null) activeAbilityUnit.OnReferenceChange -= AbilityUnitRefChange;

            // Deactivate
            if (activeAbilityUnit) activeAbility.Deactivate(thisUnit, castingPlayer, activeAbilityLevel, activeAbilityUnit, ref activeAbilityVFX);
            else if (activeAbilityLocation != Vector3.zero) activeAbility.Deactivate(thisUnit, castingPlayer, activeAbilityLevel, activeAbilityLocation, ref activeAbilityVFX);
            else activeAbility.Deactivate(thisUnit, castingPlayer, activeAbilityLevel, ref activeAbilityVFX);

            Destroy(gameObject);

            // Send info to clients
            if (NetworkManager.Singleton.IsServer)
            {
                NetworkDataSync.instance.ShadowCasterRemoveClientRpc(id);
            }
        }

        // Returns unique id for shadowCaster
        public static int GetUniqueID()
        {
            int id;
            do id = UnityEngine.Random.Range(1, 9999); // Generate a random int
            while (GameManager.instance.shadowCasterIDs.Contains(id));
            return id;
        }

        // When reference to castingUnit changes
        public void ThisUnitRefChange(Unit newRef)
        {
            if (thisUnit != null) thisUnit.OnReferenceChange -= ThisUnitRefChange;

            thisUnit = newRef;

            if (thisUnit != null) thisUnit.OnReferenceChange += ThisUnitRefChange;
            else if (activeAbility.requiresCastingUnit) Remove();
        }

        // When reference to targetUnit changes
        public void AbilityUnitRefChange(Unit newRef)
        {
            if (activeAbilityUnit != null) activeAbilityUnit.OnReferenceChange -= AbilityUnitRefChange;

            activeAbilityUnit = newRef;

            if (activeAbilityUnit != null) activeAbilityUnit.OnReferenceChange += AbilityUnitRefChange;
            else if (requiresTargetUnit) Remove();
        }
    }

    public class ShadowCasterSyncData
    {
        public UInt16 thisUnitID;
        public int castingPlayer;
        public int shadowCasterID;
        public int abilityID;
        public int abilityLevel;
        public UInt16 targetUnitID;
        public Vector3 targetLocation;
        public float range;
        public float duration;

        public ShadowCasterSyncData(UInt16 thisUnitID, int castingPlayer, int shadowCasterID, int abilityID, int abilityLevel, UInt16 targetUnitID, Vector3 targetLocation, float range, float duration)
        {
            this.thisUnitID = thisUnitID;
            this.castingPlayer = castingPlayer;
            this.shadowCasterID = shadowCasterID;
            this.abilityID = abilityID;
            this.abilityLevel = abilityLevel;
            this.targetUnitID = targetUnitID;
            this.targetLocation = targetLocation;
            this.range = range;
            this.duration = duration;
        }
    }
}