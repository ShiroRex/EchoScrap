using System;
using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace EchoScrap.Tests
{
    public sealed class CombatJuiceIntegrationTests
    {
        private CombatGameBootstrap game;
        private static readonly EnemyArchetypeId[] Archetypes =
        {
            EnemyArchetypeId.Residual, EnemyArchetypeId.Interceptor, EnemyArchetypeId.Colossus,
            EnemyArchetypeId.Mortar, EnemyArchetypeId.ExplosiveMoth
        };

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = false; game.Encounter.AutoTick = false;
            game.ResetRun(false);
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            Scene arena = game.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("Juice cleanup"));
            yield return SceneManager.UnloadSceneAsync(arena);
            CombatTargetRegistry.Clear();
        }

        [Test]
        public void ColdWarmAndPrewarmedSpawnsConsumeIdenticalCombatRandomness()
        {
            var cold = new GameObject("Cold pool").AddComponent<EnemyPool>();
            cold.BindReferences(game.Session, game.Player, game.Hazards);
            game.Session.ResetSession(44191);
            float[] speeds = new float[Archetypes.Length];
            float[] cooldowns = new float[Archetypes.Length];
            for (int i = 0; i < Archetypes.Length; i++)
            {
                var actor = cold.Spawn(Archetypes[i], new Vector3(20f + i * 3f, 1f, 20f));
                speeds[i] = actor.AssignedMoveSpeed; cooldowns[i] = actor.AttackCooldownTimer;
            }
            int samples = game.Session.Rng.Combat.SampleCount;
            game.Session.ResetSession(44191);
            for (int i = 0; i < Archetypes.Length; i++) game.Enemies.Prewarm(Archetypes[i], 80);
            Assert.AreEqual(0, game.Session.Rng.Combat.SampleCount, "Preparing visuals is not a gameplay spawn.");
            for (int i = 0; i < Archetypes.Length; i++)
            {
                var actor = game.Enemies.Spawn(Archetypes[i], new Vector3(20f + i * 3f, 1f, 20f));
                Assert.AreEqual(speeds[i], actor.AssignedMoveSpeed);
                Assert.AreEqual(cooldowns[i], actor.AttackCooldownTimer);
            }
            Assert.AreEqual(samples, game.Session.Rng.Combat.SampleCount);
            Assert.AreEqual(6, samples, "Residual/Interceptor/Moth sample once, Mortar twice, Colossus samples its initial charge cooldown.");
        }

        [Test]
        public void WarmReuseOfAllFiveEnemyTypesKeepsManagedHeapAndActorCountStable()
        {
            long probeBefore = Profiler.GetMonoUsedSizeLong();
            byte[] probe = new byte[1048576];
            long probeBytes = Profiler.GetMonoUsedSizeLong() - probeBefore;
            GC.KeepAlive(probe);
            Assert.GreaterOrEqual(probeBytes, 1048576L, "The native heap counter must detect the calibration allocation.");
            for (int warm = 0; warm < 3; warm++)
                foreach (var type in Archetypes)
                {
                    var actor = game.Enemies.Spawn(type, new Vector3(30f, 1f, 30f));
                    game.Enemies.ReturnToPool(actor);
                }
            int created = game.Enemies.TotalCreatedCount;
            long before = Profiler.GetMonoUsedSizeLong();
            for (int repeat = 0; repeat < 20; repeat++)
                for (int i = 0; i < Archetypes.Length; i++)
                {
                    var actor = game.Enemies.Spawn(Archetypes[i], new Vector3(30f, 1f, 30f));
                    game.Enemies.ReturnToPool(actor);
                }
            long growth = Profiler.GetMonoUsedSizeLong() - before;
            Assert.AreEqual(created, game.Enemies.TotalCreatedCount);
            Assert.LessOrEqual(growth, 2048L, "A hundred warmed spawns must not grow the managed heap through recreated arrays or callbacks.");
        }

        [Test]
        public void HazardBudgetHoldsNinetySixAnnouncedLandings_AndDefersNextAttack()
        {
            Assert.AreEqual(96, CorrosivePuddlePool.MaxActivePuddles);
            Assert.AreEqual(96, CorrosivePuddlePool.MaxInFlightProjectiles);
            for (int i = 0; i < 96; i++)
            {
                var spec = CorrosivePuddlePool.ProjectileSpec.CreateMortarDefault(i + 1,
                    new Vector3(30f, 2f, 30f), new Vector3(30f, 0f, 30f));
                Assert.IsTrue(game.Hazards.TryLaunchParabolicProjectile(in spec));
            }
            Assert.AreEqual(96, game.Hazards.ReservedPuddleCount);
            foreach (TrailRenderer trail in game.Hazards.GetComponentsInChildren<TrailRenderer>())
                Assert.AreEqual("Sprites/Default", trail.sharedMaterial.shader.name,
                    "The trail shader must preserve its vertex colour and alpha gradient.");
            Assert.IsFalse(game.Hazards.CanLaunchProjectile);
            game.Hazards.StepPool(0.65f);
            Assert.AreEqual(96, game.Hazards.ActivePuddleCount);
            Assert.AreEqual(0, game.Hazards.ReservedPuddleCount);
        }

        [Test]
        public void MotionDustAndInterceptorWakeRespectReducedEffectsAndReset()
        {
            var composer = game.Player.GetComponent<ActorVisualComposer>();
            composer.ResetVisualState(resetOrientation: true);
            var dust = game.Player.GetComponent<ActorMotionPresentation>();
            composer.ReportLocomotion(Vector3.forward, Vector3.forward * 8f, 8f, 10f);
            Assert.That(dust.DustParticleCount, Is.InRange(1, 12));
            var enemy = game.Enemies.Spawn(EnemyArchetypeId.Interceptor, new Vector3(20f, 1f, 20f));
            var wake = enemy.GetComponent<ActorMotionPresentation>();
            enemy.Body.linearVelocity = Vector3.forward * 6f;
            wake.Refresh(); Assert.IsTrue(wake.IsTrailEmitting);
            game.Session.Effects.ReducedEffectsMode = true;
            Assert.AreEqual(0, dust.DustParticleCount); Assert.IsFalse(wake.IsTrailEmitting);
            game.ResetRun(false);
            Assert.AreEqual(0, dust.DustParticleCount); Assert.IsFalse(wake.IsTrailEmitting);
        }

        [Test]
        public void NearbyDamageLabelsChooseSeparateVerticalLanes()
        {
            var normal = new DamageResult(1, 101, DamageSourceType.FlashlightPulse,
                Vector3.zero, Vector3.up * 2f, Vector3.forward, 10f, 10f, 10f, 90f, 100f,
                false, false, false, false, false, false, 1, 1);
            var critical = new DamageResult(1, 102, DamageSourceType.FlashlightPulse,
                Vector3.zero, Vector3.up * 2f, Vector3.forward, 17.5f, 17.5f, 17.5f, 82.5f, 100f,
                true, false, false, false, false, false, 1, 1);
            game.DamageNumbers.TryShowDamage(in normal); game.DamageNumbers.TryShowDamage(in critical);
            game.DamageNumbers.RefreshProjection();
            float a = game.DamageNumbers.GetActiveLabel(0).rectTransform.anchoredPosition.y;
            float b = game.DamageNumbers.GetActiveLabel(1).rectTransform.anchoredPosition.y;
            Assert.GreaterOrEqual(Mathf.Abs(a - b), 32f);
        }

        [Test]
        public void VictorySummaryWaitsForBossExplosionAndGem_WhileGameplayRemainsFrozen()
        {
            game.Session.Clock.SetGameplayTimeForTesting(game.WaveSettings.BossStartTime);
            game.Director.Step();
            var boss = game.Encounter.Boss; boss.AutoTick = false;
            game.Encounter.Step(.31f);
            game.Encounter.Step(.31f);
            game.Encounter.Step(BossEncounter.IntroDuration);
            Assert.AreEqual(GolgoraState.Pursuing, boss.State, "Finish the transfer and introduction before applying lethal damage.");
            var lethal = new DamageRequest(1, DamageSourceType.SolarPulse, Vector3.zero, Vector3.forward, 2000f);
            boss.ReceiveDamage(in lethal); game.Session.ResolveSimulationStepOutcome();
            game.Encounter.DeathPresentation.AutoTick = false;
            game.Hud.RefreshNow(); Assert.IsFalse(game.Hud.IsResultVisible);
            game.Encounter.DeathPresentation.StepUnscaled(1.29f);
            game.Hud.RefreshNow(); Assert.IsFalse(game.Hud.IsResultVisible);
            game.Encounter.DeathPresentation.StepUnscaled(0.02f);
            game.Hud.RefreshNow(); Assert.IsTrue(game.Hud.IsResultVisible,
                $"state={game.Session.State}, age={game.Encounter.DeathPresentation.PresentationTime}, playing={game.Encounter.DeathPresentation.IsPlaying}, bossState={boss.State}, bossHP={boss.CurrentHealth}");
            Assert.IsTrue(game.Encounter.DeathPresentation.GemAbsorbed);
            Assert.AreEqual(0f, Time.timeScale);
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void CameraLeadReachesSameOffsetAtDifferentFrameRates(int fps)
        {
            var feedback = game.CameraRig.GetComponent<CombatCameraFeedback>();
            game.Motor.GetComponent<Rigidbody>().linearVelocity = Vector3.right * 8f;
            for (int i = 0; i < fps; i++) feedback.StepFeedback(1f / fps);
            Assert.AreEqual(0.8f, feedback.CurrentLeadOffsetXZ.x, 0.001f);
            Assert.AreEqual(0f, feedback.CurrentLeadOffsetXZ.y);
        }
    }
}
