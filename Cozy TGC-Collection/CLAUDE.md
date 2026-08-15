# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Unity **6000.3.17f1** with **URP 17.3**. Rotatable pixel-art TCG cards with a view-dependent holo foil, a booster-pack opening scene and a dialog overlay driven by the **Dialog Builder** package. Not a git repository. No tests and no `.asmdef` files — everything compiles into `Assembly-CSharp` (runtime) and `Assembly-CSharp-Editor` (`Assets/Editor/`).

`CARD_SYSTEM.md` is the detailed reference for the foil layers, the pack tear meshes, the material parameter table and the pixel-art import rules. Read it before touching shaders, pack meshes or texture import settings — it documents non-obvious constraints (mip selection, hinge ramps, filtering choices) that look like mistakes otherwise.

## Commands

Normal workflow is the Unity Editor: open `Assets/Scenes/CardHoloDemo.unity` or `Assets/Scenes/PackOpening.unity` and press Play. Editor entry points live under the `Tools > Cozy TGC` menu.

Batch mode (the Editor must be **closed** — it locks the project):

```bash
/Applications/Unity/Hub/Editor/6000.3.17f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -nographics -projectPath "/Users/markuller/Desktop/Unity/Other/Cozy TGC-Collection" -logFile - -executeMethod CozyTGC.EditorTools.CardDemoBuilder.BuildDemo
```

| `-executeMethod` | Menu item |
|---|---|
| `CozyTGC.EditorTools.CardDemoBuilder.BuildDemo` | Build Demo Scene |
| `CozyTGC.EditorTools.CardPackBuilder.BuildPackScene` | Build Pack Opening Scene |
| `CozyTGC.EditorTools.CardAssetBuilder.BuildCardAssets` | Build Card Assets |
| `CozyTGC.EditorTools.CardAssetBuilder.ResetCardValues` | Reset Card Prices and Names |
| `CozyTGC.EditorTools.ShopCatalogBuilder.ResetCatalog` | Reset Shop Catalog |
| `CozyTGC.EditorTools.DialogSceneBuilder.BuildDialogScene` | Build Dialog Scene |
| `CozyTGC.EditorTools.CardPreviewRenderer.RenderPreview` | Render Foil Preview |
| `CozyTGC.EditorTools.PixelArtCardImporter.ApplyToAllCardTextures` | Apply Pixel Art Import Settings |

`RenderPreview` renders through a camera, so drop `-nographics` for it. It writes `CardFoilPreview.png` next to `Assets/`, overridable with the `COZY_PREVIEW_PATH` env var — the fastest way to eyeball a shader change without a human pressing Play.

## Generated assets

`Assets/Meshes/`, `Assets/Materials/`, `Assets/Prefabs/` and all three demo scenes are **generated** by the editor builders, which delete and recreate them. Hand edits to those assets are lost on the next build. Change the builder, or duplicate the asset under a new name.

`Assets/Dialogs/`, `Assets/Shop/` and `Assets/Cards/` are the exceptions: `DialogSceneBuilder` creates the sample tree and the `CharacterData` assets, `ShopCatalogBuilder` creates `ShopCatalog.asset`, and `CardAssetBuilder` creates the `CardDeckData` and `CardData` assets, **only when they are missing** — never touching them again, because those are authored content (dialog lines, portraits, the node graph; the shop's shelves and wanted ads; every card's price and pull weight). `CardAssetBuilder` re-syncs one thing on every run: a deck's `back` and the *order* of its `cards`, which has to keep following the artwork folder because `CardIdentity.face` indexes it.

Build order matters in a fresh clone: `CardPackBuilder` needs `Card.prefab` and the `Card_*` materials, so run **Build Demo Scene** before **Build Pack Opening Scene**. The card assets need no ordering — **Build Pack Opening Scene** calls `CardAssetBuilder.LoadOrCreateDecks` itself, and the menu item is only for making them without rebuilding a scene. `DialogSceneBuilder` is independent of all of it.

## Architecture

Runtime code is `Assets/Scripts/Runtime/` in namespace `CozyTGC`; editor code is `Assets/Editor/` in `CozyTGC.EditorTools`.

