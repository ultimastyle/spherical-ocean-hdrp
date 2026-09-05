using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Core renderer for spherical ocean worlds. Manages wave simulation, foam,
/// and GPU-driven vertex displacement on a true icosphere mesh.
/// Adapted from Crest Ocean System (MIT) for spherical/planetary rendering.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteAlways]
[DefaultExecutionOrder(-100)]
public class SphericalOceanRenderer : MonoBehaviour
{
    [Header("Planet")]
    [Tooltip("Transform of the planet center. If null, uses this transform.")]
    public Transform planetCenter;
    [Tooltip("World-space radius of the ocean surface.")]
    public float oceanRadius = 420f;
    [Tooltip("Sea level radius — water fills terrain below this.")]
    public float seaLevelRadius = 417f;

    [Header("Mesh")]
    [Range(0, 6)] public int icosphereSubdivisions = 3;
    [Range(1, 8)] public int octaveCount = 5;
    public float maxWaveAmplitude = 50f;
    public float minWaveAmplitude = 0.1f;

    [Header("Wave Spectrum")]
    public float windSpeed = 10f;
    public float windDirection = 0f;
    [Range(0f, 1f)] public float waveChoppiness = 0.8f;
    [Range(0.01f, 2f)] public float waveScale = 1f;
    [Range(0.01f, 5f)] public float waveSpeed = 1f;
    [Tooltip("Scales wave physics to match planet radius. Increase for smaller planets.")]
    public float worldScale = 1f;

    [Header("Foam")]
    public bool enableFoam = true;
    [Range(0f, 2f)] public float foamIntensity = 1f;
    [Range(0.01f, 50f)] public float foamScale = 10f;
    public Texture2D foamTexture;
    [Range(0.001f, 1f)] public float foamFeather = 0.4f;
    [Range(0.01f, 5f)] public float shorelineFoamMinDepth = 0.27f;
    public Color foamWhiteColor = Color.white;

    [Header("Subsurface Scattering")]
    public bool enableSSS = true;
    [ColorUsage(false, true)] public Color sssColor = new Color(0.088f, 0.497f, 0.456f, 1f);
    [Range(0f, 4f)] public float sssBase = 0f;
    [Range(0f, 10f)] public float sssIntensity = 1.7f;
    [Range(1f, 16f)] public float sssFalloff = 5f;
    [Range(0.01f, 50f)] public float shallowDepthMax = 10f;
    [Range(0.01f, 10f)] public float shallowDepthPower = 2.5f;
    [ColorUsage(false, true)] public Color shallowColor = new Color(0f, 0.0039f, 0.247f, 1f);

    [Header("Caustics")]
    public bool enableCaustics = true;
    public Texture2D causticsTexture;
    [Range(0f, 25f)] public float causticsTextureScale = 5f;
    [Range(0f, 1f)] public float causticsTextureAverage = 0.07f;
    [Range(0f, 10f)] public float causticsStrength = 3.2f;
    public float causticsFocalDepth = 2f;
    public float causticsDepthOfField = 0.33f;

    [Header("Normals")]
    public Texture2D normalMap;
    [Range(0.01f, 200f)] public float normalScale = 40f;
    [Range(0.01f, 2f)] public float normalStrength = 0.36f;
    [Range(0f, 1f)] public float normalsStrengthOverall = 1f;

    [Header("Scattering")]
    [ColorUsage(false, true)] public Color scatterBase = new Color(0f, 0.0027f, 0.17f, 1f);
    [ColorUsage(false, true)] public Color scatterGrazing = new Color(0f, 0.0039f, 0.169f, 1f);
    [ColorUsage(false, true)] public Color scatterShadow = new Color(0f, 0.0013f, 0.085f, 1f);
    [Range(0f, 10f)] public float scatterAmount = 3.5f;
    public Color scatterColor = new Color(0f, 1f, 0.95f, 1f);
    [Range(0f, 1f)] public float scatterFade = 0.5f;

    [Header("Reflections")]
    [Range(0f, 2f)] public float specular = 0.7f;
    [Range(0f, 1f)] public float specularMinRoughness = 0.02f;
    [Range(1f, 20f)] public float fresnelPower = 5f;
    [Range(1f, 2f)] public float refractiveIndexAir = 1f;
    [Range(1f, 2f)] public float refractiveIndexWater = 1.333f;
    public bool useExactFresnel = true;

