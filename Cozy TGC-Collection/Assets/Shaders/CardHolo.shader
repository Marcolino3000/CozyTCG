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
