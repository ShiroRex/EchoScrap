using System;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    public enum ColossusChargeState { Pursuit, Windup, Charging, Recovery }

    [Serializable]
    public sealed class ColossusChargeSettings
    {
        [Min(0.1f)] public float Windup = 0.8f;
        [Min(1f)] public float Speed = 12f;
        [Min(0.05f)] public float Duration = 0.35f;
        [Min(0.1f)] public float Recovery = 0.6f;
        [Min(0.1f)] public float CooldownMin = 5f;
        [Min(0.1f)] public float CooldownMax = 7f;
        [Min(1f)] public float TriggerRange = 8f;
        public float Distance => Speed * Duration;
    }

    /// <summary>Fixed-step, committed heavy charge. Rendering never decides hits or timing.</summary>
    [DisallowMultipleComponent]
    public sealed class ColossusCharge : MonoBehaviour
    {
        [SerializeField] private ColossusChargeSettings settings = new ColossusChargeSettings();
        private readonly RaycastHit[] sweepHits = new RaycastHit[16];
        private EnemyActor actor;
        private Rigidbody targetBody;
        private ColossusChargePresentation presentation;
        private float phaseTime;
        public ColossusChargeSettings Settings => settings;
        public ColossusChargeState State { get; private set; }
        public Vector3 LockedDirection { get; private set; } = Vector3.forward;
        public Vector3 WarningOrigin { get; private set; }
        public float WarningDistance { get; private set; }
        public float CooldownRemaining { get; private set; }
        public int ChargesStarted { get; private set; }
        public int WallStops { get; private set; }
        public int PlayerHitAttempts { get; private set; }
        public ColossusChargePresentation Presentation => presentation;
        private Vector3 Position => actor.Body.position;
        public bool AllowsOrdinaryContact => State == ColossusChargeState.Pursuit;
        public float PhaseProgress => Mathf.Clamp01(phaseTime / Mathf.Max(0.001f,
            State == ColossusChargeState.Windup ? settings.Windup :
            State == ColossusChargeState.Charging ? settings.Duration : settings.Recovery));

        public void Bind(EnemyActor owner)
        {
            actor = owner;
            targetBody = owner.TargetPlayer != null ? owner.TargetPlayer.GetComponent<Rigidbody>() : null;
            if (presentation == null)
                presentation = GetComponent<ColossusChargePresentation>() ?? gameObject.AddComponent<ColossusChargePresentation>();
            presentation.Bind(this, owner);
        }

        public void ResetForSpawn()
        {
            State = ColossusChargeState.Pursuit;
            phaseTime = 0f;
            LockedDirection = actor.FacingDirection;
            ChargesStarted = WallStops = PlayerHitAttempts = 0;
            CooldownRemaining = NextCooldown();
            presentation.ResetPresentation();
        }

        private float NextCooldown() => actor.Session != null
            ? actor.Session.Rng.Combat.Range(settings.CooldownMin, settings.CooldownMax)
            : settings.CooldownMin;

        public bool TryOverrideLocomotion(float deltaTime, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (deltaTime <= 0f || !actor.IsAlive ||
                (actor.Session != null && (actor.Session.IsPaused || !actor.Session.IsGameplayFlowActive())))
                return true;
            CooldownRemaining = Mathf.Max(0f, CooldownRemaining - deltaTime);
            if (actor.TargetPlayer == null || !actor.TargetPlayer.IsAlive) return true;

            switch (State)
            {
                case ColossusChargeState.Pursuit:
                    Vector3 target = (targetBody != null ? targetBody.position : actor.TargetPlayer.WorldCenter) - Position;
                    target.y = 0f;
                    if (CooldownRemaining > 0f || target.sqrMagnitude > settings.TriggerRange * settings.TriggerRange)
                        return false;
                    LockedDirection = target.sqrMagnitude > 0.001f ? target.normalized : actor.FacingDirection;
                    WarningOrigin = Position;
                    WarningDistance = FindWallDistance(settings.Distance);
                    phaseTime = 0f;
                    State = ColossusChargeState.Windup;
                    presentation.BeginWarning();
                    return true;
                case ColossusChargeState.Windup:
                    phaseTime += deltaTime;
                    if (phaseTime + 0.00001f >= settings.Windup)
                    {
                        State = ColossusChargeState.Charging;
                        phaseTime = 0f;
                        ChargesStarted++;
                        presentation.BeginCharge();
                    }
                    return true;
                case ColossusChargeState.Charging:
                    float slice = Mathf.Min(deltaTime, Mathf.Max(0f, settings.Duration - phaseTime));
                    float distance = settings.Speed * slice;
                    phaseTime += slice;
                    bool blocked = Sweep(distance, (1 << PhysicsLayers.DefaultLayer) | (1 << PhysicsLayers.PlayerLayer),
                        out RaycastHit hit);
                    bool touchingPlayer = actor.TargetPlayer.gameObject.layer != PhysicsLayers.PlayerDashLayer &&
                        ContactCollisionUtility.AreCollidersTouching(actor.BodyCollider, actor.TargetPlayer.HurtboxCollider,
                            out _, out _);
                    if (touchingPlayer || (blocked && hit.collider == actor.TargetPlayer.HurtboxCollider))
                    {
                        PlayerHitAttempts++;
                        actor.ResolveChargeContact(LockedDirection);
                        distance = touchingPlayer ? 0f : Mathf.Max(0f, hit.distance - 0.02f);
                        velocity = LockedDirection * (distance / deltaTime);
                        FinishCharge(Position + LockedDirection * distance, -LockedDirection, false);
                    }
                    else if (blocked)
                    {
                        distance = Mathf.Max(0f, hit.distance - 0.02f);
                        velocity = LockedDirection * (distance / deltaTime);
                        WallStops++;
                        FinishCharge(hit.point, hit.normal, true);
                    }
                    else
                    {
                        velocity = LockedDirection * (distance / deltaTime);
                        if (phaseTime + 0.00001f >= settings.Duration)
                            FinishCharge(Position + LockedDirection * distance, -LockedDirection, false);
                    }
                    return true;
                default:
                    phaseTime += deltaTime;
                    if (phaseTime + 0.00001f >= settings.Recovery)
                    {
                        State = ColossusChargeState.Pursuit;
                        phaseTime = 0f;
                    }
                    return true;
            }
        }

        private void FinishCharge(Vector3 impact, Vector3 normal, bool wall)
        {
            State = ColossusChargeState.Recovery;
            phaseTime = 0f;
            CooldownRemaining = NextCooldown();
            presentation.TriggerImpact(impact, normal, wall);
        }

        private float FindWallDistance(float distance) => Sweep(distance, 1 << PhysicsLayers.DefaultLayer, out RaycastHit hit)
            ? Mathf.Max(0f, hit.distance - 0.02f) : distance;

        private bool Sweep(float distance, int mask, out RaycastHit closest)
        {
            CapsuleCollider capsule = actor.BodyCollider;
            Vector3 scale = capsule.transform.lossyScale;
            float radius = capsule.radius * Mathf.Max(scale.x, scale.z);
            float segment = Mathf.Max(0f, capsule.height * scale.y * 0.5f - radius);
            // Cast from the simulation pose, independent of the interpolated render transform.
            Vector3 center = Position + actor.Body.rotation * Vector3.Scale(capsule.center, scale) + Vector3.up * 0.02f;
            int count = Physics.CapsuleCastNonAlloc(center - Vector3.up * segment, center + Vector3.up * segment,
                Mathf.Max(0.01f, radius - 0.01f), LockedDirection, sweepHits, distance + 0.02f, mask,
                QueryTriggerInteraction.Ignore);
            closest = default;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = sweepHits[i];
                if (Mathf.Abs(hit.normal.y) > 0.6f || hit.distance >= nearest) continue;
                nearest = hit.distance;
                closest = hit;
            }
            return nearest < float.PositiveInfinity;
        }

        private void OnDisable()
        {
            State = ColossusChargeState.Pursuit;
            phaseTime = 0f;
            presentation?.ResetPresentation();
        }
    }
}
