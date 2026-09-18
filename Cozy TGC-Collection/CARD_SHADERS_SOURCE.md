# Card shaders — complete source

Every shader file in `Assets/Shaders/`, copied verbatim. Put them all in one folder: the `.shader`
files include their `*Input.hlsl` by relative path, and `CardHoloInput.hlsl` includes
`CardWear.hlsl` and `CardDebug.hlsl` the same way.

Requires URP 17 (Unity 6000.3). Generated from the files on disk — if a shader changes, regenerate
this rather than editing it by hand.

| Shader | Files | Used for |
|---|---|---|
| `Cozy TGC/Card Holo` | `CardHolo.shader`, `CardHoloInput.hlsl`, `CardWear.hlsl`, `CardDebug.hlsl` | the card: pixel snapping, foil, wear, bending, debug views |
| `Cozy TGC/Card Overlay` | `CardOverlay.shader`, `CardWear.hlsl` | explainer wireframe / vertices / tangent frames |
| `Cozy TGC/Card Pack` | `CardPack.shader`, `CardPackInput.hlsl` | booster wrapper and its tear |
| `Cozy TGC/Pack Flash` | `PackFlash.shader`, `PackFlashInput.hlsl` | pack opening flash |
| `Cozy TGC/Card Slot` | `CardSlot.shader` | slot frames |
| `Cozy TGC/Sprite Sheet` | `SpriteSheet.shader`, `SpriteSheetInput.hlsl` | one atlas cell on a quad: albums, icons |

**Include order matters.** `CardWear.hlsl` and `CardDebug.hlsl` read material uniforms, so they must
be included *after* the `UnityPerMaterial` CBUFFER and the texture declarations — which is why
`CardHoloInput.hlsl` includes them at the bottom, and why `CardOverlay.shader` declares every
uniform `CardWear.hlsl` touches even where nothing calls it.

**Keywords need float + keyword together.** `_PIXELAA`, `_MASKTEX`, `_HOLOPIXELATE` are
`shader_feature`s: setting the material float without `EnableKeyword` does nothing.

---

## `CardHoloInput.hlsl`

```hlsl
#ifndef COZY_CARD_HOLO_INPUT_INCLUDED
#define COZY_CARD_HOLO_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Everything the material owns lives in UnityPerMaterial so the SRP Batcher stays happy.
CBUFFER_START(UnityPerMaterial)
    float4 _FrontTex_ST;
    float4 _BackTex_ST;
    float4 _MaskTex_ST;
    float4 _Tint;
    float4 _CardPixels;
    float  _Cutoff;
    float  _PixelAA;

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

    float4 _CardWorldSize;
    float4 _StockColor;
    float4 _Bow;
    float4 _CornerBend;
    float  _WearAmount;
    float  _WearSteps;
    float  _ScuffFoilLoss;
    float  _ScuffHaze;
    float  _ScuffGlint;
    float  _InkLossDesat;
    float  _DentDepth;
    float  _DentDisplace;
    float  _CornerRadius;

    /// <summary>0 is off. Anything else is a view from CardDebug.hlsl.</summary>
    float  _DebugView;
CBUFFER_END

TEXTURE2D(_FrontTex);   SAMPLER(sampler_FrontTex);
TEXTURE2D(_BackTex);
TEXTURE2D(_MaskTex);
TEXTURE2D(_WearTex);    SAMPLER(sampler_WearTex);
TEXTURE2D(_WearBackTex);

#include "CardWear.hlsl"
#include "CardDebug.hlsl"

// ---------------------------------------------------------------------------
// Pixel art sampling
// ---------------------------------------------------------------------------
// Snaps the UV to texel centres but keeps a one-screen-pixel wide ramp on the
// texel seams. With bilinear filtering this reads as crisp pixel art that does
// not crawl or shimmer while the card rotates.
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

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
float3 CardSpectrum(float t)
{
    return saturate(0.5 + 0.5 * cos(6.28318530718 * (t + float3(0.0, 0.33, 0.67))));
}

float2 CardHash22(float2 p)
{
    float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// Glitter flakes. Every flake gets a random preferred viewing angle, so it only
// fires when the card is tilted towards it - that is what makes the sparkle
// pop in and out while turning instead of just scrolling around.
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

            float2 pref = (CardHash22(cell + o + 19.19) - 0.5) * 2.0 * spread;
            float aim = saturate(1.0 - length(tilt - pref) * 1.5);

            acc = max(acc, shape * aim * aim);
        }
    }
    return acc;
}

// ---------------------------------------------------------------------------
// The foil itself
// ---------------------------------------------------------------------------
// tilt is the parallax vector derived from the view direction in tangent space:
// zero when the card faces the camera, growing as it is turned away. Every
// layer below is driven by it, which is why the whole effect reacts to rotation.
float3 CardFoil(float2 uv, float2 tilt, float ndv, float3 reflectDir, float3 baseColor, CardWear wear)
{
    float2 hUV = uv;
#ifdef _HOLOPIXELATE
    hUV = (floor(uv * _CardPixels.xy) + 0.5) / max(_CardPixels.xy, 1.0);
#endif

    // 1 - rainbow diffraction, parallax shifted so it sits under the artwork
    float2 dirR = float2(cos(_RainbowAngle), sin(_RainbowAngle));
    float2 rUV = hUV + tilt * _RainbowDepth;
    float phase = dot(rUV - 0.5, dirR) * _RainbowScale
                + dot(tilt, dirR) * _RainbowTilt
                + _Time.y * _RainbowDrift;
    float hue = frac(phase);
    if (_RainbowSteps >= 1.0) hue = floor(hue * _RainbowSteps) / _RainbowSteps;
    float3 rainbow = CardSpectrum(hue);
    rainbow = lerp(dot(rainbow, float3(0.299, 0.587, 0.114)).xxx, rainbow, _RainbowSat);

    // 2 - specular bar that slides across the card as it tilts
    float2 dirS = float2(cos(_SweepAngle), sin(_SweepAngle));
    float along = dot(hUV - 0.5, dirS);
    // Offset parks the bar near an edge when the card is at rest, so it travels
    // across the face while turning instead of sitting in the middle.
    float center = _SweepOffset + dot(tilt, dirS) * _SweepTravel;
    float sweep = exp(-pow(abs(along - center) / max(_SweepWidth, 1e-3), 2.0) * 2.0);

    // 3 - glitter
    float sparkle = 0.0;
    if (_SparkleStrength > 0.0)
    {
        sparkle = CardSparkles(hUV + tilt * _SparkleDepth, tilt,
                               _SparkleDensity, _SparkleSize, _SparkleSpread, _SparkleSeed);
    }

    // 4 - chrome: cheap analytic environment sampled by the reflection vector
    float3 env = lerp(_ChromeGround.rgb, _ChromeSky.rgb, saturate(reflectDir.y * 0.5 + 0.5));
    float sun = pow(saturate(dot(reflectDir, normalize(_ChromeSunDir.xyz))), _ChromeSharp);
    // Damped head-on so the mirror does not flatly wash out the artwork; it
    // opens up as the card turns away, which is where chrome reads best anyway.
    float chromeRamp = 0.3 + 0.7 * pow(saturate(1.0 - saturate(ndv)), 1.5);
    float3 chrome = (env + _ChromeSun.rgb * sun) * chromeRamp;

    // 5 - glossy coat on the edges
    float fresnel = pow(saturate(1.0 - saturate(ndv)), _FresnelPower);

    // mask: where the card is allowed to foil at all
    float mask = 1.0;
#ifdef _MASKTEX
    mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_FrontTex, uv).r;
#endif
    float luma = dot(baseColor, float3(0.299, 0.587, 0.114));
    float lumaMask = saturate((luma + _MaskBias - 0.5) * _MaskContrast + 0.5);
    mask *= lerp(1.0, lumaMask, _MaskFromLuma);

    // Abraded foil is dead foil. This one line is the loudest damage cue the card
    // has: every layer above is driven off the same mask, so a scuffed patch
    // stops diffracting, stops sparkling and stops sweeping all at once.
    mask *= saturate(1.0 - wear.scuff * _ScuffFoilLoss);

    float3 foil = rainbow * _RainbowStrength * (0.35 + sweep);
    foil += _SweepColor.rgb * _SweepStrength * sweep;
    foil += _SparkleColor.rgb * _SparkleStrength * sparkle;
    foil += chrome * _ChromeStrength;
    foil += _FresnelColor.rgb * _FresnelStrength * fresnel;
    foil *= mask * _FoilIntensity;

    // Added after the intensity, on purpose. Abrasion is not foil - a scuffed
    // common card has no foil to lose but still has to show its scratches, and
    // anything above this line is multiplied away to nothing on one.
    if (wear.scuff > 0.0)
    {
        foil += _ScuffHaze * wear.scuff * (0.35 + sweep);
        foil += _ScuffGlint * wear.scuff * sweep * fresnel;
    }

    if (_ColorSteps >= 1.0) foil = floor(foil * _ColorSteps) / _ColorSteps;

    return max(foil, 0.0);
}

#endif // COZY_CARD_HOLO_INPUT_INCLUDED

```

