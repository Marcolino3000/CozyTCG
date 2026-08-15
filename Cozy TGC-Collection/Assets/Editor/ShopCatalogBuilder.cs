using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CozyTGC.EditorTools
{
    /// <summary>
    /// The shelves the shop ships with, and the asset they are written into.
    ///
    /// Authored content: this makes <c>Assets/Shop/ShopCatalog.asset</c> when it is
    /// missing and never touches it again, exactly as <see cref="CardAssetBuilder"/>
    /// treats the cards. Putting the shipped shelves back is a menu item of its own, so
    /// that overwriting a shop somebody has written takes asking for.
    ///
    /// They live on the editor side because a row now names its cards - a
    /// <see cref="CardData"/> asset dropped into the offer or the want - and nothing at
    /// run time can reach an asset that is not in Resources. That is the whole reason
    /// <c>ShopCatalog.FillWithDefaults</c> is gone: a shelf written in face numbers was
    /// the only kind a runtime method could write.
    ///
    /// Between them these rows cover every shape the shop can be asked for, so writing a
    /// new one is a matter of copying the nearest.
    /// </summary>
    public static class ShopCatalogBuilder
    {
        public const string CatalogPath = "Assets/Shop/ShopCatalog.asset";

        [MenuItem("Tools/Cozy TGC/Reset Shop Catalog", false, 4)]
        public static void ResetCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath);
            if (catalog == null) { LoadOrCreate(); return; }

            if (!Application.isBatchMode &&
                !EditorUtility.DisplayDialog(
                    "Reset the shop?",
                    "Both shelves of " + CatalogPath + " go back to the ones the project " +
                    "ships with. Rows written since are lost.",
                    "Reset", "Cancel")) return;

            Fill(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Cozy TGC] {CatalogPath} put back to the shipped shelves.");
        }

        /// <summary>
        /// The catalog the pack scene is built against, stocked if it had to be made.
        /// </summary>
        public static ShopCatalog LoadOrCreate()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath);
            if (catalog != null) return catalog;

            EnsureFolder(Path.GetDirectoryName(CatalogPath)?.Replace('\\', '/'));
            catalog = ScriptableObject.CreateInstance<ShopCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            Fill(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Cozy TGC] Created {CatalogPath} - edit the shop's shelves there.");
            return catalog;
        }

        static void Fill(ShopCatalog catalog)
        {
            // The cards the rows name. Made if they are not there yet, so a fresh clone
            // gets a shop that is actually stocked rather than rows pointing at nothing.
            var decks = CardAssetBuilder.LoadOrCreateDecks();

            catalog.EditorSetShelves(BuildOffers(decks), BuildAds(decks));
            EditorUtility.SetDirty(catalog);
        }

        // -------------------------------------------------------------------
        // The buy shelf, cheapest first within each kind. Nothing here is worked out
        // from anything else - copy a line and set the number you want.
        // -------------------------------------------------------------------
        static List<ShopOffer> BuildOffers(IList<CardDeckData> decks)
        {
            var offers = new List<ShopOffer>
            {
                NewOffer("Booster pack", 100, ShopGoods.BoosterPacks, 1,
                         "A sealed pack. Five cards, and the last one is never common."),
                NewOffer("Booster packs x3", 270, ShopGoods.BoosterPacks, 3,
                         "Three sealed packs, a tenth off the single price."),
                NewOffer("Half a box", 440, ShopGoods.BoosterPacks, 5,
                         "Five packs. An afternoon's opening, at a fair discount."),
                NewOffer("A box of ten", 850, ShopGoods.BoosterPacks, 10,
                         "Ten packs at once. The cheapest wrappers in the shop."),
                NewOffer("Random card", 30, ShopGoods.RandomCards, 1,
                         "One loose card off the rack. Deck and finish unseen."),
                NewOffer("Random cards x5", 130, ShopGoods.RandomCards, 5,
                         "Five loose cards, dealt straight onto the default pile."),
                NewOffer("Random cards x10", 250, ShopGoods.RandomCards, 10,
                         "Ten off the rack. Filling a suit the slow, certain way."),
                NewOffer("A handful", 480, ShopGoods.RandomCards, 20,
                         "Twenty in one go. Good odds on something that glitters."),
            };

            // Two rows selling one card by name, which is what the card field is for.
            // Both name the print as well as the card: a shelf sells a copy, not a
            // chance at one, so the finish is part of what is being paid for - the plain
            // Fool at four times what he is worth, the shiny one at nearer ten.
            var fool = NewOffer("The Fool", 400, ShopGoods.NamedCard, 1,
                                "The Fool himself, no rummaging. The plain print.");
            fool.card = Card(decks, CardDeck.Tarot, 0);
            fool.finish = CardFinish.Common;
            offers.Add(fool);

            var shinyFool = NewOffer("The Fool, in shiny", 950, ShopGoods.NamedCard, 1,
                                     "The same card with the shine on it. Kept behind the counter.");
            shinyFool.card = Card(decks, CardDeck.Tarot, 0);
            shinyFool.finish = CardFinish.Shiny;
            offers.Add(shinyFool);

            return offers;
        }

        // -------------------------------------------------------------------
        // The board of wanted ads. Each of these is a different shape of want.
        // -------------------------------------------------------------------
        static List<WantedAd> BuildAds(IList<CardDeckData> decks)
        {
            var ads = new List<WantedAd>();

            // One named card, in one finish. The tightest an ad gets - and the finish is
            // exact, so a galaxy Judgement will not fill this either.
            var judgement = NewAd("Holo Judgement", 750,
                                  "Wanted: the Judgement, and it has to be the holo one.");
            judgement.wants.Add(new CardWant
            {
                card = Card(decks, CardDeck.Tarot, 20),
                finish = CardFinish.Holo,
            });
            ads.Add(judgement);

            // One named card, any finish.
            var tower = NewAd("The Tower", 150,
                              "Wanted: the Tower from the tarot. Any finish, it is for a friend.");
            tower.wants.Add(new CardWant { card = Card(decks, CardDeck.Tarot, 16) });
            ads.Add(tower);

            // Same card twice. count on a named card means copies of it, which is exactly
            // what you do not want for a set - see the rank ads below.
            var fools = NewAd("Two of the Fool", 260,
                              "Wanted: two Fools. One to keep, one to trade on.");
            fools.wants.Add(new CardWant { card = Card(decks, CardDeck.Tarot, 0), count = 2 });
            ads.Add(fools);

            // Two named cards, as two separate lines. Ads can ask for a list.
            var luminaries = NewAd("The Sun and the Moon", 340,
                                   "Wanted: the pair of them, and they must be the pair.");
            luminaries.wants.Add(new CardWant { card = Card(decks, CardDeck.Tarot, 19) });
            luminaries.wants.Add(new CardWant { card = Card(decks, CardDeck.Tarot, 18) });
            ads.Add(luminaries);

            // A named card of the Spanish deck, whose faces run suit by suit in tens - so
            // the king (rank index 9) of wands (suit 3) is 39.
            var king = NewAd("The King of Wands", 180,
                             "Wanted: the king of clubs, Spanish style. Any finish.");
            king.wants.Add(new CardWant { card = Card(decks, CardDeck.SpanishDeck, 39) });
            ads.Add(king);

            // One rank across every suit - four different cards, one line each.
            ads.Add(RankAd(decks, "The 4 of every suit", 600, 3,
                           "Wanted: the 4 from all four suits of the Spanish deck. Any finish."));
            ads.Add(RankAd(decks, "The King of every suit", 700, 9,
                           "Wanted: all four kings. Coins, cups, swords, wands."));

            // A whole suit: ten different cards, so ten lines.
            ads.Add(SuitAd(decks, "Complete set: Cups", 1200, 1,
                           "Wanted: every one of the ten cups. Pays for the lot."));
            ads.Add(SuitAd(decks, "Complete set: Coins", 1200, 0,
                           "Wanted: all ten coins, ace through king. Pays for the lot."));

            // By suit rather than by card: any of them, as long as it is shiny.
            var swords = NewAd("Three shiny Swords", 420,
                               "Wanted: three swords in shiny. Which three is your business.");
            swords.wants.Add(new CardWant
            {
                deck = CardDeck.SpanishDeck,
                suit = 2,
                finish = CardFinish.Shiny,
                count = 3,
            });
            ads.Add(swords);

            // By deck alone.
            var majors = NewAd("Three tarot cards", 200,
                               "Wanted: any three of the majors. A reading, not a collection.");
            majors.wants.Add(new CardWant { deck = CardDeck.Tarot, count = 3 });
            ads.Add(majors);

            // By finish alone, across everything.
            var holo = NewAd("Three cards in holo", 400,
                             "Wanted: any three cards in holo. Deck does not matter.");
            holo.wants.Add(new CardWant { finish = CardFinish.Holo, count = 3 });
            ads.Add(holo);

            var shiny = NewAd("Five cards in shiny", 300,
                              "Wanted: any five cards with that bit of shine on them.");
            shiny.wants.Add(new CardWant { finish = CardFinish.Shiny, count = 5 });
            ads.Add(shiny);

            // Everything left alone. The loosest an ad gets, and fillable from the first
            // pack, so the sell shelf is never dead on arrival.
            var bulk = NewAd("Eight cards, any at all", 240,
                             "Wanted: eight cards. No, really - any eight. Clearing a gap in a folder.");
            bulk.wants.Add(new CardWant { count = 8 });
            ads.Add(bulk);

            return ads;
        }

        /// <summary>One rank of the Spanish deck, across all four of its suits.</summary>
        static WantedAd RankAd(IList<CardDeckData> decks, string title, int price, int rank, string detail)
        {
            var ad = NewAd(title, price, detail);
            for (int suit = 0; suit < 4; suit++)
                ad.wants.Add(new CardWant { card = Card(decks, CardDeck.SpanishDeck, suit * 10 + rank) });
            return ad;
        }

        /// <summary>Every card of one Spanish suit - ten separate cards, not ten of one.</summary>
        static WantedAd SuitAd(IList<CardDeckData> decks, string title, int price, int suit, string detail)
        {
            var ad = NewAd(title, price, detail);
            for (int rank = 0; rank < 10; rank++)
                ad.wants.Add(new CardWant { card = Card(decks, CardDeck.SpanishDeck, suit * 10 + rank) });
            return ad;
        }

        /// <summary>
        /// The card asset at one face of one deck. Face numbers are used here, where the
        /// deck order is defined, and nowhere else - what lands on the shelf is the card
        /// itself, which cannot come loose from the deck the way an index can.
        /// </summary>
        static CardData Card(IList<CardDeckData> decks, CardDeck deck, int face)
        {
            int index = (int)deck;
            var data = index >= 0 && index < decks.Count ? decks[index] : null;
            var card = data != null ? data.CardAt(face) : null;
            if (card == null)
                Debug.LogWarning($"[Cozy TGC] No card at face {face} of the {deck} deck - " +
                                 "the shop row asking for it will be left empty.");
            return card;
        }

        static ShopOffer NewOffer(string title, int price, ShopGoods goods, int count, string detail)
            => new ShopOffer { title = title, price = price, goods = goods, count = count, detail = detail };

        static WantedAd NewAd(string title, int price, string detail)
            => new WantedAd { title = title, price = price, detail = detail };

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
