using System.Collections;
using System.Collections.Generic;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class PlayerStartupMovementTests
    {
        private CombatGameBootstrap game;
        private Keyboard keyboard;
        private readonly List<InputDevice> devices = new List<InputDevice>();
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private float maximumDelta;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            maximumDelta = Time.maximumDeltaTime;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            foreach (InputDevice device in new List<InputDevice>(InputSystem.devices))
                if (device.enabled) { devices.Add(device); InputSystem.DisableDevice(device); }
            keyboard = InputSystem.AddDevice<Keyboard>();
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            // Only supply the focus event absent in batchmode. Do not reset or teleport the actor.
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
            game.Session.SimulateFocusChange(true);
            game.Session.ResumeAllUserPauses();
            Assert.AreEqual(1, game.RunResetCount, "Exercise the original Start, not a test-only ResetRun.");
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            Time.maximumDeltaTime = maximumDelta;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            foreach (InputDevice device in devices) if (device.added) InputSystem.EnableDevice(device);
            devices.Clear();
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            if (game != null)
            {
                Scene scene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("Startup Movement Cleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            CombatTargetRegistry.Clear();
        }

        [UnityTest]
        public IEnumerator FreshSceneStartsMovingInAllFourDirectionsWithoutResetOrTeleport()
        {
            var input = game.Motor.GetComponent<PlayerInputReader>();
            var body = game.Motor.GetComponent<Rigidbody>();
            foreach (Key key in new[] { Key.W, Key.S, Key.A, Key.D })
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                yield return null;
                Vector3 start = body.position;
                for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
                Debug.Log($"[Startup] {key} position={body.position} delta={body.position - start} input={input.Movement} readable={input.CanRead} velocity={body.linearVelocity} constraints={body.constraints} layer={body.gameObject.layer} pause={game.Session.Clock.ActiveReasons}");
                Assert.IsTrue(input.CanRead);
                Assert.Greater(Vector3.ProjectOnPlane(body.position - start, Vector3.up).magnitude, 1f, key.ToString());
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                for (int i = 0; i < 8; i++) yield return new WaitForFixedUpdate();
            }
        }

        [UnityTest]
        public IEnumerator FreshSceneStartsDashWithoutResetOrTeleport()
        {
            var body = game.Motor.GetComponent<Rigidbody>();
            Vector3 start = body.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.Space));
            yield return null;
            bool started = false;
            for (int i = 0; i < 12; i++)
            {
                yield return new WaitForFixedUpdate();
                started |= game.Motor.IsDashing;
            }
            Assert.IsTrue(started, "Space must reach the motor on the original scene startup.");
            Assert.Greater(body.position.z - start.z, 3f, "The dash must leave the spawn without invisible scenery blocking it.");
            Assert.AreEqual(1, game.RunResetCount);
        }

        [UnityTest]
        public IEnumerator MovementContinuesWhenSeveralFixedStepsRunBetweenRenderedFrames()
        {
            // Slow editor frames expose stale interpolated Transform poses between physics ticks.
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return null;
            var body = game.Motor.GetComponent<Rigidbody>();
            Vector3 start = body.position;
            Time.maximumDeltaTime = 0.2f;
            for (int i = 0; i < 8; i++)
            {
                System.Threading.Thread.Sleep(80);
                yield return null;
            }
            Debug.Log($"[Startup slow frames] delta={body.position - start} velocity={body.linearVelocity} readable={game.Motor.GetComponent<PlayerInputReader>().CanRead}");
            Assert.Greater(body.position.z - start.z, 2f, "A slow editor must still integrate movement instead of restoring a stale pose.");
        }
    }
}
