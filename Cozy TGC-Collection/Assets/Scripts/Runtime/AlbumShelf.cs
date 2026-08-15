using System.Collections.Generic;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// The strip of book icons down the left of the frame, one per album. Clicking
    /// one is how an album gets opened, so this is the scene's navigation rather
    /// than part of the HUD - it is drawn out of the same sheet as everything else
    /// and it stays put when the HUD is hidden.
    ///
    /// It pins itself to the left edge every frame the way CardAlbum's spread fits
    /// itself into what is left, because the camera never moves and a shelf placed
    /// at a fixed x would drift off a narrow view.
    ///
    /// Hit testing is analytic, like the pack and the undrawn stack: the icons are
    /// flat camera facing quads, so projecting the ray onto their plane is simpler
    /// than fitting colliders around three diamonds.
    /// </summary>
    [DisallowMultipleComponent]
    public class AlbumShelf : MonoBehaviour
    {
        static readonly int HighlightId = Shader.PropertyToID("_Highlight");

        [SerializeField] List<MeshRenderer> icons = new List<MeshRenderer>();
        [Tooltip("Side of one icon in local units. The sheet's cells are square.")]
        [SerializeField] float iconSize = 0.44f;
        [Tooltip("Pitch between icons. Also the height of the area each one catches, " +
                 "so the strip answers the pointer without gaps between the diamonds.")]
        [SerializeField] float spacing = 0.56f;
        [Tooltip("Clearance kept from the edge of the frame. Counts twice towards Width, " +
                 "so the album is fitted into what is left with the same gap on both sides.")]
        [SerializeField] float margin = 0.16f;
        [SerializeField] float hoverPop = 1.16f;
        [SerializeField] float popSpeed = 12f;

        MaterialPropertyBlock block;
        float[] pops;
        float[] glows;
        int hovered = -1;
        int selected = -1;

        public int Count => icons.Count;

        /// <summary>
        /// How much of the frame the shelf takes off the left, popped icons included -
        /// whoever fits themselves into the rest has to clear the biggest it ever gets,
        /// or the album would resize every time the pointer crossed a book.
        /// </summary>
        public float Width => margin * 2f + iconSize * hoverPop;

        /// <summary>The column the icons sit on. The pack drawer hangs under them, on the same one.</summary>
        public float CenterX => transform.position.x;

        /// <summary>Bottom of the strip, popped icons included, for whatever hangs below it.</summary>
        public float BottomY => transform.position.y + LocalY(Mathf.Max(icons.Count - 1, 0)) - spacing * 0.5f;

        void Awake()
        {
            pops = new float[icons.Count];
            glows = new float[icons.Count];
            for (int i = 0; i < pops.Length; i++) pops[i] = 1f;
            ApplyIcons();
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float k = 1f - Mathf.Exp(-popSpeed * dt);

            for (int i = 0; i < icons.Count; i++)
            {
                bool lit = i == selected;
                pops[i] = Mathf.Lerp(pops[i], lit || i == hovered ? hoverPop : 1f, k);
                glows[i] = Mathf.Lerp(glows[i], lit ? 1f : (i == hovered ? 0.45f : 0f), k);
            }
            ApplyIcons();
        }

        /// <summary>Which icon the pointer is on, or -1.</summary>
        public int Find(Ray ray)
        {
            var plane = new Plane(transform.forward, transform.position);
            if (!plane.Raycast(ray, out float enter)) return -1;

            Vector3 local = transform.InverseTransformPoint(ray.GetPoint(enter));
            if (Mathf.Abs(local.x) > iconSize * 0.5f) return -1;

            for (int i = 0; i < icons.Count; i++)
                if (Mathf.Abs(local.y - LocalY(i)) <= spacing * 0.5f) return i;
            return -1;
        }

        public void SetHovered(int index) => hovered = index;

        /// <summary>Which album is open, so its book stays lit while it is. -1 for none.</summary>
        public void SetSelected(int index) => selected = index;

        /// <summary>
        /// Pins the strip to the left edge of the frame. The camera is nailed down
        /// but its aspect is not, so this is resolved per frame rather than authored.
        /// </summary>
        public void Layout(Camera cam)
        {
            if (cam == null) return;

            float dist = Mathf.Abs(transform.position.z - cam.transform.position.z);
            float halfHeight = cam.orthographic
                ? cam.orthographicSize
                : dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

            float x = cam.transform.position.x - halfHeight * cam.aspect + margin + iconSize * 0.5f;
            transform.position = new Vector3(x, cam.transform.position.y, transform.position.z);
        }

        float LocalY(int index) => ((icons.Count - 1) * 0.5f - index) * spacing;

        void ApplyIcons()
        {
            for (int i = 0; i < icons.Count; i++)
            {
                var icon = icons[i];
                if (icon == null) continue;

                float size = iconSize * pops[i];
                icon.transform.localPosition = new Vector3(0f, LocalY(i), 0f);
                icon.transform.localScale = new Vector3(size, size, 1f);

                block ??= new MaterialPropertyBlock();
                icon.GetPropertyBlock(block);
                block.SetFloat(HighlightId, glows[i]);
                icon.SetPropertyBlock(block);
            }
        }

#if UNITY_EDITOR
        public void EditorBind(List<MeshRenderer> iconRenderers, float size, float pitch, float edgeMargin)
        {
            icons = iconRenderers;
            iconSize = size;
            spacing = pitch;
            margin = edgeMargin;
            pops = null;
            glows = null;
        }

        /// <summary>Lays the icons out in the scene, so the built scene looks right before Play.</summary>
        public void EditorLayoutIcons()
        {
            for (int i = 0; i < icons.Count; i++)
            {
                if (icons[i] == null) continue;
                icons[i].transform.localPosition = new Vector3(0f, LocalY(i), 0f);
                icons[i].transform.localScale = new Vector3(iconSize, iconSize, 1f);
            }
        }
#endif
    }
}
