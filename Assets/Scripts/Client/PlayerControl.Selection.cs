using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Camera_TopDownNS;
using System.Collections;

namespace StrategyCore
{
    // PlayerControl.Selection.cs — выделение (формации/клики/рамка/группы). Вырезано 1:1 из PlayerControl.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class PlayerControl
    {

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
    }
}
