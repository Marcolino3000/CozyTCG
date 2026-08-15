using System.Collections.Generic;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// Layout of the UI kit under Assets/Resources/UI, and the knife that cuts pieces
    /// out of it. Same job <see cref="CardPackSheet"/> does for the wrappers, with one
    /// difference that decides the whole design:
    ///
    /// IMGUI nine-slices a *whole* texture. A GUIStyle background cannot be a rect
    /// inside an atlas the way a shader's UV rect can, so a piece has to be cut into a
    /// texture of its own. They are cut once and kept - which is also why these sheets
    /// have to be readable, and why they are the one corner of Resources/ that does not
    /// want the card import settings. <c>UiKitImporter</c> sorts both out.
    ///
    /// Pieces are cut at an integer zoom rather than left at 1:1 and stretched: the
    /// borders are two and three pixels wide, and a frame drawn at twice the size with
    /// a three pixel edge stops looking like the pixel art it came from. The zoom is
    /// the screen scale rounded off, so the edges grow with everything else.
    /// </summary>
    public static class UiSheet
    {
        const string Root = "UI/";
        public const string ButtonsPath = Root + "Sprite sheets/buttons/Square Buttons 26x26";
        public const string IconsPath = Root + "Sprite sheets/Icons/white icons";
        public const string FramesPath = Root + "emojis-free/emoji style ui/Inventory_Spritesheet";

        /// <summary>The four colours the button sheet comes in, top row down.</summary>
        public enum Tone { Bone, Cream, Tan, Brown }

        /// <summary>
        /// The kit's icons, in sheet order: six to a row, three rows. Drawn white, so
        /// the colour is whatever GUI.color is at the time.
        /// </summary>
        public enum Icon
        {
            Gamepad, Speech, Ghost, Gear, Question, Star,
            Alert, Coin, Cart, Ranking, Trophy, Crown,
            Plus, Minus, House, Check, Cross, Blocked,
        }

        // Buttons sit on a 48 px grid, 11 px in from the corner of their cell. The
        // raised one is two pixels taller than the pressed one - that is the shadow
        // lip along its bottom edge, and it is why the two have different borders.
        const int ButtonCell = 48;
        const int ButtonInset = 11;
        const int ButtonWidth = 26;
        const int RaisedHeight = 28;
        const int PressedHeight = 26;

        const int IconCell = 16;
        const int IconColumns = 6;

        // The three tints of the kit's largest square frame. Everything with a border
        // around it uses one of these: they are the only pieces in the sheet closed on
        // all four sides, and the interior is wide enough to stretch without artefacts.
        static readonly RectInt[] Frames =
        {
            new RectInt(227, 162, 42, 44),
            new RectInt(275, 162, 42, 44),
            new RectInt(323, 162, 42, 44),
        };

        static readonly Dictionary<string, Texture2D> Cuts = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Texture2D> Sheets = new Dictionary<string, Texture2D>();

        /// <summary>Nine-slice edges of a raised button, before the zoom is applied.</summary>
        public static RectOffset ButtonBorder(int zoom) => Edge(3, 3, 3, 5, zoom);
        /// <summary>The pressed one has no lip, so its bottom edge matches its top.</summary>
        public static RectOffset PressedBorder(int zoom) => Edge(3, 3, 3, 3, zoom);
        /// <summary>Frames carry a two pixel shadow under the bottom edge.</summary>
        public static RectOffset FrameBorder(int zoom) => Edge(4, 4, 4, 6, zoom);

        static RectOffset Edge(int l, int r, int t, int b, int zoom) =>
            new RectOffset(l * zoom, r * zoom, t * zoom, b * zoom);

        public static Texture2D Button(Tone tone, bool pressed, int zoom)
        {
            int row = Mathf.Clamp((int)tone, 0, 3);
            int height = pressed ? PressedHeight : RaisedHeight;
            var rect = new RectInt(ButtonInset + (pressed ? ButtonCell : 0),
                                   ButtonInset + row * ButtonCell,
                                   ButtonWidth, height);
            return Cut($"btn{row}{(pressed ? "p" : "r")}", ButtonsPath, rect, zoom);
        }

        public static Texture2D Frame(int tint, int zoom)
        {
            tint = Mathf.Clamp(tint, 0, Frames.Length - 1);
            return Cut($"frame{tint}", FramesPath, Frames[tint], zoom);
        }

        public static Texture2D IconOf(Icon icon, int zoom)
        {
            int index = Mathf.Clamp((int)icon, 0, IconColumns * 3 - 1);
            var rect = new RectInt((index % IconColumns) * IconCell,
                                   (index / IconColumns) * IconCell,
                                   IconCell, IconCell);
            return Cut($"icon{index}", IconsPath, rect, zoom);
        }

        /// <summary>
        /// Cuts a rect out of a sheet into its own texture, magnified by whole pixels.
        /// The rect's y is measured from the top of the sheet, as everywhere else here;
        /// GetPixels counts from the bottom, which is the one flip in this file.
        /// </summary>
        static Texture2D Cut(string key, string sheetPath, RectInt rect, int zoom)
        {
            zoom = Mathf.Max(1, zoom);
            string id = $"{key}@{zoom}";
            if (Cuts.TryGetValue(id, out var cached) && cached != null) return cached;

            var sheet = Sheet(sheetPath);
            if (sheet == null) return null;
            if (!sheet.isReadable)
            {
                Debug.LogError($"[Cozy TGC] Resources/{sheetPath} is not readable - run " +
                               "Tools > Cozy TGC > Apply UI Kit Import Settings.");
                return null;
            }

            Color[] src = sheet.GetPixels(rect.x, sheet.height - rect.y - rect.height,
                                          rect.width, rect.height);
            int w = rect.width * zoom;
            int h = rect.height * zoom;
            var dst = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                int row = (y / zoom) * rect.width;
                for (int x = 0; x < w; x++) dst[y * w + x] = src[row + x / zoom];
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
                name = $"UiSheet {id}",
            };
            tex.SetPixels(dst);
            tex.Apply();

            Cuts[id] = tex;
            return tex;
        }

        static Texture2D Sheet(string path)
        {
            if (Sheets.TryGetValue(path, out var cached) && cached != null) return cached;

            var sheet = Resources.Load<Texture2D>(path);
            if (sheet == null) Debug.LogError($"[Cozy TGC] UI sheet not found at Resources/{path}");
            Sheets[path] = sheet;
            return sheet;
        }

        /// <summary>
        /// Throws the cuts away when play starts. They are not saved with anything, so
        /// entering play mode with the domain reload turned off would otherwise carry
        /// last session's textures over - and destroyed ones read back as null holes.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            foreach (var tex in Cuts.Values)
            {
                if (tex == null) continue;
                if (Application.isPlaying) Object.Destroy(tex);
                else Object.DestroyImmediate(tex);
            }
            Cuts.Clear();
            Sheets.Clear();
        }
    }
}
