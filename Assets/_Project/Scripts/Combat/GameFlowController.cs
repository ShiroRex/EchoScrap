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
        private bool persist = true;
        private bool separateScenes;
        public bool IsLoading { get; private set; }
        public bool IsSelectingExpedition { get; private set; }
        public bool IsFadingExpeditionSelection { get; private set; }
        public CombatGameBootstrap Game => game;
        internal FusionStation Station => station;
        public EffectPreferences Effects => game.Session.Effects;
        private bool transientProfile;
        private bool collectionPersistence;
        private bool newCollectionConfirmation;
        public GameFlowState State { get; private set; }
        public bool IsConfirming { get; private set; }
        public bool IsConfiguring { get; private set; }
        public bool OwnsOverlay => State == GameFlowState.MainMenu || IsConfirming || IsSelectingExpedition || IsLoading;
        public bool HasCollection => persist
            ? PlayerPrefs.HasKey(ProfileKey) || PlayerPrefs.HasKey(FusionStation.SaveKey) : transientProfile;
        public GameMenuView View => view;
        public bool ShowEquipmentHint => station?.Progress is FusionProgress progress
            && progress.OwnedCount > 0 && !progress.hasEquippedCompanion;

        public void Initialize(CombatGameBootstrap owner, bool saveProgress = true, bool useSeparateScenes = false)
        {
            if (game != null) return;
            game = owner; persist = saveProgress; separateScenes = useSeparateScenes;
            travel = FindAnyObjectByType<WarehouseTravel>();
            station = FindAnyObjectByType<FusionStation>();
            if (travel == null || travel.WarehouseArrival == null || travel.CombatArrival == null)
            {
                Debug.LogError("El ciclo del juego necesita las dos llegadas de la bóveda.", this);
                enabled = false; return;
            }
            travel.Initialize(game);
            if (station != null)
            {
                collectionPersistence = persist && station.PersistProgress;
                // Opening and closing the title must not silently create a new collection.
                if (!HasCollection) station.PersistProgress = false;
            }
            game.Flow = this;
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
            if (separateScenes) SceneTransitionState.Apply(game);
            else ShowMainMenu();
        }

        public void CompleteSceneEntry(bool newCollection)
        {
            if (newCollection) CreateCollection();
            else
            {
                transientProfile = SceneTransitionState.HasTransientCollection;
                if (!persist && !string.IsNullOrEmpty(SceneTransitionState.FusionSnapshot)) station?.RestoreCollection(SceneTransitionState.FusionSnapshot);
                State = GameFlowState.Vault; Sync();
            }
        }

        private void Update()
        {
            if (IsLoading) return;
            bool cancel = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame
                || Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
            if (cancel && IsSelectingExpedition) CancelExpeditionSelection();
            else if (cancel && IsConfirming) CancelConfirmation();
            else if (cancel && IsConfiguring) CloseConfiguration();
        }

        public void RequestNewCollection()
        {
            if (State != GameFlowState.MainMenu || IsConfiguring || IsConfirming) return;
            newCollectionConfirmation = true;
            string message = HasCollection
                ? "Nombre nuevo: primera partida al ranking.\nSe reiniciarán tu colección y la fusión en curso."
                : "Nombre nuevo: primera partida al ranking.\nVacío o repetido: solo práctica.";
            Confirm("¿NUEVA COLECCIÓN?", message, true);
        }

        private void CreateCollection()
        {
            if (station != null) station.PersistProgress = collectionPersistence;
            station?.ResetCollection();
            transientProfile = true;
            if (persist) { PlayerPrefs.SetInt(ProfileKey, 1); PlayerPrefs.Save(); }
            ReturnToVault();
        }

        public void ContinueCollection()
        {
            if (State != GameFlowState.MainMenu || !HasCollection || IsConfirming || IsConfiguring) return;
            State = GameFlowState.Vault;
            game.Session.Clock.SetPause(PauseReason.FrontEnd, false);
            game.Session.ResumeAllUserPauses();
            Sync();
        }

        public void ShowMainMenu()
        {
            if (game == null || State == GameFlowState.Expedition || IsConfirming || IsLoading) return;
            if (separateScenes)
            {
                if (!Application.CanStreamedLevelBeLoaded("MainMenu")) { Debug.LogError("No se pudo cargar el menú inicial."); return; }
                SceneTransitionState.SaveProgress = persist;
                SceneTransitionState.Capture(game, station, HasCollection);
                IsLoading = true;
            }
            State = GameFlowState.MainMenu;
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
                game.ResetRun();
                EventLeaderboardSync.Instance?.BeginRankedExpedition(waves == entrySettings.CompleteRound);
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
            IsConfirming = IsConfiguring = IsSelectingExpedition = false;
            State = GameFlowState.Vault;
            game.ResetRun();
            travel.PlaceInWarehouse(true);
            Sync();
        }

        public void RequestAbandon()
        {
            if (State != GameFlowState.Expedition || !game.Hud.IsPauseVisible || IsConfirming) return;
            newCollectionConfirmation = false;
            Confirm("¿ABANDONAR EXPEDICIÓN?", "Perderás las mejoras de esta partida y volverás a la bóveda.\nTu colección se conserva.");
        }

        private void Confirm(string title, string message, bool askPlayerName = false)
        {
            IsConfirming = true;
            game.Session.Clock.SetPause(PauseReason.FrontEnd, true);
            Sync();
            view.ShowConfirmation(title, message, askPlayerName);
        }

        public void CancelConfirmation()
        {
            if (!IsConfirming) return;
            IsConfirming = false;
            if (State != GameFlowState.MainMenu) game.Session.Clock.SetPause(PauseReason.FrontEnd, false);
            Sync();
        }

        public void AcceptConfirmation()
        {
            if (!IsConfirming) return;
            IsConfirming = false;
            if (newCollectionConfirmation)
            {
                EventLeaderboardSync.StartNewPlayerSession(view.ConfirmedPlayerName);
                CreateCollection();
            }
            else ReturnToVault();
        }

        public void OpenConfiguration()
        {
            if (State != GameFlowState.MainMenu || IsConfirming) return;
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
            if (State != GameFlowState.MainMenu || IsConfirming || IsConfiguring) return;
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private void OnDestroy()
        {
            if (game == null) return;
            if (game.Session != null) game.Session.AllowManualPause = true;
            game.Flow = null;
        }
    }
}
