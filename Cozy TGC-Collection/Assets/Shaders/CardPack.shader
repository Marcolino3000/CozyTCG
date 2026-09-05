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
