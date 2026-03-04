Shader "LiveSketch/AnimatedColorProjection"
{
    Properties
    {
        _MainTex ("Scanned Color Texture", 2D) = "white" {}

        [Header(Base Colors)]
        _BaseColor ("Base Color (paper areas)", Color) = (0.95, 0.92, 0.88, 1)
        _BackgroundColor ("Background Color (out of bounds / side)", Color) = (0.9, 0.9, 0.9, 1)

        [Header(Paper Detection)]
        _PaperBrightness ("Paper Brightness Threshold", Range(0.5, 1.0)) = 0.85
        _PaperSaturation ("Paper Max Saturation", Range(0, 0.5)) = 0.15
        _BlendSmoothness ("Blend Smoothness", Range(0.01, 0.3)) = 0.1

        [Header(3D Shading)]
        _ShadingStrength ("Shading Strength (0=flat, 1=full)", Range(0, 1)) = 0.3
        _LightDir ("Light Direction (xyz)", Vector) = (0.2, 0.5, -1, 0)
        _AmbientLight ("Ambient Light (min brightness)", Range(0, 1)) = 0.6

        [Header(Side Fade)]
        _FadeThreshold ("Side Fade Threshold", Range(0, 1)) = 0.3
        _ProjectionAxis ("0=Z front, 1=X side, 2=-Z back, 3=-X rside", Float) = 0
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
                float2 uv : TEXCOORD0; // 바인드 포즈에서 구운 투영 UV
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 projUV : TEXCOORD0;   // 구운 UV (애니메이션 불변)
                float3 worldNormal : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            sampler2D _MainTex;
            fixed4 _BaseColor;
            fixed4 _BackgroundColor;
            float _PaperBrightness, _PaperSaturation;
            float _BlendSmoothness;
            float _ShadingStrength;
            float4 _LightDir;
            float _AmbientLight;
            float _FadeThreshold;
            float _ProjectionAxis;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.projUV = v.uv; // 메시에 구운 UV를 그대로 전달
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // === Step 1: 구운 UV 사용 (flip/scale/offset/rotation 이미 C#에서 적용됨) ===
                float2 uv = i.projUV;

                // === Step 2: Out of bounds → background ===
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
                    return _BackgroundColor;

                // === Step 3: Sample scan texture AS-IS ===
                fixed4 texCol = tex2D(_MainTex, uv);

                // === Step 4: Paper area → blend to base color ===
                float cMax = max(max(texCol.r, texCol.g), texCol.b);
                float cMin = min(min(texCol.r, texCol.g), texCol.b);
                float sat = (cMax > 0.001) ? (cMax - cMin) / cMax : 0.0;
                float val = cMax;

                float paperBlend = smoothstep(_PaperBrightness - _BlendSmoothness, _PaperBrightness, val)
                                 * (1.0 - smoothstep(_PaperSaturation, _PaperSaturation + _BlendSmoothness, sat));

                fixed4 finalColor = lerp(texCol, _BaseColor, paperBlend);
                finalColor.a = 1;

                // === Step 5: 3D Shading (Half-Lambert) ===
                if (_ShadingStrength > 0.001)
                {
                    float3 lightDir = normalize(_LightDir.xyz);
                    float ndl = dot(normalize(i.worldNormal), lightDir);
                    float halfLambert = ndl * 0.5 + 0.5;
                    float shading = lerp(1.0, lerp(_AmbientLight, 1.0, halfLambert), _ShadingStrength);
                    finalColor.rgb *= shading;
                }

                // === Step 6: Side fade ===
                float3 normal = normalize(i.worldNormal);
                int axis = (int)_ProjectionAxis;
                // 0,2=Z축(정면/뒷면) 1,3=X축(좌/우) 4,5=Y축(위/아래)
                float facing = (axis == 4 || axis == 5) ? abs(normal.y) :
                               (axis == 1 || axis == 3) ? abs(normal.x) : abs(normal.z);

                if (facing < _FadeThreshold)
                {
                    float t = facing / max(_FadeThreshold, 0.001);
                    finalColor = lerp(_BackgroundColor, finalColor, t);
                }

                UNITY_APPLY_FOG(i.fogCoord, finalColor);
                return finalColor;
            }
            ENDCG
        }
    }

    Fallback "Unlit/Texture"
}
