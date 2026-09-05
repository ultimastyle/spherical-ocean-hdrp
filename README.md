# Spherical Ocean HDRP

A physically-based water rendering system for **spherical/planetary** HDRP worlds. Built on techniques from the [Crest Ocean System](https://github.com/wave-harmonic/crest) (MIT), adapted for radial geometry.

![Unity](https://img.shields.io/badge/Unity-6000.5+-black?logo=unity)
![HDRP](https://img.shields.io/badge/Render%20Pipeline-HDRP-blue)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

- **True icosphere mesh** — uniform vertex distribution, no pole distortion
- **Gerstner waves in tangent space** — Phillips spectrum, 8 octaves, proper spherical displacement
- **Triplanar normal mapping** — 3-projection blend, no UV stretching at poles
- **Analytic foam** — Jacobian computed in vertex shader, wave-steepness based
- **Exact Fresnel** — dielectric reflectance (not Schlick approximation)
- **Subsurface scattering** — depth-based shallow color transition
- **HDRP native lighting** — `_DirectionalLightDatas[]`, `SampleSkyTexture()`, `SampleCameraDepth()`
- **Chromatic aberration** — R/G/B refraction offset
- **Water volume extinction** — absorption, scattering, sun transmittance
- **Radial buoyancy** — objects float on spherical surface
- **Burst jobs** — shallow water equation simulation (Flux/Scale/Apply)
- **MaterialPropertyBlock** — per-frame updates without breaking SRP Batcher

## Requirements

- Unity 6000.5+ with HDRP package
- Burst + Mathematics + Collections + Jobs

## Installation

Copy the `CrestHDRP/` folder into your Unity project's `Assets/` directory.

## Quick Start

1. Open **Tools > Spherical Ocean > Setup Wizard**
2. Set your planet center transform and radii
3. Click **Create Ocean GameObject**
4. Assign a water normal map to the renderer
5. Hit Play

## Inspector Fields

| Category | Key Fields |
|---|---|
| Planet | `planetCenter`, `oceanRadius`, `seaLevelRadius` |
| Waves | `windSpeed`, `windDirection`, `waveChoppiness`, `worldScale` |
| Normals | `normalMap`, `normalScale`, `normalsStrengthOverall` |
| Foam | `enableFoam`, `foamTexture`, `foamIntensity` |
| SSS | `enableSSS`, `sssColor`, `sssIntensity` |
| Reflections | `specular`, `useExactFresnel`, `refractiveIndexWater` |
| Transparency | `enableTransparency`, `refractionStrength`, `aberrationAmount` |
| Caustics | `enableCaustics`, `causticsTexture`, `causticsStrength` |

## How It Differs From Flat Ocean

| Flat Ocean | Spherical Ocean |
|---|---|
| Y-axis displacement | Radial displacement from planet center |
| Planar LOD distance | Angular LOD (degrees around sphere) |
| XZ wave projection | Tangent-space wave projection |
| UV-sphere or grid mesh | True icosphere (subdivided icosahedron) |
| Upward buoyancy | Radial buoyancy |

## Architecture

```
SphericalOceanRenderer (C#)
├── BuildIcosphere()          — subdivided icosahedron mesh
├── Burst Jobs                — shallow water simulation
├── MaterialPropertyBlock     — per-frame GPU updates
└── SyncMaterialProperties()  — inspector change sync

SphericalOcean.shader (HLSL)
├── Vertex: Gerstner in tangent space + analytic foam Jacobian
├── Fragment: Triplanar normals, Fresnel, SSS, reflections
└── HDRP APIs: SampleSkyTexture, SampleCameraDepth, _DirectionalLightDatas
```

## Textures Required

The system needs these textures assigned in the inspector:

- **Normal Map** — any tiled water normal map (e.g., Unity's built-in or custom)
- **Foam Texture** — tiled foam pattern (optional, enable foam toggle)
- **Caustics Texture** — tiled caustics pattern (optional, enable caustics toggle)

## License

MIT — adapted from [Crest Ocean System](https://github.com/wave-harmonic/crest), [Optically Realistic Water](https://github.com/muckSponge/Optically-Realistic-Water), [Ocean-URP](https://github.com/gasgiant/Ocean-URP).
