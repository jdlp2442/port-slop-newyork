Shader "Nyc/TreeFoliage"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.35, 0.55, 0.25, 1.0)
        _TransColor ("Subsurface Color", Color) = (0.55, 0.75, 0.20, 1.0)
        _Roughness ("Roughness", Range(0, 1)) = 0.75
        _WindStrength ("Wind Strength", Float) = 0.35
        _WindSpeed ("Wind Speed", Float) = 2.4
        _WindFrequency ("Wind Frequency", Float) = 0.25
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.33
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        LOD 200
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TransColor;
                float _Roughness;
                float _WindStrength;
                float _WindSpeed;
                float _WindFrequency;
                float _Cutoff;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);

                // Wind sway based on vertex height and sway weight baked into input.color.r
                float swayWeight = input.color.r;
                float wave = sin(_Time.y * _WindSpeed + (posWS.x + posWS.z) * _WindFrequency);
                float wave2 = cos(_Time.y * (_WindSpeed * 0.7) + posWS.y * 0.2);
                float3 windOffset = float3(wave, wave2 * 0.2, wave * 0.5) * (_WindStrength * swayWeight);
                posWS += windOffset;

                output.positionCS = TransformWorldToHClip(posWS);
                output.positionWS = posWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.color = input.color;

                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 instColor = UNITY_ACCESS_INSTANCED_PROP(Props, _InstanceColor);
                float3 baseAlbedo = instColor.a > 0.01 ? instColor.rgb : _BaseColor.rgb;

                // Vertex color green-channel carries shade/occlusion variation
                float occ = lerp(0.65, 1.0, input.color.g);
                baseAlbedo *= occ;

                // Lighting
                Light mainLight = GetMainLight();
                float3 N = normalize(input.normalWS);
                float3 L = normalize(mainLight.direction);

                float NdotL = saturate(dot(N, L));
                // Foliage transmission / translucency when looking into sun
                float backLight = saturate(dot(-N, L)) * 0.45;
                float3 diffuse = baseAlbedo * (NdotL + backLight * _TransColor.rgb) * mainLight.color;

                // Ambient light
                float3 ambient = SampleSH(N) * baseAlbedo;

                float3 finalColor = diffuse + ambient;
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float _WindStrength;
                float _WindSpeed;
                float _WindFrequency;
            CBUFFER_END

            Varyings ShadowVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float swayWeight = input.color.r;
                float wave = sin(_Time.y * _WindSpeed + (posWS.x + posWS.z) * _WindFrequency);
                posWS.x += wave * _WindStrength * swayWeight;

                output.positionCS = TransformWorldToHClip(posWS);
                return output;
            }

            float4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
