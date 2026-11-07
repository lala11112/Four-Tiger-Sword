Shader "BoundaryZERO/UI/Popup Backdrop Blur"
{
 Properties
 {
  [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
  _BlurPixels ("Sample spacing in screen pixels", Float) = 1
  _BlurDirection ("Blur direction", Vector) = (1,0,0,0)
  _StencilComp ("Stencil Comparison", Float) = 8
  _Stencil ("Stencil ID", Float) = 0
  _StencilOp ("Stencil Operation", Float) = 0
  _StencilWriteMask ("Stencil Write Mask", Float) = 255
  _StencilReadMask ("Stencil Read Mask", Float) = 255
  _ColorMask ("Color Mask", Float) = 15
 }
 SubShader
 {
  Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "DisableBatching"="True" }
  Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
  Cull Off
  Lighting Off
  ZWrite Off
  ZTest [unity_GUIZTestMode]
  Blend SrcAlpha OneMinusSrcAlpha
  ColorMask [_ColorMask]

  // Each popup must capture the lower layers at its own position in the draw order.
  GrabPass { }
  Pass
  {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.0
   #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
   #include "UnityCG.cginc"
   #include "UnityUI.cginc"
   struct appdata { float4 vertex:POSITION; fixed4 color:COLOR; };
   struct v2f { float4 vertex:SV_POSITION; float4 grab:TEXCOORD0; float4 local:TEXCOORD1; fixed4 color:COLOR; };
   sampler2D _GrabTexture;
   float4 _GrabTexture_TexelSize;
   float4 _ClipRect;
   float _BlurPixels;
   float4 _BlurDirection;
   v2f vert(appdata v) {
    v2f o;
    o.vertex=UnityObjectToClipPos(v.vertex);
    o.grab=ComputeGrabScreenPos(o.vertex);
    o.local=v.vertex;
    o.color=v.color;
    return o;
   }
   fixed4 frag(v2f i):SV_Target {
    float2 uv=i.grab.xy/i.grab.w;
    float2 texel=abs(_GrabTexture_TexelSize.xy);
    float2 spacing=texel*_BlurPixels*_BlurDirection.xy;
    half3 color=0;
    float totalWeight=0;
    // Dense, separable Gaussian samples avoid repeated silhouettes on thin HUD lines.
    [unroll] for(int tap=-10;tap<=10;tap++) {
     float weight=exp(-.5*tap*tap/(4.5*4.5));
     float2 sampleUV=clamp(uv+tap*spacing,texel*.5,1-texel*.5);
     color+=tex2D(_GrabTexture,sampleUV).rgb*weight;
     totalWeight+=weight;
    }
    color/=totalWeight;
    half alpha=i.color.a;
    #ifdef UNITY_UI_CLIP_RECT
     alpha*=UnityGet2DClipping(i.local.xy,_ClipRect);
    #endif
    return fixed4(color,alpha);
   }
   ENDCG
  }
 }
 Fallback Off
}
