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
    public sealed class EnemyResidualTests
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

            testScene = SceneManager.CreateScene($"EnemyResidualTest_{Random.Range(10000, 99999)}");
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
            session.ResetSession(7777);
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
                    Scene fallback = SceneManager.CreateScene($"ResidualCleanup_{Random.Range(10000, 99999)}");
                    SceneManager.SetActiveScene(fallback);
                }
                yield return SceneManager.UnloadSceneAsync(testScene);
            }
        }

        [Test]
        public void ResidualStats_15Hp_SpeedVariance15Percent_4PulsesKill_AndFourthPulseKnockbackBrakesSmoothly()
        {
            // 1. Verificar muestreo de velocidad dentro de 3.5 m/s ±15% ([2.975, 4.025])
            EnemyActor r1 = enemyPool.Spawn(EnemyArchetypeId.Residual, new Vector3(0f, 1f, 3.2f));
            Assert.AreEqual(15f, r1.MaxHealth, 0.001f);
            Assert.AreEqual(15f, r1.CurrentHealth, 0.001f);
            Assert.GreaterOrEqual(r1.AssignedMoveSpeed, 3.5f * 0.85f - 0.001f);
            Assert.LessOrEqual(r1.AssignedMoveSpeed, 3.5f * 1.15f + 0.001f);

            // 2. Tres primeros pulsos de linterna (3 * 3.75 = 11.25 daño -> queda a 3.75 HP, sin empuje)
            weapon.UpdateAim(0.1f);
            weapon.StepWeapon(0.20f);
            weapon.StepWeapon(0.20f);
            weapon.StepWeapon(0.20f);

            Assert.IsTrue(r1.IsAlive);
            Assert.AreEqual(3.75f, r1.CurrentHealth, 0.01f);
            Assert.AreEqual(0, r1.KnockbackAppliedCount);

            // Subir HP manualmente para probar el empuje y frenado del 4.º pulso antes de morir
            var boostData = EnemyArchetypeData.CreateDefault(EnemyArchetypeId.Residual);
            boostData.MaxHealth = 30f;
            r1.InitializeOrResetFromPool(boostData, session, playerHealth, new Vector3(0f, 1f, 3.2f));

            // Llegar al 4.º pulso en 0.80 s: aplica impulso de 10 m/s * susceptibilidad 1.0 = 10 m/s en +Z
            weapon.StepWeapon(0.20f);
            Assert.AreEqual(1, r1.KnockbackAppliedCount);
            Assert.AreEqual(10f, r1.KnockbackVelocity.magnitude, 0.05f);

            float zBefore = r1.transform.position.z;
            for (int i = 0; i < 15; i++)
            {
                r1.StepPhysics(0.02f);
                r1.transform.position = r1.Body.position + r1.Body.linearVelocity * 0.02f;
                r1.Body.position = r1.transform.position;
            }

            // Con frenado 18 m/s², tras 0.30 s la velocidad de empuje se ha reducido en 5.4 m/s (queda ~4.6 m/s) y el enemigo retrocedió en +Z
            Assert.Greater(r1.transform.position.z, zBefore + 0.5f, "El 4.º pulso debe empujar hacia atrás al Residual.");
            Assert.AreEqual(10f - 18f * 0.30f, r1.KnockbackVelocity.magnitude, 0.1f);
        }

        [Test]
        public void ContactDamage_Deals10Every05s_DeathDisablesImmediately_AndPoolResetRestoresFullContract()
        {
            EnemyActor residual = enemyPool.Spawn(EnemyArchetypeId.Residual, new Vector3(0f, 1f, 0.65f));
            Physics.SyncTransforms();

            // 1. Contacto real entre colliders: hace 10 de daño e inicia cooldown de 0.5 s en el enemigo
            bool dealtFirst = residual.TryDealContactDamage();
            Assert.IsTrue(dealtFirst);
            Assert.AreEqual(90f, playerHealth.CurrentHealth, 0.01f);

            // Antes de 0.5 s el enemigo no vuelve a intentar contacto
            session.Clock.AdvanceGameplayTime(0.45f);
            playerHealth.StepTimers(0.45f); // Expiran los 0.40 s de i-frames del jugador, pero faltan 0.05 s del cooldown de contacto del enemigo
            Assert.IsFalse(residual.TryDealContactDamage());

            // A los 0.50 s vuelve a hacer 10 de daño de contacto
            session.Clock.AdvanceGameplayTime(0.06f);
            Assert.IsTrue(residual.TryDealContactDamage());
            Assert.AreEqual(80f, playerHealth.CurrentHealth, 0.01f);

            // 2. Enemigo alterado antes de morir y reciclarse en el pool
            residual.ApplyKnockback(new Vector3(0f, 0f, 10f));
            residual.SetTimersForTesting(99f, 5f, 2f, 1f);
            residual.transform.localScale = new Vector3(2.2f, 0.5f, 2.2f);
            residual.gameObject.layer = PhysicsLayers.DefaultLayer;

            int deathsPublished = 0;
            ActorDeathEvent lastDeath = default;
            session.Events.OnActorDied += evt =>
            {
                deathsPublished++;
                lastDeath = evt;
            };

            // 3. Matar con 15 de daño: desactiva inmediatamente actor/colisión, resuelve recompensa 1 sola vez y lanza ruptura en EnemyDeathPresentationPool
            var killReq = new DamageRequest(1, DamageSourceType.FlashlightPulse, Vector3.zero, Vector3.forward, 20f);
            residual.ReceiveDamage(in killReq);

            Assert.IsTrue(residual.IsDead);
            Assert.AreEqual(1, deathsPublished, "La muerte y su recompensa deben publicarse exactamente una vez.");
            Assert.AreEqual(10, lastDeath.XpReward);
            Assert.IsTrue(lastDeath.RewardResolved);
            Assert.IsFalse(residual.gameObject.activeSelf, "El actor debe desactivarse y volver al pool inmediatamente al morir.");
            Assert.GreaterOrEqual(deathPool.ActiveBurstCount, 1, "La ruptura visual independiente debe quedar activa en el lugar de muerte.");
            Assert.GreaterOrEqual(deathPool.LastSpawnedFragmentCount, 6);
            Assert.LessOrEqual(deathPool.LastSpawnedFragmentCount, 10);

            // Un segundo impacto sobre el actor ya muerto no duplica muerte ni recompensas
            residual.ReceiveDamage(in killReq);
            Assert.AreEqual(1, deathsPublished);

            // 4. Reutilizar la misma instancia desde el pool y comprobar el contrato íntegro de restauración (Sección 2.5)
            int createdBefore = enemyPool.TotalCreatedCount;
            EnemyActor respawned = enemyPool.Spawn(EnemyArchetypeId.Residual, new Vector3(4f, 1f, 4f));
            Assert.AreSame(residual, respawned, "Debe reciclarse la instancia existente del pool sin crear otra nueva.");
            Assert.AreEqual(createdBefore, enemyPool.TotalCreatedCount);

            Assert.IsTrue(respawned.IsAlive);
            Assert.AreEqual(15f, respawned.CurrentHealth, 0.001f);
            Assert.AreEqual(Vector3.zero, respawned.KnockbackVelocity);
            Assert.AreEqual(Vector3.zero, respawned.Velocity);
            Assert.AreEqual(0f, respawned.NextContactReadyTime, 0.001f);
            Assert.AreEqual(0f, respawned.AttackCooldownTimer, 0.001f);
            Assert.AreEqual(0f, respawned.FuseTimer, 0.001f);
            Assert.AreEqual(0f, respawned.TelegraphTimer, 0.001f);
            Assert.IsFalse(respawned.IsFuseActive);
            Assert.AreEqual(PhysicsLayers.EnemyLayer, respawned.gameObject.layer);
            Assert.IsTrue(respawned.BodyCollider.enabled);
            Assert.IsFalse(respawned.BodyCollider.isTrigger);
            Assert.AreEqual(Vector3.one, respawned.transform.localScale);
            Assert.IsFalse(respawned.DeathRewardResolved);
            Assert.AreEqual(0, respawned.DeathNotifyCount);
        }

        [Test]
        public void LocalSeparation_PreventsOverlappingWhenMultipleResidualsChasePlayer()
        {
            // Colocar 4 Residuales casi en el mismo punto persiguiendo al jugador
            EnemyActor e0 = enemyPool.Spawn(EnemyArchetypeId.Residual, new Vector3(0.05f, 1f, 5.0f));
            EnemyActor e1 = enemyPool.Spawn(EnemyArchetypeId.Residual, new Vector3(-0.05f, 1f, 5.0f));
            EnemyActor e2 = enemyPool.Spawn(EnemyArchetypeId.Residual, new Vector3(0.0f, 1f, 5.05f));
            EnemyActor e3 = enemyPool.Spawn(EnemyArchetypeId.Residual, new Vector3(0.0f, 1f, 4.95f));

            float initialAvgSeparation = AveragePairwiseDistanceXZ(e0, e1, e2, e3);

            for (int step = 0; step < 30; step++)
            {
                e0.StepPhysics(0.02f);
                e1.StepPhysics(0.02f);
                e2.StepPhysics(0.02f);
                e3.StepPhysics(0.02f);

                e0.transform.position += e0.Body.linearVelocity * 0.02f;
                e1.transform.position += e1.Body.linearVelocity * 0.02f;
                e2.transform.position += e2.Body.linearVelocity * 0.02f;
                e3.transform.position += e3.Body.linearVelocity * 0.02f;
            }

            float finalAvgSeparation = AveragePairwiseDistanceXZ(e0, e1, e2, e3);
            Assert.Greater(finalAvgSeparation, initialAvgSeparation + 0.35f,
                "La separación local debe abrir el grupo de Residuales para evitar superposición.");
        }

        [UnityTest]
        public IEnumerator FirstFeelCheck_ThreeConditionsMaintainDeterministicCombat_AndCapturePreview()
        {
            // Primera comprobación de sensación (Sección 1 y 5.2):
            // Dash + Linterna + Residual comparados bajo las 3 condiciones:
            // 1) Sin efectos decorativos, 2) Con la respuesta visual completa, 3) En modo reducido.
            float hpCondition1 = SimulateEncounterUnderCondition(PresentationCondition.NoDecorativeEffects, out int fragmentsCond1);
            float hpCondition2 = SimulateEncounterUnderCondition(PresentationCondition.FullEffects, out int fragmentsCond2);
            float hpCondition3 = SimulateEncounterUnderCondition(PresentationCondition.ReducedEffects, out int fragmentsCond3);

            Assert.AreEqual(hpCondition1, hpCondition2, 0.0001f, "Cambiar partículas/efectos no debe alterar el resultado de combate.");
            Assert.AreEqual(hpCondition1, hpCondition3, 0.0001f, "El modo reducido debe conservar exactamente el mismo resultado de combate.");
            Assert.AreEqual(0, fragmentsCond1, "Sin efectos decorativos no deben emitirse fragmentos de muerte.");
            Assert.GreaterOrEqual(fragmentsCond2, 6, "Con respuesta visual completa deben emitirse 6-10 fragmentos radiales.");
            Assert.AreEqual(0, fragmentsCond3, "En modo reducido se omiten fragmentos secundarios conservando lectura clara.");

            // Preparar captura visual del primer encuentro (Jugador + Cono Linterna + Residuales + Ruptura de muerte)
            enemyPool.DespawnAllActive();
            deathPool.ClearAll();
            session.Effects.ApplyCondition(PresentationCondition.FullEffects);
            session.ResetSession(2026);
            playerObject.transform.position = Vector3.zero;
            weapon.ResetWeaponCycle();

            EnemyActor frontResidual = enemyPool.Spawn(EnemyArchetypeId.Residual, new Vector3(0.4f, 1f, 3.3f));
            EnemyActor flankResidual = enemyPool.Spawn(EnemyArchetypeId.Residual, new Vector3(-1.4f, 1f, 4.1f));
            deathPool.SpawnDeathRupture(new Vector3(1.5f, 1f, 2.8f), Vector3.forward, frontResidual.Archetype.SecondaryColor, 1f);

            weapon.UpdateAim(0.2f);
            weapon.StepWeapon(0.20f);
            weapon.StepWeapon(0.20f);
            flashlightPresentation.StepPresentation(0.01f);
            frontResidual.StepVisuals(0.01f);
            flankResidual.StepVisuals(0.01f);
            deathPool.StepPool(0.04f);

            yield return null;

            mainCamera.transform.position = new Vector3(0f, 8.8f, -2.8f);
            mainCamera.transform.LookAt(new Vector3(0.2f, 0.6f, 2.3f));

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "EnemyResidual");
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

            Assert.IsTrue(File.Exists(previewPath), "Debe generarse la captura visual del encuentro con Residual.");
        }

        private float SimulateEncounterUnderCondition(PresentationCondition condition, out int spawnedDeathFragments)
        {
            enemyPool.DespawnAllActive();
            deathPool.ClearAll();

            session.Effects.ApplyCondition(condition);
            session.ResetSession(9001);
            playerStats.ResetToInitial();
            playerStats.SetBonusMitigation(0f, 0f, 0f, critChance: 0.35f);
            playerObject.transform.position = Vector3.zero;
            weapon.ResetWeaponCycle();

            EnemyActor r1 = enemyPool.Spawn(EnemyArchetypeId.Residual, new Vector3(0f, 1f, 3.0f));
            EnemyActor r2 = enemyPool.Spawn(EnemyArchetypeId.Residual, new Vector3(0.5f, 1f, 4.2f));
            Physics.SyncTransforms();

            // Simular 4 pulsos de linterna (0.80 s) con pasos de 0.20 s
            for (int i = 0; i < 4; i++)
            {
                weapon.UpdateAim(0.20f);
                weapon.StepWeapon(0.20f);
                flashlightPresentation.StepPresentation(0.02f);
                if (r1.IsAlive)
                    r1.StepVisuals(0.02f);
                if (r2.IsAlive)
                    r2.StepVisuals(0.02f);
            }

            spawnedDeathFragments = deathPool.LastSpawnedFragmentCount;
            return r1.CurrentHealth + r2.CurrentHealth;
        }

        private static float AveragePairwiseDistanceXZ(params EnemyActor[] actors)
        {
            float sum = 0f;
            int pairs = 0;
            for (int i = 0; i < actors.Length; i++)
            {
                for (int j = i + 1; j < actors.Length; j++)
                {
                    Vector3 a = actors[i].transform.position;
                    Vector3 b = actors[j].transform.position;
                    a.y = 0f;
                    b.y = 0f;
                    sum += Vector3.Distance(a, b);
                    pairs++;
                }
            }
            return pairs > 0 ? sum / pairs : 0f;
        }
    }
}
