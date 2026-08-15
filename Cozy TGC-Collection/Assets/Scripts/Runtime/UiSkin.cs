using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// The scene's IMGUI look, built once out of <see cref="UiSheet"/> and shared by
    /// everything that draws: the shop panel, the pack HUD and the corner button.
    ///
    /// It owns the screen scale as well, which used to be worked out separately in each
    /// panel. One number for all of them is the point - two panels sitting in opposite
    /// corners at different sizes is exactly what a shared skin is for.
    ///
    /// Styles are rebuilt only when that scale changes. <see cref="Ensure"/> is safe to
    /// call at the top of every OnGUI, which is where it belongs: GUI.skin is not
    /// readable before the first one.
    /// </summary>
    public static class UiSkin
    {
        // The kit's palette, straight off the sheets. Ink is darker than anything in
        // them - the art tops out at a mid brown, which does not read as text on a
        // cream panel.
        public static readonly Color Ink = new Color32(0x4A, 0x34, 0x2C, 0xFF);
        public static readonly Color InkDim = new Color32(0x7A, 0x5C, 0x4E, 0xFF);
        public static readonly Color Parchment = new Color32(0xE8, 0xCF, 0xA6, 0xFF);

        /// <summary>Everything on screen is measured in these, the panels included.</summary>
        public static float Scale { get; private set; }

        /// <summary>
        /// Whole pixels the sheet pieces are magnified by. Kept integer and kept apart
        /// from <see cref="Scale"/>: nearest neighbour at a fractional zoom is what
        /// makes pixel art look chewed.
        /// </summary>
        public static int Zoom { get; private set; }

        /// <summary>Panels: the light frame, with room for its own border.</summary>
        public static GUIStyle Panel { get; private set; }
        /// <summary>A darker frame, for rows and slots sitting on a panel.</summary>
        public static GUIStyle Row { get; private set; }
        /// <summary>Raised, tan when the pointer is over it, sunk while it is held.</summary>
        public static GUIStyle Button { get; private set; }
        public static GUIStyle Label { get; private set; }
        public static GUIStyle Title { get; private set; }
        /// <summary>Wrapping body copy, a shade lighter than a label.</summary>
        public static GUIStyle Detail { get; private set; }

        static bool built;

        /// <summary>
        /// Brings the skin up to the current frame size. Call it first thing in OnGUI.
        /// </summary>
        public static void Ensure()
        {
            float scale = Mathf.Clamp(Screen.height / 900f, 1f, 1.8f);
            if (built && Mathf.Approximately(scale, Scale)) return;

            Scale = scale;
            Zoom = Mathf.Clamp(Mathf.RoundToInt(scale), 1, 2);
            Build();
            built = true;
        }

        /// <summary>Rounds a design size to whole pixels at the current scale.</summary>
        public static float Px(float unscaled) => Mathf.Round(unscaled * Scale);

        public static int Font(int size) => Mathf.RoundToInt(size * Scale);

        public static RectOffset Pad(int all)
        {
            int p = Mathf.RoundToInt(all * Scale);
            return new RectOffset(p, p, p, p);
        }

        /// <summary>
        /// Draws one of the kit's icons into a box, fitted whole and tinted. They are
        /// cut white for exactly this: one sheet, any colour the caller wants.
        /// </summary>
        public static void DrawIcon(Rect box, UiSheet.Icon icon, Color tint)
        {
            var tex = UiSheet.IconOf(icon, Zoom);
            if (tex == null) return;

            // Whole multiples wherever there is room, unlike the shop's card previews: a
            // 16 px glyph resampled to 21 loses its shape. A box too small for even one
            // multiple - a wide zoom in a narrow gutter - is fitted plainly rather than
            // overflowed, which is the one case where spilling would be worse.
            float fit = Mathf.Min(box.width / tex.width, box.height / tex.height);
            float steps = fit >= 1f ? Mathf.Floor(fit) : fit;
            var fitted = new Rect(0f, 0f, tex.width * steps, tex.height * steps);
            fitted.center = box.center;

            Color was = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(fitted, tex);
            GUI.color = was;
        }

        static void Build()
        {
            var frame = UiSheet.Frame(0, Zoom);
            var rowFrame = UiSheet.Frame(1, Zoom);
            var raised = UiSheet.Button(UiSheet.Tone.Cream, false, Zoom);
            var hovered = UiSheet.Button(UiSheet.Tone.Tan, false, Zoom);
            var sunk = UiSheet.Button(UiSheet.Tone.Cream, true, Zoom);

            Label = new GUIStyle
            {
                font = GUI.skin.label.font,
                fontSize = Font(13),
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
                normal = { textColor = Ink },
            };

            Title = new GUIStyle(Label)
            {
                fontSize = Font(18),
                fontStyle = FontStyle.Bold,
            };

            Detail = new GUIStyle(Label)
            {
                fontSize = Font(12),
                wordWrap = true,
                normal = { textColor = InkDim },
            };

            Panel = new GUIStyle
            {
                normal = { background = frame, textColor = Ink },
                border = UiSheet.FrameBorder(Zoom),
                padding = Pad(12),
                stretchHeight = true,
                fontSize = Font(13),
            };

            Row = new GUIStyle
            {
                normal = { background = rowFrame, textColor = Ink },
                border = UiSheet.FrameBorder(Zoom),
                padding = Pad(0),
            };

            // Focused and hovered share a face, and the "on" states repeat the off ones:
            // nothing here is a toggle, and a button that keeps a pressed face after the
            // click reads as disabled.
            Button = new GUIStyle
            {
                normal = { background = raised, textColor = Ink },
                hover = { background = hovered, textColor = Ink },
                active = { background = sunk, textColor = Ink },
                focused = { background = raised, textColor = Ink },
                onNormal = { background = raised, textColor = Ink },
                onHover = { background = hovered, textColor = Ink },
                onActive = { background = sunk, textColor = Ink },
                border = UiSheet.ButtonBorder(Zoom),
                padding = Pad(4),
                alignment = TextAnchor.MiddleCenter,
                fontSize = Font(13),
                font = GUI.skin.button.font,
                richText = false,
            };
        }
    }
}
