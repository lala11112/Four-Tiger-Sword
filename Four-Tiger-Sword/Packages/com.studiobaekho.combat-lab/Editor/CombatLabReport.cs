using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace StudioBaekho.CombatLab
{
    [Serializable] public sealed class FixtureConditionDifference
    {
        public string id, reason;
        public bool referencePresent, actualPresent;
        public float referenceValue, actualValue, delta, tolerance;
        public Vector3 referencePosition, actualPosition;
    }

    [Serializable] public sealed class FixtureConditionValidation
    {
        public string status = "not-requested";
        public string referenceCandidateId, referenceTargetIdentity, targetIdentity, executionStatus;
        public int referenceRepeatIndex;
        public float positionTolerance = .02f, angleTolerance = .1f, fovTolerance = .1f;
        public string observation = "Immediately before recording Arm/input delay, after grounded player, target and render camera remained stable for at least three game frames. Records positions, player yaw, camera pitch/yaw/roll and FOV; full world determinism is not asserted.";
        public List<ParameterValue> snapshot = new List<ParameterValue>();
        public List<FixtureConditionDifference> differences = new List<FixtureConditionDifference>();
    }

    [Serializable] public sealed class CandidateResult
    {
        public CombatCandidate candidate;
        public CombatMetrics metrics;
        public string status;
        public string error;
        public string videoPath;
        // Video time of session start. It can be negative when recording starts after the session.
        public float videoTimeOffsetSeconds;
        public int repeatIndex = 1;
        public CaptureMetadata capture;
        public FixtureConditionValidation fixtureValidation;
    }

    [Serializable] public sealed class ExperimentReport
    {
        public int schemaVersion = 1;
        public string runId;
        public string adapterId;
        public string sourceGuid;
        public string actionId;
        public string sourceDependencyHash;
        public string unityVersion;
        public string scenePath;
        public int seed;
        public string startedUtc;
        public List<CandidateResult> results = new List<CandidateResult>();
        public CandidateResult controlResult;
        public ExperimentRequest request;
        public string sourceContext, sourceContextPath, sourceContextHash, sourceContextCapturedUtc;
        public List<string> notes = new List<string>();
    }

    public static class CombatLabReport
    {
        /// <summary>Writes portable JSON and a standalone browser comparison. Videos live beside index.html.</summary>
        public static string Write(string directory, ExperimentReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            string json = JsonUtility.ToJson(report, true);
            WriteFile(Path.Combine(directory, "report.json"), json);
            // JSON stays data even if a candidate name contains HTML or a closing script tag.
            string scriptJson = json.Replace("<", "\\u003c").Replace(">", "\\u003e")
                .Replace("&", "\\u0026").Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
            string path = Path.Combine(directory, "index.html");
            WriteFile(path, Html.Replace("{{REPORT_DATA}}", scriptJson));
            return path;
        }

        static void WriteFile(string path, string content)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                // Windows readers (including report previews and scanners) may briefly deny delete sharing.
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(path)) File.Replace(temporary, path, null);
                        else File.Move(temporary, path);
                        break;
                    }
                    catch (IOException) when (attempt < 5 && File.Exists(temporary))
                    { System.Threading.Thread.Sleep(30); }
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        const string Html = @"<!doctype html>
<html lang='ko'>
<head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>Combat Lab · 실험 비교</title>
<style>
:root{color-scheme:dark;--bg:#101419;--panel:#191f27;--line:#303a47;--ink:#e8edf4;--muted:#a4b0bf;--accent:#a7decd}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}
main{max-width:1480px;margin:auto;padding:42px 32px 64px}h1{font-size:32px;line-height:1.2;letter-spacing:-1px;margin:8px 0 10px}h2{font-size:20px;margin:0 0 16px}h3{font-size:16px;margin:0}
p{margin:8px 0}.eyebrow{color:var(--accent);font-size:12px;letter-spacing:2px;font-weight:700}.muted,small{color:var(--muted)}.meta{display:flex;gap:10px 24px;flex-wrap:wrap;font:12px/1.6 ui-monospace,monospace;margin:16px 0 28px;overflow-wrap:anywhere}
.section{margin-top:30px}.grid{display:grid;grid-template-columns:1fr 1fr;gap:18px}.panel{border:1px solid var(--line);border-radius:12px;background:var(--panel);overflow:hidden}.bar{display:flex;align-items:center;gap:12px;padding:14px 18px;flex-wrap:wrap}.badge{background:#263d36;color:var(--accent);padding:2px 9px;border-radius:4px;font-weight:700}.bar select{flex:1;min-width:160px}
video{display:block;width:100%;aspect-ratio:16/9;background:#080a0c}.caption{padding:10px 18px;font-size:13px;min-height:43px;color:var(--muted)}.controls{display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin:18px 0 8px}button,select,input,textarea{font:inherit;color:var(--ink);border:1px solid #455061;background:#222b35;border-radius:6px;padding:8px 11px}button{cursor:pointer}button:hover{border-color:var(--accent)}button.primary{background:var(--accent);color:#15231e;border-color:var(--accent);font-weight:700}button:disabled{opacity:.45;cursor:not-allowed}button:focus-visible,input:focus-visible,select:focus-visible,textarea:focus-visible{outline:2px solid var(--accent);outline-offset:3px}input[type=checkbox]{accent-color:var(--accent)}input[type=range]{accent-color:var(--accent);padding:0;flex:1;min-width:140px}label{display:inline-flex;align-items:center;gap:7px}.timeline{display:flex;align-items:center;gap:14px;margin:14px 0}.timeline output{min-width:160px;font:12px ui-monospace,monospace;text-align:right}.notice{border-left:3px solid #627a90;padding:8px 14px;color:var(--muted);font-size:13px}.error{color:#ffb7ae}.table-wrap{overflow:auto;border:1px solid var(--line);border-radius:10px}table{width:100%;border-collapse:collapse;font-size:13px}th,td{text-align:left;padding:11px 14px;border-bottom:1px solid var(--line);vertical-align:top;min-width:140px}th{background:#1c242e;color:var(--muted);font-weight:500}tbody tr:last-child td{border-bottom:0}td.changed{color:var(--accent)}code{font-family:ui-monospace,monospace;font-size:12px;overflow-wrap:anywhere}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:14px}.card{padding:18px;border:1px solid var(--line);border-radius:10px;background:var(--panel)}.card ul{padding-left:20px;font-size:13px;color:var(--muted)}details{margin-top:10px}summary{cursor:pointer;color:var(--muted)}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:12px/1.6 ui-monospace,monospace;max-height:260px;overflow:auto}.judgment{display:flex;gap:12px;flex-wrap:wrap;align-items:flex-start}.judgment textarea{flex:1;min-width:260px;min-height:85px}.footer{margin-top:32px;font-size:12px;color:var(--muted)}a{color:var(--accent)}@media(max-width:800px){main{padding:24px 16px}.grid{grid-template-columns:1fr}h1{font-size:26px}.timeline output{min-width:125px}}
</style></head><body><main>
<header><div class='eyebrow'>STUDIO BAEKHO / COMBAT LAB</div><h1>전투 실험 비교</h1><p class='muted'>같은 시나리오의 후보를 재생하고, 수치와 타격 반응을 비교하세요.</p><div class='meta' id='meta'></div></header>
<section aria-label='영상 비교'>
<div class='grid'>
<article class='panel'><div class='bar'><span class='badge'>A</span><select id='selectA' aria-label='A 후보'></select></div><video id='videoA' playsinline preload='metadata'></video><div class='caption' id='captionA'></div></article>
<article class='panel'><div class='bar'><span class='badge'>B</span><select id='selectB' aria-label='B 후보'></select></div><video id='videoB' playsinline preload='metadata'></video><div class='caption' id='captionB'></div></article>
</div>
<div class='controls'><button id='play' class='primary'>함께 재생</button><button id='restart'>처음부터</button><button id='previous' aria-label='이전 캡처 시각으로'>이전 캡처 시각</button><button id='next' aria-label='다음 캡처 시각으로'>다음 캡처 시각</button><label>속도 <select id='rate'><option value='1'>1×</option><option value='.5'>0.5×</option><option value='.25'>0.25×</option></select></label><label><input id='align' type='checkbox'>첫 명중 기준 정렬</label><label>소리 <select id='sound'><option value='none'>음소거</option><option value='A'>A</option><option value='B'>B</option></select></label></div>
<div class='timeline'><input id='seek' type='range' min='0' max='1' value='0' step='.001' aria-label='공통 재생 위치'><output id='position'>영상 로딩 중</output></div>
<p id='playbackStatus' class='notice' role='status'>영상은 이 HTML과 같은 폴더에 있어야 합니다. 실행 중 생성된 보고서는 새로 고쳐 최신 결과를 확인하세요.</p>
<p class='muted'><small>첫 명중 정렬은 이벤트 시간에 따른 추정입니다. 가변 프레임 영상에서 1/60초 이동은 정확한 프레임 이동을 보장하지 않습니다. 타격감은 영상 비교 후 직접 플레이로 확인하세요.</small></p>
</section>
<section class='section' id='precisionSection' hidden><h2>명중 구간의 실제 캡처</h2><label>비교 후보 <select id='precisionCandidate'></select></label><div id='precisionGrid' class='grid'></div><p class='muted'><small>원본과 선택 후보의 저장된 이미지를 한 장씩 넘깁니다. 명중 상대 시각과 실제 캡처 간격을 확인하세요. 포착되지 않은 프레임은 복원하지 않습니다.</small></p></section>
<section class='section'><h2>측정 결과</h2><div class='table-wrap' id='metrics'></div></section>
<section class='section'><h2>설정값</h2><p class='muted'><small>각 셀은 실제 적용값을 우선 표시합니다. 기준 후보와 다른 값은 녹색으로 표시합니다.</small></p><div class='table-wrap' id='parameters'></div></section>
<section class='section'><h2>후보별 기록</h2><div class='cards' id='cards'></div></section>
<section class='section'><h2>판단 기록</h2><div class='judgment'><select id='winner' aria-label='선호하는 후보'></select><textarea id='feedback' aria-label='선택 이유' placeholder='묵직함, 반응 속도, 시야 가림 등 판단 이유를 적어주세요.'></textarea><button id='save'>평가 JSON 저장</button></div><p class='muted'><small>다운로드한 평가는 원본 게임 설정을 변경하지 않습니다. 후보를 적용하려면 Unity의 내보내기 기능을 사용하세요.</small></p></section>
<section class='section'><h2>실험 메모</h2><ul id='notes'></ul><details id='sourceContextDetails' hidden><summary id='sourceContextTitle'>실행 전 원본 맥락</summary><pre id='sourceContext'></pre></details></section>
<footer class='footer'>실험 원본: <a href='report.json'>report.json</a> · 외부 서비스에 연결하지 않는 로컬 보고서</footer>
</main><script id='reportData' type='application/json'>{{REPORT_DATA}}</script>
<script>
'use strict';
const report = JSON.parse(document.getElementById('reportData').textContent);
const originalResults = Array.isArray(report.results) ? report.results.filter(Boolean) : [];
const results = [...(report.controlResult?[report.controlResult]:[]),...originalResults];
const el = id => document.getElementById(id);
const text = (tag,value,cls) => { const n=document.createElement(tag); n.textContent=String(value ?? '—'); if(cls)n.className=cls; return n; };
const finite = v => typeof v==='number' && Number.isFinite(v);
const fmt = v => finite(v) ? Number(v.toFixed(4)).toString() : '—';
const statusName = value => ({'conditions-mismatch':'조건 불일치','conditions-unavailable':'조건 확인 불가',completed:'완료',failed:'오류',cancelled:'취소됨','no-hits':'명중 없음'})[value]||value||'대기';
const title = (r,i) => (r===report.controlResult?'원본 기준 · ':'')+(r?.candidate?.label || r?.candidate?.id || '후보 '+(i+1))+(r?.repeatIndex?' · '+r.repeatIndex+'회':'');
const list = v => Array.isArray(v) ? v.filter(Boolean) : [];
const videos=[el('videoA'),el('videoB')], selectors=[el('selectA'),el('selectB')];
let playing=false, loaded=[false,false], playbackGeneration=0;
[['실험',report.runId],['어댑터',report.adapterId],['Unity',report.unityVersion],['Seed',report.seed],['시작 UTC',report.startedUtc],['씬',report.scenePath],['원본 해시',report.sourceDependencyHash],['원본 맥락 해시',report.sourceContextHash]].forEach(([k,v])=>el('meta').append(text('span',k+' · '+(v??'—'))));
const choice=(select,label,value)=>{const n=text('option',label);n.value=value;select.append(n);};
results.forEach((r,i)=>selectors.forEach(s=>choice(s,title(r,i)+' · '+(r.status||'대기'),String(i))));
choice(el('winner'),'아직 선택하지 않음','');const winnerIds=new Set();results.forEach((r,i)=>{if(r!==report.controlResult&&!winnerIds.has(r.candidate.id)){choice(el('winner'),title(r,i),String(i));winnerIds.add(r.candidate.id);}});
selectors[0].value=results.length?'0':'';selectors[1].value=results.length>1?'1':results.length?'0':'';
const selected=i=>results[Number(selectors[i].value)];
const offset=i=>{const r=selected(i),hit=r?.metrics?.firstHitSeconds;if(!el('align').checked||!finite(hit)||hit<0)return 0;return Math.max(0,hit+(finite(r.videoTimeOffsetSeconds)?r.videoTimeOffsetSeconds:0));};
const active=()=>videos.map((v,i)=>({v,i})).filter(x=>loaded[x.i]&&finite(x.v.duration)&&x.v.duration>0);
function bounds(){const a=active();if(!a.length)return {min:0,max:0};return {min:Math.max(...a.map(x=>-offset(x.i))),max:Math.min(...a.map(x=>x.v.duration-offset(x.i)))};}
function logicalTime(){const a=active();return a.length?a[0].v.currentTime-offset(a[0].i):0;}
function stop(){playing=false;playbackGeneration++;videos.forEach(v=>v.pause());el('play').textContent='함께 재생';}
function seekTo(t){const b=bounds();t=Math.min(b.max,Math.max(b.min,t));active().forEach(({v,i})=>{v.currentTime=Math.min(v.duration,Math.max(0,t+offset(i)));});paint();}
function paint(){const b=bounds(),t=logicalTime(),ok=active().length>0&&b.max>b.min;el('seek').min=b.min;el('seek').max=Math.max(b.min+.001,b.max);el('seek').value=t;el('seek').disabled=!ok;['play','restart','previous','next'].forEach(id=>el(id).disabled=!ok);el('position').textContent=ok?(t.toFixed(2)+' / '+b.max.toFixed(2)+' s'):'재생 가능한 영상 없음';}
function load(i){stop();loaded[i]=false;const v=videos[i],r=selected(i);v.removeAttribute('src');v.load();const name=String(r?.videoPath||'').replace(/\\/g,'/').split('/').pop();el('caption'+(i?'B':'A')).textContent=title(r,Number(selectors[i].value))+' · '+(r?.status||'결과 없음');if(name){v.src=encodeURIComponent(name);v.load();}else{el('caption'+(i?'B':'A')).textContent+=' · 영상 없음';}paint();}
async function play(){if(playing){stop();return;}const b=bounds();if(logicalTime()>=b.max-.05)seekTo(b.min);const generation=++playbackGeneration;try{await Promise.all(active().map(({v})=>v.play()));if(generation!==playbackGeneration){videos.forEach(v=>v.pause());return;}playing=true;el('play').textContent='일시 정지';}catch(e){stop();el('playbackStatus').textContent='재생할 수 없습니다: '+e.message;}}
videos.forEach((v,i)=>{v.muted=true;v.addEventListener('loadedmetadata',()=>{loaded[i]=true;seekTo(bounds().min);paint();});v.addEventListener('error',()=>{loaded[i]=false;stop();el('caption'+(i?'B':'A')).textContent+=' · 영상 파일 또는 코덱을 확인하세요';paint();});v.addEventListener('ended',()=>{stop();paint();});selectors[i].addEventListener('change',()=>load(i));});
function captureStep(direction){stop();const a=active()[0];if(!a)return;const frames=list(selected(a.i)?.capture?.frames).filter(f=>finite(f.mediaPtsUs)&&f.kind!=='terminalHold');const now=a.v.currentTime;const target=direction>0?frames.find(f=>f.mediaPtsUs/1000000>now+.00001):[...frames].reverse().find(f=>f.mediaPtsUs/1000000<now-.00001);if(target){seekTo(target.mediaPtsUs/1000000-offset(a.i));el('playbackStatus').textContent='기록된 캡처 시각으로 이동합니다. 영상 디코더의 표시 프레임과 PNG 캡처는 다를 수 있습니다.';}else if(!frames.length){seekTo(logicalTime()+direction/60);el('playbackStatus').textContent='캡처 시각 자료 없음 · 1/60초 근사 이동입니다.';}}
el('play').onclick=play;el('restart').onclick=()=>{stop();seekTo(bounds().min);};el('previous').onclick=()=>captureStep(-1);el('next').onclick=()=>captureStep(1);el('seek').oninput=()=>{stop();seekTo(Number(el('seek').value));};
el('align').onchange=()=>{stop();seekTo(bounds().min);const missing=[0,1].some(i=>!finite(selected(i)?.metrics?.firstHitSeconds)||selected(i).metrics.firstHitSeconds<0);el('playbackStatus').textContent=el('align').checked?(missing?'첫 명중 이벤트가 없는 후보는 영상 시작을 기준으로 표시합니다.':'첫 명중 이벤트를 기준으로 공통 구간을 재생합니다. 0초가 추정 첫 명중 시점입니다.'):'영상 시작 기준으로 재생합니다.';};
el('rate').onchange=()=>videos.forEach(v=>v.playbackRate=Number(el('rate').value));el('sound').onchange=()=>videos.forEach((v,i)=>v.muted=el('sound').value!==(i?'B':'A'));
function tick(){if(playing){const a=active(),t=logicalTime(),b=bounds();if(t>=b.max-.025)stop();else a.slice(1).forEach(({v,i})=>{const expected=t+offset(i);if(Math.abs(v.currentTime-expected)>.12&&!v.seeking)v.currentTime=Math.max(0,Math.min(v.duration,expected));});paint();}requestAnimationFrame(tick);}requestAnimationFrame(tick);
function table(host,rows){const t=document.createElement('table'),head=document.createElement('thead'),h=document.createElement('tr');h.append(text('th','항목'));results.forEach((r,i)=>h.append(text('th',title(r,i))));head.append(h);t.append(head);const body=document.createElement('tbody');rows.forEach(row=>{const tr=document.createElement('tr');tr.append(text('td',row.label));row.values.forEach((value,i)=>tr.append(text('td',value,row.changed?.[i]?'changed':'')));body.append(tr);});t.append(body);el(host).append(t);}
const metricDefs=[['상태',r=>statusName(r.status)],['공격 요청',r=>fmt(r.metrics?.attackRequests)],['명중 이벤트',r=>fmt(r.metrics?.hits)],['가한 피해',r=>fmt(r.metrics?.damageDealt)],['받은 피해',r=>fmt(r.metrics?.damageTaken)],['첫 명중 (실시간 초)',r=>finite(r.metrics?.firstHitSeconds)&&r.metrics.firstHitSeconds>=0?fmt(r.metrics.firstHitSeconds):'없음'],['실행 시간 (실시간 초)',r=>fmt(r.metrics?.elapsedSeconds)]];
table('metrics',metricDefs.map(([label,get])=>({label,values:results.map(get)})));
const ids=[...new Set(results.flatMap(r=>[...list(r.candidate?.values),...list(r.metrics?.appliedValues)].map(p=>p.id).filter(Boolean)))];
function parameter(r,id){const requested=list(r.candidate?.values).find(p=>p.id===id),applied=list(r.metrics?.appliedValues).find(p=>p.id===id);return {value:applied?.value??requested?.value,label:applied?fmt(applied.value):(requested?fmt(requested.value)+' (요청값)':'—'),requested:requested?.value};}
table('parameters',ids.map(id=>{const values=results.map(r=>parameter(r,id)),base=values[0]?.value;return {label:id,values:values.map(v=>v.label+(finite(v.requested)&&v.requested!==v.value?' · 요청 '+fmt(v.requested):'')),changed:values.map(v=>finite(base)&&finite(v.value)&&v.value!==base)};}));
results.forEach((r,i)=>{const card=text('article','', 'card');card.append(text('h3',title(r,i)));card.append(text('p',r.status||'대기','muted'));if(r.error)card.append(text('p',r.error,'error'));const notes=[...list(r.metrics?.notes),...new Set(list(r.metrics?.events).filter(e=>e.kind==='value-limited').map(e=>'실행 중 적용 제한 · '+e.detail))];if(notes.length){const ul=document.createElement('ul');notes.forEach(n=>ul.append(text('li',n)));card.append(ul);}const events=list(r.metrics?.events);if(events.length){const d=document.createElement('details');d.append(text('summary','이벤트 '+events.length+'개'));d.append(text('pre',events.map(e=>fmt(e.realTime)+'s real / '+fmt(e.gameTime)+'s game · '+(e.kind||'')+' · '+(e.detail||'')+' · '+fmt(e.value)).join('\n')));card.append(d);}el('cards').append(card);});
const notes=list(report.notes);if(!notes.length)notes.push('추가 메모 없음');notes.forEach(n=>el('notes').append(text('li',n)));if(!results.length)el('playbackStatus').textContent='아직 후보 결과가 없습니다. 실행이 진행 중이면 완료 후 새로 고쳐주세요.';
if(report.sourceContext){el('sourceContextDetails').hidden=false;el('sourceContextTitle').textContent='실행 전 원본 맥락 · '+(report.sourceContextHash||'해시 없음')+' · '+(report.sourceContextCapturedUtc||'');el('sourceContext').textContent=typeof report.sourceContext==='string'?report.sourceContext:JSON.stringify(report.sourceContext,null,2);}
const safeImage=name=>typeof name==='string'&&name.split('/').length<=3&&name.split('/').every(part=>/^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}$/i.test(part)&&!part.includes('..')&&!/^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(part))&&/\.png$/i.test(name);
originalResults.forEach((r,i)=>choice(el('precisionCandidate'),title(r,i),String(i)));
let precisionTime=0;
function nearestCapture(frames,time){let best=0,distance=Infinity;frames.forEach((f,i)=>{const next=Math.abs(f.relativeTimeSeconds-time);if(next<distance-1e-9){distance=next;best=i;}});return best;}
function paintPrecision(){
el('precisionGrid').replaceChildren();const rows=[report.controlResult,originalResults[Number(el('precisionCandidate').value)]],panels=[];
function paintAll(){panels.forEach(p=>p());}
rows.forEach((r,side)=>{
const card=text('article','', 'panel');card.append(text('h3',side?title(r,0):'원본 그대로'));
const frames=list(r?.capture?.frames).filter(f=>safeImage(f.imagePath)&&finite(f.relativeTimeSeconds)&&f.kind!=='terminalHold');let index=0;
const img=document.createElement('img');img.alt=side?'후보 명중 캡처':'원본 명중 캡처';img.style.width='100%';const label=text('p','','muted'),controls=text('div','','controls');
function paint(){if(!frames.length){img.hidden=true;label.textContent='정밀 캡처 없음 · '+(r?.capture?.precisionStatus||'기록 없음');return;}
index=nearestCapture(frames,precisionTime);const f=frames[index],interval=index?(f.unityRealtimeUs-frames[index-1].unityRealtimeUs)/1000:null,src=f.imagePath.split('/').map(encodeURIComponent).join('/'),errorMs=(f.relativeTimeSeconds-precisionTime)*1000;
if(img.getAttribute('src')!==src){img.hidden=true;img.src=src;}else img.hidden=!img.complete||!img.naturalWidth;
label.textContent='공통 기준 '+(precisionTime*1000).toFixed(1)+'ms · 캡처 '+(index+1)+'/'+frames.length+' · 명중 '+(f.relativeTimeSeconds*1000).toFixed(1)+'ms · 정렬 오차 '+(errorMs>=0?'+':'')+errorMs.toFixed(1)+'ms · 직전 간격 '+(interval===null?'—':interval.toFixed(1)+'ms')+' · '+(r?.capture?.precisionStatus||'');}
for(const [name,delta]of [['이전 캡처',-1],['다음 캡처',1]]){const button=text('button',name);button.disabled=!frames.length;button.onclick=()=>{const target=Math.max(0,Math.min(frames.length-1,index+delta));precisionTime=frames[target].relativeTimeSeconds;paintAll();};controls.append(button);}
const zero=text('button','명중 시점');zero.disabled=!frames.length;zero.onclick=()=>{precisionTime=0;paintAll();};controls.append(zero);img.onload=()=>{img.hidden=false;};img.onerror=()=>{img.hidden=true;label.textContent='캡처 이미지 파일을 읽지 못했습니다.';};card.append(img,controls,label);el('precisionGrid').append(card);panels.push(paint);
});paintAll();}
el('precisionSection').hidden=!results.some(r=>r.capture);el('precisionCandidate').onchange=paintPrecision;paintPrecision();
el('save').onclick=()=>{const index=el('winner').value,r=index===''?null:results[Number(index)];const judgment={schemaVersion:1,runId:report.runId,sourceGuid:report.sourceGuid,selectedCandidateId:r?.candidate?.id??null,selectedCandidateIndex:r?originalResults.findIndex(item=>item.candidate.id===r.candidate.id):null,feedback:el('feedback').value,createdUtc:new Date().toISOString()};const url=URL.createObjectURL(new Blob([JSON.stringify(judgment,null,2)],{type:'application/json'}));const a=document.createElement('a');a.href=url;a.download='judgment-'+String(report.runId||'experiment').replace(/[^a-zA-Z0-9_-]/g,'_')+'.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);};
load(0);load(1);
</script></body></html>";
    }
}
