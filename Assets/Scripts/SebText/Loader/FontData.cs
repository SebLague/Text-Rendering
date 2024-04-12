using System.Collections.Generic;

namespace SebText.FontLoading
{
    // Contains the raw data loaded from a TrueType font file.
    public class FontData
    {
        public GlyphData[] Glyphs { get; private set; }
        public GlyphData MissingGlyph;
        public int UnitsPerEm;

        Dictionary<uint, GlyphData> glyphLookup;

        public FontData(GlyphData[] glyphs, int unitsPerEm)
        {
            Glyphs = glyphs;
            UnitsPerEm = unitsPerEm;
            glyphLookup = new();

            foreach (GlyphData c in glyphs)
            {
                if (c == null) continue;
                glyphLookup.Add(c.UnicodeValue, c);
                if (c.GlyphIndex == 0) MissingGlyph = c;
            }

            if (MissingGlyph == null) throw new System.Exception("No missing character glyph provided!");
        }

        public bool TryGetGlyph(uint unicode, out GlyphData character)
        {
            bool found = glyphLookup.TryGetValue(unicode, out character);
            if (!found)
            {
                character = MissingGlyph;
            }
            return found;
        }


        public class GlyphData
        {
            public uint UnicodeValue;
            public uint GlyphIndex;
            public Point[] Points;
            public int[] ContourEndIndices;
            public int AdvanceWidth;
            public int LeftSideBearing;

            public int MinX;
            public int MaxX;
            public int MinY;
            public int MaxY;

            public int Width => MaxX - MinX;
            public int Height => MaxY - MinY;

        }

        public struct Point
        {
            public int X;
            public int Y;
            public bool OnCurve;

            public Point(int x, int y) : this()
            {
                X = x;
                Y = y;
            }

            public Point(int x, int y, bool onCurve)
            {
                X = x;
                Y = y;
                OnCurve = onCurve;
            }
        }
    }

}