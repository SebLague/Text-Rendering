using System;
using System.Collections.Generic;

namespace SebText.FontLoading
{
    // An (incomplete) Parser for TrueType font files
    public static class FontParser
    {

        public static FontData Parse(string pathToFont)
        {
            using FontReader reader = new(pathToFont);

            // --- Get table locations ---
            Dictionary<string, uint> tableLocationLookup = ReadTableLocations(reader);
            uint glyphTableLocation = tableLocationLookup["glyf"];
            uint locaTableLocation = tableLocationLookup["loca"];
            uint cmapLocation = tableLocationLookup["cmap"];

            // ---- Read Head Table ----
            reader.GoTo(tableLocationLookup["head"]);
            reader.SkipBytes(18);
            // Design units to Em size (range from 64 to 16384)
            int unitsPerEm = reader.ReadUInt16();
            reader.SkipBytes(30);
            // Number of bytes used by the offsets in the 'loca' table (for looking up glyph locations)
            int numBytesPerLocationLookup = (reader.ReadInt16() == 0 ? 2 : 4);

            // --- Read 'maxp' table ---
            reader.GoTo(tableLocationLookup["maxp"]);
            reader.SkipBytes(4);
            int numGlyphs = reader.ReadUInt16();
            uint[] glyphLocations = GetAllGlyphLocations(reader, numGlyphs, numBytesPerLocationLookup, locaTableLocation, glyphTableLocation);

            GlyphMap[] mappings = GetUnicodeToGlyphIndexMappings(reader, cmapLocation);
            FontData.GlyphData[] glyphs = ReadAllGlyphs(reader, glyphLocations, mappings);

            ApplyLayoutInfo();


            FontData fontData = new(glyphs, unitsPerEm);
            return fontData;

            // Get horizontal layout information from the "hhea" and "hmtx" tables
            void ApplyLayoutInfo()
            {
                (int advance, int left)[] layoutData = new (int, int)[numGlyphs];

                // Get number of metrics from the 'hhea' table
                reader.GoTo(tableLocationLookup["hhea"]);

                reader.SkipBytes(8); // unused: version, ascent, descent
                int lineGap = reader.ReadInt16();
                int advanceWidthMax = reader.ReadInt16();
                reader.SkipBytes(22); // unused: minLeftSideBearing, minRightSideBearing, xMaxExtent, caretSlope/Offset, reserved, metricDataFormat
                int numAdvanceWidthMetrics = reader.ReadInt16();

                // Get the advance width and leftsideBearing metrics from the 'hmtx' table
                reader.GoTo(tableLocationLookup["hmtx"]);
                int lastAdvanceWidth = 0;

                for (int i = 0; i < numAdvanceWidthMetrics; i++)
                {
                    int advanceWidth = reader.ReadUInt16();
                    int leftSideBearing = reader.ReadInt16();
                    lastAdvanceWidth = advanceWidth;

                    layoutData[i] = (advanceWidth, leftSideBearing);
                }

                // Some fonts have a run of monospace characters at the end
                int numRem = numGlyphs - numAdvanceWidthMetrics;

                for (int i = 0; i < numRem; i++)
                {
                    int leftSideBearing = reader.ReadInt16();
                    int glyphIndex = numAdvanceWidthMetrics + i;

                    layoutData[glyphIndex] = (lastAdvanceWidth, leftSideBearing);
                }

                // Apply
                foreach (var c in glyphs)
                {
                    c.AdvanceWidth = layoutData[c.GlyphIndex].advance;
                    c.LeftSideBearing = layoutData[c.GlyphIndex].left;
                }
            }
        }


        // -- Read Font Directory to create a lookup of table locations by their 4-character nametag --
        static Dictionary<string, uint> ReadTableLocations(FontReader reader)
        {
            Dictionary<string, uint> tableLocations = new();

            // -- offset subtable --
            reader.SkipBytes(4); // unused: scalerType
            int numTables = reader.ReadUInt16();
            reader.SkipBytes(6); // unused: searchRange, entrySelector, rangeShift

            // -- table directory --
            for (int i = 0; i < numTables; i++)
            {
                string tag = reader.ReadString(4);
                uint checksum = reader.ReadUInt32();
                uint offset = reader.ReadUInt32();
                uint length = reader.ReadUInt32();

                tableLocations.Add(tag, offset);

            }
            return tableLocations;
        }

