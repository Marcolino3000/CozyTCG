# Rebuilding the card effects in a new Unity project

A build guide, from `Unity Hub > New Project` to a rotating holo card, a tearing booster pack and an
atlas quad. Everything that is **not** shader math is given complete — project settings, import
rules, mesh generators, the C# that drives the material. Every shader function is given as a
**skeleton with a spec**, and you fill in the body.

The answers are all in **Appendix A**, at the very bottom, numbered by step. They are deliberately
*far away* rather than inline: a collapsed block still shows its contents in a plain text editor, and
a spoiler you can scroll past by accident is not a spoiler you control. Write the body, then go and
check.

**Target:** Unity 6000.3 or newer, URP 17. Older URP works with two changes, noted where they bite.

## What you end up with

| | |
|---|---|
| `CardHolo.shader` | Pixel-snapped art, 5 foil layers, wear map, vertex bending, 10 debug views |
| `CardPack.shader` | A wrapper that tears open along a ragged seam and peels back |
| `SpriteSheet.shader` | One atlas cell on a quad, for books and icons |
| `CardWear.cs` | The damage generator that fills the wear map |
| `CardView.cs` | Rotation, hover, and the property block that drives per-card values |
| Mesh generators | The 16×24 card grid and the two torn pack halves |

## How a step is laid out

**Given** steps are complete code. Paste them, read them, move on — retyping a mesh generator teaches
nothing.

**Build** steps look like this:

```hlsl
// Spec: what it must return, in what units, and what it must not do.
float SomeFunction(float2 uv)
{
    // TODO
}
```

followed by **Constraints** (non-negotiable), **Verify** (how to prove it), and **Trap** where there
is a specific way to get it wrong. Then `→ Appendix A, Step N`.

---

# Part 0 — The project

## 0.1 Create it — Given

New project from the **Universal 3D** template. If you start from Built-in instead, you will spend
an hour on shaders that compile to magenta.

Then, before anything else:

| Setting | Where | Value |
|---|---|---|
| Color Space | `Project Settings > Player > Other Settings` | **Linear** |
| Active Input Handling | `Project Settings > Player > Other Settings` | **Both** |
| Auto Graphics API | `Project Settings > Player` | leave on |

**Linear is not optional.** Every colour constant below — `_StockColor`, the chrome sky and ground,
the foil tints — was picked in linear space. In gamma space the foil comes out washed and the card
stock reads pink.

Check the URP asset exists: `Assets/Settings/` should hold a `UniversalRenderPipelineAsset`, and
`Project Settings > Graphics > Scriptable Render Pipeline Settings` must point at it. If that field
is empty, nothing below will render.

## 0.2 Folders — Given

```
Assets/
  Editor/            editor-only C#, builders and importers
  Materials/         generated
  Meshes/            generated
  Prefabs/           generated
  Resources/Cards/   your pixel art, one folder per deck
  Scenes/
  Scripts/Runtime/   CardView, CardWear
  Shaders/
```

`Assets/Editor/` is a magic folder name — code in it compiles into the editor assembly and is
stripped from builds. `Assets/Resources/` is magic too: everything in it ships whether or not it is
referenced. Both matter later.

No `.asmdef` files. Everything lands in `Assembly-CSharp` and `Assembly-CSharp-Editor`, which is what
lets a runtime script be poked from an editor builder without ceremony.

## 0.3 The art — Given

You need, per card, a **front** and a **back** PNG at the same pixel size. This guide uses **73×113**
throughout; substitute your own size everywhere `_CardPixels` appears and keep the world size in
step (at 100 pixels per unit, 73×113 px is 0.73 × 1.13 units).

Drop them in `Assets/Resources/Cards/<deck>/`. Then `Assets/Editor/PixelArtImporter.cs`:

```csharp
using UnityEditor;
using UnityEngine;

namespace CardFx.EditorTools
{
    /// <summary>
    /// Import defaults for pixel art drawn on rotating 3D quads.
    ///
    /// Bilinear + mips rather than point filtering, on purpose. The shader snaps
    /// sampling to texel centres itself (Step 3), which keeps pixels crisp at any
    /// angle, while the hardware still filters minification so a tilted card does
    /// not crawl. Point filtering would fight the shader and lose.
    /// </summary>
    public class PixelArtImporter : AssetPostprocessor
    {
        public const string Root = "Assets/Resources/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = (TextureImporter)assetImporter;
            // Only stamp brand new assets - never fight a manual change afterwards.
            if (!importer.importSettingsMissing) return;
            Apply(importer);
        }

        public static void Apply(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.mipmapEnabled = true;
            importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = 0.5f;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;

            // Never let Unity downscale pixel art: a sheet taller than the current
            // max size gets resampled at a non-integer ratio, which destroys the grid.
            importer.GetSourceTextureWidthAndHeight(out int w, out int h);
            int longest = Mathf.Max(w, h);
            if (longest > importer.maxTextureSize)
                importer.maxTextureSize = longest <= 4096 ? 4096 : 8192;
        }

        [MenuItem("Tools/Card FX/Apply Pixel Art Import Settings")]
        public static void ApplyToAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources" });
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                    Apply(importer);
                    importer.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            Debug.Log($"[Card FX] Import settings applied to {guids.Length} texture(s)");
        }
    }
}
```

Four of those lines are load-bearing and worth understanding now, because each is a bug you would
otherwise hit three parts from here:

- `mipMapsPreserveCoverage` + `alphaTestReferenceValue = 0.5` — without it, a cutout card's border
  **dissolves** as it shrinks, because averaging alpha down the mip chain drags it under the cutoff.
- `wrapMode = Clamp` — Step 22 samples neighbouring texels for the dent normal. On Repeat, a tap at
  the card's border reads the far edge and every border pixel grows a dent it does not have.
- `sRGBTexture = true` — correct for *artwork*. It is **wrong** for the wear map, which is data. That
  map is created in code (Step 18) and never goes through this importer.
- `textureCompression = Uncompressed` — DXT on a 73×113 pixel card is visible as block artefacts on
  every hard colour edge.

## 0.4 The card mesh — Given

A quad will not do. Bending (Part 4) happens in the vertex shader, and a vertex shader can only move
vertices that **exist**. Four of them describe nothing.

`Assets/Editor/CardMeshBuilder.cs`:

```csharp
using UnityEditor;
using UnityEngine;

namespace CardFx.EditorTools
{
    public static class CardMeshBuilder
    {
        const string MeshPath = "Assets/Meshes/CardQuad.asset";

        public static readonly Vector2 CardPixels = new Vector2(73f, 113f);
        public static readonly Vector2 CardSize = new Vector2(0.73f, 1.13f);

        /// <summary>
        /// Grid the card is cut into, so the vertex shader has something to bend.
        /// Deliberately far below the 73x113 pixel grid: a fold is low frequency and
        /// only has to read in the silhouette, while the sharp side of the damage -
        /// the dent that catches the foil - is a per pixel normal in the fragment
        /// stage and needs no geometry at all.
        /// </summary>
        static readonly Vector2Int MeshCells = new Vector2Int(16, 24);

        /// <summary>
        /// Depth the bounds reserve for the bend, in world units. Must clear
        /// MaxBow + MaxCornerBend + the material's _DentDisplace, or a bowed card is
        /// culled against a slab it no longer fits in and pops out of view at the
        /// edge of the screen. That bug looks like a camera problem. It is not.
        /// </summary>
        const float MeshBendHeadroom = 0.14f;

        [MenuItem("Tools/Card FX/Build Card Mesh")]
        public static Mesh Build()
        {
            float w = CardSize.x * 0.5f;
            float h = CardSize.y * 0.5f;
            int cols = MeshCells.x, rows = MeshCells.y;
            int stride = cols + 1;

            var vertices = new Vector3[stride * (rows + 1)];
            var uvs = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var tangents = new Vector4[vertices.Length];
            // w = -1 so the bitangent lines up with +V of the UVs.
            var tangent = new Vector4(1f, 0f, 0f, -1f);

            for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= cols; x++)
            {
                int i = x + y * stride;
                var uv = new Vector2((float)x / cols, (float)y / rows);
                uvs[i] = uv;
                vertices[i] = new Vector3(Mathf.Lerp(-w, w, uv.x), Mathf.Lerp(-h, h, uv.y), 0f);
                // Face normal is -Z, same as Unity's built-in Quad: the card's face
                // points at a camera sitting on -Z with an unrotated transform, which
                // keeps the artwork reading the right way round on screen. The card's
                // visible side is therefore -transform.forward.
                normals[i] = Vector3.back;
                tangents[i] = tangent;
            }

            var triangles = new int[cols * rows * 6];
            int t = 0;
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                int bl = x + y * stride, br = bl + 1, tl = bl + stride, tr = tl + 1;
                // Wound so the geometric normal comes out along -Z.
                triangles[t++] = bl; triangles[t++] = tl; triangles[t++] = br;
                triangles[t++] = tl; triangles[t++] = tr; triangles[t++] = br;
            }

            var mesh = new Mesh { name = "CardQuad" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.triangles = triangles;
            // Bounds cover where the vertex shader will PUT the card, not where the
            // mesh sits at rest.
            mesh.bounds = new Bounds(Vector3.zero,
                new Vector3(CardSize.x, CardSize.y, MeshBendHeadroom * 2f));

            System.IO.Directory.CreateDirectory("Assets/Meshes");
            if (AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath) != null)
                AssetDatabase.DeleteAsset(MeshPath);
            AssetDatabase.CreateAsset(mesh, MeshPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        }
    }
}
```

17 × 25 = **425 vertices**, 16 × 24 × 2 = **768 triangles**. Worth knowing, because that is how many
times your vertex shader runs per card and it puts the cost of the two stages in perspective: the
fragment shader runs that many times in a few hundred pixels of card.

**The orientation convention, stated once because everything depends on it:**

> The card faces **−Z**. Its tangent frame at rest is **T = +X, B = +Y, N = −Z**. The visible face is
> `-transform.forward`. Get this backwards and every piece of artwork is mirrored.

## 0.5 CardView — Given

The component that rotates the card and owns the property block.

```csharp
using UnityEngine;

namespace CardFx
{
    /// <summary>
    /// Drives one card: hover tilt, free spin while dragging, flipping.
    ///
    /// Root/Visual split, and it is an invariant rather than a style choice: the
    /// root transform NEVER rotates, a Visual child does all the tilting. Pointer
    /// maths is done against the root, so tilt cannot feed back into the pointer
    /// position. Break this and you get jitter that looks like a smoothing bug and
    /// is not one.
    /// </summary>
    [DisallowMultipleComponent]
    public class CardView : MonoBehaviour
    {
        static readonly int FrontTexId = Shader.PropertyToID("_FrontTex");
        static readonly int BackTexId = Shader.PropertyToID("_BackTex");
        static readonly int SparkleSeedId = Shader.PropertyToID("_SparkleSeed");

        [SerializeField] Transform visual;
        [SerializeField] MeshRenderer meshRenderer;
        [SerializeField] Vector2 size = new Vector2(0.73f, 1.13f);

        [Header("Rotation")]
        [SerializeField] float dragSensitivity = 0.45f;
        [SerializeField] float maxPitch = 85f;
        [SerializeField] float idleSway = 2.5f;
        [SerializeField] float idleSpeed = 0.55f;
        [SerializeField] bool idleMotion = true;

        float yaw, pitch, restYaw, idlePhase;
        bool dragging, showingBack;
        Vector2 dragAccum;
        MaterialPropertyBlock block;

        public MeshRenderer Renderer => meshRenderer;
        public Vector2 Size => size;
        public bool ShowingBack => showingBack;

        void Awake()
        {
            if (visual == null) visual = transform.childCount > 0 ? transform.GetChild(0) : transform;
            if (meshRenderer == null) meshRenderer = GetComponentInChildren<MeshRenderer>();
            idlePhase = Random.value * 100f;
            // Per-card variation rides on the property block, not on a material copy.
            SetSparkleSeed(Random.Range(0f, 100f));
        }

        void Update()
        {
            if (!dragging && idleMotion)
            {
                idlePhase += Time.deltaTime * idleSpeed;
                yaw = restYaw + Mathf.Sin(idlePhase) * idleSway;
                pitch = Mathf.Sin(idlePhase * 0.7f) * idleSway * 0.6f;
            }
            visual.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        public void BeginDrag() { dragging = true; dragAccum = Vector2.zero; }
        public void EndDrag() { dragging = false; restYaw = Mathf.Repeat(yaw, 360f); }

        public void Drag(Vector2 screenDelta)
        {
            if (!dragging) return;
            dragAccum += screenDelta * dragSensitivity;
            yaw = restYaw + dragAccum.x;
            pitch = Mathf.Clamp(-dragAccum.y, -maxPitch, maxPitch);
            showingBack = Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)) > 90f;
        }

        public void Flip()
        {
            showingBack = !showingBack;
            restYaw = showingBack ? 180f : 0f;
            yaw = restYaw;
        }

        // -------------------------------------------------------------------
        // Material overrides
        // -------------------------------------------------------------------
        // ALWAYS through a MaterialPropertyBlock, never by touching .material -
        // that clones the material per renderer and breaks SRP batching. The
        // rarity tier is a shared material; everything per-card rides on the block.
        //
        // A block CANNOT switch a shader KEYWORD. That is why _PIXELAA and
        // _HOLOPIXELATE off needs its own material rather than a block value.

        public void SetFaces(Texture front, Texture back)
        {
            if (meshRenderer == null) return;
            block ??= new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(block);
            if (front != null) block.SetTexture(FrontTexId, front);
            if (back != null) block.SetTexture(BackTexId, back);
            meshRenderer.SetPropertyBlock(block);
        }

        public void SetSparkleSeed(float seed) => SetFloat(SparkleSeedId, seed);

        public void SetFloat(int id, float value)
        {
            if (meshRenderer == null) return;
            block ??= new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(block);
            block.SetFloat(id, value);
            meshRenderer.SetPropertyBlock(block);
        }

        public void SetVector(int id, Vector4 value)
        {
            if (meshRenderer == null) return;
            block ??= new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(block);
            block.SetVector(id, value);
            meshRenderer.SetPropertyBlock(block);
        }

        public void SetTexture(int id, Texture value)
        {
            if (meshRenderer == null || value == null) return;
            block ??= new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(block);
            block.SetTexture(id, value);
            meshRenderer.SetPropertyBlock(block);
        }

        public float GetFloat(int id, float fallback)
        {
            if (meshRenderer == null) return fallback;
            block ??= new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(block);
            return block.HasFloat(id) ? block.GetFloat(id) : fallback;
        }
    }
}
```

A minimal driver so you can turn the card with the mouse. `Assets/Scripts/Runtime/CardSpinner.cs`:

```csharp
using UnityEngine;

namespace CardFx
{
    /// Drag anywhere to spin the card. Good enough for a test bench.
    public class CardSpinner : MonoBehaviour
    {
        [SerializeField] CardView card;
        Vector3 last;

        void Reset() => card = GetComponent<CardView>();

        void Update()
        {
            if (card == null) return;
            if (Input.GetMouseButtonDown(0)) { card.BeginDrag(); last = Input.mousePosition; }
            else if (Input.GetMouseButton(0))
            {
                Vector3 now = Input.mousePosition;
                card.Drag(now - last);
                last = now;
            }
            else if (Input.GetMouseButtonUp(0)) card.EndDrag();

            if (Input.GetKeyDown(KeyCode.F)) card.Flip();
        }
    }
}
```

## 0.6 The scene — Given

