# The card shaders, without an engine

The current tutorial (`CARD_SHADER_BUILD.md`) rebuilt for a **shader preview app** instead of Unity:
same card art, same effects, same milestone-by-milestone order, but no engine underneath. You write
GLSL fragment shaders and watch them update as you type.

It covers everything the project's shaders do today, including the parts added since the Unity
tutorial was written — the pack's **cut light**, its **flutter**, the **pack flash** and the **slot
frame**.

Like `CARD_SHADER_BUILD.md`, no step hands you the shader code. Every step says what to achieve,
what you have to work out, and how to prove it in the preview. Tool configuration (file directives,
slider declarations) and **data** (atlas measurements, default parameter values) are given exactly,
because they are not what you are here to learn.

## What changes when there is no engine

Unity did a lot of this for you. Here you build it, and that is most of what makes this version
worth doing even if you have finished the Unity one.

| Unity gave you | Here you build |
|---|---|
| a mesh, a camera, and a vertex stage that places the card on screen | a camera and a **ray** per pixel, and a card you **intersect** — the vertex stage becomes arithmetic you write |
| a subdivided mesh the vertex shader could bend | a bent **surface** the ray has to find |
| mesh bounds for culling | an early-out box that has to be big enough — the same bug, in a new shape |
| materials and rarity tiers | slider uniforms and presets |
| `CardWear.cs` holding the damage map on the CPU | a **buffer pass** that generates and holds the map on the GPU |
| texture import settings | per-channel filter and wrap directives |
| a linear colour space, set once in Project Settings | colour conversion you write and apply yourself |
| a depth buffer and a `DepthOnly` pass | "which surface did this ray hit first", decided by you |
| `Blend` state on a material | compositing done by hand in the shader |
| the Frame Debugger | debug views, and nothing else |

## Where the Unity version is the reference

Keep the Unity project around. It is the answer you compare against.

- **The art** — the same PNGs, from `Assets/Resources/` (list in 0.3).
- **The numbers** — every default and range comes from the shipped shaders (`CARD_SHADERS_SOURCE.md`
  has them all) and the tier presets from `CardDemoBuilder.ConfigureTier`.
- **The look** — `Tools > Cozy TGC > Render Foil Preview` writes `CardFoilPreview.png`: five tiers
  at four angles. `Render Wear Preview` does the same for damage. Put your output beside those.

From the terminal, with the Unity editor **closed** (it locks the project):

```bash
/Applications/Unity/Hub/Editor/6000.3.17f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "/Users/markuller/Desktop/Unity/Other/CozyTCG/Cozy TGC-Collection" -logFile - -executeMethod CozyTGC.EditorTools.CardPreviewRenderer.RenderPreview
```

## The tool

**VS Code with the "Shader Toy" extension** (`stevensona.shader-toy`). Free, runs on macOS, and it
does everything below — each point checked against its README:

| Needed for | Extension feature |
|---|---|
| the card art | local images as textures: `#iChannel0 "file://assets/card.png"` |
| import settings | `#iChannel0::MinFilter "..."`, `::MagFilter "..."`, `::WrapMode "..."` |
| the wear map | other shader files as buffers: `#iChannel1 "file://wear.glsl"` |
| restoring (optional) | a pass reading its own last frame: `#iChannel2 "self"` |
| material knobs | sliders: `#iUniform float strength = 1.0 in { 0.0, 3.0 }`, `#iUniform color3 tint = color3(1.0)` |
| shared code | `#include "common.glsl"` (included files must not define `main`) |
| pixel snapping, mips | WebGL2 / GLSL ES 3.00, so `dFdx`, `fwidth`, `textureGrad`, `texelFetch` exist |
| turning the card | `iMouse`, `iTime`, `iFrame`, `iResolution` |
| key presses | `#iKeyboard`, then `isKeyPressed(Key_R)` and friends |

It uses the **Shadertoy dialect**: you write `mainImage(out vec4 fragColor, in vec2 fragCoord)`, and
the tool supplies `iResolution`, `iTime`, `iMouse` and the rest. Everything you write here also runs,
nearly unchanged, in the other Shadertoy-dialect tools:

| Tool | Fits? |
|---|---|
| **shadertoy.com** | The shader code, yes. But you cannot upload your own images, so not the card art. Good for sharing; no good for this tutorial. |
| **glslViewer** | Yes, and it runs real vertex shaders on OBJ / PLY / GLTF models with hot reload — the one to move to if you want Milestone 8 done with a mesh instead of a ray. Its file directives differ from the ones here. |
| **KodeLife** | A desktop app with passes and parameters. Plausible, but not checked for this tutorial, so you are on your own for its settings. |

## HLSL → GLSL

The Unity shaders are HLSL. Everything you compare against is written in it, so learn to read across.
The ones marked **trap** compile fine and give you the wrong answer.

| HLSL (Unity) | GLSL here | |
|---|---|---|
| `float2` `float3` `float4` `half4` | `vec2` `vec3` `vec4` | |
| `lerp(a, b, t)` | `mix(a, b, t)` | |
| `frac(x)` | `fract(x)` | |
| `saturate(x)` | `clamp(x, 0.0, 1.0)` | no built-in; write one |
| `fmod(x, y)` | `mod(x, y)` | **trap:** they disagree for negative `x`. `fmod(-1, 3)` is −1, `mod(-1.0, 3.0)` is 2 |
| `ddx` `ddy` | `dFdx` `dFdy` | |
| `SAMPLE_TEXTURE2D(t, s, uv)` | `texture(iChannel0, uv)` | one name: the channel is texture and sampler both |
| `SAMPLE_TEXTURE2D_GRAD` | `textureGrad` | |
| `SAMPLE_TEXTURE2D_LOD(..., 0)` | `textureLod(..., 0.0)` | |
| reading one exact texel | `texelFetch(iChannel1, ivec2(x, y), 0)` | no filtering, integer coordinates |
| `clip(x)` | `if (x < 0.0) discard;` — or, here, treat it as a miss | see 1.5 |
| `mul(M, v)` | `M * v` | **trap:** GLSL matrices are column-major. Build rotations and check them with a test, do not transcribe |
| `1.0.xxx` | `vec3(1.0)` | |
| integer literals in float maths | always write `1.0`, never `1` | **trap:** GLSL ES will not convert `int` to `float` for you |
| `_Time.y` | `iTime` | |

## The rules

Same as the Unity tutorial.

1. **Never write two steps' worth before testing.** When something breaks, it was the last thing.
2. **Predict the failure first.** Say what a wrong result would look like before you look.
3. **Compare after, not during.** `CARD_SHADERS_SOURCE.md` is the finished HLSL. Open it once a step
   works, to compare. Opening it during a step turns an hour of learning into five minutes of typing.
4. **When a test fails, find out why before changing anything.** Every milestone lists how it goes
   wrong.

## Step format

> **Achieve** — what must be true when you are done.
> **Work out** — what you have to decide or discover. This is the actual work.
> **Test** — the procedure, and what pass and fail look like.
> **Goes wrong as** — the specific failures, so you recognise them.

**Your one instrument** is a debug slider. From Milestone 2 on, declare
`#iUniform float debugView = 0.0 in { 0.0, 12.0 } step 1.0` and grow a list of views on it. There is no
Frame Debugger here; there is no debugger at all. A value you cannot draw is a value you cannot check.

