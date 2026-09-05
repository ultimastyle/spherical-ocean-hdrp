# OpenOceanPhysics — Full Code Review & Feedback

**Repo:** https://github.com/ultimastyle/OpenOceanPhysics
**Created:** 2026-09-05
**Purpose:** Open-source FFT ocean wave physics for Unity spherical planets (HDRP)

---

## Architecture Overview

```
OceanRenderer (orchestrator)
├── FFTWaveSimulation (GPU compute)
│   ├── OceanWaveSpectrum (Phillips/JONSWAP/Pierson-Moskowitz)
│   └── OceanWaveCompute.compute (5 kernels)
├── SphericalGerstnerWaves (Burst jobs)
├── OceanFoamGenerator (Jacobian + shore)
└── WaveCascadeData (ScriptableObject)
```

**Data flow:**
1. `FFTWaveSimulation` runs spectrum update → FFT butterfly → displacement + Jacobian textures (GPU)
2. `SphericalGerstnerWaves` evaluates 10 wave sets per vertex via Burst (CPU)
3. `OceanFoamGenerator` accumulates foam from Jacobian + shore proximity
4. `OceanRenderer` deforms sphere mesh via Gerstner, binds all textures to shader globals
5. HDRP shader samples `_OceanDisplacementTex`, `_OceanFoamTex`, `_OceanJacobianTex`

---

## Files

| File | Lines | Role |
|------|-------|------|
| `OceanWaveSpectrum.cs` | 176 | Spectrum models (Phillips, JONSWAP, PM), Gerstner sampling, dispersion |
| `FFTWaveSimulation.cs` | 471 | GPU FFT driver, buffer management, texture binding |
| `SphericalGerstnerWaves.cs` | 342 | Burst-jobbed Gerstner waves on sphere, tangent frame, numerical normals |
| `OceanFoamGenerator.cs` | 234 | Foam from Jacobian + shore proximity, ping-pong accumulation |
| `WaveCascadeData.cs` | 152 | ScriptableObject for multi-scale cascades, blend weights |
| `OceanRenderer.cs` | 260 | Orchestrator, sphere mesh gen, mesh deformation, shader binding |
| `OceanWaveCompute.compute` | 242 | 5 GPU kernels: UpdateSpectrum, Butterfly, Inverse, Jacobian, Foam |

**Total:** 1,877 lines

---

## Full Source Code

---

### 1. OceanWaveSpectrum.cs (176 lines)

```csharp
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
```

---

### 2. FFTWaveSimulation.cs (471 lines)

