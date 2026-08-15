Shader "Cozy TGC/Card Slot"
{
    Properties
    {
        _Color("Line Color", Color) = (0.62,0.58,0.8,1)
        _FillColor("Fill Color", Color) = (0.62,0.58,0.8,1)
        _HighlightColor("Highlight Color", Color) = (1,0.98,0.92,1)
        _Highlight("Highlight", Range(0,1)) = 0
        _SlotPixels("Slot Size In Pixels", Vector) = (79,122,0,0)
        _Thickness("Line Thickness (pixels)", Range(1,8)) = 2
        _CornerLength("Corner Length (pixels)", Range(2,60)) = 16
        _EdgeAlpha("Edge Alpha", Range(0,1)) = 0.55
        _FillAlpha("Fill Alpha", Range(0,1)) = 0.06
        _FillAlphaHi("Fill Alpha Highlighted", Range(0,1)) = 0.2
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
            Name "SlotForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _FillColor;
                float4 _HighlightColor;
                float4 _SlotPixels;
                float  _Highlight;
                float  _Thickness;
                float  _CornerLength;
                float  _EdgeAlpha;
                float  _FillAlpha;
                float  _FillAlphaHi;
            CBUFFER_END

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

                // Everything is measured in whole slot pixels rather than UV, so the
                // outline stays on the same grid as the pixel art sitting inside it
                // instead of going soft when the slot is scaled.
                float2 size = max(_SlotPixels.xy, 2.0);
                float2 p = floor(saturate(input.uv) * size);
                float2 edge = min(p, size - 1.0 - p);

                // "line" is an HLSL keyword, hence the name.
                float border = step(edge.x, _Thickness - 1.0) + step(edge.y, _Thickness - 1.0);
                float ring = saturate(border);
                // Corner brackets: on the outline and close to a corner on both axes.
                float bracket = ring * step(edge.x, _CornerLength) * step(edge.y, _CornerLength);

                // At rest only the brackets show; the full outline fades in when the
                // slot is the one a dragged card would land in. Crank _CornerLength
                // past half the size and the brackets meet into a plain rectangle,
                // which is all an album page needs.
                float lineA = max(bracket, ring * _Highlight) * _EdgeAlpha;
                float fillA = lerp(_FillAlpha, _FillAlphaHi, _Highlight);
                float3 lineC = lerp(_Color.rgb, _HighlightColor.rgb, _Highlight);

                // Line over fill, composited by hand so the two can be different
                // colours: a slot is a faint tint under its own outline, a page is a
                // dark panel under a light one.
                float alpha = lineA + fillA * (1.0 - lineA);
                float3 col = alpha > 1e-4
                    ? (lineC * lineA + _FillColor.rgb * fillA * (1.0 - lineA)) / alpha
                    : lineC;
                return half4(col, alpha * _Color.a);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
