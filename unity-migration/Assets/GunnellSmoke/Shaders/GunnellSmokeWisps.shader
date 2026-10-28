Shader "Gunnell Smoke/Wisps"
{
    Properties
    {
        _SoftDistance ("Soft intersection", Float) = 0.35
        [HideInInspector] _WispOrigin ("Smoke origin and vertical scale", Vector) = (0,0,0,1)
        [HideInInspector] _WispRadius ("Smoke shell radius", Float) = 1
        [HideInInspector] _WispExtinction ("Gas extinction along particle depth", Float) = 4
        [HideInInspector] _WispFrontOnly ("Front shell", Float) = 1
        [HideInInspector] _WispClip ("Clip against obstacles", Float) = 0
        [HideInInspector] _WispGasReady ("Current camera gas available", Float) = 0
        _WispRLT ("Directional flipbook RLT", 2D) = "white" {}
        _WispBBF ("Directional flipbook BBF", 2D) = "white" {}
        _WispFlipbook ("Baked smoke animation", 2D) = "white" {}
        _WispFrameGrid ("Columns, rows, first frame, last frame", Vector) = (6,6,6,32)
        _WispFilamentFrame ("Optional filament frame (-1 disables)", Float) = -1
        _WispFilamentShare ("Fraction of filament wisps", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "SmokeWisps"
            Tags { "LightMode"="GunnellSmokeWisps" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _WISP_BAKED_FLIPBOOK
            #pragma shader_feature_local_fragment _WISP_GAS_COMPOSITE
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _SoftDistance;
                float4 _WispOrigin;
                float _WispRadius;
                float _WispExtinction;
                float _WispFrontOnly;
                float _WispClip;
                float _WispGasReady;
                float4 _WispGridMin;
                float4 _WispGridSize;
                float4 _WispLightDirection;
                float _WispStylization;
                float _WispReceiveSceneShadows;
                float4 _WispSceneAmbient;
                float4 _WispSceneSun;
                float _WispLocalLightReady;
                float4 _WispLocalLightLayout;
                float4 _WispLocalLightTexel;
                float4 _WispFrameGrid;
                float _WispFilamentFrame;
                float _WispFilamentShare;
                float4 _WispFlipbook_TexelSize;
            CBUFFER_END
            TEXTURE3D(_WispOccupancy);
            SAMPLER(sampler_WispOccupancy);
            TEXTURE3D(_WispLocalLight);
            SAMPLER(sampler_WispLocalLight);
            TEXTURE2D(_WispRLT);
            TEXTURE2D(_WispBBF);
            SAMPLER(sampler_WispRLT);
            SAMPLER(sampler_WispBBF);
            TEXTURE2D(_WispFlipbook);
            SAMPLER(sampler_WispFlipbook);
            TEXTURE2D_X_HALF(_WispGasAlbedo);
            TEXTURE2D_X_HALF(_WispGasMask);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float4 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 uv : TEXCOORD1;
                float3 lightLocal : TEXCOORD2;
                half4 color : COLOR;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                output.color = input.color;
                float3 normal = TransformObjectToWorldNormal(input.normalOS);
                float3 tangent = TransformObjectToWorldDir(input.tangentOS.xyz);
                float3 bitangent = cross(normal, tangent) * input.tangentOS.w;
                output.lightLocal = float3(dot(_WispLightDirection.xyz, tangent),
                    dot(_WispLightDirection.xyz, bitangent), dot(_WispLightDirection.xyz, normal));
                return output;
            }
            float Hash(float2 p)
            {
                float3 h = frac(float3(p.xyx) * 0.1031);
                h += dot(h, h.yzx + 33.33);
                return frac((h.x + h.y) * h.z);
            }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
            }
            float2 FrameUV(float2 uv, float frame)
            {
                float2 tile = float2(fmod(frame, 4.0), floor(frame / 4.0));
                // Sample inside each tile to avoid bleeding into another frame.
                return (tile + (uv * 95.0 + 0.5) / 96.0) * 0.25;
            }
            float2 BakedFrameUV(float2 uv, float frame)
            {
                float2 grid = max(_WispFrameGrid.xy, 1.0);
                // The supplied sheet starts at the top left. Inset each frame
                // by one texel so bilinear filtering cannot reveal a neighbouring puff.
                float2 tile = float2(fmod(frame, grid.x), grid.y - 1.0 - floor(frame / grid.x));
                float2 inset = _WispFlipbook_TexelSize.xy * grid;
                return (tile + lerp(inset, 1.0 - inset, saturate(uv))) / grid;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float obstacleFade = 1.0;
                if (_WispClip > 0.5)
                {
                    float3 gridUV = (input.positionWS - _WispGridMin.xyz) / max(_WispGridSize.xyz, 0.001);
                    // Clip the billboard pixels too, including transparent glass:
                    // collision of its centre alone cannot stop a quad crossing a wall.
                    clip(min(min(gridUV.x, gridUV.y), gridUV.z));
                    clip(1.0 - max(max(gridUV.x, gridUV.y), gridUV.z));
                    float freeSpace = SAMPLE_TEXTURE3D_LOD(_WispOccupancy, sampler_WispOccupancy, gridUV, 0).g;
                    obstacleFade = smoothstep(0.5, 0.7, freeSpace);
                }
                float coverage, attenuation;
                #if defined(_WISP_BAKED_FLIPBOOK)
                float totalFrames = max(1.0, _WispFrameGrid.x * _WispFrameGrid.y);
                float firstFrame = clamp(_WispFrameGrid.z, 0.0, totalFrames - 1.0);
                float lastFrame = clamp(_WispFrameGrid.w, firstFrame, totalFrames - 1.0);
                // Play once over the particle's life. A wrapped explosion sheet
                // would jump from its dissipated end back to the initial blast.
                float frame = lerp(firstFrame, lastFrame, saturate(input.uv.z));
                // A reserved atlas tile supplies fine filaments. Stable random
                // selects them once per particle, with the same two atlas reads.
                bool filament = _WispFilamentFrame >= 0.0 && input.uv.w < _WispFilamentShare;
                if (filament) frame = clamp(floor(_WispFilamentFrame), 0.0, totalFrames - 1.0);
                float nextFrame = filament ? frame : min(floor(frame) + 1.0, lastFrame);
                float4 baked = lerp(SAMPLE_TEXTURE2D(_WispFlipbook, sampler_WispFlipbook,
                    BakedFrameUV(input.uv.xy, floor(frame))),
                    SAMPLE_TEXTURE2D(_WispFlipbook, sampler_WispFlipbook,
                    BakedFrameUV(input.uv.xy, nextFrame)), frac(frame));
                coverage = baked.a;
                // This source contains baked shading, not six-way transmission.
                // Retain its relief and tint it with the current smoke look.
                attenuation = saturate(dot(baked.rgb, float3(0.2126, 0.7152, 0.0722)));
                #else
                float frame = frac(input.uv.z * 0.65 + input.uv.w) * 16.0;
                float f0 = floor(frame), f1 = fmod(f0 + 1.0, 16.0), blend = frac(frame);
                float2 uv0 = FrameUV(input.uv.xy, f0), uv1 = FrameUV(input.uv.xy, f1);
                float4 rlt = lerp(SAMPLE_TEXTURE2D(_WispRLT, sampler_WispRLT, uv0),
                    SAMPLE_TEXTURE2D(_WispRLT, sampler_WispRLT, uv1), blend);
                float3 bbf = lerp(SAMPLE_TEXTURE2D(_WispBBF, sampler_WispBBF, uv0).rgb,
                    SAMPLE_TEXTURE2D(_WispBBF, sampler_WispBBF, uv1).rgb, blend);
                coverage = rlt.a;
                float3 light = normalize(input.lightLocal + float3(0, 0, 0.0001));
                float3 weights = light * light;
                attenuation = dot(weights, lerp(bbf, rlt.rgb, step(0.0, light)));
                #endif
                float lighting = lerp(0.62, 1.08, saturate(attenuation));
                lighting = lerp(lighting, lerp(0.66, 1.05, smoothstep(0.2, 0.7, attenuation)), _WispStylization);
                float sceneShadow = _WispReceiveSceneShadows > 0.5
                    ? MainLightRealtimeShadow(TransformWorldToShadowCoord(input.positionWS)) : 1.0;
                lighting *= lerp(0.58, 1.0, sceneShadow);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS.xy);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float particleEye = -TransformWorldToView(input.positionWS).z;
                float gasFade = 1.0;
                float3 cameraLocal=GetCameraPositionWS()-_WispOrigin.xyz;
                cameraLocal.y*=max(_WispOrigin.w,0.1);
                float insideGas=1.0-smoothstep(_WispRadius*0.65,_WispRadius*0.95,length(cameraLocal));
                float3 lightUV = (input.positionWS-_WispGridMin.xyz)/max(_WispGridSize.xyz,0.001);
                float3 lightOffset=float3(0,0,_WispLocalLightLayout.w);
                float3 atlasUV=clamp(saturate(lightUV)*_WispLocalLightLayout.xyz+lightOffset,
                    lightOffset+_WispLocalLightTexel.xyz,lightOffset+_WispLocalLightLayout.xyz-_WispLocalLightTexel.xyz);
                float3 localRadiance = _WispLocalLightReady > 0.5
                    ? SAMPLE_TEXTURE3D_LOD(_WispLocalLight,sampler_WispLocalLight,atlasUV,0).rgb : 0;
                half3 wispColor = input.color.rgb * (lighting*(_WispSceneAmbient.rgb*0.65
                    + _WispSceneSun.rgb*0.35) + localRadiance);
                #if defined(_WISP_GAS_COMPOSITE)
                if (_WispGasReady > 0.5)
                {
                    half4 gas = SAMPLE_TEXTURE2D_X(_WispGasAlbedo, sampler_PointClamp, screenUV);
                    float gasEye = SAMPLE_TEXTURE2D_X(_WispGasMask, sampler_PointClamp, screenUV).g;
                    insideGas=(1.0-smoothstep(0.12,0.8,gasEye))*smoothstep(0.65,0.95,gas.a);
                    float behindGas = smoothstep(0.02, 0.22, particleEye - gasEye);
                    // Full-ray opacity cannot occlude a nearby filament as if
                    // it were behind the entire cloud. Estimate only its gas path.
                    float rayScale=length(GetCameraPositionWS()-input.positionWS)/max(particleEye,0.001);
                    float gasPath=max(0.0,particleEye-gasEye)*rayScale;
                    gasFade=lerp(1.0,exp(-gasPath*max(_WispExtinction,0.0)),gas.a);
                    // Match exposed wisps to the lit surface beneath them. At the
                    // silhouette there is no gas colour, so their own relief remains.
                    half3 gasColor = gas.rgb / max(gas.a, 0.001);
                    wispColor = lerp(wispColor, gasColor, gas.a * (1.0 - behindGas) * 0.75);
                }
                #endif
                float softFade = saturate((sceneEye - particleEye) / max(_SoftDistance, 0.01));
                float cameraFade = smoothstep(0.12, 0.75, particleEye);
                float3 shellNormal = input.positionWS - _WispOrigin.xyz;
                shellNormal.y *= _WispOrigin.w * _WispOrigin.w;
                shellNormal *= rsqrt(max(dot(shellNormal, shellNormal), 0.0001));
                float facing = dot(shellNormal, GetWorldSpaceNormalizeViewDir(input.positionWS));
                float rim = smoothstep(-0.12, 0.25, facing);
                // Shell facing is useful outside, but must release smoothly
                // when the camera enters the actual gas, including in ducts.
                float frontFade = lerp(1.0, rim, _WispFrontOnly*(1.0-insideGas));
                half alpha = input.color.a * coverage * softFade * cameraFade * frontFade * obstacleFade * gasFade;
                return half4(wispColor * alpha, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