    [Header("Directional Light")]
    [Range(0f, 512f)] public float directionalLightBoost = 7f;
    [Range(1f, 4096f)] public float directionalLightFallOff = 275f;

    [Header("Transparency")]
    public bool enableTransparency = true;
    public Vector4 depthFogDensity = new Vector4(0.9f, 0.3f, 0.35f, 1f);
    [Range(0f, 2f)] public float refractionStrength = 0.5f;
    [Range(0f, 0.02f)] public float aberrationAmount = 0.002f;

    [Header("Water Volume")]
    public float visibility = 28f;
    public Vector3 waterExtinction = new Vector3(0.6f, 0.8f, 1f);
    public Vector3 sunTransmittance = new Vector3(0.45f, 0.55f, 0.68f);
    public Color waterColor = new Color(0.0078f, 0.5176f, 0.7f, 1f);
    [Range(0f, 100f)] public float horizonFog = 50f;

    [Header("Dynamic Waves")]
    public bool enableDynamicWaves = true;
    [Range(0.8f, 1f)] public float fluxDamping = 0.96f;
    [Range(1, 4)] public int subSteps = 2;

    [Header("Shadows")]
    public bool enableShadows = true;

    public static SphericalOceanRenderer Instance { get; private set; }

    private NativeArray<float> _depth;
    private NativeArray<float4> _flux;
    private NativeArray<float2> _vel;
    private NativeArray<float4> _packed;
    private NativeArray<byte> _src;
    private bool _alloc;

    private Material _material;
    private MaterialPropertyBlock _propBlock;
    private MeshFilter _mf;
    private MeshRenderer _mr;
    private Mesh _mesh;
    private bool _ready;

    // Track last synced values to avoid redundant SetFloat calls
    private float _lastTime = float.NaN;

    private const float G = 9.81f;
    private const float PI = 3.14159265f;

    private void Awake()
    {
        Instance = this;
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        _mesh = new Mesh { name = "SphericalOcean", indexFormat = IndexFormat.UInt32 };
        _mf.sharedMesh = _mesh;
        _propBlock = new MaterialPropertyBlock();
    }

    private void Start()
    {
        Allocate();
        BuildIcosphere();
        EnsureMaterial();
        InitFluid();
        _ready = true;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        DisposeArrays();
    }

    private void Update()
    {
        if (!_ready) return;

        float time = Application.isPlaying ? Time.time : (float)GetEditorTime();

        // Only update time and per-frame globals — skip full property sync
        _mr.GetPropertyBlock(_propBlock);
        _propBlock.SetVector("_OceanCenterPosWorld", GetPlanetCenter());
        _propBlock.SetFloat("_CrestTime", time);
        _mr.SetPropertyBlock(_propBlock);

        _lastTime = time;
    }

    private void FixedUpdate()
    {
        if (!_ready || !Application.isPlaying) return;

        float baseDt = Time.fixedDeltaTime / Mathf.Max(1, subSteps);
        float dt = Mathf.Min(baseDt, 0.45f);
        float cellSize = 2f * PI * oceanRadius / Mathf.Sqrt(_depth.Length);

        for (int s = 0; s < subSteps; s++)
        {
            var flux = new FluxJob
            {
                n = (int)Mathf.Sqrt(_depth.Length),
                dt = dt,
                cellSize = cellSize,
                g = G,
                damping = fluxDamping,
                seaR = seaLevelRadius,
                depth = _depth,
                flux = _flux
            }.Schedule(_depth.Length, 256);

            var scale = new ScaleJob
            {
                dt = dt,
                cellSize = cellSize,
                depth = _depth,
                flux = _flux
            }.Schedule(_depth.Length, 256, flux);

            var apply = new ApplyJob
            {
                n = (int)Mathf.Sqrt(_depth.Length),
                dt = dt,
                cellSize = cellSize,
                seaR = seaLevelRadius,
                ground = _depth,
                flux = _flux,
                src = _src,
                depth = _depth,
                vel = _vel
            }.Schedule(_depth.Length, 256, scale);

            apply.Complete();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_material != null)
            SyncMaterialProperties();
    }
#endif

    // --- Public API ---

    public void MarkDirty() { }

    public float GetSurfaceHeight(Vector3 worldPos)
    {
        return seaLevelRadius;
    }

    public bool IsUnderwater(Vector3 worldPos)
    {
        Vector3 center = GetPlanetCenter();
        return Vector3.Distance(worldPos, center) < GetSurfaceHeight(worldPos);
    }

