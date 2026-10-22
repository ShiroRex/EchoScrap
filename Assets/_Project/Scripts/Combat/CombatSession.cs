using System;
using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Controlador de la sesión de combate.
    /// Es el único propietario del reloj de gameplay y las pausas (manual, táctica/draft, pérdida de foco y fin de sesión),
    /// mantiene las semillas separadas de combate y presentación, y resuelve el resultado tras cada paso de simulación
    /// garantizando que la derrota prevalezca si el jugador y Gólgora mueren en el mismo paso.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public sealed class CombatSession : MonoBehaviour
    {
        [SerializeField] private int initialSeed = 1337;
        [SerializeField] private bool requireExplicitResumeOnFocusLoss = true;
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private PlayerRuntimeStats playerStats;
        [SerializeField] private PlayerInputReader playerInput;
        [SerializeField] private PlayerMotor playerMotor;

        private GameplayClock clock;
        private SessionRng rng;
        private CombatEventBus events;
        private EffectPreferences effectPreferences;

        private SessionState state = SessionState.Running;
        private bool pendingPlayerDeath;
        private ActorDeathEvent pendingPlayerDeathEvent;
        private bool pendingBossDeath;
        private ActorDeathEvent pendingBossDeathEvent;
        private bool initialized;

        public GameplayClock Clock
        {
            get
            {
                EnsureInitialized();
                return clock;
            }
        }

        public SessionRng Rng
        {
            get
            {
                EnsureInitialized();
                return rng;
            }
        }

        public CombatEventBus Events
        {
            get
            {
                EnsureInitialized();
                return events;
            }
        }

        public EffectPreferences Effects
        {
            get
            {
                EnsureInitialized();
                return effectPreferences;
            }
        }

        public SessionState State => state;
        public bool IsPaused => Clock.IsPaused;
        public float GameplayTime => Clock.GameplayTime;
        public int CurrentSeed => Rng.BaseSeed;
        public PlayerHealth PlayerHealth => playerHealth;
        public PlayerRuntimeStats PlayerStats => playerStats;
        public PlayerMotor PlayerMotor => playerMotor;

        private void Awake()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (initialized)
                return;

            initialized = true;
            clock = new GameplayClock();
            rng = new SessionRng(initialSeed);
            events = new CombatEventBus();
            effectPreferences = new EffectPreferences();
            clock.OnPauseStateChanged += HandlePauseStateChanged;
            ResolvePlayerReferences();
        }

        public void ResolvePlayerReferences()
        {
            if (playerHealth == null)
                playerHealth = FindAnyObjectByType<PlayerHealth>();
            if (playerHealth != null)
            {
                if (playerStats == null)
                    playerStats = playerHealth.GetComponent<PlayerRuntimeStats>();
                if (playerInput == null)
                    playerInput = playerHealth.GetComponent<PlayerInputReader>();
                if (playerMotor == null)
                    playerMotor = playerHealth.GetComponent<PlayerMotor>();
                playerHealth.BindSession(this);
            }
            else if (playerMotor == null)
            {
                playerMotor = FindAnyObjectByType<PlayerMotor>();
                if (playerMotor != null && playerInput == null)
                    playerInput = playerMotor.GetComponent<PlayerInputReader>();
            }
        }

        private void OnEnable()
        {
            EnsureInitialized();
            ResolvePlayerReferences();
            if (playerStats != null)
                playerStats.OnStatsChanged += PublishSessionState;
            if (playerMotor != null)
                playerMotor.OnDashStateChanged += HandleMotorDashStateChanged;
            if (playerInput != null)
                playerInput.PauseRequested += ToggleManualPause;
        }

        private void OnDisable()
        {
            if (playerStats != null)
                playerStats.OnStatsChanged -= PublishSessionState;
            if (playerMotor != null)
                playerMotor.OnDashStateChanged -= HandleMotorDashStateChanged;
            if (playerInput != null)
                playerInput.PauseRequested -= ToggleManualPause;
            Time.timeScale = 1f;
        }

        private void OnDestroy()
        {
            if (clock != null)
                clock.OnPauseStateChanged -= HandlePauseStateChanged;
            events?.ClearAllListeners();
            Time.timeScale = 1f;
        }

        private void HandleMotorDashStateChanged(DashStateEvent dashEvent)
        {
            events?.PublishDash(in dashEvent);
        }

        private void FixedUpdate()
        {
            EnsureInitialized();
            if (!clock.IsPaused && IsGameplayFlowActive())
            {
                clock.AdvanceSimulationStep(Time.fixedDeltaTime);
            }
            ResolveSimulationStepOutcome();
        }

        private void Update()
        {
            EnsureInitialized();
            if (!clock.IsPaused && IsGameplayFlowActive())
            {
                clock.AdvanceGameplayTime(Time.deltaTime);
            }
            ResolveSimulationStepOutcome();
        }

        private void LateUpdate()
        {
            ResolveSimulationStepOutcome();
        }

        public bool IsGameplayFlowActive()
        {
            return state == SessionState.Running || state == SessionState.BossIntro;
        }

        public void SetManualPause(bool paused)
        {
            EnsureInitialized();
            if (state == SessionState.Victory || state == SessionState.Defeat)
                return;

            clock.SetPause(PauseReason.Manual, paused);
            RefreshHighLevelState();
        }

        public void ToggleManualPause()
        {
            SetManualPause(!clock.HasPauseReason(PauseReason.Manual));
        }

        public void SetTacticalDraftPause(bool active)
        {
            EnsureInitialized();
            if (state == SessionState.Victory || state == SessionState.Defeat)
                return;

            clock.SetPause(PauseReason.TacticalDraft, active);
            RefreshHighLevelState();
        }

        public void ConfirmDraftSelectionAndResume()
        {
            EnsureInitialized();
            if (playerInput != null)
                playerInput.SuppressDashFromUiConfirmation();
            SetTacticalDraftPause(false);
        }

        public void SimulateFocusChange(bool hasFocus)
        {
            EnsureInitialized();
            if (!hasFocus)
            {
                if (state != SessionState.Victory && state != SessionState.Defeat)
                {
                    clock.SetPause(PauseReason.FocusLost, true);
                    RefreshHighLevelState();
                }
            }
            else if (!requireExplicitResumeOnFocusLoss)
            {
                clock.SetPause(PauseReason.FocusLost, false);
                RefreshHighLevelState();
            }
        }

        public void ResumeFromFocusLoss()
        {
            EnsureInitialized();
            if (playerInput != null)
                playerInput.SuppressDashFromUiConfirmation();
            clock.SetPause(PauseReason.FocusLost, false);
            RefreshHighLevelState();
        }

        public void ResumeAllUserPauses()
        {
            EnsureInitialized();
            if (playerInput != null)
                playerInput.SuppressDashFromUiConfirmation();
            clock.SetPause(PauseReason.Manual, false);
            clock.SetPause(PauseReason.FocusLost, false);
            RefreshHighLevelState();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            SimulateFocusChange(hasFocus);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
                SimulateFocusChange(false);
        }

        public void QueuePlayerDeath(in ActorDeathEvent deathEvent)
        {
            EnsureInitialized();
            pendingPlayerDeath = true;
            pendingPlayerDeathEvent = deathEvent;
        }

        public void QueueBossDeath(in ActorDeathEvent deathEvent)
        {
            EnsureInitialized();
            pendingBossDeath = true;
            pendingBossDeathEvent = deathEvent;
        }

        /// <summary>
        /// Resuelve el resultado de la sesión tras procesar todos los daños del paso actual.
        /// Si el jugador y Gólgora mueren en el mismo paso, prevalece siempre la derrota.
        /// </summary>
        public void ResolveSimulationStepOutcome()
        {
            if (!pendingPlayerDeath && !pendingBossDeath)
                return;

            if (state == SessionState.Victory || state == SessionState.Defeat)
            {
                pendingPlayerDeath = false;
                pendingBossDeath = false;
                return;
            }

            if (pendingPlayerDeath)
            {
                pendingPlayerDeath = false;
                pendingBossDeath = false;
                state = SessionState.Defeat;
                clock.SetPause(PauseReason.SessionEnded, true);
                events.PublishDeath(pendingPlayerDeathEvent);
                PublishSessionState();
                return;
            }

            if (pendingBossDeath)
            {
                pendingBossDeath = false;
                state = SessionState.Victory;
                events.PublishDeath(pendingBossDeathEvent);
                PublishSessionState();
            }
        }

        public void SetBossIntroState(bool inIntro)
        {
            if (state == SessionState.Victory || state == SessionState.Defeat)
                return;

            state = inIntro ? SessionState.BossIntro : SessionState.Running;
            RefreshHighLevelState();
        }

        public void ResetSession(int? newSeed = null)
        {
            EnsureInitialized();
            if (newSeed.HasValue)
                initialSeed = newSeed.Value;

            pendingPlayerDeath = false;
            pendingBossDeath = false;
            state = SessionState.Running;
            rng.Reset(initialSeed);
            clock.Reset();

            ResolvePlayerReferences();
            if (playerStats != null)
                playerStats.ResetToInitial();
            if (playerHealth != null)
                playerHealth.ResetForNewSession();
            if (playerMotor != null)
            {
                playerMotor.enabled = true;
                playerMotor.ResetDashForSession();
                playerMotor.Stop();
            }
            if (playerInput != null)
            {
                playerInput.enabled = true;
                playerInput.SetInputEnabled(true);
            }

            PublishSessionState();
        }

        private void RefreshHighLevelState()
        {
            if (state == SessionState.Victory || state == SessionState.Defeat)
                return;

            if (clock.HasPauseReason(PauseReason.TacticalDraft))
                state = SessionState.TacticalDraft;
            else if (clock.HasPauseReason(PauseReason.Manual) || clock.HasPauseReason(PauseReason.FocusLost))
                state = SessionState.Paused;
            else if (state == SessionState.Paused || state == SessionState.TacticalDraft)
                state = SessionState.Running;

            if (playerInput != null)
            {
                bool allowGameplayInput = !clock.IsPaused && (state == SessionState.Running || state == SessionState.BossIntro);
                playerInput.SetInputEnabled(allowGameplayInput);
            }

            PublishSessionState();
        }

        private void HandlePauseStateChanged(PauseReason reasons, bool paused)
        {
            RefreshHighLevelState();
        }

        public void PublishSessionState()
        {
            EnsureInitialized();
            float moveSpeed = playerMotor != null && playerMotor.Settings != null ? playerMotor.Settings.MoveSpeed : 8f;
            float currentHp = playerHealth != null ? playerHealth.CurrentHealth : 100f;
            PlayerStatsSnapshot snapshot = playerStats != null
                ? playerStats.CreateSnapshot(currentHp, moveSpeed)
                : default;

            events.PublishSessionState(new SessionStateEvent(state, clock.ActiveReasons, clock.GameplayTime, snapshot));
        }
    }
}