```csharp
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// GPU-accelerated FFT ocean wave simulation.
/// Generates a displacement field + Jacobian foam map from a wind-driven spectrum.
///
/// Pipeline each frame:
///   1. UpdateSpectrum() — time-evolve the frequency-domain spectrum via Dispersion.
///   2. FFT (Butterfly) — ping-pong horizontal + vertical passes on the compute shader.
///   3. InverseTransform() — read back displacement (XZ), height (Y), Jacobian (foam).
///
/// Requires: OceanWaveCompute.compute in the project.
/// Attach to any GameObject; call GetDisplacementBuffer()/GetNormalBuffer() to feed shaders.
/// </summary>
public class FFTWaveSimulation : MonoBehaviour
{
    [Header("Grid")]
    [Tooltip("FFT resolution (N x N). Must be power of 2.")]
    [Range(64, 512)] public int resolution = 256;

    [Tooltip("Physical size of the simulation patch in world units.")]
    public float patchSize = 200f;

    [Header("Wind")]
    [Range(0f, 40f)] public float windSpeed = 12f;
    [Tooltip("Normalized wind direction (x,z).")]
    public Vector2 windDirection = new Vector2(1f, 0.5f);
    [Range(0f, 2f)] public float windAlignment = 1f;

    [Header("Spectrum")]
    public OceanWaveSpectrum.SpectrumType spectrumType = OceanWaveSpectrum.SpectrumType.JONSWAP;
    [Range(0.001f, 0.1f)] public float spectrumScale = 0.01f;
    [Tooltip("Small-scale dampening — kills high-frequency noise.")]
    [Range(0f, 5f)] public float choppiness = 1.5f;
    [Tooltip("Gravity for dispersion relation.")]
    public float gravity = 9.81f;

    [Header("Foam")]
    [Tooltip("Jacobian threshold for foam generation (lower = more foam).")]
    [Range(-1f, 1f)] public float foamThreshold = -0.1f;
    [Tooltip("Foam decay speed (how fast foam fades).")]
    [Range(0f, 2f)] public float foamDecay = 0.15f;
    [Tooltip("Foam accumulation rate.")]
    [Range(0f, 5f)] public float foamGain = 0.9f;

    [Header("Performance")]
    [Tooltip("Simulation steps per second. 0 = every frame.")]
    public float simRate = 0f;
    [Tooltip("Sync with project time instead of wall-clock. Good for replays.")]
    public bool useProjectTime = true;

    [Header("Compute Shader")]
    public ComputeShader fftCompute;

    // --- Runtime buffers (read by shaders) ---
    private RenderTexture _dispTex;      // RGB: displacement (x,z), height (y)
    private RenderTexture _jacobianTex;  // R: J determinant for foam
    private RenderTexture _foamAccumTex; // persistent foam accumulation
    private ComputeBuffer _spectrumBuffer;  // complex spectrum per grid point
    private ComputeBuffer _pingPongA;
    private ComputeBuffer _pingPongB;

    // Butterfly lookup tables
    private ComputeBuffer _twiddleBuffer;
    private ComputeBuffer _bitReverseBuffer;

    private int _kernelSpectrumUpdate;
    private int _kernelButterfly;
    private int _kernelInverseTransform;
    private int _kernelJacobian;
    private int _kernelFoamUpdate;

    private float _time;
    private float _simTimer;
    private bool _allocated;
    private int _logN;

    // Shader property IDs (cached)
    private static readonly int ID_DispTex = Shader.PropertyToID("_OceanDisplacement");
    private static readonly int ID_JacobianTex = Shader.PropertyToID("_OceanJacobian");
    private static readonly int ID_FoamTex = Shader.PropertyToID("_OceanFoam");
    private static readonly int ID_PatchSize = Shader.PropertyToID("_OceanPatchSize");
    private static readonly int ID_Resolution = Shader.PropertyToID("_OceanResolution");
    private static readonly int ID_Time = Shader.PropertyToID("_OceanTime");
    private static readonly int ID_WindSpeed = Shader.PropertyToID("_OceanWindSpeed");
    private static readonly int ID_WindDir = Shader.PropertyToID("_OceanWindDir");
    private static readonly int ID_SpectrumScale = Shader.PropertyToID("_OceanSpectrumScale");
    private static readonly int ID_Choppiness = Shader.PropertyToID("_OceanChoppiness");
    private static readonly int ID_Gravity = Shader.PropertyToID("_OceanGravity");
    private static readonly int ID_FoamThreshold = Shader.PropertyToID("_OceanFoamThreshold");
    private static readonly int ID_FoamDecay = Shader.PropertyToID("_OceanFoamDecay");
    private static readonly int ID_FoamGain = Shader.PropertyToID("_OceanFoamGain");
    private static readonly int ID_Spectrum = Shader.PropertyToID("_Spectrum");
    private static readonly int ID_PingPongA = Shader.PropertyToID("_PingPongA");
    private static readonly int ID_PingPongB = Shader.PropertyToID("_PingPongB");
    private static readonly int ID_Twiddle = Shader.PropertyToID("_Twiddle");
    private static readonly int ID_BitReverse = Shader.PropertyToID("_BitReverse");
    private static readonly int ID_DispOut = Shader.PropertyToID("_DispOut");
    private static readonly int ID_JacOut = Shader.PropertyToID("_JacOut");

    // --- Public accessors for HDRP shaders ---
    public RenderTexture DisplacementTexture => _dispTex;
    public RenderTexture JacobianTexture => _jacobianTex;
    public RenderTexture FoamTexture => _foamAccumTex;
    public float PatchSize => patchSize;
    public int Resolution => resolution;

    private void Awake()
    {
        windDirection = windDirection.normalized;
    }

    private void Start()
    {
        Allocate();
        InitSpectrum();
        InitButterfly();
    }

    private void Update()
    {
        if (!_allocated) return;

        // Sim rate limiting
        _simTimer -= Time.unscaledDeltaTime;
        if (simRate > 0f && _simTimer > 0f) return;
        _simTimer = 1f / Mathf.Max(0.1f, simRate);

        float dt = useProjectTime ? Time.time : Time.unscaledTime;
        _time = dt;

        UpdateSpectrum();
        RunFFT();
        ComputeJacobian();
        UpdateFoam();
    }

    private void OnDestroy()
    {
        Release();
    }

    private void OnValidate()
    {
        if (windDirection.sqrMagnitude > 0.001f)
            windDirection = ((Vector2)windDirection).normalized;
        else
            windDirection = Vector2.right;

        if (_allocated)
        {
            Release();
            Start();
        }
    }

    // =========================================================================
    //  Allocation
    // =========================================================================
    private void Allocate()
    {
        if (_allocated) Release();

        resolution = Mathf.NextPowerOfTwo(Mathf.Clamp(resolution, 64, 512));
        _logN = (int)math.log2(resolution);
        int count = resolution * resolution;

        // Create ping-pong buffers for FFT butterfly
        // Each element: float2 (real, imaginary)
        _pingPongA = new ComputeBuffer(count, sizeof(float) * 2);
        _pingPongB = new ComputeBuffer(count, sizeof(float) * 2);

        // Spectrum buffer: initial frequency-domain data
        _spectrumBuffer = new ComputeBuffer(count, sizeof(float) * 4); // float4(kx, ky, omega, amplitude)

        // Twiddle factors for butterfly
        _twiddleBuffer = new ComputeBuffer(count, sizeof(float) * 2);

        // Bit-reversal table
        _bitReverseBuffer = new ComputeBuffer(count, sizeof(int));

        // Output textures
        _dispTex = CreateRT(resolution, resolution, RenderTextureFormat.ARGBFloat, "OceanDisplacement");
        _jacobianTex = CreateRT(resolution, resolution, RenderTextureFormat.RFloat, "OceanJacobian");
        _foamAccumTex = CreateRT(resolution, resolution, RenderTextureFormat.RFloat, "OceanFoam");
        _foamAccumTex.filterMode = FilterMode.Bilinear;

        // Find kernels
        if (fftCompute == null)
        {
            fftCompute = Resources.Load<ComputeShader>("OceanWaveCompute");
            if (fftCompute == null)
            {
                Debug.LogError("[FFTWaveSimulation] OceanWaveCompute.compute not found. Place it in Resources/ or assign it.");
                _allocated = false;
                return;
            }
        }

        _kernelSpectrumUpdate = fftCompute.FindKernel("UpdateSpectrum");
        _kernelButterfly = fftCompute.FindKernel("ButterflyPass");
        _kernelInverseTransform = fftCompute.FindKernel("InverseTransform");
        _kernelJacobian = fftCompute.FindKernel("ComputeJacobian");
        _kernelFoamUpdate = fftCompute.FindKernel("UpdateFoam");

        _allocated = true;
    }

    private void Release()
    {
        _pingPongA?.Dispose(); _pingPongA = null;
        _pingPongB?.Dispose(); _pingPongB = null;
        _spectrumBuffer?.Dispose(); _spectrumBuffer = null;
        _twiddleBuffer?.Dispose(); _twiddleBuffer = null;
        _bitReverseBuffer?.Dispose(); _bitReverseBuffer = null;

        if (_dispTex != null) { _dispTex.Release(); Destroy(_dispTex); _dispTex = null; }
        if (_jacobianTex != null) { _jacobianTex.Release(); Destroy(_jacobianTex); _jacobianTex = null; }
        if (_foamAccumTex != null) { _foamAccumTex.Release(); Destroy(_foamAccumTex); _foamAccumTex = null; }

        _allocated = false;
    }

    private static RenderTexture CreateRT(int w, int h, RenderTextureFormat fmt, string name)
    {
        var rt = new RenderTexture(w, h, 0, fmt)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat,
            enableRandomWrite = true
        };
        rt.Create();
        return rt;
    }

    // =========================================================================
    //  Spectrum initialization
    // =========================================================================
    private void InitSpectrum()
    {
        if (fftCompute == null || _spectrumBuffer == null) return;

        float2 wDir = new float2(windDirection.x, windDirection.y);

        // Generate spectrum on CPU, upload once
        var spectrumData = new Vector4[resolution * resolution];
        float scale = patchSize / resolution;

        for (int iy = 0; iy < resolution; iy++)
        for (int ix = 0; ix < resolution; ix++)
        {
            // Wavevector (centered, wrapped to [-N/2, N/2-1])
            int kx = ix < resolution / 2 ? ix : ix - resolution;
            int ky = iy < resolution / 2 ? iy : iy - resolution;
            float2 k = new float2(kx, ky) * (2f * math.PI / patchSize);

            float energy = OceanWaveSpectrum.Sample(k, windSpeed, wDir, gravity, spectrumType);
            energy *= spectrumScale;

            // Random phase
            float phase = UnityEngine.Random.Range(0f, 2f * math.PI);

            spectrumData[iy * resolution + ix] = new Vector4(k.x, k.y, energy, phase);
        }

        _spectrumBuffer.SetData(spectrumData);
    }

    // =========================================================================
    //  Butterfly / FFT
    // =========================================================================
    private void InitButterfly()
    {
        if (fftCompute == null || _twiddleBuffer == null) return;

        var twiddle = new Vector2[resolution * resolution];
        var bitRev = new int[resolution];

        for (int stage = 0; stage < _logN; stage++)
        {
            int halfSize = 1 << stage;
            int fullSize = halfSize * 2;
            float angle = -math.PI / halfSize;

            for (int k = 0; k < halfSize; k++)
            {
                float wAngle = angle * k;
                twiddle[stage * resolution + k] = new Vector2(math.cos(wAngle), math.sin(wAngle));
            }
        }

        // Bit reversal
        for (int i = 0; i < resolution; i++)
        {
            int rev = 0;
            int tmp = i;
            for (int j = 0; j < _logN; j++)
            {
                rev = (rev << 1) | (tmp & 1);
                tmp >>= 1;
            }
            bitRev[i] = rev;
        }

        _twiddleBuffer.SetData(twiddle);

        // Expand bit-reverse for full 2D (resolution * resolution entries)
        var bitRev2D = new int[resolution * resolution];
        for (int iy = 0; iy < resolution; iy++)
        for (int ix = 0; ix < resolution; ix++)
        {
            bitRev2D[iy * resolution + ix] = bitRev[iy] * resolution + bitRev[ix];
        }
        _bitReverseBuffer.SetData(bitRev2D);
    }

    private void UpdateSpectrum()
    {
        if (fftCompute == null) return;

        fftCompute.SetInt(ID_Resolution, resolution);
        fftCompute.SetFloat(ID_Time, _time);
        fftCompute.SetFloat(ID_WindSpeed, windSpeed);
        fftCompute.SetVector(ID_WindDir, new Vector4(windDirection.x, 0, windDirection.y, 0));
        fftCompute.SetFloat(ID_SpectrumScale, spectrumScale);
        fftCompute.SetFloat(ID_Choppiness, choppiness);
        fftCompute.SetFloat(ID_Gravity, gravity);
        fftCompute.SetFloat(ID_PatchSize, patchSize);

        fftCompute.SetBuffer(_kernelSpectrumUpdate, ID_Spectrum, _spectrumBuffer);
        fftCompute.SetBuffer(_kernelSpectrumUpdate, ID_PingPongA, _pingPongA);

        int groupsX = Mathf.CeilToInt(resolution / 8f);
        int groupsY = Mathf.CeilToInt(resolution / 8f);
        fftCompute.Dispatch(_kernelSpectrumUpdate, groupsX, groupsY, 1);
    }

    private void RunFFT()
    {
        if (fftCompute == null) return;

        fftCompute.SetInt(ID_Resolution, resolution);
        fftCompute.SetBuffer(_kernelButterfly, ID_Twiddle, _twiddleBuffer);

        int groupsX = Mathf.CeilToInt(resolution / 8f);
        int groupsY = Mathf.CeilToInt(resolution / 8f);

        // Butterfly passes (log2(N) stages)
        for (int stage = 0; stage < _logN; stage++)
        {
            bool horizontal = stage % 2 == 0;
            ComputeBuffer ping = horizontal ? _pingPongA : _pingPongB;
            ComputeBuffer pong = horizontal ? _pingPongB : _pingPongA;

            fftCompute.SetInt("_Stage", stage);
            fftCompute.SetBool("_Horizontal", horizontal);
            fftCompute.SetBuffer(_kernelButterfly, ID_PingPongA, ping);
            fftCompute.SetBuffer(_kernelButterfly, ID_PingPongB, pong);

            fftCompute.Dispatch(_kernelButterfly, groupsX, groupsY, 1);
        }

        // Final inverse transform: read from whichever buffer was last written
        ComputeBuffer resultBuffer = (_logN % 2 == 0) ? _pingPongA : _pingPongB;

        fftCompute.SetInt(ID_Resolution, resolution);
        fftCompute.SetFloat(ID_Choppiness, choppiness);
        fftCompute.SetBuffer(_kernelInverseTransform, ID_PingPongA, resultBuffer);
        fftCompute.SetTexture(_kernelInverseTransform, ID_DispOut, _dispTex);

        fftCompute.Dispatch(_kernelInverseTransform, groupsX, groupsY, 1);
    }

    private void ComputeJacobian()
    {
        if (fftCompute == null) return;

        fftCompute.SetInt(ID_Resolution, resolution);
        fftCompute.SetFloat(ID_Choppiness, choppiness);

        fftCompute.SetTexture(_kernelJacobian, ID_DispOut, _dispTex);
        fftCompute.SetTexture(_kernelJacobian, ID_JacOut, _jacobianTex);

        int groupsX = Mathf.CeilToInt(resolution / 8f);
        int groupsY = Mathf.CeilToInt(resolution / 8f);
        fftCompute.Dispatch(_kernelJacobian, groupsX, groupsY, 1);
    }

    private void UpdateFoam()
    {
        if (fftCompute == null) return;

        fftCompute.SetFloat(ID_FoamThreshold, foamThreshold);
        fftCompute.SetFloat(ID_FoamDecay, foamDecay);
        fftCompute.SetFloat(ID_FoamGain, foamGain);

        fftCompute.SetTexture(_kernelFoamUpdate, ID_JacOut, _jacobianTex);
        fftCompute.SetTexture(_kernelFoamUpdate, ID_FoamTex, _foamAccumTex);

        int groupsX = Mathf.CeilToInt(resolution / 8f);
        int groupsY = Mathf.CeilToInt(resolution / 8f);
        fftCompute.Dispatch(_kernelFoamUpdate, groupsX, groupsY, 1);
    }

    // =========================================================================
    //  Public API for HDRP shaders
    // =========================================================================

    /// <summary>
    /// Bind all ocean textures to a MaterialPropertyBlock for HDRP shaders.
    /// Usage: matProp.SetOceanTextures(waveSim); renderer.SetPropertyBlock(matProp);
    /// </summary>
    public void BindToMaterialPropertyBlock(MaterialPropertyBlock block)
    {
        if (!_allocated) return;
        block.SetTexture(ID_DispTex, _dispTex);
        block.SetTexture(ID_JacobianTex, _jacobianTex);
        block.SetTexture(ID_FoamTex, _foamAccumTex);
        block.SetFloat(ID_PatchSize, patchSize);
        block.SetInt(ID_Resolution, resolution);
    }

    /// <summary>
    /// Bind ocean textures to global shader properties (available to ALL shaders).
    /// Call once after allocation if you want global access.
    /// </summary>
    public void BindGlobal()
    {
        if (!_allocated) return;
        Shader.SetGlobalTexture(ID_DispTex, _dispTex);
        Shader.SetGlobalTexture(ID_JacobianTex, _jacobianTex);
        Shader.SetGlobalTexture(ID_FoamTex, _foamAccumTex);
        Shader.SetGlobalFloat(ID_PatchSize, patchSize);
        Shader.SetGlobalInt(ID_Resolution, resolution);
    }

    /// <summary>
    /// Sample displacement at a world-space point (bilinear from GPU texture).
    /// Returns (displacement.x, displacement.y as height, displacement.z).
    /// </summary>
    public Vector3 SampleDisplacement(Vector3 worldPos, Transform oceanOrigin)
    {
        if (!_allocated || _dispTex == null) return Vector3.zero;

        Vector3 local = worldPos - oceanOrigin.position;
        float2 uv = new float2(
            (local.x / patchSize) + 0.5f,
            (local.z / patchSize) + 0.5f
        );

        // Read back single texel (GPU readback — for CPU queries only, not per-frame)
        var tex = new Texture2D(resolution, resolution, TextureFormat.RGBAFloat, false);
        RenderTexture.active = _dispTex;
        tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        int ix = Mathf.FloorToInt(uv.x * resolution) % resolution;
        int iy = Mathf.FloorToInt(uv.y * resolution) % resolution;
        if (ix < 0) ix += resolution;
        if (iy < 0) iy += resolution;

        Color c = tex.GetPixel(ix, iy);
        Destroy(tex);

        return new Vector3(c.r, c.g, c.b); // x,z displacement; y = height
    }
}
```

