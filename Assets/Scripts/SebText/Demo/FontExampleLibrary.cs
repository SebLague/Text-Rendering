using UnityEngine;

namespace SebText.Demo
{
    public static class FontExampleLibrary
    {
        public enum TypeFace
        {
            JetBrainsMono,
            MapleMono,
            FiraCode,
            OpenSans,
            LibreBaskerville,
            RobotoSlab,
            Shrikhand,
            Pacifico,
            Cakecafe,
            Pixelify,
            PermanentMarker,
            Nicolast
        }

        public enum Variant
        {
            Regular,
            Bold
        }

        static readonly Entry[] entries =
        {
            new Entry(TypeFace.JetBrainsMono, "JetBrains-Mono", "JetBrainsMonoNL-Regular", "JetBrainsMono-Bold"),
            new Entry(TypeFace.OpenSans, "OpenSans", "OpenSans-Regular", "OpenSans-Bold"),
            new Entry(TypeFace.FiraCode, "FiraCode", "FiraCode-Regular","FiraCode-Bold"),
            new Entry(TypeFace.MapleMono, "MapleMono", "MapleMono-Regular", "MapleMono-Bold"),
            new Entry(TypeFace.LibreBaskerville, "LibreBaskerville", "LibreBaskerville-Regular", "LibreBaskerville-Bold"),
            new Entry(TypeFace.RobotoSlab, "RobotoSlab", "RobotoSlab-Regular", "RobotoSlab-Bold"),
            new Entry(TypeFace.Shrikhand, "Shrikhand", "Shrikhand-Regular", "Shrikhand-Regular"),
            new Entry(TypeFace.Pacifico, "Pacifico", "Pacifico-Regular", "Pacifico-Regular"),
            new Entry(TypeFace.Cakecafe, "Cakecafe", "Cakecafe", "Cakecafe"),
            new Entry(TypeFace.Pixelify, "Pixelify", "PixelifySans-Regular", "PixelifySans-Bold"),
            new Entry(TypeFace.PermanentMarker, "PermanentMarker", "PermanentMarker-Regular", "PermanentMarker-Regular"),
            new Entry(TypeFace.Nicolast, "Nicolast", "Nicolast", "Nicolast"),
        };

        public static string GetFontPath(TypeFace typeface, Variant variant)
        {
            foreach (Entry entry in entries)
            {
                if (entry.TypeFace == typeface)
                {
                    return variant == Variant.Regular ? entry.PathRegular : entry.PathBold;
                }
            }
            return string.Empty;
        }


        public struct Entry
        {
            public TypeFace TypeFace;
            public string PathRegular;
            public string PathBold;

            public Entry(TypeFace typeFace, string directory, string nameRegular, string nameBold)
            {
                TypeFace = typeFace;
                PathRegular = MakePath(nameRegular);
                PathBold = MakePath(nameBold);

                string MakePath(string name) => System.IO.Path.Combine(Application.dataPath, "Fonts", directory, name + ".ttf");
            }
        }
    }
}