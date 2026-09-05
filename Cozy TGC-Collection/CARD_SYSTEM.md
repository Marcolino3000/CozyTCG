# Cozy TGC — Card Rotation & Holo Foil

Rotatable 3D pixel-art cards with a view-dependent TCG foil effect. URP 17.3, Unity 6000.3.

**New here?** `CARD_WALKTHROUGH.md` is the tutorial: what a shader is, what the vertex and fragment
stages each cost, and then the foil and the wear map taken apart one step at a time.
`Assets/Scenes/CardExplainer.unity` (`Tools > Cozy TGC > Build Explainer Scene`) runs those same 36
steps live — two cards, one finished and one carrying a single layer or a single kind of damage, so
every step is a difference you can see side by side. This file is the reference: what each knob does,
and why it is the value it is.

## Demo

Open `Assets/Scenes/CardHoloDemo.unity` and press Play. Five cards, one per foil tier.

| Input | Action |
|---|---|
| Hover | Card lifts and tilts towards the pointer |
| Click | Flip (front ↔ back) |
| Drag | Turn freely in any direction, snaps to the nearest face on release |
| `F` / `R` | Flip all / reset all |
| `←` `→` | Cycle the artwork |
| `Tab` | Switch deck (tarot ↔ spanish) |
| `H` | Hide the tuning panel |
| `1`–`6` | Pick a restoration tool — a held pointer then rubs instead of spinning |
| `0` | Back to turning |
| `Space` | Hold to turn the card with a tool still in hand |

The left panel tunes the foil, the right one ages the card, hands over a tool and reads its grade
back. **Turn is the first entry in the tool row**, not the absence of a tool: with it selected the
pointer belongs to the card and drags spin it, and holding `Space` borrows it back for as long as
you hold it, so checking an angle mid-restoration does not mean putting the tool down and finding
it again.

Both panels act on the **last card hovered**, not the one under the pointer right now — reaching for
a slider or a button takes the pointer off the card, and a panel that followed the live hover would
take its own controls away before they could be clicked. The panels also block the cards underneath
them while the pointer is over one, so nothing tilts at a cursor that is not talking to it and no
card gets rubbed while the severity slider is being dragged.

`Tools > Cozy TGC > Render Foil Preview` renders a 4-angle contact sheet to
`CardFoilPreview.png` without entering play mode.

`Tools > Cozy TGC > Build Demo Scene` regenerates the mesh, materials, prefab and scene.
**It overwrites those generated assets**, so tune materials by duplicating them rather than
editing the generated ones in place if you plan to re-run it.

## Pack opening

Open `Assets/Scenes/PackOpening.unity` and press Play.

| Input | Action |
|---|---|
| Drag across the crimped top of the pack | Rip the seam open |
| Click the stack | Turn the top card over, in place |
| Click it again | Send it to the default slot |
| Drag it off the stack onto a slot | File it into whichever pile you want |
| Drag the top card of a slot | Move it to another slot |
| Click the top card of a slot | Flip it over in place |
| Hover any card | Lifts and tilts towards the pointer |
| Side menu → Album | Open the collector album |
| Drag a card into a pocket | File it into the album |
| Click the left / right page | Leaf one spread back / forward |
| Click the drawer under the books | Put the next pack on the table |
| Click the drawer again, or the bare table | Put a whole pack back in the drawer |
| Shop tab (top right) / `B` | Open the shop |
| `Space` / `R` | Next pack out of the drawer |
| `Q` | Unpack a whole booster onto the default pile |
| `C` | Empty the slots |
| `H` | Hide the panel |

The tear tracks how much of the pack's *width* the cursor has swept, not how far it has
travelled, so it works from either side and stalls rather than cancels if the cursor wanders
off the seam — let go half way and the pack re-seals. Light runs out of the seam behind the
sweep, brightest at the end the cursor just pushed out, and the rest of the table darkens
around it. Sweep `tearSpan` (82% by default) and the cut goes off: a prismatic flash across
the seam, the sealed strip thrown clear of the frame, and the opened wrapper standing there
for a beat before it drops — revealing five cards rolled from the rarity weights on
`PackOpeningController`, with the last card guaranteed above common.

