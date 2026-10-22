using System;
using System.Collections.Generic;
using UnityEngine;

namespace EchoScrap.Core
{
    public enum SessionState
    {
        Running = 0,
        Paused = 1,
        TacticalDraft = 2,
        BossIntro = 3,
        Victory = 4,
        Defeat = 5
    }

    public enum DamageSourceType
    {
        Contact = 0,
        FlashlightPulse = 1,
        SolarPulse = 2,
        MortarPuddle = 3,
        MothExplosion = 4,
        BossStomp = 5,
        BossBombardmentImpact = 6,
        BossBombardmentPuddle = 7,
        DummyOrHazard = 8
    }

    public readonly struct DamageRequest
    {
        public readonly int SourceId;
        public readonly DamageSourceType SourceType;
        public readonly Vector3 OriginPosition;
        public readonly Vector3 HitDirection;
        public readonly float BaseDamage;
        public readonly bool CanCrit;
        public readonly float CritChance;
        public readonly float CritMultiplier;
        public readonly int BurstId;
        public readonly int PulseIndex;
        public readonly float KnockbackImpulse;

        public DamageRequest(
            int sourceId,
            DamageSourceType sourceType,
            Vector3 originPosition,
            Vector3 hitDirection,
            float baseDamage,
            bool canCrit = false,
            float critChance = 0f,
            float critMultiplier = 1.75f,
            int burstId = 0,
            int pulseIndex = 0,
            float knockbackImpulse = 0f)
        {
            SourceId = sourceId;
            SourceType = sourceType;
            OriginPosition = originPosition;
            HitDirection = hitDirection;
            BaseDamage = baseDamage;
            CanCrit = canCrit;
            CritChance = critChance;
            CritMultiplier = critMultiplier;
            BurstId = burstId;
            PulseIndex = pulseIndex;
            KnockbackImpulse = knockbackImpulse;
        }
    }

    /// <summary>
    /// Resultado inmutable de daño. Conserva la posición y el estado en el instante del impacto
    /// antes de devolver al actor al pool o desactivarlo.
    /// </summary>
    public readonly struct DamageResult
    {
        public readonly int SourceId;
        public readonly int TargetId;
        public readonly DamageSourceType SourceType;
        public readonly Vector3 OriginPosition;
        public readonly Vector3 WorldPosition;
        public readonly Vector3 HitDirection;
        public readonly float RawDamage;
        public readonly float ResolvedDamage;
        public readonly float HealthLost;
        public readonly float RemainingHealth;
        public readonly float MaxHealth;
        public readonly bool IsCritical;
        public readonly bool WasBlocked;
        public readonly bool WasDodged;
        public readonly bool WasImmune;
        public readonly bool CausedDeath;
        public readonly bool IsPlayerTarget;
        public readonly int BurstId;
        public readonly int PulseIndex;

        public bool AppliedRealDamage => HealthLost > 0f && !WasImmune && !WasDodged;

        public DamageResult(
            int sourceId,
            int targetId,
            DamageSourceType sourceType,
            Vector3 originPosition,
            Vector3 worldPosition,
            Vector3 hitDirection,
            float rawDamage,
            float resolvedDamage,
            float healthLost,
            float remainingHealth,
            float maxHealth,
            bool isCritical,
            bool wasBlocked,
            bool wasDodged,
            bool wasImmune,
            bool causedDeath,
            bool isPlayerTarget,
            int burstId = 0,
            int pulseIndex = 0)
        {
            SourceId = sourceId;
            TargetId = targetId;
            SourceType = sourceType;
            OriginPosition = originPosition;
            WorldPosition = worldPosition;
            HitDirection = hitDirection;
            RawDamage = rawDamage;
            ResolvedDamage = resolvedDamage;
            HealthLost = healthLost;
            RemainingHealth = remainingHealth;
            MaxHealth = maxHealth;
            IsCritical = isCritical;
            WasBlocked = wasBlocked;
            WasDodged = wasDodged;
            WasImmune = wasImmune;
            CausedDeath = causedDeath;
            IsPlayerTarget = isPlayerTarget;
            BurstId = burstId;
            PulseIndex = pulseIndex;
        }
    }

