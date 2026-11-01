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
using UnityEngine.UI;

namespace EchoScrap.Tests
{
    public sealed class ExpeditionSelectionTests
    {
        private CombatGameBootstrap game;
        private GameFlowController flow;
        private InputDevice device;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private ExpeditionIntro Intro => Object.FindAnyObjectByType<ExpeditionIntro>();

        private void Loaded(Scene scene, LoadSceneMode mode)
        {
            var station = Object.FindAnyObjectByType<FusionStation>();
            if (station != null) station.PersistProgress = false;
        }

        [UnitySetUp] public IEnumerator SetUp()
        {
            SceneTransitionState.Reset();
            SceneManager.sceneLoaded += Loaded;
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = game.Encounter.AutoTick = false;
            flow = game.gameObject.AddComponent<GameFlowController>();
            flow.Initialize(game, false, true);
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= Loaded;
            if (device != null) InputSystem.RemoveDevice(device);
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            if (game != null)
            {
                var scene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("ExpeditionSelectionCleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            SceneTransitionState.Reset();
            CombatTargetRegistry.Clear();
            Time.timeScale = 1;
        }

        private IEnumerator Arrival()
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (flow.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(flow.IsLoading);
            Assert.AreEqual(GameFlowState.Expedition, flow.State);
            Assert.IsTrue(Intro.IsPlaying);
        }

        [UnityTest] public IEnumerator StartOpensThreeChoicesAndKeepsPlayerInVault()
        {
            int resets = game.RunResetCount;
            flow.View.StartButton.onClick.Invoke();
            Assert.IsTrue(flow.IsSelectingExpedition);
            Assert.IsTrue(flow.View.IsExpeditionSelectionVisible);
            Assert.IsFalse(flow.View.IsVaultVisible);
            Assert.AreEqual(GameFlowState.Vault, flow.State);
            Assert.IsTrue(game.Session.IsInWarehouse);
            Assert.IsTrue(game.Session.IsPaused);
            Assert.IsFalse(game.Session.AllowManualPause);
            Assert.IsFalse(flow.BeginExpedition());
            Assert.AreEqual(flow.View.DemoRoundButton.gameObject, EventSystem.current.currentSelectedGameObject);
            CollectionAssert.AreEqual(new[] { "RONDA DEMO", "RONDA COMPLETA", "SALIR" }, new[] {
                flow.View.DemoRoundButton.GetComponentInChildren<Text>().text,
                flow.View.CompleteRoundButton.GetComponentInChildren<Text>().text,
                flow.View.ExitExpeditionButton.GetComponentInChildren<Text>().text });
            yield return new WaitForSecondsRealtime(.15f);
            Assert.AreEqual(resets, game.RunResetCount);
            Assert.AreEqual(0, game.Session.GameplayTime);
        }

        [UnityTest] public IEnumerator ExitKeepsCollectionFusionAndRunAndDoesNotReopenWhileTouchingPortal()
        {
            var travel = Object.FindAnyObjectByType<WarehouseTravel>();
            var gateway = System.Array.Find(Object.FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None), g => g.FromWarehouse);
            var station = Object.FindAnyObjectByType<FusionStation>();
            station.Progress.owned[4] = 9;
            int resets = game.RunResetCount;
            yield return new WaitForSecondsRealtime(1.05f);
            var collider = game.Player.GetComponent<Collider>();
            gateway.SendMessage("OnTriggerEnter", collider);
            Assert.IsTrue(flow.IsSelectingExpedition);
            flow.View.ExitExpeditionButton.onClick.Invoke();
            gateway.SendMessage("OnTriggerStay", collider);
            Assert.IsFalse(flow.IsSelectingExpedition);
            Assert.IsTrue(travel.IsInWarehouse);
            Assert.IsFalse(game.Session.IsPaused);
            Assert.IsTrue(game.Session.AllowManualPause);
            Assert.AreEqual(9, station.Progress.owned[4]);
            Assert.AreEqual(resets, game.RunResetCount);
            Assert.IsNull(EventSystem.current.currentSelectedGameObject);
            Assert.IsFalse(LoadingScreen.IsLoading);
            gateway.SendMessage("OnTriggerExit", collider);
            gateway.SendMessage("OnTriggerEnter", collider);
            Assert.IsTrue(flow.IsSelectingExpedition);
        }

        [UnityTest] public IEnumerator DemoHasVisibleShortLoadingThenPortalAudioAndThreeMinuteObjective()
        {
            Assert.IsTrue(flow.BeginExpedition());
            float started = Time.realtimeSinceStartup;
            flow.View.DemoRoundButton.onClick.Invoke();
            // Duplicate clicks cannot change the chosen mode or start another transition.
            flow.SelectExpedition(true);
            yield return null;
            var loading = Object.FindAnyObjectByType<LoadingScreen>();
            Assert.IsNotNull(loading);
            Assert.AreEqual(Color.black, loading.GetComponentInChildren<Image>().color);
            Assert.IsTrue(game.Session.IsInWarehouse);
            Assert.IsFalse(Intro.IsPlaying);
            var frame = loading.GetComponentInChildren<RawImage>().uvRect;
            yield return new WaitForSecondsRealtime(.25f);
            Assert.AreNotEqual(frame, loading.GetComponentInChildren<RawImage>().uvRect);
            Assert.AreEqual(0, game.Session.GameplayTime);
            yield return Arrival();
            Assert.That(Time.realtimeSinceStartup - started, Is.InRange(.8f, 1.2f));
            Assert.AreEqual(180, game.WaveSettings.BossStartTime);
            Assert.AreSame(game.WaveSettings, game.Director.Settings);
            Assert.IsFalse(game.Session.IsInWarehouse);
            Assert.IsTrue(game.Session.IsPaused);
            Assert.AreEqual(0, game.Session.GameplayTime);
            var portal = Intro.GetComponent<AudioSource>();
            Assert.AreSame(Resources.Load<AudioClip>("Audio/PortalArrival"), portal.clip);
            Assert.IsTrue(portal.isPlaying);
            Assert.IsTrue(System.Array.Exists(Intro.GetComponentsInChildren<Text>(true), text => text.text == "3 MINUTOS"));
            yield return new WaitForSecondsRealtime(2.7f);
            Assert.IsFalse(Intro.IsPlaying);
            Assert.IsFalse(game.Session.IsPaused);
            if (portal.clip.length > 3.1f) Assert.IsTrue(portal.isPlaying, "The MP3 must finish naturally after the intro.");
            game.ResetRun();
            Assert.IsFalse(portal.isPlaying, "A run reset stops any remaining portal audio.");
        }

        [UnityTest] public IEnumerator CompleteRoundUsesEightMinuteWavesAndReturnsToVaultWithCollection()
        {
            var station = Object.FindAnyObjectByType<FusionStation>();
            station.Progress.owned[4] = 7;
            flow.BeginExpedition();
            flow.View.CompleteRoundButton.onClick.Invoke();
            yield return Arrival();
            Assert.AreEqual(480, game.WaveSettings.BossStartTime);
            Assert.AreSame(game.WaveSettings, game.Director.Settings);
            Assert.IsTrue(System.Array.Exists(Intro.GetComponentsInChildren<Text>(true), text => text.text == "8 MINUTOS"));
            yield return new WaitForSecondsRealtime(2.7f);
            game.Session.SetManualPause(true); game.Hud.RefreshNow();
            flow.RequestAbandon(); flow.AcceptConfirmation();
            Assert.AreEqual(GameFlowState.Vault, flow.State);
            Assert.IsTrue(game.Session.IsInWarehouse);
            Assert.AreEqual(7, station.Progress.owned[4]);
            flow.BeginExpedition(); flow.View.DemoRoundButton.onClick.Invoke();
            yield return Arrival();
            Assert.AreEqual(180, game.Director.Settings.BossStartTime);
        }

        [UnityTest] public IEnumerator EscapeClosesSelectionWithoutStartingRun()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>(); device = keyboard;
            flow.BeginExpedition();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            yield return null; yield return null;
            Assert.IsFalse(flow.IsSelectingExpedition);
            Assert.AreEqual(GameFlowState.Vault, flow.State);
            Assert.IsFalse(game.Session.IsPaused);
            Assert.IsFalse(game.Hud.IsPauseVisible);
        }

        [UnityTest] public IEnumerator GamepadCanChooseCompleteRoundWithoutDashingAfterArrival()
        {
            var pad = InputSystem.AddDevice<Gamepad>(); device = pad;
            flow.BeginExpedition();
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadDown));
            yield return null; yield return null;
            Assert.AreEqual(flow.View.CompleteRoundButton.gameObject, EventSystem.current.currentSelectedGameObject);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
            yield return null; yield return null;
            Assert.IsTrue(flow.IsLoading);
            yield return Arrival();
            Assert.AreEqual(480, game.WaveSettings.BossStartTime);
            yield return new WaitForSecondsRealtime(2.7f);
            Assert.IsFalse(game.Motor.IsDashing);
            Assert.IsFalse(game.Motor.GetComponent<PlayerInputReader>().IsDashQueued);
            Assert.IsNull(EventSystem.current.currentSelectedGameObject);
            InputSystem.QueueStateEvent(pad, new GamepadState());
        }
    }
}