1. New scene, `Assets/Scenes/CardBench.unity`.
2. Camera at `(0, 0, -2)`, rotation zero, **Projection: Perspective**, background a mid grey. Keep it
   perspective — the foil is view-dependent and an orthographic camera gives every pixel the same
   view direction, which flattens the whole effect to a constant.
3. Empty GameObject `Card` at the origin. Add `CardView` and `CardSpinner`.
4. Child of it named `Visual`. Add `MeshFilter` (→ `CardQuad.asset`) and `MeshRenderer`.
5. On `CardView`, drag `Visual` into **Visual** and its `MeshRenderer` into **Mesh Renderer**.
6. `Tools > Card FX > Build Card Mesh` if you have not already.

Leave the material empty for now — that is Step 1.

## 0.7 Checkpoint — before writing a single line of HLSL

Do not skip this. Every one of these failures is silent, and each is far harder to find once there
are 800 lines of shader on top of it.

- [ ] `Project Settings > Graphics` points at a URP asset
- [ ] Color space is **Linear**
- [ ] `CardQuad.asset` exists and its inspector preview shows a grid, not a single quad
- [ ] The card art imported with **Bilinear** filtering and mips on
- [ ] The scene renders (a magenta or grey quad is fine — a *missing* quad is not)

---

# Part 1 — The card shader

Two files. The `.shader` holds properties, tags, pass state and the two entry points; the `.hlsl`
holds the CBUFFER, the samplers and all the math. That split is not cosmetic — the include is shared
by the forward pass, the depth pass, and later by a whole separate overlay shader.

## Step 1 — Skeleton and CBUFFER — Given

`Assets/Shaders/CardHoloInput.hlsl`:

```hlsl
#ifndef CARD_HOLO_INPUT_INCLUDED
#define CARD_HOLO_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Everything the material owns lives in ONE UnityPerMaterial block, or the SRP
// Batcher silently stops batching. It does not warn; you find out in the profiler.
CBUFFER_START(UnityPerMaterial)
    float4 _FrontTex_ST;
    float4 _BackTex_ST;
    float4 _Tint;
    float4 _CardPixels;
    float  _Cutoff;
    float  _PixelAA;
CBUFFER_END

TEXTURE2D(_FrontTex);   SAMPLER(sampler_FrontTex);
TEXTURE2D(_BackTex);

#endif
```

`Assets/Shaders/CardHolo.shader`:

```hlsl
Shader "Card FX/Card Holo"
{
    Properties
    {
        [MainTexture] _FrontTex("Front Face", 2D) = "white" {}
        _BackTex("Back Face", 2D) = "white" {}
        [MainColor] _Tint("Tint", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        _CardPixels("Card Size In Pixels", Vector) = (73,113,0,0)
        [Toggle(_PIXELAA)] _PixelAA("Anti Aliased Pixels", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 200

        Pass
        {
            Name "CardForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off      // one quad, two faces
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            // 3.5, not 3.0: from Part 4 the VERTEX stage samples a texture, and
            // vertex texture fetch is only guaranteed from this tier up. Set it now
            // so you are not chasing a platform-specific failure later.
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ _PIXELAA

            #include "CardHoloInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 tangentWS   : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = nrm.normalWS;
                output.tangentWS = nrm.tangentWS;
                output.bitangentWS = nrm.bitangentWS;
                output.uv = TRANSFORM_TEX(input.uv, _FrontTex);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 base = SAMPLE_TEXTURE2D(_FrontTex, sampler_FrontTex, input.uv);
                clip(base.a - _Cutoff);
                return half4(base.rgb * _Tint.rgb, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
```

Make `Assets/Materials/Card_Lab.mat` on this shader, assign your front art, drop it on the renderer.
You should see the card. **Open the Frame Debugger** (`Window > Analysis > Frame Debugger`) and
confirm it says SRP Batcher **compatible**. Fix that now if not — the CBUFFER only gets longer.

## Step 2 — Both faces — Build

The quad has `Cull Off`, so the back face draws too. Right now it draws the front art, mirrored.

```hlsl
// In Frag, before sampling. V and N are world-space, both normalized.
//
// Spec:
//   facing  = +1 when looking at the front of the card, -1 at the back.
//   Flip N and T by it, so the tangent frame always points at the viewer.
//   Mirror uv.x on the back face.
//   Pick front or back art by it.
//
// TODO
```

**Constraints.**

- The face test must be **geometric** — from `N` against `V`. Not `SV_IsFrontFace`, not winding
  order. A bent card (Part 4) has faces pointing both ways within one triangle strip, and winding
  is a property of the triangle, not of the pixel.
- The frame flip must include `T`, or the foil on the back runs backwards. `B` is left alone —
  work out why (hint: what is `cross(N, T)` after you negate both?).

**Verify.** Put visibly different art on `_FrontTex` and `_BackTex`. Spin. The swap must land exactly
at edge-on. Put readable text on the front: it must never mirror.

**Trap.** If you flip `uv.x` but not `T`, the artwork is right and the foil is wrong — and you will
not notice until Step 9, by which point you will blame the rainbow.

→ *Appendix A, Step 2*

## Step 3 — Pixel snapping — Build

The one that makes it pixel art. Bilinear filtering blurs the texels; point filtering makes edges
crawl and shimmer as the card turns. You want neither.

```hlsl
// In CardHoloInput.hlsl.
//
// Spec: snap uv to texel centres, but keep a ramp exactly ONE SCREEN PIXEL wide
// across each texel seam. With bilinear filtering that reads as crisp pixel art
// that does not crawl.
//
// texSize is the card's pixel size, e.g. (73, 113).
float2 CardPixelUV(float2 uv, float2 texSize)
{
#ifdef _PIXELAA
    // TODO
#else
    return uv;
#endif
}
```

**The one idea you need.** With `p = uv * texSize`, `fwidth(p)` is how much `p` changes per screen
pixel — i.e. **texels per screen pixel**. So dividing a distance measured in texels by `fwidth(p)`
converts it to a distance in *screen pixels*. Everything else follows: find the nearest texel centre,
measure the offset from it, convert that offset to screen pixels, and clamp it to ±0.5.

**Constraints.**

- Keep bilinear filtering on in the importer. Solving this with point filtering is not solving it.
- `fwidth` can be 0 (a perfectly axis-aligned, perfectly still card) — clamp the divisor away from
  zero or you get NaN, which shows up as black or white speckle.

**Verify.** Toggle `_PIXELAA` and compare. Rotate continuously for ten seconds watching a diagonal
edge: nothing may crawl. Zoom out to ~30 px tall: nothing may sparkle.

**Trap — the big one.** A `[Toggle(_PIXELAA)]` property sets a **float**. The `#pragma
shader_feature` branches on a **keyword**. Setting one without the other does nothing at all, with no
error. From C# they must be set together:

```csharp
static void SetToggle(Material mat, string property, string keyword, bool on)
{
    mat.SetFloat(property, on ? 1f : 0f);
    if (on) mat.EnableKeyword(keyword);
    else mat.DisableKeyword(keyword);
}
```

And a `MaterialPropertyBlock` **cannot** set a keyword at all. If you want the same card both crisp
and smooth in one scene, that is two materials, not two property blocks.

→ *Appendix A, Step 3*

## Step 4 — The mip trap — Build

You have just written a bug you cannot see yet.

Your snapped UV is deliberately **discontinuous** at every texel seam. The hardware picks a mip level
from the screen-space derivatives of the UV you sample with — so it now sees a UV that jumps, decides
the card is being minified enormously, and grabs the coarsest mip. Minified cards turn to mush.

```hlsl
// In Frag. Sample at the SNAPPED uv, but with the derivatives of the RAW uv.
//
// TODO: replace the plain SAMPLE_TEXTURE2D calls
```

**Verify.** Zoom out until the card is ~40 px tall and pan the camera. Before the fix: the card is a
flat blur. After: it reads as a small card. Step 28's density view (view 9) makes it obvious.

→ *Appendix A, Step 4*

## Step 5 — The depth pass — Build

```hlsl
// A second Pass in the SubShader, after CardForward.
//
// Name "DepthOnly", Tags { "LightMode" = "DepthOnly" }
// ZWrite On, ColorMask R, Cull Off
// Same pragmas, INCLUDING the _PIXELAA shader_feature.
//
// TODO: vertex writes only positionCS and uv; fragment clips on exactly the same
// condition as the forward pass and returns 0.
```

**Constraints.**

- Every keyword the forward pass declares must be declared here too. A `_PIXELAA` mismatch means the
  depth silhouette is snapped differently from the colour, and the card grows a fringe.
- The clip condition must be **identical**. In Part 3 you will add a term to it — the discipline is
  to change both passes in the same edit, every time.

**Why bother.** URP's depth prepass and depth texture feed soft particles, camera-depth effects and
some SSAO configurations. A cutout card with no depth pass punches a hole through all of them.

→ *Appendix A, Step 5*

## Step 6 — Tilt — Build

The single most important function in this document. Every one of the five foil layers is driven by
it. If it is wrong, five effects are wrong at once and you will debug the wrong five.

```hlsl
// In Frag, after the facing flip. V, N, T, B are world-space and normalized.
//
// Spec: a float2 that is
//   ZERO   when the card faces the camera square-on,
//   grows  as the card turns away,
//   BOUNDED - it must not explode at grazing angles,
//   scaled by _TiltGain.
//
// It is the view direction expressed in tangent space, with the component along
// the normal divided out: the same construction as a parallax offset.
//
// TODO: float3 vT = ...
//       float  ndv = ...
//       float2 tilt = ...
```

**Constraints.**

- Write it so that substituting a **per-pixel normal** for the flat one changes nothing
  structurally. In Step 22 you will do exactly that, and if you hardcode the flat case now you will
  rewrite this step later. Concretely: compute `ndv` as a dot product against a normal vector, not as
  `vT.z`.
- Two separate mechanisms keep it bounded, and you want both: a **floor under the divisor** (about
  `0.15`) stops the division blowing up, and a **soft saturation** of the result
  (`t /= 1 + k*length(t)`) keeps the foil from stretching absurdly at grazing angles. Try removing
  each and watch what the other fails to prevent.

**Verify.** Return `float4(tilt * 0.5 + 0.5, 0.5, 1)`. Flat grey at the card's centre when it faces
you; it moves as you turn. Turn to nearly edge-on: the colour must go somewhere and **stay** there,
not blow out to white.

→ *Appendix A, Step 6*

## Step 7 — Property block — Given

Add the rest of the properties now, so you are not editing the CBUFFER every step. Paste into the
`Properties` block:

```hlsl
[Header(Foil Mask)][Space(4)]
[Toggle(_MASKTEX)] _UseMaskTex("Use Mask Texture", Float) = 0
_MaskTex("Foil Mask (R)", 2D) = "white" {}
_MaskFromLuma("Mask From Luminance", Range(0,1)) = 0.45
_MaskContrast("Luminance Contrast", Range(0.1,8)) = 2
_MaskBias("Luminance Bias", Range(-1,1)) = 0.1

[Header(Foil)][Space(4)]
_FoilIntensity("Foil Intensity", Range(0,3)) = 1
_FoilBlend("Additive to Screen", Range(0,1)) = 0.65
_TiltGain("Tilt Gain", Range(0,4)) = 1
[Toggle(_HOLOPIXELATE)] _HoloPixelate("Pixelate Foil", Float) = 1
_ColorSteps("Color Quantize Steps (0 = off)", Range(0,32)) = 0

[Header(Rainbow Diffraction)][Space(4)]
_RainbowStrength("Strength", Range(0,3)) = 0.7
_RainbowScale("Band Scale", Range(0,20)) = 3
_RainbowAngle("Band Angle", Range(0,6.2832)) = 1.1
_RainbowTilt("Tilt Response", Range(0,6)) = 1.2
_RainbowDepth("Parallax Depth", Range(0,0.5)) = 0.06
_RainbowSteps("Hue Steps (0 = smooth)", Range(0,32)) = 8
_RainbowDrift("Idle Drift", Range(0,1)) = 0.03
_RainbowSat("Saturation", Range(0,1)) = 1

[Header(Sparkle)][Space(4)]
_SparkleStrength("Strength", Range(0,4)) = 0
_SparkleColor("Color", Color) = (1,1,1,1)
_SparkleDensity("Density", Range(2,80)) = 26
_SparkleSize("Size", Range(0.01,1)) = 0.35
_SparkleSpread("Angle Spread", Range(0.1,4)) = 1.2
_SparkleDepth("Parallax Depth", Range(0,0.5)) = 0.03
_SparkleSeed("Seed", Range(0,100)) = 3

[Header(Sweep Highlight)][Space(4)]
_SweepStrength("Strength", Range(0,3)) = 0.5
_SweepColor("Color", Color) = (1,1,1,1)
_SweepWidth("Width", Range(0.02,2)) = 0.35
_SweepAngle("Angle", Range(0,6.2832)) = 1.1
_SweepTravel("Travel", Range(0,3)) = 0.9
_SweepOffset("Rest Offset", Range(-1,1)) = -0.4

[Header(Chrome)][Space(4)]
_ChromeStrength("Strength", Range(0,3)) = 0
_ChromeSky("Sky Color", Color) = (0.55,0.75,1,1)
_ChromeGround("Ground Color", Color) = (0.12,0.09,0.18,1)
_ChromeSun("Sun Color", Color) = (1,0.95,0.85,1)
_ChromeSharp("Sun Sharpness", Range(1,256)) = 48
_ChromeSunDir("Sun Direction", Vector) = (0.4,0.8,-0.45,0)

[Header(Edge)][Space(4)]
_FresnelStrength("Strength", Range(0,3)) = 0.3
_FresnelPower("Power", Range(0.5,16)) = 4
_FresnelColor("Color", Color) = (0.8,0.9,1,1)
```

And into the CBUFFER, **in this order** (order matters for batching consistency across shaders that
share the buffer layout):

```hlsl
    float4 _MaskTex_ST;
    float  _UseMaskTex;
    float  _MaskFromLuma;
    float  _MaskContrast;
    float  _MaskBias;

    float  _FoilIntensity;
    float  _FoilBlend;
    float  _TiltGain;
    float  _HoloPixelate;
    float  _ColorSteps;

    float  _RainbowStrength;
    float  _RainbowScale;
    float  _RainbowAngle;
    float  _RainbowTilt;
    float  _RainbowDepth;
    float  _RainbowSteps;
    float  _RainbowDrift;
    float  _RainbowSat;

    float4 _SparkleColor;
    float  _SparkleStrength;
    float  _SparkleDensity;
    float  _SparkleSize;
    float  _SparkleSpread;
    float  _SparkleDepth;
    float  _SparkleSeed;

    float4 _SweepColor;
    float  _SweepStrength;
    float  _SweepWidth;
    float  _SweepAngle;
    float  _SweepTravel;
    float  _SweepOffset;

    float4 _ChromeSky;
    float4 _ChromeGround;
    float4 _ChromeSun;
    float4 _ChromeSunDir;
    float  _ChromeStrength;
    float  _ChromeSharp;

    float4 _FresnelColor;
    float  _FresnelStrength;
    float  _FresnelPower;
```

Plus `TEXTURE2D(_MaskTex);` next to the others, and `#pragma shader_feature_local_fragment _
_HOLOPIXELATE` and `_MASKTEX` in the forward pass.

---

# Part 2 — The foil

Five layers, all driven off `tilt`. Build them in this order and after each one **set the previous
strengths to zero and look at the new layer alone**. A foil built all at once is a foil you cannot
tune, because you cannot tell which knob did what.