---

## `CardWear.hlsl`

```hlsl
#ifndef COZY_CARD_WEAR_INCLUDED
#define COZY_CARD_WEAR_INCLUDED

// Included from CardHoloInput.hlsl *after* the UnityPerMaterial CBUFFER and the
// texture declarations, not on its own: everything here reads material uniforms
// that have to stay in the one shared constant buffer for the SRP Batcher.

// ---------------------------------------------------------------------------
// The wear map
// ---------------------------------------------------------------------------
// One RGBA texture per card at exactly _CardPixels, carrying all five kinds of
// damage at once. Point filtered and linear (never sRGB) - it is data, not a
// picture, and a gamma curve on it would bend every rate the tools rub at.
//
//   R  scuff        abrasion, scratches, the matte patches that kill the foil
//   G  ink loss     colour coming off, worst on the edges and corners
//   B  height       0.5 is flat, below is a dent, above is a ridge
//   A  missing      material that is not there any more - chips out of the edge
//
// R and G are per face, so a card has to be restored on both sides. B and A are
// not: a dent goes through the paper and a chip is missing from both sides, so
// both always come from the front map. That also keeps DepthOnly honest, which
// only ever sees the front - a chip it could not see would write depth over a
// pixel the forward pass had already clipped away.

struct CardWear
{
    float scuff;
    float inkLoss;
    float height;
    float missing;
};

float CardWearQuantize(float v)
{
    // Same trick _ColorSteps plays on the foil. Damage in visible steps is what
    // makes a rub read as progress: a scratch goes four texels, three, two, gone,
    // instead of fading by an amount nobody can see. floor, not round, so the
    // last step actually clears the texel rather than leaving a ghost of it.
    if (_WearSteps < 1.0) return v;
    return floor(saturate(v) * _WearSteps) / _WearSteps;
}

// Every read below is an explicit LOD 0. The map has no mip chain, so it is the
// only level there is, and asking for it outright keeps the sampling out of the
// compiler's hands - these all sit inside a branch on _WearAmount, which is where
// an implicit gradient is at its least welcome.
float CardWearHeightAt(float2 uv)
{
    return (SAMPLE_TEXTURE2D_LOD(_WearTex, sampler_WearTex, uv, 0).b - 0.5) * 2.0;
}

/// snappedUV is the pixel-snapped UV the artwork is sampled with, so wear texels
/// land on art texels and share its anti-aliasing ramp. rawUV is the unsnapped
/// one, used for the height taps, where the ramp would only smear the gradient.
CardWear SampleCardWear(float2 snappedUV, float facing)
{
    CardWear wear = (CardWear)0;
    if (_WearAmount <= 0.0) return wear;

    float4 front = SAMPLE_TEXTURE2D_LOD(_WearTex, sampler_WearTex, snappedUV, 0);
    float4 back = SAMPLE_TEXTURE2D_LOD(_WearBackTex, sampler_WearTex, snappedUV, 0);
    float2 surface = facing > 0.0 ? front.rg : back.rg;

    wear.scuff = CardWearQuantize(surface.r * _WearAmount);
    wear.inkLoss = CardWearQuantize(surface.g * _WearAmount);
    wear.height = (front.b - 0.5) * 2.0 * _WearAmount;
    wear.missing = front.a * _WearAmount;
    return wear;
}

/// Tangent-space normal of the dents and creases, from the slope of the height
/// channel across neighbouring texels. Deliberately blocky - one normal per card
/// pixel - because a smooth bump map over pixel art reads as a different card.
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
    // The back is the same surface read through the paper and with u mirrored:
    // the mirror flips the u slope, and looking from behind turns every dent into
    // a bump. Both land on the same negation of the tangent-space xy.
    n.xy *= facing;
    return n;
}

// ---------------------------------------------------------------------------
// Bending
// ---------------------------------------------------------------------------
// Low frequency and geometric, so it lives on scalars and in the vertex shader
// rather than in the map: a bow or a folded corner has to change the silhouette,
// and no amount of shading will do that.

float CardCornerBend(float2 c, float2 corner, float amount)
{
    if (amount == 0.0) return 0.0;
    // 0 at that corner, 1 at the far one.
    float2 d = (1.0 - corner * c) * 0.5;
    float w = saturate(1.0 - length(d) / max(_CornerRadius, 1e-3));
    return amount * w * w * (3.0 - 2.0 * w);
}

/// Displacement along the card's own normal, in world units, at a point on it.
float CardBendHeight(float2 uv)
{
    float2 c = uv * 2.0 - 1.0;

    float h = _Bow.x * (1.0 - c.x * c.x) + _Bow.y * (1.0 - c.y * c.y);

    h += CardCornerBend(c, float2(-1.0, -1.0), _CornerBend.x);
    h += CardCornerBend(c, float2( 1.0, -1.0), _CornerBend.y);
    h += CardCornerBend(c, float2(-1.0,  1.0), _CornerBend.z);
    h += CardCornerBend(c, float2( 1.0,  1.0), _CornerBend.w);

    // Creases deep enough to move the paper rather than only shade it. Read with
    // a linear filter, not the point one the fragment stage uses: this is the
    // fold, and a fold that stair-stepped across the mesh grid would show.
    if (_WearAmount > 0.0 && _DentDisplace != 0.0)
    {
        float mapped = SAMPLE_TEXTURE2D_LOD(_WearTex, sampler_LinearClamp, uv, 0).b;
        h += (mapped - 0.5) * 2.0 * _DentDisplace * _WearAmount;
    }
    return h;
}

/// Bends the card and rebuilds the frame that was bent with it. Object space
/// throughout, where the card's tangent frame is the axes themselves: T = +X,
/// B = +Y, N = -Z (see CardDemoBuilder.BuildCardMesh).
void CardApplyBend(inout float3 positionOS, inout float3 normalOS, inout float4 tangentOS, float2 uv)
{
    // Wide enough to stay well clear of the mesh grid, which the bend is smooth
    // across anyway - a tighter step would only sample the map's own noise.
    const float e = 0.02;
    float h0 = CardBendHeight(uv);
    float hu = CardBendHeight(uv + float2(e, 0.0));
    float hv = CardBendHeight(uv + float2(0.0, e));

    // The face points at -Z, so displacing along the normal moves it that way.
    positionOS.z -= h0;

    // dP/du = T*width + N*dh/du and dP/dv = B*height + N*dh/dv, whose cross
    // product comes out as N - T*(dh/du)/width - B*(dh/dv)/height.
    float2 k = (float2(hu, hv) - h0) / (e * max(_CardWorldSize.xy, 1e-4));
    float3 n = normalize(float3(-k, 1.0));
    normalOS = float3(n.x, n.y, -n.z);
    tangentOS = float4(normalize(float3(1.0, 0.0, -k.x)), tangentOS.w);
}

/// Position-only variant, for the passes that write nothing but depth. It has to
/// agree with CardApplyBend to the last bit, or depth and colour disagree about
/// where the card is.
void CardApplyBendPosition(inout float3 positionOS, float2 uv)
{
    positionOS.z -= CardBendHeight(uv);
}

// ---------------------------------------------------------------------------
// Shading the surface damage
// ---------------------------------------------------------------------------
/// Ink coming off: the print goes flat before it goes pale, so desaturate first
/// and only then fade towards the bare card stock underneath.
float3 CardApplyInkLoss(float3 col, float inkLoss)
{
    if (inkLoss <= 0.0) return col;
    float luma = dot(col, float3(0.299, 0.587, 0.114));
    col = lerp(col, luma.xxx, saturate(inkLoss * _InkLossDesat));
    return lerp(col, _StockColor.rgb, inkLoss);
}

#endif // COZY_CARD_WEAR_INCLUDED

```

