Shader "Four Tiger Sword/Player Ground Shadow"
{
    Properties
    {
        _Opacity("Opacity", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent-50"
        }

        Pass
        {
            Name "GroundShadow"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha, Zero One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // The radius falls inside the 16-sided mesh so its edges blend all the way out.
                half falloff = 1.0h - smoothstep(0.0h, 0.98h, length(input.uv * 2.0h - 1.0h));
                return half4(0.0h, 0.0h, 0.0h, _Opacity * falloff);
            }
            ENDHLSL
        }
    }
}
