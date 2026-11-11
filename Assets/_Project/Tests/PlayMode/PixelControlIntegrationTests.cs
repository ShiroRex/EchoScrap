using System.Collections;
using System.Diagnostics;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EchoScrap.Tests
{
    public sealed class PixelControlIntegrationTests
    {
        private CombatGameBootstrap game;
        private bool savedReduced;
        private float savedShake, savedDecoration;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1;
            SceneManager.sceneLoaded += Loaded;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            SceneManager.sceneLoaded -= Loaded;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = game.Encounter.AutoTick = game.Upgrades.AutoTick = false;
            game.ResetRun(false); game.Weapon.enabled = false;
            game.Player.IgnoreIncomingDamage = true;
            game.Session.ResumeAllUserPauses();
            savedReduced = game.Session.Effects.ReducedEffectsMode;
            savedShake = game.Session.Effects.RawShakeIntensity;
            savedDecoration = game.Session.Effects.RawDecorativeIntensity;
        }

        private void Loaded(Scene _, LoadSceneMode __)
        {
            var station = Object.FindAnyObjectByType<FusionStation>();
            if (station != null) station.PersistProgress = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= Loaded;
            if (game != null)
            {
                game.Session.Effects.ReducedEffectsMode = savedReduced;
                game.Session.Effects.ShakeIntensity = savedShake;
                game.Session.Effects.DecorativeIntensity = savedDecoration;
            }
            var scene = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Pixel UI cleanup"));
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            CombatTargetRegistry.Clear(); Time.timeScale = 1;
        }

        [UnityTest]
        public IEnumerator InteractionPromptsFollowTheTargetAndStayInsideTheHudSafeArea()
        {
            var station = Object.FindAnyObjectByType<FusionStation>();
            var prompt = station.transform.Find("CompanionCanvas/ColeccionarIndicador").GetComponent<PixelInteractionPrompt>();
            var root = (RectTransform)prompt.transform;
            var canvas = root.GetComponentInParent<Canvas>();
            Canvas.ForceUpdateCanvases();
            var bounds = ((RectTransform)canvas.transform).rect;
            var corners = new Vector3[4];
            foreach (var viewport in new[] { new Vector3(0, 0, 20), new Vector3(1, 1, 20), new Vector3(.5f, .5f, 20) })
            {
                prompt.gameObject.SetActive(true);
                prompt.PlaceNear(game.GameplayCamera, game.GameplayCamera.ViewportToWorldPoint(viewport));
                root.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var local = canvas.transform.InverseTransformPoint(corner);
                    Assert.That(local.x, Is.InRange(bounds.xMin + 23.9f, bounds.xMax - 23.9f));
                    Assert.That(local.y, Is.InRange(bounds.yMin + 163.9f, bounds.yMax - 215.9f));
                }
            }
            Assert.That(root.anchoredPosition.y, Is.EqualTo(-128f).Within(1f), "The prompt should follow a centered target below its feet.");
            prompt.PlaceNear(game.GameplayCamera, game.GameplayCamera.transform.position - game.GameplayCamera.transform.forward);
            Assert.IsFalse(prompt.gameObject.activeSelf, "Targets behind the camera must not leave a floating hint on screen.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator HudUsesTheNativeClockMinimapAndInteractionArt()
        {
            var hud = game.Hud.CanvasRoot.transform;
            var typography = Resources.Load<UpgradeCardSkin>("UI/PixelPortal/Typography");
            foreach (var text in hud.GetComponentsInChildren<Text>(true))
                Assert.IsTrue(text.font == typography.regular || text.font == typography.semibold, text.name + " must use the shared UI typography.");
            Assert.AreSame(PixelControlArtwork.Load("ClockFrame/ClockFrame"), hud.Find("ClockFrame").GetComponent<Image>().sprite);
            game.Session.Clock.SetGameplayTimeForTesting(123f); game.Hud.RefreshNow();
            Assert.AreEqual("02:03", hud.Find("Clock").GetComponent<Text>().text);
            Canvas.ForceUpdateCanvases();
            Assert.Greater(hud.Find("Clock").GetComponent<Text>().cachedTextGenerator.vertexCount, 4, "The clock must generate visible glyphs inside its artwork.");
            var map = game.Hud.GetComponent<CombatMinimap>(); map.Refresh();
            Assert.AreSame(PixelControlArtwork.Load("MinimapFrame/Frame"), hud.Find("Minimap/Artwork").GetComponent<Image>().sprite);
            Assert.AreSame(PixelControlArtwork.Load("MinimapMarkers/North"), hud.Find("Minimap/Interior/Norte").GetComponent<Image>().sprite);
            Assert.AreSame(PixelControlArtwork.Load("BossPointer/Up"), hud.Find("BossOffscreenArrow/Artwork").GetComponent<Image>().sprite);
            var station = Object.FindAnyObjectByType<FusionStation>();
            var companions = station.GetComponent<CompanionSystem>();
            Assert.IsTrue(companions.TrySpawn(0)); Assert.IsTrue(companions.TrySpawn(1)); map.Refresh();
            Assert.AreSame(PixelControlArtwork.Load("MinimapMarkers/Common"), hud.Find("Minimap/Interior/MapaArena/Coleccionable_0").GetComponent<Image>().sprite);
            Assert.AreSame(PixelControlArtwork.Load("MinimapMarkers/Rare"), hud.Find("Minimap/Interior/MapaArena/Coleccionable_1").GetComponent<Image>().sprite);
            var prompts = Object.FindObjectsByType<PixelInteractionPrompt>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.AreEqual(4, prompts.Length, "Equip, collect, fusion and release share the delivered template.");
            foreach (var prompt in prompts)
            {
                Assert.AreSame(PixelControlArtwork.Load("InteractionPrompt/Plate_Normal"), prompt.GetComponent<Image>().sprite);
                Assert.IsFalse(prompt.GetComponent<Image>().raycastTarget);
                Assert.AreSame(PixelControlArtwork.Load("InteractionPrompt/Glyph_E"), prompt.transform.Find("InteractionKey/Glyph").GetComponent<Image>().sprite);
                var plate = (RectTransform)prompt.transform;
                var label = prompt.GetComponentInChildren<Text>(true);
                Assert.That(plate.sizeDelta.y, Is.EqualTo(42f));
                Assert.That(plate.sizeDelta.x, Is.LessThanOrEqualTo(215f));
                Assert.That(label.rectTransform.rect.width, Is.GreaterThanOrEqualTo(label.preferredWidth));
            }
            var equip = station.transform.Find("CompanionCanvas/EquiparIndicador").GetComponent<PixelInteractionPrompt>();
            var equipPlate = (RectTransform)equip.transform;
            float equipWidth = equipPlate.sizeDelta.x;
            equip.SetAction("EDITAR");
            Assert.That(equipPlate.sizeDelta.x, Is.LessThan(equipWidth));
            equip.SetAction("EQUIPAR");
            yield return Capture("HUD", 1920, 1080);
            Assert.IsTrue(Object.FindAnyObjectByType<WarehouseTravel>().PlaceInWarehouse(true));
            Vector3 interaction = station.InteractionPoint.position + Vector3.up;
            game.Player.transform.position = interaction;
            game.Player.GetComponent<Rigidbody>().position = interaction;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.5f);
            Assert.IsTrue(station.CanInteract);
            var fusionPrompt = station.GetComponentInChildren<Canvas>().transform.Find("IndicadorFusion");
            Assert.IsNotNull(fusionPrompt);
            Assert.IsTrue(fusionPrompt.gameObject.activeInHierarchy, "The real fusion prompt is visible at the station.");
            yield return Capture("Interaccion_Fusion", 1920, 1080);
        }

        [UnityTest]
        public IEnumerator PauseControlsKeepTheirActionsAndRespondToPointerAndSubmit()
        {
            game.Session.SetManualPause(true); game.Hud.RefreshNow();
            var pause = game.Hud.CanvasRoot.transform.Find("PauseAndResultOverlay/Panel/Pause");
            var soundTab = pause.Find("Tabs/Sound").GetComponent<Button>();
            var interfaceTab = pause.Find("Tabs/Interface").GetComponent<Button>();
            var volume = pause.Find("Sound/VolumeSlider").GetComponent<Slider>();
            var music = pause.Find("Sound/MusicSlider").GetComponent<Slider>();
            var full = pause.Find("Interface/StatsDisplayMode/Full").GetComponent<Button>();
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            var baseEvent = new BaseEventData(EventSystem.current);
            float oldVolume = AudioListener.volume;
            bool hadVolume = PlayerPrefs.HasKey("EchoScrap.Audio.MasterVolume");
            float storedVolume = PlayerPrefs.GetFloat("EchoScrap.Audio.MasterVolume", 1f);
            const string musicKey = "EchoScrap.Audio.MusicVolume";
            bool hadMusic = PlayerPrefs.HasKey(musicKey);
            float oldMusic = CombatMusicPlayer.MusicVolume;
            try
            {
                soundTab.onClick.Invoke();
                Assert.IsTrue(pause.Find("Sound").gameObject.activeInHierarchy);
                Assert.IsFalse(pause.Find("General").gameObject.activeInHierarchy);
                volume.value = 37;
                music.value = 26;
                Assert.AreEqual(.37f, AudioListener.volume, .001f);
                Assert.AreEqual(.26f, CombatMusicPlayer.MusicVolume, .001f);
                Assert.AreEqual(.26f, PlayerPrefs.GetFloat(musicKey), .001f);
                Assert.AreSame(PixelControlArtwork.Load("Slider/Track"), volume.transform.Find("Background").GetComponent<Image>().sprite);
                Assert.AreEqual(48f, volume.handleRect.rect.height, .1f, "The cross-axis anchors must not stretch the handle twice.");
                ExecuteEvents.Execute(volume.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                Assert.AreSame(PixelControlArtwork.Load("Slider/Handle_Hover"), ((Image)volume.targetGraphic).sprite);
                ExecuteEvents.Execute(volume.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                Assert.AreSame(PixelControlArtwork.Load("Slider/Handle_Pressed"), ((Image)volume.targetGraphic).sprite);
                ExecuteEvents.Execute(volume.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(volume.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                EventSystem.current.SetSelectedGameObject(null);
                interfaceTab.onClick.Invoke();
                Assert.IsFalse(pause.Find("Sound").gameObject.activeInHierarchy);
                ExecuteEvents.Execute(full.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                Assert.AreEqual(StatsDisplayMode.Full, game.Hud.StatsMode);
                ExecuteEvents.Execute(full.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                Assert.AreSame(PixelControlArtwork.Load("StatsSelector/Selected_Hover"), full.image.sprite);
                EventSystem.current.SetSelectedGameObject(full.gameObject);
                ExecuteEvents.Execute(full.gameObject, baseEvent, ExecuteEvents.submitHandler);
                Assert.AreSame(PixelControlArtwork.Load("StatsSelector/Selected_Pressed"), full.image.sprite);
                yield return new WaitForSecondsRealtime(.15f);
                Assert.AreSame(PixelControlArtwork.Load("StatsSelector/Selected_Hover"), full.image.sprite);
                ExecuteEvents.Execute(full.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                EventSystem.current.SetSelectedGameObject(null);
                Assert.AreSame(PixelControlArtwork.Load("StatsSelector/Selected"), full.image.sprite);
                yield return Capture("Pausa_Interfaz_720", 1280, 720);
                soundTab.onClick.Invoke();
                volume.value = 37;
                yield return Capture("Pausa_Sonido_720", 1280, 720);
                pause.Find("Tabs/General").GetComponent<Button>().onClick.Invoke();
                yield return Capture("Pausa_1080", 1920, 1080);
                yield return Capture("Pausa_720", 1280, 720);
                var views = pause.GetComponentsInChildren<PixelControlArtwork>();
                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < 1000; i++) foreach (var view in views) view.Refresh();
                stopwatch.Stop();
                string path = ReviewPath("UIRefresh.txt");
                File.WriteAllText(path, "1000 refresh batches of " + views.Length + " controls: " + stopwatch.Elapsed.TotalMilliseconds.ToString("F3") + " ms. Event-driven visuals have no Update method.");
            }
            finally
            {
                AudioListener.volume = oldVolume;
                if (hadVolume) PlayerPrefs.SetFloat("EchoScrap.Audio.MasterVolume", storedVolume); else PlayerPrefs.DeleteKey("EchoScrap.Audio.MasterVolume");
                CombatMusicPlayer.SetMusicVolume(oldMusic);
                if (!hadMusic) PlayerPrefs.DeleteKey(musicKey);
                PlayerPrefs.Save();
            }
        }

        [UnityTest]
        public IEnumerator TitleSettingsReuseTheControlsAndDisabledSliderArt()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            var title = menu.View.Canvas.transform.Find("MainMenu");
            Assert.AreSame(PixelControlArtwork.Load("Presentation/CoverArt"), title.Find("CoverArtwork").GetComponent<Image>().sprite);
            Assert.AreSame(PixelControlArtwork.Load("Presentation/Logo"), title.Find("LanternLogo").GetComponent<Image>().sprite);
            Assert.AreSame(PixelControlArtwork.Load("Presentation/Title"), title.Find("TitleArtwork").GetComponent<Image>().sprite);
            Assert.AreSame(PixelControlArtwork.Load("Presentation/ButtonBlue"), title.Find("TitleFrame/NewCollection").GetComponent<Image>().sprite);
            bool previous = menu.Effects.ReducedEffectsMode;
            try
            {
                menu.OpenConfiguration(); menu.Settings.SetReducedEffects(true);
                var panel = menu.View.Canvas.transform.Find("MenuSettings/Panel");
                var slider = panel.Find("ShakeSlider").GetComponent<Slider>();
                Assert.IsFalse(slider.interactable);
                Assert.AreSame(PixelControlArtwork.Load("Slider/Handle_Disabled"), ((Image)slider.targetGraphic).sprite);
                Assert.AreSame(PixelControlArtwork.Load("Slider/Track_Disabled"), slider.transform.Find("Background").GetComponent<Image>().sprite);
                yield return Capture("Configuracion", 1920, 1080);
            }
            finally { menu.Settings.SetReducedEffects(previous); }
        }

        private static string ReviewPath(string name)
        {
            // Automated captures must live beside the external QA project, not the game checkout.
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Review"));
            Directory.CreateDirectory(directory); return Path.Combine(directory, name);
        }

        [UnityTest]
        public IEnumerator VaultTipsAndExpeditionButtonFitAt720p()
        {
            var flow = game.gameObject.AddComponent<GameFlowController>();
            flow.Initialize(game, false);
            flow.View.NewCollectionButton.onClick.Invoke();
            if (flow.IsConfirming) flow.AcceptConfirmation();
            yield return null;
            Assert.IsTrue(flow.View.IsVaultVisible);
            var tips = (RectTransform)flow.View.Canvas.transform.Find("VaultTips");
            var start = (RectTransform)flow.View.StartButton.transform;
            Assert.That(tips.anchoredPosition.y, Is.EqualTo(start.anchoredPosition.y));
            Assert.AreEqual(Vector2.zero, tips.anchorMin);
            Assert.AreEqual(new Vector2(1, 0), start.anchorMin);
            Assert.IsNotNull(tips.Find("CollectorLantern").GetComponent<Image>().sprite);
            yield return Capture("Boveda_720", 1280, 720);
        }

        [UnityTest]
        public IEnumerator VaultArrowsLeadThroughTheStairsAndHideOutsideExploration()
        {
            var flow = game.gameObject.AddComponent<GameFlowController>();
            flow.Initialize(game, false);
            flow.View.NewCollectionButton.onClick.Invoke();
            if (flow.IsConfirming) flow.AcceptConfirmation();
            yield return null;
            var directions = flow.View.Canvas.transform.Find("VaultDirections");
            var fusion = (RectTransform)directions.Find("Fusion/Arrow");
            var equipment = (RectTransform)directions.Find("Equipment/Arrow");
            Assert.IsTrue(fusion.gameObject.activeInHierarchy && equipment.gameObject.activeInHierarchy);
            Assert.IsFalse(fusion.GetComponent<Image>().raycastTarget || equipment.GetComponent<Image>().raycastTarget);
            yield return Capture("Boveda_Flechas_720", 1280, 720);

            var station = Object.FindAnyObjectByType<FusionStation>();
            var body = game.Player.GetComponent<Rigidbody>();
            void Place(Vector3 position)
            {
                body.position = position;
                body.transform.position = position;
                Physics.SyncTransforms();
            }

            void AssertBearing(RectTransform arrow, Vector3 worldTarget)
            {
                Vector3 offset = worldTarget - body.transform.position;
                offset.y = 0;
                Vector3 right = game.GameplayCamera.transform.right;
                right.y = 0;
                Vector3 expected = new Vector3(Vector3.Dot(offset, right),
                    Vector3.Dot(offset, Vector3.Cross(right, Vector3.up)), 0).normalized;
                Assert.That(Vector3.Dot(expected, arrow.localRotation * Vector3.up), Is.GreaterThan(.98f), arrow.parent.name);
            }

            Place(new Vector3(180, 1, 8));
            yield return null;
            AssertBearing(equipment, new Vector3(160, 0, -.5f));
            AssertBearing(fusion, station.InteractionPoint.position);

            Place(new Vector3(160, 4, 8));
            yield return null;
            AssertBearing(equipment, new Vector3(160, 0, 16.5f));
            AssertBearing(fusion, new Vector3(160, 0, -.5f));
            Assert.That(equipment.parent.Find("Label").GetComponent<Text>().text, Is.EqualTo("SUBE AL BALCÓN"));

            Place(new Vector3(167, 6, 22));
            yield return null;
            Assert.IsFalse(equipment.gameObject.activeInHierarchy);
            Assert.That(equipment.parent.Find("Label").GetComponent<Text>().text, Is.EqualTo("PUESTO 2 · EQUIPAR"));
            AssertBearing(fusion, new Vector3(160, 0, 16.5f));

            game.Session.SetManualPause(true);
            yield return null;
            Assert.IsFalse(directions.gameObject.activeInHierarchy);
            game.Session.SetManualPause(false);
            Object.FindAnyObjectByType<WarehouseTravel>().PlaceInWarehouse(false);
            yield return null;
            Assert.IsFalse(directions.gameObject.activeInHierarchy);
        }

        private IEnumerator Capture(string name, int width, int height)
        {
            var camera = game != null ? game.GameplayCamera : new GameObject("Pixel UI capture").AddComponent<Camera>();
            bool temporaryCamera = game == null;
            var target = new RenderTexture(width, height, 24);
            var oldTarget = camera.targetTexture; camera.targetTexture = target;
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var changed = new System.Collections.Generic.List<Canvas>();
            foreach (var canvas in canvases)
                if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; changed.Add(canvas); }
            Canvas.ForceUpdateCanvases(); yield return null; Canvas.ForceUpdateCanvases();
            camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(ReviewPath(name + ".png"), image.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = oldTarget;
            foreach (var canvas in changed) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
            Object.Destroy(image); Object.Destroy(target); if (temporaryCamera) Object.Destroy(camera.gameObject);
        }
    }
}
