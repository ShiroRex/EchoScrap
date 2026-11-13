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
    /// <summary>Live arena regressions: the player walks at normal speed while the real flashlight keeps firing.</summary>
    public sealed class MovingMortarAttackTests
    {
        private CombatGameBootstrap game;
        private Keyboard keyboard;
        private readonly List<InputDevice> disabledDevices = new List<InputDevice>();
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            disabledDevices.Clear();
            foreach (InputDevice device in new List<InputDevice>(InputSystem.devices))
            {
                if (!device.enabled) continue;
                disabledDevices.Add(device);
                InputSystem.DisableDevice(device);
            }
            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return SceneManager.LoadSceneAsync("CombatArena", LoadSceneMode.Single);
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            Assert.IsNotNull(game);
            Assert.IsTrue(game.IsInitialized);
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            game.ResetRun(false);
            game.Session.ResetSession(20261025);
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
            game.Session.ResumeAllUserPauses();
            Assert.IsTrue(game.Motor.enabled);
            Assert.IsTrue(game.Weapon.enabled, "Exercise the actual auto attack rather than a harmless stationary target.");
            foreach (var lava in Object.FindObjectsByType<LavaHazard>(FindObjectsSortMode.None)) lava.AutoTick = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            foreach (InputDevice device in disabledDevices) if (device.added) InputSystem.EnableDevice(device);
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
            if (game != null)
            {
                Scene arena = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("MovingMortarCleanup"));
                yield return SceneManager.UnloadSceneAsync(arena);
            }
            CombatTargetRegistry.Clear();
        }

        [UnityTest]
        public IEnumerator MortarAtNormalSpawnDistanceAttacksBeforeAutoFireKillsItWhilePlayerApproaches()
        {
            TeleportPlayer(Vector3.zero);
            EnemyArchetypeData data = game.WaveSettings.GetArchetype(EnemyArchetypeId.Mortar);
            EnemyActor mortar = game.Enemies.Spawn(data, new Vector3(0f, 0f, 15f));
            // Include the slowest initial phase; the former 5-second cycle lost this encounter to auto fire.
            mortar.SetTimersForTesting(0f, data.MortarAttack.FireInterval - 2f, 0f, 0f);
            Vector3 start = game.Player.transform.position;
            SetKeys(Key.W);
            int steps = Mathf.CeilToInt(3f / Time.fixedDeltaTime);
            for (int i = 0; i < steps && mortar.IsAlive && mortar.MortarShotsFiredCount == 0; i++)
            {
                game.Session.ResumeFromFocusLoss();
                yield return new WaitForFixedUpdate();
            }
            SetKeys();
            Assert.That(game.Player.transform.position.z - start.z, Is.GreaterThan(7f), "The player must actually walk at gameplay speed.");
            Assert.That(game.Weapon.TotalPulsesEmitted, Is.GreaterThanOrEqualTo(4));
            Assert.That(mortar.MortarShotsFiredCount, Is.GreaterThanOrEqualTo(1),
                $"Moving encounter never fired: health={mortar.CurrentHealth}, cooldown={mortar.AttackCooldownTimer}, " +
                $"distance={Vector3.Distance(game.Player.transform.position, mortar.transform.position)}, paused={game.Session.IsPaused}.");
            Assert.That(game.Hazards.TotalProjectilesLaunched, Is.GreaterThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator RepeatedShortPassesAcrossRangeNoLongerStarveTheReload()
        {
            TeleportPlayer(new Vector3(0f, 0f, -13.5f));
            EnemyActor mortar = game.Enemies.Spawn(EnemyArchetypeId.Mortar, Vector3.zero);
            // Keep the emitter still so this test measures repeated range crossings, not navigation.
            mortar.Body.constraints = RigidbodyConstraints.FreezeAll;
            mortar.SetTimersForTesting(0f, 0f, 0f, 0f);
            bool toward = true;
            int crossings = 0;
            int steps = Mathf.CeilToInt(8f / Time.fixedDeltaTime);
            for (int i = 0; i < steps && mortar.MortarShotsFiredCount == 0; i++)
            {
                float z = game.Player.transform.position.z;
                if (toward && z >= -9f) { toward = false; crossings++; }
                if (!toward && z <= -13.5f) { toward = true; crossings++; }
                SetKeys(toward ? Key.W : Key.S);
                game.Session.ResumeFromFocusLoss();
                yield return new WaitForFixedUpdate();
            }
            SetKeys();
            Assert.That(crossings, Is.GreaterThanOrEqualTo(4), "The player must repeatedly leave and re-enter the 12 m attack range.");
            Assert.That(game.Weapon.TotalPulsesEmitted, Is.GreaterThan(8));
            Assert.That(mortar.MortarShotsFiredCount, Is.EqualTo(1),
                "Reload persists across range crossings, but each shot still requires a complete in-range warning.");
        }

        private void TeleportPlayer(Vector3 ground)
        {
            Rigidbody body = game.Player.GetComponent<Rigidbody>();
            ground.y = ArenaSurface.SupportHeight(ground, game.Player.HitRadius) + 1f;
            body.position = ground;
            game.Player.transform.position = ground;
            body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        private void SetKeys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }
    }
}
