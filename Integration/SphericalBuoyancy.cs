using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Spherical buoyancy system for planetary ocean worlds.
/// Applies radial buoyancy forces relative to planet center.
/// </summary>
public class SphericalBuoyancy : MonoBehaviour
{
    [Header("References")]
    public SphericalOceanRenderer oceanRenderer;

    [Header("Buoyancy")]
    [Tooltip("Force applied to push objects toward the water surface.")]
    public float buoyancyForce = 15f;
    [Tooltip("Drag applied when object is in water.")]
    public float waterDrag = 2.5f;
    [Tooltip("Drag applied when object is in air.")]
    public float airDrag = 0.5f;
    [Tooltip("Wave influence multiplier on buoyancy.")]
    public float waveInfluence = 1f;

    [Header("Bobbing")]
    [Tooltip("Enable wave-based bobbing motion.")]
    public bool enableBobbing = true;
    public float bobbingFrequency = 1f;
    public float bobbingAmplitude = 0.3f;

    [Header("Rotation")]
    [Tooltip("Enable surface-aligned rotation.")]
    public bool enableSurfaceAlignment = true;
    public float alignmentSpeed = 2f;

    private Rigidbody _rb;
    private Vector3 _lastBuoyancyPoint;
    private float _buoyancyFactor;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
        {
            _rb = gameObject.AddComponent<Rigidbody>();
            _rb.useGravity = true;
            _rb.mass = 10f;
            _rb.linearDamping = airDrag;
        }
    }

    private void FixedUpdate()
    {
        if (oceanRenderer == null || _rb == null) return;

        Vector3 center = oceanRenderer.GetPlanetCenter();
        Vector3 toCenter = center - transform.position;
        float distFromCenter = toCenter.magnitude;
        float surfaceRadius = oceanRenderer.seaLevelRadius;

        float waveHeight = GetWaveHeight(transform.position);
        float waterSurfaceRadius = surfaceRadius + waveHeight;

        bool submerged = distFromCenter < waterSurfaceRadius;

        if (submerged)
        {
            float depthBelowSurface = waterSurfaceRadius - distFromCenter;
            float submersionFactor = Mathf.Clamp01(depthBelowSurface / 2f);

            Vector3 radialDir = (transform.position - center).normalized;
            float buoyancyMagnitude = buoyancyForce * submersionFactor * waveInfluence;

            _rb.AddForce(radialDir * buoyancyMagnitude, ForceMode.Acceleration);

            _rb.linearDamping = Mathf.Lerp(waterDrag, airDrag, 1f - submersionFactor);

            if (enableBobbing)
            {
                float bobPhase = Mathf.Sin(Time.time * bobbingFrequency + distFromCenter * 0.1f);
                _rb.AddForce(radialDir * bobPhase * bobbingAmplitude, ForceMode.Acceleration);
            }

            if (enableSurfaceAlignment)
            {
                Quaternion targetRot = Quaternion.FromToRotation(transform.up, radialDir) * transform.rotation;
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRot, Time.fixedDeltaTime * alignmentSpeed));
            }

            _buoyancyFactor = submersionFactor;
        }
        else
        {
            _rb.linearDamping = airDrag;
            _buoyancyFactor = 0f;
        }

        _lastBuoyancyPoint = transform.position;
    }

    private float GetWaveHeight(Vector3 worldPos)
    {
        if (oceanRenderer == null) return 0f;

        float time = Time.time;
        float height = 0f;

        float2 windDir = new float2(
            Mathf.Cos(oceanRenderer.windDirection),
            Mathf.Sin(oceanRenderer.windDirection));

        float2 pos2D = worldPos.xz;

        for (int i = 0; i < 5; i++)
        {
            float frequency = (i + 1) * oceanRenderer.waveScale * 0.1f;
            float amplitude = oceanRenderer.maxWaveAmplitude * Mathf.Exp(-i * 0.5f) * oceanRenderer.waveScale;

            float2 k = windDir * frequency;
            float phase = math.dot(k, pos2D) - time * oceanRenderer.waveSpeed * frequency;

            height += amplitude * Mathf.Sin(phase);
        }

        return height * oceanRenderer.waveChoppiness;
    }

    public float BuoyancyFactor => _buoyancyFactor;

    public bool IsSubmerged => _buoyancyFactor > 0f;

    public float DepthBelowSurface
    {
        get
        {
            if (oceanRenderer == null) return 0f;
            Vector3 center = oceanRenderer.GetPlanetCenter();
            float dist = Vector3.Distance(transform.position, center);
            return oceanRenderer.seaLevelRadius - dist;
        }
    }
}
