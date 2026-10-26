using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class AcidPresentationTests
    {
        private Scene testScene;
        private CorrosivePuddlePool pool;
        private Camera camera;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            testScene = SceneManager.CreateScene("MortarAcidPresentation");
            SceneManager.SetActiveScene(testScene);
            camera = new GameObject("AcidTestCamera").AddComponent<Camera>();
            camera.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            pool = new GameObject("CorrosivePuddlePool").AddComponent<CorrosivePuddlePool>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (testScene.IsValid() && testScene.isLoaded)
            {
                if (SceneManager.sceneCount <= 1)
                    SceneManager.SetActiveScene(SceneManager.CreateScene("MortarAcidCleanup"));
                yield return SceneManager.UnloadSceneAsync(testScene);
            }
        }

        [Test]
        public void DrawnAcidAtlasesPreserveFramesPointFilteringAndEmissionMaps()
        {
            MortarAcidSet set = MortarAcidSet.Load();
            Assert.That(set, Is.Not.Null, "Run EchoScrap/Art/Connect Mortar Acid after exporting the Aseprite sources.");
            Assert.That(set.IsReady, Is.True);
            CheckAtlas(set.projectileMaterial, 64, 16);
            CheckAtlas(set.impactMaterial, 192, 32);
            CheckAtlas(set.puddleMaterial, 256, 64);
            Assert.That(pool.GetComponentsInChildren<Light>(true), Is.Empty, "Acid emission uses masks, without one light per pool slot.");
        }

        [UnityTest]
        public IEnumerator FlyingAcidUsesDrawnBillboardAndAdvancesOnlyWithThePool()
        {
            var spec = CorrosivePuddlePool.ProjectileSpec.CreateMortarDefault(1,
                new Vector3(0f, 4f, 4f), new Vector3(2f, 0f, 0f));
            Assert.That(pool.TryLaunchParabolicProjectile(in spec), Is.True);
            Transform shell = pool.transform.Find("MortarProjectileSlot_0/ParabolicShell");
            Assert.That(shell.GetComponent<Collider>(), Is.Null);
            Assert.That(shell.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(4));
            Assert.That(shell.GetComponent<Renderer>().sharedMaterial, Is.EqualTo(MortarAcidSet.Load().projectileMaterial));
            pool.StepPool(0.15f);
            Assert.That(Frame(shell.GetComponent<Renderer>()).x, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(pool.ReservedPuddleCount, Is.EqualTo(1));
            Time.timeScale = 0f;
            pool.StepPool(5f);
            Assert.That(Frame(shell.GetComponent<Renderer>()).x, Is.EqualTo(0.5f).Within(0.0001f));
            yield return null;
            Assert.That(Quaternion.Angle(shell.rotation, camera.transform.rotation), Is.LessThan(0.01f));
            Time.timeScale = 1f;
            pool.StepPool(0.5f);
            Assert.That(pool.ActiveProjectileCount, Is.Zero);
            Assert.That(pool.ReservedPuddleCount, Is.Zero);
            Assert.That(pool.ActivePuddleCount, Is.EqualTo(1));
            Transform impact = pool.transform.Find("CorrosivePuddleSlot_0/LandingRipple");
            Assert.That(impact.gameObject.activeInHierarchy, Is.True);
            Assert.That(impact.GetComponent<Renderer>().sharedMaterial, Is.EqualTo(MortarAcidSet.Load().impactMaterial));
            pool.StepPool(0.31f);
            Assert.That(impact.gameObject.activeSelf, Is.False);
            Assert.That(pool.ActivePuddleCount, Is.EqualTo(1), "Splash completion cannot shorten the damaging puddle.");
        }

        [Test]
        public void HillPuddlesKeepExactHorizontalDangerRadiusAndBorderUntilExpiry()
        {
            new GameObject("ArenaSurface").AddComponent<ArenaSurface>();
            Vector3 center = new Vector3(-19f, 0f, 3f);
            const float radius = 1.2f;
            int id = pool.SpawnPuddle(4, center, radius, 1f);
            Assert.That(id, Is.GreaterThan(0));
            Transform border = pool.transform.Find("CorrosivePuddleSlot_0/SharpOuterBorder");
            Assert.That(Vector3.Angle(border.up, ArenaSurface.Normal(center)), Is.LessThan(0.01f));
            Assert.That(border.localScale.x, Is.EqualTo(radius * 2f).Within(0.0001f));
            Vector3[] vertices = border.GetComponent<MeshFilter>().sharedMesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = border.TransformPoint(vertices[i]);
                float distance = new Vector2(world.x - center.x, world.z - center.z).magnitude;
                Assert.That(distance, Is.EqualTo(radius * (i % 2 == 0 ? 1f : 0.93f)).Within(0.0001f));
                Assert.That(world.y, Is.EqualTo(ArenaSurface.Height(world) + 0.033f).Within(0.0001f));
            }
            pool.StepPool(0.99f);
            Assert.That(pool.ContainsSequenceId(id), Is.True);
            Assert.That(pool.GetPuddleBySequenceId(id).FadeAlpha, Is.LessThan(0.03f));
            Assert.That(border.gameObject.activeInHierarchy, Is.True);
            var properties = new MaterialPropertyBlock();
            border.GetComponent<Renderer>().GetPropertyBlock(properties);
            Assert.That(properties.GetColor("_BaseColor").a, Is.EqualTo(0.95f));
            pool.StepPool(0.01f);
            Assert.That(pool.ContainsSequenceId(id), Is.False);
            Assert.That(border.gameObject.activeInHierarchy, Is.False);
        }

        [Test]
        public void CoreBubblesAnimateAndDryInsideAStableDangerBoundary()
        {
            pool.SpawnPuddle(1, Vector3.zero, 1.2f, 1f);
            Transform core = pool.transform.Find("CorrosivePuddleSlot_0/AcidCoreFill");
            Vector4 initialFrame = Frame(core.GetComponent<Renderer>());
            pool.StepPool(0.17f);
            Assert.That(Frame(core.GetComponent<Renderer>()).x, Is.Not.EqualTo(initialFrame.x));
            pool.StepPool(0.82f);
            var properties = new MaterialPropertyBlock();
            core.GetComponent<Renderer>().GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_GlowStrength"), Is.LessThan(0.02f));
            Assert.That(properties.GetColor("_BaseColor").a, Is.GreaterThan(0f));
            Assert.That(pool.ActivePuddleCount, Is.EqualTo(1));
        }

        private static Vector4 Frame(Renderer renderer)
        {
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            return properties.GetVector("_FrameRect");
        }

        private static void CheckAtlas(Material material, int width, int height)
        {
            Assert.That(material.shader.name, Is.EqualTo("EchoScrap/Mortar Acid"));
            Texture2D color = material.GetTexture("_BaseMap") as Texture2D;
            Texture2D emission = material.GetTexture("_GlowMap") as Texture2D;
            Assert.That(color, Is.Not.Null);
            Assert.That(emission, Is.Not.Null);
            Assert.That(color.width, Is.EqualTo(width));
            Assert.That(color.height, Is.EqualTo(height));
            Assert.That(emission.width, Is.EqualTo(width));
            Assert.That(emission.height, Is.EqualTo(height));
            Assert.That(color.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(emission.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(color.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(color.mipmapCount, Is.EqualTo(1));
            Assert.That(emission.mipmapCount, Is.EqualTo(1));
            Assert.That(color.isDataSRGB, Is.True);
            Assert.That(emission.isDataSRGB, Is.False);
        }
    }
}
