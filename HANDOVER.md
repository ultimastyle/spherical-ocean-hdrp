# Spherical Ocean HDRP — Handover

## What this is
A standalone HDRP water rendering system for spherical/planetary worlds. Adapted from Crest Ocean System (MIT) for spherical geometry. Built 2026-09-05, polished with techniques from 10+ open-source water shader repos. Went through 6 rounds of Gemini code review until clean.

## Location
`D:\Dev\myhdrp\CrestHDRP\`
GitHub: `https://github.com/ultimastyle/spherical-ocean-hdrp`

## File structure
```
CrestHDRP/
├── Scripts/
│   ├── SphericalOcean.asmdef            — Assembly definition (runtime)
│   └── SphericalOceanRenderer.cs        — Core renderer, icosphere mesh
├── Shaders/
│   ├── HDRP/
│   │   └── SphericalOcean.shader        — Main HDRP surface shader
│   └── Library/
│       └── SphericalOceanGlobals.hlsl   — Shared sampler states
├── Integration/
│   ├── SphericalOceanCustomPass.cs      — HDRP Custom Pass for rendering
│   ├── SphericalUnderwaterEffect.cs     — Underwater fog stub (needs full-screen shader)
│   └── SphericalBuoyancy.cs             — Radial buoyancy
├── Editor/
│   ├── SphericalOcean.Editor.asmdef     — Editor assembly definition
│   └── SphericalOceanSetup.cs           — Setup wizard
├── README.md                            — User-facing docs
└── HANDOVER.md                          — This file
```

## Architecture

### C# Renderer (`SphericalOceanRenderer.cs`)
- **True icosphere** mesh: subdivided icosahedron, level 4 (2562 verts)
- **MaterialPropertyBlock**: per-frame updates (time, center) without breaking SRP Batcher
- **SyncMaterialProperties()**: called once on inspector change, not every frame
- **No dead code**: fluid sim, Burst jobs, NativeArrays all removed

### Shader (`SphericalOcean.shader`)
- **Gerstner waves in tangent space**: computed from local tangent frame
- **Triplanar normal mapping**: 4 scales × 3 projections = 12 texture lookups
- **Analytic foam Jacobian**: computed in vertex shader
- **GGX specular**: energy-conserving microfacet model (replaced unstable atan(1000))
- **Exact Fresnel**: Schlick or full dielectric
- **Subsurface scattering**: directional + depth-based
- **Chromatic aberration**: per-channel clamped UV sampling
- **Sky reflection**: cubemap-based (assign HDRP baking cubemap)
- **Opaque output**: no blend mode

## Dependencies
- Unity 6000.5+ with HDRP
- Assembly definitions reference: `Unity.RenderPipelines.HighDefinition.Runtime`, `Unity.RenderPipelines.Core.Runtime`

## Gemini Review History (2026-09-05)

### Round 1 — Initial review (12 issues)
1. CBUFFER conflict — `_CrestTime`/`_OceanCenterPosWorld` in CBUFFER can't be overridden by MaterialPropertyBlock → moved to plain uniforms
2. Tangent frame discontinuity — hard branch at `normal.y ≈ 0.999` → continuous Fritsch-Style formulation
3. HDRP light access — direct buffer access fragile → added bounds check + index clamping
4. Sky sampling — `SampleSkyTexture` signature mismatch in Unity 6 → replaced with cubemap
5. Depth reversed-Z — confirmed correct with `_ZBufferParams`
6. Chromatic aberration — `_CameraColorTexture` confirmed OK for ForwardOnly, added UV clamping
7. Sun specular — `pow(atan(...), 1000.0)` unstable → replaced with GGX microfacet
8. Triplanar blend — exponent 3.0 → 1.5 (softer seams)
9. Dead fluid sim — fully removed (NativeArrays, Burst jobs, FixedUpdate)
10. Burst jobs — removed (was dead code, never wired to shader)
11. Texture lookups — reduced 18 → 12 (4 scales × 3 projections)
12. Icosphere subdivision — increased 3 (642 verts) → 4 (2562 verts)

### Round 2 — Follow-up (3 issues)
13. GGX visibility term — missing Smith approximation → fixed with proper `Vis = 1/(NdotH²(1-a²)+a²)`
14. `_WorldSpaceLightPos0` — Built-in RP variable, not in HDRP → replaced with `GetMainLight().direction.y`
15. Unused includes/globals — cleaned up `CommonMaterial.hlsl` and dead Crest structures

### Round 3 — Follow-up (2 issues)
16. GGX normalization — confirmed correct (standard GGX/Trowbridge-Reitz)
17. LOAD_TEXTURE2D_X UV safety — added per-channel clamping after offset application

### Round 4-5 — Follow-up (1 issue)
18. `SampleSkyTexture` signature — not valid in Unity 6 → replaced with `SAMPLE_TEXTURECUBE`

### Round 6 — CLEAN
- No remaining compilation issues
- Shader ready for Unity 6 HDRP

## HDRP API reference
| Need | HDRP API | Include |
|---|---|---|
| Main light | `_DirectionalLightDatas[idx]` | `ShaderVariablesLightLoop.hlsl` |
| Light count | `_DirectionalLightCount` | `ShaderVariablesLightLoop.hlsl` |
| Shadow index | `_DirectionalShadowIndex` | `ShaderVariablesGlobal.hlsl` |
| Camera depth | `SampleCameraDepth(uv)` | `ShaderVariables.hlsl` |
| Linear depth | `LinearEyeDepth(depth, _ZBufferParams)` | `Common.hlsl` |
| Screen size | `_ScreenSize` (not `_ScaledScreenParams`) | `ShaderVariables.hlsl` |
| Sky reflection | `SAMPLE_TEXTURECUBE(_SkyCubemap, ...)` | Custom cubemap |

## Known issues / next steps
1. **Underwater post-process**: `SphericalUnderwaterEffect.cs` is a stub — needs full-screen shader pass
2. **Textures not provided**: User must assign normal map, foam, caustics, sky cubemap
3. **No LOD cascade**: Single-scale Gerstner only
4. **HDRP native lighting**: Custom `GetMainLight()` works for single directional light; full HDRP light loop integration is a future enhancement