**The pack scene has a floor.** `SceneBackdrop` is one unlit quad on `Table.mat` (the table picture in `Resources/`), built by `CardPackBuilder` like everything else in that scene — hand-placing a background there would be lost on the next rebuild. It *covers* the frame rather than stretching to it, refitting itself when the aspect changes, and the camera's solid clear colour stays as the ground behind it.

**Two scenes, one card.** `CardView` + `CardInteractor` are shared. `CardDemoController` is the shader test bench; `PackOpeningController` drives the pack loop (`CardPackView` wrapper → `CardPackDeck` stack → dealt `CardView`s).

**Root/Visual split** — the invariant both `CardView` and `CardPackView` are built on: the root transform never rotates, a `Visual` child does all tilting and spinning. Pointer rays are projected onto a plane derived from the *root*, so tilt cannot feed back into the pointer position. Breaking this produces jitter that looks like a smoothing bug.

**Orientation** — the card mesh faces **-Z** (matching Unity's built-in Quad). The visible face is `-transform.forward`, the camera sits on -Z unrotated, and "towards the viewer" is -Z. Getting this backwards mirrors the artwork.

**Hit testing is deliberately split.** Dealt cards use a `BoxCollider` + `Physics.Raycast` through `CardInteractor`. The pack, the undrawn stack, the shelf and the pack drawer are tested analytically (`TryGetLocalPointer` / `OverDeck` / `PackTray.Hits` project the ray onto a plane) because they are flat camera-facing quads. Collider `enabled` is the ownership flag: cards in the stack have theirs off, `CardPackDeck` switches it on when a card lands, which is exactly when `CardInteractor` starts seeing it.

`CardView.SyncCollider` resizes the box every frame to track the hover pop. A rest-sized collider would let the cursor sit on the visibly enlarged card while missing the hit box.

**Material overrides go through `MaterialPropertyBlock`** (`CardView.SetFaces` / `SetFloat` / `GetFloat`), never by instancing materials. Rarity tiers are just materials — `CardFactory` assigns `Renderer.sharedMaterial` per card, and per-card variation (artwork, sparkle seed) rides on the property block.

**A card knows what it is.** `CardView.Identity` is a `CardIdentity` (deck, face, tier) stamped on by `CardFactory`, which is the only thing that mints cards — `CardPackDeck.Fill` for a booster, `PackOpeningController.Buy` for the shop, both from a `CardDraw`. It has to be carried rather than derived because a filed card gives nothing away: the face is on a property block and the finish is a shared material. `CardCatalog` names one back ("Holo Judgement") out of `CardDeckData`, and resolves it to the `CardData` asset the card is a copy of — which is where its price is.

**The shop** takes the whole frame less `edgeMargin`, and its purse-and-tab corner is drawn *before* the panel and stays up while it is open — the tab toggles, so the same button opens and closes it. That corner overlaps the panel, so `DrawHeader`/`DrawTabs` are handed its width and end short of it: IMGUI gives a click to whichever control asked for it first, and nothing of the panel's own may hide under the tab. `ShopView` (IMGUI panel, draws only) + `ShopStock` (stocks the two shelves from `ShopCatalog.asset`, and rolls filler ads when the written ones run out) + `CardCollection` (what is owned, and taking it away). What is *on* the shelves is authored in the asset, not in code — offers are used in order, ads are a pool the board rotates through. A row about one particular card is written by dropping its `CardData` into `ShopOffer.card` (with `goods = NamedCard`) or `CardWant.card` — there is no face number any more, and `deck`/`suit` survive only for the wants that deliberately name no card ("any three shiny swords"). `CardCatalog.Locate` is the reverse lookup that turns such a card back into the `(deck, face)` a minted card carries. Nothing picks a row's picture either: `ShopStock.IconFor` derives it from what the row is selling or asking for. A row that sells a card names its print too — `ShopOffer.finish` starts on `Common`, and `Any` (which rolls it per copy) is kept for later and flagged in the inspector; a `CardWant.finish` of `Any` is the ordinary case and means the collector does not mind. Because the shipped shelves reference card assets, they live in `ShopCatalogBuilder` (editor) rather than in a runtime `FillWithDefaults` — **Reset Shop Catalog** is what puts them back, and it overwrites. A `CardWant`/`ShopOffer` inspector row hides the fields that do not apply (`ShopCatalogDrawers`). `PackOpeningController` is the `ShopView.ICustomer`: it holds the purse and the drawer of packs, mints bought cards onto the default slot, and hands over the ones a wanted ad asks for. Only cards filed in a pile or an album pocket count as owned — a card in the pack or in mid drag is not. Packs come out of `PackTray`, the drawer under the shelf; `PackOpeningController.OpenNextPack` is the only way to put one on the table, and it spends inventory.

**Input** — `CardInteractor.PointerPosition()` / `PointerPressed()` are static and handle both the new Input System and the legacy manager behind `ENABLE_INPUT_SYSTEM`. `PackOpeningController` calls them rather than reading input itself. Any new input path should keep both branches compiling.

**Every card is an asset.** `CardData` (face, name, `price`, `weight`) under `Assets/Cards/<deck>/`, held in face order by a `CardDeckData` (back, `packWeight`, the suit/rank naming scheme). The pack scene reads those; nothing loads card artwork at run time any more. Adding a deck means adding a `Resources/Cards/` folder, a `DeckSpec` in `CardAssetBuilder` and a `CardDeck` enum member, then running **Build Card Assets** — the convention (`CardArtLibrary.Load`: the texture named `back` is the back, the rest sort numerically) now only decides the order the cards are laid out in, and the shader test bench still swaps whole folders with it.

A `DeckSpec` carries the *scheme* a new card is made with — its name, and `price`/`weight` as functions of the face index. The Spanish deck prices by suit (coins 25, cups 20, swords 15, wands 10) with the four cards above the numbers climbing 3 a step, jack → knight → king → ace, at weight 1 throughout; the tarot runs 100 to 184 in fours and thins from 0.1 on the Fool to 0.01 on the World. Because a built card is then authored content, changing that scheme reaches existing cards only through **Reset Card Prices and Names**, which is the one editor action here that overwrites hand edits — it asks first.

**Three rolls make a card**, narrowing, in `PackOpeningController.RollCard`: which deck by `CardDeckData.packWeight`, which card of it by `CardData.weight`, which finish by `CardFinishOdds`. The finish rule is one shared curve rather than a table — each step along Common → Shiny → Holo → Galaxy → Chrome takes another `FalloffPerFinish` (0.2) off the card's odds, so chrome lands at a fifth of the common's rate. A card at `weight` 0 is never dealt, which is how a card exists for the album without being in packs. `CardData.price` is what a collector pays: the rolled ads in `ShopStock` are multiples of the prices of the cards they name, and the written ads on `ShopCatalog` carry their own flat price and ignore it.

**A pack mixes decks.** The deck is rolled per card from `packWeight`, so a booster is mostly Spanish with a tarot card in roughly every second one. That forces one shared back per pack — the two decks' backs look nothing alike, and dealing each card its own would show the rare card through the face-down stack. `CardPackDeck.Take` swaps a card back to its own `CardView.DeckBack` as it leaves, face up, where the change cannot be seen.

**Dialog** — `DialogSystem.prefab` carries both halves: the package rig (`DialogBuilderHQ` + `DialogTreeRunner` + `DecisionHandler`) and the HUD under its own canvas. Nothing on the package side is wired by hand except `DialogBuilderHQ.treeRunner`; HQ scans the scene for `IDialogInterface` implementations at Start, which is how `DialogHud` (`IDialogReceiver`), `DialogChoiceList` (`IDialogOptionReceiver`, Player) and `DialogSession` (`IDialogStarter` + `IDialogTreeSetter`) get connected. Start conversations through `DialogSession.Play(tree)`, never by poking the runner.

Which portrait lights up comes from the **node type** on `DialogTreeRunner.DialogNodeSelected`, not from the name passed to `DisplayDialogLine` — the runner hard-codes `"Marlene"` for every player line, so name matching would break on a rename.

Two things about the package itself, both load-bearing:

- It has an undeclared dependency on **`com.cod.audioplayer`** (which in turn needs **Odin Inspector**, `Assets/Plugins/Sirenix/`). `DialogTreeRunner` holds a serialized `MarkerManager` field and the asmdef references that package by GUID `f658efd66cf1847239955bbdac42f6d6`, but `package.json` does not list it — so it has to stay in `Packages/manifest.json` by hand. Remove it and Dialog Builder fails with CS0246, which takes **the whole project** down with it. Nothing else in the project uses the audio tooling; the marker asset exists because the runner reads that field without a null check as soon as a node carries an `AudioClip`.
- `DialogTreeRunner.Update` reads the legacy `UnityEngine.Input`, which throws while Active Input Handling is *Input System Package (New)*. Player Settings must stay on **Both**.

**`CardPackSheet`** is the single source of truth for the `CardPacks.png` atlas (9x20 wrappers, 84x154 px). The sheet is never sliced into sprites — one material draws all 180 wrappers, picked by UV rect. Column origins are a hard-coded array because the eighth column is off-pitch by one pixel.

**`AlbumBookSheet`** does the same job for `Resources/Collectors Albums/`: the seven 190x160 frames of `RADL_Book_*.png` (three colours, three albums) and the 4x3 grid of 32x32 icons in `exp book.png` that the `AlbumShelf` menu on the left is cut from. It also **defines the album's geometry** — the page rect and the album's origin (the spine, on a half pixel) are measured off the open book art, so `CardPackBuilder` derives page size, pocket pitch and the pocket's scale from them rather than choosing numbers. A page carries a 2x2 grid of pockets scaled so the slot *frames* tile the parchment, not the cards, or neighbouring frames double up their fill along the seams. Note the album is fitted on screen by `Art*` — the union of the opaque pixels across all seven frames — and not by the sheet cell, whose blank margins would otherwise eat into the frame. Changing the art means changing the constants there, not the builder. `CardAlbum` is a state machine that waits on `AlbumBook`'s clip before it puts a spread up, which is also what hides the swap during a page turn.

**`UiSheet` + `UiSkin`** are the same idea for the screen-space chrome, cut from the kit in `Resources/UI/`. The palette is deliberately darker than the kit's own art: the project renders in **linear** colour, where small anti-aliased type on a light ground draws thinner and paler than the swatch says, which reads as the panel being half transparent when it is fully opaque. `Ink` is ~10:1 on a panel, and row copy uses it rather than `InkDim`.  The pack scene's IMGUI — shop panel, purse, tab, HUD box, the corner button to the dialog scene — all draw through `UiSkin`, which also owns the one screen scale they share. The constraint that shapes it: **IMGUI nine-slices a whole texture**, so a `GUIStyle` background cannot be a rect inside an atlas the way a shader's UV rect can. Pieces are therefore cut into their own small textures (`UiSheet.Cut`), which is why those sheets must be **readable** and why `Resources/UI/` is the one corner of `Resources/` that wants `UiKitImporter`'s settings (point, no mips, readable) instead of `PixelArtCardImporter`'s. Pieces are cut at an integer zoom so a two pixel border does not stay two pixels wide on a panel drawn at twice the size. Icons are cut from `white icons.png` and tinted at draw time — one sheet, any colour.

Run **Tools > Cozy TGC > Apply UI Kit Import Settings** after adding art to `Resources/UI/`; a sheet that is not readable logs an error and leaves the panel it backs invisible.

## Shaders

`CardHolo.shader` / `CardPack.shader` / `SpriteSheet.shader` each pair with an `*Input.hlsl` holding the CBUFFER, samplers and the actual math. All have a `UniversalForward` and a `DepthOnly` pass — keyword pragmas must be kept in sync across both passes. `SpriteSheet.shader` is the plain atlas quad (album books, shelf icons) and deliberately has no keywords at all, so its pixel filter is always on.

Keyword-backed features (`_PIXELAA`, `_MASKTEX`, `_HOLOPIXELATE`) need **both** the float property and the shader keyword set together; see `CardDemoBuilder.SetToggle`. Setting only the float silently does nothing.
