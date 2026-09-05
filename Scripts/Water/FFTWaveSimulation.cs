using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// GPU-accelerated FFT ocean wave simulation.
/// Generates a displacement field + Jacobian foam map from a wind-driven spectrum.
///
/// Pipeline each frame:
///   1. UpdateSpectrum() — time-evolve with conjugate symmetry for real IFFT output.
///   2. FFT (Butterfly) — log2(N) horizontal+vertical passes on the compute shader.
///   3. InverseTransform() — bit-reverse permutation + displacement (XZ), height (Y).
///   4. ComputeJacobian() — foam source from displacement gradients.
///   5. UpdateFoam() — frame-rate independent accumulation + decay.
///
/// Requires: OceanWaveCompute.compute in Resources/ or assigned manually.
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
    private RenderTexture _dispTex;
    private RenderTexture _jacobianTex;
    private RenderTexture _foamAccumTex;
    private ComputeBuffer _spectrumBuffer;
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

    // Async readback cache
    private Texture2D _readbackTex;
    private bool _readbackPending;
    private float _lastReadbackTime;
    private const float READBACK_INTERVAL = 0.1f; // Throttle to ~10 readbacks/sec

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
    private static readonly int ID_DeltaTime = Shader.PropertyToID("_DeltaTime");

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
        if (_readbackTex != null) { Destroy(_readbackTex); _readbackTex = null; }
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

        _pingPongA = new ComputeBuffer(count, sizeof(float) * 2);
        _pingPongB = new ComputeBuffer(count, sizeof(float) * 2);
        _spectrumBuffer = new ComputeBuffer(count, sizeof(float) * 4);
        _twiddleBuffer = new ComputeBuffer(count, sizeof(float) * 2);
        _bitReverseBuffer = new ComputeBuffer(count, sizeof(int));

        _dispTex = CreateRT(resolution, resolution, RenderTextureFormat.ARGBFloat, "OceanDisplacement");
        _jacobianTex = CreateRT(resolution, resolution, RenderTextureFormat.RFloat, "OceanJacobian");
        _foamAccumTex = CreateRT(resolution, resolution, RenderTextureFormat.RFloat, "OceanFoam");
        _foamAccumTex.filterMode = FilterMode.Bilinear;

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

        var spectrumData = new Vector4[resolution * resolution];

        // First pass: generate independent phases for unique half of spectrum
        // Second pass: enforce conjugate symmetry h0(-k) = conj(h0(k))
        var phases = new float[resolution * resolution];
        for (int iy = 0; iy < resolution; iy++)
        for (int ix = 0; ix < resolution; ix++)
        {
            phases[iy * resolution + ix] = UnityEngine.Random.Range(0f, 2f * math.PI);
        }

        for (int iy = 0; iy < resolution; iy++)
        for (int ix = 0; ix < resolution; ix++)
        {
            int kx = ix < resolution / 2 ? ix : ix - resolution;
            int ky = iy < resolution / 2 ? iy : iy - resolution;
            float2 k = new float2(kx, ky) * (2f * math.PI / patchSize);

            float energy = OceanWaveSpectrum.Sample(k, windSpeed, wDir, gravity, spectrumType);
            energy *= spectrumScale;

            // Mirror index for conjugate symmetry: h0(-k) = conj(h0(k))
            int mx = (resolution - ix) % resolution;
            int my = (resolution - iy) % resolution;

            float phase;
            if (ix == mx && iy == my)
            {
                // DC component or Nyquist — use original phase
                phase = phases[iy * resolution + ix];
            }
            else if (ix < mx || (ix == mx && iy < my))
            {
                // This is the "positive" half — use random phase
                phase = phases[iy * resolution + ix];
            }
            else
            {
                // This is the mirror of a cell we already processed
                // Use negative phase so h0(-k) = conj(h0(k))
                phase = -phases[my * resolution + mx];
            }

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
            float angle = -math.PI / halfSize;

            for (int k = 0; k < halfSize; k++)
            {
                float wAngle = angle * k;
                twiddle[stage * resolution + k] = new Vector2(math.cos(wAngle), math.sin(wAngle));
            }
        }

        // Bit reversal for 1D
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

        // Expand bit-reverse for full 2D
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

        int groups = Mathf.CeilToInt(resolution / 8f);
        fftCompute.Dispatch(_kernelSpectrumUpdate, groups, groups, 1);
    }

    private void RunFFT()
    {
        if (fftCompute == null) return;

        fftCompute.SetInt(ID_Resolution, resolution);
        fftCompute.SetBuffer(_kernelButterfly, ID_Twiddle, _twiddleBuffer);

        int groups = Mathf.CeilToInt(resolution / 8f);

        for (int stage = 0; stage < _logN; stage++)
        {
            bool horizontal = stage % 2 == 0;
            ComputeBuffer ping = horizontal ? _pingPongA : _pingPongB;
            ComputeBuffer pong = horizontal ? _pingPongB : _pingPongA;

            fftCompute.SetInt("_Stage", stage);
            fftCompute.SetBool("_Horizontal", horizontal);
            fftCompute.SetBuffer(_kernelButterfly, ID_PingPongA, ping);
            fftCompute.SetBuffer(_kernelButterfly, ID_PingPongB, pong);

            fftCompute.Dispatch(_kernelButterfly, groups, groups, 1);
        }

        // Inverse transform reads from the last-written buffer + applies bit-reversal
        ComputeBuffer resultBuffer = (_logN % 2 == 0) ? _pingPongA : _pingPongB;

        fftCompute.SetInt(ID_Resolution, resolution);
        fftCompute.SetFloat(ID_Choppiness, choppiness);
        fftCompute.SetBuffer(_kernelInverseTransform, ID_PingPongA, resultBuffer);
        fftCompute.SetBuffer(_kernelInverseTransform, ID_BitReverse, _bitReverseBuffer);
        fftCompute.SetTexture(_kernelInverseTransform, ID_DispOut, _dispTex);

        fftCompute.Dispatch(_kernelInverseTransform, groups, groups, 1);
    }

    private void ComputeJacobian()
    {
        if (fftCompute == null) return;

        fftCompute.SetInt(ID_Resolution, resolution);
        fftCompute.SetFloat(ID_Choppiness, choppiness);

        fftCompute.SetTexture(_kernelJacobian, ID_DispOut, _dispTex);
        fftCompute.SetTexture(_kernelJacobian, ID_JacOut, _jacobianTex);

        int groups = Mathf.CeilToInt(resolution / 8f);
        fftCompute.Dispatch(_kernelJacobian, groups, groups, 1);
    }

    private void UpdateFoam()
    {
        if (fftCompute == null) return;

        fftCompute.SetFloat(ID_FoamThreshold, foamThreshold);
        fftCompute.SetFloat(ID_FoamDecay, foamDecay);
        fftCompute.SetFloat(ID_FoamGain, foamGain);
        fftCompute.SetFloat(ID_DeltaTime, Time.deltaTime);

        fftCompute.SetTexture(_kernelFoamUpdate, ID_JacOut, _jacobianTex);
        fftCompute.SetTexture(_kernelFoamUpdate, ID_FoamTex, _foamAccumTex);

        int groups = Mathf.CeilToInt(resolution / 8f);
        fftCompute.Dispatch(_kernelFoamUpdate, groups, groups, 1);
    }

    // =========================================================================
    //  Public API for HDRP shaders
    // =========================================================================

    public void BindToMaterialPropertyBlock(MaterialPropertyBlock block)
    {
        if (!_allocated) return;
        block.SetTexture(ID_DispTex, _dispTex);
        block.SetTexture(ID_JacobianTex, _jacobianTex);
        block.SetTexture(ID_FoamTex, _foamAccumTex);
        block.SetFloat(ID_PatchSize, patchSize);
        block.SetInt(ID_Resolution, resolution);
    }

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
    /// Sample displacement at a world-space point using async GPU readback.
    /// Non-blocking — returns the cached result from the previous frame's readback.
    /// First call returns Vector3.zero (no data yet).
    /// </summary>
    public Vector3 SampleDisplacement(Vector3 worldPos, Transform oceanOrigin)
    {
        if (!_allocated || _dispTex == null) return Vector3.zero;

        // Init readback texture on first call
        if (_readbackTex == null || _readbackTex.width != resolution)
        {
            if (_readbackTex != null) Destroy(_readbackTex);
            _readbackTex = new Texture2D(resolution, resolution, TextureFormat.RGBAFloat, false);
        }

        // Throttle readback requests to avoid flooding the pipeline
        if (!_readbackPending && Time.unscaledTime - _lastReadbackTime > READBACK_INTERVAL)
        {
            _readbackPending = true;
            _lastReadbackTime = Time.unscaledTime;

            AsyncGPUReadback.Request(_dispTex, 0, TextureFormat.RGBAFloat, (request) =>
            {
                _readbackPending = false;
                if (request.hasError)
                {
                    return;
                }

                var data = request.GetData<Color>();
                _readbackTex.SetPixels(data.ToArray());
                _readbackTex.Apply();
            });
        }

        // Sample from the cached texture (previous frame's data)
        if (_readbackTex == null) return Vector3.zero;

        Vector3 local = worldPos - oceanOrigin.position;
        float2 uv = new float2(
            (local.x / patchSize) + 0.5f,
            (local.z / patchSize) + 0.5f
        );

        int ix = Mathf.FloorToInt(uv.x * resolution) % resolution;
        int iy = Mathf.FloorToInt(uv.y * resolution) % resolution;
        if (ix < 0) ix += resolution;
        if (iy < 0) iy += resolution;

        Color c = _readbackTex.GetPixel(ix, iy);
        return new Vector3(c.r, c.g, c.b);
    }
}
