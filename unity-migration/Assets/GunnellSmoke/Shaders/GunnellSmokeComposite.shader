Shader "Hidden/GunnellSmoke/Composite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "GunnellSmokeComposite"
            ZTest Always
            ZWrite Off
            Cull Off
            // El buffer trae color PREMULTIPLICADO (el accum del raymarch ya
            // multiplica por alfa): mezclar con One, no con SrcAlpha, o todo
            // sale al doble de oscuro.
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D_X_HALF(_GunnellAlbedo);
            TEXTURE2D_X_HALF(_GunnellMask);
            float4 _GunnellBufferSize;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                o.uv = GetFullScreenTriangleTexCoord(vertexID);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float2 pixel = uv * _GunnellBufferSize.xy - 0.5;
                float2 basePixel = floor(pixel);
                float2 fraction = frac(pixel);
                float4 result = 0.0;
                float totalWeight = 0.0;
                // Bilinear de cuatro vecinos con rechazo por profundidad antes
                // de interpolar. No mezclar el centinela de una muestra vacía
                // con la profundidad del humo: recortaba los bordes transparentes.
                [unroll] for (int y = 0; y < 2; y++)
                [unroll] for (int x = 0; x < 2; x++)
                {
                    float2 tapUV = (basePixel + float2(x, y) + 0.5) * _GunnellBufferSize.zw;
                    float4 smoke = SAMPLE_TEXTURE2D_X(_GunnellAlbedo, sampler_PointClamp, tapUV);
                    float2 mask = SAMPLE_TEXTURE2D_X(_GunnellMask, sampler_PointClamp, tapUV).rg;
                    float tapEye = LinearEyeDepth(SampleSceneDepth(tapUV), _ZBufferParams);
                    float2 bilinear = lerp(1.0 - fraction, fraction, float2(x, y));
                    float depthWeight = exp(-abs(tapEye - sceneEye) / max(0.1, sceneEye * 0.02));
                    float weight = bilinear.x * bilinear.y * max(depthWeight, 0.0001);
                    float accept = smoothstep(-0.15, 0.0, sceneEye - mask.g);
                    result += smoke * accept * weight;
                    totalWeight += weight;
                }
                result /= max(totalWeight, 0.0001);
                return half4(result.rgb, saturate(result.a));
            }
            ENDHLSL
        }
        Pass
        {
            Name "GunnellSmokeSceneShadow"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend DstColor Zero
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D_X_HALF(_GunnellSceneShadow);
            float4 _GunnellBufferSize;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(id);
                o.uv = GetFullScreenTriangleTexCoord(id);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float sceneEye = LinearEyeDepth(SampleSceneDepth(i.uv), _ZBufferParams);
                float2 pixel = i.uv * _GunnellBufferSize.xy - 0.5;
                float2 basePixel = floor(pixel), fraction = frac(pixel);
                float attenuation = 0, totalWeight = 0;
                [unroll] for (int y = 0; y < 2; y++)
                [unroll] for (int x = 0; x < 2; x++)
                {
                    float2 tapUV = (basePixel + float2(x,y) + 0.5) * _GunnellBufferSize.zw;
                    float2 tap = SAMPLE_TEXTURE2D_X(_GunnellSceneShadow, sampler_PointClamp, tapUV).rg;
                    float2 weightXY = lerp(1.0-fraction, fraction, float2(x,y));
                    float weight = weightXY.x * weightXY.y
                        * exp(-abs(tap.y-sceneEye) / max(0.08, sceneEye*0.008));
                    attenuation += tap.x * weight; totalWeight += weight;
                }
                attenuation = totalWeight > 0.0001 ? attenuation/totalWeight : 1.0;
                return half4(attenuation.xxx, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
