# Card Shader Lab

Rebuild this card's shader yourself, from an empty file, one effect at a time.

This is **not** a tutorial. `CARD_WALKTHROUGH.md` explains the finished shader; this makes you write
it. Every lab gives you a goal, the rules the result has to obey, and a way to prove you got it —
and no solution. The answer is already in the repo, so the only thing standing between you and it is
you not opening that file yet. **That is the whole exercise.** A lab you looked up teaches nothing.

## How a lab is laid out

| Section | What to do with it |
|---|---|
| **Goal** | What is on screen when you are done |
| **Rules** | Non-negotiable. They are what forces the right shape |
| **Work out first** | Answer these *before* typing. In writing, in your own words |
| **Verify** | How to prove it, not how it feels |
| **Done when** | Acceptance criteria |
| **Nudge** | Read only when stuck 20+ minutes. Names a tool, never a line |
| **Compare** | The file to open **after** it works. Diff your thinking, not just your code |

## Ground rules

1. **Do not open the `Compare` file before the lab works.** Not "for a peek at the signature".
2. **Do not open `CARD_WALKTHROUGH.md` Part 1–3 while working.** Part 0 is fair game any time —
   it is background, not answers.
3. `CARD_SYSTEM.md` **is** allowed throughout. It documents what knobs exist and what they mean,
   which is a spec, not a solution.
4. Write your answers to **Work out first** down. If you cannot write the sentence, you do not know
   it yet, and the code you are about to type will be cargo cult.
5. When a lab fails, **find out why before changing anything**. Every lab has a Verify step that
   isolates it. Poking parameters until it looks right is how you end up with a shader you cannot
   change later.

## Prerequisites

- Unity 6000.3.17f1, URP 17.3 — the project as it stands.
- You have run `Tools > Cozy TGC > Build Demo Scene` once, so `Card.prefab`, `CardQuad.asset` and
  the `Card_*` materials exist.
- You can read HLSL syntax. You do not need to know URP.

---

# Lab 0 — A card on screen, nothing else

## Setup, and why these names

Create three files. Everything you write for the rest of this document lives in them.

| File | What it is |
|---|---|
| `Assets/Shaders/CardLab.shader` | Your shader. Name it `Cozy TGC/Card Lab` |
| `Assets/Shaders/CardLabInput.hlsl` | Your CBUFFER, samplers and math |
| `Assets/Materials/Card_Lab.mat` | Your material |

Then a scene `Assets/Scenes/CardLab.unity` with a camera, one GameObject with a `MeshFilter`
(`Assets/Meshes/CardQuad.asset`), a `MeshRenderer` on `Card_Lab.mat`, and nothing else yet.

Two constraints from `CLAUDE.md` you are working around on purpose:

- `Assets/Materials/` and `Assets/Meshes/` are **generated** — the builders delete and recreate what
  they own. `Card_Lab.mat` is a new name, so nothing owns it. Never edit `Card_Holo.mat` for a lab.
- The four demo scenes are generated too. `CardLab.unity` is not one of them.

## The lab

**Goal.** The front artwork of a Spanish deck card, drawn on the quad, correct way up, no filtering
mush, no foil. Rotate the object in the Scene view — the back of the quad shows the *back* artwork,
mirrored correctly.

**Rules.**

- `Cull Off`. One quad, two faces, one shader.
- Which face you are looking at must be decided **geometrically** — from the surface normal against
  the view direction — not from `SV_IsFrontFace` and not from triangle winding.
- The card mesh faces **−Z** (`CARD_SYSTEM.md`, Orientation convention). Its tangent frame is
  T = +X, B = +Y, N = −Z. Get this backwards and the artwork mirrors.
- `RenderPipeline = UniversalPipeline` in the SubShader tags, and the `LightMode` tag on the pass.
- Every material property must sit in **one** `CBUFFER_START(UnityPerMaterial)` block, in the
  `.hlsl`, or the SRP Batcher silently stops batching your card.

**Work out first.**

1. Unity's `Create > Shader > Unlit Shader` menu gives you a **Built-in pipeline** template. What
   specifically about it fails under URP? Name three things it is missing or has wrong.
2. What does `dot(N, V)` tell you, and why is its *sign* the face test rather than its magnitude?
3. When you flip to the back face, the u coordinate has to be mirrored. Why? Draw the quad, its uv
   square, and the camera on both sides, and convince yourself with the picture rather than by
   trying both and keeping the one that looks right.
