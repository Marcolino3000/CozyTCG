# Building the card effects, step by step

A build order for a new Unity project. Every step says **what to achieve** and **how to see whether
you achieved it**, in Scene view or Play mode. None of them hands you the code.

The unit here is not "a function" but "a thing you can look at". You should never be more than about
twenty minutes from something on screen that is either right or wrong. If a step's test cannot fail,
it is not a test — tell yourself what the failure would look like before you run it.

## The rules

1. **Never write two steps' worth before testing.** The entire point of this ordering is that when
   something breaks you know it was the last thing you did.
2. **Predict the failure first.** Before each test, say out loud what a wrong result would look like.
   A test you cannot fail teaches nothing.
3. **Do not reach for the finished project.** `CARD_SHADER_PORT.md` in this repo has the complete
   code and an answer key. It is there for *after* a step works, to compare against. Opening it
   during a step converts an hour of learning into five minutes of typing.
4. **When a test fails, find out why before changing anything.** Every milestone below lists the
   specific ways it goes wrong. Read those before you start editing at random.

## Reading the step format

> **Achieve** — what must be true when you are done.
> **Work out** — what you have to decide or discover. This is the actual work.
> **Test** — the procedure, and what pass and fail look like.
> **Goes wrong as** — the specific failures for this step, so you recognise them.
> **Vocabulary** — names to look up. Not answers; the words you need to search for.

---

# Milestone 0 — A project that renders at all

Nothing here is about cards. It is about eliminating four silent failures that would otherwise
poison every later test.

## 0.1 Create the project

**Achieve.** A new project on the **Universal 3D** template, Unity 6000.3 or newer.

**Test.** The default scene shows its skybox and any object you drop in is lit and grey — **not
magenta**. Magenta means the render pipeline and the shaders disagree, and everything below will
inherit that.

**Goes wrong as.** Starting from the Built-in 3D template. You can convert later, but you will spend
the conversion time confused about whether your own shader is broken.

## 0.2 Colour space and input

**Achieve.** Colour space **Linear**, Active Input Handling **Both**.

The two are not equally important. **Linear is load-bearing** — see below. **Both** is only
convenience: Milestone 4.3 has you read the mouse, and the legacy `UnityEngine.Input` calls throw at
runtime if the project is set to *Input System Package (New)* alone. "Both" means either API works,
so you can pick one without it being a decision. If you already know which you want, set that
instead and ignore this.

**Work out.** Find where each of those lives in Project Settings. While you are there, find where the
project says which render pipeline asset is in use, and confirm it is not empty.

**Test.** The setting itself is the ground truth — read it in Project Settings and you know. What is
worth checking is that the project actually *renders* that way, and for that you need something that
**combines** two values, because a flat colour will not show you anything: an unlit quad at sRGB 128
renders as 128 on screen in either colour space. Linear converts it down on upload and back up on
output, and the round trip cancels.

So blend instead. Three quads in Scene view, on an unlit shader, post-processing **off** on the
camera (tonemapping would move the numbers):

1. Black.
2. White, alpha **0.5**, drawn over the black one.
3. A reference at sRGB **128**, next to them.

Screenshot and pick the two greys with a colour picker.

- **Linear:** the blend happens in linear, giving 0.5 linear, which displays around **187**. Clearly
  brighter than the reference.
- **Gamma:** the blend happens in display space, giving **128**. Matches the reference.

If they match, you are in gamma and you did not achieve the step.

**Why this one works when the flat quad did not:** linear colour space changes what happens to values
that are *combined* — blends, lighting, mip averaging, anti-aliased edges. It does not change a
colour that only travels from the inspector to the screen untouched.

**Goes wrong as.** Gamma space costs you nothing today and ruins the foil in Milestone 8: every
colour constant you tune will be wrong, and the card stock colour reads pink. It is the single most
annoying thing to discover late, because fixing it re-tunes every value you have chosen since.

## 0.3 Folders

**Achieve.** `Editor/`, `Materials/`, `Meshes/`, `Resources/Cards/`, `Scenes/`, `Scripts/Runtime/`,
`Shaders/` under `Assets/`.

**Work out.** Two of those folder names are **magic** — Unity treats them specially regardless of
what is in them. Which two, and what does each one do? You need to know this before Milestone 2,
because putting an editor script in the wrong place produces a build error that names none of this.

**Test.** No test. But write the answer down; the next milestone assumes it.

## 0.4 The bench scene

**Achieve.** A scene `CardBench.unity` with a perspective camera looking down −Z at the origin, and a
background you can see a card's silhouette against.

**Work out.** Why perspective and not orthographic? This matters more than it sounds — think about
what an orthographic projection does to the *view direction* at each pixel, and what a view-dependent
effect has left to work with when every pixel shares one. Answer this now; in Milestone 7 it becomes
the difference between an effect and a flat colour.

**Test.** Drop a default Cube at the origin. In Play mode it is visible, centred, and you can see
perspective foreshortening on its side faces.

---

# Milestone 1 — A card on screen with no shader of your own

Before you write a line of HLSL, prove your **art** and **scene** are right. Every bug you find here
is a bug you would otherwise blame on your shader for a day.

## 1.1 Get the art in

**Achieve.** A front and a back image for one card, same pixel size, in `Assets/Resources/Cards/`.
This guide assumes **73 × 113**; use your own and keep it consistent everywhere.

**Test.** Both appear in the Project window and their inspector reports the pixel size you expect.

## 1.2 Draw it on a stock material

**Achieve.** Unity's built-in Quad, with a **URP/Unlit** material carrying your front art, visible in
Scene view.

**Test.** You can see the card. Now zoom the Scene view camera right in on it.

**Look carefully, because this is the whole reason for the step:** the pixels are **blurry**. That
blur is the default bilinear filter, and removing it without losing anti-aliasing is what Milestone 6
is about. You want that image in your memory now, so that when Milestone 6 works you can tell.

## 1.3 Import settings

**Achieve.** Import settings suited to pixel art on a rotating 3D quad, applied automatically to
anything dropped in `Resources/`.

**Work out.** Six decisions, and each one has a specific consequence later:

| Decision | The question to answer |
|---|---|
| Filter mode | Point *seems* right for pixel art. It is not, here. Why? What does a point-filtered card do as it slowly rotates? |
| Mip maps | On or off, for a card that gets small on screen? |
| Mip alpha coverage | What happens to a **cutout** silhouette as it shrinks down the mip chain, if you do nothing about it? |
| Wrap mode | Milestone 10 samples *neighbouring* texels. What does a tap at the card's border read, on the wrong setting? |
| sRGB | Correct for artwork. Note it now — it will be **wrong** for the wear map in Milestone 9, and you should be able to say why. |
| Compression | What does DXT do to a 73×113 image with hard colour edges? |

**Achieve, part two.** An `AssetPostprocessor` that stamps these automatically, plus a menu item to
re-apply them to everything already imported.

