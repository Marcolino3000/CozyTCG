using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// The table everything else sits on: one quad behind the whole scene, kept filling
    /// the frame.
    ///
    /// Fitted at run time rather than sized once by the builder, for the same reason the
    /// shelf of books and the drawer of packs pin themselves to the edges - the camera
    /// never moves, but the aspect it is running at is not fixed, and a backdrop cut for
    /// a wide frame ends halfway up a narrow one.
    ///
    /// It fills rather than stretches: the picture keeps its own shape and whatever does
    /// not fit hangs off the edge. A table stretched to the frame reads as a table drawn
    /// wrong, and this one is 1:1 while almost every screen is not.
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneBackdrop : MonoBehaviour
    {
        [SerializeField] Camera cam;
        [Tooltip("Plane it sits on. +Z is away from the camera, so this is behind " +
                 "everything the scene puts on the table.")]
        [SerializeField] float depth = 1f;
        [Tooltip("A shade over the frame, so no seam shows along an edge while the " +
                 "window is being resized.")]
        [SerializeField] float overscan = 1.02f;

        /// <summary>Width against height of the picture, so filling keeps its shape.</summary>
        float pictureAspect = 1f;
        float fittedAspect = -1f;

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            ReadPictureAspect();
        }

        void OnEnable() => fittedAspect = -1f;

        void LateUpdate()
        {
            if (cam == null) return;

            // The fit only changes with the aspect, and that changes about as often as
            // somebody drags the window edge.
            if (Mathf.Approximately(cam.aspect, fittedAspect)) return;
            fittedAspect = cam.aspect;
            Fit();
        }

        void ReadPictureAspect()
        {
            var renderer = GetComponent<Renderer>();
            var tex = renderer != null && renderer.sharedMaterial != null
                ? renderer.sharedMaterial.mainTexture : null;
            if (tex != null && tex.height > 0) pictureAspect = tex.width / (float)tex.height;
        }

        void Fit()
        {
            float dist = Mathf.Abs(depth - cam.transform.position.z);
            float frameHeight = 2f * (cam.orthographic
                ? cam.orthographicSize
                : dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            float frameWidth = frameHeight * cam.aspect;

            // Cover: the tighter of the two axes decides, so the picture is never
            // smaller than the frame on either.
            float height = Mathf.Max(frameHeight, frameWidth / Mathf.Max(pictureAspect, 0.01f)) * overscan;
            transform.localScale = new Vector3(height * pictureAspect, height, 1f);
            transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, depth);
        }

#if UNITY_EDITOR
        public void EditorBind(Camera camera, float plane)
        {
            cam = camera;
            depth = plane;
            ReadPictureAspect();
            if (cam != null) Fit();
        }
#endif
    }
}
