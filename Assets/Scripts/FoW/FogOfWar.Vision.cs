using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // FogOfWar.Vision.cs — пересчёт видимости (UpdateVision*/тайлы/квадранты). Вырезано 1:1 из FogOfWar.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class FogOfWar
    {
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
    }
}
