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

        [Header("Wear Bench")]
        [SerializeField, Range(0f, 1f)] float ageSeverity = 0.55f;
        [Tooltip("-1 is off, and the pointer spins and flips cards as usual. Anything " +
                 "else is an index into RestorationTools.All, and a held pointer rubs.")]
        [SerializeField] int toolIndex = -1;

        /// <summary>
        /// The card both panels are pointed at, which is the last one hovered rather
        /// than the one hovered right now. Panels that follow the live hover cannot
        /// be used at all: reaching for a button or a slider takes the pointer off
        /// the card, which drops the hover, which takes the control away before it
        /// can be clicked. It only lets go when another card is picked up.
        /// </summary>
        CardView selected;

        static readonly Vector2 HudSize = new Vector2(300f, 480f);
        static readonly Vector2 WearSize = new Vector2(300f, 380f);

        Rect HudRect => new Rect(12f, 12f, HudSize.x, HudSize.y);
        Rect WearRect => new Rect(Screen.width - WearSize.x - 12f, 12f, WearSize.x, WearSize.y);

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

            if (KeyPressed(Key.Digit0)) toolIndex = -1;
            for (int i = 0; i < RestorationTools.All.Length; i++)
                if (KeyPressed(Key.Digit1 + i)) toolIndex = i;

            if (interactor != null)
            {
                // Takes the cards out of the pointer's reach while it is over a panel,
                // so a card sitting behind one neither tilts at a cursor that is not
                // talking to it nor gets rubbed while the severity slider is dragged.
                interactor.SetBlocked(PointerOverPanel());
                if (interactor.Hovered != null) selected = interactor.Hovered;
            }

            Restore();
        }

        bool PointerOverPanel()
        {
            if (!showHud) return false;
            Vector2 pointer = CardInteractor.PointerPosition();
            // GUI space counts down from the top, pointer space counts up from the bottom.
            var gui = new Vector2(pointer.x, Screen.height - pointer.y);
            return HudRect.Contains(gui) || WearRect.Contains(gui);
        }

        /// <summary>
        /// True while the pointer belongs to the card rather than to a tool: with the
        /// tools down, or while the turn modifier is held. Restoring a card means
        /// looking at it from every angle - having to put the tool down to turn it
        /// over, and pick it up again after, is the wrong trade for a bench.
        /// </summary>
        bool Turning => toolIndex < 0 || toolIndex >= RestorationTools.All.Length || KeyHeld(Key.Space);

        /// <summary>
        /// Rubs the selected tool over whichever card is under a held pointer. The
        /// press is taken off the interactor every frame it is held rather than once,
        /// because script order between the two is not fixed - let it through and the
        /// release reads as a click and flips the card mid stroke.
        /// </summary>
        void Restore()
        {
            // Hands the gesture straight back to CardInteractor, which spins on drag
            // and flips on click exactly as it does with no tool selected.
            if (Turning) return;
            if (interactor == null || cam == null) return;

            interactor.CancelPress();
            if (!CardInteractor.PointerPressed()) return;

            var card = interactor.Hovered;
            if (card == null) return;
            var wear = card.GetComponent<CardWear>();
            if (wear == null) return;

            var ray = cam.ScreenPointToRay(CardInteractor.PointerPosition());
            if (!card.TryGetLocalPointer(ray, out Vector2 local)) return;

            var uv = new Vector2(local.x * 0.5f + 0.5f, local.y * 0.5f + 0.5f);
            // The shader mirrors u on the back face, so the pointer has to be
            // mirrored with it or the tool works the wrong half of the card.
            if (card.ShowingBack) uv.x = 1f - uv.x;

            wear.Rub(uv, card.ShowingBack, RestorationTools.All[toolIndex], Time.deltaTime);
        }

#if ENABLE_INPUT_SYSTEM
        static bool KeyPressed(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }

        /// <summary>Held down, not struck. The turn modifier is a hold.</summary>
        static bool KeyHeld(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].isPressed;
        }