---

## `CardDebug.hlsl`

```hlsl
#ifndef COZY_CARD_DEBUG_INCLUDED
#define COZY_CARD_DEBUG_INCLUDED

// Included from CardHoloInput.hlsl, after the CBUFFER and after CardWear.hlsl.
//
// Views of the forward pass's OWN intermediates - the raw uv, the snapped uv,
// the wear-perturbed normal, tilt - handed in from Frag rather than worked out
// again here. A debug view that recomputed its subject would be free to drift
// from it, and a teaching view that drifts is worse than none.
//
// The whole thing hangs off one branch on _DebugView, which is a uniform: every
// pixel of the draw takes it the same way, so the hardware skips the block whole
// and a card at 0 pays nothing. That is the same reasoning that keeps the wear
// code out of a shader keyword - see the note in CardHolo.shader.

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

    // 1 - UV. The interpolator, straight out: red is u, green is v. Everything
    //     the fragment stage knows about WHERE it is on the card starts here.
    if (view < CARD_DEBUG_UV + 0.5) return float3(uv, 0.0);

    // 2 - the 73x113 texel grid, with the snapping ramp on top of it in red.
    //     CardPixelUV bends the uv towards texel centres; where the two differ
    //     is exactly the one-screen-pixel seam that keeps the art crisp.
    if (view < CARD_DEBUG_TEXELS + 0.5)
    {
        float2 cell = floor(texels);
        float check = fmod(cell.x + cell.y, 2.0);
        float ramp = saturate(length(sampleUV - uv) * max(_CardPixels.x, 1.0) * 2.0);
        return lerp(float3(0.16, 0.16, 0.2), float3(0.72, 0.72, 0.78), check) + float3(ramp, 0.0, 0.0);
    }

    // 3 - the tangent-space normal. Flat paper is (0,0,1), which encodes to the
    //     familiar blue; a dent or a crease pushes it off and every foil layer
    //     below follows it.
    if (view < CARD_DEBUG_NORMAL + 0.5) return nTS * 0.5 + 0.5;

    // 4 - tilt, the one vector the whole foil is driven by. Grey at the centre
    //     of the card when it faces you, and it moves as you turn it.
    if (view < CARD_DEBUG_TILT + 0.5) return float3(tilt * 0.5 + 0.5, 0.5);

    // 5 - N dot V. White head on, black at grazing. The Fresnel layer is this
    //     number and nothing else; the back face is tinted so the flip shows.
    if (view < CARD_DEBUG_FACING + 0.5)
        return saturate(ndv) * (facing > 0.0 ? float3(1.0, 1.0, 1.0) : float3(1.0, 0.65, 0.6));

    // 6 - the map's surface channels as they arrive: R scuff, G ink, B missing.
    if (view < CARD_DEBUG_WEAR + 0.5) return float3(wear.scuff, wear.inkLoss, wear.missing);

    // 7 - the height channel, signed. Orange is a ridge, blue a dent, dark is flat.
    if (view < CARD_DEBUG_HEIGHT + 0.5)
    {
        float h = wear.height;
        return float3(saturate(h), 0.08 + 0.18 * (1.0 - abs(h)), saturate(-h));
    }

    // 8 - the foil on its own, with the artwork taken away.
    if (view < CARD_DEBUG_FOIL + 0.5) return foil;

    // 9 - texel density: how many card texels one screen pixel covers. This is
    //     the number mip selection is made of, and the reason the artwork is
    //     sampled with the UNTOUCHED derivatives rather than the snapped ones.
    float level = log2(max(density, 1e-4));
    return float3(saturate(level * 0.5), saturate(1.0 - abs(level) * 0.5), saturate(-level * 0.5));
}

#endif // COZY_CARD_DEBUG_INCLUDED

```

---

## `CardHolo.shader`

