# Shaders, by way of one card

A shader tutorial that never leaves this project. Part 0 is what a shader *is* — the two programs,
what they are given, why the two of them are priced so differently. Parts 1–3 are this card's own
shader taken apart one layer at a time: the **foil**, which is five effects driven off a single
vector, and the **wear map**, which is four channels a tool rubs back out.

Nothing here is generic advice. Every rule is one this shader actually leans on, and every number is
the number in the file.

- `CARD_SYSTEM.md` is the **reference** — what each knob does.
- This is the **order to meet them in**.
- `Assets/Scenes/CardExplainer.unity` is the same 36 steps, live, with the card in front of you.

```bash
Tools > Cozy TGC > Build Explainer Scene
```

Then open the scene and press Play. It needs `Card.prefab` and the `Card_*` materials, so run
**Build Demo Scene** first in a fresh clone.

| Input | Action |
|---|---|
| `←` `→` | Previous / next step |
| `Tab` | Jump to the next chapter |
| `R` | Re-run the current step (re-ages the card) |
| `T` | Auto-turn on/off |
| `M` | Show/hide the wear map |
| `H` | Hide the panels |
| `1`–`6` | Pick a restoration tool |
| `0` | Put the tool down |
| `Space` | Hold to turn the card with a tool still in hand |
| Drag / click a card | Turn it / flip it |

**Two cards, always.** The left one is finished and never changes. The right one is the step's
subject, carrying exactly one layer or one kind of damage. Every step is a difference you can see
side by side.

The step numbers below are the step numbers in the scene.

---

# Part 0 — What a shader is

Every step in this part is **drawn on the card**. The scene has three overlay meshes and ten debug
views of the shader's own internals; the panel on the left switches between them.

| Overlay | What it is |
|---|---|
| Wireframe | the mesh's index list, drawn — 768 triangles over 425 shared corners |
| Vertices | one dot per vertex, so "425 runs" is something you can count |
| Tangent frames | T / B / N at 24 points, rebuilt by the bend |

| Debug view | Shows |
|---|---|
| UV | the interpolator: red = u, green = v |
| texels | the 73×113 grid, with the snapping ramp in red |
| normal | the tangent-space normal, dents included |
| tilt | the one vector the whole foil is driven by |
| N·V | how far off-axis this pixel is |
| wear | R scuff, G ink, B missing |
| height | signed: orange ridge, blue dent |
| foil | the foil with the artwork taken away |
| density | card texels per screen pixel — what mip selection is made of |

The overlays run the card's **own** `CardApplyBend`, and the debug views are handed the forward
pass's **own** intermediates. Neither recomputes its subject, so neither can drift from it.

## 1. A program that runs a lot

A shader is a small program that runs on the **GPU**. Not once — many times, in parallel, and you
never write the loop. You write the body; the hardware runs it once per vertex and once per covered
pixel.

| Stage | Runs | Decides |
|---|---|---|
| **Vertex** | once per point of the mesh | **where** things are |
| **Fragment** | once per pixel the triangles cover | **what colour** |

Between them sits the **rasteriser** — fixed hardware, not your code. It works out which pixels each
triangle covers and blends the vertex outputs across them.

A `.shader` file is a list of properties, some render state, and one or more *passes*; each pass names
a vertex function and a fragment function. In this project:

```
CardHolo.shader        properties, passes, render state, #pragma keyword declarations
CardHoloInput.hlsl     the CBUFFER, the samplers, and CardFoil() — the actual math
CardWear.hlsl          wear sampling, dent normals, bending
CardDebug.hlsl         the debug views this chapter draws
CardOverlay.shader     the wireframe / vertex / tangent-frame overlay
```

Splitting it that way is a convention, not a rule: `.hlsl` files are `#include`d verbatim. The reason
here is that `CardWear.hlsl` must be included *after* the CBUFFER and the texture declarations,
because everything in it reads material uniforms that have to stay in that one shared buffer.

## 2. The mesh

**A mesh is not a shape. It is arrays.**

| Array | Length | Contains |
|---|---|---|
| `vertices` | 425 | positions in object space |
| `uv` | 425 | texture coordinates |
| `normals` | 425 | directions |
| `tangents` | 425 | directions, plus a handedness in `w` |
| `triangles` | 2304 | **indices** into all of the above |
| `bounds` | — | 0.73 × 1.13 × 0.28, for culling |

Switch the wireframe on: those blue lines are the index list, drawn. 768 triangles sharing 425
corners — an inner grid point is used by six triangles and exists **once**. That sharing is the whole
reason meshes are indexed rather than a flat list of 2304 corners.

The card is a **16×24 grid**, not the four-vertex quad it started as, because a vertex shader can only
move vertices that exist. Step 6 bends it.

