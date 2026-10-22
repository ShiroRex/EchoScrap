using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Dummy de combate para pruebas de daño, mitigación, colisión de colliders grandes,
    /// hit-stop local, flash por MaterialPropertyBlock y residuo visual de muerte sin colisión.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatDummy : MonoBehaviour, IDamageTarget
    {
        [SerializeField] private CombatSession session;
        [SerializeField] private PlayerHealth targetPlayer;
        [SerializeField] private Collider bodyCollider;
        [SerializeField] private ActorVisualComposer visualComposer;

        [Header("Salud")]
        [SerializeField, Min(1f)] private float maxHealth = 30f;
        [SerializeField] private bool isBossDummy;

        [Header("Contacto Dañino")]
        [SerializeField] private bool dealsContactDamage = true;
        [SerializeField, Min(0f)] private float contactDamage = 10f;
        [SerializeField, Min(0.05f)] private float contactCooldown = 0.50f;
        [SerializeField, Min(0.18f)] private float deathVisualDuration = 0.22f;

        private float currentHealth;
        private float nextContactReadyTime;
        private bool isDead;
        private bool deathRewardResolved;
        private float deathVisualTimer;
        private bool initialized;

        public int TargetId => gameObject.GetHashCode();
        public float CurrentHealth => EnsureInitialized() ? currentHealth : 0f;
        public float MaxHealth => maxHealth;
        public bool IsAlive => EnsureInitialized() && !isDead && currentHealth > 0f;
        public bool IsDead => !IsAlive;
        public bool IsPlayingDeathVisual => isDead && deathVisualTimer > 0f;
        public bool DeathRewardResolved => deathRewardResolved;
        public int DeathNotifyCount { get; private set; }
        public Vector3 WorldCenter => transform.position;
        public Collider BodyCollider => bodyCollider;
        public ActorVisualComposer VisualComposer => visualComposer;

        public float HitRadius
        {
            get
            {
                if (bodyCollider is CapsuleCollider cap)
                    return cap.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
                if (bodyCollider is SphereCollider sph)
                    return sph.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
                if (bodyCollider != null)
                {
                    Vector3 ext = bodyCollider.bounds.extents;
                    return Mathf.Max(ext.x, ext.z);
                }
                return 0.5f * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        public bool EnsureInitialized()
        {
            if (initialized)
                return true;

            initialized = true;
            if (bodyCollider == null)
                bodyCollider = GetComponent<Collider>();
            if (visualComposer == null)
                visualComposer = GetComponent<ActorVisualComposer>();
            if (visualComposer == null)
                visualComposer = gameObject.AddComponent<ActorVisualComposer>();
            if (session == null)
                session = FindAnyObjectByType<CombatSession>();
            if (targetPlayer == null)
                targetPlayer = FindAnyObjectByType<PlayerHealth>();

            currentHealth = maxHealth;
            return true;
        }

        public void Configure(
            CombatSession combatSession,
            PlayerHealth player,
            float hp = 30f,
            bool enableContact = true,
            float contactDmg = 10f,
            float contactCd = 0.50f,
            bool asBossDummy = false)
        {
            EnsureInitialized();
            session = combatSession;
            targetPlayer = player;
            maxHealth = Mathf.Max(1f, hp);
            currentHealth = maxHealth;
            dealsContactDamage = enableContact;
            contactDamage = Mathf.Max(0f, contactDmg);
            contactCooldown = Mathf.Max(0.05f, contactCd);
            isBossDummy = asBossDummy;
            ResetDummy();
        }

        private void FixedUpdate()
        {
            EnsureInitialized();
            if (session != null && session.IsPaused)
                return;
            if (Time.timeScale <= 0f)
                return;

            TryDealContactDamage();
        }

        private void Update()
        {
            EnsureInitialized();
            if (session != null && session.IsPaused)
                return;
            if (Time.timeScale <= 0f)
                return;

            StepVisuals(Time.deltaTime);
        }

        public void StepVisuals(float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            if (isDead)
            {
                if (deathVisualTimer > 0f)
                {
                    deathVisualTimer = Mathf.Max(0f, deathVisualTimer - deltaTime);
                    if (visualComposer != null && visualComposer.PresentationPivot != null)
                    {
                        float t = Mathf.Clamp01(deathVisualTimer / Mathf.Max(0.01f, deathVisualDuration));
                        visualComposer.PresentationPivot.localScale = new Vector3(1.25f - 0.25f * t, Mathf.Max(0.05f, t), 1.25f - 0.25f * t);
                    }
                    if (deathVisualTimer <= 0f && visualComposer != null && visualComposer.OrientationRoot != null)
                    {
                        visualComposer.OrientationRoot.gameObject.SetActive(false);
                    }
                }
                return;
            }

            if (visualComposer != null)
            {
                bool reduced = session != null && session.Effects.ReducedEffectsMode;
                visualComposer.StepVisuals(deltaTime, 720f, reduced);
            }
        }

        public bool TryDealContactDamage()
        {
            if (!dealsContactDamage || !IsAlive || targetPlayer == null || !targetPlayer.IsAlive)
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

            nextContactReadyTime = now + contactCooldown;
            var request = new DamageRequest(
                TargetId,
                DamageSourceType.Contact,
                transform.position,
                hitDirection,
                contactDamage);
            targetPlayer.ReceiveDamage(in request);
            return true;
        }

        public DamageResult ReceiveDamage(in DamageRequest request)
        {
            EnsureInitialized();
            Vector3 worldPos = transform.position;

            if (!IsAlive || request.BaseDamage <= 0f || float.IsNaN(request.BaseDamage))
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
                    maxHealth,
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
            bool causedDeath = currentHealth <= 0f;

            if (visualComposer != null)
            {
                bool reduced = session != null && session.Effects.ReducedEffectsMode;
                visualComposer.TriggerHitReaction(request.HitDirection, isCritical, isPlayerHurt: false, reducedEffects: reduced);
            }

            // El resultado de daño conserva posición y datos antes de desactivar o reciclar
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
                maxHealth,
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

        private void ExecuteDeath(Vector3 deathPosition, Vector3 hitDirection, DamageSourceType cause)
        {
            if (isDead)
                return;

            isDead = true;
            deathRewardResolved = true;
            DeathNotifyCount++;
            deathVisualTimer = deathVisualDuration;

            // La muerte desactiva inmediatamente daño, selección y colisión del actor
            if (bodyCollider != null)
                bodyCollider.enabled = false;

            var deathEvent = new ActorDeathEvent(
                TargetId,
                isBossDummy ? 99 : 0,
                false,
                isBossDummy,
                deathPosition,
                hitDirection,
                cause,
                isBossDummy ? 500 : 10,
                false,
                true);

            if (session != null)
            {
                if (isBossDummy)
                    session.QueueBossDeath(in deathEvent);
                else
                    session.Events.PublishDeath(in deathEvent);
            }
        }

        public void ResetDummy()
        {
            EnsureInitialized();
            isDead = false;
            deathRewardResolved = false;
            DeathNotifyCount = 0;
            deathVisualTimer = 0f;
            nextContactReadyTime = 0f;
            currentHealth = maxHealth;

            if (bodyCollider != null)
                bodyCollider.enabled = true;
            if (visualComposer != null)
            {
                if (visualComposer.OrientationRoot != null)
                    visualComposer.OrientationRoot.gameObject.SetActive(true);
                visualComposer.ResetVisualState();
            }
        }
    }
}
