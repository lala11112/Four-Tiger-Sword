Shader "EasternLab/GateAddon/SpatialCyberAtmosphere" {
 Properties {
  _EdgeColor("Distant blue",Color)=(0.055,0.19,0.29,1)
  _CenterColor("Pale blue depth",Color)=(0.48,0.78,0.94,1)
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Opaque"}
  Pass {
   Tags {"LightMode"="SRPDefaultUnlit"}
   Cull Off ZWrite Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _EdgeColor,_CenterColor;
   CBUFFER_END
   struct A {float4 positionOS:POSITION;};
   struct V {float4 positionCS:SV_POSITION;float2 p:TEXCOORD0;};
   V vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.p=a.positionOS.xy;return o;}
   half4 frag(V i):SV_Target {
    float2 p=(i.p-float2(0,1.7))/float2(22,19);
    float atmosphere=exp(-dot(p,p)*.75);
    return half4(lerp(_EdgeColor.rgb,_CenterColor.rgb,atmosphere),1);
   }
   ENDHLSL
  }
 }
}
