using DataStructures.PriorityQueue;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Stores all units in grid cells for faster search functions
    public class Grid : MonoBehaviour
    {
        public static Grid instance;

        [Tooltip("Total map coverage in scene units. \nStarts at x0,y0,z0 and increases in positive axis. For example: x10, y0, z10.")]
        public int width = 100;
        public int height = 100;

        [Tooltip("Division of MapSize by cellSize and chunkSize must be a whole number.")]
        public float cellSize = 1;
        public float chunkSize = 10; 

        [HideInInspector] public static bool[] cellOccupancy; // True if cell is ocupied
        [HideInInspector] public static Dictionary<int, List<Unit>> chunkUnits = new Dictionary<int, List<Unit>>(); // Holds gameObjects at cell coordinates
        // Dictionary<Coordinate, HashSet<Unit>> gridCell = new Dictionary<Coordinate, HashSet<Unit>>(); 
        [HideInInspector] public static int cellCountX; public static int cellCountY;
        [HideInInspector] public static int chunkCountX; public static int chunkCountY;

        // Start is called before the first frame update
        void Awake()
        {
            if (instance == null) instance = this;

            // How many cells will be on the map depenging on grid size
            cellCountY = (int)(height / cellSize); 
            cellCountX = (int)(width / cellSize);

            chunkCountY = (int)Mathf.Round(height / chunkSize);
            chunkCountX = (int)Mathf.Round(width / chunkSize);

            // MiniMap camera size
            // [Interflow fix 2026-08-01 grid-headless] Камера миникарты — клиентский рендер: в серверном
            // билде Roles-стрип вырезает Camera → NRE обрывал Awake ДО Initialize() → chunkUnits пуст →
            // KeyNotFoundException в каждом Unit.Spawn («юниты не выходят»). Гейт вместо жёсткой ссылки.
            Transform miniCam = transform.Find("MiniMapCamera");
            Camera miniCamCamera = miniCam != null ? miniCam.GetComponent<Camera>() : null;
            if (miniCamCamera != null) miniCamCamera.orthographicSize = (width > height) ? width * 0.5f : height * 0.5f;

            Initialize();
        }

        public void Initialize()
        {
            // Declares chunkUnits Dictionary
            chunkUnits.Clear();
            for (int i = 0; i < chunkCountY * chunkCountX; i++)
            {
                chunkUnits.Add(i, new List<Unit>());
            }

            // Cell occupancy
            cellOccupancy = new bool[cellCountY * cellCountX];

            // Calculate cell occupancy
            GameObject environment = GameObject.Find("/Environment");
            if (environment != null)
            {
                PopulateGrid(environment.transform);
            }
        }

        // CHUNKS ----------------------------------------------------------------------------------------------------------------------------------------------------------------

        // [Interflow fix 2026-06-26] Чанк-координата позиции с клампом в границы сетки.
        // Без клампа позиция вне [0,width)×[0,height) даёт ключ за пределами chunkUnits → KeyNotFoundException (Grid.cs:76).
        // Юнит за краем привязывается к крайнему чанку — это безопаснее краша; первопричину (юнит вне сетки) проверять отдельно.
        private static Coordinate ChunkCoordClamped(Unit unit)
        {
            int cx = Mathf.Clamp((int)Math.Floor(unit.transform.position.x / Grid.instance.chunkSize), 0, chunkCountX - 1);
            int cy = Mathf.Clamp((int)Math.Floor(unit.transform.position.z / Grid.instance.chunkSize), 0, chunkCountY - 1);
            return new Coordinate(cx, cy);
        }

        // Assigns unit to its corresponding chunk cell
        public static void AssignToChunk(Unit unit)
        {
            Coordinate newCell = ChunkCoordClamped(unit);   // [Interflow fix 2026-06-26]

            if (unit.currentCell != newCell)
            {
                chunkUnits[unit.currentCell.x + unit.currentCell.y * chunkCountX].Remove(unit);
                chunkUnits[newCell.x + newCell.y * chunkCountX].Add(unit);

                unit.currentCell = newCell;
            }
        }

        // Assign unit at the start to the chunk cell. Difference with above it has no Remove method
        public static void AssignToChunkInitial(Unit unit)
        {
            Coordinate newCell = ChunkCoordClamped(unit);   // [Interflow fix 2026-06-26]

            chunkUnits[newCell.x + newCell.y * chunkCountX].Add(unit);
            unit.currentCell = newCell;
        }

        // Remove from cell
        public static void RemoveFromChunk(Unit unit)
        {
            chunkUnits[unit.currentCell.x + unit.currentCell.y * chunkCountX].Remove(unit);
        }

        // CELLS ------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Populate Grid
        // Environment object should contain environment props. Object that do not have EntityCore script attached
        // Children of object are not taken into account, only of empty objects
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
                if (obj.GetComponent<BoxCollider>())
                {
                    Vector2[] rect = GetRectangleFromCollider(obj);

                    for (int x = 0; x < cellCountX; x++)
                    {
                        for (int y = 0; y < cellCountY; y++)
                        {
                            // Counter clockwise
                            Vector2[] cellRect = new Vector2[4];
                            cellRect[0] = new Vector2(x * cellSize, y * cellSize);
                            cellRect[1] = new Vector2(x * cellSize + cellSize, y * cellSize);
                            cellRect[2] = new Vector2(x * cellSize + cellSize, y * cellSize + cellSize);
                            cellRect[3] = new Vector2(x * cellSize, y * cellSize + cellSize);

                            if (IsPolygonsIntersecting(rect, cellRect))
                            {
                                if (removeCellOccupancy) cellOccupancy[x + y * cellCountX] = false;
                                else cellOccupancy[x + y * cellCountX] = true;
                            }
                        }
                    }
                }
            }
        }

        // Returns array of cells that are occupied by specified object
        public int[] GetOccupiedCells(Transform obj, out bool occupiedCellIntersection)
        {
            occupiedCellIntersection = false;
            List<int> cells = new List<int>();
            if (obj.GetComponent<BoxCollider>())
            {
                Vector2[] rect = GetRectangleFromCollider(obj);

                for (int x = 0; x < cellCountX; x++)
                {
                    for (int y = 0; y < cellCountY; y++)
                    {
                        // Counter clockwise
                        Vector2[] cellRect = new Vector2[4];
                        cellRect[0] = new Vector2(x * cellSize, y * cellSize);
                        cellRect[1] = new Vector2(x * cellSize + cellSize, y * cellSize);
                        cellRect[2] = new Vector2(x * cellSize + cellSize, y * cellSize + cellSize);
                        cellRect[3] = new Vector2(x * cellSize, y * cellSize + cellSize);

                        if (IsPolygonsIntersecting(rect, cellRect))
                        {
                            cells.Add(x + y * cellCountX);

                            if (cellOccupancy[x + y * cellCountX])
                            {
                                occupiedCellIntersection = true;
                            }
                        }
                    }
                }
            }

            return cells.ToArray();
        }


        // Debug ----------------------------------------------------------------------------------------------------------------------------------------------------------

        // Draw grid
        void OnDrawGizmos()
        {
            // Grids
            for (int x = 0; x < cellCountX; x++)
            {
                for (int y = 0; y < cellCountY; y++)
                {
                    if (cellOccupancy[x + y * cellCountX] == true)
                    {
                        Gizmos.color = new Color(1, 0, 0, 0.2f);
                    }
                    else
                    {
                        Gizmos.color = new Color(0, 1, 0, 0.2f);
                    }
                    Gizmos.DrawCube(new Vector3(x * cellSize + cellSize * 0.5f, 0, y * cellSize + cellSize * 0.5f), new Vector3(cellSize, 0.1f, cellSize));
                }
            }
        }

        // Helpers --------------------------------------------------------------------------------------------------------------------------------------------------------

        // Returns True if cell is occupied
        public bool CellOccupancy(Coordinate cell)
        {
            return cellOccupancy[cell.x + cell.y * cellCountX];
        }

        // Returns rectangle points ABCD from box collider to be used in checking which grid cells are occupied
        // First we get vertices of box collider
        // Then convert it to 2D rectangle - CounterClockwise
        public Vector2[] GetRectangleFromCollider(Transform GO)
        {
            Vector3[] vertices = new Vector3[8];

            BoxCollider b = GO.GetComponent<BoxCollider>();
            vertices[0] = GO.transform.TransformPoint(b.center + new Vector3(-b.size.x, b.size.y, -b.size.z) * 0.5f);
            vertices[1] = GO.transform.TransformPoint(b.center + new Vector3(-b.size.x, b.size.y, b.size.z) * 0.5f);
            vertices[2] = GO.transform.TransformPoint(b.center + new Vector3(b.size.x, b.size.y, b.size.z) * 0.5f);
            vertices[3] = GO.transform.TransformPoint(b.center + new Vector3(b.size.x, b.size.y, -b.size.z) * 0.5f);

            vertices[4] = GO.transform.TransformPoint(b.center + new Vector3(-b.size.x, -b.size.y, -b.size.z) * 0.5f);
            vertices[5] = GO.transform.TransformPoint(b.center + new Vector3(b.size.x, -b.size.y, -b.size.z) * 0.5f);
            vertices[6] = GO.transform.TransformPoint(b.center + new Vector3(b.size.x, -b.size.y, b.size.z) * 0.5f);
            vertices[7] = GO.transform.TransformPoint(b.center + new Vector3(-b.size.x, -b.size.y, b.size.z) * 0.5f);

            // Convert to 2D rectangle
            Vector2[] rectABCD = new Vector2[4];
            rectABCD[3] = new Vector2(vertices[0][0], vertices[0][2]);
            rectABCD[2] = new Vector2(vertices[1][0], vertices[1][2]);
            rectABCD[1] = new Vector2(vertices[2][0], vertices[2][2]);
            rectABCD[0] = new Vector2(vertices[3][0], vertices[3][2]);

            return rectABCD;
        }

        // Rectangle is represented by points ABCD - Clockwise
        // Test if given point m is inside rectangle r is given by:
        //
        // 0 <= dot(AB,AM) <= dot(AB,AB) && 0 <= dot(BC,BM) <= dot(BC,BC)
        //

        public bool IsPointInsideRect(Vector2 m, Vector2[] ABCD_CounterClockwise)
        {
            Vector2[] ABCD = new Vector2[4];
            ABCD[0] = ABCD_CounterClockwise[3];
            ABCD[1] = ABCD_CounterClockwise[2];
            ABCD[2] = ABCD_CounterClockwise[1];
            ABCD[3] = ABCD_CounterClockwise[0];

            Vector2 AB = ABCD[1] - ABCD[0];
            Vector2 AD = ABCD[3] - ABCD[0];
            Vector2 AM = m - ABCD[0];
            float dotAMAB = Vector2.Dot(AM, AB);
            float dotABAB = Vector2.Dot(AB, AB);
            float dotAMAD = Vector2.Dot(AM, AD);
            float dotADAD = Vector2.Dot(AD, AD);
            return 0 <= dotAMAB && dotAMAB <= dotABAB && 0 <= dotAMAD && dotAMAD <= dotADAD;
        }

        // Checks if the two rectangles are intersecting, counter clockwise or clockwise
        bool IsPolygonsIntersecting(Vector2[] a, Vector2[] b)
        {
            foreach (var polygon in new[] { a, b })
            {
                for (int i1 = 0; i1 < polygon.Length; i1++)
                {
                    int i2 = (i1 + 1) % polygon.Length;
                    var p1 = polygon[i1];
                    var p2 = polygon[i2];

                    var normal = new Vector2(p2.y - p1.y, p1.x - p2.x);

                    double? minA = null, maxA = null;
                    foreach (var p in a)
                    {
                        var projected = normal.x * p.x + normal.y * p.y;
                        if (minA == null || projected < minA)
                            minA = projected;
                        if (maxA == null || projected > maxA)
                            maxA = projected;
                    }

                    double? minB = null, maxB = null;
                    foreach (var p in b)
                    {
                        var projected = normal.x * p.x + normal.y * p.y;
                        if (minB == null || projected < minB)
                            minB = projected;
                        if (maxB == null || projected > maxB)
                            maxB = projected;
                    }

                    if (maxA < minB || maxB < minA)
                        return false;
                }
            }
            return true;
        }

        // PATHFINDING --------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // A* Pathfinding
        public static Coordinate[] Pathfind(Coordinate start, Coordinate end)
        {
            PriorityQueue<Coordinate, int> frontier = new PriorityQueue<Coordinate, int>(0);
            frontier.Insert(start, 0);

            Dictionary<Coordinate, Coordinate> came_from = new Dictionary<Coordinate, Coordinate>();
            Dictionary<Coordinate, int> cost_so_far = new Dictionary<Coordinate, int>();

            came_from.Add(start, new Coordinate(-1, -1)); // - 1 means null
            cost_so_far.Add(start, 0);

            Coordinate current;

            while (!frontier.isEmpty())
            {
                current = frontier.Pop();

                if (current.Equals(end))
                    break;

                foreach (Coordinate next in Coordinate.EmptyStraightNeighbors(current)) // To include diagonal cells change to Neighbours
                {
                    int new_cost = cost_so_far[current] + Coordinate.Cost(current, next); // Cost is always 1, not implemented

                    if (!cost_so_far.ContainsKey(next) || new_cost < cost_so_far[next])
                    {
                        cost_so_far[next] = new_cost;

                        int priority = Heuristic(end, next);
                        frontier.Insert(next, priority);

                        came_from[next] = current;
                    }
                }
            }

            // Reconstruct path
            // If does not contain end coordinate, pathfinding failed
            if (!came_from.ContainsKey(end))
            {
                return new Coordinate[0];
            }

            current = end;
            List<Coordinate> path = new List<Coordinate>();
            while (!current.Equals(start))
            {
                path.Add(current);
                current = came_from[current];
            }
            path.Add(start); // Optional start cell inclusion
            path.Reverse(); // Optional path reverse. It will be in correct order from start to end

            return path.ToArray();
        }

        // Manhattan distance on a square grid
        public static int Heuristic(Coordinate a, Coordinate b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }
    }

    public struct Coordinate
    {
        public int x;
        public int y;

        public Coordinate(int X, int Y)
        {
            x = X;
            y = Y;
        }

        public override bool Equals(object coord)
        {
            Coordinate c = (Coordinate)coord;
            if (x == c.x && y == c.y) return true;
            else return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(x, y);
        }

        public static bool operator ==(Coordinate coord1, Coordinate coord2)
        {
            if (coord1.x == coord2.x && coord1.y == coord2.y) return true;
            else return false;
        }

        public static bool operator ==(Coordinate coord1, int coord)
        {
            if (coord1.x == coord && coord1.y == coord) return true;
            else return false;
        }

        public static bool operator !=(Coordinate coord1, Coordinate coord2)
        {
            if (coord1.x != coord2.x || coord1.y != coord2.y) return true;
            else return false;
        }

        public static bool operator !=(Coordinate coord1, int coord)
        {
            if (coord1.x != coord || coord1.y != coord) return true;
            else return false;
        }

        public static int Cost(Coordinate from, Coordinate to)
        {
            // Not implemented, Cost is always 1
            return 1;

            // Diagonal cells are a little more costly
            // if (from.x == to.x || from.y == to.y) return 1;
            // else return 3;
        }
        public static Coordinate[] Neighbors(Coordinate cell, bool includeSelf = false)
        {
            List<Coordinate> neighbors = new List<Coordinate>();

            if (includeSelf)
            {
                neighbors.Add(cell);
            }

            // Left
            if (cell.x > 0)
            {
                // Left center
                neighbors.Add(new Coordinate(cell.x - 1, cell.y));

                // Left bottom
                if (cell.y > 0)
                {
                    neighbors.Add(new Coordinate(cell.x - 1, cell.y - 1));
                }

                // Left top
                if (cell.y != Grid.cellCountY - 1)
                {
                    neighbors.Add(new Coordinate(cell.x - 1, cell.y + 1));
                }
            }

            // Centre bottom
            if (cell.y > 0)
            {
                neighbors.Add(new Coordinate(cell.x, cell.y - 1));
            }

            // Centre top
            if (cell.y != Grid.cellCountY - 1)
            {
                neighbors.Add(new Coordinate(cell.x, cell.y + 1));
            }

            // Right
            if (cell.x != Grid.cellCountX - 1)
            {
                // Right center
                neighbors.Add(new Coordinate(cell.x + 1, cell.y));

                // Right bottom
                if (cell.y > 0)
                {
                    neighbors.Add(new Coordinate(cell.x + 1, cell.y - 1));
                }

                // Right top
                if (cell.y != Grid.cellCountY - 1)
                {
                    neighbors.Add(new Coordinate(cell.x + 1, cell.y + 1));
                }
            }

            return neighbors.ToArray();
        }

        public static Coordinate[] EmptyNeighbors(Coordinate cell, bool includeSelf = false)
        {
            List<Coordinate> neighbors = new List<Coordinate>();

            if (includeSelf)
            {
                neighbors.Add(cell);
            }

            // Left
            if (cell.x > 0)
            {
                // Left center
                if (!Grid.cellOccupancy[(cell.x - 1) + cell.y * Grid.cellCountX])
                {   
                    neighbors.Add(new Coordinate(cell.x - 1, cell.y));
                }

                // Left bottom
                if (cell.y > 0 && !Grid.cellOccupancy[(cell.x - 1) + (cell.y - 1) * Grid.cellCountX])
                {
                    neighbors.Add(new Coordinate(cell.x - 1, cell.y - 1));
                }

                // Left top
                if (cell.y != Grid.cellCountY - 1 && !Grid.cellOccupancy[(cell.x - 1) + (cell.y + 1) * Grid.cellCountX])
                {
                    neighbors.Add(new Coordinate(cell.x - 1, cell.y + 1));
                }
            }

            // Centre bottom
            if (cell.y > 0 && !Grid.cellOccupancy[cell.x + (cell.y - 1) * Grid.cellCountX])
            {
                neighbors.Add(new Coordinate(cell.x, cell.y - 1));
            }

            // Centre top
            if (cell.y != Grid.cellCountY - 1 && !Grid.cellOccupancy[cell.x + (cell.y + 1) * Grid.cellCountX])
            {
                neighbors.Add(new Coordinate(cell.x, cell.y + 1));
            }

            // Right
            if (cell.x != Grid.cellCountX - 1)
            {
                // Right center
                if (Grid.cellOccupancy[(cell.x + 1) + cell.y * Grid.cellCountX])
                {
                    neighbors.Add(new Coordinate(cell.x + 1, cell.y));
                }
           
                // Right bottom
                if (cell.y > 0 && !Grid.cellOccupancy[(cell.x + 1) + (cell.y - 1) * Grid.cellCountX])
                {
                    neighbors.Add(new Coordinate(cell.x + 1, cell.y - 1));
                }

                // Right top
                if (cell.y != Grid.cellCountY - 1 && !Grid.cellOccupancy[(cell.x + 1) + (cell.y + 1) * Grid.cellCountX])
                {
                    neighbors.Add(new Coordinate(cell.x + 1, cell.y + 1));
                }
            }

            return neighbors.ToArray();
        }

        // Returns neighbouring cells that are vertical and horizontal. No diagonal cells included
        public static Coordinate[] StraightNeighbors(Coordinate cell, bool includeSelf = false)
        {
            List<Coordinate> neighbors = new List<Coordinate>();

            if (includeSelf)
            {
                neighbors.Add(cell);
            }

            // Left
            if (cell.x > 0)
            {
                // Left center
                neighbors.Add(new Coordinate(cell.x - 1, cell.y));
            }

            // Centre bottom
            if (cell.y > 0)
            {
                neighbors.Add(new Coordinate(cell.x, cell.y - 1));
            }

            // Centre top
            if (cell.y != Grid.cellCountY - 1)
            {
                neighbors.Add(new Coordinate(cell.x, cell.y + 1));
            }

            // Right
            if (cell.x != Grid.cellCountX - 1)
            {
                // Right center
                neighbors.Add(new Coordinate(cell.x + 1, cell.y));
            }

            return neighbors.ToArray();
        }

        // Returns EMPTY neighbouring cells that are vertical and horizontal. No diagonal cells included
        public static Coordinate[] EmptyStraightNeighbors(Coordinate cell, bool includeSelf = false)
        {
            List<Coordinate> neighbors = new List<Coordinate>();

            if (includeSelf && !Grid.cellOccupancy[cell.x + cell.y * Grid.cellCountX])
            {
                neighbors.Add(cell);
            }

            // Left
            if (cell.x > 0 && !Grid.cellOccupancy[(cell.x - 1) + cell.y * Grid.cellCountX])
            {
                // Left center
                neighbors.Add(new Coordinate(cell.x - 1, cell.y));
            }

            // Centre bottom
            if (cell.y > 0 && !Grid.cellOccupancy[cell.x + (cell.y - 1) * Grid.cellCountX])
            {
                neighbors.Add(new Coordinate(cell.x, cell.y - 1));
            }

            // Centre top
            if (cell.y != Grid.cellCountY - 1 && !Grid.cellOccupancy[cell.x + (cell.y + 1) * Grid.cellCountX])
            {
                neighbors.Add(new Coordinate(cell.x, cell.y + 1));
            }

            // Right
            if (cell.x != Grid.cellCountX - 1 && !Grid.cellOccupancy[(cell.x + 1) + cell.y * Grid.cellCountX])
            {
                // Right center
                neighbors.Add(new Coordinate(cell.x + 1, cell.y));
            }

            return neighbors.ToArray();
        }

        // Chunks
        public static Coordinate GetChunkByPosition(Vector2 pos)
        {
            return new Coordinate((int)(pos.x / Grid.instance.chunkSize), (int)(pos.y / Grid.instance.chunkSize));
        }

        public static Vector2 GetChunkPosition(Coordinate cell)
        {
            return new Vector2(cell.x * Grid.instance.chunkSize, cell.y * Grid.instance.chunkSize);
        }

        // Cells
        public static Coordinate GetCellByPosition(Vector2 pos)
        {
            return new Coordinate((int)(pos.x / Grid.instance.cellSize), (int)(pos.y / Grid.instance.cellSize));
        }

        public static Vector2 GetCellPosition(Coordinate cell)
        {
            return new Vector2(cell.x * Grid.instance.cellSize, cell.y * Grid.instance.cellSize);
        }
    }
}
