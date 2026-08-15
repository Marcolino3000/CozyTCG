using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CozyTGC
{
    /// <summary>
    /// Runs the pack opening loop: roll a wrapper and its contents, let the
    /// player rip the seam open, then sort the stack into the piles along the
    /// bottom - a click sends the next card to the default slot, a drag lets it
    /// be dropped on any of them. From there cards go into one of the three
    /// collector albums, picked off the shelf of books down the left.
    ///
    /// Pointer handling for the wrapper, the stack, the slots and the shelf is
    /// analytic - they are all flat quads facing the camera, so projecting the ray
    /// onto their plane is simpler and more forgiving than fitting colliders
    /// around them. Cards that have landed in a slot keep their colliders and
    /// belong to CardInteractor, which is what gives them hover, flip and free spin.
    ///
    /// The camera never moves. Anything that needs a closer look comes to it
    /// instead: the pack rig slides in while the wrapper is sealed, and the album
    /// scales itself into the frame. That keeps the row of piles pinned where the
    /// player left it, and it is why the pointer maths can assume a fixed frame.
    ///
    /// Packs are not free: they come out of the drawer under the shelf, which is
    /// filled at the shop. This is also the shop's <see cref="ShopView.ICustomer"/> -
    /// it holds the purse, mints the cards a purchase delivers and hands over the
    /// ones a wanted ad asks for, because it is the only thing here that knows both
    /// where cards come from and where they end up.
    /// </summary>
    public class PackOpeningController : MonoBehaviour, ShopView.ICustomer
    {
        enum View { Packs, Album }

        [Header("References")]
        [SerializeField] Camera cam;
        [SerializeField] CardInteractor interactor;
        [SerializeField] CardPackView pack;
        [SerializeField] CardPackDeck deck;
        [SerializeField] CardSlotBoard board;
        [Tooltip("One per book on the shelf. Each album keeps its own cards.")]
        [SerializeField] List<CardAlbum> albums = new List<CardAlbum>();
        [SerializeField] AlbumShelf shelf;
        [Tooltip("The drawer of unopened packs, under the shelf.")]
        [SerializeField] PackTray tray;
        [SerializeField] ShopView shop;
        [Tooltip("The button in the bottom right corner that leaves for the dialog scene.")]
        [SerializeField] SceneLinkButton dialogLink;
        [Tooltip("What a bought card is made from. The booster stack is bound to the same " +
                 "prefab separately - a card off the shelf and a card out of a pack have to " +
                 "be the same object, or one of them cannot be filed, dragged or sold.")]
        [SerializeField] GameObject cardPrefab;

        [Header("Contents")]
        [SerializeField] List<CardArtSet> artSets = new List<CardArtSet>();
        [SerializeField] List<Material> rarityMaterials = new List<Material>();
        [SerializeField] List<string> rarityNames = new List<string>();
        [Tooltip("Pull weight per rarity, in the same order as the materials.")]
        [SerializeField] List<float> rarityWeights = new List<float> { 62f, 20f, 10f, 5f, 3f };
        [Tooltip("Lowest rarity the last card in a pack may roll, so every pack " +
                 "finishes on something worth turning over.")]
        [SerializeField] int guaranteedTier = 1;

        [Header("Shop")]
        [Tooltip("What the shop sells and what its collectors are after. Authored asset - " +
                 "edit Assets/Shop/ShopCatalog.asset, not this scene.")]
        [SerializeField] ShopCatalog shopCatalog;
        [Tooltip("Enough to walk in and buy something on the first day.")]
        [SerializeField] int startingCoins = 350;
        [Tooltip("Packs in the drawer at the start. The first one is put on the table at once.")]
        [SerializeField] int startingPacks = 3;
        [Tooltip("What one card is worth to a collector. Only the made-up ads that keep " +
                 "the sell board full are priced off this - everything the shop charges, " +
                 "and every written ad's payout, is a flat number on the catalog.")]
        [SerializeField] int cardValue = 30;

        [Header("Sorting")]
        [Tooltip("Pointer travel below this counts as a click into the default slot " +
                 "instead of the start of a drag.")]
        [SerializeField] float dragThreshold = 6f;
        [Tooltip("Depth the held card floats at, in front of the slots and the stack.")]
        [SerializeField] float dragDepth = -0.5f;
        [SerializeField] float dragFollow = 22f;

        [Header("Slot Row")]
        [Tooltip("Size the row of piles sits at, so the pack and the album get most of " +
                 "the frame. Fixed on purpose: a row that swelled under the pointer " +
                 "would shove the piles around underneath the cursor aiming at them, " +
                 "and it moved the landing spot of cards that were still flying in.")]
        [SerializeField] float rowScale = 0.62f;
        [Tooltip("Half height of a slot frame at full size, used to work out the row's " +
                 "bottom edge from the position it was authored at.")]
        [SerializeField] float rowSlotHalfHeight = 0.61f;

        [Header("Framing")]
        [Tooltip("Pack and stack hang off this. The camera never moves - the rig is what " +
                 "travels, sliding towards the camera while the wrapper is sealed and back " +
                 "out once the cards are on their way to the slots.")]
        [SerializeField] Transform packRig;
        [Tooltip("Where the rig rides while the pack is sealed, measured from its resting " +
                 "place. -Z is towards the camera, so the wrapper comes close enough that " +
                 "the seam is a comfortable drag across.")]
        [SerializeField] Vector3 sealedRigOffset = new Vector3(0f, -0.62f, -2.52f);
        [SerializeField] float rigSpeed = 3.4f;
        [Tooltip("Clearance the fitted album keeps from the edge of the frame, from the " +
                 "shelf and from the row of piles. The fit is height bound in every " +
                 "sensible aspect, so this is the knob that sizes the album: less " +
                 "margin, bigger book.")]
        [SerializeField] float albumMargin = 0.05f;
        [Tooltip("Plane the albums are fitted onto. The fit only ever moves an album across " +
                 "the frame, never through it.")]
        [SerializeField] float albumDepth;

        [Header("HUD")]
        [SerializeField] bool showHud = true;

        bool wasPressed;
        bool grabbedPack;
        bool overPack;
        bool shopWasOpen;
        Vector3 rigRest;

        View view = View.Packs;
        int albumIndex = -1;
        bool packWasActive = true;
        bool deckWasActive = true;

        float rowBottomEdge;

        bool pressedDeck;
        CardSlot pressedSlot;
        /// <summary>Which half of the album spread the press landed on: -1 left, +1 right, 0 neither.</summary>
        int pressedSide;
        /// <summary>Which book on the shelf the press landed on, or -1.</summary>
        int pressedIcon = -1;
        /// <summary>Did the press land on the drawer of packs?</summary>
        bool pressedTray;
        /// <summary>Did the press land on the bare table - nothing at all under it?</summary>
        bool pressedEmpty;
        Vector2 pressPointer;
        CardView heldCard;
        /// <summary>Slot the held card came out of, or null if it came off the pack.</summary>
        CardSlot heldFrom;
        /// <summary>Last card the pack or the shop produced, for the readout.</summary>
        CardIdentity lastCard = CardIdentity.None;

        GUIStyle labelStyle;
        GUIStyle titleStyle;

        int packsOpened;

        CardCatalog catalog;
        ShopStock stock;
        int coins;
        int packs;
        /// <summary>The wrapper the drawer is showing, which is the one that comes out next.</summary>
        int nextPack;

        readonly List<CardDraw> draws = new List<CardDraw>();
        readonly List<CardCollection.Held> owned = new List<CardCollection.Held>();
        readonly List<CardCollection.Held> matched = new List<CardCollection.Held>();
        readonly List<int> adProgress = new List<int>();

        /// <summary>The album that is out, or null while the packs have the frame.</summary>
        CardAlbum Album => albumIndex >= 0 && albumIndex < albums.Count ? albums[albumIndex] : null;

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            foreach (var set in artSets) set.Load();
            if (pack != null) pack.Opened += OnPackOpened;
            if (packRig != null) rigRest = packRig.position;

            catalog = new CardCatalog(artSets, rarityNames);
            stock = new ShopStock(shopCatalog, catalog, CardPackSheet.Load(), cardValue);
            if (shop != null) shop.Bind(stock, this);

            coins = Mathf.Max(0, startingCoins);
            packs = Mathf.Max(0, startingPacks);
            nextPack = CardPackSheet.RandomIndex();
            RefreshTray();

            if (board != null)
            {
                // The row is authored at full size. Remember the bottom edge that
                // implies and pin it, so shrinking pulls the row down towards the edge
                // of the frame instead of sliding half of it off the bottom.
                rowBottomEdge = board.transform.position.y - rowSlotHalfHeight;
                ApplyRowScale();
            }
        }

        void OnDestroy()
        {
            if (pack != null) pack.Opened -= OnPackOpened;
        }

        void Start()
        {
            foreach (var album in albums)
                if (album != null) album.Close(true);

            // The wrapper the builder left on the table is a rest pose with nothing
            // inside it, so the first pack is dealt outright rather than through the
            // drawer's one-at-a-time check - which would see that wrapper and refuse.
            // With an empty drawer it is cleared away instead, for the same reason.
            if (!DealPack() && pack != null) pack.gameObject.SetActive(false);

            // Opens with the pack already close in rather than flying towards the camera.
            if (packRig != null) packRig.position = rigRest + sealedRigOffset;
        }

        // -------------------------------------------------------------------
        // Views
        // -------------------------------------------------------------------
        /// <summary>
        /// Brings one of the three albums out. Clicking the book that is already
        /// open shuts it again, which is the only way back to the packs that does
        /// not go through the keyboard.
        /// </summary>
        void OpenAlbum(int index)
        {
            if (index < 0 || index >= albums.Count || albums[index] == null) return;
            if (view == View.Album && index == albumIndex) { CloseAlbum(); return; }

            ReturnHeldCard();

            if (view == View.Packs)
            {
                // The pack and its stack step aside; the row of piles stays, because
                // filling an album means dragging out of it.
                if (pack != null) { packWasActive = pack.gameObject.activeSelf; pack.gameObject.SetActive(false); }
                if (deck != null) { deckWasActive = deck.gameObject.activeSelf; deck.gameObject.SetActive(false); }
            }
            else
            {
                // Straight swap between two albums: the one on screen goes at once
                // rather than closing over the one coming out.
                var current = Album;
                if (current != null) current.Close(true);
            }

            view = View.Album;
            albumIndex = index;
            if (shelf != null) shelf.SetSelected(index);

            albums[index].Open();
            // Fitted before the first frame it is on screen, or it flashes at whatever
            // size the scene happened to leave it.
            FitAlbum();
        }

        /// <summary>
        /// Starts the album shutting. The packs come back when the book has finished,
        /// not before - UpdateAlbumView is what waits for it.
        /// </summary>
        void CloseAlbum()
        {
            if (view != View.Album) return;

            ReturnHeldCard();
            var current = Album;
            if (current != null) current.Close(false);
            if (shelf != null) shelf.SetSelected(-1);
        }

        void ShowPacks()
        {
            if (view == View.Packs) return;

            ReturnHeldCard();
            view = View.Packs;
            albumIndex = -1;

            foreach (var album in albums)
                if (album != null) album.Close(true);

            if (shelf != null) shelf.SetSelected(-1);
            if (pack != null) pack.gameObject.SetActive(packWasActive);
            if (deck != null) deck.gameObject.SetActive(deckWasActive);
        }

        /// <summary>Is the wrapper still on? Then the rig rides close to the camera.</summary>
        bool PackSealed => pack != null && pack.gameObject.activeSelf &&
                           (pack.Current == CardPackView.State.Sealed ||
                            pack.Current == CardPackView.State.Tearing);

        void Update()
        {
            if (KeyPressed(Key.Space) || KeyPressed(Key.R)) OpenNextPack();
            if (KeyPressed(Key.Q)) UnpackAll();
            if (KeyPressed(Key.C)) ClearBoard();
            if (KeyPressed(Key.H)) showHud = !showHud;
            if (KeyPressed(Key.B) && shop != null) shop.Toggle();
            if (KeyPressed(Key.Escape) && shop != null) shop.SetOpen(false);
            WatchShop();

            MoveRig();
            if (shelf != null) shelf.Layout(cam);
            LayoutTray();
            UpdateAlbumView();
            RefreshProgress();
            HandlePointer();
        }

        /// <summary>
        /// Watched rather than hooked, because the shop is opened from three places -
        /// its own tab, the B key, and closing it again from inside. Going to the
        /// counter puts a whole pack back in the drawer: the panel covers the table, and
        /// coming back to a pack left sitting under it is worse than putting it away.
        /// </summary>
        void WatchShop()
        {
            bool open = shop != null && shop.IsOpen;
            if (open && !shopWasOpen) StowPack();
            shopWasOpen = open;

            // The panel is modal and dims the frame; the way out of the scene goes with
            // the table under it rather than sitting on top of the dimming.
            if (dialogLink != null) dialogLink.SetVisible(!open);
        }

        /// <summary>
        /// Hangs the drawer of packs off the bottom of the shelf. Both pin themselves
        /// to the left edge every frame rather than sitting where they were authored,
        /// because the camera is fixed but the aspect it is running at is not.
        /// </summary>
        void LayoutTray()
        {
            if (tray == null) return;
            tray.Layout(cam,
                        shelf != null ? shelf.CenterX : tray.transform.position.x,
                        shelf != null ? shelf.BottomY : tray.transform.position.y);
        }

        void ApplyRowScale()
        {
            board.transform.localScale = Vector3.one * rowScale;
            Vector3 p = board.transform.position;
            p.y = rowBottomEdge + rowSlotHalfHeight * rowScale;
            board.transform.position = p;
        }

        /// <summary>
        /// What used to be a camera dolly, turned inside out: the camera is nailed
        /// down and the pack and its stack come to it instead. Only they move, so
        /// the row of piles keeps its place in the frame throughout.
        /// </summary>
        void MoveRig()
        {
            if (packRig == null) return;

            Vector3 target = PackSealed ? rigRest + sealedRigOffset : rigRest;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            packRig.position = Vector3.Lerp(packRig.position, target, 1f - Mathf.Exp(-rigSpeed * dt));
        }

        void UpdateAlbumView()
        {
            if (view != View.Album) return;

            var open = Album;
            // The packs only come back once the book has finished shutting, so the
            // two never share the frame.
            if (open == null || !open.IsVisible) { ShowPacks(); return; }
            FitAlbum();
        }

        /// <summary>
        /// Sizes the open spread into whatever the frame has left beside the shelf
        /// and above the row of piles. The camera never pulls back for it, so a
        /// spread that fits a wide view has to give in a narrow one - hence a fit
        /// rather than a fixed size.
        /// </summary>
        void FitAlbum()
        {
            var open = Album;
            if (open == null || cam == null) return;

            float dist = Mathf.Abs(albumDepth - cam.transform.position.z);
            Vector2 frameHalf = FrameHalfSize(albumDepth);
            float halfHeight = frameHalf.y;
            float halfWidth = frameHalf.x;

            // The row is a fixed size and the shelf is measured at its widest, so the
            // album keeps clear of both without resizing as the pointer crosses them.
            //
            // The shelf's width is projected onto this plane first: it lives nearer
            // the camera, so it covers more of the frame than its own width says, and
            // reserving the raw number would let the album slide under it.
            float shelfWidth = 0f;
            if (shelf != null)
            {
                float shelfDist = Mathf.Abs(shelf.transform.position.z - cam.transform.position.z);
                shelfWidth = shelf.Width * (cam.orthographic ? 1f : dist / Mathf.Max(shelfDist, 0.01f));
            }

            float bottom = rowBottomEdge + rowSlotHalfHeight * 2f * rowScale + albumMargin;
            float top = cam.transform.position.y + halfHeight - albumMargin;
            float left = cam.transform.position.x - halfWidth + albumMargin + shelfWidth;
            float right = cam.transform.position.x + halfWidth - albumMargin;

            // No ceiling on the scale: an album is authored at the size of the book
            // art, 190 by 160 pixels, and is meant to be blown up into what the frame
            // has left rather than held at the size it was drawn.
            //
            // Fitted by the whole book rather than by the spread of pages, or its
            // covers would hang off the frame and over the row of piles. That is also
            // why the centring is corrected: the album's origin is the spine, which
            // the art does not sit symmetrically around.
            Vector2 frame = open.FrameSize;
            float scale = Mathf.Max(Mathf.Min((right - left) / Mathf.Max(frame.x, 0.01f),
                                              (top - bottom) / Mathf.Max(frame.y, 0.01f)), 0.01f);

            Vector2 offset = open.FrameCenter * scale;
            open.transform.localScale = Vector3.one * scale;
            open.transform.position = new Vector3((left + right) * 0.5f - offset.x,
                                                  (bottom + top) * 0.5f - offset.y, albumDepth);
        }

        /// <summary>
        /// Half the frame at a given depth. Everything that has to know where the edge
        /// of the picture is - the album fit, the shelf, a card flying in from the shop -
        /// asks here, because the camera is fixed but its aspect is not.
        /// </summary>
        Vector2 FrameHalfSize(float depth)
        {
            if (cam == null) return Vector2.one;

            float dist = Mathf.Abs(depth - cam.transform.position.z);
            float halfHeight = cam.orthographic
                ? cam.orthographicSize
                : dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return new Vector2(halfHeight * cam.aspect, halfHeight);
        }

        // -------------------------------------------------------------------
        // Pack lifecycle
        // -------------------------------------------------------------------
        /// <summary>
        /// Is there still a pack on the table - sealed, half torn, or half dealt? The
        /// wrapper counts as gone when it is switched off as well as when it has slid
        /// away, which is how an empty drawer leaves the table properly clear.
        /// </summary>
        bool PackInPlay => (pack != null && pack.gameObject.activeSelf &&
                            pack.Current != CardPackView.State.Gone) ||
                           (deck != null && deck.Remaining > 0);

        /// <summary>
        /// Takes a pack out of the drawer and puts it on the table, where it rides up
        /// close to the camera while it is sealed - which is the whole of "show it
        /// large so it can be opened".
        ///
        /// Clicking the drawer while an album is out is a way back to the table
        /// rather than a request for another pack, so that spends nothing. Neither
        /// does clicking it while a pack is already out: one at a time, or the cards
        /// still in the old one are thrown away by the new one.
        /// </summary>
        void OpenNextPack()
        {
            if (view == View.Album) ShowPacks();
            if (PackInPlay) return;
            DealPack();
        }

        bool DealPack()
        {
            if (packs <= 0) return false;

            packs--;
            NewPack();
            RefreshTray();
            return true;
        }

        void NewPack()
        {
            if (pack == null || deck == null) return;

            // The piles are the point of the scene, so a new pack never clears them.
            ShowPacks();
            ReturnHeldCard();
            FillDeck();
            pack.Load(nextPack, CardPackSheet.Load());
            // Rolled ahead of time so the drawer can show the wrapper that is coming.
            nextPack = CardPackSheet.RandomIndex();
            grabbedPack = false;
            packsOpened++;
        }

        void RefreshTray()
        {
            if (tray == null) return;
            tray.SetCount(packs);
            tray.SetPack(nextPack);
        }

        /// <summary>
        /// Puts a pack back in the drawer unopened, for a pack that has been brought
        /// out and then thought better of - clicking the drawer again, clicking the
        /// bare table around it, or walking off to the shop.
        ///
        /// Only while the wrapper is whole. Once it is torn the cards are out and there
        /// is nothing to put back, and a pack that was picked up and put down again is
        /// the same pack: it keeps its wrapper, and its contents are rolled fresh when
        /// it next comes out, because an unopened pack has not been dealt yet.
        /// </summary>
        bool StowPack()
        {
            if (!PackSealed) return false;

            nextPack = pack.PackIndex;
            pack.gameObject.SetActive(false);
            grabbedPack = false;
            if (deck != null) deck.Clear();

            packs++;
            RefreshTray();
            return true;
        }

        void ClearBoard()
        {
            ReturnHeldCard();
            if (board != null) board.ClearAll();
        }

        void FillDeck()
        {
            // The deck is rolled per card, not per pack: a booster is mostly one deck
            // with the odd card from another mixed in, and how odd is the packWeight on
            // each art set.
            int count = Mathf.Max(1, deck.CardCapacity);
            draws.Clear();
            for (int i = 0; i < count; i++)
            {
                // The last slot is the one people wait for, so it never rolls common.
                int floor = i == count - 1 ? Mathf.Clamp(guaranteedTier, 0, rarityMaterials.Count - 1) : 0;
                var draw = RollCard(PickArtSet(), floor);
                if (!draw.id.IsValid)
                {
                    Debug.LogError("[Cozy TGC] No card artwork loaded - check the art sets on PackOpeningController.");
                    return;
                }
                draws.Add(draw);
            }

            deck.Fill(draws, PackBack());
        }

        /// <summary>
        /// The back every card in a pack is dealt with. The decks' backs look nothing
        /// alike, so dealing each card its own would show through the face-down stack -
        /// the purple tarot back among the blue ones announces the rare card before
        /// anybody turns it over. Taken from the deck the pack is mostly made of, so it
        /// follows the weights rather than being picked; a card puts its own back on as
        /// it leaves the pack, face up.
        /// </summary>
        Texture2D PackBack()
        {
            CardArtSet heaviest = null;
            foreach (var set in artSets)
            {
                if (set == null || set.Back == null) continue;
                if (heaviest == null || set.packWeight > heaviest.packWeight) heaviest = set;
            }
            return heaviest != null ? heaviest.Back : null;
        }

        /// <summary>
        /// Rolls one card of one deck. Everything that mints a card goes through here,
        /// so a card bought off the shop's rack carries the same identity a pulled one
        /// does and can be sold back the same way.
        /// </summary>
        CardDraw RollCard(int setIndex, int minTier)
        {
            var set = catalog.Set(setIndex);
            // An identity of its own rather than a blank draw: the fields of a blank
            // one read as a perfectly good "first card of the first deck".
            if (set == null || set.FaceCount == 0) return new CardDraw { id = CardIdentity.None };

            int face = Random.Range(0, set.FaceCount);
            int tier = RollTier(minTier);
            return new CardDraw
            {
                id = new CardIdentity { set = setIndex, face = face, tier = tier },
                face = set.Faces[face],
                back = set.Back,
                material = tier < rarityMaterials.Count ? rarityMaterials[tier] : null,
            };
        }

        /// <summary>
        /// Which deck the next card comes off, by the art sets' pull weights - the same
        /// roll for a card in a pack and a card off the shop's rack, so the rare deck is
        /// as rare either way.
        /// </summary>
        int PickArtSet()
        {
            float total = 0f;
            foreach (var set in artSets)
                if (set != null) total += Mathf.Max(0f, set.packWeight);

            if (total <= 0f) return artSets.Count > 0 ? 0 : -1;

            float roll = Random.value * total;
            for (int i = 0; i < artSets.Count; i++)
            {
                if (artSets[i] == null) continue;
                roll -= Mathf.Max(0f, artSets[i].packWeight);
                if (roll <= 0f) return i;
            }
            return artSets.Count - 1;
        }

        int RollTier(int minTier)
        {
            int tiers = rarityMaterials.Count;
            if (tiers == 0) return 0;

            float total = 0f;
            for (int i = minTier; i < tiers; i++) total += Weight(i);
            if (total <= 0f) return minTier;

            float roll = Random.value * total;
            for (int i = minTier; i < tiers; i++)
            {
                roll -= Weight(i);
                if (roll <= 0f) return i;
            }
            return tiers - 1;
        }

        float Weight(int tier) => tier < rarityWeights.Count ? Mathf.Max(0f, rarityWeights[tier]) : 1f;

        void OnPackOpened()
        {
            // The rig follows the pack's state on its own, so the stack pulls back to
            // its resting place while the wrapper falls away.
            deck.Reveal();
        }

        /// <summary>
        /// Opens a pack without the ceremony: the wrapper is ripped in one go and every
        /// card is turned over and sent to the default pile, which is where a plain
        /// click on the stack would have put them one at a time.
        ///
        /// With nothing on the table it takes the next pack out of the drawer first, so
        /// one press is one pack from drawer to pile. With a pack half dealt it finishes
        /// that one rather than skipping to the next.
        /// </summary>
        void UnpackAll()
        {
            if (deck == null || board == null) return;

            var slot = board.Default;
            if (slot == null) return;

            // The album has to go: the pack and its stack are switched off behind it,
            // and a wrapper that cannot run its own Update never finishes falling.
            ShowPacks();
            ReturnHeldCard();

            if (!PackInPlay && !DealPack()) return;
            if (pack != null) pack.RipOpen();

            // RipOpen raises Opened, which reveals the stack - but a pack that was torn
            // by hand revealed it long ago, and one with no wrapper never will.
            if (!deck.Revealed) deck.Reveal();

            while (deck.Remaining > 0)
            {
                deck.RevealTop();
                var card = deck.Take();
                if (card == null) break;
                Deliver(slot, card, true);
            }
        }

        // -------------------------------------------------------------------
        // Shop
        // -------------------------------------------------------------------
        public int Coins => coins;

        /// <summary>
        /// Packs go into the drawer; cards are minted on the spot and flown onto the
        /// default pile, which is the same place a click on the stack sends them.
        /// Nothing is charged for until it is known that it can be delivered.
        /// </summary>
        public bool Buy(ShopOffer offer)
        {
            if (offer == null || offer.count <= 0 || offer.price > coins) return false;

            if (offer.goods == ShopGoods.BoosterPacks)
            {
                coins -= offer.price;
                packs += offer.count;
                RefreshTray();
                return true;
            }

            var slot = board != null ? board.Default : null;
            if (slot == null || cardPrefab == null || artSets.Count == 0) return false;

            coins -= offer.price;
            for (int i = 0; i < offer.count; i++)
            {
                var draw = RollCard(PickArtSet(), 0);
                if (!draw.id.IsValid) break;

                // Face up, and wearing its own back: a bought card has already been
                // seen - the shop is the one place in this scene where you know what
                // you are getting.
                var card = CardFactory.Create(cardPrefab, null, draw, false);
                if (card == null) break;

                card.transform.position = CounterSpawn(i, offer.count);
                Deliver(slot, card, true);
            }
            return true;
        }

        /// <summary>
        /// Where a bought card comes in from: off the right of the frame, so a handful
        /// of them reads as being passed over a counter rather than appearing on the
        /// pile. Staggered, or twenty cards fly in as one.
        /// </summary>
        Vector3 CounterSpawn(int index, int count)
        {
            if (cam == null) return Vector3.zero;

            Vector2 half = FrameHalfSize(dragDepth);
            float spread = Mathf.Min(count, 10) * 0.05f;
            return new Vector3(cam.transform.position.x + half.x + 0.6f,
                               cam.transform.position.y - half.y * 0.4f + index * 0.05f - spread * 0.5f,
                               dragDepth);
        }

        public int Progress(int adIndex) => adIndex >= 0 && adIndex < adProgress.Count ? adProgress[adIndex] : 0;

        /// <summary>
        /// Fills a wanted ad: the cards it asks for leave the piles and the albums for
        /// good, and a fresh ad takes its place on the board. Checked again here
        /// rather than trusting the button, since the collection can change under an
        /// open panel - a card being dragged out of a pile is a card that is not there.
        /// </summary>
        public bool Sell(int adIndex)
        {
            if (stock == null || adIndex < 0 || adIndex >= stock.Wanted.Count) return false;

            var ad = stock.Wanted[adIndex];
            if (ad == null || ad.Total <= 0) return false;

            CardCollection.Gather(board, albums, owned);
            if (CardCollection.Match(owned, ad, matched, catalog) < ad.Total) return false;

            CardCollection.Remove(matched);
            coins += ad.price;
            stock.Reroll(ad);
            RefreshProgress();
            return true;
        }

        /// <summary>
        /// How far along every ad on the board is, worked out once a frame while the
        /// shop is open rather than per row: OnGUI runs at least twice a frame and
        /// would otherwise walk the whole collection for each of them, twice.
        /// </summary>
        void RefreshProgress()
        {
            if (stock == null || shop == null || !shop.IsOpen) return;

            CardCollection.Gather(board, albums, owned);
            while (adProgress.Count < stock.Wanted.Count) adProgress.Add(0);
            for (int i = 0; i < stock.Wanted.Count; i++)
                adProgress[i] = CardCollection.Match(owned, stock.Wanted[i], matched, catalog);
        }

        // -------------------------------------------------------------------
        // Input
        // -------------------------------------------------------------------
        void HandlePointer()
        {
            if (cam == null) return;

            Vector2 pointer = CardInteractor.PointerPosition();
            bool pressed = CardInteractor.PointerPressed();

            // A panel drawn over the scene owns the pointer completely while it is
            // under it. IMGUI swallows the click that lands on one of its buttons,
            // but nothing tells the world about the one that lands beside it - so
            // the world is told here, and the cards are taken out of reach as well.
            bool onPanel = (shop != null && shop.Blocks(pointer)) ||
                           (dialogLink != null && dialogLink.Blocks(pointer));
            if (interactor != null) interactor.SetBlocked(onPanel);
            if (onPanel)
            {
                if (pack != null) pack.SetHovered(false);
                if (shelf != null) shelf.SetHovered(-1);
                if (tray != null) tray.SetHovered(false);

                // A tear that wandered onto the panel is let go rather than left
                // holding: the wrapper would sit half ripped and never reseal.
                if (grabbedPack)
                {
                    grabbedPack = false;
                    if (pack != null) pack.ReleaseTear();
                }
                overPack = false;

                // Whatever was in hand goes home rather than following a pointer that
                // has stopped talking to it.
                ReturnHeldCard();
                wasPressed = pressed;
                return;
            }

            bool down = pressed && !wasPressed;
            bool up = !pressed && wasPressed;
            Ray ray = cam.ScreenPointToRay(pointer);

            HandlePack(ray, down, pressed, up);
            HandleSorting(ray, pointer, down, pressed, up);

            wasPressed = pressed;
        }

        void HandlePack(Ray ray, bool down, bool pressed, bool up)
        {
            if (pack == null) return;

            overPack = false;
            if (pack.gameObject.activeSelf && pack.TryGetLocalPointer(ray, out Vector2 onPack))
            {
                bool over = Mathf.Abs(onPack.x) <= 1f && Mathf.Abs(onPack.y) <= 1f;
                overPack = over;
                pack.SetHovered(over || grabbedPack);
                pack.SetPointer(onPack);

                if (down && pack.CanGrab(onPack))
                {
                    grabbedPack = true;
                    pack.BeginTear(onPack);
                }
                else if (grabbedPack && pressed)
                {
                    pack.UpdateTear(onPack);
                }
            }
            else
            {
                pack.SetHovered(false);
            }

            if (up && grabbedPack)
            {
                grabbedPack = false;
                pack.ReleaseTear();
            }
        }

        void HandleSorting(Ray ray, Vector2 pointer, bool down, bool pressed, bool up)
        {
            if (deck == null || board == null) return;

            // The stack only answers once the wrapper is fully out of frame, so the
            // click that finishes the tear cannot also deal the first card.
            bool stackReady = pack == null || pack.Current == CardPackView.State.Gone;

            var open = Album;
            int side = open != null ? open.HitSide(ray) : 0;
            bool onPage = side != 0;

            int overIcon = shelf != null ? shelf.Find(ray) : -1;
            if (shelf != null) shelf.SetHovered(overIcon);

            bool overTray = tray != null && tray.Hits(ray);
            if (tray != null) tray.SetHovered(overTray);

            // A press anywhere on an album, on a book on the shelf or on the drawer
            // under it belongs to them, not to the card behind. Re-asserted every
            // frame because script execution order between this and CardInteractor is
            // not fixed, so cancelling once may be too early.
            if ((onPage || overIcon >= 0 || overTray) && pressed && interactor != null) interactor.CancelPress();

            if (down && !grabbedPack && !overPack && heldCard == null)
            {
                pressedSide = side;
                pressedIcon = overIcon;
                pressedTray = overTray;

                // A press on the side menu is the menu's, so nothing behind it gets to
                // claim the card it would otherwise pick up.
                if (overIcon < 0 && !overTray)
                {
                    if (view == View.Packs && stackReady && deck.CanDraw && OverDeck(ray))
                    {
                        pressedDeck = true;
                        pressPointer = pointer;
                    }
                    else
                    {
                        // Anywhere in a slot's catch area grabs whatever is on top of it,
                        // so a pile can be picked up without hitting the card exactly.
                        var under = FindSlot(ray);
                        if (under != null && under.Count > 0)
                        {
                            pressedSlot = under;
                            pressPointer = pointer;
                        }
                        else if (under == null && side == 0)
                        {
                            // Nothing under it at all: the bare table, which is one of
                            // the ways a pack goes back in the drawer.
                            pressedEmpty = true;
                            pressPointer = pointer;
                        }
                    }
                }
            }

            // A press that travels lifts the card out; one that stays put is a click.
            // Same threshold CardInteractor uses to give the gesture up, so a drag is
            // never read as a flip as well.
            if (pressed && heldCard == null && (pressedDeck || pressedSlot != null) &&
                (pointer - pressPointer).magnitude > dragThreshold)
            {
                // Only a card that has already been turned over leaves the pack - the
                // first press on a face down one is what reveals it.
                if (pressedDeck && deck.TopRevealed)
                {
                    heldFrom = null;
                    heldCard = deck.Take();
                    if (heldCard == null) pressedDeck = false;
                }
                else if (pressedSlot != null)
                {
                    heldFrom = pressedSlot;
                    heldCard = pressedSlot.TakeTop();
                    if (heldCard == null) { pressedSlot = null; heldFrom = null; }
                }
            }

            if (heldCard != null)
            {
                DragHeldCard(ray);
                HighlightDropTarget(FindDropTarget(ray));
            }

            if (!up) return;

            if (heldCard != null)
            {
                var target = FindDropTarget(ray);
                // Moving a card between slots says nothing new about what came out of
                // a pack, so none of these touch the readout.
                if (target != null) Deliver(target, heldCard, false);
                else if (heldFrom != null) heldFrom.Receive(heldCard);   // dropped on nothing, flies home
                else deck.Return(heldCard);

                heldCard = null;
                heldFrom = null;
                HighlightDropTarget(null);
            }
            else if (pressedIcon >= 0)
            {
                // Only counts if the pointer is still on the book it went down on.
                if (pressedIcon == overIcon) OpenAlbum(pressedIcon);
            }
            else if (pressedTray)
            {
                // Clicking the drawer while a whole pack is out is putting that one
                // back, not asking for another.
                if (overTray && !StowPack()) OpenNextPack();
            }
            else if (pressedDeck && deck.CanDraw)
            {
                // First click turns the card over where it lies, so it can be looked
                // at before it is filed away. The next one moves it.
                if (!deck.TopRevealed)
                {
                    deck.RevealTop();
                    lastCard = deck.TopIdentity;
                }
                else
                {
                    var slot = board.Default;
                    if (slot != null) Deliver(slot, deck.Take(), true);
                }
            }
            else if (pressedSide != 0 && open != null && open.IsOpen)
            {
                // Nothing was carried off, so the press was a plain click on a sheet:
                // the left page steps back through the album, the right page forward.
                open.Turn(pressedSide);
            }
            else if (pressedEmpty && (pointer - pressPointer).magnitude <= dragThreshold)
            {
                // A click on the bare table around a sealed pack puts it away again, so
                // changing your mind about a pack does not mean having to tear it open.
                StowPack();
            }
            // A click on a pile's top card is CardInteractor's - it flips it in place.

            ForgetPress();
        }

        /// <summary>Album pockets take priority - an open sheet sits in front of the row.</summary>
        CardSlot FindDropTarget(Ray ray)
        {
            var open = Album;
            if (open != null && open.IsVisible)
            {
                var pocket = open.FindDropTarget(ray);
                if (pocket != null) return pocket;
                // A press on the book but not in a free pocket must not fall through
                // to a pile hidden behind it. That holds while it is still swinging
                // open as well, when it has no pockets to offer at all.
                if (open.HitsPage(ray)) return null;
            }
            return board != null ? board.FindDropTarget(ray) : null;
        }

        CardSlot FindSlot(Ray ray)
        {
            var open = Album;
            if (open != null && open.IsVisible)
            {
                var pocket = open.FindSlot(ray);
                if (pocket != null) return pocket;
                if (open.HitsPage(ray)) return null;
            }
            return board != null ? board.Find(ray) : null;
        }

        void HighlightDropTarget(CardSlot target)
        {
            if (board != null) board.Highlight(target);

            var open = Album;
            if (open != null) open.Highlight(target);
        }

        /// <summary>
        /// Files a card into a slot. <paramref name="announce"/> is for cards that are
        /// new to the table - out of a pack or off the shop's rack - and is what the
        /// readout reports; shuffling a card between two piles is not news.
        /// </summary>
        void Deliver(CardSlot slot, CardView card, bool announce)
        {
            if (card == null) return;
            slot.Receive(card);
            if (announce) lastCard = card.Identity;
        }

        void DragHeldCard(Ray ray)
        {
            var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, dragDepth));
            if (!plane.Raycast(ray, out float enter)) return;

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float k = 1f - Mathf.Exp(-dragFollow * dt);
            heldCard.transform.position = Vector3.Lerp(heldCard.transform.position, ray.GetPoint(enter), k);
            // Lifted out of a shrunken pile it still carries that pile's size, so grow
            // it back to full while it is in hand - you are meant to be able to see it.
            heldCard.transform.localScale = Vector3.Lerp(heldCard.transform.localScale, Vector3.one, k);
        }

        /// <summary>
        /// Sends whatever is in hand home - to the slot it came out of, or back onto
        /// the pack - so nothing is left floating when the frame changes underneath it.
        /// </summary>
        void ReturnHeldCard()
        {
            if (heldCard != null)
            {
                if (heldFrom != null) heldFrom.Receive(heldCard);
                else if (deck != null) deck.Return(heldCard);

                heldCard = null;
                heldFrom = null;
                HighlightDropTarget(null);
            }

            ForgetPress();
        }

        /// <summary>Gives up on a press without acting on it, whatever it had latched onto.</summary>
        void ForgetPress()
        {
            pressedDeck = false;
            pressedSlot = null;
            pressedSide = 0;
            pressedIcon = -1;
            pressedTray = false;
            pressedEmpty = false;
        }

        bool OverDeck(Ray ray)
        {
            var plane = new Plane(deck.transform.forward, deck.transform.position);
            if (!plane.Raycast(ray, out float enter)) return false;

            Vector3 local = deck.transform.InverseTransformPoint(ray.GetPoint(enter));
            Vector2 half = deck.ClickSize;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y;
        }

