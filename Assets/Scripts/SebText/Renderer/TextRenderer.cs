using UnityEngine;
using SebText.Rendering.Helpers;
using SebText.FontLoading;
using System.Collections.Generic;
using System;

namespace SebText.Rendering
{
    public class TextRenderer
    {
        const string ShaderFileName = "TextShader";
        readonly Mesh quadMesh;
        readonly Material fontMat;

        TextData textData;
        FontData fontData;
        ComputeBuffer argsBuffer;
        ComputeBuffer instanceDataBuffer;
        ComputeBuffer bezierDataBuffer;
        ComputeBuffer contourLengthsBuffer;

        InstanceData[] instanceData;
        LayoutSettings layoutSettings;
        Bounds bounds;
        string fontPath;
        string text;

        public TextRenderer(string fontPath, string displayString, LayoutSettings layoutSettings)
        {
            fontMat = new Material(Resources.Load<Shader>(ShaderFileName));
            quadMesh = QuadMeshGenerator.GenerateQuadMesh();
            bounds = new Bounds(Vector3.zero, Vector3.one * 10000);

            Update(displayString, fontPath, layoutSettings);
        }

        List<GlyphHelper.TextRenderData.GlyphData> prevGlyphRenderData;

        public void Update(string text, string fontPath, LayoutSettings layoutSettings)
        {
            bool fontChanged = false;
            bool textChanged = false;

            // -- Update font --
            if (this.fontPath != fontPath)
            {
                this.fontPath = fontPath;
                fontData = FontParser.Parse(fontPath);
                fontChanged = true;
            }
            // -- Update text --
            if (this.text != text || fontChanged)
            {
                textChanged = true;
                this.text = text;

                textData = new TextData(text, fontData);
                if (textData.PrintableCharacters.Length > 0)
                {
                    var textRenderData = GlyphHelper.CreateRenderData(textData.UniquePrintableCharacters, fontData);
                    prevGlyphRenderData = textRenderData.AllGlyphData;

                    // Create buffers
                    ComputeHelper.CreateArgsBuffer(ref argsBuffer, quadMesh, textData.PrintableCharacters.Length);
                    ComputeHelper.CreateStructuredBuffer<InstanceData>(ref instanceDataBuffer, textData.PrintableCharacters.Length);
                    ComputeHelper.CreateStructuredBuffer(ref bezierDataBuffer, textRenderData.BezierPoints);
                    ComputeHelper.CreateStructuredBuffer(ref contourLengthsBuffer, textRenderData.GlyphMetaData);

                    // Assign buffers
                    fontMat.SetBuffer("BezierData", bezierDataBuffer);
                    fontMat.SetBuffer("GlyphMetaData", contourLengthsBuffer);
                }
            }
            // -- Update layout --
            bool forceLayoutUpdate = fontChanged || textChanged;
            if (!this.layoutSettings.Equals(layoutSettings) || forceLayoutUpdate)
            {
                this.layoutSettings = layoutSettings;
                if (textData.PrintableCharacters.Length > 0)
                {
                    CreateInstanceData(ref instanceData, textData, layoutSettings);
                    instanceDataBuffer.SetData(instanceData);
                }
                fontMat.SetBuffer("PerInstanceData", instanceDataBuffer);

            }
        }


        public void Render(Vector2 position, Color col)
        {
            if (textData != null && textData.PrintableCharacters.Length > 0)
            {
                fontMat.SetVector("globalOffset", position);
                fontMat.SetColor("textCol", col);
                Graphics.DrawMeshInstancedIndirect(quadMesh, 0, fontMat, bounds, argsBuffer);
            }
        }

        public void Release()
        {
            ComputeHelper.Release(argsBuffer, instanceDataBuffer, bezierDataBuffer, contourLengthsBuffer);
        }

        void CreateInstanceData(ref InstanceData[] instanceData, TextData textData, LayoutSettings layoutSettings)
        {
            if (instanceData == null || instanceData.Length != textData.PrintableCharacters.Length)
            {
                instanceData = new InstanceData[textData.PrintableCharacters.Length];
            }

            for (int i = 0; i < textData.PrintableCharacters.Length; i++)
            {
                TextData.PrintableCharacter layout = textData.PrintableCharacters[i];
                float posX = layout.GetAdvanceX(layoutSettings.FontSize, layoutSettings.LetterSpacing, layoutSettings.WordSpacing);
                float posY = layout.GetAdvanceY(layoutSettings.FontSize, layoutSettings.LineSpacing);

                var info = prevGlyphRenderData[layout.GlyphIndex];
                instanceData[i] = new InstanceData(new Vector2(posX, posY), info.Size, layoutSettings.FontSize, info.ContourDataOffset);
            }
        }

        public readonly struct InstanceData
        {
            public readonly Vector2 pos;
            public readonly Vector2 size;
            public readonly float fontSize;
            public readonly int dataOffset;

            public InstanceData(Vector2 pos, Vector2 size, float fontSize, int dataOffset)
            {
                this.pos = pos;
                this.size = size;
                this.fontSize = fontSize;
                this.dataOffset = dataOffset;
            }
        }

        [System.Serializable]
        public struct LayoutSettings : IEquatable<LayoutSettings>
        {
            public float FontSize;
            public float LineSpacing;
            public float LetterSpacing;
            public float WordSpacing;

            public LayoutSettings(float fontSize, float lineSpacing, float letterSpacing, float wordSpacing)
            {
                FontSize = fontSize;
                LineSpacing = lineSpacing;
                LetterSpacing = letterSpacing;
                WordSpacing = wordSpacing;
            }

            public bool Equals(LayoutSettings other)
            {
                return FontSize == other.FontSize && LineSpacing == other.LineSpacing && LetterSpacing == other.LetterSpacing && WordSpacing == other.WordSpacing;
            }
        }

    }
}