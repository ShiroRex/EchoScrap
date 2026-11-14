using System;
using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Base compartida de enemigos (Residual, Interceptor, Coloso, Mortero, Polilla Explosiva).
    /// Resuelve locomoción con Rigidbody en FixedUpdate, apoyo sobre el relieve, separación y rutas compartidas,
    /// daño por contacto de colliders reales, empuje (impulso * susceptibilidad y frenado),
    /// reacción visual / hit-stop local mediante ActorVisualComposer y reciclaje íntegro en pool.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class EnemyActor : MonoBehaviour, IDamageTarget
    {
        public const float MortarMinBandRange = 5.5f;
        public const float MortarMaxBandRange = 6.5f;
        public const float MortarFireInterval = 5f;
        public const float MortarTelegraphLeadTime = 0.45f;
        private MortarAttackData MortarAttack => runtimeArchetype.MortarAttack.FireInterval > 0f
            ? runtimeArchetype.MortarAttack : MortarAttackData.Default;
        public const float MothFuseDuration = 0.9f;
        public const float MothArmDistance = 2f;

        private static readonly List<EnemyActor> activeEnemies = new List<EnemyActor>(256);
        private static readonly EnemyNeighborIndex neighborIndex=new EnemyNeighborIndex();
        private static readonly List<int> neighborCandidates=new List<int>(64);
        private static double neighborStep = double.NaN;
        private static bool neighborsCapturedInPhysics;
        private static float largestHitRadius;
        private static void InvalidateNeighbors() => neighborStep = double.NaN;
        private static void RefreshNeighborIndex()
        {
            bool inPhysics = Time.inFixedTimeStep;
            if (neighborStep == Time.fixedTimeAsDouble && neighborsCapturedInPhysics == inPhysics) return;
            neighborIndex.Clear();
            largestHitRadius = 0f;
            for (int i = 0; i < activeEnemies.Count; i++)
            {
                EnemyActor enemy = activeEnemies[i];
                neighborIndex.Add(enemy != null ? enemy.WorldCenter : Vector3.zero);
                if (enemy != null) largestHitRadius = Mathf.Max(largestHitRadius, enemy.HitRadius);
            }
            neighborStep = Time.fixedTimeAsDouble;
            neighborsCapturedInPhysics = inPhysics;
        }

        internal static void GatherSegmentCandidates(Vector3 start, Vector3 end, float radius, List<EnemyActor> results)
        {
            RefreshNeighborIndex();
            Vector3 delta = end - start;
            delta.y = 0f;
            neighborIndex.Query(start + delta * .5f, delta.magnitude * .5f + radius + largestHitRadius + .02f, neighborCandidates);
            results.Clear();
            foreach (int index in neighborCandidates)
                results.Add(activeEnemies[index]);
        }

        internal static bool TouchesSegment(Vector3 from, Vector3 to, Vector3 center, float radius, out float fraction)
        {
            Vector3 delta = to - from;
            Vector3 offset = center - from;
            delta.y = offset.y = 0f;
            float lengthSquared = delta.sqrMagnitude;
            fraction = lengthSquared > .000001f ? Mathf.Clamp01(Vector3.Dot(offset, delta) / lengthSquared) : 0f;
            return (offset - delta * fraction).sqrMagnitude <= radius * radius;
        }

        private static readonly Dictionary<int, Material> sharedPrimaryMaterials = new Dictionary<int, Material>();
        private static readonly Dictionary<int, Material> sharedSecondaryMaterials = new Dictionary<int, Material>();
        public static IReadOnlyList<EnemyActor> ActiveEnemies => activeEnemies;

        [SerializeField] private EnemyArchetypeSettings archetypeSettings;
        [SerializeField] private EnemyArchetypeData runtimeArchetype = EnemyArchetypeData.CreateDefault(EnemyArchetypeId.Residual);
        [SerializeField] private CombatSession session;
        [SerializeField] private PlayerHealth targetPlayer;
        [SerializeField] private ActorVisualComposer visualComposer;
        [SerializeField] private CorrosivePuddlePool puddlePool;

        [Header("Separación Local y Evitación")]
        [SerializeField, Min(0.2f)] private float separationRadius = 1.15f;
        [SerializeField, Min(0f)] private float separationWeight = 2.6f;
        [SerializeField, Min(0.2f)] private float obstacleLookAhead = 1.1f;
        [Header("Interceptor")]
        [SerializeField, Min(0f)] private float interceptorPredictionSeconds = 0.4f;
        [SerializeField, Min(0f)] private float interceptorPredictionSpeedCap = 8f;
        [SerializeField, Min(0f)] private float interceptorDirectPursuitDistance = 1.2f;

        private Rigidbody body;
        private CapsuleCollider bodyCollider;
        private Rigidbody targetBody;
        private readonly EnemyObstacleNavigator obstacleNavigator = new EnemyObstacleNavigator();

        private float currentHealth;
        private float assignedMoveSpeed;
        private Vector3 knockbackVelocity;
        private float wallSlamTimeRemaining;
        private int wallSlamTier;
        private static readonly List<EnemyActor> wallSlamCandidates = new List<EnemyActor>(64);
        private Vector3 currentDesiredVelocity;
        private Vector3 facingDirection = Vector3.forward;

        private float nextContactReadyTime;
        private float attackCooldownTimer;
        private float fuseTimer;
        private float telegraphTimer;
        private bool isFuseActive;
        private bool isDead;
        private bool deathRewardResolved;
        private bool initialized;
        private MothFusePresentation mothPresentation;
        private Prototype.EnemyMoth prototypeMoth;
        private MothExplosionPool mothExplosions;
        private const float InterceptorAttackVolume = 0.6f;
        private AudioSource interceptorSound;
        private AudioClip interceptorSoundClip;
        private const float MothSoundVolume = 0.6f;
        private AudioSource mothSound;
        private AudioClip mothSoundClip;
        private MortarSpriteView mortarView;
        private EnemySpriteView groundSpriteView;
        private Transform mortarEmitter;
        private Vector3 mortarEmitterRestPosition;
        private Vector3 mortarEmitterRestScale;
        private float mortarRecoilRemaining;
        private ColossusCharge colossusCharge;
        private EliteEnemyController elite;
        private readonly List<Renderer> archetypeRenderers = new List<Renderer>(8);
        private bool materialsConfigured;
        private EnemyArchetypeId presentedArchetypeId;
        private Color presentedPrimaryColor;
        private Color presentedSecondaryColor;

        public Action<EnemyActor> ReturnToPoolCallback { get; set; }

        public int TargetId => gameObject.GetHashCode();
        public EnemyArchetypeId ArchetypeId => runtimeArchetype.ArchetypeId;
        public EnemyArchetypeData Archetype => runtimeArchetype;
        public float CurrentHealth => EnsureInitialized() ? currentHealth : 0f;
        public float MaxHealth => runtimeArchetype.MaxHealth;
        public float AssignedMoveSpeed => assignedMoveSpeed;
        public Vector3 KnockbackVelocity => knockbackVelocity;
        public Vector3 Velocity => body != null ? body.linearVelocity : Vector3.zero;
        public Vector3 FacingDirection => facingDirection;
        public bool IsAlive => EnsureInitialized() && !isDead && (currentHealth > 0f || isFuseActive);
        public bool IsDead => !IsAlive;
        public bool IsEnemyTarget => true;
        public bool DeathRewardResolved => deathRewardResolved;
        public int DeathNotifyCount { get; private set; }
        public int KnockbackAppliedCount { get; private set; }
        public int MortarShotsFiredCount { get; private set; }
        public float NextContactReadyTime => nextContactReadyTime;
        public float FuseTimer => fuseTimer;
        public float AttackCooldownTimer => attackCooldownTimer;
        public float TelegraphTimer => telegraphTimer;
        public bool IsMortarCharging => ArchetypeId == EnemyArchetypeId.Mortar && telegraphTimer > 0f;
        public float MortarChargeProgress => Mathf.Clamp01(telegraphTimer /
            Mathf.Clamp(MortarAttack.WarningDuration, 0.1f, Mathf.Max(0.5f, MortarAttack.FireInterval)));
        public bool IsFuseActive => isFuseActive;
        public Vector3 WorldCenter => transform.position;
        public CapsuleCollider BodyCollider => bodyCollider;
        public Rigidbody Body => body;
        /// <summary>Runtime overrides used by the laboratory; reset whenever this actor is reused.</summary>
        public bool PreventLethalDamage { get; set; }
        public bool SimulationSuspended { get; set; }
        public ActorVisualComposer VisualComposer => visualComposer;
        public CombatSession Session => session;
        public PlayerHealth TargetPlayer => targetPlayer;
        public ColossusCharge Charge => colossusCharge;
        public EliteEnemyController Elite => elite;
        public CorrosivePuddlePool PuddlePool => puddlePool;

        public void BindPuddlePool(CorrosivePuddlePool pool)
        {
            puddlePool = pool;
        }

        private CorrosivePuddlePool EnsurePuddlePool()
        {
            if (puddlePool != null)
            {
                puddlePool.BindReferences(session, targetPlayer);
                return puddlePool;
            }

            puddlePool = FindAnyObjectByType<CorrosivePuddlePool>();
            if (puddlePool == null)
            {
                var poolGo = new GameObject("CorrosivePuddlePool");
                puddlePool = poolGo.AddComponent<CorrosivePuddlePool>();
            }

            puddlePool.BindReferences(session, targetPlayer);
            return puddlePool;
        }

        public float HitRadius
        {
            get
            {
                EnsureInitialized();
                Vector3 scale = transform.lossyScale;
                float scaleXZ = Mathf.Max(scale.x, scale.z);
                return (bodyCollider != null ? bodyCollider.radius : runtimeArchetype.ColliderRadius) * scaleXZ;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        public bool EnsureInitialized()
        {
            if (initialized && body != null && bodyCollider != null && visualComposer != null)
                return true;

            initialized = true;
            PhysicsLayers.EnsureConfigured();

            if (archetypeSettings != null)
                runtimeArchetype = archetypeSettings.Data;
            else if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.None)
                runtimeArchetype = EnemyArchetypeData.CreateDefault(EnemyArchetypeId.Residual);

            body = GetComponent<Rigidbody>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;

            bodyCollider = GetComponent<CapsuleCollider>();
            if (bodyCollider == null)
                bodyCollider = gameObject.AddComponent<CapsuleCollider>();

            EnsureVisualHierarchy();

            if (session == null)
                session = FindAnyObjectByType<CombatSession>();
            if (targetPlayer == null)
                targetPlayer = session != null && session.PlayerHealth != null
                    ? session.PlayerHealth
                    : FindAnyObjectByType<PlayerHealth>();
            targetBody = targetPlayer != null ? targetPlayer.GetComponent<Rigidbody>() : null;

            return true;
        }

        private void EnsureVisualHierarchy()
        {
            Transform visualRoot = transform.Find("Visual");
            if (visualRoot == null)
            {
                var visualGo = new GameObject("Visual");
                visualGo.transform.SetParent(transform, false);
                visualRoot = visualGo.transform;

                var bodyMesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                bodyMesh.name = "BodyMesh";
                bodyMesh.transform.SetParent(visualRoot, false);
                DestroyImmediate(bodyMesh.GetComponent<Collider>());

                var noseMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                noseMesh.name = "FacingCrest";
                noseMesh.transform.SetParent(visualRoot, false);
                noseMesh.transform.localPosition = new Vector3(0f, 0.35f, 0.42f);
                noseMesh.transform.localScale = new Vector3(0.28f, 0.22f, 0.38f);
                DestroyImmediate(noseMesh.GetComponent<Collider>());
            }

            if (visualComposer == null)
                visualComposer = GetComponent<ActorVisualComposer>();
            if (visualComposer == null)
                visualComposer = gameObject.AddComponent<ActorVisualComposer>();

            visualComposer.EnsureHierarchy();
            if (visualComposer.PresentationPivot != null)
                mortarEmitter = visualComposer.PresentationPivot.Find("FacingCrest");
            ApplyArchetypeMaterials();
            visualComposer.CacheRenderers();
        }

        private void ApplyArchetypeMaterials()
        {
            if (visualComposer == null || visualComposer.PresentationPivot == null)
                return;
            if (materialsConfigured && presentedArchetypeId == runtimeArchetype.ArchetypeId
                && presentedPrimaryColor == runtimeArchetype.PrimaryColor
                && presentedSecondaryColor == runtimeArchetype.SecondaryColor)
                return;

            int key = (int)runtimeArchetype.ArchetypeId;
            if (!sharedPrimaryMaterials.TryGetValue(key, out Material primaryMat) || primaryMat == null)
            {
                Shader litShader = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Standard");
                primaryMat = new Material(litShader)
                {
                    name = $"EnemyPrimary_{runtimeArchetype.ArchetypeId}",
                    color = runtimeArchetype.PrimaryColor
                };
                sharedPrimaryMaterials[key] = primaryMat;
            }
            else
            {
                primaryMat.color = runtimeArchetype.PrimaryColor;
            }

            if (!sharedSecondaryMaterials.TryGetValue(key, out Material secondaryMat) || secondaryMat == null)
            {
                Shader litShader = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Standard");
                secondaryMat = new Material(litShader)
                {
                    name = $"EnemySecondary_{runtimeArchetype.ArchetypeId}",
                    color = runtimeArchetype.SecondaryColor
                };
                sharedSecondaryMaterials[key] = secondaryMat;
            }
            else
            {
                secondaryMat.color = runtimeArchetype.SecondaryColor;
            }

            Transform crest = visualComposer.PresentationPivot.Find("FacingCrest");
            if (crest != null)
            {
                switch (runtimeArchetype.ArchetypeId)
                {
                    case EnemyArchetypeId.Interceptor:
                        crest.localPosition = new Vector3(0f, 0.15f, 0.52f);
                        crest.localScale = new Vector3(0.36f, 0.20f, 0.65f);
                        break;
                    case EnemyArchetypeId.Colossus:
                        crest.localPosition = new Vector3(0f, 0.55f, 0.35f);
                        crest.localScale = new Vector3(0.72f, 0.36f, 0.45f);
                        break;
                    case EnemyArchetypeId.Mortar:
                        crest.localPosition = new Vector3(0f, 0.65f, 0.15f);
                        crest.localScale = new Vector3(0.35f, 0.55f, 0.55f);
                        mortarEmitter = crest;
                        mortarEmitterRestPosition = crest.localPosition;
                        mortarEmitterRestScale = crest.localScale;
                        break;
                    case EnemyArchetypeId.ExplosiveMoth:
                        crest.localPosition = new Vector3(0f, 0.25f, -0.32f);
                        crest.localScale = new Vector3(0.58f, 0.48f, 0.62f);
                        break;
                    default:
                        crest.localPosition = new Vector3(0f, 0.35f, 0.42f);
                        crest.localScale = new Vector3(0.28f, 0.22f, 0.38f);
                        break;
                }
            }

            archetypeRenderers.Clear();
            visualComposer.PresentationPivot.GetComponentsInChildren(true, archetypeRenderers);
            for (int i = 0; i < archetypeRenderers.Count; i++)
            {
                Renderer r = archetypeRenderers[i];
                if (r == null)
                    continue;

                if (r.name == "Polilla Reactor Pixels" || r.name == "Mortero Campana Pixels" || r.name == "Enemy Sprite Pixels") continue;
                if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.GolgoraBoss && r.GetComponentInParent<Animator>() != null) continue;

                r.sharedMaterial = r.gameObject.name.Contains("Crest") ? secondaryMat : primaryMat;
            }
            presentedArchetypeId = runtimeArchetype.ArchetypeId;
            presentedPrimaryColor = runtimeArchetype.PrimaryColor;
            presentedSecondaryColor = runtimeArchetype.SecondaryColor;
            materialsConfigured = true;
        }

        private void OnEnable()
        {
            EnsureInitialized();
            if (!activeEnemies.Contains(this))
                activeEnemies.Add(this);
            InvalidateNeighbors();
            if (IsAlive)
                CombatTargetRegistry.Register(this);
        }

        private void OnDisable()
        {
            wallSlamTimeRemaining = 0f;
            obstacleNavigator.Reset();
            UnregisterFromCombat();
            if (body != null && !body.isKinematic)
                body.linearVelocity = Vector3.zero;
        }

        internal void UnregisterFromCombat()
        {
            activeEnemies.Remove(this);
            InvalidateNeighbors();
            CombatTargetRegistry.Unregister(this);
        }

        /// <summary>
        /// Configura y restaura completamente la instancia al salir del pool o crearse.
        /// </summary>
        public void InitializeOrResetFromPool(
            EnemyArchetypeData data,
            CombatSession combatSession,
            PlayerHealth player,
            Vector3 spawnPosition)
        {
            ConfigurePoolReferences(data, combatSession, player);
            ResetForSpawn(spawnPosition);
        }

        /// <summary>Builds reusable views and references without sampling any gameplay randomness.</summary>
        public void PrepareForPool(EnemyArchetypeData data, CombatSession combatSession, PlayerHealth player)
        {
            ConfigurePoolReferences(data, combatSession, player);
            currentHealth = 0f;
            isDead = true;
            assignedMoveSpeed = runtimeArchetype.BaseMoveSpeed;
            bodyCollider.enabled = false;
            body.isKinematic = true;
            BindGroundSpriteView();
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Mortar)
            {
                EnsurePuddlePool();
                transform.localScale = runtimeArchetype.VisualScale;
                mortarView = GetComponent<MortarSpriteView>() ?? gameObject.AddComponent<MortarSpriteView>();
                mortarView.Bind(this);
            }
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.ExplosiveMoth)
            {
                transform.localScale = runtimeArchetype.VisualScale;
                mothPresentation = GetComponent<MothFusePresentation>() ?? gameObject.AddComponent<MothFusePresentation>();
                mothPresentation.ResetPresentation();
                prototypeMoth = GetComponent<Prototype.EnemyMoth>() ?? gameObject.AddComponent<Prototype.EnemyMoth>();
                prototypeMoth.Bind(this);
                mothExplosions = MothExplosionPool.GetOrCreate(session, targetPlayer);
                (GetComponent<MothSpriteView>() ?? gameObject.AddComponent<MothSpriteView>()).Bind(this);
            }
        }

        private void BindGroundSpriteView()
        {
            if (!EnemySpriteSet.Supports(runtimeArchetype.ArchetypeId)) return;
            transform.localScale = runtimeArchetype.VisualScale;
            groundSpriteView = GetComponent<EnemySpriteView>() ?? gameObject.AddComponent<EnemySpriteView>();
            groundSpriteView.Bind(this);
        }

        public Bounds CaptureSpawnModelBounds()
        {
            visualComposer.PresentationPivot.GetComponentsInChildren(true, archetypeRenderers);
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            for (int i = 0; i < archetypeRenderers.Count; i++)
            {
                Renderer view = archetypeRenderers[i];
                if (view == null || !view.enabled) continue;
                if (!(view is MeshRenderer) && !(view is SkinnedMeshRenderer)) continue;
                Bounds local = view.localBounds;
                Matrix4x4 toActor = transform.worldToLocalMatrix * view.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = local.center + Vector3.Scale(local.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    bounds.Encapsulate(toActor.MultiplyPoint3x4(point));
                }
            }
            return bounds;
        }

        private void ConfigurePoolReferences(EnemyArchetypeData data, CombatSession combatSession, PlayerHealth player)
        {
            runtimeArchetype = data;
            session = combatSession;
            targetPlayer = player;
            targetBody = player != null ? player.GetComponent<Rigidbody>() : null;
            EnsureInitialized();
            ApplyArchetypeMaterials();
            visualComposer.CacheRenderers();

            if (data.ArchetypeId == EnemyArchetypeId.Interceptor)
            {
                var motion = GetComponent<ActorMotionPresentation>() ?? gameObject.AddComponent<ActorMotionPresentation>();
                motion.Bind(session, visualComposer, this);
            }
            if (data.ArchetypeId == EnemyArchetypeId.Colossus)
            {
                colossusCharge = GetComponent<ColossusCharge>() ?? gameObject.AddComponent<ColossusCharge>();
                colossusCharge.Bind(this);
            }
            if (data.ArchetypeId == EnemyArchetypeId.GhostKnight || data.ArchetypeId == EnemyArchetypeId.CopperSentinel)
            {
                elite = GetComponent<EliteEnemyController>() ?? gameObject.AddComponent<EliteEnemyController>();
                elite.Bind(this);
            }
        }

        /// <summary>
        /// Contrato estricto de restauración al salir del pool o reiniciarse (Sección 2.5):
        /// - Salud máxima.
        /// - Velocidades y empuje a cero.
        /// - Temporizadores de contacto, disparo, mecha o telegrafía.
        /// - Colliders, triggers y capas.
        /// - Escala, rotación y MaterialPropertyBlock.
        /// - Estado de muerte y recompensas.
        /// </summary>
        private float slowFraction;
        private float cableSlow, cableSlowRemaining, cableDamageReadyAt;
        public float SlowFraction => Mathf.Max(slowFraction, cableSlow);
        public float SlowRemaining { get; private set; }
        public float FrozenRemaining { get; private set; }
        public float StunRemaining { get; private set; }
        public bool IsFrozen => FrozenRemaining > 0f;
        public bool IsControlLocked => IsFrozen || StunRemaining > 0f;
        public int SpawnVersion { get; private set; }
        public float FrozenVisualCoverage { get; private set; }
        private int coldHits;
        public Vector3 PursuitGoal => session != null && session.Upgrades != null ? session.Upgrades.PursuitGoal(transform.position, targetPlayer != null ? targetPlayer.WorldCenter : transform.position) : (targetPlayer != null ? targetPlayer.WorldCenter : transform.position);
        public virtual void ApplySlow(float fraction, float duration) { slowFraction = Mathf.Max(slowFraction, Mathf.Clamp01(fraction)); SlowRemaining = Mathf.Max(SlowRemaining, duration); RefreshColdTint(); }
        internal bool TouchElectricCable(float time, float interval, float fraction)
        {
            cableSlow = LimitCableSlow(fraction);
            cableSlowRemaining = .08f;
            if (time < cableDamageReadyAt) return false;
            cableDamageReadyAt = time + Mathf.Max(.01f, interval);
            return true;
        }

        protected virtual float LimitCableSlow(float fraction) => fraction;

        public virtual void ApplyFreeze(float duration)
        {
            FrozenRemaining = Mathf.Max(FrozenRemaining, duration);
            if (FrozenRemaining > 0f) FrozenVisualCoverage = Mathf.Max(.12f, FrozenVisualCoverage);
            coldHits = 0;
            RefreshColdTint();
        }
        public virtual void ApplyStun(float duration) => StunRemaining = Mathf.Max(StunRemaining, duration);
        public void RegisterColdHit(int required, float duration) { if (SlowRemaining > 0f && !IsFrozen && ++coldHits >= required) ApplyFreeze(duration); }
        public virtual void StepUpgradeStatus(float dt)
        {
            SlowRemaining = Mathf.Max(0f, SlowRemaining - dt);
            FrozenRemaining = Mathf.Max(0f, FrozenRemaining - dt);
            StunRemaining = Mathf.Max(0f, StunRemaining - dt);
            if (SlowRemaining <= 0f) { slowFraction = 0f; coldHits = 0; }
            cableSlowRemaining = Mathf.Max(0f, cableSlowRemaining - dt);
            if (cableSlowRemaining <= 0f) cableSlow = 0f;
            FrozenVisualCoverage = Mathf.MoveTowards(FrozenVisualCoverage, IsFrozen ? 1f : 0f, Mathf.Max(0f, dt) / (IsFrozen ? .14f : .12f));
            RefreshColdTint();
        }
        private void RefreshColdTint()
        {
            Color baseTint = SlowRemaining > 0f ? new Color(.38f, .78f, 1f) : Color.white;
            visualComposer?.SetStatusTint(Color.Lerp(baseTint, new Color(.65f, .88f, 1f), FrozenVisualCoverage));
        }
        protected void ClearCrowdControl()
        {
            SlowRemaining = FrozenRemaining = StunRemaining = slowFraction = FrozenVisualCoverage = 0f;
            cableSlow = cableSlowRemaining = 0f;
            wallSlamTimeRemaining = 0f;
            coldHits = 0;
            RefreshColdTint();
        }
        protected virtual float IncomingDamageMultiplier(DamageType type) => elite != null ? elite.DamageMultiplier(type) : 1f;
        public void PushDistance(Vector3 direction, float distance) => ApplyKnockback(direction.normalized * Mathf.Sqrt(2f * Mathf.Max(1f, runtimeArchetype.KnockbackBraking) * distance));

        public virtual void ResetForSpawn(Vector3 spawnPosition)
        {
            EnsureInitialized();
            PreventLethalDamage = false;
            SimulationSuspended = false;
            PhysicsLayers.EnsureConfigured();

            // 1. Salud máxima y estado de muerte/recompensas
            SpawnVersion++; SlowRemaining = FrozenRemaining = StunRemaining = slowFraction = FrozenVisualCoverage = 0f; coldHits = 0;
            cableSlow = cableSlowRemaining = cableDamageReadyAt = 0f;
            currentHealth = Mathf.Max(1f, runtimeArchetype.MaxHealth);
            isDead = false;
            deathRewardResolved = false;
            DeathNotifyCount = 0;
            KnockbackAppliedCount = 0;
            MortarShotsFiredCount = 0;

            // 2. Muestreo determinista de velocidad inicial (p.ej. Residual 3.5 m/s ±15%)
            float variance = Mathf.Clamp(runtimeArchetype.SpeedVarianceFraction, 0f, 0.5f);
            if (variance > 0.0001f)
            {
                DeterministicRandom rng = session != null ? session.Rng.Combat : new DeterministicRandom(1337);
                float mult = rng.Range(1f - variance, 1f + variance);
                assignedMoveSpeed = runtimeArchetype.BaseMoveSpeed * mult;
            }
            else
            {
                assignedMoveSpeed = runtimeArchetype.BaseMoveSpeed;
            }

            // 3. Velocidades y empuje a cero
            wallSlamTimeRemaining = 0f;
            knockbackVelocity = Vector3.zero;
            currentDesiredVelocity = Vector3.zero;
            facingDirection = Vector3.forward;
            obstacleNavigator.Reset();

            // 4. Temporizadores de contacto, disparo, mecha y telegrafía a cero
            nextContactReadyTime = 0f;
            attackCooldownTimer = 0f;
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Mortar)
            {
                DeterministicRandom attackRng = session != null ? session.Rng.Combat : new DeterministicRandom(1337);
                float interval = Mathf.Max(0.5f, MortarAttack.FireInterval);
                float warning = Mathf.Clamp(MortarAttack.WarningDuration, 0.1f, interval);
                float minimumDelay = Mathf.Max(warning, Mathf.Min(1f, interval));
                float maximumDelay = Mathf.Max(minimumDelay, Mathf.Min(2f, interval));
                attackCooldownTimer = interval - attackRng.Range(minimumDelay, maximumDelay);
                EnsurePuddlePool();
                mortarRecoilRemaining = 0f;
                if (mortarEmitter != null)
                {
                    mortarEmitter.localPosition = mortarEmitterRestPosition;
                    mortarEmitter.localScale = mortarEmitterRestScale;
                }
            }
            fuseTimer = 0f;
            telegraphTimer = 0f;
            isFuseActive = false;
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.ExplosiveMoth)
            {
                mothPresentation = GetComponent<MothFusePresentation>();
                if (mothPresentation == null)
                    mothPresentation = gameObject.AddComponent<MothFusePresentation>();
                mothPresentation.ResetPresentation();
                prototypeMoth = GetComponent<Prototype.EnemyMoth>() ?? gameObject.AddComponent<Prototype.EnemyMoth>();
                prototypeMoth.Bind(this);
                mothExplosions = MothExplosionPool.GetOrCreate(session, targetPlayer);
                (GetComponent<MothSpriteView>() ?? gameObject.AddComponent<MothSpriteView>()).Bind(this);
            }

            // 5. Colliders, triggers y capas
            gameObject.layer = PhysicsLayers.EnemyLayer;
            gameObject.tag = PhysicsLayers.EnemyTag;
            if (bodyCollider != null)
            {
                bodyCollider.radius = runtimeArchetype.ColliderRadius;
                bodyCollider.height = runtimeArchetype.ColliderHeight;
                bodyCollider.isTrigger = false;
                bodyCollider.enabled = true;
                if (targetPlayer != null && targetPlayer.HurtboxCollider != null)
                {
                    Physics.IgnoreCollision(bodyCollider, targetPlayer.HurtboxCollider, !runtimeArchetype.IsHeavyMass);
                }
            }

            // 6. Escala, rotación, posición y MaterialPropertyBlock
            Vector3 clampedSpawn = new Vector3(spawnPosition.x, 1f, spawnPosition.z);
            clampedSpawn.y += ArenaSurface.SupportHeight(clampedSpawn, HitRadius);
            transform.localScale = runtimeArchetype.VisualScale;
            // The heavy capsule scales to 3.2 m tall. Keep its bottom on the floor at fixed root Y=1.
            bodyCollider.center = EnemySpawnGeometry.LocalColliderCenter(runtimeArchetype);
            transform.SetPositionAndRotation(clampedSpawn, Quaternion.identity);
            if (targetPlayer != null)
            {
                Vector3 toTarget = PursuitGoal - clampedSpawn;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.001f)
                    facingDirection = toTarget.normalized;
            }

            if (body != null)
            {
                body.isKinematic = false;
                body.mass = runtimeArchetype.IsHeavyMass ? 500f : 1f;
                body.position = clampedSpawn;
                body.rotation = Quaternion.identity;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            if (visualComposer != null)
            {
                bool immune = runtimeArchetype.KnockbackSusceptibility <= 0.0001f;
                visualComposer.ConfigureActorMass(runtimeArchetype.IsHeavyMass, immune, 0.03f);
                float tilt = runtimeArchetype.ArchetypeId == EnemyArchetypeId.Interceptor
                    ? 12f
                    : (runtimeArchetype.IsHeavyMass ? 3f : 6f);
                visualComposer.SetLocomotionTiltDegrees(tilt);
                if (visualComposer.OrientationRoot != null)
                {
                    visualComposer.OrientationRoot.gameObject.SetActive(true);
                    visualComposer.OrientationRoot.rotation = Quaternion.LookRotation(facingDirection, Vector3.up);
                }
                visualComposer.ResetVisualState();
            }

            colossusCharge?.ResetForSpawn();
            elite?.ResetForSpawn();
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            if (!activeEnemies.Contains(this))
                activeEnemies.Add(this);
            InvalidateNeighbors();
            CombatTargetRegistry.Register(this);
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.ExplosiveMoth)
                GetComponent<MothSpriteView>()?.Advance(0f);
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Mortar)
            {
                mortarView = GetComponent<MortarSpriteView>() ?? gameObject.AddComponent<MortarSpriteView>();
                mortarView.Bind(this);
            }
            BindGroundSpriteView();
            Physics.SyncTransforms();
        }

        private void FixedUpdate()
        {
            EnsureInitialized();
            if (isDead || SimulationSuspended)
            {
                if (body != null && !body.isKinematic) body.linearVelocity = Vector3.zero;
                return;
            }

            if ((session != null && (session.IsPaused || !session.IsGameplayFlowActive())) || Time.timeScale <= 0f)
            {
                if (body != null && !body.isKinematic)
                    body.linearVelocity = Vector3.zero;
                return;
            }

            StepPhysics(Time.fixedDeltaTime);
        }

        public virtual void StepPhysics(float fixedDeltaTime)
        {
            if (isDead || fixedDeltaTime <= 0f || (session != null && (session.IsPaused || !session.IsGameplayFlowActive())))
                return;

            StepWallSlam(fixedDeltaTime);

            if (IsControlLocked)
            {
                if (body != null && !body.isKinematic)
                {
                    knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, Mathf.Max(1f,runtimeArchetype.KnockbackBraking)*fixedDeltaTime);
                    body.linearVelocity = IsFrozen ? Vector3.zero : knockbackVelocity;
                }
                return;
            }

            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.ExplosiveMoth)
            {
                bool wasArmed = isFuseActive;
                if (!isFuseActive && targetPlayer != null && targetPlayer.IsAlive)
                {
                    Vector3 distance = PursuitGoal - transform.position;
                    distance.y = 0f;
                    if (distance.sqrMagnitude < MothArmDistance * MothArmDistance)
                        ArmMoth();
                }
                if (isFuseActive)
                {
                    if (wasArmed) prototypeMoth.StepFuse(fixedDeltaTime);
                    fuseTimer = prototypeMoth.FuseRemaining;
                    if (isDead) return;
                }
            }

            // 1. Frenado progresivo del empuje según arquetipo
            if (knockbackVelocity.sqrMagnitude > 0.0001f)
            {
                float braking = Mathf.Max(1f, runtimeArchetype.KnockbackBraking);
                knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, braking * fixedDeltaTime);
            }
            else
            {
                knockbackVelocity = Vector3.zero;
            }

            bool isKnockedBack = knockbackVelocity.sqrMagnitude > 0.1f;

            // 2. Calcular velocidad de locomoción hacia el objetivo + separación local + evitación sencilla
            Vector3 locomotionVelocity;
            if (elite != null && elite.TryOverrideLocomotion(fixedDeltaTime, out Vector3 eliteVelocity))
                locomotionVelocity = eliteVelocity;
            else if (colossusCharge != null && colossusCharge.TryOverrideLocomotion(fixedDeltaTime, out Vector3 chargeVelocity))
                locomotionVelocity = chargeVelocity * (1f - SlowFraction);
            else
                locomotionVelocity = isKnockedBack ? Vector3.zero : ComputeArchetypeLocomotion(fixedDeltaTime);
            currentDesiredVelocity = locomotionVelocity;

            if (elite != null && elite.State != EliteEnemyState.Pursuit)
                facingDirection = elite.LockedDirection;
            else if (colossusCharge != null && colossusCharge.State != ColossusChargeState.Pursuit)
            {
                facingDirection = colossusCharge.LockedDirection;
            }
            else if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Mortar && targetPlayer != null)
            {
                Vector3 toPlayer = PursuitGoal - transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.001f)
                    facingDirection = toPlayer.normalized;
            }
            else if (locomotionVelocity.sqrMagnitude > 0.001f)
            {
                facingDirection = locomotionVelocity.normalized;
            }
            else if (targetPlayer != null)
            {
                Vector3 toPlayer = PursuitGoal - transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.001f)
                    facingDirection = toPlayer.normalized;
            }

            // 3. Escribir velocidad planar en el Rigidbody (empuje prioritario cuando está activo, como en Arena)
            if (body != null && !body.isKinematic)
            {
                Vector3 combined = isKnockedBack ? knockbackVelocity : (locomotionVelocity + knockbackVelocity);
                combined.y = 0f;
                body.linearVelocity = combined;
                if (ArenaSurface.Active != null)
                    ArenaSurface.Follow(body, HitRadius, 1f, fixedDeltaTime);
                else if (Mathf.Abs(body.position.y - 1f) > 0.01f)
                    body.position = new Vector3(body.position.x, 1f, body.position.z);
            }

            // 4. Mortero: cadencia editable y charco con reserva propia.
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Mortar)
            {
                StepMortarAttack(fixedDeltaTime);
            }

            // 5. Comprobar daño de contacto mediante los colliders reales
            if (!isKnockedBack)
                TryDealContactDamage();
        }

        private void StepMortarAttack(float fixedDeltaTime)
        {
            if (isDead || targetPlayer == null || !targetPlayer.IsAlive)
            {
                telegraphTimer = 0f;
                return;
            }

            MortarAttackData attack = MortarAttack;
            Vector3 offset = PursuitGoal - transform.position; offset.y = 0f;
            float maximumRange = Mathf.Max(1f, attack.MaximumRange);
            float interval = Mathf.Max(0.5f, attack.FireInterval);
            float warning = Mathf.Clamp(attack.WarningDuration, 0.1f, interval);
            float readyForWarning = interval - warning;
            bool hasCapacity = puddlePool != null && puddlePool.CanLaunchProjectile;
            if (offset.sqrMagnitude > maximumRange * maximumRange || !hasCapacity)
            {
                // Reload while approaching. Only the final warning needs range and a reserved shot.
                // Resetting the entire cooldown here prevented slow mortars from ever firing at a moving player.
                attackCooldownTimer = Mathf.Min(readyForWarning, attackCooldownTimer + fixedDeltaTime);
                telegraphTimer = 0f;
                return;
            }
            attackCooldownTimer = Mathf.Min(interval, attackCooldownTimer + fixedDeltaTime);
            telegraphTimer = Mathf.Clamp(attackCooldownTimer - readyForWarning, 0f, warning);

            if (attackCooldownTimer >= interval && hasCapacity)
            {
                Vector3 launchOrigin = mortarView != null && mortarView.IsReady
                    ? mortarView.LaunchOrigin
                    : transform.position + new Vector3(0f, 1.5f, 0f);
                var spec = CorrosivePuddlePool.ProjectileSpec.CreateMortarDefault(
                    TargetId,
                    launchOrigin,
                    PursuitGoal);
                spec.FlightDuration = Mathf.Max(0.1f, attack.FlightDuration);
                spec.PuddleRadius = spec.ImpactRadius = Mathf.Max(0.1f, attack.PuddleRadius);
                spec.PuddleDuration = Mathf.Max(0.1f, attack.PuddleDuration);
                spec.PuddleTickDamage = Mathf.Max(0f, attack.TickDamage);
                spec.PuddleTickInterval = Mathf.Max(0.1f, attack.TickInterval);
                if (puddlePool.TryLaunchParabolicProjectile(in spec))
                {
                    attackCooldownTimer = 0f;
                    telegraphTimer = 0f;
                    mortarRecoilRemaining = 0.16f;
                    MortarShotsFiredCount++;
                    mortarView?.NotifyProjectileLaunched();
                }
            }
        }

        protected virtual Vector3 ComputeArchetypeLocomotion(float fixedDeltaTime)
        {
            if (isFuseActive)
                return Vector3.zero;

            if (targetPlayer == null || !targetPlayer.IsAlive)
                return Vector3.zero;

            Vector3 myPos = transform.position;
            Vector3 toPlayer = PursuitGoal - myPos;
            toPlayer.y = 0f;
            float distSqr = toPlayer.sqrMagnitude;
            float dist = distSqr > 0.0001f ? Mathf.Sqrt(distSqr) : 0f;
            Vector3 dirToPlayer = dist > 0.0001f ? (toPlayer / dist) : Vector3.forward;

            Vector3 seekDir;
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Mortar)
            {
                // Conserva la banda editable, retrocediendo sin dejar de mirar al jugador.
                if (dist > Mathf.Max(MortarAttack.RetreatDistance, MortarAttack.HoldDistance))
                    seekDir = dirToPlayer;
                else if (dist < Mathf.Max(0f, MortarAttack.RetreatDistance))
                    seekDir = -dirToPlayer;
                else
                    seekDir = Vector3.zero;
            }
            else
            {
                Vector3 pursuitOffset = toPlayer;
                if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Interceptor && targetBody != null &&
                    dist > interceptorDirectPursuitDistance && (PursuitGoal-targetPlayer.WorldCenter).sqrMagnitude < .001f)
                {
                    Vector3 targetVelocity = targetBody.linearVelocity;
                    targetVelocity.y = 0f;
                    pursuitOffset += Vector3.ClampMagnitude(targetVelocity, interceptorPredictionSpeedCap) * interceptorPredictionSeconds;
                }
                seekDir = pursuitOffset.sqrMagnitude > 0.0001f ? pursuitOffset.normalized : Vector3.zero;
            }

            // Separación local entre enemigos cercanos para evitar apilamiento sin requerir colisiones O(N^2) en PhysX
            Vector3 separation = ComputeLocalSeparation(myPos);

            if (seekDir.sqrMagnitude < 0.0001f && separation.sqrMagnitude < 0.05f)
                return Vector3.zero;

            Vector3 combinedDir = seekDir + separation * separationWeight;
            combinedDir.y = 0f;
            if (combinedDir.sqrMagnitude > 0.0001f)
                combinedDir.Normalize();
            else
                return Vector3.zero;

            Vector3 navigationGoal = PursuitGoal;
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Interceptor && targetBody != null && dist > interceptorDirectPursuitDistance && (PursuitGoal-targetPlayer.WorldCenter).sqrMagnitude < .001f)
                navigationGoal += Vector3.ClampMagnitude(targetBody.linearVelocity, interceptorPredictionSpeedCap) * interceptorPredictionSeconds;
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Mortar && dist < Mathf.Max(0f, MortarAttack.RetreatDistance))
                navigationGoal = myPos - dirToPlayer * Mathf.Max(3f, MortarAttack.HoldDistance - dist);
            combinedDir = obstacleNavigator.Steer(this, combinedDir, navigationGoal, obstacleLookAhead, fixedDeltaTime);

            return combinedDir * assignedMoveSpeed * (1f - SlowFraction);
        }

        protected Vector3 ComputeLocalSeparation(Vector3 myPos)
        {
            Vector3 push = Vector3.zero;
            float minSepDist = separationRadius * Mathf.Max(1f, transform.localScale.x);
            float minSepSqr = minSepDist * minSepDist;

            RefreshNeighborIndex();
            bool indexed=!neighborIndex.IsDense(myPos);
            if(indexed)neighborIndex.Query(myPos,minSepDist,neighborCandidates);
            int checkedNeighbors = 0;
            for (int i = 0; i < (indexed?neighborCandidates.Count:activeEnemies.Count) && checkedNeighbors < 16; i++)
            {
                int index = indexed ? neighborCandidates[i] : i;
                Vector3 diff = myPos - neighborIndex.PositionAt(index);
                diff.y = 0f;
                float distSqr = diff.sqrMagnitude;
                if (distSqr >= minSepSqr)
                    continue;

                EnemyActor other = activeEnemies[index];
                if (other == null || other == this || !other.IsAlive)
                    continue;
                checkedNeighbors++;
                if (distSqr < 0.0001f)
                {
                    float tieAngle = (TargetId & 255) * 0.024f;
                    push += new Vector3(Mathf.Cos(tieAngle), 0f, Mathf.Sin(tieAngle));
                }
                else
                {
                    float dist = Mathf.Sqrt(distSqr);
                    float weight = 1f - (dist / minSepDist);
                    push += (diff / dist) * weight;
                }
            }

            return push;
        }

        // Shared by specialised pursuit implementations, including Gólgora.
        protected Vector3 ApplySimpleObstacleAvoidance(Vector3 origin, Vector3 desiredDir)
        {
            Vector3 goal = targetPlayer != null ? PursuitGoal : origin + desiredDir * 8f;
            return obstacleNavigator.Steer(this, desiredDir, goal, obstacleLookAhead, Time.fixedDeltaTime);
        }

        public bool TryDealContactDamage()
        {
            if (colossusCharge != null && !colossusCharge.AllowsOrdinaryContact)
                return false;
            if (isDead || runtimeArchetype.ContactDamage <= 0f || targetPlayer == null || !targetPlayer.IsAlive)
                return false;

            float now = session != null ? session.GameplayTime : Time.time;
            if (now < nextContactReadyTime)
                return false;

            if (!ContactCollisionUtility.AreCollidersTouching(
                bodyCollider,
                targetPlayer.HurtboxCollider,
                out _,
                out Vector3 hitDirection))
            {
                return false;
            }

            return ResolveChargeContact(hitDirection);
        }

        // A swept charge contact can occur between the current and next physics position.
        // It uses the same cooldown and damage contract as physical contact.
        internal bool ResolveChargeContact(Vector3 hitDirection)
        {
            if (isDead || targetPlayer == null || !targetPlayer.IsAlive ||
                targetPlayer.gameObject.layer == PhysicsLayers.PlayerDashLayer)
                return false;
            float now = session != null ? session.GameplayTime : Time.time;
            if (now < nextContactReadyTime) return false;
            nextContactReadyTime = now + Mathf.Max(0.05f, runtimeArchetype.ContactCooldown);
            var request = new DamageRequest(
                TargetId,
                DamageSourceType.Contact,
                transform.position,
                hitDirection,
                runtimeArchetype.ContactDamage);

            targetPlayer.ReceiveDamage(in request);
            groundSpriteView?.NotifyContact();
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Interceptor)
                PlayInterceptorAttackSound();
            return true;
        }

        private void PlayInterceptorAttackSound()
        {
            if (interceptorSound == null)
            {
                interceptorSoundClip = Resources.Load<AudioClip>("Audio/Galgo_Ataque");
                interceptorSound = gameObject.AddComponent<AudioSource>();
                interceptorSound.playOnAwake = false;
                interceptorSound.spatialBlend = 0f;
                interceptorSound.volume = InterceptorAttackVolume;
            }
            if (interceptorSoundClip != null)
                interceptorSound.PlayOneShot(interceptorSoundClip);
        }

        /// <summary>
        /// Aplica empuje según la fórmula de Arena: velocidadEmpuje = impulsoLinterna * susceptibilidad.
        /// Los enemigos inmunes (susceptibilidad == 0) no se desplazan pero registran deformación pesada.
        /// </summary>
        internal void ApplyFlashlightFinisher(Vector3 impulse, int tier, float window)
        {
            ApplyKnockback(impulse);
            if (isDead || !CanBeRamPushed || knockbackVelocity.sqrMagnitude <= .1f) return;
            wallSlamTier = tier;
            wallSlamTimeRemaining = window;
        }

        private void StepWallSlam(float dt)
        {
            if (wallSlamTimeRemaining <= 0f) return;
            float step = Mathf.Min(dt, wallSlamTimeRemaining);
            if (IsFrozen || knockbackVelocity.sqrMagnitude <= .1f)
            {
                wallSlamTimeRemaining = 0f;
                return;
            }

            Vector3 start = body.position;
            Vector3 travel = Vector3.MoveTowards(knockbackVelocity, Vector3.zero,
                Mathf.Max(1f, runtimeArchetype.KnockbackBraking) * dt) * step;
            Vector3 end = start + travel;
            GatherSegmentCandidates(start, end, HitRadius, wallSlamCandidates);
            float closest = float.PositiveInfinity;
            Bounds bounds = bodyCollider.bounds;
            foreach (EnemyActor other in wallSlamCandidates)
            {
                if (other == null || other == this || other.Session != session || !other.IsAlive ||
                    other.BodyCollider == null || !other.BodyCollider.enabled ||
                    other is GolgoraActor { IsAirborne: true } ||
                    Vector3.Dot(other.WorldCenter - start, travel) <= 0f)
                    continue;
                Bounds otherBounds = other.BodyCollider.bounds;
                if (bounds.max.y < otherBounds.min.y || otherBounds.max.y < bounds.min.y) continue;
                if (TouchesSegment(start, end, other.WorldCenter, HitRadius + other.HitRadius, out float fraction) &&
                    fraction < closest && !Physics.Linecast(start, other.WorldCenter, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore))
                    closest = fraction;
            }
            if (closest <= 1f) TriggerWallSlam(Vector3.Lerp(start, end, closest));
            else wallSlamTimeRemaining = Mathf.Max(0f, wallSlamTimeRemaining - dt);
        }

        private void OnCollisionEnter(Collision collision) => CheckWallSlamContact(collision);
        private void OnCollisionStay(Collision collision) => CheckWallSlamContact(collision);

        private void CheckWallSlamContact(Collision collision)
        {
            if (wallSlamTimeRemaining <= 0f || IsFrozen || collision.gameObject.layer != PhysicsLayers.DefaultLayer ||
                (ArenaSurface.Active != null && collision.collider == ArenaSurface.Active.GroundCollider))
                return;

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                if (Mathf.Abs(contact.normal.y) > .6f || Vector3.Dot(knockbackVelocity, contact.normal) >= -.1f)
                    continue;
                TriggerWallSlam(body.position);
                break;
            }
        }

        private void TriggerWallSlam(Vector3 position)
        {
            wallSlamTimeRemaining = 0f;
            session?.Upgrades?.TriggerWallSlam(position, wallSlamTier);
        }

        internal bool CanBeRamPushed => runtimeArchetype.KnockbackSusceptibility > .0001f;

        internal Vector3 CarryAlongDash(Vector3 dashDirection, float distance, RaycastHit[] sweepHits)
        {
            Vector3 from = body.position;
            if (!CanBeRamPushed || distance <= .001f) return from;
            Vector3 center = bodyCollider.bounds.center;
            float radius = HitRadius;
            float span = Mathf.Max(0f, bodyCollider.bounds.extents.y - radius);
            int count = Physics.CapsuleCastNonAlloc(center - Vector3.up * span, center + Vector3.up * span,
                radius, dashDirection, sweepHits, distance, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore);
            if (count == sweepHits.Length) return from;
            Collider ground = ArenaSurface.Active != null ? ArenaSurface.Active.GroundCollider : null;
            for (int i = 0; i < count; i++)
            {
                var hit = sweepHits[i];
                if (hit.collider != ground && (hit.distance > 0f || Vector3.Dot(hit.normal, dashDirection) < -.05f))
                    distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - .05f));
            }

            // Preserve the body height during the swept displacement. The next physics step follows the relief.
            Vector3 carried = from + dashDirection * distance;
            carried.y = from.y;
            if (distance > .001f)
            {
                // Sync the interpolated pose now for damage feedback and other attacks this step.
                body.position = carried;
                transform.position = carried;
                InvalidateNeighbors();
            }
            return carried;
        }

        internal void LaunchFromDash(Vector3 direction, float speed) =>
            ApplyKnockback(direction * (speed / runtimeArchetype.KnockbackSusceptibility));

        public void ApplyKnockback(Vector3 planarImpulse)
        {
            wallSlamTimeRemaining = 0f;
            if (isDead)
                return;

            Vector3 flatImpulse = new Vector3(planarImpulse.x, 0f, planarImpulse.z);
            if (flatImpulse.sqrMagnitude <= 0.0001f)
                return;

            float susceptibility = Mathf.Max(0f, runtimeArchetype.KnockbackSusceptibility);
            if (susceptibility <= 0.0001f)
            {
                // Enemigo inmune al empuje (Coloso / Gólgora): sin desplazamiento físico
                visualComposer?.ReportKnockbackVelocity(Vector3.zero);
                return;
            }

            knockbackVelocity = flatImpulse * susceptibility;
            KnockbackAppliedCount++;
            visualComposer?.ReportKnockbackVelocity(knockbackVelocity);
        }

        private void Update()
        {
            if (isDead || (session != null && session.IsPaused) || Time.timeScale <= 0f)
                return;

            StepVisuals(Time.deltaTime);
        }

        public void StepVisuals(float deltaTime)
        {
            if (deltaTime <= 0f || visualComposer == null)
                return;

            float now = session != null ? session.GameplayTime : Time.time;
            float decIntensity = session != null ? session.Effects.DecorativeIntensity : 1f;
            bool reduced = session != null && session.Effects.ReducedEffectsMode;

            visualComposer.ReportLocomotion(facingDirection, Velocity, Mathf.Max(0.1f, assignedMoveSpeed), now, decIntensity);
            visualComposer.ReportKnockbackVelocity(knockbackVelocity);
            visualComposer.StepVisuals(deltaTime, 540f, reduced);
            colossusCharge?.Presentation.Step(deltaTime);
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Mortar && mortarEmitter != null)
            {
                mortarRecoilRemaining = Mathf.Max(0f, mortarRecoilRemaining - deltaTime);
                float intensity = reduced ? 0f : decIntensity;
                float charge = Mathf.Clamp01(telegraphTimer / Mathf.Max(0.1f, MortarAttack.WarningDuration)) * intensity;
                float recoil = Mathf.Clamp01(mortarRecoilRemaining / 0.16f) * intensity;
                mortarEmitter.localPosition = mortarEmitterRestPosition + Vector3.back * (0.08f * recoil);
                mortarEmitter.localScale = Vector3.Scale(mortarEmitterRestScale,
                    new Vector3(1f + charge * 0.12f, 1f - charge * 0.12f, 1f - recoil * 0.18f));
            }
        }

        public virtual DamageResult ReceiveDamage(in DamageRequest request)
        {
            EnsureInitialized();
            Vector3 worldPos = transform.position;

            if (isDead || currentHealth <= 0f || (isFuseActive && runtimeArchetype.ArchetypeId == EnemyArchetypeId.ExplosiveMoth)
                || request.BaseDamage <= 0f || float.IsNaN(request.BaseDamage))
            {
                var ignored = new DamageResult(
                    request.SourceId,
                    TargetId,
                    request.SourceType,
                    request.OriginPosition,
                    worldPos,
                    request.HitDirection,
                    Mathf.Max(0f, request.BaseDamage),
                    0f,
                    0f,
                    currentHealth,
                    runtimeArchetype.MaxHealth,
                    false,
                    false,
                    false,
                    true,
                    false,
                    false,
                    request.BurstId,
                    request.PulseIndex, request.DamageType);

                session?.Events.PublishDamage(in ignored);
                return ignored;
            }

            DeterministicRandom combatRng = session != null ? session.Rng.Combat : new DeterministicRandom(1337);
            bool isCritical = request.CanCrit && request.CritChance > 0f && combatRng.NextFloat() < Mathf.Clamp01(request.CritChance);
            float resolvedDamage = isCritical
                ? request.BaseDamage * Mathf.Max(1f, request.CritMultiplier)
                : request.BaseDamage;

            bool executed = false;
            bool playerAttack = DamageTypes.IsFlashlight(request.SourceType) ||
                (request.SourceType >= DamageSourceType.UpgradeProjectile && request.SourceType <= DamageSourceType.UpgradeBlade) ||
                request.SourceType is DamageSourceType.WallSlam or DamageSourceType.DashRam or DamageSourceType.DashReturn or
                    DamageSourceType.ElectricCable or DamageSourceType.SharpGem;
            var upgradeStats = session != null ? session.PlayerStats : null;
            if (playerAttack && upgradeStats != null)
            {
                resolvedDamage *= upgradeStats.CompanionDamageMultiplier;
                if (IsFrozen) resolvedDamage *= 1f + upgradeStats.Upgrades.frozenDamageBonus;
                if (isCritical && upgradeStats.GetUpgradeTier(UpgradeId.CRIT_05) > 0 && currentHealth / MaxHealth < upgradeStats.Upgrades.eyeHealthThreshold)
                    resolvedDamage *= upgradeStats.Upgrades.eyeCritMultiplier;
            }
            float resistance = IncomingDamageMultiplier(request.DamageType);
            resolvedDamage *= resistance;
            if (playerAttack && upgradeStats != null)
            {
                float threshold = UpgradeTuning.At(upgradeStats.Upgrades.executionThreshold, upgradeStats.GetUpgradeTier(UpgradeId.CRIT_03));
                if (runtimeArchetype.ArchetypeId != EnemyArchetypeId.GolgoraBoss && threshold > 0f &&
                    resolvedDamage < currentHealth && (currentHealth - resolvedDamage) / MaxHealth + .000001f < threshold)
                {
                    executed = true;
                    resolvedDamage = currentHealth;
                }
            }
            float previous = currentHealth;
            currentHealth = Mathf.Max(PreventLethalDamage ? 1f : 0f, currentHealth - resolvedDamage);
            float lost = previous - currentHealth;
            bool lethal = currentHealth <= 0f;
            bool moth = runtimeArchetype.ArchetypeId == EnemyArchetypeId.ExplosiveMoth;
            if (lethal && moth)
                ArmMoth(fromLethalDamage: true);
            bool causedDeath = lethal && !moth;

            if (visualComposer != null)
            {
                bool reduced = session != null && session.Effects.ReducedEffectsMode;
                visualComposer.TriggerHitReaction(request.HitDirection, isCritical, isPlayerHurt: false, reducedEffects: reduced);
            }
            if (!lethal) groundSpriteView?.NotifyHit();

            // El impacto conserva su posición y resultado antes de desactivar o devolver el enemigo al pool
            var result = new DamageResult(
                request.SourceId,
                TargetId,
                request.SourceType,
                request.OriginPosition,
                worldPos,
                request.HitDirection,
                request.BaseDamage,
                resolvedDamage,
                lost,
                currentHealth,
                runtimeArchetype.MaxHealth,
                isCritical,
                false,
                false,
                false,
                causedDeath,
                false,
                request.BurstId,
                request.PulseIndex, request.DamageType, wasExecuted: executed, wasResisted: resistance < 1f && !executed);

            session?.Events.PublishDamage(in result);

            if (causedDeath)
            {
                ExecuteDeath(worldPos, request.HitDirection, request.SourceType);
            }

            return result;
        }

        public void ArmMoth(bool fromLethalDamage = false)
        {
            if (isDead || isFuseActive || runtimeArchetype.ArchetypeId != EnemyArchetypeId.ExplosiveMoth)
                return;
            isFuseActive = true;
            fuseTimer = prototypeMoth.StartPriming(fromLethalDamage);
            PlayMothWarningSound();
        }

        private void PlayMothWarningSound()
        {
            if (mothSound == null)
            {
                mothSoundClip = Resources.Load<AudioClip>("Audio/Polilla_Aviso_Explosion");
                mothSound = gameObject.AddComponent<AudioSource>();
                mothSound.playOnAwake = false;
                mothSound.spatialBlend = 0f;
                mothSound.volume = MothSoundVolume;
            }
            if (mothSoundClip != null)
                mothSound.PlayOneShot(mothSoundClip);
        }

        public void CompleteMothDetonation()
        {
            if (!isFuseActive || isDead) return;
            ExecuteDeath(transform.position, facingDirection, DamageSourceType.MothExplosion);
        }

        protected virtual void ExecuteDeath(Vector3 capturedPosition, Vector3 hitDirection, DamageSourceType cause)
        {
            if (isDead)
                return;

            session?.Upgrades?.EnemyDied(this, capturedPosition);
            isDead = true;
            CombatTargetRegistry.Unregister(this);
            activeEnemies.Remove(this);
            InvalidateNeighbors();

            // Desactivar inmediatamente daño, selección y colisión del actor
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Mortar)
                mortarView?.PlayDeath();
            groundSpriteView?.PlayDeath();
            if (bodyCollider != null)
                bodyCollider.enabled = false;
            if (body != null && !body.isKinematic)
                body.linearVelocity = Vector3.zero;
            knockbackVelocity = Vector3.zero;

            // Resolver recompensa una única vez usando la semilla de combate
            bool droppedMedkit = false;
            int xpReward = 0;
            if (!deathRewardResolved)
            {
                deathRewardResolved = true;
                DeathNotifyCount++;
                xpReward = runtimeArchetype.XpReward;

                DeterministicRandom combatRng = session != null ? session.Rng.Combat : new DeterministicRandom(1337);
                float dropChance = session != null && session.PlayerStats != null
                    ? session.PlayerStats.GetMedkitDropChance(runtimeArchetype.IsEliteDropTier)
                    : (runtimeArchetype.IsEliteDropTier ? 0.25f : 0.04f);
                // El cierre del jefe acredita XP aparte; no crea botiquín ni consume una tirada de drop.
                droppedMedkit = runtimeArchetype.ArchetypeId != EnemyArchetypeId.GolgoraBoss && combatRng.NextFloat() < dropChance;
            }

            var deathEvent = new ActorDeathEvent(
                TargetId,
                (int)runtimeArchetype.ArchetypeId,
                false,
                runtimeArchetype.ArchetypeId == EnemyArchetypeId.GolgoraBoss,
                capturedPosition,
                hitDirection,
                cause,
                xpReward,
                droppedMedkit,
                true);

            if (session != null)
            {
                if (deathEvent.IsBoss)
                    session.QueueBossDeath(in deathEvent);
                else
                    session.Events.PublishDeath(in deathEvent);
            }

            // Devolver al pool si pertenece a uno, o desactivar el actor (su explosión/ruptura visual corre en EnemyDeathPresentationPool)
            if (ReturnToPoolCallback != null)
                ReturnToPoolCallback.Invoke(this);
            else
                gameObject.SetActive(false);
        }

        public void SetTimersForTesting(float contactTime, float attackCd, float fuse, float telegraph)
        {
            nextContactReadyTime = contactTime;
            attackCooldownTimer = attackCd;
            fuseTimer = fuse;
            telegraphTimer = telegraph;
            isFuseActive = fuse > 0f;
        }
    }

}
