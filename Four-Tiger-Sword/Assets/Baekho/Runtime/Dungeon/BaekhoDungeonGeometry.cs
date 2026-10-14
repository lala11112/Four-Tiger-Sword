using System;
using UnityEngine;

namespace StudioBaekho
{
    /// <summary>Fits replaceable visual prefabs; collision is a predictable box independent of art pivots.</summary>
    public static class BaekhoDungeonGeometry
    {
        public static GameObject PlacePiece(BaekhoPrefabPiece piece, Transform parent, Vector3 center,
            Vector3 size, Quaternion rotation, int layer, bool collider = true)
        {
            if (piece == null || piece.prefab == null) throw new ArgumentException("A visual prefab is required.");
            if (!Positive(size) || !Positive(piece.localBounds.size))
                throw new ArgumentException("Piece size and prepared local bounds must be finite and positive.");
            if (piece.placement == BaekhoPiecePlacement.Repeat && (!Finite(piece.repeatLength) || piece.repeatLength <= 0f))
                throw new ArgumentException("Repeat length must be finite and positive.");

            var container = new GameObject(piece.prefab.name);
            container.transform.SetParent(parent, false);
            container.transform.localPosition = center;
            container.transform.localRotation = rotation;
            container.layer = layer;
            int count = piece.placement == BaekhoPiecePlacement.Repeat ? Mathf.CeilToInt(size.z / piece.repeatLength) : 1;
            if (count > 10000) { DestroyObject(container); throw new ArgumentException("A piece would require over 10000 repeats."); }
            float offset = -size.z * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float length = piece.placement == BaekhoPiecePlacement.Repeat ? Mathf.Min(piece.repeatLength, size.z * 0.5f - offset) : size.z;
                var visual = UnityEngine.Object.Instantiate(piece.prefab, container.transform, false);
                visual.name = "Visual_" + i;
                var wanted = new Vector3(size.x, size.y, length);
                var source = piece.localBounds.size;
                var scale = new Vector3(wanted.x / source.x, wanted.y / source.y, wanted.z / source.z);
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = scale;
                visual.transform.localPosition = new Vector3(0f, 0f, offset + length * 0.5f) - Vector3.Scale(piece.localBounds.center, scale);
                foreach (var sourceCollider in visual.GetComponentsInChildren<Collider>(true)) sourceCollider.enabled = false;
                SetLayerRecursively(visual, layer);
                offset += length;
            }
            if (collider)
            {
                var box = container.AddComponent<BoxCollider>();
                box.size = size;
                box.isTrigger = false;
            }
            return container;
        }

        public static void BuildSegment(BaekhoCorridorSegment segment, float width, BaekhoCorridorStyle style, Transform parent)
        {
            var delta = segment.end - segment.start;
            if (Mathf.Abs(delta.y) > 0.001f || (Mathf.Abs(delta.x) > 0.001f && Mathf.Abs(delta.z) > 0.001f))
                throw new ArgumentException("Corridor segments must be level and axis aligned.");
            float length = delta.magnitude;
            if (length < 0.001f) return;
            var rotation = Quaternion.LookRotation(delta / length, Vector3.up);
            var middle = (segment.start + segment.end) * 0.5f;
            var right = rotation * Vector3.right;
            int ground = LayerMask.NameToLayer(style.groundLayer);
            if (ground < 0) throw new ArgumentException("Unknown ground layer: " + style.groundLayer);
            PlacePiece(style.floor, parent, middle - Vector3.up * (style.floorThickness * 0.5f),
                new Vector3(width, style.floorThickness, length), rotation, ground).name = "Floor";
            for (int side = -1; side <= 1; side += 2)
                PlacePiece(style.wall, parent, middle + right * (side * (width + style.wallThickness) * 0.5f) + Vector3.up * (style.wallHeight * 0.5f),
                    new Vector3(style.wallThickness, style.wallHeight, length), rotation, 0).name = side < 0 ? "LeftWall" : "RightWall";
        }

        public static void FinishDoor(BaekhoMapModule module, int socketIndex, float openWidth, BaekhoCorridorStyle style, Transform parent)
        {
            var socket = module.connections[socketIndex];
            if (socket.placeholders != null)
                foreach (var placeholder in socket.placeholders)
                    if (placeholder != null) placeholder.SetActive(false);
            // All geometry coordinates are relative to the generated root, not the module.
            Vector3 position = parent.InverseTransformPoint(socket.transform.position);
            Vector3 forward = parent.InverseTransformDirection(socket.transform.forward).normalized;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 right = rotation * Vector3.right;
            float height = socket.height;
            float thickness = style.wallThickness;
            Vector3 center = position + Vector3.up * (height * 0.5f) - forward * (thickness * 0.5f);
            if (openWidth <= 0f)
            {
                var closure = socket.closedWall;
                if (closure != null && closure.template != null)
                {
                    PlaceWallSection(closure.template, closure.templateLocalBounds, closure.localCenter,
                        closure.localRotation, closure.size, module, parent, "ClosedWall_" + socket.id);
                    if (closure.additionalSections != null)
                        for (int i = 0; i < closure.additionalSections.Length; i++)
                        {
                            var section = closure.additionalSections[i];
                            if (section == null || section.template == null)
                                throw new ArgumentException("Missing wall section on " + module.name + "/" + socket.id);
                            PlaceWallSection(section.template, section.templateLocalBounds, section.localCenter,
                                section.localRotation, section.size, module, parent, "ClosedWallFoundation_" + socket.id + "_" + i);
                        }
                    if (closure.replaceWhenClosed != null)
                        foreach (var filler in closure.replaceWhenClosed)
                            if (filler != null) filler.SetActive(false);
                    return;
                }
                PlacePiece(style.cap, parent, center, new Vector3(socket.width, height, thickness), rotation, 0).name = "Closed_" + socket.id;
                return;
            }
            float shoulder = (socket.width - openWidth) * 0.5f;
            if (shoulder <= 0.001f) return;
            for (int side = -1; side <= 1; side += 2)
                PlacePiece(style.cap, parent, center + right * (side * (openWidth + shoulder) * 0.5f),
                    new Vector3(shoulder, height, thickness), rotation, 0).name = "DoorShoulder_" + socket.id;
        }

        private static void PlaceWallSection(GameObject template, Bounds localBounds, Vector3 localCenter,
            Quaternion localRotation, Vector3 size, BaekhoMapModule module, Transform parent, string name)
        {
            if (!Positive(size) || !Positive(localBounds.size))
                throw new ArgumentException("Invalid closed wall metadata on " + module.name);
            var piece = new BaekhoPrefabPiece { prefab = template, localBounds = localBounds };
            Vector3 center = parent.InverseTransformPoint(module.transform.TransformPoint(localCenter));
            Quaternion rotation = Quaternion.Inverse(parent.rotation) * module.transform.rotation * localRotation;
            PlacePiece(piece, parent, center, size, rotation, template.layer).name = name;
        }

        public static void SetLayerRecursively(GameObject root, int layer)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
        }

        public static bool Positive(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && value.x > 0f && value.y > 0f && value.z > 0f;
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static void DestroyObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
