using System;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Player
{
    /// <summary>
    /// Único componente que escribe la velocidad del Rigidbody del jugador.
    /// Resuelve locomoción y dash (22 m/s, 0.15 s, cooldown 1 s tras terminar, invulnerabilidad y capa de faseo).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(PlayerInputReader))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [SerializeField] private LocomotionSettings settings;
        [SerializeField] private Transform visual;
        [SerializeField] private PlayerRuntimeStats runtimeStats;
        [SerializeField] private ActorVisualComposer visualComposer;

        private Rigidbody body;
        private PlayerInputReader input;
        private Collider playerCollider;

        private bool isDashing;
        private Vector3 dashDirection = Vector3.forward;
        private float activeDashSpeed = 22f;
        private float dashTimeRemaining;
        private float cooldownRemaining;
        private float activeCooldownDuration = 1f;
        private bool wallBlockedThisDash;
        private int savedLayer;
        private bool dashLayerApplied;

        public LocomotionSettings Settings => settings;
        public Vector3 FacingDirection { get; private set; } = Vector3.forward;
        public Vector3 Velocity => body != null ? body.linearVelocity : Vector3.zero;

        public bool IsDashing => isDashing;
        public bool IsDashInvulnerable => isDashing;
        public bool IsDashAvailable => !isDashing && cooldownRemaining <= 0.0001f && isActiveAndEnabled && (input == null || input.CanRead);
        public float DashTimeRemaining => Mathf.Max(0f, dashTimeRemaining);
        public float DashCooldownRemaining => Mathf.Max(0f, cooldownRemaining);
        public float ActiveDashCooldownDuration => activeCooldownDuration;
        public Vector3 DashDirection => dashDirection;
        public bool WasBlockedByWallThisDash => wallBlockedThisDash;
        public int WallBlockReactionCount { get; private set; }

        public float EffectiveDashSpeed => ResolveRuntimeStats() != null ? runtimeStats.DashSpeed : (settings != null ? settings.DashSpeed : 22f);
        public float EffectiveDashDuration => ResolveRuntimeStats() != null ? runtimeStats.DashDuration : (settings != null ? settings.DashDuration : 0.15f);
        public float EffectiveDashCooldown => ResolveRuntimeStats() != null ? runtimeStats.DashCooldown : (settings != null ? settings.DashCooldown : 1.0f);

        public event Action<DashStateEvent> OnDashStateChanged;
        public event Action<Vector3, Vector3> OnDashBlockedByWall;

        private void Awake()
        {
            PhysicsLayers.EnsureConfigured();
            body = GetComponent<Rigidbody>();
            input = GetComponent<PlayerInputReader>();
            playerCollider = GetComponent<Collider>();
            ResolveRuntimeStats();
            ResolveVisualComposer();

            savedLayer = gameObject.layer;

            if (settings == null || visual == null)
            {
                Debug.LogError("[PlayerMotor] Asigna la configuración de locomoción y el hijo Visual.", this);
                enabled = false;
            }
        }

        private PlayerRuntimeStats ResolveRuntimeStats()
        {
            if (runtimeStats == null)
                runtimeStats = GetComponent<PlayerRuntimeStats>();
            return runtimeStats;
        }

        private ActorVisualComposer ResolveVisualComposer()
        {
            if (visualComposer == null)
                visualComposer = GetComponent<ActorVisualComposer>();
            return visualComposer;
        }

        private void OnEnable()
        {
            PhysicsLayers.EnsureConfigured();
            if (input != null)
                input.ControlsSuppressed += HandleControlsSuppressed;
        }

        private void OnDisable()
        {
            if (input != null)
                input.ControlsSuppressed -= HandleControlsSuppressed;
            CancelDashAndRestoreState();
            Stop();
        }

        private void FixedUpdate()
        {
            if (Time.timeScale <= 0f)
                return;

            if (!input.CanRead)
            {
                if (!isDashing)
                    Stop();
                return;
            }

            float dt = Time.fixedDeltaTime;

            // Si el dash activo completó su duración exacta en el paso anterior, cerrarlo ahora
            if (isDashing && dashTimeRemaining <= 0.00001f)
            {
                CompleteDash();
            }

            // Reducir el cooldown únicamente después de haber terminado el dash
            if (!isDashing && cooldownRemaining > 0f)
            {
                float previousCooldown = cooldownRemaining;
                cooldownRemaining = Mathf.Max(0f, cooldownRemaining - dt);
                if (previousCooldown > 0f && cooldownRemaining <= 0.00001f)
                {
                    input.ClearPendingDash();
                    PublishDashEvent(started: false, ended: false, blocked: false);
                }
            }

            Vector2 movement = input.Movement;
            Vector3 moveDirection = new Vector3(movement.x, 0f, movement.y);

            // Iniciar en el siguiente paso físico disponible si está disponible y hubo una nueva pulsación
            if (!isDashing && cooldownRemaining <= 0.0001f && input.ConsumeDashRequest())
            {
                StartDashInFixedStep(moveDirection);
            }
            else if (isDashing || cooldownRemaining > 0.0001f)
            {
                // No almacenar pulsaciones durante el dash o su cooldown: exigir una nueva pulsación tras estar disponible
                input.ClearPendingDash();
            }

            if (isDashing)
            {
                StepActiveDash(dt);
                ArenaSurface.Follow(body, 0.45f, 1f, dt);
                return;
            }

            if (moveDirection.sqrMagnitude > 0.0001f)
                FacingDirection = moveDirection.normalized;

            Vector3 target = moveDirection * settings.MoveSpeed;
            float rate = moveDirection.sqrMagnitude > 0.0001f
                ? settings.Acceleration : settings.Deceleration;

            // Control arcade deliberado: PhysX resuelve las colisiones después de fijar
            // la velocidad deseada. El Transform de la raíz nunca se mueve directamente.
            body.linearVelocity = Vector3.MoveTowards(body.linearVelocity, target, rate * dt);
            ArenaSurface.Follow(body, 0.45f, 1f, dt);
        }

        private void StartDashInFixedStep(Vector3 currentMoveDirection)
        {
            dashDirection = currentMoveDirection.sqrMagnitude > 0.0001f
                ? currentMoveDirection.normalized
                : (FacingDirection.sqrMagnitude > 0.0001f ? FacingDirection.normalized : Vector3.forward);

            FacingDirection = dashDirection;
            activeDashSpeed = EffectiveDashSpeed;
            dashTimeRemaining = EffectiveDashDuration;
            activeCooldownDuration = EffectiveDashCooldown;
            wallBlockedThisDash = false;
            isDashing = true;

            ApplyDashLayer(true);
            ResolveVisualComposer()?.SetDashState(true);
            PublishDashEvent(started: true, ended: false, blocked: false);
        }

        private void StepActiveDash(float dt)
        {
            if (dashTimeRemaining >= dt - 0.00001f)
            {
                body.linearVelocity = dashDirection * activeDashSpeed;
                dashTimeRemaining = Mathf.Max(0f, dashTimeRemaining - dt);
            }
            else
            {
                float partialFraction = Mathf.Clamp01(dashTimeRemaining / Mathf.Max(0.0001f, dt));
                body.linearVelocity = dashDirection * (activeDashSpeed * partialFraction);
                dashTimeRemaining = 0f;
            }
        }

        private void CompleteDash()
        {
            if (!isDashing)
                return;

            isDashing = false;
            dashTimeRemaining = 0f;
            cooldownRemaining = activeCooldownDuration;
            ApplyDashLayer(false);
            body.linearVelocity = Vector3.zero;
            ResolveVisualComposer()?.SetDashState(false, endedNow: true);
            PublishDashEvent(started: false, ended: true, blocked: false);
        }

        public void CancelDashAndRestoreState(bool resetCooldown = false)
        {
            bool wasDashing = isDashing;
            isDashing = false;
            dashTimeRemaining = 0f;
            wallBlockedThisDash = false;
            if (resetCooldown)
                cooldownRemaining = 0f;

            ApplyDashLayer(false);
            input?.ClearPendingDash();
            if (body != null)
                body.linearVelocity = Vector3.zero;
            ResolveVisualComposer()?.SetDashState(false);

            if (wasDashing)
                PublishDashEvent(started: false, ended: true, blocked: false);
        }

        private void ApplyDashLayer(bool enableDashLayer)
        {
            PhysicsLayers.EnsureConfigured();
            if (enableDashLayer)
            {
                if (!dashLayerApplied)
                {
                    savedLayer = gameObject.layer;
                    dashLayerApplied = true;
                }
                gameObject.layer = PhysicsLayers.PlayerDashLayer;
                if (playerCollider != null)
                    playerCollider.gameObject.layer = PhysicsLayers.PlayerDashLayer;
            }
            else if (dashLayerApplied)
            {
                gameObject.layer = savedLayer;
                if (playerCollider != null)
                    playerCollider.gameObject.layer = savedLayer;
                dashLayerApplied = false;
            }
        }

        private void OnCollisionEnter(Collision collision) => CheckWallBlockCollision(collision);
        private void OnCollisionStay(Collision collision) => CheckWallBlockCollision(collision);

        private void CheckWallBlockCollision(Collision collision)
        {
            if (!isDashing || wallBlockedThisDash || collision == null)
                return;

            int count = collision.contactCount;
            for (int i = 0; i < count; i++)
            {
                ContactPoint cp = collision.GetContact(i);
                if (Mathf.Abs(cp.normal.y) > 0.5f)
                    continue;

                Vector3 planarNormal = new Vector3(cp.normal.x, 0f, cp.normal.z);
                if (planarNormal.sqrMagnitude < 0.001f)
                    continue;

                planarNormal.Normalize();
                if (Vector3.Dot(planarNormal, dashDirection) < -0.20f)
                {
                    wallBlockedThisDash = true;
                    WallBlockReactionCount++;
                    ResolveVisualComposer()?.SetDashState(true, endedNow: false, blockedByWallNow: true);
                    OnDashBlockedByWall?.Invoke(cp.point, planarNormal);
                    PublishDashEvent(started: false, ended: false, blocked: true);
                    return;
                }
            }
        }

        private void Update()
        {
            ActorVisualComposer composer = ResolveVisualComposer();
            if (composer != null && settings != null)
            {
                composer.ReportLocomotion(FacingDirection, Velocity, settings.MoveSpeed, Time.time);
                if (Time.timeScale > 0f)
                    composer.StepVisuals(Time.deltaTime, settings.TurnSpeed);
                return;
            }

            if (visual != null && settings != null)
                visual.rotation = Quaternion.RotateTowards(visual.rotation,
                    Quaternion.LookRotation(FacingDirection, Vector3.up),
                    settings.TurnSpeed * Time.deltaTime);
        }

        private void HandleControlsSuppressed()
        {
            Stop();
        }

        public void Stop()
        {
            if (body != null)
                body.linearVelocity = Vector3.zero;
        }

        public void ResetDashForSession()
        {
            WallBlockReactionCount = 0;
            CancelDashAndRestoreState(resetCooldown: true);
            Vector3 initialFacing = transform.forward;
            initialFacing.y = 0f;
            FacingDirection = initialFacing.sqrMagnitude > 0.0001f ? initialFacing.normalized : Vector3.forward;
            dashDirection = FacingDirection;
        }

        private void PublishDashEvent(bool started, bool ended, bool blocked)
        {
            var evt = new DashStateEvent(
                started,
                ended,
                blocked,
                isDashing,
                IsDashAvailable,
                transform.position,
                dashDirection,
                DashCooldownRemaining,
                activeCooldownDuration);
            OnDashStateChanged?.Invoke(evt);
        }
    }
}
