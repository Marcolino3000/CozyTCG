using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CozyTGC
{
    /// <summary>
    /// A guided tour of the two systems a card is made of: the foil, which is five
    /// layers driven off one tilt vector, and the wear map, which is four channels
    /// a tool rubs back out.
    ///
    /// Two cards, always. The left one is the finished article and never changes.
    /// The right one is the step's subject, carrying exactly one layer or one kind
    /// of damage at a time. Every step is therefore a difference you can see side
    /// by side, which is the only thing this scene does that CARD_SYSTEM.md cannot.
    ///
    /// Nothing here is a reimplementation. The steps drive the same
    /// <see cref="CardWear"/> and the same material properties the game does, so a
    /// step that lies is a step whose numbers have drifted from the shader's.
    /// </summary>
    public class CardExplainerController : MonoBehaviour
    {
        // -------------------------------------------------------------------
        // Shader properties
        // -------------------------------------------------------------------
        static readonly int FoilIntensityId = Shader.PropertyToID("_FoilIntensity");
        static readonly int FoilBlendId = Shader.PropertyToID("_FoilBlend");
        static readonly int TiltGainId = Shader.PropertyToID("_TiltGain");
        static readonly int RainbowStrengthId = Shader.PropertyToID("_RainbowStrength");
        static readonly int RainbowScaleId = Shader.PropertyToID("_RainbowScale");
        static readonly int RainbowTiltId = Shader.PropertyToID("_RainbowTilt");
        static readonly int RainbowStepsId = Shader.PropertyToID("_RainbowSteps");
        static readonly int SweepStrengthId = Shader.PropertyToID("_SweepStrength");
        static readonly int SweepWidthId = Shader.PropertyToID("_SweepWidth");
        static readonly int SweepTravelId = Shader.PropertyToID("_SweepTravel");
        static readonly int SparkleStrengthId = Shader.PropertyToID("_SparkleStrength");
        static readonly int SparkleDensityId = Shader.PropertyToID("_SparkleDensity");
        static readonly int SparkleSizeId = Shader.PropertyToID("_SparkleSize");
        static readonly int ChromeStrengthId = Shader.PropertyToID("_ChromeStrength");
        static readonly int FresnelStrengthId = Shader.PropertyToID("_FresnelStrength");
        static readonly int FresnelPowerId = Shader.PropertyToID("_FresnelPower");
        static readonly int MaskFromLumaId = Shader.PropertyToID("_MaskFromLuma");
        static readonly int MaskContrastId = Shader.PropertyToID("_MaskContrast");
        static readonly int MaskBiasId = Shader.PropertyToID("_MaskBias");
        static readonly int ColorStepsId = Shader.PropertyToID("_ColorSteps");
        static readonly int WearStepsId = Shader.PropertyToID("_WearSteps");
        static readonly int DebugViewId = Shader.PropertyToID("_DebugView");

        /// <summary>
        /// The views in CardDebug.hlsl, in the order the shader tests them. Index
        /// is the value of _DebugView, which is why the list starts with "off".
        /// </summary>
        static readonly string[] DebugViews =
        {
            "off", "UV", "texels", "normal", "tilt", "N.V", "wear", "height", "foil", "density",
        };

        /// <summary>
        /// Everything a rarity tier differs by. Read off the tier's own material
        /// rather than written out again here - two copies of the same numbers is
        /// two places for them to drift, and the materials are the ones the game
        /// actually ships. Colours are left out: the property block only carries
        /// the floats this bench touches.
        /// </summary>
        static readonly string[] TierFloats =
        {
            "_FoilIntensity", "_TiltGain", "_MaskFromLuma",
            "_RainbowStrength", "_RainbowScale", "_RainbowTilt", "_RainbowSteps",
            "_SweepStrength", "_SweepWidth",
            "_SparkleStrength", "_SparkleDensity", "_SparkleSize",
            "_ChromeStrength", "_ChromeSharp",
            "_FresnelStrength", "_ColorSteps",
        };

        // -------------------------------------------------------------------
        // Wiring
        // -------------------------------------------------------------------
        [Header("Scene")]
        [SerializeField] Camera cam;
        [SerializeField] CardInteractor interactor;
        [Tooltip("The finished card. Never changes - it is what the subject is read against.")]
        [SerializeField] CardView reference;
        [Tooltip("The card every step acts on.")]
        [SerializeField] CardView subject;

        [Header("Materials")]
        [Tooltip("The shipped card material, pixel snapping on.")]
        [SerializeField] Material snapped;
        [Tooltip("The same material with _PIXELAA and _HOLOPIXELATE off, for step 2. " +
                 "Those are keywords, so they cannot be switched through a property block.")]
        [SerializeField] Material smooth;
        [Tooltip("Card_Common .. Card_Chrome, in order. The tier buttons read their " +
                 "numbers straight off these.")]
        [SerializeField] Material[] tiers = Array.Empty<Material>();

        [Header("Overlay")]
        [Tooltip("Wireframe, vertex dots and tangent frames over the subject card.")]
        [SerializeField] CardMeshOverlay overlay;

        [Header("Artwork")]
        [SerializeField] string resourceFolder = CardArtLibrary.Root + "tarot_free - monochrome";
        [SerializeField] string backName = "back";
        [SerializeField] int faceIndex = 8;

        [Header("Ageing")]
        [SerializeField] int wearSeed = 20260816;
        [SerializeField, Range(0f, 1f)] float severity = 0.85f;

        // -------------------------------------------------------------------
        // State
        // -------------------------------------------------------------------
        enum MapChannel { Raw, Scuff, Ink, Height, Missing }

        /// <summary>One page of the tour. <see cref="enter"/> sets the cards up for it.</summary>
        class Step
        {
            public string chapter;
            public string title;
            public string body;
            public Action enter;
            public Action controls;
        }

        readonly List<Step> steps = new List<Step>();
        int index;

        Texture2D[] faces;
        Texture2D back;

        int toolIndex = -1;
        CardWear.Damage stepKinds = CardWear.Damage.All;
        MapChannel channel = MapChannel.Raw;
        bool showBackMap;
        bool showMap;
        bool showGpu;
        bool showTexture;
        int debugView;
        bool showPanels = true;
        bool autoTurn = true;
        bool useSmooth;

        // A scripted pass, walked a few points per frame rather than dumped in one
        // go: a tool that spends its whole time in one frame is a tool nobody can
        // see working, and it would put ten thousand strokes in the save list.
        const int GridCols = 12, GridRows = 18;
        const int BorderSteps = 30;
        bool passRunning;
        int passIndex, passSteps;
        float passSeconds;
        bool passBorder;

        /// <summary>The camera distance the scene was built at. Never come in closer than this.</summary>
        float baseDistance = 3.4f;

        Texture2D mapView;
        Vector2 guideScroll;
        GUIStyle titleStyle, bodyStyle, labelStyle, chapterStyle;

        CardWear Wear => subject != null ? subject.GetComponent<CardWear>() : null;
        bool HasTool => toolIndex >= 0 && toolIndex < RestorationTools.All.Length;
        RestorationTool CurrentTool => RestorationTools.All[Mathf.Clamp(toolIndex, 0, RestorationTools.All.Length - 1)];

        static readonly Vector2 GuideSize = new Vector2(410f, 700f);
        static readonly Vector2 ReadoutSize = new Vector2(330f, 560f);

        Rect GuideRect => new Rect(Screen.width - GuideSize.x - 12f, 12f,
                                   GuideSize.x, Mathf.Min(GuideSize.y, Screen.height - 24f));
        /// <summary>
        /// Grows with what the step has switched on. A panel that reserved room for
        /// everything would cover the card it is describing.
        /// </summary>
        Rect ReadoutRect => new Rect(12f, 12f, ReadoutSize.x,
                                    Mathf.Min(175f + (showGpu ? 190f : 0f)
                                                   + (showTexture ? 165f : 0f)
                                                   + (showMap ? 330f : 0f),
                                              Screen.height - 24f));

        // -------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------
        void Awake()
        {
            if (cam == null) cam = Camera.main;
            faces = CardArtLibrary.Load(resourceFolder, backName, out back);
        }

        void Start()
        {
            // Start, not Awake: CardView resolves its own renderer in Awake, and
            // script order between the two is not fixed.
            if (cam != null) baseDistance = Mathf.Abs(cam.transform.position.z);
            ApplyArt();
            if (reference != null)
            {
                reference.SetIdleSway(9f, 0.5f);
                ApplyTier(Tier("Holo"), reference);
            }
            BuildSteps();
            Enter(0);
        }

        void OnDestroy()
        {
            if (mapView != null) Destroy(mapView);
        }

        void ApplyArt()
        {
            if (faces == null || faces.Length == 0) return;
            var face = faces[(int)Mathf.Repeat(faceIndex, faces.Length)];
            if (reference != null) reference.SetFaces(face, back);
            if (subject != null) subject.SetFaces(face, back);
        }

        // -------------------------------------------------------------------
        // Input
        // -------------------------------------------------------------------
        void Update()
        {
            if (KeyPressed(Key.RightArrow)) Enter(index + 1);
            if (KeyPressed(Key.LeftArrow)) Enter(index - 1);
            if (KeyPressed(Key.Tab)) Enter(NextChapter());
            if (KeyPressed(Key.R)) Enter(index);
            if (KeyPressed(Key.T)) SetAutoTurn(!autoTurn);
            if (KeyPressed(Key.H)) showPanels = !showPanels;
            if (KeyPressed(Key.M)) showMap = !showMap;

            if (KeyPressed(Key.Digit0)) toolIndex = -1;
            for (int i = 0; i < RestorationTools.All.Length; i++)
                if (KeyPressed(Key.Digit1 + i)) toolIndex = i;

            if (interactor != null) interactor.SetBlocked(PointerOverPanel());

            LayOutCards();
            AdvancePass();
            Rubbing();
        }

        bool PointerOverPanel()
        {
            if (!showPanels) return false;
            Vector2 pointer = CardInteractor.PointerPosition();
            // GUI space counts down from the top, pointer space counts up from the bottom.
            var gui = new Vector2(pointer.x, Screen.height - pointer.y);
            return GuideRect.Contains(gui) || ReadoutRect.Contains(gui);
        }

        /// <summary>
        /// The pointer belongs to the card whenever no tool is in hand, and while
        /// the turn modifier is held. Restoring means looking at the card from
        /// every angle - putting the tool down to turn it over is the wrong trade.
        /// </summary>
        bool Turning => !HasTool || KeyHeld(Key.Space);

        /// <summary>
        /// Rubs the step's tool over the subject while the pointer is held on it.
        /// Straight out of <c>CardDemoController.Restore</c>, including taking the
        /// press off the interactor every frame it is held - script order between
        /// the two is not fixed, so the release would otherwise read as a click and
        /// flip the card mid stroke.
        /// </summary>
        void Rubbing()
        {
            if (Turning || interactor == null || cam == null) return;

            interactor.CancelPress();
            if (!CardInteractor.PointerPressed()) return;

            var card = interactor.Hovered;
            if (card == null || card != subject) return;
            var wear = Wear;
            if (wear == null) return;

            var ray = cam.ScreenPointToRay(CardInteractor.PointerPosition());
            if (!card.TryGetLocalPointer(ray, out Vector2 local)) return;

            var uv = new Vector2(local.x * 0.5f + 0.5f, local.y * 0.5f + 0.5f);
            // The shader mirrors u on the back face, so the pointer has to be
            // mirrored with it or the tool works the wrong half of the card.
            if (card.ShowingBack) uv.x = 1f - uv.x;

            wear.Rub(uv, card.ShowingBack, CurrentTool, Time.deltaTime);
        }

        /// <summary>
        /// Puts the pair in the middle of whatever the panels leave, and no wider
        /// than that. The panels are in pixels and the cards are in world units, so
        /// the gap between them has to be measured every frame rather than chosen
        /// once against one aspect ratio - and a bench that hides half its subject
        /// behind its own guide is no use at all.
        ///
        /// It moves the roots, which never rotate, so none of the pointer maths
        /// cares that they have moved.
        /// </summary>
        void LayOutCards()
        {
            if (cam == null || subject == null || reference == null) return;

            float left = showPanels ? ReadoutRect.xMax + 8f : 0f;
            float right = showPanels ? GuideRect.xMin - 8f : Screen.width;
            if (right <= left) { left = 0f; right = Screen.width; }

            float strip = Mathf.Max(1f, right - left);
            float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            // The card at its hovered size, plus a little air. Two of them side by
            // side is what the strip has to hold.
            float card = subject.Size.x * 1.12f;

            // A screen pixel covers worldHeight/Screen.height world units, and
            // worldHeight is 2 * distance * tan(fov/2). Solve that for the distance
            // at which the strip is exactly wide enough, and never come closer than
            // the distance the scene was framed at.
            float fit = card * 2f * Screen.height / (strip * 2f * tan);
            float distance = Mathf.Max(baseDistance, fit);
            var camPosition = cam.transform.position;
            if (!Mathf.Approximately(camPosition.z, -distance))
                cam.transform.position = new Vector3(camPosition.x, camPosition.y, -distance);

            float perPixel = 2f * distance * tan / Mathf.Max(1, Screen.height);
            float free = strip * perPixel;
            float spread = Mathf.Clamp(free * 0.25f, card * 0.5f, card);

            float centre = (left + right) * 0.5f - Screen.width * 0.5f;
            float x = centre * perPixel;

            Place(reference, x - spread);
            Place(subject, x + spread);
        }

        static void Place(CardView card, float x)
        {
            Vector3 position = card.transform.position;
            if (!Mathf.Approximately(position.x, x))
                card.transform.position = new Vector3(x, position.y, position.z);
        }

        // -------------------------------------------------------------------
        // Scripted passes
        // -------------------------------------------------------------------
        void StartPass(float seconds, bool border)
        {
            if (!HasTool) return;
            passRunning = true;
            passBorder = border;
            passSeconds = Mathf.Max(0.01f, seconds);
            passIndex = 0;
            passSteps = CurrentTool.wholeCard ? 1 : (border ? BorderSteps * 4 : GridCols * GridRows);
        }

        void AdvancePass()
        {
            if (!passRunning) return;
            var wear = Wear;
            if (wear == null) { passRunning = false; return; }

            float per = passSeconds / passSteps;
            int budget = Mathf.Max(1, Mathf.CeilToInt(passSteps * Time.deltaTime / passSeconds));
            bool back = subject != null && subject.ShowingBack;

            for (int i = 0; i < budget && passIndex < passSteps; i++, passIndex++)
                wear.Rub(PassPoint(passIndex), back, CurrentTool, per);

            if (passIndex >= passSteps) passRunning = false;
        }

        /// <summary>
        /// Where the scripted pass is at point <paramref name="i"/>. A serpentine
        /// over the face, or a lap of the border - which is where chips and edge
        /// ink loss are, and the only place the fill and the pen are worth pointing.
        /// </summary>
        Vector2 PassPoint(int i)
        {
            if (CurrentTool.wholeCard) return new Vector2(0.5f, 0.5f);
            if (passBorder)
            {
                int side = i / BorderSteps;
                float t = (i % BorderSteps + 0.5f) / BorderSteps;
                return side switch
                {
                    0 => new Vector2(t, 0.02f),
                    1 => new Vector2(0.98f, t),
                    2 => new Vector2(1f - t, 0.98f),
                    _ => new Vector2(0.02f, 1f - t),
                };
            }
            int row = i / GridCols;
            int col = i % GridCols;
            if ((row & 1) == 1) col = GridCols - 1 - col;
            return new Vector2((col + 0.5f) / GridCols, (row + 0.5f) / GridRows);
        }

        // -------------------------------------------------------------------
        // Steps
        // -------------------------------------------------------------------
        void Enter(int next)
        {
            if (steps.Count == 0) return;
            index = Mathf.Clamp(next, 0, steps.Count - 1);

            toolIndex = -1;
            passRunning = false;
            useSmooth = false;
            SetMaterial(snapped);

            if (subject != null)
            {
                subject.ResetRotation();
                subject.SetIdleSway(autoTurn ? 14f : 0f, 0.5f);
            }
            if (reference != null) reference.SetIdleSway(autoTurn ? 9f : 0f, 0.5f);

            FoilOff();
            var wear = Wear;
            if (wear != null) wear.MakePristine();
            showMap = false;
            showGpu = false;
            showTexture = false;
            SetDebugView(0);
            if (overlay != null) overlay.Show(false, false, false);

            steps[index].enter?.Invoke();
            guideScroll = Vector2.zero;
        }

        int NextChapter()
        {
            string here = steps[index].chapter;
            for (int i = index + 1; i < steps.Count; i++)
                if (steps[i].chapter != here) return i;
            return 0;
        }

        void SetAutoTurn(bool value)
        {
            autoTurn = value;
            if (subject != null) subject.SetIdleSway(value ? 14f : 0f, 0.5f);
            if (reference != null) reference.SetIdleSway(value ? 9f : 0f, 0.5f);
        }

        void SetMaterial(Material material)
        {
            if (subject == null || material == null) return;
            var renderer = subject.Renderer != null ? subject.Renderer : subject.GetComponentInChildren<MeshRenderer>();
            // sharedMaterial, never an instance: rarity tiers are materials in this
            // project and per-card variation rides on the property block.
            if (renderer != null) renderer.sharedMaterial = material;
        }

        Material Tier(string name)
        {
            foreach (var m in tiers)
                if (m != null && m.name.EndsWith(name, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        void ApplyTier(Material tier, CardView card)
        {
            if (tier == null || card == null) return;
            foreach (string property in TierFloats)
            {
                int id = Shader.PropertyToID(property);
                if (tier.HasFloat(id)) card.SetFloat(id, tier.GetFloat(id));
            }
        }

        /// <summary>
        /// Every foil layer off, everything else at the material's own settings.
        /// Each step turns exactly one layer back on, so what is on screen is only
        /// ever the layer being described.
        /// </summary>
        void FoilOff()
        {
            if (subject == null) return;
            subject.SetFloat(FoilIntensityId, 1f);
            subject.SetFloat(FoilBlendId, 0.65f);
            subject.SetFloat(TiltGainId, 1f);
            subject.SetFloat(MaskFromLumaId, 0.45f);
            subject.SetFloat(MaskContrastId, 2f);
            subject.SetFloat(MaskBiasId, 0.1f);
            subject.SetFloat(RainbowStrengthId, 0f);
            subject.SetFloat(SweepStrengthId, 0f);
            subject.SetFloat(SparkleStrengthId, 0f);
            subject.SetFloat(ChromeStrengthId, 0f);
            subject.SetFloat(FresnelStrengthId, 0f);
            subject.SetFloat(ColorStepsId, 0f);
        }

        void Layer(int id, float value)
        {
            if (subject != null) subject.SetFloat(id, value);
        }

        void SetDebugView(int view)
        {
            debugView = Mathf.Clamp(view, 0, DebugViews.Length - 1);
            if (subject != null) subject.SetFloat(DebugViewId, debugView);
        }

        void Overlay(bool wire, bool vertices, bool frames)
        {
            if (overlay != null) overlay.Show(wire, vertices, frames);
        }

        /// <summary>Where the pointer is on the subject, in its own 0..1 uv.</summary>
        bool PointerUV(out Vector2 uv)
        {
            uv = Vector2.zero;
            if (cam == null || subject == null) return false;
            var ray = cam.ScreenPointToRay(CardInteractor.PointerPosition());
            if (!subject.TryGetLocalPointer(ray, out Vector2 local)) return false;

            uv = new Vector2(local.x * 0.5f + 0.5f, local.y * 0.5f + 0.5f);
            // The shader mirrors u on the back face; the readout has to agree with
            // it or it names a texel on the wrong half of the card.
            if (subject.ShowingBack) uv.x = 1f - uv.x;
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }

        Texture2D CurrentFace()
        {
            if (faces == null || faces.Length == 0) return null;
            return faces[(int)Mathf.Repeat(faceIndex, faces.Length)];
        }

        void Age(CardWear.Damage kinds)
        {
            stepKinds = kinds;
            var wear = Wear;
            if (wear != null) wear.Age(wearSeed, severity, kinds);
        }

        void BuildSteps()
        {
            steps.Clear();
            const string Basics = "0 - What a shader is";
            const string Foil = "1 - The foil";
            const string Damage = "2 - The damage";
            const string Restoring = "3 - Restoring";

            // ---------------------------------------------------------------
            // Chapter 0 - the fundamentals, on this card
            // ---------------------------------------------------------------
            steps.Add(new Step
            {
                chapter = Basics,
                title = "A program that runs a lot",
                body =
                    "A shader is a small program that runs on the GPU. Not once - many times, in " +
                    "parallel, and you never write the loop. You write the body; the hardware runs " +
                    "it once per vertex and once per covered pixel.\n\n" +
                    "  VERTEX    once per point of the mesh. Decides WHERE.\n" +
                    "  FRAGMENT  once per pixel the triangles cover. Decides WHAT COLOUR.\n\n" +
                    "Between them sits the rasteriser - fixed hardware, not your code. It works out " +
                    "which pixels each triangle covers and blends the vertex outputs across them.\n\n" +
                    "The counts on the left are this card, this frame. The next ten steps show you " +
                    "each piece of that on the card itself.",
                enter = () => showGpu = true,
            });

            steps.Add(new Step
            {
                chapter = Basics,
                title = "The mesh",
                body =
                    "A mesh is not a shape. It is arrays.\n\n" +
                    "  vertices   425 positions in object space\n" +
                    "  uv         425 texture coordinates\n" +
                    "  normals    425 directions\n" +
                    "  tangents   425 directions (+ a handedness)\n" +
                    "  triangles  2304 INDICES into all of the above\n\n" +
                    "The blue lines are that index list, drawn: 768 triangles sharing 425 corners. " +
                    "An inner grid point is used by six triangles and exists once - that sharing is " +
                    "the whole reason meshes are indexed.\n\n" +
                    "This card is a 16x24 grid rather than the four-vertex quad it started as, " +
                    "because a vertex shader can only move vertices that exist. Step 6 bends it.\n\n" +
                    "The wireframe is a real mesh with MeshTopology.Lines, drawn by " +
                    "CardOverlay.shader - which calls the CARD's own bend function, so it can never " +
                    "describe a surface the card is no longer on.",
                enter = () =>
                {
                    Overlay(true, false, false);
                    showGpu = true;
                },
            });

            steps.Add(new Step
            {
                chapter = Basics,
                title = "Vertices",
                body =
                    "One amber dot per vertex. 425 of them, and the vertex program runs exactly " +
                    "once for each - that is what the count on the left means, literally.\n\n" +
                    "Everything a vertex carries is indexed by the same number: vertex 137 has a " +
                    "position, a uv, a normal and a tangent, one entry in each of four arrays. " +
                    "Those four arrays are exactly the struct the shader receives:\n\n" +
                    "  struct Attributes {\n" +
                    "      float4 positionOS : POSITION;\n" +
                    "      float3 normalOS   : NORMAL;\n" +
                    "      float4 tangentOS  : TANGENT;\n" +
                    "      float2 uv         : TEXCOORD0;\n" +
                    "  };\n\n" +
                    "Leave an array off the mesh and the shader reads zeros there.",
                enter = () =>
                {
                    Overlay(true, true, false);
                    showGpu = true;
                },
            });

            steps.Add(new Step
            {
                chapter = Basics,
                title = "UV: where you are on the card",
                body =
                    "The card is showing its uv: red is u, green is v, both 0..1 across the face. " +
                    "Black is the bottom-left corner, yellow the top-right.\n\n" +
                    "UV is stored per VERTEX - 425 values - and arrives per PIXEL, because the " +
                    "rasteriser interpolates it across each triangle. That is the single most " +
                    "useful thing to understand about the fragment stage: most of what it knows, " +
                    "it knows because three corners knew it and the hardware blended between them.\n\n" +
                    "Point at the card and watch the texture panel on the left: it names the uv " +
                    "under your cursor and the texel of the artwork it lands on. That mapping - " +
                    "0..1 to a pixel of a picture - is all a texture fetch is.",
                enter = () =>
                {
                    SetDebugView(1);
                    showTexture = true;
                    Overlay(true, true, false);
                },
            });

            steps.Add(new Step
            {
                chapter = Basics,
                title = "Uniforms, and where they live",
                body =
                    "The fourth kind of input, after attributes, interpolators and textures. A " +
                    "UNIFORM is per draw and constant for every invocation - _RainbowStrength, " +
                    "_WearAmount, _Bow.\n\n" +
                    "They all live in one buffer:\n\n" +
                    "  CBUFFER_START(UnityPerMaterial)\n" +
                    "      float _FoilIntensity;\n" +
                    "      ...\n" +
                    "  CBUFFER_END\n\n" +
                    "Not tidiness: the SRP Batcher requires it, and a property declared outside it " +
                    "silently stops the whole shader batching.\n\n" +
                    "Two cards, one material, different values: that is a MaterialPropertyBlock, " +
                    "per renderer, and it is how the artwork, the sparkle seed and the two wear " +
                    "maps get onto each card without instancing a material per card.\n\n" +
                    "Try the tier buttons under step 15 later - rarity in this project is nothing " +
                    "but a different set of uniforms.",
                enter = () =>
                {
                    ApplyTier(Tier("Holo"), subject);
                    showGpu = true;
                },
            });

            steps.Add(new Step
            {
                chapter = Basics,
                title = "The vertex stage",
                body =
                    "The card is bowed, and the wireframe and the vertex dots are bowed with it - " +
                    "because they run the SAME CardApplyBend, out of the same file.\n\n" +
                    "This is the only place geometry can change. Anything that has to alter the " +
                    "SILHOUETTE lives here; no amount of shading will bend a flat quad. That is why " +
                    "the bow, the folded corner and deep creases are vertex work while the dent " +
                    "that catches the foil is not.\n\n" +
                    "425 runs is nothing, which is why low-frequency work belongs in this stage.\n\n" +
                    "Two traps this makes concrete:\n\n" +
                    "  - #pragma target is 3.5, not 3.0, because this vertex stage SAMPLES A " +
                    "TEXTURE (crease depth, out of the wear map).\n" +
                    "  - The mesh bounds reserve 0.14 units for the displacement. Culling reads " +
                    "bounds and runs BEFORE your vertex shader, so a bowed card culled against a " +
                    "flat slab pops out of view at the edge of the screen.\n\n" +
                    "Drag the severity slider and watch the grid move.",
                enter = () =>
                {
                    severity = 1f;
                    Age(CardWear.Damage.Bend);
                    ApplyTier(Tier("Shiny"), subject);
                    Overlay(true, true, false);
                    showGpu = true;
                },
                controls = AgeControls,
            });

            steps.Add(new Step
            {
                chapter = Basics,
                title = "The tangent frame",
                body =
                    "Three axes at 24 points across the card:\n\n" +
                    "  RED    T, along +u of the artwork\n" +
                    "  GREEN  B, along +v\n" +
                    "  BLUE   N, out of the face\n\n" +
                    "That is TANGENT SPACE - the card's own frame. Working in it means the foil " +
                    "does not care where the card is in the world or how it is rotated, only how it " +
                    "is turned relative to you. Every layer in chapter 1 is computed there.\n\n" +
                    "Bow the card and watch the blue axes fan out. They are not drawn from stored " +
                    "normals: CardApplyBend rebuilds the frame from three evaluations of the " +
                    "displacement, and the gizmo asks the same function. Finite differences rather " +
                    "than a hand-derived gradient - slower, and impossible to get out of step with " +
                    "the displacement when someone changes it.\n\n" +
                    "tangent.w is the handedness: B = cross(N, T) * w. It is -1 here so B lines up " +
                    "with +v of the UVs rather than against it.",
                enter = () =>
                {
                    severity = 1f;
                    Age(CardWear.Damage.Bend);
                    ApplyTier(Tier("Shiny"), subject);
                    Overlay(false, true, true);
                    showGpu = true;
                },
                controls = AgeControls,
            });

            steps.Add(new Step
            {
                chapter = Basics,
                title = "The fragment stage",
                body =
                    "Now the other program. It runs once per covered pixel - see the count on the " +
                    "left, some hundreds of times the vertex count.\n\n" +
                    "The card is showing its texel grid: the 73x113 cells the artwork is stored in, " +
                    "with the snapping ramp in red where CardPixelUV bends the uv towards a texel " +
                    "centre. Every one of those cells is many fragments at this size, and every " +
                    "fragment ran the whole foil.\n\n" +
                    "That ratio is the entire cost model. It also explains a rule that looks odd " +
                    "from the CPU: a branch every invocation takes the same way is FREE - the " +
                    "hardware skips the block whole - while a branch that differs pixel to pixel is " +
                    "not, because neighbouring pixels run in lockstep and both sides get executed.\n\n" +
                    "So `if (_WearAmount > 0)` costs nothing, and a pristine card pays nothing at " +
                    "all for the damage code. The debug views you are switching between are the " +
                    "same trick: one uniform branch at the end of the pass.",
                enter = () =>
                {
                    SetDebugView(2);
                    ApplyTier(Tier("Holo"), subject);
                    showGpu = true;
                },
            });

            steps.Add(new Step
            {
                chapter = Basics,
                title = "Textures and samplers",
                body =
                    "A texture is the data. The SAMPLER is how it is read: filtering, mip " +
                    "selection, and what happens outside 0..1. Two opposite settings in this one " +
                    "shader, both deliberate:\n\n" +
                    "  artwork    bilinear, sRGB   a rotating quad point-filtered CRAWLS\n" +
                    "  wear map   point, clamp, LINEAR   it is data, not a picture\n\n" +
                    "A gamma curve on the wear map would bend every rate the restoration tools rub " +
                    "at, which is why it is created linear and read with explicit LOD 0.\n\n" +
                    "The card is showing texel DENSITY - how many card texels one screen pixel " +
                    "covers. That number is what mip selection is made of. Move the camera or turn " +
                    "the card and watch it change.\n\n" +
                    "It is also the reason the artwork is sampled with SAMPLE_TEXTURE2D_GRAD using " +
                    "the UNTOUCHED derivatives: CardPixelUV's snapped uv jumps at every seam, and " +
                    "handed to the hardware it reads as an enormous density, picks the smallest " +
                    "mip, and collapses the card to a flat colour.",
                enter = () =>
                {
                    SetDebugView(9);
                    showTexture = true;
                    showGpu = true;
                },
            });

            steps.Add(new Step
            {
                chapter = Basics,
                title = "Passes, and variants",
                body =
                    "One object can be drawn several times a frame by different programs. A PASS " +
                    "is one of those. This shader has two:\n\n" +
                    "  UniversalForward   the colour you see\n" +
                    "  DepthOnly          depth only - ColorMask R, no foil math at all\n\n" +
                    "They must agree about two things or the card comes apart: WHERE it is " +
                    "(CardApplyBendPosition mirrors CardApplyBend exactly) and WHICH PIXELS are " +
                    "thrown away (the chip clip is repeated in both). Leave the clip out of " +
                    "DepthOnly and a chipped pixel writes depth over the background the forward " +
                    "pass clipped through to - the hole fills with whatever is behind the card.\n\n" +
                    "KEYWORDS are the other half. `#pragma shader_feature_local_fragment _ _PIXELAA` " +
                    "compiles TWO programs and the material picks one. A compile-time #ifdef, not a " +
                    "runtime if - which is why setting the _PixelAA float without EnableKeyword " +
                    "changes nothing, and why the pragmas are repeated in every pass that needs " +
                    "them.\n\n" +
                    "The live keyword list is on the left. The next step switches between two whole " +
                    "MATERIALS, precisely because a property block cannot switch a keyword.",
                enter = () =>
                {
                    ApplyTier(Tier("Holo"), subject);
                    showGpu = true;
                },
            });

            // ---------------------------------------------------------------
            // Chapter 1 - the foil
            // ---------------------------------------------------------------
            steps.Add(new Step
            {
                chapter = Foil,
                title = "The flat print",
                body =
                    "No foil, no damage. Just the artwork on a quad that can bend.\n\n" +
                    "The mesh faces -Z and the camera sits on -Z unrotated, so the visible " +
                    "side is -transform.forward. Get that backwards and the artwork mirrors.\n\n" +
                    "The root object never rotates. A Visual child does all the tilting, and " +
                    "the pointer is projected onto a plane taken from the root, so the tilt " +
                    "cannot feed back into the pointer and shake the card.\n\n" +
                    "Left is the finished card. Everything from here adds one layer at a time " +
                    "until the right one looks like it.",
            });

            steps.Add(new Step
            {
                chapter = Foil,
                title = "Pixel snapping",
                body =
                    "The card textures are imported bilinear, not point - these are rotating " +
                    "3D quads, and point filtering crawls at an angle.\n\n" +
                    "CardPixelUV puts the crispness back: it snaps the UV to texel centres but " +
                    "keeps a one-screen-pixel ramp on the seams, which reads as clean pixel art " +
                    "at any angle without shimmering.\n\n" +
                    "Because that UV is deliberately discontinuous, the artwork is sampled with " +
                    "SAMPLE_TEXTURE2D_GRAD using the untouched derivatives. Feed the snapped UV " +
                    "to the hardware and mip selection breaks - the card collapses to a flat colour " +
                    "at distance.\n\n" +
                    "_PIXELAA and _HOLOPIXELATE are shader keywords. Setting the float without " +
                    "EnableKeyword does nothing at all.",
                enter = () => Layer(RainbowStrengthId, 0.55f),
                controls = () =>
                {
                    if (GUILayout.Button(useSmooth ? "Snapping: OFF - turn it on" : "Snapping: ON - turn it off"))
                    {
                        useSmooth = !useSmooth;
                        SetMaterial(useSmooth ? smooth : snapped);
                    }
                    GUILayout.Label(useSmooth
                        ? "Unsnapped: the art softens and the foil goes smooth."
                        : "Snapped: art and foil both land on the 73x113 grid.");
                },
            });

            steps.Add(new Step
            {
                chapter = Foil,
                title = "One vector: tilt",
                body =
                    "Every layer below is driven by one pair of numbers, computed per pixel:\n\n" +
                    "  vT   = (dot(V,T), dot(V,B), dot(V,N))\n" +
                    "  tilt = vT.xy / max(vT.z, 0.15) * _TiltGain\n" +
                    "  tilt = tilt / (1 + 0.35 * |tilt|)\n\n" +
                    "V is the view direction in tangent space. tilt is zero with the card facing " +
                    "you and grows as it turns away. The 0.15 floor keeps the divide from blowing " +
                    "up at grazing angles; the softening keeps it finite.\n\n" +
                    "Drag the card and watch the readout on the left. That single vector is why " +
                    "the whole effect reacts to rotation - and, later, why a dent breaks all five " +
                    "layers at once: the dent perturbs N, and everything is measured against N.",
                enter = () =>
                {
                    showMap = false;
                    Layer(RainbowStrengthId, 0.35f);
                },
                controls = () => Knob("_TiltGain", TiltGainId, 0f, 4f, 1f),
            });

            steps.Add(new Step
            {
                chapter = Foil,
                title = "Layer 1: rainbow diffraction",
                body =
                    "A grating phase built out of the UV and the tilt together:\n\n" +
                    "  phase = dot(uv - 0.5, dir) * _RainbowScale\n" +
                    "        + dot(tilt, dir)     * _RainbowTilt\n" +
                    "        + time               * _RainbowDrift\n\n" +
                    "hue = frac(phase), posterised by _RainbowSteps, turned into colour by a " +
                    "three-phase cosine spectrum.\n\n" +
                    "_RainbowDepth parallax-shifts the UV by the tilt first, which is what makes " +
                    "the bands sit under the artwork instead of looking painted on top of it.",
                enter = () => Layer(RainbowStrengthId, 0.7f),
                controls = () =>
                {
                    Knob("_RainbowStrength", RainbowStrengthId, 0f, 3f, 0.7f);
                    Knob("_RainbowScale", RainbowScaleId, 0f, 20f, 3f);
                    Knob("_RainbowTilt", RainbowTiltId, 0f, 6f, 1.2f);
                    Knob("_RainbowSteps", RainbowStepsId, 0f, 32f, 8f);
                },
            });

            steps.Add(new Step
            {
                chapter = Foil,
                title = "Layer 2: sweep",
                body =
                    "One gaussian bar along _SweepAngle:\n\n" +
                    "  along  = dot(uv - 0.5, dir)\n" +
                    "  centre = _SweepOffset + dot(tilt, dir) * _SweepTravel\n" +
                    "  sweep  = exp(-((along - centre) / _SweepWidth)^2 * 2)\n\n" +
                    "_SweepOffset parks the bar near an edge when the card is at rest, so turning " +
                    "the card walks it across the face instead of wobbling it about the middle.\n\n" +
                    "The rainbow is multiplied by (0.35 + sweep) as well, so the bands brighten " +
                    "under the bar rather than the two effects ignoring each other.",
                enter = () => Layer(SweepStrengthId, 0.9f),
                controls = () =>
                {
                    Knob("_SweepStrength", SweepStrengthId, 0f, 3f, 0.9f);
                    Knob("_SweepWidth", SweepWidthId, 0.02f, 2f, 0.35f);
                    Knob("_SweepTravel", SweepTravelId, 0f, 3f, 0.9f);
                },
            });

            steps.Add(new Step
            {
                chapter = Foil,
                title = "Layer 3: sparkle",
                body =
                    "Hash-based flakes on a grid of _SparkleDensity cells, tested over the 3x3 " +
                    "neighbourhood so a flake does not get clipped at a cell border.\n\n" +
                    "The trick is per-flake aim: every flake gets a random preferred tilt, and its " +
                    "brightness falls off with the distance between that and the current tilt. A " +
                    "flake only fires when the card is turned towards it, so the glitter pops in " +
                    "and out while you turn instead of scrolling across the face.\n\n" +
                    "_SparkleSeed is per card, set from CardView.Awake - two cards side by side " +
                    "must not glitter in step.",
                enter = () =>
                {
                    Layer(SparkleStrengthId, 1.6f);
                    Layer(SweepStrengthId, 0.15f);
                },
                controls = () =>
                {
                    Knob("_SparkleStrength", SparkleStrengthId, 0f, 4f, 1.6f);
                    Knob("_SparkleDensity", SparkleDensityId, 2f, 80f, 26f);
                    Knob("_SparkleSize", SparkleSizeId, 0.01f, 1f, 0.35f);
                },
            });

            steps.Add(new Step
            {
                chapter = Foil,
                title = "Layer 4: chrome",
                body =
                    "No cubemap and no probe - an analytic environment sampled by the reflection " +
                    "vector:\n\n" +
                    "  env = lerp(_ChromeGround, _ChromeSky, reflect.y * 0.5 + 0.5)\n" +
                    "  sun = pow(saturate(dot(reflect, _ChromeSunDir)), _ChromeSharp)\n\n" +
                    "Damped head-on by 0.3 + 0.7 * (1 - ndv)^1.5, so a mirror finish does not " +
                    "flatly wash out the artwork when the card faces you. It opens up as the card " +
                    "turns away, which is where chrome reads best anyway.\n\n" +
                    "The reflection is taken against the wear-perturbed normal, so this is one of " +
                    "the layers a dent breaks.",
                enter = () => Layer(ChromeStrengthId, 0.8f),
                controls = () => Knob("_ChromeStrength", ChromeStrengthId, 0f, 3f, 0.8f),
            });

            steps.Add(new Step
            {
                chapter = Foil,
                title = "Layer 5: edge glow",
                body =
                    "Fresnel: pow(saturate(1 - ndv), _FresnelPower), tinted by _FresnelColor.\n\n" +
                    "The one layer that does not care which way the card is turned, only how far " +
                    "off-axis it is. It reads as the gloss on a sleeve or a laminate, and it is " +
                    "what stops a turned card looking like a flat sticker at the silhouette.",
                enter = () => Layer(FresnelStrengthId, 1.2f),
                controls = () =>
                {
                    Knob("_FresnelStrength", FresnelStrengthId, 0f, 3f, 1.2f);
                    Knob("_FresnelPower", FresnelPowerId, 0.5f, 16f, 4f);
                },
            });

            steps.Add(new Step
            {
                chapter = Foil,
                title = "Where it is allowed to foil",
                body =
                    "All five layers are added up and then multiplied by one mask:\n\n" +
                    "  mask = (_MaskTex.r if _MASKTEX)\n" +
                    "       * lerp(1, luminance ramp, _MaskFromLuma)\n" +
                    "       * (1 - scuff * _ScuffFoilLoss)\n\n" +
                    "With no mask texture the artwork's own luminance decides: bright areas foil, " +
                    "dark areas stay matte, shaped by _MaskContrast and _MaskBias. Author a mask " +
                    "PNG and only the frame or a sigil foils.\n\n" +
                    "One mask for all five layers is what makes damage read later: a scuffed patch " +
                    "stops diffracting, sparkling and sweeping together, in one line of shader.\n\n" +
                    "Slide _MaskFromLuma to 0 and the foil floods the whole card, artwork and all - " +
                    "which is exactly what it looks like when the mask is wrong.",
                enter = () =>
                {
                    ApplyTier(Tier("Holo"), subject);
                    Layer(SparkleStrengthId, 0.6f);
                },
                controls = () =>
                {
                    Knob("_MaskFromLuma", MaskFromLumaId, 0f, 1f, 0.45f);
                    Knob("_MaskContrast", MaskContrastId, 0.1f, 8f, 2f);
                    Knob("_MaskBias", MaskBiasId, -1f, 1f, 0.1f);
                },
            });

            steps.Add(new Step
            {
                chapter = Foil,
                title = "Keeping it pixel art",
                body =
                    "Three quantisers, and the whole reason the foil sits on the artwork rather " +
                    "than on top of it:\n\n" +
                    "  _HOLOPIXELATE  snaps the foil UVs to the 73x113 grid\n" +
                    "  _RainbowSteps  posterises the hue\n" +
                    "  _ColorSteps    posterises the finished foil\n\n" +
                    "Without them the foil is a smooth gradient over pixel art, and the eye reads " +
                    "two pictures at once.\n\n" +
                    "The foil is then mixed into the base colour by _FoilBlend: 0 is additive, 1 is " +
                    "screen. Rarity tiers are nothing but materials with these numbers set - " +
                    "Card_Common is _FoilIntensity 0, and that is the only difference.",
                enter = () => ApplyTier(Tier("Galaxy"), subject),
                controls = () =>
                {
                    Knob("_ColorSteps", ColorStepsId, 0f, 32f, 0f);
                    Knob("_FoilBlend", FoilBlendId, 0f, 1f, 0.65f);
                    Knob("_FoilIntensity", FoilIntensityId, 0f, 3f, 1f);
                    GUILayout.Space(4);
                    GUILayout.Label("Tiers, read off the shipped materials:");
                    GUILayout.BeginHorizontal();
                    foreach (var tier in tiers)
                    {
                        if (tier == null) continue;
                        string label = tier.name.Replace("Card_", string.Empty);
                        if (GUILayout.Button(label)) ApplyTier(tier, subject);
                    }
                    GUILayout.EndHorizontal();
                },
            });

            // ---------------------------------------------------------------
            // Chapter 2 - the damage
            // ---------------------------------------------------------------
            steps.Add(new Step
            {
                chapter = Damage,
                title = "One map, four channels",
                body =
                    "Five kinds of damage, one RGBA texture per face at the card's own 73x113. " +
                    "CardWear is the only thing that writes it.\n\n" +
                    "  R  scuff    abrasion, scratches, the matte that kills foil\n" +
                    "  G  ink      print coming off\n" +
                    "  B  height   0.5 is flat, below a dent, above a ridge\n" +
                    "  A  missing  material that is not there any more\n\n" +
                    "R and G are per face, so a card has to be restored on both sides. B and A " +
                    "always come from the FRONT map: a dent goes through the paper, a chip is gone " +
                    "from both sides - and DepthOnly only ever samples the front, so a chip it " +
                    "could not see would write depth over a pixel the forward pass had clipped.\n\n" +
                    "The map lives on the CPU as floats. It is 8249 texels: a stroke touches a few " +
                    "hundred, the whole thing uploads in a fraction of a frame, and generation, " +
                    "grading and saving all stay plain C# with no readback to wait on.\n\n" +
                    "Created linear, point, clamp. It is data - a gamma curve on it would bend " +
                    "every rate the tools rub at.",
                enter = () =>
                {
                    Age(CardWear.Damage.All);
                    ApplyTier(Tier("Holo"), subject);
                    showMap = true;
                },
                controls = AgeControls,
            });

            steps.Add(new Step
            {
                chapter = Damage,
                title = "Ageing 1: the edges",
                body =
                    "Edges first, and hardest. A card is handled by its border, pulled in and out " +
                    "of a sleeve by it and squared against a table on it, so the print goes there " +
                    "long before anything happens in the middle.\n\n" +
                    "The weight is (1 - distance to the nearest edge / 5 texels)^2, and corners " +
                    "take it twice over because they are the border of two edges. Value noise on " +
                    "top so the band is grained rather than a clean vignette.\n\n" +
                    "Watch the G channel on the left - and note the front and back maps differ: " +
                    "the noise is seeded apart for each face.",
                enter = () =>
                {
                    Age(CardWear.Damage.EdgeWear);
                    channel = MapChannel.Ink;
                    showMap = true;
                },
                controls = AgeControls,
            });

            steps.Add(new Step
            {
                chapter = Damage,
                title = "Ageing 2: scratches",
                body =
                    "Walked lines, up to 16 of them at severity 1, fading along their length the " +
                    "way a dragged edge lifts off the card. About a quarter are two texels wide, " +
                    "and 60% land on the front.\n\n" +
                    "A scratch writes scuff AND ink, not just scuff. Scuff alone only kills the " +
                    "foil, which leaves a scratch across a dark common card completely invisible.\n\n" +
                    "Damage stamps with max(), not +=, so two scratches crossing do not add up " +
                    "into a hole. What a tool leaves behind is the one thing that does accumulate.",
                enter = () =>
                {
                    Age(CardWear.Damage.Scratches);
                    channel = MapChannel.Scuff;
                    showMap = true;
                },
                controls = AgeControls,
            });

            steps.Add(new Step
            {
                chapter = Damage,
                title = "Ageing 3: dents and creases",
                body =
                    "The B channel, signed around 0.5. A dent is a squared falloff; a crease is a " +
                    "Mexican hat - (1 - d^2) * exp(-d^2) - which is the fold itself with the paper " +
                    "standing up either side of it. The crest loses ink too: print cracks off a " +
                    "fold before anything else gives.\n\n" +
                    "In the shader, four neighbour taps of B become a tangent-space normal. Every " +
                    "foil layer is measured against that normal, so a crease breaks the rainbow, " +
                    "the sweep, the sparkle aim and the chrome at once - without one line of any of " +
                    "them knowing that wear exists. That is the whole reason height is a channel.\n\n" +
                    "The normals are one per card pixel and deliberately blocky. A smooth bump map " +
                    "over pixel art reads as a different card.\n\n" +
                    "Turn the card and watch the bands break along the fold.",
                enter = () =>
                {
                    Age(CardWear.Damage.Dents | CardWear.Damage.Creases);
                    ApplyTier(Tier("Holo"), subject);
                    channel = MapChannel.Height;
                    showMap = true;
                },
                controls = AgeControls,
            });

            steps.Add(new Step
            {
                chapter = Damage,
                title = "Ageing 4: chips",
                body =
                    "The A channel, and the only damage that changes the outline:\n\n" +
                    "  clip(base.a - wear.missing - _Cutoff)\n\n" +
                    "In BOTH passes. Left out of DepthOnly, a chipped pixel writes depth over the " +
                    "background the forward pass clipped through to, and the hole fills in with " +
                    "whatever is behind the card.\n\n" +
                    "Chips are centred ON the border rather than inside it, so the bite opens into " +
                    "the edge instead of turning up as a pinprick in the middle of the frame. " +
                    "Corners chip more often than sides. Round the hole is a torn lip of crushed " +
                    "paper, written into scuff and ink on both faces.",
                enter = () =>
                {
                    Age(CardWear.Damage.Chips);
                    channel = MapChannel.Missing;
                    showMap = true;
                },
                controls = AgeControls,
            });

            steps.Add(new Step
            {
                chapter = Damage,
                title = "Ageing 5: bending",
                body =
                    "Not in the map. Three scalars - _Bow.xy and _CornerBend per corner - applied " +
                    "in the vertex shader, because a bow has to change the silhouette and no amount " +
                    "of shading will do that.\n\n" +
                    "That is why CardQuad.asset is a 16x24 grid rather than the four-vertex quad it " +
                    "started as: a vertex shader can only move vertices that exist. The mesh bounds " +
                    "reserve 0.14 units of headroom, or a bowed card culls against a slab it no " +
                    "longer fits inside and pops out of view at the edge of the screen.\n\n" +
                    "Creases deep enough to matter displace the mesh too, read out of B with a " +
                    "LINEAR filter - the fragment stage uses the point one - so the fold does not " +
                    "stair-step across the mesh grid.\n\n" +
                    "CardApplyBend rebuilds the normal and tangent from three evaluations of the " +
                    "displacement. CardApplyBendPosition in DepthOnly must agree with it exactly, " +
                    "or depth and colour disagree about where the card is.",
                enter = () =>
                {
                    Age(CardWear.Damage.Bend);
                    ApplyTier(Tier("Shiny"), subject);
                    showMap = false;
                },
                controls = AgeControls,
            });

            steps.Add(new Step
            {
                chapter = Damage,
                title = "Reading it back as a grade",
                body =
                    "CardCondition averages each channel over the whole card, then divides by what " +
                    "a severity-1 card actually averages:\n\n" +
                    "  ScuffFull 0.065   InkFull 0.090\n" +
                    "  DentFull  0.038   MissingFull 0.005\n\n" +
                    "Those are measured, not guessed. The raw means are tiny because damage " +
                    "concentrates - a chipped corner is sixty texels out of eight thousand - and " +
                    "scored against 1 every card in the game grades Mint.\n\n" +
                    "  score = 1 - (scuff*.28 + ink*.24 + missing*.22 + dent*.16 + bend*.10)\n\n" +
                    "Then a stepped price multiplier, 0.2x at Poor up to 1.6x at Mint. Stepped on " +
                    "purpose: a collector pays for the label, so the rub that tips Excellent into " +
                    "Near Mint has to be worth something visible.\n\n" +
                    "Change how ageing works and those four constants have to be re-measured. " +
                    "Tools > Cozy TGC > Render Wear Preview logs the ramp they come from.",
                enter = () =>
                {
                    Age(CardWear.Damage.All);
                    ApplyTier(Tier("Holo"), subject);
                    showMap = true;
                },
                controls = () =>
                {
                    AgeControls();
                    GUILayout.Space(4);
                    GUILayout.Label("The measured ramp:");
                    GUILayout.BeginHorizontal();
                    foreach (float s in new[] { 0f, 0.3f, 0.55f, 0.85f, 1f })
                    {
                        if (!GUILayout.Button(s.ToString("0.00"))) continue;
                        severity = s;
                        Age(CardWear.Damage.All);
                    }
                    GUILayout.EndHorizontal();
                },
            });

            // ---------------------------------------------------------------
            // Chapter 3 - restoring
            // ---------------------------------------------------------------
            steps.Add(new Step
            {
                chapter = Restoring,
                title = "The bench",
                body =
                    "Rub() is the only edit path there is. Age stamps damage into the map, a tool " +
                    "rubs it back out - the same system read in two directions, which is what keeps " +
                    "them one thing rather than two that have to agree.\n\n" +
                    "A RestorationTool is nothing but rates: what it takes off, how deep it reaches " +
                    "(scuffFloor, inkFloor), and what it puts back on in exchange.\n\n" +
                    "How to work:\n" +
                    "  hold the pointer on the right card to rub\n" +
                    "  hold SPACE to turn the card with the tool still in hand\n" +
                    "  1-6 pick a tool directly, 0 puts it down\n\n" +
                    "R at any point re-runs the current step, which re-ages the card.\n\n" +
                    "The next six steps are in the order the rates reward. Nothing enforces it.",
                enter = () =>
                {
                    severity = 0.85f;
                    Age(CardWear.Damage.All);
                    ApplyTier(Tier("Holo"), subject);
                    showMap = true;
                },
                controls = ToolControls,
            });

            AddToolStep(Restoring, 0, "Press",
                "Weight, not a stroke. wholeCard is set, so where the pointer sits does not " +
                "matter - it works the whole card at once.\n\n" +
                "  bendRate    0.35/s   bow and every folded corner towards flat\n" +
                "  flattenRate 0.15/s   dents, gently, on the way past\n\n" +
                "Flatten first. Everything after this is a spot tool, and working a spot on a " +
                "bowed card is working it at the wrong angle.\n\n" +
                "It is the one tool in the set that costs nothing at all.");

            AddToolStep(Restoring, 1, "Burnishing Bone",
                "  flattenRate 0.8/s    rolls dents and creases towards flat\n" +
                "  scuffCost   0.1/s    while it works\n" +
                "  overworkScuff 0.03/s where there is nothing left to flatten\n\n" +
                "Burnishing is polishing and polishing is abrasion, so it leaves its own marks. " +
                "That is fine here, because the pad comes next and takes scuff off. Do it after " +
                "the pad and you have just undone the pad.\n\n" +
                "Watch the height channel empty out on the left while the scuff channel fills.");

            AddToolStep(Restoring, 2, "Abrasive Pad",
                "  scuffRate 0.9/s, scuffFloor 0   takes scratches all the way out\n" +
                "  inkCost   0.12/s                lifts print while it does\n" +
                "  overworkScuff 0.1/s             the second worst in the set\n\n" +
                "The cloth cannot do this: its scuffFloor is 0.4, so a real scratch stops it dead. " +
                "This is the only tool that reaches the bottom.\n\n" +
                "inkCost is a cost, not a ratchet. Above about 0.15 the pad would take off more " +
                "print than the pen can lay back, no order of tools would improve a card, and that " +
                "reads to a player as the game being broken rather than as a trade-off they are " +
                "getting wrong.");

            AddToolStep(Restoring, 3, "Paper Fill",
                "  fillRate 0.5/s     puts a chipped edge back\n" +
                "  inkCost  0.8/s     the highest in the set - the fill arrives blank\n" +
                "  radius   0.05      the smallest in the set\n\n" +
                "Dwell it on the chip. Sweeping it round the whole border spends its time where " +
                "there is nothing to fill, and every texel with nothing left to do is charged " +
                "overworkScuff instead.\n\n" +
                "This is why chips barely move under a careless pass, and why a chipped card stays " +
                "cheap more or less forever. That is the intended outcome, not a gap in the tools.\n\n" +
                "Try the border pass below, then try holding the pointer on one chipped corner, " +
                "and compare what the grade does.");

            AddToolStep(Restoring, 4, "Touch-Up Pen",
                "  inkRate 1.1/s, inkFloor 0   lays colour back in\n" +
                "  overworkScuff 0.3/s         by far the worst in the set\n\n" +
                "After the pad, never before. Go the other way and the pad takes off the ink the " +
                "pen just laid.\n\n" +
                "Keep going past done and it beads up: with the ink at zero there is nothing left " +
                "for it to do, and 0.3 a second of scuff is what it charges for the privilege. " +
                "Sweeping a pen over the whole face is the single worst thing you can do to a card.");

            AddToolStep(Restoring, 5, "Soft Cloth",
                "  scuffRate 0.55/s, scuffFloor 0.4   lifts haze and dust, and no more\n" +
                "  overworkScuff 0.02/s               the lightest touch in the set\n\n" +
                "Last, and for one job: polishing away the scuff the burnisher and the pen left " +
                "behind. It cannot reach a real scratch - the floor stops it at 0.4 - so reaching " +
                "for it first feels like it is working and achieves nothing.");

            steps.Add(new Step
            {
                chapter = Restoring,
                title = "Overworking",
                body =
                    "Pick any tool and hold the pointer in one place. Watch the score fall.\n\n" +
                    "Every tool charges overworkScuff on any texel where it has nothing left to " +
                    "do. Nothing left to fix means you are no longer restoring the card, you are " +
                    "just rubbing it - and rubbing a card is how it got like this.\n\n" +
                    "That single rule is what orders the six steps without one check enforcing the " +
                    "order, and what makes a light touch worth having. Change a rate and you change " +
                    "the puzzle; there is no sequence table to keep in step with it.\n\n" +
                    "It is also why a full clumsy pass with everything takes an 0.85 card from " +
                    "Played back to Good and no further. Dents and bending come out completely, " +
                    "scuff and ink only partly, chips hardly at all.",
                enter = () =>
                {
                    severity = 0.85f;
                    Age(CardWear.Damage.All);
                    ApplyTier(Tier("Holo"), subject);
                    toolIndex = 4;
                    showMap = true;
                },
                controls = ToolControls,
            });

            steps.Add(new Step
            {
                chapter = Restoring,
                title = "What gets saved",
                body =
                    "Never the map.\n\n" +
                    "A CardWear.State is the seed, the severity, which kinds were stamped, and the " +
                    "list of strokes. Generation and rubbing are both deterministic, so replaying " +
                    "them lands on exactly the same texels - a card worked on for an hour saves in " +
                    "a few kilobytes instead of the map's sixty-six.\n\n" +
                    "A stroke stores uv, which face, how long, and the tool's RATES. The name and " +
                    "the blurb are dropped on the way in, because Stamp never reads anything else " +
                    "off the tool - they are for the UI, not for the replay.\n\n" +
                    "Capture, wipe the card, load it back. If the two ever disagree, something in " +
                    "ageing or rubbing has stopped being deterministic.",
                enter = () =>
                {
                    severity = 0.85f;
                    Age(CardWear.Damage.All);
                    ApplyTier(Tier("Holo"), subject);
                    toolIndex = 2;
                    showMap = true;
                },
                controls = () =>
                {
                    ToolControls();
                    GUILayout.Space(4);
                    var wear = Wear;
                    if (wear == null) return;
                    if (GUILayout.Button("Capture -> Pristine -> Load"))
                    {
                        var state = wear.Capture();
                        var before = wear.Condition.Score;
                        wear.MakePristine();
                        wear.Load(state);
                        Debug.Log($"[Cozy TGC] Replay: {before:0.0000} -> {wear.Condition.Score:0.0000} " +
                                  $"over {state.strokes.Count} strokes.");
                    }
                    GUILayout.Label("Check the console: the two scores must match to four places.");
                },
            });
        }

        void AddToolStep(string chapter, int tool, string title, string body)
        {
            steps.Add(new Step
            {
                chapter = chapter,
                title = $"{tool + 1} - {title}",
                body = body,
                enter = () =>
                {
                    severity = 0.85f;
                    Age(CardWear.Damage.All);
                    ApplyTier(Tier("Holo"), subject);
                    toolIndex = tool;
                    showMap = true;
                },
                controls = ToolControls,
            });
        }

        // -------------------------------------------------------------------
        // Panel controls shared by several steps
        // -------------------------------------------------------------------
        void AgeControls()
        {
            var wear = Wear;
            if (wear == null) return;

            GUILayout.Label($"Severity: {severity:0.00}");
            float next = GUILayout.HorizontalSlider(severity, 0f, 1f);
            if (!Mathf.Approximately(next, severity))
            {
                severity = next;
                Age(stepKinds);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Re-age")) Age(stepKinds);
            if (GUILayout.Button("Pristine")) wear.MakePristine();
            if (GUILayout.Button("New seed"))
            {
                wearSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                Age(stepKinds);
            }
            GUILayout.EndHorizontal();
        }

        void ToolControls()
        {
            var tools = RestorationTools.All;
            GUILayout.Label(Turning ? "Turning - drag spins, click flips"
                                    : "Tool in hand - hold the pointer on the right card");

            int picked = GUILayout.SelectionGrid(toolIndex + 1, ToolLabels(tools), 2) - 1;
            if (picked != toolIndex) toolIndex = picked;

            if (HasTool)
            {
                GUILayout.Label(tools[toolIndex].blurb);
                GUILayout.BeginHorizontal();
                GUI.enabled = !passRunning;
                if (GUILayout.Button("Run a pass")) StartPass(6f, border: false);
                if (GUILayout.Button("Border pass")) StartPass(4f, border: true);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                if (passRunning) GUILayout.Label($"working... {passIndex * 100 / Mathf.Max(1, passSteps)}%");
            }

            GUILayout.Space(2);
            AgeControls();
        }

        string[] toolLabels;

        string[] ToolLabels(RestorationTool[] tools)
        {
            if (toolLabels != null && toolLabels.Length == tools.Length + 1) return toolLabels;
            toolLabels = new string[tools.Length + 1];
            toolLabels[0] = "0  Turn";
            for (int i = 0; i < tools.Length; i++) toolLabels[i + 1] = $"{i + 1}  {tools[i].name}";
            return toolLabels;
        }

        void Knob(string label, int id, float min, float max, float fallback)
        {
            if (subject == null) return;
            float value = subject.GetFloat(id, fallback);
            GUILayout.Label($"{label}: {value:0.00}");
            float next = GUILayout.HorizontalSlider(value, min, max);
            if (!Mathf.Approximately(next, value)) subject.SetFloat(id, next);
        }

        // -------------------------------------------------------------------
        // The panels
        // -------------------------------------------------------------------
        void OnGUI()
        {
            titleStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 15 };
            chapterStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            bodyStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };

            DrawCardLabels();
            if (!showPanels)
            {
                GUI.Label(new Rect(12f, 12f, 300f, 20f), "H: show the walkthrough");
                return;
            }

            DrawGuide();
            DrawReadout();
        }

        void DrawGuide()
        {
            if (steps.Count == 0) return;
            var step = steps[index];

            GUILayout.BeginArea(GuideRect, GUI.skin.box);

            GUILayout.Label(step.chapter, chapterStyle);
            GUILayout.Label($"{index + 1} / {steps.Count}   {step.title}", titleStyle);
            GUILayout.Space(4);

            guideScroll = GUILayout.BeginScrollView(guideScroll);
            GUILayout.Label(step.body, bodyStyle);
            GUILayout.Space(6);
            step.controls?.Invoke();
            GUILayout.EndScrollView();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUI.enabled = index > 0;
            if (GUILayout.Button("< Back")) Enter(index - 1);
            GUI.enabled = index < steps.Count - 1;
            if (GUILayout.Button("Next >")) Enter(index + 1);
            GUI.enabled = true;
            if (GUILayout.Button("Chapter")) Enter(NextChapter());
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool turn = GUILayout.Toggle(autoTurn, "auto-turn (T)");
            if (turn != autoTurn) SetAutoTurn(turn);
            showMap = GUILayout.Toggle(showMap, "wear map (M)");
            GUILayout.EndHorizontal();
            GUILayout.Label("arrows: step   Tab: chapter   R: redo   H: hide", bodyStyle);

            GUILayout.EndArea();
        }

        void DrawReadout()
        {
            GUILayout.BeginArea(ReadoutRect, GUI.skin.box);
            GUILayout.Label("Readout", chapterStyle);

            Vector2 tilt = Tilt(subject);
            GUILayout.Label($"tilt at the card centre: ({tilt.x:0.00}, {tilt.y:0.00})\n" +
                            $"|tilt| {tilt.magnitude:0.00}   showing {(subject != null && subject.ShowingBack ? "back" : "front")}");

            GUILayout.Space(2);
            GUILayout.Label("Debug view (CardDebug.hlsl)", chapterStyle);
            int picked = GUILayout.SelectionGrid(debugView, DebugViews, 5);
            if (picked != debugView) SetDebugView(picked);

            if (showGpu) DrawGpu();
            if (showTexture) DrawTextureInspector();

            var wear = Wear;
            if (wear == null || !wear.IsWorn)
            {
                if (showMap) GUILayout.Label("Card is pristine - no map allocated yet.");
                GUILayout.EndArea();
                return;
            }

            var condition = wear.Condition;
            GUILayout.Space(4);
            GUILayout.Label($"{CardCondition.Label(condition.Grade)}   " +
                            $"score {condition.Score:0.00}   x{condition.PriceMultiplier:0.00}", titleStyle);
            GUILayout.Label($"scuff {condition.Scuff:0.00}   ink {condition.InkLoss:0.00}   dent {condition.Dent:0.00}\n" +
                            $"chip  {condition.Missing:0.00}   bend {condition.Bend:0.00}   " +
                            $"bow ({wear.Bow.x:0.000}, {wear.Bow.y:0.000})");

            if (!showMap)
            {
                GUILayout.EndArea();
                return;
            }

            GUILayout.Space(4);
            channel = (MapChannel)GUILayout.SelectionGrid((int)channel,
                new[] { "RGB", "R scuff", "G ink", "B height", "A missing" }, 3);
            showBackMap = GUILayout.Toggle(showBackMap, "show the back map");

            RefreshMapView(wear);
            if (mapView != null)
            {
                var rect = GUILayoutUtility.GetRect(ReadoutSize.x - 24f, 250f);
                GUI.DrawTexture(rect, mapView, ScaleMode.ScaleToFit, false);
            }
            GUILayout.Label(ChannelHint(), bodyStyle);

            GUILayout.EndArea();
        }

        /// <summary>
        /// What the two stages are actually being asked to do, this frame, on this
        /// card. The ratio between the two numbers is the only thing that decides
        /// whether a line belongs in the vertex stage or the fragment stage, and it
        /// is far easier to believe measured than asserted.
        /// </summary>
        void DrawGpu()
        {
            if (subject == null) return;
            var filter = subject.GetComponentInChildren<MeshFilter>();
            var renderer = subject.Renderer != null ? subject.Renderer : subject.GetComponentInChildren<MeshRenderer>();
            var mesh = filter != null ? filter.sharedMesh : null;

            int vertices = mesh != null ? mesh.vertexCount : 0;
            int triangles = mesh != null ? (int)(mesh.GetIndexCount(0) / 3) : 0;
            int fragments = ScreenPixels(subject);

            GUILayout.Space(2);
            GUILayout.Label($"vertex stage    {vertices} runs ({triangles} tris)\n" +
                            $"fragment stage  ~{fragments} runs\n" +
                            $"ratio           1 : {(vertices > 0 ? fragments / vertices : 0)}", bodyStyle);
            GUILayout.Label("(fragments = the card's screen bounding box, one pass. The order of " +
                            "magnitude is the point, not the digits.)", bodyStyle);

            var material = renderer != null ? renderer.sharedMaterial : null;
            if (material != null)
            {
                string[] keywords = material.shaderKeywords;
                GUILayout.Space(2);
                GUILayout.Label($"material: {material.name}\n" +
                                $"keywords: {(keywords.Length == 0 ? "(none)" : string.Join(", ", keywords))}",
                                bodyStyle);
            }

            if (overlay == null) return;
            GUILayout.BeginHorizontal();
            bool wire = GUILayout.Toggle(overlay.WireOn, "wire");
            bool dots = GUILayout.Toggle(overlay.PointsOn, "verts");
            bool frame = GUILayout.Toggle(overlay.FramesOn, "frame");
            GUILayout.EndHorizontal();
            if (wire != overlay.WireOn || dots != overlay.PointsOn || frame != overlay.FramesOn)
                overlay.Show(wire, dots, frame);
        }

        /// <summary>
        /// The artwork as the sampler sees it, with a crosshair where the pointer
        /// is on the card. Naming the texel under the cursor is the shortest route
        /// from "uv is a number between 0 and 1" to what that number is for.
        /// </summary>
        void DrawTextureInspector()
        {
            var face = CurrentFace();
            if (face == null) return;

            GUILayout.Space(2);
            Rect area = GUILayoutUtility.GetRect(ReadoutSize.x - 24f, 120f);
            Rect fitted = Fit(area, face.width / (float)face.height);
            GUI.DrawTexture(fitted, face, ScaleMode.StretchToFill, true);

            if (!PointerUV(out Vector2 uv))
            {
                GUILayout.Label("point at the right-hand card", bodyStyle);
                return;
            }

            // GUI counts down from the top, uv counts up from the bottom.
            float x = fitted.x + uv.x * fitted.width;
            float y = fitted.yMax - uv.y * fitted.height;
            Color previous = GUI.color;
            GUI.color = new Color(1f, 0.3f, 0.3f, 0.9f);
            GUI.DrawTexture(new Rect(fitted.x, y, fitted.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x, fitted.y, 1f, fitted.height), Texture2D.whiteTexture);
            GUI.color = previous;

            int tx = Mathf.Clamp(Mathf.FloorToInt(uv.x * face.width), 0, face.width - 1);
            int ty = Mathf.Clamp(Mathf.FloorToInt(uv.y * face.height), 0, face.height - 1);
            GUILayout.Label($"uv ({uv.x:0.000}, {uv.y:0.000})  ->  texel ({tx}, {ty})  " +
                            $"of {face.width}x{face.height}", bodyStyle);
        }

        /// <summary>Largest rect of the given aspect that fits inside another.</summary>
        static Rect Fit(Rect area, float aspect)
        {
            float width = Mathf.Min(area.width, area.height * aspect);
            float height = width / Mathf.Max(aspect, 1e-4f);
            return new Rect(area.x + (area.width - width) * 0.5f,
                            area.y + (area.height - height) * 0.5f, width, height);
        }

        /// <summary>
        /// Screen-space bounding box of the card's four corners. Generous - a turned
        /// card does not fill its own box, and clipped texels never reach the
        /// fragment stage - and it counts one pass, not both.
        /// </summary>
        int ScreenPixels(CardView card)
        {
            if (cam == null || card == null) return 0;
            Transform visual = card.transform.childCount > 0 ? card.transform.GetChild(0) : card.transform;
            float w = card.Size.x * 0.5f, h = card.Size.y * 0.5f;

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                var local = new Vector3((i & 1) == 0 ? -w : w, (i & 2) == 0 ? -h : h, 0f);
                Vector3 screen = cam.WorldToScreenPoint(visual.TransformPoint(local));
                if (screen.z <= 0f) return 0;
                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }
            return Mathf.RoundToInt((max.x - min.x) * (max.y - min.y));
        }

        string ChannelHint() => channel switch
        {
            MapChannel.Raw => "Raw texels: red is scuff, green is ink, blue is height (mid = flat).",
            MapChannel.Scuff => "Scuff. White is fully abraded - the foil is dead there.",
            MapChannel.Ink => "Ink loss. White is bare card stock.",
            MapChannel.Height => "Height. Blue is a dent, orange a ridge, grey is flat.",
            _ => "Missing material. White is a hole - it is clipped away in both passes.",
        };

        /// <summary>
        /// Rebuilds the little map view from the live wear texture. Flipped in V:
        /// the map's first texel is the bottom-left, and IMGUI counts down from
        /// the top.
        /// </summary>
        void RefreshMapView(CardWear wear)
        {
            Texture2D src = showBackMap ? wear.BackMap : wear.FrontMap;
            if (src == null) return;

            if (mapView == null || mapView.width != src.width || mapView.height != src.height)
            {
                if (mapView != null) Destroy(mapView);
                mapView = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
                {
                    name = "CardWearMapView",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            NativeArray<Color32> from = src.GetRawTextureData<Color32>();
            NativeArray<Color32> to = mapView.GetRawTextureData<Color32>();
            int w = src.width, h = src.height;
            for (int y = 0; y < h; y++)
            {
                int read = y * w;
                int write = (h - 1 - y) * w;
                for (int x = 0; x < w; x++) to[write + x] = Paint(from[read + x]);
            }
            mapView.Apply(false);
        }

        Color32 Paint(Color32 t) => channel switch
        {
            MapChannel.Raw => new Color32(t.r, t.g, t.b, 255),
            MapChannel.Scuff => Grey(t.r),
            MapChannel.Ink => Grey(t.g),
            MapChannel.Height => Height(t.b),
            _ => Grey(t.a),
        };

        static Color32 Grey(byte v) => new Color32(v, v, v, 255);

        /// <summary>Signed, so a dent and a ridge do not look like the same damage.</summary>
        static Color32 Height(byte raw)
        {
            float h = (raw / 255f - 0.5f) * 2f;
            float dent = Mathf.Clamp01(-h);
            float ridge = Mathf.Clamp01(h);
            return new Color32(
                (byte)(28f + ridge * 227f),
                (byte)(28f + (dent + ridge) * 0.5f * 227f),
                (byte)(28f + dent * 227f),
                255);
        }

        /// <summary>
        /// The shader's tilt, worked out on the CPU at the card's centre. The real
        /// one is per pixel and taken against the wear-perturbed normal; this is
        /// the same arithmetic on a flat card, which is what makes it a readout of
        /// the shader rather than a second opinion.
        /// </summary>
        Vector2 Tilt(CardView card)
        {
            if (card == null || cam == null) return Vector2.zero;
            Transform visual = card.transform.childCount > 0 ? card.transform.GetChild(0) : card.transform;

            Vector3 V = (cam.transform.position - visual.position).normalized;
            Vector3 N = -visual.forward;   // the card faces -Z
            Vector3 T = visual.right;
            Vector3 B = visual.up;

            // Whichever face points at the camera wins, exactly as the fragment
            // stage decides it. B is not flipped there either.
            float facing = Vector3.Dot(N, V) >= 0f ? 1f : -1f;
            N *= facing;
            T *= facing;

            var vT = new Vector3(Vector3.Dot(V, T), Vector3.Dot(V, B), Vector3.Dot(V, N));
            float gain = card.GetFloat(TiltGainId, 1f);
            Vector2 tilt = new Vector2(vT.x, vT.y) / Mathf.Max(vT.z, 0.15f) * gain;
            return tilt / (1f + 0.35f * tilt.magnitude);
        }

        void DrawCardLabels()
        {
            if (cam == null) return;
            Label(reference, "the finished card");
            Label(subject, steps.Count > 0 ? steps[index].title : "subject");
        }

        void Label(CardView card, string text)
        {
            if (card == null) return;
            Vector3 world = card.transform.position + Vector3.down * (card.Size.y * 0.5f + 0.16f);
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0f) return;
            var rect = new Rect(screen.x - 110f, Screen.height - screen.y - 10f, 220f, 20f);
            GUI.Label(rect, text, labelStyle);
        }

        // -------------------------------------------------------------------
        // Input plumbing - both the new Input System and the legacy manager.
        // -------------------------------------------------------------------
#if ENABLE_INPUT_SYSTEM
        static bool KeyPressed(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }

        static bool KeyHeld(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].isPressed;
        }
#else
        // Digit0..Digit6 have to stay contiguous: the tool row is picked with
        // Key.Digit1 + index, the same way the Input System's own enum allows.
        enum Key { RightArrow, LeftArrow, Tab, R, T, H, M, Space, Digit0, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6 }

        static KeyCode Map(Key key) => key switch
        {
            Key.RightArrow => KeyCode.RightArrow,
            Key.LeftArrow => KeyCode.LeftArrow,
            Key.Tab => KeyCode.Tab,
            Key.R => KeyCode.R,
            Key.T => KeyCode.T,
            Key.H => KeyCode.H,
            Key.M => KeyCode.M,
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
        static bool KeyHeld(Key key) => Input.GetKey(Map(key));
#endif

#if UNITY_EDITOR
        public void EditorBind(Camera camera, CardInteractor cardInteractor,
                               CardView referenceCard, CardView subjectCard,
                               Material snappedMaterial, Material smoothMaterial, Material[] tierMaterials,
                               CardMeshOverlay meshOverlay, string deckFolder)
        {
            cam = camera;
            interactor = cardInteractor;
            reference = referenceCard;
            subject = subjectCard;
            snapped = snappedMaterial;
            smooth = smoothMaterial;
            tiers = tierMaterials;
            overlay = meshOverlay;
            resourceFolder = deckFolder;
        }
#endif
    }
}
