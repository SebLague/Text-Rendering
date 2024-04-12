using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using SebText.FontLoading;
using SebText.Rendering.Helpers;
using SebText.Demo;

public class TestCreator : MonoBehaviour
{
    [Header("Input")]
    public FontExampleLibrary.TypeFace[] fonts;
    public string characters;

    [Header("Settings")]
    public bool generate;
    public int targetTestCountPerGlyph;
    public Vector2 testBoxSizeMinMax;

    [Header("Other")]
    public string serializedData;
    public int testVisIndex;
    public float lineThickness;

    GlyphTest[] tests;

    void Start()
    {
        if (generate)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Generate();

            Debug.Log("Gen time: " + sw.ElapsedMilliseconds + " ms");
        }
    }

    private void OnDrawGizmos()
    {
        if (tests != null && tests.Length > testVisIndex && testVisIndex >= 0)
        {
            // Test vis
            GlyphTest test = tests[testVisIndex];
            DrawTestDebugView(test, true);
        }
    }

    void Generate()
    {

        List<GlyphTest> testList = new();

        foreach (var font in fonts)
        {
            FontData fontData = FontParser.Parse(FontExampleLibrary.GetFontPath(font, FontExampleLibrary.Variant.Bold));
            GlyphTest[] fontTests = new GlyphTest[characters.Length];

            System.Threading.Tasks.Parallel.For(0, characters.Length, i =>
            {
                var rng = new System.Random(42);
                GlyphTest test;
                test = Generate(fontData, characters[i], rng);
                fontTests[i] = test;
            });

            testList.AddRange(fontTests);
        }

        tests = testList.ToArray();
        serializedData = JsonConvert.SerializeObject(tests, Formatting.Indented);
    }

    GlyphTest Generate(FontData fontData, char character, System.Random rng)
    {
        fontData.TryGetGlyph(character, out var c);
        var renderData = GlyphHelper.CreateRenderData(new FontData.GlyphData[] { c }, fontData);

        var xs = renderData.BezierPoints.Select(p => p.x).ToArray();
        var ys = renderData.BezierPoints.Select(p => p.y).ToArray();

        return Generate(xs, ys, renderData.GlyphMetaData, rng);
    }

    GlyphTest Generate(float[] beziersX, float[] beziersY, List<int> meta, System.Random rng)
    {

        float minX = beziersX.Min();
        float maxX = beziersX.Max();
        float minY = beziersY.Min();
        float maxY = beziersY.Max();

        Vector2 boundsSize = new Vector2(maxX - minX, maxY - minY);
        Vector2 boundsCentre = new Vector2((minX + maxX) / 2, (minY + maxY) / 2);

        GlyphTest test = new();
        test.beziersX = beziersX;
        test.beziersY = beziersY;
        test.metaData = meta.ToArray();

        const float padding = 0.025f;

        List<TestBox> testBoxes = new();

        for (int boxGenIt = 0; boxGenIt < targetTestCountPerGlyph * 50; boxGenIt++)
        {

            float size = Mathf.Lerp(testBoxSizeMinMax.x, testBoxSizeMinMax.y, (float)rng.NextDouble());

            Vector2 centre;
            if (rng.NextDouble() < 0.1f)
            {
                float ox = boundsSize.x * (float)(rng.NextDouble() - 0.5f);
                float oy = boundsSize.y * (float)(rng.NextDouble() - 0.5f);
                centre = boundsCentre + new Vector2(ox, oy) * (1 + size);
            }
            else
            {
                int pointIndex = rng.Next(0, test.beziersX.Length);
                float offsetX = -(padding * 2 + (float)(rng.NextDouble() * testBoxSizeMinMax.y / 4));
                float offsetY = ((float)rng.NextDouble() - 0.5f) * testBoxSizeMinMax.x;

                Vector2 refPoint = new Vector2(test.beziersX[pointIndex], test.beziersY[pointIndex]);
                centre = refPoint + Vector2.right * (-size / 2 + offsetX) + Vector2.up * offsetY;
            }
            //Debug.Log("Testing: " + refPoint + " " + offsetX + " " + centre);

            if (BoxIsEmpty(centre, size + padding))
            {
                (bool success, bool inside) = BoxIsInside(centre, size);
                if (!success) continue;
                TestBox box = new TestBox() { centre = centre, size = size, insideFlag = inside };
                testBoxes.Add(box);
            }

            if (testBoxes.Count >= targetTestCountPerGlyph) break;
        }

        test.boxCentresX = testBoxes.Select(b => b.centre.x).ToArray();
        test.boxCentresY = testBoxes.Select(b => b.centre.y).ToArray();
        test.boxSizes = testBoxes.Select(b => b.size).ToArray();
        test.insideFlags = testBoxes.Select(b => b.insideFlag).ToArray();

        if (testBoxes.Count == 0) throw new System.Exception("Failed to create any valid boxes");
        // Debug.Log(testBoxes.Count);
        return test;

        (bool success, bool inside) BoxIsInside(Vector2 centre, float size)
        {

            const int numTests = 10;
            const int falsePositiveThreshold = 0;
            int numInsideResults = 0;
            int numOutsideResults = 0;

            for (int i = 0; i < numTests; i++)
            {
                float t = i / (numTests - 1f);
                float rayX = centre.x + (i % 2 == 0 ? size / 2 : -size / 2);
                float rayY = centre.y + size / 2 - size * t;

                bool isInside = InsideTest(new Vector2(rayX, rayY), test.beziersX, test.beziersY, test.metaData);
                if (isInside) numInsideResults++;
                else numOutsideResults++;
            }

            bool success = true;
            if (Mathf.Min(numInsideResults, numOutsideResults) > falsePositiveThreshold)
            {
                success = false;
                //throw new System.Exception($"Failed to confidently determine box in/out ({numInsideResults} / {numOutsideResults})");
            }
            return (success, numInsideResults > numOutsideResults);
        }

        bool BoxIsEmpty(Vector2 centre, float size)
        {

            foreach (TestBox other in testBoxes)
            {
                if (BoxesOverlap(centre, Vector2.one * size, other.centre, Vector2.one * other.size))
                {
                    return false;
                }
            }

            for (int i = 0; i < test.beziersX.Length; i++)
            {
                float ox = centre.x - test.beziersX[i];
                float oy = centre.y - test.beziersY[i];
                if (Mathf.Abs(ox) <= size / 2 && Mathf.Abs(oy) <= size / 2)
                {
                    return false;
                }
            }

            const int bezRes = 32;
            Vector2 bottomLeft = new Vector2(centre.x - size / 2, centre.y - size / 2);
            Vector2 topLeft = new Vector2(centre.x - size / 2, centre.y + size / 2);
            Vector2 topRight = new Vector2(centre.x + size / 2, centre.y + size / 2);
            Vector2 bottomRight = new Vector2(centre.x + size / 2, centre.y - size / 2);



            int pointOffset = test.metaData[0];
            int numContours = test.metaData[1];

            for (int contIndex = 0; contIndex < numContours; contIndex++)
            {
                int contLength = Mathf.Abs(test.metaData[2 + contIndex]);

                for (int i = 0; i < contLength; i += 2)
                {
                    Vector2 p0 = new Vector2(test.beziersX[i + pointOffset], test.beziersY[i + pointOffset]);
                    Vector2 p1 = new Vector2(test.beziersX[i + 1 + pointOffset], test.beziersY[i + 1 + pointOffset]);
                    Vector2 p2 = new Vector2(test.beziersX[i + 2 + pointOffset], test.beziersY[i + 2 + pointOffset]);

                    Vector2 a = p0 - 2 * p1 + p2;
                    Vector2 b = 2 * (p1 - p0);
                    Vector2 c = p0;

                    Vector2 prev = p0;

                    for (int r = 1; r < bezRes; r++)
                    {
                        float t = r / (bezRes - 1f);
                        Vector2 bezPoint = a * t * t + b * t + c;
                        bool int1 = LineSegmentsIntersect(prev, bezPoint, bottomLeft, topLeft);
                        bool int2 = LineSegmentsIntersect(prev, bezPoint, topLeft, topRight);
                        bool int3 = LineSegmentsIntersect(prev, bezPoint, topRight, bottomRight);
                        bool int4 = LineSegmentsIntersect(prev, bezPoint, bottomRight, bottomLeft);
                        prev = bezPoint;
                        if (int1 || int2 || int3 || int4) return false;
                    }

                }

                pointOffset += contLength + 1;
            }

            return true;
        }

    }


    [System.Serializable]
    public struct GlyphTest
    {
        public float[] beziersX;
        public float[] beziersY;
        public int[] metaData;
        public float[] boxCentresX;
        public float[] boxCentresY;
        public float[] boxSizes;
        public bool[] insideFlags;
    }

    public struct TestBox
    {
        public Vector2 centre;
        public float size;
        public bool insideFlag;
    }


    static bool InsideTest(Vector2 pos, float[] bezX, float[] bezY, int[] meta)
    {
        float rayX = pos.x;
        float rayY = pos.y;
        int numIntersections = 0;

        int pointOffset = meta[0];
        int numContours = meta[1];

        for (int contIndex = 0; contIndex < numContours; contIndex++)
        {
            int contLength = Mathf.Abs(meta[2 + contIndex]);

            for (int i = 0; i < contLength; i += 2)
            {
                Vector2 p0 = new Vector2(bezX[i + pointOffset], bezY[i + pointOffset]);
                Vector2 p1 = new Vector2(bezX[i + 1 + pointOffset], bezY[i + 1 + pointOffset]);
                Vector2 p2 = new Vector2(bezX[i + 2 + pointOffset], bezY[i + 2 + pointOffset]);

                // Skip trivial cases (all points above or all points below)
                if (p0.y < rayY && p1.y < rayY && p2.y < rayY)
                {
                    continue;
                }
                if (p0.y > rayY && p1.y > rayY && p2.y > rayY)
                {
                    continue;
                }


                Vector2 a = p0 - 2 * p1 + p2;
                Vector2 b = 2 * (p1 - p0);
                Vector2 c = p0;
                (float rootA, float rootB) = CalculateQuadraticRoots(a.y, b.y, c.y - rayY);


                bool onCurveA = rootA >= 0 && rootA < 1;
                bool onCurveB = rootB >= 0 && rootB < 1;

                float aX = a.x * rootA * rootA + b.x * rootA + c.x;
                float bX = a.x * rootB * rootB + b.x * rootB + c.x;
                bool rightSideA = aX > rayX;
                bool rightSideB = bX > rayX;
                bool validA = onCurveA && rightSideA;
                bool validB = onCurveB && rightSideB;
                if (validA) numIntersections++;
                if (validB) numIntersections++;
            }

            pointOffset += contLength + 1;
        }


        return numIntersections % 2 != 0;
    }

    public static (float rootA, float rootB) CalculateQuadraticRoots(float a, float b, float c)
    {
        // If 'a' is zero then the equation is a straight line and we cannot use quadratic formula
        if (Mathf.Abs(a) < 0.0001f)
        {
            float linearRoot = CalculateLinearRoot(b, c);
            return (linearRoot, float.NaN);
        }

        float discriminant = b * b - 4 * a * c;
        int numRoots = discriminant < 0 ? 0 : 2;
        float rootA = float.NaN;
        float rootB = float.NaN;

        if (numRoots >= 1)
        {
            float s = System.MathF.Sqrt(discriminant);
            rootA = (-b + s) / (2 * a);
            rootB = (-b - s) / (2 * a);
        }

        return (rootA, rootB);
    }

    // Calculate root of linear equation (value for which a×t + b = 0)
    static float CalculateLinearRoot(float a, float b)
    {
        // If 'a' is zero then the equation is a horizontal line
        if (a == 0)
        {
            if (b == 0) return 0;
            return float.NaN;
        }
        // Solve for t: a×t + b = 0
        return -b / a;
    }


    public static bool BoxesOverlap(Vector2 centreA, Vector2 sizeA, Vector2 centreB, Vector2 sizeB)
    {
        float leftA = centreA.x - sizeA.x / 2;
        float rightA = centreA.x + sizeA.x / 2;
        float topA = centreA.y + sizeA.y / 2;
        float bottomA = centreA.y - sizeA.y / 2;

        float leftB = centreB.x - sizeB.x / 2;
        float rightB = centreB.x + sizeB.x / 2;
        float topB = centreB.y + sizeB.y / 2;
        float bottomB = centreB.y - sizeB.y / 2;

        return leftA <= rightB && rightA >= leftB && topA >= bottomB && bottomA <= topB;
    }

    public static bool LineSegmentsIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        float d = (b2.x - b1.x) * (a1.y - a2.y) - (a1.x - a2.x) * (b2.y - b1.y);
        if (d == 0)
            return false;
        float t = ((b1.y - b2.y) * (a1.x - b1.x) + (b2.x - b1.x) * (a1.y - b1.y)) / d;
        float u = ((a1.y - a2.y) * (a1.x - b1.x) + (a2.x - a1.x) * (a1.y - b1.y)) / d;

        return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }

    public static void DrawTestDebugView(GlyphTest test, bool showAllWindows, int windowIndex = 0, float bezierPointSizeMultiplier = 1, int bezHighlightIndex = -1, float lineThickness = .01f)
    {
        const int bezRes = 16;


        int pointOffset = test.metaData[0];
        int numContours = test.metaData[1];
        float thickScale = Camera.main.orthographicSize;
        for (int contIndex = 0; contIndex < numContours; contIndex++)
        {
            int contLength = Mathf.Abs(test.metaData[2 + contIndex]);

            for (int i = 0; i < contLength; i += 2)
            {
                Vector2 p0 = new Vector2(test.beziersX[i + pointOffset], test.beziersY[i + pointOffset]);
                Vector2 p1 = new Vector2(test.beziersX[i + 1 + pointOffset], test.beziersY[i + 1 + pointOffset]);
                Vector2 p2 = new Vector2(test.beziersX[i + 2 + pointOffset], test.beziersY[i + 2 + pointOffset]);

                Vector2 a = p0 - 2 * p1 + p2;
                Vector2 b = 2 * (p1 - p0);
                Vector2 c = p0;

                Vector2 prev = p0;

                Color bezCol = i / 2 == bezHighlightIndex ? Color.yellow : Color.yellow;
                float thicMul = i / 2 == bezHighlightIndex ? 5f : 1;


                for (int r = 1; r < bezRes; r++)
                {
                    float t = r / (bezRes - 1f);
                    Vector2 bezPoint = a * t * t + b * t + c;
                    Gizmos.DrawLine(prev, bezPoint);
                    prev = bezPoint;
                }
            }
            pointOffset += contLength + 1;
        }

        pointOffset = test.metaData[0];

        for (int contIndex = 0; contIndex < numContours; contIndex++)
        {
            int contLength = Mathf.Abs(test.metaData[2 + contIndex]);

            for (int i = 0; i < contLength; i += 2)
            {
                Vector2 p0 = new Vector2(test.beziersX[i + pointOffset], test.beziersY[i + pointOffset]);
                Vector2 p1 = new Vector2(test.beziersX[i + 1 + pointOffset], test.beziersY[i + 1 + pointOffset]);
                Vector2 p2 = new Vector2(test.beziersX[i + 2 + pointOffset], test.beziersY[i + 2 + pointOffset]);
                Gizmos.color = new Color(1, 0, 0, 0.8f);
                Gizmos.DrawSphere(p0, 0.0065f * bezierPointSizeMultiplier);
                Gizmos.color = new Color(67 / 255f, 165 / 255f, 0.8f);
                Gizmos.DrawSphere(p1, 0.0045f * bezierPointSizeMultiplier);
            }
            pointOffset += contLength + 1;
        }

        if (showAllWindows)
        {
            for (int i = 0; i < test.boxCentresX.Length; i++)
            {
                DrawWindow(i);
            }
        }
        else
        {
            DrawWindow(windowIndex);
        }

        void DrawWindow(int i)
        {
            Vector2 centre = new Vector2(test.boxCentresX[i], test.boxCentresY[i]);
            Vector2 size = new Vector2(test.boxSizes[i], test.boxSizes[i]);
            Gizmos.color = test.insideFlags[i] ? Color.green : Color.red;
            Gizmos.DrawWireCube(centre, size);
        }
    }

}