**Work out.** An `AssetPostprocessor` fires on *every* import. Yours must (a) only touch textures
under your own folder, and (b) **not** fight a human who has changed a setting by hand afterwards.
There is a property on the importer that tells you whether this is a first import — find it.

**Test.** Scene view, zoomed in: still blurry (you have not written the snapping shader yet), but now
zoom *out* until the card is tiny. Watch the border. With coverage-preserving mips on, the silhouette
holds its shape as it shrinks; without, it dissolves. Toggle the setting and watch the difference —
this is the clearest demonstration of that setting you will ever get.

**Vocabulary.** `AssetPostprocessor`, `OnPreprocessTexture`, `TextureImporter`,
`mipMapsPreserveCoverage`, `alphaTestReferenceValue`, `importSettingsMissing`, `MenuItem`,
`AssetDatabase.FindAssets`, `SaveAndReimport`.

---

# Milestone 2 — Your own mesh, in four testable stages

A quad has four vertices. Bending (Milestone 11) happens in the vertex shader, and a vertex shader
can only move vertices that **exist**. So you need your own mesh — but build it in stages, because a
mesh generator that is wrong produces a card that is invisible, inside-out, or mirrored, and those
look alike from the outside.

## 2.1 An editor menu item that does nothing

**Achieve.** `Tools > … > Build Card Mesh` appears in the menu bar and logs a line when clicked.

**Test.** The menu item exists and the console gets your line.

**Why bother with a step this small.** It proves the editor assembly compiles and your folder choice
in 0.3 was right, in isolation, before there is any mesh code to blame.

## 2.2 A four-vertex quad, generated

**Achieve.** The menu item writes a `Mesh` asset with four vertices, two triangles, and UVs, and you
have assigned it to a MeshFilter in the bench scene.

**Work out.**

- The card must face **−Z**, matching Unity's built-in Quad, with the visible side at
  `-transform.forward`. Work out why that convention and not +Z: think about where the camera sits
  when the card's transform is unrotated, and which way the art must face to read correctly.
- Which winding order gives you a face whose geometric normal is −Z? Get this wrong and the card is
  invisible from the front and visible from behind.
- World size: at 100 pixels per unit, what world size matches 73 × 113 px?

**Test.** Three separate checks, each catching a different mistake:

1. **Select the asset.** The inspector preview shows a quad and reports 4 verts / 2 tris.
2. **In the scene**, the card is visible from the camera's default position — if it is invisible but
   appears when you orbit behind it, your winding or normal is backwards.
3. **Put readable text in the art.** It must read correctly, not mirrored.

## 2.3 Subdivide it

**Achieve.** The same generator, now producing a grid — 16 × 24 cells is a good choice.

**Work out.**

- Vertex and triangle counts for `cols × rows`. Compute them **before** you look at the inspector,
  then compare. If your arithmetic and Unity disagree, you have an off-by-one in your stride.
- Why 16 × 24 and not 73 × 113 (one cell per pixel)? The bend you will build in Milestone 11 is
  *low frequency* and only has to read in the silhouette. The sharp side of damage is a per-pixel
  normal in the fragment stage and needs no geometry at all. What would 73 × 113 cells cost you, and
  what would it buy?

**Test.** **Scene view, Shading mode → Wireframe** (the drop-down in the Scene view toolbar). You see
the grid. Count a row against your arithmetic. Then switch back to Shaded and confirm the card still
looks *identical* to 2.2 — subdividing a flat quad must change nothing visible.

That last check is the real test: a subdivision bug shows up as seams, T-junctions or a missing
triangle, and against a flat card those are visible as hairline cracks.

## 2.4 Normals, tangents, and bounds

**Achieve.** Every vertex carries the correct normal and tangent, and the mesh's bounds reserve depth
for a bend that does not exist yet.

**Work out.**

- At rest, the card's tangent frame is **T = +X, B = +Y, N = −Z**. The tangent is a `Vector4` — what
  is the `w` component for, and which sign do you need so that the bitangent lines up with **+V** of
  your UVs? Getting this wrong mirrors the foil later without mirroring the artwork, which is a
  maddening bug to find.
- Bounds are used for **culling**, not rendering. If a vertex shader later moves vertices outside the
  bounds, what happens, and *when* does it happen? (Hint: it depends on where the object is relative
  to the camera frustum.) Reserve roughly 0.14 units of depth and note why.

**Test for the tangent.** You cannot see it yet. Rather than guessing, write down what you chose and
why, and mark this as the first thing to re-check if the foil comes out mirrored in Milestone 8.

**Test for the bounds.** Temporarily set the bounds to something absurdly small (say 0.01 deep).
Enter Play mode and pan the camera so the card drifts toward the edge of the screen. It **pops out of
existence** before it leaves the view. That is exactly the bug you are pre-empting, and seeing it
once now means you will recognise it instantly in Milestone 11 instead of blaming the camera.

**Vocabulary.** `Mesh.vertices/uv/normals/tangents/triangles`, `Mesh.bounds`,
`AssetDatabase.CreateAsset`, Scene view Shading Mode.

---

# Milestone 3 — Your first shader, in five increments

Do not write the card shader. Write three shaders' worth of increments, each visible.

## 3.1 A solid colour

**Achieve.** Your own `.shader` file, a URP unlit pass, output one hard-coded colour. Material on the
card.

**Work out.** `Create > Shader > URP Unlit Shader` gives you a working URP shader — the URP package
ships the template. Start from it and **cut it back** to a single hard-coded colour: no texture, no
properties, nothing sampled. (Older Unity only offered a Built-in pipeline template here, which
compiles to magenta under URP. If your menu has no URP entry, that is why.)

Then read what is left and answer these, because the template quietly leaves out things you need
from 3.5 onward:

- What does the SubShader's `RenderPipeline` tag do? Delete it and find out.
- The Pass carries **no** `LightMode` tag, and it renders anyway. Find out what URP does with an
  untagged pass. Then work out why you will have to name it explicitly the moment you add a second
  pass in 3.5.
- `RenderType` is `Opaque` and there is no `Queue` tag. Note both — 3.5 changes them.
- Which include gives you `TransformObjectToHClip`, and what did it replace?

**Test.** The card is your colour, flat, in **Scene view** — no Play mode needed. If it is magenta,
open the shader in the inspector and read the error at the top; do not guess.

## 3.2 Output the UV

**Achieve.** Return `float4(uv, 0, 1)` — red across, green up.

**Test, and this is the most informative single test in the whole document.** You should see black in
one corner, red along one axis, green along the other, yellow in the far corner. Now check:

- **Which corner is black?** That tells you where uv (0,0) is.
- Does red increase in the direction you expect? Does green?
- Is the gradient smooth right across, with no seam or smear where two triangles meet? A wrong UV on
  a single vertex shows up here as a visible tear, which is the cheapest way you will ever catch one.

