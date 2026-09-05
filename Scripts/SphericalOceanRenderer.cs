using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Core renderer for spherical ocean worlds. Manages wave simulation, foam,
/// and GPU-driven vertex displacement on a true icosphere mesh.
/// Adapted from Crest Ocean System (MIT) for spherical/planetary rendering.
///
/// Merged with OpenOceanPhysics: supports FFT wave cascades, Burst Gerstner,
/// Jacobian foam, and distance-based cascade blending.
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
    [Range(0, 6)] public int icosphereSubdivisions = 4;
    [Range(1, 8)] public int octaveCount = 5;
    public float maxWaveAmplitude = 10f;
    public float minWaveAmplitude = 0.1f;

    [Header("Wave Spectrum")]
    public float windSpeed = 10f;
    public float windDirection = 0f;
    [Range(0f, 1f)] public float waveChoppiness = 0.8f;
    [Range(0.01f, 2f)] public float waveScale = 1f;
    [Range(0.01f, 5f)] public float waveSpeed = 1f;
    [Tooltip("Scales wave physics to match planet radius. Increase for smaller planets.")]
    public float worldScale = 1f;

    [Header("FFT Wave Cascades")]
    [Tooltip("Enable GPU FFT wave simulation cascades. Adds realistic ocean swells.")]
    public bool enableFFTCascades = true;
    [Tooltip("Cascade configurations. Typically 2-4 cascades for far/mid/near detail.")]
    public WaveCascadeData cascadeData;
    [Tooltip("Max number of cascades to use.")]
    [Range(1, 4)] public int maxCascades = 3;

    [Header("Burst Gerstner Detail")]
    [Tooltip("Enable Burst job Gerstner waves for high-frequency detail on top of FFT.")]
    public bool enableGerstnerDetail = true;
    public SphericalGerstnerWaves gerstnerWaves;

    [Header("Foam")]
    public bool enableFoam = true;
    [Range(0f, 2f)] public float foamIntensity = 1.5f;
    [Range(0.01f, 50f)] public float foamScale = 12f;
    public Texture2D foamTexture;
    [Range(0.001f, 1f)] public float foamFeather = 0.35f;
    [Range(0.01f, 5f)] public float shorelineFoamMinDepth = 0.2f;
    public Color foamWhiteColor = Color.white;

    [Header("Subsurface Scattering")]
    public bool enableSSS = true;
    [ColorUsage(false, true)] public Color sssColor = new Color(0.0f, 0.55f, 0.5f, 1f);
    [Range(0f, 4f)] public float sssBase = 0.3f;
    [Range(0f, 10f)] public float sssIntensity = 1.2f;
    [Range(1f, 16f)] public float sssFalloff = 4f;
    [Range(0.01f, 50f)] public float shallowDepthMax = 15f;
    [Range(0.01f, 10f)] public float shallowDepthPower = 2f;
    [ColorUsage(false, true)] public Color shallowColor = new Color(0.0f, 0.7f, 0.65f, 1f);

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
    [Range(0.01f, 200f)] public float normalScale = 80f;
    [Range(0.01f, 2f)] public float normalStrength = 0.4f;
    [Range(0f, 1f)] public float normalsStrengthOverall = 0.85f;

    [Header("Scattering")]
    [ColorUsage(false, true)] public Color scatterBase = new Color(0.0f, 0.01f, 0.12f, 1f);
    [ColorUsage(false, true)] public Color scatterGrazing = new Color(0.0f, 0.008f, 0.1f, 1f);
    [ColorUsage(false, true)] public Color scatterShadow = new Color(0.0f, 0.003f, 0.06f, 1f);
    [Range(0f, 10f)] public float scatterAmount = 2f;
    public Color scatterColor = new Color(0.0f, 0.9f, 0.7f, 1f);
    [Range(0f, 1f)] public float scatterFade = 0.6f;

    [Header("Reflections")]
    [Range(0f, 2f)] public float specular = 0.8f;
    [Range(0f, 1f)] public float specularMinRoughness = 0.015f;
    [Range(1f, 20f)] public float fresnelPower = 5f;
    [Range(1f, 2f)] public float refractiveIndexAir = 1f;
    [Range(1f, 2f)] public float refractiveIndexWater = 1.333f;
    public bool useExactFresnel = true;

    [Header("Directional Light")]
    [Range(0f, 512f)] public float directionalLightBoost = 2f;
    [Range(1f, 4096f)] public float directionalLightFallOff = 300f;

    [Header("Transparency")]
    public bool enableTransparency = true;
    public Vector4 depthFogDensity = new Vector4(0.7f, 0.25f, 0.3f, 1f);
    [Range(0f, 2f)] public float refractionStrength = 0.8f;
    [Range(0f, 0.02f)] public float aberrationAmount = 0.0005f;

    [Header("Water Volume")]
    public float visibility = 40f;
    public Vector3 waterExtinction = new Vector3(0.5f, 0.7f, 0.9f);
    public Vector3 sunTransmittance = new Vector3(0.5f, 0.6f, 0.75f);
    public Color waterColor = new Color(0.01f, 0.45f, 0.65f, 1f);
    [Range(0f, 100f)] public float horizonFog = 60f;

    [Header("Sky")]
    public Cubemap skyCubemap;
    [Range(0f, 5f)] public float skyIntensity = 1f;

    [Header("Shadows")]
    public bool enableShadows = true;

    public static SphericalOceanRenderer Instance { get; private set; }

    private Material _material;
    private bool _createdMaterial;
    private MaterialPropertyBlock _propBlock;
    private MeshFilter _mf;
    private MeshRenderer _mr;
    private Mesh _mesh;
    private bool _ready;

    // Track last synced values to avoid redundant SetFloat calls
    private float _lastTime = float.NaN;

    // FFT cascade instances
    private readonly List<FFTWaveSimulation> _cascadeSims = new List<FFTWaveSimulation>();
    private const int MAX_CASCADES = 4;
    private static readonly int[] ID_CascadeDisp = new int[MAX_CASCADES];
    private static readonly int[] ID_CascadeFoam = new int[MAX_CASCADES];
    private static readonly int[] ID_CascadeJac = new int[MAX_CASCADES];
    private static readonly int ID_CascadeCount = Shader.PropertyToID("_OceanCascadeCount");
    private static readonly int ID_CascadeWeights = Shader.PropertyToID("_OceanCascadeWeights");

    // Foam generator
    private OceanFoamGenerator _foamGenerator;

    static SphericalOceanRenderer()
    {
        for (int i = 0; i < MAX_CASCADES; i++)
        {
            ID_CascadeDisp[i] = Shader.PropertyToID($"_OceanCascadeDisp{i}");
            ID_CascadeFoam[i] = Shader.PropertyToID($"_OceanCascadeFoam{i}");
            ID_CascadeJac[i] = Shader.PropertyToID($"_OceanCascadeJac{i}");
        }
    }

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
        BuildIcosphere();
        EnsureMaterial();

        if (enableFFTCascades)
            InstantiateCascades();

        if (enableGerstnerDetail && gerstnerWaves == null)
            gerstnerWaves = gameObject.GetComponent<SphericalGerstnerWaves>();

        if (enableFoam && _foamGenerator == null)
            _foamGenerator = gameObject.GetComponent<OceanFoamGenerator>();

        _ready = true;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        // Clean up created material to prevent leaks
        if (_createdMaterial && _material != null)
        {
            if (Application.isPlaying)
                Destroy(_material);
            else
                DestroyImmediate(_material);
            _material = null;
        }
    }

    private void Update()
    {
        if (!_ready) return;

        float time = Application.isPlaying ? Time.time : (float)GetEditorTime();

        // Bind FFT cascade textures and compute cascade weights
        if (enableFFTCascades && _cascadeSims.Count > 0)
        {
            BindCascadeTextures(time);
        }

        // Only update time and per-frame globals — skip full property sync
        _mr.GetPropertyBlock(_propBlock);
        _propBlock.SetVector("_OceanCenterPosWorld", GetPlanetCenter());
        _propBlock.SetFloat("_CrestTime", time);
        _mr.SetPropertyBlock(_propBlock);

        // Also set globally for underwater shader
        Shader.SetGlobalVector("_OceanCenterPosWorld", GetPlanetCenter());

        _lastTime = time;
    }

    /// <summary>
    /// Create FFT simulation instances for each cascade level.
    /// </summary>
    private void InstantiateCascades()
    {
        if (cascadeData == null || cascadeData.cascades == null) return;

        int count = Mathf.Min(cascadeData.cascades.Length, maxCascades);
        for (int i = 0; i < count; i++)
        {
            var config = cascadeData.cascades[i];

            // Create a child GameObject for each cascade sim
            string cascadeName = $"FFTCascade_{config.name}";
            var cascadeGO = new GameObject(cascadeName);
            cascadeGO.transform.SetParent(transform, false);

            var sim = cascadeGO.AddComponent<FFTWaveSimulation>();
            sim.resolution = config.resolution;
            sim.patchSize = config.patchSize;
            sim.windSpeed = config.windSpeed;
            sim.windDirection = config.windDirection;
            sim.windAlignment = config.windAlignment;
            sim.spectrumType = config.spectrumType;
            sim.spectrumScale = config.spectrumScale;
            sim.choppiness = config.choppiness;
            sim.gravity = config.gravity;
            sim.foamThreshold = config.foamThreshold;
            sim.foamDecay = config.foamDecay;
            sim.foamGain = config.foamGain;

            _cascadeSims.Add(sim);
        }
    }

    /// <summary>
    /// Bind cascade displacement/foam textures to shader globals and compute blend weights.
    /// </summary>
    private void BindCascadeTextures(float time)
    {
        Camera cam = Camera.main;
        float camDist = cam != null
            ? Vector3.Distance(cam.transform.position, GetPlanetCenter())
            : 1000f;

        float[] weights = new float[MAX_CASCADES];
        float totalWeight = 0f;

        int count = Mathf.Min(_cascadeSims.Count, MAX_CASCADES);
        for (int i = 0; i < count; i++)
        {
            var sim = _cascadeSims[i];
            if (sim == null || sim.DisplacementTexture == null) continue;

            // Set cascade textures
            Shader.SetGlobalTexture(ID_CascadeDisp[i], sim.DisplacementTexture);
            if (sim.FoamTexture != null)
                Shader.SetGlobalTexture(ID_CascadeFoam[i], sim.FoamTexture);
            if (sim.JacobianTexture != null)
                Shader.SetGlobalTexture(ID_CascadeJac[i], sim.JacobianTexture);

            // Compute distance-based weight from cascade config
            if (cascadeData != null && i < cascadeData.cascades.Length)
            {
                var config = cascadeData.cascades[i];
                float t = Mathf.InverseLerp(config.distanceRange.x, config.distanceRange.y, camDist);
                weights[i] = Mathf.Lerp(config.blendIn, config.blendOut, t);

                // Smooth edge fade
                float edgeFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(config.distanceRange.x, config.distanceRange.x + 20f, camDist))
                               * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(config.distanceRange.y, config.distanceRange.y - 20f, camDist));
                weights[i] *= edgeFade;
            }
            else
            {
                weights[i] = 1f;
            }

            totalWeight += weights[i];
        }

        // Normalize weights
        if (totalWeight > 0.001f)
        {
            for (int i = 0; i < MAX_CASCADES; i++)
                weights[i] /= totalWeight;
        }
        else if (count > 0)
        {
            // Fallback: if all weights are zero (e.g., camera outside all cascade ranges),
            // use the first active cascade at full weight
            weights[0] = 1f;
        }

        Shader.SetGlobalInt(ID_CascadeCount, count);
        Shader.SetGlobalVector(ID_CascadeWeights, new Vector4(weights[0], weights[1], weights[2], weights[3]));
    }


