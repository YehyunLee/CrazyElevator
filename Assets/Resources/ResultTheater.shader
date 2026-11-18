Shader "CrazyElevator/Result Theater"
{
    Properties
    {
        [HideInInspector] _MainTex ("Image", 2D) = "white" {}
        _Theater ("Curtain opening, lamp, darkness, opacity", Vector) = (0, 0, 1, 1)
        _Focus ("Spotlight center and radii", Vector) = (0.5, 0.5, 0.2, 0.25)
        _Accent ("Winner color", Color) = (1, 0.7, 0.45, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            float4 _Theater;
            float4 _Focus;
            float4 _Accent;

            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Output Vert(Input input)
            {
                Output output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            half3 Velvet(float u, float v, float gold, float lamp)
            {
                float ribs = .5 + .5 * cos(6.283185 * (u + .046 * sin(v * 10.0)));
                float folds = pow(ribs, 1.8);
                float rim = pow(ribs, 11.0);
                float grain = frac(sin(dot(floor(float2(u * 790, v * 1111)), float2(17.17, 71.93))) * 41721.137);
                half3 wine = half3(.073, .006, .019);
                half3 red = half3(.43, .036, .067);
                half3 fabric = lerp(wine, red, folds * .80 + rim * .20);
                fabric *= (1.08 - v * .36) * (.95 + grain * .08) + lamp * .35;
                half3 braid = lerp(half3(.27, .12, .065), half3(.69, .40, .16), .48 + folds * .52);
                return lerp(fabric, braid, gold);
            }

            half4 Frag(Output input) : SV_Target
            {
                float2 p = float2(input.uv.x, 1.0 - input.uv.y);
                float open = _Theater.x;
                float lamp = _Theater.y;
                float2 distance = (p - _Focus.xy) / max(_Focus.zw, .001);
                float radius = length(distance);
                float pool = 1.0 - smoothstep(.64, 1.06, radius);
                float halo = 1.0 - smoothstep(.22, 1.30, radius);

                // Layer a feathered circle and a very faint cone from the overhead lamp.
                float ray = saturate((p.y - .055) / max(.035, _Focus.y - .055));
                float beamX = lerp(.5, _Focus.x, ray);
                float halfWidth = lerp(.003, _Focus.z * .73, ray);
                float beam = (1.0 - smoothstep(halfWidth * .12, halfWidth, abs(p.x - beamX)))
                    * smoothstep(.055, .14, p.y) * (1.0 - smoothstep(_Focus.y, _Focus.y + .12, p.y));
                float darkAlpha = _Theater.z * (1.0 - pool * lamp);
                float glowAlpha = lamp * (halo * .21 + beam * .16);
                float alpha = glowAlpha + darkAlpha * (1.0 - glowAlpha);
                half3 warm = lerp(half3(1.0, .80, .53), _Accent.rgb, .18);
                half3 color = warm * glowAlpha / max(alpha, .001);

                float edgeX = min(p.x, 1.0 - p.x);
                float curtainWidth = lerp(.501, .084, open);
                float localX = edgeX / curtainWidth;
                float billow = sin(p.y * 5.0 + .8) * .013 * open;
                float curtain = 1.0 - smoothstep(curtainWidth + billow - .0012,
                    curtainWidth + billow + .0008, edgeX);
                float trim = smoothstep(.958, .976, saturate(localX)) * open;
                float valanceEdge = .065 + .021 * pow(abs(sin(p.x * 15.70796)), .78);
                float valance = 1.0 - smoothstep(valanceEdge, valanceEdge + .0018, p.y);
                float fringe = smoothstep(valanceEdge - .0065, valanceEdge - .0015, p.y);
                half3 cloth = Velvet(localX * 11.0 - open * 2.0, p.y, trim * .72, halo * lamp);
                half3 topCloth = Velvet(p.x * 32.0, p.y * 4.0, fringe * .8, 0);
                color = lerp(color, cloth, curtain);
                color = lerp(color, topCloth, valance);
                alpha = max(alpha, max(curtain, valance));
                return half4(color, alpha * _Theater.w);
            }
            ENDCG
        }
    }
}