```hlsl
Shader "Cozy TGC/Card Holo"
{
    Properties
    {
        [Header(Card Faces)][Space(4)]
        [MainTexture] _FrontTex("Front Face", 2D) = "white" {}
        _BackTex("Back Face", 2D) = "white" {}
        [MainColor] _Tint("Tint", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        _CardPixels("Card Size In Pixels", Vector) = (73,113,0,0)
        [Toggle(_PIXELAA)] _PixelAA("Anti Aliased Pixels", Float) = 1

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

        // No keyword gates the wear. _WearAmount at 0 is a uniform branch the GPU
        // skips whole, which costs a pristine card nothing, and a keyword here
        // would be one more thing to keep in step across the two passes.
        [Header(Wear)][Space(4)]
        _WearTex("Wear Front (R scuff, G ink, B height, A missing)", 2D) = "white" {}
        _WearBackTex("Wear Back (R scuff, G ink)", 2D) = "white" {}
        _WearAmount("Amount", Range(0,1)) = 0
        _WearSteps("Quantize Steps (0 = off)", Range(0,16)) = 5
        _StockColor("Card Stock", Color) = (0.84,0.80,0.72,1)
        _InkLossDesat("Ink Loss Desaturate", Range(0,2)) = 1.2
        _ScuffFoilLoss("Scuff Kills Foil", Range(0,1)) = 1
        _ScuffHaze("Scuff Haze", Range(0,2)) = 0.35
        _ScuffGlint("Scuff Glint", Range(0,4)) = 1.2
        _DentDepth("Dent Normal Depth", Range(0,16)) = 8

        // Off on every shipped material. It is a uniform, so the branch at the end
        // of the forward pass is one every pixel of the draw takes the same way and
        // the hardware skips it whole - the same reasoning that keeps the wear code
        // out of a keyword.
        [Header(Debug)][Space(4)]
        _DebugView("View (0 off, see CardDebug.hlsl)", Range(0,9)) = 0

        [Header(Bending)][Space(4)]
        _CardWorldSize("Card Size In Units", Vector) = (0.73,1.13,0,0)
        _DentDisplace("Crease Displacement", Range(0,0.05)) = 0.012
        _Bow("Bow (X, Y)", Vector) = (0,0,0,0)
        _CornerBend("Corner Bend (BL, BR, TL, TR)", Vector) = (0,0,0,0)
        _CornerRadius("Corner Bend Radius", Range(0.05,1.5)) = 0.5
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

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            // 3.5 rather than 3.0: the vertex stage samples the wear map now, and
            // vertex texture fetch is only guaranteed from this tier up.
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ _PIXELAA
            #pragma shader_feature_local_fragment _ _MASKTEX
            #pragma shader_feature_local_fragment _ _HOLOPIXELATE

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

                // Bend before anything is transformed, so the frame that comes out
                // of GetVertexNormalInputs is the bent card's, not the flat one's.
                float3 positionOS = input.positionOS.xyz;
                float3 normalOS = input.normalOS;
                float4 tangentOS = input.tangentOS;
                CardApplyBend(positionOS, normalOS, tangentOS, input.uv);

                VertexPositionInputs pos = GetVertexPositionInputs(positionOS);
                VertexNormalInputs nrm = GetVertexNormalInputs(normalOS, tangentOS);

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

                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 N = normalize(input.normalWS);
                float3 T = normalize(input.tangentWS);
                float3 B = normalize(input.bitangentWS);

                // Which side are we looking at? Geometric, so it is independent of
                // triangle winding - the face whose normal points at the camera wins.
                float facing = dot(N, V) >= 0.0 ? 1.0 : -1.0;
                N *= facing;
                T *= facing;

                float2 uv = input.uv;
                uv.x = facing > 0.0 ? uv.x : 1.0 - uv.x;

                // CardPixelUV is deliberately discontinuous at texel seams, which
                // would confuse hardware mip selection - feed it the untouched
                // UV derivatives so minification still picks a sane mip level.
                float2 sampleUV = CardPixelUV(uv, max(_CardPixels.xy, 1.0));
                float2 ddxUV = ddx(uv);
                float2 ddyUV = ddy(uv);
                half4 front = SAMPLE_TEXTURE2D_GRAD(_FrontTex, sampler_FrontTex, sampleUV, ddxUV, ddyUV);
                half4 back  = SAMPLE_TEXTURE2D_GRAD(_BackTex, sampler_FrontTex, sampleUV, ddxUV, ddyUV);
                half4 base  = facing > 0.0 ? front : back;

                CardWear wear = SampleCardWear(sampleUV, facing);

                // A chip is material that is not there any more, so it leaves the
                // card the same way a transparent texel does.
                clip(base.a - wear.missing - _Cutoff);

                // Dents and creases as a per pixel normal. Everything below reads
                // the card's surface off this, so the whole foil breaks over a
                // dent without a single line of it knowing about wear.
                float3 nTS = CardWearNormal(uv, facing);

                // View direction in tangent space -> parallax / tilt vector, taken
                // against the dented normal rather than the flat one. Falls back to
                // exactly the old maths where nTS is (0,0,1).
                float3 vT = float3(dot(V, T), dot(V, B), dot(V, N));
                float ndv = dot(vT, nTS);
                float2 tilt = (vT.xy - nTS.xy * ndv) / max(ndv, 0.15) * _TiltGain;
                tilt /= (1.0 + 0.35 * length(tilt));

                float3 Nw = normalize(T * nTS.x + B * nTS.y + N * nTS.z);
                float3 reflectDir = reflect(-V, Nw);

                float3 col = base.rgb * _Tint.rgb;
                col = CardApplyInkLoss(col, wear.inkLoss);
                // Faded artwork foils less on its own: CardFoil masks by luminance,
                // and this is already the faded colour going in.
                float3 foil = CardFoil(uv, tilt, ndv, reflectDir, col, wear);

                float3 additive = col + foil;
                float3 screen = 1.0 - (1.0 - saturate(col)) * (1.0 - saturate(foil));
                col = lerp(additive, screen, _FoilBlend);

                // Everything the debug views show is handed in from here rather
                // than recomputed, so a view cannot drift from what the card
                // actually did this pixel.
                if (_DebugView > 0.0)
                    col = CardDebugColor(uv, sampleUV, nTS, tilt, ndv, wear, foil, facing);

                return half4(col, 1.0);
            }
            ENDHLSL
        }

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
            // 3.5 rather than 3.0: the vertex stage samples the wear map now, and
            // vertex texture fetch is only guaranteed from this tier up.
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
                // Has to bend exactly as the forward pass does, or depth and colour
                // disagree about where the card is.
                float3 positionOS = input.positionOS.xyz;
                CardApplyBendPosition(positionOS, input.uv);
                output.positionCS = TransformObjectToHClip(positionOS);
                output.uv = TRANSFORM_TEX(input.uv, _FrontTex);
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 sampleUV = CardPixelUV(input.uv, max(_CardPixels.xy, 1.0));
                half alpha = SAMPLE_TEXTURE2D_GRAD(_FrontTex, sampler_FrontTex, sampleUV,
                                                   ddx(input.uv), ddy(input.uv)).a;
                // Chips have to take the depth with them. Left in, a chipped pixel
                // writes depth over the background the forward pass clipped through
                // to, and the hole fills with whatever is behind the card.
                half missing = 0.0;
                if (_WearAmount > 0.0)
                    missing = SAMPLE_TEXTURE2D_LOD(_WearTex, sampler_WearTex, sampleUV, 0).a * _WearAmount;
                clip(alpha - missing - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}

```