---

### 3. SphericalGerstnerWaves.cs (342 lines)

```csharp
using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Gerstner waves on a sphere surface.
/// Unlike flat Gerstner which displaces in (x,y,z), spherical Gerstner displaces
/// radially outward from the sphere center + tangent-plane horizontal shift.
///
/// The key insight: on a sphere, "height" is the radial distance from the center.
/// Each Gerstner wave produces:
///   - Radial displacement: the wave height (along the surface normal)
///   - Tangential displacement: the wave choppiness (along the surface plane)
///
/// Designed for 8-16 overlapping wave sets at different scales (swell, chop, ripple).
/// Runs as a Burst job for zero GC per frame.
/// </summary>
public class SphericalGerstnerWaves : MonoBehaviour
{
    [Header("Wave Sets")]
    [Tooltip("Up to 16 wave sets. Each is a Gerstner component with its own direction/amplitude/wavelength.")]
    public WaveSet[] waveSets = DefaultWaveSets();

    [Header("Planet")]
    [Tooltip("Radius of the sphere surface these waves ride on.")]
    public float sphereRadius = 417f;
    [Tooltip("Planet center (world space). If null, uses transform.position.")]
    public Transform planetCenter;

    [Header("Runtime")]
    [Tooltip("If true, wave time uses Time.time (syncs with sim). If false, uses unscaledTime.")]
    public bool useSimTime = true;

    private NativeArray<WaveData> _waveData;
    private bool _allocated;
    private float _time;

    /// <summary>
    /// Per-vertex output: displaced position + normal.
    /// </summary>
    public struct VertexResult
    {
        public float3 position;   // world-space displaced position
        public float3 normal;     // world-space surface normal
        public float2 foam;       // x = wave steepness factor, y = 0
    }

    [Serializable]
    public struct WaveSet
    {
        [Tooltip("Wave direction in world XZ (normalized at runtime).")]
        public Vector2 direction;
        [Tooltip("Wave amplitude (radial displacement).")]
        [Range(0f, 10f)] public float amplitude;
        [Tooltip("Wavelength in world units.")]
        public float wavelength;
        [Tooltip("Steepness (0 = sine wave, 1 = sharp Gerstner crest).")]
        [Range(0f, 1f)] public float steepness;
        [Tooltip("Speed multiplier (phase speed = sqrt(g*lambda / 2pi) * speed).")]
        [Range(0.1f, 3f)] public float speed;
        [Tooltip("Optional: force this wave's phase (0 = auto from dispersion).")]
        public float phaseOffset;
    }

    private struct WaveData
    {
        public float2 direction;
        public float amplitude;
        public float k;          // wavenumber = 2pi / wavelength
        public float omega;      // angular frequency = sqrt(g * k) deep water
        public float steepness;
        public float speed;
        public float phase;
    }

    // Default wave sets — mixed swell + chop for realistic ocean
    public static WaveSet[] DefaultWaveSets()
    {
        return new WaveSet[]
        {
            // Large swell
            new WaveSet { direction = new Vector2(1f, 0f), amplitude = 2.5f, wavelength = 80f, steepness = 0.4f, speed = 1f },
            new WaveSet { direction = new Vector2(0.7f, 0.7f), amplitude = 1.8f, wavelength = 60f, steepness = 0.35f, speed = 1.1f },
            // Medium wind waves
            new WaveSet { direction = new Vector2(0.9f, 0.4f), amplitude = 1.2f, wavelength = 35f, steepness = 0.5f, speed = 1.2f },
            new WaveSet { direction = new Vector2(0.6f, 0.8f), amplitude = 0.9f, wavelength = 25f, steepness = 0.45f, speed = 1.0f },
            new WaveSet { direction = new Vector2(-0.3f, 0.95f), amplitude = 0.7f, wavelength = 20f, steepness = 0.55f, speed = 0.9f },
            // Chop
            new WaveSet { direction = new Vector2(0.8f, -0.6f), amplitude = 0.5f, wavelength = 12f, steepness = 0.6f, speed = 1.3f },
            new WaveSet { direction = new Vector2(-0.5f, 0.86f), amplitude = 0.35f, wavelength = 8f, steepness = 0.5f, speed = 1.1f },
            // Ripples
            new WaveSet { direction = new Vector2(1f, 0.2f), amplitude = 0.15f, wavelength = 3f, steepness = 0.7f, speed = 1.5f },
            new WaveSet { direction = new Vector2(0.2f, 1f), amplitude = 0.1f, wavelength = 2f, steepness = 0.8f, speed = 1.8f },
            new WaveSet { direction = new Vector2(-0.7f, 0.7f), amplitude = 0.08f, wavelength = 1.5f, steepness = 0.9f, speed = 2f },
        };
    }

    private void Start()
    {
        Allocate();
    }

    private void Update()
    {
        if (!_allocated) return;
        _time = useSimTime ? Time.time : Time.unscaledTime;
    }

    private void OnDestroy()
    {
        if (_allocated) { _waveData.Dispose(); _allocated = false; }
    }

    private void OnValidate()
    {
        if (_allocated) { _waveData.Dispose(); _allocated = false; Start(); }
    }

    private void Allocate()
    {
        if (waveSets == null || waveSets.Length == 0) waveSets = DefaultWaveSets();

        int count = Mathf.Min(waveSets.Length, 16);
        _waveData = new NativeArray<WaveData>(count, Allocator.Persistent);

        for (int i = 0; i < count; i++)
        {
            var ws = waveSets[i];
            float2 dir = new float2(ws.direction.x, ws.direction.y);
            float len = math.length(dir);
            if (len > 0.001f) dir /= len;
            else dir = new float2(1f, 0f);

            float k = 2f * math.PI / Mathf.Max(0.1f, ws.wavelength);
            float g = 9.81f;
            float omega = math.sqrt(g * k);  // deep water dispersion

            _waveData[i] = new WaveData
            {
                direction = dir,
                amplitude = ws.amplitude,
                k = k,
                omega = omega * ws.speed,
                steepness = ws.steepness,
                speed = ws.speed,
                phase = ws.phaseOffset
            };
        }

        _allocated = true;
    }

    // =========================================================================
    //  Burst Jobs — evaluate all wave sets for a batch of surface points
    // =========================================================================

    [BurstCompile]
    private struct EvaluateWavesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<WaveData> waves;
        [ReadOnly] public NativeArray<float3> surfacePoints;  // base sphere surface positions
        public NativeArray<VertexResult> results;

        public float time;
        public float3 centre;

        public void Execute(int i)
        {
            float3 p = surfacePoints[i];
            float3 normal = math.normalize(p - centre);

            // Tangent frame on sphere
            float3 up = normal;
            float3 east = math.normalizesafe(math.cross(up, new float3(0, 1, 0)));
            if (math.lengthsq(east) < 1e-6f) east = math.normalizesafe(math.cross(up, new float3(1, 0, 0)));
            float3 north = math.cross(up, east);

            float totalDisp = 0;
            float3 tangentDisp = float3.zero;

            for (int w = 0; w < waves.Length; w++)
            {
                var wave = waves[w];

                // Project wave direction onto local tangent plane
                float2 waveDir2D = wave.direction;
                float3 waveDir3D = east * waveDir2D.x + north * waveDir2D.y;

                // Phase along the wave direction
                float phase = math.dot(waveDir3D, p) * wave.k - wave.omega * time + wave.phase;

                float sinP = math.sin(phase);
                float cosP = math.cos(phase);

                // Gerstner radial displacement (height above sphere)
                totalDisp += wave.amplitude * sinP;

                // Gerstner tangential displacement (horizontal choppiness)
                float Q = wave.steepness / (wave.k * wave.amplitude + 1e-6f);
                tangentDisp += Q * wave.amplitude * waveDir3D * cosP;
            }

            // Displace radially outward + tangentially
            float3 displacedPos = p + normal * totalDisp + tangentDisp;

            // Recompute normal via finite differences (analytical for Gerstner)
            float3 newNormal = ComputeNormal(p, time, waves, east, north, normal);

            results[i] = new VertexResult
            {
                position = displacedPos,
                normal = newNormal,
                foam = new float2(math.saturate(-totalDisp * 0.3f), 0) // height-based foam hint
            };
        }

        private float3 ComputeNormal(float3 p, float t, NativeArray<WaveData> wv,
            float3 east, float3 north, float3 baseNormal)
        {
            // Numerical normal: sample 4 neighbors
            float eps = 0.1f;
            float hCenter = GetHeight(p, t, wv, east, north);
            float hEast = GetHeight(p + east * eps, t, wv, east, north);
            float hNorth = GetHeight(p + north * eps, t, wv, east, north);
            float hWest = GetHeight(p - east * eps, t, wv, east, north);
            float hSouth = GetHeight(p - north * eps, t, wv, east, north);

            float3 tangentX = east * (hEast - hWest) / (2f * eps);
            float3 tangentZ = north * (hNorth - hSouth) / (2f * eps);

            return math.normalizesafe(baseNormal - tangentX - tangentZ);
        }

        private float GetHeight(float3 p, float t, NativeArray<WaveData> wv,
            float3 east, float3 north)
        {
            float h = 0;
            for (int w = 0; w < wv.Length; w++)
            {
                var wave = wv[w];
                float3 waveDir3D = east * wave.direction.x + north * wave.direction.y;
                float phase = math.dot(waveDir3D, p) * wave.k - wave.omega * t + wave.phase;
                h += wave.amplitude * math.sin(phase);
            }
            return h;
        }
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>
    /// Evaluate waves for a set of base sphere surface points.
    /// Returns displaced positions + normals.
    /// </summary>
    public NativeArray<VertexResult> Evaluate(Vector3[] baseSurfacePositions)
    {
        if (!_allocated) Allocate();

        int count = baseSurfacePositions.Length;
        var input = new NativeArray<float3>(count, Allocator.TempJob);
        var output = new NativeArray<VertexResult>(count, Allocator.TempJob);

        for (int i = 0; i < count; i++)
            input[i] = baseSurfacePositions[i];

        float3 centre = planetCenter != null
            ? (float3)planetCenter.position
            : (float3)transform.position;

        var job = new EvaluateWavesJob
        {
            waves = _waveData,
            surfacePoints = input,
            results = output,
            time = _time,
            centre = centre
        };

        JobHandle handle = job.Schedule(count, 64);
        handle.Complete();

        input.Dispose();
        return output;
    }

    /// <summary>
    /// Evaluate waves for a single point on the sphere surface.
    /// </summary>
    public VertexResult EvaluateSingle(Vector3 baseSurfacePosition)
    {
        var arr = new Vector3[] { baseSurfacePosition };
        var results = Evaluate(arr);
        var result = results[0];
        results.Dispose();
        return result;
    }

    /// <summary>
    /// Burst-compatible evaluation for use inside other jobs.
    /// Pass your own NativeArray<WaveData> and wave parameters.
    /// </summary>
    public static float3 EvaluateWaveHeight(
        float3 surfacePoint, float3 centre, float time,
        NativeArray<WaveData> waves,
        float3 east, float3 north, float3 normal)
    {
        float totalDisp = 0;
        float3 tangentDisp = float3.zero;

        for (int w = 0; w < waves.Length; w++)
        {
            var wave = waves[w];
            float3 waveDir3D = east * wave.direction.x + north * wave.direction.y;
            float phase = math.dot(waveDir3D, surfacePoint) * wave.k - wave.omega * time + wave.phase;

            float sinP = math.sin(phase);
            float cosP = math.cos(phase);

            totalDisp += wave.amplitude * sinP;

            float Q = wave.steepness / (wave.k * wave.amplitude + 1e-6f);
            tangentDisp += Q * wave.amplitude * waveDir3D * cosP;
        }

        return surfacePoint + normal * totalDisp + tangentDisp;
    }

    /// <summary>
    /// WaveData struct for external Burst jobs that need wave evaluation.
    /// </summary>
    public NativeArray<WaveData> GetWaveDataCopy()
    {
        if (!_allocated) return default;
        var copy = new NativeArray<WaveData>(_waveData.Length, Allocator.TempJob);
        NativeArray<WaveData>.Copy(_waveData, copy);
        return copy;
    }
}
```

