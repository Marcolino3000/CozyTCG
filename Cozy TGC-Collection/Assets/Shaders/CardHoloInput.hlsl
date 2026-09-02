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
