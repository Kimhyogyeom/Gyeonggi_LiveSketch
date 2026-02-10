Shader "LiveSketch/PlanarProjectionUnlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor ("Base Color (No Texture)", Color) = (0.85, 0.85, 0.85, 1)

        [Header(UV Adjustment)]
        _OffsetX ("Offset X", Range(-1, 1)) = 0
        _OffsetY ("Offset Y", Range(-1, 1)) = 0
        _ScaleX ("Scale X", Range(0.1, 5)) = 1
        _ScaleY ("Scale Y", Range(0.1, 5)) = 1

        [Header(Flip)]
        _FlipX ("Flip X", Float) = 0
        _FlipY ("Flip Y", Float) = 0

        [Header(Projection)]
        [Tooltip(Object bounds for UV mapping)]
        _BoundsMin ("Bounds Min (X, Y)", Vector) = (-0.5, -0.5, 0, 0)
        _BoundsMax ("Bounds Max (X, Y)", Vector) = (0.5, 0.5, 0, 0)

        [Header(Background)]
        _BackgroundColor ("Background Color", Color) = (0.9, 0.9, 0.9, 1)
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
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 objectPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            sampler2D _MainTex;
            fixed4 _BaseColor;
            fixed4 _BackgroundColor;
            float _OffsetX, _OffsetY;
            float _ScaleX, _ScaleY;
            float _FlipX, _FlipY;
            float4 _BoundsMin;
            float4 _BoundsMax;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.objectPos = v.vertex.xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 오브젝트 로컬 좌표를 UV로 변환 (평면 투영)
                // Bounds 범위를 0~1로 정규화
                float2 uv;
                uv.x = (i.objectPos.x - _BoundsMin.x) / (_BoundsMax.x - _BoundsMin.x);
                uv.y = (i.objectPos.y - _BoundsMin.y) / (_BoundsMax.y - _BoundsMin.y);

                // Flip 적용
                if (_FlipX > 0.5)
                    uv.x = 1.0 - uv.x;
                if (_FlipY > 0.5)
                    uv.y = 1.0 - uv.y;

                // Scale 적용 (중심 기준)
                uv.x = (uv.x - 0.5) * _ScaleX + 0.5;
                uv.y = (uv.y - 0.5) * _ScaleY + 0.5;

                // Offset 적용
                uv.x += _OffsetX;
                uv.y += _OffsetY;

                // UV 범위 체크 - 범위 밖이면 배경색
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
                {
                    return _BackgroundColor;
                }

                // 텍스처 샘플링
                fixed4 col = tex2D(_MainTex, uv);

                // 위/아래 면은 배경색으로 페이드
                float3 normal = normalize(i.worldNormal);
                float topBottomFade = abs(normal.y);
                if (topBottomFade > 0.7)
                {
                    col = lerp(col, _BackgroundColor, (topBottomFade - 0.7) * 2.5);
                }

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    Fallback "Unlit/Texture"
}
