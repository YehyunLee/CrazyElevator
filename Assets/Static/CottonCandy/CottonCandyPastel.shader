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
        [Enum(Lines,0,Dots,1,Tiles,2,Diamonds,3,Planks,4,Panels,5)]
        _PatternMode ("Surface pattern", Float) = 0
        [ToggleUI] _KeepPatternScale ("Pattern size follows mesh scale", Float) = 0
        _ToonSteps ("Lighting steps", Range(2,5)) = 3
        _SoftLighting ("Soften lighting steps", Range(0,1)) = 0
        // Opaque by default; door material instances enable alpha blending.
        [HideInInspector] _SrcBlend ("Source blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination blend", Float) = 0
        [HideInInspector] _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionOS : TEXCOORD1; float fog : TEXCOORD2; float3 normalOS : TEXCOORD3; float3 surfacePosition : TEXCOORD4; };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _StripeColor;
                half4 _PatternColor;
                float _StripeScale;
                float _PatternScale;
                float _PatternStrength;
                float _PatternMode;
                float _KeepPatternScale;
                float _ToonSteps;
                float _SoftLighting;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionOS = input.positionOS.xyz;
                o.normalOS = input.normalOS;
                o.surfacePosition = input.positionOS.xyz;
                if (_KeepPatternScale > .5)
                {
                    // Measure in scaled object space so the pattern follows the car.
                    float3x3 basis = (float3x3)GetObjectToWorldMatrix();
                    o.surfacePosition *= float3(length(mul(basis, float3(1,0,0))),
                        length(mul(basis, float3(0,1,0))), length(mul(basis, float3(0,0,1))));
                }
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float PatternLine(float coordinate, float halfWidth)
            {
                float lineDistance = abs(frac(coordinate + .5) - .5);
                float edge = min(.2, max(.006, fwidth(coordinate)));
                return 1 - smoothstep(halfWidth, halfWidth + edge, lineDistance);
            }

            float SurfacePattern(float2 uv)
            {
                if (_PatternMode >= 4.5)
                {
                    float2 panel = uv * float2(.65, 1);
                    float2 cell = frac(panel) - .5;
                    float seam = max(PatternLine(panel.x, .023), PatternLine(panel.y, .023));
                    float light = (1 - smoothstep(.13, .16, abs(cell.x)))
                        * (1 - smoothstep(.025, .055, abs(cell.y + .33)));
                    return max(seam * .72, light);
                }
                if (_PatternMode >= 3.5)
                {
                    float across = uv.x * 1.4;
                    float along = uv.y * .4 + fmod(floor(across), 2) * .5;
                    return max(PatternLine(across, .025), PatternLine(along, .012) * .65);
                }
                if (_PatternMode >= 2.5)
                {
                    float2 cell = abs(frac(uv) - .5);
                    return 1 - smoothstep(.19, .23 + min(.2, fwidth(uv.x) + fwidth(uv.y)), cell.x + cell.y);
                }
                if (_PatternMode >= 1.5)
                    return max(PatternLine(uv.x, .024), PatternLine(uv.y, .024));
                if (_PatternMode >= .5)
                    return 1 - smoothstep(.10, .20, length(frac(uv) - .5));
                return 1 - smoothstep(.035, .075, abs(frac(uv.x + uv.y) - .5));
            }

            half4 frag(Varyings input) : SV_Target
            {
                float wave = sin((input.positionOS.y + input.positionOS.x * .17) * _StripeScale);
                float stripe = _StripeScale > 0 ? smoothstep(-.08, .08, wave) : 0;
                half3 color = lerp(_BaseColor.rgb, _StripeColor.rgb, stripe);

                float3 normal = abs(normalize(_KeepPatternScale > .5 ? input.normalOS : input.normalWS));
                float3 surface = input.surfacePosition;
                float2 paperUV = normal.y > normal.x && normal.y > normal.z ? surface.xz
                    : normal.x > normal.z ? surface.zy : surface.xy;
                float pattern = SurfacePattern(paperUV * _PatternScale);
                color = lerp(color, _PatternColor.rgb, pattern * _PatternStrength);

                float light = saturate(dot(normalize(input.normalWS), normalize(float3(-.6, 1, -.7))) * .5 + .5);
                float steps = max(2, round(_ToonSteps));
                float steppedLight = floor(light * steps) / max(1, steps - 1);
                light = lerp(steppedLight, light, _SoftLighting);
                // Stepped cool shadows and warm highlights keep the geometry
                // dimensional while reading like layered cut paper.
                color *= lerp(half3(.76, .72, .84), half3(1.03, 1.01, .96), saturate(light));
                return half4(MixFog(color, input.fog), _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
