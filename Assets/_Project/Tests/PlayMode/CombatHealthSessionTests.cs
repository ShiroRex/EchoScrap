using System.Collections;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace EchoScrap.Tests
{
    public sealed class CombatHealthSessionTests
    {
        private Scene testScene;
        private CombatSession session;
        private PlayerHealth playerHealth;
        private PlayerRuntimeStats playerStats;
        private PlayerHurtPresentation hurtPresentation;
        private ActorVisualComposer playerVisuals;
        private CombatDummy dummy;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("LocomotionSandbox", LoadSceneMode.Single);
            testScene = SceneManager.GetActiveScene();

            var motor = Object.FindAnyObjectByType<PlayerMotor>();
            Assert.That(motor, Is.Not.Null);
            GameObject playerGo = motor.gameObject;

            playerVisuals = playerGo.AddComponent<ActorVisualComposer>();
            playerStats = playerGo.AddComponent<PlayerRuntimeStats>();
            playerHealth = playerGo.AddComponent<PlayerHealth>();
            hurtPresentation = playerGo.AddComponent<PlayerHurtPresentation>();

            var sessionGo = new GameObject("CombatSession");
            session = sessionGo.AddComponent<CombatSession>();
            session.ResolvePlayerReferences();
            hurtPresentation.Bind(session, playerHealth);

            var dummyGo = new GameObject("CombatDummy");
            dummyGo.transform.position = new Vector3(0f, 1f, 3.5f);
            var dummyCapsule = dummyGo.AddComponent<CapsuleCollider>();
            dummyCapsule.height = 2f;
            dummyCapsule.radius = 0.5f;
            dummyCapsule.isTrigger = true;

            var visualRoot = new GameObject("Visual");
            visualRoot.transform.SetParent(dummyGo.transform, false);
            var bodyMesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bodyMesh.name = "DummyBody";
            bodyMesh.transform.SetParent(visualRoot.transform, false);
            Object.DestroyImmediate(bodyMesh.GetComponent<Collider>());
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.78f, 0.34f, 0.24f));
            bodyMesh.GetComponent<Renderer>().sharedMaterial = mat;

            var dummyComposer = dummyGo.AddComponent<ActorVisualComposer>();
            dummyComposer.CacheRenderers();

            dummy = dummyGo.AddComponent<CombatDummy>();
            dummy.Configure(session, playerHealth, hp: 30f, enableContact: true, contactDmg: 10f, contactCd: 0.50f);

            var input = playerGo.GetComponent<PlayerInputReader>();
            input.SendMessage("OnApplicationFocus", true);
            session.ResumeAllUserPauses();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            var cleanup = SceneManager.CreateScene("CombatHealthSession Cleanup");
            SceneManager.SetActiveScene(cleanup);
            if (testScene.IsValid() && testScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(testScene);
            yield return null;
        }

        [Test]
        public void InitialPlayerStatsAndMitigationCapsMatchGddContract()
        {
            Assert.That(playerHealth.MaxHealth, Is.EqualTo(100f));
            Assert.That(playerHealth.CurrentHealth, Is.EqualTo(100f));
            Assert.That(playerStats.DamageProtectionDuration, Is.EqualTo(0.40f).Within(0.0001f));
            Assert.That(playerStats.Armor, Is.EqualTo(0f));
            Assert.That(playerStats.Evasion, Is.EqualTo(0f));
            Assert.That(playerStats.BlockChance, Is.EqualTo(0f));
            Assert.That(playerStats.BlockDamageReduction, Is.EqualTo(0.50f).Within(0.0001f));
            Assert.That(playerStats.CritChance, Is.EqualTo(0f));
            Assert.That(playerStats.CritMultiplier, Is.EqualTo(1.75f).Within(0.0001f));

            // Verificar límites máximos: 60% armadura, 35% evasión, 50% bloqueo
            playerStats.SetBonusMitigation(armor: 0.95f, evasion: 0.90f, blockChance: 0.90f);
            Assert.That(playerStats.Armor, Is.EqualTo(0.60f).Within(0.0001f));
            Assert.That(playerStats.Evasion, Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(playerStats.BlockChance, Is.EqualTo(0.50f).Within(0.0001f));
        }

        [Test]
        public void MitigationOrderAndProtectionWindowFollowExactRules()
        {
            // 1. Evasión no inicia ventana de protección ni activa feedback de daño recibido
            // Encontrar una semilla donde el primer roll sea < 0.35
            session.ResetSession(4);
            playerStats.SetBonusMitigation(armor: 0f, evasion: 0.35f, blockChance: 0f);
            float sample = new DeterministicRandom(4).NextFloat();
            if (sample >= 0.35f)
                session.ResetSession(1);

            // Forzar una semilla cuyo primer roll sea menor que 0.35
            int dodgeSeed = 0;
            for (int s = 1; s < 500; s++)
            {
                if (new DeterministicRandom(s).NextFloat() < 0.35f)
                {
                    dodgeSeed = s;
                    break;
                }
            }
            session.ResetSession(dodgeSeed);
            playerStats.SetBonusMitigation(armor: 0f, evasion: 0.35f, blockChance: 0f);

            DamageResult dodgeHit = playerHealth.TakeContactOrHazardDamage(20f, new Vector3(0f, 1f, 3f));
            Assert.That(dodgeHit.WasDodged, Is.True);
            Assert.That(dodgeHit.HealthLost, Is.EqualTo(0f));
            Assert.That(playerHealth.CurrentHealth, Is.EqualTo(100f));
            Assert.That(playerHealth.IsInvulnerable, Is.False, "La evasión no debe iniciar ventana de protección.");
            Assert.That(hurtPresentation.HurtFeedbackTriggerCount, Is.EqualTo(0), "La evasión no debe disparar feedback de daño.");

            // 2. Bloqueo (50% reducción) -> Armadura (50% reducción) -> Pérdida de salud (40 -> 20 -> 10)
            int blockSeed = 0;
            for (int s = 1; s < 500; s++)
            {
                if (new DeterministicRandom(s).NextFloat() < 0.50f)
                {
                    blockSeed = s;
                    break;
                }
            }
            session.ResetSession(blockSeed);
            playerStats.SetBonusMitigation(armor: 0.50f, evasion: 0f, blockChance: 0.50f);

            DamageResult mitigatedHit = playerHealth.TakeContactOrHazardDamage(40f, new Vector3(0f, 1f, 3f));
            Assert.That(mitigatedHit.WasBlocked, Is.True);
            Assert.That(mitigatedHit.ResolvedDamage, Is.EqualTo(10f).Within(0.001f));
            Assert.That(mitigatedHit.HealthLost, Is.EqualTo(10f).Within(0.001f));
            Assert.That(playerHealth.CurrentHealth, Is.EqualTo(90f).Within(0.001f));
            Assert.That(playerHealth.HasDamageProtection, Is.True);
            Assert.That(playerHealth.ProtectionRemaining, Is.EqualTo(0.40f).Within(0.001f));
            Assert.That(hurtPresentation.HurtFeedbackTriggerCount, Is.EqualTo(1));
            Assert.That(hurtPresentation.ScreenBorderAlpha, Is.GreaterThan(0.05f));
            Assert.That(hurtPresentation.DirectionalMarkerAlpha, Is.GreaterThan(0.5f));

            // 3. Golpe durante los 0.40 s de protección es ignorado, sin reiniciar la ventana ni repetir feedback
            playerHealth.StepTimers(0.15f);
            Assert.That(playerHealth.ProtectionRemaining, Is.EqualTo(0.25f).Within(0.005f));

            DamageResult immuneHit = playerHealth.TakeContactOrHazardDamage(35f, new Vector3(3f, 1f, 0f));
            Assert.That(immuneHit.WasImmune, Is.True);
            Assert.That(immuneHit.HealthLost, Is.EqualTo(0f));
            Assert.That(playerHealth.CurrentHealth, Is.EqualTo(90f).Within(0.001f));
            Assert.That(playerHealth.ProtectionRemaining, Is.EqualTo(0.25f).Within(0.005f), "Un golpe inmune no debe extender ni reiniciar la ventana de 0.40 s.");
            Assert.That(hurtPresentation.HurtFeedbackTriggerCount, Is.EqualTo(1), "Un golpe inmune no debe reproducir feedback de daño.");

            // 4. Al vencer los 0.40 s, el jugador vuelve a ser vulnerable
            playerHealth.StepTimers(0.26f);
            Assert.That(playerHealth.IsInvulnerable, Is.False);
        }

        [Test]
        public void HealingClampsToMaxAndSkipsFeedbackWhenFull()
        {
            // A vida llena (100/100), consumir curación no emite confirmación visual de curación
            HealResult fullHeal = playerHealth.Heal(15f);
            Assert.That(fullHeal.WasAlreadyFull, Is.True);
            Assert.That(fullHeal.ActualHealed, Is.EqualTo(0f));
            Assert.That(hurtPresentation.HealFeedbackTriggerCount, Is.EqualTo(0));

            // Con daño previo, cura hasta el máximo y emite confirmación
            playerHealth.TakeContactOrHazardDamage(25f, Vector3.forward);
            Assert.That(playerHealth.CurrentHealth, Is.EqualTo(75f).Within(0.001f));

            HealResult partialHeal = playerHealth.Heal(15f);
            Assert.That(partialHeal.ActualHealed, Is.EqualTo(15f).Within(0.001f));
            Assert.That(playerHealth.CurrentHealth, Is.EqualTo(90f).Within(0.001f));
            Assert.That(hurtPresentation.HealFeedbackTriggerCount, Is.EqualTo(1));

            HealResult cappedHeal = playerHealth.Heal(25f);
            Assert.That(cappedHeal.ActualHealed, Is.EqualTo(10f).Within(0.001f));
            Assert.That(playerHealth.CurrentHealth, Is.EqualTo(100f).Within(0.001f));
            Assert.That(hurtPresentation.HealFeedbackTriggerCount, Is.EqualTo(2));
        }

        [Test]
        public void PauseCoexistencePreservesFrozenStateUntilAllReasonsClear()
        {
            playerHealth.TakeContactOrHazardDamage(15f, Vector3.forward);
            float protectionBefore = playerHealth.ProtectionRemaining;
            float gameplayTimeBefore = session.GameplayTime;

            session.SetManualPause(true);
            session.SetTacticalDraftPause(true);
            session.SimulateFocusChange(false);

            Assert.That(session.IsPaused, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(session.Clock.HasPauseReason(PauseReason.Manual), Is.True);
            Assert.That(session.Clock.HasPauseReason(PauseReason.TacticalDraft), Is.True);
            Assert.That(session.Clock.HasPauseReason(PauseReason.FocusLost), Is.True);

            // Cerrar el draft táctico no debe reanudar la partida mientras sigan activas pausa manual o foco perdido
            session.SetTacticalDraftPause(false);
            Assert.That(session.IsPaused, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f));

            // Recuperar el foco del sistema operativo exige reanudación explícita
            session.SimulateFocusChange(true);
            Assert.That(session.IsPaused, Is.True);
            Assert.That(session.Clock.HasPauseReason(PauseReason.FocusLost), Is.True);

            // Cerrar pausa manual aún mantiene la pausa por foco perdido hasta confirmar reanudación
            session.SetManualPause(false);
            Assert.That(session.IsPaused, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f));

            // Confirmar reanudación explícita limpia la última causa y reactiva Time.timeScale
            session.ResumeFromFocusLoss();
            Assert.That(session.IsPaused, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(playerHealth.ProtectionRemaining, Is.EqualTo(protectionBefore).Within(0.0001f));
            Assert.That(session.GameplayTime, Is.EqualTo(gameplayTimeBefore).Within(0.0001f));
        }

        [Test]
        public void SeparateSeedsIsolateCombatOutcomesFromPresentationCalls()
        {
            session.ResetSession(2026);
            float[] runACrits = new float[16];
            for (int i = 0; i < runACrits.Length; i++)
                runACrits[i] = session.Rng.Combat.NextFloat();

            session.ResetSession(2026);
            // Consumir 1000 muestras de presentación (partículas, shakes, escombros)
            for (int p = 0; p < 1000; p++)
                session.Rng.Presentation.NextFloat();

            for (int i = 0; i < runACrits.Length; i++)
                Assert.That(session.Rng.Combat.NextFloat(), Is.EqualTo(runACrits[i]));
        }

        [Test]
        public void LargeColliderContactAndDummyDeathDisableCollisionsImmediately()
        {
            // Escalar el dummy a 3.5x (escala de Gólgora): radio efectivo ~1.75 m + 0.5 m jugador = 2.25 m
            dummy.transform.localScale = Vector3.one * 3.5f;
            dummy.transform.position = new Vector3(0f, 1f, 2.05f); // Centros a 2.05 m, pero colliders se solapan
            Physics.SyncTransforms();

            bool dealt = dummy.TryDealContactDamage();
            Assert.That(dealt, Is.True, "El contacto debe comprobarse con los colliders reales, no una distancia fija pequeña entre centros.");
            Assert.That(playerHealth.CurrentHealth, Is.EqualTo(90f).Within(0.001f));

            // Dañar al dummy comprueba flash sin instanciar materiales y hit-stop local
            var renderer = dummy.GetComponentInChildren<Renderer>();
            Material sharedBefore = renderer.sharedMaterial;
            var hitReq = new DamageRequest(playerHealth.TargetId, DamageSourceType.FlashlightPulse, Vector3.up, Vector3.forward, 12f, canCrit: true, critChance: 1f, critMultiplier: 1.75f);
            DamageResult hitRes = dummy.ReceiveDamage(in hitReq);

            Assert.That(hitRes.IsCritical, Is.True);
            Assert.That(hitRes.ResolvedDamage, Is.EqualTo(21f).Within(0.001f));
            Assert.That(dummy.VisualComposer.IsFlashActive, Is.True);
            Assert.That(dummy.VisualComposer.IsHitStopActive, Is.True);
            Assert.That(dummy.VisualComposer.HitStopRemaining, Is.EqualTo(0.05f).Within(0.001f));
            Assert.That(renderer.sharedMaterial, Is.SameAs(sharedBefore), "El flash debe usar MaterialPropertyBlock sin instanciar materiales.");

            // Daño letal desactiva inmediatamente colisión, selección y daño, mientras continúa su residuo visual
            DamageResult killRes = dummy.ReceiveDamage(in hitReq);
            Assert.That(killRes.CausedDeath, Is.True);
            Assert.That(dummy.IsAlive, Is.False);
            Assert.That(dummy.BodyCollider.enabled, Is.False, "El collider debe desactivarse inmediatamente al morir.");
            Assert.That(dummy.IsPlayingDeathVisual, Is.True, "El residuo visual breve puede continuar sin colisión.");
            Assert.That(dummy.DeathNotifyCount, Is.EqualTo(1), "La muerte y recompensa deben resolverse una sola vez.");

            // Intentar dañarlo o recibir contacto muerto no surte efecto
            playerHealth.ResetForNewSession();
            Assert.That(dummy.TryDealContactDamage(), Is.False);
            DamageResult postDeathHit = dummy.ReceiveDamage(in hitReq);
            Assert.That(postDeathHit.WasImmune, Is.True);
            Assert.That(dummy.DeathNotifyCount, Is.EqualTo(1));
        }

        [Test]
        public void SimultaneousPlayerAndBossDeathResolvesToDefeat()
        {
            dummy.Configure(session, playerHealth, hp: 10f, enableContact: false, asBossDummy: true);

            // En el mismo paso de simulación, muere el jefe y muere el jugador
            var killBoss = new DamageRequest(playerHealth.TargetId, DamageSourceType.FlashlightPulse, Vector3.up, Vector3.forward, 50f);
            dummy.ReceiveDamage(in killBoss);
            playerHealth.TakeContactOrHazardDamage(250f, dummy.transform.position, dummy.TargetId, DamageSourceType.BossStomp);

            // Resolver el paso de simulación: debe prevalecer la derrota
            session.ResolveSimulationStepOutcome();
            Assert.That(session.State, Is.EqualTo(SessionState.Defeat));
        }

        [UnityTest]
        public IEnumerator PreviewCapturesPlayerHurtAndDummyHitFeedback()
        {
            // Crear un overlay de prueba para verificar visualmente las barras y el borde suave
            var canvasGo = new GameObject("HurtPreviewCanvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            Image border = CreateUiBox(canvasGo.transform, "BorderOverlay", Vector2.zero, new Vector2(1600, 900), new Color(0.85f, 0.1f, 0.1f, 0f));
            Image barBg = CreateUiBox(canvasGo.transform, "HealthBarBg", new Vector2(-600, 380), new Vector2(280, 22), new Color(0.1f, 0.12f, 0.15f, 0.9f));
            Image recentFill = CreateUiBox(barBg.transform, "RecentFill", Vector2.zero, new Vector2(280, 22), new Color(0.95f, 0.58f, 0.2f, 1f), filled: true);
            Image hpFill = CreateUiBox(barBg.transform, "HpFill", Vector2.zero, new Vector2(280, 22), new Color(0.35f, 0.85f, 0.45f, 1f), filled: true);
            Image dirImg = CreateUiBox(canvasGo.transform, "DirectionalMark", new Vector2(0, 60), new Vector2(36, 14), new Color(1f, 0.35f, 0.18f, 1f));
            hurtPresentation.AssignUiReferences(hpFill, recentFill, border, dirImg.rectTransform, dirImg);

            dummy.transform.position = new Vector3(1.8f, 1f, 1.8f);
            var hitDummy = new DamageRequest(playerHealth.TargetId, DamageSourceType.FlashlightPulse, Vector3.up, new Vector3(1f, 0f, 1f).normalized, 8f);
            dummy.ReceiveDamage(in hitDummy);
            playerHealth.TakeContactOrHazardDamage(25f, dummy.transform.position);
            hurtPresentation.StepPresentation(0.02f);

            Assert.That(hurtPresentation.RecentDamageRatio, Is.GreaterThan(hurtPresentation.ImmediateHealthRatio));

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
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                image.Apply();
                Directory.CreateDirectory("Logs/CombatHealthSession");
                File.WriteAllBytes("Logs/CombatHealthSession/Preview.png", image.EncodeToPNG());
                Assert.That(File.Exists("Logs/CombatHealthSession/Preview.png"), Is.True);
            }
            finally
            {
                if (camera != null)
                    camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                texture.Release();
                Object.Destroy(texture);
                Object.Destroy(image);
                Object.Destroy(canvasGo);
            }
        }

        private static Image CreateUiBox(Transform parent, string name, Vector2 anchoredPos, Vector2 size, Color color, bool filled = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = color;
            if (filled)
            {
                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Horizontal;
                img.fillAmount = 1f;
            }
            return img;
        }
    }
}
