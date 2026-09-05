# Spherical Ocean HDRP — Handover for Claude

## What this is
A standalone HDRP water rendering system for spherical/planetary worlds. Adapted from Crest Ocean System (MIT) for spherical geometry. Built 2026-09-05, polished with techniques from 10+ open-source water shader repos. Major review and rewrite done 2026-09-05 based on Gemini code review feedback.

## Location
`D:\Dev\myhdrp\CrestHDRP\`

## File structure
```
CrestHDRP/
├── Scripts/
│   ├── SphericalOcean.asmdef            — Assembly definition (runtime)
│   └── SphericalOceanRenderer.cs        — Core renderer, wave sim, Burst jobs, icosphere mesh
├── Shaders/
│   ├── HDRP/
│   │   └── SphericalOcean.shader        — Main HDRP surface shader
│   └── Library/
│       └── SphericalOceanGlobals.hlsl   — Shared constants/cbuffers
├── Integration/
│   ├── SphericalOceanCustomPass.cs      — HDRP Custom Pass for rendering
│   ├── SphericalUnderwaterEffect.cs     — Underwater fog, caustics, color grading
│   └── SphericalBuoyancy.cs             — Radial buoyancy for objects on spherical ocean
├── Editor/
│   ├── SphericalOcean.Editor.asmdef     — Editor assembly definition
│   └── SphericalOceanSetup.cs           — Setup wizard, editor menu items
├── README.md                            — User-facing docs
└── HANDOVER.md                          — This file
```

## How it differs from flat Crest
- All displacement is **radial** (outward from planet center), not Y-axis
- LOD is **angular** (degrees around sphere), not planar distance
- Wave spectrum projects onto **tangent space**, not XZ plane
- Buoyancy pushes **radial**, not just upward
- Mesh is a **true icosphere** (subdivided icosahedron), not a UV-sphere

## Dependencies
- Unity 2022.3+ with HDRP package
- Burst + Mathematics + Collections + Jobs
- Assembly definitions reference: `Unity.Mathematics`, `Unity.Burst`, `Unity.Collections`, `Unity.Jobs`, `Unity.RenderPipelines.HighDefinition.Runtime`, `Unity.RenderPipelines.Core.Runtime`

## Architecture

### C# Renderer
- **True icosphere** mesh: subdivided icosahedron for uniform vertex distribution (no pole distortion)
- **Burst jobs**: FluxJob → ScaleJob → ApplyJob (shallow water equations)
- **MaterialPropertyBlock**: per-frame updates (time, center) without breaking SRP Batcher
- **SyncMaterialProperties()**: called once on inspector change, not every frame
- **No dead ComputeBuffer**: removed PushToGpu/ComputeBuffer that was never read by shader

### Shader
- **Gerstner waves in tangent space**: computed from local tangent frame, not worldPos.xz
- **Triplanar normal mapping**: 3-projection blend, no pole stretching
- **Analytic foam Jacobian**: computed in vertex shader, not ddx/ddy in fragment
- **World Scale multiplier**: scales wave physics for miniaturized planets
- **HDRP native light access**: `_DirectionalLightDatas[]` structured buffer
- **HDRP sky sampling**: `SampleSkyTexture()` instead of legacy `unity_SpecCube0`
- **HDRP depth**: `SampleCameraDepth()` instead of `LinearEyeDepth` with _ZBufferParams
- **Opaque output**: no blend mode (was incorrectly using alpha blend)

## Gemini review fixes (2026-09-05)

### Critical
1. **Gerstner on sphere** (Q5): Was using `worldPos.xz` which projects a flat plane through the sphere. Fixed to compute tangent frame from sphere normal, do Gerstner in tangent space, transform back to world space.
2. **Normal mapping** (Q8): Same issue — was using worldPos.xz for UVs. Fixed to triplanar mapping with 3-projection blend using `ComputeTriplanarWeights`.
3. **Foam Jacobian** (Q7): Was using ddx/ddy in fragment which breaks at grazing angles on spheres. Moved to analytic computation in vertex shader.
4. **Phillips spectrum scaling** (Q6): Real-world gravity (9.81) doesn't work on 420m sphere. Added `_WorldScale` multiplier to tune wave frequency to planet radius.
5. **Mesh generation** (Q10): Was UV-sphere (lat/lon) which bunches vertices at poles. Replaced with true icosphere (subdivided icosahedron) for uniform distribution.
6. **Dead ComputeBuffer** (Q12): `PushToGpu()` was copying data to GPU every frame that the shader never read. Removed entirely.
7. **Material sync** (Q13): Was calling `Material.SetFloat` 40x per frame. Changed to `MaterialPropertyBlock` for per-frame globals, `SyncMaterialProperties()` only on inspector change.
8. **Blend mode** (6.7): Was `Blend SrcAlpha OneMinusSrcAlpha` but output is `half4(col, 1.0)`. Removed blend — fully opaque.
9. **Reflections** (6.2): `unity_SpecCube0` is legacy/broken in HDRP. Replaced with `SampleSkyTexture()`.
10. **Depth buffer** (6.3): Replaced `LinearEyeDepth` with HDRP's `SampleCameraDepth()`.
11. **LightMode** (6.1): Changed to `ForwardOnly` with Custom Pass injection.
12. **Underwater** (6.8): Decoupled from surface mesh. Underwater should use a separate Custom Pass Volume with full-screen post-processing, not backface rendering on the ocean mesh.

## HDRP API reference (from actual package source)
| Need | HDRP API | Include |
|---|---|---|
| Main light direction | `_DirectionalLightDatas[idx].forward` | `ShaderVariablesLightLoop.hlsl` |
| Main light color | `_DirectionalLightDatas[idx].color` | `ShaderVariablesLightLoop.hlsl` |
| Light count | `_DirectionalLightCount` | `ShaderVariablesLightLoop.hlsl` |
| Shadow light index | `_DirectionalShadowIndex` | `ShaderVariablesGlobal.hlsl` |
| Sky reflection | `SampleSkyTexture(dir, lod, slice)` | `ShaderVariablesFunctions.hlsl` |
| Camera depth | `SampleCameraDepth(uv)` | `ShaderVariables.hlsl` |
| Linear eye depth | `LinearEyeDepth(depth, _ZBufferParams)` | `Common.hlsl` |
| Triplanar weights | `ComputeTriplanarWeights(normal)` | `CommonMaterial.hlsl` |
| Screen size | `_ScreenSize` (not `_ScaledScreenParams`) | `ShaderVariables.hlsl` |

## Current state (2026-09-05) — COMPLETE
- [x] True icosphere mesh with uniform vertex distribution
- [x] Gerstner waves in tangent space (no pole distortion)
- [x] Triplanar normal mapping (no UV stretching)
- [x] Analytic foam Jacobian in vertex shader
- [x] World scale multiplier for Phillips spectrum
- [x] HDRP native light access (`_DirectionalLightDatas[]`)
- [x] HDRP sky sampling (`SampleSkyTexture`)
- [x] HDRP depth sampling (`SampleCameraDepth`)
- [x] MaterialPropertyBlock for per-frame updates
- [x] Opaque output (no blend mode)
- [x] Dead ComputeBuffer removed
- [x] All inspector fields exposed (no hardcoded values)
- [x] OnValidate for live editor updates
- [x] Assembly definitions (runtime + editor)
- [x] README + HANDOVER docs

## Known issues / next steps
1. **Underwater**: Need to create a separate underwater post-process Custom Pass Volume. The current `SphericalUnderwaterEffect.cs` is a stub — needs a proper full-screen shader pass for fog/caustics when camera is below surface.
2. **Foam texture**: No foam texture provided. User must assign a tiled foam texture.
3. **Normal map**: No normal map provided. User must assign a tiled water normal map.
4. **Caustics texture**: No caustics texture provided. User must assign a tiled caustics texture.
5. **HDRP ForwardOnly tag**: May need to change to a Custom Pass injection point depending on HDRP version.
6. **Dynamic waves**: Burst jobs run on a grid that doesn't match the icosphere vertices. The sim data isn't wired to mesh displacement yet — shader does its own Gerstner.
7. **LOD**: No cascade system yet — single-scale Gerstner only.
