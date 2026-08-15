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
