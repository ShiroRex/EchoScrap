using System;
using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Base compartida de enemigos (Residual, Interceptor, Coloso, Mortero, Polilla Explosiva).
    /// Resuelve locomoción con Rigidbody en FixedUpdate, altura fija, separación local, evitación sencilla,
    /// daño por contacto de colliders reales, empuje (impulso * susceptibilidad y frenado),
    /// reacción visual / hit-stop local mediante ActorVisualComposer y reciclaje íntegro en pool.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class EnemyActor : MonoBehaviour, IDamageTarget
    {
        public const float MortarMinBandRange = 6.5f;
        public const float MortarMaxBandRange = 7.5f;
        public const float MortarFireInterval = 3.5f;
        public const float MortarTelegraphLeadTime = 0.25f;
        public const float MothFuseDuration = 0.9f;
        public const float MothArmDistance = 2f;

        private static readonly List<EnemyActor> activeEnemies = new List<EnemyActor>(256);
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

        private float currentHealth;
        private float assignedMoveSpeed;
        private Vector3 knockbackVelocity;
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
        private Transform mortarEmitter;
        private Vector3 mortarEmitterRestPosition;
        private Vector3 mortarEmitterRestScale;
        private float mortarRecoilRemaining;
        private ColossusCharge colossusCharge;
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
        public bool IsFuseActive => isFuseActive;
        public Vector3 WorldCenter => transform.position;
        public CapsuleCollider BodyCollider => bodyCollider;
        public Rigidbody Body => body;
        public ActorVisualComposer VisualComposer => visualComposer;
        public CombatSession Session => session;
        public PlayerHealth TargetPlayer => targetPlayer;
        public ColossusCharge Charge => colossusCharge;
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
                float scaleXZ = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
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
            if (IsAlive)
                CombatTargetRegistry.Register(this);
        }

        private void OnDisable()
        {
            activeEnemies.Remove(this);
            CombatTargetRegistry.Unregister(this);
            if (body != null && !body.isKinematic)
                body.linearVelocity = Vector3.zero;
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
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Mortar)
                EnsurePuddlePool();
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.ExplosiveMoth)
            {
                mothPresentation = GetComponent<MothFusePresentation>() ?? gameObject.AddComponent<MothFusePresentation>();
                mothPresentation.ResetPresentation();
                prototypeMoth = GetComponent<Prototype.EnemyMoth>() ?? gameObject.AddComponent<Prototype.EnemyMoth>();
                prototypeMoth.Bind(this);
                mothExplosions = MothExplosionPool.GetOrCreate(session, targetPlayer);
            }
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
        public virtual void ResetForSpawn(Vector3 spawnPosition)
        {
            EnsureInitialized();
            PhysicsLayers.EnsureConfigured();

            // 1. Salud máxima y estado de muerte/recompensas
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
            knockbackVelocity = Vector3.zero;
            currentDesiredVelocity = Vector3.zero;
            facingDirection = Vector3.forward;

            // 4. Temporizadores de contacto, disparo, mecha y telegrafía a cero
            nextContactReadyTime = 0f;
            attackCooldownTimer = 0f;
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Mortar)
            {
                DeterministicRandom attackRng = session != null ? session.Rng.Combat : new DeterministicRandom(1337);
                attackCooldownTimer = MortarFireInterval - attackRng.Range(1f, MortarFireInterval);
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
            transform.localScale = runtimeArchetype.VisualScale;
            // The heavy capsule scales to 3.2 m tall. Keep its bottom on the floor at fixed root Y=1.
            bodyCollider.center = runtimeArchetype.ArchetypeId == EnemyArchetypeId.Colossus
                ? Vector3.up * Mathf.Max(0f, runtimeArchetype.ColliderHeight * 0.5f - 1f / runtimeArchetype.VisualScale.y)
                : Vector3.zero;
            transform.SetPositionAndRotation(clampedSpawn, Quaternion.identity);
            if (targetPlayer != null)
            {
                Vector3 toTarget = targetPlayer.transform.position - clampedSpawn;
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
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            if (!activeEnemies.Contains(this))
                activeEnemies.Add(this);
            CombatTargetRegistry.Register(this);
            Physics.SyncTransforms();
        }

        private void FixedUpdate()
        {
            EnsureInitialized();
            if (isDead)
                return;

            if ((session != null && session.IsPaused) || Time.timeScale <= 0f)
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

            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.ExplosiveMoth)
            {
                bool wasArmed = isFuseActive;
                if (!isFuseActive && targetPlayer != null && targetPlayer.IsAlive)
                {
                    Vector3 distance = targetPlayer.transform.position - transform.position;
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
            if (colossusCharge != null && colossusCharge.TryOverrideLocomotion(fixedDeltaTime, out Vector3 chargeVelocity))
                locomotionVelocity = chargeVelocity;
            else
                locomotionVelocity = isKnockedBack ? Vector3.zero : ComputeArchetypeLocomotion(fixedDeltaTime);
            currentDesiredVelocity = locomotionVelocity;

            if (colossusCharge != null && colossusCharge.State != ColossusChargeState.Pursuit)
            {
                facingDirection = colossusCharge.LockedDirection;
            }
            else if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Mortar && targetPlayer != null)
            {
                Vector3 toPlayer = targetPlayer.transform.position - transform.position;
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
                Vector3 toPlayer = targetPlayer.transform.position - transform.position;
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
                if (Mathf.Abs(body.position.y - 1f) > 0.01f)
                    body.position = new Vector3(body.position.x, 1f, body.position.z);
            }

            // 4. Mortero: cadencia de 3,5 s, vuelo de 0,65 s y charco con reserva propia.
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

            attackCooldownTimer = Mathf.Min(MortarFireInterval, attackCooldownTimer + fixedDeltaTime);
            float remaining = MortarFireInterval - attackCooldownTimer;
            bool hasCapacity = puddlePool != null && puddlePool.CanLaunchProjectile;
            telegraphTimer = hasCapacity && remaining <= MortarTelegraphLeadTime && remaining > 0f
                ? (MortarTelegraphLeadTime - remaining)
                : 0f;

            if (attackCooldownTimer >= MortarFireInterval && hasCapacity)
            {
                Vector3 launchOrigin = transform.position + new Vector3(0f, 0.65f, 0f) + facingDirection * 0.25f;
                var spec = CorrosivePuddlePool.ProjectileSpec.CreateMortarDefault(
                    TargetId,
                    launchOrigin,
                    targetPlayer.transform.position);
                if (puddlePool.TryLaunchParabolicProjectile(in spec))
                {
                    attackCooldownTimer = 0f;
                    telegraphTimer = 0f;
                    mortarRecoilRemaining = 0.16f;
                    MortarShotsFiredCount++;
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
            Vector3 toPlayer = targetPlayer.transform.position - myPos;
            toPlayer.y = 0f;
            float distSqr = toPlayer.sqrMagnitude;
            float dist = distSqr > 0.0001f ? Mathf.Sqrt(distSqr) : 0f;
            Vector3 dirToPlayer = dist > 0.0001f ? (toPlayer / dist) : Vector3.forward;

            Vector3 seekDir;
            if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Mortar)
            {
                // Mantiene 6,5–7,5 m, retrocediendo sin dejar de mirar al jugador.
                if (dist > MortarMaxBandRange)
                    seekDir = dirToPlayer;
                else if (dist < MortarMinBandRange)
                    seekDir = -dirToPlayer;
                else
                    seekDir = Vector3.zero;
            }
            else
            {
                Vector3 pursuitOffset = toPlayer;
                if (runtimeArchetype.ArchetypeId == EnemyArchetypeId.Interceptor && targetBody != null &&
                    dist > interceptorDirectPursuitDistance)
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

            // Evitación sencilla de obstáculos estáticos (capa Default = 0)
            combinedDir = ApplySimpleObstacleAvoidance(myPos, combinedDir);

            return combinedDir * assignedMoveSpeed;
        }

        protected Vector3 ComputeLocalSeparation(Vector3 myPos)
        {
            Vector3 push = Vector3.zero;
            float minSepDist = separationRadius * Mathf.Max(1f, transform.localScale.x);
            float minSepSqr = minSepDist * minSepDist;

            int checkedNeighbors = 0;
            for (int i = 0; i < activeEnemies.Count && checkedNeighbors < 16; i++)
            {
                EnemyActor other = activeEnemies[i];
                if (other == null || other == this || !other.IsAlive)
                    continue;

                Vector3 diff = myPos - other.transform.position;
                diff.y = 0f;
                float distSqr = diff.sqrMagnitude;
                if (distSqr >= minSepSqr)
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

        protected Vector3 ApplySimpleObstacleAvoidance(Vector3 origin, Vector3 desiredDir)
        {
            if (desiredDir.sqrMagnitude < 0.001f)
                return desiredDir;

            int defaultLayerMask = 1 << PhysicsLayers.DefaultLayer;
            if (Physics.SphereCast(
                new Vector3(origin.x, 1f, origin.z),
                HitRadius * 0.85f,
                desiredDir,
                out RaycastHit hit,
                obstacleLookAhead,
                defaultLayerMask,
                QueryTriggerInteraction.Ignore))
            {
                if (Mathf.Abs(hit.normal.y) < 0.4f)
                {
                    Vector3 slide = Vector3.ProjectOnPlane(desiredDir, new Vector3(hit.normal.x, 0f, hit.normal.z).normalized);
                    if (slide.sqrMagnitude > 0.01f)
                        return slide.normalized;
                }
            }

            return desiredDir;
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
            return true;
        }

        /// <summary>
        /// Aplica empuje según la fórmula de Arena: velocidadEmpuje = impulsoLinterna * susceptibilidad.
        /// Los enemigos inmunes (susceptibilidad == 0) no se desplazan pero registran deformación pesada.
        /// </summary>
        public void ApplyKnockback(Vector3 planarImpulse)
        {
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
                float charge = Mathf.Clamp01(telegraphTimer / MortarTelegraphLeadTime) * intensity;
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
                    request.PulseIndex);

                session?.Events.PublishDamage(in ignored);
                return ignored;
            }

            DeterministicRandom combatRng = session != null ? session.Rng.Combat : new DeterministicRandom(1337);
            bool isCritical = request.CanCrit && request.CritChance > 0f && combatRng.NextFloat() < Mathf.Clamp01(request.CritChance);
            float resolvedDamage = isCritical
                ? request.BaseDamage * Mathf.Max(1f, request.CritMultiplier)
                : request.BaseDamage;

            float previous = currentHealth;
            currentHealth = Mathf.Max(0f, currentHealth - resolvedDamage);
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
                request.PulseIndex);

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

            isDead = true;
            CombatTargetRegistry.Unregister(this);
            activeEnemies.Remove(this);

            // Desactivar inmediatamente daño, selección y colisión del actor
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

    /// <summary>
    /// Pool reutilizable de instancias de EnemyActor libre de asignaciones en tiempo de combate.
    /// </summary>
    public sealed class EnemyPool : MonoBehaviour
    {
        private readonly Dictionary<EnemyArchetypeId, Stack<EnemyActor>> inactivePools =
            new Dictionary<EnemyArchetypeId, Stack<EnemyActor>>();
        private readonly List<EnemyActor> allCreatedActors = new List<EnemyActor>(256);

        [SerializeField] private CombatSession session;
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private CorrosivePuddlePool puddlePool;

        public int TotalCreatedCount => allCreatedActors.Count;
        private CombatSession subscribedSession;
        private Action<EnemyActor> returnCallback;
        private Action<EnemyActor> ReturnCallback => returnCallback ??= ReturnToPool;

        private void OnEnable() => SubscribeToSession();
        private void OnDisable() => UnsubscribeFromSession();

        private void SubscribeToSession()
        {
            if (subscribedSession == session) return;
            UnsubscribeFromSession();
            if (session == null || !isActiveAndEnabled) return;
            subscribedSession = session;
            subscribedSession.SessionReset += DespawnAllActive;
        }

        private void UnsubscribeFromSession()
        {
            if (subscribedSession != null)
                subscribedSession.SessionReset -= DespawnAllActive;
            subscribedSession = null;
        }

        public void BindReferences(CombatSession combatSession, PlayerHealth player, CorrosivePuddlePool corrosivePool = null)
        {
            session = combatSession;
            playerHealth = player;
            if (corrosivePool != null)
                puddlePool = corrosivePool;
            SubscribeToSession();
        }

        public void Prewarm(EnemyArchetypeId archetypeId, int count)
        {
            if (!inactivePools.TryGetValue(archetypeId, out Stack<EnemyActor> stack))
            {
                stack = new Stack<EnemyActor>(count);
                inactivePools[archetypeId] = stack;
            }

            EnemyArchetypeData data = EnemyArchetypeData.CreateDefault(archetypeId);
            while (stack.Count < count)
            {
                EnemyActor actor = CreateInstance(data);
                actor.gameObject.SetActive(false);
                stack.Push(actor);
            }
        }

        public EnemyActor Spawn(EnemyArchetypeId archetypeId, Vector3 position)
        {
            EnemyArchetypeData data = EnemyArchetypeData.CreateDefault(archetypeId);
            return Spawn(data, position);
        }

        public EnemyActor Spawn(EnemyArchetypeData data, Vector3 position)
        {
            if (!inactivePools.TryGetValue(data.ArchetypeId, out Stack<EnemyActor> stack))
            {
                stack = new Stack<EnemyActor>(32);
                inactivePools[data.ArchetypeId] = stack;
            }

            EnemyActor actor = null;
            while (stack.Count > 0 && actor == null)
            {
                actor = stack.Pop();
            }

            if (actor == null)
            {
                actor = CreateInstance(data);
            }

            actor.ReturnToPoolCallback = ReturnCallback;
            if (puddlePool != null)
                actor.BindPuddlePool(puddlePool);
            actor.InitializeOrResetFromPool(data, session, playerHealth, position);
            return actor;
        }

        public void ReturnToPool(EnemyActor actor)
        {
            if (actor == null)
                return;

            CombatTargetRegistry.Unregister(actor);
            actor.gameObject.SetActive(false);

            EnemyArchetypeId id = actor.ArchetypeId;
            if (!inactivePools.TryGetValue(id, out Stack<EnemyActor> stack))
            {
                stack = new Stack<EnemyActor>(32);
                inactivePools[id] = stack;
            }

            if (!stack.Contains(actor))
                stack.Push(actor);
        }

        public void DespawnAllActive()
        {
            for (int i = 0; i < allCreatedActors.Count; i++)
            {
                EnemyActor actor = allCreatedActors[i];
                if (actor != null && actor.gameObject.activeSelf)
                    ReturnToPool(actor);
            }
        }

        private EnemyActor CreateInstance(EnemyArchetypeData data)
        {
            var go = new GameObject($"Enemy_{data.DisplayName}_{allCreatedActors.Count}");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            go.layer = PhysicsLayers.EnemyLayer;
            go.tag = PhysicsLayers.EnemyTag;

            EnemyActor actor = data.ArchetypeId == EnemyArchetypeId.GolgoraBoss
                ? go.AddComponent<GolgoraActor>()
                : go.AddComponent<EnemyActor>();
            actor.ReturnToPoolCallback = ReturnCallback;
            if (puddlePool != null)
                actor.BindPuddlePool(puddlePool);
            actor.PrepareForPool(data, session, playerHealth);
            allCreatedActors.Add(actor);
            return actor;
        }
    }
}