All of Part 2 lives in one function in `CardHoloInput.hlsl`:

```hlsl
float3 CardFoil(float2 uv, float2 tilt, float ndv, float3 reflectDir, float3 baseColor)
{
    float2 hUV = uv;
#ifdef _HOLOPIXELATE
    // Step 15 fills this in.
#endif

    float3 foil = 0.0;
    // Steps 8-14 accumulate here.
    return max(foil, 0.0);
}
```

Call it from `Frag` and add the result to the base colour for now; Step 15 does the real compositing.

## Step 8 — A spectrum — Build

```hlsl
// Spec: t in 0..1 -> a colour that runs through the spectrum and WRAPS, so
// t = 0 and t = 1 give the same colour. Cheap: no branches, no texture.
float3 CardSpectrum(float t)
{
    // TODO
}
```

**The idea.** Three cosines of the same frequency, offset in phase by roughly a third of a cycle
each, remapped from −1..1 into 0..1. R, G and B then peak at three different points around the cycle,
which is what a spectrum is.

**Verify.** Output `CardSpectrum(uv.x)` across the card. You want a smooth hue sweep with no dark
band and no seam at the wrap.

→ *Appendix A, Step 8*

## Step 9 — Layer 1: rainbow diffraction — Build

```hlsl
// Spec:
//   - Bands running across the card at _RainbowAngle.
//   - The band phase is driven by THREE things: position across the card
//     (_RainbowScale), tilt (_RainbowTilt), and time (_RainbowDrift).
//   - On top of that, the sampling POSITION is offset by tilt * _RainbowDepth,
//     so the bands sit at a depth under the surface rather than painted on it.
//   - Hue quantised to _RainbowSteps when it is >= 1.
//   - _RainbowSat lerps between greyscale and full saturation.
//
// TODO
```

**Work out.** Of the three phase terms, only one makes the effect view-dependent. Which — and what do
the other two contribute that it cannot? (Position gives the bands *shape*; time keeps a card alive
while it sits still.)

**Then the interesting one:** `_RainbowDepth` shifts the sampled position by tilt, on top of the
phase shift. Why is that different from just adding more `_RainbowTilt`? Look at a real holographic
card edge-on: the pattern appears to sit *below* the surface, and it slides against the print as you
move. Phase shifting alone cannot produce that; parallax can.

**Quantising.** `floor(hue * steps) / steps`, not `round`. `round` gives you a half-width step at
each end of the range and the banding reads as uneven.

**Verify.** Everything else at 0, `_RainbowSteps = 8`. Turn the card: discrete bands march across it.
Set `_RainbowTilt = 0`: bands must stop reacting to rotation but stay on the card. Raise
`_RainbowDrift`: they must move on their own with the card still.

→ *Appendix A, Step 9*

## Step 10 — Layer 2: sweep — Build

```hlsl
// Spec: a soft specular bar that slides across the face as the card tilts.
//   - Direction from _SweepAngle.
//   - Width from _SweepWidth, soft-edged (gaussian-ish, not a hard band).
//   - Its centre = _SweepOffset + (tilt along the bar direction) * _SweepTravel.
// Returns a scalar 0..1.
//
// TODO: float sweep = ...
```

**Why the rest offset.** Set `_SweepOffset = 0` and turn the card through its whole range. The bar
starts in the middle and wobbles. Parking it off one edge at rest is what makes it *travel across*
the card as you turn, which is the entire effect.

**Verify.** `_SweepStrength` up, all else 0. Turning slowly, the bar must enter one edge and leave
the other.

→ *Appendix A, Step 10*

## Step 11 — Layer 3: sparkle — Build

The hardest one here. Two functions.

```hlsl
// Spec: stable pseudo-random float2 in 0..1 from a float2. No visible structure -
// no diagonal banding, no repeats, no correlation between the two outputs.
float2 CardHash22(float2 p)
{
    // TODO
}

// Spec: glitter flakes that POP IN AND OUT as the card turns, rather than a
// static sparkle pattern that slides around.
//   - One flake per cell of a uv grid at `density`.
//   - Each flake at a hashed position within its cell, radius `size`.
//   - Each flake has its own random PREFERRED TILT, spread by `spread`. It only
//     fires when the current tilt is near its preferred one.
//   - `seed` offsets the whole field, so two cards do not sparkle identically.
float CardSparkles(float2 uv, float2 tilt, float density, float size,
                   float spread, float seed)
{
    // TODO
}
```

**Test your hash before you build on it.** Output `float3(CardHash22(floor(uv*40)), 0)` full-screen.
You are looking for television static. If you see diagonal stripes or a repeating block, the hash is
broken and every symptom in this step will lie to you about why. Most one-line hashes found online
fail this at some scale.

**The 3×3 neighbourhood.** You must check the 8 neighbouring cells as well as your own. A flake near
a cell boundary has a radius that reaches into the next cell — if you only test your own cell, every
flake is clipped to a square and you get a visible grid.

**The per-flake preferred angle is the whole effect.** Without it you have a sparkle *texture* that
slides with tilt. With it, each flake has an angle at which it lights up, so turning the card makes
individual flakes wink on and off at different moments — which is what real glitter does.

**Accumulate with `max`, not `+`.** Two overlapping flakes summing to 2.0 give you a blown-out white
blob; `max` keeps every flake the same brightness.

**Verify.** Card still: flakes steady, not fizzing. Turning slowly: individual flakes wink
independently — if they all brighten together, the preferred angle is not being used.
`_SparkleDensity = 80`: no moiré, no visible grid.

**Performance note.** Nine cells × two hashes is the most expensive thing in this shader. `[unroll]`
both loops — an unrolled 3×3 with no dynamic indexing is much faster than the loop, and on some
targets the loop will not compile without it.

→ *Appendix A, Step 11*

## Step 12 — Layer 4: chrome — Build

```hlsl
// Spec: a mirror finish, from an analytic environment. No cubemap, no probe.
//   - Sky above, ground below, blended by the reflection direction.
//   - A sun: a tight highlight from _ChromeSunDir, sharpness _ChromeSharp.
//   - DAMPED when the card faces you and opening up as it turns away, so the
//     mirror does not flatly wash out the artwork.
//
// reflectDir is passed in from Frag - you compute it there from V and the
// surface normal.
//
// TODO
```

**Where the reflection is computed matters.** It has to be in **world space**, or the sky stops being
up when the card turns. In `Frag`: build the world-space normal from the tangent frame (in Step 22
this becomes the *dented* normal), then `reflect(-V, Nw)`.

**The damping ramp.** You already have the quantity it should be a function of — `ndv` from Step 6.
Something like `0.3 + 0.7 * pow(saturate(1 - saturate(ndv)), 1.5)`: a floor so chrome never fully
disappears, opening toward grazing where a mirror reads best anyway.

**Verify.** Chrome only. Face the card at the camera: the print must still be readable. Turn 45°: the
mirror takes over. The sun must be a tight highlight that sweeps, not a wash.

→ *Appendix A, Step 12*

## Step 13 — Layer 5: edge glow — Build

```hlsl
// Spec: a glossy rim that appears at grazing angles and vanishes head-on.
// One line, from a quantity you already have.
//
// TODO: float fresnel = ...
```

Physically this stands in for Fresnel reflectance — the fact that any dielectric surface becomes more
reflective the further from its normal you view it. A `pow` of one dot product is a defensible cheat
and it is what most real-time shaders use.

**Verify.** It must appear on the *silhouette*, wherever the silhouette currently is — which for a
bent card in Part 4 is not the outline of the mesh.

→ *Appendix A, Step 13*

## Step 14 — Where the card is allowed to foil — Build

Uniform foil over the whole card looks like cling film. Real foil is printed on specific areas.

```hlsl
// Spec: a 0..1 mask, from two sources multiplied together.
//   1. #ifdef _MASKTEX: the R channel of _MaskTex.
//   2. From the LUMINANCE of baseColor: remapped with _MaskContrast around a
//      0.5 pivot, biased by _MaskBias, then blended in by _MaskFromLuma.
//
// _MaskFromLuma = 0 must be EXACTLY 1.0 - i.e. no masking at all.
//
// TODO: float mask = 1.0; ...
```

**Why luminance at all,** when you could paint a mask per card? Count the cards in your deck folder,
then count them again for a second deck. A luminance mask is free and lands foil on the bright parts
of the print, which is where foil goes anyway.

**Luminance weights.** `0.299, 0.587, 0.114` — the standard Rec.601 coefficients. Not `1/3` each,
because the eye is far more sensitive to green than to blue, and an even average makes a saturated
blue read as bright as a mid grey when it is not.

**Verify.** `_MaskFromLuma = 0` must be pixel-identical to the unmasked card. Screenshot both and
diff them — "looks the same" is not the test. Then raise `_MaskContrast` to 8 and watch the foil
retreat onto the brightest parts of the print.

→ *Appendix A, Step 14*

## Step 15 — Compositing — Build

```hlsl
// A. In CardFoil, fill in the _HOLOPIXELATE block: snap hUV to the card's texel
//    grid, WITHOUT the screen-pixel ramp from Step 3.
//
// B. At the end of CardFoil, quantise the accumulated foil to _ColorSteps.
//
// C. In Frag, composite. _FoilBlend lerps between:
//       additive:  col + foil
//       screen:    1 - (1-col)*(1-foil)
//
// TODO
```

**Why no ramp on the foil snap.** In Step 3 the ramp existed to anti-alias the *artwork's* texel
edges against the screen. The foil is not artwork — it is a value that varies smoothly across the
card, and snapping it to the pixel grid is what makes it look printed *on* the pixel art rather than
floating above it. A ramp would blur exactly the edges you are trying to create.

So `_PIXELAA` and `_HOLOPIXELATE` are two different snaps applied to two different things. Confusing
them is the classic mistake in this shader.

**Additive vs screen.** Additive is brighter and clips: `1.0 + 0.5` is white and stays white, so foil
over already-bright artwork loses all detail. Screen saturates toward 1 instead of crossing it, which
keeps the print visible under the foil but reads flatter. `_FoilBlend = 0.65` is a compromise found
by looking, not derived.

**Verify.** Toggle both keywords — **in both passes**. Set `_FoilBlend` to 0 and 1 on a bright card
and confirm you can see the difference in the highlights.

→ *Appendix A, Step 15*

## Step 16 — Rarity tiers — Given

Five materials, one shader. This is data, not insight — paste it.

`Assets/Editor/CardMaterialBuilder.cs`:

```csharp
using UnityEditor;
using UnityEngine;

namespace CardFx.EditorTools
{
    public static class CardMaterialBuilder
    {
        const string ShaderPath = "Assets/Shaders/CardHolo.shader";
        const string Folder = "Assets/Materials";
        static readonly string[] Tiers = { "Common", "Shiny", "Holo", "Galaxy", "Chrome" };

        [MenuItem("Tools/Card FX/Build Card Materials")]
        public static void Build()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) { Debug.LogError("Shader not found"); return; }
            System.IO.Directory.CreateDirectory(Folder);

            foreach (string tier in Tiers)
            {
                string path = $"{Folder}/Card_{tier}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
                var mat = new Material(shader) { name = $"Card_{tier}" };
                ConfigureShared(mat);
                ConfigureTier(mat, tier);
                AssetDatabase.CreateAsset(mat, path);
            }
            AssetDatabase.SaveAssets();
        }

        static void ConfigureShared(Material mat)
        {
            mat.SetVector("_CardPixels", new Vector4(73f, 113f, 0f, 0f));
            mat.SetVector("_CardWorldSize", new Vector4(0.73f, 1.13f, 0f, 0f));
            mat.SetFloat("_Cutoff", 0.5f);
            SetToggle(mat, "_PixelAA", "_PIXELAA", true);
            SetToggle(mat, "_HoloPixelate", "_HOLOPIXELATE", true);
            SetToggle(mat, "_UseMaskTex", "_MASKTEX", false);
            mat.SetFloat("_FoilBlend", 0.65f);
            mat.SetFloat("_MaskFromLuma", 0.45f);
            mat.SetFloat("_MaskContrast", 2f);
            mat.SetFloat("_MaskBias", 0.1f);

            // Wear. _WearAmount stays 0 on the shared material - CardWear turns it on
            // per card through the property block, so a pristine card never samples
            // the maps at all. The rest is the LOOK of damage, same on every tier.
            mat.SetFloat("_WearAmount", 0f);
            mat.SetFloat("_WearSteps", 5f);
            mat.SetColor("_StockColor", new Color(0.84f, 0.80f, 0.72f, 1f));
            mat.SetFloat("_InkLossDesat", 1.2f);
            mat.SetFloat("_ScuffFoilLoss", 1f);
            mat.SetFloat("_ScuffHaze", 0.35f);
            mat.SetFloat("_ScuffGlint", 1.2f);
            mat.SetFloat("_DentDepth", 8f);
            mat.SetFloat("_DentDisplace", 0.012f);
            mat.SetFloat("_CornerRadius", 0.5f);
            mat.SetVector("_Bow", Vector4.zero);
            mat.SetVector("_CornerBend", Vector4.zero);
        }

        static void ConfigureTier(Material mat, string tier)
        {
            mat.SetFloat("_FoilIntensity", 1f);
            mat.SetFloat("_TiltGain", 1f);
            mat.SetFloat("_RainbowStrength", 0f);
            mat.SetFloat("_SparkleStrength", 0f);
            mat.SetFloat("_ChromeStrength", 0f);
            mat.SetFloat("_SweepStrength", 0f);
            mat.SetFloat("_FresnelStrength", 0f);
            mat.SetFloat("_ColorSteps", 0f);

            switch (tier)
            {
                case "Common":
                    mat.SetFloat("_FoilIntensity", 0f);
                    break;
                case "Shiny":
                    mat.SetFloat("_RainbowStrength", 0.1f);
                    mat.SetFloat("_SweepStrength", 0.4f);
                    mat.SetFloat("_SweepWidth", 0.22f);
                    mat.SetFloat("_FresnelStrength", 0.2f);
                    break;
                case "Holo":
                    mat.SetFloat("_RainbowStrength", 0.55f);
                    mat.SetFloat("_RainbowScale", 3.5f);
                    mat.SetFloat("_RainbowTilt", 1.4f);
                    mat.SetFloat("_RainbowSteps", 8f);
                    mat.SetFloat("_SweepStrength", 0.25f);
                    mat.SetFloat("_FresnelStrength", 0.2f);
                    mat.SetFloat("_MaskFromLuma", 0.35f);
                    break;
                case "Galaxy":
                    mat.SetFloat("_RainbowStrength", 0.4f);
                    mat.SetFloat("_RainbowScale", 2f);
                    mat.SetFloat("_RainbowSteps", 6f);
                    mat.SetFloat("_SparkleStrength", 1.1f);
                    mat.SetFloat("_SparkleDensity", 30f);
                    mat.SetFloat("_SparkleSize", 0.28f);
                    mat.SetFloat("_SweepStrength", 0.2f);
                    mat.SetFloat("_FresnelStrength", 0.25f);
                    mat.SetColor("_SparkleColor", new Color(1f, 0.95f, 0.8f, 1f));
                    break;
                case "Chrome":
                    mat.SetFloat("_FoilIntensity", 0.9f);
                    mat.SetFloat("_ChromeStrength", 0.45f);
                    mat.SetFloat("_ChromeSharp", 64f);
                    mat.SetFloat("_RainbowStrength", 0.18f);
                    mat.SetFloat("_RainbowSteps", 10f);
                    mat.SetFloat("_SweepStrength", 0.5f);
                    mat.SetFloat("_SweepWidth", 0.18f);
                    mat.SetFloat("_FresnelStrength", 0.3f);
                    mat.SetFloat("_MaskFromLuma", 0.2f);
                    break;
            }
        }

        static void SetToggle(Material mat, string property, string keyword, bool on)
        {
            mat.SetFloat(property, on ? 1f : 0f);
            if (on) mat.EnableKeyword(keyword);
            else mat.DisableKeyword(keyword);
        }
    }
}
```