---

## `CardOverlay.shader`

```hlsl
Shader "Cozy TGC/Card Overlay"
{
    // The card's own geometry drawn on top of it: its triangle edges, its
    // vertices, and the tangent frame at a grid of points across it.
    //
    // What makes it worth having rather than misleading is that it has no idea of
    // its own where the card is. It includes CardWear.hlsl and runs the very same
    // CardApplyBend the card's vertex stage runs, so the wireframe of a bowed card
    // IS the bow, and the normals fan out because CardApplyBend's last three lines
    // say they do - not because something here draws them fanning out.
    //
    // Three meshes, one material. What separates them is vertex colour, which the
    // meshes carry and which costs nothing to read.
    Properties
    {
        _Color("Tint", Color) = (1,1,1,1)
        // Length of one tangent frame axis, in world units.
        _AxisLength("Axis Length", Range(0,0.5)) = 0.14

        // Everything below exists because CardWear.hlsl reads it. The bend needs
        // most of them; the rest have to be declared for the include to compile.
        _CardPixels("Card Size In Pixels", Vector) = (73,113,0,0)
        _CardWorldSize("Card Size In Units", Vector) = (0.73,1.13,0,0)
        _WearTex("Wear Front", 2D) = "white" {}
        _WearBackTex("Wear Back", 2D) = "white" {}
        _WearAmount("Wear Amount", Range(0,1)) = 0
        _WearSteps("Wear Quantize Steps", Range(0,16)) = 5
        _StockColor("Card Stock", Color) = (0.84,0.80,0.72,1)
        _InkLossDesat("Ink Loss Desaturate", Range(0,2)) = 1.2
        _DentDepth("Dent Normal Depth", Range(0,16)) = 8
        _DentDisplace("Crease Displacement", Range(0,0.05)) = 0.012
        _Bow("Bow (X, Y)", Vector) = (0,0,0,0)
        _CornerBend("Corner Bend (BL, BR, TL, TR)", Vector) = (0,0,0,0)
        _CornerRadius("Corner Bend Radius", Range(0.05,1.5)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CardOverlay"
            Tags { "LightMode" = "UniversalForward" }

            // Always on top. The overlay is coincident with the card surface, so
            // depth testing it against the card is a coin toss per pixel; and
            // seeing the far side of the grid through the card is what makes it
            // read as one sheet of geometry rather than a decal on the front.
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            // 3.5 for the same reason the card needs it: this vertex stage samples
            // the wear map, to find the creases deep enough to move paper.
            #pragma target 3.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _CardPixels;
                float4 _CardWorldSize;
                float4 _StockColor;
                float4 _Bow;
                float4 _CornerBend;
                float  _AxisLength;
                float  _WearAmount;
                float  _WearSteps;
                float  _InkLossDesat;
                float  _DentDepth;
                float  _DentDisplace;
                float  _CornerRadius;
            CBUFFER_END

            TEXTURE2D(_WearTex);    SAMPLER(sampler_WearTex);
            TEXTURE2D(_WearBackTex);

            // After the CBUFFER and the samplers, never before: everything in it
            // reads uniforms that have to live in that one buffer.
            #include "CardWear.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                /// <summary>x: which axis (0 T, 1 B, 2 N). y: 1 on the tip of an axis line.</summary>
                float2 axis       : TEXCOORD1;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // The card's rest frame, written out rather than read off this mesh:
                // the overlay meshes carry no normals or tangents, and the card is
                // authored flat with T = +X, B = +Y, N = -Z (CardDemoBuilder).
                float3 positionOS = input.positionOS.xyz;
                float3 normalOS = float3(0.0, 0.0, -1.0);
                float4 tangentOS = float4(1.0, 0.0, 0.0, -1.0);

                // The card's own bend, rebuilding the frame as it goes. A vertex dot
                // carries its source vertex's uv on all four corners, so the whole
                // dot lands where that vertex went instead of shearing across it.
                CardApplyBend(positionOS, normalOS, tangentOS, input.uv);

                // Tangent frame gizmo: base and tip arrive at the same position, and
                // only the tip is pushed out - along the frame the bend just rebuilt.
                if (input.axis.y > 0.5)
                {
                    float3 N = normalize(normalOS);
                    float3 T = normalize(tangentOS.xyz);
                    float3 B = normalize(cross(N, T) * tangentOS.w);
                    float3 axis = input.axis.x < 0.5 ? T : (input.axis.x < 1.5 ? B : N);
                    positionOS += axis * _AxisLength;
                }

                output.positionCS = TransformObjectToHClip(positionOS);
                output.color = input.color * _Color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}

```

---

## `CardPackInput.hlsl`

