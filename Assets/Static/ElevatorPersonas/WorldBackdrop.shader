Shader "CrazyElevator/Interior World Backdrop"
{
    Properties
    {
        _BaseMap ("World reference", 2D) = "white" {}
        _Region ("Illustration UV rectangle", Vector) = (1,1,0,0)
        _Tint ("Tint", Color) = (1,1,1,1)
        _Grade ("Contrast", Range(0.5,1.5)) = 1.08
        _Posterize ("Color steps", Range(0,16)) = 9
        _PatternColor ("Paper pattern color", Color) = (0.04,0.08,0.16,1)
        _PatternStrength ("Paper pattern strength", Range(0,0.2)) = 0.045
        _PatternScale ("Paper pattern scale", Float) = 32
        _PatternMode ("Pattern mode (0 lines, 1 dots)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _Region;
                half4 _Tint;
                half4 _PatternColor;
                float _Grade;
                float _Posterize;
                float _PatternStrength;
                float _PatternScale;
                float _PatternMode;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 paperUV : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // The cabin camera sees the reverse side of Unity's built-in quad.
                output.uv = float2(1 - input.uv.x, input.uv.y) * _Region.xy + _Region.zw;
                output.paperUV = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 color = saturate(source.rgb * _Tint.rgb);
                color = saturate((color - .5h) * _Grade + .5h);
                if (_Posterize > 1)
                    color = floor(color * _Posterize + .5h) / _Posterize;

                // Wandersong-inspired printed-paper texture: either slim
                // diagonal hatching or a compact halftone dot field.
                float diagonal = abs(frac((input.paperUV.x + input.paperUV.y) * _PatternScale) - .5);
                float lines = 1 - smoothstep(.035, .075, diagonal);
                float2 cell = frac(input.paperUV * _PatternScale) - .5;
                float dots = 1 - smoothstep(.10, .18, length(cell));
                float pattern = lerp(lines, dots, step(.5, _PatternMode));
                color = lerp(color, _PatternColor.rgb, pattern * _PatternStrength);
                return half4(color, source.a);
            }
            ENDHLSL
        }
    }
}