Note what the tiers actually are: **the same five layers at different strengths**. Common is
`_FoilIntensity = 0` — literally the same shader with the foil turned off, which is why a common card
costs nothing extra to draw.

---

# Part 3 — Damage

Five kinds of damage — scuffs, ink loss, dents, creases, chips — plus bending. The lesson of this
part is that they are **one texture**, and that one of its four channels makes three of the foil
layers react to damage without a single line of foil code knowing damage exists.

## Step 17 — The map — Given (this one is a spec, not code)

One RGBA texture **per face**, at exactly the card's pixel size (73×113), holding everything:

| Channel | Holds | Per face? |
|---|---|---|
| **R** | scuff — abrasion, scratches, the matte patches that kill foil | **yes** |
| **G** | ink loss — colour coming off, worst at edges and corners | **yes** |
| **B** | height — `0.5` is flat, below is a dent, above is a ridge | **front only** |
| **A** | missing — material that is not there, chips out of the edge | **front only** |

Four decisions worth understanding, because each is a bug if you get it wrong:

1. **R and G are per face.** A scuff on the front is not a scuff on the back. That is what forces a
   player to turn the card over to restore it.
2. **B and A always come from the front map, for both faces.** A dent goes *through* the paper — a
   valley on one side is a ridge on the other, not an independent value. A chip is missing from both
   sides by definition.
3. Following from 2: the `DepthOnly` pass only ever sees the front. If chips lived in the back map,
   a chipped pixel viewed from behind would write depth over a hole the forward pass had already
   clipped away, and the hole would fill with whatever is behind the card.
4. **Height is signed in an unsigned texture**, so `0.5` is flat and both dents and ridges have
   somewhere to go. Encode `h*0.5 + 0.5`, decode `(v - 0.5) * 2`.

**Import settings — created in code, never through the importer:**

| Setting | Value | Why |
|---|---|---|
| Format | `RGBA32` | one byte per channel, uploaded whole |
| linear | **true** (i.e. *not* sRGB) | it is data. A gamma curve bends every rate |
| Filter | **Point** | wear texels must land on art texels |
| Wrap | **Clamp** | the Step 22 neighbour taps must not wrap to the far edge |
| Mips | **off** | there is one level, and Step 19 asks for it explicitly |

**Bending is not in the map.** A bow has to change the **silhouette**, and no amount of shading will
do that. It is three scalars and lives in the vertex shader — Part 4.

## Step 18 — The damage generator — Given

`Assets/Scripts/Runtime/CardWear.cs`. Long, but it is all bookkeeping around five small passes.

```csharp
using System;
using Unity.Collections;
using UnityEngine;

namespace CardFx
{
    /// <summary>
    /// One card's condition, and the only thing that writes it. Five kinds of
    /// damage in a single RGBA map per face at the card's own pixel size, plus
    /// three scalars for bending, which is geometry and cannot be painted.
    ///
    /// The map is held on the CPU on purpose. At 73x113 the whole thing uploads in
    /// a fraction of a frame, and generation stays plain C# with no readback to
    /// wait on and no ping-pong buffer to keep in step.
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

        /// <summary>Bow at the centre of a ruined card, world units. Generous against
        /// the card's 1.13 height: anything subtle enough to be plausible is invisible.
        /// The mesh bounds must reserve room for this - see MeshBendHeadroom.</summary>
        public const float MaxBow = 0.035f;
        public const float MaxCornerBend = 0.07f;

        /// <summary>Which passes a card is aged with. The single kinds are for a
        /// bench that shows one at a time. Note a subset is its OWN card, not a
        /// layer of the full one: the passes share one random stream, so leaving one
        /// out changes the draws the rest get.</summary>
        [Flags]
        public enum Damage
        {
            None = 0,
            EdgeWear = 1 << 0, Scratches = 1 << 1, Dents = 1 << 2,
            Creases = 1 << 3, Chips = 1 << 4, Bend = 1 << 5,
            All = EdgeWear | Scratches | Dents | Creases | Chips | Bend,
        }

        /// <summary>One texel, kept in floats rather than the bytes it uploads as.
        /// A soft brush rubs its outermost texels at a twentieth of the centre's
        /// rate - in bytes that rounds to no change at all, and the brush grows a
        /// hard edge it should not have.</summary>
        struct Texel { public float Scuff, Ink, Height, Missing; }

        [Tooltip("Must match _CardPixels on the material, or wear texels stop landing " +
                 "on artwork texels and the damage goes blurry.")]
        [SerializeField] Vector2Int resolution = new Vector2Int(73, 113);
        [SerializeField, Range(0f, 1f)] float defaultSeverity;

        CardView view;
        Texel[] front, back;
        Texture2D frontMap, backMap;
        Vector2 bow;
        Vector4 cornerBend;
        bool frontDirty, backDirty, bendDirty;
        int seed;
        float severity;
        Damage damage = Damage.All;

        int W => resolution.x;
        int H => resolution.y;

        public bool IsWorn => front != null;
        public Vector2 Bow => bow;
        public Vector4 CornerBend => cornerBend;
        public Texture2D FrontMap => frontMap;
        public Texture2D BackMap => backMap;

        void Awake()
        {
            view = GetComponent<CardView>();
            if (defaultSeverity > 0f) Age(UnityEngine.Random.Range(int.MinValue, int.MaxValue), defaultSeverity);
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
        /// <summary>Deterministic from the seed, so the same card ages the same way
        /// every time it loads and none of it has to be stored.</summary>
        public void Age(int cardSeed, float cardSeverity, Damage kinds = Damage.All)
        {
            seed = cardSeed;
            severity = Mathf.Clamp01(cardSeverity);
            damage = kinds;
            Rebuild();
        }

        public void MakePristine() { severity = 0f; Rebuild(); }

        void Rebuild()
        {
            EnsureMaps();
            Array.Clear(front, 0, front.Length);
            Array.Clear(back, 0, back.Length);
            bow = Vector2.zero;
            cornerBend = Vector4.zero;
            if (severity > 0f) Generate();
            frontDirty = backDirty = bendDirty = true;
        }

        bool Ages(Damage kind) => (damage & kind) != 0;

        void Generate()
        {
            var rng = new System.Random(seed);
            float s = severity;

            if (Ages(Damage.EdgeWear)) EdgeWear(rng, s);

            int scratches = Mathf.RoundToInt(Mathf.Lerp(0f, 16f, s * s));
            if (Ages(Damage.Scratches)) for (int i = 0; i < scratches; i++) Scratch(rng, s);

            int dents = Mathf.RoundToInt(Mathf.Lerp(0f, 7f, s));
            if (Ages(Damage.Dents)) for (int i = 0; i < dents; i++) Dent(rng, s);

            int creases = s < 0.45f ? 0 : (s < 0.8f ? 1 : 2);
            if (Ages(Damage.Creases)) for (int i = 0; i < creases; i++) Crease(rng, s);

            int chips = Mathf.RoundToInt(Mathf.Lerp(0f, 5f, Mathf.InverseLerp(0.25f, 1f, s)));
            if (Ages(Damage.Chips)) for (int i = 0; i < chips; i++) Chip(rng, s);

            if (!Ages(Damage.Bend)) return;
            // A card that has been sat on is bowed, and the corner that was on the
            // outside of the pile is the one that turned up.
            bow = new Vector2(Range(rng, -1f, 1f), Range(rng, -1f, 1f)) * MaxBow * s;
            if (s > 0.35f) cornerBend[rng.Next(0, 4)] = Range(rng, 0.4f, 1f) * MaxCornerBend * s;
        }

        /// <summary>Edges first, and hardest. A card is handled by its border, pulled
        /// in and out of a sleeve by it and squared against a table on it, which is
        /// why the print goes there long before anything happens in the middle.</summary>
        void EdgeWear(System.Random rng, float s)
        {
            const float band = 5f;
            int hash = rng.Next();
            for (int y = 0; y < H; y++)
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

        void Scratch(System.Random rng, float s)
        {
            var map = rng.NextDouble() < 0.6 ? front : back;
            float angle = Range(rng, 0f, Mathf.PI * 2f);
            float length = Range(rng, 6f, 34f) * Mathf.Lerp(0.5f, 1f, s);
            float depth = Range(rng, 0.3f, 1f) * s;
            float wobble = Range(rng, -0.02f, 0.02f);
            bool wide = rng.NextDouble() < 0.25;
            float x0 = Range(rng, 0f, W), y0 = Range(rng, 0f, H);

            for (float t = 0f; t < length; t += 0.7f)
            {
                float a = angle + wobble * t;
                int px = Mathf.RoundToInt(x0 + Mathf.Cos(a) * t);
                int py = Mathf.RoundToInt(y0 + Mathf.Sin(a) * t);
                // Fades along its length, the way a dragged edge lifts off the card.
                float fade = depth * (1f - t / length * 0.6f);
                // A scratch takes print with it - that is what makes it visible at
                // all. Scuff alone only kills foil, which leaves a COMMON card
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

        /// <summary>A fold, not a scratch: a valley along the line with the paper
        /// standing up either side of it, which is what a card actually does when it
        /// is bent and pressed back. The crest loses ink too - print cracks off a
        /// fold before anything else on the card gives.</summary>
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

        /// <summary>A bite out of the outline. Centred ON the border rather than
        /// inside it, so the hole opens into the edge instead of turning up as a
        /// pinprick in the middle of the frame.</summary>
        void Chip(System.Random rng, float s)
        {
            float r = Range(rng, 1.2f, 3.4f) * Mathf.Lerp(0.6f, 1f, s);
            int cx, cy;
            if (rng.NextDouble() < 0.45)      // corners chip more than sides do
            {
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
            for (int x = cx - ri; x <= cx + ri; x++)
            {
                if (!Inside(x, y)) continue;
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                int i = x + y * W;
                if (d <= r) front[i].Missing = 1f;
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

        // -------------------------------------------------------------------
        // Upload
        // -------------------------------------------------------------------
        void EnsureMaps()
        {
            if (front != null) return;
            front = new Texel[W * H];
            back = new Texel[W * H];
            frontMap = NewMap("WearFront");
            backMap = NewMap("WearBack");
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
            // linear (the `true` on the end), NEVER sRGB: this is data, and a gamma
            // curve on it would bend every rate. Point + Clamp so wear texels land on
            // artwork texels, and so the dent-normal neighbour taps do not wrap round
            // to the far side of the card.
            return new Texture2D(W, H, TextureFormat.RGBA32, false, true)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
        }

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
                    ToByte(t.Height * 0.5f + 0.5f),   // 128 is flat
                    ToByte(t.Missing));
            }
            tex.Apply(false);
        }

        static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

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

        /// <summary>Stamping damage takes the WORSE of the two rather than piling up,
        /// so two scratches crossing do not read as a hole.</summary>
        static void Raise(ref float value, float amount)
        {
            if (amount <= 0f) return;
            value = Mathf.Clamp01(Mathf.Max(value, amount));
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
    }
}
```

Add `CardWear` to the card object and set **Default Severity** to `0.7`. Nothing will change yet —
the shader does not read the map. That is the next four steps.

**Out of scope here:** restoration. If you want it, the shape is that `Rub(uv, backFace, tool,
seconds)` is the *only* edit path, a tool is nothing but a set of rates, and every tool damages
something else while it works. That makes the repair order emerge from the rates rather than from any
check enforcing it. It needs nothing new from the shader.

## Step 19 — Reading the map — Build

Add to the CBUFFER: `float4 _StockColor;`, `float _WearAmount, _WearSteps, _ScuffFoilLoss,
_ScuffHaze, _ScuffGlint, _InkLossDesat, _DentDepth;` and
`TEXTURE2D(_WearTex); SAMPLER(sampler_WearTex); TEXTURE2D(_WearBackTex);`

New file `Assets/Shaders/CardWear.hlsl`, included from `CardHoloInput.hlsl` **after** the CBUFFER and
the samplers — everything in it reads uniforms that must live in that one buffer.

```hlsl
struct CardWear { float scuff, inkLoss, height, missing; };

// Spec: quantise to _WearSteps. floor, not round - see below.
float CardWearQuantize(float v)
{
    // TODO (return v unchanged when _WearSteps < 1)
}

// Spec: sample both maps once and fill the struct.
//   - snappedUV is the pixel-snapped uv the ARTWORK is sampled with.
//   - facing picks front.rg or back.rg for scuff and ink.
//   - height and missing ALWAYS come from the front map.
//   - everything scales by _WearAmount, and scuff/ink are quantised.
//   - early out entirely when _WearAmount <= 0.
CardWear SampleCardWear(float2 snappedUV, float facing)
{
    // TODO
}
```

**Constraints.**

- Every read is an **explicit LOD 0** (`SAMPLE_TEXTURE2D_LOD(..., 0)`). Two reasons: the map has no
  mip chain, so it is the only level there is; and these sit inside a branch on `_WearAmount`, which
  is exactly where an implicit gradient is least welcome.
- `floor`, not `round`. Damage in visible steps is what makes a rub read as *progress* — a scratch
  goes four texels, three, two, gone. With `round`, the last step leaves a ghost of the texel behind
  instead of clearing it. Try `round` and watch it happen.
- Sample wear at the **snapped** uv, so wear texels land on art texels and share the Step 3 ramp.
  Step 22's height taps are the exception and use the **raw** uv — there the ramp only smears the
  gradient you are trying to measure.

→ *Appendix A, Step 19*

## Step 20 — Ink loss — Build

```hlsl
// Spec: print coming off. It does TWO things to the colour, in an order:
//   1. the print goes FLAT (loses saturation),
//   2. then it fades toward the bare card stock underneath (_StockColor).
// _InkLossDesat scales how fast the first happens relative to the second.
float3 CardApplyInkLoss(float3 col, float inkLoss)
{
    // TODO
}
```

**Do it in the wrong order once**, deliberately — fade to stock first, then desaturate. The
difference is subtle and instructive: worn print that desaturates first looks *chalky*, which is what
old ink does; the other order looks like the card was washed.

Call it in `Frag` on the base colour **before** computing foil. Faded artwork then foils less on its
own, for free — the Step 14 luminance mask is already looking at the faded colour.

→ *Appendix A, Step 20*

## Step 21 — Chips — Build

```hlsl
// A. In Frag: fold `missing` into the existing clip.
// B. In DepthOnly's fragment: the SAME clip. It reads only _WearTex's alpha,
//    at LOD 0, and only when _WearAmount > 0.
//
// TODO
```

**Predict before you write.** What do you see if you do A and forget B? Then forget B on purpose and
check you were right. (A chipped pixel writes depth over the background the forward pass clipped
through to, so the hole fills with whatever is behind the card.)

**Verify.** Age hard. Chips must be visible holes with the background through them, from **both**
faces, that do not flicker against anything drawn behind.

→ *Appendix A, Step 21*

## Step 22 — Dents as a normal — Build

