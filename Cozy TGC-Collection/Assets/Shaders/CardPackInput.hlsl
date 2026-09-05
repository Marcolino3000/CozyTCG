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