    public Vector3 GetPlanetCenter()
    {
        return planetCenter != null ? planetCenter.position : transform.position;
    }

    // --- True Icosphere mesh generation ---

    private void BuildIcosphere()
    {
        // Golden ratio
        float t = (1f + Mathf.Sqrt(5f)) / 2f;

        // 12 vertices of an icosahedron
        var verts = new List<Vector3>
        {
            new Vector3(-1,  t,  0).normalized * seaLevelRadius,
            new Vector3( 1,  t,  0).normalized * seaLevelRadius,
            new Vector3(-1, -t,  0).normalized * seaLevelRadius,
            new Vector3( 1, -t,  0).normalized * seaLevelRadius,
            new Vector3( 0, -1,  t).normalized * seaLevelRadius,
            new Vector3( 0,  1,  t).normalized * seaLevelRadius,
            new Vector3( 0, -1, -t).normalized * seaLevelRadius,
            new Vector3( 0,  1, -t).normalized * seaLevelRadius,
            new Vector3( t,  0, -1).normalized * seaLevelRadius,
            new Vector3( t,  0,  1).normalized * seaLevelRadius,
            new Vector3(-t,  0, -1).normalized * seaLevelRadius,
            new Vector3(-t,  0,  1).normalized * seaLevelRadius,
        };

        // 20 faces of an icosahedron
        var tris = new List<int>
        {
            0,11,5,  0,5,1,   0,1,7,   0,7,10,  0,10,11,
            1,5,9,   5,11,4,  11,10,2,  10,7,6,  7,1,8,
            3,9,4,   3,4,2,   3,2,6,   3,6,8,   3,8,9,
            4,9,5,   2,4,11,  6,2,10,  8,6,7,   9,8,1,
        };

        // Subdivide
        var midpointCache = new Dictionary<long, int>();
        for (int i = 0; i < icosphereSubdivisions; i++)
        {
            var newTris = new List<int>();
            for (int j = 0; j < tris.Count; j += 3)
            {
                int a = tris[j];
                int b = tris[j + 1];
                int c = tris[j + 2];

                int ab = GetMidpoint(a, b, verts, midpointCache);
                int bc = GetMidpoint(b, c, verts, midpointCache);
                int ca = GetMidpoint(c, a, verts, midpointCache);

                newTris.AddRange(new[] { a, ab, ca });
                newTris.AddRange(new[] { b, bc, ab });
                newTris.AddRange(new[] { c, ca, bc });
                newTris.AddRange(new[] { ab, bc, ca });
            }
            tris = newTris;
        }

        // Compute UVs (spherical lat/lon for texture mapping, not for wave phase)
        var uvs = new Vector2[verts.Count];
        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 n = verts[i].normalized;
            float lon = Mathf.Atan2(n.x, n.z);
            float lat = Mathf.Asin(n.y);
            uvs[i] = new Vector2(
                (lon / (2f * PI)) + 0.5f,
                (lat / PI) + 0.5f);
        }

