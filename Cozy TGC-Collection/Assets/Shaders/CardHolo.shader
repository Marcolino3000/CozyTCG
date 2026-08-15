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
            #pragma target 3.0
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

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);

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

                clip(base.a - _Cutoff);

                // View direction in tangent space -> parallax / tilt vector.
                float3 vT = float3(dot(V, T), dot(V, B), dot(V, N));
                float2 tilt = vT.xy / max(vT.z, 0.15) * _TiltGain;
                tilt /= (1.0 + 0.35 * length(tilt));

                float3 reflectDir = reflect(-V, N);
                float3 col = base.rgb * _Tint.rgb;
                float3 foil = CardFoil(uv, tilt, vT.z, reflectDir, col);

                float3 additive = col + foil;
                float3 screen = 1.0 - (1.0 - saturate(col)) * (1.0 - saturate(foil));
                col = lerp(additive, screen, _FoilBlend);

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
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _FrontTex);
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 sampleUV = CardPixelUV(input.uv, max(_CardPixels.xy, 1.0));
                half alpha = SAMPLE_TEXTURE2D_GRAD(_FrontTex, sampler_FrontTex, sampleUV,
                                                   ddx(input.uv), ddy(input.uv)).a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