**The deck is rolled per card, not per pack**, from `packWeight` on each `CardArtSet`: at 9
against 1 a pack is mostly Spanish with a tarot card in about every second one (a tenth of
five cards). The same roll picks the deck for a card bought off the shop's rack, so the rare
deck is as rare either way.

That mix is why a pack deals every card with **one shared back** — the deck the weights make
it mostly of — instead of each card's own. The two backs look nothing alike, and one purple
back in a stack of blue ones announces the tarot card before anybody turns it over. A card
puts its real back on as it leaves the pack (`CardPackDeck.Take`), by which time it is face
up and nothing sees the change.

Packs are not free. They come out of the **drawer** hanging under the shelf of albums, which
starts with `startingPacks` in it and is refilled at the shop; the drawer shows the wrapper
of the pack that comes out next and prints how many are left underneath it. Clicking it puts
one on the table — and since a sealed pack rides up close to the camera, that *is* "show it
large so it can be opened". One at a time: clicking it while a pack is still out spends
nothing, and neither does clicking it to come back from an album.

`Q` skips the ceremony: the wrapper is ripped in one go (`CardPackView.RipOpen` fills the
sweep in rather than jumping over it, so the flash goes off across the whole seam the way it
does by hand) and every card is turned
over and sent to the **default pile**, the same place a plain click on the stack sends them.
With nothing on the table it takes the next pack out of the drawer first, so one press is one
pack from drawer to pile; with a pack half dealt it finishes that one instead of skipping to
the next. It puts the album away first, because the pack and its stack are switched off behind
it and a wrapper that cannot run its own `Update` never finishes falling.

A pack you think better of goes back. While the wrapper is still whole, clicking the drawer
again, clicking the bare table around the pack, or opening the shop all **stow** it: the
count goes back up and the drawer shows that same wrapper again, because it is the same pack.
Its contents are not, though — they are rolled fresh when it next comes out, since an
unopened pack has not been dealt yet. Once the seam is torn there is nothing left to put back,
so the gesture stops working.

### Sorting

Five `CardSlot` piles sit along the bottom under a `CardSlotBoard`. One is marked
`isDefault` (the middle one) and is where a plain click sends cards; its frame is tinted
amber, and the slot a dragged card would land in lights up with a full outline.

Nothing leaves the pack before it has been seen. The first press on the stack turns the top
card over where it lies — it does not move — and only then does it answer to being filed:
click again for the default slot, or drag it onto another one. A card that is dragged and
let go over nothing returns to the pack and stays face up, since revealing it twice would
just be a chore.

That second press splits by `dragThreshold`, exactly like `CardInteractor` splits flip from
spin: travel under it files to the default slot, travel over it lifts the card out. Because
the reveal owns the first press, a face-down card cannot be dragged at all — press, look,
then decide.

Cards that have already been filed move the same way: drag the top card of a pile onto
another slot. A drag here **moves** rather than turns — the demo scene's free spin would
fight it — so `CardInteractor` runs in `DragBehaviour.HandOff` in this scene: it still does
hover and click-to-flip, but once a press starts to travel it lets the press go, and
`PackOpeningController` picks the card up instead. Both use the same `dragThreshold`; if they
ever diverge, a drag between the two values lands in a dead zone neither acts on.

A grab anywhere in a slot's catch area takes whatever is on top of that pile, so a card can
be picked up without hitting it exactly. Dropped over nothing, it flies home to the slot it
came from rather than to the pack.

The slots' catch areas tile without gaps, so anywhere along the row lands *somewhere* — only
a drop above the row counts as a miss. Piles survive a new pack (they are the point of the
scene), so `Space` only refills the booster; `C` empties the board.

The row rides at `rowRestScale` so the pack and the album own the frame, and swells as a
whole under the pointer. It **freezes at whatever size it has while a card is in hand**:
swelling under a drag slides the target slot out from under the pointer aiming at it, which
was worst on the drag straight off the stack — the row jumped to full size the moment the
card crossed into it.

### Framing

**The camera never moves.** It is parked where a full row of five piles fits even in a 4:3
view, and whatever needs a closer look comes to it instead:

