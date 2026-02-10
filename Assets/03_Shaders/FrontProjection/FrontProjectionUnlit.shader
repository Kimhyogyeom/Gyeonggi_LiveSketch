Shader "LiveSketch/FrontProjectionUnlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor ("Base Color (No Texture)", Color) = (0.85, 0.85, 0.85, 1)

        [Header(UV Adjustment)]
        _OffsetX ("Offset X", Range(-2, 2)) = 0
        _OffsetY ("Offset Y", Range(-2, 2)) = 0
        _ScaleX ("Scale X", Range(0.05, 20)) = 1
        _ScaleY ("Scale Y", Range(0.05, 20)) = 1

        [Header(Flip)]
        _FlipX ("Flip X", Float) = 0
        _FlipY ("Flip Y", Float) = 0

        [Header(Rotation)]
        _Rotation ("Rotation (0, 90, 180, 270)", Float) = 0

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
                float4 screenPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 objectPos : TEXCOORD2;
                UNITY_FOG_COORDS(3)
            };

            sampler2D _MainTex;
            fixed4 _BaseColor;
            fixed4 _BackgroundColor;
            float _OffsetX, _OffsetY;
            float _ScaleX, _ScaleY;
            float _FlipX, _FlipY;
            float _Rotation;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.objectPos = v.vertex.xyz;
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 오브젝트 로컬 좌표 기준 UV (Z 무시 → 평면 투영)
                float2 uv;
                uv.x = i.objectPos.x + 0.5;
                uv.y = i.objectPos.y + 0.5;

                // Flip 적용
                if (_FlipX > 0.5)
                    uv.x = 1.0 - uv.x;
                if (_FlipY > 0.5)
                    uv.y = 1.0 - uv.y;

                // Rotation 적용 (중심 기준)
                float2 centered = uv - 0.5;
                int rot = ((int)_Rotation / 90) % 4;
                if (rot == 1) // 90도
                {
                    uv = float2(centered.y + 0.5, -centered.x + 0.5);
                }
                else if (rot == 2) // 180도
                {
                    uv = float2(-centered.x + 0.5, -centered.y + 0.5);
                }
                else if (rot == 3) // 270도
                {
                    uv = float2(-centered.y + 0.5, centered.x + 0.5);
                }

                // Scale 적용 (중심 기준) - 값이 클수록 텍스처가 커짐
                uv.x = (uv.x - 0.5) / _ScaleX + 0.5;
                uv.y = (uv.y - 0.5) / _ScaleY + 0.5;

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

                // 측면 페이드 (옵션)
                float3 normal = normalize(i.worldNormal);
                float facing = abs(normal.z);
                if (facing < 0.3)
                {
                    col = lerp(_BackgroundColor, col, facing / 0.3);
                }

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    Fallback "Unlit/Texture"
}
