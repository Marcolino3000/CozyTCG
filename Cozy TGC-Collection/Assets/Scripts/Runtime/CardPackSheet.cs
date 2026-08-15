using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// Grid layout of the pack sheet under Assets/Resources: 9 x 20 wrappers,
    /// 84x154 px each, separated by transparent gutters.
    ///
    /// The sheet is imported as one texture rather than sliced into sprites, so
    /// the pack shader picks a wrapper by UV rect instead of by material - one
    /// material draws all 180 of them.
    /// </summary>
    public static class CardPackSheet
    {
        public const string ResourcePath = CardArtLibrary.Root + "CardPacks/CardPacks";

        public const int PackWidth = 84;
        public const int PackHeight = 154;

        /// <summary>Crimped seal along the top edge. This is the bit that gets torn.</summary>
        public const int TopSealPixels = 11;

        public const int Rows = 20;
        const int RowOrigin = 3;
        const int RowPitch = 157;

        // Columns sit on a 95 px pitch except the eighth, which is one pixel to
        // the left, so the offsets are listed rather than computed.
        static readonly int[] ColumnOrigins = { 3, 98, 193, 288, 383, 478, 573, 667, 762 };

        public static int Columns => ColumnOrigins.Length;
        public static int Count => Columns * Rows;

        public static Texture2D Load()
        {
            var sheet = Resources.Load<Texture2D>(ResourcePath);
            if (sheet == null) Debug.LogError($"[Cozy TGC] Pack sheet not found at Resources/{ResourcePath}");
            return sheet;
        }

        /// <summary>Pixel rect of one wrapper, with y measured from the top of the sheet.</summary>
        public static RectInt PixelRect(int index)
        {
            index = Mathf.Clamp(index, 0, Count - 1);
            int column = index % Columns;
            int row = index / Columns;
            return new RectInt(ColumnOrigins[column], RowOrigin + row * RowPitch, PackWidth, PackHeight);
        }

        /// <summary>
        /// UV rect of one wrapper as the shader wants it: xy = offset, zw = size.
        /// Flipped into Unity's bottom-up UV space on the way out.
        /// </summary>
        public static Vector4 UvRect(Texture sheet, int index)
        {
            if (sheet == null) return new Vector4(0f, 0f, 1f, 1f);

            RectInt px = PixelRect(index);
            float w = sheet.width;
            float h = sheet.height;
            return new Vector4(px.x / w, (h - px.y - px.height) / h, px.width / w, px.height / h);
        }

        public static int RandomIndex()
        {
            return Random.Range(0, Count);
        }
    }
}
