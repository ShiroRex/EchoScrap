using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class EnemyNavigationTests
    {
        private Scene scene;
        private PlayerHealth player;
        private CombatSession session;
        private readonly List<Collider> obstacles = new List<Collider>();
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            previousTimeScale = Time.timeScale;
            scene = SceneManager.CreateScene("Navigation_" + Random.Range(1, 100000));
            SceneManager.SetActiveScene(scene);
            // Earlier arena fixtures can leave CombatArena loaded. Additive test geometry would
            // then share its colliders, surface and globally discovered player/session references.
            Scene arena = SceneManager.GetSceneByName("CombatArena");
            if (arena.IsValid() && arena.isLoaded)
                yield return SceneManager.UnloadSceneAsync(arena);
            PhysicsLayers.EnsureConfigured();
            CombatTargetRegistry.Clear();
            obstacles.Clear();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "NavigationFloor";
            floor.transform.localScale = new Vector3(20f, 1f, 20f);
            var target = new GameObject("NavigationTarget");
            target.layer = PhysicsLayers.PlayerLayer;
            target.transform.position = new Vector3(0f, 1f, 10f);
            var rb = target.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var capsule = target.AddComponent<CapsuleCollider>();
            capsule.radius = 0.45f; capsule.height = 1.8f;
            target.AddComponent<PlayerRuntimeStats>().ResetToInitial();
            player = target.AddComponent<PlayerHealth>();
            session = new GameObject("NavigationSession").AddComponent<CombatSession>();
            player.BindSession(session);
            session.ResetSession(8731);
            session.ResumeFromFocusLoss();
            Assert.AreSame(player, session.PlayerHealth, "The session must own this fixture's player.");
            Assert.AreEqual(scene, player.gameObject.scene);
            Assert.AreEqual(scene, session.gameObject.scene);
            Time.timeScale = 5f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = previousTimeScale;
            CombatTargetRegistry.Clear();
            SceneManager.SetActiveScene(SceneManager.CreateScene("NavigationCleanup_" + Random.Range(1, 100000)));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator FrontalWallIsRoutedAround_WithoutOscillationOrPenetration()
        {
            AddWall(new Vector3(0f, 2f, 2f), new Vector3(8f, 4f, 0.6f));
            var enemy = Spawn(new Vector3(0f, 1f, -4f));
            float maximumSide = 0f;
            for (int step = 0; step < 500 && PlanarDistance(enemy.Body.position, player.transform.position) > 1.5f; step++)
            {
                yield return new WaitForFixedUpdate();
                maximumSide = Mathf.Max(maximumSide, Mathf.Abs(enemy.Body.position.x));
                if (step % 10 == 0) AssertNoPenetration(enemy);
            }
            Assert.Greater(maximumSide, 4.35f, "A frontal wall requires choosing and retaining a side.");
            Assert.Less(PlanarDistance(enemy.Body.position, player.transform.position), 1.6f,
                "The enemy must finish the detour and resume pursuit.");
        }

        [UnityTest]
        public IEnumerator UShapedStructureOnHill_LeavesDeadEndBeforePursuingTarget()
        {
            GameObject.Find("NavigationFloor").AddComponent<ArenaSurface>();
            Vector3 origin = new Vector3(-24f, 0f, 3f);
            // The cap faces the target; an enemy inside must initially travel away from it.
            AddWall(ArenaSurface.OnGround(origin + new Vector3(-3f, 0f, 3f), 3f), new Vector3(0.6f, 6f, 6.6f));
            AddWall(ArenaSurface.OnGround(origin + new Vector3(3f, 0f, 3f), 3f), new Vector3(0.6f, 6f, 6.6f));
            AddWall(ArenaSurface.OnGround(origin + new Vector3(0f, 0f, 6f), 3f), new Vector3(6.6f, 6f, 0.6f));
            player.GetComponent<Rigidbody>().position = ArenaSurface.OnGround(origin + Vector3.forward * 11f, 1f);
            player.transform.position = player.GetComponent<Rigidbody>().position;
            var enemy = Spawn(origin + Vector3.forward * 4f);
            Assert.Greater(enemy.Body.position.y, 4f, "This regression must exercise obstacle sensing above y=1.");
            float minimumZ = enemy.Body.position.z;
            float maximumSide = 0f;
            for (int step = 0; step < 900 && PlanarDistance(enemy.Body.position, player.transform.position) > 1.5f; step++)
            {
                yield return new WaitForFixedUpdate();
                minimumZ = Mathf.Min(minimumZ, enemy.Body.position.z);
                maximumSide = Mathf.Max(maximumSide, Mathf.Abs(enemy.Body.position.x - origin.x));
                if (step % 10 == 0) AssertNoPenetration(enemy);
            }
            Debug.Log("[Navigation U] final=" + enemy.Body.position + " target=" + player.transform.position
                + " minimumZ=" + minimumZ + " maximumSide=" + maximumSide + " " + DescribeRoute(enemy));
            Assert.Less(minimumZ, origin.z - 0.5f, "Must leave through the open end of the U.");
            Assert.Greater(maximumSide, 3.6f, "Must pass outside a side wall rather than through it.");
            Assert.Less(PlanarDistance(enemy.Body.position, player.transform.position), 1.6f,
                "Must reach the target after exiting the dead end.");
            Assert.AreEqual(ArenaSurface.SupportHeight(enemy.Body.position, enemy.HitRadius) + 1f,
                enemy.Body.position.y, 0.3f, "Routing must retain terrain support.");
        }

        [UnityTest]
        public IEnumerator OpenDoorwayIsTraversed_AndOverheadBeamIsNotTreatedAsSolidWall()
        {
            AddWall(new Vector3(-3f, 2f, 2f), new Vector3(3f, 4f, 0.6f));
            AddWall(new Vector3(3f, 2f, 2f), new Vector3(3f, 4f, 0.6f));
            AddWall(new Vector3(0f, 3.5f, 2f), new Vector3(3f, 1f, 0.6f));
            var enemy = Spawn(new Vector3(0f, 1f, -4f));
            float maximumSide = 0f;
            for (int step = 0; step < 300 && PlanarDistance(enemy.Body.position, player.transform.position) > 1.5f; step++)
            {
                yield return new WaitForFixedUpdate();
                maximumSide = Mathf.Max(maximumSide, Mathf.Abs(enemy.Body.position.x));
                if (step % 10 == 0) AssertNoPenetration(enemy);
            }
            Assert.Less(maximumSide, 0.3f, "Use the visible doorway with adequate headroom.");
            Assert.Less(PlanarDistance(enemy.Body.position, player.transform.position), 1.6f);
        }

        private EnemyActor Spawn(Vector3 position)
        {
            Physics.SyncTransforms();
            var enemy = new GameObject("NavigationEnemy").AddComponent<EnemyActor>();
            var data = EnemyArchetypeData.CreateDefault(EnemyArchetypeId.Residual);
            data.SpeedVarianceFraction = 0f; data.ContactDamage = 0f;
            enemy.InitializeOrResetFromPool(data, session, player, position);
            return enemy;
        }

        private void AddWall(Vector3 position, Vector3 size)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "NavigationObstacle";
            wall.transform.position = position;
            wall.transform.localScale = size;
            obstacles.Add(wall.GetComponent<Collider>());
        }

        private void AssertNoPenetration(EnemyActor enemy)
        {
            foreach (Collider obstacle in obstacles)
                if (Physics.ComputePenetration(enemy.BodyCollider, enemy.Body.position, enemy.Body.rotation,
                    obstacle, obstacle.transform.position, obstacle.transform.rotation, out _, out float distance))
                    Assert.Less(distance, 0.035f, "Navigation must not push or teleport through a wall.");
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        { a.y = b.y = 0f; return Vector3.Distance(a, b); }

        private static string DescribeRoute(EnemyActor enemy)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var navigator = (EnemyObstacleNavigator)typeof(EnemyActor).GetField("obstacleNavigator", flags).GetValue(enemy);
            var route = (Vector3[])typeof(EnemyObstacleNavigator).GetField("route", flags).GetValue(navigator);
            int count = (int)typeof(EnemyObstacleNavigator).GetField("routeCount", flags).GetValue(navigator);
            int index = (int)typeof(EnemyObstacleNavigator).GetField("routeIndex", flags).GetValue(navigator);
            var text = new StringBuilder("route " + index + "/" + count + ": ");
            for (int i = 0; i < count; i++) text.Append(route[i]).Append(" ");
            return text.ToString();
        }
    }
}