4. What is `TRANSFORM_TEX` for, and what breaks if you skip it?

**Verify.**

- Put a texture on `_FrontTex` and a visibly different one on `_BackTex`. Spin the object 180° in
  the Scene view. The swap must happen exactly at edge-on, not before or after.
- Put a texture with readable text on the front. Rotating the card must never mirror the text.
- Open **Frame Debugger** (`Window > Analysis > Frame Debugger`). Your card must be drawn by *your*
  shader, in one draw call. If it says "SRP Batcher not compatible", your CBUFFER is wrong — fix it
  now, not later, because it only gets harder to find once there are 40 properties in it.

**Done when.** Front and back are right, the SRP Batcher accepts it, and you can state why the face
test cannot use winding order.

**Nudge.** `GetWorldSpaceViewDir`, `GetVertexPositionInputs`, `GetVertexNormalInputs`, all from
`Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl`. Read that file — it is the
whole vocabulary of every lab below.

**Compare.** `Assets/Shaders/CardHolo.shader`, the `CardForward` pass, down as far as the `base`
assignment. And `Assets/Shaders/CardHoloInput.hlsl`, the CBUFFER.

---

# Lab 1 — Cutout, and the hole it leaves in depth

**Goal.** The transparent border of the card art is gone. The card sorts correctly against other
cards with no transparency sorting artefacts.

**Rules.**

- Alpha **cutout**, not blending. `RenderType = TransparentCutout`, `Queue = AlphaTest`, `ZWrite On`.
- A second pass named `DepthOnly`, `LightMode = DepthOnly`, `ColorMask R`.
- Both passes must clip on **exactly the same condition**.

**Work out first.**

1. Why cutout rather than `Blend SrcAlpha OneMinusSrcAlpha` for a card? Give the reason in terms of
   what the depth buffer ends up containing.
2. What is the `DepthOnly` pass *for* in URP? What consumes it? (Find at least one consumer by name.)
3. If the two passes clipped on different conditions, what would you see, and when?

**Verify.** Put a second card behind the first, overlapping, slightly rotated. Then in the Frame
Debugger step to the depth prepass and look at the card's silhouette.

**Done when.** Both passes clip identically, and you can name what reads DepthOnly.

**Nudge.** The trap is not writing the pass. It is keeping it in step with the forward pass forever
after. Write yourself a comment now saying so — you will add three more things to that condition
before Lab 16.

**Compare.** The `DepthOnly` pass of `CardHolo.shader`.

---

# Lab 2 — Pixel snapping

This is the lab that makes it pixel art. Do not skip it because it "looks fine already".

**Goal.** The 73×113 artwork stays crisp at any card size and any rotation, with **no crawling and no
shimmer** while the card turns, and no jagged stair-steps either.

**Rules.**

- Bilinear filtering stays **on** in the texture import settings. You are not allowed to solve this
  with point filtering.
- The snap must be a `shader_feature_local_fragment` keyword called `_PIXELAA`, so you can toggle it
  and see the difference. It must be declared in **both** passes.
- One screen pixel. Not "a small number" — derive it.

**Work out first.**

1. Point filtering is the obvious answer and it is wrong here. Turn it on and rotate the card
   slowly. Describe precisely what you see on the diagonal edges of the pips.
2. Bilinear is the other obvious answer and it is also wrong. Describe what *that* looks like.
3. So you want texel centres almost everywhere, and a ramp exactly at the seams. How wide should the
   ramp be, in screen pixels, for the result to be neither aliased nor blurry?
4. `fwidth(p)` where `p = uv * texSize`: what are its **units**? Write the sentence "one unit of
   `fwidth(p)` means ______ per screen pixel." This one sentence is the entire lab.
5. Given the answer to 4, what does dividing an offset-from-texel-centre by `fwidth` get you, and
   why does clamping the result to ±0.5 produce the ramp you want?

**Verify.**

- Toggle the keyword on and off at runtime. **Careful:** setting the float property alone does
  nothing — a keyword needs `Material.EnableKeyword`/`DisableKeyword` set together with it. See
  `CardDemoBuilder.SetToggle`. Getting this wrong is the single most common way to spend an hour on
  a shader that "does not react to anything".
- Rotate the card continuously for 10 seconds. Watch a diagonal edge. Nothing may crawl.
- Zoom the camera out until the card is ~30 px tall. It must not sparkle with aliasing.

**Done when.** You can state the `fwidth` sentence from memory, and both keywords are declared in
both passes.