---

# Milestone 0 — A workspace that shows a pixel

## 0.1 Install and prove it updates

**Achieve.** VS Code, the Shader Toy extension, and one `.glsl` file whose preview updates as you
type.

**Work out.** Find the extension's setting `shader-toy.webglVersion` and make it request **WebGL2**.
The default tries WebGL2 and quietly falls back to WebGL1 — and under WebGL1 half of this tutorial
(`textureGrad`, `texelFetch`, mipmaps on a 73 × 113 texture) does not exist. You want a loud failure,
not a quiet fallback.

**Test.** `fragColor = vec4(fragCoord / iResolution.xy, 0.0, 1.0);` fills the preview with a
black-red-green-yellow gradient. Change a number, and the preview follows without you saving or
restarting anything. Then write something GLSL ES 3.00 has and WebGL1 does not — `texelFetch` on any
channel — and confirm it compiles. If it does not, you are on WebGL1.

## 0.2 Colour space, by hand

In Unity you set "Linear" once and every texture was decoded and every output re-encoded for you.
Here, nothing is. The tool hands you the image's bytes as 0–1 values and writes your output straight
to the screen. Everything is "gamma" unless you make it otherwise.

**Achieve.** Two functions, one taking a colour from display (sRGB) values to linear, one back. From
here on: decode every texture sample and every colour constant you tune, do all maths in linear, and
encode once at the very end of `mainImage`.

**Work out.**

- The exact sRGB curve is piecewise (a linear toe and a power section). A plain `pow(c, 2.2)` is the
  common shortcut. Find the exact one, and decide whether the difference matters for pixel art.
- **Which values get converted?** Colours, yes. What about the alpha channel, the wear map (Milestone
  6), the tint sliders, a number like `0.5` meaning "half strength"? Get this wrong and every effect
  is subtly off with nothing obviously broken. The rule you want is about what the number *means*.

**Test — the same test as the Unity tutorial's 0.2, in shader form.** Draw three rectangles by
`fragCoord`:

1. white blended at 50% over black, blended in **linear**, then encoded,
2. the same blend done on the raw display values,
3. a reference of display value 128 (0.502).

Open **Digital Color Meter** (`/Applications/Utilities/`), aperture 1 pixel. Rectangle 1 should read
about **187**, rectangles 2 and 3 about **128**. If 1 and 2 match, your conversion is not happening.

**Goes wrong as.** Converting twice (decode on sample, *and* again on a constant that was already
linear). Nothing crashes; colours go muddy and dark. And converting alpha — it is coverage, not
light, and converting it changes where your cutout edge is.

## 0.3 Folders and art

**Achieve.** A workspace folder, outside the Unity project, holding the art and your shader files.

```
card-shaders/
  assets/
  common.glsl        shared functions, #included — no main, no mainImage
  card.glsl          the card (the image pass)
  wear.glsl          the damage map (a buffer pass, Milestone 6)
  pack.glsl          the booster wrapper (Milestone 10)
  sheet.glsl         atlas quad and slot frame (Milestone 11)
```

Copy these from `Cozy TGC-Collection/Assets/Resources/`, **renaming as you go** — the source folders
have spaces in their names and some files end in upper-case `.PNG`, both of which make `file://` paths
fragile:

| Copy | To | Size |
|---|---|---|
| `Cards/spanish deck/14.PNG` (or any number) | `assets/front.png` | 73 × 113 |
| `Cards/spanish deck/back.PNG` | `assets/back.png` | 73 × 113 |
| `Cards/tarot_free - monochrome/00.png` | `assets/front_tarot.png` | 73 × 113 |
| `Cards/CardPacks/CardPacks.png` | `assets/packs.png` | 849 × 3143 |
| `Collectors Albums/RADL_Book_red.png` | `assets/book.png` | 1330 × 160 |
| `Collectors Albums/exp book.png` | `assets/icons.png` | |

**Work out.** Which file does `#include "common.glsl"` resolve against — the including file's folder,
or the workspace root? Find out once now, with a function that returns a constant, rather than later
when a path error is buried under ten others.

**Test.** `card.glsl` includes `common.glsl`, calls a function from it, and draws its result.

---

# Milestone 1 — The card, placed by you

In Unity, a mesh, a camera and the vertex stage put the card on screen. Here you do it in the
fragment shader: for every pixel, fire a ray from a camera and ask whether it hits a rectangle. It
sounds heavier than it is — a handful of lines — and it gives you, per pixel, exactly what Unity's
vertex stage used to interpolate for you: a UV, a position, a normal, a tangent frame, and a view
direction.

## 1.1 A camera

**Achieve.** A ray origin and a normalised ray direction for every pixel, from a perspective camera
at `(0, 0, -2)` looking down +Z.

**Work out.**

- From `fragCoord` and `iResolution`, get a screen coordinate centred on the middle of the preview
  with a square aspect — so a circle drawn in it stays round when you resize the window.
- A field of view. What single number turns "screen coordinate" into "direction", and what happens
  to the picture as it grows?
- **Why perspective and not orthographic** — the Unity tutorial asked this in 0.4 and the answer
  matters more here, because you are about to *compute* the view direction instead of receiving it.
  With an orthographic camera, every ray has the same direction. What does a view-dependent foil
  have left to work with then?

**Test.** Draw the ray direction as colour (`dir * 0.5 + 0.5`). The centre of the preview is one flat
colour, and it shifts smoothly toward each edge. Resize the preview panel: nothing stretches.

## 1.2 A rectangle

**Achieve.** A function that intersects a ray with a card — a rectangle 0.73 × 1.13 units, centred on
a point, with its own orientation — and returns: did it hit, how far along the ray, the UV, the hit
position, and the card's frame (T, B, N).

**Keep the Unity convention**, or every later comparison with the reference project breaks: the card
at rest faces **−Z**, with **T = +X**, **B = +Y**, **N = −Z**, and UV `(0, 0)` at the bottom-left of
the face you see.

**Work out.**

- Ray–plane intersection: what is `t` in terms of the ray, the plane's point and the plane's normal?
  What happens to that expression when the ray runs parallel to the card, and what do you do about
  it?
- Once you have the hit point, how do you get UV from it? You need two dot products and the card's
  size — nothing else.
- Hits **behind** the camera are real solutions of the equation. Reject them.

**Test.** Return the UV as colour on hits and a grey background on misses — this is the Unity
tutorial's 3.2 test again, and it is still the most informative single test there is.

- Black corner bottom-left, red bottom-right, green top-left, yellow top-right.
- Clearly taller than wide. Hold a ruler to the screen if you have to: 73 to 113.
- Centred, with no stretching when the panel is resized.

**Goes wrong as.** Swapping the dot products, which mirrors or rotates the gradient; a card that
fills the screen (you forgot to divide by the card size); a card visible *behind* the camera when you
move the camera past it.

## 1.3 Turn it

**Achieve.** The mouse turns the card: horizontal position → yaw across ±180°, vertical → pitch
clamped to about ±80°. When the mouse is idle, the card sways gently on `iTime`.

**Work out.**

- Rotate the **card** (its centre and its frame), not the camera. The foil depends on the angle
  between the view and the card, so both would *look* the same today — but in Milestone 5 you put
  two cards side by side that must turn together, and moving the camera would not give you that.
