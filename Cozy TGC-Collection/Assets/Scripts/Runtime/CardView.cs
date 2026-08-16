using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// Drives a single card: hover tilt, free spin while dragging and flipping
    /// front/back. The root object stays put (stable pointer maths and hover
    /// detection), everything that moves happens on the child visual.
    /// </summary>
    [DisallowMultipleComponent]
    public class CardView : MonoBehaviour
    {
        static readonly int FrontTexId = Shader.PropertyToID("_FrontTex");
        static readonly int BackTexId = Shader.PropertyToID("_BackTex");
        static readonly int SparkleSeedId = Shader.PropertyToID("_SparkleSeed");

        [Header("References")]
        [SerializeField] Transform visual;
        [SerializeField] MeshRenderer meshRenderer;

        [Header("Card")]
        [Tooltip("World size of the card face in units. 73x113 px at 100 PPU = 0.73 x 1.13.")]
        [SerializeField] Vector2 size = new Vector2(0.73f, 1.13f);

        [Header("Hover Tilt")]
        [SerializeField] float maxTilt = 14f;
        [SerializeField] float tiltStiffness = 260f;
        [SerializeField] float tiltDamping = 16f;

        [Header("Hover Pop")]
        [SerializeField] float hoverLift = 0.1f;
        [SerializeField] float hoverPush = 0.12f;
        [SerializeField] float hoverScale = 1.07f;
        [SerializeField] float hoverSpeed = 12f;
        [Tooltip("Extra margin added to the hover region while the card is popped, so the " +
                 "cursor cannot fall off the enlarged card and drop it out of hover.")]
        [SerializeField] float hoverPadding = 0.04f;

        [Header("Drag Spin")]
        [Tooltip("Degrees of rotation per screen pixel dragged.")]
        [SerializeField] float dragSensitivity = 0.45f;
        [SerializeField] float maxPitch = 85f;
        [SerializeField] float releaseSpin = 0.6f;

        [Header("Idle")]
        [SerializeField] float idleSway = 2.5f;
        [SerializeField] float idleSpeed = 0.55f;
        [Tooltip("Resting sway. A card sitting in a stack should hold still; a card " +
                 "on its own reads as dead without it.")]
        [SerializeField] bool idleMotion = true;

        [Header("Stacking")]
        [Tooltip("Lift a turning card out of the pile by its own depth swing, so the card " +
                 "underneath can never poke through it. On for stacked cards, off for cards " +
                 "that stand alone.")]
        [SerializeField] bool stackClearance;
        [Tooltip("Headroom on top of the measured swing.")]
        [SerializeField] float clearanceSlack = 1.1f;

        float yaw, pitch;
        float yawVel, pitchVel;
        float restYaw;
        float idlePhase;

        Vector2 pointer;
        Vector2 dragAccum;
        bool hovered;
        bool dragging;
        float hoverBlend;

        BoxCollider box;
        Vector3 visualOffset;
        float visualScale = 1f;

        MaterialPropertyBlock block;

        /// <summary>
        /// What this card is. Set once, when the card is made - the face texture and
        /// the rarity material are both invisible to anything holding the card later,
        /// so this is what the shop matches its wanted ads against.
        /// </summary>
        public CardIdentity Identity { get; set; } = CardIdentity.None;

        /// <summary>
        /// The back of the deck this card was printed in, which is not always the back
        /// it is showing: a card in a booster wears the pack's back so that the stack
        /// gives nothing away, and puts this one on as it is dealt out.
        /// </summary>
        public Texture2D DeckBack { get; set; }

        public bool ShowingBack { get; private set; }
        public bool IsDragging => dragging;
        public MeshRenderer Renderer => meshRenderer;
        public Vector2 Size => size;

        void Awake()
        {
            if (visual == null) visual = transform.childCount > 0 ? transform.GetChild(0) : transform;
            if (meshRenderer == null) meshRenderer = GetComponentInChildren<MeshRenderer>();
            box = GetComponent<BoxCollider>();
            idlePhase = Random.value * 100f;
            SetSparkleSeed(Random.Range(0f, 100f));
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            if (dragging)
            {
                float dYaw = dragAccum.x * dragSensitivity;
                float dPitch = -dragAccum.y * dragSensitivity;
                dragAccum = Vector2.zero;

                yaw += dYaw;
                pitch = Mathf.Clamp(pitch + dPitch, -maxPitch, maxPitch);

                if (dt > 0f)
                {
                    yawVel = Mathf.Lerp(yawVel, dYaw / dt, 0.5f);
                    pitchVel = Mathf.Lerp(pitchVel, dPitch / dt, 0.5f);
                }
            }
            else
            {
                float sway = hovered || !idleMotion ? 0f : idleSway;
                float targetYaw = restYaw
                                  + (hovered ? pointer.x * maxTilt : 0f)
                                  + Mathf.Sin((Time.time + idlePhase) * idleSpeed) * sway;
                float targetPitch = (hovered ? -pointer.y * maxTilt : 0f)
                                    + Mathf.Cos((Time.time + idlePhase) * idleSpeed * 0.8f) * sway * 0.5f;

                int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
                float sub = dt / steps;
                for (int i = 0; i < steps; i++)
                {
                    Spring(ref yaw, ref yawVel, targetYaw, tiltStiffness, tiltDamping, sub);
                    Spring(ref pitch, ref pitchVel, targetPitch, tiltStiffness, tiltDamping, sub);
                }
            }

            hoverBlend = Mathf.Lerp(hoverBlend, hovered || dragging ? 1f : 0f, 1f - Mathf.Exp(-hoverSpeed * dt));

            visualScale = Mathf.Lerp(1f, hoverScale, hoverBlend);

            // The card face points along -Z (see CardDemoBuilder.BuildCardMesh), so a
            // hovered card moves along -Z to come towards the viewer.
            float push = hoverPush * hoverBlend;
            if (stackClearance) push = Mathf.Max(push, DepthSwing() * clearanceSlack);
            visualOffset = new Vector3(0f, hoverLift * hoverBlend, -push);

            visual.localRotation = Quaternion.Euler(pitch, yaw, 0f);
            visual.localPosition = visualOffset;
            visual.localScale = Vector3.one * visualScale;

            SyncCollider();
        }

        /// <summary>
        /// How far the card's furthest corner reaches behind its rest plane at the
        /// current rotation. For Euler(pitch, yaw, 0) a local point (x, y, 0) lands at
        /// z = -sin(yaw)*x + cos(yaw)*sin(pitch)*y, so the worst corner is the sum of
        /// both terms at the half extents. Pile steps are a hair over a centimetre,
        /// while a card tilted 14 degrees swings more than ten times that - without
        /// lifting it out first, the card below cuts straight through it.
        /// </summary>
        float DepthSwing()
        {
            float yawRad = yaw * Mathf.Deg2Rad;
            float pitchRad = pitch * Mathf.Deg2Rad;
            return Mathf.Abs(Mathf.Sin(yawRad)) * size.x * 0.5f * visualScale
                 + Mathf.Abs(Mathf.Cos(yawRad) * Mathf.Sin(pitchRad)) * size.y * 0.5f * visualScale;
        }

        /// <summary>
        /// Keeps the hover region on top of the card as it pops: the visual grows,
        /// lifts and moves towards the camera, and a rest-sized collider would let
        /// the cursor sit on the visible card while missing the hit box, dropping
        /// the card straight back out of hover. Stays axis aligned on purpose - a
        /// collider that tilted with the card would flicker at the edges.
        /// </summary>
        void SyncCollider()
        {
            if (box == null) return;

            float pad = hoverPadding * hoverBlend;
            // A tilted card sweeps in depth, so keep the slab thick enough that its
            // near corner still sits inside the hit box.
            float tiltSweep = size.y * 0.5f * visualScale * Mathf.Sin(maxTilt * Mathf.Deg2Rad) * hoverBlend;

            box.center = new Vector3(0f, visualOffset.y, visualOffset.z * 0.5f);
            box.size = new Vector3(size.x * visualScale + pad * 2f,
                                   size.y * visualScale + pad * 2f,
                                   Mathf.Abs(visualOffset.z) + tiltSweep + 0.02f);
        }

        static void Spring(ref float value, ref float velocity, float target, float stiffness, float damping, float dt)
        {
            velocity += (target - value) * stiffness * dt;
            velocity *= Mathf.Exp(-damping * dt);
            value += velocity * dt;
        }

        // -------------------------------------------------------------------
        // Interaction API
        // -------------------------------------------------------------------
        public void SetHovered(bool value)
        {
            hovered = value;
            if (!value) pointer = Vector2.zero;
        }

        /// <summary>Pointer position on the card, -1..1 on both axes.</summary>
        public void SetPointer(Vector2 normalized)
        {
            pointer = new Vector2(Mathf.Clamp(normalized.x, -1f, 1f), Mathf.Clamp(normalized.y, -1f, 1f));
        }

        public void BeginDrag()
        {
            dragging = true;
            dragAccum = Vector2.zero;
        }

        public void Drag(Vector2 screenDelta)
        {
            dragAccum += screenDelta;
        }

        public void EndDrag()
        {
            if (!dragging) return;
            dragging = false;

            // Let the throw carry into the settle, then snap to whichever face
            // ends up pointing at the camera.
            yaw += yawVel * releaseSpin * 0.1f;
            int half = Mathf.RoundToInt(yaw / 180f);
            restYaw = half * 180f;
            ShowingBack = (half & 1) != 0;
        }

        public void Flip()
        {
            restYaw += 180f;
            ShowingBack = !ShowingBack;
            yawVel += 120f;
        }

        /// <summary>
        /// Snaps to a face without playing the flip. For cards that are dealt
        /// face down, where animating the turn would just be a spin on spawn.
        /// </summary>
        public void SetFace(bool showBack)
        {
            restYaw = showBack ? 180f : 0f;
            yaw = restYaw;
            yawVel = 0f;
            ShowingBack = showBack;
        }

        /// <summary>Turns the resting sway on or off. Off for cards sitting in a stack.</summary>
        public void SetIdleMotion(bool value) => idleMotion = value;

        /// <summary>
        /// Turns depth clearance on or off. On for any card sharing a pile, so turning
        /// it lifts it clear of whatever is stacked behind it.
        /// </summary>
        public void SetStackClearance(bool value) => stackClearance = value;

        public void ResetRotation()
        {
            restYaw = 0f;
            yaw = 0f;
            pitch = 0f;
            yawVel = 0f;
            pitchVel = 0f;
            ShowingBack = false;
        }

        /// <summary>
        /// Projects a ray onto the card's rest plane. Uses the untilted root so
        /// the tilt cannot feed back into the pointer position.
        /// </summary>
        public bool TryGetLocalPointer(Ray ray, out Vector2 normalized)
        {
            normalized = Vector2.zero;
            // Follows the popped card's position and scale, but never its rotation,
            // so the tilt cannot feed back into the pointer position.
            var plane = new Plane(transform.forward, transform.TransformPoint(visualOffset));
            if (!plane.Raycast(ray, out float enter)) return false;

            Vector3 local = transform.InverseTransformPoint(ray.GetPoint(enter)) - visualOffset;
            normalized = new Vector2(local.x / (size.x * 0.5f * visualScale),
                                     local.y / (size.y * 0.5f * visualScale));
            return true;
        }

        // -------------------------------------------------------------------
        // Material overrides (per renderer, the shared material stays untouched)
        // -------------------------------------------------------------------
        /// <summary>
        /// Resolves the renderer on demand. CardWear can bind its maps from its own
        /// Awake, and script execution order between the two is not fixed - without
        /// this, a card whose wear arrived first would silently drop it.
        /// </summary>
        MeshRenderer Target
        {
            get
            {
                if (meshRenderer == null) meshRenderer = GetComponentInChildren<MeshRenderer>();
                return meshRenderer;
            }
        }

        public void SetFaces(Texture front, Texture back)
        {
            if (Target == null) return;
            block ??= new MaterialPropertyBlock();
            Target.GetPropertyBlock(block);
            if (front != null) block.SetTexture(FrontTexId, front);
            if (back != null) block.SetTexture(BackTexId, back);
            Target.SetPropertyBlock(block);
        }

        public void SetSparkleSeed(float seed)
        {
            SetFloat(SparkleSeedId, seed);
        }

        public void SetFloat(int propertyId, float value)
        {
            if (Target == null) return;
            block ??= new MaterialPropertyBlock();
            Target.GetPropertyBlock(block);
            block.SetFloat(propertyId, value);
            Target.SetPropertyBlock(block);
        }

        public void SetVector(int propertyId, Vector4 value)
        {
            if (Target == null) return;
            block ??= new MaterialPropertyBlock();
            Target.GetPropertyBlock(block);
            block.SetVector(propertyId, value);
            Target.SetPropertyBlock(block);
        }

        public void SetTexture(int propertyId, Texture value)
        {
            if (Target == null || value == null) return;
            block ??= new MaterialPropertyBlock();
            Target.GetPropertyBlock(block);
            block.SetTexture(propertyId, value);
            Target.SetPropertyBlock(block);
        }

        public float GetFloat(int propertyId, float fallback)
        {
            if (Target == null) return fallback;
            block ??= new MaterialPropertyBlock();
            Target.GetPropertyBlock(block);
            if (block.HasFloat(propertyId)) return block.GetFloat(propertyId);
            var mat = Target.sharedMaterial;
            return mat != null && mat.HasFloat(propertyId) ? mat.GetFloat(propertyId) : fallback;
        }

#if UNITY_EDITOR
        public void EditorBind(Transform cardVisual, MeshRenderer cardRenderer, Vector2 cardSize)
        {
            visual = cardVisual;
            meshRenderer = cardRenderer;
            size = cardSize;
        }
#endif
    }
}
