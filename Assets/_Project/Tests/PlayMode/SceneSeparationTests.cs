using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class SceneSeparationTests
    {
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
        private InputDevice device;
        private MainMenuController Menu => Object.FindAnyObjectByType<MainMenuController>();
        private CombatGameBootstrap Game => Object.FindAnyObjectByType<CombatGameBootstrap>();
        [UnitySetUp] public IEnumerator SetUp()
        {
            SceneTransitionState.Reset(); SceneTransitionState.SaveProgress = false;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Time.timeScale = 1;
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
            Assert.IsNotNull(Menu);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (device != null) InputSystem.RemoveDevice(device);
            var scene = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("SceneSeparationCleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);
            SceneTransitionState.Reset(); CombatTargetRegistry.Clear(); Time.timeScale = 1;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
        }
        private IEnumerator World()
        {
            float deadline = Time.realtimeSinceStartup + 30;
            while ((Game == null || Game.Flow == null) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsNotNull(Game); Assert.IsNotNull(Game.Flow);
            Game.Director.AutoTick = Game.Encounter.AutoTick = false;
            Game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
        }
        private IEnumerator Title()
        {
            float deadline = Time.realtimeSinceStartup + 30;
            while (Menu == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsNotNull(Menu); yield return null;
        }
        private IEnumerator NewCollection()
        { Menu.View.NewCollectionButton.onClick.Invoke(); yield return World(); }

        [UnityTest] public IEnumerator IdleTitleDoesNotConstructWorldPlayerPoolsOrFusion()
        {
            yield return new WaitForSecondsRealtime(.25f);
            Assert.IsNull(Game);
            Assert.IsNull(Object.FindAnyObjectByType<CombatSession>());
            Assert.IsNull(Object.FindAnyObjectByType<EnemyPool>());
            Assert.IsNull(Object.FindAnyObjectByType<FusionStation>());
            Assert.IsNull(Object.FindAnyObjectByType<PlayerMotor>());
            Assert.IsTrue(Menu.View.IsTitleVisible);
            Assert.IsFalse(Menu.View.ContinueButton.interactable);
            Assert.AreEqual("MainMenu", SceneManager.GetActiveScene().name);
            Assert.AreEqual(1, Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length);
        }
        [UnityTest] public IEnumerator NewCollectionLoadsOnlyWorldAndEntersVaultWithoutTitleAssets()
        {
            yield return NewCollection();
            Assert.IsNull(Menu);
            Assert.AreEqual("CombatArena", SceneManager.GetActiveScene().name);
            Assert.IsFalse(SceneManager.GetSceneByName("MainMenu").isLoaded);
            Assert.AreEqual(GameFlowState.Vault, Game.Flow.State);
            Assert.IsTrue(Game.Session.IsInWarehouse);
            Assert.AreEqual(0, Game.Session.GameplayTime);
            Assert.IsFalse(Game.Flow.View.IsTitleVisible);
            Assert.IsEmpty(Object.FindObjectsByType<MenuBackdrop>(FindObjectsSortMode.None));
            Assert.IsEmpty(Object.FindObjectsByType<LavaHazard>(FindObjectsSortMode.None));
            Assert.AreEqual(1, Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length);
        }
        [UnityTest] public IEnumerator ReturningToTitleUnloadsWorldAndContinuePreservesCollectionAndFusion()
        {
            yield return NewCollection();
            var station = Object.FindAnyObjectByType<FusionStation>();
            station.Progress.owned[4] = 6;
            Assert.IsTrue(station.Progress.Begin(new[] { 0, 0, 0, 0 }, 1234, 0));
            station.Advance(12); float remaining = station.Progress.remaining; int result = station.Progress.result;
            Game.Flow.ShowMainMenu(); yield return Title();
            Assert.IsNull(Game); Assert.IsNull(Object.FindAnyObjectByType<FusionStation>());
            Assert.IsTrue(Menu.HasCollection); Assert.IsTrue(Menu.View.ContinueButton.interactable);
            Menu.ContinueCollection(); yield return World();
            station = Object.FindAnyObjectByType<FusionStation>();
            Assert.AreEqual(6, station.Progress.owned[4]); Assert.AreEqual(result, station.Progress.result);
            Assert.AreEqual(FusionPhase.Fusing, station.Progress.phase);
            Assert.That(station.Progress.remaining, Is.InRange(remaining - .5f, remaining));
            Assert.AreEqual(GameFlowState.Vault, Game.Flow.State);
        }
        [UnityTest] public IEnumerator NewCollectionConfirmationPreservesOldDataUntilAccepted()
        {
            yield return NewCollection(); Object.FindAnyObjectByType<FusionStation>().Progress.owned[4] = 8;
            Game.Flow.ShowMainMenu(); yield return Title();
            Menu.RequestNewCollection(); Assert.IsTrue(Menu.IsConfirming);
            Menu.CancelConfirmation(); Menu.ContinueCollection(); yield return World();
            Assert.AreEqual(8, Object.FindAnyObjectByType<FusionStation>().Progress.owned[4]);
            Game.Flow.ShowMainMenu(); yield return Title();
            Menu.RequestNewCollection(); Menu.AcceptConfirmation(); yield return World();
            Assert.AreEqual(0, Object.FindAnyObjectByType<FusionStation>().Progress.owned[4]);
        }
        [UnityTest] public IEnumerator SettingsWorkWithoutArenaAndSurviveBothSceneDirections()
        {
            Menu.OpenConfiguration(); Assert.IsTrue(Menu.IsConfiguring); Assert.IsNull(Game);
            Menu.Settings.SetShakePercent(35); Menu.Settings.SetDecorativePercent(45);
            Canvas.ForceUpdateCanvases();
            foreach (var slider in Menu.Settings.GetComponentsInChildren<UnityEngine.UI.Slider>())
                Assert.AreEqual(slider.normalizedValue * ((RectTransform)slider.fillRect.parent).rect.width, slider.fillRect.rect.width, .1f, "Slider fill must fit its track at the actual percentage.");
            Menu.Settings.SetStatsMode(StatsDisplayMode.Reduced); Menu.Settings.SetReducedEffects(true);
            Menu.CloseConfiguration(); yield return NewCollection();
            Assert.AreEqual(.35f, Game.Session.Effects.RawShakeIntensity, .001f);
            Assert.AreEqual(.45f, Game.Session.Effects.RawDecorativeIntensity, .001f);
            Assert.IsTrue(Game.Session.Effects.ReducedEffectsMode); Assert.AreEqual(StatsDisplayMode.Reduced, Game.Hud.StatsMode);
            Game.Hud.SetShakePercent(75); Game.Hud.SetReducedEffects(false);
            Game.Flow.ShowMainMenu(); yield return Title();
            Assert.AreEqual(.75f, Menu.Effects.RawShakeIntensity, .001f); Assert.IsFalse(Menu.Effects.ReducedEffectsMode);
        }
        [UnityTest] public IEnumerator RepeatedExpeditionsStayInSameWorldAndResultsReturnToVault()
        {
            yield return NewCollection(); var original = Game; var station = Object.FindAnyObjectByType<FusionStation>(); station.Progress.owned[4] = 2;
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(Game.Flow.BeginExpedition());
                Game.Session.SetManualPause(true); Game.Hud.RefreshNow(); Game.Flow.RequestAbandon(); Game.Flow.AcceptConfirmation();
                Assert.AreSame(original, Game); Assert.AreEqual(GameFlowState.Vault, Game.Flow.State);
            }
            Assert.AreEqual(2, station.Progress.owned[4]); Assert.AreEqual(0, Game.Session.GameplayTime);
            Assert.IsEmpty(Object.FindObjectsByType<MenuBackdrop>(FindObjectsSortMode.None)); yield return null;
        }
        [UnityTest] public IEnumerator GamepadSubmitCannotCauseDashAfterSceneTransition()
        {
            var pad = InputSystem.AddDevice<Gamepad>(); device = pad;
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
            yield return null; yield return World();
            Assert.IsFalse(Game.Motor.IsDashing);
            Assert.IsFalse(Game.Motor.GetComponent<PlayerInputReader>().IsDashQueued);
            Assert.IsNull(EventSystem.current.currentSelectedGameObject);
            InputSystem.QueueStateEvent(pad, new GamepadState());
        }
    }
}
