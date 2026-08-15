using System;
using System.Collections.Generic;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// A picture on a shop row. Carries a UV rect as well as a texture because some
    /// of the previews are cells of an atlas - a booster wrapper is one of 180 on
    /// CardPacks.png - and slicing those into sprites just to show them is the one
    /// thing this project never does with a sheet.
    /// </summary>
    public struct ShopIcon
    {
        public Texture texture;
        public Rect uv;

        public static ShopIcon Whole(Texture texture) => new ShopIcon { texture = texture, uv = new Rect(0f, 0f, 1f, 1f) };

        /// <summary>From the xy = offset, zw = size vector the sheets hand out.</summary>
        public static ShopIcon Cell(Texture texture, Vector4 rect)
            => new ShopIcon { texture = texture, uv = new Rect(rect.x, rect.y, rect.z, rect.w) };

        public bool IsValid => texture != null && uv.width > 0f && uv.height > 0f;
    }

    public enum ShopGoods
    {
        /// <summary>Goes into the drawer under the shelf, to be opened later.</summary>
        BoosterPacks,
        /// <summary>Rolled on the spot and dealt onto the default pile, face up.</summary>
        RandomCards,
        /// <summary>One named card off <see cref="ShopOffer.card"/>, dealt the same way.</summary>
        NamedCard,
    }

    /// <summary>
    /// One row on the buy shelf, as authored on a <see cref="ShopCatalog"/>.
    ///
    /// A row selling one particular card is written by dropping the card's own
    /// <see cref="CardData"/> asset into <see cref="card"/> and setting
    /// <see cref="goods"/> to <see cref="ShopGoods.NamedCard"/>. There is nothing to
    /// pick for the picture: the row draws what it is selling.
    ///
    /// Such a row names the finish too, and starts on Common rather than Any - a card
    /// on a shelf is a particular copy of a particular card, price and all. Any still
    /// works and is kept for later, for a row that sells a known card at an unknown
    /// print.
    /// </summary>
    [Serializable]
    public class ShopOffer
    {
        public string title = "Booster pack";
        [TextArea(2, 3)] public string detail;
        public int price = 100;
        public ShopGoods goods = ShopGoods.BoosterPacks;
        [Tooltip("How many of them one purchase delivers.")]
        public int count = 1;

        [Tooltip("The card asset this row sells. Only read when goods is NamedCard, and " +
                 "the row is dead without one.")]
        public CardData card;
        [Tooltip("What finish it is printed in. A row selling a named card is meant to " +
                 "name the print as well - you are buying a particular card off a shelf, " +
                 "not opening it. Any is left in for later: it rolls the finish the way " +
                 "a pack would, for a row that sells a known card at an unknown print.")]
        public CardFinish finish = CardFinish.Common;

        /// <summary>Worked out when the shelf is stocked; never written to the asset.</summary>
        [NonSerialized] public ShopIcon icon;
    }

    /// <summary>
    /// One line of a wanted ad: cards of a kind, and how many of them.
    ///
    /// A line about one particular card is written by dropping that card's own
    /// <see cref="CardData"/> asset into <see cref="card"/>. That is the whole of
    /// which card it is, which is why there is no face number here any more - a card
    /// is picked, not counted to.
    ///
    /// <see cref="deck"/> and <see cref="suit"/> are what is left for the lines that
    /// deliberately do *not* name a card: "any three cards of the Spanish deck",
    /// "three swords in shiny". Any and -1 mean the line does not care, so a want can
    /// be as loose as "five cards" or as tight as "the Judgement, in holo".
    ///
    /// A class rather than a struct so that a line added in the inspector starts on
    /// those -1s. Zeroes would quietly mean "the first suit of the first deck".
    /// </summary>
    [Serializable]
    public class CardWant
    {
        [Tooltip("The exact card wanted, as its own asset. Set, and the deck and suit " +
                 "below stop applying - only the finish still does.")]
        public CardData card;

        [Tooltip("Which deck the card has to come from, for a line that does not name one.")]
        public CardDeck deck = CardDeck.Any;
        [Tooltip("0 coins, 1 cups, 2 swords, 3 wands. -1 for any suit.")]
        public int suit = -1;
        [Tooltip("Exactly this finish - a collector after a holo does not want the galaxy " +
                 "one either. Any, for a card in whatever finish it turns up in.")]
        public CardFinish finish = CardFinish.Any;
        [Tooltip("How many cards matching this line. Note that a line asking for two of " +
                 "one named card wants two copies of it.")]
        public int count = 1;

        public bool Matches(CardIdentity id, CardCatalog catalog)
        {
            if (!id.IsValid) return false;
            if (finish != CardFinish.Any && id.tier != (int)finish) return false;

            // A named card answers the whole question of which card this is, deck and
            // suit included, so neither is asked as well. The finish is a different
            // question and still stands.
            if (card != null) return catalog != null && catalog.Card(id) == card;

            if (deck != CardDeck.Any && id.set != (int)deck) return false;
            if (suit < 0) return true;

            var set = catalog?.Set(id.set);
            return set != null && set.SuitOf(id.face) == suit;
        }
    }

    /// <summary>
    /// A collector's ad on the shop's board: what they are after and what they pay.
    /// Selling is only ever this - there is no price on a single card, because a
    /// card is worth what somebody is asking for it.
    /// </summary>
    [Serializable]
    public class WantedAd
    {
        public string title = "Wanted";
        [TextArea(2, 3)] public string detail;
        public int price = 100;
        public List<CardWant> wants = new List<CardWant>();

        /// <summary>Worked out when the ad goes on the board; never written to the asset.</summary>
        [NonSerialized] public ShopIcon icon;

        /// <summary>How many cards the ad is asking for in total.</summary>
        public int Total
        {
            get
            {
                int total = 0;
                foreach (var want in wants)
                    if (want != null) total += Mathf.Max(0, want.count);
                return total;
            }
        }
    }

    /// <summary>
    /// What the shop has on its two shelves, put together from the authored
    /// <see cref="ShopCatalog"/> asset.
    ///
    /// The buy shelf is that asset's list, in order. The sell board is a rotation:
    /// it shows a handful of the asset's ads at a time, and filling one brings up
    /// another that is not already up there. When the authored ones run out it can
    /// keep going with rolled ads - the four kinds below, written against whatever
    /// decks the catalog actually holds, so a deck with no suits never produces an ad
    /// asking for one.
    /// </summary>
    public class ShopStock
    {
        readonly ShopCatalog shop;
        readonly CardCatalog catalog;
        readonly List<ShopOffer> offers = new List<ShopOffer>();
        readonly List<WantedAd> wanted = new List<WantedAd>();
        readonly Texture packSheet;

        /// <summary>
        /// What a card is worth when its own asset does not say - the payout of a rolled
        /// ad is a multiple of the price on the cards it names, and of this for the ads
        /// that name none.
        /// </summary>
        readonly int cardValue;

        public IReadOnlyList<ShopOffer> Offers => offers;
        public IReadOnlyList<WantedAd> Wanted => wanted;

        public ShopStock(ShopCatalog shopCatalog, CardCatalog cardCatalog, Texture sheet, int cardValue)
        {
            shop = shopCatalog;
            catalog = cardCatalog;
            packSheet = sheet;
            this.cardValue = Mathf.Max(1, cardValue);

            if (shop == null)
            {
                Debug.LogWarning("[Cozy TGC] No shop catalog assigned - the shop opens empty. " +
                                 "Assets/Shop/ShopCatalog.asset is made by Build Pack Opening Scene.");
                return;
            }

            foreach (var offer in shop.Offers)
            {
                if (offer == null) continue;
                offer.icon = IconFor(offer);
                offers.Add(offer);
            }

            RefillBoard();
        }

        /// <summary>
        /// A row draws what it is selling, and nothing says what that is except the row
        /// itself - which is why there is no picture to pick on an offer. A wrapper for
        /// packs, the card's own face where the row names one, and the back of a card
        /// for the ones sold unseen, because that is exactly what is being bought.
        /// </summary>
        ShopIcon IconFor(ShopOffer offer)
        {
            if (offer.goods == ShopGoods.BoosterPacks)
                return ShopIcon.Cell(packSheet, CardPackSheet.UvRect(packSheet, CardPackSheet.RandomIndex()));

            if (offer.goods == ShopGoods.NamedCard && offer.card != null)
                return ShopIcon.Whole(offer.card.face);

            var deck = catalog?.Set(0);
            return ShopIcon.Whole(deck != null ? deck.back : null);
        }

        /// <summary>
        /// The same for an ad, which shows the card it is about: the first line that
        /// names one, or the first card of a suit for a line asking by suit. An ad that
        /// wants anything at all has nothing better to show than the back of a card.
        /// </summary>
        ShopIcon IconFor(WantedAd ad)
        {
            foreach (var want in ad.wants)
            {
                if (want == null) continue;
                if (want.card != null) return ShopIcon.Whole(want.card.face);
                if (want.deck == CardDeck.Any) continue;

                var deck = catalog?.Set((int)want.deck);
                if (deck == null) continue;

                int face = want.suit >= 0 ? deck.FaceOf(want.suit, 0) : -1;
                if (face >= 0) return ShopIcon.Whole(deck.FaceAt(face));
            }

            var first = catalog?.Set(0);
            return ShopIcon.Whole(first != null ? first.back : null);
        }

        /// <summary>
        /// What a collector starts from for one named card: the price typed on the
        /// card's own asset, or the flat <see cref="cardValue"/> for a card that has
        /// none. Every rolled ad's payout is worked out from this and then multiplied
        /// by how hard it is to fill.
        /// </summary>
        int PriceOf(CardDeckData deck, int face)
        {
            var card = deck != null ? deck.CardAt(face) : null;
            return card != null && card.price > 0 ? card.price : cardValue;
        }

        // -------------------------------------------------------------------
        // The sell board
        // -------------------------------------------------------------------
        void RefillBoard()
        {
            int size = shop != null ? shop.AdsOnBoard : 0;
            while (wanted.Count < size)
            {
                var ad = NextAd(null);
                if (ad == null) return;
                wanted.Add(ad);
            }
        }

        /// <summary>
        /// Replaces a filled ad with another, so the board stays the same length and
        /// the shop never runs out of buyers.
        /// </summary>
        public void Reroll(WantedAd ad)
        {
            int index = wanted.IndexOf(ad);
            if (index < 0) return;

            wanted.RemoveAt(index);
            // Excluded from its own replacement: an ad that comes straight back after
            // being filled reads as the sale not having happened.
            var fresh = NextAd(ad);
            if (fresh != null) wanted.Insert(index, fresh);
        }

        /// <summary>
        /// An authored ad that is not already up, or a rolled one when they have all
        /// been used. Null when there is nothing left to put up at all.
        /// </summary>
        WantedAd NextAd(WantedAd exclude)
        {
            if (shop == null) return null;

            var pool = new List<WantedAd>();
            foreach (var ad in shop.Ads)
            {
                if (ad == null || ad == exclude || ad.Total <= 0) continue;
                if (wanted.Contains(ad)) continue;
                pool.Add(ad);
            }

            WantedAd chosen = pool.Count > 0 ? pool[UnityEngine.Random.Range(0, pool.Count)]
                            : shop.TopUpWithRolledAds ? RollAd()
                            : null;

            if (chosen != null) chosen.icon = IconFor(chosen);
            return chosen;
        }

        /// <summary>
        /// The rolled ads, for a board that has run through everything on the asset.
        /// Four kinds, two of which need a deck dealt in suits - so with only the tarot
        /// loaded the roll is cut to the two that do not. Weighted towards the small
        /// ones: an ad for a whole suit is a scene's worth of packs, and a board of
        /// nothing but those has nothing to do on it.
        /// </summary>
        WantedAd RollAd()
        {
            int suited = catalog != null ? catalog.FirstSuitedSet() : -1;

            int roll = UnityEngine.Random.Range(0, suited >= 0 ? 10 : 5);
            if (roll < 3) return NamedCardAd(false);   // 30%: one named card, any finish
            if (roll < 5) return AnyFinishAd();        // 20%: N cards at a finish
            if (roll < 8) return RankAd(suited);       // 30%: one rank across the suits
            if (roll < 9) return NamedCardAd(true);    // 10%: one named card in a finish
            return SuitAd(suited);                     // 10%: a complete suit
        }

        WantedAd NamedCardAd(bool withFinish)
        {
            int setIndex = UnityEngine.Random.Range(0, Mathf.Max(1, catalog != null ? catalog.SetCount : 1));
            var deck = catalog?.Set(setIndex);
            if (deck == null || deck.FaceCount == 0) return FallbackAd();

            int face = UnityEngine.Random.Range(0, deck.FaceCount);
            // Never the top tier: an ad nobody can fill is a row that never lights up.
            int tier = withFinish ? UnityEngine.Random.Range(1, Mathf.Max(2, catalog.TierCount - 1)) : 0;
            string finish = catalog.TierName(tier);
            string name = deck.NameOf(face);

            var ad = new WantedAd
            {
                title = withFinish ? $"{finish} {name}" : name,
                detail = withFinish
                    ? $"Wanted: the {name} from the {deck.Name}, and it has to be the " +
                      $"{finish.ToLowerInvariant()} one."
                    : $"Wanted: the {name} from the {deck.Name}. Any finish will do.",
                // Five times what the card is worth, and more again for a finish that
                // has to be waited for. A dear card is a dear ad.
                price = Mathf.RoundToInt(PriceOf(deck, face) * 5f * (1f + 2f * tier)),
            };
            // The card asset itself, the same way a hand written ad names one - so a
            // rolled ad and an authored one are the same thing by the time they are on
            // the board.
            ad.wants.Add(new CardWant
            {
                card = deck.CardAt(face),
                finish = withFinish ? (CardFinish)tier : CardFinish.Any,
                count = 1,
            });
            return ad;
        }

        WantedAd RankAd(int setIndex)
        {
            var deck = catalog?.Set(setIndex);
            if (deck == null || deck.Suits == 0) return FallbackAd();

            int rank = UnityEngine.Random.Range(0, deck.suitSize);
            string rankName = deck.RankName(rank);

            var ad = new WantedAd
            {
                title = $"The {rankName} of every suit",
                detail = $"Wanted: the {rankName} from all {deck.Suits} suits of the {deck.Name}. Any finish.",
            };

            // Priced off the cards it actually names rather than off a card count, so a
            // rank whose four cards are dear pays like it.
            int worth = 0;
            for (int suit = 0; suit < deck.Suits; suit++)
            {
                int face = deck.FaceOf(suit, rank);
                if (face < 0) continue;
                worth += PriceOf(deck, face);
                ad.wants.Add(new CardWant { card = deck.CardAt(face), count = 1 });
            }

            ad.price = Mathf.RoundToInt(worth * 5f);
            return ad;
        }

        WantedAd SuitAd(int setIndex)
        {
            var deck = catalog?.Set(setIndex);
            if (deck == null || deck.Suits == 0) return FallbackAd();

            int suit = UnityEngine.Random.Range(0, deck.Suits);
            string suitName = deck.SuitName(suit);

            var ad = new WantedAd
            {
                title = $"Complete set: {suitName}",
                detail = $"Wanted: every one of the {deck.suitSize} {suitName} cards. Pays for the lot.",
            };

            int worth = 0;
            for (int rank = 0; rank < deck.suitSize; rank++)
            {
                int face = deck.FaceOf(suit, rank);
                if (face < 0) continue;
                worth += PriceOf(deck, face);
                ad.wants.Add(new CardWant { card = deck.CardAt(face), count = 1 });
            }

            // A shade under the rank ad's rate per card: ten of one suit is a longer
            // wait, but it is one deck's worth of luck rather than four suits' worth.
            ad.price = Mathf.RoundToInt(worth * 4f);
            return ad;
        }

        WantedAd AnyFinishAd()
        {
            int tier = catalog != null && catalog.TierCount > 2
                ? UnityEngine.Random.Range(1, catalog.TierCount - 1) : 1;
            int count = UnityEngine.Random.Range(2, 6);
            string finish = catalog != null ? catalog.TierName(tier) : "shiny";

            var ad = new WantedAd
            {
                title = $"{count} cards in {finish.ToLowerInvariant()}",
                detail = $"Wanted: any {count} cards in {finish.ToLowerInvariant()}. Deck does not matter.",
                price = Mathf.RoundToInt(cardValue * count * (1.5f + tier)),
            };
            ad.wants.Add(new CardWant { finish = (CardFinish)tier, count = count });
            return ad;
        }

        /// <summary>For a catalog with nothing loaded, so a rolled ad is never null.</summary>
        WantedAd FallbackAd()
        {
            var ad = new WantedAd
            {
                title = "Any card at all",
                detail = "Wanted: one card, any deck, any finish.",
                price = cardValue * 2,
            };
            ad.wants.Add(new CardWant { count = 1 });
            return ad;
        }
    }
}
