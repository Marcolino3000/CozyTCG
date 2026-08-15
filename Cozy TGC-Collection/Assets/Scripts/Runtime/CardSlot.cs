using System.Collections.Generic;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// One pile at the bottom of the board. Cards land here from the booster
    /// stack, either because they were clicked into the default slot or because
    /// they were dropped on this one.
    ///
    /// Only the top card keeps its collider, so the scene's CardInteractor works
    /// on the card you can actually see rather than the ones buried under it.
    /// </summary>
    [DisallowMultipleComponent]
    public class CardSlot : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int HighlightId = Shader.PropertyToID("_Highlight");

        class Arrival
        {
            public CardView card;
            public Vector3 from;
            public Quaternion fromRotation;
            public Vector3 fromScale;
            public float t;
        }

        [Header("References")]
        [SerializeField] MeshRenderer frame;

        [Header("Slot")]
        [SerializeField] string label = "Stack";
        [Tooltip("Clicking the booster stack sends cards to whichever slot is the default.")]
        [SerializeField] bool isDefault;
        [SerializeField] Color restColor = new Color(0.62f, 0.58f, 0.8f, 1f);
        [SerializeField] Color defaultColor = new Color(0.95f, 0.78f, 0.45f, 1f);

        [Header("Catch Area")]
        [Tooltip("Half extents of the area that catches a dropped card. Sized so " +
                 "neighbouring slots tile without gaps - anywhere along the row lands somewhere.")]
        [SerializeField] Vector2 catchExtents = new Vector2(0.44f, 0.72f);

        [Header("Pile")]
        [Tooltip("How many cards fit. 0 for a pile with no limit; an album pocket holds one.")]
        [SerializeField] int capacity;
        [SerializeField] Vector3 stackStep = new Vector3(0.004f, 0.009f, -0.012f);
        [Tooltip("The pile stops growing visually past this many cards. The count keeps rising.")]
        [SerializeField] int visibleDepth = 14;
        [SerializeField] float tiltJitter = 2f;
        [Tooltip("Cards sit dead straight and ignore the pointer entirely. An album pocket " +
                 "is a printed page rather than a pile tossed on a table, so a card in one " +
                 "should look filed: no lean, no hover pop, no tilt following the cursor. " +
                 "It is the collider that is withheld, which is what CardInteractor works " +
                 "off - dragging a card back out goes through the pocket's catch area and " +
                 "is unaffected.")]
        [SerializeField] bool still;

        [Header("Arrival")]
        [SerializeField] float flightDuration = 0.45f;
        [Tooltip("How far the card leans towards the camera mid flight.")]
        [SerializeField] float flightArc = 0.4f;

        readonly List<CardView> cards = new List<CardView>();
        readonly List<Arrival> arrivals = new List<Arrival>();

        MaterialPropertyBlock block;
        float highlight;
        float highlightTarget;

        public string Label => label;
        public bool IsDefault => isDefault;
        public int Count => cards.Count;
        /// <summary>Bottom of the pile first. The shop reads this to work out what is owned.</summary>
        public IReadOnlyList<CardView> Cards => cards;
        /// <summary>Is there still room? A full pocket refuses a card rather than swapping.</summary>
        public bool CanAccept => capacity <= 0 || cards.Count < capacity;

        void Awake()
        {
            highlight = 0f;
            PushFrame();
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            highlight = Mathf.Lerp(highlight, highlightTarget, 1f - Mathf.Exp(-14f * dt));
            PushFrame();
            AdvanceArrivals(dt);
        }

        public void SetHighlighted(bool on) => highlightTarget = on ? 1f : 0f;

        // -------------------------------------------------------------------
        // Contents
        // -------------------------------------------------------------------
        /// <summary>
        /// Takes ownership of a card and flies it onto the top of the pile. Cards
        /// are always turned over on the pack before they leave it, so they arrive
        /// face up and nothing here has to flip them.
        /// </summary>
        public void Receive(CardView card)
        {
            if (card == null) return;

            card.transform.SetParent(transform, true);
            card.SetIdleMotion(false);
            // The card on top of a pile is still hoverable, and a 14 degree tilt
            // reaches far deeper than the pile's step - it has to ride clear of the
            // cards under it while it leans. A card that never leans needs none of
            // that, and the clearance would only lift it off its page.
            card.SetStackClearance(!still);
            cards.Add(card);

            arrivals.Add(new Arrival
            {
                card = card,
                from = card.transform.position,
                fromRotation = card.transform.rotation,
                // Reparenting keeps the card's world size, which it does by writing the
                // difference into localScale. The arrival unwinds that back to 1, so the
                // card ends up the size of wherever it landed - the row of piles rides
                // small, an album pocket does not.
                fromScale = card.transform.localScale,
            });

            // Nothing in the pile answers the pointer while a card is still coming
            // in, or it could be grabbed out of the tween that is driving it.
            RefreshColliders();
        }

        /// <summary>
        /// Lifts the top card off the pile so it can be carried somewhere else. The
        /// caller owns it from here; the cards under it keep the positions they were
        /// given, since only the last one is ever removed.
        /// </summary>
        public CardView TakeTop()
        {
            if (cards.Count == 0) return null;

            var card = cards[cards.Count - 1];
            cards.RemoveAt(cards.Count - 1);

            // It may still have been flying in - cancel that, or the arrival tween
            // keeps writing the position out from under the drag.
            for (int i = arrivals.Count - 1; i >= 0; i--)
                if (arrivals[i].card == card) arrivals.RemoveAt(i);

            // Out of the pile and into the player's hand, so out of the pile's transform
            // too - otherwise the row's rest scale would keep shrinking a card that is
            // no longer in the row.
            card.transform.SetParent(null, true);

            var collider = card.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            RefreshColliders();
            return card;
        }

        /// <summary>
        /// Pulls one card out from wherever it sits in the pile, for a sale - the
        /// buyer wants a particular card, not whatever happens to be on top. The
        /// caller owns it from here.
        ///
        /// Unlike <see cref="TakeTop"/> this leaves a hole, so the cards above it
        /// are re-laid at once. They snap rather than slide: a pile settling by one
        /// step is not worth an animation, and the alternative is a visible gap.
        /// </summary>
        public bool Remove(CardView card)
        {
            int index = cards.IndexOf(card);
            if (index < 0) return false;

            cards.RemoveAt(index);
            for (int i = arrivals.Count - 1; i >= 0; i--)
                if (arrivals[i].card == card) arrivals.RemoveAt(i);

            card.transform.SetParent(null, true);
            Restack();
            RefreshColliders();
            return true;
        }

        public void Clear()
        {
            foreach (var card in cards)
                if (card != null) Destroy(card.gameObject);

            cards.Clear();
            arrivals.Clear();
        }

        /// <summary>Puts every card that is not still flying in back on its own step of the pile.</summary>
        void Restack()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;
                if (arrivals.Exists(a => a.card == cards[i])) continue;
                cards[i].transform.SetPositionAndRotation(PositionFor(i), RotationFor(i));
            }
        }

        void AdvanceArrivals(float dt)
        {
            for (int i = arrivals.Count - 1; i >= 0; i--)
            {
                var arrival = arrivals[i];
                if (arrival.card == null) { arrivals.RemoveAt(i); continue; }

                arrival.t += dt / Mathf.Max(flightDuration, 0.01f);
                float t = Mathf.Clamp01(arrival.t);
                float e = 1f - Mathf.Pow(1f - t, 3f);

                // Resolved every frame rather than captured on take-off: an album
                // fits itself into the frame every frame it is open, which moves the
                // pocket a card is aimed at while it is still on its way there.
                int depth = cards.IndexOf(arrival.card);
                if (depth < 0) { arrivals.RemoveAt(i); continue; }

                Vector3 to = PositionFor(depth);
                Quaternion toRotation = RotationFor(depth);

                Vector3 position = Vector3.Lerp(arrival.from, to, e);
                // Arc towards the camera (-Z) so the card rides over the pile it lands on.
                position.z -= Mathf.Sin(t * Mathf.PI) * flightArc;
                arrival.card.transform.SetPositionAndRotation(
                    position, Quaternion.Slerp(arrival.fromRotation, toRotation, e));
                arrival.card.transform.localScale = Vector3.Lerp(arrival.fromScale, Vector3.one, e);

                if (t < 1f) continue;

                arrivals.RemoveAt(i);
                RefreshColliders();
            }
        }

        // -------------------------------------------------------------------
        // Layout
        // -------------------------------------------------------------------
        Vector3 PositionFor(int index) => transform.TransformPoint(stackStep * Mathf.Min(index, visibleDepth));

        Quaternion RotationFor(int index)
        {
            if (still) return transform.rotation;

            // Deterministic per depth, so a card does not re-roll its lean.
            float angle = (Mathf.PerlinNoise(index * 3.7f, 0.5f) - 0.5f) * 2f * tiltJitter;
            return transform.rotation * Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>Is this world point inside the slot's catch area?</summary>
        public bool Catches(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(local.x) <= catchExtents.x && Mathf.Abs(local.y) <= catchExtents.y;
        }

        void RefreshColliders()
        {
            bool busy = arrivals.Count > 0;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;
                var collider = cards[i].GetComponent<Collider>();
                if (collider != null) collider.enabled = !still && !busy && i == cards.Count - 1;
            }
        }

        void PushFrame()
        {
            if (frame == null) return;
            block ??= new MaterialPropertyBlock();
            frame.GetPropertyBlock(block);
            block.SetColor(ColorId, isDefault ? defaultColor : restColor);
            block.SetFloat(HighlightId, highlight);
            frame.SetPropertyBlock(block);
        }

#if UNITY_EDITOR
        public void EditorBind(MeshRenderer slotFrame, string slotLabel, bool asDefault,
                               int slotCapacity = 0, float catchWidth = 0f, float catchHeight = 0f,
                               bool holdsStill = false)
        {
            frame = slotFrame;
            label = slotLabel;
            isDefault = asDefault;
            capacity = slotCapacity;
            still = holdsStill;
            if (catchWidth > 0f && catchHeight > 0f) catchExtents = new Vector2(catchWidth, catchHeight);
        }
#endif
    }
}
