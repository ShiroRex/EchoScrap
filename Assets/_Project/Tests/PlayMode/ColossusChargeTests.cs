using System.Collections;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EchoScrap.Tests
{
    public sealed class ColossusChargeTests
    {
        private CombatGameBootstrap game;
        private EnemyActor actor;
        private ColossusCharge charge;
        private SimulationMode previousMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = false; game.Encounter.AutoTick = false;
            game.ResetRun(false);
            game.Weapon.enabled = false;
            game.Motor.enabled = false;
            game.Session.enabled = false;
            game.Player.GetComponent<Rigidbody>().isKinematic = true;
            previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            MovePlayer(new Vector3(0f, 1f, 6f));
            Spawn(Vector3.up);
        }

        private void Spawn(Vector3 position)
        {
            actor = game.Enemies.Spawn(EnemyArchetypeId.Colossus, position);
            actor.enabled = true; // A pooled actor may still have the fixture's manual stepping disabled.
            actor.enabled = false; // The test owns fixed steps; the component and views stay active.
            actor.Body.interpolation = RigidbodyInterpolation.None;
            charge = actor.GetComponent<ColossusCharge>();
            Physics.SyncTransforms();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Physics.simulationMode = previousMode;
            Time.timeScale = 1f;
            Scene arena = game.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("Colossus charge cleanup"));
            yield return SceneManager.UnloadSceneAsync(arena);
            CombatTargetRegistry.Clear();
            Assert.AreEqual(0, EnemyActor.ActiveEnemies.Count, "The fixture must not leave disabled pooled actors in the static registry.");
        }

        private void MovePlayer(Vector3 position)
        {
            game.Player.transform.position = position;
            game.Player.GetComponent<Rigidbody>().position = position;
            Physics.SyncTransforms();
        }

        private void Tick(float deltaTime = 0.02f)
        {
            actor.StepPhysics(deltaTime);
            if (!game.Session.IsPaused) Physics.Simulate(deltaTime);
            game.Session.Clock.AdvanceGameplayTime(deltaTime);
            actor.StepVisuals(deltaTime);
        }

        private void BeginWindup()
        {
            charge.TryOverrideLocomotion(charge.CooldownRemaining, out _);
            Assert.AreEqual(ColossusChargeState.Windup, charge.State);
        }

        private void BeginCharge(float deltaTime = 0.02f)
        {
            BeginWindup();
            for (int i = 0; i < 100 && charge.State == ColossusChargeState.Windup; i++) Tick(deltaTime);
            Assert.AreEqual(ColossusChargeState.Charging, charge.State);
        }

        [Test]
        public void HeavyCapsuleAndVisualStandOnFloor_AndActuallyPursueAtOnePointEight()
        {
            float start = actor.Body.position.z;
            for (int i = 0; i < 50; i++) Tick();
            Assert.AreEqual(1f, actor.Body.position.y, 0.001f);
            Assert.AreEqual(1.8f, actor.Body.position.z - start, 0.08f,
                "The Colossus must physically move, not merely report a configured speed.");
            Assert.GreaterOrEqual(actor.BodyCollider.bounds.min.y, -0.01f);
            var body = charge.Presentation.AttackPose.Find("BodyMesh").GetComponent<Renderer>();
            Assert.That(body.bounds.min.y, Is.InRange(-0.05f, 0.15f));
        }

        [TestCase(0.01f)]
        [TestCase(0.02f)]
        [TestCase(0.05f)]
        public void TelegraphLocksAim_ThenChargeTravelsExactlyFourPointTwoMeters(float step)
        {
            BeginCharge(step);
            Vector3 start = actor.Body.position;
            Assert.IsTrue(charge.LockedDirection == Vector3.forward);
            MovePlayer(new Vector3(8f, 1f, 6f));
            for (int i = 0; i < 100 && charge.State == ColossusChargeState.Charging; i++) Tick(step);
            Assert.AreEqual(ColossusChargeState.Recovery, charge.State);
            Assert.AreEqual(4.2f, actor.Body.position.z - start.z, 0.04f);
            Assert.AreEqual(start.x, actor.Body.position.x, 0.01f);
            Assert.AreEqual(1, charge.ChargesStarted);
            Assert.That(charge.CooldownRemaining, Is.InRange(5f, 7f));
            for (int i = 0; i < Mathf.CeilToInt(0.58f / step); i++) Tick(step);
            Assert.LessOrEqual(Vector3.Distance(start + Vector3.forward * 4.2f, actor.Body.position), 0.05f);
        }

        [Test]
        public void HeavyAnticipationAndImpactSquashKeepTheFeetNearTheFloor()
        {
            BeginWindup();
            for (int i = 0; i < 25; i++) Tick();
            AssertCapsuleFoot();
            MovePlayer(new Vector3(8f, 1f, 6f));
            for (int i = 0; i < 100 && charge.State != ColossusChargeState.Recovery; i++) Tick();
            for (int i = 0; i < 6; i++) Tick();
            AssertCapsuleFoot();
        }

        private void AssertCapsuleFoot()
        {
            var mesh = charge.Presentation.AttackPose.Find("BodyMesh").GetComponent<MeshFilter>();
            float foot = float.PositiveInfinity;
            foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                foot = Mathf.Min(foot, mesh.transform.TransformPoint(vertex).y);
            Assert.That(foot, Is.InRange(-0.04f, 0.08f), "Squash should read as weight pressing into the floor, not hovering.");
        }

        private Collider Wall(Vector3 position, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Charge test thin wall"; wall.transform.position = position; wall.transform.localScale = scale;
            return wall.GetComponent<Collider>();
        }

        [UnityTest]
        public IEnumerator InterpolatedBodyStopsAtWallDuringActualUnityFixedUpdates()
        {
            Collider wall = Wall(new Vector3(0f, 1.7f, 3f), new Vector3(8f, 3.4f, 0.08f));
            Physics.SyncTransforms();
            BeginWindup();
            Physics.simulationMode = SimulationMode.FixedUpdate;
            actor.Body.interpolation = RigidbodyInterpolation.Interpolate;
            actor.enabled = true;
            for (int i = 0; i < 100 && charge.State != ColossusChargeState.Recovery; i++)
                yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, charge.WallStops);
            Assert.AreEqual(1, charge.Presentation.ImpactCount);
            Assert.LessOrEqual(actor.Body.position.z + actor.HitRadius, wall.bounds.min.z + 0.025f);
            Assert.AreEqual(1f, actor.Body.position.y, 0.001f);
        }

        [TestCase(0f, false)]
        [TestCase(45f, false)]
        [TestCase(45f, true)]
        public void SweptChargeStopsAtThinWall_DiagonalOrCorner_AndImpactsOnlyOnce(float angle, bool corner)
        {
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            MovePlayer(Vector3.up + direction * 6f);
            Collider wall = Wall(new Vector3(0f, 1.7f, 2f), new Vector3(8f, 3.4f, 0.08f));
            Collider side = corner ? Wall(new Vector3(2f, 1.7f, 0f), new Vector3(0.08f, 3.4f, 8f)) : null;
            Physics.SyncTransforms();
            BeginCharge();
            Assert.Less(charge.WarningDistance, charge.Settings.Distance);
            for (int i = 0; i < 40 && charge.State == ColossusChargeState.Charging; i++) Tick();
            Assert.AreEqual(1, charge.WallStops);
            Assert.AreEqual(1, charge.Presentation.ImpactCount);
            Assert.LessOrEqual(actor.BodyCollider.bounds.max.z, wall.bounds.min.z + 0.025f);
            if (side != null) Assert.LessOrEqual(actor.BodyCollider.bounds.max.x, side.bounds.min.x + 0.025f);
            for (int i = 0; i < 20; i++) Tick();
            Assert.AreEqual(1, charge.WallStops);
            Assert.AreEqual(1, charge.Presentation.ImpactCount);
        }

        [Test]
        public void ChargeDealsExistingTwentyFiveContactDamageOnce_AndRecoveryIsSafe()
        {
            MovePlayer(new Vector3(0f, 1f, 2.5f));
            BeginCharge();
            for (int i = 0; i < 40 && charge.State == ColossusChargeState.Charging; i++) Tick();
            Assert.AreEqual(75f, game.Player.CurrentHealth, 0.001f);
            Assert.AreEqual(1, charge.PlayerHitAttempts);
            for (int i = 0; i < 29; i++) Tick();
            Assert.AreEqual(75f, game.Player.CurrentHealth, 0.001f);
            Assert.IsFalse(actor.TryDealContactDamage());
        }

        [Test]
        public void PlayerDashPhasesThroughTheEntireCharge_WithoutDamageOrStoppingIt()
        {
            MovePlayer(new Vector3(0f, 1f, 2.5f));
            BeginCharge();
            game.Player.gameObject.layer = PhysicsLayers.PlayerDashLayer;
            game.Player.SetDashInvulnerable(true);
            for (int i = 0; i < 40 && charge.State == ColossusChargeState.Charging; i++) Tick();
            Assert.AreEqual(100f, game.Player.CurrentHealth);
            Assert.AreEqual(0, charge.PlayerHitAttempts);
            Assert.AreEqual(4.2f, actor.Body.position.z, 0.04f);
        }

        [Test]
        public void SweptContactStillPassesThroughPlayerInvulnerabilityContract()
        {
            MovePlayer(new Vector3(0f, 1f, 2.5f));
            BeginCharge();
            game.Player.SetDashInvulnerable(true);
            for (int i = 0; i < 40 && charge.State == ColossusChargeState.Charging; i++) Tick();
            Assert.AreEqual(100f, game.Player.CurrentHealth);
            Assert.AreEqual(1, charge.PlayerHitAttempts);
            Assert.AreEqual(ColossusChargeState.Recovery, charge.State);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PauseFreezesWindupAndCharge_AndResumesWithoutRestarting(bool charging)
        {
            if (charging) { BeginCharge(); Tick(); } else { BeginWindup(); Tick(); }
            float phase = charge.PhaseProgress;
            float cooldown = charge.CooldownRemaining;
            Vector3 position = actor.Body.position;
            game.Session.SetManualPause(true);
            for (int i = 0; i < 100; i++) Tick();
            Assert.AreEqual(phase, charge.PhaseProgress);
            Assert.AreEqual(cooldown, charge.CooldownRemaining);
            Assert.AreEqual(position, actor.Body.position);
            game.Session.SetManualPause(false); Tick();
            Assert.Greater(charge.PhaseProgress, phase);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeathCancelsWarningAndEffects_AndPoolReuseStartsClean(bool charging)
        {
            if (charging) BeginCharge(); else BeginWindup();
            Assert.AreEqual(!charging, charge.Presentation.IsWarningVisible);
            var lethal = new DamageRequest(1, DamageSourceType.SolarPulse, Vector3.zero, Vector3.forward, 1000f);
            actor.ReceiveDamage(in lethal);
            Assert.IsFalse(charge.Presentation.IsWarningVisible);
            Assert.AreEqual(0, charge.Presentation.ActiveParticleCount);
            EnemyActor previous = actor;
            int created = game.Enemies.TotalCreatedCount;
            Spawn(new Vector3(0f, 1f, -3f));
            Assert.AreSame(previous, actor);
            Assert.AreEqual(created, game.Enemies.TotalCreatedCount);
            Assert.AreEqual(ColossusChargeState.Pursuit, charge.State);
            Assert.AreEqual(0, charge.WallStops + charge.ChargesStarted + charge.Presentation.ImpactCount);
            Assert.That(charge.CooldownRemaining, Is.InRange(5f, 7f));
        }

        [Test]
        public void ReducedEffectsKeepWarningAndGameplay_WhileClearingDustAndDeformation()
        {
            BeginWindup();
            game.Session.Effects.ReducedEffectsMode = true;
            for (int i = 0; i < 10; i++) Tick();
            Assert.IsTrue(charge.Presentation.IsWarningVisible);
            Assert.AreEqual(Vector3.one, charge.Presentation.AttackPose.localScale);
            Assert.AreEqual(0, charge.Presentation.ActiveParticleCount);
            game.Session.Effects.ReducedEffectsMode = false;
            for (int i = 0; i < 100 && charge.State == ColossusChargeState.Windup; i++) Tick();
            Assert.Greater(charge.Presentation.ActiveParticleCount, 0);
            Assert.LessOrEqual(charge.Presentation.ActiveParticleCount, ColossusChargePresentation.ParticleBudget);
            game.Session.Effects.ReducedEffectsMode = true;
            charge.Presentation.Step(0f);
            Assert.AreEqual(0, charge.Presentation.ActiveParticleCount);
        }

        [Test]
        public void CooldownsAreStaggeredByCombatSeed_AndPreparingViewsDoesNotRoll()
        {
            game.Session.ResetSession(8119);
            game.Enemies.Prewarm(EnemyArchetypeId.Colossus, 25);
            Assert.AreEqual(0, game.Session.Rng.Combat.SampleCount);
            var first = game.Enemies.Spawn(EnemyArchetypeId.Colossus, new Vector3(15f, 1f, 10f));
            var second = game.Enemies.Spawn(EnemyArchetypeId.Colossus, new Vector3(20f, 1f, 10f));
            float a = first.GetComponent<ColossusCharge>().CooldownRemaining;
            float b = second.GetComponent<ColossusCharge>().CooldownRemaining;
            Assert.That(a, Is.InRange(5f, 7f)); Assert.That(b, Is.InRange(5f, 7f));
            Assert.AreNotEqual(a, b);
            Assert.AreEqual(2, game.Session.Rng.Combat.SampleCount);
        }

        [UnityTest]
        public IEnumerator PreviewCapturesWarningAndWallImpact()
        {
            game.Hud.gameObject.SetActive(false);
            game.GameplayCamera.enabled = false; game.CameraRig.enabled = false;
            var preview = new GameObject("Colossus preview camera").AddComponent<Camera>();
            preview.enabled = false; preview.transform.position = new Vector3(6f, 8f, -5f);
            preview.transform.LookAt(new Vector3(0f, 1f, 2f)); preview.fieldOfView = 52f;
            var light = new GameObject("Preview fill").AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1f; light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            Wall(new Vector3(0f, 1.7f, 3.3f), new Vector3(6f, 3.4f, 0.2f));
            Physics.SyncTransforms(); BeginWindup();
            for (int i = 0; i < 30; i++) Tick();
            Capture(preview, "Warning.png");
            for (int i = 0; i < 100 && charge.State != ColossusChargeState.Recovery; i++) Tick();
            charge.Presentation.Step(0.10f);
            foreach (var particles in actor.GetComponentsInChildren<ParticleSystem>()) particles.Simulate(0.10f, false, false);
            Capture(preview, "Impact.png");
            Assert.AreEqual(1, charge.WallStops);
            yield return null;
        }

        private static void Capture(Camera camera, string filename)
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ColossusCharge");
            Directory.CreateDirectory(directory);
            var target = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0f, 0f, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(directory, filename), image.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        }
    }
}
