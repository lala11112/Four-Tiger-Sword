using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FourTigerSword.Rendering.Editor
{
    public static class PlayerGroundShadowValidation
    {
        private const string MaterialPath = "Assets/_Project/Rendering/PlayerShadow/PlayerGroundShadow.mat";

        [MenuItem("Tools/Four Tiger Sword/Lab Visuals/Validate Player Ground Shadow")]
        private static void ValidateFromMenu()
        {
            string result = Validate();
            if (result.StartsWith("PASS", StringComparison.Ordinal)) Debug.Log(result);
            else Debug.LogError(result);
        }

        public static string Validate()
        {
            if (Application.isPlaying) return "FAIL: Run player ground shadow validation in Edit Mode.";
            GameObject scope = null;
            try
            {
                int groundLayer = LayerMask.NameToLayer("Ground");
                Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                Require(groundLayer >= 0 && material != null && material.shader.isSupported,
                    "Ground layer or supported ground shadow material is missing.");

                scope = new GameObject("Temporary Player Shadow Validation") { hideFlags = HideFlags.HideAndDontSave };
                scope.transform.position = new Vector3(1200f, 30f, 1400f);
                BoxCollider flat = Box(scope.transform, "Flat", new Vector3(0f, -0.25f, 0f),
                    new Vector3(4f, 0.5f, 4f), groundLayer);
                var player = new GameObject("Validation Player") { hideFlags = HideFlags.HideAndDontSave };
                player.transform.SetParent(scope.transform, false);
                player.transform.localPosition = Vector3.up;
                CharacterController capsule = player.AddComponent<CharacterController>();
                capsule.height = 2f;
                capsule.radius = 0.3f;
                var shadow = player.AddComponent<PlayerGroundShadow>();
                var serialized = new SerializedObject(shadow);
                serialized.FindProperty("shadowMaterial").objectReferenceValue = material;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Refresh(shadow);
                Require(shadow.IsVisible && shadow.VisibleTriangleCount == 16,
                    "The shadow did not fully cover a flat floor when grounded.");
                Require(Mathf.Abs(shadow.CurrentOpacity - 0.375f) < 0.005f,
                    "The grounded contact shadow did not reach its expected opacity.");
                var filter = player.GetComponentInChildren<MeshFilter>();
                Mesh mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                Require(renderer.shadowCastingMode == ShadowCastingMode.Off && !renderer.receiveShadows,
                    "The contact shadow mesh participates in lighting shadows.");
                Require(Mathf.Abs(filter.transform.TransformPoint(mesh.vertices[0]).y - scope.transform.position.y - 0.01f) < 0.002f,
                    "The mesh is not offset 1 cm above the floor.");

                player.transform.localPosition = Vector3.up * 2.5f;
                Refresh(shadow);
                Require(shadow.IsVisible && Mathf.Abs(shadow.CurrentOpacity - 0.1875f) < 0.005f,
                    "The contact shadow did not fade at the middle of a jump.");
                player.transform.localPosition = Vector3.up * 4.1f;
                Refresh(shadow);
                Require(!shadow.IsVisible, "The contact shadow remained visible beyond its 3 m ground range.");

                flat.enabled = false;
                BoxCollider upper = Box(scope.transform, "Upper step", new Vector3(-2f, -0.25f, 0f),
                    new Vector3(4f, 0.5f, 4f), groundLayer);
                BoxCollider lower = Box(scope.transform, "Lower step", new Vector3(2f, -0.65f, 0f),
                    new Vector3(4f, 0.5f, 4f), groundLayer);
                player.transform.localPosition = new Vector3(-0.05f, 1f, 0f);
                Refresh(shadow);
                Require(shadow.IsVisible && shadow.VisibleTriangleCount > 0 && shadow.VisibleTriangleCount < 16,
                    "The contact shadow did not clip triangles across a step.");
                Vector3[] vertices = mesh.vertices;
                foreach (int index in mesh.GetIndices(0))
                    Require(filter.transform.TransformPoint(vertices[index]).x <= scope.transform.position.x + 0.002f,
                        "A contact shadow triangle crossed the edge of the upper step.");

                lower.enabled = false;
                int stepTriangleCount = shadow.VisibleTriangleCount;
                Refresh(shadow);
                Require(shadow.IsVisible && shadow.VisibleTriangleCount == stepTriangleCount,
                    "The contact shadow extended over an open ledge.");

                upper.enabled = false;
                BoxCollider slope = Box(scope.transform, "Ramp", new Vector3(0f, -0.25f, 0f),
                    new Vector3(4f, 0.5f, 4f), groundLayer);
                slope.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);
                player.transform.localPosition = new Vector3(1f, 1.45f, -0.5f);
                player.transform.localRotation = Quaternion.Euler(0f, 70f, 0f);
                Refresh(shadow);
                Require(shadow.IsVisible && shadow.VisibleTriangleCount == 16,
                    "A walkable ramp caused holes in the contact shadow after relocation.");
                Vector3 planePoint = slope.transform.TransformPoint(Vector3.up * 0.25f);
                foreach (Vector3 vertex in mesh.vertices)
                    Require(Mathf.Abs(Vector3.Dot(filter.transform.TransformPoint(vertex) - planePoint, slope.transform.up) - 0.01f) < 0.002f,
                        "The contact shadow did not follow the ramp surface.");

                player.transform.localPosition = new Vector3(12f, 1f, 0f);
                BoxCollider trigger = Box(scope.transform, "Trigger over the void", new Vector3(12f, -0.25f, 0f),
                    Vector3.one, groundLayer);
                trigger.isTrigger = true;
                Refresh(shadow);
                Require(!shadow.IsVisible && shadow.CurrentOpacity == 0f,
                    "A Ground trigger over empty space caught the contact shadow.");

                player.SetActive(false);
                Require(mesh == null && filter == null, "The player retained temporary shadow objects after deactivation.");
                return "PASS: Ground shadow placement, jump fade, range, step and ledge clipping, ramp and relocation, trigger exclusion, and cleanup.";
            }
            catch (Exception exception)
            {
                return "FAIL: " + exception.Message;
            }
            finally
            {
                if (scope != null) Object.DestroyImmediate(scope);
            }
        }

        private static BoxCollider Box(Transform parent, string name, Vector3 position, Vector3 size, int layer)
        {
            var box = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave, layer = layer };
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            var collider = box.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        private static void Refresh(PlayerGroundShadow shadow)
        {
            Physics.SyncTransforms();
            shadow.RefreshShadow();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
