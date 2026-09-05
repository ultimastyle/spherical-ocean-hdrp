// SphericalOceanUnderwater.shader
// Full-screen post-process effect for underwater rendering.
// Applies color absorption, fog, and caustics when camera is below the ocean surface.
//
// Usage: Attach to a Camera via HDRP Custom Pass Volume (Full-screen, after opaques).

Shader "SphericalOcean/Underwater"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _WaterColor ("Water Color", Color) = (0.0, 0.1, 0.2, 1.0)
        _AbsorptionColor ("Absorption Color", Color) = (0.0, 0.1, 0.2, 1.0)
        _ScatteringColor ("Scattering Color", Color) = (0.0, 0.4, 0.35, 1.0)
        _FogDensity ("Fog Density", Range(0.0, 1.0)) = 0.05
        _FogStart ("Fog Start Distance", Float) = 1.0
        _FogEnd ("Fog End Distance", Float) = 100.0
        _CausticsTexture ("Caustics Texture", 2D) = "black" {}
        _CausticsScale ("Caustics Scale", Float) = 5.0
        _CausticsStrength ("Caustics Strength", Range(0.0, 10.0)) = 2.0
        _CausticsSpeed ("Caustics Speed", Float) = 1.0
        _WaveIntensity ("Wave Distortion", Range(0.0, 1.0)) = 0.1
        _DepthFade ("Depth Fade", Range(0.0, 100.0)) = 50.0
        _WaveCrestFog ("Wave Crest Fog", Range(0.0, 1.0)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "HDRenderPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "SphericalOceanUnderwater"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 5.0

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesFunctions.hlsl"

            // --- Properties ---
            CBUFFER_START(UnderwaterCB)
                float4 _WaterColor;
                float4 _AbsorptionColor;
                float4 _ScatteringColor;
                float _FogDensity;
                float _FogStart;
                float _FogEnd;
                float _CausticsScale;
                float _CausticsStrength;
                float _CausticsSpeed;
                float _WaveIntensity;
                float _DepthFade;
                float _WaveCrestFog;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_CausticsTexture);
            SAMPLER(sampler_CausticsTexture);
            TEXTURE2D_X(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            // --- Vertex ---
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                float3 viewRay : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(Varyings, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.texcoord = input.texcoord;

                // Reconstruct view ray from screen position
                float2 ndc = input.texcoord * 2.0 - 1.0;
                float aspect = _ScreenSize.x / _ScreenSize.y;
                o.viewRay = float3(ndc.x * aspect, ndc.y, 1.0);

                return o;
            }

            // --- Fragment ---
            float4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 uv = i.texcoord;
                float4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                // Reconstruct world position from depth
                float rawDepth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, uv);
                float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                // View direction in world space
                float3 viewDir = normalize(i.viewRay);
                float3 worldPos = _WorldSpaceCameraPos + viewDir * sceneDepth;

                // --- Water Absorption ---
                // Absorption increases exponentially with depth
                float absorptionFactor = 1.0 - exp(-sceneDepth * _FogDensity);
                float3 absorption = lerp(float3(1, 1, 1), _AbsorptionColor.rgb, absorptionFactor);

                // --- Underwater Fog ---
                // Fog increases with distance, with a wave crest variation
                float fogFactor = saturate((sceneDepth - _FogStart) / max(_FogEnd - _FogStart, 0.001));
                fogFactor = fogFactor * _FogDensity;

                // Add wave crest fog (sinusoidal variation)
                float waveCrest = sin(_Time.y * 2.0 + worldPos.x * 0.5 + worldPos.z * 0.3) * 0.5 + 0.5;
                fogFactor += waveCrest * _WaveCrestFog * fogFactor;

                float3 fogColor = lerp(_WaterColor.rgb, _ScatteringColor.rgb, fogFactor);

                // --- Caustics ---
                // Project caustics from above (simulates light caustics on the sea floor)
                float2 causticsUV = worldPos.xz * _CausticsScale;
                float2 causticsOffset1 = float2(_Time.y * _CausticsSpeed * 0.1, _Time.y * _CausticsSpeed * 0.07);
                float2 causticsOffset2 = float2(-_Time.y * _CausticsSpeed * 0.08, _Time.y * _CausticsSpeed * 0.12);

                float caustics1 = SAMPLE_TEXTURE2D(_CausticsTexture, sampler_CausticsTexture, causticsUV + causticsOffset1).r;
                float caustics2 = SAMPLE_TEXTURE2D(_CausticsTexture, sampler_CausticsTexture, causticsUV * 1.3 + causticsOffset2).r;
                float caustics = min(caustics1, caustics2) * _CausticsStrength;

                // Fade caustics with depth
                float causticsFade = saturate(1.0 - sceneDepth / _DepthFade);
                caustics *= causticsFade;

                // --- Wave Distortion ---
                // Slight screen-space distortion from waves
                float2 distortion = float2(
                    sin(_Time.y * 3.0 + uv.y * 10.0) * _WaveIntensity,
                    cos(_Time.y * 2.5 + uv.x * 8.0) * _WaveIntensity
                ) * 0.01;

                float4 distortedSource = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + distortion);

                // --- Combine ---
                float3 color = distortedSource.rgb * absorption;
                color = lerp(color, fogColor, saturate(fogFactor));
                color += caustics * _WaterColor.rgb;

                // Depth-based color shift to deeper blue at distance
                float depthShift = saturate(sceneDepth / _DepthFade);
                color = lerp(color, _WaterColor.rgb * 0.5, depthShift * 0.3);

                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
