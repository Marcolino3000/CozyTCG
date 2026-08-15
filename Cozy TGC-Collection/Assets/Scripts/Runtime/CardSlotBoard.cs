using System.Collections.Generic;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// The row of piles along the bottom of the screen. Owns which slot is the
    /// default (where a plain click sends cards) and works out which one a
    /// dragged card is hovering over.
    /// </summary>
    [DisallowMultipleComponent]
    public class CardSlotBoard : MonoBehaviour
    {
        [SerializeField] List<CardSlot> slots = new List<CardSlot>();

        public IReadOnlyList<CardSlot> Slots => slots;

        /// <summary>Where a click sends the next card. Falls back to the first slot.</summary>
        public CardSlot Default
        {
            get
            {
                foreach (var slot in slots)
                    if (slot != null && slot.IsDefault) return slot;
                return slots.Count > 0 ? slots[0] : null;
            }
        }

        public int TotalCards
        {
            get
            {
                int total = 0;
                foreach (var slot in slots) if (slot != null) total += slot.Count;
                return total;
            }
        }

        /// <summary>
        /// Slot under the pointer, or null. The slots' catch areas tile without
        /// gaps, so anywhere along the row lands somewhere and only a drop above
        /// the row counts as a miss.
        /// </summary>
        public CardSlot Find(Ray ray)
        {
            var plane = new Plane(transform.forward, transform.position);
            if (!plane.Raycast(ray, out float enter)) return null;

            Vector3 point = ray.GetPoint(enter);
            foreach (var slot in slots)
                if (slot != null && slot.Catches(point)) return slot;
            return null;
        }

        /// <summary>Slot under the pointer that could actually take a card right now.</summary>
        public CardSlot FindDropTarget(Ray ray)
        {
            var slot = Find(ray);
            return slot != null && slot.CanAccept ? slot : null;
        }

        public void Highlight(CardSlot target)
        {
            foreach (var slot in slots)
                if (slot != null) slot.SetHighlighted(slot == target);
        }

        public void ClearAll()
        {
            foreach (var slot in slots)
                if (slot != null) slot.Clear();
        }

#if UNITY_EDITOR
        public void EditorBind(List<CardSlot> slotList)
        {
            slots = slotList;
        }
#endif
    }
}
