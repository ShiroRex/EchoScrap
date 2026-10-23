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
using Object = UnityEngine.Object;

namespace EchoScrap.Tests
{
    public sealed class CollectorSpriteTests
    {
        private CombatGameBootstrap game;
        private CollectorSpriteView view;
        private Gamepad pad;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior;
        private readonly List<InputDevice> disabledDevices = new List<InputDevice>();

        [TestCase(1, 0, CharacterFacing.Right)]
        [TestCase(-1, 0, CharacterFacing.Left)]
        [TestCase(0, 1, CharacterFacing.Up)]
        [TestCase(0, -1, CharacterFacing.Down)]
        public void FacingUsesScreenAxes(float x, float z, CharacterFacing expected)
        {
            Assert.That(CollectorSpriteView.ResolveFacing(new Vector3(x, 0, z), Quaternion.Euler(60, 0, 0),
                CharacterFacing.Down, 0.12f), Is.EqualTo(expected));
        }

        [Test]
        public void DiagonalKeepsPreviousAxisUntilItClearlyChanges()
        {
            Quaternion camera = Quaternion.Euler(60, 0, 0);
            Assert.That(CollectorSpriteView.ResolveFacing(new Vector3(0.7f, 0, 0.71f), camera,
                CharacterFacing.Right, 0.12f), Is.EqualTo(CharacterFacing.Right));
            Assert.That(CollectorSpriteView.ResolveFacing(new Vector3(0.4f, 0, 0.9f), camera,
                CharacterFacing.Right, 0.12f), Is.EqualTo(CharacterFacing.Up));
        }

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            disabledDevices.Clear();
            foreach (InputDevice device in new List<InputDevice>(InputSystem.devices))
                if (device.enabled) { disabledDevices.Add(device); InputSystem.DisableDevice(device); }
            pad = InputSystem.AddDevice<Gamepad>();
            yield return SceneManager.LoadSceneAsync("CombatArena", LoadSceneMode.Single);
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            Assert.That(game, Is.Not.Null);
            game.Director.enabled = false;
            game.Enemies.DespawnAllActive();
            game.Session.SimulateFocusChange(true);
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
            yield return null;
            view = game.Motor.GetComponent<CollectorSpriteView>();
            Assert.That(view, Is.Not.Null, "CombatPlayer must contain its authored sprite view.");
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (pad != null) InputSystem.RemoveDevice(pad);
            foreach (InputDevice device in disabledDevices) if (device.added) InputSystem.EnableDevice(device);
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthoredClipsAndGroundPivotAreConnectedToThePlayablePrefab()
        {
            Assert.That(view.Sprites.clips.Length, Is.EqualTo(16));
            foreach (CharacterFacing facing in System.Enum.GetValues(typeof(CharacterFacing)))
            {
                Assert.That(view.Sprites.GetClip(CharacterMotion.Idle, facing).frames.Length, Is.EqualTo(8));
                var walk = view.Sprites.GetClip(CharacterMotion.Walk, facing);
                Assert.That(walk.FrameAt(0.091f), Is.EqualTo(1));
                Assert.That(walk.FrameAt(walk.Duration + 0.001f), Is.EqualTo(0));
                Assert.That(view.Sprites.GetClip(CharacterMotion.Dash, facing).Duration, Is.EqualTo(0.15f).Within(0.0001f));
                var death = view.Sprites.GetClip(CharacterMotion.Death, facing);
                Assert.That(death.Duration, Is.EqualTo(0.94f).Within(0.0001f));
                Assert.That(death.FrameAt(2f), Is.EqualTo(7));
            }
            Assert.That(view.CharacterRenderer.sharedMaterial.shader.isSupported, Is.True);
            Assert.That(view.CharacterRenderer.sharedMaterial.GetTexture("_BumpMap"), Is.Not.Null);
            CapsuleCollider collider = game.Motor.GetComponent<CapsuleCollider>();
            Assert.That(view.CharacterRenderer.transform.position.y,
                Is.EqualTo(game.Motor.transform.position.y + collider.center.y - collider.height / 2f + 0.025f).Within(0.01f));
            Assert.That(Quaternion.Angle(view.CharacterRenderer.transform.rotation, game.GameplayCamera.transform.rotation), Is.LessThan(0.1f));
            foreach (Transform child in game.Motor.GetComponentsInChildren<Transform>(true))
                if (child.name == "Placeholder Body" || child.name == "Facing Marker") Assert.That(child.gameObject.activeSelf, Is.False);
            yield return Capture(game.GameplayCamera, "Arena.png");
            var close = new GameObject("Collector close-up camera").AddComponent<Camera>();
            close.CopyFrom(game.GameplayCamera);
            close.transform.rotation = game.GameplayCamera.transform.rotation;
            close.transform.position = view.CharacterRenderer.transform.position + close.transform.up * 1.25f - close.transform.forward * 6f;
            close.fieldOfView = 42f;
            yield return Capture(close, "CloseUp.png");
            Object.Destroy(close.gameObject);
        }

