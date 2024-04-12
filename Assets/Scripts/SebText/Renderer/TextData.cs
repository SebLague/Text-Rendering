using System.Collections.Generic;
using SebText.FontLoading;

namespace SebText.Rendering
{

    public class TextData
    {
        const float SpaceSizeEM = 0.333f;
        const float LineHeightEM = 1.3f;

        public readonly FontData.GlyphData[] UniquePrintableCharacters;
        public readonly PrintableCharacter[] PrintableCharacters;

        public TextData(string text, FontData fontData)
        {
            List<FontData.GlyphData> uniqueCharsList = new();
            List<PrintableCharacter> characterLayoutList = new();
            Dictionary<FontData.GlyphData, int> charToIndexTable = new();

            float scale = 1f / fontData.UnitsPerEm;
            float letterAdvance = 0;
            float wordAdvance = 0;
            float lineAdvance = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ')
                {
                    wordAdvance += SpaceSizeEM;
                }
                else if (text[i] == '\t')
                {
                    wordAdvance += SpaceSizeEM * 4; // TODO: proper tab implementation
                }
                else if (text[i] == '\n')
                {
                    lineAdvance += LineHeightEM;
                    wordAdvance = 0;
                    letterAdvance = 0;
                }
                else if (!char.IsControl(text[i]))
                {
                    fontData.TryGetGlyph(text[i], out FontData.GlyphData character);

                    if (!charToIndexTable.TryGetValue(character, out int uniqueIndex))
                    {
                        uniqueIndex = uniqueCharsList.Count;
                        charToIndexTable.Add(character, uniqueIndex);
                        uniqueCharsList.Add(character);
                    }

                    float offsetX = (character.MinX + character.Width / 2) * scale;
                    float offsetY = (character.MinY + character.Height / 2) * scale;

                    PrintableCharacter printable = new(uniqueIndex, letterAdvance, wordAdvance, lineAdvance, offsetX, offsetY);
                    characterLayoutList.Add(printable);
                    letterAdvance += character.AdvanceWidth * scale;
                }
            }

            PrintableCharacters = characterLayoutList.ToArray();
            UniquePrintableCharacters = uniqueCharsList.ToArray();
        }

        public readonly struct PrintableCharacter
        {
            public readonly int GlyphIndex;
            readonly float letterAdvance;
            readonly float wordAdvance;
            readonly float lineAdvance;
            readonly float offsetX;
            readonly float offsetY;

            public PrintableCharacter(int glyphIndex, float letterAdvance, float wordAdvance, float lineAdvance, float offsetX, float offsetY)
            {
                GlyphIndex = glyphIndex;
                this.letterAdvance = letterAdvance;
                this.wordAdvance = wordAdvance;
                this.lineAdvance = lineAdvance;
                this.offsetX = offsetX;
                this.offsetY = offsetY;
            }

            public float GetAdvanceX(float fontSize, float letterSpacing, float wordSpacing)
            {
                return (letterAdvance * letterSpacing + wordAdvance * wordSpacing + offsetX) * fontSize;
            }

            public float GetAdvanceY(float fontSize, float lineSpacing)
            {
                return (-lineAdvance * lineSpacing + offsetY) * fontSize;
            }
        }
    }
}