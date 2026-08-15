using UnityEngine;
using UnityEngine.SceneManagement;

namespace CozyTGC
{
    /// <summary>
    /// The corner button that leaves this scene for another one - as it stands, the
    /// table for the dialog scene.
    ///
    /// Drawn in IMGUI, like the rest of this scene's chrome: the pack HUD, the slot
    /// labels and the shop's purse and tab are all OnGUI, and one button is not worth
    /// a second UI system in the scene (see <see cref="ShopView"/>). It takes the
    /// bottom right corner, and it stays up while the HUD is hidden - it is the way
    /// out of the scene, not a readout.
    ///
    /// Face and frame come from <see cref="UiSkin"/>: the kit's speech bubble on the
    /// kit's button, unless an <see cref="icon"/> of its own is dropped in.
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneLinkButton : MonoBehaviour
    {
        [Header("Destination")]
        [Tooltip("Scene to load, by name. It has to be in Build Settings - the scene " +
                 "builders put every scene they generate there.")]
        [SerializeField] string sceneName = "DialogDemo";

        [Header("Face")]
        [Tooltip("Optional. Drawn in place of the kit's speech bubble when it is set.")]
        [SerializeField] Texture2D icon;
        [Tooltip("Tooltip, and what a missing kit sheet falls back to.")]
        [SerializeField] string label = "Talk";
        [Tooltip("Side of the square, in unscaled pixels, before the screen's own scale is applied.")]
        [SerializeField] float size = 64f;
        [Tooltip("Gap to the bottom and right edges, unscaled.")]
        [SerializeField] float margin = 12f;

        bool visible = true;
        Rect rect;

        /// <summary>
        /// Taken off screen while something modal is up. The shop dims the whole frame
        /// from its own OnGUI, and which of the two draws last is a question of script
        /// order - so this one steps aside rather than racing it.
        /// </summary>
        public void SetVisible(bool value) => visible = value;

        /// <summary>
        /// Is the pointer the button's? The controller asks every frame, the same way it
        /// asks <see cref="ShopView.Blocks"/> - the table under this corner answers a
        /// click by putting a card down, and walking off to a conversation is not that.
        /// </summary>
        public bool Blocks(Vector2 screenPointer)
        {
            if (!visible) return false;

            // OnGUI counts y down from the top, the pointer counts up from the bottom.
            return rect.Contains(new Vector2(screenPointer.x, Screen.height - screenPointer.y));
        }

        void OnGUI()
        {
            if (!visible)
            {
                rect = Rect.zero;
                return;
            }

            UiSkin.Ensure();

            float side = UiSkin.Px(size);
            float pad = UiSkin.Px(margin);
            rect = new Rect(Screen.width - side - pad, Screen.height - side - pad, side, side);

            // The button carries the frame and the three states; the face goes on top of
            // it afterwards, because a GUIStyle draws its content inside the padding and
            // a picture this size wants the whole square.
            bool clicked = GUI.Button(rect, new GUIContent(string.Empty, label), UiSkin.Button);

            Rect face = Inset(rect, UiSkin.Px(10f));
            if (icon != null) GUI.DrawTexture(face, icon, ScaleMode.ScaleToFit);
            else UiSkin.DrawIcon(face, UiSheet.Icon.Speech, UiSkin.Ink);

            if (clicked) Go();
        }

        static Rect Inset(Rect r, float by) =>
            new Rect(r.x + by, r.y + by, r.width - by * 2f, r.height - by * 2f);

        void Go()
        {
            if (string.IsNullOrEmpty(sceneName)) return;

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogWarning($"[Cozy TGC] Scene '{sceneName}' is not in Build Settings - " +
                                 "run Tools > Cozy TGC > Build Dialog Scene, or add it by hand.");
                return;
            }

            SceneManager.LoadScene(sceneName);
        }

#if UNITY_EDITOR
        /// <summary>Face is optional: the button draws the label until there is a picture.</summary>
        public void EditorBind(string scene, string text, Texture2D face = null)
        {
            sceneName = scene;
            label = text;
            icon = face;
        }
#endif
    }
}
