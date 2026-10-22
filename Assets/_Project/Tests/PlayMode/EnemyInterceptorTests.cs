using System.Collections;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class EnemyInterceptorTests
    {
        private Scene testScene;
        private CombatSession session;
        private GameObject playerObject;
        private PlayerRuntimeStats playerStats;
        private PlayerHealth playerHealth;
        private FlashlightWeapon weapon;
        private FlashlightPresentation flashlightPresentation;
        private EnemyPool enemyPool;
        private EnemyDeathPresentationPool deathPool;
        private Camera mainCamera;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PhysicsLayers.EnsureConfigured();
            CombatTargetRegistry.Clear();

            testScene = SceneManager.CreateScene($"EnemyInterceptorTest_{Random.Range(10000, 99999)}");
            SceneManager.SetActiveScene(testScene);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "ArenaFloor";
            floor.transform.localScale = new Vector3(8f, 1f, 8f);
            floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            {
                color = new Color(0.16f, 0.18f, 0.22f, 1f)
            };

            var lightGo = new GameObject("DirectionalLight");
            var dirLight = lightGo.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.intensity = 1.15f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            mainCamera = camGo.AddComponent<Camera>();
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            mainCamera.transform.position = new Vector3(0f, 9.5f, -5.2f);
            mainCamera.transform.rotation = Quaternion.Euler(58f, 0f, 0f);

            var sessionGo = new GameObject("CombatSession");
            session = sessionGo.AddComponent<CombatSession>();
            enemyPool = sessionGo.AddComponent<EnemyPool>();
            deathPool = sessionGo.AddComponent<EnemyDeathPresentationPool>();
            deathPool.BindSession(session);

            playerObject = new GameObject("Player");
            playerObject.layer = PhysicsLayers.PlayerLayer;
            playerObject.transform.position = new Vector3(0f, 1f, 0f);

            var playerRb = playerObject.AddComponent<Rigidbody>();
            playerRb.useGravity = false;
            playerRb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;

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
                color = new Color(0.24f, 0.68f, 0.92f, 1f)
            };

            var aimPivotGo = new GameObject("FlashlightPivot");
            aimPivotGo.transform.SetParent(playerObject.transform, false);

            playerStats = playerObject.AddComponent<PlayerRuntimeStats>();
            playerStats.ResetToInitial();

            var composer = playerObject.AddComponent<ActorVisualComposer>();
            composer.EnsureHierarchy();
            composer.CacheRenderers();

            playerHealth = playerObject.AddComponent<PlayerHealth>();
            playerHealth.BindSession(session);

            weapon = playerObject.AddComponent<FlashlightWeapon>();
            weapon.BindSession(session);
            weapon.ResetWeaponCycle();

            flashlightPresentation = playerObject.AddComponent<FlashlightPresentation>();
            flashlightPresentation.BindSession(session);
            flashlightPresentation.EnsureInitialized();

            enemyPool.BindReferences(session, playerHealth);
            session.ResetSession(31415);
            weapon.ResetWeaponCycle();

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            CombatTargetRegistry.Clear();
            if (testScene.IsValid() && testScene.isLoaded)
            {
                if (SceneManager.sceneCount <= 1)
                {
                    Scene fallback = SceneManager.CreateScene($"InterceptorCleanup_{Random.Range(10000, 99999)}");
                    SceneManager.SetActiveScene(fallback);
                }
                yield return SceneManager.UnloadSceneAsync(testScene);
            }
        }

        [Test]
        public void InterceptorStats_9Hp_Speed62Variance10Percent_Contact8Every05s_And3PulsesKill()
        {
            EnemyActor interceptor = enemyPool.Spawn(EnemyArchetypeId.Interceptor, new Vector3(0f, 1f, 0.65f));
            Physics.SyncTransforms();

            Assert.AreEqual(9f, interceptor.MaxHealth, 0.001f);
            Assert.AreEqual(9f, interceptor.CurrentHealth, 0.001f);
            Assert.GreaterOrEqual(interceptor.AssignedMoveSpeed, 6.2f * 0.90f - 0.001f);
            Assert.LessOrEqual(interceptor.AssignedMoveSpeed, 6.2f * 1.10f + 0.001f);
            Assert.AreEqual(new Vector3(0.75f, 0.75f, 1.10f), interceptor.transform.localScale);

            // Contacto real: inflige 8 de daño cada 0.5 s
            Assert.IsTrue(interceptor.TryDealContactDamage());
            Assert.AreEqual(92f, playerHealth.CurrentHealth, 0.01f);

            session.Clock.AdvanceGameplayTime(0.45f);
            playerHealth.StepTimers(0.45f);
            Assert.IsFalse(interceptor.TryDealContactDamage());

            session.Clock.AdvanceGameplayTime(0.06f);
            Assert.IsTrue(interceptor.TryDealContactDamage());
            Assert.AreEqual(84f, playerHealth.CurrentHealth, 0.01f);

            // Tres pulsos de linterna (3 * 3.75 = 11.25 >= 9 HP) abaten al Interceptor antes del 4.º pulso
            weapon.UpdateAim(0.1f);
            weapon.StepWeapon(0.20f);
            Assert.AreEqual(5.25f, interceptor.CurrentHealth, 0.01f);
            weapon.StepWeapon(0.20f);
            Assert.AreEqual(1.50f, interceptor.CurrentHealth, 0.01f);
            weapon.StepWeapon(0.20f);
            Assert.IsTrue(interceptor.IsDead, "3 pulsos normales (11.25 de daño) deben abatir al Interceptor de 9 HP.");
        }

        [Test]
        public void KnockbackException_InterceptorReceivesFullImpulseAndSlidesFartherThanResidualDueTo12Braking()
        {
            EnemyActor residual = enemyPool.Spawn(EnemyArchetypeId.Residual, new Vector3(-1.5f, 1f, 3.0f));
            EnemyActor interceptor = enemyPool.Spawn(EnemyArchetypeId.Interceptor, new Vector3(1.5f, 1f, 3.0f));

            Assert.AreEqual(1.0f, residual.Archetype.KnockbackSusceptibility, 0.001f);
            Assert.AreEqual(18f, residual.Archetype.KnockbackBraking, 0.001f);
            Assert.AreEqual(1.0f, interceptor.Archetype.KnockbackSusceptibility, 0.001f);
            Assert.AreEqual(12f, interceptor.Archetype.KnockbackBraking, 0.001f);

            residual.ApplyKnockback(new Vector3(0f, 0f, 10f));
            interceptor.ApplyKnockback(new Vector3(0f, 0f, 10f));

            Assert.AreEqual(10f, residual.KnockbackVelocity.magnitude, 0.01f);
            Assert.AreEqual(10f, interceptor.KnockbackVelocity.magnitude, 0.01f);

            float peakResidualSlide = 0f;
            float peakInterceptorSlide = 0f;

            // Simular hasta que ambos terminen su deslizamiento de empuje
            for (int step = 0; step < 50; step++)
            {
                if (residual.KnockbackVelocity.sqrMagnitude > 0.1f)
                {
                    residual.StepPhysics(0.02f);
                    residual.transform.position += residual.Body.linearVelocity * 0.02f;
                    peakResidualSlide = Mathf.Max(peakResidualSlide, residual.transform.position.z - 3.0f);
                }

                if (interceptor.KnockbackVelocity.sqrMagnitude > 0.1f)
                {
                    interceptor.StepPhysics(0.02f);
                    interceptor.transform.position += interceptor.Body.linearVelocity * 0.02f;
                    peakInterceptorSlide = Mathf.Max(peakInterceptorSlide, interceptor.transform.position.z - 3.0f);
                }
            }

            // Residual (v0=10, a=18): d ≈ 2.67 m; Interceptor (v0=10, a=12): d ≈ 4.08 m
            Assert.AreEqual(2.67f, peakResidualSlide, 0.20f);
            Assert.AreEqual(4.08f, peakInterceptorSlide, 0.20f);
            Assert.Greater(peakInterceptorSlide, peakResidualSlide + 1.1f,
                "El Interceptor debe deslizar notablemente más lejos que el Residual tras recibir el mismo impulso de 10 m/s.");
        }

        [Test]
        public void PursuitPredictsVelocity_CapsDashPrediction_AndSwitchesToDirectNearPlayer()
        {
            Rigidbody targetBody = playerObject.GetComponent<Rigidbody>();
            EnemyActor interceptor = enemyPool.Spawn(EnemyArchetypeId.Interceptor, new Vector3(0f, 1f, 5f));
            targetBody.linearVelocity = Vector3.right * 6f;
            interceptor.StepPhysics(0.02f);
            Vector3 expected = new Vector3(2.4f, 0f, -5f).normalized;
            Assert.That(Vector3.Dot(interceptor.Velocity.normalized, expected), Is.GreaterThan(0.9999f));

            targetBody.linearVelocity = Vector3.right * 22f;
            interceptor.StepPhysics(0.02f);
            expected = new Vector3(3.2f, 0f, -5f).normalized;
            Assert.That(Vector3.Dot(interceptor.Velocity.normalized, expected), Is.GreaterThan(0.9999f),
                "Un dash no debe llevar la predicción por encima de 8 m/s.");

            interceptor.Body.position = interceptor.transform.position = new Vector3(0f, 1f, 1.1f);
            Physics.SyncTransforms();
            interceptor.StepPhysics(0.02f);
            Assert.That(Vector3.Dot(interceptor.Velocity.normalized, Vector3.back), Is.GreaterThan(0.9999f),
                "A menos de 1.2 m utiliza persecución directa.");
        }

        [UnityTest]
        public IEnumerator SilhouetteAndForwardTilt_ShowsPronouncedLeanAndCapturesPreview()
        {
            EnemyActor interceptor = enemyPool.Spawn(EnemyArchetypeId.Interceptor, new Vector3(0.4f, 1f, 4.2f));
            EnemyActor interceptor2 = enemyPool.Spawn(EnemyArchetypeId.Interceptor, new Vector3(-1.1f, 1f, 3.4f));

            // Avanzar varios pasos físicos y visuales para observar la inclinación pronunciada de avance (hasta 12°)
            for (int i = 0; i < 6; i++)
            {
                interceptor.StepPhysics(0.02f);
                interceptor.transform.position += interceptor.Body.linearVelocity * 0.02f;
                interceptor.StepVisuals(0.02f);

                interceptor2.StepPhysics(0.02f);
                interceptor2.transform.position += interceptor2.Body.linearVelocity * 0.02f;
                interceptor2.StepVisuals(0.02f);
            }

            Assert.AreEqual(12f, interceptor.VisualComposer.MaxLocomotionTiltDegrees, 0.01f);
            Assert.Greater(Mathf.Abs(interceptor.VisualComposer.CurrentTiltPitch), 8f,
                "El Interceptor debe mostrar una inclinación pronunciada al avanzar a alta velocidad.");

            weapon.UpdateAim(0.2f);
            weapon.StepWeapon(0.20f);
            flashlightPresentation.StepPresentation(0.01f);

            yield return null;

            mainCamera.transform.position = new Vector3(0f, 8.6f, -2.7f);
            mainCamera.transform.LookAt(new Vector3(-0.2f, 0.6f, 2.2f));

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "EnemyInterceptor");
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

            Assert.IsTrue(File.Exists(previewPath), "Debe generarse la captura visual del Interceptor.");
        }
    }
}