```hlsl
#ifndef COZY_CARD_PACK_INPUT_INCLUDED
#define COZY_CARD_PACK_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Everything the material owns lives in UnityPerMaterial so the SRP Batcher stays happy.
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

    float  _CutHead;
    float  _CutGlow;
    float  _CutWidth;
    float4 _CutColor;
    float  _CutRainbow;
    float  _Flash;

    float  _Flutter;
    float  _FlutterWaves;
    float  _FlutterPhase;
    float  _FlutterCurl;

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

// ---------------------------------------------------------------------------
// Sheet cell
// ---------------------------------------------------------------------------
// The mesh is authored in cell space (0..1 over one pack), so a single mesh and
// material can draw any of the 180 packs on the sheet: _PackRect maps cell UV
// onto the atlas, xy = offset, zw = size.
float2 PackAtlasUV(float2 cellUV)
{
    return _PackRect.xy + cellUV * _PackRect.zw;
}

// The wrapper is only ever magnified - it fills a good part of the frame and never
// turns away the way a card does - so it samples mip 0 outright instead of taking
// the card shader's SAMPLE_TEXTURE2D_GRAD route. Mip selection would be shaky here
// anyway: the mesh is one quad per two pixels, so most 2x2 rasterizer quads straddle
// a triangle edge, and the peel displaces neighbouring columns by different amounts.
// Any pixel that guessed a coarser mip would pull in the sheet's transparent gutters.
half4 SamplePack(float2 atlasUV)
{
    return SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, atlasUV, 0);
}

// Snaps to texel centres with a one screen pixel ramp on the seams. Done in cell
// space against _PackPixels, which lands on atlas texel centres too because every
// cell starts on a whole pixel. Same trick as CardHoloInput.hlsl - see the note
// there on why this beats point filtering.
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

// ---------------------------------------------------------------------------
// The tear
// ---------------------------------------------------------------------------
// How far the seam has been ripped open at this column. _PeelMin.._PeelMax is the
// span of the pack's width the cursor has swept, and the feather keeps the strip
// attached either side of it so it bulges rather than snapping off in one piece.
// _PeelFlat blends the whole strip to fully peeled once the tear completes.
float PackPeelShape(float u)
{
    float f = max(_PeelFeather, 1e-4);
    float s = smoothstep(_PeelMin, _PeelMin + f, u) * (1.0 - smoothstep(_PeelMax - f, _PeelMax, u));
    return saturate(lerp(s, 1.0, saturate(_PeelFlat)));
}

// uv1.y runs 0 at the tear line to 1 at the far edge of the piece, so the flap
// hinges on the seam. The pack faces -Z, so curling towards -Z lifts it into view.
// It is a shared linear ramp over the mesh's height and may sit slightly outside
// 0..1 where a column is cut above or below the nominal line - clamping it would
// break neighbouring columns apart along their shared edge, so leave it alone.
float3 PackPeelPosition(float3 positionOS, float2 uv, float2 uv1, out float shape)
{
    shape = PackPeelShape(uv.x);
    float lift = _PeelLift * shape * uv1.y;
    positionOS.y += lift;
    positionOS.z -= lift * _PeelCurl;
    return positionOS;
}

// A travelling wave along the strip, for the lid once it has come off the pack. It
// is driven off uv.x and the shared hinge ramp rather than anything per column, for
// the same reason the peel is: neighbouring columns have to agree on their shared
// edge, or the wave shears the strip into stripes.
//
// The hinge end is held down and the free edge swings, so the strip flaps the way a
// piece of foil does rather than sliding about as a rigid sheet. It is written for
// the lid but costs nothing on the body, whose _Flutter is left at zero.
float3 PackFlutter(float3 positionOS, float2 uv, float2 uv1)
{
    float wave = sin((uv.x * _FlutterWaves + _FlutterPhase) * 6.28318530718);
    float grip = 0.25 + uv1.y;
    positionOS.z -= wave * _Flutter * grip;
    positionOS.y += wave * _Flutter * _FlutterCurl * grip;
    return positionOS;
}

// Raw torn lip, stepped onto whole texture pixels. A smooth falloff here reads as
// a glow pasted over the pixel art, so the lip is one pixel bright and the shading
// behind it steps down a pixel at a time. tearDist comes off the mesh in pixels.
void PackTornEdge(float tearDist, float torn, out float lip, out float shade)
{
    float band = saturate(1.0 - floor(tearDist) / max(_TearEdgeWidth, 1.0));
    float onSeam = step(0.999, band);
    lip = onSeam * torn;
    shade = (band - onSeam) * torn;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
float3 PackSpectrum(float t)
{
    return saturate(0.5 + 0.5 * cos(6.28318530718 * (t + float3(0.0, 0.33, 0.67))));
}

// ---------------------------------------------------------------------------
// The cut
// ---------------------------------------------------------------------------
// Light spilling out of the seam while it is being cut open. Same pixel discipline
// as PackTornEdge: the band is stepped on whole texture pixels, because a smooth
// falloff over pixel art reads as a glow pasted on top of the wrapper rather than
// as light coming through it.
//
// swept is the peel shape, so the seam only lights where the sweep has actually
// been, and the head of the sweep burns brighter than the tail it leaves behind -
// which is what makes the cut read as travelling rather than as the whole seam
// fading up at once. _Flash then blows the same band open at the end.
float3 PackCutLight(float tearDist, float swept, float u)
{
    float width = max(_CutWidth, 1.0) * (1.0 + _Flash * 5.0);
    float band = saturate(1.0 - floor(tearDist) / width);

    float head = exp(-pow(abs(u - _CutHead) / 0.1, 2.0) * 2.0);
    float amount = swept * _CutGlow * (1.0 + head * 2.0) + _Flash * 3.0;

    // Colour only where the light has fallen off: a cut edge is blown out white
    // where it is strongest and splits into a spectrum on the way out of it.
    float3 fringe = lerp(PackSpectrum(band * 0.8 + 0.1), _CutColor.rgb, saturate(band * 1.4 - 0.2));
    float3 tint = lerp(_CutColor.rgb, fringe, _CutRainbow);
    tint = lerp(tint, float3(1.0, 1.0, 1.0), saturate(band * band + _Flash * 0.8));

    return tint * (band * band * amount);
}

// Foil sheen: a gaussian bar that slides across the wrapper as it tilts, same
// shape as the card's sweep so the pack and the cards inside read as one set.
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

#endif // COZY_CARD_PACK_INPUT_INCLUDED

```

---

## `CardPack.shader`

```hlsl
Shader "Cozy TGC/Card Pack"
{
    Properties
    {
        [Header(Pack)][Space(4)]
        [MainTexture] _MainTex("Pack Sheet", 2D) = "white" {}
        [MainColor] _Tint("Tint", Color) = (1,1,1,1)
        _PackRect("Sheet Cell (xy offset, zw size)", Vector) = (0,0,1,1)
        _PackPixels("Pack Size In Pixels", Vector) = (84,154,0,0)
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        [Toggle(_PIXELAA)] _PixelAA("Anti Aliased Pixels", Float) = 1
        _BackShade("Back Face Shade", Range(0,1)) = 0.45

        [Header(Tear)][Space(4)]
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

        [Header(Cut)][Space(4)]
        _CutGlow("Seam Light", Range(0,4)) = 0
        _CutWidth("Seam Light Width (pixels)", Range(1,24)) = 2
        _CutColor("Seam Light Color", Color) = (0.55,0.95,1,1)
        _CutRainbow("Seam Rainbow", Range(0,1)) = 0.7
        _CutHead("Sweep Head (U)", Range(-0.5,1.5)) = 0.5
        _Flash("Flash", Range(0,1)) = 0

        [Header(Flutter)][Space(4)]
        _Flutter("Flutter Amplitude", Range(0,0.4)) = 0
        _FlutterWaves("Flutter Waves", Range(0.25,6)) = 1.6
        _FlutterPhase("Flutter Phase", Float) = 0
        _FlutterCurl("Flutter Curl", Range(0,2)) = 0.55

        [Header(Foil Sheen)][Space(4)]
        _ShineStrength("Strength", Range(0,3)) = 0.4
        _ShineColor("Color", Color) = (1,1,1,1)
        _ShineWidth("Width", Range(0.02,2)) = 0.3
        _ShineAngle("Angle", Range(0,6.2832)) = 1.15
        _ShineTravel("Tilt Travel", Range(0,3)) = 1.1
        _ShineOffset("Rest Offset", Range(-1,1)) = -0.35
        _ShineTint("Rainbow Tint", Range(0,1)) = 0.35
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
            Name "PackForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ _PIXELAA

            #include "CardPackInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                // x = distance from the tear line in object units, y = 0 on the
                // seam to 1 at the far edge of the piece. Built by CardPackBuilder.
                float2 uv1        : TEXCOORD1;
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
                float2 tear        : TEXCOORD5; // x = distance from seam, y = peel shape
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float shape;
                float3 positionOS = PackPeelPosition(input.positionOS.xyz, input.uv, input.uv1, shape);
                positionOS = PackFlutter(positionOS, input.uv, input.uv1);

                VertexPositionInputs pos = GetVertexPositionInputs(positionOS);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = nrm.normalWS;
                output.tangentWS = nrm.tangentWS;
                output.bitangentWS = nrm.bitangentWS;
                output.uv = input.uv;
                output.tear = float2(input.uv1.x, shape);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 N = normalize(input.normalWS);
                float3 T = normalize(input.tangentWS);
                float3 B = normalize(input.bitangentWS);

                // Geometric facing, so a flap that curls past edge-on still lights
                // sanely regardless of triangle winding.
                float facing = dot(N, V) >= 0.0 ? 1.0 : -1.0;
                N *= facing;
                T *= facing;

                half4 base = SamplePack(PackAtlasUV(PackPixelUV(input.uv, max(_PackPixels.xy, 1.0))));
                clip(base.a - _Cutoff);

                float3 col = base.rgb * _Tint.rgb;

                // View direction in tangent space -> tilt vector driving the sheen.
                float3 vT = float3(dot(V, T), dot(V, B), dot(V, N));
                float2 tilt = vT.xy / max(vT.z, 0.15);
                tilt /= (1.0 + 0.35 * length(tilt));
                col += PackShine(input.uv, tilt, col);

                // Raw torn edge, faded in by how far the tear has actually reached.
                float lip, shade;
                PackTornEdge(input.tear.x, saturate(input.tear.y), lip, shade);
                col *= 1.0 - _TearShade * shade;
                col += _TearEdgeColor.rgb * (_TearEdge * lip);

                // Light through the seam. Added before the back shade so the far side
                // of a flipping lid keeps its rim: the cut goes through the foil, so
                // it is just as bright looked at from behind.
                float3 cut = PackCutLight(input.tear.x, saturate(input.tear.y), input.uv.x);

                // The underside of a peeled flap is the inside of the wrapper.
                col *= facing > 0.0 ? 1.0 : (1.0 - _BackShade);
                col += cut;

                return half4(col, 1.0);
            }
            ENDHLSL
        }

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
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ _PIXELAA

            #include "CardPackInput.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
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

                // Same displacement as the forward pass, or the depth silhouette
                // drifts away from the peeled flap.
                float shape;
                float3 positionOS = PackPeelPosition(input.positionOS.xyz, input.uv, input.uv1, shape);
                positionOS = PackFlutter(positionOS, input.uv, input.uv1);

                output.positionCS = TransformObjectToHClip(positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half alpha = SamplePack(PackAtlasUV(PackPixelUV(input.uv, max(_PackPixels.xy, 1.0)))).a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}

```