You cannot check the back face yet — with no `Cull` directive the shader defaults to `Cull Back` and
the reverse side is not drawn. That check belongs to Milestone 5, which is where `Cull Off` arrives.
If you want it now, add `Cull Off` to the Pass temporarily and take it out again.

Everything the fragment stage will ever know about *where it is* on the card comes from this. Five
minutes here saves you from a whole class of bug where the art is right and the effect is not.

## 3.3 Sample the texture

**Achieve.** A `_FrontTex` property, sampled at the UV, tinted by a `_Tint` colour.

**Work out.** URP's texture declaration and sampling macros are not the old `sampler2D` /
`tex2D`. Find the current ones. Also find out what `TRANSFORM_TEX` is for and what breaks without it
— set a non-default tiling on the material and see.

**Test.** The card art appears, right way up, unmirrored. Change `_Tint` in the inspector and watch
it apply live in Scene view.

## 3.4 The batching check

**Achieve.** All material properties in a single correctly-named constant buffer.

**Work out.** URP's SRP Batcher has a specific requirement about where material properties are
declared. Find out what the buffer must be called, and what happens to batching when a property is
outside it.

**Test.** `Window > Analysis > Frame Debugger`, enable, find your card's draw call. It must say the
batcher is **compatible**. If not, the reason is printed there — fix it now, while the shader has
four properties instead of sixty.

**Goes wrong as.** Nothing looks wrong. You find out months later in a profiler, and by then the
CBUFFER is long enough that finding the offending line is tedious.

## 3.5 Alpha cutout and the depth pass

**Achieve.** The transparent border is gone, and the shader has a second pass that writes depth only.

**Work out.**

- Cutout or alpha blending, for a card? Answer it in terms of what ends up in the **depth buffer**,
  and what that means when two cards overlap.
- Which SubShader tags and which render queue go with cutout?
- What is the `DepthOnly` pass *for* in URP — what consumes it? Find at least one consumer by name.
- Both passes must clip on **exactly the same condition**. Why, and what does a mismatch look like?

**Test.** Two cards, overlapping, one rotated slightly. Rotate in Scene view. Neither card's
transparent border may occlude the other. Then, in the Frame Debugger, step to the depth prepass and
confirm the card's silhouette appears there — not a full rectangle.

**Vocabulary.** `HLSLPROGRAM`, `Core.hlsl`, `TransformObjectToHClip`, `GetVertexPositionInputs`,
`TEXTURE2D` / `SAMPLER` / `SAMPLE_TEXTURE2D`, `CBUFFER_START(UnityPerMaterial)`, `clip`, `LightMode`,
`RenderType`, `Queue`, `ColorMask`.

---

# Milestone 4 — CardView, in five Play-mode tests

Now the card has to move, because every effect from Milestone 7 on is **view-dependent** and you
cannot judge any of them on a stationary card. Build the driver in stages; each one is a Play-mode
test.

## 4.1 It turns at all

**Achieve.** A MonoBehaviour with a serialized yaw angle that rotates the card.

**Test.** Play mode. Drag the yaw field in the inspector; the card turns live. Leave Play mode and
confirm the value snapped back — a good moment to internalise that Play-mode inspector edits are
discarded.

## 4.2 The Root/Visual split

**Achieve.** The component sits on a **root** object that never rotates; a **child** called `Visual`
holds the MeshFilter and MeshRenderer and does all the turning.

**Work out.** This looks like pointless indirection until you know what it prevents. In 4.3 you will
convert a mouse position into a rotation. If the object you measure *against* is the same object you
*rotate*, what happens? Reason it through before you build it — this is a feedback loop, and the
jitter it produces looks exactly like a bad smoothing filter, which is where people waste the day.

**Test.** Play mode, card turning. Select the root: its Transform rotation stays at `(0,0,0)`
throughout. Select `Visual`: its rotation is what moves. If the root ever rotates, the split is not
actually in place.

## 4.3 Drag to spin

**Achieve.** Press and drag anywhere; the card spins by the drag delta. Release and it holds.

**Work out.** Pitch has to be clamped or the card flips over its own pole and the controls invert.
Where do you clamp, and to what?

**Test.** Play mode. Drag left and right — the card yaws. Drag up and down — it pitches, and stops at
the limit rather than tumbling. Release mid-drag and it stays where you left it, without a jump.

That last one — no jump on release — is a real test. It fails if you are storing the accumulated
drag rather than committing it on release.

## 4.4 Idle motion

**Achieve.** A card nobody is touching sways gently, and each card sways out of phase with its
neighbours.

**Work out.** Where does the per-card phase offset come from? Two cards swaying in lockstep read as
one object, which is worse than no sway at all.

**Test.** Play mode, two cards. Both move; they are visibly *not* synchronised.

## 4.5 The property block

**Achieve.** Per-card values — face texture, a random sparkle seed — applied through a
`MaterialPropertyBlock`, never by touching `Renderer.material`.

**Work out.**

- What does reading `Renderer.material` actually do, and why is that the thing to avoid?
- A property block has one hard limitation that will shape your design in Milestone 6: it cannot set
  one particular kind of shader state. Find out which. (You will hit it as a real problem later; know
  it now.)

**Test.** Two cards in the scene, **the same material asset** on both, different `_Tint` values set
through property blocks. Both tints show. Now open the Frame Debugger: they must still batch. If you
used `.material`, you will see two materials and no batching — do it wrong once, look at the
difference, then do it right.

**Vocabulary.** `MaterialPropertyBlock`, `Renderer.SetPropertyBlock`, `Shader.PropertyToID`,
`sharedMaterial` vs `material`.

---

# Milestone 5 — Two faces

**Achieve.** `Cull Off`, and the back of the card shows the back artwork, correctly oriented.

**Work out.**

- The face test must be **geometric** — derived from the surface normal against the view direction.
  Not `SV_IsFrontFace`, not winding. Work out why: in Milestone 11 a bent card has surface facing
  both ways within one triangle strip, and winding is a property of the triangle, not of the pixel.
- Something about the UV has to change on the back face. Draw the quad, its UV square, and the camera
  on both sides, and derive it from the picture rather than trying both signs and keeping whichever
  looks right — because in Milestone 10 you need to know *why* it flips, to work out what else flips
  with it.
- The tangent frame must flip too. Which of T, B, N do you negate — and which one must you leave
  alone to preserve handedness? Work out what `cross(N, T)` does when you negate both.

**Test.** Put clearly different art on front and back. Play mode, drag the card slowly through 180°.

1. The swap happens **exactly** at edge-on. Not before, not after.
2. Neither face is mirrored — readable text stays readable on both.
3. Turn slowly through the transition several times. There is no flicker at the crossover.

**Goes wrong as.** Flipping the UV but not the tangent frame gives you correct artwork and a foil
that runs backwards on the back face. You will not notice until Milestone 8, and you will blame the
rainbow. Write a note now.

---

# Milestone 6 — Pixel-perfect sampling

