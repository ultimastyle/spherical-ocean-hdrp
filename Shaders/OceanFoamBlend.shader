// Hidden/OceanFoamBlend.shader
// Ping-pong foam accumulation: blends Jacobian-triggered foam with decaying previous frame.
// Used by OceanFoamGenerator via Graphics.Blit.

Shader "Hidden/OceanFoamBlend"
{
    Properties
    {
        _PrevFoam ("Previous Foam", 2D) = "black" {}
        _JacobianTex ("Jacobian", 2D) = "white" {}
        _FoamIntensity ("Foam Intensity", Float) = 1.5
        _FoamLifetime ("Foam Lifetime", Float) = 3.0
        _WindStretch ("Wind Stretch", Float) = 0.3
        _NoiseScale ("Noise Scale", Float) = 2.0
        _Time ("Time", Float) = 0.0
        _JacobianThreshold ("Jacobian Threshold", Float) = -0.05
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="HDRenderPipeline" }
        LOD 100
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            TEXTURE2D(_PrevFoam);
            SAMPLER(sampler_linear_clamp);
            TEXTURE2D(_JacobianTex);

            float _FoamIntensity;
            float _FoamLifetime;
            float _WindStretch;
            float _NoiseScale;
            float _Time;
            float _JacobianThreshold;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            // Simple hash-based noise for foam detail
            float Hash(float2 p)
            {
                float h = dot(p, float2(127.1, 311.7));
                return frac(sin(h) * 43758.5453);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash(i);
                float b = Hash(i + float2(1.0, 0.0));
                float c = Hash(i + float2(0.0, 1.0));
                float d = Hash(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                // Previous foam with frame-rate independent decay
                float prevFoam = SAMPLE_TEXTURE2D(_PrevFoam, sampler_linear_clamp, uv).r;

                // Decay based on lifetime: each frame, foam loses 1/lifetime fraction
                float decayFactor = exp(-1.0 / max(_FoamLifetime, 0.1));
                prevFoam *= decayFactor;

                // Jacobian-based foam generation
                float newFoam = 0.0;
                float J = SAMPLE_TEXTURE2D(_JacobianTex, sampler_linear_clamp, uv).r;
                if (J < _JacobianThreshold)
                {
                    newFoam = _FoamIntensity * saturate(_JacobianThreshold - J);
                }

                // Wind-stretched noise detail for visual variety
                float2 noiseUV = uv * _NoiseScale + float2(_Time * _WindStretch, _Time * 0.1);
                float noiseDetail = Noise(noiseUV * 8.0) * 0.5 + 0.5;

                // Combine: accumulate new foam on top of decaying old foam
                float foam = saturate(prevFoam + newFoam * noiseDetail);

                return float4(foam, foam, foam, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
