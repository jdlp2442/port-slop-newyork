Shader "Nyc/VehicleLightBeam"
{
    Properties
    {
        _HeadlightColor ("Headlight Beam Color", Color) = (1.0, 0.97, 0.90, 0.65)
        _TaillightColor ("Taillight Color", Color) = (1.0, 0.12, 0.06, 0.85)
        _NightIntensity ("Night Intensity", Range(0, 1)) = 1.0
        _BeamFalloff ("Beam Falloff", Float) = 2.0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+50" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 100
        Blend One One // Additive blend for light cones/beams
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
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _HeadlightColor;
                float4 _TaillightColor;
                float _NightIntensity;
                float _BeamFalloff;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _LightParams) // rgb: color tint, a: isBraking/type
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float4 lightParam = UNITY_ACCESS_INSTANCED_PROP(Props, _LightParams);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = lightParam;

                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float dist = input.uv.y; // 0 at lamp, 1 at beam tip
                float lateral = abs(input.uv.x - 0.5) * 2.0;

                // Soft circular/conical beam falloff
                float latFalloff = saturate(1.0 - lateral * lateral);
                float longFalloff = pow(saturate(1.0 - dist), _BeamFalloff);
                float intensity = latFalloff * longFalloff;

                float3 beamCol = input.color.rgb * intensity * _NightIntensity;
                return float4(beamCol, 1.0);
            }
            ENDHLSL
        }
    }
}