        static FontData.GlyphData[] ReadAllGlyphs(FontReader reader, uint[] glyphLocations, GlyphMap[] mappings)
        {
            FontData.GlyphData[] glyphs = new FontData.GlyphData[mappings.Length];

            for (int i = 0; i < mappings.Length; i++)
            {
                GlyphMap mapping = mappings[i];

                FontData.GlyphData glyphData = ReadGlyph(reader, glyphLocations, mapping.GlyphIndex);
                glyphData.UnicodeValue = mapping.Unicode;
                glyphs[i] = glyphData;
            }

            return glyphs;
        }

        static FontData.GlyphData ReadGlyph(FontReader reader, uint[] glyphLocations, uint glyphIndex)
        {
            uint glyphLocation = glyphLocations[glyphIndex];

            reader.GoTo(glyphLocation);
            int contourCount = reader.ReadInt16();

            // Glyph is either simple or compound
            // * Simple: outline data is stored here directly
            // * Compound: two or more simple glyphs need to be looked up, transformed, and combined
            bool isSimpleGlyph = contourCount >= 0;

            if (isSimpleGlyph) return ReadSimpleGlyph(reader, glyphLocations, glyphIndex);
            else return ReadCompoundGlyph(reader, glyphLocations, glyphIndex);
        }

        // Read a simple glyph from the 'glyf' table
        static FontData.GlyphData ReadSimpleGlyph(FontReader reader, uint[] glyphLocations, uint glyphIndex)
        {
            // Flag masks
            const int OnCurve = 0;
            const int IsSingleByteX = 1;
            const int IsSingleByteY = 2;
            const int Repeat = 3;
            const int InstructionX = 4;
            const int InstructionY = 5;

            reader.GoTo(glyphLocations[glyphIndex]);

            FontData.GlyphData glyphData = new();
            glyphData.GlyphIndex = glyphIndex;

            int contourCount = reader.ReadInt16();
            if (contourCount < 0) throw new Exception("Expected simple glyph, but found compound glyph instead");

            glyphData.MinX = reader.ReadInt16();
            glyphData.MinY = reader.ReadInt16();
            glyphData.MaxX = reader.ReadInt16();
            glyphData.MaxY = reader.ReadInt16();

            // Read contour ends
            int numPoints = 0;
            int[] contourEndIndices = new int[contourCount];

            for (int i = 0; i < contourCount; i++)
            {
                int contourEndIndex = reader.ReadUInt16();
                numPoints = Math.Max(numPoints, contourEndIndex + 1);
                contourEndIndices[i] = contourEndIndex;
            }

            int instructionsLength = reader.ReadInt16();
            reader.SkipBytes(instructionsLength); // skip instructions (hinting stuff)

            byte[] allFlags = new byte[numPoints];
            FontData.Point[] points = new FontData.Point[numPoints];

            for (int i = 0; i < numPoints; i++)
            {
                byte flag = reader.ReadByte();
                allFlags[i] = flag;

                if (FlagBitIsSet(flag, Repeat))
                {
                    int repeatCount = reader.ReadByte();

                    for (int r = 0; r < repeatCount; r++)
                    {
                        i++;
                        allFlags[i] = flag;
                    }
                }
            }

            ReadCoords(true);
            ReadCoords(false);
            glyphData.Points = points;
            glyphData.ContourEndIndices = contourEndIndices;
            return glyphData;

            void ReadCoords(bool readingX)
            {
                int min = int.MaxValue;
                int max = int.MinValue;

                int singleByteFlagBit = readingX ? IsSingleByteX : IsSingleByteY;
                int instructionFlagMask = readingX ? InstructionX : InstructionY;

                int coordVal = 0;

                for (int i = 0; i < numPoints; i++)
                {
                    byte currFlag = allFlags[i];

                    // Offset value is represented with 1 byte (unsigned)
                    // Here the instruction flag tells us whether to add or subtract the offset
                    if (FlagBitIsSet(currFlag, singleByteFlagBit))
                    {
                        int coordOffset = reader.ReadByte();
                        bool positiveOffset = FlagBitIsSet(currFlag, instructionFlagMask);
                        coordVal += positiveOffset ? coordOffset : -coordOffset;
                    }
                    // Offset value is represented with 2 bytes (signed)
                    // Here the instruction flag tells us whether an offset value exists or not
                    else if (!FlagBitIsSet(currFlag, instructionFlagMask))
                    {
                        coordVal += reader.ReadInt16();
                    }

                    if (readingX) points[i].X = coordVal;
                    else points[i].Y = coordVal;
                    points[i].OnCurve = FlagBitIsSet(currFlag, OnCurve);

                    min = Math.Min(min, coordVal);
                    max = Math.Max(max, coordVal);
                }
            }
        }

