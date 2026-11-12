using System;
using UnityEngine;

namespace StudioBaekho
{
    /// <summary>Fits replaceable visual prefabs; collision is a predictable box independent of art pivots.</summary>
    public static class BaekhoDungeonGeometry
    {
        public static GameObject PlacePiece(BaekhoPrefabPiece piece, Transform parent, Vector3 center,
            Vector3 size, Quaternion rotation, int layer, bool collider = true,
            BaekhoPieceTilingAxes tiledAxes = BaekhoPieceTilingAxes.Z)
        {
            if (piece == null || piece.prefab == null) throw new ArgumentException("A visual prefab is required.");
            if (!Positive(size) || !Positive(piece.localBounds.size))
                throw new ArgumentException("Piece size and prepared local bounds must be finite and positive.");
            if (piece.placement == BaekhoPiecePlacement.Repeat && (!Finite(piece.repeatLength) || piece.repeatLength <= 0f))
                throw new ArgumentException("Repeat length must be finite and positive.");
            if (piece.placement == BaekhoPiecePlacement.TilePreserveSize)
                return PlaceTiledPiece(piece, parent, center, size, rotation, layer, collider, tiledAxes);

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

        private static GameObject PlaceTiledPiece(BaekhoPrefabPiece piece, Transform parent, Vector3 center,
            Vector3 size, Quaternion rotation, int layer, bool collider, BaekhoPieceTilingAxes axes)
        {
            if (!ValidateTiledPiece(piece, out string error)) throw new ArgumentException(error);
            if (axes != BaekhoPieceTilingAxes.X && axes != BaekhoPieceTilingAxes.Z && axes != BaekhoPieceTilingAxes.XZ)
                throw new ArgumentException("Tiling axes must be X, Z, or XZ.");
            Vector3 authoredScale = piece.prefab.transform.localScale;
            Vector3 source = piece.localBounds.size;
            Vector3 nativeSize = Vector3.Scale(source, Abs(authoredScale));
            bool tileX = (axes & BaekhoPieceTilingAxes.X) != 0;
            bool tileZ = (axes & BaekhoPieceTilingAxes.Z) != 0;
            double countX = tileX ? Math.Max(1d, Math.Ceiling((double)size.x / nativeSize.x - 0.000001d)) : 1d;
            double countZ = tileZ ? Math.Max(1d, Math.Ceiling((double)size.z / nativeSize.z - 0.000001d)) : 1d;
            if (countX * countZ > 10000d)
                throw new ArgumentException("A piece would require over 10000 tiles, including both floor axes.");
            var scale = new Vector3(tileX ? authoredScale.x : Mathf.Sign(authoredScale.x) * size.x / source.x,
                Mathf.Sign(authoredScale.y) * size.y / source.y,
                tileZ ? authoredScale.z : Mathf.Sign(authoredScale.z) * size.z / source.z);
            float tileWidth = tileX ? nativeSize.x : size.x;
            float tileLength = tileZ ? nativeSize.z : size.z;
            var container = new GameObject(piece.prefab.name);
            // Attach while active so Unity also sends OnDestroy after an inactive staging build fails.
            var meshes = container.AddComponent<BaekhoGeneratedMeshes>();
            try
            {
                container.transform.SetParent(parent, false);
                container.transform.localPosition = center;
                container.transform.localRotation = rotation;
                container.layer = layer;
                var clipBounds = new Bounds(Vector3.zero, size);
                for (int z = 0; z < (int)countZ; z++)
                    for (int x = 0; x < (int)countX; x++)
                    {
                        var visual = UnityEngine.Object.Instantiate(piece.prefab, container.transform, false);
                        visual.name = "Visual_" + (z * (int)countX + x);
                        visual.transform.localRotation = Quaternion.identity;
                        visual.transform.localScale = scale;
                        visual.transform.localPosition = new Vector3(-size.x * 0.5f + (x + 0.5f) * tileWidth,
                            0f, -size.z * 0.5f + (z + 0.5f) * tileLength) - Vector3.Scale(piece.localBounds.center, scale);
                        foreach (var sourceCollider in visual.GetComponentsInChildren<Collider>(true)) sourceCollider.enabled = false;
                        SetLayerRecursively(visual, layer);
                        bool cropped = (x + 1) * tileWidth > size.x + 0.00001f || (z + 1) * tileLength > size.z + 0.00001f;
                        if (!cropped) continue;
                        foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
                        {
                            if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null) continue;
                            Matrix4x4 meshToContainer = RelativeMatrix(filter.transform, container.transform);
                            // Nested meshes fully within this tile can keep sharing their source mesh.
                            if (ContainsBounds(clipBounds, filter.sharedMesh.bounds, meshToContainer)) continue;
                            Mesh cut;
                            try { cut = BaekhoCorridorMeshClipper.Clip(filter.sharedMesh, meshToContainer, clipBounds); }
                            catch (Exception exception)
                            { throw new ArgumentException("Cannot crop " + piece.prefab.name + "/" + filter.name + ": " + exception.Message, exception); }
                            meshes.Own(cut);
                            filter.sharedMesh = cut;
                            if (cut.vertexCount == 0) filter.GetComponent<MeshRenderer>().enabled = false;
                        }
                    }
                if (collider) container.AddComponent<BoxCollider>().size = size;
                return container;
            }
            catch
            {
                DestroyObject(container);
                throw;
            }
        }