- `iMouse` does not give you a drag delta. Find out exactly what its four components hold, while the
  button is down and after it is released. Then use the **absolute** position, not a delta — that
  way the card holds its angle when you let go, with no memory needed. (Unity's `CardView` needed a
  stored `restYaw` for this. You can avoid state entirely, and should.)
- Build the rotation as a 3 × 3 matrix. Here is where the column-major trap bites: write it, then
  **test** it rather than trust it — rotate the frame by 90° about Y and check T now points where you
  expect.

**Test.** Drag left and right — the card spins round and you see the back side of the rectangle
(still showing the UV gradient; there is no back art yet). Drag up and down — it pitches and stops at
the limit. Release — it stays put. Leave the mouse alone — it sways.

The Unity tutorial made a big deal of the Root/Visual split (4.2): measuring the pointer against the
thing you rotate creates a feedback loop. Using the absolute mouse position against the *screen*
cannot loop, which is why you do not need the split here. Know why it is safe, rather than assuming.

## 1.4 Both faces

**Achieve.** The card's back shows the UVs as its back art will need them: mirrored in u, frame
flipped, so that later the back artwork reads correctly.

**Work out.** The same three questions as the Unity tutorial's Milestone 5, now with nothing hidden
from you:

- **facing** comes from the card's normal against the ray — not from which side of the rectangle you
  computed. (In Milestone 8 the card bends and faces both ways at once.)
- Which of T, B, N do you negate on the back, and which must stay alone to keep the frame's
  handedness? Work out what `cross(N, T)` does when you negate both.
- What happens to u on the back, and why? Derive it from a drawing of the card seen from each side.

**Test.** Draw uv as colour again. Turn the card through 180° slowly. On the front, black is
bottom-left. On the back, black should be bottom-**right** as you look at it — the same corner of the
card, seen from behind — until you apply the u flip, after which the back reads left-to-right like the
front. The swap happens exactly edge-on, with no flicker.

## 1.5 The cutout

**Achieve.** Transparent pixels in the art are not part of the card — the ray passes through to the
background.

**Work out.** In Unity this was `clip` in the forward pass, plus the same `clip` in the depth pass so
the two agreed. What is the equivalent here, where there is no depth pass? (It is "the ray missed" —
and in Milestone 5, with two cards on screen, "missed this one" has to mean "try the next nearest".)
Why must this test run **before** any shading uses the hit?

**Test.** Needs the art from 2.1 — come back to it. The card's rounded corners show background
through them, and so does every transparent pixel inside the art.

---

# Milestone 2 — Pixel-perfect art

## 2.1 Load the art

**Achieve.** Front art on the front, back art on the back, loaded through channel directives:

```glsl
#iChannel0 "file://assets/front.png"
#iChannel1 "file://assets/back.png"
#iChannel0::MinFilter "LinearMipMapLinear"
#iChannel0::MagFilter "Linear"
#iChannel0::WrapMode "Clamp"
```

(Same three lines for `iChannel1`.) These are your import settings. Compare them with the Unity
tutorial's 1.3 table: bilinear, mips on, clamp. The reasons have not changed.

**Work out.**

- **Is the image upside down?** Image files store their top row first; GL textures put `v = 0` at the
  bottom. Some tools flip on upload and some do not. Look at the art, and fix it in *one* place —
  decide which — so you never fix it twice.
- The card is 73 × 113, not a power of two. Under WebGL1 that meant no mipmaps and no repeat. You
  set WebGL2 in 0.1; this is where it pays off. `textureSize(iChannel0, 0)` tells you what actually
  loaded — draw it if in doubt.
- Decode the sample (0.2) before using it.

**Test.** The art appears on both faces, right way up, never mirrored — readable text stays readable
from both sides. Zoom the preview in (or move the camera to `z = -0.8`): it is **blurry**. That blur is
bilinear filtering, and remembering what it looks like now is the point, because 2.3 removes it.

## 2.2 Texel grid view

**Achieve.** Debug view 1: a checkerboard of the card's own texel grid instead of the art.

**Work out.** From UV to "which texel am I in" needs the card's pixel size. Make it a constant or a
slider now: `vec2(73.0, 113.0)`.

**Test.** Exactly 73 × 113 cells, square. Rectangular cells mean your pixel size and your card aspect
disagree.

## 2.3 The snap

**Achieve.** Sampling snapped to texel centres, keeping a ramp exactly **one screen pixel** wide
across each texel seam. With a slider to switch it off.

**Work out.** The single idea, word for word from the Unity tutorial, because it has not changed:
with `p = uv * texSize`, what does `fwidth(p)` measure, and in what units? Write the sentence "one
unit of `fwidth(p)` means ______ per screen pixel", and the rest follows: find the nearest texel
centre, measure your offset from it, convert the offset to screen pixels, clamp it.

**Test.**

1. **Static:** slider off — blurry. On — crisp, hard-edged pixels.
2. **Turning:** drag the card slowly for ten seconds, watching one diagonal edge. Nothing crawls.
   Then set `MinFilter`/`MagFilter` to `"Nearest"` and do it again: it crawls badly. That is the
   whole argument for doing it this way.
3. **Small:** move the camera back until the card is about 30 pixels tall. No aliasing sparkle.

**Goes wrong as — new here.** At the card's **silhouette**, the 2 × 2 pixel block the GPU uses for
`fwidth` can contain pixels that missed the card. If a miss leaves `uv` at zero, the derivative there
is enormous and the edge pixels turn to garbage. In Unity the rasterizer never ran the fragment
shader off the triangle, so this could not happen. Fix it by computing the plane's UV **even for
misses** — the plane is infinite, the rectangle is not — so the derivatives stay continuous. Watch the
card's outline zoomed in, before and after.

## 2.4 The mip trap

**Achieve.** Sample at the snapped UV, pick the mip level from the **unsnapped** one.

**Work out.** Same as the Unity tutorial's 6.3: the snapped UV jumps at every texel seam, the
hardware reads that as extreme minification, and grabs the coarsest mip. Find the sampling function
that takes gradients explicitly.

**Test.** Card at ~40 pixels tall, turning: before, a flat smear; after, a small, readable card. Add
debug view 2: `log2` of the texel density as colour. It must vary smoothly across the card.

**Also check** whether the tool built mips at all for a non-power-of-two image. Set `MinFilter` to
`"LinearMipMapLinear"`, shrink the card, and compare against `"Linear"`. If they look identical at
30 pixels tall, there are no mips — and your density view is describing a level that does not exist.

---

# Milestone 3 — Parameters instead of materials

A Unity material was just a set of uniform values. Here, those are sliders.

## 3.1 Sliders

**Achieve.** Every material knob from `CardHolo.shader`'s `Properties` block as an `#iUniform`, with
the **same default and range**. Copy them from `CARD_SHADERS_SOURCE.md` — this is data, not solution:

```glsl
#iUniform float foilIntensity = 1.0 in { 0.0, 3.0 }
#iUniform float rainbowSteps  = 8.0 in { 0.0, 32.0 } step 1.0
#iUniform color3 sparkleColor = color3(1.0)
```

