using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // FogOfWar.Cells.cs — привязка юнитов к клеткам + временное раскрытие. Вырезано 1:1 из FogOfWar.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class FogOfWar
    {

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
