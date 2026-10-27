using System.Collections;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class FlashlightBurstFeedbackTests
    {
        private Scene testScene;
        private CombatSession session;
        private GameObject playerObject;
        private PlayerRuntimeStats playerStats;
        private PlayerHealth playerHealth;
        private ActorVisualComposer playerComposer;
        private FlashlightWeapon weapon;
        private FlashlightPresentation presentation;
        private CombatCameraFeedback cameraFeedback;
        private Camera mainCamera;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PhysicsLayers.EnsureConfigured();
            CombatTargetRegistry.Clear();

            testScene = SceneManager.CreateScene($"FlashlightTest_{Random.Range(1000, 9999)}");
            SceneManager.SetActiveScene(testScene);
            // A preceding arena integration fixture can leave its player loaded. Session
            // discovery must only see this fixture's actor, or reset targets the other weapon.
            for (int index = SceneManager.sceneCount - 2; index >= 0; index--)
            {
                Scene previous = SceneManager.GetSceneAt(index);
                if (previous.IsValid() && previous.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(previous);
            }

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "ArenaFloor";
            floor.transform.localScale = new Vector3(6f, 1f, 6f);
            var floorRenderer = floor.GetComponent<Renderer>();
            floorRenderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            {
                color = new Color(0.16f, 0.18f, 0.21f, 1f)
            };

            var lightGo = new GameObject("DirectionalLight");
            var dirLight = lightGo.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(52f, -30f, 0f);

            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            mainCamera = camGo.AddComponent<Camera>();
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            mainCamera.transform.position = new Vector3(0f, 11.5f, -6.5f);
            mainCamera.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

            var sessionGo = new GameObject("CombatSession");
            session = sessionGo.AddComponent<CombatSession>();

            playerObject = new GameObject("Player");
            playerObject.layer = PhysicsLayers.PlayerLayer;
            playerObject.transform.position = new Vector3(0f, 1f, 0f);
            var playerCol = playerObject.AddComponent<CapsuleCollider>();
            playerCol.radius = 0.45f;
            playerCol.height = 1.8f;

            var visualRoot = new GameObject("Visual");
            visualRoot.transform.SetParent(playerObject.transform, false);
            var bodyMesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bodyMesh.name = "PlayerBodyMesh";
            bodyMesh.transform.SetParent(visualRoot.transform, false);
            Object.DestroyImmediate(bodyMesh.GetComponent<Collider>());
            bodyMesh.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            {
                color = new Color(0.95f, 0.72f, 0.22f, 1f)
            };

            var aimPivotGo = new GameObject("FlashlightPivot");
            aimPivotGo.transform.SetParent(playerObject.transform, false);
            aimPivotGo.transform.localPosition = Vector3.zero;

            playerStats = playerObject.AddComponent<PlayerRuntimeStats>();
            playerStats.ResetToInitial();
            playerComposer = playerObject.AddComponent<ActorVisualComposer>();
            playerComposer.EnsureHierarchy();
            playerComposer.CacheRenderers();

            playerHealth = playerObject.AddComponent<PlayerHealth>();
            playerHealth.BindSession(session);

            weapon = playerObject.AddComponent<FlashlightWeapon>();
            weapon.BindSession(session);
            weapon.ResetWeaponCycle();

            presentation = playerObject.AddComponent<FlashlightPresentation>();
            presentation.BindSession(session);
            presentation.EnsureInitialized();

            var vcamGo = new GameObject("CombatVcam");
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Follow = playerObject.transform;
            cameraFeedback = vcamGo.AddComponent<CombatCameraFeedback>();
            cameraFeedback.BindReferences(session, playerObject.transform, null, null, weapon);

            session.ResetSession(4242);
            weapon.ResetWeaponCycle();
            presentation.ResynchronizeStateWithoutRepeatingFlashes();
            yield return null;
            // The first rendered frame can compile shaders. Manual timing starts after that frame.
            session.ResetSession(4242);
            weapon.ResetWeaponCycle();
            presentation.ResynchronizeStateWithoutRepeatingFlashes();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            CombatTargetRegistry.Clear();
            if (testScene.IsValid() && testScene.isLoaded)
            {
                Scene cleanup = SceneManager.GetSceneByName("FlashlightCleanupScene");
                if (!cleanup.IsValid() || !cleanup.isLoaded)
                    cleanup = SceneManager.CreateScene("FlashlightCleanupScene");
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(testScene);
            }
        }

        [Test]
        public void PulseSchedule_02_04_06_08_Rest04_Cycle12_AndFourthPulseKnockbackOnly()
        {
            CombatDummy dummy = CreateDummy("ScheduleDummy", new Vector3(0f, 1f, 4f), hp: 50f);

            // t = 0.19 s: aún no se ha emitido el primer pulso
            weapon.StepWeapon(0.19f);
            Assert.AreEqual(0, weapon.EmittedPulsesInCurrentBurst);
            Assert.AreEqual(50f, dummy.CurrentHealth, 0.001f);

            // t = 0.20 s: Pulso 1 (-3.75 HP, sin empuje)
            weapon.StepWeapon(0.011f);
            Assert.AreEqual(1, weapon.EmittedPulsesInCurrentBurst);
            Assert.AreEqual(46.25f, dummy.CurrentHealth, 0.01f);
            Assert.AreEqual(0, dummy.KnockbackAppliedCount);

            // t = 0.40 s: Pulso 2 (-3.75 HP, sin empuje)
            weapon.StepWeapon(0.20f);
            Assert.AreEqual(2, weapon.EmittedPulsesInCurrentBurst);
            Assert.AreEqual(42.50f, dummy.CurrentHealth, 0.01f);
            Assert.AreEqual(0, dummy.KnockbackAppliedCount);

            // t = 0.60 s: Pulso 3 (-3.75 HP, sin empuje)
            weapon.StepWeapon(0.20f);
            Assert.AreEqual(3, weapon.EmittedPulsesInCurrentBurst);
            Assert.AreEqual(38.75f, dummy.CurrentHealth, 0.01f);
            Assert.AreEqual(0, dummy.KnockbackAppliedCount);

            // t = 0.80 s: Pulso 4 (-3.75 HP -> total 15 HP en 4 pulsos, empuje 10 m/s aplicado y entra en descanso)
            weapon.StepWeapon(0.20f);
            Assert.AreEqual(4, weapon.EmittedPulsesInCurrentBurst);
            Assert.AreEqual(35.00f, dummy.CurrentHealth, 0.01f);
            Assert.AreEqual(1, dummy.KnockbackAppliedCount, "El empuje debe aplicarse únicamente en el cuarto pulso.");
            Assert.AreEqual(10f, dummy.LastKnockbackImpulse.magnitude, 0.05f, "El impulso del cuarto pulso debe ser 10 m/s.");
            Assert.IsTrue(weapon.IsInRestPhase, "Tras el 4.º pulso en 0.8 s debe entrar en el descanso de 0.4 s.");
            Assert.IsTrue(presentation.IsShowingRestDimState, "Los 4 cuadrados deben permanecer tenues durante el descanso.");

            // t = 1.15 s: sigue en descanso (ciclo de 1.2 s)
            weapon.StepWeapon(0.35f);
            Assert.IsTrue(weapon.IsInRestPhase);
            Assert.AreEqual(4, presentation.DisplayedPulseCount);

            // t = 1.20 s: comienza la ráfaga 2 y se reinician los cuadrados a 0
            weapon.StepWeapon(0.06f);
            Assert.AreEqual(2, weapon.CurrentBurstId);
            Assert.IsFalse(weapon.IsInRestPhase);
            Assert.AreEqual(0, weapon.EmittedPulsesInCurrentBurst);
            Assert.AreEqual(0, presentation.DisplayedPulseCount, "Los cuadrados deben reiniciarse al comenzar la siguiente ráfaga.");
        }

        [Test]
        public void BurstParameterSnapshot_UpgradesMidBurstApplyOnlyToNextBurst_AndLowFpsDoesNotDuplicateOrDrift()
        {
            CombatDummy dummy = CreateDummy("SnapshotDummy", new Vector3(0f, 1f, 4f), hp: 200f);

            // Emitir pulsos 1 y 2 en un único paso largo de baja tasa de FPS (0.42 s)
            weapon.StepWeapon(0.42f);
            Assert.AreEqual(2, weapon.EmittedPulsesInCurrentBurst, "Una caída de FPS debe resolver los pulsos vencidos una vez cada uno.");
            Assert.AreEqual(200f - 7.50f, dummy.CurrentHealth, 0.01f);

            // Aplicar mejoras a mitad de la ráfaga 1 (FLASH_01: +1m y +10°; FLASH_04 tier 1: +50% daño y -20°)
            playerStats.SetUpgradeTier("FLASH_01", 2); // +2 m -> 9 m, +20° -> 80°
            playerStats.SetUpgradeTier("FLASH_04", 1); // +50% daño -> 5.625, -20° -> 60°

            // La ráfaga 1 conserva los parámetros capturados al inicio (3.75 daño, 7 m alcance, 60° apertura)
            Assert.AreEqual(1, weapon.CurrentBurstId);
            Assert.AreEqual(2, weapon.EmittedPulsesInCurrentBurst, "Mejorar no debe reiniciar el contador de la ráfaga activa.");
            Assert.AreEqual(7f, weapon.ActiveBurstRange, 0.001f);
            Assert.AreEqual(60f, weapon.ActiveBurstAngle, 0.001f);

            // Completar pulsos 3 y 4 de la ráfaga 1: siguen haciendo 3.75 de daño cada uno
            weapon.StepWeapon(0.40f);
            Assert.AreEqual(4, weapon.EmittedPulsesInCurrentBurst);
            Assert.AreEqual(200f - 15.0f, dummy.CurrentHealth, 0.01f);

            // Completar el descanso de 0.4 s para iniciar la ráfaga 2 y emitir su primer pulso (t = 1.42 s)
            weapon.StepWeapon(0.60f);
            Assert.AreEqual(2, weapon.CurrentBurstId);
            Assert.AreEqual(1, weapon.EmittedPulsesInCurrentBurst);
            Assert.AreEqual(9f, weapon.ActiveBurstRange, 0.001f, "La ráfaga 2 ya adopta el nuevo alcance (9 m).");
            float expectedUpgradedPulse = 3.75f * 1.50f;
            Assert.AreEqual(200f - 15.0f - expectedUpgradedPulse, dummy.CurrentHealth, 0.02f);
        }

        [Test]
        public void Detection_RespectsCover_ThenConsidersRadiusAndDeduplicatesTargets()
        {
            // Colocar un muro/obstáculo directamente entre el jugador (0,1,0) y los objetivos
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.name = "SolidWallObstacle";
            obstacle.transform.position = new Vector3(0f, 1f, 1.5f);
            obstacle.transform.localScale = new Vector3(4f, 2f, 0.5f);

            // Objetivo 1 detrás del obstáculo con múltiples colliders hijos
            CombatDummy multiColDummy = CreateDummy("MultiColliderDummy", new Vector3(0f, 1f, 3.0f), hp: 50f);
            var extraChild1 = new GameObject("ExtraCol1");
            extraChild1.transform.SetParent(multiColDummy.transform, false);
            extraChild1.layer = multiColDummy.gameObject.layer;
            extraChild1.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
            var extraChild2 = new GameObject("ExtraCol2");
            extraChild2.transform.SetParent(multiColDummy.transform, false);
            extraChild2.layer = multiColDummy.gameObject.layer;
            extraChild2.AddComponent<SphereCollider>().radius = 0.6f;

            // Objetivo 2 cuyo centro está a 34° (> 30° semiángulo), pero cuyo radio (1.0 m a distancia 4.0 m -> ~14° radio angular) toca el borde del cono
            float angleDeg = 34f * Mathf.Deg2Rad;
            Vector3 edgePos = new Vector3(Mathf.Sin(angleDeg) * 4.0f, 1f, Mathf.Cos(angleDeg) * 4.0f);
            CombatDummy edgeDummy = CreateDummy("EdgeRadiusDummy", edgePos, hp: 50f);
            edgeDummy.transform.localScale = new Vector3(2f, 1f, 2f); // HitRadius = 0.5 * 2 = 1.0 m

            // Objetivo 3 fuera del cono (50° con radio pequeño)
            float outDeg = 50f * Mathf.Deg2Rad;
            CombatDummy outsideDummy = CreateDummy("OutsideConeDummy", new Vector3(Mathf.Sin(outDeg) * 4.0f, 1f, Mathf.Cos(outDeg) * 4.0f), hp: 50f);

            Physics.SyncTransforms();

            // Avanzar hasta el primer pulso (0.20 s) apuntando al objetivo más cercano (MultiColliderDummy en +Z)
            weapon.StepWeapon(0.21f);

            Assert.AreEqual(50f, multiColDummy.CurrentHealth, "La cobertura bloquea el daño.");
            Assert.AreEqual(50f, edgeDummy.CurrentHealth);
            Object.DestroyImmediate(obstacle);
            Physics.SyncTransforms();
            weapon.StepWeapon(0.20f);

            // MultiColliderDummy (detrás del obstáculo y con 3 colliders) debe recibir exactamente 1 impacto de 3.75 HP
            Assert.AreEqual(50f - 3.75f, multiColDummy.CurrentHealth, 0.01f, "Sin cobertura, múltiples colliders reciben un solo impacto.");

            // EdgeRadiusDummy debe recibir impacto gracias a su radio en el borde del cono
            Assert.AreEqual(50f - 3.75f, edgeDummy.CurrentHealth, 0.01f, "Debe considerar el radio del objetivo en los bordes del cono.");

            // OutsideDummy no debe recibir impacto
            Assert.AreEqual(50f, outsideDummy.CurrentHealth, 0.01f);

            Object.DestroyImmediate(obstacle);
        }

        [Test]
        public void IndicatorContract_NoHitNoDiamond_HitShowsDiamond_AndViewResyncDoesNotRepeatFlashes()
        {
            int initialMeshBuilds = presentation.GeometryBuildCount;

            // Emitir pulso 1 y 2 sin objetivos en escena
            weapon.StepWeapon(0.21f);
            presentation.StepPresentation(0.01f);
            Assert.AreEqual(1, presentation.DisplayedPulseCount);
            Assert.IsFalse(presentation.IsContactDiamondVisible, "Sin blancos no debe aparecer el rombo de confirmación.");

            weapon.StepWeapon(0.20f);
            presentation.StepPresentation(0.01f);
            Assert.AreEqual(2, presentation.DisplayedPulseCount);
            Assert.IsFalse(presentation.IsContactDiamondVisible);
            Assert.Greater(presentation.CurrentEmissionEnvelope, 0.1f, "Acaba de emitir un pulso.");

            // Desactivar y reactivar la vista tras haber emitido 2 pulsos: debe reconstruir el conteo (2) sin repetir el flash
            presentation.enabled = false;
            presentation.enabled = true;
            Assert.AreEqual(2, presentation.DisplayedPulseCount, "Al reactivar la vista debe reconstruir el conteo actual.");
            Assert.AreEqual(0f, presentation.CurrentEmissionEnvelope, 0.001f, "Reactivar la vista no debe repetir flashes anteriores.");
            Assert.IsFalse(presentation.IsContactDiamondVisible);

            // Añadir un objetivo y emitir el pulso 3: ahora sí debe aparecer el rombo de confirmación
            CombatDummy target = CreateDummy("HitDiamondDummy", new Vector3(0f, 1f, 3.5f), hp: 40f);
            weapon.StepWeapon(0.20f);
            presentation.StepPresentation(0.005f);
            Assert.AreEqual(3, presentation.DisplayedPulseCount);
            Assert.IsTrue(presentation.IsContactDiamondVisible, "Con impacto válido debe activarse el rombo de confirmación.");
            Assert.Greater(presentation.HitSparksSpawnedCount, 0, "Debe emitir chispa de impacto en la posición del objetivo.");

            // La geometría del cono no se reconstruye si no cambian alcance o ángulo
            Assert.AreEqual(initialMeshBuilds, presentation.GeometryBuildCount, "El mesh del cono sólo debe reconstruirse si cambia alcance o apertura.");
            Object.DestroyImmediate(target.gameObject);
        }

        [Test]
        public void CameraFeedback_PredictsActualVelocity_CleanWarpAbove4m_AndUseLargestXZShakePolicy()
        {
            // El autoapuntado no adelanta la cámara estando quieto.
            CombatDummy aimTarget = CreateDummy("CameraLeadTarget", new Vector3(0f, 1f, 5f), hp: 100f);
            weapon.UpdateAim(0.1f);
            cameraFeedback.StepFeedback(0.20f);
            Assert.AreEqual(0f, cameraFeedback.CurrentLeadOffsetXZ.magnitude, 0.001f);

            // Se predicen 0.10 s de velocidad real; 8 m/s produce 0.8 m, sin depender del input.
            Rigidbody body = playerObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            cameraFeedback.BindReferences(session, playerObject.transform, null, null, weapon);
            body.linearVelocity = Vector3.right * 8f;
            cameraFeedback.StepFeedback(0.08f);
            Assert.AreEqual(0.8f, cameraFeedback.CurrentLeadOffsetXZ.x, 0.01f);
            Assert.AreEqual(0f, cameraFeedback.CurrentLeadOffsetXZ.z, 0.001f);
            Assert.AreEqual(0f, cameraFeedback.CurrentLeadOffsetXZ.y, 0.0001f);

            // Al teletransportar > 4 m hace warp limpio
            playerObject.transform.position = new Vector3(10f, 1f, 10f);
            cameraFeedback.StepFeedback(0.016f);
            Assert.AreEqual(1, cameraFeedback.WarpCount, "Al teletransportar > 4 m la cámara debe registrar un warp limpio.");

            // 2. Coordinador de Camera Shake con política UseLargest en XZ
            session.Effects.ShakeIntensity = 1f;
            session.Effects.ReducedEffectsMode = false;

            cameraFeedback.RequestPresetShake(ShakePriority.Low);
            cameraFeedback.RequestPresetShake(ShakePriority.High);
            cameraFeedback.StepFeedback(0.001f);

            Assert.AreEqual(ShakePriority.High, cameraFeedback.CurrentWinningPriority);
            Assert.AreEqual(CombatCameraFeedback.PlayerHurtShakeAmplitude, cameraFeedback.CurrentWinningShakeAmplitude, 0.01f,
                "Política UseLargest: debe usar daño del jugador sin sumar el cuarto pulso.");
            Assert.LessOrEqual(cameraFeedback.CurrentShakeOffsetXZ.magnitude, CombatCameraFeedback.PlayerHurtShakeAmplitude + 0.005f);
            Assert.AreEqual(0f, cameraFeedback.CurrentShakeOffsetXZ.y, 0.0001f, "El shake sólo debe desplazar en XZ.");

            // Control de intensidad al 50%
            session.Effects.ShakeIntensity = 0.5f;
            cameraFeedback.StepFeedback(0.001f);
            Assert.LessOrEqual(cameraFeedback.CurrentWinningShakeAmplitude, CombatCameraFeedback.PlayerHurtShakeAmplitude * 0.5f + 0.01f);

            // Control de intensidad al 0%
            session.Effects.ShakeIntensity = 0f;
            cameraFeedback.StepFeedback(0.001f);
            Assert.AreEqual(0f, cameraFeedback.CurrentShakeOffsetXZ.magnitude, 0.0001f);

            Object.DestroyImmediate(aimTarget.gameObject);
        }

        [Test]
        public void CameraFeedback_ExpiringLowerIndexDoesNotInvalidateSurvivingWinner()
        {
            cameraFeedback.ResetFeedbackState();
            cameraFeedback.RequestShake(ShakePriority.Low, 0.08f, 0.10f);
            cameraFeedback.RequestShake(ShakePriority.High, 0.24f, 0.30f);

            Assert.DoesNotThrow(() => cameraFeedback.StepFeedback(0.11f),
                "Quitar una sacudida anterior de la lista no debe invalidar el ganador ya seleccionado.");
            float remaining = 1f - 0.11f / 0.30f;
            Assert.AreEqual(ShakePriority.High, cameraFeedback.CurrentWinningPriority);
            Assert.AreEqual(0.24f * remaining * remaining, cameraFeedback.CurrentWinningShakeAmplitude, 0.0001f);
            cameraFeedback.StepFeedback(0.20f);
            Assert.IsFalse(cameraFeedback.IsShaking);
        }

        [Test]
        public void CameraFeedback_VelocityScalesLead_DashIsCapped_AndReducedEffectsClearIt()
        {
            Rigidbody body = playerObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            cameraFeedback.BindReferences(session, playerObject.transform, null, null, weapon);

            body.linearVelocity = Vector3.forward * 2f;
            cameraFeedback.StepFeedback(0.08f);
            Assert.AreEqual(0.20f, cameraFeedback.CurrentLeadOffsetXZ.z, 0.001f);

            body.linearVelocity = Vector3.forward * 22f;
            cameraFeedback.StepFeedback(0.08f);
            Assert.AreEqual(CombatCameraFeedback.MaxLeadDistance, cameraFeedback.CurrentLeadOffsetXZ.z, 0.001f);

            body.linearVelocity = Vector3.zero;
            cameraFeedback.StepFeedback(0.12f);
            Assert.AreEqual(Vector3.zero, cameraFeedback.CurrentLeadOffsetXZ);

            body.linearVelocity = Vector3.forward * 22f;
            cameraFeedback.StepFeedback(0.08f);
            cameraFeedback.RequestPresetShake(ShakePriority.High);
            cameraFeedback.StepFeedback(0.001f);
            session.Effects.ReducedEffectsMode = true;
            Assert.AreEqual(Vector3.zero, cameraFeedback.CurrentLeadOffsetXZ);
            Assert.IsFalse(cameraFeedback.IsShaking);
            cameraFeedback.StepFeedback(0.20f);
            Assert.AreEqual(Vector3.zero, cameraFeedback.CurrentLeadOffsetXZ);
        }

        [Test]
        public void CameraFeedback_PauseAndResetDiscardOldShakes_AndAmplitudeIsCapped()
        {
            cameraFeedback.RequestShake(ShakePriority.Critical, 100f, 1f);
            cameraFeedback.StepFeedback(0f);
            Assert.AreEqual(CombatCameraFeedback.MaxShakeAmplitude, cameraFeedback.CurrentWinningShakeAmplitude, 0.0001f);

            session.SetManualPause(true);
            Assert.AreEqual(Vector3.zero, cameraFeedback.CurrentShakeOffsetXZ);
            Assert.IsFalse(cameraFeedback.IsShaking);
            session.SetManualPause(false);
            cameraFeedback.StepFeedback(0.01f);
            Assert.IsFalse(cameraFeedback.IsShaking);

            cameraFeedback.RequestPresetShake(ShakePriority.High);
            cameraFeedback.StepFeedback(0.001f);
            Assert.IsTrue(cameraFeedback.IsShaking);
            session.ResetSession(4242);
            Assert.IsFalse(cameraFeedback.IsShaking, "El reinicio no puede conservar una sacudida de la sesión anterior.");
        }

        [UnityTest]
        public IEnumerator VisualPreview_CapturesRescueLensCone4SquaresAndTargetHit()
        {
            session.Effects.ShakeIntensity = 1f;
            session.Effects.ReducedEffectsMode = false;
            playerObject.transform.position = Vector3.zero;

            CombatDummy targetDummy = CreateDummy("PreviewTarget", new Vector3(1.2f, 1f, 3.8f), hp: 50f);
            var rend = targetDummy.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                rend.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                {
                    color = new Color(0.85f, 0.28f, 0.24f, 1f)
                };
            }

            // Emitir 3 pulsos sobre el objetivo para mostrar el cono Apollo, 3 cuadrados encendidos, rombo de contacto y flash de impacto
            weapon.UpdateAim(0.2f);
            weapon.StepWeapon(0.20f);
            weapon.StepWeapon(0.20f);
            weapon.StepWeapon(0.20f);
            presentation.StepPresentation(0.005f);

            session.SetManualPause(true);
            yield return null;

            mainCamera.transform.position = new Vector3(0.6f, 8.8f, -2.6f);
            mainCamera.transform.LookAt(new Vector3(0.6f, 0.5f, 2.2f));

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "FlashlightBurstFeedback");
            Directory.CreateDirectory(dir);
            string previewPath = Path.Combine(dir, "Preview.png");

            var rt = new RenderTexture(1280, 720, 24);
            RenderTexture prevTarget = mainCamera.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                mainCamera.targetTexture = rt;
                mainCamera.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(previewPath, tex.EncodeToPNG());
                Object.Destroy(tex);
            }
            finally
            {
                mainCamera.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.Destroy(rt);
            }

            Assert.IsTrue(File.Exists(previewPath), "Debe generarse la captura visual de la linterna.");
            Assert.AreEqual(3, presentation.DisplayedPulseCount);
            Assert.IsTrue(presentation.IsContactDiamondVisible);
        }

        [Test]
        public void SolarIndicatorCountsFourPulses_HoldsFourthAcrossBoundary_AndResyncDoesNotReplayFlash()
        {
            playerStats.SetUpgradeTier("FLASH_05", 1);
            weapon.ResetWeaponCycle();
            for (int index = 1; index <= 4; index++)
            {
                weapon.StepWeapon(0.2f);
                Assert.AreEqual(index, presentation.DisplayedPulseCount);
            }
            Assert.AreEqual(4, weapon.TotalPulsesEmitted);
            Assert.IsFalse(weapon.IsInRestPhase);
            Assert.Greater(presentation.CurrentEmissionEnvelope, 0f, "El cuarto acento no debe borrarse al abrir el siguiente bloque Solar.");
            presentation.enabled = false;
            presentation.enabled = true;
            presentation.StepPresentation(0f);
            Assert.AreEqual(4, presentation.DisplayedPulseCount);
            Assert.AreEqual(0f, presentation.CurrentEmissionEnvelope, "Reactivar la vista no repite emisiones.");
            weapon.StepWeapon(0.2f);
            Assert.AreEqual(5, weapon.TotalPulsesEmitted);
            Assert.AreEqual(1, presentation.DisplayedPulseCount);
        }

        [Test]
        public void AcquiringSolarMidBurstPreservesCurrentDamageAndCoverageUntilNextCompleteBurst()
        {
            CombatDummy target = CreateDummy("Upgrade boundary target", new Vector3(0f, 1f, 5f), 1000f);
            weapon.ResetWeaponCycle();
            weapon.StepWeapon(0.2f);
            playerStats.SetUpgradeTier("FLASH_05", 1);
            playerStats.SetUpgradeTier("FLASH_01", 5);
            Assert.AreEqual(7f, weapon.ActiveBurstRange);
            weapon.StepWeapon(0.6f);
            Assert.AreEqual(985f, target.CurrentHealth, 0.001f);
            Assert.IsTrue(weapon.IsInRestPhase);
            weapon.StepWeapon(0.4f);
            Assert.AreEqual(19f, weapon.ActiveBurstRange);
            weapon.StepWeapon(0.2f);
            Assert.AreEqual(975.625f, target.CurrentHealth, 0.001f);
            Assert.AreEqual(1, presentation.DisplayedPulseCount);
        }

        [Test]
        public void ImportedRefactorViewUsesRealShadowsAndFourMarkers_WithThreeDimensionalBeam()
        {
            Assert.IsNotNull(presentation.ImportedView);
            Assert.AreEqual(LightShadows.Soft, presentation.ImportedView.KeyLight.shadows);
            Assert.AreEqual(0.8f, presentation.ImportedView.KeyLight.transform.localPosition.z, 0.001f);
            for (int index = 1; index <= 4; index++)
            {
                weapon.StepWeapon(0.2f);
                presentation.StepPresentation(0f);
                Assert.AreEqual(index, presentation.ImportedView.EmittedCount);
                Assert.IsFalse(presentation.ConeRenderer.enabled, "The legacy ground fan must not duplicate the 3D beam.");
                Assert.IsTrue(presentation.OilLantern.BeamRenderer.enabled);
                Assert.IsTrue(presentation.EmitterRenderer.enabled, "Hiding the fan must preserve the four pulse markers.");
            }
            Assert.IsNotNull(presentation.OilLantern.OilGlow);
            Assert.AreEqual(LightType.Point, presentation.OilLantern.OilGlow.type);
            Assert.Greater(presentation.OilLantern.BeamMesh.bounds.size.y, 4f,
                "The beam has vertical volume, not only an XZ fan.");
            session.Effects.ReducedEffectsMode = true;
            presentation.StepPresentation(0f);
            Assert.IsFalse(presentation.OilLantern.BeamRenderer.enabled);
            Assert.IsFalse(presentation.ConeRenderer.enabled, "Reduced effects must not restore the ground fan.");
            Assert.IsTrue(presentation.ImportedView.KeyLight.enabled);
            Assert.AreEqual(4, presentation.ImportedView.EmittedCount);
            session.Effects.ReducedEffectsMode = false;
            playerStats.SetUpgradeTier("FLASH_05", 1);
            weapon.StepWeapon(0.4f);
            presentation.StepPresentation(0f);
            Assert.IsTrue(weapon.IsSolarModeActive);
            Assert.IsFalse(presentation.ConeRenderer.enabled);
            Assert.IsTrue(presentation.OilLantern.BeamRenderer.enabled);
            presentation.enabled = false;
            presentation.enabled = true;
            presentation.StepPresentation(0f);
            Assert.IsFalse(presentation.ConeRenderer.enabled, "Reactivating the view must preserve the single-beam presentation.");
            Assert.IsTrue(presentation.EmitterRenderer.enabled);
        }

        [UnityTest]
        public IEnumerator ImportedOilLanternPreviewIlluminatesTallObjects_FromGameplayAndSideViews()
        {
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (Light light in lights)
                if (light.type == LightType.Directional) { light.intensity = 0.12f; light.color = new Color(0.65f, 0.77f, 1f); }
            Color oldAmbient = RenderSettings.ambientLight;
            var oldMode = RenderSettings.ambientMode;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.09f, 0.13f);
            for (int i = 0; i < 3; i++)
            {
                var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.name = "3D light witness " + i;
                obstacle.transform.position = new Vector3((i - 1) * 1.7f, 0.7f + i * 0.35f, 4f + i * 1.1f);
                obstacle.transform.localScale = new Vector3(0.8f, 1.4f + i * 0.7f, 0.8f);
            }
            weapon.StepWeapon(0.2f); presentation.StepPresentation(0f);
            session.SetManualPause(true);
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "PrototypeFlashlight");
            Directory.CreateDirectory(directory);
            try
            {
                for (int view = 0; view < 2; view++)
                {
                    mainCamera.transform.position = view == 0 ? new Vector3(1f, 12f, -5f) : new Vector3(9f, 4.5f, -2f);
                    mainCamera.transform.LookAt(new Vector3(0f, 1f, 3.5f));
                    var target = new RenderTexture(1280, 720, 24);
                    mainCamera.targetTexture = target;
                    yield return null; yield return null; yield return null;
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    var capture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                    capture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); capture.Apply();
                    File.WriteAllBytes(Path.Combine(directory, view == 0 ? "Preview.png" : "Preview_Side.png"), capture.EncodeToPNG());
                    RenderTexture.active = previous; mainCamera.targetTexture = null;
                    Object.Destroy(capture); Object.Destroy(target);
                }
            }
            finally
            {
                RenderSettings.ambientMode = oldMode; RenderSettings.ambientLight = oldAmbient;
                session.SetManualPause(false);
            }
        }

        [Test]
        public void BossClosingShakeContinuesDuringVictoryFreeze_ThenExpiresAndResets()
        {
            var death = new ActorDeathEvent(99, 99, false, true, Vector3.zero,
                Vector3.forward, DamageSourceType.SolarPulse, 500, false, true);
            session.QueueBossDeath(in death);
            session.ResolveSimulationStepOutcome();
            Assert.AreEqual(SessionState.Victory, session.State);
            Assert.AreEqual(0f, Time.timeScale);
            float gameplayTime = session.GameplayTime;
            cameraFeedback.StepFeedback(0.01f);
            Assert.IsTrue(cameraFeedback.IsShaking);
            Assert.AreEqual(ShakePriority.Critical, cameraFeedback.CurrentWinningPriority);
            Assert.AreEqual(Vector3.zero, cameraFeedback.CurrentLeadOffsetXZ);
            cameraFeedback.RequestPresetShake(ShakePriority.High);
            cameraFeedback.StepFeedback(0.45f);
            Assert.IsFalse(cameraFeedback.IsShaking);
            Assert.AreEqual(gameplayTime, session.GameplayTime);
            session.ResetSession();
            Assert.AreEqual(Vector3.zero, cameraFeedback.CurrentShakeOffsetXZ);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void ReducedEffectsAndSimultaneousDefeatDoNotPlayBossClosingShake()
        {
            var death = new ActorDeathEvent(99, 99, false, true, Vector3.zero,
                Vector3.forward, DamageSourceType.SolarPulse, 500, false, true);
            session.Effects.ReducedEffectsMode = true;
            session.QueueBossDeath(in death);
            session.ResolveSimulationStepOutcome();
            cameraFeedback.StepFeedback(0.01f);
            Assert.IsFalse(cameraFeedback.IsShaking);
            session.ResetSession();
            session.Effects.ReducedEffectsMode = false;
            session.QueueBossDeath(in death);
            var playerDeath = new ActorDeathEvent(1, 0, true, false, Vector3.zero,
                Vector3.forward, DamageSourceType.DummyOrHazard, 0, false, true);
            session.QueuePlayerDeath(in playerDeath);
            session.ResolveSimulationStepOutcome();
            cameraFeedback.StepFeedback(0.01f);
            Assert.AreEqual(SessionState.Defeat, session.State);
            Assert.IsFalse(cameraFeedback.IsShaking);
            session.ResetSession();
        }

        [Test]
        public void CriticalHealthBorderPersistsBelowThirtyPercent_AndHealingClearsIt()
        {
            var hurt = playerObject.AddComponent<PlayerHurtPresentation>();
            hurt.Bind(session, playerHealth);
            playerHealth.ForceSetHealthForTesting(30f);
            hurt.StepPresentation(1f);
            Assert.That(hurt.ScreenBorderAlpha, Is.InRange(0.0719f, 0.1001f),
                "At the 30% threshold the soft border must remain visible throughout its breathing cycle.");
            float thresholdAlpha = hurt.ScreenBorderAlpha;
            playerHealth.ForceSetHealthForTesting(15f);
            hurt.StepPresentation(1f);
            Assert.Greater(hurt.ScreenBorderAlpha, thresholdAlpha, "The warning grows as health falls.");
            playerHealth.ForceSetHealthForTesting(30f);
            session.Effects.ReducedEffectsMode = true;
            Assert.AreEqual(0.07f, hurt.ScreenBorderAlpha, 0.0001f);
            playerHealth.Heal(10f);
            Assert.AreEqual(0f, hurt.ScreenBorderAlpha);
            session.ResetSession();
            Assert.AreEqual(0f, hurt.ScreenBorderAlpha);
        }

        [Test]
        public void SessionResetRestoresAimAndVisualOrientation_WithoutRetainingTarget()
        {
            CreateDummy("Aim behind", new Vector3(0f, 1f, -4f), 100f);
            weapon.UpdateAim(1f);
            Assert.Less(weapon.CurrentAimDirection.z, -0.9f);
            Assert.IsNotNull(weapon.CurrentTarget);
            playerComposer.ReportLocomotion(Vector3.back, Vector3.back * 8f, 8f, 0f);
            playerComposer.StepVisuals(0.2f, reducedEffects: false);
            session.ResetSession();
            Assert.IsNull(weapon.CurrentTarget);
            Assert.AreEqual(Vector3.forward, weapon.CurrentAimDirection);
            Assert.Less(Quaternion.Angle(Quaternion.identity, playerComposer.OrientationRoot.localRotation), 0.01f);
        }

        private CombatDummy CreateDummy(string name, Vector3 position, float hp)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.layer = PhysicsLayers.EnemyLayer;
            go.transform.position = position;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            var meshGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            meshGo.name = "Mesh";
            meshGo.transform.SetParent(visual.transform, false);
            Object.DestroyImmediate(meshGo.GetComponent<Collider>());
            Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(go.GetComponent<MeshFilter>());

            var composer = go.AddComponent<ActorVisualComposer>();
            composer.EnsureHierarchy();
            composer.CacheRenderers();

            var dummy = go.AddComponent<CombatDummy>();
            dummy.Configure(session, playerHealth, hp: hp, enableContact: false);
            Physics.SyncTransforms();
            return dummy;
        }
    }
}