The wireframe is itself a real mesh, with `MeshTopology.Lines`, drawn by `CardOverlay.shader` — which
calls the *card's* bend function rather than a copy, so it can never describe a surface the card is no
longer on.

## 3. Vertices

One amber dot per vertex. The vertex program runs exactly once per dot; that is what the counter on
the left means, literally.

Everything a vertex carries is indexed by the same number: vertex 137 has a position, a uv, a normal
and a tangent — one entry in each of four arrays. Those arrays are exactly the struct the shader is
handed:

```hlsl
struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 uv         : TEXCOORD0;
};
```

Leave an array off the mesh and the shader reads zeros there. Add one (a second UV set, a colour) and
you declare it with the next semantic — the mesh and the struct are two ends of the same contract.
The overlay meshes use that: they carry `uv2` to say which axis a tangent-frame line is, and vertex
`COLOR` to say what colour to draw it.

## 4. UV: where you are on the card

The card is showing its own uv — red is u, green is v, both 0..1 across the face. Black is the
bottom-left corner, yellow the top-right.

UV is stored **per vertex** (425 values) and arrives **per pixel**, because the rasteriser
interpolates it across each triangle. That is the single most useful thing to understand about the
fragment stage: most of what it knows, it knows because three corners knew it and the hardware blended
between them.

Point at the card and watch the texture panel: it names the uv under the cursor and the artwork texel
it lands on. That mapping — a number between 0 and 1 to a pixel of a picture — is all a texture fetch
is.

## 5. Uniforms, and where they live

The fourth kind of input, after attributes, interpolators and textures. A **uniform** is per draw and
constant for every invocation: `_RainbowStrength`, `_WearAmount`, `_Bow`.

They all live in one buffer:

```hlsl
CBUFFER_START(UnityPerMaterial)
    float  _FoilIntensity;
    float  _RainbowStrength;
    ...
CBUFFER_END
```

Not tidiness. The **SRP Batcher** requires it, and a property declared outside that buffer silently
stops the whole shader batching.

Two cards, one material, different values: that is a **`MaterialPropertyBlock`**, which belongs to a
renderer rather than to a material. It is how the artwork, the sparkle seed and the two wear maps get
onto each card without instancing a material per card. What it *cannot* do is switch a keyword — see
step 10.

A material is a shader plus a set of uniforms. That is the whole of it, which is why rarity in this
project is not code: `Card_Common` … `Card_Chrome` are one shader with different numbers, and
`Card_Common` is simply `_FoilIntensity = 0`.

## 6. The vertex stage

The card is bowed, and the wireframe and the dots are bowed with it — because they run the **same**
`CardApplyBend`, out of the same file.

```hlsl
Varyings Vert(Attributes input)
{
    float3 positionOS = input.positionOS.xyz;
    CardApplyBend(positionOS, normalOS, tangentOS, input.uv);   // bend FIRST

    VertexPositionInputs pos = GetVertexPositionInputs(positionOS);
    output.positionCS = pos.positionCS;
    ...
}
```

This is the only place geometry can change, so anything that alters the **silhouette** lives here —
the bow, the folded corner, creases deep enough to move paper. The dent that merely *catches* the
foil does not: that is a per-pixel normal and needs no geometry at all.

The bend is applied **before** the tangent frame is built, so `GetVertexNormalInputs` returns the
*bent* card's frame. Do it after, and the foil lights the card as though it were still flat.

Two traps this makes concrete, both visible if you drag the severity slider:

- `#pragma target` is **3.5**, not 3.0, because this vertex stage *samples a texture* — it reads
  crease depth out of the wear map — and vertex texture fetch is only guaranteed from that tier up.
- The mesh **bounds** reserve 0.14 units for the displacement. Culling reads bounds and runs *before*
  your vertex shader, so a bowed card culled against a flat slab pops out of view at the screen edge
  and nothing in the shader can save it.

## 7. The tangent frame

Three axes at 24 points across the card:

| | Axis | Along |
|---|---|---|
| **red** | T | +u of the artwork |
| **green** | B | +v |
| **blue** | N | out of the face |

That is **tangent space** — the card's own frame. Working in it means the foil does not care where the
card is in the world or how it is rotated, only how it is turned *relative to you*. Every layer in
Part 1 is computed there.

Bow the card and watch the blue axes fan out. They are not drawn from stored normals: `CardApplyBend`
rebuilds the frame from **three evaluations of the displacement**, and the gizmo asks the same
function.

```hlsl
float h0 = CardBendHeight(uv);
float hu = CardBendHeight(uv + float2(e, 0.0));
float hv = CardBendHeight(uv + float2(0.0, e));
float2 k = (float2(hu, hv) - h0) / (e * _CardWorldSize.xy);
float3 n = normalize(float3(-k, 1.0));
```

