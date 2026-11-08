using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace EchoScrap.Tests
{
    public sealed class GameLoopTests
    {
        private CombatGameBootstrap game;
        private GameFlowController flow;
        private WarehouseTravel travel;
        private FusionStation station;
        private InputDevice testDevice;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
        private void Loaded(Scene scene, LoadSceneMode mode)
        {
            var fusion = Object.FindAnyObjectByType<FusionStation>();
            if (fusion != null) fusion.PersistProgress = false;
        }
        [UnitySetUp] public IEnumerator SetUp()
        {
            Time.timeScale = 1;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            SceneManager.sceneLoaded += Loaded;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            Bind();
            flow = game.gameObject.AddComponent<GameFlowController>();
            flow.Initialize(game, false);
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
        }
        private void Bind()
        {
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            travel = Object.FindAnyObjectByType<WarehouseTravel>();
            station = Object.FindAnyObjectByType<FusionStation>();
            game.Director.AutoTick = false; game.Encounter.AutoTick = false;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= Loaded;
            if (testDevice != null) InputSystem.RemoveDevice(testDevice);
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
            Time.timeScale = 1;
            if (game != null)
            {
                var scene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("GameLoopCleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            CombatTargetRegistry.Clear(); Time.timeScale = 1;
        }
        private void EnterVault() => flow.View.NewCollectionButton.onClick.Invoke();
        private IEnumerator StartRun() { EnterVault(); flow.View.StartButton.onClick.Invoke(); yield return ChooseRound(); }
        private IEnumerator ChooseRound()
        {
            Assert.That(flow.IsSelectingExpedition, Is.True);
            flow.View.DemoRoundButton.onClick.Invoke();
            float deadline = Time.realtimeSinceStartup + 12;
            while ((flow.IsLoading || Object.FindAnyObjectByType<ExpeditionIntro>().IsPlaying) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Expedition));
            Assert.That(game.Session.IsPaused, Is.False);
        }
        private Button HudButton(string path) => game.Hud.CanvasRoot.transform.Find("PauseAndResultOverlay/Panel/" + path).GetComponent<Button>();

        [UnityTest] public IEnumerator FirstCollectibleShowsVaultHintUntilEquipmentHasBeenUsed()
        {
            yield return StartRun();
            var hint = flow.View.Canvas.transform.Find("EquipmentHint").gameObject;
            Assert.IsFalse(hint.activeSelf);
            Assert.IsTrue(station.CollectFromArena(FusionCatalog.MapIds[0]));
            yield return null;
            Assert.IsFalse(hint.activeSelf);
            Assert.IsTrue(travel.TryTravel(false, game.Player));
            yield return null;
            Assert.IsTrue(hint.activeSelf);
            for (int i = 0; i < 3; i++)
            {
                var label = station.DisplaySlots[i].GetComponentInChildren<Text>();
                Assert.That(label.text, Is.EqualTo($"EQUIPO {i + 1}"));
            }
            Assert.IsTrue(station.EquipCompanion(0, FusionCatalog.MapIds[0]));
            yield return null;
            Assert.IsFalse(hint.activeSelf);
            Assert.IsTrue(station.EquipCompanion(0, -1));
            yield return null;
            Assert.IsFalse(hint.activeSelf);
        }

        [UnityTest] public IEnumerator TitleFreezesWorldAndContinueRequiresACollection()
        {
            Assert.That(flow.State, Is.EqualTo(GameFlowState.MainMenu));
            Assert.That(flow.View.IsTitleVisible, Is.True);
            Assert.That(flow.View.ContinueButton.interactable, Is.False);
            flow.ContinueCollection();
            game.Session.ToggleManualPause();
            var backdrop = flow.View.GetComponentInChildren<MenuBackdrop>();
            float backdropTime = backdrop.PresentationTime;
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(backdrop.PresentationTime, Is.GreaterThan(backdropTime), "Decorative motion must continue while the menu freezes gameplay.");
            Assert.That(game.Session.GameplayTime, Is.Zero);
            Assert.That(game.Session.IsInWarehouse, Is.True);
            Assert.That(game.Session.IsPaused, Is.True);
            Assert.That(game.Hud.IsPauseVisible, Is.False);
            Assert.That(game.Director.ActiveEnemyCount, Is.Zero);
            Assert.That(game.Hud.CanvasRoot.transform.Find("Integrity").gameObject.activeSelf, Is.False);
        }

        [UnityTest] public IEnumerator NewCollectionEntersPhysicalVaultWithoutStartingCombat()
        {
            EnterVault();
            yield return new WaitForSeconds(.15f);
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Vault));
            Assert.That(flow.View.IsVaultVisible, Is.True);
            Assert.That(flow.View.GetComponentInChildren<MenuBackdrop>(true).gameObject.activeSelf, Is.False);
            Assert.That(travel.IsInWarehouse, Is.True);
            Assert.That(Vector3.Distance(game.Player.transform.position, travel.WarehouseArrival.position), Is.LessThan(2));
            Assert.That(game.Session.GameplayTime, Is.Zero);
            Assert.That(game.Director.ActiveEnemyCount, Is.Zero);
            Assert.That(game.Motor.GetComponent<PlayerInputReader>().CanRead, Is.True);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null, "Dash in the physical vault must not submit the start button.");
            Assert.That(game.Hud.IsStatsVisible, Is.False);
            Assert.That(game.Player.CurrentHealth, Is.EqualTo(game.Player.MaxHealth));
        }

        [UnityTest] public IEnumerator StartButtonBeginsFreshRunAndRestoresCombatHud()
        {
            yield return StartRun();
            yield return new WaitForSeconds(.1f);
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Expedition));
            Assert.That(game.Session.IsInWarehouse, Is.False);
            Assert.That(game.Session.IsPaused, Is.False);
            Assert.That(game.Session.GameplayTime, Is.GreaterThan(0));
            Assert.That(Vector3.Distance(game.Player.transform.position, game.SpawnPosition), Is.LessThan(2));
            Assert.That(game.Hud.CanvasRoot.transform.Find("Integrity").gameObject.activeSelf, Is.True);
            Assert.That(game.Progression.Level, Is.EqualTo(1));
            Assert.That(game.Weapon.enabled, Is.True);
            Assert.That(flow.View.IsVaultVisible, Is.False);
        }

        [UnityTest] public IEnumerator VaultPortalAlsoStartsAnExpedition()
        {
            EnterVault();
            yield return new WaitForSeconds(1.05f);
            Assert.That(travel.TryTravel(true, game.Player), Is.True);
            yield return ChooseRound();
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Expedition));
            Assert.That(game.Session.IsInWarehouse, Is.False);
            Assert.That(game.Session.GameplayTime, Is.LessThan(.5f));
        }

        [UnityTest] public IEnumerator ExpeditionSelectorCanBeClosedSilentlyFromButtonOrPortal()
        {
            EnterVault();
            yield return new WaitForSeconds(1.05f);
            var roarClip = Resources.Load<AudioClip>("Audio/ArenaSelectionRoar");
            Assert.That(roarClip, Is.Not.Null);
            var roar = System.Array.Find(flow.GetComponents<AudioSource>(), source => source.clip == roarClip);
            Assert.That(roar, Is.Not.Null);

            flow.View.StartButton.onClick.Invoke();
            Assert.That(flow.IsSelectingExpedition, Is.True);
            Assert.That(roar.isPlaying, Is.False);
            flow.View.ExitExpeditionButton.onClick.Invoke();
            yield return null;
            Assert.That(roar.isPlaying, Is.False);

            Assert.That(travel.TryTravel(true, game.Player), Is.True);
            Assert.That(flow.IsSelectingExpedition, Is.True);
            Assert.That(roar.isPlaying, Is.False);
            flow.CancelExpeditionSelection();
            yield return null;
            Assert.That(roar.isPlaying, Is.False);
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Vault));
            Assert.That(game.Session.IsPaused, Is.False);
        }

        [UnityTest] public IEnumerator WalkingIntoPhysicalPortalStartsRunWithoutClickingUi()
        {
            EnterVault();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>(); testDevice = keyboard;
            // The vault portal is now at the front of the room, behind the arrival point.
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S));
            float deadline = Time.realtimeSinceStartup + 5;
            while (!flow.IsSelectingExpedition && Time.realtimeSinceStartup < deadline) yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            yield return ChooseRound();
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Expedition), $"Walking must reach the existing trigger. Position={game.Player.transform.position}, input={game.Motor.GetComponent<PlayerInputReader>().Movement}.");
            Assert.That(game.Session.IsInWarehouse, Is.False);
            Assert.That(Vector3.Distance(game.Player.transform.position, game.SpawnPosition), Is.LessThan(3));
        }

        [UnityTest] public IEnumerator GamepadCanConfirmTitleWithoutDashingInVault()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var pad = InputSystem.AddDevice<Gamepad>(); testDevice = pad;
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
            yield return null; yield return null;
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Vault));
            Assert.That(game.Motor.IsDashing, Is.False);
            Assert.That(game.Motor.GetComponent<PlayerInputReader>().IsDashQueued, Is.False);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null);
            InputSystem.QueueStateEvent(pad, new GamepadState());
        }

        [UnityTest] public IEnumerator AbandonCanBeCancelledThenConfirmedWithoutLosingCollection()
        {
            yield return StartRun();
            station.Progress.owned[4] = 3;
            game.Stats.SetUpgradeTier("BIO_01", 1);
            game.Enemies.Spawn(EnemyArchetypeId.Residual, Vector3.right * 12 + Vector3.up);
            game.Session.SetManualPause(true); game.Hud.RefreshNow();
            HudButton("Pause/Restart").onClick.Invoke();
            Assert.That(flow.IsConfirming, Is.True);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(flow.View.CancelButton.gameObject));
            flow.CancelConfirmation();
            Assert.That(game.Hud.IsPauseVisible, Is.True);
            Assert.That(game.Session.IsPaused, Is.True);
            Assert.That(game.Stats.GetUpgradeTier(UpgradeId.BIO_01), Is.EqualTo(1));
            HudButton("Pause/Restart").onClick.Invoke(); flow.AcceptConfirmation();
            yield return null;
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Vault));
            Assert.That(game.Session.IsInWarehouse, Is.True);
            Assert.That(game.Session.IsPaused, Is.False);
            Assert.That(game.Session.GameplayTime, Is.Zero);
            Assert.That(game.Director.ActiveEnemyCount, Is.Zero);
            Assert.That(game.Stats.GetUpgradeTier(UpgradeId.BIO_01), Is.Zero);
            Assert.That(station.Progress.owned[4], Is.EqualTo(3));
        }

        [UnityTest] public IEnumerator DefeatReturnsToVaultAndAllowsAnotherRun()
        {
            yield return StartRun(); game.Player.TakeContactOrHazardDamage(10000, Vector3.right);
            game.Session.ResolveSimulationStepOutcome(); game.Hud.RefreshNow();
            Assert.That(game.Session.State, Is.EqualTo(SessionState.Defeat));
            Assert.That(game.Hud.IsResultVisible, Is.True);
            HudButton("Result/Restart").onClick.Invoke();
            yield return null;
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Vault));
            Assert.That(game.Player.IsAlive, Is.True);
            Assert.That(game.Session.IsPaused, Is.False);
            flow.View.StartButton.onClick.Invoke();
            yield return ChooseRound();
            Assert.That(game.Session.IsInWarehouse, Is.False);
            Assert.That(game.Session.State, Is.EqualTo(SessionState.Running));
        }

        [UnityTest] public IEnumerator VictoryReturnsToVaultWithoutResettingFusion()
        {
            yield return StartRun(); station.Progress.owned[0]=4;station.Progress.Begin(new[] { 0, 0, 0, 0 }, 9000, 0);
            float remaining = station.Progress.remaining;
            var death = new ActorDeathEvent(999, 5, false, true, Vector3.zero, Vector3.forward, DamageSourceType.FlashlightPulse, 0, false, true);
            game.Session.QueueBossDeath(in death); game.Session.ResolveSimulationStepOutcome(); game.Hud.RefreshNow();
            Assert.That(game.Hud.IsResultVisible, Is.True);
            HudButton("Result/Restart").onClick.Invoke();
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Vault));
            Assert.That(station.Progress.phase, Is.EqualTo(FusionPhase.Fusing));
            Assert.That(station.Progress.remaining, Is.EqualTo(remaining));
            yield return null;
        }

        [UnityTest] public IEnumerator ExistingCollectionRequiresConfirmationAndCancelPreservesIt()
        {
            EnterVault(); station.Progress.owned[4] = 7;
            flow.ShowMainMenu(); flow.RequestNewCollection();
            Assert.That(flow.IsConfirming, Is.True);
            Assert.That(station.Progress.owned[4], Is.EqualTo(7));
            flow.CancelConfirmation();
            Assert.That(flow.State, Is.EqualTo(GameFlowState.MainMenu));
            Assert.That(station.Progress.owned[4], Is.EqualTo(7));
            flow.ContinueCollection();
            Assert.That(station.Progress.owned[4], Is.EqualTo(7));
            flow.ShowMainMenu(); flow.RequestNewCollection(); flow.AcceptConfirmation();
            Assert.That(station.Progress.owned[4], Is.Zero);
            Assert.That(flow.State, Is.EqualTo(GameFlowState.Vault));
            yield return null;
        }

        [UnityTest] public IEnumerator ConfigurationReusesPauseControlsAndReturnsToTitle()
        {
            flow.OpenConfiguration();
            Assert.That(game.Hud.IsPauseVisible, Is.True);
            Assert.That(flow.View.IsTitleVisible, Is.False);
            game.Hud.SetShakePercent(35);
            game.Hud.ResumeGame();
            yield return new WaitForSecondsRealtime(.1f);
            Assert.That(flow.State, Is.EqualTo(GameFlowState.MainMenu));
            Assert.That(flow.View.IsTitleVisible, Is.True);
            Assert.That(game.Hud.IsPauseVisible, Is.False);
            Assert.That(game.Session.IsPaused, Is.True);
            Assert.That(game.Session.GameplayTime, Is.Zero);
            yield return StartRun();
            Assert.That(game.Session.Effects.RawShakeIntensity, Is.EqualTo(.35f).Within(.001));
        }

        [UnityTest] public IEnumerator RepeatedCyclesReuseUiAndPools()
        {
            EnterVault(); int created = game.Enemies.TotalCreatedCount;
            for (int i = 0; i < 5; i++)
            {
                Assert.That(flow.BeginExpedition(), Is.True);
                yield return ChooseRound();
                game.Session.SetManualPause(true); game.Hud.RefreshNow();
                flow.RequestAbandon(); flow.AcceptConfirmation();
            }
            yield return null;
            Assert.That(Object.FindObjectsByType<GameMenuView>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<CombatSession>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(game.Enemies.TotalCreatedCount, Is.EqualTo(created));
            Assert.That(game.Session.GameplayTime, Is.Zero);
        }

        [UnityTest] public IEnumerator ReducedEffectsFreezeBackgroundAndReturningToMenuReusesIt()
        {
            var backdrop = flow.View.GetComponentInChildren<MenuBackdrop>();
            game.Session.Effects.ReducedEffectsMode = true;
            float frozen = backdrop.PresentationTime;
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(backdrop.PresentationTime, Is.EqualTo(frozen));
            EnterVault(); flow.ShowMainMenu();
            Assert.That(flow.View.GetComponentInChildren<MenuBackdrop>(), Is.SameAs(backdrop));
            game.Session.Effects.ReducedEffectsMode = false;
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(backdrop.PresentationTime, Is.GreaterThan(frozen));
        }

        [UnityTest] public IEnumerator PortalPlaysAtSixtyFramesAndCrossesAtlasPagesWhileGameplayIsPaused()
        {
            yield return null;
            var backdrop = flow.View.GetComponentInChildren<MenuBackdrop>();
            var background = backdrop.GetComponent<RawImage>();
            var firstTexture = background.texture;
            int firstFrame = backdrop.PortalFrameIndex;
            float started = backdrop.PresentationTime;
            Assert.That(backdrop.PortalFrameRate, Is.EqualTo(60));
            yield return new WaitForSecondsRealtime(1.05f);
            float advanced = (backdrop.PresentationTime - started) * 60;
            int frames = (backdrop.PortalFrameIndex - firstFrame + 288) % 288;
            Assert.That(frames, Is.EqualTo(advanced).Within(1.1f));
            Assert.That(background.texture, Is.Not.SameAs(firstTexture), "A page transition must update the texture as well as the UVs.");
            Assert.That(game.Session.GameplayTime, Is.Zero);
        }

        [UnityTest] public IEnumerator GameSpritesOrbitAndRecycleAutomaticallyWithoutChangingCollection()
        {
            yield return null;
            var backdrop = flow.View.GetComponentInChildren<MenuBackdrop>();
            var images = (Image[])backdrop.Orbiters.Clone();
            var beforeSprites = new Sprite[images.Length];
            for (int i = 0; i < images.Length; i++) beforeSprites[i] = images[i].sprite;
            var position = images[0].rectTransform.anchoredPosition;
            var before = (int[])station.Progress.owned.Clone();
            Assert.That(backdrop.SpriteVariantCount, Is.GreaterThan(40));
            bool readable = false;
            foreach (var image in images) readable |= image.color.a > .9f;
            Assert.That(readable, Is.True, "Sprites must retain their original colors between entry and exit fades.");
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(images[0].rectTransform.anchoredPosition, Is.Not.EqualTo(position));
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.That(backdrop.RecycledCount, Is.GreaterThan(0), "New sprites must appear without any click or drag.");
            bool replaced = false;
            for (int i = 0; i < images.Length; i++) replaced |= images[i].sprite != beforeSprites[i];
            Assert.That(replaced, Is.True);
            CollectionAssert.AreEqual(images, backdrop.Orbiters, "The visual pool must be reused.");
            Assert.That(game.Session.GameplayTime, Is.Zero);
            CollectionAssert.AreEqual(before, station.Progress.owned);
        }

        [UnityTest] public IEnumerator OrbitSpritesNeverCaptureInputAndReducedEffectsFreezeThem()
        {
            yield return null;
            var backdrop = flow.View.GetComponentInChildren<MenuBackdrop>();
            Assert.That(backdrop.GetComponentsInChildren<Selectable>(), Is.Empty);
            foreach (var graphic in backdrop.GetComponentsInChildren<Graphic>()) Assert.That(graphic.raycastTarget, Is.False);
            game.Hud.SetReducedEffects(true);
            var image = backdrop.Orbiters[1];
            var position = image.rectTransform.anchoredPosition;
            var rotation = image.rectTransform.localRotation;
            var sprite = image.sprite;
            int recycled = backdrop.RecycledCount;
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(image.rectTransform.anchoredPosition, Is.EqualTo(position));
            Assert.That(image.rectTransform.localRotation, Is.EqualTo(rotation));
            Assert.That(image.sprite, Is.SameAs(sprite));
            Assert.That(backdrop.RecycledCount, Is.EqualTo(recycled));
            flow.OpenConfiguration(); flow.CloseConfiguration();
            EnterVault(); flow.ShowMainMenu();
            Assert.That(flow.View.GetComponentInChildren<MenuBackdrop>(), Is.SameAs(backdrop));
            Assert.That(backdrop.Orbiters[1], Is.SameAs(image));
        }

        [UnityTest] public IEnumerator BuildEntryShowsTitleWithoutLoadingArena()
        {
            Assert.That(SceneUtility.GetScenePathByBuildIndex(0), Does.EndWith("MainMenu.unity"));
            yield return SceneManager.LoadSceneAsync("MainMenu");
            game = null;
            yield return null;
            var title = Object.FindAnyObjectByType<MainMenuController>();
            Assert.That(title, Is.Not.Null);
            Assert.That(title.View.IsTitleVisible, Is.True);
            Assert.That(Object.FindAnyObjectByType<CombatGameBootstrap>(), Is.Null);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MainMenu"));
        }

        [UnityTest] public IEnumerator StartingFromTitleBuildsCompanionUiBeforeEnteringTheVault()
        {
            bool saveProgress = SceneTransitionState.SaveProgress;
            bool hadCollection = SceneTransitionState.HasTransientCollection;
            string previousCollection = SceneTransitionState.FusionSnapshot;
            SceneTransitionState.SaveProgress = false;
            SceneTransitionState.HasTransientCollection = false;
            try
            {
                yield return SceneManager.LoadSceneAsync("MainMenu");
                game = null;
                yield return null;
                Object.FindAnyObjectByType<MainMenuController>().RequestNewCollection();

                float deadline = Time.realtimeSinceStartup + 30f;
                while (LoadingScreen.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsFalse(LoadingScreen.IsLoading, "The title transition must finish.");
                Bind();
                Assert.That(game.IsSceneEntryReady, Is.True);
                Assert.That(game.Flow.State, Is.EqualTo(GameFlowState.Vault));
                var companions = station.GetComponent<CompanionSystem>();
                Assert.That(companions.Abilities, Is.Not.Null);
                var companionCanvas = station.transform.Find("CompanionCanvas");
                Assert.That(companionCanvas.Find("CompanerosEquipados/Retrato_0/Cara"), Is.Not.Null);

                game.Session.SetManualPause(true);
                yield return null;
                Assert.That(game.Hud.IsPauseVisible, Is.True);
                Assert.That(companionCanvas.Find("EquiparIndicador").gameObject.activeInHierarchy, Is.False);
                Assert.That(companionCanvas.Find("ColeccionarIndicador").gameObject.activeInHierarchy, Is.False);
            }
            finally
            {
                SceneTransitionState.SaveProgress = saveProgress;
                SceneTransitionState.HasTransientCollection = hadCollection;
                SceneTransitionState.FusionSnapshot = previousCollection;
            }
        }
    }
}

