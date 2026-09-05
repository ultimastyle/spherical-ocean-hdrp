// Spherical Ocean HDRP — Shader library globals
// Adapted from Crest Ocean System (MIT)

#ifndef SPHERICAL_OCEAN_GLOBALS_H
#define SPHERICAL_OCEAN_GLOBALS_H

// Sampler states
SamplerState LODData_linear_clamp_sampler;
SamplerState LODData_point_clamp_sampler;
SamplerState sampler_SphericalOcean_linear_repeat;

// Cascade parameters — matches C# struct
struct SphericalCascadeParams
{
    float2 _posSnapped;
    float _scale;
    float _textureRes;
    float _oneOverTextureRes;
    float _texelWidth;
    float _weight;
    float _maxWavelength;
};

StructuredBuffer<SphericalCascadeParams> _SphericalCascadeData;

// Per-cascade instance data
struct SphericalPerCascadeInstanceData
{
    float _meshScaleLerp;
    float _farNormalsWeight;
    float _geoGridWidth;
    float2 _normalScrollSpeeds;
    float3 __padding;
};

StructuredBuffer<SphericalPerCascadeInstanceData> _SphericalPerCascadeInstanceData;

// LOD texture arrays
Texture2DArray _LD_TexArray_AnimatedWaves;
Texture2DArray _LD_TexArray_SeaFloorDepth;
Texture2DArray _LD_TexArray_Foam;
Texture2DArray _LD_TexArray_Flow;
Texture2DArray _LD_TexArray_Shadow;

#endif // SPHERICAL_OCEAN_GLOBALS_H