---

## `PackFlashInput.hlsl`

```hlsl
#ifndef COZY_PACK_FLASH_INPUT_INCLUDED
#define COZY_PACK_FLASH_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Two jobs, one shader, because they are the same moment seen from either side of
// the wrapper: the bar is the light coming out of the cut, the dim is the room
// going quiet behind it. Blend state is a material property so both can be plain
// materials rather than two shaders that would then drift apart.
CBUFFER_START(UnityPerMaterial)
    float  _Mode;
    float  _SrcBlend;
    float  _DstBlend;
    float  _ZTest;

    float4 _Span;
    float  _Head;
    float  _HeadWidth;
    float  _Glow;
    float  _Flash;
    float  _FlashSpill;
    float  _Feather;

    float4 _CoreColor;
    float4 _EdgeColor;
    float  _Thickness;
    float  _Rainbow;

    float4 _DimColor;
    float  _Dim;
CBUFFER_END

// Same cosine palette the pack's foil sheen uses, so the light out of the seam and
// the sheen on the wrapper are the same rainbow.
float3 PackFlashSpectrum(float t)
{
    return saturate(0.5 + 0.5 * cos(6.28318530718 * (t + float3(0.0, 0.33, 0.67))));
}

// The bar. uv.x is pack space - 0 and 1 are the wrapper's own edges and the quad
// carries UVs past both, which is how the flash spills wider than the pack without
// anything having to resize. uv.y is signed distance from the cut, 0 on the line.
//
// _Span is the stretch of seam the sweep has opened, so the bar is only as long as
// the cut is; _FlashSpill then runs it out past both ends as the flash goes off.
float3 PackFlashBar(float2 uv)
{
    float f = max(_Feather, 1e-4);
    float spill = _Flash * _FlashSpill;
    float lo = _Span.x - spill;
    float hi = _Span.y + spill;
    float span = smoothstep(lo - f, lo + f, uv.x) * (1.0 - smoothstep(hi - f, hi + f, uv.x));

    float d = abs(uv.y);
    float thick = max(_Thickness, 1e-4) * (1.0 + _Flash * 7.0);
    float bar = exp(-pow(d / thick, 2.0) * 2.0);
    float core = pow(bar, 8.0);

    // The head of the sweep is where the wrapper is being cut right now, so it is
    // the brightest point on the line and it is what the eye follows.
    // abs before the pow: a negative base is undefined, same reason PackShine takes
    // it. The head is symmetric anyway, so the abs costs nothing.
    float head = exp(-pow(abs(uv.x - _Head) / max(_HeadWidth, 1e-3), 2.0) * 2.0);

    float3 fringe = lerp(float3(1.0, 1.0, 1.0),
                         PackFlashSpectrum(saturate(d / thick) * 0.65 + 0.06),
                         _Rainbow);

    float3 col = _EdgeColor.rgb * fringe * bar + _CoreColor.rgb * core * 1.8;
    return col * span * (_Glow * (1.0 + head * 2.5) + _Flash * 6.0);
}

#endif // COZY_PACK_FLASH_INPUT_INCLUDED

```

---

## `PackFlash.shader`

```hlsl
Shader "Cozy TGC/Pack Flash"
{
    Properties
    {
        [Header(Mode)][Space(4)]
        [Enum(Bar,0,Dim,1)] _Mode("Mode", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Z Test", Float) = 8

        [Header(Bar)][Space(4)]
        _Span("Lit Span (x=start U, y=end U)", Vector) = (0.5,0.5,0,0)
        _Head("Sweep Head (U)", Range(-0.5,1.5)) = 0.5
        _HeadWidth("Sweep Head Width", Range(0.01,0.5)) = 0.07
        _Glow("Glow", Range(0,4)) = 0
        _Flash("Flash", Range(0,1)) = 0
        _FlashSpill("Flash Spill (U)", Range(0,2)) = 0.85
        _Feather("Span Feather", Range(0.001,0.3)) = 0.02
        _CoreColor("Core Color", Color) = (1,1,1,1)
        _EdgeColor("Edge Color", Color) = (0.5,0.92,1,1)
        _Thickness("Thickness", Range(0.005,1)) = 0.055
        _Rainbow("Rainbow", Range(0,1)) = 0.8

        [Header(Dim)][Space(4)]
        _DimColor("Dim Color", Color) = (0.38,0.4,0.5,1)
        _Dim("Dim", Range(0,1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "PackFlash"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "PackFlashInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Uniform branch: a material is one mode for its whole life, so this
                // costs nothing and beats keeping two near identical shaders in step.
                if (_Mode < 0.5)
                    return half4(PackFlashBar(input.uv), 0.0);

                // Multiplied over what is behind, so one number darkens the whole
                // room without knowing anything about what is standing in it.
                return half4(lerp(float3(1.0, 1.0, 1.0), _DimColor.rgb, saturate(_Dim)), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}

```

