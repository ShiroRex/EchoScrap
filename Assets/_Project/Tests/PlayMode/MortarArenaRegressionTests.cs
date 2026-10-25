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
    public sealed class MortarArenaRegressionTests
    {
        private CombatGameBootstrap game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded("CombatArena"));
            yield return SceneManager.LoadSceneAsync("CombatArena", LoadSceneMode.Single);
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            Assert.IsNotNull(game);
            Assert.IsTrue(game.IsInitialized);
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            game.ResetRun(false);
            game.Session.ResetSession(20261025);
            game.Weapon.enabled = false;
            game.Motor.Stop();
            game.Motor.enabled = false;
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
            game.Session.ResumeAllUserPauses();
            Assert.IsFalse(game.Session.IsPaused);
            foreach (var lava in Object.FindObjectsByType<LavaHazard>(FindObjectsSortMode.None))
                lava.AutoTick = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (game != null)
            {
                Scene arena = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("MortarArenaCleanup"));
                yield return SceneManager.UnloadSceneAsync(arena);
            }
            CombatTargetRegistry.Clear();
        }

        [UnityTest]
        public IEnumerator AuthoredMortar_FiresAndLandsAtPlayer_InActualArenaCenter()
        {
            yield return VerifyAttack(Vector3.zero, "ArenaCenter");
        }

        [UnityTest]
        public IEnumerator AuthoredMortar_FiresAndLandsAtPlayer_OnActualArenaHill()
        {
            Vector3 hill = ArenaSurface.ReferenceToWorld(new Vector3(-24f, 0f, 3f));
            Assert.That(ArenaSurface.Height(hill), Is.GreaterThan(5f));
            yield return VerifyAttack(hill, "ArenaHill");
        }

        private IEnumerator VerifyAttack(Vector3 playerGroundPosition, string evidenceName)
        {
            var playerBody = game.Player.GetComponent<Rigidbody>();
            Vector3 playerPosition = playerGroundPosition;
            playerPosition.y = ArenaSurface.SupportHeight(playerPosition, game.Player.HitRadius) + 1f;
            playerBody.position = playerPosition;
            playerBody.linearVelocity = Vector3.zero;
            playerBody.constraints = RigidbodyConstraints.FreezeAll;
            game.Player.transform.position = playerPosition;

            EnemyArchetypeData data = game.WaveSettings.GetArchetype(EnemyArchetypeId.Mortar);
            // A six-metre diagonal keeps the complete enemy beside the player in the gameplay camera.
            EnemyActor mortar = game.Enemies.Spawn(data, playerGroundPosition + new Vector3(4.8f, 0f, 3.6f));
            mortar.Body.constraints = RigidbodyConstraints.FreezeAll;
            Physics.SyncTransforms();
            Assert.AreSame(game.Hazards, mortar.PuddlePool, "The authored arena must bind its live hazard pool.");
            Assert.That(data.MortarAttack.MaximumRange, Is.GreaterThan(6f));
            Assert.IsTrue(game.Hazards.CanLaunchProjectile);

            // Exercise the real FixedUpdate path, rather than skipping the cooldown with one large step.
            int maximumFixedSteps = Mathf.CeilToInt(data.MortarAttack.FireInterval / Time.fixedDeltaTime) + 3;
            for (int index = 0; index < maximumFixedSteps && mortar.MortarShotsFiredCount == 0; index++)
            {
                game.Session.ResumeFromFocusLoss();
                yield return new WaitForFixedUpdate();
            }

            Assert.That(mortar.MortarShotsFiredCount, Is.EqualTo(1),
                $"No attack: distance={Vector3.Distance(playerPosition, mortar.transform.position):F3}, " +
                $"cooldown={mortar.AttackCooldownTimer:F3}, paused={game.Session.IsPaused}, " +
                $"alive={game.Player.IsAlive}, capacity={game.Hazards.CanLaunchProjectile}");
            Assert.That(game.Hazards.TotalProjectilesLaunched, Is.EqualTo(1));
            Assert.That(game.Hazards.ActiveProjectileCount, Is.EqualTo(1));
            Assert.That(game.Hazards.ReservedPuddleCount, Is.EqualTo(1));

            // Keep the actual actor and geometry, but advance flight deterministically for the evidence.
            mortar.enabled = false;
            game.Hazards.StepPool(data.MortarAttack.FlightDuration * 0.35f);
            Assert.That(game.Hazards.ActiveProjectileCount, Is.EqualTo(1),
                "The projectile must leave its emitter instead of colliding immediately with the arena.");
            game.Session.SetManualPause(true);
            yield return Capture(evidenceName + "_Flight");
            game.Session.SetManualPause(false);
            game.Hazards.StepPool(data.MortarAttack.FlightDuration);
            Assert.That(game.Hazards.ActiveProjectileCount, Is.Zero);
            Assert.That(game.Hazards.ReservedPuddleCount, Is.Zero);
            Assert.That(game.Hazards.ActivePuddleCount, Is.EqualTo(1));
            var puddle = game.Hazards.GetPuddleBySequenceId(1);
            Vector3 planarMiss = puddle.CenterXZ - playerGroundPosition;
            planarMiss.y = 0f;
            Assert.That(planarMiss.magnitude, Is.LessThan(0.15f),
                "An unobstructed attack must land at the player's ground position.");
            Assert.That(puddle.CenterXZ.y, Is.EqualTo(ArenaSurface.Height(playerGroundPosition)).Within(0.03f));
            Assert.That(game.Player.CurrentHealth, Is.LessThan(game.Player.MaxHealth));
            game.Player.GetComponent<ActorVisualComposer>().StepVisuals(0.5f);

            string directory = EvidenceDirectory();
            File.WriteAllText(Path.Combine(directory, evidenceName + "_Attack.txt"),
                $"Mortar shots: {mortar.MortarShotsFiredCount}\n" +
                $"Projectiles launched: {game.Hazards.TotalProjectilesLaunched}\n" +
                $"Puddles created: {game.Hazards.TotalPuddlesCreated}\n" +
                $"Player: {playerPosition}\nMortar: {mortar.transform.position}\n" +
                $"Landing: {puddle.CenterXZ}\nPlayer health: {game.Player.CurrentHealth}\n");
            game.Session.SetManualPause(true);
            yield return Capture(evidenceName + "_Impact");
        }

        private static string EvidenceDirectory()
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "MortarCampana", "ArenaRegression");
            Directory.CreateDirectory(directory);
            return directory;
        }

        private IEnumerator Capture(string evidenceName)
        {
            Camera camera = game.GameplayCamera;
            var renderTexture = new RenderTexture(1280, 720, 24);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                camera.targetTexture = renderTexture;
                yield return null;
                yield return null;
                yield return null;
                RenderTexture.active = renderTexture;
                texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(EvidenceDirectory(), evidenceName + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                renderTexture.Release();
                Object.Destroy(renderTexture);
                if (texture != null) Object.Destroy(texture);
            }
        }
    }
}
