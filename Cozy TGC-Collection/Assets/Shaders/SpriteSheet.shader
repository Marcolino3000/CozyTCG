Shader "Cozy TGC/Sprite Sheet"
{
    Properties
    {
        [MainTexture] _MainTex("Sheet", 2D) = "white" {}
        _Rect("Cell UV Rect", Vector) = (0,0,1,1)
        _CellPixels("Cell Size In Pixels", Vector) = (32,32,0,0)
        [MainColor] _Color("Tint", Color) = (1,1,1,1)
        _HighlightColor("Highlight Color", Color) = (1,0.94,0.76,1)
        _Highlight("Highlight", Range(0,1)) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
    }

    // One cell of a pixel art sheet on a quad: the album books and the icons on
    // the shelf. The cell is picked by UV rect rather than by slicing the sheet
    // into sprites, which is what lets one material draw every frame of a book
    // animation and a MaterialPropertyBlock step through them.
    //
    // See SpriteSheetInput.hlsl for the cell maths. There are no keyword backed
    // features here, so unlike CardHolo and CardPack the two passes have nothing
    // to keep in step beyond the alpha clip.
    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "SheetForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "SpriteSheetInput.hlsl"

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

                half4 col = SampleSheet(SheetAtlasUV(input.uv));
                clip(col.a - _Cutoff);

                col.rgb *= _Color.rgb;
                // Tints and brightens rather than washing to a flat colour, so a
                // highlighted icon still reads as the book it is.
                col.rgb = lerp(col.rgb, saturate(col.rgb * _HighlightColor.rgb * 1.7), saturate(_Highlight));
                return half4(col.rgb, 1.0);
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

            #include "SpriteSheetInput.hlsl"

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
                output.uv = input.uv;
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Same clip as the forward pass, or the transparent margin around a
                // book would write depth and punch a hole in whatever is behind it.
                clip(SampleSheet(SheetAtlasUV(input.uv)).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
