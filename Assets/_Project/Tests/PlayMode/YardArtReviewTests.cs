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
    public sealed class YardArtReviewTests
    {
        private CombatGameBootstrap game;
        [UnityTest]
        public IEnumerator BlenderKitHasSupportedGeometryAndRendersFromTheGameplayCamera()
        {
            yield return SceneManager.LoadSceneAsync("CombatArena"); yield return null; yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = game.Encounter.AutoTick = false; game.ResetRun(false);
            game.Session.SetManualPause(true);
            var view = game.Player.GetComponent<CollectorSpriteView>();
            var camera = game.GameplayCamera;
            camera.GetComponent<Unity.Cinemachine.CinemachineBrain>().enabled = false;
            Vector3 offset = camera.transform.position - game.Player.transform.position;
            var world = GameObject.Find("Mundo3D_EchoScrap");
            int trees = 0, pockets = 0;
            foreach (var filter in world.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.name.StartsWith("Refined ")) continue;
                if (filter.name.Contains("DeadTree")) trees++; else pockets++;
                var renderer = filter.GetComponent<MeshRenderer>();
                Assert.IsTrue(renderer.enabled); Assert.IsTrue(renderer.sharedMaterial.shader.isSupported);
                Assert.IsNotNull(renderer.sharedMaterial.GetTexture("_BumpMap"));
                uint indices = 0;
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++) indices += filter.sharedMesh.GetIndexCount(sub);
                Assert.Less(indices / 3, 11000, "Decoration must fit the geometry budget.");
                Assert.That(renderer.bounds.min.y - ArenaSurface.Height(renderer.bounds.center), Is.InRange(-.5f, .3f));
            }
            Assert.AreEqual(4, trees); Assert.AreEqual(12, pockets);
            Assert.AreEqual("Universal Render Pipeline/Lit", game.ArenaGround.GetComponent<Renderer>().sharedMaterial.shader.name);
            Vector3[] positions = { new Vector3(-24,0,12), new Vector3(32,0,22), new Vector3(-39,0,26) };
            string[] names = { "TreeAndScrap", "Industrial", "WestYard" };
            Directory.CreateDirectory("Logs/YardArtReview");
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = ArenaSurface.OnGround(positions[i], 1f);
                game.Player.GetComponent<Rigidbody>().position = p; game.Player.transform.position = p;
                camera.transform.position = p + offset; Physics.SyncTransforms(); view.AlignToCamera();
                yield return null; yield return null;
                Assert.AreEqual(1.1f, view.CharacterRenderer.transform.localScale.x, .0001f);
                var target = new RenderTexture(1600,1000,24);
                var old = camera.targetTexture; var active = RenderTexture.active;
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                var image = new Texture2D(1600,1000,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1600,1000),0,0); image.Apply();
                File.WriteAllBytes("Logs/YardArtReview/" + names[i] + ".png", image.EncodeToPNG());
                camera.targetTexture = old; RenderTexture.active = active;
                target.Release(); Object.Destroy(target); Object.Destroy(image);
            }
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            if (game == null) yield break;
            var scene = game.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("YardReviewCleanup"));
            yield return SceneManager.UnloadSceneAsync(scene); CombatTargetRegistry.Clear();
        }
    }
}
