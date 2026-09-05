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
    /// Default 3-cascade setup for tropical island water:
    ///   Far: gentle ocean swells
    ///   Mid: moderate wind waves
    ///   Near: fine ripples and surface detail
    /// </summary>
    public static CascadeConfig[] DefaultCascades()
    {
        return new CascadeConfig[]
        {
            // Far: gentle ocean swells — broad, calm motion
            new CascadeConfig
            {
                name = "Ocean Swells",
                resolution = 128,
                patchSize = 600f,
                windSpeed = 8f,
                windDirection = new Vector2(1f, 0.3f),
                windAlignment = 0.8f,
                spectrumType = OceanWaveSpectrum.SpectrumType.PiersonMoskowitz,
                spectrumScale = 0.012f,
                choppiness = 0.6f,
                gravity = 9.81f,
                foamThreshold = -0.2f,
                foamDecay = 0.1f,
                foamGain = 0.7f,
                distanceRange = new Vector2(300f, 2500f),
                blendIn = 0f,
                blendOut = 0.35f
            },
            // Mid: wind waves — visible chop, tropical feel
            new CascadeConfig
            {
                name = "Wind Waves",
                resolution = 256,
                patchSize = 250f,
                windSpeed = 10f,
                windDirection = new Vector2(0.8f, 0.5f),
                windAlignment = 1.0f,
                spectrumType = OceanWaveSpectrum.SpectrumType.JONSWAP,
                spectrumScale = 0.01f,
                choppiness = 1.2f,
                gravity = 9.81f,
                foamThreshold = -0.1f,
                foamDecay = 0.15f,
                foamGain = 0.9f,
                distanceRange = new Vector2(80f, 600f),
                blendIn = 0.3f,
                blendOut = 0.7f
            },
            // Near: fine detail — ripples, wave crests near shore
            new CascadeConfig
            {
                name = "Detail Ripples",
                resolution = 256,
                patchSize = 100f,
                windSpeed = 6f,
                windDirection = new Vector2(0.6f, 0.8f),
                windAlignment = 1.2f,
                spectrumType = OceanWaveSpectrum.SpectrumType.JONSWAP,
                spectrumScale = 0.008f,
                choppiness = 1.8f,
                gravity = 9.81f,
                foamThreshold = -0.05f,
                foamDecay = 0.2f,
                foamGain = 1.0f,
                distanceRange = new Vector2(0f, 180f),
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
