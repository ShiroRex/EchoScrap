using System;
using EchoScrap.Core;
using Unity.Cinemachine;
using Unity.Profiling;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Session-clock scheduler and bounded, atomic group placement outside a stable 3D view.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public sealed class CombatWaveDirector : MonoBehaviour
    {
        [SerializeField] private CombatSession session;
        [SerializeField] private EnemyPool enemyPool;
        [SerializeField] private PlayerHealth player;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private CorrosivePuddlePool hazards;
        [SerializeField] private CombatWaveSettings settings;
        [SerializeField] private bool autoTick = true;
        [SerializeField] private bool stressPopulation;

        [SerializeField] private Collider arenaGround;
        [SerializeField] private CinemachineCamera cameraRig;
        private static readonly ProfilerMarker PlanMarker = new ProfilerMarker("EchoScrap.Spawner.PlanGroup");
        private readonly Plane[] viewPlanes = new Plane[6];
        private readonly EnemyArchetypeData[] groupData = new EnemyArchetypeData[4];
        private readonly EnemySpawnGeometry[] groupGeometry = new EnemySpawnGeometry[4];
        private readonly Vector3[] groupOffsets = new Vector3[4];
        private readonly Vector3[] groupPositions = new Vector3[4];
        private readonly EnemyActor[] activatedGroup = new EnemyActor[4];
        private readonly int[] discardedByReason = new int[8];
        private readonly Collider[] obstacleBuffer = new Collider[32];
        private readonly Vector3[] reservationPositions = new Vector3[16];
        private readonly float[] reservationRadii = new float[16];
        private readonly DeterministicRandom fallbackRng = new DeterministicRandom(1337);
        private CombatWaveSettings ownedSettings;
        private bool subscribed;
        private int reservationCount;
        private int minionOrderOffset;
        private float nextSpawnTime;
        private float nextMinionTime;
        private int phaseIndex = -1;

        public event Action OnBossPhaseRequested;

        public bool AutoTick { get => autoTick; set => autoTick = value; }
        public bool StressPopulation { get => stressPopulation; set => stressPopulation = value; }
        public bool IsBossPhase { get; private set; }
        public bool BossActive { get; private set; }
        public float Elapsed => session != null ? session.GameplayTime : 0f;
        public float NextSpawnTime => nextSpawnTime;
        public int PhaseIndex => phaseIndex;
        public int ReservedCount => reservationCount;
        public int TotalSpawnedCount { get; private set; }
        public int TotalDiscardedCount { get; private set; }
        public int GroupsAttemptedCount { get; private set; }
        public int BossMinionWavesCount { get; private set; }
        public int LastGroupSpawnedCount { get; private set; }
        public enum DiscardReason { None, PopulationCap, InvalidCamera, MissingGround, OutsideArena, Obstructed, QuerySaturated, PoolFailure }
        public Collider ArenaGround => arenaGround;
        public int LastAnchorAttempts { get; private set; }
        public int LastMemberValidations { get; private set; }
        public double TotalPlanningMilliseconds { get; private set; }
        public int Discarded(DiscardReason reason) => discardedByReason[(int)reason];
        public int PopulationLimit => Mathf.Max(1, stressPopulation ? Settings.StressPopulationCap : Settings.PopulationCap);
        public CombatWaveSettings Settings
        {
            get
            {
                if (settings == null)
                {
                    if (ownedSettings == null)
                    {
                        ownedSettings = ScriptableObject.CreateInstance<CombatWaveSettings>();
                        ownedSettings.name = "CombatWaveSettings (runtime)";
                    }
                    settings = ownedSettings;
                }
                return settings;
            }
            set
            {
                settings = value;
                ScheduleCurrentPhase();
            }
        }

        public int ActiveEnemyCount
        {
            get
            {
                int count = 0;
                var active = EnemyActor.ActiveEnemies;
                for (int i = 0; i < active.Count; i++)
                {
                    EnemyActor actor = active[i];
                    if (actor != null && actor.IsAlive && actor.gameObject.activeInHierarchy
                        && (session == null || actor.Session == session)
                        && actor.ArchetypeId != EnemyArchetypeId.GolgoraBoss)
                        count++;
                }
                return count;
            }
        }

        private void Awake()
        {
            ResolveReferences();
            ScheduleCurrentPhase();
        }

        private void OnEnable()
        {
            ResolveReferences();
            BindPools();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (ownedSettings != null)
                Destroy(ownedSettings);
        }

        private void ResolveReferences()
        {
            if (session == null) session = FindAnyObjectByType<CombatSession>();
            if (enemyPool == null) enemyPool = FindAnyObjectByType<EnemyPool>();
            if (player == null) player = session != null ? session.PlayerHealth : null;
            if (player == null) player = FindAnyObjectByType<PlayerHealth>();
            if (gameplayCamera == null) gameplayCamera = Camera.main;
            if (hazards == null) hazards = FindAnyObjectByType<CorrosivePuddlePool>();
        }

        public void BindReferences(CombatSession combatSession, EnemyPool pool, PlayerHealth target, Camera camera)
        {
            Unsubscribe();
            session = combatSession;
            enemyPool = pool;
            player = target;
            gameplayCamera = camera;
            ResolveReferences();
            BindPools();
            PrepareSpawnGeometry();
            if (isActiveAndEnabled)
                Subscribe();
            ResetForSession();
        }

        public void BindSpawnContext(Collider floor, CinemachineCamera rig = null)
        {
            arenaGround = floor;
            cameraRig = rig;
        }

        private void PrepareSpawnGeometry()
        {
            if (enemyPool == null) return;
            for (int id = (int)EnemyArchetypeId.Residual; id <= (int)EnemyArchetypeId.ExplosiveMoth; id++)
                enemyPool.GetSpawnGeometry(Settings.GetArchetype((EnemyArchetypeId)id));
        }

        public void BindHazardPool(CorrosivePuddlePool pool)
        {
            hazards = pool;
            BindPools();
        }

        private void BindPools()
        {
            enemyPool?.BindReferences(session, player, hazards);
            hazards?.BindReferences(session, player);
        }

        private void Subscribe()
        {
            if (session == null || subscribed)
                return;
            session.SessionReset += ResetForSession;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (session != null && subscribed)
                session.SessionReset -= ResetForSession;
            subscribed = false;
        }

        public void ResetForSession()
        {
            nextMinionTime = 0f;
            reservationCount = 0;
            minionOrderOffset = 0;
            IsBossPhase = false;
            BossActive = false;
            TotalSpawnedCount = 0;
            TotalDiscardedCount = 0;
            GroupsAttemptedCount = 0;
            BossMinionWavesCount = 0;
            LastGroupSpawnedCount = 0;
            LastAnchorAttempts = LastMemberValidations = 0;
            TotalPlanningMilliseconds = 0;
            Array.Clear(discardedByReason, 0, discardedByReason.Length);
            enemyPool?.DespawnAllActive();
            hazards?.ClearAll();
            ScheduleCurrentPhase();
        }

        private void ScheduleCurrentPhase()
        {
            CombatWavePhase phase = Settings.GetPhase(Elapsed, out phaseIndex, out float progress);
            nextSpawnTime = Elapsed + ResolveInterval(phase, progress);
        }

        private static float ResolveInterval(CombatWavePhase phase, float progress)
        {
            return Mathf.Max(0.05f, Mathf.Lerp(phase.InitialInterval, phase.FinalInterval, progress));
        }

        private bool CanRun()
        {
            return isActiveAndEnabled && session != null && !session.IsPaused
                && session.IsGameplayFlowActive() && player != null && player.IsAlive
                && enemyPool != null && Time.timeScale > 0f;
        }

        private void LateUpdate()
        {
            if (autoTick)
                Step();
        }

        /// <summary>Evalúa el reloj de sesión; nunca lo avanza ni acumula nacimientos pendientes.</summary>
        public void Step()
        {
            if (!CanRun())
                return;

            float elapsed = Elapsed;
            if (!IsBossPhase && elapsed + 0.00001f >= Mathf.Max(0f, Settings.BossStartTime))
            {
                BeginBossPhase();
                return;
            }

            if (IsBossPhase)
            {
                if (BossActive)
                {
                    float interval = Mathf.Max(0.05f, Settings.BossMinionInterval);
                    if (elapsed + 0.00001f >= nextMinionTime)
                    {
                        SpawnBossMinionWave();
                        nextMinionTime += interval;
                        if (nextMinionTime <= elapsed + 0.00001f)
                            nextMinionTime = elapsed + interval;
                    }
                }
                return;
            }

            CombatWavePhase phase = Settings.GetPhase(elapsed, out int currentPhase, out float progress);
            float spawnInterval = ResolveInterval(phase, progress);
            if (currentPhase != phaseIndex)
            {
                phaseIndex = currentPhase;
                nextSpawnTime = elapsed + spawnInterval;
                return;
            }

            if (elapsed + 0.00001f < nextSpawnTime)
                return;

            GroupsAttemptedCount++;
            LastGroupSpawnedCount = 0;
            EnemyWaveWeights weights = EnemyWaveWeights.Lerp(phase.InitialWeights, phase.FinalWeights, progress);
            SpawnGroup(Mathf.Clamp(phase.GroupSize, 1, 4), weights, PopulationLimit, false);

            // Mantener el calendario en frames normales, pero descartar intervalos vencidos al saltar tiempo.
            nextSpawnTime += spawnInterval;
            if (nextSpawnTime <= elapsed + 0.00001f)
                nextSpawnTime = elapsed + spawnInterval;
        }

        private DeterministicRandom RandomSource => session != null ? session.Rng.Combat : fallbackRng;

        private void BeginBossPhase()
        {
            IsBossPhase = true;
            BossActive = false;
            nextMinionTime = 0f;
            reservationCount = 0;
            enemyPool.DespawnAllActive();
            if (hazards == null)
                hazards = FindAnyObjectByType<CorrosivePuddlePool>();
            hazards?.ClearAll();
            MothExplosionPool.GetOrCreate(session, player).ClearAll();
            session.SetBossIntroState(true);
            OnBossPhaseRequested?.Invoke();
        }

        public void SetBossActive(bool active)
        {
            BossActive = active && IsBossPhase;
            nextMinionTime = Elapsed + Mathf.Max(0.05f, Settings.BossMinionInterval);
        }

        public int SpawnBossMinionWave()
        {
            if (!CanRun() || !IsBossPhase || !BossActive)
                return 0;

            EnemyArchetypeId[] order = Settings.BossMinionOrder;
            if (order == null || order.Length == 0)
                return 0;

            BossMinionWavesCount++;
            LastGroupSpawnedCount = 0;
            int cap = Mathf.Min(PopulationLimit, Mathf.Max(1, Settings.BossMinionCap));
            SpawnGroup(Mathf.Min(4, order.Length), default, cap, true);
            minionOrderOffset = (minionOrderOffset + 1) % order.Length;
            return LastGroupSpawnedCount;
        }

        private void RecordDiscard(int count, DiscardReason reason)
        {
            TotalDiscardedCount += count;
            discardedByReason[(int)reason] += count;
        }

        private void SpawnGroup(int requested, EnemyWaveWeights weights, int cap, bool minions)
        {
            LastAnchorAttempts = LastMemberValidations = 0;
            int size = Mathf.Min(requested, Mathf.Max(0, cap - ActiveEnemyCount - reservationCount));
            RecordDiscard(requested - size, DiscardReason.PopulationCap);
            if (size == 0) return;
            int mortarCount = 0;
            var activeEnemies = EnemyActor.ActiveEnemies;
            for (int i = 0; i < activeEnemies.Count; i++)
                if (activeEnemies[i] != null && activeEnemies[i].IsAlive && activeEnemies[i].gameObject.activeInHierarchy
                    && (session == null || activeEnemies[i].Session == session) && activeEnemies[i].ArchetypeId == EnemyArchetypeId.Mortar) mortarCount++;
            for (int i = 0; i < size; i++)
            {
                EnemyArchetypeId id = minions
                    ? Settings.BossMinionOrder[(i + minionOrderOffset) % Settings.BossMinionOrder.Length]
                    : weights.Choose(RandomSource.NextFloat());
                if (id == EnemyArchetypeId.Mortar)
                {
                    if (mortarCount >= Mathf.Max(0, Settings.MortarPopulationCap)) id = EnemyArchetypeId.Residual;
                    else mortarCount++;
                }
                groupData[i] = Settings.GetArchetype(id);
                groupGeometry[i] = enemyPool.GetSpawnGeometry(groupData[i]);
            }
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            bool planned;
            DiscardReason failure;
            using (PlanMarker.Auto()) planned = TryPlanGroup(size, out failure);
            TotalPlanningMilliseconds += (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (!planned)
            {
                RecordDiscard(size, failure);
                return;
            }
            int activated = 0;
            try
            {
                // Reserve the entire validated formation before any actor can become active.
                reservationCount = size;
                for (int i = 0; i < size; i++)
                {
                    reservationPositions[i] = groupPositions[i];
                    reservationRadii[i] = groupGeometry[i].Radius;
                }
                for (int i = 0; i < size; i++)
                {
                    EnemyActor actor = enemyPool.Spawn(groupData[i], groupPositions[i]);
                    if (actor == null) return;
                    activatedGroup[activated++] = actor;
                }
                LastGroupSpawnedCount = size;
                TotalSpawnedCount += size;
            }
            finally
            {
                if (activated != size)
                {
                    for (int i = 0; i < activated; i++) enemyPool.ReturnToPool(activatedGroup[i]);
                    RecordDiscard(size, DiscardReason.PoolFailure);
                }
                Array.Clear(activatedGroup, 0, activatedGroup.Length);
                reservationCount = 0;
            }
        }

        public bool TryFindSpawnPosition(EnemyArchetypeId id, out Vector3 position)
        {
            position = default;
            if (enemyPool == null) return false;
            groupData[0] = Settings.GetArchetype(id);
            groupGeometry[0] = enemyPool.GetSpawnGeometry(groupData[0]);
            LastAnchorAttempts = LastMemberValidations = 0;
            if (!TryPlanGroup(1, out _)) return false;
            position = groupPositions[0];
            return true;
        }

        private bool TryPlanGroup(int size, out DiscardReason failure)
        {
            failure = DiscardReason.InvalidCamera;
            if (player == null || !BuildStableView()) return false;
            failure = DiscardReason.MissingGround;
            if (arenaGround == null || !arenaGround.enabled || !arenaGround.gameObject.activeInHierarchy) return false;
            Vector3 origin = player.transform.position;
            origin.y = EnemySpawnGeometry.RootHeight;
            float maxRadius = 0f;
            for (int i = 0; i < size; i++) maxRadius = Mathf.Max(maxRadius, groupGeometry[i].Radius);
            float spacing = 2f * maxRadius + Mathf.Max(0f, Settings.SpawnClearance);
            int attempts = Mathf.Clamp(Settings.PositionAttempts, 1, 16);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                LastAnchorAttempts++;
                float angle = RandomSource.Range(0f, Mathf.PI * 2f);
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 tangent = new Vector3(-radial.z, 0f, radial.x);
                Bounds envelope = default;
                for (int i = 0; i < size; i++)
                {
                    Vector2 offset = FormationOffset(size, i) * spacing;
                    groupOffsets[i] = tangent * offset.x + radial * offset.y;
                    Bounds member = groupGeometry[i].VisualBounds;
                    member.center += groupOffsets[i];
                    if (i == 0) envelope = member; else envelope.Encapsulate(member);
                }
                float distance = RandomSource.Range(Mathf.Max(0f, Settings.BaselineMinimumDistance),
                    Mathf.Max(Settings.BaselineMinimumDistance, Settings.BaselineMaximumDistance));
                if (!TryFindViewExit(origin + envelope.center, envelope.extents, radial, out float exit)) continue;
                distance = Mathf.Max(distance, exit + 0.05f);
                Vector3 anchor = origin + radial * distance;
                Bounds worldEnvelope = envelope;
                worldEnvelope.center += anchor;
                if (!OutsideView(worldEnvelope)) continue;
                bool valid = true;
                for (int i = 0; i < size; i++)
                {
                    LastMemberValidations++;
                    Vector3 position = anchor + groupOffsets[i];
                    groupPositions[i] = position;
                    if (!InsideArena(new Vector2(position.x, position.z), groupGeometry[i].Radius))
                    { failure = DiscardReason.OutsideArena; valid = false; break; }
                    if (!HasGround(position, groupGeometry[i].Radius))
                    { failure = DiscardReason.MissingGround; valid = false; break; }
                    if (!PositionIsClear(position, groupGeometry[i], i, out failure))
                    { valid = false; break; }
                }
                if (valid) { failure = DiscardReason.None; return true; }
            }
            return false;
        }

        private static Vector2 FormationOffset(int size, int index)
        {
            if (size == 2) return new Vector2(index == 0 ? -0.5f : 0.5f, 0f);
            if (size == 3) return index == 2 ? new Vector2(0f, Mathf.Sqrt(3f) / 3f)
                : new Vector2(index == 0 ? -0.5f : 0.5f, -Mathf.Sqrt(3f) / 6f);
            if (size == 4) return new Vector2((index & 1) == 0 ? -0.5f : 0.5f, index < 2 ? -0.5f : 0.5f);
            return Vector2.zero;
        }

        public bool InsideArena(Vector2 point, float radius)
        {
            Vector2 delta = point - Settings.ArenaCenter;
            float inset = Mathf.Max(0f, Settings.ArenaBoundaryInset) + Mathf.Max(0f, radius);
            return Mathf.Abs(delta.x) <= Settings.ArenaHalfExtents.x - inset
                && Mathf.Abs(delta.y) <= Settings.ArenaHalfExtents.y - inset;
        }

        private float ViewMargin => Mathf.Max(0f, Settings.OffscreenMargin)
            + CombatCameraFeedback.MaxLeadDistance + CombatCameraFeedback.MaxShakeAmplitude;

        private bool BuildStableView()
        {
            if (gameplayCamera == null || gameplayCamera.orthographic) return false;
            Vector3 position = gameplayCamera.transform.position;
            Quaternion rotation = gameplayCamera.transform.rotation;
            float fov = gameplayCamera.fieldOfView, near = gameplayCamera.nearClipPlane, far = gameplayCamera.farClipPlane;
            if (cameraRig != null)
            {
                if (!cameraRig.PreviousStateIsValid) return false;
                CameraState state = cameraRig.State;
                position = state.RawPosition;
                rotation = state.RawOrientation;
                fov = state.Lens.FieldOfView;
                near = state.Lens.NearClipPlane;
                far = state.Lens.FarClipPlane;
            }
            if (position.y <= Settings.GroundHeight || (rotation * Vector3.forward).y >= -0.01f
                || float.IsNaN(position.sqrMagnitude) || float.IsInfinity(position.sqrMagnitude)
                || fov <= 0f || fov >= 179f || gameplayCamera.aspect <= 0f) return false;
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f))
                * Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
            GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Perspective(fov, gameplayCamera.aspect, near, far) * view, viewPlanes);
            return true;
        }

        private bool TryFindViewExit(Vector3 center, Vector3 extents, Vector3 direction, out float exit)
        {
            exit = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                Plane plane = viewPlanes[i];
                float slope = Vector3.Dot(plane.normal, direction);
                if (slope >= -0.00001f) continue;
                float crossing = (-ViewMargin - Support(plane.normal, extents) - plane.GetDistanceToPoint(center)) / slope;
                exit = Mathf.Min(exit, Mathf.Max(0f, crossing));
            }
            return !float.IsInfinity(exit);
        }

        private static float Support(Vector3 normal, Vector3 extents) => Mathf.Abs(normal.x) * extents.x
            + Mathf.Abs(normal.y) * extents.y + Mathf.Abs(normal.z) * extents.z;

        private bool OutsideView(Bounds bounds)
        {
            for (int i = 0; i < 4; i++)
                if (viewPlanes[i].GetDistanceToPoint(bounds.center) + Support(viewPlanes[i].normal, bounds.extents) < -ViewMargin)
                    return true;
            return false;
        }

        public bool IsSpawnVolumeOutsideView(EnemyArchetypeId id, Vector3 position)
        {
            if (enemyPool == null || !BuildStableView()) return false;
            Bounds bounds = enemyPool.GetSpawnGeometry(Settings.GetArchetype(id)).VisualBounds;
            bounds.center += position;
            return OutsideView(bounds);
        }

        private bool HasGround(Vector3 position, float radius)
        {
            for (int i = 0; i < 5; i++)
            {
                Vector3 offset = i == 0 ? Vector3.zero : i == 1 ? Vector3.right * radius
                    : i == 2 ? Vector3.left * radius : i == 3 ? Vector3.forward * radius : Vector3.back * radius;
                Vector3 start = position + offset;
                start.y = Settings.GroundHeight + 0.1f;
                if (!arenaGround.Raycast(new Ray(start, Vector3.down), out RaycastHit hit, 0.2f)
                    || Mathf.Abs(hit.point.y - Settings.GroundHeight) > 0.02f || hit.normal.y < 0.99f) return false;
            }
            return true;
        }

        private bool PositionIsClear(Vector3 position, EnemySpawnGeometry geometry, int plannedBefore, out DiscardReason failure)
        {
            failure = DiscardReason.Obstructed;
            float radius = geometry.Radius;
            float clearance = Mathf.Max(0f, Settings.SpawnClearance);
            var active = EnemyActor.ActiveEnemies;
            for (int i = 0; i < active.Count; i++)
            {
                EnemyActor actor = active[i];
                if (actor == null || !actor.IsAlive || !actor.gameObject.activeInHierarchy) continue;
                if (TooClose(position, actor.transform.position, radius + actor.HitRadius + clearance)) return false;
            }
            for (int i = 0; i < reservationCount; i++)
                if (TooClose(position, reservationPositions[i], radius + reservationRadii[i] + clearance)) return false;
            for (int i = 0; i < plannedBefore; i++)
                if (TooClose(position, groupPositions[i], radius + groupGeometry[i].Radius + clearance - 0.0001f)) return false;
            if (player != null)
            {
                float playerRadius = player.HurtboxCollider != null
                    ? Mathf.Max(player.HurtboxCollider.bounds.extents.x, player.HurtboxCollider.bounds.extents.z) : 0.5f;
                if (TooClose(position, player.transform.position, radius + playerRadius + clearance)) return false;
            }
            geometry.CapsuleAt(position, out Vector3 bottom, out Vector3 top);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius + clearance, obstacleBuffer,
                Settings.ObstacleMask, QueryTriggerInteraction.Ignore);
            if (count >= obstacleBuffer.Length) { failure = DiscardReason.QuerySaturated; return false; }
            for (int i = 0; i < count; i++)
                if (obstacleBuffer[i] != null && obstacleBuffer[i] != arenaGround) return false;
            failure = DiscardReason.None;
            return true;
        }

        private static bool TooClose(Vector3 a, Vector3 b, float distance)
        {
            Vector3 offset = a - b; offset.y = 0f;
            return offset.sqrMagnitude < distance * distance;
        }
    }
}
