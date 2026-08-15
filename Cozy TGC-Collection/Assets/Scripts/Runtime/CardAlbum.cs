using System.Collections.Generic;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// One collector album: a drawn book (<see cref="AlbumBook"/>) with a stack of
    /// sheets laid over its two open pages, shown two at a time as a spread. A
    /// sheet is just a CardSlotBoard whose pockets hold one card each, so dropping
    /// into a pocket is the same gesture as dropping into a pile at the bottom of
    /// the screen.
    ///
    /// There is one of these per book on the shelf, and each keeps its own cards -
    /// what is filed in one is not in the others.
    ///
    /// Only the two sheets of the open spread are active. That is what hides the
    /// cards filed elsewhere in the album, without anything having to reparent
    /// them. Even pages hang left of the spine, odd pages right of it, and the
    /// side of the spread that is clicked is the direction the album turns.
    ///
    /// The book animation is what the album's state follows: the sheets stay off
    /// until it has finished swinging open, and a page turn hides them for the
    /// length of the clip - which is also what covers the swap to the next spread.
    /// </summary>
    [DisallowMultipleComponent]
    public class CardAlbum : MonoBehaviour
    {
        enum State { Shut, Opening, Open, Turning, Closing }

        [SerializeField] string title = "Album";
        [SerializeField] AlbumBook book;
        [SerializeField] List<CardSlotBoard> pages = new List<CardSlotBoard>();
        [Tooltip("Half extents of a single sheet, in local units. This is the parchment " +
                 "the book draws, not a size of its own - see AlbumBookSheet.")]
        [SerializeField] Vector2 pageExtents = new Vector2(0.345f, 0.545f);
        [Tooltip("Distance from the spine to the middle of a sheet. The pages are laid " +
                 "out from this rather than from where they sit in the scene, so what is " +
                 "drawn and what is clicked cannot drift apart.")]
        [SerializeField] float pageOffset = 0.36f;
        [Tooltip("Size of the drawn book in local units - the art, not the cell it sits in. " +
                 "This is what gets fitted on screen; see AlbumBookSheet.")]
        [SerializeField] Vector2 frameSize = new Vector2(1.57f, 1.42f);
        [Tooltip("Middle of that art, measured from the spine.")]
        [SerializeField] Vector2 frameCenter = new Vector2(0f, 0.055f);

        State state = State.Shut;
        int spread;
        int pending;

        public string Title => title;

        /// <summary>Is the spread up and ready to be filed into? False while the book moves.</summary>
        public bool IsOpen => state == State.Open;

        /// <summary>
        /// Is the album on screen at all? True through the whole open and shut
        /// animation, which is what stops a click on a book that is still swinging
        /// from falling through to a pile behind it.
        /// </summary>
        public bool IsVisible => gameObject.activeSelf;

        public int SpreadCount => (pages.Count + 1) / 2;
        public int SpreadNumber => SpreadCount == 0 ? 0 : spread + 1;

        /// <summary>Size of a whole open spread in local units - the parchment, not the book around it.</summary>
        public Vector2 SpreadSize => new Vector2((pageOffset + pageExtents.x) * 2f, pageExtents.y * 2f);

        /// <summary>
        /// Size of the drawn book in local units, which is what has to fit on screen:
        /// covers and gilt corners reach well past the pages, so fitting the spread
        /// alone would push all of that off the edges of the frame.
        /// </summary>
        public Vector2 FrameSize => frameSize.x > 0f && frameSize.y > 0f ? frameSize : SpreadSize;

        /// <summary>
        /// Middle of the drawn book, measured from the spine the pages hang off. The
        /// art is not centred on its spine - the book opens leftwards off a right
        /// edge that stays put - so whoever centres it on screen corrects for this.
        /// </summary>
        public Vector2 FrameCenter => frameCenter;

        public CardSlotBoard LeftPage => PageAt(spread * 2);
        public CardSlotBoard RightPage => PageAt(spread * 2 + 1);

        /// <summary>
        /// Every sheet, not just the spread that is up. What is filed in a closed
        /// album still counts as owned, so the shop walks all of them.
        /// </summary>
        public IReadOnlyList<CardSlotBoard> Pages => pages;

        public int CardCount
        {
            get
            {
                int total = 0;
                foreach (var sheet in pages) if (sheet != null) total += sheet.TotalCards;
                return total;
            }
        }

        void Update()
        {
            if (state == State.Shut || state == State.Open) return;
            if (book != null && book.Playing) return;

            switch (state)
            {
                case State.Opening:
                    EnterOpen();
                    break;
                case State.Turning:
                    spread = pending;
                    EnterOpen();
                    break;
                case State.Closing:
                    state = State.Shut;
                    gameObject.SetActive(false);
                    break;
            }
        }

        public void Open()
        {
            gameObject.SetActive(true);
            if (state == State.Opening || state == State.Open) return;

            state = State.Opening;
            HideAllPages();
            if (book == null) EnterOpen();
            else book.Play(AlbumBook.Clip.Open);
        }

        /// <summary>
        /// Shuts the album. Instantly when something else is about to take the
        /// frame - two books swinging over each other reads as a glitch rather than
        /// as a transition - and otherwise by playing the open clip backwards, with
        /// the album still counting as visible until it has finished.
        /// </summary>
        public void Close(bool instant)
        {
            if (!gameObject.activeSelf)
            {
                state = State.Shut;
                return;
            }

            HideAllPages();

            if (instant || book == null)
            {
                if (book != null) book.Snap(AlbumBook.Clip.Shut);
                state = State.Shut;
                gameObject.SetActive(false);
                return;
            }

            state = State.Closing;
            book.Play(AlbumBook.Clip.Shut);
        }

        /// <summary>
        /// Steps one spread back (direction below zero) or forward. Stops at the
        /// covers rather than wrapping - an album that jumps from the last page
        /// back to the first hides how much of it is left. The new spread goes up
        /// when the page has landed, so the turning page is what covers the swap.
        /// </summary>
        public void Turn(int direction)
        {
            if (state != State.Open || SpreadCount == 0 || direction == 0) return;

            pending = Mathf.Clamp(spread + (direction < 0 ? -1 : 1), 0, SpreadCount - 1);
            if (pending == spread) return;

            state = State.Turning;
            HideAllPages();
            if (book == null)
            {
                spread = pending;
                EnterOpen();
                return;
            }
            book.Play(direction < 0 ? AlbumBook.Clip.TurnBack : AlbumBook.Clip.TurnForward);
        }

        void EnterOpen()
        {
            state = State.Open;
            ShowSpread(spread);
        }

        void HideAllPages()
        {
            foreach (var sheet in pages)
                if (sheet != null) sheet.gameObject.SetActive(false);
        }

        void ShowSpread(int index)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                if (pages[i] == null) continue;

                bool open = i / 2 == index;
                pages[i].gameObject.SetActive(open);
                if (!open) continue;

                // Depth is left alone: a page carries whatever the scene gave it.
                var placed = pages[i].transform.localPosition;
                pages[i].transform.localPosition =
                    new Vector3(i % 2 == 0 ? -pageOffset : pageOffset, 0f, placed.z);
            }
        }

        CardSlotBoard PageAt(int index) => index >= 0 && index < pages.Count ? pages[index] : null;

        /// <summary>
        /// Which half of the open spread the pointer is on: -1 for the left page,
        /// +1 for the right, 0 for anywhere off the album. Pockets included - the
        /// whole page turns, unless the press picks a card up first.
        /// </summary>
        public int HitSide(Ray ray)
        {
            if (!IsVisible) return 0;

            var plane = new Plane(transform.forward, transform.position);
            if (!plane.Raycast(ray, out float enter)) return 0;

            Vector3 local = transform.InverseTransformPoint(ray.GetPoint(enter));
            if (Mathf.Abs(local.y) > pageExtents.y) return 0;
            if (Mathf.Abs(local.x) > pageOffset + pageExtents.x) return 0;
            return local.x < 0f ? -1 : 1;
        }

        /// <summary>Is the pointer over the open spread at all?</summary>
        public bool HitsPage(Ray ray) => HitSide(ray) != 0;

        /// <summary>Pocket under the pointer with room in it, or null.</summary>
        public CardSlot FindDropTarget(Ray ray)
        {
            if (!IsOpen) return null;

            var left = LeftPage;
            var slot = left != null ? left.FindDropTarget(ray) : null;
            if (slot != null) return slot;

            var right = RightPage;
            return right != null ? right.FindDropTarget(ray) : null;
        }

        /// <summary>Pocket under the pointer, full or not.</summary>
        public CardSlot FindSlot(Ray ray)
        {
            if (!IsOpen) return null;

            var left = LeftPage;
            var slot = left != null ? left.Find(ray) : null;
            if (slot != null) return slot;

            var right = RightPage;
            return right != null ? right.Find(ray) : null;
        }

        public void Highlight(CardSlot target)
        {
            // Both pages every time: a target on one of them has to clear the other.
            var left = LeftPage;
            if (left != null) left.Highlight(target);

            var right = RightPage;
            if (right != null) right.Highlight(target);
        }

#if UNITY_EDITOR
        public void EditorBind(string albumTitle, AlbumBook albumBook, List<CardSlotBoard> sheets,
                               Vector2 extents, float spineOffset, Vector2 artSize, Vector2 artCenter)
        {
            title = albumTitle;
            book = albumBook;
            pages = sheets;
            pageExtents = extents;
            pageOffset = spineOffset;
            frameSize = artSize;
            frameCenter = artCenter;
        }
#endif
    }
}