**Work out.** The slider values are "what the inspector showed". Unity converted `Color` properties
from sRGB to linear on upload, and left plain floats alone. Your sliders arrive raw. Which of them do
you decode? (The answer to 0.2's question about what a number *means*.)

**Test.** Drag a slider; the preview follows at once, no recompile.

## 3.2 Rarity tiers

**Achieve.** A `tier` slider, 0–4, selecting Common, Shiny, Holo, Galaxy, Chrome — each a set of
overrides on the slider values.

The presets, from `CardDemoBuilder.ConfigureTier`. Everything not listed stays at its default,
except that **rainbow, sparkle, chrome, sweep and edge strengths all start at 0** before a tier
turns any on:

| Tier | Overrides |
|---|---|
| Common | foil intensity 0 |
| Shiny | rainbow 0.1, sweep 0.4, sweep width 0.22, edge 0.2 |
| Holo | rainbow 0.55, band scale 3.5, tilt response 1.4, hue steps 8, sweep 0.25, edge 0.2, mask from luma 0.35 |
| Galaxy | rainbow 0.4, band scale 2, hue steps 6, sparkle 1.1, density 30, size 0.28, sweep 0.2, edge 0.25, sparkle colour (1, 0.95, 0.8) |
| Chrome | foil intensity 0.9, chrome 0.45, sun sharpness 64, rainbow 0.18, hue steps 10, sweep 0.5, sweep width 0.18, edge 0.3, mask from luma 0.2 |

Shared on every tier: foil blend 0.65, mask from luma 0.45, mask contrast 2, mask bias 0.1, both
pixel snaps on.

**Work out.** A slider *and* a preset both set the same value. Which wins, and how do you make that
obvious in the code? One clean answer: a preset *multiplies* or *replaces* a base value, and a
separate "use sliders" switch turns presets off while you tune.

**Test.** Only testable once Milestone 5 exists. Then: step the tier slider 0 → 4 and compare each
against its row in `CardFoilPreview.png` from the Unity project (it is already rendered, next to
`Assets/`).

---

# Milestone 4 — Tilt

One `vec2`, five effects hanging off it. Everything the Unity tutorial said in Milestone 7 applies;
what changes is that you are now the one computing every input.

## 4.1 The frame, in world space

**Achieve.** T, B, N for the hit point, in world space, after the card's rotation and the back-face
flip.

**Test.** Debug view 3: `N * 0.5 + 0.5`. One flat colour across a flat card, changing smoothly as it
turns.

## 4.2 Tilt

**Achieve.** A `vec2` that is zero when the card faces the camera square-on, grows as it turns away,
and stays bounded near edge-on.

**Work out.** The Unity tutorial's four questions, unchanged:

- Why express the view direction in **tangent space**? What must change tilt, and what must not?
- The component of the view direction **along** the surface, divided by how head-on you are.
- Two bounding mechanisms — a floor under the divisor and a soft saturation — and what each prevents
  alone.
- Write "how head-on am I" as a dot product against a normal **vector**, not as a component, so that
  Milestone 7 can swap in a dented normal with a one-line change.

One thing is new: which way is the **view direction**? In Unity, `GetWorldSpaceViewDir` pointed from
the surface toward the camera. Your ray points from the camera toward the surface. One of them is
the negative of the other. Get it wrong and the whole foil runs backwards — and looks entirely
plausible while doing it.

**Test.** Debug view 4: `vec3(tilt * 0.5 + 0.5, 0.5)`.

1. Card square-on, centre of the card: flat grey.
2. Turning: the colour shifts smoothly and consistently.
3. Near edge-on: the colour goes somewhere and **stays**. Blowing out to white means an unbounded tilt.
4. **Move** the card sideways — change its centre, not its rotation. Tilt barely changes. If it swings,
   you built something that responds to position.
5. **New:** compare against Unity's own tilt. In the Unity project, open `CardHoloDemo.unity`, set
   `_DebugView` to 4 on a card material, press Play and turn the card right; do the same here. The
   colour must move toward the same corner in both. If it goes the opposite way, check the view
   direction's sign before anything else. (The materials are regenerated by the demo builder, so the
   `_DebugView` change will not survive a rebuild — which is fine for a look.)


---

# Milestone 5 — The foil, one layer at a time

Five layers. Build them in order, and after each one **turn every other strength to zero and look at
the new layer alone**. Everything in the Unity tutorial's Milestone 8 carries over — the questions
are the same questions — so this milestone restates them briefly and spends its words on what is
different here.

## 5.0 A comparison rig

**Achieve.** The preview split down the middle: the left half renders the card with a **fixed
reference tier**, the right half with the one you are working on. Both turn together from the same
mouse.

**Work out.** In Unity this was two objects. Here it is one shader drawing two cards — either two
rectangles side by side (and a nearest-hit rule, 1.5), or the simpler trick of choosing the tier by
`fragCoord.x` and moving the card's centre left or right depending on the half. Pick one and know
what the other would cost.

**Test.** Both cards turn in sync. From now on every judgement is a difference between two cards in
the same frame — never "better than ten minutes ago".

## 5.1 Spectrum

**Achieve.** `t` in 0–1 → a colour cycling through the spectrum, seamless at the wrap.

**Test.** `spectrum(uv.x)` across the card: smooth, no dark band, no seam between the left and right
edges.

## 5.2 Rainbow diffraction

**Achieve.** Bands at an angle, whose hue shifts as the card turns, quantised to steps.

**Work out.** Three terms drive the phase — position, tilt, time — and separately the sampling
position is offset by tilt (parallax). Which term makes it view-dependent? What does parallax add
that phase alone cannot? `floor` or `round` for the steps?

**Test.** Four sliders, four distinguishable behaviours: turning marches the bands; tilt response at
0 freezes them on the card; drift moves them with the card still; parallax depth slides them against
the art. If two sliders do the same thing, the maths is wrong.

## 5.3 Sweep

**Achieve.** A soft bar that crosses the face as the card turns, parked off one edge at rest.

**Test.** Sweep only, turning slowly: the bar enters one edge and leaves the other.

## 5.4 Sparkle

**Achieve.** Glitter flakes that **wink** as the card turns, rather than a texture that slides.

**Work out**, testing each before the next:

1. **A hash.** `vec2 → vec2`, no visible structure. Test: `hash(floor(uv * 40.0))` as colour looks
   like television static. **New here:** most GLSL hashes you will find online were written for
   `highp` desktop floats. WebGL can give you less precision than you think, and a hash that is
   static on your Mac can show stripes on another machine. Test yours at `uv * 400.0` too.
2. **Cells** — one flake per cell, at a hashed position.
3. **The 3 × 3 neighbourhood** — and draw the failure case first: a flake near a cell edge.
4. **A preferred tilt per flake** — the entire effect.
5. **`max` or sum** across the nine.

**Test.** Still card: steady. Turning: individual flakes wink at different moments. Density at max:
no grid, no moiré. **Performance:** this is your most expensive layer. GLSL has no `[unroll]`; with
constant loop bounds the compiler usually unrolls anyway. If the preview's frame rate drops, this is
the first suspect.

## 5.5 Chrome

**Achieve.** A mirror with sky, ground and sun, damped head-on.

**Work out.** Reflect in **world space**, so the sky stays up when the card turns upside down. The
damping ramp is a function of something you already computed in 4.2.

