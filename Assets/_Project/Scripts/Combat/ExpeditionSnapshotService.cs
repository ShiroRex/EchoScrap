using System;
using System.Collections.Generic;
using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Captures and silently restores full simulation snapshots of an active expedition,
    /// bound to the active save profile, attempt ID, and monotonic revision.
    /// </summary>
    public static class ExpeditionSnapshotService
    {
        public const float AutosaveIntervalSeconds = 3f;

        private static readonly PauseReason[] TransientPauseReasonsToClear =
        {
            PauseReason.Manual,
            PauseReason.FocusLost,
            PauseReason.FusionMenu,
            PauseReason.CompanionMenu,
            PauseReason.SessionEnded
        };

        public static bool IsRestoring { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            IsRestoring = false;
        }

        public static bool ValidateSnapshotPayload(string json)
        {
            return TryParseValidSnapshot(json, out _);
        }

        internal static bool TryParseValidSnapshot(string json, out ExpeditionSnapshotData snapshot)
        {
            snapshot = null;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                snapshot = JsonUtility.FromJson<ExpeditionSnapshotData>(json);
                if (snapshot == null || snapshot.schemaVersion < 1) return false;
                if (string.IsNullOrEmpty(snapshot.profileId) || string.IsNullOrEmpty(snapshot.attemptId)) return false;
                if (snapshot.revision < 1) return false;
                if (snapshot.modeMinutes != 3 && snapshot.modeMinutes != 8) return false;
                if (float.IsNaN(snapshot.gameplayTime) || float.IsInfinity(snapshot.gameplayTime) || snapshot.gameplayTime < 0f)
                    return false;
                if (snapshot.session == null || snapshot.player == null ||
                    snapshot.player.motor == null || snapshot.player.stats == null ||
                    snapshot.progression == null || snapshot.waveDirector == null)
                    return false;
                return true;
            }
            catch (Exception)
            {
                snapshot = null;
                return false;
            }
        }

        public static bool CaptureFromSession(CombatSession session, bool forceImmediate = false)
        {
            if (session == null) return false;
            var game = session.GetComponentInParent<CombatGameBootstrap>();
            if (game == null) game = UnityEngine.Object.FindAnyObjectByType<CombatGameBootstrap>();
            if (game == null || game.Flow == null) return false;
            return CaptureAndSave(game, game.Flow.Station, forceImmediate);
        }

        public static bool CaptureAndSave(CombatGameBootstrap game, FusionStation station, bool forceImmediate = false)
        {
            if (IsRestoring) return false;
            if (game == null || !game.IsInitialized || game.Session == null || game.Player == null || !game.Player.IsAlive)
                return false;
            if (!SaveSlotService.CanWriteActiveProfile) return false;
            if (game.Flow != null && game.Flow.State != GameFlowState.Expedition) return false;
            if (game.Session.State == SessionState.Victory || game.Session.State == SessionState.Defeat) return false;

            if (station == null)
                station = UnityEngine.Object.FindAnyObjectByType<FusionStation>();
            WarehouseTravel travel = UnityEngine.Object.FindAnyObjectByType<WarehouseTravel>();
            LeaderboardAttemptSnapshot leaderboardSnap = EventLeaderboardSync.Instance != null
                ? EventLeaderboardSync.Instance.CaptureSnapshot()
                : new LeaderboardAttemptSnapshot();

            string attemptId = leaderboardSnap != null && leaderboardSnap.active && !string.IsNullOrEmpty(leaderboardSnap.attemptId)
                ? leaderboardSnap.attemptId
                : SaveSlotService.GetActiveAttemptId();
            if (string.IsNullOrEmpty(attemptId))
                attemptId = Guid.NewGuid().ToString("N");

            long revision = SaveSlotService.NextActiveExpeditionRevision();
            int modeMinutes = ResolveModeMinutes(game);

            string fusionJson = string.Empty;
            if (station != null && station.Progress != null)
            {
                station.Progress.Validate();
                fusionJson = JsonUtility.ToJson(station.Progress);
            }
            else
            {
                FusionProgress activeFusion = SaveSlotService.CloneActiveFusionProgress();
                fusionJson = JsonUtility.ToJson(activeFusion);
            }

            var snapshot = new ExpeditionSnapshotData
            {
                schemaVersion = ExpeditionSnapshotData.CurrentSchemaVersion,
                profileId = SaveSlotService.ActiveProfileId,
                attemptId = attemptId,
                revision = revision,
                modeMinutes = modeMinutes,
                gameplayTime = game.Session.Clock.GameplayTime,
                isInWarehouse = game.Session.IsInWarehouse,
                fusionSnapshotJson = fusionJson,
                sceneName = game.gameObject.scene.name ?? string.Empty,
                rng = game.Session.Rng.CaptureState(),
                session = game.Session.CaptureSnapshot(),
                travel = travel != null ? travel.CaptureSnapshot() : new WarehouseTravelSnapshot(),
                progression = game.Progression != null ? game.Progression.CaptureSnapshot() : new CombatProgressionSnapshot(),
                draft = game.Draft != null ? game.Draft.CaptureSnapshot() : new UpgradeDraftSnapshot(),
                weapon = game.Weapon != null ? game.Weapon.CaptureSnapshot() : new FlashlightWeaponSnapshot(),
                waveDirector = game.Director != null ? game.Director.CaptureSnapshot() : new WaveDirectorSnapshot(),
                bossEncounter = game.Encounter != null ? game.Encounter.CaptureSnapshot() : new BossEncounterSnapshot(),
                bossArena = game.BossArena != null ? game.BossArena.CaptureSnapshot() : new BossArenaSnapshot(),
                hud = game.Hud != null ? game.Hud.CaptureSnapshot() : new HudSnapshot(),
                leaderboard = leaderboardSnap ?? new LeaderboardAttemptSnapshot()
            };

            game.Player.CaptureSnapshot(snapshot.player);

            GolgoraActor boss = game.Encounter != null ? game.Encounter.Boss : null;
            snapshot.golgora = boss != null ? boss.CaptureGolgoraSnapshot() : new GolgoraSnapshot();
            snapshot.magmaRings = boss != null && boss.RingPool != null
                ? boss.RingPool.CaptureSnapshot()
                : new MagmaRingPoolSnapshot();

            var nonBossActors = new List<EnemyActor>(EnemyActor.ActiveEnemies.Count);
            IReadOnlyList<EnemyActor> activeEnemies = EnemyActor.ActiveEnemies;
            for (int i = 0; i < activeEnemies.Count; i++)
            {
                EnemyActor actor = activeEnemies[i];
                if (actor == null || !actor.gameObject.activeInHierarchy || actor.ArchetypeId == EnemyArchetypeId.GolgoraBoss)
                    continue;
                nonBossActors.Add(actor);
                snapshot.enemies.Add(actor.CaptureSnapshot());
            }

            var enemyLookup = new List<EnemyActor>(nonBossActors.Count + 1);
            enemyLookup.AddRange(nonBossActors);
            if (boss != null && boss.gameObject.activeInHierarchy)
                enemyLookup.Add(boss);

            EliteProjectilePool projectilePool = game.Session.GetComponent<EliteProjectilePool>();
            snapshot.eliteProjectiles = projectilePool != null ? projectilePool.CaptureSnapshot() : new EliteProjectilePoolSnapshot();
            snapshot.puddles = game.Hazards != null ? game.Hazards.CaptureSnapshot() : new CorrosivePuddlePoolSnapshot();
            snapshot.mothExplosions = game.MothExplosions != null ? game.MothExplosions.CaptureSnapshot() : new MothExplosionPoolSnapshot();
            snapshot.pickups = game.Pickups != null ? game.Pickups.CaptureSnapshot() : new CombatPickupPoolSnapshot();

            CompanionSystem companions = station != null ? station.GetComponent<CompanionSystem>() : null;
            snapshot.companionField = companions != null ? companions.CaptureSnapshot() : new CompanionFieldSnapshot();
            snapshot.companionPowers = companions != null && companions.Abilities != null
                ? companions.Abilities.CaptureSnapshot(enemyLookup)
                : new CompanionPowerSnapshot();
            snapshot.upgradeRuntime = game.Upgrades != null
                ? game.Upgrades.CaptureSnapshot(enemyLookup)
                : new UpgradeRuntimeSnapshot();

            string json = JsonUtility.ToJson(snapshot);
            if (string.IsNullOrEmpty(json)) return false;
            return SaveSlotService.SetActiveExpeditionState(attemptId, json, revision, station != null ? station.Progress : null);
        }

        public static bool TryRestoreActiveExpedition(
            CombatGameBootstrap game,
            FusionStation station,
            WarehouseTravel travel,
            Func<int, CombatWaveSettings> waveResolver,
            out bool restoredInWarehouse,
            bool keepFrontEndPause = false)
        {
            restoredInWarehouse = false;
            if (game == null || !game.IsInitialized || game.Session == null || game.Player == null)
                return false;
            string json = SaveSlotService.PendingExpeditionJson;
            if (!TryParseValidSnapshot(json, out ExpeditionSnapshotData snapshot))
                return false;
            if (SaveSlotService.HasLoadedProfile && snapshot.profileId != SaveSlotService.ActiveProfileId)
                return false;

            if (station == null)
                station = UnityEngine.Object.FindAnyObjectByType<FusionStation>();
            if (travel == null)
                travel = UnityEngine.Object.FindAnyObjectByType<WarehouseTravel>();

            PlayerInputReader inputReader = game.Motor != null ? game.Motor.GetComponent<PlayerInputReader>() : null;
            Vector3 previousPlayerPosition = game.Player.transform.position;
            IsRestoring = true;
            try
            {
                // Suspend simulation and gameplay input while world and HUD are reconstructed.
                game.Session.Clock.SetPause(PauseReason.FrontEnd, true);
                if (inputReader != null)
                {
                    inputReader.SetInputEnabled(false);
                    inputReader.SuppressDashFromUiConfirmation();
                }

                CombatWaveSettings resolvedWaves = waveResolver != null ? waveResolver(snapshot.modeMinutes) : null;
                if (resolvedWaves != null)
                    game.SelectWaveSettings(resolvedWaves);

                game.Enemies?.DespawnAllActive();
                game.Hazards?.ClearAll();
                game.MothExplosions?.ClearAll();
                game.Session.GetComponent<EliteProjectilePool>()?.ClearAll();

                // Restore collection & equipment before PlayerRuntimeStats so RefreshEquipment does not overwrite saved card stats.
                station?.RestoreFromSnapshotJson(snapshot.fusionSnapshotJson);

                game.Session.RestoreFromSnapshot(snapshot.session);
                game.Session.Clock.SetPause(PauseReason.FrontEnd, true);
                for (int i = 0; i < TransientPauseReasonsToClear.Length; i++)
                    game.Session.Clock.SetPause(TransientPauseReasonsToClear[i], false);

                if (game.Motor != null)
                {
                    game.Motor.CancelDashAndRestoreState(true);
                    game.Motor.Stop();
                }
                game.Player.RestoreFromSnapshot(snapshot.player, notifyUi: true);
                game.Player.GetComponent<ActorVisualComposer>()?.ResetVisualState();

                game.Progression?.RestoreFromSnapshot(snapshot.progression, notifyUi: false);
                game.Draft?.RestoreFromSnapshot(snapshot.draft);
                game.Session.SetTacticalDraftPause(game.Draft != null && game.Draft.IsOpen);

                if (game.Weapon != null)
                {
                    game.Weapon.enabled = true;
                    game.Weapon.RestoreFromSnapshot(snapshot.weapon);
                    game.Player.GetComponent<FlashlightPresentation>()?.ResynchronizeStateWithoutRepeatingFlashes();
                }

                game.Director?.RestoreFromSnapshot(snapshot.waveDirector);
                game.Encounter?.RestoreFromSnapshot(snapshot.bossEncounter, snapshot.bossArena, snapshot.golgora, snapshot.magmaRings);

                int enemyCount = snapshot.enemies != null ? snapshot.enemies.Count : 0;
                var restoredNonBoss = new List<EnemyActor>(enemyCount);
                if (snapshot.enemies != null && game.Enemies != null)
                {
                    CombatWaveSettings settings = game.Director != null && game.Director.Settings != null
                        ? game.Director.Settings
                        : game.WaveSettings;
                    for (int i = 0; i < snapshot.enemies.Count; i++)
                    {
                        EnemySnapshot enemySnap = snapshot.enemies[i];
                        if (enemySnap == null)
                        {
                            restoredNonBoss.Add(null);
                            continue;
                        }
                        var archetypeId = (EnemyArchetypeId)enemySnap.archetypeId;
                        EnemyArchetypeData archetype = settings != null
                            ? settings.GetArchetype(archetypeId)
                            : EnemyArchetypeData.CreateDefault(archetypeId);
                        EnemyActor spawned = game.Enemies.SpawnForRestore(archetype, enemySnap.position, enemySnap.rotation);
                        if (spawned != null)
                            spawned.RestoreFromSnapshot(enemySnap);
                        restoredNonBoss.Add(spawned);
                    }
                }

                GolgoraActor restoredBoss = game.Encounter != null ? game.Encounter.Boss : null;
                var enemyLookup = new List<EnemyActor>(restoredNonBoss.Count + 1);
                enemyLookup.AddRange(restoredNonBoss);
                if (restoredBoss != null && restoredBoss.gameObject.activeInHierarchy)
                    enemyLookup.Add(restoredBoss);

                EliteProjectilePool projectilePool = game.Session.GetComponent<EliteProjectilePool>();
                if (projectilePool == null && snapshot.eliteProjectiles != null &&
                    snapshot.eliteProjectiles.bolts != null && snapshot.eliteProjectiles.bolts.Count > 0)
                {
                    projectilePool = EliteProjectilePool.GetOrCreate(game.Session, game.Player);
                }
                projectilePool?.RestoreFromSnapshot(snapshot.eliteProjectiles);

                game.Hazards?.RestoreFromSnapshot(snapshot.puddles);
                game.MothExplosions?.RestoreFromSnapshot(snapshot.mothExplosions);
                game.Pickups?.RestoreFromSnapshot(snapshot.pickups);

                CompanionSystem companions = station != null ? station.GetComponent<CompanionSystem>() : null;
                companions?.RestoreFromSnapshot(snapshot.companionField);
                if (companions != null && companions.Abilities != null)
                    companions.Abilities.RestoreFromSnapshot(snapshot.companionPowers, enemyLookup);

                game.Upgrades?.RestoreFromSnapshot(snapshot.upgradeRuntime, enemyLookup);

                travel?.RestoreFromSnapshot(snapshot.travel, snapshot.isInWarehouse, restoredNonBoss, snapshot.enemies);

                game.Hud?.RestoreFromSnapshot(snapshot.hud);
                EventLeaderboardSync.Instance?.RestoreFromSnapshot(snapshot.leaderboard);

                // Apply saved RNG streams last so no setup step shifts combat/presentation state.
                game.Session.Rng.RestoreState(snapshot.rng);

                if (game.CameraRig != null)
                {
                    game.CameraRig.OnTargetObjectWarped(game.Player.transform, game.Player.transform.position - previousPlayerPosition);
                    game.CameraRig.PreviousStateIsValid = false;
                }
                Physics.SyncTransforms();

                game.Hud?.RefreshNow();

                restoredInWarehouse = snapshot.isInWarehouse;
                if (!keepFrontEndPause)
                {
                    game.Session.Clock.SetPause(PauseReason.FrontEnd, false);
                    inputReader?.SuppressDashFromUiConfirmation();
                }
                return true;
            }
            finally
            {
                IsRestoring = false;
            }
        }

        private static int ResolveModeMinutes(CombatGameBootstrap game)
        {
            CombatWaveSettings settings = game.Director != null && game.Director.Settings != null
                ? game.Director.Settings
                : game.WaveSettings;
            return settings != null && settings.BossStartTime >= 300f ? 8 : 3;
        }
    }
}
