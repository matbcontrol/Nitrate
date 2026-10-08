Shader "Nitrate/Atmosphere"
{
    Properties
    {
        [Header(Sky)]
        _HorizonColor ("Horizon Haze", Color) = (0.12, 0.12, 0.12, 1)
        _ZenithColor ("Zenith", Color) = (0.015, 0.015, 0.015, 1)
        _NadirColor ("Below Horizon (fog over water)", Color) = (0.02, 0.02, 0.02, 1)
        _SkyGradient ("Sky Gradient (x start, y end, sine of elevation)", Vector) = (-0.02, 0.5, 0, 0)
        _GlowWide ("Wide Moon Glow (x intensity, y width deg)", Vector) = (1.2, 9, 0, 0)
        _GlowTight ("Tight Moon Glow (x intensity, y width deg)", Vector) = (2.5, 2.2, 0, 0)
        _GlowOnFog ("Share of Wide Glow on Fogged Objects", Range(0, 1)) = 0.35
        _MoonRadius ("Moon Radius (degrees)", Float) = 1.35
        _MoonIntensity ("Moon Disc Intensity (HDR)", Float) = 6

        [Header(Fog)]
        _FogDistance ("Fog Distance (m behind the figure)", Float) = 120
        _FogGamma ("Fog Curve (above 1 keeps near layers dark)", Range(0.5, 4)) = 1.6
        _FogMax ("Fog Max (below 1 so lights shine through)", Range(0, 1)) = 0.95
        _MistAmount ("Ground Mist", Range(0, 1)) = 0.15
        _MistHeight ("Ground Mist Height (m above Box Min)", Float) = 2.5

        [Header(Beams)]
        _BeamDensity ("Beam Density", Float) = 0.004
        _BeamHeightFalloff ("Beam Height Falloff (m)", Float) = 25
        _BeamAnisotropy ("Beam Anisotropy g", Range(0, 0.95)) = 0.8
        _BeamIntensity ("Beam Intensity", Float) = 2
        _BeamMaxDistance ("Beam Max Distance (m)", Float) = 200
        _BeamFloor ("Beam Floor (removes the even veil)", Float) = 0.02
        _BeamGain ("Beam Gain after the floor", Float) = 1.5
        _BlurDepthTolerance ("Blur and Upsample Depth Tolerance", Float) = 0.1
        _HeroMask ("Hero Mask (m behind the figure before beams and glow appear)", Float) = 4

        [Header(Depth blur ladder)]
        _BlurFar1 ("Far Blur level 1: full at this many m behind", Float) = 40
        _BlurFar2 ("Far Blur level 2: full at this many m behind", Float) = 160
        _BlurSky ("Sky Blur (level 2 weight)", Range(0, 1)) = 1

        [Header(Water)]
        _WaterLevel ("Water Level (world y)", Float) = 0
        _WaterReflect ("Water Reflection Strength", Range(0, 2)) = 0.9
        _Ripple ("Ripple Strength", Range(0, 0.3)) = 0.09

        [Header(Dust in the beams)]
        _Motes ("Dust Motes", Range(0, 3)) = 1

        [Header(Lantern glow in the fog)]
        _LanternScatter ("Lantern Scatter", Float) = 0.025
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "NitrateCommon.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _HorizonColor;
        half4 _ZenithColor;
        half4 _NadirColor;
        float4 _SkyGradient;
        float4 _GlowWide;
        float4 _GlowTight;
        float _GlowOnFog;
        float _MoonRadius;
        float _MoonIntensity;
        float _FogDistance;
        float _FogGamma;
        float _FogMax;
        float _MistAmount;
        float _MistHeight;
        float _BeamDensity;
        float _BeamHeightFalloff;
        float _BeamAnisotropy;
        float _BeamIntensity;
        float _BeamMaxDistance;
        float _BeamFloor;
        float _BeamGain;
        float _BlurDepthTolerance;
        float _HeroMask;
        float _LanternScatter;
        float _BlurFar1;
        float _BlurFar2;
        float _BlurSky;
        float _WaterLevel;
        float _WaterReflect;
        float _Ripple;
        float _Motes;
    CBUFFER_END

    // Set per draw by NitrateAtmosphereFeature.cs (MaterialPropertyBlock), not by the material.
    TEXTURE2D(_NitrateBeamTex);  // R: in-scattered moonlight, G: eye depth of the texel
    float4 _NitrateBeamSize;     // xy: beam texture size in texels, zw: 1 / size
    float _NitrateBeamSteps;
    float _NitrateBeamsOn;
    float _NitrateGlowLate;      // 1: the depth blur pass adds the lantern glow (after blurring), so the composite skips it
    float _NitrateDebug;         // 0 off, 1 shadow at depth, 2 beams only, 3 fog amount
    float2 _NitrateBlurDir;
    TEXTURE2D(_NitrateBlur1);    // quarter resolution: rgb colour, a eye depth
    TEXTURE2D(_NitrateBlur2);    // eighth resolution
    float4 _NitrateBlur1Size;    // xy size, zw 1/size
    float4 _NitrateBlur2Size;

    struct ViewRay
    {
        float3 origin;
        float3 dir;
        float distance;   // to the surface, or to the far plane for sky pixels
        float eyeDepth;
        float3 positionWS;
        bool sky;
    };

    // uv is the normalized screen position (0..1), the same convention ComputeWorldSpacePosition expects.
    ViewRay GetViewRay(float2 uv, float rawDepth)
    {
        ViewRay r;
        r.sky = IsSkyDepth(rawDepth);
        r.positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
        r.origin = GetCameraPositionWS();
        float3 toPixel = r.positionWS - r.origin;
        r.distance = length(toPixel);
        r.dir = toPixel / max(r.distance, 1e-4);
        r.eyeDepth = r.sky ? 1e4 : LinearEyeDepth(rawDepth, _ZBufferParams);
        return r;
    }

    // One function for the sky colour, used for sky pixels AND as the fog colour, so fogged geometry dissolves
    // into the sky behind it. The moon glow has a wide and a tight lobe; fogged objects only get part of the wide one,
    // so a silhouette in front of the moon stays dark against the light (the eclipse).
    float3 SkyColor(float3 dir, float wideGlow, float tightGlow)
    {
        float t = smoothstep(_SkyGradient.x, _SkyGradient.y, dir.y);
        float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, t);
        sky = lerp(sky, _NadirColor.rgb, smoothstep(0.0, -0.12, dir.y));
        float angle = degrees(acos(clamp(dot(dir, _NitrateLightDir.xyz), -1.0, 1.0)));
        float wide = _GlowWide.x * exp(-pow(angle / max(_GlowWide.y, 0.01), 2.0));
        float tight = _GlowTight.x * exp(-pow(angle / max(_GlowTight.y, 0.01), 2.0));
        return sky + _NitrateMoonColor.rgb * (wide * wideGlow + tight * tightGlow);
    }

    float MoonDisc(float3 dir)
    {
        float cosMoon = dot(dir, _NitrateLightDir.xyz);
        float radius = radians(_MoonRadius);
        float edge = radius * 0.04;
        return smoothstep(cos(radius + edge), cos(radius - edge), cosMoon) * _MoonIntensity;
    }

    // Main light shadow at a world position: 1 = lit, 0 = in shadow.
    // One cascade only (the URP asset is set to 1 cascade), so no shadow keywords or cascade search are needed.
    float MoonShadow(float3 positionWS)
    {
        float3 coord = mul(_MainLightWorldToShadow[0], float4(positionWS, 1.0)).xyz;
        float lit = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, coord);
        bool outside = coord.z <= 0.0 || coord.z >= 1.0 || any(coord.xy != saturate(coord.xy));
        return outside ? 1.0 : lit;
    }

    // ---------------------------------------------------------------------------------------------
    // Pass 0: beams by ray marching through the moon's shadow map, at reduced resolution.
    // ---------------------------------------------------------------------------------------------
    float4 FragBeams(Varyings input) : SV_Target
    {
        int2 p = int2(input.positionCS.xy);
        float2 uv = (float2(p) + 0.5) * _NitrateBeamSize.zw;
        int2 fullPixel = min(int2(uv * _ScaledScreenParams.xy), int2(_ScaledScreenParams.xy) - 1);
        float rawDepth = LoadSceneDepth(fullPixel);
        ViewRay ray = GetViewRay(uv, rawDepth);

        // Spend the steps only where there is something to see: inside the beam box and in front of the surface.
        float2 hit = RayBox(ray.origin, ray.dir, _NitrateBoxMin.xyz, _NitrateBoxMax.xyz);
        float tStart = max(hit.x, 0.0);
        float tEnd = min(min(hit.y, ray.distance), _BeamMaxDistance);
        if (tEnd <= tStart)
            return float4(0.0, ray.eyeDepth, 0.0, 0.0);

        int steps = max((int)_NitrateBeamSteps, 1);
        float stepLength = (tEnd - tStart) / steps;
        // Blue-noise start offset, re-rolled 24 times a second: the banding of a few steps turns into fine noise
        // that the blur removes and the film grain hides. No temporal accumulation, so nothing ghosts.
        float jitter = BlueNoise(p, _NitrateFilmFrame);
        float t = tStart + stepLength * jitter;

        float sum = 0.0;
        [loop]
        for (int i = 0; i < steps; i++)
        {
            float3 position = ray.origin + ray.dir * t;
            float height = max(position.y - _NitrateBoxMin.y, 0.0);
            float density = exp(-height / max(_BeamHeightFalloff, 0.01));
            sum += MoonShadow(position) * density;
            t += stepLength;
        }

        float cosTheta = dot(ray.dir, _NitrateLightDir.xyz);
        float phase = lerp(1.0 / (4.0 * PI), PhaseHG(cosTheta, _BeamAnisotropy), 0.85);
        float scatter = sum * stepLength * _BeamDensity * phase * _BeamIntensity;
        return float4(scatter, ray.eyeDepth, 0.0, 0.0);
    }

    // ---------------------------------------------------------------------------------------------
    // Pass 1: fallback beams, screen-space light scattering (GPU Gems 3, ch. 13).
    // Marches from the pixel toward the moon on screen and counts open sky. Works only while the moon is on screen.
    // ---------------------------------------------------------------------------------------------
    float4 FragBeamsRadial(Varyings input) : SV_Target
    {
        int2 p = int2(input.positionCS.xy);
        float2 uv = (float2(p) + 0.5) * _NitrateBeamSize.zw;
        int2 size = int2(_ScaledScreenParams.xy);
        float rawDepth = LoadSceneDepth(min(int2(uv * size), size - 1));
        ViewRay ray = GetViewRay(uv, rawDepth);

        float3 moonWS = ray.origin + _NitrateLightDir.xyz * 1000.0;
        float4 moonCS = mul(UNITY_MATRIX_VP, float4(moonWS, 1.0));
        if (moonCS.w <= 0.0)
            return float4(0.0, ray.eyeDepth, 0.0, 0.0);
        float2 moonUV = ComputeNormalizedDeviceCoordinates(moonWS, UNITY_MATRIX_VP);

        int steps = max((int)_NitrateBeamSteps * 2, 2);
        float2 delta = (moonUV - uv) / steps;
        float2 sampleUV = uv + delta * BlueNoise(p, _NitrateFilmFrame);
        float decay = 1.0, sum = 0.0;
        [loop]
        for (int i = 0; i < steps; i++)
        {
            int2 q = clamp(int2(sampleUV * size), int2(0, 0), size - 1);
            sum += IsSkyDepth(LoadSceneDepth(q)) ? decay : 0.0;
            decay *= 0.97;
            sampleUV += delta;
        }
        float cosTheta = dot(ray.dir, _NitrateLightDir.xyz);
        float scatter = sum / steps * PhaseHG(cosTheta, _BeamAnisotropy) * _BeamIntensity * _BeamDensity * 20.0;
        return float4(scatter, ray.eyeDepth, 0.0, 0.0);
    }

    // ---------------------------------------------------------------------------------------------
    // Pass 2 (run twice, horizontal then vertical): separable blur of the beam texture that does not bleed across depth edges.
    // ---------------------------------------------------------------------------------------------
    float4 FragBlur(Varyings input) : SV_Target
    {
        int2 p = int2(input.positionCS.xy);
        int2 size = int2(_NitrateBeamSize.xy);
        float2 center = LOAD_TEXTURE2D(_BlitTexture, p).rg;
        // 9 taps, Gaussian with sigma = 2 texels: wide enough to turn the quarter-resolution noise into soft shafts.
        static const float kWeights[5] = { 1.0, 0.8825, 0.6065, 0.3247, 0.1353 };
        float sum = center.r * kWeights[0];
        float weightSum = kWeights[0];
        [unroll]
        for (int k = 1; k <= 4; k++)
        {
            [unroll]
            for (int s = -1; s <= 1; s += 2)
            {
                int2 q = clamp(p + int2(_NitrateBlurDir) * k * s, int2(0, 0), size - 1);
                float2 tap = LOAD_TEXTURE2D(_BlitTexture, q).rg;
                float w = kWeights[k] * saturate(1.0 - abs(tap.g - center.g) / (center.g * _BlurDepthTolerance));
                sum += tap.r * w;
                weightSum += w;
            }
        }
        return float4(sum / weightSum, center.g, 0.0, 0.0);
    }

    // Bilinear upsample of the low-resolution beams that ignores texels from a different depth,
    // so beams don't leak over the edges of silhouettes.
    float UpsampleBeams(int2 pixel, float eyeDepth)
    {
        float2 beamPos = (float2(pixel) + 0.5) / _ScaledScreenParams.xy * _NitrateBeamSize.xy - 0.5;
        int2 base = int2(floor(beamPos));
        float2 f = beamPos - base;
        int2 maxTexel = int2(_NitrateBeamSize.xy) - 1;
        float4 bilinear = float4((1 - f.x) * (1 - f.y), f.x * (1 - f.y), (1 - f.x) * f.y, f.x * f.y);
        int2 offsets[4] = { int2(0, 0), int2(1, 0), int2(0, 1), int2(1, 1) };

        float sum = 0.0, weightSum = 0.0, nearest = 0.0, nearestDelta = 1e9;
        [unroll]
        for (int i = 0; i < 4; i++)
        {
            float2 tap = LOAD_TEXTURE2D(_NitrateBeamTex, clamp(base + offsets[i], int2(0, 0), maxTexel)).rg;
            float delta = abs(tap.g - eyeDepth);
            float w = bilinear[i] * saturate(1.0 - delta / (eyeDepth * _BlurDepthTolerance));
            sum += tap.r * w;
            weightSum += w;
            if (delta < nearestDelta) { nearestDelta = delta; nearest = tap.r; }
        }
        return weightSum > 1e-3 ? sum / weightSum : nearest;
    }

    // Glow of a point light in fog along the view ray, in closed form: the integral of 1 / distance^2
    // along the ray is an arctangent. No shadows, but cut at the first surface, so silhouettes occlude it.
    float LanternGlow(float3 origin, float3 dir, float length)
    {
        float3 v = origin - _NitrateLanternPos.xyz;
        float b = dot(dir, v);
        float h = sqrt(max(dot(v, v) - b * b, 1e-4));
        return (atan((length + b) / h) - atan(b / h)) / h;
    }

    // The lantern flame flickers a little, a new value every film frame.
    float LanternFlicker()
    {
        return 1.0 + 0.12 * (Hash11(floor(_NitrateFilmFrame) * 1.7 + 3.0) * 2.0 - 1.0);
    }

    // Lantern light scattered in the fog. Masked like the beams: nothing on or in front of the figure's plane.
    float3 LanternFog(ViewRay ray, float behind)
    {
        float glowLength = ray.sky ? _BeamMaxDistance : ray.distance;
        float hero = saturate(behind / max(_HeroMask, 0.01));
        return _NitrateLanternColor.rgb * (_NitrateLanternPos.w * LanternFlicker() * _LanternScatter * LanternGlow(ray.origin, ray.dir, glowLength)) * hero;
    }

    // Water: a dark mirror. Ripples are a few crossed sine waves stepped at 24 Hz; they bend the normal mostly along
    // the view (z), so the reflected moon breaks into horizontal slivers, the way a moon path does.
    float3 WaterReflection(ViewRay ray, float flicker)
    {
        float t = floor(_NitrateFilmFrame) / 24.0;
        // Ripples run downwind and grow in gusts: the same gusts that roll through the grass darken the water in patches.
        float gust = NitrateGust(ray.positionWS);
        float3 p = ray.positionWS - _NitrateWind.xyz * (t * 1.5);
        float2 ripple;
        ripple.x = 0.25 * sin(p.x * 1.3 + p.z * 0.4 + t * 1.1) + 0.15 * sin(p.x * 3.1 - p.z * 1.7 + t * 2.3);
        ripple.y = sin(p.z * 2.3 + t * 1.7) * sin(p.x * 0.5 + p.z * 0.9 - t) + 0.5 * sin(p.z * 5.7 - t * 2.9);
        float strength = _Ripple * (0.35 + 1.1 * gust);
        float3 n = normalize(float3(ripple.x * strength * 0.4, 1.0, ripple.y * strength));
        float3 r = reflect(ray.dir, n);
        r.y = abs(r.y);
        float cosine = saturate(dot(-ray.dir, n));
        float fresnel = 0.02 + 0.98 * pow(1.0 - cosine, 5.0);
        float3 sky = SkyColor(r, 1.0, 1.0) + _NitrateMoonColor.rgb * MoonDisc(r) * 0.7;
        float3 lantern = _NitrateLanternColor.rgb * (_NitrateLanternPos.w * flicker * _LanternScatter * LanternGlow(p, r, 60.0));
        return (sky + lantern) * fresnel * _WaterReflect;
    }

    // Dust: two layers of tiny specks drifting slowly up and left, visible only where a beam lights them.
    float Motes(int2 pixel, float beams)
    {
        float t = floor(_NitrateFilmFrame) / 24.0;
        float scale = _ScaledScreenParams.y / 1080.0;
        float sum = 0.0;
        [unroll]
        for (int layer = 0; layer < 2; layer++)
        {
            float cells = layer == 0 ? 38.0 : 18.0;
            float2 uv = float2(pixel) / _ScaledScreenParams.y * cells + float2(0.0667, 0.0333) * t * (layer + 1) * cells * 0.25;
            float2 cell = floor(uv);
            float2 h = Hash21(cell.x * 57.0 + cell.y * 113.0 + layer * 7.0);
            if (h.x > 0.35) continue;                          // most cells are empty
            float2 center = cell + 0.2 + 0.6 * Hash21(cell.x * 13.0 + cell.y * 71.0 + layer);
            float radius = (0.8 + h.y * 1.2) * scale * cells / _ScaledScreenParams.y;
            float d = length(uv - center) / max(radius, 1e-4);
            float twinkle = 0.5 + 0.5 * Hash11(cell.x + cell.y * 31.0 + floor(_NitrateFilmFrame) * 0.37);
            sum += saturate(1.0 - d) * twinkle;
        }
        return sum * beams * _Motes * 4.0;
    }

    // ---------------------------------------------------------------------------------------------
    // Pass 4: composite. Sky, fog measured from the figure's plane, beams, lantern glow.
    // ---------------------------------------------------------------------------------------------
    half4 FragComposite(Varyings input) : SV_Target
    {
        int2 p = int2(input.positionCS.xy);
        float2 uv = (float2(p) + 0.5) / _ScaledScreenParams.xy;
        float3 scene = LOAD_TEXTURE2D_X(_BlitTexture, p).rgb;
        float rawDepth = LoadSceneDepth(p);
        ViewRay ray = GetViewRay(uv, rawDepth);

        // Distance behind the figure's plane. Fog, beams and glow are all functions of it, so every object keeps
        // its grey value during the dolly zoom, and everything on or in front of the plane stays pure black.
        float behind = ray.sky ? 1e4 : max(dot(_NitrateFogPlane.xyz, ray.positionWS) + _NitrateFogPlane.w, 0.0);

        float flicker = LanternFlicker();

        float3 color;
        float fog = 1.0;
        if (ray.sky)
        {
            color = SkyColor(ray.dir, 1.0, 1.0) + _NitrateMoonColor.rgb * MoonDisc(ray.dir);
        }
        else
        {
            // A curved ramp: near layers stay almost black, far layers climb quickly into the haze.
            fog = pow(1.0 - exp(-behind / max(_FogDistance, 0.01)), _FogGamma);
            float low = saturate(1.0 - (ray.positionWS.y - _NitrateBoxMin.y) / max(_MistHeight, 0.01));
            fog += (1.0 - fog) * _MistAmount * low * saturate(behind * 0.05);
            fog = min(fog, _FogMax);
            float water = ray.positionWS.y < _WaterLevel + 0.05 ? 1.0 : 0.0;
            float3 surface = scene + water * WaterReflection(ray, flicker);
            color = lerp(surface, SkyColor(ray.dir, _GlowOnFog, 0.0), fog);
        }

        // Hero mask: the figure's plane and everything in front of it get no beams and no glow (LIMBO keeps them black).
        float hero = saturate(behind / max(_HeroMask, 0.01));

        float beams = _NitrateBeamsOn > 0.5 ? UpsampleBeams(p, ray.eyeDepth) : 0.0;
        // Remove the even veil every unshadowed ray collects, so the shafts read as light with dark gaps between them.
        beams = max(beams - _BeamFloor, 0.0) * _BeamGain;
        color += _NitrateMoonColor.rgb * beams * hero;
        color += _NitrateMoonColor.rgb * Motes(p, beams * hero);

        // The glow lives around the lantern, in the sharp plane: with the depth blur on, it is added after the blur,
        // otherwise each depth layer behind it would blur it by a different amount and cut it into blocks.
        if (_NitrateGlowLate < 0.5)
            color += LanternFog(ray, behind);

        if (_NitrateDebug > 0.5)
        {
            if (_NitrateDebug < 1.5)
                color = ray.sky ? float3(0.2, 0.2, 0.6) : MoonShadow(ray.positionWS).xxx;
            else if (_NitrateDebug < 2.5)
                color = _NitrateMoonColor.rgb * beams;
            else
                color = fog.xxx;
        }
        return half4(color, 1.0);
    }
    // ---------------------------------------------------------------------------------------------
    // Depth blur ladder (replaces the Bokeh depth of field). LIMBO's depth reads from blur as much as from fog:
    // the figure's plane is sharp, near shapes go soft, far layers get softer the farther they are.
    // ---------------------------------------------------------------------------------------------

    // The blur ladder works on compressed colour, c / (1 + luma). The moon behind the hub is many times brighter than
    // white; blurred as is, it would flood the black sails crossing it and they would vanish in the glare. Compressed,
    // a black lattice in front of the moon stays dark when blurred, and smooth areas come back unchanged.
    float3 CompressHdr(float3 c) { return c / (1.0 + dot(c, float3(0.2126, 0.7152, 0.0722))); }
    float3 ExpandHdr(float3 c) { return c / max(1.0 - dot(c, float3(0.2126, 0.7152, 0.0722)), 1e-3); }

    // Pass 4: full resolution -> quarter resolution, colour (compressed) plus eye depth.
    float4 FragDownsample(Varyings input) : SV_Target
    {
        int2 o = int2(input.positionCS.xy) * 4;
        int2 hi = int2(_ScaledScreenParams.xy) - 1;
        float3 c = 0.0;
        float depth = 0.0;
        int2 taps[4] = { int2(1, 1), int2(3, 1), int2(1, 3), int2(3, 3) };
        [unroll]
        for (int i = 0; i < 4; i++)
        {
            int2 q = min(o + taps[i], hi);
            c += CompressHdr(LOAD_TEXTURE2D_X(_BlitTexture, q).rgb);
            float raw = LoadSceneDepth(q);
            depth += IsSkyDepth(raw) ? 1e4 : LinearEyeDepth(raw, _ZBufferParams);
        }
        return float4(c * 0.25, depth * 0.25);
    }

    // Pass 5: separable 9-tap blur of colour that ignores samples clearly nearer than the centre,
    // so a sharp foreground silhouette never smears a dark halo onto the blurred background behind it.
    float4 FragBlurColor(Varyings input) : SV_Target
    {
        int2 p = int2(input.positionCS.xy);
        int2 size = int2(_NitrateBeamSize.xy);
        float4 center = LOAD_TEXTURE2D(_BlitTexture, p);
        static const float kWeights[5] = { 1.0, 0.8825, 0.6065, 0.3247, 0.1353 };
        float3 sum = center.rgb * kWeights[0];
        float weightSum = kWeights[0];
        [unroll]
        for (int k = 1; k <= 4; k++)
        {
            [unroll]
            for (int s = -1; s <= 1; s += 2)
            {
                float4 tap = LOAD_TEXTURE2D(_BlitTexture, clamp(p + int2(_NitrateBlurDir) * k * s, int2(0, 0), size - 1));
                float w = kWeights[k] * (tap.a > center.a * 0.85 ? 1.0 : 0.05);
                sum += tap.rgb * w;
                weightSum += w;
            }
        }
        return float4(sum / weightSum, center.a);
    }

    // Pass 6: halve the resolution again (average of 4 texels, depth averaged too).
    float4 FragHalf(Varyings input) : SV_Target
    {
        int2 o = int2(input.positionCS.xy) * 2;
        int2 hi = int2(_NitrateBeamSize.xy) - 1; // here: the size of the source texture
        float4 c = LOAD_TEXTURE2D(_BlitTexture, min(o, hi)) + LOAD_TEXTURE2D(_BlitTexture, min(o + int2(1, 0), hi))
                 + LOAD_TEXTURE2D(_BlitTexture, min(o + int2(0, 1), hi)) + LOAD_TEXTURE2D(_BlitTexture, min(o + int2(1, 1), hi));
        return c * 0.25;
    }

    // Pass 7: pick sharp, quarter-res blur or eighth-res blur per pixel from its distance to the figure's plane.
    half4 FragDepthBlur(Varyings input) : SV_Target
    {
        int2 p = int2(input.positionCS.xy);
        float2 uv = (float2(p) + 0.5) / _ScaledScreenParams.xy;
        float3 sharp = LOAD_TEXTURE2D_X(_BlitTexture, p).rgb;
        float rawDepth = LoadSceneDepth(p);
        ViewRay ray = GetViewRay(uv, rawDepth);
        float signedDistance = ray.sky ? 1e4 : dot(_NitrateFogPlane.xyz, ray.positionWS) + _NitrateFogPlane.w;

        float w1, w2;
        if (ray.sky)
        {
            w1 = 1.0;
            w2 = _BlurSky;
        }
        else if (signedDistance < 0.0)
        {
            // In front of the figure: sharp. A gather blur can only pull in what lies behind a pixel, so a thin black
            // branch would turn into a grey ghost of the sky behind it, even where it crosses the black figure.
            w1 = 0.0;
            w2 = 0.0;
        }
        else
        {
            w1 = saturate(signedDistance / max(_BlurFar1, 0.01));
            w2 = saturate((signedDistance - _BlurFar1) / max(_BlurFar2 - _BlurFar1, 0.01));
        }

        float2 uv1 = (float2(p) + 0.5) * 0.25 * _NitrateBlur1Size.zw;
        float2 uv2 = (float2(p) + 0.5) * 0.125 * _NitrateBlur2Size.zw;
        float3 blur1 = ExpandHdr(SAMPLE_TEXTURE2D_LOD(_NitrateBlur1, sampler_LinearClamp, uv1, 0).rgb);
        float3 blur2 = ExpandHdr(SAMPLE_TEXTURE2D_LOD(_NitrateBlur2, sampler_LinearClamp, uv2, 0).rgb);
        float3 color = lerp(sharp, blur1, w1);
        color = lerp(color, blur2, w2);
        color += LanternFog(ray, ray.sky ? 1e4 : max(signedDistance, 0.0));
        return half4(color, 1.0);
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
            Name "Nitrate Beams"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBeams
            ENDHLSL
        }

        Pass
        {
            Name "Nitrate Beams Radial"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBeamsRadial
            ENDHLSL
        }

        Pass
        {
            Name "Nitrate Beams Blur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur
            ENDHLSL
        }

        Pass
        {
            Name "Nitrate Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }

        Pass
        {
            Name "Nitrate Downsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDownsample
            ENDHLSL
        }

        Pass
        {
            Name "Nitrate Blur Color"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurColor
            ENDHLSL
        }

        Pass
        {
            Name "Nitrate Half"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHalf
            ENDHLSL
        }

        Pass
        {
            Name "Nitrate Depth Blur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepthBlur
            ENDHLSL
        }
    }
}