This is what turns "a texture on a quad" into pixel art. Three steps, and the first one is an
instrument, not a feature.

## 6.1 Build the texel grid view first

**Achieve.** A `_DebugView` float on the material. At 1, the shader draws a checkerboard of the
card's own texel grid instead of the artwork.

**Work out.** How do you get from a UV to "which texel am I in"? You need the card's pixel size as a
property — add `_CardPixels` now.

**Test.** Set `_DebugView` to 1 in Scene view. You see a checkerboard with exactly 73 × 113 cells.
Count one row across if you doubt it. Zoom in: the cells are square and evenly sized. If they are
rectangular, your `_CardPixels` and your art's aspect ratio disagree.

**Why first.** The next step manipulates UVs at sub-texel precision. Debugging that without being
able to see the grid is guesswork.

## 6.2 The snap

**Achieve.** Sampling snapped to texel centres, but with a ramp exactly **one screen pixel** wide
across each texel seam. Behind a keyword so you can toggle it.

**Work out.** This is the one piece of real insight in the milestone, and it is a single idea:

> With `p = uv * texSize`, what does `fwidth(p)` measure, and in what units?

Write the sentence "one unit of `fwidth(p)` means ______ per screen pixel." Once you have that
sentence, the rest follows mechanically: find the nearest texel centre, measure your offset from it,
convert that offset into screen pixels using the answer, and clamp it.

Then work out **why one screen pixel** is the right ramp width — what does a wider ramp look like,
and what does a narrower one look like?

**Test, in three parts.**

1. **Static:** toggle the keyword in Scene view, zoomed in. Off: blurry (the Milestone 1.2 image).
   On: crisp, hard-edged pixels.
2. **Rotating:** Play mode, spin the card continuously for ten seconds and watch one diagonal edge.
   Nothing may crawl or shimmer. Compare with point filtering set in the importer — that crawls
   badly, and seeing it is the argument for this whole approach.
3. **Minified:** zoom out until the card is ~30 px tall. It must not sparkle with aliasing.

**Goes wrong as — and this one costs people an hour.** A `[Toggle]` property sets a **float**. A
`#pragma shader_feature` branches on a **keyword**. Setting one without the other does nothing at
all, silently. From C# they must always be set together. Find out which two calls you need, and note
that this is the limitation you found in 4.5 — a property block cannot do it.

## 6.3 The mip trap

You have just written a bug you cannot see. Find it before it finds you.

**Achieve.** The artwork samples at the snapped UV but selects its mip level from the **unsnapped**
one.

**Work out.**

- How does the hardware choose a mip level? What exactly does it look at?
- Your snapped UV is deliberately **discontinuous** at every texel seam. What does that do to the
  answer above?
- So you need to sample at one UV with the derivatives of another. Find the sampling macro that lets
  you pass gradients explicitly.

**Test.** Zoom out until the card is ~40 px tall and pan the camera. Before the fix it is a flat
smear; after, it reads as a small card. Then add a debug view (number 2) that draws
`log2` of the texel density as a colour — it must vary smoothly across the card, not flicker per
texel.

**Vocabulary.** `fwidth`, `ddx` / `ddy`, `shader_feature_local_fragment`,
`Material.EnableKeyword` / `DisableKeyword`, `SAMPLE_TEXTURE2D_GRAD`.

---

# Milestone 7 — Tilt

One `float2`. Five effects hang off it. If it is wrong, all five are wrong and you will debug the
wrong five — so it gets its own milestone and its own debug view.

## 7.1 The tangent frame reaches the fragment stage

**Achieve.** T, B and N in world space, interpolated to the fragment stage.

**Test.** Debug view 3: output the world normal as `N * 0.5 + 0.5`. A flat card facing you shows one
flat colour. Turn it in Play mode: the colour changes smoothly with rotation and there are no seams
between triangles. Seams mean you are not normalising after interpolation.

## 7.2 Tilt itself

**Achieve.** A `float2` that is **zero** when the card faces the camera square-on, grows as it turns
away, and stays **bounded** at grazing angles.

**Work out.**

- Why express the view direction in **tangent space** rather than using it in world space? Answer in
  terms of what happens when you *move* the card versus when you *turn* it — one must change tilt and
  the other must not.
- You want the component of the view direction that lies **along the card's surface**, divided by how
  head-on you are. Write that as a formula. It is the same construction as a parallax offset; that is
  not a coincidence.
- Two *separate* mechanisms are needed to keep it bounded: a floor under the divisor, and a soft
  saturation of the result. Work out what each prevents on its own, and why one does not cover for
  the other. Try removing each in turn and look.
- **Write it so that a per-pixel normal can be substituted for the flat one without restructuring
  it.** In Milestone 10 you will do exactly that, and it should be a one-line change. Concretely:
  derive "how head-on am I" as a dot product against a normal *vector*, not as a single component.

**Test.** Debug view 4: `float3(tilt * 0.5 + 0.5, 0.5)`.

1. Card facing you, centre of the card: **flat grey**. Not grey-ish — grey.
2. Play mode, turn: the colour shifts smoothly and consistently with the direction you turn.
3. Turn to nearly edge-on: the colour goes somewhere and **stays**. If it blows out to white, your
   bound is missing or in the wrong place.
4. **Move** the card sideways without rotating it: tilt must barely change. If it swings about, you
   have built something that responds to position rather than orientation.

That fourth test is the one that catches a genuinely wrong tilt, and it is the one people skip.

---

# Milestone 8 — The foil, one visible layer per step

Five layers. Build them in this order, and after each one **set every other strength to zero and look
at the new layer alone**. A foil built all at once is a foil you cannot tune, because you cannot
attribute anything you see to anything you did.

Each step below is a Play-mode test with the card turning. A still card tells you almost nothing
about a view-dependent effect.

## 8.0 A rig that makes comparison possible

**Achieve.** Two cards side by side in the bench scene, turning together. One stays on a fixed
reference material; the other is the one you are working on.

**Test.** Play mode: both turn in sync. From here on, every judgement is a *difference between two
cards*, not a memory of what the card looked like ten minutes ago. This is the single biggest
accelerator in the whole document and it costs ten minutes.

## 8.1 A spectrum function

**Achieve.** `t` in 0..1 → a colour running through the spectrum, wrapping seamlessly at 1.

**Work out.** You want three channels peaking at three different points in the cycle. The standard
cheap trick is three cosines with phase offsets. Work out what offsets, and how to remap a cosine's
−1..1 into 0..1.

**Test.** Output `Spectrum(uv.x)` across the card. A smooth hue sweep, no dark band anywhere, and
**no visible seam** at the left and right edges — hold the card and check the wrap specifically.

## 8.2 Rainbow diffraction

**Achieve.** Bands of spectrum across the card whose hue shifts as it turns, quantised to a small
number of steps.

**Work out.**

- Bands at an angle: how do you turn an angle and a UV into a scalar that increases across the card
  in that direction? (One dot product.)
