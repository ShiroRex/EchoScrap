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
        public void ProximityArmsOnce_StopsPursuit_AndUsesPrototypeNineTenthsFuse()
        {
            EnemyActor moth = Spawn(new Vector3(0f, 1f, 1.9f));
            Assert.AreEqual(12f, moth.MaxHealth);
            Assert.That(moth.AssignedMoveSpeed, Is.InRange(4.32f, 5.28f));
            Assert.AreEqual(18f, moth.Archetype.KnockbackBraking);
            moth.StepPhysics(0.02f);
            Assert.IsTrue(moth.IsFuseActive); Assert.AreEqual(0.9f, moth.FuseTimer, 0.0001f);
            Assert.That(moth.Velocity.sqrMagnitude, Is.LessThan(0.0001f));
            moth.StepPhysics(0.89f); explosions.FlushExplosions();
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
            moth.ArmMoth(); moth.StepPhysics(0.9f); explosions.FlushExplosions();
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
            first.ArmMoth(); first.StepPhysics(0.9f); explosions.FlushExplosions();
            Assert.AreEqual(1, explosions.TotalExplosions); Assert.AreEqual(1, deaths);
            Assert.IsTrue(second.IsFuseActive); Assert.AreEqual(1.2f, second.FuseTimer, 0.0001f);
            Assert.IsTrue(second.IsAlive); Assert.AreEqual(0, second.DeathNotifyCount);
            second.StepPhysics(1.2f); explosions.FlushExplosions(); explosions.FlushExplosions();
            Assert.AreEqual(2, deaths); Assert.AreEqual(30, xp); Assert.AreEqual(2, explosions.TotalExplosions);
        }

        [Test]
        public void DashInvulnerabilityProtectsFromExplosion()
        {
            player.SetDashInvulnerable(true);
            EnemyActor moth = Spawn(new Vector3(0f, 1f, 2f));
            moth.ArmMoth(); moth.StepPhysics(0.9f); explosions.FlushExplosions();
            Assert.AreEqual(100f, player.CurrentHealth);
            Assert.IsFalse(moth.IsAlive);
        }

        [Test]
        public void PauseFreezesFuseAndQueuedDamage_AndResetClearsBlastQueue()
        {
            EnemyActor moth = Spawn(new Vector3(0f, 1f, 2f)); moth.ArmMoth();
            session.SetManualPause(true); moth.StepPhysics(0.4f);
            Assert.AreEqual(0.9f, moth.FuseTimer);
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

        [Test]
        public void ThreeMothsChainThroughIndependentDeathFuses_AndAwardEachDeathOnce()
        {
            EnemyActor first = Spawn(new Vector3(10f, 1f, 0f));
            EnemyActor second = Spawn(new Vector3(12f, 1f, 0f));
            EnemyActor third = Spawn(new Vector3(14f, 1f, 0f));
            int deaths = 0, xp = 0;
            session.Events.OnActorDied += death => { deaths++; xp += death.XpReward; };
            first.ArmMoth(); first.StepPhysics(0.9f); explosions.FlushExplosions();
            Assert.AreEqual(1, deaths); Assert.IsTrue(second.IsFuseActive);
            Assert.AreEqual(1.2f, second.FuseTimer, 0.001f);
            Assert.IsFalse(third.IsFuseActive);
            Assert.Greater(second.KnockbackVelocity.magnitude, 0f);
            second.StepPhysics(1.19f); explosions.FlushExplosions();
            Assert.AreEqual(1, deaths);
            second.StepPhysics(0.01f); explosions.FlushExplosions();
            Assert.AreEqual(2, deaths); Assert.IsTrue(third.IsFuseActive);
            third.StepPhysics(1.2f); explosions.FlushExplosions(); explosions.FlushExplosions();
            Assert.AreEqual(3, deaths); Assert.AreEqual(45, xp);
            Assert.AreEqual(3, explosions.TotalExplosions);
        }

        [Test]
        public void PrototypeExplosionPushesSurvivorsWithRadialFalloff_AndRespectsHeavyImmunity()
        {
            var durable = EnemyArchetypeData.CreateDefault(EnemyArchetypeId.Residual);
            durable.MaxHealth = 100f;
            EnemyActor near = enemies.Spawn(durable, new Vector3(21f, 1f, 0f));
            EnemyActor far = enemies.Spawn(durable, new Vector3(22.5f, 1f, 0f));
            EnemyActor heavy = Spawn(new Vector3(20f, 1f, 2f), EnemyArchetypeId.Colossus);
            int combatSamples = session.Rng.Combat.SampleCount;
            explosions.Enqueue(999, new Vector3(20f, 1f, 0f)); explosions.FlushExplosions();
            Assert.AreEqual(65f, near.CurrentHealth); Assert.AreEqual(65f, far.CurrentHealth);
            Assert.AreEqual(6.4f, near.KnockbackVelocity.x, 0.001f);
            Assert.AreEqual(4f, far.KnockbackVelocity.x, 0.001f);
            Assert.AreEqual(Vector3.zero, heavy.KnockbackVelocity);
            Assert.AreEqual(combatSamples, session.Rng.Combat.SampleCount, "Visual particles do not consume combat randomness.");
        }

        [Test]
        public void SaturatedExplosionVisualsReuseInstances_WithoutDroppingGameplayOrLeakingAcrossReset()
        {
            int objects = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            for (int i = 0; i < 80; i++) explosions.Enqueue(i + 100, new Vector3(30f, 1f, 30f));
            explosions.FlushExplosions();
            Assert.AreEqual(80, explosions.TotalExplosions);
            Assert.AreEqual(0, explosions.PendingCount);
            Assert.AreEqual(32, explosions.ActiveVisualCount);
            Assert.AreEqual(objects, Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            session.Effects.ReducedEffectsMode = true;
            Assert.AreEqual(0, explosions.ActiveVisualCount);
            session.ResetSession();
            Assert.AreEqual(0, explosions.ActiveVisualCount);
            Assert.AreEqual(0, explosions.TotalExplosions);
        }

        [UnityTest]
        public IEnumerator PrototypeExplosionPreviewShowsFireSparksSmokeAndNeighbourFuse()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = Vector3.one * 8f;
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.color = new Color(0.12f, 0.16f, 0.21f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var light = new GameObject("Cold fill").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 0.35f;
            light.color = new Color(0.65f, 0.78f, 1f);
            light.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            player.SetDashInvulnerable(true);
            EnemyActor first = Spawn(new Vector3(0f, 1f, 4f));
            EnemyActor second = Spawn(new Vector3(2f, 1f, 4f));
            EnemyActor third = Spawn(new Vector3(4f, 1f, 4f));
            first.ArmMoth(); first.StepPhysics(0.9f); explosions.FlushExplosions();
            Assert.IsTrue(second.IsFuseActive);
            var effects = explosions.GetComponentsInChildren<EchoScrap.Combat.Prototype.ProceduralExplosionVfx>();
            Assert.AreEqual(1, effects.Length);
            foreach (ParticleSystem system in effects[0].GetComponentsInChildren<ParticleSystem>())
                system.Simulate(0.1f, false, false, false);
            explosions.StepVisuals(0.1f);
            Camera camera = new GameObject("Explosion evidence").AddComponent<Camera>();
            camera.transform.position = new Vector3(7f, 8f, -5f); camera.transform.LookAt(new Vector3(2f, 0.5f, 4f));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.07f);
            session.SetManualPause(true);
            var target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
            yield return null; yield return null; yield return null;
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var capture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            capture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); capture.Apply();
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "PrototypeMoth");
            Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "Preview.png"), capture.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.Destroy(capture); Object.Destroy(target); Object.Destroy(floorMaterial);
            session.SetManualPause(false);
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
