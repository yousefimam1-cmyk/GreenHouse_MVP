// Mazraa water: canals, ditches and the river on the High / Ultra presets (WaterLook swaps the shared water material to it).
// - colour by depth: the scene depth behind the surface (_CameraDepthTexture, PC_RPAsset "Depth Texture" is on) gives how
//   much water the view ray crosses; shallow = clear greenish, deep = dark teal; _DepthBias adds depth for the flat farm ditches;
// - normal-mapped waves without a texture: five analytic sine waves (wind direction from _MazraaWind) plus fine ripples;
// - reflections from the reflection probe / sky (URP GlossyEnvironmentReflection through UniversalFragmentPBR), sun glints,
//   Fresnel (grazing views reflect more and hide the bed), additional lights (lamps, headlights), main light shadows, fog;
// - foam where the bed rises quickly to the surface (banks, things standing in the water), broken up by noise.
// Transparent, no depth write, no shadow casting. No textures, so nothing to import.
Shader "Mazraa/Water"
{
    Properties
    {
        _ShallowColor("Shallow colour", Color) = (0.30, 0.40, 0.33, 1)
        _DeepColor("Deep colour", Color) = (0.06, 0.14, 0.13, 1)
        _Absorption("Absorption per metre", Float) = 2.0
        _DepthBias("Extra depth (flat ditches, m)", Float) = 0.35
        _ShallowAlpha("Opacity at the edge", Range(0, 1)) = 0.55
        _WaterSmoothness("Smoothness", Range(0, 1)) = 0.94
        _WaveScale("Wave length (m)", Float) = 1.8
        _WaveSpeed("Wave speed", Float) = 0.6
        _WaveStrength("Wave normal strength", Range(0, 2)) = 0.6
        _FoamColor("Foam colour", Color) = (0.82, 0.8, 0.72, 1)
        _FoamDistance("Foam depth (m)", Float) = 0.3
        _FoamEdge("Foam edge sharpness", Float) = 25
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVert
            #pragma fragment WaterFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _Absorption;
                float _DepthBias;
                half _ShallowAlpha;
                half _WaterSmoothness;
                float _WaveScale;
                float _WaveSpeed;
                half _WaveStrength;
                half4 _FoamColor;
                float _FoamDistance;
                float _FoamEdge;
            CBUFFER_END

            // Global (AtmosphereLook): xz = wind direction, w = strength 0..1.5
            float4 _MazraaWind;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings WaterVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Slope (dh/dx, dh/dz) of one sine wave travelling along dir with wave length len and steepness k.
            float2 WaveSlope(float2 p, float2 dir, float len, float k, float t)
            {
                float w = 6.2831853 / len;
                float c = sqrt(9.81 * w);   // deep-water dispersion: long waves travel faster
                return dir * (k * cos(dot(dir, p) * w + t * c));
            }

            half4 WaterFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 positionWS = input.positionWS;
                float t = _Time.y * _WaveSpeed;

                // ---- waves (world space, along the wind with some spread)
                float2 wind = _MazraaWind.xz;
                wind = dot(wind, wind) > 1e-4 ? normalize(wind) : float2(0.6, 0.8);
                float2 side = float2(-wind.y, wind.x);
                float rough = saturate(0.35 + _MazraaWind.w * 0.5);
                float2 p = positionWS.xz;
                float L = max(0.05, _WaveScale);
                float2 slope = 0;
                slope += WaveSlope(p, wind, L, 0.10, t);
                slope += WaveSlope(p, normalize(wind + side * 0.6), L * 0.61, 0.08, t);
                slope += WaveSlope(p, normalize(wind - side * 0.7), L * 0.43, 0.07, t);
                slope += WaveSlope(p, normalize(wind * 0.3 + side), L * 0.29, 0.05, t);
                slope += WaveSlope(p, normalize(-wind * 0.2 - side), L * 0.17, 0.04, t);
                // fine ripples (noise gradient), drifting with the wind
                float2 q = p * (3.1 / L) + wind * t * 0.6;
                float e = 0.15;
                float n0 = ValueNoise(q);
                slope += float2(ValueNoise(q + float2(e, 0)) - n0, ValueNoise(q + float2(0, e)) - n0) * (0.35 / e) * 0.12;
                slope *= _WaveStrength * rough;

                half3 baseNormal = normalize(input.normalWS);
                half3 waveNormal = normalize(half3(-slope.x, 1.0, -slope.y));
                half3 normalWS = normalize(lerp(baseNormal, waveNormal, saturate(baseNormal.y)));   // only the top faces get waves

                // ---- depth of water along the view ray
                float2 suv = input.screenPos.xy / input.screenPos.w;
                float surfaceEye = input.screenPos.w;
                float depth = 1.0;
                if (unity_OrthoParams.w < 0.5)
                {
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    depth = max(0.0, sceneEye - surfaceEye);
                }
                float absorb = 1.0 - exp(-(depth + _DepthBias) * _Absorption);
                half3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, absorb);

                // ---- foam: shallow and where the bed changes quickly (banks), broken by noise, faded with distance
                float edge = saturate(1.0 - depth / max(0.01, _FoamDistance)) * saturate(fwidth(depth) * _FoamEdge);
                float fn = ValueNoise(p * 2.7 + wind * t * 0.25) * 0.6 + ValueNoise(p * 7.3 - t * 0.4) * 0.4;
                half foam = saturate(edge * 1.8 - (1.0 - fn) * 0.9) * saturate(1.0 - surfaceEye / 120.0);

                // ---- lighting: URP PBR (sun + shadows, additional lights, probe / sky reflections, fog)
                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = lerp(water, _FoamColor.rgb, foam);
                surface.metallic = 0;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = lerp(_WaterSmoothness, 0.25, foam);
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = 1;
                half4 color = UniversalFragmentPBR(inputData, surface);

                // Fresnel: grazing views are mirror-like and hide the bed
                half fresnel = pow(1.0 - saturate(dot(normalWS, inputData.viewDirectionWS)), 5.0);
                half alpha = saturate(lerp(_ShallowAlpha, 1.0, absorb) + fresnel * 0.6 + foam);

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Simple Lit"
}
