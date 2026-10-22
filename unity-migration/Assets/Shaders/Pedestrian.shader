// ---------------------------------------------------------------------------------------------
// Instanced pedestrian crowd. One draw call for the whole crowd via
// Graphics.DrawMeshInstancedIndirect; the per-person data (position, heading, gait phase,
// speed, scale, tint) lives in a StructuredBuffer written by PedestrianCrowd.cs.
//
// The walk cycle is a vertex-shader swing on six box parts (torso, head, 2 arms, 2 legs).
// uv1 = (partId, 0 at the pivot .. 1 at the extremity). Swap this mesh for a VAT-skinned one
// when the real pedestrian model is ported; the buffer contract stays the same.
// ---------------------------------------------------------------------------------------------
Shader "Nyc/Pedestrian"
{
    Properties
    {
        _AmbientBoost ("Ambient boost", Range(0, 2)) = 0.55
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/NycCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _AmbientBoost;
            CBUFFER_END

            struct PedInstance
            {
                float4 posHeading;   // xyz = world position, w = yaw (Unity)
                float4 params;       // x = gait phase, y = speed, z = scale, w = unused
                float4 tint;
            };

            StructuredBuffer<PedInstance> _Instances;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv1        : TEXCOORD1;
                uint   instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                PedInstance inst = _Instances[input.instanceID];

                float part = input.uv1.x;
                float t = input.uv1.y;
                float phase = inst.params.x;
                float speed = inst.params.y;
                float scale = max(inst.params.z, 0.01);

                // Arms swing opposite to legs.
                float offset = (abs(part - 3.0) < 0.5 || abs(part - 4.0) < 0.5) ? 3.14159265 : 0.0;

                float pivotY = 0.0;
                float amplitude = 0.0;
                if (part > 3.5)       { pivotY = 0.88; amplitude = 0.58; }   // legs
                else if (part > 1.5)  { pivotY = 1.44; amplitude = 0.42; }   // arms

                float swing = sin(phase + offset) * amplitude * saturate(speed / 1.4) * t;

                float3 p = input.positionOS.xyz;
                float3 rel = p - float3(0.0, pivotY, 0.0);
                float cs = cos(swing);
                float sn = sin(swing);
                rel = float3(rel.x, rel.y * cs - rel.z * sn, rel.y * sn + rel.z * cs);
                p = rel + float3(0.0, pivotY, 0.0);
                p *= scale;

                float heading = inst.posHeading.w;
                float ch = cos(heading);
                float sh = sin(heading);
                float3 world = float3(p.x * ch + p.z * sh, p.y, -p.x * sh + p.z * ch) + inst.posHeading.xyz;

                float3 n = input.normalOS;
                n = float3(n.x * ch + n.z * sh, n.y, -n.x * sh + n.z * ch);

                output.positionCS = TransformWorldToHClip(world);
                output.positionWS = world;
                output.normalWS = n;
                output.color = inst.tint;
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float ndl = saturate(dot(normalWS, mainLight.direction));
                float shadow = mainLight.shadowAttenuation;

                float3 ambient = SampleSH(normalWS) * _AmbientBoost;
                float3 col = input.color.rgb * (mainLight.color * ndl * shadow + ambient);

                col = MixFog(col, input.fogCoord);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
