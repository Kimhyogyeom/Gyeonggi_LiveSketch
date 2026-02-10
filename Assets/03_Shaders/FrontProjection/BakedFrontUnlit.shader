Shader "LiveSketch/BakedFrontUnlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        [Header(Side Fade)]
        _BackgroundColor ("Background Color", Color) = (0.9, 0.9, 0.9, 1)
        _FadeThreshold ("Fade Threshold", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _BackgroundColor;
            float _FadeThreshold;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 베이크된 UV로 텍스처 샘플링
                fixed4 col = tex2D(_MainTex, i.uv);

                // 측면 페이드: 카메라를 안 보는 면은 배경색으로 페이드
                float3 normal = normalize(i.worldNormal);
                float facing = abs(normal.z);
                if (facing < _FadeThreshold)
                {
                    col = lerp(_BackgroundColor, col, facing / _FadeThreshold);
                }

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    Fallback "Unlit/Texture"
}