**Nudge.** `floor(p + 0.5)` gives you the nearest texel centre in texel units. Everything else is
what you do with `p - that`.

**Compare.** `CardPixelUV` in `CardHoloInput.hlsl`. Also `SheetAtlasUV` in `SpriteSheetInput.hlsl`,
which does the same thing plus one extra clamp — work out what that clamp is for before you read
the comment above it.

---

# Lab 3 — The mip trap

Do this immediately after Lab 2, while your snapping is fresh. It is a bug you have already written
and cannot see yet.

**Goal.** The card, minified to ~40 px tall, picks a sane mip level and looks like a small card
rather than noise.

**Work out first.**

1. How does the hardware decide which mip level to sample? What does it use, exactly?
2. Your snapped UV from Lab 2 is deliberately **discontinuous** at every texel seam. What does that
   do to the answer to question 1?
3. So you need to sample at one UV but with the derivatives of a *different* UV. Which URP sampling
   macro lets you pass gradients explicitly?

**Verify.** Zoom out until the card is small on screen and pan the camera. Then write yourself a
debug view (you will formalise this in Lab 12) that outputs `log2` of the texel density —
card texels per screen pixel — as a colour, and check it varies smoothly across the card rather than
flickering per texel.

**Done when.** The artwork is sampled with the *untouched* UV derivatives, and you can explain in
one sentence why passing the snapped UV's derivatives is wrong.

**Compare.** The `ddxUV` / `ddyUV` lines in `CardHolo.shader`'s `Frag`, and debug view 9 in
`CardDebug.hlsl`.

---

# Lab 4 — Tilt: the one vector

Everything in Part 2 hangs off this. If tilt is wrong, five effects are wrong and you will debug the
wrong five.

**Goal.** A single `float2` that is **zero** when the card faces the camera and grows as you turn it
away, bounded so it never blows up at grazing angles.

**Rules.**

- Tangent space. You need T, B, N in world space in the fragment stage — the vertex stage has to
  pass them.
- The face flip from Lab 0 has to flip the frame too, or the back face's foil runs backwards.
- Must not explode at grazing angles. `1/x` where `x → 0` is not acceptable.
- Exposed as `_TiltGain`.

**Work out first.**

1. What is the tangent frame, and why can you not just use the world-space view direction? Answer in
   terms of what happens when you *move the card* versus when you *turn* it.
2. Express V in tangent space. What are the three dot products?
3. You want the component of V that lies **along the card's surface**, divided by how head-on you
   are. Write that as a formula. What is it geometrically? (It is the same construction as a
   parallax offset — that is not a coincidence.)
4. Two separate things stop it exploding: a floor under the divisor, and a soft saturation of the
   result. What does each one do on its own, and why do you want both?

**Verify.** Output `tilt * 0.5 + 0.5` as the colour. It must be flat grey at the centre of the card
when the card faces you, and it must move as you turn. Turn the card to nearly edge-on: the colour
must go somewhere and stay there, not go white and blow out.

**Done when.** Grey at rest, bounded at grazing, mirrored correctly on the back face.

**Nudge.** In Lab 16 the `N` you build this against stops being `(0,0,1)`. Write the maths so that
substituting a per-pixel normal changes nothing structurally — if you hardcode the flat case now you
will rewrite this lab later.

**Compare.** The `vT` / `ndv` / `tilt` block in `CardHolo.shader`, and debug view 4 in
`CardDebug.hlsl`.

---

# Part 2 — The foil, one layer at a time

Five effects. Build them **in this order**, and after each one set the previous ones' strengths to
zero and look at the new one alone. A foil built all at once is a foil you cannot tune.

---

# Lab 5 — Rainbow diffraction

**Goal.** Bands of spectrum across the card that shift hue as it turns, quantised to a small number
of steps so they read as pixel art rather than as a gradient.

**Rules.** Properties: `_RainbowStrength`, `_RainbowScale`, `_RainbowAngle`, `_RainbowTilt`,
`_RainbowDepth`, `_RainbowSteps`, `_RainbowDrift`, `_RainbowSat`. All of them must do something
distinguishable — if two of your knobs are the same knob, the maths is wrong.

**Work out first.**

1. You need a hue → RGB function cheap enough for a fragment shader. The standard trick is three
   cosines at 120° phase offsets. Derive why that produces something spectrum-like, and what its
   saturation and mean are.
2. Bands at an angle across the card: how do you turn `_RainbowAngle` and a uv into a scalar that
   increases across the card in that direction? (One dot product.)
