using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Defines a unit that works with resources. Storage/Collectible/Collector.
    public class ResourceUnit : MonoBehaviour
    {
        [Header("Storage")]
        [Tooltip("If this unit accepts gathered resources that will be added to player")]
        public bool isStorage;

        [Header("Collectible")]
        [Tooltip("If this unit holds resources to collect")]
        public bool isCollectible;
        [Tooltip("If this unit needs to be hit for the resources to be collected. Example: Tree")]
        public bool hitToCollect;
        [Tooltip("Resource types and amount that this collectible holds")]
        public ResourceWrapper[] collectibleResources;

        [Header("Collector")]
        [Tooltip("If this unit can collect resources")]
        public bool isCollector;
        [Tooltip("How many seconds should the collector wait before acquiring resources at the non-HitToCollect collectible")]
        public float timeToCollect;
        [Tooltip("Amount of resources this collector collects at non-HitToCollect collectible and maximum carry capacity for Hit Resources")]
        public ResourceWrapper[] collectorResources;
        [Tooltip("Amount of resources this collector gains each time it hits a HitToCollect collectible. The same resource types also should be in Collector Resources, it will define the max capacity for carrying this type of resource")]
        public ResourceWrapper[] hitResources;
        [Tooltip("If this unit should play different animation sets when carrying different resource types, specify it here. If you leave resource type empty, it will be applied to all resource types")]
        public ResourceAnimationBlendingIndex[] animationBlendingIndex;

        // Technical
        Unit thisUnit; // the unit reference of this script
        [HideInInspector] public bool isCollecting = false; // For collector if the collector is at the resource unit collecting resources
                                                            // For collectible if this particular resource is being collected right now, so only 1 unit at a time can collect it
        bool collectUpdate = false; // If true every update collect function will play
        [HideInInspector] public bool constructionCommand; // To make sure that when player commands to collect a resource we will not trigger construction

        // Collector
        [HideInInspector] public float currentWaitTime = 0;
        [HideInInspector] public Unit storageObj; // current storage unit that this collector brings resources to
        [HideInInspector] public Unit collectibleObj; // current collectible unit that this collector takes resources from
        [HideInInspector] public List<ResourceWrapper> currentHeldResources; // The amount of resources at the hands of the collector / available resources at collectible
        private bool animationBlendingSet; // If animation blending was set

        // Called by Initialize() of thisUnit
        public void Initialize()
        {
            thisUnit = GetComponent<Unit>();
            thisUnit.OnDie += DieCallback;

            if (isCollector)
            {
                // Hit collectible add to callbacks
                thisUnit.OnAfterDamageDealCallbacks.Add(new AfterDamageDealCallback
                {
                    Callback = HitCollectible,
                    Ability = null,
                    Level = -9 // Indication that the callback is for resourceUnit
                });

                thisUnit.OnFollowReach += UnitReached;
                thisUnit.OnCommand += OnCommandIssued;
            }
        }

        // What happens when this unit dies
        private void DieCallback(Unit unitThatDies, int playerKiller, Unit unitKiller, bool rewards)
        {
            // Clean up
            thisUnit.OnDie -= DieCallback;

            if (isCollector)
            {
                // Hit collectible
                // Remove from callbacks
                for (int i = 0; i < thisUnit.OnAfterDamageDealCallbacks.Count; i++)
                {
                    var c = thisUnit.OnAfterDamageDealCallbacks[i];
                    if (c.Level == -9)
                    {
                        thisUnit.OnAfterDamageDealCallbacks.RemoveAt(i);
                        break;
                    }
                }

                thisUnit.OnFollowReach -= UnitReached;
                thisUnit.OnCommand -= OnCommandIssued;

                if (storageObj)
                {
                    storageObj.OnReferenceChange -= StorageReferenceChange;
                    storageObj.OnDie -= StorageDied;
                }

                if (collectibleObj)
                {
                    collectibleObj.OnDie -= CollectibleDied;
                }
            }
        }

        // Update is called once per frame
        void Update()
        {
            CollectResources();
        }

        // Collector: If suitable storage found returns true
        public bool FindStorage()
        {
            storageObj = Utils.GetClosestResourceUnit(thisUnit.owner, new Vector2(transform.position.x, transform.position.z), new Vector2(transform.position.x, transform.position.z), Utils.searchRadius, null, 0);
            if (storageObj != null)
            {
                storageObj.OnReferenceChange += StorageReferenceChange;
                storageObj.OnDie += StorageDied;
                return true;
            }
            return false;
        }

        // Collector: If suitable collectible found returns true. For non-hit collectibles
        public bool FindCollectible()
        {
            // If we have resources currently held, we try to find the same collectible source
            if (currentHeldResources.Count > 0)
            {
                for (int i = 0; i < currentHeldResources.Count; i++)
                {
                    collectibleObj = Utils.GetClosestResourceUnit(thisUnit.owner, new Vector2(transform.position.x, transform.position.z), new Vector2(transform.position.x, transform.position.z), Utils.searchRadius, currentHeldResources[currentHeldResources.Count - i - 1].type, 1);
                    if (collectibleObj != null) return true;
                }
            }
            else
            {
                for (int i = 0; i < collectorResources.Length; i++)
                {
                    collectibleObj = Utils.GetClosestResourceUnit(thisUnit.owner, new Vector2(transform.position.x, transform.position.z), new Vector2(transform.position.x, transform.position.z), Utils.searchRadius, collectorResources[i].type, 1);
                    if (collectibleObj != null) return true;
                }
            }
            return false;
        }

        // Find collectible based on the current collectible resource types. For hit collectibles
        public bool FindCollectibleBased()
        {
            if (collectibleObj && collectibleObj.resourceUnit.collectibleResources.Length > 0)
            {
                Unit tempCollectible;
                for (int i = 0; i < collectibleObj.resourceUnit.collectibleResources.Length; i++)
                {
                    tempCollectible = Utils.GetClosestResourceUnit(thisUnit.owner, new Vector2(transform.position.x, transform.position.z), new Vector2(collectibleObj.transform.position.x, collectibleObj.transform.position.z), Utils.searchRadius, collectibleObj.resourceUnit.collectibleResources[i].type, 1, collectibleObj);
                    if (tempCollectible != null)
                    {
                        collectibleObj = tempCollectible;
                        collectibleObj.OnDie += CollectibleDied;
                        return true;
                    }
                }
            }
            else if (currentHeldResources.Count > 0)
            {
                for (int i = 0; i < currentHeldResources.Count; i++)
                {
                    collectibleObj = Utils.GetClosestResourceUnit(thisUnit.owner, new Vector2(transform.position.x, transform.position.z), new Vector2(transform.position.x, transform.position.z), Utils.searchRadius, currentHeldResources[currentHeldResources.Count - i - 1].type, 1);
                    if (collectibleObj != null)
                    {
                        collectibleObj.OnDie += CollectibleDied;
                        return true;
                    }
                }
            }

            return false;
        }

        // Collecctor: Sets storage for this resourceUnit if resource types are compatible
        public bool SetStorage(Unit storageUnit)
        {
            // Check if storage is being constructed. If storage is being upgraded it will pass this check
            if (storageUnit.constructionUnit && !storageUnit.constructionUnit.completed) return false;

            if (storageUnit.resourceUnit && storageUnit.resourceUnit.isStorage)
            {
                storageObj = storageUnit;
                storageObj.OnReferenceChange += StorageReferenceChange;
                storageObj.OnDie += StorageDied;
                return true;
            }
            return false;
        }

        // Collecctor: Sets collectibleUnit for this resourceUnit if resource types are compatible and resource count > 0
        public bool SetCollectible(Unit collectible)
        {
            // Check if unit is being constructed. If unit is being upgraded it will pass this check
            if (collectible.constructionUnit && !collectible.constructionUnit.completed) return false;

            if (collectible.resourceUnit && collectible.resourceUnit.isCollectible)
            {
                for (int i = 0; i < collectorResources.Length; i++)
                {
                    int resourceIndex = GetResourceIndex(collectorResources[i].type, collectible.resourceUnit.collectibleResources);
                    if (resourceIndex != -1 && collectible.resourceUnit.collectibleResources[resourceIndex].value > 0)
                    {
                        collectibleObj = collectible;
                        return true;
                    }
                }
            }
            return false;
        }

        // Collectible: Obtain resources. Returns true if there are still resources to collect, false if there are none
        public bool ObtainResources(ResourceUnit collector)
        {
            bool resourcesLeft = false;
            for (int i = 0; i < collector.collectorResources.Length; i++)
            {
                int resourceIndex = GetResourceIndex(collector.collectorResources[i].type, collectibleResources);

                if (resourceIndex != -1 && collectibleResources[resourceIndex].value > 0)
                {
                    // Collectible has more resources than collector can take
                    if (collectibleResources[resourceIndex].value > collector.collectorResources[i].value)
                    {
                        collector.AddCurrentHeldResources(collector.collectorResources[i].type, collector.collectorResources[i].value);
                        collectibleResources[resourceIndex].value -= collector.collectorResources[i].value;
                        resourcesLeft = true;
                    }
                    // Collectible has less resources than collector can take
                    else
                    {
                        collector.AddCurrentHeldResources(collector.collectorResources[i].type, collectibleResources[resourceIndex].value);
                        collectibleResources[resourceIndex].value = 0;
                        thisUnit.Die(-1, null, false);
                    }
                }
            }
            return resourcesLeft;
        }

        // Collector: Every update update timer to Collect resources
        public void CollectResources()
        {
            if (!isCollector) return;
            if (!collectUpdate) return;

            // Should test for distance?

            if (isCollecting)
            {
                // Already reached collectible, wait X seconds and collect resources
                currentWaitTime += Time.deltaTime;

                if (currentWaitTime >= timeToCollect)
                {
                    // Waited X seconds, obtain resources and go back to storage
                    isCollecting = false;
                    collectUpdate = false;
                    collectibleObj.resourceUnit.isCollecting = false;

                    if (!collectibleObj.resourceUnit.ObtainResources(this))
                    {
                        // No resources left
                        collectibleObj = null;
                        FindCollectible();
                    }

                    // Go to storage
                    GoToStorage();
                }
            }
            else if (collectibleObj.resourceUnit.isCollecting == false) // Wait in queue for collectible to be available
            {
                // Has just reached the collectible, start waiting to collect resources
                currentWaitTime = 0;
                isCollecting = true;
                collectibleObj.resourceUnit.isCollecting = true;
            }
        }

        // Collector: Add resources to player. Storage owner gets the resources. If storage owner is neutral passive, collector owner gets the resources.
        public void UnloadResources()
        {
            // Unloads resources at the storage
            int player = storageObj.resourceUnit.thisUnit.owner;
            if (player == (int)Players.NeutralPassive) player = thisUnit.owner;

            for (int i = 0; i < currentHeldResources.Count; i++)
            {
                GameResources.Instance.ChangeAmount(player, currentHeldResources[i], 1, false, true);
                FloatingText.Spawn(thisUnit.owner, thisUnit.transform.position + new Vector3(0, thisUnit.unitHeight + i * 0.5f, 0), "+" + currentHeldResources[i].value.ToString(), currentHeldResources[i].type.resourceColor, true);
            }

            // Try to find similar collectible if does not exists
            if (collectibleObj == null) FindCollectibleBased();

            // Clear the list
            currentHeldResources.Clear();

            if (collectibleObj != null)
            {
                if (collectibleObj.resourceUnit.hitToCollect) thisUnit.Attack(collectibleObj);
                else thisUnit.Follow(collectibleObj);
            }
        }

        // Collector: This function is called when this collector hits a unit, we test if it is collectibleRef and collect resources
        public static void HitCollectible(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack, DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level)
        {
            // If target is our hit collectible unit
            if (byUnit == null || directAttack == false) return;
            ResourceUnit ru = byUnit.resourceUnit;

            if (ru.collectibleObj && ru.collectibleObj == byUnit.target && ru.collectibleObj.resourceUnit.hitToCollect)
            {
                bool resourcesLeft = false;
                bool goingBack = false;
                for (int i = 0; i < ru.hitResources.Length; i++)
                {
                    int resourceIndex = GetResourceIndex(ru.hitResources[i].type, ru.collectibleObj.resourceUnit.collectibleResources);

                    if (resourceIndex != -1 && ru.collectibleObj.resourceUnit.collectibleResources[resourceIndex].value > 0)
                    {
                        int currentHitResourcesCollected = ru.hitResources[i].value;

                        // The maximum amount of resources this collector can take with the hit, defined by max amount of resources it can carry
                        int currentHeldAmount = 0;

                        int currentHeldResIndex = GetResourceIndex(ru.hitResources[i].type, ru.currentHeldResources);
                        if (currentHeldResIndex != -1) currentHeldAmount = ru.currentHeldResources[currentHeldResIndex].value;

                        int collectorResourceIndex = GetResourceIndex(ru.hitResources[i].type, ru.collectorResources);

                        if (currentHeldAmount + currentHitResourcesCollected > ru.collectorResources[collectorResourceIndex].value)
                        {
                            currentHitResourcesCollected = ru.collectorResources[collectorResourceIndex].value - currentHeldAmount;
                        }

                        // Collectible has more resources than collector can take
                        if (ru.collectibleObj.resourceUnit.collectibleResources[resourceIndex].value > currentHitResourcesCollected)
                        {
                            ru.AddCurrentHeldResources(ru.hitResources[i].type, currentHitResourcesCollected);
                            ru.collectibleObj.resourceUnit.collectibleResources[resourceIndex].value -= currentHitResourcesCollected;
                            resourcesLeft = true;
                        }
                        // Collectible has less resources than collector can take
                        else
                        {
                            ru.AddCurrentHeldResources(ru.hitResources[i].type, currentHitResourcesCollected);
                            ru.collectibleObj.resourceUnit.collectibleResources[resourceIndex].value = 0;
                        }

                        // Collector can`t carry anymore, go to storage
                        if (currentHeldResIndex != -1 && ru.currentHeldResources[currentHeldResIndex].value >= ru.collectorResources[collectorResourceIndex].value)
                        {
                            ru.GoToStorage();
                            goingBack = true;
                        }
                    }
                }

                // No resources left in current collectible, find new one
                if (!resourcesLeft)
                {
                    ru.collectibleObj.OnDie -= ru.CollectibleDied;
                    ru.collectibleObj.Die(byUnit.owner, byUnit, false);

                    if (ru.FindCollectibleBased())
                    {
                        if (goingBack == false)
                        {
                            if (ru.collectibleObj.resourceUnit.hitToCollect)
                            {
                                byUnit.Attack(ru.collectibleObj);
                            }
                            else
                            {
                                byUnit.Follow(ru.collectibleObj);
                            }
                        }
                    }
                    else
                    {
                        // No collectible found, go to storage
                        ru.collectibleObj = null;
                        if (goingBack == false)
                        {
                            ru.GoToStorage();
                        }
                    }
                }
            }
        }

        // Collector: Check if collector can carry more resources at current collectible
        public bool CanCarryMoreAtCurrentCollectible()
        {
            // If collectible exists
            if (collectibleObj)
            {
                if (collectibleObj.resourceUnit.hitToCollect)
                {
                    // Hit collectible
                    for (int i = 0; i < hitResources.Length; i++)
                    {
                        int resourceIndexInCollectible = GetResourceIndex(hitResources[i].type, collectibleObj.resourceUnit.collectibleResources);
                        int resourceIndexInCurrent = GetResourceIndex(hitResources[i].type, currentHeldResources);

                        // Collector can`t carry anymore, go to storage
                        if (resourceIndexInCollectible != -1 && (resourceIndexInCurrent == -1 || currentHeldResources[resourceIndexInCurrent].value < collectorResources[i].value))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    // Standard collectible
                    for (int i = 0; i < collectorResources.Length; i++)
                    {
                        int resourceIndexInCollectible = GetResourceIndex(collectorResources[i].type, collectibleObj.resourceUnit.collectibleResources);
                        int resourceIndexInCurrent = GetResourceIndex(collectorResources[i].type, currentHeldResources);

                        // Collector can`t carry anymore, go to storage
                        if (resourceIndexInCollectible != -1 && (resourceIndexInCurrent == -1 || currentHeldResources[resourceIndexInCurrent].value < collectorResources[i].value))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // Collector: Command was issued to the unit
        public void OnCommandIssued(bool issuedByPlayer)
        {
            // Reset current collecting data
            if (collectibleObj)
            {
                if (collectibleObj.resourceUnit.isCollecting == true && isCollecting == true)
                {
                    // Reset the collectible ref if present and was collecting
                    collectibleObj.resourceUnit.isCollecting = false;
                }
                if (issuedByPlayer)
                {
                    collectibleObj.OnDie -= CollectibleDied;
                    collectibleObj = null;
                }
            }
            isCollecting = false;
            collectUpdate = false;

            // Check if storage or collectible is followed/attacked
            // Check if current storage or collectible
            if (thisUnit.target)
            {
                // Check if construction, to fix the overlapping of commands
                if (issuedByPlayer)
                {
                    // We decide if command was done by the player to construct/repair or to collect resources
                    if (thisUnit.target.constructionUnit != null && thisUnit.target.constructionUnit.isBuilding && thisUnit.target.owner == thisUnit.owner)
                    {
                        // If we are carrying resources, when clicked on a storage that is also a construction we check if it needs repairs
                        // If it does not needs repairs we continue the resource collection
                        if (currentHeldResources.Count == 0 || thisUnit.target.constructionUnit.completed == false || thisUnit.target.health < thisUnit.target.maxHealth)
                        {
                            // Construction command, not a resource command
                            constructionCommand = true;
                            return;
                        }
                    }
                }
                constructionCommand = false;

                if (thisUnit.target != collectibleObj && thisUnit.target != storageObj)
                {
                    if (thisUnit.unitState == UnitStates.Follow)
                    {
                        // Check if compatible storage or collectible
                        if (SetCollectible(thisUnit.target))
                        {
                            // If hit collectible change command to attack
                            if (collectibleObj.resourceUnit.hitToCollect) thisUnit.Attack(collectibleObj);
                            // Subscribe to the death of the collectible
                            collectibleObj.OnDie += CollectibleDied;

                            // If collectible is set, but storage is not defined, we should look for one from the current unit position
                            if (storageObj == null) FindStorage();
                        }
                        else if (SetStorage(thisUnit.target))
                        {
                        }
                    }
                    // Cheeck if hit collectible was attacked
                    else if (thisUnit.unitState == UnitStates.Attack && thisUnit.target.resourceUnit && thisUnit.target.resourceUnit.hitToCollect)
                    {
                        // Check if compatible collectible
                        if (SetCollectible(thisUnit.target))
                        {
                            // Subscribe to the death of the collectible
                            collectibleObj.OnDie += CollectibleDied;

                            // If collectible is set, but storage is not defined, we should look for one from the current unit position
                            if (storageObj == null) FindStorage();
                        }
                    }
                }
                else if (thisUnit.target == collectibleObj && collectibleObj.resourceUnit.hitToCollect) thisUnit.Attack(collectibleObj);
            }
        }

        // Collector: Collectible died
        public void CollectibleDied(Unit unitThatDies, int playerKiller, Unit unitKiller, bool rewards)
        {
            collectibleObj.OnDie -= CollectibleDied;

            // Find the similar collectible
            if (FindCollectibleBased())
            {
                if (CanCarryMoreAtCurrentCollectible())
                {
                    // Go to collectible if can carry more
                    if (collectibleObj.resourceUnit.hitToCollect)
                    {
                        thisUnit.Attack(collectibleObj);
                    }
                    else
                    {
                        thisUnit.Follow(collectibleObj);
                    }
                }
                else if (storageObj)
                {
                    // No collectible found, go to storage
                    thisUnit.Follow(storageObj);
                }
            }
            else
            {
                // No collectible found
                // collectibleRef = null;
                // Go to storage
                if (storageObj) thisUnit.Follow(storageObj);
            }
        }

        // Collector: Storage died
        public void StorageDied(Unit unitThatDies, int playerKiller, Unit unitKiller, bool rewards)
        {
            storageObj.OnDie -= StorageDied;

            if (FindStorage())
            {
                if (!collectUpdate)
                {
                    // Storage fund, go there
                    thisUnit.Follow(storageObj);
                }
            }
        }

        // Reference of the storage was changed
        public void StorageReferenceChange(Unit newRef)
        {
            if (storageObj != null)
            {
                storageObj.OnReferenceChange -= StorageReferenceChange;
                storageObj.OnDie -= StorageDied;
            }

            storageObj = newRef;

            if (storageObj != null)
            {
                storageObj.OnReferenceChange += StorageReferenceChange;
                storageObj.OnDie += StorageDied;
            }
        }

        // UNIT ACTIONS ------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Collector: invoked when unit reaches the target
        private void UnitReached()
        {
            // Only for resource collector
            if (!isCollector) return;

            // Target is being constructed
            if (constructionCommand) return;

            // See if target is collectible or storage
            if (thisUnit.target == collectibleObj || SetCollectible(thisUnit.target))
            {
                // Came to collectible
                if (CanCarryMoreAtCurrentCollectible())
                {
                    // If this unit is hitCollectible, we do not collect resources. OnDamageDeal subscribed function HitCollectible is responsible for resource collection in this case
                    if (!collectibleObj.resourceUnit.hitToCollect)
                    {
                        // Test if can carry
                        collectUpdate = true;
                        CollectResources();
                    }
                }
                else GoToStorage();
            }
            else if (thisUnit.target == storageObj || SetStorage(thisUnit.target))
            {
                // Came to storage
                UnloadResources();

                // Animation reset
                if (animationBlendingSet)
                {
                    thisUnit.SetAnimationBlendingIndex(0);
                    animationBlendingSet = false;
                }
            }
        }

        // Collector: Go to storage, or find and go
        public void GoToStorage()
        {
            if (storageObj == null)
            {
                if (!FindStorage())
                {
                    thisUnit.Idle();
                    return;
                }
                thisUnit.Follow(storageObj);
            }
            else thisUnit.Follow(storageObj);
        }

        // Collector: Go to collectible, or find and go
        public void GoToCollectible()
        {
            if (collectibleObj == null)
            {
                if (!FindCollectible())
                {
                    thisUnit.Idle();
                    return;
                }

                if (collectibleObj.resourceUnit.hitToCollect) thisUnit.Attack(collectibleObj);
                else thisUnit.Follow(collectibleObj);

            }
            else if (collectibleObj.resourceUnit.hitToCollect) thisUnit.Attack(collectibleObj);
            else thisUnit.Follow(collectibleObj);
        }

        // UTILS ------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Collector: Adds specified resource to currently held resources
        public void AddCurrentHeldResources(Resource type, int amount)
        {
            // Change the animation blending
            if (!animationBlendingSet)
            {
                for (int i = 0; i < animationBlendingIndex.Length; i++)
                {
                    if (animationBlendingIndex[i].type == null || animationBlendingIndex[i].type == type)
                    {
                        thisUnit.SetAnimationBlendingIndex(animationBlendingIndex[i].blendingIndex);
                        animationBlendingSet = true;
                        break;
                    }
                }
            }

            // If already has a resource of this type, add the value
            for (int i = 0; i < currentHeldResources.Count; i++)
            {
                if (currentHeldResources[i].type == type)
                {
                    // Holds resource of this type, change the amount
                    currentHeldResources[i].value += amount;
                    return;
                }
            }

            // Currently does not hold resoucres of this type, create a new wrapper
            currentHeldResources.Add(new ResourceWrapper(type, amount));
        }

        // Collectible return resource index
        public int GetResourceIndex(Resource resourceForSearch)
        {
            for (int i = 0; i < collectibleResources.Length; i++)
            {
                if (collectibleResources[i].type == resourceForSearch)
                {
                    return i;
                }
            }

            return -1;
        }

        // Returns index of resourceForSearch inside of resourceHolder. -1 if no in the resourceHolder.
        public static int GetResourceIndex(Resource resourceForSearch, ResourceWrapper[] resourceHolder)
        {
            for (int i = 0; i < resourceHolder.Length; i++)
            {
                if (resourceHolder[i].type == resourceForSearch)
                {
                    return i;
                }
            }

            return -1;
        }

        public static int GetResourceIndex(Resource resourceForSearch, Resource[] resourceHolder)
        {
            for (int i = 0; i < resourceHolder.Length; i++)
            {
                if (resourceHolder[i] == resourceForSearch)
                {
                    return i;
                }
            }

            return -1;
        }

        public static int GetResourceIndex(Resource resourceForSearch, List<ResourceWrapper> resourceHolder)
        {
            for (int i = 0; i < resourceHolder.Count; i++)
            {
                if (resourceHolder[i].type == resourceForSearch)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
