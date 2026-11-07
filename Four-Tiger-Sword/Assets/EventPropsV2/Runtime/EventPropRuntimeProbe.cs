using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EasternCyber.EventPropsV2
{
    /// <summary>Optional example-scene mechanical playback and validation; no game rewards are granted.</summary>
    public sealed class EventPropRuntimeProbe : MonoBehaviour
    {
        [Serializable] public class Result { public string id; public int started, ready, completed; public bool busyRejected, resetWorked, moved, passed, beamPassed=true; public float beamMaxExtent, beamRestExtent; public string[] movedParts; }
        [Serializable] public class Report { public bool passed; public int startFrame, endFrame; public float elapsed; public List<Result> devices = new List<Result>(); }
        public bool autoRun = true;
        IEnumerator Start()
        {
            Application.runInBackground = true;
            if (!autoRun) yield break;
            yield return null;
            var report = new Report { startFrame = Time.frameCount };
            float start = Time.realtimeSinceStartup;
            foreach (var device in FindObjectsByType<EventPropController>(FindObjectsSortMode.None))
            {
                var r = new Result { id = device.DeviceId };
                device.onUseStarted.AddListener(() => r.started++);
                device.onRewardReady.AddListener(() => r.ready++);
                device.onUseCompleted.AddListener(() => r.completed++);
                var parts = device.GetComponentsInChildren<Transform>();
                var positions = new Vector3[parts.Length]; var rotations = new Quaternion[parts.Length]; var scales=new Vector3[parts.Length];
                var movedParts = new HashSet<string>();
                var beamTransform=device.transform.Find("Healing_Active_Beam");
                var beam=beamTransform ? beamTransform.GetComponentInChildren<MeshRenderer>():null;
                device.ResetDevice();
                for (int i=0;i<parts.Length;i++) { positions[i]=parts[i].localPosition; rotations[i]=parts[i].localRotation;scales[i]=parts[i].localScale; }
                int runs = device.DeviceId == "Roulette" ? 3 : 1;
                for (int run=0;run<runs;run++)
                {
                    if (runs==3) device.UseRoulette((RouletteOutcome)run); else device.Use();
                    int before=r.started; device.Use(); r.busyRejected = r.started==before;
                    float limit=Time.realtimeSinceStartup+15;
                    while(device.IsBusy && Time.realtimeSinceStartup<limit)
                    {
                        for(int i=0;i<parts.Length;i++)
                            if(Vector3.Distance(positions[i],parts[i].localPosition)>.0005f || Quaternion.Angle(rotations[i],parts[i].localRotation)>.1f || Vector3.Distance(scales[i],parts[i].localScale)>.0005f) { r.moved=true; movedParts.Add(parts[i].name); }
                        if(beam)r.beamMaxExtent=Mathf.Max(r.beamMaxExtent,beam.bounds.size.magnitude);
                        yield return null;
                    }
                    device.ResetDevice(); yield return null;
                }
                r.resetWorked=!device.IsBusy;
                for(int i=0;i<parts.Length;i++)
                    if(Vector3.Distance(positions[i],parts[i].localPosition)>.0005f || Quaternion.Angle(rotations[i],parts[i].localRotation)>.1f || Vector3.Distance(scales[i],parts[i].localScale)>.0005f)r.resetWorked=false;
                r.movedParts=new List<string>(movedParts).ToArray();
                bool allMoved=true;foreach(var part in device.MovingPartNames)if(!movedParts.Contains(part))allMoved=false;
                if(beam){r.beamRestExtent=beam.bounds.size.magnitude;r.beamPassed=r.beamMaxExtent>.1f && r.beamRestExtent<.01f && beam.sharedMaterial.GetFloat("_Surface")==1 && beam.sharedMaterial.GetColor("_BaseColor").a<.5f;}
                r.passed=r.started==runs && r.ready==runs && r.completed==runs && r.busyRejected && r.resetWorked && r.moved && allMoved && r.beamPassed;
                report.devices.Add(r);
            }
            report.endFrame=Time.frameCount; report.elapsed=Time.realtimeSinceStartup-start;
            report.passed=report.devices.Count==7 && report.endFrame>report.startFrame && report.devices.TrueForAll(r=>r.passed);
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../runtime-validation.json"));
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("EventProps runtime validation: "+report.passed+"; frames "+report.startFrame+"–"+report.endFrame+"; "+output);
        }
    }
}
