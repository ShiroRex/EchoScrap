using System;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;

namespace EchoScrap.Tests
{
    public sealed class ExpeditionSnapshotEditModeTests
    {
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "EchoScrapSnapEdit_" + Guid.NewGuid().ToString("N"));
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
        public void SessionRng_CombatAndPresentationIsolateAndRestoreByStateAndSeedCount()
        {
            var source = new SessionRng(424242);
            // Interleave calls on both streams to advance counters asymmetrically.
            for (int i = 0; i < 17; i++)
            {
                source.Combat.NextFloat();
                source.Combat.Range(0, 100);
                source.Combat.Range(-15f, 15f);
                source.Combat.InsideUnitCircle();
                if ((i & 1) == 0)
                {
                    source.Presentation.NextFloat();
                    source.Presentation.InsideUnitCircle();
                }
            }

            SessionRngState captured = source.CaptureState();
            Assert.AreEqual(17 * 5, captured.combat.sampleCount);
            Assert.AreEqual(9 * 3, captured.presentation.sampleCount);

            // Further Presentation draws must NOT shift Combat draws.
            var presPerturbed = new SessionRng(424242);
            presPerturbed.RestoreState(captured);
            for (int p = 0; p < 50; p++)
                presPerturbed.Presentation.Range(0, 999);

            // Reconstruct Combat & Presentation from (seed, sampleCount) only.
            var seedCountOnly = new SessionRng(1);
            seedCountOnly.Combat.RestoreFromSeedAndCount(captured.combat.seed, captured.combat.sampleCount);
            seedCountOnly.Presentation.RestoreFromSeedAndCount(captured.presentation.seed, captured.presentation.sampleCount);

            var stateRestored = new SessionRng(1);
            stateRestored.RestoreState(captured);

            for (int k = 0; k < 25; k++)
            {
                float c0 = source.Combat.NextFloat();
                float c1 = stateRestored.Combat.NextFloat();
                float c2 = seedCountOnly.Combat.NextFloat();
                float c3 = presPerturbed.Combat.NextFloat();
                Assert.AreEqual(c0, c1, 0f);
                Assert.AreEqual(c0, c2, 0f);
                Assert.AreEqual(c0, c3, 0f);

                int r0 = source.Combat.Range(-50, 50);
                int r1 = stateRestored.Combat.Range(-50, 50);
                int r2 = seedCountOnly.Combat.Range(-50, 50);
                int r3 = presPerturbed.Combat.Range(-50, 50);
                Assert.AreEqual(r0, r1);
                Assert.AreEqual(r0, r2);
                Assert.AreEqual(r0, r3);

                float f0 = source.Combat.Range(-25.5f, 99.25f);
                float f1 = stateRestored.Combat.Range(-25.5f, 99.25f);
                float f2 = seedCountOnly.Combat.Range(-25.5f, 99.25f);
                float f3 = presPerturbed.Combat.Range(-25.5f, 99.25f);
                Assert.AreEqual(f0, f1, 0f);
                Assert.AreEqual(f0, f2, 0f);
                Assert.AreEqual(f0, f3, 0f);

                Vector2 v0 = source.Combat.InsideUnitCircle();
                Vector2 v1 = stateRestored.Combat.InsideUnitCircle();
                Vector2 v2 = seedCountOnly.Combat.InsideUnitCircle();
                Vector2 v3 = presPerturbed.Combat.InsideUnitCircle();
                Assert.AreEqual(v0, v1);
                Assert.AreEqual(v0, v2);
                Assert.AreEqual(v0, v3);

                // Compare Presentation between source, stateRestored and seedCountOnly too.
                Assert.AreEqual(source.Presentation.NextFloat(), stateRestored.Presentation.NextFloat(), 0f);
                seedCountOnly.Presentation.NextFloat();
            }

            Assert.AreEqual(stateRestored.Presentation.NextFloat(), seedCountOnly.Presentation.NextFloat(), 0f);
        }