- **Three** separate things must move the band phase: position across the card, tilt, and time. Work
  out what each contributes — and specifically, which one makes the effect view-dependent, and what
  the other two do that it cannot.
- Separately from all that, offset the *sampling position* by tilt. Work out what this adds that
  phase-shifting alone cannot. Look at a real holographic card at a shallow angle: the pattern
  appears to sit *below* the surface and slides against the print. One of these two mechanisms
  produces that and the other does not.
- Quantising: `floor` or `round`? Do both and look at the band widths at each end of the range.

**Test.** Everything else at 0, steps at 8.

1. Turning: discrete bands march across the card.
2. Set the tilt response to 0: bands stop reacting to rotation but stay on the card.
3. Set the drift up with the card **still**: bands move on their own.
4. Set the parallax depth up and turn: the bands slide *against* the artwork rather than with it.

Four knobs, four distinguishable behaviours. If two of your knobs do the same thing, the maths is
wrong.

## 8.3 Sweep

**Achieve.** A soft specular bar that slides across the face as the card turns, parked near an edge
at rest.

**Work out.** Same "distance along a direction" scalar as 8.2; what differs is what you do with it.
You want a soft-edged bar — find a falloff, and identify what controls its width in UV units.

Then: why does it need a **rest offset**? Set it to zero and turn the card through its full range.
Describe what you lose.

**Test.** Play mode, sweep only. Turning slowly through the full range, the bar must **enter one edge
and leave the other**. If it sits near the middle and wobbles, your offset and travel are wrong.

## 8.4 Sparkle

The hardest one here. Budget accordingly.

**Achieve.** Glitter flakes that **wink on and off** as the card turns — not a sparkle texture that
slides around.

**Work out, in this order, testing each:**

1. **A hash.** A stable pseudo-random `float2` from a `float2`.
   **Test it before building on it:** output `float3(hash(floor(uv*40)), 0)` across the card. You
   want television static. Diagonal stripes, repeating blocks or a correlation between the two output
   channels all mean it is broken — and every symptom later will lie to you about why.
2. **Cell placement.** Divide UV into cells, one flake per cell at a hashed position.
   **Test:** flakes appear, evenly scattered, no visible grid.
3. **The neighbourhood.** You must test the 8 surrounding cells too. Work out the failure case first
   — draw a flake near a cell boundary and ask what a pixel in the next cell sees.
   **Test:** compare with and without. Without, every flake is clipped square and a grid appears.
4. **The per-flake preferred angle.** This is the entire effect. Each flake gets its own random
   preferred tilt and only fires when the current tilt is near it.
   **Test:** turn slowly. Individual flakes wink on and off *at different moments*. If they all
   brighten together, this is not wired in.
5. **Accumulation.** `max` or sum across the nine cells? Work out what the wrong one does where two
   flakes overlap.

**Test, overall.** Card still: flakes steady, not fizzing. Density at maximum: no moiré, no visible
grid. Turning: independent winking.

## 8.5 Chrome

**Achieve.** A mirror finish with a sky, a ground and a sun in it, that does **not** wash out the
artwork when the card faces you.

**Work out.**

- Reflect the view direction about the surface normal. In **which space** must this be computed for
  the sky to stay *up* as the card turns? Get this wrong and the environment rotates with the card,
  which is exactly backwards.
- Sky above, ground below, from the reflection direction alone: which single component do you need?
- A sun: a dot product against a direction, raised to a power. What does the exponent control?
- Head-on, a full mirror hides the print. Build a ramp that damps chrome when you look straight at
  the card and opens it up as it turns away. It should be a function of a quantity you already
  computed in Milestone 7.

**Test.** Chrome only. Face the card at the camera: **the print must still be readable**. Turn 45°:
the mirror takes over. The sun is a tight highlight that sweeps as you turn, not a wash. Then turn
the card upside down — the sky must stay up.

## 8.6 Edge glow

**Achieve.** A glossy rim at grazing angles, gone head-on.

**Work out.** It is one line, built from a quantity you already have. Also: what is Fresnel
reflectance physically, and why is a power of one dot product a defensible cheat for it?

**Test.** Strength up, everything else 0. The rim appears on the **silhouette** and vanishes head-on.
Note where "the silhouette" is — in Milestone 11 it stops being the outline of the mesh, and this
layer will follow it for free.

## 8.7 The mask

**Achieve.** Foil that lands on the print rather than smeared uniformly, from two combinable sources:
an optional mask texture, and the artwork's own **luminance**.

**Work out.**

- Why derive a mask from luminance rather than painting one per card? Count your cards, then count
  them again for a second deck.
- Luminance weights: why the standard uneven coefficients rather than an even third each?
- Contrast and bias around a pivot: write the remap, and identify where the pivot is. What happens to
  the mask as contrast goes to its maximum?
- The blend parameter at 0 must mean **exactly** no masking — 1.0, not approximately 1.0. Make sure
  your formula degrades to that rather than approaching it.

**Test.** Set the luminance blend to 0 and screenshot. Compare with the unmasked card **pixel by
pixel**, not by impression. Then raise contrast to maximum and watch the foil retreat onto the
brightest parts of the print.

## 8.8 Compositing, and keeping it pixel art

**Achieve.** Five layers into one card, still reading as pixel art. Three things:

- A blend parameter between **additive** and **screen** compositing.
- A second keyword that snaps the *foil* to the card's texel grid — **without** the screen-pixel ramp
  from 6.2.
- A quantisation of the foil's colour into steps.

**Work out.**

- Write both blend formulas. Where do they differ most, and what does additive do on already-bright
  artwork that screen does not?
- **Why no ramp on the foil snap,** when the ramp was essential for the artwork? Think about what the
  ramp was *for* in 6.2 — it anti-aliased the artwork's texel edges against the screen. The foil is
  not artwork; it is a smoothly varying value, and snapping it to the grid is what makes it look
  printed *on* the pixel art rather than floating above it. A ramp would blur exactly the edges you
  are trying to create.
- State clearly what each of your two keywords is applied to. Confusing them is the classic mistake
  in this shader.

**Test.** Toggle each keyword **in both passes** and confirm both do what you expect. Set the blend
to 0 and 1 on a bright card and confirm you can see the difference in the highlights. Then set up
five materials at five different strength presets and look at them side by side, turning — that is
your rarity ladder, and it is the same shader five times.

---

# Milestone 9 — The wear map, built as data first

Five kinds of damage. The insight of this part is that they are **one texture**, and one of its
channels makes three foil layers react to damage without any of them knowing damage exists.

Build the *data* before the shading, and look at the data directly.

## 9.1 Design the channels

Before any code. This is the most valuable half hour in the document.

**The brief.** A card can be scuffed, lose ink, be dented and creased, and have chips missing from
its edges. It must be restorable **per face** by rubbing. It is 73 × 113 pixels.

**Work out — write your answers down before continuing.**

