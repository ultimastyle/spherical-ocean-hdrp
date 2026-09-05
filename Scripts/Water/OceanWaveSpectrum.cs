using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Physically-based ocean wave spectrum generator.
/// Supports Phillips, JONSWAP, and Pierson-Moskowitz spectra.
/// Used by FFTWaveSimulation to generate initial frequency-domain data.
///
/// References:
///   - Tessendorf (2001) "Simulating Ocean Water"
///   - Phillips (1958) "The Input of Gravity Waves to the Sea"
///   - Hasselmann et al. (1973) JONSWAP
///   - Pierson & Moskowitz (1964)
/// </summary>
public static class OceanWaveSpectrum
{
    public enum SpectrumType
    {
        Phillips,
        JONSWAP,
        PiersonMoskowitz
    }

    /// <summary>
    /// Sample the wave spectrum at a given frequency wavevector (kx, ky).
    /// Returns the energy density for that frequency component.
    /// </summary>
    public static float Sample(float2 k, float windSpeed, float2 windDir, float gravity, SpectrumType type)
    {
        float kLen = math.length(k);
        if (kLen < 1e-7f) return 0f;

        return type switch
        {
            SpectrumType.Phillips => Phillips(k, kLen, windSpeed, windDir, gravity),
            SpectrumType.JONSWAP => JONSWAP(k, kLen, windSpeed, windDir, gravity),
            SpectrumType.PiersonMoskowitz => PiersonMoskowitz(kLen, windSpeed, gravity),
            _ => 0f
        };
    }

    /// <summary>
    /// Phillips spectrum: P(k) = A * exp(-1 / (kLen^2 * L^2)) * k^4 * exp(-kLen^2 * l^2)
    /// where L = V^2/g (Phillips scale), l = wind fetch length.
    /// Good for storm-driven seas.
    /// </summary>
    private static float Phillips(float2 k, float kLen, float windSpeed, float2 windDir, float g)
    {
        float L = (windSpeed * windSpeed) / g;           // Phillips length scale
        float l = L * 0.1f;                               // small-scale cutoff

        // Directional alignment: cos^2 between wave direction and wind
        float2 kNorm = k / kLen;
        float cosTheta = math.dot(kNorm, windDir);
        float directional = cosTheta > 0f ? cosTheta * cosTheta : 0f;

        float k2 = kLen * kLen;
        float k4 = k2 * k2;

        float A = windSpeed * windSpeed;  // scaling factor
        float phillips = A * math.exp(-1f / (k2 * L * L)) * directional / k4;
        float damp = math.exp(-k2 * l * l);

        return math.max(0f, phillips * damp);
    }

    /// <summary>
    /// JONSWAP spectrum: Modified Pierson-Moskowitz with peak enhancement.
    /// P(w) = alpha * g^2 * w^-5 * exp(-5/4 * (w0/w)^4) * gamma^r
    /// where r = exp(-(w-w0)^2 / (2*sigma^2*w0^2))
    /// </summary>
    private static float JONSWAP(float2 k, float kLen, float windSpeed, float2 windDir, float g)
    {
        // Pierson-Moskowitz base
        float pm = PiersonMoskowitz(kLen, windSpeed, g);

        // JONSWAP peak enhancement
        float alpha = 0.0081f;      // fetch-limited constant
        float gamma = 3.3f;         // peak enhancement factor
        float sigma = 0.07f;        // spectral width parameter

        // Dominant frequency from wind
        float w0 = g * 0.87f / Mathf.Max(1f, windSpeed);

        // Convert k to angular frequency: w^2 = g * k (deep water dispersion)
        float w = math.sqrt(g * kLen);

        float r = math.exp(-math.pow(w - w0, 2f) / (2f * sigma * sigma * w0 * w0));
        float peakBoost = math.pow(gamma, r);

        // Directional spreading (cos^2)
        float2 kNorm = k / kLen;
        float cosTheta = math.dot(kNorm, windDir);
        float directional = cosTheta > 0f ? cosTheta * cosTheta : 1f / (2f * math.PI);

        return pm * peakBoost * directional * alpha;
    }

    /// <summary>
    /// Pierson-Moskowitz spectrum: fully-developed sea.
    /// P(w) = (alpha * g^2) / w^5 * exp(-5/4 * (w0/w)^4)
    /// </summary>
    private static float PiersonMoskowitz(float kLen, float windSpeed, float g)
    {
        float alpha = 0.0081f;
        float w0 = g * 0.87f / Mathf.Max(1f, windSpeed);
        float w = math.sqrt(g * kLen);

        if (w < 1e-6f) return 0f;

        float w4 = w * w * w * w;
        float w04 = w0 * w0 * w0 * w0;

        return (alpha * g * g) / (w * w4) * math.exp(-1.25f * w04 / w4);
    }

    /// <summary>
    /// Deep-water dispersion relation: omega = sqrt(g * |k|)
    /// Returns angular frequency for a given wavenumber magnitude.
    /// </summary>
    public static float DispersionFrequency(float kLen, float gravity)
    {
        return math.sqrt(gravity * kLen);
    }

    /// <summary>
    /// Group velocity for deep water waves: vg = 0.5 * omega / k
    /// </summary>
    public static float2 GroupVelocity(float2 k, float gravity)
    {
        float kLen = math.length(k);
        if (kLen < 1e-7f) return float2.zero;
        float w = DispersionFrequency(kLen, gravity);
        return 0.5f * (w / kLen) * (k / kLen);
    }

    /// <summary>
    /// Sample displacement + normal for a Gerstner wave at world position.
    /// Used for analytical wave overlay on top of FFT.
    /// </summary>
    public static void SampleGerstnerWave(
        float2 pos, float time, float amplitude, float2 direction,
        float steepness, float wavelength,
        out float3 displacement, out float3 normal)
    {
        float k = 2f * math.PI / wavelength;
        float c = math.sqrt(9.81f / k);        // phase speed (deep water)
        float f = k * (math.dot(direction, pos) - c * time);
        float a = amplitude / k;               // amplitude in displacement units

        float sinF = math.sin(f);
        float cosF = math.cos(f);

        displacement = new float3(
            steepness * a * direction.x * cosF,
            a * sinF,
            steepness * a * direction.y * cosF
        );

        normal = new float3(
            -direction.x * steepness * cosF * cosF,  // simplified
            1f - steepness * sinF,
            -direction.y * steepness * cosF * cosF
        );
    }

    /// <summary>
    /// Compute the Phillips-limited maximum wave height to prevent wave breaking.
    /// Steepness = k * A <= steepnessLimit (typically 0.21 for deep water).
    /// </summary>
    public static float MaxAmplitudeForWavelength(float wavelength, float steepnessLimit = 0.21f)
    {
        float k = 2f * math.PI / wavelength;
        return steepnessLimit / k;
    }
}
