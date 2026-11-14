using System;
using System.IO;
using System.Linq;
using EchoScrap.Combat;
using NUnit.Framework;
using UnityEngine;

namespace EchoScrap.Tests
{
    public sealed class SaveSlotStorageTests
    {
        private string tempRoot;
        private string prevProdSave, prevLegacySave, prevProfile, prevName, prevNameTest, prevRank, prevRankTest;
        private bool hadProdSave, hadLegacySave, hadProfile, hadName, hadNameTest, hadRank, hadRankTest;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "EchoScrapSlotTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            SaveSlotService.ResetRuntimeState();
            SaveSlotService.SetStorageRootForTesting(tempRoot);
            SaveSlotService.SetPersistenceOverride(true);
            BackupPlayerPrefs();
            ClearLegacyPrefs();
        }

        [TearDown]
        public void TearDown()
        {
            RestorePlayerPrefs();
            SaveSlotService.SetPersistenceOverride(null);
            SaveSlotService.SetStorageRootForTesting(null);
            SaveSlotService.ResetRuntimeState();
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
            catch (Exception) { }
        }

        [Test]
        public void CreateAndReloadIsolatesThreeSlotsWithUniqueProfileIdsAndHhMmSsPlaytime()
        {
            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(0, "ALPHA"));
            SaveSlotService.MarkSlotLoadCompleted();
            var p0 = SaveSlotService.CloneActiveFusionProgress();
            p0.owned[0] = 3;
            SaveSlotService.SaveActiveFusionProgress(p0);
            for (int step = 0; step < 14660; step++) SaveSlotService.RecordActivePlaytime(10f); // Clamp 0.25s/step -> 3665s = 01:01:05
            SaveSlotService.FlushAndSuspendActiveProfile(p0);

            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(1, "")); // Practice
            SaveSlotService.MarkSlotLoadCompleted();
            var p1 = SaveSlotService.CloneActiveFusionProgress();
            p1.owned[2] = 5;
            SaveSlotService.SaveActiveFusionProgress(p1);
            SaveSlotService.FlushAndSuspendActiveProfile(p1);

            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(2, "BETA"));
            SaveSlotService.MarkSlotLoadCompleted();
            var p2 = SaveSlotService.CloneActiveFusionProgress();
            p2.owned[4] = 1;
            SaveSlotService.SaveActiveFusionProgress(p2);
            SaveSlotService.FlushAndSuspendActiveProfile(p2);

            SlotSummary[] summaries = SaveSlotService.GetSlotSummaries();
            Assert.AreEqual(3, summaries.Length);
            Assert.AreEqual("ALPHA", summaries[0].DisplayTitle);
            Assert.AreEqual("Práctica", summaries[1].DisplayTitle);
            Assert.AreEqual("BETA", summaries[2].DisplayTitle);
            Assert.AreEqual("3/18", summaries[0].CollectiblesLabel);
            Assert.AreEqual("5/18", summaries[1].CollectiblesLabel);
            Assert.AreEqual("1/18", summaries[2].CollectiblesLabel);
            Assert.AreEqual("01:01:05", summaries[0].FormattedPlayTime);
            Assert.AreEqual("00:00:00", summaries[1].FormattedPlayTime);
            Assert.AreEqual("101:41:41", SaveSlotService.FormatPlayTime(366101d));

            string oldSlot1Profile = summaries[1].ProfileId;
            Assert.AreEqual(3, summaries.Select(s => s.ProfileId).Distinct().Count());

            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(1, "GAMMA"));
            SaveSlotService.MarkSlotLoadCompleted();
            SlotSummary[] afterReplace = SaveSlotService.GetSlotSummaries();
            Assert.AreNotEqual(oldSlot1Profile, afterReplace[1].ProfileId);
            Assert.AreEqual("GAMMA", afterReplace[1].DisplayTitle);
            Assert.AreEqual("0/18", afterReplace[1].CollectiblesLabel);
            Assert.AreEqual("3/18", afterReplace[0].CollectiblesLabel);
            Assert.AreEqual("1/18", afterReplace[2].CollectiblesLabel);
        }

        [Test]
        public void AtomicWriteVerifiesSha256AndFallsBackToBackupWhenPrimaryIsCorrupted()
        {
            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(0, "VIKING"));
            SaveSlotService.MarkSlotLoadCompleted();
            var rev1 = SaveSlotService.CloneActiveFusionProgress();
            rev1.owned[0] = 2;
            Assert.IsTrue(SaveSlotService.SaveActiveFusionProgress(rev1));

            var rev2 = SaveSlotService.CloneActiveFusionProgress();
            rev2.owned[0] = 7;
            SaveSlotService.FlushAndSuspendActiveProfile(rev2);

            string primaryPath = SaveSlotService.GetSlotFilePath(0);
            string bakPath = primaryPath + ".bak";
            Assert.IsTrue(File.Exists(primaryPath));
            Assert.IsTrue(File.Exists(bakPath));

            // Corrupt primary file -> must transparently fall back to valid .bak (owned[0] == 2).
            File.WriteAllText(primaryPath, "{corrupt-json-sha-mismatch");
            SlotSummary[] fallbackSummaries = SaveSlotService.GetSlotSummaries();
            Assert.AreEqual(SlotHealth.ValidVault, fallbackSummaries[0].Health);
            Assert.AreEqual(2, fallbackSummaries[0].OwnedCount);

            // Corrupt backup too -> slot must be flagged Corrupted and non-continuable.
            File.WriteAllText(bakPath, "{also-corrupt");
            SlotSummary[] brokenSummaries = SaveSlotService.GetSlotSummaries();
            Assert.AreEqual(SlotHealth.Corrupted, brokenSummaries[0].Health);
            Assert.IsFalse(brokenSummaries[0].CanContinue);
            Assert.AreEqual("Archivo dañado", brokenSummaries[0].StatusLabel);
            Assert.IsFalse(SaveSlotService.HasAnyValidSlot());
        }

        [Test]
        public void OutOfOrderOrWrongProfileRevisionCannotOverwriteNewerSave()
        {
            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(0, "GUARD"));
            SaveSlotService.MarkSlotLoadCompleted();
            string profileId = SaveSlotService.ActiveProfileId;

            var p = SaveSlotService.CloneActiveFusionProgress();
            p.owned[1] = 4;
            Assert.IsTrue(SaveSlotService.SaveActiveFusionProgress(p));

            var staleSameProfile = new SlotProfileData
            {
                slotIndex = 0,
                profileId = profileId,
                playerName = "GUARD",
                revision = 1,
                fusion = new FusionProgress()
            };
            staleSameProfile.fusion.owned[1] = 1;
            Assert.IsFalse(SaveSlotService.TryCommitProfileRevision(staleSameProfile));

            var wrongProfile = new SlotProfileData
            {
                slotIndex = 0,
                profileId = "stale-previous-owner",
                playerName = "OLD",
                revision = 999,
                fusion = new FusionProgress()
            };
            Assert.IsFalse(SaveSlotService.TryCommitProfileRevision(wrongProfile));

            SlotSummary[] summaries = SaveSlotService.GetSlotSummaries();
            Assert.AreEqual(4, summaries[0].OwnedCount);
        }

        [Test]
        public void CorruptedExpeditionAllowsExplicitVaultRecoveryWithoutLosingCollection()
        {
            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(0, "SCOUT"));
            SaveSlotService.MarkSlotLoadCompleted();
            var fusion = SaveSlotService.CloneActiveFusionProgress();
            fusion.owned[4] = 6;
            Assert.IsTrue(SaveSlotService.SaveActiveFusionProgress(fusion));

            var header = new ExpeditionSnapshotHeader
            {
                profileId = SaveSlotService.ActiveProfileId,
                attemptId = "att_test_1",
                revision = 1,
                modeMinutes = 3,
                gameplayTime = 42.5f
            };
            Assert.IsTrue(SaveSlotService.SetActiveExpeditionState("att_test_1", JsonUtility.ToJson(header), 1, fusion));
            Assert.IsTrue(SaveSlotService.SetActiveExpeditionState("att_test_1", JsonUtility.ToJson(header), 2, fusion));
            SaveSlotService.FlushAndSuspendActiveProfile(fusion);

            Assert.AreEqual("Expedición en curso", SaveSlotService.GetSlotSummaries()[0].StatusLabel);

            string expPath = SaveSlotService.GetExpeditionFilePath(0);
            File.WriteAllText(expPath, "bad-data");
            File.WriteAllText(expPath + ".bak", "bad-bak");

            SlotSummary damaged = SaveSlotService.GetSlotSummaries()[0];
            Assert.AreEqual(SlotHealth.ExpeditionDamagedRecoverable, damaged.Health);
            Assert.IsFalse(damaged.CanContinue);
            Assert.IsTrue(damaged.CanRecoverVault);
            Assert.AreEqual(6, damaged.OwnedCount);
            Assert.IsTrue(SaveSlotService.HasAnyContinuableOrRecoverableSlot());

            Assert.IsTrue(SaveSlotService.RecoverVaultInSlot(0, out SlotSummary recovered));
            Assert.AreEqual(SlotHealth.ValidVault, recovered.Health);
            Assert.AreEqual(6, recovered.OwnedCount);
            Assert.AreEqual("En la bóveda", SaveSlotService.GetSlotSummaries()[0].StatusLabel);
        }

        [Test]
        public void MigratesLegacyPlayerPrefsOncePreservingInventoryDiscoveriesEquipmentFusionAliasAndOutbox()
        {
            var legacy = new FusionProgress
            {
                version = 4,
                owned = new int[FusionCatalog.Count],
                discovered = new bool[FusionCatalog.Count],
                equipped = new[] { 2, -1, 4 },
                displays = new int[10]
            };
            legacy.owned[0] = 3;
            legacy.owned[2] = 1;
            legacy.owned[4] = 1;
            legacy.discovered[0] = true;
            legacy.discovered[2] = true;
            legacy.discovered[4] = true;
            legacy.discovered[9] = true; // Discovered before, even with 0 copies now.
            legacy.phase = FusionPhase.Fusing;
            legacy.result = 8;
            legacy.remaining = 17f;
            legacy.duration = 30f;

            string nameKey = Application.isBatchMode ? EventLeaderboardSync.PlayerNamePrefKey + ".Test" : EventLeaderboardSync.PlayerNamePrefKey;
            string ledgerKey = Application.isBatchMode ? "EchoScrap.Rank.Test.v2" : "EchoScrap.Rank.v2";

            PlayerPrefs.SetString(FusionStation.SaveKey, JsonUtility.ToJson(legacy));
            PlayerPrefs.SetString(nameKey, "HERENCIA");
            PlayerPrefs.SetString(ledgerKey, "{\"names\":[\"HERENCIA\"],\"name\":\"HERENCIA\",\"eligible\":false,\"interrupted\":\"old_cancel\",\"pending\":[{\"sessionId\":\"old_pending\",\"name\":\"HERENCIA\",\"score\":500}]}");
            PlayerPrefs.Save();

            SaveSlotService.EnsureMigrated();

            SlotSummary[] summaries = SaveSlotService.GetSlotSummaries();
            Assert.AreEqual(SlotHealth.ValidVault, summaries[0].Health);
            Assert.AreEqual("HERENCIA", summaries[0].DisplayTitle);
            Assert.AreEqual(5, summaries[0].OwnedCount);
            Assert.AreEqual(0d, summaries[0].TotalPlaySeconds);
            Assert.AreEqual("00:00:00", summaries[0].FormattedPlayTime);
            Assert.AreEqual(SlotHealth.Empty, summaries[1].Health);
            Assert.AreEqual(SlotHealth.Empty, summaries[2].Health);

            Assert.IsTrue(SaveSlotService.SelectSlotForContinue(0, out _, out _));
            FusionProgress loaded = SaveSlotService.CloneActiveFusionProgress();
            Assert.AreEqual(3, loaded.owned[0]);
            Assert.AreEqual(1, loaded.owned[2]);
            Assert.AreEqual(1, loaded.owned[4]);
            Assert.AreEqual(4, loaded.DiscoveredCount);
            Assert.IsTrue(loaded.IsDiscovered(9));
            CollectionAssert.AreEqual(new[] { 2, -1, 4 }, loaded.equipped);
            Assert.AreEqual(FusionPhase.Fusing, loaded.phase);
            Assert.AreEqual(8, loaded.result);
            Assert.AreEqual(17f, loaded.remaining, .01f);

            var outbox = SaveSlotService.GetPendingRankingItems();
            Assert.IsTrue(outbox.Any(i => i.record.sessionId == "old_pending" && i.record.score == 500));
            Assert.IsTrue(outbox.Any(i => i.record.sessionId == "old_cancel" && i.record.status == "CANCELADA"));

            // Overwriting or clearing slot 0 must NOT trigger a second migration.
            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(0, "NUEVO"));
            SaveSlotService.EnsureMigrated();
            SlotSummary[] after = SaveSlotService.GetSlotSummaries();
            Assert.AreEqual("NUEVO", after[0].DisplayTitle);
            Assert.AreEqual(SlotHealth.Empty, after[1].Health);
        }

        [Test]
        public void FusionProgressVersionFourPreservesDisplaysAndExpandsToEighteen()
        {
            string v4Json = "{\"version\":4,\"owned\":[2,1,0,0,1,0,0,0,0,0,0,0,0,0,0],\"discovered\":[true,true,false,false,true,false,false,false,false,false,false,false,false,false,false],\"equipped\":[0,4,-1],\"displays\":[0,4,-1,1,0,-1,-1,-1,-1,-1],\"phase\":0}";
            var progress = JsonUtility.FromJson<FusionProgress>(v4Json);
            progress.RestoreAfterLoading();
            Assert.AreEqual(18, progress.displays.Length);
            Assert.AreEqual(0, progress.displays[0]);
            Assert.AreEqual(4, progress.displays[1]);
            Assert.AreEqual(1, progress.displays[3]);
            CollectionAssert.AreEqual(new[] { 0, 4, -1 }, progress.equipped);
            Assert.AreEqual(4, progress.OwnedCount);
            for (int i = 10; i < 18; i++) Assert.AreEqual(-1, progress.displays[i]);
        }

        private void BackupPlayerPrefs()
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
        }

        private void RestorePlayerPrefs()
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

        private static void ClearLegacyPrefs()
        {
            PlayerPrefs.DeleteKey(FusionStation.SaveKey);
            PlayerPrefs.DeleteKey("EchoScrap.Collection.v1");
            PlayerPrefs.DeleteKey(GameFlowController.ProfileKey);
            PlayerPrefs.DeleteKey(EventLeaderboardSync.PlayerNamePrefKey);
            PlayerPrefs.DeleteKey(EventLeaderboardSync.PlayerNamePrefKey + ".Test");
            PlayerPrefs.DeleteKey("EchoScrap.Rank.v2");
            PlayerPrefs.DeleteKey("EchoScrap.Rank.Test.v2");
            PlayerPrefs.Save();
        }

        private static void RestoreKey(string key, bool had, string val)
        {
            if (had) PlayerPrefs.SetString(key, val);
            else PlayerPrefs.DeleteKey(key);
        }
    }
}
