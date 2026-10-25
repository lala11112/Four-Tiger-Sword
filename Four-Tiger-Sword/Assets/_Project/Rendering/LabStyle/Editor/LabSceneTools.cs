using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FourTigerSword.Rendering.Editor
{
    /// <summary>Explicit opt-in scene setup. No vendor materials, pipeline assets or prefabs are edited.</summary>
    public static class LabSceneTools
    {
        public const string Folder = "Assets/_Project/Rendering/LabStyle";
        public const string ProfilePath = Folder + "/LabVolumeProfile.asset";

        public static VolumeProfile CreateProfile()
        {
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (existing != null) return existing;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            var tone = Add<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.Neutral);
            var color = Add<ColorAdjustments>(profile);
            color.postExposure.Override(0.1f);
            color.contrast.Override(8f);
            color.saturation.Override(-4f);
            var balance = Add<WhiteBalance>(profile);
            balance.temperature.Override(-4f);
            balance.tint.Override(0f);
            var ranges = Add<ShadowsMidtonesHighlights>(profile);
            ranges.shadows.Override(new Vector4(0.98f, 1f, 1.025f, 0f));
            ranges.midtones.Override(new Vector4(1f, 1f, 1f, 0f));
            ranges.highlights.Override(new Vector4(1f, 1f, 1f, 0f));
            ranges.shadowsStart.Override(0f);
            ranges.shadowsEnd.Override(0.3f);
            ranges.highlightsStart.Override(0.6f);
            ranges.highlightsEnd.Override(1f);
            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(1.15f);
            bloom.intensity.Override(0.18f);
            bloom.scatter.Override(0.45f);
            bloom.highQualityFiltering.Override(false);
            var vignette = Add<Vignette>(profile);
            vignette.intensity.Override(0.12f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }

        private static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var component = profile.Add<T>(false);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        [MenuItem("Tools/Four Tiger Sword/Lab Visuals/Apply Lighting To Active Scene")]
        public static void ApplyLightingToActiveScene()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Apply scene lighting outside Play Mode.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (UniversalRenderPipeline.asset == null) throw new InvalidOperationException("This setup requires the installed URP pipeline.");
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Apply Lab Lighting");
            var settings = Unsupported.GetRenderSettings();
            Undo.RecordObject(settings, "Lab ambient lighting");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.60f, 0.67f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.50f, 0.56f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.28f, 0.34f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.reflectionIntensity = 0.7f;

            GameObject rig = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "Lab Visual Lighting") rig = root;
            if (rig == null)
            {
                rig = new GameObject("Lab Visual Lighting");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rig, scene);
                Undo.RegisterCreatedObjectUndo(rig, "Create lab lighting");
            }
            var volume = rig.GetComponent<Volume>();
            if (volume == null) volume = Undo.AddComponent<Volume>(rig);
            Undo.RecordObject(volume, "Lab volume");
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.weight = 1f;
            volume.sharedProfile = CreateProfile();

            // A low-energy directional fill gives consistent orientation in open modules;
            // enclosed rooms retain their authored fixture lighting and real-time shadows.
            var light = rig.GetComponent<Light>();
            if (light == null) light = Undo.AddComponent<Light>(rig);
            Undo.RecordObject(light, "Lab key light");
            light.type = LightType.Directional;
            light.color = new Color(0.90f, 0.95f, 1f);
            light.intensity = 0.35f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.8f;
            light.shadowBias = 0.035f;
            light.shadowNormalBias = 0.25f;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            Undo.RecordObject(rig.transform, "Lab light direction");
            rig.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

            foreach (var root in scene.GetRootGameObjects())
            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
            {
                if (camera.cameraType != CameraType.Game) continue;
                var data = camera.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) continue;
                Undo.RecordObject(data, "Lab camera post processing");
                data.renderPostProcessing = true;
                data.volumeLayerMask |= 1 << rig.layer;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            SceneView.RepaintAll();
        }
    }
}