3. Now three separate things must move the bands: position across the card, tilt, and time. What is
   each one's job? Which one makes the effect *view-dependent* — and what do the other two do that
   the first cannot?
4. `_RainbowDepth` shifts the *sampling position* by tilt, on top of that. What does that add that
   phase-shifting alone does not? (Think about what a hologram sitting *under* a surface looks like.)
5. Quantising hue to `_RainbowSteps`: `floor` or `round`? Try both. Which one gives you steps that
   are even, and which gives you a half-width step at each end?

**Verify.** Set every other layer to 0. `_RainbowSteps = 8`. Turn the card. You should get discrete
bands that march across it. Set `_RainbowTilt = 0`: the bands must stop reacting to rotation but
stay on the card. Set `_RainbowDrift` up: they must move on their own with the card still.

**Done when.** Each of the eight knobs has a visible, distinct effect you can demonstrate.

**Compare.** `CardSpectrum` and section 1 of `CardFoil` in `CardHoloInput.hlsl`.

---

# Lab 6 — Sweep

**Goal.** A soft specular bar that slides across the face as the card turns, and that is parked near
an edge when the card is at rest.

**Work out first.**

1. Same "distance along a direction" scalar as Lab 5. What changes is what you do with it.
2. You want a soft bar, not a hard line. A Gaussian-ish falloff: write one and identify which
   constant controls its width, and what the width means in uv units.
3. Why does the bar need a **rest offset**? Put `_SweepOffset = 0` and turn the card through its
   whole range. What do you lose?

**Verify.** `_SweepStrength` up, everything else 0. Turn the card slowly through its full range. The
bar must cross the entire face — enter one edge, leave the other. If it only ever sits in the middle
and wobbles, your offset and travel are wrong.

**Done when.** The bar crosses the whole card over the card's usable rotation range.

**Compare.** Section 2 of `CardFoil`.

---

# Lab 7 — Sparkle

The hardest of the five. Budget an evening.

**Goal.** Glitter flakes that **pop in and out** as the card turns, rather than a static sparkle
texture that slides around.

**Rules.**

- Procedural. No sparkle texture.
- Each flake fires only when the card is tilted *towards that particular flake*.
- Must not be a shimmering mess when the card is still.

**Work out first.**

1. You need a stable pseudo-random `float2` from a `float2`. Write a hash. Test it: fill the screen
   with `hash(floor(uv*n))` and confirm there is no visible structure — no diagonal banding, no
   repeats. Most one-liners you will find fail this. Check yours before you build on it.
2. Cell-based point placement: divide uv into cells, put one flake per cell at a hashed position
   inside it. Why must you check the **9** neighbouring cells rather than only the one you are in?
   Draw the failure case.
3. A flake's brightness needs two independent factors. One is "how close is this pixel to the
   flake". What is the other, and where does tilt enter?
4. Give each flake a random **preferred tilt**. What is `_SparkleSpread` then, in units of tilt?
5. Accumulating across the 9 cells: `max` or `+`? What does the wrong one look like when two flakes
   overlap?

**Verify.**

- Everything else at 0. Hold the card still: flakes must be steady, not fizzing.
- Turn the card slowly: individual flakes must **wink on and off** at different moments. If they all
  brighten together, your per-flake preferred angle is not being used.
- `_SparkleDensity` up to 80: no moiré, no grid you can see.

**Done when.** Flakes wink individually and the grid the cells sit on is invisible.

**Nudge.** `[unroll]` the two loops. And if you see a faint diagonal seam across the card, the
problem is question 1, not questions 2–5.

**Compare.** `CardHash22` and `CardSparkles` in `CardHoloInput.hlsl`.

---

# Lab 8 — Chrome

**Goal.** A mirror finish with a sky, a ground and a sun in it, that does **not** wash the artwork
out when the card faces you.

**Rules.** No cubemap, no reflection probe. Analytic, from the reflection vector and three colours.

**Work out first.**

1. Reflect the view direction about the surface normal. Which URP-space vectors do you need, and in
   which space does the reflection have to be computed for the sky to stay *up* when the card turns?
2. Sky above, ground below, from `reflectDir` alone: what is the one component you need and how do
   you map it to a 0..1 lerp factor?
3. A sun: a dot product against a direction, raised to a power. What does the exponent control, and
   why is a Range up to 256 the sensible cap here?
4. Head-on, a full mirror hides the print. Build a ramp that damps chrome when you look straight at
   the card and opens it up as it turns away. What does that ramp have to be a function of? (You
   already computed the quantity in Lab 4.)

