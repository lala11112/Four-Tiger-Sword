using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EasternCyber.EventPropsV2.Editor
{
    [InitializeOnLoad]
    public static class RouletteStopObserver
    {
        [Serializable] class Stop { public string outcome;public float expectedBlenderDegrees,angularError;public Vector3 observedUnityEuler;public int frame; }
        [Serializable] class Report { public bool passed;public int completed;public List<Stop> stops=new List<Stop>(); }
        static RouletteStopObserver(){EditorApplication.playModeStateChanged+=Changed;}
        public static void Arm(){SessionState.SetBool("EventPropsV2.ValidateRouletteStops",true);}
        static void Changed(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("EventPropsV2.ValidateRouletteStops",false))return;
            SessionState.SetBool("EventPropsV2.ValidateRouletteStops",false);
            var device=UnityEngine.Object.FindObjectsByType<EventPropController>(FindObjectsSortMode.None).First(d=>d.DeviceId=="Roulette");
            var wheel=device.transform.Find("Wheel_Rotor_Y_Axis");
            var spec=JsonUtility.FromJson<EventPropAssetBuilder.Manifest>(File.ReadAllText(EventPropAssetBuilder.Root+"/Models/Roulette.json"));
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(EventPropAssetBuilder.Root+"/Models/Roulette.fbx");
            var points=source.GetComponentsInChildren<Transform>();
            var basis=Matrix4x4.identity;
            basis.SetColumn(0,(points.First(t=>t.name=="Basis_X").position-source.transform.position).normalized);
            basis.SetColumn(1,(points.First(t=>t.name=="Basis_Y").position-source.transform.position).normalized);
            basis.SetColumn(2,(points.First(t=>t.name=="Basis_Z").position-source.transform.position).normalized);
            Vector3 axis=basis.MultiplyVector(Vector3.up)*Mathf.Sign(basis.determinant);
            var expected=spec.metadata.outcome_stop_degrees_blender_y;
            var report=new Report();
            device.onRewardReady.AddListener(()=>
            {
                float angle=device.LastOutcome==RouletteOutcome.Miss?expected.X:device.LastOutcome==RouletteOutcome.Gold?expected.Gold:expected.Item;
                report.stops.Add(new Stop{outcome=device.LastOutcome.ToString(),expectedBlenderDegrees=angle,observedUnityEuler=wheel.localEulerAngles,
                    angularError=Quaternion.Angle(wheel.localRotation,Quaternion.AngleAxis(angle,axis)),frame=Time.frameCount});
            });
            device.onUseCompleted.AddListener(()=>
            {
                report.completed++;
                if(report.completed<3)return;
                report.passed=report.stops.Count==3 && report.stops.Select(s=>s.outcome).Distinct().Count()==3 && report.stops.All(s=>s.angularError<.15f);
                File.WriteAllText(Path.GetFullPath("../roulette-stop-validation.json"),JsonUtility.ToJson(report,true));
                Debug.Log("Roulette actual Play stop validation: "+report.passed);
            });
        }
    }
}
