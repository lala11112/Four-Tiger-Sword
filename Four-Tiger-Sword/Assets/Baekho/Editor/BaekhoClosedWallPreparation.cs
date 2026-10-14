using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace StudioBaekho
{
    /// <summary>Fits a closed doorway to the actual wall bay, independently of its walkable opening.</summary>
    public static class BaekhoClosedWallPreparation
    {
        private const float Tolerance = 0.02f;

        private sealed class Candidate
        {
            public GameObject template;
            public Bounds localBounds;
            public Bounds socketBounds;
            public Quaternion moduleRotation;
        }

        private sealed class FoundationBay
        {
            public Candidate candidate;
            public Bounds bounds;
        }

        public static BaekhoClosedWall Bake(GameObject root, BaekhoMapConnection socket, float authoredCellSize = 0f)
        {
            if (socket.closedWall != null && socket.closedWall.manualOverride) return socket.closedWall;
            var empty = new BaekhoClosedWall();
            var candidates = new List<Candidate>();
            float reach = Mathf.Max(6f, authoredCellSize * 4f, socket.width * 4f);
            foreach (Transform wall in root.GetComponentsInChildren<Transform>(true))
            {
                if (wall == root.transform || !wall.gameObject.activeSelf ||
                    !wall.name.StartsWith("wall_", StringComparison.OrdinalIgnoreCase)) continue;
                bool nestedWall = false;
                for (Transform parent = wall.parent; parent != null && parent != root.transform; parent = parent.parent)
                    if (parent.name.StartsWith("wall_", StringComparison.OrdinalIgnoreCase)) { nestedWall = true; break; }
                if (nestedWall) continue;
                Bounds local;
                try { local = BaekhoDungeonSetup.MeasureGeometryBounds(wall.gameObject, true); }
                catch (InvalidDataException) { continue; }
                Bounds bounds = TransformBounds(local, socket.transform.worldToLocalMatrix * wall.localToWorldMatrix);
                // A lintel starts above the opening. It must never become the full-height wall template.
                if (bounds.min.y > Tolerance || bounds.max.y <= Tolerance || bounds.size.y <= Tolerance ||
                    bounds.size.x <= Tolerance || bounds.size.z <= Tolerance) continue;
                if (Mathf.Abs(bounds.center.z) > reach ||
                    Mathf.Min(Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x)) > reach) continue;
                Quaternion rotation = Quaternion.Inverse(root.transform.rotation) * wall.rotation;
                Quaternion socketRotation = Quaternion.Inverse(root.transform.rotation) * socket.transform.rotation;
                Quaternion relative = Quaternion.Inverse(rotation) * socketRotation;
                if (!Cardinal(relative * Vector3.right) || !Cardinal(relative * Vector3.up) || !Cardinal(relative * Vector3.forward))
                    continue;
                candidates.Add(new Candidate
                {
                    template = wall.gameObject, localBounds = local, socketBounds = bounds, moduleRotation = rotation
                });
            }

            Candidate chosen = null;
            Bounds bay = default(Bounds);
            float bestBandDistance = float.PositiveInfinity;
            float bestWidth = float.PositiveInfinity;
            float expectedWallPlane = authoredCellSize > 0f ? -authoredCellSize * 0.5f : 0f;
            // Both jamb edges must agree on the wall's vertical extent and its depth plane.
            // This avoids picking a nearer internal wall or the thin header above an opening.
            foreach (Candidate left in candidates)
            {
                Bounds a = left.socketBounds;
                if (a.max.x > Tolerance) continue;
                foreach (Candidate right in candidates)
                {
                    Bounds b = right.socketBounds;
                    if (b.min.x < -Tolerance || left == right || !SameWallBand(a, b)) continue;
                    float width = b.min.x - a.max.x;
                    if (width <= Tolerance || width > reach * 2f ||
                        a.max.x > -socket.width * 0.5f + Tolerance || b.min.x < socket.width * 0.5f - Tolerance) continue;
                    float bandDistance = Mathf.Abs(a.center.z - expectedWallPlane);
                    if (bandDistance > bestBandDistance + Tolerance ||
                        (Mathf.Abs(bandDistance - bestBandDistance) <= Tolerance && width >= bestWidth)) continue;
                    chosen = left;
                    bestBandDistance = bandDistance;
                    bestWidth = width;
                    bay = new Bounds(new Vector3((a.max.x + b.min.x) * 0.5f, a.center.y, a.center.z),
                        new Vector3(width, a.size.y, a.size.z));
                }
            }
            if (chosen == null)
            {
                Debug.LogWarning("Could not infer matching neighboring walls for " + root.name + "/" + socket.id +
                    ". The corridor style cap will be used; assign a manual closed-wall override for custom geometry.", socket);
                return empty;
            }

            Quaternion socketModuleRotation = Quaternion.Inverse(root.transform.rotation) * socket.transform.rotation;
            Quaternion socketToTemplate = Quaternion.Inverse(chosen.moduleRotation) * socketModuleRotation;
            Vector3 fittedSize = Abs(socketToTemplate * Vector3.right) * bay.size.x +
                Abs(socketToTemplate * Vector3.up) * bay.size.y + Abs(socketToTemplate * Vector3.forward) * bay.size.z;
            var fillers = new List<GameObject>();
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                bool matchingFiller = MatchingFiller(child.name, socket.id);
                bool floor = child.name.StartsWith("floor_", StringComparison.OrdinalIgnoreCase);
                bool ceiling = child.name.StartsWith("ceiling_", StringComparison.OrdinalIgnoreCase);
                if (!matchingFiller && !floor && !ceiling) continue;
                Bounds filler;
                try
                {
                    filler = TransformBounds(BaekhoDungeonSetup.MeasureGeometryBounds(child.gameObject, true),
                        socket.transform.worldToLocalMatrix * child.localToWorldMatrix);
                }
                catch (InvalidDataException) { continue; }
                if ((matchingFiller && OverlapsVolume(bay, filler)) || CoveredSurface(bay, filler, floor, ceiling))
                    fillers.Add(child.gameObject);
            }
            return new BaekhoClosedWall
            {
                template = chosen.template,
                templateLocalBounds = chosen.localBounds,
                localCenter = root.transform.InverseTransformPoint(socket.transform.TransformPoint(bay.center)),
                localRotation = chosen.moduleRotation,
                size = fittedSize,
                replaceWhenClosed = fillers.ToArray(),
                additionalSections = BakeFoundations(root, socket, bay)
            };
        }

        private static BaekhoWallSection[] BakeFoundations(GameObject root, BaekhoMapConnection socket, Bounds wallBay)
        {
            var cliffs = new List<Candidate>();
            Quaternion socketRotation = Quaternion.Inverse(root.transform.rotation) * socket.transform.rotation;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (!child.gameObject.activeSelf || !child.name.StartsWith("cliff_", StringComparison.OrdinalIgnoreCase)) continue;
                bool nested = false;
                for (Transform parent = child.parent; parent != null && parent != root.transform; parent = parent.parent)
                    if (parent.name.StartsWith("cliff_", StringComparison.OrdinalIgnoreCase)) { nested = true; break; }
                if (nested) continue;
                Bounds local;
                try { local = BaekhoDungeonSetup.MeasureGeometryBounds(child.gameObject, true); }
                catch (InvalidDataException) { continue; }
                Bounds bounds = TransformBounds(local, socket.transform.worldToLocalMatrix * child.localToWorldMatrix);
                // Only the exterior foundation band crossing this doorway's boundary plane is eligible.
                // Interior pit faces and above-floor cliffs are unrelated to closing the doorway.
                if (bounds.max.y > Tolerance || bounds.min.y >= -Tolerance || bounds.min.z > Tolerance || bounds.max.z < -Tolerance ||
                    bounds.size.x <= Tolerance || bounds.size.y <= Tolerance || bounds.size.z <= Tolerance) continue;
                if (bounds.max.x < wallBay.min.x - bounds.size.x - Tolerance ||
                    bounds.min.x > wallBay.max.x + bounds.size.x + Tolerance) continue;
                Quaternion rotation = Quaternion.Inverse(root.transform.rotation) * child.rotation;
                Quaternion relative = Quaternion.Inverse(rotation) * socketRotation;
                if (!Cardinal(relative * Vector3.right) || !Cardinal(relative * Vector3.up) || !Cardinal(relative * Vector3.forward)) continue;
                cliffs.Add(new Candidate
                {
                    template = child.gameObject, localBounds = local, socketBounds = bounds, moduleRotation = rotation
                });
            }
            if (cliffs.Count < 2) return Array.Empty<BaekhoWallSection>();
            var proposals = new List<FoundationBay>();
            foreach (Candidate left in cliffs)
            {
                Bounds a = left.socketBounds;
                if (a.max.x > -socket.width * 0.5f + Tolerance || a.max.x < wallBay.min.x - Tolerance) continue;
                foreach (Candidate right in cliffs)
                {
                    Bounds b = right.socketBounds;
                    if (b.min.x < socket.width * 0.5f - Tolerance || b.min.x > wallBay.max.x + Tolerance ||
                        left == right || !SameWallBand(a, b)) continue;
                    var gap = new Bounds(new Vector3((a.max.x + b.min.x) * 0.5f, a.center.y, a.center.z),
                        new Vector3(b.min.x - a.max.x, a.size.y, a.size.z));
                    if (gap.size.x <= Tolerance) continue;
                    bool occupied = false;
                    foreach (Candidate existing in cliffs)
                        if (existing != left && existing != right && OverlapsVolume(gap, existing.socketBounds)) { occupied = true; break; }
                    if (!occupied) proposals.Add(new FoundationBay { candidate = left, bounds = gap });
                }
            }
            if (proposals.Count == 0) return Array.Empty<BaekhoWallSection>();
            // Start at floor level; lower bands are included only when they directly continue an accepted band.
            proposals.Sort((a, b) =>
            {
                int top = b.bounds.max.y.CompareTo(a.bounds.max.y);
                return top != 0 ? top : a.bounds.size.x.CompareTo(b.bounds.size.x);
            });
            var accepted = new List<Bounds>();
            var result = new List<BaekhoWallSection>();
            Bounds moduleBounds = BaekhoDungeonSetup.MeasureGeometryBounds(root, true);
            foreach (FoundationBay proposal in proposals)
            {
                Bounds bounds = proposal.bounds;
                bool connected = Mathf.Abs(bounds.max.y) <= Tolerance;
                bool overlaps = false;
                foreach (Bounds previous in accepted)
                {
                    if (OverlapsVolume(previous, bounds)) { overlaps = true; break; }
                    if (Mathf.Abs(previous.min.y - bounds.max.y) <= Tolerance &&
                        Mathf.Abs(previous.min.x - bounds.min.x) <= Tolerance && Mathf.Abs(previous.max.x - bounds.max.x) <= Tolerance &&
                        previous.max.z >= bounds.min.z && previous.min.z <= bounds.max.z) connected = true;
                }
                if (overlaps || !connected) continue;
                Bounds moduleBay = TransformBounds(bounds, root.transform.worldToLocalMatrix * socket.transform.localToWorldMatrix);
                if (!Contains(moduleBounds, moduleBay)) continue;
                Candidate template = proposal.candidate;
                Quaternion socketToTemplate = Quaternion.Inverse(template.moduleRotation) * socketRotation;
                result.Add(new BaekhoWallSection
                {
                    template = template.template,
                    templateLocalBounds = template.localBounds,
                    localCenter = root.transform.InverseTransformPoint(socket.transform.TransformPoint(bounds.center)),
                    localRotation = template.moduleRotation,
                    size = Abs(socketToTemplate * Vector3.right) * bounds.size.x +
                        Abs(socketToTemplate * Vector3.up) * bounds.size.y + Abs(socketToTemplate * Vector3.forward) * bounds.size.z
                });
                accepted.Add(bounds);
            }
            return result.ToArray();
        }

        private static bool Contains(Bounds outer, Bounds inner)
        {
            return inner.min.x >= outer.min.x - Tolerance && inner.min.y >= outer.min.y - Tolerance && inner.min.z >= outer.min.z - Tolerance &&
                inner.max.x <= outer.max.x + Tolerance && inner.max.y <= outer.max.y + Tolerance && inner.max.z <= outer.max.z + Tolerance;
        }

        private static bool MatchingFiller(string name, string socketId)
        {
            return name.StartsWith("wall_fill-doorTop-" + socketId + "-", StringComparison.Ordinal) ||
                name.StartsWith("wall_fill-doorSide-" + socketId + "-", StringComparison.Ordinal);
        }

        private static bool CoveredSurface(Bounds bay, Bounds surface, bool floor, bool ceiling)
        {
            // Only remove a doorway-sized tile. A larger shared floor or ceiling must stay intact.
            if ((!floor && !ceiling) || surface.min.x < bay.min.x - Tolerance || surface.max.x > bay.max.x + Tolerance ||
                surface.min.z < bay.min.z - Tolerance || surface.max.z > bay.max.z + Tolerance) return false;
            return (floor && Mathf.Abs(surface.max.y - bay.min.y) <= Tolerance && surface.center.y <= bay.min.y) ||
                (ceiling && Mathf.Abs(surface.min.y - bay.max.y) <= Tolerance && surface.center.y >= bay.max.y);
        }

        private static bool SameWallBand(Bounds a, Bounds b)
        {
            return Mathf.Abs(a.min.y - b.min.y) <= Tolerance && Mathf.Abs(a.max.y - b.max.y) <= Tolerance &&
                Mathf.Abs(a.min.z - b.min.z) <= Tolerance && Mathf.Abs(a.max.z - b.max.z) <= Tolerance;
        }

        private static bool OverlapsVolume(Bounds a, Bounds b)
        {
            Vector3 overlap = Vector3.Min(a.max, b.max) - Vector3.Max(a.min, b.min);
            return overlap.x > 0.001f && overlap.y > 0.001f && overlap.z > 0.001f;
        }

        private static bool Cardinal(Vector3 vector)
        {
            return Mathf.Max(Mathf.Abs(vector.x), Mathf.Abs(vector.y), Mathf.Abs(vector.z)) >= 0.9999f;
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            Bounds result = new Bounds(matrix.MultiplyPoint3x4(bounds.center - bounds.extents), Vector3.zero);
            for (int i = 1; i < 8; i++)
                result.Encapsulate(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return result;
        }
    }
}
