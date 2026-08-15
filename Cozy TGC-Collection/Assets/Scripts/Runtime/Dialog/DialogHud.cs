using Core;
using Nodes.Decorator;
using Tree;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTGC
{
    /// <summary>
    /// The dialog overlay: the spoken line along the bottom of the screen, the player's
    /// options stacked above it, and one portrait per side - the main character on the
    /// left, whoever they are talking to on the right. The portrait of whoever is
    /// speaking is lit and raised, the other one dims and steps back.
    ///
    /// Dialog Builder finds this component on its own: <c>DialogBuilderHQ</c> scans the
    /// scene for <see cref="IDialogInterface"/> implementations at Start and hands them
    /// to the <see cref="DialogTreeRunner"/>, which then pushes every paragraph through
    /// <see cref="DisplayDialogLine"/>. No manual wiring on the package side.
    ///
    /// Which portrait lights up deliberately does *not* come from the name in that call.
    /// The runner labels player lines with a hard-coded "Marlene" and NPC lines with the
    /// name of the tree's CharacterData asset, so name matching would break the moment a
    /// character is renamed. The node type on
    /// <see cref="DialogTreeRunner.DialogNodeSelected"/> says who is talking without
    /// guessing.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class DialogHud : MonoBehaviour, IDialogReceiver
    {
        [Header("Characters")]
        [Tooltip("The main character, shown on the left. Their portrait comes from the " +
                 "Icon on this asset.")]
        [SerializeField] CharacterData playerCharacter;
        [Tooltip("Shown under the left portrait while no CharacterData is assigned. " +
                 "DialogTreeRunner labels every player line with this name, so keeping " +
                 "the two in sync keeps the name plate and the portrait from disagreeing.")]
        [SerializeField] string playerFallbackName = "Marlene";
        [Tooltip("Shown under the right portrait when the dialog tree has no " +
                 "CharacterData in its blackboard - the same fallback the runner uses.")]
        [SerializeField] string partnerFallbackName = "NPC";

        [Header("Parts")]
        [SerializeField] CanvasGroup root;
        [SerializeField] DialogPortrait playerPortrait;
        [SerializeField] DialogPortrait partnerPortrait;
        [SerializeField] GameObject linePanel;
        [SerializeField] Text speakerLabel;
        [SerializeField] Text lineLabel;

        [Header("Feel")]
        [SerializeField] float fadeSeconds = 0.18f;

        bool visible;

        void Awake()
        {
            if (root == null) root = GetComponent<CanvasGroup>();

            if (playerPortrait != null) playerPortrait.Bind(playerCharacter, playerFallbackName);
            if (partnerPortrait != null) partnerPortrait.Bind(null, partnerFallbackName);

            SetLine(null, null);
            root.alpha = 0f;
            root.blocksRaycasts = false;
        }

        void OnEnable()
        {
            DialogTreeRunner.OnDialogRunningStatusChanged += HandleRunningChanged;
            DialogTreeRunner.DialogNodeSelected += HandleNodeSelected;
        }

        void OnDisable()
        {
            // Both are static events on the runner, so an un-subscribe that is missed
            // keeps this instance alive across scene loads and replays lines into a
            // destroyed HUD.
            DialogTreeRunner.OnDialogRunningStatusChanged -= HandleRunningChanged;
            DialogTreeRunner.DialogNodeSelected -= HandleNodeSelected;
        }

        void Update()
        {
            float target = visible ? 1f : 0f;
            root.alpha = fadeSeconds <= 0f
                ? target
                : Mathf.MoveTowards(root.alpha, target, Time.unscaledDeltaTime / fadeSeconds);
            root.blocksRaycasts = visible;
        }

        /// <summary>
        /// The runner raises this on every step of the tree, not just once per dialog,
        /// so everything in here has to stay idempotent.
        /// </summary>
        void HandleRunningChanged(bool running, DialogTree tree)
        {
            visible = running;

            if (running)
            {
                var partner = tree != null && tree.Blackboard != null ? tree.Blackboard.CharacterData : null;
                if (partnerPortrait != null) partnerPortrait.Bind(partner, partnerFallbackName);
                return;
            }

            SetLine(null, null);
            if (playerPortrait != null) playerPortrait.SetSpeaking(false);
            if (partnerPortrait != null) partnerPortrait.SetSpeaking(false);
        }

        void HandleNodeSelected(DialogOptionNode node)
        {
            bool playerSpeaks = node is PlayerDialogOption;
            if (playerPortrait != null) playerPortrait.SetSpeaking(playerSpeaks);
            if (partnerPortrait != null) partnerPortrait.SetSpeaking(!playerSpeaks);
        }

        public void DisplayDialogLine(string characterName, string text)
        {
            SetLine(characterName, text);
        }

        public void HideDialogLine()
        {
            SetLine(null, null);
        }

        void SetLine(string speaker, string text)
        {
            if (linePanel != null) linePanel.SetActive(!string.IsNullOrEmpty(text));
            if (speakerLabel != null) speakerLabel.text = speaker ?? string.Empty;
            if (lineLabel != null) lineLabel.text = text ?? string.Empty;
        }

#if UNITY_EDITOR
        public void EditorBind(CanvasGroup group, DialogPortrait left, DialogPortrait right,
                               GameObject panel, Text speaker, Text line, CharacterData player)
        {
            root = group;
            playerPortrait = left;
            partnerPortrait = right;
            linePanel = panel;
            speakerLabel = speaker;
            lineLabel = line;
            playerCharacter = player;
        }
#endif
    }
}
