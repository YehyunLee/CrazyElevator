Shader "Crazy Elevator/Storybook Surface"
{
    Properties
    {
        [MainColor] _BaseColor ("Base color", Color) = (0.3,0.8,0.65,1)
        _StripeColor ("Stripe color", Color) = (0.55,0.92,0.72,1)
        _StripeScale ("Stripe frequency (0 = solid)", Float) = 0
        _PatternColor ("Paper pattern color", Color) = (0.04,0.08,0.16,1)
        _PatternScale ("Paper pattern scale", Float) = 3.5
        _PatternStrength ("Paper pattern strength", Range(0,0.18)) = 0.045
        _PatternMode ("Pattern mode (0 lines, 1 dots)", Range(0,1)) = 0
        _ToonSteps ("Lighting steps", Range(2,5)) = 3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionOS : TEXCOORD1; float fog : TEXCOORD2; };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _StripeColor;
                half4 _PatternColor;
                float _StripeScale;
                float _PatternScale;
                float _PatternStrength;
                float _PatternMode;
                float _ToonSteps;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionOS = input.positionOS.xyz;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float wave = sin((input.positionOS.y + input.positionOS.x * .17) * _StripeScale);
                float stripe = _StripeScale > 0 ? smoothstep(-.08, .08, wave) : 0;
                half3 color = lerp(_BaseColor.rgb, _StripeColor.rgb, stripe);

                float3 normal = abs(normalize(input.normalWS));
                float2 paperUV = normal.y > normal.x && normal.y > normal.z ? input.positionOS.xz
                    : normal.x > normal.z ? input.positionOS.zy : input.positionOS.xy;
                float diagonal = abs(frac((paperUV.x + paperUV.y) * _PatternScale) - .5);
                float lines = 1 - smoothstep(.035, .075, diagonal);
                float2 cell = frac(paperUV * _PatternScale) - .5;
                float dots = 1 - smoothstep(.10, .20, length(cell));
                float pattern = lerp(lines, dots, step(.5, _PatternMode));
                color = lerp(color, _PatternColor.rgb, pattern * _PatternStrength);

                float light = saturate(dot(normalize(input.normalWS), normalize(float3(-.6, 1, -.7))) * .5 + .5);
                float steps = max(2, round(_ToonSteps));
                light = floor(light * steps) / max(1, steps - 1);
                // Stepped cool shadows and warm highlights keep the geometry
                // dimensional while reading like layered cut paper.
                color *= lerp(half3(.76, .72, .84), half3(1.03, 1.01, .96), saturate(light));
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }
    }
}
