Shader "Cozy TGC/Pack Flash"
{
    Properties
    {
        [Header(Mode)][Space(4)]
        [Enum(Bar,0,Dim,1)] _Mode("Mode", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Z Test", Float) = 8

        [Header(Bar)][Space(4)]
        _Span("Lit Span (x=start U, y=end U)", Vector) = (0.5,0.5,0,0)
        _Head("Sweep Head (U)", Range(-0.5,1.5)) = 0.5
        _HeadWidth("Sweep Head Width", Range(0.01,0.5)) = 0.07
        _Glow("Glow", Range(0,4)) = 0
        _Flash("Flash", Range(0,1)) = 0
        _FlashSpill("Flash Spill (U)", Range(0,2)) = 0.85
        _Feather("Span Feather", Range(0.001,0.3)) = 0.02
        _CoreColor("Core Color", Color) = (1,1,1,1)
        _EdgeColor("Edge Color", Color) = (0.5,0.92,1,1)
        _Thickness("Thickness", Range(0.005,1)) = 0.055
        _Rainbow("Rainbow", Range(0,1)) = 0.8

        [Header(Dim)][Space(4)]
        _DimColor("Dim Color", Color) = (0.38,0.4,0.5,1)
        _Dim("Dim", Range(0,1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "PackFlash"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "PackFlashInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Uniform branch: a material is one mode for its whole life, so this
                // costs nothing and beats keeping two near identical shaders in step.
                if (_Mode < 0.5)
                    return half4(PackFlashBar(input.uv), 0.0);

                // Multiplied over what is behind, so one number darkens the whole
                // room without knowing anything about what is standing in it.
                return half4(lerp(float3(1.0, 1.0, 1.0), _DimColor.rgb, saturate(_Dim)), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
