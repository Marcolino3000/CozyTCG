using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CozyTGC
{
    /// <summary>
    /// Test bench for the card rotation and the holo shader: swaps the artwork
    /// between the imported decks and lets every foil parameter be tuned live
    /// on the card under the pointer.
    /// </summary>
    public class CardDemoController : MonoBehaviour
    {
        [Serializable]
        public class Deck
        {
            public string resourceFolder;
            public string backName = "back";
            [NonSerialized] public Texture2D[] Faces;
            [NonSerialized] public Texture2D Back;
        }

        [SerializeField] Camera cam;
        [SerializeField] CardInteractor interactor;
        [SerializeField] List<CardView> cards = new List<CardView>();
        [SerializeField] List<Deck> decks = new List<Deck>();
        [SerializeField] List<string> cardLabels = new List<string>();
        [SerializeField] bool showHud = true;

        static readonly (string label, string property)[] Tunables =
        {
            ("Foil Intensity", "_FoilIntensity"),
            ("Rainbow", "_RainbowStrength"),
            ("Sparkle", "_SparkleStrength"),
            ("Chrome", "_ChromeStrength"),
            ("Sweep", "_SweepStrength"),
            ("Edge Glow", "_FresnelStrength"),
            ("Tilt Gain", "_TiltGain"),
        };

        readonly int[] tunableIds = new int[Tunables.Length];
        int deckIndex;
        int artOffset;
        GUIStyle labelStyle;
        GUIStyle hudTitleStyle;

        void Awake()
        {
            for (int i = 0; i < Tunables.Length; i++)
                tunableIds[i] = Shader.PropertyToID(Tunables[i].property);

            foreach (var deck in decks) LoadDeck(deck);
            ApplyArt();
        }

        static void LoadDeck(Deck deck)
        {
            deck.Faces = CardArtLibrary.Load(deck.resourceFolder, deck.backName, out Texture2D back);
            deck.Back = back;
        }

        void ApplyArt()
        {
            if (decks.Count == 0) return;
            var deck = decks[Mathf.Clamp(deckIndex, 0, decks.Count - 1)];
            if (deck.Faces == null || deck.Faces.Length == 0) return;

            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;
                int index = (int)Mathf.Repeat(artOffset + i, deck.Faces.Length);
                cards[i].SetFaces(deck.Faces[index], deck.Back);
            }
        }

        void Update()
        {
            if (KeyPressed(Key.RightArrow)) { artOffset++; ApplyArt(); }
            if (KeyPressed(Key.LeftArrow)) { artOffset--; ApplyArt(); }
            if (KeyPressed(Key.Tab)) { deckIndex = (deckIndex + 1) % Mathf.Max(1, decks.Count); artOffset = 0; ApplyArt(); }
            if (KeyPressed(Key.F)) foreach (var c in cards) if (c != null) c.Flip();
            if (KeyPressed(Key.R)) foreach (var c in cards) if (c != null) c.ResetRotation();
            if (KeyPressed(Key.H)) showHud = !showHud;
        }

#if ENABLE_INPUT_SYSTEM
        static bool KeyPressed(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }
#else
        enum Key { RightArrow, LeftArrow, Tab, F, R, H }
        static bool KeyPressed(Key key)
        {
            switch (key)
            {
                case Key.RightArrow: return Input.GetKeyDown(KeyCode.RightArrow);
                case Key.LeftArrow: return Input.GetKeyDown(KeyCode.LeftArrow);
                case Key.Tab: return Input.GetKeyDown(KeyCode.Tab);
                case Key.F: return Input.GetKeyDown(KeyCode.F);
                case Key.R: return Input.GetKeyDown(KeyCode.R);
                case Key.H: return Input.GetKeyDown(KeyCode.H);
            }
            return false;
        }
#endif

        void OnGUI()
        {
            labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            hudTitleStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            DrawCardLabels();
            if (!showHud) return;

            GUILayout.BeginArea(new Rect(12, 12, 300, 460), GUI.skin.box);
            GUILayout.Label("Cozy TGC - Card Holo Demo", hudTitleStyle);
            GUILayout.Label("Hover: tilt   Drag: turn freely\nClick: flip   F/R: flip/reset all\n← → : swap artwork   Tab: swap deck\nH: hide this panel");

            if (decks.Count > 0)
            {
                var deck = decks[Mathf.Clamp(deckIndex, 0, decks.Count - 1)];
                GUILayout.Label($"Deck: {deck.resourceFolder} ({(deck.Faces?.Length ?? 0)} cards)");
            }

            GUILayout.Space(6);
            var target = interactor != null ? interactor.Hovered : null;
            if (target == null)
            {
                GUILayout.Label("Hover a card to tune its foil.");
            }
            else
            {
                int index = cards.IndexOf(target);
                string name = index >= 0 && index < cardLabels.Count ? cardLabels[index] : target.name;
                GUILayout.Label($"Tuning: {name}", hudTitleStyle);
                for (int i = 0; i < Tunables.Length; i++)
                {
                    float value = target.GetFloat(tunableIds[i], 0f);
                    GUILayout.Label($"{Tunables[i].label}: {value:0.00}");
                    float next = GUILayout.HorizontalSlider(value, 0f, 3f);
                    if (!Mathf.Approximately(next, value)) target.SetFloat(tunableIds[i], next);
                }
            }
            GUILayout.EndArea();
        }

        void DrawCardLabels()
        {
            if (cam == null) return;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null || i >= cardLabels.Count) continue;
                Vector3 world = cards[i].transform.position + Vector3.down * (cards[i].Size.y * 0.5f + 0.14f);
                Vector3 screen = cam.WorldToScreenPoint(world);
                if (screen.z <= 0f) continue;
                var rect = new Rect(screen.x - 70f, Screen.height - screen.y - 10f, 140f, 20f);
                GUI.Label(rect, cardLabels[i], labelStyle);
            }
        }

#if UNITY_EDITOR
        public void EditorBind(Camera camera, CardInteractor cardInteractor, List<CardView> cardViews,
                               List<string> labels, List<Deck> deckList)
        {
            cam = camera;
            interactor = cardInteractor;
            cards = cardViews;
            cardLabels = labels;
            decks = deckList;
        }
#endif
    }
}