---

### 4. OceanFoamGenerator.cs (234 lines)

```csharp
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Generates foam data from wave simulation output.
/// Two foam sources:
///   1. Jacobian foam — from FFT wave crests overlapping (Jacobian determinant < threshold)
///   2. Shore foam — proximity to terrain, where waves break against land
///
/// Outputs a foam texture that can be sampled by HDRP shaders.
/// Uses Burst jobs for the CPU-side foam generation.
/// </summary>
public class OceanFoamGenerator : MonoBehaviour
{
    [Header("Jacobian Foam (from FFT)")]
    [Tooltip("Use FFT Jacobian as primary foam source.")]
    public bool useJacobianFoam = true;
    [Tooltip("Jacobian value below which foam appears.")]
    [Range(-1f, 0.5f)] public float jacobianThreshold = -0.05f;
    [Tooltip("Foam intensity scale.")]
    [Range(0f, 5f)] public float foamIntensity = 1.5f;

    [Header("Shore Foam")]
    [Tooltip("Generate foam where waves meet land.")]
    public bool useShoreFoam = true;
    [Tooltip("Distance from shore (world units) at which foam starts.")]
    [Range(0f, 30f)] public float shoreDistance = 10f;
    [Tooltip("Foam intensity at the shoreline.")]
    [Range(0f, 3f)] public float shoreIntensity = 2f;

    [Header("Foam Properties")]
    [Tooltip("Foam texture resolution.")]
    [Range(32, 512)] public int foamResolution = 256;
    [Tooltip("Foam fades over this many seconds without new wave energy.")]
    [Range(0.1f, 10f)] public float foamLifetime = 3f;
    [Tooltip("Wind effect on foam — stretches foam in wind direction.")]
    [Range(0f, 1f)] public float windStretch = 0.3f;
    [Tooltip("Noise scale for foam detail texture.")]
    public float noiseScale = 2f;

    [Header("Input")]
    [Tooltip("FFTWaveSimulation for Jacobian data. If null, tries to find on this object.")]
    public FFTWaveSimulation waveSimulation;
    [Tooltip("WaterSystem for local water depth (shore foam). If null, tries to find.")]
    public WaterSystem waterSystem;
    [Tooltip("Planet center for radial calculations.")]
    public Transform planetCenter;
    [Tooltip("Planet radius.")]
    public float planetRadius = 417f;

    private RenderTexture _foamTexture;
    private RenderTexture _prevFoamTexture;
    private Material _foamMaterial;
    private bool _allocated;

    // Shader property IDs
    private static readonly int ID_FoamTex = Shader.PropertyToID("_FoamTex");
    private static readonly int ID_PrevFoam = Shader.PropertyToID("_PrevFoam");
    private static readonly int ID_JacobianTex = Shader.PropertyToID("_JacobianTex");
    private static readonly int ID_Time = Shader.PropertyToID("_Time");
    private static readonly int ID_FoamIntensity = Shader.PropertyToID("_FoamIntensity");
    private static readonly int ID_FoamLifetime = Shader.PropertyToID("_FoamLifetime");
    private static readonly int ID_WindStretch = Shader.PropertyToID("_WindStretch");
    private static readonly int ID_NoiseScale = Shader.PropertyToID("_NoiseScale");
    private static readonly int ID_JacobianThreshold = Shader.PropertyToID("_JacobianThreshold");
    private static readonly int ID_Resolution = Shader.PropertyToID("_Resolution");
    private static readonly int ID_PatchSize = Shader.PropertyToID("_PatchSize");

    public RenderTexture FoamTexture => _foamTexture;

    private void Start()
    {
        if (waveSimulation == null) waveSimulation = GetComponent<FFTWaveSimulation>();
        if (waterSystem == null) waterSystem = FindFirstObjectByType<WaterSystem>();
        Allocate();
    }

    private void OnDestroy()
    {
        Release();
    }

    private void OnValidate()
    {
        if (_allocated) { Release(); Start(); }
    }

    private void Allocate()
    {
        if (_allocated) Release();

        _foamTexture = CreateRT(foamResolution, foamResolution, "FoamResult");
        _prevFoamTexture = CreateRT(foamResolution, foamResolution, "FoamPrevious");

        _allocated = true;
    }

    private void Release()
    {
        _foamTexture?.Release(); _foamTexture = null;
        _prevFoamTexture?.Release(); _prevFoamTexture = null;
        _allocated = false;
    }

    private static RenderTexture CreateRT(int w, int h, string name)
    {
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat,
            enableRandomWrite = true
        };
        rt.Create();
        return rt;
    }

    /// <summary>
    /// Update foam from Jacobian data (called by FFTWaveSimulation or manually).
    /// Blends Jacobian foam with shore foam and accumulates over time.
    /// </summary>
    public void UpdateFoam()
    {
        if (!_allocated) return;

        // Swap ping-pong
        (_prevFoamTexture, _foamTexture) = (_foamTexture, _prevFoamTexture);

        // Simple GPU-side foam update using Graphics.Blit with a material
        // In production, this would be a compute dispatch. For now, use Blit.
        if (_foamMaterial == null)
        {
            // Create a minimal material for foam blending
            Shader shader = Shader.Find("Hidden/OceanFoamBlend");
            if (shader == null)
            {
                // Fallback: just copy the previous foam with decay
                Graphics.Blit(_prevFoamTexture, _foamTexture);
                return;
            }
            _foamMaterial = new Material(shader);
        }

        _foamMaterial.SetFloat(ID_FoamIntensity, foamIntensity);
        _foamMaterial.SetFloat(ID_FoamLifetime, foamLifetime);
        _foamMaterial.SetFloat(ID_WindStretch, windStretch);
        _foamMaterial.SetFloat(ID_NoiseScale, noiseScale);
        _foamMaterial.SetFloat(ID_Time, Time.time);

        // Feed Jacobian texture if available
        if (useJacobianFoam && waveSimulation != null && waveSimulation.JacobianTexture != null)
        {
            _foamMaterial.SetTexture(ID_JacobianTex, waveSimulation.JacobianTexture);
            _foamMaterial.SetFloat(ID_JacobianThreshold, jacobianThreshold);
        }

        Graphics.Blit(_prevFoamTexture, _foamTexture, _foamMaterial);
    }

    /// <summary>
    /// Generate shore foam CPU-side using raycasting from the water patch.
    /// Writes directly into the foam texture.
    /// </summary>
    public void GenerateShoreFoam(Vector3[] cellDirections, float[] groundHeights, float seaRadius)
    {
        if (!_allocated || !useShoreFoam || cellDirections == null) return;

        int n = (int)Mathf.Sqrt(foamResolution);
        n = Mathf.Max(n, 16);

        var job = new ShoreFoamJob
        {
            cellDirs = cellDirections,
            groundHeights = groundHeights,
            seaRadius = seaRadius,
            shoreDistance = shoreDistance,
            shoreIntensity = shoreIntensity,
            planetCentre = planetCenter != null ? (float3)planetCenter.position : float3.zero,
            n = n,
            foamResolution = foamResolution
        };

        // This would write to a NativeArray<float> which then gets uploaded to the texture
        // For now, this is a placeholder for the full implementation
        job.Schedule(cellDirections.Length, 64).Complete();
    }

    /// <summary>
    /// Bind foam texture to a renderer's material property block.
    /// </summary>
    public void BindToRenderer(Renderer renderer)
    {
        if (!_allocated || renderer == null) return;
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetTexture(ID_FoamTex, _foamTexture);
        renderer.SetPropertyBlock(block);
    }

    /// <summary>
    /// Bind foam texture globally for all shaders.
    /// </summary>
    public void BindGlobal()
    {
        if (!_allocated) return;
        Shader.SetGlobalTexture(ID_FoamTex, _foamTexture);
    }

    [BurstCompile]
    private struct ShoreFoamJob : IJobParallelFor
    {
        [ReadOnly] public Vector3[] cellDirs;
        [ReadOnly] public float[] groundHeights;
        public float seaRadius;
        public float shoreDistance;
        public float shoreIntensity;
        public float3 planetCentre;
        public int n;
        public int foamResolution;

        public void Execute(int i)
        {
            if (i >= cellDirs.Length) return;
            // Shore foam logic would go here:
            // For each cell, check if ground is within shoreDistance of sea level
            // If so, generate foam proportional to proximity
            float heightDiff = Mathf.Abs(groundHeights[i] - seaRadius);
            float foam = Mathf.Clamp01(1f - heightDiff / shoreDistance) * shoreIntensity;
            // Would write to a foam array here
        }
    }
}
```