#if ENABLE_INPUT_SYSTEM
        static bool KeyPressed(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }
#else
        enum Key { Space, R, Q, C, H, B, Escape }
        static bool KeyPressed(Key key)
        {
            switch (key)
            {
                case Key.Space: return Input.GetKeyDown(KeyCode.Space);
                case Key.R: return Input.GetKeyDown(KeyCode.R);
                case Key.Q: return Input.GetKeyDown(KeyCode.Q);
                case Key.C: return Input.GetKeyDown(KeyCode.C);
                case Key.H: return Input.GetKeyDown(KeyCode.H);
                case Key.B: return Input.GetKeyDown(KeyCode.B);
                case Key.Escape: return Input.GetKeyDown(KeyCode.Escape);
            }
            return false;
        }
#endif

        // -------------------------------------------------------------------
        // HUD
        // -------------------------------------------------------------------
        void OnGUI()
        {
            UiSkin.Ensure();

            // The labels over the table keep the plain white face on purpose: they sit
            // on the wood, not on a panel, and the skin's ink is meant for parchment.
            labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            titleStyle = UiSkin.Title;

            DrawSlotLabels();
            DrawHeldLabel();
            DrawPackCount();
            if (!showHud) return;

            // No album buttons here: the shelf of books down the left is the menu,
            // and it stays on screen when the HUD is hidden - as do the shop's own
            // tab in the opposite corner and the dialog button under it.
            GUILayout.BeginArea(new Rect(UiSkin.Px(12f), UiSkin.Px(12f), UiSkin.Px(320f), UiSkin.Px(240f)),
                                UiSkin.Panel);
            GUILayout.Label("Cozy TGC - Pack Opening", titleStyle);
            GUILayout.Label(Hint(), UiSkin.Detail);
            GUILayout.Space(UiSkin.Px(4f));
            // No coins here: the purse is on screen in the top right whatever this panel
            // is doing, and two live copies of one number is one too many.
            GUILayout.Label($"Packs: {packs}   Pack #{packsOpened}", UiSkin.Label);
            GUILayout.Label($"In pack: {(deck != null ? deck.Remaining : 0)}" +
                            $"   Sorted: {(board != null ? board.TotalCards : 0)}", UiSkin.Label);
            GUILayout.Label(AlbumLine(), UiSkin.Label);
            if (lastCard.IsValid) GUILayout.Label($"Last card: {catalog.FullNameOf(lastCard)}", UiSkin.Label);
            GUILayout.Label("Space / R: pack   Q: unpack it all   B: shop\nC: clear slots   H: hide",
                            UiSkin.Detail);
            GUILayout.EndArea();

            DrawTearMeter();
        }

        string AlbumLine()
        {
            var open = Album;
            if (open != null)
                return $"{open.Title}: spread {open.SpreadNumber}/{open.SpreadCount}, {open.CardCount} filed";

            int filed = 0;
            foreach (var album in albums) if (album != null) filed += album.CardCount;
            return $"Albums: {filed} filed";
        }

        string Hint()
        {
            if (view == View.Album)
            {
                var open = Album;
                return open != null && open.CardCount == 0
                    ? "Drag cards up from the piles into the pages.\nClick a page to leaf through, the book to close."
                    : "Drag cards in or out of the pages.\nClick a page to leaf through, the book to close.";
            }
            if (pack == null || deck == null) return string.Empty;
            if (pack.Current == CardPackView.State.Sealed || pack.Current == CardPackView.State.Tearing)
                return "Drag across the crimped top of the pack to rip it open.\n" +
                       "Click the table around it to put it back in the drawer.";
            if (heldCard != null || deck.TopRevealed)
                return "Click again to file the card into the default\nslot, or drag it onto any slot you like.";
            if (deck.CanDraw)
                return "Click the stack to turn the next card over.";
            if (packs > 0)
                return "Click the drawer under the books for the\nnext pack, or a book to open an album.";
            return "Out of packs - the shop sells them, and\nbuys the cards collectors are asking for.";
        }

        void DrawTearMeter()
        {
            if (pack == null || !pack.IsTearing || cam == null) return;

            Vector3 world = pack.transform.position + Vector3.up * (pack.Size.y * 0.5f + 0.12f);
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0f) return;

            float width = UiSkin.Px(120f);
            var rect = new Rect(screen.x - width * 0.5f, Screen.height - screen.y - UiSkin.Px(8f),
                                width, UiSkin.Px(6f));

            // Flat fills rather than the skin's frame: the meter is six pixels tall and
            // a nine-slice with a four pixel border has nothing left to put in the middle.
            Fill(rect, UiSkin.Ink);
            Fill(new Rect(rect.x, rect.y, rect.width * pack.TearProgress, rect.height), UiSkin.Parchment);
        }

        static void Fill(Rect rect, Color colour)
        {
            Color was = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = was;
        }

        void DrawSlotLabels()
        {
            if (cam == null || board == null) return;

            foreach (var slot in board.Slots)
            {
                if (slot == null) continue;

                // Offset tracks the row's size, or the label drifts away from a shrunken pile.
                Vector3 screen = cam.WorldToScreenPoint(
                    slot.transform.position + Vector3.down * (0.63f * rowScale));
                if (screen.z <= 0f) continue;

                string text = slot.Count > 0 ? $"{slot.Label}  {slot.Count}" : slot.Label;
                if (slot.IsDefault) text += "  *";

                GUI.Label(new Rect(screen.x - 70f, Screen.height - screen.y - 10f, 140f, 20f), text, labelStyle);
            }
        }

        void DrawHeldLabel()
        {
            if (cam == null || heldCard == null || !heldCard.Identity.IsValid) return;

            Vector3 screen = cam.WorldToScreenPoint(heldCard.transform.position + Vector3.down * 0.7f);
            if (screen.z <= 0f) return;

            GUI.Label(new Rect(screen.x - 90f, Screen.height - screen.y - 10f, 180f, 20f),
                      catalog.FullNameOf(heldCard.Identity), labelStyle);
        }

        /// <summary>How many packs are in the drawer, printed under it.</summary>
        void DrawPackCount()
        {
            if (cam == null || tray == null || !tray.isActiveAndEnabled) return;

            Vector3 screen = cam.WorldToScreenPoint(tray.LabelPosition);
            if (screen.z <= 0f) return;

            string text = packs > 0 ? $"{packs} pack{(packs == 1 ? "" : "s")}" : "empty";
            GUI.Label(new Rect(screen.x - 60f, Screen.height - screen.y - 10f, 120f, 20f), text, labelStyle);
        }