1. One RGBA texture per face. Five kinds of damage, four channels. Which two share a channel, or
   which one is not in the map at all — and why?
2. Which channels must be **per face**, and which must come from **one** face's map for both sides?
   Justify each from physics: what does a dent do to the other side of a piece of card? What does a
   chip do?
3. Given your answer to 2 — what does the `DepthOnly` pass see, and what breaks if chips live in the
   wrong map?
4. Height must be **signed** (dents *and* ridges) in an unsigned texture. What is your encoding, and
   where is flat?
5. Import settings, and a reason for each: sRGB or linear? Point or bilinear? Mips? Wrap mode? At
   least one of your reasons should be about the *neighbour taps* you will write in 10.4.
6. One kind of damage in the brief **cannot** live in a texture at all. Which, and why? What has to
   change for it to work?

**Test.** Write out your channel table. Then compare it against the one in `CARD_SYSTEM.md` (the
reference doc — reading a spec is not cheating). If you differ, work out which is right and why. It
is entirely possible yours is better; what is not acceptable is not knowing which.

## 9.2 A map you can see

**Achieve.** A component that creates two `Texture2D`s at the card's pixel size, fills them with a
recognisable test pattern (a gradient, or your initials), and binds them to the material through the
property block.

**Test.** Add a debug view that outputs the map's RGB directly. Your test pattern appears on the
card, at the right size, the right way up, not mirrored, not blurred. **Do this before writing any
damage generator** — it isolates "can I get data onto the card" from "is my damage any good", and
those two bugs look identical from a distance.

**Work out.** The texture must be created in code with specific settings, not imported. Which
constructor arguments control linear-vs-sRGB and mips? Which properties control filtering and wrap?
And why must this texture be the exception to your Milestone 1.3 import rules?

## 9.3 The damage passes, one at a time

**Achieve.** Five generator passes writing into a CPU-side array that uploads to the texture:
**edge wear, scratches, dents, creases, chips**.

**Build and test them one at a time**, each with the other four disabled. Give yourself a flags enum
so you can.

For each pass, the questions are the same three: *what shape is it, where does it appear, and how
does severity scale it?* Some specific ones worth thinking about:

| Pass | Work out |
|---|---|
| Edge wear | Why the border and not the middle? What does a card actually get handled by? Why do corners take it worse than edges, and by how much? |
| Scratches | A scratch must take **print** with it, not only scuff. Work out why: what does a scuff-only scratch look like on a *common* card with no foil? |
| Dents | Where on a card do dents concentrate, and why? What profile does a dent have from centre to rim? |
| Creases | A crease is a **fold**, not a scratch: a valley with the paper standing up either side. What function has that shape? What else does a fold do to the print along its crest? |
| Chips | Centred **on** the border or inside it? Work out what you get from the wrong choice. What happens to the paper immediately around the hole? |

Two structural decisions to make deliberately:

- **Stamping**: when two scratches cross, do the values **add** or do you take the **worse**? Try
  both; one of them turns a crossing into a hole.
- **Determinism**: the same card must age identically every time it loads, so nothing has to be
  stored but a seed. What does that require of your random number source, and what happens to the
  other four passes if you disable one? (They share the stream — so a subset is its *own* card, not a
  layer of the full one. Decide whether you are happy with that.)

**Test, per pass.** Enable that pass only, severity at 1, and look at the raw map through your 9.2
debug view. You are checking the *distribution*: is it where it should be, at the scale it should be?
Then step severity 0 → 1 and confirm it scales sensibly rather than jumping.

**Test, all five.** Age at severity 0.3, 0.6 and 1.0 on three cards side by side. They should read as
"lightly played", "played" and "poor" — if 0.3 already looks wrecked, your scaling is wrong.

## 9.4 Uploading

**Achieve.** The float array uploads to the texture efficiently, once per change, not per frame.

**Work out.** Why hold the map as **floats** on the CPU when it uploads as bytes? Think about a soft
brush whose outermost texels change by a twentieth of the centre's rate, per frame, in a byte.

Find the efficient upload path — there is one that avoids allocating a `Color32[]` every time.

**Test.** Play mode, Profiler open. Age a card repeatedly. The upload must not allocate per frame,
and a card that has not changed must not upload at all.

---

# Milestone 10 — Damage in the shader

Now make the card show it. Four steps, each visible.

## 10.1 Read the map

**Achieve.** A struct holding the four values, sampled once, scaled by a global amount and quantised
into visible steps.

**Work out.**

- Every read should be an **explicit LOD 0**. Two separate reasons — one about the texture, one about
  where in the code the sampling sits. Find both.
- Quantising with `floor` versus `round`: do both and watch what happens at zero. One of them leaves
  a ghost of the texel behind instead of clearing it. This is only learnable by looking.
- Sample wear at the **same snapped UV as the artwork**, so wear texels land on art texels. But the
  height taps in 10.4 must use the **raw** UV. Work out why the ramp helps in one case and hurts in
  the other.

**Test.** Debug view 6: R = scuff, G = ink, B = missing. Age a card and confirm all three channels
have content and land where 9.3 put them.

## 10.2 Ink loss

**Achieve.** Print that fades toward the bare card stock underneath.

**Work out.** Ink coming off does **two** things to the colour, in an order. What are they, and which
comes first? Also: what colour is underneath, and why is that a tunable property rather than white?

**Test.** Do it in **both** orders and compare side by side on your two-card rig. One reads as chalky
worn print; the other reads as a card that was washed. Decide which is right and be able to say why.

## 10.3 Chips

**Achieve.** Chips leave actual holes you can see the background through.

**Work out.**

- A chip is material that is not there. What mechanism in your shader **already** removes a pixel
  completely? Use it — do not invent a second one.
- Following from that: what else must change, in the other pass? **Predict what you will see if you
  forget**, then forget it deliberately and check you were right.

**Test.** Age hard. Chips are visible holes with the background through them, from **both** faces,
and they do not flicker against anything drawn behind the card. Put a bright object behind the card
and turn it — flicker means the depth pass disagrees.

## 10.4 Height → normal → the payoff

The best step in this document. Everything so far has been building toward it.

**Achieve.** Dents and creases that **break the foil over them** — the rainbow bends, the sweep
fractures, the sparkle fires different flakes — with **no line of foil code knowing wear exists**.

**Work out.**

- A per-pixel tangent-space normal from a height field: how do you get a slope from heights sampled
  at neighbouring texels? What is one texel, in UV?
- Then the whole point: **where does that normal go?** Look at what you wrote in Milestone 7.2 and
  find the one thing that is currently a constant and should not be. If you followed the constraint
  in 7.2, this is a one-line change.
- On the back face, **two** separate things flip the normal: the mirrored UV from Milestone 5, and
  the fact that a dent seen from behind is a bump. Work out what each does to the tangent-space xy
  and what the two together come to.
- Make it deliberately **blocky** — one normal per card texel, not smoothed. Then smooth it and look.
  Work out why the blocky version is right for this card specifically.

