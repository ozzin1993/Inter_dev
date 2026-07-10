using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Camera_TopDownNS;
using System.Collections;

namespace StrategyCore
{
    public class PlayerControl : MonoBehaviour
    {
        public static PlayerControl instance;
        public static StrategyCoreInput coreInput;

        // Initial technical
        UIManager UImanager;
        public static bool isCursorOverUI;

        [HideInInspector] public PCMode mode = PCMode.Default;
        private ParticleSystem moveVFX;
        private ParticleSystem moveAttackVFX;

        // Selection
        [HideInInspector] public Unit activeUnit;
        [HideInInspector] public List<Unit> selectedUnits = new List<Unit>();

        // Selected units - hotkey (CTRL + number)
        List<Unit>[] quickSelection = new List<Unit>[10];

        bool dragSelect = false;
        Vector2 mousePosition1; // Drag selection initial mouse position
        Rect selectionRect; // Rectangle of drag selection

        float currentTime = 0; // For performance gain when dragSelecting, do selection only specified times per second

        bool dblClickWasPerformedThisFrame = false;
        [HideInInspector] public Unit dblClickUnit = null;

        // Mode
        SpriteRenderer unitProjectorSpawned; // For displaying unit at cursor
        int unitProjectorSize; // To properly display the selection texture
        bool unitProjectorColor; // To properly color the projector

        Transform areaProjectorSpawned; // For displaying area ability
        Transform rangeProjectorSpawned; // For displaying the range of the unit

        Ability activeAbility;
        int activeAbilityIndex; // Ability that is in active phase.
        bool activeIsItem = false; // if active ability is item

        // ShadowBuilding
        [HideInInspector] public Transform shadowBuilding; // When placing a building visual version of it will be stored here
        List<Renderer> shadowBuildingRenderers; // We apply material through here
        float shadowBuildingRadius;
        int shadowBuildingMask;
        bool shadowBuildingIsAir;

        // Constant
        public const float rayDistance = 300; // Distance for raycast for unit selection
        const float groupSelectionMagnitutde = 40; // if pointer moves for more than this value then we are making a group selection

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
            coreInput = new StrategyCoreInput();
        }
        private void OnEnable()
        {
            if (ServerBootstrap.IsHeadlessServer) return;   // [Interflow fix 2026-06-20]
            UImanager = GetComponent<UIManager>();
            coreInput.Enable();
            PlayerControl.coreInput.Main.QuickSelectionAssign.performed += QuickSelectionAssign;
            PlayerControl.coreInput.Main.AnyKey.performed += QuickSelect;
            PlayerControl.coreInput.Main.AnyKeyDbl.performed += QuickSelectDbl;
            PlayerControl.coreInput.Main.Chat.performed += UImanager.ChatButtonPressed;
            if (UIManagerMenu.instance) PlayerControl.coreInput.Main.Menu.performed += ShowInGameMenu;

            Camera_TopDown.onDoubleClick += DblClickSelection;
        }
        private void OnDisable()
        {
            if (ServerBootstrap.IsHeadlessServer) return;   // [Interflow fix 2026-06-20]
            Camera_TopDown.onDoubleClick -= DblClickSelection;

            PlayerControl.coreInput.Main.QuickSelectionAssign.performed -= QuickSelectionAssign;
            PlayerControl.coreInput.Main.AnyKey.performed -= QuickSelect;
            PlayerControl.coreInput.Main.AnyKeyDbl.performed -= QuickSelectDbl;
            PlayerControl.coreInput.Main.Chat.performed -= UImanager.ChatButtonPressed;
            PlayerControl.coreInput.Main.Menu.performed -= ShowInGameMenu;
            coreInput.Disable();
        }

        // Start is called before the first frame update
        void Start()
        {
            if (ServerBootstrap.IsHeadlessServer) return;   // [Interflow fix 2026-06-20]
            // VFX
            moveVFX = Instantiate(ReferenceManager.instance.moveVFX, Vector3.zero, Quaternion.identity);
            moveVFX.gameObject.SetActive(false);
            moveAttackVFX = Instantiate(ReferenceManager.instance.moveAttackVFX, Vector3.zero, Quaternion.identity);
            moveAttackVFX.gameObject.SetActive(false);

            // Healthbar display
            coreInput.Main.HealthBarDisplay.performed += HealthBarDisplay;

            // MiniMap Ping
            coreInput.Main.Select.performed += MiniMapPing;

            // Projector
            UnitProjectorCreate();
        }

