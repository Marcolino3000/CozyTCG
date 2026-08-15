using System;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// A sealed booster wrapper that opens by dragging across its crimped top
    /// edge. The drag records how much of the seam it has swept; the strip peels
    /// open over exactly that span, and once the sweep covers the seam the whole
    /// wrapper slides out of the bottom of the frame.
    ///
    /// Same split as CardView: the root never tilts, so pointer projection and
    /// the tear maths cannot feed back into themselves - the child visual does
    /// all the leaning.
    /// </summary>
    [DisallowMultipleComponent]
    public class CardPackView : MonoBehaviour
    {
        public enum State { Sealed, Tearing, Opening, Gone }

        static readonly int PackRectId = Shader.PropertyToID("_PackRect");
        static readonly int PeelMinId = Shader.PropertyToID("_PeelMin");
        static readonly int PeelMaxId = Shader.PropertyToID("_PeelMax");
        static readonly int PeelFlatId = Shader.PropertyToID("_PeelFlat");
        static readonly int PeelLiftId = Shader.PropertyToID("_PeelLift");

        [Header("References")]
        [SerializeField] Camera cam;
        [SerializeField] Transform visual;
        [SerializeField] Transform lid;
        [SerializeField] MeshRenderer bodyRenderer;
        [SerializeField] MeshRenderer lidRenderer;

        [Header("Pack")]
        [Tooltip("World size of the wrapper in units. 84x154 px at 100 PPU = 0.84 x 1.54.")]
        [SerializeField] Vector2 size = new Vector2(0.84f, 1.54f);

        [Header("Tear Band")]
        [Tooltip("Normalised height (-1..1) the press has to land in to grab the seam.")]
        [SerializeField] float grabBandMin = 0.5f;
        [SerializeField] float grabBandMax = 1.45f;
        [Tooltip("Wider band the drag may wander through before the tear stops feeding.")]
        [SerializeField] float holdBandMin = 0.2f;
        [SerializeField] float holdBandMax = 2f;
        [SerializeField] float grabSideSlack = 0.3f;
        [Tooltip("Fraction of the pack's width that has to be swept to open it.")]
        [SerializeField] float tearSpan = 0.82f;
        [Tooltip("How fast a half finished tear closes back up after letting go.")]
        [SerializeField] float resealSpeed = 1.8f;

        [Header("Peel")]
        [SerializeField] float peelLift = 0.1f;
        [SerializeField] float openPeelLift = 0.3f;
        [SerializeField] float peelSpeed = 14f;
        [Tooltip("Once the rip reaches a side of the pack the strip is free there, " +
                 "so the peel is pushed past the edge instead of staying pinned to it.")]
        [SerializeField] float peelEdgeRelease = 0.14f;

        [Header("Slide Away")]
        [SerializeField] float slideDuration = 0.6f;
        [Tooltip("Extra clearance below the frame before the pack is switched off.")]
        [SerializeField] float slideClearance = 0.4f;

        [Header("Life")]
        [SerializeField] float maxTilt = 9f;
        [SerializeField] float tiltSpeed = 10f;
        [SerializeField] float idleSway = 1.6f;
        [SerializeField] float idleSpeed = 0.5f;
        [Tooltip("How hard the wrapper shakes while the seam is being ripped.")]
        [SerializeField] float tearShake = 2.2f;

        MaterialPropertyBlock block;
        Vector4 packRect = new Vector4(0f, 0f, 1f, 1f);

        State state = State.Sealed;
        float tearMin = 0.5f, tearMax = 0.5f;
        bool holding;
        float peelFlat;
        float currentLift;
        float shake;
        float idlePhase;

        Vector2 pointer;
        bool hovered;
        Vector3 tilt;

        Vector3 slideFrom, slideTo;
        float slideTime;

        Vector3 lidRest;
        /// <summary>Rest pose within the rig that carries the pack, so reloading cannot fight it.</summary>
        Vector3 restLocalPosition;

        /// <summary>Raised the moment the seam gives way and the wrapper starts to slide off.</summary>
        public event Action Opened;

        public State Current => state;
        public int PackIndex { get; private set; }
        public Vector2 Size => size;
        public bool IsTearing => state == State.Tearing;

        /// <summary>0..1 across the seam. Reaching 1 opens the pack.</summary>
        public float TearProgress => Mathf.Clamp01((tearMax - tearMin) / Mathf.Max(tearSpan, 0.05f));

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (visual == null) visual = transform.childCount > 0 ? transform.GetChild(0) : transform;
            lidRest = lid != null ? lid.localPosition : Vector3.zero;
            restLocalPosition = transform.localPosition;
            idlePhase = UnityEngine.Random.value * 100f;

            // Park on a real cell rather than the whole sheet, so nothing can show
            // the atlas if the pack is drawn before Load picks a wrapper.
            packRect = CardPackSheet.UvRect(CardPackSheet.Load(), 0);
            PushMaterial();
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            switch (state)
            {
                case State.Sealed:
                    Reseal(dt);
                    break;
                case State.Tearing:
                    if (!holding) Reseal(dt);
                    break;
                case State.Opening:
                    Slide(dt);
                    break;
            }

            AnimateLift(dt);
            AnimateTilt(dt);
            PushMaterial();
        }

        // -------------------------------------------------------------------
        // Setup
        // -------------------------------------------------------------------
        /// <summary>Resets the wrapper to sealed and puts one of the sheet's packs on it.</summary>
        public void Load(int packIndex, Texture sheet)
        {
            PackIndex = Mathf.Clamp(packIndex, 0, CardPackSheet.Count - 1);
            packRect = CardPackSheet.UvRect(sheet, PackIndex);

            state = State.Sealed;
            holding = false;
            tearMin = tearMax = 0.5f;
            peelFlat = 0f;
            currentLift = 0f;
            shake = 0f;
            slideTime = 0f;
            hovered = false;
            pointer = Vector2.zero;

            transform.localPosition = restLocalPosition;
            if (lid != null)
            {
                lid.localPosition = lidRest;
                lid.localRotation = Quaternion.identity;
            }

            gameObject.SetActive(true);
            PushMaterial();
        }

        // -------------------------------------------------------------------
        // Interaction
        // -------------------------------------------------------------------
        public void SetHovered(bool value)
        {
            hovered = value;
            if (!value) pointer = Vector2.zero;
        }

        /// <summary>Pointer position on the wrapper, -1..1 on both axes.</summary>
        public void SetPointer(Vector2 normalized) => pointer = normalized;

        public bool CanGrab(Vector2 local)
        {
            if (state != State.Sealed && state != State.Tearing) return false;
            return local.y >= grabBandMin && local.y <= grabBandMax
                   && Mathf.Abs(local.x) <= 1f + grabSideSlack;
        }

        public void BeginTear(Vector2 local)
        {
            if (!CanGrab(local)) return;

            float u = ToU(local.x);
            if (state != State.Tearing)
            {
                state = State.Tearing;
                tearMin = tearMax = u;
            }
            else
            {
                // Grabbing again part way through carries on from where it stopped.
                tearMin = Mathf.Min(tearMin, u);
                tearMax = Mathf.Max(tearMax, u);
            }
            holding = true;
        }

        public void UpdateTear(Vector2 local)
        {
            if (state != State.Tearing || !holding) return;

            // Wandering off the seam does not cancel the tear, it just stops
            // feeding it - the rip stalls until the cursor comes back to the top.
            if (local.y < holdBandMin || local.y > holdBandMax) return;

            float u = ToU(local.x);
            float before = tearMax - tearMin;
            tearMin = Mathf.Min(tearMin, u);
            tearMax = Mathf.Max(tearMax, u);
            shake = Mathf.Min(shake + (tearMax - tearMin - before) * 6f, 1f);

            if (TearProgress >= 1f) Open();
        }

        public void ReleaseTear()
        {
            holding = false;
        }

        /// <summary>
        /// Rips the seam the rest of the way with no drag, for opening a pack in one
        /// go. The tear is filled in first rather than jumped over, so the strip is
        /// fully peeled as the wrapper falls and it comes off looking the way it does
        /// by hand. <see cref="Opened"/> fires exactly once, as ever.
        /// </summary>
        public void RipOpen()
        {
            if (state != State.Sealed && state != State.Tearing) return;

            tearMin = 0f;
            tearMax = 1f;
            Open();
        }

        static float ToU(float localX) => Mathf.Clamp01(localX * 0.5f + 0.5f);

        /// <summary>
        /// Projects a ray onto the wrapper's rest plane. Uses the untilted root so
        /// the lean cannot feed back into the pointer position.
        /// </summary>
        public bool TryGetLocalPointer(Ray ray, out Vector2 normalized)
        {
            normalized = Vector2.zero;
            var plane = new Plane(transform.forward, transform.position);
            if (!plane.Raycast(ray, out float enter)) return false;

            Vector3 local = transform.InverseTransformPoint(ray.GetPoint(enter));
            normalized = new Vector2(local.x / (size.x * 0.5f), local.y / (size.y * 0.5f));
            return true;
        }

        // -------------------------------------------------------------------
        // States
        // -------------------------------------------------------------------
        void Reseal(float dt)
        {
            if (tearMax - tearMin <= 0.0005f)
            {
                state = State.Sealed;
                return;
            }
            float mid = (tearMin + tearMax) * 0.5f;
            float step = resealSpeed * dt;
            tearMin = Mathf.MoveTowards(tearMin, mid, step);
            tearMax = Mathf.MoveTowards(tearMax, mid, step);
        }

        void Open()
        {
            state = State.Opening;
            holding = false;
            slideTime = 0f;
            slideFrom = transform.position;
            slideTo = ExitPosition();
            Opened?.Invoke();
        }

        void Slide(float dt)
        {
            slideTime += dt;
            float t = Mathf.Clamp01(slideTime / Mathf.Max(slideDuration, 0.01f));
            // Accelerating rather than easing out: the wrapper is falling away,
            // not being parked somewhere.
            float e = t * t;

            transform.position = Vector3.Lerp(slideFrom, slideTo, e);
            peelFlat = Mathf.MoveTowards(peelFlat, 1f, dt / 0.16f);

            if (lid != null)
            {
                // The freed strip hinges on the seam and flicks up and forward as
                // the rest of the wrapper drops out from under it.
                lid.localPosition = lidRest + new Vector3(0f, 0.05f * e, -0.14f * e);
                lid.localRotation = Quaternion.Euler(-42f * e, 0f, 7f * e);
            }

            if (t >= 1f)
            {
                state = State.Gone;
                gameObject.SetActive(false);
            }
        }

        Vector3 ExitPosition()
        {
            Vector3 exit = transform.position;
            exit.y = CameraBottom() - size.y - slideClearance;
            return exit;
        }

        /// <summary>
        /// Bottom of the frame at the depth the pack is falling at. The camera is
        /// fixed in this scene, so the pack's own frustum reading is the right one -
        /// it only has to be taken at the depth it actually rides, which is nearer
        /// the camera than its resting place while the wrapper is still sealed.
        /// </summary>
        float CameraBottom()
        {
            if (cam == null) return -2f;
            float dist = Mathf.Abs(transform.position.z - cam.transform.position.z);
            float half = cam.orthographic
                ? cam.orthographicSize
                : dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return cam.transform.position.y - half;
        }

        // -------------------------------------------------------------------
        // Presentation
        // -------------------------------------------------------------------
        void AnimateLift(float dt)
        {
            float target = state == State.Opening ? openPeelLift
                         : state == State.Tearing ? peelLift
                         : 0f;
            currentLift = Mathf.Lerp(currentLift, target, 1f - Mathf.Exp(-peelSpeed * dt));
            shake = Mathf.Max(0f, shake - dt * 4f);
        }

        void AnimateTilt(float dt)
        {
            if (visual == null) return;

            float sway = hovered || state != State.Sealed ? 0f : idleSway;
            var target = new Vector3(
                (hovered ? -pointer.y * maxTilt : 0f) + Mathf.Cos((Time.time + idlePhase) * idleSpeed * 0.8f) * sway * 0.5f,
                (hovered ? pointer.x * maxTilt : 0f) + Mathf.Sin((Time.time + idlePhase) * idleSpeed) * sway,
                0f);

            if (shake > 0f)
            {
                float wobble = Mathf.Sin(Time.time * 46f) * tearShake * shake;
                target.z += wobble;
                target.x += wobble * 0.35f;
            }

            tilt = Vector3.Lerp(tilt, target, 1f - Mathf.Exp(-tiltSpeed * dt));
            visual.localRotation = Quaternion.Euler(tilt);
        }

        void PushMaterial()
        {
            ApplyBlock(bodyRenderer, 0f);
            ApplyBlock(lidRenderer, currentLift);
        }

        void ApplyBlock(MeshRenderer target, float lift)
        {
            if (target == null) return;
            block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetVector(PackRectId, packRect);
            block.SetFloat(PeelMinId, tearMin <= 0.001f ? -peelEdgeRelease : tearMin);
            block.SetFloat(PeelMaxId, tearMax >= 0.999f ? 1f + peelEdgeRelease : tearMax);
            block.SetFloat(PeelFlatId, peelFlat);
            block.SetFloat(PeelLiftId, lift);
            target.SetPropertyBlock(block);
        }

#if UNITY_EDITOR
        public void EditorBind(Camera camera, Transform packVisual, Transform packLid,
                               MeshRenderer body, MeshRenderer strip, Vector2 packSize)
        {
            cam = camera;
            visual = packVisual;
            lid = packLid;
            bodyRenderer = body;
            lidRenderer = strip;
            size = packSize;
        }
#endif
    }
}