**Verify.** Chrome only, artwork visible. Face the card at the camera: the print must still read.
Turn it 45°: the mirror must take over. The sun must be a tight highlight that sweeps, not a wash.

**Compare.** Section 4 of `CardFoil`.

---

# Lab 9 — Edge glow

**Goal.** A glossy rim that appears at grazing angles.

**Work out first.** You already have the quantity this is made of. Which one, and what is the
exponent doing? Write down what Fresnel actually is physically and why a `pow` of one dot product is
a defensible cheat for it.

**Verify.** Strength up, everything else 0. The rim must be on the *silhouette*, wherever that is,
and must vanish head-on.

**Compare.** Section 5 of `CardFoil`. Debug view 5 in `CardDebug.hlsl` shows the raw quantity.

---

# Lab 10 — Where the card is allowed to foil

**Goal.** Foil that lands on the print rather than smeared uniformly over the whole card, with an
optional explicit mask texture.

**Rules.** Two mechanisms, combined: an optional `_MaskTex` behind a `_MASKTEX` keyword, and a mask
derived from the **luminance of the artwork itself**, with `_MaskFromLuma`, `_MaskContrast`,
`_MaskBias`.

**Work out first.**

1. Why derive a mask from luminance at all, when you could just paint one per card? Count the cards
   in `Assets/Resources/Cards/spanish deck/` and answer again.
2. Luminance weights: why `0.299 / 0.587 / 0.114` and not `1/3` each?
3. Contrast and bias around a pivot: write the remap. Where is the pivot, and what happens to the
   mask when contrast goes to 8?
4. `_MaskFromLuma` at 0 must mean "no luminance masking at all". Make sure your formula actually
   degrades to 1.0 there, rather than to something close to it.

**Verify.** `_MaskFromLuma = 0` must be identical to the un-masked card — compare screenshots, not
impressions. Then raise contrast and watch the foil retreat onto the bright parts of the print.

**Compare.** The mask block in `CardFoil`.

---

# Lab 11 — Combining, and keeping it pixel art

**Goal.** Five layers into one card, still reading as pixel art.

**Rules.**

- `_FoilBlend` blends between **additive** and **screen** compositing. Both must be implemented.
- `_HOLOPIXELATE` keyword: the foil itself quantised to the card's own 73×113 grid.
- `_ColorSteps` quantises the foil's colour.

**Work out first.**

1. Write out additive and screen blending as formulas. Where do they differ most, and what does
   additive do that screen does not, on a bright print?
2. `_HOLOPIXELATE` snaps the uv the *foil* is computed at to texel centres — with no ramp, unlike
   Lab 2. Why is a ramp wrong here, when it was essential there?
3. So `_PIXELAA` and `_HOLOPIXELATE` are two different snaps applied to two different things. State
   what each one is applied to. Getting these confused is the classic mistake in this shader.
4. Both are **keywords**. `CLAUDE.md` says a material with the keyword off cannot be switched by a
   `MaterialPropertyBlock`. What does that force, if you want the same card both ways in one scene?
   (`Card_Explain_Smooth.mat` exists for exactly this reason — work out why before you look at it.)

**Verify.** Toggle each keyword, in both passes. Then run `Tools > Cozy TGC > Render Foil Preview` —
it renders five tiers at four angles to a PNG next to `Assets/`, without a human pressing Play. This
is the fastest feedback loop in the project; start using it now.

**Done when.** Both blends work, both keywords work in both passes, and the preview sheet renders.

**Compare.** The end of `CardFoil`, and the compositing in `CardHolo.shader`'s `Frag`.

---

# Lab 12 — Your own debug views

Out of order on purpose: you have been debugging blind for eleven labs. Now build the instrument and
notice how much faster Part 3 goes.

**Goal.** A `_DebugView` float that switches the fragment output between at least these five:
uv, the texel grid with the snapping ramp on it, the tangent-space normal, tilt, and N·V.

**Rules.**

- **A debug view must never recompute its subject.** Every value it draws is passed in from `Frag`.
  Work out why that rule exists — what exactly goes wrong on the day you refactor `Frag` and the
  debug view keeps its old copy of the maths?
- Off on shipped materials, and it must cost a card at `_DebugView = 0` **nothing**.
- Derivatives (`ddx`/`ddy`) must be taken before any branching. Find out why — the rule is about
  *uniform control flow*, and it will bite you in the density view specifically.

