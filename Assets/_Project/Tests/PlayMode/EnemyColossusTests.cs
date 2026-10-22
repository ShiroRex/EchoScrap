using System.Collections;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class EnemyColossusTests
    {
        private Scene testScene;
        private CombatSession session;
        private GameObject playerObject;
        private Rigidbody playerRb;
        private CapsuleCollider playerCol;
        private PlayerRuntimeStats playerStats;
        private PlayerHealth playerHealth;
        private FlashlightWeapon weapon;
        private FlashlightPresentation flashlightPresentation;
        private CombatCameraFeedback cameraFeedback;
        private EnemyPool enemyPool;
        private EnemyDeathPresentationPool deathPool;
        private Camera mainCamera;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PhysicsLayers.EnsureConfigured();
            CombatTargetRegistry.Clear();

            testScene = SceneManager.CreateScene($"EnemyColossusTest_{Random.Range(10000, 99999)}");
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
            mainCamera.transform.position = new Vector3(0f, 10f, -5.5f);
            mainCamera.transform.rotation = Quaternion.Euler(58f, 0f, 0f);

            var sessionGo = new GameObject("CombatSession");
            session = sessionGo.AddComponent<CombatSession>();
            enemyPool = sessionGo.AddComponent<EnemyPool>();
            deathPool = sessionGo.AddComponent<EnemyDeathPresentationPool>();
            deathPool.BindSession(session);

            playerObject = new GameObject("Player");
            playerObject.layer = PhysicsLayers.PlayerLayer;
            playerObject.transform.position = new Vector3(0f, 1f, 0f);

            playerRb = playerObject.AddComponent<Rigidbody>();
            playerRb.useGravity = false;
            playerRb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;

            playerCol = playerObject.AddComponent<CapsuleCollider>();
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

            var vcamGo = new GameObject("CombatVcam");
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Follow = playerObject.transform;
            cameraFeedback = vcamGo.AddComponent<CombatCameraFeedback>();
            cameraFeedback.BindReferences(session, playerObject.transform, null, null, weapon);

            enemyPool.BindReferences(session, playerHealth);
            session.ResetSession(8080);
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
                    Scene fallback = SceneManager.CreateScene($"ColossusCleanup_{Random.Range(10000, 99999)}");
                    SceneManager.SetActiveScene(fallback);
                }
                yield return SceneManager.UnloadSceneAsync(testScene);
            }
        }

        [Test]
        public void ColossusStats_80Hp_Speed18_Contact25Every05s_KnockbackImmune_AndHeavyDeformation2To4Percent()
        {
            EnemyActor colossus = enemyPool.Spawn(EnemyArchetypeId.Colossus, new Vector3(0f, 1f, 3.0f));
            Physics.SyncTransforms();

            Assert.AreEqual(80f, colossus.MaxHealth, 0.001f);
            Assert.AreEqual(80f, colossus.CurrentHealth, 0.001f);
            Assert.AreEqual(1.8f, colossus.AssignedMoveSpeed, 0.001f);
            Assert.AreEqual(new Vector3(1.6f, 1.6f, 1.6f), colossus.transform.localScale);
            Assert.AreEqual(0f, colossus.Archetype.KnockbackSusceptibility, 0.0001f);
            Assert.IsTrue(colossus.Archetype.IsEliteDropTier);
            Assert.AreEqual(50, colossus.Archetype.XpReward);

            // Emitir una ráfaga completa de 4 pulsos (4 * 3.75 = 15 de daño -> queda a 65 HP)
            Vector3 initialPos = colossus.transform.position;
            weapon.UpdateAim(0.1f);
            weapon.StepWeapon(0.20f);
            weapon.StepWeapon(0.20f);
            weapon.StepWeapon(0.20f);
            weapon.StepWeapon(0.20f);

            Assert.AreEqual(65f, colossus.CurrentHealth, 0.01f);
            Assert.AreEqual(0, colossus.KnockbackAppliedCount, "El Coloso es inmune al empuje del cuarto pulso.");
            Assert.AreEqual(Vector3.zero, colossus.KnockbackVelocity);
            Assert.AreEqual(initialPos, colossus.transform.position, "La raíz física no debe desplazarse por empuje.");

            // Verificar deformación pesada del 2%-4% (0.03 -> escala X/Z ~1.03) en PresentationPivot sin mover la raíz física
            Vector3 hitScale = colossus.VisualComposer.CurrentPresentationScale;
            float xzExpansion = hitScale.x - 1f;
            Assert.GreaterOrEqual(xzExpansion, 0.02f - 0.002f, "La deformación pesada debe estar en el rango 2%-4%.");
            Assert.LessOrEqual(xzExpansion, 0.04f + 0.002f, "La deformación pesada no debe exceder el 4%.");

            // Verificar daño de contacto real (25 HP cada 0.5 s) gracias al radio escalado (0.5 * 1.6 = 0.8 m)
            colossus.transform.position = new Vector3(0f, 1f, 1.15f);
            colossus.Body.position = colossus.transform.position;
            Physics.SyncTransforms();

            Assert.IsTrue(colossus.TryDealContactDamage(), "El collider real de 1.6x debe contactar a 1.15 m de distancia entre centros.");
            Assert.AreEqual(75f, playerHealth.CurrentHealth, 0.01f);
        }

        [UnityTest]
        public IEnumerator SolidBodyAndDeathShake_BlocksNormalPlayer_PhasedByDash_AndTriggersMediumShakeOnDeath()
        {
            EnemyActor colossus = enemyPool.Spawn(EnemyArchetypeId.Colossus, new Vector3(0f, 1f, 2.2f));
            Physics.SyncTransforms();

            // 1. El Coloso conserva cuerpo sólido frente al jugador en locomoción normal (IgnoreCollision == false)
            Assert.IsFalse(Physics.GetIgnoreCollision(colossus.BodyCollider, playerCol),
                "El Coloso debe conservar colisión física sólida con el jugador.");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(PhysicsLayers.PlayerLayer, PhysicsLayers.EnemyLayer));

            // 2. Durante el dash (PlayerDashLayer), la matriz ignora EnemyLayer y permite atravesar el cuerpo sólido del Coloso
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(PhysicsLayers.PlayerDashLayer, PhysicsLayers.EnemyLayer));

            // 3. Al morir el Coloso, dispara automáticamente shake Medium (0.14 m / 0.16 s) y entrega 50 XP
            int xpGranted = 0;
            session.Events.OnActorDied += evt => xpGranted = evt.XpReward;

            var killReq = new DamageRequest(1, DamageSourceType.FlashlightPulse, Vector3.zero, Vector3.forward, 100f);
            colossus.ReceiveDamage(in killReq);
            cameraFeedback.StepFeedback(0.001f);

            Assert.IsTrue(colossus.IsDead);
            Assert.AreEqual(50, xpGranted);
            Assert.AreEqual(ShakePriority.Medium, cameraFeedback.CurrentWinningPriority, "Morir un Coloso debe activar shake Medium.");
            Assert.AreEqual(CombatCameraFeedback.MediumExplosionShakeAmplitude, cameraFeedback.CurrentWinningShakeAmplitude, 0.01f);

            // 4. Preparar captura visual con un Coloso recibiendo impacto frente a un Residual para comparar escala 1.6x
            EnemyActor previewColossus = enemyPool.Spawn(EnemyArchetypeId.Colossus, new Vector3(0.3f, 1f, 3.6f));
            EnemyActor previewResidual = enemyPool.Spawn(EnemyArchetypeId.Residual, new Vector3(-1.4f, 1f, 3.4f));
            weapon.ResetWeaponCycle();
            weapon.UpdateAim(0.2f);
            weapon.StepWeapon(0.20f);
            flashlightPresentation.StepPresentation(0.01f);
            previewColossus.StepVisuals(0.01f);
            previewResidual.StepVisuals(0.01f);
            deathPool.StepPool(0.03f);

            yield return null;

            mainCamera.transform.position = new Vector3(0f, 9.2f, -3.0f);
            mainCamera.transform.LookAt(new Vector3(0f, 0.8f, 2.3f));

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "EnemyColossus");
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

            Assert.IsTrue(File.Exists(previewPath), "Debe generarse la captura visual del Coloso.");
        }
    }
}
