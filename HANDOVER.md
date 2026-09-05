# HANDOVER — Spherical Ocean HDRP (Merged)

**Project:** Spherical Ocean HDRP
**Path:** `D:\Dev\myhdrp\SphericalOcean\`
**GitHub:** `https://github.com/ultimastyle/spherical-ocean-hdrp`
**Status:** Merged from CrestHDRP + OpenOceanPhysics, 3 Gemini reviews complete
**Goal:** Standalone reusable tool for planetary water rendering in Unity 6 HDRP

---

## What This Is

A complete spherical ocean rendering system for planetary worlds. Combines:
- **CrestHDRP**: True icosphere mesh, MaterialPropertyBlock, HDRP Custom Pass, SSS, caustics, Fresnel, cubemap sky, triplanar normals
- **OpenOceanPhysics**: GPU FFT wave cascades, Burst job Gerstner waves, Jacobian foam, shore foam, wave spectrum

## Git History (4 commits)

| Commit | Description |
|--------|-------------|
| `98ec9f5` | Merge CrestHDRP + OpenOceanPhysics: FFT cascades, Burst Gerstner, Jacobian foam, underwater post-process |
| `c9214ef` | Fix Gemini review issues: CBUFFER UnityPerMaterial, fullscreen triangle underwater, cascade weight fallback |
| `55c436a` | Fix material leak: track created material, destroy in OnDestroy |
| `a8ff45d` | Tropical water preset: tuned SSS, scattering, foam, specular, cascades + editor setup wizard |
| `7b78ccc` | Fix Gemini review: directionalLightBoost 2.0, sssIntensity 1.2, maxWaveAmplitude 10, scatterAmount 2, aberration 0.0005 |

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
| `Shaders/Underwater/SphericalOceanUnderwater.shader` | Full-screen underwater shader: absorption, fog, caustics, wave distortion. Proper HDRP fullscreen triangle + stereo depth. |
| `Scripts/Water/FFTWaveSimulation.cs` | GPU FFT engine. Spectrum update → butterfly FFT → displacement → Jacobian → foam. |
| `Scripts/Water/SphericalGerstnerWaves.cs` | Burst job Gerstner waves on sphere. Analytical normals, zero GC. |
| `Scripts/Water/OceanFoamGenerator.cs` | Jacobian + shore foam generation. |
| `Scripts/Water/WaveCascadeData.cs` | ScriptableObject with cascade configurations (resolution, patch size, wind, distance ranges). |
| `Scripts/Water/OceanWaveSpectrum.cs` | Phillips/JONSWAP/Pierson-Moskowitz spectrum sampling. |
| `Shaders/Compute/OceanWaveCompute.compute` | FFT compute shader (5 kernels: UpdateSpectrum, ButterflyPass, InverseTransform, ComputeJacobian, UpdateFoam). |
| `Integration/SphericalBuoyancy.cs` | Radial buoyancy. Samples FFT + Gerstner for accurate float height. |
| `Integration/SphericalUnderwaterEffect.cs` | Underwater post-process controller. Creates CustomPassVolume dynamically, enables/disables based on camera submersion. |
| `Editor/SphericalOceanSetup.cs` | Editor wizard: Tools > Spherical Ocean > Setup Wizard. Creates ocean + tropical cascade asset. |

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

## Tropical Preset Defaults (tuned to match reference)

| Parameter | Value | Why |
|---|---|---|
| `shallowColor` | `(0, 0.7, 0.65)` | Turquoise shallows |
| `sssIntensity` | `1.2` | Moderate subsurface glow |
| `sssBase` | `0.3` | Base SSS even in deep water |
| `specular` | `0.8` | Strong sun reflection |
| `directionalLightBoost` | `2.0` | HDR-safe specular (NOT 128 — Gemini flagged as HDR clipping) |
| `normalScale` | `80` | Finer wave detail |
| `refractionStrength` | `0.8` | Clearer see-through near shore |
| `visibility` | `40` | Tropical clear water |
| `scatterColor` | `(0, 0.9, 0.7)` | Warm tropical scatter |
| `scatterAmount` | `2.0` | Not too flat/opaque |
| `maxWaveAmplitude` | `10` | Safe for 420-radius sphere (50 causes pole tearing) |
| `aberrationAmount` | `0.0005` | Subtle, no artifacts |

### Cascade Presets

| Cascade | Spectrum | Patch | Wind | Choppiness | Distance |
|---|---|---|---|---|---|
| Ocean Swells (far) | Pierson-Moskowitz | 600m | 8 m/s | 0.6 | 300–2500m |
| Wind Waves (mid) | JONSWAP | 250m | 10 m/s | 1.2 | 80–600m |
| Detail Ripples (near) | JONSWAP | 100m | 6 m/s | 1.8 | 0–180m |

## Gemini Review History

### Round 1 — CrestHDRP (12 issues, all fixed)
CBUFFER conflict, tangent frame, HDRP light access, sky sampling, depth reversed-Z, chromatic aberration, GGX specular, triplanar blend, dead fluid sim, Burst jobs, texture lookups, icosphere level

### Round 2 — CrestHDRP (3 issues, all fixed)
GGX visibility term, `_WorldSpaceLightPos0` → `GetMainLight()`, unused includes

### Round 3 — CrestHDRP (2 issues, all fixed)
Chromatic aberration clamping, GGX normalization

### Rounds 4-5 — CrestHDRP (1 issue, fixed)
`SampleSkyTexture` signature → `SAMPLE_TEXTURECUBE`

### Round 6 — CrestHDRP: **CLEAN**

### Round 7 — Merged Code (3 issues, all fixed)
CBUFFER naming (UnityPerMaterial), underwater shader rewrite (fullscreen triangle + proper depth), cascade weight fallback

### Round 8 — Tropical Preset (5 issues, all fixed)
directionalLightBoost 128→2, sssIntensity 2.5→1.2, maxWaveAmplitude 50→10, scatterAmount 4→2, aberration 0.0015→0.0005

## Reviews Location

All Gemini review files saved to: `D:\Dev\myhdrp\SphericalOcean\reviews\`

## Gotchas

1. **FFT → Sphere Mapping**: FFT textures use spherical UV (lat/lon) sampling. Standard planar FFT will distort at poles — use tangent-space local projection for best results.
2. **Property Management**: Compute shaders use global properties (`Shader.SetGlobalFloat`). Material properties use `MaterialPropertyBlock`. Never clone the material.
3. **Burst Dependencies**: `SphericalGerstnerWaves.cs` requires `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics` packages.
4. **GPU Readback for Buoyancy**: Async GPU readback is too slow for `FixedUpdate`. Buoyancy uses an analytic approximation of the FFT spectrum.
5. **Underwater Effect**: Creates `CustomPassVolume` dynamically. Component manages enable/disable based on camera submersion.

## Next Steps

1. **Test compile in Unity 6 HDRP** — open `D:\Dev\myhdrp\SphericalOcean\` as a Unity project
2. Assign textures (normal map, foam texture, caustics texture, sky cubemap)
3. Create `WaveCascadeData` asset (right-click → Create → SphericalOcean → Cascade Configuration) or use editor wizard
4. Tune cascade distance ranges for your planet size
5. Verify FFT displacement sampling works correctly on sphere (pole distortion)
