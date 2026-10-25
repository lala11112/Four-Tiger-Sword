// Copy into Assets/Baekho/Editor. Reads original prefab bounds without modifying source assets.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StudioBaekho
{
    public static class BaekhoPropCatalog
    {
        [Serializable] public sealed class LocalBounds { public Vector3 center; public Vector3 size; }
        [Serializable] private sealed class Entry { public string guid; public Vector3 size; public LocalBounds bounds; }
        [Serializable] public sealed class Connector { public Vector3 position; public Vector3 direction; }
        [Serializable] public sealed class WallMount
        {
            public string kind;
            public string family;
            public string[] sites;
            public string heightMode;
            public float height;
            public float spacing;
            public float probability;
            public string doorSide;
            public Connector[] mounts;
            public Connector[] sockets;
            public LocalBounds[] collisionBoxes;
        }

        public sealed class Measurement
        {
            public Bounds bounds;
            public WallMount wallMount;
        }

        [MenuItem("Assets/Baekho/Export Prop Bounds Catalog")]
        private static void ExportSelected()
        {
            try
            {
                string[] guids = ResolveSelectedPrefabGuids(Selection.objects);
                string output = EditorUtility.SaveFilePanel("Export Baekho prop bounds (" + guids.Length + " selected)", "", "baekho-prop-bounds", "json");
                if (string.IsNullOrEmpty(output)) return;
                int count = ExportToPath(output, guids);
                Debug.Log("Baekho prop bounds catalog exported: " + count + " prefabs, " + output);
                EditorUtility.DisplayDialog("Baekho 소품 크기", count + "개 프리팹의 크기·피벗·벽 부착 정보를 내보냈습니다. 맵 제작기의 크기 가져오기 · 프리팹 일괄 등록을 사용하세요.", "OK");
            }
            catch (Exception exception) { EditorUtility.DisplayDialog("Baekho prop catalog failed", exception.Message, "OK"); }
        }

        /// <summary>Only explicitly selected Project prefab assets; folders and scene objects are never expanded.</summary>
        public static string[] ResolveSelectedPrefabGuids(UnityEngine.Object[] selection)
        {
            const string instruction = "Unity Project 창에서 내보낼 .prefab 에셋만 직접 선택하세요. 폴더, 씬 오브젝트, 다른 종류의 에셋은 선택에서 제외하세요.";
            if (selection == null || selection.Length == 0)
                throw new InvalidOperationException(instruction);
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UnityEngine.Object selected in selection)
            {
                string path = selected == null ? "" : AssetDatabase.GetAssetPath(selected);
                if (!(selected is GameObject) || !EditorUtility.IsPersistent(selected) || !AssetDatabase.IsMainAsset(selected) ||
                    !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(instruction);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException(instruction);
                guids.Add(guid);
            }
            var list = new List<string>(guids);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        /// <summary>Root-local bounds at root scale one; renderers and colliders (including triggers) are unioned.</summary>
        public static int ExportToPath(string output, string[] guids)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before exporting prefab bounds.");
            if (guids == null) throw new ArgumentNullException("guids");
            var entries = new List<string>();
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string guid in guids)
                {
                    if (!seen.Add(guid)) continue;
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("A source prefab is missing: " + guid);
                    Measurement measurement = MeasurePrefab(prefab, path, preview);
                    if (measurement == null) { Debug.LogWarning("Baekho: skipped prefab without measurable 3D bounds: " + path); continue; }
                    string entry = JsonUtility.ToJson(new Entry { guid = guid, size = measurement.bounds.size, bounds = ToLocalBounds(measurement.bounds) }, true);
                    // Avoid JsonUtility's inline-class null serialization: wallMount is genuinely optional.
                    if (measurement.wallMount != null) entry = entry.TrimEnd().TrimEnd('}') + ",\n  \"wallMount\": " + JsonUtility.ToJson(measurement.wallMount, true) + "\n}";
                    entries.Add(entry);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            File.WriteAllText(output, "{\n  \"version\": 2,\n  \"assets\": [\n" + string.Join(",\n", entries.ToArray()) + "\n  ]\n}\n", new UTF8Encoding(false));
            return entries.Count;
        }

        /// <summary>Shared read-only inspection. The caller owns the preview scene; each temporary instance is always destroyed.</summary>
        public static Measurement MeasurePrefab(GameObject prefab, string path, Scene preview)
        {
            if (prefab == null || !EditorSceneManager.IsPreviewScene(preview))
                throw new ArgumentException("Prefab measurements require an asset and an isolated preview scene.");
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, preview) as GameObject;
            if (instance == null) throw new InvalidDataException("Could not inspect prefab: " + path);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                BaekhoWallModule[] modules = instance.GetComponentsInChildren<BaekhoWallModule>(true);
                if (modules.Length > 1 || (modules.Length == 1 && modules[0].gameObject != instance))
                    throw new InvalidDataException(path + ": BaekhoWallModule must appear once, on the prefab root.");
                BaekhoWallModule module = modules.Length == 0 ? null : modules[0];
                Bounds bounds;
                bool measured = TryMeasure(instance, out bounds);
                if (!measured && module != null && module.collisionBoxes != null && module.collisionBoxes.Length > 0)
                {
                    bounds = module.collisionBoxes[0];
                    foreach (Bounds box in module.collisionBoxes) bounds.Encapsulate(box);
                    measured = ValidVector(bounds.center) && ValidVector(bounds.size) && bounds.size.x > 0 && bounds.size.y > 0 && bounds.size.z > 0;
                }
                if (!measured) return null;
                return new Measurement { bounds = bounds, wallMount = module == null ? null : ExportWallModule(module, bounds, path) };
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static bool TryMeasure(GameObject instance, out Bounds bounds)
        {
            Bounds result = new Bounds(); bool found = false;
            Action<Bounds, Transform> add = (bounds, transform) =>
            {
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) || float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z))
                        throw new InvalidDataException("Non-finite prefab bounds: " + instance.name);
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                    else result.Encapsulate(point);
                }
            };
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>())
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (renderer.enabled && filter != null && filter.sharedMesh != null) add(filter.sharedMesh.bounds, renderer.transform);
            }
            foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (renderer.enabled && renderer.sharedMesh != null) add(renderer.localBounds, renderer.transform);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled) continue;
                var box = collider as BoxCollider; var mesh = collider as MeshCollider;
                var sphere = collider as SphereCollider; var capsule = collider as CapsuleCollider;
                if (box != null) add(new Bounds(box.center, box.size), box.transform);
                else if (mesh != null && mesh.sharedMesh != null) add(mesh.sharedMesh.bounds, mesh.transform);
                else if (sphere != null) add(new Bounds(sphere.center, Vector3.one * sphere.radius * 2), sphere.transform);
                else if (capsule != null)
                {
                    Vector3 capsuleSize = Vector3.one * capsule.radius * 2;
                    capsuleSize[capsule.direction] = Math.Max(capsule.height, capsule.radius * 2);
                    add(new Bounds(capsule.center, capsuleSize), capsule.transform);
                }
            }
            bounds = result;
            return found && bounds.size.x > 0 && bounds.size.y > 0 && bounds.size.z > 0;
        }

        private static LocalBounds ToLocalBounds(Bounds bounds)
        {
            return new LocalBounds { center = bounds.center, size = bounds.size };
        }

        private static WallMount ExportWallModule(BaekhoWallModule module, Bounds measured, string path)
        {
            Action<bool, string> require = (condition, message) => { if (!condition) throw new InvalidDataException(path + ": " + message); };
            require(Enum.IsDefined(typeof(BaekhoWallModule.ModuleKind), module.kind), "Unknown wall module kind.");
            require(Enum.IsDefined(typeof(BaekhoWallModule.HeightMode), module.heightMode), "Unknown height mode.");
            require(Enum.IsDefined(typeof(BaekhoWallModule.DoorSide), module.doorSide), "Unknown door side.");
            require(Finite(module.height) && Math.Abs(module.height) <= 300, "Height must be finite and within -300..300m.");
            require(Finite(module.spacing) && module.spacing >= .05f && module.spacing <= 100, "Spacing must be within 0.05..100m.");
            require(Finite(module.probability) && module.probability >= 0 && module.probability <= 100, "Probability must be within 0..100 percent.");
            string family = (module.family ?? "").Trim();
            require(family.Length <= 80 && !Array.Exists(family.ToCharArray(), character => char.IsControl(character)), "Family must contain at most 80 characters without control characters.");
            bool single = module.kind == BaekhoWallModule.ModuleKind.Single;
            bool corner = module.kind == BaekhoWallModule.ModuleKind.InnerCorner || module.kind == BaekhoWallModule.ModuleKind.OuterCorner;
            bool elbow = module.kind == BaekhoWallModule.ModuleKind.Elbow;
            bool tee = module.kind == BaekhoWallModule.ModuleKind.Tee;
            bool junction = elbow || tee;
            require(single || family.Length > 0, "Connected modules require a family shared by all compatible parts.");
            var sites = new List<string>();
            require(module.sites != null && module.sites.Length > 0, "Select at least one install site.");
            foreach (BaekhoWallModule.InstallSite site in module.sites)
            {
                require(Enum.IsDefined(typeof(BaekhoWallModule.InstallSite), site), "Unknown install site.");
                string siteName = LowerFirst(site.ToString());
                if (!sites.Contains(siteName)) sites.Add(siteName);
            }
            bool cornerSingle = single && sites.Contains("corner");
            require(!cornerSingle || sites.Count == 1, "Single corner fixtures use Corner as their only install site; use separate prefab variants for flat walls.");
            require(!junction || sites.Count == 1 && sites[0] == "wall", "Elbow/Tee junctions use Wall as their only install site.");
            int mountCount = corner || cornerSingle ? 2 : module.kind == BaekhoWallModule.ModuleKind.EndCap && (module.mounts == null || module.mounts.Length == 0) ? 0 : 1;
            Connector[] mounts = ExportConnectors(module, module.mounts, mountCount, "mounts", require);
            Connector[] sockets = ExportConnectors(module, module.sockets, single ? 0 : module.kind == BaekhoWallModule.ModuleKind.EndCap ? 1 : tee ? 3 : 2, "sockets", require, junction);
            if (junction)
            {
                bool vertical = false, horizontal = false, opposite = false;
                for (int index = 0; index < sockets.Length; index++)
                {
                    Connector socket = sockets[index];
                    require(Math.Abs(Vector3.Dot(mounts[0].direction, socket.direction)) < .00001f,
                        "Elbow/Tee sockets must be tangent to the same flat wall, perpendicular to the mount normal.");
                    vertical |= Math.Abs(socket.direction.y) > .99999f;
                    horizontal |= Math.Abs(socket.direction.y) < .00001f;
                    for (int other = index + 1; other < sockets.Length; other++)
                    {
                        float dot = Vector3.Dot(socket.direction, sockets[other].direction);
                        require(dot < .99999f, "Elbow/Tee sockets must have unique outgoing directions.");
                        opposite |= dot < -.99999f;
                    }
                }
                require(vertical && horizontal, "Elbow/Tee must join horizontal and vertical arms on one wall.");
                require(elbow ? Math.Abs(Vector3.Dot(sockets[0].direction, sockets[1].direction)) < .00001f : opposite,
                    "Elbow sockets must be perpendicular; Tee sockets need an opposite pair and a perpendicular branch.");
                Connector a = sockets[0];
                Connector b = Array.Find(sockets, socket => Math.Abs(Vector3.Dot(a.direction, socket.direction)) < .00001f);
                require(b != null, "Elbow/Tee needs a perpendicular socket pair.");
                Vector3 hub = a.position + a.direction * Vector3.Dot(b.position - a.position, a.direction);
                foreach (Connector socket in sockets)
                {
                    Vector3 relative = socket.position - hub;
                    float armLength = Vector3.Dot(relative, socket.direction);
                    require(armLength > .00001f && (relative - socket.direction * armLength).magnitude < .00001f,
                        "Elbow/Tee sockets must point outward from one common hub in the same wall plane.");
                }
            }
            else if (corner || cornerSingle)
            {
                require(Math.Abs(Vector3.Dot(mounts[0].direction, mounts[1].direction)) < .001f, "Corner mounts must have perpendicular room-facing directions.");
                if (corner) require(Math.Abs(Vector3.Dot(sockets[0].direction, sockets[1].direction)) < .001f, "Corner sockets must have perpendicular outgoing directions.");
            }
            else if (!single)
            {
                if (mounts.Length > 0)
                    foreach (Connector socket in sockets)
                        require(Math.Abs(Vector3.Dot(mounts[0].direction, socket.direction)) < .001f, "Sockets must run along the wall, perpendicular to the mount normal.");
                if (sockets.Length == 2)
                {
                    Vector3 delta = sockets[1].position - sockets[0].position;
                    require(delta.magnitude > .001f && Vector3.Dot(sockets[0].direction, sockets[1].direction) < -.999f,
                        "Straight sockets must be distinct and face opposite directions.");
                    require(Vector3.Dot(delta.normalized, sockets[1].direction) > .999f,
                        "Straight sockets must face outward from opposite ends on one straight axis.");
                }
            }
            if (sockets.Length == 2 && !junction)
                require(Math.Abs(sockets[0].position.y - sockets[1].position.y) < .001f, "Sockets must be at the same height for horizontal assembly.");
            Bounds[] boxes = module.collisionBoxes;
            require(!corner || (boxes != null && boxes.Length >= 2), "Corner parts need at least two collision boxes, one per arm; do not reserve the empty inside of an elbow.");
            require(!junction || (boxes != null && boxes.Length >= (tee ? 3 : 2)), "Flat-wall Elbow/Tee parts need one collision box per arm: at least two for Elbow, three for Tee.");
            if (boxes == null || boxes.Length == 0) boxes = new[] { measured };
            require(boxes.Length <= 32, "Use at most 32 collision boxes.");
            var collisionBoxes = new LocalBounds[boxes.Length];
            for (int index = 0; index < boxes.Length; index++)
            {
                Bounds box = boxes[index];
                require(ValidVector(box.center) && ValidVector(box.size) && box.size.x > 0 && box.size.y > 0 && box.size.z > 0,
                    "Collision boxes must have finite centres and strictly positive XYZ sizes within 1000m.");
                collisionBoxes[index] = ToLocalBounds(box);
            }
            return new WallMount { kind = LowerFirst(module.kind.ToString()), family = family, sites = sites.ToArray(),
                heightMode = LowerFirst(module.heightMode.ToString()), height = module.height, spacing = module.spacing,
                probability = module.probability, doorSide = LowerFirst(module.doorSide.ToString()), mounts = mounts,
                sockets = sockets, collisionBoxes = collisionBoxes };
        }

        private static Connector[] ExportConnectors(BaekhoWallModule module, Transform[] references, int expected, string label, Action<bool, string> require, bool allowVertical = false)
        {
            require((references == null ? 0 : references.Length) == expected, label + " requires exactly " + expected + " Transform references for " + module.kind + ".");
            var result = new Connector[expected];
            var seen = new HashSet<Transform>();
            for (int index = 0; index < expected; index++)
            {
                Transform reference = references[index];
                require(reference != null && (reference == module.transform || reference.IsChildOf(module.transform)), label + " must reference transforms inside the same prefab.");
                require(seen.Add(reference), label + " must not reuse the same transform twice.");
                Vector3 position = module.transform.InverseTransformPoint(reference.position);
                Vector3 direction = module.transform.InverseTransformDirection(reference.forward).normalized;
                require(ValidVector(position) && Finite(direction), label + " must have finite coordinates within 1000m.");
                bool horizontal = Math.Abs(direction.y) < .00001f && (Math.Abs(direction.x) > .99999f && Math.Abs(direction.z) < .00001f || Math.Abs(direction.z) > .99999f && Math.Abs(direction.x) < .00001f);
                bool vertical = Math.Abs(direction.y) > .99999f && Math.Abs(direction.x) < .00001f && Math.Abs(direction.z) < .00001f;
                require(horizontal || allowVertical && vertical,
                    label + " blue +Z arrows must be cardinal directions; only Elbow/Tee sockets may point vertically.");
                direction = vertical ? new Vector3(0, Math.Sign(direction.y), 0) : Math.Abs(direction.x) > .99999f ? new Vector3(Math.Sign(direction.x), 0, 0) : new Vector3(0, 0, Math.Sign(direction.z));
                result[index] = new Connector { position = position, direction = direction };
            }
            return result;
        }

        private static string LowerFirst(string value) { return char.ToLowerInvariant(value[0]) + value.Substring(1); }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
        private static bool ValidVector(Vector3 value) { return Finite(value) && Math.Abs(value.x) <= 1000 && Math.Abs(value.y) <= 1000 && Math.Abs(value.z) <= 1000; }
    }
}
