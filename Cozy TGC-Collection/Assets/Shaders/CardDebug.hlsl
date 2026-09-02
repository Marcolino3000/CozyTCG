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
