using System;
using UnityEngine;

/// <summary>
/// Manages multiple FFT wave simulation cascades at different scales.
/// This is how production ocean systems achieve detail at all distances:
///
///   Cascade 0 (far):   Large swells, low resolution FFT, large patch
///   Cascade 1 (mid):   Medium wind waves, medium resolution
///   Cascade 2 (near):  Fine detail + ripples, high resolution, small patch
///
/// Each cascade has its own FFTWaveSimulation with tuned parameters.
/// The cascades are blended in the HDRP shader by distance to camera.
/// </summary>
[CreateAssetMenu(fileName = "WaveCascades", menuName = "ProceduralPlanet/Wave Cascades")]
public class WaveCascadeData : ScriptableObject
{
    [Serializable]
    public class CascadeConfig
    {
        [Tooltip("Name for debugging.")]
        public string name = "Cascade";

        [Header("FFT Settings")]
        [Range(64, 512)] public int resolution = 256;
        public float patchSize = 200f;

        [Header("Wind / Spectrum")]
        [Range(0f, 40f)] public float windSpeed = 12f;
        public Vector2 windDirection = new Vector2(1f, 0.5f);
        [Range(0f, 2f)] public float windAlignment = 1f;
        public OceanWaveSpectrum.SpectrumType spectrumType = OceanWaveSpectrum.SpectrumType.JONSWAP;
        [Range(0.001f, 0.1f)] public float spectrumScale = 0.01f;
        [Range(0f, 5f)] public float choppiness = 1.5f;
        public float gravity = 9.81f;

        [Header("Foam")]
        [Range(-1f, 1f)] public float foamThreshold = -0.1f;
        [Range(0f, 2f)] public float foamDecay = 0.15f;
        [Range(0f, 5f)] public float foamGain = 0.9f;

        [Header("Blending")]
        [Tooltip("Distance range where this cascade is visible. x = start, y = end.")]
        public Vector2 distanceRange = new Vector2(0f, 1000f);
        [Tooltip("Blend weight at the start of this cascade's range.")]
        [Range(0f, 1f)] public float blendIn = 0f;
        [Tooltip("Blend weight at the end of this cascade's range.")]
        [Range(0f, 1f)] public float blendOut = 1f;
    }

    [Tooltip("Cascade configurations. Typically 2-4 cascades.")]
    public CascadeConfig[] cascades = DefaultCascades();

    /// <summary>
    /// Default 3-cascade setup: far swells + mid wind waves + near detail.
    /// </summary>
    public static CascadeConfig[] DefaultCascades()
    {
        return new CascadeConfig[]
        {
            // Far: large swells
            new CascadeConfig
            {
                name = "Far Swells",
                resolution = 128,
                patchSize = 500f,
                windSpeed = 15f,
                spectrumScale = 0.015f,
                choppiness = 0.8f,
                distanceRange = new Vector2(200f, 2000f),
                blendIn = 0f,
                blendOut = 0.3f
            },
            // Mid: wind waves
            new CascadeConfig
            {
                name = "Wind Waves",
                resolution = 256,
                patchSize = 200f,
                windSpeed = 12f,
                spectrumScale = 0.01f,
                choppiness = 1.5f,
                distanceRange = new Vector2(50f, 500f),
                blendIn = 0.3f,
                blendOut = 0.7f
            },
            // Near: detail + ripples
            new CascadeConfig
            {
                name = "Detail Ripples",
                resolution = 256,
                patchSize = 80f,
                windSpeed = 8f,
                spectrumScale = 0.008f,
                choppiness = 2f,
                distanceRange = new Vector2(0f, 150f),
                blendIn = 0.7f,
                blendOut = 1f
            }
        };
    }

    /// <summary>
    /// Get the cascade configuration at a given distance from the camera.
    /// Returns null if outside all cascade ranges.
    /// </summary>
    public CascadeConfig GetCascadeAtDistance(float distance)
    {
        if (cascades == null) return null;

        foreach (var cascade in cascades)
        {
            if (distance >= cascade.distanceRange.x && distance <= cascade.distanceRange.y)
                return cascade;
        }

        // Return closest cascade
        float minDist = float.MaxValue;
        CascadeConfig closest = null;
        foreach (var cascade in cascades)
        {
            float d = Mathf.Min(
                Mathf.Abs(distance - cascade.distanceRange.x),
                Mathf.Abs(distance - cascade.distanceRange.y)
            );
            if (d < minDist) { minDist = d; closest = cascade; }
        }
        return closest;
    }

    /// <summary>
    /// Get blend weights for all cascades at a given distance.
    /// Useful for shader blending.
    /// </summary>
    public void GetBlendWeights(float distance, float[] weights)
    {
        if (cascades == null || weights == null) return;

        int count = Mathf.Min(cascades.Length, weights.Length);
        for (int i = 0; i < count; i++)
        {
            var c = cascades[i];
            float t = Mathf.InverseLerp(c.distanceRange.x, c.distanceRange.y, distance);
            weights[i] = Mathf.Lerp(c.blendIn, c.blendOut, t);

            // Smooth blend at edges
            float edgeFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(c.distanceRange.x, c.distanceRange.x + 20f, distance))
                           * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(c.distanceRange.y, c.distanceRange.y - 20f, distance));
            weights[i] *= edgeFade;
        }
    }
}