**Work out first.**

1. Why is a `if (_DebugView > 0)` branch on a uniform effectively free, while an `if` on something
   that varies per pixel is not? What does the hardware actually do in each case?
2. Given that, why does the wear code in Lab 13 onwards *also* not need a keyword?

**Verify.** Frame Debugger: the shipped card and the debug card must compile to the same variant
count. If adding debug views multiplied your variants, you reached for a keyword and should not have.

**Compare.** `CardDebug.hlsl`, all ten views, and the note at the top of it.

---

# Part 3 — Damage

Five kinds of damage. The lesson of this part is that they are **one texture**, and that one of the
four channels makes the other three effects work without any of them knowing damage exists.

---

# Lab 13 — Design the map

Before any code. This is a design exercise and it is the most valuable page here.

**The brief.** A card can be scuffed, can lose ink, can be dented and creased, and can have chips
missing from its edges. It must be restorable, per face, by rubbing. It is 73×113 pixels.

**Work out first, and write the answer down before reading on.**

1. You have one RGBA texture per face. Five kinds of damage, four channels. Which two share, or
   which one is not in the map at all — and why?
2. Which channels must be **per face** and which must come from the front map for *both* faces?
   Justify each from physics: what does a dent do to the other side of a piece of card? A chip?
3. Given your answer to 2, what does the `DepthOnly` pass see, and what breaks if a chip is only in
   the back map?
4. Height needs to be **signed** — dents and ridges — in an unsigned texture. What is your encoding
   and where is flat?
5. Import settings: sRGB or linear? Point or bilinear? Mips or no mips? Clamp or repeat? Give a
   reason for each, and for at least one of them the reason should be about the *neighbour taps* you
   will write in Lab 16.
6. One kind of damage in the brief **cannot** live in a texture at all. Which, and why? What has to
   change for it to work? (You will build it in Lab 17.)

**Verify.** Write your channel table out. Then open `CARD_SYSTEM.md`'s "Wear and restoration"
section — this is documentation, so it is allowed — and check your table against it. If you differ,
work out which is right and why before continuing. It is entirely possible your answer is better;
what is not acceptable is not knowing which.

**Compare.** The header comment of `Assets/Shaders/CardWear.hlsl`.

---

# Lab 14 — Reading the map

**Goal.** A struct holding the four values, sampled once, with a global `_WearAmount` scaling it and
a `_WearSteps` quantisation.

**Rules.**

- Explicit LOD 0 on every read. Work out why an implicit mip level is a bad idea here — there are
  two separate reasons, one about the texture and one about where the sampling sits in the code.
- `_WearSteps` quantises with `floor`, not `round`. Do both and find out what `round` leaves behind
  at zero. This one you can only learn by seeing it.
- Wear must be sampled at the **same snapped uv as the artwork**, so its texels line up with the art
  texels — except for the height taps in Lab 16, which must use the raw uv. Work out why the ramp
  helps in one case and hurts in the other.

**Setup.** You do not need to write the ageing generator — `CardWear.cs` already writes the maps.
Add `CardView` + `CardWear` to your lab card, call `Age(seed, severity)` from a two-line script, and
`CardWear` will bind `_WearTex`, `_WearBackTex`, `_WearAmount`, `_Bow` and `_CornerBend` onto the
renderer's property block for you. **Your property names are the contract** — spell one differently
and you get a white texture and no error. That is worth experiencing once, deliberately.

**Verify.** A debug view of the raw channels: R scuff, G ink, B missing. Age the card at severity 1
and check every channel has something in it.

**Compare.** `SampleCardWear` and `CardWearQuantize` in `CardWear.hlsl`.

---

# Lab 15 — Ink loss and chips

**Goal.** Print that fades toward bare card stock, and chips that leave actual holes.

**Work out first.**

1. Ink coming off a card does **two** things to the colour, in an order. What are they, and which
   comes first? Do it in the wrong order and describe the difference — it is subtle and it matters.
2. What colour is underneath? Why is that a property (`_StockColor`) and not white?
3. A chip is material that is not there. What existing mechanism in your shader already removes a
   pixel completely, from Lab 1? Use it — do not invent a second one.
4. Following from 3: what else has to change, in the other pass? What do you see if you forget?
   (Predict it first, then forget it on purpose and check you were right.)

**Verify.** Age a card hard. Chips must be visible **holes** with the background through them, from
both faces, and must not flicker against anything drawn behind them.

