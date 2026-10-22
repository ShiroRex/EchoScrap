using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    public enum GolgoraState { Intro, Pursuing, StompWindup, BarrageWindup, Recovery, Dead }

    /// <summary>Jefe con ataques excluyentes, avisos honestos y cooldown contado desde su preparación.</summary>
    public sealed class GolgoraActor : EnemyActor
    {
        public const float StompTriggerRange = 6.5f;
        public const float StompRadius = 5f;
        public const float StompWindupDuration = 1.2f;
        public const float RecoveryDuration = 0.8f;
        public const float BarrageWindupDuration = 0.4f;

        private readonly EnemyActor[] collateralTargets = new EnemyActor[256];
        private readonly Vector3[] barrageTargets = new Vector3[3];
        private CorrosivePuddlePool hazards;
        private GolgoraPresentation presentation;
        private float phaseTime;
        private float nextStompTime;
        private float nextBarrageTime;
        private Vector3 stompOrigin;
        private Rigidbody playerBody;

        public bool AutoTick { get; set; } = true;
        public GolgoraState State { get; private set; } = GolgoraState.Intro;
        public float BossTime { get; private set; }
        public bool IsCombatActive => State != GolgoraState.Intro && State != GolgoraState.Dead && IsAlive;
        public bool EnragedVisual => CurrentHealth > 0f && CurrentHealth <= MaxHealth * 0.5f;
        public int StompsExecuted { get; private set; }
        public int BarragesExecuted { get; private set; }
        public Vector3 LastStompOrigin => stompOrigin;
        public float PhaseProgress => State == GolgoraState.StompWindup
            ? Mathf.Clamp01(phaseTime / StompWindupDuration)
            : State == GolgoraState.BarrageWindup ? Mathf.Clamp01(phaseTime / BarrageWindupDuration) : 0f;

        public void BindHazards(CorrosivePuddlePool pool) => hazards = pool;

        public override void ResetForSpawn(Vector3 spawnPosition)
        {
            base.ResetForSpawn(spawnPosition);
            State = GolgoraState.Intro;
            BossTime = phaseTime = 0f;
            nextStompTime = 2f;
            nextBarrageTime = 3.5f;
            StompsExecuted = BarragesExecuted = 0;
            playerBody = TargetPlayer != null ? TargetPlayer.GetComponent<Rigidbody>() : null;
            // La raíz permanece en y=1 como el motor común. El collider gigante se apoya sobre
            // el suelo en vez de escalar tres metros por debajo de él con FreezePositionY.
            float worldScaleY = Mathf.Max(0.01f, transform.lossyScale.y);
            BodyCollider.center = Vector3.up * (BodyCollider.height * 0.5f - 0.95f / worldScaleY);
            BodyCollider.enabled = false;
            Body.linearVelocity = Vector3.zero;
            Body.isKinematic = true;
            CombatTargetRegistry.Unregister(this);
            if (presentation == null)
                presentation = GetComponent<GolgoraPresentation>() ?? gameObject.AddComponent<GolgoraPresentation>();
            if (hazards == null)
                hazards = PuddlePool;
            presentation.Bind(this);
            presentation.ResetPresentation();
        }

        public void ActivateCombat()
        {
            if (!IsAlive || State != GolgoraState.Intro)
                return;
            State = GolgoraState.Pursuing;
            phaseTime = BossTime = 0f;
            Body.isKinematic = false;
            BodyCollider.enabled = true;
            CombatTargetRegistry.Register(this);
            presentation?.ResetPresentation();
        }

        public override DamageResult ReceiveDamage(in DamageRequest request)
        {
            if (State == GolgoraState.Intro)
            {
                var immune = new DamageResult(request.SourceId, TargetId, request.SourceType,
                    request.OriginPosition, WorldCenter, request.HitDirection, request.BaseDamage, 0f, 0f,
                    CurrentHealth, MaxHealth, false, false, false, true, false, false,
                    request.BurstId, request.PulseIndex);
                Session?.Events.PublishDamage(in immune);
                return immune;
            }
            return base.ReceiveDamage(in request);
        }

        public override void StepPhysics(float fixedDeltaTime)
        {
            if (AutoTick)
                Step(fixedDeltaTime);
        }

        public void Step(float deltaTime)
        {
            if (deltaTime <= 0f || !IsAlive || (Session != null && (Session.IsPaused || !Session.IsGameplayFlowActive())))
                return;
            if (State == GolgoraState.Intro)
            {
                presentation?.Step(deltaTime);
                return;
            }
            BossTime += deltaTime;
            phaseTime += deltaTime;
            switch (State)
            {
                case GolgoraState.Pursuing:
                    base.StepPhysics(deltaTime);
                    if (TargetPlayer != null && TargetPlayer.IsAlive)
                    {
                        Vector3 delta = TargetPlayer.WorldCenter - WorldCenter;
                        delta.y = 0f;
                        if (BossTime >= nextStompTime && delta.sqrMagnitude <= StompTriggerRange * StompTriggerRange)
                            BeginStomp();
                        else if (BossTime >= nextBarrageTime && CanFireBarrage())
                            BeginBarrage();
                    }
                    break;
                case GolgoraState.StompWindup:
                    StopBody();
                    if (phaseTime + 0.0001f >= StompWindupDuration)
                    {
                        ExecuteStomp();
                        EnterRecovery();
                    }
                    break;
                case GolgoraState.BarrageWindup:
                    StopBody();
                    // Esperar capacidad conserva los proyectiles y áreas ya anunciados.
                    if (phaseTime + 0.0001f >= BarrageWindupDuration && CanFireBarrage())
                    {
                        ExecuteBarrage();
                        EnterRecovery();
                    }
                    break;
                case GolgoraState.Recovery:
                    StopBody();
                    if (phaseTime + 0.0001f >= RecoveryDuration)
                    {
                        State = GolgoraState.Pursuing;
                        phaseTime = 0f;
                    }
                    break;
            }
            if (State != GolgoraState.Pursuing)
                TryDealContactDamage();
            presentation?.Step(deltaTime);
        }

        protected override Vector3 ComputeArchetypeLocomotion(float fixedDeltaTime)
        {
            if (TargetPlayer == null || !TargetPlayer.IsAlive || State != GolgoraState.Pursuing)
                return Vector3.zero;
            Vector3 delta = TargetPlayer.WorldCenter - WorldCenter;
            delta.y = 0f;
            return ApplySimpleObstacleAvoidance(WorldCenter, delta.normalized) * AssignedMoveSpeed;
        }

        private void StopBody()
        {
            if (Body != null && !Body.isKinematic)
                Body.linearVelocity = Vector3.zero;
        }

        private void BeginStomp()
        {
            State = GolgoraState.StompWindup;
            phaseTime = 0f;
            nextStompTime = BossTime + 6f;
            stompOrigin = WorldCenter;
            stompOrigin.y = 0f;
            StopBody();
        }

        private bool CanFireBarrage()
        {
            return hazards != null &&
                CorrosivePuddlePool.MaxInFlightProjectiles - hazards.ActiveProjectileCount >= 3 &&
                CorrosivePuddlePool.MaxActivePuddles - hazards.ActivePuddleCount - hazards.ReservedPuddleCount >= 3;
        }

        private void BeginBarrage()
        {
            State = GolgoraState.BarrageWindup;
            phaseTime = 0f;
            nextBarrageTime = BossTime + 8f;
            StopBody();
        }

        private void ExecuteStomp()
        {
            StompsExecuted++;
            DamagePlayerInRadius(stompOrigin, StompRadius, 40f, DamageSourceType.BossStomp);
            int count = 0;
            var actors = ActiveEnemies;
            for (int i = 0; i < actors.Count && count < collateralTargets.Length; i++)
            {
                EnemyActor actor = actors[i];
                if (actor == null || actor == this || !actor.IsAlive || actor.ArchetypeId == EnemyArchetypeId.GolgoraBoss)
                    continue;
                Vector3 delta = actor.WorldCenter - stompOrigin;
                delta.y = 0f;
                float hitRadius = StompRadius + actor.HitRadius;
                if (delta.sqrMagnitude <= hitRadius * hitRadius)
                    collateralTargets[count++] = actor;
            }
            for (int i = 0; i < count; i++)
            {
                EnemyActor actor = collateralTargets[i];
                collateralTargets[i] = null;
                if (actor == null || !actor.IsAlive)
                    continue;
                Vector3 direction = actor.WorldCenter - stompOrigin;
                direction.y = 0f;
                direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
                var damage = new DamageRequest(TargetId, DamageSourceType.BossStomp, stompOrigin, direction, 50f);
                actor.ReceiveDamage(in damage);
                if (actor.IsAlive)
                    actor.ApplyKnockback(direction * 10f);
            }
            presentation?.TriggerStomp(stompOrigin);
        }

        private void DamagePlayerInRadius(Vector3 origin, float radius, float damage, DamageSourceType source)
        {
            if (TargetPlayer == null || !TargetPlayer.IsAlive)
                return;
            Vector3 direction = TargetPlayer.WorldCenter - origin;
            direction.y = 0f;
            float hitRadius = radius + TargetPlayer.HitRadius;
            if (direction.sqrMagnitude > hitRadius * hitRadius)
                return;
            var request = new DamageRequest(TargetId, source, origin,
                direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward, damage);
            TargetPlayer.ReceiveDamage(in request);
        }

        private void ExecuteBarrage()
        {
            Vector3 velocity = playerBody != null ? playerBody.linearVelocity : Vector3.zero;
            velocity.y = 0f;
            Vector3 predicted = TargetPlayer.WorldCenter + velocity * 0.6f;
            predicted.y = 0f;
            Vector3 forward = predicted - WorldCenter;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            barrageTargets[0] = predicted;
            barrageTargets[1] = predicted + Quaternion.AngleAxis(-60f, Vector3.up) * forward * 4f;
            barrageTargets[2] = predicted + Quaternion.AngleAxis(60f, Vector3.up) * forward * 4f;
            for (int i = 0; i < barrageTargets.Length; i++)
            {
                Vector3 position = barrageTargets[i];
                position.x = Mathf.Clamp(position.x, -56.5f, 56.5f);
                position.z = Mathf.Clamp(position.z, -56.5f, 56.5f);
                barrageTargets[i] = position;
                var spec = new CorrosivePuddlePool.ProjectileSpec
                {
                    SourceId = TargetId, LaunchOrigin = WorldCenter + Vector3.up * 3.3f,
                    TargetPosition = position, FlightDuration = 0.65f, ArcPeakHeight = 3.5f,
                    ImpactRadius = 2.5f, DirectImpactDamage = 25f,
                    DirectImpactSource = DamageSourceType.BossBombardmentImpact,
                    PuddleRadius = 2.5f, PuddleDuration = 4f, PuddleTickDamage = 5f,
                    PuddleTickInterval = 0.5f, PuddleDamageSource = DamageSourceType.BossBombardmentPuddle
                };
                hazards.TryLaunchParabolicProjectile(in spec);
            }
            BarragesExecuted++;
        }

        public Vector3 GetLastBarrageTarget(int index) => barrageTargets[Mathf.Clamp(index, 0, 2)];

        private void EnterRecovery()
        {
            State = GolgoraState.Recovery;
            phaseTime = 0f;
            StopBody();
        }

        protected override void ExecuteDeath(Vector3 capturedPosition, Vector3 hitDirection, DamageSourceType cause)
        {
            State = GolgoraState.Dead;
            presentation?.ResetPresentation();
            base.ExecuteDeath(capturedPosition, hitDirection, cause);
        }
    }
}