- The pack and its stack hang off a **`PackRig`**, which `PackOpeningController` slides
  towards the camera by `sealedRigOffset` while the wrapper is sealed and back out once it
  tears. That offset is the old sealed camera shot turned inside out, so the sealed pack
  fills the frame exactly as it used to. Both ride the same rig because the wrapper cannot
  be close to the camera while the stack it is hiding sits back at half the size.
- The album **scales itself** into what the frame has left above the row (`FitAlbum`),
  measured against the row at its *hover* size so the two never overlap.

Because the frustum no longer moves, the wrapper works out its own exit height from the
camera as it starts to slide — at the depth it is actually falling at, which is nearer the
camera than its resting place.

The row is on screen throughout now, including during a tear, so the sealed wrapper overlaps
it instead of having a frame to itself. It therefore **shields the piles behind it**, exactly
as an open album sheet does: a press anywhere on the pack is the pack's, whether or not it
landed on the tear band, or it would reach through and lift a card off the pile underneath.

### The album

The side menu switches between two views. **Album** puts the pack and its stack away and
opens a spread of two sheets, four pockets each in a 2x2 grid; the row of piles stays on
screen, because filling the album means dragging out of it. A pocket is just a `CardSlot`
with `capacity = 1`, so dropping into one is the same code path as dropping onto a pile —
and a card can be dragged back out again. A full pocket refuses a second card rather than
swapping, and the card flies home.

Six sheets, paired into three spreads: **even sheets hang left of the spine, odd ones
right**, and `CardAlbum` re-applies that offset itself whenever it opens a spread, so what is
drawn and what `HitSide` tests cannot drift apart. Clicking the left page leafs one spread
back, the right page one forward; it stops at the covers rather than wrapping, so how much
album is left stays visible. Only the two open sheets are active, which is what hides the
cards filed elsewhere without anything reparenting them.

A press on a sheet belongs to the album, so `PackOpeningController` calls
`CardInteractor.CancelPress()` for as long as it is held — every frame, not once, because
script execution order between the two is not fixed and a single call on the press frame can
land before the press is even seen. The side is latched on the press, and only spends itself
on release if nothing was carried off: a drag out of a pocket moves the card, a click turns
the page.

The sheet is the slot shader again, with `_CornerLength` cranked past half the page so the
brackets meet into a plain rectangle, a near-opaque dark `_FillColor` and a light hairline on
top. Both materials carry an **explicit render queue** (`AlbumPageQueue` 3000, `SlotQueue`
3020). Transparent geometry otherwise sorts by distance from the camera, and a sheet whose
distance falls between its own near and far row of pockets paints the far row's frames out —
which is exactly what happened before the queues were pinned.

### The shop

The purse sits in the top right corner and is **always** on screen — it survives `H`, and it
is drawn over the dimmed frame rather than under it, because how much money there is decides
whether to open the shop at all. It is therefore not repeated inside the panel, and the HUD
box no longer carries a second copy either.

The **Shop** tab under it (or `B`) opens a panel with two shelves. It is modal: centred, over
a dimmed frame, and the whole pointer belongs to it while it is up. Close it with the button
in its header, `B` or `Esc`. The tab is only drawn while the shop is *shut* — IMGUI gives a
mouse event to whichever control asked for it first, so a tab left underneath would eat the
clicks landing on the panel over it. That whole corner blocks the world, or reading your
balance would stow the pack on the table behind it.

Row size is `rowHeight` and `iconWidth` on `ShopView`, and the picture is fitted into the
row's height — those are the knobs to turn if the previews want to be bigger still.

**Buy** sells booster packs and loose cards, one or several at a time, cheaper by the unit in
bulk. Packs go into the drawer; cards are minted on the spot and fly in from off the right of
the frame onto the **default pile** — the same slot a click on the stack sends cards to. They
arrive face up, because the shop is the one place in the scene where you know what you bought.

**Sell** is a board of **wanted ads** rather than a price list: a card is worth what somebody
is asking for it. An ad is a list of `CardWant`s — a set, a face, a suit and a minimum finish,
any of which may be "any" — so the same shape covers *the complete Copas suit*, *the 4 of
every suit*, *a holo Judgement* and *any five shiny cards*. Each row shows how far along it is
(`4/10 owned`) and only lights up its button when the collection can cover it. Filling one
takes those cards out of the piles and the pockets for good and puts a fresh ad in its place.

