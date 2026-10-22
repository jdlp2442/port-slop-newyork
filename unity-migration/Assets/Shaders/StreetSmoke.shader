Shader "Nyc/StreetSmoke"
{
    Properties
    {
        _MainTex ("Smoke Texture (RGBA)", 2D) = "white" {}
        _SmokeColor ("Smoke Color", Color) = (0.95, 0.95, 0.98, 1.0)
        _ShadowColor ("Shadow Color", Color) = (0.55, 0.58, 0.65, 1.0)
        _SunDirection ("Sun Direction", Vector) = (0.45, 0.75, 0.45, 0.0)
        _DayNightFactor ("Day Night Factor", Range(0, 1)) = 1.0
        _Speed ("Rise Speed", Float) = 1.2
        _Expansion ("Expansion", Float) = 2.4
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 params : TEXCOORD1; // x: life, y: sunLobe, z: scale, w: alpha
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _SmokeColor;
                float4 _ShadowColor;
                float4 _SunDirection;
                float _DayNightFactor;
                float _Speed;
                float _Expansion;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _SmokeOrigin) // xyz: origin, w: phase
                UNITY_DEFINE_INSTANCED_PROP(float4, _SmokeWind)   // xyz: wind dir, w: scaleMul
                UNITY_DEFINE_INSTANCED_PROP(float4, _SmokeParams) // x: spinSpeed, y: curlSeed, zw: unused
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float4 origin = UNITY_ACCESS_INSTANCED_PROP(Props, _SmokeOrigin);
                float4 wind = UNITY_ACCESS_INSTANCED_PROP(Props, _SmokeWind);
                float4 sParams = UNITY_ACCESS_INSTANCED_PROP(Props, _SmokeParams);

                // Continuous rising life cycle [0..1]
                float phase = origin.w;
                float life = frac(phase + _Time.y * _Speed);

                // Expansion over lifetime
                float scale = (0.6 + life * _Expansion) * wind.w;

                // Wind curl and drift
                float3 windDrift = wind.xyz * (life * life * 4.5);
                float3 risePos = origin.xyz + float3(0, life * 7.5, 0) + windDrift;

                // Billboarding: camera facing quad
                float3 camRight = UNITY_MATRIX_V[0].xyz;
                float3 camUp    = UNITY_MATRIX_V[1].xyz;

                // Rotation of quad
                float spin = sParams.x * _Time.y + sParams.y;
                float cs = cos(spin), sn = sin(spin);
                float2 rotPos = float2(input.positionOS.x * cs - input.positionOS.y * sn,
                                       input.positionOS.x * sn + input.positionOS.y * cs);

                float3 worldPos = risePos + (camRight * rotPos.x + camUp * rotPos.y) * scale;
                output.positionCS = TransformWorldToHClip(worldPos);
                output.uv = input.uv;

                // Soft fade in at nozzle, dispersion at top
                float fadeIn = smoothstep(0.0, 0.15, life);
                float fadeOut = smoothstep(1.0, 0.65, life);
                float alpha = fadeIn * fadeOut;

                output.params = float4(life, 1.0, scale, alpha);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // R = 3D cloud billow illumination (top sun catch)
                // G = vertical self-shadowing gradient
                // B = inner vapor density
                // A = multi-lobe soft silhouette
                float sunCatch = tex.r;
                float hemiGrad = tex.g;
                float density  = tex.a * input.params.w;

                // Diffuse lighting mix between warm sunlit top and cool shadowed canyon
                float3 litColor = lerp(_ShadowColor.rgb, _SmokeColor.rgb, sunCatch * hemiGrad);
                // At night, steam catches warm street sodium lamps
                float3 nightColor = float3(0.65, 0.45, 0.25) * 0.45;
                float3 finalColor = lerp(nightColor, litColor, _DayNightFactor);

                return float4(finalColor, density);
            }
            ENDHLSL
        }
    }
}