**Test.** Square-on, the print stays readable. At 45°, the mirror takes over. Upside down, the sky is
still above.

## 5.6 Edge glow

**Achieve.** A rim at grazing angles, gone head-on — one line.

**Test.** It appears on the silhouette. In Milestone 8 the silhouette moves when the card bends, and
this layer must follow it without being told.

## 5.7 The mask

**Achieve.** Foil on the print's bright areas, from the art's **luminance** (plus an optional mask
texture on another channel).

**Work out.** Luminance of what — the display value or the linear value? You decided the general rule
in 0.2; this is where it bites. The Unity shader works in linear, so its mask thresholds were tuned
against linear luminance. Compute it on the other one and every tier's mask lands in a different
place.

**Test.** Mask-from-luma at 0 must be **pixel-identical** to no mask. Digital Color Meter on the same
three spots, both ways — not an impression.

## 5.8 Composite

**Achieve.** The five layers into one: additive and screen blends mixed by a slider, a second snap
that puts the **foil** on the texel grid (no ramp — know why it differs from 2.3), and colour
quantisation.

**Test.** Step the tier slider 0 → 4 with the reference split. Then put your five tiers next to
`CardFoilPreview.png` from the Unity project, at roughly the same four angles. They will not match
pixel for pixel — different renderer, different camera — but each tier's **character** must: Shiny
is a sweep and little else, Holo is banded rainbow, Galaxy sparkles, Chrome mirrors. If one of yours
reads as a different tier, find which layer is off before touching the numbers.

---

# Milestone 6 — The wear map, as a buffer

In Unity, `CardWear.cs` held the damage as an array of floats on the CPU and uploaded it as a 73 × 113
texture. Here there is no CPU side. The map lives in a **buffer pass** — a second shader whose output
is kept and read by the card shader as a texture.

## 6.1 Design the channels

The brief and the questions are the Unity tutorial's 9.1, unchanged: one RGBA map, five kinds of
damage, per-face or shared, signed height in an unsigned channel, and which damage cannot live in a
map at all. Answer them before writing anything. Then compare with the channel table in
`CARD_SYSTEM.md`.

**One thing is new.** In Unity the back map was a *second texture*. Here it is simplest to put both
faces in one buffer — the front map in one 73 × 113 region, the back in another beside it. Decide
the layout and write it down.

## 6.2 A buffer you can see

**Achieve.** `wear.glsl` writes a recognisable test pattern into a 73 × 113 region of its output;
`card.glsl` reads it through a channel and shows it on the card as debug view 5.

```glsl
// card.glsl
#iChannel2 "file://wear.glsl"
#iChannel2::MinFilter "Nearest"
#iChannel2::MagFilter "Nearest"
#iChannel2::WrapMode "Clamp"
```

For multi-file work, use the extension's **"Shader Toy: Show Static GLSL Preview"** command, so the
preview keeps showing the card while you edit `wear.glsl` in another tab.

**Work out.**

- The buffer is the size of the whole preview, but you use only a 73 × 113 corner of it. So a card
  texel maps to a buffer **pixel**, not a UV. Read it with `texelFetch` and integer coordinates. Why
  is that better here than `texture` with a UV, even with Nearest filtering?
- Which UV do you convert to a texel index — the raw one or the snapped one from 2.3? (Unity
  sampled wear at the snapped UV, so wear texels land exactly on art texels.)
- Is the buffer **float**, or eight bits per channel? It matters: Unity held the map in floats because
  a soft brush changes outer texels by amounts smaller than 1/255. Find out empirically: write
  `0.5 / 255.0` into a texel and read it back multiplied by 255. If you get 0.5, it is float. If you
  get 0 or 1, it is not — and Milestone 12's restoring will need a workaround.

**Test.** Your pattern appears on the card, exactly 73 × 113, right way up, sharp, not mirrored.
Everything you learn here about orientation is what later damage lands on.

## 6.3 Generate once

**Achieve.** The map is computed on the first frame and then **kept**, not recomputed sixty times a
second — with a key that regenerates it from a new seed.

**Work out.** `wear.glsl` reads **itself** (`#iChannel0 "self"`) and passes its previous value
through, except on frame 0 or when a key is pressed. That is the GPU version of "the map is state".
Also: what happens to the kept map when you **resize the preview**? Buffers are the size of the
screen. Find out, and decide whether you care.

**Test.** A pattern that depends on `iTime` stops changing after frame 0. Press your key — it
changes once, then holds.

## 6.4 The damage, gathered

**Achieve.** Edge wear, scratches, dents, creases and chips, with the same shapes and the same rules
as `CardWear.cs`: edges and corners first, scratches take print with them, dents biased toward the
border, creases as a valley with ridges beside it, chips centred **on** the outline.

**Work out — this is the real lesson of the milestone.** `CardWear.cs` **stamps**: for each scratch,
walk along it and write into the texels it crosses. A fragment shader cannot do that — each pixel
writes only itself. You have to **gather**: for *this* texel, loop over every scratch and ask whether
it covers me.

- Rewrite each pass as a gather. Which ones are easy (edge wear already is one), and which change
  shape?
- `CardWear` combined overlapping damage with **the worse of the two**, not the sum, so two crossing
  scratches do not punch a hole. What is that in a gather loop?
- Determinism: Unity used one `System.Random` stream shared by all five passes. You will use a hash of
  `(seed, pass, index)`. Consequence: disabling one pass **no longer changes the others** — unlike
  Unity, where a subset of passes was a different card. Which do you prefer, and why did Unity not
  do this?
- Counts scale with severity: scratches `round(mix(0, 16, s²))`, dents `round(mix(0, 7, s))`,
  creases 0 / 1 / 2 at severity below 0.45 / below 0.8 / above, chips
  `round(mix(0, 5, (s - 0.25) / 0.75))` clamped. That is data — use it.

**Test.** Give each pass its own toggle, turn on one at a time at severity 1, and look through debug
view 5 as in the Unity tutorial's 9.3. Then three cards at severity 0.3, 0.6, 1.0 — lightly played,
played, poor. Compare with `CardWearPreview.png` from the Unity project: not the same scratches (the
random numbers differ), but the same *distribution* and the same overall wear at each severity.

**Goes wrong as.** A gather loop over sixteen scratches, each walking fifty steps, for every texel,
every frame — slow, and you will notice. That is why 6.3 generates once.

---

# Milestone 7 — Damage on the card

The Unity tutorial's Milestone 10, unchanged in substance: read, ink loss, chips, the height normal,
scuff. Four notes on what is different.

## 7.1 Read

Same as Unity — scale by an amount, quantise with `floor` into visible steps. Reads are `texelFetch`,
so the explicit-LOD question from Unity does not arise; know why.

## 7.2 Ink loss

Desaturate, then fade to card stock — try both orders. The stock colour is an sRGB value
(0.84, 0.80, 0.72); decode it.

## 7.3 Chips

A chip is a missing piece of card, so it must behave like a transparent pixel — the **ray passes
through** (1.5). There is no second pass to keep in step, but there is a new version of the same
trap: if a later step asks "did the ray hit the card?" anywhere else, it has to use this same rule.

**Test:** chips show the background through them from **both** faces.

## 7.4 Height → normal → the payoff

The central step. A normal from neighbouring height texels, xy negated on the back face for **two**
reasons, blocky on purpose — then substitute it for the flat normal in your tilt from 4.2.

