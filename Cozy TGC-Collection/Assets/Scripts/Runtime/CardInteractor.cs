using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CozyTGC
{
    /// <summary>
    /// Turns pointer input into hover / tilt / drag-spin / flip on the cards.
    /// One instance per scene, sitting next to the camera.
    /// </summary>
    public class CardInteractor : MonoBehaviour
    {
        public enum DragBehaviour
        {
            /// <summary>Dragging turns the card in place.</summary>
            Spin,
            /// <summary>
            /// Dragging belongs to someone else - let go of the press instead of
            /// spinning, and do not treat the release as a click. For scenes where
            /// a drag moves the card rather than turning it.
            /// </summary>
            HandOff,
        }

        [SerializeField] Camera cam;
        [SerializeField] LayerMask cardMask = ~0;
        [SerializeField] float maxDistance = 100f;
        [Tooltip("Pointer travel below this counts as a click (flip) instead of a drag.")]
        [SerializeField] float dragThreshold = 6f;
        [Tooltip("What a drag on a card does. Whoever picks the gesture up in HandOff " +
                 "mode should use the same threshold, or a drag between the two lands " +
                 "in a dead zone that neither acts on.")]
        [SerializeField] DragBehaviour onDrag = DragBehaviour.Spin;

        CardView hovered;
        CardView pressedCard;
        Vector2 lastPointer;
        Vector2 pressPointer;
        bool wasPressed;
        bool dragStarted;
        bool blocked;

        public CardView Hovered => hovered;

        void Awake()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
        }

        void Update()
        {
            if (cam == null) return;

            Vector2 pointer = PointerPosition();
            bool pressed = PointerPressed();

            if (blocked)
            {
                // Still tracked while something else has the pointer, so letting go
                // over the panel is not read as a click the moment it closes.
                SetHovered(null);
                CancelPress();
                wasPressed = pressed;
                lastPointer = pointer;
                return;
            }

            bool down = pressed && !wasPressed;
            bool up = !pressed && wasPressed;
            Vector2 delta = pointer - lastPointer;

            Ray ray = cam.ScreenPointToRay(pointer);
            CardView under = null;
            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, cardMask, QueryTriggerInteraction.Collide))
                under = hit.collider.GetComponentInParent<CardView>();

            if (pressedCard == null)
                SetHovered(under);

            if (hovered != null && hovered.TryGetLocalPointer(ray, out Vector2 local))
                hovered.SetPointer(local);

            if (down && under != null)
            {
                pressedCard = under;
                pressPointer = pointer;
                dragStarted = false;
            }

            if (pressedCard != null && pressed)
            {
                if (!dragStarted && (pointer - pressPointer).magnitude > dragThreshold)
                {
                    dragStarted = true;
                    if (onDrag == DragBehaviour.Spin) pressedCard.BeginDrag();
                    // Handing the gesture over: drop the press so the release is not
                    // read as a click and the card does not flip under whoever took it.
                    else pressedCard = null;
                }
                if (dragStarted && pressedCard != null) pressedCard.Drag(delta);
            }

            if (up)
            {
                if (pressedCard != null)
                {
                    if (dragStarted) pressedCard.EndDrag();
                    else pressedCard.Flip();
                }
                pressedCard = null;
                dragStarted = false;
            }

            wasPressed = pressed;
            lastPointer = pointer;
        }

        /// <summary>
        /// Lets go of the press without acting on it - no flip, no spin - for when
        /// something else claims the gesture. Hover is untouched. Call it every frame
        /// the claim holds: script execution order against the claimant is not fixed,
        /// so a single call on the press frame may land before the press is even seen.
        /// </summary>
        public void CancelPress()
        {
            pressedCard = null;
            dragStarted = false;
        }

        /// <summary>
        /// Takes the cards out of the pointer's reach entirely - hover included, which
        /// is what separates this from <see cref="CancelPress"/>. For a panel drawn
        /// over the scene: a card that lifts and tilts behind an open shop is answering
        /// a pointer that is not talking to it.
        /// </summary>
        public void SetBlocked(bool value)
        {
            if (blocked == value) return;
            blocked = value;
            if (!blocked) return;

            SetHovered(null);
            CancelPress();
        }

        void SetHovered(CardView card)
        {
            if (hovered == card) return;
            if (hovered != null) hovered.SetHovered(false);
            hovered = card;
            if (hovered != null) hovered.SetHovered(true);
        }

        // -------------------------------------------------------------------
        // Input plumbing - works with the new Input System or the old manager.
        // -------------------------------------------------------------------
        public static Vector2 PointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
                return touch.primaryTouch.position.ReadValue();
            var mouse = Mouse.current;
            return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        public static bool PointerPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed) return true;
            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.isPressed;
#else
            return Input.GetMouseButton(0);
#endif
        }

#if UNITY_EDITOR
        public void EditorBind(Camera camera, DragBehaviour behaviour = DragBehaviour.Spin)
        {
            cam = camera;
            onDrag = behaviour;
        }
#endif
    }
}
