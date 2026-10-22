using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class CombatWaveDirectorTests
    {
        private Scene testScene;
        private CombatSession session;
        private PlayerHealth player;
        private EnemyPool enemies;
        private CorrosivePuddlePool hazards;
        private Camera camera;
        private CombatWaveDirector director;
        private CombatWaveSettings settings;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            PhysicsLayers.EnsureConfigured();
            CombatTargetRegistry.Clear();
            testScene = SceneManager.CreateScene($"WaveDirectorTest_{Random.Range(10000, 99999)}");
            SceneManager.SetActiveScene(testScene);
            session = new GameObject("CombatSession").AddComponent<CombatSession>();
            var playerObject = new GameObject("Player");
            playerObject.layer = PhysicsLayers.PlayerLayer;
            playerObject.transform.position = Vector3.up;
            var body = playerObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            playerObject.AddComponent<CapsuleCollider>();
            playerObject.AddComponent<PlayerRuntimeStats>();
            player = playerObject.AddComponent<PlayerHealth>();
            player.BindSession(session);
            hazards = new GameObject("Hazards").AddComponent<CorrosivePuddlePool>();
            hazards.BindReferences(session, player);
            enemies = new GameObject("Enemies").AddComponent<EnemyPool>();
            enemies.BindReferences(session, player, hazards);
            camera = new GameObject("GameplayCamera").AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.aspect = 16f / 9f;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 18f, -10.3923f), Quaternion.Euler(60f, 0f, 0f));
            settings = ScriptableObject.CreateInstance<CombatWaveSettings>();
            director = new GameObject("WaveDirector").AddComponent<CombatWaveDirector>();
            director.AutoTick = false;
            director.Settings = settings;
            director.BindReferences(session, enemies, player, camera);
            director.BindHazardPool(hazards);
            session.ResetSession(20261022);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            CombatTargetRegistry.Clear();
            if (testScene.IsValid() && testScene.isLoaded)
            {
                if (SceneManager.sceneCount <= 1)
                    SceneManager.SetActiveScene(SceneManager.CreateScene($"WaveCleanup_{Random.Range(10000, 99999)}"));
                yield return SceneManager.UnloadSceneAsync(testScene);
            }
            if (settings != null)
                Object.Destroy(settings);
        }

        [Test]
        public void DefaultPhases_KeepAcceptedCadenceCompositionAndSeparateStressCap()
        {
            CombatWavePhase phase = settings.GetPhase(0f, out int index, out float progress);
            Assert.AreEqual(0, index);
            Assert.AreEqual(1, phase.GroupSize);
            Assert.AreEqual(1.2f, phase.InitialInterval);
            Assert.AreEqual(100f, phase.InitialWeights.Residual);
            phase = settings.GetPhase(60f, out index, out progress);
            Assert.AreEqual(1, index);
            Assert.AreEqual(2, phase.GroupSize);
            Assert.AreEqual(0.8f, phase.InitialInterval);
            Assert.AreEqual(25f, phase.InitialWeights.Interceptor);
            Assert.AreEqual(15f, phase.InitialWeights.Mortar);
            Assert.AreEqual(10f, phase.InitialWeights.Moth);
            phase = settings.GetPhase(120f, out index, out progress);
            Assert.AreEqual(2, index);
            Assert.AreEqual(3, phase.GroupSize);
            Assert.AreEqual(0.6f, phase.InitialInterval);
            phase = settings.GetPhase(240f, out index, out progress);
            Assert.AreEqual(3, index);
            Assert.AreEqual(0.36f, Mathf.Lerp(phase.InitialInterval, phase.FinalInterval, progress), 0.0001f);
            EnemyWaveWeights weights = EnemyWaveWeights.Lerp(phase.InitialWeights, phase.FinalWeights, progress);
            Assert.AreEqual(32.5f, weights.Interceptor);
            Assert.AreEqual(7.5f, weights.Colossus);
            phase = settings.GetPhase(390f, out index, out progress);
            Assert.AreEqual(4, index);
            Assert.AreEqual(0.26f, Mathf.Lerp(phase.InitialInterval, phase.FinalInterval, progress), 0.0001f);
            weights = EnemyWaveWeights.Lerp(phase.InitialWeights, phase.FinalWeights, progress);
            Assert.AreEqual(27.5f, weights.Interceptor);
            Assert.AreEqual(22.5f, weights.Mortar);
            Assert.AreEqual(150, director.PopulationLimit);
            director.StressPopulation = true;
            Assert.AreEqual(250, director.PopulationLimit);
        }

        [Test]
        public void FirstAttemptWaitsItsInterval_AndPhaseTransitionsDoNotReleaseOldGroups()
        {
            director.Step(1.19f);
            Assert.AreEqual(0, director.GroupsAttemptedCount);
            director.Step(0.01f);
            Assert.AreEqual(1, director.GroupsAttemptedCount);
            Assert.AreEqual(1, director.LastGroupSpawnedCount);
            Assert.AreEqual(EnemyArchetypeId.Residual, EnemyActor.ActiveEnemies[0].ArchetypeId);
            director.Step(58.8f);
            Assert.AreEqual(1, director.GroupsAttemptedCount, "Changing phase does not release a catch-up group.");
            Assert.AreEqual(1, director.PhaseIndex);
            Assert.AreEqual(60.8f, director.NextSpawnTime, 0.0001f);
            director.Step(0.79f);
            Assert.AreEqual(1, director.GroupsAttemptedCount);
            director.Step(0.01f);
            Assert.AreEqual(2, director.GroupsAttemptedCount);
            Assert.AreEqual(2, director.LastGroupSpawnedCount);
            director.Step(5f);
            Assert.AreEqual(3, director.GroupsAttemptedCount, "An oversized frame attempts at most one group.");
            director.Step(0.01f);
            Assert.AreEqual(3, director.GroupsAttemptedCount);
        }

        [Test]
        public void SpawnPositions_ExpandPastVisibleRingWithMargin_AndStayInsideArena()
        {
            bool expandedPastRing = false;
            for (int i = 0; i < 64; i++)
            {
                Assert.IsTrue(director.TryFindSpawnPosition(EnemyArchetypeId.Colossus, out Vector3 position));
                Assert.IsTrue(director.InsideArena(new Vector2(position.x, position.z), 0.8f));
                AssertOutsideProjectedFootprint(position, 2.8f);
                expandedPastRing |= new Vector2(position.x, position.z).magnitude > 16f;
            }
            Assert.IsTrue(expandedPastRing, "The initial camera can see past 16 m, so the ring must expand.");

            player.transform.position = new Vector3(56f, 1f, 56f);
            player.GetComponent<Rigidbody>().position = player.transform.position;
            camera.transform.position += new Vector3(56f, 0f, 56f);
            Physics.SyncTransforms();
            int cornerPositions = 0;
            for (int i = 0; i < 64; i++)
            {
                if (!director.TryFindSpawnPosition(EnemyArchetypeId.Residual, out Vector3 position))
                    continue; // Sixteen unsuccessful directions are allowed to discard this attempt near a wall.
                cornerPositions++;
                Assert.IsTrue(director.InsideArena(new Vector2(position.x, position.z), 0.45f));
                AssertOutsideProjectedFootprint(position, 2.45f);
            }
            Assert.That(cornerPositions, Is.GreaterThanOrEqualTo(32));
        }

        [Test]
        public void SpawnPosition_RejectsBlockedVolumeAndInvalidCamera_WithoutBacklog()
        {
            var obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstruction.transform.position = Vector3.up;
            obstruction.transform.localScale = new Vector3(120f, 2f, 120f);
            Physics.SyncTransforms();
            Assert.IsFalse(director.TryFindSpawnPosition(EnemyArchetypeId.Residual, out _));
            director.Step(20f);
            Assert.AreEqual(1, director.GroupsAttemptedCount);
            Assert.AreEqual(0, director.TotalSpawnedCount);
            Assert.AreEqual(1, director.TotalDiscardedCount);
            Object.DestroyImmediate(obstruction);
            Physics.SyncTransforms();
            director.Step(0.01f);
            Assert.AreEqual(0, director.TotalSpawnedCount, "A cleared obstruction does not release missed spawns.");
            director.Step(1.2f);
            Assert.AreEqual(1, director.TotalSpawnedCount);
            camera.transform.rotation = Quaternion.Euler(-60f, 0f, 0f);
            Assert.IsFalse(director.TryFindSpawnPosition(EnemyArchetypeId.Residual, out _));
        }

        [Test]
        public void PopulationCap_DiscardsSaturation_StressIsExplicit_AndNoSpawnVolumesOverlap()
        {
            settings.PopulationCap = 3;
            settings.StressPopulationCap = 5;
            settings.Phases = new[]
            {
                new CombatWavePhase(0f, 0.1f, 0.1f, 4,
                    new EnemyWaveWeights(100f, 0f, 0f, 0f, 0f),
                    new EnemyWaveWeights(100f, 0f, 0f, 0f, 0f))
            };
            director.ResetForSession();
            director.Step(0.1f);
            Assert.AreEqual(3, director.ActiveEnemyCount);
            Assert.AreEqual(3, director.LastGroupSpawnedCount);
            Assert.AreEqual(0, director.ReservedCount);
            director.Step(5f);
            Assert.AreEqual(3, director.ActiveEnemyCount);
            Assert.AreEqual(0, director.LastGroupSpawnedCount);
            director.StressPopulation = true;
            director.Step(0.1f);
            Assert.AreEqual(5, director.ActiveEnemyCount);
            Assert.AreEqual(2, director.LastGroupSpawnedCount);
            var active = EnemyActor.ActiveEnemies;
            for (int i = 0; i < active.Count; i++)
            for (int j = i + 1; j < active.Count; j++)
            {
                Vector3 delta = active[i].transform.position - active[j].transform.position;
                delta.y = 0f;
                Assert.That(delta.magnitude, Is.GreaterThanOrEqualTo(active[i].HitRadius + active[j].HitRadius + settings.SpawnClearance - 0.001f));
            }
        }

        [Test]
        public void PauseAndFocusFreezeDirector_AndSeededPositionSamplingResets()
        {
            Assert.IsTrue(director.TryFindSpawnPosition(EnemyArchetypeId.Residual, out Vector3 first));
            for (int i = 0; i < 20; i++) session.Rng.Presentation.NextFloat();
            session.ResetSession(20261022);
            Assert.IsTrue(director.TryFindSpawnPosition(EnemyArchetypeId.Residual, out Vector3 repeated));
            Assert.AreEqual(first, repeated, "Decorative randomness cannot alter spawn placement.");
            session.SetManualPause(true);
            director.Step(100f);
            Assert.AreEqual(0f, director.Elapsed);
            session.SimulateFocusChange(false);
            session.SetManualPause(false);
            director.Step(100f);
            Assert.AreEqual(0f, director.Elapsed, "The other pause reason remains active.");
            session.ResumeFromFocusLoss();
            director.Step(1.2f);
            Assert.AreEqual(1, director.ActiveEnemyCount);
        }

        [Test]
        public void BossTransition_ClearsActorsAndHazardsWithoutDeaths_AndRequestsOnlyOnce()
        {
            MothExplosionPool explosions = MothExplosionPool.GetOrCreate(session, player);
            explosions.Enqueue(999, player.WorldCenter);
            int deaths = 0;
            int requests = 0;
            session.Events.OnActorDied += _ => deaths++;
            director.OnBossPhaseRequested += () => requests++;
            enemies.Spawn(EnemyArchetypeId.Residual, new Vector3(12f, 1f, 5f));
            hazards.SpawnPuddle(1, new Vector3(20f, 0f, 0f));
            var projectile = CorrosivePuddlePool.ProjectileSpec.CreateMortarDefault(2, Vector3.up * 2f, new Vector3(20f, 0f, 0f));
            Assert.IsTrue(hazards.TryLaunchParabolicProjectile(in projectile));
            director.Step(480f);
            Assert.AreEqual(0, explosions.PendingCount, "La entrada elimina explosiones pendientes de la horda retirada.");
            Assert.IsTrue(director.IsBossPhase);
            Assert.IsFalse(director.BossActive);
            Assert.AreEqual(SessionState.BossIntro, session.State);
            Assert.AreEqual(1, requests);
            Assert.AreEqual(0, deaths);
            Assert.AreEqual(0, director.ActiveEnemyCount);
            Assert.AreEqual(0, hazards.ActiveProjectileCount);
            Assert.AreEqual(0, hazards.ActivePuddleCount);
            Assert.AreEqual(0, hazards.ReservedPuddleCount);
            director.Step(10f);
            Assert.AreEqual(1, requests);
            Assert.AreEqual(0, director.TotalSpawnedCount);
            session.ResetSession(20261022);
            Assert.IsFalse(director.IsBossPhase);
            Assert.IsFalse(director.BossActive);
            Assert.AreEqual(0f, director.Elapsed);
            Assert.AreEqual(0, director.TotalSpawnedCount);
            Assert.AreEqual(0, director.ReservedCount);
        }

        [Test]
        public void BossMinions_FourSecondGroupsRespectCap_RotatePartialComposition_AndNeverCatchUp()
        {
            director.Step(480f);
            director.SetBossActive(true);
            director.Step(3.99f);
            Assert.AreEqual(0, director.BossMinionWavesCount);
            director.Step(0.01f);
            Assert.AreEqual(1, director.BossMinionWavesCount);
            Assert.AreEqual(4, director.ActiveEnemyCount);
            int residual = 0, mortar = 0, moth = 0;
            foreach (EnemyActor actor in EnemyActor.ActiveEnemies)
            {
                if (actor.ArchetypeId == EnemyArchetypeId.Residual) residual++;
                if (actor.ArchetypeId == EnemyArchetypeId.Mortar) mortar++;
                if (actor.ArchetypeId == EnemyArchetypeId.ExplosiveMoth) moth++;
            }
            Assert.AreEqual(2, residual);
            Assert.AreEqual(1, mortar);
            Assert.AreEqual(1, moth);
            director.Step(40f);
            Assert.AreEqual(2, director.BossMinionWavesCount);
            Assert.AreEqual(8, director.ActiveEnemyCount, "A long frame attempts one minion wave only.");
            director.Step(0.01f);
            Assert.AreEqual(2, director.BossMinionWavesCount);
            settings.BossMinionCap = 9;
            Assert.AreEqual(1, director.SpawnBossMinionWave());
            Assert.AreEqual(9, director.ActiveEnemyCount);
            EnemyActor last = EnemyActor.ActiveEnemies[EnemyActor.ActiveEnemies.Count - 1];
            Assert.AreEqual(EnemyArchetypeId.Mortar, last.ArchetypeId, "Order rotates so a partial group does not always select Residual.");
            Assert.AreEqual(0, director.SpawnBossMinionWave());
            session.SetManualPause(true);
            director.Step(20f);
            Assert.AreEqual(4, director.BossMinionWavesCount);
        }

        private void AssertOutsideProjectedFootprint(Vector3 position, float margin)
        {
            var corners = new Vector2[4];
            var ground = new Plane(Vector3.up, Vector3.zero);
            float area = 0f;
            for (int i = 0; i < 4; i++)
            {
                Ray ray = camera.ViewportPointToRay(new Vector3(i == 1 || i == 2 ? 1f : 0f, i >= 2 ? 1f : 0f, 0f));
                Assert.IsTrue(ground.Raycast(ray, out float distance));
                Vector3 corner = ray.GetPoint(distance);
                corners[i] = new Vector2(corner.x, corner.z);
            }
            for (int i = 0; i < 4; i++)
                area += corners[i].x * corners[(i + 1) % 4].y - corners[i].y * corners[(i + 1) % 4].x;
            Vector2 point = new Vector2(position.x, position.z);
            bool beyondMargin = false;
            for (int i = 0; i < 4; i++)
            {
                Vector2 edge = corners[(i + 1) % 4] - corners[i];
                Vector2 inward = new Vector2(-edge.y, edge.x).normalized * Mathf.Sign(area);
                beyondMargin |= Vector2.Dot(inward, point - corners[i]) <= -margin;
            }
            Assert.IsTrue(beyondMargin, $"Spawn {position} must clear the projected view by {margin} m.");
        }
    }
}
