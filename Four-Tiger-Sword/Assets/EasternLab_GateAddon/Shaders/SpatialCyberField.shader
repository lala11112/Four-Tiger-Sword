Shader "EasternLab/GateAddon/SpatialCyberField"
{
 Properties
 {
  _NearTint("Near tint",Color)=(.92,1.02,1.03,1)
  _FarTint("Far tint",Color)=(.52,.72,.80,1)
  _NearOpacity("Near opacity",Range(0,1))=.90
  _FarOpacity("Far opacity",Range(0,1))=.22
  _PreviewTime("Preview time (-1 live)",Float)=-1
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
  Pass
  {
   Name "SpatialVerticalFlow"
   Tags { "LightMode"="SRPDefaultUnlit" }
   Blend SrcAlpha OneMinusSrcAlpha
   Cull Off ZWrite Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _NearTint;
   half4 _FarTint;
   float _NearOpacity;
   float _FarOpacity;
   float _PreviewTime;
   CBUFFER_END
   struct Attributes {float3 positionOS:POSITION;float2 uv:TEXCOORD0;float4 flow:TEXCOORD1;float4 shape:TEXCOORD2;float4 color:COLOR;};
   struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 flow:TEXCOORD1;float4 shape:TEXCOORD2;float4 color:TEXCOORD3;};
   Varyings Vert(Attributes input)
   {
    Varyings o;float3 center=TransformObjectToWorld(input.positionOS);
    float3 right=normalize(float3(UNITY_MATRIX_I_V._m00,0,UNITY_MATRIX_I_V._m20));
    float3 p=center+right*((input.uv.x-.5)*input.shape.x)+float3(0,(input.uv.y-.5)*16,0);
    o.positionCS=TransformWorldToHClip(p);o.uv=input.uv;o.flow=input.flow;o.shape=input.shape;
    float depthWeight=smoothstep(3,24,input.positionOS.z);
    o.color=float4(lerp(_NearTint.rgb,_FarTint.rgb,depthWeight),lerp(_NearOpacity,_FarOpacity,depthWeight));
    return o;
   }
   half4 Frag(Varyings i):SV_Target
   {
    float time=_PreviewTime>=0?_PreviewTime:_Time.y;
    // Only the continuous coordinate moves; floor identifies spatial packet addresses.
    float row=(i.uv.y*16-time*i.flow.x+i.flow.y)/.18;
    float address=row-floor(row/38)*38;
    float cell=frac(row);
    float aa=max(fwidth(row),.006);
    float dash=1-smoothstep(i.shape.y/.18-aa,i.shape.y/.18+aa,cell);
    float packet=1-smoothstep(i.flow.z-.08,i.flow.z+.08,address);
    float head=smoothstep(i.flow.z-2,i.flow.z-.3,address);
    float lateral=abs(i.uv.x-.5)*2;
    float edge=1-smoothstep(.76,1,lateral);
    float ends=smoothstep(0,.03,i.uv.y)*(1-smoothstep(.97,1,i.uv.y));
    float a=dash*packet*edge*ends;
    return half4(i.color.rgb*(1+.04*head),a*i.color.a);
   }
   ENDHLSL
  }
 }
}