**Test, in three escalating steps.**

1. Debug view 3, now showing the *perturbed* normal: flat paper is one flat colour, dents and creases
   push it off it.
2. **The real test.** Age a card, Play mode, turn it slowly, watch a crease. The rainbow must
   **bend** along it. The sweep must **fracture** across it. If a dent only *darkens* the card, your
   normal is being computed and not used.
3. `grep` your foil code for "wear". The rainbow, sweep, sparkle and chrome sections must contain
   **no hit at all** — and still break over dents. If they do mention it, you have plumbed damage
   into each effect by hand and missed the entire point of the design.

## 10.5 Scuff

**Achieve.** Abraded patches stop foiling — and scratches are still visible on a **common** card that
has no foil at all.

**Work out.** Two separate additions, and their **order relative to the foil intensity multiply**
matters. Reason it out: a common card has zero foil intensity. What happens to anything you add above
that multiply? What does that mean for where the scratch's own haze and glint have to go?

**Test.** Two cards, same damage, one Holo and one Common. On the Holo, scuffed patches go dead. On
the Common — which has no foil to lose — the scratches must still be clearly visible. If the Common
card looks untouched, your ordering is wrong, and this is the only test that catches it.

---

# Milestone 11 — Bending

Low frequency, geometric, in the vertex shader — because it must change the **silhouette**, and no
amount of shading will do that.

## 11.1 Bow, position only

**Achieve.** Two parameters bow the card across x and y. Position only; ignore normals for now.

**Work out.** What is the simplest function that is 0 at both edges and maximal in the middle?

**Test — and the test is the point of the milestone.** Look at the card nearly **edge-on** against a
contrasting background in Scene view. The **outline** must curve. If the card only shades differently
but its silhouette stays straight, you have written a shading effect, not a bend.

Also: recall the bounds you set in 2.4. Set the bow to maximum and pan the camera to the screen edge.
If the card pops out of view, your headroom is too small — and you have now seen that bug on purpose,
twice.

## 11.2 Corner folds

**Achieve.** Four parameters, one per corner, folding that corner up over a tunable radius.

**Work out.** The falloff needs **smooth derivatives at both ends**, or the fold shows a visible
crease where it meets the flat part of the card. Which standard interpolation curve has that
property?

**Test.** Fold one corner, look edge-on: the corner lifts, and there is no hard line where the fold
blends back to flat. Fold all four and confirm they are independent and land on the corners you
expect — mixing up the order of a four-component vector is easy and produces a card that bends at the
wrong corner.

## 11.3 Rebuild the frame

**Achieve.** Normal and tangent rebuilt from the bend, so lighting and foil follow the bent surface.

**Work out.** This is the one piece of real calculus here.

- You have displaced the surface along its normal by `h(u,v)`. Write the two surface tangents `dP/du`
  and `dP/dv` in terms of T, B, N and the slopes of `h`.
- Take their cross product and simplify.
- **Where do the card's world dimensions enter?** Work this out carefully. Leave them out and the
  shading is wrong by the card's aspect ratio — which looks like a subtly wrong bow rather than a
  bug, and is therefore easy to ship.
- To get the slopes you sample `h` at a small offset. How small? Too tight and you sample the wear
  map's own texel noise; too wide and you miss a fold. What sets the floor?
- **Where in the vertex shader** must the bend happen — before or after you transform the position
  and build the frame? Only one order gives you the *bent* card's frame.

**Test.** Bow the card hard, Play mode, turn it. The foil's sweep bar must now **curve** as it
crosses the bow rather than staying straight. Debug view 3 shows the normal fanning across the bend
rather than staying constant. If the silhouette bends but the shading does not, you have done 11.1
and not 11.3.

## 11.4 Creases move the paper

**Achieve.** The wear map's height channel displaces the mesh too, not just the shading from 10.4.

**Work out.** This vertex-stage tap must use a **linear** filter, unlike the point sampling the
fragment stage uses. Work out why — think about what a fold looks like when it stair-steps across a
16 × 24 grid.

Also: your `#pragma target` now has to go up. Why — what did you just start doing in the vertex
stage? Find the minimum tier that guarantees it.

**Test.** Crease a card, look edge-on. The fold is visible in the **outline**. Compare against 10.4
alone (crease shading with no displacement) side by side — that comparison is what tells you whether
the displacement is worth its cost.

## 11.5 Depth agreement

**Achieve.** The depth pass bends **identically** to the forward pass.

**Test.** Put an object intersecting the bowed card. Any disagreement shows up as z-fighting along
the bend — a shimmering band that moves as the camera moves. Then bow the card while watching its
shadow or its depth-buffer silhouette in the Frame Debugger.

**Goes wrong as.** The two functions drifting apart over time as you tune one and forget the other.
Make them share code rather than duplicating it, and note this as a permanent invariant.

---

# Milestone 12 — The pack tear

A wrapper that rips open and peels back. The insight: **the rip is in the mesh, the peel is in the
shader.** Raggedness is baked into geometry once; the animation is two scalars.

## 12.1 A flat pack

**Achieve.** A quad with the wrapper art, using your atlas approach (see 13.1 — do that first if you
want one material to draw many wrappers).

**Test.** The wrapper renders, correct size, correct way up.

## 12.2 Split it in two along a ragged seam

**Achieve.** Two meshes — body and lid — cut along a shared ragged line, generated as a run of
**column quads with flat tops** so the rip is a pixel staircase rather than a smooth diagonal.

**Work out.**

- Both halves must share **one** seam array. Why — what happens if each generates its own?
- The seam needs **two scales of noise**. A straight cut with fine jitter on top reads as a print
  artefact rather than a rip. What does the coarse scale add?
- Snap the seam to whole pixels. Why?
- Pin it flat at both ends. What does the pack look like while still sealed if you do not?

**Test.** Separate the two meshes by a visible gap in the Scene view. The two edges must **interlock
exactly** — every tooth on one matches a notch on the other. Then bring them together and confirm no
hairline gap shows along the seam at any camera angle. (One pixel of deliberate overlap is a
legitimate fix; work out why it is needed.)

## 12.3 The peel

**Achieve.** The lid hinges on the seam and curls toward the viewer, driven by parameters.

**Work out.**

- The mesh must carry an extra per-vertex value: a **hinge ramp**, 0 at the tear line and 1 at the
  far edge. Work out why this must be a **shared linear function of height** rather than a per-column
  0..1 ramp. Neighbouring columns are cut at different heights — what happens on their shared edge if
  each has its own ramp? This is the least obvious thing in the milestone.
- Following from that: the shader must **not** clamp this value, even though it sometimes falls
  outside 0..1. Why?
- Three parameters, not one: how far the tear has opened across the width, how far the flap lifts,
  and how much it curls. Work out why one number cannot produce a flap that both rises and rolls.

