using System.Collections.Generic;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// The stack of cards a wrapper was hiding. Cards sit face down until they
    /// are taken off the top, at which point they belong to whichever CardSlot
    /// catches them - this only owns the pile that is still in the pack.
    ///
    /// Stacked cards keep their collider switched off, which is what keeps the
    /// scene's CardInteractor off them until they have been dealt out.
    /// </summary>
    [DisallowMultipleComponent]
    public class CardPackDeck : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] GameObject cardPrefab;

        [Header("Stack")]
        [SerializeField] int cardsPerPack = 5;
        [Tooltip("Step between stacked cards. +Z is away from the camera, so the " +
                 "stack recedes and the card on top is the one nearest the viewer.")]
        [SerializeField] Vector3 stackStep = new Vector3(0.004f, -0.006f, 0.014f);
        [SerializeField] float stackTiltJitter = 1.4f;
        [SerializeField] float stackSettleSpeed = 12f;
        [Tooltip("Where the cards start when the wrapper comes off, so they rise " +
                 "into the stack instead of popping into existence.")]
        [SerializeField] Vector3 revealOffset = new Vector3(0f, -0.32f, 0.06f);

        readonly List<CardView> cards = new List<CardView>();

        bool revealed;

        public int Remaining => cards.Count;
        public bool CanDraw => revealed && cards.Count > 0;
        public bool Revealed => revealed;
        /// <summary>Has the card on top already been turned over in place?</summary>
        public bool TopRevealed => cards.Count > 0 && !cards[0].ShowingBack;
        /// <summary>How many cards a full pack holds, before any are taken.</summary>
        public int CardCapacity => cardsPerPack;
        /// <summary>What the card that would come off next is, for the HUD readout.</summary>
        public CardIdentity TopIdentity => cards.Count > 0 ? cards[0].Identity : CardIdentity.None;

        void Update()
        {
            SettleStack(Mathf.Min(Time.deltaTime, 0.05f));
        }

        // -------------------------------------------------------------------
        // Contents
        // -------------------------------------------------------------------
        /// <summary>
        /// Throws away whatever is left of the last pack and deals a fresh, hidden stack.
        ///
        /// Every card is dealt with <paramref name="packBack"/> rather than with its own
        /// deck's back, because a pack holds cards from more than one deck and their
        /// backs do not look alike: one purple back in a stack of blue ones announces
        /// the rare card before anybody turns it over. <see cref="Take"/> puts the
        /// card's real back on as it leaves. Null to let each card keep its own.
        /// </summary>
        public void Fill(IList<CardDraw> draws, Texture2D packBack)
        {
            Clear();

            // Build while the deck is active so every card runs Awake now rather
            // than half a pack later, when the wrapper finally comes off.
            gameObject.SetActive(true);

            int count = Mathf.Min(cardsPerPack, draws?.Count ?? 0);
            for (int i = 0; i < count; i++)
            {
                // Face down: the whole point of the stack is that the card is not
                // known until it is turned over. CardFactory sets the rest - cards in
                // this scene spend their life in a pile, so the resting sway is off
                // and the depth clearance is on from the start.
                var view = CardFactory.Create(cardPrefab, transform, draws[i], true);
                if (view == null) break;

                if (packBack != null) view.SetFaces(null, packBack);
                view.name = $"PackCard_{i}";
                view.transform.SetLocalPositionAndRotation(StackPosition(i) + revealOffset, StackRotation(i));
                cards.Add(view);
            }

            revealed = false;
            gameObject.SetActive(false);
        }

        /// <summary>Destroys what is left in the pack. Cards already in a slot are not ours.</summary>
        public void Clear()
        {
            foreach (var card in cards)
                if (card != null) Destroy(card.gameObject);

            cards.Clear();
            revealed = false;
        }

        /// <summary>Called when the wrapper starts sliding off; the stack rises into view behind it.</summary>
        public void Reveal()
        {
            revealed = true;
            gameObject.SetActive(true);
        }

        // -------------------------------------------------------------------
        // Handing cards over
        // -------------------------------------------------------------------
        /// <summary>
        /// Turns the top card face up where it lies. Nothing leaves the pack until
        /// it has been seen, so this is always the first thing that happens to it.
        /// </summary>
        public void RevealTop()
        {
            if (cards.Count > 0 && cards[0].ShowingBack) cards[0].Flip();
        }

        /// <summary>Lifts the top card off the pack. The caller owns it from here.</summary>
        public CardView Take()
        {
            if (!CanDraw) return null;

            var card = cards[0];
            cards.RemoveAt(0);

            // Out of the pack, so it stops wearing the pack's back and puts its own on.
            // Nothing sees the change: a card only ever leaves face up.
            if (card.DeckBack != null) card.SetFaces(null, card.DeckBack);

            // Same as a slot hands one over: a carried card belongs to no pile.
            card.transform.SetParent(null, true);

            var collider = card.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            return card;
        }

        /// <summary>
        /// Puts a card back on top, for a drag that was let go over nothing. It
        /// stays face up - it has already been seen, so turning it back over would
        /// just make the player reveal it a second time.
        /// </summary>
        public void Return(CardView card)
        {
            if (card == null) return;

            card.transform.SetParent(transform, true);
            cards.Insert(0, card);

            var collider = card.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
        }

        void SettleStack(float dt)
        {
            if (!revealed) return;

            float k = 1f - Mathf.Exp(-stackSettleSpeed * dt);
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (card == null) continue;

                var t = card.transform;
                t.localPosition = Vector3.Lerp(t.localPosition, StackPosition(i), k);
                t.localRotation = Quaternion.Slerp(t.localRotation, StackRotation(i), k);
                // A card handed back from elsewhere carries that place's size in its
                // localScale; settle it back to the pack's.
                t.localScale = Vector3.Lerp(t.localScale, Vector3.one, k);
            }
        }

        // -------------------------------------------------------------------
        // Layout
        // -------------------------------------------------------------------
        Vector3 StackPosition(int depth) => stackStep * depth;

        Quaternion StackRotation(int index)
        {
            // Deterministic per depth, so a card does not re-roll its lean every frame.
            float angle = (Mathf.PerlinNoise(index * 3.7f, 0.5f) - 0.5f) * 2f * stackTiltJitter;
            return Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>Half extents of the stack's click target, in world units.</summary>
        public Vector2 ClickSize
        {
            get
            {
                var top = cards.Count > 0 ? cards[0] : null;
                return top != null ? top.Size * 0.5f : new Vector2(0.365f, 0.565f);
            }
        }

#if UNITY_EDITOR
        public void EditorBind(GameObject prefab, int perPack)
        {
            cardPrefab = prefab;
            cardsPerPack = perPack;
        }
#endif
    }
}