What makes any of this possible is that a card knows what it is. `CardView.Identity` is a
`CardIdentity` — deck, face, tier — stamped on by `CardFactory` at the moment the card is
made, because by the time a card is sitting in a pile there is nothing else left to ask: the
face rides on a `MaterialPropertyBlock` and the finish is a shared material. `CardCatalog`
turns one back into a name ("Holo Judgement"), reading the deck's own naming off `CardArtSet`
— suits and ranks for the Spanish deck, a list of titles for the tarot majors.

Only filed cards count as owned. A card still in the pack, or in mid drag, is deliberately not
in the collection — which is also what stops a sale pulling a card out from under the cursor.

The panel is IMGUI, like the rest of this scene's readouts. While it is open the world stops
answering entirely (`ShopView.Blocks` → `CardInteractor.SetBlocked`): IMGUI swallows the click
that lands on a button, but nothing tells the pack, the piles or the albums about the one that
lands beside it. A tear or a drag that was running when it opened is let go rather than left
holding.

**What is on the shelves is authored, not coded**: `Assets/Shop/ShopCatalog.asset`, edited in
the inspector. `CardPackBuilder` writes it with the shipped shelves the first time and never
touches it again, exactly as `DialogSceneBuilder` treats the dialog tree — so it survives
every later rebuild of the scene. Right-click → **Reset** puts the defaults back; delete it
and rebuild to start over.

- **Buy**: `Offers`, one element per row, used in the order they are listed. Title, copy,
  price in coins, `goods` (booster packs or random cards) and how many one purchase delivers.
  Leave `preview` empty and the row shows a wrapper off the sheet or the back of a card,
  whichever it is selling.
- **Sell**: `Ads` is a *pool*, not the board. `Ads On Board` of them are up at a time and
  filling one brings up another that is not already there, so writing more than fit gives the
  board something to rotate through. `Top Up With Rolled Ads` keeps it full with made-up ads
  once the written ones run out; turn it off to show only what is authored.

An ad is a list of `wants`, and each want is where the matching happens:

| Field | Meaning |
|---|---|
| `deck` | `Any`, `Tarot`, `Spanish Deck` |
| `face` | tarot 0–21 = files `00`–`21` (20 is the Judgement); Spanish 0–39 = suit × 10 + rank; -1 any |
| `suit` | 0 Oros, 1 Copas, 2 Espadas, 3 Bastos, -1 any. Only read when `face` is -1 |
| `finish` | `Any`, `Common`, `Shiny`, `Holo`, `Galaxy`, `Chrome` — **exact**: a galaxy card does not fill a want for a holo one |
| `count` | how many cards matching this line |

`CardDeck` and `CardFinish` (in `CardCatalog.cs`) are named front ends for the two indices a
`CardIdentity` actually carries — the deck's position in the art sets on
`PackOpeningController`, and the tier's position in the rarity materials. `Any` is -1 in both,
so they compare straight through. Add a deck in `CardPackBuilder.BuildArtSets` and it needs a
name in `CardDeck` as well; an index with no name still matches, it just cannot be picked in
the inspector.

So *the 4 of every suit* is four wants naming faces 3, 13, 23, 33 — **not** one want with
`count = 4`, which would take any four cards of that description including four of the same.
*Any five shiny* is the opposite case, and is one want with `count = 5`.

The fourteen ads the asset ships with are one of each shape — a named card, a named card in a
finish, two copies of one card, a list of two different cards, a rank across the suits, a
whole suit, a suit filtered by finish, a deck, a finish alone, and anything at all — so the
nearest one is worth duplicating rather than starting from an empty ad.

Two things are still in code because they are not content: what the *rolled* filler ads ask
for (`ShopStock.RollAd` and the four builders under it, paid in multiples of `cardValue` on
`PackOpeningController`), and the card **names** the ads are written in, which come from
`CardArtSet` as wired in `CardPackBuilder.BuildArtSets` and need a scene rebuild to change.
Everything on the asset is picked up on the next Play.

