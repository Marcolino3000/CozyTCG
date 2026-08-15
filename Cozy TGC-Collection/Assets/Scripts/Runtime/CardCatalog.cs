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
        /// <summary>Index into the controller's art sets, or -1 for a card that came from nowhere.</summary>
        public int set;
        /// <summary>Index into that set's faces, in the order CardArtLibrary sorted them.</summary>
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
    /// The values are the art sets' own order on PackOpeningController, which is what
    /// <see cref="CardIdentity.set"/> holds, so the two compare directly. Adding a deck
    /// in CardPackBuilder.BuildArtSets means adding it here too; an index with no name
    /// here still matches, it just has nothing to be picked by.
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
        readonly IReadOnlyList<CardArtSet> sets;
        readonly IReadOnlyList<string> tiers;

        public CardCatalog(IReadOnlyList<CardArtSet> artSets, IReadOnlyList<string> tierNames)
        {
            sets = artSets ?? new List<CardArtSet>();
            tiers = tierNames ?? new List<string>();
        }

        public int SetCount => sets.Count;
        public int TierCount => tiers.Count;

        public CardArtSet Set(int index) => index >= 0 && index < sets.Count ? sets[index] : null;

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
