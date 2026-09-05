# Spherical Ocean HDRP — Code Review for Gemini

> **Repo:** https://github.com/ultimastyle/spherical-ocean-hdrp
> **Date:** 2026-09-05 (post-rewrite, all previous issues addressed)
> **Unity:** 6000.5.8f1 (Unity 6) with HDRP
> **Purpose:** Physically-based water for spherical/planetary worlds

---

## 1. File Inventory

| File | Lines | Purpose |
|---|---|---|
| `Shaders/HDRP/SphericalOcean.shader` | 697 | Main HDRP surface shader |
| `Scripts/SphericalOceanRenderer.cs` | 586 | Core C# renderer, icosphere mesh, Burst jobs |
| `Integration/SphericalOceanCustomPass.cs` | 55 | HDRP Custom Pass |
| `Integration/SphericalBuoyancy.cs` | 140 | Radial buoyancy |
| `Integration/SphericalUnderwaterEffect.cs` | 130 | Underwater fog/caustics (stub) |
| `Editor/SphericalOceanSetup.cs` | 102 | Editor wizard |
| `Scripts/SphericalOcean.asmdef` | 21 | Runtime assembly def |
| `Editor/SphericalOcean.Editor.asmdef` | 20 | Editor assembly def |

**Total: ~1,751 lines of code**

---

## 2. Architecture

### Rendering pipeline
```
SphericalOceanRenderer (C#)
  → BuildIcosphere()           — subdivided icosahedron, uniform vertices
  → EnsureMaterial()           — find/create shader, SyncMaterialProperties()
  → Update()                   — MaterialPropertyBlock: _CrestTime, _OceanCenterPosWorld
  → FixedUpdate()              — Burst Jobs: Flux → Scale → Apply (shallow water sim)

SphericalOceanCustomPass (HDRP)
  → Execute()                  — sets property block, Graphics.DrawMesh()

SphericalOcean.shader (HLSL)
  → Vert: ComputeTangentFrame → GerstnerWave(tangentSpace) → displace radially
  → Frag: triplanar normals → scatter → fresnel → sky reflection → refraction → foam
```

### Key design decisions
- **Tangent space Gerstner**: Waves computed in local tangent frame, not worldPos.xz
- **Triplanar normals**: 3-projection blend, no pole stretching
- **Analytic foam Jacobian**: Computed in vertex shader, not ddx/ddy
- **MaterialPropertyBlock**: Per-frame updates don't break SRP Batcher
- **True icosphere**: Uniform vertex distribution (no pole bunching)
- **HDRP-native APIs**: SampleSkyTexture, SampleCameraDepth, _DirectionalLightDatas[]

---

## 3. Shader Code — Sections for Review

### 3a. Tangent frame computation (line 206-212)
```hlsl
void ComputeTangentFrame(float3 normal, out float3 tangent, out float3 bitangent)
{
    float3 anyVec = abs(normal.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
    tangent = normalize(cross(anyVec, normal));
    bitangent = cross(normal, tangent);
}
```
**Concern:** Is this robust for all sphere orientations? The arbitrary axis selection could cause discontinuities when `normal.y` crosses 0.999. Should I use a more stable method?

### 3b. Gerstner wave in tangent space (line 317-369)
```hlsl
GerstnerResult GerstnerWave(float3 tangentPos, float time)
{
    float2 pos2D = tangentPos.xz * _WorldScale;
    // ... 8 octaves of Phillips spectrum ...
    // displacement.x = tangent, displacement.z = bitangent, displacement.y = up
}
```
**Concern:** The tangent frame is computed once per vertex in the vertex shader. Is this stable enough, or should I pass the tangent frame as varyings to the fragment shader for normal mapping?

### 3c. Triplanar normal mapping (line 381-434)
```hlsl
float3 SampleTriplanarNormal(float3 worldPos, float3 normal, float2 scale, float time)
{
    float3 blendWeights = pow(abs(normal), 3.0);
    blendWeights /= dot(blendWeights, 1.0);
    // Sample 3 projections, blend by weights
}
```
**Concern:** The triplanar weights use `pow(abs(normal), 3.0)` which is the standard sharp-blend approach. But for water normals, should the blend be softer (e.g., power of 1.5) to avoid visible seams at projection transitions?

