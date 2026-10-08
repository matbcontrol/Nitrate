#ifndef NITRATE_FILM_NODES_INCLUDED
#define NITRATE_FILM_NODES_INCLUDED

// Code for the Custom Function nodes of NitrateFilm.shadergraph (Type: File).
// These are the parts of the film print that need a loop, an integer index or a hash, which the node library
// cannot express. They are copies of the helpers in NitrateCommon.hlsl and NitrateFilm.shader.
//
// NitrateCommon.hlsl is not included on purpose. The graph declares _NitrateFilmFrame and _NitrateBlueNoise as
// global properties, and NitrateCommon.hlsl declares the same names, so including both would define them twice.
// The blue noise texture arrives through a node input instead.
//
// All functions are _float only: the Custom Function nodes are set to Single precision.

static const float kNitrateFilmGoldenRatio = 0.61803398875;

float NitrateFilmHash11(float p)
{
    p = frac(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

float2 NitrateFilmHash21(float p)
{
    float3 p3 = frac(p * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// Blue noise in [0,1) for a pixel. Adding the golden ratio once per film frame re-rolls the pattern
// while every single frame stays blue-noise distributed.
float NitrateFilmBlueNoise(UnityTexture2D noise, int2 pixel, float seed)
{
    float n = LOAD_TEXTURE2D(noise.tex, pixel & 63).r;
    return frac(n + seed * kNitrateFilmGoldenRatio);
}

// Triangular-PDF noise in (-1, 1): the sum of two independent uniform values minus one.
float NitrateFilmBlueNoiseTPDF(UnityTexture2D noise, int2 pixel, float seed)
{
    return NitrateFilmBlueNoise(noise, pixel, seed) + NitrateFilmBlueNoise(noise, pixel + int2(17, 41), seed + 7.0) - 1.0;
}

// Hash11 node: one float in, one float out.
void NitrateHash11_float(float In, out float Out)
{
    Out = NitrateFilmHash11(In);
}

// Hash21 node: one float in, two floats out.
void NitrateHash21_float(float In, out float2 Out)
{
    Out = NitrateFilmHash21(In);
}

// Smoothly interpolated TPDF blue noise at a fractional cell position.
// The size the camera renders at (smaller than the screen when the render scale is below 1).
void NitrateScaledScreen_float(out float Width, out float Height)
{
    Width = _ScaledScreenParams.x;
    Height = _ScaledScreenParams.y;
}

void NitrateGrainTPDF_float(UnityTexture2D BlueNoise, float2 Cell, float Frame, out float Out)
{
    int2 b = int2(floor(Cell));
    float2 f = smoothstep(0.0, 1.0, Cell - b);
    float n00 = NitrateFilmBlueNoiseTPDF(BlueNoise, b, Frame), n10 = NitrateFilmBlueNoiseTPDF(BlueNoise, b + int2(1, 0), Frame);
    float n01 = NitrateFilmBlueNoiseTPDF(BlueNoise, b + int2(0, 1), Frame), n11 = NitrateFilmBlueNoiseTPDF(BlueNoise, b + int2(1, 1), Frame);
    // Bilinear blending lowers the variance; rescale so the strength stays what the slider says.
    Out = lerp(lerp(n00, n10, f.x), lerp(n01, n11, f.x), f.y) * 1.6;
}

// TPDF blue noise for the pixel under Position (pixel coordinates, as SV_Position). The graph adds 3 to the film frame
// and divides by 255 to get the one-step dither.
void NitrateDither_float(UnityTexture2D BlueNoise, float2 Position, float Frame, out float Out)
{
    Out = NitrateFilmBlueNoiseTPDF(BlueNoise, int2(Position), Frame);
}

// A few specks of dust per film frame and an occasional vertical scratch. Out = (amount, target value).
void NitrateDustAndScratches_float(float2 Position, float2 Size, float Frame, float Dust, out float2 Out)
{
    float scale = Size.y / 1080.0;
    float amount = 0.0, target = 0.0;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float2 h = NitrateFilmHash21(Frame * 3.0 + i);
        if (NitrateFilmHash11(Frame * 7.0 + i * 13.0) > Dust)
            continue;
        float2 center = h * Size;
        float radius = (1.0 + 2.5 * NitrateFilmHash11(Frame + i * 5.0)) * scale;
        float d = length(Position - center) / radius;
        float speck = saturate(1.0 - d * d);
        if (speck > amount) { amount = speck; target = NitrateFilmHash11(Frame * 11.0 + i) > 0.5 ? 1.0 : 0.0; }
    }
    // A scratch lives for 8 film frames and wanders slightly.
    float scratchId = floor(Frame / 8.0);
    if (NitrateFilmHash11(scratchId * 17.0) < 0.35 * Dust)
    {
        float x = (NitrateFilmHash11(scratchId * 23.0) + 0.002 * sin(Frame * 1.7)) * Size.x;
        float w = 0.8 * scale;
        float scratch = saturate(1.0 - abs(Position.x - x) / w) * 0.5;
        if (scratch > amount) { amount = scratch; target = 0.85; }
    }
    Out = float2(amount, target);
}

#endif
