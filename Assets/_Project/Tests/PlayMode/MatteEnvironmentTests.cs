using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EchoScrap.Tests
{
    public sealed class MatteEnvironmentTests
    {
        private const string TextureRoot = "Assets/_Project/Art/Mundo3D/Texturas/";
        private CombatGameBootstrap game;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CombatArena", LoadSceneMode.Single);
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            game.ResetRun(false);
            game.Session.SetManualPause(true);
            game.Hud.CanvasRoot.SetActive(false);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            if (game != null)
            {
                var scene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("MatteEnvironmentCleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            CombatTargetRegistry.Clear();
        }

#if UNITY_EDITOR
        [Test]
        public void AllTwentyNineImportedPropMaterialsAreMatteAndRetainTheirTextureMaps()
        {
            var paths = Directory.GetFiles(TextureRoot, "*.mat", SearchOption.AllDirectories);
            Assert.That(paths.Length, Is.EqualTo(29));
            foreach (string path in paths)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path.Replace('\\', '/'));
                Assert.That(material, Is.Not.Null, path);
                string shader = path.Replace('\\', '/').StartsWith(TextureRoot + "casa/")
                    ? "EchoScrap/Cabin Roof" : "Universal Render Pipeline/Lit";
                Assert.That(material.shader.name, Is.EqualTo(shader), path);
                Assert.That(material.GetFloat("_SpecularHighlights"), Is.Zero, path);
                Assert.That(material.GetFloat("_EnvironmentReflections"), Is.Zero, path);
                Assert.That(material.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"), Is.True, path);
                Assert.That(material.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"), Is.True, path);
                Assert.That(material.GetTexture("_BaseMap"), Is.Not.Null, path);
                Assert.That(material.GetTexture("_BumpMap"), Is.Not.Null, path);
                Assert.That(material.GetTexture("_MetallicGlossMap"), Is.Not.Null, path);
                Assert.That(material.GetFloat("_ReceiveShadows"), Is.EqualTo(1f), path);
            }
        }

        [UnityTest]
        public IEnumerator RealArenaPropsLoseWhiteReflectionsWhenTheirSpecularResponseIsDisabled()
        {
            var world = GameObject.Find("Mundo3D_EchoScrap");
            Assert.That(world, Is.Not.Null);
            var renderers = world.GetComponentsInChildren<Renderer>(true);
            var originals = new Dictionary<Renderer, Material[]>();
            var clones = new Dictionary<Material, Material>();
            Renderer bags = renderers
                .Where(r => r.sharedMaterials.Any(m => m != null && AssetDatabase.GetAssetPath(m).Replace('\\','/').StartsWith(TextureRoot + "bolsa_basura/")))
                .OrderBy(r => (r.bounds.center - game.Player.WorldCenter).sqrMagnitude)
                .FirstOrDefault();
            Assert.That(bags, Is.Not.Null, "Capture an actual garbage prop from the playable map.");
            var camera = new GameObject("Matte environment audit camera").AddComponent<Camera>();
            Vector3 focus = bags.bounds.center;
            float distance = Mathf.Max(9f, bags.bounds.size.magnitude * 1.7f);
            camera.transform.position = focus + new Vector3(.4f, .9f, -1.2f).normalized * distance;
            camera.transform.LookAt(focus);
            camera.fieldOfView = 50f;
            camera.farClipPlane = 300f;
            try
            {
                foreach (var renderer in renderers)
                {
                    var source = renderer.sharedMaterials;
                    bool changed = false;
                    var replacement = new Material[source.Length];
                    for (int i = 0; i < source.Length; i++)
                    {
                        var material = source[i];
                        if (material != null && AssetDatabase.GetAssetPath(material).Replace('\\','/').StartsWith(TextureRoot))
                        {
                            if (!clones.TryGetValue(material, out var clone))
                            {
                                clone = new Material(material) { name = material.name + " temporary specular comparison" };
                                SetReflections(clone, true);
                                clones.Add(material, clone);
                            }
                            replacement[i] = clone;
                            changed = true;
                        }
                        else replacement[i] = material;
                    }
                    if (!changed) continue;
                    originals.Add(renderer, source);
                    renderer.sharedMaterials = replacement;
                }
                Assert.That(clones.Count, Is.GreaterThan(20), "Compare the imported materials actually used by the arena.");
                Color32[] before = null, after = null;
                yield return Capture(camera, "Specular_Before", pixels => before = pixels);
                foreach (var clone in clones.Values) SetReflections(clone, false);
                yield return Capture(camera, "Matte_After", pixels => after = pixels);
                int changedPixels = 0;
                long beforeBrightness = 0, afterBrightness = 0;
                for (int i = 0; i < before.Length; i++)
                {
                    int b = before[i].r + before[i].g + before[i].b;
                    int a = after[i].r + after[i].g + after[i].b;
                    beforeBrightness += b; afterBrightness += a;
                    if (Mathf.Abs(b-a) > 3) changedPixels++;
                }
                Assert.That(changedPixels, Is.GreaterThan(100), "The switch must remove visible reflected light, not merely change serialized flags.");
                Assert.That(afterBrightness, Is.LessThan(beforeBrightness), "Suppressing specular light must reduce the captured highlights.");
                Debug.Log($"[Matte environment] {clones.Count} scene materials compared; {changedPixels} pixels changed; RGB sum {beforeBrightness} -> {afterBrightness}.");
            }
            finally
            {
                foreach (var pair in originals) if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
                foreach (var clone in clones.Values) Object.Destroy(clone);
                Object.Destroy(camera.gameObject);
            }
        }

        private static void SetReflections(Material material, bool enabled)
        {
            material.SetFloat("_SpecularHighlights", enabled ? 1f : 0f);
            material.SetFloat("_EnvironmentReflections", enabled ? 1f : 0f);
            if (enabled)
            {
                material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
                material.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            }
            else
            {
                material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            }
        }

        private static IEnumerator Capture(Camera camera, string name, System.Action<Color32[]> pixels)
        {
            const int width = 1280, height = 720;
            var target = new RenderTexture(width, height, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            Texture2D image = null;
            try
            {
                camera.targetTexture = target;
                yield return null; yield return null; yield return null;
                RenderTexture.active = target;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                pixels(image.GetPixels32());
                string folder = Path.Combine(Application.dataPath, "..", "Logs", "MatteEnvironment");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                target.Release(); Object.Destroy(target);
                if (image != null) Object.Destroy(image);
            }
        }
#endif
    }
}
