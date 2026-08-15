using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CozyTGC.EditorTools
{
    /// <summary>
    /// Makes one <see cref="CardData"/> asset per card out of the artwork folders, and
    /// one <see cref="CardDeckData"/> per deck to hold them in face order.
    ///
    /// Authored content, so what is already there is never overwritten: a card keeps its
    /// price, its weight and its name however often this is run - the same deal
    /// Assets/Shop/ShopCatalog.asset and Assets/Dialogs get, and the reason the cards
    /// live outside the generated folders. What is re-synced on every run is the
    /// structure: a card asset for artwork that has none yet, the deck's back, and the
    /// deck's list put back in the order <see cref="CardArtLibrary"/> sorts the folder -
    /// which is the order <see cref="CardIdentity.face"/> counts in, and therefore the
    /// order the shop's written ads were authored against.
    ///
    /// An existing card is recognised by the face texture on it rather than by its file
    /// name, so a card can be renamed in the project window without this making a second
    /// one of it next time.
    /// </summary>
    public static class CardAssetBuilder
    {
        const string Root = "Assets/Cards";

        /// <summary>
        /// What each deck is built from. The order is what <see cref="CardDeck"/> names
        /// and what <see cref="CardIdentity.set"/> holds, so a deck is added to the end
        /// of this and to the end of that enum, never in the middle.
        ///
        /// Only used the first time a deck asset is made. After that the numbers on the
        /// asset are the ones that count, so a pack weight tuned in the inspector is not
        /// undone by the next build.
        /// </summary>
        class DeckSpec
        {
            public string assetName;
            public string resourceFolder;
            public string displayName;
            public float packWeight = 1f;
            public int suitSize;
            public string[] suitNames;
            public string[] rankNames;
            /// <summary>One per face, for a deck whose cards are named singles. Null for
            /// a suited deck, which names its cards from its suits and ranks instead.</summary>
            public string[] cardNames;
            /// <summary>What a card of this face is worth, in coins.</summary>
            public Func<int, int> price = face => 30;
            /// <summary>How often it is dealt, against the other cards of its own deck.</summary>
            public Func<int, float> weight = face => 1f;
        }

        /// <summary>Ordering the shop's wanted ads read the artwork by.
        /// The tarot folder is 00-21, the majors in Rider-Waite order, so 08 is
        /// Strength and 11 is Justice - and 20 is the Judgement the ads ask for.</summary>
        static readonly string[] TarotNames =
        {
            "The Fool", "The Magician", "The High Priestess", "The Empress", "The Emperor",
            "The Hierophant", "The Lovers", "The Chariot", "Strength", "The Hermit",
            "Wheel of Fortune", "Justice", "The Hanged Man", "Death", "Temperance",
            "The Devil", "The Tower", "The Star", "The Moon", "The Sun",
            "Judgement", "The World",
        };

        /// <summary>Prices along the majors: the Fool at the bottom, four coins more with
        /// every card, the World dearest at 100 + 21 * 4.</summary>
        const int TarotFirstPrice = 100;
        const int TarotPriceStep = 4;

        /// <summary>And they thin out the same way, evenly from the Fool to the World.</summary>
        const float TarotFirstWeight = 0.1f;
        const float TarotLastWeight = 0.01f;

        /// <summary>The Spanish folder is 1-40 sorted numerically: four suits of ten,
        /// in this order, each running 1-7 and then the three court cards. Named in
        /// English - oros are coins, copas cups, espadas swords, bastos wands, and the
        /// sota, caballo and rey are the jack, the knight and the king.</summary>
        static readonly string[] SpanishSuits = { "Coins", "Cups", "Swords", "Wands" };
        static readonly string[] SpanishRanks =
            { "Ace", "2", "3", "4", "5", "6", "7", "Jack", "Knight", "King" };
        const int SpanishSuitSize = 10;

        /// <summary>What a plain numbered card of each suit is worth, in the suits' own
        /// order: coins are the dear suit and wands the cheap one.</summary>
        static readonly int[] SpanishSuitValues = { 25, 20, 15, 10 };
        /// <summary>Added per step up the four cards above the numbers - jack, knight,
        /// king, ace - so the ace of coins is the dearest card in the deck.</summary>
        const int SpanishCourtStep = 3;

        // A tenth of the cards in a pack, which at five cards a pack is one tarot card
        // every second pack.
        const float TarotPackWeight = 1f;
        const float SpanishPackWeight = 9f;

        static readonly DeckSpec[] Specs =
        {
            new DeckSpec
            {
                assetName = "Tarot",
                resourceFolder = CardArtLibrary.Root + "tarot_free - monochrome",
                displayName = "tarot",
                packWeight = TarotPackWeight,
                cardNames = TarotNames,
                price = face => TarotFirstPrice + TarotPriceStep * face,
                weight = TarotWeight,
            },
            new DeckSpec
            {
                assetName = "Spanish deck",
                resourceFolder = CardArtLibrary.Root + "spanish deck",
                displayName = "Spanish deck",
                packWeight = SpanishPackWeight,
                suitSize = SpanishSuitSize,
                suitNames = SpanishSuits,
                rankNames = SpanishRanks,
                price = SpanishPrice,
                // Nothing in the deck is rarer than anything else in it - which suit and
                // rank a card is only decides what it is worth, not how hard it is to get.
                weight = face => 1f,
            },
        };

        /// <summary>
        /// Suit first, then rank: a numbered card is worth its suit's base, and the four
        /// cards above the numbers climb by <see cref="SpanishCourtStep"/> a step - jack,
        /// knight, king, and then the ace, which the folder files first but which tops
        /// the deck.
        /// </summary>
        static int SpanishPrice(int face)
        {
            int suit = face / SpanishSuitSize;
            int rank = face % SpanishSuitSize;

            int step = rank == 0 ? 4              // the ace, four steps above the numbers
                     : rank >= 7 ? rank - 6       // sota, caballo, rey at one, two, three
                     : 0;                         // 2 to 7, flat at the suit's base
            int suitValue = suit >= 0 && suit < SpanishSuitValues.Length ? SpanishSuitValues[suit] : 0;
            return suitValue + step * SpanishCourtStep;
        }

        /// <summary>
        /// Evenly from the Fool down to the World, rounded to four places so the assets
        /// read as numbers somebody chose rather than as float noise.
        /// </summary>
        static float TarotWeight(int face)
        {
            int last = Mathf.Max(1, TarotNames.Length - 1);
            float t = Mathf.Clamp01(face / (float)last);
            return Mathf.Round(Mathf.Lerp(TarotFirstWeight, TarotLastWeight, t) * 10000f) / 10000f;
        }

        [MenuItem("Tools/Cozy TGC/Build Card Assets", false, 2)]
        public static void BuildCardAssets()
        {
            var decks = LoadOrCreateDecks();

            int cards = 0;
            foreach (var deck in decks) cards += deck.FaceCount;
            Debug.Log($"[Cozy TGC] {decks.Count} decks, {cards} cards under {Root}. " +
                      "Prices and pull weights are on the cards themselves.");
        }

        /// <summary>
        /// Puts every card back to the scheme above - its name, its price and its pull
        /// weight - and renames the asset files to match. The one thing here that does
        /// overwrite: prices tuned by hand since the cards were made are lost, which is
        /// the point of it, and why it asks first.
        ///
        /// It is how a change to the scheme reaches cards that already exist, since
        /// building them again deliberately leaves them alone.
        /// </summary>
        [MenuItem("Tools/Cozy TGC/Reset Card Prices and Names", false, 3)]
        public static void ResetCardValues()
        {
            if (!Application.isBatchMode &&
                !EditorUtility.DisplayDialog(
                    "Reset card prices and names?",
                    "Every card under Assets/Cards goes back to the scheme in CardAssetBuilder - " +
                    "name, price and pull weight - whatever has been typed over them since. " +
                    "The decks' suit and rank names are put back with them.",
                    "Reset", "Cancel")) return;

            int touched = 0;
            foreach (var spec in Specs)
            {
                var deck = AssetDatabase.LoadAssetAtPath<CardDeckData>($"{Root}/{spec.assetName}.asset");
                if (deck == null) continue;

                // The names come off the deck's own scheme, so that has to go back first
                // or forty Spanish cards are renamed from the arrays they already had.
                deck.displayName = spec.displayName;
                deck.suitSize = spec.suitSize;
                deck.suitNames = spec.suitNames;
                deck.rankNames = spec.rankNames;
                EditorUtility.SetDirty(deck);

                for (int face = 0; face < deck.FaceCount; face++)
                {
                    var card = deck.CardAt(face);
                    if (card == null) continue;

                    ApplyScheme(spec, deck, face, card);
                    EditorUtility.SetDirty(card);

                    string path = AssetDatabase.GetAssetPath(card);
                    string wanted = FileName(card.displayName);
                    if (!string.IsNullOrEmpty(path) && Path.GetFileNameWithoutExtension(path) != wanted)
                        AssetDatabase.RenameAsset(path, wanted);

                    touched++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Cozy TGC] {touched} cards put back to the scheme in CardAssetBuilder.");
        }

        /// <summary>
        /// The decks, made if they are not there yet. This is what the pack scene is
        /// built against, so building that scene stocks a fresh clone on its own.
        /// </summary>
        public static List<CardDeckData> LoadOrCreateDecks()
        {
            EnsureFolder(Root);

            var decks = new List<CardDeckData>();
            foreach (var spec in Specs)
            {
                var deck = LoadOrCreateDeck(spec);
                if (deck != null) decks.Add(deck);
            }

            AssetDatabase.SaveAssets();
            return decks;
        }

        static CardDeckData LoadOrCreateDeck(DeckSpec spec)
        {
            var faces = CardArtLibrary.Load(spec.resourceFolder, "back", out Texture2D back);
            if (faces.Length == 0)
            {
                Debug.LogError($"[Cozy TGC] No artwork in Resources/{spec.resourceFolder} - " +
                               $"nothing to build the {spec.assetName} deck out of.");
                return null;
            }

            string deckPath = $"{Root}/{spec.assetName}.asset";
            var deck = AssetDatabase.LoadAssetAtPath<CardDeckData>(deckPath);
            if (deck == null)
            {
                deck = ScriptableObject.CreateInstance<CardDeckData>();
                deck.displayName = spec.displayName;
                deck.packWeight = spec.packWeight;
                deck.suitSize = spec.suitSize;
                deck.suitNames = spec.suitNames;
                deck.rankNames = spec.rankNames;
                AssetDatabase.CreateAsset(deck, deckPath);
                Debug.Log($"[Cozy TGC] Created {deckPath} - the deck's own numbers are edited there.");
            }

            // The back is which texture, not a number somebody tuned, so it is re-pointed
            // every run: renaming the file or reimporting the folder would otherwise
            // leave the whole deck backless.
            deck.back = back;

            string folder = $"{Root}/{spec.assetName}";
            EnsureFolder(folder);

            var existing = ExistingCards(folder);
            var ordered = new List<CardData>(faces.Length);
            int made = 0;

            for (int face = 0; face < faces.Length; face++)
            {
                var card = Find(existing, faces[face]);
                if (card == null)
                {
                    card = CreateCard(spec, deck, folder, face, faces[face]);
                    made++;
                }
                ordered.Add(card);
            }

            deck.cards = ordered;
            EditorUtility.SetDirty(deck);

            if (made > 0) Debug.Log($"[Cozy TGC] {made} new card assets in {folder}.");

            // A card whose artwork has gone is left where it is rather than deleted -
            // it may be holding a price worth keeping - but it is no longer in any pack,
            // and a card that quietly stops being dealt is worth saying out loud.
            int orphans = existing.Count - (ordered.Count - made);
            if (orphans > 0)
                Debug.LogWarning($"[Cozy TGC] {orphans} card assets in {folder} have no artwork in " +
                                 $"Resources/{spec.resourceFolder} and are not in the deck.");

            return deck;
        }

        /// <summary>
        /// What a new card is called: the deck's list of names where it has one, and the
        /// suit and rank scheme where it does not - which is what saves forty Spanish
        /// cards from being typed out. The scheme is read off the deck rather than the
        /// spec, so renaming a suit on the asset renames the cards made after it.
        /// </summary>
        static string NameFor(DeckSpec spec, CardDeckData deck, int face)
        {
            if (spec.cardNames != null && face < spec.cardNames.Length) return spec.cardNames[face];
            return deck.SchemeName(face);
        }

        static CardData CreateCard(DeckSpec spec, CardDeckData deck, string folder, int face, Texture2D art)
        {
            var card = ScriptableObject.CreateInstance<CardData>();
            card.face = art;
            ApplyScheme(spec, deck, face, card);

            // Unique rather than exact: two cards of a deck are allowed to share a name,
            // and overwriting the first one's asset would take its price with it.
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{FileName(card.displayName)}.asset");
            AssetDatabase.CreateAsset(card, path);
            return card;
        }

        /// <summary>What the spec says a card is called and what it is worth. Written when
        /// the card is made, and again by the reset below - never in between.</summary>
        static void ApplyScheme(DeckSpec spec, CardDeckData deck, int face, CardData card)
        {
            card.displayName = NameFor(spec, deck, face);
            card.price = spec.price != null ? spec.price(face) : 0;
            card.weight = spec.weight != null ? spec.weight(face) : 1f;
        }

        static List<CardData> ExistingCards(string folder)
        {
            var found = new List<CardData>();
            foreach (string guid in AssetDatabase.FindAssets("t:CardData", new[] { folder }))
            {
                var card = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(guid));
                if (card != null) found.Add(card);
            }
            return found;
        }

        static CardData Find(List<CardData> cards, Texture2D face)
        {
            foreach (var card in cards)
                if (card != null && card.face == face) return card;
            return null;
        }

        /// <summary>A card name as a file name - "10 of Oros" is fine, "1/2" is not.</summary>
        static string FileName(string cardName)
        {
            if (string.IsNullOrEmpty(cardName)) return "Card";

            foreach (char bad in Path.GetInvalidFileNameChars())
                cardName = cardName.Replace(bad, ' ');
            return cardName.Trim();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