### 3d. HDRP light access (line 225-243)
```hlsl
MainLight GetMainLight()
{
    if (_DirectionalLightCount == 0) return light;
    uint idx = (_DirectionalShadowIndex >= 0) ? (uint)_DirectionalShadowIndex : 0u;
    DirectionalLightData dirLight = _DirectionalLightDatas[idx];
    // ...
}
```
**Concern:** This includes `ShaderVariablesLightLoop.hlsl` and `LightLoopDef.hlsl`. In a `ForwardOnly` pass, are these buffers guaranteed to be bound and populated? Or do I need to use a Custom Pass injection point that runs inside the light loop?

### 3e. HDRP sky sampling (line 634)
```hlsl
float4 skyColor = SampleSkyTexture(refl, 0, 0);
```
**Concern:** `SampleSkyTexture` is declared in `ShaderVariablesFunctions.hlsl`. Is this the correct function for sampling the procedural sky, or should I use the full light loop path with `InitSkyEnvLightData` + `SampleEnv`?

### 3f. HDRP depth sampling (line 656)
```hlsl
float deviceDepth = SampleCameraDepth(refractedScreenUV);
float refractedSceneZ = LinearEyeDepth(deviceDepth, _ZBufferParams);
```
**Concern:** `SampleCameraDepth` is declared in `ShaderVariables.hlsl`. The `_ZBufferParams` is also available. Is this the correct pattern for HDRP, or does HDRP use reversed-Z that I need to handle differently?

### 3g. Chromatic aberration (line 663-667)
```hlsl
sceneColour.r = LOAD_TEXTURE2D_X(_CameraColorTexture, uint2(...) - uint2(...)).r;
sceneColour.g = LOAD_TEXTURE2D_X(_CameraColorTexture, uint2(...)).g;
sceneColour.b = LOAD_TEXTURE2D_X(_CameraColorTexture, uint2(...) + uint2(...)).b;
```
**Concern:** Using `LOAD_TEXTURE2D_X` with integer pixel coordinates. Is `_CameraColorTexture` the correct texture name in HDRP's forward path, or is it `_ColorPyramidTexture` or something else?

### 3h. Sun specular (line 638-641)
```hlsl
float sunSpec = min(pow(atan(max(dot(refl, -lightDir), 0.0) * 1.55), 1000.0) * reflectivity * _DirectionalLightBoost, 50.0);
```
**Concern:** This `atan` formula is from Optically Realistic Water. The `pow(..., 1000.0)` is extremely aggressive. Is this correct, or should it be a lower exponent?

### 3i. Opaque output with no blend (line 689, 105-107)
```hlsl
Cull Back
ZWrite On
ZTest LEqual
// No Blend statement — opaque
return half4(col, 1.0);
```
**Concern:** The ocean is rendered as fully opaque. For a water surface, should there be alpha blending for transparency at grazing angles, or is opaque correct for this use case?

---

## 4. C# Code — Sections for Review

### 4a. Icosphere mesh generation (line 257-329)
```csharp
private void BuildIcosphere()
{
    // 12 vertices of icosahedron
    // Subdivide N times with midpoint cache
    // Compute spherical UVs for texture mapping
}
```
**Concern:** The UVs are computed as spherical lat/lon `(atan2, asin)`. These are used for texture mapping only (not wave phase). Is this correct, or should I use a different UV projection for the triplanar normals?

### 4b. MaterialPropertyBlock usage (line 165-178)
```csharp
private void Update()
{
    _mr.GetPropertyBlock(_propBlock);
    _propBlock.SetVector("_OceanCenterPosWorld", GetPlanetCenter());
    _propBlock.SetFloat("_CrestTime", time);
    _mr.SetPropertyBlock(_propBlock);
}
```
**Concern:** I'm setting `_OceanCenterPosWorld` and `_CrestTime` via MaterialPropertyBlock, but the shader declares them in a CBUFFER (`SphericalOceanGlobals`). Does MaterialPropertyBlock override CBUFFER values, or do I need to set them differently?

