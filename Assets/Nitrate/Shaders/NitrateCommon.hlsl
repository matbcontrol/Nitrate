#ifndef NITRATE_COMMON_INCLUDED
#define NITRATE_COMMON_INCLUDED

// Scene state, pushed every frame by NitrateAtmosphere.cs as global shader properties.
// These are globals (not material properties) because the light, the camera rig and the lantern move at runtime.
float4 _NitrateLightDir;     // xyz: direction TO the moon, normalized
float4 _NitrateMoonColor;    // rgb: colour * intensity of the moonlight in the air
float4 _NitrateFogPlane;     // xyz: normal, w: offset. Fog grows with the distance BEHIND this plane (the figure's plane)
float4 _NitrateBoxMin;       // the box where the beams live (world space); rays are clipped to it
float4 _NitrateBoxMax;
float4 _NitrateLanternPos;   // xyz: lantern position, w: intensity
float4 _NitrateLanternColor;
float  _NitrateFilmFrame;    // advances 24 times a second, like a film projector

// Wind, pushed every frame by NitrateAtmosphere.cs. One function drives grass, trees, moss and the water ripples,
// so a gust visibly rolls across the whole scene. Every period divides the 15 s loop, so recorded loops stay seamless.
float4 _NitrateWind;         // xyz: horizontal direction (normalized), w: bend in metres at full flexibility
float4 _NitrateWindParams;   // x: clock (s), y: gust travel speed (m/s), z: flutter share, w: flutter frequency (Hz)

// Gust strength around 0.2..1.0 at a world position: two slow waves that travel along the wind direction.
float NitrateGust(float3 positionWS)
{
    float t = _NitrateWindParams.x;
    float along = dot(positionWS, _NitrateWind.xyz) / max(_NitrateWindParams.y, 0.01);
    return 0.55 + 0.3 * sin(2.0 * PI * (t - along) / 7.5) + 0.15 * sin(2.0 * PI * (t - along) / 3.75 + 1.3);
}

// Wind displacement of a vertex. flexibility: 0 at a root, 1 at a free tip. phase: per-blade random 0..1,
// so neighbouring blades do not flutter in unison.
float3 NitrateWindOffset(float3 positionWS, float flexibility, float phase)
{
    if (flexibility <= 0.0)
        return 0.0;
    float gust = NitrateGust(positionWS);
    float flutter = sin(2.0 * PI * (_NitrateWindParams.x * _NitrateWindParams.w + phase) + positionWS.y * 1.7) * _NitrateWindParams.z;
    float bend = flexibility * flexibility * _NitrateWind.w * (gust + flutter);
    // Bend along the wind and sink a little, so a bending blade keeps roughly its length.
    return _NitrateWind.xyz * bend + float3(0.0, -0.3 * bend * bend, 0.0);
}

// 64x64 single-channel tiling blue noise (load it with point filtering, no sRGB, no mips).
TEXTURE2D(_NitrateBlueNoise);

static const float kGoldenRatio = 0.61803398875;

// Blue noise in [0,1) for a pixel. Adding the golden ratio once per film frame re-rolls the pattern
// while every single frame stays blue-noise distributed (spatio-temporal blue noise, the cheap way).
float BlueNoise(int2 pixel, float seed)
{
    float n = LOAD_TEXTURE2D(_NitrateBlueNoise, pixel & 63).r;
    return frac(n + seed * kGoldenRatio);
}

// Triangular-PDF noise in (-1, 1): the sum of two independent uniform values minus one.
// Dithering with TPDF noise removes banding without making the noise depend on the signal (Gjoel, "Banding in Games", 2014).
float BlueNoiseTPDF(int2 pixel, float seed)
{
    return BlueNoise(pixel, seed) + BlueNoise(pixel + int2(17, 41), seed + 7.0) - 1.0;
}

float Hash11(float p)
{
    p = frac(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

float2 Hash21(float p)
{
    float3 p3 = frac(p * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// Henyey-Greenstein phase function: how much light scatters toward the viewer.
// g > 0 scatters forward, so beams are brightest when you look toward the moon.
float PhaseHG(float cosTheta, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4), 1.5));
}

// Ray against an axis-aligned box: returns (entry, exit) distances. exit < entry means a miss.
float2 RayBox(float3 origin, float3 dir, float3 boxMin, float3 boxMax)
{
    float3 inv = 1.0 / dir;
    float3 t0 = (boxMin - origin) * inv;
    float3 t1 = (boxMax - origin) * inv;
    float3 tMin = min(t0, t1);
    float3 tMax = max(t0, t1);
    return float2(max(max(tMin.x, tMin.y), tMin.z), min(min(tMax.x, tMax.y), tMax.z));
}

bool IsSkyDepth(float rawDepth)
{
#if UNITY_REVERSED_Z
    return rawDepth <= 1e-6;
#else
    return rawDepth >= 1.0 - 1e-6;
#endif
}

float LinearToSRGB1(float c)
{
    c = max(c, 0.0);
    return c <= 0.0031308 ? 12.92 * c : 1.055 * pow(c, 1.0 / 2.4) - 0.055;
}

float SRGBToLinear1(float c)
{
    c = max(c, 0.0);
    return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
}

#endif
