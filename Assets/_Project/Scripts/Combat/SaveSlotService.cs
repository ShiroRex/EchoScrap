using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace EchoScrap.Combat
{
    public enum SlotHealth
    {
        Empty = 0,
        ValidVault = 1,
        ValidExpedition = 2,
        ExpeditionDamagedRecoverable = 3,
        Corrupted = 4
    }

    public enum SlotSelectorMode
    {
        None = 0,
        Continue = 1,
        NewGame = 2
    }

    [Serializable]
    public sealed class SlotProfileData
    {
        public int schemaVersion = 1;
        public int slotIndex;
        public string profileId = "";
        public string playerName = "";
        public double totalPlaySeconds;
        public long createdAtUtcMs;
        public long updatedAtUtcMs;
        public long revision = 1;
        public bool hasActiveExpedition;
        public string activeAttemptId = "";
        public int bestLocalScore;
        public int completedRankedRuns;
        public FusionProgress fusion = new FusionProgress();

        public SlotProfileData Clone()
        {
            return new SlotProfileData
            {
                schemaVersion = schemaVersion,
                slotIndex = slotIndex,
                profileId = profileId ?? "",
                playerName = playerName ?? "",
                totalPlaySeconds = totalPlaySeconds,
                createdAtUtcMs = createdAtUtcMs,
                updatedAtUtcMs = updatedAtUtcMs,
                revision = revision,
                hasActiveExpedition = hasActiveExpedition,
                activeAttemptId = activeAttemptId ?? "",
                bestLocalScore = bestLocalScore,
                completedRankedRuns = completedRankedRuns,
                fusion = JsonUtility.FromJson<FusionProgress>(JsonUtility.ToJson(fusion ?? new FusionProgress())) ?? new FusionProgress()
            };
        }
    }

    [Serializable]
    public sealed class SaveIndexData
    {
        public int schemaVersion = 1;
        public int lastPlayedSlot = -1;
        public bool legacyMigrated;
        public long legacyMigratedAtUtcMs;
        public long nextSequence = 1;
    }

    [Serializable]
    public sealed class PendingRankingItem
    {
        public string ownerProfileId = "";
        public EventPlayerRecord record;
    }

    [Serializable]
    public sealed class RankingOutboxData
    {
        public int schemaVersion = 1;
        public long revision = 1;
        public List<PendingRankingItem> items = new List<PendingRankingItem>();
    }

    [Serializable]
    public sealed class ExpeditionSnapshotHeader
    {
        public int schemaVersion = 1;
        public string profileId = "";
        public string attemptId = "";
        public long revision = 1;
        public int modeMinutes = 3;
        public float gameplayTime;
        public bool isInWarehouse;
        public string fusionSnapshotJson = "";
    }

    public sealed class SlotSummary
    {
        public int SlotIndex;
        public SlotHealth Health;
        public string ProfileId = "";
        public string PlayerName = "";
        public int OwnedCount;
        public int DiscoveredCount;
        public double TotalPlaySeconds;
        public string ActiveAttemptId = "";
        public int BestLocalScore;
        public int CompletedRankedRuns;
        public string ErrorDetail = "";

        public bool IsValid => Health == SlotHealth.ValidVault || Health == SlotHealth.ValidExpedition;
        public bool CanContinue => IsValid;
        public bool CanRecoverVault => Health == SlotHealth.ExpeditionDamagedRecoverable;
        public bool CanInteractInContinue => CanContinue || CanRecoverVault;
        public bool IsOccupied => Health != SlotHealth.Empty;
        public int Capacity => FusionProgress.Capacity;
        public string DisplayTitle => Health switch
        {
            SlotHealth.Empty => "—",
            SlotHealth.Corrupted => string.IsNullOrEmpty(PlayerName) ? "ILEGIBLE" : PlayerName,
            _ => string.IsNullOrEmpty(PlayerName) ? "Práctica" : PlayerName
        };
        public string FormattedPlayTime => SaveSlotService.FormatPlayTime(TotalPlaySeconds);
        public string CollectiblesLabel => Health switch
        {
            SlotHealth.Empty => $"—/{FusionProgress.Capacity}",
            SlotHealth.Corrupted => $"--/{FusionProgress.Capacity}",
            _ => $"{OwnedCount}/{FusionProgress.Capacity}"
        };
        public string StatusLabel => Health switch
        {
            SlotHealth.Empty => "Vacío",
            SlotHealth.ValidVault => "En la bóveda",
            SlotHealth.ValidExpedition => "Expedición en curso",
            SlotHealth.ExpeditionDamagedRecoverable => "Expedición dañada",
            _ => "Archivo dañado"
        };
    }

    /// <summary>
    /// Versioned atomic storage for 3 independent profile slots, legacy migration,
    /// active playtime tracking, and expedition snapshot/outbox isolation.
    /// </summary>
    public static class SaveSlotService
    {
        public const int SlotCount = 3;
        private const int CurrentEnvelopeSchema = 1;
        private const int CurrentSlotSchema = 1;

        private static string storageRootOverride;
        private static bool? persistenceOverride;
        private static int activeSlotIndex = -1;
        private static string activeProfileId = "";
        private static SlotProfileData activeProfile;
        private static bool writesEnabled;
        private static string pendingExpeditionJson;
        private static readonly long[] highestKnownSlotRevision = new long[SlotCount];
        private static readonly long[] highestKnownExpeditionRevision = new long[SlotCount];
        private static bool? hasPendingRankingItemsCache;

        public static bool HasPendingRankingItems =>
            hasPendingRankingItemsCache ?? (hasPendingRankingItemsCache = GetPendingRankingItems().Count > 0).Value;

        // In-memory store used when SceneTransitionState.SaveProgress == false (PlayMode tests without disk).
        private static SaveIndexData transientIndex = new SaveIndexData();
        private static readonly SlotProfileData[] transientSlots = new SlotProfileData[SlotCount];
        private static readonly SlotHealth[] transientForcedHealth = new SlotHealth[SlotCount];
        private static readonly string[] transientExpeditions = new string[SlotCount];
        private static readonly RankingOutboxData transientOutbox = new RankingOutboxData();

        public static bool IsPersistent => (persistenceOverride ?? SceneTransitionState.SaveProgress);
        public static int ActiveSlotIndex => activeSlotIndex;
        public static string ActiveProfileId => activeProfileId;
        public static bool HasLoadedProfile => activeProfile != null && activeSlotIndex >= 0 && activeSlotIndex < SlotCount;
        public static bool CanWriteActiveProfile => writesEnabled && HasLoadedProfile;
        public static bool HasActiveExpeditionInLoadedSlot => HasLoadedProfile && activeProfile.hasActiveExpedition;
        public static bool HasPendingExpeditionSnapshot => !string.IsNullOrEmpty(pendingExpeditionJson);
        public static string PendingExpeditionJson => pendingExpeditionJson;

        public static long NextActiveExpeditionRevision()
        {
            if (!HasLoadedProfile) return 1;
            return highestKnownExpeditionRevision[activeSlotIndex] + 1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            storageRootOverride = null;
            ResetRuntimeState();
        }

        public static void ResetRuntimeState()
        {
            persistenceOverride = null;
            activeSlotIndex = -1;
            activeProfileId = "";
            activeProfile = null;
            writesEnabled = false;
            pendingExpeditionJson = null;
            Array.Clear(highestKnownSlotRevision, 0, SlotCount);
            Array.Clear(highestKnownExpeditionRevision, 0, SlotCount);
            transientIndex = new SaveIndexData();
            Array.Clear(transientSlots, 0, SlotCount);
            Array.Clear(transientForcedHealth, 0, SlotCount);
            Array.Clear(transientExpeditions, 0, SlotCount);
            transientOutbox.items.Clear();
            transientOutbox.revision = 1;
            hasPendingRankingItemsCache = null;
        }

        public static void SetStorageRootForTesting(string directoryPath)
        {
            storageRootOverride = string.IsNullOrWhiteSpace(directoryPath) ? null : directoryPath;
            Array.Clear(highestKnownSlotRevision, 0, SlotCount);
            Array.Clear(highestKnownExpeditionRevision, 0, SlotCount);
            activeSlotIndex = -1;
            activeProfileId = "";
            activeProfile = null;
            writesEnabled = false;
            pendingExpeditionJson = null;
        }

        public static void SetPersistenceOverride(bool? enabled)
        {
            persistenceOverride = enabled;
        }

        public static string GetStorageDirectory()
        {
            if (!string.IsNullOrEmpty(storageRootOverride)) return storageRootOverride;
            string folder = Application.isBatchMode ? "Saves_BatchTest" : "Saves";
            return Path.Combine(Application.persistentDataPath, folder);
        }

        public static string GetSlotFilePath(int slotIndex) =>
            Path.Combine(GetStorageDirectory(), $"slot_{slotIndex}.json");

        public static string GetExpeditionFilePath(int slotIndex) =>
            Path.Combine(GetStorageDirectory(), $"expedition_{slotIndex}.json");

        public static string GetIndexFilePath() =>
            Path.Combine(GetStorageDirectory(), "index.json");

        public static string GetOutboxFilePath() =>
            Path.Combine(GetStorageDirectory(), "ranking_outbox.json");

        public static string FormatPlayTime(double totalSeconds)
        {
            if (double.IsNaN(totalSeconds) || double.IsInfinity(totalSeconds) || totalSeconds < 0d)
                totalSeconds = 0d;
            long whole = (long)Math.Floor(totalSeconds);
            long hours = whole / 3600;
            long minutes = (whole % 3600) / 60;
            long seconds = whole % 60;
            return $"{hours:00}:{minutes:00}:{seconds:00}";
        }

        public static void EnsureMigrated()
        {
            if (!IsPersistent) return;
            var index = ReadIndex();
            if (index.legacyMigrated) return;

            if (Application.isBatchMode && string.IsNullOrEmpty(storageRootOverride))
            {
                index.legacyMigrated = true;
                index.legacyMigratedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                WriteIndex(index);
                return;
            }

            bool hasProdKey = PlayerPrefs.HasKey(FusionStation.SaveKey);
            bool hasLegacyKey = PlayerPrefs.HasKey("EchoScrap.Collection.v1");
            bool hasProfileKey = PlayerPrefs.HasKey(GameFlowController.ProfileKey);
            string nameKey = Application.isBatchMode ? EventLeaderboardSync.PlayerNamePrefKey + ".Test" : EventLeaderboardSync.PlayerNamePrefKey;
            string ledgerKey = Application.isBatchMode ? "EchoScrap.Rank.Test.v2" : "EchoScrap.Rank.v2";
            string rawName = PlayerPrefs.GetString(nameKey, "");
            string rawLedger = PlayerPrefs.GetString(ledgerKey, "");

            if (!hasProdKey && !hasLegacyKey && !hasProfileKey && string.IsNullOrEmpty(rawName) && string.IsNullOrEmpty(rawLedger))
            {
                index.legacyMigrated = true;
                index.legacyMigratedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                WriteIndex(index);
                return;
            }

            FusionProgress migratedFusion = null;
            bool validFusion = false;
            if (hasProdKey || hasLegacyKey)
            {
                string raw = PlayerPrefs.GetString(hasProdKey ? FusionStation.SaveKey : "EchoScrap.Collection.v1", "");
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        var parsed = JsonUtility.FromJson<FusionProgress>(raw);
                        if (parsed != null && parsed.owned != null && parsed.owned.Length > 0)
                        {
                            if (!hasProdKey) parsed.version = 4;
                            if (parsed.version >= 4)
                            {
                                parsed.RestoreAfterLoading();
                                migratedFusion = parsed;
                                validFusion = true;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        validFusion = false;
                    }
                }
            }

            EventLeaderboardSync.RankingLedger legacyLedger = null;
            if (!string.IsNullOrEmpty(rawLedger))
            {
                try { legacyLedger = JsonUtility.FromJson<EventLeaderboardSync.RankingLedger>(rawLedger); }
                catch (Exception) { legacyLedger = null; }
            }

            string legacyAlias = EventLeaderboardSync.SanitizeName(!string.IsNullOrEmpty(legacyLedger?.name) ? legacyLedger.name : rawName);
            bool hasPendingLedger = legacyLedger != null &&
                ((legacyLedger.pending != null && legacyLedger.pending.Count > 0) || !string.IsNullOrEmpty(legacyLedger.interrupted));

            if (!validFusion && !hasProfileKey && string.IsNullOrEmpty(legacyAlias) && !hasPendingLedger)
            {
                index.legacyMigrated = true;
                index.legacyMigratedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                WriteIndex(index);
                return;
            }

            int targetSlot = -1;
            if (validFusion || hasProfileKey || !string.IsNullOrEmpty(legacyAlias))
            {
                var summaries = GetSlotSummariesWithoutMigration();
                for (int i = 0; i < SlotCount; i++)
                {
                    if (summaries[i].Health == SlotHealth.Empty)
                    {
                        targetSlot = i;
                        break;
                    }
                }
                if (targetSlot < 0) return; // Keep un-migrated if no empty slot exists.
            }

            string migratedProfileId = "legacy";
            if (targetSlot >= 0)
            {
                migratedFusion ??= new FusionProgress();
                migratedFusion.RestoreAfterLoading();
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var profile = new SlotProfileData
                {
                    schemaVersion = CurrentSlotSchema,
                    slotIndex = targetSlot,
                    profileId = Guid.NewGuid().ToString("N"),
                    playerName = legacyAlias,
                    totalPlaySeconds = 0d,
                    createdAtUtcMs = now,
                    updatedAtUtcMs = now,
                    revision = Math.Max(1, index.nextSequence++),
                    hasActiveExpedition = false,
                    activeAttemptId = "",
                    fusion = migratedFusion
                };
                if (!WriteSlotFileVerified(targetSlot, profile)) return;
                migratedProfileId = profile.profileId;
                if (index.lastPlayedSlot < 0) index.lastPlayedSlot = targetSlot;
            }

            if (hasPendingLedger)
            {
                var outbox = ReadOutbox();
                if (legacyLedger.pending != null)
                {
                    foreach (var record in legacyLedger.pending)
                    {
                        if (record == null || string.IsNullOrEmpty(record.sessionId)) continue;
                        if (outbox.items.All(item => item?.record == null || item.record.sessionId != record.sessionId))
                            outbox.items.Add(new PendingRankingItem { ownerProfileId = migratedProfileId, record = record });
                    }
                }
                if (!string.IsNullOrEmpty(legacyLedger.interrupted))
                {
                    string cancelledId = legacyLedger.interrupted;
                    if (outbox.items.All(item => item?.record == null || item.record.sessionId != cancelledId))
                    {
                        outbox.items.Add(new PendingRankingItem
                        {
                            ownerProfileId = migratedProfileId,
                            record = new EventPlayerRecord { sessionId = cancelledId, status = "CANCELADA" }
                        });
                    }
                }
                if (!WriteOutbox(outbox)) return;
            }

            index.legacyMigrated = true;
            index.legacyMigratedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            WriteIndex(index);
        }

        public static SlotSummary[] GetSlotSummaries()
        {
            EnsureMigrated();
            return GetSlotSummariesWithoutMigration();
        }

        private static SlotSummary[] GetSlotSummariesWithoutMigration()
        {
            var result = new SlotSummary[SlotCount];
            for (int i = 0; i < SlotCount; i++)
                result[i] = InspectSlot(i, out _, out _);
            return result;
        }

        public static bool HasAnyValidSlot()
        {
            var summaries = GetSlotSummaries();
            for (int i = 0; i < summaries.Length; i++)
                if (summaries[i].IsValid) return true;
            return false;
        }

        public static bool HasAnyContinuableOrRecoverableSlot()
        {
            var summaries = GetSlotSummaries();
            for (int i = 0; i < summaries.Length; i++)
                if (summaries[i].CanInteractInContinue) return true;
            return false;
        }

        public static int GetLastPlayedSlot()
        {
            EnsureMigrated();
            return IsPersistent ? ReadIndex().lastPlayedSlot : transientIndex.lastPlayedSlot;
        }

        public static int GetDefaultSlotFocus(SlotSelectorMode mode)
        {
            var summaries = GetSlotSummaries();
            int lastPlayed = GetLastPlayedSlot();
            if (mode == SlotSelectorMode.Continue)
            {
                if (lastPlayed >= 0 && lastPlayed < SlotCount && summaries[lastPlayed].CanInteractInContinue)
                    return lastPlayed;
                for (int i = 0; i < SlotCount; i++)
                    if (summaries[i].CanContinue) return i;
                for (int i = 0; i < SlotCount; i++)
                    if (summaries[i].CanRecoverVault) return i;
                return 0;
            }
            for (int i = 0; i < SlotCount; i++)
                if (summaries[i].Health == SlotHealth.Empty) return i;
            return lastPlayed >= 0 && lastPlayed < SlotCount ? lastPlayed : 0;
        }

        public static bool SelectSlotForContinue(int slotIndex, out SlotSummary summary, out bool needsVaultRecovery)
        {
            needsVaultRecovery = false;
            summary = InspectSlot(slotIndex, out var loadedProfile, out var loadedExpedition);
            if (summary.CanRecoverVault)
            {
                needsVaultRecovery = true;
                return false;
            }
            if (!summary.CanContinue || loadedProfile == null)
                return false;

            ActivateLoadedProfile(slotIndex, loadedProfile, summary.Health == SlotHealth.ValidExpedition ? loadedExpedition : null, activateWritesNow: false);
            return true;
        }

        public static bool RecoverVaultInSlot(int slotIndex, out SlotSummary updatedSummary)
        {
            var before = InspectSlot(slotIndex, out var profile, out _);
            if (!before.CanRecoverVault || profile == null)
            {
                updatedSummary = before;
                return false;
            }

            if (!string.IsNullOrEmpty(profile.activeAttemptId) && !string.IsNullOrEmpty(profile.playerName))
                EnqueuePendingRecord(profile.profileId, new EventPlayerRecord { sessionId = profile.activeAttemptId, status = "CANCELADA" });

            profile.hasActiveExpedition = false;
            profile.activeAttemptId = "";
            profile.updatedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            profile.revision = NextSlotRevision(slotIndex, profile.revision);

            bool wrote = IsPersistent
                ? WriteSlotFileVerified(slotIndex, profile)
                : WriteTransientSlot(slotIndex, profile);
            if (!wrote)
            {
                updatedSummary = InspectSlot(slotIndex, out _, out _);
                return false;
            }

            DeleteExpeditionFiles(slotIndex);
            ActivateLoadedProfile(slotIndex, profile, null, activateWritesNow: false);
            updatedSummary = InspectSlot(slotIndex, out _, out _);
            return updatedSummary.Health == SlotHealth.ValidVault;
        }

        public static bool CreateNewGameInSlot(int slotIndex, string sanitizedName)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount) return false;

            // Cancel previous interrupted ranked attempt from the replaced profile without losing its outbox owner.
            var previousSummary = InspectSlot(slotIndex, out var previousProfile, out _);
            if (previousSummary.IsOccupied && previousProfile != null &&
                !string.IsNullOrEmpty(previousProfile.activeAttemptId) &&
                !string.IsNullOrEmpty(previousProfile.playerName))
            {
                EnqueuePendingRecord(previousProfile.profileId, new EventPlayerRecord
                {
                    sessionId = previousProfile.activeAttemptId,
                    status = "CANCELADA"
                });
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var freshFusion = new FusionProgress();
            freshFusion.Validate();
            var created = new SlotProfileData
            {
                schemaVersion = CurrentSlotSchema,
                slotIndex = slotIndex,
                profileId = Guid.NewGuid().ToString("N"),
                playerName = sanitizedName ?? "",
                totalPlaySeconds = 0d,
                createdAtUtcMs = now,
                updatedAtUtcMs = now,
                revision = Math.Max(1, NextSequence()),
                hasActiveExpedition = false,
                activeAttemptId = "",
                fusion = freshFusion
            };

            bool wrote = IsPersistent
                ? WriteSlotFileVerified(slotIndex, created)
                : WriteTransientSlot(slotIndex, created);
            if (!wrote) return false;

            DeleteExpeditionFiles(slotIndex);
            ActivateLoadedProfile(slotIndex, created, null, activateWritesNow: false);
            return true;
        }

        public static void MarkSlotLoadCompleted()
        {
            if (!HasLoadedProfile) return;
            writesEnabled = true;
            SetLastPlayedSlot(activeSlotIndex);
        }

        public static void FlushAndSuspendActiveProfile(FusionProgress currentFusion = null)
        {
            if (CanWriteActiveProfile)
            {
                if (currentFusion != null) SaveActiveFusionProgress(currentFusion);
                else SaveActiveProfileMetadata();
            }
            writesEnabled = false;
            pendingExpeditionJson = null;
            activeProfile = null;
            activeSlotIndex = -1;
            activeProfileId = "";
        }

        public static void EnsureLoadedFallbackForDirectArenaEntry()
        {
            if (HasLoadedProfile) return;
            var summaries = GetSlotSummaries();
            int candidate = GetDefaultSlotFocus(SlotSelectorMode.Continue);
            if (candidate >= 0 && candidate < SlotCount && summaries[candidate].CanContinue &&
                SelectSlotForContinue(candidate, out _, out _))
            {
                MarkSlotLoadCompleted();
                return;
            }
            CreateNewGameInSlot(0, "");
            MarkSlotLoadCompleted();
        }

        public static FusionProgress CloneActiveFusionProgress()
        {
            if (activeProfile?.fusion == null)
            {
                var empty = new FusionProgress();
                empty.RestoreAfterLoading();
                return empty;
            }
            var clone = JsonUtility.FromJson<FusionProgress>(JsonUtility.ToJson(activeProfile.fusion)) ?? new FusionProgress();
            clone.RestoreAfterLoading();
            return clone;
        }

        public static string ActivePlayerName => activeProfile?.playerName ?? "";
        public static double ActivePlayTimeSeconds => activeProfile?.totalPlaySeconds ?? 0d;

        public static void RecordActivePlaytime(float deltaSeconds)
        {
            if (!CanWriteActiveProfile || deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
                return;
            activeProfile.totalPlaySeconds += Math.Min(deltaSeconds, 0.25f);
        }

        public static bool SaveActiveFusionProgress(FusionProgress progress)
        {
            if (!CanWriteActiveProfile || progress == null) return false;
            var snapshot = JsonUtility.FromJson<FusionProgress>(JsonUtility.ToJson(progress)) ?? new FusionProgress();
            snapshot.Validate();
            activeProfile.fusion = snapshot;
            return SaveActiveProfileMetadata();
        }

        public static bool SaveActiveProfileMetadata()
        {
            if (!CanWriteActiveProfile) return false;
            activeProfile.updatedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            activeProfile.revision = NextSlotRevision(activeSlotIndex, activeProfile.revision);
            return WriteActiveSlotChecked(activeProfile);
        }

        public static bool SetActiveExpeditionState(string attemptId, string serializedExpeditionJson, long snapshotRevision, FusionProgress currentFusion = null)
        {
            if (!CanWriteActiveProfile || string.IsNullOrEmpty(attemptId) || string.IsNullOrEmpty(serializedExpeditionJson))
                return false;

            if (snapshotRevision <= highestKnownExpeditionRevision[activeSlotIndex])
                return false;

            if (IsPersistent)
            {
                string expPath = GetExpeditionFilePath(activeSlotIndex);
                if (TryReadVerifiedPayload(expPath, expPath + ".bak", out string diskExpJson, out long diskRev) &&
                    TryParseExpeditionHeader(diskExpJson, out var diskHeader))
                {
                    if (diskHeader.profileId == activeProfileId && snapshotRevision <= diskRev)
                    {
                        highestKnownExpeditionRevision[activeSlotIndex] = Math.Max(highestKnownExpeditionRevision[activeSlotIndex], diskRev);
                        return false;
                    }
                }
            }

            if (currentFusion != null)
            {
                var clone = JsonUtility.FromJson<FusionProgress>(JsonUtility.ToJson(currentFusion)) ?? new FusionProgress();
                clone.Validate();
                activeProfile.fusion = clone;
            }

            if (IsPersistent)
            {
                if (!WriteVerifiedJsonFile(GetExpeditionFilePath(activeSlotIndex), serializedExpeditionJson, snapshotRevision))
                    return false;
            }
            else
            {
                transientExpeditions[activeSlotIndex] = serializedExpeditionJson;
            }

            highestKnownExpeditionRevision[activeSlotIndex] = snapshotRevision;
            pendingExpeditionJson = serializedExpeditionJson;
            activeProfile.hasActiveExpedition = true;
            activeProfile.activeAttemptId = attemptId;
            return SaveActiveProfileMetadata();
        }

        public static bool ClearActiveExpeditionState(FusionProgress finalFusion = null)
        {
            if (!CanWriteActiveProfile) return false;
            if (finalFusion != null)
            {
                var clone = JsonUtility.FromJson<FusionProgress>(JsonUtility.ToJson(finalFusion)) ?? new FusionProgress();
                clone.Validate();
                activeProfile.fusion = clone;
            }
            activeProfile.hasActiveExpedition = false;
            activeProfile.activeAttemptId = "";
            pendingExpeditionJson = null;
            bool saved = SaveActiveProfileMetadata();
            DeleteExpeditionFiles(activeSlotIndex);
            return saved;
        }

        public static void ClearPendingExpeditionAfterRestore()
        {
            pendingExpeditionJson = null;
        }

        public static string GetActiveAttemptId() => activeProfile?.activeAttemptId ?? "";

        public static void RecordActiveProfileBestScore(int finalScore)
        {
            if (!HasLoadedProfile) return;
            activeProfile.completedRankedRuns++;
            if (finalScore > activeProfile.bestLocalScore)
                activeProfile.bestLocalScore = finalScore;
            if (CanWriteActiveProfile) SaveActiveProfileMetadata();
        }

        public static List<PendingRankingItem> GetPendingRankingItems()
        {
            var outbox = ReadOutbox();
            return outbox.items ?? new List<PendingRankingItem>();
        }

        public static bool EnqueuePendingRecord(string ownerProfileId, EventPlayerRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.sessionId)) return false;
            var outbox = ReadOutbox();
            outbox.items ??= new List<PendingRankingItem>();
            int existing = outbox.items.FindIndex(item => item?.record != null && item.record.sessionId == record.sessionId);
            var entry = new PendingRankingItem
            {
                ownerProfileId = ownerProfileId ?? "",
                record = record
            };
            if (existing >= 0) outbox.items[existing] = entry;
            else outbox.items.Add(entry);
            return WriteOutbox(outbox);
        }

        public static bool RemovePendingRecord(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return false;
            var outbox = ReadOutbox();
            outbox.items ??= new List<PendingRankingItem>();
            int removed = outbox.items.RemoveAll(item => item?.record == null || item.record.sessionId == sessionId);
            return removed > 0 && WriteOutbox(outbox);
        }

        // Testing helpers to simulate crash/corruption scenarios deterministically.
        public static void DeleteAllSlotsAndMigrationForTesting()
        {
            if (Application.isBatchMode || !string.IsNullOrEmpty(storageRootOverride))
            {
                string dir = GetStorageDirectory();
                try
                {
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
                catch (Exception)
                {
                }
            }
            ResetRuntimeState();
        }

        public static void ForceCorruptTransientSlotForTesting(int slotIndex, SlotHealth health)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount) return;
            transientForcedHealth[slotIndex] = health;
            if (health == SlotHealth.Corrupted) transientSlots[slotIndex] = null;
            if (health == SlotHealth.ExpeditionDamagedRecoverable && transientSlots[slotIndex] != null)
            {
                transientSlots[slotIndex].hasActiveExpedition = true;
                transientExpeditions[slotIndex] = null;
            }
        }

        private static void ActivateLoadedProfile(int slotIndex, SlotProfileData profile, string expeditionJson, bool activateWritesNow)
        {
            activeSlotIndex = slotIndex;
            activeProfileId = profile.profileId;
            activeProfile = profile.Clone();
            pendingExpeditionJson = expeditionJson;
            highestKnownSlotRevision[slotIndex] = Math.Max(highestKnownSlotRevision[slotIndex], profile.revision);
            if (!string.IsNullOrEmpty(expeditionJson) && TryParseExpeditionHeader(expeditionJson, out var header))
                highestKnownExpeditionRevision[slotIndex] = Math.Max(highestKnownExpeditionRevision[slotIndex], header.revision);
            writesEnabled = activateWritesNow;
        }

        public static bool TryCommitProfileRevision(SlotProfileData candidate)
        {
            if (candidate == null || candidate.slotIndex < 0 || candidate.slotIndex >= SlotCount || string.IsNullOrEmpty(candidate.profileId))
                return false;
            int slotIndex = candidate.slotIndex;
            if (!IsPersistent)
            {
                var existing = transientSlots[slotIndex];
                if (existing != null && (existing.profileId != candidate.profileId || candidate.revision <= existing.revision))
                    return false;
                if (candidate.revision <= highestKnownSlotRevision[slotIndex])
                    return false;
                return WriteTransientSlot(slotIndex, candidate);
            }

            string path = GetSlotFilePath(slotIndex);
            if (TryReadVerifiedPayload(path, path + ".bak", out string diskJson, out long diskRev) &&
                TryParseSlotProfile(diskJson, slotIndex, out var diskProfile))
            {
                if (diskProfile.profileId != candidate.profileId || candidate.revision <= diskRev)
                    return false;
            }
            if (candidate.revision <= highestKnownSlotRevision[slotIndex])
                return false;
            return WriteSlotFileVerified(slotIndex, candidate);
        }

        private static void SetLastPlayedSlot(int slotIndex)
        {
            if (!IsPersistent)
            {
                transientIndex.lastPlayedSlot = slotIndex;
                return;
            }
            var index = ReadIndex();
            index.lastPlayedSlot = slotIndex;
            WriteIndex(index);
        }

        private static SlotSummary InspectSlot(int slotIndex, out SlotProfileData profile, out string expeditionJson)
        {
            profile = null;
            expeditionJson = null;
            var summary = new SlotSummary { SlotIndex = slotIndex, Health = SlotHealth.Empty };
            if (slotIndex < 0 || slotIndex >= SlotCount)
            {
                summary.Health = SlotHealth.Corrupted;
                summary.ErrorDetail = "Índice de archivo inválido.";
                return summary;
            }

            if (!IsPersistent)
            {
                if (transientForcedHealth[slotIndex] == SlotHealth.Corrupted)
                {
                    summary.Health = SlotHealth.Corrupted;
                    summary.ErrorDetail = "Archivo de guardado dañado.";
                    return summary;
                }
                profile = transientSlots[slotIndex]?.Clone();
                if (profile == null) return summary;
            }
            else
            {
                string path = GetSlotFilePath(slotIndex);
                string bakPath = path + ".bak";
                if (!File.Exists(path) && !File.Exists(bakPath))
                    return summary;

                if (!TryReadVerifiedPayload(path, bakPath, out string slotJson, out _))
                {
                    summary.Health = SlotHealth.Corrupted;
                    summary.ErrorDetail = "No se pudo verificar la integridad del archivo.";
                    return summary;
                }
                if (!TryParseSlotProfile(slotJson, slotIndex, out profile))
                {
                    summary.Health = SlotHealth.Corrupted;
                    summary.ErrorDetail = "Estructura del archivo incompatible o dañada.";
                    profile = null;
                    return summary;
                }
            }

            profile.fusion ??= new FusionProgress();
            profile.fusion.Validate();
            summary.ProfileId = profile.profileId;
            summary.PlayerName = profile.playerName ?? "";
            summary.OwnedCount = profile.fusion.OwnedCount;
            summary.DiscoveredCount = profile.fusion.DiscoveredCount;
            summary.TotalPlaySeconds = profile.totalPlaySeconds;
            summary.ActiveAttemptId = profile.activeAttemptId ?? "";
            summary.BestLocalScore = profile.bestLocalScore;
            summary.CompletedRankedRuns = profile.completedRankedRuns;

            if (!profile.hasActiveExpedition)
            {
                summary.Health = SlotHealth.ValidVault;
                return summary;
            }

            if (ValidateExpeditionForProfile(slotIndex, profile, out expeditionJson))
            {
                summary.Health = SlotHealth.ValidExpedition;
                return summary;
            }

            summary.Health = SlotHealth.ExpeditionDamagedRecoverable;
            summary.ErrorDetail = "Estado de expedición ausente o dañado.";
            return summary;
        }

        private static bool ValidateExpeditionForProfile(int slotIndex, SlotProfileData profile, out string expeditionJson)
        {
            expeditionJson = null;
            if (!IsPersistent)
            {
                if (transientForcedHealth[slotIndex] == SlotHealth.ExpeditionDamagedRecoverable)
                    return false;
                expeditionJson = transientExpeditions[slotIndex];
            }
            else
            {
                string expPath = GetExpeditionFilePath(slotIndex);
                if (!TryReadVerifiedPayload(expPath, expPath + ".bak", out expeditionJson, out _))
                    return false;
            }

            if (string.IsNullOrEmpty(expeditionJson)) return false;
            if (!TryParseExpeditionHeader(expeditionJson, out var header)) return false;
            if (header.profileId != profile.profileId) return false;
            if (string.IsNullOrEmpty(header.attemptId) || (!string.IsNullOrEmpty(profile.activeAttemptId) && header.attemptId != profile.activeAttemptId))
                return false;
            if (header.modeMinutes != 3 && header.modeMinutes != 8) return false;
            if (float.IsNaN(header.gameplayTime) || float.IsInfinity(header.gameplayTime) || header.gameplayTime < 0f)
                return false;
            if (expeditionJson.IndexOf("\"session\":", StringComparison.Ordinal) >= 0 &&
                !ExpeditionSnapshotService.ValidateSnapshotPayload(expeditionJson))
                return false;
            return true;
        }

        private static bool TryParseExpeditionHeader(string json, out ExpeditionSnapshotHeader header)
        {
            header = null;
            try
            {
                header = JsonUtility.FromJson<ExpeditionSnapshotHeader>(json);
                return header != null && header.schemaVersion >= 1 && !string.IsNullOrEmpty(header.profileId);
            }
            catch (Exception)
            {
                header = null;
                return false;
            }
        }

        private static bool TryParseSlotProfile(string json, int expectedSlotIndex, out SlotProfileData profile)
        {
            profile = null;
            try
            {
                profile = JsonUtility.FromJson<SlotProfileData>(json);
                if (profile == null || profile.schemaVersion < 1 || profile.slotIndex != expectedSlotIndex)
                    return false;
                if (string.IsNullOrEmpty(profile.profileId) || profile.revision < 1)
                    return false;
                if (double.IsNaN(profile.totalPlaySeconds) || double.IsInfinity(profile.totalPlaySeconds) || profile.totalPlaySeconds < 0d)
                    return false;
                if (profile.fusion == null || profile.fusion.owned == null || profile.fusion.owned.Length == 0)
                    return false;
                return true;
            }
            catch (Exception)
            {
                profile = null;
                return false;
            }
        }

        private static bool WriteActiveSlotChecked(SlotProfileData profile)
        {
            if (profile == null || profile.slotIndex != activeSlotIndex || profile.profileId != activeProfileId)
                return false;

            if (!IsPersistent)
            {
                if (profile.revision <= highestKnownSlotRevision[profile.slotIndex] && transientSlots[profile.slotIndex] != null)
                    return false;
                highestKnownSlotRevision[profile.slotIndex] = profile.revision;
                return WriteTransientSlot(profile.slotIndex, profile);
            }

            if (profile.revision <= highestKnownSlotRevision[profile.slotIndex])
                return false;

            // Reject late writes against a newer revision or replaced owner on disk.
            string path = GetSlotFilePath(profile.slotIndex);
            if (TryReadVerifiedPayload(path, path + ".bak", out string diskJson, out long diskRev) &&
                TryParseSlotProfile(diskJson, profile.slotIndex, out var diskProfile))
            {
                if (diskProfile.profileId != profile.profileId || profile.revision <= diskRev)
                    return false;
            }

            if (!WriteSlotFileVerified(profile.slotIndex, profile))
                return false;
            highestKnownSlotRevision[profile.slotIndex] = profile.revision;
            return true;
        }

        private static bool WriteTransientSlot(int slotIndex, SlotProfileData profile)
        {
            transientForcedHealth[slotIndex] = SlotHealth.Empty;
            transientSlots[slotIndex] = profile.Clone();
            highestKnownSlotRevision[slotIndex] = Math.Max(highestKnownSlotRevision[slotIndex], profile.revision);
            return true;
        }

        private static long NextSequence()
        {
            if (!IsPersistent) return ++transientIndex.nextSequence;
            var index = ReadIndex();
            long seq = ++index.nextSequence;
            WriteIndex(index);
            return seq;
        }

        private static long NextSlotRevision(int slotIndex, long currentRevision)
        {
            long known = slotIndex >= 0 && slotIndex < SlotCount ? highestKnownSlotRevision[slotIndex] : 0;
            return Math.Max(currentRevision, known) + 1;
        }

        private static SaveIndexData ReadIndex()
        {
            if (!IsPersistent) return transientIndex;
            string path = GetIndexFilePath();
            if (TryReadVerifiedPayload(path, path + ".bak", out string payload, out _))
            {
                try
                {
                    var parsed = JsonUtility.FromJson<SaveIndexData>(payload);
                    if (parsed != null && parsed.schemaVersion >= 1) return parsed;
                }
                catch (Exception) { }
            }
            return new SaveIndexData();
        }

        private static bool WriteIndex(SaveIndexData index)
        {
            if (!IsPersistent)
            {
                transientIndex = index;
                return true;
            }
            return WriteVerifiedJsonFile(GetIndexFilePath(), JsonUtility.ToJson(index), Math.Max(1, index.nextSequence));
        }

        private static RankingOutboxData ReadOutbox()
        {
            if (!IsPersistent) return transientOutbox;
            string path = GetOutboxFilePath();
            if (TryReadVerifiedPayload(path, path + ".bak", out string payload, out _))
            {
                try
                {
                    var parsed = JsonUtility.FromJson<RankingOutboxData>(payload);
                    if (parsed != null && parsed.schemaVersion >= 1)
                    {
                        parsed.items ??= new List<PendingRankingItem>();
                        return parsed;
                    }
                }
                catch (Exception) { }
            }
            return new RankingOutboxData();
        }

        private static bool WriteOutbox(RankingOutboxData outbox)
        {
            hasPendingRankingItemsCache = outbox?.items != null && outbox.items.Count > 0;
            if (!IsPersistent) return true;
            outbox.revision = Math.Max(1, outbox.revision + 1);
            return WriteVerifiedJsonFile(GetOutboxFilePath(), JsonUtility.ToJson(outbox), outbox.revision);
        }

        private static bool WriteSlotFileVerified(int slotIndex, SlotProfileData profile)
        {
            string payload = JsonUtility.ToJson(profile);
            if (!WriteVerifiedJsonFile(GetSlotFilePath(slotIndex), payload, profile.revision))
                return false;
            highestKnownSlotRevision[slotIndex] = Math.Max(highestKnownSlotRevision[slotIndex], profile.revision);
            return true;
        }

        private static void DeleteExpeditionFiles(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount) return;
            highestKnownExpeditionRevision[slotIndex] = 0;
            if (!IsPersistent)
            {
                transientExpeditions[slotIndex] = null;
                if (transientForcedHealth[slotIndex] == SlotHealth.ExpeditionDamagedRecoverable)
                    transientForcedHealth[slotIndex] = SlotHealth.Empty;
                return;
            }
            string path = GetExpeditionFilePath(slotIndex);
            TryDeleteFile(path);
            TryDeleteFile(path + ".tmp");
            TryDeleteFile(path + ".bak");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception) { }
        }

        private static bool WriteVerifiedJsonFile(string targetPath, string payloadJson, long revision)
        {
            try
            {
                string dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmpPath = targetPath + ".tmp";
                string bakPath = targetPath + ".bak";
                string sha = ComputeSha256Hex(payloadJson);
                string envelope = $"{{\"schemaVersion\":{CurrentEnvelopeSchema},\"revision\":{revision},\"sha256\":\"{sha}\",\"data\":{payloadJson}}}";
                byte[] utf8 = new UTF8Encoding(false).GetBytes(envelope);
                using (var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(utf8, 0, utf8.Length);
                    stream.Flush(true);
                }

                string verifiedText = File.ReadAllText(tmpPath, Encoding.UTF8);
                if (!TryVerifyEnvelopeText(verifiedText, out _, out long readRev) || readRev != revision)
                {
                    TryDeleteFile(tmpPath);
                    return false;
                }

                if (File.Exists(targetPath))
                    File.Replace(tmpPath, targetPath, bakPath, true);
                else
                    File.Move(tmpPath, targetPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"SaveSlotService: error al escribir '{targetPath}': {ex.Message}");
                return false;
            }
        }

        private static bool TryReadVerifiedPayload(string primaryPath, string backupPath, out string payloadJson, out long revision)
        {
            payloadJson = null;
            revision = 0;
            if (TryReadSingleVerifiedFile(primaryPath, out payloadJson, out revision))
                return true;
            if (!string.IsNullOrEmpty(backupPath) && TryReadSingleVerifiedFile(backupPath, out payloadJson, out revision))
                return true;
            return false;
        }

        private static bool TryReadSingleVerifiedFile(string path, out string payloadJson, out long revision)
        {
            payloadJson = null;
            revision = 0;
            try
            {
                if (!File.Exists(path)) return false;
                string text = File.ReadAllText(path, Encoding.UTF8);
                return TryVerifyEnvelopeText(text, out payloadJson, out revision);
            }
            catch (Exception)
            {
                return false;
            }
        }

        [Serializable]
        private sealed class EnvelopeHeader
        {
            public int schemaVersion;
            public long revision;
            public string sha256;
        }

        private static bool TryVerifyEnvelopeText(string text, out string payloadJson, out long revision)
        {
            payloadJson = null;
            revision = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            const string marker = "\"data\":";
            int markerIdx = text.IndexOf(marker, StringComparison.Ordinal);
            if (markerIdx < 0) return false;
            int dataStart = markerIdx + marker.Length;
            while (dataStart < text.Length && char.IsWhiteSpace(text[dataStart])) dataStart++;
            int lastBrace = text.LastIndexOf('}');
            if (dataStart >= lastBrace || text[dataStart] != '{') return false;

            // Find the exact closing brace matching the outer envelope vs payload.
            int dataEnd = FindMatchingJsonBrace(text, dataStart);
            if (dataEnd < dataStart || dataEnd >= lastBrace) return false;
            payloadJson = text.Substring(dataStart, dataEnd - dataStart + 1);

            EnvelopeHeader header;
            try { header = JsonUtility.FromJson<EnvelopeHeader>(text); }
            catch (Exception) { return false; }

            if (header == null || header.schemaVersion != CurrentEnvelopeSchema || string.IsNullOrEmpty(header.sha256))
                return false;

            string expectedSha = ComputeSha256Hex(payloadJson);
            if (!string.Equals(header.sha256, expectedSha, StringComparison.OrdinalIgnoreCase))
                return false;

            revision = header.revision;
            return true;
        }

        private static int FindMatchingJsonBrace(string json, int openIndex)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int i = openIndex; i < json.Length; i++)
            {
                char c = json[i];
                if (escaped) { escaped = false; continue; }
                if (inString)
                {
                    if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return i;
            }
            return -1;
        }

        private static string ComputeSha256Hex(string input)
        {
            using var sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(input ?? "");
            byte[] hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
