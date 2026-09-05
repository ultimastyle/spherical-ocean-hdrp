using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Gerstner waves on a sphere surface.
/// Uses analytical partial derivatives for normals (no finite-difference sampling).
/// Runs as a Burst job for zero GC per frame.
///
/// For a single Gerstner wave with phase theta = k*d.x*x0 + k*d.z*z0 - omega*t:
///   x = x0 - Q * (kx/k) * A * sin(theta)
///   y = A * cos(theta)
///   z = z0 - Q * (kz/k) * A * sin(theta)
///
/// Tangent vectors T_x0 and T_z0 are computed analytically:
///   T_x0 = (1 - Q*kx^2/k*A*cos, -kx*A*sin, -Q*kx*kz/k*A*cos)
///   T_z0 = (-Q*kz*kx/k*A*cos, -kz*A*sin, 1 - Q*kz^2/k*A*cos)
///   Normal = normalize(cross(T_z0, T_x0))
/// </summary>
public class SphericalGerstnerWaves : MonoBehaviour
{
    [Header("Wave Sets")]
    public WaveSet[] waveSets = DefaultWaveSets();

    [Header("Planet")]
    public float sphereRadius = 417f;
    public Transform planetCenter;

    [Header("Runtime")]
    public bool useSimTime = true;

    private NativeArray<WaveData> _waveData;
    private bool _allocated;
    private float _time;

    public struct VertexResult
    {
        public float3 position;
        public float3 normal;
        public float2 foam;
    }

    [Serializable]
    public struct WaveSet
    {
        public Vector2 direction;
        [Range(0f, 10f)] public float amplitude;
        public float wavelength;
        [Range(0f, 1f)] public float steepness;
        [Range(0.1f, 3f)] public float speed;
        public float phaseOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveData
    {
        public float2 direction;
        public float amplitude;
        public float k;
        public float omega;
        public float steepness;
        public float speed;
        public float phase;
    }

    public static WaveSet[] DefaultWaveSets()
    {
        return new WaveSet[]
        {
            new WaveSet { direction = new Vector2(1f, 0f), amplitude = 2.5f, wavelength = 80f, steepness = 0.4f, speed = 1f },
            new WaveSet { direction = new Vector2(0.7f, 0.7f), amplitude = 1.8f, wavelength = 60f, steepness = 0.35f, speed = 1.1f },
            new WaveSet { direction = new Vector2(0.9f, 0.4f), amplitude = 1.2f, wavelength = 35f, steepness = 0.5f, speed = 1.2f },
            new WaveSet { direction = new Vector2(0.6f, 0.8f), amplitude = 0.9f, wavelength = 25f, steepness = 0.45f, speed = 1.0f },
            new WaveSet { direction = new Vector2(-0.3f, 0.95f), amplitude = 0.7f, wavelength = 20f, steepness = 0.55f, speed = 0.9f },
            new WaveSet { direction = new Vector2(0.8f, -0.6f), amplitude = 0.5f, wavelength = 12f, steepness = 0.6f, speed = 1.3f },
            new WaveSet { direction = new Vector2(-0.5f, 0.86f), amplitude = 0.35f, wavelength = 8f, steepness = 0.5f, speed = 1.1f },
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
            float omega = math.sqrt(g * k);

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
    //  Burst Jobs — analytical normals, O(1) per wave per vertex
    // =========================================================================

    [BurstCompile]
    private struct EvaluateWavesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<WaveData> waves;
        [ReadOnly] public NativeArray<float3> surfacePoints;
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

            // Accumulate tangent vectors for analytical normal
            // T_x0 and T_z0 start as the identity (flat surface tangents)
            float3 Tx = east;   // d(pos)/d(x0) in tangent space
            float3 Tz = north;  // d(pos)/d(z0) in tangent space

            for (int w = 0; w < waves.Length; w++)
            {
                var wave = waves[w];

                float2 waveDir2D = wave.direction;
                float3 waveDir3D = east * waveDir2D.x + north * waveDir2D.y;

                float phase = math.dot(waveDir3D, p) * wave.k - wave.omega * time + wave.phase;

                float sinP = math.sin(phase);
                float cosP = math.cos(phase);

                // Gerstner displacement
                float Q = wave.steepness / (wave.k * wave.amplitude + 1e-6f);
                float QA = Q * wave.amplitude;
                float Ak = wave.amplitude / wave.k;

                totalDisp += wave.amplitude * sinP;
                tangentDisp += QA * waveDir3D * cosP;

                // --- Analytical partial derivatives ---
                // For wave direction d = (dx, dz), wavenumber k, amplitude A:
                //   dDx/dx0 = 1 - Q * dx^2 * k * A * cos(theta)  [east component]
                //   dDy/dx0 = -dx * k * A * sin(theta)           [up component]
                //   dDz/dx0 = -Q * dx * dz * k * A * cos(theta) [north component]
                //
                //   dDx/dz0 = -Q * dz * dx * k * A * cos(theta)
                //   dDy/dz0 = -dz * k * A * sin(theta)
                //   dDz/dz0 = 1 - Q * dz^2 * k * A * cos(theta)

                float kAcos = wave.k * wave.amplitude * cosP;
                float kAsin = wave.k * wave.amplitude * sinP;

                float dx = waveDir2D.x;  // east component of wave dir
                float dz = waveDir2D.y;  // north component of wave dir

                // Tangent T_x0 (derivative w.r.t. east coordinate)
                Tx += new float3(
                    -QA * dx * dx * kAcos / wave.amplitude,   // = -Q * dx^2 * k * A * cos
                    -dx * kAsin,                                // = -dx * k * A * sin
                    -QA * dx * dz * kAcos / wave.amplitude     // = -Q * dx * dz * k * A * cos
                );

                // Tangent T_z0 (derivative w.r.t. north coordinate)
                Tz += new float3(
                    -QA * dz * dx * kAcos / wave.amplitude,   // = -Q * dz * dx * k * A * cos
                    -dz * kAsin,                                // = -dz * k * A * sin
                    1f - QA * dz * dz * kAcos / wave.amplitude // = 1 - Q * dz^2 * k * A * cos
                );
            }

            // Displace radially outward + tangentially
            float3 displacedPos = p + normal * totalDisp + tangentDisp;

            // Normal from analytical tangents: N = cross(Tz, Tx)
            float3 newNormal = math.normalizesafe(math.cross(Tz, Tx));

            results[i] = new VertexResult
            {
                position = displacedPos,
                normal = newNormal,
                foam = new float2(math.saturate(-totalDisp * 0.3f), 0)
            };
        }
    }

    // =========================================================================
    //  Public API
    // =========================================================================

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

    public VertexResult EvaluateSingle(Vector3 baseSurfacePosition)
    {
        var arr = new Vector3[] { baseSurfacePosition };
        var results = Evaluate(arr);
        var result = results[0];
        results.Dispose();
        return result;
    }

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

    public NativeArray<WaveData> GetWaveDataCopy()
    {
        if (!_allocated) return default;
        var copy = new NativeArray<WaveData>(_waveData.Length, Allocator.TempJob);
        NativeArray<WaveData>.Copy(_waveData, copy);
        return copy;
    }
}
