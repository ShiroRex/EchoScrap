using System;
using System.IO;
using System.Linq;
using EchoScrap.Combat;
using NUnit.Framework;

namespace EchoScrap.Tests
{
    public sealed class RankingBestScoreTests
    {
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "EchoScrapRankEdit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            SaveSlotService.ResetRuntimeState();
            SaveSlotService.SetStorageRootForTesting(tempRoot);
            SaveSlotService.SetPersistenceOverride(true);
        }

        [TearDown]
        public void TearDown()
        {
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
        public void UnlimitedRankedRunsAndBestScoreTracking_PreserveHighestScore()
        {
            var ledger = new EventLeaderboardSync.RankingLedger();
            ledger.NewCollection("ECO PILOTO");
            Assert.IsTrue(ledger.Claim(), "First attempt must claim eligibility.");
            Assert.IsTrue(ledger.Claim(), "Second attempt must still be eligible.");
            Assert.IsTrue(ledger.Claim(), "Unlimited ranked attempts must remain eligible.");

            Assert.AreEqual(500, EventLeaderboardSync.CalculateScore(1001, 8));
            Assert.AreEqual(1001, EventLeaderboardSync.CalculateScore(1001, 3));

            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(0, "ECO PILOTO"));
            SaveSlotService.MarkSlotLoadCompleted();
            SaveSlotService.RecordActiveProfileBestScore(1200);
            SaveSlotService.RecordActiveProfileBestScore(2450);
            SaveSlotService.RecordActiveProfileBestScore(900);

            SlotSummary summary = SaveSlotService.GetSlotSummaries()[0];
            Assert.AreEqual(2450, summary.BestLocalScore);
            Assert.AreEqual(3, summary.CompletedRankedRuns);
        }

        [Test]
        public void Outbox_PreservesHistoricalFinalsAndCancelsInterruptedOnSlotOverwrite()
        {
            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(0, "PILOTO UNO"));
            SaveSlotService.MarkSlotLoadCompleted();
            string originalProfileId = SaveSlotService.ActiveProfileId;

            var firstRun = new EventPlayerRecord
            {
                sessionId = "attempt_high_01",
                playerName = "PILOTO UNO",
                gamertagNormalized = "PILOTO UNO",
                status = "VICTORIA",
                modeMinutes = 3,
                rulesVersion = 2,
                score = 4200,
                updatedAt = 1000
            };
            var secondRun = new EventPlayerRecord
            {
                sessionId = "attempt_low_02",
                playerName = "PILOTO UNO",
                gamertagNormalized = "PILOTO UNO",
                status = "DERROTA",
                modeMinutes = 8,
                rulesVersion = 2,
                score = 1800,
                updatedAt = 2000
            };
            Assert.IsTrue(SaveSlotService.EnqueuePendingRecord(originalProfileId, firstRun));
            Assert.IsTrue(SaveSlotService.EnqueuePendingRecord(originalProfileId, secondRun));

            // Simulate an active interrupted third attempt in slot 0 when slot 0 is overwritten.
            Assert.IsTrue(SaveSlotService.SetActiveExpeditionState("attempt_active_03", "{\"schemaVersion\":1}", 1, null));
            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(0, "PILOTO DOS"));
            Assert.AreNotEqual(originalProfileId, SaveSlotService.ActiveProfileId);

            // Re-read from disk to confirm atomic persistence.
            SaveSlotService.ResetRuntimeState();
            SaveSlotService.SetStorageRootForTesting(tempRoot);
            SaveSlotService.SetPersistenceOverride(true);

            var pending = SaveSlotService.GetPendingRankingItems();
            Assert.AreEqual(3, pending.Count);
            Assert.IsTrue(pending.Any(i => i.ownerProfileId == originalProfileId && i.record.sessionId == "attempt_high_01" && i.record.status == "VICTORIA"));
            Assert.IsTrue(pending.Any(i => i.ownerProfileId == originalProfileId && i.record.sessionId == "attempt_low_02" && i.record.status == "DERROTA"));
            Assert.IsTrue(pending.Any(i => i.ownerProfileId == originalProfileId && i.record.sessionId == "attempt_active_03" && i.record.status == "CANCELADA"));

            // Removing one delivered record does not affect the remaining records.
            Assert.IsTrue(SaveSlotService.RemovePendingRecord("attempt_high_01"));
            Assert.AreEqual(2, SaveSlotService.GetPendingRankingItems().Count);
        }
    }
}