```hlsl
// Spec: tangent-space normal of the dents and creases, from the SLOPE of the
// height channel across neighbouring texels.
//   - Four taps: left, right, down, up, one texel apart. RAW uv, not snapped.
//   - Scale the slope by _DentDepth and _WearAmount.
//   - Flat paper must return exactly (0, 0, 1).
//   - On the back face the xy must negate. TWO separate reasons produce that
//     single negation - work out both.
float3 CardWearNormal(float2 rawUV, float facing)
{
    // TODO
}
```

**The back-face negation.** Reason one: uv.x is mirrored on the back (Step 2), which flips the u
slope. Reason two: a dent seen from behind is a *bump*. Each flips something; together they land on
one negation of the tangent-space xy. Convince yourself with a drawing before you accept the line.

**Deliberately blocky.** One normal per card texel, not smoothed. Smooth it and look — a soft bump
map over pixel art reads as a completely different card, like a 3D render of a card rather than a
pixel-art card that is damaged.

**Verify.** Debug view it (`nTS * 0.5 + 0.5`). Flat paper is the familiar flat blue; dents and
creases push it off.

→ *Appendix A, Step 22*

## Step 23 — The payoff — Build

This is what the whole part was for, and it is a two-line edit.

```hlsl
// In Frag, the tilt block from Step 6 currently treats the surface as flat.
// Make it use nTS instead.
//
// Spec: with nTS = (0,0,1) the result must be BIT-IDENTICAL to Step 6.
// TODO
```

Nothing in `CardFoil` changes. Not one line.

**Verify — and this is the real test of the part.** Age a card, turn it, watch a crease. The rainbow
must **bend** along it. The sweep must **fracture** across it. The sparkle must fire different flakes
where the surface is tilted differently. Then:

```bash
grep -n "wear" Assets/Shaders/CardHoloInput.hlsl
```

The rainbow, sweep, sparkle and chrome sections must contain **no hit at all** — and still break over
dents. If a dent only *darkens* the card, the normal is being computed and not used.

→ *Appendix A, Step 23*

## Step 24 — Scuff kills foil — Build

```hlsl
// A. In CardFoil's mask: abraded foil is dead foil.
//      mask *= saturate(1 - wear.scuff * _ScuffFoilLoss)
//    Pass the CardWear struct into CardFoil to do it.
//
// B. AFTER the mask and AFTER _FoilIntensity, add the abrasion's own light:
//      _ScuffHaze  * scuff * (0.35 + sweep)      - a dull haze
//      _ScuffGlint * scuff * sweep * fresnel     - the glint off a scratch
//
// TODO
```

**Why B comes after the intensity, and it is not arbitrary.** A scuffed **common** card has
`_FoilIntensity = 0`. It has no foil to lose, but it still has to show its scratches — and everything
above that line has just been multiplied away to nothing. Put the haze above it and common cards
cannot be damaged.

**A is the loudest damage cue the card has.** Every foil layer is driven off the same mask, so a
scuffed patch stops diffracting, stops sparkling and stops sweeping all at once — with one line.

→ *Appendix A, Step 24*

---

# Part 4 — Bending

Low frequency, geometric, and in the vertex shader — because it has to change the **silhouette**, and
no amount of shading will do that.

## Step 25 — The height field — Build

Add to the CBUFFER: `float4 _CardWorldSize, _Bow, _CornerBend; float _DentDisplace, _CornerRadius;`

```hlsl
// Spec: 0 at that corner, 1 at the far one, falling off over _CornerRadius,
// with SMOOTH derivatives at both ends (a linear ramp creases visibly).
// c is uv remapped to -1..1; corner is one of (+-1, +-1).
float CardCornerBend(float2 c, float2 corner, float amount)
{
    // TODO
}

// Spec: displacement along the card's own normal, in world units, at a point.
//   - _Bow.x bows across x, _Bow.y across y. Zero at the edges, max in the middle.
//   - Plus the four corner bends: _CornerBend is (BL, BR, TL, TR).
//   - Plus creases from the wear map's height channel, scaled by _DentDisplace,
//     but ONLY when _WearAmount > 0.
float CardBendHeight(float2 uv)
{
    // TODO
}
```

**The crease tap uses a LINEAR sampler**, not the point one the fragment stage uses:
`SAMPLE_TEXTURE2D_LOD(_WearTex, sampler_LinearClamp, uv, 0)`. This is the *fold*, and a fold that
stair-stepped across the 16×24 mesh grid would be visible as facets. `sampler_LinearClamp` is a
built-in URP sampler — you do not declare it.

**Smoothstep for the corner.** `w*w*(3-2*w)` is `smoothstep`'s polynomial; its derivative is zero at
both ends, which is what stops the fold showing a crease where it meets the flat part.

→ *Appendix A, Step 25*

## Step 26 — Rebuilding the frame — Build

The hard one. You have moved the surface; the normal and tangent must follow, or the foil still
thinks the card is flat.

```hlsl
// Spec: bend the card AND rebuild the frame that was bent with it.
// Object space throughout, where the card's frame is the axes themselves:
// T = +X, B = +Y, N = -Z.
//
//   - Sample the height at uv, and at uv + (e,0) and uv + (0,e).
//   - Displace positionOS along the normal.
//   - Rebuild normalOS and tangentOS from the two slopes.
//
// Must degrade to exactly the flat frame when the height field is constant.
void CardApplyBend(inout float3 positionOS, inout float3 normalOS,
                   inout float4 tangentOS, float2 uv)
{
    const float e = 0.02;
    // TODO
}

// Position-only variant for the depth pass. Must agree with the above TO THE BIT.
void CardApplyBendPosition(inout float3 positionOS, float2 uv)
{
    // TODO
}
```

**The derivation, since it is the one piece of real calculus here.** After displacing along the
normal by `h(u,v)`, the surface tangents are

```
dP/du = T*width  + N*(dh/du)
dP/dv = B*height + N*(dh/dv)
```

Their cross product simplifies to `N - T*(dh/du)/width - B*(dh/dv)/height`. So the new normal in the
card's own frame is `normalize(float3(-k, 1))` where `k` is the two slopes **divided by the card's
world dimensions**. Leave `_CardWorldSize` out and the bend's shading is wrong by the card's aspect
ratio — which looks like a subtly wrong bow rather than like a bug, and is why it is easy to ship.

**Why `e = 0.02` and not something tiny.** The bend is smooth across the mesh grid anyway, and a
tighter step just samples the wear map's own texel noise. Wide enough to stay clear of the grid, small
enough to still see a fold.

**Call it first in `Vert`,** before `GetVertexPositionInputs` — so the frame that comes out of
`GetVertexNormalInputs` is the *bent* card's, not the flat one's.

→ *Appendix A, Step 26*

## Step 27 — Keeping depth honest — Build

```hlsl
// In DepthVert: call CardApplyBendPosition on positionOS before transforming.
// TODO
```

Three things that must now be true, all of which are silent failures:

1. `#pragma target 3.5` in **both** passes — the vertex stage samples a texture now.
2. `CardApplyBend` and `CardApplyBendPosition` compute the same displacement. Any drift is
   z-fighting along the bend.
3. The mesh bounds reserve `MeshBendHeadroom` (Step 0.4). A bowed card culled against a flat slab
   pops out of view at the edge of the screen — a bug that looks like a camera problem.

**Verify.** Look at the card **edge-on** against a contrasting background: the bow must be visible in
the **outline**, not just in the shading. Then put something intersecting it and check for z-fighting.

→ *Appendix A, Step 27*

---

# Part 5 — Debug views

Out of order on purpose: you have debugged blind for 27 steps. Build the instrument now and notice
how much faster Parts 6 and 7 go.

## Step 28 — CardDebug.hlsl — Build

```hlsl
// Add to the CBUFFER: float _DebugView;
// Property: _DebugView("View (0 off)", Range(0,9)) = 0
//
// Spec: switch the fragment output between ten views. Every value it draws is
// PASSED IN from Frag - never recomputed here.
float3 CardDebugColor(float2 uv, float2 sampleUV, float3 nTS, float2 tilt,
                      float ndv, CardWear wear, float3 foil, float facing)
{
    // Derivatives FIRST, before any branching (see below).
    float2 texels = uv * max(_CardPixels.xy, 1.0);
    float density = max(length(ddx(texels)), length(ddy(texels)));

    // 1 uv          red = u, green = v
    // 2 texels      the card's texel grid, with the Step 3 snapping ramp in red
    // 3 normal      nTS * 0.5 + 0.5 - flat paper is the familiar blue
    // 4 tilt        the one vector the whole foil is driven by
    // 5 N.V         white head-on, black at grazing; tint the back face
    // 6 wear        R scuff, G ink, B missing
    // 7 height      signed: orange ridge, blue dent
    // 8 foil        the foil alone, artwork taken away
    // 9 density     card texels per screen pixel - what mip selection is made of
    //
    // TODO
}
```

Then at the end of `Frag`:

```hlsl
if (_DebugView > 0.0)
    col = CardDebugColor(uv, sampleUV, nTS, tilt, ndv, wear, foil, facing);
```

**Three rules, each with a reason:**

- **Never recompute the subject.** A debug view that works out its own tilt is free to drift from the
  tilt the card actually used, and a teaching view that drifts is worse than none. Hand everything in.
- **Derivatives before any branch.** `ddx`/`ddy` are only defined in *uniform control flow* — every
  pixel in a 2×2 rasterizer quad must reach them. Taking them at the top keeps that true whatever the
  compiler does with the ladder below. View 9 is where this bites.
- **A uniform branch, not a keyword.** `_DebugView` is the same for every pixel of the draw, so the
  hardware skips the whole block and a card at 0 pays nothing. A keyword would double your variant
  count for a feature no shipped material uses. The same reasoning is why the wear code has no
  keyword either.

**Verify.** Frame Debugger: adding the views must not change the variant count.

→ *Appendix A, Step 28*

---

# Part 6 — The pack tear

A booster wrapper that rips open along a ragged seam and peels back. Two meshes, one material.

The insight worth taking from this part: **the rip is in the mesh, the peel is in the shader.** The
seam's raggedness is baked into geometry once, at build time; the animation is two scalars.

## Step 29 — The torn meshes — Given

The wrapper is cut into a run of column quads with flat tops, so the rip is a **pixel staircase**
rather than a smooth diagonal. Both halves share one seam array, which is what makes them interlock
exactly.

`Assets/Editor/PackMeshBuilder.cs`:

```csharp
using UnityEditor;
using UnityEngine;

namespace CardFx.EditorTools
{
    public static class PackMeshBuilder
    {
        const float PixelsPerUnit = 100f;
        const int PackWidthPx = 84, PackHeightPx = 154;
        static readonly Vector2 PackSize = new Vector2(PackWidthPx / PixelsPerUnit, PackHeightPx / PixelsPerUnit);

        /// <summary>The seam sits one pixel below the crimped seal, so the strip that
        /// peels away is exactly the sealed part of the wrapper.</summary>
        const int TopSealPixels = 11;
        const int TearPixelsFromTop = TopSealPixels + 1;

        /// <summary>One column per two pixels, which keeps the rip on the pixel grid.</summary>
        const int TearColumns = PackWidthPx / 2;
        const float TearAmplitude = 0.03f;

        /// <summary>Body and lid overlap by a pixel so no hairline shows along the seam.</summary>
        const float SeamOverlap = 0.01f;

        [MenuItem("Tools/Card FX/Build Pack Meshes")]
        public static void Build()
        {
            float[] seam = BuildSeam(TearColumns, TearAmplitude);
            float tearY = PackSize.y * 0.5f - TearPixelsFromTop / PixelsPerUnit;
            System.IO.Directory.CreateDirectory("Assets/Meshes");
            BuildPackMesh(seam, tearY, false, "Assets/Meshes/PackBody.asset", "PackBody");
            BuildPackMesh(seam, tearY, true, "Assets/Meshes/PackLid.asset", "PackLid");
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Height offset of the tear at every column boundary. TWO scales of noise:
        /// a straight cut with jitter on top reads as a print artefact - a rip needs
        /// coarse waviness as well. Snapped to whole pixels so the seam stays on the
        /// art's grid, and pinned flat at both ends so the pack keeps a square
        /// silhouette while it is still sealed.
        /// </summary>
        static float[] BuildSeam(int columns, float amplitude)
        {
            var seam = new float[columns];
            var rng = new System.Random(20260814);
            float phase = (float)rng.NextDouble() * 10f;

            for (int i = 0; i < columns; i++)
            {
                float t = i / (float)(columns - 1);
                float coarse = Mathf.Sin(t * 6.3f + phase) * 0.55f + Mathf.Sin(t * 15.7f + phase * 2.1f) * 0.25f;
                float fine = (float)rng.NextDouble() * 2f - 1f;
                float value = (coarse * 0.62f + fine * 0.38f) * amplitude;
                seam[i] = Mathf.Round(value * PixelsPerUnit) / PixelsPerUnit;
            }
            seam[0] = 0f;
            seam[columns - 1] = 0f;
            return seam;
        }

        static Mesh BuildPackMesh(float[] seam, float tearY, bool isLid, string path, string name)
        {
            int columns = seam.Length;
            float w = PackSize.x * 0.5f, h = PackSize.y * 0.5f;
            float pivot = isLid ? tearY : 0f;
            float hingeSpan = isLid ? h - tearY : h + tearY;

            var vertices = new Vector3[columns * 4];
            var uv = new Vector2[vertices.Length];
            var uv2 = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var tangents = new Vector4[vertices.Length];
            var triangles = new int[columns * 6];

            var normal = Vector3.back;                      // faces -Z, like the card
            var tangent = new Vector4(1f, 0f, 0f, -1f);

            for (int i = 0; i < columns; i++)
            {
                float x0 = Mathf.Lerp(-w, w, i / (float)columns);
                float x1 = Mathf.Lerp(-w, w, (i + 1) / (float)columns);
                float cut = tearY + seam[i];

                // Near edge is the seam, far edge is the outside of the pack.
                float near = isLid ? cut : cut + SeamOverlap;
                float far = isLid ? h : -h;
                float span = Mathf.Max(Mathf.Abs(far - near), 1e-4f);

                int v = i * 4;
                vertices[v + 0] = new Vector3(x0, near - pivot, 0f);
                vertices[v + 1] = new Vector3(x1, near - pivot, 0f);
                vertices[v + 2] = new Vector3(x0, far - pivot, 0f);
                vertices[v + 3] = new Vector3(x1, far - pivot, 0f);

                float u0 = (x0 + w) / PackSize.x, u1 = (x1 + w) / PackSize.x;
                float vNear = (near + h) / PackSize.y, vFar = (far + h) / PackSize.y;
                uv[v + 0] = new Vector2(u0, vNear);
                uv[v + 1] = new Vector2(u1, vNear);
                uv[v + 2] = new Vector2(u0, vFar);
                uv[v + 3] = new Vector2(u1, vFar);

                // x = distance from this column's own cut in TEXTURE PIXELS, which is
                // what the shader steps the torn lip along - hence pixels, not units.
                //
                // y is the hinge the peel swings on, and it is deliberately a SHARED
                // linear function of the undisplaced height rather than a per-column
                // 0..1 ramp. Neighbouring columns are cut at different heights, so a
                // per-column ramp would send the same point on their shared edge to
                // two different depths, and perspective opens that into a hairline
                // crack. For the same reason the shader must not clamp it.
                float spanPixels = span * PixelsPerUnit;
                uv2[v + 0] = new Vector2(0f, Hinge(near, tearY, hingeSpan, isLid));
                uv2[v + 1] = uv2[v + 0];
                uv2[v + 2] = new Vector2(spanPixels, Hinge(far, tearY, hingeSpan, isLid));
                uv2[v + 3] = uv2[v + 2];

                for (int k = 0; k < 4; k++) { normals[v + k] = normal; tangents[v + k] = tangent; }

                int t = i * 6;
                triangles[t + 0] = v + 0; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 2; triangles[t + 4] = v + 3; triangles[t + 5] = v + 1;
            }

            var mesh = new Mesh { name = name };
            mesh.vertices = vertices; mesh.uv = uv; mesh.uv2 = uv2;
            mesh.normals = normals; mesh.tangents = tangents; mesh.triangles = triangles;
            mesh.RecalculateBounds();

            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        /// <summary>0 on the nominal tear line, 1 at the piece's outside edge. Measured
        /// from the SHARED line rather than each column's own cut, so every column
        /// agrees on where a given height sits along the hinge.</summary>
        static float Hinge(float y, float tearY, float span, bool isLid)
            => (isLid ? y - tearY : tearY - y) / span;
    }
}
```

