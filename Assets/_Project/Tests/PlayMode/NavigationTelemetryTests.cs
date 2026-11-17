using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class NavigationTelemetryTests
    {
        private Scene scene;
        private Scene previousScene;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            previousScene = SceneManager.GetActiveScene();
            scene = SceneManager.CreateScene("NavTelemetry_" + Random.Range(1, 100000));
            SceneManager.SetActiveScene(scene);
            PhysicsLayers.EnsureConfigured();
            CombatTargetRegistry.Clear();
            EnemyObstacleNavigator.ResetTelemetry();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            CombatTargetRegistry.Clear();
            if (previousScene.IsValid() && previousScene.isLoaded)
                SceneManager.SetActiveScene(previousScene);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator Steer_RecordsQueryCountsPendingAndNavigationTime()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(10f, 1f, 10f);

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 1.5f, 1.5f);
            wall.transform.localScale = new Vector3(8f, 3f, 0.6f);

            var target = new GameObject("Target");
            target.layer = PhysicsLayers.PlayerLayer;
            target.transform.position = new Vector3(0f, 1f, 8f);
            target.AddComponent<Rigidbody>().isKinematic = true;
            var tc = target.AddComponent<CapsuleCollider>();
            tc.radius = 0.45f; tc.height = 1.8f;
            target.AddComponent<PlayerRuntimeStats>().ResetToInitial();
            var player = target.AddComponent<PlayerHealth>();
            var session = new GameObject("Session").AddComponent<CombatSession>();
            player.BindSession(session);
            session.ResetSession(20261022);
            session.ResumeFromFocusLoss();

            Physics.SyncTransforms();
            var enemy = new GameObject("Enemy").AddComponent<EnemyActor>();
            var data = EnemyArchetypeData.CreateDefault(EnemyArchetypeId.Residual);
            data.SpeedVarianceFraction = 0f;
            enemy.InitializeOrResetFromPool(data, session, player, new Vector3(0f, 1f, -3f));

            EnemyObstacleNavigator.ResetTelemetry();
            for (int i = 0; i < 15; i++)
                yield return new WaitForFixedUpdate();

            EnemyObstacleNavigator.NavigationTelemetry telemetry = EnemyObstacleNavigator.CaptureTelemetry();
            Assert.Greater(telemetry.TotalQueries, 0L, "Expected physics queries to be counted.");
            Assert.Greater(telemetry.OverlapBoxQueries + telemetry.OverlapCapsuleQueries, 0L);
            Assert.Greater(telemetry.TotalNavigationMs, 0.0);
            Assert.GreaterOrEqual(telemetry.MaxRequestAgeMs, 0.0);

            EnemyObstacleNavigator.ResetTelemetry();
            EnemyObstacleNavigator.NavigationTelemetry afterReset = EnemyObstacleNavigator.CaptureTelemetry();
            Assert.AreEqual(0L, afterReset.TotalQueries);
            Assert.AreEqual(0.0, afterReset.TotalNavigationMs, 0.0001);
        }
    }
}