#else
        // Digit0..Digit6 have to stay contiguous: the tool row is picked with
        // Key.Digit1 + index, the same way the Input System's own enum allows.
        enum Key { RightArrow, LeftArrow, Tab, F, R, H, Space, Digit0, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6 }

        static KeyCode Map(Key key) => key switch
        {
            Key.RightArrow => KeyCode.RightArrow,
            Key.LeftArrow => KeyCode.LeftArrow,
            Key.Tab => KeyCode.Tab,
            Key.F => KeyCode.F,
            Key.R => KeyCode.R,
            Key.H => KeyCode.H,
            Key.Space => KeyCode.Space,
            Key.Digit0 => KeyCode.Alpha0,
            Key.Digit1 => KeyCode.Alpha1,
            Key.Digit2 => KeyCode.Alpha2,
            Key.Digit3 => KeyCode.Alpha3,
            Key.Digit4 => KeyCode.Alpha4,
            Key.Digit5 => KeyCode.Alpha5,
            _ => KeyCode.Alpha6,
        };

        static bool KeyPressed(Key key) => Input.GetKeyDown(Map(key));

        /// <summary>Held down, not struck. The turn modifier is a hold.</summary>
        static bool KeyHeld(Key key) => Input.GetKey(Map(key));
#endif

        void OnGUI()
        {
            labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            hudTitleStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            DrawCardLabels();
            if (!showHud) return;

            GUILayout.BeginArea(HudRect, GUI.skin.box);
            GUILayout.Label("Cozy TGC - Card Holo Demo", hudTitleStyle);
            GUILayout.Label("Hover: tilt   Drag: turn freely\nClick: flip   F/R: flip/reset all\n← → : swap artwork   Tab: swap deck\n1-6: pick a tool   0: back to turning\nSpace: hold to turn with a tool in hand\nH: hide this panel");

            if (decks.Count > 0)
            {
                var deck = decks[Mathf.Clamp(deckIndex, 0, decks.Count - 1)];
                GUILayout.Label($"Deck: {deck.resourceFolder} ({(deck.Faces?.Length ?? 0)} cards)");
            }

            GUILayout.Space(6);
            var target = selected;
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

            DrawWearPanel(target);
        }

        /// <summary>
        /// The wear bench. Ages the card under the pointer, hands over a tool and
        /// reads the grade back - the whole loop the shader was built for, without
        /// having to have the rest of the game around it.
        /// </summary>
        void DrawWearPanel(CardView target)
        {
            GUILayout.BeginArea(WearRect, GUI.skin.box);
            GUILayout.Label("Wear & Restoration", hudTitleStyle);

            var wear = target != null ? target.GetComponent<CardWear>() : null;
            if (wear == null)
            {
                GUILayout.Label("Hover a card to pick it up. The panel then stays on " +
                                "it until you hover another, so the buttons can be reached.");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"Card: {target.name}");

            GUILayout.Label($"Severity to stamp: {ageSeverity:0.00}");
            ageSeverity = GUILayout.HorizontalSlider(ageSeverity, 0f, 1f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Age")) wear.Age(UnityEngine.Random.Range(int.MinValue, int.MaxValue), ageSeverity);
            if (GUILayout.Button("Pristine")) wear.MakePristine();
            GUILayout.EndHorizontal();

            var condition = wear.Condition;
            GUILayout.Space(4);
            GUILayout.Label($"Grade: {CardCondition.Label(condition.Grade)}   " +
                            $"({condition.Score:0.00}, x{condition.PriceMultiplier:0.00})", hudTitleStyle);
            GUILayout.Label($"scuff {condition.Scuff:0.00}   ink {condition.InkLoss:0.00}   " +
                            $"dent {condition.Dent:0.00}\nchip {condition.Missing:0.00}   " +
                            $"bend {condition.Bend:0.00}");

            GUILayout.Space(6);
            GUILayout.Label(Turning ? "Turning - drag to spin, click to flip"
                                    : "Tool - hold the pointer on the card to rub", hudTitleStyle);
            var tools = RestorationTools.All;
            int picked = GUILayout.SelectionGrid(toolIndex + 1, BuildToolLabels(tools), 2) - 1;
            if (picked != toolIndex) toolIndex = picked;
            GUILayout.Label(toolIndex >= 0 && toolIndex < tools.Length
                ? tools[toolIndex].blurb
                : "Drag turns the card, click flips it. Hold Space to turn without " +
                  "putting a tool down.");

            GUILayout.EndArea();
        }

        string[] toolLabels;

        string[] BuildToolLabels(RestorationTool[] tools)
        {
            if (toolLabels != null && toolLabels.Length == tools.Length + 1) return toolLabels;
            toolLabels = new string[tools.Length + 1];
            // Turning is the mode you are in with no tool selected, so it belongs in
            // the same row as the tools rather than being an absence of one.
            toolLabels[0] = "0  Turn";
            for (int i = 0; i < tools.Length; i++) toolLabels[i + 1] = $"{i + 1}  {tools[i].name}";
            return toolLabels;
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
