using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    public enum Cardinal
    {
        East,
        North,
        West,
        South
    }

    public class Quadrant
    {
        public Cardinal cardinal { get; set; } = Cardinal.East;
        public Coordinate originPoint { get; set; } = new Coordinate();

        public Quadrant(Cardinal cardinal, Coordinate originPoint)
        {
            this.cardinal = cardinal;
            this.originPoint = originPoint;
        }

        public Coordinate QuadrantToLevel(Coordinate quadrantVector)
        {
            switch (cardinal)
            {
                default:
                case Cardinal.East:
                    return new Coordinate(originPoint.x + quadrantVector.x, originPoint.y + quadrantVector.y);
                case Cardinal.North:
                    return new Coordinate(originPoint.x - quadrantVector.y, originPoint.y + quadrantVector.x);
                case Cardinal.West:
                    return new Coordinate(originPoint.x - quadrantVector.x, originPoint.y - quadrantVector.y);
                case Cardinal.South:
                    return new Coordinate(originPoint.x + quadrantVector.y, originPoint.y - quadrantVector.x);
            }
        }
    }

    public class Column
    {
        // Depth is the x axis of the eastern quadrant
        public int depth { get; private set; } = 0;
        public int maxDepth { get; private set; } = 0;

        // StartSlope is the 'lower' one, and the endSlope is the 'higher' one
        public float startSlope { get; set; } = 0;
        public float endSlope { get; set; } = 0;

        public Column(int depth, int maxDepth, float startSlope, float endSlope)
        {
            this.depth = depth;
            this.maxDepth = maxDepth;
            this.startSlope = startSlope;
            this.endSlope = endSlope;
        }

        public List<Coordinate> GetTiles()
        {
            List<Coordinate> quadrantPoints = new List<Coordinate>();

            int minRow = Mathf.RoundToInt(depth * startSlope);
            int maxRow = Mathf.RoundToInt(depth * endSlope);

            for (int i = minRow; i < maxRow + 1; i++)
            {
                quadrantPoints.Add(new Coordinate(depth, i));
            }

            if (endSlope == 1)
            {
                quadrantPoints.RemoveAt(quadrantPoints.Count - 1);
            }

            return quadrantPoints;
        }

        public bool IsProceedable()
        {
            return (depth < maxDepth);
        }

        public void ProceedIfPossible()
        {
            if (depth < maxDepth)
            {
                depth += 1;
            }
        }
    }
}