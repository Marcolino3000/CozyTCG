using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// The shop panel: two shelves, buy and sell, as a scrolling list of rows with a
    /// picture, a line of copy and a price.
    ///
    /// Drawn in IMGUI, like the rest of this scene's readouts - the pack HUD, the
    /// slot labels and the tear meter are all OnGUI, and a canvas here would be a
    /// second UI system in one scene for the sake of a list. It draws itself only;
    /// what a purchase does is the controller's, reached through
    /// <see cref="ICustomer"/> so the panel never has to know what a card or a pile is.
    ///
    /// It is modal: centred, over a dimmed frame, and while it is up the world stops
    /// answering the pointer entirely (see <see cref="Blocks"/>). A shop you can tear
    /// a pack open behind is a shop where every click lands twice - and the pictures
    /// are the point of a card shop, so they get the room rather than the scene
    /// behind them.
    ///
    /// What is on the two shelves lives in <see cref="ShopStock"/>, not here.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShopView : MonoBehaviour
    {
        /// <summary>Whoever keeps the purse, the drawer of packs and the collection.</summary>
        public interface ICustomer
        {
            int Coins { get; }
            /// <summary>Buys it and says whether that worked; the panel only greys out on price.</summary>
            bool Buy(ShopOffer offer);
            /// <summary>How many of that ad's cards are already filed away.</summary>
            int Progress(int adIndex);
            bool Sell(int adIndex);
        }

        enum Shelf { Buy, Sell }

        [Header("Panel")]
        [Tooltip("Size in unscaled pixels, before the screen's own scale is applied. " +
                 "Clamped to the frame, so a small window gets a smaller panel rather " +
                 "than one hanging off the edges.")]
        [SerializeField] float panelWidth = 620f;
        [SerializeField] float panelHeight = 660f;
        [Tooltip("How tall one item is, which is also how big its picture gets - the two " +
                 "knobs to turn if the previews want to be larger still.")]
        [SerializeField] float rowHeight = 150f;
        [Tooltip("Width the picture is fitted into. A card and a wrapper are both taller " +
                 "than they are wide, so the row height usually decides the size and this " +
                 "only has to leave them room.")]
        [SerializeField] float iconWidth = 110f;
        [Tooltip("How long a purchase or sale stays on the line under the tabs.")]
        [SerializeField] float messageTime = 2.6f;

        ShopStock stock;
        ICustomer customer;

        Shelf shelf = Shelf.Buy;
        Vector2 buyScroll;
        Vector2 sellScroll;
        bool open;

        string message = string.Empty;
        float messageAt = -99f;

        float scale = 1f;
        float builtScale;
        GUIStyle panelStyle, titleStyle, tabStyle, tabOnStyle, rowStyle, rowTitleStyle,
                 detailStyle, priceStyle, noteStyle, buttonStyle, purseStyle, tabButtonStyle;
        Texture2D dim;

        Rect panelRect;
        /// <summary>The purse and, under it, the tab - the corner this owns either way.</summary>
        Rect chromeRect;

        public bool IsOpen => open;

        public void Bind(ShopStock shopStock, ICustomer shopCustomer)
        {
            stock = shopStock;
            customer = shopCustomer;
        }

        public void Toggle() => SetOpen(!open);

        public void SetOpen(bool value)
        {
            open = value;
            if (!open) message = string.Empty;
        }

        /// <summary>
        /// Is the pointer the panel's? The controller asks every frame and stops
        /// handing the ray to the pack, the piles and the albums while it is.
        ///
        /// Everything, while it is open: the panel is modal and sits over a dimmed
        /// frame, so a click anywhere is a click at the shop. IMGUI swallows the one
        /// that lands on a button, but nothing tells the world about the rest.
        ///
        /// Shut, it is the corner with the purse and the tab in it. That corner has to
        /// be inert as well - the table behind it answers a click by putting a sealed
        /// pack away, and reading how much money you have is not that.
        /// </summary>
        public bool Blocks(Vector2 screenPointer)
        {
            if (open) return true;

            // OnGUI counts y down from the top, the pointer counts up from the bottom.
            var point = new Vector2(screenPointer.x, Screen.height - screenPointer.y);
            return chromeRect.Contains(point);
        }

        void OnDestroy()
        {
            if (dim != null) Destroy(dim);
        }

        // -------------------------------------------------------------------
        // Drawing
        // -------------------------------------------------------------------
        void OnGUI()
        {
            UiSkin.Ensure();
            scale = UiSkin.Scale;
            EnsureStyles();

            float pad = 12f * scale;

            // Before the corner, so the purse stays crisp over a dimmed frame rather
            // than being dimmed along with the table.
            if (open) GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Dim);
            DrawCorner(pad);

            if (!open) { panelRect = Rect.zero; return; }

            float panelW = Mathf.Min(panelWidth * scale, Screen.width - pad * 2f);
            float panelH = Mathf.Min(panelHeight * scale, Screen.height - pad * 2f);
            panelRect = new Rect((Screen.width - panelW) * 0.5f, (Screen.height - panelH) * 0.5f, panelW, panelH);

            GUILayout.BeginArea(panelRect, panelStyle);
            DrawHeader();
            DrawTabs();
            DrawMessage();
            if (shelf == Shelf.Buy) DrawBuyShelf(); else DrawSellShelf();
            GUILayout.EndArea();
        }

        /// <summary>
        /// The purse, and the tab under it. The purse is on screen whatever else is:
        /// the panel is where money is spent, but knowing how much there is decides
        /// whether to open it at all - so it does not live inside it, and it does not
        /// go away with the rest of the HUD either.
        ///
        /// The tab is drawn only while the shop is shut. The open panel carries its own
        /// close button, and a tab left under it would eat the clicks landing on the
        /// panel over it - IMGUI gives an event to whichever control asked for it first.
        /// </summary>
        void DrawCorner(float pad)
        {
            float width = 104f * scale;
            float height = 28f * scale;
            var purse = new Rect(Screen.width - width - pad, pad, width, height);

            // The coin says what the number is, so the number is only the number. Both
            // styles keep a gutter open on the left in their padding, which is what the
            // centred text is centred inside - the icon goes in the gutter afterwards.
            GUI.Box(purse, $"{(customer != null ? customer.Coins : 0)}", purseStyle);
            UiSkin.DrawIcon(Gutter(purse), UiSheet.Icon.Coin, UiSkin.Ink);

            chromeRect = purse;
            if (open) return;

            var tab = new Rect(purse.x, purse.yMax + 6f * scale, width, height);
            if (GUI.Button(tab, "Shop", tabButtonStyle)) SetOpen(true);
            UiSkin.DrawIcon(Gutter(tab), UiSheet.Icon.Cart, UiSkin.Ink);
            chromeRect = new Rect(purse.x, purse.y, width, tab.yMax - purse.y);
        }

        /// <summary>The strip along the left of a corner box that its padding leaves free.</summary>
        static Rect Gutter(Rect box) =>
            new Rect(box.x + UiSkin.Px(5f), box.y, UiSkin.Px(18f), box.height);

        void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("The Card Counter", titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", buttonStyle, GUILayout.Width(72f * scale))) SetOpen(false);
            GUILayout.EndHorizontal();
        }

        void DrawTabs()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Buy", shelf == Shelf.Buy ? tabOnStyle : tabStyle)) shelf = Shelf.Buy;
            if (GUILayout.Button("Sell", shelf == Shelf.Sell ? tabOnStyle : tabStyle)) shelf = Shelf.Sell;
            GUILayout.EndHorizontal();
        }

        void DrawMessage()
        {
            string text = Time.unscaledTime - messageAt < messageTime ? message : string.Empty;
            if (string.IsNullOrEmpty(text))
                text = shelf == Shelf.Buy
                    ? "Packs go to the drawer on the left, cards to the default pile."
                    : "Collectors pay for cards filed in your piles and albums.";

            GUILayout.Label(text, noteStyle);
        }

        void DrawBuyShelf()
        {
            buyScroll = GUILayout.BeginScrollView(buyScroll);
            if (stock != null)
                foreach (var offer in stock.Offers) DrawOffer(offer);
            GUILayout.EndScrollView();
        }

        void DrawOffer(ShopOffer offer)
        {
            if (offer == null) return;

            Rect body = DrawRowFrame(Row(), offer.icon, offer.title, offer.detail);
            bool afford = customer != null && customer.Coins >= offer.price;

            DrawPrice(body, $"{offer.price} c");
            GUI.enabled = afford;
            if (DrawAction(body, "Buy"))
            {
                bool bought = customer != null && customer.Buy(offer);
                Say(bought
                    ? (offer.goods == ShopGoods.BoosterPacks
                        ? $"+{offer.count} pack{(offer.count == 1 ? "" : "s")} in the drawer."
                        : $"+{offer.count} card{(offer.count == 1 ? "" : "s")} on the default pile.")
                    : "That did not go through.");
            }
            GUI.enabled = true;
        }

        void DrawSellShelf()
        {
            sellScroll = GUILayout.BeginScrollView(sellScroll);
            if (stock != null)
                for (int i = 0; i < stock.Wanted.Count; i++) DrawAd(i, stock.Wanted[i]);
            GUILayout.EndScrollView();
        }

        void DrawAd(int index, WantedAd ad)
        {
            if (ad == null) return;

            Rect body = DrawRowFrame(Row(), ad.icon, ad.title, ad.detail);

            int total = ad.Total;
            int have = customer != null ? customer.Progress(index) : 0;
            bool complete = have >= total && total > 0;

            DrawPrice(body, complete ? $"{ad.price} c   ready" : $"{ad.price} c   {have}/{total} owned");

            GUI.enabled = complete;
            if (DrawAction(body, "Sell"))
            {
                bool sold = customer != null && customer.Sell(index);
                Say(sold ? $"Sold for {ad.price} c." : "They turned it down.");
            }
            GUI.enabled = true;
        }

        Rect Row() => GUILayoutUtility.GetRect(1f, rowHeight * scale, GUILayout.ExpandWidth(true));

        /// <summary>
        /// Draws the shared half of a row - box, picture, title, copy - and hands
        /// back the area beside the picture, which is where a price and its button go.
        /// </summary>
        Rect DrawRowFrame(Rect row, ShopIcon icon, string title, string detail)
        {
            GUI.Box(row, GUIContent.none, rowStyle);

            float inset = 8f * scale;
            var iconBox = new Rect(row.x + inset, row.y + inset, iconWidth * scale, row.height - inset * 2f);
            DrawIcon(iconBox, icon);

            var body = new Rect(iconBox.xMax + inset * 1.5f, row.y + inset,
                                row.xMax - iconBox.xMax - inset * 2.5f, row.height - inset * 2f);
            GUI.Label(new Rect(body.x, body.y, body.width, 22f * scale), title, rowTitleStyle);
            // Stops short of the price line along the bottom, which shares the row.
            GUI.Label(new Rect(body.x, body.y + 22f * scale, body.width, body.height - 52f * scale),
                      detail, detailStyle);
            return body;
        }

        void DrawPrice(Rect body, string text)
        {
            GUI.Label(new Rect(body.x, body.yMax - 26f * scale, body.width - 84f * scale, 24f * scale),
                      text, priceStyle);
        }

        bool DrawAction(Rect body, string label)
        {
            return GUI.Button(new Rect(body.xMax - 76f * scale, body.yMax - 28f * scale,
                                       76f * scale, 26f * scale), label, buttonStyle);
        }

        /// <summary>
        /// Fits a picture into its box on the box's own terms: the previews are cards
        /// (73x113) and wrappers (84x154) side by side, and stretching either into a
        /// shared square is worse than the gap that letterboxing leaves.
        ///
        /// Deliberately not snapped to whole multiples, even though these are pixel art
        /// at point filtering. A card is 113 tall and a wrapper 154, so any box big
        /// enough to matter puts one of them between multiples - snapping would drop
        /// both back to their own size and make the box's size mean nothing.
        /// </summary>
        void DrawIcon(Rect box, ShopIcon icon)
        {
            if (!icon.IsValid) return;

            float pixelWidth = icon.texture.width * icon.uv.width;
            float pixelHeight = icon.texture.height * icon.uv.height;
            if (pixelWidth <= 0f || pixelHeight <= 0f) return;

            float fit = Mathf.Min(box.width / pixelWidth, box.height / pixelHeight);
            var fitted = new Rect(0f, 0f, pixelWidth * fit, pixelHeight * fit);
            fitted.center = box.center;
            GUI.DrawTextureWithTexCoords(fitted, icon.texture, icon.uv);
        }

        void Say(string text)
        {
            message = text;
            messageAt = Time.unscaledTime;
        }

        // -------------------------------------------------------------------
        // Styles
        // -------------------------------------------------------------------
        Texture2D Dim
        {
            get
            {
                if (dim != null) return dim;

                dim = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                dim.SetPixel(0, 0, new Color(0.03f, 0.03f, 0.05f, 0.72f));
                dim.Apply();
                return dim;
            }
        }

        /// <summary>
        /// Everything here is the shared skin with a size on it. The panel keeps the
        /// light frame and the rows the darker one, so a list of them reads as a list
        /// rather than as one long slab.
        /// </summary>
        void EnsureStyles()
        {
            if (panelStyle != null && Mathf.Approximately(builtScale, scale)) return;
            builtScale = scale;

            panelStyle = new GUIStyle(UiSkin.Panel);
            titleStyle = new GUIStyle(UiSkin.Title);

            // Left padding wide enough for the icon that goes in beside the text.
            var gutter = new RectOffset(Mathf.RoundToInt(24f * scale), Mathf.RoundToInt(6f * scale), 0, 0);
            purseStyle = new GUIStyle(UiSkin.Panel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Font(16),
                padding = gutter,
                stretchHeight = false,
            };
            tabButtonStyle = new GUIStyle(UiSkin.Button) { fontSize = Font(14), padding = gutter };

            tabStyle = new GUIStyle(UiSkin.Button) { fontSize = Font(14), fixedHeight = 28f * scale };
            tabOnStyle = new GUIStyle(tabStyle) { fontStyle = FontStyle.Bold };
            rowStyle = new GUIStyle(UiSkin.Row);
            rowTitleStyle = new GUIStyle(UiSkin.Label) { fontStyle = FontStyle.Bold, fontSize = Font(15) };
            detailStyle = new GUIStyle(UiSkin.Detail) { fontSize = Font(12) };
            priceStyle = new GUIStyle(UiSkin.Label)
            {
                alignment = TextAnchor.LowerLeft,
                fontSize = Font(14),
            };
            noteStyle = new GUIStyle(UiSkin.Detail)
            {
                fontSize = Font(12),
                fixedHeight = 34f * scale,
            };
            buttonStyle = new GUIStyle(UiSkin.Button) { fontSize = Font(13) };
        }

        int Font(int size) => UiSkin.Font(size);
    }
}