### 4c. Burst job scheduling (line 188-224)
```csharp
var flux = new FluxJob { ... }.Schedule(_depth.Length, 256);
var scale = new ScaleJob { ... }.Schedule(_depth.Length, 256, flux);
var apply = new ApplyJob { ... }.Schedule(_depth.Length, 256, scale);
apply.Complete();
```
**Concern:** For 128² = 16,384 items with batch size 256, that's 64 batches. Is the dependency chain (flux → scale → apply) correct? Should I use `ScheduleParallel` instead of `Schedule` for IJobParallelFor?

### 4d. Fluid sim grid vs icosphere mesh (line 345-360)
```csharp
private void Allocate()
{
    int n = Mathf.Max(32, (int)Mathf.Pow(2, icosphereSubdivisions + 4));
    int nc = n * n;
    // NativeArrays of size nc
}
```
**Concern:** The fluid sim runs on an n×n grid, but the mesh is an icosphere. These are two different coordinate systems. The sim data (`_depth`, `_vel`) is never actually used by the shader — the shader does its own Gerstner calculation. Is this dead code that should be removed, or is there a plan to wire the sim to the mesh?

### 4e. Shader property sync (line 411-481)
```csharp
public void SyncMaterialProperties()
{
    _material.SetFloat("_WindSpeed", windSpeed);
    // ... 40+ SetFloat/SetColor/SetVector calls
}
```
**Concern:** This is called from `OnValidate` (editor) and `EnsureMaterial` (startup). Is calling ~40 material property sets once at startup acceptable, or should I batch them differently?

---

## 5. Questions for Gemini

### Shader compilation
1. Will this shader compile in Unity 6000.5 HDRP? Are there any missing includes or undefined variables?
2. Is `ForwardOnly` the correct LightMode tag for a custom water surface in HDRP?
3. Are `_DirectionalLightDatas`, `_DirectionalLightCount`, `_DirectionalShadowIndex` available in a `ForwardOnly` pass?
4. Is `SampleSkyTexture` the right function for sky reflections, or do I need the full light loop path?
5. Is `_CameraColorTexture` the correct texture name for the scene color buffer in HDRP forward?
6. Does `SampleCameraDepth` + `LinearEyeDepth(depth, _ZBufferParams)` work correctly with HDRP's reversed-Z?

### Math & rendering
7. Is the `ComputeTangentFrame` function robust for all sphere orientations?
8. Should triplanar normal blending use a softer power (1.5 vs 3.0) for water?
9. Is the `atan(..., 1000.0)` sun specular formula physically reasonable?
10. Should the ocean output be opaque or have alpha at grazing angles?

### C# & architecture
11. Does MaterialPropertyBlock correctly override CBUFFER values in HDRP?
12. Are the Burst job dependencies (flux → scale → apply) correct?
13. Should the fluid sim grid be removed since the shader doesn't use it?
14. Is there a better approach than calling SetFloat 40 times at startup?

### Performance
15. Is 6 triplanar normal samples × 6 scales = 18 texture lookups per fragment too expensive?
16. Should Gerstner waves move to a compute shader for better GPU utilization?
17. What's the expected vertex count for icosphere subdivision level 3?

---

## 6. What Still Needs Work

1. **Underwater post-process**: `SphericalUnderwaterEffect.cs` is a stub — needs a full-screen shader pass
2. **Textures not provided**: User must assign normal map, foam, caustics
3. **Dynamic wave sim not wired**: Burst jobs run but output isn't connected to shader
4. **No LOD cascade**: Single-scale Gerstner only
5. **Foam texture sampling**: Shader declares `_FoamTexture` but never samples it in fragment
6. **Caustics texture sampling**: Shader declares `_CausticsTexture` but never samples it in fragment

---

## 7. How to Run

```bash
git clone https://github.com/ultimastyle/spherical-ocean-hdrp.git
# Copy into Unity project Assets/ folder
# Open Unity with HDRP template
# Tools > Spherical Ocean > Setup Wizard
# Assign normal map texture to renderer
# Hit Play
```
