using System.Collections.Generic;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// A deck as an asset: the cards it holds in face order, the one back they share,
    /// how often a pack pulls from it, and how its cards are named when they do not
    /// name themselves.
    ///
    /// This is what the controller used to carry as a serialized art set pointing at a
    /// folder of textures. The cards are assets now (<see cref="CardData"/>) so they can
    /// be priced one by one, but the deck still owns everything that is true of all of
    /// them at once - the back, the pull weight, the suits and ranks a Spanish card is
    /// named from.
    ///
    /// The order of <see cref="cards"/> is what <see cref="CardIdentity.face"/> indexes.
    /// Tools > Cozy TGC > Build Card Assets keeps it in the order CardArtLibrary sorts
    /// the artwork folder, which is the order the shop's written ads count in.
    /// </summary>
    [CreateAssetMenu(fileName = "Deck", menuName = "Cozy TGC/Card Deck", order = 0)]
    public class CardDeckData : ScriptableObject
    {
        [Tooltip("What the deck is called on a shop shelf. Falls back to the asset name.")]
        public string displayName;
        [Tooltip("The one back every card of this deck wears.")]
        public Texture2D back;
        [Tooltip("How often a card comes off this deck, against the other decks' weights. " +
                 "A pack rolls its deck per card, so this is the mix inside one pack, not " +
                 "how often a pack is 'a deck of this kind'.")]
        public float packWeight = 1f;

        [Header("Naming")]
        [Tooltip("Cards per suit. 0 for a deck of named singles, like the tarot majors, " +
                 "where the faces are a run rather than a grid.")]
        public int suitSize;
        [Tooltip("One per suit, in face order.")]
        public string[] suitNames;
        [Tooltip("One per position within a suit - a Spanish deck runs 1-7 and then " +
                 "three court cards, so the numbering is not the position.")]
        public string[] rankNames;

        [Header("Cards")]
        [Tooltip("In face order, which is the order the artwork folder sorts in. " +
                 "Tools > Cozy TGC > Build Card Assets fills this and leaves the prices " +
                 "and weights on the cards themselves alone.")]
        public List<CardData> cards = new List<CardData>();

        public int FaceCount => cards.Count;

        public string Name => string.IsNullOrEmpty(displayName) ? name : displayName;

        /// <summary>How many suits the cards make up, or 0 for a deck of singles.</summary>
        public int Suits => suitSize > 0 ? FaceCount / suitSize : 0;

        public CardData CardAt(int face) => face >= 0 && face < cards.Count ? cards[face] : null;

        public Texture2D FaceAt(int face)
        {
            var card = CardAt(face);
            return card != null ? card.face : null;
        }

        public int SuitOf(int face) => suitSize > 0 && face >= 0 ? face / suitSize : -1;
        public int RankOf(int face) => suitSize > 0 && face >= 0 ? face % suitSize : -1;

        /// <summary>Face index of one card of one suit, or -1 if the deck is not dealt that way.</summary>
        public int FaceOf(int suit, int rank)
        {
            if (suitSize <= 0 || suit < 0 || rank < 0 || rank >= suitSize) return -1;
            int face = suit * suitSize + rank;
            return face < FaceCount ? face : -1;
        }

        public string SuitName(int suit)
            => suitNames != null && suit >= 0 && suit < suitNames.Length ? suitNames[suit] : $"suit {suit + 1}";

        public string RankName(int rank)
            => rankNames != null && rank >= 0 && rank < rankNames.Length ? rankNames[rank] : (rank + 1).ToString();

        /// <summary>"4 of Copas", "Judgement". The card's own name wins.</summary>
        public string NameOf(int face)
        {
            var card = CardAt(face);
            return card != null && !string.IsNullOrEmpty(card.displayName) ? card.displayName : SchemeName(face);
        }

        /// <summary>
        /// What the deck would call the card at a face with nothing to go on but its
        /// place - a rank and a suit, or a bare number. Also what the asset builder
        /// names a new card of a suited deck, so the forty Spanish cards do not have to
        /// be typed out anywhere.
        /// </summary>
        public string SchemeName(int face)
        {
            if (face < 0) return "Card";
            if (suitSize > 0) return $"{RankName(RankOf(face))} of {SuitName(SuitOf(face))}";
            return $"#{face}";
        }
    }
}