**Test, unchanged from Unity:** turn a creased card and watch the rainbow bend along the crease and
the sweep fracture across it. Then search your foil code for the word "wear" (VS Code's search does
it). The rainbow, sweep, sparkle and chrome functions must not contain it — and must still break over
dents.

## 7.5 Scuff

Two additions, and their order against the foil intensity multiply decides whether a **Common** card
can show scratches at all. Test with the split rig: Holo on one side, Common on the other, same
damage.


---

# Milestone 8 — Bending, without vertices

In Unity, bending moved the vertices of a 16 × 24 mesh, and the rasterizer drew whatever shape
resulted. There are no vertices here. The ray has to **find** the bent surface.

This milestone is the one where the standalone version teaches something the Unity one could not.

## 8.1 The height field

**Achieve.** A function `h(uv)` giving the card's displacement along its normal: a bow across x and
across y (zero at the edges), a fold at each of the four corners over a radius, and — once
Milestone 6 exists — creases from the wear map's height channel.

The numbers, from `CardWear.cs` and the shader defaults: a fully ruined card bows up to **0.035**
units, a folded corner lifts up to **0.07**, the fold radius defaults to **0.5** (measured per axis in 0–1
card space, from that corner toward the opposite one), and creases displace by up to **0.012**.

**Work out.** Same as the Unity tutorial's 11.1–11.2: the simplest function that is zero at both
edges; a corner falloff with smooth derivatives at **both** ends (which standard curve?). And one new
question: the crease read must be **linear-filtered**, but your wear channel is set to Nearest. Bind
the same buffer a second time, on another channel, with Linear filtering — that is exactly what
Unity's `sampler_LinearClamp` was. Why does a fold need smoothing when a dent's shading does not?

**Test.** Debug view 6: `h` as a colour, card flat-on. The bow is a smooth dome, a corner fold is a
smooth bump at its corner, and nothing is bent yet.

## 8.2 Find the bent surface

**Achieve.** The ray hits the **bent** card: its outline curves, a folded corner lifts off the
background, and turning the card edge-on shows the bow in the silhouette.

**Work out.** Write the bent card as an **implicit surface**: a function `F(p)` of a point in the
card's own space that is zero on the surface, negative on one side, positive on the other. With the
card facing −Z and `h` pushing the face toward −Z, `F` is the point's z plus `h` at the point's UV.

Then find where the ray crosses `F = 0`:

- **March:** step along the ray between where it enters and leaves a box around the card, watching
  for `F` to change sign.
- **Refine:** once it changes sign, bisect between the last two steps a few times.

How many steps, how many bisections? Too few and a thin fold is stepped over entirely; too many and
the frame rate goes. Find the least that holds up, with a debug view that colours each pixel by the
number of steps it took.

**The box is Unity's mesh bounds, back again.** You only march where the ray is inside a box around
the card. Make that box exactly as thick as the flat card and rays that would have hit the bowed edge
are never tested — the edge is cut off flat. The Unity tutorial made you see that bug in 2.4 so you
would recognise it; here it is. Give the box the same **0.14** depth either side.

**Test.** Bow the card hard, turn it edge-on against the background. The **outline** curves. Then
shrink the box to the card's thickness and watch the bowed edge get sliced off; put it back.

**Goes wrong as.** A sign change missed because a fold is thinner than one step — the fold has holes
in it that move as you turn. More steps, or a smaller step where `F` is small.

## 8.3 The frame follows the bend

**Achieve.** Normal and tangent that follow the bent surface, so the foil does too.

**Work out.** Two routes, and they must agree:

- The **gradient** of `F` is perpendicular to the surface — so the normal is `∇F`, normalised. Get it
  by finite differences on `F`.
- The Unity route, `CardApplyBend`: slopes of `h`, divided by the card's world size, into
  `normalize(vec3(-k, 1))`. The Unity tutorial asked you to derive it in 11.3.

Compute both, draw the difference as a debug view, and make it black. Where does the card's world
size enter each route? If they disagree by a factor that depends on the aspect ratio, that is the
answer.

The tangent must bend too — the Unity shader rebuilds it from the u slope. What goes wrong with the
foil if the normal bends but T stays flat?

**Test.** Bow the card, turn it. The sweep bar **curves** as it crosses the bow. The normal debug
view fans across the bend.

## 8.4 One surface, everywhere

In Unity, the forward pass and the depth pass had to bend identically, or the colour and the depth
disagreed about where the card was. Here there is only one pass — but every question of the form
"did the ray hit the card, and where?" (the cutout, the chips, the two-card comparison, the mouse in
Milestone 12) must use **this** surface, the bent one. Put the intersection in `common.glsl` and call
it from everywhere.

**Test.** Bow the card and chip it. The chips are holes in the **bent** surface, at the right place,
from both sides.

---

# Milestone 9 — Debug views

You have been adding them since Milestone 2. Consolidate them on the one slider, and follow the rules
the Unity shader's `CardDebug.hlsl` follows:

- **Hand every value in** from the main path. A view that recomputes its subject is free to drift
  from it.
- **Take derivatives before any branching.** `fwidth` and `dFdx` are only defined when all four
  pixels in a 2 × 2 block reach them. **New here:** a pixel that missed the card and returned early
  is exactly such a branch. Compute the derivatives before you decide hit or miss.
- **A slider costs nothing when it is 0** — every pixel takes the same branch.

The Unity set, for reference, plus what this version needs on top:

| View | Shows |
|---|---|
| uv | the interpolated UV, now computed by you |
| texels | the 73 × 113 grid, snapping ramp on top |
| normal | the surface normal, dents and bends included |
| tilt | the one vector the foil runs on |
| N·V | head-on white, grazing black, back face tinted |
| wear | scuff, ink, missing |
| height | signed: ridge one colour, dent another |
| foil | the foil without the art |
| density | card texels per screen pixel |
| **h** | the bend height field (8.1) |
| **march steps** | how hard 8.2 is working per pixel |
| **route difference** | the two normals of 8.3, subtracted |

---

# Milestone 10 — The pack

A booster wrapper that tears open along a ragged seam, lets light out of the cut, peels its lid back,
flutters it, and finishes with a flash. Everything in `CardPack.shader` and `PackFlash.shader`.

## 10.1 One wrapper out of the sheet

**Achieve.** A flat rectangle, 0.84 × 1.54 units, showing **one** wrapper cut from `packs.png`.

The sheet is 849 × 3143 pixels: 9 columns × 20 rows of wrappers, each **84 × 154** pixels. Measured
from the **top** of the sheet:

- row `r` starts at `y = 3 + 157 * r`
- column `c` starts at `x = [3, 98, 193, 288, 383, 478, 573, 667, 762][c]`

The columns are listed, not computed: they sit 95 pixels apart except the eighth, which is one pixel
to the left. Assume a regular pitch and one column in nine is visibly wrong.

**Work out.** Turn a pixel rect into a UV rect (offset + size), accounting for whichever way up the
tool loads images (you found out in 2.1). Then snap to wrapper pixels the way you did for the card,
but in the wrapper's own 84 × 154 space. And sample **mip 0** explicitly — why is mip selection a
bad idea on an atlas whose neighbours are right against the cell?

