using System.Collections;
using System.Collections.Generic;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
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
    public sealed class PlayerDashTests
    {
        private Keyboard keyboard;
        private Gamepad gamepad;
        private PlayerMotor motor;
        private PlayerInputReader input;
        private Rigidbody body;
        private PlayerHealth health;
        private PlayerRuntimeStats runtimeStats;
        private ActorVisualComposer visualComposer;
        private PlayerDashPresentation dashPresentation;
        private CombatSession session;
        private Scene sandbox;

        private readonly HashSet<Key> heldKeys = new HashSet<Key>();
        private readonly List<InputDevice> disabledDevices = new List<InputDevice>();
        private GamepadState gamepadState;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PhysicsLayers.EnsureConfigured();
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

#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/LocomotionSandbox.unity",new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("LocomotionSandbox", LoadSceneMode.Single);
#endif
            sandbox = SceneManager.GetActiveScene();

            motor = Object.FindAnyObjectByType<PlayerMotor>();
            Assert.IsNotNull(motor, "Falta PlayerMotor en la escena.");
            input = motor.GetComponent<PlayerInputReader>();
            body = motor.GetComponent<Rigidbody>();

            input.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.IsTrue(input.CanRead);

            runtimeStats = motor.gameObject.GetComponent<PlayerRuntimeStats>();
            if (runtimeStats == null)
                runtimeStats = motor.gameObject.AddComponent<PlayerRuntimeStats>();
            runtimeStats.ResetToInitial();

            visualComposer = motor.gameObject.GetComponent<ActorVisualComposer>();
            if (visualComposer == null)
                visualComposer = motor.gameObject.AddComponent<ActorVisualComposer>();
            visualComposer.EnsureHierarchy();
            visualComposer.CacheRenderers();

            health = motor.gameObject.GetComponent<PlayerHealth>();
            if (health == null)
                health = motor.gameObject.AddComponent<PlayerHealth>();

            dashPresentation = motor.gameObject.GetComponent<PlayerDashPresentation>();
            if (dashPresentation == null)
                dashPresentation = motor.gameObject.AddComponent<PlayerDashPresentation>();
            dashPresentation.EnsureInitialized();

            var sessionGo = new GameObject("DashTestSession");
            SceneManager.MoveGameObjectToScene(sessionGo, sandbox);
            session = sessionGo.AddComponent<CombatSession>();
            session.ResetSession(2026);
            health.BindSession(session);
            health.ResetForNewSession();

            motor.gameObject.layer = PhysicsLayers.PlayerLayer;
            Teleport(new Vector3(-30f, 1f, -30f));
            motor.ResetDashForSession();
            yield return new WaitForFixedUpdate();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (input != null)
                input.SetInputEnabled(false);

            var cleanup = SceneManager.CreateScene("Dash Test Cleanup");
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

        [UnityTest]
        public IEnumerator FreeDash_TravelsApprox3Point3Meters_LocksDirection_AndCounts1sCooldownAfterEnd()
        {
            Teleport(new Vector3(-30f, 1f, -30f));
            Vector3 startPos = body.position;

            PressKey(keyboard.wKey);
            PressKey(keyboard.spaceKey);

            yield return new WaitForFixedUpdate();
            Assert.IsTrue(motor.IsDashing, "El dash debe comenzar en el siguiente paso físico disponible.");
            Assert.IsTrue(motor.IsDashInvulnerable, "El jugador debe ser invulnerable durante el dash.");
            Assert.AreEqual(PhysicsLayers.PlayerDashLayer, motor.gameObject.layer, "Debe usar la capa PlayerDash durante el dash.");

            // Intentar cambiar la dirección a mitad del dash: la dirección debe permanecer fija
            ReleaseKey(keyboard.wKey);
            PressKey(keyboard.dKey);

            while (motor.IsDashing)
            {
                Assert.That(motor.DashDirection.z, Is.GreaterThan(0.99f), "La dirección del dash debe quedar fijada al comenzar.");
                yield return new WaitForFixedUpdate();
            }

            ReleaseKey(keyboard.dKey);
            ReleaseKey(keyboard.spaceKey);

            Vector3 endPos = body.position;
            float distance = Vector3.Distance(
                new Vector3(startPos.x, 0f, startPos.z),
                new Vector3(endPos.x, 0f, endPos.z));

            Assert.AreEqual(3.3f, distance, 0.22f, "El recorrido libre debe ser ~3.3 m (22 m/s durante 0.15 s).");
            Assert.AreEqual(PhysicsLayers.PlayerLayer, motor.gameObject.layer, "Debe restaurar la capa original al terminar el dash.");
            Assert.AreEqual(1.0f, motor.DashCooldownRemaining, 0.05f, "El cooldown de 1 s debe contarse después de terminar el dash.");
            Assert.IsFalse(motor.IsDashAvailable, "El dash no debe estar disponible durante el cooldown.");

            // Pulsar dash durante el cooldown no debe encolar otro dash automático al expirar
            PressKey(keyboard.spaceKey);
            yield return new WaitForFixedUpdate();
            ReleaseKey(keyboard.spaceKey);
            Assert.IsFalse(motor.IsDashing);

            yield return StepFixedSeconds(1.05f);
            Assert.IsTrue(motor.IsDashAvailable, "Tras 1 s desde el final del dash debe volver a estar disponible.");
            Assert.IsFalse(motor.IsDashing, "Exige una nueva pulsación después de terminar el cooldown.");
        }

        [UnityTest]
        public IEnumerator Directions_StationaryUsesLastFacing_DiagonalAndGamepadSouthWork()
        {
            Teleport(new Vector3(-30f, 1f, -30f));

            // 1. Orientar hacia la izquierda y detenerse
            PressKey(keyboard.aKey);
            yield return StepFixedSeconds(0.15f);
            ReleaseKey(keyboard.aKey);
            yield return StepFixedSeconds(0.25f);
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(0.05f));

            Vector3 stationaryStart = body.position;
            PressKey(keyboard.spaceKey);
            yield return StepUntilDashCompletes();
            ReleaseKey(keyboard.spaceKey);

            Vector3 stationaryDelta = body.position - stationaryStart;
            Assert.That(stationaryDelta.x, Is.LessThan(-3.0f), "Estando quieto debe usar la última orientación.");
            Assert.That(Mathf.Abs(stationaryDelta.z), Is.LessThan(0.15f));

            // 2. Diagonal analógica con stick izquierdo y botón sur del mando
            motor.ResetDashForSession();
            Teleport(new Vector3(-30f, 1f, -30f));
            SetLeftStick(new Vector2(0.45f, 0.45f));
            PressGamepadSouth(true);
            yield return StepUntilDashCompletes();
            PressGamepadSouth(false);
            SetLeftStick(Vector2.zero);

            Vector3 diagDelta = body.position - new Vector3(-30f, 1f, -30f);
            float planarDist = new Vector2(diagDelta.x, diagDelta.z).magnitude;
            Assert.AreEqual(3.3f, planarDist, 0.25f, "El dash diagonal analógico con mando debe recorrer ~3.3 m.");
            Assert.AreEqual(diagDelta.x, diagDelta.z, 0.2f);
        }

        [UnityTest]
        public IEnumerator EnemyPhasing_PassesThroughColossusAndGolgoraBodies_AndRestoresLayerOnDeathOrDisable()
        {
            Teleport(new Vector3(-30f, 1f, -30f));

            // Crear cuerpos sólidos tipo Coloso (1.6x) y Gólgora (3.5x) en la capa Enemy (8) en la trayectoria del dash
            var colossusBlocker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            colossusBlocker.name = "ColossusSolidBody";
            colossusBlocker.layer = PhysicsLayers.EnemyLayer;
            colossusBlocker.transform.position = new Vector3(-30f, 1f, -28.7f);
            colossusBlocker.transform.localScale = new Vector3(1.6f, 1.6f, 1.6f);

            var golgoraBlocker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            golgoraBlocker.name = "GolgoraSolidBody";
            golgoraBlocker.layer = PhysicsLayers.EnemyLayer;
            golgoraBlocker.transform.position = new Vector3(-30f, 1f, -27.5f);
            golgoraBlocker.transform.localScale = new Vector3(2.2f, 2.0f, 1.0f);

            try
            {
                Assert.IsFalse(Physics.GetIgnoreLayerCollision(PhysicsLayers.PlayerLayer, PhysicsLayers.EnemyLayer));
                Assert.IsTrue(Physics.GetIgnoreLayerCollision(PhysicsLayers.PlayerDashLayer, PhysicsLayers.EnemyLayer));

                PressKey(keyboard.wKey);
                PressKey(keyboard.spaceKey);
                yield return StepUntilDashCompletes();
                ReleaseKey(keyboard.wKey);
                ReleaseKey(keyboard.spaceKey);

                float zTravel = body.position.z - (-30f);
                Assert.AreEqual(3.3f, zTravel, 0.25f, "El dash debe atravesar cuerpos enemigos incluidos Coloso y Gólgora.");
                Assert.AreEqual(PhysicsLayers.PlayerLayer, motor.gameObject.layer);

                // Probar restauración de capa al morir en mitad de un dash
                colossusBlocker.SetActive(false);
                golgoraBlocker.SetActive(false);
                motor.ResetDashForSession();
                Teleport(new Vector3(-30f, 1f, -30f));

                PressKey(keyboard.wKey);
                PressKey(keyboard.spaceKey);
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(motor.IsDashing);
                Assert.AreEqual(PhysicsLayers.PlayerDashLayer, motor.gameObject.layer);

                health.ForceSetHealthForTesting(0f);
                Assert.IsFalse(motor.IsDashing, "La muerte debe cancelar el dash inmediatamente.");
                Assert.AreEqual(PhysicsLayers.PlayerLayer, motor.gameObject.layer, "La muerte debe restaurar la capa original.");
                ReleaseKey(keyboard.wKey);
                ReleaseKey(keyboard.spaceKey);
            }
            finally
            {
                Object.Destroy(colossusBlocker);
                Object.Destroy(golgoraBlocker);
            }
        }

        [UnityTest]
        public IEnumerator WallCollisions_FrontalDiagonalAndCorner_KeepDurationAndEmitSingleBlockReaction()
        {
            // 1. Colisión frontal contra muro este (x ~ 59.5)
            Teleport(new Vector3(58f, 1f, -30f));
            motor.ResetDashForSession();

            PressKey(keyboard.dKey);
            PressKey(keyboard.spaceKey);

            int activeFixedSteps = 0;
            while (true)
            {
                yield return new WaitForFixedUpdate();
                if (motor.IsDashing)
                    activeFixedSteps++;
                else if (activeFixedSteps > 0)
                    break;
            }
            ReleaseKey(keyboard.dKey);
            ReleaseKey(keyboard.spaceKey);

            Assert.That(body.position.x, Is.LessThan(59.6f), "El dash no debe atravesar muros ni obstáculos.");
            Assert.That(activeFixedSteps, Is.GreaterThanOrEqualTo(7), "Debe mantener la duración prevista (~0.15 s) aunque choque con pared.");
            Assert.AreEqual(1, motor.WallBlockReactionCount, "Debe emitir una sola reacción de bloqueo por dash.");
            Assert.AreEqual(1, dashPresentation.WallSparkEmitCount, "Debe emitir una sola chispa de bloqueo por dash.");
            Assert.AreEqual(1.0f, motor.DashCooldownRemaining, 0.05f, "Debe conservar el cooldown previsto tras chocar con pared.");

            // 2. Colisión diagonal contra muro este
            motor.ResetDashForSession();
            Teleport(new Vector3(58f, 1f, -30f));
            PressKey(keyboard.dKey);
            PressKey(keyboard.wKey);
            PressKey(keyboard.spaceKey);
            yield return StepUntilDashCompletes();
            ReleaseKey(keyboard.dKey);
            ReleaseKey(keyboard.wKey);
            ReleaseKey(keyboard.spaceKey);

            Assert.That(body.position.x, Is.LessThan(59.6f));
            Assert.That(body.position.z, Is.GreaterThan(-29.0f), "En colisión diagonal debe deslizar sin engancharse.");
            Assert.AreEqual(1, motor.WallBlockReactionCount, "Una sola reacción de bloqueo en impacto diagonal.");

            // 3. Colisión en esquina noreste (x ~ 59.5, z ~ 59.5)
            motor.ResetDashForSession();
            Teleport(new Vector3(58f, 1f, 58f));
            PressKey(keyboard.dKey);
            PressKey(keyboard.wKey);
            PressKey(keyboard.spaceKey);
            yield return StepUntilDashCompletes();
            ReleaseKey(keyboard.dKey);
            ReleaseKey(keyboard.wKey);
            ReleaseKey(keyboard.spaceKey);

            Assert.That(body.position.x, Is.LessThan(59.6f));
            Assert.That(body.position.z, Is.LessThan(59.6f));
            Assert.AreEqual(1, motor.WallBlockReactionCount, "En esquina con dos muros sólo debe emitir una reacción de bloqueo.");
        }

        [UnityTest]
        public IEnumerator Invulnerability_PauseDraftAndUiConfirm_BehaveAccordingToContract()
        {
            Teleport(new Vector3(-30f, 1f, -30f));
            motor.ResetDashForSession();

            PressKey(keyboard.wKey);
            PressKey(keyboard.spaceKey);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(motor.IsDashing);

            // Daño recibido durante el dash se ignora por invulnerabilidad y no activa ventana de protección
            DamageResult hitDuringDash = health.TakeContactOrHazardDamage(25f, body.position + Vector3.forward);
            Assert.IsTrue(hitDuringDash.WasImmune);
            Assert.AreEqual(0f, hitDuringDash.HealthLost, 0.001f);
            Assert.AreEqual(100f, health.CurrentHealth, 0.001f);
            Assert.IsFalse(health.HasDamageProtection, "Un impacto ignorado por dash no debe abrir otra ventana de protección.");

            // Pausa táctica (draft) congela el tiempo restante del dash
            float remainingBeforeDraft = motor.DashTimeRemaining;
            session.SetTacticalDraftPause(true);
            Assert.AreEqual(0f, Time.timeScale, 0.0001f);
            Assert.AreEqual(remainingBeforeDraft, motor.DashTimeRemaining, 0.0001f);

            // Al confirmar una carta en UI, la pulsación de confirmación no puede convertirse en otro dash
            session.ConfirmDraftSelectionAndResume();
            Assert.AreEqual(1f, Time.timeScale, 0.0001f);

            yield return StepUntilDashCompletes();
            ReleaseKey(keyboard.wKey);
            ReleaseKey(keyboard.spaceKey);

            // Ahora con cooldown a 0 y manteniendo Espacio/A desde la confirmación de una carta, no debe dispararse un dash accidental
            motor.ResetDashForSession();
            session.SetTacticalDraftPause(true);
            PressKey(keyboard.spaceKey);
            session.ConfirmDraftSelectionAndResume();

            yield return new WaitForFixedUpdate();
            Assert.IsFalse(motor.IsDashing, "La pulsación usada para confirmar una carta no debe convertirse en un dash.");
            ReleaseKey(keyboard.spaceKey);
        }

        [UnityTest]
        public IEnumerator VisualJuice_StretchesSilhouette_LimitsTo3Afterimages_AndCapturesPreview()
        {
            Teleport(new Vector3(-30f, 1f, -30f));
            motor.ResetDashForSession();

            PressKey(keyboard.dKey);
            PressKey(keyboard.spaceKey);

            yield return new WaitForFixedUpdate();
            Assert.IsTrue(motor.IsDashing);

            // Comprobar estiramiento 1.30x / 0.85x en el pivot de presentación
            Vector3 dashScale = visualComposer.CurrentPresentationScale;
            Assert.AreEqual(0.85f, dashScale.x, 0.02f);
            Assert.AreEqual(0.85f, dashScale.y, 0.02f);
            Assert.AreEqual(1.30f, dashScale.z, 0.02f);

            // Avanzar hasta ~0.10 s dentro del dash para tener las 3 afterimages simultáneas activas
            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate();
                dashPresentation.StepPresentation(Time.fixedDeltaTime);
            }

            Assert.LessOrEqual(dashPresentation.ActiveAfterimageCount, PlayerDashPresentation.MaxAfterimages);
            Assert.AreEqual(3, dashPresentation.TotalAfterimagesSpawnedThisDash, "Debe emitir hasta 3 afterimages por dash.");

            // Capturar preview visual del dash con la silueta estirada y sus 3 afterimages visibles
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "PlayerDash");
            Directory.CreateDirectory(dir);
            string previewPath = Path.Combine(dir, "Preview.png");

            Camera cam = Camera.main;
            Assert.IsNotNull(cam);
            var cinemachineBrain = cam.GetComponent<CinemachineBrain>();
            if (cinemachineBrain != null)
                cinemachineBrain.enabled = false;
            cam.transform.position = motor.transform.position + new Vector3(-1.1f, 6.2f, -5.2f);
            cam.transform.LookAt(motor.transform.position + new Vector3(-1.1f, 0.9f, 0f));

            var rt = new RenderTexture(1280, 720, 24);
            RenderTexture prevTarget = cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(previewPath, tex.EncodeToPNG());
                Object.Destroy(tex);
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.Destroy(rt);
            }

            Assert.IsTrue(File.Exists(previewPath), "Debe generarse la captura visual del dash.");

            // Terminar el dash y verificar que las afterimages expiran tras 0.12 s
            yield return StepUntilDashCompletes();
            ReleaseKey(keyboard.dKey);
            ReleaseKey(keyboard.spaceKey);
            dashPresentation.StepPresentation(0.15f);
            Assert.AreEqual(0, dashPresentation.ActiveAfterimageCount, "Las afterimages deben expirar tras 0.12 s.");
        }

        private void Teleport(Vector3 position)
        {
            motor.Stop();
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = Quaternion.identity;
            motor.transform.position = position;
            Physics.SyncTransforms();
        }

        private void PressKey(KeyControl key)
        {
            heldKeys.Add(key.keyCode);
            SendKeyboardState();
        }

        private void ReleaseKey(KeyControl key)
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

        private void SetLeftStick(Vector2 value)
        {
            gamepadState.leftStick = value;
            InputSystem.QueueStateEvent(gamepad, gamepadState);
            InputSystem.Update();
        }

        private void PressGamepadSouth(bool pressed)
        {
            gamepadState = gamepadState.WithButton(GamepadButton.South, pressed);
            InputSystem.QueueStateEvent(gamepad, gamepadState);
            InputSystem.Update();
        }

        private IEnumerator StepFixedSeconds(float seconds)
        {
            int steps = Mathf.CeilToInt(seconds / Time.fixedDeltaTime);
            for (int i = 0; i < steps; i++)
                yield return new WaitForFixedUpdate();
        }

        private IEnumerator StepUntilDashCompletes()
        {
            int guard = 0;
            while (!motor.IsDashing && guard < 5)
            {
                guard++;
                yield return new WaitForFixedUpdate();
            }

            guard = 0;
            while (motor.IsDashing && guard < 30)
            {
                guard++;
                yield return new WaitForFixedUpdate();
            }
        }
    }
}
