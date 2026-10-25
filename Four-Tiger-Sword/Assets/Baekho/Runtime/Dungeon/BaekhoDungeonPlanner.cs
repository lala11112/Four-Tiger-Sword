using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioBaekho
{
    /// <summary>Deterministic layout planning. No scene objects or global random state are changed.</summary>
    public static class BaekhoDungeonPlanner
    {
        private const float Epsilon = 0.002f;

        private sealed class ModuleData
        {
            public BaekhoMapModule prefab;
            public BaekhoSocketData[] sockets;
            public readonly List<int> usable = new List<int>();
        }

        private sealed class PlacedData
        {
            public ModuleData data;
            public BaekhoPlacedModule placed;
            public bool[] used;
            public int depth;
            public int degree;
        }

        // Shared across every candidate and layout retry. A failed candidate also spends
        // one unit, so geometry rejected before A* cannot bypass the total work limit.
        private sealed class PlanningBudget
        {
            public int remaining;
            public int candidates;
            public int expansions;

            public bool SpendCandidate()
            {
                if (remaining <= 0) return false;
                remaining--; candidates++; return true;
            }

            public bool SpendExpansion()
            {
                if (remaining <= 0) return false;
                remaining--; expansions++; return true;
            }
        }

        private struct RectXZ
        {
            public float minX, maxX, minZ, maxZ;
            public RectXZ(Bounds bounds)
            {
                minX = bounds.min.x; maxX = bounds.max.x;
                minZ = bounds.min.z; maxZ = bounds.max.z;
            }
            public RectXZ Expanded(float amount)
            {
                return new RectXZ { minX = minX - amount, maxX = maxX + amount,
                    minZ = minZ - amount, maxZ = maxZ + amount };
            }
            public bool Intersects(RectXZ other)
            {
                return maxX > other.minX + Epsilon && other.maxX > minX + Epsilon
                    && maxZ > other.minZ + Epsilon && other.maxZ > minZ + Epsilon;
            }
            public bool ContainsIntersection(RectXZ a, RectXZ b)
            {
                return Mathf.Max(a.minX, b.minX) >= minX - Epsilon
                    && Mathf.Min(a.maxX, b.maxX) <= maxX + Epsilon
                    && Mathf.Max(a.minZ, b.minZ) >= minZ - Epsilon
                    && Mathf.Min(a.maxZ, b.maxZ) <= maxZ + Epsilon;
            }
        }

        private sealed class RouteNode
        {
            public Vector3 position;
            public int direction;
            public float distance;
            public int order;
            public float score;
            public RouteNode previous;
            // A node represents either one straight step or one complete corner room.
            public BaekhoPlacedModule corner;
            public int cornerInput;
            public int cornerOutput;
            public bool hasStraightRun;
        }

        private struct StateKey : IEquatable<StateKey>
        {
            private readonly int x, z, direction;
            private readonly bool canTurn;
            public StateKey(RouteNode node, float grid)
            {
                x = Mathf.RoundToInt(node.position.x / grid);
                z = Mathf.RoundToInt(node.position.z / grid);
                direction = node.direction; canTurn = node.hasStraightRun;
            }
            public bool Equals(StateKey other) => x == other.x && z == other.z
                && direction == other.direction && canTurn == other.canTurn;
            public override bool Equals(object obj) => obj is StateKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return (((x * 397) ^ z) * 397 ^ direction) * 2 + (canTurn ? 1 : 0); }
            }
        }

        private sealed class NodeHeap
        {
            private readonly List<RouteNode> nodes = new List<RouteNode>();
            public int Count => nodes.Count;
            private static bool Before(RouteNode a, RouteNode b)
                => a.score < b.score || (a.score == b.score && (a.distance > b.distance
                    || (a.distance == b.distance && a.order < b.order)));
            public void Push(RouteNode node)
            {
                int index = nodes.Count; nodes.Add(node);
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!Before(node, nodes[parent])) break;
                    nodes[index] = nodes[parent]; index = parent;
                }
                nodes[index] = node;
            }
            public RouteNode Pop()
            {
                RouteNode result = nodes[0];
                RouteNode last = nodes[nodes.Count - 1]; nodes.RemoveAt(nodes.Count - 1);
                if (nodes.Count == 0) return result;
                int index = 0;
                while (index * 2 + 1 < nodes.Count)
                {
                    int child = index * 2 + 1;
                    if (child + 1 < nodes.Count && Before(nodes[child + 1], nodes[child])) child++;
                    if (!Before(nodes[child], last)) break;
                    nodes[index] = nodes[child]; index = child;
                }
                nodes[index] = last;
                return result;
            }
        }

        public static bool TryPlan(BaekhoDungeonSettings settings, int seed,
            out BaekhoDungeonPlan plan, out string error)
        {
            plan = null;
            if (!Validate(settings, out List<ModuleData> maps, out ModuleData corner,
                out ModuleData startRoom, out ModuleData endRoom, out ModuleData bossRoom,
                out ModuleData shopRoom, out error)) return false;
            var random = new System.Random(seed);
            var budget = new PlanningBudget { remaining = settings.maxSearchNodes };
            bool hasBoss = bossRoom != null;
            bool needsBranch = settings.mapCount >= (hasBoss ? 5 : 4);
            var hubs = new List<ModuleData>();
            foreach (ModuleData map in maps) if (map.usable.Count >= 3) hubs.Add(map);
            bool branchAtStart = needsBranch && hubs.Count == 0;
            var roots = new List<ModuleData>();
            ModuleData explicitRoot = startRoom ?? (settings.mapCount == 1 ? endRoom : null);
            if (explicitRoot != null)
            {
                if (!branchAtStart || explicitRoot.usable.Count >= 3) roots.Add(explicitRoot);
            }
            else
                foreach (ModuleData map in maps)
                    if (!branchAtStart || map.usable.Count >= 3) roots.Add(map);
            if (roots.Count == 0)
            {
                error = "A branching layout needs either an ordinary map with three usable doors or a start room with three usable doors.";
                return false;
            }

            for (int layout = 0; layout < settings.layoutAttempts; layout++)
            {
                var candidate = new BaekhoDungeonPlan { seed = seed, startMapId = 0,
                    endMapId = settings.mapCount - 1, bossMapId = hasBoss ? settings.mapCount - 2 : -1,
                    shopMapId = shopRoom != null ? settings.mapCount - (hasBoss ? 3 : 2) : -1 };
                var placed = new List<PlacedData>();
                ModuleData root = roots[random.Next(roots.Count)];
                var rootPlacement = new BaekhoPlacedModule { id = 0, prefab = root.prefab,
                    position = Vector3.zero, quarterTurns = random.Next(4),
                    role = settings.mapCount == 1 ? BaekhoRoomRole.Start | BaekhoRoomRole.End : BaekhoRoomRole.Start };
                candidate.rooms.Add(rootPlacement);
                placed.Add(new PlacedData { data = root, placed = rootPlacement, used = new bool[root.sockets.Length] });
                bool complete = true;
                int hubId = branchAtStart ? 0 : 1;

                for (int mapIndex = 1; mapIndex < settings.mapCount; mapIndex++)
                {
                    bool isEnd = mapIndex == candidate.endMapId;
                    bool isBoss = mapIndex == candidate.bossMapId;
                    bool isShop = mapIndex == candidate.shopMapId;
                    bool added = false;
                    for (int attempt = 0; attempt < settings.placementAttempts && !added; attempt++)
                    {
                        if (!budget.SpendCandidate())
                        {
                            error = BudgetError(settings, seed, budget);
                            return false;
                        }
                        var parents = ParentCandidates(placed, needsBranch, hubId,
                            isEnd && hasBoss ? candidate.bossMapId : -1, isEnd || isBoss);
                        if (parents.Count == 0) break;
                        PlacedData parent = parents[random.Next(parents.Count)];
                        var available = new List<int>();
                        foreach (int index in parent.data.usable) if (!parent.used[index]) available.Add(index);
                        int fromSocket = available[random.Next(available.Count)];
                        ModuleData data;
                        if (isBoss) data = bossRoom;
                        else if (isShop) data = shopRoom;
                        else if (isEnd && endRoom != null) data = endRoom;
                        else if (needsBranch && !branchAtStart && mapIndex == hubId) data = hubs[random.Next(hubs.Count)];
                        else data = maps[random.Next(maps.Count)];
                        int toSocket = data.usable[random.Next(data.usable.Count)];
                        Vector3 source = SocketPosition(parent.placed, parent.data, fromSocket);
                        int sourceDirection = Direction(SocketForward(parent.placed, parent.data, fromSocket));

                        // Mix bent routes with cheap straight fallbacks; every chosen transform remains on the grid.
                        int choice = random.Next(4);
                        int targetDirection = (sourceDirection + (choice == 0 ? 1 : choice == 1 ? 3 : 2)) % 4;
                        int rotation = (targetDirection - Direction(data.sockets[toSocket].forward) + 4) % 4;
                        var room = new BaekhoPlacedModule { id = mapIndex, prefab = data.prefab, quarterTurns = rotation,
                            role = isBoss ? BaekhoRoomRole.Boss : isEnd ? BaekhoRoomRole.End
                                : isShop ? BaekhoRoomRole.Shop : BaekhoRoomRole.Normal };
                        Vector3 relativeSocket = Rotate(data.sockets[toSocket].position, rotation);
                        RectXZ relativeBounds = new RectXZ(room.WorldBounds);
                        Vector3 forward = Forward(sourceDirection), right = Forward((sourceDirection + 1) % 4);
                        float inset = Mathf.Min(0f, MinProjection(relativeBounds, forward) - Vector3.Dot(relativeSocket, forward));
                        float exitDepth = Mathf.Max(0f, MaxProjection(new RectXZ(parent.placed.WorldBounds), forward) - Vector3.Dot(source, forward));
                        float extra = settings.gridSize * random.Next(2, Mathf.Max(3,
                            Mathf.FloorToInt(settings.maximumConnectionLength * 0.3f / settings.gridSize)));
                        float distance = SnapUp(exitDepth + settings.minimumMapGap - inset + extra, settings.gridSize);
                        int lateralCells = Mathf.Max(1, Mathf.FloorToInt(settings.maximumConnectionLength * 0.25f / settings.gridSize));
                        float lateral = attempt % 4 == 3 ? 0f : random.Next(-lateralCells, lateralCells + 1) * settings.gridSize;
                        Vector3 target = source + forward * distance + right * lateral;
                        room.position = target - relativeSocket;
                        if (!RoomFits(room, candidate, settings.minimumMapGap, settings.style.wallThickness)) continue;

                        // Door openings are finished locally; they must not narrow the entire connection.
                        float width = settings.corridorWidth;
                        if (TryRoute(settings, candidate, parent, fromSocket, room, data, toSocket, corner, width, budget,
                            out BaekhoDungeonLink link))
                        {
                            parent.used[fromSocket] = true;
                            parent.degree++;
                            var child = new PlacedData { data = data, placed = room, used = new bool[data.sockets.Length],
                                depth = parent.depth + 1, degree = 1 };
                            child.used[toSocket] = true;
                            placed.Add(child); candidate.rooms.Add(room); candidate.links.Add(link); added = true;
                        }
                    }
                    if (!added) { complete = false; break; }
                }
                if (complete)
                {
                    int cornerId = settings.mapCount;
                    foreach (BaekhoDungeonLink connection in candidate.links)
                        foreach (BaekhoPlacedModule turn in connection.corners) turn.id = cornerId++;
                    plan = candidate; error = null; return true;
                }
            }
            if (budget.remaining == 0)
            {
                error = BudgetError(settings, seed, budget);
                return false;
            }
            error = $"Could not place {settings.mapCount} maps without overlap for seed {seed} "
                + $"within {settings.layoutAttempts} layouts and {settings.placementAttempts} candidates per map. "
                + "Increase the connection length or attempts, or reduce the map count/gap.";
            return false;
        }

        private static List<PlacedData> ParentCandidates(List<PlacedData> placed, bool needsBranch,
            int hubId, int requiredParentId, bool preferDeepest)
        {
            // The start may have only one doorway. An ordinary hub receives an incoming
            // connection and two children before the terminal chain consumes the remaining slots.
            int forcedHub = needsBranch && placed.Count > hubId && placed[hubId].degree < 3 ? hubId : -1;
            var candidates = new List<PlacedData>();
            int deepest = -1;
            foreach (PlacedData item in placed)
            {
                // A shop is an optional destination, never a route to another room.
                if ((item.placed.role & BaekhoRoomRole.Shop) != 0) continue;
                if (!HasUnusedSocket(item)) continue;
                if (requiredParentId >= 0 && item.placed.id != requiredParentId) continue;
                if (requiredParentId < 0 && forcedHub >= 0 && item.placed.id != forcedHub) continue;
                if (preferDeepest && item.depth < deepest) continue;
                if (preferDeepest && item.depth > deepest) { candidates.Clear(); deepest = item.depth; }
                candidates.Add(item);
            }
            return candidates;
        }

        private static bool TryRoute(BaekhoDungeonSettings settings, BaekhoDungeonPlan plan,
            PlacedData from, int fromIndex, BaekhoPlacedModule to, ModuleData toData, int toIndex,
            ModuleData corner, float width, PlanningBudget budget, out BaekhoDungeonLink link)
        {
            link = null;
            Vector3 start = SocketPosition(from.placed, from.data, fromIndex);
            Vector3 end = SocketPosition(to, toData, toIndex);
            int direction = Direction(SocketForward(from.placed, from.data, fromIndex));
            int finalDirection = (Direction(SocketForward(to, toData, toIndex)) + 2) % 4;
            if (Mathf.Abs(start.y - end.y) > Epsilon || Manhattan(start, end) > settings.maximumConnectionLength + Epsilon) return false;

            // Route and collision checks use the same configured width for straight and bent connections.
            var root = new RouteNode { position = start, direction = direction };
            if (CanFinish(root, end, finalDirection, settings.maximumConnectionLength)
                && SegmentFits(start, end, width, root, settings, plan, from, fromIndex, to, toData, toIndex))
            {
                link = NewLink(from.placed.id, fromIndex, to.id, toIndex, width);
                link.segments.Add(new BaekhoCorridorSegment(start, end));
                return true;
            }

            var heap = new NodeHeap();
            var visited = new Dictionary<StateKey, float>();
            int order = 0;
            root.score = Manhattan(start, end); root.order = order++;
            heap.Push(root); visited[new StateKey(root, settings.gridSize)] = 0f;
            while (heap.Count > 0 && budget.SpendExpansion())
            {
                RouteNode node = heap.Pop();
                StateKey key = new StateKey(node, settings.gridSize);
                if (visited.TryGetValue(key, out float best) && node.distance > best + Epsilon) continue;
                if (!IsFirstRun(node) && CanFinish(node, end, finalDirection, settings.maximumConnectionLength)
                    && SegmentFits(node.position, end, width, node, settings, plan, from, fromIndex, to, toData, toIndex))
                {
                    var finish = new RouteNode { position = end, direction = node.direction, previous = node };
                    link = NewLink(from.placed.id, fromIndex, to.id, toIndex, width);
                    BuildRoute(finish, link);
                    return true;
                }

                Vector3 next = node.position + Forward(node.direction) * settings.gridSize;
                float nextDistance = node.distance + settings.gridSize;
                if (nextDistance + Manhattan(next, end) <= settings.maximumConnectionLength + Epsilon
                    && SegmentFits(node.position, next, width, node, settings, plan, from, fromIndex, to, toData, toIndex))
                    Enqueue(new RouteNode { position = next, direction = node.direction, distance = nextDistance,
                        previous = node, hasStraightRun = true }, end, settings.gridSize, visited, heap, ref order);

                if (!node.hasStraightRun) continue;
                // Both input assignments are essential: rotating an L alone cannot substitute for its reflection.
                for (int input = 0; input < 2; input++)
                {
                    int output = 1 - input;
                    int rotation = (node.direction + 2 - Direction(corner.sockets[input].forward) + 4) % 4;
                    var turn = new BaekhoPlacedModule { id = -1, prefab = corner.prefab, quarterTurns = rotation,
                        position = node.position - Rotate(corner.sockets[input].position, rotation) };
                    Vector3 exit = SocketPosition(turn, corner, output);
                    float distance = node.distance + Manhattan(node.position, exit);
                    if (distance + Manhattan(exit, end) > settings.maximumConnectionLength + Epsilon) continue;
                    if (!CornerFits(turn, input, node, width, settings, plan, from.placed, to)) continue;
                    Enqueue(new RouteNode { position = exit, direction = Direction(SocketForward(turn, corner, output)),
                        distance = distance, previous = node, corner = turn, cornerInput = input, cornerOutput = output,
                        hasStraightRun = false }, end, settings.gridSize, visited, heap, ref order);
                }
            }
            return false;
        }

        private static string BudgetError(BaekhoDungeonSettings settings, int seed, PlanningBudget budget)
            => $"Total planning budget maxSearchNodes={settings.maxSearchNodes} exhausted for seed {seed} "
                + $"after {budget.candidates} placement candidates and {budget.expansions} route expansions across all layout retries. "
                + "Check map/corner bounds and doorway clearance, reduce map count or spacing, or increase maxSearchNodes.";

        private static void Enqueue(RouteNode node, Vector3 end, float grid, Dictionary<StateKey, float> visited,
            NodeHeap heap, ref int order)
        {
            StateKey key = new StateKey(node, grid);
            if (visited.TryGetValue(key, out float previous) && previous <= node.distance + Epsilon) return;
            visited[key] = node.distance;
            node.order = order++;
            node.score = node.distance + Manhattan(node.position, end);
            heap.Push(node);
        }

        private static bool CanFinish(RouteNode node, Vector3 end, int finalDirection, float maximum)
        {
            Vector3 delta = end - node.position;
            float distance = Vector3.Dot(delta, Forward(node.direction));
            return node.direction == finalDirection && distance > Epsilon
                && Mathf.Abs(Vector3.Dot(delta, Forward((node.direction + 1) % 4))) < Epsilon
                && node.distance + distance <= maximum + Epsilon;
        }

        private static BaekhoDungeonLink NewLink(int from, int fromSocket, int to, int toSocket, float width)
            => new BaekhoDungeonLink { fromMapId = from, fromSocketIndex = fromSocket,
                toMapId = to, toSocketIndex = toSocket, width = width };

        private static void BuildRoute(RouteNode finish, BaekhoDungeonLink link)
        {
            var nodes = new List<RouteNode>();
            for (RouteNode node = finish; node != null; node = node.previous) nodes.Add(node);
            nodes.Reverse();
            Vector3 runStart = nodes[0].position;
            for (int i = 1; i < nodes.Count; i++)
            {
                RouteNode node = nodes[i];
                if (node.corner == null) continue;
                link.segments.Add(new BaekhoCorridorSegment(runStart, node.previous.position));
                link.corners.Add(node.corner);
                runStart = node.position;
            }
            link.segments.Add(new BaekhoCorridorSegment(runStart, finish.position));
        }

        private static bool RoomFits(BaekhoPlacedModule room, BaekhoDungeonPlan plan, float gap, float wallThickness)
        {
            RectXZ bounds = new RectXZ(room.WorldBounds);
            foreach (BaekhoPlacedModule existing in plan.rooms)
                if (bounds.Expanded(gap).Intersects(new RectXZ(existing.WorldBounds))) return false;
            foreach (BaekhoDungeonLink link in plan.links)
            {
                foreach (BaekhoPlacedModule corner in link.corners)
                    if (bounds.Intersects(new RectXZ(corner.WorldBounds))) return false;
                foreach (BaekhoCorridorSegment segment in link.segments)
                    if (bounds.Intersects(SegmentBounds(segment.start, segment.end, link.width, wallThickness))) return false;
            }
            return true;
        }

        private static bool SegmentFits(Vector3 start, Vector3 end, float width, RouteNode history,
            BaekhoDungeonSettings settings, BaekhoDungeonPlan plan, PlacedData from, int fromIndex,
            BaekhoPlacedModule to, ModuleData toData, int toIndex)
        {
            RectXZ bounds = SegmentBounds(start, end, width, settings.style.wallThickness);
            foreach (BaekhoPlacedModule room in plan.rooms)
            {
                RectXZ obstacle = new RectXZ(room.WorldBounds);
                if (!bounds.Intersects(obstacle)) continue;
                if (room == from.placed && IsFirstRun(history)
                    && IsDoorwayRun(start, end, SocketPosition(room, from.data, fromIndex),
                        SocketForward(room, from.data, fromIndex), false)
                    && AtMouth(bounds, obstacle, SocketPosition(room, from.data, fromIndex),
                        SocketForward(room, from.data, fromIndex), width, settings.style.wallThickness)) continue;
                return false;
            }
            RectXZ targetBounds = new RectXZ(to.WorldBounds);
            if (bounds.Intersects(targetBounds)
                && (!IsDoorwayRun(start, end, SocketPosition(to, toData, toIndex), SocketForward(to, toData, toIndex), true)
                    || !AtMouth(bounds, targetBounds, SocketPosition(to, toData, toIndex),
                        SocketForward(to, toData, toIndex), width, settings.style.wallThickness))) return false;

            foreach (BaekhoDungeonLink previousLink in plan.links)
            {
                foreach (BaekhoPlacedModule turn in previousLink.corners)
                    if (bounds.Intersects(new RectXZ(turn.WorldBounds))) return false;
                foreach (BaekhoCorridorSegment segment in previousLink.segments)
                    if (bounds.Intersects(SegmentBounds(segment.start, segment.end, previousLink.width, settings.style.wallThickness))) return false;
            }

            bool latestCorner = true;
            for (RouteNode node = history; node != null && node.previous != null; node = node.previous)
            {
                if (node.corner != null)
                {
                    RectXZ obstacle = new RectXZ(node.corner.WorldBounds);
                    if (bounds.Intersects(obstacle))
                    {
                        BaekhoSocketData socket = node.corner.prefab.GetSocket(node.cornerOutput);
                        Vector3 forward = Rotate(socket.forward, node.corner.quarterTurns);
                        if (!latestCorner || !IsDoorwayRun(start, end, node.position, forward, false)
                            || !AtMouth(bounds, obstacle, node.position, forward, width, settings.style.wallThickness)) return false;
                    }
                    latestCorner = false;
                }
                else if (bounds.Intersects(SegmentBounds(node.previous.position, node.position, width, settings.style.wallThickness)))
                    return false;
            }
            return true;
        }

        private static bool CornerFits(BaekhoPlacedModule turn, int input, RouteNode history, float width,
            BaekhoDungeonSettings settings, BaekhoDungeonPlan plan, BaekhoPlacedModule from, BaekhoPlacedModule to)
        {
            RectXZ bounds = new RectXZ(turn.WorldBounds);
            foreach (BaekhoPlacedModule room in plan.rooms)
                if (bounds.Intersects(new RectXZ(room.WorldBounds))) return false;
            if (bounds.Intersects(new RectXZ(to.WorldBounds))) return false;
            foreach (BaekhoDungeonLink previousLink in plan.links)
            {
                foreach (BaekhoPlacedModule corner in previousLink.corners)
                    if (bounds.Intersects(new RectXZ(corner.WorldBounds))) return false;
                foreach (BaekhoCorridorSegment segment in previousLink.segments)
                    if (bounds.Intersects(SegmentBounds(segment.start, segment.end, previousLink.width, settings.style.wallThickness))) return false;
            }
            bool incomingRun = true;
            BaekhoSocketData socket = turn.prefab.GetSocket(input);
            Vector3 inputPosition = turn.position + Rotate(socket.position, turn.quarterTurns);
            Vector3 inputForward = Rotate(socket.forward, turn.quarterTurns);
            for (RouteNode node = history; node != null && node.previous != null; node = node.previous)
            {
                if (node.corner != null)
                {
                    if (bounds.Intersects(new RectXZ(node.corner.WorldBounds))) return false;
                    incomingRun = false;
                }
                else
                {
                    RectXZ segment = SegmentBounds(node.previous.position, node.position, width, settings.style.wallThickness);
                    if (bounds.Intersects(segment)
                        && (!incomingRun || !IsDoorwayRun(node.previous.position, node.position, inputPosition, inputForward, true)
                            || !AtMouth(segment, bounds, inputPosition, inputForward, width, settings.style.wallThickness))) return false;
                }
            }
            return true;
        }

        private static bool IsDoorwayRun(Vector3 start, Vector3 end, Vector3 socket, Vector3 outward, bool entering)
        {
            Vector3 perpendicular = Forward((Direction(outward) + 1) % 4);
            return Mathf.Abs(Vector3.Dot(start - socket, perpendicular)) < Epsilon
                && Mathf.Abs(Vector3.Dot(end - socket, perpendicular)) < Epsilon
                && Vector3.Dot(start - socket, outward) >= -Epsilon
                && Vector3.Dot(end - socket, outward) >= -Epsilon
                && Vector3.Dot(end - start, outward) * (entering ? -1f : 1f) > Epsilon;
        }

        // The only overlap exemption is the selected owner's outward doorway throat.
        // Third-party geometry is checked independently above, even inside that throat.
        private static bool AtMouth(RectXZ corridor, RectXZ owner, Vector3 position, Vector3 forward,
            float width, float wallThickness)
        {
            float half = width * 0.5f + wallThickness;
            RectXZ throat;
            int direction = Direction(forward);
            if (direction == 0 || direction == 2)
                throat = new RectXZ { minX = position.x - half, maxX = position.x + half,
                    minZ = direction == 0 ? position.z : owner.minZ,
                    maxZ = direction == 0 ? owner.maxZ : position.z };
            else
                throat = new RectXZ { minZ = position.z - half, maxZ = position.z + half,
                    minX = direction == 1 ? position.x : owner.minX,
                    maxX = direction == 1 ? owner.maxX : position.x };
            return throat.ContainsIntersection(corridor, owner);
        }

        private static RectXZ SegmentBounds(Vector3 start, Vector3 end, float width, float wallThickness)
        {
            float half = width * 0.5f + wallThickness;
            bool xAxis = Mathf.Abs(end.x - start.x) > Mathf.Abs(end.z - start.z);
            return new RectXZ { minX = Mathf.Min(start.x, end.x) - (xAxis ? 0f : half),
                maxX = Mathf.Max(start.x, end.x) + (xAxis ? 0f : half),
                minZ = Mathf.Min(start.z, end.z) - (xAxis ? half : 0f),
                maxZ = Mathf.Max(start.z, end.z) + (xAxis ? half : 0f) };
        }

        private static bool IsFirstRun(RouteNode node)
        {
            for (; node != null; node = node.previous) if (node.corner != null) return false;
            return true;
        }

        private static bool HasUnusedSocket(PlacedData room)
        {
            foreach (int index in room.data.usable) if (!room.used[index]) return true;
            return false;
        }

        private static bool Validate(BaekhoDungeonSettings settings, out List<ModuleData> maps,
            out ModuleData corner, out ModuleData startRoom, out ModuleData endRoom,
            out ModuleData bossRoom, out ModuleData shopRoom, out string error)
        {
            maps = new List<ModuleData>(); corner = null; startRoom = null; endRoom = null;
            bossRoom = null; shopRoom = null; error = null;
            if (settings == null) { error = "Dungeon settings are missing."; return false; }
            if (settings.mapCount < 1 || settings.placementAttempts < 1 || settings.layoutAttempts < 1 || settings.maxSearchNodes < 1
                || !Positive(settings.gridSize) || !Positive(settings.maximumConnectionLength)
                || !Positive(settings.corridorWidth) || !Positive(settings.minimumDoorWidth)
                || !Finite(settings.minimumMapGap) || settings.minimumMapGap < 0f
                || settings.corridorWidth + Epsilon < settings.minimumDoorWidth)
            { error = "Dungeon counts, limits, grid, widths, and gap must be finite and valid positive values."; return false; }
            if (settings.style == null || !Positive(settings.style.wallThickness)
                || !Positive(settings.style.wallHeight) || !Positive(settings.style.floorThickness))
            { error = "A corridor style with positive finite wall/floor dimensions is required."; return false; }
            if (settings.bossRoom != null && settings.mapCount < 3)
            { error = "A boss before a separate end room requires mapCount of at least three (start, boss, end)."; return false; }
            if (settings.shopRoom != null && settings.mapCount < (settings.bossRoom != null ? 5 : 4))
            { error = "A shop branch requires mapCount of at least four, or five with a boss (start, ordinary hub, shop, optional boss, end)."; return false; }
            if (settings.shopRoom != null && (settings.shopRoom == settings.startRoom
                || settings.shopRoom == settings.endRoom || settings.shopRoom == settings.bossRoom))
            { error = "The dedicated Shop Room must use a separate prefab from start, end and boss rooms."; return false; }
            if (settings.mapCount == 1 && settings.startRoom != null && settings.endRoom != null
                && settings.startRoom != settings.endRoom)
            { error = "A one-room layout cannot use different explicit start and end prefabs; use one prefab or increase mapCount."; return false; }
            if (settings.startRoom != null && !ValidateModule(settings.startRoom, false, settings, out startRoom, out error)) return false;
            if (settings.endRoom != null && !ValidateModule(settings.endRoom, false, settings, out endRoom, out error)) return false;
            if (settings.shopRoom != null && !ValidateModule(settings.shopRoom, false, settings, out shopRoom, out error)) return false;
            if (settings.bossRoom != null)
            {
                if (!ValidateModule(settings.bossRoom, false, settings, out bossRoom, out error)) return false;
                if (bossRoom.usable.Count < 2)
                { error = "The boss room needs at least two usable doors: one entrance and one connection to the separate end room."; return false; }
            }
            int ordinaryCount = Mathf.Max(0, settings.mapCount - 2 - (bossRoom != null ? 1 : 0)
                - (shopRoom != null ? 1 : 0));
            bool needsPool = settings.mapCount == 1 ? startRoom == null && endRoom == null
                : ordinaryCount > 0 || startRoom == null || endRoom == null;
            if (needsPool && settings.maps != null)
            {
                foreach (BaekhoMapModule prefab in settings.maps)
                {
                    // Explicit role assets are never drawn again as ordinary/random endpoint rooms.
                    if (prefab != null && (prefab == settings.startRoom || prefab == settings.endRoom
                        || prefab == settings.bossRoom || prefab == settings.shopRoom)) continue;
                    if (!ValidateModule(prefab, false, settings, out ModuleData data, out error)) return false;
                    maps.Add(data);
                }
            }
            if (needsPool && maps.Count == 0)
            { error = "The ordinary Maps pool is empty after excluding explicit start/end/boss/shop prefabs. Add an ordinary map or assign the missing endpoint prefab."; return false; }
            if (!ValidateModule(settings.cornerRoom, true, settings, out corner, out error)) return false;
            return true;
        }

        private static bool ValidateModule(BaekhoMapModule prefab, bool isCorner, BaekhoDungeonSettings settings,
            out ModuleData data, out string error)
        {
            data = null; error = null;
            if (prefab == null) { error = isCorner ? "A corner-room prefab is required." : "The map list contains a missing prefab."; return false; }
            string label = prefab.name;
            if (prefab.isCornerRoom != isCorner) { error = $"{label}: corner-room classification does not match its slot."; return false; }
            Bounds bounds = prefab.localBounds;
            if (!Finite(bounds.center) || !Finite(bounds.size) || bounds.size.x <= Epsilon || bounds.size.z <= Epsilon || bounds.size.y < 0f)
            { error = $"{label}: local geometry bounds are missing or invalid."; return false; }
            for (Transform current = prefab.transform; current != null; current = current.parent)
                if (!UnitScale(current.localScale)) { error = $"{label}: module/ancestor scale must be (1,1,1)."; return false; }
            if (prefab.connections == null || prefab.connections.Length == 0 || (isCorner && prefab.connections.Length != 2))
            { error = $"{label}: maps need sockets; a corner room needs exactly two sockets."; return false; }
            data = new ModuleData { prefab = prefab, sockets = new BaekhoSocketData[prefab.connections.Length] };
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < prefab.connections.Length; index++)
            {
                BaekhoMapConnection connection = prefab.connections[index];
                if (connection == null || !connection.transform.IsChildOf(prefab.transform)
                    || string.IsNullOrWhiteSpace(connection.id) || !ids.Add(connection.id))
                { error = $"{label}: every socket needs a unique ID and must be a child of the module."; return false; }
                for (Transform current = connection.transform; current != prefab.transform; current = current.parent)
                    if (!UnitScale(current.localScale)) { error = $"{label}/{connection.id}: socket ancestry must have unit scale."; return false; }
                BaekhoSocketData socket = prefab.GetSocket(index);
                Vector3 up = prefab.transform.InverseTransformDirection(connection.transform.up);
                if (!Finite(socket.position) || !Finite(socket.forward) || !Positive(socket.width) || !Positive(socket.height)
                    || Vector3.Dot(up, Vector3.up) < 0.9999f || Vector3.Dot(socket.forward, Forward(Direction(socket.forward))) < 0.9999f
                    || !OnGrid(socket.position.x, settings.gridSize) || !OnGrid(socket.position.z, settings.gridSize))
                { error = $"{label}/{connection.id}: sockets need finite dimensions, upright cardinal facing, and X/Z positions on the routing grid."; return false; }
                RectXZ rect = new RectXZ(bounds);
                if (socket.position.x < rect.minX - Epsilon || socket.position.x > rect.maxX + Epsilon
                    || socket.position.z < rect.minZ - Epsilon || socket.position.z > rect.maxZ + Epsilon)
                { error = $"{label}/{connection.id}: socket lies outside the authored geometry bounds."; return false; }
                data.sockets[index] = socket;
                if (socket.width + Epsilon >= settings.minimumDoorWidth) data.usable.Add(index);
            }
            if (data.usable.Count == 0 || (isCorner && data.usable.Count != 2))
            { error = $"{label}: no usable doors meet minimumDoorWidth, or a corner doorway is too narrow."; return false; }
            if (isCorner && (Mathf.Abs(Vector3.Dot(data.sockets[0].forward, data.sockets[1].forward)) > Epsilon
                || Mathf.Abs(data.sockets[0].position.y - data.sockets[1].position.y) > Epsilon))
            { error = $"{label}: corner sockets must be perpendicular and at the same floor height."; return false; }
            return true;
        }

        private static Vector3 SocketPosition(BaekhoPlacedModule room, ModuleData data, int index)
            => room.position + Rotate(data.sockets[index].position, room.quarterTurns);
        private static Vector3 SocketForward(BaekhoPlacedModule room, ModuleData data, int index)
            => Rotate(data.sockets[index].forward, room.quarterTurns);
        private static Vector3 Rotate(Vector3 value, int quarters)
        {
            switch (quarters & 3)
            {
                case 1: return new Vector3(value.z, value.y, -value.x);
                case 2: return new Vector3(-value.x, value.y, -value.z);
                case 3: return new Vector3(-value.z, value.y, value.x);
                default: return value;
            }
        }
        private static Vector3 Forward(int direction)
        {
            switch (direction & 3)
            {
                case 1: return Vector3.right;
                case 2: return Vector3.back;
                case 3: return Vector3.left;
                default: return Vector3.forward;
            }
        }
        private static int Direction(Vector3 forward) => Mathf.Abs(forward.x) > Mathf.Abs(forward.z)
            ? (forward.x >= 0 ? 1 : 3) : (forward.z >= 0 ? 0 : 2);
        private static float MinProjection(RectXZ bounds, Vector3 axis)
            => axis.x > 0.5f ? bounds.minX : axis.x < -0.5f ? -bounds.maxX : axis.z > 0.5f ? bounds.minZ : -bounds.maxZ;
        private static float MaxProjection(RectXZ bounds, Vector3 axis)
            => -MinProjection(bounds, -axis);
        private static float Manhattan(Vector3 a, Vector3 b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
        private static float SnapUp(float value, float grid) => Mathf.Ceil(value / grid) * grid;
        private static bool OnGrid(float value, float grid) => Mathf.Abs(value / grid - Mathf.Round(value / grid)) < Epsilon;
        private static bool UnitScale(Vector3 scale) => (scale - Vector3.one).sqrMagnitude < Epsilon * Epsilon;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Positive(float value) => Finite(value) && value > 0f;
    }
}
