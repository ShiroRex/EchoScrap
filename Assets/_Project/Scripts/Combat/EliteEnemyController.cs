using EchoScrap.Core;
using Unity.Profiling;
using UnityEngine;

namespace EchoScrap.Combat
{
    public enum EliteEnemyState { Pursuit, Windup, Recovery }

    /// <summary>Committed attacks and resistance windows advance on the actor's simulation clock.</summary>
    [DisallowMultipleComponent]
    public sealed class EliteEnemyController : MonoBehaviour
    {
        private static readonly ProfilerMarker StepMarker = new ProfilerMarker("EchoScrap.Elite.Step");
        private EnemyActor actor;
        private EliteEnemyPresentation presentation;
        private EliteProjectilePool projectiles;
        private EnemyEffectPool effects;
        private const float SweepVolume = 0.7f;
        private const float SentinelVolume = 0.7f;
        private AudioSource sentinelSound;
        private AudioClip sentinelClip;
        private AudioSource sweepSound;
        private AudioClip sweepClip;
        private float phaseTime;
        public EliteEnemyState State { get; private set; }
        public Vector3 LockedDirection { get; private set; } = Vector3.forward;
        public Vector3 WarningOrigin { get; private set; }
        public float CooldownRemaining { get; private set; }
        public float PhaseTime => phaseTime;
        public int AttacksResolved { get; private set; }
        public int PlayerHitAttempts { get; private set; }
        public EliteEnemyPresentation Presentation => presentation;
        public bool IsCopper => actor != null && actor.ArchetypeId == EnemyArchetypeId.CopperSentinel;
        public bool IsVulnerable => IsCopper && actor.IsAlive && actor.gameObject.activeInHierarchy && State == EliteEnemyState.Recovery;
        public EliteAttackData Settings => actor.Archetype.EliteAttack.Windup > 0f ? actor.Archetype.EliteAttack
            : IsCopper ? EliteAttackData.Copper : EliteAttackData.Ghost;

        public void Bind(EnemyActor owner)
        {
            actor = owner;
            effects = EnemyEffectPool.GetOrCreate(owner.Session);
            presentation = GetComponent<EliteEnemyPresentation>() ?? gameObject.AddComponent<EliteEnemyPresentation>();
            presentation.Bind(actor, this);
            if (IsCopper) projectiles = EliteProjectilePool.GetOrCreate(actor.Session, actor.TargetPlayer);
        }

        public void ResetForSpawn()
        {
            State = EliteEnemyState.Pursuit; phaseTime = 0f;
            CooldownRemaining = .8f; LockedDirection = Vector3.forward;
            AttacksResolved = PlayerHitAttempts = 0;
            presentation?.Refresh();
        }

        public float DamageMultiplier(DamageType type)
        {
            if (actor == null || (IsCopper && IsVulnerable)) return 1f;
            DamageType resisted = IsCopper ? DamageType.Supernatural : DamageType.Physical;
            return type == resisted ? Mathf.Clamp(Settings.ResistedDamageMultiplier, .01f, 1f) : 1f;
        }

