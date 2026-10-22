using System.Collections;
using System.Collections.Generic;
using System.IO;
using EchoScrap.Player;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class LocomotionTests
    {
        private Keyboard keyboard;
        private Gamepad gamepad;
        private PlayerMotor motor;
        private PlayerInputReader input;
        private Rigidbody body;
        private Scene sandbox;
        private readonly HashSet<Key> heldKeys = new HashSet<Key>();
        private readonly List<InputDevice> disabledDevices = new List<InputDevice>();
        private GamepadState gamepadState;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior;

        [UnitySetUp]
        public IEnumerator LoadSandbox()
        {
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            heldKeys.Clear();
            disabledDevices.Clear();
            gamepadState = default;
            foreach (InputDevice device in new List<InputDevice>(InputSystem.devices))
            {
                if (!device.enabled)
                    continue;
                disabledDevices.Add(device);
                InputSystem.DisableDevice(device);
            }
            keyboard = InputSystem.AddDevice<Keyboard>();
            gamepad = InputSystem.AddDevice<Gamepad>();
            yield return SceneManager.LoadSceneAsync("LocomotionSandbox", LoadSceneMode.Single);
            sandbox = SceneManager.GetActiveScene();
            motor = Object.FindAnyObjectByType<PlayerMotor>();
            Assert.That(motor, Is.Not.Null);
            input = motor.GetComponent<PlayerInputReader>();
            body = motor.GetComponent<Rigidbody>();
            // El runner batch no posee una ventana enfocada. Simular el evento real
            // permite probar controles y pérdida de foco sin depender del escritorio.
            input.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.That(input.CanRead, Is.True);
        }

        [UnityTearDown]
        public IEnumerator UnloadSandbox()
        {
            if (input != null)
                input.SetInputEnabled(false);
            var cleanup = SceneManager.CreateScene("Locomotion Test Cleanup");
            SceneManager.SetActiveScene(cleanup);
            if (sandbox.IsValid() && sandbox.isLoaded)
                yield return SceneManager.UnloadSceneAsync(sandbox);
            yield return null;
            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            if (gamepad != null && gamepad.added)
                InputSystem.RemoveDevice(gamepad);
            foreach (InputDevice device in disabledDevices)
                if (device.added)
                    InputSystem.EnableDevice(device);
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
        }

        [Test]
        public void SceneContainsConfiguredPlayerArenaAndCamera()
        {
            Assert.That(GameObject.Find("Arena/Floor").transform.localScale,
                Is.EqualTo(new Vector3(120f, 1f, 120f)));
            foreach (string name in new[] { "North", "South", "East", "West" })
                Assert.That(GameObject.Find("Arena/Wall " + name).GetComponent<Collider>(), Is.Not.Null);
            Assert.That(body.useGravity, Is.False);
            Assert.That(body.interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate));
            Assert.That(body.constraints.HasFlag(RigidbodyConstraints.FreezePositionY), Is.True);
            Assert.That(body.constraints.HasFlag(RigidbodyConstraints.FreezeRotation), Is.True);
            Assert.That(motor.transform.Find("Visual"), Is.Not.Null);
            Assert.That(body.GetComponent<CapsuleCollider>().isTrigger, Is.False);
            Assert.That(motor.Settings.MoveSpeed, Is.EqualTo(8f));
            Assert.That(motor.Settings.Acceleration, Is.EqualTo(80f));
            Assert.That(motor.Settings.Deceleration, Is.EqualTo(70f));
            Assert.That(Camera.main.orthographic, Is.False);
            Assert.That(Camera.main.fieldOfView, Is.EqualTo(60f).Within(0.01f));
            Assert.That(Mathf.DeltaAngle(Camera.main.transform.eulerAngles.x, 60f),
                Is.EqualTo(0f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator WasdAndArrowBindingsCoverFourDirections()
        {
            var keys = new[] { keyboard.wKey, keyboard.sKey, keyboard.aKey, keyboard.dKey,
                keyboard.upArrowKey, keyboard.downArrowKey, keyboard.leftArrowKey, keyboard.rightArrowKey };
            var expected = new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right,
                Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            for (int i = 0; i < keys.Length; i++)
            {
                Press(keys[i]);
                yield return null;
                Assert.That(Vector2.Distance(input.Movement, expected[i]), Is.LessThan(0.001f));
                Release(keys[i]);
                yield return null;
                Assert.That(input.Movement, Is.EqualTo(Vector2.zero));
            }
        }

        [UnityTest]
        public IEnumerator CardinalAndDiagonalHaveTheSameSpeedAndTravel()
        {
            ResetPosition(new Vector3(-30f, 1f, -30f));
            Press(keyboard.wKey);
            yield return FixedSteps(8);
            Vector3 start = body.position;
            yield return FixedSteps(50);
            float cardinal = Vector3.Distance(start, body.position);
            Assert.That(body.linearVelocity.magnitude, Is.EqualTo(8f).Within(0.02f));
            Release(keyboard.wKey);
            ResetPosition(new Vector3(-30f, 1f, -30f));
            Press(keyboard.wKey);
            Press(keyboard.dKey);
            yield return FixedSteps(8);
            start = body.position;
            yield return FixedSteps(50);
            float diagonal = Vector3.Distance(start, body.position);
            Assert.That(input.Movement.magnitude, Is.EqualTo(1f).Within(0.001f));
            Assert.That(body.linearVelocity.magnitude, Is.EqualTo(8f).Within(0.02f));
            Assert.That(cardinal, Is.EqualTo(8f).Within(0.2f));
            Assert.That(diagonal, Is.EqualTo(cardinal).Within(0.2f));
        }

        [UnityTest]
        public IEnumerator AccelerationAndBrakingReachTheirExpectedLimits()
        {
            ResetPosition(new Vector3(-30f, 1f, -30f));
            Press(keyboard.wKey);
            yield return new WaitForFixedUpdate();
            Assert.That(body.linearVelocity.magnitude, Is.GreaterThan(0f).And.LessThan(8f));
            float acceleratingSpeed = body.linearVelocity.magnitude;
            yield return new WaitForFixedUpdate();
            Assert.That((body.linearVelocity.magnitude - acceleratingSpeed) / Time.fixedDeltaTime,
                Is.EqualTo(80f).Within(0.05f));
            yield return FixedSteps(6);
            Assert.That(body.linearVelocity.z, Is.EqualTo(8f).Within(0.02f));
            Release(keyboard.wKey);
            yield return new WaitForFixedUpdate();
            Assert.That(body.linearVelocity.magnitude, Is.GreaterThan(0f).And.LessThan(8f));
            float brakingSpeed = body.linearVelocity.magnitude;
            yield return new WaitForFixedUpdate();
            Assert.That((brakingSpeed - body.linearVelocity.magnitude) / Time.fixedDeltaTime,
                Is.EqualTo(70f).Within(0.05f));
            yield return FixedSteps(6);
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(0.01f));
            Assert.That(Vector3.Dot(motor.FacingDirection, Vector3.forward), Is.GreaterThan(0.999f));
        }

        [UnityTest]
        public IEnumerator AnalogStickPreservesItsProcessedMagnitudeAndDeadzone()
        {
            ResetPosition(new Vector3(-30f, 1f, -30f));
            Set(gamepad.leftStick, new Vector2(0.5f, 0f));
            yield return FixedSteps(8);
            float expected = gamepad.leftStick.ReadValue().x;
            Assert.That(expected, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(input.Movement.x, Is.EqualTo(expected).Within(0.001f));
            Assert.That(body.linearVelocity.x, Is.EqualTo(8f * expected).Within(0.02f));
            Set(gamepad.leftStick, new Vector2(0.01f, 0f));
            yield return FixedSteps(8);
            Assert.That(input.Movement, Is.EqualTo(Vector2.zero));
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator GamepadDpadDiagonalIsNormalized()
        {
            Press(gamepad.dpad.up);
            Press(gamepad.dpad.right);
            yield return FixedSteps(8);
            Assert.That(input.Movement.x, Is.GreaterThan(0f));
            Assert.That(input.Movement.y, Is.GreaterThan(0f));
            Assert.That(input.Movement.magnitude, Is.EqualTo(1f).Within(0.001f));
            Assert.That(body.linearVelocity.magnitude, Is.EqualTo(8f).Within(0.02f));
        }

        [UnityTest]
        public IEnumerator WallsBlockFrontalDiagonalAndCornerMovement()
        {
            ResetPosition(new Vector3(58f, 1f, -30f));
            Set(gamepad.leftStick, Vector2.right);
            yield return FixedSteps(40);
            Assert.That(body.position.x, Is.InRange(59.35f, 59.52f));
            Assert.That(body.position.y, Is.EqualTo(1f).Within(0.001f));

            ResetPosition(new Vector3(58f, 1f, -30f));
            Set(gamepad.leftStick, Vector2.one.normalized);
            yield return FixedSteps(50);
            Assert.That(body.position.x, Is.LessThanOrEqualTo(59.52f));
            Assert.That(body.position.z, Is.GreaterThan(-27f), "Debe deslizarse a lo largo del muro.");

            ResetPosition(new Vector3(58f, 1f, 58f));
            Set(gamepad.leftStick, Vector2.one.normalized);
            yield return FixedSteps(50);
            Assert.That(body.position.x, Is.InRange(59.35f, 59.52f));
            Assert.That(body.position.z, Is.InRange(59.35f, 59.52f));
            Assert.That(body.position.y, Is.EqualTo(1f).Within(0.001f));
            Assert.That(Quaternion.Angle(body.rotation, Quaternion.identity), Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator ObstaclesBlockMovementWithoutChangingHeightOrTilting()
        {
            ResetPosition(new Vector3(-8f, 1f, -1f));
            Set(gamepad.leftStick, Vector2.up);
            yield return FixedSteps(60);
            Assert.That(body.position.z, Is.InRange(0.85f, 1.02f));
            Assert.That(body.position.y, Is.EqualTo(1f).Within(0.001f));
            Assert.That(Quaternion.Angle(body.rotation, Quaternion.identity), Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator DisablingControlStopsImmediatelyAndPersistsAcrossReactivation()
        {
            Press(keyboard.wKey);
            yield return FixedSteps(8);
            Assert.That(body.linearVelocity.magnitude, Is.GreaterThan(7f));
            input.SetInputEnabled(false);
            Assert.That(input.Movement, Is.EqualTo(Vector2.zero));
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
            input.enabled = false;
            input.enabled = true;
            yield return FixedSteps(5);
            Assert.That(input.CanRead, Is.False);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
            Release(keyboard.wKey);
            input.SetInputEnabled(true);
            yield return FixedSteps(3);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
            Press(keyboard.dKey);
            yield return FixedSteps(8);
            Assert.That(body.linearVelocity.x, Is.EqualTo(8f).Within(0.02f));
        }

        [UnityTest]
        public IEnumerator DisablingMotorStopsTheBody()
        {
            Press(keyboard.wKey);
            yield return FixedSteps(8);
            motor.enabled = false;
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
            yield return FixedSteps(5);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
        }

        [UnityTest]
        public IEnumerator FocusLossAndApplicationPauseClearRetainedMovement()
        {
            Press(keyboard.wKey);
            yield return FixedSteps(8);
            input.SendMessage("OnApplicationFocus", false);
            Assert.That(input.Movement, Is.EqualTo(Vector2.zero));
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
            yield return FixedSteps(3);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
            Release(keyboard.wKey);
            input.SendMessage("OnApplicationFocus", true);
            yield return FixedSteps(3);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
            Press(keyboard.dKey);
            yield return FixedSteps(8);
            input.SendMessage("OnApplicationPause", true);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
            Release(keyboard.dKey);
            input.SendMessage("OnApplicationPause", false);
            yield return FixedSteps(3);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
        }

        [UnityTest]
        public IEnumerator RepeatedPlayerActivationKeepsTheInputLifecycleValid()
        {
            for (int i = 0; i < 4; i++)
            {
                motor.gameObject.SetActive(false);
                Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
                motor.gameObject.SetActive(true);
                input.SendMessage("OnApplicationFocus", true);
                Press(keyboard.wKey);
                yield return FixedSteps(7);
                Assert.That(body.linearVelocity.z, Is.EqualTo(8f).Within(0.02f));
                Release(keyboard.wKey);
                yield return FixedSteps(7);
                Assert.That(body.linearVelocity.magnitude, Is.LessThan(0.01f));
            }
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void CameraFollowsTheSameTrajectoryAtDifferentUpdateRates(int updatesPerSecond)
        {
            Object.FindAnyObjectByType<CinemachineBrain>().enabled = false;
            var camera = Object.FindAnyObjectByType<CinemachineCamera>();
            var follow = camera.GetComponent<CinemachineFollow>();
            var target = new GameObject("Camera Trajectory Target").transform;
            target.position = Vector3.up;
            camera.Follow = target;
            camera.PreviousStateIsValid = false;
            camera.InternalUpdateCameraState(Vector3.up, -1f);
            Vector3 previous = camera.State.RawPosition;
            float dt = 1f / updatesPerSecond;
            float peakLag = 0f;
            for (int frame = 1; frame <= updatesPerSecond * 2; frame++)
            {
                target.position = new Vector3(8f * frame * dt, 1f, 0f);
                camera.InternalUpdateCameraState(Vector3.up, dt);
                Vector3 current = camera.State.RawPosition;
                float lag = target.position.x + follow.FollowOffset.x - current.x;
                peakLag = Mathf.Max(peakLag, lag);
                Assert.That(current.x, Is.GreaterThanOrEqualTo(previous.x - 0.001f));
                Assert.That(current.x - previous.x, Is.LessThanOrEqualTo(8f * dt + 0.01f));
                Assert.That(lag, Is.InRange(-0.01f, 1f));
                Assert.That(Quaternion.Angle(camera.State.RawOrientation,
                    Quaternion.Euler(60f, 0f, 0f)), Is.LessThan(0.01f));
                previous = current;
            }
            for (int frame = 0; frame < updatesPerSecond; frame++)
                camera.InternalUpdateCameraState(Vector3.up, dt);
            Assert.That(Vector3.Distance(camera.State.RawPosition,
                target.position + follow.FollowOffset), Is.LessThan(0.02f));
            Debug.Log($"[Locomotion Camera] simulatedHz={updatesPerSecond}, peakLagMetres={peakLag:F4}");
        }

        [UnityTest]
        public IEnumerator PreviewRendersThePlayerAndObstacles()
        {
            var texture = new RenderTexture(1600, 900, 24);
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            Camera camera = Camera.main;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                texture.Create();
                camera.targetTexture = texture;
                yield return null;
                yield return null;
                yield return null;
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                image.Apply();
                Directory.CreateDirectory("Logs/Locomotion");
                File.WriteAllBytes("Logs/Locomotion/Preview.png", image.EncodeToPNG());
                int playerPixels = 0;
                foreach (Color pixel in image.GetPixels(700, 350, 200, 200))
                    if (pixel.r > 0.3f && pixel.r > pixel.g * 1.1f && pixel.g > pixel.b * 1.4f)
                        playerPixels++;
                Assert.That(playerPixels, Is.GreaterThan(100),
                    "La cámara debe mostrar al jugador amarillo dentro del encuadre.");
            }
            finally
            {
                if (camera != null)
                    camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                texture.Release();
                Object.Destroy(texture);
                Object.Destroy(image);
            }
        }

        private void ResetPosition(Vector3 position)
        {
            motor.Stop();
            body.position = position;
            body.rotation = Quaternion.identity;
            Physics.SyncTransforms();
        }

        private void Press(ButtonControl control)
        {
            if (control is KeyControl key)
            {
                heldKeys.Add(key.keyCode);
                SendKeyboardState();
            }
            else
            {
                if (control == gamepad.dpad.up)
                    gamepadState = gamepadState.WithButton(GamepadButton.DpadUp);
                else if (control == gamepad.dpad.right)
                    gamepadState = gamepadState.WithButton(GamepadButton.DpadRight);
                else
                    Assert.Fail("Control virtual de prueba no soportado: " + control);
                InputSystem.QueueStateEvent(gamepad, gamepadState);
                InputSystem.Update();
            }
        }

        private void Release(KeyControl key)
        {
            heldKeys.Remove(key.keyCode);
            SendKeyboardState();
        }

        private void SendKeyboardState()
        {
            var keys = new Key[heldKeys.Count];
            heldKeys.CopyTo(keys);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        private void Set(StickControl control, Vector2 value)
        {
            Assert.That(control, Is.SameAs(gamepad.leftStick));
            gamepadState.leftStick = value;
            InputSystem.QueueStateEvent(gamepad, gamepadState);
            InputSystem.Update();
        }

        private static IEnumerator FixedSteps(int count)
        {
            for (int i = 0; i < count; i++)
                yield return new WaitForFixedUpdate();
        }
    }
}