        _mesh.Clear();
        _mesh.SetVertices(verts);
        _mesh.SetUVs(0, uvs);
        _mesh.SetTriangles(tris, 0);
        _mesh.RecalculateNormals();
        _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * oceanRadius * 3f);
    }

    private int GetMidpoint(int a, int b, List<Vector3> verts, Dictionary<long, int> cache)
    {
        long key = ((long)Mathf.Min(a, b) << 32) + Mathf.Max(a, b);
        if (cache.TryGetValue(key, out int idx)) return idx;

        Vector3 mid = ((verts[a] + verts[b]) * 0.5f).normalized * seaLevelRadius;
        idx = verts.Count;
        verts.Add(mid);
        cache[key] = idx;
        return idx;
    }

    // --- Fluid simulation ---

    private void Allocate()
    {
        int n = Mathf.Max(32, (int)Mathf.Pow(2, icosphereSubdivisions + 4));
        int nc = n * n;

        if (_alloc && _depth.Length != nc) DisposeArrays();
        if (!_alloc)
        {
            _depth = new NativeArray<float>(nc, Allocator.Persistent);
            _flux = new NativeArray<float4>(nc, Allocator.Persistent);
            _vel = new NativeArray<float2>(nc, Allocator.Persistent);
            _src = new NativeArray<byte>(nc, Allocator.Persistent);
            _packed = new NativeArray<float4>(nc, Allocator.Persistent);
            _alloc = true;
        }
    }

    private void DisposeArrays()
    {
        if (!_alloc) return;
        _depth.Dispose();
        _flux.Dispose();
        _vel.Dispose();
        _src.Dispose();
        _packed.Dispose();
        _alloc = false;
    }

    private void InitFluid()
    {
        for (int i = 0; i < _depth.Length; i++)
        {
            _flux[i] = float4.zero;
            _vel[i] = float2.zero;
            _depth[i] = Mathf.Max(0f, seaLevelRadius);
        }
    }

    private void EnsureMaterial()
    {
        _material = _mr.sharedMaterial;
        if (_material == null || _material.shader.name != "SphericalOcean/HDRP")
        {
            Shader shader = Shader.Find("SphericalOcean/HDRP");
            if (shader != null)
            {
                _material = new Material(shader) { name = "SphericalOceanMat" };
                _mr.sharedMaterial = _material;
            }
        }

        if (_material != null)
        {
            SyncMaterialProperties();

            if (normalMap != null) _material.SetTexture("_Normals", normalMap);
            if (foamTexture != null) _material.SetTexture("_FoamTexture", foamTexture);
            if (causticsTexture != null) _material.SetTexture("_CausticsTexture", causticsTexture);

            _mr.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    /// <summary>
    /// Sync all inspector values to the material. Call when properties change.
    /// </summary>
    public void SyncMaterialProperties()
    {
        if (_material == null) return;

        _material.SetFloat("_WindSpeed", windSpeed);
        _material.SetFloat("_WindDirection", windDirection);
        _material.SetFloat("_WaveChoppiness", waveChoppiness);
        _material.SetFloat("_WaveScale", waveScale);
        _material.SetFloat("_WaveSpeed", waveSpeed);
        _material.SetFloat("_MaxWaveAmplitude", maxWaveAmplitude);
        _material.SetFloat("_WorldScale", worldScale);

        _material.SetFloat("_NormalsStrengthOverall", normalsStrengthOverall);
        _material.SetFloat("_NormalsStrength", normalStrength);
        _material.SetFloat("_NormalsScale", normalScale);

        _material.SetColor("_Diffuse", scatterBase);
        _material.SetColor("_DiffuseGrazing", scatterGrazing);
        _material.SetColor("_DiffuseShadow", scatterShadow);
        _material.SetFloat("_ScatterAmount", scatterAmount);
        _material.SetColor("_ScatterColor", scatterColor);
        _material.SetFloat("_ScatterFade", scatterFade);

        _material.SetColor("_SubSurfaceColour", sssColor);
        _material.SetFloat("_SubSurfaceBase", sssBase);
        _material.SetFloat("_SubSurfaceSun", sssIntensity);
        _material.SetFloat("_SubSurfaceSunFallOff", sssFalloff);
        _material.SetFloat("_SubSurfaceDepthMax", shallowDepthMax);
        _material.SetFloat("_SubSurfaceDepthPower", shallowDepthPower);
        _material.SetColor("_SubSurfaceShallowCol", shallowColor);

        _material.SetFloat("_Specular", specular);
        _material.SetFloat("_SpecularMinRoughness", specularMinRoughness);
        _material.SetFloat("_FresnelPower", fresnelPower);
        _material.SetFloat("_RefractiveIndexOfAir", refractiveIndexAir);
        _material.SetFloat("_RefractiveIndexOfWater", refractiveIndexWater);
        _material.SetFloat("_UseExactFresnel", useExactFresnel ? 1f : 0f);

        _material.SetFloat("_DirectionalLightFallOff", directionalLightFallOff);
        _material.SetFloat("_DirectionalLightBoost", directionalLightBoost);

        _material.SetFloat("_FoamScale", foamScale);
        _material.SetColor("_FoamWhiteColor", foamWhiteColor);
        _material.SetFloat("_WaveFoamFeather", foamFeather);
        _material.SetFloat("_ShorelineFoamMinDepth", shorelineFoamMinDepth);
        _material.SetFloat("_FoamIntensity", foamIntensity);

        _material.SetVector("_DepthFogDensity", depthFogDensity);
        _material.SetFloat("_RefractionStrength", refractionStrength);
        _material.SetFloat("_AberrationAmount", aberrationAmount);

        _material.SetFloat("_CausticsTextureScale", causticsTextureScale);
        _material.SetFloat("_CausticsTextureAverage", causticsTextureAverage);
        _material.SetFloat("_CausticsStrength", causticsStrength);
        _material.SetFloat("_CausticsFocalDepth", causticsFocalDepth);
        _material.SetFloat("_CausticsDepthOfField", causticsDepthOfField);

        _material.SetFloat("_Visibility", visibility);
        _material.SetVector("_WaterExtinction", waterExtinction);
        _material.SetVector("_SunTransmittance", sunTransmittance);
        _material.SetColor("_WaterColor", waterColor);
        _material.SetFloat("_HorizonFog", horizonFog);

        // Keywords
        SetKeyword("ENABLE_FOAM", enableFoam);
        SetKeyword("ENABLE_SSS", enableSSS);
        SetKeyword("ENABLE_CAUSTICS", enableCaustics);
        SetKeyword("ENABLE_TRANSPARENCY", enableTransparency);
        SetKeyword("ENABLE_NORMALS", normalMap != null);
        SetKeyword("ENABLE_SHADOWS", enableShadows);
    }

    private void SetKeyword(string keyword, bool on)
    {
        if (_material == null) return;
        if (on) _material.EnableKeyword(keyword);
        else _material.DisableKeyword(keyword);
    }

#if UNITY_EDITOR
    private double GetEditorTime() => UnityEditor.EditorApplication.timeSinceStartup;
#else
    private double GetEditorTime() => 0.0;
#endif

    // --- Burst Jobs ---

    [Unity.Burst.BurstCompile]
    private struct FluxJob : IJobParallelFor
    {
        public int n;
        public float dt, cellSize, g, damping, seaR;
        [ReadOnly] public NativeArray<float> depth;
        public NativeArray<float4> flux;

        public void Execute(int i)
        {
            int x = i % n, y = i / n;
            float hi = depth[i];
            float4 f = flux[i] * damping;

            f.x = Step(f.x, hi, x - 1, y, x > 0);
            f.y = Step(f.y, hi, x + 1, y, x < n - 1);
            f.z = Step(f.z, hi, x, y - 1, y > 0);
            f.w = Step(f.w, hi, x, y + 1, y < n - 1);

            flux[i] = math.max(0f, f);
        }

        private float Step(float prev, float hi, int nx, int ny, bool inside)
        {
            float hj = inside ? depth[ny * n + nx] : seaR;
            float dh = hi - hj;
            return prev + dt * cellSize * g * dh / cellSize;
        }
    }

    [Unity.Burst.BurstCompile]
    private struct ScaleJob : IJobParallelFor
    {
        public float dt, cellSize;
        [ReadOnly] public NativeArray<float> depth;
        public NativeArray<float4> flux;

        public void Execute(int i)
        {
            float4 f = flux[i];
            float outSum = f.x + f.y + f.z + f.w;
            if (outSum <= 1e-7f) return;
            float avail = depth[i] * cellSize * cellSize;
            float want = outSum * dt;
            if (want > avail) flux[i] = f * (avail / want);
        }
    }

    [Unity.Burst.BurstCompile]
    private struct ApplyJob : IJobParallelFor
    {
        public int n;
        public float dt, cellSize, seaR;
        [ReadOnly] public NativeArray<float> ground;
        [ReadOnly] public NativeArray<float4> flux;
        [ReadOnly] public NativeArray<byte> src;
        public NativeArray<float> depth;
        public NativeArray<float2> vel;

        public void Execute(int i)
        {
            int x = i % n, y = i / n;

            if (src[i] != 0)
            {
                depth[i] = math.max(0f, seaR - ground[i]);
                vel[i] = float2.zero;
                return;
            }

            float fOut = flux[i].x + flux[i].y + flux[i].z + flux[i].w;
            float fIn = 0f;
            if (x > 0) fIn += flux[i - 1].y;
            if (x < n - 1) fIn += flux[i + 1].x;
            if (y > 0) fIn += flux[i - n].w;
            if (y < n - 1) fIn += flux[i + n].z;

            float d = depth[i] + dt * (fIn - fOut) / (cellSize * cellSize);
            depth[i] = math.max(0f, d);

            float uNet = 0.5f * ((flux[i].y - flux[i].x)
                + ((x > 0 ? flux[i - 1].y : 0f) - (x < n - 1 ? flux[i + 1].x : 0f)));
            float vNet = 0.5f * ((flux[i].w - flux[i].z)
                + ((y > 0 ? flux[i - n].w : 0f) - (y < n - 1 ? flux[i + n].z : 0f)));
            float dd = math.max(0.05f, depth[i]);
            vel[i] = new float2(uNet, vNet) / (dd * cellSize);
        }
    }
}
