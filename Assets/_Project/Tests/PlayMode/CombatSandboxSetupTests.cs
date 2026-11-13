using System.Collections;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class CombatSandboxSetupTests
    {
        private Scene sandbox;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            CombatTargetRegistry.Clear();
            if (sandbox.IsValid() && sandbox.isLoaded)
            {
                if (SceneManager.sceneCount <= 1)
                    SceneManager.SetActiveScene(SceneManager.CreateScene("CombatSandboxCleanup"));
                yield return SceneManager.UnloadSceneAsync(sandbox);
            }
        }

        [UnityTest]
        public IEnumerator PlayableFiveEnemyEncounterResetsDeathHazardsAndWeapon_WithoutRecreatingPoolActors()
        {
            Time.timeScale = 1f;
            CombatTargetRegistry.Clear();
            yield return SceneManager.LoadSceneAsync("LocomotionSandbox", LoadSceneMode.Single);
            sandbox = SceneManager.GetActiveScene();
            var setup = new GameObject("CombatSandboxTestSetup").AddComponent<CombatSandboxSetup>();
            setup.RestartEncounter();
            Assert.That(setup.IsInitialized, Is.True);
            Assert.That(setup.Enemies.TotalCreatedCount, Is.EqualTo(48));
            Assert.That(CombatTargetRegistry.ActiveTargets.Count, Is.EqualTo(5));
            Assert.That(setup.Weapon.Session, Is.EqualTo(setup.Session));
            Assert.That(setup.Health.GetComponent<PlayerDashPresentation>(), Is.Not.Null);
            Assert.That(setup.Health.GetComponent<FlashlightPresentation>(), Is.Not.Null);
            yield return null;

            setup.CorrosiveHazards.SpawnPuddle(500, new Vector3(4f, 0f, 0f));
            setup.MothExplosions.Enqueue(501, new Vector3(40f, 0f, 40f));
            setup.Weapon.StepWeapon(0.4f);
            setup.Health.ForceSetHealthForTesting(0f);
            setup.Session.ResolveSimulationStepOutcome();
            Assert.That(setup.Session.State, Is.EqualTo(SessionState.Defeat));
            Assert.That(setup.Weapon.enabled, Is.False);
            setup.RestartEncounter();
            Assert.That(setup.Session.State, Is.EqualTo(SessionState.Running));
            Assert.That(setup.Session.IsPaused, Is.False);
            Assert.That(setup.Health.CurrentHealth, Is.EqualTo(100f));
            Assert.That(setup.Health.GetComponent<PlayerMotor>().enabled, Is.True);
            Assert.That(setup.Weapon.enabled, Is.True);
            Assert.That(setup.Weapon.TotalPulsesEmitted, Is.Zero);
            Assert.That(setup.CorrosiveHazards.ActivePuddleCount, Is.Zero);
            Assert.That(setup.CorrosiveHazards.ActiveProjectileCount, Is.Zero);
            Assert.That(setup.MothExplosions.PendingCount, Is.Zero);
            Assert.That(setup.Enemies.TotalCreatedCount, Is.EqualTo(48));
            Assert.That(CombatTargetRegistry.ActiveTargets.Count, Is.EqualTo(5));
            setup.Session.SetManualPause(true);

            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var target = new RenderTexture(1280, 720, 24);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            Texture2D texture = null;
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "CombatSandbox");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "Preview.png");
            try
            {
                camera.targetTexture = target;
                yield return null;
                yield return null;
                yield return null;
                RenderTexture.active = target;
                texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                target.Release();
                Object.Destroy(target);
                if (texture != null) Object.Destroy(texture);
                setup.Session.SetManualPause(false);
                Time.timeScale = 1f;
            }
            Assert.That(File.Exists(path), Is.True);
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(10000));
        }
    }
}
