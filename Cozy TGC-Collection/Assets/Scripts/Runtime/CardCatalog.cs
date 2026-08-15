using System.Collections.Generic;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// Which card this is, in the only terms that survive being filed away: the
    /// deck it was printed from, which face of it, and the tier it rolled.
    ///
    /// A <see cref="CardView"/> carries one from the moment it is made. Once a card
    /// is sitting in a pile or an album pocket it has nothing else left to say what
    /// it is - the face texture rides on a MaterialPropertyBlock and the rarity is
    /// a shared material - and the shop's wanted ads have to match on exactly this.
    /// </summary>
    public struct CardIdentity
    {
        /// <summary>Index into the controller's decks, or -1 for a card that came from nowhere.</summary>
        public int set;
        /// <summary>Index into that deck's cards, in the order CardArtLibrary sorted them.</summary>
        public int face;
        /// <summary>Rarity tier, as an index into the rarity materials.</summary>
        public int tier;

        /// <summary>
        /// A struct defaults to all zeros, which reads as a perfectly good "face 0 of
        /// deck 0" - so anything holding an identity starts on this instead.
        /// </summary>
        public static CardIdentity None => new CardIdentity { set = -1, face = -1, tier = -1 };

        public bool IsValid => set >= 0 && face >= 0;
    }

    /// <summary>
    /// The decks, by name, for anything authored by hand - a wanted ad asking for a
    /// tarot card should say so rather than say 0.
    ///
    /// The values are the decks' own order on PackOpeningController, which is what
    /// <see cref="CardIdentity.set"/> holds, so the two compare directly. Adding a deck
    /// in CardAssetBuilder means adding it here too; an index with no name here still
    /// matches, it just has nothing to be picked by.
    /// </summary>
    public enum CardDeck
    {
        Any = -1,
        Tarot = 0,
        SpanishDeck = 1,
    }

    /// <summary>
    /// The rarity tiers by name, in the order CardPackBuilder lists them - the same
    /// order as the rarity materials and their names on PackOpeningController.
    /// </summary>
    public enum CardFinish
    {
        Any = -1,
        Common = 0,
        Shiny = 1,
        Holo = 2,
        Galaxy = 3,
        Chrome = 4,
    }

    /// <summary>
    /// The decks a pack can be rolled from and the finishes it can roll, together in
    /// one place. Anything that has to *name* a card - the HUD readout, a shop offer,
    /// a wanted ad - goes through this rather than through the controller's own
    /// serialized lists, which is what keeps the shop from needing a reference to it.
    /// </summary>
    public class CardCatalog
    {
        readonly IReadOnlyList<CardDeckData> sets;
        readonly IReadOnlyList<string> tiers;
        /// <summary>
        /// Which deck and face each card asset sits at, so a shop row that names a card
        /// can be turned into the (deck, face) a minted card has to carry. Built once:
        /// the shop asks per purchase and per row it draws, and a card is in exactly one
        /// place - a duplicate in two decks keeps the first, which is the one the wanted
        /// ads would have matched anyway.
        /// </summary>
        readonly Dictionary<CardData, CardIdentity> places = new Dictionary<CardData, CardIdentity>();

        public CardCatalog(IReadOnlyList<CardDeckData> decks, IReadOnlyList<string> tierNames)
        {
            sets = decks ?? new List<CardDeckData>();
            tiers = tierNames ?? new List<string>();

            for (int set = 0; set < sets.Count; set++)
            {
                var deck = sets[set];
                if (deck == null) continue;

                for (int face = 0; face < deck.FaceCount; face++)
                {
                    var card = deck.CardAt(face);
                    if (card == null || places.ContainsKey(card)) continue;
                    places[card] = new CardIdentity { set = set, face = face, tier = 0 };
                }
            }
        }

        public int SetCount => sets.Count;
        public int TierCount => tiers.Count;

        public CardDeckData Set(int index) => index >= 0 && index < sets.Count ? sets[index] : null;

        /// <summary>The card asset a filed card is a copy of, or null if it came from nowhere.</summary>
        public CardData Card(CardIdentity id)
        {
            var set = Set(id.set);
            return set != null ? set.CardAt(id.face) : null;
        }

        /// <summary>What one copy of it is worth, off its own asset. 0 for a card with none.</summary>
        public int PriceOf(CardIdentity id)
        {
            var card = Card(id);
            return card != null ? card.price : 0;
        }

        /// <summary>
        /// The other way round: where a card asset sits, in the terms a minted card is
        /// stamped with. <see cref="CardIdentity.None"/> for a card that is in no deck
        /// the scene is holding - a shop row naming one of those cannot be delivered,
        /// which is what the invalid identity is there to say.
        /// </summary>
        public CardIdentity Locate(CardData card, int tier)
        {
            if (card == null || !places.TryGetValue(card, out CardIdentity id)) return CardIdentity.None;

            id.tier = tier;
            return id;
        }

        public string TierName(int tier) => tier >= 0 && tier < tiers.Count ? tiers[tier] : string.Empty;

        /// <summary>"4 of Copas", "Judgement".</summary>
        public string NameOf(CardIdentity id)
        {
            var set = Set(id.set);
            return set != null ? set.NameOf(id.face) : "Card";
        }

        /// <summary>The same with the finish in front: "Holo Judgement".</summary>
        public string FullNameOf(CardIdentity id)
        {
            string tier = TierName(id.tier);
            string name = NameOf(id);
            return string.IsNullOrEmpty(tier) ? name : $"{tier} {name}";
        }

        public Texture2D FaceOf(CardIdentity id)
        {
            var set = Set(id.set);
            return set != null ? set.FaceAt(id.face) : null;
        }

        /// <summary>A deck that is dealt in suits, or -1 if none is. Wanted ads about
        /// suits and ranks only make sense for one of those.</summary>
        public int FirstSuitedSet()
        {
            for (int i = 0; i < sets.Count; i++)
                if (sets[i] != null && sets[i].Suits > 0) return i;
            return -1;
        }
    }
}
