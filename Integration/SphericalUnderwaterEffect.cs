using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Underwater rendering effect for spherical ocean worlds.
/// Handles fog, caustics, and depth-based coloring when camera is submerged.
/// </summary>
[RequireComponent(typeof(Camera))]
[ExecuteAlways]
public class SphericalUnderwaterEffect : MonoBehaviour
{
    [Header("References")]
    public SphericalOceanRenderer oceanRenderer;

    [Header("Underwater Fog")]
    public Color fogColor = new Color(0f, 0.02f, 0.1f, 1f);
    [Range(0.01f, 5f)] public float fogDensity = 0.5f;
    [Range(0f, 1f)] public float fogAbsorption = 0.8f;

    [Header("Caustics")]
    public Texture2D causticsTexture;
    [Range(0f, 10f)] public float causticsStrength = 3.2f;
    public float causticsScale = 5f;
    public float causticsFocalDepth = 2f;
    public float causticsDepthOfField = 0.33f;
    public float causticsAnimationSpeed = 1f;

    [Header("Lighting")]
    public Color underwaterAmbient = new Color(0.1f, 0.2f, 0.3f, 1f);
    [Range(0f, 1f)] public float lightAttenuation = 0.5f;
    public float lightDepthFalloff = 10f;

    [Header("Color Grading")]
    public Color underwaterTintColor = new Color(0.3f, 0.6f, 0.8f, 1f);
    [Range(0f, 2f)] public float saturationBoost = 1.2f;
    [Range(0f, 2f)] public float contrastBoost = 1.1f;

    [Header("State")]
    [SerializeField] private bool _isUnderwater;
    [SerializeField] private float _depthBelowSurface;

    private Camera _camera;
    private Material _underwaterMaterial;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        // Create a simple post-process material for underwater tinting
        Shader shader = Shader.Find("Hidden/SphericalOcean/UnderwaterPostProcess");
        if (shader != null)
        {
            _underwaterMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
    }

    private void OnDisable()
    {
        if (_underwaterMaterial != null)
        {
            DestroyImmediate(_underwaterMaterial);
            _underwaterMaterial = null;
        }
    }

    private void Update()
    {
        if (oceanRenderer == null) return;

        Vector3 center = oceanRenderer.GetPlanetCenter();
        float camDist = Vector3.Distance(transform.position, center);
        float surfaceHeight = oceanRenderer.GetSurfaceHeight(transform.position);
        _isUnderwater = camDist < surfaceHeight;
        _depthBelowSurface = surfaceHeight - camDist;
    }

    /// <summary>
    /// Get fog color at a given depth below the surface.
    /// </summary>
    public Color GetFogColor(float depthBelowSurface)
    {
        float fogFactor = 1f - Mathf.Exp(-fogAbsorption * depthBelowSurface);
        return Color.Lerp(underwaterTintColor, fogColor, fogFactor);
    }

    /// <summary>
    /// Get light intensity at a given depth below the surface.
    /// </summary>
    public float GetLightIntensity(float depthBelowSurface)
    {
        return Mathf.Exp(-lightAttenuation * depthBelowSurface / lightDepthFalloff);
    }

    /// <summary>
    /// Sample caustics at a world position.
    /// </summary>
    public float SampleCaustics(Vector3 worldPos, float depthBelowSurface)
    {
        if (causticsTexture == null) return 0f;

        float2 uv = worldPos.xz / causticsScale;
        float time = Time.time * causticsAnimationSpeed;

        float c1 = causticsTexture.GetPixelBilinear(
            math.fmod((uv.x + time * 0.044f), 1f),
            math.fmod((uv.y - time * 0.169f), 1f)).r;
        float c2 = causticsTexture.GetPixelBilinear(
            math.fmod((uv.x * 1.37f + time * 0.248f), 1f),
            math.fmod((uv.y * 1.37f + time * 0.117f), 1f)).r;

        float caustics = 0.5f * (c1 + c2);
        float attenuation = GetLightIntensity(depthBelowSurface);
        return causticsStrength * caustics * attenuation;
    }

    /// <summary>
    /// Whether the camera is currently underwater.
    /// </summary>
    public bool IsUnderwater => _isUnderwater;

    /// <summary>
    /// Current depth of camera below the water surface.
    /// </summary>
    public float DepthBelowSurface => _depthBelowSurface;
}
