#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BoundaryZeroUI.Editor {
public static class ExportUIParts {
 [Serializable] public class Entry {public string path,title,group,note;public int width,height,sourceWidth,sourceHeight,offsetX,offsetY;public float pivotX=.5f,pivotY=.5f;public int[] border;public int transparentPixels,partialAlphaPixels,visiblePixels;}
 [Serializable] public class Manifest {public int referenceWidth=2560,referenceHeight=1440;public string alpha="straight RGBA",coordinateOrigin="top-left",edgeQuality="4x spatial supersampling, premultiplied area resolve; large panels use 2x";public List<Entry> assets=new List<Entry>();}
 static Manifest manifest;static string output;static Camera camera;static GameObject canvasGO;static RectTransform root;static RenderTexture target;
 static readonly Color Silver=UIFactory.Ivory,Red=UIFactory.Red;
 static int nativeWidth,nativeHeight,renderScale;
 static string Group(string path){return path.Split('/')[0];}
 static void Prepare(int w,int h,bool bright=true){
  if(canvasGO)UnityEngine.Object.DestroyImmediate(canvasGO);if(target){target.Release();UnityEngine.Object.DestroyImmediate(target);}
  nativeWidth=w;nativeHeight=h;renderScale=w*h>1600000?2:4;
  target=new RenderTexture(w*renderScale,h*renderScale,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Default);target.antiAliasing=1;target.Create();camera.targetTexture=target;camera.orthographic=true;camera.orthographicSize=h/2f;camera.aspect=(float)w/h;
  canvasGO=new GameObject("ExportCanvas",typeof(RectTransform),typeof(Canvas));var canvas=canvasGO.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=camera;canvas.pixelPerfect=true;
  var rect=canvasGO.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(w,h);rect.position=Vector3.zero;
  root=UIFactory.Node(rect,"ExportRoot",0,0,w,h);if(bright)root.gameObject.AddComponent<BrightUiScope>();
 }
 static Color32[] Matte(Color color){camera.backgroundColor=color;Canvas.ForceUpdateCanvases();camera.Render();var prev=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();RenderTexture.active=prev;var pixels=texture.GetPixels32();UnityEngine.Object.DestroyImmediate(texture);return pixels;}
 static Texture2D Render(){
  foreach(var graphic in root.GetComponentsInChildren<Graphic>()){if(!graphic.GetComponent<CanvasRenderer>())graphic.gameObject.AddComponent<CanvasRenderer>();graphic.SetAllDirty();graphic.canvasRenderer.cull=false;graphic.Rebuild(CanvasUpdate.PreRender);}Canvas.ForceUpdateCanvases();var black=Matte(Color.black);var white=Matte(Color.white);var pixels=new Color32[nativeWidth*nativeHeight];
  // Resolve coverage and premultiplied color together, then unpremultiply once.
  // Averaging straight RGB would put black fringes around transparent artwork.
  int samples=renderScale*renderScale;
  for(int y=0;y<nativeHeight;y++)for(int x=0;x<nativeWidth;x++){
   int r=0,g=0,b=0,difference=0;
   for(int sy=0;sy<renderScale;sy++)for(int sx=0;sx<renderScale;sx++){int k=(y*renderScale+sy)*target.width+x*renderScale+sx;r+=black[k].r;g+=black[k].g;b+=black[k].b;difference+=(white[k].r-black[k].r)+(white[k].g-black[k].g)+(white[k].b-black[k].b);}
   float a=Mathf.Clamp01(1-difference/(765f*samples));int i=y*nativeWidth+x;
   if(a<.5f/255){pixels[i]=new Color32(0,0,0,0);continue;}
   float inv=1/(samples*a);pixels[i]=new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(r*inv),0,255),(byte)Mathf.Clamp(Mathf.RoundToInt(g*inv),0,255),(byte)Mathf.Clamp(Mathf.RoundToInt(b*inv),0,255),(byte)Mathf.RoundToInt(a*255));
  }
  var tex=new Texture2D(nativeWidth,nativeHeight,TextureFormat.RGBA32,false);tex.SetPixels32(pixels);tex.Apply();return tex;
 }
 static void Save(Texture2D texture,string path,string title,string note="",bool trim=false,int[] border=null){
  int sw=texture.width,sh=texture.height,x0=0,y0=0,x1=sw-1,y1=sh-1;var pix=texture.GetPixels32();
  if(trim){x0=sw;y0=sh;x1=-1;y1=-1;for(int y=0;y<sh;y++)for(int x=0;x<sw;x++)if(pix[y*sw+x].a>1){x0=Math.Min(x0,x);x1=Math.Max(x1,x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);}if(x1<0)throw new Exception("Empty part: "+path);x0=Math.Max(0,x0-3);y0=Math.Max(0,y0-3);x1=Math.Min(sw-1,x1+3);y1=Math.Min(sh-1,y1+3);}
  int w=x1-x0+1,h=y1-y0+1;var result=new Texture2D(w,h,TextureFormat.RGBA32,false);var colors=new Color32[w*h];for(int y=0;y<h;y++)Array.Copy(pix,(y+y0)*sw+x0,colors,y*w,w);result.SetPixels32(colors);result.Apply();
  var entry=new Entry{path="PNG/"+path+".png",title=title,group=Group(path),width=w,height=h,sourceWidth=sw,sourceHeight=sh,offsetX=x0,offsetY=sh-1-y1,note=note,border=border??new int[4]};
  foreach(var c in colors){if(c.a==0)entry.transparentPixels++;else entry.visiblePixels++;if(c.a>0&&c.a<255)entry.partialAlphaPixels++;}
  if(entry.visiblePixels==0)throw new Exception("Fully transparent part: "+path);
  string full=Path.Combine(output,entry.path);Directory.CreateDirectory(Path.GetDirectoryName(full));File.WriteAllBytes(full,result.EncodeToPNG());manifest.assets.Add(entry);UnityEngine.Object.DestroyImmediate(result);UnityEngine.Object.DestroyImmediate(texture);Debug.Log("UI_PART "+path+" "+w+"x"+h);
 }
 static void Job(string path,string title,int w,int h,Action<RectTransform> draw,string note="",bool trim=false,int[] border=null,bool bright=true){Prepare(w,h,bright);draw(root);Save(Render(),path,title,note,trim,border);}
 static void Svg(Transform p,string data,int w,int h){var r=UIFactory.Node(p,"Geometry",0,0,w,h);var g=r.gameObject.AddComponent<SvgFrameGraphic>();g.Source=new TextAsset("<svg>"+data+"</svg>");g.ViewBox=new Vector2(w,h);g.raycastTarget=false;}
 static void NativeHud(RectTransform p,int x,int y){var g=UIFactory.Node(p,"HUD",-x,-y,2560,1440).gameObject.AddComponent<SvgFrameGraphic>();g.Resource="BoundaryUI/Art/hud-frame";g.ViewBox=new Vector2(2560,1440);}
 static void HudPart(RectTransform p,int x,int y,bool crest){
  string svg=crest?"<g fill='#0D1521' fill-opacity='.88'><path d='M87 126L119 112L119 228L97 244Z'/></g><g fill='#E8EEF5'><path d='M65 126L106 73L119 78L95 111L145 60L131 89L100 123L119 116L88 147L87 123Z'/><path d='M76 124L57 147L72 141L95 115Z'/><path d='M104 80L127 51L127 67L119 81Z'/><path d='M116 119L141 98L127 128L116 135Z'/></g><g fill='#E72B46'><path d='M104 133L123 121V220L104 239Z'/><path d='M110 137L114 135V236L110 241Z'/></g><g fill='none' stroke='#E8EEF5' stroke-width='1'><path d='M103 96L110 115L126 120L109 124L102 144L100 125L83 120L99 115Z'/><path d='M94 151V207M83 151V176M126 142V181' stroke-opacity='.45'/></g>":"<g fill='#0D1521' fill-opacity='.88'><path d='M132 73H657L620 121H141L119 142H81L115 105Z'/><path d='M159 82H652L626 117H143Z'/></g><g fill='none' stroke='#E8EEF5' stroke-width='1.5'><path d='M139 73H657L620 121H150M177 86H642L619 115H151M174 126H598M180 132H560'/><path d='M151 67H305M321 67H350M605 133H624L665 82' stroke-opacity='.6'/></g><g fill='#E8EEF5'><path d='M146 129H162L149 144H133Z M168 129H184L171 144H155Z M190 129H206L193 144H177Z' fill-opacity='.65'/></g><g fill='#E72B46'><path d='M650 85L664 70H671L657 85Z'/><path d='M170 62H177L171 68H164Z M181 62H188L182 68H175Z M192 62H199L193 68H186Z'/></g>";
  var q=UIFactory.Node(p,"HUDPart",-x,-y,2560,1440);Svg(q,svg,2560,1440);
 }
 static RectTransform Card(RectTransform p,int w,int h,bool selected){var method=typeof(CompleteViews).GetMethod("Card",BindingFlags.NonPublic|BindingFlags.Static);return (RectTransform)method.Invoke(null,new object[]{p,"Card",0f,0f,(float)w,(float)h,selected});}
 static void Button(RectTransform p,int w,int h,bool primary,string state){var b=UIFactory.Button(p,"",0,0,w,h,null,primary,state!="disabled");if(state=="hover"||state=="pressed")b.GetComponent<UIFocusLight>().Light.alpha=state=="hover"?1:.3f;}
 static void CopyImage(string source,string path,string title){var t=new Texture2D(2,2,TextureFormat.RGBA32,false);t.LoadImage(File.ReadAllBytes(source));Save(t,path,title);}
 static void ResourceImages(){
  string art="Assets/BoundaryUI/Resources/BoundaryUI/Art";
  string[] form={"fire","earth","metal","water","wood","neutral"},ko={"화","토","금","수","목","공통"};
  for(int i=0;i<6;i++)CopyImage(art+"/Glyph"+i+".png","Icons/element_"+form[i],"속성 아이콘 · "+ko[i]);
  for(int i=0;i<3;i++)CopyImage(art+"/Ability"+i+".png","Icons/ability_"+(i+1),"스킬 아이콘 "+(i+1));
  CopyImage(art+"/Seal.png","Icons/red_seal","붉은 인장");
  string[] itemNames={"적층 인장","퇴적 코어","백철 잔편","환류 고리","결속 부적","경계 데이터편","주홍 기록지","압축 용기","백철 환","청류 관","생장 접점","삼각 기록체"};
  for(int page=0;page<2;page++){
   var atlas=new Texture2D(2,2,TextureFormat.RGBA32,false);atlas.LoadImage(File.ReadAllBytes(art+(page==0?"/items.png":"/items-extra.png")));int w=atlas.width/3,h=atlas.height/2;
   for(int i=0;i<6;i++){var tex=new Texture2D(w,h,TextureFormat.RGBA32,false);tex.SetPixels(atlas.GetPixels(i%3*w,i<3?h:0,w,h));tex.Apply();Save(tex,"Items/item_"+(page*6+i+1).ToString("00"),itemNames[page*6+i],"개별 유물 이미지. 이름·등급은 게임 텍스트로 표시합니다.");}UnityEngine.Object.DestroyImmediate(atlas);
  }
  foreach(InterfaceSymbol kind in Enum.GetValues(typeof(InterfaceSymbol))){var k=kind;Job("Icons/stat_"+k.ToString().ToLowerInvariant(),new Dictionary<InterfaceSymbol,string>{{InterfaceSymbol.Health,"체력"},{InterfaceSymbol.Attack,"공격력"},{InterfaceSymbol.Coins,"금화"},{InterfaceSymbol.LegacyCurrency,"영구 재화"},{InterfaceSymbol.Upgrade,"강화"}}[k]+" 아이콘",256,256,p=>InterfaceSymbols.Draw(p,k,0,0,256));}
 }
 static void Hud(){
  Job("HUD/health_housing","체력바 하우징",625,202,p=>NativeHud(p,52,46),"QHD 배치 좌상단 (52,46). 수치·체력 채움 없음.");
  Job("HUD/blade_crest","체력바 칼날 인장",105,202,p=>HudPart(p,52,46,true),"분리 조립용. QHD 배치 (52,46). 체력바 레일 위에 겹칩니다.");
  Job("HUD/health_rail","체력바 레일",598,92,p=>HudPart(p,77,57,false),"분리 조립용. QHD 배치 (77,57). 칼날 인장과 체력 채움을 별도로 추가합니다.");
  Job("HUD/health_fill","체력 채움",458,18,p=>Svg(p,"<path d='M15 0H458L443 18H0Z' fill='#E72B46'/>",458,18),"왼쪽 기준 마스크 또는 Filled로 체력에 연결합니다.");
  Job("HUD/minimap_frame","미니맵 외곽 틀",245,255,p=>{
   // The shared static frame is already at its final QHD display size.
   // Reverse that placement for the native, padded 245x255 sprite export.
   float scale=ReferenceHud.MapScale;
   var g=UIFactory.Node(p,"MinimapNativeCrop",-ReferenceHud.MapX/scale,-ReferenceHud.MapY/scale,2560/scale,1440/scale).gameObject.AddComponent<SvgFrameGraphic>();
   g.Resource="BoundaryUI/Art/hud-frame";g.ViewBox=new Vector2(2560,1440);g.raycastTarget=false;
  },"원본 245×255. QHD 배치 (2095,54), 표시 배율 1.65, 크기 404.25×420.75. 지도 콘텐츠 (2101.6,85.35), 크기 366.3×366.3. 내부 지도는 비워 둡니다.");
  string[] forms={"fire","earth","metal","water","wood"};var colors=new[]{new Color32(231,43,70,255),new Color32(207,166,81,255),new Color32(232,238,245,255),new Color32(72,190,225,255),new Color32(83,175,122,255)};
  for(int i=0;i<5;i++){string c=ColorUtility.ToHtmlStringRGB(colors[i]);int n=i;Job("HUD/element_mount_"+forms[i],"속성 슬롯 "+new[]{"화","토","금","수","목"}[i],72,76,p=>{var r=UIFactory.Node(p,"Inset",2,2,68,72);Svg(r,"<path d='M34 1L67 34L34 67L1 34Z' fill='#0D1521' fill-opacity='.88' stroke='#"+c+"' stroke-opacity='.72' stroke-width='1.4'/><path d='M34 7L61 34L34 61L7 34Z' fill='none' stroke='#E8EEF5' stroke-opacity='.18' stroke-width='1'/><path d='M34 0V9M34 59V68' stroke='#"+c+"' stroke-width='2'/>",68,72);},"원본 72×76. QHD 좌하단, 첫 PNG (64,1232), 126px 간격, 1.50배 표시. 순서 화·수·목·금·토.");}
  Job("HUD/element_selection","속성 선택 테두리",72,82,p=>{var r=UIFactory.Node(p,"Inset",2,2,68,78);Svg(r,"<path d='M0 24V9H15M53 9H68V24M0 54V69H15M53 69H68V54' fill='none' stroke='#E8EEF5' stroke-width='2'/><path d='M28 76H40' stroke='#E72B46' stroke-width='2'/>",68,78);},"별도 오버레이. 첫 슬롯 PNG (64,1232), 126px 간격, 1.50배 표시. 선택 PNG는 슬롯보다 7.5px 위에 겹칩니다. 키는 y1349, 크기 36×34.5, 글자 22.5px.");
  Job("HUD/skill_mount","스킬 슬롯 틀",98,120,p=>{var r=UIFactory.Node(p,"Inset",2,2,94,116);Svg(r,"<path d='M16 1H77L93 17V78L77 94H16L1 78V17Z' fill='#0D1521' fill-opacity='.76'/><circle cx='47' cy='47' r='39' fill='none' stroke='#E8EEF5' stroke-opacity='.45' stroke-width='1'/><path d='M0 26V15L15 0H36M58 94H79L94 79V68' fill='none' stroke='#E8EEF5' stroke-opacity='.8' stroke-width='1.5'/><path d='M37 105H57' stroke='#E72B46' stroke-width='2'/>",94,116);},"원본 98×120. QHD 첫 PNG E (2199.7,1198), R (2364.4,1198), 1.35배 표시 132.3×162, 간격 164.7. Q 슬롯 없음. 원본 Ability1/Ability2만 사용. 마지막 오른쪽 2496.7. 키는 첫 PNG 기준 (33.75,168.75), 크기 64.8×37.8, 글자 24.3px.");
  Job("HUD/crosshair","조준점",20,20,p=>{var r=UIFactory.Node(p,"Inset",2,2,16,16);Svg(r,"<path d='M8 0V4M8 12V16M0 8H4M12 8H16' stroke='#E8EEF5' stroke-opacity='.75'/>",16,16);});
  Job("HUD/interaction_plate","상호작용 바탕",192,54,p=>UIFactory.Box(p,"Plate",0,0,192,54,new Color32(13,21,33,235)),"키와 상점 이름은 별도 텍스트로 추가합니다.",false,new[]{12,12,12,12});
 }
 static void Frames(){
  for(int n=0;n<2;n++){bool compact=n==1;int w=compact?1100:1920,h=compact?720:1120;string name=compact?"compact":"wide";
   Job("Frames/window_"+name+"_border",compact?"소형 팝업 외곽":"대형 팝업 외곽",w,h,p=>p.gameObject.AddComponent<LegacyWindowGraphic>(),"외곽만 분리. 아래 바탕과 같은 좌표로 겹칩니다.",false,new[]{340,210,340,210});
   Job("Frames/window_"+name+"_glass",compact?"소형 팝업 반투명 바탕":"대형 팝업 반투명 바탕",w,h,p=>{DesignArt.Surface(p,34,40,w-58,h-58,new Color(.015f,.025f,.045f,.22f),new Color(.015f,.025f,.045f,.24f));DesignArt.Surface(p,29,29,w-58,h-58,new Color(18/255f,33/255f,51/255f,.91f),new Color(8/255f,17/255f,30/255f,.91f));},"반투명 알파 포함. 대형은 원본에서 0.88배 표시됩니다.",false,new[]{80,80,80,80});
  }
  Job("Frames/header_accent","제목 붉은 포인트",8,48,p=>UIFactory.Line(p,2,2,4,44,Red));
  Job("Frames/header_divider","창 제목 구분선",1774,4,p=>{UIFactory.Line(p,0,1,1774,1,new Color(.76f,.84f,.95f,.22f));UIFactory.Line(p,0,1,110,2,Silver);UIFactory.Line(p,115,1,42,2,Red);},"좌우 폭 조절 시 붉은 포인트가 늘어나지 않도록 9-slice 사용.",false,new[]{170,0,4,0});
  Job("Frames/detail_panel","아이템 상세 바탕",478,720,p=>DesignArt.Surface(p,0,0,478,720,new Color32(24,26,31,255),new Color32(12,14,18,255)),"상점·인벤토리 공용 바탕.",false,new[]{60,32,60,32});
  Job("Frames/synthesis_panel","합성 결과 바탕",470,678,p=>DesignArt.Surface(p,0,0,470,678,new Color32(24,26,31,255),new Color32(12,14,18,255)),"아이콘·속성 선택·글자는 별도.",false,new[]{60,32,60,32});
  Job("Frames/event_choice","이벤트 선택 바탕",585,400,p=>DesignArt.Surface(p,0,0,585,400,new Color(.095f,.10f,.12f,1),new Color(.035f,.04f,.055f,1)),"선택 문구·이익·대가는 별도.",false,new[]{60,32,60,32});
  Job("Frames/event_art_panel","이벤트 그림 바탕",505,650,p=>DesignArt.Surface(p,0,0,505,650,new Color(.095f,.10f,.12f,1),new Color(.035f,.04f,.055f,1)),"아이템 그림과 효과는 별도.",false,new[]{60,32,60,32});
  Job("Frames/stat_divider","수치 행 구분선",480,3,p=>UIFactory.Line(p,0,1,480,1,new Color(Silver.r,Silver.g,Silver.b,.24f)),"늘려 쓰는 단색 선입니다.");
 }
 static void Controls(){
  foreach(bool primary in new[]{false,true})foreach(string state in new[]{"normal","hover","pressed","disabled"}){bool pr=primary;string s=state;Job("Controls/button_"+(pr?"primary_":"secondary_")+s,(pr?"주 버튼 · ":"보조 버튼 · ")+new Dictionary<string,string>{{"normal","기본"},{"hover","올림"},{"pressed","누름"},{"disabled","비활성"}}[s],300,65,p=>Button(p,300,65,pr,s),"글자 없는 버튼. Image Type=Sliced 권장.",false,new[]{72,24,48,24});}
  foreach(var size in new[]{new[]{330,78},new[]{950,94},new[]{288,54},new[]{235,65},new[]{265,64}}){int w=size[0],h=size[1];Job("Controls/button_primary_"+w+"x"+h,"주 버튼 "+w+"×"+h,w,h,p=>Button(p,w,h,true,"normal"),"일시정지·결과·상점·설정의 원본 크기.",false,new[]{72,24,48,24});}
  foreach(string state in new[]{"normal","selected"}){bool active=state=="selected";Job("Controls/tab_"+state,"탭 · "+(active?"선택":"기본"),135,57,p=>{if(active){DesignArt.Surface(p,0,0,135,57,new Color(Silver.r,Silver.g,Silver.b,.10f),new Color(Silver.r,Silver.g,Silver.b,.025f));UIFactory.Line(p,8,55,119,2,Red);}else UIFactory.Line(p,8,55,119,1,new Color(Silver.r,Silver.g,Silver.b,.12f));},active?"선택 배경+밑줄. 탭 이름은 별도 텍스트.":"기본 탭의 얇은 구분선.",false,new[]{20,4,20,4});}
  foreach(int width in new[]{34,50,110,285,430}){int w=width,h=w<=50?34:44;Job("Controls/keycap_"+w,"키 표시 틀 "+w,w,h,p=>{if(w<=50)UIFactory.Border(p,1,1,w-2,h-2,new Color(.91f,.93f,.96f,.48f));else DesignArt.Surface(p,0,0,w,h,new Color32(24,26,31,255),new Color32(12,14,18,255));},"키 문자·조작 안내는 별도.",false,new[]{12,12,12,12});}
  Job("Controls/close_button","닫기 버튼 바탕",44,43,p=>Button(p,44,43,false,"normal"),"X 기호 별도.");
  Job("Controls/close_cross","닫기 X",24,24,p=>Svg(p,"<path d='M5 5L19 19M19 5L5 19' stroke='#E8EEF5' stroke-width='2'/>",24,24));
  Job("Controls/slider_track","설정 슬라이더 눈금",674,40,p=>{var q=UIFactory.Node(p,"Inset",2,2,670,36);for(int tick=0;tick<=10;tick++)UIFactory.Line(q,tick*670/10f,29,1,tick%5==0?7:4,new Color(186/255f,201/255f,201/255f,.55f));UIFactory.Box(q,"Track",0,16,670,4,new Color(186/255f,201/255f,201/255f,.4f));});
  Job("Controls/slider_fill","설정 슬라이더 채움",670,4,p=>UIFactory.Box(p,"Fill",0,0,670,4,Red));
  Job("Controls/slider_handle","설정 슬라이더 손잡이",14,36,p=>UIFactory.Box(p,"Handle",0,0,14,36,Silver));
 }
 static void Cards(){
  var specs=new[]{new[]{172,142},new[]{296,80},new[]{200,147},new[]{559,650},new[]{680,650}};var names=new[]{"인벤토리","상점 목록","합성 재료","3개 선택 보상","2개 선택 보상"};
  for(int i=0;i<specs.Length;i++)foreach(bool active in new[]{false,true}){int w=specs[i][0],h=specs[i][1];string title=names[i];bool a=active;Job("Cards/card_"+w+"x"+h+"_"+(a?"selected":"normal"),title+" 카드 · "+(a?"선택":"기본"),w,h,p=>Card(p,w,h,a),"그림·이름·등급이 없는 카드 바탕.",false,new[]{30,14,30,14});}
  Job("Cards/upgrade_card","영구 강화 카드 바탕",530,657,p=>DesignArt.Surface(p,0,0,530,657,new Color32(24,26,31,255),new Color32(12,14,18,255)),"아이콘·강화 단계·버튼 별도.",false,new[]{60,32,60,32});
  Job("Cards/selected_caption","아이템 선택 강조",170,53,p=>{UIFactory.Box(p,"Tint",0,0,170,53,new Color(Silver.r,Silver.g,Silver.b,.065f));UIFactory.Line(p,0,51,52,2,Silver);},"아이템 카드 하단에 덧씌우는 선택 효과.");
 }
 static void Effects(){
  Job("Effects/halo_silver","은색 공명 고리",512,512,p=>DesignArt.Halo(p,0,0,512,Silver));
  Job("Effects/halo_red","붉은 공명 고리",512,512,p=>DesignArt.Halo(p,0,0,512,Red));
  Job("Effects/traces","회로 장식",600,320,p=>DesignArt.Traces(p,0,0,600,320,new Color(Silver.r,Silver.g,Silver.b,.4f)));
 }
 static void Wheel(){
  Job("Wheel/rings","폼 체인지 원형 틀",800,800,p=>{var points=new System.Text.StringBuilder();foreach(int radius in new[]{330,156})points.Append("<circle cx='400' cy='400' r='"+radius+"' fill='none' stroke='#D1DBEB' stroke-opacity='.72' stroke-width='1'/>");for(int i=0;i<5;i++){float a=(i*72-126)*Mathf.Deg2Rad;points.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,"<path d='M{0} {1}L{2} {3}' stroke='#D1DBEB' stroke-opacity='.72'/>",400+156*Mathf.Cos(a),400+156*Mathf.Sin(a),400+330*Mathf.Cos(a),400+330*Mathf.Sin(a));}Svg(p,points.ToString(),800,800);},"800×800 중심 (400,400). 아이콘 중심 반지름245; 화·토·금·수·목을 위에서 시계방향으로 배치.");
  Job("Wheel/halo","폼 체인지 바깥 공명",800,800,p=>DesignArt.Halo(p,0,0,800,new Color(.84f,.88f,.94f,.55f)));
  Job("Wheel/hover","폼 체인지 선택 강조",170,170,p=>{UIFactory.Box(p,"Hover",0,0,170,170,new Color(.91f,.17f,.27f,.16f));},"선택 영역의 별도 오버레이.");
 }
 static void MainAndResults(){
  Job("Main/blade_logo","메인 칼날 로고",1120,660,p=>TitleArt.Blade(p,0,0,1120,660),"메인 메뉴의 독립 문양.",true,bright:false);
  Job("Main/zero_wordmark","ZERO 워드마크",810,208,p=>TitleArt.Wordmark(p,0,0,810,208),"게임 로고만 포함.",true,bright:false);
  Job("Main/korean_wordmark","경계 로고 글자",760,109,p=>{var t=UIFactory.Label(p,"경계",0,0,760,109,77,Silver);t.font=Resources.Load<TMP_FontAsset>("BoundaryUI/Fonts/TitleKorean");t.fontStyle=FontStyles.Normal;t.characterSpacing=2;},"메뉴·동적 텍스트와 별개인 로고.",true,bright:false);
  Job("Main/menu_selected","메인 메뉴 선택 바탕",700,103,p=>p.gameObject.AddComponent<TitleMenuBand>(),"메뉴 글자 없이 선택 띠만 분리.");
  Job("Main/menu_separator","메인 메뉴 구분선",545,3,p=>UIFactory.Line(p,0,1,545,1,new Color(.56f,.65f,.76f,.15f)));
  Job("Results/clear_gate","클리어 경계문",1120,806,p=>OutcomeSymbols.Draw(p,false,0,0,1120,806),"결과 제목·수치·버튼은 별도.",true,bright:false);
  Job("Results/death_talisman","사망 부적",1120,806,p=>OutcomeSymbols.Draw(p,true,0,0,1120,806),"결과 제목·수치·버튼은 별도.",true,bright:false);
  Job("Results/title_rule","결과 제목 구분선",950,6,p=>{UIFactory.Line(p,0,1,100,4,Red);UIFactory.Line(p,120,2,830,1,new Color(.65f,.75f,.86f,.25f));},"가로폭 변경 시 왼쪽 붉은 포인트 고정.",false,new[]{128,0,4,0});
 }
 static void Loading(){
  string aligned="모든 로딩 인장 파츠는 1200×1200, 피벗 중앙으로 정렬합니다. 원본 QHD 표시 배율0.875, 중심(1280,620).";
  for(int part=0;part<=6;part++){int count=part==1||part==2?5:1;for(int segment=0;segment<count;segment++){int pt=part,sg=segment;string stem=new[]{"housing","shutter","channel","rotor","ticks","core","pulse"}[part]+(count==5?"_"+(segment+1):"");string title=new[]{"고정 하우징","금속 조각","붉은 채널","회전 고리","고정 눈금","중앙 금속 인장","중앙 붉은 맥동"}[part]+(count==5?" "+(segment+1):"");Job("Loading/"+stem,title,1200,1200,p=>{var g=UIFactory.Node(p,"LoadingLayer",-680,-120,2560,1440).gameObject.AddComponent<LoadingSpriteLayers>();g.Part=pt;g.Segment=sg;},aligned+(part==3?" 이 고리만 천천히 회전.":part==2?" 해당 금속 조각 위에 겹쳐 실제 진행률로 표시.":""));}}
  Job("Loading/progress_track","로딩 진행 틀",520,4,p=>UIFactory.Line(p,0,1,520,2,new Color(.65f,.75f,.86f,.24f)));
  Job("Loading/progress_fill","로딩 진행 채움",520,4,p=>UIFactory.Box(p,"Fill",0,0,520,4,Silver),"실제 진행률에 따라 가로 마스크로 조절.");
  Job("Loading/activity_signal","진행률 미확정 신호",64,4,p=>UIFactory.Box(p,"Signal",0,0,64,4,Red),"진행률이 없을 때만 진행 틀 위에서 좌우 이동.");
 }
 public static void Run(){
  var args=Environment.GetCommandLineArgs();int ix=Array.IndexOf(args,"-partsOutput");if(ix<0||ix+1>=args.Length)throw new Exception("-partsOutput is required");output=Path.GetFullPath(args[ix+1]);Directory.CreateDirectory(output);manifest=new Manifest();
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);QualitySettings.antiAliasing=0;QualitySettings.renderPipeline=null;UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=null;
  camera=new GameObject("ExportCamera").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.allowHDR=false;camera.allowMSAA=false;camera.nearClipPlane=.1f;camera.farClipPlane=100;
  ResourceImages();Hud();Frames();Controls();Cards();Effects();Wheel();MainAndResults();Loading();
  File.WriteAllText(Path.Combine(output,"manifest.json"),JsonUtility.ToJson(manifest,true),new System.Text.UTF8Encoding(false));
  File.WriteAllText(Path.Combine(output,"export-check.txt"),"Parts: "+manifest.assets.Count+"\nEmpty parts: "+manifest.assets.Count(e=>e.visiblePixels==0)+"\nAlpha: straight RGBA, reconstructed from black/white matte passes\nReference: 2560x1440\nDynamic labels and values: omitted\n",new System.Text.UTF8Encoding(false));
  Debug.Log("BOUNDARY_UI_PARTS_DONE count="+manifest.assets.Count);
 }
}
}
#endif