        static FontData.GlyphData ReadCompoundGlyph(FontReader reader, uint[] glyphLocations, uint glyphIndex)
        {
            FontData.GlyphData compoundGlyph = new();
            compoundGlyph.GlyphIndex = glyphIndex;

            uint glyphLocation = glyphLocations[glyphIndex];
            reader.GoTo(glyphLocation);
            reader.SkipBytes(2);

            compoundGlyph.MinX = reader.ReadInt16();
            compoundGlyph.MinY = reader.ReadInt16();
            compoundGlyph.MaxX = reader.ReadInt16();
            compoundGlyph.MaxY = reader.ReadInt16();

            List<FontData.Point> allPoints = new();
            List<int> allContourEndIndices = new();

            while (true)
            {
                (FontData.GlyphData componentGlyph, bool hasMoreGlyphs) = ReadNextComponentGlyph(reader, glyphLocations, glyphLocation);

                // Add all contour end indices from the simple glyph component to the compound glyph's data
                // Note: indices must be offset to account for previously-added component glyphs
                foreach (int endIndex in componentGlyph.ContourEndIndices)
                {
                    allContourEndIndices.Add(endIndex + allPoints.Count);
                }
                allPoints.AddRange(componentGlyph.Points);

                if (!hasMoreGlyphs) break;
            }

            compoundGlyph.Points = allPoints.ToArray();
            compoundGlyph.ContourEndIndices = allContourEndIndices.ToArray();
            return compoundGlyph;
        }

        static (FontData.GlyphData glyph, bool hasMoreGlyphs) ReadNextComponentGlyph(FontReader reader, uint[] glyphLocations, uint glyphLocation)
        {
            uint flag = reader.ReadUInt16();
            uint glyphIndex = reader.ReadUInt16();

            uint componentGlyphLocation = glyphLocations[glyphIndex];
            // If compound glyph refers to itself, return empty glyph to avoid infinite loop.
            // Had an issue with this on the 'carriage return' character in robotoslab.
            // There's likely a bug in my parsing somewhere, but this is my work-around for now...
            if (componentGlyphLocation == glyphLocation)
            {
                return (new FontData.GlyphData() { Points = Array.Empty<FontData.Point>(), ContourEndIndices = Array.Empty<int>() }, false);
            }

            // Decode flags
            bool argsAre2Bytes = FlagBitIsSet(flag, 0);
            bool argsAreXYValues = FlagBitIsSet(flag, 1);
            bool roundXYToGrid = FlagBitIsSet(flag, 2);
            bool isSingleScaleValue = FlagBitIsSet(flag, 3);
            bool isMoreComponentsAfterThis = FlagBitIsSet(flag, 5);
            bool isXAndYScale = FlagBitIsSet(flag, 6);
            bool is2x2Matrix = FlagBitIsSet(flag, 7);
            bool hasInstructions = FlagBitIsSet(flag, 8);
            bool useThisComponentMetrics = FlagBitIsSet(flag, 9);
            bool componentsOverlap = FlagBitIsSet(flag, 10);

            // Read args (these are either x/y offsets, or point number)
            int arg1 = argsAre2Bytes ? reader.ReadInt16() : reader.ReadSByte();
            int arg2 = argsAre2Bytes ? reader.ReadInt16() : reader.ReadSByte();

            if (!argsAreXYValues) throw new Exception("TODO: Args1&2 are point indices to be matched, rather than offsets");

            double offsetX = arg1;
            double offsetY = arg2;

            double iHat_x = 1;
            double iHat_y = 0;
            double jHat_x = 0;
            double jHat_y = 1;

            if (isSingleScaleValue)
            {
                iHat_x = reader.ReadFixedPoint2Dot14();
                jHat_y = iHat_x;
            }
            else if (isXAndYScale)
            {
                iHat_x = reader.ReadFixedPoint2Dot14();
                jHat_y = reader.ReadFixedPoint2Dot14();
            }
            // Todo: incomplete implemntation
            else if (is2x2Matrix)
            {
                iHat_x = reader.ReadFixedPoint2Dot14();
                iHat_y = reader.ReadFixedPoint2Dot14();
                jHat_x = reader.ReadFixedPoint2Dot14();
                jHat_y = reader.ReadFixedPoint2Dot14();
            }

            uint currentCompoundGlyphReadLocation = reader.GetLocation();
            FontData.GlyphData simpleGlyph = ReadGlyph(reader, glyphLocations, glyphIndex);
            reader.GoTo(currentCompoundGlyphReadLocation);

            for (int i = 0; i < simpleGlyph.Points.Length; i++)
            {
                (double xPrime, double yPrime) = TransformPoint(simpleGlyph.Points[i].X, simpleGlyph.Points[i].Y);
                simpleGlyph.Points[i].X = (int)xPrime;
                simpleGlyph.Points[i].Y = (int)yPrime;
            }

            return (simpleGlyph, isMoreComponentsAfterThis);

            (double xPrime, double yPrime) TransformPoint(double x, double y)
            {
                double xPrime = iHat_x * x + jHat_x * y + offsetX;
                double yPrime = iHat_y * x + jHat_y * y + offsetY;
                return (xPrime, yPrime);
            }
        }


