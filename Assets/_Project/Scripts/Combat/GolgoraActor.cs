using System;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    // Preserve the original values for serialized scenes and recorded QA data.
    public enum GolgoraState { Intro, Pursuing, StompWindup, BarrageWindup, Recovery, Dead, SweepWindup, PhaseTransition, CercoReposition, CercoWindup, CercoActive }
    public enum GolgoraAttackKind { None, Hammer, Sweep, Barrage, Cerco }

    /// <summary>Exclusive, announced attacks driven by the same clock as animation and hit resolution.</summary>
    public sealed class GolgoraActor : EnemyActor
    {
        public const float StompTriggerRange = 6.5f;
        public const float StompRadius = 3f;
        public const float StompWindupDuration = 1.2f;
        public const float RecoveryDuration = 1f;
        public const float BarrageWindupDuration = .7f;
        [SerializeField] private GolgoraCombatSettings combatSettings;
        private readonly EnemyActor[] collateralTargets = new EnemyActor[256];
        private readonly Vector3[] barrageTargets = new Vector3[3];
        private CorrosivePuddlePool hazards;
        private GolgoraPresentation presentation;
        private MataColeccionistaView modelView;
        private GolgoraMagmaRingPool ringPool;
        private float phaseTime, nextStompTime, nextSweepTime, nextBarrageTime, nextCercoTime;
        private Vector3 stompOrigin, attackDirection = Vector3.forward, arenaCenter;
        private float arenaRadius = 22f, firstCercoGap;
        private Rigidbody playerBody;
        private bool cercoSequence, preferSweep, ownsFallbackSettings;
        private Vector3 jumpStart;
        private bool jumpWasKinematic, jumpHadCollider;
        public bool IsAirborne => State==GolgoraState.CercoReposition;
        public float JumpProgress => Mathf.Clamp01(phaseTime/Mathf.Max(.5f,Settings.CercoJumpDuration));
        public float JumpHeight { get; private set; }

        public bool AutoTick { get; set; } = true;
        public GolgoraState State { get; private set; } = GolgoraState.Intro;
        public float BossTime { get; private set; }
        public bool IsCombatActive => State != GolgoraState.Intro && State != GolgoraState.Dead && IsAlive;
        public bool IsPhaseTwo { get; private set; }
        public bool IsCercoSequence => cercoSequence;
        public bool EnragedVisual => IsPhaseTwo || CurrentHealth > 0f && CurrentHealth <= MaxHealth * Settings.PhaseTwoHealthFraction;
        public int StompsExecuted { get; private set; }
        public int SweepsExecuted { get; private set; }
        public int BarragesExecuted { get; private set; }
        public int CercosExecuted { get; private set; }
        public int CercoPulsesEmitted { get; private set; }
        public int CercoPulseCount => IsPhaseTwo ? 3 : 1;
        public Vector3 LastStompOrigin => stompOrigin;
        public Vector3 AttackOrigin => stompOrigin;
        public Vector3 AttackDirection => attackDirection;
        public Vector3 ArenaCenter => arenaCenter;
        public float ArenaRadius => arenaRadius;
        public GolgoraAttackKind CurrentAttackKind { get; private set; }
        public float NextCercoGapAngleDegrees => firstCercoGap + CercoPulsesEmitted * Settings.RingGapRotation;
        public GolgoraMagmaRingPool RingPool => ringPool;
        public GolgoraCombatSettings Settings
        {
            get
            {
                if (combatSettings == null)
                {
                    combatSettings = Resources.Load<GolgoraCombatSettings>("Golgora/CombatSettings");
                    if (combatSettings == null)
                    { combatSettings = ScriptableObject.CreateInstance<GolgoraCombatSettings>(); ownsFallbackSettings = true; }
                }
                return combatSettings;
            }
        }
        public float CurrentPhaseDuration => State switch
        {
            GolgoraState.StompWindup => Settings.HammerWindup,
            GolgoraState.SweepWindup => Settings.SweepWindup,
            GolgoraState.BarrageWindup => Settings.BarrageWindup,
            GolgoraState.CercoWindup => Settings.CercoWindup,
            GolgoraState.PhaseTransition => Mathf.Max(Settings.TransitionDuration,GolgoraRoarPresentation.RoarDuration),
            GolgoraState.CercoReposition => Settings.CercoJumpDuration,
            GolgoraState.Recovery => CurrentAttackKind switch
            {
                GolgoraAttackKind.Hammer => Settings.HammerRecovery,
                GolgoraAttackKind.Sweep => Settings.SweepRecovery,
                GolgoraAttackKind.Barrage => Settings.BarrageRecovery,
                _ => Settings.CercoRecovery
            },
            _ => 1f
        };
        public float PhaseProgress => Mathf.Clamp01(phaseTime / Mathf.Max(.01f, CurrentPhaseDuration));
        public float RecoveryProgress => State == GolgoraState.Recovery ? PhaseProgress : 0f;
        public event Action CercoStarted;
        public event Action CercoCompleted;
        public event Action PhaseTransitionStarted;
        public event Action PhaseTransitionCompleted;
        public event Action<GolgoraAttackKind, Vector3, Vector3> AttackExecuted;

        public void SetCombatSettings(GolgoraCombatSettings settings)
        {
            if (ownsFallbackSettings && combatSettings != null) Destroy(combatSettings);
            combatSettings = settings; ownsFallbackSettings = false;
        }
        public void BindHazards(CorrosivePuddlePool pool) => hazards = pool;
        public void BindArena(Vector3 center, float radius)
        { arenaCenter = ArenaSurface.OnGround(center); arenaRadius = Mathf.Max(8f, radius); }

        public override void ResetForSpawn(Vector3 spawnPosition)
        {
            base.ResetForSpawn(spawnPosition);
            State = GolgoraState.Intro; CurrentAttackKind = GolgoraAttackKind.None;
            IsPhaseTwo = cercoSequence = preferSweep = false;
            JumpHeight=0;
            BossTime = phaseTime = 0f;
            nextStompTime = nextSweepTime = 2f; nextBarrageTime = 3.5f;
            nextCercoTime = Settings.FirstCercoTime;
            StompsExecuted = SweepsExecuted = BarragesExecuted = CercosExecuted = CercoPulsesEmitted = 0;
            stompOrigin = ArenaSurface.OnGround(WorldCenter); attackDirection = FacingDirection;
            playerBody = TargetPlayer != null ? TargetPlayer.GetComponent<Rigidbody>() : null;
            float worldScaleY = Mathf.Max(.01f, transform.lossyScale.y);
            BodyCollider.center = Vector3.up * (BodyCollider.height * .5f - .95f / worldScaleY);
            BodyCollider.enabled = false; Body.linearVelocity = Vector3.zero; Body.isKinematic = true;
            CombatTargetRegistry.Unregister(this);
            if (ringPool == null) ringPool = GetComponent<GolgoraMagmaRingPool>() ?? gameObject.AddComponent<GolgoraMagmaRingPool>();
            ringPool.Bind(this); ringPool.Clear();
            if (presentation == null) presentation = GetComponent<GolgoraPresentation>() ?? gameObject.AddComponent<GolgoraPresentation>();
            if (hazards == null) hazards = PuddlePool;
            presentation.Bind(this); presentation.ResetPresentation();
            modelView = GetComponent<MataColeccionistaView>();
        }
        public void ActivateCombat()
        {
            if (!IsAlive || State != GolgoraState.Intro) return;
            State = GolgoraState.Pursuing; phaseTime = BossTime = 0f;
            Body.isKinematic = false; BodyCollider.enabled = true;
            CombatTargetRegistry.Register(this); presentation?.ResetPresentation();
        }
        public override DamageResult ReceiveDamage(in DamageRequest request)
        {
            if (State == GolgoraState.Intro || State==GolgoraState.PhaseTransition || IsAirborne)
            {
                var immune = new DamageResult(request.SourceId, TargetId, request.SourceType,
                    request.OriginPosition, WorldCenter, request.HitDirection, request.BaseDamage, 0f, 0f,
                    CurrentHealth, MaxHealth, false, false, false, true, false, false, request.BurstId, request.PulseIndex);
                Session?.Events.PublishDamage(in immune); return immune;
            }
            var result=base.ReceiveDamage(in request);
            if(IsAlive && !IsPhaseTwo && CurrentHealth<=MaxHealth*Settings.PhaseTwoHealthFraction)BeginTransition();
            return result;
        }
        public override void StepPhysics(float fixedDeltaTime) { if (AutoTick) Step(fixedDeltaTime); }
        public void Step(float deltaTime)
        {
            if (deltaTime <= 0f || !IsAlive || SimulationSuspended ||
                (Session != null && (Session.IsPaused || !Session.IsGameplayFlowActive()))) return;
            if (State == GolgoraState.Intro) { presentation?.Step(deltaTime); return; }
            ringPool?.Step(deltaTime);
            if (IsControlLocked) { StopBody(); return; }
            BossTime += deltaTime;
            phaseTime += State==GolgoraState.PhaseTransition && Session!=null?deltaTime/Mathf.Max(.05f,Session.Clock.SimulationRate):deltaTime;
            switch (State)
            {
                case GolgoraState.Pursuing:
                    if (!IsPhaseTwo && CurrentHealth <= MaxHealth * Settings.PhaseTwoHealthFraction)
                    { BeginTransition(); break; }
                    if (BossTime >= nextCercoTime) { BeginCerco(); break; }
                    base.StepPhysics(deltaTime); ClampBodyInsideArena();
                    if (TargetPlayer != null && TargetPlayer.IsAlive)
                    {
                        Vector3 delta = PursuitGoal - WorldCenter; delta.y = 0f;
                        if (delta.sqrMagnitude <= Settings.HammerTriggerRange * Settings.HammerTriggerRange)
                        {
                            if (preferSweep && BossTime >= nextSweepTime) BeginSweep();
                            else if (BossTime >= nextStompTime) BeginStomp();
                            else if (BossTime >= nextSweepTime) BeginSweep();
                        }
                        else if (BossTime >= nextBarrageTime && CanFireBarrage()) BeginBarrage();
                    }
                    break;
                case GolgoraState.StompWindup:
                    StopBody();
                    if (phaseTime + .0001f >= CurrentPhaseDuration) { ExecuteStomp(); EnterRecovery(); }
                    break;
                case GolgoraState.SweepWindup:
                    StopBody();
                    if (phaseTime + .0001f >= CurrentPhaseDuration) { ExecuteSweep(); EnterRecovery(); }
                    break;
                case GolgoraState.BarrageWindup:
                    StopBody();
                    if (phaseTime + .0001f >= CurrentPhaseDuration && CanFireBarrage()) { ExecuteBarrage(); EnterRecovery(); }
                    break;
                case GolgoraState.PhaseTransition:
                    StopBody();
                    if (phaseTime + .0001f >= CurrentPhaseDuration) {PhaseTransitionCompleted?.Invoke();BeginCerco();}
                    break;
                case GolgoraState.CercoReposition: StepReturnToCenter(deltaTime); break;
                case GolgoraState.CercoWindup:
                    StopBody();
                    if (phaseTime + .0001f >= CurrentPhaseDuration)
                    { State = GolgoraState.CercoActive; phaseTime = 0f; EmitCercoPulse(); }
                    break;
                case GolgoraState.CercoActive:
                    StopBody();
                    while (CercoPulsesEmitted < CercoPulseCount &&
                        phaseTime + .0001f >= CercoPulsesEmitted * Mathf.Max(.1f, Settings.CercoPulseInterval))
                    { if (!EmitCercoPulse()) break; }
                    if (CercoPulsesEmitted >= CercoPulseCount && ringPool.ActiveCount == 0)
                    { ringPool.HideGapPreview(); EnterRecovery(); }
                    break;
                case GolgoraState.Recovery:
                    StopBody();
                    if (phaseTime + .0001f >= CurrentPhaseDuration)
                    {
                        if (cercoSequence)
                        { cercoSequence = false; nextCercoTime = BossTime + Settings.CercoCooldown; CercoCompleted?.Invoke(); }
                        State = GolgoraState.Pursuing; phaseTime = 0f;
                    }
                    break;
            }
            if (State != GolgoraState.Pursuing && VisualComposer != null && VisualComposer.OrientationRoot != null)
                VisualComposer.OrientationRoot.rotation = Quaternion.LookRotation(attackDirection, Vector3.up);
            presentation?.Step(deltaTime);
        }
        protected override Vector3 ComputeArchetypeLocomotion(float fixedDeltaTime)
        {
            if (TargetPlayer == null || !TargetPlayer.IsAlive || State != GolgoraState.Pursuing) return Vector3.zero;
            Vector3 delta = PursuitGoal - WorldCenter; delta.y = 0f;
            return ApplySimpleObstacleAvoidance(WorldCenter, delta.normalized) * AssignedMoveSpeed * (1f - SlowFraction);
        }
        private void ClampBodyInsideArena()
        {
            Vector3 delta = Body.position - arenaCenter; delta.y = 0f;
            float limit = Mathf.Max(1f, arenaRadius - HitRadius);
            if (delta.sqrMagnitude <= limit * limit) return;
            Vector3 position = arenaCenter + delta.normalized * limit;
            position.y = Body.position.y; Body.position = position;
            Vector3 velocity = Body.linearVelocity;
            float outward = Vector3.Dot(velocity, delta.normalized);
            if (outward > 0f) Body.linearVelocity = velocity - delta.normalized * outward;
        }
        private void StopBody() { if (Body != null && !Body.isKinematic) Body.linearVelocity = Vector3.zero; }
        private void LockDirection()
        {
            attackDirection = PursuitGoal - WorldCenter; attackDirection.y = 0f;
            attackDirection = attackDirection.sqrMagnitude > .001f ? attackDirection.normalized : Vector3.forward;
        }
        private void BeginStomp()
        {
            State = GolgoraState.StompWindup; CurrentAttackKind = GolgoraAttackKind.Hammer;
            phaseTime = 0f; nextStompTime = BossTime + Settings.HammerCooldown; preferSweep = true;
            LockDirection();
            stompOrigin = ArenaSurface.OnGround(WorldCenter + attackDirection * Settings.HammerForwardOffset);
            StopBody();
        }
        private void BeginSweep()
        {
            State = GolgoraState.SweepWindup; CurrentAttackKind = GolgoraAttackKind.Sweep;
            phaseTime = 0f; nextSweepTime = BossTime + Settings.SweepCooldown; preferSweep = false;
            LockDirection(); stompOrigin = ArenaSurface.OnGround(WorldCenter); StopBody();
        }
        private bool CanFireBarrage() => hazards != null &&
            CorrosivePuddlePool.MaxInFlightProjectiles - hazards.ActiveProjectileCount >= 3 &&
            CorrosivePuddlePool.MaxActivePuddles - hazards.ActivePuddleCount - hazards.ReservedPuddleCount >= 3;
        private void BeginBarrage()
        {
            State = GolgoraState.BarrageWindup; CurrentAttackKind = GolgoraAttackKind.Barrage;
            phaseTime = 0f; nextBarrageTime = BossTime + Settings.BarrageCooldown;
            LockDirection(); stompOrigin = ArenaSurface.OnGround(WorldCenter);
            Vector3 velocity = playerBody != null ? playerBody.linearVelocity : Vector3.zero; velocity.y = 0f;
            Vector3 predicted = PursuitGoal + velocity * Settings.BarragePrediction;
            Vector3 forward = predicted - WorldCenter; forward.y = 0f;
            forward = forward.sqrMagnitude > .001f ? forward.normalized : attackDirection;
            barrageTargets[0] = ClampTarget(predicted);
            barrageTargets[1] = ClampTarget(predicted + Quaternion.AngleAxis(-60f, Vector3.up) * forward * Settings.BarrageFlankDistance);
            barrageTargets[2] = ClampTarget(predicted + Quaternion.AngleAxis(60f, Vector3.up) * forward * Settings.BarrageFlankDistance);
            StopBody();
        }
        private Vector3 ClampTarget(Vector3 position)
        {
            Vector3 delta = position - arenaCenter; delta.y = 0f;
            position = arenaCenter + Vector3.ClampMagnitude(delta, Mathf.Max(1f, arenaRadius - Settings.BarrageRadius));
            return ArenaSurface.OnGround(position);
        }
        private void ExecuteStomp()
        {
            StompsExecuted++;
            DamagePlayerInRadius(stompOrigin, Settings.HammerRadius, Settings.HammerDamage, DamageSourceType.BossStomp);
            int count = 0; var actors = ActiveEnemies;
            for (int i = 0; i < actors.Count && count < collateralTargets.Length; i++)
            {
                EnemyActor actor = actors[i];
                if (actor == null || actor == this || !actor.IsAlive || actor.ArchetypeId == EnemyArchetypeId.GolgoraBoss) continue;
                Vector3 delta = actor.WorldCenter - stompOrigin; delta.y = 0f;
                float radius = Settings.HammerRadius + actor.HitRadius;
                if (delta.sqrMagnitude <= radius * radius) collateralTargets[count++] = actor;
            }
            for (int i = 0; i < count; i++)
            {
                EnemyActor actor = collateralTargets[i]; collateralTargets[i] = null;
                if (actor == null || !actor.IsAlive) continue;
                Vector3 direction = actor.WorldCenter - stompOrigin; direction.y = 0f;
                direction = direction.sqrMagnitude > .001f ? direction.normalized : attackDirection;
                var damage = new DamageRequest(TargetId, DamageSourceType.BossStomp, stompOrigin, direction, Settings.CollateralDamage);
                actor.ReceiveDamage(in damage);
                if (actor.IsAlive) actor.ApplyKnockback(direction * Settings.CollateralImpulse);
            }
            AttackExecuted?.Invoke(GolgoraAttackKind.Hammer, stompOrigin, attackDirection);
        }
        private void ExecuteSweep()
        {
            SweepsExecuted++;
            if (TargetPlayer != null && TargetPlayer.IsAlive &&
                SweepContains(TargetPlayer.WorldCenter - stompOrigin, TargetPlayer.HitRadius,
                    attackDirection, Settings.SweepRadius, Settings.SweepArc))
            {
                var damage = new DamageRequest(TargetId, DamageSourceType.BossSweep, stompOrigin, attackDirection, Settings.SweepDamage);
                TargetPlayer.ReceiveDamage(in damage);
            }
            AttackExecuted?.Invoke(GolgoraAttackKind.Sweep, stompOrigin, attackDirection);
        }
        public static bool SweepContains(Vector3 delta, float targetRadius, Vector3 forward, float radius, float degrees)
        {
            delta.y = 0f; forward.y = 0f;
            float distance = delta.magnitude;
            if (distance > radius + targetRadius) return false;
            if (distance <= targetRadius) return true;
            float extension = Mathf.Asin(Mathf.Clamp01(targetRadius / distance)) * Mathf.Rad2Deg;
            return Vector3.Angle(forward, delta) <= degrees * .5f + extension;
        }
        private void DamagePlayerInRadius(Vector3 origin, float radius, float amount, DamageSourceType source)
        {
            if (TargetPlayer == null || !TargetPlayer.IsAlive) return;
            Vector3 direction = TargetPlayer.WorldCenter - origin; direction.y = 0f;
            float hitRadius = radius + TargetPlayer.HitRadius;
            if (direction.sqrMagnitude > hitRadius * hitRadius) return;
            var damage = new DamageRequest(TargetId, source, origin, direction.sqrMagnitude > .001f ? direction.normalized : attackDirection, amount);
            TargetPlayer.ReceiveDamage(in damage);
        }
        private void ExecuteBarrage()
        {
            for (int i = 0; i < barrageTargets.Length; i++)
            {
                var spec = new CorrosivePuddlePool.ProjectileSpec
                {
                    SourceId = TargetId, LaunchOrigin = WorldCenter + Vector3.up * 3.3f,
                    TargetPosition = barrageTargets[i], FlightDuration = Settings.BarrageFlightDuration, ArcPeakHeight = 3.5f,
                    ImpactRadius = Settings.BarrageRadius, DirectImpactDamage = Settings.BarrageDamage,
                    DirectImpactSource = DamageSourceType.BossBombardmentImpact,
                    PuddleRadius = Settings.BarrageRadius, PuddleDuration = Settings.PuddleDuration,
                    PuddleTickDamage = Settings.PuddleDamage, PuddleTickInterval = Settings.PuddleTickInterval,
                    PuddleDamageSource = DamageSourceType.BossBombardmentPuddle, Style = CorrosivePuddlePool.HazardStyle.BossMagma
                };
                hazards.TryLaunchParabolicProjectile(in spec);
            }
            BarragesExecuted++; AttackExecuted?.Invoke(GolgoraAttackKind.Barrage, stompOrigin, attackDirection);
        }
        public Vector3 GetLastBarrageTarget(int index) => barrageTargets[Mathf.Clamp(index, 0, 2)];
        private void BeginTransition()
        {
            IsPhaseTwo = true; State = GolgoraState.PhaseTransition; CurrentAttackKind = GolgoraAttackKind.None;
            phaseTime = 0f; LockDirection(); stompOrigin = ArenaSurface.OnGround(WorldCenter); StopBody();
            ringPool?.Clear();hazards?.ClearAll();
            PhaseTransitionStarted?.Invoke();
        }
        private void BeginCerco()
        {
            cercoSequence = true; State = GolgoraState.CercoReposition; CurrentAttackKind = GolgoraAttackKind.Cerco;
            phaseTime = 0f; CercoPulsesEmitted = 0; ringPool.Clear(); StopBody();
            jumpStart=Body.position;jumpWasKinematic=Body.isKinematic;jumpHadCollider=BodyCollider.enabled;
            Body.isKinematic=true;BodyCollider.enabled=false;CombatTargetRegistry.Unregister(this);
            stompOrigin=arenaCenter;LockDirection();
            Vector3 toPlayer=TargetPlayer!=null?TargetPlayer.WorldCenter-arenaCenter:attackDirection;
            firstCercoGap=Mathf.Atan2(toPlayer.x,toPlayer.z)*Mathf.Rad2Deg+60f;
            ringPool.ShowGapPreview(arenaCenter,firstCercoGap,Settings.RingStartRadius,arenaRadius,Settings.RingGapDegrees);
            CercoStarted?.Invoke();
            StepReturnToCenter(0f);
        }
        private void StepReturnToCenter(float deltaTime)
        {
            float progress=JumpProgress;
            float flight=Mathf.InverseLerp(.16f,1f,progress);
            Vector3 target=ArenaSurface.OnGround(arenaCenter,1f);
            Body.position=Vector3.Lerp(jumpStart,target,Mathf.SmoothStep(0,1,flight));
            transform.position=Body.position;
            JumpHeight=Mathf.Sin(flight*Mathf.PI)*Settings.CercoJumpHeight;
            modelView?.SetJumpHeight(JumpHeight);
            if(progress>=1f-.0001f)
            {
                JumpHeight=0;modelView?.SetJumpHeight(0);
                Body.position=target;Body.isKinematic=jumpWasKinematic;BodyCollider.enabled=jumpHadCollider;
                if(BodyCollider.enabled)CombatTargetRegistry.Register(this);StopBody();
                State=GolgoraState.CercoActive;phaseTime=0;stompOrigin=arenaCenter;
                DamagePlayerInRadius(arenaCenter,Settings.RingStartRadius,Settings.RingDamage,DamageSourceType.BossStomp);
                EmitCercoPulse();
            }
        }
        private bool EmitCercoPulse()
        {
            if (!ringPool.TryEmit(arenaCenter, arenaRadius, NextCercoGapAngleDegrees)) return false;
            if (CercoPulsesEmitted == 0) CercosExecuted++;
            CercoPulsesEmitted++;
            AttackExecuted?.Invoke(GolgoraAttackKind.Cerco, arenaCenter, attackDirection);
            if (CercoPulsesEmitted < CercoPulseCount)
                ringPool.ShowGapPreview(arenaCenter, NextCercoGapAngleDegrees, Settings.RingStartRadius, arenaRadius, Settings.RingGapDegrees);
            else ringPool.HideGapPreview();
            return true;
        }
        private void EnterRecovery() { State = GolgoraState.Recovery; phaseTime = 0f; StopBody(); }
        protected override void ExecuteDeath(Vector3 capturedPosition, Vector3 hitDirection, DamageSourceType cause)
        {
            State = GolgoraState.Dead; ringPool?.Clear(); presentation?.ResetPresentation();
            if (cercoSequence) { cercoSequence = false; CercoCompleted?.Invoke(); }
            base.ExecuteDeath(capturedPosition, hitDirection, cause);
        }
        private void OnDestroy()
        {
            if (ownsFallbackSettings && combatSettings != null) Destroy(combatSettings);
        }
    }
}