**Compare.** `CardApplyInkLoss` in `CardWear.hlsl`, the `clip` in `Frag`, and the `missing` handling
in the `DepthOnly` pass.

---

# Lab 16 — Height, and the payoff

The best lab here. Everything so far has been building toward it.

**Goal.** Dents and creases that **break the foil over them** — the rainbow bends, the sweep
fractures, the sparkle changes which flakes fire — with **no line of foil code knowing that wear
exists**.

**Work out first.**

1. You have a height field in one channel. You want a per-pixel tangent-space normal from it. How do
   you get a slope from a height field sampled at neighbouring texels? What is the texel step in uv?
2. Having built the normal, **where does it go?** This is the whole lab. Look at what you wrote in
   Lab 4 and find the one thing that is currently a constant and should not be.
3. On the back face, two separate things flip the normal: the mirrored u from Lab 0, and the fact
   that a dent seen from behind is a bump. Work out what each does to the tangent-space xy and what
   the two together come to.
4. This normal is deliberately **blocky** — one per card texel, not smoothed. Smooth it and see. Why
   is the blocky version the correct choice for this card?

**Verify.**

- A debug view of the tangent normal. Flat paper must be the familiar flat blue; dents push it off.
- Then the real test: age a card, turn it, and watch a crease. The rainbow must **bend** along it,
  the sweep must break across it. If a dent only darkens the card, your normal is being computed but
  not used, which means answer 2 is still wrong.
- `Tools > Cozy TGC > Render Wear Preview` renders one card per frame at a size where 73×113 damage
  is actually visible, and logs the grading ramp.

**Done when.** Grep your foil code for `wear`. Rainbow, sweep, sparkle and chrome must contain no
hit at all, and they must still break over dents.

**Compare.** `CardWearNormal` in `CardWear.hlsl`, and the `nTS` / `Nw` lines in `CardHolo.shader`.

---

# Lab 17 — Bending

**Goal.** A bowed card and folded corners that change the **silhouette**.

**Rules.**

- Vertex stage. `_Bow` (x, y) and `_CornerBend` (BL, BR, TL, TR) plus `_CornerRadius`.
- Creases from the height channel displace the mesh too, on top of the shading in Lab 16.
- The tangent frame must be **rebuilt** from the bend, in the vertex shader, before anything is
  transformed.
- Forward and `DepthOnly` must bend **identically**.
- The crease displacement reads the wear map with a **linear** filter, not the point one the
  fragment stage uses. Work out why before you write it.

**Work out first.**

1. Why can bending not be part of the wear map? Answer in terms of silhouette. (This is question 6
   from Lab 13 — check your old answer.)
2. A quad has 4 vertices. `CardQuad.asset` has 425. Why? State the rule about what a vertex shader
   can and cannot do.
3. A bow across the card: what is the simplest function that is 0 at both edges and maximal in the
   middle? Now the same for one corner, falling off over `_CornerRadius` — and it wants smooth
   derivatives at both ends, so what shape?
4. **Rebuilding the frame.** You have displaced along the normal by `h(u,v)`. The new surface has
   tangents `dP/du` and `dP/dv`. Write both out, take the cross product, and simplify. Where do the
   card's world-space dimensions enter, and what goes wrong if you leave them out?
5. Sampling `h` at a small offset to get the slope: how small? Too tight and you sample the map's own
   noise; too wide and you miss the fold. What sets the floor on that step?
6. `#pragma target` has to go from 3.0 to 3.5. Why? What exactly did you just start doing in the
   vertex stage?

**Verify.**

- Silhouette. Look at the card **edge-on** against a contrasting background. A bow must be visible
  in the outline, not just in the shading.
- Depth agreement: put something intersecting the bowed card. Any disagreement between the two
  passes shows up as z-fighting along the bend.
- Bounds: a bent card that pops out of view when the camera turns has mesh bounds that do not
  reserve room for the displacement. Find where that headroom is set.

**Done when.** The outline bends, and the two passes agree bit for bit.

**Compare.** `CardBendHeight`, `CardCornerBend`, `CardApplyBend` and `CardApplyBendPosition` in
`CardWear.hlsl`.

---

# Lab 18 — Prove it against the real thing

**Goal.** Your shader and `CardHolo.shader` produce the same card.

**How.**

1. Copy `Card_Holo.mat`'s parameter values onto `Card_Lab.mat` — the table in `CARD_SYSTEM.md`,
   "Material parameters", is the list.
