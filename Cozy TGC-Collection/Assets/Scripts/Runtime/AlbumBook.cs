using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// The drawn half of an album: one quad stepping through the frames of a
    /// RADL_Book sheet. It owns nothing but the picture - where the pockets sit
    /// and what is filed in them is CardAlbum's business, and CardAlbum drives
    /// this by asking for a clip and waiting for <see cref="Playing"/> to go false.
    ///
    /// The frame is pushed through a MaterialPropertyBlock, so the three albums
    /// share one shader and one material each rather than instancing anything.
    /// </summary>
    [DisallowMultipleComponent]
    public class AlbumBook : MonoBehaviour
    {
        public enum Clip { Open, Shut, TurnForward, TurnBack }

        static readonly int RectId = Shader.PropertyToID("_Rect");

        // Frame 3 is the book lying open, which is the resting pose every clip
        // except Shut finishes on. The page turn runs 4-6 and lands back on it.
        static readonly int[] OpenFrames = { 0, 1, 2, 3 };
        static readonly int[] ShutFrames = { 3, 2, 1, 0 };
        static readonly int[] TurnForwardFrames = { 4, 5, 6, 3 };
        static readonly int[] TurnBackFrames = { 6, 5, 4, 3 };

        [SerializeField] MeshRenderer sheet;
        [Tooltip("Frames per second. Four frames make a clip, so this is most of how " +
                 "long opening the album takes.")]
        [SerializeField] float frameRate = 11f;

        MaterialPropertyBlock block;
        Texture texture;
        int[] frames = ShutFrames;
        int step;
        float timer;

        /// <summary>Is a clip still running? CardAlbum waits on this before it shows a spread.</summary>
        public bool Playing => step < frames.Length - 1;

        void Awake()
        {
            // Taken off the material rather than wired separately: the frame rects
            // are pixel rects of this very sheet, and a second reference to it could
            // drift out of step with the one that is actually drawn.
            if (sheet != null && sheet.sharedMaterial != null) texture = sheet.sharedMaterial.mainTexture;
            step = frames.Length - 1;
            Push();
        }

        void Update()
        {
            if (!Playing) return;

            timer += Time.deltaTime;
            float perFrame = 1f / Mathf.Max(frameRate, 1f);
            // A while loop rather than one step per frame, so a hitch skips frames
            // instead of stretching the clip out behind everything waiting on it.
            while (timer >= perFrame && Playing)
            {
                timer -= perFrame;
                step++;
            }
            Push();
        }

        /// <summary>Runs a clip from its first frame.</summary>
        public void Play(Clip clip)
        {
            frames = FramesFor(clip);
            step = 0;
            timer = 0f;
            Push();
        }

        /// <summary>Jumps straight to where a clip would have ended.</summary>
        public void Snap(Clip clip)
        {
            frames = FramesFor(clip);
            step = frames.Length - 1;
            timer = 0f;
            Push();
        }

        static int[] FramesFor(Clip clip)
        {
            switch (clip)
            {
                case Clip.Shut: return ShutFrames;
                case Clip.TurnForward: return TurnForwardFrames;
                case Clip.TurnBack: return TurnBackFrames;
                default: return OpenFrames;
            }
        }

        void Push()
        {
            if (sheet == null) return;

            block ??= new MaterialPropertyBlock();
            sheet.GetPropertyBlock(block);
            block.SetVector(RectId, AlbumBookSheet.BookUvRect(texture, frames[step]));
            sheet.SetPropertyBlock(block);
        }

#if UNITY_EDITOR
        public void EditorBind(MeshRenderer bookSheet)
        {
            sheet = bookSheet;
        }
#endif
    }
}