Finite differences: slower than the analytic derivative, and impossible to get out of step with the
displacement when someone changes it. That trade is usually right in a vertex stage, where you have
runs to spare.

`tangent.w` is the **handedness**: `B = cross(N, T) * w`. It is `-1` here so B lines up with +v of the
UVs rather than against it — get it wrong and every normal map on the object is mirrored.

## 8. The fragment stage

Now the other program. It runs once per covered pixel — the counter shows both, and the ratio is
around **1 : 500** at a normal size.

The card is showing its texel grid: the 73×113 cells the artwork is stored in, with the snapping ramp
in red. Every one of those cells is many fragments at this size, and every fragment ran the whole
foil.

> A line moved from the fragment stage to the vertex stage gets some hundreds of times cheaper.
> Moved the other way, some hundreds of times dearer.

It also explains a rule that looks odd coming from the CPU: **branches**.

- A branch every invocation takes the same way is **free** — the hardware skips the block whole.
- A branch that differs pixel to pixel is **not**, because neighbouring pixels run in lockstep and
  both sides get executed.

That is why this shader has no keyword guarding the wear code:

```hlsl
// No keyword gates the wear. _WearAmount at 0 is a uniform branch the GPU
// skips whole, which costs a pristine card nothing, and a keyword here
// would be one more thing to keep in step across the two passes.
```

The debug views you are switching between are the same trick — one uniform branch at the end of the
pass, and a card with `_DebugView` at 0 pays nothing for any of them.

## 9. Textures and samplers

A texture is the data. The **sampler** is how it is read: filtering, mip selection, and what happens
outside 0..1. Two opposite settings in this one shader, both deliberate:

| | Filter | Wrap | Colour space | Why |
|---|---|---|---|---|
| Artwork | bilinear | — | sRGB | a rotating quad, point-filtered, crawls |
| Wear map | point | clamp | **linear** | it is *data*; a gamma curve on it would bend every rate the tools rub at |

The card is showing texel **density** — how many card texels one screen pixel covers. Move the camera
or turn the card and watch it change. That number is what mip selection is made of, and it is why the
artwork is sampled with the *untouched* derivatives:

```hlsl
half4 front = SAMPLE_TEXTURE2D_GRAD(_FrontTex, sampler_FrontTex, sampleUV, ddx(uv), ddy(uv));
```

`CardPixelUV`'s snapped uv jumps at every texel seam. Handed to the hardware it reads as an enormous
density, picks the smallest mip, and collapses the card to a flat colour at distance.

Every read of the wear map is an explicit `SAMPLE_TEXTURE2D_LOD(..., 0)` for the same family of
reason: it has no mip chain, level 0 is the only one there is, and asking outright keeps sampling out
of the compiler's hands inside a branch.

## 10. Passes, and variants

One object can be drawn several times a frame by different programs. A **pass** is one of those. This
shader has two:

| Pass | LightMode | Does |
|---|---|---|
| `CardForward` | `UniversalForward` | the colour you see |
| `DepthOnly` | `DepthOnly` | depth only — `ColorMask R`, no foil math at all |

They must agree about two things or the card comes apart:

1. **Where it is.** `CardApplyBendPosition` (DepthOnly) mirrors `CardApplyBend` (forward) exactly. If
   they drift, depth and colour disagree about where the card is.
2. **Which pixels are thrown away.** The chip `clip` is repeated in both. Leave it out of DepthOnly
   and a chipped pixel writes depth over the background the forward pass clipped through to — the
   hole fills with whatever is behind the card.

**Keywords** are the other half of a pass:

```hlsl
#pragma shader_feature_local_fragment _ _PIXELAA
```

compiles **two programs**, and the material picks one. It is a compile-time `#ifdef`, not a runtime
`if` — which is why:

- setting the `_PixelAA` float without calling `EnableKeyword` changes **nothing at all**
  (see `CardDemoBuilder.SetToggle`, which always does both);
- the pragmas have to be repeated in **every pass** that needs them;
- step 12 switches between two whole *materials*, because a property block cannot switch a keyword.

`shader_feature` strips unused variants from the build; `multi_compile` keeps them all. `_local` keeps
the keyword out of the global keyword budget. Use `shader_feature_local` unless something sets the
keyword from script at runtime.

---

# Part 1 — The foil

Files: [`CardHolo.shader`](Assets/Shaders/CardHolo.shader),
[`CardHoloInput.hlsl`](Assets/Shaders/CardHoloInput.hlsl).

## 11. The flat print

Artwork on a quad that can bend. Two conventions everything else depends on:

- The mesh faces **-Z**, matching Unity's built-in Quad, and the camera sits on -Z unrotated. The
  visible side is `-transform.forward`. Backwards, and the artwork mirrors.
