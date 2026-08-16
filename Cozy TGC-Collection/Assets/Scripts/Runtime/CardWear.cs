using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// One card's condition, and the only thing that writes it. Five kinds of
    /// damage - scuffs, ink loss, dents, creases, chips - all live in a single
    /// RGBA map per face at the card's own pixel size, plus three scalars for the
    /// bending, which is geometry and cannot be painted.
    ///
    /// Ageing and restoring are the same system read in two directions: ageing
    /// stamps damage into the map, a tool rubs it back out. <see cref="Rub"/> is
    /// the only edit path there is, which is what keeps them one thing.
    ///
    /// The map is held on the CPU on purpose. It is 73x113: a brush stroke touches
    /// a few hundred texels, the whole thing uploads in a fraction of a frame, and
    /// generation, grading and saving all stay plain C# with no readback to wait
    /// on and no ping-pong buffer to keep in step.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CardView))]
    public class CardWear : MonoBehaviour
    {
        static readonly int WearTexId = Shader.PropertyToID("_WearTex");
        static readonly int WearBackTexId = Shader.PropertyToID("_WearBackTex");
        static readonly int WearAmountId = Shader.PropertyToID("_WearAmount");
        static readonly int BowId = Shader.PropertyToID("_Bow");
        static readonly int CornerBendId = Shader.PropertyToID("_CornerBend");

        /// <summary>
        /// Bow at the centre of a fully ruined card, in world units. Generous
        /// against the card's 0.73 x 1.13: a bowed card is obvious in the hand, and
        /// anything subtle enough to be plausible is invisible on screen.
        /// Assets/Meshes bounds reserve room for this - see MeshBendHeadroom.
        /// </summary>
        public const float MaxBow = 0.035f;
        /// <summary>Lift of a fully folded corner, in world units.</summary>
        public const float MaxCornerBend = 0.07f;

        /// <summary>
        /// One texel of the map, kept in floats rather than in the bytes it is
        /// uploaded as. A brush is soft, so its outermost texels are rubbed at a
        /// twentieth of the centre's rate - in bytes that rounds to no change at
        /// all every frame, and the brush grows a hard edge it should not have.
        /// </summary>
        struct Texel
        {
            public float Scuff;
            public float Ink;
            /// <summary>Signed: below zero is a dent, above is a ridge. Front map only.</summary>
            public float Height;
            /// <summary>Material that is not there any more. Front map only.</summary>
            public float Missing;
        }

        [Tooltip("Map resolution. Must match _CardPixels on the material, or wear " +
                 "texels stop landing on artwork texels and the damage goes blurry.")]
        [SerializeField] Vector2Int resolution = new Vector2Int(73, 113);

        [Tooltip("How hard a card is aged on spawn when nothing else says otherwise. " +
                 "0 is straight out of the pack.")]
        [SerializeField, Range(0f, 1f)] float defaultSeverity;

        CardView view;
        Texel[] front, back;
        Texture2D frontMap, backMap;

        Vector2 bow;
        Vector4 cornerBend;

        bool frontDirty, backDirty, bendDirty;
        bool conditionDirty = true;
        CardCondition condition = CardCondition.Pristine;

        int seed;
        float severity;
        readonly List<Stroke> strokes = new List<Stroke>();

        int W => resolution.x;
        int H => resolution.y;

        /// <summary>True once this card has been aged and has a map to hold it.</summary>
        public bool IsWorn => front != null;

        /// <summary>The card as a grader would read it. Recomputed only when the map has moved.</summary>
        public CardCondition Condition
        {
            get
            {
                if (conditionDirty) Recompute();
                return condition;
            }
        }

        public Vector2 Bow => bow;
        public Vector4 CornerBend => cornerBend;

        void Awake()
        {
            view = GetComponent<CardView>();
            if (defaultSeverity > 0f)
                Age(UnityEngine.Random.Range(int.MinValue, int.MaxValue), defaultSeverity);
        }

        void LateUpdate() => Flush();

        void OnDestroy()
        {
            if (frontMap != null) Destroy(frontMap);
            if (backMap != null) Destroy(backMap);
        }

        // -------------------------------------------------------------------
        // Ageing
        // -------------------------------------------------------------------
        /// <summary>
        /// Stamps a card with the damage it arrives carrying. Deterministic from
        /// the seed, so the same card ages the same way every time it is loaded
        /// and none of it has to be stored.
        /// </summary>
        public void Age(int cardSeed, float cardSeverity)
        {
            seed = cardSeed;
            severity = Mathf.Clamp01(cardSeverity);
            strokes.Clear();
            Rebuild();
        }

        /// <summary>Puts the card back to how it left the printer.</summary>
        public void MakePristine()
        {
            severity = 0f;
            strokes.Clear();
            Rebuild();
        }

        void Rebuild()
        {
            EnsureMaps();
            Array.Clear(front, 0, front.Length);
            Array.Clear(back, 0, back.Length);
            bow = Vector2.zero;
            cornerBend = Vector4.zero;

            if (severity > 0f) Generate();
            for (int i = 0; i < strokes.Count; i++) Stamp(strokes[i]);

            frontDirty = backDirty = bendDirty = conditionDirty = true;
        }

        void Generate()
        {
            var rng = new System.Random(seed);
            float s = severity;

            EdgeWear(rng, s);

            int scratches = Mathf.RoundToInt(Mathf.Lerp(0f, 16f, s * s));
            for (int i = 0; i < scratches; i++) Scratch(rng, s);

            int dents = Mathf.RoundToInt(Mathf.Lerp(0f, 7f, s));
            for (int i = 0; i < dents; i++) Dent(rng, s);

            int creases = s < 0.45f ? 0 : (s < 0.8f ? 1 : 2);
            for (int i = 0; i < creases; i++) Crease(rng, s);

            int chips = Mathf.RoundToInt(Mathf.Lerp(0f, 5f, Mathf.InverseLerp(0.25f, 1f, s)));
            for (int i = 0; i < chips; i++) Chip(rng, s);

            // A card that has been sat on is bowed, and the corner that was on the
            // outside of the pile is the one that turned up.
            bow = new Vector2(Range(rng, -1f, 1f), Range(rng, -1f, 1f)) * MaxBow * s;
            if (s > 0.35f) cornerBend[rng.Next(0, 4)] = Range(rng, 0.4f, 1f) * MaxCornerBend * s;
        }

        /// <summary>
        /// Edges first, and hardest. A card is handled by its border, pulled in and
        /// out of a sleeve by it and squared against a table on it, which is why the
        /// print goes there long before anything happens in the middle.
        /// </summary>
        void EdgeWear(System.Random rng, float s)
        {
            const float band = 5f;
            int hash = rng.Next();

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float edge = 1f - Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y)) / band;
                    if (edge <= 0f) continue;
                    edge *= edge;

                    // Corners take it twice over: they are the border of two edges.
                    float cx = Mathf.Abs(x / (W - 1f) * 2f - 1f);
                    float cy = Mathf.Abs(y / (H - 1f) * 2f - 1f);
                    float corner = Mathf.Clamp01((cx + cy - 1.2f) / 0.8f);
                    float weight = edge * (0.55f + corner) * s;

                    int i = x + y * W;
                    Raise(ref front[i].Ink, weight * (0.55f + 0.45f * Noise(x, y, hash)));
                    Raise(ref back[i].Ink, weight * (0.55f + 0.45f * Noise(x, y, hash + 91)));
                    Raise(ref front[i].Scuff, edge * 0.6f * s * Noise(x, y, hash + 17));
                    Raise(ref back[i].Scuff, edge * 0.6f * s * Noise(x, y, hash + 53));
                }
            }
        }

        void Scratch(System.Random rng, float s)
        {
            var map = rng.NextDouble() < 0.6 ? front : back;
            float angle = Range(rng, 0f, Mathf.PI * 2f);
            float length = Range(rng, 6f, 34f) * Mathf.Lerp(0.5f, 1f, s);
            float depth = Range(rng, 0.3f, 1f) * s;
            float wobble = Range(rng, -0.02f, 0.02f);
            bool wide = rng.NextDouble() < 0.25;

            float x0 = Range(rng, 0f, W);
            float y0 = Range(rng, 0f, H);

            for (float t = 0f; t < length; t += 0.7f)
            {
                float a = angle + wobble * t;
                int px = Mathf.RoundToInt(x0 + Mathf.Cos(a) * t);
                int py = Mathf.RoundToInt(y0 + Mathf.Sin(a) * t);
                // Fades along its length, the way a dragged edge lifts off the card.
                float fade = depth * (1f - t / length * 0.6f);
                // A scratch takes print with it - that is what makes it visible at
                // all. Scuff alone only kills the foil, which leaves a common card
                // looking untouched and a scratch across dark artwork invisible.
                RaiseAt(map, px, py, fade, fade * 0.55f);
                if (wide) RaiseAt(map, px + (Mathf.Sin(a) > 0f ? 1 : -1), py, fade * 0.5f, fade * 0.25f);
            }
        }

        void Dent(System.Random rng, float s)
        {
            float r = Range(rng, 1.6f, 5f);
            // Biased outwards: the middle of a card is the best protected part of it.
            bool nearEdge = rng.NextDouble() < 0.65;
            int cx = nearEdge && rng.NextDouble() < 0.5
                ? (rng.NextDouble() < 0.5 ? rng.Next(0, Mathf.Max(1, W / 5)) : rng.Next(W * 4 / 5, W))
                : rng.Next(0, W);
            int cy = nearEdge
                ? (rng.NextDouble() < 0.5 ? rng.Next(0, Mathf.Max(1, H / 5)) : rng.Next(H * 4 / 5, H))
                : rng.Next(0, H);
            float depth = Range(rng, 0.3f, 0.9f) * s;

            int ri = Mathf.CeilToInt(r);
            for (int y = cy - ri; y <= cy + ri; y++)
            {
                for (int x = cx - ri; x <= cx + ri; x++)
                {
                    if (!Inside(x, y)) continue;
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                    if (d >= 1f) continue;
                    float f = (1f - d) * (1f - d);
                    int i = x + y * W;
                    front[i].Height = Mathf.Clamp(front[i].Height - depth * f, -1f, 1f);
                    Raise(ref front[i].Scuff, depth * f * 0.35f);
                }
            }
        }

        /// <summary>
        /// A fold, not a scratch: a valley along the line with the paper standing
        /// up either side of it, which is what a card actually does when it is bent
        /// and pressed back. The crest loses ink too - print cracks off a fold
        /// before anything else on the card gives.
        /// </summary>
        void Crease(System.Random rng, float s)
        {
            bool horizontal = rng.NextDouble() < 0.55;
            float angle = (horizontal ? 0f : Mathf.PI * 0.5f) + Range(rng, -0.35f, 0.35f);
            var n = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle));
            var mid = new Vector2(W * 0.5f, H * 0.5f);
            float offset = Vector2.Dot(mid, n) + Range(rng, -0.3f, 0.3f) * (horizontal ? H : W);

            float width = Range(rng, 1.2f, 2.8f);
            float depth = Range(rng, 0.45f, 1f) * s;

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float d = (Vector2.Dot(new Vector2(x, y), n) - offset) / width;
                    if (Mathf.Abs(d) > 3f) continue;
                    // Mexican hat: the fold itself, and the ridges beside it.
                    float profile = (1f - d * d) * Mathf.Exp(-d * d);
                    float crest = Mathf.Exp(-d * d);
                    int i = x + y * W;

                    front[i].Height = Mathf.Clamp(front[i].Height - depth * profile, -1f, 1f);
                    Raise(ref front[i].Scuff, depth * crest * 0.7f);
                    Raise(ref back[i].Scuff, depth * crest * 0.5f);
                    if (Mathf.Abs(d) < 0.8f)
                    {
                        Raise(ref front[i].Ink, depth * crest * 0.65f);
                        Raise(ref back[i].Ink, depth * crest * 0.5f);
                    }
                }
            }
        }

        /// <summary>
        /// A bite out of the outline. Centred on the border rather than inside it,
        /// so the hole opens into the edge instead of turning up as a pinprick in
        /// the middle of the frame.
        /// </summary>
        void Chip(System.Random rng, float s)
        {
            float r = Range(rng, 1.2f, 3.4f) * Mathf.Lerp(0.6f, 1f, s);
            int cx, cy;
            if (rng.NextDouble() < 0.45)
            {
                // Corners chip more than sides do.
                cx = rng.NextDouble() < 0.5 ? 0 : W - 1;
                cy = rng.NextDouble() < 0.5 ? 0 : H - 1;
            }
            else if (rng.NextDouble() < 0.5)
            {
                cx = rng.NextDouble() < 0.5 ? 0 : W - 1;
                cy = rng.Next(0, H);
            }
            else
            {
                cx = rng.Next(0, W);
                cy = rng.NextDouble() < 0.5 ? 0 : H - 1;
            }

            int ri = Mathf.CeilToInt(r) + 2;
            for (int y = cy - ri; y <= cy + ri; y++)
            {
                for (int x = cx - ri; x <= cx + ri; x++)
                {
                    if (!Inside(x, y)) continue;
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    int i = x + y * W;
                    if (d <= r)
                    {
                        front[i].Missing = 1f;
                    }
                    else if (d <= r + 1.5f)
                    {
                        // The torn lip round the hole, where the paper is crushed.
                        float f = 1f - (d - r) / 1.5f;
                        Raise(ref front[i].Scuff, f * 0.8f);
                        Raise(ref back[i].Scuff, f * 0.8f);
                        Raise(ref front[i].Ink, f * 0.7f);
                        Raise(ref back[i].Ink, f * 0.7f);
                    }
                }
            }
        }

        // -------------------------------------------------------------------
        // Restoration
        // -------------------------------------------------------------------
        /// <summary>
        /// Works a tool over the card for <paramref name="seconds"/>. uv is where
        /// the pointer sits on the face being worked, 0..1, and backFace says which
        /// face that is - scuffs and ink loss are per side, so restoring a card
        /// means turning it over and doing the other half.
        /// </summary>
        public void Rub(Vector2 uv, bool backFace, in RestorationTool tool, float seconds)
        {
            if (seconds <= 0f) return;
            EnsureMaps();

            // The names are for the UI, not for the replay. Dropping them keeps a
            // saved stroke to its rates, which is all Stamp ever reads.
            var rates = tool;
            rates.name = null;
            rates.blurb = null;

            var stroke = new Stroke { u = uv.x, v = uv.y, back = backFace, seconds = seconds, tool = rates };
            strokes.Add(stroke);
            Stamp(stroke);
        }

        void Stamp(in Stroke stroke)
        {
            var tool = stroke.tool;

            if (tool.bendRate > 0f)
            {
                float pull = tool.bendRate * stroke.seconds;
                bow = Vector2.MoveTowards(bow, Vector2.zero, pull * MaxBow);
                for (int c = 0; c < 4; c++)
                    cornerBend[c] = Mathf.MoveTowards(cornerBend[c], 0f, pull * MaxCornerBend);
                bendDirty = true;
            }

            var map = stroke.back ? back : front;

            int x0 = 0, y0 = 0, x1 = W - 1, y1 = H - 1;
            float cx = 0f, cy = 0f, rx = 1f, ry = 1f;

            if (!tool.wholeCard)
            {
                Vector2 size = view != null ? view.Size : new Vector2(0.73f, 1.13f);
                rx = Mathf.Max(tool.radius / Mathf.Max(size.x, 1e-4f) * W, 1f);
                ry = Mathf.Max(tool.radius / Mathf.Max(size.y, 1e-4f) * H, 1f);
                cx = stroke.u * (W - 1);
                cy = stroke.v * (H - 1);
                x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rx));
                x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + rx));
                y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry));
                y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + ry));
                if (x0 > x1 || y0 > y1) return;
            }

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float f = 1f;
                    if (!tool.wholeCard)
                    {
                        float dx = (x - cx) / rx;
                        float dy = (y - cy) / ry;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d >= 1f) continue;
                        f = 1f - d;
                        f = f * f * (3f - 2f * f);
                    }

                    float dt = stroke.seconds * f;
                    int i = x + y * W;
                    bool worked = false;

                    if (tool.scuffRate > 0f && map[i].Scuff > tool.scuffFloor)
                    {
                        map[i].Scuff = Mathf.Max(tool.scuffFloor, map[i].Scuff - tool.scuffRate * dt);
                        worked = true;
                    }
                    if (tool.inkRate > 0f && map[i].Ink > tool.inkFloor)
                    {
                        map[i].Ink = Mathf.Max(tool.inkFloor, map[i].Ink - tool.inkRate * dt);
                        worked = true;
                    }
                    // Dents and chips are in the paper rather than on a face, so they
                    // always come out of the front map whichever side is being worked.
                    if (tool.flattenRate > 0f && Mathf.Abs(front[i].Height) > 0.002f)
                    {
                        front[i].Height = Mathf.MoveTowards(front[i].Height, 0f, tool.flattenRate * dt);
                        frontDirty = true;
                        worked = true;
                    }
                    if (tool.fillRate > 0f && front[i].Missing > 0f)
                    {
                        front[i].Missing = Mathf.Max(0f, front[i].Missing - tool.fillRate * dt);
                        frontDirty = true;
                        worked = true;
                    }

                    if (worked)
                    {
                        Add(ref map[i].Scuff, tool.scuffCost * dt);
                        Add(ref map[i].Ink, tool.inkCost * dt);
                    }
                    else
                    {
                        // Nothing left here to put right, so this is just rubbing a
                        // card. Keep going and you are the damage.
                        Add(ref map[i].Scuff, tool.overworkScuff * dt);
                    }
                }
            }

            if (stroke.back) backDirty = true;
            else frontDirty = true;
            conditionDirty = true;
        }

        // -------------------------------------------------------------------
        // Grading
        // -------------------------------------------------------------------
        void Recompute()
        {
            conditionDirty = false;
            if (front == null)
            {
                condition = CardCondition.Pristine;
                return;
            }

            double scuff = 0, ink = 0, dent = 0, missing = 0;
            for (int i = 0; i < front.Length; i++)
            {
                scuff += front[i].Scuff + back[i].Scuff;
                ink += front[i].Ink + back[i].Ink;
                dent += Mathf.Abs(front[i].Height);
                missing += front[i].Missing;
            }

            int n = front.Length;
            float bend = (Mathf.Abs(bow.x) + Mathf.Abs(bow.y)) / (MaxBow * 2f)
                       + (Mathf.Abs(cornerBend.x) + Mathf.Abs(cornerBend.y)
                        + Mathf.Abs(cornerBend.z) + Mathf.Abs(cornerBend.w)) / (MaxCornerBend * 2f);

            // Raw means. CardCondition is what knows they are small and what a
            // ruined card actually averages - see the full-scale constants there.
            condition = new CardCondition(
                (float)(scuff / (n * 2)),
                (float)(ink / (n * 2)),
                (float)(dent / n),
                (float)(missing / n),
                Mathf.Clamp01(bend));
        }

        // -------------------------------------------------------------------
        // Upload
        // -------------------------------------------------------------------
        void EnsureMaps()
        {
            if (front != null) return;

            front = new Texel[W * H];
            back = new Texel[W * H];
            frontMap = NewMap("CardWearFront");
            backMap = NewMap("CardWearBack");

            Bind();
            frontDirty = backDirty = bendDirty = true;
        }

        void Bind()
        {
            if (view == null) view = GetComponent<CardView>();
            if (view == null) return;
            view.SetTexture(WearTexId, frontMap);
            view.SetTexture(WearBackTexId, backMap);
            view.SetFloat(WearAmountId, 1f);
        }

        Texture2D NewMap(string name)
        {
            // linear, never sRGB: this is data, and a gamma curve on it would bend
            // every rate the tools rub at. Point and clamp so wear texels land on
            // artwork texels, and so the neighbour taps that build the dent normals
            // do not wrap round to the far side of the card.
            return new Texture2D(W, H, TextureFormat.RGBA32, false, true)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
        }

        /// <summary>
        /// Pushes whatever moved onto the card. Called for you every frame, and
        /// public for the two cases where there is no frame to wait for: an editor
        /// tool that has to render the card it just aged, and anything that wants
        /// the card correct in the same frame it changed it.
        /// </summary>
        public void Flush()
        {
            if (bendDirty && view != null)
            {
                view.SetVector(BowId, new Vector4(bow.x, bow.y, 0f, 0f));
                view.SetVector(CornerBendId, cornerBend);
                bendDirty = false;
            }

            if (frontDirty) { Upload(front, frontMap); frontDirty = false; }
            if (backDirty) { Upload(back, backMap); backDirty = false; }
        }

        static void Upload(Texel[] map, Texture2D tex)
        {
            if (map == null || tex == null) return;
            NativeArray<Color32> data = tex.GetRawTextureData<Color32>();
            for (int i = 0; i < map.Length; i++)
            {
                var t = map[i];
                data[i] = new Color32(
                    ToByte(t.Scuff),
                    ToByte(t.Ink),
                    // 128 is flat, so a dent and a ridge both have somewhere to go.
                    ToByte(t.Height * 0.5f + 0.5f),
                    ToByte(t.Missing));
            }
            tex.Apply(false);
        }

        static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        // -------------------------------------------------------------------
        // Saving
        // -------------------------------------------------------------------
        /// <summary>
        /// One pass of a tool. Rates only, no names: this is replay data, and
        /// <see cref="Stamp"/> never reads anything else off the tool.
        /// </summary>
        [Serializable]
        public struct Stroke
        {
            public float u, v;
            public bool back;
            public float seconds;
            public RestorationTool tool;
        }

        /// <summary>
        /// A card's whole condition in the seed it was aged from and every stroke
        /// worked into it since. The map itself is never stored: generation and
        /// rubbing are both deterministic, so replaying them lands on the same
        /// texels, and a card that has been worked on for an hour still saves in a
        /// few kilobytes instead of the map's sixty-six.
        /// </summary>
        [Serializable]
        public class State
        {
            public int seed;
            public float severity;
            public List<Stroke> strokes = new List<Stroke>();
        }

        public State Capture() => new State
        {
            seed = seed,
            severity = severity,
            strokes = new List<Stroke>(strokes),
        };

        public void Load(State state)
        {
            if (state == null) return;
            seed = state.seed;
            severity = state.severity;
            strokes.Clear();
            if (state.strokes != null) strokes.AddRange(state.strokes);
            Rebuild();
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------
        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

        void RaiseAt(Texel[] map, int x, int y, float scuff, float ink)
        {
            if (!Inside(x, y)) return;
            int i = x + y * W;
            Raise(ref map[i].Scuff, scuff);
            Raise(ref map[i].Ink, ink);
        }

        /// <summary>
        /// Stamping damage takes the worse of the two rather than piling up, so two
        /// scratches crossing do not read as a hole.
        /// </summary>
        static void Raise(ref float value, float amount)
        {
            if (amount <= 0f) return;
            value = Mathf.Clamp01(Mathf.Max(value, amount));
        }

        /// <summary>
        /// What a tool leaves behind does pile up, one frame's worth at a time -
        /// that is the whole reason overworking a spot costs anything.
        /// </summary>
        static void Add(ref float value, float amount)
        {
            if (amount <= 0f) return;
            value = Mathf.Clamp01(value + amount);
        }

        static float Range(System.Random rng, float min, float max)
            => min + (float)rng.NextDouble() * (max - min);

        /// <summary>Deterministic value noise in 0..1, so a card grains the same way twice.</summary>
        static float Noise(int x, int y, int seed)
        {
            unchecked
            {
                int h = seed * 73856093 ^ x * 19349663 ^ y * 83492791;
                h ^= h >> 13;
                h *= 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

#if UNITY_EDITOR
        public void EditorBind(Vector2Int mapResolution) => resolution = mapResolution;
#endif
    }
}
