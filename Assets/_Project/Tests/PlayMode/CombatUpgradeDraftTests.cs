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
        public void CatalogPreviewsDoNotMutateStats_AndAllEightMaxTierFormulasAgree()
        {
            Assert.That(UpgradeCatalog.Cards.Count, Is.EqualTo(8));
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
            Assert.That(stats.MaxHealth, Is.EqualTo(200f));
            Assert.That(stats.MagnetRadius, Is.EqualTo(12.5f));
            Assert.That(stats.MedkitHealAmount, Is.EqualTo(25f));
            Assert.That(stats.GetMedkitDropChance(false), Is.EqualTo(0.14f));
            Assert.That(stats.GetMedkitDropChance(true), Is.EqualTo(0.60f));
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
            for (int seed = 1; seed <= 128 && !solarOfferedWithoutPrerequisites; seed++)
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
            Assert.That(health.MaxHealth, Is.EqualTo(120f));
            Assert.That(health.CurrentHealth, Is.EqualTo(85f));
            Assert.That(stats.BioAlloyTier, Is.EqualTo(1));
            Assert.That(draft.TryChoose(0), Is.True);
            Assert.That(health.MaxHealth, Is.EqualTo(140f));
            Assert.That(health.CurrentHealth, Is.EqualTo(100f));
            Assert.That(draft.TryChoose(0), Is.True);
            Assert.That(health.MaxHealth, Is.EqualTo(160f));
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
    }
}
