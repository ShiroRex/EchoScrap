using System;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Entrada segura y cierre del jefe; el director conserva la propiedad exclusiva de sus esbirros.</summary>
    [DisallowMultipleComponent]
    public sealed class BossEncounter : MonoBehaviour
    {
        public static float IntroDuration => GolgoraRoarPresentation.EntranceDuration;
        public const int ClosingXp = 500;
        public const float ClearanceMargin = 0.5f;
        [SerializeField] private CombatSession session;
        [SerializeField] private EnemyPool enemies;
        [SerializeField] private CorrosivePuddlePool hazards;
        [SerializeField] private CombatWaveDirector director;
        [SerializeField] private PlayerHealth player;
        private CombatSession subscribedSession;
        private CombatWaveDirector subscribedDirector;
        private GolgoraDeathPresentation deathPresentation;
        private Rigidbody playerBody;
        private bool introInProgress;
        private bool closingRewardResolved;
        private BossArena arena;

        public bool AutoTick { get; set; } = true;
        public GolgoraActor Boss { get; private set; }
        public float IntroRemaining { get; private set; }
        public bool IsEncounterActive { get; private set; }
        public int ClosingXpReward { get; private set; }
        public bool IntroWaitingForClearance { get; private set; }
        public GolgoraDeathPresentation DeathPresentation => deathPresentation;
        public BossArena Arena => arena;
        public GolgoraRoarPresentation RoarPresentation { get; private set; }
        public event Action<GolgoraActor> OnBossActivated;
        public event Action<int> ClosingXpAwarded;

        public void BindArena(BossArena encounterArena) => arena = encounterArena;

        public void BindReferences(CombatSession combatSession, EnemyPool enemyPool, CorrosivePuddlePool hazardPool,
            CombatWaveDirector waveDirector, PlayerHealth targetPlayer)
        {
            Unsubscribe();
            session = combatSession;
            enemies = enemyPool;
            hazards = hazardPool;
            director = waveDirector;
            player = targetPlayer;
            playerBody = player != null ? player.GetComponent<Rigidbody>() : null;
            deathPresentation = GetComponent<GolgoraDeathPresentation>() ?? gameObject.AddComponent<GolgoraDeathPresentation>();
            deathPresentation.BindSession(session);
            RoarPresentation = GetComponent<GolgoraRoarPresentation>() ?? gameObject.AddComponent<GolgoraRoarPresentation>();
            RoarPresentation.Bind(session);
            if (isActiveAndEnabled) Subscribe();
        }

        private void OnEnable() => Subscribe();
        private void Subscribe()
        {
            if (session != null && subscribedSession != session)
            {
                subscribedSession = session;
                subscribedSession.SessionReset += ResetForSession;
                subscribedSession.Events.OnActorDied += HandleActorDeath;
            }
            if (director != null && subscribedDirector != director)
            {
                subscribedDirector = director;
                subscribedDirector.OnBossPhaseRequested += BeginEncounter;
            }
        }

        private void Unsubscribe()
        {
            if (subscribedSession != null)
            {
                subscribedSession.SessionReset -= ResetForSession;
                subscribedSession.Events.OnActorDied -= HandleActorDeath;
            }
            if (subscribedDirector != null)
                subscribedDirector.OnBossPhaseRequested -= BeginEncounter;
            subscribedSession = null;
            subscribedDirector = null;
        }

        private void OnDisable()
        {
            Unsubscribe();
            ResetForSession();
        }

        public void BeginEncounter()
        {
            if (IsEncounterActive || closingRewardResolved || enemies == null || player == null || !player.IsAlive
                || (session != null && !session.IsGameplayFlowActive()))
                return;
            enemies.DespawnAllActive();
            hazards?.ClearAll();
            if (session != null) MothExplosionPool.GetOrCreate(session, player).ClearAll();
            session?.GetComponent<EliteProjectilePool>()?.ClearAll();
            director?.SetBossActive(false);
            director?.SuppressBossMinions(false);
            session?.SetBossIntroState(true);
            if (player != null) player.IsBossCinematicProtected = true;
            Vector3 spawn = arena != null ? ArenaSurface.OnGround(arena.Center, 1f) : Vector3.up;
            Boss = enemies.Spawn(EnemyArchetypeId.GolgoraBoss, spawn) as GolgoraActor;
            if (Boss == null) { ReleaseCinematic(); return; }
            Boss.BindHazards(hazards);
            Boss.PhaseTransitionStarted += BeginPhaseRoar;
            Boss.PhaseTransitionCompleted += ReleaseCinematic;
            if (arena != null)
            {
                Boss.BindArena(arena.Center, arena.Radius);
                Boss.CercoStarted += SuspendReinforcements;
                Boss.CercoCompleted += ResumeReinforcements;
                arena.BeginEncounter(Boss);
            }
            IsEncounterActive = true;
            introInProgress = true;
            IntroRemaining = IntroDuration;
            IntroWaitingForClearance = false;
            ClosingXpReward = 0;
            deathPresentation?.Clear();
            RoarPresentation?.BeginIntro();
        }

        private void Update()
        {
            if (AutoTick) Step(Time.deltaTime);
        }

        public void Step(float deltaTime)
        {
            if (deltaTime <= 0f || !IsEncounterActive || Boss == null
                || (session != null && (session.IsPaused || !session.IsGameplayFlowActive())))
                return;
            arena?.Step(deltaTime);
            float cinematicDelta = deltaTime / Mathf.Max(.05f, session != null ? session.Clock.SimulationRate : 1f);
            if (!introInProgress)
            {
                if (Boss.State == GolgoraState.PhaseTransition)
                {
                    session?.PlayerMotor?.Stop();
                    RoarPresentation?.StepPhase(Boss.PhaseProgress * Boss.CurrentPhaseDuration, cinematicDelta);
                }
                return;
            }
            session?.PlayerMotor?.Stop();
            if (arena != null && arena.IsTransferring) return;
            IntroRemaining = Mathf.Max(0f, IntroRemaining - cinematicDelta);
            RoarPresentation?.StepIntro(IntroDuration - IntroRemaining, cinematicDelta);
            var presentation = Boss.GetComponent<GolgoraPresentation>();
            presentation?.SetIntroProgress(1f - IntroRemaining / IntroDuration);
            if (IntroRemaining > 0.0001f) return;
            // Nunca activar un collider gigante sobre el jugador. Si el centro estuviera completamente
            // bloqueado por un escenario personalizado, conservar el aviso mientras se obtiene espacio.
            IntroWaitingForClearance = !EnsurePlayerClearance();
            if (IntroWaitingForClearance) return;
            introInProgress = false;
            IntroRemaining = 0f;
            Boss.ActivateCombat();
            ReleaseCinematic();
            director?.SetBossActive(true);
            OnBossActivated?.Invoke(Boss);
        }

        private void SuspendReinforcements()
        {
            director?.SuppressBossMinions(true);
            arena?.CancelReinforcements();
            // The foundry discharges before its ring sequence. No kill rewards or delayed explosions.
            var actors = EnemyActor.ActiveEnemies;
            for (int i = actors.Count - 1; i >= 0; i--)
            {
                var actor = actors[i];
                if (actor != null && actor != Boss && actor.Session == session) enemies.ReturnToPool(actor);
            }
            hazards?.ClearAll();
            if (session != null) MothExplosionPool.GetOrCreate(session, player).ClearAll();
            session?.GetComponent<EliteProjectilePool>()?.ClearAll();
        }

        private void ResumeReinforcements() => director?.SuppressBossMinions(false);

        private void BeginPhaseRoar()
        {
            if (player != null) player.IsBossCinematicProtected = true;
            session?.SetBossIntroState(true);
            SuspendReinforcements();
            RoarPresentation?.BeginPhase();
        }

        private void ReleaseCinematic()
        {
            RoarPresentation?.Cancel();
            if (player != null) player.IsBossCinematicProtected = false;
            session?.SetBossIntroState(false);
        }

        private void DetachBossEvents()
        {
            if (Boss == null) return;
            Boss.CercoStarted -= SuspendReinforcements;
            Boss.CercoCompleted -= ResumeReinforcements;
            Boss.PhaseTransitionStarted -= BeginPhaseRoar;
            Boss.PhaseTransitionCompleted -= ReleaseCinematic;
        }

        private bool EnsurePlayerClearance()
        {
            if (player == null || !player.IsAlive || Boss == null) return false;
            Vector3 delta = player.WorldCenter - Boss.WorldCenter;
            delta.y = 0f;
            float collisionRadius = Boss.HitRadius + player.HitRadius;
            if (delta.sqrMagnitude >= collisionRadius * collisionRadius) return true;
            Vector3 direction = delta.sqrMagnitude > 0.0001f ? delta.normalized
                : session != null && session.PlayerMotor != null ? session.PlayerMotor.FacingDirection : player.transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            direction.Normalize();
            float radius = collisionRadius + ClearanceMargin;
            float playerRadius = Mathf.Max(0.1f, player.HitRadius);
            float halfHeight = player.HurtboxCollider != null
                ? Mathf.Max(playerRadius, player.HurtboxCollider.bounds.extents.y) : 0.9f;
            for (int ring = 0; ring < 12; ring++)
            {
                for (int sample = 0; sample < 16; sample++)
                {
                    int side = sample % 2 == 0 ? 1 : -1;
                    float angle = ((sample + 1) / 2) * 22.5f * side;
                    Vector3 outward = Quaternion.AngleAxis(angle, Vector3.up) * direction;
                    Vector3 position = Boss.WorldCenter + outward * (radius + ring * 0.5f);
                    position.y = player.WorldCenter.y;
                    Vector3 halfSegment = Vector3.up * Mathf.Max(0f, halfHeight - playerRadius);
                    // A two-metre player rests exactly on y=0. Floor contact is not a blocked escape.
                    // Lift only the query slightly; the actual Rigidbody keeps its fixed gameplay height.
                    Vector3 queryCenter = position + Vector3.up * 0.03f;
                    if (Physics.CheckCapsule(queryCenter - halfSegment, queryCenter + halfSegment, playerRadius,
                        1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore))
                        continue;
                    if (playerBody != null)
                    {
                        playerBody.position = position;
                        if (!playerBody.isKinematic) playerBody.linearVelocity = Vector3.zero;
                    }
                    player.transform.position = position;
                    Physics.SyncTransforms();
                    return true;
                }
            }
            return false;
        }

        private void HandleActorDeath(ActorDeathEvent death)
        {
            if (!IsEncounterActive) return;
            if (death.IsPlayer)
            {
                EndEncounter();
                return;
            }
            if (!death.IsBoss || Boss == null || death.ActorId != Boss.TargetId || closingRewardResolved) return;
            closingRewardResolved = true;
            ClosingXpReward = ClosingXp;
            EndEncounter();
            deathPresentation?.Play(death.WorldPosition, player != null ? player.transform : null);
            ClosingXpAwarded?.Invoke(ClosingXpReward);
        }

        private void EndEncounter()
        {
            DetachBossEvents();
            arena?.EndEncounter();
            ReleaseCinematic();
            introInProgress = false;
            IntroRemaining = 0f;
            IsEncounterActive = false;
            IntroWaitingForClearance = false;
            director?.SetBossActive(false);
            director?.SuppressBossMinions(false);
            enemies?.DespawnAllActive();
            hazards?.ClearAll();
            if (session != null) MothExplosionPool.GetOrCreate(session, player).ClearAll();
        }

        public void ResetForSession()
        {
            DetachBossEvents();
            arena?.EndEncounter();
            ReleaseCinematic();
            if (Boss != null && Boss.gameObject.activeSelf)
            {
                if (enemies != null) enemies.ReturnToPool(Boss);
                else Boss.gameObject.SetActive(false);
            }
            Boss = null;
            IsEncounterActive = introInProgress = IntroWaitingForClearance = closingRewardResolved = false;
            IntroRemaining = 0f;
            ClosingXpReward = 0;
            director?.SetBossActive(false);
            director?.SuppressBossMinions(false);
            deathPresentation?.Clear();
        }
    }
}
