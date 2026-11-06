using System;
using System.Collections.Generic;
using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Linterna automática de 4 pulsos (0.2 / 0.4 / 0.6 / 0.8 s, descanso 0.4 s, ciclo 1.2 s)
    /// y soporte de Hiper-Rayo Solar (FLASH_05: alcance base 14 m, daño x2.5, sin descanso).
    /// Opera en XZ considerando el radio del objetivo en los bordes del cono, atraviesa obstáculos,
    /// daña una sola vez por objetivo y pulso aunque tenga varios colliders, y captura parámetros por ráfaga.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlashlightWeapon : MonoBehaviour
    {
        public const int PulsesPerBurst = 4;

        [SerializeField] private CombatSession session;
        [SerializeField] private PlayerRuntimeStats runtimeStats;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private Transform aimPivot;

        private readonly Collider[] overlapBuffer = new Collider[256];
        private readonly List<IDamageTarget> candidateTargets = new List<IDamageTarget>(256);
        private readonly HashSet<int> hitTargetIdsThisPulse = new HashSet<int>(128);

        private bool initialized;
        private float weaponTime;
        private int currentBurstId;
        private bool isBurstInitialized;
        private bool isInRestPhase;
        private int emittedPulsesInCurrentBurst; // 0..4
        private float burstAnchorTime;
        private float restEndTime;

        // Parámetros capturados al inicio de la ráfaga activa
        private float capturedRange = 7f;
        private float capturedAngle = 60f;
        private float capturedDamagePerPulse = 3.75f;
        private float capturedPulseInterval = 0.20f;
        private float capturedRestDuration = 0.40f;
        private float capturedKnockbackImpulse = 10f;
        private float capturedCritChance;
        private float capturedCritMultiplier = 1.75f;
        private bool capturedSolarMode;

        private Vector3 currentAimDirection = Vector3.forward;
        private IDamageTarget currentTarget;
        private float lastNotifiedRange = -1f;
        private float lastNotifiedAngle = -1f;

        public PlayerRuntimeStats Stats
        {
            get
            {
                EnsureInitialized();
                return runtimeStats;
            }
        }

        public CombatSession Session => session;
        public Transform AimPivot => aimPivot != null ? aimPivot : transform;
        public Vector3 OriginPosition => AimPivot.position;
        public Vector3 CurrentAimDirection => currentAimDirection;
        public IDamageTarget CurrentTarget => currentTarget;

        public float EffectiveRange => Stats != null ? Stats.FlashlightRange : 7f;
        public float EffectiveAngle => Stats != null ? Stats.FlashlightAngle : 60f;
        public float ActiveBurstRange => isBurstInitialized && !isInRestPhase ? capturedRange : EffectiveRange;
        public float ActiveBurstAngle => isBurstInitialized && !isInRestPhase ? capturedAngle : EffectiveAngle;
        public float ActivePulseInterval => isBurstInitialized ? capturedPulseInterval : (Stats != null ? Stats.EffectivePulseInterval : 0.20f);
        public float ActiveRestDuration => isBurstInitialized ? capturedRestDuration : (Stats != null ? Stats.EffectiveBurstRestDuration : 0.40f);
        public float ActiveDamagePerPulse => isBurstInitialized && !isInRestPhase ? capturedDamagePerPulse : (Stats != null ? Stats.FlashlightDamagePerPulse : 3.75f);

        public int CurrentBurstId => currentBurstId;
        public int EmittedPulsesInCurrentBurst => emittedPulsesInCurrentBurst;
        public bool IsBurstActive => isBurstInitialized && !isInRestPhase;
        public bool IsInRestPhase => isBurstInitialized && isInRestPhase;
        public bool IsSolarModeActive => isBurstInitialized ? capturedSolarMode : (Stats != null && Stats.IsSolarActive);
        public float WeaponTime => weaponTime;
        public int TotalPulsesEmitted { get; private set; }

        private int guaranteedPending, guaranteedActive;
        public void QueueGuaranteedCriticalPulses(int count) => guaranteedPending = Mathf.Max(guaranteedPending, count);
        public event Action<int> OnBurstStarted;
        public event Action<PulseResolvedEvent> OnPulseEmitted;
        public event Action<int> OnBurstEnteredRest;
        public event Action OnWeaponStopped;
        public event Action<float, float> OnGeometryChanged;

        private void Awake()
        {
            EnsureInitialized();
        }

        public bool EnsureInitialized()
        {
            if (initialized)
                return true;

            initialized = true;
            if (runtimeStats == null)
                runtimeStats = GetComponent<PlayerRuntimeStats>();
            if (runtimeStats == null)
                runtimeStats = gameObject.AddComponent<PlayerRuntimeStats>();

            if (motor == null)
                motor = GetComponent<PlayerMotor>();
            if (inputReader == null)
                inputReader = GetComponent<PlayerInputReader>();
            if (playerHealth == null)
                playerHealth = GetComponent<PlayerHealth>();
            if (session == null)
                session = FindAnyObjectByType<CombatSession>();

            if (aimPivot == null)
            {
                var composer = GetComponent<ActorVisualComposer>();
                if (composer != null && composer.FlashlightPivot != null)
                    aimPivot = composer.FlashlightPivot;
                else
                {
                    Transform found = transform.Find("FlashlightPivot");
                    if (found == null)
                    {
                        var pivotGo = new GameObject("FlashlightPivot");
                        pivotGo.transform.SetParent(transform, false);
                        found = pivotGo.transform;
                    }
                    aimPivot = found;
                }
            }

            currentAimDirection = FlatDirection(transform.forward, Vector3.forward);
            return true;
        }

        public void BindSession(CombatSession combatSession)
        {
            EnsureInitialized();
            session = combatSession;
        }

        private void OnEnable()
        {
            EnsureInitialized();
            if (playerHealth != null)
                playerHealth.OnDied += HandlePlayerDied;
            ResetWeaponCycle();
            CheckAndPublishGeometryChange();
        }

        private void OnDisable()
        {
            if (playerHealth != null)
                playerHealth.OnDied -= HandlePlayerDied;
            StopWeapon();
        }

        private void HandlePlayerDied()
        {
            StopWeapon();
            enabled = false;
        }

        public void StopWeapon()
        {
            isBurstInitialized = false;
            isInRestPhase = false;
            emittedPulsesInCurrentBurst = 0;
            currentTarget = null;
            OnWeaponStopped?.Invoke();
        }

        public void ResetWeaponCycle()
        {
            EnsureInitialized();
            weaponTime = 0f;
            guaranteedPending = guaranteedActive = 0;
            currentBurstId = 0;
            TotalPulsesEmitted = 0;
            isBurstInitialized = false;
            isInRestPhase = false;
            emittedPulsesInCurrentBurst = 0;
            StartNewBurst(0f);
        }

        public void ResetAimForSession()
        {
            EnsureInitialized();
            currentTarget = null;
            currentAimDirection = FlatDirection(transform.forward, Vector3.forward);
            if (aimPivot != null)
                aimPivot.rotation = Quaternion.LookRotation(currentAimDirection, Vector3.up);
        }

        private bool CanOperate()
        {
            if (!isActiveAndEnabled)
                return false;
            if (playerHealth != null && playerHealth.IsDead)
                return false;
            if (session != null && (session.IsPaused || session.State==SessionState.BossIntro || !session.IsGameplayFlowActive()))
                return false;
            if (Time.timeScale <= 0f)
                return false;
            return true;
        }

        private void Update()
        {
            EnsureInitialized();
            if (!CanOperate())
                return;

            StepWeapon(Time.deltaTime);
        }

        /// <summary>
        /// Avanza el apuntado y el calendario acumulado de pulsos de la linterna.
        /// </summary>
        public void StepWeapon(float deltaTime)
        {
            EnsureInitialized();
            if (deltaTime <= 0f)
                return;
            if (playerHealth != null && playerHealth.IsDead)
                return;

            UpdateAim(deltaTime);
            CheckAndPublishGeometryChange();

            if (!isBurstInitialized)
            {
                StartNewBurst(weaponTime);
            }

            weaponTime += deltaTime;

            // Resolver pulsos programados con tiempos acumulados sin deriva
            int safetyIterations = 0;
            while (safetyIterations++ < 32)
            {
                if (!isInRestPhase)
                {
                    int nextPulseNumber = emittedPulsesInCurrentBurst + 1;
                    float scheduledPulseTime = burstAnchorTime + nextPulseNumber * capturedPulseInterval;

                    if (weaponTime + 0.00001f >= scheduledPulseTime)
                    {
                        emittedPulsesInCurrentBurst = nextPulseNumber;
                        ExecutePulse(nextPulseNumber);

                        if (emittedPulsesInCurrentBurst >= PulsesPerBurst)
                        {
                            float fourthPulseTime = burstAnchorTime + PulsesPerBurst * capturedPulseInterval;
                            if (capturedRestDuration <= 0.0001f)
                            {
                                // Modo Hiper-Rayo Solar (sin descanso entre ráfagas)
                                StartNewBurst(fourthPulseTime);
                            }
                            else
                            {
                                isInRestPhase = true;
                                restEndTime = fourthPulseTime + capturedRestDuration;
                                OnBurstEnteredRest?.Invoke(currentBurstId);
                            }
                        }
                        continue;
                    }
                    break;
                }
                else
                {
                    if (weaponTime + 0.00001f >= restEndTime)
                    {
                        StartNewBurst(restEndTime);
                        continue;
                    }
                    break;
                }
            }
        }

        private void StartNewBurst(float anchorTime)
        {
            guaranteedActive = guaranteedPending; guaranteedPending = 0;
            isBurstInitialized = true;
            isInRestPhase = false;
            emittedPulsesInCurrentBurst = 0;
            burstAnchorTime = anchorTime;
            currentBurstId++;

            // Capturar parámetros de la ráfaga al comenzar; mejoras durante la ráfaga aplican a la siguiente
            capturedSolarMode = runtimeStats != null && runtimeStats.IsSolarActive;
            capturedRange = EffectiveRange;
            capturedAngle = EffectiveAngle;
            capturedDamagePerPulse = runtimeStats != null ? runtimeStats.FlashlightDamagePerPulse : 3.75f;
            capturedPulseInterval = runtimeStats != null ? runtimeStats.EffectivePulseInterval : 0.20f;
            capturedRestDuration = runtimeStats != null ? runtimeStats.EffectiveBurstRestDuration : 0.40f;
            capturedKnockbackImpulse = runtimeStats != null ? runtimeStats.FlashlightKnockback : 10f;
            capturedCritChance = runtimeStats != null ? runtimeStats.CritChance : 0f;
            capturedCritMultiplier = runtimeStats != null ? runtimeStats.CritMultiplier : 1.75f;

            CheckAndPublishGeometryChange();
            OnBurstStarted?.Invoke(currentBurstId);
        }

        public void UpdateAim(float deltaTime)
        {
            EnsureInitialized();
            Vector3 origin = OriginPosition;
            float searchRange = ActiveBurstRange;
            currentTarget = FindNearestEnemyTarget(origin, searchRange, out _);

            Vector3 desiredDirection;
            if (currentTarget != null)
            {
                desiredDirection = currentTarget.WorldCenter - origin;
            }
            else if (inputReader != null && inputReader.Movement.sqrMagnitude > 0.0001f)
            {
                desiredDirection = motor != null ? motor.ResolveMovementDirection(inputReader.Movement)
                    : new Vector3(inputReader.Movement.x, 0f, inputReader.Movement.y);
            }
            else if (motor != null && motor.FacingDirection.sqrMagnitude > 0.0001f)
            {
                desiredDirection = motor.FacingDirection;
            }
            else
            {
                desiredDirection = currentAimDirection;
            }

            desiredDirection = FlatDirection(desiredDirection, currentAimDirection);

            // Desempatar giro de 180° para no quedar bloqueado al invertir la dirección
            if (Vector3.Dot(currentAimDirection, desiredDirection) < -0.9999f)
                currentAimDirection = Quaternion.Euler(0f, 1f, 0f) * currentAimDirection;

            float aimSpeed = runtimeStats != null && runtimeStats.Settings != null
                ? runtimeStats.Settings.AimTurnSpeed
                : 720f;
            currentAimDirection = Vector3.RotateTowards(
                currentAimDirection,
                desiredDirection,
                aimSpeed * Mathf.Deg2Rad * Mathf.Max(0f, deltaTime),
                0f);
            currentAimDirection = FlatDirection(currentAimDirection, desiredDirection);

            if (aimPivot != null && currentAimDirection.sqrMagnitude > 0.0001f)
                aimPivot.rotation = Quaternion.LookRotation(currentAimDirection, Vector3.up);
        }

        public IDamageTarget FindNearestEnemyTarget(Vector3 origin, float maxRange, out float nearestDistanceSqr)
        {
            nearestDistanceSqr = float.MaxValue;
            IDamageTarget nearest = null;

            GatherCandidateTargets(origin, maxRange + 4f);
            for (int i = 0; i < candidateTargets.Count; i++)
            {
                IDamageTarget candidate = candidateTargets[i];
                if (!IsValidEnemyTarget(candidate))
                    continue;

                Vector3 offset = candidate.WorldCenter - origin;
                offset.y = 0f;
                float distSqr = offset.sqrMagnitude;
                float maxAllowedDist = maxRange + Mathf.Max(0f, candidate.HitRadius);
                if (distSqr > maxAllowedDist * maxAllowedDist)
                    continue;
                if (Physics.Linecast(origin, candidate.WorldCenter, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore))
                    continue;

                if (distSqr < nearestDistanceSqr)
                {
                    nearestDistanceSqr = distSqr;
                    nearest = candidate;
                }
            }

            return nearest;
        }

        private void ExecutePulse(int pulseNumber)
        {
            TotalPulsesEmitted++;
            Vector3 origin = OriginPosition;
            Vector3 forward = FlatDirection(currentAimDirection, Vector3.forward);

            float range = capturedRange;
            float angle = capturedAngle;
            float damage = capturedDamagePerPulse;
            float critChance = pulseNumber <= guaranteedActive ? 1f : capturedCritChance;
            float critMultiplier = capturedCritMultiplier;

            bool applyKnockback = capturedSolarMode || pulseNumber == PulsesPerBurst;
            float knockbackImpulse = applyKnockback ? capturedKnockbackImpulse : 0f;

            hitTargetIdsThisPulse.Clear();
            GatherCandidateTargets(origin, range + 4f);

            int hitCount = 0;
            int killCount = 0;
            int critCount = 0;
            int sourceId = gameObject.GetHashCode();
            DamageSourceType sourceType = capturedSolarMode ? DamageSourceType.SolarPulse : DamageSourceType.FlashlightPulse;

            for (int i = 0; i < candidateTargets.Count; i++)
            {
                IDamageTarget target = candidateTargets[i];
                if (!IsValidEnemyTarget(target))
                    continue;

                int targetId = target.TargetId;
                if (hitTargetIdsThisPulse.Contains(targetId))
                    continue;

                if (!IsTargetInsideConeXZ(origin, forward, range, angle, target.WorldCenter, target.HitRadius, out Vector3 planarOffset))
                    continue;
                if (Physics.Linecast(origin, target.WorldCenter, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore))
                    continue;

                // Garantizar un único impacto por objetivo y pulso aunque tenga múltiples colliders
                hitTargetIdsThisPulse.Add(targetId);

                Vector3 hitDir = planarOffset.sqrMagnitude > 0.0001f ? planarOffset.normalized : forward;
                var request = new DamageRequest(
                    sourceId,
                    sourceType,
                    origin,
                    hitDir,
                    damage,
                    canCrit: true,
                    critChance: critChance,
                    critMultiplier: critMultiplier,
                    burstId: currentBurstId,
                    pulseIndex: pulseNumber,
                    knockbackImpulse: knockbackImpulse);

                DamageResult result = target.ReceiveDamage(in request);
                if (result.AppliedRealDamage)
                {
                    hitCount++;
                    if (result.IsCritical)
                        critCount++;
                    if (result.CausedDeath)
                        killCount++;

                }
                // Una Polilla armada con HP cero sigue siendo un cuerpo empujable durante su mecha.
                if (applyKnockback && knockbackImpulse > 0f && target.IsAlive &&
                    (result.AppliedRealDamage || target is EnemyActor { IsFuseActive: true }))
                    target.ApplyKnockback(hitDir * knockbackImpulse);
            }

            var pulseEvent = new PulseResolvedEvent(
                currentBurstId,
                pulseNumber,
                capturedSolarMode,
                hitCount,
                killCount,
                critCount,
                origin,
                forward,
                range,
                angle);

            OnPulseEmitted?.Invoke(pulseEvent);
            session?.Events.PublishPulse(in pulseEvent);
        }

        /// <summary>
        /// Comprueba en el plano XZ si un objetivo circular (centro + radio) intersecta el sector del cono.
        /// Atraviesa obstáculos y utiliza exactamente el mismo origen, alcance y apertura que el cono visual.
        /// </summary>
        public static bool IsTargetInsideConeXZ(
            Vector3 origin,
            Vector3 forward,
            float range,
            float fullAngleDegrees,
            Vector3 targetCenter,
            float targetRadius,
            out Vector3 planarOffset)
        {
            planarOffset = new Vector3(targetCenter.x - origin.x, 0f, targetCenter.z - origin.z);
            float distance = planarOffset.magnitude;
            float clampedRadius = Mathf.Max(0f, targetRadius);

            if (distance > range + clampedRadius)
                return false;

            if (distance <= 0.001f || distance <= clampedRadius)
                return true;

            Vector3 flatForward = FlatDirection(forward, Vector3.forward);
            float centerAngle = Vector3.Angle(flatForward, planarOffset);
            float angularRadius = Mathf.Rad2Deg * Mathf.Atan2(clampedRadius, distance);
            return Mathf.Max(0f, centerAngle - angularRadius) <= fullAngleDegrees * 0.5f;
        }

        private void GatherCandidateTargets(Vector3 origin, float queryRadius)
        {
            candidateTargets.Clear();

            // 1. Añadir objetivos del registro activo
            IReadOnlyList<IDamageTarget> registered = CombatTargetRegistry.ActiveTargets;
            for (int i = 0; i < registered.Count; i++)
            {
                IDamageTarget target = registered[i];
                if (IsValidEnemyTarget(target))
                    candidateTargets.Add(target);
            }

            // 2. Respaldo por OverlapSphereNonAlloc para descubrir objetivos con varios colliders o no registrados aún
            int colliderCount = Physics.OverlapSphereNonAlloc(origin, queryRadius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < colliderCount; i++)
            {
                Collider col = overlapBuffer[i];
                if (col == null)
                    continue;

                IDamageTarget target = col.GetComponentInParent<IDamageTarget>();
                if (IsValidEnemyTarget(target))
                    candidateTargets.Add(target);
            }
        }

        private static bool IsValidEnemyTarget(IDamageTarget target)
        {
            if (target == null || !target.IsEnemyTarget || !target.IsAlive)
                return false;
            if (target is MonoBehaviour mb && !mb.isActiveAndEnabled)
                return false;
            return true;
        }

        public void CheckAndPublishGeometryChange()
        {
            float range = ActiveBurstRange;
            float angle = ActiveBurstAngle;
            if (Mathf.Approximately(range, lastNotifiedRange) && Mathf.Approximately(angle, lastNotifiedAngle))
                return;

            lastNotifiedRange = range;
            lastNotifiedAngle = angle;
            OnGeometryChanged?.Invoke(range, angle);
        }

        private static Vector3 FlatDirection(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : fallback;
        }
    }
}