        static uint[] GetAllGlyphLocations(FontReader reader, int numGlyphs, int bytesPerEntry, uint locaTableLocation, uint glyfTableLocation)
        {
            uint[] allGlyphLocations = new uint[numGlyphs];
            bool isTwoByteEntry = bytesPerEntry == 2;

            for (int glyphIndex = 0; glyphIndex < numGlyphs; glyphIndex++)
            {
                reader.GoTo(locaTableLocation + glyphIndex * bytesPerEntry);
                // If 2-byte format is used, the stored location is half of actual location (so multiply by 2)
                uint glyphDataOffset = isTwoByteEntry ? reader.ReadUInt16() * 2u : reader.ReadUInt32();
                allGlyphLocations[glyphIndex] = glyfTableLocation + glyphDataOffset;
            }

            return allGlyphLocations;
        }

        // Create a lookup from unicode to font's internal glyph index
        static GlyphMap[] GetUnicodeToGlyphIndexMappings(FontReader reader, uint cmapOffset)
        {
            List<GlyphMap> glyphPairs = new();
            reader.GoTo(cmapOffset);

            uint version = reader.ReadUInt16();
            uint numSubtables = reader.ReadUInt16(); // font can contain multiple character maps for different platforms

            // --- Read through metadata for each character map to find the one we want to use ---
            uint cmapSubtableOffset = 0;
            int selectedUnicodeVersionID = -1;

            for (int i = 0; i < numSubtables; i++)
            {
                int platformID = reader.ReadUInt16();
                int platformSpecificID = reader.ReadUInt16();
                uint offset = reader.ReadUInt32();

                // Unicode encoding
                if (platformID == 0)
                {
                    // Use highest supported unicode version
                    if (platformSpecificID is 0 or 1 or 3 or 4 && platformSpecificID > selectedUnicodeVersionID)
                    {
                        cmapSubtableOffset = offset;
                        selectedUnicodeVersionID = platformSpecificID;
                    }
                }
                // Microsoft Encoding
                else if (platformID == 3 && selectedUnicodeVersionID == -1)
                {
                    if (platformSpecificID is 1 or 10)
                    {
                        cmapSubtableOffset = offset;
                    }
                }
            }

            if (cmapSubtableOffset == 0)
            {
                throw new Exception("Font does not contain supported character map type (TODO)");
            }

            // Go to the character map
            reader.GoTo(cmapOffset + cmapSubtableOffset);
            int format = reader.ReadUInt16();
            bool hasReadMissingCharGlyph = false;

            if (format != 12 && format != 4)
            {
                throw new Exception("Font cmap format not supported (TODO): " + format);
            }

            // ---- Parse Format 4 ----
            if (format == 4)
            {
                int length = reader.ReadUInt16();
                int languageCode = reader.ReadUInt16();
                // Number of contiguous segments of character codes
                int segCount2X = reader.ReadUInt16();
                int segCount = segCount2X / 2;
                reader.SkipBytes(6); // Skip: searchRange, entrySelector, rangeShift

                // Ending character code for each segment (last = 2^16 - 1)
                int[] endCodes = new int[segCount];
                for (int i = 0; i < segCount; i++)
                {
                    endCodes[i] = reader.ReadUInt16();
                }

                reader.Skip16BitEntries(1); // Reserved pad

                int[] startCodes = new int[segCount];
                for (int i = 0; i < segCount; i++)
                {
                    startCodes[i] = reader.ReadUInt16();
                }

                int[] idDeltas = new int[segCount];
                for (int i = 0; i < segCount; i++)
                {
                    idDeltas[i] = reader.ReadUInt16();
                }

                (int offset, int readLoc)[] idRangeOffsets = new (int, int)[segCount];
                for (int i = 0; i < segCount; i++)
                {
                    int readLoc = (int)reader.GetLocation();
                    int offset = reader.ReadUInt16();
                    idRangeOffsets[i] = (offset, readLoc);
                }

                for (int i = 0; i < startCodes.Length; i++)
                {
                    int endCode = endCodes[i];
                    int currCode = startCodes[i];

                    if (currCode == 65535) break; // not sure about this (hack to avoid out of bounds on a specific font)

                    while (currCode <= endCode)
                    {
                        int glyphIndex;
                        // If idRangeOffset is 0, the glyph index can be calculated directly
                        if (idRangeOffsets[i].offset == 0)
                        {
                            glyphIndex = (currCode + idDeltas[i]) % 65536;
                        }
                        // Otherwise, glyph index needs to be looked up from an array
                        else
                        {
                            uint readerLocationOld = reader.GetLocation();
                            int rangeOffsetLocation = idRangeOffsets[i].readLoc + idRangeOffsets[i].offset;
                            int glyphIndexArrayLocation = 2 * (currCode - startCodes[i]) + rangeOffsetLocation;

                            reader.GoTo(glyphIndexArrayLocation);
                            glyphIndex = reader.ReadUInt16();

                            if (glyphIndex != 0)
                            {
                                glyphIndex = (glyphIndex + idDeltas[i]) % 65536;
                            }

                            reader.GoTo(readerLocationOld);
                        }

                        glyphPairs.Add(new((uint)glyphIndex, (uint)currCode));
                        hasReadMissingCharGlyph |= glyphIndex == 0;
                        currCode++;
                    }
                }
            }
            // ---- Parse Format 12 ----
            else if (format == 12)
            {
                reader.SkipBytes(10); // Skip: reserved, subtableByteLengthInlcudingHeader, languageCode
                uint numGroups = reader.ReadUInt32();

                for (int i = 0; i < numGroups; i++)
                {
                    uint startCharCode = reader.ReadUInt32();
                    uint endCharCode = reader.ReadUInt32();
                    uint startGlyphIndex = reader.ReadUInt32();

                    uint numChars = endCharCode - startCharCode + 1;
                    for (int charCodeOffset = 0; charCodeOffset < numChars; charCodeOffset++)
                    {
                        uint charCode = (uint)(startCharCode + charCodeOffset);
                        uint glyphIndex = (uint)(startGlyphIndex + charCodeOffset);

                        glyphPairs.Add(new(glyphIndex, charCode));
                        hasReadMissingCharGlyph |= glyphIndex == 0;
                    }
                }
            }

            if (!hasReadMissingCharGlyph)
            {
                glyphPairs.Add(new(0, 65535));
            }

            return glyphPairs.ToArray();
        }

        static bool FlagBitIsSet(byte flag, int bitIndex) => ((flag >> bitIndex) & 1) == 1;
        static bool FlagBitIsSet(uint flag, int bitIndex) => ((flag >> bitIndex) & 1) == 1;

        public readonly struct GlyphMap
        {
            public readonly uint GlyphIndex;
            public readonly uint Unicode;

            public GlyphMap(uint index, uint unicode)
            {
                GlyphIndex = index;
                Unicode = unicode;
            }
        }

        public struct HeadTableData
        {
            public uint UnitsPerEM;
            public uint NumBytesPerGlyphIndexToLocationEntry;

            public HeadTableData(uint unitsPerEM, uint numBytesPerGlyphIndexToLocationEntry)
            {
                UnitsPerEM = unitsPerEM;
                NumBytesPerGlyphIndexToLocationEntry = numBytesPerGlyphIndexToLocationEntry;
            }
        }

    }

}