#if UNITY_EDITOR
        public void EditorBind(Camera camera, CardInteractor cardInteractor, CardPackView packView,
                               CardPackDeck packDeck, CardSlotBoard slotBoard,
                               List<CardAlbum> cardAlbums, AlbumShelf albumShelf, float albumPlane,
                               Transform rig, Vector3 sealedOffset,
                               List<CardArtSet> sets, List<Material> materials, List<string> names)
        {
            cam = camera;
            interactor = cardInteractor;
            pack = packView;
            deck = packDeck;
            board = slotBoard;
            albums = cardAlbums;
            shelf = albumShelf;
            albumDepth = albumPlane;
            packRig = rig;
            sealedRigOffset = sealedOffset;
            artSets = sets;
            rarityMaterials = materials;
            rarityNames = names;
        }

        /// <summary>
        /// The shop half, kept separate only because the list above is already as long
        /// as a positional call can usefully get.
        /// </summary>
        public void EditorBindShop(GameObject prefab, PackTray packTray, ShopView shopPanel,
                                   ShopCatalog catalogAsset)
        {
            cardPrefab = prefab;
            tray = packTray;
            shop = shopPanel;
            shopCatalog = catalogAsset;
        }

        public void EditorBindDialogLink(SceneLinkButton link) => dialogLink = link;
#endif
    }
}
