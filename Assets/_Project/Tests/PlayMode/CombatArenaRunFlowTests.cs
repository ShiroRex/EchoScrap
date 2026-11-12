using System.Collections;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace EchoScrap.Tests
{
    public sealed class CombatArenaRunFlowTests
    {
        private CombatGameBootstrap game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded("CombatArena"), "Include the saved CombatArena scene in Build Settings before running this fixture.");
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
        public void SelectedRoundsStartBossAtTheirOwnConfiguredTime()
        {
            var entry = Resources.Load<ExpeditionEntrySettings>("UI/ExpeditionEntrySettings");
            Assert.That(entry, Is.Not.Null);
            foreach (var variant in new[] { (entry.CompleteRound, 480f), (entry.DemoRound, 180f) })
            {
                Assert.That(variant.Item1, Is.Not.Null);
                game.SelectWaveSettings(variant.Item1);
                Assert.That(game.IsInitialized, Is.True);
                var station = Object.FindAnyObjectByType<FusionStation>();
                Assert.That(station, Is.Not.Null, "Both duration variants retain the latest fusion station.");
                station.PersistProgress = false;
                Assert.That(station.gameObject.scene, Is.EqualTo(game.gameObject.scene));
                Assert.That(station.gameObject.scene.name, Is.EqualTo("CombatArena"));
                Assert.That(station.DisplaySlots, Has.Length.EqualTo(10), "Both rounds retain the expanded vault.");
                Assert.That(station.Door, Is.Not.Null);
                Assert.That(station.InteractionPoint, Is.Not.Null);
                Assert.That(station.ResultPoint, Is.Not.Null);
                game.Director.AutoTick = false;
                game.Encounter.AutoTick = false;
                game.ResetRun(false);
                Assert.That(game.WaveSettings.BossStartTime, Is.EqualTo(variant.Item2));
                Assert.That(game.WaveSettings.name, Is.EqualTo(variant.Item2 == 480f ? "CombatWaves_8Min" : "CombatWaves_3Min"));
                game.Session.Clock.SetGameplayTimeForTesting(variant.Item2 - .1f);
                game.Director.Step();
                Assert.That(game.Director.IsBossPhase, Is.False);
                game.Session.Clock.SetGameplayTimeForTesting(variant.Item2);
                game.Director.Step();
                Assert.That(game.Director.IsBossPhase, Is.True);
                Assert.That(game.Encounter.Boss, Is.Not.Null);
                Assert.That(game.Session.State, Is.EqualTo(SessionState.BossIntro));
                game.ResetRun(false);
                Assert.That(game.WaveSettings.BossStartTime, Is.EqualTo(variant.Item2));
                Assert.That(game.Director.IsBossPhase, Is.False);
            }
        }

        [Test]
        public void PlayableScene_ComposesOneSession_PlayerWeaponPoolsDraftDirectorBossAndPhysicalArena()
        {
            Assert.AreEqual(1, Object.FindObjectsByType<CombatSession>(FindObjectsSortMode.None).Length);
#if UNITY_EDITOR
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded("MainMenu"));
            Assert.IsFalse(Application.CanStreamedLevelBeLoaded("LocomotionSandbox"));
            Assert.IsFalse(Application.CanStreamedLevelBeLoaded("CombatSandbox"));
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
            Assert.AreEqual(game.Lighting.Profile.CameraFieldOfView, game.GameplayCamera.fieldOfView, 0.01f);
            Transform arena = GameObject.Find("Arena_120m").transform;
            Assert.AreEqual(new Vector3(200f, 1f, 200f), arena.Find("Floor").localScale);
            // Se conservan las comprobaciones de los muros de nuestra rama.
            foreach (string wall in new[] { "Wall_North", "Wall_South", "Wall_East", "Wall_West" })
            {
                Transform wallTransform = arena.Find(wall);
                Assert.IsNotNull(wallTransform, "No se encontro el muro: " + wall);
                Collider wallCollider = wallTransform.GetComponent<Collider>();
                Assert.IsNotNull(wallCollider, "El muro no tiene Collider: " + wall);
                Assert.IsTrue(wallCollider.enabled, "El Collider del muro esta desactivado: " + wall);
            }

            // Se conservan las comprobaciones nuevas de develop para la arena de 200 m.
            Assert.AreEqual(new Vector2(100f, 100f), game.WaveSettings.ArenaHalfExtents);
            foreach (string edge in new[] { "North", "South", "East", "West" })
            {
                Transform border = arena.Find("ArenaBounds/Border_" + edge);
                Assert.IsNotNull(border, "No se encontro el limite: Border_" + edge);
                BoxCollider borderCollider = border.GetComponent<BoxCollider>();
                Assert.IsNotNull(borderCollider, "El limite no tiene BoxCollider: Border_" + edge);
                Assert.IsTrue(borderCollider.enabled, "El Collider del limite esta desactivado: Border_" + edge);
            }
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
            var slider = game.Hud.CanvasRoot.transform
                .Find("PauseAndResultOverlay/Panel/Pause/General/EffectsSlider").GetComponent<UnityEngine.UI.Slider>();
            slider.value = 99f;
            Assert.IsTrue(game.Session.Effects.ReducedEffectsMode);
            Assert.IsTrue(slider.interactable);
            slider.value = 100f;
            Assert.IsFalse(game.Session.Effects.ReducedEffectsMode);
            Assert.AreEqual(1f, game.Session.Effects.DecorativeIntensity);
            game.Hud.SetDecorativePercent(99.9f);
            Assert.IsFalse(game.Session.Effects.ReducedEffectsMode);
            game.Hud.SetShakePercent(35f);
            slider.value = 60f;
            Assert.IsTrue(game.Session.Effects.ReducedEffectsMode);
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

        [UnityTest]
        public IEnumerator VictoryAndDefeat_DisplaySummaryAndRestartTheSameArena()
        {
            game.Session.Effects.ReducedEffectsMode = false;
            game.Session.Effects.DecorativeIntensity = 1f;
            var overlay = game.Hud.CanvasRoot.transform.Find("PauseAndResultOverlay");
            var confetti = overlay.Find("VictoryConfetti").gameObject;
            var pulse = overlay.Find("DefeatPulse").gameObject;
            game.Session.Clock.SetGameplayTimeForTesting(193f);
            game.Progression.AddExperience(87);
            var bossDeath = new ActorDeathEvent(999, 99, false, true, Vector3.up, Vector3.forward, DamageSourceType.FlashlightPulse, 500, false, true);
            game.Session.QueueBossDeath(in bossDeath);
            game.Session.ResolveSimulationStepOutcome();
            game.Hud.RefreshNow();
            Assert.IsTrue(game.Hud.IsResultVisible);
            Assert.IsTrue(confetti.activeInHierarchy);
            Assert.IsNotNull(confetti.GetComponent<CanvasRenderer>());
            Assert.IsFalse(pulse.activeSelf);
            StringAssert.Contains("Gólgora ha caído", game.Hud.DisplayedSummary);
            StringAssert.Contains("TIEMPO: 00:00", game.Hud.DisplayedSummary);
            StringAssert.Contains("XP OBTENIDA: 0", game.Hud.DisplayedSummary);
            Assert.AreEqual(0f, Time.timeScale);

            yield return new WaitForSecondsRealtime(1.05f);
            game.Hud.RefreshNow();
            StringAssert.Contains("TIEMPO: 03:13", game.Hud.DisplayedSummary);
            StringAssert.Contains($"BAJAS: {game.Hud.KillCount}\n", game.Hud.DisplayedSummary);
            StringAssert.EndsWith($"XP OBTENIDA: {87 + game.Encounter.ClosingXpReward}", game.Hud.DisplayedSummary);

            game.Hud.RestartGame();
            Assert.AreEqual(SessionState.Running, game.Session.State);
            Assert.IsFalse(game.Hud.IsResultVisible);
            Assert.IsFalse(confetti.activeSelf);
            game.Session.Effects.ReducedEffectsMode = true;
            game.Session.Clock.SetGameplayTimeForTesting(23f);
            game.Progression.AddExperience(7);
            game.Player.TakeContactOrHazardDamage(500f, Vector3.back);
            game.Session.ResolveSimulationStepOutcome();
            game.Hud.RefreshNow();
            Assert.IsTrue(game.Hud.IsResultVisible);
            Assert.IsFalse(confetti.activeSelf);
            Assert.IsFalse(pulse.activeSelf);
            StringAssert.Contains("integridad llegó a cero", game.Hud.DisplayedSummary);
            StringAssert.Contains("TIEMPO: 00:23", game.Hud.DisplayedSummary);
            StringAssert.EndsWith("XP OBTENIDA: 7", game.Hud.DisplayedSummary);
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
            game.Session.Clock.SetGameplayTimeForTesting(game.WaveSettings.BossStartTime);
            game.Director.Step();
            var boss = game.Encounter.Boss; boss.AutoTick = false;
            game.Encounter.Step(.31f);
            game.Encounter.Step(.31f);
            game.Encounter.Step(BossEncounter.IntroDuration);
            game.Session.Effects.ReducedEffectsMode = true;
            yield return null;
            yield return null;

            // The foundry now transfers the player and fits both actors in view. Force an
            // actual offscreen projection to exercise the HUD fallback independently.
            Camera camera = game.GameplayCamera;
            var brain = camera.GetComponent<CinemachineBrain>();
            Assert.IsNotNull(brain);
            bool wasEnabled = brain.enabled;
            Quaternion originalRotation = camera.transform.rotation;
            try
            {
                brain.enabled = false;
                Vector3 awayFromBoss = camera.transform.position - boss.WorldCenter;
                camera.transform.rotation = Quaternion.LookRotation(awayFromBoss, Vector3.up);
                Assert.Less(camera.WorldToViewportPoint(boss.WorldCenter).z, 0f, "The test must really place the boss behind the camera.");
                game.Hud.RefreshNow();
                yield return null;
                yield return null;
                Assert.IsTrue(game.Hud.IsBossArrowVisible);
            }
            finally
            {
                camera.transform.rotation = originalRotation;
                brain.enabled = wasEnabled;
                game.CameraRig.PreviousStateIsValid = false;
            }
        }

        [UnityTest]
        public IEnumerator ImportedHudUsesOriginalFramesAndPortrait_AndFreezesAnimationWhenPaused()
        {
            var root=game.Hud.CanvasRoot.transform;
            var energy=root.Find("Dash").GetComponent<HudBarArtwork>();
            Assert.AreEqual(5,energy.FrameCount);
            Assert.IsNotNull(root.Find("Integrity/Portrait").GetComponent<UnityEngine.UI.Image>().sprite);
            game.Session.ResumeFromFocusLoss();
            var seen=new System.Collections.Generic.HashSet<int>();
            for(int i=0;i<30;i++) { seen.Add(energy.FrameIndex); yield return new WaitForSeconds(.04f); }
            Assert.Greater(seen.Count,1,"The supplied animation must play.");
            game.Session.SetManualPause(true);
            int held=energy.FrameIndex;
            yield return new WaitForSecondsRealtime(.3f);
            Assert.AreEqual(held,energy.FrameIndex);
            game.Session.SetManualPause(false);
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

        [UnityTest]
        public IEnumerator PauseMenu_DeliveredArtPreservesFrameAndColoredButtons_WithWorkingResumeAndRestart()
        {
            PauseMenuSkin skin = Resources.Load<PauseMenuSkin>("UI/PauseMenuSkin");
            Assert.IsNotNull(skin);
            Assert.That(skin.frame.rect.size, Is.EqualTo(new Vector2(113, 87)));
            Sprite blueButton = PixelControlArtwork.Load("Presentation/ButtonBlue");
            Sprite redButton = PixelControlArtwork.Load("Presentation/ButtonRed");
            foreach (Sprite sprite in new[] { skin.frame, blueButton, redButton, skin.greenButton })
                Assert.That(sprite.texture.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(skin.greenButton.border, Is.EqualTo(Vector4.one * 14f));
            game.Progression.AddExperience(20);
            game.Session.SetManualPause(true);
            game.Hud.RefreshNow();
            Transform pause = game.Hud.CanvasRoot.transform.Find("PauseAndResultOverlay/Panel/Pause");
            Image frame = pause.Find("Artwork").GetComponent<Image>();
            Button resume = pause.Find("Resume").GetComponent<Button>();
            Button restart = pause.Find("Restart").GetComponent<Button>();
            Assert.That(frame.sprite, Is.SameAs(skin.frame));
            Assert.That(frame.type, Is.EqualTo(Image.Type.Sliced), "The integrated pause frame stretches its center, not its corners.");
            Assert.That(frame.sprite.border.x, Is.GreaterThan(0));
            Assert.That(frame.sprite.border.y, Is.GreaterThan(0));
            Assert.That(frame.rectTransform.sizeDelta, Is.EqualTo(((RectTransform)pause).sizeDelta));
            foreach (string ornament in new[] { "TopCrest", "BottomCrest", "SideOrnament" })
            {
                Image decoration = pause.Find(ornament).GetComponent<Image>();
                Assert.AreEqual(decoration.sprite.rect.width / decoration.sprite.rect.height,
                    decoration.rectTransform.rect.width / decoration.rectTransform.rect.height, .001f,
                    ornament + " must keep the original art proportions.");
            }
            Assert.That(resume.image.sprite, Is.SameAs(blueButton));
            Assert.That(restart.image.sprite, Is.SameAs(redButton));
            Assert.That(restart.transform.localPosition.x, Is.LessThan(resume.transform.localPosition.x));
            EventSystem.current.SetSelectedGameObject(null);
            yield return null;
            foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1080) })
                yield return CapturePauseArtwork(size, "Pause");
            EventSystem.current.SetSelectedGameObject(restart.gameObject);
            yield return null;
            Assert.That(restart.image.overrideSprite, Is.SameAs(blueButton));
            Assert.That(resume.image.overrideSprite, Is.SameAs(blueButton));
            yield return CapturePauseArtwork(new Vector2Int(1920, 1080), "Pause_Focus");
            ExecuteEvents.Execute(restart.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            Assert.That(game.Progression.TotalXp, Is.Zero, "The red navigation button still restarts the run.");
            Assert.That(game.Session.IsPaused, Is.False);
            game.Session.SetManualPause(true); game.Hud.RefreshNow();
            EventSystem.current.SetSelectedGameObject(resume.gameObject);
            ExecuteEvents.Execute(resume.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Assert.That(game.Session.IsPaused, Is.False, "Keyboard/gamepad submission still resumes the run.");
            Assert.That(game.Hud.IsPauseVisible, Is.False);
        }

        private IEnumerator CapturePauseArtwork(Vector2Int size, string name)
        {
            Camera camera = game.GameplayCamera;
            var cameraData = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            bool postProcessing = cameraData != null && cameraData.renderPostProcessing;
            var antialiasing = cameraData != null ? cameraData.antialiasing : UnityEngine.Rendering.Universal.AntialiasingMode.None;
            Canvas canvas = game.Hud.HudCanvas;
            RenderMode mode = canvas.renderMode; Camera previousCamera = canvas.worldCamera;
            float plane = canvas.planeDistance;
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            var target = new RenderTexture(size.x, size.y, 24);
            Texture2D texture = null;
            try
            {
                // Runtime Overlay UI renders after world effects. Keep those effects off the UI in this camera capture too.
                if (cameraData != null)
                {
                    cameraData.renderPostProcessing = false;
                    cameraData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
                }
                camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 0.5f;
                Canvas.ForceUpdateCanvases();
                yield return null; yield return null; yield return null;
                Transform pause = canvas.transform.Find("PauseAndResultOverlay/Panel/Pause");
                foreach (Text text in pause.GetComponentsInChildren<Text>())
                    Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 1f), "Pause text must fit: " + text.name);
                RenderTexture.active = target;
                texture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); texture.Apply();
                string output = Path.Combine(Application.dataPath, "..", "Logs", "PauseMenuArt"); Directory.CreateDirectory(output);
                File.WriteAllBytes(Path.Combine(output, $"{name}_{size.x}x{size.y}.png"), texture.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = mode; canvas.worldCamera = previousCamera; canvas.planeDistance = plane;
                if (cameraData != null) { cameraData.renderPostProcessing = postProcessing; cameraData.antialiasing = antialiasing; }
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                target.Release(); Object.Destroy(target);
                if (texture != null) Object.Destroy(texture);
            }
        }
    }
}