**Test.** Step through several wrappers with a slider. Each one is clean: no sliver of its
neighbour on any edge, zoomed right in.

## 10.2 The seam

**Achieve.** The wrapper in two pieces — body and lid — along a ragged tear **12 pixels** below the
top edge (the crimped seal is 11 pixels; the cut sits one below it).

The shape, from `CardPackBuilder`: the seam is a staircase of **42 columns**, two wrapper pixels
each. Each column's height is offset by up to **3 pixels**, snapped to whole pixels, pinned to zero at
both ends. The offset mixes a coarse wave (62%) with per-column noise (38%).

**Work out.**

- Why two noise scales? What does a cut with only fine jitter look like?
- Why pinned at the ends?
- In Unity the seam was baked into two meshes. Here it is a function: given `u`, which column am I
  in, and where is its cut? Body is below the cut, lid above.
- For each point, compute its **distance from its own column's cut, in pixels** — the torn edge
  (10.4) and the cut light run on it.

**Test.** Draw the lid a different colour from the body. The boundary is a pixel staircase, not a
slope. Then shift the lid up by a few pixels: the teeth and the notches line up exactly.

## 10.3 The peel

**Achieve.** The lid hinges on the seam, lifts, and curls toward the viewer, opening from one side as
the tear travels.

The parameters, from `CardPack.shader`: the tear has reached from `peelMin` to `peelMax` across the
width (both in U), with a **feather of 0.09** either side so the strip bulges rather than snapping; a
`peelFlat` value finishes the tear across the whole width; `peelLift` raises the lid and `peelCurl`
(default **1.5**) sets how much of that lift comes toward the viewer.

**Work out.**

- The **hinge ramp**: 0 at the tear line, 1 at the lid's top edge. In Unity it was a *shared linear
  function of height* rather than a per-column 0–1 ramp, and the shader must not clamp it. Work out
  why both rules exist — then notice they apply here unchanged. A per-column ramp gives neighbouring
  columns different lifts along their shared edge; in a mesh that opened a hairline crack, and here it
  opens a vertical slit.
- Finding the peeled lid is 8.2 again — but here there is a shortcut worth finding. For any fixed
  `u`, every displacement is **affine** in the undisplaced height, so each column of the lid is a
  flat, tilted strip. Use that, or use the general march from 8.2; know what the shortcut buys.

**Test.** Drag `peelMax` from `peelMin` to 1: the lid opens from one side, bulging at the edge of the
tear. Look along the seam at a shallow angle — no slits between columns.

## 10.4 The torn edge and the cut light

**Achieve.** A bright torn lip exactly one pixel wide along the cut, darker pixels stepping down
behind it — and light spilling out of the cut while it is being made.

The torn edge: width **3 pixels**, shade 0.4, lip colour (1, 0.96, 0.88). The cut light, new since
the Unity tutorial: a band **2 pixels** wide, colour (0.55, 0.95, 1), rainbow 0.7, brightest at the
**head** of the sweep (the point the tear is at right now), and widened by a `flash` value at the
end.

**Work out.**

- Both are stepped on **whole wrapper pixels**. A smooth falloff reads as a glow pasted on top of the
  pixel art. Where does the `floor` go?
- The cut light only lights where the tear has **been** — the peel shape from 10.3 — and its head burns
  brighter than the tail it leaves. What makes the cut read as travelling rather than fading up all at
  once?
- It is white where strongest and splits into a spectrum where it falls off. Which of your existing
  functions gives the spectrum?
- On the lid's **back** face — the inside of the wrapper — the body shade darkens it, but the cut light
  should stay bright. Why, and does the order of those two operations decide it?

**Test.** Animate the head across with a slider. Zoom in: the lip is one pixel, the steps behind it
are whole pixels, the brightest point follows the head.

## 10.5 Flutter

**Achieve.** Once the lid has come off, a wave travels along it: the hinge end stays down and the free
edge swings, like a strip of foil.

From `PackFlutter`: amplitude up to **0.4**, **1.6** waves across the strip, a phase that you animate,
curl **0.55**. The grip is `0.25 + hinge`.

**Work out.** Why is the wave driven by `u` and the shared hinge, and not by anything per column? (The
same reason as 10.3 — and the failure is the same slit, now moving.) Why `0.25 +` rather than the
hinge alone?

**Test.** Animate the phase with `iTime`: a smooth wave runs along the lid, the hinged end nearly
still, no stripes.

## 10.6 Sheen

The pack's foil — the card's sweep bar from 5.3, tinted by the spectrum from 5.1, masked by the
wrapper's own luminance so dark ink stays dark. Tilt as in 4.2, against the pack's flat normal.

Defaults: strength 0.4, width 0.3, angle 1.15, travel 1.1, rest offset −0.35, rainbow tint 0.35.

**Test.** Tilt the pack: a bar crosses it, and the dark printed areas never light up.

## 10.7 The flash

`PackFlash.shader` is a separate shader in Unity, drawn on its own quad with its own blend state. It
does two jobs: a **bar** of light along the cut, and a **dim** that darkens everything behind.

**Achieve.** Both, composited by hand over your pack.

- **The bar** runs along the cut, only as long as the part that has been opened, then spills
  **0.85** of the pack width past both ends as the flash goes off. Thickness **0.055**, soft edges,
  a bright core, a rainbow fringe (0.8), edge colour (0.5, 0.92, 1), a brighter spot at the head
  (width 0.07), and thickness growing with the flash.
- **The dim** multiplies the whole scene toward (0.38, 0.4, 0.5).

**Work out.**

- In Unity, the bar was drawn with additive blending and the dim with a multiply — set by the material,
  not the shader. You have no blend state. Write both as arithmetic on the colour you have already
  computed. Which order do they go in?
- The bar is drawn on a quad whose UVs run **past** 0 and 1, so the spill can go wider than the pack
  without resizing anything. What is your equivalent coordinate — and why is it simpler for you?
- Pow of a negative number is undefined. Where does that bite in the head's falloff, and what is the
  cheap fix?

**Test.** One `progress` slider, 0 → 1, should drive the whole opening — peel range, cut head, lid
lift, flutter, flash, dim. Designing that mapping from one number to eight is the exercise. Then
compare the sequence with the pack opening in the Unity game (`PackOpening.unity`).

---

# Milestone 11 — Atlas quads and the slot frame

## 11.1 A cell of a sheet

**Achieve.** A rectangle showing one cell of `icons.png` (128 × 96: a 4 × 3 grid of 32 × 32 icons),
and another showing one frame of `book.png` (1330 × 160: seven frames of 190 × 160, left to right).
Snapped to cell pixels, sampled at mip 0.

**Work out.** The Unity tutorial's 13.2: clamp samples to the cell's outer texel **centres**, not its
edges. Some icons touch the top or bottom of their cells; what does a sample exactly on the boundary
blend in?

**Test.** Pick an icon that touches its cell edge, zoom in, and look for a one-pixel fringe of its
neighbour. Present without the clamp, gone with it.

## 11.2 Animate the book

**Achieve.** The book cycles through its seven frames on `iTime`.

**Work out.** The step is `floor(iTime * frameRate)` — and nothing else changes, because the frame is
just a different rect. In Unity that is why a property block could animate it. Why is picking a rect
cheaper than swapping textures?

