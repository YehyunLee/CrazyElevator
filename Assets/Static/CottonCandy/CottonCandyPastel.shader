Shader "Crazy Elevator/Cotton Candy Pastel"
{
    Properties
    {
        [MainColor] _BaseColor ("Base color", Color) = (0.3,0.8,0.65,1)
        _StripeColor ("Stripe color", Color) = (0.55,0.92,0.72,1)
        _StripeScale ("Stripe frequency (0 = solid)", Float) = 0
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
                float _StripeScale;
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
                float light = saturate(dot(normalize(input.normalWS), normalize(float3(-.6, 1, -.7))));
                // Cooler shadow sides and warm highlights give the candy shapes readable volume.
                color *= lerp(half3(.69, .64, .80), half3(1.0, .99, .94), light);
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }
    }
}
