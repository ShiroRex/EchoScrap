using System.Collections;
using System.IO;
using System.Linq;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class WorldArtIntegrationTests
    {
        private CombatGameBootstrap game;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            game.ResetRun(false);
            game.Weapon.enabled = false;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            if (game != null)
            {
                var scene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("WorldArtCleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            CombatTargetRegistry.Clear();
        }

        [UnityTest]
        public IEnumerator AuthoredMap_HasTexturedModelsClearBossAreaAndReferenceLandmarks()
        {
            var world = GameObject.Find("Mundo3D_EchoScrap");
            Assert.IsNotNull(world);
            var models = world.GetComponentsInChildren<Renderer>().Where(r => r.GetComponent<MeshFilter>() != null && r.transform.parent != null && r.transform.parent.name != "Emblema_triangular").ToArray();
            Assert.Greater(models.Length, 70);
            foreach (var renderer in models.Where(r => r.name != "Emblema_triangular" && r.name != "PortalVortex"))
                foreach (var material in renderer.sharedMaterials)
                {
                    Assert.AreEqual("Universal Render Pipeline/Lit", material.shader.name, renderer.name);
                    Assert.IsNotNull(material.GetTexture("_BaseMap"), renderer.name);
                }
            Assert.IsNotNull(world.transform.Find("Emblema_triangular"));
            Assert.IsNotNull(world.transform.Find("Portal/portal_43_-40"));
            Assert.IsFalse(GameObject.Find("Arena_120m").transform.Find("FloorGuides_10m").gameObject.activeSelf);
            foreach (var collider in world.GetComponentsInChildren<Collider>())
                Assert.Greater(Vector3.Distance(collider.bounds.ClosestPoint(new Vector3(0, 1, 0)), new Vector3(0, 1, 0)), 5f, collider.name);
            var camera = new GameObject("MapAuditCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 115, -85);
            camera.transform.LookAt(Vector3.zero);
            camera.orthographic = true; camera.orthographicSize = 80;
            camera.farClipPlane = 400;
            yield return Capture(camera, "Mapa", 1200, 1200);
            Object.Destroy(camera.gameObject);
        }

        [UnityTest]
        public IEnumerator Boss_EntersAt180Seconds_UsesImportedRigHammerAndSynchronizedAttacks()
        {
            Assert.AreEqual(180f, game.WaveSettings.BossStartTime);
            game.Session.Clock.SetGameplayTimeForTesting(179.99f);
            game.Director.Step();
            Assert.IsFalse(game.Encounter.IsEncounterActive);
            game.Session.Clock.SetGameplayTimeForTesting(180f);
            game.Director.Step();
            Assert.IsTrue(game.Encounter.IsEncounterActive);
            var boss = game.Encounter.Boss;
            boss.AutoTick = false;
            var view = boss.GetComponent<MataColeccionistaView>();
            Assert.IsTrue(view.IsReady);
            var hand = boss.GetComponentsInChildren<Transform>().First(t => t.name.EndsWith("RightHand"));
            Assert.IsNotNull(hand.Find("HammerGrip"));
            game.Encounter.Step(BossEncounter.IntroDuration);
            Assert.IsTrue(boss.IsCombatActive);
            var camera = new GameObject("BossAuditCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(10, 8, 13);
            camera.transform.LookAt(new Vector3(0, 3, 0));
            camera.fieldOfView = 48;
            game.Player.transform.position = new Vector3(0, 1, 5.7f);
            game.Player.GetComponent<Rigidbody>().position = game.Player.transform.position;
            Physics.SyncTransforms();
            boss.Step(2f);
            Assert.AreEqual("HammerSlam", view.CurrentClip);
            boss.Step(0.8f);
            yield return Capture(camera, "Jefe_Preparacion", 1000, 800);
            boss.Step(0.4f);
            Assert.AreEqual(1, boss.StompsExecuted);
            Assert.AreEqual(0.35f, view.NormalizedPose, 0.001f);
            yield return Capture(camera, "Jefe_Impacto", 1000, 800);
            game.Session.SetManualPause(true);
            float held = view.NormalizedPose;
            boss.Step(1f);
            Assert.AreEqual(held, view.NormalizedPose);
            game.Session.SetManualPause(false);
            boss.Step(GolgoraActor.RecoveryDuration);
            game.Player.transform.position = new Vector3(0, 1, 20);
            game.Player.GetComponent<Rigidbody>().position = game.Player.transform.position;
            boss.Step(2f);
            Assert.AreEqual("ProjectileThrow", view.CurrentClip);
            boss.Step(GolgoraActor.BarrageWindupDuration);
            Assert.AreEqual(1, boss.BarragesExecuted);
            yield return Capture(camera, "Jefe_Lanzamiento", 1000, 800);
            Object.Destroy(camera.gameObject);
            game.ResetRun(false);
            Assert.IsFalse(game.Encounter.IsEncounterActive);
        }

        private static IEnumerator Capture(Camera camera, string name, int width, int height)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs/WorldArtIntegration");
            Directory.CreateDirectory(folder);
            var target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            yield return null; yield return null;
            var old = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = null;
            Object.Destroy(image); Object.Destroy(target);
        }

        [UnityTest]
        public IEnumerator ImportedClips_StayNearCollider_AndWalkingFeetRestOnGround()
        {
            game.Session.SetManualPause(true);
            var model = Object.Instantiate(Resources.Load<GameObject>("MataColeccionista/MataColeccionista"));
            model.transform.localScale = Vector3.one * 3.5f;
            var animator = model.GetComponentInChildren<Animator>();
            animator.applyRootMotion = false; animator.speed = 0;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = new Mesh();
            var camera = new GameObject("ClipAuditCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(10,8,13);
            camera.transform.LookAt(new Vector3(0,3,0)); camera.fieldOfView = 48;
            foreach (string clip in new[] { "Spawn", "Walk", "WalkHeavy", "Run", "HammerSlam", "HeavySweep", "ProjectileThrow" })
                foreach (float time in new[] { 0f, 0.25f, 0.5f, 0.75f, 0.99f })
                {
                    animator.Play(clip, 0, time); animator.Update(0);
                    skin.BakeMesh(mesh);
                    var vertices = mesh.vertices;
                    Bounds bounds = new Bounds(skin.transform.TransformPoint(vertices[0]), Vector3.zero);
                    foreach (var vertex in vertices) bounds.Encapsulate(skin.transform.TransformPoint(vertex));
                    Assert.Less(new Vector2(bounds.center.x, bounds.center.z).magnitude, 3f, clip + " drift " + time);
                    if (clip == "Walk" && time == 0f)
                        Assert.That(bounds.min.y, Is.InRange(-0.12f, 0.12f), "Standing feet must meet the floor.");
                    if (time == 0.5f && (clip == "Spawn" || clip == "WalkHeavy" || clip == "HeavySweep"))
                        yield return Capture(camera, "Clip_" + clip, 1000, 800);
                }
            Object.Destroy(mesh); Object.Destroy(model); Object.Destroy(camera.gameObject);
        }
    }
}
