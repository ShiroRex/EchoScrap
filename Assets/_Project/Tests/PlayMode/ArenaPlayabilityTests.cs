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
    public sealed class ArenaPlayabilityTests
    {
        private CombatGameBootstrap game;
        private Keyboard keyboard;
        private readonly List<InputDevice> disabledDevices = new List<InputDevice>();
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        [UnitySetUp] public IEnumerator Setup()
        {
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            foreach (var device in InputSystem.devices)
                if (device.enabled) { disabledDevices.Add(device); InputSystem.DisableDevice(device); }
            keyboard = InputSystem.AddDevice<Keyboard>(); Time.timeScale = 1;
            yield return SceneManager.LoadSceneAsync("CombatArena"); yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = game.Encounter.AutoTick = false; game.ResetRun(false); game.Weapon.enabled = false;
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
            game.Session.ResumeFromFocusLoss();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.timeScale = 1;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            foreach (var device in disabledDevices) if (device.added) InputSystem.EnableDevice(device);
            disabledDevices.Clear();
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            if (game != null)
            {
                var scene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("ArenaPlayabilityCleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            CombatTargetRegistry.Clear();
        }
        private void MovePlayer(Vector3 position)
        {
            position = ArenaSurface.OnGround(position, 1f);
            var body = game.Player.GetComponent<Rigidbody>(); body.position = position; body.linearVelocity = Vector3.zero;
            game.Player.transform.position = position; Physics.SyncTransforms();
        }

        [UnityTest] public IEnumerator CharacterKeepsItsSizeWhileActuallyWalkingUpTheHill()
        {
            MovePlayer(new Vector3(-9, 0, 3));
            var view = game.Motor.GetComponent<CollectorSpriteView>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A)); yield return null;
            float minimum = float.MaxValue, maximum = 0;
            for (int i = 0; i < 115; i++)
            {
                yield return new WaitForFixedUpdate();
                Assert.AreEqual(1.1f, view.CharacterRenderer.transform.localScale.x, 0.0001f);
                if (i < 15) continue;
                var b = view.CharacterRenderer.bounds;
                float h = Mathf.Abs(game.GameplayCamera.WorldToScreenPoint(b.max).y - game.GameplayCamera.WorldToScreenPoint(b.min).y);
                minimum = Mathf.Min(minimum, h); maximum = Mathf.Max(maximum, h);
            }
            Assert.Greater(game.Motor.transform.position.y, 2.5f);
            Assert.Less(maximum / minimum, 1.12f, "No size jumps while climbing.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            yield return Capture("Hill", game.GameplayCamera);
        }

        [UnityTest] public IEnumerator DashCannotExitAnySideOrCornerOfTheMap()
        {
            var points = new[] { new Vector3(56.8f,0,0), new Vector3(-56.8f,0,0), new Vector3(0,0,56.8f), new Vector3(0,0,-56.8f), new Vector3(56.8f,0,56.8f) };
            var keys = new[] { Key.D, Key.A, Key.W, Key.S, Key.D };
            for (int n = 0; n < points.Length; n++)
            {
                game.Motor.CancelDashAndRestoreState(true); MovePlayer(points[n]);
                InputSystem.QueueStateEvent(keyboard, n == 4 ? new KeyboardState(Key.D, Key.W, Key.Space) : new KeyboardState(keys[n], Key.Space));
                yield return null;
                for (int i = 0; i < 35; i++) yield return new WaitForFixedUpdate();
                var p = game.Motor.transform.position;
                Assert.Less(Mathf.Abs(p.x), 58f, "Escaped east/west " + p);
                Assert.Less(Mathf.Abs(p.z), 58f, "Escaped north/south " + p);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            }
            yield return Capture("Perimeter", game.GameplayCamera);
        }

        [UnityTest] public IEnumerator EnemyRoutesAroundTheActualCarInsteadOfPushingIntoIt()
        {
            MovePlayer(new Vector3(-43, 0, 27));
            var enemy = game.Enemies.Spawn(EnemyArchetypeId.Residual, ArenaSurface.OnGround(new Vector3(-43,0,11),1f));
            float largestSideStep = 0f;
            for (int i = 0; i < 620; i++)
            {
                yield return new WaitForFixedUpdate();
                largestSideStep = Mathf.Max(largestSideStep, Mathf.Abs(enemy.transform.position.x + 43));
                if (Vector3.Distance(enemy.transform.position, game.Player.transform.position) < 2.5f) break;
            }
            Assert.Greater(largestSideStep, 2.5f, "The route must go around the visible car.");
            Assert.Less(Vector3.Distance(enemy.transform.position, game.Player.transform.position), 3f, "Enemy remains trapped: " + enemy.transform.position);
            yield return Capture("EnemyRoute", game.GameplayCamera);
        }

        [UnityTest] public IEnumerator ForegroundWallFadesButStillBlocksMovement()
        {
            MovePlayer(new Vector3(0,0,-57.4f));
            yield return new WaitForSecondsRealtime(0.2f);
            var wall = GameObject.Find("Arena_120m/Wall_South");
            var renderer = wall.GetComponent<Renderer>();
            var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties, 0);
            Assert.Less(properties.GetColor("_BaseColor").a, 0.5f, "Foreground wall still hides the character.");
            Assert.IsTrue(wall.GetComponent<BoxCollider>().enabled, "Fading must retain physical bounds.");
            yield return Capture("WallCutaway", game.GameplayCamera);
            MovePlayer(Vector3.zero);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.Less(renderer.sharedMaterial.renderQueue, 2500, "Opaque material must be restored.");
        }

        [UnityTest] public IEnumerator MortarApproachesAndFiresInTheAuthoredMap()
        {
            MovePlayer(Vector3.zero);
            var mortar = game.Enemies.Spawn(EnemyArchetypeId.Mortar, new Vector3(0, 1, 15));
            bool warning = false;
            for (int i = 0; i < 330 && mortar.MortarShotsFiredCount == 0; i++)
            {
                yield return new WaitForFixedUpdate(); warning |= mortar.TelegraphTimer > 0f;
            }
            Assert.IsTrue(warning, "First in-range shot must show its preparation.");
            Assert.Greater(mortar.MortarShotsFiredCount, 0, "Mortar never fires while approaching on the actual map.");
            Assert.Greater(game.Hazards.TotalProjectilesLaunched, 0);
            yield return Capture("Mortar", game.GameplayCamera);
        }

        [UnityTest] public IEnumerator ConnectedRoutesAndGroundedWasteHaveNoPerFramePathAllocations()
        {
            var nav = ArenaNavigation.Active;
            Assert.IsNotNull(nav); nav.Rebuild();
            Assert.Greater(nav.ReachableCells / (float)nav.WalkableCells, 0.985f, "Disconnected walkable pockets.");
            for (int i = 0; i < 50; i++) nav.Direction(new Vector3(20,0,20), Vector3.back);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++) nav.Direction(new Vector3(20,0,20), Vector3.back);
            Assert.AreEqual(0, System.GC.GetAllocatedBytesForCurrentThread() - before);
            foreach (var lava in Object.FindObjectsByType<LavaHazard>(FindObjectsSortMode.None))
            {
                var visual = lava.transform.Find("MoltenWaste").GetComponent<MeshFilter>();
                foreach (var p in visual.sharedMesh.vertices)
                {
                    var world = visual.transform.TransformPoint(p);
                    Assert.AreEqual(ArenaSurface.Height(world) + 0.045f, world.y, 0.005f, "Floating hot waste.");
                }
            }
            var camera = new GameObject("Arena overview QA").AddComponent<Camera>();
            camera.transform.position = new Vector3(0,100,-75); camera.transform.LookAt(Vector3.zero);
            camera.orthographic = true; camera.orthographicSize = 72; camera.farClipPlane = 300;
            yield return Capture("Overview", camera); Object.Destroy(camera.gameObject);
            MovePlayer(new Vector3(33,0,23));
            yield return new WaitForFixedUpdate(); yield return null;
            yield return Capture("Waste", game.GameplayCamera);
        }

        private static IEnumerator Capture(string name, Camera camera)
        {
            yield return null; yield return null;
            var target = new RenderTexture(1280, 900, 24); var previous = camera.targetTexture;
            var active = RenderTexture.active; camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply();
            Directory.CreateDirectory("Logs/ArenaPlayability"); File.WriteAllBytes("Logs/ArenaPlayability/" + name + ".png", image.EncodeToPNG());
            camera.targetTexture = previous; RenderTexture.active = active; target.Release(); Object.Destroy(target); Object.Destroy(image);
        }
    }
}
