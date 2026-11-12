using System;
using System.Collections;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EchoScrap.Tests
{
    public sealed class PlayerStatsReadoutTests
    {
        private CombatGameBootstrap game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CombatArena", LoadSceneMode.Single);
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = game.Encounter.AutoTick = game.Upgrades.AutoTick = false;
            game.ResetRun(false);
            game.Weapon.enabled = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (game != null)
            {
                Scene scene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("Stats readout cleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            CombatTargetRegistry.Clear();
        }

        [Test]
        public void PauseSelectorWorksWithPointerAndSubmitWithoutChangingCombatOrEffects()
        {
            Assert.That(game.Hud.StatsMode, Is.EqualTo(StatsDisplayMode.None));
            Assert.That(game.Hud.ShowStats, Is.False);
            Assert.That(game.Hud.IsStatsVisible, Is.False);
            game.Session.SetManualPause(true);
            game.Hud.RefreshNow();
            Transform pause = game.Hud.CanvasRoot.transform.Find("PauseAndResultOverlay/Panel/Pause");
            var interfaceTab = pause.Find("Tabs/Interface").GetComponent<Button>();
            interfaceTab.onClick.Invoke();
            Transform selector = pause.Find("Interface/StatsDisplayMode");
            var full = selector.Find("Full").GetComponent<Button>();
            var reduced = selector.Find("Reduced").GetComponent<Button>();
            var none = selector.Find("None").GetComponent<Button>();
            var resume = pause.Find("Resume").GetComponent<Button>();
            var powers = pause.Find("Interface/CompanionPowers/Yes").GetComponent<Toggle>();
            int samples = game.Session.Rng.Combat.SampleCount;
            float shake = game.Session.Effects.RawShakeIntensity;
            float decorative = game.Session.Effects.RawDecorativeIntensity;
            bool reducedEffects = game.Session.Effects.ReducedEffectsMode;
            ExecuteEvents.Execute(full.gameObject, new PointerEventData(EventSystem.current)
                { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            Assert.That(game.Hud.StatsMode, Is.EqualTo(StatsDisplayMode.Full));
            Assert.That(game.Hud.ShowStats, Is.True);
            Assert.That(game.Session.IsPaused, Is.True);
            Assert.That(game.Hud.IsStatsVisible, Is.False, "The pause artwork keeps priority over the ingame readout.");
            Assert.That(interfaceTab.FindSelectableOnDown(), Is.SameAs(full));
            Assert.That(full.FindSelectableOnUp(), Is.SameAs(interfaceTab));
            Assert.That(full.FindSelectableOnDown(), Is.SameAs(powers));
            Assert.That(full.FindSelectableOnLeft(), Is.SameAs(none));
            Assert.That(full.FindSelectableOnRight(), Is.SameAs(reduced));
            reduced.onClick.Invoke();
            Assert.That(game.Hud.StatsMode, Is.EqualTo(StatsDisplayMode.Reduced));
            Assert.That(interfaceTab.FindSelectableOnDown(), Is.SameAs(reduced));
            Assert.That(reduced.image.sprite, Is.SameAs(PixelControlArtwork.Load("StatsSelector/Selected")));
            Assert.That(full.image.sprite, Is.SameAs(PixelControlArtwork.Load("StatsSelector/Normal")));
            full.onClick.Invoke();
            game.Hud.ResumeGame();
            Assert.That(game.Hud.IsStatsVisible, Is.True, "Complete statistics include base values.");
            game.Stats.SetUpgradeTier("FLASH_01", 1);
            Assert.That(game.Hud.IsStatsVisible, Is.True);
            game.Session.SetManualPause(true);
            interfaceTab.onClick.Invoke();
            EventSystem.current.SetSelectedGameObject(none.gameObject);
            ExecuteEvents.Execute(none.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Assert.That(game.Hud.StatsMode, Is.EqualTo(StatsDisplayMode.None));
            Assert.That(game.Hud.ShowStats, Is.False);
            Assert.That(game.Session.IsPaused, Is.True);
            Assert.That(game.Session.Rng.Combat.SampleCount, Is.EqualTo(samples));
            Assert.That(game.Session.Effects.RawShakeIntensity, Is.EqualTo(shake));
            Assert.That(game.Session.Effects.RawDecorativeIntensity, Is.EqualTo(decorative));
            Assert.That(game.Session.Effects.ReducedEffectsMode, Is.EqualTo(reducedEffects));
            game.Hud.ResumeGame();
            Assert.That(game.Hud.IsStatsVisible, Is.False);
        }

        [Test]
        public void PauseVolumeControlsTheAudioListenerAndKeepsTheSavedPreferenceAcrossRunReset()
        {
            const string key="EchoScrap.Audio.MasterVolume";
            bool existed=PlayerPrefs.HasKey(key);float saved=PlayerPrefs.GetFloat(key,1f),volume=AudioListener.volume;
            try
            {
                game.Session.SetManualPause(true);game.Hud.RefreshNow();
                var pause=game.Hud.CanvasRoot.transform.Find("PauseAndResultOverlay/Panel/Pause");
                pause.Find("Tabs/Sound").GetComponent<Button>().onClick.Invoke();
                var slider=pause.Find("Sound/VolumeSlider").GetComponent<Slider>();
                slider.value=35;
                Assert.AreEqual(.35f,AudioListener.volume,.001f);
                Assert.AreEqual(.35f,PlayerPrefs.GetFloat(key),.001f);
                Assert.AreSame(pause.Find("Sound/MusicSlider").GetComponent<Slider>(),slider.FindSelectableOnDown());
                Assert.AreEqual(Image.Type.Sliced,pause.Find("Artwork").GetComponent<Image>().type);
                Assert.Greater(pause.Find("Artwork").GetComponent<Image>().sprite.border.x,0);
                game.ResetRun(false);Assert.AreEqual(.35f,AudioListener.volume,.001f);
            }
            finally { AudioListener.volume=volume;if(existed)PlayerPrefs.SetFloat(key,saved);else PlayerPrefs.DeleteKey(key);PlayerPrefs.Save(); }
        }
        [UnityTest]
        public IEnumerator PauseFitsSmallPortraitAndWideScreensWithoutOverlappingControls()
        {
            game.Session.SetManualPause(true);game.Hud.RefreshNow();
            foreach(var size in new[]{new Vector2Int(640,480),new Vector2Int(720,1280),new Vector2Int(2560,720)})
                yield return Capture(size,true);
        }

        [Test]
        public void ModesShowFullOrChangedValuesAndReuseTheSameObjects()
        {
            string[] changed = { "range", "angle", "critChance", "armor" };
            game.Hud.SetStatsDisplayMode(StatsDisplayMode.Full);
            var readout = game.Hud.StatsReadout;
            int objects = readout.GetComponentsInChildren<Transform>(true).Length;
            Assert.That(readout.VisibleRowCount, Is.EqualTo(20));
            Assert.That(game.Hud.IsStatsVisible, Is.True);
            game.Stats.SetUpgradeTier("FLASH_01", 3);
            game.Stats.SetUpgradeTier("CRIT_01", 2);
            game.Stats.SetUpgradeTier("SCRAP_01", 1);
            float fullHeight = ((RectTransform)readout.transform).rect.height;
            Assert.That(readout.VisibleRowCount, Is.EqualTo(20));
            Assert.That(readout.IsRowVisible("angle"), Is.True);
            Assert.That(readout.IsRowVisible("armor"), Is.True);
            Assert.That(readout.IsRowVisible("damage"), Is.True);
            game.Hud.SetStatsDisplayMode(StatsDisplayMode.Reduced);
            Assert.That(readout.DisplayMode, Is.EqualTo(StatsDisplayMode.Reduced));
            Assert.That(readout.VisibleRowCount, Is.EqualTo(4));
            foreach (string key in changed) Assert.That(readout.IsRowVisible(key), Is.True, key);
            Assert.That(readout.IsRowVisible("damage"), Is.False);
            Assert.That(((RectTransform)readout.transform).rect.height, Is.EqualTo(fullHeight - 480f));
            var labels = readout.GetComponentsInChildren<Text>(true);
            float lastY = 0f, spacing = 0f;
            for (int i = 0; i < changed.Length; i++)
            {
                Text value = Array.Find(labels, label => label.name == changed[i] + "Value");
                Assert.That(value, Is.Not.Null);
                float y = readout.transform.InverseTransformPoint(value.transform.position).y;
                if (i == 1) { spacing = lastY - y; Assert.That(spacing, Is.InRange(20f, 32f)); }
                if (i > 1) Assert.That(lastY - y, Is.EqualTo(spacing).Within(.1f), "Hidden rows leave no gaps.");
                lastY = y;
            }
            Assert.That(readout.GetValue("range"), Is.EqualTo("10 m"));
            Assert.That(readout.GetValue("critChance"), Is.EqualTo("16%"));
            foreach (var mode in new[] { StatsDisplayMode.None, StatsDisplayMode.Full, StatsDisplayMode.Reduced })
            {
                game.Hud.SetStatsDisplayMode(mode);
                game.ResetRun(false);
                Assert.That(game.Hud.StatsMode, Is.EqualTo(mode), "The selected mode survives restarting the run.");
                Assert.That(game.Hud.IsStatsVisible, Is.EqualTo(mode == StatsDisplayMode.Full));
                Assert.That(readout.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(objects));
            }
        }

        [Test]
        public void ArmorRemainsFunctionalInFullStatsWithoutAnExtraIndicatorUnderHealth()
        {
            game.Stats.SetUpgradeTier("SCRAP_01", 3);
            game.Hud.SetStatsDisplayMode(StatsDisplayMode.Full);
            game.Hud.RefreshNow();
            Assert.That(game.Hud.CanvasRoot.transform.Find("Integrity/Armor"), Is.Null);
            Assert.That(game.Hud.StatsReadout.GetValue("armor"), Is.EqualTo("12%"));
            var hit = game.Player.TakeContactOrHazardDamage(10f, Vector3.back);
            Assert.That(hit.HealthLost, Is.EqualTo(8.8f).Within(.001f), "Removing the duplicate indicator must not alter armor mitigation.");
            game.Hud.SetStatsDisplayMode(StatsDisplayMode.Reduced);
            Assert.That(game.Hud.StatsReadout.IsRowVisible("armor"), Is.True);
            game.Hud.SetStatsDisplayMode(StatsDisplayMode.None);
            game.Hud.RefreshNow();
            Assert.That(game.Hud.IsStatsVisible, Is.False);
            Assert.That(game.Hud.CanvasRoot.transform.Find("Integrity/Armor"), Is.Null);
        }

        [Test]
        public void ReadoutShowsEffectiveValuesImmediatelyAfterUpgradesAndResets()
        {
            game.Hud.SetStatsVisible(true);
            var readout = game.Hud.StatsReadout;
            Assert.That(readout.GetValue("damage"), Is.EqualTo("3,75"));
            Assert.That(readout.GetValue("range"), Is.EqualTo("7 m"));
            Assert.That(readout.GetValue("execution"), Is.EqualTo("—"));
            game.Stats.SetUpgradeTier("FLASH_01", 2);
            game.Stats.SetUpgradeTier("FLASH_02", 2);
            game.Stats.SetUpgradeTier("FLASH_04", 2);
            game.Stats.SetUpgradeTier("CRIT_01", 3);
            game.Stats.SetUpgradeTier("CRIT_02", 2);
            game.Stats.SetUpgradeTier("SCRAP_01", 2);
            game.Stats.SetUpgradeTier("DASH_01", 3);
            game.Stats.SetUpgradeTier("BIO_03", 2);
            game.Stats.SetUpgradeTier("GREED_01", 3);
            game.Stats.SetUpgradeTier("GREED_02", 3);
            game.Stats.SetUpgradeTier("GREED_03", 2);
            game.Stats.SetUpgradeTier("CRYO_01", 3);
            game.Stats.SetUpgradeTier("CRYO_02", 2);
            game.Stats.SetUpgradeTier("CRIT_03", 2);
            game.Stats.SetUpgradeTier("DEMO_01", 3);
            game.Stats.SetBonusMitigation(.10f, .12f, .20f, .05f);
            Assert.That(readout.GetValue("damage"), Is.EqualTo("6,75"));
            Assert.That(readout.GetValue("attackSpeed"), Is.EqualTo("×1,3"));
            Assert.That(readout.GetValue("range"), Is.EqualTo("9 m"));
            Assert.That(readout.GetValue("angle"), Is.EqualTo("60°"));
            Assert.That(readout.GetValue("critChance"), Is.EqualTo("29%"));
            Assert.That(readout.GetValue("critMultiplier"), Is.EqualTo("×2,15"));
            Assert.That(readout.GetValue("moveSpeed"), Is.EqualTo("8 m/s"));
            Assert.That(readout.GetValue("armor"), Is.EqualTo("18%"));
            Assert.That(readout.GetValue("evasion"), Is.EqualTo("12%"));
            Assert.That(readout.GetValue("blockChance"), Is.EqualTo("20%"));
            Assert.That(readout.GetValue("dashCooldown"), Is.EqualTo("0,7 s"));
            Assert.That(readout.GetValue("regeneration"), Is.EqualTo("0,8/s"));
            Assert.That(readout.GetValue("healing"), Is.EqualTo("19"));
            Assert.That(readout.GetValue("magnet"), Is.EqualTo("8,5 m"));
            Assert.That(readout.GetValue("experience"), Is.EqualTo("+45%"));
            Assert.That(readout.GetValue("slow"), Is.EqualTo("28%"));
            Assert.That(readout.GetValue("freeze"), Is.EqualTo("1,8 s"));
            Assert.That(readout.GetValue("execution"), Is.EqualTo("< 8%"));
            Assert.That(readout.GetValue("explosionDamage"), Is.EqualTo("+36%"));
            Assert.That(readout.GetValue("explosionRadius"), Is.EqualTo("+45%"));
            game.Stats.SetUpgradeTier("DASH_04", 1);
            Assert.That(readout.GetValue("dashCooldown"), Is.EqualTo("0,5 s"));
            Assert.That(readout.IsRowVisible("dashCooldown"), Is.True);
            game.Hud.SetStatsVisible(false);
            game.Stats.SetUpgradeTier("FLASH_01", 4);
            game.Hud.SetStatsVisible(true);
            Assert.That(readout.GetValue("range"), Is.EqualTo("11 m"), "Hidden changes must appear when enabled again.");
            game.ResetRun(false);
            Assert.That(game.Hud.ShowStats, Is.True);
            Assert.That(game.Hud.IsStatsVisible, Is.True);
            Assert.That(readout.GetValue("damage"), Is.EqualTo("3,75"));
            Assert.That(readout.GetValue("armor"), Is.EqualTo("0%"));
            Assert.That(readout.GetValue("regeneration"), Is.EqualTo("0/s"));
        }

        [Test]
        public void ReadoutYieldsToDraftAndResultAndNeverInterceptsGameplayInput()
        {
            game.Hud.SetStatsVisible(true);
            game.Stats.SetUpgradeTier("FLASH_01", 1);
            Assert.That(game.Hud.StatsReadout.GetComponentsInChildren<Selectable>(true), Is.Empty);
            foreach (Graphic graphic in game.Hud.StatsReadout.GetComponentsInChildren<Graphic>(true))
                Assert.That(graphic.raycastTarget, Is.False, graphic.name);
            game.Progression.AddExperience(100);
            game.Hud.RefreshNow();
            Assert.That(game.Hud.IsStatsVisible, Is.False);
            Assert.That(game.Hud.ShowStats, Is.True);
            Assert.That(game.Draft.TryChoose(0), Is.True);
            game.Hud.RefreshNow();
            Assert.That(game.Hud.IsStatsVisible, Is.True);
            game.Player.TakeContactOrHazardDamage(10000f, Vector3.back);
            game.Session.ResolveSimulationStepOutcome();
            game.Hud.RefreshNow();
            Assert.That(game.Hud.IsResultVisible, Is.True);
            Assert.That(game.Hud.IsStatsVisible, Is.False);
            game.ResetRun(false);
            Assert.That(game.Hud.IsStatsVisible, Is.True);
            game.Hud.SetStatsVisible(false);
            game.ResetRun(false);
            Assert.That(game.Hud.ShowStats || game.Hud.IsStatsVisible, Is.False);
        }

        [Test]
        public void UnchangedReadoutRefreshDoesNotAllocateOrRecreateLabels()
        {
            game.Hud.SetStatsVisible(true);
            var readout = game.Hud.StatsReadout;
            game.Stats.SetCompanionBonuses(0f, 1f, .1f, 1f, 0f, 0f);
            Assert.That(readout.IsRowVisible("damage"), Is.True);
            int children = readout.transform.childCount;
            for (int i = 0; i < 32; i++) readout.Refresh(game);
            string sameText = readout.GetValue("damage");
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) readout.Refresh(game);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(bytes, Is.Zero);
            Assert.That(readout.GetValue("damage"), Is.SameAs(sameText));
            Assert.That(readout.transform.childCount, Is.EqualTo(children));
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < 1000; i++) readout.Refresh(game);
            double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"[Player stats] 1000 unchanged refreshes: {elapsedMs:0.###} ms CPU; managed allocations: {bytes} bytes.");
            game.Stats.SetCompanionBonuses(0f, 1f, 0f, 1f, 0f, 0f);
            game.Hud.SetStatsDisplayMode(StatsDisplayMode.Reduced);
            Assert.That(readout.IsRowVisible("damage"), Is.False);
        }

        [UnityTest]
        public IEnumerator StatsAndPauseRemainReadableAt720p1080pAndUltrawide()
        {
            foreach (var card in UpgradeCatalog.Cards) game.Stats.SetUpgradeTier(card.Id, card.MaxTier);
            game.Hud.SetStatsVisible(true);
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1080) })
            {
                game.Hud.SetStatsDisplayMode(StatsDisplayMode.Full);
                game.Hud.ResumeGame();
                yield return Capture(size, false);
                game.Hud.SetStatsDisplayMode(StatsDisplayMode.Reduced);
                yield return Capture(size, false);
                game.Session.SetManualPause(true);
                game.Hud.RefreshNow();
                yield return Capture(size, true);
            }
        }

        private IEnumerator Capture(Vector2Int size, bool pause)
        {
            Camera camera = game.GameplayCamera;
            var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            bool postProcessing = cameraData != null && cameraData.renderPostProcessing;
            var antialiasing = cameraData != null ? cameraData.antialiasing : AntialiasingMode.None;
            Canvas canvas = game.Hud.HudCanvas;
            RenderMode mode = canvas.renderMode;
            Camera previousCamera = canvas.worldCamera;
            float plane = canvas.planeDistance;
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            var target = new RenderTexture(size.x, size.y, 24);
            Texture2D texture = null;
            try
            {
                if (cameraData != null) { cameraData.renderPostProcessing = false; cameraData.antialiasing = AntialiasingMode.None; }
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .5f;
                Canvas.ForceUpdateCanvases();
                yield return null; yield return null; yield return null;
                Transform content = pause ? canvas.transform.Find("PauseAndResultOverlay/Panel/Pause") : game.Hud.StatsReadout.transform;
                foreach (Text text in content.GetComponentsInChildren<Text>())
                {
                    Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + .01f), text.name + " height");
                    Assert.That(text.preferredWidth, Is.LessThanOrEqualTo(text.rectTransform.rect.width + 1f), text.name + " width");
                }
                if (!pause)
                {
                    Rect stats = WorldRect((RectTransform)content);
                    Rect bounds = WorldRect((RectTransform)canvas.transform);
                    Assert.That(bounds.Contains(stats.min) && bounds.Contains(stats.max), Is.True, "Readout stays onscreen.");
                    foreach (string name in new[] { "Integrity", "Integrity/Artwork", "Dash", "Dash/Artwork", "AcquiredCards" })
                        Assert.That(stats.Overlaps(WorldRect((RectTransform)canvas.transform.Find(name))), Is.False, "Readout overlaps " + name);
                }
                else
                {
                    Rect bounds=WorldRect((RectTransform)canvas.transform),panel=WorldRect((RectTransform)content);
                    Assert.IsTrue(bounds.Contains(panel.min)&&bounds.Contains(panel.max),"The complete pause frame must stay on screen.");
                    Vector2 canvasSize = ((RectTransform)canvas.transform).rect.size;
                    Vector2 pauseSize = ((RectTransform)content).rect.size;
                    float previousFit = Mathf.Clamp(Mathf.Min((canvasSize.x - 48f) / pauseSize.x,
                        (canvasSize.y - 48f) / pauseSize.y), .1f, 1f);
                    Assert.That(content.localScale.x, Is.EqualTo(previousFit * .85f).Within(.001f));
                    foreach (string action in new[] { "Resume", "Restart" })
                        Assert.That(((RectTransform)content.Find(action)).sizeDelta, Is.EqualTo(new Vector2(248f, 64f)));
                    string[] controls={"Tabs","General/ShakeLabel","General/ShakeSlider","General/EffectsLabel","General/EffectsSlider","Resume","Restart"};
                    for(int a=0;a<controls.Length;a++)for(int b=a+1;b<controls.Length;b++)
                        Assert.IsFalse(WorldRect((RectTransform)content.Find(controls[a])).Overlaps(WorldRect((RectTransform)content.Find(controls[b]))),controls[a]+" overlaps "+controls[b]);
                    Transform selector = content.Find("Interface/StatsDisplayMode");
                    Rect selectorRect = WorldRect((RectTransform)selector);
                    foreach (string choice in new[] { "Full", "Reduced", "None" })
                    {
                        Rect button = WorldRect((RectTransform)selector.Find(choice));
                        Assert.That(selectorRect.Contains(button.min) && selectorRect.Contains(button.max), Is.True, choice + " stays in its row.");
                        foreach (string name in new[] { "Interface/CompanionPowers", "Resume", "Restart" })
                            Assert.That(button.Overlaps(WorldRect((RectTransform)content.Find(name))), Is.False, choice + " overlaps " + name);
                    }
                }
                RenderTexture.active = target;
                texture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); texture.Apply();
                string output = Path.Combine(Application.dataPath, "..", "Logs", "PlayerStatsReadout");
                Directory.CreateDirectory(output);
                File.WriteAllBytes(Path.Combine(output, $"{(pause ? "Pause" : "Stats" + game.Hud.StatsMode)}_{size.x}x{size.y}.png"), texture.EncodeToPNG());
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

        private static Rect WorldRect(RectTransform transform)
        {
            var corners = new Vector3[4]; transform.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
    }
}
