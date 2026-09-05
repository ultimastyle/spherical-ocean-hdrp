# Senior Technical Review: Spherical Ocean HDRP

## Executive Summary

This architecture successfully marries an icosphere-based planetary layout with high-performance C# job systems and a single-pass HDRP surface shader. Moving to a tangent-space Gerstner implementation and relying on analytic Jacobian calculations instead of derivatives (`ddx`/`ddy`) makes the vertex shader robust. 

However, several critical issues threaten compilation, visual fidelity, and stability under **Unity 6 (HDRP)**. Most notably, the global CBUFFER properties conflict with `MaterialPropertyBlock` usage, several texture names and HDRP lighting loops are misaligned with the Unity 6 core pipeline, and several features flagged as dead code (such as the unlinked fluid simulation) will waste memory if not integrated or purged.

---

## 3. Shader Code â€” Detailed Review

### 3a. Tangent frame computation (Line 206-212)
**Code:**
```hlsl
void ComputeTangentFrame(float3 normal, out float3 tangent, out float3 bitangent)
{
    float3 anyVec = abs(normal.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
    tangent = normalize(cross(anyVec, normal));
    bitangent = cross(normal, tangent);
}
```
* **Verdict:** *Conditionally unstable.*
* **Analysis:** When the vertex normal points directly along the world Y-axis (or very close to it, within `0.999`), `anyVec` shifts from `(0, 1, 0)` to `(1, 0, 0)`. Across a smoothly curved sphere surface, this introduces a hard, visible discontinuity (flip) in the tangent space frame. 
* **Recommendation:** Replace this with a robust Fritsch-Style or householder reflection method, or derive it directly from the icosphere's analytic spherical parameters (latitude/longitude derivatives). If keeping cross products, ensure the fallback vector never shares a major axis with the poles.

### 3b. Gerstner wave in tangent space (Line 317-369)
* **Verdict:** *Correct architecture, but evaluate vertex density.*
* **Analysis:** Computing Gerstner waves in a local tangent frame per vertex avoids world-space shearing distortions common on planetary scales. 
* **Recommendation:** Because this is entirely vertex-driven, ensure your icosphere subdivision level yields sufficient vertex density. If subdivision is too low, high-frequency Gerstner wavelengths will alias heavily or disappear.

### 3c. Triplanar normal mapping (Line 381-434)
* **Verdict:** *Needs adjustment for water.*
* **Analysis:** `pow(abs(normal), 3.0)` creates an aggressive, highly localized blend that pinches sharply at projection axes. For water normals, this creates shimmering grid-aligned seams when the camera orbits.
* **Recommendation:** Lower the exponent to `1.5` or use a smooth polynomial blend function to feather the transitions across projection axes.

### 3d. HDRP light access (Line 225-243)
* **Verdict:** *Broken for Unity 6 HDRP.*
* **Analysis:** Including `ShaderVariablesLightLoop.hlsl` and directly reading `_DirectionalLightDatas` inside a standard forward pass is brittle. In Unity 6, light loop structures change depending on whether clustered or tile-based lighting is active, and buffers are not guaranteed to be bound unless rendered within specific HDRP passes.
* **Recommendation:** Use HDRP's native lighting evaluation functions. Instead of manual buffer extraction, include `Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.hlsl` and evaluate lights via HDRP's abstraction layer.

### 3e. HDRP sky sampling (Line 634)
* **Verdict:** *API Mismatch.*
* **Analysis:** `SampleSkyTexture(refl, 0, 0)` relies on legacy or internal signature parameters that vary across HDRP versions in Unity 6. 
* **Recommendation:** Use the modern HDRP environment lighting API:
  ```hlsl
  EnvLightData skyData = InitSkyEnvLightData(refl);
  // Or evaluate via HDRP's evaluation context
  ```
  Check `Packages/com.unity.render-pipelines.high-definition/Runtime/Sky/SkyEvaluator.hlsl` for the exact Unity 6 signature.

