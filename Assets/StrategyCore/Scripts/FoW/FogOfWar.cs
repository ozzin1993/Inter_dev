using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    public class FogOfWar : MonoBehaviour
    {
        public static FogOfWar instance;

        [Tooltip("Turn off fog of war.")]
        public bool TurnOff;
        [Tooltip("Grid width and height divided by this number must be a whole number!")]
        public float cellSize = 1f;
        [Tooltip("Amount of time temporarily revealed cells are visible for.")]
        public float revealTime = 2;

        private float visionRangeMultiplier; // We multiply visionRange by this number to ensure we cover all the vision cells according to radius of visionRange
        int gridSizeX; // How many cells will be on the map depenging on grid size = Grid.instance.width / cellSize
        int gridSizeY; // Grid.instance.height / cellSize

        // Holds information about visible/fogged cells for each team. First index is team, second index is cell state
        int[,,] gridFoW; // 0 Not visible
                         // > 1 Visible

        // Unit holder. Index is cellID, entries are list of units in that cell
        Dictionary<int, List<Unit>> unitsFoW = new Dictionary<int, List<Unit>>();
        Dictionary<int, List<VFXEnabler>> effectsFoW = new Dictionary<int, List<VFXEnabler>>();

        // Vision textures for current team
        [HideInInspector] public Texture2D visionMask;
        [HideInInspector] public Texture2D visionMask2;
        [SerializeField] private float lerpSpeed = 0.05f; // Time needed to lerp update the vision mask

        Color32[] pixels;
        float lerpFactor = 0; // For deciding which visionMask to use
        bool alternator = false; // For deciding which visionMask to use

        // Material
        [SerializeField] Material matFOW;
        [Tooltip("For non-URP this is just a placeholder, not used")]
        [SerializeField] Material matEdgeFOW;
        [SerializeField] Color fogColor = Color.white;
        Color halfFogColor;

        // FoW of static blockers
        [HideInInspector] private static int[] cellData; // -1,0,1,2,3 level of the cell; -10 means either is occupied or no terrain data was fetched
        [HideInInspector] private static Vector2[] cellDirecton; // When cell is occupied by view blocker we need its directional data

        // ============================= START / UPDATE =============================

        void Awake()
        {
            if (instance == null) instance = this;

            if (SlotManager.instance == null) GameObject.Find("ProjectManager").GetComponent<SlotManager>().InstanceSet();
            if (GameManager.instance == null) GameManager.instance = GetComponent<GameManager>();
            if (Grid.instance == null) Grid.instance = GetComponent<Grid>();

            // Vision range
            Utils.maxVisionRange += Mathf.CeilToInt(FogOfWar.instance.cellSize);
            Initialize();
        }

        void Start()
        {
            GameManager.instance.Tick += TimeUpdate;
        }

        private void OnDestroy()
        {
            GameManager.instance.Tick -= TimeUpdate;
        }

        // After all the manipulations with the textures with must apply them
        void LateUpdate()
        {
            if (alternator)
            {
                lerpFactor -= Time.deltaTime * (1f / lerpSpeed);
            }
            else
            {
                lerpFactor += Time.deltaTime * (1f / lerpSpeed);
            }

            if (lerpFactor > 0.995f)
            {
                lerpFactor = 1;
                visionMask.SetPixels32(pixels);
                visionMask.Apply();
                alternator = true;
            }
            else if (lerpFactor < 0.005f)
            {
                lerpFactor = 0;
                visionMask2.SetPixels32(pixels);
                visionMask2.Apply();
                alternator = false;
            }

            matFOW.SetFloat("_LerpTime", lerpFactor);

            return;
        }

        // Displays height information
        void OnDrawGizmos()
        {
            if (Application.isPlaying)
            {
                // Grids
                Color tempColor;
                for (int x = 0; x < gridSizeX; x++)
                {
                    for (int y = 0; y < gridSizeY; y++)
                    {
                        // FoW cell vision
                        if (pixels[x + y * gridSizeX] == fogColor) tempColor = Color.red;
                        else tempColor = Color.green;

                        // // Height data
                        // if (cellData[x + y * gridSizeX] == -2) tempColor = Color.black;
                        // else if (cellData[x + y * gridSizeX] == -1) tempColor = Color.blue;
                        // else if (cellData[x + y * gridSizeX] == 0) tempColor = Color.green;
                        // else if (cellData[x + y * gridSizeX] == 1) tempColor = Color.yellow;
                        // else if (cellData[x + y * gridSizeX] == 2) tempColor = Color.red;
                        // else tempColor = Color.white;

                        tempColor.a = 0.2f;
                        Gizmos.color = tempColor;
                        Gizmos.DrawCube(new Vector3(x * cellSize + cellSize * 0.5f, 0, y * cellSize + cellSize * 0.5f), new Vector3(cellSize, 0.1f, cellSize));
                    }
                }
            }
        }

        // ============================= INITIALIZATION =============================

        /// <summary>
        /// Initializes the Fog of War system, setting up textures, vision masks, and grid data.
        /// </summary>
        public void Initialize()
        {
            visionRangeMultiplier = Grid.instance.cellSize / cellSize;

            // Half Fog Color
            halfFogColor = fogColor;
            halfFogColor.a = fogColor.a / 2;
            if (TurnOff) fogColor.a = 0;

            // How many cells will be on the map depenging on grid size
            gridSizeX = (int)(Grid.instance.width / cellSize);
            gridSizeY = (int)(Grid.instance.height / cellSize);

            // Create cell state holder
            gridFoW = new int[Enum.GetNames(typeof(Teams)).Length, gridSizeX, gridSizeY];

            // UnitsFoW
            unitsFoW.Clear();
            effectsFoW.Clear();
            for (int i = 0; i < gridSizeY * gridSizeX + gridSizeX; i++)
            {
                unitsFoW.Add(i, new List<Unit>());
                effectsFoW.Add(i, new List<VFXEnabler>());
            }

            // Create textures - Only for current team
            visionMask = new Texture2D(gridSizeX, gridSizeY, TextureFormat.Alpha8, -1, false);
            visionMask2 = new Texture2D(gridSizeX, gridSizeY, TextureFormat.Alpha8, -1, false);

            visionMask.wrapMode = TextureWrapMode.Clamp;
            visionMask.filterMode = FilterMode.Bilinear;
            visionMask2.wrapMode = TextureWrapMode.Clamp;
            visionMask2.filterMode = FilterMode.Bilinear;

            // Fill texture
            pixels = visionMask.GetPixels32();
            for (var q = 0; q < pixels.Length; ++q)
            {
                pixels[q] = fogColor;
            }

            visionMask.SetPixels32(pixels);
            visionMask.Apply();

            visionMask2.SetPixels32(pixels);
            visionMask2.Apply();

            // Player mask set on material
            matFOW.SetTexture("_ShadowTex", visionMask);
            matFOW.SetTexture("_ShadowTex2", visionMask2);
            matFOW.SetFloat("_LerpTime", 1f / lerpSpeed);
            matFOW.SetFloat("fogAlpha", fogColor.a);
            matEdgeFOW.SetFloat("fogAlpha", fogColor.a);

            // Enable projector and projector parameters
            if (TurnOff == false)
            {
                Transform projectorObj = transform.Find("FoWProjector/FoWProjector");
                ProjectorHelper.InitializeFoWProjector(projectorObj, matFOW, matEdgeFOW);
            }

            // Set Utils.cs Ground Mask - We set it here, not in GameManager because these variables are used by fog of war in Awake()
            Utils.terrainMaskVisuals = LayerMask.GetMask("Ground", "Water", "ShallowWater", "Terrain"); 
            Utils.terrainMask = LayerMask.GetMask("Ground", "Water", "ShallowWater");
            Utils.groundMask = LayerMask.GetMask("Ground", "ShallowWater");
            Utils.waterMask = LayerMask.GetMask("Water", "ShallowWater");

            // Cell Data
            cellData = new int[gridSizeY * gridSizeX + gridSizeX];
            cellDirecton = new Vector2[gridSizeY * gridSizeX + gridSizeX];

            // Cell levels
            for (int x = 0; x < gridSizeX; x++)
            {
                for (int y = 0; y < gridSizeY; y++)
                {
                    CalculateTheHeightData(x, y);
                }
            }

            // Calculate cell occupancy
            GameObject environment = GameObject.Find("/Environment");
            if (environment != null)
            {
                PopulateGrid(environment.transform);
            }

            // Reset UI MiniMap, otherwise it will not update the texture
            if (UIManager.instance) UIManager.instance.ResetMiniMap();
        }

        /// <summary>
        /// Populates the FoW grid with vision-blocking objects from the environment.
        /// </summary>
        /// <param name="obj">Children of this transform will be evaluated. Initially called on Environment object in the scene.</param>
        /// <param name="removeCellOccupancy">Should add or remove the occupancy data.</param>
        public void PopulateGrid(Transform obj, bool removeCellOccupancy = false)
        {
            // If it is empty object we populate grid with its children, otherwise we populate with the object itself
            if (obj.GetComponents<Component>().Length == 1)
            {
                for (int i = 0; i < obj.childCount; ++i)
                {
                    Transform child = obj.GetChild(i);

                    if (!child.gameObject.activeInHierarchy) continue;

                    PopulateGrid(child);
                }
            }
            else
            {
                // Unit view blocking calculation is done by Unit.cs
                if (!obj.GetComponent<Unit>())
                {
                    if (obj.GetComponent<BoxCollider>())
                    {
                        // View cell occupy based on box collider
                        CellOcupation(Utils.GetRectangleFromCollider(obj), removeCellOccupancy);
                    }
                    else
                    {
                        // Single cell occupier
                        SingleCellOcupation(obj.position, removeCellOccupancy);
                    }
                }
            }
        }

        /// <summary>
        /// Calculate FoW grid occupancy based on Units that are view blockers.
        /// </summary>
        /// <param name="unit">Unit to evaluate.</param>
        /// <param name="removeCellOccupancy">Should add or remove the occupancy data.</param>
        public void UnitViewBlockCalculate(Unit unit, bool removeCellOccupancy = false)
        {
            // Remove vision of units that can see this view blocker then update their vision after cell data is updated
            Unit[] units = Utils.GetAllUnitsInRadius(new Vector2(unit.transform.position.x, unit.transform.position.z), Utils.maxVisionRange);
            for (int i = 0; i < units.Length; i++)
            {
                if (units[i].isAir) UpdateVisionAir(units[i].team, units[i].FoWCell, (int)(units[i].visionRange * visionRangeMultiplier), true);
                else UpdateVisionNew(units[i].team, units[i].FoWCell, (int)(units[i].visionRange * visionRangeMultiplier), true);
            }

            // Set cell occupancy
            if (!unit.singleCellViewBlocker && unit.GetComponent<BoxCollider>())
            {
                // View cell occupy based on box collider
                CellOcupation(Utils.GetRectangleFromCollider(unit.transform), removeCellOccupancy);
            }
            else
            {
                // Single cell occupier
                SingleCellOcupation(unit.transform.position, removeCellOccupancy);
            }

            // Update vision of units around this view blocker
            for (int i = 0; i < units.Length; i++)
            {
                if (units[i].isAir) UpdateVisionAir(units[i].team, units[i].FoWCell, (int)(units[i].visionRange * visionRangeMultiplier));
                else UpdateVisionNew(units[i].team, units[i].FoWCell, (int)(units[i].visionRange * visionRangeMultiplier));
            }
        }

        /// <summary>
        /// Marks multiple grid cells as occupied based on a rectangular area (e.g., a building or large obstacle).
        /// </summary>
        /// <param name="rect">Rectangle data.</param>
        /// <param name="remove">Should add or remove the occupancy data. If removed restores the height information on the cell.</param>
        private void CellOcupation(Vector2[] rect, bool remove)
        {
            // Expand 4 vtx in opposite direction towards the center of the rectangle
            Vector2[] expandedRect = new Vector2[4];

            Vector2 center = (rect[0] + rect[1] + rect[2] + rect[3]) / 4;
            for (int i = 0; i < rect.Length; i++)
            {
                expandedRect[i] = (rect[i] - center).normalized * cellSize + rect[i];
            }
            expandedRect = rect;

            // For each edge get its direction inwards, get occipied cells and set data
            // If single width, we set the cell as wall
            bool singleWidth = false;

            Vector2Int edge1_1 = GetCellByPositionVector2(expandedRect[0]);
            Vector2Int edge1_2 = GetCellByPositionVector2(expandedRect[1]);
            Vector2Int edge3_1 = GetCellByPositionVector2(expandedRect[2]);
            Vector2Int edge3_2 = GetCellByPositionVector2(expandedRect[3]);

            if (edge1_1 == edge3_2 && edge1_2 == edge3_1) singleWidth = true;
            else
            {
                edge1_1 = GetCellByPositionVector2(expandedRect[1]);
                edge1_2 = GetCellByPositionVector2(expandedRect[2]);
                edge3_1 = GetCellByPositionVector2(expandedRect[3]);
                edge3_2 = GetCellByPositionVector2(expandedRect[0]);

                if (edge1_1 == edge3_2 && edge1_2 == edge3_1) singleWidth = true;
            }

            for (int i = 0; i < 4; i++)
            {
                // Find intersected grid cells
                Vector2Int startCell = GetCellByPositionVector2(expandedRect[i]);
                Vector2Int endCell = (i == 3) ? GetCellByPositionVector2(expandedRect[0]) : GetCellByPositionVector2(expandedRect[i + 1]);
                List<Vector2Int> intersectedCells = Utils.GetLineCells(startCell, endCell);

                // Find edge
                Vector2 edge = (i == 3) ? expandedRect[0] - expandedRect[i] : expandedRect[i + 1] - expandedRect[i];
                // Calculate edge`s direction facing inwards
                Vector2 inwardNormal = new Vector2(edge.y, -edge.x).normalized; // Counter clockwise rotation? -y

                // Set angles. For corners we set the average
                for (int c = 0; c < intersectedCells.Count; c++)
                {
                    if (remove)
                    {
                        CalculateTheHeightData(intersectedCells[c].x, intersectedCells[c].y);
                        cellDirecton[intersectedCells[c].x + intersectedCells[c].y * gridSizeX] = Vector2.zero;
                    }
                    else
                    {
                        if (singleWidth)
                        {
                            cellData[intersectedCells[c].x + intersectedCells[c].y * gridSizeX] = -10;
                            continue;
                        }

                        // Not first edges, start corner || Last edge, end corner
                        if ((i != 0 && c == 0) || (i == 3 && c == intersectedCells.Count - 1))
                        {
                            cellDirecton[intersectedCells[c].x + intersectedCells[c].y * gridSizeX] = (cellDirecton[intersectedCells[c].x + intersectedCells[c].y * gridSizeX] + inwardNormal) / 2;
                        }
                        // Else
                        else cellDirecton[intersectedCells[c].x + intersectedCells[c].y * gridSizeX] = inwardNormal;
                    }
                }
            }
        }

        /// <summary>
        /// Marks a single grid cell as occupied or restores its original terrain height.
        /// </summary>
        /// <param name="position">Vector3 position of the cell.</param>
        /// <param name="remove">Should add or remove the occupancy data. If removed restores the height information on the cell.</param>
        private void SingleCellOcupation(Vector3 position, bool remove)
        {
            int x = (int)(position.x / cellSize);
            int y = (int)(position.z / cellSize);

            if (remove)
            {
                CalculateTheHeightData(x, y);
            }
            else
            {
                cellData[x + y * gridSizeX] = -10;
            }
        }

        /// <summary>
        /// Given cellData X and Y coordinate calculates its height data.
        /// </summary>
        /// <param name="x">X index of the cell.</param>
        /// <param name="y">Y index of the cell.</param>
        private void CalculateTheHeightData(int x, int y)
        {
            // We make 4 raycasts and choose the lowest level
            Vector2 terrainPoint1 = new Vector2((x * cellSize) + cellSize * 0.1f, (y * cellSize) + cellSize * 0.1f);
            Vector2 terrainPoint2 = new Vector2((x * cellSize) + cellSize * 0.9f, (y * cellSize) + cellSize * 0.1f);
            Vector2 terrainPoint3 = new Vector2((x * cellSize) + cellSize * 0.9f, (y * cellSize) + cellSize * 0.9f);
            Vector2 terrainPoint4 = new Vector2((x * cellSize) + cellSize * 0.1f, (y * cellSize) + cellSize * 0.9f);

            float Ypoint1 = Utils.GetTerrainHeightAbsolute(terrainPoint1);
            float Ypoint2 = Utils.GetTerrainHeightAbsolute(terrainPoint2);
            float Ypoint3 = Utils.GetTerrainHeightAbsolute(terrainPoint3);
            float Ypoint4 = Utils.GetTerrainHeightAbsolute(terrainPoint4);

            float minYPoint = Mathf.Max(Ypoint1, Ypoint2, Ypoint3, Ypoint4);

            if (minYPoint == -9999)
            {
                cellData[x + y * gridSizeX] = -10;
            }
            else
            {
                // For height values we add offset and then floor them to properly calculate the height level
                cellData[x + y * gridSizeX] = (int)Mathf.Floor((minYPoint + Utils.levelHeightOffset) / Grid.instance.cellSize);
            }
        }

        // ============================= FoW LOGIC =============================

        public void UpdateVisionNew(int team, Coordinate newCoord, int radius, bool hide = false)
        {
            int unitElevationLevel = cellData[newCoord.x + newCoord.y * gridSizeX];
            Vector2 currentPosition = new Vector2(newCoord.x * cellSize + cellSize * 0.5f, newCoord.y * cellSize + cellSize * 0.5f);

            // Reveal the first tile where the revealer is at
            if (hide) HideTile(team, newCoord);
            else RevealTile(team, newCoord);

            // Give the quadrant iterator the revealer's position
            Quadrant quadrantIterator = new Quadrant(Cardinal.East, newCoord);

            // We deal with 90 degrees each, anti-clockwise, starting from east to west
            for (int c = 0; c < 4; c++)
            {
                quadrantIterator.cardinal = (Cardinal)c;

                // Here goes the BFS algorithm, we queue a new pass during each pass if needed, then start the new one
                Queue<Column> columnIterators = new Queue<Column>();

                // The first pass of the given quadrant, start from slope -1 to slope 1
                columnIterators.Enqueue(new Column(1, radius, -1, 1));

                while (columnIterators.Count > 0)
                {
                    Column columnIterator = columnIterators.Dequeue();

                    // Note that the given points may have negative y values instead of starting from zero
                    List<Coordinate> quadrantPoints = columnIterator.GetTiles();

                    // This is to detect points where the obstacle tile and the empty tile are adjacent
                    Coordinate lastQuadrantPoint = new Coordinate();

                    // This is to skip the first pass where the lastQuadrantPoint variable is not assigned yet
                    bool firstStepFlag = true;

                    foreach (Coordinate quadrantPoint in quadrantPoints)
                    {
                        // Reveal current tile based on following
                        if (!IsTileUpperLevel(quadrantIterator, quadrantPoint, unitElevationLevel) && //  Should not be on another level, if it is we do not show the tile
                            (IsTileObstacle(quadrantIterator, quadrantPoint, unitElevationLevel, currentPosition) == true || // If obstacle (edges of the viewBlocker) we show the tile
                            IsTileVisible(columnIterator, quadrantPoint))) // If visible and not blocked
                        {
                            if (hide) HideTileIteratively(team, quadrantIterator, quadrantPoint, radius);
                            else RevealTileIteratively(team, quadrantIterator, quadrantPoint, radius);
                        }

                        if (firstStepFlag == false)
                        {
                            // Start slope if previous tile is obstacle and current tile is empty
                            if (IsTileObstacle(quadrantIterator, lastQuadrantPoint, unitElevationLevel, currentPosition) == true && IsTileObstacle(quadrantIterator, quadrantPoint, unitElevationLevel, currentPosition) == false)
                            {
                                columnIterator.startSlope = GetQuadrantSlope(quadrantPoint);
                            }

                            // End slope if previous tile is empty and current tile is obstacle
                            if (IsTileObstacle(quadrantIterator, lastQuadrantPoint, unitElevationLevel, currentPosition) == false && IsTileObstacle(quadrantIterator, quadrantPoint, unitElevationLevel, currentPosition) == true)
                            {
                                if (columnIterator.IsProceedable() == false)
                                {
                                    continue;
                                }

                                Column nextColumnIterator = new Column(
                                    columnIterator.depth,
                                    radius,
                                    columnIterator.startSlope,
                                    GetQuadrantSlope(quadrantPoint));

                                nextColumnIterator.ProceedIfPossible();

                                columnIterators.Enqueue(nextColumnIterator);
                            }
                        }

                        lastQuadrantPoint = quadrantPoint;

                        firstStepFlag = false;
                    }

                    // Scan the next column if previous tile is empty
                    if (columnIterator.IsProceedable() == true && IsTileObstacle(quadrantIterator, lastQuadrantPoint, unitElevationLevel, currentPosition) == false)
                    {
                        columnIterator.ProceedIfPossible();

                        columnIterators.Enqueue(columnIterator);
                    }
                }
            }
        }

        // Updates the vision for air units
        public void UpdateVisionAir(int team, Coordinate newCoord, int radius, bool hide = false)
        {
            // Reveal the first tile where the revealer is at
            if (hide) HideTile(team, newCoord);
            else RevealTile(team, newCoord);

            // Give the quadrant iterator the revealer's position
            Quadrant quadrantIterator = new Quadrant(Cardinal.East, newCoord);

            // We deal with 90 degrees each, anti-clockwise, starting from east to west
            for (int c = 0; c < 4; c++)
            {
                quadrantIterator.cardinal = (Cardinal)c;

                // Here goes the BFS algorithm, we queue a new pass during each pass if needed, then start the new one
                Queue<Column> columnIterators = new Queue<Column>();

                // The first pass of the given quadrant, start from slope -1 to slope 1
                columnIterators.Enqueue(new Column(1, radius, -1, 1));

                while (columnIterators.Count > 0)
                {
                    Column columnIterator = columnIterators.Dequeue();

                    // Note that the given points may have negative y values instead of starting from zero
                    List<Coordinate> quadrantPoints = columnIterator.GetTiles();

                    foreach (Coordinate quadrantPoint in quadrantPoints)
                    {
                        // Reveal current tile based on following
                        if (IsTileVisible(columnIterator, quadrantPoint))
                        {
                            if (hide) HideTileIteratively(team, quadrantIterator, quadrantPoint, radius);
                            else RevealTileIteratively(team, quadrantIterator, quadrantPoint, radius);
                        }
                    }

                    // Scan the next column if previous tile is empty
                    if (columnIterator.IsProceedable() == true)
                    {
                        columnIterator.ProceedIfPossible();

                        columnIterators.Enqueue(columnIterator);
                    }
                }
            }
        }

        private bool WithingBounds(Coordinate coord)
        {
            return
                coord.x >= 0 &&
                coord.x < gridSizeX &&
                coord.y >= 0 &&
                coord.y < gridSizeY;
        }

        private void RevealTileIteratively(int team, Quadrant quadrant, Coordinate quadrantPoint, int radius)
        {
            Coordinate levelCoordinates = quadrant.QuadrantToLevel(quadrantPoint);

            if (WithingBounds(levelCoordinates) == false) return;

            if (Mathf.Sqrt(quadrantPoint.x * quadrantPoint.x + quadrantPoint.y * quadrantPoint.y) > radius) // Quadrant point magnitude check
            {
                return;
            }

            if (gridFoW[team, levelCoordinates.x, levelCoordinates.y] == 0)
            {
                // This cell was not visible before, we must update the texture and show units in this cell

                if (team == SlotManager.instance.currentTeam)
                {
                    pixels[levelCoordinates.x + levelCoordinates.y * gridSizeX] = new Color(fogColor.r, fogColor.g, fogColor.b, 0);
                    ShowUnits(team, levelCoordinates.x + levelCoordinates.y * gridSizeX);
                }
            }

            gridFoW[team, levelCoordinates.x, levelCoordinates.y] += 1;
        }

        private void RevealTile(int team, Coordinate levelCoordinates)
        {
            if (WithingBounds(levelCoordinates) == false) return;

            if (gridFoW[team, levelCoordinates.x, levelCoordinates.y] == 0)
            {
                // This cell was not visible before, we must update the texture and show units in this cell
                if (team == SlotManager.instance.currentTeam)
                {
                    pixels[levelCoordinates.x + levelCoordinates.y * gridSizeX] = new Color(fogColor.r, fogColor.g, fogColor.b, 0);
                    ShowUnits(team, levelCoordinates.x + levelCoordinates.y * gridSizeX);
                }
            }

            gridFoW[team, levelCoordinates.x, levelCoordinates.y] += 1;
        }

        private void HideTileIteratively(int team, Quadrant quadrant, Coordinate quadrantPoint, int radius)
        {
            Coordinate levelCoordinates = quadrant.QuadrantToLevel(quadrantPoint);

            if (WithingBounds(levelCoordinates) == false) return;

            if (Mathf.Sqrt(quadrantPoint.x * quadrantPoint.x + quadrantPoint.y * quadrantPoint.y) > radius) // Quadrant point magnitude check
            {
                return;
            }

            gridFoW[team, levelCoordinates.x, levelCoordinates.y] -= 1;

            if (gridFoW[team, levelCoordinates.x, levelCoordinates.y] == 0)
            {
                // This cell became hidden, we must update the texture and hide units in this cell

                if (team == SlotManager.instance.currentTeam)
                {
                    pixels[levelCoordinates.x + levelCoordinates.y * gridSizeX] = new Color(fogColor.r, fogColor.g, fogColor.b, fogColor.a);
                    HideUnits(team, levelCoordinates.x + levelCoordinates.y * gridSizeX);
                }
            }
        }

        private void HideTile(int team, Coordinate levelCoordinates)
        {
            if (WithingBounds(levelCoordinates) == false) return;

            gridFoW[team, levelCoordinates.x, levelCoordinates.y] -= 1;

            if (gridFoW[team, levelCoordinates.x, levelCoordinates.y] == 0)
            {
                // This cell became hidden, we must update the texture and hide units in this cell
                if (team == SlotManager.instance.currentTeam)
                {
                    pixels[levelCoordinates.x + levelCoordinates.y * gridSizeX] = new Color(fogColor.r, fogColor.g, fogColor.b, fogColor.a);
                    HideUnits(team, levelCoordinates.x + levelCoordinates.y * gridSizeX);
                }
            }
        }

        private bool IsTileEmpty(Quadrant quadrant, Coordinate quadrantPoint, int unitElevationLevel)
        {
            Coordinate levelCoordinates = quadrant.QuadrantToLevel(quadrantPoint);

            if (WithingBounds(levelCoordinates) == false) return true;

            if (cellData[levelCoordinates.x + levelCoordinates.y * gridSizeX] == -10) return false;
            else if (cellData[levelCoordinates.x + levelCoordinates.y * gridSizeX] > unitElevationLevel) return false;
            return true;
        }

        private bool IsTileObstacle(Quadrant quadrant, Coordinate quadrantPoint, int unitElevationLevel, Vector2 currentPosition)
        {
            Coordinate levelCoordinates = quadrant.QuadrantToLevel(quadrantPoint);

            if (WithingBounds(levelCoordinates) == false) return false;

            // Check direction
            Vector2 dir = currentPosition - new Vector2(levelCoordinates.x * cellSize + cellSize * 0.5f, levelCoordinates.y * cellSize + cellSize * 0.5f);
            float dotProduct = Vector2.Dot(dir, cellDirecton[levelCoordinates.x + levelCoordinates.y * gridSizeX]);

            // If it is -10, it is a wall
            if (cellData[levelCoordinates.x + levelCoordinates.y * gridSizeX] == -10) return true;
            // If dot product is less than 0, it means wall is looking at opposite direction to the unit. Treat as wall
            else if (dotProduct < 0) return true;
            // Treat as wall if cell is at another elevation level
            else if (cellData[levelCoordinates.x + levelCoordinates.y * gridSizeX] > unitElevationLevel) return true;
            return false;
        }

        private bool IsTileUpperLevel(Quadrant quadrant, Coordinate quadrantPoint, int unitElevationLevel)
        {
            Coordinate levelCoordinates = quadrant.QuadrantToLevel(quadrantPoint);

            if (WithingBounds(levelCoordinates) == false) return true;

            if (cellData[levelCoordinates.x + levelCoordinates.y * gridSizeX] > unitElevationLevel) return true;
            return false;
        }

        private bool IsTileVisible(Column columnIterator, Coordinate quadrantPoint)
        {
            return (quadrantPoint.y >= columnIterator.depth * columnIterator.startSlope) &&
                (quadrantPoint.y <= columnIterator.depth * columnIterator.endSlope);
        }

        private float GetQuadrantSlope(Coordinate quadrantPoint)
        {
            // The reason that this is not simply y / x is that the wall is diamond-shaped, refer to the links at the top
            return (((quadrantPoint.y * 2) - 1) / ((float)quadrantPoint.x * 2));
        }

        // Returns if cell is visible for certain team
        public bool IsVisible(Coordinate cell, int team)
        {
            if (TurnOff) return true;
            if (gridFoW[team, cell.x, cell.y] == 0) return false;
            return true;
        }

        public bool IsVisible(Vector2 position, int team)
        {
            return IsVisible(GetCellByPosition(position), team);
        }

        public bool IsVisible(Vector3 position, int team)
        {
            return IsVisible(GetCellByPosition(position), team);
        }

        // ============================= GET CELL BY POSITION =============================

        public static Coordinate GetCellByPosition(Vector2 pos)
        {
            return new Coordinate((int)(pos.x / FogOfWar.instance.cellSize), (int)(pos.y / FogOfWar.instance.cellSize));
        }

        public static Coordinate GetCellByPosition(Vector3 pos)
        {
            return new Coordinate((int)(pos.x / FogOfWar.instance.cellSize), (int)(pos.z / FogOfWar.instance.cellSize));
        }

        public static Vector2Int GetCellByPositionVector2(Vector2 pos)
        {
            return new Vector2Int((int)(pos.x / FogOfWar.instance.cellSize), (int)(pos.y / FogOfWar.instance.cellSize));
        }

        public static Vector2Int GetCellByPositionVector2(Vector3 pos)
        {
            return new Vector2Int((int)(pos.x / FogOfWar.instance.cellSize), (int)(pos.z / FogOfWar.instance.cellSize));
        }

        // ============================= UNIT VISION HANDLING =============================

        // Units call this method every frame to check if their cell was changed, if true it triggers vision mask change
        public void CellAssignment(Unit unit)
        {
            if (TurnOff) return;

            // [Interflow fix 2026-07-10] Кламп клетки в границы FoW-сетки: юнит вне [0,gridSizeX)x[0,gridSizeY) давал
            // отрицательный/выходящий индекс -> IndexOutOfRange (gridFoW) / KeyNotFound (unitsFoW). Привязываем
            // к крайней клетке (безопаснее краша; первопричину — юнит вне карты — искать отдельно). Как Grid.ChunkCoordClamped.
            Coordinate newCell = new Coordinate(
                Mathf.Clamp((int)(unit.transform.position.x / cellSize), 0, gridSizeX - 1),
                Mathf.Clamp((int)(unit.transform.position.z / cellSize), 0, gridSizeY - 1));

            if (unit.FoWCell != newCell)
            {
                // Assign unit to a new cell
                unitsFoW[unit.FoWCell.x + unit.FoWCell.y * gridSizeX].Remove(unit);
                unitsFoW[newCell.x + newCell.y * gridSizeX].Add(unit);

                // If this unit is not current player`s team, we should hide and unhide based on the visibility of current player`s team
                if (unit.team != SlotManager.instance.currentTeam)
                {
                    if (gridFoW[SlotManager.instance.currentTeam, unit.FoWCell.x, unit.FoWCell.y] == 0 && gridFoW[SlotManager.instance.currentTeam, newCell.x, newCell.y] > 0)
                    {
                        // Previously was invisible, now is visible. Show the unit.
                        unit.ShowRenderers();
                        unit.FoWVisible = true;
                    }
                    else if (gridFoW[SlotManager.instance.currentTeam, newCell.x, newCell.y] == 0 && gridFoW[SlotManager.instance.currentTeam, unit.FoWCell.x, unit.FoWCell.y] > 0)
                    {
                        // Previously was visible, now is invisible. Hide the unit.
                        unit.HideRenderers();
                        unit.FoWVisible = false;
                    }
                }
                else unit.FoWVisible = true;

                if (unit.isAir)
                {
                    UpdateVisionAir(unit.team, newCell, (int)(unit.visionRange * visionRangeMultiplier)); // Reveal                                                                                      
                    UpdateVisionAir(unit.team, unit.FoWCell, (int)(unit.visionRange * visionRangeMultiplier), true); // Hide
                }
                else
                {
                    UpdateVisionNew(unit.team, newCell, (int)(unit.visionRange * visionRangeMultiplier)); // Reveal                                                                                      
                    UpdateVisionNew(unit.team, unit.FoWCell, (int)(unit.visionRange * visionRangeMultiplier), true); // Hide
                }

                unit.FoWCell = newCell;
            }
        }

        // Initial cell assignment
        public Coordinate CellAssignment(Unit unit, bool initial)
        {
            if (TurnOff) return unit.FoWCell;

            Coordinate newCell = new Coordinate((int)(unit.transform.position.x / cellSize), (int)(unit.transform.position.z / cellSize));

            unitsFoW[newCell.x + newCell.y * gridSizeX].Add(unit);

            // If this unit is not current player`s team, we should hide and unhide based on the visibility of current player`s team
            if (unit.team != SlotManager.instance.currentTeam)
            {
                if (gridFoW[SlotManager.instance.currentTeam, newCell.x, newCell.y] == 0)
                {
                    // Unit not visible. Hide it.
                    unit.HideRenderers();
                    unit.FoWVisible = false;
                }
                else
                {
                    // Unit is visible. Show it
                    unit.ShowRenderers();
                    unit.FoWVisible = true;
                }
            }
            else unit.FoWVisible = true;

            if (unit.isAir) UpdateVisionAir(unit.team, newCell, (int)(unit.visionRange * visionRangeMultiplier));
            else UpdateVisionNew(unit.team, newCell, (int)(unit.visionRange * visionRangeMultiplier));

            return newCell;
        }

        // Unit remove from cell
        public void CellRemove(Unit unit)
        {
            if (TurnOff) return;

            unitsFoW[unit.FoWCell.x + unit.FoWCell.y * gridSizeX].Remove(unit);

            if (unit.isAir) UpdateVisionAir(unit.team, unit.FoWCell, (int)(unit.visionRange * visionRangeMultiplier), true);
            else UpdateVisionNew(unit.team, unit.FoWCell, (int)(unit.visionRange * visionRangeMultiplier), true);
        }

        // Hide units that do not belong to current player`s team
        void HideUnits(int team, int cellIndex)
        {
            if (team == SlotManager.instance.currentTeam)
            {
                foreach (var unit in unitsFoW[cellIndex])
                {
                    // No need to unhide current player`s units
                    if (unit.team != SlotManager.instance.currentTeam)
                    {
                        unit.HideRenderers();
                        unit.FoWVisible = false;
                    }
                }

                foreach (var enabler in effectsFoW[cellIndex])
                {
                    enabler.Disable();
                }
            }
        }

        void ShowUnits(int team, int cellIndex)
        {
            if (team == SlotManager.instance.currentTeam)
            {
                foreach (var unit in unitsFoW[cellIndex])
                {
                    // No need to unhide current player`s units
                    if (unit.team != SlotManager.instance.currentTeam)
                    {
                        unit.ShowRenderers();
                        unit.FoWVisible = true;
                    }
                }

                foreach (var enabler in effectsFoW[cellIndex])
                {
                    enabler.Enable();
                }
            }
        }

        // ============================= VFX VISION HANDLING =============================

        // Assigns vfx to cell
        public void CellAssignVFX(VFXEnabler vfx)
        {
            if (TurnOff) return;

            vfx.FoWCell = GetCellByPosition(vfx.transform.position);
            effectsFoW[vfx.FoWCell.x + vfx.FoWCell.y * gridSizeX].Add(vfx);
        }

        // Remove vfx from cell
        public void CellRemoveVFX(VFXEnabler vfx)
        {
            if (TurnOff) return;

            effectsFoW[vfx.FoWCell.x + vfx.FoWCell.y * gridSizeX].Remove(vfx);
        }

        // ============================= TEMPORARILY REVEALED UNITS =============================

        // Reveal tile for N seconds (Caused by attacking unit that might not be fow visible)
        public List<TemporalCells> temporarilyRevealed = new List<TemporalCells>(); // After certain amount of time this cells will lose the vision

        // To hold temporarily revealed cell data
        public class TemporalCells
        {
            public Coordinate coordinate; // Coordinate of cell
            public float time; // Current amount of time that this cell has been visible for
            public int team; // Team that can see this cell

            public TemporalCells(int team, Coordinate coordinate)
            {
                this.coordinate = coordinate;
                this.team = team;
                this.time = 0;
            }
        }

        // Reveals the specified tile for certain amount of time
        public void TemporalReveal(int team, Coordinate cell)
        {
            RevealTile(team, cell);
            temporarilyRevealed.Add(new TemporalCells(team, cell));
        }

        // Every tickrate we update time and if due hide the tile
        public void TimeUpdate()
        {
            for (int i = temporarilyRevealed.Count - 1; i >= 0; i--)
            {
                temporarilyRevealed[i].time += GameManager.instance.currentDeltaTime;

                if (temporarilyRevealed[i].time > revealTime)
                {
                    HideTile(temporarilyRevealed[i].team, temporarilyRevealed[i].coordinate);
                    temporarilyRevealed.RemoveAt(i);
                }
            }
        }
    }
}
