using System;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Oleadas acumuladas sin deuda de apariciones. Reserva cada plaza antes de crear el enemigo y
    /// coloca su volumen fuera del encuadre proyectado, conservando el perímetro físico de la arena.
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

        private readonly Vector2[] footprint = new Vector2[4];
        private readonly Collider[] obstacleBuffer = new Collider[32];
        private readonly Vector3[] reservationPositions = new Vector3[16];
        private readonly float[] reservationRadii = new float[16];
        private readonly DeterministicRandom fallbackRng = new DeterministicRandom(1337);
        private CombatWaveSettings ownedSettings;
        private bool subscribed;
        private int reservationCount;
        private int minionOrderOffset;
        private float footprintWinding;
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
            if (isActiveAndEnabled)
                Subscribe();
            ResetForSession();
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
            int size = Mathf.Clamp(phase.GroupSize, 1, 64);
            for (int i = 0; i < size; i++)
            {
                EnemyArchetypeId id = weights.Choose(RandomSource.NextFloat());
                if (TrySpawn(id, PopulationLimit))
                    LastGroupSpawnedCount++;
                else
                    TotalDiscardedCount++;
            }

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
            for (int i = 0; i < order.Length; i++)
            {
                EnemyArchetypeId id = order[(i + minionOrderOffset) % order.Length];
                if (TrySpawn(id, cap))
                    LastGroupSpawnedCount++;
                else
                    TotalDiscardedCount++;
            }
            minionOrderOffset = (minionOrderOffset + 1) % order.Length;
            return LastGroupSpawnedCount;
        }

        private bool TrySpawn(EnemyArchetypeId id, int cap)
        {
            if (ActiveEnemyCount + reservationCount >= cap || reservationCount >= reservationPositions.Length)
                return false;

            EnemyArchetypeData data = Settings.GetArchetype(id);
            if (!TryFindSpawnPosition(data, out Vector3 position))
                return false;

            int reservation = reservationCount++;
            reservationPositions[reservation] = position;
            reservationRadii[reservation] = GetRadius(data);
            try
            {
                EnemyActor actor = enemyPool.Spawn(data, position);
                if (actor == null)
                    return false;
                TotalSpawnedCount++;
                return true;
            }
            finally
            {
                reservationCount--;
            }
        }

        public bool TryFindSpawnPosition(EnemyArchetypeId id, out Vector3 position)
        {
            return TryFindSpawnPosition(Settings.GetArchetype(id), out position);
        }

        private bool TryFindSpawnPosition(EnemyArchetypeData data, out Vector3 position)
        {
            position = default;
            if (player == null || gameplayCamera == null || !BuildCameraFootprint())
                return false;

            Vector3 playerPosition = player.transform.position;
            Vector2 origin = new Vector2(playerPosition.x, playerPosition.z);
            float radius = GetRadius(data);
            float margin = Mathf.Max(0f, Settings.OffscreenMargin) + radius;
            float minimum = Mathf.Max(0f, Settings.BaselineMinimumDistance);
            float maximum = Mathf.Max(minimum, Settings.BaselineMaximumDistance);
            int attempts = Mathf.Clamp(Settings.PositionAttempts, 1, 16);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                float angle = RandomSource.Range(0f, Mathf.PI * 2f);
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float distance = RandomSource.Range(minimum, maximum);
                if (TryFindFootprintExit(origin, direction, margin, out float exitDistance))
                    distance = Mathf.Max(distance, exitDistance + 0.05f);

                Vector2 candidate = origin + direction * distance;
                if (!InsideArena(candidate, radius) || !OutsideExpandedFootprint(candidate, margin))
                    continue;

                Vector3 candidateWorld = new Vector3(candidate.x, 1f, candidate.y);
                if (!PositionIsClear(candidateWorld, data, radius))
                    continue;

                position = candidateWorld;
                return true;
            }
            return false;
        }

        private static float GetRadius(EnemyArchetypeData data)
        {
            return Mathf.Max(0.05f, data.ColliderRadius
                * Mathf.Max(Mathf.Abs(data.VisualScale.x), Mathf.Abs(data.VisualScale.z)));
        }

        public bool InsideArena(Vector2 point, float radius)
        {
            Vector2 half = Settings.ArenaHalfExtents;
            Vector2 delta = point - Settings.ArenaCenter;
            float inset = Mathf.Max(0f, Settings.ArenaBoundaryInset) + Mathf.Max(0f, radius);
            return Mathf.Abs(delta.x) <= half.x - inset && Mathf.Abs(delta.y) <= half.y - inset;
        }

        private bool BuildCameraFootprint()
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, Settings.GroundHeight, 0f));
            for (int i = 0; i < 4; i++)
            {
                float x = i == 1 || i == 2 ? 1f : 0f;
                float y = i >= 2 ? 1f : 0f;
                Ray ray = gameplayCamera.ViewportPointToRay(new Vector3(x, y, 0f));
                if (!plane.Raycast(ray, out float distance) || distance < 0f)
                    return false;
                Vector3 point = ray.GetPoint(distance);
                footprint[i] = new Vector2(point.x, point.z);
            }

            float area = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = footprint[i];
                Vector2 b = footprint[(i + 1) % 4];
                area += a.x * b.y - a.y * b.x;
            }
            if (Mathf.Abs(area) < 0.0001f)
                return false;
            footprintWinding = Mathf.Sign(area);
            return true;
        }

        // Recorte de un rayo contra las cuatro semiplanos del encuadre expandido.
        private bool TryFindFootprintExit(Vector2 origin, Vector2 direction, float margin, out float exit)
        {
            float entry = 0f;
            exit = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                Vector2 edge = footprint[(i + 1) % 4] - footprint[i];
                Vector2 inward = new Vector2(-edge.y, edge.x).normalized * footprintWinding;
                float signedDistance = Vector2.Dot(inward, origin - footprint[i]);
                float slope = Vector2.Dot(inward, direction);
                if (Mathf.Abs(slope) < 0.00001f)
                {
                    if (signedDistance < -margin)
                        return false;
                    continue;
                }

                float crossing = (-margin - signedDistance) / slope;
                if (slope > 0f)
                    entry = Mathf.Max(entry, crossing);
                else
                    exit = Mathf.Min(exit, crossing);
                if (exit < entry)
                    return false;
            }
            return !float.IsInfinity(exit) && exit >= entry;
        }

        private bool OutsideExpandedFootprint(Vector2 point, float margin)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector2 edge = footprint[(i + 1) % 4] - footprint[i];
                Vector2 inward = new Vector2(-edge.y, edge.x).normalized * footprintWinding;
                if (Vector2.Dot(inward, point - footprint[i]) < -margin)
                    return true;
            }
            return false;
        }

        private bool PositionIsClear(Vector3 position, EnemyArchetypeData data, float radius)
        {
            float clearance = Mathf.Max(0f, Settings.SpawnClearance);
            var active = EnemyActor.ActiveEnemies;
            for (int i = 0; i < active.Count; i++)
            {
                EnemyActor actor = active[i];
                if (actor == null || !actor.IsAlive || !actor.gameObject.activeInHierarchy)
                    continue;
                float safeDistance = radius + actor.HitRadius + clearance;
                Vector3 offset = actor.transform.position - position;
                offset.y = 0f;
                if (offset.sqrMagnitude < safeDistance * safeDistance)
                    return false;
            }
            for (int i = 0; i < reservationCount; i++)
            {
                float safeDistance = radius + reservationRadii[i] + clearance;
                Vector3 offset = reservationPositions[i] - position;
                offset.y = 0f;
                if (offset.sqrMagnitude < safeDistance * safeDistance)
                    return false;
            }

            if (player != null)
            {
                float playerRadius = player.HurtboxCollider != null
                    ? Mathf.Max(player.HurtboxCollider.bounds.extents.x, player.HurtboxCollider.bounds.extents.z)
                    : 0.5f;
                Vector3 offset = player.transform.position - position;
                offset.y = 0f;
                float safeDistance = radius + playerRadius + clearance;
                if (offset.sqrMagnitude < safeDistance * safeDistance)
                    return false;
            }

            float halfHeight = Mathf.Max(radius, data.ColliderHeight * Mathf.Abs(data.VisualScale.y) * 0.5f);
            float segment = Mathf.Max(0f, halfHeight - radius);
            int count = Physics.OverlapCapsuleNonAlloc(position - Vector3.up * segment,
                position + Vector3.up * segment, radius + clearance, obstacleBuffer,
                Settings.ObstacleMask, QueryTriggerInteraction.Ignore);
            if (count >= obstacleBuffer.Length)
                return false;
            for (int i = 0; i < count; i++)
            {
                Collider obstacle = obstacleBuffer[i];
                if (obstacle == null || obstacle.bounds.max.y <= Settings.GroundHeight + 0.15f)
                    continue; // El suelo no impide crear los actores de altura fija.
                return false;
            }
            return true;
        }
    }
}