`Tools > Cozy TGC > Build Pack Opening Scene` regenerates the wrapper meshes, the light bar
quad, every material, the pack and slot prefabs and the scene, and adds the scene to the
build settings. It needs
`Card.prefab`, `CardQuad.asset` and the `Card_*` materials, so run **Build Demo Scene** first
in a fresh clone.

### How the wrapper tears

The wrapper is two meshes split along one shared jagged seam: `PackBody` and `PackLid`, the
strip covering the crimped seal. Both are built as a run of column quads with flat tops, one
per two pixels, so the rip is a pixel staircase rather than a smooth diagonal, and both read
the same seam array so they interlock exactly while sealed.

The lid's pivot sits on the seam, and `CardPack.shader` lifts each column in the vertex stage
by how much of the seam that column has lost (`_PeelMin`.._`PeelMax`, feathered so the strip
stays attached either side of the rip and bulges towards the cursor). Two details keep that
from falling apart:

- **The hinge ramp (`uv1.y`) is a shared linear function of height**, not a per-column 0..1
  ramp. Neighbouring columns are cut at different heights, so a per-column ramp sends the
  same point on their shared edge to two different depths and perspective opens it into a
  hairline crack. The shader must not clamp it either, for the same reason.
- **The pack samples mip 0 outright** instead of the card's `SAMPLE_TEXTURE2D_GRAD`. It is
  only ever magnified, and with a quad every two pixels most rasterizer quads straddle a
  triangle edge, so any pixel that guessed a coarser mip would pull in the sheet's gutters.

### How the cut is lit

Three things light the seam, and only the first is on the wrapper itself.

- **`PackCutLight` in `CardPackInput.hlsl`** is the light coming through the seam. Its band is
  stepped on whole texture pixels, like the torn lip beside it, because a smooth falloff over
  pixel art reads as a glow pasted on top rather than as light coming through. It is gated by
  the same peel shape the tear is, so the seam only lights where the sweep has been, and it is
  added *after* the back-face shade — a cut goes through the foil, so the far side of a
  tumbling strip keeps its rim.
- **`PackFlash.shader`** draws the bar and the dim, two materials off one shader with a uniform
  branch on `_Mode` and blend state as a material property (`PackFlare.mat` adds, `PackDim.mat`
  multiplies). The bar is on `PackFlare.asset`, a quad four pack widths across whose **UVs are
  in pack space** — u is 0 and 1 on the wrapper's own edges and carries on past them, which is
  what lets the shader be handed the sweep in the same 0..1 the tear is measured in, and lets
  the flash spill past the pack without anything resizing. It draws `ZTest Always`.
- **The dim is geometry, not a post effect.** It is a frame-filling quad parked `dimDepth`
  behind the pack and refitted to the frustum every frame, multiplied over what is already
  drawn with an ordinary depth test. Everything further from the camera — table, piles, album
  — darkens; the wrapper occludes it and stays lit. That is also why it hangs off the pack's
  root rather than under `Visual`: the wrapper's lean must not carry the frame with it.

`_Flutter` is the ripple on the strip once it is off. It is driven off `uv.x` and the shared
hinge ramp rather than anything per column, for the same reason the peel is — neighbouring
columns have to agree on their shared edge, or the wave shears the strip into stripes. The
strip flies in **world space**: `CardPackView.DetachLid` reparents it out of the hierarchy,
because the body drops away underneath it and the rig they both ride pulls back to the table,
and a piece thrown clear must not be dragged along by either. `Gone` is therefore about the
pack being out of the way, not about the object being finished — the strip is still in the
air, and needs an `Update` to fly it.

**The throw is sideways on purpose.** A sealed pack fills the frame top to bottom, so there
is barely 0.2 units of headroom above it and about four times as much room to cross — thrown
straight up, the strip is out of shot in an eighth of a second and nobody sees the cut edge
at all. `lidLaunch` arcs it across the upper corner instead, peaking just under the top of
the frame and leaving through the side after roughly a second, which overlaps the wrapper's
own slide. `CardPackView.LidOffScreen` therefore tests all four edges at the strip's own
depth, not just the top.

