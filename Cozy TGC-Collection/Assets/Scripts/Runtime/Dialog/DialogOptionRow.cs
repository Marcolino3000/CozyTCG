using System;
using Nodes.Decorator;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CozyTGC
{
    /// <summary>
    /// One clickable line in the option list. Cloned from a template by
    /// <see cref="DialogChoiceList"/>, one clone per option that is currently offered.
    ///
    /// The row sits inside a layout group, so the hover feedback is colour and scale
    /// only: anything that moved the rect would be undone by the next layout rebuild.
    /// </summary>
    public class DialogOptionRow : MonoBehaviour,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public event Action<DialogOptionRow> Clicked;

        public DialogOptionNode Node { get; private set; }

        [SerializeField] RectTransform rect;
        [SerializeField] Image background;
        [SerializeField] Text label;

        [Header("Feel")]
        [SerializeField] Color idleBackground = new Color(0.10f, 0.09f, 0.13f, 0.72f);
        [SerializeField] Color hoverBackground = new Color(0.20f, 0.18f, 0.26f, 0.94f);
        [SerializeField] float hoverScale = 1.015f;
        [SerializeField] float blendSeconds = 0.1f;

        bool hovered;
        float blend;

        void Awake()
        {
            if (rect == null) rect = (RectTransform)transform;
            Apply();
        }

        void OnDisable()
        {
            // Rows are pooled: a row hidden while the pointer was on it would come back
            // still lit, because no exit event ever reaches a disabled object.
            hovered = false;
            blend = 0f;
            Apply();
        }

        void Update()
        {
            float target = hovered ? 1f : 0f;
            if (Mathf.Approximately(blend, target)) return;

            blend = blendSeconds <= 0f
                ? target
                : Mathf.MoveTowards(blend, target, Time.unscaledDeltaTime / blendSeconds);
            Apply();
        }

        public void Bind(DialogOptionNode node, Color textColour)
        {
            Node = node;
            if (label != null)
            {
                label.text = node != null ? node.LocalizedLine : string.Empty;
                label.color = textColour;
            }
        }

        void Apply()
        {
            if (background != null) background.color = Color.Lerp(idleBackground, hoverBackground, blend);
            if (rect != null) rect.localScale = Vector3.one * Mathf.Lerp(1f, hoverScale, blend);
        }

        public void OnPointerEnter(PointerEventData eventData) => hovered = true;

        public void OnPointerExit(PointerEventData eventData) => hovered = false;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Node == null) return;
            Clicked?.Invoke(this);
        }

#if UNITY_EDITOR
        public void EditorBind(RectTransform rowRect, Image backgroundImage, Text labelText)
        {
            rect = rowRect;
            background = backgroundImage;
            label = labelText;
        }
#endif
    }
}