Read the `uv2` comment twice. It is the least obvious thing in this document and the source of a
hairline crack that only appears in perspective, at certain angles, on certain hardware.

## Step 30 — The pack shader — Given

`Assets/Shaders/CardPackInput.hlsl` — CBUFFER, atlas and sampling. The tear math is Steps 31–33.

```hlsl
#ifndef CARD_PACK_INPUT_INCLUDED
#define CARD_PACK_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    float4 _Tint;
    float4 _PackRect;
    float4 _PackPixels;
    float  _Cutoff;
    float  _PixelAA;

    float  _PeelMin;
    float  _PeelMax;
    float  _PeelFeather;
    float  _PeelFlat;
    float  _PeelLift;
    float  _PeelCurl;

    float4 _TearEdgeColor;
    float  _TearEdge;
    float  _TearEdgeWidth;
    float  _TearShade;

    float4 _ShineColor;
    float  _ShineStrength;
    float  _ShineWidth;
    float  _ShineAngle;
    float  _ShineTravel;
    float  _ShineOffset;
    float  _ShineTint;
    float  _BackShade;
CBUFFER_END

TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);

// The mesh is authored in CELL space (0..1 over one pack), so a single mesh and
// material can draw any pack on a sheet: _PackRect maps cell uv onto the atlas,
// xy = offset, zw = size. A MaterialPropertyBlock then picks the wrapper.
float2 PackAtlasUV(float2 cellUV)
{
    return _PackRect.xy + cellUV * _PackRect.zw;
}

// The wrapper is only ever MAGNIFIED - it fills a good part of the frame and never
// turns away the way a card does - so it samples mip 0 outright instead of taking
// the card shader's _GRAD route. Mip selection would be shaky here anyway: the mesh
// is one quad per two pixels, so most 2x2 rasterizer quads straddle a triangle edge,
// and the peel displaces neighbouring columns by different amounts. Any pixel that
// guessed a coarser mip would pull in the sheet's transparent gutters.
half4 SamplePack(float2 atlasUV)
{
    return SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, atlasUV, 0);
}

// Same snap as the card, in cell space against _PackPixels - which lands on atlas
// texel centres too, because every cell starts on a whole pixel.
float2 PackPixelUV(float2 cellUV, float2 cellPixels)
{
#ifdef _PIXELAA
    float2 p = cellUV * cellPixels;
    float2 seam = floor(p + 0.5);
    float2 dudv = clamp(fwidth(p), 1e-5, 1.0);
    p = seam + clamp((p - seam) / dudv, -0.5, 0.5);
    return p / cellPixels;
#else
    return cellUV;
#endif
}
```

