using System;
using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Salud, mitigación e invulnerabilidad del jugador.
    /// Orden estricto: invulnerabilidad -> evasión -> bloqueo -> armadura -> pérdida de salud.
    /// La ventana de protección (0.40 s) y el feedback de daño sólo se activan con daño efectivo (HP perdido > 0).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerRuntimeStats))]
    public sealed class PlayerHealth : MonoBehaviour, IDamageTarget
    {
        [SerializeField] private CombatSession session;
        [SerializeField] private PlayerRuntimeStats stats;
        [SerializeField] private Collider hurtboxCollider;
        [SerializeField] private ActorVisualComposer visualComposer;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerInputReader inputReader;

        private float currentHealth;
        private float lastMaxHealth;
        private float protectionRemaining;
        private bool dashInvulnerable;
        private float upgradeProtection;
        public float UpgradeProtectionRemaining => upgradeProtection;
        public void GrantUpgradeProtection(float duration) => upgradeProtection = Mathf.Max(upgradeProtection, duration);
        private bool isDead;
        private bool initialized;

        public int TargetId => gameObject.GetHashCode();
        public float CurrentHealth => EnsureInitialized() ? currentHealth : 0f;
        public float MaxHealth => EnsureInitialized() ? stats.MaxHealth : 100f;
        public bool IsAlive => EnsureInitialized() && !isDead && currentHealth > 0f;
        public bool IsDead => !IsAlive;
        public bool IsEnemyTarget => false;
        public float ProtectionRemaining => Mathf.Max(0f, protectionRemaining);
        public bool HasDamageProtection => protectionRemaining > 0.0001f;
        public bool IsDashInvulnerable => dashInvulnerable || (motor != null && motor.IsDashInvulnerable);
        public bool IgnoreIncomingDamage { get; set; }
        public bool IsInvulnerable => IgnoreIncomingDamage || HasDamageProtection || IsDashInvulnerable || upgradeProtection > 0f;
        public Vector3 WorldCenter => transform.position;
        public float HitRadius => hurtboxCollider is CapsuleCollider cap ? cap.radius : 0.5f;
        public Collider HurtboxCollider => hurtboxCollider;
        public PlayerRuntimeStats Stats => stats;
        public CombatSession Session => session;

        public event Action<DamageResult> OnDamageProcessed;
        public event Action<HealResult> OnHealed;
        public event Action<float, float> OnHealthChanged;
        public event Action OnDied;

        private void Awake()
        {
            EnsureInitialized();
        }

        public bool EnsureInitialized()
        {
            if (initialized)
                return true;

            initialized = true;
            if (stats == null)
                stats = GetComponent<PlayerRuntimeStats>();
            if (stats == null)
                stats = gameObject.AddComponent<PlayerRuntimeStats>();
            if (hurtboxCollider == null)
                hurtboxCollider = GetComponent<Collider>();
            if (visualComposer == null)
                visualComposer = GetComponent<ActorVisualComposer>();
            if (motor == null)
                motor = GetComponent<PlayerMotor>();
            if (inputReader == null)
                inputReader = GetComponent<PlayerInputReader>();
            if (session == null)
                session = FindAnyObjectByType<CombatSession>();

            currentHealth = stats.MaxHealth;
            lastMaxHealth = currentHealth;
            stats.OnStatsChanged += HandleStatsChanged;
            return true;
        }

        private void OnDestroy()
        {
            if (stats != null)
                stats.OnStatsChanged -= HandleStatsChanged;
        }

        public void BindSession(CombatSession combatSession)
        {
            EnsureInitialized();
            session = combatSession;
        }

        public void SetDashInvulnerable(bool value)
        {
            dashInvulnerable = value;
        }

        public void ApplyKnockback(Vector3 planarImpulse)
        {
            // El jugador no recibe empuje de la propia linterna.
        }

        private void Update()
        {
            EnsureInitialized();
            if (session != null && session.IsPaused)
                return;
            if (Time.timeScale <= 0f)
                return;

            StepTimers(Time.deltaTime);
        }

        public void StepTimers(float deltaTime)
        {
            if (deltaTime <= 0f || isDead)
                return;

            upgradeProtection = Mathf.Max(0f, upgradeProtection - deltaTime);
            if (protectionRemaining > 0f)
                protectionRemaining = Mathf.Max(0f, protectionRemaining - deltaTime);

            if (visualComposer != null)
            {
                bool reduced = session != null && session.Effects.ReducedEffectsMode;
                float turnSpeed = motor != null && motor.Settings != null ? motor.Settings.TurnSpeed : 720f;
                visualComposer.StepVisuals(deltaTime, turnSpeed, reduced);
            }
        }

        public DamageResult TakeContactOrHazardDamage(float amount, Vector3 sourcePosition, int sourceId = 0, DamageSourceType sourceType = DamageSourceType.Contact)
        {
            Vector3 hitDir = transform.position - sourcePosition;
            hitDir.y = 0f;
            if (hitDir.sqrMagnitude < 0.0001f)
                hitDir = Vector3.back;
            else
                hitDir.Normalize();

            var request = new DamageRequest(
                sourceId,
                sourceType,
                sourcePosition,
                hitDir,
                amount);
            return ReceiveDamage(in request);
        }

        /// <summary>
        /// Orden de resolución: invulnerabilidad -> evasión -> bloqueo -> armadura -> pérdida de salud.
        /// </summary>
        public DamageResult ReceiveDamage(in DamageRequest request)
        {
            EnsureInitialized();
            Vector3 worldPos = transform.position;
            float maxHp = stats.MaxHealth;

            if (isDead || currentHealth <= 0f || request.BaseDamage <= 0f || float.IsNaN(request.BaseDamage) || IsInvulnerable)
            {
                var immuneResult = new DamageResult(
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
                    maxHp,
                    false,
                    false,
                    false,
                    true,
                    false,
                    true,
                    request.BurstId,
                    request.PulseIndex, request.DamageType);

                OnDamageProcessed?.Invoke(immuneResult);
                session?.Events.PublishDamage(in immuneResult);
                return immuneResult;
            }

            DeterministicRandom combatRng = session != null ? session.Rng.Combat : new DeterministicRandom(1337);

            // 2. Evasión (no inicia ventana de protección ni reproduce feedback de daño recibido)
            float evasion = stats.Evasion;
            if (evasion > 0f && combatRng.NextFloat() < evasion)
            {
                var dodgedResult = new DamageResult(
                    request.SourceId,
                    TargetId,
                    request.SourceType,
                    request.OriginPosition,
                    worldPos,
                    request.HitDirection,
                    request.BaseDamage,
                    0f,
                    0f,
                    currentHealth,
                    maxHp,
                    false,
                    false,
                    true,
                    false,
                    false,
                    true,
                    request.BurstId,
                    request.PulseIndex, request.DamageType);

                OnDamageProcessed?.Invoke(dodgedResult);
                session?.Events.PublishDamage(in dodgedResult);
                return dodgedResult;
            }

            float workingDamage = request.BaseDamage;

            // 3. Bloqueo
            bool wasBlocked = false;
            float blockChance = stats.BlockChance;
            if (blockChance > 0f && combatRng.NextFloat() < blockChance)
            {
                wasBlocked = true;
                workingDamage *= (1f - stats.BlockDamageReduction);
            }

            // 4. Armadura
            float armor = stats.Armor;
            if (armor > 0f)
            {
                workingDamage *= (1f - armor);
            }

            // 5. Pérdida de salud
            float previousHp = currentHealth;
            currentHealth = Mathf.Max(0f, currentHealth - workingDamage);
            float hpLost = previousHp - currentHealth;
            bool causedDeath = currentHealth <= 0f;

            if (hpLost > 0f)
            {
                protectionRemaining = stats.DamageProtectionDuration;
                if (visualComposer != null)
                {
                    bool reduced = session != null && session.Effects.ReducedEffectsMode;
                    visualComposer.TriggerHitReaction(request.HitDirection, false, isPlayerHurt: true, reducedEffects: reduced);
                }
            }

            var finalResult = new DamageResult(
                request.SourceId,
                TargetId,
                request.SourceType,
                request.OriginPosition,
                worldPos,
                request.HitDirection,
                request.BaseDamage,
                workingDamage,
                hpLost,
                currentHealth,
                maxHp,
                false,
                wasBlocked,
                false,
                false,
                causedDeath,
                true,
                request.BurstId,
                request.PulseIndex, request.DamageType);

            OnHealthChanged?.Invoke(currentHealth, maxHp);
            OnDamageProcessed?.Invoke(finalResult);
            session?.Events.PublishDamage(in finalResult);

            if (causedDeath)
            {
                ExecuteDeath(request.HitDirection, request.SourceType);
            }
            else
            {
                session?.PublishSessionState();
            }

            return finalResult;
        }

        public HealResult Heal(float amount)
        {
            EnsureInitialized();
            float maxHp = stats.MaxHealth;
            if (isDead || amount <= 0f || float.IsNaN(amount))
            {
                return new HealResult(Mathf.Max(0f, amount), 0f, currentHealth, maxHp, currentHealth >= maxHp - 0.0001f);
            }

            bool wasAlreadyFull = currentHealth >= maxHp - 0.0001f;
            float previous = currentHealth;
            currentHealth = Mathf.Min(maxHp, currentHealth + amount);
            float actualHealed = currentHealth - previous;

            var result = new HealResult(amount, actualHealed, currentHealth, maxHp, wasAlreadyFull);
            if (actualHealed > 0f)
            {
                OnHealthChanged?.Invoke(currentHealth, maxHp);
                session?.PublishSessionState();
            }

            OnHealed?.Invoke(result);
            session?.Events.PublishPlayerHealed(in result);
            return result;
        }

        private void HandleStatsChanged()
        {
            float maxHp = stats.MaxHealth;
            if (!Mathf.Approximately(maxHp, lastMaxHealth))
            {
                currentHealth = Mathf.Min(currentHealth, maxHp);
                lastMaxHealth = maxHp;
                OnHealthChanged?.Invoke(currentHealth, maxHp);
            }
        }

        private void ExecuteDeath(Vector3 hitDirection, DamageSourceType cause)
        {
            if (isDead)
                return;

            isDead = true;
            protectionRemaining = 0f;
            dashInvulnerable = false;
            upgradeProtection = 0f;

            // Desactivar inmediatamente daño, selección y colisión del actor; cancelar dash si estaba activo
            if (hurtboxCollider != null)
                hurtboxCollider.enabled = false;
            if (inputReader != null)
                inputReader.SetInputEnabled(false);
            if (motor != null)
            {
                motor.CancelDashAndRestoreState(resetCooldown: true);
                motor.Stop();
                motor.enabled = false;
            }

            var deathEvent = new ActorDeathEvent(
                TargetId,
                -1,
                true,
                false,
                transform.position,
                hitDirection,
                cause,
                0,
                false,
                false);

            OnDied?.Invoke();
            if (session != null)
                session.QueuePlayerDeath(in deathEvent);
        }

        public void ResetForNewSession()
        {
            EnsureInitialized();
            IgnoreIncomingDamage = false;
            isDead = false;
            protectionRemaining = 0f;
            dashInvulnerable = false;
            upgradeProtection = 0f;
            currentHealth = stats.MaxHealth;
            lastMaxHealth = currentHealth;
            if (hurtboxCollider != null)
                hurtboxCollider.enabled = true;
            if (visualComposer != null)
                visualComposer.ResetVisualState(resetOrientation: true);
            OnHealthChanged?.Invoke(currentHealth, currentHealth);
        }

        public void ForceSetHealthForTesting(float health)
        {
            EnsureInitialized();
            currentHealth = Mathf.Clamp(health, 0f, stats.MaxHealth);
            if (currentHealth <= 0f && !isDead)
                ExecuteDeath(Vector3.back, DamageSourceType.DummyOrHazard);
            else
                OnHealthChanged?.Invoke(currentHealth, stats.MaxHealth);
        }
    }
}
