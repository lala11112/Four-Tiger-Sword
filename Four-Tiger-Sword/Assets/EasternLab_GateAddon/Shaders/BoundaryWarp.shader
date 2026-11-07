Shader "EasternLab/GateAddon/BoundaryWarp"
{
    Properties
    {
        [HDR] _Color ("Data color", Color) = (0.07, 0.72, 1, 1)
        [HDR] _RimColor ("Rim color", Color) = (0.23, 0.9, 1, 1)
        _Intensity ("Intensity", Range(0, 5)) = 1.2
        _Speed ("Inward flow speed", Range(0, 3)) = 0.55
        _Depth ("Tunnel depth", Range(2, 20)) = 9
        _Opacity ("Interior opacity", Range(0, 1)) = 1
        _Radius ("Local aperture radius", Float) = 1.745
        _GroundClipY ("Local ground clip", Float) = -1.52
        _OpenAmount ("Digital conversion", Range(0, 1)) = 0
        _PreviewTime ("Fixed preview time (-1 = live)", Float) = -1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "BoundaryWarp"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _RimColor;
                float _Intensity, _Speed, _Depth, _Opacity, _Radius, _GroundClipY, _PreviewTime, _OpenAmount;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionOS=input.positionOS.xyz;
                o.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float GridLine(float v, float width)
            {
                float d=abs(frac(v+0.5)-0.5);
                return 1-smoothstep(width,width+max(fwidth(v),0.001),d);
            }
            half4 Frag(Varyings i) : SV_Target
            {
                clip(i.positionOS.y-_GroundClipY);
                float2 p=i.positionOS.xy/max(_Radius,0.001);
                float r=length(p);
                clip(1-r);
                float t=(_PreviewTime>=0 ? _PreviewTime : _Time.y)*_Speed;
                float opened=saturate(_OpenAmount);
                // Quiet closed seal: neutral material, a segmented seam, two amber status pixels.
                float2 tiles=p*21;
                float2 tileID=floor(tiles);
                float tileNoise=Hash(tileID);
                float2 tileLocal=frac(tiles);
                float seams=max(GridLine(tiles.x,0.008),GridLine(tiles.y,0.008));
                float3 seal=float3(0.027,0.030,0.032)*(0.88+tileNoise*0.18);
                seal-=seams*0.003;
                float quietSeam=(1-smoothstep(0.002,0.005,abs(p.x)))*step(0.32,frac((p.y+1)*10));
                seal+=float3(0.025,0.034,0.038)*quietSeam;
                float indicator=step(abs(p.x-0.07),0.008)*step(abs(p.y+0.16),0.008);
                indicator+=step(abs(p.x+0.07),0.008)*step(abs(p.y+0.16),0.008);
                seal+=float3(0.28,0.13,0.025)*indicator;
                // Equal-radius squares convert together, from the center outward.
                float2 tileCenter=(tileID+0.5)/21;
                // The nearest four centers have radius sqrt(2)/42.
                float firstTileRadius=0.0336717515;
                float tileRadius=length(tileCenter);
                float2 radialDirection=tileCenter/max(tileRadius,0.0001);
                
                float radialY=radialDirection.y*max(_Radius,0.001);
                // Match radial progress to the visible outline, including the ground-cut bottom.
                float boundaryRadius=1;
                if(radialY < -0.0001 && _GroundClipY < 0)
                    boundaryRadius=min(boundaryRadius,_GroundClipY/radialY);
                float conversionOrder=saturate((tileRadius-firstTileRadius)/max(boundaryRadius-firstTileRadius,0.0001));
                float scan=opened*1.036-0.018;
                float reveal=smoothstep(conversionOrder-0.018,conversionOrder+0.018,scan);
                reveal*=smoothstep(0,0.025,opened);
                reveal=lerp(reveal,1,smoothstep(0.97,1,opened));
                float front=(1-smoothstep(0.018,0.095,abs(conversionOrder-scan)))*smoothstep(0.015,0.09,opened)*(1-smoothstep(0.90,1,opened));
                float tileBorder=1-smoothstep(0.035,0.09,min(min(tileLocal.x,1-tileLocal.x),min(tileLocal.y,1-tileLocal.y)));
                float3 color=seal;
                color+=_RimColor.rgb*front*(0.045+tileBorder*0.43)*step(0.22,tileNoise)*_Intensity;
                float tears=step(0.965,Hash(float2(floor(p.y*58),floor(t*4))))*step(0.45,Hash(tileID+17));
                color+=_Color.rgb*tears*front*0.25*_Intensity;
                float rim=exp(-pow((r-0.984)/0.005,2));
                float segmented=step(0.18,frac(atan2(p.y,p.x)*11));
                color+=_RimColor.rgb*rim*segmented*lerp(0.025,0.42,opened)*_Intensity;
                float edge=1-smoothstep(0.991,1,r);
                // Converted tiles reveal the real rendered destination, never a painted tunnel.
                float alpha=edge*_Opacity*(1-reveal);
                if(opened>=0.9999)alpha=0;
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

