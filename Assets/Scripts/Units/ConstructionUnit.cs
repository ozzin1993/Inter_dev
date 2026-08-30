using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    // Defines a unit that either is a constructible building or a worker that can construct a building
    public class ConstructionUnit : MonoBehaviour
    {
        [Header("Building")]
        [Tooltip("True means this unit is a constructible building. False means this unit is a builder")]
        public bool isBuilding;
        [Tooltip("If this building is a finished construction")]
        public bool completed = false; // Upgrades do not change this state
        [Tooltip("Amount of time needed for construction")]
        public float constructionTime;
        [Tooltip("Does it need workers to be built")]
        public bool buildsItself;
        [Tooltip("Can multiple builders work at this building at the same time to build it faster")]
        public bool multipleBuilders;

        // Technical
        Unit thisUnit; // the unit reference of this script

        // Building
        [HideInInspector] public float currentConstructionPercentage = 0; // Current completion percentage of the building
        [HideInInspector] public int buildersWorking; // How many builders are working on this construction
        [HideInInspector] public bool constructionAnimExists; // If construction animation exists

        // Upgradeable building
        [HideInInspector] public ResourceWrapper[] upgradeCost; // cost that is returned to the player if building upgrade gets cancelled
        [HideInInspector] public GameObject originalRenderer; // Link to the main renderer gameobject of the original building
        [HideInInspector] public Unit upgradeBuildingRef; // Reference to upgrade building
        [HideInInspector] public float upgradeTime; // Time needed to upgrade this building

        // Builder
        private float[] accumulatedResourceCost; // Cost for repairing
        [HideInInspector] public bool isWorking = false; // If building is currently working
        [HideInInspector] public Unit buildingObj; // Current building of the builder
        [HideInInspector] public Unit constructionRef; // Building that is about to be built
        [HideInInspector] public Vector3 buildingPosition; // Position of the new building
        [HideInInspector] public Transform shadowBuilding; // When commanded to build keep reference to the shadowBuilding

        // Called by Initialize() of thisUnit
        public void Initialize()
        {
            thisUnit = GetComponent<Unit>();
            thisUnit.OnDie += DieCallback;
            if (thisUnit.animator)
            {
                constructionAnimExists = thisUnit.animator.HasState(0, Animator.StringToHash("construction"));
            }

            if (isBuilding)
            {
                // Building
                if (!completed)
                {
                    thisUnit.SetHP(currentConstructionPercentage * thisUnit.maxHealth);
                    // Animation
                    thisUnit.AnimatorSetBool(AnimationState.Idle, false);
                    if (constructionAnimExists) thisUnit.animator.Play("construction", 0, currentConstructionPercentage);
                }

                if (buildsItself)
                {
                    buildingObj = thisUnit;
                    isWorking = true;
                }
            }
            else
            {
                // Builder
                thisUnit.OnFollowReach += BuildingReachedWrapper;
            }
        }

        // What happens when this unit dies
        private void DieCallback(Unit unitThatDies, int playerKiller, Unit unitKiller, bool rewards)
        {
            // Destroy shadowBuilding
            if (shadowBuilding) Destroy(shadowBuilding.gameObject);
            // If was going to build a building, return the resources
            if (constructionRef) GameResources.Instance.ChangeAmount(thisUnit.owner, constructionRef.resourceCost);
            // If was working, reduce the builders count
            if (buildingObj != null) buildingObj.constructionUnit.buildersWorking--;

            // Clean up
            thisUnit.OnDie -= DieCallback;
            
            if (!isBuilding)
            {
                thisUnit.OnFollowReach -= BuildingReachedWrapper;
                thisUnit.OnCommand -= StopTheConstruction;
            }
            if (constructionRef)
            {
                thisUnit.OnPositionReach -= ConstructionPositionReached;
                thisUnit.OnCommand -= ResetConstructionStates;
            }

            GameManager.Instance.Tick -= RepairUpdate;
            thisUnit.OnCommand -= StopTheRepairs;
        }

        // Update is called once per frame
        void Update()
        {
            Build();
        }

        // Set the parameters that will define the upgrade of this building
        public void UpgradeBuilding(Unit upgradeUnit, float time, ResourceWrapper[] cost)
        {
            // We can only call an upgrade on a completed building
            if (!completed) return;

            thisUnit.Idle();
            // If time 0 - skip
            if (time == 0)
            {
                upgradeBuildingRef = upgradeUnit;
                FinishConstruction();
            }
            else
            {
                // Only for buildings
                if (!isBuilding) return;
                upgradeBuildingRef = upgradeUnit;
                // Builds itself
                buildsItself = true;
                buildingObj = thisUnit;
                isWorking = true;
                // Parameters
                multipleBuilders = false;
                // completed = false; // We should not change the completed state of this unit when upgrading
                thisUnit.isBeingBuilt = true;
                // Time
                upgradeTime = time;
                currentConstructionPercentage = 0;
                // Cost
                this.upgradeCost = cost;
                // Renderers
                originalRenderer = thisUnit.mainRenderer;
                thisUnit.ReplaceRenderers(upgradeUnit, false);

                // ReSelect
                if (Presentation.Selection?.ActiveUnit == thisUnit) Presentation.Selection?.AddToSelection(thisUnit, true, true);
            }
        }

        // Builder or Building: construct the building
        private void Build()
        {
            if (isWorking)
            {
                if (thisUnit.stunned) return;

                // Construction process
                if (buildingObj != null && !buildingObj.dead && buildingObj.isBeingBuilt)
                {
                    if (buildingObj.constructionUnit.currentConstructionPercentage < 1)
                    {
                        if (upgradeBuildingRef)
                        {
                            // Currently upgrading
                            float updatePercentage = Time.deltaTime / buildingObj.constructionUnit.upgradeTime;
                            buildingObj.constructionUnit.currentConstructionPercentage = Mathf.Clamp(buildingObj.constructionUnit.currentConstructionPercentage + updatePercentage, 0f, 1f);
                        }
                        else
                        {
                            // Currently being built
                            float updatePercentage = Time.deltaTime / buildingObj.constructionUnit.constructionTime;
                            buildingObj.constructionUnit.currentConstructionPercentage = Mathf.Clamp(buildingObj.constructionUnit.currentConstructionPercentage + updatePercentage, 0f, 1f);
                            // [Interflow fix 2026-08-29 heal-through-receiver] Набор здоровья ПОСТРОЙКОЙ —
                            // не лечение (решение Artsiom 27.08.2026): множитель получаемого лечения
                            // к нему не применяется. Второе исключение — ремонт, ниже по файлу.
                            buildingObj.ChangeHP(updatePercentage * buildingObj.maxHealth, countAsHeal: false);
                        }

                        // Animation
                        if (buildingObj.animator && buildingObj.constructionUnit.constructionAnimExists && thisUnit.FoWVisible)
                        {
                            buildingObj.animator.Play("construction", 0, buildingObj.constructionUnit.currentConstructionPercentage);
                        }
                    }

                    // Finished construction
                    if (buildingObj.constructionUnit.currentConstructionPercentage >= 1)
                    {
                        // Only server should finish construction and then sync with clients
                        if (!NetworkConnectionHandler.isClient)
                        {
                            FinishConstruction();
                            if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.BuildingFinished(thisUnit);
                        }
                    }

                    // NETWORK: Only for clients - we also should rotate worker towards the building
                    if (NetworkConnectionHandler.isClient && !isBuilding)
                    {
                        thisUnit.LookAt(buildingObj.transform.position);
                    }
                }
                else
                {
                    // Builder stop working on a building; if building set working = false

                    // Network Sync
                    if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.WorkerStopConstructing(buildingObj, thisUnit);

                    isWorking = false;
                    buildingObj = null;
                    thisUnit.AnimatorSetBool(AnimationState.Building, false);
                    if (!isBuilding) thisUnit.Idle();
                }
            }
        }

        // ============================= BUILDER: OnFollowReach =============================

        private void BuildingReachedWrapper()
        {
            // Check if we are just collecting resources
            if (thisUnit.resourceUnit != null && thisUnit.resourceUnit.constructionCommand == false) return;

            // If worker is already working - Needed?
            if (isWorking) return;

            // Not a building or does not belong to the owner
            if (thisUnit.target.constructionUnit == null || !thisUnit.target.constructionUnit.isBuilding || thisUnit.target.owner != thisUnit.owner) return;

            // Check if builder reached the building for repairs
            if (thisUnit.target.constructionUnit.completed)
            {
                // Ownership and Health check
                if (thisUnit.target.health < thisUnit.target.maxHealth)
                {
                    // Resource check
                    if (thisUnit.target.resourceCost.Length != 0)
                    {
                        for (int i = 0; i < thisUnit.target.resourceCost.Length; i++)
                        {
                            if (!thisUnit.target.resourceCost[i].type.limited)
                            {
                                if (!GameResources.Instance.CheckAmount(thisUnit.target.owner, new ResourceWrapper(thisUnit.target.resourceCost[i].type, 1)))
                                {
                                    // Not enough resources to start the repairs
                                    return; 
                                }
                            }
                        }
                    }

                    // Start the repairs
                    StartTheRepairs(thisUnit.target);
                }
            }
            else
            {
                // If not a multiple builders and already working
                if (!thisUnit.target.constructionUnit.multipleBuilders && (thisUnit.target.constructionUnit.buildsItself || thisUnit.target.constructionUnit.buildersWorking > 0)) return;

                // Start the construction
                StartTheConstruction(thisUnit.target);
            }
        }

        // ============================= BUILDER: THE REPAIRS =============================

        // Builder: Starts the repairs
        public void StartTheRepairs(Unit building)
        {
            buildingObj = building;
            buildingObj.constructionUnit.buildersWorking++;
            accumulatedResourceCost = new float[buildingObj.resourceCost.Length];
            thisUnit.AnimatorSetBool(AnimationState.Building, true);
            GameManager.Instance.Tick += RepairUpdate;
            thisUnit.OnCommand += StopTheRepairs;

            if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.WorkerStartTheRepairs(thisUnit, buildingObj);                                     
        }

        // Builder: stops the repairs
        public void StopTheRepairs(bool issuedByPlayer)
        {
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.WorkerStopTheRepairs(thisUnit);
           
            GameManager.Instance.Tick -= RepairUpdate;
            thisUnit.OnCommand -= StopTheRepairs;
            if (buildingObj) buildingObj.constructionUnit.buildersWorking--;
            buildingObj = null;
            accumulatedResourceCost = null;
            thisUnit.AnimatorSetBool(AnimationState.Building, false);
        }

        // Repair process
        private void RepairUpdate()
        {
            if (buildingObj != null && !buildingObj.dead)
            {
                // Accumulate costs
                bool enoughResources = true;
                for (int i = 0; i < buildingObj.resourceCost.Length; i++)
                {
                    if (!buildingObj.resourceCost[i].type.limited)
                    {
                        accumulatedResourceCost[i] += buildingObj.resourceCost[i].value / buildingObj.constructionUnit.constructionTime * GameManager.Instance.currentDeltaTime;

                        // Convert accumulated cost to an integer amount
                        int costToDeduct = Mathf.FloorToInt(accumulatedResourceCost[i]);

                        if (costToDeduct > 0)
                        {
                            ResourceWrapper rw = new ResourceWrapper(buildingObj.resourceCost[i].type, costToDeduct);

                            if (GameResources.Instance.CheckAmount(buildingObj.owner, rw))
                            {
                                // Subtract the integer part
                                GameResources.Instance.ChangeAmount(buildingObj.owner, rw, 1, true, true);
                                // Keep only the remaining fraction
                                accumulatedResourceCost[i] -= costToDeduct;
                            }
                            else
                            {
                                // Net enough resources, end the repair process
                                enoughResources = false;
                            }
                        }
                    }
                }

                if (!enoughResources || buildingObj.health >= buildingObj.maxHealth)
                {
                    // Not enough resources or Full HP, stop repairing
                    thisUnit.Idle();
                }
                else
                {
                    // Repair, not full HP
                    // Increase hp depending on construction time
                    // [Interflow fix 2026-08-29 heal-through-receiver] РЕМОНТ здания рабочим за ресурсы —
                    // тоже не лечение (решение Artsiom 30.08.2026): множитель к нему не применяется.
                    buildingObj.ChangeHP(buildingObj.maxHealth / buildingObj.constructionUnit.constructionTime * GameManager.Instance.currentDeltaTime, countAsHeal: false);
                }
            }
            else
            {
                // Building is dead
                thisUnit.Idle();
            }
        }

        // ============================= BUILDER: THE CONSTRUCTION =============================

        public void StartTheConstruction(Unit building)
        {
            building.constructionUnit.buildersWorking++;
            buildingObj = building;  
            isWorking = true;
            thisUnit.AnimatorSetBool(AnimationState.Building, true);
            thisUnit.OnCommand += StopTheConstruction;

            // Network Sync
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.WorkerStartConstructing(building, thisUnit);
        }

        // Builder: commanded to do something else
        public void StopTheConstruction(bool issuedByPlayer)
        {
            // Network Sync
            if (isWorking && NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.WorkerStopConstructing(buildingObj, thisUnit);

            thisUnit.OnCommand -= StopTheConstruction;
            if (buildingObj != null) buildingObj.constructionUnit.buildersWorking--;
            isWorking = false;
            buildingObj = null;
            thisUnit.AnimatorSetBool(AnimationState.Building, false);
        }

        // Builder: position reached, instantiate building prefab and start working on it
        public void ConstructionPositionReached()
        {
            // Last collision check
            if (constructionRef.CollisionCheck(buildingPosition))
            {
                Presentation.NotifyMsg("Can't build there.", thisUnit.owner, true);
                ResetConstructionStates(false);
                thisUnit.Idle();
                return;
            }

            // Create building that is to be constructed
            Unit newBuilding = Unit.Spawn(constructionRef, buildingPosition, 0, thisUnit.owner);
            if (newBuilding == null) 
            {
                ResetConstructionStates(false);
                thisUnit.Idle();
            }
            else
            {
                ResetConstructionStatesReached();
                // Follow the building, means reach and start building
                StartCoroutine(FollowCommand(newBuilding));
            }
        }

        // Builder, upon reaching the construction site, should follow new building after 1 frame. Because when reaches the position it triggers ConstructionPositionReached() and then Idle() causing the worker to stop the construction.
        IEnumerator FollowCommand(Unit newBuilding)
        {
            yield return null; // Waits until the next frame
            if (newBuilding != null)
            {
                // Make sure it is a construction command
                if (thisUnit.resourceUnit != null) thisUnit.resourceUnit.constructionCommand = true;
                thisUnit.Follow(newBuilding, 0, false, true);
            }
        }

        // Builder: if unit is interrupted while going to construction site, stop construction. Or if building is reached and construction has started, reset the states.
        public void ResetConstructionStates(bool issuedByPlayer)
        {
            // Send info to client
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.ResetConstructionState(thisUnit, false);

            // Resources return
            GameResources.Instance.ChangeAmount(thisUnit.owner, constructionRef.resourceCost); // Return resources

            if (shadowBuilding) Destroy(shadowBuilding.gameObject); // Destroy shadowBUilding
            constructionRef = null;
            thisUnit.OnPositionReach -= ConstructionPositionReached;
            thisUnit.OnCommand -= ResetConstructionStates;
        }

        // Builder: when we start the construction we return limited resources only and reset the states
        public void ResetConstructionStatesReached()
        {
            // Send info to client
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.ResetConstructionState(thisUnit, true);

            // Limited resources return
            if (constructionRef.resourceCost != null)
            {
                for (int i = 0; i < constructionRef.resourceCost.Length; i++)
                {
                    if (constructionRef.resourceCost[i].type.limited) GameResources.Instance.ChangeAmount(thisUnit.owner, constructionRef.resourceCost[i]); // We decrease the resource
                }
            }

            if (shadowBuilding) Destroy(shadowBuilding.gameObject); // Destroy shadowBUilding
            constructionRef = null;
            thisUnit.OnPositionReach -= ConstructionPositionReached;
            thisUnit.OnCommand -= ResetConstructionStates;
        }

        // ============================= BUILDING/BUILDER: THE CONSTRUCTION =============================

        // Building or Worker: Finish the construction of currently being worked building
        public void FinishConstruction()
        {
            if (upgradeBuildingRef)
            {
                // Finished upgrade

                // Replace the original with upgrade
                bool wasSelected = Presentation.Selection?.ActiveUnit == thisUnit;
                float yRotation = thisUnit.GetUnitRotation();

                // We do not use spawn because Use() method is called on clients too
                Unit upgradedUnit = Instantiate(upgradeBuildingRef, thisUnit.transform.position, Quaternion.identity);
                upgradedUnit.SetUnitRotation(yRotation);
                upgradedUnit.SetOwnership(thisUnit.owner);

                // Replace NetID
                upgradedUnit.netID = thisUnit.netID;
                upgradedUnit.spawned = true;
                if (thisUnit.netID != 0) SlotManager.Instance.RemoveNetID(thisUnit.netID, thisUnit);
                SlotManager.Instance.unitNetID.Add(upgradedUnit.netID, upgradedUnit);
                GameManager.Instance.AddUnitCount(upgradedUnit);

                // ConstructionUnit parameters
                ConstructionUnit cu = upgradedUnit.GetComponent<ConstructionUnit>();
                if (cu)
                {
                    upgradedUnit.isBeingBuilt = false;
                    cu.completed = true;
                    cu.currentConstructionPercentage = 1;
                }

                // Copy Waypoint
                upgradedUnit.SetWaypointDirect(thisUnit.waypointUnit, thisUnit.waypointLocation);

                // Initialize
                upgradedUnit.Initialize();

                // Add to selection
                if (wasSelected) Presentation.Selection?.AddToSelection(upgradedUnit, true, true);

                // Replace reference
                thisUnit.OnReferenceChange?.Invoke(upgradedUnit);

                // Return the limited resources used by the Upgrade ability
                if (upgradeCost != null)
                {
                    for (int i = 0; i < upgradeCost.Length; i++)
                    {
                        if (upgradeCost[i].type.limited) GameResources.Instance.ChangeAmount(thisUnit.owner, upgradeCost[i]); // We decrease the limited resource usage
                    }
                }

                // Delete original building
                thisUnit.Die(-1, null, false, false, true);

                // Play upgrade sound
                if (ReferenceManager.Instance.upgradeComplete != null) Presentation.Audio?.PlayVoiceClip(ReferenceManager.Instance.upgradeComplete, 1);
            }
            else
            {
                // Finished construction
                buildingObj.isBeingBuilt = false;
                buildingObj.constructionUnit.completed = true;
                buildingObj.AnimatorSetBool(AnimationState.Idle, true);
                buildingObj.OnRedrawAbilityView?.Invoke();

                GameManager.Instance.Tick += buildingObj.HandleEveryFrameAbilities;
                GameManager.Instance.Tick += buildingObj.CooldownCalculate;

                // Unlock TechTree when this unit is created, if there is something to unlock
                TechnologyManager.Instance.UnlockTech(buildingObj);

                // Resource Production when unit is created. For limited resource it increases the limits
                if (buildingObj.resourceProduced != null)
                {
                    for (int i = 0; i < buildingObj.resourceProduced.Length; i++)
                    {
                        if (buildingObj.resourceProduced[i].type.limited) GameResources.Instance.ChangeLimit(buildingObj.owner, buildingObj.resourceProduced[i]);
                        else GameResources.Instance.ChangeAmount(buildingObj.owner, buildingObj.resourceProduced[i]);
                    }
                }

                // Add to selection
                if (Presentation.Selection?.ActiveUnit == buildingObj) Presentation.Selection?.AddToSelection(buildingObj, true, true);

                // Play construction complete sound
                if (ReferenceManager.Instance.buildingComplete != null) Presentation.Audio?.PlayVoiceClip(ReferenceManager.Instance.buildingComplete, 1);
            }
        }

        // Building: Cancel the construction and return resources per GameManager parameters
        public void CancelConstruction(bool calledByServer)
        {
            if (calledByServer == false)
            {
                // If server we send the data about the cancellation of the building
                if (NetworkConnectionHandler.isClient)
                {
                    NetworkCommandSync.Instance.ConstructionCancelSend(thisUnit);
                    return;
                }
            }

            // We must the cancellation info to clients
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.ConstructionCancel(thisUnit.netID);

            // Building cancelled was being constructed, not upgraded
            if (!upgradeBuildingRef)
            {
                thisUnit.Die(-1, null, false, false);

                // Return the cost
                if (GameManager.Instance.constructionCancelFullReturn)
                {
                    // Full resources return
                    if (thisUnit.resourceCost != null)
                    {
                        for (int i = 0; i < thisUnit.resourceCost.Length; i++)
                        {
                            // For regular resource types we add them back
                            if (!thisUnit.resourceCost[i].type.limited) GameResources.Instance.ChangeAmount(thisUnit.owner, thisUnit.resourceCost[i]);
                        }
                    }
                }
                else
                {
                    // Partial resource return
                    if (thisUnit.resourceCost != null)
                    {
                        for (int i = 0; i < thisUnit.resourceCost.Length; i++)
                        {
                            // For regular resource types we add them back
                            if (!thisUnit.resourceCost[i].type.limited) GameResources.Instance.ChangeAmount(thisUnit.owner, thisUnit.resourceCost[i], 1 - currentConstructionPercentage, false, true);
                        }
                    }
                }
            }
            // Building cancelled was being upgraded
            else
            {
                // Reset the parmeters
                isWorking = false;
                completed = true;
                thisUnit.isBeingBuilt = false;
                upgradeBuildingRef = null;
                // Time
                currentConstructionPercentage = 1;
                // Renderers
                thisUnit.RestoreRenderers();
                // Animation
                thisUnit.AnimatorSetBool(AnimationState.Idle, true);
                // ReSelect
                if (Presentation.Selection?.ActiveUnit == thisUnit) Presentation.Selection?.AddToSelection(thisUnit, true, true);

                // Return the cost
                if (GameManager.Instance.constructionCancelFullReturn)
                {
                    // Full resources return
                    if (upgradeCost != null)
                    {
                        for (int i = 0; i < upgradeCost.Length; i++)
                        {
                            // For regular resource types we add them back
                            if (!upgradeCost[i].type.limited) GameResources.Instance.ChangeAmount(thisUnit.owner, upgradeCost[i]);
                            else GameResources.Instance.ChangeAmount(thisUnit.owner, upgradeCost[i]); // We decrease the limited resource usage
                        }
                    }
                }
                else
                {
                    // Partial resource return
                    if (upgradeCost != null)
                    {
                        for (int i = 0; i < upgradeCost.Length; i++)
                        {
                            // For regular resource types we add them back
                            if (!upgradeCost[i].type.limited) GameResources.Instance.ChangeAmount(thisUnit.owner, upgradeCost[i], 1 - currentConstructionPercentage, false, true);
                            else GameResources.Instance.ChangeAmount(thisUnit.owner, upgradeCost[i]); // We decrease the limited resource usage
                        }
                    }
                }
            }
        }
    }
}