---

### 5. WaveCascadeData.cs (152 lines)

```csharp
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
```

---

### 6. OceanRenderer.cs (260 lines)

```csharp
using UnityEngine;

/// <summary>
/// Orchestrator: ties FFTWaveSimulation + SphericalGerstnerWaves + OceanFoamGenerator
/// into a single unified ocean rendering pipeline.
///
/// Attach this to the ocean sphere. It manages:
///   1. FFT wave simulation (GPU) - for the main displacement field
///   2. Spherical Gerstner waves (Burst) - for analytical wave detail
///   3. Foam generation - from Jacobian + shore proximity
///   4. Cascade blending - multiple FFT passes at different scales
///   5. Shader binding - feeds all data to HDRP shaders
///
/// This sits between your physics layer and your HDRP shader layer.
/// Call BindGlobal() once to push all textures to shader globals.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class OceanRenderer : MonoBehaviour
{
    [Header("References")]
    public Transform planetCenter;
    public float planetRadius = 417f;
    public WaterSystem waterSystem;

    [Header("Wave Simulation")]
    public FFTWaveSimulation fftWaves;
    public SphericalGerstnerWaves gerstnerWaves;
    public OceanFoamGenerator foamGenerator;

    [Header("Cascade Settings")]
    public WaveCascadeData cascadeData;

    [Header("Rendering")]
    public Material oceanMaterial;
    [Range(32, 256)] public int meshResolution = 128;
    [Range(0, 10)] public int meshUpdateInterval = 1;
    public float meshUpdateDistance = 500f;

    private Mesh _mesh;
    private MeshRenderer _meshRenderer;
    private Vector3[] _baseVertices;
    private Vector3[] _displacedVertices;
    private Vector3[] _normals;
    private int _frameCounter;
    private Camera _mainCamera;

    private static readonly int ID_DispTex = Shader.PropertyToID("_OceanDisplacementTex");
    private static readonly int ID_FoamTex = Shader.PropertyToID("_OceanFoamTex");
    private static readonly int ID_JacTex = Shader.PropertyToID("_OceanJacobianTex");
    private static readonly int ID_PatchSize = Shader.PropertyToID("_OceanPatchSize");
    private static readonly int ID_PlanetRadius = Shader.PropertyToID("_OceanPlanetRadius");
    private static readonly int ID_PlanetCentre = Shader.PropertyToID("_OceanPlanetCentre");
    private static readonly int ID_Time = Shader.PropertyToID("_OceanTime");
    private static readonly int ID_CascadeWeights = Shader.PropertyToID("_OceanCascadeWeights");

    private void Awake()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
    }

    private void Start()
    {
        if (planetCenter == null) planetCenter = transform;
        if (waterSystem == null) waterSystem = FindFirstObjectByType<WaterSystem>();
        _mainCamera = Camera.main;

        CreateOceanMesh();

        if (fftWaves == null)
            fftWaves = gameObject.AddComponent<FFTWaveSimulation>();

        if (gerstnerWaves == null)
            gerstnerWaves = gameObject.AddComponent<SphericalGerstnerWaves>();

        gerstnerWaves.sphereRadius = planetRadius;
        gerstnerWaves.planetCenter = planetCenter;

        if (foamGenerator == null)
            foamGenerator = gameObject.AddComponent<OceanFoamGenerator>();

        foamGenerator.planetCenter = planetCenter;
        foamGenerator.planetRadius = planetRadius;

        BindGlobal();
    }

    private void Update()
    {
        Shader.SetGlobalFloat(ID_Time, Time.time);

        if (cascadeData != null && _mainCamera != null)
        {
            float dist = Vector3.Distance(_mainCamera.transform.position, transform.position);
            float[] weights = new float[cascadeData.cascades.Length];
            cascadeData.GetBlendWeights(dist, weights);
            if (weights.Length >= 4)
                Shader.SetGlobalVector(ID_CascadeWeights, new Vector4(weights[0], weights[1], weights[2], weights[3]));
        }

        _frameCounter++;
        if (meshUpdateInterval > 0 && _frameCounter % meshUpdateInterval != 0) return;

        if (_mainCamera != null)
        {
            float dist = Vector3.Distance(_mainCamera.transform.position, transform.position);
            if (dist > meshUpdateDistance) return;
        }

        DeformMesh();
    }

    private void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
    }

    private void CreateOceanMesh()
    {
        _mesh = new Mesh { name = "OceanSphere", indexFormat = IndexFormat.UInt32 };
        GetComponent<MeshFilter>().sharedMesh = _mesh;

        int res = meshResolution;
        int vertCount = (res + 1) * (res + 1);
        int triCount = res * res * 6;

        _baseVertices = new Vector3[vertCount];
        _displacedVertices = new Vector3[vertCount];
        _normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        var tris = new int[triCount];

        for (int lat = 0; lat <= res; lat++)
        {
            float theta = Mathf.PI * lat / res;
            float sinTheta = Mathf.Sin(theta);
            float cosTheta = Mathf.Cos(theta);

            for (int lon = 0; lon <= res; lon++)
            {
                float phi = 2f * Mathf.PI * lon / res;
                int i = lat * (res + 1) + lon;

                Vector3 dir = new Vector3(
                    sinTheta * Mathf.Cos(phi),
                    cosTheta,
                    sinTheta * Mathf.Sin(phi)
                );

                _baseVertices[i] = dir;
                _displacedVertices[i] = dir;
                _normals[i] = dir;
                uvs[i] = new Vector2((float)lon / res, (float)lat / res);
            }
        }

        int ti = 0;
        for (int lat = 0; lat < res; lat++)
        {
            for (int lon = 0; lon < res; lon++)
            {
                int a = lat * (res + 1) + lon;
                int b = a + 1;
                int c = a + (res + 1);
                int d = c + 1;

                tris[ti++] = a;
                tris[ti++] = c;
                tris[ti++] = b;
                tris[ti++] = b;
                tris[ti++] = c;
                tris[ti++] = d;
            }
        }

        _mesh.Clear();
        _mesh.vertices = _baseVertices;
        _mesh.uv = uvs;
        _mesh.triangles = tris;
        _mesh.RecalculateNormals();
        _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * planetRadius * 3f);
    }

    private void DeformMesh()
    {
        if (_mesh == null || _baseVertices == null) return;

        if (gerstnerWaves != null)
        {
            var results = gerstnerWaves.Evaluate(_baseVertices);
            int vertCount = _baseVertices.Length;

            for (int i = 0; i < vertCount && i < results.Length; i++)
            {
                _displacedVertices[i] = results[i].position;
                _normals[i] = results[i].normal;
            }
            results.Dispose();
        }

        _mesh.vertices = _displacedVertices;
        _mesh.normals = _normals;
        _mesh.RecalculateBounds();
    }

    public void BindGlobal()
    {
        if (fftWaves != null) fftWaves.BindGlobal();
        if (foamGenerator != null) foamGenerator.BindGlobal();

        Shader.SetGlobalFloat(ID_PatchSize, fftWaves != null ? fftWaves.PatchSize : 200f);
        Shader.SetGlobalFloat(ID_PlanetRadius, planetRadius);
        Shader.SetGlobalVector(ID_PlanetCentre,
            planetCenter != null ? (Vector4)planetCenter.position : Vector4.zero);
    }

    public void BindToMaterialPropertyBlock(MaterialPropertyBlock block)
    {
        if (fftWaves != null) fftWaves.BindToMaterialPropertyBlock(block);
        if (foamGenerator != null) block.SetTexture(ID_FoamTex, foamGenerator.FoamTexture);
    }

    public void BindToMaterial(Material mat)
    {
        if (mat == null) return;
        if (fftWaves != null && fftWaves.DisplacementTexture != null)
            mat.SetTexture(ID_DispTex, fftWaves.DisplacementTexture);
        if (fftWaves != null && fftWaves.JacobianTexture != null)
            mat.SetTexture(ID_JacTex, fftWaves.JacobianTexture);
        if (foamGenerator != null && foamGenerator.FoamTexture != null)
            mat.SetTexture(ID_FoamTex, foamGenerator.FoamTexture);
        mat.SetFloat(ID_PatchSize, fftWaves != null ? fftWaves.PatchSize : 200f);
        mat.SetFloat(ID_PlanetRadius, planetRadius);
    }

    public float SampleOceanHeight(Vector3 worldPosition)
    {
        Vector3 centre = planetCenter != null ? planetCenter.position : transform.position;
        Vector3 toPoint = worldPosition - centre;
        Vector3 dir = toPoint.normalized;

        float height = planetRadius;

        if (gerstnerWaves != null)
        {
            var result = gerstnerWaves.EvaluateSingle(dir * planetRadius);
            float displacement = (result.position - dir * planetRadius).magnitude;
            height += displacement;
        }

        return height;
    }

    public bool IsUnderwater(Vector3 worldPosition)
    {
        float oceanH = SampleOceanHeight(worldPosition);
        Vector3 centre = planetCenter != null ? planetCenter.position : transform.position;
        float pointDist = Vector3.Distance(worldPosition, centre);
        return pointDist < oceanH;
    }
}
```

