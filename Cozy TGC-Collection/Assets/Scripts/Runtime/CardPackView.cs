using System;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// A sealed booster wrapper that opens by dragging across its crimped top edge.
    /// The drag records how much of the seam it has swept and light runs out of the
    /// cut behind it; once the sweep covers the seam the cut goes off in a prismatic
    /// flash, the sealed strip is thrown clear of the frame, and the open wrapper
    /// holds for a beat before it drops away.
    ///
    /// Same split as CardView: the root never tilts, so pointer projection and the
    /// cut maths cannot feed back into themselves - the child visual does all the
    /// leaning.
    /// </summary>
    [DisallowMultipleComponent]
    public class CardPackView : MonoBehaviour
    {
        public enum State { Sealed, Tearing, Flash, Opening, Gone }

        static readonly int PackRectId = Shader.PropertyToID("_PackRect");
        static readonly int PeelMinId = Shader.PropertyToID("_PeelMin");
        static readonly int PeelMaxId = Shader.PropertyToID("_PeelMax");
        static readonly int PeelFlatId = Shader.PropertyToID("_PeelFlat");
        static readonly int PeelLiftId = Shader.PropertyToID("_PeelLift");
        static readonly int CutGlowId = Shader.PropertyToID("_CutGlow");
        static readonly int CutHeadId = Shader.PropertyToID("_CutHead");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int FlutterId = Shader.PropertyToID("_Flutter");
        static readonly int FlutterPhaseId = Shader.PropertyToID("_FlutterPhase");
        static readonly int FlutterWavesId = Shader.PropertyToID("_FlutterWaves");
        static readonly int SpanId = Shader.PropertyToID("_Span");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int HeadId = Shader.PropertyToID("_Head");
        static readonly int DimId = Shader.PropertyToID("_Dim");

        [Header("References")]
        [SerializeField] Camera cam;
        [SerializeField] Transform visual;
        [SerializeField] Transform lid;
        [SerializeField] MeshRenderer bodyRenderer;
        [SerializeField] MeshRenderer lidRenderer;
        [Tooltip("The light bar sitting on the seam. Its UVs are in pack space, so the " +
                 "bar tracks the cut without anything having to move or resize.")]
        [SerializeField] MeshRenderer flareRenderer;
        [Tooltip("Frame-wide darkener parked behind the pack, so everything further " +
                 "back drops away while the seam is being cut.")]
        [SerializeField] Transform dim;
        [SerializeField] MeshRenderer dimRenderer;

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
        [SerializeField] float peelLift = 0.055f;
        [SerializeField] float openPeelLift = 0.22f;
        [SerializeField] float peelSpeed = 14f;
        [Tooltip("Once the rip reaches a side of the pack the strip is free there, " +
                 "so the peel is pushed past the edge instead of staying pinned to it.")]
        [SerializeField] float peelEdgeRelease = 0.14f;

        [Header("Cut Light")]
        [Tooltip("Brightness of the light coming out of the seam at a finished sweep.")]
        [SerializeField] float cutGlow = 1.5f;
        [SerializeField] float glowSpeed = 12f;
        [Tooltip("Share of the cut light left on the opened wrapper's own edge, which " +
                 "holds until it drops out of the frame.")]
        [SerializeField] float openEdgeGlow = 0.7f;
        [Tooltip("Share of it left on the freed strip's edge, which lasts the whole flight.")]
        [SerializeField] float flightEdgeGlow = 0.85f;
        [Tooltip("How dark the rest of the table goes while the seam is being cut.")]
        [SerializeField] float dimAmount = 0.55f;
        [SerializeField] float dimSpeed = 7f;
        [Tooltip("How far behind the pack the darkener is parked. Anything nearer " +
                 "the camera than this is left alone, which is what keeps the pack lit.")]
        [SerializeField] float dimDepth = 0.35f;

        [Header("Flash")]
        [SerializeField] float flashDuration = 0.24f;
        [Tooltip("Fraction of the flash spent rising. The rest is the fall.")]
        [SerializeField] float flashAttack = 0.18f;

        [Header("Lid Flight")]
        [Tooltip("World velocity the freed strip is thrown with, in units per second. " +
                 "Sideways dominates on purpose: a sealed pack fills the frame top to " +
                 "bottom, so there is roughly four times as much room to cross as there " +
                 "is to climb, and a strip thrown straight up is gone before it reads.")]
        [SerializeField] Vector3 lidLaunch = new Vector3(1.9f, 1f, -0.25f);
        [SerializeField] Vector3 lidSpin = new Vector3(130f, 220f, 160f);
        [SerializeField] float lidGravity = 1.6f;
        [Tooltip("Hard stop, for a strip that somehow never leaves the frame.")]
        [SerializeField] float lidFlightTime = 1.6f;
        [Tooltip("How far past the edge of the frame the strip has to be to be done with.")]
        [SerializeField] float lidClearance = 0.5f;
        [Tooltip("How far the flying strip ripples, in world units.")]
        [SerializeField] float lidFlutter = 0.05f;
        [SerializeField] float lidFlutterWaves = 1.7f;
        [SerializeField] float lidFlutterSpeed = 2.4f;

        [Header("Slide Away")]
        [Tooltip("How long the opened wrapper stands there before it drops.")]
        [SerializeField] float bodyHold = 0.3f;
        [Tooltip("How far the wrapper is shoved down by the strip coming off it.")]
        [SerializeField] float bodyRecoil = 0.055f;
        [SerializeField] float slideDuration = 0.55f;
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

        /// <summary>U of the end of the sweep that moved last - where the cut is being made.</summary>
        float cutHead = 0.5f;

        /// <summary>
        /// A head far outside the pack, which is how the hot spot is switched off: once
        /// the seam has given there is no point being cut any more, and the whole edge
        /// should light evenly rather than keep a bright mark wherever the sweep
        /// happened to finish - least of all on a strip that is tumbling away.
        /// </summary>
        const float NoHead = -5f;

        float LiveHead => state == State.Tearing ? cutHead : NoHead;
        /// <summary>Light on the wrapper's own cut edge.</summary>
        float bodyGlow;
        /// <summary>Light on the freed strip's cut edge, which outlives the wrapper's.</summary>
        float lidGlow;
        float flash;
        float dimNow;
        float flashTime;
        float holdTime;
        float recoil;

        Transform lidParent;
        bool lidFlying;
        float lidTime;
        Vector3 lidVelocity;
        float flutterPhase;

        Vector2 pointer;
        bool hovered;
        Vector3 tilt;

        Vector3 slideFrom, slideTo;
        float slideTime;

        Vector3 lidRest;
        Vector3 visualRest;
        /// <summary>Rest pose within the rig that carries the pack, so reloading cannot fight it.</summary>
        Vector3 restLocalPosition;

        /// <summary>
        /// Raised once the flash has gone off and the strip is clear, which is when
        /// there is something to see inside the wrapper.
        /// </summary>
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
            lidParent = lid != null ? lid.parent : null;
            visualRest = visual != null ? visual.localPosition : Vector3.zero;
            restLocalPosition = transform.localPosition;
            idlePhase = UnityEngine.Random.value * 100f;

            // Park on a real cell rather than the whole sheet, so nothing can show
            // the atlas if the pack is drawn before Load picks a wrapper.
            packRect = CardPackSheet.UvRect(CardPackSheet.Load(), 0);
            PushMaterial();
        }

        void OnDisable()
        {
            // A strip thrown clear is parented to the world, so it would hang there
            // in mid air the moment this object stops running its own Update.
            ParkLid();
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
                case State.Flash:
                    Burn(dt);
                    break;
                case State.Opening:
                    HoldThenSlide(dt);
                    break;
            }

            FlyLid(dt);
            AnimateLift(dt);
            AnimateLight(dt);
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
            cutHead = 0.5f;
            peelFlat = 0f;
            currentLift = 0f;
            shake = 0f;
            slideTime = 0f;
            flashTime = 0f;
            holdTime = 0f;
            recoil = 0f;
            bodyGlow = 0f;
            lidGlow = 0f;
            flash = 0f;
            dimNow = 0f;
            hovered = false;
            pointer = Vector2.zero;

            transform.localPosition = restLocalPosition;
            if (visual != null) visual.localPosition = visualRest;
            ParkLid();
            if (lid != null) lid.gameObject.SetActive(true);
            if (bodyRenderer != null) bodyRenderer.enabled = true;
            if (flareRenderer != null) flareRenderer.enabled = false;
            if (dimRenderer != null) dimRenderer.enabled = false;

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
            cutHead = u;
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

            // Whichever end the sweep pushed out is where the wrapper is being cut,
            // and that is the end the light has to burn brightest at.
            if (u < tearMin) cutHead = u;
            else if (u > tearMax) cutHead = u;

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
        /// Cuts the seam the rest of the way with no drag, for opening a pack in one
        /// go. The sweep is filled in first rather than jumped over, so the flash goes
        /// off across the whole seam the way it does by hand. <see cref="Opened"/>
        /// still fires exactly once, when the strip comes clear.
        /// </summary>
        public void RipOpen()
        {
            if (state != State.Sealed && state != State.Tearing) return;

            tearMin = 0f;
            tearMax = 1f;
            cutHead = 0.5f;
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
            cutHead = Mathf.Clamp(cutHead, tearMin, tearMax);
        }

        void Open()
        {
            state = State.Flash;
            holding = false;
            flashTime = 0f;
            slideTime = 0f;
        }

        /// <summary>
        /// The flash. Straight up and a longer fall back, because this is an event
        /// rather than a fade - a symmetric curve reads as a lamp being turned on.
        /// The strip is thrown clear at the end of it, which is also where anything
        /// waiting on the pack is told it is open.
        /// </summary>
        void Burn(float dt)
        {
            flashTime += dt;
            float t = Mathf.Clamp01(flashTime / Mathf.Max(flashDuration, 0.01f));
            float attack = Mathf.Clamp(flashAttack, 0.01f, 0.99f);

            flash = t < attack
                ? t / attack
                : Mathf.Pow(1f - (t - attack) / (1f - attack), 2f);

            // The whole seam is cut by now, so the peel stops following the sweep.
            peelFlat = Mathf.MoveTowards(peelFlat, 1f, dt / 0.1f);
            recoil = Mathf.Lerp(recoil, bodyRecoil, 1f - Mathf.Exp(-18f * dt));

            if (t >= 1f) Launch();
        }

        void Launch()
        {
            state = State.Opening;
            flash = 0f;
            holdTime = 0f;
            DetachLid();
            Opened?.Invoke();
        }

        /// <summary>
        /// The opened wrapper stands there for a beat with its cut edge still lit,
        /// then drops out of the frame the way it always did.
        /// </summary>
        void HoldThenSlide(float dt)
        {
            recoil = Mathf.Lerp(recoil, 0f, 1f - Mathf.Exp(-9f * dt));

            holdTime += dt;
            if (holdTime < bodyHold) return;

            // Read where it is falling from at the moment it starts falling. The rig
            // the pack rides is already travelling back towards the table by now, so
            // a position taken at the flash would snap the wrapper backwards.
            if (slideTime <= 0f)
            {
                slideFrom = transform.position;
                slideTo = ExitPosition();
            }

            Slide(dt);
        }

        void Slide(float dt)
        {
            slideTime += dt;
            float t = Mathf.Clamp01(slideTime / Mathf.Max(slideDuration, 0.01f));
            // Accelerating rather than easing out: the wrapper is falling away,
            // not being parked somewhere.
            float e = t * t;

            transform.position = Vector3.Lerp(slideFrom, slideTo, e);

            if (t < 1f) return;

            // Gone is about the pack being out of the way, not about this object
            // being finished: a strip still in the air needs an Update to fly it.
            state = State.Gone;
            if (bodyRenderer != null) bodyRenderer.enabled = false;
            if (flareRenderer != null) flareRenderer.enabled = false;
            if (dimRenderer != null) dimRenderer.enabled = false;
            if (!lidFlying) gameObject.SetActive(false);
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
        float CameraBottom() => cam == null ? -2f : cam.transform.position.y - FrustumHalfHeight(transform.position.z);

        float FrustumHalfHeight(float z)
        {
            if (cam == null) return 2f;
            if (cam.orthographic) return cam.orthographicSize;
            float dist = Mathf.Abs(z - cam.transform.position.z);
            return dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        // -------------------------------------------------------------------
        // The freed strip
        // -------------------------------------------------------------------
        /// <summary>
        /// Takes the sealed strip off the pack entirely. It has to leave the
        /// hierarchy rather than just be animated in place: the body drops away
        /// underneath it and the rig they both ride pulls back towards the table, and
        /// a piece that was thrown clear must not be dragged along by either.
        /// </summary>
        void DetachLid()
        {
            if (lid == null || lidFlying) return;

            lid.SetParent(null, true);
            lidFlying = true;
            lidTime = 0f;
            lidVelocity = lidLaunch;
            flutterPhase = UnityEngine.Random.value * 10f;
        }

        void FlyLid(float dt)
        {
            if (!lidFlying || lid == null) return;

            lidTime += dt;
            lidVelocity += Vector3.down * (lidGravity * dt);
            lid.position += lidVelocity * dt;
            lid.Rotate(lidSpin * dt, Space.Self);
            flutterPhase += dt * lidFlutterSpeed;

            if (lidTime < lidFlightTime && !LidOffScreen()) return;

            ParkLid();
            if (state == State.Gone) gameObject.SetActive(false);
        }

        /// <summary>
        /// Has the strip left the frame? Tested on all four edges rather than just the
        /// top: it is thrown across the shot rather than out of it, so sideways is how
        /// it usually goes. Measured at its own depth, which is not the pack's - it
        /// drifts towards the camera as it tumbles.
        /// </summary>
        bool LidOffScreen()
        {
            if (cam == null) return true;

            float half = FrustumHalfHeight(lid.position.z);
            float slack = lidClearance + size.x * 0.5f;
            Vector3 p = lid.position - cam.transform.position;
            return Mathf.Abs(p.y) > half + slack || Mathf.Abs(p.x) > half * cam.aspect + slack;
        }

        /// <summary>Puts the strip back where the prefab has it, flying or not.</summary>
        void ParkLid()
        {
            if (lid == null)
            {
                lidFlying = false;
                return;
            }

            if (lidFlying)
            {
                lid.SetParent(lidParent, false);
                // It is out of the frame by now, and the next pack switches it back on.
                lid.gameObject.SetActive(false);
            }

            lid.localPosition = lidRest;
            lid.localRotation = Quaternion.identity;
            lidFlying = false;
        }

        // -------------------------------------------------------------------
        // Presentation
        // -------------------------------------------------------------------
        void AnimateLift(float dt)
        {
            float target = state == State.Flash || state == State.Opening ? openPeelLift
                         : state == State.Tearing ? peelLift
                         : 0f;
            currentLift = Mathf.Lerp(currentLift, target, 1f - Mathf.Exp(-peelSpeed * dt));
            shake = Mathf.Max(0f, shake - dt * 4f);
        }

        /// <summary>
        /// The light out of the seam, and the room going quiet behind it. Both are
        /// chased towards a target rather than set, so letting go of a half cut seam
        /// takes the glow back down with the reseal instead of snapping it off.
        /// </summary>
        void AnimateLight(float dt)
        {
            // The wrapper's cut edge stays lit for as long as the wrapper is standing
            // there, and only goes out as it drops - it is the thing being looked at
            // while the strip is in the air.
            float bodyTarget =
                state == State.Tearing ? cutGlow * (0.35f + 0.65f * TearProgress)
              : state == State.Flash ? cutGlow
              : state == State.Opening ? cutGlow * openEdgeGlow * (1f - Mathf.Clamp01(slideTime / Mathf.Max(slideDuration, 0.01f)))
              : 0f;
            bodyGlow = Mathf.Lerp(bodyGlow, bodyTarget, 1f - Mathf.Exp(-glowSpeed * dt));

            // The strip's edge is on its own clock, because the strip is: it is still
            // tumbling through the frame long after the pack it came off is Gone, and
            // the cut is what the eye follows on it.
            float lidTarget = lidFlying
                ? cutGlow * flightEdgeGlow * (1f - Mathf.Clamp01(lidTime / Mathf.Max(lidFlightTime, 0.01f)))
                : bodyTarget;
            lidGlow = Mathf.Lerp(lidGlow, lidTarget, 1f - Mathf.Exp(-glowSpeed * dt));

            // The dim comes up with the cut and is let go by the flash itself, so the
            // table is already back by the time the strip is in the air.
            float dimTarget =
                state == State.Tearing ? dimAmount * (0.3f + 0.7f * TearProgress)
              : state == State.Flash ? dimAmount * (1f - flash)
              : 0f;
            dimNow = Mathf.Lerp(dimNow, dimTarget, 1f - Mathf.Exp(-dimSpeed * dt));

            PushFlare();
            PushDim();
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
            visual.localPosition = visualRest + Vector3.down * recoil;
        }

        void PushFlare()
        {
            if (flareRenderer == null) return;

            bool lit = bodyGlow > 0.002f || flash > 0.002f;
            flareRenderer.enabled = lit;
            if (!lit) return;

            block ??= new MaterialPropertyBlock();
            flareRenderer.GetPropertyBlock(block);
            block.SetVector(SpanId, new Vector4(tearMin, tearMax, 0f, 0f));
            block.SetFloat(HeadId, LiveHead);
            block.SetFloat(GlowId, bodyGlow);
            block.SetFloat(FlashId, flash);
            flareRenderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// The darkener is a flat quad parked behind the pack, so it needs refitting
        /// to the frame every frame the pack moves. It is not depth tested away by
        /// the pack itself - it is simply further from the camera, which is what
        /// leaves the wrapper, and only the wrapper, at full brightness.
        /// </summary>
        void PushDim()
        {
            if (dimRenderer == null || dim == null) return;

            bool lit = dimNow > 0.002f;
            dimRenderer.enabled = lit;
            if (!lit || cam == null) return;

            float z = transform.position.z + dimDepth;
            float half = FrustumHalfHeight(z);
            dim.position = new Vector3(cam.transform.position.x, cam.transform.position.y, z);
            dim.localScale = new Vector3(half * 2f * cam.aspect, half * 2f, 1f);

            block ??= new MaterialPropertyBlock();
            dimRenderer.GetPropertyBlock(block);
            block.SetFloat(DimId, dimNow);
            dimRenderer.SetPropertyBlock(block);
        }

        void PushMaterial()
        {
            ApplyBlock(bodyRenderer, 0f, 0f, bodyGlow);
            ApplyBlock(lidRenderer, currentLift, lidFlying ? lidFlutter : 0f, lidGlow);
        }

        void ApplyBlock(MeshRenderer target, float lift, float flutter, float edgeGlow)
        {
            if (target == null) return;
            block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetVector(PackRectId, packRect);
            block.SetFloat(PeelMinId, tearMin <= 0.001f ? -peelEdgeRelease : tearMin);
            block.SetFloat(PeelMaxId, tearMax >= 0.999f ? 1f + peelEdgeRelease : tearMax);
            block.SetFloat(PeelFlatId, peelFlat);
            block.SetFloat(PeelLiftId, lift);
            block.SetFloat(CutGlowId, edgeGlow);
            block.SetFloat(CutHeadId, LiveHead);
            block.SetFloat(FlashId, flash);
            block.SetFloat(FlutterId, flutter);
            block.SetFloat(FlutterPhaseId, flutterPhase);
            block.SetFloat(FlutterWavesId, lidFlutterWaves);
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

        public void EditorBindLight(MeshRenderer flareMesh, Transform dimQuad, MeshRenderer dimMesh)
        {
            flareRenderer = flareMesh;
            dim = dimQuad;
            dimRenderer = dimMesh;
        }
#endif
    }
}
