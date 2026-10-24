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
    public sealed class EnemyMortarTests
    {
        private Scene testScene;
        private CombatSession session;
        private GameObject playerObject;
        private PlayerHealth playerHealth;
        private EnemyPool enemies;
        private CorrosivePuddlePool hazards;
        private Camera camera;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            PhysicsLayers.EnsureConfigured();
            CombatTargetRegistry.Clear();
            testScene = SceneManager.CreateScene($"EnemyMortarTest_{Random.Range(10000, 99999)}");
            SceneManager.SetActiveScene(testScene);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "ArenaFloor";
            floor.transform.localScale = Vector3.one * 8f;
            floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            { color = new Color(0.12f, 0.15f, 0.19f) };
            var light = new GameObject("DirectionalLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            camera = new GameObject("MortarTestCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.08f, 0.11f);
            camera.transform.position = new Vector3(0f, 12f, -5f);
            camera.transform.LookAt(new Vector3(0f, 0f, 3f));
            session = new GameObject("CombatSession").AddComponent<CombatSession>();
            playerObject = new GameObject("Player");
            playerObject.layer = PhysicsLayers.PlayerLayer;
            playerObject.transform.position = Vector3.up;
            var body = playerObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            var collider = playerObject.AddComponent<CapsuleCollider>();
            collider.radius = 0.45f;
            collider.height = 1.8f;
            var visual = new GameObject("Visual");
            visual.transform.SetParent(playerObject.transform, false);
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            mesh.transform.SetParent(visual.transform, false);
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            mesh.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            { color = new Color(0.25f, 0.65f, 0.95f) };
            playerObject.AddComponent<PlayerRuntimeStats>();
            playerObject.AddComponent<ActorVisualComposer>();
            playerHealth = playerObject.AddComponent<PlayerHealth>();
            playerHealth.BindSession(session);
            hazards = new GameObject("CorrosivePuddlePool").AddComponent<CorrosivePuddlePool>();
            hazards.BindReferences(session, playerHealth);
            enemies = new GameObject("EnemyPool").AddComponent<EnemyPool>();
            enemies.BindReferences(session, playerHealth, hazards);
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
                    SceneManager.SetActiveScene(SceneManager.CreateScene($"MortarCleanup_{Random.Range(10000, 99999)}"));
                yield return SceneManager.UnloadSceneAsync(testScene);
            }
        }

        [Test]
        public void MortarCannotShootAcrossTheArenaOrBankShotsOutsideItsRange()
        {
            EnemyActor mortar = enemies.Spawn(EnemyArchetypeId.Mortar, new Vector3(0, 1, 30));
            mortar.StepPhysics(100f);
            Assert.That(mortar.MortarShotsFiredCount, Is.Zero);
            Assert.That(hazards.ActiveProjectileCount, Is.Zero);
            Move(mortar, new Vector3(0, 1, 8));
            mortar.StepPhysics(4.9f);
            Assert.That(mortar.MortarShotsFiredCount, Is.Zero, "Entering range starts a full attack cycle.");
            mortar.StepPhysics(0.11f);
            Assert.That(mortar.MortarShotsFiredCount, Is.EqualTo(1));
            Move(mortar, new Vector3(0, 1, 30));
            mortar.StepPhysics(100f);
            Assert.That(mortar.MortarShotsFiredCount, Is.EqualTo(1));
        }

        [Test]
        public void EditableMortarDataControlsRangeCadenceAndItsPuddles()
        {
            var data = EnemyArchetypeData.CreateDefault(EnemyArchetypeId.Mortar);
            data.MortarAttack.MaximumRange = 10f; data.MortarAttack.FireInterval = 2f;
            data.MortarAttack.FlightDuration = 0.2f; data.MortarAttack.PuddleDuration = 1.1f;
            data.MortarAttack.PuddleRadius = 0.8f; data.MortarAttack.TickDamage = 3f;
            EnemyActor mortar = enemies.Spawn(data, new Vector3(0, 1, 11));
            mortar.StepPhysics(10f); Assert.That(mortar.MortarShotsFiredCount, Is.Zero);
            Move(mortar, new Vector3(0, 1, 8)); mortar.StepPhysics(2f);
            Assert.That(mortar.MortarShotsFiredCount, Is.EqualTo(1));
            hazards.StepPool(0.21f);
            Assert.That(hazards.ActivePuddleCount, Is.EqualTo(1));
            var puddle = hazards.GetPuddleBySequenceId(1);
            Assert.That(puddle.Radius, Is.EqualTo(0.8f)); Assert.That(puddle.TotalDuration, Is.EqualTo(1.1f));
            Assert.That(playerHealth.CurrentHealth, Is.EqualTo(97f));
        }

        [Test]
        public void MortarGddStats_DistanceBand_Facing_ContactAndKnockback()
        {
            EnemyActor mortar = enemies.Spawn(EnemyArchetypeId.Mortar, new Vector3(0f, 1f, 12f));
            Assert.AreEqual(25f, mortar.MaxHealth);
            Assert.That(mortar.AssignedMoveSpeed, Is.InRange(2.2f * 0.9f, 2.2f * 1.1f));
            Assert.AreEqual(0.10f, mortar.Archetype.SpeedVarianceFraction);
            Assert.AreEqual(25, mortar.Archetype.XpReward);
            Assert.IsTrue(mortar.Archetype.IsEliteDropTier);
            mortar.StepPhysics(0.02f);
            Assert.Less(mortar.Velocity.z, -1.9f);
            Move(mortar, new Vector3(0f, 1f, 5f));
            mortar.StepPhysics(0.02f);
            Assert.Greater(mortar.Velocity.z, 1.9f);
            Assert.Less(mortar.FacingDirection.z, -0.99f, "Retreat keeps its emitter facing the player.");
            Move(mortar, new Vector3(0f, 1f, 6f));
            mortar.StepPhysics(0.02f);
            Assert.That(mortar.Velocity.magnitude, Is.LessThan(0.001f));
            mortar.ApplyKnockback(Vector3.forward * 10f);
            Assert.AreEqual(8f, mortar.KnockbackVelocity.z, 0.001f);
            mortar.StepPhysics(0.1f);
            Assert.AreEqual(6.2f, mortar.KnockbackVelocity.z, 0.001f);

            Move(mortar, new Vector3(0f, 1f, 0.8f));
            Assert.IsTrue(mortar.TryDealContactDamage());
            Assert.AreEqual(95f, playerHealth.CurrentHealth, 0.001f);
            Assert.IsFalse(mortar.TryDealContactDamage());
            AdvanceHealthClock(0.5f);
            Assert.IsTrue(mortar.TryDealContactDamage());
            Assert.AreEqual(90f, playerHealth.CurrentHealth, 0.001f);
        }

        [Test]
        public void MortarStaggeredFirstShot_FiveSecondCadence_AndProjectileSurvivesShooterDeath()
        {
            EnemyActor mortar = enemies.Spawn(EnemyArchetypeId.Mortar, new Vector3(0f, 1f, 7f));
            float firstDelay = EnemyActor.MortarFireInterval - mortar.AttackCooldownTimer;
            Assert.That(firstDelay, Is.InRange(1f, 5f));
            mortar.StepPhysics(firstDelay - 0.01f);
            Assert.AreEqual(0, mortar.MortarShotsFiredCount);
            mortar.StepPhysics(0.02f);
            Assert.AreEqual(1, mortar.MortarShotsFiredCount);
            Assert.AreEqual(1, hazards.ActiveProjectileCount);
            Assert.AreEqual(1, hazards.ReservedPuddleCount);
            mortar.StepPhysics(4.99f);
            Assert.AreEqual(1, mortar.MortarShotsFiredCount);
            mortar.StepPhysics(0.011f);
            Assert.AreEqual(2, mortar.MortarShotsFiredCount);
            var kill = new DamageRequest(1, DamageSourceType.FlashlightPulse, Vector3.zero, Vector3.forward, 100f);
            Assert.IsTrue(mortar.ReceiveDamage(in kill).CausedDeath);
            hazards.StepPool(0.84f);
            Assert.AreEqual(2, hazards.ActiveProjectileCount);
            Assert.AreEqual(0, hazards.ActivePuddleCount);
            hazards.StepPool(0.01f);
            Assert.AreEqual(0, hazards.ActiveProjectileCount);
            Assert.AreEqual(0, hazards.ReservedPuddleCount);
            Assert.AreEqual(2, hazards.ActivePuddleCount);
            Assert.AreEqual(95f, playerHealth.CurrentHealth, 0.001f, "First puddle tick is 5; overlapping arrival respects protection.");
        }

        [Test]
        public void PuddleUsesActualHurtbox_FixedTickSchedule_AndExpiresAtExactlyFourSeconds()
        {
            playerObject.transform.position = new Vector3(1.9f, 1f, 0f);
            Physics.SyncTransforms();
            int id = hazards.SpawnPuddle(4, Vector3.zero);
            Assert.Greater(id, 0);
            hazards.StepPool(0.01f);
            Assert.AreEqual(95f, playerHealth.CurrentHealth, 0.001f, "Capsule intersects the 1.5 m area although its center lies outside.");
            AdvanceHealthClock(0.49f);
            hazards.StepPool(0.49f);
            Assert.AreEqual(90f, playerHealth.CurrentHealth, 0.001f);

            playerObject.transform.position = new Vector3(2.1f, 1f, 0f);
            AdvanceHealthClock(0.5f);
            hazards.StepPool(0.5f);
            Assert.AreEqual(90f, playerHealth.CurrentHealth);
            playerObject.transform.position = Vector3.up;
            hazards.StepPool(0.24f);
            Assert.AreEqual(90f, playerHealth.CurrentHealth, "Re-entry does not reset the puddle's tick phase.");
            AdvanceHealthClock(0.5f);
            hazards.StepPool(0.26f);
            Assert.AreEqual(85f, playerHealth.CurrentHealth);
            playerHealth.SetDashInvulnerable(true);
            hazards.StepPool(2.49f);
            Assert.IsTrue(hazards.ContainsSequenceId(id));
            Assert.AreEqual(1.5f, hazards.GetPuddleBySequenceId(id).Radius, 0.001f);
            Assert.Less(hazards.GetPuddleBySequenceId(id).FadeAlpha, 0.03f);
            hazards.StepPool(0.01f);
            Assert.IsFalse(hazards.ContainsSequenceId(id));
            Assert.AreEqual(85f, playerHealth.CurrentHealth);
        }

        [Test]
        public void SaturationReservesLandings_NeverReplacesHazards_AndMortarWaitsWithoutBacklog()
        {
            var spec = CorrosivePuddlePool.ProjectileSpec.CreateMortarDefault(8, new Vector3(0f, 2f, 7f), new Vector3(20f, 0f, 0f));
            for (int i = 0; i < CorrosivePuddlePool.MaxActivePuddles; i++)
                Assert.IsTrue(hazards.TryLaunchParabolicProjectile(in spec));
            Assert.IsFalse(hazards.TryLaunchParabolicProjectile(in spec));
            Assert.IsFalse(hazards.TrySpawnPuddle(9, Vector3.zero, out _));
            Assert.AreEqual(CorrosivePuddlePool.MaxActivePuddles, hazards.ReservedPuddleCount);
            hazards.StepPool(0.65f);
            Assert.AreEqual(CorrosivePuddlePool.MaxActivePuddles, hazards.ActivePuddleCount);
            Assert.IsTrue(hazards.ContainsSequenceId(1));
            Assert.IsFalse(hazards.TrySpawnPuddle(9, Vector3.zero, out _));
            Assert.IsTrue(hazards.ContainsSequenceId(1), "A live puddle must retain its entire lifetime under pressure.");

            EnemyActor mortar = enemies.Spawn(EnemyArchetypeId.Mortar, new Vector3(0f, 1f, 7f));
            mortar.StepPhysics(20f);
            Assert.AreEqual(0, mortar.MortarShotsFiredCount);
            hazards.StepPool(4f);
            mortar.StepPhysics(0.02f);
            Assert.AreEqual(1, mortar.MortarShotsFiredCount);
            mortar.StepPhysics(0.02f);
            Assert.AreEqual(1, mortar.MortarShotsFiredCount, "Capacity returning cannot release a catch-up salvo.");
        }

        [Test]
        public void Pause_FreezesFlightAndHazards_AndSessionResetReleasesAllReservations()
        {
            var spec = CorrosivePuddlePool.ProjectileSpec.CreateMortarDefault(1, new Vector3(0f, 2f, 7f), Vector3.zero);
            int puddleId = hazards.SpawnPuddle(2, new Vector3(10f, 0f, 0f));
            Assert.IsTrue(hazards.TryLaunchParabolicProjectile(in spec));
            session.SetManualPause(true);
            hazards.StepPool(10f);
            Assert.AreEqual(1, hazards.ActiveProjectileCount);
            Assert.AreEqual(4f, hazards.GetPuddleBySequenceId(puddleId).RemainingTime);
            Assert.AreEqual(100f, playerHealth.CurrentHealth);
            Assert.IsFalse(hazards.TryLaunchParabolicProjectile(in spec));
            session.SetManualPause(false);
            hazards.StepPool(0.65f);
            Assert.AreEqual(95f, playerHealth.CurrentHealth);
            session.ResetSession(20261022);
            Assert.AreEqual(0, hazards.ActiveProjectileCount);
            Assert.AreEqual(0, hazards.ActivePuddleCount);
            Assert.AreEqual(0, hazards.ReservedPuddleCount);
            Assert.AreEqual(100f, playerHealth.CurrentHealth);
            Assert.IsTrue(hazards.TryLaunchParabolicProjectile(in spec));
            hazards.gameObject.SetActive(false);
            hazards.gameObject.SetActive(true);
            Assert.AreEqual(0, hazards.ReservedPuddleCount);
            Assert.IsTrue(hazards.TryLaunchParabolicProjectile(in spec));
        }

        [Test]
        public void OversizedFrame_AdvancesOnlyPostImpactPuddleTime_AndRetainsWarningInReducedEffects()
        {
            session.Effects.ReducedEffectsMode = true;
            var spec = CorrosivePuddlePool.ProjectileSpec.CreateMortarDefault(1, new Vector3(0f, 2f, 7f), Vector3.zero);
            Assert.IsTrue(hazards.TryLaunchParabolicProjectile(in spec));
            Transform warning = hazards.transform.Find("MortarProjectileSlot_0/GroundReticleOuter");
            Assert.IsTrue(warning.gameObject.activeInHierarchy);
            Assert.AreEqual(3f, warning.localScale.x, 0.001f);
            hazards.StepPool(0.75f);
            Assert.AreEqual(3.9f, hazards.GetPuddleBySequenceId(1).RemainingTime, 0.001f);
            Assert.AreEqual(95f, playerHealth.CurrentHealth);
            Transform border = hazards.transform.Find("CorrosivePuddleSlot_0/SharpOuterBorder");
            Assert.IsTrue(border.gameObject.activeInHierarchy);
            Assert.IsFalse(hazards.transform.Find("CorrosivePuddleSlot_0/LandingRipple").gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator MortarPreview_ProjectileReticleAndCorrosiveBoundaryRemainReadable()
        {
            EnemyActor mortar = enemies.Spawn(EnemyArchetypeId.Mortar, new Vector3(-2f, 1f, 7f));
            mortar.enabled = false;
            mortar.StepVisuals(0.02f);
            hazards.SpawnPuddle(10, new Vector3(-2.5f, 0f, 1f), duration: 4f);
            hazards.SpawnPuddle(11, new Vector3(2.5f, 0f, 4f), duration: 4f);
            var spec = CorrosivePuddlePool.ProjectileSpec.CreateMortarDefault(mortar.TargetId,
                mortar.transform.position + Vector3.up * 0.65f, new Vector3(1.7f, 0f, 1f));
            Assert.IsTrue(hazards.TryLaunchParabolicProjectile(in spec));
            hazards.StepPool(0.32f);
            session.SetManualPause(true);
            var renderTexture = new RenderTexture(1280, 720, 24);
            var previousTexture = camera.targetTexture;
            var previousActive = RenderTexture.active;
            Texture2D texture = null;
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "EnemyMortar");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "Preview.png");
            try
            {
                camera.targetTexture = renderTexture;
                yield return null;
                yield return null;
                yield return null;
                RenderTexture.active = renderTexture;
                texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTexture;
                RenderTexture.active = previousActive;
                renderTexture.Release();
                Object.Destroy(renderTexture);
                if (texture != null) Object.Destroy(texture);
                session.SetManualPause(false);
            }
            Assert.IsTrue(File.Exists(path));
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(10000));
        }

        private static void Move(EnemyActor actor, Vector3 position)
        {
            actor.transform.position = position;
            actor.Body.position = position;
            Physics.SyncTransforms();
        }
        private void AdvanceHealthClock(float delta)
        {
            session.Clock.AdvanceGameplayTime(delta);
            playerHealth.StepTimers(delta);
        }
    }
}
