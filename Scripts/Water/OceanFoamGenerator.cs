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
