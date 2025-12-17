Shader "LiveSketch/SidePlanarUnlitURP"
{
    Properties
    {
        _MainTex ("Side Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)

        _SideThreshold ("Side Threshold", Range(0,1)) = 0.6
        _EdgeSoftness ("Edge Softness", Range(0,0.5)) = 0.08

        _UInset ("U Inset (Crop Left/Right)", Range(0,0.49)) = 0.0
        _VInset ("V Inset (Crop Top/Bottom)", Range(0,0.49)) = 0.0

        _UOffset ("U Offset", Range(-0.5,0.5)) = 0
        _VOffset ("V Offset", Range(-0.5,0.5)) = 0

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
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _SideThreshold;
                float _EdgeSoftness;

                float _UInset, _VInset;
                float _UOffset, _VOffset;

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

                float side = abs(n.x);
                float soft = max(_EdgeSoftness, 1e-5);
                float mask = smoothstep(_SideThreshold, _SideThreshold + soft, side);

                float y01 = saturate((IN.positionOS.y - _MinY) / max(_MaxY - _MinY, 1e-5));
                float z01 = saturate((IN.positionOS.z - _MinZ) / max(_MaxZ - _MinZ, 1e-5));

                float u = lerp(_UInset, 1.0 - _UInset, z01) + _UOffset;
                float v = lerp(_VInset, 1.0 - _VInset, y01) + _VOffset;

                u = clamp(u, 0.001, 0.999);
                v = clamp(v, 0.001, 0.999);

                float2 uv = float2(u, v);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * _Tint;

                return lerp(half4(1,1,1,1), tex, mask);
            }
            ENDHLSL
        }
    }
}
