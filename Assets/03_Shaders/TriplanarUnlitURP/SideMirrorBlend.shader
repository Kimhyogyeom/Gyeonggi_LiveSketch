Shader "LiveSketch/SideMirrorBlend"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.85, 0.85, 0.85, 1)
        _HasTexture ("Has Texture", Float) = 0

        [Header(UV Adjustment)]
        _OffsetX ("UV Offset X", Range(-1, 1)) = 0
        _OffsetY ("UV Offset Y", Range(-1, 1)) = 0
        _ScaleX ("UV Scale X", Range(0.1, 5)) = 1
        _ScaleY ("UV Scale Y", Range(0.1, 5)) = 1

        [Header(Blending)]
        _BlendSharpness ("Blend Sharpness", Range(0.5, 5)) = 2

        [Header(Out of Bounds)]
        _OutOfBoundsColor ("Out of Bounds Color", Color) = (0.1, 0.1, 0.1, 1)

        [Header(Empty Area Fill)]
        _FillColor ("Fill Color (Empty Areas)", Color) = (0.5, 0.5, 0.5, 1)
        _FillMode ("Fill Mode (0=Gray, 1=White Outline, 2=Dominant Color)", Range(0, 2)) = 0
        _OutlineThickness ("Outline Thickness", Range(0.001, 0.05)) = 0.01
        _OutlineColor ("Outline Color", Color) = (1, 1, 1, 1)
        _AlphaThreshold ("Alpha Threshold", Range(0, 1)) = 0.1
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
                float3 worldNormal : TEXCOORD0;
                float2 uv : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize; // 텍스처 크기 정보
            fixed4 _BaseColor;
            fixed4 _OutOfBoundsColor;
            fixed4 _FillColor;
            fixed4 _OutlineColor;
            float _HasTexture;
            float _OffsetX, _OffsetY;
            float _ScaleX, _ScaleY;
            float _BlendSharpness;
            float _FillMode;
            float _OutlineThickness;
            float _AlphaThreshold;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            // 주변 픽셀 중 색이 있는지 체크 (윤곽선 감지용)
            float checkNearbyColor(float2 uv, float thickness)
            {
                float hasColor = 0;
                // 8방향 체크
                float2 offsets[8] = {
                    float2(-1, 0), float2(1, 0), float2(0, -1), float2(0, 1),
                    float2(-1, -1), float2(1, -1), float2(-1, 1), float2(1, 1)
                };

                for (int j = 0; j < 8; j++)
                {
                    float2 sampleUV = uv + offsets[j] * thickness;
                    if (sampleUV.x >= 0 && sampleUV.x <= 1 && sampleUV.y >= 0 && sampleUV.y <= 1)
                    {
                        fixed4 sample = tex2D(_MainTex, sampleUV);
                        float sampleBrightness = (sample.r + sample.g + sample.b) / 3.0;
                        // 색이 있고 (너무 밝지 않고) 알파가 있으면
                        if (sample.a > _AlphaThreshold && sampleBrightness < 0.9)
                        {
                            hasColor = 1;
                            break;
                        }
                    }
                }
                return hasColor;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // No texture -> base color
                if (_HasTexture < 0.5)
                    return _BaseColor;

                float3 normal = normalize(i.worldNormal);

                // 기본 UV
                float2 uv = i.uv;

                // X, Y 개별 스케일 적용 (중심 기준)
                uv.x = (uv.x - 0.5) * _ScaleX + 0.5;
                uv.y = (uv.y - 0.5) * _ScaleY + 0.5;

                // 오프셋 적용
                uv.x += _OffsetX;
                uv.y += _OffsetY;

                // UV Clamp (범위 밖이면 가장자리 색상으로 늘림)
                float2 clampedUV = float2(
                    clamp(uv.x, 0.001, 0.999),
                    clamp(uv.y, 0.001, 0.999)
                );

                // 텍스처 샘플링 (clamped UV 사용)
                fixed4 col = tex2D(_MainTex, clampedUV);
                float brightness = (col.r + col.g + col.b) / 3.0;

                // === 빈 영역 처리 (투명하거나 흰색인 부분) ===
                bool isEmpty = (col.a < _AlphaThreshold) || (brightness > 0.92);

                if (isEmpty)
                {
                    // FillMode에 따른 처리
                    // 0 = 회색 채우기
                    // 1 = 흰색 윤곽선 (주변에 색이 있으면 윤곽선, 없으면 회색)
                    // 2 = 주요 색상으로 채우기 (BaseColor 사용)

                    if (_FillMode < 0.5)
                    {
                        // 모드 0: 단순 회색 채우기
                        col = _FillColor;
                    }
                    else if (_FillMode < 1.5)
                    {
                        // 모드 1: 윤곽선 모드 - 주변에 색이 있으면 윤곽선 표시
                        float nearbyColor = checkNearbyColor(clampedUV, _OutlineThickness);
                        if (nearbyColor > 0.5)
                        {
                            // 색 경계에 있으면 윤곽선 색
                            col = _OutlineColor;
                        }
                        else
                        {
                            // 완전히 빈 영역이면 채우기 색
                            col = _FillColor;
                        }
                    }
                    else
                    {
                        // 모드 2: 주요 색상으로 채우기
                        col = _BaseColor;
                    }
                }

                // 위/아래 면만 살짝 페이드
                float topBottomFade = abs(normal.y);
                if (topBottomFade > 0.7)
                {
                    col = lerp(col, _FillColor, (topBottomFade - 0.7) * 2.5);
                }

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}
