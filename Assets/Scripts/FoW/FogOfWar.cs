using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    public partial class FogOfWar : MonoBehaviour // [Interflow fix 2026-08-01 partial-split] класс разрезан на partial-файлы (задача №11)
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
            // [Interflow 2026-08-01 server-opt] На дедике маски видимости не рисуем: логика FoW живёт в gridFoW,
            // текстуры/материал — клиентский рендер (SetPixels32+Apply — заметный CPU каждый порог лерпа).
            if (Utils.Headless) return;
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
            Presentation.UI?.ResetMiniMap(); // [Interflow 2026-08-01 ADR-005]
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

    }
}