- The **root never rotates**. A `Visual` child does all the tilting, and the pointer is projected onto
  a plane taken from the root — so the tilt cannot feed back into the pointer and shake the card.
  Breaking this produces jitter that looks like a smoothing bug.

Which face you are looking at is decided **geometrically**, not by triangle winding:

```hlsl
float facing = dot(N, V) >= 0.0 ? 1.0 : -1.0;
N *= facing;
T *= facing;
uv.x = facing > 0.0 ? uv.x : 1.0 - uv.x;   // the back is mirrored
```

`Cull Off`, so one draw gets both sides. That `uv.x` mirror is why `CardWear.Rub` has to mirror the
pointer too when the card shows its back — otherwise the tool works the wrong half.

## 12. Pixel snapping

Card textures are imported **bilinear**, not point: these are rotating 3D quads and point filtering
crawls at an angle. `CardPixelUV` puts the crispness back — it snaps the UV to texel centres but
keeps a one-screen-pixel ramp on the seams:

```hlsl
float2 p    = uv * texSize;
float2 seam = floor(p + 0.5);
float2 dudv = clamp(fwidth(p), 1e-5, 1.0);
p = seam + clamp((p - seam) / dudv, -0.5, 0.5);
return p / texSize;
```

`fwidth` is a **derivative** — how much this value changes between neighbouring pixels. Available in
the fragment stage only, because that is the only stage that runs pixels in blocks and can compare
them. It is what makes the ramp exactly one screen pixel wide at any distance and any angle.

Because that UV is deliberately **discontinuous**, the artwork is sampled with
`SAMPLE_TEXTURE2D_GRAD` using the *untouched* derivatives:

```hlsl
half4 front = SAMPLE_TEXTURE2D_GRAD(_FrontTex, sampler_FrontTex, sampleUV, ddx(uv), ddy(uv));
```

Hand the snapped UV to the hardware and mip selection breaks — it sees a huge jump at every seam,
picks the smallest mip, and the card collapses to a flat colour at distance.

## 13. One vector: tilt

Everything below is driven by one pair of numbers, computed per pixel:

```hlsl
float3 vT   = float3(dot(V,T), dot(V,B), dot(V,N));   // view dir in tangent space
float2 tilt = vT.xy / max(vT.z, 0.15) * _TiltGain;    // 0 facing you, grows when turned
tilt /= (1.0 + 0.35 * length(tilt));                  // and stays finite
```

**Tangent space** is the card's own frame: `T` along +U of the artwork, `B` along +V, `N` out of the
face. Working there means the foil does not care where the card is in the world or how it is rotated
— only how it is turned *relative to you*.

The `0.15` floor keeps the divide from blowing up at grazing angles. This single vector is why the
whole effect reacts to rotation — and, later, why a dent breaks all five layers at once: the dent
perturbs `N`, and everything is measured against `N`.

## 14. Layer 1 — rainbow diffraction

A grating phase built out of the UV *and* the tilt:

```hlsl
phase = dot(uv - 0.5, dir) * _RainbowScale
      + dot(tilt,     dir) * _RainbowTilt
      + _Time.y            * _RainbowDrift;
```

`hue = frac(phase)`, posterised by `_RainbowSteps`, coloured by a three-phase cosine spectrum:

```hlsl
saturate(0.5 + 0.5 * cos(6.28318 * (t + float3(0.0, 0.33, 0.67))));
```

Three cosines a third of a cycle apart is a full spectrum in one instruction and no lookup texture —
a good example of what shader code prefers: cheap arithmetic over memory.

`_RainbowDepth` parallax-shifts the UV by the tilt first, which is what makes the bands sit *under*
the artwork instead of looking painted on top of it.

## 15. Layer 2 — sweep

One gaussian bar along `_SweepAngle`, centred at `_SweepOffset + dot(tilt, dir) * _SweepTravel`. The
rest offset parks it near an edge when the card is still, so turning the card walks it across the face
rather than wobbling it about the middle. The rainbow is multiplied by `(0.35 + sweep)`, so the bands
brighten under the bar instead of the two ignoring each other.

## 16. Layer 3 — sparkle

Hash flakes on a grid of `_SparkleDensity` cells, tested over the 3×3 neighbourhood so a flake is not
clipped at a cell border. Cell noise like this is the standard way to get "many small things" without
a texture or a buffer: `floor(uv * density)` names the cell, a hash turns that name into a position,
and you only ever look at the nine cells that could reach this pixel.

The trick is **per-flake aim**: each flake gets a random *preferred* tilt and only fires when the card
is turned towards it, so glitter pops in and out while you turn instead of scrolling across the face.

`_SparkleSeed` is per card (`CardView.Awake`) — two cards side by side must not glitter in step. It
rides on the property block, which is exactly what property blocks are for.

