using System.Collections;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace EchoScrap.Combat
{
    /// <summary>Front end with no combat dependencies. The world loads only after a collection is chosen.</summary>
    public sealed class MainMenuController : MonoBehaviour, IGameMenuActions
    {
        public GameFlowState State => GameFlowState.MainMenu;
        public bool HasCollection => SceneTransitionState.HasCollection;
        public bool IsConfirming { get; private set; }
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
            View = gameObject.AddComponent<GameMenuView>();
            View.Build(this);
            Settings = gameObject.AddComponent<MenuSettingsView>();
            Settings.Build(View.Canvas.transform, this);
            View.Refresh();
        }

        private void Update()
        {
            if (IsLoading) return;
            bool cancel = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame
                || Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
            if (cancel && IsConfirming) CancelConfirmation();
            else if (cancel && IsConfiguring) CloseConfiguration();
        }

        public void ContinueCollection() { if (HasCollection) LoadWorld(SceneTransitionState.Entry.Continue); }
        public void RequestNewCollection()
        {
            if (IsLoading || IsConfirming || IsConfiguring) return;
            if (!HasCollection) { LoadWorld(SceneTransitionState.Entry.NewCollection); return; }
            IsConfirming = true; View.Refresh();
            View.ShowConfirmation("¿NUEVA COLECCIÓN?", "Se reiniciarán tu colección y la fusión en curso.\nEsta acción no se puede deshacer.");
        }
        public void CancelConfirmation() { IsConfirming = false; View.Refresh(); }
        public void AcceptConfirmation()
        {
            if (!IsConfirming || IsLoading) return;
            IsConfirming = false; LoadWorld(SceneTransitionState.Entry.NewCollection);
        }
        public void OpenConfiguration()
        {
            if (IsLoading || IsConfirming || IsConfiguring) return;
            IsConfiguring = true; View.Refresh(); Settings.Show();
        }
        public void CloseConfiguration() { IsConfiguring = false; Settings.Hide(); View.Refresh(); }
        public bool BeginExpedition() => false;
        public void ShowMainMenu() { if (!IsLoading) { IsConfiguring = IsConfirming = false; Settings.Hide(); View.Refresh(); } }
        public void QuitGame()
        {
            if (IsLoading || IsConfiguring || IsConfirming) return;
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
        private void LoadWorld(SceneTransitionState.Entry entry)
        {
            if (IsLoading || IsConfiguring || IsConfirming) return;
            if (!Application.CanStreamedLevelBeLoaded("CombatArena"))
            { View.SetStatus("No se pudo cargar el Vertedero."); return; }
            SceneTransitionState.PendingEntry = entry;
            IsLoading = true; View.Refresh(); View.SetStatus("CARGANDO EL VERTEDERO…");
            StartCoroutine(Load());
        }
        private IEnumerator Load()
        {
            // Show the loading label before Unity starts importing the world.
            yield return null;
            yield return SceneManager.LoadSceneAsync("CombatArena", LoadSceneMode.Single);
        }
    }
}
