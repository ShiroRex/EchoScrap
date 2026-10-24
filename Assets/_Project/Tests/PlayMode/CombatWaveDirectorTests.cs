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
        private Collider floor;

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
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "ArenaGround";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(120f, 1f, 120f);
            floor = ground.GetComponent<Collider>();
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
            director.BindSpawnContext(floor);
            session.ResetSession(20261022);
            yield return null;
            session.Clock.SetGameplayTimeForTesting(0f);
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
        public void MortarPopulationCapReplacesExtraArtilleryWithoutRemovingWavePressure()
        {
            var mortarOnly = new EnemyWaveWeights(0, 0, 0, 100, 0);
            settings.Phases = new[] { new CombatWavePhase(0f, 0.1f, 0.1f, 2, mortarOnly, mortarOnly) };
            settings.MortarPopulationCap = 2; director.Settings = settings;
            for (int i = 0; i < 12; i++) AdvanceDirector(0.11f);
            int mortars = 0, residuals = 0;
            foreach (EnemyActor actor in EnemyActor.ActiveEnemies)
            {
                if (actor.Session != session || !actor.IsAlive || !actor.gameObject.activeInHierarchy) continue;
                if (actor.ArchetypeId == EnemyArchetypeId.Mortar) mortars++;
                if (actor.ArchetypeId == EnemyArchetypeId.Residual) residuals++;
            }
            Assert.That(mortars, Is.EqualTo(2));
            Assert.That(residuals, Is.GreaterThan(4), "Capping artillery must not cancel the rest of the horde.");
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
            AdvanceDirector(1.19f);
            Assert.AreEqual(0, director.GroupsAttemptedCount);
            AdvanceDirector(0.01f);
            Assert.AreEqual(1, director.GroupsAttemptedCount);
            Assert.AreEqual(1, director.LastGroupSpawnedCount);
            Assert.AreEqual(EnemyArchetypeId.Residual, EnemyActor.ActiveEnemies[0].ArchetypeId);
            AdvanceDirector(58.8f);
            Assert.AreEqual(1, director.GroupsAttemptedCount, "Changing phase does not release a catch-up group.");
            Assert.AreEqual(1, director.PhaseIndex);
            Assert.AreEqual(60.8f, director.NextSpawnTime, 0.0001f);
            AdvanceDirector(0.79f);
            Assert.AreEqual(1, director.GroupsAttemptedCount);
            AdvanceDirector(0.01f);
            Assert.AreEqual(2, director.GroupsAttemptedCount);
            Assert.AreEqual(2, director.LastGroupSpawnedCount);
            AdvanceDirector(5f);
            Assert.AreEqual(3, director.GroupsAttemptedCount, "An oversized frame attempts at most one group.");
            AdvanceDirector(0.01f);
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
            AdvanceDirector(20f);
            Assert.AreEqual(1, director.GroupsAttemptedCount);
            Assert.AreEqual(0, director.TotalSpawnedCount);
            Assert.AreEqual(1, director.TotalDiscardedCount);
            Object.DestroyImmediate(obstruction);
            Physics.SyncTransforms();
            AdvanceDirector(0.01f);
            Assert.AreEqual(0, director.TotalSpawnedCount, "A cleared obstruction does not release missed spawns.");
            AdvanceDirector(1.2f);
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
            AdvanceDirector(0.1f);
            Assert.AreEqual(3, director.ActiveEnemyCount);
            Assert.AreEqual(3, director.LastGroupSpawnedCount);
            Assert.AreEqual(0, director.ReservedCount);
            AdvanceDirector(5f);
            Assert.AreEqual(3, director.ActiveEnemyCount);
            Assert.AreEqual(0, director.LastGroupSpawnedCount);
            director.StressPopulation = true;
            AdvanceDirector(0.1f);
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
            AdvanceDirector(100f);
            Assert.AreEqual(0f, director.Elapsed);
            session.SimulateFocusChange(false);
            session.SetManualPause(false);
            AdvanceDirector(100f);
            Assert.AreEqual(0f, director.Elapsed, "The other pause reason remains active.");
            session.ResumeFromFocusLoss();
            AdvanceDirector(1.2f);
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
            AdvanceDirector(480f);
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
            AdvanceDirector(10f);
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
            AdvanceDirector(480f);
            director.SetBossActive(true);
            AdvanceDirector(3.99f);
            Assert.AreEqual(0, director.BossMinionWavesCount);
            AdvanceDirector(0.01f);
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
            AdvanceDirector(40f);
            Assert.AreEqual(2, director.BossMinionWavesCount);
            Assert.AreEqual(8, director.ActiveEnemyCount, "A long frame attempts one minion wave only.");
            AdvanceDirector(0.01f);
            Assert.AreEqual(2, director.BossMinionWavesCount);
            settings.BossMinionCap = 9;
            Assert.AreEqual(1, director.SpawnBossMinionWave());
            Assert.AreEqual(9, director.ActiveEnemyCount);
            EnemyActor last = EnemyActor.ActiveEnemies[EnemyActor.ActiveEnemies.Count - 1];
            Assert.AreEqual(EnemyArchetypeId.Mortar, last.ArchetypeId, "Order rotates so a partial group does not always select Residual.");
            Assert.AreEqual(0, director.SpawnBossMinionWave());
            session.SetManualPause(true);
            AdvanceDirector(20f);
            Assert.AreEqual(4, director.BossMinionWavesCount);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void GroupsShareCompactFormation_AndEveryVisualVolumeIsOutsideCamera(int size)
        {
            SetConstantGroup(size, EnemyArchetypeId.Colossus);
            AdvanceDirector(1.2f);
            Assert.AreEqual(size, director.LastGroupSpawnedCount);
            Assert.AreEqual(0, director.ReservedCount);
            Assert.LessOrEqual(director.LastAnchorAttempts, 16);
            Assert.LessOrEqual(director.LastMemberValidations, 64);
            float spacing = 1.6f + settings.SpawnClearance;
            var actors = EnemyActor.ActiveEnemies;
            float longest = 0f, shortest = float.MaxValue;
            for (int a = 0; a < actors.Count; a++)
            {
                Assert.IsTrue(director.IsSpawnVolumeOutsideView(actors[a].ArchetypeId, actors[a].transform.position));
                for (int b = a + 1; b < actors.Count; b++)
                {
                    float distance = Vector3.Distance(actors[a].transform.position, actors[b].transform.position);
                    longest = Mathf.Max(longest, distance);
                    shortest = Mathf.Min(shortest, distance);
                }
            }
            Assert.AreEqual(spacing, shortest, 0.001f);
            Assert.AreEqual(size == 4 ? spacing * Mathf.Sqrt(2f) : spacing, longest, 0.001f);
        }

        [Test]
        public void FarEnemiesKeepTheirPopulationSlots_AndDeathReleasesOnlyAvailablePlaces()
        {
            settings.PopulationCap = 2;
            SetConstantGroup(2, EnemyArchetypeId.Residual);
            AdvanceDirector(1.2f);
            var first = EnemyActor.ActiveEnemies[0];
            var second = EnemyActor.ActiveEnemies[1];
            first.Body.position = first.transform.position = new Vector3(55f, 1f, 55f);
            second.Body.position = second.transform.position = new Vector3(-55f, 1f, -55f);
            AdvanceDirector(12f);
            Assert.IsTrue(first.IsAlive && second.IsAlive);
            Assert.AreEqual(2, director.ActiveEnemyCount);
            Assert.AreEqual(2, director.TotalSpawnedCount);
            var damage = new DamageRequest(1, DamageSourceType.FlashlightPulse, Vector3.zero, Vector3.forward, 999f);
            first.ReceiveDamage(in damage);
            AdvanceDirector(1.2f);
            Assert.AreEqual(2, director.ActiveEnemyCount);
            Assert.AreEqual(1, director.LastGroupSpawnedCount);
            Assert.IsTrue(second.IsAlive);
        }

        [Test]
        public void GroundIsRequired_AndLowObstaclesAreNotMistakenForFloor()
        {
            director.BindSpawnContext(null);
            Assert.IsFalse(director.TryFindSpawnPosition(EnemyArchetypeId.Residual, out _));
            director.BindSpawnContext(floor);
            floor.enabled = false;
            Assert.IsFalse(director.TryFindSpawnPosition(EnemyArchetypeId.Residual, out _));
            floor.enabled = true;
            var low = GameObject.CreatePrimitive(PrimitiveType.Cube);
            low.transform.position = new Vector3(0f, 0.05f, 0f);
            low.transform.localScale = new Vector3(120f, 0.1f, 120f);
            Physics.SyncTransforms();
            Assert.IsFalse(director.TryFindSpawnPosition(EnemyArchetypeId.Residual, out _));
            Object.DestroyImmediate(low);
            Physics.SyncTransforms();
            Assert.IsTrue(director.TryFindSpawnPosition(EnemyArchetypeId.Residual, out _));
        }

        [Test]
        public void ColossusUsesShiftedCapsule_AndRejectsObstaclesAboveTheOldQuery()
        {
            var data = settings.GetArchetype(EnemyArchetypeId.Colossus);
            EnemySpawnGeometry geometry = enemies.GetSpawnGeometry(data);
            geometry.CapsuleAt(Vector3.up, out Vector3 bottom, out Vector3 top);
            Assert.AreEqual(0f, bottom.y - geometry.Radius, 0.001f);
            Assert.AreEqual(3.2f, top.y + geometry.Radius, 0.001f);
            EnemyActor actor = enemies.Spawn(data, new Vector3(45f, 1f, 45f));
            Assert.AreEqual(EnemySpawnGeometry.LocalColliderCenter(data), actor.BodyCollider.center);
            Assert.AreEqual(0f, actor.BodyCollider.bounds.min.y, 0.001f);
            Assert.AreEqual(3.2f, actor.BodyCollider.bounds.max.y, 0.001f);
            enemies.ReturnToPool(actor);
            var high = GameObject.CreatePrimitive(PrimitiveType.Cube);
            high.transform.position = new Vector3(0f, 3.05f, 0f);
            high.transform.localScale = new Vector3(120f, 0.1f, 120f);
            Physics.SyncTransforms();
            Assert.IsTrue(director.TryFindSpawnPosition(EnemyArchetypeId.Residual, out _));
            Assert.IsFalse(director.TryFindSpawnPosition(EnemyArchetypeId.Colossus, out _));
        }

        [TestCase(1.7777778f)]
        [TestCase(2.3333333f)]
        public void RootOutsideScreenDoesNotAllowVisibleColossusHead(float aspect)
        {
            camera.aspect = aspect;
            // A shallower view lets a head cross the bottom plane while its root remains outside.
            camera.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            var rootPlane = new Plane(Vector3.up, Vector3.up);
            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, -0.01f, 0f));
            Assert.IsTrue(rootPlane.Raycast(ray, out float distance));
            Vector3 root = ray.GetPoint(distance);
            Assert.Less(camera.WorldToViewportPoint(root).y, 0f);
            Vector3 headViewport = camera.WorldToViewportPoint(root + Vector3.up * 2.2f);
            Assert.That(headViewport.y, Is.InRange(0f, 1f));
            Assert.IsFalse(director.IsSpawnVolumeOutsideView(EnemyArchetypeId.Colossus, root));
            for (int i = 0; i < 16; i++)
            {
                Assert.IsTrue(director.TryFindSpawnPosition(EnemyArchetypeId.Colossus, out Vector3 position));
                Assert.IsTrue(director.IsSpawnVolumeOutsideView(EnemyArchetypeId.Colossus, position));
            }
        }

        [Test]
        public void SaturatedObstacleQueriesDiscardWholeGroup_AndRespectSearchBudget()
        {
            SetConstantGroup(4, EnemyArchetypeId.Residual);
            for (int i = 0; i < 33; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = Vector3.up;
                cube.transform.localScale = new Vector3(120f, 2f, 120f);
            }
            Physics.SyncTransforms();
            AdvanceDirector(1.2f);
            Assert.AreEqual(0, director.TotalSpawnedCount);
            Assert.AreEqual(4, director.Discarded(CombatWaveDirector.DiscardReason.QuerySaturated));
            Assert.AreEqual(0, director.ReservedCount);
            Assert.LessOrEqual(director.LastAnchorAttempts, 16);
            Assert.LessOrEqual(director.LastMemberValidations, 64);
        }

        [Test]
        public void PreparingVisualGeometryDoesNotDrawCombatRandomness()
        {
            var cold = new GameObject("ColdGeometryPool").AddComponent<EnemyPool>();
            cold.BindReferences(session, player, hazards);
            int before = session.Rng.Combat.SampleCount;
            cold.GetSpawnGeometry(settings.GetArchetype(EnemyArchetypeId.Colossus));
            cold.GetSpawnGeometry(settings.GetArchetype(EnemyArchetypeId.ExplosiveMoth));
            Assert.AreEqual(before, session.Rng.Combat.SampleCount);
            Assert.AreEqual(2, cold.TotalCreatedCount);
            Assert.AreEqual(0, director.ActiveEnemyCount);
        }

        private void SetConstantGroup(int size, EnemyArchetypeId id)
        {
            var weights = new EnemyWaveWeights(id == EnemyArchetypeId.Residual ? 100f : 0f,
                0f, id == EnemyArchetypeId.Colossus ? 100f : 0f, 0f, 0f);
            settings.Phases = new[] { new CombatWavePhase(0f, 1.2f, 1.2f, size, weights, weights) };
            session.Clock.SetGameplayTimeForTesting(0f);
            director.ResetForSession();
        }

        [Test]
        public void WarmPlacementSamplingReusesBuffersWithoutGrowingPoolsOrManagedHeap()
        {
            long probeBefore = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            byte[] probe = new byte[1048576];
            long probeSize = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - probeBefore;
            System.GC.KeepAlive(probe);
            Assert.GreaterOrEqual(probeSize, 1048576L, "Calibrate the native heap counter before trusting it.");
            for (int i = 0; i < 20; i++) director.TryFindSpawnPosition(EnemyArchetypeId.Colossus, out _);
            int created = enemies.TotalCreatedCount;
            int collections = System.GC.CollectionCount(0) + System.GC.CollectionCount(1) + System.GC.CollectionCount(2);
            long before = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            int found = 0;
            for (int i = 0; i < 1024; i++)
                if (director.TryFindSpawnPosition(EnemyArchetypeId.Colossus, out _)) found++;
            long growth = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - before;
            Assert.AreEqual(1024, found);
            Assert.AreEqual(created, enemies.TotalCreatedCount);
            Assert.AreEqual(collections, System.GC.CollectionCount(0) + System.GC.CollectionCount(1) + System.GC.CollectionCount(2),
                "A collection must not hide allocations during measurement.");
            Assert.LessOrEqual(growth, 2048L);
        }

        private void AdvanceDirector(float deltaTime)
        {
            session.Clock.AdvanceGameplayTime(deltaTime);
            director.Step();
        }

        [Test]
        public void DirectorUsesSessionClock_AndRepeatedEvaluationDoesNotAdvanceTimeOrSpawnAgain()
        {
            session.Clock.SetGameplayTimeForTesting(1.2f);
            director.Step();
            Assert.AreEqual(1.2f, director.Elapsed);
            Assert.AreEqual(1, director.GroupsAttemptedCount);
            director.Step();
            Assert.AreEqual(1.2f, session.GameplayTime);
            Assert.AreEqual(1, director.GroupsAttemptedCount);
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
