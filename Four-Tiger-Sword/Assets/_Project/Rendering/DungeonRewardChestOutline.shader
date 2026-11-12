Shader "Four Tiger Sword/Dungeon Reward Chest Outline"
{
    Properties
    {
        _Color("Outline Color", Color) = (1, 0.8117647, 0.3921569, 1)
        _OutlinePixels("Width at 1080p", Float) = 3
        [HideInInspector] _CenterOS("Mesh Center", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ColorMask("Color Write Mask", Float) = 15
        [HideInInspector] _StencilComp("Stencil Comparison", Float) = 6
        [HideInInspector] _StencilPass("Stencil Operation", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+991"
            "DisableBatching" = "True"
        }

        Pass
        {
            Name "RewardChestOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask [_ColorMask]

            // Queues 3990–3992 stamp the original silhouette, draw the gold rim, then clear this bit.
            Stencil
            {
                Ref 8
                ReadMask 8
                WriteMask 8
                Comp [_StencilComp]
                Pass [_StencilPass]
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _CenterOS;
                float _OutlinePixels;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float4 positionCS = TransformObjectToHClip(input.positionOS);
                if (_OutlinePixels > 0.0 && positionCS.w > 0.0001)
                {
                    float4 centerCS = TransformObjectToHClip(_CenterOS.xyz);
                    if (centerCS.w > 0.0001)
                    {
                        // Expand in screen space so split vertices at hard edges move together.
                        float2 screenSize = GetScaledScreenParams().xy;
                        float2 delta = (positionCS.xy / positionCS.w - centerCS.xy / centerCS.w) * screenSize;
                        float2 direction = delta * rsqrt(max(dot(delta, delta), 0.0001));
                        float width = _OutlinePixels * (screenSize.y / 1080.0);
                        positionCS.xy += direction * (2.0 * width * positionCS.w / screenSize);
                    }
                }
                output.positionCS = positionCS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