        public bool TryOverrideLocomotion(float dt, out Vector3 velocity)
        {
            using var marker = StepMarker.Auto();
            velocity = Vector3.zero;
            if (actor == null || !actor.IsAlive || actor.SimulationSuspended || actor.IsControlLocked || dt <= 0f ||
                (actor.Session != null && (actor.Session.IsPaused || !actor.Session.IsGameplayFlowActive())))
                return State != EliteEnemyState.Pursuit;
            var player = actor.TargetPlayer;
            if (player == null || !player.IsAlive)
            {
                State = EliteEnemyState.Pursuit; phaseTime = 0f;
                presentation.Refresh(); return false;
            }
            if (State == EliteEnemyState.Pursuit)
            {
                CooldownRemaining = Mathf.Max(0f, CooldownRemaining - dt);
                Vector3 offset = player.WorldCenter - actor.WorldCenter; offset.y = 0f;
                if (CooldownRemaining > 0f || offset.sqrMagnitude > Settings.Range * Settings.Range ||
                    Physics.Linecast(actor.WorldCenter, player.WorldCenter, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore))
                    return false;
                LockedDirection = offset.sqrMagnitude > .0001f ? offset.normalized : actor.FacingDirection;
                WarningOrigin = actor.WorldCenter;
                State = EliteEnemyState.Windup; phaseTime = 0f;
                if (IsCopper) PlaySentinelAttackSound();
                else PlaySweepSound();
            }
            else
            {
                phaseTime += dt;
                if (State == EliteEnemyState.Windup && phaseTime + .00001f >= Mathf.Max(.1f, Settings.Windup))
                {
                    if (IsCopper)
                    {
                        if (projectiles == null || !projectiles.FireFan(actor.TargetId, WarningOrigin, LockedDirection, Settings))
                            return true;
                        effects?.Play(EnemyEffectId.CopperMuzzle, WarningOrigin + LockedDirection * .6f, LockedDirection);
                    }
                    else
                    {
                        ResolveSweep();
                        effects?.Play(EnemyEffectId.SpectralSweep, WarningOrigin, LockedDirection, Vector2.one * Settings.Range * 2.5f);
                    }
                    AttacksResolved++;
                    State = EliteEnemyState.Recovery; phaseTime = 0f;
                }
                else if (State == EliteEnemyState.Recovery && phaseTime + .00001f >= Mathf.Max(.1f, Settings.Recovery))
                {
                    State = EliteEnemyState.Pursuit; phaseTime = 0f;
                    CooldownRemaining = Mathf.Max(.1f, Settings.Cooldown);
                }
            }
            presentation.Refresh();
            return State != EliteEnemyState.Pursuit;
        }

        private void PlaySentinelAttackSound()
        {
            if (sentinelSound == null)
            {
                sentinelClip = Resources.Load<AudioClip>("Audio/Centinela_Disparo");
                sentinelSound = gameObject.AddComponent<AudioSource>();
                sentinelSound.playOnAwake = false;
                sentinelSound.spatialBlend = 0f;
                sentinelSound.volume = SentinelVolume;
            }
            if (sentinelClip != null)
                sentinelSound.PlayOneShot(sentinelClip);
        }

        private void PlaySweepSound()
        {
            if (sweepSound == null)
            {
                sweepClip = Resources.Load<AudioClip>("Audio/Caballero_Barrido");
                sweepSound = gameObject.AddComponent<AudioSource>();
                sweepSound.playOnAwake = false;
                sweepSound.spatialBlend = 0f;
                sweepSound.volume = SweepVolume;
            }
            if (sweepClip != null)
                sweepSound.PlayOneShot(sweepClip);
        }

        private void ResolveSweep()
        {
            PlayerHealth player = actor.TargetPlayer;
            Vector3 toPlayer = player.WorldCenter - WarningOrigin; toPlayer.y = 0f;
            float distance = toPlayer.magnitude;
            float extraAngle = distance > player.HitRadius ? Mathf.Asin(Mathf.Clamp01(player.HitRadius / distance)) * Mathf.Rad2Deg : 180f;
            if (distance > Settings.Range + player.HitRadius ||
                Vector3.Angle(LockedDirection, toPlayer) > Settings.ArcDegrees * .5f + extraAngle ||
                Physics.Linecast(WarningOrigin, player.WorldCenter, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore)) return;
            PlayerHitAttempts++;
            var hit = new DamageRequest(actor.TargetId, DamageSourceType.SpectralSweep, WarningOrigin,
                toPlayer.sqrMagnitude > .001f ? toPlayer.normalized : LockedDirection, Mathf.Max(0f, Settings.Damage), damageType: DamageType.Supernatural);
            player.ReceiveDamage(in hit);
        }

        private void OnDisable()
        {
            State = EliteEnemyState.Pursuit; phaseTime = 0f;
            presentation?.Refresh();
        }
    }
}
