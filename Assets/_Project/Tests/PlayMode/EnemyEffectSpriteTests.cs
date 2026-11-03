using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class EnemyEffectSpriteTests
    {
        private Scene scene;
        private CombatSession session;
        private PlayerHealth player;
        private Camera camera;
        private EnemyPool enemies;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f; PhysicsLayers.EnsureConfigured(); CombatTargetRegistry.Clear();
            scene = SceneManager.CreateScene("Enemy effect sprites"); SceneManager.SetActiveScene(scene);
            session = new GameObject("Session").AddComponent<CombatSession>();
            var go = new GameObject("Player"); go.transform.position = Vector3.up; go.layer = PhysicsLayers.PlayerLayer;
            var body = go.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
            go.AddComponent<CapsuleCollider>(); go.AddComponent<PlayerRuntimeStats>();
            player = go.AddComponent<PlayerHealth>(); player.BindSession(session);
            camera = new GameObject("Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.SetPositionAndRotation(new Vector3(0, 10, -10), Quaternion.Euler(50f, 0f, 0f));
            enemies = new GameObject("Enemies").AddComponent<EnemyPool>(); enemies.BindReferences(session, player);
            session.ResolvePlayerReferences(); session.ResetSession(20261102); session.SimulateFocusChange(true);
            yield return null;
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f; CombatTargetRegistry.Clear();
            Scene cleanup = SceneManager.GetSceneByName("Enemy effect cleanup");
            if (!cleanup.IsValid()) cleanup = SceneManager.CreateScene("Enemy effect cleanup");
            SceneManager.SetActiveScene(cleanup);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        [Test]
        public void DrawnEffectsRetainAllAsepriteTimingsAndUncompressedPointFilteredCells()
        {
            var art = EnemyEffectLibrary.Load(); Assert.IsNotNull(art); Assert.IsTrue(art.IsReady);
            int frames = 0;
            foreach (var clip in art.clips)
            {
                Assert.AreEqual(8, clip.animation.frames.Length);
                Assert.Greater(clip.animation.Duration, 0f);
                foreach (var f in clip.animation.frames) { Assert.Greater(f.duration, 0f); Assert.GreaterOrEqual(f.uvRect.x, 0f); frames++; }
            }
            Assert.AreEqual(120, frames);
            var atlas = (Texture2D)art.material.GetTexture("_BaseMap");
            Assert.AreEqual(512, atlas.width); Assert.AreEqual(960, atlas.height);
            Assert.AreEqual(FilterMode.Point, atlas.filterMode); Assert.AreEqual(1, atlas.mipmapCount);
            Assert.AreEqual(TextureFormat.RGBA32, atlas.format);
        }

        [Test]
        public void CopperMissilesLightTheSceneFollowFlightFreezeOnPauseAndResetWithoutGhostLights()
        {
            var shots = EliteProjectilePool.GetOrCreate(session, player); shots.AutoTick = false;
            Assert.IsTrue(shots.FireFan(7, new Vector3(0, 1, 8), Vector3.forward, EliteAttackData.Copper));
            Assert.AreEqual(3, shots.ActiveLightCount);
            var sprite = shots.SpriteAt(1); Assert.IsNotNull(sprite); Assert.AreEqual(EnemyEffectId.CopperMissile, sprite.Id);
            shots.Step(.2f);
            var position = shots.PositionAt(1);
            Assert.Greater(position.z, 8f);
            var light = shots.LightAt(1);
            Assert.AreEqual(LightType.Point, light.type); Assert.Greater(light.intensity, 0f);
            Assert.AreEqual(position, light.transform.position);
            session.SetManualPause(true); shots.Step(1f);
            Assert.AreEqual(position, shots.PositionAt(1));
            session.SetManualPause(false);
            session.Effects.ReducedEffectsMode = true; Assert.AreEqual(0, shots.ActiveLightCount);
            Assert.IsTrue(sprite.Root.activeInHierarchy, "A live projectile stays visible when decorative light is disabled.");
            session.Effects.ReducedEffectsMode = false; Assert.AreEqual(3, shots.ActiveLightCount);
            session.ResetSession(20261102); Assert.AreEqual(0, shots.ActiveCount); Assert.AreEqual(0, shots.ActiveLightCount);
            Assert.IsFalse(light.enabled); Assert.IsFalse(sprite.Root.activeInHierarchy);
        }

        [Test]
        public void MissilePoolKeepsItsDamageCapacityWhileBoundingSceneLightsAndExpiringViews()
        {
            var shots = EliteProjectilePool.GetOrCreate(session, player); shots.AutoTick = false;
            for (int i = 0; i < EliteProjectilePool.Capacity / 3; i++)
                Assert.IsTrue(shots.FireFan(i + 1, new Vector3(i * 2, 1, 10), Vector3.forward, EliteAttackData.Copper));
            Assert.AreEqual(48, shots.ActiveCount);
            Assert.AreEqual(EnemyEffectLibrary.Load().missileLightBudget, shots.ActiveLightCount);
            Assert.IsFalse(shots.FireFan(99, Vector3.up, Vector3.forward, EliteAttackData.Copper));
            shots.Step(3f); Assert.AreEqual(0, shots.ActiveCount); Assert.AreEqual(0, shots.ActiveLightCount);
            for (int i = 0; i < EliteProjectilePool.Capacity; i++) Assert.IsFalse(shots.LightAt(i).enabled);
        }

        [Test]
        public void MissilesExplodeAtWallsAndTheirSpritesNeverCreateExtraDamage()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0, 1, 4); wall.transform.localScale = new Vector3(12, 3, .3f);
            Physics.SyncTransforms();
            var shots = EliteProjectilePool.GetOrCreate(session, player); shots.AutoTick = false;
            float health = player.CurrentHealth;
            shots.FireFan(7, new Vector3(0, 1, 8), Vector3.back, EliteAttackData.Copper);
            shots.Step(1f);
            Assert.AreEqual(0, shots.ActiveCount); Assert.AreEqual(0, shots.ActiveLightCount);
            Assert.AreEqual(0, shots.PlayerHitAttempts); Assert.AreEqual(health, player.CurrentHealth);
            Assert.AreEqual(3, EnemyEffectPool.GetOrCreate(session).Count(EnemyEffectId.CopperImpact));
        }

        [TestCase(EnemyEffectId.ColossusImpact)]
        [TestCase(EnemyEffectId.SpectralSweep)]
        [TestCase(EnemyEffectId.MagmaSweep)]
        public void DrawnGroundEffectsFollowTheWholeFootprintAcrossSlopes(EnemyEffectId id)
        {
            new GameObject("Terrain").AddComponent<ArenaSurface>();
            var effects = EnemyEffectPool.GetOrCreate(session); effects.AutoTick = false;
            Vector3 center = ArenaSurface.ReferenceToWorld(new Vector3(-32, 0, 3));
            Assert.IsTrue(effects.Play(id, center, Vector3.forward, Vector2.one * 9f));
            foreach (var filter in effects.GetComponentsInChildren<MeshFilter>())
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    Vector3 point = filter.transform.TransformPoint(vertex);
                    Assert.AreEqual(ArenaSurface.Height(point) + .065f, point.y, .002f);
                }
        }

        [Test]
        public void CosmeticEffectsRemainBoundedAndFreezeWithoutAffectingHealthOrCombatRandomness()
        {
            var effects = EnemyEffectPool.GetOrCreate(session); effects.AutoTick = false;
            float health = player.CurrentHealth; int samples = session.Rng.Combat.SampleCount;
            for (int i = 0; i < EnemyEffectPool.Capacity; i++) Assert.IsTrue(effects.Play(EnemyEffectId.MagmaImpact, Vector3.up));
            Assert.IsFalse(effects.Play(EnemyEffectId.MagmaImpact, Vector3.up));
            session.SetManualPause(true); effects.Step(10f); Assert.AreEqual(96, effects.ActiveCount);
            session.SetManualPause(false); effects.Step(1f); Assert.AreEqual(0, effects.ActiveCount);
            Assert.AreEqual(health, player.CurrentHealth); Assert.AreEqual(samples, session.Rng.Combat.SampleCount);
            Assert.IsTrue(effects.Play(EnemyEffectId.MothSmoke, Vector3.up));
            session.ResetSession(20261102); Assert.AreEqual(0, effects.ActiveCount);
        }

        [Test]
        public void WarmedEffectsAndMissilesReuseTheirViewsWithoutGrowingTheManagedHeap()
        {
            var effects = EnemyEffectPool.GetOrCreate(session); effects.AutoTick = false;
            var shots = EliteProjectilePool.GetOrCreate(session, player); shots.AutoTick = false;
            for (int c = 0; c < 15; c++) effects.Play((EnemyEffectId)c, new Vector3(10, 1, 10), Vector3.right);
            effects.Step(2f);
            shots.FireFan(7, new Vector3(0, 1, 10), Vector3.forward, EliteAttackData.Copper); shots.Step(3f);
            long calibration = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            var probe = new byte[1048576];
            Assert.GreaterOrEqual(UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - calibration, 1048576L);
            System.GC.KeepAlive(probe);
            long before = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            for (int repeat = 0; repeat < 30; repeat++)
            {
                for (int c = 0; c < 15; c++) effects.Play((EnemyEffectId)c, new Vector3(10, 1, 10), Vector3.right);
                effects.Step(2f);
                shots.FireFan(7, new Vector3(0, 1, 10), Vector3.forward, EliteAttackData.Copper); shots.Step(3f);
            }
            long growth = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - before;
            Assert.LessOrEqual(growth, 2048L, "Warmed views, frame changes and moving lights must reuse their managed storage.");
        }

        [TestCase(EnemyArchetypeId.GhostKnight, EnemyEffectId.SpectralSweep)]
        [TestCase(EnemyArchetypeId.CopperSentinel, EnemyEffectId.CopperMuzzle)]
        public void EliteArtStartsWhenTheWarnedAttackActuallyResolves(EnemyArchetypeId id, EnemyEffectId effect)
        {
            var actor = enemies.Spawn(id, new Vector3(0, 1, 3)); actor.enabled = false;
            var effects = EnemyEffectPool.GetOrCreate(session); effects.AutoTick = false;
            actor.Elite.TryOverrideLocomotion(.8f, out _);
            Assert.AreEqual(0, effects.Count(effect));
            actor.Elite.TryOverrideLocomotion(actor.Elite.Settings.Windup, out _);
            Assert.AreEqual(1, effects.Count(effect));
            Assert.AreEqual(1, actor.Elite.AttacksResolved);
        }
    }
}