        [UnityTest]
        public IEnumerator FourDirectionsPauseDashDeathAndRestartUseTheRealPlayerStates()
        {
            Vector2[] moves = { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            CharacterFacing[] facings = { CharacterFacing.Right, CharacterFacing.Up, CharacterFacing.Left, CharacterFacing.Down };
            for (int i = 0; i < moves.Length; i++)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = moves[i] });
                yield return new WaitForSeconds(0.16f);
                Assert.That(view.Motion, Is.EqualTo(CharacterMotion.Walk));
                Assert.That(view.Facing, Is.EqualTo(facings[i]));
            }
            game.Session.Clock.SetPause(PauseReason.Manual, true);
            int pausedFrame = view.FrameIndex;
            float pausedTime = view.ClipTime;
            yield return new WaitForSecondsRealtime(0.18f);
            Assert.That(view.FrameIndex, Is.EqualTo(pausedFrame));
            Assert.That(view.ClipTime, Is.EqualTo(pausedTime));
            game.Session.Clock.SetPause(PauseReason.Manual, false);
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.South));
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate(); yield return null;
            Assert.That(game.Motor.IsDashing, Is.True);
            Assert.That(view.Motion, Is.EqualTo(CharacterMotion.Dash));
            var dash = game.Motor.GetComponent<PlayerDashPresentation>();
            Assert.That(dash.TotalAfterimagesSpawnedThisDash, Is.GreaterThan(0));
            int spriteGhosts = 0;
            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (renderer.name.StartsWith("DashAfterimage_"))
                {
                    Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("EchoScrap/Character Afterimage"));
                    Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(view.CharacterMesh));
                    spriteGhosts++;
                }
            Assert.That(spriteGhosts, Is.EqualTo(3));
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return new WaitForSeconds(0.25f);
            game.Player.TakeContactOrHazardDamage(5f, game.Motor.transform.position + Vector3.left);
            view.Advance(0f);
            var block = new MaterialPropertyBlock();
            view.CharacterRenderer.GetPropertyBlock(block);
            Assert.That(block.GetColor("_BaseColor").r, Is.GreaterThan(0.9f));
            Assert.That(block.GetVector("_FrameRect").z, Is.EqualTo(0.125f));
            yield return new WaitForSeconds(0.15f);
            game.Player.ForceSetHealthForTesting(0f);
            yield return new WaitForSecondsRealtime(0.24f);
            Assert.That(game.Session.State, Is.EqualTo(SessionState.Defeat));
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(view.Motion, Is.EqualTo(CharacterMotion.Death));
            Assert.That(view.FrameIndex, Is.GreaterThan(0));
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.That(view.DeathComplete, Is.True);
            Assert.That(view.FrameIndex, Is.EqualTo(7));
            game.ResetRun();
            yield return null;
            Assert.That(view.Motion, Is.EqualTo(CharacterMotion.Idle));
            Assert.That(view.Facing, Is.EqualTo(CharacterFacing.Down));
            Assert.That(view.DeathComplete, Is.False);
            Assert.That(game.Player.IsAlive, Is.True);
        }

        [UnityTest]
        public IEnumerator FatalDamageAfterDashDoesNotFreezeGhostsOrTheDamageTint()
        {
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.South));
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate(); yield return null;
            var dash = game.Motor.GetComponent<PlayerDashPresentation>();
            Assert.That(dash.ActiveAfterimageCount, Is.GreaterThan(0));
            game.Motor.CancelDashAndRestoreState();
            game.Player.TakeContactOrHazardDamage(10000f, game.Motor.transform.position + Vector3.left);
            Assert.That(game.Player.IsDead, Is.True);
            Assert.That(game.Motor.GetComponent<ActorVisualComposer>().IsFlashActive, Is.True);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(view.Motion, Is.EqualTo(CharacterMotion.Death));
            Assert.That(dash.ActiveAfterimageCount, Is.Zero);
            Assert.That(game.Motor.GetComponent<ActorVisualComposer>().IsFlashActive, Is.False);
        }

        private static IEnumerator Capture(Camera camera, string name)
        {
            var target = new RenderTexture(1920, 1080, 24);
            RenderTexture old = camera.targetTexture;
            RenderTexture oldActive = RenderTexture.active;
            camera.targetTexture = target;
            yield return null; yield return null; yield return null;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            string directory = Path.Combine(Application.dataPath, "..", "Logs", "CollectorIntegration");
            Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name), texture.EncodeToPNG());
            camera.targetTexture = old; RenderTexture.active = oldActive;
            target.Release(); Object.Destroy(target); Object.Destroy(texture);
        }
    }
}
