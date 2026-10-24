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
    public sealed class CombatArenaRunFlowTests
    {
        private CombatGameBootstrap game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded("CombatArena"), "Generate CombatArena with CombatArenaBuilder.Rebuild before running this fixture.");
            yield return SceneManager.LoadSceneAsync("CombatArena", LoadSceneMode.Single);
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            Assert.IsNotNull(game);
            Assert.IsTrue(game.IsInitialized);
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            game.ResetRun(false);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (game != null)
            {
                Scene arena = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("CombatArenaCleanup"));
                yield return SceneManager.UnloadSceneAsync(arena);
            }
            CombatTargetRegistry.Clear();
        }

        [Test]
        public void PlayableScene_ComposesOneSession_PlayerWeaponPoolsDraftDirectorBossAndPhysicalArena()
        {
            Assert.AreEqual(1, Object.FindObjectsByType<CombatSession>(FindObjectsSortMode.None).Length);
#if UNITY_EDITOR
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded("LocomotionSandbox"));
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded("CombatSandbox"));
#endif
            Assert.IsNotNull(game.Player);
            Assert.IsNotNull(game.Stats);
            Assert.IsNotNull(game.Weapon);
            Assert.IsNotNull(game.Pickups);
            Assert.IsNotNull(game.Hazards);
            Assert.IsNotNull(game.MothExplosions);
            Assert.IsNotNull(game.Progression);
            Assert.IsNotNull(game.Draft);
            Assert.IsNotNull(game.Encounter);
            Assert.IsNotNull(game.WaveSettings, "The playable scene must retain an authored wave settings asset.");
            Assert.AreSame(game.WaveSettings, game.Director.Settings);
            Assert.IsNotNull(game.Hud.CanvasRoot);
            Assert.That(game.Enemies.TotalCreatedCount, Is.GreaterThanOrEqualTo(150));
            Assert.AreEqual(0, game.Director.ActiveEnemyCount);
            Assert.AreEqual(60f, game.GameplayCamera.fieldOfView, 0.01f);
            Transform arena = GameObject.Find("Arena_120m").transform;
            Assert.AreEqual(new Vector3(120f, 1f, 120f), arena.Find("Floor").localScale);
            foreach (string wall in new[] { "Wall_North", "Wall_South", "Wall_East", "Wall_West" })
                Assert.IsTrue(arena.Find(wall).GetComponent<Collider>().enabled);
        }

        [Test]
        public void InspectorBalanceAsset_DrivesHealthWeaponDashAndSurvivesRunReset()
        {
            PlayerCombatSettings original = game.Stats.Settings;
            var edited = Object.Instantiate(original);
            try
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(PlayerCombatSettings).GetField("maxHealth", flags).SetValue(edited, 137f);
                typeof(PlayerCombatSettings).GetField("flashlightRange", flags).SetValue(edited, 9f);
                typeof(PlayerCombatSettings).GetField("damagePerPulse", flags).SetValue(edited, 8.25f);
                typeof(PlayerCombatSettings).GetField("dashSpeed", flags).SetValue(edited, 31f);
                game.Stats.Settings = edited;
                game.ResetRun(false);
                Assert.AreEqual(137f, game.Player.CurrentHealth);
                Assert.AreEqual(9f, game.Weapon.ActiveBurstRange);
                Assert.AreEqual(8.25f, game.Weapon.ActiveDamagePerPulse);
                Assert.AreEqual(31f, game.Motor.EffectiveDashSpeed);
                Assert.AreSame(edited, game.Stats.Settings, "Restarting must preserve the assigned balance configuration.");
                Assert.IsFalse(game.Motor.transform.GetComponent<FlashlightPresentation>().ConeRenderer.enabled);
            }
            finally
            {
                game.Stats.Settings = original;
                game.ResetRun(false);
                Object.DestroyImmediate(edited);
            }
        }

        [Test]
        public void RuntimeEffects_StayGroupedAndDoNotMultiplyOnRestart()
        {
            Transform effects = game.transform.Find("RuntimeEffects");
            Assert.IsNotNull(effects);
            Assert.IsNotNull(effects.Find("DashPresentationPool"));
            Assert.IsNotNull(effects.Find("FlashlightHitSparkPool"));
            var moth = game.Enemies.Spawn(EnemyArchetypeId.ExplosiveMoth, new Vector3(10f, 1f, 0f));
            var colossus = game.Enemies.Spawn(EnemyArchetypeId.Colossus, new Vector3(-10f, 1f, 0f));
            int children = game.Enemies.transform.childCount;
            int effectChildren = effects.childCount;
            int rootCount = game.gameObject.scene.rootCount;
            for (int index = 0; index < 10; index++) game.ResetRun(false);
            Assert.AreEqual(children, game.Enemies.transform.childCount);
            Assert.AreEqual(effectChildren, effects.childCount);
            Assert.AreEqual(rootCount, game.gameObject.scene.rootCount);
            foreach (GameObject root in game.gameObject.scene.GetRootGameObjects())
            {
                Assert.AreNotEqual("VFX_ExplosionTelegraph", root.name);
                Assert.AreNotEqual("Colossus charge ground effects", root.name);
                Assert.AreNotEqual("CombatEventSystem", root.name);
            }
            Assert.IsFalse(moth.gameObject.activeSelf);
            Assert.IsFalse(colossus.gameObject.activeSelf);
        }

        [Test]
        public void Hud_ShowsImmediateDamageAndRecentBar_AndTracksCardsAndQueuedDraftWithoutExtraPausePanel()
        {
            game.Player.TakeContactOrHazardDamage(20f, Vector3.right * 2f);
            game.Hud.RefreshNow();
            Assert.AreEqual("80 / 100", game.Hud.DisplayedHealth);
            Assert.AreEqual(0.8f, game.Hud.ImmediateHealthFill.fillAmount, 0.001f);
            Assert.AreEqual(1f, game.Hud.RecentHealthFill.fillAmount, 0.001f);
            game.Player.Heal(10f);
            game.Hud.RefreshNow();
            Assert.AreEqual("90 / 100", game.Hud.DisplayedHealth);
            game.Progression.AddExperience(100);
            Assert.IsTrue(game.Draft.IsOpen);
            game.Hud.RefreshNow();
            Assert.IsFalse(game.Hud.IsPauseVisible);
            Assert.IsTrue(game.DraftPresentation.CanvasRoot.activeSelf);
            string acquired = game.Draft.Offers[0].Card.Name;
            Assert.IsTrue(game.Draft.TryChoose(0));
            game.Hud.RefreshNow();
            StringAssert.Contains(acquired, game.Hud.DisplayedCards);
            Assert.IsFalse(game.Draft.IsOpen);
            Assert.IsFalse(game.Session.IsPaused);
        }

        [Test]
        public void PauseMenu_ChangesEffectsAndResumesBothUserPauseReasonsWhileKeepingPreferences()
        {
            game.Session.SetManualPause(true);
            game.Hud.RefreshNow();
            Assert.IsTrue(game.Hud.IsPauseVisible);
            game.Hud.SetShakePercent(35f);
            game.Hud.SetDecorativePercent(60f);
            game.Hud.SetReducedEffects(true);
            Assert.AreEqual(0.35f, game.Session.Effects.RawShakeIntensity, 0.001f);
            Assert.AreEqual(0.60f, game.Session.Effects.RawDecorativeIntensity, 0.001f);
            Assert.AreEqual(0f, game.Session.Effects.ShakeIntensity);
            Assert.AreEqual(0f, game.Session.Effects.DecorativeIntensity);
            game.Session.SimulateFocusChange(false);
            game.Hud.ResumeGame();
            Assert.IsFalse(game.Session.IsPaused);
            Assert.IsFalse(game.Hud.IsPauseVisible);
            Assert.AreEqual(1f, Time.timeScale);
            game.ResetRun();
            Assert.IsTrue(game.Session.Effects.ReducedEffectsMode, "Preferences survive a new run.");
            Assert.AreEqual(0.35f, game.Session.Effects.RawShakeIntensity, 0.001f);
        }

        [Test]
        public void VictoryAndDefeat_DisplaySummaryAndRestartTheSameArena()
        {
            var bossDeath = new ActorDeathEvent(999, 99, false, true, Vector3.up, Vector3.forward, DamageSourceType.FlashlightPulse, 500, false, true);
            game.Session.QueueBossDeath(in bossDeath);
            game.Session.ResolveSimulationStepOutcome();
            game.Hud.RefreshNow();
            Assert.IsTrue(game.Hud.IsResultVisible);
            StringAssert.Contains("MataColeccionista ha caído", game.Hud.DisplayedSummary);
            StringAssert.Contains("XP OBTENIDA", game.Hud.DisplayedSummary);
            game.Hud.RestartGame();
            Assert.AreEqual(SessionState.Running, game.Session.State);
            Assert.IsFalse(game.Hud.IsResultVisible);
            game.Player.TakeContactOrHazardDamage(500f, Vector3.back);
            game.Session.ResolveSimulationStepOutcome();
            game.Hud.RefreshNow();
            Assert.IsTrue(game.Hud.IsResultVisible);
            StringAssert.Contains("integridad llegó a cero", game.Hud.DisplayedSummary);
            Assert.IsFalse(game.Weapon.enabled);
            game.Hud.RestartGame();
            Assert.IsTrue(game.Player.IsAlive);
            Assert.IsTrue(game.Weapon.enabled);
            Assert.AreEqual(100f, game.Player.CurrentHealth);
        }

        [Test]
        public void TenRestarts_ResetBossPoolsPickupsStatsAndWeapon_WithoutCreatingMoreSceneObjects()
        {
            game.Session.Clock.AdvanceGameplayTime(180f); game.Director.Step();
            game.Encounter.Step(1.5f);
            Assert.IsNotNull(game.Encounter.Boss);
            game.ResetRun(false); // Warm the boss actor and its mandatory presentation once.
            int initialObjects = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int initialCreated = game.Enemies.TotalCreatedCount;
            int initialResets = game.RunResetCount;
            for (int iteration = 0; iteration < 10; iteration++)
            {
                game.Enemies.Spawn(EnemyArchetypeId.Residual, new Vector3(4f, 1f, 4f));
                game.Pickups.SpawnExperience(new Vector3(25f, 0f, 0f), 10);
                game.Pickups.SpawnMedkit(new Vector3(25f, 0f, 5f));
                game.Stats.SetUpgradeTier("FLASH_01", 1);
                game.Session.Clock.AdvanceGameplayTime(180f); game.Director.Step();
                game.Hazards.SpawnPuddle(1, new Vector3(20f, 0f, 0f));
                game.Weapon.enabled = false;
                game.Session.SetManualPause(true);
                game.Hud.RestartGame();
                Assert.AreEqual(SessionState.Running, game.Session.State);
                Assert.IsFalse(game.Session.IsPaused);
                Assert.AreEqual(0, game.Director.ActiveEnemyCount);
                Assert.AreEqual(0, game.Hazards.ActivePuddleCount);
                Assert.AreEqual(0, game.Hazards.ActiveProjectileCount);
                Assert.AreEqual(0, game.MothExplosions.PendingCount);
                Assert.AreEqual(0, game.Pickups.ActivePickupCount);
                Assert.AreEqual(0, game.Stats.GetUpgradeTier("FLASH_01"));
                Assert.AreEqual(1, game.Progression.Level);
                Assert.AreEqual(0, game.Progression.TotalXp);
                Assert.IsFalse(game.Draft.IsOpen);
                Assert.IsFalse(game.Director.IsBossPhase);
                Assert.IsFalse(game.Encounter.IsEncounterActive);
                Assert.IsTrue(game.Weapon.enabled);
                Assert.AreEqual(0, game.Weapon.TotalPulsesEmitted);
                Assert.AreEqual(game.SpawnPosition, game.Player.transform.position);
                Assert.AreEqual(initialResets + iteration + 1, game.RunResetCount);
                Assert.AreEqual(initialCreated, game.Enemies.TotalCreatedCount);
                Assert.AreEqual(initialObjects, Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            }
        }

        [UnityTest]
        public IEnumerator SpawnSamplingUsesStableCinemachinePose_RegardlessOfDecorativeShake()
        {
            yield return null;
            game.Session.ResetSession(20261022);
            game.Session.Effects.ApplyCondition(PresentationCondition.FullEffects);
            var feedback = game.CameraRig.GetComponent<CombatCameraFeedback>();
            feedback.RequestShake(ShakePriority.Medium, 0.35f, 1f, 13f);
            yield return null;
            Assert.IsTrue(game.Director.TryFindSpawnPosition(EnemyArchetypeId.Colossus, out Vector3 full));
            int samples = game.Session.Rng.Combat.SampleCount;
            game.Session.ResetSession(20261022);
            game.Session.Effects.ApplyCondition(PresentationCondition.ReducedEffects);
            yield return null;
            Assert.IsTrue(game.Director.TryFindSpawnPosition(EnemyArchetypeId.Colossus, out Vector3 reduced));
            Assert.That(Vector3.Distance(full, reduced), Is.LessThan(0.001f));
            Assert.AreEqual(samples, game.Session.Rng.Combat.SampleCount);
        }

        [UnityTest]
        public IEnumerator BossArrow_RemainsVisibleOutsideFrameWithEffectsReduced()
        {
            Vector3 position = new Vector3(50f, 1f, 50f);
            game.Player.transform.position = position;
            game.Player.GetComponent<Rigidbody>().position = position;
            game.CameraRig.PreviousStateIsValid = false;
            game.Session.Clock.AdvanceGameplayTime(180f); game.Director.Step();
            game.Encounter.Step(1.5f);
            game.Session.Effects.ReducedEffectsMode = true;
            yield return null;
            yield return null;
            game.Hud.RefreshNow();
            yield return null;
            Assert.IsTrue(game.Hud.IsBossArrowVisible);
        }

        [UnityTest]
        public IEnumerator HudPreview_CapturesReadable720pAnd1080p_WithAllEightAcquiredCards()
        {
            foreach (UpgradeCardDefinition card in UpgradeCatalog.Cards)
                game.Stats.SetUpgradeTier(card.Id, 1);
            game.Weapon.ResetWeaponCycle();
            game.Player.TakeContactOrHazardDamage(22f, Vector3.right * 2f);
            game.Enemies.Spawn(EnemyArchetypeId.Residual, new Vector3(-3f, 1f, 6f));
            game.Enemies.Spawn(EnemyArchetypeId.Colossus, new Vector3(4f, 1f, 8f));
            game.Enemies.Spawn(EnemyArchetypeId.Mortar, new Vector3(-6f, 1f, 9f));
            game.Pickups.SpawnExperience(new Vector3(2f, 0f, 3f), 10);
            game.Session.SetTacticalDraftPause(true);
            game.Hud.RefreshNow();
            foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080) })
            {
                Camera camera = game.GameplayCamera;
                Canvas canvas = game.Hud.HudCanvas;
                RenderMode previousMode = canvas.renderMode;
                Camera previousCamera = canvas.worldCamera;
                float previousPlaneDistance = canvas.planeDistance;
                var previousTexture = camera.targetTexture;
                var previousActive = RenderTexture.active;
                var renderTexture = new RenderTexture(size.x, size.y, 24);
                Texture2D texture = null;
                string directory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "CombatArena");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"HUD-{size.y}p.png");
                try
                {
                    camera.targetTexture = renderTexture;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 0.5f;
                    Canvas.ForceUpdateCanvases();
                    yield return null;
                    yield return null;
                    yield return null;
                    RenderTexture.active = renderTexture;
                    texture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                    texture.ReadPixels(new Rect(0f, 0f, size.x, size.y), 0, 0);
                    texture.Apply();
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                }
                finally
                {
                    canvas.renderMode = previousMode;
                    canvas.worldCamera = previousCamera;
                    canvas.planeDistance = previousPlaneDistance;
                    camera.targetTexture = previousTexture;
                    RenderTexture.active = previousActive;
                    renderTexture.Release(); Object.Destroy(renderTexture);
                    if (texture != null) Object.Destroy(texture);
                }
                Assert.IsTrue(File.Exists(path));
                Assert.That(new FileInfo(path).Length, Is.GreaterThan(12000));
            }
            game.Session.SetTacticalDraftPause(false);
        }
    }
}
