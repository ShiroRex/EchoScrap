using System.Collections.Generic;
using EchoScrap.Core;
using EchoScrap.Player;
using Unity.Cinemachine;
using UnityEngine;

namespace EchoScrap.Combat
{
    public enum ShakePriority
    {
        Low = 1,       // Cuarto pulso de linterna
        Medium = 2,    // Muerte de Coloso o explosión de Polilla
        High = 3,      // Daño recibido por el jugador
        Critical = 4   // Ataques o derrota de Gólgora
    }

    /// <summary>
    /// Extensión de Cinemachine para combate:
    /// 1. Anticipación de cámara: predice 0.10 s de velocidad física real, sin seguir el autoapuntado,
    ///    hasta 1.2 m (entrada 0.08 s, retorno 0.12 s), con warp limpio al teletransportar > 4 m.
    /// 2. Coordinador único de Camera Shake con política UseLargest (sin sumar amplitudes),
    ///    desplazamiento exclusivo en XZ y soporte de intensidad (100% / 50% / 0%).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatCameraFeedback : CinemachineExtension
    {
        public const float SustainedDirectionDelay = 0.10f;
        public const float PredictionTime = SustainedDirectionDelay;
        public const float MaxLeadDistance = 1.20f;
        public const float LeadEntryDuration = 0.08f;
        public const float LeadReturnDuration = 0.12f;
        public const float TeleportWarpThreshold = 4.00f;

        public const float FourthPulseShakeAmplitude = 0.08f;
        public const float FourthPulseShakeDuration = 0.12f;

        public const float MediumExplosionShakeAmplitude = 0.14f;
        public const float MediumExplosionShakeDuration = 0.16f;

        public const float PlayerHurtShakeAmplitude = 0.24f;
        public const float PlayerHurtShakeDuration = 0.30f;

        public const float BossCriticalShakeAmplitude = 0.35f;
        public const float BossCriticalShakeDuration = 0.45f;
        public const float MaxShakeAmplitude = 0.35f;

        [SerializeField] private CombatSession session;
        [SerializeField] private Transform trackedTarget;
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private PlayerMotor playerMotor;
        [SerializeField] private FlashlightWeapon flashlightWeapon;
        [SerializeField, Min(1f)] private float shakeFrequency = 26f;

        private struct ActiveShake
        {
            public ShakePriority Priority;
            public float BaseAmplitude;
            public float Duration;
            public float Elapsed;
            public float PhaseSeed;
            public float Frequency;
            public bool ClosingCinematic;
        }

        private readonly List<ActiveShake> activeShakes = new List<ActiveShake>(8);
        private Rigidbody trackedBody;

        private Vector3 lastTargetPosition;
        private bool hasLastTargetPosition;
        private Vector3 sustainedReferenceDir;
        private float sustainedDirectionTimer;
        private Vector3 currentLeadOffsetXZ;
        private Vector3 currentShakeOffsetXZ;
        private float currentWinningShakeAmplitude;
        private ShakePriority? currentWinningPriority;

        public Vector3 CurrentLeadOffsetXZ => currentLeadOffsetXZ;
        public Vector3 CurrentShakeOffsetXZ => currentShakeOffsetXZ;
        public float CurrentWinningShakeAmplitude => currentWinningShakeAmplitude;
        public ShakePriority? CurrentWinningPriority => currentWinningPriority;
        public float SustainedDirectionTimer => sustainedDirectionTimer;
        public int WarpCount { get; private set; }
        public bool IsShaking => activeShakes.Count > 0 && currentWinningShakeAmplitude > 0.0001f;

        protected override void OnEnable()
        {
            base.OnEnable();
            ResolveReferences();
            SubscribeSessionEvents();
            ResetFeedbackState();
        }

        private void OnDisable()
        {
            UnsubscribeSessionEvents();
            ResetFeedbackState();
        }

        protected override void OnDestroy()
        {
            UnsubscribeSessionEvents();
            base.OnDestroy();
        }

        public void BindReferences(
            CombatSession combatSession,
            Transform target,
            PlayerInputReader input = null,
            PlayerMotor motor = null,
            FlashlightWeapon weapon = null)
        {
            UnsubscribeSessionEvents();
            session = combatSession;
            trackedTarget = target;
            inputReader = input;
            playerMotor = motor;
            flashlightWeapon = weapon;
            ResolveReferences();
            if (isActiveAndEnabled)
                SubscribeSessionEvents();
            ResetFeedbackState();
        }

        private void ResolveReferences()
        {
            if (session == null)
                session = FindAnyObjectByType<CombatSession>();

            if (trackedTarget == null && ComponentOwner != null && ComponentOwner.Follow != null)
                trackedTarget = ComponentOwner.Follow;

            if (trackedTarget == null)
            {
                var foundMotor = FindAnyObjectByType<PlayerMotor>();
                if (foundMotor != null)
                    trackedTarget = foundMotor.transform;
            }

            if (trackedTarget != null)
            {
                trackedBody = trackedTarget.GetComponent<Rigidbody>();
                if (inputReader == null)
                    inputReader = trackedTarget.GetComponent<PlayerInputReader>();
                if (playerMotor == null)
                    playerMotor = trackedTarget.GetComponent<PlayerMotor>();
                if (flashlightWeapon == null)
                    flashlightWeapon = trackedTarget.GetComponent<FlashlightWeapon>();
            }
            else
            {
                trackedBody = null;
            }
        }

        private void SubscribeSessionEvents()
        {
            if (session == null)
                return;

            session.Events.OnPulseResolved -= HandlePulseResolved;
            session.Events.OnDamageResolved -= HandleDamageResolved;
            session.Events.OnActorDied -= HandleActorDied;
            session.Events.OnSessionStateChanged -= HandleSessionStateChanged;
            session.Effects.OnPreferencesChanged -= HandlePreferencesChanged;

            session.Events.OnPulseResolved += HandlePulseResolved;
            session.Events.OnDamageResolved += HandleDamageResolved;
            session.Events.OnActorDied += HandleActorDied;
            session.Events.OnSessionStateChanged += HandleSessionStateChanged;
            session.Effects.OnPreferencesChanged += HandlePreferencesChanged;
        }

        private void UnsubscribeSessionEvents()
        {
            if (session == null)
                return;

            session.Events.OnPulseResolved -= HandlePulseResolved;
            session.Events.OnDamageResolved -= HandleDamageResolved;
            session.Events.OnActorDied -= HandleActorDied;
            session.Events.OnSessionStateChanged -= HandleSessionStateChanged;
            session.Effects.OnPreferencesChanged -= HandlePreferencesChanged;
        }

        private void HandleSessionStateChanged(SessionStateEvent stateEvent)
        {
            if (stateEvent.State == SessionState.Victory)
            {
                // La partida queda congelada; solo continúa la sacudida final del jefe.
                activeShakes.RemoveAll(shake => !shake.ClosingCinematic);
                currentLeadOffsetXZ = Vector3.zero;
                return;
            }
            if (stateEvent.PauseReasons != PauseReason.None
                || (stateEvent.State == SessionState.Running && stateEvent.GameplayTime <= 0f))
                ResetFeedbackState();
        }

        private void HandlePreferencesChanged()
        {
            if (session != null && session.Effects.ReducedEffectsMode)
                ResetFeedbackState();
        }

        public void ResetFeedbackState()
        {
            activeShakes.Clear();
            currentLeadOffsetXZ = Vector3.zero;
            currentShakeOffsetXZ = Vector3.zero;
            currentWinningShakeAmplitude = 0f;
            currentWinningPriority = null;
            sustainedDirectionTimer = 0f;
            sustainedReferenceDir = Vector3.zero;
            WarpCount = 0;

            if (trackedTarget != null)
            {
                lastTargetPosition = trackedTarget.position;
                hasLastTargetPosition = true;
            }
            else
            {
                hasLastTargetPosition = false;
            }
        }

        public override void OnTargetObjectWarped(CinemachineVirtualCameraBase vcam, Transform target, Vector3 positionDelta)
        {
            if (target != trackedTarget) return;
            // Warehouse travel and the foundry already notify Cinemachine explicitly. Consume that
            // notification so the next frame's automatic detector does not apply the same warp twice.
            lastTargetPosition = trackedTarget.position;
            hasLastTargetPosition = true;
            sustainedDirectionTimer = 0f;
            sustainedReferenceDir = Vector3.zero;
            currentLeadOffsetXZ = Vector3.zero;
        }

        private void HandlePulseResolved(PulseResolvedEvent pulse)
        {
            if (pulse.PulseIndex == FlashlightWeapon.PulsesPerBurst)
            {
                RequestShake(ShakePriority.Low, FourthPulseShakeAmplitude, FourthPulseShakeDuration, 24f);
            }
        }

        private void HandleDamageResolved(DamageResult result)
        {
            if (result.IsPlayerTarget && result.AppliedRealDamage)
            {
                RequestShake(ShakePriority.High, PlayerHurtShakeAmplitude, PlayerHurtShakeDuration, 10f);
            }
        }

        private void HandleActorDied(ActorDeathEvent deathEvent)
        {
            if (deathEvent.IsBoss)
            {
                RequestShakeInternal(ShakePriority.Critical, BossCriticalShakeAmplitude, BossCriticalShakeDuration, 8f, true);
            }
            else if (deathEvent.ArchetypeId == (int)EnemyArchetypeId.Colossus
                     || deathEvent.ArchetypeId == (int)EnemyArchetypeId.ExplosiveMoth)
            {
                RequestShake(ShakePriority.Medium, MediumExplosionShakeAmplitude, MediumExplosionShakeDuration, 14f);
            }
        }

        public void RequestPresetShake(ShakePriority priority)
        {
            switch (priority)
            {
                case ShakePriority.Low:
                    RequestShake(ShakePriority.Low, FourthPulseShakeAmplitude, FourthPulseShakeDuration, 24f);
                    break;
                case ShakePriority.Medium:
                    RequestShake(ShakePriority.Medium, MediumExplosionShakeAmplitude, MediumExplosionShakeDuration, 14f);
                    break;
                case ShakePriority.High:
                    RequestShake(ShakePriority.High, PlayerHurtShakeAmplitude, PlayerHurtShakeDuration, 10f);
                    break;
                case ShakePriority.Critical:
                    RequestShake(ShakePriority.Critical, BossCriticalShakeAmplitude, BossCriticalShakeDuration, 8f);
                    break;
            }
        }

        /// <summary>
        /// Registra o reemplaza una sacudida de su misma prioridad para ser evaluada con política UseLargest.
        /// </summary>
        public void RequestShake(ShakePriority priority, float amplitude, float duration, float frequency = 0f)
            => RequestShakeInternal(priority, amplitude, duration, frequency, false);

        private void RequestShakeInternal(ShakePriority priority, float amplitude, float duration, float frequency, bool closingCinematic)
        {
            float scale = GetEffectiveShakeScale();
            if (scale <= 0.0001f || amplitude <= 0.0001f || duration <= 0.0001f
                || (session != null && session.IsPaused && !(closingCinematic && session.State == SessionState.Victory)))
                return;

            amplitude = Mathf.Min(amplitude, MaxShakeAmplitude);

            float phaseSeed = session != null
                ? session.Rng.Presentation.Range(0f, Mathf.PI * 2f)
                : ((int)priority * 1.17f);

            var newShake = new ActiveShake
            {
                Priority = priority,
                BaseAmplitude = amplitude,
                Duration = duration,
                Elapsed = 0f,
                PhaseSeed = phaseSeed,
                Frequency = frequency > 0f ? frequency : shakeFrequency,
                ClosingCinematic = closingCinematic
            };

            for (int i = 0; i < activeShakes.Count; i++)
            {
                if (activeShakes[i].Priority == priority)
                {
                    activeShakes[i] = newShake;
                    return;
                }
            }

            activeShakes.Add(newShake);
        }

        private float GetEffectiveShakeScale()
        {
            if (session == null)
                return 1f;
            return session.Effects.ShakeIntensity;
        }

        public void StepFeedback(float deltaTime, CinemachineVirtualCameraBase vcam = null)
        {
            if (deltaTime < 0f)
                return;
            if ((session != null && session.IsPaused) || Time.timeScale <= 0f)
            {
                if (session != null && session.State == SessionState.Victory)
                {
                    currentLeadOffsetXZ = Vector3.zero;
                    StepCameraShake(deltaTime);
                    return;
                }
                ResetFeedbackState();
                return;
            }

            StepCameraLead(deltaTime, vcam);
            StepCameraShake(deltaTime);
        }

        private void StepCameraLead(float deltaTime, CinemachineVirtualCameraBase vcam)
        {
            bool warped = false;
            if (trackedTarget != null)
            {
                Vector3 currentPos = trackedTarget.position;
                if (hasLastTargetPosition)
                {
                    Vector3 delta = currentPos - lastTargetPosition;
                    Vector2 planarDelta = new Vector2(delta.x, delta.z);
                    if (planarDelta.magnitude > TeleportWarpThreshold)
                    {
                        WarpCount++;
                        sustainedDirectionTimer = 0f;
                        currentLeadOffsetXZ = Vector3.zero;
                        if (vcam != null)
                            vcam.OnTargetObjectWarped(trackedTarget, delta);
                        warped = true;
                    }
                }
                lastTargetPosition = currentPos;
                hasLastTargetPosition = true;
            }

            if (warped || (session != null && session.Effects.ReducedEffectsMode))
            {
                currentLeadOffsetXZ = Vector3.zero;
                sustainedDirectionTimer = 0f;
                sustainedReferenceDir = Vector3.zero;
                return;
            }

            if (deltaTime <= 0f)
                return;

            Vector3 planarVelocity = DetermineLeadVelocity();
            Vector3 desiredDirection = planarVelocity;
            if (desiredDirection.sqrMagnitude > 0.001f)
            {
                desiredDirection.Normalize();
                if (sustainedReferenceDir.sqrMagnitude > 0.001f && Vector3.Dot(sustainedReferenceDir, desiredDirection) >= 0.70f)
                {
                    sustainedDirectionTimer += deltaTime;
                    sustainedReferenceDir = Vector3.Slerp(sustainedReferenceDir, desiredDirection, 0.35f).normalized;
                }
                else
                {
                    sustainedReferenceDir = desiredDirection;
                    sustainedDirectionTimer = deltaTime;
                }
            }
            else
            {
                sustainedDirectionTimer = 0f;
                sustainedReferenceDir = Vector3.zero;
            }

            bool leadActive = planarVelocity.sqrMagnitude > 0.001f;
            Vector3 targetOffset = Vector3.ClampMagnitude(planarVelocity * PredictionTime, MaxLeadDistance);
            float speed = leadActive
                ? (MaxLeadDistance / Mathf.Max(0.001f, LeadEntryDuration))
                : (MaxLeadDistance / Mathf.Max(0.001f, LeadReturnDuration));

            currentLeadOffsetXZ = Vector3.MoveTowards(currentLeadOffsetXZ, targetOffset, speed * deltaTime);
            currentLeadOffsetXZ.y = 0f;
            if (currentLeadOffsetXZ.magnitude > MaxLeadDistance)
                currentLeadOffsetXZ = currentLeadOffsetXZ.normalized * MaxLeadDistance;
        }

        private Vector3 DetermineLeadVelocity()
        {
            Vector3 velocity = playerMotor != null
                ? playerMotor.Velocity
                : (trackedBody != null ? trackedBody.linearVelocity : Vector3.zero);
            velocity.y = 0f;
            return velocity;
        }

        private void StepCameraShake(float deltaTime)
        {
            float scale = GetEffectiveShakeScale();
            if (scale <= 0.0001f)
            {
                activeShakes.Clear();
                currentShakeOffsetXZ = Vector3.zero;
                currentWinningShakeAmplitude = 0f;
                currentWinningPriority = null;
                return;
            }

            bool hasWinner = false;
            ActiveShake winner = default;
            float largestCurrentAmplitude = 0f;
            ShakePriority winningPriority = ShakePriority.Low;

            for (int i = activeShakes.Count - 1; i >= 0; i--)
            {
                ActiveShake shake = activeShakes[i];
                shake.Elapsed += deltaTime;
                if (shake.Elapsed >= shake.Duration)
                {
                    activeShakes.RemoveAt(i);
                    continue;
                }

                activeShakes[i] = shake;
                float normalizedRemaining = Mathf.Clamp01(1f - (shake.Elapsed / Mathf.Max(0.0001f, shake.Duration)));
                float currentAmp = shake.BaseAmplitude * normalizedRemaining * normalizedRemaining * scale;

                // Política UseLargest: escoger la fuente de mayor amplitud actual (o mayor prioridad en empate), nunca sumar
                bool isBetter = currentAmp > largestCurrentAmplitude + 0.0001f
                    || (Mathf.Abs(currentAmp - largestCurrentAmplitude) <= 0.0001f && shake.Priority > winningPriority);

                if (isBetter)
                {
                    largestCurrentAmplitude = currentAmp;
                    winningPriority = shake.Priority;
                    winner = shake;
                    hasWinner = true;
                }
            }

            if (!hasWinner || largestCurrentAmplitude <= 0.0001f)
            {
                currentShakeOffsetXZ = Vector3.zero;
                currentWinningShakeAmplitude = 0f;
                currentWinningPriority = null;
                return;
            }

            currentWinningShakeAmplitude = largestCurrentAmplitude;
            currentWinningPriority = winner.Priority;

            float phase = winner.PhaseSeed + winner.Elapsed * winner.Frequency * Mathf.PI * 2f;
            Vector3 unitXZ = new Vector3(Mathf.Cos(phase), 0f, Mathf.Sin(phase * 1.37f));
            if (unitXZ.sqrMagnitude > 1f)
                unitXZ.Normalize();

            currentShakeOffsetXZ = unitXZ * largestCurrentAmplitude;
            currentShakeOffsetXZ.y = 0f;
        }

        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam,
            CinemachineCore.Stage stage,
            ref CameraState state,
            float deltaTime)
        {
            if (deltaTime < 0f)
            {
                ResetFeedbackState();
                return;
            }

            bool isPaused = (session != null && session.IsPaused) || Time.timeScale <= 0f;
            if (isPaused)
            {
                if (session != null && session.State == SessionState.Victory)
                {
                    currentLeadOffsetXZ = Vector3.zero;
                    if (stage == CinemachineCore.Stage.Noise)
                    {
                        StepCameraShake(Time.unscaledDeltaTime);
                        state.PositionCorrection += currentShakeOffsetXZ;
                    }
                    return;
                }
                ResetFeedbackState();
                return;
            }
            float effectiveDt = isPaused ? 0f : deltaTime;

            if (stage == CinemachineCore.Stage.Body)
            {
                StepCameraLead(effectiveDt, vcam);
                state.PositionCorrection += new Vector3(currentLeadOffsetXZ.x, 0f, currentLeadOffsetXZ.z);
            }
            else if (stage == CinemachineCore.Stage.Noise)
            {
                StepCameraShake(effectiveDt);
                state.PositionCorrection += new Vector3(currentShakeOffsetXZ.x, 0f, currentShakeOffsetXZ.z);
            }
        }
    }
}