## 17. Layer 4 — chrome

No cubemap and no reflection probe. Sky/ground lerped by the reflection vector's `y`, plus a `pow()`
sun:

```hlsl
float3 env = lerp(_ChromeGround.rgb, _ChromeSky.rgb, saturate(reflectDir.y * 0.5 + 0.5));
float  sun = pow(saturate(dot(reflectDir, normalize(_ChromeSunDir.xyz))), _ChromeSharp);
```

Damped head-on by `0.3 + 0.7 * (1 - ndv)^1.5`, so a mirror finish does not wash out the artwork when
the card faces you; it opens up as the card turns away, which is where chrome reads best.

`reflectDir` is taken against the **wear-perturbed** normal, which is what makes this one of the
layers a dent breaks.

## 18. Layer 5 — edge glow

`pow(saturate(1 - ndv), _FresnelPower)`. The one layer that does not care *which* way the card is
turned, only how far off-axis it is. It is what stops a turned card looking like a flat sticker.

## 19. Where it is allowed to foil

All five layers are summed and multiplied by **one** mask:

```hlsl
mask  = (_MaskTex.r if _MASKTEX)
      * lerp(1, luminance ramp, _MaskFromLuma)
      * saturate(1 - wear.scuff * _ScuffFoilLoss);
```

With no mask texture the artwork's own luminance decides — bright foils, dark stays matte, shaped by
`_MaskContrast` / `_MaskBias`. One mask for all five layers is what makes damage read later: a
scuffed patch stops diffracting, sparkling and sweeping **together**, in one line of shader.

Then, deliberately, *after* `_FoilIntensity`:

```hlsl
if (wear.scuff > 0.0)
{
    foil += _ScuffHaze  * wear.scuff * (0.35 + sweep);
    foil += _ScuffGlint * wear.scuff * sweep * fresnel;
}
```

Order matters because a scuffed **common** card has no foil to lose but still has to show its
scratches. Anything above that line is multiplied away to nothing on `_FoilIntensity = 0`.

## 20. Keeping it pixel art

Three quantisers: `_HOLOPIXELATE` snaps the foil UVs to the 73×113 grid, `_RainbowSteps` posterises
the hue, `_ColorSteps` posterises the finished foil. Without them the foil is a smooth gradient over
pixel art and the eye reads two pictures at once.

The foil is mixed into the base by `_FoilBlend` — 0 additive, 1 screen:

```hlsl
float3 additive = col + foil;
float3 screen   = 1.0 - (1.0 - saturate(col)) * (1.0 - saturate(foil));
col = lerp(additive, screen, _FoilBlend);
```

**Rarity tiers are nothing but materials** with these numbers set; `Card_Common` is
`_FoilIntensity = 0` and that is the only difference.

---

# Part 2 — The damage

Files: [`CardWear.cs`](Assets/Scripts/Runtime/CardWear.cs),
[`CardWear.hlsl`](Assets/Shaders/CardWear.hlsl),
[`CardCondition.cs`](Assets/Scripts/Runtime/CardCondition.cs).

## 21. One map, four channels

Five kinds of damage, **one RGBA texture per face** at the card's own 73×113. `CardWear` is the only
thing that writes it.

| | Channel | What it is |
|---|---|---|
| R | scuff | abrasion, scratches, the matte that kills foil |
| G | ink loss | print coming off |
| B | height | 0.5 flat, below a dent, above a ridge |
| A | missing | material that is not there any more |

Packing four unrelated things into one RGBA texture is ordinary shader practice, for an ordinary
reason: a texture *fetch* costs far more than the arithmetic around it, so four channels read in one
tap beat four textures read in four.

R and G are **per face**, so a card has to be restored on both sides. B and A always come from the
**front** map: a dent goes through the paper, a chip is gone from both sides — and `DepthOnly` only
ever samples the front, so a chip it could not see would write depth over a pixel the forward pass
had already clipped through.

The map lives on the **CPU** as floats. It is 8249 texels: a stroke touches a few hundred, the whole
thing uploads in a fraction of a frame, and generation, grading and saving all stay plain C# with no
readback to wait on and no ping-pong buffer to keep in step. It is created **linear, point, clamp** —
it is data, and a gamma curve on it would bend every rate the tools rub at.

Every read of it is an explicit `SAMPLE_TEXTURE2D_LOD(..., 0)`. The map has no mip chain, so level 0
is the only one there is, and asking outright keeps sampling out of the compiler's hands — these all
sit inside a branch on `_WearAmount`, which is where an implicit gradient is least welcome.

## 22. Ageing 1 — the edges