#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_material != null)
            SyncMaterialProperties();
    }
#endif

    // --- Public API ---

    public void MarkDirty()
    {
        if (_material != null)
            SyncMaterialProperties();
    }

    public float GetSurfaceHeight(Vector3 worldPos)
    {
        // Use Gerstner waves only for gameplay queries (buoyancy, underwater check).
        // FFT is GPU-only — too heavy for CPU per-query evaluation.
        float height = seaLevelRadius;
        float time = Application.isPlaying ? Time.time : (float)GetEditorTime();
        float2 windDir = new float2(Mathf.Cos(windDirection), Mathf.Sin(windDirection));
        Vector3 center = GetPlanetCenter();
        Vector3 dir = (worldPos - center).normalized;
        float2 pos2D = dir.xz * worldScale;

        for (int i = 0; i < 5; i++)
        {
            float freq = (i + 1) * waveScale * 0.1f;
            float amp = maxWaveAmplitude * Mathf.Exp(-i * 0.5f) * waveScale;
            float2 k = windDir * freq;
            float phase = math.dot(k, pos2D) - time * waveSpeed * freq;
            height += amp * Mathf.Sin(phase);
        }

        return height * waveChoppiness;
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
                _createdMaterial = true;
            }
        }

        if (_material != null)
        {
            SyncMaterialProperties();

            if (normalMap != null) _material.SetTexture("_Normals", normalMap);
            if (foamTexture != null) _material.SetTexture("_FoamTexture", foamTexture);
            if (causticsTexture != null) _material.SetTexture("_CausticsTexture", causticsTexture);
            if (skyCubemap != null) _material.SetTexture("_SkyCubemap", skyCubemap);

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

        _material.SetFloat("_SkyIntensity", skyIntensity);

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
}
