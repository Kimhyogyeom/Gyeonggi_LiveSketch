Shader "LiveSketch/SideMirrorBlend"
{
    Properties
    {
        [Header(Side Texture)]
        _MainTex ("Side Texture", 2D) = "white" {}
        _HasTexture ("Has Side Texture", Float) = 0

        [Header(Front Texture)]
        _FrontTex ("Front Texture", 2D) = "white" {}
        _HasFrontTexture ("Has Front Texture", Float) = 0

        _BaseColor ("Base Color", Color) = (0.85, 0.85, 0.85, 1)

        [Header(Side UV Adjustment)]
        _OffsetX ("Side UV Offset X", Range(-1, 1)) = 0
        _OffsetY ("Side UV Offset Y", Range(-1, 1)) = 0
        _ScaleX ("Side UV Scale X", Range(0.1, 5)) = 1
        _ScaleY ("Side UV Scale Y", Range(0.1, 5)) = 1

        [Header(Front UV Adjustment)]
        _FrontOffsetX ("Front UV Offset X", Range(-1, 1)) = 0
        _FrontOffsetY ("Front UV Offset Y", Range(-1, 1)) = 0
        _FrontScaleX ("Front UV Scale X", Range(0.1, 5)) = 1
        _FrontScaleY ("Front UV Scale Y", Range(0.1, 5)) = 1
        _FrontFlipX ("Front Flip X", Float) = 0

        [Header(Blending)]
        _BlendSharpness ("Side Blend Sharpness", Range(0.5, 5)) = 2
        _FrontBlendStart ("Front Blend Start", Range(0, 1)) = 0.3
        _FrontBlendEnd ("Front Blend End", Range(0, 1)) = 0.7

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
                float3 worldPos : TEXCOORD3;
                float3 objectPos : TEXCOORD4;
                UNITY_FOG_COORDS(2)
            };

            sampler2D _MainTex;
            sampler2D _FrontTex;
            float4 _MainTex_TexelSize;
            float4 _FrontTex_TexelSize;
            fixed4 _BaseColor;
            fixed4 _OutOfBoundsColor;
            fixed4 _FillColor;
            fixed4 _OutlineColor;
            float _HasTexture;
            float _HasFrontTexture;
            float _OffsetX, _OffsetY;
            float _ScaleX, _ScaleY;
            float _FrontOffsetX, _FrontOffsetY;
            float _FrontScaleX, _FrontScaleY;
            float _FrontFlipX;
            float _BlendSharpness;
            float _FrontBlendStart, _FrontBlendEnd;
            float _FillMode;
            float _OutlineThickness;
            float _AlphaThreshold;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.objectPos = v.vertex.xyz;
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            // 주변 픽셀 중 색이 있는지 체크 (윤곽선 감지용)
            float checkNearbyColorSide(float2 uv, float thickness)
            {
                float hasColor = 0;
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
                        if (sample.a > _AlphaThreshold && sampleBrightness < 0.9)
                        {
                            hasColor = 1;
                            break;
                        }
                    }
                }
                return hasColor;
            }

            float checkNearbyColorFront(float2 uv, float thickness)
            {
                float hasColor = 0;
                float2 offsets[8] = {
                    float2(-1, 0), float2(1, 0), float2(0, -1), float2(0, 1),
                    float2(-1, -1), float2(1, -1), float2(-1, 1), float2(1, 1)
                };

                for (int j = 0; j < 8; j++)
                {
                    float2 sampleUV = uv + offsets[j] * thickness;
                    if (sampleUV.x >= 0 && sampleUV.x <= 1 && sampleUV.y >= 0 && sampleUV.y <= 1)
                    {
                        fixed4 sample = tex2D(_FrontTex, sampleUV);
                        float sampleBrightness = (sample.r + sample.g + sample.b) / 3.0;
                        if (sample.a > _AlphaThreshold && sampleBrightness < 0.9)
                        {
                            hasColor = 1;
                            break;
                        }
                    }
                }
                return hasColor;
            }

            // 빈 영역 처리 함수
            fixed4 processFillColor(fixed4 col, float2 uv, bool isFront)
            {
                float brightness = (col.r + col.g + col.b) / 3.0;
                bool isEmpty = (col.a < _AlphaThreshold) || (brightness > 0.92);

                if (isEmpty)
                {
                    if (_FillMode < 0.5)
                    {
                        col = _FillColor;
                    }
                    else if (_FillMode < 1.5)
                    {
                        float nearbyColor = isFront ? checkNearbyColorFront(uv, _OutlineThickness) : checkNearbyColorSide(uv, _OutlineThickness);
                        if (nearbyColor > 0.5)
                        {
                            col = _OutlineColor;
                        }
                        else
                        {
                            col = _FillColor;
                        }
                    }
                    else
                    {
                        col = _BaseColor;
                    }
                }
                return col;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // No texture -> base color
                if (_HasTexture < 0.5 && _HasFrontTexture < 0.5)
                    return _BaseColor;

                float3 normal = normalize(i.worldNormal);

                // === Side 텍스처 UV 계산 (좌우 측면용) ===
                float2 sideUV = i.uv;
                sideUV.x = (sideUV.x - 0.5) * _ScaleX + 0.5;
                sideUV.y = (sideUV.y - 0.5) * _ScaleY + 0.5;
                sideUV.x += _OffsetX;
                sideUV.y += _OffsetY;
                float2 clampedSideUV = float2(
                    clamp(sideUV.x, 0.001, 0.999),
                    clamp(sideUV.y, 0.001, 0.999)
                );

                // === Front 텍스처 UV 계산 (정면용) - Object Position 기반 투영 ===
                // 모델의 로컬 좌표(X, Y)를 UV로 사용하여 좌우 대칭 문제 해결
                // objectPos는 보통 -0.5 ~ 0.5 범위이므로 0~1로 정규화
                float2 frontUV;
                frontUV.x = i.objectPos.x + 0.5;  // -0.5~0.5 → 0~1
                frontUV.y = i.objectPos.y + 0.5;  // -0.5~0.5 → 0~1

                // X축 반전 옵션
                if (_FrontFlipX > 0.5)
                    frontUV.x = 1.0 - frontUV.x;

                // Side와 동일한 스케일/오프셋 적용
                frontUV.x = (frontUV.x - 0.5) * _FrontScaleX + 0.5;
                frontUV.y = (frontUV.y - 0.5) * _FrontScaleY + 0.5;
                frontUV.x += _FrontOffsetX;
                frontUV.y += _FrontOffsetY;
                float2 clampedFrontUV = float2(
                    clamp(frontUV.x, 0.001, 0.999),
                    clamp(frontUV.y, 0.001, 0.999)
                );

                // === 텍스처 샘플링 ===
                fixed4 sideCol = tex2D(_MainTex, clampedSideUV);
                fixed4 frontCol = tex2D(_FrontTex, clampedFrontUV);

                // === 빈 영역 처리 ===
                sideCol = processFillColor(sideCol, clampedSideUV, false);
                frontCol = processFillColor(frontCol, clampedFrontUV, true);

                // === 앞/측면 블렌딩 계산 ===
                // normal.z > 0: 정면 (Front 텍스처)
                // normal.z < 0: 뒷면 (Side 텍스처)
                // |normal.x| 큼: 좌우 측면 (Side 텍스처)

                // === 최종 색상 계산 ===
                fixed4 col;

                if (_HasFrontTexture > 0.5 && _HasTexture > 0.5)
                {
                    // 둘 다 있음
                    // 카메라 방향 벡터 계산 (월드 공간에서 카메라를 향하는 방향)
                    float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);

                    // 카메라를 향하고 있는지 확인 (뒷면인지 앞면인지)
                    float facing = dot(normal, viewDir);

                    // 전환점 계산
                    float threshold = (_FrontBlendStart + _FrontBlendEnd) * 0.5;

                    // 앞면(facing > 0)이고 normal.z가 threshold 이상이면 Front
                    // 뒷면(facing < 0)이거나 측면이면 Side
                    if (facing > 0 && normal.z > threshold)
                    {
                        col = frontCol;
                    }
                    else
                    {
                        col = sideCol;
                    }
                }
                else if (_HasFrontTexture > 0.5)
                {
                    col = frontCol;
                }
                else
                {
                    col = sideCol;
                }

                // 위/아래 면
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
