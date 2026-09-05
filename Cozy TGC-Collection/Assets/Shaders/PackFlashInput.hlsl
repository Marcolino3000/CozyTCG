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
