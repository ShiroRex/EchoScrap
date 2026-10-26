using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace EchoScrap.Tests
{
    public sealed class CombatUpgradeDraftTests
    {
        private Scene scene;
        private CombatSession session;
        private PlayerRuntimeStats stats;
        private PlayerHealth health;
        private CombatProgression progression;
        private UpgradeDraftController draft;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            // Input events in these tests must reach the synthetic devices even when the batch
            // editor has no focused Game view, matching the existing locomotion/dash fixtures.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            scene = SceneManager.CreateScene($"UpgradeDraft_{Random.Range(10000, 99999)}");
            SceneManager.SetActiveScene(scene);
            session = new GameObject("CombatSession").AddComponent<CombatSession>();
            var player = new GameObject("Player");
            player.AddComponent<CapsuleCollider>();
            stats = player.AddComponent<PlayerRuntimeStats>();
            health = player.AddComponent<PlayerHealth>();
            health.BindSession(session);
            session.ResolvePlayerReferences();
            progression = new GameObject("Progression").AddComponent<CombatProgression>();
            progression.BindSession(session);
            draft = new GameObject("UpgradeDraft").AddComponent<UpgradeDraftController>();
            draft.BindReferences(session, stats, health, progression);
            session.ResetSession(101010);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
            if (scene.IsValid() && scene.isLoaded)
            {
                if (SceneManager.sceneCount <= 1)
                    SceneManager.SetActiveScene(SceneManager.CreateScene("UpgradeDraftCleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [Test]
        public void CatalogPreviewsDoNotMutateStats_AndAllMaxTierFormulasAgree()
        {
            Assert.That(UpgradeCatalog.Cards.Count, Is.EqualTo(35));
            var ids = new HashSet<string>();
            foreach (var card in UpgradeCatalog.Cards)
            {
                Assert.That(ids.Add(card.Id), Is.True);
                var offer = new UpgradeOffer(card, stats);
                Assert.That(offer.CurrentTier, Is.Zero);
                Assert.That(offer.NextTier, Is.EqualTo(1));
                Assert.That(offer.Before, Is.Not.Empty);
                Assert.That(offer.After, Is.Not.Empty.And.Not.EqualTo(offer.Before));
                Assert.That(stats.GetUpgradeTier(card.Id), Is.Zero, "Preview must never acquire the card.");
            }
            foreach (var card in UpgradeCatalog.Cards) stats.SetUpgradeTier(card.Id, card.MaxTier);
            Assert.That(stats.FlashlightRange, Is.EqualTo(19f).Within(0.0001f));
            Assert.That(stats.FlashlightAngle, Is.EqualTo(90f).Within(0.0001f));
            Assert.That(stats.FlashlightDamagePerPulse, Is.EqualTo(20.625f).Within(0.0001f));
            Assert.That(stats.FlashlightKnockback, Is.EqualTo(30f));
            Assert.That(stats.EffectivePulseInterval, Is.EqualTo(0.2f / 1.75f).Within(0.0001f));
            Assert.That(stats.EffectiveBurstRestDuration, Is.Zero);
            Assert.That(stats.DashSpeed, Is.EqualTo(30.8f).Within(0.0001f));
            Assert.That(stats.DashCooldown, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(stats.MaxHealth, Is.EqualTo(250f));
            Assert.That(stats.MagnetRadius, Is.EqualTo(12.5f));
            Assert.That(stats.MedkitHealAmount, Is.EqualTo(21f));
            Assert.That(stats.GetMedkitDropChance(false), Is.EqualTo(0.06f));
            Assert.That(stats.GetMedkitDropChance(true), Is.EqualTo(0.15f));
        }

        [Test]
        public void OffersAreUniqueAndStable_AndResetReproducesCombatRngWithoutPresentationDraws()
        {
            int presentationSamples = session.Rng.Presentation.SampleCount;
            Assert.That(progression.AddExperience(100), Is.EqualTo(1));
            Assert.That(draft.IsOpen, Is.True);
            Assert.That(draft.Offers.Count, Is.EqualTo(3));
            var ids = new HashSet<string>();
            foreach (var offer in draft.Offers) Assert.That(ids.Add(offer.Card.Id), Is.True);
            string first = OfferIds();
            int combatSamples = session.Rng.Combat.SampleCount;
            draft.RefreshPendingChoices();
            draft.BindReferences(session, stats, health, progression);
            Assert.That(OfferIds(), Is.EqualTo(first));
            Assert.That(session.Rng.Combat.SampleCount, Is.EqualTo(combatSamples), "Repeated refresh cannot reroll.");
            Assert.That(session.Rng.Presentation.SampleCount, Is.EqualTo(presentationSamples));
            session.ResetSession(101010);
            Assert.That(draft.IsOpen, Is.False);
            Assert.That(draft.Offers.Count, Is.Zero);
            Assert.That(progression.PendingChoices, Is.Zero);
            Assert.That(progression.AddExperience(100), Is.EqualTo(1));
            Assert.That(OfferIds(), Is.EqualTo(first));
        }

        [Test]
        public void QueuedChoicesKeepTacticalPause_AndChoosingLastPreservesManualAndFocusPause()
        {
            Assert.That(progression.AddExperience(500), Is.EqualTo(3));
            Assert.That(draft.PendingChoices, Is.EqualTo(3));
            Assert.That(session.Clock.HasPauseReason(PauseReason.TacticalDraft), Is.True);
            int unpausedEvents = 0;
            session.Clock.OnPauseStateChanged += (_, paused) => { if (!paused) unpausedEvents++; };
            session.SetManualPause(true);
            session.SimulateFocusChange(false);
            for (int remaining = 2; remaining >= 0; remaining--)
            {
                Assert.That(draft.TryChoose(0), Is.True);
                Assert.That(draft.PendingChoices, Is.EqualTo(remaining));
                Assert.That(session.IsPaused, Is.True);
                Assert.That(Time.timeScale, Is.Zero);
                if (remaining > 0)
                {
                    Assert.That(draft.IsOpen, Is.True);
                    Assert.That(session.Clock.HasPauseReason(PauseReason.TacticalDraft), Is.True);
                }
            }
            Assert.That(draft.IsOpen, Is.False);
            Assert.That(draft.TryChoose(0), Is.False, "One choice cannot be consumed twice.");
            Assert.That(session.Clock.HasPauseReason(PauseReason.TacticalDraft), Is.False);
            Assert.That(session.Clock.HasPauseReason(PauseReason.Manual), Is.True);
            Assert.That(session.Clock.HasPauseReason(PauseReason.FocusLost), Is.True);
            Assert.That(unpausedEvents, Is.Zero);
            session.ResumeAllUserPauses();
            Assert.That(session.IsPaused, Is.False);
        }

        [Test]
        public void CappedCardsAreExcluded_RaritiesRenormalize_AndSolarNeedsNoPrerequisite()
        {
            // Leave only one rare and the SSR: empty N/SR weights must not produce an invalid offer.
            foreach (var card in UpgradeCatalog.Cards)
                if (card.Id != "FLASH_05" && card.Id != "GREED_03") stats.SetUpgradeTier(card.Id, card.MaxTier);
            progression.AddExperience(100);
            Assert.That(draft.Offers.Count, Is.EqualTo(2));
            Assert.That(new HashSet<string> { draft.Offers[0].Card.Id, draft.Offers[1].Card.Id },
                Is.EquivalentTo(new[] { "FLASH_05", "GREED_03" }));
            int solarIndex = draft.Offers[0].Card.Id == "FLASH_05" ? 0 : 1;
            Assert.That(draft.TryChoose(solarIndex), Is.True);
            Assert.That(stats.IsSolarActive, Is.True);
            session.ResetSession(100);
            foreach (var card in UpgradeCatalog.Cards)
                if (card.Id != "FLASH_05") stats.SetUpgradeTier(card.Id, card.MaxTier);
            // Solar is the sole remaining card even though its own tier starts at zero.
            progression.AddExperience(100);
            Assert.That(draft.Offers.Count, Is.EqualTo(1));
            Assert.That(draft.Offers[0].Card.Id, Is.EqualTo("FLASH_05"));
            Assert.That(UpgradeCatalog.RarityWeight(UpgradeRarity.N), Is.EqualTo(60f));
            Assert.That(UpgradeCatalog.RarityWeight(UpgradeRarity.R), Is.EqualTo(25f));
            Assert.That(UpgradeCatalog.RarityWeight(UpgradeRarity.SR), Is.EqualTo(12f));
            Assert.That(UpgradeCatalog.RarityWeight(UpgradeRarity.SSR), Is.EqualTo(3f));

            // Exercise SSR eligibility while all other cards are still tier zero.
            bool solarOfferedWithoutPrerequisites = false;
            for (int seed = 1; seed <= 1024 && !solarOfferedWithoutPrerequisites; seed++)
            {
                session.ResetSession(seed);
                progression.AddExperience(100);
                foreach (var offer in draft.Offers)
                    if (offer.Card.Id == "FLASH_05") solarOfferedWithoutPrerequisites = true;
                Assert.That(stats.LensTier + stats.BallastTier + stats.OverloadTier, Is.Zero);
            }
            Assert.That(solarOfferedWithoutPrerequisites, Is.True);
        }

        [Test]
        public void ExhaustedCatalogOffersContinue_ConsumesQueuedChoicesAndResumes()
        {
            foreach (var card in UpgradeCatalog.Cards) stats.SetUpgradeTier(card.Id, card.MaxTier);
            progression.AddExperience(500);
            Assert.That(draft.IsOpen, Is.True);
            Assert.That(draft.HasAvailableCards, Is.False);
            Assert.That(draft.PendingChoices, Is.EqualTo(3));
            Assert.That(draft.TryChoose(0), Is.False);
            Assert.That(draft.ContinueWithoutUpgrade(), Is.True);
            Assert.That(draft.PendingChoices, Is.Zero);
            Assert.That(draft.IsOpen, Is.False);
            Assert.That(session.IsPaused, Is.False);
            Assert.That(draft.ContinueWithoutUpgrade(), Is.False);
        }

        [Test]
        public void BioAlloyAddsTwentyMaxHealthAndHealsFifteenOnce_AndSelectionCannotExceedTierCap()
        {
            foreach (var card in UpgradeCatalog.Cards)
                if (card.Id != "BIO_01") stats.SetUpgradeTier(card.Id, card.MaxTier);
            health.ForceSetHealthForTesting(70f);
            progression.AddExperience(500);
            Assert.That(draft.Offers[0].Card.Id, Is.EqualTo("BIO_01"));
            int choices = 0;
            draft.OnCardChosen += card => { if (card.Id == "BIO_01") choices++; };
            Assert.That(draft.TryChoose(0), Is.True);
            Assert.That(health.MaxHealth, Is.EqualTo(170f));
            Assert.That(health.CurrentHealth, Is.EqualTo(85f));
            Assert.That(stats.BioAlloyTier, Is.EqualTo(1));
            Assert.That(draft.TryChoose(0), Is.True);
            Assert.That(health.MaxHealth, Is.EqualTo(190f));
            Assert.That(health.CurrentHealth, Is.EqualTo(100f));
            Assert.That(draft.TryChoose(0), Is.True);
            Assert.That(health.MaxHealth, Is.EqualTo(210f));
            Assert.That(health.CurrentHealth, Is.EqualTo(115f));
            Assert.That(choices, Is.EqualTo(3));
            Assert.That(draft.TryChoose(0), Is.False);
        }

        [Test]
        public void ResetDisableAndDefeatClearDraft_AndSubscriptionsDoNotDuplicateChoices()
        {
            progression.AddExperience(100);
            draft.enabled = false;
            Assert.That(draft.IsOpen, Is.False);
            Assert.That(draft.Offers.Count, Is.Zero);
            draft.enabled = true;
            Assert.That(draft.IsOpen, Is.True);
            int chosen = 0;
            draft.OnCardChosen += _ => chosen++;
            Assert.That(draft.TryChoose(0), Is.True);
            Assert.That(chosen, Is.EqualTo(1));
            session.ResetSession(909);
            Assert.That(draft.IsOpen, Is.False);
            Assert.That(draft.Offers.Count, Is.Zero);
            Assert.That(stats.GetUpgradeTier("FLASH_01"), Is.Zero);
            progression.AddExperience(100);
            health.ForceSetHealthForTesting(0f);
            session.ResolveSimulationStepOutcome();
            Assert.That(session.State, Is.EqualTo(SessionState.Defeat));
            Assert.That(draft.IsOpen, Is.False);
            Assert.That(draft.TryChoose(0), Is.False);
            Assert.That(draft.ContinueWithoutUpgrade(), Is.False);
            session.ResetSession(909);
            Assert.That(draft.IsOpen, Is.False);
            Assert.That(session.IsPaused, Is.False);
        }

        [Test]
        public void FocusReturnDuringDraftRestoresKeyboardChoiceWithoutAdvancingGameplay()
        {
            var disabled = new List<InputDevice>();
            foreach (var device in InputSystem.devices)
                if (device.enabled) { disabled.Add(device); InputSystem.DisableDevice(device); }
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                var view = draft.gameObject.AddComponent<UpgradeDraftPresentation>();
                view.BindReferences(session, draft);
                progression.AddExperience(250);
                string offersBefore = OfferIds();
                float gameplayTime = session.GameplayTime;
                session.SendMessage("OnApplicationFocus", false);
                view.SendMessage("OnApplicationFocus", false, SendMessageOptions.DontRequireReceiver);
                view.StepInput(0.016f);
                session.SendMessage("OnApplicationFocus", true);
                view.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);

                Assert.That(session.Clock.HasPauseReason(PauseReason.FocusLost), Is.False,
                    "Returning to the paused draft must restore its controls without an inaccessible Resume button.");
                Assert.That(session.State, Is.EqualTo(SessionState.TacticalDraft));
                Assert.That(Time.timeScale, Is.Zero);
                Assert.That(OfferIds(), Is.EqualTo(offersBefore), "Refocusing cannot reroll the offer.");

                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                view.StepInput(0.016f);
                string chosen = draft.Offers[1].Card.Id;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Digit2));
                InputSystem.Update();
                view.StepInput(0.016f);
                Assert.That(view.IsConfirming, Is.True);
                view.StepPresentation(0.17f);
                Assert.That(stats.GetUpgradeTier(chosen), Is.EqualTo(1));
                Assert.That(progression.PendingChoices, Is.EqualTo(1));
                Assert.That(session.IsPaused, Is.True, "The next queued choice still pauses combat.");
                Assert.That(session.GameplayTime, Is.EqualTo(gameplayTime));
                Assert.That(draft.TryChoose(0), Is.True);
                Assert.That(session.State, Is.EqualTo(SessionState.Running));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                foreach (var device in disabled) if (device.added) InputSystem.EnableDevice(device);
            }
        }

        [Test]
        public void FocusReturnDuringConfirmationFinishesOnceAndPreservesManualPause()
        {
            var view = draft.gameObject.AddComponent<UpgradeDraftPresentation>();
            view.BindReferences(session, draft);
            progression.AddExperience(100);
            string chosen = draft.Offers[0].Card.Id;
            int acquired = 0;
            draft.OnCardChosen += _ => acquired++;
            session.SetManualPause(true);
            Assert.That(view.ConfirmSelection(), Is.True);
            view.StepPresentation(0.08f);
            session.SendMessage("OnApplicationFocus", false);
            view.StepPresentation(1f);
            Assert.That(acquired, Is.Zero, "Unfocused confirmation remains suspended.");

            session.SendMessage("OnApplicationFocus", true);
            view.StepPresentation(0.081f);
            view.StepPresentation(1f);
            Assert.That(acquired, Is.EqualTo(1));
            Assert.That(stats.GetUpgradeTier(chosen), Is.EqualTo(1));
            Assert.That(draft.IsOpen, Is.False);
            Assert.That(session.Clock.HasPauseReason(PauseReason.FocusLost), Is.False);
            Assert.That(session.Clock.HasPauseReason(PauseReason.Manual), Is.True);
            Assert.That(session.State, Is.EqualTo(SessionState.Paused));
            session.ResumeAllUserPauses();
            Assert.That(session.State, Is.EqualTo(SessionState.Running));

            // Outside a tactical choice, returning to the application still requires Resume.
            session.SendMessage("OnApplicationFocus", false);
            session.SendMessage("OnApplicationFocus", true);
            Assert.That(session.Clock.HasPauseReason(PauseReason.FocusLost), Is.True);
            Assert.That(session.IsPaused, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FocusReturnRequiresReleaseBeforeMouseOrGamepadCanConfirm(bool useGamepad)
        {
            var disabled = new List<InputDevice>();
            foreach (var device in InputSystem.devices)
                if (device.enabled) { disabled.Add(device); InputSystem.DisableDevice(device); }
            var mouse = InputSystem.AddDevice<Mouse>();
            var gamepad = InputSystem.AddDevice<Gamepad>();
            try
            {
                var view = draft.gameObject.AddComponent<UpgradeDraftPresentation>();
                view.BindReferences(session, draft);
                progression.AddExperience(100);
                Canvas.ForceUpdateCanvases();
                var card = (RectTransform)view.CanvasRoot.transform.Find("UpgradeCard_1");
                var releasedMouse = new MouseState { position = RectTransformUtility.WorldToScreenPoint(null, card.position) };
                InputSystem.QueueStateEvent(mouse, releasedMouse);
                InputSystem.QueueStateEvent(gamepad, new GamepadState());
                InputSystem.Update();
                view.StepInput(0.016f);
                view.StepInput(0.016f);

                session.SendMessage("OnApplicationFocus", false);
                view.SendMessage("OnApplicationFocus", false, SendMessageOptions.DontRequireReceiver);
                if (useGamepad)
                    InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
                else
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = releasedMouse.position }.WithButton(MouseButton.Left));
                InputSystem.Update();
                session.SendMessage("OnApplicationFocus", true);
                view.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);
                view.StepInput(0.016f);
                Assert.That(view.IsConfirming, Is.False, "The refocus press must not acquire a card.");
                Assert.That(progression.PendingChoices, Is.EqualTo(1));

                InputSystem.QueueStateEvent(mouse, releasedMouse);
                InputSystem.QueueStateEvent(gamepad, new GamepadState());
                InputSystem.Update();
                view.StepInput(0.016f);
                if (useGamepad)
                    InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
                else
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = releasedMouse.position }.WithButton(MouseButton.Left));
                InputSystem.Update();
                view.StepInput(0.016f);
                Assert.That(view.IsConfirming, Is.True, "A fresh press after release must remain responsive.");
                view.StepPresentation(0.17f);
                Assert.That(draft.IsOpen, Is.False);
                Assert.That(session.IsPaused, Is.False);
            }
            finally
            {
                InputSystem.RemoveDevice(gamepad);
                InputSystem.RemoveDevice(mouse);
                foreach (var device in disabled) if (device.added) InputSystem.EnableDevice(device);
            }
        }

        [Test]
        public void PresentationShowsBeforeAfter_NavigatesAndConfirmsLocally_FocusLossSuspendsAnimation()
        {
            var view = draft.gameObject.AddComponent<UpgradeDraftPresentation>();
            view.BindReferences(session, draft);
            progression.AddExperience(250);
            Assert.That(view.CanvasRoot.activeSelf, Is.True);
            Assert.That(view.SelectedIndex, Is.Zero);
            view.MoveSelection(-1);
            Assert.That(view.SelectedIndex, Is.EqualTo(2));
            string id = draft.Offers[2].Card.Id;
            int tier = stats.GetUpgradeTier(id);
            var before = view.CanvasRoot.transform.Find("UpgradeCard_3/Panel/Before").GetComponent<Text>();
            var after = view.CanvasRoot.transform.Find("UpgradeCard_3/Panel/After").GetComponent<Text>();
            Assert.That(before.text, Does.StartWith("AHORA\n"));
            Assert.That(after.text, Does.StartWith("AL ELEGIR\n"));
            float gameTime = session.GameplayTime;
            Assert.That(view.ConfirmSelection(), Is.True);
            Assert.That(view.ConfirmSelection(), Is.False);
            view.StepPresentation(0.08f);
            Assert.That(view.IsConfirming, Is.True);
            Assert.That(stats.GetUpgradeTier(id), Is.EqualTo(tier));
            session.SimulateFocusChange(false);
            view.StepPresentation(1f);
            Assert.That(view.IsConfirming, Is.True);
            Assert.That(stats.GetUpgradeTier(id), Is.EqualTo(tier));
            session.ResumeFromFocusLoss();
            view.StepPresentation(0.081f);
            Assert.That(stats.GetUpgradeTier(id), Is.EqualTo(tier + 1));
            Assert.That(progression.PendingChoices, Is.EqualTo(1));
            Assert.That(session.GameplayTime, Is.EqualTo(gameTime));
            Assert.That(draft.IsOpen, Is.True);
            session.Effects.ReducedEffectsMode = true;
            Assert.That(view.ConfirmSelection(), Is.True);
            view.StepPresentation(0f);
            Assert.That(draft.IsOpen, Is.False);
            Assert.That(view.CanvasRoot.activeSelf, Is.False);
            Assert.That(session.IsPaused, Is.False);
        }

        [Test]
        public void KeyboardGamepadAndMouseCanNavigateAndChoose_StationaryPointerDoesNotStealSelection()
        {
            var disabled = new List<InputDevice>();
            foreach (var device in InputSystem.devices)
                if (device.enabled) { disabled.Add(device); InputSystem.DisableDevice(device); }
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var gamepad = InputSystem.AddDevice<Gamepad>();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                var view = draft.gameObject.AddComponent<UpgradeDraftPresentation>();
                view.BindReferences(session, draft);
                progression.AddExperience(500);
                Canvas.ForceUpdateCanvases();
                var firstCard = (RectTransform)view.CanvasRoot.transform.Find("UpgradeCard_1");
                var mouseState = new MouseState { position = RectTransformUtility.WorldToScreenPoint(null, firstCard.position) };
                InputSystem.QueueStateEvent(mouse, mouseState);
                InputSystem.Update();
                view.StepInput(0.016f); // Arm only after release; cache the pointer on the next input step.
                view.StepInput(0.016f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightArrow));
                InputSystem.Update();
                Assert.That(keyboard.rightArrowKey.isPressed, Is.True, "The synthetic keyboard event must reach the test device.");
                view.StepInput(0.016f);
                Assert.That(view.SelectedIndex, Is.EqualTo(1), "A parked mouse cannot undo keyboard navigation.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.DpadRight));
                InputSystem.Update();
                view.StepInput(0.016f);
                Assert.That(view.SelectedIndex, Is.EqualTo(2));
                string selected = draft.Offers[2].Card.Id;
                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
                InputSystem.Update();
                view.StepInput(0.016f);
                Assert.That(view.IsConfirming, Is.True);
                view.StepPresentation(0.17f);
                Assert.That(stats.GetUpgradeTier(selected), Is.EqualTo(1));
                Assert.That(progression.PendingChoices, Is.EqualTo(2));

                // A held confirm cannot automatically select the next queued offer.
                view.StepInput(0.016f);
                Assert.That(view.IsConfirming, Is.False);
                InputSystem.QueueStateEvent(gamepad, new GamepadState());
                InputSystem.Update();
                view.StepInput(0.016f);
                selected = draft.Offers[1].Card.Id;
                int previousTier = stats.GetUpgradeTier(selected);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Digit2));
                InputSystem.Update();
                view.StepInput(0.016f);
                Assert.That(view.SelectedIndex, Is.EqualTo(1));
                Assert.That(view.IsConfirming, Is.True);
                view.StepPresentation(0.17f);
                Assert.That(stats.GetUpgradeTier(selected), Is.EqualTo(previousTier + 1));

                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                view.StepInput(0.016f);
                Canvas.ForceUpdateCanvases();
                firstCard = (RectTransform)view.CanvasRoot.transform.Find("UpgradeCard_1");
                mouseState.position = RectTransformUtility.WorldToScreenPoint(null, firstCard.position);
                mouseState = mouseState.WithButton(MouseButton.Left);
                selected = draft.Offers[0].Card.Id;
                previousTier = stats.GetUpgradeTier(selected);
                InputSystem.QueueStateEvent(mouse, mouseState);
                InputSystem.Update();
                view.StepInput(0.016f);
                Assert.That(view.IsConfirming, Is.True);
                view.StepPresentation(0.17f);
                Assert.That(stats.GetUpgradeTier(selected), Is.EqualTo(previousTier + 1));
                Assert.That(draft.IsOpen, Is.False);
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(gamepad);
                InputSystem.RemoveDevice(keyboard);
                foreach (var device in disabled) if (device.added) InputSystem.EnableDevice(device);
            }
        }

        [Test]
        public void ConfirmingDraftWithHeldSouthDoesNotQueueDash_ReleaseAndNewPressCanDash()
        {
            var disabled = new List<InputDevice>();
            foreach (var device in InputSystem.devices)
                if (device.enabled) { disabled.Add(device); InputSystem.DisableDevice(device); }
            var gamepad = InputSystem.AddDevice<Gamepad>();
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = actions.AddActionMap("Player");
            map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2").AddBinding("<Gamepad>/leftStick");
            map.AddAction("Dash", InputActionType.Button).AddBinding("<Gamepad>/buttonSouth");
            map.AddAction("Pause", InputActionType.Button).AddBinding("<Gamepad>/start");
            try
            {
                var player = health.gameObject;
                player.SetActive(false);
                var input = player.AddComponent<PlayerInputReader>();
                // Set the serialized asset before Awake, just as a prefab inspector does.
                typeof(PlayerInputReader).GetField("actions", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(input, actions);
                player.SetActive(true);
                input.SendMessage("OnApplicationFocus", true);
                session.ResolvePlayerReferences();
                Assert.That(input.CanRead, Is.True);
                progression.AddExperience(100);
                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
                InputSystem.Update();
                Assert.That(draft.TryChoose(0), Is.True);
                InputSystem.Update();
                Assert.That(input.ConsumeDashRequest(), Is.False);
                InputSystem.QueueStateEvent(gamepad, new GamepadState());
                InputSystem.Update();
                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
                InputSystem.Update();
                Assert.That(gamepad.buttonSouth.isPressed, Is.True);
                Assert.That(input.ConsumeDashRequest(), Is.True, "A fresh press after release must remain responsive.");
            }
            finally
            {
                InputSystem.RemoveDevice(gamepad);
                foreach (var device in disabled) if (device.added) InputSystem.EnableDevice(device);
                Object.DestroyImmediate(actions);
            }
        }

        [UnityTest]
        public IEnumerator PresentationPreviewContainsThreeCardsAndCanBeCapturedDuringTacticalPause()
        {
            var camera = new GameObject("DraftPreviewCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.07f, 0.1f);
            camera.transform.position = new Vector3(0f, 15f, -8f);
            camera.transform.LookAt(Vector3.zero);
            var view = draft.gameObject.AddComponent<UpgradeDraftPresentation>();
            view.BindReferences(session, draft);
            progression.AddExperience(100);
            Assert.That(view.CanvasRoot.activeSelf, Is.True);
            Assert.That(view.CanvasRoot.GetComponentsInChildren<Text>().Length, Is.GreaterThan(20));
            Assert.That(session.Clock.HasPauseReason(PauseReason.TacticalDraft), Is.True);

            // Render the same canvas through the camera so capture also works in batch-mode Play Mode.
            var canvas = view.CanvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            var renderTexture = new RenderTexture(1280, 720, 24);
            var previousTexture = camera.targetTexture;
            var previousActive = RenderTexture.active;
            Texture2D screenshot = null;
            string directory = Path.Combine(Application.dataPath, "..", "Logs", "CombatUpgradeDraft");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "Preview.png");
            try
            {
                camera.targetTexture = renderTexture;
                Canvas.ForceUpdateCanvases();
                yield return null;
                yield return null;
                yield return null;
                RenderTexture.active = renderTexture;
                screenshot = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
                screenshot.Apply();
                File.WriteAllBytes(path, screenshot.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTexture;
                RenderTexture.active = previousActive;
                renderTexture.Release();
                Object.Destroy(renderTexture);
                if (screenshot != null) Object.Destroy(screenshot);
            }
            Assert.That(File.Exists(path), Is.True);
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(10000));
        }

        private string OfferIds()
        {
            string value = string.Empty;
            foreach (var offer in draft.Offers) value += offer.Card.Id + ";";
            return value;
        }

        [Test]
        public void PixelCardSkinContainsFourSlicedFramesTenDistinctIconsAndSpanishFonts()
        {
            UpgradeCardSkin skin = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");
            Assert.That(skin, Is.Not.Null);
            Assert.That(skin.frames.Length, Is.EqualTo(4)); Assert.That(skin.icons.Length, Is.EqualTo(10));
            Assert.That(new HashSet<Sprite>(skin.frames).Count, Is.EqualTo(4));
            Assert.That(skin.frames[0].rect.size, Is.EqualTo(new Vector2(64, 64)));
            Assert.That(skin.frames[1].rect.size, Is.EqualTo(new Vector2(64, 64)));
            Assert.That(skin.frames[2].rect.size, Is.EqualTo(new Vector2(72, 64)), "Preserve the original epic canvas.");
            Assert.That(skin.frames[3].rect.size, Is.EqualTo(new Vector2(64, 70)), "Preserve the original legendary canvas.");
            var icons = new HashSet<Sprite>();
            foreach (Sprite frame in skin.frames)
            {
                Assert.That(frame.border, Is.EqualTo(Vector4.one * 12));
                Assert.That(frame.texture.filterMode, Is.EqualTo(FilterMode.Point));
            }
            foreach (Sprite icon in skin.icons)
            {
                Assert.That(icons.Add(icon), Is.True); Assert.That(icon.rect.size, Is.EqualTo(Vector2.one * 64));
                Assert.That(icon.texture.filterMode, Is.EqualTo(FilterMode.Point));
            }
            Assert.That(skin.regular.name, Does.StartWith("ChakraPetch"));
            foreach (char letter in "áéíóúñÁÉÍÓÚÑ") Assert.That(skin.regular.HasCharacter(letter), Is.True);
        }

        [Test]
        public void SharedIconFamiliesCoverAllThirtyFiveImplementedCards()
        {
            var expected = new HashSet<string>();
            foreach (var group in new[] { ("FLASH", 5), ("DASH", 4), ("BIO", 4), ("GREED", 5), ("CRIT", 5), ("DEMO", 4), ("SCRAP", 4), ("CRYO", 4) })
                for (int i = 1; i <= group.Item2; i++) expected.Add($"{group.Item1}_{i:00}");
            Assert.That(UpgradeCatalog.IconAssignments.Keys, Is.EquivalentTo(expected));
            Assert.That(new HashSet<UpgradeIcon>(UpgradeCatalog.IconAssignments.Values).Count, Is.EqualTo(10));
            foreach (var card in UpgradeCatalog.Cards)
            {
                Assert.That(UpgradeCatalog.TryGetIcon(card.Id, out var icon), Is.True);
                Assert.That(card.Icon, Is.EqualTo(icon));
            }
            Assert.That(UpgradeCatalog.Cards.Count, Is.EqualTo(35));
            Assert.That(UpgradeCatalog.TryGet("CRYO_01", out _), Is.True);
            Assert.That(UpgradeCatalog.TryGetIcon("UNKNOWN", out _), Is.False);
        }

        [UnityTest]
        public IEnumerator SharedGrayscaleTexturesReceiveRarityTintAndRefreshWithoutColorLeak()
        {
            var view = draft.gameObject.AddComponent<UpgradeDraftPresentation>(); view.BindReferences(session, draft);
            progression.AddExperience(100);
            SetVisualOffers("BIO_01", "GREED_03", "FLASH_04"); view.RefreshOffers(); view.StepPresentation(0.8f);
            Image common = view.CanvasRoot.transform.Find("UpgradeCard_1/Panel/TypeIcon").GetComponent<Image>();
            Image rare = view.CanvasRoot.transform.Find("UpgradeCard_2/Panel/TypeIcon").GetComponent<Image>();
            Image epic = view.CanvasRoot.transform.Find("UpgradeCard_3/Panel/TypeIcon").GetComponent<Image>();
            Assert.That(common.sprite, Is.SameAs(rare.sprite), "Health and medkits share one neutral texture.");
            Assert.That(common.color, Is.EqualTo(UpgradeCatalog.RarityColor(UpgradeRarity.N)));
            Assert.That(rare.color, Is.EqualTo(UpgradeCatalog.RarityColor(UpgradeRarity.R)));
            Assert.That(epic.color, Is.EqualTo(UpgradeCatalog.RarityColor(UpgradeRarity.SR)));
            var camera = new GameObject("Grayscale cards preview camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 15f, -8f); camera.transform.LookAt(Vector3.zero);
            Canvas canvas = view.CanvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1f;
            yield return CapturePixelCards(camera, view, "Grayscale_Common_Rare_Epic.png", 1920, 1080);
            Sprite power = epic.sprite;
            SetVisualOffers("FLASH_05", "BIO_01", "GREED_03"); view.RefreshOffers(); view.StepPresentation(0.8f);
            Assert.That(common.sprite, Is.SameAs(power), "Power is shared by epic and legendary upgrades.");
            Assert.That(common.color, Is.EqualTo(UpgradeCatalog.RarityColor(UpgradeRarity.SSR)));
            Assert.That(rare.color, Is.EqualTo(UpgradeCatalog.RarityColor(UpgradeRarity.N)), "Reused card resets its previous rarity tint.");
            Assert.That(epic.color, Is.EqualTo(UpgradeCatalog.RarityColor(UpgradeRarity.R)));
            yield return CapturePixelCards(camera, view, "Grayscale_Legendary.png", 1920, 1080);
        }

        [Test]
        public void RepeatCardShowsGreenUpgradeArrowAndEffectiveHealthComparisonWithoutAcquiringIt()
        {
            stats.SetUpgradeTier("BIO_01", 1);
            var view = draft.gameObject.AddComponent<UpgradeDraftPresentation>(); view.BindReferences(session, draft);
            progression.AddExperience(100);
            SetVisualOffers("BIO_01", "GREED_03", "FLASH_04"); view.RefreshOffers();
            Transform panel = view.CanvasRoot.transform.Find("UpgradeCard_1/Panel");
            Assert.That(panel.parent.Find("UpgradeArrow").gameObject.activeSelf, Is.True);
            Assert.That(panel.parent.Find("NewBadge").gameObject.activeSelf, Is.False);
            Assert.That(panel.Find("Level").GetComponent<Text>().text, Is.EqualTo("NIVEL\n1 > 2"));
            Assert.That(panel.Find("Before").GetComponent<Text>().text, Does.Contain("120 HP"));
            Assert.That(panel.Find("After").GetComponent<Text>().text, Does.Contain("140 HP"));
            Assert.That(view.CanvasRoot.transform.Find("UpgradeCard_2/UpgradeArrow").gameObject.activeSelf, Is.False);
            Assert.That(view.CanvasRoot.transform.Find("UpgradeCard_2/NewBadge").gameObject.activeSelf, Is.True);
            Assert.That(stats.GetUpgradeTier("BIO_01"), Is.EqualTo(1));
            session.Effects.ReducedEffectsMode = true; view.StepPresentation(0.5f);
            Vector3 rest = panel.parent.localPosition;
            view.StepPresentation(0.1f);
            Assert.That(panel.parent.localPosition, Is.EqualTo(rest));
            Assert.That(panel.parent.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(panel.parent.localScale, Is.EqualTo(Vector3.one));
        }

        [UnityTest]
        public IEnumerator PixelCardsRenderEveryRarityAndPreserveCornersWhenResized()
        {
            stats.SetUpgradeTier("BIO_01", 1); stats.SetUpgradeTier("DASH_01", 2);
            var view = draft.gameObject.AddComponent<UpgradeDraftPresentation>(); view.BindReferences(session, draft);
            progression.AddExperience(100);
            SetVisualOffers("BIO_01", "GREED_03", "FLASH_04"); view.RefreshOffers(); view.StepPresentation(0.8f);
            var camera = new GameObject("Pixel cards preview camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.03f, 0.04f, 0.06f);
            camera.transform.position = new Vector3(0f, 15f, -8f); camera.transform.LookAt(Vector3.zero);
            Canvas canvas = view.CanvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1f;
            yield return CapturePixelCards(camera, view, "Common_Rare_Epic_1080.png", 1920, 1080);
            Image frame = view.CanvasRoot.transform.Find("UpgradeCard_1/Frame").GetComponent<Image>();
            Mesh mesh;
            Sprite originalFrame = frame.sprite;
            foreach (Sprite rarityFrame in Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin").frames)
            foreach (Vector2 size in new[] { new Vector2(300f, 460f), new Vector2(400f, 620f), new Vector2(480f, 700f) })
            {
                frame.sprite = rarityFrame;
                frame.rectTransform.sizeDelta = size; Canvas.ForceUpdateCanvases(); mesh = frame.canvasRenderer.GetMesh();
                Assert.That(mesh.vertexCount, Is.EqualTo(64), "Nine-slice corners, straight rails and four intact edge ornaments; no center.");
                Assert.That(mesh.vertices[2].x - mesh.vertices[0].x, Is.EqualTo(24f).Within(1f));
                Assert.That(mesh.vertices[1].y - mesh.vertices[0].y, Is.EqualTo(24f).Within(1f));
                Assert.That(mesh.vertices[50].x - mesh.vertices[48].x, Is.EqualTo(80f).Within(1f), "The original top/bottom ornament must not stretch.");
                Assert.That(mesh.vertices[61].y - mesh.vertices[60].y, Is.EqualTo(80f).Within(1f), "The original side ornament must not stretch.");
            }
            frame.sprite = originalFrame;
            frame.rectTransform.sizeDelta = new Vector2(400f, 620f);
            SetVisualOffers("FLASH_05", "DASH_01", "GREED_01"); view.RefreshOffers();
            view.StepPresentation(0.08f);
            Transform legendary = view.CanvasRoot.transform.Find("UpgradeCard_1");
            Assert.That(Quaternion.Angle(legendary.localRotation, Quaternion.identity), Is.GreaterThan(0.05f));
            for (int i = 1; i <= 3; i++)
                Assert.That(view.CanvasRoot.transform.Find($"UpgradeCard_{i}/RarityGlow"), Is.Null, "Cards must have no blurred outer border.");
            view.StepPresentation(0.8f);
            Assert.That(legendary.localRotation, Is.EqualTo(Quaternion.identity));
            yield return CapturePixelCards(camera, view, "Legendary_Dash_Magnet_1080.png", 1920, 1080);
            yield return CapturePixelCards(camera, view, "Legendary_720.png", 1280, 720);
            yield return CapturePixelCards(camera, view, "Legendary_Ultrawide.png", 2560, 1080);
            SetVisualOffers("FLASH_01", "FLASH_02", "GREED_03"); view.RefreshOffers(); view.StepPresentation(0.8f);
            yield return CapturePixelCards(camera, view, "Range_Cadence_Supplies_1080.png", 1920, 1080);
            // Warm then measure the real presentation and canvas rebuild, with the same paused UI.
            for (int i = 0; i < 10; i++) { view.StepPresentation(1f / 60f); Canvas.ForceUpdateCanvases(); }
            var watch = new System.Diagnostics.Stopwatch();
            for (int i = 0; i < 120; i++)
            {
                watch.Start(); view.StepPresentation(1f / 60f); Canvas.ForceUpdateCanvases(); watch.Stop(); yield return null;
            }
            double ms = watch.Elapsed.TotalMilliseconds / 120;
            Debug.Log($"[Pixel cards] Presentation + canvas rebuild: {ms:F3} ms mean over 120 frames.");
            Assert.That(ms, Is.LessThan(16f));
        }

        [Test]
        public void SpaceNeverChoosesACardAfterDashEvenWhenReleasedAndPressedAgain()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); InputSystem.Update();
                var view = draft.gameObject.AddComponent<UpgradeDraftPresentation>(); view.BindReferences(session, draft);
                progression.AddExperience(100);
                SetVisualOffers("BIO_01", "GREED_03", "FLASH_04"); view.RefreshOffers();
                view.StepInput(0.016f);
                for (int i = 0; i < 3; i++)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); view.StepInput(0.016f);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); InputSystem.Update(); view.StepInput(0.016f);
                    view.StepPresentation(0.3f);
                    Assert.That(draft.IsOpen, Is.True); Assert.That(view.IsConfirming, Is.False);
                    Assert.That(stats.GetUpgradeTier("BIO_01"), Is.Zero);
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); view.StepInput(0.016f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter)); InputSystem.Update(); view.StepInput(0.016f);
                Assert.That(view.IsConfirming, Is.True); view.StepPresentation(0.3f);
                Assert.That(stats.GetUpgradeTier("BIO_01"), Is.EqualTo(1));
            }
            finally { InputSystem.RemoveDevice(keyboard); }
        }

        [Test]
        public void EpicCopyExplainsTheTradeoffAndDoesNotRepeatTheAreaPenalty()
        {
            UpgradeCatalog.TryGet("FLASH_04", out var card);
            var first = new UpgradeOffer(card, stats);
            Assert.That(first.Detail, Does.StartWith("Menos área de ataque."));
            Assert.That(first.Detail, Does.Not.Contain("°").And.Not.Contain("Cono"));
            float originalAngle = stats.FlashlightAngle;
            Assert.That(stats.GetUpgradeTier(card.Id), Is.Zero);
            stats.SetUpgradeTier(card.Id, 1);
            Assert.That(stats.FlashlightAngle, Is.EqualTo(originalAngle - 20f));
            var repeat = new UpgradeOffer(card, stats);
            Assert.That(repeat.Detail, Does.StartWith("Misma área de ataque."));
            Assert.That(repeat.PrimaryBefore, Is.Not.EqualTo(repeat.PrimaryAfter));
            Assert.That(stats.FlashlightAngle, Is.EqualTo(originalAngle - 20f));
            Assert.That(stats.GetUpgradeTier(card.Id), Is.EqualTo(1));
        }

        [Test]
        public void PointerProjectsTheWholeCardKeepsItsHitAreaAndRespectsReducedEffects()
        {
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                var view = draft.gameObject.AddComponent<UpgradeDraftPresentation>(); view.BindReferences(session, draft);
                progression.AddExperience(100); SetVisualOffers("BIO_01", "GREED_03", "FLASH_04");
                view.RefreshOffers(); view.StepPresentation(0.8f); Canvas.ForceUpdateCanvases();
                var root = (RectTransform)view.CanvasRoot.transform.Find("UpgradeCard_1");
                var motion = root.GetComponent<UpgradeCardMotion>();
                var frame = root.Find("Frame").GetComponent<Image>();
                Vector3 corner = frame.canvasRenderer.GetMesh().vertices[0];
                Rect hitRect = root.rect; Vector3 rootPosition = root.position;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(5, 5) }); InputSystem.Update(); view.StepInput(0.016f);
                Vector2 hover = RectTransformUtility.WorldToScreenPoint(null, root.TransformPoint(new Vector3(150f, 0f)));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = hover }); InputSystem.Update(); view.StepInput(0.016f);
                view.StepPresentation(0.3f); Canvas.ForceUpdateCanvases();
                Assert.That(motion.Tilt.x, Is.GreaterThan(0.5f));
                Assert.That(motion.SheenAlpha, Is.GreaterThan(0.01f));
                Assert.That(root.rect, Is.EqualTo(hitRect)); Assert.That(root.position, Is.EqualTo(rootPosition));
                Assert.That(RectTransformUtility.RectangleContainsScreenPoint(root, hover), Is.True);
                Assert.That(Vector3.Distance(frame.canvasRenderer.GetMesh().vertices[0], corner), Is.GreaterThan(1f));
                float near = motion.Project(new Vector3(200, 300)).y - motion.Project(new Vector3(200, -300)).y;
                float far = motion.Project(new Vector3(-200, 300)).y - motion.Project(new Vector3(-200, -300)).y;
                Assert.That(near, Is.GreaterThan(far + 5f), "The near side must grow: actual perspective, not a flat skew.");
                Assert.That(root.GetComponentsInChildren<UpgradeCardPerspective>().Length, Is.GreaterThan(10));
                Assert.That(root.Find("SurfaceSheen").GetComponent<RawImage>().material.shader.isSupported, Is.True);
                long inputAllocations = 0;
                for (int i = 0; i < 30; i++)
                {
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = hover + new Vector2(i * 0.1f, 0f) }); InputSystem.Update();
                    long allocated = System.GC.GetAllocatedBytesForCurrentThread();
                    view.StepInput(0.016f);
                    inputAllocations += System.GC.GetAllocatedBytesForCurrentThread() - allocated;
                }
                Assert.That(inputAllocations, Is.Zero, "Moving inside the selected card must not rebuild labels or allocate each frame.");
                float fullDeformation = Vector3.Distance(frame.canvasRenderer.GetMesh().vertices[0], corner);
                session.Effects.DecorativeIntensity = 0.25f; view.StepPresentation(0.016f); Canvas.ForceUpdateCanvases();
                Assert.That(Vector3.Distance(frame.canvasRenderer.GetMesh().vertices[0], corner), Is.LessThan(fullDeformation * 0.5f));
                session.Effects.DecorativeIntensity = 1f;

                view.MoveSelection(1); view.StepInput(0.016f); view.StepPresentation(1f);
                Assert.That(view.SelectedIndex, Is.EqualTo(1), "A stationary cursor must not steal keyboard/controller selection.");
                Assert.That(motion.Tilt.magnitude, Is.LessThan(0.001f));
                Assert.That(motion.SheenAlpha, Is.LessThan(0.001f));
                motion.SetPointer(new Vector2(0.8f, 0.8f), true); view.StepPresentation(0.3f);
                session.Effects.ReducedEffectsMode = true; view.StepPresentation(0.016f);
                Assert.That(motion.Tilt, Is.EqualTo(Vector2.zero)); Assert.That(motion.SheenAlpha, Is.Zero);
                session.Effects.ReducedEffectsMode = false;
                motion.SetPointer(Vector2.right, true); view.StepPresentation(0.3f);
                session.Effects.DecorativeIntensity = 0f; view.StepPresentation(0.016f);
                Assert.That(motion.Tilt, Is.EqualTo(Vector2.zero)); Assert.That(motion.SheenAlpha, Is.Zero);
                session.Effects.DecorativeIntensity = 1f;
                Vector2 clickPoint = hover + new Vector2(5, 5);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = clickPoint }); InputSystem.Update(); view.StepInput(0.016f);
                view.StepPresentation(0.3f);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = clickPoint }.WithButton(MouseButton.Left)); InputSystem.Update(); view.StepInput(0.016f);
                Assert.That(view.IsConfirming, Is.True);
                view.StepPresentation(0.3f);
                Assert.That(stats.GetUpgradeTier("BIO_01"), Is.EqualTo(1), "A tilted card must still accept the stable click target exactly once.");
            }
            finally { InputSystem.RemoveDevice(mouse); }
        }

        [UnityTest]
        public IEnumerator CardPerspectiveAndSurfaceReflectionRenderWithoutAnOuterHalo()
        {
            stats.SetUpgradeTier("BIO_01", 1);
            var view = draft.gameObject.AddComponent<UpgradeDraftPresentation>(); view.BindReferences(session, draft);
            progression.AddExperience(100); SetVisualOffers("BIO_01", "GREED_03", "FLASH_04");
            view.RefreshOffers(); view.StepPresentation(0.8f);
            view.MoveSelection(1); view.MoveSelection(1);
            var camera = new GameObject("Card surface preview camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.03f, 0.04f, 0.06f);
            Canvas canvas = view.CanvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1f;
            var root = view.CanvasRoot.transform.Find("UpgradeCard_3");
            var motion = root.GetComponent<UpgradeCardMotion>();
            var sheen = root.Find("SurfaceSheen").GetComponent<RawImage>();
            motion.SetPointer(new Vector2(0.75f, 0.6f), true); view.StepPresentation(1f);
            sheen.enabled = false;
            yield return CapturePixelCards(camera, view, "Perspective_NoReflection.png", 1920, 1080);
            sheen.enabled = true;
            yield return CapturePixelCards(camera, view, "Perspective_Reflection.png", 1920, 1080);
            var before = new Texture2D(2, 2); var after = new Texture2D(2, 2);
            string folder = Path.Combine(Application.dataPath, "..", "Logs", "PixelCards");
            before.LoadImage(File.ReadAllBytes(Path.Combine(folder, "Perspective_NoReflection.png")));
            after.LoadImage(File.ReadAllBytes(Path.Combine(folder, "Perspective_Reflection.png")));
            Color32[] a = before.GetPixels32(), b = after.GetPixels32(); int brighter = 0;
            for (int i = 0; i < a.Length; i++)
                if (b[i].r > a[i].r + 2 && b[i].g > a[i].g + 2) brighter++;
            Assert.That(brighter, Is.InRange(500, 150000), "The shader must visibly reflect light on the card surface.");
            Object.Destroy(before); Object.Destroy(after);

            double elapsed = 0;
            var watch = new System.Diagnostics.Stopwatch();
            for (int i = 0; i < 120; i++)
            {
                motion.SetPointer(new Vector2(Mathf.Sin(i * 0.08f) * 0.8f, Mathf.Cos(i * 0.08f) * 0.6f), true);
                watch.Start(); view.StepPresentation(1f / 60f); Canvas.ForceUpdateCanvases(); watch.Stop();
                if (i % 4 == 0) yield return CapturePixelCards(camera, view, $"Motion_{i / 4:D2}.png", 1280, 720);
                yield return null;
            }
            elapsed = watch.Elapsed.TotalMilliseconds / 120;
            Debug.Log($"[Card perspective] Animated presentation + canvas: {elapsed:F3} ms mean over 120 frames.");
            Assert.That(elapsed, Is.LessThan(16f));
        }

        [UnityTest]
        public IEnumerator FrameNormalsRespondToLightAndTheRarestOfferHasMoreSpecular()
        {
            var view = draft.gameObject.AddComponent<UpgradeDraftPresentation>(); view.BindReferences(session, draft);
            progression.AddExperience(100); SetVisualOffers("BIO_01", "GREED_03", "FLASH_04"); view.RefreshOffers(); view.StepPresentation(0.8f);
            var camera = new GameObject("Normal frame preview camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var canvas = view.CanvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1f;
            var common = view.CanvasRoot.transform.Find("UpgradeCard_1/Frame").GetComponent<Image>();
            var epic = view.CanvasRoot.transform.Find("UpgradeCard_3/Frame").GetComponent<Image>();
            Assert.That(epic.material.GetFloat("_Emphasis"), Is.GreaterThan(common.material.GetFloat("_Emphasis") * 2));
            var skin = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");
            foreach (var material in skin.litFrames)
            {
                var normal = (Texture2D)material.GetTexture("_NormalMap");
                Assert.That(material.shader.isSupported, Is.True); Assert.That(normal, Is.Not.Null);
                Assert.That(normal.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(normal.graphicsFormat), Is.False);
            }
            view.enabled = false; view.CanvasRoot.SetActive(true);
            epic.material.SetFloat("_Emphasis", 0.4f);
            epic.material.SetVector("_LightDirection", new Vector4(-0.8f, 0.3f, 0.6f, 0));
            yield return CapturePixelCards(camera, view, "FrameLight_Left.png", 1920, 1080);
            epic.material.SetVector("_LightDirection", new Vector4(0.8f, 0.3f, 0.6f, 0));
            yield return CapturePixelCards(camera, view, "FrameLight_Right.png", 1920, 1080);
            string folder = Path.Combine(Application.dataPath, "..", "Logs", "PixelCards");
            var left = new Texture2D(2, 2); var right = new Texture2D(2, 2);
            left.LoadImage(File.ReadAllBytes(Path.Combine(folder, "FrameLight_Left.png")));
            right.LoadImage(File.ReadAllBytes(Path.Combine(folder, "FrameLight_Right.png")));
            Color32[] a = left.GetPixels32(), b = right.GetPixels32(); int changed = 0;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 15) changed++;
            Assert.That(changed, Is.InRange(200, 40000), "Opposite lights must reveal the frame bevels without changing the whole card.");
            Object.Destroy(left); Object.Destroy(right);
        }

        [UnityTest]
        public IEnumerator CardCentersAndButtonsRemainSymmetricWithProportionalSelection()
        {
            var view=draft.gameObject.AddComponent<UpgradeDraftPresentation>();view.BindReferences(session,draft);progression.AddExperience(100);
            stats.SetUpgradeTier("SCRAP_02",1);stats.SetUpgradeTier("GREED_04",2);
            SetVisualOffers("SCRAP_02","GREED_04","CRIT_04");view.RefreshOffers();view.StepPresentation(.8f);Canvas.ForceUpdateCanvases();
            for(int selection=0;selection<3;selection++)
            {
                if(selection>0)view.MoveSelection(1);
                view.StepPresentation(.8f);Canvas.ForceUpdateCanvases();
                for(int i=0;i<3;i++)
                {
                    var root=(RectTransform)view.CanvasRoot.transform.Find($"UpgradeCard_{i+1}");
                    Assert.That(root.rect.size,Is.EqualTo(new Vector2(400,620)));
                    Assert.That(root.anchoredPosition,Is.EqualTo(new Vector2((i-1)*456,-20)));
                    float scale=i==selection?1.018f:1f;
                    Assert.That(root.localScale.x,Is.EqualTo(scale).Within(.0001));
                    Assert.That(root.localScale.y,Is.EqualTo(scale).Within(.0001));
                    foreach(string centered in new[]{"RarityTier","TypeIcon","Type","Name","Description","PrimaryLabel","ComparisonArrow","Detail","ChooseButton"})
                    {
                        var rect=(RectTransform)root.Find("Panel/"+centered);
                        Assert.That(rect.anchoredPosition.x,Is.Zero,centered+" must share the frame center.");
                        Assert.That(root.InverseTransformPoint(rect.position).x,Is.Zero.Within(.0001),centered);
                    }
                    var before=(RectTransform)root.Find("Panel/Before");var after=(RectTransform)root.Find("Panel/After");
                    Assert.That(before.anchoredPosition.x,Is.EqualTo(-after.anchoredPosition.x));
                    Assert.That(before.anchoredPosition.y,Is.EqualTo(after.anchoredPosition.y));
                    var button=(RectTransform)root.Find("Panel/ChooseButton");
                    Assert.That(button.anchoredPosition,Is.EqualTo(new Vector2(0,-272)));
                    Assert.That(button.rect.size,Is.EqualTo(new Vector2(280,40)));
                    Assert.That(root.Find("NewBadge").GetComponent<UpgradeCardPerspective>(),Is.Not.Null);
                    Assert.That(root.Find("UpgradeArrow").GetComponent<UpgradeCardPerspective>(),Is.Not.Null);
                }
            }
            foreach(string name in new[]{"Title","Subtitle","RerollButton"})
                Assert.That(((RectTransform)view.CanvasRoot.transform.Find(name)).anchoredPosition.x,Is.Zero,name);
            var camera=new GameObject("Centered cards camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.035f,.05f);
            camera.transform.position=new Vector3(0,15,-8);camera.transform.LookAt(Vector3.zero);
            var canvas=view.CanvasRoot.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            view.MoveSelection(1);view.StepPresentation(.8f);
            yield return CapturePixelCards(camera,view,"Centered_Repeat_720.png",1280,720);
            yield return CapturePixelCards(camera,view,"Centered_Repeat_1040.png",1040,664);
            yield return CapturePixelCards(camera,view,"Centered_Repeat_Ultrawide.png",2560,1080);
            SetVisualOffers("CRYO_01","DEMO_02","CRIT_03");view.RefreshOffers();view.MoveSelection(1);view.StepPresentation(.8f);
            Assert.That(view.ConfirmSelection(),Is.True);view.StepPresentation(.08f);Canvas.ForceUpdateCanvases();
            var middle=(RectTransform)view.CanvasRoot.transform.Find("UpgradeCard_2");
            Assert.That(middle.localScale.x,Is.EqualTo(1.018f*1.045f).Within(.0001));
            var rerollBounds=RectTransformUtility.CalculateRelativeRectTransformBounds(view.CanvasRoot.transform,view.CanvasRoot.transform.Find("RerollButton"));
            var labelBounds=RectTransformUtility.CalculateRelativeRectTransformBounds(view.CanvasRoot.transform,middle.Find("NewBadge"));
            Assert.That(rerollBounds.Intersects(labelBounds),Is.False,"The larger confirmation must not collide with the header.");
        }

        [UnityTest]
        public IEnumerator CornerLabelsAndAllThirtyFiveCardsFitAtEveryOfferedTier()
        {
            var skin = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");
            Assert.That(skin.newBadge, Is.Not.Null); Assert.That(skin.upgradeBadge, Is.Not.Null);
            Assert.That(skin.newBadge.rect.size, Is.EqualTo(new Vector2(48,20)));
            Assert.That(skin.upgradeBadge.rect.size, Is.EqualTo(new Vector2(32,32)));
            foreach(var sprite in new[]{skin.newBadge,skin.upgradeBadge})
            {
                Assert.That(sprite.texture.filterMode,Is.EqualTo(FilterMode.Point));
                Assert.That(sprite.texture.mipmapCount,Is.EqualTo(1));
            }
            var view=draft.gameObject.AddComponent<UpgradeDraftPresentation>();view.BindReferences(session,draft);
            progression.AddExperience(100);yield return null;
            int checkedOffers=0;
            foreach(bool combined in new[]{false,true})
            foreach(var card in UpgradeCatalog.Cards)
            for(int tier=0;tier<card.MaxTier;tier++)
            {
                stats.ResetToInitial();
                if(combined)foreach(var other in UpgradeCatalog.Cards)stats.SetUpgradeTier(other.Id,other.MaxTier);
                stats.SetUpgradeTier(card.Id,tier);
                SetVisualOffers(card.Id);view.RefreshOffers();view.StepPresentation(.8f);Canvas.ForceUpdateCanvases();
                var root=view.CanvasRoot.transform.Find("UpgradeCard_1");var panel=root.Find("Panel");
                Assert.That(root.Find("NewBadge").gameObject.activeSelf,Is.EqualTo(tier==0),card.Id);
                Assert.That(root.Find("UpgradeArrow").gameObject.activeSelf,Is.EqualTo(tier>0),card.Id);
                Assert.That(panel.Find("Level").GetComponent<Text>().text,Is.EqualTo(tier==0?$"NIVEL\n1 / {card.MaxTier}":$"NIVEL\n{tier} > {tier+1}"));
                Assert.That(root.Find("NewBadge").GetComponent<Image>().sprite,Is.SameAs(skin.newBadge));
                Assert.That(root.Find("UpgradeArrow").GetComponent<Image>().sprite,Is.SameAs(skin.upgradeBadge));
                Assert.That(root.Find("NewBadge").localPosition.x,Is.LessThan(-100));
                Assert.That(root.Find("UpgradeArrow").localPosition.x,Is.GreaterThan(190));
                foreach(var text in panel.GetComponentsInChildren<Text>())
                    Assert.That(text.preferredHeight,Is.LessThanOrEqualTo(text.rectTransform.rect.height+.5f),card.Id+" tier "+tier+" / "+text.name+" / "+text.text);
                foreach(var pair in new[]{("Type","Name"),("Name","Description"),("Description","PrimaryLabel"),("PrimaryLabel","Before"),("Before","Detail"),("Detail","ChooseButton")})
                {
                    var upper=(RectTransform)panel.Find(pair.Item1);var lower=(RectTransform)panel.Find(pair.Item2);
                    Assert.That(upper.anchoredPosition.y-upper.rect.height*.5f,Is.GreaterThanOrEqualTo(lower.anchoredPosition.y+lower.rect.height*.5f),card.Id+" overlap "+pair);
                }
                checkedOffers++;
            }
            Debug.Log($"[Card labels] {checkedOffers} offers checked across all 35 cards, their levels and combined upgrades.");
        }

        [UnityTest]
        public IEnumerator StickerLabelsAndLongNamesRemainReadableAcrossGameViewSizes()
        {
            var view=draft.gameObject.AddComponent<UpgradeDraftPresentation>();view.BindReferences(session,draft);progression.AddExperience(100);
            var camera=new GameObject("Label preview camera").AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.035f,.05f);
            camera.transform.position=new Vector3(0,15,-8);camera.transform.LookAt(Vector3.zero);
            var canvas=view.CanvasRoot.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            stats.SetUpgradeTier("GREED_04",1);
            SetVisualOffers("CRYO_01","DEMO_02","CRIT_03");view.RefreshOffers();view.StepPresentation(.8f);
            yield return CapturePixelCards(camera,view,"Labels_Reported_1040.png",1040,664);
            yield return CapturePixelCards(camera,view,"Labels_Reported_1080.png",1920,1080);
            var reroll=view.CanvasRoot.transform.Find("RerollButton");
            var rerollBounds=RectTransformUtility.CalculateRelativeRectTransformBounds(view.CanvasRoot.transform,reroll);
            for(int i=1;i<=3;i++)
                Assert.That(rerollBounds.Intersects(RectTransformUtility.CalculateRelativeRectTransformBounds(view.CanvasRoot.transform,view.CanvasRoot.transform.Find($"UpgradeCard_{i}/NewBadge"))),Is.False,"The reroll must not cover a corner sticker.");
            stats.SetUpgradeTier("SCRAP_02",1);stats.SetUpgradeTier("GREED_04",2);
            SetVisualOffers("SCRAP_02","GREED_04","CRIT_04");view.RefreshOffers();view.StepPresentation(.8f);
            yield return CapturePixelCards(camera,view,"Labels_Repeat_720.png",1280,720);
            yield return CapturePixelCards(camera,view,"Labels_Repeat_1080.png",1920,1080);
        }

        private void SetVisualOffers(params string[] ids)
        {
            var offers = (List<UpgradeOffer>)typeof(UpgradeDraftController).GetField("offers", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(draft);
            offers.Clear();
            foreach (string id in ids) { Assert.That(UpgradeCatalog.TryGet(id, out var card), Is.True); offers.Add(new UpgradeOffer(card, stats)); }
        }

        private static IEnumerator CapturePixelCards(Camera camera, UpgradeDraftPresentation view, string name, int width, int height)
        {
            var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
            yield return null; yield return null; yield return null;
            Canvas.ForceUpdateCanvases();
            foreach (Text text in view.CanvasRoot.GetComponentsInChildren<Text>())
                Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 1f), "Text must fit: " + text.name + " / " + text.text);
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
            string output = Path.Combine(Application.dataPath, "..", "Logs", "PixelCards"); Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous;
            target.Release(); Object.Destroy(target); Object.Destroy(texture);
        }
    }
}
