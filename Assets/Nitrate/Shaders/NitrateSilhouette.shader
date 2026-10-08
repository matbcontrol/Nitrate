Shader "Nitrate/Silhouette"
{
    // Everything in the world is this: pure black, moved by the wind. The wind runs in every pass (colour, shadow,
    // depth), so the beams cut by a swaying branch sway with it, and depth priming still matches pixel for pixel.
    // Vertex colour carries the wind response, written by the procedural generators:
    //   R = stiffness (1 = rigid, 0 = free tip). Meshes without vertex colours read white, so they stay rigid.
    //   G = per-blade phase, so neighbours do not flutter in unison.
    Properties
    {
        _BaseColor ("Color", Color) = (0, 0, 0, 1)
        _WindResponse ("Wind Response", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "NitrateCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _WindResponse;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 color : COLOR;
        };

        float3 WindyPositionWS(Attributes input)
        {
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float flexibility = saturate(1.0 - input.color.r) * _WindResponse;
            return positionWS + NitrateWindOffset(positionWS, flexibility, input.color.g);
        }
        ENDHLSL

        Pass
        {
            Name "Silhouette"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            float4 Vertex(Attributes input) : SV_POSITION
            {
                return TransformWorldToHClip(WindyPositionWS(input));
            }

            half4 Fragment() : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Set by URP while it renders shadow maps (same as its own ShadowCasterPass.hlsl).
            float3 _LightDirection;
            float3 _LightPosition;

            float4 Vertex(Attributes input) : SV_POSITION
            {
                float3 positionWS = WindyPositionWS(input);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                return ApplyShadowClamping(positionCS);
            }

            half4 Fragment() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            float4 Vertex(Attributes input) : SV_POSITION
            {
                return TransformWorldToHClip(WindyPositionWS(input));
            }

            half Fragment() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(WindyPositionWS(input));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