### 3f. HDRP depth sampling (Line 656)
* **Verdict:** *Incorrect for Reversed-Z.*
* **Analysis:** HDRP uses a **reversed-Z** buffer where the near plane is `1.0` and the far plane is `0.0`. Standard `LinearEyeDepth` calculations assuming a `0.0` to `1.0` range will evaluate incorrectly unless explicitly handling reversed-Z matrices.
* **Recommendation:** Use Unity's built-in HDRP depth reconstruction:
  ```hlsl
  float deviceDepth = SampleCameraDepth(refractedScreenUV);
  // Use HDRP's built-in linear depth helper or manual inversion:
  float linearDepth = LinearEyeDepth(deviceDepth, _ZBufferParams); 
  // Ensure _ZBufferParams aligns with HDRP's projection matrix conventions.
  ```

### 3g. Chromatic aberration & Scene Color (Line 663-667)
* **Verdict:** *Fragile API dependency.*
* **Analysis:** `_CameraColorTexture` is not guaranteed to be bound in all HDRP configurations (e.g., when custom passes inject before transparents or when using DLSS/FSR upscaling). 
* **Recommendation:** Use `_ColorPyramidTexture` or access the camera color buffer via HDRP Custom Pass Injection volumes correctly configured via the pass inputs (`RenderPassEvent`).

### 3h. Sun specular (Line 638-641)
* **Verdict:** *Physically implausible and prone to precision overflow.*
* **Analysis:** `pow(..., 1000.0)` combined with `atan(...)` creates a needle-thin highlight that will break down into pixelated aliasing (sparkles) during camera motion. Clamping to `50.0` masks the worst of it, but mathematically it bypasses energy conservation.
* **Recommendation:** Replace this with a standard GGX or Beckmann microfacet specular term using HDRP's built-in lighting structures.

### 3i. Opaque output with no blend (Line 689, 105-107)
* **Verdict:** *Architecturally sound for performance, but limits visual depth.*
* **Analysis:** Rendering as opaque prevents sorting issues with other transparent objects, but makes shoreline blending and soft water edges impossible without custom depth-fade calculations.
* **Recommendation:** Keep it opaque if performance is paramount, but implement a soft particle-style depth fade in the fragment shader against `_CameraDepthTexture` to soften intersections with terrain geometry.

---

## 4. C# Code â€” Detailed Review

### 4a. Icosphere mesh generation (Line 257-329)
* **Verdict:** *Correct.* Spherical lat/lon UVs are standard and acceptable for triplanar texturing.

### 4b. MaterialPropertyBlock usage (Line 165-178)
* **Verdict:** *Conflicting Architecture.*
* **Analysis:** If properties like `_OceanCenterPosWorld` and `_CrestTime` are defined inside a CBUFFER (`CBUFFER_START(SphericalOceanGlobals)`) in the HLSL shader, **`MaterialPropertyBlock` values will fail to override them**. MaterialPropertyBlocks only override properties declared outside of SRP dynamic/static CBuffers (i.e., standard material properties).
* **Recommendation:** Remove those properties from the global CBUFFER in the HLSL code, or convert them to standard uniform properties so the `MaterialPropertyBlock` can update them per-frame without breaking SRP batching.

### 4c. Burst job scheduling (Line 188-224)
* **Verdict:** *Optimizable.*
* **Analysis:** Chaining dependencies (`flux.Schedule()`, then passing the handle to `scale.Schedule(..., fluxHandle)`) forces a strict serial dependency pipeline across jobs. For small datasets (16k elements), job scheduling overhead may exceed compute time.
* **Recommendation:** Use `.ScheduleParallel()` for `IJobParallelFor` to ensure the work distributes effectively across worker threads. However, see section 4d regarding whether this simulation runs at all.

### 4d. Fluid sim grid vs icosphere mesh (Line 345-360)
* **Verdict:** *Dead Code / Disconnected Architecture.*
* **Analysis:** You allocate native arrays for an $n \times n$ fluid simulation grid, run Burst jobs on them, but **never pass the output buffers to the material or shader**. The shader relies entirely on vertex-shader Gerstner waves. 
* **Recommendation:** Either wire the fluid sim output texture/buffer into the material properties so it influences vertex displacement, or **purge the fluid simulation code entirely** to save memory and CPU overhead.

### 4e. Shader property sync (Line 411-481)
* **Verdict:** *Sub-optimal lifecycle management.*
* **Analysis:** Setting 40+ properties sequentially in `OnValidate` and initialization is acceptable at startup, but can cause main-thread stutter if triggered frequently in editor mode.
* **Recommendation:** Group properties into a serializable settings struct and pass them in bulk, or cache property IDs (`Shader.PropertyToID`) to minimize string-lookup overhead.

