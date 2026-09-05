# Spherical Ocean HDRP

A standalone, physically-based spherical ocean rendering system for Unity 6 HDRP planetary worlds.

Merges **CrestHDRP** (icosphere mesh, HDRP lighting, SSS, caustics) with **OpenOceanPhysics** (GPU FFT waves, Burst Gerstner, Jacobian foam, cascades).

## Features

### Wave Simulation
- **GPU FFT Cascades**: Multi-scale ocean swells via compute shader FFT (far/mid/near)
- **Burst Gerstner Waves**: High-frequency analytical detail on top of FFT
- **Hybrid Displacement**: FFT handles large/medium scales, Gerstner adds micro-choppiness
- **Distance-Based Cascade Blending**: Seamless LOD transitions by camera distance

### Rendering
- **True Icosphere Mesh**: No pole artifacts, radial displacement from planet center
- **HDRP Native**: Opaque output, correct depth writes, Custom Pass integration
- **SSS**: Subsurface scattering with shallow/deep color blending
- **Caustics**: Animated caustic projection on underwater surfaces
- **Fresnel**: Exact dielectric or Schlick approximation
- **Cubemap Sky Reflections**: Unity 6 compatible `SAMPLE_TEXTURECUBE`
- **Triplanar Normals**: 4-scale cascaded detail without UV seams

### Foam
- **Jacobian Foam**: Physics-driven from FFT wave crest overlap
- **Shore Foam**: Proximity-based breaking waves
- **Analytic Foam**: Gerstner wave steepness fallback

### Underwater
- **Full-Screen Post-Process**: Color absorption, fog, caustics
- **Depth-Based Tinting**: Progressive blue shift with distance
- **Wave Distortion**: Subtle screen-space wobble

### Buoyancy
- **Radial Forces**: Planet-relative buoyancy for any rigidbody
- **Wave-Accurate Bobbing**: Samples FFT + Gerstner for correct float height
- **Surface Alignment**: Objects align to local gravity

## Requirements

- Unity 6000.5+ with HDRP
- Burst package (for Gerstner waves)
- Mathematics + Collections packages

## Quick Start

1. Copy `SphericalOcean/` into your Unity project's `Assets/`
2. Create an empty GameObject, add `SphericalOceanRenderer`
3. Set `planetCenter` to your planet transform
4. Adjust `oceanRadius` and `seaLevelRadius`
5. Enable `enableFFTCascades` and assign a `WaveCascadeData` asset
6. Assign textures: normal map, foam texture, caustics texture, sky cubemap
7. For underwater: add `SphericalUnderwaterEffect` to your camera

## File Structure

```
SphericalOcean/
├── Scripts/
│   ├── SphericalOceanRenderer.cs      # Core renderer + FFT cascade manager
│   └── Water/
│       ├── FFTWaveSimulation.cs        # GPU FFT engine
│       ├── SphericalGerstnerWaves.cs   # Burst job Gerstner
│       ├── OceanFoamGenerator.cs       # Jacobian + shore foam
│       ├── WaveCascadeData.cs          # Cascade configurations
│       └── OceanWaveSpectrum.cs        # Phillips/JONSWAP/PM spectra
├── Shaders/
│   ├── HDRP/
│   │   └── SphericalOcean.shader      # Main surface shader (FFT + Gerstner + SSS + caustics)
│   ├── Compute/
│   │   └── OceanWaveCompute.compute   # FFT compute shader
│   ├── Library/
│   │   └── SphericalOceanGlobals.hlsl # Shared declarations
│   ├── Underwater/
│   │   └── SphericalOceanUnderwater.shader  # Full-screen underwater post-process
│   └── OceanFoamBlend.shader          # Foam accumulation shader
├── Integration/
│   ├── SphericalBuoyancy.cs           # Radial buoyancy system
│   ├── SphericalOceanCustomPass.cs    # HDRP Custom Pass wrapper
│   └── SphericalUnderwaterEffect.cs   # Underwater post-process controller
├── Editor/
│   └── SphericalOceanSetup.cs         # Editor wizard
└── Resources/
    └── OceanWaveCompute.compute       # FFT compute shader (fallback)
```

## Credits

- **Crest Ocean System** (MIT) — wave spectrum, LOD architecture
- **OpenOceanPhysics** — GPU FFT engine, Burst Gerstner, Jacobian foam
- **Martins Upitis** — optically realistic water techniques
- **Tessendorf (2001)** — FFT ocean simulation foundations