Edges first, and hardest. A card is handled by its border, pulled in and out of a sleeve by it and
squared against a table on it, so the print goes there long before anything happens in the middle.
Weight is `(1 - distance to nearest edge / 5)²`; corners take it twice over, being the border of two
edges. Value noise on top, seeded apart per face, so the band is grained rather than a clean vignette.

## 23. Ageing 2 — scratches

Walked lines, up to 16 at severity 1, fading along their length the way a dragged edge lifts off.
About a quarter are two texels wide; 60% land on the front.

A scratch writes scuff **and** ink. Scuff alone only kills the foil, which leaves a scratch across a
dark common card invisible. Damage stamps with `max()`, not `+=`, so two crossing scratches do not add
up into a hole — what a *tool* leaves behind is the one thing that accumulates.

## 24. Ageing 3 — dents and creases

The B channel, signed around 0.5. A dent is a squared falloff; a crease is a Mexican hat,
`(1 - d²)·exp(-d²)` — the fold itself with the paper standing up either side of it. The crest loses
ink too: print cracks off a fold before anything else gives.

**This is the channel that earns its keep**, and it is the tutorial's central idea. Four neighbour
taps of B become a tangent-space normal:

```hlsl
float2 slope = float2(hr - hl, hu - hd) * 0.5 * _DentDepth * _WearAmount;
float3 n = normalize(float3(-slope, 1.0));
n.xy *= facing;
```

That is a **normal map built at runtime out of a height field** — the standard trick, four taps and a
cross product's worth of algebra. And because every foil layer is measured against `N`, a crease
breaks the rainbow, the sweep, the sparkle aim and the chrome *at once*, without one line of any of
them knowing that wear exists.

That is worth stating as a principle: **perturb the input every effect shares, rather than teaching
every effect about the new thing.** Five layers stayed unchanged when damage was added.

The normals are one per card pixel and deliberately blocky. A smooth bump map over pixel art reads as
a different card.

## 25. Ageing 4 — chips

The A channel, and the only damage that changes the outline:

```hlsl
clip(base.a - wear.missing - _Cutoff);
```

`clip()` discards the pixel outright — no blending, no sorting, which is why the shader is
`TransparentCutout` in the `AlphaTest` queue rather than transparent. Cheap and order-independent;
the cost is that it is all-or-nothing per pixel, which suits pixel art exactly.

In **both** passes. Left out of `DepthOnly`, a chipped pixel writes depth over the background the
forward pass clipped through to, and the hole fills with whatever is behind the card.

Chips are centred **on** the border rather than inside it, so the bite opens into the edge instead of
turning up as a pinprick in the middle of the frame.

## 26. Ageing 5 — bending

Not in the map. Three scalars — `_Bow.xy` and `_CornerBend` per corner — applied in the **vertex
shader**, because a bow has to change the silhouette and no amount of shading will do that.

The normal is rebuilt from **three evaluations of the displacement** rather than a hand-derived
gradient:

```hlsl
float h0 = CardBendHeight(uv);
float hu = CardBendHeight(uv + float2(e, 0.0));
float hv = CardBendHeight(uv + float2(0.0, e));
float2 k = (float2(hu, hv) - h0) / (e * _CardWorldSize.xy);
float3 n = normalize(float3(-k, 1.0));
```

Finite differences: slower than the analytic derivative, and impossible to get out of step with the
displacement when someone changes it. That trade is usually right in a vertex shader, where you have
runs to spare.

Two traps this makes concrete:

- The mesh **bounds** reserve `MeshBendHeadroom` (0.14 units) for the displacement. Bounds are what
  culling uses, and culling happens *before* your vertex shader — a bowed card culled against a flat
  slab pops out of view at the screen edge, and nothing in the shader can save it.
- The crease displacement is read with a **linear** filter (`sampler_LinearClamp`) while the fragment
  stage uses the point one. A fold that stair-stepped across the mesh grid would show.

## 27. Reading it back as a grade

`CardCondition` averages each channel over the card, then divides by what a severity-1 card
**actually** averages: `ScuffFull` 0.065, `InkFull` 0.090, `DentFull` 0.038, `MissingFull` 0.005.

Those are measured, not guessed. The raw means are tiny because damage concentrates — a chipped
corner is sixty texels out of eight thousand — and scored against 1, every card in the game grades
Mint.

```
score = 1 - (scuff·0.28 + ink·0.24 + missing·0.22 + dent·0.16 + bend·0.10)
```

Then a **stepped** price multiplier, 0.2× at Poor up to 1.6× at Mint. Stepped on purpose: a collector
pays for the label, so the one rub that tips Excellent into Near Mint has to be worth something
visible.

Change how ageing works and those four constants have to be re-measured, or the whole game grades
Mint again. `Tools > Cozy TGC > Render Wear Preview` logs the ramp they come from:

