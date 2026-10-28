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
                float seed = inst.params.w;

                // Arms swing opposite to legs.
                float offset = (abs(part - 3.0) < 0.5 || abs(part - 4.0) < 0.5) ? 3.14159265 : 0.0;

                float pivotY = 0.0;
                float amplitude = 0.0;
                if (part > 3.5)       { pivotY = 0.88; amplitude = 0.62; }   // legs: crisp scissor stride
                else if (part > 1.5)  { pivotY = 1.44; amplitude = 0.48; }   // arms: clear arm swing

                float swing = sin(phase + offset) * amplitude * saturate(speed / 1.4) * t;

                float3 p = input.positionOS.xyz;
                float3 rel = p - float3(0.0, pivotY, 0.0);
                float cs = cos(swing);
                float sn = sin(swing);
                rel = float3(rel.x, rel.y * cs - rel.z * sn, rel.y * sn + rel.z * cs);
                p = rel + float3(0.0, pivotY, 0.0);

                // Organic gait dynamics: natural torso/shoulder counter-rotation & stride bob
                // Makes pedestrians instantly recognizable as real walking humans from high/aerial views
                float gaitNorm = saturate(speed / 1.4);
                if (part < 1.5)
                {
                    float torsoYaw = sin(phase) * 0.052 * gaitNorm; // subtle shoulder sway
                    float cy = cos(torsoYaw);
                    float sy = sin(torsoYaw);
                    p = float3(p.x * cy + p.z * sy, p.y, -p.x * sy + p.z * cy);
                }
                float strideBob = abs(sin(phase)) * 0.034 * gaitNorm;
                p.y += strideBob;
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

                // --- Multiverse Palette Assignment by Part ---
                float s1 = frac(seed * 7.13 + 0.17);
                float s2 = frac(seed * 13.31 + 0.53);
                float s3 = frac(seed * 19.79 + 0.81);

                // Diverse NYC skin tones
                float3 skinTones[6] = {
                    float3(0.92, 0.78, 0.68), // Fair peach
                    float3(0.86, 0.70, 0.56), // Warm beige
                    float3(0.74, 0.58, 0.44), // Olive tan
                    float3(0.58, 0.42, 0.30), // Warm bronze
                    float3(0.38, 0.25, 0.17), // Deep brown
                    float3(0.24, 0.16, 0.11)  // Espresso
                };
                float3 skin = skinTones[min((int)(s1 * 6.0), 5)];

                // Hair tones
                float3 hairTones[5] = {
                    float3(0.08, 0.08, 0.09), // Black
                    float3(0.20, 0.13, 0.08), // Dark brown
                    float3(0.74, 0.62, 0.38), // Blonde
                    float3(0.36, 0.18, 0.10), // Chestnut
                    float3(0.52, 0.52, 0.55)  // Grey
                };
                float3 hair = hairTones[min((int)(s2 * 5.0), 4)];

                // Pants tones (jeans, chinos, dark trousers)
                float3 pantsTones[6] = {
                    float3(0.14, 0.22, 0.38), // Blue denim
                    float3(0.09, 0.12, 0.22), // Dark wash
                    float3(0.60, 0.54, 0.42), // Khaki chino
                    float3(0.16, 0.16, 0.18), // Charcoal slacks
                    float3(0.07, 0.07, 0.08), // Black jeans
                    float3(0.44, 0.45, 0.48)  // Grey trousers
                };
                float3 pants = pantsTones[min((int)(s3 * 6.0), 5)];
                float3 shoes = (s1 > 0.5) ? float3(0.85, 0.85, 0.85) : float3(0.10, 0.10, 0.12);

                float4 vertexCol = inst.tint; // Part 0: Torso jacket/coat/shirt

                if (abs(part - 1.0) < 0.5) // Part 1: Head
                {
                    // Hair on top crown and sides; skin on face/neck
                    bool isHair = (input.positionOS.y > 1.66) || (input.positionOS.y > 1.62 && input.normalOS.y > 0.3);
                    vertexCol = float4(isHair ? hair : skin, 1.0);
                }
                else if (part > 1.5 && part < 3.5) // Parts 2 & 3: Arms
                {
                    // Upper 72% sleeve matches coat/jacket; lower 28% is bare hand/wrist
                    vertexCol = (t > 0.72) ? float4(skin, 1.0) : inst.tint;
                }
                else if (part > 3.5) // Parts 4 & 5: Legs
                {
                    // Upper 82% pants; lower 18% shoes
                    vertexCol = (t > 0.82) ? float4(shoes, 1.0) : float4(pants, 1.0);
                }

                output.color = vertexCol;
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Smooth distance-based dither fade to eliminate pop-in at horizon and from aerial views
                float camDist = length(input.positionWS - _WorldSpaceCameraPos);
                float fade = 1.0 - saturate((camDist - 440.0) / 90.0);
                float2 screenPos = input.positionCS.xy;
                int2 ditherCoord = int2(fmod(screenPos, 4.0));
                float dither[16] = {
                    0.0625, 0.5625, 0.1875, 0.6875,
                    0.8125, 0.3125, 0.9375, 0.4375,
                    0.2500, 0.7500, 0.1250, 0.6250,
                    1.0000, 0.5000, 0.8750, 0.3750
                };
                float threshold = dither[ditherCoord.y * 4 + ditherCoord.x];
                clip(fade - threshold);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float ndl = saturate(dot(normalWS, mainLight.direction));
                float shadow = mainLight.shadowAttenuation;

                // Rich hemisphere ambient fill so characters are never pitch-black
                float3 skyFill = lerp(_NycSkyHorizonColor.rgb, _NycSkyTopColor.rgb, 0.40) * 0.42;
                float3 groundFill = float3(0.20, 0.20, 0.22) * 0.35;
                float hemi = normalWS.y * 0.5 + 0.5;
                float3 ambient = lerp(groundFill, skyFill, hemi) * _AmbientBoost + SampleSH(normalWS) * 0.35;
                ambient = max(ambient, float3(0.24, 0.24, 0.26));

                // Distinct human silhouette contrast from both street level and aerial (helicopter/flight) cameras
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), 3.0);
                float topDownAngle = saturate(viewDir.y * 1.4);
                float3 rimGlow = mainLight.color * fresnel * (0.28 + topDownAngle * 0.25);

                float3 lighting = mainLight.color * ndl * shadow + ambient + rimGlow;
                float3 col = input.color.rgb * lighting;

                col = MixFog(col, input.fogCoord);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
