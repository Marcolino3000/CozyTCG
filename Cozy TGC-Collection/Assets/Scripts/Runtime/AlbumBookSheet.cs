using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// Layout of the two sheets under Resources/Collectors Albums, and the source
    /// of every album's geometry.
    ///
    /// RADL_Book_*.png is one book in seven 190x160 frames laid out in a row: 0-3
    /// swing it open from the closed cover, and 4-6 lift a page over to the other
    /// side and land back on frame 3. The three colours are the three albums.
    ///
    /// "exp book.png" is a 4x3 grid of 32x32 isometric book icons - the shelf
    /// picks three of them, one per album.
    ///
    /// Neither sheet is sliced into sprites. Both are drawn by SpriteSheet.shader,
    /// which picks a cell by UV rect, so one material covers a whole animation.
    ///
    /// The page rect below is measured off the art rather than chosen, because the
    /// pockets are laid out on the parchment the book draws. A page is 69x109, and
    /// a card is 73x113 - within a few percent the same shape, which is why an
    /// album page holds exactly one card.
    /// </summary>
    public static class AlbumBookSheet
    {
        const string Folder = "Collectors Albums/";

        // -------------------------------------------------------------------
        // The animated book
        // -------------------------------------------------------------------
        public const int BookCellWidth = 190;
        public const int BookCellHeight = 160;
        public const int BookFrames = 7;

        /// <summary>Parchment of one open page, in cell pixels.</summary>
        public const int PageWidth = 69;
        public const int PageHeight = 109;

        /// <summary>Spine to the middle of a page.</summary>
        public const int PageOffset = 36;

        /// <summary>
        /// Where an album's own origin lands in the cell: the spine, at the height
        /// the two pages are centred on. Everything the builder places hangs off
        /// this, and it is not the middle of the cell - the book opens leftwards off
        /// a right edge that stays put, so the art sits off to one side of its frame.
        ///
        /// Both land on a half pixel because the pages are an odd number of pixels
        /// wide either side of a three pixel spine. Rounding them costs half a pixel
        /// of alignment between the pockets and the pages printed under them, which
        /// is several screen pixels once the album is blown up to fill the frame.
        /// </summary>
        public const float OriginX = 90.5f;
        public const float OriginY = 80.5f;

        /// <summary>
        /// The drawn book, as opposed to the cell it is laid out in: the union of
        /// every frame's opaque pixels, y measured down from the top of the cell.
        /// Whoever fits an album on screen fits this - the cell carries blank margins
        /// that would otherwise eat into the frame, and the covers reach well past
        /// the pages, so fitting the parchment alone hangs them off the edges.
        /// </summary>
        public const int ArtLeft = 12;
        public const int ArtTop = 4;
        public const int ArtWidth = 157;
        public const int ArtHeight = 142;

        static readonly string[] BookTextures = { "RADL_Book_red", "RADL_Book_blue", "RADL_Book_white" };

        /// <summary>Names of the three albums, in book order.</summary>
        public static readonly string[] Names = { "Crimson", "Cobalt", "Ivory" };

        public static int Albums => BookTextures.Length;

        // -------------------------------------------------------------------
        // The shelf icons
        // -------------------------------------------------------------------
        public const string IconTexture = "exp book";
        public const int IconSize = 32;
        public const int IconColumns = 4;
        public const int IconRows = 3;

        /// <summary>
        /// Cell of "exp book.png" that stands for each album, chosen to match the
        /// cover of its book: a crimson, a blue and a pale one.
        /// </summary>
        public static readonly int[] Icons = { 5, 3, 6 };

        // -------------------------------------------------------------------
        // Loading
        // -------------------------------------------------------------------
        public static Texture2D LoadBook(int album)
        {
            string name = BookTextures[Mathf.Clamp(album, 0, BookTextures.Length - 1)];
            var sheet = Resources.Load<Texture2D>(Folder + name);
            if (sheet == null) Debug.LogError($"[Cozy TGC] Book sheet not found at Resources/{Folder}{name}");
            return sheet;
        }

        public static Texture2D LoadIcons()
        {
            var sheet = Resources.Load<Texture2D>(Folder + IconTexture);
            if (sheet == null) Debug.LogError($"[Cozy TGC] Icon sheet not found at Resources/{Folder}{IconTexture}");
            return sheet;
        }

        // -------------------------------------------------------------------
        // Cells
        // -------------------------------------------------------------------
        /// <summary>UV rect of one animation frame: xy = offset, zw = size.</summary>
        public static Vector4 BookUvRect(Texture sheet, int frame)
        {
            frame = Mathf.Clamp(frame, 0, BookFrames - 1);
            return UvRect(sheet, new RectInt(frame * BookCellWidth, 0, BookCellWidth, BookCellHeight));
        }

        /// <summary>UV rect of one shelf icon, counted left to right and top to bottom.</summary>
        public static Vector4 IconUvRect(Texture sheet, int index)
        {
            index = Mathf.Clamp(index, 0, IconColumns * IconRows - 1);
            return UvRect(sheet, new RectInt(
                index % IconColumns * IconSize, index / IconColumns * IconSize, IconSize, IconSize));
        }

        /// <summary>
        /// Pixel rect to UV rect, flipped into Unity's bottom-up UV space on the
        /// way out - both sheets are laid out with y running down from the top.
        /// </summary>
        static Vector4 UvRect(Texture sheet, RectInt px)
        {
            if (sheet == null) return new Vector4(0f, 0f, 1f, 1f);

            float w = sheet.width;
            float h = sheet.height;
            return new Vector4(px.x / w, (h - px.y - px.height) / h, px.width / w, px.height / h);
        }
    }
}