Both cut edges are lit, and on **separate clocks**: `bodyGlow` holds while the opened wrapper
is standing there and only goes out as it drops, `lidGlow` lasts the strip's whole flight,
which outlives the pack. The hot spot at the sweep's head is switched off the moment the seam
gives (`LiveHead`) — there is no point being cut any more, so the whole edge lights evenly
rather than keeping a bright mark wherever the drag happened to finish.

`CardPacks.png` is one sheet of 9 x 20 wrappers, 84x154 px each. It is never sliced into
sprites: `CardPackSheet` hands the shader a UV rect and one material draws all 180 of them.

The pack, the stack and the slots are all hit-tested analytically — they are flat quads
facing the camera, so projecting the ray onto their plane beats fitting colliders around
them. Colliders are instead used as the handover to `CardInteractor`: cards in the booster
stack, cards still flying into a slot and the card currently being carried all have theirs
switched off, and a slot only enables the collider on the card at the top of its pile. So the
interactor only ever sees the card you can actually reach.

`CardView.idleMotion` is switched off for every card in this scene (`CardPackDeck.Fill`).
The resting sway keeps a lone card on a table from reading as dead, but a pile of them all
breathing at slightly different phases reads as wobbling, not as a stack. Hover still tilts
whatever is on top, which is what keeps the foil moving. The demo scene leaves it on.

`CardView.stackClearance` is switched **on** for the same cards, plus every card a slot
takes in. Pile steps are ~1.2 cm of depth, while a card tilted 14° swings more than 20 cm
and one turning over in place sweeps its full half width — so any card that rotates in a
pile cuts straight through the cards behind it. With clearance on, `DepthSwing()` measures
how far the furthest corner reaches back at the current rotation and the card rides out by
at least that much, so it always leans *over* the pile instead of into it. A card at rest
gets no offset, so stacks look unchanged until something actually turns. Leave it off for
cards that stand alone — the demo scene does.

The slot frame is drawn by `CardSlot.shader` rather than a texture: corner brackets at rest,
a full outline when highlighted, measured in whole slot pixels so it stays on the same grid
as the pixel art sitting inside it instead of going soft when the frame is scaled.

## How the foil works

Everything derives from one vector, computed per pixel in `CardHoloInput.hlsl`:

```hlsl
float3 vT   = float3(dot(V,T), dot(V,B), dot(V,N));  // view dir in tangent space
float2 tilt = vT.xy / max(vT.z, 0.15);               // 0 facing the camera, grows when turned
```

Five layers are driven by `tilt`, which is why the whole effect reacts to rotation:

1. **Rainbow diffraction** — hue from a grating phase built out of the UV *and* the tilt, parallax-shifted so it sits under the artwork.
2. **Sweep** — a gaussian specular bar that slides across the face as the card turns.
3. **Sparkle** — hash-based glitter flakes, each with a random *preferred* viewing angle, so they pop in and out rather than scrolling.
4. **Chrome** — analytic sky/ground/sun environment sampled by the reflection vector, damped head-on so it doesn't flatten the art.
5. **Fresnel** — glossy edge glow.

Two settings keep it reading as pixel art rather than a gradient pasted on top:
`_HoloPixelate` snaps the foil UVs to the card's texel grid, and `_RainbowSteps` posterises the hue.

## Material parameters

| Group | Key properties |
|---|---|
| Card | `_FrontTex`, `_BackTex`, `_CardPixels` (73×113), `_Cutoff`, `_PixelAA` |
| Mask | `_UseMaskTex` + `_MaskTex`, or `_MaskFromLuma` / `_MaskContrast` / `_MaskBias` |
| Global | `_FoilIntensity`, `_FoilBlend` (additive→screen), `_TiltGain`, `_HoloPixelate`, `_ColorSteps` |
| Rainbow | `_RainbowStrength`, `_RainbowScale`, `_RainbowAngle`, `_RainbowTilt`, `_RainbowDepth`, `_RainbowSteps`, `_RainbowDrift`, `_RainbowSat` |
| Sparkle | `_SparkleStrength`, `_SparkleColor`, `_SparkleDensity`, `_SparkleSize`, `_SparkleSpread`, `_SparkleDepth`, `_SparkleSeed` |
| Sweep | `_SweepStrength`, `_SweepColor`, `_SweepWidth`, `_SweepAngle`, `_SweepTravel`, `_SweepOffset` |
| Chrome | `_ChromeStrength`, `_ChromeSky`, `_ChromeGround`, `_ChromeSun`, `_ChromeSharp`, `_ChromeSunDir` |
| Edge | `_FresnelStrength`, `_FresnelPower`, `_FresnelColor` |
| Wear | `_WearTex`, `_WearBackTex`, `_WearAmount`, `_WearSteps`, `_StockColor`, `_InkLossDesat`, `_ScuffFoilLoss`, `_ScuffHaze`, `_ScuffGlint`, `_DentDepth` |
| Bending | `_CardWorldSize`, `_DentDisplace`, `_Bow`, `_CornerBend`, `_CornerRadius` |

