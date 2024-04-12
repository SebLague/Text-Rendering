using System.Collections.Generic;
using UnityEngine;
using System;
using SebText.FontLoading;
using static SebText.FontLoading.FontData;

namespace SebText.Rendering.Helpers
{
    public static class GlyphHelper
    {

        public static TextRenderData CreateRenderData(GlyphData[] uniqueCharacters, FontData fontData)
        {
            TextRenderData renderData = new();

            float scale = 1.0f / fontData.UnitsPerEm;
            for (int charIndex = 0; charIndex < uniqueCharacters.Length; charIndex++)
            {
                List<Vector2[]> contours = CreateContoursWithImpliedPoints(uniqueCharacters[charIndex], scale);

                var glyphBounds = GetBounds(uniqueCharacters[charIndex], fontData);
                TextRenderData.GlyphData glyphData = new()
                {
                    Size = glyphBounds.size,
                    ContourDataOffset = renderData.GlyphMetaData.Count,
                    PointDataOffset = renderData.BezierPoints.Count,
                    NumContours = contours.Count
                };

                renderData.AllGlyphData.Add(glyphData);
                renderData.GlyphMetaData.Add(renderData.BezierPoints.Count);
                renderData.GlyphMetaData.Add(contours.Count);

                foreach (var contour in contours)
                {
                    renderData.GlyphMetaData.Add(contour.Length - 1);
                    for (int i = 0; i < contour.Length; i++)
                    {
                        renderData.BezierPoints.Add(contour[i] - glyphBounds.centre);

                    }
                }
            }

            return renderData;
        }


        public static List<Vector2[]> CreateContoursWithImpliedPoints(GlyphData character, float scale)
        {
            const bool convertStraightLinesToBezier = true;

            int startPointIndex = 0;
            int contourCount = character.ContourEndIndices.Length;

            List<Vector2[]> contours = new();

            for (int contourIndex = 0; contourIndex < contourCount; contourIndex++)
            {
                int contourEndIndex = character.ContourEndIndices[contourIndex];
                int numPointsInContour = contourEndIndex - startPointIndex + 1;
                Span<Point> contourPoints = character.Points.AsSpan(startPointIndex, numPointsInContour);

                List<Vector2> reconstructedPoints = new();
                List<Vector2> onCurvePoints = new();

                // Get index of first on-curve point (seems to not always be first point for whatever reason)
                int firstOnCurvePointIndex = 0;
                for (int i = 0; i < contourPoints.Length; i++)
                {
                    if (contourPoints[i].OnCurve)
                    {
                        firstOnCurvePointIndex = i;
                        break;
                    }
                }

                for (int i = 0; i < contourPoints.Length; i++)
                {
                    Point curr = contourPoints[(i + firstOnCurvePointIndex + 0) % contourPoints.Length];
                    Point next = contourPoints[(i + firstOnCurvePointIndex + 1) % contourPoints.Length];

                    reconstructedPoints.Add(new Vector2(curr.X * scale, curr.Y * scale));
                    if (curr.OnCurve) onCurvePoints.Add(new Vector2(curr.X * scale, curr.Y * scale));
                    bool isConsecutiveOffCurvePoints = !curr.OnCurve && !next.OnCurve;
                    bool isStraightLine = curr.OnCurve && next.OnCurve;

                    if (isConsecutiveOffCurvePoints || (isStraightLine && convertStraightLinesToBezier))
                    {
                        bool onCurve = isConsecutiveOffCurvePoints;
                        float newX = (curr.X + next.X) / 2.0f * scale;
                        float newY = (curr.Y + next.Y) / 2.0f * scale;
                        reconstructedPoints.Add(new Vector2(newX, newY));
                        if (onCurve) onCurvePoints.Add(new Vector2(newX, newY));
                    }
                }
                reconstructedPoints.Add(reconstructedPoints[0]);
                reconstructedPoints = MakeMonotonic(reconstructedPoints);


                contours.Add(reconstructedPoints.ToArray());

                startPointIndex = contourEndIndex + 1;
            }

            return contours;
        }

        public static (Vector2 centre, Vector2 size) GetBounds(GlyphData character, FontData fontData)
        {
            const float antiAliasPadding = 0.005f;
            float scale = 1f / fontData.UnitsPerEm;

            float left = character.MinX * scale;
            float right = character.MaxX * scale;
            float top = character.MaxY * scale;
            float bottom = character.MinY * scale;

            Vector2 centre = new Vector2(left + right, top + bottom) / 2;
            Vector2 size = new Vector2(right - left, top - bottom) + Vector2.one * antiAliasPadding;
            return (centre, size);
        }

        public static List<Vector2> MakeMonotonic(List<Vector2> original)
        {
            List<Vector2> monotonic = new(original.Count);
            monotonic.Add(original[0]);

            for (int i = 0; i < original.Count - 1; i += 2)
            {
                Vector2 p0 = original[i];
                Vector2 p1 = original[i + 1];
                Vector2 p2 = original[i + 2];

                if ((p1.y < Mathf.Min(p0.y, p2.y) || p1.y > Mathf.Max(p0.y, p2.y)))
                {
                    var split = SplitAtTurningPointY(p0, p1, p2);
                    monotonic.Add(split.a1);
                    monotonic.Add(split.a2);
                    monotonic.Add(split.b1);
                    monotonic.Add(split.b2);
                }
                else
                {
                    monotonic.Add(p1);
                    monotonic.Add(p2);
                }
            }
            return monotonic;
        }

        static (Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2) SplitAtTurningPointY(Vector2 p0, Vector2 p1, Vector2 p2)
        {
            Vector2 a = p0 - 2 * p1 + p2;
            Vector2 b = 2 * (p1 - p0);
            Vector2 c = p0;

            // Calculate turning point by setting gradient.y to 0: 2at + b = 0; therefore t = -b / 2a
            float turningPointT = -b.y / (2 * a.y);
            Vector2 turningPoint = a * turningPointT * turningPointT + b * turningPointT + c;

            // Calculate the new p1 point for curveA with points: p0, p1A, turningPoint
            // This is done by saying that p0 + gradient(t=0) * ? = p1A = (p1A.x, turningPoint.y)
            // Solve for lambda using the known turningPoint.y, and then solve for p1A.x
            float lambdaA = (turningPoint.y - p0.y) / b.y;
            float p1A_x = p0.x + b.x * lambdaA;

            // Calculate the new p1 point for curveB with points: turningPoint, p1B, p2
            // This is done by saying that p2 + gradient(t=1) * ? = p1B = (p1B.x, turningPoint.y)
            // Solve for lambda using the known turningPoint.y, and then solve for p1B.x
            float lambdaB = (turningPoint.y - p2.y) / (2 * a.y + b.y);
            float p1B_x = p2.x + (2 * a.x + b.x) * lambdaB;

            return (new Vector2(p1A_x, turningPoint.y), turningPoint, new Vector2(p1B_x, turningPoint.y), p2);
        }


        [System.Serializable]
        public class TextRenderData
        {
            public List<Vector2> BezierPoints = new();
            public List<GlyphData> AllGlyphData = new();
            // Metadata for each glyph: bezier data offset, num contours, contour length/s
            public List<int> GlyphMetaData = new();

            [System.Serializable]
            public struct GlyphData
            {
                public int NumContours;
                public int ContourDataOffset;
                public int PointDataOffset;
                public Vector2 Size;
            }

        }
    }
}