---

### 7. OceanWaveCompute.compute (242 lines)

```hlsl
// OceanWaveCompute.compute
// GPU FFT ocean wave simulation — spectrum update, butterfly FFT, displacement, Jacobian, foam.
// Dispatch groups: (resolution/8, resolution/8, 1) for all kernels.
//
// Ping-pong buffers: float2 (real, imag) for complex frequency-domain data.
// Output textures: float4 displacement (x,height,z,0), float Jacobian, accumulated foam.

#pragma kernel UpdateSpectrum
#pragma kernel ButterflyPass
#pragma kernel InverseTransform
#pragma kernel ComputeJacobian
#pragma kernel UpdateFoam

// --- Parameters ---
int _Resolution;
float _Time;
float _WindSpeed;
float2 _WindDir;
float _SpectrumScale;
float _Choppiness;
float _Gravity;
float _PatchSize;
float _FoamThreshold;
float _FoamDecay;
float _FoamGain;
int _Stage;
bool _Horizontal;

// --- Buffers ---
StructuredBuffer<float4> _Spectrum;     // (kx, ky, energy, phase) per cell
StructuredBuffer<float2> _Twiddle;      // twiddle factors per stage
RWStructuredBuffer<float2> _PingPongA;
RWStructuredBuffer<float2> _PingPongB;

// --- Textures ---
RWTexture2D<float4> _DispOut;   // displacement output
RWTexture2D<float>  _JacOut;   // Jacobian output
RWTexture2D<float>  _FoamTex;  // accumulated foam (read + write)

// --- Sampler ---
SamplerState sampler_linear_clamp;

// =========================================================================
//  Kernel 0: UpdateSpectrum
//  Time-evolve the spectrum: generate complex amplitudes H(k,t) from energy P(k).
//  H(k,t) = sqrt(P(k)) * e^(i * omega * t + i * phase) for each wavevector.
// =========================================================================
[numthreads(8, 8, 1)]
void UpdateSpectrum(uint3 id : SV_DispatchThreadID)
{
    int idx = id.y * _Resolution + id.x;
    if (idx >= _Resolution * _Resolution) return;

    float4 spec = _Spectrum[idx];
    float2 k = spec.xy;        // wavevector
    float energy = spec.z;     // spectrum amplitude
    float phase = spec.w;      // random initial phase

    float kLen = length(k);
    if (kLen < 1e-7)
    {
        _PingPongA[idx] = float2(0, 0);
        return;
    }

    // Deep-water dispersion: omega = sqrt(g * |k|)
    float omega = sqrt(_Gravity * kLen);

    // Complex exponential: e^(i * (omega*t + phase))
    float theta = omega * _Time + phase;
    float cosT = cos(theta);
    float sinT = sin(theta);

    // Amplitude = sqrt(energy) with directional alignment
    float2 kNorm = k / kLen;
    float alignment = dot(kNorm, _WindDir);
    float directional = alignment > 0 ? pow(alignment, 2.0) : 0;
    float amp = sqrt(max(0, energy)) * (1.0 + _WindSpeed * 0.01 * directional);

    // H(k,t) = amp * e^(i*theta)
    _PingPongA[idx] = float2(amp * cosT, amp * sinT);
}

// =========================================================================
//  Kernel 1: ButterflyPass
//  Cooley-Tukey radix-2 FFT butterfly. One stage per dispatch.
// =========================================================================
[numthreads(8, 8, 1)]
void ButterflyPass(uint3 id : SV_DispatchThreadID)
{
    int idx = id.y * _Resolution + id.x;
    if (idx >= _Resolution * _Resolution) return;

    int stage = _Stage;
    int halfSize = 1 << stage;
    int fullSize = halfSize << 1;

    int groupIdx = idx % fullSize;
    int butterflyIdx = groupIdx % halfSize;

    float2 twiddle = _Twiddle[stage * _Resolution + butterflyIdx];

    int srcIdx, dstIdx;
    float2 a, b;

    if (_Horizontal)
    {
        int row = idx / _Resolution;
        int col = idx % _Resolution;
        int colGroup = col / fullSize;
        int colInGroup = col % fullSize;

        srcIdx = row * _Resolution + colGroup * fullSize + colInGroup;
        int partner = (colInGroup < halfSize)
            ? srcIdx + halfSize
            : srcIdx - halfSize;

        a = _PingPongA[srcIdx];
        b = _PingPongA[partner];

        if (colInGroup < halfSize)
        {
            _PingPongB[idx] = a + twiddle.x * b - float2(0, twiddle.y) * b;
        }
        else
        {
            _PingPongB[idx] = b + twiddle.x * a - float2(0, twiddle.y) * a;
        }
    }
    else
    {
        int row = idx / _Resolution;
        int col = idx % _Resolution;
        int rowGroup = row / fullSize;
        int rowInGroup = row % fullSize;

        srcIdx = rowGroup * fullSize + rowInGroup;
        srcIdx = srcIdx * _Resolution + col;
        int partnerOffset = (rowInGroup < halfSize) ? halfSize * _Resolution : -halfSize * _Resolution;

        a = _PingPongA[srcIdx];
        b = _PingPongA[srcIdx + partnerOffset];

        if (rowInGroup < halfSize)
        {
            _PingPongB[idx] = a + twiddle.x * b - float2(0, twiddle.y) * b;
        }
        else
        {
            _PingPongB[idx] = b + twiddle.x * a - float2(0, twiddle.y) * a;
        }
    }
}

// =========================================================================
//  Kernel 2: InverseTransform
//  Read the finished IFFT result, generate displacement height map.
//  Displacement XZ = choppiness * real component, height Y = imag component.
// =========================================================================
[numthreads(8, 8, 1)]
void InverseTransform(uint3 id : SV_DispatchThreadID)
{
    int2 coord = int2(id.x, id.y);
    if (id.x >= (uint)_Resolution || id.y >= (uint)_Resolution) return;

    int idx = id.y * _Resolution + id.x;
    float2 val = _PingPongA[idx];

    // Normalize by resolution (FFT convention)
    float norm = 1.0 / (float)(_Resolution * _Resolution);
    float real = val.x * norm;
    float imag = val.y * norm;

    // Height = imaginary part, displacement = choppiness * real part
    float height = imag;
    float2 disp = float2(real, real) * _Choppiness;

    // Output: x=dispX, y=height, z=dispZ, w=unused
    _DispOut[coord] = float4(disp.x, height, disp.y, 0);
}

// =========================================================================
//  Kernel 3: ComputeJacobian
//  Jacobian determinant of the displacement field — used for foam generation.
//  J = (1 + dDx/dx)(1 + dDz/dz) - (dDx/dz)(dDz/dx)
//  Where D is the horizontal displacement field.
// =========================================================================
[numthreads(8, 8, 1)]
void ComputeJacobian(uint3 id : SV_DispatchThreadID)
{
    int2 coord = int2(id.x, id.y);
    if (id.x >= (uint)_Resolution || id.y >= (uint)_Resolution) return;

    int2 left  = (id.x > 0) ? int2(id.x - 1, id.y) : int2(_Resolution - 1, id.y);
    int2 right = (id.x < (uint)(_Resolution - 1)) ? int2(id.x + 1, id.y) : int2(0, id.y);
    int2 down  = (id.y > 0) ? int2(id.x, id.y - 1) : int2(id.x, _Resolution - 1);
    int2 up    = (id.y < (uint)(_Resolution - 1)) ? int2(id.x, id.y + 1) : int2(id.x, 0);

    float4 c = _DispOut[coord];
    float4 l = _DispOut[left];
    float4 r = _DispOut[right];
    float4 d = _DispOut[down];
    float4 u = _DispOut[up];

    // Finite differences of displacement (x and z components)
    float dDx_dx = (r.x - l.x) * 0.5;
    float dDx_dz = (u.x - d.x) * 0.5;
    float dDz_dx = (r.z - l.z) * 0.5;
    float dDz_dz = (u.z - d.z) * 0.5;

    // Jacobian determinant
    float J = (1.0 + dDx_dx) * (1.0 + dDz_dz) - dDx_dz * dDz_dx;

    _JacOut[coord] = J;
}

// =========================================================================
//  Kernel 4: UpdateFoam
//  Accumulate foam from Jacobian: foam increases when J < threshold,
//  decays exponentially otherwise.
// =========================================================================
[numthreads(8, 8, 1)]
void UpdateFoam(uint3 id : SV_DispatchThreadID)
{
    int2 coord = int2(id.x, id.y);
    if (id.x >= (uint)_Resolution || id.y >= (uint)_Resolution) return;

    float J = _JacOut[coord];
    float prevFoam = _FoamTex[coord];

    // Foam accumulates where wave crests overlap (J < threshold)
    float foamDelta = 0;
    if (J < _FoamThreshold)
    {
        foamDelta = _FoamGain * (_FoamThreshold - J);
    }

    // Exponential decay
    float foam = (prevFoam + foamDelta) * exp(-_FoamDecay * 0.016); // ~60fps assumption

    _FoamTex[coord] = saturate(foam);
}
```

