using System.Collections;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class EnemyMothTests
    {
        private Scene scene;
        private CombatSession session;
        private PlayerHealth player;
        private EnemyPool enemies;
        private MothExplosionPool explosions;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            PhysicsLayers.EnsureConfigured(); CombatTargetRegistry.Clear();
            scene = SceneManager.CreateScene("Moth " + Random.Range(10000, 99999));
            SceneManager.SetActiveScene(scene);
            session = new GameObject("Session").AddComponent<CombatSession>();
            var go = new GameObject("Player"); go.transform.position = Vector3.up;
            go.layer = PhysicsLayers.PlayerLayer;
            var body = go.AddComponent<Rigidbody>(); body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            go.AddComponent<CapsuleCollider>().radius = 0.45f;
            go.AddComponent<PlayerRuntimeStats>(); go.AddComponent<ActorVisualComposer>();
            player = go.AddComponent<PlayerHealth>(); player.BindSession(session);
            session.ResolvePlayerReferences(); session.ResetSession(8181);
            enemies = new GameObject("Enemies").AddComponent<EnemyPool>(); enemies.BindReferences(session, player);
            explosions = MothExplosionPool.GetOrCreate(session, player); explosions.AutoTick = false;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f; CombatTargetRegistry.Clear();
            if (SceneManager.sceneCount <= 1)
                SceneManager.SetActiveScene(SceneManager.CreateScene("Moth cleanup " + Random.Range(10000, 99999)));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        private EnemyActor Spawn(Vector3 position, EnemyArchetypeId id = EnemyArchetypeId.ExplosiveMoth)
        {
            EnemyActor actor = enemies.Spawn(id, position);
            return actor;
        }

        private static DamageRequest Lethal() => new DamageRequest(555, DamageSourceType.FlashlightPulse,
            Vector3.up, Vector3.forward, 100f);

        [Test]
        public void ProximityArmsOnce_StopsPursuit_AndWaitsHalfSecond()
        {
            EnemyActor moth = Spawn(new Vector3(0f, 1f, 1.9f));
            Assert.AreEqual(12f, moth.MaxHealth);
            Assert.That(moth.AssignedMoveSpeed, Is.InRange(4.32f, 5.28f));
            Assert.AreEqual(18f, moth.Archetype.KnockbackBraking);
            moth.StepPhysics(0.02f);
            Assert.IsTrue(moth.IsFuseActive); Assert.AreEqual(0.5f, moth.FuseTimer, 0.0001f);
            Assert.That(moth.Velocity.sqrMagnitude, Is.LessThan(0.0001f));
            moth.StepPhysics(0.49f); explosions.FlushExplosions();
            Assert.IsTrue(moth.IsAlive); Assert.AreEqual(100f, player.CurrentHealth);
            moth.StepPhysics(0.01f); explosions.FlushExplosions();
            Assert.IsFalse(moth.IsAlive); Assert.AreEqual(65f, player.CurrentHealth);
            Assert.AreEqual(1, explosions.TotalExplosions);
        }

        [Test]
        public void LethalPulseArmsInsteadOfDying_FourthPulseStillPushesArmedZeroHpBody()
        {
            EnemyActor moth = Spawn(new Vector3(0f, 1f, 3f));
            var weapon = player.gameObject.AddComponent<FlashlightWeapon>();
            weapon.BindSession(session); weapon.enabled = false; weapon.ResetWeaponCycle();
            for (int i = 0; i < 4; i++) weapon.StepWeapon(0.2f);
            Assert.AreEqual(0f, moth.CurrentHealth); Assert.IsTrue(moth.IsAlive); Assert.IsTrue(moth.IsFuseActive);
            Assert.AreEqual(0, moth.DeathNotifyCount); Assert.AreEqual(1, moth.KnockbackAppliedCount);
            Assert.AreEqual(10f, moth.KnockbackVelocity.magnitude, 0.001f);
            moth.StepPhysics(0.2f); float remaining = moth.FuseTimer;
            weapon.StepWeapon(1.2f);
            Assert.AreEqual(remaining, moth.FuseTimer, 0.0001f);
            Assert.AreEqual(2, moth.KnockbackAppliedCount, "Otro cuarto pulso puede empujar la Polilla ya armada.");
        }

        [Test]
        public void ExplosionHurtsBothFactions_AccountsForColliderEdge_AndDoesNotReachOutside()
        {
            EnemyActor moth = Spawn(new Vector3(0f, 1f, 2f));
            EnemyActor colossus = Spawn(new Vector3(0f, 1f, 5.2f), EnemyArchetypeId.Colossus);
            EnemyActor far = Spawn(new Vector3(0f, 1f, 8f), EnemyArchetypeId.Residual);
            moth.ArmMoth(); moth.StepPhysics(0.5f); explosions.FlushExplosions();
            Assert.AreEqual(65f, player.CurrentHealth);
            Assert.AreEqual(45f, colossus.CurrentHealth, 0.001f);
            Assert.AreEqual(15f, far.CurrentHealth);
            Assert.AreEqual(1, moth.DeathNotifyCount);
        }

        [Test]
        public void ChainDetonationArmsNeighbour_WithoutRecursionOrDuplicateRewards()
        {
            int deaths = 0, xp = 0;
            session.Events.OnActorDied += death => { deaths++; xp += death.XpReward; };
            EnemyActor first = Spawn(new Vector3(0f, 1f, 2.5f));
            EnemyActor second = Spawn(new Vector3(0f, 1f, 4.5f));
            first.ArmMoth(); first.StepPhysics(0.5f); explosions.FlushExplosions();
            Assert.AreEqual(1, explosions.TotalExplosions); Assert.AreEqual(1, deaths);
            Assert.IsTrue(second.IsFuseActive); Assert.AreEqual(0.5f, second.FuseTimer, 0.0001f);
            Assert.IsTrue(second.IsAlive); Assert.AreEqual(0, second.DeathNotifyCount);
            second.StepPhysics(0.5f); explosions.FlushExplosions(); explosions.FlushExplosions();
            Assert.AreEqual(2, deaths); Assert.AreEqual(30, xp); Assert.AreEqual(2, explosions.TotalExplosions);
        }

        [Test]
        public void DashInvulnerabilityProtectsFromExplosion()
        {
            player.SetDashInvulnerable(true);
            EnemyActor moth = Spawn(new Vector3(0f, 1f, 2f));
            moth.ArmMoth(); moth.StepPhysics(0.5f); explosions.FlushExplosions();
            Assert.AreEqual(100f, player.CurrentHealth);
            Assert.IsFalse(moth.IsAlive);
        }

        [Test]
        public void PauseFreezesFuseAndQueuedDamage_AndResetClearsBlastQueue()
        {
            EnemyActor moth = Spawn(new Vector3(0f, 1f, 2f)); moth.ArmMoth();
            session.SetManualPause(true); moth.StepPhysics(0.4f);
            Assert.AreEqual(0.5f, moth.FuseTimer);
            explosions.Enqueue(1, player.WorldCenter); explosions.FlushExplosions();
            Assert.AreEqual(100f, player.CurrentHealth); Assert.AreEqual(1, explosions.PendingCount);
            session.ResetSession(); Assert.AreEqual(0, explosions.PendingCount);
        }

        [Test]
        public void ReusedActorRestoresHealthFuseColliderAndDangerPresentation()
        {
            EnemyActor moth = Spawn(new Vector3(0f, 1f, 3f)); moth.ArmMoth();
            Assert.IsTrue(moth.GetComponent<MothFusePresentation>().IsVisible);
            enemies.ReturnToPool(moth);
            EnemyActor reused = enemies.Spawn(EnemyArchetypeId.ExplosiveMoth, new Vector3(5f, 1f, 0f));
            Assert.AreSame(moth, reused); Assert.AreEqual(12f, reused.CurrentHealth);
            Assert.IsFalse(reused.IsFuseActive); Assert.AreEqual(0f, reused.FuseTimer);
            Assert.IsTrue(reused.BodyCollider.enabled);
            Assert.IsFalse(reused.GetComponent<MothFusePresentation>().IsVisible);
        }

        [UnityTest]
        public IEnumerator FusePreviewKeepsDangerVisibleWithReducedEffects()
        {
            session.Effects.ReducedEffectsMode = true;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.localScale = Vector3.one * 5f;
            var light = new GameObject("Light").AddComponent<Light>(); light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            EnemyActor moth = Spawn(new Vector3(1f, 1f, 3f)); moth.ArmMoth(); moth.StepPhysics(0.3f);
            Assert.IsTrue(moth.GetComponent<MothFusePresentation>().IsVisible);
            Camera camera = new GameObject("Preview camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 11f, -5f); camera.transform.LookAt(new Vector3(0f, 0f, 2f));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.08f, 0.1f, 0.14f);
            var target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
            yield return null; yield return null; yield return null;
            camera.Render(); RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var capture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            capture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); capture.Apply();
            string directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "EnemyMoth");
            Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "Preview.png"), capture.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.DestroyImmediate(capture); Object.DestroyImmediate(target);
        }
    }
}