        public static bool ValidateTiledPiece(BaekhoPrefabPiece piece, out string error)
        {
            error = null;
            if (piece == null || piece.prefab == null) { error = "A tiled visual prefab is required."; return false; }
            string name = piece.prefab.name;
            if (!Positive(piece.localBounds.size) || !Positive(Abs(piece.prefab.transform.localScale))
                || !Positive(Vector3.Scale(piece.localBounds.size, Abs(piece.prefab.transform.localScale))))
            { error = name + ": tiled bounds and authored scale must be finite and nonzero."; return false; }
            int supported = 0;
            foreach (var renderer in piece.prefab.GetComponentsInChildren<Renderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!(renderer is MeshRenderer) || filter == null || filter.sharedMesh == null)
                { error = name + "/" + renderer.name + ": TilePreserveSize supports only static MeshFilter/MeshRenderer geometry, not " + renderer.GetType().Name + "."; return false; }
                Mesh mesh = filter.sharedMesh;
                if (!mesh.isReadable)
                { error = name + "/" + renderer.name + ": mesh '" + mesh.name + "' must be readable (enable Read/Write) for TilePreserveSize cropping."; return false; }
                if (mesh.blendShapeCount != 0)
                { error = name + "/" + renderer.name + ": blend shapes are not supported by TilePreserveSize."; return false; }
                for (int i = 0; i < mesh.subMeshCount; i++)
                    if (mesh.GetTopology(i) != MeshTopology.Triangles)
                    { error = name + "/" + renderer.name + ": tiled meshes must use triangle topology."; return false; }
                supported++;
            }
            if (supported == 0) error = name + ": TilePreserveSize requires a readable static mesh.";
            return error == null;
        }

        private static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        private static bool ContainsBounds(Bounds outer, Bounds inner, Matrix4x4 transform)
        {
            Vector3 min = outer.min - Vector3.one * 0.00001f, max = outer.max + Vector3.one * 0.00001f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = transform.MultiplyPoint3x4(inner.center + Vector3.Scale(inner.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (p.x < min.x || p.y < min.y || p.z < min.z || p.x > max.x || p.y > max.y || p.z > max.z) return false;
            }
            return true;
        }

        private static Matrix4x4 RelativeMatrix(Transform child, Transform ancestor)
        {
            // Compose the small local transforms directly. Cancelling distant world matrices can
            // lose enough precision to create microscopic extra cuts along a tile's flush faces.
            Matrix4x4 result = Matrix4x4.identity;
            for (Transform current = child; current != ancestor; current = current.parent)
            {
                if (current == null) throw new ArgumentException("The mesh must belong to its piece container.");
                result = Matrix4x4.TRS(current.localPosition, current.localRotation, current.localScale) * result;
            }
            return result;
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
            var floor = PlacePiece(style.floor, parent, middle - Vector3.up * (style.floorThickness * 0.5f),
                new Vector3(width, style.floorThickness, length), rotation, ground,
                tiledAxes: BaekhoPieceTilingAxes.XZ);
            floor.name = "Floor";
            if (style.appearance) SetPieceMaterial(floor, style.appearance.floorMaterial);
            for (int side = -1; side <= 1; side += 2)
            {
                var size = new Vector3(style.wallThickness, style.wallHeight, length);
                var wall = PlacePiece(style.wall, parent, middle + right * (side * (width + style.wallThickness) * 0.5f) + Vector3.up * (style.wallHeight * 0.5f),
                    size, rotation, 0);
                wall.name = side < 0 ? "LeftWall" : "RightWall";
                ApplyWallAppearance(wall, size, style);
            }
            var ceiling = PlacePiece(style.CeilingPiece, parent, middle + Vector3.up * (style.wallHeight + style.ceilingThickness * 0.5f),
                new Vector3(width + style.wallThickness * 2f, style.ceilingThickness, length), rotation, 0,
                tiledAxes: BaekhoPieceTilingAxes.XZ);
            ceiling.name = "Ceiling";
            if (style.appearance) SetPieceMaterial(ceiling, style.appearance.wallMaterial);
            BuildSegmentLights(style, parent, middle, rotation, width, length);
        }

        /// <summary>Places an authored fixture without scaling its art or modifying its source prefab.</summary>
        public static GameObject PlaceLight(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation, string name)
        {
            if (prefab == null) return null;
            var instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.name = name;
            instance.transform.localPosition = position;
            instance.transform.localRotation = rotation;
            instance.transform.localScale = prefab.transform.localScale;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var light in instance.GetComponentsInChildren<Light>(true))
            {
                if (light.shadows == LightShadows.None) continue;
                var shadow = light.GetComponent<BaekhoGeneratedLightShadow>()
                    ?? light.gameObject.AddComponent<BaekhoGeneratedLightShadow>();
                shadow.authoredMode = light.shadows;
                light.shadows = LightShadows.None;
            }
            return instance;
        }

