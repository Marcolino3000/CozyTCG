using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// One rolled card, before it exists: what it is, what it is printed with and
    /// which rarity material draws it. Kept together rather than as parallel lists,
    /// because the identity has to arrive on the card at the same moment its face
    /// does - a card whose picture and paperwork disagree is unsellable.
    /// </summary>
    public struct CardDraw
    {
        public CardIdentity id;
        public Texture2D face;
        public Texture2D back;
        public Material material;
    }

    /// <summary>
    /// Makes a card from a draw. Both places that mint cards go through here - the
    /// booster stack when a pack is filled, and the shop when cards are bought
    /// straight off the shelf - so a bought card is the same object as a pulled one
    /// and can be filed, sold or dropped into an album exactly the same way.
    /// </summary>
    public static class CardFactory
    {
        /// <summary>
        /// Cards start with their collider off and no idle sway: everything this
        /// scene makes is going into a pile, and the slot that catches one is what
        /// hands its collider back. See CardSlot.RefreshColliders.
        /// </summary>
        public static CardView Create(GameObject prefab, Transform parent, in CardDraw draw, bool faceDown)
        {
            if (prefab == null)
            {
                Debug.LogError("[Cozy TGC] No card prefab to make a card from.");
                return null;
            }

            var instance = Object.Instantiate(prefab, parent);
            var view = instance.GetComponent<CardView>();
            if (view == null)
            {
                Debug.LogError($"[Cozy TGC] {prefab.name} has no CardView on it.");
                Object.Destroy(instance);
                return null;
            }

            view.Identity = draw.id;
            view.DeckBack = draw.back;
            view.SetFaces(draw.face, draw.back);
            view.SetFace(faceDown);
            view.SetIdleMotion(false);
            view.SetStackClearance(true);

            if (draw.material != null && view.Renderer != null) view.Renderer.sharedMaterial = draw.material;

            var collider = instance.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            return view;
        }
    }
}
