using System.Collections;
using System.Diagnostics;
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
    public sealed class EnemySpriteIntegrationTests
    {
        private Scene scene;
        private CombatSession session;
        private PlayerHealth player;
        private EnemyPool enemies;
        private Camera camera;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            PhysicsLayers.EnsureConfigured(); CombatTargetRegistry.Clear();
            scene = SceneManager.CreateScene("Enemy Sprite Integration " + Random.Range(1000, 9999));
            SceneManager.SetActiveScene(scene);
            session = new GameObject("Session").AddComponent<CombatSession>();
            var go = new GameObject("Player"); go.transform.position = Vector3.up; go.layer = PhysicsLayers.PlayerLayer;
            var body = go.AddComponent<Rigidbody>(); body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            go.AddComponent<CapsuleCollider>().radius = .45f;
            go.AddComponent<PlayerRuntimeStats>(); go.AddComponent<ActorVisualComposer>();
            player = go.AddComponent<PlayerHealth>(); player.BindSession(session);
            session.ResolvePlayerReferences(); session.ResetSession(20261102);
            camera = new GameObject("Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.SetPositionAndRotation(new Vector3(0, 9, -9), Quaternion.Euler(50, 0, 0));
            enemies = new GameObject("Enemies").AddComponent<EnemyPool>(); enemies.BindReferences(session, player);
            yield return null;
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f; CombatTargetRegistry.Clear();
            if (SceneManager.sceneCount <= 1) SceneManager.SetActiveScene(SceneManager.CreateScene("Enemy Sprite Cleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [TestCase(EnemyArchetypeId.Residual, 144, 20, 1728)]
        [TestCase(EnemyArchetypeId.Colossus, 200, 28, 2400)]
        public void ApprovedAtlasesRetainTheirResolutionTimingsAndAllDirections(EnemyArchetypeId id, int frames, int clips, int height)
        {
            var set = EnemySpriteSet.Load(id); Assert.IsNotNull(set);
            int foundFrames = 0, foundClips = 0;
            foreach (var clip in set.clips)
            {
                if (clip == null || clip.frames == null || clip.frames.Length == 0) continue;
                foundClips++; foundFrames += clip.frames.Length;
                foreach (var frame in clip.frames) Assert.That(frame.duration, Is.GreaterThan(0f));
            }
            Assert.That(foundClips, Is.EqualTo(clips)); Assert.That(foundFrames, Is.EqualTo(frames));
            var texture = (Texture2D)set.material.GetTexture("_BaseMap");
            Assert.That(texture.width, Is.EqualTo(768)); Assert.That(texture.height, Is.EqualTo(height));
            Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Point)); Assert.That(texture.mipmapCount, Is.EqualTo(1));
            Assert.That(set.material.shader.name, Is.EqualTo("EchoScrap/Character Pixel Lit"));
            foreach (var v in set.mesh.vertices) Assert.That(v.y, Is.GreaterThanOrEqualTo(0f), "Below-pivot sword pixels must not be buried under the ground.");
        }

        [TestCase(EnemyArchetypeId.Residual, 4, 0, CharacterFacing.Left)]
        [TestCase(EnemyArchetypeId.Residual, -4, 0, CharacterFacing.Right)]
        [TestCase(EnemyArchetypeId.Residual, 0, 4, CharacterFacing.Down)]
        [TestCase(EnemyArchetypeId.Residual, 0, -4, CharacterFacing.Up)]
        [TestCase(EnemyArchetypeId.Colossus, 4, 0, CharacterFacing.Left)]
        [TestCase(EnemyArchetypeId.Colossus, -4, 0, CharacterFacing.Right)]
        [TestCase(EnemyArchetypeId.Colossus, 0, 4, CharacterFacing.Down)]
        [TestCase(EnemyArchetypeId.Colossus, 0, -4, CharacterFacing.Up)]
        public void SpawnUsesCorrectCameraRelativeViewAndHidesThePrimitiveModel(EnemyArchetypeId id, float x, float z, CharacterFacing expected)
        {
            var actor = enemies.Spawn(id, new Vector3(x, 1, z));
            var view = actor.GetComponent<EnemySpriteView>(); view.Advance(0f);
            Assert.That(view.IsReady, Is.True); Assert.That(view.Facing, Is.EqualTo(expected));
            Assert.That(view.CharacterRenderer.transform.position.y, Is.EqualTo(0f).Within(.001f));
            foreach (var r in actor.VisualComposer.PresentationPivot.GetComponentsInChildren<Renderer>(true))
                Assert.That(r.enabled, Is.EqualTo(r == view.CharacterRenderer));
            Assert.That(actor.BodyCollider.enabled, Is.True);
            Assert.That(actor.Archetype.IsHeavyMass, Is.EqualTo(id == EnemyArchetypeId.Colossus));
        }

        [Test]
        public void WalkingHitPauseAndPoolReuseDoNotLeakAnimationState()
        {
            var actor = enemies.Spawn(EnemyArchetypeId.Residual, new Vector3(4, 1, 0));
            var view = actor.GetComponent<EnemySpriteView>();
            actor.Body.linearVelocity = Vector3.left * actor.AssignedMoveSpeed; view.Advance(.2f);
            Assert.That(view.Motion, Is.EqualTo(EnemySpriteMotion.Walk)); Assert.That(view.FrameIndex, Is.GreaterThan(0));
            float clock = view.ClipTime; int frame = view.FrameIndex;
            session.SetManualPause(true); view.Advance(1f);
            Assert.That(view.ClipTime, Is.EqualTo(clock)); Assert.That(view.FrameIndex, Is.EqualTo(frame));
            session.SetManualPause(false);
            var hit = new DamageRequest(1, DamageSourceType.FlashlightPulse, Vector3.zero, Vector3.left, 1f);
            actor.ReceiveDamage(in hit);
            Assert.That(view.Motion, Is.EqualTo(EnemySpriteMotion.Hit));
            view.Advance(.2f); Assert.That(view.ClipTime, Is.Zero, "Local hit stop holds the hit pose.");
            actor.StepVisuals(.3f); view.Advance(.15f); Assert.That(view.ClipTime, Is.GreaterThan(0f));
            enemies.ReturnToPool(actor);
            var reused = enemies.Spawn(EnemyArchetypeId.Residual, new Vector3(2, 1, 2));
            Assert.That(reused, Is.SameAs(actor)); Assert.That(view.Motion, Is.EqualTo(EnemySpriteMotion.Idle));
            Assert.That(view.ClipTime, Is.Zero);
        }

        [Test]
        public void SwordPoseFollowsRealContactWithoutChangingDamageOrCooldown()
        {
            var actor = enemies.Spawn(EnemyArchetypeId.Residual, new Vector3(0, 1, .65f));
            var view = actor.GetComponent<EnemySpriteView>(); Physics.SyncTransforms();
            Assert.That(actor.TryDealContactDamage(), Is.True); Assert.That(player.CurrentHealth, Is.EqualTo(90f));
            Assert.That(view.Motion, Is.EqualTo(EnemySpriteMotion.Attack)); Assert.That(view.FrameIndex, Is.EqualTo(3));
            view.Advance(.2f); Assert.That(player.CurrentHealth, Is.EqualTo(90f), "Drawing an attack cannot inflict extra damage.");
            Assert.That(actor.TryDealContactDamage(), Is.False);
            session.Clock.AdvanceGameplayTime(.51f); player.StepTimers(.51f);
            Assert.That(actor.TryDealContactDamage(), Is.True); Assert.That(player.CurrentHealth, Is.EqualTo(80f));
        }

        [Test]
        public void WendigoChargeClipsFollowTheAuthoritativePhysicsPhases()
        {
            var actor = enemies.Spawn(EnemyArchetypeId.Colossus, new Vector3(0, 1, 7));
            var view = actor.GetComponent<EnemySpriteView>();
            actor.StepPhysics(20f); view.Advance(0f);
            Assert.That(view.Motion, Is.EqualTo(EnemySpriteMotion.Windup));
            actor.StepPhysics(.4f); view.Advance(0f);
            Assert.That(view.ClipTime, Is.EqualTo(.4f).Within(.001f));
            actor.StepPhysics(.4f); view.Advance(0f);
            Assert.That(view.Motion, Is.EqualTo(EnemySpriteMotion.Charge));
            actor.StepPhysics(.36f); view.Advance(0f);
            Assert.That(view.Motion, Is.EqualTo(EnemySpriteMotion.Recovery));
            actor.StepPhysics(.3f); view.Advance(0f);
            Assert.That(view.ClipTime, Is.EqualTo(.3f).Within(.001f));
        }

        [TestCase(EnemyArchetypeId.Residual)]
        [TestCase(EnemyArchetypeId.Colossus)]
        public void AuthoredDeathSurvivesActorReuseAndResetsWithTheSession(EnemyArchetypeId id)
        {
            var generic = new GameObject("Generic Deaths").AddComponent<EnemyDeathPresentationPool>(); generic.BindSession(session);
            var actor = enemies.Spawn(id, new Vector3(0, 1, 5));
            var corpses = EnemySpriteDeathPool.GetOrCreate(session);
            var lethal = new DamageRequest(1, DamageSourceType.FlashlightPulse, Vector3.zero, Vector3.forward, 10000f);
            actor.ReceiveDamage(in lethal);
            Assert.That(actor.gameObject.activeSelf, Is.False); Assert.That(corpses.ActiveCount, Is.EqualTo(1));
            Assert.That(generic.TotalBurstsPlayed, Is.Zero);
            var reused = enemies.Spawn(id, new Vector3(5, 1, 0));
            Assert.That(reused, Is.SameAs(actor)); Assert.That(corpses.ActiveCount, Is.EqualTo(1));
            session.SetManualPause(true); corpses.Step(10f); Assert.That(corpses.ActiveCount, Is.EqualTo(1));
            session.SetManualPause(false); corpses.Step(10f); Assert.That(corpses.ActiveCount, Is.Zero);
            reused.ReceiveDamage(in lethal); session.ResetSession(20261102); Assert.That(corpses.ActiveCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SpriteCrowdRendersOnTheArenaSurfaceWithBoundedPresentationCost()
        {
            new GameObject("Arena Surface").AddComponent<ArenaSurface>();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.localScale = Vector3.one * 5;
            var light = new GameObject("Key Light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2f;
            light.transform.rotation = Quaternion.Euler(40, -20, 0); RenderSettings.ambientLight = new Color(.7f,.7f,.7f);
            const int count = 120;
            var views = new EnemySpriteView[count];
            for (int i = 0; i < count; i++)
            {
                var actor = enemies.Spawn(i % 5 == 0 ? EnemyArchetypeId.Colossus : EnemyArchetypeId.Residual,
                    new Vector3((i % 12 - 5.5f) * 1.8f, 1, (i / 12) * 2f + 4f));
                actor.SimulationSuspended = true;
                actor.Body.linearVelocity = actor.AssignedMoveSpeed * Vector3.back;
                views[i] = actor.GetComponent<EnemySpriteView>();
            }
            var watch = Stopwatch.StartNew();
            for (int sample = 0; sample < 30; sample++) for (int i = 0; i < count; i++) views[i].Advance(1f / 60f);
            watch.Stop();
            double ms = watch.Elapsed.TotalMilliseconds / 30;
            UnityEngine.Debug.Log("[EnemySpriteIntegration] 120 animated enemy views: " + ms.ToString("F3") + " ms per presentation step");
            Assert.That(ms, Is.LessThan(16.67), "Presentation alone must fit within the 60 Hz CPU budget.");
            camera.transform.position = new Vector3(0, 24, -9); camera.transform.LookAt(new Vector3(0, 0, 11));
            yield return null;
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "EnemySpriteIntegration"); Directory.CreateDirectory(dir);
            var rt = new RenderTexture(1280, 720, 24); var old = RenderTexture.active;
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1280,720,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(dir,"Crowd.png"),image.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = old; rt.Release(); Object.Destroy(rt); Object.Destroy(image);
        }
    }
}
