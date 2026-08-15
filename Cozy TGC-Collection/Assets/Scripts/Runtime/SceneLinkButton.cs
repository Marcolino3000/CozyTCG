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
    /// The picture is still to come. Until an <see cref="icon"/> is dropped in it
    /// draws <see cref="label"/>, which becomes the tooltip once there is one.
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneLinkButton : MonoBehaviour
    {
        [Header("Destination")]
        [Tooltip("Scene to load, by name. It has to be in Build Settings - the scene " +
                 "builders put every scene they generate there.")]
        [SerializeField] string sceneName = "DialogDemo";

        [Header("Face")]
        [Tooltip("Optional. Fills the button once it is set, and the label becomes its tooltip.")]
        [SerializeField] Texture2D icon;
        [SerializeField] string label = "Talk";
        [Tooltip("Side of the square, in unscaled pixels, before the screen's own scale is applied.")]
        [SerializeField] float size = 64f;
        [Tooltip("Gap to the bottom and right edges, unscaled.")]
        [SerializeField] float margin = 12f;

        bool visible = true;
        Rect rect;
        GUIStyle style;
        float builtScale = -1f;

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

            // The shop's scale, so the two corners keep the same size to each other.
            float scale = Mathf.Clamp(Screen.height / 900f, 1f, 1.8f);
            EnsureStyle(scale);

            float side = size * scale;
            float pad = margin * scale;
            rect = new Rect(Screen.width - side - pad, Screen.height - side - pad, side, side);

            // GUIContent(Texture, string) is picture and tooltip, with no text - so the
            // label steps out of the way of the icon rather than sharing the button.
            bool clicked = icon != null
                ? GUI.Button(rect, new GUIContent(icon, label), style)
                : GUI.Button(rect, label, style);
            if (clicked) Go();
        }

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

        void EnsureStyle(float scale)
        {
            if (style != null && Mathf.Approximately(builtScale, scale)) return;
            builtScale = scale;

            int inset = Mathf.RoundToInt(6f * scale);
            style = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(14 * scale),
                // Small, even padding: an icon should fill the square rather than sit
                // in the middle of a button's default text margins.
                padding = new RectOffset(inset, inset, inset, inset),
            };
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
