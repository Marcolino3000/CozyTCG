using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// The drawer of unopened packs, hanging under the shelf of albums down the left
    /// of the frame. It is a slot frame like the ones the cards land in, with the
    /// wrapper of the pack you would open next sitting in it, and it holds however
    /// many packs have been bought.
    ///
    /// Clicking it is what puts a pack on the table - <see cref="PackOpeningController"/>
    /// owns the count and does the opening; this only draws the slot and answers the
    /// pointer. Hit testing is analytic, exactly like the shelf above it and the
    /// undrawn stack: it is a flat camera facing quad, so projecting the ray onto its
    /// plane beats fitting a collider to it.
    ///
    /// It pins itself under the shelf every frame rather than sitting at an authored
    /// height, because the shelf pins itself to an edge of the frame whose position
    /// depends on the aspect the game is running at.
    /// </summary>
    [DisallowMultipleComponent]
    public class PackTray : MonoBehaviour
    {
        static readonly int RectId = Shader.PropertyToID("_Rect");
        static readonly int HighlightId = Shader.PropertyToID("_Highlight");

        [SerializeField] MeshRenderer frame;
        [SerializeField] MeshRenderer wrapper;
        [Tooltip("Size of the wrapper picture in local units. 84x154 px at 100 PPU, " +
                 "scaled down to sit in the strip beside the album icons.")]
        [SerializeField] Vector2 packSize = new Vector2(0.3f, 0.55f);
        [Tooltip("Frame is the wrapper grown a little, so the pack sits inside it - " +
                 "the same relationship a slot has to the card that lands on it.")]
        [SerializeField] float frameScale = 1.1f;
        [Tooltip("Clearance below the last book on the shelf.")]
        [SerializeField] float gap = 0.12f;
        [SerializeField] float hoverPop = 1.12f;
        [SerializeField] float popSpeed = 12f;

        MaterialPropertyBlock block;
        Texture sheet;
        int packIndex;
        int count;
        float pop = 1f;
        float glow;
        bool hovered;

        /// <summary>How much of the frame the drawer takes, popped and framed.</summary>
        public float Height => packSize.y * frameScale * hoverPop;

        /// <summary>Under the drawer, where the controller prints how many packs are in it.</summary>
        public Vector3 LabelPosition =>
            transform.position + Vector3.down * (packSize.y * frameScale * 0.5f + 0.06f);

        void Awake()
        {
            // Off the material, like AlbumBook: the cell rects are pixel rects of this
            // very sheet, and a second reference to it could drift out of step.
            if (wrapper != null && wrapper.sharedMaterial != null) sheet = wrapper.sharedMaterial.mainTexture;
            Apply();
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float k = 1f - Mathf.Exp(-popSpeed * dt);

            bool lit = hovered && count > 0;
            pop = Mathf.Lerp(pop, lit ? hoverPop : 1f, k);
            glow = Mathf.Lerp(glow, lit ? 1f : 0f, k);
            Apply();
        }

        /// <summary>How many packs are in the drawer. An empty one shows the bare slot.</summary>
        public void SetCount(int value)
        {
            count = Mathf.Max(0, value);
            if (wrapper != null) wrapper.enabled = count > 0;
        }

        /// <summary>Which wrapper off the sheet is on top of the drawer.</summary>
        public void SetPack(int index)
        {
            packIndex = index;
            Apply();
        }

        public void SetHovered(bool value) => hovered = value;

        /// <summary>Is the pointer on the drawer?</summary>
        public bool Hits(Ray ray)
        {
            var plane = new Plane(transform.forward, transform.position);
            if (!plane.Raycast(ray, out float enter)) return false;

            Vector3 local = transform.InverseTransformPoint(ray.GetPoint(enter));
            Vector2 half = packSize * 0.5f * frameScale;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y;
        }

        /// <summary>Hangs the drawer under the shelf, on the shelf's own column.</summary>
        public void Layout(Camera cam, float centerX, float topY)
        {
            if (cam == null) return;
            transform.position = new Vector3(
                centerX, topY - gap - packSize.y * frameScale * 0.5f, transform.position.z);
        }

        void Apply()
        {
            block ??= new MaterialPropertyBlock();

            if (wrapper != null)
            {
                wrapper.transform.localScale = new Vector3(packSize.x * pop, packSize.y * pop, 1f);
                wrapper.GetPropertyBlock(block);
                block.SetVector(RectId, CardPackSheet.UvRect(sheet, packIndex));
                block.SetFloat(HighlightId, glow);
                wrapper.SetPropertyBlock(block);
            }

            if (frame == null) return;
            frame.transform.localScale = new Vector3(packSize.x * frameScale * pop,
                                                     packSize.y * frameScale * pop, 1f);
            frame.GetPropertyBlock(block);
            block.SetFloat(HighlightId, glow);
            frame.SetPropertyBlock(block);
        }

#if UNITY_EDITOR
        public void EditorBind(MeshRenderer slotFrame, MeshRenderer packWrapper, Vector2 size, float framing)
        {
            frame = slotFrame;
            wrapper = packWrapper;
            packSize = size;
            frameScale = framing;
        }

        /// <summary>Sizes the two quads in the scene, so the built scene reads before Play.</summary>
        public void EditorLayoutQuads()
        {
            if (wrapper != null) wrapper.transform.localScale = new Vector3(packSize.x, packSize.y, 1f);
            if (frame != null)
                frame.transform.localScale = new Vector3(packSize.x * frameScale, packSize.y * frameScale, 1f);
        }
#endif
    }
}
