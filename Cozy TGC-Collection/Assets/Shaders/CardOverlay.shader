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