    public readonly struct PulseResolvedEvent
    {
        public readonly int BurstId;
        public readonly int PulseIndex; // 1..4
        public readonly bool IsSolarMode;
        public readonly int HitCount;
        public readonly int KillCount;
        public readonly int CritCount;
        public readonly Vector3 Origin;
        public readonly Vector3 AimDirection;
        public readonly float Range;
        public readonly float Angle;

        public PulseResolvedEvent(
            int burstId,
            int pulseIndex,
            bool isSolarMode,
            int hitCount,
            int killCount,
            int critCount,
            Vector3 origin,
            Vector3 aimDirection,
            float range,
            float angle)
        {
            BurstId = burstId;
            PulseIndex = pulseIndex;
            IsSolarMode = isSolarMode;
            HitCount = hitCount;
            KillCount = killCount;
            CritCount = critCount;
            Origin = origin;
            AimDirection = aimDirection;
            Range = range;
            Angle = angle;
        }
    }

    public readonly struct DashStateEvent
    {
        public readonly bool Started;
        public readonly bool Ended;
        public readonly bool BlockedByWall;
        public readonly bool IsDashing;
        public readonly bool IsAvailable;
        public readonly Vector3 Position;
        public readonly Vector3 Direction;
        public readonly float CooldownRemaining;
        public readonly float CooldownDuration;

        public DashStateEvent(
            bool started,
            bool ended,
            bool blockedByWall,
            bool isDashing,
            bool isAvailable,
            Vector3 position,
            Vector3 direction,
            float cooldownRemaining,
            float cooldownDuration)
        {
            Started = started;
            Ended = ended;
            BlockedByWall = blockedByWall;
            IsDashing = isDashing;
            IsAvailable = isAvailable;
            Position = position;
            Direction = direction;
            CooldownRemaining = cooldownRemaining;
            CooldownDuration = cooldownDuration;
        }
    }

    public readonly struct ActorDeathEvent
    {
        public readonly int ActorId;
        public readonly int ArchetypeId;
        public readonly bool IsPlayer;
        public readonly bool IsBoss;
        public readonly Vector3 WorldPosition;
        public readonly Vector3 HitDirection;
        public readonly DamageSourceType Cause;
        public readonly int XpReward;
        public readonly bool DroppedMedkit;
        public readonly bool RewardResolved;

        public ActorDeathEvent(
            int actorId,
            int archetypeId,
            bool isPlayer,
            bool isBoss,
            Vector3 worldPosition,
            Vector3 hitDirection,
            DamageSourceType cause,
            int xpReward,
            bool droppedMedkit,
            bool rewardResolved)
        {
            ActorId = actorId;
            ArchetypeId = archetypeId;
            IsPlayer = isPlayer;
            IsBoss = isBoss;
            WorldPosition = worldPosition;
            HitDirection = hitDirection;
            Cause = cause;
            XpReward = xpReward;
            DroppedMedkit = droppedMedkit;
            RewardResolved = rewardResolved;
        }
    }

    public readonly struct PlayerStatsSnapshot
    {
        public readonly float CurrentHealth;
        public readonly float MaxHealth;
        public readonly float ProtectionDuration;
        public readonly float Armor;
        public readonly float Evasion;
        public readonly float BlockChance;
        public readonly float BlockReduction;
        public readonly float CritChance;
        public readonly float CritMultiplier;
        public readonly float MoveSpeed;
        public readonly float DashSpeed;
        public readonly float DashDuration;
        public readonly float DashCooldown;
        public readonly float FlashlightRange;
        public readonly float FlashlightAngle;
        public readonly float FlashlightDamagePerPulse;
        public readonly float FlashlightAttackSpeed;
        public readonly float FlashlightKnockback;
        public readonly bool IsSolarActive;
        public readonly float MagnetRadius;
        public readonly float MedkitHealAmount;

        public PlayerStatsSnapshot(
            float currentHealth,
            float maxHealth,
            float protectionDuration,
            float armor,
            float evasion,
            float blockChance,
            float blockReduction,
            float critChance,
            float critMultiplier,
            float moveSpeed,
            float dashSpeed,
            float dashDuration,
            float dashCooldown,
            float flashlightRange,
            float flashlightAngle,
            float flashlightDamagePerPulse,
            float flashlightAttackSpeed,
            float flashlightKnockback,
            bool isSolarActive,
            float magnetRadius,
            float medkitHealAmount)
        {
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
            ProtectionDuration = protectionDuration;
            Armor = armor;
            Evasion = evasion;
            BlockChance = blockChance;
            BlockReduction = blockReduction;
            CritChance = critChance;
            CritMultiplier = critMultiplier;
            MoveSpeed = moveSpeed;
            DashSpeed = dashSpeed;
            DashDuration = dashDuration;
            DashCooldown = dashCooldown;
            FlashlightRange = flashlightRange;
            FlashlightAngle = flashlightAngle;
            FlashlightDamagePerPulse = flashlightDamagePerPulse;
            FlashlightAttackSpeed = flashlightAttackSpeed;
            FlashlightKnockback = flashlightKnockback;
            IsSolarActive = isSolarActive;
            MagnetRadius = magnetRadius;
            MedkitHealAmount = medkitHealAmount;
        }
    }

