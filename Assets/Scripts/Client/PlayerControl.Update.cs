using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Camera_TopDownNS;
using System.Collections;

namespace StrategyCore
{
    // PlayerControl.Update.cs — жизненный цикл и ввод (Awake..OnDrawGizmos, Update/LateUpdate). Вырезано 1:1 из PlayerControl.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class PlayerControl
    {
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
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
            if (UIManagerMenu.Instance) PlayerControl.coreInput.Main.Menu.performed += ShowInGameMenu;

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
            moveVFX = Instantiate(ReferenceManager.Instance.moveVFX, Vector3.zero, Quaternion.identity);
            moveVFX.gameObject.SetActive(false);
            moveAttackVFX = Instantiate(ReferenceManager.Instance.moveAttackVFX, Vector3.zero, Quaternion.identity);
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
            isCursorOverUI = IsOverUI(Camera_TopDown.Instance.GetCursorPosition());

            // When over UI we turn off drag selection and hide the projector.
            // When in projection we show cursor over ui, but hide when not
            bool isProjectionMode;
            if (mode == PCMode.Area || mode == PCMode.Placement) isProjectionMode = true;
            else isProjectionMode = false;

            if (isCursorOverUI)
            {
                dragSelect = false;
                if (Camera_TopDown.Instance.cursorHidden) Camera_TopDown.Instance.ShowCursor();
                if (mode == PCMode.DragDrop) UnitProjectorUpdate();
                else UnitProjectorHide();
            }
            else
            {
                if (!isProjectionMode) UnitProjectorUpdate();
                else if (!Camera_TopDown.Instance.cursorHidden) Camera_TopDown.Instance.HideCursor();

                // COMMANDS

                if (mode == PCMode.Default)
                {
                    // SELECTION ---------------------------------------------------------------------------------------------------------------------------------------

                    // If we press the left mouse button, save mouse location and begin selection
                    if (!dblClickWasPerformedThisFrame && coreInput.Main.Select.WasPressedThisFrame())
                    {
                        dragSelect = true;
                        mousePosition1 = Camera_TopDown.Instance.GetCursorPosition();
                        currentTime = 0;
                    }

                    if (dragSelect == true)
                    {
                        Vector2 mousePosition2 = Camera_TopDown.Instance.GetCursorPosition();
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
                                    if (FogOfWar.Instance.IsVisible(selectedUnit.FoWCell, SlotManager.Instance.currentTeam) && selectedUnit.IsVisible(SlotManager.Instance.currentTeam))
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

                    // Прямого управления юнитами нет (решение Artsiom 09.09): игрок задаёт режим «Атака»/«Защита»
                    // и применяет умения через интерфейс. Ветка ввода снята — её приёмник на сервере закрыт.
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
                    Vector3 currentPlanePosition = Utils.TerrainScreenRaycast(Camera_TopDown.Instance.GetCursorPosition());
                    if (currentPlanePosition == Vector3.zero) currentPlanePosition = Utils.PlaneRayCast(Camera_TopDown.Instance.GetCursorPosition());

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
                        ActivateAbilityAndResetStates(Utils.TerrainScreenRaycast(Camera_TopDown.Instance.GetCursorPosition()));
                    }
                }
                // Unit ability
                else if (mode == PCMode.Unit)
                {
                    if (coreInput.Main.Select.WasReleasedThisFrame())
                    {
                        Unit hitUnit = Utils.GetUnitAtCursor();

                        if (hitUnit && hitUnit.IsVisible(SlotManager.Instance.currentTeam) && (hitUnit.unitType == UnitType.StaticDestructible || hitUnit.unitType == UnitType.Tree || FogOfWar.Instance.IsVisible(hitUnit.FoWCell, SlotManager.Instance.currentTeam)))
                        {
                            if (UnitSelector.IsUnitCompatible(activeUnit.owner, hitUnit, activeAbility.unitSelector) && FogOfWar.Instance.IsVisible(hitUnit.FoWCell, SlotManager.Instance.currentTeam))
                            {
                                // Only compatible and visible units can be targeted
                                ActivateAbilityAndResetStates(hitUnit);
                            }
                            else
                            {
                                UImanager.ShowNotifyMsg("You must select a compatible unit", SlotManager.Instance.currentPlayer, false);
                            }
                        }
                        else
                        {
                            UImanager.ShowNotifyMsg("You must select a compatible unit", SlotManager.Instance.currentPlayer, false);
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
                            if (hitUnit.IsVisible(SlotManager.Instance.currentTeam) && FogOfWar.Instance.IsVisible(hitUnit.FoWCell, SlotManager.Instance.currentTeam))
                            {
                                if (hitUnit.team == SlotManager.Instance.currentTeam && hitUnit.InventorySize > 0)
                                {
                                    if (Vector2.Distance(new Vector2(hitUnit.transform.position.x, hitUnit.transform.position.z), new Vector2(activeUnit.transform.position.x, activeUnit.transform.position.z)) <= GameManager.Instance.shopRadius)
                                    {
                                        activeUnit.shopUnit = hitUnit;
                                        ChangeMode(PCMode.Default);
                                    }
                                    else
                                    {
                                        UImanager.ShowNotifyMsg("This unit is too far", SlotManager.Instance.currentPlayer, false);
                                    }
                                }
                                else
                                {
                                    UImanager.ShowNotifyMsg("You can only select unit that has inventory and belongs to you or your allies", SlotManager.Instance.currentPlayer, false);
                                }
                            }
                            else
                            {
                                UImanager.ShowNotifyMsg("You must select a compatible unit", SlotManager.Instance.currentPlayer, false);
                            }
                        }
                        else
                        {
                            UImanager.ShowNotifyMsg("You must select a compatible unit", SlotManager.Instance.currentPlayer, false);
                        }
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
                    Vector3 currentPlanePosition = Utils.TerrainScreenRaycast(Camera_TopDown.Instance.GetCursorPosition(), shadowBuildingMask);
                    if (currentPlanePosition == Vector3.zero)
                    {
                        invalidPosition = true;
                        currentPlanePosition = Utils.PlaneRayCast(Camera_TopDown.Instance.GetCursorPosition());
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
                                else if (pointOnCircle.x < 0 || pointOnCircle.x > Grid.Instance.width || pointOnCircle.x > Grid.Instance.height || pointOnCircle.y < 0 || pointOnCircle.y > Grid.Instance.width || pointOnCircle.y > Grid.Instance.height)
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
                                else if (corners[i].x < 0 || corners[i].x > Grid.Instance.width || corners[i].x > Grid.Instance.height || corners[i].y < 0 || corners[i].y > Grid.Instance.width || corners[i].y > Grid.Instance.height)
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
                            hits = Physics.SphereCastAll(new Vector3(shadowBuilding.position.x, Utils.raycastPointY, shadowBuilding.position.z), shadowBuildingRadius, Vector3.down, Utils.raycastPointY * 5f, Utils.defaultMask);
                        }
                        else
                        {
                            // Box Collider - Check against units/buildings/objects; Air not included
                            BoxCollider boxCollider = shadowBuilding.GetComponent<BoxCollider>();
                            hits = Physics.BoxCastAll(new Vector3(shadowBuilding.position.x, Utils.raycastPointY, shadowBuilding.position.z), boxCollider.size * 0.5f * shadowBuilding.localScale.x, Vector3.down, shadowBuilding.rotation, Utils.raycastPointY * 5f, Utils.defaultMask);
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
                            for (int i = 0; i < materials.Length; i++) materials[i] = ReferenceManager.Instance.shadowMaterialInvalid;
                            renderer.materials = materials;
                        }
                    }
                    else
                    {
                        foreach (var renderer in shadowBuildingRenderers)
                        {
                            Material[] materials = renderer.materials;
                            for (int i = 0; i < materials.Length; i++) materials[i] = ReferenceManager.Instance.shadowMaterial;
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
            //         Gizmos.DrawCube(new Vector3(x * Grid.Instance.chunkSize + Grid.Instance.chunkSize * 0.5f, 0, y * Grid.Instance.chunkSize + Grid.Instance.chunkSize * 0.5f), new Vector3(Grid.Instance.chunkSize, 0.1f, Grid.Instance.chunkSize));
            //     }
            // }
        }
    }
}
