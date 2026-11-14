using EchoScrap.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace EchoScrap.Combat
{
    /// <summary>Front end with no combat dependencies. The world loads only after a save slot is chosen.</summary>
    public sealed class MainMenuController : MonoBehaviour, IGameMenuActions
    {
        private MenuSlotCoordinator slots;

        public GameFlowState State => GameFlowState.MainMenu;
        public bool HasCollection => SaveSlotService.HasAnyContinuableOrRecoverableSlot();
        public bool IsSlotSelectorOpen => slots != null && slots.IsSlotSelectorOpen;
        public SlotSelectorMode SlotSelectionMode => slots != null ? slots.Mode : SlotSelectorMode.None;
        public bool IsConfirming => slots != null && slots.IsConfirming;
        public bool IsConfiguring { get; private set; }
        public bool IsLoading { get; private set; }
        public EffectPreferences Effects => SceneTransitionState.Effects;
        public GameMenuView View { get; private set; }
        public MenuSettingsView Settings { get; private set; }

        public void Initialize()
        {
            if (View != null) return;
            Time.timeScale = 1f;
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("MenuEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            slots = new MenuSlotCoordinator(
                () => View,
                () => View?.Refresh(),
                () => Application.CanStreamedLevelBeLoaded("CombatArena"),
                LoadWorldForSlot);
            View = gameObject.AddComponent<GameMenuView>();
            View.Build(this);
            Settings = MenuSettingsView.CreateForMainMenu((RectTransform)View.Canvas.transform, this);
            SaveSlotService.EnsureMigrated();
            View.Refresh();
            MenuMusic.Play();
        }

        private void Update()
        {
            if (IsLoading) return;
            bool cancel = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame
                || Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
            if (cancel && IsConfirming) CancelConfirmation();
            else if (cancel && IsSlotSelectorOpen) CloseSlotSelector();
            else if (cancel && IsConfiguring) CloseConfiguration();
        }

        public void ContinueCollection()
        {
            if (IsLoading || IsConfirming || IsConfiguring || IsSlotSelectorOpen) return;
            slots.OpenContinue();
        }

        public void RequestNewCollection()
        {
            if (IsLoading || IsConfirming || IsConfiguring || IsSlotSelectorOpen) return;
            slots.OpenNewGame();
        }

        public void CloseSlotSelector()
        {
            if (IsLoading || IsConfirming || IsConfiguring) return;
            slots.CloseSelector();
        }

        public void SelectSaveSlot(int slotIndex)
        {
            if (IsLoading || IsConfirming || IsConfiguring) return;
            slots.SelectSlot(slotIndex);
        }

        public int GetDefaultSlotFocus(SlotSelectorMode mode)
        {
            if (slots != null && slots.Mode == mode && slots.SelectedSlotIndex >= 0)
                return slots.SelectedSlotIndex;
            return SaveSlotService.GetDefaultSlotFocus(mode);
        }

        public void CancelConfirmation() => slots?.CancelConfirmation();
        public void AcceptConfirmation() => slots?.AcceptConfirmation();

        public void OpenConfiguration()
        {
            if (IsLoading || IsConfirming || IsConfiguring || IsSlotSelectorOpen) return;
            IsConfiguring = true; View.Refresh(); Settings.Show();
        }

        public void CloseConfiguration()
        {
            IsConfiguring = false;
            Settings.Hide();
            PlayerPrefs.Save();
            View.Refresh();
        }

        public bool BeginExpedition() => false;

        public void ShowMainMenu()
        {
            if (!IsLoading)
            {
                IsConfiguring = false;
                slots?.Reset();
                Settings.Hide();
                View.Refresh();
            }
        }

        public void QuitGame()
        {
            if (IsLoading || IsConfiguring || IsConfirming || IsSlotSelectorOpen) return;
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private void LoadWorldForSlot(SceneTransitionState.Entry entry, int slotIndex)
        {
            if (IsLoading || IsConfiguring) return;
            if (!Application.CanStreamedLevelBeLoaded("CombatArena"))
            {
                View.SetStatus("No se pudo cargar el Vertedero.");
                return;
            }
            SceneTransitionState.PendingEntry = entry;
            SceneTransitionState.PendingSlotIndex = slotIndex;
            IsLoading = true;
            View.Refresh();
            LoadingScreen.LoadScene("CombatArena");
        }
    }
}
