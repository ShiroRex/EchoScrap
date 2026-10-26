using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class EditorPlayerStartupTests
    {
        [SerializeField] private bool previousEnabled;
        [SerializeField] private int previousOptions;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            previousEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            previousOptions = (int)EditorSettings.enterPlayModeOptions;
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/CombatArena.unity");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            EditorSettings.enterPlayModeOptionsEnabled = previousEnabled;
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)previousOptions;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [UnityTest]
        public IEnumerator FastPlayFromSavedSceneStartsWithWorkingMovement()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            yield return new EnterPlayMode(false);
            yield return CheckMovement();
            yield return new ExitPlayMode();
            yield return new EnterPlayMode(false);
            yield return CheckMovement();
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator FullReloadFromSavedSceneStartsWithWorkingMovement()
        {
            EditorSettings.enterPlayModeOptionsEnabled = false;
            yield return new EnterPlayMode();
            yield return CheckMovement();
            yield return new ExitPlayMode();
        }

        private static IEnumerator CheckMovement()
        {
            yield return null;
            var game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            Assert.IsNotNull(game);
            Assert.IsTrue(game.IsInitialized);
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            var input = game.Motor.GetComponent<PlayerInputReader>();
            var body = game.Motor.GetComponent<Rigidbody>();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            input.SendMessage("OnApplicationFocus", true);
            game.Session.SimulateFocusChange(true);
            game.Session.ResumeAllUserPauses();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return null;
            Vector3 start = body.position;
            float startTime = Time.fixedTime;
            double deadline = EditorApplication.timeSinceStartup + 15d;
            // EditMode tests are driven by editor updates even after EnterPlayMode.
            // Wait for actual simulation time, rather than counting editor iterations.
            while (Time.fixedTime - startTime < 0.5f && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.GreaterOrEqual(Time.fixedTime - startTime, 0.5f, "Physics did not advance after entering Play.");
            Debug.Log($"[Editor Play] options={(int)EditorSettings.enterPlayModeOptions} reloadEnabled={EditorSettings.enterPlayModeOptionsEnabled} moved={body.position - start} readable={input.CanRead} movement={input.Movement} velocity={body.linearVelocity} layer={game.Motor.gameObject.layer}");
            InputSystem.RemoveDevice(keyboard);
            Assert.Greater(body.position.z - start.z, 1f, "Entering Play from the saved editor scene must work without an extra LoadScene or ResetRun.");
        }
    }
}
