Shader "LiveSketch/SimpleTriplanar"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.8, 0.8, 0.8, 1)
        _MinZ ("MinZ", Float) = 0
        _MaxZ ("MaxZ", Float) = 1
        _MinY ("MinY", Float) = 0
        _MaxY ("MaxY", Float) = 1
        _HasTexture ("Has Texture", Float) = 0
        _OffsetX ("UV Offset X", Range(-1, 1)) = 0
        _OffsetY ("UV Offset Y", Range(-1, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
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
                float3 localPos : TEXCOORD0;
                float3 localNormal : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_FOG_COORDS(3)
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _BaseColor;
            float _MinZ;
            float _MaxZ;
            float _MinY;
            float _MaxY;
            float _HasTexture;
            float _OffsetX;
            float _OffsetY;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.localPos = v.vertex.xyz;
                o.localNormal = v.normal;
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 텍스처 없으면 베이스 컬러
                if (_HasTexture < 0.5)
                    return _BaseColor;

                // ★ 실시간 UV 오프셋 조정 (Inspector에서 조절 가능)
                // 좌우반전: 1.0 - i.uv.x 사용
                float2 uv = float2(1.0 - i.uv.x + _OffsetX, i.uv.y + _OffsetY);

                // 텍스처 샘플링
                fixed4 col = tex2D(_MainTex, uv);

                // 흰색/투명 영역은 베이스 컬러로 (임계값 높임: 0.92 → 0.98)
                float brightness = (col.r + col.g + col.b) / 3.0;
                if (brightness > 0.98)
                    return _BaseColor;

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    Fallback "Unlit/Texture"
}
