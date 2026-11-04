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
        [UnityTest] public IEnumerator OriginalPerspectiveAndPlayerStayIntactAtWallsAndBalcony()
        {
            var guard = game.CameraRig.GetComponent<WarehouseCameraFraming>(); Assert.IsTrue(guard.IsConfining);
            Assert.IsFalse(game.GameplayCamera.orthographic);
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
                Assert.IsFalse(camera.orthographic);
                Assert.AreEqual(65f,camera.fieldOfView,.01f,"Wall limits must not zoom the camera.");
                Assert.Less(Quaternion.Angle(camera.transform.rotation,Quaternion.Euler(32,0,0)),.01f,"Wall limits must not tilt the camera.");
                Assert.Less(Vector3.Distance(game.CameraRig.GetComponent<Unity.Cinemachine.CinemachineFollow>().FollowOffset,
                    Quaternion.Euler(32,0,0)*Vector3.back*10f),.01f,"Keep the original follow distance.");
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
                        + " predicted="+camera.WorldToViewportPoint(character.GetFramingCorner(corner))
                        + " view pitch="+character.CharacterRenderer.transform.eulerAngles.x+" pixelHeight="+camera.pixelHeight
                        + " scale="+character.CharacterRenderer.transform.lossyScale+" world="+p);
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
        [UnityTest] public IEnumerator CameraStopsAtTheWallsWhileTheCollectorContinuesMoving()
        {
            game.GameplayCamera.aspect = 16f / 9f;
            Bounds room = game.CameraRig.GetComponent<WarehouseCameraFraming>().WalkableBounds;
            foreach (Vector3 direction in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                Vector3 wall = room.center + Vector3.Scale(room.extents, direction); wall.y = 0;
                Place(wall - direction * 3f); for (int i = 0; i < 6; i++) yield return null;
                Vector3 before = game.GameplayCamera.transform.position, playerBefore = game.Player.transform.position;
                Place(wall - direction * .75f); for (int i = 0; i < 6; i++) yield return null;
                Assert.Greater(Vector3.Dot(game.Player.transform.position - playerBefore,direction),2f,"The camera boundary must not stop the player.");
                Assert.Less(Mathf.Abs(Vector3.Dot(game.GameplayCamera.transform.position - before,direction)),.025f,
                    "The camera must stay at the wall while the player continues: " + direction);
            }
        }
        [UnityTest] public IEnumerator FloorBoundaryBlendsIntoTheBackgroundWithoutAVisibleCut()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires a rendering device.");
            var guard = game.CameraRig.GetComponent<WarehouseCameraFraming>();
            Assert.IsNotNull(guard.BoundaryFadeRenderer); Assert.IsTrue(guard.BoundaryFadeRenderer.sharedMaterial.shader.isSupported);
            Assert.IsNull(guard.BoundaryFadeRenderer.GetComponent<Collider>(),"The backdrop must not extend the playable area.");
            Bounds outer = guard.RoomBounds, inner = guard.WalkableBounds;
            Assert.Greater(outer.max.x - inner.max.x,travel.InteriorBoundaryFadeWidth);
            Assert.Greater(inner.min.x - outer.min.x,travel.InteriorBoundaryFadeWidth);
            Assert.Greater(outer.max.z - inner.max.z,travel.InteriorBoundaryFadeWidth);
            Assert.Greater(inner.min.z - outer.min.z,travel.InteriorBoundaryFadeWidth);
            var probe = new GameObject("Vault boundary probe camera").AddComponent<Camera>();
            try
            {
                probe.CopyFrom(game.GameplayCamera); probe.orthographic = true; probe.orthographicSize = .2f;
                probe.GetUniversalAdditionalCameraData().SetRenderer(game.Lighting.Profile.RendererIndex);
                probe.transform.SetPositionAndRotation(new Vector3(outer.max.x-.5f,10,outer.center.z),Quaternion.Euler(90,0,0));
                Color edge = RenderCamera(probe)[256*512+256];
                probe.transform.position += Vector3.right*2f;
                Color background = RenderCamera(probe)[256*512+256];
                Assert.Less(Mathf.Abs(edge.r-background.r)+Mathf.Abs(edge.g-background.g)+Mathf.Abs(edge.b-background.b),.02f,
                    "The outer floor must meet the real rendered background without a hard edge.");
            }
            finally { Object.DestroyImmediate(probe.gameObject); }
            yield return null;
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
