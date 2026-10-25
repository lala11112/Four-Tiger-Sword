using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StudioBaekho
{
    [DisallowMultipleComponent]
    public sealed class BaekhoDungeonGenerator : MonoBehaviour
    {
        public BaekhoDungeonSettings settings;
        public bool generateOnStart = true;
        public bool IsGenerating { get; private set; }
        public BaekhoDungeonResult LastResult { get; private set; }
        public GameObject CurrentRoot { get; private set; }
        public event Action<BaekhoDungeonResult> Completed;
        public event Action<BaekhoDungeonResult> Failed;

        private GameObject stagingRoot;
        private void Start()
        {
            if (generateOnStart) GenerateConfigured();
        }

        [ContextMenu("Generate Dungeon")]
        public void GenerateConfigured()
        {
            if (!Application.isPlaying) { Debug.LogWarning("Enter Play Mode to generate the dungeon.", this); return; }
            int actualSeed = settings != null && !settings.randomizeSeed ? settings.seed : Guid.NewGuid().GetHashCode();
            StartCoroutine(Generate(actualSeed));
        }

        public IEnumerator Generate(int seed)
        {
            if (IsGenerating)
            {
                EmitFailure(seed, "Generation is already running.");
                yield break;
            }
            IsGenerating = true;
            var result = new BaekhoDungeonResult { seed = seed };
            BaekhoDungeonPlan plan = null;
            string error = null;
            try
            {
                if ((transform.lossyScale - Vector3.one).sqrMagnitude > 0.0001f || Vector3.Dot(transform.up, Vector3.up) < 0.9999f)
                    error = "Generator root must have unit scale and no X/Z tilt.";
                else if (settings == null || settings.style == null) error = "Dungeon settings and corridor style are required.";
                else if (!ValidateStyle(settings.style, out error)) { }
                else if (!BaekhoDungeonPlanner.TryPlan(settings, seed, out plan, out error)) { }
            }
            catch (Exception exception) { error = exception.Message; }
            if (plan == null || !string.IsNullOrEmpty(error))
            {
                IsGenerating = false;
                EmitFailure(seed, error ?? "No valid layout could be generated.");
                yield break;
            }

            result.plan = plan;
            stagingRoot = new GameObject("GeneratedDungeon_" + seed);
            stagingRoot.SetActive(false);
            stagingRoot.transform.SetParent(transform, false);
            result.root = stagingRoot;
            var build = Build(plan, settings.style, result);
            while (true)
            {
                bool next = false;
                try { next = build.MoveNext(); }
                catch (Exception exception) { error = exception.ToString(); }
                if (!next || error != null) break;
                yield return build.Current;
            }
            if (error != null)
            {
                BaekhoDungeonGeometry.DestroyObject(stagingRoot);
                stagingRoot = null;
                IsGenerating = false;
                EmitFailure(seed, error);
                yield break;
            }

            if (CurrentRoot != null)
            {
                CurrentRoot.SetActive(false);
                BaekhoDungeonGeometry.DestroyObject(CurrentRoot);
            }
            CurrentRoot = stagingRoot;
            stagingRoot = null;
            CurrentRoot.SetActive(true);
            result.success = true;
            LastResult = result;
            IsGenerating = false;
            Debug.Log($"[Baekho Dungeon] Seed {seed}: {plan.rooms.Count} maps, {plan.links.Count} links, {CountCorners(plan)} corner rooms.", this);
            Completed?.Invoke(result);
        }

        private IEnumerator Build(BaekhoDungeonPlan plan, BaekhoCorridorStyle style, BaekhoDungeonResult result)
        {
            var modules = new Dictionary<int, BaekhoMapModule>();
            var used = new Dictionary<int, Dictionary<int, float>>();
            foreach (var room in plan.rooms)
            {
                var instance = InstantiateModule(room, result.root.transform, style);
                modules.Add(room.id, instance);
                used.Add(room.id, new Dictionary<int, float>());
                result.rooms.Add(instance);
                if ((room.role & BaekhoRoomRole.Start) != 0) result.startRoom = instance;
                if ((room.role & BaekhoRoomRole.End) != 0) result.endRoom = instance;
                if ((room.role & BaekhoRoomRole.Boss) != 0) result.bossRoom = instance;
                if ((room.role & BaekhoRoomRole.Shop) != 0) result.shopRoom = instance;
                yield return null;
            }
            foreach (var link in plan.links)
            {
                used[link.fromMapId].Add(link.fromSocketIndex, link.width);
                used[link.toMapId].Add(link.toSocketIndex, link.width);
                var corridorRoot = new GameObject($"Connection_{link.fromMapId}_{link.toMapId}");
                corridorRoot.transform.SetParent(result.root.transform, false);
                foreach (var corner in link.corners)
                {
                    var instance = InstantiateModule(corner, result.root.transform, style);
                    instance.name = "Corner_" + corner.id;
                    for (int i = 0; i < instance.connections.Length; i++)
                        BaekhoDungeonGeometry.FinishDoor(instance, i, link.width, style, result.root.transform);
                    yield return null;
                }
                foreach (var segment in link.segments)
                    BaekhoDungeonGeometry.BuildSegment(segment, link.width, style, corridorRoot.transform);
                yield return null;
            }
            foreach (var pair in modules)
            {
                for (int i = 0; i < pair.Value.connections.Length; i++)
                {
                    used[pair.Key].TryGetValue(i, out float width);
                    BaekhoDungeonGeometry.FinishDoor(pair.Value, i, width, style, result.root.transform);
                }
            }
        }

        private static BaekhoMapModule InstantiateModule(BaekhoPlacedModule placement, Transform parent, BaekhoCorridorStyle style)
        {
            var instance = Instantiate(placement.prefab, parent, false);
            instance.name = (placement.role == BaekhoRoomRole.Normal ? "Map" : placement.role.ToString().Replace(", ", "_"))
                + "_" + placement.id + "_" + placement.prefab.name;
            var identity = instance.GetComponent<BaekhoGeneratedRoom>() ?? instance.gameObject.AddComponent<BaekhoGeneratedRoom>();
            identity.instanceId = placement.id;
            identity.role = placement.role;
            instance.transform.localPosition = placement.position;
            instance.transform.localRotation = placement.Rotation;
            instance.transform.localScale = Vector3.one;
            int groundLayer = LayerMask.NameToLayer(style.groundLayer);
            foreach (var ground in instance.groundObjects)
                if (ground != null) BaekhoDungeonGeometry.SetLayerRecursively(ground, groundLayer);
            return instance;
        }

        public static bool ValidateStyle(BaekhoCorridorStyle style, out string error)
        {
            error = null;
            if (style == null) { error = "Corridor style is required."; return false; }
            if (!BaekhoDungeonGeometry.Positive(new Vector3(style.wallHeight, style.wallThickness, style.floorThickness)))
                error = "Wall height/thickness and floor thickness must be finite and positive.";
            else if (!BaekhoDungeonGeometry.Finite(style.ceilingThickness) || style.ceilingThickness <= 0f)
                error = "Ceiling thickness must be finite and positive.";
            else if (!BaekhoDungeonGeometry.Finite(style.lightSpacing) || style.lightSpacing < 0.1f)
                error = "Light spacing must be finite and at least 0.1m.";
            else if (!BaekhoDungeonGeometry.Finite(style.wallLightHeight) || style.wallLightHeight <= 0f || style.wallLightHeight > style.wallHeight)
                error = "Wall light height must be finite, positive and no higher than the corridor wall.";
            else if (LayerMask.NameToLayer(style.groundLayer) < 0) error = "Ground layer does not exist: " + style.groundLayer;
            else
            {
                foreach (var piece in new[] { style.floor, style.wall, style.cap, style.CeilingPiece })
                {
                    if (piece == null || piece.prefab == null || !BaekhoDungeonGeometry.Positive(piece.localBounds.size))
                    { error = "Assign floor, wall and cap prefabs and prepare all piece bounds, including the ceiling when assigned."; break; }
                    if (piece.placement == BaekhoPiecePlacement.Repeat && (!BaekhoDungeonGeometry.Finite(piece.repeatLength) || piece.repeatLength < 0.1f))
                    { error = "Repeated prefab length must be at least 0.1m."; break; }
                    if (piece.placement == BaekhoPiecePlacement.TilePreserveSize && !BaekhoDungeonGeometry.ValidateTiledPiece(piece, out error)) break;
                    if (piece.prefab.GetComponentInChildren<Rigidbody>(true) != null)
                    { error = "Corridor visual prefabs must not contain Rigidbodies."; break; }
                }
                if (error == null)
                    foreach (var prefab in new[] { style.wallLightPrefab, style.ceilingLightPrefab })
                    {
                        if (prefab == null) continue;
                        Vector3 scale = prefab.transform.localScale;
                        if (!BaekhoDungeonGeometry.Positive(new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))))
                        { error = "Light fixture root scale must be finite and nonzero: " + prefab.name; break; }
                        if (prefab.GetComponentInChildren<Rigidbody>(true) != null)
                        { error = "Light fixture prefabs must not contain Rigidbodies: " + prefab.name; break; }
                    }
            }
            return error == null;
        }

        [ContextMenu("Clear Generated Dungeon")]
        public void ClearGenerated()
        {
            StopAllCoroutines();
            IsGenerating = false;
            if (stagingRoot != null) { stagingRoot.SetActive(false); BaekhoDungeonGeometry.DestroyObject(stagingRoot); }
            stagingRoot = null;
            if (CurrentRoot != null) { CurrentRoot.SetActive(false); BaekhoDungeonGeometry.DestroyObject(CurrentRoot); }
            CurrentRoot = null;
            LastResult = null;
        }

        private void OnDisable()
        {
            if (!IsGenerating) return;
            StopAllCoroutines();
            IsGenerating = false;
            if (stagingRoot != null) BaekhoDungeonGeometry.DestroyObject(stagingRoot);
            stagingRoot = null;
        }

        private void EmitFailure(int seed, string error)
        {
            var result = new BaekhoDungeonResult { seed = seed, success = false, error = error, root = CurrentRoot };
            LastResult = result;
            Debug.LogWarning($"[Baekho Dungeon] Seed {seed}: {error}", this);
            Failed?.Invoke(result);
        }

        private static int CountCorners(BaekhoDungeonPlan plan)
        {
            int count = 0;
            foreach (var link in plan.links) count += link.corners.Count;
            return count;
        }
    }
}
