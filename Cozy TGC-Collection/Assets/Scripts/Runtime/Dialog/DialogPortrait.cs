using Core;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTGC
{
    /// <summary>
    /// One side's portrait. Shows the <see cref="CharacterData.Icon"/> when the character
    /// has one and falls back to a name plate over a colour derived from the name when it
    /// does not, so a dialog is readable before any portrait art exists.
    ///
    /// <see cref="SetSpeaking"/> is the only state: the speaker is fully lit, raised and
    /// at full size, the listener dims and shrinks slightly. Both portraits stay on
    /// screen - the highlight, not the presence, says who is talking.
    /// </summary>
    public class DialogPortrait : MonoBehaviour
    {
        [Header("Parts")]
        [SerializeField] CanvasGroup group;
        [Tooltip("Scaled and raised on speaking. Separate from the root so the layout " +
                 "keeps its slot while the portrait moves.")]
        [SerializeField] RectTransform pivot;
        [SerializeField] Image frame;
        [SerializeField] Image icon;
        [Tooltip("Big initial, shown instead of the icon while the character has no Icon sprite.")]
        [SerializeField] Text initial;
        [SerializeField] Text nameLabel;

        [Header("Feel")]
        [SerializeField] float idleAlpha = 0.5f;
        [SerializeField] float idleScale = 0.93f;
        [SerializeField] float speakingScale = 1f;
        [Tooltip("How far the portrait lifts while its character speaks, in canvas units.")]
        [SerializeField] float rise = 16f;
        [SerializeField] float blendSeconds = 0.18f;

        bool speaking;
        float blend;
        Vector2 restPosition;
        bool cached;

        void Awake()
        {
            Cache();
            Apply();
        }

        void Cache()
        {
            if (cached) return;
            if (pivot != null) restPosition = pivot.anchoredPosition;
            cached = true;
        }

        void Update()
        {
            float target = speaking ? 1f : 0f;
            if (Mathf.Approximately(blend, target)) return;

            blend = blendSeconds <= 0f
                ? target
                : Mathf.MoveTowards(blend, target, Time.unscaledDeltaTime / blendSeconds);
            Apply();
        }

        /// <summary>
        /// Points the portrait at a character. A null character is not an error - the
        /// right-hand side starts out empty and dialog trees may run without a
        /// CharacterData in their blackboard.
        /// </summary>
        public void Bind(CharacterData character, string fallbackName)
        {
            string display = character != null ? character.name : fallbackName;
            Sprite sprite = character != null ? character.Icon : null;

            if (nameLabel != null) nameLabel.text = display;
            if (frame != null) frame.color = ColourFor(display);

            if (icon != null)
            {
                icon.sprite = sprite;
                icon.enabled = sprite != null;
            }

            if (initial != null)
            {
                initial.enabled = sprite == null;
                initial.text = string.IsNullOrEmpty(display)
                    ? "?"
                    : char.ToUpperInvariant(display[0]).ToString();
            }
        }

        public void SetSpeaking(bool isSpeaking)
        {
            speaking = isSpeaking;
        }

        void Apply()
        {
            Cache();

            if (group != null) group.alpha = Mathf.Lerp(idleAlpha, 1f, blend);

            if (pivot != null)
            {
                pivot.localScale = Vector3.one * Mathf.Lerp(idleScale, speakingScale, blend);
                pivot.anchoredPosition = restPosition + new Vector2(0f, rise * blend);
            }
        }

        /// <summary>
        /// A stable colour per character name. Hashed by hand rather than through
        /// string.GetHashCode, which is not guaranteed to be stable between runs, so a
        /// character would otherwise change colour on every launch.
        /// </summary>
        static Color ColourFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return new Color(0.28f, 0.27f, 0.34f, 1f);

            unchecked
            {
                int hash = 17;
                foreach (char c in key) hash = hash * 31 + c;
                float hue = Mathf.Abs(hash % 360) / 360f;
                return Color.HSVToRGB(hue, 0.3f, 0.45f);
            }
        }

#if UNITY_EDITOR
        public void EditorBind(CanvasGroup canvasGroup, RectTransform pivotRect, Image frameImage,
                               Image iconImage, Text initialText, Text nameText)
        {
            group = canvasGroup;
            pivot = pivotRect;
            frame = frameImage;
            icon = iconImage;
            initial = initialText;
            nameLabel = nameText;
        }
#endif
    }
}
