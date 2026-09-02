using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// Switches the three geometry views on the card on and off: its triangle
    /// edges, its vertices, and the tangent frame at a grid of points across it.
    ///
    /// It holds no copy of the bend. CardOverlay.shader includes CardWear.hlsl and
    /// runs the very same CardApplyBend the card runs, so the wireframe of a bowed
    /// card is the bow itself. All this component does is hand those three
    /// renderers the values CardWear wrote onto the card - a MaterialPropertyBlock
    /// belongs to one renderer, and these are three other ones.
    /// </summary>
    [DisallowMultipleComponent]
    public class CardMeshOverlay : MonoBehaviour
    {
        static readonly int BowId = Shader.PropertyToID("_Bow");
        static readonly int CornerBendId = Shader.PropertyToID("_CornerBend");
        static readonly int WearTexId = Shader.PropertyToID("_WearTex");
        static readonly int WearAmountId = Shader.PropertyToID("_WearAmount");

        [SerializeField] CardWear wear;
        [Tooltip("Every triangle edge, as MeshTopology.Lines.")]
        [SerializeField] MeshRenderer wire;
        [Tooltip("One small square per vertex.")]
        [SerializeField] MeshRenderer points;
        [Tooltip("T, B and N at a grid of sample points.")]
        [SerializeField] MeshRenderer frames;

        MaterialPropertyBlock block;

        public bool WireOn => wire != null && wire.enabled;
        public bool PointsOn => points != null && points.enabled;
        public bool FramesOn => frames != null && frames.enabled;

        void Awake()
        {
            if (wear == null) wear = GetComponent<CardWear>();
            Show(false, false, false);
        }

        public void Show(bool showWire, bool showPoints, bool showFrames)
        {
            if (wire != null) wire.enabled = showWire;
            if (points != null) points.enabled = showPoints;
            if (frames != null) frames.enabled = showFrames;
        }

        /// <summary>
        /// After CardWear has moved, before the frame is drawn. The values come off
        /// CardWear rather than out of the card's own property block, because a
        /// block is write-mostly: reading one back to copy it would make a second
        /// source of truth for something that already has one.
        /// </summary>
        void LateUpdate()
        {
            if (wear == null) return;
            if (!WireOn && !PointsOn && !FramesOn) return;

            var bow = new Vector4(wear.Bow.x, wear.Bow.y, 0f, 0f);
            Vector4 corners = wear.CornerBend;
            Texture map = wear.FrontMap;
            // The creases only displace while there is a map to read them out of.
            float amount = wear.IsWorn && map != null ? 1f : 0f;

            Push(wire, bow, corners, map, amount);
            Push(points, bow, corners, map, amount);
            Push(frames, bow, corners, map, amount);
        }

        void Push(MeshRenderer renderer, Vector4 bow, Vector4 corners, Texture map, float amount)
        {
            if (renderer == null || !renderer.enabled) return;
            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetVector(BowId, bow);
            block.SetVector(CornerBendId, corners);
            block.SetFloat(WearAmountId, amount);
            if (map != null) block.SetTexture(WearTexId, map);
            renderer.SetPropertyBlock(block);
        }

#if UNITY_EDITOR
        public void EditorBind(CardWear cardWear, MeshRenderer wireRenderer,
                               MeshRenderer pointRenderer, MeshRenderer frameRenderer)
        {
            wear = cardWear;
            wire = wireRenderer;
            points = pointRenderer;
            frames = frameRenderer;
        }
#endif
    }
}