---

## 5. Answers to Specific Developer Questions

### Shader Compilation & HDRP APIs
1. **Will this compile in Unity 6000.5 HDRP?** No, out of the box it will fail due to CBUFFER conflicts with `MaterialPropertyBlock`, missing/deprecated sky evaluation signatures, and unresolved light loop buffer declarations.
2. **Is `ForwardOnly` the correct LightMode tag?** Yes, `ForwardOnly` is the correct tag for modern HDRP custom surface shaders.
3. **Are light structures available in `ForwardOnly`?** Not via manual raw buffer indexing (`_DirectionalLightDatas`) without breaking across platform updates. Use HDRP light evaluation helper functions.
4. **Is `SampleSkyTexture` correct?** No, use the modern `SkyEvaluator` or environment lighting utility APIs matching Unity 6.
5. **Is `_CameraColorTexture` correct?** It is standard, but safer accessed via HDRP custom pass color buffers or `_ColorPyramidTexture`.
6. **Does depth sampling work with reversed-Z?** No, you must account for HDRP's reversed-Z bounds (`1.0` to `0.0`).

### Math & Rendering
7. **Is `ComputeTangentFrame` robust?** No, it suffers from pole-flipping artifacts at normal.y â‰ˆ Â±0.999.
8. **Should triplanar normal blend use power 1.5?** Yes, reduce it from `3.0` to `1.5` or `2.0` to prevent harsh seam pinching.
9. **Is the sun specular formula reasonable?** No, `pow(..., 1000.0)` is unstable and prone to aliasing. Replace with standard GGX microfacet specular.
10. **Opaque vs Alpha at grazing angles?** Keep opaque for performance, but add a depth-fade intersection term to soften shorelines.

### C# & Architecture
11. **Does MaterialPropertyBlock override CBUFFER values?** **No.** Properties inside HLSL CBuffers cannot be overridden by `MaterialPropertyBlock`. Move them out of the CBUFFER.
12. **Are Burst job dependencies correct?** Syntactically yes, but logically redundant if the simulation output isn't plugged into the shader.
13. **Should the fluid sim grid be removed?** Yes, unless you intend to bind its output texture to the ocean shader. Right now, it is dead code.
14. **Is there a better approach than 40 `SetFloat` calls?** Cache property IDs statically using `Shader.PropertyToID`.

### Performance
15. **Are 18 texture lookups per fragment too expensive?** Yes, 6 triplanar samples across multiple texture channels will heavily impact fragment fill-rate on lower-end GPUs. Consolidate normal maps or drop to 2 projections where possible.
16. **Should Gerstner waves move to a compute shader?** If vertex counts exceed ~65k vertices, yes. For standard icosphere subdivision levels 3â€“5, vertex shader execution is acceptable.
17. **Expected vertex count for icosphere subdivision level 3?** 
    * Base icosahedron = 12 vertices, 20 triangles.
    * Subdiv 1 = 42 vertices
    * Subdiv 2 = 162 vertices
    * Subdiv 3 = 642 vertices
    * Subdiv 4 = 2,562 vertices
    * Subdiv 5 = 10,242 vertices
    * Level 3 is very lightweight; you can safely push to Level 4 or 5 for planetary scales.

---

## 6. Action Items Checklist

1. [ ] **Fix CBUFFER Conflict:** Remove `_OceanCenterPosWorld` and `_CrestTime` from the HLSL CBUFFER so `MaterialPropertyBlock` can successfully override them.
2. [ ] **Rewrite Tangent Frame:** Replace the pole-flipping branch in `ComputeTangentFrame` with a continuous mathematical formulation.
3. [ ] **Update HDRP Lighting & Sky:** Replace direct light buffer parsing with official Unity 6 HDRP lighting include files and evaluation functions.
4. [ ] **Fix Depth Reconstruction:** Invert/adjust depth sampling logic to properly support HDRP's reversed-Z buffer.
5. [ ] **Purge or Wire Fluid Sim:** Either connect the C# Burst fluid simulation output to a height/velocity texture consumed by the shader, or delete the unused simulation jobs and arrays.
6. [ ] **Soften Triplanar Blending:** Lower the triplanar exponent from `3.0` to `1.5` to eliminate surface seams.