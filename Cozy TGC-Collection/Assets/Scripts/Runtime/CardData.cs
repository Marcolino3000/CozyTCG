using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// One printed card, as an asset: its picture, what it is called, what a collector
    /// pays for it and how often it turns up.
    ///
    /// A card used to be nothing but an index into a folder of textures, which left
    /// nowhere to say that one of them is dearer or rarer than the rest. Every card is
    /// its own asset now, so both can be typed in next to the artwork they belong to.
    ///
    /// What has not changed is <see cref="CardIdentity"/>: a filed card is still
    /// (deck, face, finish), and the face is this card's place in its
    /// <see cref="CardDeckData.cards"/> list. The builder keeps that list in the order
    /// the artwork folder sorts in, so the wanted ads on ShopCatalog still ask for the
    /// cards they were written against.
    /// </summary>
    [CreateAssetMenu(fileName = "Card", menuName = "Cozy TGC/Card", order = 1)]
    public class CardData : ScriptableObject
    {
        [Tooltip("The card's face. The back is the deck's - every card of a deck shares one.")]
        public Texture2D face;

        [Tooltip("What it is called on a shop row and under the pointer. Left empty, the " +
                 "deck names it from its own scheme, which is how forty Spanish cards get " +
                 "named without any of them being typed out.")]
        public string displayName;

        [Tooltip("What one copy is worth in coins. The shop's rolled ads pay multiples of " +
                 "it, so a dear card is worth being asked for; the ads written on " +
                 "ShopCatalog name their own flat price and ignore this.")]
        public int price = 30;

        [Tooltip("How often this card comes up against the other cards of its own deck. " +
                 "1 is the plain rate, 2 is twice as likely, 0 keeps it out of packs " +
                 "altogether. Which deck is pulled from in the first place is packWeight " +
                 "on the deck, one level up.")]
        public float weight = 1f;

        /// <summary>The name to print, falling back to the asset's own.</summary>
        public string Name => string.IsNullOrEmpty(displayName) ? name : displayName;

        /// <summary>
        /// How likely this card is in one finish, before it is weighed against the rest
        /// of the deck: its own pull weight, thinned by <see cref="CardFinishOdds"/>.
        /// </summary>
        public float FinishWeight(int finish) => Mathf.Max(0f, weight) * CardFinishOdds.Weight(finish);
    }

    /// <summary>
    /// How much rarer each finish is than the plain print. Every step along the list -
    /// shiny, holo, galaxy, chrome - takes another fifth off the card's odds, so a shiny
    /// turns up at 80% of the rate a common does and a chrome at 20%.
    ///
    /// One rule for every card rather than a table per card or per deck: the finish is
    /// rolled after the card is, so this only ever scales what the card's own weight has
    /// already decided. Change the falloff here and every card follows.
    /// </summary>
    public static class CardFinishOdds
    {
        /// <summary>Taken off the odds per step, counting from the common at 0.</summary>
        public const float FalloffPerFinish = 0.2f;

        /// <summary>
        /// The common's share is 1. A finish past the point where the falloff runs out -
        /// a sixth one, at this rate - weighs nothing and is never rolled, rather than
        /// coming back round as a negative.
        /// </summary>
        public static float Weight(int finish)
            => finish <= 0 ? 1f : Mathf.Max(0f, 1f - FalloffPerFinish * finish);
    }
}