---

## `CardSlot.shader`

```hlsl
Shader "Cozy TGC/Card Slot"
{
    Properties
    {
        _Color("Line Color", Color) = (0.62,0.58,0.8,1)
        _FillColor("Fill Color", Color) = (0.62,0.58,0.8,1)
        _HighlightColor("Highlight Color", Color) = (1,0.98,0.92,1)
        _Highlight("Highlight", Range(0,1)) = 0
        _SlotPixels("Slot Size In Pixels", Vector) = (79,122,0,0)
        _Thickness("Line Thickness (pixels)", Range(1,8)) = 2
        _CornerLength("Corner Length (pixels)", Range(2,60)) = 16
        _EdgeAlpha("Edge Alpha", Range(0,1)) = 0.55
        _FillAlpha("Fill Alpha", Range(0,1)) = 0.06
        _FillAlphaHi("Fill Alpha Highlighted", Range(0,1)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "SlotForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _FillColor;
                float4 _HighlightColor;
                float4 _SlotPixels;
                float  _Highlight;
                float  _Thickness;
                float  _CornerLength;
                float  _EdgeAlpha;
                float  _FillAlpha;
                float  _FillAlphaHi;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Everything is measured in whole slot pixels rather than UV, so the
                // outline stays on the same grid as the pixel art sitting inside it
                // instead of going soft when the slot is scaled.
                float2 size = max(_SlotPixels.xy, 2.0);
                float2 p = floor(saturate(input.uv) * size);
                float2 edge = min(p, size - 1.0 - p);

                // "line" is an HLSL keyword, hence the name.
                float border = step(edge.x, _Thickness - 1.0) + step(edge.y, _Thickness - 1.0);
                float ring = saturate(border);
                // Corner brackets: on the outline and close to a corner on both axes.
                float bracket = ring * step(edge.x, _CornerLength) * step(edge.y, _CornerLength);

                // At rest only the brackets show; the full outline fades in when the
                // slot is the one a dragged card would land in. Crank _CornerLength
                // past half the size and the brackets meet into a plain rectangle,
                // which is all an album page needs.
                float lineA = max(bracket, ring * _Highlight) * _EdgeAlpha;
                float fillA = lerp(_FillAlpha, _FillAlphaHi, _Highlight);
                float3 lineC = lerp(_Color.rgb, _HighlightColor.rgb, _Highlight);

                // Line over fill, composited by hand so the two can be different
                // colours: a slot is a faint tint under its own outline, a page is a
                // dark panel under a light one.
                float alpha = lineA + fillA * (1.0 - lineA);
                float3 col = alpha > 1e-4
                    ? (lineC * lineA + _FillColor.rgb * fillA * (1.0 - lineA)) / alpha
                    : lineC;
                return half4(col, alpha * _Color.a);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}

```

---

## `SpriteSheetInput.hlsl`

```hlsl
#ifndef COZY_SPRITE_SHEET_INPUT_INCLUDED
#define COZY_SPRITE_SHEET_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Everything the material owns lives in UnityPerMaterial so the SRP Batcher stays happy.
CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    float4 _Rect;
    float4 _CellPixels;
    float4 _Color;
    float4 _HighlightColor;
    float  _Highlight;
    float  _Cutoff;
CBUFFER_END

TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);

// ---------------------------------------------------------------------------
// Sheet cell
// ---------------------------------------------------------------------------
// The quad is authored in cell space (0..1 over one cell), so one mesh and one
// material can draw any cell of the sheet: _Rect maps cell UV onto the atlas,
// xy = offset, zw = size. Same arrangement CardPackInput.hlsl uses for the 180
// wrappers, and it is what lets a MaterialPropertyBlock step an animation.
//
// Snapped to texel centres with a one screen pixel ramp on the seams, the filter
// CardHoloInput.hlsl explains - unlike the card and pack shaders there is no
// keyword for it, because nothing but pixel art is ever drawn through here.
//
// Clamped to the outer texel centres rather than run to the edge of the cell,
// which point filtering would not need but an atlas does: the icon sheet draws
// its diamonds right up against the top and bottom of their cells, so a sample
// on the boundary would blend in the tip of whatever sits in the next row.
float2 SheetAtlasUV(float2 cellUV)
{
    float2 cellPixels = max(_CellPixels.xy, 1.0);
    float2 p = saturate(cellUV) * cellPixels;
    float2 seam = floor(p + 0.5);
    float2 dudv = clamp(fwidth(p), 1e-5, 1.0);
    p = clamp(seam + clamp((p - seam) / dudv, -0.5, 0.5), 0.5, cellPixels - 0.5);
    return _Rect.xy + (p / cellPixels) * _Rect.zw;
}

// Mip 0 outright, like the pack shader: every one of these quads is magnified,
// and a coarser mip would reach past the cell into whatever the sheet draws
// next door.
half4 SampleSheet(float2 atlasUV)
{
    return SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, atlasUV, 0);
}

#endif // COZY_SPRITE_SHEET_INPUT_INCLUDED

```

---

## `SpriteSheet.shader`

```hlsl
Shader "Cozy TGC/Sprite Sheet"
{
    Properties
    {
        [MainTexture] _MainTex("Sheet", 2D) = "white" {}
        _Rect("Cell UV Rect", Vector) = (0,0,1,1)
        _CellPixels("Cell Size In Pixels", Vector) = (32,32,0,0)
        [MainColor] _Color("Tint", Color) = (1,1,1,1)
        _HighlightColor("Highlight Color", Color) = (1,0.94,0.76,1)
        _Highlight("Highlight", Range(0,1)) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
    }

    // One cell of a pixel art sheet on a quad: the album books and the icons on
    // the shelf. The cell is picked by UV rect rather than by slicing the sheet
    // into sprites, which is what lets one material draw every frame of a book
    // animation and a MaterialPropertyBlock step through them.
    //
    // See SpriteSheetInput.hlsl for the cell maths. There are no keyword backed
    // features here, so unlike CardHolo and CardPack the two passes have nothing
    // to keep in step beyond the alpha clip.
    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "SheetForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "SpriteSheetInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 col = SampleSheet(SheetAtlasUV(input.uv));
                clip(col.a - _Cutoff);

                col.rgb *= _Color.rgb;
                // Tints and brightens rather than washing to a flat colour, so a
                // highlighted icon still reads as the book it is.
                col.rgb = lerp(col.rgb, saturate(col.rgb * _HighlightColor.rgb * 1.7), saturate(_Highlight));
                return half4(col.rgb, 1.0);
            }
            ENDHLSL
        }

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
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "SpriteSheetInput.hlsl"

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
                output.uv = input.uv;
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Same clip as the forward pass, or the transparent margin around a
                // book would write depth and punch a hole in whatever is behind it.
                clip(SampleSheet(SheetAtlasUV(input.uv)).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}

```

