Shader "EasternLab/GateAddon/CyberTransition" {
 Properties {
  [PerRendererData] _MainTex("Sprite",2D)="white"{}
  _Progress("Conversion Progress",Range(0,1))=0
 }
 SubShader {
  Tags {"Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True"}
  Cull Off ZWrite Off ZTest Always
  Blend SrcAlpha OneMinusSrcAlpha
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   struct appdata {float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
   struct v2f {float4 vertex:SV_POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
   float _Progress;
   v2f vert(appdata v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   fixed4 frag(v2f i):SV_Target {
    float p=saturate(_Progress);
    float aspect=_ScreenParams.x/max(_ScreenParams.y,1);
    float2 grid=i.uv*float2(38*aspect,38);
    float2 cell=floor(grid),f=frac(grid);
    float seed=hash(cell);
    // Staggered data cells disappear across the image and expose the new space.
    float release=.18+seed*.54+i.uv.x*.12;
    float tile=1-smoothstep(release,release+.18,p);
    float edge=1-smoothstep(.025,.065,min(min(f.x,1-f.x),min(f.y,1-f.y)));
    float scan=pow(saturate(1-abs(i.uv.y-frac(p*1.7+seed*.015))*65),3);
    float fragments=step(.84,seed)*step(.72,f.x)*step(.35,f.y);
    float detail=edge*.22+scan*.20+fragments*.18;
    float3 charcoal=float3(.012,.025,.032);
    float3 cyan=float3(.05,.58,.66);
    float3 col=lerp(charcoal,cyan,saturate(detail));
    float alpha=tile*lerp(.76,.94,saturate(p*9))*(1-smoothstep(.86,1,p));
    return fixed4(col,alpha)*i.color;
   }
   ENDCG
  }
 }
}
