using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Camera_TopDownNS;
using System.Collections;

namespace StrategyCore
{
    // PlayerControl.Modes.cs — режимы/проекторы/тень здания. Вырезано 1:1 из PlayerControl.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class PlayerControl
    {

        // MODES -------------------------------------------------------------------------------------------------------------------------------------------------------

        // Modes. Various modes that allow to use items and abilities.
        public void ChangeMode(PCMode newMode) // Generic mode change, can used for reset
        {
            if (newMode == PCMode.Default) // Reset
            {
                // Cancel item DragDrop
                if (mode == PCMode.DragDrop) UIManager.Instance.ItemDragCancel();

                if (areaProjectorSpawned != null) GameObject.Destroy(areaProjectorSpawned.gameObject);

                HideRangeProjector();
                UnitProjectorHide();

                if (shadowBuilding) GameObject.Destroy(shadowBuilding.gameObject);

                UIManager.Instance.HideCancelButton();
                Camera_TopDown.Instance.ShowCursor();

                UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
            else if (newMode == PCMode.DragDrop) // Item drag&drop
            {
                dragSelect = false;
            }
            else if (newMode == PCMode.ShopUnit) // Shop unit change
            {
                UnityEngine.Cursor.SetCursor(ReferenceManager.Instance.modeCursor, new Vector2(64, 64), CursorMode.Auto);
            }
            else if (newMode == PCMode.AttackMove) // AttackMove command
            {
                UnityEngine.Cursor.SetCursor(ReferenceManager.Instance.modeCursor, new Vector2(64, 64), CursorMode.Auto);
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
            // if (IsOverUI(Camera_TopDown.Instance.GetCursorPosition())) Camera_TopDown.Instance.SetCursorPosition(true);

            // Area selection
            if (newMode == PCMode.Area)
            {
                CreateAreaSelector(areaRadius);
                Camera_TopDown.Instance.HideCursor();
            }
            // Unit/Position selection
            else if (newMode == PCMode.Unit || newMode == PCMode.Position)
            {
                UnityEngine.Cursor.SetCursor(ReferenceManager.Instance.modeCursor, new Vector2(64, 64), CursorMode.Auto);
            }
            // Building placement
            else if (newMode == PCMode.Placement)
            {
                Camera_TopDown.Instance.HideCursor();
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
            UIManagerMenu.Instance.ShowUIDocument();
        }

        // Toggles HealthBar display

        private void HealthBarDisplay(InputAction.CallbackContext context)
        {
            Utils.MainCamera.cullingMask ^= 1 << LayerMask.NameToLayer("Healthbar");
        }

        // Ping on minimap
        private void MiniMapPing(InputAction.CallbackContext context)
        {
            // Ping on ground
            bool overUI = IsOverUI(Camera_TopDown.Instance.GetCursorPosition());

            if (!overUI && coreInput.Main.MiniMapPing.IsPressed())
            {
                Vector3 clickPosition = Camera_TopDown.Instance.PlaneRayCast(Camera_TopDown.Instance.GetCursorPosition());

                UIManager.Instance.WorldToMiniMapPing(clickPosition.x / Grid.Instance.width, clickPosition.z / Grid.Instance.height);
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

        /// <summary>
        /// Лежит ли объект внутри контейнера надюнитовых элементов (полоски, иконка миникарты, эффекты).
        /// Ищем по имени, а не по ссылке на юнита: у копии для призрака компоненты уже сняты
        /// (Utils.UnitRemoveComponents), спрашивать поле не у кого.
        /// </summary>
        private static bool IsUnderUnitOverlay(Transform target)
        {
            for (Transform current = target; current != null; current = current.parent)
                if (current.name == Unit.OverlayName) return true;
            return false;
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
                // [Interflow 2026-09-09 unit-overlay] Надюнитовые элементы (полоски, иконка миникарты,
                // держатель эффектов) собраны в контейнере — отсекаем всё, что лежит под ним, одним условием.
                // Круг выделения и проектор дальности в контейнер не входят (решение Artsiom 09.09) и
                // отсекаются по именам, как раньше.
                if (!IsUnderUnitOverlay(renderer.transform) &&
                    renderer.name != "SelectionQuad(Clone)" &&
                    renderer.name != "RangeProjector(Clone)" &&
                    renderer.name != "MiniMapIcon")   // иконка, положенная прямым ребёнком в префаб юнита: контейнера на копии префаба нет
                {
                    // Turn off shadows and change material
                    shadowBuildingRenderers.Add(renderer);
                    Material[] materials = renderer.materials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = ReferenceManager.Instance.shadowMaterial;
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
                if (mode == PCMode.Placement) UIManager.Instance.ShowNotifyMsg("Can't do that.", SlotManager.Instance.currentPlayer, false);

                bool success = activeUnit.UseAbilityItem(activeAbilityIndex, activeIsItem, null, position, true);

                if (mode == PCMode.Placement)
                {
                    if (success)
                    {
                        // Save shadowBuilding
                        if (activeUnit.owner == SlotManager.Instance.currentPlayer)
                        {
                            if (activeUnit.constructionUnit.constructionRef != null)  // if constructionRef is null we already reached the destination
                            {
                                activeUnit.constructionUnit.shadowBuilding = shadowBuilding;
                                shadowBuilding = null;
                            }
                        }
                        UIManager.Instance.ShowNotifyMsg("", SlotManager.Instance.currentPlayer, false);
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
                Vector2 mousePositionCorrected = UIManager.Instance.CursorToUIposition();
                VisualElement picked = UIManager.Instance.uiDocument.rootVisualElement.panel.Pick(mousePositionCorrected);

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
            unitProjectorSpawned = Instantiate(ReferenceManager.Instance.selectionRenderer);

            unitProjectorSpawned.sprite = ReferenceManager.Instance.selectionSmall;
            unitProjectorSize = 0;
            unitProjectorSpawned.color = ReferenceManager.Instance.selectionAtCursorColor;
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

            if (unitAtCursor && unitAtCursor.isSelectable && unitAtCursor != activeUnit && FogOfWar.Instance.IsVisible(unitAtCursor.FoWCell, SlotManager.Instance.currentTeam) && unitAtCursor.IsVisible(SlotManager.Instance.currentTeam))
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
                        unitProjectorSpawned.sprite = ReferenceManager.Instance.selectionLarge;
                        unitProjectorSize = 2;
                    }
                }
                else if (unitAtCursor.unitRadius > Utils.mediumSelectorSize)
                {
                    if (unitProjectorSize != 1)
                    {
                        unitProjectorSpawned.sprite = ReferenceManager.Instance.selectionMedium;
                        unitProjectorSize = 1;
                    }
                }
                else if (unitProjectorSize != 0)
                {
                    unitProjectorSpawned.sprite = ReferenceManager.Instance.selectionSmall;
                    unitProjectorSize = 0;
                }

                // Color
                if (SlotManager.Instance.currentTeam != unitAtCursor.team && unitAtCursor.team != (int)Teams.NeutralPassive)
                {
                    if (!unitProjectorColor)
                    {
                        unitProjectorSpawned.color = ReferenceManager.Instance.selectionEnemyColor;
                        unitProjectorColor = true;
                    }
                }
                else if (unitProjectorColor)
                {
                    unitProjectorSpawned.color = ReferenceManager.Instance.selectionAtCursorColor;
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
                    unitProjectorSpawned.sprite = ReferenceManager.Instance.selectionLarge;
                    unitProjectorSize = 2;
                }
            }
            else if (radius > Utils.mediumSelectorSize)
            {
                if (unitProjectorSize != 1)
                {
                    unitProjectorSpawned.sprite = ReferenceManager.Instance.selectionMedium;
                    unitProjectorSize = 1;
                }
            }
            else if (unitProjectorSize != 0)
            {
                unitProjectorSpawned.sprite = ReferenceManager.Instance.selectionSmall;
                unitProjectorSize = 0;
            }

            // Color
            if (colorRed)
            {
                if (!unitProjectorColor)
                {
                    unitProjectorSpawned.color = ReferenceManager.Instance.selectionEnemyColor;
                    unitProjectorColor = true;
                }
            }
            else if (unitProjectorColor)
            {
                unitProjectorSpawned.color = ReferenceManager.Instance.selectionAtCursorColor;
                unitProjectorColor = false;
            }

            // Trasform
            unitProjectorSpawned.transform.position = obj.transform.position + new Vector3(0, 0.1f, 0);
        }

        public void CreateRangeProjector()
        {
            if (rangeProjectorSpawned != null) GameObject.Destroy(rangeProjectorSpawned.gameObject);

            rangeProjectorSpawned = Instantiate(ReferenceManager.Instance.rangeProjector);
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

            areaProjectorSpawned = GameObject.Instantiate(ReferenceManager.Instance.areaProjector).transform;
            areaProjectorSpawned.localScale = new Vector3(areaRadius, areaRadius, areaRadius); // For storing area radius

            areaProjectorSpawned.position = Utils.TerrainScreenRaycast(Camera_TopDown.Instance.GetCursorPosition());

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
