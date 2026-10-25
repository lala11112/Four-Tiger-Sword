using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FourTigerSword.Rendering.Editor
{
    /// <summary>Checks scoped overrides using temporary objects without editing source assets.</summary>
    public static class LabVisualValidation
    {
        [MenuItem("Tools/Four Tiger Sword/Lab Visuals/Validate Scoped Overrides")]
        public static void ValidateFromMenu()
        {
            string result = Validate();
            if (result.StartsWith("PASS", StringComparison.Ordinal)) Debug.Log(result);
            else Debug.LogError(result);
            string shadows = ValidateShadowBudget();
            if (shadows.StartsWith("PASS", StringComparison.Ordinal)) Debug.Log(shadows);
            else Debug.LogError(shadows);
        }

        public static string ValidateShadowBudget()
        {
            if (Application.isPlaying) return "FAIL: Run shadow budget validation in Edit Mode.";

            GameObject scope = null;
            GameObject outside = null;
            GameObject controllerObject = null;
            try
            {
                var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                Require(pipeline != null, "Shadow budget validation requires the project's active URP asset.");
                bool supported = pipeline.supportsAdditionalLightShadows;
                scope = TemporaryRoot("Lab Shadow Validation Scope");
                outside = TemporaryRoot("Lab Shadow Validation Outside Scope");
                var spots = new Light[6];
                var originalModes = new LightShadows[6];
                float[] distances = { 1f, 2f, 3f, 4f, 8f, 40f };
                for (int index = 0; index < spots.Length; index++)
                {
                    spots[index] = AddLight(scope.transform, "Budget Spot " + index, LightType.Spot, 1f);
                    spots[index].transform.localPosition = Vector3.right * distances[index];
                    spots[index].shadows = originalModes[index] = index % 2 == 0 ? LightShadows.Hard : LightShadows.Soft;
                    spots[index].cullingMask = 0;
                    spots[index].shadowStrength = 0.7f;
                }
                Light unshadowed = AddLight(scope.transform, "Authored No Shadows", LightType.Point, 1f);
                unshadowed.shadows = LightShadows.None;
                unshadowed.cullingMask = 0;
                Light directional = AddLight(scope.transform, "Unaffected Directional", LightType.Directional, 1f);
                directional.shadows = LightShadows.Soft;
                directional.cullingMask = 0;
                Light outsideLight = AddLight(outside.transform, "Outside Shadow Light", LightType.Spot, 1f);
                outsideLight.shadows = LightShadows.Hard;
                outsideLight.cullingMask = 0;
                var cameraObject = new GameObject("Budget View Camera") { hideFlags = HideFlags.HideAndDontSave };
                cameraObject.transform.SetParent(scope.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 0;

                // Active lights exercise the distance selection; zero culling masks keep the open scene untouched.
                scope.SetActive(true);
                controllerObject = TemporaryRoot("Lab Shadow Validation Controller");
                LabLocalShadowBudget budget = controllerObject.AddComponent<LabLocalShadowBudget>();
                budget.explicitRoot = scope.transform;
                budget.viewCamera = camera;
                budget.maxShadowedLights = 4;
                budget.maxDistance = 25f;
                controllerObject.SetActive(true);
                budget.ApplyCurrent();
                budget.ApplyCurrent();
                Require(budget.ShadowedLightCount == (supported ? 4 : 0),
                    "The shadow count does not respect the active URP capability and four-light limit.");
                for (int index = 0; index < spots.Length; index++)
                    Require(spots[index].shadows == (supported && index < 4 ? originalModes[index] : LightShadows.None),
                        "Shadow selection did not retain the nearest lights within range, or changed on repeated application: " + index);
                Require(unshadowed.shadows == LightShadows.None, "An authored no-shadow light received shadows.");
                Require(directional.shadows == LightShadows.Soft && outsideLight.shadows == LightShadows.Hard,
                    "Shadow budgeting affected a directional light or an object outside its scope.");
                budget.RestoreOriginals();
                for (int index = 0; index < spots.Length; index++)
                    Require(spots[index].shadows == originalModes[index], "Restore did not recover an authored shadow mode: " + index);

                budget.ApplyCurrent();
                // Soft differs from both the first light's authored Hard mode and a platform fallback None mode.
                spots[0].shadows = LightShadows.Soft;
                spots[1].shadowStrength = 0.21f;
                budget.ApplyCurrent();
                budget.RestoreOriginals();
                Require(spots[0].shadows == LightShadows.Soft, "An external shadow-mode change was overwritten.");
                Require(Mathf.Approximately(spots[1].shadowStrength, 0.21f), "Externally animated shadow strength was overwritten.");
                for (int index = 1; index < spots.Length; index++)
                    Require(spots[index].shadows == originalModes[index], "Restore after external changes lost another light's original mode.");
                Require(unshadowed.shadows == LightShadows.None && directional.shadows == LightShadows.Soft &&
                    outsideLight.shadows == LightShadows.Hard, "Restore changed an untracked light.");

                return "PASS: Shadow budget scope, nearest-four/range selection, repeated application, authored-mode restoration, and external shadow overrides; additional-light shadow support=" + supported + ".";
            }
            catch (Exception exception)
            {
                return "FAIL: " + exception.Message;
            }
            finally
            {
                if (controllerObject != null) UnityEngine.Object.DestroyImmediate(controllerObject);
                if (scope != null) UnityEngine.Object.DestroyImmediate(scope);
                if (outside != null) UnityEngine.Object.DestroyImmediate(outside);
            }
        }

        public static string Validate()
        {
            if (Application.isPlaying) return "FAIL: Run scoped override validation in Edit Mode.";

            GameObject scope = null;
            GameObject outside = null;
            GameObject controllerObject = null;
            LabVisualStyle style = null;
            Material source = null;
            Material replacement = null;
            Material deathSwap = null;
            Texture2D texture = null;
            try
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                Require(shader != null, "The installed URP/Lit shader could not be found.");
                source = new Material(shader) { name = "Validation Source", hideFlags = HideFlags.HideAndDontSave };
                replacement = new Material(shader) { name = "Validation Replacement", hideFlags = HideFlags.HideAndDontSave };
                deathSwap = new Material(shader) { name = "Validation External Death Swap", hideFlags = HideFlags.HideAndDontSave };
                Require(source.HasProperty("_BaseMap") && source.HasProperty("_BaseColor") && source.HasProperty("_Smoothness"),
                    "The installed URP/Lit shader is missing its verified authoring properties.");

                texture = new Texture2D(2, 2) { name = "Validation Source Texture", hideFlags = HideFlags.HideAndDontSave };
                Color sourceColor = new Color(0.31f, 0.43f, 0.57f, 0.83f);
                Vector2 sourceScale = new Vector2(2f, 3f);
                Vector2 sourceOffset = new Vector2(0.2f, 0.4f);
                source.SetColor("_BaseColor", sourceColor);
                source.SetFloat("_Smoothness", 0.37f);
                source.SetTexture("_BaseMap", texture);
                source.SetTextureScale("_BaseMap", sourceScale);
                source.SetTextureOffset("_BaseMap", sourceOffset);

                // Inactive scopes ensure temporary test lights never illuminate the open scene.
                scope = TemporaryRoot("Lab Visual Validation Scope");
                MeshRenderer renderer = AddRenderer(scope.transform, "Scoped Renderer", source, deathSwap);
                Light point = AddLight(scope.transform, "Scoped Point", LightType.Point, 10f);
                Light directional = AddLight(scope.transform, "Scoped Directional", LightType.Directional, 2f);
                outside = TemporaryRoot("Lab Visual Validation Outside Scope");
                MeshRenderer outsideRenderer = AddRenderer(outside.transform, "Outside Renderer", source);
                Light outsideLight = AddLight(outside.transform, "Outside Point", LightType.Point, 9f);

                style = ScriptableObject.CreateInstance<LabVisualStyle>();
                style.hideFlags = HideFlags.HideAndDontSave;
                style.materialReplacements = new[]
                {
                    new LabVisualStyle.MaterialReplacement { source = source, replacement = replacement }
                };
                style.localLightIntensityMultiplier = 0.5f;
                style.localLightIntensityLimit = 3f;
                controllerObject = TemporaryRoot("Lab Visual Validation Controller");
                LabVisualStyleController controller = controllerObject.AddComponent<LabVisualStyleController>();
                controller.style = style;
                controller.explicitRoot = scope.transform;
                controllerObject.SetActive(true);
                controller.ApplyCurrent();

                Require(renderer.sharedMaterials[0] == replacement, "The listed source material was not remapped.");
                Require(renderer.sharedMaterials[1] == deathSwap, "An unlisted material was modified.");
                Require(Mathf.Approximately(point.intensity, 3f), "Local light scaling and its intensity cap were not applied.");
                Require(Mathf.Approximately(directional.intensity, 2f), "The local-light rule changed a directional light.");
                Require(outsideRenderer.sharedMaterial == source && Mathf.Approximately(outsideLight.intensity, 9f),
                    "An object outside the explicit scope was modified.");

                // Encounter preparation can add renderers after the generator's first completion callback.
                MeshRenderer lateRenderer = AddRenderer(scope.transform, "Late Encounter Renderer", source);
                controller.ApplyCurrent();
                Require(renderer.sharedMaterials[0] == replacement && lateRenderer.sharedMaterial == replacement,
                    "A repeated apply lost its earlier mapping or missed a newly spawned renderer.");
                Require(Mathf.Approximately(point.intensity, 3f), "Repeated callbacks compounded local-light intensity.");
                controller.RestoreOriginals();
                Require(renderer.sharedMaterials[0] == source && renderer.sharedMaterials[1] == deathSwap &&
                    lateRenderer.sharedMaterial == source, "Restore did not recover the original material slots.");
                Require(Mathf.Approximately(point.intensity, 10f), "Restore did not recover the original light intensity.");

                controller.ApplyCurrent();
                Material[] externalMaterials = renderer.sharedMaterials;
                externalMaterials[0] = deathSwap;
                renderer.sharedMaterials = externalMaterials;
                point.intensity = 7f;
                controller.ApplyCurrent();
                controller.RestoreOriginals();
                Require(renderer.sharedMaterials[0] == deathSwap,
                    "A death-fade or other external material swap was overwritten by apply/restore.");
                Require(Mathf.Approximately(point.intensity, 7f), "An external light-intensity change was overwritten.");
                Require(source.GetColor("_BaseColor") == sourceColor && Mathf.Approximately(source.GetFloat("_Smoothness"), 0.37f) &&
                    source.GetTexture("_BaseMap") == texture && source.GetTextureScale("_BaseMap") == sourceScale &&
                    source.GetTextureOffset("_BaseMap") == sourceOffset,
                    "Applying a style edited the original material's color, texture connection, UV transform, or smoothness.");

                return "PASS: Exact material mapping, scope isolation, late encounter renderers, idempotent light calibration, restoration, external death/light overrides, and source material preservation.";
            }
            catch (Exception exception)
            {
                return "FAIL: " + exception.Message;
            }
            finally
            {
                // Controller first: its normal lifecycle gets the same restoration check as a scene unload.
                if (controllerObject != null) UnityEngine.Object.DestroyImmediate(controllerObject);
                if (scope != null) UnityEngine.Object.DestroyImmediate(scope);
                if (outside != null) UnityEngine.Object.DestroyImmediate(outside);
                if (style != null) UnityEngine.Object.DestroyImmediate(style);
                if (source != null) UnityEngine.Object.DestroyImmediate(source);
                if (replacement != null) UnityEngine.Object.DestroyImmediate(replacement);
                if (deathSwap != null) UnityEngine.Object.DestroyImmediate(deathSwap);
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static GameObject TemporaryRoot(string name)
        {
            var root = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            return root;
        }

        private static MeshRenderer AddRenderer(Transform parent, string name, params Material[] materials)
        {
            var child = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(parent, false);
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            return renderer;
        }

        private static Light AddLight(Transform parent, string name, LightType type, float intensity)
        {
            var child = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(parent, false);
            Light light = child.AddComponent<Light>();
            light.type = type;
            light.intensity = intensity;
            return light;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
