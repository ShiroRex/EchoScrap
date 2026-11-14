using System;
using System.Collections;
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
    public sealed class SaveSlotFlowTests
    {
        private string tempRoot;
        private string prevProdSave, prevLegacySave, prevProfile, prevName, prevNameTest, prevRank, prevRankTest;
        private bool hadProdSave, hadLegacySave, hadProfile, hadName, hadNameTest, hadRank, hadRankTest;
        private MainMenuController Menu => Object.FindAnyObjectByType<MainMenuController>();
        private CombatGameBootstrap Game => Object.FindAnyObjectByType<CombatGameBootstrap>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "EchoScrapFlowTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            BackupAndClearLegacyPrefs();
            SceneTransitionState.Reset();
            SceneTransitionState.SaveProgress = true;
            SaveSlotService.SetStorageRootForTesting(tempRoot);
            SaveSlotService.SetPersistenceOverride(true);
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Assert.IsNotNull(Menu);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var active = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("SaveSlotFlowCleanup"));
            yield return SceneManager.UnloadSceneAsync(active);
            SaveSlotService.SetPersistenceOverride(null);
            SaveSlotService.SetStorageRootForTesting(null);
            SceneTransitionState.Reset();
            RestoreLegacyPrefs();
            CombatTargetRegistry.Clear();
            Time.timeScale = 1f;
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
            catch (Exception) { }
        }

        private void BackupAndClearLegacyPrefs()
        {
            hadProdSave = PlayerPrefs.HasKey(FusionStation.SaveKey);
            prevProdSave = PlayerPrefs.GetString(FusionStation.SaveKey, "");
            hadLegacySave = PlayerPrefs.HasKey("EchoScrap.Collection.v1");
            prevLegacySave = PlayerPrefs.GetString("EchoScrap.Collection.v1", "");
            hadProfile = PlayerPrefs.HasKey(GameFlowController.ProfileKey);
            prevProfile = PlayerPrefs.GetString(GameFlowController.ProfileKey, "");
            hadName = PlayerPrefs.HasKey(EventLeaderboardSync.PlayerNamePrefKey);
            prevName = PlayerPrefs.GetString(EventLeaderboardSync.PlayerNamePrefKey, "");
            hadNameTest = PlayerPrefs.HasKey(EventLeaderboardSync.PlayerNamePrefKey + ".Test");
            prevNameTest = PlayerPrefs.GetString(EventLeaderboardSync.PlayerNamePrefKey + ".Test", "");
            hadRank = PlayerPrefs.HasKey("EchoScrap.Rank.v2");
            prevRank = PlayerPrefs.GetString("EchoScrap.Rank.v2", "");
            hadRankTest = PlayerPrefs.HasKey("EchoScrap.Rank.Test.v2");
            prevRankTest = PlayerPrefs.GetString("EchoScrap.Rank.Test.v2", "");

            PlayerPrefs.DeleteKey(FusionStation.SaveKey);
            PlayerPrefs.DeleteKey("EchoScrap.Collection.v1");
            PlayerPrefs.DeleteKey(GameFlowController.ProfileKey);
            PlayerPrefs.DeleteKey(EventLeaderboardSync.PlayerNamePrefKey);
            PlayerPrefs.DeleteKey(EventLeaderboardSync.PlayerNamePrefKey + ".Test");
            PlayerPrefs.DeleteKey("EchoScrap.Rank.v2");
            PlayerPrefs.DeleteKey("EchoScrap.Rank.Test.v2");
            PlayerPrefs.Save();
        }

        private void RestoreLegacyPrefs()
        {
            RestoreKey(FusionStation.SaveKey, hadProdSave, prevProdSave);
            RestoreKey("EchoScrap.Collection.v1", hadLegacySave, prevLegacySave);
            RestoreKey(GameFlowController.ProfileKey, hadProfile, prevProfile);
            RestoreKey(EventLeaderboardSync.PlayerNamePrefKey, hadName, prevName);
            RestoreKey(EventLeaderboardSync.PlayerNamePrefKey + ".Test", hadNameTest, prevNameTest);
            RestoreKey("EchoScrap.Rank.v2", hadRank, prevRank);
            RestoreKey("EchoScrap.Rank.Test.v2", hadRankTest, prevRankTest);
            PlayerPrefs.Save();
        }

        private static void RestoreKey(string key, bool had, string val)
        {
            if (had) PlayerPrefs.SetString(key, val);
            else PlayerPrefs.DeleteKey(key);
        }

        private IEnumerator WaitForTitle()
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while ((Menu == null || LoadingScreen.IsLoading) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsNotNull(Menu);
            yield return null;
        }

        private IEnumerator WaitForWorld()
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while ((Game == null || Game.Flow == null || LoadingScreen.IsLoading) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsNotNull(Game);
            Assert.IsNotNull(Game.Flow);
            Game.Director.AutoTick = Game.Encounter.AutoTick = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ContinueButtonStartsDisabledAndEnablesOnlyForValidOrRecoverableSlots()
        {
            Assert.IsFalse(Menu.View.ContinueButton.interactable);
            Assert.AreEqual("CONTINUAR", Menu.View.ContinueButton.GetComponentInChildren<Text>().text);
            Assert.AreEqual("NUEVA PARTIDA", Menu.View.NewGameButton.GetComponentInChildren<Text>().text);

            // Only a corrupted slot on disk -> Continue must remain disabled.
            File.WriteAllText(SaveSlotService.GetSlotFilePath(0), "{corrupt-primary");
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return WaitForTitle();
            Assert.IsFalse(Menu.View.ContinueButton.interactable);
            Assert.AreEqual("Archivo dañado", SaveSlotService.GetSlotSummaries()[0].StatusLabel);

            // Add a valid slot with damaged expedition recoverable in slot 1 -> Continue enables.
            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(1, "VULCANO"));
            SaveSlotService.MarkSlotLoadCompleted();
            var header = new ExpeditionSnapshotHeader
            {
                profileId = SaveSlotService.ActiveProfileId,
                attemptId = "att_bad",
                revision = 1,
                modeMinutes = 3,
                gameplayTime = 12f
            };
            Assert.IsTrue(SaveSlotService.SetActiveExpeditionState("att_bad", JsonUtility.ToJson(header), 1));
            SaveSlotService.FlushAndSuspendActiveProfile();
            File.WriteAllText(SaveSlotService.GetExpeditionFilePath(1), "broken-expedition");

            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return WaitForTitle();
            Assert.IsTrue(Menu.View.ContinueButton.interactable);
            Assert.AreEqual(SlotHealth.ExpeditionDamagedRecoverable, SaveSlotService.GetSlotSummaries()[1].Health);
        }

        [UnityTest]
        public IEnumerator SlotSelectorFocusesLastPlayedSlotAndHandlesRecoveryAndOverwriteWarnings()
        {
            // Populate Slot 0 ("Práctica", 1 unit) and Slot 2 ("ALFA", 3 units); Slot 2 played last.
            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(0, ""));
            SaveSlotService.MarkSlotLoadCompleted();
            var f0 = SaveSlotService.CloneActiveFusionProgress();
            f0.owned[0] = 1;
            Assert.IsTrue(SaveSlotService.SaveActiveFusionProgress(f0));
            SaveSlotService.FlushAndSuspendActiveProfile(f0);

            // Populate Slot 1 with recoverable damaged expedition and 4 units.
            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(1, "RECUPERA"));
            SaveSlotService.MarkSlotLoadCompleted();
            var f1 = SaveSlotService.CloneActiveFusionProgress();
            f1.owned[1] = 4;
            Assert.IsTrue(SaveSlotService.SaveActiveFusionProgress(f1));
            Assert.IsTrue(SaveSlotService.SetActiveExpeditionState("att_rec", "{\"schemaVersion\":1}", 1, f1));
            SaveSlotService.FlushAndSuspendActiveProfile(f1);
            File.WriteAllText(SaveSlotService.GetExpeditionFilePath(1), "corrupt");

            // Populate Slot 2 last so lastPlayedSlot == 2.
            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(2, "ALFA"));
            SaveSlotService.MarkSlotLoadCompleted();
            var f2 = SaveSlotService.CloneActiveFusionProgress();
            f2.owned[4] = 3;
            Assert.IsTrue(SaveSlotService.SaveActiveFusionProgress(f2));
            SaveSlotService.FlushAndSuspendActiveProfile(f2);

            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return WaitForTitle();

            Menu.View.ContinueButton.onClick.Invoke();
            yield return null;
            Assert.IsTrue(Menu.View.IsSlotSelectorVisible);
            Assert.AreSame(Menu.View.SlotCardButton(2).gameObject, EventSystem.current.currentSelectedGameObject,
                "Continue selector must focus the last played valid slot.");

            // Check card labels
            var card0Name = Menu.View.SlotCardButton(0).transform.Find("ProfileName").GetComponent<Text>().text;
            Assert.AreEqual("Práctica", card0Name);

            // Clicking damaged expedition slot 1 opens explicit RECUPERAR BÓVEDA prompt.
            Menu.View.SlotCardButton(1).onClick.Invoke();
            yield return null;
            Assert.IsTrue(Menu.IsConfirming);
            StringAssert.Contains("RECUPERAR BÓVEDA", Menu.View.AcceptButton.GetComponentInChildren<Text>().text);
            Menu.CancelConfirmation();
            Assert.AreEqual(SlotHealth.ExpeditionDamagedRecoverable, SaveSlotService.GetSlotSummaries()[1].Health);

            // Clicking NewGame -> occupied Slot 2 shows slot number & name before overwriting.
            Menu.CloseSlotSelector();
            Menu.View.NewGameButton.onClick.Invoke();
            Menu.View.SlotCardButton(2).onClick.Invoke();
            yield return null;
            Assert.IsTrue(Menu.IsConfirming);
            var confirmTexts = Menu.View.Canvas.transform.Find("Confirmation/ConfirmFrame").GetComponentsInChildren<Text>(true);
            StringAssert.Contains("3", confirmTexts[0].text);
            StringAssert.Contains("ALFA", confirmTexts[1].text);
            Menu.CancelConfirmation();
            Menu.CloseSlotSelector();
            Assert.AreEqual(3, SaveSlotService.GetSlotSummaries()[2].OwnedCount);

            // Recover Slot 1 explicitly and enter Vault with its 4 collectibles intact.
            Menu.View.ContinueButton.onClick.Invoke();
            Menu.View.SlotCardButton(1).onClick.Invoke();
            Assert.IsTrue(Menu.IsConfirming);
            Menu.AcceptConfirmation();
            yield return WaitForWorld();
            var station = Object.FindAnyObjectByType<FusionStation>();
            Assert.AreEqual(GameFlowState.Vault, Game.Flow.State);
            Assert.AreEqual(4, station.Progress.OwnedCount);
            Assert.AreEqual(SlotHealth.ValidVault, SaveSlotService.GetSlotSummaries()[1].Health);
        }

        [UnityTest]
        public IEnumerator SwitchingSlotsPreservesCollectionsAndPlaytimePausesOutsideGameplay()
        {
            // Create Slot 0 (Practice) with 3 units.
            Menu.View.NewGameButton.onClick.Invoke();
            Menu.View.SlotCardButton(0).onClick.Invoke();
            Menu.AcceptConfirmation();
            yield return WaitForWorld();
            var station = Object.FindAnyObjectByType<FusionStation>();
            station.Progress.owned[0] = 3;
            station.RefreshDisplays();
            station.Save();

            // Accumulate unpaused time in Vault.
            float elapsed = 0f;
            while (elapsed < 0.35f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            double activeAfterVault = SaveSlotService.ActivePlayTimeSeconds;
            Assert.Greater(activeAfterVault, 0.25d);

            // Paused time must NOT increment saved playtime.
            Game.Session.SetManualPause(true);
            float pauseWait = 0f;
            while (pauseWait < 0.35f)
            {
                pauseWait += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.AreEqual(activeAfterVault, SaveSlotService.ActivePlayTimeSeconds, 0.02d);
            Game.Session.SetManualPause(false);

            // Exit to MainMenu and wait; playtime must stay frozen on MainMenu.
            Game.Flow.ShowMainMenu();
            yield return WaitForTitle();
            yield return new WaitForSecondsRealtime(0.25f);
            double slot0SavedSeconds = SaveSlotService.GetSlotSummaries()[0].TotalPlaySeconds;
            Assert.AreEqual(activeAfterVault, slot0SavedSeconds, 0.05d);

            // Create Slot 1 and confirm Slot 0 was not cleared.
            Menu.View.NewGameButton.onClick.Invoke();
            Menu.View.SlotCardButton(1).onClick.Invoke();
            Menu.AcceptConfirmation();
            yield return WaitForWorld();
            station = Object.FindAnyObjectByType<FusionStation>();
            Assert.AreEqual(0, station.Progress.OwnedCount);
            station.Progress.owned[4] = 2;
            station.Save();

            // Switch back to Slot 0 and verify 3 units & preserved playtime.
            Game.Flow.ShowMainMenu();
            yield return WaitForTitle();
            Menu.View.ContinueButton.onClick.Invoke();
            Menu.View.SlotCardButton(0).onClick.Invoke();
            yield return WaitForWorld();
            station = Object.FindAnyObjectByType<FusionStation>();
            Assert.AreEqual(3, station.Progress.owned[0]);
            Assert.AreEqual(0, station.Progress.owned[4]);
            Assert.GreaterOrEqual(SaveSlotService.ActivePlayTimeSeconds, slot0SavedSeconds - 0.01d);
        }

        [UnityTest]
        public IEnumerator TransparentStartLogoStaysAboveTitleFrameWithoutOverlapOrClippingAcrossAspects()
        {
            var logoArea = (RectTransform)Menu.View.Canvas.transform.Find("MainMenu/LogoReservedArea");
            var titleFrame = (RectTransform)Menu.View.Canvas.transform.Find("MainMenu/TitleFrame");
            Assert.IsNotNull(logoArea);
            Assert.IsNotNull(titleFrame);
            Assert.IsNull(logoArea.GetComponent<Image>(), "LogoReservedArea must remain completely transparent.");

            var logoImg = logoArea.Find("Logo")?.GetComponent<Image>();
            var titleImg = logoArea.Find("Title")?.GetComponent<Image>();
            Assert.IsNotNull(logoImg);
            Assert.IsNotNull(titleImg);
            Assert.IsNotNull(logoImg.sprite);
            Assert.IsNotNull(titleImg.sprite);
            Assert.IsTrue(logoImg.preserveAspect);
            Assert.IsTrue(titleImg.preserveAspect);
            Assert.IsFalse(logoImg.raycastTarget);
            Assert.IsFalse(titleImg.raycastTarget);
            Assert.AreEqual(2, logoArea.GetComponentsInChildren<Image>(true).Length, "Only Logo and Title images should render in LogoReservedArea.");

            Vector2 logoMin = logoImg.rectTransform.anchoredPosition - logoImg.rectTransform.sizeDelta * 0.5f;
            Vector2 logoMax = logoImg.rectTransform.anchoredPosition + logoImg.rectTransform.sizeDelta * 0.5f;
            Vector2 titleMin = titleImg.rectTransform.anchoredPosition - titleImg.rectTransform.sizeDelta * 0.5f;
            Vector2 titleMax = titleImg.rectTransform.anchoredPosition + titleImg.rectTransform.sizeDelta * 0.5f;
            Assert.Greater(logoMin.y, titleMax.y, "Lantern Logo must appear strictly centered above EchoScrap Title.");

            float logoAreaBottom = logoArea.anchoredPosition.y - logoArea.sizeDelta.y * 0.5f;
            float logoAreaTop = logoArea.anchoredPosition.y + logoArea.sizeDelta.y * 0.5f;
            float frameTop = titleFrame.anchoredPosition.y + titleFrame.sizeDelta.y * 0.5f;
            float frameBottom = titleFrame.anchoredPosition.y - titleFrame.sizeDelta.y * 0.5f;
            Assert.Greater(logoAreaBottom, frameTop, "LogoReservedArea must not overlap TitleFrame.");

            Vector2[] resolutions =
            {
                new Vector2(1920, 1080),
                new Vector2(1280, 720),
                new Vector2(2560, 1080),
                new Vector2(1024, 768),
                new Vector2(1440, 900)
            };
            foreach (Vector2 res in resolutions)
            {
                float scaleFactor = Mathf.Pow(res.x / 1920f, 0.5f) * Mathf.Pow(res.y / 1080f, 0.5f);
                float canvasWidth = res.x / scaleFactor;
                float canvasHeight = res.y / scaleFactor;
                Assert.LessOrEqual(logoAreaTop, canvasHeight * 0.5f, $"Top logo clips vertically at {res.x}x{res.y}");
                Assert.GreaterOrEqual(frameBottom, -canvasHeight * 0.5f, $"Bottom menu clips vertically at {res.x}x{res.y}");
                Assert.LessOrEqual(logoArea.sizeDelta.x * 0.5f, canvasWidth * 0.5f, $"Logo clips horizontally at {res.x}x{res.y}");
                Assert.GreaterOrEqual(titleMin.y, -logoArea.sizeDelta.y * 0.5f);
                Assert.LessOrEqual(logoMax.y, logoArea.sizeDelta.y * 0.5f);
            }
            yield return null;
        }
    }
}