    public readonly struct SessionStateEvent
    {
        public readonly SessionState State;
        public readonly PauseReason PauseReasons;
        public readonly float GameplayTime;
        public readonly PlayerStatsSnapshot EffectiveStats;

        public SessionStateEvent(
            SessionState state,
            PauseReason pauseReasons,
            float gameplayTime,
            PlayerStatsSnapshot effectiveStats)
        {
            State = state;
            PauseReasons = pauseReasons;
            GameplayTime = gameplayTime;
            EffectiveStats = effectiveStats;
        }
    }

    public readonly struct HealResult
    {
        public readonly float RequestedAmount;
        public readonly float ActualHealed;
        public readonly float CurrentHealth;
        public readonly float MaxHealth;
        public readonly bool WasAlreadyFull;

        public HealResult(float requestedAmount, float actualHealed, float currentHealth, float maxHealth, bool wasAlreadyFull)
        {
            RequestedAmount = requestedAmount;
            ActualHealed = actualHealed;
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
            WasAlreadyFull = wasAlreadyFull;
        }
    }

    public interface IDamageTarget
    {
        int TargetId { get; }
        bool IsAlive { get; }
        bool IsEnemyTarget { get; }
        Vector3 WorldCenter { get; }
        float HitRadius { get; }
        DamageResult ReceiveDamage(in DamageRequest request);
        void ApplyKnockback(Vector3 planarImpulse);
    }

    /// <summary>
    /// Registro activo libre de asignaciones para enemigos y objetivos de combate.
    /// </summary>
    public static class CombatTargetRegistry
    {
        private static readonly List<IDamageTarget> activeTargets = new List<IDamageTarget>(256);

        public static IReadOnlyList<IDamageTarget> ActiveTargets => activeTargets;

        public static void Register(IDamageTarget target)
        {
            if (target != null && !activeTargets.Contains(target))
                activeTargets.Add(target);
        }

        public static void Unregister(IDamageTarget target)
        {
            if (target != null)
                activeTargets.Remove(target);
        }

        public static void Clear()
        {
            activeTargets.Clear();
        }
    }

    /// <summary>
    /// Canal de eventos tipados propio de la sesión activa.
    /// Se limpia íntegramente al reiniciar o cerrar la sesión.
    /// </summary>
    public sealed class CombatEventBus
    {
        public event Action<DamageResult> OnDamageResolved;
        public event Action<PulseResolvedEvent> OnPulseResolved;
        public event Action<DashStateEvent> OnDashStateChanged;
        public event Action<ActorDeathEvent> OnActorDied;
        public event Action<SessionStateEvent> OnSessionStateChanged;
        public event Action<HealResult> OnPlayerHealed;

        public void PublishDamage(in DamageResult result) => OnDamageResolved?.Invoke(result);
        public void PublishPulse(in PulseResolvedEvent pulse) => OnPulseResolved?.Invoke(pulse);
        public void PublishDash(in DashStateEvent dashState) => OnDashStateChanged?.Invoke(dashState);
        public void PublishDeath(in ActorDeathEvent deathEvent) => OnActorDied?.Invoke(deathEvent);
        public void PublishSessionState(in SessionStateEvent stateEvent) => OnSessionStateChanged?.Invoke(stateEvent);
        public void PublishPlayerHealed(in HealResult healResult) => OnPlayerHealed?.Invoke(healResult);

        public void ClearAllListeners()
        {
            OnDamageResolved = null;
            OnPulseResolved = null;
            OnDashStateChanged = null;
            OnActorDied = null;
            OnSessionStateChanged = null;
            OnPlayerHealed = null;
        }
    }
}
