Shader "CrazyElevator/Menu Texture"
{
    Properties { _MainTex ("Image", 2D) = "white" {} }
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
            sampler2D _MainTex;
            sampler2D _GUIClipTexture;
            float4x4 unity_GUIClipTextureMatrix;
            struct Input
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            struct Output
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 clipUV : TEXCOORD1;
                float4 color : COLOR;
            };
            Output Vert(Input input)
            {
                Output output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.clipUV = mul(unity_GUIClipTextureMatrix,
                    float4(UnityObjectToViewPos(input.vertex).xy, 0, 1)).xy;
                output.color = input.color;
                return output;
            }
            half4 Frag(Output input) : SV_Target
            {
                // Keep the sampled image in the framebuffer's colour space.
                half4 color = tex2D(_MainTex, input.uv) * input.color;
                color.a *= tex2D(_GUIClipTexture, input.clipUV).a;
                return color;
            }
            ENDCG
        }
    }
}
