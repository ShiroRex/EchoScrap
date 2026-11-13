using System;
using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Terrain-aware pursuit around static geometry. Walkability and search storage are shared by
    /// capsule size; only one bounded search runs at a time. Actors retain their route and wall side.
    /// No collections or physics result arrays are created while steering a crowd.
    /// </summary>
    public sealed class EnemyObstacleNavigator
    {
        private const float Skin = 0.06f;
        private const int ExpansionsPerFrame = 192;
        private const int MaximumSearchExpansions = 12000;
        private static readonly Dictionary<ShapeKey, Grid> grids = new Dictionary<ShapeKey, Grid>(4);
        private static readonly Collider[] overlaps = new Collider[24];
        private static readonly RaycastHit[] hits = new RaycastHit[24];
        private static Search search;
        private static SceneHandle contextScene = SceneHandle.None;
        private static EntityId contextSurface = EntityId.None;
        private static int budgetFrame = -1, usedExpansions;

        private readonly Vector3[] route = new Vector3[256];
        private int routeCount, routeIndex, wallSide;
        private float retryDelay, stalledTime;
        private Vector3 lastPosition, routeGoal;
        private bool hasPosition;
        private Grid grid;

        public void Reset()
        {
            if (search != null && search.Owner == this) search.Cancel();
            routeCount = routeIndex = wallSide = 0;
            retryDelay = stalledTime = 0f;
            hasPosition = false;
            grid = null;
        }

        public Vector3 Steer(EnemyActor actor, Vector3 desiredDirection, Vector3 goal,
            float lookAhead, float deltaTime)
        {
            if (desiredDirection.sqrMagnitude < 0.001f) return Vector3.zero;
            EnsureContext();
            ShapeKey key = new ShapeKey(actor.BodyCollider);
            if (grid == null || !grid.Key.Equals(key) || grid.Scene != contextScene || grid.Surface != contextSurface)
            {
                if (!grids.TryGetValue(key, out grid))
                {
                    grid = new Grid(key, contextScene, contextSurface);
                    grids.Add(key, grid);
                }
                routeCount = routeIndex = 0;
            }

            Vector3 position = actor.Body.position;
            retryDelay = Mathf.Max(0f, retryDelay - deltaTime);
            if (hasPosition && PlanarSqrDistance(position, lastPosition) < 0.0025f * deltaTime)
                stalledTime += deltaTime;
            else stalledTime = 0f;
            lastPosition = position;
            hasPosition = true;

            if (routeCount > 0 && PlanarSqrDistance(goal, routeGoal) > 16f)
                routeCount = routeIndex = 0;

            if (search != null && search.Owner == this)
                search.Advance();

            if (routeIndex < routeCount)
            {
                while (routeIndex < routeCount && PlanarSqrDistance(position, route[routeIndex]) < 0.18f * 0.18f)
                {
                    // Being close to a corner is insufficient: the capsule may still be behind
                    // the wall's end. Keep approaching until the outgoing edge is actually clear.
                    if (routeIndex + 1 < routeCount && !grid.ClearSegment(position, route[routeIndex + 1])) break;
                    routeIndex++;
                }
                // Smooth only across verified capsule sweeps, so diagonal shortcuts cannot cut a corner.
                int visible = routeIndex;
                for (int i = routeIndex + 1; i < Mathf.Min(routeCount, routeIndex + 9); i++)
                {
                    if (PlanarSqrDistance(position, route[i]) > 49f) break;
                    if (!grid.ClearSegment(position, route[i])) break;
                    visible = i;
                }
                routeIndex = visible;
                if (routeIndex < routeCount)
                {
                    Vector3 alongRoute = route[routeIndex] - position;
                    alongRoute.y = 0f;
                    if (grid.ClearSegment(position, position + alongRoute.normalized * Mathf.Min(lookAhead, alongRoute.magnitude)))
                    {
                        Vector3 pathDirection = alongRoute.normalized;
                        if (alongRoute.sqrMagnitude > 0.6f * 0.6f)
                        {
                            Vector3 lateral = Vector3.ProjectOnPlane(desiredDirection, pathDirection);
                            Vector3 separatedDirection = (pathDirection + lateral * 0.35f).normalized;
                            if (grid.ClearSegment(position, position + separatedDirection * lookAhead))
                                return separatedDirection;
                        }
                        return pathDirection;
                    }
                    routeCount = routeIndex = 0;
                }
            }

            Vector3 probeEnd = position + desiredDirection.normalized * Mathf.Max(lookAhead, key.Radius * 1.5f);
            if (grid.ClearSegment(position, probeEnd))
            {
                if (stalledTime < 0.65f) return desiredDirection;
            }

            if (retryDelay <= 0f && (search == null || search.Owner == null))
            {
                if (search == null) search = new Search();
                routeGoal = goal;
                search.Begin(this, grid, position, goal);
                search.Advance();
            }

            // Keep a chosen side while a route is pending. Frontal walls used to project pursuit
            // onto zero, and symmetric corners used to alternate directions every physics step.
            Vector3 forward = goal - position; forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = desiredDirection;
            forward.Normalize();
            if (wallSide == 0) wallSide = (actor.TargetId & 1) == 0 ? 1 : -1;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float angle = attempt < 3 ? (60f + 45f * attempt) * wallSide
                    : (60f + 45f * (attempt - 3)) * -wallSide;
                Vector3 candidate = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                if (!grid.ClearSegment(position, position + candidate * Mathf.Max(0.55f, key.Radius))) continue;
                if (attempt >= 3) wallSide = -wallSide;
                return candidate;
            }
            return Vector3.zero;
        }

        private static void EnsureContext()
        {
            SceneHandle scene = SceneManager.GetActiveScene().handle;
            EntityId surface = ArenaSurface.Active != null ? ArenaSurface.Active.GetEntityId() : EntityId.None;
            if (contextScene == scene && contextSurface == surface) return;
            grids.Clear();
            search?.Cancel();
            contextScene = scene;
            contextSurface = surface;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearSharedState()
        {
            grids.Clear(); search = null;
            contextScene = SceneHandle.None; contextSurface = EntityId.None; budgetFrame = -1; usedExpansions = 0;
        }

        private static float PlanarSqrDistance(Vector3 a, Vector3 b)
        { float x = a.x - b.x, z = a.z - b.z; return x * x + z * z; }

        private readonly struct ShapeKey : IEquatable<ShapeKey>
        {
            private readonly int radius, height, offset;
            public float Radius => radius * 0.025f;
            public float HalfHeight => height * 0.025f;
            public float CenterOffset => offset * 0.025f;
            public ShapeKey(CapsuleCollider capsule)
            {
                Vector3 scale = capsule.transform.lossyScale;
                radius = Mathf.CeilToInt((capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) + Skin) / 0.025f);
                height = Mathf.CeilToInt(capsule.height * Mathf.Abs(scale.y) * 0.5f / 0.025f);
                offset = Mathf.RoundToInt(capsule.center.y * scale.y / 0.025f);
            }
            public bool Equals(ShapeKey other) => radius == other.radius && height == other.height && offset == other.offset;
            public override bool Equals(object obj) => obj is ShapeKey other && Equals(other);
            public override int GetHashCode() => (radius * 397 ^ height) * 397 ^ offset;
        }

        private sealed class Grid
        {
            public readonly ShapeKey Key;
            public readonly SceneHandle Scene;
            public readonly EntityId Surface;
            public readonly int Width, Depth, Count;
            public readonly float Cell;
            private readonly float minimumX, minimumZ;
            private readonly sbyte[] occupancy;
            private readonly byte[] checkedEdges, blockedEdges;
            private readonly Collider ground;
            public Grid(ShapeKey key, SceneHandle scene, EntityId surface)
            {
                Key = key; Scene = scene; Surface = surface;
                ground = ArenaSurface.Active != null ? ArenaSurface.Active.GroundCollider : null;
                Bounds bounds = ground != null ? ground.bounds : new Bounds(Vector3.zero, new Vector3(256f, 1f, 256f));
                Cell = Mathf.Max(0.8f, Key.Radius * 1.2f);
                minimumX = bounds.min.x + Key.Radius; minimumZ = bounds.min.z + Key.Radius;
                Width = Mathf.Max(2, Mathf.FloorToInt((bounds.size.x - Key.Radius * 2f) / Cell) + 1);
                Depth = Mathf.Max(2, Mathf.FloorToInt((bounds.size.z - Key.Radius * 2f) / Cell) + 1);
                Count = Width * Depth;
                occupancy = new sbyte[Count]; checkedEdges = new byte[Count]; blockedEdges = new byte[Count];
            }
            public int Index(Vector3 position) => Mathf.Clamp(Mathf.RoundToInt((position.z - minimumZ) / Cell), 0, Depth - 1) * Width
                + Mathf.Clamp(Mathf.RoundToInt((position.x - minimumX) / Cell), 0, Width - 1);
            private Vector3 PlanarPosition(int index) =>
                new Vector3(minimumX + index % Width * Cell, 1f, minimumZ + index / Width * Cell);
            public Vector3 Position(int index) => Grounded(PlanarPosition(index));
            private Vector3 Grounded(Vector3 point)
            { point.y = ArenaSurface.SupportHeight(point, Key.Radius) + EnemySpawnGeometry.RootHeight; return point; }
            private void Capsule(Vector3 root, out Vector3 bottom, out Vector3 top)
            {
                Vector3 center = root + Vector3.up * Key.CenterOffset;
                float segment = Mathf.Max(0f, Key.HalfHeight - Key.Radius);
                bottom = center - Vector3.up * segment; top = center + Vector3.up * segment;
            }
            private bool IsObstacle(Collider collider)
            {
                if (collider == null || collider == ground) return false;
                // Flat sandbox floors have no ArenaSurface component. The capsule remains above them.
                return ground != null || collider.bounds.max.y > 0.08f;
            }
            public bool Walkable(int index)
            {
                if (occupancy[index] != 0) return occupancy[index] > 0;
                Capsule(Position(index), out Vector3 bottom, out Vector3 top);
                int count = Physics.OverlapCapsuleNonAlloc(bottom, top, Key.Radius, overlaps,
                    1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore);
                bool free = count < overlaps.Length;
                for (int i = 0; i < count && free; i++) free = !IsObstacle(overlaps[i]);
                occupancy[index] = free ? (sbyte)1 : (sbyte)-1;
                return free;
            }
            public bool ClearSegment(Vector3 start, Vector3 end)
            {
                // Long smoothed routes follow the sampled relief too. A single straight cast
                // between two valley endpoints would otherwise miss a structure on a hill.
                Vector3 planarDelta = end - start; planarDelta.y = 0f;
                int segments = Mathf.Max(1, Mathf.CeilToInt(planarDelta.magnitude / Cell));
                Vector3 previous = Grounded(start);
                for (int segment = 1; segment <= segments; segment++)
                {
                    Vector3 next = Grounded(Vector3.Lerp(start, end, segment / (float)segments));
                    if (!ClearGroundedSegment(previous, next)) return false;
                    previous = next;
                }
                return true;
            }
            private bool ClearGroundedSegment(Vector3 start, Vector3 end)
            {
                Capsule(start, out Vector3 bottom, out Vector3 top);
                Vector3 delta = end - start;
                float distance = delta.magnitude;
                if (distance < 0.001f) return true;
                int count = Physics.CapsuleCastNonAlloc(bottom, top, Key.Radius, delta / distance, hits,
                    distance, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore);
                if (count == hits.Length) return false;
                for (int i = 0; i < count; i++) if (IsObstacle(hits[i].collider)) return false;
                // A cast deliberately ignores its starting overlap; test the destination as well.
                Capsule(end, out bottom, out top);
                count = Physics.OverlapCapsuleNonAlloc(bottom, top, Key.Radius, overlaps,
                    1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore);
                if (count == overlaps.Length) return false;
                for (int i = 0; i < count; i++) if (IsObstacle(overlaps[i])) return false;
                return true;
            }
            public bool Edge(int from, int to, int direction)
            {
                int mask = 1 << direction;
                if ((checkedEdges[from] & mask) != 0) return (blockedEdges[from] & mask) == 0;
                bool clear = ClearSegment(PlanarPosition(from), PlanarPosition(to));
                checkedEdges[from] |= (byte)mask;
                if (!clear) blockedEdges[from] |= (byte)mask;
                return clear;
            }
            public int NearestWalkable(Vector3 point)
            {
                int center = Index(point), x = center % Width, z = center / Width;
                if (Walkable(center)) return center;
                for (int r = 1; r <= 5; r++)
                    for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;
                        int nx = x + dx, nz = z + dz;
                        if (nx >= 0 && nx < Width && nz >= 0 && nz < Depth && Walkable(nz * Width + nx))
                            return nz * Width + nx;
                    }
                return center;
            }
        }

        private sealed class Search
        {
            private static readonly int[] dx = { 1, 0, -1, 0, 1, -1, -1, 1 };
            private static readonly int[] dz = { 0, 1, 0, -1, 1, 1, -1, -1 };
            public EnemyObstacleNavigator Owner { get; private set; }
            private Grid map;
            private float[] cost, score;
            private int[] parent, seen, closed, heap, heapIndex, reverse;
            private int generation, heapCount, goal, expanded;

            public void Cancel() { Owner = null; heapCount = 0; }
            public void Begin(EnemyObstacleNavigator owner, Grid grid, Vector3 from, Vector3 destination)
            {
                Owner = owner; map = grid;
                if (cost == null || cost.Length < grid.Count)
                {
                    cost = new float[grid.Count]; score = new float[grid.Count]; parent = new int[grid.Count];
                    seen = new int[grid.Count]; closed = new int[grid.Count]; heap = new int[grid.Count];
                    heapIndex = new int[grid.Count]; reverse = new int[grid.Count]; generation = 0;
                }
                generation++; heapCount = expanded = 0;
                goal = map.NearestWalkable(destination);
                int start = map.Index(from);
                seen[start] = generation; cost[start] = 0f; parent[start] = -1;
                score[start] = Heuristic(start); Push(start);
            }
            private float Heuristic(int index)
            {
                int x = Mathf.Abs(index % map.Width - goal % map.Width), z = Mathf.Abs(index / map.Width - goal / map.Width);
                return Mathf.Max(x, z) + Mathf.Min(x, z) * 0.41421356f;
            }
            public void Advance()
            {
                if (Owner == null) return;
                if (budgetFrame != Time.frameCount) { budgetFrame = Time.frameCount; usedExpansions = 0; }
                while (heapCount > 0 && usedExpansions < ExpansionsPerFrame && expanded < MaximumSearchExpansions)
                {
                    int current = Pop();
                    if (current == goal) { Complete(current); return; }
                    closed[current] = generation; usedExpansions++; expanded++;
                    int x = current % map.Width, z = current / map.Width;
                    for (int direction = 0; direction < 8; direction++)
                    {
                        int nx = x + dx[direction], nz = z + dz[direction];
                        if (nx < 0 || nz < 0 || nx >= map.Width || nz >= map.Depth) continue;
                        int next = nz * map.Width + nx;
                        if (closed[next] == generation) continue;
                        float nextCost = cost[current] + (direction < 4 ? 1f : 1.41421356f);
                        if (seen[next] == generation && nextCost >= cost[next]) continue;
                        if (!map.Walkable(next)) continue;
                        if (direction >= 4 && (!map.Walkable(z * map.Width + nx) || !map.Walkable(nz * map.Width + x))) continue;
                        if (!map.Edge(current, next, direction)) continue;
                        bool existing = seen[next] == generation;
                        seen[next] = generation; cost[next] = nextCost; score[next] = nextCost + Heuristic(next); parent[next] = current;
                        if (existing) Up(heapIndex[next]); else Push(next);
                    }
                }
                if (heapCount == 0 || expanded >= MaximumSearchExpansions)
                {
                    Owner.retryDelay = 1f;
                    Cancel();
                }
            }
            private void Complete(int end)
            {
                int count = 0;
                while (end >= 0 && count < reverse.Length) { reverse[count++] = end; end = parent[end]; }
                Owner.routeCount = Owner.routeIndex = 0;
                // The first edge connects the actor's exact current position to the snapped grid.
                int previousDirection = int.MinValue;
                for (int i = count - 1; i >= 0 && Owner.routeCount < Owner.route.Length; i--)
                {
                    int direction = i > 0 ? reverse[i - 1] - reverse[i] : int.MinValue;
                    if (i == count - 1 || i == 0 || direction != previousDirection)
                        Owner.route[Owner.routeCount++] = map.Position(reverse[i]);
                    previousDirection = direction;
                }
                Owner.retryDelay = 0.25f; Owner.stalledTime = 0f;
                Cancel();
            }
            private void Push(int node) { heap[heapCount] = node; heapIndex[node] = heapCount; Up(heapCount++); }
            private void Up(int index)
            {
                while (index > 0)
                {
                    int above = (index - 1) / 2;
                    if (score[heap[above]] <= score[heap[index]]) break;
                    Swap(index, above); index = above;
                }
            }
            private int Pop()
            {
                int first = heap[0]; heapCount--;
                if (heapCount == 0) return first;
                heap[0] = heap[heapCount]; heapIndex[heap[0]] = 0;
                int index = 0;
                while (index * 2 + 1 < heapCount)
                {
                    int child = index * 2 + 1;
                    if (child + 1 < heapCount && score[heap[child + 1]] < score[heap[child]]) child++;
                    if (score[heap[index]] <= score[heap[child]]) break;
                    Swap(index, child); index = child;
                }
                return first;
            }
            private void Swap(int a, int b)
            {
                int node = heap[a]; heap[a] = heap[b]; heap[b] = node;
                heapIndex[heap[a]] = a; heapIndex[heap[b]] = b;
            }
        }
    }
}