**Rarity tiers** are just materials. `Card_Common` has `_FoilIntensity = 0`; add a tier by
duplicating a material and assigning it to the card's `MeshRenderer`. Per-card variation
(sparkle seed, artwork) goes through `MaterialPropertyBlock` — see `CardView.SetFaces` /
`SetFloat`.

**Foil masks**: with no mask texture the foil is masked by artwork luminance
(`_MaskFromLuma`). For per-card control, author a mask PNG, assign `_MaskTex` and
enable `_UseMaskTex` so only the frame or a sigil foils.

## Wear and restoration

Step by step, with a card in front of you: `CARD_WALKTHROUGH.md` parts 2 and 3.

Five kinds of damage — scuffs and scratches, ink coming off, dents, creases, chipped edges —
all ride in **one RGBA map per face** at the card's own 73×113, plus three scalars for the
bending. `CardWear` owns both and is the only thing that writes them.

| Channel | Damage | What it does in the shader |
|---|---|---|
| R | scuff, scratches | kills the foil mask, then adds haze and a glint the foil intensity cannot scale away |
| G | ink loss | desaturates, then fades towards `_StockColor` |
| B | height, 0.5 flat | neighbour taps → tangent normal; also displaces the mesh through `_DentDisplace` |
| A | missing material | joins the artwork's own alpha in the `clip`, in **both** passes |

R and G are per face, so a card has to be restored on both sides. **B and A always come from
the front map**: a dent goes through the paper and a chip is missing from both sides. That is
not tidiness — `DepthOnly` only ever sees the front, and a chip it could not see would write
depth over a pixel the forward pass had already clipped through.

**The height channel is the whole trick.** Every foil layer is driven by `tilt`, so perturbing
the normal breaks the rainbow, the sweep, the sparkle aim and the chrome over a dent at once,
without any of them knowing wear exists. A creased holo card loses its bands along the fold
and catches a hard line instead. `_ScuffFoilLoss` does the same job the blunt way, by taking
the foil mask down where the surface is abraded.

Nothing is smoothed. Wear is sampled through the same pixel-snapped UV the artwork is, so wear
texels land on art texels, and the dent normals are deliberately blocky — one normal per card
pixel. A smooth bump map over pixel art reads as a different card. `_WearSteps` posterises the
surface channels for the same reason it posterises the hue, and because damage in visible steps
is what makes a rub read as progress: a scratch goes four texels, three, two, gone.

**Bending is geometry**, which is why `CardQuad.asset` is a 16×24 grid and not the four-vertex
quad it started as — a vertex shader can only move vertices that exist. `_Bow` bows the whole
card on either axis, `_CornerBend` lifts one corner each, and creases deep enough to matter are
read out of the height channel with a *linear* filter (the fragment stage uses the point one)
so the fold does not stair-step across the mesh. `CardApplyBend` rebuilds the normal and tangent
from three evaluations of the displacement rather than a hand-derived gradient; `DepthOnly` runs
`CardApplyBendPosition`, which must agree with it exactly.

`#pragma target` is **3.5**, not 3.0, because the vertex stage samples a texture now.

**The map lives on the CPU.** It is 73×113: a stroke touches a few hundred texels and the whole
thing uploads in a fraction of a frame, so generation, grading and saving stay plain C# with no
readback and no ping-pong buffer. The texture is created **linear, point, clamp** — it is data,
and a gamma curve on it would bend every rate the tools rub at.

### Restoring

