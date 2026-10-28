using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class BossArenaIntegrationTests
    {
        private CombatGameBootstrap game;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = game.Encounter.AutoTick = false;
            game.ResetRun(false);
            game.Session.ResumeFromFocusLoss();
            game.Weapon.enabled = false;
            game.Player.IgnoreIncomingDamage = true;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            if (game != null)
            {
                Scene old = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("FoundryCleanup"));
                yield return SceneManager.UnloadSceneAsync(old);
            }
            CombatTargetRegistry.Clear();
        }

        private void Begin()
        {
            game.Session.Clock.SetGameplayTimeForTesting(game.WaveSettings.BossStartTime);
            game.Director.Step();
            Assert.IsNotNull(game.Encounter.Boss);
            game.Encounter.Boss.AutoTick = false;
        }

        private void Activate()
        {
            Begin();
            game.Encounter.Step(0.31f);
            game.Encounter.Step(0.31f);
            game.Encounter.Step(BossEncounter.IntroDuration);
            Assert.IsTrue(game.Director.BossActive);
        }

        [Test]
        public void TransferFromMapEdgePreservesBuildAndHealth_AndManualPauseFreezesFade()
        {
            var body = game.Player.GetComponent<Rigidbody>();
            body.position = new Vector3(80f, 1f, -75f);
            game.Player.transform.position = body.position;
            float health = game.Player.CurrentHealth;
            float damage = game.Weapon.ActiveDamagePerPulse;
            Begin();
            game.Encounter.Step(0.12f);
            float alpha = game.BossArena.TransferAlpha;
            game.Session.SetManualPause(true);
            game.Encounter.Step(3f);
            Assert.AreEqual(alpha, game.BossArena.TransferAlpha);
            Assert.Greater(body.position.x, 70f);
            game.Session.SetManualPause(false);
            game.Encounter.Step(0.2f);
            Assert.Less(Vector3.Distance(body.position, game.BossArena.Center + new Vector3(0f, 1f, -12f)), 0.2f);
            Assert.AreEqual(health, game.Player.CurrentHealth);
            Assert.AreEqual(damage, game.Weapon.ActiveDamagePerPulse);
            Assert.IsTrue(game.BossArena.BarriersEnabled);
            game.Encounter.Step(0.31f);
            Assert.IsFalse(game.BossArena.IsTransferring);
            game.Encounter.Step(BossEncounter.IntroDuration);
            Assert.AreEqual(GolgoraState.Pursuing, game.Encounter.Boss.State);
            game.Hud.RefreshNow();
            StringAssert.Contains("GÓLGORA", game.Hud.DisplayedBossName);
        }

        [Test]
        public void TransferOwnsTemporaryProtectionWithoutOverwritingOtherInvulnerability()
        {
            game.Player.IgnoreIncomingDamage = false;
            var lava = Object.FindAnyObjectByType<LavaHazard>();
            Assert.IsNotNull(lava);
            var body = game.Player.GetComponent<Rigidbody>();
            Vector3 position = ArenaSurface.OnGround(lava.transform.position, 1f);
            body.position = game.Player.transform.position = position;
            float health = game.Player.CurrentHealth;
            Begin();
            lava.StepHazard(0.02f);
            Assert.IsTrue(game.Player.IsEncounterTransferProtected);
            Assert.AreEqual(health, game.Player.CurrentHealth, "An immobilized transfer must survive the actual lava hazard.");
            game.Encounter.Step(0.61f);
            Assert.IsFalse(game.Player.IsEncounterTransferProtected);
            Assert.IsFalse(game.Player.IgnoreIncomingDamage);
            game.Player.TakeContactOrHazardDamage(5f, Vector3.zero, 0, DamageSourceType.Lava);
            Assert.AreEqual(health - 5f, game.Player.CurrentHealth, "Transfer protection must be released after the fade.");
            game.Player.IgnoreIncomingDamage = true;
            game.BossArena.EndEncounter();
            Assert.IsTrue(game.Player.IgnoreIncomingDamage, "Releasing transfer ownership must preserve another invulnerability source.");
            game.ResetRun(false);
            Assert.IsFalse(game.Player.IsEncounterTransferProtected);
        }

        [UnityTest]
        public IEnumerator FramingKeepsOppositeEdgesVisibleWithoutRotatingCamera()
        {
            Activate();
            Quaternion rotation = game.CameraRig.transform.rotation;
            Vector3 playerPosition = new Vector3(0f, 1f, -21.4f);
            Vector3 bossPosition = new Vector3(0f, 1f, 20f);
            game.Player.GetComponent<Rigidbody>().position = game.Player.transform.position = playerPosition;
            game.Encounter.Boss.Body.position = game.Encounter.Boss.transform.position = bossPosition;
            for (int i = 0; i < 45; i++) yield return null;
            Vector3 playerViewport = game.GameplayCamera.WorldToViewportPoint(playerPosition - Vector3.up);
            Vector3 bossViewport = game.GameplayCamera.WorldToViewportPoint(bossPosition + Vector3.up * 5f);
            Assert.That(playerViewport.y, Is.InRange(0.1f, 0.9f));
            Assert.That(bossViewport.y, Is.InRange(0.1f, 0.9f));
            Assert.Greater(playerViewport.z, 0f);
            Assert.Less(Quaternion.Angle(rotation, game.CameraRig.transform.rotation), 0.01f);
        }

        [Test]
        public void ReinforcementsHaveWarningAndCap_AndCercoClearsPressureWithoutKillingBoss()
        {
            Activate();
            int queued = game.BossArena.QueueMinionWave(game.WaveSettings, 0, 8);
            Assert.Greater(queued, 0);
            Assert.AreEqual(0, game.BossArena.SpawnedMinions);
            game.Encounter.Step(0.4f);
            Assert.AreEqual(0, game.BossArena.SpawnedMinions);
            game.Encounter.Step(0.51f);
            Assert.Greater(game.BossArena.SpawnedMinions, 0);
            game.BossArena.QueueMinionWave(game.WaveSettings, 0, 8);
            game.Encounter.Step(1f);
            int alive = 0;
            foreach (var actor in EnemyActor.ActiveEnemies) if (actor != game.Encounter.Boss && actor.IsAlive) alive++;
            Assert.LessOrEqual(alive + game.BossArena.PendingMinions, 8);
            float bossHealth = game.Encounter.Boss.CurrentHealth;
            game.Encounter.Boss.Step(game.Encounter.Boss.Settings.FirstCercoTime);
            Assert.IsTrue(game.Encounter.Boss.IsCercoSequence);
            Assert.IsTrue(game.Director.BossMinionsSuppressed);
            Assert.AreEqual(0, game.BossArena.PendingMinions);
            Assert.IsTrue(game.Encounter.Boss.IsAlive);
            Assert.AreEqual(bossHealth, game.Encounter.Boss.CurrentHealth);
            Assert.AreEqual(0, game.Director.SpawnBossMinionWave());
            game.Encounter.Boss.Step(game.Encounter.Boss.Settings.CercoWindup);
            game.Encounter.Boss.Step(5f);
            game.Encounter.Boss.Step(game.Encounter.Boss.Settings.CercoRecovery);
            Assert.IsFalse(game.Director.BossMinionsSuppressed);
        }

        [Test]
        public void RepeatedRestartReopensPerimeterClearsWarningsAndRestoresCamera()
        {
            var follow = game.CameraRig.GetComponent<CinemachineFollow>();
            Vector3 offset = follow.FollowOffset;
            Quaternion rotation = game.CameraRig.transform.rotation;
            for (int i = 0; i < 10; i++)
            {
                Activate();
                game.BossArena.QueueMinionWave(game.WaveSettings, 0, 8);
                game.ResetRun(false);
                Assert.IsFalse(game.BossArena.IsClosed);
                Assert.IsFalse(game.BossArena.BarriersEnabled);
                Assert.AreEqual(0, game.BossArena.PendingMinions);
                Assert.AreEqual(0f, game.BossArena.TransferAlpha);
                Assert.IsFalse(game.Player.IsEncounterTransferProtected);
                Assert.AreSame(game.Player.transform, game.CameraRig.Follow);
                Assert.AreEqual(offset, follow.FollowOffset);
                Assert.Less(Quaternion.Angle(rotation, game.CameraRig.transform.rotation), 0.01f);
            }
        }
    }
}
