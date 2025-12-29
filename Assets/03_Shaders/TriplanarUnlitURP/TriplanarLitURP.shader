Shader "LiveSketch/SidePlanarLitURP"
{
    Properties
    {
        _MainTex ("Side Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)

        [Header(Base Material)]
        _BaseColor ("Base Color (색칠 안한 부분)", Color) = (0.8, 0.8, 0.8, 1)
        [Toggle] _UseLitForBase ("색칠 안한 부분도 Lit 사용", Float) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0

        _SideThreshold ("Side Threshold", Range(0,1)) = 0.6
        _EdgeSoftness ("Edge Softness", Range(0,0.5)) = 0.08

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
        Tags { "RenderPipeline"="UniversalRenderPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // URP Lighting
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float4 _BaseColor;
                float _UseLitForBase;
                float _Smoothness;
                float _Metallic;

                float _SideThreshold;
                float _EdgeSoftness;

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
                float3 positionWS  : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(IN.normalOS);

                OUT.positionHCS = vertexInput.positionCS;
                OUT.positionWS = vertexInput.positionWS;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalWS = normalInput.normalWS;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);

                // 측면 마스크 계산
                float side = abs(n.x);
                float soft = max(_EdgeSoftness, 1e-5);
                float mask = smoothstep(_SideThreshold, _SideThreshold + soft, side);

                // Y좌표 → V (상하), Z좌표 → U (앞뒤/좌우)
                float y01 = saturate((IN.positionOS.y - _MinY) / max(_MaxY - _MinY, 1e-5));
                float z01 = saturate((IN.positionOS.z - _MinZ) / max(_MaxZ - _MinZ, 1e-5));

                float u = z01;
                float v = y01;

                // 회전 적용
                float rad = _Rotation * 3.14159265 / 180.0;
                float cosR = cos(rad);
                float sinR = sin(rad);
                float cu = u - 0.5;
                float cv = v - 0.5;
                u = cu * cosR - cv * sinR + 0.5;
                v = cu * sinR + cv * cosR + 0.5;

                // 스케일 적용
                u = (u - 0.5) * _ScaleX + 0.5;
                v = (v - 0.5) * _ScaleY + 0.5;

                // 오프셋 적용
                u += _OffsetX;
                v += _OffsetY;

                // 반전
                if (_FlipX > 0.5) u = 1.0 - u;
                if (_FlipY > 0.5) v = 1.0 - v;

                // UV 범위 체크
                bool outOfBounds = (u < 0.0 || u > 1.0 || v < 0.0 || v > 1.0);

                // 클램프 (샘플링용)
                float uClamped = clamp(u, 0.001, 0.999);
                float vClamped = clamp(v, 0.001, 0.999);

                // 텍스처 샘플링
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(uClamped, vClamped)) * _Tint;

                // 텍스처 적용 여부 체크
                bool isTextured = !(outOfBounds || mask < 0.01);
                half4 albedo = isTextured ? texColor : _BaseColor;

                // 색칠 안한 부분은 Unlit (기본), 옵션 켜면 Lit
                if (!isTextured && _UseLitForBase < 0.5)
                {
                    // Unlit: 라이트 없어도 보이게 (검은색 안 됨)
                    return _BaseColor;
                }

                // === Lighting 계산 (색칠한 부분 또는 UseLitForBase 켠 경우) ===
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo.rgb;
                surfaceData.alpha = albedo.a;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.occlusion = 1.0;
                surfaceData.emission = half3(0, 0, 0);

                // URP Lighting 계산
                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                // 검은색 방지: 최소 환경광 추가
                half3 minAmbient = albedo.rgb * 0.2;
                color.rgb = max(color.rgb, minAmbient);

                return color;
            }
            ENDHLSL
        }

        // ShadowCaster Pass (그림자 생성용)
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float3 _LightDirection;

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }

        // DepthOnly Pass
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