2. Two cards side by side, same artwork, same seed, one on each shader. Rotate both together.
3. Point `CardPreviewRenderer` at your material and diff the PNGs.

Where they differ, the question is not "which looks better" but **which is right, and why**. Some of
your differences will be bugs. At least one, if you have been thinking, will be a defensible choice
the shipped shader made differently. Find both kinds.

**Then, and only then, read:**

- `CARD_WALKTHROUGH.md` Parts 1–3 — the same 36 steps, explained.
- `Assets/Scenes/CardExplainer.unity` (`Tools > Cozy TGC > Build Explainer Scene`) — the same 36
  steps with the card live in front of you, two cards side by side, one carrying a single layer.
- `CARD_WALKTHROUGH.md` Appendix B, "Traps this shader actually hit". Count how many you hit too.

---

# Stretch labs

Same rules, less scaffolding.

**S1 — The pack tear.** `CardPack.shader` peels a wrapper open along a curling edge, with a torn
edge highlight, from an atlas of 180 wrappers picked by uv rect. Work out: how a mesh authored in
cell space draws any cell; why the peel needs `_PeelFlat`, `_PeelLift` **and** `_PeelCurl` rather
than one number; why the pack samples mip 0 outright where the card goes through `_GRAD`.
*Compare:* `CardPackInput.hlsl`, and "How the wrapper tears" in `CARD_SYSTEM.md`.

**S2 — Atlas quads.** `SpriteSheet.shader` draws album books and shelf icons from one sheet with one
material. It has **no keywords at all**. Work out why that is right here and wrong for the card, and
what the extra clamp in `SheetAtlasUV` is protecting against. *Compare:* `SpriteSheetInput.hlsl`.

**S3 — The overlay.** `CardOverlay.shader` draws the card's own wireframe, vertices and tangent
frames — and it does **not** know where the card is. It includes `CardWear.hlsl` and runs the card's
own `CardApplyBend`. Build your own version. The design question: what makes an overlay *trustworthy*
as a teaching tool, and what would make it a liar? *Compare:* `CardOverlay.shader` and
`CardMeshOverlay.cs`.

**S4 — Write the ageing generator.** Everything above consumed the map. Now produce it: five passes
(edge wear, scratches, dents, creases, chips) on one shared random stream, CPU-side, uploaded whole.
Then the reverse — restoration tools that are nothing but rates, where the *ordering* of the repair
steps emerges from the rates rather than from any check enforcing it. *Compare:* `CardWear.cs`,
`RestorationTool.cs`, and "Restoring" in `CARD_SYSTEM.md`.

---

# Traps, listed without their solutions

You will hit most of these. When something is inexplicable, read this list before you read anything
else — it is faster than bisecting.

1. A keyword's float property set without the keyword itself. Silent, total no-op.
2. A keyword declared in the forward pass but not in `DepthOnly`. Shows up as a silhouette that
   disagrees with the colour.
3. A property outside `UnityPerMaterial`, or the CBUFFER's field order not matching. SRP Batcher
   quietly stops batching; nothing looks wrong until you profile.
4. `ddx`/`ddy` inside non-uniform control flow. Garbage or a compile error, depending on the target.
5. Sampling with the snapped uv's derivatives. Only visible when minified.
6. A vertex texture fetch at `#pragma target 3.0`.
7. The two bend functions drifting apart.
8. The wear texture imported as sRGB. Every rate is subtly wrong and nothing is obviously broken.
9. Mips on the wear map. Neighbour taps for the dent normal read a level that does not exist as you
   expect.
10. `wrapMode` left on Repeat. The dent normal taps at the card's border wrap to the far edge.
11. `_Time.y` in an effect you are trying to compare in a still screenshot.
12. Face flipping done with winding order rather than geometrically, then `Cull Off`.
13. A hash function with visible structure, discovered three labs after you built on it.

---

# Where things live

| | |
|---|---|
| `CARD_SYSTEM.md` | Reference — every knob, the parameter table, import rules. Allowed always |
| `CARD_WALKTHROUGH.md` | The explained version. Part 0 always; Parts 1–3 after Lab 18 |
| `Assets/Scenes/CardExplainer.unity` | The same 36 steps, live |
| `Tools > Cozy TGC > Render Foil Preview` | Five tiers, four angles, to a PNG. No Play mode |
| `Tools > Cozy TGC > Render Wear Preview` | One card per frame, plus the grading ramp in the log |
| `Window > Analysis > Frame Debugger` | Draw calls, variants, SRP Batcher compatibility |