**Test.** The book animates cleanly, with no frame showing a sliver of the next.

## 11.3 The slot frame

`CardSlot.shader`, which the Unity tutorial never covered: the outline a card can be dropped into.

**Achieve.** A rectangle **79 × 122** pixels (slightly larger than a card) drawn as an outline
**2 pixels** thick, showing only **corner brackets 16 pixels** long at rest, and the full outline plus a
stronger fill when highlighted.

Colours and alphas: line (0.62, 0.58, 0.8), fill the same colour, highlight (1, 0.98, 0.92); edge alpha
0.55, fill alpha 0.06, highlighted fill 0.2.

**Work out.**

- Measure everything in **whole slot pixels**, not UV, so the outline stays on the same grid as the art
  inside it. How do you get "distance to the nearest edge, in pixels"?
- A bracket is "on the outline **and** near a corner on both axes". Write that as arithmetic.
- The line is drawn **over** the fill and they have different colours, so you composite the two by
  hand, then composite the result over the scene. Write "A over B" for colour and alpha. (What does
  the Unity shader divide by, and why?)

**Test.** Put a card inside the slot. The brackets sit on the pixel grid, 16 pixels long each. Push a
`highlight` slider to 1: the full outline fades in. Set the corner length past half the slot size: the
brackets meet into a plain rectangle.

---

# Milestone 12 — Restoring (optional)

The Unity game lets you rub damage out of a card with tools. The shader-app version is a small leap
from Milestone 6.

**Achieve.** Hold a key to pick a tool, drag the mouse across the card, and the wear under the cursor
rubs out, gradually.

**Work out.**

- `wear.glsl` already keeps its previous frame (6.3). Now it also needs **where the mouse is on the
  card** — the same ray and the same bent surface as the card shader. That is why the intersection
  went into `common.glsl` in 8.4.
- A soft brush changes outer texels by very small amounts per frame. You found out in 6.2 whether your
  buffer is float. If it is not, small amounts round to nothing, and the brush grows a hard edge.
- In Unity, a tool is nothing but a set of rates, and every tool damages something else as it works —
  that is what orders the repair without a single rule enforcing it. Design three tools as rates and
  see whether an order emerges.
- Scuffs and ink loss are per face. Rubbing the front must not touch the back.

**Test.** Rub a scratch out: it goes in visible steps (the quantising from 7.1), not a smooth fade.
Turn the card over: the back is untouched.


---

# Appendix A — Test recipes

**Reading an exact colour.** Digital Color Meter (`/Applications/Utilities/`), aperture 1 pixel,
hovering over the preview. For "must be identical" claims, read the same three points both ways.
"Looks the same" misses a 2% difference that matters later.

**Comparing two states honestly.** The split rig from 5.0 — two cards, same frame, same angle. Your
memory of a minute ago is not evidence.

**Freezing time.** Anything on `iTime` makes a still image impossible to compare. Put a
`#iUniform float timeScale = 1.0 in { 0.0, 2.0 }` in front of every use of `iTime` and set it to 0
while judging.

**A fixed angle.** For side-by-side comparisons with `CardFoilPreview.png`, add sliders for yaw and
pitch that override the mouse, and set the four angles the Unity sheet uses by eye.

**Reading a buffer.** Draw it. A debug view that shows the wear buffer's raw texels, blown up, is the
only way to know what the buffer holds.

**Multi-file editing.** "Shader Toy: Show Static GLSL Preview" pins the preview to one file, so it
keeps showing the card while you edit the buffer or `common.glsl` in another tab.

**When the preview goes black.** A compile error. The extension reports it — read the line number
before changing anything. The two most common: an `int` where GLSL wanted a `float` (write `1.0`),
and a function used before it was declared (GLSL needs it defined above the call).

---

# Appendix B — Unity file → your file

| Unity | Here |
|---|---|
| `CardHolo.shader` + `CardHoloInput.hlsl` | `card.glsl` + `common.glsl` |
| `CardWear.hlsl` (sampling, dent normal, bend) | `common.glsl` |
| `CardDebug.hlsl` | the `debugView` branch in `card.glsl` |
| `CardWear.cs` (generator, map) | `wear.glsl` (a buffer pass) |
| `CardView.cs` (rotation) | a few lines of `iMouse` in `common.glsl` |
| `CardDemoBuilder.cs` (mesh, tiers) | the ray–card intersection, and the `tier` presets |
| `PixelArtCardImporter.cs` | `#iChannel::MinFilter / MagFilter / WrapMode` |
| `CardPack.shader` + `CardPackInput.hlsl` | `pack.glsl` |
| `CardPackBuilder.cs` (torn meshes) | the seam function in `pack.glsl` |
| `PackFlash.shader` | the bar and dim at the end of `pack.glsl` |
| `SpriteSheet.shader`, `CardSlot.shader` | `sheet.glsl` |
| `CardOverlay.shader` (wireframe) | no equivalent — there is no mesh. The texel and height views stand in for it |
| Project Settings → Linear | your two conversion functions from 0.2 |

---

# Appendix C — What to look up

| Milestone | Look up |
|---|---|
| 0 | the extension's README; `shader-toy.webglVersion`; the sRGB transfer function |
| 1 | ray–plane intersection; rotation matrices in GLSL (column-major); `iMouse` in Shadertoy |
| 2 | `textureSize`, `fwidth`, `textureGrad`; WebGL2 non-power-of-two textures |
| 3 | `#iUniform` syntax in the extension README |
| 4 | tangent space; parallax offset |
| 5 | `fract`, `smoothstep`, `reflect`; Rec. 601 luminance weights; GLSL hash functions and float precision |
| 6 | Shadertoy multipass buffers and `self` feedback; `texelFetch`; gather vs scatter on the GPU |
| 7 | central differences |
| 8 | implicit surfaces; ray marching; bisection; gradient of a scalar field |
| 10 | affine maps; premultiplied vs straight alpha |
| 11 | texel-centre clamping in atlases; the "over" operator |

---

# Appendix D — If you are stuck, read upward

```
colour space (0.2) ─────────────────────────────────────────┐
                                                             │
camera (1.1) ──> card intersection (1.2) ──> faces (1.4) ──┬─┼──> tilt (4.2) ──> foil (5.x)
                         │                                  │ │                    │
                         │                             art (2.1)          mask ──> composite (5.8)
                         │                                  │                      ▲
                         │                     snap (2.3) ──> mips (2.4)           │
                         │                                                          │
                         └──> bent surface (8.2) ──> bent frame (8.3) ──> tilt      │
                                    ▲                                               │
wear buffer (6.2) ──> generator (6.4) ──> read (7.1) ──┬──> ink (7.2)              │
                                                        ├──> chips (7.3)            │
                                                        ├──> dent normal (7.4) ──> tilt
                                                        └──> scuff (7.5) ──────────┘
```

- Foil wrong **everywhere** → tilt (4.2), and first the sign of the view direction.
- Foil wrong **only on damaged areas** → 7.4.
- **Colours** all slightly wrong, nothing broken → 0.2: converting twice, or not at all.
- The **edge** of the card is noisy but the middle is fine → derivatives at the silhouette (2.3,
  Milestone 9).
- A **bent** card has its edge cut off flat → the march box in 8.2.
- The **art** itself is wrong → nothing below 2.4 matters yet.