        // Update is called once per frame
        void Update()
        {
            if (ServerBootstrap.IsHeadlessServer) return;   // [Interflow fix 2026-06-20]
            // Static bool to know if cursor currently over UI elements
            isCursorOverUI = IsOverUI(Camera_TopDown.instance.GetCursorPosition());

            // When over UI we turn off drag selection and hide the projector.
            // When in projection we show cursor over ui, but hide when not
            bool isProjectionMode;
            if (mode == PCMode.Area || mode == PCMode.Placement) isProjectionMode = true;
            else isProjectionMode = false;

            if (isCursorOverUI)
            {
                dragSelect = false;
                if (Camera_TopDown.instance.cursorHidden) Camera_TopDown.instance.ShowCursor();
                if (mode == PCMode.DragDrop) UnitProjectorUpdate();
                else UnitProjectorHide();
            }
            else
            {
                if (!isProjectionMode) UnitProjectorUpdate();
                else if (!Camera_TopDown.instance.cursorHidden) Camera_TopDown.instance.HideCursor();

                // COMMANDS

                if (mode == PCMode.Default)
                {
                    // SELECTION ---------------------------------------------------------------------------------------------------------------------------------------

                    // If we press the left mouse button, save mouse location and begin selection
                    if (!dblClickWasPerformedThisFrame && coreInput.Main.Select.WasPressedThisFrame())
                    {
                        dragSelect = true;
                        mousePosition1 = Camera_TopDown.instance.GetCursorPosition();
                        currentTime = 0;
                    }

                    if (dragSelect == true)
                    {
                        Vector2 mousePosition2 = Camera_TopDown.instance.GetCursorPosition();
                        // Check if group selection is occuring
                        if (coreInput.Main.Select.IsPressed())
                        {
                            //SelectUnits();
                            if ((mousePosition1 - mousePosition2).magnitude > groupSelectionMagnitutde)
                            {
                                RectSelection();
                            }
                            // For GUI
                            else selectionRect = Utils.GetScreenRect(mousePosition1, mousePosition2);
                        }

                        // If we let go of the left mouse button, end selection
                        if (coreInput.Main.Select.WasReleasedThisFrame())
                        {
                            dragSelect = false;
                            if ((mousePosition1 - mousePosition2).magnitude > groupSelectionMagnitutde)
                            {
                                //SelectUnits();
                                RectSelection(true);
                            }
                            else if (!dblClickWasPerformedThisFrame)
                            {
                                // Single unit selection
                                Unit selectedUnit = Utils.GetUnitAtCursor();

                                if (selectedUnit && selectedUnit.isSelectable)
                                {
                                    if (FogOfWar.instance.IsVisible(selectedUnit.FoWCell, SlotManager.instance.currentTeam) && selectedUnit.IsVisible(SlotManager.instance.currentTeam))
                                    {
                                        // Add to selection only visible units
                                        if (coreInput.Main.MultipleSelection.IsPressed())
                                        {
                                            if (selectedUnits.Contains(selectedUnit)) RemoveFromSelection(selectedUnit);
                                            else AddToSelection(selectedUnit, false);
                                        }
                                        else AddToSelection(selectedUnit, true);
                                    }
                                }
                            }
                        }
                    }

                    // The following are commands that can be performed only if we have selected units
                    if (selectedUnits.Count == 0) { }

                    // ATTACK LOCATION ---------------------------------------------------------------------------------------------------------------------------------------

                    else if (coreInput.Main.AttackPosition.IsPressed() && coreInput.Main.Command.WasReleasedThisFrame())
                    {
                        // Attack position
                        Vector3 terrainPoint = Utils.TerrainScreenRaycast(Camera_TopDown.instance.GetCursorPosition());

                        bool someoneCommanded = false;
                        for (int i = 0; i < selectedUnits.Count; i++)
                        {
                            // Control only player units
                            if (selectedUnits[i].owner != SlotManager.instance.currentPlayer && !SlotManager.instance.debugMode) continue;

                            if (selectedUnits[i].isSplash && !selectedUnits[i].isBeingBuilt)
                            {
                                selectedUnits[i].Attack(new Vector2(terrainPoint.x, terrainPoint.z), true);
                                someoneCommanded = true;
                            }
                        }
                        // Attack sound
                        if (someoneCommanded && activeUnit.attackCommandSound.Length > 0) SoundFXManager.instance.PlayCommandSound(activeUnit, activeUnit.attackCommandSound, 1);
                    }

                    // COMMANDS ---------------------------------------------------------------------------------------------------------------------------------------

                    else if (coreInput.Main.Command.WasReleasedThisFrame())
                    {
                        // Cast ray to see if units was selected to be followed/attacked or we are moving to a position
                        Ray ray = Camera.main.ScreenPointToRay(Camera_TopDown.instance.GetCursorPosition());
                        RaycastHit hit;

                        if (Physics.Raycast(ray, out hit, rayDistance))
                        {
                            Unit hitUnit = hit.transform.GetComponent<Unit>();
                            bool staticCopy = hit.transform.tag == "StaticDestructible" ? true : false;
                            bool soundPlayOnce = false;
                            bool wasCommanded = false;

                            // Player commanded (RightClick on unit) to Follow/Attack unit
                            if (hitUnit && hitUnit.IsVisible(SlotManager.instance.currentTeam) && (hitUnit.unitType == UnitType.StaticDestructible || hitUnit.unitType == UnitType.Tree || FogOfWar.instance.IsVisible(hitUnit.FoWCell, SlotManager.instance.currentTeam)))
                            {
                                for (int i = 0; i < selectedUnits.Count; i++)
                                {
                                    // Control only player units
                                    if (selectedUnits[i].owner != SlotManager.instance.currentPlayer && !SlotManager.instance.debugMode) continue;

                                    // Waypoint - Unit
                                    if (selectedUnits[i].isWaypoint)
                                    {
                                        if (staticCopy) selectedUnits[i].SetWaypoint(new Vector2(hitUnit.transform.position.x, hitUnit.transform.position.z));
                                        else selectedUnits[i].SetWaypoint(hitUnit);
                                        wasCommanded = true; // To make move command
                                        soundPlayOnce = false; // Not to play move VFX
                                    }

                                    // Commands
                                    else if (!selectedUnits[i].isBeingBuilt)
                                    {
                                        // Enemy unit was selected, try to attack it
                                        if (selectedUnits[i].canAttack && UnitSelector.IsUnitCompatible(selectedUnits[i].owner, hitUnit, selectedUnits[i].attackUnitSelector) && (selectedUnits[i].team != hitUnit.team && hitUnit.team != (int)Teams.NeutralPassive))
                                        {
                                            // if not self
                                            if (selectedUnits[i] != hitUnit)
                                            {
                                                // Range or can move
                                                if (selectedUnits[i].canMove || Vector2.Distance(new Vector2(selectedUnits[i].transform.position.x, selectedUnits[i].transform.position.z), new Vector2(hitUnit.transform.position.x, hitUnit.transform.position.z)) <= selectedUnits[i].attackRange)
                                                {
                                                    selectedUnits[i].Attack(hitUnit, true);
                                                    if (!soundPlayOnce && activeUnit.attackCommandSound.Length > 0) SoundFXManager.instance.PlayCommandSound(activeUnit, activeUnit.attackCommandSound, 1);
                                                    soundPlayOnce = true;
                                                    wasCommanded = true;
                                                }
                                                else
                                                {
                                                    UIManager.instance.ShowNotifyMsg("This unit is too far!", SlotManager.instance.currentPlayer, false);
                                                }
                                            }
                                        }
                                        // NonEnemy unit, or can not attack, try to follow
                                        else if (hitUnit.isSelectable)
                                        {
                                            wasCommanded = true;
                                            // Enemy unit is invulnerable
                                            if (hitUnit.isInvulnerable && hitUnit.team != SlotManager.instance.currentTeam && hitUnit.team != SlotManager.instance.playerTeam[(int)Players.NeutralPassive])
                                            {
                                                UIManager.instance.ShowNotifyMsg("This unit is invulnerable!", SlotManager.instance.currentPlayer, false);
                                            }
                                            // Friendly unit, follow it
                                            else
                                            {
                                                if (selectedUnits[i] != hitUnit)
                                                {
                                                    if (!soundPlayOnce && activeUnit.moveSound.Length > 0) SoundFXManager.instance.PlayCommandSound(activeUnit, activeUnit.moveSound, 1);
                                                    soundPlayOnce = true;

                                                    // Transport: Either unit to enter transport or transport to take in, depending on active unit
                                                    if ((selectedUnits[i].transportUnit || hitUnit.transportUnit) && hitUnit.owner == selectedUnits[i].owner && coreInput.Main.CommandDblClick.WasPerformedThisFrame())
                                                    {
                                                        selectedUnits[i].Follow(hitUnit, 0, true, true);
                                                    }
                                                    else
                                                    {
                                                        selectedUnits[i].Follow(hitUnit, 0, false, true);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            if (!wasCommanded)
                            {
                                // Move to position
                                Vector3 point = Utils.TerrainScreenRaycast(Camera_TopDown.instance.GetCursorPosition());
                                List<Vector2> destinations = ComputeFormation(selectedUnits, point);

                                // Move units that can move and belong to the player
                                int destIndex = 0; // Destination index. If we have selected enemy units too, we should give destionations only for own units
                                for (int i = 0; i < selectedUnits.Count; i++)
                                {
                                    if (selectedUnits[i].owner == SlotManager.instance.currentPlayer || SlotManager.instance.debugMode)
                                    {
                                        // Waypoint - Position
                                        if (selectedUnits[i].isWaypoint)
                                        {
                                            selectedUnits[i].SetWaypoint(new Vector2(point.x, point.z));
                                        }
                                        // Move command
                                        else if (selectedUnits[i].canMove && !selectedUnits[i].isBeingBuilt)
                                        {
                                            selectedUnits[i].Move(destinations[destIndex], 0, true);
                                            if (!soundPlayOnce && activeUnit.moveSound.Length > 0) SoundFXManager.instance.PlayCommandSound(activeUnit, activeUnit.moveSound, 1);
                                            soundPlayOnce = true;
                                            destIndex++;
                                        }
                                    }
                                }

                                // VFX
                                if (soundPlayOnce && destinations.Count > 0 && !staticCopy) SetMoveVFX(point);
                            }
                        }
                    }
                }

                // ABILITY MODES ---------------------------------------------------------------------------------------------------------------------------------------

                // The following commnands below can be done only if we have active unit
                else if (activeUnit == null && coreInput.Main.Select.WasReleasedThisFrame())
                {
                    ChangeMode(PCMode.Default);
                }

                // Area ability
                else if (mode == PCMode.Area)
                {
                    Vector3 currentPlanePosition = Utils.TerrainScreenRaycast(Camera_TopDown.instance.GetCursorPosition());
                    if (currentPlanePosition == Vector3.zero) currentPlanePosition = Utils.PlaneRayCast(Camera_TopDown.instance.GetCursorPosition());

                    areaProjectorSpawned.position = currentPlanePosition;

                    if (coreInput.Main.Select.WasReleasedThisFrame())
                    {
                        ActivateAbilityAndResetStates(areaProjectorSpawned.position);
                    }
                }
                // Location ability
                else if (mode == PCMode.Position)
                {
                    if (coreInput.Main.Select.WasReleasedThisFrame())
                    {
                        ActivateAbilityAndResetStates(Utils.TerrainScreenRaycast(Camera_TopDown.instance.GetCursorPosition()));
                    }
                }
                // Unit ability
                else if (mode == PCMode.Unit)
                {
                    if (coreInput.Main.Select.WasReleasedThisFrame())
                    {
                        Unit hitUnit = Utils.GetUnitAtCursor();

                        if (hitUnit && hitUnit.IsVisible(SlotManager.instance.currentTeam) && (hitUnit.unitType == UnitType.StaticDestructible || hitUnit.unitType == UnitType.Tree || FogOfWar.instance.IsVisible(hitUnit.FoWCell, SlotManager.instance.currentTeam)))
                        {
                            if (UnitSelector.IsUnitCompatible(activeUnit.owner, hitUnit, activeAbility.unitSelector) && FogOfWar.instance.IsVisible(hitUnit.FoWCell, SlotManager.instance.currentTeam))
                            {
                                // Only compatible and visible units can be targeted
                                ActivateAbilityAndResetStates(hitUnit);
                            }
                            else
                            {
                                UImanager.ShowNotifyMsg("You must select a compatible unit", SlotManager.instance.currentPlayer, false);
                            }
                        }
                        else
                        {
                            UImanager.ShowNotifyMsg("You must select a compatible unit", SlotManager.instance.currentPlayer, false);
                        }
                    }
                }
                // Item drag&drop
                else if (mode == PCMode.DragDrop)
                {
                    // Handled by UIManager.cs
                }
                // Shop unit change
                else if (mode == PCMode.ShopUnit)
                {
                    if (coreInput.Main.Select.WasReleasedThisFrame())
                    {
                        Unit hitUnit = Utils.GetUnitAtCursor();

                        if (hitUnit)
                        {
                            // We check if unit is visible to make sure that it can not be abused to locate enemy units
                            if (hitUnit.IsVisible(SlotManager.instance.currentTeam) && FogOfWar.instance.IsVisible(hitUnit.FoWCell, SlotManager.instance.currentTeam))
                            {
                                if (hitUnit.team == SlotManager.instance.currentTeam && hitUnit.InventorySize > 0)
                                {
                                    if (Vector2.Distance(new Vector2(hitUnit.transform.position.x, hitUnit.transform.position.z), new Vector2(activeUnit.transform.position.x, activeUnit.transform.position.z)) <= GameManager.instance.shopRadius)
                                    {
                                        activeUnit.shopUnit = hitUnit;
                                        ChangeMode(PCMode.Default);
                                    }
                                    else
                                    {
                                        UImanager.ShowNotifyMsg("This unit is too far", SlotManager.instance.currentPlayer, false);
                                    }
                                }
                                else
                                {
                                    UImanager.ShowNotifyMsg("You can only select unit that has inventory and belongs to you or your allies", SlotManager.instance.currentPlayer, false);
                                }
                            }
                            else
                            {
                                UImanager.ShowNotifyMsg("You must select a compatible unit", SlotManager.instance.currentPlayer, false);
                            }
                        }
                        else
                        {
                            UImanager.ShowNotifyMsg("You must select a compatible unit", SlotManager.instance.currentPlayer, false);
                        }
                    }
                }
                // AttackMove command
                else if (mode == PCMode.AttackMove)
                {
                    if (coreInput.Main.Select.WasReleasedThisFrame())
                    {
                        bool positionSelection = true; // Attack move on unit will just command to attack
                        bool staticCopy = false; // Main unit is dead, but static copy is still there. Commanded to attack, we just move to its position
                        bool soundPlayOnce = false;
                        Ray ray = Camera.main.ScreenPointToRay(Camera_TopDown.instance.GetCursorPosition());
                        RaycastHit hit;

                        // Check if attack command was made
                        if (Physics.Raycast(ray, out hit, rayDistance))
                        {
                            Unit hitUnit = hit.transform.GetComponent<Unit>();
                            staticCopy = hit.transform.tag == "StaticDestructible" ? true : false;
                            if (hitUnit && hitUnit.IsVisible(SlotManager.instance.currentTeam) && (hitUnit.unitType == UnitType.StaticDestructible || hitUnit.unitType == UnitType.Tree || FogOfWar.instance.IsVisible(hitUnit.FoWCell, SlotManager.instance.currentTeam)))
                            {
                                if (UnitSelector.IsUnitCompatible(activeUnit.owner, hitUnit, activeUnit.attackUnitSelector))
                                {
                                    positionSelection = false;

                                    for (int i = 0; i < selectedUnits.Count; i++)
                                    {
                                        // Control only player units
                                        if (selectedUnits[i].owner != SlotManager.instance.currentPlayer && !SlotManager.instance.debugMode) continue;

                                        if (selectedUnits[i] != hitUnit && !selectedUnits[i].isBeingBuilt)
                                        {
                                            // Range or can move
                                            if (selectedUnits[i].canMove || Vector2.Distance(new Vector2(selectedUnits[i].transform.position.x, selectedUnits[i].transform.position.z), new Vector2(hitUnit.transform.position.x, hitUnit.transform.position.z)) <= selectedUnits[i].attackRange)
                                            {
                                                selectedUnits[i].Attack(hitUnit, true);

                                                if (!soundPlayOnce && activeUnit.attackCommandSound.Length > 0) SoundFXManager.instance.PlayCommandSound(activeUnit, activeUnit.attackCommandSound, 1);
                                                soundPlayOnce = true;
                                            }
                                            else
                                            {
                                                UIManager.instance.ShowNotifyMsg("This unit is too far!", SlotManager.instance.currentPlayer, false);
                                            }
                                        }
                                    }
                                }
                                else if (hitUnit.isSelectable)
                                {
                                    positionSelection = false;
                                    if (hitUnit.isInvulnerable)
                                    {
                                        UIManager.instance.ShowNotifyMsg("This unit is invulnerable!", SlotManager.instance.currentPlayer, false);
                                    }
                                    else
                                    {
                                        UIManager.instance.ShowNotifyMsg("Selected unit can not attack this target!", SlotManager.instance.currentPlayer, false);
                                    }
                                }
                            }

                            // AttackMove to position was made or staticCopy was selected
                            if (positionSelection || staticCopy)
                            {
                                // Move to position
                                Vector3 point = Utils.TerrainScreenRaycast(Camera_TopDown.instance.GetCursorPosition());
                                List<Vector2> destinations = ComputeFormation(selectedUnits, point);

                                if (!staticCopy && destinations.Count > 0) SetMoveVFX(point, true);

                                // Move units that can move and belong to the player
                                int destIndex = 0; // Destination index. If we have selected enemy units too, we should give destionations only for own units
                                for (int i = 0; i < selectedUnits.Count; i++)
                                {
                                    if (selectedUnits[i].owner == SlotManager.instance.currentPlayer || SlotManager.instance.debugMode)
                                    {
                                        if (selectedUnits[i].canMove && !selectedUnits[i].isBeingBuilt)
                                        {
                                            if (staticCopy) selectedUnits[i].Move(destinations[destIndex], 0, true);
                                            else selectedUnits[i].AttackMove(destinations[destIndex], true);
                                            destIndex++;

                                            if (!soundPlayOnce && activeUnit.moveSound.Length > 0) SoundFXManager.instance.PlayCommandSound(activeUnit, activeUnit.moveSound, 1);
                                            soundPlayOnce = true;
                                        }
                                    }
                                }
                            }
                        }

                        ChangeMode(PCMode.Default);
                    }
                }
            }

            // Cancel / Deselect
            if (mode != PCMode.Default && coreInput.Main.Command.WasReleasedThisFrame())
            {
                ChangeMode(PCMode.Default);
            }

            if (coreInput.Main.Cancel.WasReleasedThisFrame())
            {
                // Deselect only if we are not using any abilities. If we are using any abilities we first reset the mode.
                if (mode == PCMode.Default)
                {
                    DeselectAll();
                }
                ChangeMode(PCMode.Default);
            }

            if (dblClickWasPerformedThisFrame) dblClickWasPerformedThisFrame = false;
        }

        private void LateUpdate()
        {
            if (!isCursorOverUI)
            {
                // Area ability
                // Must be in LateUpdate to make sure that color is set for next frame and is not getting reset by the unit`s OverlayColorReset function
                if (mode == PCMode.Area)
                {
                    foreach (var unit in Utils.GetUnitsInRadius(new Vector2(areaProjectorSpawned.position.x, areaProjectorSpawned.position.z), areaProjectorSpawned.localScale.x, activeUnit.owner, activeAbility.unitSelector))
                    {
                        unit.SetOverlayColor(StateColors.Affected);
                    }
                }
                // Building placement
                else if (mode == PCMode.Placement)
                {
                    bool invalidPosition = false;

                    // Central raycast
                    Vector3 currentPlanePosition = Utils.TerrainScreenRaycast(Camera_TopDown.instance.GetCursorPosition(), shadowBuildingMask);
                    if (currentPlanePosition == Vector3.zero)
                    {
                        invalidPosition = true;
                        currentPlanePosition = Utils.PlaneRayCast(Camera_TopDown.instance.GetCursorPosition());
                    }

                    // Check terrain, slope
                    if (!invalidPosition)
                    {
                        if (shadowBuilding.GetComponent<CapsuleCollider>())
                        {
                            // Test 6 point around the center of the capsule collider
                            float capRadius = shadowBuilding.GetComponent<CapsuleCollider>().radius;

                            for (int i = 0; i < 6; i++)
                            {
                                Vector2 pointOnCircle = Utils.PointOnCircle(new Vector2(currentPlanePosition.x, currentPlanePosition.z), capRadius + Utils.agentTypeRadius, i * 0.1666f);

                                Vector3 terrainPosition = Utils.TerrainRaycastByPosition(pointOnCircle, shadowBuildingMask);
                                if (terrainPosition == Vector3.zero)
                                {
                                    invalidPosition = true;
                                    break;
                                }
                                else if (pointOnCircle.x < 0 || pointOnCircle.x > Grid.instance.width || pointOnCircle.x > Grid.instance.height || pointOnCircle.y < 0 || pointOnCircle.y > Grid.instance.width || pointOnCircle.y > Grid.instance.height)
                                {
                                    // Outside the map
                                    invalidPosition = true;
                                    break;
                                }
                                else
                                {
                                    // Slope cheeck
                                    if (Mathf.Abs(currentPlanePosition.y - terrainPosition.y) > Utils.maxSlope)
                                    {
                                        invalidPosition = true;
                                        break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Test 4 corners of boxCollider
                            Vector2[] corners = Utils.GetRectangleFromCollider(shadowBuilding, Utils.agentTypeRadius);
                            float[] cornerHeights = new float[4];

                            for (int i = 0; i < corners.Length; i++)
                            {
                                cornerHeights[i] = Utils.GetTerrainHeight(corners[i], shadowBuildingMask);
                                if (cornerHeights[i] == -9999)
                                {
                                    invalidPosition = true;
                                    break;
                                }
                                else if (corners[i].x < 0 || corners[i].x > Grid.instance.width || corners[i].x > Grid.instance.height || corners[i].y < 0 || corners[i].y > Grid.instance.width || corners[i].y > Grid.instance.height)
                                {
                                    // Outside the map
                                    invalidPosition = true;
                                    break;
                                }
                                else
                                {
                                    // Slope check
                                    if (Mathf.Abs(currentPlanePosition.y - cornerHeights[i]) > Utils.maxSlope)
                                    {
                                        invalidPosition = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    // Rotate - Turned off
                    // if (coreInput.Main.Select.IsPressed())
                    // {
                    //     rotatingShadowBuilding = true;
                    //     shadowBuilding.LookAt(new Vector3(currentPlanePosition.x, shadowBuilding.transform.position.x, currentPlanePosition.z));
                    // }
                    // else shadowBuilding.position = currentPlanePosition;

                    shadowBuilding.position = currentPlanePosition;

                    // Check the intersections with units
                    foreach (var unit in Utils.GetAllUnitsInRadius(new Vector2(shadowBuilding.position.x, shadowBuilding.position.z), shadowBuildingRadius, !shadowBuildingIsAir, !shadowBuildingIsAir, shadowBuildingIsAir))
                    {
                        // We will inform the player only if these collided units are visible
                        if (unit.FoWVisible)
                        {
                            unit.SetOverlayColor(StateColors.Invalid);
                            invalidPosition = true;
                        }
                    }

                    // Collider tests
                    if (!invalidPosition)
                    {
                        RaycastHit[] hits;

                        if (shadowBuilding.GetComponent<CapsuleCollider>())
                        {
                            // Capsule Collider - Check against units/buildings/objects; Air not included
                            hits = Physics.SphereCastAll(new Vector3(shadowBuilding.position.x, Utils.raycastPointY, shadowBuilding.position.z), shadowBuildingRadius, Vector3.down, Utils.raycastPointY * 5f, LayerMask.GetMask("Default"));
                        }
                        else
                        {
                            // Box Collider - Check against units/buildings/objects; Air not included
                            BoxCollider boxCollider = shadowBuilding.GetComponent<BoxCollider>();
                            hits = Physics.BoxCastAll(new Vector3(shadowBuilding.position.x, Utils.raycastPointY, shadowBuilding.position.z), boxCollider.size * 0.5f * shadowBuilding.localScale.x, Vector3.down, shadowBuilding.rotation, Utils.raycastPointY * 5f, LayerMask.GetMask("Default"));
                        }

                        Unit temp;
                        for (int i = 0; i < hits.Length; i++)
                        {
                            temp = hits[i].transform.GetComponent<Unit>();
                            if (temp == null)
                            {
                                // Not a unit, show invalid position
                                invalidPosition = true;
                                break;
                            }
                            else
                            {
                                // We will inform the player only if these collided units are visible
                                if (temp.FoWVisible)
                                {
                                    invalidPosition = true;
                                    break;
                                }
                            }
                        }
                    }

                    // Apply Color
                    if (invalidPosition)
                    {
                        foreach (var renderer in shadowBuildingRenderers)
                        {
                            Material[] materials = renderer.materials;
                            for (int i = 0; i < materials.Length; i++) materials[i] = ReferenceManager.instance.shadowMaterialInvalid;
                            renderer.materials = materials;
                        }
                    }
                    else
                    {
                        foreach (var renderer in shadowBuildingRenderers)
                        {
                            Material[] materials = renderer.materials;
                            for (int i = 0; i < materials.Length; i++) materials[i] = ReferenceManager.instance.shadowMaterial;
                            renderer.materials = materials;
                        }
                    }

                    ConstructionProjectorUpdate(shadowBuilding, shadowBuildingRadius, invalidPosition);

                    if (coreInput.Main.Select.WasReleasedThisFrame())
                    {
                        if (!invalidPosition)
                        {
                            if (!activeUnit.constructionUnit || activeUnit.constructionUnit.isBuilding)
                            {
                                // Not a builder
                                UImanager.ShowNotifyMsg("The selected unit is not a builder, add ConstructionUnit.cs and set isBuilding to false");
                                return;
                            }

                            // Activate the ability or item
                            ActivateAbilityAndResetStates(shadowBuilding.position);
                        }
                        else
                        {
                            UImanager.ShowNotifyMsg("Cant build there");
                        }
                    }
                }
            }

            // Reset mode
            if (mode == PCMode.ResetNextFrame) mode = PCMode.Default;
        }

        // GUI -------------------------------------------------------------------------------------------------------------------------------------------------------

        void OnGUI()
        {
            // Drag selection
            if (dragSelect && !isCursorOverUI)
            {
                // Create a rect from both mouse positions
                Utils.DrawScreenRect(selectionRect, new Color(0.8f, 0.8f, 0.95f, 0.25f));
                Utils.DrawScreenRectBorder(selectionRect, 2, new Color(0.8f, 0.8f, 0.95f));
            }
        }

        void OnDrawGizmos()
        {
            if (shadowBuilding)
            {
                BoxCollider boxCollider = shadowBuilding.GetComponent<BoxCollider>();
                Vector3 boxCenter = boxCollider.bounds.center;

                Gizmos.color = Color.red;
                // Or solid cube:
                Gizmos.DrawCube(boxCenter, boxCollider.size);
            }

            // For debugging rect selection - not used
            // int minX, minY, maxX, maxY;
            // for (int x = minX; x <= maxX; x++)
            // {
            //     for (int y = minY; y <= maxY; y++)
            //     {
            //         Gizmos.color = new Color(0.8f, 0.8f, 0.95f, 0.25f);
            //         Gizmos.DrawCube(new Vector3(x * Grid.instance.chunkSize + Grid.instance.chunkSize * 0.5f, 0, y * Grid.instance.chunkSize + Grid.instance.chunkSize * 0.5f), new Vector3(Grid.instance.chunkSize, 0.1f, Grid.instance.chunkSize));
            //     }
            // }
        }

        // UNITS -------------------------------------------------------------------------------------------------------------------------------------------------------

        public List<Vector2> ComputeFormation(List<Unit> units, Vector3 formationCenter)
        {
            Vector2 center = new Vector2(formationCenter.x, formationCenter.z);

            int n = units.Count;
            List<Vector2> result = new List<Vector2>(new Vector2[n]);

            if (n == 0)
                return result;

            float gap = 0.1f;

            //-------------------------------------------------
            // AVERAGE POSITION -> MOVE DIRECTION
            //-------------------------------------------------
            Vector2 avg = Vector2.zero;

            for (int i = 0; i < n; i++)
            {
                avg += new Vector2(
                    units[i].transform.position.x,
                    units[i].transform.position.z);
            }

            avg /= n;

            // formation faces from units toward destination
            Vector2 moveDir = center - avg;

            if (moveDir.sqrMagnitude < 0.0001f)
                moveDir = Vector2.up;

            moveDir.Normalize();

            //-------------------------------------------------
            // GRID SIZE
            //-------------------------------------------------
            int cols = Mathf.CeilToInt(Mathf.Sqrt(n));
            int rows = Mathf.CeilToInt((float)n / cols);

            //-------------------------------------------------
            // SORT UNITS (highest priority at front center)
            //-------------------------------------------------
            List<(Unit unit, int index)> sorted = new List<(Unit, int)>();

            for (int i = 0; i < n; i++)
                sorted.Add((units[i], i));

            sorted.Sort((a, b) =>
            {
                int compare = b.unit.formationPriority.CompareTo(a.unit.formationPriority);

                if (compare == 0)
                    compare = a.unit.netID.CompareTo(b.unit.netID);

                return compare;
            });

            //-------------------------------------------------
            // SLOT ORDER
            // Front row first, center first
            //-------------------------------------------------
            List<Vector2Int> slots = new List<Vector2Int>();

            float centerCol = (cols - 1) * 0.5f;

            for (int r = 0; r < rows; r++)
            {
                int unitsInRow = (r == rows - 1)
                    ? n - (rows - 1) * cols
                    : cols;

                int startCol = (cols - unitsInRow) / 2;

                List<int> rowCols = new List<int>();

                for (int i = 0; i < unitsInRow; i++)
                    rowCols.Add(startCol + i);

                rowCols.Sort((a, b) =>
                {
                    float da = Mathf.Abs(a - centerCol);
                    float db = Mathf.Abs(b - centerCol);
                    return da.CompareTo(db);
                });

                foreach (int c in rowCols)
                    slots.Add(new Vector2Int(c, r));
            }

            //-------------------------------------------------
            // SLOT SIZES
            //-------------------------------------------------
            float[] colWidth = new float[cols];
            float[] rowHeight = new float[rows];

            for (int i = 0; i < n; i++)
            {
                Unit u = sorted[i].unit;
                Vector2Int slot = slots[i];

                float size = u.unitRadius * 2f;

                if (size > colWidth[slot.x])
                    colWidth[slot.x] = size;

                if (size > rowHeight[slot.y])
                    rowHeight[slot.y] = size;
            }

            //-------------------------------------------------
            // LOCAL POSITIONS
            //-------------------------------------------------
            float totalWidth = 0f;
            for (int c = 0; c < cols; c++)
                totalWidth += colWidth[c];

            totalWidth += gap * (cols - 1);

            float totalHeight = 0f;
            for (int r = 0; r < rows; r++)
                totalHeight += rowHeight[r];

            totalHeight += gap * (rows - 1);

            float[] localX = new float[cols];
            float[] localY = new float[rows];

            float xPos = -totalWidth * 0.5f;

            for (int c = 0; c < cols; c++)
            {
                localX[c] = xPos + colWidth[c] * 0.5f;
                xPos += colWidth[c] + gap;
            }

            // front row should be nearest target direction
            float yPos = totalHeight * 0.5f;

            for (int r = 0; r < rows; r++)
            {
                localY[r] = yPos - rowHeight[r] * 0.5f;
                yPos -= rowHeight[r] + gap;
            }

            //-------------------------------------------------
            // ROTATION BASIS
            //-------------------------------------------------
            Vector2 forward = moveDir;
            Vector2 right = new Vector2(forward.y, -forward.x);

            //-------------------------------------------------
            // ASSIGN
            //-------------------------------------------------
            for (int i = 0; i < n; i++)
            {
                var data = sorted[i];
                var slot = slots[i];

                float x = localX[slot.x];
                float y = localY[slot.y];

                // rotate local formation into move direction
                Vector2 offset = right * x + forward * y;

                result[data.index] = center + offset;
            }

            return result;
        }

        // SELECTION -------------------------------------------------------------------------------------------------------------------------------------------------------

        private void DblClickSelection(Vector2 position)
        {
            Unit selectedUnit = Utils.GetUnitAtCursor();
            if (selectedUnit != null && dblClickUnit == selectedUnit && selectedUnit.isSelectable && selectedUnit.owner == SlotManager.instance.currentPlayer)
            {
                RectSelection(true, selectedUnit.unitTypeID);

                dragSelect = false;
                dblClickWasPerformedThisFrame = true;
            }

            dblClickUnit = null;
        }

        // Slow way of Rectange selection, for debugging only.
        void SelectUnits()
        {
            DeselectAll();

            Vector2 mousePosition2 = Camera_TopDown.instance.GetCursorPosition();
            selectionRect = Utils.GetScreenRect(mousePosition1, mousePosition2);

            // Get selection rays from the camera
            Ray bottomLeftRay = Utils.cachedMainCamera.ScreenPointToRay(mousePosition1);
            Ray topLeftRay = Utils.cachedMainCamera.ScreenPointToRay(new Vector3(mousePosition1.x, mousePosition2.y));
            Ray topRightRay = Utils.cachedMainCamera.ScreenPointToRay(mousePosition2);
            Ray bottomRightRay = Utils.cachedMainCamera.ScreenPointToRay(new Vector3(mousePosition2.x, mousePosition1.y));

            // Extend the selection into the world (e.g., 100 units deep)
            float selectionDepth = Utils.raycastPointY * 2;
            Vector3[] frustumCorners = new Vector3[4];

            frustumCorners[0] = bottomLeftRay.GetPoint(selectionDepth);
            frustumCorners[1] = topLeftRay.GetPoint(selectionDepth);
            frustumCorners[2] = topRightRay.GetPoint(selectionDepth);
            frustumCorners[3] = bottomRightRay.GetPoint(selectionDepth);

            Vector3 camPos = Utils.cachedMainCamera.transform.position;

            // Ensure correct clockwise sorting (reorders if needed)
            frustumCorners = Utils.SortVectorsClockwiseXZ(frustumCorners);

            // Debug Draw Selection Frustum
            Debug.DrawRay(camPos, frustumCorners[0] - camPos, Color.red);
            Debug.DrawRay(camPos, frustumCorners[1] - camPos, Color.green);
            Debug.DrawRay(camPos, frustumCorners[2] - camPos, Color.blue);
            Debug.DrawRay(camPos, frustumCorners[3] - camPos, Color.yellow);

            foreach (Unit unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
            {
                Vector3 unitPos = unit.transform.position;

                if (Utils.PyramidCheck(unitPos, frustumCorners, unit.unitRadius))
                {
                    AddToSelection(unit, false, true);
                }
            }
        }

        // Performs calculations for Rect selection
        void RectSelection(bool skip = false, int unitType = 0)
        {
            // We are selecting units of certain type, full visible screen selection
            Vector2 mousePosition2;
            if (unitType != 0)
            {
                mousePosition1 = new Vector2(0, 0);
                mousePosition2 = new Vector2(Screen.width, Screen.height);
                selectionRect = Utils.GetScreenRect(new Vector2(0, 0), new Vector2(Screen.width, Screen.height));
            }
            else
            {
                // For GUI also, so should update every frame
                mousePosition2 = Camera_TopDown.instance.GetCursorPosition();
                selectionRect = Utils.GetScreenRect(mousePosition1, mousePosition2);
            }

            // If selection is at the exact point, skip
            if (selectionRect.xMin == selectionRect.xMax && selectionRect.yMin == selectionRect.yMax) return;

            // Play only serveral times a second, for performance
            if (!skip)
            {
                currentTime -= Time.deltaTime;
                if (currentTime < 0.01f) currentTime = 0.1f;
                else return;
            }

            // For rect selection we do some manual adding and removing of the units from the selectedUnits list, this is because of performance issues when doing it regular way
            bool multipleSelection = coreInput.Main.MultipleSelection.IsPressed();

            // Get selection rays from the camera
            Ray bottomLeftRay = Utils.cachedMainCamera.ScreenPointToRay(mousePosition1);
            Ray topLeftRay = Utils.cachedMainCamera.ScreenPointToRay(new Vector3(mousePosition1.x, mousePosition2.y));
            Ray topRightRay = Utils.cachedMainCamera.ScreenPointToRay(mousePosition2);
            Ray bottomRightRay = Utils.cachedMainCamera.ScreenPointToRay(new Vector3(mousePosition2.x, mousePosition1.y));

            // Extend the selection into the world (e.g., 100 units deep)
            float selectionDepth = Utils.raycastPointY * 2;
            Vector3[] frustumCorners = new Vector3[4];

            frustumCorners[0] = bottomLeftRay.GetPoint(selectionDepth);
            frustumCorners[1] = topLeftRay.GetPoint(selectionDepth);
            frustumCorners[2] = topRightRay.GetPoint(selectionDepth);
            frustumCorners[3] = bottomRightRay.GetPoint(selectionDepth);

            Vector3 camPos = Utils.cachedMainCamera.transform.position;

            // Ensure correct clockwise sorting (reorders if needed)
            frustumCorners = Utils.SortVectorsClockwiseXZ(frustumCorners);

            // Corners of the selection box, clockwise
            Vector3[] corners = new Vector3[4];

            RaycastHit hit;
            if (Physics.Raycast(camPos, frustumCorners[0] - camPos, out hit, Utils.raycastPointY * 5f, Utils.terrainMaskVisuals)) corners[0] = hit.point;
            if (Physics.Raycast(camPos, frustumCorners[1] - camPos, out hit, Utils.raycastPointY * 5f, Utils.terrainMaskVisuals)) corners[1] = hit.point;
            if (Physics.Raycast(camPos, frustumCorners[2] - camPos, out hit, Utils.raycastPointY * 5f, Utils.terrainMaskVisuals)) corners[2] = hit.point;
            if (Physics.Raycast(camPos, frustumCorners[3] - camPos, out hit, Utils.raycastPointY * 5f, Utils.terrainMaskVisuals)) corners[3] = hit.point;

            // 2D corners counter-clockwise
            Vector2[] corners2D = new Vector2[4];
            corners2D[0] = new Vector2(corners[0].x, corners[0].z);
            corners2D[1] = new Vector2(corners[1].x, corners[1].z);
            corners2D[2] = new Vector2(corners[2].x, corners[2].z);
            corners2D[3] = new Vector2(corners[3].x, corners[3].z);

            // We get the chunks that are intersected by the selection box
            // Convert corners to grid coordinates

            // minChunkX
            int minX;
            if (corners2D[0].x < corners2D[3].x) minX = Coordinate.GetChunkByPosition(corners2D[0]).x - 1;
            else minX = Coordinate.GetChunkByPosition(corners2D[3]).x - 1;
            if (minX < 0) minX = 0;

            // maxChunkX
            int maxX;
            if (corners2D[1].x > corners2D[3].x) maxX = Coordinate.GetChunkByPosition(corners2D[1]).x + 1;
            else maxX = Coordinate.GetChunkByPosition(corners2D[3]).x + 1;
            if (maxX >= Grid.chunkCountX) maxX = Grid.chunkCountX - 1;

            // minChunkY
            int minY;
            if (corners2D[2].y < corners2D[3].y) minY = Coordinate.GetChunkByPosition(corners2D[2]).y - 1;
            else minY = Coordinate.GetChunkByPosition(corners2D[3]).y - 1;
            if (minY < 0) minY = 0;

            // maxChunkY
            int maxY;
            if (corners2D[0].y > corners2D[1].y) maxY = Coordinate.GetChunkByPosition(corners2D[0]).y + 1;
            else maxY = Coordinate.GetChunkByPosition(corners2D[1]).y + 1;
            if (maxY >= Grid.chunkCountY) maxY = Grid.chunkCountY - 1;

            // If we rect select the not own unit, we can select only 1 unit
            // If during the same rect selection own units are in rect, we select only own units
            Unit notOwnUnit = null;
            Unit building = null;

            List<Unit> newSelection = new List<Unit>();

            if (true)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        int chunkIndex = x + y * Grid.chunkCountX;

                        for (int i = 0; i < Grid.chunkUnits[chunkIndex].Count; i++)
                        {
                            // Skip if multiple selection and already selected
                            if (multipleSelection && selectedUnits.Contains(Grid.chunkUnits[chunkIndex][i])) continue;

                            // Add to selection only visible units
                            if (Grid.chunkUnits[chunkIndex][i].isSelectable && FogOfWar.instance.IsVisible(Grid.chunkUnits[chunkIndex][i].FoWCell, SlotManager.instance.currentTeam) && Grid.chunkUnits[chunkIndex][i].IsVisible(SlotManager.instance.currentTeam))
                            {
                                Vector3 pos = Grid.chunkUnits[chunkIndex][i].transform.position;

                                if (Utils.PyramidCheck(pos, frustumCorners, Grid.chunkUnits[chunkIndex][i].unitRadius))
                                {
                                    // Skip if not own
                                    if (Grid.chunkUnits[chunkIndex][i].owner != SlotManager.instance.currentPlayer)
                                    {
                                        if (notOwnUnit != null && notOwnUnit.unitType == UnitType.Unit) continue;
                                        notOwnUnit = Grid.chunkUnits[chunkIndex][i];
                                        continue;
                                    }

                                    // If selection is of certain type
                                    if (unitType != 0)
                                    {
                                        if (Grid.chunkUnits[chunkIndex][i].unitTypeID != unitType) continue;
                                    }
                                    // Skip if building
                                    else if (Grid.chunkUnits[chunkIndex][i].unitType != UnitType.Unit)
                                    {
                                        building = Grid.chunkUnits[chunkIndex][i];
                                        continue;
                                    }

                                    newSelection.Add(Grid.chunkUnits[chunkIndex][i]);
                                }
                            }
                        }
                    }
                }
            }

            // Multiple selection
            if (multipleSelection)
            {
                // Add to selection
                for (int i = 0; i < newSelection.Count; i++)
                {
                    newSelection[i].ProjectorsEnable();
                    selectedUnits.Add(newSelection[i]);
                }

                if (activeUnit == null)
                {
                    // New active unit
                    activeUnit = newSelection[0];
                    UImanager.SubscribeToUnit();

                    // Click sound only when rect selection finished
                    if (activeUnit.owner == SlotManager.instance.currentPlayer || SlotManager.instance.debugMode)
                    {
                        if (skip && activeUnit.clickSound.Length > 0) SoundFXManager.instance.PlayCommandSound(activeUnit, activeUnit.clickSound, 1);
                    }
                }
            }
            // Standard selection
            else
            {
                // Replace to not own unit if we are not selecting own units
                if (newSelection.Count == 0)
                {
                    newSelection.Clear();
                    if (building != null) newSelection.Add(building);
                    else if (notOwnUnit != null) newSelection.Add(notOwnUnit);
                }

                // Disable Projectors
                for (int i = 0; i < selectedUnits.Count; i++)
                {
                    if (!newSelection.Contains(selectedUnits[i]))
                    {
                        selectedUnits[i].ProjectorsDisable();
                    }
                }

                // Enable projectors
                for (int i = 0; i < newSelection.Count; i++)
                {
                    if (!selectedUnits.Contains(newSelection[i]))
                    {
                        newSelection[i].ProjectorsEnable();
                    }
                }

                // Set active unit
                if (newSelection.Count > 0)
                {
                    if (activeUnit == null)
                    {
                        // New active unit
                        activeUnit = newSelection[0];
                        UImanager.SubscribeToUnit();
                    }
                    else if (activeUnit != newSelection[0])
                    {
                        // Replace
                        UImanager.UnsubscribeToUnit(activeUnit);
                        UImanager.ResetUnitUI();

                        activeUnit = newSelection[0];
                        UImanager.SubscribeToUnit();
                    }

                    selectedUnits = newSelection;

                    // Click sound
                    if (activeUnit.owner == SlotManager.instance.currentPlayer || SlotManager.instance.debugMode)
                    {
                        if (skip && activeUnit.clickSound.Length > 0) SoundFXManager.instance.PlayCommandSound(activeUnit, activeUnit.clickSound, 1);
                    }
                }
                else
                {
                    // No selection was made, it means we clear all selected units
                    if (activeUnit != null) DeselectAll();
                }
            }
        }

        public IEnumerator AddToSelectionDelayed(Unit newUnit, bool replaceSelection, bool noSound = false)
        {
            yield return null;

            if (newUnit) AddToSelection(newUnit, replaceSelection, noSound);
        }

        public void AddToSelection(Unit newUnit, bool replaceSelection, bool noSound = false)
        {
            // [Interflow fix 2026-06-26 путь1] UImanager берётся в OnEnable (пропущен на headless) → на сервере null.
            // Выделение — клиентская презентация: no-op по своему состоянию, а не по флагу сервера.
            if (UImanager == null) return;
            // Multiple selection OFF
            if (replaceSelection) DeselectAll();
            else if (selectedUnits.Contains(newUnit)) return;

            selectedUnits.Add(newUnit);

            // Selection circle
            newUnit.ProjectorsEnable();

            if (activeUnit != null) UImanager.UnsubscribeToUnit(activeUnit);
            activeUnit = newUnit;
            UImanager.SubscribeToUnit();

            // Click sound 
            if (!noSound && (activeUnit.owner == SlotManager.instance.currentPlayer || SlotManager.instance.debugMode))
            {
                if (activeUnit.clickSound.Length > 0) SoundFXManager.instance.PlayCommandSound(activeUnit, activeUnit.clickSound, 1);
            }
        }

        public void RemoveFromSelection(Unit unit)
        {
            // [Interflow fix 2026-06-26 путь1] На сервере UImanager == null (OnEnable пропущен) → выходим: списка выделения и UI-подписок нет.
            if (UImanager == null) return;
            selectedUnits.Remove(unit);

            // Selection circle
            unit.ProjectorsDisable();

            if (activeUnit != null && activeUnit == unit)
            {
                HideRangeProjector();
                UImanager.UnsubscribeToUnit(unit);
                UImanager.ResetUnitUI();
                activeUnit = null;

                if (selectedUnits.Count > 0)
                {
                    activeUnit = selectedUnits[0];
                    UImanager.SubscribeToUnit();
                }
            }
        }

        // Quick selection replace
        private void ReplaceSelected(int index)
        {
            if (quickSelection[index] != null && quickSelection[index].Count > 0)
            {
                // Remove null units from quick selection
                List<Unit> tempList = new List<Unit>();
                for (int i = 0; i < quickSelection[index].Count; i++) if (quickSelection[index][i] != null && quickSelection[index][i].owner == SlotManager.instance.currentPlayer) tempList.Add(quickSelection[index][i]);

                // Copy to quick selection and check if any units to select left
                quickSelection[index] = new List<Unit>(tempList);
                if (quickSelection[index].Count > 0)
                {
                    DeselectAll();

                    selectedUnits = new List<Unit>(quickSelection[index]);

                    for (int i = 0; i < selectedUnits.Count; i++)
                    {
                        selectedUnits[i].ProjectorsEnable();
                    }

                    activeUnit = selectedUnits[0];
                    if (activeUnit.clickSound.Length > 0) SoundFXManager.instance.PlayCommandSound(activeUnit, activeUnit.clickSound, 1);
                    UImanager.SubscribeToUnit();
                }
            }
        }

        // Clear selection
        void DeselectAll()
        {
            for (int i = 0; i < selectedUnits.Count; i++)
            {
                selectedUnits[i].ProjectorsDisable();
            }

            selectedUnits.Clear();

            if (activeUnit != null)
            {
                UImanager.UnsubscribeToUnit(activeUnit);
                UImanager.ResetUnitUI();
                activeUnit = null;
            }
        }

        // Quick unit selection
        void QuickSelectionAssign(InputAction.CallbackContext ctx)
        {
            // Assign
            if (activeUnit != null)
            {
                if (Keyboard.current.digit1Key.isPressed) quickSelection[1] = new List<Unit>(selectedUnits);
                else if (Keyboard.current.digit2Key.isPressed) quickSelection[2] = new List<Unit>(selectedUnits);
                else if (Keyboard.current.digit3Key.isPressed) quickSelection[3] = new List<Unit>(selectedUnits);
                else if (Keyboard.current.digit4Key.isPressed) quickSelection[4] = new List<Unit>(selectedUnits);
                else if (Keyboard.current.digit5Key.isPressed) quickSelection[5] = new List<Unit>(selectedUnits);
                else if (Keyboard.current.digit6Key.isPressed) quickSelection[6] = new List<Unit>(selectedUnits);
                else if (Keyboard.current.digit7Key.isPressed) quickSelection[7] = new List<Unit>(selectedUnits);
                else if (Keyboard.current.digit8Key.isPressed) quickSelection[8] = new List<Unit>(selectedUnits);
                else if (Keyboard.current.digit9Key.isPressed) quickSelection[9] = new List<Unit>(selectedUnits);
                else if (Keyboard.current.digit0Key.isPressed) quickSelection[0] = new List<Unit>(selectedUnits);
            }
        }

        void QuickSelect(InputAction.CallbackContext ctx)
        {
            if (UIManager.instance.chatON) return;

            // Select
            if (Keyboard.current.digit1Key.isPressed) ReplaceSelected(1);
            else if (Keyboard.current.digit2Key.isPressed) ReplaceSelected(2);
            else if (Keyboard.current.digit3Key.isPressed) ReplaceSelected(3);
            else if (Keyboard.current.digit4Key.isPressed) ReplaceSelected(4);
            else if (Keyboard.current.digit5Key.isPressed) ReplaceSelected(5);
            else if (Keyboard.current.digit6Key.isPressed) ReplaceSelected(6);
            else if (Keyboard.current.digit7Key.isPressed) ReplaceSelected(7);
            else if (Keyboard.current.digit8Key.isPressed) ReplaceSelected(8);
            else if (Keyboard.current.digit9Key.isPressed) ReplaceSelected(9);
            else if (Keyboard.current.digit0Key.isPressed) ReplaceSelected(0);
        }

        void QuickSelectDbl(InputAction.CallbackContext ctx)
        {
            // Move camera to active unit
            if (activeUnit != null)
            {
                Camera_TopDown.instance.SetPosition(activeUnit.transform.position);
            }
        }

        public bool IsSelected(Unit unit)
        {
            // [Interflow fix 2026-06-26 путь1] Гейт не нужен: selectedUnits всегда инициализирован и на сервере пуст → Contains вернёт false естественным образом.
            return selectedUnits.Contains(unit);
        }

        // MODES -------------------------------------------------------------------------------------------------------------------------------------------------------

        // Modes. Various modes that allow to use items and abilities.
        public void ChangeMode(PCMode newMode) // Generic mode change, can used for reset
        {
            if (newMode == PCMode.Default) // Reset
            {
                // Cancel item DragDrop
                if (mode == PCMode.DragDrop) UIManager.instance.ItemDragCancel();

                if (areaProjectorSpawned != null) GameObject.Destroy(areaProjectorSpawned.gameObject);

                HideRangeProjector();
                UnitProjectorHide();

                if (shadowBuilding) GameObject.Destroy(shadowBuilding.gameObject);

                UIManager.instance.HideCancelButton();
                Camera_TopDown.instance.ShowCursor();

                UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
            else if (newMode == PCMode.DragDrop) // Item drag&drop
            {
                dragSelect = false;
            }
            else if (newMode == PCMode.ShopUnit) // Shop unit change
            {
                UnityEngine.Cursor.SetCursor(ReferenceManager.instance.modeCursor, new Vector2(64, 64), CursorMode.Auto);
            }
            else if (newMode == PCMode.AttackMove) // AttackMove command
            {
                UnityEngine.Cursor.SetCursor(ReferenceManager.instance.modeCursor, new Vector2(64, 64), CursorMode.Auto);
            }

            if (mode != PCMode.Default && newMode != PCMode.Default) ChangeMode(PCMode.Default);
            mode = newMode;
            activeAbilityIndex = -1;
            activeAbility = null;
        }

        public void ChangeMode(PCMode newMode, float areaRadius, int abilityIndex, bool isItem, Ability ability) // Mode 1 accepts areaRadius
        {
            // Reset previous
            if (mode != PCMode.Default && newMode != PCMode.Default) ChangeMode(PCMode.Default);

            // Set mode
            mode = newMode;

            // Set what is currently active
            activeAbility = ability;
            activeAbilityIndex = abilityIndex;
            activeIsItem = isItem;

            // Center cursor
            // if (IsOverUI(Camera_TopDown.instance.GetCursorPosition())) Camera_TopDown.instance.SetCursorPosition(true);

            // Area selection
            if (newMode == PCMode.Area)
            {
                CreateAreaSelector(areaRadius);
                Camera_TopDown.instance.HideCursor();
            }
            // Unit/Position selection
            else if (newMode == PCMode.Unit || newMode == PCMode.Position)
            {
                UnityEngine.Cursor.SetCursor(ReferenceManager.instance.modeCursor, new Vector2(64, 64), CursorMode.Auto);
            }
            // Building placement
            else if (newMode == PCMode.Placement)
            {
                Camera_TopDown.instance.HideCursor();
                //CreateAreaSelector(areaRadius);

                // Network isOwner: create duplicate of the building that is to be built for collision checks
                if (abilityIndex != -1)
                {
                    Construction constructionAbility = (Construction)Utils.GetAbilityByIndexName(activeUnit, activeAbilityIndex.ToString(), out int _);
                    shadowBuilding = CreateShadowBuilding(constructionAbility.building[activeUnit.abilityLevel[abilityIndex]]);
                }
            }
        }

        // UTILS -------------------------------------------------------------------------------------------------------------------------------------------------------

        // InGame Menu handling
        public void ShowInGameMenu(InputAction.CallbackContext ctx)
        {
            UIManagerMenu.instance.ShowUIDocument();
        }

        // Toggles HealthBar display

        private void HealthBarDisplay(InputAction.CallbackContext context)
        {
            Camera.main.cullingMask ^= 1 << LayerMask.NameToLayer("Healthbar");
        }

        // Ping on minimap
        private void MiniMapPing(InputAction.CallbackContext context)
        {
            // Ping on ground
            bool overUI = IsOverUI(Camera_TopDown.instance.GetCursorPosition());

            if (!overUI && coreInput.Main.MiniMapPing.IsPressed())
            {
                Vector3 clickPosition = Camera_TopDown.instance.PlaneRayCast(Camera_TopDown.instance.GetCursorPosition());

                UIManager.instance.WorldToMiniMapPing(clickPosition.x / Grid.instance.width, clickPosition.z / Grid.instance.height);
            }
        }

        private void SetMoveVFX(Vector3 position, bool moveAttack = false)
        {
            if (moveAttack)
            {
                moveVFX.gameObject.SetActive(false);
                moveAttackVFX.gameObject.SetActive(true);
                moveAttackVFX.transform.position = position + new Vector3(0, 0.05f, 0); // offset
                moveAttackVFX.Play();
            }
            else
            {
                moveAttackVFX.gameObject.SetActive(false);
                moveVFX.gameObject.SetActive(true);
                moveVFX.transform.position = position + new Vector3(0, 0.05f, 0); // offset
                moveVFX.Play();
            }
        }

        // Creates building that is visible to current player only. Deletes components except renderers
        private Transform CreateShadowBuilding(Unit building)
        {
            Unit unitCopy = GameObject.Instantiate(building);
            shadowBuildingRadius = unitCopy.unitRadius;
            shadowBuildingIsAir = unitCopy.isAir;

            // Create projector
            ConstructionProjectorUpdate(unitCopy.transform, unitCopy.unitRadius, false);

            // Raycast mask
            shadowBuildingMask = Utils.groundMask;
            if ((unitCopy.isGround && unitCopy.isWater) || unitCopy.isAir) shadowBuildingMask = Utils.terrainMask;
            else if (unitCopy.isWater) shadowBuildingMask = Utils.waterMask;

            // Collider
            if (unitCopy.GetComponent<BoxCollider>() == null && unitCopy.GetComponent<CapsuleCollider>() == null)
            {
                CapsuleCollider capsuleCollider = unitCopy.gameObject.AddComponent<CapsuleCollider>();
                capsuleCollider.radius = unitCopy.unitRadius;
                capsuleCollider.height = unitCopy.unitHeight;
                capsuleCollider.center = new Vector3(0, unitCopy.unitHeight * 0.5f, 0);
            }

            // Remove Components
            Utils.UnitRemoveComponents(unitCopy);

            // Assign renderers
            Renderer[] renderers = unitCopy.GetComponentsInChildren<Renderer>();
            shadowBuildingRenderers = new List<Renderer>();

            foreach (var renderer in renderers)
            {
                if (renderer.name != "HealthBar(Clone)" &&
                    renderer.name != "MiniMapIcon(Clone)" &&
                    renderer.name != "SelectionQuad(Clone)" &&
                    renderer.name != "RangeProjector(Clone)" &&
                    renderer.name != "VFXHolder" &&
                    renderer.name != "MiniMapIcon")
                {
                    // Turn off shadows and change material
                    shadowBuildingRenderers.Add(renderer);
                    Material[] materials = renderer.materials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = ReferenceManager.instance.shadowMaterial;
                    renderer.materials = materials;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                else
                {
                    Destroy(renderer);
                }
            }

            // Change layer to ground to avoid collision detection with itself
            unitCopy.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");

            return unitCopy.transform;
        }

        // Activates currently active ability and resets the mode
        // activeAbilityIndex -2 means we are selecting a position for AttackMove command
        private void ActivateAbilityAndResetStates(Vector3 position)
        {
            // Activate the ability or item
            if (activeAbilityIndex == -2)
            {
                // AttackMove
                if (!activeUnit.isBeingBuilt) activeUnit.AttackMove(new Vector2(position.x, position.z), true);
            }
            else
            {
                // item or ability

                // For placement a little bit hacky, but works. Display erro msg, if successfull clear it, if not msg either changes to error msg or stays as is.
                if (mode == PCMode.Placement) UIManager.instance.ShowNotifyMsg("Can't do that.", SlotManager.instance.currentPlayer, false);

                bool success = activeUnit.UseAbilityItem(activeAbilityIndex, activeIsItem, null, position, true);

                if (mode == PCMode.Placement)
                {
                    if (success)
                    {
                        // Save shadowBuilding
                        if (activeUnit.owner == SlotManager.instance.currentPlayer)
                        {
                            if (activeUnit.constructionUnit.constructionRef != null)  // if constructionRef is null we already reached the destination
                            {
                                activeUnit.constructionUnit.shadowBuilding = shadowBuilding;
                                shadowBuilding = null;
                            }
                        }
                        UIManager.instance.ShowNotifyMsg("", SlotManager.instance.currentPlayer, false);
                    }
                    else
                    {
                        return;
                    }
                }
            }

            // Reset states
            ChangeMode(PCMode.Default);
        }

        // Activates currently active ability and resets the mode
        private void ActivateAbilityAndResetStates(Unit unit)
        {
            // Activate the ability or item
            activeUnit.UseAbilityItem(activeAbilityIndex, activeIsItem, unit, Vector3.zero, true);

            // Reset states
            ChangeMode(PCMode.Default);
        }

        // If the curesor is overUI
        public bool IsOverUI(Vector2 cursorPosition)
        {
            if (EventSystem.current != null)
            {
                Vector2 mousePositionCorrected = UIManager.instance.CursorToUIposition();
                VisualElement picked = UIManager.instance.uiDocument.rootVisualElement.panel.Pick(mousePositionCorrected);

                // Check if the cursor is over ui
                if (picked != null)
                {
                    return true;
                }
                return false;

                // Code below does not work in some cases, don`t know why. Bug?
                // Check if over UI element
                // var click_results = new List<RaycastResult>();
                // var click_data = new PointerEventData(EventSystem.current);
                // click_data.position = cursorPosition;
                // EventSystem.current.RaycastAll(click_data, click_results);
                // 
                // if (click_results.Count == 0)
                // {
                //     return false;
                // }
                // else
                // {
                //     return true;
                // }
            }
            else
            {
                return false;
            }
        }

        // UNIT PROJECTORS --------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        public void UnitProjectorCreate()
        {
            unitProjectorSpawned = Instantiate(ReferenceManager.instance.selectionRenderer);

            unitProjectorSpawned.sprite = ReferenceManager.instance.selectionSmall;
            unitProjectorSize = 0;
            unitProjectorSpawned.color = ReferenceManager.instance.selectionAtCursorColor;
            unitProjectorColor = false;

            UnitProjectorHide();
        }

        public void UnitProjectorHide()
        {
            if (unitProjectorSpawned.gameObject.activeSelf) unitProjectorSpawned.gameObject.SetActive(false);
        }

        public void UnitProjectorUpdate()
        {
            Unit unitAtCursor = Utils.GetUnitAtCursor();

            if (unitAtCursor && unitAtCursor.isSelectable && unitAtCursor != activeUnit && FogOfWar.instance.IsVisible(unitAtCursor.FoWCell, SlotManager.instance.currentTeam) && unitAtCursor.IsVisible(SlotManager.instance.currentTeam))
            {
                if (!unitProjectorSpawned.gameObject.activeSelf)
                {
                    unitProjectorSpawned.gameObject.SetActive(true);
                }

                // Sprite
                if (unitAtCursor.unitRadius > Utils.largeSelectorSize)
                {
                    if (unitProjectorSize != 2)
                    {
                        unitProjectorSpawned.sprite = ReferenceManager.instance.selectionLarge;
                        unitProjectorSize = 2;
                    }
                }
                else if (unitAtCursor.unitRadius > Utils.mediumSelectorSize)
                {
                    if (unitProjectorSize != 1)
                    {
                        unitProjectorSpawned.sprite = ReferenceManager.instance.selectionMedium;
                        unitProjectorSize = 1;
                    }
                }
                else if (unitProjectorSize != 0)
                {
                    unitProjectorSpawned.sprite = ReferenceManager.instance.selectionSmall;
                    unitProjectorSize = 0;
                }

                // Color
                if (SlotManager.instance.currentTeam != unitAtCursor.team && unitAtCursor.team != (int)Teams.NeutralPassive)
                {
                    if (!unitProjectorColor)
                    {
                        unitProjectorSpawned.color = ReferenceManager.instance.selectionEnemyColor;
                        unitProjectorColor = true;
                    }
                }
                else if (unitProjectorColor)
                {
                    unitProjectorSpawned.color = ReferenceManager.instance.selectionAtCursorColor;
                    unitProjectorColor = false;
                }

                // Trasform
                unitProjectorSpawned.transform.localScale = new Vector3(unitAtCursor.unitRadius, unitAtCursor.unitRadius, 1);
                unitProjectorSpawned.transform.position = unitAtCursor.transform.position + new Vector3(0, 0.11f, 0);
            }
            else UnitProjectorHide();
        }

        public void ConstructionProjectorUpdate(Transform obj, float radius, bool colorRed)
        {
            if (!unitProjectorSpawned.gameObject.activeSelf)
            {
                // Radius
                unitProjectorSpawned.transform.localScale = new Vector3(radius, radius, 1);

                unitProjectorSpawned.gameObject.SetActive(true);
            }

            // Sprite
            if (radius > Utils.largeSelectorSize)
            {
                if (unitProjectorSize != 2)
                {
                    unitProjectorSpawned.sprite = ReferenceManager.instance.selectionLarge;
                    unitProjectorSize = 2;
                }
            }
            else if (radius > Utils.mediumSelectorSize)
            {
                if (unitProjectorSize != 1)
                {
                    unitProjectorSpawned.sprite = ReferenceManager.instance.selectionMedium;
                    unitProjectorSize = 1;
                }
            }
            else if (unitProjectorSize != 0)
            {
                unitProjectorSpawned.sprite = ReferenceManager.instance.selectionSmall;
                unitProjectorSize = 0;
            }

            // Color
            if (colorRed)
            {
                if (!unitProjectorColor)
                {
                    unitProjectorSpawned.color = ReferenceManager.instance.selectionEnemyColor;
                    unitProjectorColor = true;
                }
            }
            else if (unitProjectorColor)
            {
                unitProjectorSpawned.color = ReferenceManager.instance.selectionAtCursorColor;
                unitProjectorColor = false;
            }

            // Trasform
            unitProjectorSpawned.transform.position = obj.transform.position + new Vector3(0, 0.1f, 0);
        }

        public void CreateRangeProjector()
        {
            if (rangeProjectorSpawned != null) GameObject.Destroy(rangeProjectorSpawned.gameObject);

            rangeProjectorSpawned = Instantiate(ReferenceManager.instance.rangeProjector);
            rangeProjectorSpawned.position = activeUnit.transform.position;
            rangeProjectorSpawned.parent = activeUnit.transform;
            ProjectorHelper.SetSize(rangeProjectorSpawned, activeUnit.attackRange);
        }

        public void HideRangeProjector()
        {
            if (rangeProjectorSpawned != null) GameObject.Destroy(rangeProjectorSpawned.gameObject);
        }

        // For abilities that accept area as target
        private void CreateAreaSelector(float areaRadius)
        {
            if (areaProjectorSpawned != null) GameObject.Destroy(areaProjectorSpawned.gameObject);

            areaProjectorSpawned = GameObject.Instantiate(ReferenceManager.instance.areaProjector).transform;
            areaProjectorSpawned.localScale = new Vector3(areaRadius, areaRadius, areaRadius); // For storing area radius

            areaProjectorSpawned.position = Utils.TerrainScreenRaycast(Camera_TopDown.instance.GetCursorPosition());

            ProjectorHelper.SetSize(areaProjectorSpawned, areaRadius);
        }

        // ---------------------------------------- RESET ----------------------------------------
        // for scene reset

        public void Reset()
        {
            // [Interflow fix 2026-06-26 путь1] На сервере UImanager == null (OnEnable пропущен) → выходим: сбрасывать клиентское выделение/UI нечего (DeselectAll обращается к UImanager).
            if (UImanager == null) return;
            DeselectAll();
            quickSelection = new List<Unit>[10];
        }
    }
}
