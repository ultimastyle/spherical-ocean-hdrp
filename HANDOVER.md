# HANDOVER — Spherical Ocean HDRP (Merged)

**Project:** Spherical Ocean HDRP
**Path:** `D:\Dev\myhdrp\SphericalOcean\`
**Status:** Merged from CrestHDRP + OpenOceanPhysics, 6 rounds Gemini-reviewed (CrestHDRP portion)
**Goal:** Standalone reusable tool for planetary water rendering in Unity 6 HDRP

---

## What This Is

A complete spherical ocean rendering system for planetary worlds. Combines:
- **CrestHDRP**: True icosphere mesh, MaterialPropertyBlock, HDRP Custom Pass, SSS, caustics, Fresnel, cubemap sky, triplanar normals
- **OpenOceanPhysics**: GPU FFT wave cascades, Burst job Gerstner waves, Jacobian foam, shore foam, wave spectrum

## Architecture

### Hybrid Wave Displacement
The shader blends two displacement sources:
1. **FFT Cascades** (large/medium scales): GPU compute shader generates displacement textures at multiple resolutions. Blended by distance from camera.
2. **Burst Gerstner** (high-frequency detail): Analytical waves with Burst job normals, added on top of FFT.

### Key Files

| File | Purpose |
|------|---------|
| `Scripts/SphericalOceanRenderer.cs` | Core renderer. Manages icosphere mesh, FFT cascade instantiation, cascade weight calculation, shader binding. |
| `Shaders/HDRP/SphericalOcean.shader` | Main surface shader. Opaque, HDRP Lit. Samples FFT displacement textures, blends with Gerstner, applies SSS/caustics/foam/refraction. |
| `Scripts/Water/FFTWaveSimulation.cs` | GPU FFT engine. Spectrum update → butterfly FFT → displacement → Jacobian → foam. |
| `Scripts/Water/SphericalGerstnerWaves.cs` | Burst job Gerstner waves on sphere. Analytical normals, zero GC. |
| `Scripts/Water/OceanFoamGenerator.cs` | Jacobian + shore foam generation. |
| `Scripts/Water/WaveCascadeData.cs` | ScriptableObject with cascade configurations (resolution, patch size, wind, distance ranges). |
| `Scripts/Water/OceanWaveSpectrum.cs` | Phillips/JONSWAP/Pierson-Moskowitz spectrum sampling. |
| `Shaders/Compute/OceanWaveCompute.compute` | FFT compute shader (5 kernels: UpdateSpectrum, ButterflyPass, InverseTransform, ComputeJacobian, UpdateFoam). |
| `Integration/SphericalBuoyancy.cs` | Radial buoyancy. Samples FFT + Gerstner for accurate float height. |
| `Integration/SphericalUnderwaterEffect.cs` | Underwater post-process controller. Enables/disables Custom Pass when camera is submerged. |
| `Shaders/Underwater/SphericalOceanUnderwater.shader` | Full-screen underwater shader: absorption, fog, caustics, wave distortion. |

### Shader Uniforms (Global, set by SphericalOceanRenderer)

| Uniform | Type | Source |
|---------|------|--------|
| `_OceanCascadeDisp0..3` | Texture2D | FFT displacement textures per cascade |
| `_OceanCascadeFoam0..3` | Texture2D | FFT foam textures per cascade |
| `_OceanCascadeJac0..3` | Texture2D | FFT Jacobian textures per cascade |
| `_OceanCascadeCount` | int | Number of active cascades |
| `_OceanCascadeWeights` | float4 | Distance-based blend weights |
| `_OceanCenterPosWorld` | float3 | Planet center position |
| `_CrestTime` | float | Current time |

### Shader Uniforms (Material, set via MaterialPropertyBlock)

All visual parameters (wind, waves, SSS, foam, caustics, refraction, etc.) are synced via `SyncMaterialProperties()` only on change.

## Gemini Review History (CrestHDRP portion)

- **Round 1** (12 issues): CBUFFER conflict, tangent frame, HDRP light access, sky sampling, depth reversed-Z, chromatic aberration, GGX specular, triplanar blend, dead fluid sim, Burst jobs, texture lookups, icosphere level
- **Round 2** (3 issues): GGX visibility term, `_WorldSpaceLightPos0` → `GetMainLight()`, unused includes
- **Round 3** (2 issues): Chromatic aberration clamping, GGX normalization
- **Round 4-5** (1 issue): `SampleSkyTexture` signature → `SAMPLE_TEXTURECUBE`
- **Round 6**: **CLEAN**

## Gotchas

1. **FFT → Sphere Mapping**: FFT textures use spherical UV (lat/lon) sampling. Standard planar FFT will distort at poles — use tangent-space local projection for best results.
2. **Property Management**: Compute shaders use global properties (`Shader.SetGlobalFloat`). Material properties use `MaterialPropertyBlock`. Never clone the material.
3. **Burst Dependencies**: `SphericalGerstnerWaves.cs` requires `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics` packages.
4. **GPU Readback for Buoyancy**: Async GPU readback is too slow for `FixedUpdate`. Buoyancy uses an analytic approximation of the FFT spectrum.
5. **Underwater Effect**: Requires HDRP Custom Pass Volume. The `SphericalUnderwaterEffect` component manages this automatically.

## Next Steps

1. Test compile in actual Unity 6 HDRP project
2. Assign textures (normal map, foam, caustics, sky cubemap)
3. Create `WaveCascadeData` asset with cascade configurations
4. Tune cascade distance ranges for your planet size
5. Verify FFT displacement sampling works correctly on sphere