        private static void BuildSegmentLights(BaekhoCorridorStyle style, Transform parent, Vector3 middle,
            Quaternion rotation, float width, float length)
        {
            if (style.wallLightPrefab == null && style.ceilingLightPrefab == null) return;
            if (!Finite(style.lightSpacing) || style.lightSpacing < 0.1f)
                throw new ArgumentException("Light spacing must be finite and at least 0.1m.");
            double count = Math.Max(1d, Math.Ceiling((double)length / style.lightSpacing));
            if (count > 10000d) throw new ArgumentException("A corridor would require over 10000 light stations.");
            for (int i = 0; i < (int)count; i++)
            {
                float along = -length * 0.5f + length * ((i + 0.5f) / (float)count);
                PlaceSegmentLight(style.ceilingLightPrefab, parent, middle, rotation, width, style.wallHeight,
                    length, along, 0, style.wallLightHeight, "CeilingLight_" + i);
                PlaceSegmentLight(style.wallLightPrefab, parent, middle, rotation, width, style.wallHeight,
                    length, along, (i & 1) == 0 ? -1 : 1, style.wallLightHeight, "WallLight_" + i);
            }
        }

        private static void PlaceSegmentLight(GameObject prefab, Transform parent, Vector3 middle,
            Quaternion corridorRotation, float width, float height, float length, float along, int side,
            float wallLightHeight, string name)
        {
            if (prefab == null) return;
            Quaternion rotation = side == 0 ? Quaternion.identity : Quaternion.LookRotation(Vector3.left * side, Vector3.up);
            Bounds bounds = LightBounds(prefab, rotation);
            Vector3 position = new Vector3(-bounds.center.x, height - bounds.max.y, along - bounds.center.z);
            if (side != 0)
            {
                position.x = side < 0 ? -width * 0.5f - bounds.min.x : width * 0.5f - bounds.max.x;
                position.y = wallLightHeight - bounds.center.y;
            }
            var fitted = new Bounds(bounds.center + position, bounds.size);
            const float tolerance = 0.001f;
            if (fitted.min.x < -width * 0.5f - tolerance || fitted.max.x > width * 0.5f + tolerance
                || fitted.min.y < -tolerance || fitted.max.y > height + tolerance
                || fitted.min.z < -length * 0.5f - tolerance || fitted.max.z > length * 0.5f + tolerance) return;
            PlaceLight(prefab, parent, middle + corridorRotation * position, corridorRotation * rotation, name);
        }

