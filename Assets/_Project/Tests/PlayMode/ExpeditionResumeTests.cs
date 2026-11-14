using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EchoScrap.Tests
{
    public sealed class ExpeditionResumeTests
    {
        private string tempRoot;
        private string prevProdSave, prevLegacySave, prevProfile, prevName, prevNameTest, prevRank, prevRankTest;
        private bool hadProdSave, hadLegacySave, hadProfile, hadName, hadNameTest, hadRank, hadRankTest;

        private MainMenuController Menu => Object.FindAnyObjectByType<MainMenuController>();
        private CombatGameBootstrap Game => Object.FindAnyObjectByType<CombatGameBootstrap>();
        private FusionStation Station => Object.FindAnyObjectByType<FusionStation>();
        private WarehouseTravel Travel => Object.FindAnyObjectByType<WarehouseTravel>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "EchoScrapResumeTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            BackupAndClearLegacyPrefs();
            SaveSlotService.ResetRuntimeState();
            SaveSlotService.SetStorageRootForTesting(tempRoot);
            SaveSlotService.SetPersistenceOverride(true);
            SceneTransitionState.Reset();
            SceneTransitionState.SaveProgress = true;
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Assert.IsNotNull(Menu);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var active = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("ExpeditionResumeCleanup"));
            yield return SceneManager.UnloadSceneAsync(active);
            SaveSlotService.SetPersistenceOverride(null);
            SaveSlotService.SetStorageRootForTesting(null);
            SaveSlotService.ResetRuntimeState();
            EventLeaderboardSync.EnableNetworkInTests = false;
            EventLeaderboardSync.DatabaseUrlOverride = null;
            EventLeaderboardSync.AuthTokenOverride = null;
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

        private IEnumerator WaitForWorldReady()
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var bootstrap = Game;
                if (bootstrap != null && bootstrap.IsSceneEntryReady && bootstrap.Flow != null && !LoadingScreen.IsLoading)
                {
                    yield return null;
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("Timed out waiting for CombatArena entry.");
        }

        private IEnumerator WaitForExpeditionEntry()
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (Game != null && Game.Flow != null && Game.Flow.State == GameFlowState.Expedition &&
                    !Game.Flow.IsLoading && !LoadingScreen.IsLoading && !Game.Session.IsPaused)
                {
                    yield return null;
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("Timed out waiting for expedition start.");
        }

        private IEnumerator CreateSlotAndEnterVault(int slotIndex, string name)
        {
            Menu.View.NewGameButton.onClick.Invoke();
            yield return null;
            Menu.View.SlotCardButton(slotIndex).onClick.Invoke();
            yield return null;
            if (!string.IsNullOrEmpty(name))
            {
                // Pre-populated storage fallback keeps UI test free from remote network Firebase calls.
                Menu.CancelConfirmation();
                Menu.CloseSlotSelector();
                Assert.IsTrue(SaveSlotService.CreateNewGameInSlot(slotIndex, name));
                Menu.View.ContinueButton.onClick.Invoke();
                yield return null;
                Menu.View.SlotCardButton(slotIndex).onClick.Invoke();
            }
            else
            {
                Menu.AcceptConfirmation();
            }
            yield return WaitForWorldReady();
            Assert.AreEqual(GameFlowState.Vault, Game.Flow.State);
        }

        private IEnumerator SimulateRestartAndContinueSlot(int slotIndex, bool runNormalQuitHook)
        {
            if (runNormalQuitHook && Game != null && Game.Flow != null)
                Game.Flow.SendMessage("OnApplicationQuit");

            SaveSlotService.ResetRuntimeState();
            SaveSlotService.SetStorageRootForTesting(tempRoot);
            SaveSlotService.SetPersistenceOverride(true);
            SceneTransitionState.Reset();
            SceneTransitionState.SaveProgress = true;
            CombatTargetRegistry.Clear();

            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            Assert.IsNotNull(Menu);
            Assert.IsTrue(Menu.HasCollection);
            Menu.View.ContinueButton.onClick.Invoke();
            yield return null;
            Menu.View.SlotCardButton(slotIndex).onClick.Invoke();
            yield return WaitForWorldReady();
        }

        [UnityTest]
        public IEnumerator PauseQuitAndReopen_RestoresFullSimulationWithoutDuplicateEventsOrRngShift()
        {
            yield return CreateSlotAndEnterVault(0, "PILOT_8M");
            Station.Progress.owned[0] = 4;
            Station.Save();
            Game.Motor.transform.position = Station.InteractionPoint.position + Vector3.back * 1.5f;
            Physics.SyncTransforms();
            Assert.IsTrue(Station.TryOpenMenu());
            Assert.IsTrue(Station.StartFusion(new[] { 0, 0, 0, 0 }));

            Assert.IsTrue(Game.Flow.BeginExpedition());
            Game.Flow.SelectExpedition(true); // 8-minute mode
            yield return WaitForExpeditionEntry();
            Game.Director.AutoTick = false;

            string originalAttemptId = SaveSlotService.GetActiveAttemptId();
            Assert.IsFalse(string.IsNullOrEmpty(originalAttemptId));
            Assert.GreaterOrEqual(Game.Director.Settings.BossStartTime, 300f);

            // Configure player upgrades, pickups, hazards, enemies, collectible pickup and fusion.
            Game.Stats.SetUpgradeTier("DEMO_03", 1); // Mines (infinite life)
            Game.Stats.SetUpgradeTier("DASH_07", 1); // Electric cables
            Game.Stats.SetUpgradeTier("SCRAP_04", 1); // Turret
            Game.Stats.SetUpgradeTier("BIO_01", 2);
            Game.Motor.transform.position = new Vector3(12f, 1f, -8f);
            Physics.SyncTransforms();

            Game.Player.ReceiveDamage(new DamageRequest(1, DamageSourceType.Contact, Vector3.zero, Vector3.forward, 20f));
            float expectedHp = Game.Player.CurrentHealth;
            Game.Progression.AddExperience(120);
            if (Game.Draft.IsOpen && Game.Draft.Offers.Count > 0)
                Game.Draft.TryChoose(0);

            int xpId = Game.Pickups.SpawnExperience(new Vector3(18f, 0f, -8f), 25);
            int medId = Game.Pickups.SpawnMedkit(new Vector3(20f, 0f, -8f));
            Assert.GreaterOrEqual(xpId, 0);
            Assert.GreaterOrEqual(medId, 0);
            int puddleId = Game.Hazards.SpawnPuddle(1, new Vector3(16f, 0f, -12f), 2f, 6f, 4f, 0.5f);
            Assert.GreaterOrEqual(puddleId, 0);

            EnemyActor moth = Game.Enemies.Spawn(EnemyArchetypeId.ExplosiveMoth, new Vector3(25f, 0f, -5f));
            Assert.IsNotNull(moth);
            moth.ReceiveDamage(new DamageRequest(1, DamageSourceType.FlashlightPulse, Vector3.zero, Vector3.forward, 5f));
            float expectedMothHp = moth.CurrentHealth;

            CompanionSystem companions = Station.GetComponent<CompanionSystem>();
            Assert.IsTrue(companions.TrySpawn(1));

            // Let initial t=0 wave timer settle past immediate spawn window, then pause & capture.
            yield return new WaitForSeconds(0.15f);
            Game.Session.SetManualPause(true);
            int expectedEnemyCount = EnemyActor.ActiveEnemies.Count;
            float expectedTime = Game.Session.Clock.GameplayTime;
            int expectedStep = Game.Session.Clock.SimulationStep;
            int expectedLevel = Game.Progression.Level;
            long expectedTotalXp = Game.Progression.TotalXp;
            float nextRngDrawBeforeClose = Game.Session.Rng.Combat.NextFloat();
            // Restore RNG to right before test peek by saving again? Wait: capture/peek after save on clone!
            SaveSlotService.FlushAndSuspendActiveProfile(Station.Progress);

            // Reopen from MainMenu with normal quit hook already saved.
            SaveSlotService.ResetRuntimeState();
            SaveSlotService.SetStorageRootForTesting(tempRoot);
            SaveSlotService.SetPersistenceOverride(true);
            CombatTargetRegistry.Clear();
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;

            int levelEventsDuringLoad = 0;
            int bossEventsDuringLoad = 0;
            Menu.View.ContinueButton.onClick.Invoke();
            yield return null;
            Menu.View.SlotCardButton(0).onClick.Invoke();

            // Hook events as soon as CombatArena bootstrap appears.
            CombatGameBootstrap resuming = null;
            float hookDeadline = Time.realtimeSinceStartup + 10f;
            bool hooked = false;
            while (Time.realtimeSinceStartup < hookDeadline)
            {
                resuming = Game;
                if (resuming != null && resuming.IsInitialized && !hooked)
                {
                    resuming.Progression.OnLevelGained += _ => levelEventsDuringLoad++;
                    resuming.Encounter.OnBossActivated += _ => bossEventsDuringLoad++;
                    if (resuming.Director != null) resuming.Director.AutoTick = false;
                    hooked = true;
                }
                if (resuming != null && resuming.IsSceneEntryReady && resuming.Flow != null && !LoadingScreen.IsLoading)
                {
                    Game.Session.SetManualPause(true);
                    break;
                }
                yield return null;
            }

            Assert.AreEqual(0, levelEventsDuringLoad, "Restoration must not re-fire OnLevelGained.");
            Assert.AreEqual(0, bossEventsDuringLoad, "Restoration must not re-fire OnBossActivated.");
            Assert.AreEqual(GameFlowState.Expedition, Game.Flow.State);
            Assert.AreEqual(SessionState.Paused, Game.Session.State);
            Assert.GreaterOrEqual(Game.Director.Settings.BossStartTime, 300f);
            Assert.AreEqual(originalAttemptId, SaveSlotService.GetActiveAttemptId());
            Assert.AreEqual(originalAttemptId, EventLeaderboardSync.Instance.ActiveAttemptId);
            Assert.AreEqual(expectedTime, Game.Session.Clock.GameplayTime, 0.1f);
            Assert.AreEqual(expectedStep, Game.Session.Clock.SimulationStep);
            Assert.AreEqual(expectedHp, Game.Player.CurrentHealth, 0.01f);
            Assert.AreEqual(expectedLevel, Game.Progression.Level);
            Assert.AreEqual(expectedTotalXp, Game.Progression.TotalXp);
            Assert.IsTrue(Game.Pickups.ActiveExperienceCount >= 1);
            Assert.IsTrue(Game.Pickups.ActiveMedkitCount >= 1);
            Assert.IsTrue(Game.Hazards.ContainsSequenceId(puddleId));
            Assert.AreEqual(expectedEnemyCount, EnemyActor.ActiveEnemies.Count);
            EnemyActor restoredMoth = EnemyActor.ActiveEnemies.FirstOrDefault(e => e.ArchetypeId == EnemyArchetypeId.ExplosiveMoth);
            Assert.IsNotNull(restoredMoth);
            Assert.AreEqual(expectedMothHp, restoredMoth.CurrentHealth, 0.01f);
            Assert.AreEqual(1, Station.GetComponent<CompanionSystem>().ActivePickups);
            Assert.AreEqual(FusionPhase.Fusing, Station.Progress.phase);
            Assert.AreEqual(nextRngDrawBeforeClose, Game.Session.Rng.Combat.NextFloat(), 0f);
        }

        [UnityTest]
        public IEnumerator OpenDraftAndVaultVisit_RestoreExactOffersAndCombatReturnPosition()
        {
            yield return CreateSlotAndEnterVault(1, ""); // Practice 3-min slot
            Assert.IsTrue(Game.Flow.BeginExpedition());
            Game.Flow.SelectExpedition(false); // 3-minute mode
            yield return WaitForExpeditionEntry();
            Game.Director.AutoTick = false;

            // Open draft on level up and verify same offers survive restart without reroll.
            Game.Progression.AddExperience(Game.Progression.RequiredXp);
            yield return null;
            Assert.IsTrue(Game.Draft.IsOpen);
            string[] expectedOfferIds = Game.Draft.Offers.Select(o => o.Card.Id).ToArray();
            Assert.AreEqual(3, expectedOfferIds.Length);

            yield return SimulateRestartAndContinueSlot(1, runNormalQuitHook: true);
            Assert.AreEqual(GameFlowState.Expedition, Game.Flow.State);
            Assert.Less(Game.Director.Settings.BossStartTime, 300f);
            Assert.IsTrue(Game.Draft.IsOpen);
            Assert.AreEqual(SessionState.TacticalDraft, Game.Session.State);
            CollectionAssert.AreEqual(expectedOfferIds, Game.Draft.Offers.Select(o => o.Card.Id).ToArray());

            Assert.IsTrue(Game.Draft.TryChoose(0));
            yield return null;
            Assert.IsFalse(Game.Draft.IsOpen);

            // Spawn enemy & move player to non-default position, then enter Vault temporarily.
            Game.Enemies.DespawnAllActive();
            EnemyActor chaser = Game.Enemies.Spawn(EnemyArchetypeId.Residual, new Vector3(32f, 0f, 10f));
            Assert.IsNotNull(chaser);
            Vector3 expectedReturnPos = ArenaSurface.OnGround(new Vector3(26f, 1f, -14f), 1f);
            Game.Motor.transform.position = expectedReturnPos;
            var rb = Game.Motor.GetComponent<Rigidbody>();
            rb.position = expectedReturnPos;
            Physics.SyncTransforms();

            Assert.IsTrue(Travel.TryTravel(false, Game.Player));
            Assert.IsTrue(Game.Session.IsInWarehouse);

            // Simulate restart while inside Vault during active expedition.
            yield return SimulateRestartAndContinueSlot(1, runNormalQuitHook: true);
            Assert.AreEqual(GameFlowState.Expedition, Game.Flow.State);
            Assert.IsTrue(Game.Session.IsInWarehouse);
            Assert.IsTrue(chaser == null || !chaser.gameObject.activeInHierarchy); // Old instance unloaded
            Assert.AreEqual(1, EnemyActor.ActiveEnemies.Count);
            Assert.IsTrue(EnemyActor.ActiveEnemies[0].GetComponent<Rigidbody>().isKinematic, "Outside enemy must stay frozen in Vault.");

            // Travel back out to arena -> must return to saved arena position, not default arrival.
            yield return new WaitForSeconds(1.05f); // Respect Travel nextTravel cooldown (1s)
            Assert.IsTrue(Travel.TryTravel(true, Game.Player));
            Assert.IsFalse(Game.Session.IsInWarehouse);
            Assert.AreEqual(expectedReturnPos.x, Game.Player.transform.position.x, 0.1f);
            Assert.AreEqual(expectedReturnPos.z, Game.Player.transform.position.z, 0.1f);
        }

        [UnityTest]
        public IEnumerator GolgoraBossAndForcedCloseAutosave_ResumeAndCleanUpOnOutcomeAndAbandon()
        {
            yield return CreateSlotAndEnterVault(2, "GOLGORA_TEST");
            Assert.IsTrue(Game.Flow.BeginExpedition());
            Game.Flow.SelectExpedition(false);
            yield return WaitForExpeditionEntry();
            Game.Director.AutoTick = false;

            // Start Boss encounter and damage Golgora without mid-wait AI drift.
            Game.Encounter.BeginEncounter();
            yield return null;
            Game.Encounter.AutoTick = false;
            Assert.IsTrue(Game.Encounter.IsEncounterActive);
            Assert.IsNotNull(Game.Encounter.Boss);
            Game.Encounter.Boss.ReceiveDamage(new DamageRequest(1, DamageSourceType.FlashlightPulse, Vector3.zero, Vector3.forward, 50f));
            float expectedBossHp = Game.Encounter.Boss.CurrentHealth;

            // Let periodic autosave (~3s unscaled) persist snapshot WITHOUT calling OnApplicationQuit (forced-close simulation).
            float waited = 0f;
            while (waited < ExpeditionSnapshotService.AutosaveIntervalSeconds + 0.5f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return null;

            yield return SimulateRestartAndContinueSlot(2, runNormalQuitHook: false);
            Assert.AreEqual(GameFlowState.Expedition, Game.Flow.State);
            Assert.IsTrue(Game.Encounter.IsEncounterActive);
            Assert.IsNotNull(Game.Encounter.Boss);
            Assert.AreEqual(expectedBossHp, Game.Encounter.Boss.CurrentHealth, 0.1f);

            // Abandon explicitly -> removes active snapshot, queues CANCELADA, next load enters Vault.
            Game.Session.SetManualPause(true);
            Game.Hud.RefreshNow();
            Game.Flow.RequestAbandon();
            Game.Flow.AcceptConfirmation();
            yield return null;
            Assert.AreEqual(GameFlowState.Vault, Game.Flow.State);
            Assert.AreEqual(SlotHealth.ValidVault, SaveSlotService.GetSlotSummaries()[2].Health);
            Assert.IsTrue(SaveSlotService.GetPendingRankingItems().Any(p => p.record != null && p.record.status == "CANCELADA"));

            // Start fresh expedition and die -> active snapshot must clear immediately upon Defeat before ReturnToVault.
            Assert.IsTrue(Game.Flow.BeginExpedition());
            Game.Flow.SelectExpedition(false);
            yield return WaitForExpeditionEntry();
            Assert.AreEqual(SlotHealth.ValidExpedition, SaveSlotService.GetSlotSummaries()[2].Health);
            Game.Player.ForceSetHealthForTesting(0f);
            yield return null;
            Assert.AreEqual(SessionState.Defeat, Game.Session.State);
            Assert.AreEqual(SlotHealth.ValidVault, SaveSlotService.GetSlotSummaries()[2].Health);
        }

        [UnityTest]
        public IEnumerator ForcedCloseKeepsChangesAlreadySavedInVault()
        {
            yield return CreateSlotAndEnterVault(0, "");
            Station.Progress.owned[0] = 2;
            Station.Save();
            Assert.IsTrue(Game.Flow.BeginExpedition());
            Game.Flow.SelectExpedition(false);
            yield return WaitForExpeditionEntry();
            Game.Director.AutoTick = false;

            float deadline = Time.realtimeSinceStartup + 5f;
            while (!Travel.TryTravel(false, Game.Player) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(Game.Session.IsInWarehouse, "Entering the vault saves the prior collection.");
            Assert.IsTrue(Station.EquipCompanion(0, 0));
            Assert.IsTrue(Station.ReleaseCollectible(0));
            Assert.AreEqual(1, SaveSlotService.GetSlotSummaries()[0].OwnedCount, "The profile already persisted the release.");
            Assert.AreEqual(0, Station.Progress.equipped[0], "The companion is equipped before closing.");

            yield return SimulateRestartAndContinueSlot(0, runNormalQuitHook: false);

            Assert.IsTrue(Game.Session.IsInWarehouse);
            Assert.AreEqual((1, 0), (Station.Progress.OwnedCount, Station.Progress.equipped[0]),
                "Resuming must keep the already saved inventory and equipped companion.");
        }

        [UnityTest]
        public IEnumerator ResumingBossIntroRestoresProtectionAndMinionSuppression()
        {
            yield return CreateSlotAndEnterVault(0, "");
            Assert.IsTrue(Game.Flow.BeginExpedition());
            Game.Flow.SelectExpedition(false);
            yield return WaitForExpeditionEntry();

            Game.Director.AutoTick = Game.Encounter.AutoTick = false;
            Game.Session.Clock.SetGameplayTimeForTesting(Game.WaveSettings.BossStartTime);
            Game.Director.Step();
            Assert.IsTrue(Game.Director.IsBossPhase);
            Assert.IsTrue(Game.Encounter.IsEncounterActive);
            Assert.IsTrue(Game.Player.IsBossCinematicProtected);
            Game.Director.SuppressBossMinions(true);
            Assert.IsTrue(ExpeditionSnapshotService.CaptureAndSave(Game, Station));
            yield return SimulateRestartAndContinueSlot(0, runNormalQuitHook: false);

            Assert.IsTrue(Game.Encounter.IsEncounterActive);
            Assert.AreEqual(GolgoraState.Intro, Game.Encounter.Boss.State);
            Assert.AreEqual((true, SessionState.BossIntro, true),
                (Game.Player.IsBossCinematicProtected, Game.Session.State, Game.Director.BossMinionsSuppressed));
        }

        [UnityTest]
        public IEnumerator RankedProfile_PublishesResumedAndSubsequentAttempts_WithSameAndDistinctAttemptIds()
        {
            int port;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }

            var received = new List<(string method, string path, string body)>();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            bool serverRunning = true;
            var serverTask = System.Threading.Tasks.Task.Run(async () =>
            {
                while (serverRunning && listener.IsListening)
                {
                    try
                    {
                        HttpListenerContext ctx = await listener.GetContextAsync();
                        string body = string.Empty;
                        using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                            body = reader.ReadToEnd();
                        lock (received)
                            received.Add((ctx.Request.HttpMethod, ctx.Request.Url.AbsolutePath, body));
                        byte[] ok = Encoding.UTF8.GetBytes("{}");
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentType = "application/json";
                        ctx.Response.OutputStream.Write(ok, 0, ok.Length);
                        ctx.Response.Close();
                    }
                    catch (Exception) { break; }
                }
            });

            try
            {
                EventLeaderboardSync.DatabaseUrlOverride = $"http://127.0.0.1:{port}";
                EventLeaderboardSync.AuthTokenOverride = "test-token";
                EventLeaderboardSync.EnableNetworkInTests = false;

                // 1. Named profile starts an expedition offline, gains kills/time, pauses, quits, resumes, and wins.
                yield return CreateSlotAndEnterVault(0, "PILOTO_RANK");
                Assert.IsTrue(Game.Flow.BeginExpedition());
                Game.Flow.SelectExpedition(false);
                yield return WaitForExpeditionEntry();
                string firstAttemptId = EventLeaderboardSync.Instance.ActiveAttemptId;
                Assert.IsFalse(string.IsNullOrEmpty(firstAttemptId));

                Game.Director.AutoTick = false;
                yield return new WaitForSeconds(0.2f);

                // Resume offline -> must keep the exact same attemptId.
                yield return SimulateRestartAndContinueSlot(0, runNormalQuitHook: true);
                Assert.AreEqual(firstAttemptId, EventLeaderboardSync.Instance.ActiveAttemptId);

                // Trigger Victory -> record must keep original attemptId and score on ranking.
                var bossDeath = new ActorDeathEvent(9999, 15, false, true, Vector3.zero, Vector3.forward,
                    DamageSourceType.FlashlightPulse, 0, false, true);
                Game.Session.QueueBossDeath(in bossDeath);
                Game.Session.ResolveSimulationStepOutcome();
                Assert.AreEqual(SessionState.Victory, Game.Session.State);
                EventPlayerRecord firstRecord = EventLeaderboardSync.Instance.LastRecord;
                Assert.IsNotNull(firstRecord);
                Assert.AreEqual(firstAttemptId, firstRecord.sessionId);
                Assert.AreEqual("VICTORIA", firstRecord.status);
                Assert.AreEqual(EventLeaderboardSync.RawPoints(firstRecord.kills, firstRecord.collectibles, firstRecord.discoveredCount, firstRecord.highestRarity, firstRecord.survivalSeconds), firstRecord.score);
                Assert.IsTrue(SaveSlotService.GetPendingRankingItems().Any(i => i.record.sessionId == firstAttemptId));

                // 2. Overwrite slot 0 with a different profile -> first pending attempt MUST survive in outbox.
                Game.Flow.ReturnFromResult();
                yield return null;
                SaveSlotService.CreateNewGameInSlot(0, "OTRO_PILOTO");
                SaveSlotService.MarkSlotLoadCompleted();
                EventLeaderboardSync.SyncSessionNameFromActiveSlot();
                Assert.IsTrue(SaveSlotService.GetPendingRankingItems().Any(i => i.record.sessionId == firstAttemptId));

                // 3. Turn network back on -> pending historical record from replaced slot AND second attempt push cleanly.
                EventLeaderboardSync.EnableNetworkInTests = true;
                float uploadDeadline = Time.realtimeSinceStartup + 5f;
                while (SaveSlotService.GetPendingRankingItems().Any(i => i.record.sessionId == firstAttemptId) &&
                       Time.realtimeSinceStartup < uploadDeadline)
                {
                    yield return null;
                }
                Assert.IsFalse(SaveSlotService.GetPendingRankingItems().Any(i => i.record.sessionId == firstAttemptId),
                    "Pending record from previous slot owner should upload when connection returns.");

                // 4. Same named profile runs a second expedition with a DIFFERENT lower score -> distinct sessionId.
                Assert.IsTrue(Game.Flow.BeginExpedition());
                Game.Flow.SelectExpedition(true);
                yield return WaitForExpeditionEntry();
                string secondAttemptId = EventLeaderboardSync.Instance.ActiveAttemptId;
                Assert.AreNotEqual(firstAttemptId, secondAttemptId);
                Game.Player.ForceSetHealthForTesting(0f);
                yield return null;
                Assert.AreEqual(SessionState.Defeat, Game.Session.State);
                Assert.AreEqual(secondAttemptId, EventLeaderboardSync.Instance.LastRecord.sessionId);

                float secondDeadline = Time.realtimeSinceStartup + 5f;
                while (SaveSlotService.GetPendingRankingItems().Count > 0 && Time.realtimeSinceStartup < secondDeadline)
                    yield return null;

                lock (received)
                {
                    Assert.IsTrue(received.Any(r => r.method == "PUT" && r.path == $"/players/{firstAttemptId}.json" && r.body.Contains("\"VICTORIA\"")));
                    Assert.IsTrue(received.Any(r => r.method == "PUT" && r.path == $"/players/{secondAttemptId}.json" && r.body.Contains("\"DERROTA\"")));
                }
            }
            finally
            {
                serverRunning = false;
                try { listener.Stop(); listener.Close(); } catch (Exception) { }
                EventLeaderboardSync.EnableNetworkInTests = false;
                EventLeaderboardSync.DatabaseUrlOverride = null;
                EventLeaderboardSync.AuthTokenOverride = null;
            }
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
            void RestoreKey(string key, bool had, string val)
            {
                if (had) PlayerPrefs.SetString(key, val);
                else PlayerPrefs.DeleteKey(key);
            }
            RestoreKey(FusionStation.SaveKey, hadProdSave, prevProdSave);
            RestoreKey("EchoScrap.Collection.v1", hadLegacySave, prevLegacySave);
            RestoreKey(GameFlowController.ProfileKey, hadProfile, prevProfile);
            RestoreKey(EventLeaderboardSync.PlayerNamePrefKey, hadName, prevName);
            RestoreKey(EventLeaderboardSync.PlayerNamePrefKey + ".Test", hadNameTest, prevNameTest);
            RestoreKey("EchoScrap.Rank.v2", hadRank, prevRank);
            RestoreKey("EchoScrap.Rank.Test.v2", hadRankTest, prevRankTest);
            PlayerPrefs.Save();
        }
    }
}
