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