        private static Bounds LightBounds(GameObject prefab, Quaternion rotation)
        {
            bool found = false;
            Bounds result = default;
            Matrix4x4 rootToSegment = Matrix4x4.TRS(Vector3.zero, rotation, prefab.transform.localScale);
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled) continue;
                bool active = true;
                for (Transform child = renderer.transform; child != prefab.transform; child = child.parent)
                    if (!child.gameObject.activeSelf) { active = false; break; }
                if (!active) continue;
                Bounds local = renderer.localBounds;
                Matrix4x4 matrix = rootToSegment * prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!Finite(point.x) || !Finite(point.y) || !Finite(point.z))
                        throw new ArgumentException("Light fixture has invalid geometry bounds: " + prefab.name);
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                    else result.Encapsulate(point);
                }
            }
            // A fixture containing only Light components uses its root as the mounting point.
            return result;
        }

        public static void FinishDoor(BaekhoMapModule module, int socketIndex, float openWidth, BaekhoCorridorStyle style, Transform parent)
        {
            var socket = module.connections[socketIndex];
            if (openWidth <= 0f && socket.runtimeDoor != null) socket.runtimeDoor.SetActive(false);
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
                        closure.localRotation, closure.size, module, parent, "ClosedWall_" + socket.id, style);
                    if (closure.additionalSections != null)
                        for (int i = 0; i < closure.additionalSections.Length; i++)
                        {
                            var section = closure.additionalSections[i];
                            if (section == null || section.template == null)
                                throw new ArgumentException("Missing wall section on " + module.name + "/" + socket.id);
                            PlaceWallSection(section.template, section.templateLocalBounds, section.localCenter,
                                section.localRotation, section.size, module, parent, "ClosedWallFoundation_" + socket.id + "_" + i, style);
                        }
                    if (closure.replaceWhenClosed != null)
                        foreach (var filler in closure.replaceWhenClosed)
                            if (filler != null) filler.SetActive(false);
                    return;
                }
                var size = new Vector3(socket.width, height, thickness);
                var cap = PlacePiece(style.cap, parent, center, size, rotation, 0, tiledAxes: BaekhoPieceTilingAxes.X);
                cap.name = "Closed_" + socket.id;
                ApplyWallAppearance(cap, size, style);
                return;
            }
            bool corridorIsWider = openWidth > socket.width;
            float clearWidth = Mathf.Min(socket.width, openWidth);
            float shoulder = Mathf.Abs(socket.width - openWidth) * 0.5f;
            if (shoulder <= 0.001f) return;
            if (corridorIsWider)
            {
                // Close the corridor ends outside the authored wall, preserving the actual door opening
                // and avoiding coplanar faces with the room's existing wall geometry.
                height = style.wallHeight;
                center = position + Vector3.up * (height * 0.5f) + forward * (thickness * 0.5f);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                var size = new Vector3(shoulder, height, thickness);
                var cap = PlacePiece(style.cap, parent, center + right * (side * (clearWidth + shoulder) * 0.5f),
                    size, rotation, 0, tiledAxes: BaekhoPieceTilingAxes.X);
                cap.name = (corridorIsWider ? "CorridorShoulder_" : "DoorShoulder_") + socket.id;
                ApplyWallAppearance(cap, size, style);
            }
        }

        private static void PlaceWallSection(GameObject template, Bounds localBounds, Vector3 localCenter,
            Quaternion localRotation, Vector3 size, BaekhoMapModule module, Transform parent, string name, BaekhoCorridorStyle style)
        {
            if (!Positive(size) || !Positive(localBounds.size))
                throw new ArgumentException("Invalid closed wall metadata on " + module.name);
            var piece = new BaekhoPrefabPiece { prefab = template, localBounds = localBounds };
            Vector3 center = parent.InverseTransformPoint(module.transform.TransformPoint(localCenter));
            Quaternion rotation = Quaternion.Inverse(parent.rotation) * module.transform.rotation * localRotation;
            var wall = PlacePiece(piece, parent, center, size, rotation, template.layer);
            wall.name = name;
            ApplyWallAppearance(wall, size, style);
        }

        private static void ApplyWallAppearance(GameObject wall, Vector3 size, BaekhoCorridorStyle style)
        {
            if (style.appearance) BaekhoWallAppearance.Apply(wall.transform, new Bounds(Vector3.zero, size), style.appearance);
        }

        private static void SetPieceMaterial(GameObject piece, Material material)
        {
            if (!material) return;
            foreach (var renderer in piece.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
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
            if (value is GameObject root)
                foreach (var meshes in root.GetComponentsInChildren<BaekhoGeneratedMeshes>(true)) meshes.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
