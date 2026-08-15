using System.Collections.Generic;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// What the player actually owns, gathered from the two places a card can be
    /// filed: the row of piles and the pockets of every album, open or shut. A card
    /// still in the pack or in mid drag is deliberately not owned yet - it has not
    /// been put anywhere - which is also what keeps a sale from pulling a card out
    /// from under the cursor carrying it.
    ///
    /// Everything here works in (slot, card) pairs, because a sale has to take a
    /// named card out of whichever pile happens to hold it rather than off the top
    /// of one.
    /// </summary>
    public static class CardCollection
    {
        public struct Held
        {
            public CardSlot slot;
            public CardView card;
        }

        static readonly List<Held> pool = new List<Held>();
        static readonly System.Comparison<Held> ByTier =
            (a, b) => a.card.Identity.tier.CompareTo(b.card.Identity.tier);

        public static void Gather(CardSlotBoard board, IList<CardAlbum> albums, List<Held> into)
        {
            if (into == null) return;
            into.Clear();

            Gather(board, into);
            if (albums == null) return;

            foreach (var album in albums)
            {
                if (album == null) continue;
                foreach (var page in album.Pages) Gather(page, into);
            }
        }

        static void Gather(CardSlotBoard board, List<Held> into)
        {
            if (board == null) return;

            foreach (var slot in board.Slots)
            {
                if (slot == null) continue;
                foreach (var card in slot.Cards)
                    if (card != null) into.Add(new Held { slot = slot, card = card });
            }
        }

        /// <summary>
        /// How many of an ad's cards the collection can cover, filling
        /// <paramref name="matched"/> with the ones it would hand over. Short of
        /// <see cref="WantedAd.Total"/> means the ad cannot be filled yet - the shop
        /// shows that as progress rather than hiding the row.
        ///
        /// Cards are spent lowest finish first, so a want that would take anything
        /// does not swallow a galaxy card that a common would have satisfied.
        /// </summary>
        public static int Match(IReadOnlyList<Held> owned, WantedAd ad, List<Held> matched, CardCatalog catalog)
        {
            if (matched == null) return 0;
            matched.Clear();
            if (ad == null || owned == null) return 0;

            pool.Clear();
            for (int i = 0; i < owned.Count; i++)
                if (owned[i].card != null) pool.Add(owned[i]);
            pool.Sort(ByTier);

            int found = 0;
            foreach (var want in ad.wants)
            {
                if (want == null) continue;
                for (int n = 0; n < want.count; n++)
                {
                    int index = -1;
                    for (int i = 0; i < pool.Count; i++)
                    {
                        if (!want.Matches(pool[i].card.Identity, catalog)) continue;
                        index = i;
                        break;
                    }
                    if (index < 0) break;

                    matched.Add(pool[index]);
                    pool.RemoveAt(index);
                    found++;
                }
            }
            return found;
        }

        /// <summary>Hands the cards over: out of their piles and gone for good.</summary>
        public static void Remove(List<Held> cards)
        {
            if (cards == null) return;

            foreach (var held in cards)
            {
                if (held.card == null) continue;
                if (held.slot != null) held.slot.Remove(held.card);
                Object.Destroy(held.card.gameObject);
            }
        }
    }
}
