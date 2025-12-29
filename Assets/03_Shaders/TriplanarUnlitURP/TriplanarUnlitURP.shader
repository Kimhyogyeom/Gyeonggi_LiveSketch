Shader "LiveSketch/SidePlanarUnlitURP"
{
    Properties
    {
        _MainTex ("Side Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)

        _SideThreshold ("Side Threshold", Range(0,1)) = 0.6
        _EdgeSoftness ("Edge Softness", Range(0,0.5)) = 0.08

        [Header(Transparency)]
        _WhiteThreshold ("White Threshold (투명 처리)", Range(0.5, 1.0)) = 0.95
        _BaseColor ("Base Color (색칠 안한 부분 색)", Color) = (1, 1, 1, 1)

        [Header(Position Adjust)]
        _OffsetX ("Offset X (좌우)", Range(-1,1)) = 0
        _OffsetY ("Offset Y (상하)", Range(-1,1)) = 0
        _ScaleX ("Scale X (가로 크기)", Range(0.1, 3)) = 1
        _ScaleY ("Scale Y (세로 크기)", Range(0.1, 3)) = 1
        _Rotation ("Rotation (회전)", Range(-180, 180)) = 0

        [Header(Flip Options)]
        [Toggle] _FlipX ("Flip X (좌우 반전)", Float) = 0
        [Toggle] _FlipY ("Flip Y (상하 반전)", Float) = 0

        [Header(Bounds Auto)]
        _MinY ("MinY", Float) = -1
        _MaxY ("MaxY", Float) = 1
        _MinZ ("MinZ", Float) = -1
        _MaxZ ("MaxZ", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalRenderPipeline" "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _SideThreshold;
                float _EdgeSoftness;
                float _WhiteThreshold;
                float4 _BaseColor;

                float _OffsetX, _OffsetY;
                float _ScaleX, _ScaleY;
                float _Rotation;
                float _FlipX, _FlipY;

                float _MinY, _MaxY;
                float _MinZ, _MaxZ;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionOS  = IN.positionOS.xyz;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);

                // 측면만 텍스처 적용
                float side = abs(n.x);
                float soft = max(_EdgeSoftness, 1e-5);
                float mask = smoothstep(_SideThreshold, _SideThreshold + soft, side);

                // Y좌표 → V (상하), Z좌표 → U (앞뒤/좌우)
                float y01 = saturate((IN.positionOS.y - _MinY) / max(_MaxY - _MinY, 1e-5));
                float z01 = saturate((IN.positionOS.z - _MinZ) / max(_MaxZ - _MinZ, 1e-5));

                // U = 좌우 (Z축 기준), V = 상하 (Y축 기준)
                float u = z01;
                float v = y01;

                // 회전 적용 (중심 기준, 도 단위) - 스케일보다 먼저!
                float rad = _Rotation * 3.14159265 / 180.0;
                float cosR = cos(rad);
                float sinR = sin(rad);
                float cu = u - 0.5;
                float cv = v - 0.5;
                u = cu * cosR - cv * sinR + 0.5;
                v = cu * sinR + cv * cosR + 0.5;

                // 스케일 적용 (중심 기준, X/Y 개별)
                // 값이 커지면 텍스처가 커짐 (확대)
                u = (u - 0.5) * _ScaleX + 0.5;
                v = (v - 0.5) * _ScaleY + 0.5;

                // 오프셋 적용
                u += _OffsetX;
                v += _OffsetY;

                // 반전
                if (_FlipX > 0.5) u = 1.0 - u;
                if (_FlipY > 0.5) v = 1.0 - v;

                // UV 범위 체크 - 벗어나면 흰색
                bool outOfBounds = (u < 0.0 || u > 1.0 || v < 0.0 || v > 1.0);

                // 클램프 (샘플링용)
                float uClamped = clamp(u, 0.001, 0.999);
                float vClamped = clamp(v, 0.001, 0.999);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(uClamped, vClamped)) * _Tint;

                // 흰색 판단 (색칠 안한 부분) - Inspector에서 조정 가능
                float luminance = dot(tex.rgb, float3(0.299, 0.587, 0.114));
                bool isWhite = luminance > _WhiteThreshold;

                // 범위 밖이거나 흰색이면 Base Color로 (반투명 가능)
                if (outOfBounds || isWhite || mask < 0.01)
                {
                    // Base Color 반환 (알파 조정 가능)
                    return _BaseColor;
                }

                // === 색칠한 부분만 명암 적용 ===
                Light mainLight = GetMainLight();
                float3 normal = normalize(IN.normalWS);

                // Lambert diffuse
                float NdotL = saturate(dot(normal, mainLight.direction));

                // Ambient + Diffuse
                float3 lighting = 0.4 + (mainLight.color * NdotL * 0.6);

                // 최종 색상 = 텍스처 * 명암 * 마스크
                half3 finalColor = tex.rgb * lighting;
                half alpha = mask; // 측면만 보이게

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
