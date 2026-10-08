Shader "Nitrate/Film"
{
    Properties
    {
        [Header(Tone)]
        _Exposure ("Exposure", Float) = 1
        _BlackPoint ("Black Point (display value)", Range(0, 0.5)) = 0.03
        _WhitePoint ("White Point (display value)", Range(0.5, 1.5)) = 0.95
        _Gamma ("Midtone Gamma (above 1 = darker)", Range(0.3, 3)) = 1.15
        _Contrast ("S-Curve", Range(0, 1)) = 0.3
        _Ember ("Keep Colour of Saturated Pixels", Range(0, 1)) = 1
        _EmberThreshold ("Ember Saturation Threshold", Range(0, 1)) = 0.45

        [Header(Vignette)]
        _VignetteStrength ("Vignette Strength", Range(0, 1)) = 0.92
        _VignetteInner ("Vignette Inner Radius", Range(0, 1.5)) = 0.3
        _VignetteOuter ("Vignette Outer Radius", Range(0.1, 2)) = 1.05

        [Header(Film)]
        _GrainAmount ("Grain Amount (fraction of brightness)", Range(0, 0.2)) = 0.04
        _GrainSize ("Grain Size (pixels at 1080p, streaks are 2.5x wider)", Range(1, 4)) = 2
        _Flicker ("Flicker (exposure, +/-)", Range(0, 0.1)) = 0.015
        _Weave ("Gate Weave (pixels at 1080p)", Range(0, 2)) = 0.35
        _Dust ("Dust and Scratches", Range(0, 1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "NitrateCommon.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float _Exposure;
        float _BlackPoint;
        float _WhitePoint;
        float _Gamma;
        float _Contrast;
        float _Ember;
        float _EmberThreshold;
        float _VignetteStrength;
        float _VignetteInner;
        float _VignetteOuter;
        float _GrainAmount;
        float _GrainSize;
        float _Flicker;
        float _Weave;
        float _Dust;
    CBUFFER_END

    // Bilinear fetch by pixel position (the gate weave moves the image by a fraction of a pixel).
    // Done with loads, so it does not depend on how big the render target behind _BlitTexture really is.
    float3 LoadBilinear(float2 position, int2 size)
    {
        float2 q = position - 0.5;
        int2 b = int2(floor(q));
        float2 f = q - b;
        int2 hi = size - 1;
        float3 c00 = LOAD_TEXTURE2D_X(_BlitTexture, clamp(b, int2(0, 0), hi)).rgb;
        float3 c10 = LOAD_TEXTURE2D_X(_BlitTexture, clamp(b + int2(1, 0), int2(0, 0), hi)).rgb;
        float3 c01 = LOAD_TEXTURE2D_X(_BlitTexture, clamp(b + int2(0, 1), int2(0, 0), hi)).rgb;
        float3 c11 = LOAD_TEXTURE2D_X(_BlitTexture, clamp(b + int2(1, 1), int2(0, 0), hi)).rgb;
        return lerp(lerp(c00, c10, f.x), lerp(c01, c11, f.x), f.y);
    }

    // Smoothly interpolated TPDF blue noise at a fractional cell position.
    float GrainTPDF(float2 cell, float frame)
    {
        int2 b = int2(floor(cell));
        float2 f = smoothstep(0.0, 1.0, cell - b);
        float n00 = BlueNoiseTPDF(b, frame), n10 = BlueNoiseTPDF(b + int2(1, 0), frame);
        float n01 = BlueNoiseTPDF(b + int2(0, 1), frame), n11 = BlueNoiseTPDF(b + int2(1, 1), frame);
        // Bilinear blending lowers the variance; rescale so the strength stays what the slider says.
        return lerp(lerp(n00, n10, f.x), lerp(n01, n11, f.x), f.y) * 1.6;
    }

    // A few specks of dust per film frame and an occasional vertical scratch. Returns (amount, target value).
    float2 DustAndScratches(float2 position, float2 size, float frame)
    {
        float scale = size.y / 1080.0;
        float amount = 0.0, target = 0.0;
        [unroll]
        for (int i = 0; i < 3; i++)
        {
            float2 h = Hash21(frame * 3.0 + i);
            if (Hash11(frame * 7.0 + i * 13.0) > _Dust)
                continue;
            float2 center = h * size;
            float radius = (1.0 + 2.5 * Hash11(frame + i * 5.0)) * scale;
            float d = length(position - center) / radius;
            float speck = saturate(1.0 - d * d);
            if (speck > amount) { amount = speck; target = Hash11(frame * 11.0 + i) > 0.5 ? 1.0 : 0.0; }
        }
        // A scratch lives for 8 film frames and wanders slightly.
        float scratchId = floor(frame / 8.0);
        if (Hash11(scratchId * 17.0) < 0.35 * _Dust)
        {
            float x = (Hash11(scratchId * 23.0) + 0.002 * sin(frame * 1.7)) * size.x;
            float w = 0.8 * scale;
            float scratch = saturate(1.0 - abs(position.x - x) / w) * 0.5;
            if (scratch > amount) { amount = scratch; target = 0.85; }
        }
        return float2(amount, target);
    }

    half4 FragFilm(Varyings input) : SV_Target
    {
        int2 size = int2(_ScaledScreenParams.xy);
        float2 position = input.positionCS.xy;
        float2 uv = position / _ScaledScreenParams.xy;
        float frame = _NitrateFilmFrame;
        float scale = _ScaledScreenParams.y / 1080.0;

        // Gate weave: the film moves a little in the projector gate, a different offset every film frame.
        float2 weave = (float2(sin(frame * 0.37), sin(frame * 0.23 + 1.7)) * 0.7 + (Hash21(frame) - 0.5) * 0.6) * _Weave * scale;
        float3 color = LoadBilinear(position + weave, size);

        // Flicker: exposure changes from film frame to film frame, with a rare deeper dip.
        float flicker = (Hash11(frame) * 2.0 - 1.0) * _Flicker;
        flicker -= Hash11(frame * 1.31 + 5.0) > 0.97 ? 3.0 * _Flicker : 0.0;
        color *= _Exposure * (1.0 + flicker);

        // Monochrome, except strongly saturated pixels (the lantern), if Ember is on.
        float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
        float maxC = max(color.r, max(color.g, color.b));
        float saturation = (maxC - min(color.r, min(color.g, color.b))) / max(maxC, 1e-4);
        // Tone curve in display space: black point, white point, midtone gamma, S-curve.
        float y = LinearToSRGB1(luma);
        // Only bright, saturated pixels keep their colour, so a faint warm tint in the fog stays grey.
        float keep = smoothstep(_EmberThreshold, _EmberThreshold + 0.2, saturation) * smoothstep(0.3, 0.6, y) * _Ember;
        y = saturate((y - _BlackPoint) / max(_WhitePoint - _BlackPoint, 1e-3));
        y = pow(y, _Gamma);
        y = lerp(y, y * y * (3.0 - 2.0 * y), _Contrast);

        // Hard vignette: the frame corners go almost black, like an old lens and projector.
        float aspect = _ScaledScreenParams.x / _ScaledScreenParams.y;
        float r = length((uv - 0.5) * float2(aspect, 1.0)) / (0.5 * length(float2(aspect, 1.0)));
        y *= 1.0 - _VignetteStrength * smoothstep(_VignetteInner, _VignetteOuter, r);

        float2 dust = DustAndScratches(position, _ScaledScreenParams.xy, frame);
        y = lerp(y, dust.y, dust.x);

        // Grain: blue noise with a triangular distribution, a new pattern every film frame (24 Hz). Film grain clumps
        // into short horizontal streaks (measured on LIMBO: about 5 x 2 px), so the noise is stretched and smoothly
        // interpolated. Its strength follows brightness, like real grain. Then a 1-step dither so the 8-bit output
        // has no bands.
        float grain = GrainTPDF(position / (_GrainSize * scale * float2(2.5, 1.0)), frame) * _GrainAmount * (0.15 + y);
        float dither = BlueNoiseTPDF(int2(position), frame + 3.0) / 255.0;
        y = saturate(y + grain + dither);

        float linearY = SRGBToLinear1(y);
        float3 mono = linearY.xxx;
        float3 tinted = color * (linearY / max(luma, 1e-5));
        return half4(lerp(mono, tinted, keep), 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "Nitrate Film"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragFilm
            ENDHLSL
        }
    }
}