---

## Known Issues & Concerns

### 1. FFT Butterfly Implementation (HIGH)
- **Concern:** The butterfly pass uses a simplified Cooley-Tukey approach that may not handle all stage/buffer combinations correctly. The horizontal/vertical ping-pong logic has edge cases at resolution boundaries.
- **Question:** Has this been tested at multiple resolutions (128, 256, 512)? The twiddle factor table indexing needs verification.
- **Fix needed:** Validate FFT output against known analytical solutions (single sine wave should reconstruct perfectly).

### 2. Spectrum Initialization (MEDIUM)
- **Concern:** `InitSpectrum()` generates the spectrum once at `Start()`. Real ocean sims re-generate the spectrum when wind speed/direction changes at runtime.
- **Question:** Should we add a `RegenerateSpectrum()` method and call it when wind parameters change?
- **Fix needed:** Add dirty flag + spectrum regeneration when wind changes.

### 3. Displacement Readback (MEDIUM)
- **Concern:** `SampleDisplacement()` does a full `Texture2D.ReadPixels()` every call — this is extremely slow (GPU->CPU readback). It's only meant for CPU queries, but nothing prevents it from being called per-frame.
- **Question:** Should we add a warning or rate-limit this method? Or add an async readback path?
- **Fix needed:** Add `[System.Obsolete]` or a frame-rate limiter. Better: use `AsyncGPUReadback` for non-blocking reads.

