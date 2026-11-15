using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class NavigationBudgetTests
    {
        private Scene scene;
        private Scene previousScene;
        private PlayerHealth player;
        private CombatSession session;
        private readonly List<EnemyActor> spawnedEnemies = new List<EnemyActor>();
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            previousTimeScale = Time.timeScale;
            previousScene = SceneManager.GetActiveScene();
            scene = SceneManager.CreateScene("NavBudget_" + UnityEngine.Random.Range(1, 100000));
            SceneManager.SetActiveScene(scene);
            Scene arena = SceneManager.GetSceneByName("CombatArena");
            if (arena.IsValid() && arena.isLoaded)
                yield return SceneManager.UnloadSceneAsync(arena);

            PhysicsLayers.EnsureConfigured();
            CombatTargetRegistry.Clear();
            spawnedEnemies.Clear();

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "NavBudgetFloor";
            floor.transform.localScale = new Vector3(24f, 1f, 24f);

            var target = new GameObject("Target");
            target.layer = PhysicsLayers.PlayerLayer;
            target.transform.position = new Vector3(0f, 1f, 18f);
            target.AddComponent<Rigidbody>().isKinematic = true;
            var tc = target.AddComponent<CapsuleCollider>();
            tc.radius = 0.45f;
            tc.height = 1.8f;
            target.AddComponent<PlayerRuntimeStats>().ResetToInitial();
            player = target.AddComponent<PlayerHealth>();
            session = new GameObject("Session").AddComponent<CombatSession>();
            player.BindSession(session);
            session.ResetSession(20261022);
            session.ResumeFromFocusLoss();

            Physics.SyncTransforms();
            EnemyObstacleNavigator.ResetTelemetry();
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = previousTimeScale;
            for (int i = 0; i < spawnedEnemies.Count; i++)
            {
                if (spawnedEnemies[i] != null)
                    UnityEngine.Object.Destroy(spawnedEnemies[i].gameObject);
            }
            spawnedEnemies.Clear();
            CombatTargetRegistry.Clear();
            if (previousScene.IsValid() && previousScene.isLoaded)
                SceneManager.SetActiveScene(previousScene);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator MultipleFixedUpdatesInSameFrame_NeverExceed1024QueryBudget()
        {
            // Place a wide blocking wall so every enemy performs multi-probe avoidance and triggers planning demand.
            AddWall(new Vector3(0f, 2f, 6f), new Vector3(40f, 4f, 0.8f));

            for (int i = 0; i < 160; i++)
            {
                float x = ((i % 20) - 9.5f) * 1.4f;
                float z = -2f - (i / 20) * 1.5f;
                SpawnEnemy(new Vector3(x, 1f, z));
            }
            Physics.SyncTransforms();

            // Let 1 FixedUpdate run to register initial intents, then step into a fresh render frame
            // with Time.timeScale = 0 so background FixedUpdates cannot pre-consume this frame's budget.
            yield return new WaitForFixedUpdate();
            Time.timeScale = 0f;
            yield return null;
            EnemyObstacleNavigator.ResetTelemetry();
            Assert.AreEqual(0, EnemyObstacleNavigator.CaptureTelemetry().CurrentFrameTotalQueries,
                "Fresh paused frame should begin with 0 consumed budget queries.");
            long queriesBefore = EnemyObstacleNavigator.CaptureTelemetry().TotalQueries;

            // Simulate 5 physics steps within the SAME Time.frameCount (as occurs after a slow frame).
            MethodInfo stepPhysics = typeof(EnemyObstacleNavigator).GetMethod(
                "StepCoordinatorPhysics", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(stepPhysics);

            for (int subStep = 0; subStep < 5; subStep++)
            {
                stepPhysics.Invoke(null, null);
                for (int i = 0; i < spawnedEnemies.Count; i++)
                    spawnedEnemies[i].StepPhysics(0.02f);

                EnemyObstacleNavigator.NavigationTelemetry mid = EnemyObstacleNavigator.CaptureTelemetry();
                Assert.LessOrEqual(mid.CurrentFrameTotalQueries, EnemyObstacleNavigator.TotalQueriesPerFrame,
                    $"CurrentFrameTotalQueries exceeded 1024 on subStep {subStep}");
            }

            Time.timeScale = 1f;
            long queriesInFrame = EnemyObstacleNavigator.CaptureTelemetry().TotalQueries - queriesBefore;
            Assert.LessOrEqual(queriesInFrame, EnemyObstacleNavigator.TotalQueriesPerFrame,
                $"Total queries executed in a single frame across 5 FixedUpdates was {queriesInFrame}, exceeding 1024.");
            Assert.Greater(queriesInFrame, 0L, "Expected navigation queries to actually execute.");
        }

        [UnityTest]
        public IEnumerator PendingBudgetExhaustion_DoesNotMarkGridEdgesBlocked_AndResumesCleanly()
        {
            // Build a narrow U-trap requiring A* grid discovery + multiple NearestWalkable / Edge checks.
            AddWall(new Vector3(0f, 2f, 5f), new Vector3(12f, 4f, 0.8f));
            AddWall(new Vector3(-6f, 2f, 2f), new Vector3(0.8f, 4f, 6f));
            AddWall(new Vector3(6f, 2f, 2f), new Vector3(0.8f, 4f, 6f));

            // Yield before spawning hero so no prior FixedUpdate can pre-verify a segment for hero.
            yield return null;

            var hero = SpawnEnemy(new Vector3(0f, 1f, 3.8f));
            Physics.SyncTransforms();

            FieldInfo frameImmediateField = typeof(EnemyObstacleNavigator).GetField(
                "frameImmediateUsed", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo framePlanningField = typeof(EnemyObstacleNavigator).GetField(
                "framePlanningUsed", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo budgetFrameField = typeof(EnemyObstacleNavigator).GetField(
                "budgetFrame", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(frameImmediateField);
            Assert.IsNotNull(framePlanningField);
            Assert.IsNotNull(budgetFrameField);

            budgetFrameField.SetValue(null, Time.frameCount);
            frameImmediateField.SetValue(null, EnemyObstacleNavigator.ImmediateReservedPerFrame);
            framePlanningField.SetValue(null, EnemyObstacleNavigator.PlanningReservedPerFrame);

            // Force hero into a Pending state: must return zero velocity when no verified stretch exists.
            hero.StepPhysics(0.02f);
            Assert.AreEqual(Vector3.zero, hero.Velocity,
                "When Pending with no prior verified stretch, enemy must not reuse an unverified direction.");

            // Check grid edges were NOT wrongly marked blocked when budget exhausted.
            var nav = GetNavigator(hero);
            var gridObj = typeof(EnemyObstacleNavigator).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(nav);
            var blockedEdges = (byte[])gridObj.GetType().GetField("blockedEdges", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gridObj);
            int nonZeroBlocked = 0;
            for (int i = 0; i < blockedEdges.Length; i++)
                if (blockedEdges[i] != 0) nonZeroBlocked++;
            Assert.AreEqual(0, nonZeroBlocked, "Exhausted budget (Pending) must never mark static edges as blocked.");

            // Advance normal frames: resumable checks + round-robin must complete A* and guide hero around U-trap.
            Time.timeScale = 5f;
            float minZ = hero.Body.position.z;
            float maxSide = 0f;
            for (int i = 0; i < 650 && Vector2.Distance(new Vector2(hero.Body.position.x, hero.Body.position.z), new Vector2(0f, 18f)) > 2.2f; i++)
            {
                yield return new WaitForFixedUpdate();
                minZ = Mathf.Min(minZ, hero.Body.position.z);
                maxSide = Mathf.Max(maxSide, Mathf.Abs(hero.Body.position.x));
            }
            Assert.Less(minZ, 0.5f, "Hero should exit the south opening of the U-trap after budget recovery.");
            Assert.Greater(maxSide, 6.2f, "Hero should route cleanly outside the side walls.");
        }

        [UnityTest]
        public IEnumerator RoundRobin_UnderBudgetContention_NeverStarvesTailEnemies_AndDespawnClearsPendingRequests()
        {
            // Place a full-width blocking wall right in front of 220 enemies so every enemy requires
            // direct check + multi-angle wall-side probes, overflowing 1024 queries on single steps.
            AddWall(new Vector3(0f, 2f, 2f), new Vector3(48f, 4f, 0.8f));
            for (int i = 0; i < 220; i++)
                SpawnEnemy(new Vector3(((i % 22) - 10.5f) * 1.5f, 1f, 0.5f - (i / 22) * 0.4f));
            Physics.SyncTransforms();

            yield return null;

            FieldInfo stepIdField = typeof(EnemyObstacleNavigator).GetField(
                "currentPhysicsStepId", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo resolvedStepField = typeof(EnemyObstacleNavigator).GetField(
                "resolvedStepId", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(stepIdField);
            Assert.IsNotNull(resolvedStepField);

            var resolvedByActor = new bool[spawnedEnemies.Count];
            for (int frame = 0; frame < 30; frame++)
            {
                yield return null;
                yield return new WaitForFixedUpdate();
                int curStep = (int)stepIdField.GetValue(null);
                for (int i = 0; i < spawnedEnemies.Count; i++)
                {
                    if ((int)resolvedStepField.GetValue(GetNavigator(spawnedEnemies[i])) == curStep)
                        resolvedByActor[i] = true;
                }
            }

            int unserved = 0;
            for (int i = 0; i < resolvedByActor.Length; i++)
                if (!resolvedByActor[i]) unserved++;
            Assert.AreEqual(0, unserved, "Round-robin scheduling must give turns to all active enemies including the tail.");

            // Despawn / deactivate all enemies -> all pending requests and active registrations must clear.
            for (int i = 0; i < spawnedEnemies.Count; i++)
                spawnedEnemies[i].gameObject.SetActive(false);

            yield return null;
            Assert.AreEqual(0, EnemyObstacleNavigator.ActiveNavigatorCount,
                "Deactivating/despawning enemies must unregister navigators.");
            Assert.AreEqual(0, EnemyObstacleNavigator.CaptureTelemetry().PendingRequests,
                "Despawning enemies must cancel all pending navigation requests.");
        }

        [UnityTest]
        public IEnumerator VerifiedSegmentFallback_MovesSafelyWhenPending_AndStopsAfterObstacleInvalidation()
        {
            yield return null;
            var hero = SpawnEnemy(new Vector3(0f, 1f, 0f));
            Physics.SyncTransforms();

            // Step synchronously once under full budget so hero verifies its forward segment [0,0,0 -> 0,0,1.1+].
            hero.StepPhysics(0.02f);
            Assert.Greater(hero.Velocity.z, 0.5f, "Open forward probe should allow movement.");

            // Exhaust frame budget intentionally on next step: hero has verified stretch so it may advance safely.
            FieldInfo frameImmediateField = typeof(EnemyObstacleNavigator).GetField(
                "frameImmediateUsed", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo framePlanningField = typeof(EnemyObstacleNavigator).GetField(
                "framePlanningUsed", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo budgetFrameField = typeof(EnemyObstacleNavigator).GetField(
                "budgetFrame", BindingFlags.Static | BindingFlags.NonPublic);
            budgetFrameField.SetValue(null, Time.frameCount);
            frameImmediateField.SetValue(null, EnemyObstacleNavigator.ImmediateReservedPerFrame);
            framePlanningField.SetValue(null, EnemyObstacleNavigator.PlanningReservedPerFrame);

            hero.StepPhysics(0.02f);
            Assert.Greater(hero.Velocity.z, 0.5f,
                "Pending check inside an already-verified segment must retain safe forward movement without freezing.");

            // Invalidate static obstacles (e.g. dynamic barrier toggled) while still out of budget:
            // previously verified stretch must immediately become invalid and yield Vector3.zero!
            EnemyObstacleNavigator.InvalidateStaticObstacles();
            budgetFrameField.SetValue(null, Time.frameCount);
            frameImmediateField.SetValue(null, EnemyObstacleNavigator.ImmediateReservedPerFrame);
            framePlanningField.SetValue(null, EnemyObstacleNavigator.PlanningReservedPerFrame);

            hero.StepPhysics(0.02f);
            Assert.AreEqual(Vector3.zero, hero.Velocity,
                "After obstacle invalidation under Pending budget, navigator must return zero rather than stale clearance.");
        }

        private EnemyActor SpawnEnemy(Vector3 position)
        {
            var go = new GameObject("NavBudgetEnemy_" + spawnedEnemies.Count);
            var enemy = go.AddComponent<EnemyActor>();
            var data = EnemyArchetypeData.CreateDefault(EnemyArchetypeId.Residual);
            data.SpeedVarianceFraction = 0f;
            data.ContactDamage = 0f;
            enemy.InitializeOrResetFromPool(data, session, player, position);
            spawnedEnemies.Add(enemy);
            return enemy;
        }

        private static EnemyObstacleNavigator GetNavigator(EnemyActor enemy)
        {
            return (EnemyObstacleNavigator)typeof(EnemyActor)
                .GetField("obstacleNavigator", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(enemy);
        }

        private static void AddWall(Vector3 position, Vector3 size)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "NavBudgetObstacle";
            wall.transform.position = position;
            wall.transform.localScale = size;
        }
    }
}
