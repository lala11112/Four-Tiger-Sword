Shader "EasternLab/GateAddon/SpatialPortal" {
 Properties {
  _PortalTex("Live portal camera",2D)="black"{}
  _Radius("Aperture radius",Float)=1.745
  _GroundClipY("Local ground cutoff",Float)=0.03
  _EdgeSoftness("Aperture edge",Range(0.0001,0.03))=0.003
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Pass {
   Name "SpatialPortal"
   Tags {"LightMode"="SRPDefaultUnlit"}
   Cull Off ZWrite On ZTest LEqual
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_PortalTex); SAMPLER(sampler_PortalTex);
   CBUFFER_START(UnityPerMaterial)
   float _Radius, _GroundClipY, _EdgeSoftness;
   float4x4 _PortalProjector;
   CBUFFER_END
   struct Attributes {float4 positionOS:POSITION;};
   struct Varyings {float4 positionCS:SV_POSITION;float3 positionOS:TEXCOORD0;float4 portalPosition:TEXCOORD1;};
   Varyings vert(Attributes v){Varyings o;float3 world=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(world);o.positionOS=v.positionOS.xyz;o.portalPosition=mul(_PortalProjector,float4(world,1));return o;}
   half4 frag(Varyings i):SV_Target {
    float edge=min(_Radius-length(i.positionOS.xy),i.positionOS.y-_GroundClipY);
    clip(edge);
    float2 uv=i.portalPosition.xy/i.portalPosition.w*.5+.5;
    #if UNITY_UV_STARTS_AT_TOP
    uv.y=1-uv.y;
    #endif
    half3 color=SAMPLE_TEXTURE2D(_PortalTex,sampler_PortalTex,uv).rgb;
    float coverage=smoothstep(0,max(fwidth(edge),_EdgeSoftness),edge);
    return half4(color*coverage,1);
   }
   ENDHLSL
  }
 }
}
