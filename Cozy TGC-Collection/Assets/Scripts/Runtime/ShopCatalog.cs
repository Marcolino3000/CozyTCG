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
    /// Tools > Cozy TGC > Reset Shop Catalog puts the shipped shelves back.
    ///
    /// The buy shelf is used in order, top to bottom. The ads are a pool rather than a
    /// list: <see cref="AdsOnBoard"/> of them are up at a time, and filling one brings
    /// up another that is not already there - so writing more than fit gives the board
    /// something to rotate through.
    ///
    /// Both shelves can be written in cards rather than in numbers: drop a
    /// <see cref="CardData"/> asset into <see cref="ShopOffer.card"/> for a row that
    /// sells one particular card, or into <see cref="CardWant.card"/> for an ad that
    /// asks for one. The row shows that card's face on its own, and neither can drift
    /// out of step with the decks the way a face index can.
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

#if UNITY_EDITOR
        /// <summary>
        /// Writes both shelves at once. Only the editor's ShopCatalogBuilder calls this:
        /// the shipped shelves name card assets, which nothing at run time can reach, so
        /// the defaults live over there rather than in a method here.
        /// </summary>
        public void EditorSetShelves(List<ShopOffer> buy, List<WantedAd> sell)
        {
            offers = buy ?? new List<ShopOffer>();
            ads = sell ?? new List<WantedAd>();
        }
#endif
    }
}