`CardWear.Rub` is the only edit path there is, in both directions: `Age` stamps damage in,
a tool rubs it back out. `Age` also takes an optional `Damage` mask, for stamping one generator pass
on its own — the passes share one random stream, so a subset is its own card rather than a layer of
the full one, and `Damage.All` is bit-identical to what it always was. The full-scale constants below
are measured against `All` and nothing else. A tool (`RestorationTool`) is nothing but rates, and every one of them
damages something else while it works — that is what puts the steps in an order.

| Tool | Takes off | Puts back |
|---|---|---|
| Soft Cloth | light scuff, down to `scuffFloor` 0.4 | — |
| Abrasive Pad | scuff, all of it | ink loss |
| Burnishing Bone | dents and creases | scuff |
| Press | bow and corner bend, whole card | — |
| Paper Fill | chips | ink loss, the fill arrives blank |
| Touch-Up Pen | ink loss | scuff, if overworked |

Every tool also has `overworkScuff`, charged on any texel where it has nothing left to do. That
is the rule that makes a light touch worth having, and it is why the intended order — press,
burnish, abrade, fill, re-ink, polish — is enforced by the rates rather than by any check.

`CardCondition` reads the map back as one number and a grade; `PriceMultiplier` is what the shop
should move `CardData.price` by. Saving stores the seed and the stroke list, never the map:
generation and rubbing are both deterministic, so replaying them lands on the same texels.

**The raw channel means are tiny** — damage concentrates, so a chipped corner is sixty texels out
of eight thousand. Scored against 1, every card in the game grades Mint. Each channel is therefore
divided by what a severity-1 card actually averages (`ScuffFull` and friends, measured, not
guessed). Change how ageing works and those constants have to be re-measured, or the grading
silently flattens again.

`Tools > Cozy TGC > Render Wear Preview` writes `CardWearPreview.png`: the same card aged, turned,
restored and flipped, one per frame, and it logs the ramp those constants come from. As it stands:

| | 0.00 | 0.30 | 0.55 | 0.85 | 1.00 |
|---|---|---|---|---|---|
| aged | Mint | Near Mint | Good | Played | Poor |

A clumsy full-card pass with every tool takes an 0.85 card from Played back to Good. It does not
reach Mint and should not: dents and bending come out completely, scuffs and ink loss only partly,
and **chips barely move at all** — the fill has to be dwelled on a chip, and sweeping it round the
whole border spends its time where there is nothing to fill. A chipped card staying cheap forever
is the intended outcome.

## Orientation convention

The card mesh faces **-Z**, matching Unity's built-in Quad: the visible face is
`-transform.forward`, and the camera sits on -Z with an unrotated transform. Getting this
backwards mirrors the artwork on screen.

The root object never tilts — only the `Visual` child does. Pointer position is projected
onto a plane that follows the card's *position and scale* but never its rotation, so the
tilt cannot feed back into the pointer.

Hover detection uses a `BoxCollider` on the root, resized every frame by
`CardView.SyncCollider` to track the popped card: a rest-sized collider lets the cursor sit
on the visibly enlarged card while missing the hit box, which drops it straight back out of
hover. The box stays axis-aligned (a collider that tilted with the card would flicker at the
edges) and `hoverPadding` adds hysteresis. If you raise `hoverScale` or tighten card
spacing, check that neighbouring hover boxes still do not overlap.

## Pixel art import

`PixelArtCardImporter` stamps textures under `Assets/Resources/`: Sprite, 100 PPU,
uncompressed, alpha-as-transparency, mips with coverage preserved, and no downscaling of
oversized sheets.

Filtering is **bilinear, not point**, on purpose. The cards are rotating 3D quads, so point
filtering crawls. `CardPixelUV` in the shader snaps sampling to texel centres with a
one-screen-pixel ramp on the seams, which stays crisp at any angle while the hardware still
filters minification. Because that UV is deliberately discontinuous, the base texture is
sampled with `SAMPLE_TEXTURE2D_GRAD` using the untouched UV derivatives — otherwise mip
selection breaks and the card collapses to a flat colour.

The postprocessor only stamps *new* assets (`importSettingsMissing`); run
`Tools > Cozy TGC > Apply Pixel Art Import Settings` to force it over existing ones.