(That last one is Step 3's answer, handed to you — you already earned it.)

## Step 31 — How far the seam has ripped — Build

```hlsl
// Spec: how open the tear is at this column of the pack.
//   - _PeelMin.._PeelMax is the span of the pack's width the cursor has swept.
//   - _PeelFeather keeps the strip ATTACHED either side of that span, so it
//     bulges rather than snapping off in one piece.
//   - _PeelFlat blends the whole strip to fully peeled once the tear completes.
// Returns 0..1.
float PackPeelShape(float u)
{
    // TODO
}
```

**The shape you want** is a plateau: 0 outside the swept span, 1 inside it, with a soft ramp at each
end. Two `smoothstep`s multiplied — one rising at `_PeelMin`, one falling at `_PeelMax` — give you
exactly that, and `_PeelFeather` is the width of both ramps.

**Verify.** Animate `_PeelMax` from `_PeelMin` to 1 and watch the strip open from one side.

→ *Appendix A, Step 31*

## Step 32 — The peel — Build

```hlsl
// Spec: lift and curl the flap. uv1.y is the hinge ramp from the mesh: 0 at the
// tear line, 1 at the far edge of the piece, so the flap hinges on the seam.
// The pack faces -Z, so curling toward -Z lifts it into view.
float3 PackPeelPosition(float3 positionOS, float2 uv, float2 uv1, out float shape)
{
    // TODO
}
```

**Do not clamp `uv1.y`.** It is a shared linear ramp and it sits slightly outside 0..1 where a column
is cut above or below the nominal line. Clamping breaks neighbouring columns apart along their shared
edge — the crack the Step 29 comment warns about.

**Three knobs, not one.** `_PeelLift` moves the flap along the pack's own plane; `_PeelCurl`
multiplies how much it *also* comes toward the viewer; `_PeelFlat` (inside `PackPeelShape`) finishes
the tear across the full width. One number could not produce a flap that both rises and rolls over.

→ *Appendix A, Step 32*

## Step 33 — The torn edge — Build

```hlsl
// Spec: the raw white lip along the tear, stepped onto WHOLE TEXTURE PIXELS.
//   - tearDist is distance from the cut, in pixels, off the mesh's uv1.x.
//   - `lip`   is 1 on the single outermost pixel, else 0.
//   - `shade` is the darkening behind it, stepping down one pixel at a time
//     over _TearEdgeWidth pixels.
//   - Both scale by `torn` (how far this column has actually opened).
void PackTornEdge(float tearDist, float torn, out float lip, out float shade)
{
    // TODO
}
```

**Why stepped and not smooth.** A smooth falloff here reads as a glow pasted over the pixel art — the
one thing that instantly breaks the illusion. `floor` the distance so the lip is exactly one pixel
bright and the shading behind it steps.

→ *Appendix A, Step 33*

## Step 34 — Wrapper sheen — Build

```hlsl
// Spec: a gaussian bar sliding across the wrapper as it tilts. Same shape as the
// card's sweep, so pack and cards read as one set.
//   - Plus a rainbow tint, blended in by _ShineTint.
//   - Foil only catches the light where the print is BRIGHT, so dark ink stays dark.
float3 PackShine(float2 uv, float2 tilt, float3 baseColor)
{
    // TODO
}
```

You have written all three pieces before: Step 10's bar, Step 8's spectrum, Step 14's luminance.

→ *Appendix A, Step 34*

## Step 35 — Wiring it up — Given

The `.shader` file is the card's structure with the pack's math. Properties:

```hlsl
[MainTexture] _MainTex("Pack Sheet", 2D) = "white" {}
[MainColor] _Tint("Tint", Color) = (1,1,1,1)
_PackRect("Sheet Cell (xy offset, zw size)", Vector) = (0,0,1,1)
_PackPixels("Pack Size In Pixels", Vector) = (84,154,0,0)
_Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
[Toggle(_PIXELAA)] _PixelAA("Anti Aliased Pixels", Float) = 1
_BackShade("Back Face Shade", Range(0,1)) = 0.45
_PeelMin("Peel Start (U)", Range(-0.5,1.5)) = 0.5
_PeelMax("Peel End (U)", Range(-0.5,1.5)) = 0.5
_PeelFeather("Peel Feather", Range(0.001,0.5)) = 0.09
_PeelFlat("Peel Flatten", Range(0,1)) = 0
_PeelLift("Peel Lift", Range(0,1)) = 0
_PeelCurl("Peel Curl", Range(0,4)) = 1.5
_TearEdge("Torn Edge Strength", Range(0,3)) = 1
_TearEdgeWidth("Torn Edge Width (pixels)", Range(1,16)) = 3
_TearEdgeColor("Torn Edge Color", Color) = (1,0.96,0.88,1)
_TearShade("Torn Edge Shade", Range(0,1)) = 0.4
_ShineStrength("Strength", Range(0,3)) = 0.4
_ShineColor("Color", Color) = (1,1,1,1)
_ShineWidth("Width", Range(0.02,2)) = 0.3
_ShineAngle("Angle", Range(0,6.2832)) = 1.15
_ShineTravel("Tilt Travel", Range(0,3)) = 1.1
_ShineOffset("Rest Offset", Range(-1,1)) = -0.35
_ShineTint("Rainbow Tint", Range(0,1)) = 0.35
```

The vertex stage takes `uv1 : TEXCOORD1` in `Attributes`, calls `PackPeelPosition` **before**
`GetVertexPositionInputs`, and passes `float2 tear = (uv1.x, shape)` through to the fragment stage.
The fragment stage, in order:

```hlsl
half4 base = SamplePack(PackAtlasUV(PackPixelUV(input.uv, max(_PackPixels.xy, 1.0))));
clip(base.a - _Cutoff);
float3 col = base.rgb * _Tint.rgb;

// tilt, exactly as the card does it (Step 6), against the flat normal
float3 vT = float3(dot(V, T), dot(V, B), dot(V, N));
float2 tilt = vT.xy / max(vT.z, 0.15);
tilt /= (1.0 + 0.35 * length(tilt));
col += PackShine(input.uv, tilt, col);

float lip, shade;
PackTornEdge(input.tear.x, saturate(input.tear.y), lip, shade);
col *= 1.0 - _TearShade * shade;
col += _TearEdgeColor.rgb * (_TearEdge * lip);

// The underside of a peeled flap is the INSIDE of the wrapper.
col *= facing > 0.0 ? 1.0 : (1.0 - _BackShade);
```

`#pragma target 3.0` is enough here — nothing samples a texture in the vertex stage. And the
`DepthOnly` pass must call `PackPeelPosition` too, or the depth silhouette stays flat while the flap
peels away from it.

---

# Part 7 — The atlas quad

The simplest of the three, and the one you will reuse most: one cell of a pixel-art sheet on a quad,
picked by UV rect rather than by slicing the sheet into sprites. That is what lets one material draw
every frame of an animation with a `MaterialPropertyBlock` stepping through them.

## Step 36 — Cell UV — Build

```hlsl
// CBUFFER: float4 _MainTex_ST, _Rect, _CellPixels, _Color, _HighlightColor;
//          float _Highlight, _Cutoff;
//
// Spec: cell uv (0..1 over one cell) -> atlas uv.
//   - _Rect maps cell onto atlas: xy = offset, zw = size.
//   - Snap to texel centres with the one-screen-pixel ramp (Step 3).
//   - CLAMP to the outer texel CENTRES of the cell, not to its edges.
float2 SheetAtlasUV(float2 cellUV)
{
    // TODO
}
```

**That last clamp is the whole lesson of this part.** Point filtering would not need it, but an atlas
does: if your art runs right up against the top and bottom of its cell, a sample exactly on the
boundary blends in the tip of whatever sits in the next row of the sheet. Clamping to `0.5` and
`cellPixels - 0.5` keeps every sample inside the cell's own outermost texels.

**No keywords in this shader at all.** Nothing but pixel art is ever drawn through it, so the filter
is always on and the two passes have nothing to keep in step beyond the alpha clip. Fewer variants,
one less way to be wrong.

→ *Appendix A, Step 36*

---

# Part 8 — Verifying the whole thing

## An offline preview renderer — Given

The fastest feedback loop you will have: renders the card through a camera to a PNG, in batch mode,
without a human pressing Play.

```csharp
using UnityEditor;
using UnityEngine;

namespace CardFx.EditorTools
{
    public static class CardPreviewRenderer
    {
        const int CardPx = 220, Pad = 16;
        static readonly string[] Tiers = { "Common", "Shiny", "Holo", "Galaxy", "Chrome" };
        static readonly float[] Angles = { 0f, 18f, 34f, 52f };

        [MenuItem("Tools/Card FX/Render Foil Preview")]
        public static void RenderPreview()
        {
            int w = CardPx * Tiers.Length + Pad * (Tiers.Length + 1);
            int h = Mathf.RoundToInt(CardPx * 1.55f) * Angles.Length + Pad * (Angles.Length + 1);

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var camGo = new GameObject("PreviewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.14f, 0.14f, 0.17f, 1f);
            cam.orthographic = false;
            cam.fieldOfView = 30f;
            cam.targetTexture = rt;

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Meshes/CardQuad.asset");
            var holder = new GameObject("PreviewCards");

            // ... instantiate one quad per (tier, angle), position it in a grid in
            // front of the camera, assign Card_<tier>.mat, rotate by the angle ...

            cam.Render();
            RenderTexture.active = rt;
            var png = new Texture2D(w, h, TextureFormat.RGB24, false);
            png.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            png.Apply();
            RenderTexture.active = null;

            string path = System.Environment.GetEnvironmentVariable("PREVIEW_PATH") ?? "CardFoilPreview.png";
            System.IO.File.WriteAllBytes(path, png.EncodeToPNG());
            Debug.Log($"[Card FX] Wrote {path}");

            Object.DestroyImmediate(holder);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(png);
            rt.Release();
        }
    }
}
```

Five tiers across, four view angles down, in one sheet. Because the foil is **view-dependent**, a
single screenshot tells you almost nothing — the four angles are the point.

Run it from the command line with the editor **closed** (it locks the project):

```bash
/Applications/Unity/Hub/Editor/6000.3.17f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "<your project>" -logFile - -executeMethod CardFx.EditorTools.CardPreviewRenderer.RenderPreview
```

Note: **no `-nographics`** — it renders through a camera. That flag is for jobs that never rasterize.

For wear, render **one card filling each frame**. Damage authored at 73×113 is invisible at the size
a five-card row leaves.

## Final checklist

- [ ] Frame Debugger says **SRP Batcher compatible** for all three shaders
- [ ] Every keyword is declared in **both** passes of its shader
- [ ] `CardApplyBend` and `CardApplyBendPosition` produce identical displacement
- [ ] Mesh bounds clear `MaxBow + MaxCornerBend + _DentDisplace`
- [ ] The wear map is created **linear**, Point, Clamp, no mips
- [ ] The artwork imports **sRGB**, Bilinear, mips on, coverage preserved
- [ ] Grep the foil code for `wear`: rainbow, sweep, sparkle and chrome must not mention it
- [ ] A card at `_WearAmount = 0` and `_DebugView = 0` samples no wear textures
- [ ] Zoomed out to ~30 px, the card does not sparkle with aliasing
- [ ] Turning for 10 seconds, no diagonal edge crawls

## Traps, collected

Every one of these is silent. When something is inexplicable, read this before you bisect.

1. A `[Toggle]` float set without its keyword. Total no-op, no warning.
2. A keyword declared in the forward pass but not in `DepthOnly`. Silhouette disagrees with colour.
3. A property outside `UnityPerMaterial`, or declared in a different order across shaders. Batching
   stops; nothing looks wrong until you profile.
4. `ddx`/`ddy` inside non-uniform control flow.
5. Sampling with the snapped uv's derivatives (Step 4). Only visible when minified.
6. A vertex texture fetch at `#pragma target 3.0`. Works on your desktop, fails on someone else's.
7. The two bend functions drifting apart. Z-fighting along the bend.
8. Mesh bounds not reserving bend headroom. Card pops out of view at the screen edge.
9. The wear map imported/created as sRGB. Every rate subtly wrong, nothing obviously broken.
10. Mips or Repeat wrap on the wear map. Border pixels grow dents they do not have.
11. `mipMapsPreserveCoverage` off. Cutout borders dissolve as the card shrinks.
12. Gamma colour space. The whole foil washes out and the card stock reads pink.
13. An orthographic camera. Every pixel gets the same view direction and the foil goes flat.
14. Face flipping by winding order with `Cull Off`.
15. A hash with visible structure, discovered three steps after you built on it.
16. Clamping the pack's `uv1.y` hinge ramp. Hairline cracks between columns, in perspective only.
17. `_ScuffHaze` added before `_FoilIntensity`. Common cards cannot be damaged.

---

# Appendix A — Answer key

Check against these **after** your version works. Where yours differs, the question is not "which
looks better" but **which is right, and why** — some differences are bugs, and at least one, if you
have been thinking, will be a defensible choice you made differently.

<details>
<summary><b>Step 2 — Both faces</b></summary>

```hlsl
float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));
float3 N = normalize(input.normalWS);
float3 T = normalize(input.tangentWS);
float3 B = normalize(input.bitangentWS);

// Geometric, so it is independent of triangle winding - the face whose normal
// points at the camera wins.
float facing = dot(N, V) >= 0.0 ? 1.0 : -1.0;
N *= facing;
T *= facing;

float2 uv = input.uv;
uv.x = facing > 0.0 ? uv.x : 1.0 - uv.x;

half4 front = SAMPLE_TEXTURE2D(_FrontTex, sampler_FrontTex, uv);
half4 back  = SAMPLE_TEXTURE2D(_BackTex,  sampler_FrontTex, uv);
half4 base  = facing > 0.0 ? front : back;
```

`B` is left alone because it is already `cross(N, T) * w` — negating both `N` and `T` leaves their
cross product unchanged, which is exactly the handedness you want. Negating `B` as well would flip
the frame's handedness and mirror the foil.

One sampler for both textures is deliberate: they are the same size with the same filtering, and a
second `SAMPLER` costs a sampler slot for nothing.
</details>

<details>
<summary><b>Step 3 — Pixel snapping</b></summary>

```hlsl
float2 CardPixelUV(float2 uv, float2 texSize)
{
#ifdef _PIXELAA
    float2 p = uv * texSize;
    float2 seam = floor(p + 0.5);
    float2 dudv = clamp(fwidth(p), 1e-5, 1.0);
    p = seam + clamp((p - seam) / dudv, -0.5, 0.5);
    return p / texSize;
#else
    return uv;
#endif
}
```

Line by line: `p` is position in texels. `seam` is the nearest texel centre. `dudv` is texels per
screen pixel. `(p - seam) / dudv` is the offset from that centre **measured in screen pixels**;
clamping it to ±0.5 collapses everything more than half a screen pixel from a seam onto the seam
itself, and leaves a linear ramp exactly one screen pixel wide across it. Bilinear filtering then
interpolates across that ramp and nowhere else.

The upper clamp of `1.0` on `dudv` matters when the card is heavily minified: without it the ramp
grows wider than a texel and the snap turns back into plain bilinear.
</details>

<details>
<summary><b>Step 4 — The mip trap</b></summary>

```hlsl
float2 sampleUV = CardPixelUV(uv, max(_CardPixels.xy, 1.0));
float2 ddxUV = ddx(uv);
float2 ddyUV = ddy(uv);
half4 front = SAMPLE_TEXTURE2D_GRAD(_FrontTex, sampler_FrontTex, sampleUV, ddxUV, ddyUV);
half4 back  = SAMPLE_TEXTURE2D_GRAD(_BackTex,  sampler_FrontTex, sampleUV, ddxUV, ddyUV);
half4 base  = facing > 0.0 ? front : back;
```

Sample position from the snapped uv, mip selection from the untouched one.
</details>

<details>
<summary><b>Step 5 — The depth pass</b></summary>

```hlsl
Pass
{
    Name "DepthOnly"
    Tags { "LightMode" = "DepthOnly" }

    ZWrite On
    ColorMask R
    Cull Off

    HLSLPROGRAM
    #pragma vertex DepthVert
    #pragma fragment DepthFrag
    #pragma target 3.5
    #pragma multi_compile_instancing
    #pragma shader_feature_local_fragment _ _PIXELAA

    #include "CardHoloInput.hlsl"

    struct DepthAttributes
    {
        float4 positionOS : POSITION;
        float2 uv         : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct DepthVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv         : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    DepthVaryings DepthVert(DepthAttributes input)
    {
        DepthVaryings output = (DepthVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = TRANSFORM_TEX(input.uv, _FrontTex);
        return output;
    }

    half4 DepthFrag(DepthVaryings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        float2 sampleUV = CardPixelUV(input.uv, max(_CardPixels.xy, 1.0));
        half alpha = SAMPLE_TEXTURE2D_GRAD(_FrontTex, sampler_FrontTex, sampleUV,
                                           ddx(input.uv), ddy(input.uv)).a;
        clip(alpha - _Cutoff);
        return 0;
    }
    ENDHLSL
}
```

Only the front texture's alpha is tested. The two faces of a card have the same silhouette, so
sampling the back as well would cost a fetch to reach the same answer.
</details>

<details>
<summary><b>Step 6 — Tilt</b></summary>

```hlsl
// nTS is (0,0,1) until Step 23 replaces it with the dented normal.
float3 nTS = float3(0.0, 0.0, 1.0);

float3 vT = float3(dot(V, T), dot(V, B), dot(V, N));
float ndv = dot(vT, nTS);
float2 tilt = (vT.xy - nTS.xy * ndv) / max(ndv, 0.15) * _TiltGain;
tilt /= (1.0 + 0.35 * length(tilt));
```

Written this way, `vT.xy - nTS.xy * ndv` is the component of the view direction **perpendicular to
the surface normal** — with a flat normal it reduces to plain `vT.xy`, which is why substituting a
per-pixel normal in Step 23 changes nothing structurally.

`max(ndv, 0.15)` stops the divide exploding; the soft saturation stops the *result* stretching without
bound once the divisor has bottomed out. Remove either and the other does not cover for it.
</details>

<details>
<summary><b>Step 8 — Spectrum</b></summary>

```hlsl
float3 CardSpectrum(float t)
{
    return saturate(0.5 + 0.5 * cos(6.28318530718 * (t + float3(0.0, 0.33, 0.67))));
}
```

Three cosines of one cycle, phase-offset by thirds, remapped from −1..1 to 0..1. `0.33` and `0.67`
rather than exact thirds is a hair of asymmetry that reads slightly warmer; exact thirds are equally
defensible.
</details>

<details>
<summary><b>Step 9 — Rainbow</b></summary>

```hlsl
float2 dirR = float2(cos(_RainbowAngle), sin(_RainbowAngle));
float2 rUV = hUV + tilt * _RainbowDepth;
float phase = dot(rUV - 0.5, dirR) * _RainbowScale
            + dot(tilt, dirR) * _RainbowTilt
            + _Time.y * _RainbowDrift;
float hue = frac(phase);
if (_RainbowSteps >= 1.0) hue = floor(hue * _RainbowSteps) / _RainbowSteps;
float3 rainbow = CardSpectrum(hue);
rainbow = lerp(dot(rainbow, float3(0.299, 0.587, 0.114)).xxx, rainbow, _RainbowSat);
```

Two separate uses of tilt, doing different jobs: `rUV` shifts **where** the pattern is sampled
(parallax — the bands appear to sit under the surface), while the second term shifts **what phase**
it is at (diffraction — the colour changes with viewing angle). Real holographic foil does both.

`_RainbowSat` desaturates toward the rainbow's own luminance rather than toward grey, so turning
saturation down darkens nothing.
</details>

<details>
<summary><b>Step 10 — Sweep</b></summary>

```hlsl
float2 dirS = float2(cos(_SweepAngle), sin(_SweepAngle));
float along = dot(hUV - 0.5, dirS);
float center = _SweepOffset + dot(tilt, dirS) * _SweepTravel;
float sweep = exp(-pow(abs(along - center) / max(_SweepWidth, 1e-3), 2.0) * 2.0);
```

`exp(-x²·2)` is a gaussian; `_SweepWidth` is its standard deviation in uv units, and the `2.0` sets
how fast the tails fall. `_SweepOffset = -0.4` parks the bar off the left edge at rest, so the card's
usable rotation range sweeps it all the way across.
</details>

<details>
<summary><b>Step 11 — Sparkle</b></summary>

```hlsl
float2 CardHash22(float2 p)
{
    float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float CardSparkles(float2 uv, float2 tilt, float density, float size, float spread, float seed)
{
    float2 g = uv * density + seed * 7.13;
    float2 cell = floor(g);
    float2 f = g - cell;

    float acc = 0.0;
    [unroll] for (int y = -1; y <= 1; y++)
    {
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float2 o = float2(x, y);
            float2 h = CardHash22(cell + o);
            float2 pt = o + 0.15 + h * 0.7;

            float d = length(f - pt);
            float shape = saturate(1.0 - d / max(size, 1e-3));
            shape *= shape;

            // Every flake gets a random preferred viewing angle, so it only fires
            // when the card is tilted towards it. That is what makes the sparkle
            // pop in and out while turning instead of just scrolling around.
            float2 pref = (CardHash22(cell + o + 19.19) - 0.5) * 2.0 * spread;
            float aim = saturate(1.0 - length(tilt - pref) * 1.5);

            acc = max(acc, shape * aim * aim);
        }
    }
    return acc;
}
```

`o + 0.15 + h * 0.7` keeps each flake inside the middle 70% of its cell, so a flake never sits exactly
on a boundary where two cells would both claim it.

`aim * aim` rather than `aim` sharpens the angular response — a flake that fades in linearly over
tilt reads as a moving smudge rather than a glint.

The two hash calls use offsets 19.19 apart so position and preferred angle are independent; reusing
one hash for both correlates them and every flake in a region fires at once.
</details>

<details>
<summary><b>Step 12 — Chrome</b></summary>

In `Frag`, before calling `CardFoil`:

```hlsl
float3 Nw = normalize(T * nTS.x + B * nTS.y + N * nTS.z);
float3 reflectDir = reflect(-V, Nw);
```

In `CardFoil`:

```hlsl
float3 env = lerp(_ChromeGround.rgb, _ChromeSky.rgb, saturate(reflectDir.y * 0.5 + 0.5));
float sun = pow(saturate(dot(reflectDir, normalize(_ChromeSunDir.xyz))), _ChromeSharp);
// Damped head-on so the mirror does not flatly wash out the artwork; it opens up
// as the card turns away, which is where chrome reads best anyway.
float chromeRamp = 0.3 + 0.7 * pow(saturate(1.0 - saturate(ndv)), 1.5);
float3 chrome = (env + _ChromeSun.rgb * sun) * chromeRamp;
```

`reflectDir.y` is the world-space up component, which is why the whole thing has to be computed in
world space. Doing it in tangent space gives you a sky that rotates with the card.

Building `Nw` from the tangent frame rather than using `N` directly is what makes chrome — and only
chrome — respond to dents in Step 23, since `nTS` is the dented normal by then.
</details>

<details>
<summary><b>Step 13 — Edge glow</b></summary>

```hlsl
float fresnel = pow(saturate(1.0 - saturate(ndv)), _FresnelPower);
```

Both `saturate`s earn their place: the inner one clamps `ndv` (which can go slightly negative on a
steeply bent card), the outer keeps the base of the `pow` non-negative — `pow` of a negative base is
undefined and returns NaN on some compilers.
</details>

<details>
<summary><b>Step 14 — Foil mask</b></summary>

```hlsl
float mask = 1.0;
#ifdef _MASKTEX
    mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_FrontTex, uv).r;
#endif
float luma = dot(baseColor, float3(0.299, 0.587, 0.114));
float lumaMask = saturate((luma + _MaskBias - 0.5) * _MaskContrast + 0.5);
mask *= lerp(1.0, lumaMask, _MaskFromLuma);
```

The `- 0.5 ... + 0.5` pair is a contrast remap about a **0.5 pivot**: mid-grey stays put and
everything else is pushed toward black or white. Without the pivot, raising contrast would also raise
brightness and the mask would open up rather than tighten.

`lerp(1.0, lumaMask, _MaskFromLuma)` is what makes `_MaskFromLuma = 0` exactly 1.0 — a `pow` or a
multiply would only approach it.
</details>

<details>
<summary><b>Step 15 — Compositing</b></summary>

In `CardFoil`:

```hlsl
    float2 hUV = uv;
#ifdef _HOLOPIXELATE
    hUV = (floor(uv * _CardPixels.xy) + 0.5) / max(_CardPixels.xy, 1.0);
#endif
    ...
    if (_ColorSteps >= 1.0) foil = floor(foil * _ColorSteps) / _ColorSteps;
    return max(foil, 0.0);
```

In `Frag`:

```hlsl
float3 col = base.rgb * _Tint.rgb;
float3 foil = CardFoil(uv, tilt, ndv, reflectDir, col);

float3 additive = col + foil;
float3 screen = 1.0 - (1.0 - saturate(col)) * (1.0 - saturate(foil));
col = lerp(additive, screen, _FoilBlend);
```

`floor(uv * pixels) + 0.5` lands on texel **centres** with no ramp — a hard snap, unlike Step 3.

`max(foil, 0.0)` at the end guards the composite: the scuff terms in Step 24 can be subtractive in
principle, and a negative foil turns the screen blend inside out.
</details>

<details>
<summary><b>Step 19 — Reading the map</b></summary>

```hlsl
float CardWearQuantize(float v)
{
    // Damage in visible steps is what makes a rub read as progress: a scratch goes
    // four texels, three, two, gone, instead of fading by an amount nobody can see.
    // floor, not round, so the last step actually clears the texel rather than
    // leaving a ghost of it.
    if (_WearSteps < 1.0) return v;
    return floor(saturate(v) * _WearSteps) / _WearSteps;
}

CardWear SampleCardWear(float2 snappedUV, float facing)
{
    CardWear wear = (CardWear)0;
    if (_WearAmount <= 0.0) return wear;

    float4 front = SAMPLE_TEXTURE2D_LOD(_WearTex,     sampler_WearTex, snappedUV, 0);
    float4 back  = SAMPLE_TEXTURE2D_LOD(_WearBackTex, sampler_WearTex, snappedUV, 0);
    float2 surface = facing > 0.0 ? front.rg : back.rg;

    wear.scuff   = CardWearQuantize(surface.r * _WearAmount);
    wear.inkLoss = CardWearQuantize(surface.g * _WearAmount);
    wear.height  = (front.b - 0.5) * 2.0 * _WearAmount;   // front only
    wear.missing = front.a * _WearAmount;                 // front only
    return wear;
}
```

Height is **not** quantised — it feeds a gradient in Step 22, and quantising it there would produce
flat plateaus with vertical walls between them, which is not what a dent looks like.
</details>

<details>
<summary><b>Step 20 — Ink loss</b></summary>

```hlsl
float3 CardApplyInkLoss(float3 col, float inkLoss)
{
    if (inkLoss <= 0.0) return col;
    float luma = dot(col, float3(0.299, 0.587, 0.114));
    col = lerp(col, luma.xxx, saturate(inkLoss * _InkLossDesat));
    return lerp(col, _StockColor.rgb, inkLoss);
}
```

Desaturate first, then fade to stock — the print goes flat before it goes pale. `_InkLossDesat = 1.2`
makes the desaturation run slightly ahead of the fade, so there is a window where the artwork is
grey-but-present, which is exactly how a worn card looks.
</details>

<details>
<summary><b>Step 21 — Chips</b></summary>

Forward pass:

```hlsl
CardWear wear = SampleCardWear(sampleUV, facing);
// A chip is material that is not there any more, so it leaves the card the same
// way a transparent texel does.
clip(base.a - wear.missing - _Cutoff);
```

Depth pass:

```hlsl
// Chips have to take the depth with them. Left in, a chipped pixel writes depth
// over the background the forward pass clipped through to, and the hole fills
// with whatever is behind the card.
half missing = 0.0;
if (_WearAmount > 0.0)
    missing = SAMPLE_TEXTURE2D_LOD(_WearTex, sampler_WearTex, sampleUV, 0).a * _WearAmount;
clip(alpha - missing - _Cutoff);
```

The depth pass reads the front map's alpha regardless of which face it is drawing — the same reason
`missing` is front-only in the first place.
</details>

<details>
<summary><b>Step 22 — Dent normal</b></summary>

```hlsl
float CardWearHeightAt(float2 uv)
{
    return (SAMPLE_TEXTURE2D_LOD(_WearTex, sampler_WearTex, uv, 0).b - 0.5) * 2.0;
}

float3 CardWearNormal(float2 rawUV, float facing)
{
    if (_WearAmount <= 0.0 || _DentDepth <= 0.0) return float3(0.0, 0.0, 1.0);

    float2 texel = 1.0 / max(_CardPixels.xy, 1.0);
    float hl = CardWearHeightAt(rawUV - float2(texel.x, 0.0));
    float hr = CardWearHeightAt(rawUV + float2(texel.x, 0.0));
    float hd = CardWearHeightAt(rawUV - float2(0.0, texel.y));
    float hu = CardWearHeightAt(rawUV + float2(0.0, texel.y));

    float2 slope = float2(hr - hl, hu - hd) * 0.5 * _DentDepth * _WearAmount;
    float3 n = normalize(float3(-slope, 1.0));
    // The back is the same surface read through the paper and with u mirrored: the
    // mirror flips the u slope, and looking from behind turns every dent into a
    // bump. Both land on the same negation of the tangent-space xy.
    n.xy *= facing;
    return n;
}
```

A central difference (`hr - hl`, halved) rather than a forward difference: it is symmetric, so a
symmetric dent produces a symmetric normal and the highlight sits in the middle of it rather than
half a texel off.
</details>

<details>
<summary><b>Step 23 — The payoff</b></summary>

```hlsl
float3 nTS = CardWearNormal(uv, facing);
```

That is the entire change. `nTS` was `float3(0,0,1)`; now it is the dented normal, and the Step 6
tilt maths already handles it. `Nw` and `reflectDir` follow for free.
</details>

<details>
<summary><b>Step 24 — Scuff</b></summary>

In `CardFoil`'s mask, after the luminance mask:

```hlsl
// Abraded foil is dead foil. This one line is the loudest damage cue the card has:
// every layer above is driven off the same mask, so a scuffed patch stops
// diffracting, stops sparkling and stops sweeping all at once.
mask *= saturate(1.0 - wear.scuff * _ScuffFoilLoss);
```

After the intensity multiply:

```hlsl
foil *= mask * _FoilIntensity;

// Added after the intensity, on purpose. Abrasion is not foil - a scuffed common
// card has no foil to lose but still has to show its scratches, and anything above
// this line is multiplied away to nothing on one.
if (wear.scuff > 0.0)
{
    foil += _ScuffHaze  * wear.scuff * (0.35 + sweep);
    foil += _ScuffGlint * wear.scuff * sweep * fresnel;
}
```

Both scuff terms reuse `sweep`, so a scratch catches the light as the bar passes over it — which is
what a scratch on a real card does, and it costs nothing extra.
</details>

<details>
<summary><b>Step 25 — Height field</b></summary>

```hlsl
float CardCornerBend(float2 c, float2 corner, float amount)
{
    if (amount == 0.0) return 0.0;
    // 0 at that corner, 1 at the far one.
    float2 d = (1.0 - corner * c) * 0.5;
    float w = saturate(1.0 - length(d) / max(_CornerRadius, 1e-3));
    return amount * w * w * (3.0 - 2.0 * w);
}

float CardBendHeight(float2 uv)
{
    float2 c = uv * 2.0 - 1.0;

    float h = _Bow.x * (1.0 - c.x * c.x) + _Bow.y * (1.0 - c.y * c.y);

    h += CardCornerBend(c, float2(-1.0, -1.0), _CornerBend.x);
    h += CardCornerBend(c, float2( 1.0, -1.0), _CornerBend.y);
    h += CardCornerBend(c, float2(-1.0,  1.0), _CornerBend.z);
    h += CardCornerBend(c, float2( 1.0,  1.0), _CornerBend.w);

    // Creases deep enough to move the paper rather than only shade it. Read with a
    // LINEAR filter, not the point one the fragment stage uses: this is the fold,
    // and a fold that stair-stepped across the mesh grid would show.
    if (_WearAmount > 0.0 && _DentDisplace != 0.0)
    {
        float mapped = SAMPLE_TEXTURE2D_LOD(_WearTex, sampler_LinearClamp, uv, 0).b;
        h += (mapped - 0.5) * 2.0 * _DentDisplace * _WearAmount;
    }
    return h;
}
```

`1 - c²` is the bow: zero at both edges (`c = ±1`), one in the middle, and smooth throughout.
</details>

<details>
<summary><b>Step 26 — Rebuilding the frame</b></summary>

```hlsl
void CardApplyBend(inout float3 positionOS, inout float3 normalOS,
                   inout float4 tangentOS, float2 uv)
{
    // Wide enough to stay well clear of the mesh grid, which the bend is smooth
    // across anyway - a tighter step would only sample the map's own noise.
    const float e = 0.02;
    float h0 = CardBendHeight(uv);
    float hu = CardBendHeight(uv + float2(e, 0.0));
    float hv = CardBendHeight(uv + float2(0.0, e));

    // The face points at -Z, so displacing along the normal moves it that way.
    positionOS.z -= h0;

    // dP/du = T*width + N*dh/du and dP/dv = B*height + N*dh/dv, whose cross product
    // comes out as N - T*(dh/du)/width - B*(dh/dv)/height.
    float2 k = (float2(hu, hv) - h0) / (e * max(_CardWorldSize.xy, 1e-4));
    float3 n = normalize(float3(-k, 1.0));
    normalOS = float3(n.x, n.y, -n.z);
    tangentOS = float4(normalize(float3(1.0, 0.0, -k.x)), tangentOS.w);
}

void CardApplyBendPosition(inout float3 positionOS, float2 uv)
{
    positionOS.z -= CardBendHeight(uv);
}
```

The `-n.z` when writing `normalOS` is the card's −Z convention: `n` is built in a +Z-up frame and has
to be handed back in the card's own.

`tangentOS.w` is preserved rather than recomputed — it is the bitangent's handedness sign from the
mesh, and it does not change under a bend.
</details>

<details>
<summary><b>Step 27 — Depth agreement</b></summary>

```hlsl
DepthVaryings DepthVert(DepthAttributes input)
{
    ...
    // Has to bend exactly as the forward pass does, or depth and colour disagree
    // about where the card is.
    float3 positionOS = input.positionOS.xyz;
    CardApplyBendPosition(positionOS, input.uv);
    output.positionCS = TransformObjectToHClip(positionOS);
    output.uv = TRANSFORM_TEX(input.uv, _FrontTex);
    return output;
}
```

And in `Vert`, the bend goes **first**:

```hlsl
float3 positionOS = input.positionOS.xyz;
float3 normalOS = input.normalOS;
float4 tangentOS = input.tangentOS;
CardApplyBend(positionOS, normalOS, tangentOS, input.uv);

VertexPositionInputs pos = GetVertexPositionInputs(positionOS);
VertexNormalInputs nrm = GetVertexNormalInputs(normalOS, tangentOS);
```
</details>

<details>
<summary><b>Step 28 — Debug views</b></summary>

```hlsl
#define CARD_DEBUG_UV       1.0
#define CARD_DEBUG_TEXELS   2.0
#define CARD_DEBUG_NORMAL   3.0
#define CARD_DEBUG_TILT     4.0
#define CARD_DEBUG_FACING   5.0
#define CARD_DEBUG_WEAR     6.0
#define CARD_DEBUG_HEIGHT   7.0
#define CARD_DEBUG_FOIL     8.0
#define CARD_DEBUG_DENSITY  9.0

float3 CardDebugColor(float2 uv, float2 sampleUV, float3 nTS, float2 tilt,
                      float ndv, CardWear wear, float3 foil, float facing)
{
    // Derivatives first, before any branching. They are only defined in uniform
    // control flow, and taking them at the top keeps that true whatever the
    // compiler decides to do with the ladder below.
    float2 texels = uv * max(_CardPixels.xy, 1.0);
    float density = max(length(ddx(texels)), length(ddy(texels)));

    float view = _DebugView;

    if (view < CARD_DEBUG_UV + 0.5) return float3(uv, 0.0);

    if (view < CARD_DEBUG_TEXELS + 0.5)
    {
        float2 cell = floor(texels);
        float check = fmod(cell.x + cell.y, 2.0);
        float ramp = saturate(length(sampleUV - uv) * max(_CardPixels.x, 1.0) * 2.0);
        return lerp(float3(0.16, 0.16, 0.2), float3(0.72, 0.72, 0.78), check) + float3(ramp, 0.0, 0.0);
    }

    if (view < CARD_DEBUG_NORMAL + 0.5) return nTS * 0.5 + 0.5;

    if (view < CARD_DEBUG_TILT + 0.5) return float3(tilt * 0.5 + 0.5, 0.5);

    if (view < CARD_DEBUG_FACING + 0.5)
        return saturate(ndv) * (facing > 0.0 ? float3(1.0, 1.0, 1.0) : float3(1.0, 0.65, 0.6));

    if (view < CARD_DEBUG_WEAR + 0.5) return float3(wear.scuff, wear.inkLoss, wear.missing);

    if (view < CARD_DEBUG_HEIGHT + 0.5)
    {
        float h = wear.height;
        return float3(saturate(h), 0.08 + 0.18 * (1.0 - abs(h)), saturate(-h));
    }

    if (view < CARD_DEBUG_FOIL + 0.5) return foil;

    // Texel density: how many card texels one screen pixel covers. This is the
    // number mip selection is made of, and the reason the artwork is sampled with
    // the UNTOUCHED derivatives rather than the snapped ones.
    float level = log2(max(density, 1e-4));
    return float3(saturate(level * 0.5), saturate(1.0 - abs(level) * 0.5), saturate(-level * 0.5));
}
```

The `< N + 0.5` ladder rather than `==` is because `_DebugView` is a float coming off a slider, and
exact float equality against a slider value is a coin toss.
</details>

<details>
<summary><b>Step 31 — Peel shape</b></summary>

```hlsl
float PackPeelShape(float u)
{
    float f = max(_PeelFeather, 1e-4);
    float s = smoothstep(_PeelMin, _PeelMin + f, u) * (1.0 - smoothstep(_PeelMax - f, _PeelMax, u));
    return saturate(lerp(s, 1.0, saturate(_PeelFlat)));
}
```
</details>

<details>
<summary><b>Step 32 — Peel position</b></summary>

```hlsl
float3 PackPeelPosition(float3 positionOS, float2 uv, float2 uv1, out float shape)
{
    shape = PackPeelShape(uv.x);
    float lift = _PeelLift * shape * uv1.y;
    positionOS.y += lift;
    positionOS.z -= lift * _PeelCurl;
    return positionOS;
}
```

Note `uv1.y` multiplying the lift: the hinge ramp is zero at the seam, so the flap pivots there
rather than sliding as a whole.
</details>

<details>
<summary><b>Step 33 — Torn edge</b></summary>

```hlsl
void PackTornEdge(float tearDist, float torn, out float lip, out float shade)
{
    float band = saturate(1.0 - floor(tearDist) / max(_TearEdgeWidth, 1.0));
    float onSeam = step(0.999, band);
    lip = onSeam * torn;
    shade = (band - onSeam) * torn;
}
```

`step(0.999, band)` isolates the single outermost pixel; subtracting it out of `band` leaves the
stepped shading behind it with no overlap, so the lip is never both bright and shaded.
</details>

<details>
<summary><b>Step 34 — Wrapper sheen</b></summary>

```hlsl
float3 PackSpectrum(float t)
{
    return saturate(0.5 + 0.5 * cos(6.28318530718 * (t + float3(0.0, 0.33, 0.67))));
}

float3 PackShine(float2 uv, float2 tilt, float3 baseColor)
{
    float2 dir = float2(cos(_ShineAngle), sin(_ShineAngle));
    float along = dot(uv - 0.5, dir);
    float center = _ShineOffset + dot(tilt, dir) * _ShineTravel;
    float bar = exp(-pow(abs(along - center) / max(_ShineWidth, 1e-3), 2.0) * 2.0);

    float3 tint = lerp(1.0.xxx, PackSpectrum(frac(along * 1.7 + center * 0.5)), _ShineTint);

    // Foil only catches the light where the print is bright, so dark ink stays dark.
    float luma = dot(baseColor, float3(0.299, 0.587, 0.114));
    return _ShineColor.rgb * tint * (_ShineStrength * bar * saturate(luma * 1.35));
}
```
</details>

<details>
<summary><b>Step 36 — Atlas cell</b></summary>

```hlsl
float2 SheetAtlasUV(float2 cellUV)
{
    float2 cellPixels = max(_CellPixels.xy, 1.0);
    float2 p = saturate(cellUV) * cellPixels;
    float2 seam = floor(p + 0.5);
    float2 dudv = clamp(fwidth(p), 1e-5, 1.0);
    p = clamp(seam + clamp((p - seam) / dudv, -0.5, 0.5), 0.5, cellPixels - 0.5);
    return _Rect.xy + (p / cellPixels) * _Rect.zw;
}

half4 SampleSheet(float2 atlasUV)
{
    // Mip 0 outright, like the pack shader: every one of these quads is magnified,
    // and a coarser mip would reach past the cell into whatever the sheet draws
    // next door.
    return SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, atlasUV, 0);
}
```

Step 3's filter with one extra `clamp(..., 0.5, cellPixels - 0.5)` — the outer texel centres of the
cell. That is the difference between an atlas and a standalone texture.
</details>

---

# Appendix B — Reading order if you get lost

The dependency chain, so you know what to fix first when several things look wrong at once:

```
import settings ──> Step 3 snapping ──> Step 4 mips
                                   │
mesh (0.4) ────────────────────────┼──> Step 26 bend ──> Step 27 depth
                                   │
Step 2 facing ──> Step 6 tilt ─────┴──> Steps 9-13 foil layers
                        ▲                        │
                        │                   Step 14 mask ──> Step 15 composite
                        │                        ▲
CardWear.cs (18) ──> Step 19 read ──> Step 22 normal
                          │                      │
                          ├──> Step 20 ink       └──> Step 23 (rewires tilt)
                          ├──> Step 21 chips
                          └──> Step 24 scuff ────────> mask
```

Read it upward from whatever is broken. If the foil is wrong **everywhere**, suspect Step 6 before
suspecting any of Steps 9–13. If it is wrong only where the card is damaged, suspect Step 22 or 23.
If the artwork itself is wrong, nothing below Step 4 matters yet.
