using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using SebText.Rendering.Helpers;
using System.Linq;
using System;

public class TestRunner : MonoBehaviour
{
    public bool runTests = true;
    public bool quick;
    public int quickStartIndex;
    public TextAsset dataFile;
    public ComputeShader compute;
    public MeshRenderer display;

    public bool crossReferenceWithPrevFailures;

    TestCaseID[] testCaseIDs;

    [Header("Vis Settings")]
    public int fontSize;
    public bool visWholeGlyph;
    public bool visTestCase;
    public TestCaseID testCaseToVis;
    public float bezierPointSizeMultiplier = 1;
    public float highlightThicknessMul = 1;
    public int bezHighlightIndex;

    public List<TestCaseID> failedTestIds = new();
    public List<TestCaseID> failedTestIdsPrev;
    ComputeBuffer errorCountBuffer;
    Vector2 referencePos;
    bool showRefRay;
    TestCreator.GlyphTest[] tests;

    int[] resolutions = { 61, 127, 509, 733, 1019, 1657 };
    RenderTexture debugTex;
    bool paramChangedSinceLastUpdate;
    string log;
    GUIStyle guiStyle = new GUIStyle();
    GUIStyle inputguiStyle;

    private void OnGUI()
    {
        inputguiStyle = new GUIStyle(GUI.skin.textField);
        guiStyle.fontSize = fontSize;
        inputguiStyle.fontSize = fontSize;
        guiStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(10, 10, 400, 2000), log, guiStyle);

        int fieldHeight = 33;
        int fieldWidth = 68;
        int fieldSpacing = 20;
        int ox = 300;
        string glyphS = GUI.TextField(new Rect(ox + (fieldWidth + fieldSpacing) * 0, 40, fieldWidth, fieldHeight), testCaseToVis.testGlyphIndex + "", inputguiStyle);
        string resS = GUI.TextField(new Rect(ox + (fieldWidth + fieldSpacing) * 1, 40, fieldWidth, fieldHeight), testCaseToVis.resolutionIndex + "", inputguiStyle);
        string winS = GUI.TextField(new Rect(ox + (fieldWidth + fieldSpacing) * 2, 40, fieldWidth, fieldHeight), testCaseToVis.testWindowIndex + "", inputguiStyle);

        string bexHighlightS = GUI.TextField(new Rect(Screen.width - fieldWidth * 2, 40, fieldWidth, fieldHeight), bezHighlightIndex + "", inputguiStyle);
        int.TryParse(bexHighlightS, out bezHighlightIndex);

        var test = testCaseToVis;
        int.TryParse(glyphS, out testCaseToVis.testGlyphIndex);
        int.TryParse(resS, out testCaseToVis.resolutionIndex);
        int.TryParse(winS, out testCaseToVis.testWindowIndex);
        if (!test.Equals(testCaseToVis))
        {
            showRefRay = false;
            paramChangedSinceLastUpdate = true;
        }

