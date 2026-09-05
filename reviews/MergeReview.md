# Merge Review: CrestHDRP + OpenOceanPhysics

## Project A: CrestHDRP (Spherical Ocean HDRP)
**Path:** `D:\Dev\myhdrp\CrestHDRP\`
**Status:** 6 rounds Gemini-reviewed, CLEAN
**Goal:** Standalone reusable tool for planetary water rendering

### Strengths
- True icosphere mesh (subdivided icosahedron, radial displacement)
- MaterialPropertyBlock (no material cloning, GC-free)
- Opaque output (writes depth, correct for HDRP)
- HDRP Custom Pass integration
- Cubemap sky sampling (Unity 6 compatible)
- Analytic wave spectrum (JONSWAP)
- SSS, foam, caustics, refraction, Fresnel
- Radial buoyancy
- Editor wizard for quick setup

### Weaknesses
- No FFT (analytic spectrum only, less realistic)
- No cascades (single-scale waves)
- No Burst jobs (CPU-side wave calc)
- Foam is basic (no Jacobian)
- No GPU displacement textures

---

## Project B: OpenOceanPhysics
**Path:** `D:\Dev\myhdrp\OpenOceanPhysics\`
**Status:** Not reviewed by Gemini yet
**Goal:** Physics-based ocean with FFT + cascades

### Strengths
- GPU FFT wave simulation (compute shader)
- Multiple cascades (far/mid/near) for detail at all distances
- Burst job Gerstner waves (analytical normals, zero GC)
- Jacobian foam generation (realistic wave crest overlap)
- Shore foam (proximity to terrain)
- Wave spectrum (Phillips/JONSWAP)
- Cascade blending system

### Weaknesses
- UV sphere mesh (not icosphere, poles have artifacts)
- Material-based (clones material, GC pressure)
- Transparent output (no depth write)
- No HDRP Custom Pass integration
- No cubemap sky sampling
- No caustics
- No refraction
- No buoyancy

---

## Merge Strategy Questions

1. **Mesh:** Keep icosphere from CrestHDRP (radial displacement, no pole artifacts)?
2. **Wave Simulation:** Add FFT cascades from OpenOceanPhysics to CrestHDRP?
3. **Gerstner:** Keep Burst job Gerstner from OpenOceanPhysics for analytical detail?
4. **Foam:** Add Jacobian + shore foam from OpenOceanPhysics?
5. **Shader:** Merge CrestHDRP's HDRP shader quality (SSS, caustics, refraction) with OpenOceanPhysics's FFT displacement?
6. **Cascade Blending:** Add distance-based cascade blending to shader?
7. **Material:** Keep MaterialPropertyBlock from CrestHDRP (no cloning)?
8. **Integration:** Keep HDRP Custom Pass from CrestHDRP?

## Recommendation Request

Please analyze both codebases and recommend:
1. What to keep from each project
2. What overlaps and should be merged
3. What is unique and should be added
4. Step-by-step merge plan
5. Potential conflicts and how to resolve them