| | 0.00 | 0.30 | 0.55 | 0.85 | 1.00 |
|---|---|---|---|---|---|
| aged | Mint | Near Mint | Good | Played | Poor |

---

# Part 3 — Restoring

File: [`RestorationTool.cs`](Assets/Scripts/Runtime/RestorationTool.cs).

This part is not shader code at all — it is the CPU half, and it is here because the map is the
interface between the two. The shader only ever *reads* those four channels; everything below writes
them, and neither half knows anything else about the other.

## 28. The bench

`CardWear.Rub` is the **only edit path there is**. `Age` stamps damage into the map, a tool rubs it
back out — the same system read in two directions, which is what keeps them one thing rather than two
that have to agree.

A `RestorationTool` is nothing but rates: what it takes off, how deep it reaches (`scuffFloor`,
`inkFloor`), and what it puts back on in exchange. The exchange is the point — a tool that only ever
improved the card would reduce restoring one to holding the button down.

The six steps below are in the order the rates reward. **Nothing enforces it.**

## 29. Press

```
wholeCard              where the pointer sits does not matter
bendRate    0.35 /s    bow and every folded corner towards flat
flattenRate 0.15 /s    dents, gently, on the way past
```

Flatten first. Everything after this is a spot tool, and working a spot on a bowed card is working it
at the wrong angle. It is the one tool in the set that costs nothing at all.

## 30. Burnishing Bone

```
flattenRate   0.8  /s   rolls dents and creases flat
scuffCost     0.1  /s   while it works
overworkScuff 0.03 /s   where there is nothing left to flatten
```

Burnishing is polishing and polishing is abrasion, so it leaves its own marks. Fine here, because the
pad comes next and takes scuff off. Do it *after* the pad and you have just undone the pad.

## 31. Abrasive Pad

```
scuffRate 0.9 /s, scuffFloor 0    takes scratches all the way out
inkCost   0.12 /s                 lifts print while it does
overworkScuff 0.1 /s              second worst in the set
```

The cloth cannot do this — its `scuffFloor` is 0.4, so a real scratch stops it dead. This is the only
tool that reaches the bottom.

`inkCost` is a **cost, not a ratchet**. Above about 0.15 the pad would take off more print than the
pen can lay back, no order of tools would improve a card, and that reads to a player as the game being
broken rather than as a trade-off they are getting wrong.

## 32. Paper Fill

```
fillRate 0.5 /s     puts a chipped edge back
inkCost  0.8 /s     highest in the set — the fill arrives blank
radius   0.05       smallest in the set
```

**Dwell it on the chip.** Sweeping it round the whole border spends its time where there is nothing
to fill, and every texel with nothing left to do is charged `overworkScuff` instead. This is why chips
barely move under a careless pass, and why a chipped card stays cheap more or less forever — the
intended outcome, not a gap in the tools.

## 33. Touch-Up Pen

```
inkRate 1.1 /s, inkFloor 0    lays colour back in
overworkScuff 0.3 /s          by far the worst in the set
```

After the pad, never before: go the other way and the pad takes off the ink the pen just laid. Keep
going past done and it beads up. Sweeping a pen over the whole face is the single worst thing you can
do to a card.

## 34. Soft Cloth

```
scuffRate 0.55 /s, scuffFloor 0.4    lifts haze and dust, and no more
overworkScuff 0.02 /s                lightest touch in the set
```

Last, and for one job: polishing away the scuff the burnisher and the pen left behind. It cannot reach
a real scratch, so reaching for it first feels like it is working and achieves nothing.

## 35. Overworking

Every tool charges `overworkScuff` on any texel where it has **nothing left to do**. Nothing left to
fix means you are no longer restoring the card, you are just rubbing it — and rubbing a card is how it
got like this.

That single rule is what orders the six steps without one check enforcing the order. Change a rate and
you change the puzzle; there is no sequence table to keep in step with it.

It is also why a full clumsy pass with everything takes an 0.85 card from **Played** back to **Good**
and no further. Dents and bending come out completely, scuff and ink only partly, chips hardly at all.

## 36. What gets saved

Never the map. A `CardWear.State` is the **seed**, the **severity**, which kinds were stamped, and the
**stroke list**. Generation and rubbing are both deterministic, so replaying them lands on exactly the
same texels — a card worked on for an hour saves in a few kilobytes instead of the map's sixty-six.

A stroke stores uv, which face, how long, and the tool's **rates**. The name and the blurb are dropped
on the way in, because `Stamp` never reads anything else off the tool — they are for the UI, not for
the replay.

---

# Appendix A — Reading the fragment shader top to bottom

The whole of `Frag`, in order, with what each group is for. This is the shape most fragment shaders
have: rebuild the surface, sample, decide whether the pixel exists, shade.

