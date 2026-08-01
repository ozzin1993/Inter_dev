using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Utils.Geometry.cs — способности/геометрия/уникальные ID SO. Вырезано 1:1 из Utils.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public static partial class Utils
    {
        // ============================= ABILITIES =================================================================

        /// <summary>
        /// Retrieves an ability by its hierarchical name.
        /// The name should be formatted as "1-5-6", representing unit.abilities[1].abilities[5].abilities[6].
        /// </summary>
        /// <param name="unit">The unit containing abilities.</param>
        /// <param name="name">The hierarchical name of the ability (e.g., "1-5-6").</param>
        /// <param name="abilityIndex">Outputs the global ability index.</param>
        /// <returns>The requested ability if found, otherwise null.</returns>
        public static Ability GetAbilityByName(Unit unit, string name, out int abilityIndex)
        {
            Ability ability;

            string[] names = name.Split('-');

            if (names.Length > 0)
            {
                if (int.TryParse(names[0], out int initialIndex))
                {
                    ability = unit.abilities[initialIndex];

                    for (int i = 1; i < names.Length; i++)
                    {
                        Container ac = (Container)ability;

                        if (int.TryParse(names[i], out int index))
                        {
                            ability = ac.abilities[index];
                        }
                        else
                        {
                            abilityIndex = 0;
                            return null;
                        }
                    }

                    abilityIndex = initialIndex;
                    return ability;
                }
            }

            abilityIndex = 0;
            return null;
        }

        /// <summary>
        /// Retrieves an ability by its global index.
        /// </summary>
        /// <param name="unit">The unit containing abilities.</param>
        /// <param name="name">The global index of the ability as a string.</param>
        /// <param name="abilityIndex">Outputs the global index of the ability if found, otherwise -1.</param>
        /// <returns>The requested ability if found, otherwise null.</returns>
        public static Ability GetAbilityByIndexName(Unit unit, string name, out int abilityIndex)
        {
            // Try to parse the name
            if (int.TryParse(name, out int abilityGlobalIndex))
            {
                int currentIndexCount = -1;
                abilityIndex = abilityGlobalIndex;
                return RecursiveAbilitySearch(unit.abilities, abilityGlobalIndex, ref currentIndexCount);
            }
            else
            {
                abilityIndex = -1;
                return null;
            }
        }

        /// <summary>
        /// Retrieves an ability from a unit by its global index.
        /// </summary>
        /// <param name="unit">The unit containing abilities.</param>
        /// <param name="index">The global index of the ability.</param>
        /// <returns>The ability if found, otherwise null.</returns>
        public static Ability GetAbilityByIndex(Unit unit, int index)
        {
            int currentIndexCount = -1;
            return RecursiveAbilitySearch(unit.abilities, index, ref currentIndexCount);
        }

        /// <summary>
        /// Recursively searches through all abilities and ability containers to find an ability by its global index.
        /// </summary>
        /// <param name="recursiveAbilities">The current ability list being searched.</param>
        /// <param name="abilityIndex">The target global index of the ability.</param>
        /// <param name="currentIndexCount">A reference counter tracking the current traversal index.</param>
        /// <returns>The ability if found, otherwise null.</returns>
        private static Ability RecursiveAbilitySearch(Ability[] recursiveAbilities, int abilityIndex, ref int currentIndexCount)
        {
            for (int i = 0; i < recursiveAbilities.Length; i++)
            {
                currentIndexCount = currentIndexCount + 1;
                if (currentIndexCount == abilityIndex)
                {
                    // Ability found return ability
                    return recursiveAbilities[i];
                }
                else if (recursiveAbilities[i].type == AbilityType.Container)
                {
                    Container container = (Container)recursiveAbilities[i];

                    Ability abilityFound = RecursiveAbilitySearch(container.abilities, abilityIndex, ref currentIndexCount);
                    if (abilityFound != null)
                    {
                        return abilityFound;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Retrieves the global index of an ability.
        /// </summary>
        /// <param name="abilities">The root ability list to search within.</param>
        /// <param name="abilityToSearch">The ability to locate.</param>
        /// <returns>The global index of the ability, or -1 if not found.</returns>
        public static int GetAbilityIndex(Ability[] abilities, Ability abilityToSearch)
        {
            int currentIndexCount = -1;
            bool abilityFound = RecursiveAbilityIndexSearch(abilities);

            return currentIndexCount;

            bool RecursiveAbilityIndexSearch(Ability[] recursiveAbilities)
            {
                for (int i = 0; i < recursiveAbilities.Length; i++)
                {
                    currentIndexCount++;
                    if (abilityToSearch == recursiveAbilities[i])
                    {
                        // Ability found return abilityIndex
                        return true;
                    }
                    else if (recursiveAbilities[i].type == AbilityType.Container)
                    {
                        Container container = (Container)recursiveAbilities[i];
                        if (RecursiveAbilityIndexSearch(container.abilities))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        // ============================= GEOMETRY =================================================================

        /// <summary>
        /// Retrieves the four corner points of a 2D rectangle derived from a 3D BoxCollider.
        /// The rectangle is oriented counterclockwise and can be expanded outward.
        /// </summary>
        /// <param name="GO">The Transform of the GameObject containing the BoxCollider.</param>
        /// <param name="expand">Optional expansion distance applied outward from the center.</param>
        /// <returns>An array of 4 Vector2 points representing the rectangle in 2D space.</returns>
        public static Vector2[] GetRectangleFromCollider(Transform GO, float expand = 0)
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

            if (expand == 0) return rectABCD;
            else
            {
                // Expand 4 vtx in opposite to the center of the rectangle
                Vector2[] expandedRect = new Vector2[4];

                Vector2 center = (rectABCD[0] + rectABCD[1] + rectABCD[2] + rectABCD[3]) / 4;
                for (int i = 0; i < rectABCD.Length; i++)
                {
                    expandedRect[i] = (rectABCD[i] - center).normalized * expand + rectABCD[i];
                }

                return expandedRect;
            }
        }

        /// <summary>
        /// Determines if a given point is inside a rectangle defined by four corner points.
        /// The rectangle is assumed to be axis-aligned, and the order of points must be counterclockwise.
        /// </summary>
        /// <param name="m">The point to check.</param>
        /// <param name="ABCD_CounterClockwise">An array of four Vector2 points representing the rectangle in counterclockwise order.</param>
        /// <returns>True if the point is inside the rectangle, otherwise false.</returns>
        /// <remarks>
        /// This method is based on the vector projection test:
        /// 0 ≤ dot(AB, AM) ≤ dot(AB, AB) && 0 ≤ dot(AD, AM) ≤ dot(AD, AD)
        /// It does NOT work for arbitrary quadrilateral shapes.
        /// </remarks>
        public static bool IsPointInsideRect(Vector2 m, Vector2[] ABCD_CounterClockwise)
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

        /// <summary>
        /// Determines whether a circle with a given center and radius is inside or intersects a quadrilateral.
        /// </summary>
        /// <param name="m">The center of the circle.</param>
        /// <param name="radius">The radius of the circle.</param>
        /// <param name="ABCD">An array of four Vector2 points representing the quadrilateral's vertices.</param>
        /// <returns>True if the circle is inside or intersects the quadrilateral, otherwise false.</returns>
        /// <remarks>
        /// The method first checks if the circle's center is inside the quadrilateral using <c>IsPointInQuadrilateral</c>.
        /// If not, it checks if the circle intersects any of the quadrilateral's edges using <c>IsCircleIntersectingEdge</c>.
        /// This works for arbitrary four-vertex polygons, and the vertex order is not important.
        /// </remarks>
        public static bool IsCircleInQuadrilateral(Vector2 m, float radius, Vector2[] ABCD)
        {
            // Ensure ABCD contains exactly 4 vertices
            if (ABCD.Length != 4)
            {
                Debug.LogError("ABCD must contain exactly 4 vertices.");
                return false;
            }

            // Check if the center is inside the quadrilateral
            bool isCenterInside = IsPointInQuadrilateral(m, ABCD);
            if (isCenterInside)
            {
                return true;
            }

            // Check if the circle intersects any of the quadrilateral's edges
            for (int i = 0; i < 4; i++)
            {
                Vector2 p1 = ABCD[i];
                Vector2 p2 = ABCD[(i + 1) % 4];

                if (IsCircleIntersectingEdge(m, radius, p1, p2))
                {
                    return true;
                }
            }

            // The circle is neither inside the quadrilateral nor intersecting any edges
            return false;
        }

        /// <summary>
        /// Determines whether a point is inside a quadrilateral.
        /// </summary>
        /// <param name="m">The point to check.</param>
        /// <param name="ABCD">An array of four Vector2 points representing the quadrilateral's vertices.</param>
        /// <returns>True if the point is inside the quadrilateral, otherwise false.</returns>
        /// <remarks>
        /// This method splits the quadrilateral into two triangles:
        /// - Triangle 1: Formed by the first three vertices (A, B, C)
        /// - Triangle 2: Formed by the first, third, and fourth vertices (A, C, D)
        ///
        /// The point is considered inside the quadrilateral if it is inside either of these triangles.
        /// It assumes the quadrilateral is convex and its vertices are ordered correctly.
        /// </remarks>
        private static bool IsPointInQuadrilateral(Vector2 m, Vector2[] ABCD)
        {
            // Triangle 1: A, B, C
            bool inTriangle1 = IsPointInTriangle(m, ABCD[0], ABCD[1], ABCD[2]);

            // Triangle 2: A, C, D
            bool inTriangle2 = IsPointInTriangle(m, ABCD[0], ABCD[2], ABCD[3]);

            return inTriangle1 || inTriangle2;
        }

        /// <summary>
        /// Determines whether a point is inside a triangle using the barycentric coordinate method.
        /// </summary>
        /// <param name="p">The point to check.</param>
        /// <param name="a">The first vertex of the triangle.</param>
        /// <param name="b">The second vertex of the triangle.</param>
        /// <param name="c">The third vertex of the triangle.</param>
        /// <returns>True if the point is inside the triangle (including edges), otherwise false.</returns>
        /// <remarks>
        /// The function calculates barycentric coordinates (u, v) to determine if the point lies within the triangle.
        /// The point is inside if: 
        /// - u >= 0
        /// - v >= 0
        /// - u + v <= 1
        ///
        /// This method is computationally efficient as it avoids costly cross products and division inside loops.
        /// It assumes that the triangle is well-formed (non-degenerate).
        /// </remarks>
        private static bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = c - a;
            Vector2 v1 = b - a;
            Vector2 v2 = p - a;

            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);

            float invDenom = 1 / (dot00 * dot11 - dot01 * dot01);
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;

            return (u >= 0) && (v >= 0) && (u + v <= 1);
        }

        /// <summary>
        /// Determines whether a circle intersects a line segment.
        /// </summary>
        /// <param name="center">The center of the circle.</param>
        /// <param name="radius">The radius of the circle.</param>
        /// <param name="p1">The first endpoint of the line segment.</param>
        /// <param name="p2">The second endpoint of the line segment.</param>
        /// <returns>True if the circle intersects the line segment, otherwise false.</returns>
        /// <remarks>
        /// This method works by projecting the circle's center onto the edge and clamping the projection within the segment.
        /// Then, it finds the closest point on the segment to the circle's center and checks if the distance is within the radius.
        /// </remarks>
        private static bool IsCircleIntersectingEdge(Vector2 center, float radius, Vector2 p1, Vector2 p2)
        {
            // Vector from p1 to p2
            Vector2 edge = p2 - p1;

            // Vector from p1 to center
            Vector2 centerToP1 = center - p1;

            // Project centerToP1 onto the edge vector
            float projection = Vector2.Dot(centerToP1, edge.normalized);

            // Clamp the projection to the edge length
            float clampedProjection = Mathf.Clamp(projection, 0, edge.magnitude);

            // Closest point on the edge to the circle's center
            Vector2 closestPoint = p1 + edge.normalized * clampedProjection;

            // Distance from the circle's center to the closest point on the edge
            float distanceToEdge = Vector2.Distance(center, closestPoint);

            // Check if this distance is less than or equal to the radius
            return distanceToEdge <= radius;
        }

        /// <summary>
        /// Determines whether two convex polygons (represented by their vertices) are intersecting.
        /// </summary>
        /// <param name="a">The first polygon's vertices in counterclockwise or clockwise order.</param>
        /// <param name="b">The second polygon's vertices in counterclockwise or clockwise order.</param>
        /// <returns>True if the polygons intersect, otherwise false.</returns>
        /// <remarks>
        /// This method uses the **Separating Axis Theorem (SAT)** to determine if two polygons overlap.
        /// If there exists an axis along which the projections of both polygons do not overlap, the polygons are not intersecting.
        /// The method iterates through the edges of both polygons, projects them onto a perpendicular axis, and checks for separation.
        /// If no separating axis is found, the polygons intersect.
        /// </remarks>
        public static bool IsPolygonsIntersecting(Vector2[] a, Vector2[] b)
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

        /// <summary>
        /// Computes the grid cells that a straight line passes through using Bresenham's Line Algorithm.
        /// </summary>
        /// <param name="start">The starting cell (inclusive).</param>
        /// <param name="end">The ending cell (inclusive).</param>
        /// <returns>A list of grid cells (Vector2Int) that the line passes through.</returns>
        /// <remarks>
        /// This implementation of Bresenham's algorithm ensures that every cell intersected by the line is included.
        /// It works for any direction and handles both shallow and steep slopes correctly.
        /// </remarks>
        public static List<Vector2Int> GetLineCells(Vector2Int start, Vector2Int end)
        {
            List<Vector2Int> intersectedCells = new List<Vector2Int>();

            // Calculate delta values
            int deltaX = Mathf.Abs(end.x - start.x);
            int deltaY = Mathf.Abs(end.y - start.y);

            // Determine the step direction for x and y
            int stepX = start.x < end.x ? 1 : -1;
            int stepY = start.y < end.y ? 1 : -1;

            // Bresenham's algorithm initialization
            int error = deltaX - deltaY;

            int currentX = start.x;
            int currentY = start.y;

            // Add the starting cell
            intersectedCells.Add(new Vector2Int(currentX, currentY));

            // Loop through all the points until you reach the end
            while (currentX != end.x || currentY != end.y)
            {
                // Calculate error to decide whether to move in x or y
                int error2 = 2 * error;

                if (error2 > -deltaY)
                {
                    error -= deltaY;
                    currentX += stepX;
                }

                if (error2 < deltaX)
                {
                    error += deltaX;
                    currentY += stepY;
                }

                // Add the current cell to the list
                intersectedCells.Add(new Vector2Int(currentX, currentY));
            }

            return intersectedCells;
        }

        /// <summary>
        /// Computes the grid cells that a straight line passes through using Bresenham's Line Algorithm.
        /// </summary>
        /// <param name="start">The starting grid cell.</param>
        /// <param name="end">The ending grid cell.</param>
        /// <returns>A list of Vector2Int representing the grid cells the line crosses.</returns>
        /// <remarks>
        /// This version efficiently handles all line slopes, including vertical, horizontal, and diagonal lines.
        /// </remarks>
        public static List<Vector2Int> GetCellsByLine(Vector2Int start, Vector2Int end)
        {
            List<Vector2Int> cells = new List<Vector2Int>();

            int x = start.x;
            int y = start.y;
            int x2 = end.x;
            int y2 = end.y;

            int w = x2 - x;
            int h = y2 - y;

            int dx1 = 0, dy1 = 0, dx2 = 0, dy2 = 0;

            // Determine step direction for dx1, dy1, dx2, dy2
            if (w < 0) dx1 = -1; else if (w > 0) dx1 = 1;
            if (h < 0) dy1 = -1; else if (h > 0) dy1 = 1;
            if (w < 0) dx2 = -1; else if (w > 0) dx2 = 1;

            int longest = Mathf.Abs(w);
            int shortest = Mathf.Abs(h);

            // Swap if height is greater than width
            if (longest <= shortest)
            {
                longest = Mathf.Abs(h);
                shortest = Mathf.Abs(w);
                if (h < 0) dy2 = -1; else if (h > 0) dy2 = 1;
                dx2 = 0;
            }

            int numerator = longest >> 1;

            // Loop through the line and add the intersected cells
            for (int i = 0; i <= longest; i++)
            {
                cells.Add(new Vector2Int(x, y));  // Add current cell
                numerator += shortest;
                if (numerator >= longest)
                {
                    numerator -= longest;
                    x += dx1;
                    y += dy1;
                }
                else
                {
                    x += dx2;
                    y += dy2;
                }
            }

            return cells;
        }

        // ============================= SCRIPTABLE OBJECTS UNIQUE IDs =================================================================

        /// <summary>
        /// Determines if the second name is a duplicate of the first name.
        /// </summary>
        /// <param name="name1">The first name to compare.</param>
        /// <param name="name2">The second name to compare.</param>
        /// <returns>True if name2 is a duplicate of name1, otherwise false.</returns>
        public static bool GetTheDuplicate(string name1, string name2)
        {
            int number1 = GetNumberFromName(name1);
            int number2 = GetNumberFromName(name2);

            // If one has no number, consider it the base name
            if (number1 == -1 && number2 != -1)
            {
                return true; // name2 is duplicate
            }
            else if (number1 != -1 && number2 == -1)
            {
                return false; // name1 is duplicate
            }
            else
            {
                // Compare the numbers
                if (number1 > number2)
                {
                    return false; // name1 is duplicate
                }
                else
                {
                    return true; // name2 is duplicate
                }
            }
        }

        /// <summary>
        /// Extracts the base name without any trailing numbers.
        /// </summary>
        /// <param name="name">The full name which may contain a number at the end.</param>
        /// <returns>The base name without the trailing number.</returns>
        public static string GetBaseName(string name)
        {
            int lastSpaceIndex = name.LastIndexOf(' ');
            if (lastSpaceIndex != -1 && int.TryParse(name.Substring(lastSpaceIndex + 1), out _))
            {
                return name.Substring(0, lastSpaceIndex);
            }
            return name; // Return the whole name if there's no number
        }

        /// <summary>
        /// Extracts the number at the end of a name, if present.
        /// </summary>
        /// <param name="name">The full name which may contain a trailing number.</param>
        /// <returns>The extracted number or -1 if no number is found.</returns>
        public static int GetNumberFromName(string name)
        {
            int lastSpaceIndex = name.LastIndexOf(' ');
            if (lastSpaceIndex != -1)
            {
                string numberPart = name.Substring(lastSpaceIndex + 1);
                if (int.TryParse(numberPart, out int result))
                {
                    return result;
                }
            }
            return -1; // Return -1 if there's no number at the end
        }
    }
}
