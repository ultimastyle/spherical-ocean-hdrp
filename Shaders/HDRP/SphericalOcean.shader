// Spherical Ocean HDRP — Physically-based water surface shader for planetary worlds
// Techniques adapted from:
//   - Crest Ocean System (MIT) — wave spectrum, LOD, foam
//   - Martins Upitis Optically Realistic Water (MIT) — Fresnel, scattering, extinction
//   - Ocean-URP (MIT) — JONSWAP spectrum, Bruneton sky, subsurface
//   - Water Surface Shader (MIT) — trochoid waves, ripple noise

Shader "SphericalOcean/HDRP"
{
    Properties
    {
        [Header(Wave Spectrum)]
        _WindSpeed("Wind Speed m/s", Float) = 10.0
        _WindDirection("Wind Direction rad", Float) = 0.0
        _WaveScale("Wave Scale", Range(0.01, 5.0)) = 1.0
        _WaveSpeed("Wave Speed", Range(0.01, 5.0)) = 1.0
        _WaveChoppiness("Choppiness", Range(0.0, 1.0)) = 0.8
        _MaxWaveAmplitude("Max Amplitude", Float) = 50.0
        _WorldScale("World Scale", Float) = 1.0

        [Header(Normals)]
        [NoScaleOffset] _Normals("Normal Map", 2D) = "bump" {}
        _NormalsScale("Normal Scale", Range(0.01, 200.0)) = 40.0
        _NormalsStrength("Normal Strength", Range(0.01, 2.0)) = 0.36
        _NormalsStrengthOverall("Overall Normal Strength", Range(0.0, 1.0)) = 1.0

        [Header(Scattering)]
        _Diffuse("Scatter Base", Color) = (0.0, 0.0027, 0.17, 1.0)
        _DiffuseGrazing("Scatter Grazing", Color) = (0.0, 0.0039, 0.169, 1.0)
        _DiffuseShadow("Scatter Shadow", Color) = (0.0, 0.0013, 0.085, 1.0)
        _ScatterAmount("Scatter Amount", Range(0.0, 10.0)) = 3.5
        _ScatterColor("Scatter Color", Color) = (0.0, 1.0, 0.95, 1.0)
        _ScatterFade("Scatter Fade", Range(0.0, 1.0)) = 0.5

        [Header(Subsurface Scattering)]
        [Toggle] _SubSurfaceScattering("Enable SSS", Float) = 1
        _SubSurfaceColour("SSS Tint", Color) = (0.088, 0.497, 0.456, 1.0)
        _SubSurfaceBase("SSS Base", Range(0.0, 4.0)) = 0.0
        _SubSurfaceSun("SSS Sun", Range(0.0, 10.0)) = 1.7
        _SubSurfaceSunFallOff("SSS Sun Falloff", Range(1.0, 16.0)) = 5.0
        _SubSurfaceDepthMax("Shallow Depth Max", Range(0.01, 50.0)) = 10.0
        _SubSurfaceDepthPower("Shallow Depth Falloff", Range(0.01, 10.0)) = 2.5
        _SubSurfaceShallowCol("Shallow Color", Color) = (0.0, 0.0039, 0.247, 1.0)

        [Header(Reflections)]
        _Specular("Specular", Range(0.0, 2.0)) = 0.7
        _SpecularMinRoughness("Min Roughness", Range(0.0, 1.0)) = 0.02
        _FresnelPower("Fresnel Power", Range(1.0, 20.0)) = 5.0
        _RefractiveIndexOfAir("IOR Air", Range(1.0, 2.0)) = 1.0
        _RefractiveIndexOfWater("IOR Water", Range(1.0, 2.0)) = 1.333
        [Toggle] _UseExactFresnel("Use Exact Fresnel", Float) = 1

        [Header(Directional Light)]
        _DirectionalLightBoost("Boost", Range(0.0, 512.0)) = 7.0
        _DirectionalLightFallOff("Falloff", Range(1.0, 4096.0)) = 275.0

        [Header(Foam)]
        [Toggle] _Foam("Enable Foam", Float) = 1
        [NoScaleOffset] _FoamTexture("Foam Texture", 2D) = "white" {}
        _FoamScale("Foam Scale", Range(0.01, 50.0)) = 10.0
        _WaveFoamFeather("Foam Feather", Range(0.001, 1.0)) = 0.4
        _FoamWhiteColor("Foam Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _ShorelineFoamMinDepth("Shoreline Foam Min Depth", Range(0.01, 5.0)) = 0.27
        _FoamIntensity("Foam Intensity", Range(0.0, 2.0)) = 1.0

        [Header(Transparency)]
        [Toggle] _Transparency("Enable Transparency", Float) = 1
        _DepthFogDensity("Depth Fog Density", Vector) = (0.9, 0.3, 0.35, 1.0)
        _RefractionStrength("Refraction Strength", Range(0.0, 2.0)) = 0.5
        _AberrationAmount("Chromatic Aberration", Range(0.0, 0.02)) = 0.002

        [Header(Caustics)]
        [Toggle] _Caustics("Enable Caustics", Float) = 1
        [NoScaleOffset] _CausticsTexture("Caustics Texture", 2D) = "black" {}
        _CausticsTextureScale("Caustics Scale", Range(0.0, 25.0)) = 5.0
        _CausticsTextureAverage("Caustics Grey Point", Range(0.0, 1.0)) = 0.07
        _CausticsStrength("Caustics Strength", Range(0.0, 10.0)) = 3.2
        _CausticsFocalDepth("Caustics Focal Depth", Range(0.0, 250.0)) = 2.0
        _CausticsDepthOfField("Caustics DoF", Range(0.01, 1000.0)) = 0.33

        [Header(Water Volume)]
        _Visibility("Visibility", Float) = 28.0
        _WaterExtinction("Water Extinction", Vector) = (0.6, 0.8, 1.0, 0.0)
        _SunTransmittance("Sun Transmittance", Vector) = (0.45, 0.55, 0.68, 0.0)
        _WaterColor("Water Color", Color) = (0.0078, 0.5176, 0.700, 1.0)

        [Header(Horizon)]
        _HorizonFog("Horizon Fog", Range(0.0, 100.0)) = 50.0

        [Header(Sky)]
        [NoScaleOffset] _SkyCubemap("Sky Cubemap", Cube) = "" {}
        _SkyIntensity("Sky Intensity", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "HDRenderPipelineLit"
            "Queue" = "Geometry+250"
        }

        Pass
        {
            Name "SphericalOceanHDRP"
            Tags { "LightMode" = "ForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ ENABLE_FOAM
            #pragma multi_compile _ ENABLE_SSS
            #pragma multi_compile _ ENABLE_CAUSTICS
            #pragma multi_compile _ ENABLE_TRANSPARENCY
            #pragma multi_compile _ ENABLE_NORMALS
            #pragma multi_compile _ ENABLE_SHADOWS

            // --- Core includes ---
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesFunctions.hlsl"

            // --- HDRP light access ---
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightDefinition.cs.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/ShaderVariablesLightLoop.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"

            // --- Texture declarations ---
            TEXTURE2D(_Normals);               SAMPLER(sampler_Normals);
            TEXTURE2D(_FoamTexture);           SAMPLER(sampler_FoamTexture);
            TEXTURE2D(_CausticsTexture);       SAMPLER(sampler_CausticsTexture);
            TEXTURE2D_X(_CameraDepthTexture);  SAMPLER(sampler_CameraDepthTexture);
            TEXTURE2D_X(_CameraColorTexture);  SAMPLER(sampler_CameraColorTexture);
            TEXTURECUBE(_SkyCubemap);          SAMPLER(sampler_SkyCubemap);

            // --- Per-frame uniforms (set via MaterialPropertyBlock, NOT in CBUFFER) ---
            float3 _OceanCenterPosWorld;
            float _CrestTime;

            // --- Material properties ---
            CBUFFER_START(SphericalOceanMaterial)
                float _WindSpeed;
                float _WindDirection;
                float _WaveChoppiness;
                float _WaveScale;
                float _WaveSpeed;
                float _MaxWaveAmplitude;
                float _WorldScale;
                half _NormalsStrengthOverall;
                half _NormalsStrength;
                half _NormalsScale;
                half3 _Diffuse;
                half3 _DiffuseGrazing;
                half3 _DiffuseShadow;
                half3 _SubSurfaceColour;
                half _SubSurfaceBase;
                half _SubSurfaceSun;
                half _SubSurfaceSunFallOff;
                half _SubSurfaceDepthMax;
                half _SubSurfaceDepthPower;
                half3 _SubSurfaceShallowCol;
                half _Specular;
                half _SpecularMinRoughness;
                half _FresnelPower;
                float _RefractiveIndexOfAir;
                float _RefractiveIndexOfWater;
                half _DirectionalLightFallOff;
                half _DirectionalLightBoost;
                half _FoamScale;
                half4 _FoamWhiteColor;
                half _WaveFoamFeather;
                half _ShorelineFoamMinDepth;
                half _FoamIntensity;
                half4 _DepthFogDensity;
                half _RefractionStrength;
                half _AberrationAmount;
                half _CausticsTextureScale;
                half _CausticsTextureAverage;
                half _CausticsStrength;
                half _CausticsFocalDepth;
                half _CausticsDepthOfField;
                half _ScatterAmount;
                half3 _ScatterColor;
                half _ScatterFade;
                float _Visibility;
                float3 _WaterExtinction;
                float3 _SunTransmittance;
                float3 _WaterColor;
                float _HorizonFog;
                float _UseExactFresnel;
                float _SkyIntensity;
            CBUFFER_END

            static const float PI = 3.14159265;
            static const float G = 9.81;

            // ============================================================
            //  TANGENT FRAME — compute tangent/bitangent from sphere normal
            // ============================================================

            void ComputeTangentFrame(float3 normal, out float3 tangent, out float3 bitangent)
            {
                // Continuous tangent frame — no hard branch at poles
                // Fritsch-Style: pick axis based on which component is smallest
                float3 a = abs(normal);
                float3 up = a.x <= a.y ? float3(1, 0, 0) : float3(0, 1, 0);
                if (a.z > max(a.x, a.y)) up = float3(0, 1, 0);
                tangent = normalize(cross(up, normal));
                bitangent = cross(normal, tangent);
            }

            // ============================================================
            //  HDRP LIGHT HELPER
            // ============================================================

            struct MainLight
            {
                float3 direction;
                float3 color;
                float  dimmer;
            };

            MainLight GetMainLight()
            {
                MainLight light = (MainLight)0;
                light.direction = float3(0, 1, 0);
                light.color = float3(1, 1, 1);
                light.dimmer = 1.0;

                // Guard: only access if buffer is bound (safe fallback if not)
                if (_DirectionalLightCount > 0)
                {
                    uint idx = (_DirectionalShadowIndex >= 0)
                        ? (uint)_DirectionalShadowIndex
                        : 0u;
                    idx = min(idx, (uint)_DirectionalLightCount - 1u);

                    DirectionalLightData dirLight = _DirectionalLightDatas[idx];
                    light.direction = dirLight.forward;
                    light.color = dirLight.color * dirLight.lightDimmer;
                    light.dimmer = dirLight.lightDimmer;
                }
                return light;
            }

            // ============================================================
            //  EXACT FRESNEL
            // ============================================================

            float FresnelDielectric(float3 incoming, float3 normal, float eta)
            {
                float c = abs(dot(incoming, normal));
                float g = eta * eta - 1.0 + c * c;

                if (g > 0.0)
                {
                    g = sqrt(g);
                    float A = (g - c) / (g + c);
                    float B = (c * (g + c) - 1.0) / (c * (g - c) + 1.0);
                    return 0.5 * A * A * (1.0 + B * B);
                }

                return 1.0;
            }

            float SchlickFresnel(float3 viewDir, float3 normal, float eta)
            {
                float R0 = (eta - 1.0) / (eta + 1.0);
                R0 *= R0;
                float cosTheta = saturate(dot(viewDir, normal));
                float fp = max(_FresnelPower, 1.0);
                return R0 + (1.0 - R0) * pow(1.0 - cosTheta, fp);
            }

            float CalculateFresnel(float3 viewDir, float3 normal)
            {
                float eta = _RefractiveIndexOfAir / _RefractiveIndexOfWater;

                if (_UseExactFresnel > 0.5)
                    return FresnelDielectric(-viewDir, normal, eta);

                return SchlickFresnel(viewDir, normal, eta);
            }

            // ============================================================
            //  GERSTNER WAVE — in TANGENT SPACE, Phillips spectrum
            // ============================================================

            struct GerstnerResult
            {
                float3 displacement; // in tangent space
                float3 normal;       // in tangent space
                float sss;
                float foamJacobian;  // analytic Jacobian for foam
            };

            float Phillips(float2 k, float2 wind)
            {
                float kLen = length(k);
                if (kLen < 1e-4) return 0.0;

                float kLen2 = kLen * kLen;
                float kLen4 = kLen2 * kLen2;
                float wDotK = dot(normalize(k), wind);
                float L = (_WindSpeed * _WindSpeed) / G;
                float L2 = L * L;

                float phillips = exp(-1.0 / (kLen2 * L2)) / kLen4;

                if (wDotK < 0.0)
                    phillips *= 0.07;

                phillips *= exp(-kLen2 * L2 * 0.01);

                return phillips;
            }

            GerstnerResult GerstnerWave(float3 tangentPos, float time)
            {
                // tangentPos is the position projected onto the local tangent plane
                GerstnerResult result;
                result.displacement = float3(0, 0, 0);
                result.normal = float3(0, 1, 0);
                result.sss = 0;
                result.foamJacobian = 0;

                float2 windDir = float2(cos(_WindDirection), sin(_WindDirection));
                float2 pos2D = tangentPos.xz * _WorldScale;

                // Accumulate wave steepness derivatives for analytic Jacobian
                float ddx_sum = 0;
                float ddy_sum = 0;

                for (int i = 0; i < 8; i++)
                {
                    float frequency = (i + 1) * _WaveScale * 0.08;
                    float2 k = windDir * frequency * 10.0;
                    float spectrum = Phillips(k, windDir * _WindSpeed);
                    float amplitude = sqrt(max(0.0, spectrum)) * _MaxWaveAmplitude * _WaveScale;
                    amplitude = min(amplitude, _MaxWaveAmplitude * 0.5);

                    float steepness = _WaveChoppiness * 0.3 / max(amplitude, 0.001);
                    steepness = min(steepness, 1.0);

                    float phase = dot(k * 0.1, pos2D) - time * _WaveSpeed * frequency;

                    float sinPhase = sin(phase);
                    float cosPhase = cos(phase);

                    // Displacement in tangent space: X = tangent, Z = bitangent
                    result.displacement.x += amplitude * steepness * cosPhase * windDir.x;
                    result.displacement.z += amplitude * steepness * cosPhase * windDir.y;
                    result.displacement.y += amplitude * sinPhase;

                    // Normal in tangent space
                    result.normal.x -= amplitude * sinPhase * windDir.x * frequency;
                    result.normal.z -= amplitude * sinPhase * windDir.y * frequency;

                    result.sss += amplitude * cosPhase;

                    // Analytic Jacobian derivatives (sum of Q_i * k_i * sin(phase))
                    ddx_sum += amplitude * steepness * windDir.x * frequency * sinPhase;
                    ddy_sum += amplitude * steepness * windDir.y * frequency * sinPhase;
                }

                // Foam from analytic Jacobian determinant
                result.foamJacobian = 1.0 - saturate(1.0 + ddx_sum + ddy_sum);
                result.normal = normalize(result.normal);
                return result;
            }

            // ============================================================
            //  TRIPLANAR NORMAL MAPPING
            // ============================================================

            struct NormalData
            {
                float3 normal;
                float3 lightNormal;
            };

            float3 SampleTriplanarNormal(float3 worldPos, float3 normal, float2 scale, float time)
            {
                float3 absNormal = abs(normal);
                float3 blendWeights = pow(absNormal, 1.5);
                blendWeights /= dot(blendWeights, 1.0);

                float2 uvXZ = worldPos.xz * scale;
                float2 uvXY = worldPos.xy * scale;
                float2 uvZY = worldPos.zy * scale;

                // Animate UVs with wind
                float2 windOffset = float2(cos(_WindDirection), sin(_WindDirection)) * time * _WindSpeed * 0.04;
                uvXZ += windOffset;
                uvXY += windOffset * 0.7;
                uvZY += windOffset * 0.5;

                float3 nXZ = UnpackNormal(SAMPLE_TEXTURE2D(_Normals, sampler_Normals, uvXZ)).xyz;
                float3 nXY = UnpackNormal(SAMPLE_TEXTURE2D(_Normals, sampler_Normals, uvXY)).xyz;
                float3 nZY = UnpackNormal(SAMPLE_TEXTURE2D(_Normals, sampler_Normals, uvZY)).xyz;

                // Blend using triplanar weights
                float3 blended = nXZ * blendWeights.y + nXY * blendWeights.z + nZY * blendWeights.x;
                return normalize(blended);
            }

            NormalData SampleNormalMaps(float3 worldPos, float3 vertexNormal, float time, float scale)
            {
                NormalData nd;
                float2 s = float2(scale, scale) * 0.001;

                // Sample at different scales for cascaded detail (4 scales = 12 texture lookups)
                float3 n0 = SampleTriplanarNormal(worldPos, vertexNormal, s * 0.04, time);
                float3 n1 = SampleTriplanarNormal(worldPos, vertexNormal, s * 0.1, time);
                float3 n2 = SampleTriplanarNormal(worldPos, vertexNormal, s * 0.5, time);
                float3 n3 = SampleTriplanarNormal(worldPos, vertexNormal, s * 1.5, time);

                float2 bigWaves = float2(0.35, 0.35);
                float2 smallWaves = float2(0.15, 0.15);

                nd.normal = normalize(
                    n0 * bigWaves.x + n1 * bigWaves.y +
                    n2 * smallWaves.x + n3 * smallWaves.y);

                nd.lightNormal = normalize(
                    n0 * bigWaves.x * 0.5 + n1 * bigWaves.y * 0.5 +
                    n2 * smallWaves.x * 0.1 + n3 * smallWaves.y * 0.1);

                return nd;
            }

            // ============================================================
            //  SCATTER COLOUR
            // ============================================================

            half3 ScatterColour(
                half surfaceDepth,
                half shadow,
                half sss,
                half3 view,
                half3 lightDir,
                half3 lightCol)
            {
                float v = abs(view.y);
                half3 col = lerp(_DiffuseGrazing, _Diffuse, v);

                #if defined(ENABLE_SHADOWS)
                    col = lerp(_DiffuseShadow, col, shadow);
                #endif

                #if defined(ENABLE_SSS)
                {
                    float shallowness = pow(1.0 - saturate(surfaceDepth / _SubSurfaceDepthMax), _SubSurfaceDepthPower);
                    col = lerp(col, _SubSurfaceShallowCol, shallowness);

                    half towardsSun = pow(saturate(dot(lightDir, -view)), _SubSurfaceSunFallOff);
                    half3 subsurface = (_SubSurfaceBase + _SubSurfaceSun * towardsSun) * _SubSurfaceColour.rgb * lightCol * shadow;
                    subsurface *= saturate(1.0 - v * v) * sss;

                    col += subsurface;
                }
                #endif

                return col;
            }

            // ============================================================
            //  WATER VOLUME
            // ============================================================

            float3 WaterColor(float3 viewDir, float3 lightDir, float sunFade)
            {
                float waterSunGradient = dot(viewDir, -lightDir);
                waterSunGradient = saturate(pow(waterSunGradient * 0.7 + 0.3, 2.0));

                float3 waterSunColor = float3(0.0, 1.0, 0.85) * waterSunGradient * 0.25;

                float waterGradient = saturate(dot(viewDir, float3(0.0, -1.0, 0.0)) * 0.5 + 0.5);

                float3 watercolor = (_WaterColor.rgb + waterSunColor) * waterGradient * 1.5;
                watercolor = lerp(watercolor * 0.3 * sunFade, watercolor, saturate(1.0 - exp(-sunFade * _SunTransmittance)));

                return watercolor;
            }

            // ============================================================
            //  VERTEX / FRAGMENT STRUCTURES
            // ============================================================

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 tangentSpacePos : TEXCOORD2; // position in tangent space for Gerstner
                float pixelZ : TEXCOORD3;
                float4 positionNDC : TEXCOORD4;
                float3 viewDir : TEXCOORD5;
                float3 lightDir : TEXCOORD6;
                half foamValue : TEXCOORD7;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ============================================================
            //  VERTEX SHADER — Gerstner in tangent space, foam analytic
            // ============================================================

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(Varyings, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 unpivotPos = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float3 center = _OceanCenterPosWorld;
                float3 sphereNormal = normalize(unpivotPos - center);

                // Compute tangent frame on sphere surface
                float3 tangent, bitangent;
                ComputeTangentFrame(sphereNormal, tangent, bitangent);

                // Project vertex position into tangent space for Gerstner phase calculation
                float3 localOffset = unpivotPos - center;
                float3 tangentSpacePos;
                tangentSpacePos.x = dot(localOffset, tangent);
                tangentSpacePos.y = dot(localOffset, sphereNormal);
                tangentSpacePos.z = dot(localOffset, bitangent);

                // Gerstner waves in tangent space
                GerstnerResult wave = GerstnerWave(tangentSpacePos, _CrestTime);

                // Displace in world space using tangent frame
                float3 displacedPos = unpivotPos;
                displacedPos += tangent * wave.displacement.x;
                displacedPos += bitangent * wave.displacement.z;
                displacedPos += sphereNormal * wave.displacement.y;

                o.worldPos = displacedPos;

                // Transform Gerstner normal from tangent space to world space
                float3 waveNormalTS = normalize(
                    float3(0, 1, 0) +
                    float3(1, 0, 0) * wave.normal.x +
                    float3(0, 0, 1) * wave.normal.z);
                float3 waveNormalWS = normalize(
                    tangent * waveNormalTS.x +
                    sphereNormal * waveNormalTS.y +
                    bitangent * waveNormalTS.z);
                o.worldNormal = normalize(v.normal + waveNormalWS - sphereNormal);

                o.tangentSpacePos = tangentSpacePos;
                o.foamValue = wave.foamJacobian;

                o.uv = v.uv;
                o.positionCS = TransformWorldToHClip(displacedPos);
                o.positionNDC = ComputeScreenPos(o.positionCS);
                o.pixelZ = TransformWorldToView(displacedPos).z;

                o.viewDir = normalize(_WorldSpaceCameraPos - displacedPos);

                MainLight mainLight = GetMainLight();
                o.lightDir = normalize(mainLight.direction);

                return o;
            }

            // ============================================================
            //  FRAGMENT SHADER
            // ============================================================

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 view = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 n_pixel = normalize(i.worldNormal);

                MainLight mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);
                half3 lightCol = mainLight.color * mainLight.dimmer;

                half sss = saturate(i.foamValue > 0 ? 0.5 : 0.1); // simplified SSS from vertex

                // Normal mapping with triplanar
                float3 mappedNormal = n_pixel;
                half3 lightScatterContrib = 0;
                float sunFade = saturate(1.0 - exp(-mainLight.direction.y));

                #if defined(ENABLE_NORMALS)
                {
                    NormalData nd = SampleNormalMaps(i.worldPos, n_pixel, _CrestTime, _NormalsScale);
                    mappedNormal = normalize(n_pixel + nd.normal * _NormalsStrengthOverall);

                    float3 lightNormal = normalize(n_pixel + nd.lightNormal * _NormalsStrengthOverall * 0.3);

                    // Light scatter
                    float3 lR = reflect(-lightDir, lightNormal);
                    float s = max(dot(lR, view) * 2.0 - 1.2, 0);
                    float lightScatter = saturate(
                        (saturate(dot(-lightDir, lightNormal) * 0.7 + 0.3) * s) * _ScatterAmount
                    ) * sunFade;

                    float3 scatterTint = lerp(_ScatterColor * float3(1.0, 0.4, 0.0), _ScatterColor, saturate(1.0 - sunFade));
                    lightScatterContrib = lightScatter * scatterTint;
                }
                #endif

                // Scatter colour
                half shadow = 1.0;
                half3 scatterCol = ScatterColour(0.0, shadow, sss, view, lightDir, lightCol);
                half3 col = scatterCol + lightScatterContrib;

                // Reflection via HDRP SampleSkyTexture
                {
                    half3 refl = reflect(-view, mappedNormal);
                    refl.y = max(refl.y, 0.0);

                    // Sky reflection via cubemap (assign HDRP baking cubemap or procedural sky cubemap)
                    float4 skyColor = SAMPLE_TEXTURECUBE(_SkyCubemap, sampler_SkyCubemap, refl) * _SkyIntensity;

                    #if defined(ENABLE_SHADOWS)
                    // GGX specular — energy-conserving microfacet model
                    float3 halfVec = normalize(-lightDir + view);
                    float NdotH = saturate(dot(mappedNormal, halfVec));
                    float NdotV = saturate(dot(mappedNormal, view));
                    float NdotL = saturate(dot(mappedNormal, -lightDir));
                    float roughness = max(_SpecularMinRoughness, 0.04);
                    float a2 = roughness * roughness;
                    float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
                    float D = a2 / (3.14159265 * d * d);
                    float Vis = 1.0 / (NdotH * NdotH * (1.0 - a2) + a2);
                    float sunSpec = D * Vis * _DirectionalLightBoost * saturate(NdotL);
                    sunSpec = min(sunSpec, 50.0);
                    skyColor.rgb += sunSpec * lightCol * shadow * sunFade;
                    #endif

                    float fresnel = CalculateFresnel(view, mappedNormal);
                    col = lerp(col, skyColor.rgb, fresnel * _Specular);
                }

                // Transparency / refraction with chromatic aberration
                #if defined(ENABLE_TRANSPARENCY)
                {
                    float2 refrOffset = _RefractionStrength * mappedNormal.xz;
                    refrOffset.y *= _ScreenSize.z * abs(_ScreenSize.w);
                    float2 refractedScreenUV = (i.positionNDC.xy + refrOffset) / i.positionNDC.w;

                    // Use HDRP depth sampling
                    float deviceDepth = SampleCameraDepth(refractedScreenUV);
                    float refractedSceneZ = LinearEyeDepth(deviceDepth, _ZBufferParams);
                    float refractedDepthDiff = refractedSceneZ - abs(i.pixelZ);
                    refrOffset *= saturate(refractedDepthDiff);
                    refractedScreenUV = (i.positionNDC.xy + refrOffset) / i.positionNDC.w;

                    // Chromatic aberration — clamp UVs to prevent OOB GPU reads
                    float2 rcoord = reflect(view, mappedNormal).xz;
                    float2 pixelCoord = refractedScreenUV * _ScreenSize.xy;
                    float2 offsetR = rcoord * -_AberrationAmount * _ScreenSize.xy;
                    float2 offsetB = rcoord * _AberrationAmount * _ScreenSize.xy;
                    float2 coordR = clamp(pixelCoord + offsetR, float2(1, 1), _ScreenSize.xy - float2(1, 1));
                    float2 coordG = clamp(pixelCoord, float2(1, 1), _ScreenSize.xy - float2(1, 1));
                    float2 coordB = clamp(pixelCoord + offsetB, float2(1, 1), _ScreenSize.xy - float2(1, 1));
                    half3 sceneColour;
                    sceneColour.r = LOAD_TEXTURE2D_X(_CameraColorTexture, uint2(coordR)).r;
                    sceneColour.g = LOAD_TEXTURE2D_X(_CameraColorTexture, uint2(coordG)).g;
                    sceneColour.b = LOAD_TEXTURE2D_X(_CameraColorTexture, uint2(coordB)).b;

                    // Depth fog
                    float depthFogDistance = max(refractedSceneZ - abs(i.pixelZ), 0.0);
                    half3 fogAlpha = half3(
                        1.0 - exp(-_DepthFogDensity.x * depthFogDistance),
                        1.0 - exp(-_DepthFogDensity.y * depthFogDistance),
                        1.0 - exp(-_DepthFogDensity.z * depthFogDistance));

                    col = lerp(sceneColour, col, fogAlpha);
                }
                #endif

                // Foam from analytic Jacobian (computed in vertex shader)
                #if defined(ENABLE_FOAM)
                {
                    half foam = saturate(i.foamValue * _FoamIntensity * 3.0);
                    foam = pow(foam, 0.5);
                    col = lerp(col, _FoamWhiteColor.rgb, foam * _FoamWhiteColor.a);
                }
                #endif

                return half4(col, 1.0);
            }

            ENDHLSL
        }
    }

    Fallback "HDRP/Lit"
}