### 4. Gerstner Wave Normal Computation (MEDIUM)
- **Concern:** The numerical normal computation samples 5 points (center + 4 neighbors) per vertex. With 16 Gerstner wave sets and 256x256 mesh, that's 16 x 5 x 65,536 = 5.2M wave evaluations per frame.
- **Question:** Is this within budget on a 4060 Ti? The Burst compiler should help, but the O(N) per-vertex wave loop might be a bottleneck.
- **Fix needed:** Profile on target hardware. Consider pre-computing normals analytically for Gerstner (they have closed-form expressions).

### 5. Foam Material Shader Missing (LOW)
- **Concern:** `OceanFoamGenerator` references `Shader.Find("Hidden/OceanFoamBlend")` which doesn't exist. The fallback is just a copy (no foam blending).
- **Question:** Is this shader planned, or should we implement a simple one?
- **Fix needed:** Create the `OceanFoamBlend` shader or use a compute shader for foam update.

### 6. Compute Shader Resource Loading (LOW)
- **Concern:** `FFTWaveSimulation` tries `Resources.Load<ComputeShader>("OceanWaveCompute")` but the compute shader is in `Assets/Shaders/`, not `Assets/Resources/`. It will fail to auto-load.
- **Question:** Should we move the compute shader to `Resources/` or keep it as a manual assignment?
- **Fix needed:** Either move to `Assets/Resources/` or remove the auto-load fallback and require manual assignment.

### 7. Sphere Mesh Vertex Count (LOW)
- **Concern:** The lat/lon grid has degenerate vertices at poles (all longitude verts converge to one point). This wastes triangles and causes UV stretching.
- **Question:** Should we use an icosphere subdivision instead, or just accept the pole artifact for a planet-scale ocean?
- **Fix needed:** Low priority for planet-scale, but could cause visual artifacts at close zoom.

### 8. Missing .meta Files (LOW)
- **Concern:** The new files don't have Unity `.meta` files. Unity will generate them on first import, but they won't match the original ProceduralPlanet project's GUIDs.
- **Question:** Is this intentional (standalone repo) or should we generate stable GUIDs?
- **Fix needed:** None if standalone. If merging back, need to match GUIDs.

---

## Questions for Review

1. **FFT correctness:** Does the butterfly implementation produce correct displacement for a known spectrum? The twiddle factor table needs verification against reference implementations.

2. **Spherical Gerstner math:** Is the radial displacement + tangent choppiness approach physically correct for sphere surfaces? The standard Gerstner derivation assumes a flat plane.

3. **Cascade blending:** The `WaveCascadeData` has blend weights but no actual cascade instantiation. Should `OceanRenderer` create multiple `FFTWaveSimulation` instances?

4. **Foam persistence:** The foam ping-pong uses `exp(-decay * 0.016)` assuming 60fps. Should this use `Time.deltaTime` for frame-rate independence?

5. **Shader binding:** `BindGlobal()` pushes to global shader properties. Is this safe if multiple ocean instances exist (e.g., multiplayer)?

6. **Memory:** Each FFT cascade allocates `resolution^2 * 8 bytes` for ping-pong buffers + spectrum + twiddle. At 512x512 that's ~8MB per cascade. Is this acceptable?

7. **Burst compatibility:** The `EvaluateWavesJob` uses `NativeArray<WaveData>` with `float2` fields. Does the Burst compiler handle this correctly, or do we need `[BurstCompile]` struct layouts?

---

## What's Missing (Not Yet Implemented)

1. **HDRP shader** — The physics layer outputs textures but no shader reads them yet
2. **Cascade instantiation** — `WaveCascadeData` is a config asset but doesn't spawn multiple FFT sims
3. **OceanFoamBlend shader** — Referenced but not created
4. **Wind change spectrum regen** — Static spectrum only
5. **Async GPU readback** — `SampleDisplacement()` is synchronous
6. **Buoyancy integration** — No buoyancy force calculation from wave displacement
7. **Spray/particle system** — No particle emission from wave crests
8. **Underwater fog/volume** — No subsurface scattering or underwater rendering
9. **GPU vertex displacement** — Mesh deformation is CPU-side (Burst), not vertex shader

---

## Suggested Next Steps

1. **Validate FFT output** — Run the compute shader, read back displacement, verify against analytical waves
2. **Create OceanFoamBlend.shader** — Simple ping-pong foam accumulation shader
3. **Move compute shader to Resources/** or require manual assignment
4. **Profile Burst Gerstner** on target hardware (4060 Ti)
5. **Write HDRP ocean shader** — Sample displacement + foam + Jacobian, apply PBR water rendering
6. **Add cascade instantiation** to OceanRenderer
7. **Implement async GPU readback** for SampleDisplacement()

---

*Full source code embedded for review. Paste into Google Gemini or any AI reviewer for feedback.*
