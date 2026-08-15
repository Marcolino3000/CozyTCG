using System.Collections.Generic;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// What the shop sells and what its collectors are asking for - authored content,
    /// edited in the inspector rather than in code.
    ///
    /// This is why it is an asset and not a list on the controller: the pack opening
    /// scene is generated, so anything typed into it is thrown away by the next
    /// rebuild. `CardPackBuilder` creates this asset **only when it is missing** and
    /// never touches it again, exactly as `DialogSceneBuilder` treats the dialog tree.
    /// Right-click > Reset in the inspector puts the shipped shelves back.
    ///
    /// The buy shelf is used in order, top to bottom. The ads are a pool rather than a
    /// list: <see cref="AdsOnBoard"/> of them are up at a time, and filling one brings
    /// up another that is not already there - so writing more than fit gives the board
    /// something to rotate through.
    /// </summary>
    [CreateAssetMenu(fileName = "ShopCatalog", menuName = "Cozy TGC/Shop Catalog", order = 0)]
    public class ShopCatalog : ScriptableObject
    {
        [Header("Buy")]
        [Tooltip("One row per entry, shown in this order. Prices are in coins, flat - " +
                 "nothing is worked out from anything else.")]
        [SerializeField] List<ShopOffer> offers = new List<ShopOffer>();

        [Header("Sell")]
        [Tooltip("The pool of wanted ads. More than fit on the board is the point: the " +
                 "rest come up as the ones on it are filled.")]
        [SerializeField] List<WantedAd> ads = new List<WantedAd>();
        [Tooltip("How many ads are up at once.")]
        [SerializeField] int adsOnBoard = 6;
        [Tooltip("Once every written ad has been used, keep the board full with ads " +
                 "made up from the decks that are loaded. Off, and the board runs down " +
                 "to only what is written here.")]
        [SerializeField] bool topUpWithRolledAds = true;

        public IReadOnlyList<ShopOffer> Offers => offers;
        public IReadOnlyList<WantedAd> Ads => ads;
        public int AdsOnBoard => Mathf.Max(1, adsOnBoard);
        public bool TopUpWithRolledAds => topUpWithRolledAds;

        void Reset() => FillWithDefaults();

        /// <summary>
        /// The shelves the project ships with. Also what the builder writes into a
        /// fresh asset, so the scene has a stocked shop the first time it is built.
        ///
        /// The deck and face numbers are the ones wired up in
        /// `CardPackBuilder.BuildArtSets`: set 0 is the tarot, whose faces are the
        /// files 00-21, and set 1 is the Spanish deck, whose 40 faces run suit by suit
        /// in tens.
        /// </summary>
        public void FillWithDefaults()
        {
            // The buy shelf, cheapest first within each kind. Nothing here is worked
            // out from anything else - copy a line and set the number you want.
            offers.Clear();
            offers.Add(NewOffer("Booster pack", 100, ShopGoods.BoosterPacks, 1,
                                "A sealed pack. Five cards, and the last one is never common."));
            offers.Add(NewOffer("Booster packs x3", 270, ShopGoods.BoosterPacks, 3,
                                "Three sealed packs, a tenth off the single price."));
            offers.Add(NewOffer("Half a box", 440, ShopGoods.BoosterPacks, 5,
                                "Five packs. An afternoon's opening, at a fair discount."));
            offers.Add(NewOffer("A box of ten", 850, ShopGoods.BoosterPacks, 10,
                                "Ten packs at once. The cheapest wrappers in the shop."));
            offers.Add(NewOffer("Random card", 30, ShopGoods.RandomCards, 1,
                                "One loose card off the rack. Deck and finish unseen."));
            offers.Add(NewOffer("Random cards x5", 130, ShopGoods.RandomCards, 5,
                                "Five loose cards, dealt straight onto the default pile."));
            offers.Add(NewOffer("Random cards x10", 250, ShopGoods.RandomCards, 10,
                                "Ten off the rack. Filling a suit the slow, certain way."));
            offers.Add(NewOffer("A handful", 480, ShopGoods.RandomCards, 20,
                                "Twenty in one go. Good odds on something that glitters."));

            // The board of wanted ads. Each of these is a different shape of want -
            // between them they cover everything the matcher can be asked for, so the
            // nearest one is worth copying rather than starting from an empty ad.
            ads.Clear();

            // One named card, in one finish. The tightest an ad gets - and the finish
            // is exact, so a galaxy Judgement will not fill this either.
            var judgement = NewAd("Holo Judgement", 750,
                                  "Wanted: the Judgement, and it has to be the holo one.");
            judgement.wants.Add(new CardWant { deck = CardDeck.Tarot, face = 20, finish = CardFinish.Holo });
            ads.Add(judgement);

            // One named card, any finish.
            var tower = NewAd("The Tower", 150,
                              "Wanted: the Tower from the tarot. Any finish, it is for a friend.");
            tower.wants.Add(new CardWant { deck = CardDeck.Tarot, face = 16 });
            ads.Add(tower);

            // Same card twice. count on a named face means copies of it, which is
            // exactly what you do not want for a set - see the rank ads below.
            var fools = NewAd("Two of the Fool", 260,
                              "Wanted: two Fools. One to keep, one to trade on.");
            fools.wants.Add(new CardWant { deck = CardDeck.Tarot, face = 0, count = 2 });
            ads.Add(fools);

            // Two named cards, as two separate lines. Ads can ask for a list.
            var luminaries = NewAd("The Sun and the Moon", 340,
                                   "Wanted: the pair of them, and they must be the pair.");
            luminaries.wants.Add(new CardWant { deck = CardDeck.Tarot, face = 19 });
            luminaries.wants.Add(new CardWant { deck = CardDeck.Tarot, face = 18 });
            ads.Add(luminaries);

            // A named card of the Spanish deck: face = suit * 10 + rank, so the Rey
            // (rank index 9) of Bastos (suit 3) is 39.
            var rey = NewAd("The Rey de Bastos", 180,
                            "Wanted: the king of clubs, Spanish style. Any finish.");
            rey.wants.Add(new CardWant { deck = CardDeck.SpanishDeck, face = 39 });
            ads.Add(rey);

            // One rank across every suit - four different cards, one line each.
            ads.Add(RankAd("The 4 of every suit", 600, 3,
                           "Wanted: the 4 from all four suits of the Spanish deck. Any finish."));
            ads.Add(RankAd("The Rey of every suit", 700, 9,
                           "Wanted: all four kings. Oros, Copas, Espadas, Bastos."));

            // A whole suit: ten different cards, so ten lines.
            ads.Add(SuitAd("Complete set: Copas", 1200, 1,
                           "Wanted: every one of the ten Copas. Pays for the lot."));
            ads.Add(SuitAd("Complete set: Oros", 1200, 0,
                           "Wanted: all ten Oros, 1 through Rey. Pays for the lot."));

            // By suit rather than by face: any card of it, as long as it is shiny.
            var espadas = NewAd("Three shiny Espadas", 420,
                                "Wanted: three swords in shiny. Which three is your business.");
            espadas.wants.Add(new CardWant
            {
                deck = CardDeck.SpanishDeck,
                suit = 2,
                finish = CardFinish.Shiny,
                count = 3,
            });
            ads.Add(espadas);

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

            // Everything left at Any. The loosest an ad gets, and fillable from the
            // first pack, so the sell shelf is never dead on arrival.
            var bulk = NewAd("Eight cards, any at all", 240,
                             "Wanted: eight cards. No, really - any eight. Clearing a gap in a folder.");
            bulk.wants.Add(new CardWant { count = 8 });
            ads.Add(bulk);
        }

        static ShopOffer NewOffer(string title, int price, ShopGoods goods, int count, string detail)
        {
            return new ShopOffer { title = title, price = price, goods = goods, count = count, detail = detail };
        }

        static WantedAd NewAd(string title, int price, string detail)
        {
            return new WantedAd { title = title, price = price, detail = detail };
        }

        /// <summary>One rank of the Spanish deck, across all four of its suits.</summary>
        static WantedAd RankAd(string title, int price, int rank, string detail)
        {
            var ad = NewAd(title, price, detail);
            for (int suit = 0; suit < 4; suit++)
                ad.wants.Add(new CardWant { deck = CardDeck.SpanishDeck, face = suit * 10 + rank });
            return ad;
        }

        /// <summary>Every card of one Spanish suit - ten separate cards, not ten of one.</summary>
        static WantedAd SuitAd(string title, int price, int suit, string detail)
        {
            var ad = NewAd(title, price, detail);
            for (int rank = 0; rank < 10; rank++)
                ad.wants.Add(new CardWant { deck = CardDeck.SpanishDeck, face = suit * 10 + rank });
            return ad;
        }
    }
}
