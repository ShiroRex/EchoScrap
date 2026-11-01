using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EchoScrap.Tests
{
    public sealed class ArenaSpriteLightingTests
    {
        private CombatGameBootstrap game;

        private void Loaded(Scene scene, LoadSceneMode mode)
        {
            var station = Object.FindAnyObjectByType<FusionStation>();
            if (station != null) station.PersistProgress = false;
        }

        private IEnumerator LoadArena()
        {
            SceneTransitionState.Reset();
            SceneManager.sceneLoaded += Loaded;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            Assert.IsNotNull(game);
            game.Director.AutoTick = game.Encounter.AutoTick = false;
            yield return null;
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            SceneManager.sceneLoaded -= Loaded;
            if (game != null)
            {
                Scene scene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("ArenaSpriteLightingCleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            SceneTransitionState.Reset(); CombatTargetRegistry.Clear(); Time.timeScale = 1f;
        }

        [UnityTest] public IEnumerator CollectorUsesOneRealExteriorShadowAndRestoresWarehouseAndDisabledState()
        {
            yield return LoadArena();
            var view = game.Motor.GetComponent<CollectorSpriteView>();
            var lighting = Object.FindAnyObjectByType<ArenaLightingController>();
            var travel = Object.FindAnyObjectByType<WarehouseTravel>();
            Assert.IsNotNull(lighting);
            Assert.IsTrue(ArenaLightingController.IsExteriorActive);
            view.Advance(0f);
            Assert.IsTrue(view.IsUsingArenaShadow);
            Assert.AreEqual(ShadowCastingMode.On, view.CharacterRenderer.shadowCastingMode);
            Assert.IsFalse(view.ProjectedShadowRenderer.enabled, "The old projected silhouette must not double the shadow map.");

            view.enabled = false;
            Assert.AreEqual(ShadowCastingMode.Off, view.CharacterRenderer.shadowCastingMode);
            Assert.IsTrue(view.ProjectedShadowRenderer.enabled, "Disabling the override restores the authored renderer state.");
            view.enabled = true;
            view.Advance(0f);
            Assert.AreEqual(ShadowCastingMode.On, view.CharacterRenderer.shadowCastingMode);
            Assert.IsFalse(view.ProjectedShadowRenderer.enabled);

            Assert.IsTrue(travel.PlaceInWarehouse(true));
            yield return null;
            view.Advance(0f);
            Assert.IsFalse(ArenaLightingController.IsExteriorActive);
            Assert.IsFalse(view.IsUsingArenaShadow);
            Assert.AreEqual(ShadowCastingMode.Off, view.CharacterRenderer.shadowCastingMode);
            Assert.IsTrue(view.ProjectedShadowRenderer.enabled);

            Assert.IsTrue(travel.PlaceInWarehouse(false));
            yield return null;
            view.Advance(0f);
            Assert.IsTrue(view.IsUsingArenaShadow);
            lighting.SetLightingEnabled(false);
            view.Advance(0f);
            Assert.IsFalse(view.IsUsingArenaShadow);
            Assert.AreEqual(ShadowCastingMode.Off, view.CharacterRenderer.shadowCastingMode);
            Assert.IsTrue(view.ProjectedShadowRenderer.enabled);
            lighting.SetLightingEnabled(true);
            view.Advance(0f);
            Assert.AreEqual(ShadowCastingMode.On, view.CharacterRenderer.shadowCastingMode);
            Assert.IsFalse(view.ProjectedShadowRenderer.enabled);
        }

        [UnityTest] public IEnumerator ExteriorOverridePreservesAPreviouslyDisabledProjection()
        {
            yield return LoadArena();
            var view = game.Motor.GetComponent<CollectorSpriteView>();
            var lighting = Object.FindAnyObjectByType<ArenaLightingController>();
            lighting.SetLightingEnabled(false);
            view.Advance(0f);
            view.ProjectedShadowRenderer.enabled = false;
            lighting.SetLightingEnabled(true);
            view.Advance(0f);
            Assert.IsTrue(view.IsUsingArenaShadow);
            view.enabled = false;
            Assert.IsFalse(view.ProjectedShadowRenderer.enabled);
            Assert.AreEqual(ShadowCastingMode.Off, view.CharacterRenderer.shadowCastingMode);
        }

        [Test] public void AtlasAlphaHolesAndFrameChangesMatchAcrossFocusAndNormals()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("This comparison requires a rendering device.");
            Shader shader = Shader.Find("EchoScrap/Character Pixel Lit");
            Assert.IsNotNull(shader); Assert.IsTrue(shader.isSupported);
            var material = new Material(shader);
            var opaque = new Material(shader);
            var atlas = new Texture2D(8, 4, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[32];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++) pixels[y * 8 + x] = x == 1 && y == 1 ? Color.clear : Color.white;
            atlas.SetPixels(pixels); atlas.Apply();
            material.SetTexture("_BaseMap", atlas);
            material.SetVector("_FrameRect", new Vector4(0, 0, .5f, 1));
            opaque.SetTexture("_BaseMap", Texture2D.whiteTexture);
            opaque.SetVector("_FrameRect", new Vector4(0, 0, 1, 1));
            var mesh = new Mesh { name = "Atlas alpha pass comparison" };
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            var camera = new GameObject("Atlas pass comparison camera").AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1.1f; camera.aspect = 1f;
            camera.nearClipPlane = .1f; camera.farClipPlane = 10f;
            camera.transform.position = Vector3.back * 4f;
            var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            target.Create();
            try
            {
                foreach (string pass in new[] { "ForwardLit", "ShadowCaster", "DepthOnly", "DepthNormalsOnly", "ArenaFocusMask" })
                    Assert.That(material.FindPass(pass), Is.GreaterThanOrEqualTo(0), pass);
                Color[] mask = DrawPass(camera, target, mesh, material, "ArenaFocusMask");
                Color[] full = DrawPass(camera, target, mesh, opaque, "ArenaFocusMask");
                Assert.That(VisiblePixels(mask), Is.GreaterThan(500));
                Assert.That(VisiblePixels(full) - VisiblePixels(mask), Is.GreaterThan(50), "The transparent hole must remain a hole.");
                Color[] normals = DrawPass(camera, target, mesh, material, "DepthNormalsOnly");
                Assert.AreEqual(VisiblePixels(mask), VisiblePixels(normals), "Normals cannot fill a transparent texel or the unused atlas half.");
                material.SetVector("_FrameRect", new Vector4(.5f, 0, .5f, 1));
                Assert.AreEqual(0, VisiblePixels(DrawPass(camera, target, mesh, material, "ArenaFocusMask")), "Switching to a transparent frame must clear all protection.");
                Assert.AreEqual(0, VisiblePixels(DrawPass(camera, target, mesh, material, "DepthNormalsOnly")));
            }
            finally
            {
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material); Object.DestroyImmediate(opaque); Object.DestroyImmediate(atlas); Object.DestroyImmediate(camera.gameObject);
            }
        }

        [Test] public void GroundNormalPassAddsReliefWithoutChangingSurfaceCoverage()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("This comparison requires a rendering device.");
            Shader shader = Shader.Find("EchoScrap/WastelandGround");
            Assert.IsNotNull(shader); Assert.IsTrue(shader.isSupported);
            var material = new Material(shader);
            var mesh = new Mesh { name = "Ground normal comparison" };
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            // This diagnostic card faces the camera; preserve the winding used by the terrain.
            material.SetFloat("_Cull", 0f);
            var camera = new GameObject("Ground normal comparison camera").AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1.1f; camera.aspect = 1f;
            camera.nearClipPlane = .1f; camera.farClipPlane = 10f; camera.transform.position = Vector3.back * 4f;
            var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            target.Create();
            try
            {
                Assert.That(material.FindPass("DepthNormalsOnly"), Is.GreaterThanOrEqualTo(0));
                material.SetFloat("_ReliefStrength", 0f);
                Color[] flat = DrawPass(camera, target, mesh, material, "DepthNormalsOnly");
                material.SetFloat("_ReliefStrength", 1f);
                Color[] relief = DrawPass(camera, target, mesh, material, "DepthNormalsOnly");
                Assert.AreEqual(VisiblePixels(flat), VisiblePixels(relief), "The relief is shading, not new terrain geometry.");
                int changed = 0;
                for (int i = 0; i < flat.Length; i++)
                    if (Mathf.Abs(flat[i].r - relief[i].r) + Mathf.Abs(flat[i].g - relief[i].g) + Mathf.Abs(flat[i].b - relief[i].b) > .005f) changed++;
                Assert.Greater(changed, 50, "The depth-normals pass must contain the microrelief used by visible lighting.");
            }
            finally
            {
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material); Object.DestroyImmediate(camera.gameObject);
            }
        }

        private static int VisiblePixels(Color[] colors)
        {
            int count = 0;
            foreach (Color color in colors)
                if (Mathf.Abs(color.r) + Mathf.Abs(color.g) + Mathf.Abs(color.b) > .5f) count++;
            return count;
        }

        private static Color[] DrawPass(Camera camera, RenderTexture target, Mesh mesh, Material material, string pass)
        {
            var commands = new CommandBuffer { name = "Animated atlas geometry pass comparison" };
            Vector4 previousBias = Shader.GetGlobalVector("_ShadowBias"), previousLight = Shader.GetGlobalVector("_LightDirection");
            commands.SetRenderTarget(target);
            commands.ClearRenderTarget(true, true, Color.black);
            commands.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
            commands.SetGlobalVector("_ShadowBias", Vector4.zero);
            commands.SetGlobalVector("_LightDirection", new Vector4(0, 1, 0, 0));
            commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, material.FindPass(pass));
            commands.SetGlobalVector("_ShadowBias", previousBias); commands.SetGlobalVector("_LightDirection", previousLight);
            Graphics.ExecuteCommandBuffer(commands); commands.Release();
            RenderTexture previous = RenderTexture.active;
            var pixels = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
                return pixels.GetPixels();
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
        }
    }
}