```hlsl
// 1. Rebuild the frame. Interpolated vectors arrive un-normalised, because
//    interpolating unit vectors does not give you a unit vector.
float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));
float3 N = normalize(input.normalWS);
float3 T = normalize(input.tangentWS);
float3 B = normalize(input.bitangentWS);

// 2. Which side am I? Geometric, so triangle winding does not decide it.
float facing = dot(N, V) >= 0.0 ? 1.0 : -1.0;
N *= facing; T *= facing;
uv.x = facing > 0.0 ? uv.x : 1.0 - uv.x;

// 3. Sample the artwork through the snapped UV, with untouched derivatives.
float2 sampleUV = CardPixelUV(uv, max(_CardPixels.xy, 1.0));
half4 base = facing > 0.0 ? front : back;

// 4. Does this pixel exist at all? Everything below is wasted if not, so the
//    clip goes as early as the data allows.
CardWear wear = SampleCardWear(sampleUV, facing);
clip(base.a - wear.missing - _Cutoff);

// 5. Perturb the surface. One line; five effects change behaviour.
float3 nTS = CardWearNormal(uv, facing);

// 6. Derive everything view-dependent from the perturbed surface.
float3 vT = float3(dot(V, T), dot(V, B), dot(V, N));
float  ndv = dot(vT, nTS);
float2 tilt = (vT.xy - nTS.xy * ndv) / max(ndv, 0.15) * _TiltGain;
float3 Nw = normalize(T * nTS.x + B * nTS.y + N * nTS.z);
float3 reflectDir = reflect(-V, Nw);

// 7. Shade: base colour, damage to it, then the foil on top.
float3 col  = CardApplyInkLoss(base.rgb * _Tint.rgb, wear.inkLoss);
float3 foil = CardFoil(uv, tilt, ndv, reflectDir, col, wear);
col = lerp(col + foil, screen(col, foil), _FoilBlend);
```

Two habits worth stealing from step 6: it falls back to **exactly** the old flat-card maths when
`nTS` is `(0,0,1)`, so adding dents could not regress an undamaged card; and the perturbed normal is
computed once and shared, rather than each layer perturbing itself.

# Appendix B — Traps this shader actually hit

| Symptom | Cause |
|---|---|
| Property does nothing | Keyword not enabled. Set the float **and** `EnableKeyword` — `CardDemoBuilder.SetToggle` |
| Card goes flat-coloured at distance | Snapped UV fed to hardware mip selection. Use `SAMPLE_TEXTURE2D_GRAD` with the untouched derivatives |
| Chips fill with background | `clip` missing from `DepthOnly` — it only samples the front map |
| Bowed card pops out of view at the screen edge | Mesh bounds too tight. Culling runs before the vertex shader |
| Bending looks fine, depth is wrong | `CardApplyBendPosition` drifted from `CardApplyBend` |
| Tool rates feel wrong after a texture change | Wear map imported sRGB instead of linear — it is data |
| Dents blur across the card | Wear sampled with a filter, or through a different UV than the artwork |
| Whole shader stops batching | A property declared outside `CBUFFER(UnityPerMaterial)` |
| Every card grades Mint | `CardCondition` full-scale constants not re-measured after an ageing change |
| Back of the card restores the wrong half | Pointer not mirrored to match the shader's `uv.x` flip on the back face |

Two tools that answer most of these without entering play mode:

```bash
Tools > Cozy TGC > Render Foil Preview    # 5 tiers x 4 angles -> CardFoilPreview.png
Tools > Cozy TGC > Render Wear Preview    # aged / turned / restored / flipped, + the grading ramp
```

---

# Where things live

| | |
|---|---|
| Properties, passes, keyword pragmas | `Assets/Shaders/CardHolo.shader` |
| Foil math, CBUFFER, samplers | `Assets/Shaders/CardHoloInput.hlsl` — `CardFoil` |
| Wear sampling, dent normals, bending | `Assets/Shaders/CardWear.hlsl` |
| Ageing, rubbing, grading, saving | `Assets/Scripts/Runtime/CardWear.cs` |
| The six tools, as rates | `Assets/Scripts/Runtime/RestorationTool.cs` |
| Grade and price multiplier | `Assets/Scripts/Runtime/CardCondition.cs` |
| Rotation, hover, flip, property block | `Assets/Scripts/Runtime/CardView.cs` |
| The mesh, materials and prefab | `Assets/Editor/CardDemoBuilder.cs` |
| The debug views | `Assets/Shaders/CardDebug.hlsl` |
| The wireframe / vertex / frame overlay | `Assets/Shaders/CardOverlay.shader` + `CardMeshOverlay.cs` |
| This tutorial, as a scene | `Assets/Scripts/Runtime/CardExplainerController.cs` |
| The scene it is built into | `Assets/Editor/CardExplainerBuilder.cs` |