        float wx = tests[testCaseToVis.testGlyphIndex].boxCentresX[testCaseToVis.testWindowIndex];
        float wy = tests[testCaseToVis.testGlyphIndex].boxCentresY[testCaseToVis.testWindowIndex];
        float ws = tests[testCaseToVis.testGlyphIndex].boxSizes[testCaseToVis.testWindowIndex];
        Vector2 wPos = new Vector2(wx - ws / 2, wy + ws / 2);
        Vector2 wSPos = Camera.main.WorldToScreenPoint(wPos);
        int res = resolutions[testCaseToVis.resolutionIndex];
        GUI.Label(new Rect(wSPos.x, Screen.height - wSPos.y - 30, 200, 30), $"{res} x {res}", guiStyle);
        // Debug.Log(wSPos);
    }

    void Start()
    {
        log = "";
        display.sharedMaterial = new Material(Shader.Find("Unlit/Texture"));

        debugTex = ComputeHelper.CreateRenderTexture(resolutions[^1], resolutions[^1]);
        display.sharedMaterial.mainTexture = debugTex;
        compute.SetTexture(0, "DebugTex", debugTex);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        tests = JsonConvert.DeserializeObject<TestCreator.GlyphTest[]>(dataFile.text);

        if (quick) tests = tests.AsSpan(quickStartIndex, 20).ToArray();

        int testCaseCount = tests.Sum(t => t.boxSizes.Length) * resolutions.Length;
        int testCaseOffset = 0;
        testCaseIDs = new TestCaseID[testCaseCount];
        int caseIndex = 0;
        for (int testIndex = 0; testIndex < tests.Length; testIndex++)
        {
            for (int resIndex = 0; resIndex < resolutions.Length; resIndex++)
            {
                for (int windowIndex = 0; windowIndex < tests[testIndex].boxCentresX.Length; windowIndex++)
                {
                    testCaseIDs[caseIndex] = new TestCaseID(testIndex, resIndex, windowIndex);
                    caseIndex++;
                }

            }
        }

        errorCountBuffer = ComputeHelper.CreateStructuredBuffer(new uint[testCaseCount]);
        compute.SetBuffer(0, "ErrorCounter", errorCountBuffer);

        if (runTests)
        {

            for (int i = 0; i < tests.Length; i++)
            {
                Run(tests[i], testCaseOffset);
                testCaseOffset += tests[i].boxCentresX.Length * resolutions.Length;
            }

            uint[] errorCounts = new uint[testCaseCount];
            errorCountBuffer.GetData(errorCounts);



            uint totalErrorCount = 0;
            int numFailedCases = 0;

            for (int i = 0; i < errorCounts.Length; i++)
            {
                totalErrorCount += errorCounts[i];
                bool caseFailed = errorCounts[i] > 0;
                if (caseFailed)
                {
                    numFailedCases++;
                    TestCaseID failedID = testCaseIDs[i];
                    failedTestIds.Add(failedID);
                }
            }
            log = $"Failed: {numFailedCases} / {testCaseCount}\n";
            log += "Glyph | Res | Window\n";
            for (int i = 0; i < failedTestIds.Count; i++)
            {
                TestCaseID failedCase = failedTestIds[i];
                TestCaseID nextfailedCase = failedTestIds[(i - 1 + failedTestIds.Count) % failedTestIds.Count];
                //if (failedCase.testGlyphIndex != nextfailedCase.testGlyphIndex || failedCase.testWindowIndex != nextfailedCase.testWindowIndex)
                {
                    log += $"{failedCase.testGlyphIndex} | {failedCase.resolutionIndex} | {failedCase.testWindowIndex}\n";
                }
            }

            Debug.Log($"Test complete ({sw.ElapsedMilliseconds} ms). Num failed: {numFailedCases} / {testCaseCount}");

            Debug.Log("Note: make sure gizmos are enabled to view debug vis");
            if (quick) Debug.Log("Note: QUICK is enabled, only a subset of the full test has been run");

            if (crossReferenceWithPrevFailures && failedTestIdsPrev != null)
            {
                foreach (var failedCase in failedTestIds)
                {
                    if (!failedTestIdsPrev.Contains(failedCase))
                    {
                        Debug.Log("New failed case: " + failedCase);
                    }
                }
            }
        }

        ComputeHelper.Release(errorCountBuffer);
    }

    private void Update()
    {
        if (paramChangedSinceLastUpdate)
        {

            paramChangedSinceLastUpdate = false;

            int resolution = visWholeGlyph ? resolutions[^1] : resolutions[testCaseToVis.resolutionIndex];
            ComputeHelper.CreateRenderTexture(ref debugTex, resolution, resolution);
            display.sharedMaterial.mainTexture = debugTex;
            compute.SetTexture(0, "DebugTex", debugTex);
            compute.SetInt("debugBezIndex", bezHighlightIndex);

            var debugParams = new DebugParams(true, visWholeGlyph, testCaseToVis.resolutionIndex, testCaseToVis.testWindowIndex);
            var test = tests[testCaseToVis.testGlyphIndex];

            Run(test, 0, debugParams);

            display.transform.position = new Vector3(test.boxCentresX[testCaseToVis.testWindowIndex], test.boxCentresY[testCaseToVis.testWindowIndex]);
            display.transform.localScale = Vector3.one * test.boxSizes[testCaseToVis.testWindowIndex];
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            showRefRay = true;
            referencePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        }

    }

    private void OnDrawGizmos()
    {
  
        if (visTestCase && Application.isPlaying)
        {
            TestCreator.DrawTestDebugView(tests[testCaseToVis.testGlyphIndex], false, testCaseToVis.testWindowIndex, bezierPointSizeMultiplier, bezHighlightIndex);

            if (showRefRay)
            {
                Gizmos.color = new Color(1, 0, 1);
                Gizmos.DrawLine(referencePos, referencePos + Vector2.right * 100);
            }
        }
    }

    void Run(TestCreator.GlyphTest test, int testCaseOffset, DebugParams debugParams = default)
    {
        Vector2[] bezierData = test.beziersX.Zip(test.beziersY, (x, y) => new Vector2(x, y)).ToArray();
        ComputeBuffer bezierBuffer = ComputeHelper.CreateStructuredBuffer(bezierData);
        ComputeBuffer metaBuffer = ComputeHelper.CreateStructuredBuffer(test.metaData);

        float minX = test.beziersX.Min();
        float maxX = test.beziersX.Max();
        float minY = test.beziersY.Min();
        float maxY = test.beziersY.Max();

        compute.SetBuffer(0, "BezierData", bezierBuffer);
        compute.SetBuffer(0, "GlyphMetaData", metaBuffer);


        Vector2 glyphCentre = new Vector2((minX + maxX) / 2, (minY + maxY) / 2);
        float glyphSize = Mathf.Max(maxX - minX, maxY - minY);

        if (debugParams.isDebugMode)
        {
            if (debugParams.drawWholeGlyph)
            {
                RunTestCase(resolutions.Length - 1, 0, true);
            }
            else
            {
                RunTestCase(debugParams.singleWindowResolutionIndex, debugParams.singleWindowIndex, false);
            }
        }
        else
        {
            for (int resIndex = 0; resIndex < resolutions.Length; resIndex++)
            {
                for (int testBoxIndex = 0; testBoxIndex < test.boxCentresX.Length; testBoxIndex++)
                {
                    RunTestCase(resIndex, testBoxIndex, false);
                    testCaseOffset++;
                }
            }
        }

        void RunTestCase(int resolutionIndex, int windowIndex, bool drawWholeGlyph)
        {
            int resolution = resolutions[resolutionIndex];
            compute.SetInt("errorWeight", Mathf.CeilToInt((float)resolutions[^1]) / resolution);

            Vector2 testCentre = new Vector2(test.boxCentresX[windowIndex], test.boxCentresY[windowIndex]);
            float testSize = test.boxSizes[windowIndex];

            Vector2 centre = drawWholeGlyph ? glyphCentre : testCentre;
            float size = drawWholeGlyph ? glyphSize : testSize;

            compute.SetVector("centre", centre);
            compute.SetFloat("size", size);
            compute.SetBool("insideFlag", test.insideFlags[windowIndex]);
            compute.SetInt("testCaseIndex", testCaseOffset);

            compute.SetInt("resolution", resolution);
            ComputeHelper.Dispatch(compute, resolution, resolution);
        }

        ComputeHelper.Release(bezierBuffer, metaBuffer);

    }

    [System.Serializable]
    public struct TestCaseID
    {
        public int testGlyphIndex;
        public int resolutionIndex;
        public int testWindowIndex;

        public TestCaseID(int testGlyphIndex, int resolutionIndex, int testWindowIndex)
        {
            this.testGlyphIndex = testGlyphIndex;
            this.resolutionIndex = resolutionIndex;
            this.testWindowIndex = testWindowIndex;
        }

        public override string ToString()
        {
            return $"(testIndex: {testGlyphIndex}, resolutionIndex: {resolutionIndex}, windowIndex: {testWindowIndex})";
        }
    }

    struct DebugParams
    {
        public bool isDebugMode;
        public bool drawWholeGlyph;
        public int singleWindowResolutionIndex;
        public int singleWindowIndex;

        public DebugParams(bool isDebugMode, bool drawWholeGlyph, int singleWindowResolutionIndex, int singleWindowIndex)
        {
            this.isDebugMode = isDebugMode;
            this.drawWholeGlyph = drawWholeGlyph;
            this.singleWindowResolutionIndex = singleWindowResolutionIndex;
            this.singleWindowIndex = singleWindowIndex;
        }
    }

    private void OnValidate()
    {
        paramChangedSinceLastUpdate = true;
    }
}
