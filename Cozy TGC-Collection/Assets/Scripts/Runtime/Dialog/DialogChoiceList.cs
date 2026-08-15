using System;
using System.Collections.Generic;
using Core;
using Nodes.Decorator;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// The player's options, stacked directly above the line at the bottom of the screen.
    ///
    /// Registers with Dialog Builder as the <see cref="DialogOptionType.Player"/> option
    /// receiver - the runner only calls presenters whose type matches the options it is
    /// about to show, so the NPC side (the package's <c>DecisionHandler</c>, which picks
    /// an answer by itself) never reaches this list.
    /// </summary>
    public class DialogChoiceList : MonoBehaviour, IDialogOptionReceiver
    {
        public event Action<DialogOptionNode> DialogOptionSelected;

        public DialogOptionType DialogOptionType => DialogOptionType.Player;

        [SerializeField] RectTransform container;
        [Tooltip("Inactive row inside the container, cloned once per offered option.")]
        [SerializeField] DialogOptionRow template;

        [Header("Answer colours")]
        [SerializeField] Color smallTalk = new Color(0.95f, 0.87f, 0.62f, 1f);
        [SerializeField] Color deepTalk = new Color(0.68f, 0.90f, 0.71f, 1f);
        [SerializeField] Color trashTalk = new Color(0.93f, 0.66f, 0.63f, 1f);
        [SerializeField] Color other = new Color(0.92f, 0.90f, 0.95f, 1f);

        readonly List<DialogOptionRow> rows = new List<DialogOptionRow>();

        void Awake()
        {
            if (template != null) template.gameObject.SetActive(false);
        }

        public void ShowDialogOptions(DialogOptionNode[] options)
        {
            if (options == null || template == null)
            {
                HideDialogOptions();
                return;
            }

            for (int i = 0; i < options.Length; i++)
            {
                var row = Row(i);
                row.Bind(options[i], ColourFor(options[i]));
                row.gameObject.SetActive(true);
            }

            for (int i = options.Length; i < rows.Count; i++)
                rows[i].gameObject.SetActive(false);
        }

        public void HideDialogOptions()
        {
            foreach (var row in rows)
                row.gameObject.SetActive(false);
        }

        /// <summary>
        /// Answers for the player when the idle timer on DialogBuilderHQ runs out. Called
        /// on every presenter, so an empty list has to be a no-op rather than a throw.
        /// </summary>
        public void TriggerIdleReaction()
        {
            var offered = new List<DialogOptionRow>();
            foreach (var row in rows)
            {
                if (row.gameObject.activeSelf && row.Node != null) offered.Add(row);
            }

            if (offered.Count == 0) return;

            Select(offered[UnityEngine.Random.Range(0, offered.Count)]);
        }

        DialogOptionRow Row(int index)
        {
            while (rows.Count <= index)
            {
                var row = Instantiate(template, container);
                row.name = $"Option {rows.Count}";
                row.Clicked += Select;
                rows.Add(row);
            }

            return rows[index];
        }

        void Select(DialogOptionRow row)
        {
            var node = row.Node;

            // Clear the list before handing the choice over: the runner starts playing the
            // line synchronously, and a second click on a still-live row would select a
            // second option on top of it.
            HideDialogOptions();
            DialogOptionSelected?.Invoke(node);
        }

        Color ColourFor(DialogOptionNode node)
        {
            if (node is PlayerDialogOption player)
            {
                switch (player.Type)
                {
                    case AnswerType.SmallTalk: return smallTalk;
                    case AnswerType.DeepTalk: return deepTalk;
                    case AnswerType.TrashTalk: return trashTalk;
                }
            }

            return other;
        }

#if UNITY_EDITOR
        public void EditorBind(RectTransform containerRect, DialogOptionRow rowTemplate)
        {
            container = containerRect;
            template = rowTemplate;
        }
#endif
    }
}