        [Test]
        public void ExpeditionSnapshotData_JsonRoundTripAndValidationHold()
        {
            var data = new ExpeditionSnapshotData
            {
                schemaVersion = ExpeditionSnapshotData.CurrentSchemaVersion,
                profileId = "prof_roundtrip",
                attemptId = "att_roundtrip",
                revision = 7,
                modeMinutes = 8,
                gameplayTime = 312.5f,
                isInWarehouse = true,
                fusionSnapshotJson = "{}",
                sceneName = "CombatArena",
                rng = new SessionRng(99).CaptureState()
            };
            data.travel.hasSavedReturnPose = true;
            data.travel.returnPosition = new Vector3(14f, 1f, -22f);
            data.travel.returnRotation = Quaternion.Euler(0f, 125f, 0f);

            data.draft.isOpen = true;
            data.draft.rerollsUsed = 1;
            data.draft.selectionTimeRemaining = 11.4f;
            data.draft.offerIds.AddRange(new[] { "BIO_01", "DEMO_03", "SCRAP_04" });

            data.upgradeRuntime.hasReturnMarker = true;
            data.upgradeRuntime.dashStart = new Vector3(2f, 1f, 3f);
            data.upgradeRuntime.devices.Add(new UpgradeDeviceSnapshot
            {
                kind = 0, // Mine with infiniteLife instead of float.PositiveInfinity
                position = new Vector3(5f, 1f, 6f),
                infiniteLife = true,
                damage = 42f,
                placementOrder = 9
            });
            data.upgradeRuntime.cables.Add(new ElectricCableSnapshot
            {
                slotIndex = 0,
                start = Vector3.zero,
                end = new Vector3(6f, 0f, 0f),
                expiresAt = 95f,
                damage = 18f
            });
            data.upgradeRuntime.chains.Add(new VoltaicChainSnapshot
            {
                count = 2,
                next = 1,
                previousEnemyIndex = 0,
                targetEnemyIndices = new[] { 0, 1, -1, -1 },
                damage = 25f
            });

            string json = JsonUtility.ToJson(data);
            Assert.IsTrue(ExpeditionSnapshotService.ValidateSnapshotPayload(json));

            var restored = JsonUtility.FromJson<ExpeditionSnapshotData>(json);
            Assert.IsNotNull(restored);
            Assert.AreEqual(8, restored.modeMinutes);
            Assert.IsTrue(restored.isInWarehouse);
            Assert.IsTrue(restored.travel.hasSavedReturnPose);
            Assert.AreEqual(new Vector3(14f, 1f, -22f), restored.travel.returnPosition);
            Assert.AreEqual(3, restored.draft.offerIds.Count);
            Assert.AreEqual("DEMO_03", restored.draft.offerIds[1]);
            Assert.IsTrue(restored.upgradeRuntime.devices[0].infiniteLife);
            Assert.AreEqual(1, restored.upgradeRuntime.cables.Count);
            Assert.AreEqual(1, restored.upgradeRuntime.chains[0].targetEnemyIndices[1]);
        }

        [Test]
        public void SaveSlotService_RejectsStaleRevisionAndCorruptedStage2Payload()
        {
            Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(0, "PILOTO"));
            SaveSlotService.MarkSlotLoadCompleted();

            var validSnap = new ExpeditionSnapshotData
            {
                profileId = SaveSlotService.ActiveProfileId,
                attemptId = "att_rev_1",
                revision = SaveSlotService.NextActiveExpeditionRevision(),
                modeMinutes = 3,
                gameplayTime = 12f,
                rng = new SessionRng(10).CaptureState()
            };
            string json1 = JsonUtility.ToJson(validSnap);
            Assert.IsTrue(SaveSlotService.SetActiveExpeditionState("att_rev_1", json1, validSnap.revision));

            // Stale / equal revision must be refused.
            Assert.IsFalse(SaveSlotService.SetActiveExpeditionState("att_rev_1", json1, validSnap.revision));
            Assert.IsFalse(SaveSlotService.SetActiveExpeditionState("att_rev_1", json1, validSnap.revision - 1));

            // Higher revision succeeds.
            validSnap.revision = SaveSlotService.NextActiveExpeditionRevision();
            validSnap.gameplayTime = 24f;
            Assert.IsTrue(SaveSlotService.SetActiveExpeditionState("att_rev_1", JsonUtility.ToJson(validSnap), validSnap.revision));
            SaveSlotService.FlushAndSuspendActiveProfile();
            Assert.AreEqual(SlotHealth.ValidExpedition, SaveSlotService.GetSlotSummaries()[0].Health);

            // Replace valid snapshot with truncated/malformed Stage 2 body (valid header + invalid modeMinutes=99).
            Assert.IsTrue(SaveSlotService.SelectSlotForContinue(0, out _, out _));
            SaveSlotService.MarkSlotLoadCompleted();
            validSnap.revision = SaveSlotService.NextActiveExpeditionRevision();
            validSnap.modeMinutes = 99;
            string badJson = JsonUtility.ToJson(validSnap);
            // SetActiveExpeditionState writes envelope, then InspectSlot rejects invalid body modeMinutes.
            Assert.IsTrue(SaveSlotService.SetActiveExpeditionState("att_rev_1", badJson, validSnap.revision));
            SaveSlotService.FlushAndSuspendActiveProfile();
            Assert.AreEqual(SlotHealth.ExpeditionDamagedRecoverable, SaveSlotService.GetSlotSummaries()[0].Health);
        }
    }
}