**Test.** Animate the peel parameters in Play mode (or just drag them in the inspector). The flap
lifts and rolls toward the camera, hinging at the seam. Then look along the seam at a shallow angle
in perspective: **no hairline cracks between columns**. That failure only appears in perspective, at
certain angles — check specifically.

## 12.4 The torn edge and the sheen

**Achieve.** A bright torn lip along the cut with stepped shading behind it, and a foil sheen sliding
across the wrapper as it tilts.

**Work out.**

- The lip must be stepped onto **whole texture pixels**. Work out why a smooth falloff is wrong here
  — what does it look like against pixel art?
- The sheen is three things you have already built: the bar from 8.3, the spectrum from 8.1, and the
  luminance idea from 8.7. Work out how they combine and why the luminance term is there.

**Test.** Tear the pack slowly. The lip is exactly one pixel bright and the shading behind it steps
down in whole pixels — zoom in and count them. The sheen sweeps as you tilt and does **not** light up
the dark ink.

---

# Milestone 13 — The atlas quad

The simplest shader of the three and the one you will reuse most: one cell of a sheet on a quad,
picked by UV rect rather than by slicing the sheet into sprites.

## 13.1 Draw one cell

**Achieve.** A shader that takes a rect (offset + size) and draws that region of a sheet on a quad,
with the pixel snapping from 6.2.

**Work out.** Author the mesh's UVs in **cell space** (0..1 over one cell) rather than atlas space.
Work out what that buys you — specifically, what it lets a `MaterialPropertyBlock` do that slicing
into sprites does not.

**Test.** Step the rect through several cells from the inspector. Each cell draws correctly with no
part of its neighbours visible.

## 13.2 The clamp

**Achieve.** Sampling clamped to the cell's outer texel **centres**, not its edges.

**Work out.** Point filtering would not need this; an atlas does. Work out the failure: if your art
runs right up to the top of its cell, what does a sample exactly on the boundary blend in?

**Test.** Put art that touches the cell edge next to a contrasting cell on the sheet. Without the
clamp you get a one-pixel fringe of the neighbour along that edge; with it, clean. Zoom right in —
this is a one-pixel bug and you will not see it otherwise.

**Work out, finally.** This shader has **no keywords at all**. Work out why that is right here and
wrong for the card.

---

# Appendix — Test recipes

Procedures referenced above, in one place.

**Wireframe / shading modes.** Scene view toolbar, the leftmost drop-down. Wireframe shows your mesh
topology; Shaded Wireframe shows both at once and is what you want for checking a subdivision.

**Frame Debugger.** `Window > Analysis > Frame Debugger`, Enable. Click through the draw calls to
find your card. It tells you the shader, the pass, the keywords in use, the property values, and
whether the SRP Batcher accepted the draw. When a keyword "does nothing", this is where you find out
it is not actually set.

**Counting draw calls.** Same window, or the Stats overlay in Game view. Two cards on one material
should be one batch; if they are two, something instanced the material.

**Comparing two states honestly.** Two objects side by side, in the same frame, is the only reliable
comparison. Your memory of what the card looked like before the change is not evidence — the eye
adapts within seconds.

**Screenshot diffing.** For "must be exactly identical" claims (8.7's mask test), take two Game view
screenshots at the same camera position and diff them in an image editor. "Looks the same" fails to
catch a 2% difference that will matter later.

**Checking a value in the shader.** Add a debug view rather than guessing. Build the view *before*
the feature that needs it wherever you can — 6.1 and 7.2 are laid out that way on purpose. Rules for
debug views: hand values **in** from the main path rather than recomputing them (a view that
recomputes its subject is free to drift from it), take any derivatives **before** any branching, and
switch on a **uniform** so a card at 0 pays nothing.

**Play mode versus Scene view.** Anything static — sampling, masks, compositing — is testable in
Scene view with no Play. Anything **view-dependent** needs the card *turning*, which means Play mode
or dragging in Scene view. If you find yourself judging the foil from a still image, stop.

---

# Appendix — Where to look things up

Names to search for, by milestone. These are vocabulary, not answers.

| Milestone | Look up |
|---|---|
| 0 | Project Settings → Player, Graphics; URP asset |
| 1 | `AssetPostprocessor`, `OnPreprocessTexture`, `TextureImporter`, `mipMapsPreserveCoverage`, `importSettingsMissing` |
| 2 | `Mesh` (vertices, uv, normals, tangents, triangles, bounds), `AssetDatabase.CreateAsset`, `MenuItem` |
| 3 | URP `Core.hlsl`, `TransformObjectToHClip`, `GetVertexPositionInputs`, `GetVertexNormalInputs`, `TEXTURE2D`/`SAMPLER`, `CBUFFER_START(UnityPerMaterial)`, `TRANSFORM_TEX`, `LightMode` tags |
| 4 | `MaterialPropertyBlock`, `Shader.PropertyToID`, `sharedMaterial` vs `material` |
| 5 | `GetWorldSpaceViewDir`, `Cull Off` |
| 6 | `fwidth`, `ddx`/`ddy`, `shader_feature_local_fragment`, `EnableKeyword`, `SAMPLE_TEXTURE2D_GRAD` |
| 7 | tangent space, parallax offset |
| 8 | `frac`, `floor`, `smoothstep`, `reflect`, Rec.601 luminance weights |
| 9 | `Texture2D` constructor (linear flag), `FilterMode`, `TextureWrapMode`, `GetRawTextureData`, `Texture2D.Apply`, `System.Random` |
| 10 | `SAMPLE_TEXTURE2D_LOD`, central differences |
| 11 | `#pragma target`, vertex texture fetch, `sampler_LinearClamp` |
| 12 | `Mesh.uv2`, `smoothstep` |
| 13 | atlas UV rect, texel-centre clamping |

---

# Appendix — If you are stuck, read upward

The dependency chain. When several things look wrong at once, fix the highest broken one first.

```
import settings (1.3) ──> snapping (6.2) ──> mips (6.3)
                                         │
mesh (2.2-2.4) ──────────────────────────┼──> bend (11.1-11.3) ──> depth (11.5)
                                         │
faces (5) ──> tilt (7.2) ────────────────┴──> foil layers (8.2-8.6)
                   ▲                                    │
                   │                              mask (8.7) ──> composite (8.8)
                   │                                    ▲
map data (9.2-9.4) ──> read (10.1) ──> normal (10.4)    │
                          │                  │          │
                          ├──> ink (10.2)    └──> rewires tilt
                          ├──> chips (10.3)
                          └──> scuff (10.5) ────────────┘
```

- Foil wrong **everywhere** → suspect tilt (7.2) before any individual layer.
- Foil wrong **only where the card is damaged** → suspect 10.4.
- **Artwork itself** wrong → nothing below 6.3 matters yet; fix 2.x and 3.x first.
- Something works in Scene view and not in Play mode → suspect a property block, an `Awake`, or a
  value you set by hand in the inspector and never in code.
