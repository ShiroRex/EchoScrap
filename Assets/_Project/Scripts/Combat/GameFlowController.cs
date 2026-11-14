using System.Collections;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EchoScrap.Combat
{
    public enum GameFlowState { MainMenu, Vault, Expedition }

    /// <summary>Connects the title, existing physical vault, expeditions and results without reloading actors.</summary>
    [DisallowMultipleComponent]
    public sealed class GameFlowController : MonoBehaviour, IGameMenuActions
    {
        public const string ProfileKey = "EchoScrap.CollectionStarted.v1";
        private CombatGameBootstrap game;
        private WarehouseTravel travel;
        private FusionStation station;
        private GameMenuView view;
        private ExpeditionIntro entrance;
        private ExpeditionEntrySettings entrySettings;
        private AudioSource selectionAudio;
        private MenuSlotCoordinator slots;
        private bool persist = true;
        private bool separateScenes;
        private float vaultSaveClock;
        private float expeditionSaveClock;
        private bool pendingStepSave;

        public bool IsLoading { get; private set; }
        public bool IsSelectingExpedition { get; private set; }
        public bool IsFadingExpeditionSelection { get; private set; }
        public CombatGameBootstrap Game => game;
        internal FusionStation Station => station;
        public EffectPreferences Effects => game.Session.Effects;
        public GameFlowState State { get; private set; }
        public bool IsSlotSelectorOpen => slots != null && slots.IsSlotSelectorOpen;
        public SlotSelectorMode SlotSelectionMode => slots != null ? slots.Mode : SlotSelectorMode.None;
        public bool IsConfirming => slots != null && slots.IsConfirming;
        public bool IsConfiguring { get; private set; }
        public bool OwnsOverlay => State == GameFlowState.MainMenu || IsConfirming || IsSlotSelectorOpen || IsSelectingExpedition || IsLoading;
        public bool HasCollection => SaveSlotService.HasAnyContinuableOrRecoverableSlot();
        public GameMenuView View => view;
        public bool ShowEquipmentHint => station?.Progress is FusionProgress progress
            && progress.OwnedCount > 0 && !progress.hasEquippedCompanion;

        public void Initialize(CombatGameBootstrap owner, bool saveProgress = true, bool useSeparateScenes = false)
        {
            if (game != null) return;
            game = owner;
            persist = saveProgress;
            separateScenes = useSeparateScenes;
            if (!persist) SaveSlotService.SetPersistenceOverride(false);

            travel = FindAnyObjectByType<WarehouseTravel>();
            station = FindAnyObjectByType<FusionStation>();
            if (travel == null || travel.WarehouseArrival == null || travel.CombatArrival == null)
            {
                Debug.LogError("El ciclo del juego necesita las dos llegadas de la bóveda.", this);
                enabled = false; return;
            }
            travel.Initialize(game);
            game.Flow = this;
            game.Session.Events.OnSessionStateChanged += HandleSessionStateChanged;
            game.Session.Clock.OnPauseStateChanged += HandlePauseStateChanged;
            slots = new MenuSlotCoordinator(
                () => view,
                Sync,
                () => true,
                EnterLoadedSlot,
                HandleAbandonAccepted);

            view = gameObject.AddComponent<GameMenuView>();
            view.Build(this, !separateScenes);
            entrance = Instantiate(Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro"), transform);
            entrance.Bind(game);
            entrySettings = Resources.Load<ExpeditionEntrySettings>("UI/ExpeditionEntrySettings");
            selectionAudio = gameObject.AddComponent<AudioSource>();
            selectionAudio.playOnAwake = false;
            selectionAudio.spatialBlend = 0f;
            selectionAudio.clip = entrySettings.SelectionRoar;
            selectionAudio.volume = entrySettings.RoarVolume;

            ReturnToVault();
            if (separateScenes)
            {
                SceneTransitionState.Apply(game);
            }
            else
            {
                SaveSlotService.EnsureMigrated();
                ShowMainMenu();
            }
        }

        public void CompleteSceneEntry(bool newCollection)
        {
            if (!SaveSlotService.HasLoadedProfile)
                SaveSlotService.EnsureLoadedFallbackForDirectArenaEntry();
            station?.ReloadFromActiveSlot();
            EnterLoadedSlot(newCollection ? SceneTransitionState.Entry.NewCollection : SceneTransitionState.Entry.Continue, SaveSlotService.ActiveSlotIndex);
        }

        internal CombatWaveSettings ResolveWaveSettings(int modeMinutes)
        {
            if (entrySettings == null) return game != null ? game.WaveSettings : null;
            return modeMinutes == 8 && entrySettings.CompleteRound != null
                ? entrySettings.CompleteRound
                : entrySettings.DemoRound != null ? entrySettings.DemoRound : game?.WaveSettings;
        }

        private void EnterLoadedSlot(SceneTransitionState.Entry entry, int slotIndex)
        {
            if (entry != SceneTransitionState.Entry.NewCollection && SaveSlotService.HasPendingExpeditionSnapshot &&
                ExpeditionSnapshotService.TryRestoreActiveExpedition(
                    game, station, travel, ResolveWaveSettings, out _, keepFrontEndPause: true))
            {
                IsConfiguring = IsSelectingExpedition = false;
                slots?.Reset();
                State = GameFlowState.Expedition;
                vaultSaveClock = 0f;
                expeditionSaveClock = 0f;
                pendingStepSave = false;
                SaveSlotService.ClearPendingExpeditionAfterRestore();
                SaveSlotService.MarkSlotLoadCompleted();
                EventLeaderboardSync.SyncSessionNameFromActiveSlot();
                Sync();
                StartCoroutine(ResumeRestoredExpeditionWhenHudReady());
                return;
            }

            station?.ReloadFromActiveSlot();
            ReturnToVault();
            game.Session.Clock.SetPause(PauseReason.FrontEnd, false);
            game.Session.ResumeAllUserPauses();
            SaveSlotService.MarkSlotLoadCompleted();
            EventLeaderboardSync.SyncSessionNameFromActiveSlot();
            Sync();
        }

        private IEnumerator ResumeRestoredExpeditionWhenHudReady()
        {
            while (LoadingScreen.IsLoading) yield return null;
            if (game == null || game.Session == null || State != GameFlowState.Expedition) yield break;
            Sync();
            Canvas.ForceUpdateCanvases();
            game.Session.Clock.SetPause(PauseReason.FrontEnd, false);
            game.Motor?.GetComponent<EchoScrap.Player.PlayerInputReader>()?.SuppressDashFromUiConfirmation();
        }

        private void Update()
        {
            if (IsLoading) return;

            if (!LoadingScreen.IsLoading && (State == GameFlowState.Vault || State == GameFlowState.Expedition) &&
                !game.Session.IsPaused && !OwnsOverlay && !IsConfiguring)
            {
                SaveSlotService.RecordActivePlaytime(Time.unscaledDeltaTime);
                if (State == GameFlowState.Vault && SaveSlotService.CanWriteActiveProfile)
                {
                    vaultSaveClock += Time.unscaledDeltaTime;
                    if (vaultSaveClock >= 5f)
                    {
                        vaultSaveClock = 0f;
                        SaveSlotService.SaveActiveFusionProgress(station?.Progress);
                    }
                }
                else if (State == GameFlowState.Expedition && SaveSlotService.CanWriteActiveProfile &&
                    (game.Session.State == SessionState.Running || game.Session.State == SessionState.BossIntro))
                {
                    expeditionSaveClock += Time.unscaledDeltaTime;
                    if (expeditionSaveClock >= ExpeditionSnapshotService.AutosaveIntervalSeconds)
                    {
                        expeditionSaveClock = 0f;
                        pendingStepSave = true;
                    }
                }
            }

            bool cancel = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame
                || Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
            if (cancel && IsSelectingExpedition) CancelExpeditionSelection();
            else if (cancel && IsConfirming) CancelConfirmation();
            else if (cancel && IsSlotSelectorOpen) CloseSlotSelector();
            else if (cancel && IsConfiguring) CloseConfiguration();
        }

        private void LateUpdate()
        {
            if (!pendingStepSave) return;
            pendingStepSave = false;
            if (State == GameFlowState.Expedition && SaveSlotService.CanWriteActiveProfile &&
                !ExpeditionSnapshotService.IsRestoring &&
                game != null && game.Session != null && game.Player != null && game.Player.IsAlive &&
                game.Session.State != SessionState.Victory && game.Session.State != SessionState.Defeat)
            {
                ExpeditionSnapshotService.CaptureAndSave(game, station);
            }
        }

        public void RequestNewCollection()
        {
            if (State != GameFlowState.MainMenu || IsConfiguring || IsConfirming || IsSlotSelectorOpen) return;
            slots.OpenNewGame();
        }

        public void ContinueCollection()
        {
            if (State != GameFlowState.MainMenu || !HasCollection || IsConfirming || IsConfiguring || IsSlotSelectorOpen) return;
            slots.OpenContinue();
        }

        public void CloseSlotSelector()
        {
            if (State != GameFlowState.MainMenu || IsConfiguring || IsConfirming) return;
            slots.CloseSelector();
        }

        public void SelectSaveSlot(int slotIndex)
        {
            if (State != GameFlowState.MainMenu || IsConfiguring || IsConfirming) return;
            slots.SelectSlot(slotIndex);
        }

        public int GetDefaultSlotFocus(SlotSelectorMode mode)
        {
            if (slots != null && slots.Mode == mode && slots.SelectedSlotIndex >= 0)
                return slots.SelectedSlotIndex;
            return SaveSlotService.GetDefaultSlotFocus(mode);
        }

        public void ShowMainMenu()
        {
            if (game == null || State == GameFlowState.Expedition || IsConfirming || IsLoading) return;
            SaveSlotService.FlushAndSuspendActiveProfile(station?.Progress);
            vaultSaveClock = 0f;
            if (separateScenes)
            {
                if (!Application.CanStreamedLevelBeLoaded("MainMenu")) { Debug.LogError("No se pudo cargar el menú inicial."); return; }
                SceneTransitionState.SaveProgress = persist;
                SceneTransitionState.Capture(game, station, HasCollection);
                IsLoading = true;
            }
            State = GameFlowState.MainMenu;
            slots?.Reset();
            game.Motor.Stop();
            game.Session.Clock.SetPause(PauseReason.FrontEnd, true);
            station?.CloseMenu();
            game.Session.ResumeAllUserPauses();
            Sync();
            if (separateScenes) LoadingScreen.LoadScene("MainMenu");
        }

        public bool BeginExpedition()
        {
            if (State != GameFlowState.Vault || game.Session.IsPaused || OwnsOverlay || IsConfiguring) return false;
            IsSelectingExpedition = true;
            game.Motor.CancelDashAndRestoreState();
            game.Motor.Stop();
            game.Session.Clock.SetPause(PauseReason.FrontEnd, true);
            Sync();
            return true;
        }

        public void SelectExpedition(bool completeRound)
        {
            if (!IsSelectingExpedition || IsLoading) return;
            IsSelectingExpedition = false;
            IsLoading = true;
            IsFadingExpeditionSelection = true;
            selectionAudio.Play();
            Sync();
            view.PressExpeditionSelection(completeRound);
            StartCoroutine(EnterArena(completeRound ? entrySettings.CompleteRound : entrySettings.DemoRound));
        }

        private IEnumerator EnterArena(CombatWaveSettings waves)
        {
            float duration = selectionAudio.clip.length / Mathf.Max(.01f, Mathf.Abs(selectionAudio.pitch));
            float started = Time.unscaledTime;
            float elapsed;
            do
            {
                elapsed = Time.unscaledTime - started;
                view.AnimateExpeditionSelectionExit(
                    Mathf.InverseLerp(entrySettings.ButtonPressDuration, duration, elapsed),
                    Mathf.Clamp01(elapsed / entrySettings.ButtonPressDuration));
                yield return null;
            }
            while (elapsed < duration || selectionAudio.isPlaying);
            view.AnimateExpeditionSelectionExit(1f, 1f);
            IsFadingExpeditionSelection = false;
            Sync();
            yield return LoadingScreen.Transition(entrySettings.TransitionDuration, () =>
            {
                game.SelectWaveSettings(waves);
                State = GameFlowState.Expedition;
                expeditionSaveClock = 0f;
                pendingStepSave = false;
                game.ResetRun();
                EventLeaderboardSync.Instance?.BeginRankedExpedition(waves == entrySettings.CompleteRound);
                ExpeditionSnapshotService.CaptureAndSave(game, station, forceImmediate: true);
                IsLoading = false;
                Sync();
                entrance.Play();
            });
        }

        public void CancelExpeditionSelection()
        {
            if (!IsSelectingExpedition || IsLoading) return;
            IsSelectingExpedition = false;
            game.Motor.GetComponent<EchoScrap.Player.PlayerInputReader>()?.SuppressDashFromUiConfirmation();
            game.Session.Clock.SetPause(PauseReason.FrontEnd, false);
            Sync();
        }

        public void ReturnFromResult()
        {
            if (game.Session.State != SessionState.Victory && game.Session.State != SessionState.Defeat) return;
            ReturnToVault();
        }

        private void ReturnToVault()
        {
            IsConfiguring = IsSelectingExpedition = false;
            slots?.Reset();
            State = GameFlowState.Vault;
            vaultSaveClock = 0f;
            expeditionSaveClock = 0f;
            pendingStepSave = false;
            game.ResetRun();
            travel.PlaceInWarehouse(true);
            Sync();
        }

        public void RequestAbandon()
        {
            if (State != GameFlowState.Expedition || !game.Hud.IsPauseVisible || IsConfirming) return;
            game.Session.Clock.SetPause(PauseReason.FrontEnd, true);
            slots.OpenAbandonConfirmation("¿ABANDONAR EXPEDICIÓN?",
                "Perderás las mejoras de esta partida y volverás a la bóveda.\nTu colección se conserva.");
        }

        private void HandleAbandonAccepted()
        {
            pendingStepSave = false;
            expeditionSaveClock = 0f;
            EventLeaderboardSync.Instance?.AbandonActiveAttempt();
            if (SaveSlotService.CanWriteActiveProfile)
                SaveSlotService.ClearActiveExpeditionState(station?.Progress);
            ReturnToVault();
        }

        public void CancelConfirmation()
        {
            if (!IsConfirming || view.IsValidatingName) return;
            slots.CancelConfirmation();
            if (State != GameFlowState.MainMenu) game.Session.Clock.SetPause(PauseReason.FrontEnd, false);
            Sync();
        }

        public void AcceptConfirmation()
        {
            if (!IsConfirming) return;
            slots.AcceptConfirmation();
        }

        public void OpenConfiguration()
        {
            if (State != GameFlowState.MainMenu || IsConfirming || IsSlotSelectorOpen) return;
            IsConfiguring = true;
            game.Session.SetManualPause(true);
            Sync();
        }

        public void CloseConfiguration()
        {
            if (!IsConfiguring) return;
            IsConfiguring = false;
            game.Session.SetManualPause(false);
            Sync();
        }

        private void Sync()
        {
            game.Session.AllowManualPause = !OwnsOverlay && !IsConfiguring;
            game.Hud.SetHubMode(State != GameFlowState.Expedition);
            game.Hud.RefreshNow();
            view.Refresh();
        }

        public void QuitGame()
        {
            if (State != GameFlowState.MainMenu || IsConfirming || IsConfiguring || IsSlotSelectorOpen) return;
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private void HandleSessionStateChanged(SessionStateEvent change)
        {
            if (State == GameFlowState.Expedition &&
                (change.State == SessionState.Victory || change.State == SessionState.Defeat))
            {
                pendingStepSave = false;
                expeditionSaveClock = 0f;
                if (SaveSlotService.CanWriteActiveProfile)
                    SaveSlotService.ClearActiveExpeditionState(station?.Progress);
            }
        }

        private void HandlePauseStateChanged(PauseReason reasons, bool paused)
        {
            if (paused && State == GameFlowState.Expedition && !ExpeditionSnapshotService.IsRestoring &&
                (reasons & PauseReason.Manual) != PauseReason.None && SaveSlotService.CanWriteActiveProfile)
            {
                ExpeditionSnapshotService.CaptureAndSave(game, station, forceImmediate: true);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused || !SaveSlotService.CanWriteActiveProfile || State == GameFlowState.MainMenu) return;
            if (State == GameFlowState.Expedition)
                ExpeditionSnapshotService.CaptureAndSave(game, station, forceImmediate: true);
            else
                SaveSlotService.SaveActiveFusionProgress(station?.Progress);
        }

        private void OnApplicationQuit()
        {
            if (!SaveSlotService.CanWriteActiveProfile || State == GameFlowState.MainMenu) return;
            if (State == GameFlowState.Expedition)
                ExpeditionSnapshotService.CaptureAndSave(game, station, forceImmediate: true);
            else
                SaveSlotService.SaveActiveFusionProgress(station?.Progress);
        }

        private void OnDestroy()
        {
            if (game == null) return;
            if (game.Session != null)
            {
                game.Session.Events.OnSessionStateChanged -= HandleSessionStateChanged;
                game.Session.Clock.OnPauseStateChanged -= HandlePauseStateChanged;
                game.Session.AllowManualPause = true;
            }
            game.Flow = null;
        }
    }
}
