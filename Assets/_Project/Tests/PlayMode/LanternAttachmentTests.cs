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
    public sealed class LanternAttachmentTests
    {
        private CombatGameBootstrap game;
        private CollectorSpriteView collector;
        private FlashlightPresentation presentation;
        private Gamepad pad;
        private readonly List<InputDevice> disabledDevices = new List<InputDevice>();
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditor;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditor = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            foreach (var device in new List<InputDevice>(InputSystem.devices))
                if (device.enabled) { disabledDevices.Add(device); InputSystem.DisableDevice(device); }
            pad = InputSystem.AddDevice<Gamepad>();
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            game.ResetRun(false);
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
            game.Session.ResumeFromFocusLoss();
            collector = game.Motor.GetComponent<CollectorSpriteView>();
            presentation = game.Motor.GetComponent<FlashlightPresentation>();
            // Isolate attachment to a moving sprite from unrelated scenery contacts.
            GameObject.Find("Mundo3D_EchoScrap").SetActive(false);
            foreach (var lava in Object.FindObjectsByType<LavaHazard>(FindObjectsSortMode.None)) lava.gameObject.SetActive(false);
            game.Motor.GetComponent<Rigidbody>().interpolation = RigidbodyInterpolation.None;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            foreach (var device in disabledDevices) if (device.added) InputSystem.EnableDevice(device);
            disabledDevices.Clear();
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditor;
            if (game != null)
            {
                var oldScene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("LanternAttachmentCleanup"));
                yield return SceneManager.UnloadSceneAsync(oldScene);
            }
            CombatTargetRegistry.Clear();
        }

        [UnityTest]
        public IEnumerator TurningWalkingAndPausingOnReliefKeepTheBeamAtTheHeldLantern()
        {
            Vector2[] movements = { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            CharacterFacing[] facings = { CharacterFacing.Right, CharacterFacing.Up, CharacterFacing.Left, CharacterFacing.Down };
            var body = game.Motor.GetComponent<Rigidbody>();
            Vector3 gameplayOriginOffset = game.Weapon.AimPivot.localPosition;
            float range = game.Weapon.EffectiveRange;
            foreach (Vector3 reference in new[] { Vector3.zero, new Vector3(-24f, 0f, 3f) })
            {
                body.position = ArenaSurface.OnGround(ArenaSurface.ReferenceToWorld(reference), 1f);
                body.linearVelocity = Vector3.zero;
                Physics.SyncTransforms();
                for (int direction = 0; direction < movements.Length; direction++)
                {
                    InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = movements[direction] });
                    yield return new WaitForSeconds(0.16f);
                    yield return null;
                    Assert.AreEqual(facings[direction], collector.Facing);
                    AssertAttachment(gameplayOriginOffset, range);
                    game.Session.SetManualPause(true);
                    yield return null;
                    yield return null;
                    AssertAttachment(gameplayOriginOffset, range);
                    game.Session.SetManualPause(false);
                }
            }

            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right });
            body.position = Vector3.up;
            body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.16f);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return new WaitForSeconds(0.10f);
            game.Session.SetManualPause(true);
            foreach (Vector3 offset in new[] { new Vector3(-3f, 0f, -0.8f), new Vector3(5f, 0f, 1.2f) })
            {
                var enemy = game.Enemies.Spawn(EnemyArchetypeId.Residual, ArenaSurface.OnGround(body.position + offset, 1f));
                enemy.enabled = false;
                enemy.Body.linearVelocity = Vector3.zero;
            }
            yield return CaptureArena();
        }

        private void AssertAttachment(Vector3 gameplayOriginOffset, float range)
        {
            Assert.IsTrue(collector.TryGetLanternWorldPosition(out Vector3 lantern));
            Assert.Less(Vector3.Distance(presentation.ImportedView.KeyLight.transform.position, lantern), 0.005f,
                "The real light must originate at the held lantern, including after direction/elevation changes.");
            Assert.Less(Vector3.Distance(presentation.OilLantern.OilGlow.transform.position, lantern), 0.005f);
            Vector3[] vertices = presentation.OilLantern.BeamMesh.vertices;
            Vector3 nearCenter = Vector3.zero;
            int count = 0;
            // Skip the repeated closing vertex and average the ring around the beam's source.
            for (int i = 0; i < vertices.Length - 2; i += 2) { nearCenter += vertices[i]; count++; }
            nearCenter = presentation.OilLantern.BeamRenderer.transform.TransformPoint(nearCenter / count);
            Vector3 gap = game.GameplayCamera.WorldToViewportPoint(nearCenter) - game.GameplayCamera.WorldToViewportPoint(lantern);
            Assert.Less(new Vector2(gap.x * 1280f, gap.y * 720f).magnitude, 4f,
                "The visible beam's near ring must touch the lantern in the actual gameplay projection.");
            Assert.Less(Vector3.Distance(game.Weapon.AimPivot.localPosition, gameplayOriginOffset), 0.005f,
                "Attaching the decorative light must preserve the gameplay damage origin.");
            Assert.AreEqual(range, game.Weapon.EffectiveRange);
        }

        private IEnumerator CaptureArena()
        {
            var camera = game.GameplayCamera;
            var target = new RenderTexture(1280, 720, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            yield return null; yield return null;
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            Directory.CreateDirectory("Logs/CollectorLanternFix");
            File.WriteAllBytes("Logs/CollectorLanternFix/Arena.png", texture.EncodeToPNG());
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            target.Release();
            Object.Destroy(texture);
            Object.Destroy(target);
        }
    }
}
