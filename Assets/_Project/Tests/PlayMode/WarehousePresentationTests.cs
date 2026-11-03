using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EchoScrap.Tests
{
    public sealed class WarehousePresentationTests
    {
        private CombatGameBootstrap game;
        private WarehouseTravel travel;
        private float originalAspect;
        private RenderTexture viewportTarget;
        private RenderTexture originalTarget;
        private void Loaded(Scene scene, LoadSceneMode mode)
        {
            var station = Object.FindAnyObjectByType<FusionStation>();
            if (station != null) station.PersistProgress = false;
        }
        [UnitySetUp] public IEnumerator Setup()
        {
            SceneTransitionState.Reset(); SceneTransitionState.SaveProgress = false;
            SceneManager.sceneLoaded += Loaded;
            yield return SceneManager.LoadSceneAsync("CombatArena"); yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>(); Assert.IsNotNull(game);
            game.Director.AutoTick = game.Encounter.AutoTick = false; game.Weapon.enabled = false;
            game.Session.ResumeFromFocusLoss();
            originalAspect = game.GameplayCamera.aspect;
            originalTarget = game.GameplayCamera.targetTexture;
            viewportTarget = new RenderTexture(1280, 720, 24); viewportTarget.Create();
            game.GameplayCamera.targetTexture = viewportTarget;
            travel = Object.FindAnyObjectByType<WarehouseTravel>();
            foreach (var gateway in Object.FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None)) gateway.GetComponent<Collider>().enabled = false;
            Assert.IsTrue(travel.PlaceInWarehouse(true));
            for (int i = 0; i < 5; i++) yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            SceneManager.sceneLoaded -= Loaded;
            if (game != null)
            {
                game.GameplayCamera.aspect = originalAspect;
                game.GameplayCamera.targetTexture = originalTarget;
                Scene scene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("Warehouse presentation cleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            if (viewportTarget != null) { viewportTarget.Release(); Object.DestroyImmediate(viewportTarget); }
            SceneTransitionState.Reset(); CombatTargetRegistry.Clear(); Time.timeScale = 1;
        }
        [UnityTest] public IEnumerator FullViewStaysOverFloorAndPlayerStaysOnScreenAtWallsAndBalcony()
        {
            var guard = game.CameraRig.GetComponent<WarehouseCameraFraming>(); Assert.IsTrue(guard.IsConfining);
            Assert.IsTrue(game.GameplayCamera.orthographic);
            Bounds room = guard.RoomBounds;
            Bounds walkable = guard.WalkableBounds;
            Vector3[] points = {
                walkable.center, new Vector3(walkable.min.x + .75f, 0, 0), new Vector3(walkable.max.x - .75f, 0, 0),
                new Vector3(walkable.center.x, 0, walkable.min.z + .75f), new Vector3(walkable.center.x, 0, walkable.max.z - .75f),
                new Vector3(walkable.min.x + .75f, 0, walkable.min.z + .75f), new Vector3(walkable.max.x - .75f, 0, walkable.min.z + .75f),
                new Vector3(walkable.min.x + .75f, 0, walkable.max.z - .75f), new Vector3(walkable.max.x - .75f, 0, walkable.max.z - .75f),
                new Vector3(167, 5, 23), new Vector3(167, 5, 29), new Vector3(160, 2.5f, 8)
            };
            foreach (float aspect in new[] { 4f / 3, 16f / 9, 21f / 9, 9f / 16 })
            foreach (Vector3 point in points)
            {
                game.GameplayCamera.aspect = aspect; Place(point);
                for (int frame = 0; frame < 5; frame++) yield return null;
                var camera = game.GameplayCamera;
                Assert.That(camera.transform.position.x, Is.InRange(room.min.x - .025f, room.max.x + .025f), "Camera outside the side walls at " + point);
                Assert.That(camera.transform.position.z, Is.InRange(room.min.z - .025f, room.max.z + .025f), "Camera behind a foreground wall at " + point);
                var floor = new Plane(Vector3.up, new Vector3(0, room.max.y, 0));
                for (int corner = 0; corner < 4; corner++)
                {
                    Ray ray = camera.ViewportPointToRay(new Vector3(corner % 2, corner / 2, 0));
                    Assert.IsTrue(floor.Raycast(ray, out float distance), "The vault view cannot look above the horizon.");
                    Vector3 hit = ray.GetPoint(distance);
                    Assert.That(hit.x, Is.InRange(room.min.x - .025f, room.max.x + .025f), point + " aspect=" + aspect);
                    Assert.That(hit.z, Is.InRange(room.min.z - .025f, room.max.z + .025f), point + " aspect=" + aspect);
                }
                Bounds body = game.Player.GetComponent<Collider>().bounds;
                var character = game.Player.GetComponent<CollectorSpriteView>();
                Bounds card = character.CharacterMesh.bounds;
                Assert.Less(Vector3.Angle(character.CharacterRenderer.transform.up, Vector3.up), .01f, "The sprite must stay upright and grounded, including at the walls.");
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 p = character.CharacterRenderer.transform.TransformPoint(new Vector3(
                        (corner & 1) == 0 ? card.min.x : card.max.x, (corner & 2) == 0 ? card.min.y : card.max.y, card.center.z));
                    Vector3 viewport = camera.WorldToViewportPoint(p);
                    Assert.That(viewport.x, Is.InRange(0f, 1f), "Sprite card clipped at " + point + " aspect=" + aspect);
                    Assert.That(viewport.y, Is.InRange(0f, 1f), "Sprite card clipped at " + point + " aspect=" + aspect
                        + " body="+body.center+" sprite="+character.CharacterRenderer.transform.position
                        + " pitch="+camera.transform.eulerAngles.x+" fov="+camera.fieldOfView
                        + " predicted="+camera.WorldToViewportPoint(character.GetFramingCorner(corner,camera.transform.rotation))
                        + " view pitch="+character.CharacterRenderer.transform.eulerAngles.x+" pixelHeight="+camera.pixelHeight
                        + " scale="+character.CharacterRenderer.transform.lossyScale+" world="+p+" room="+room);
                }
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 p = body.center + Vector3.Scale(body.extents, new Vector3((corner & 1) == 0 ? -1 : 1,
                        (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    Vector3 viewport = camera.WorldToViewportPoint(p);
                    Assert.That(viewport.x, Is.InRange(.005f, .995f), "Player clipped at " + point + " aspect=" + aspect);
                    Assert.That(viewport.y, Is.InRange(.005f, .995f), "Player clipped at " + point + " aspect=" + aspect);
                }
            }
        }
        [UnityTest] public IEnumerator OpaqueObstaclesRevealOnlyTheOccludedPlayerEvenWithoutDecoration()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires a rendering device.");
            Place(new Vector3(180, 0, 0)); for (int i = 0; i < 5; i++) yield return null;
            var view = game.Player.GetComponent<CollectorSpriteView>(); var renderer = view.CharacterRenderer;
            var camera = game.GameplayCamera;
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(blocker.GetComponent<Collider>());
            blocker.transform.SetPositionAndRotation(game.Player.transform.position - camera.transform.forward * 2, camera.transform.rotation);
            blocker.transform.localScale = new Vector3(5, 5, .4f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.SetColor("_BaseColor", new Color(.01f, .01f, .015f));
            blocker.GetComponent<MeshRenderer>().sharedMaterial = material; blocker.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            uint layers = renderer.renderingLayerMask;
            try
            {
                foreach (bool decoration in new[] { true, false })
                {
                    game.Lighting.SetDecorationEnabled(decoration);
                    game.Session.Effects.ReducedEffectsMode = !decoration;
                    Assert.IsTrue(ArenaLightingView.TryGetForCamera(camera, out _));
                    renderer.renderingLayerMask = layers;
                    Color[] visible = RenderCamera(camera);
                    renderer.renderingLayerMask = layers & ~ArenaLightingRig.CollectorVisibilityRenderingLayer;
                    Color[] hidden = RenderCamera(camera);
                    int changed = 0;
                    for (int i = 0; i < visible.Length; i++)
                        if (Mathf.Abs(visible[i].r - hidden[i].r) + Mathf.Abs(visible[i].g - hidden[i].g) + Mathf.Abs(visible[i].b - hidden[i].b) > .05f) changed++;
                    Assert.Greater(changed, 100, "The actual GPU pass must reveal the player behind an opaque obstacle; decoration=" + decoration);
                }
            }
            finally { renderer.renderingLayerMask = layers; Object.DestroyImmediate(blocker); Object.DestroyImmediate(material); }
        }
        [UnityTest] public IEnumerator ReturningAndResettingRestoreTheExteriorFraming()
        {
            var guard = game.CameraRig.GetComponent<WarehouseCameraFraming>();
            Assert.IsTrue(guard.IsConfining); Assert.IsTrue(travel.PlaceInWarehouse(false)); yield return null;
            Assert.IsFalse(guard.IsConfining);
            Assert.IsFalse(game.GameplayCamera.orthographic);
            Assert.AreEqual(game.Lighting.Profile.CameraPitch, game.CameraRig.transform.eulerAngles.x, .01f);
            Assert.AreEqual(game.Lighting.Profile.CameraFieldOfView, game.CameraRig.Lens.FieldOfView, .01f);
            Assert.IsTrue(travel.PlaceInWarehouse(true)); yield return null; game.ResetRun(); yield return null;
            Assert.IsFalse(guard.IsConfining); Assert.IsTrue(ArenaLightingController.IsExteriorActive);
        }
        private void Place(Vector3 point)
        {
            var body = game.Player.GetComponent<Rigidbody>(); Vector3 old = body.position;
            point = ArenaSurface.OnGround(point, 1); game.Motor.Stop(); body.position = game.Player.transform.position = point; body.linearVelocity = Vector3.zero;
            game.CameraRig.OnTargetObjectWarped(game.Player.transform, point - old); game.CameraRig.PreviousStateIsValid = false; Physics.SyncTransforms();
        }
        public static Color[] RenderCamera(Camera camera)
        {
            var target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32); target.Create();
            var previous = camera.targetTexture; bool enabled = camera.enabled; var active = RenderTexture.active;
            var pixels = new Texture2D(512, 512, TextureFormat.RGB24, false);
            try
            {
                camera.enabled = false; camera.targetTexture = target;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); pixels.Apply(); return pixels.GetPixels();
            }
            finally
            {
                camera.targetTexture = previous; camera.enabled = enabled; RenderTexture.active = active;
                Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target);
            }
        }
    }
}
