using System;
using System.Collections.Generic;
using System.Diagnostics;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Terrain-aware pursuit around static geometry with a hard per-frame physics-query budget
    /// (shared across FixedUpdate calls via Time.frameCount), resumable segment/grid/A* operations,
    /// persistent round-robin turn scheduling, single-candidate shortcut smoothing, and strict
    /// verified-stretch fallback safety.
    /// </summary>
    public sealed class EnemyObstacleNavigator
    {
        public const int TotalQueriesPerFrame = 1024;
        public const int ImmediateReservedPerFrame = 768;
        public const int PlanningReservedPerFrame = 256;

        public struct NavigationTelemetry
        {
            public long OverlapBoxQueries;
            public long CapsuleCastQueries;
            public long OverlapCapsuleQueries;
            public long TotalQueries => OverlapBoxQueries + CapsuleCastQueries + OverlapCapsuleQueries;
            public int PendingRequests;
            public double OldestPendingRequestAgeMs;
            public double MaxRequestAgeMs;
            public double TotalNavigationMs;
            public int CurrentFrameTotalQueries;
            public int CurrentFrameImmediateQueries;
            public int CurrentFramePlanningQueries;
            public long BudgetExhaustedEvents;
        }

        private enum QueryResult : byte
        {
            Clear = 0,
            Blocked = 1,
            Pending = 2
        }

        private enum BudgetChannel : byte
        {
            Immediate = 0,
            Planning = 1
        }

        private enum SegmentStage : byte
        {
            OverlapBox = 0,
            CapsuleCast = 1,
            OverlapCapsule = 2
        }

        private enum NeighborStage : byte
        {
            WalkTarget = 0,
            WalkCornerA = 1,
            WalkCornerB = 2,
            EdgeCheck = 3
        }

        private struct ResumableSegmentCheck
        {
            public bool Active;
            public Vector3 Start;
            public Vector3 End;
            public ShapeKey Shape;
            public int EnvVersion;
            public int SegmentCount;
            public int CurrentSegment;
            public int CompletedSegments;
            public SegmentStage Stage;
            public Vector3 PreviousGrounded;
            public Vector3 NextGrounded;
            public bool HasSubsegmentPoints;

            public void Reset()
            {
                Active = false;
                HasSubsegmentPoints = false;
                CompletedSegments = 0;
                CurrentSegment = 0;
                SegmentCount = 0;
                Stage = SegmentStage.OverlapBox;
            }

            public bool Matches(Vector3 start, Vector3 end, ShapeKey shape, int envVersion, float maxEndpointDriftSqr = 0.0016f)
            {
                return Active
                    && EnvVersion == envVersion
                    && Shape.Equals(shape)
                    && PlanarSqrDistance(Start, start) <= maxEndpointDriftSqr
                    && PlanarSqrDistance(End, end) <= maxEndpointDriftSqr;
            }

            public void Begin(Grid grid, Vector3 start, Vector3 end, int envVersion)
            {
                Active = true;
                Start = start;
                End = end;
                Shape = grid.Key;
                EnvVersion = envVersion;
                Vector3 planarDelta = end - start;
                planarDelta.y = 0f;
                SegmentCount = Mathf.Max(1, Mathf.CeilToInt(planarDelta.magnitude / grid.Cell));
                CurrentSegment = 1;
                CompletedSegments = 0;
                Stage = SegmentStage.OverlapBox;
                PreviousGrounded = grid.Grounded(start);
                HasSubsegmentPoints = false;
            }

            public QueryResult Step(Grid grid, BudgetChannel channel)
            {
                while (CurrentSegment <= SegmentCount)
                {
                    if (!HasSubsegmentPoints)
                    {
                        NextGrounded = grid.Grounded(Vector3.Lerp(Start, End, CurrentSegment / (float)SegmentCount));
                        Stage = SegmentStage.OverlapBox;
                        HasSubsegmentPoints = true;
                    }

                    Vector3 delta = NextGrounded - PreviousGrounded;
                    float distance = delta.magnitude;
                    if (distance < 0.001f)
                    {
                        AdvanceSubsegment();
                        continue;
                    }

                    grid.Capsule(PreviousGrounded, out Vector3 bottom, out Vector3 top);
                    Vector3 endBottom = bottom + delta;
                    Vector3 endTop = top + delta;

                    if (Stage == SegmentStage.OverlapBox)
                    {
                        Vector3 padding = Vector3.one * grid.Key.Radius;
                        Vector3 min = Vector3.Min(bottom, endBottom) - padding;
                        Vector3 max = Vector3.Max(top, endTop) + padding;
                        if (!grid.QueryOverlapBox((min + max) * 0.5f, (max - min) * 0.5f, channel, out int boxCount))
                            return QueryResult.Pending;

                        bool needsSweep = boxCount >= overlaps.Length;
                        for (int i = 0; i < boxCount && !needsSweep; i++)
                            needsSweep = grid.IsObstacle(overlaps[i]);

                        if (!needsSweep)
                        {
                            AdvanceSubsegment();
                            continue;
                        }

                        Stage = SegmentStage.CapsuleCast;
                    }

                    if (Stage == SegmentStage.CapsuleCast)
                    {
                        if (!grid.QueryCapsuleCast(bottom, top, grid.Key.Radius, delta / distance, distance, channel, out int castCount))
                            return QueryResult.Pending;

                        if (castCount >= hits.Length)
                        {
                            Active = false;
                            return QueryResult.Blocked;
                        }

                        for (int i = 0; i < castCount; i++)
                        {
                            if (grid.IsObstacle(hits[i].collider))
                            {
                                Active = false;
                                return QueryResult.Blocked;
                            }
                        }

                        Stage = SegmentStage.OverlapCapsule;
                    }

                    if (Stage == SegmentStage.OverlapCapsule)
                    {
                        if (!grid.QueryOverlapCapsule(endBottom, endTop, grid.Key.Radius, channel, out int capCount))
                            return QueryResult.Pending;

                        if (capCount >= overlaps.Length)
                        {
                            Active = false;
                            return QueryResult.Blocked;
                        }

                        for (int i = 0; i < capCount; i++)
                        {
                            if (grid.IsObstacle(overlaps[i]))
                            {
                                Active = false;
                                return QueryResult.Blocked;
                            }
                        }

                        AdvanceSubsegment();
                    }
                }

                Active = false;
                return QueryResult.Clear;
            }

            private void AdvanceSubsegment()
            {
                CompletedSegments++;
                CurrentSegment++;
                PreviousGrounded = NextGrounded;
                HasSubsegmentPoints = false;
                Stage = SegmentStage.OverlapBox;
            }
        }

        private const float Skin = 0.06f;
        private const int MaximumSearchExpansions = 12000;
        private const int MaxSearchCpuExpansionsPerTurn = 512;
        private const float SafeStepMargin = 0.14f;
        private const float MaxSafeLateralOffset = Skin * 0.5f;

        private static readonly Dictionary<ShapeKey, Grid> grids = new Dictionary<ShapeKey, Grid>(4);
        private static readonly Collider[] overlaps = new Collider[24];
        private static readonly RaycastHit[] hits = new RaycastHit[24];
        private static readonly List<EnemyObstacleNavigator> registered = new List<EnemyObstacleNavigator>(256);

        private static Search search;
        private static SceneHandle contextScene = SceneHandle.None;
        private static EntityId contextSurface = EntityId.None;
        private static int obstacleVersion;
        private static int contextObstacleVersion;
        private static int environmentGeneration = 1;

        private static int budgetFrame = -1;
        private static int frameImmediateUsed;
        private static int framePlanningUsed;
        private static bool allowPlanningSpareQuota;
        private static bool immediateExhaustedInStep;

        private static int planningDemandNavCount;
        private static int currentPhysicsStepId;
        private static int immediateCursor;
        private static int planningCursor;
        private static int shortcutCursor;

        private static long overlapBoxQueries, capsuleCastQueries, overlapCapsuleQueries, totalNavigationTicks;
        private static long budgetExhaustedEvents;
        private static long searchStartedTick;
        private static double maxObservedRequestAgeMs;
        private static readonly double ticksToMs = 1000.0 / Stopwatch.Frequency;

        // Private route fields keep their exact names/types for EnemyNavigationTests reflection diagnostics.
        private readonly Vector3[] route = new Vector3[256];
        private int routeCount, routeIndex, wallSide;
        private float retryDelay, stalledTime;
        private Vector3 lastPosition, routeGoal;
        private bool hasPosition;
        private Grid grid;

        private int registeredIndex = -1;
        private EnemyActor boundActor;
        private SceneHandle actorSceneHandle;

        // Latest actor intent for coordinator turn scheduling.
        private bool hasIntent;
        private int intentStepId = -1;
        private Vector3 intentDesiredDir;
        private Vector3 intentGoal;
        private float intentLookAhead;
        private float intentDeltaTime;

        // Pre-resolved immediate evaluation result for the current physics step.
        private bool resolvedFromCoordinator;
        private int resolvedStepId = -1;
        private int resolvedEnvVersion = -1;
        private Vector3 resolvedPosition;
        private Vector3 resolvedDesiredDir;
        private Vector3 resolvedDirection;
        private QueryResult resolvedStatus;

        // Resumable immediate probes (route transition, route step, separation, direct, wall-side).
        private ResumableSegmentCheck immediateCornerCheck;
        private ResumableSegmentCheck immediateRouteStepCheck;
        private ResumableSegmentCheck immediateSepCheck;
        private ResumableSegmentCheck immediateDirectCheck;
        private ResumableSegmentCheck immediateWallCheck;
        private int pendingWallAttempt;
        private Vector3 pendingWallOrigin;
        private Vector3 pendingWallForward;

        // Verified obstacle-free corridor fallback for Safe Step / Pending safety.
        private bool verifiedValid;
        private Vector3 verifiedStart;
        private Vector3 verifiedEnd;
        private Vector3 verifiedDir;
        private float verifiedLength;
        private ShapeKey verifiedShape;
        private int verifiedEnvVersion;

        // Queued route search state.
        private bool routeRequested;
        private Vector3 routeRequestFrom;
        private Vector3 routeRequestGoal;
        private long routeRequestStartTick;
        private long pendingImmediateStartTick;

        // Optional single-candidate shortcut smoothing state.
        private bool shortcutActive;
        private bool countedInPlanningDemand;
        private int shortcutTargetIndex;
        private Vector3 shortcutOrigin;
        private ResumableSegmentCheck shortcutCheck;
        private int shortcutBlockedAtRouteIndex = -1;
        private int shortcutRetryAfterStep = -1;

        public static int ObstacleVersion => obstacleVersion;
        public static int ActiveNavigatorCount => registered.Count;

        public static void InvalidateStaticObstacles()
        {
            obstacleVersion++;
        }

        public static void ResetTelemetry()
        {
            overlapBoxQueries = 0;
            capsuleCastQueries = 0;
            overlapCapsuleQueries = 0;
            totalNavigationTicks = 0;
            budgetExhaustedEvents = 0;
            maxObservedRequestAgeMs = 0.0;

            long now = Stopwatch.GetTimestamp();
            searchStartedTick = (search != null && search.Owner != null) ? now : 0;
            for (int i = 0; i < registered.Count; i++)
            {
                EnemyObstacleNavigator nav = registered[i];
                if (nav.routeRequested)
                    nav.routeRequestStartTick = now;
                if (nav.pendingImmediateStartTick != 0)
                    nav.pendingImmediateStartTick = now;
            }
        }

        public static NavigationTelemetry CaptureTelemetry()
        {
            RefreshBudgetFrameIfNeeded();
            long now = Stopwatch.GetTimestamp();
            int pendingCount = 0;
            double oldestPendingMs = 0.0;

            if (search != null && search.Owner != null)
            {
                pendingCount++;
                if (searchStartedTick != 0)
                {
                    double age = Math.Max(0.0, (now - searchStartedTick) * ticksToMs);
                    if (age > oldestPendingMs) oldestPendingMs = age;
                }
            }

            for (int i = 0; i < registered.Count; i++)
            {
                EnemyObstacleNavigator nav = registered[i];
                if (nav.routeRequested && (search == null || search.Owner != nav))
                {
                    pendingCount++;
                    if (nav.routeRequestStartTick != 0)
                    {
                        double age = Math.Max(0.0, (now - nav.routeRequestStartTick) * ticksToMs);
                        if (age > oldestPendingMs) oldestPendingMs = age;
                    }
                }
                if (nav.pendingImmediateStartTick != 0)
                {
                    pendingCount++;
                    double age = Math.Max(0.0, (now - nav.pendingImmediateStartTick) * ticksToMs);
                    if (age > oldestPendingMs) oldestPendingMs = age;
                }
                if (nav.shortcutActive)
                {
                    pendingCount++;
                }
            }

            return new NavigationTelemetry
            {
                OverlapBoxQueries = overlapBoxQueries,
                CapsuleCastQueries = capsuleCastQueries,
                OverlapCapsuleQueries = overlapCapsuleQueries,
                PendingRequests = pendingCount,
                OldestPendingRequestAgeMs = oldestPendingMs,
                MaxRequestAgeMs = Math.Max(maxObservedRequestAgeMs, oldestPendingMs),
                TotalNavigationMs = totalNavigationTicks * ticksToMs,
                CurrentFrameTotalQueries = frameImmediateUsed + framePlanningUsed,
                CurrentFrameImmediateQueries = frameImmediateUsed,
                CurrentFramePlanningQueries = framePlanningUsed,
                BudgetExhaustedEvents = budgetExhaustedEvents
            };
        }

        public void Reset()
        {
            if (search != null && search.Owner == this)
                search.Cancel();

            ClearRouteRequest(markObserved: false);
            ClearPendingImmediateTimestamp();
            ClearImmediateResumables();
            ClearPendingShortcut();
            InvalidateVerifiedSegment();

            routeCount = routeIndex = wallSide = 0;
            routeGoal = Vector3.zero;
            retryDelay = stalledTime = 0f;
            hasPosition = false;
            hasIntent = false;
            intentStepId = -1;
            resolvedFromCoordinator = false;
            resolvedStepId = -1;
            resolvedEnvVersion = -1;
            grid = null;
            boundActor = null;
            actorSceneHandle = SceneHandle.None;

            Unregister(this);
        }

        public Vector3 Steer(EnemyActor actor, Vector3 desiredDirection, Vector3 goal,
            float lookAhead, float deltaTime)
        {
            long startTick = Stopwatch.GetTimestamp();
            try
            {
                EnemyNavigationCoordinator.EnsureExists();
                return SteerInternal(actor, desiredDirection, goal, lookAhead, deltaTime);
            }
            finally
            {
                totalNavigationTicks += Stopwatch.GetTimestamp() - startTick;
            }
        }

        internal static void StepCoordinatorPhysics()
        {
            long startTick = Stopwatch.GetTimestamp();
            try
            {
                EnsureContext();
                RefreshBudgetFrameIfNeeded();
                currentPhysicsStepId++;
                immediateExhaustedInStep = false;
                allowPlanningSpareQuota = false;

                PruneInvalidNavigators();

                // 1. Process Immediate probes round-robin BEFORE EnemyActor FixedUpdate runs.
                bool stoppedOnBudget = RunImmediatePass();

                // 2. Process Planning within its protected 256-query reserve during FixedUpdate.
                ProcessPlanningTurn(allowSpare: false);

                // 3. If Immediate paused to protect Planning's reserve and Planning finished early
                // leaving unused budget in this step, consume leftover spare quota immediately.
                if (stoppedOnBudget && !HasAnyPlanningDemand() && !IsImmediateBudgetExhausted())
                {
                    immediateExhaustedInStep = false;
                    RunImmediatePass();
                }
            }
            finally
            {
                totalNavigationTicks += Stopwatch.GetTimestamp() - startTick;
            }
        }

        private static bool RunImmediatePass()
        {
            int count = registered.Count;
            if (count == 0)
                return false;

            immediateCursor %= count;
            int start = immediateCursor;
            bool stoppedOnBudget = false;
            for (int offset = 0; offset < count; offset++)
            {
                int idx = (start + offset) % count;
                EnemyObstacleNavigator nav = registered[idx];
                if (!nav.ShouldCoordinatorPreResolve())
                    continue;

                QueryResult res = nav.PreResolveImmediate(currentPhysicsStepId);
                if (res == QueryResult.Pending && IsImmediateBudgetExhausted())
                {
                    immediateCursor = idx;
                    immediateExhaustedInStep = true;
                    stoppedOnBudget = true;
                    break;
                }
            }

            if (!stoppedOnBudget)
                immediateCursor = (start + 1) % count;
            return stoppedOnBudget;
        }

        internal static void StepCoordinatorUpdate()
        {
            long startTick = Stopwatch.GetTimestamp();
            try
            {
                EnsureContext();
                RefreshBudgetFrameIfNeeded();
                PruneInvalidNavigators();

                // All FixedUpdate steps for this Time.frameCount have finished; remaining budget up to
                // 1024 is spare quota that Planning (A* & optional shortcut smoothing) can safely consume.
                ProcessPlanningTurn(allowSpare: true);
            }
            finally
            {
                totalNavigationTicks += Stopwatch.GetTimestamp() - startTick;
            }
        }

        internal static void HandleSceneUnloaded(Scene unloadedScene)
        {
            SceneHandle handle = unloadedScene.handle;
            if (search != null && search.Owner != null && search.Owner.actorSceneHandle == handle)
                search.Cancel();

            for (int i = registered.Count - 1; i >= 0; i--)
            {
                EnemyObstacleNavigator nav = registered[i];
                if (nav.boundActor == null || nav.actorSceneHandle == handle ||
                    nav.boundActor.gameObject.scene.handle == handle)
                {
                    nav.Reset();
                }
            }
        }

        internal static void HandleActiveSceneChanged(Scene previous, Scene current)
        {
            EnsureContext();
        }

        private Vector3 SteerInternal(EnemyActor actor, Vector3 desiredDirection, Vector3 goal,
            float lookAhead, float deltaTime)
        {
            if (actor == null || actor.Body == null || actor.BodyCollider == null)
                return Vector3.zero;

            if (desiredDirection.sqrMagnitude < 0.001f)
                return Vector3.zero;

            EnsureContext();
            ShapeKey key = new ShapeKey(actor.BodyCollider);
            BindGridAndActor(actor, key);
            Register(this);

            Vector3 position = actor.Body.position;
            retryDelay = Mathf.Max(0f, retryDelay - deltaTime);
            if (hasPosition && PlanarSqrDistance(position, lastPosition) < 0.0025f * deltaTime)
                stalledTime += deltaTime;
            else
                stalledTime = 0f;
            lastPosition = position;
            hasPosition = true;

            if (routeCount > 0 && PlanarSqrDistance(goal, routeGoal) > 16f)
            {
                routeCount = routeIndex = 0;
                ClearPendingShortcut();
            }

            Vector3 normalizedDesired = desiredDirection.normalized;
            RecordIntent(normalizedDesired, goal, lookAhead, deltaTime);

            // Consume coordinator's result ONLY if it belongs to this exact physics step, environment,
            // current body position, and compatible steering intent/route.
            if (TryConsumeCoordinatorResult(position, normalizedDesired, lookAhead, key, out Vector3 coordinatedDir))
                return coordinatedDir;

            // If Coordinator already exhausted the Immediate budget in this physics step, do not jump
            // the round-robin queue; honor strict verified-stretch fallback or wait at zero velocity.
            if (immediateExhaustedInStep)
            {
                MarkPendingImmediateTimestamp();
                return SafeFallbackOrZero(position, normalizedDesired, key);
            }

            QueryResult directEval = EvaluateImmediate(actor, position, normalizedDesired, goal, lookAhead, out Vector3 evaluatedDir);
            resolvedFromCoordinator = false;
            resolvedStepId = currentPhysicsStepId;
            resolvedEnvVersion = environmentGeneration;
            resolvedPosition = position;
            resolvedDesiredDir = normalizedDesired;
            resolvedDirection = evaluatedDir;
            resolvedStatus = directEval;

            if (directEval == QueryResult.Pending)
            {
                MarkPendingImmediateTimestamp();
                return SafeFallbackOrZero(position, normalizedDesired, key);
            }

            ClearPendingImmediateTimestamp();
            return evaluatedDir;
        }

        private void BindGridAndActor(EnemyActor actor, ShapeKey key)
        {
            boundActor = actor;
            actorSceneHandle = actor.gameObject.scene.handle;
            if (grid == null || !grid.Key.Equals(key) || grid.Scene != contextScene ||
                grid.Surface != contextSurface || grid.ObstacleGen != contextObstacleVersion)
            {
                if (!grids.TryGetValue(key, out grid))
                {
                    grid = new Grid(key, contextScene, contextSurface, contextObstacleVersion);
                    grids.Add(key, grid);
                }
                else
                {
                    grid.EnsureValid(contextScene, contextSurface, contextObstacleVersion);
                }
                routeCount = routeIndex = 0;
                ClearImmediateResumables();
                ClearPendingShortcut();
                InvalidateVerifiedSegment();
            }
        }

        private void RecordIntent(Vector3 normalizedDesired, Vector3 goal, float lookAhead, float deltaTime)
        {
            hasIntent = true;
            intentStepId = currentPhysicsStepId;
            intentDesiredDir = normalizedDesired;
            intentGoal = goal;
            intentLookAhead = lookAhead;
            intentDeltaTime = deltaTime;
        }

        private bool ShouldCoordinatorPreResolve()
        {
            if (!hasIntent || boundActor == null || boundActor.Body == null || boundActor.BodyCollider == null)
                return false;
            if (!boundActor.gameObject.activeInHierarchy || boundActor.IsDead ||
                boundActor.SimulationSuspended || boundActor.IsControlLocked)
                return false;
            if (boundActor.Session != null && (boundActor.Session.IsPaused || !boundActor.Session.IsGameplayFlowActive()))
                return false;
            if (currentPhysicsStepId - intentStepId > 1)
                return false;
            return true;
        }

        private QueryResult PreResolveImmediate(int stepId)
        {
            ShapeKey key = new ShapeKey(boundActor.BodyCollider);
            BindGridAndActor(boundActor, key);

            Vector3 position = boundActor.Body.position;
            if (routeCount > 0 && PlanarSqrDistance(intentGoal, routeGoal) > 16f)
            {
                routeCount = routeIndex = 0;
                ClearPendingShortcut();
            }

            QueryResult res = EvaluateImmediate(boundActor, position, intentDesiredDir, intentGoal, intentLookAhead, out Vector3 outDir);
            resolvedFromCoordinator = true;
            resolvedStepId = stepId;
            resolvedEnvVersion = environmentGeneration;
            resolvedPosition = position;
            resolvedDesiredDir = intentDesiredDir;
            resolvedDirection = outDir;
            resolvedStatus = res;

            if (res == QueryResult.Pending)
                MarkPendingImmediateTimestamp();
            else
                ClearPendingImmediateTimestamp();

            return res;
        }

        private bool TryConsumeCoordinatorResult(Vector3 position, Vector3 normalizedDesired,
            float lookAhead, ShapeKey key, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!resolvedFromCoordinator || resolvedStepId != currentPhysicsStepId ||
                resolvedEnvVersion != environmentGeneration)
                return false;

            if (PlanarSqrDistance(resolvedPosition, position) > 0.04f * 0.04f)
                return false;

            if (resolvedStatus == QueryResult.Pending)
            {
                direction = SafeFallbackOrZero(position, normalizedDesired, key);
                return true;
            }

            // Route movement follows verified route edges regardless of small local separation deltas.
            if (routeIndex < routeCount)
            {
                direction = resolvedDirection;
                return true;
            }

            // Direct/wall-side movement: require close desired direction agreement (~16 deg) so sharp
            // target flips or test injections re-verify rather than moving along a stale angle.
            if (Vector3.Dot(normalizedDesired, resolvedDesiredDir) >= 0.96f)
            {
                direction = resolvedDirection;
                return true;
            }

            return false;
        }

        private QueryResult EvaluateImmediate(
            EnemyActor actor,
            Vector3 position,
            Vector3 normalizedDesired,
            Vector3 goal,
            float lookAhead,
            out Vector3 outputDirection)
        {
            outputDirection = Vector3.zero;

            // 1. Active A* route following (smoothing 8-candidate loop removed; runs 1 optional shortcut on Planning).
            if (routeIndex < routeCount)
            {
                while (routeIndex < routeCount && PlanarSqrDistance(position, route[routeIndex]) < 0.18f * 0.18f)
                {
                    if (routeIndex + 1 < routeCount)
                    {
                        QueryResult cornerCheck = ClearSegmentResumable(
                            ref immediateCornerCheck, grid, position, route[routeIndex + 1], BudgetChannel.Immediate);
                        if (cornerCheck == QueryResult.Pending)
                        {
                            // Wait safely rather than skipping past an unverified corner.
                            if (PlanarSqrDistance(position, route[routeIndex]) < 0.02f * 0.02f)
                                return QueryResult.Pending;
                            break;
                        }
                        if (cornerCheck == QueryResult.Blocked)
                            break;
                    }
                    routeIndex++;
                    shortcutBlockedAtRouteIndex = -1;
                }

                if (routeIndex < routeCount)
                {
                    Vector3 alongRoute = route[routeIndex] - position;
                    alongRoute.y = 0f;
                    float alongDist = alongRoute.magnitude;
                    if (alongDist > 0.001f)
                    {
                        Vector3 pathDirection = alongRoute / alongDist;
                        float segLen = Mathf.Min(lookAhead, alongDist);
                        Vector3 segEnd = position + pathDirection * segLen;
                        QueryResult segRes = ClearSegmentResumable(
                            ref immediateRouteStepCheck, grid, position, segEnd, BudgetChannel.Immediate);

                        if (segRes == QueryResult.Pending)
                        {
                            RecordPartialSubsegment(ref immediateRouteStepCheck, pathDirection, grid.Key);
                            return QueryResult.Pending;
                        }

                        if (segRes == QueryResult.Clear)
                        {
                            RecordVerifiedSegment(position, segEnd, pathDirection, segLen, grid.Key);
                            if (alongDist > 0.6f)
                            {
                                Vector3 lateral = Vector3.ProjectOnPlane(normalizedDesired, pathDirection);
                                Vector3 separated = pathDirection + lateral * 0.35f;
                                separated.y = 0f;
                                if (separated.sqrMagnitude > 0.001f)
                                {
                                    separated.Normalize();
                                    Vector3 sepEnd = position + separated * lookAhead;
                                    QueryResult sepRes = ClearSegmentResumable(
                                        ref immediateSepCheck, grid, position, sepEnd, BudgetChannel.Immediate);
                                    if (sepRes == QueryResult.Clear)
                                    {
                                        RecordVerifiedSegment(position, sepEnd, separated, lookAhead, grid.Key);
                                        outputDirection = separated;
                                        return QueryResult.Clear;
                                    }
                                }
                            }

                            outputDirection = pathDirection;
                            return QueryResult.Clear;
                        }
                    }

                    routeCount = routeIndex = 0;
                    ClearPendingShortcut();
                    InvalidateVerifiedSegment();
                }
            }

            // 2. Direct probe toward desiredDirection.
            float probeLen = Mathf.Max(lookAhead, grid.Key.Radius * 1.5f);
            Vector3 probeEnd = position + normalizedDesired * probeLen;
            QueryResult directRes = ClearSegmentResumable(
                ref immediateDirectCheck, grid, position, probeEnd, BudgetChannel.Immediate);

            if (directRes == QueryResult.Pending)
            {
                RecordPartialSubsegment(ref immediateDirectCheck, normalizedDesired, grid.Key);
                return QueryResult.Pending;
            }

            if (directRes == QueryResult.Clear)
            {
                RecordVerifiedSegment(position, probeEnd, normalizedDesired, probeLen, grid.Key);
                if (stalledTime < 0.65f)
                {
                    pendingWallAttempt = 0;
                    immediateWallCheck.Reset();
                    outputDirection = normalizedDesired;
                    return QueryResult.Clear;
                }
            }
            else
            {
                // Blocked straight ahead: invalidate forward verified stretch before trying wall-side probes.
                InvalidateVerifiedSegment();
            }

            // 3. Queue A* route request asynchronously on Planning queue (never monopolizes global search).
            if (retryDelay <= 0f && !routeRequested && (search == null || search.Owner != this))
                EnqueueRouteRequest(position, goal);

            // 4. Resumable wall-side immediate probing while waiting for A*.
            Vector3 forward = goal - position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = normalizedDesired;
            forward.Normalize();

            if (wallSide == 0)
                wallSide = (actor.TargetId & 1) == 0 ? 1 : -1;

            if (PlanarSqrDistance(pendingWallOrigin, position) > 0.04f * 0.04f ||
                Vector3.Dot(pendingWallForward, forward) < 0.96f)
            {
                pendingWallAttempt = 0;
                pendingWallOrigin = position;
                pendingWallForward = forward;
                immediateWallCheck.Reset();
            }

            float sideDist = Mathf.Max(0.55f, grid.Key.Radius);
            for (int attempt = pendingWallAttempt; attempt < 6; attempt++)
            {
                float angle = attempt < 3
                    ? (60f + 45f * attempt) * wallSide
                    : (60f + 45f * (attempt - 3)) * -wallSide;
                Vector3 candidate = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                QueryResult wallRes = ClearSegmentResumable(
                    ref immediateWallCheck, grid, position, position + candidate * sideDist, BudgetChannel.Immediate);

                if (wallRes == QueryResult.Pending)
                {
                    pendingWallAttempt = attempt;
                    return QueryResult.Pending;
                }

                if (wallRes == QueryResult.Clear)
                {
                    if (attempt >= 3)
                        wallSide = -wallSide;
                    pendingWallAttempt = 0;
                    RecordVerifiedSegment(position, position + candidate * sideDist, candidate, sideDist, grid.Key);
                    outputDirection = candidate;
                    return QueryResult.Clear;
                }

                pendingWallAttempt = attempt + 1;
            }

            pendingWallAttempt = 0;
            outputDirection = Vector3.zero;
            return QueryResult.Blocked;
        }

        private Vector3 SafeFallbackOrZero(Vector3 position, Vector3 requestedDir, ShapeKey key)
        {
            if (TryFollowVerifiedSegment(position, requestedDir, key, out Vector3 safeDir))
                return safeDir;
            return Vector3.zero;
        }

        private void RecordVerifiedSegment(Vector3 start, Vector3 end, Vector3 dir, float length, ShapeKey shape)
        {
            if (length <= SafeStepMargin)
                return;
            verifiedValid = true;
            verifiedStart = start;
            verifiedEnd = end;
            verifiedDir = dir;
            verifiedLength = length;
            verifiedShape = shape;
            verifiedEnvVersion = environmentGeneration;
        }

        private void RecordPartialSubsegment(ref ResumableSegmentCheck check, Vector3 dir, ShapeKey shape)
        {
            if (!check.Active || check.CompletedSegments <= 0)
                return;
            Vector3 partialEnd = check.PreviousGrounded;
            Vector3 delta = partialEnd - check.Start;
            delta.y = 0f;
            float len = delta.magnitude;
            if (len > SafeStepMargin)
                RecordVerifiedSegment(check.Start, partialEnd, dir, len, shape);
        }

        private void InvalidateVerifiedSegment()
        {
            verifiedValid = false;
        }

        private bool TryFollowVerifiedSegment(Vector3 position, Vector3 requestedDir, ShapeKey key, out Vector3 safeDirection)
        {
            safeDirection = Vector3.zero;
            if (!verifiedValid || verifiedEnvVersion != environmentGeneration || !verifiedShape.Equals(key))
                return false;

            // Ensure verified corridor points generally forward relative to current goal/route intention.
            Vector3 intendedDir = requestedDir;
            if (routeIndex < routeCount)
            {
                Vector3 toRoute = route[routeIndex] - position;
                toRoute.y = 0f;
                if (toRoute.sqrMagnitude > 0.001f)
                    intendedDir = toRoute.normalized;
            }
            if (Vector3.Dot(verifiedDir, intendedDir) <= 0.25f)
                return false;

            Vector3 delta = position - verifiedStart;
            delta.y = 0f;
            float along = delta.x * verifiedDir.x + delta.z * verifiedDir.z;
            if (along < -0.04f)
                return false;

            float lateralSqr = Mathf.Max(0f, delta.sqrMagnitude - along * along);
            if (lateralSqr > MaxSafeLateralOffset * MaxSafeLateralOffset)
                return false;

            float remaining = verifiedLength - along;
            if (remaining <= SafeStepMargin)
                return false;

            safeDirection = verifiedDir;
            return true;
        }

        private static QueryResult ClearSegmentResumable(
            ref ResumableSegmentCheck check,
            Grid grid,
            Vector3 start,
            Vector3 end,
            BudgetChannel channel)
        {
            if (!check.Matches(start, end, grid.Key, environmentGeneration))
                check.Begin(grid, start, end, environmentGeneration);
            return check.Step(grid, channel);
        }

        private void EnqueueRouteRequest(Vector3 from, Vector3 goal)
        {
            routeRequested = true;
            RefreshPlanningDemandRegistration();
            routeRequestFrom = from;
            routeRequestGoal = goal;
            if (routeRequestStartTick == 0)
                routeRequestStartTick = Stopwatch.GetTimestamp();
        }

        private void ClearRouteRequest(bool markObserved)
        {
            if (markObserved && routeRequestStartTick != 0)
            {
                double ageMs = Math.Max(0.0, (Stopwatch.GetTimestamp() - routeRequestStartTick) * ticksToMs);
                if (ageMs > maxObservedRequestAgeMs)
                    maxObservedRequestAgeMs = ageMs;
            }
            routeRequested = false;
            RefreshPlanningDemandRegistration();
            routeRequestStartTick = 0;
        }

        private void MarkPendingImmediateTimestamp()
        {
            if (pendingImmediateStartTick == 0)
                pendingImmediateStartTick = Stopwatch.GetTimestamp();
        }

        private void ClearPendingImmediateTimestamp()
        {
            if (pendingImmediateStartTick != 0)
            {
                double ageMs = Math.Max(0.0, (Stopwatch.GetTimestamp() - pendingImmediateStartTick) * ticksToMs);
                if (ageMs > maxObservedRequestAgeMs)
                    maxObservedRequestAgeMs = ageMs;
                pendingImmediateStartTick = 0;
            }
        }

        private void ClearImmediateResumables()
        {
            immediateCornerCheck.Reset();
            immediateRouteStepCheck.Reset();
            immediateSepCheck.Reset();
            immediateDirectCheck.Reset();
            immediateWallCheck.Reset();
            pendingWallAttempt = 0;
        }

        private void ClearPendingShortcut()
        {
            shortcutActive = false;
            RefreshPlanningDemandRegistration();
            shortcutCheck.Reset();
            shortcutBlockedAtRouteIndex = -1;
            shortcutRetryAfterStep = -1;
        }

        private static void ProcessPlanningTurn(bool allowSpare)
        {
            allowPlanningSpareQuota = allowSpare;
            try
            {
                if (search == null)
                    search = new Search();

                // A* route planning has priority within Planning quota.
                int expansionsRemaining = MaxSearchCpuExpansionsPerTurn;
                while (expansionsRemaining > 0 && !IsPlanningBudgetExhausted())
                {
                    if (search.Owner == null)
                    {
                        EnemyObstacleNavigator nextRequester = DequeueNextRouteRequesterRoundRobin();
                        if (nextRequester == null)
                            break;

                        ShapeKey key = new ShapeKey(nextRequester.boundActor.BodyCollider);
                        nextRequester.BindGridAndActor(nextRequester.boundActor, key);
                        search.Begin(nextRequester, nextRequester.grid, nextRequester.boundActor.Body.position, nextRequester.routeRequestGoal);
                    }

                    QueryResult searchRes = search.Advance(expansionsRemaining, out int consumedExpansions);
                    expansionsRemaining -= consumedExpansions;
                    if (searchRes == QueryResult.Pending)
                        break;
                }

                // Remaining Planning budget serves 1 optional pending shortcut smoothing check per actor round-robin.
                AdvanceShortcutSmoothingRoundRobin();
            }
            finally
            {
                allowPlanningSpareQuota = false;
            }
        }

        private static EnemyObstacleNavigator DequeueNextRouteRequesterRoundRobin()
        {
            int count = registered.Count;
            if (count == 0)
                return null;

            planningCursor %= count;
            int start = planningCursor;
            for (int offset = 0; offset < count; offset++)
            {
                int idx = (start + offset) % count;
                EnemyObstacleNavigator nav = registered[idx];
                if (!nav.routeRequested || nav.boundActor == null || nav.boundActor.Body == null ||
                    nav.boundActor.BodyCollider == null || !nav.boundActor.gameObject.activeInHierarchy ||
                    nav.boundActor.IsDead)
                {
                    if (nav.routeRequested)
                        nav.ClearRouteRequest(markObserved: true);
                    continue;
                }

                planningCursor = (idx + 1) % count;
                return nav;
            }

            return null;
        }

        private static void AdvanceShortcutSmoothingRoundRobin()
        {
            int count = registered.Count;
            if (count == 0 || IsPlanningBudgetExhausted())
                return;

            shortcutCursor %= count;
            int start = shortcutCursor;
            bool stoppedOnBudget = false;
            for (int offset = 0; offset < count; offset++)
            {
                int idx = (start + offset) % count;
                EnemyObstacleNavigator nav = registered[idx];
                QueryResult res = nav.StepOptionalShortcut();
                if (res == QueryResult.Pending && IsPlanningBudgetExhausted())
                {
                    shortcutCursor = idx;
                    stoppedOnBudget = true;
                    break;
                }
            }

            if (!stoppedOnBudget && registered.Count > 0)
                shortcutCursor = (start + 1) % registered.Count;
        }

        private QueryResult StepOptionalShortcut()
        {
            if (boundActor == null || boundActor.Body == null || !boundActor.gameObject.activeInHierarchy ||
                boundActor.IsDead || grid == null || routeIndex + 1 >= routeCount)
            {
                ClearPendingShortcut();
                return QueryResult.Clear;
            }

            if (shortcutBlockedAtRouteIndex == routeIndex && currentPhysicsStepId < shortcutRetryAfterStep)
                return QueryResult.Clear;

            Vector3 position = boundActor.Body.position;
            int maxTarget = Mathf.Min(routeCount - 1, routeIndex + 8);

            if (shortcutActive)
            {
                if (shortcutTargetIndex <= routeIndex || shortcutTargetIndex > maxTarget ||
                    PlanarSqrDistance(position, shortcutOrigin) > Mathf.Max(0.09f, grid.Key.Radius * grid.Key.Radius * 0.25f) ||
                    PlanarSqrDistance(position, route[shortcutTargetIndex]) > 49f)
                {
                    shortcutActive = false;
                    RefreshPlanningDemandRegistration();
                    shortcutCheck.Reset();
                }
            }

            if (!shortcutActive)
            {
                int candidate = routeIndex + 1;
                if (candidate > maxTarget || PlanarSqrDistance(position, route[candidate]) > 49f)
                    return QueryResult.Clear;

                shortcutActive = true;
                RefreshPlanningDemandRegistration();
                shortcutTargetIndex = candidate;
                shortcutOrigin = position;
                shortcutCheck.Begin(grid, position, route[candidate], environmentGeneration);
            }

            QueryResult res = shortcutCheck.Step(grid, BudgetChannel.Planning);
            if (res == QueryResult.Pending)
                return QueryResult.Pending;

            shortcutActive = false;
            RefreshPlanningDemandRegistration();
            if (res == QueryResult.Clear)
            {
                if (shortcutTargetIndex > routeIndex && shortcutTargetIndex < routeCount)
                    routeIndex = shortcutTargetIndex;
                shortcutBlockedAtRouteIndex = -1;
                return QueryResult.Clear;
            }

            shortcutBlockedAtRouteIndex = routeIndex;
            shortcutRetryAfterStep = currentPhysicsStepId + 15;
            return QueryResult.Blocked;
        }

        private static void Register(EnemyObstacleNavigator nav)
        {
            if (nav.registeredIndex >= 0)
                return;
            nav.registeredIndex = registered.Count;
            registered.Add(nav);
            nav.RefreshPlanningDemandRegistration();
        }

        private static void Unregister(EnemyObstacleNavigator nav)
        {
            int idx = nav.registeredIndex;
            if (idx < 0 || idx >= registered.Count)
            {
                nav.registeredIndex = -1;
                nav.RefreshPlanningDemandRegistration();
                return;
            }

            int last = registered.Count - 1;
            if (idx != last)
            {
                EnemyObstacleNavigator moved = registered[last];
                registered[idx] = moved;
                moved.registeredIndex = idx;
            }
            registered.RemoveAt(last);
            nav.registeredIndex = -1;
            nav.RefreshPlanningDemandRegistration();

            if (registered.Count == 0)
            {
                immediateCursor = planningCursor = shortcutCursor = 0;
            }
            else
            {
                if (immediateCursor > idx) immediateCursor--;
                if (planningCursor > idx) planningCursor--;
                if (shortcutCursor > idx) shortcutCursor--;
                immediateCursor %= registered.Count;
                planningCursor %= registered.Count;
                shortcutCursor %= registered.Count;
            }
        }

        private static void PruneInvalidNavigators()
        {
            for (int i = registered.Count - 1; i >= 0; i--)
            {
                EnemyObstacleNavigator nav = registered[i];
                if (nav.boundActor == null || !nav.boundActor.gameObject.activeInHierarchy || nav.boundActor.IsDead)
                    nav.Reset();
            }
        }

        private void RefreshPlanningDemandRegistration()
        {
            bool active = registeredIndex >= 0 && (routeRequested || shortcutActive);
            if (active == countedInPlanningDemand)
                return;
            countedInPlanningDemand = active;
            planningDemandNavCount += active ? 1 : -1;
            if (planningDemandNavCount < 0)
                planningDemandNavCount = 0;
        }

        private static bool HasAnyPlanningDemand()
        {
            return (search != null && search.Owner != null) || planningDemandNavCount > 0;
        }

        private static void RefreshBudgetFrameIfNeeded()
        {
            int frame = Time.frameCount;
            if (budgetFrame != frame)
            {
                budgetFrame = frame;
                frameImmediateUsed = 0;
                framePlanningUsed = 0;
                immediateExhaustedInStep = false;
            }
        }

        private static bool IsImmediateBudgetExhausted()
        {
            RefreshBudgetFrameIfNeeded();
            int totalUsed = frameImmediateUsed + framePlanningUsed;
            if (totalUsed >= TotalQueriesPerFrame)
                return true;
            int protectedPlanning = HasAnyPlanningDemand()
                ? Mathf.Max(0, PlanningReservedPerFrame - framePlanningUsed)
                : 0;
            return totalUsed >= TotalQueriesPerFrame - protectedPlanning;
        }

        private static bool IsPlanningBudgetExhausted()
        {
            RefreshBudgetFrameIfNeeded();
            int totalUsed = frameImmediateUsed + framePlanningUsed;
            if (totalUsed >= TotalQueriesPerFrame)
                return true;
            return !allowPlanningSpareQuota && framePlanningUsed >= PlanningReservedPerFrame;
        }

        private static bool TryConsumeQuery(BudgetChannel channel)
        {
            RefreshBudgetFrameIfNeeded();
            int totalUsed = frameImmediateUsed + framePlanningUsed;
            if (totalUsed >= TotalQueriesPerFrame)
            {
                budgetExhaustedEvents++;
                return false;
            }

            if (channel == BudgetChannel.Immediate)
            {
                int protectedPlanning = HasAnyPlanningDemand()
                    ? Mathf.Max(0, PlanningReservedPerFrame - framePlanningUsed)
                    : 0;
                if (totalUsed >= TotalQueriesPerFrame - protectedPlanning)
                {
                    budgetExhaustedEvents++;
                    return false;
                }
                frameImmediateUsed++;
                return true;
            }

            if (!allowPlanningSpareQuota && framePlanningUsed >= PlanningReservedPerFrame)
            {
                budgetExhaustedEvents++;
                return false;
            }

            framePlanningUsed++;
            return true;
        }

        private static void EnsureContext()
        {
            SceneHandle scene = SceneManager.GetActiveScene().handle;
            EntityId surface = ArenaSurface.Active != null ? ArenaSurface.Active.GetEntityId() : EntityId.None;
            int obs = obstacleVersion;
            if (contextScene == scene && contextSurface == surface && contextObstacleVersion == obs)
                return;

            bool sceneOrSurfaceChanged = contextScene != scene || contextSurface != surface;
            contextScene = scene;
            contextSurface = surface;
            contextObstacleVersion = obs;
            environmentGeneration++;

            if (sceneOrSurfaceChanged)
                grids.Clear();
            else
            {
                // Obstacle layout changed in the same scene/surface: reuse cached grid arrays.
                foreach (KeyValuePair<ShapeKey, Grid> kvp in grids)
                    kvp.Value.InvalidateInPlace(scene, surface, obs);
            }

            search?.Cancel();
            for (int i = 0; i < registered.Count; i++)
            {
                EnemyObstacleNavigator nav = registered[i];
                nav.ClearImmediateResumables();
                nav.ClearPendingShortcut();
                nav.InvalidateVerifiedSegment();
                if (sceneOrSurfaceChanged)
                {
                    nav.ClearRouteRequest(markObserved: false);
                    nav.routeCount = nav.routeIndex = 0;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearSharedState()
        {
            for (int i = 0; i < registered.Count; i++)
            {
                registered[i].registeredIndex = -1;
                registered[i].countedInPlanningDemand = false;
            }
            grids.Clear();
            registered.Clear();
            planningDemandNavCount = 0;
            search = null;
            contextScene = SceneHandle.None;
            contextSurface = EntityId.None;
            obstacleVersion = 0;
            contextObstacleVersion = 0;
            environmentGeneration = 1;
            budgetFrame = -1;
            frameImmediateUsed = 0;
            framePlanningUsed = 0;
            allowPlanningSpareQuota = false;
            immediateExhaustedInStep = false;
            currentPhysicsStepId = 0;
            immediateCursor = planningCursor = shortcutCursor = 0;
            overlapBoxQueries = capsuleCastQueries = overlapCapsuleQueries = totalNavigationTicks = searchStartedTick = 0;
            budgetExhaustedEvents = 0;
            maxObservedRequestAgeMs = 0.0;
        }

        private static float PlanarSqrDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z;
        }

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
            public SceneHandle Scene;
            public EntityId Surface;
            public int ObstacleGen;
            public readonly int Width, Depth, Count;
            public readonly float Cell;
            private readonly float minimumX, minimumZ;
            private readonly sbyte[] occupancy;
            private readonly byte[] checkedEdges, blockedEdges;
            private readonly Collider ground;

            public Grid(ShapeKey key, SceneHandle scene, EntityId surface, int obstacleGen)
            {
                Key = key;
                Scene = scene;
                Surface = surface;
                ObstacleGen = obstacleGen;
                ground = ArenaSurface.Active != null ? ArenaSurface.Active.GroundCollider : null;
                Bounds bounds = ground != null ? ground.bounds : new Bounds(Vector3.zero, new Vector3(256f, 1f, 256f));
                Cell = Mathf.Max(0.8f, Key.Radius * 1.2f);
                minimumX = bounds.min.x + Key.Radius;
                minimumZ = bounds.min.z + Key.Radius;
                Width = Mathf.Max(2, Mathf.FloorToInt((bounds.size.x - Key.Radius * 2f) / Cell) + 1);
                Depth = Mathf.Max(2, Mathf.FloorToInt((bounds.size.z - Key.Radius * 2f) / Cell) + 1);
                Count = Width * Depth;
                occupancy = new sbyte[Count];
                checkedEdges = new byte[Count];
                blockedEdges = new byte[Count];
            }

            public void EnsureValid(SceneHandle scene, EntityId surface, int obsGen)
            {
                if (Scene == scene && Surface == surface && ObstacleGen == obsGen)
                    return;
                InvalidateInPlace(scene, surface, obsGen);
            }

            public void InvalidateInPlace(SceneHandle scene, EntityId surface, int obsGen)
            {
                Scene = scene;
                Surface = surface;
                ObstacleGen = obsGen;
                Array.Clear(occupancy, 0, occupancy.Length);
                Array.Clear(checkedEdges, 0, checkedEdges.Length);
                Array.Clear(blockedEdges, 0, blockedEdges.Length);
            }

            public int Index(Vector3 position) =>
                Mathf.Clamp(Mathf.RoundToInt((position.z - minimumZ) / Cell), 0, Depth - 1) * Width
                + Mathf.Clamp(Mathf.RoundToInt((position.x - minimumX) / Cell), 0, Width - 1);

            public Vector3 PlanarPosition(int index) =>
                new Vector3(minimumX + index % Width * Cell, 1f, minimumZ + index / Width * Cell);

            public Vector3 Position(int index) => Grounded(PlanarPosition(index));

            public Vector3 Grounded(Vector3 point)
            {
                point.y = ArenaSurface.SupportHeight(point, Key.Radius) + EnemySpawnGeometry.RootHeight;
                return point;
            }

            public void Capsule(Vector3 root, out Vector3 bottom, out Vector3 top)
            {
                Vector3 center = root + Vector3.up * Key.CenterOffset;
                float segment = Mathf.Max(0f, Key.HalfHeight - Key.Radius);
                bottom = center - Vector3.up * segment;
                top = center + Vector3.up * segment;
            }

            public bool IsObstacle(Collider collider)
            {
                if (collider == null || collider == ground)
                    return false;
                return ground != null || collider.bounds.max.y > 0.08f;
            }

            public bool QueryOverlapBox(Vector3 center, Vector3 halfExtents, BudgetChannel channel, out int count)
            {
                if (!TryConsumeQuery(channel))
                {
                    count = 0;
                    return false;
                }
                overlapBoxQueries++;
                count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlaps, Quaternion.identity,
                    1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore);
                return true;
            }

            public bool QueryCapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 direction,
                float distance, BudgetChannel channel, out int count)
            {
                if (!TryConsumeQuery(channel))
                {
                    count = 0;
                    return false;
                }
                capsuleCastQueries++;
                count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, hits, distance,
                    1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore);
                return true;
            }

            public bool QueryOverlapCapsule(Vector3 bottom, Vector3 top, float radius, BudgetChannel channel, out int count)
            {
                if (!TryConsumeQuery(channel))
                {
                    count = 0;
                    return false;
                }
                overlapCapsuleQueries++;
                count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlaps,
                    1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore);
                return true;
            }

            public QueryResult Walkable(int index, BudgetChannel channel)
            {
                if (occupancy[index] != 0)
                    return occupancy[index] > 0 ? QueryResult.Clear : QueryResult.Blocked;

                Capsule(Position(index), out Vector3 bottom, out Vector3 top);
                if (!QueryOverlapCapsule(bottom, top, Key.Radius, channel, out int count))
                    return QueryResult.Pending;

                bool free = count < overlaps.Length;
                for (int i = 0; i < count && free; i++)
                    free = !IsObstacle(overlaps[i]);
                occupancy[index] = free ? (sbyte)1 : (sbyte)-1;
                return free ? QueryResult.Clear : QueryResult.Blocked;
            }

            public QueryResult Edge(int from, int to, int direction, ref ResumableSegmentCheck segmentCheck,
                int envVersion, BudgetChannel channel)
            {
                int mask = 1 << direction;
                if ((checkedEdges[from] & mask) != 0)
                    return (blockedEdges[from] & mask) == 0 ? QueryResult.Clear : QueryResult.Blocked;

                Vector3 fromPos = PlanarPosition(from);
                Vector3 toPos = PlanarPosition(to);
                if (!segmentCheck.Matches(fromPos, toPos, Key, envVersion, 0.0001f))
                    segmentCheck.Begin(this, fromPos, toPos, envVersion);

                QueryResult res = segmentCheck.Step(this, channel);
                if (res == QueryResult.Pending)
                    return QueryResult.Pending;

                checkedEdges[from] |= (byte)mask;
                if (res == QueryResult.Blocked)
                    blockedEdges[from] |= (byte)mask;
                return res;
            }
        }

        private sealed class Search
        {
            private static readonly int[] dx = { 1, 0, -1, 0, 1, -1, -1, 1 };
            private static readonly int[] dz = { 0, 1, 0, -1, 1, 1, -1, -1 };

            public EnemyObstacleNavigator Owner { get; private set; }
            private Grid map;
            private int searchEnvVersion;
            private Vector3 originPos, destinationPos;

            private float[] cost, score;
            private int[] parent, seen, closed, heap, heapIndex, reverse;
            private int generation, heapCount, goal, startIndex, expanded;

            // Resumable NearestWalkable state.
            private bool findingGoal;
            private int nwCenter, nwRadius, nwDz, nwDx, nwResolvedGoal;

            // Resumable neighbor expansion state.
            private bool hasActiveNode;
            private int activeNode, activeDirection;
            private NeighborStage neighborStage;
            private ResumableSegmentCheck edgeSegmentCheck;

            public void Cancel()
            {
                if (Owner != null && searchStartedTick != 0)
                {
                    double ageMs = Math.Max(0.0, (Stopwatch.GetTimestamp() - searchStartedTick) * ticksToMs);
                    if (ageMs > maxObservedRequestAgeMs)
                        maxObservedRequestAgeMs = ageMs;
                    searchStartedTick = 0;
                }

                Owner = null;
                heapCount = 0;
                findingGoal = false;
                hasActiveNode = false;
                edgeSegmentCheck.Reset();
            }

            public void Begin(EnemyObstacleNavigator owner, Grid grid, Vector3 from, Vector3 destination)
            {
                Owner = owner;
                map = grid;
                searchEnvVersion = environmentGeneration;
                originPos = from;
                destinationPos = destination;
                searchStartedTick = Stopwatch.GetTimestamp();

                if (cost == null || cost.Length < grid.Count)
                {
                    cost = new float[grid.Count];
                    score = new float[grid.Count];
                    parent = new int[grid.Count];
                    seen = new int[grid.Count];
                    closed = new int[grid.Count];
                    heap = new int[grid.Count];
                    heapIndex = new int[grid.Count];
                    reverse = new int[grid.Count];
                    generation = 0;
                }

                generation++;
                heapCount = 0;
                expanded = 0;
                startIndex = map.Index(from);

                findingGoal = true;
                nwCenter = map.Index(destination);
                nwRadius = 0;
                nwDz = 0;
                nwDx = 0;
                nwResolvedGoal = nwCenter;

                hasActiveNode = false;
                edgeSegmentCheck.Reset();
            }

            public QueryResult Advance(int maxCpuExpansions, out int consumedExpansions)
            {
                consumedExpansions = 0;
                if (Owner == null)
                    return QueryResult.Clear;

                if (searchEnvVersion != environmentGeneration)
                {
                    Begin(Owner, map, Owner.boundActor != null && Owner.boundActor.Body != null ? Owner.boundActor.Body.position : originPos, destinationPos);
                }

                if (findingGoal)
                {
                    QueryResult nwRes = StepNearestWalkable();
                    if (nwRes == QueryResult.Pending)
                        return QueryResult.Pending;

                    goal = nwResolvedGoal;
                    findingGoal = false;
                    seen[startIndex] = generation;
                    cost[startIndex] = 0f;
                    parent[startIndex] = -1;
                    score[startIndex] = Heuristic(startIndex);
                    Push(startIndex);
                }

                while (consumedExpansions < maxCpuExpansions && expanded < MaximumSearchExpansions)
                {
                    if (!hasActiveNode)
                    {
                        if (heapCount == 0)
                        {
                            Fail();
                            return QueryResult.Blocked;
                        }

                        activeNode = Pop();
                        if (activeNode == goal)
                        {
                            Complete(activeNode);
                            return QueryResult.Clear;
                        }

                        closed[activeNode] = generation;
                        expanded++;
                        consumedExpansions++;
                        activeDirection = 0;
                        neighborStage = NeighborStage.WalkTarget;
                        edgeSegmentCheck.Reset();
                        hasActiveNode = true;
                    }

                    int x = activeNode % map.Width;
                    int z = activeNode / map.Width;
                    while (activeDirection < 8)
                    {
                        int nx = x + dx[activeDirection];
                        int nz = z + dz[activeDirection];
                        if (nx < 0 || nz < 0 || nx >= map.Width || nz >= map.Depth)
                        {
                            AdvanceDirection();
                            continue;
                        }

                        int next = nz * map.Width + nx;
                        if (closed[next] == generation)
                        {
                            AdvanceDirection();
                            continue;
                        }

                        float nextCost = cost[activeNode] + (activeDirection < 4 ? 1f : 1.41421356f);
                        if (seen[next] == generation && nextCost >= cost[next])
                        {
                            AdvanceDirection();
                            continue;
                        }

                        if (neighborStage == NeighborStage.WalkTarget)
                        {
                            QueryResult walkTarget = map.Walkable(next, BudgetChannel.Planning);
                            if (walkTarget == QueryResult.Pending)
                                return QueryResult.Pending;
                            if (walkTarget == QueryResult.Blocked)
                            {
                                AdvanceDirection();
                                continue;
                            }
                            neighborStage = activeDirection >= 4 ? NeighborStage.WalkCornerA : NeighborStage.EdgeCheck;
                        }

                        if (neighborStage == NeighborStage.WalkCornerA)
                        {
                            QueryResult cornerA = map.Walkable(z * map.Width + nx, BudgetChannel.Planning);
                            if (cornerA == QueryResult.Pending)
                                return QueryResult.Pending;
                            if (cornerA == QueryResult.Blocked)
                            {
                                AdvanceDirection();
                                continue;
                            }
                            neighborStage = NeighborStage.WalkCornerB;
                        }

                        if (neighborStage == NeighborStage.WalkCornerB)
                        {
                            QueryResult cornerB = map.Walkable(nz * map.Width + x, BudgetChannel.Planning);
                            if (cornerB == QueryResult.Pending)
                                return QueryResult.Pending;
                            if (cornerB == QueryResult.Blocked)
                            {
                                AdvanceDirection();
                                continue;
                            }
                            neighborStage = NeighborStage.EdgeCheck;
                        }

                        if (neighborStage == NeighborStage.EdgeCheck)
                        {
                            QueryResult edgeRes = map.Edge(
                                activeNode, next, activeDirection, ref edgeSegmentCheck, environmentGeneration, BudgetChannel.Planning);
                            if (edgeRes == QueryResult.Pending)
                                return QueryResult.Pending;

                            if (edgeRes == QueryResult.Clear)
                            {
                                bool existing = seen[next] == generation;
                                seen[next] = generation;
                                cost[next] = nextCost;
                                score[next] = nextCost + Heuristic(next);
                                parent[next] = activeNode;
                                if (existing)
                                    Up(heapIndex[next]);
                                else
                                    Push(next);
                            }

                            AdvanceDirection();
                        }
                    }

                    hasActiveNode = false;
                }

                if (expanded >= MaximumSearchExpansions)
                {
                    Fail();
                    return QueryResult.Blocked;
                }

                return QueryResult.Pending;
            }

            private void AdvanceDirection()
            {
                activeDirection++;
                neighborStage = NeighborStage.WalkTarget;
                edgeSegmentCheck.Reset();
            }

            private QueryResult StepNearestWalkable()
            {
                if (nwRadius == 0)
                {
                    QueryResult centerRes = map.Walkable(nwCenter, BudgetChannel.Planning);
                    if (centerRes == QueryResult.Pending)
                        return QueryResult.Pending;
                    if (centerRes == QueryResult.Clear)
                    {
                        nwResolvedGoal = nwCenter;
                        return QueryResult.Clear;
                    }
                    nwRadius = 1;
                    nwDz = -1;
                    nwDx = -1;
                }

                int x = nwCenter % map.Width;
                int z = nwCenter / map.Width;
                while (nwRadius <= 5)
                {
                    while (nwDz <= nwRadius)
                    {
                        while (nwDx <= nwRadius)
                        {
                            if (Mathf.Abs(nwDx) == nwRadius || Mathf.Abs(nwDz) == nwRadius)
                            {
                                int nx = x + nwDx;
                                int nz = z + nwDz;
                                if (nx >= 0 && nx < map.Width && nz >= 0 && nz < map.Depth)
                                {
                                    int idx = nz * map.Width + nx;
                                    QueryResult res = map.Walkable(idx, BudgetChannel.Planning);
                                    if (res == QueryResult.Pending)
                                        return QueryResult.Pending;
                                    if (res == QueryResult.Clear)
                                    {
                                        nwResolvedGoal = idx;
                                        return QueryResult.Clear;
                                    }
                                }
                            }
                            nwDx++;
                        }
                        nwDz++;
                        nwDx = -nwRadius;
                    }
                    nwRadius++;
                    nwDz = -nwRadius;
                    nwDx = -nwRadius;
                }

                nwResolvedGoal = nwCenter;
                return QueryResult.Blocked;
            }

            private float Heuristic(int index)
            {
                int x = Mathf.Abs(index % map.Width - goal % map.Width);
                int z = Mathf.Abs(index / map.Width - goal / map.Width);
                return Mathf.Max(x, z) + Mathf.Min(x, z) * 0.41421356f;
            }

            private void Fail()
            {
                if (Owner != null)
                {
                    Owner.retryDelay = 1f;
                    Owner.ClearRouteRequest(markObserved: true);
                }
                Cancel();
            }

            private void Complete(int end)
            {
                int count = 0;
                while (end >= 0 && count < reverse.Length)
                {
                    reverse[count++] = end;
                    end = parent[end];
                }

                Owner.routeCount = Owner.routeIndex = 0;
                Owner.routeGoal = destinationPos;
                int previousDirection = int.MinValue;
                for (int i = count - 1; i >= 0 && Owner.routeCount < Owner.route.Length; i--)
                {
                    int direction = i > 0 ? reverse[i - 1] - reverse[i] : int.MinValue;
                    if (i == count - 1 || i == 0 || direction != previousDirection)
                        Owner.route[Owner.routeCount++] = map.Position(reverse[i]);
                    previousDirection = direction;
                }

                Owner.retryDelay = 0.25f;
                Owner.stalledTime = 0f;
                Owner.ClearRouteRequest(markObserved: true);
                Owner.ClearPendingShortcut();
                Cancel();
            }

            private void Push(int node)
            {
                heap[heapCount] = node;
                heapIndex[node] = heapCount;
                Up(heapCount++);
            }

            private void Up(int index)
            {
                while (index > 0)
                {
                    int above = (index - 1) / 2;
                    if (score[heap[above]] <= score[heap[index]])
                        break;
                    Swap(index, above);
                    index = above;
                }
            }

            private int Pop()
            {
                int first = heap[0];
                heapCount--;
                if (heapCount == 0)
                    return first;
                heap[0] = heap[heapCount];
                heapIndex[heap[0]] = 0;
                int index = 0;
                while (index * 2 + 1 < heapCount)
                {
                    int child = index * 2 + 1;
                    if (child + 1 < heapCount && score[heap[child + 1]] < score[heap[child]])
                        child++;
                    if (score[heap[index]] <= score[heap[child]])
                        break;
                    Swap(index, child);
                    index = child;
                }
                return first;
            }

            private void Swap(int a, int b)
            {
                int node = heap[a];
                heap[a] = heap[b];
                heap[b] = node;
                heapIndex[heap[a]] = a;
                heapIndex[heap[b]] = b;
            }
        }
    }
}
