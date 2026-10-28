using System.Collections;
using System.Collections.Generic;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class PlayerReliefScaleTests
    {
        private CombatGameBootstrap game;
        private CollectorSpriteView view;
        private Keyboard keyboard;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior;
        private readonly List<InputDevice> disabledDevices = new List<InputDevice>();
        private readonly List<Object> temporaryObjects = new List<Object>();

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            Time.timeScale = 1f;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            foreach (InputDevice device in new List<InputDevice>(InputSystem.devices))
                if (device.enabled) { disabledDevices.Add(device); InputSystem.DisableDevice(device); }
            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return SceneManager.LoadSceneAsync("CombatArena");
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            game.ResetRun(false);
            game.Weapon.enabled = false;
            game.Enemies.DespawnAllActive();
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
            game.Session.ResumeFromFocusLoss();
            view = game.Motor.GetComponent<CollectorSpriteView>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            foreach (Object item in temporaryObjects) if (item != null) Object.DestroyImmediate(item);
            temporaryObjects.Clear();
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            foreach (InputDevice device in disabledDevices) if (device.added) InputSystem.EnableDevice(device);
            disabledDevices.Clear();
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
            if (game != null)
            {
                Scene scene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("Player Relief Cleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            CombatTargetRegistry.Clear();
        }

        [Test]
        public void AuthoredSizeIsIndependentOfTerrainCameraDepthAndRenderResolution()
        {
            var camera = new GameObject("Player scale regression camera").AddComponent<Camera>();
            temporaryObjects.Add(camera.gameObject);
            camera.CopyFrom(game.GameplayCamera);
            camera.enabled = false;
            camera.orthographic = false;
            camera.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            view.Bind(camera, game.Session);
            var body = game.Motor.GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.None;
            Vector3 expectedBounds = view.CharacterMesh.bounds.size;
            expectedBounds = Vector3.Scale(expectedBounds, new Vector3(0.65f, 0.65f * 1.6f, 0.65f));
            foreach (int renderHeight in new[] { 180, 360, 1080 })
            {
                var target = new RenderTexture(renderHeight * 16 / 9, renderHeight, 24);
                temporaryObjects.Add(target);
                camera.targetTexture = target;
                foreach (Vector3 reference in new[] { Vector3.zero, new Vector3(-24f, 0f, 3f), new Vector3(-37f, 0f, 50f) })
                {
                    Vector3 point = ArenaSurface.OnGround(ArenaSurface.ReferenceToWorld(reference), 1f);
                    body.position = point;
                    game.Motor.transform.position = point;
                    foreach (float depth in new[] { 6f, 12f, 24f, 60f })
                    {
                        camera.transform.position = point - camera.transform.forward * depth;
                        view.AlignToCamera();
                        string context = $"height={point.y}, depth={depth}, renderHeight={renderHeight}";
                        Assert.That(view.CharacterRenderer.transform.localScale.x, Is.EqualTo(0.65f).Within(0.001f), context);
                        Assert.That(view.CharacterRenderer.bounds.size.x, Is.EqualTo(expectedBounds.x).Within(0.001f), context);
                        Assert.That(view.CharacterRenderer.bounds.size.y, Is.EqualTo(expectedBounds.y).Within(0.001f), context);
                        Assert.That(view.CharacterRenderer.transform.position.y, Is.EqualTo(point.y - 0.975f).Within(0.001f), context);
                    }
                }
                camera.targetTexture = null;
            }
            view.Bind(game.GameplayCamera, game.Session);
        }

        [UnityTest]
        public IEnumerator GameplayCameraFollowsElevationWithoutIncreasingTheCharacterSize()
        {
            var body = game.Motor.GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.None;
            Vector3 referenceOffset = Vector3.zero;
            int sample = 0;
            foreach (Vector3 reference in new[] { Vector3.zero, new Vector3(-24f, 0f, 3f), new Vector3(-37f, 0f, 50f) })
            {
                Vector3 before = body.position;
                body.position = ArenaSurface.OnGround(ArenaSurface.ReferenceToWorld(reference), 1f);
                game.Motor.transform.position = body.position;
                body.linearVelocity = Vector3.zero;
                // Use the same warp contract as warehouse travel and the boss transfer. Reading the
                // camera before LateUpdate compares this physics step with the previous rendered pose.
                game.CameraRig.OnTargetObjectWarped(game.Motor.transform, body.position - before);
                game.CameraRig.PreviousStateIsValid = false;
                Physics.SyncTransforms();
                for (int i = 0; i < 4; i++) { yield return new WaitForFixedUpdate(); yield return null; }
                yield return new WaitForEndOfFrame();
                Vector3 offset = game.GameplayCamera.transform.position - body.position;
                if (sample++ == 0) referenceOffset = offset;
                Assert.That(offset.y, Is.EqualTo(referenceOffset.y).Within(0.15f), "The camera must follow the elevated ground immediately.");
                Assert.That(view.CharacterRenderer.transform.localScale.x, Is.EqualTo(0.65f).Within(0.001f));
            }
        }

        [Test]
        public void StandingPixelsMatchCommonEnemyCapsuleAndDashGhostKeepsProportions()
        {
            EnemyActor residual = game.Enemies.Spawn(EnemyArchetypeId.Residual,
                game.Motor.transform.position + Vector3.right * 3f);
            Assert.That(residual, Is.Not.Null);
            Assert.That(residual.BodyCollider.bounds.size.y, Is.EqualTo(1.8f).Within(0.01f));
            view.AlignToCamera();
            // Compare projected silhouettes. A 3D capsule's depth remains visible at 50 degrees,
            // while the upright sprite's height is foreshortened by the same camera.
            MeshFilter capsuleMesh = null;
            foreach (var mesh in residual.GetComponentsInChildren<MeshFilter>())
                if (mesh.name == "BodyMesh") capsuleMesh = mesh;
            Assert.That(capsuleMesh, Is.Not.Null);
            float referenceBottom = float.PositiveInfinity, referenceTop = float.NegativeInfinity;
            foreach (Vector3 vertex in capsuleMesh.sharedMesh.vertices)
            {
                float screenY = game.GameplayCamera.WorldToScreenPoint(capsuleMesh.transform.TransformPoint(vertex)).y;
                referenceBottom = Mathf.Min(referenceBottom, screenY);
                referenceTop = Mathf.Max(referenceTop, screenY);
            }
            float referenceHeight = referenceTop - referenceBottom;
            var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            temporaryObjects.Add(atlas);
            Assert.That(atlas.LoadImage(File.ReadAllBytes(Path.Combine(Application.dataPath,
                "_Project/Art/Characters/Coleccionista/Coleccionista_Atlas.png"))), Is.True);
            Color32[] pixels = atlas.GetPixels32();
            foreach (CharacterFacing facing in System.Enum.GetValues(typeof(CharacterFacing)))
                foreach (CharacterMotion motion in new[] { CharacterMotion.Idle, CharacterMotion.Walk })
                    foreach (CharacterSpriteSet.Frame frame in view.Sprites.GetClip(motion, facing).frames)
                    {
                        OpaquePixelRows(pixels, atlas.width, atlas.height, frame.uvRect, out int bottom, out int top);
                        Vector3 bottomWorld = view.CharacterRenderer.transform.TransformPoint(
                            new Vector3(0f, (bottom - 2f) / view.Sprites.pixelsPerUnit, 0f));
                        Vector3 topWorld = view.CharacterRenderer.transform.TransformPoint(
                            new Vector3(0f, (top + 1f - 2f) / view.Sprites.pixelsPerUnit, 0f));
                        float height = game.GameplayCamera.WorldToScreenPoint(topWorld).y
                            - game.GameplayCamera.WorldToScreenPoint(bottomWorld).y;
                        Assert.That(height / referenceHeight, Is.InRange(0.90f, 1.15f), $"{motion} {facing}: projected capsule ratio");
                    }

            var ghost = new GameObject("Collector resized dash ghost regression");
            temporaryObjects.Add(ghost);
            MeshRenderer renderer = ghost.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = view.AfterimageMaterial;
            view.CopyAfterimageTo(ghost.transform, renderer);
            Assert.That(ghost.transform.localScale, Is.EqualTo(view.CharacterRenderer.transform.lossyScale));
            Assert.That(view.ProjectedShadowRenderer.transform.lossyScale,
                Is.EqualTo(view.CharacterRenderer.transform.lossyScale));
        }

        [UnityTest]
        public IEnumerator SavedAndLiveCameraKeepFiftyDegreePitchAndAlignedFollowOffset()
        {
            var rig = game.CameraRig;
            var follow = rig.GetComponent<Unity.Cinemachine.CinemachineFollow>();
            Vector3 expectedOffsetDirection = -(Quaternion.Euler(50f, 0f, 0f) * Vector3.forward);
            Assert.That(Vector3.Angle(follow.FollowOffset, expectedOffsetDirection), Is.LessThan(0.01f));
            for (int i = 0; i < 4; i++) { yield return new WaitForFixedUpdate(); yield return null; }
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(50f, rig.transform.eulerAngles.x)), Is.LessThan(0.01f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(50f, game.GameplayCamera.transform.eulerAngles.x)), Is.LessThan(0.01f));
        }

        private static void OpaquePixelRows(Color32[] pixels, int width, int height, Vector4 frame, out int bottom, out int top)
        {
            int x0 = Mathf.RoundToInt(frame.x * width), y0 = Mathf.RoundToInt(frame.y * height);
            int columns = Mathf.RoundToInt(frame.z * width), rows = Mathf.RoundToInt(frame.w * height);
            bottom = rows; top = -1;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                    if (pixels[(y0 + y) * width + x0 + x].a > 0)
                    {
                        bottom = Mathf.Min(bottom, y);
                        top = Mathf.Max(top, y);
                    }
            Assert.That(top, Is.GreaterThanOrEqualTo(bottom), "Sprite frame must contain visible art.");
        }

        [UnityTest]
        public IEnumerator PlayerCrossesClearDoorOnReliefAndIsStoppedByItsSolidSide()
        {
            // Isolate a real physical doorway from the arena's decorative obstacles.
            GameObject.Find("Mundo3D_EchoScrap").SetActive(false);
            Vector3 start = ArenaSurface.ReferenceToWorld(new Vector3(-24f, 0f, -4f));
            float floor = ArenaSurface.Height(start);
            float topFloor = Mathf.Max(floor, ArenaSurface.Height(start + Vector3.forward * 5f));
            Vector3 door = start + Vector3.forward * 2.5f;
            float wallHeight = topFloor - floor + 3f;
            CreateBox("Door left solid side", new Vector3(door.x - 1f, floor + wallHeight * 0.5f, door.z), new Vector3(0.6f, wallHeight, 0.6f));
            CreateBox("Door right solid side", new Vector3(door.x + 1f, floor + wallHeight * 0.5f, door.z), new Vector3(0.6f, wallHeight, 0.6f));
            CreateBox("Door overhead beam", new Vector3(door.x, topFloor + 2.7f, door.z), new Vector3(2.6f, 0.6f, 0.6f));
            var body = game.Motor.GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.None;
            foreach (bool throughOpening in new[] { true, false })
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                Vector3 position = start + (throughOpening ? Vector3.zero : Vector3.right);
                body.position = ArenaSurface.OnGround(position, 1f);
                game.Motor.transform.position = body.position;
                body.linearVelocity = Vector3.zero;
                Physics.SyncTransforms();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                yield return null;
                for (int i = 0; i < 37; i++) yield return new WaitForFixedUpdate();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                if (throughOpening)
                    Assert.That(body.position.z, Is.GreaterThan(door.z + 1.5f), "A clear doorway with enough headroom must remain traversable on relief.");
                else
                    Assert.That(body.position.z, Is.LessThan(door.z - 0.5f), "A visible solid side remains impassable; do not climb onto its roof.");
                Assert.That(body.position.y, Is.EqualTo(ArenaSurface.SupportHeight(body.position, 0.45f) + 1f).Within(0.25f));
            }
        }

        private void CreateBox(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            temporaryObjects.Add(go);
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
        }
    }
}
