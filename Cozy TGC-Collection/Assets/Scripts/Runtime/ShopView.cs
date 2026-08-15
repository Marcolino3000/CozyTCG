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
        [Tooltip("Gap left between the panel and the edge of the screen, in unscaled " +
                 "pixels. The panel takes everything inside it: a card shop is a wall of " +
                 "pictures and prices, and a box in the middle of the frame reads small " +
                 "however big the type in it is.")]
        [SerializeField] float edgeMargin = 48f;
        [Tooltip("How tall one item is, which is also how big its picture gets - the two " +
                 "knobs to turn if the previews want to be larger still.")]
        [SerializeField] float rowHeight = 225f;
        [Tooltip("Width the picture is fitted into. A card and a wrapper are both taller " +
                 "than they are wide, so the row height usually decides the size and this " +
                 "only has to leave them room.")]
        [SerializeField] float iconWidth = 168f;
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

            float pad = edgeMargin * scale;

            // Before the corner, so the purse stays crisp over a dimmed frame rather
            // than being dimmed along with the table.
            if (open) GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Dim);

            // And before the panel, which is what keeps the corner clickable while the
            // shop is open: IMGUI hands a click to whichever control asked for it first,
            // so the corner is drawn first and the panel leaves its block free.
            float corner = DrawCorner(pad);

            if (!open) { panelRect = Rect.zero; return; }

            panelRect = new Rect(pad, pad, Screen.width - pad * 2f, Screen.height - pad * 2f);

            GUILayout.BeginArea(panelRect, panelStyle);
            DrawHeader(corner);
            DrawTabs(corner);
            DrawMessage();
            if (shelf == Shelf.Buy) DrawBuyShelf(); else DrawSellShelf();
            GUILayout.EndArea();
        }

        /// <summary>
        /// The purse, and the tab under it. Both are on screen whatever else is: the
        /// panel is where money is spent, but knowing how much there is decides whether
        /// to open it at all - so the purse does not live inside it, and it does not go
        /// away with the rest of the HUD either.
        ///
        /// The tab stays up with the shop open, and shuts it again - one button that
        /// goes in and comes back out, rather than one to open and a different one to
        /// close. It draws before the panel so that the click is the tab's: IMGUI hands
        /// an event to whichever control asked for it first, and the panel keeps this
        /// block clear rather than racing it.
        ///
        /// Hands back the width the panel has to leave alone.
        /// </summary>
        float DrawCorner(float pad)
        {
            float width = 164f * scale;
            float height = 40f * scale;
            var purse = new Rect(Screen.width - width - pad, pad, width, height);

            // The coin says what the number is, so the number is only the number. Both
            // styles keep a gutter open on the left in their padding, which is what the
            // centred text is centred inside - the icon goes in the gutter afterwards.
            GUI.Box(purse, $"{(customer != null ? customer.Coins : 0)}", purseStyle);
            UiSkin.DrawIcon(Gutter(purse), UiSheet.Icon.Coin, UiSkin.Ink);

            var tab = new Rect(purse.x, purse.yMax + 6f * scale, width, height);
            if (GUI.Button(tab, "Shop", tabButtonStyle)) SetOpen(!open);
            // A cross while it is open: the button is the way out as well as the way in,
            // and the cart on a shop that is already up says nothing.
            UiSkin.DrawIcon(Gutter(tab), open ? UiSheet.Icon.Cross : UiSheet.Icon.Cart, UiSkin.Ink);

            chromeRect = new Rect(purse.x, purse.y, width, tab.yMax - purse.y);
            return width + pad;
        }

        /// <summary>The strip along the left of a corner box that its padding leaves free.</summary>
        static Rect Gutter(Rect box) =>
            new Rect(box.x + UiSkin.Px(8f), box.y, UiSkin.Px(26f), box.height);

        /// <summary>
        /// <paramref name="corner"/> is the block the purse and the tab sit in, over the
        /// panel's top right. Both rows that reach that far end short of it, so nothing
        /// of the panel's own is hiding under a button that answers first.
        /// </summary>
        void DrawHeader(float corner)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("The Card Counter", titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", buttonStyle, GUILayout.Width(120f * scale))) SetOpen(false);
            GUILayout.Space(corner);
            GUILayout.EndHorizontal();
        }

        void DrawTabs(float corner)
        {
            // Held to a readable width rather than split across the whole panel: on a
            // full screen shelf a half-width Buy button is a banner, not a tab.
            var width = GUILayout.Width(190f * scale);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Buy", shelf == Shelf.Buy ? tabOnStyle : tabStyle, width)) shelf = Shelf.Buy;
            if (GUILayout.Button("Sell", shelf == Shelf.Sell ? tabOnStyle : tabStyle, width)) shelf = Shelf.Sell;
            GUILayout.FlexibleSpace();
            GUILayout.Space(corner);
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
            GUI.Label(new Rect(body.x, body.y, body.width, 38f * scale), title, rowTitleStyle);
            // Stops short of the price line along the bottom, which shares the row.
            GUI.Label(new Rect(body.x, body.y + 38f * scale, body.width, body.height - 84f * scale),
                      detail, detailStyle);
            return body;
        }

        void DrawPrice(Rect body, string text)
        {
            GUI.Label(new Rect(body.x, body.yMax - 40f * scale, body.width - 152f * scale, 38f * scale),
                      text, priceStyle);
        }

        bool DrawAction(Rect body, string label)
        {
            return GUI.Button(new Rect(body.xMax - 138f * scale, body.yMax - 44f * scale,
                                       138f * scale, 42f * scale), label, buttonStyle);
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
                // Deep enough that the panel is the only lit thing on screen. The
                // table behind is warm and busy, and a shy dim left the cream frame
                // sitting on top of it looking like tracing paper.
                dim.SetPixel(0, 0, new Color(0.03f, 0.03f, 0.05f, 0.86f));
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
            // The shop takes the whole frame, so its type is a size up on the pack HUD's
            // rather than the same: a panel this big with small print in it reads as a
            // wall of grey.
            titleStyle = new GUIStyle(UiSkin.Title) { fontSize = Font(30) };

            // Left padding wide enough for the icon that goes in beside the text.
            var gutter = new RectOffset(Mathf.RoundToInt(24f * scale), Mathf.RoundToInt(6f * scale), 0, 0);
            purseStyle = new GUIStyle(UiSkin.Panel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Font(23),
                padding = gutter,
                stretchHeight = false,
            };
            tabButtonStyle = new GUIStyle(UiSkin.Button) { fontSize = Font(21), padding = gutter };

            tabStyle = new GUIStyle(UiSkin.Button) { fontSize = Font(21), fixedHeight = 42f * scale };
            tabOnStyle = new GUIStyle(tabStyle) { fontStyle = FontStyle.Bold };
            rowStyle = new GUIStyle(UiSkin.Row);
            rowTitleStyle = new GUIStyle(UiSkin.Label) { fontStyle = FontStyle.Bold, fontSize = Font(23) };
            // A row's copy is the smallest type on screen and the most of it, so it is
            // the one place the dim ink is dropped: it sits on the darker row frame,
            // where a lighter brown is exactly the "why is this faded" of the panel.
            detailStyle = new GUIStyle(UiSkin.Detail)
            {
                fontSize = Font(19),
                normal = { textColor = UiSkin.Ink },
            };
            priceStyle = new GUIStyle(UiSkin.Label)
            {
                alignment = TextAnchor.LowerLeft,
                fontStyle = FontStyle.Bold,
                fontSize = Font(21),
            };
            noteStyle = new GUIStyle(UiSkin.Detail)
            {
                fontSize = Font(19),
                fixedHeight = 56f * scale,
            };
            buttonStyle = new GUIStyle(UiSkin.Button) { fontSize = Font(20) };
        }

        int Font(int size) => UiSkin.Font(size);
    }
}
