using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class CombatPickupsXpTests
    {
        private Scene testScene;
        private CombatSession session;
        private PlayerRuntimeStats stats;
        private PlayerHealth health;
        private CombatProgression progression;
        private CombatPickupPool pickups;
        private ExperiencePickupAudio pickupAudio;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            CombatTargetRegistry.Clear();
            testScene = SceneManager.CreateScene($"PickupsXP_{Random.Range(10000, 99999)}");
            SceneManager.SetActiveScene(testScene);
            session = new GameObject("CombatSession").AddComponent<CombatSession>();
            var player = new GameObject("Player");
            player.transform.position = Vector3.up;
            var collider = player.AddComponent<CapsuleCollider>();
            collider.radius = 0.45f;
            var body = player.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeAll;
            stats = player.AddComponent<PlayerRuntimeStats>();
            health = player.AddComponent<PlayerHealth>();
            health.BindSession(session);
            session.ResolvePlayerReferences();
            progression = new GameObject("Progression").AddComponent<CombatProgression>();
            progression.BindSession(session);
            pickups = new GameObject("Pickups").AddComponent<CombatPickupPool>();
            pickups.AutoTick = false;
            pickups.BindReferences(session, health, stats, progression);
            pickupAudio = pickups.gameObject.AddComponent<ExperiencePickupAudio>();
            pickupAudio.Bind(pickups, session);
            new GameObject("XP audio listener").AddComponent<AudioListener>();
            session.ResetSession(90101);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            CombatTargetRegistry.Clear();
            if (testScene.IsValid() && testScene.isLoaded)
            {
                if (SceneManager.sceneCount <= 1)
                    SceneManager.SetActiveScene(SceneManager.CreateScene("PickupsCleanup"));
                yield return SceneManager.UnloadSceneAsync(testScene);
            }
        }

        [Test]
        public void ExperienceUsesSuppliedClipAndIndependentPitchesWithoutChangingCombatRandomness()
        {
            var clip = Resources.Load<AudioClip>("Audio/XP_sound");
            Assert.That(clip, Is.Not.Null);Assert.That(clip.length, Is.GreaterThan(.1f));
            var samples = new float[clip.samples * clip.channels];
            Assert.That(clip.GetData(samples, 0));
            float peak = 0;foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
            Assert.That(peak, Is.GreaterThan(.05f), "The supplied MP3 must import as audible audio.");
            int combatRolls = session.Rng.Combat.SampleCount;
            var globalRandom = Random.state;
            float previousPitch = 0;
            for (int i = 0; i < 6; i++)
            {
                pickups.SpawnExperience(new Vector3(i * 3 + 20, 0, 20), 1);
                pickups.CollectAll();
                var voice = pickups.transform.Find("ExperiencePickupVoice" + i).GetComponent<AudioSource>();
                Assert.That(voice.clip, Is.SameAs(clip));Assert.That(voice.pitch, Is.InRange(.9f, 1.15f));
                Assert.That(Mathf.Abs(voice.pitch - previousPitch), Is.GreaterThanOrEqualTo(.037f));
                previousPitch = voice.pitch;
            }
            Assert.That(pickupAudio.SoundsPlayed, Is.EqualTo(6));
            Assert.That(session.Rng.Combat.SampleCount, Is.EqualTo(combatRolls));Assert.That(Random.state, Is.EqualTo(globalRandom));
            for (int i = 0; i < 30; i++) { pickups.SpawnExperience(new Vector3(20, 0, 20), 1);pickups.CollectAll(); }
            Assert.That(pickups.GetComponentsInChildren<AudioSource>().Length, Is.EqualTo(8), "Repeated collection reuses a bounded set of voices.");
        }

        [Test]
        public void ExperienceAudioIgnoresMedkitsAndStopsOnPauseResetAndDisable()
        {
            pickups.SpawnMedkit(new Vector3(20, 0, 20));pickups.CollectAll();
            progression.AddExperience(1);Assert.That(pickupAudio.SoundsPlayed, Is.Zero);
            pickups.SpawnExperience(new Vector3(20, 0, 20), 1);pickups.CollectAll();
            Assert.That(pickupAudio.SoundsPlayed, Is.EqualTo(1));
            session.SetManualPause(true);
            foreach (var voice in pickups.GetComponentsInChildren<AudioSource>()) Assert.That(voice.isPlaying, Is.False);
            session.SetManualPause(false);
            session.ResetSession(90101);
            foreach (var voice in pickups.GetComponentsInChildren<AudioSource>()) Assert.That(voice.isPlaying, Is.False);
            pickupAudio.enabled = false;
            pickups.SpawnExperience(new Vector3(20, 0, 20), 1);pickups.CollectAll();
            Assert.That(pickupAudio.SoundsPlayed, Is.EqualTo(1));
            pickupAudio.enabled = true;pickupAudio.Bind(pickups, session);
            pickups.SpawnExperience(new Vector3(20, 0, 20), 1);pickups.CollectAll();
            Assert.That(pickupAudio.SoundsPlayed, Is.EqualTo(2), "Rebinding must not duplicate the pickup subscription.");
        }

        [Test]
        public void OrbThatOpensTheLevelUpDraftStillPlaysItsSound()
        {
            progression.OnLevelGained += _ => session.SetTacticalDraftPause(true);
            pickups.SpawnExperience(new Vector3(20, 0, 20), progression.RequiredXp);pickups.CollectAll();
            Assert.That(session.State, Is.EqualTo(SessionState.TacticalDraft));
            Assert.That(pickupAudio.SoundsPlayed, Is.EqualTo(1));
        }

        [Test]
        public void MedkitSupplyIsBoundedAndExpiresWithoutDiscardingExperience()
        {
            int first = 0;
            for (int i = 0; i < 100; i++)
            {
                int id = pickups.SpawnMedkit(new Vector3(30 + i, 0, 30));
                if (i == 0) first = id;
            }
            Assert.That(pickups.ActiveMedkitCount, Is.EqualTo(stats.Settings.MaximumGroundMedkits));
            int xp = pickups.SpawnExperience(new Vector3(30, 0, 30), 10);
            pickups.Step(stats.Settings.MedkitLifetime + 0.01f);
            Assert.That(pickups.ActiveMedkitCount, Is.Zero);
            Assert.That(pickups.TryGetPickupInfo(first, out _), Is.False);
            Assert.That(pickups.TryGetPickupInfo(xp, out _), Is.True);
            Assert.That(pickups.TotalCollectedMedkits, Is.Zero);
        }

        [Test]
        public void RewardMedkitsHaveASessionClockCooldownAndResetWithTheRun()
        {
            var death = new ActorDeathEvent(800, 3, false, false, new Vector3(20, 1, 20), Vector3.forward,
                DamageSourceType.FlashlightPulse, 0, true, true);
            for (int i = 0; i < 100; i++) session.Events.PublishDeath(in death);
            Assert.That(pickups.ActiveMedkitCount, Is.EqualTo(1));
            session.Clock.AdvanceGameplayTime(stats.Settings.MedkitDropInterval);
            session.Events.PublishDeath(in death);
            Assert.That(pickups.ActiveMedkitCount, Is.EqualTo(2));
            pickups.ResetForSession(); session.Events.PublishDeath(in death);
            Assert.That(pickups.ActiveMedkitCount, Is.EqualTo(1));
        }

        [Test]
        public void ExperienceThresholdsFollowGddRecurrenceAndQueueEveryChoice()
        {
            int[] thresholds = { 100, 150, 225, 338, 507, 760 };
            int total = 0;
            for (int i = 0; i < thresholds.Length; i++)
            {
                Assert.That(progression.RequiredXp, Is.EqualTo(thresholds[i]));
                Assert.That(progression.AddExperience(thresholds[i]), Is.EqualTo(1));
                total += thresholds[i];
                Assert.That(progression.Level, Is.EqualTo(i + 2));
                Assert.That(progression.PendingChoices, Is.EqualTo(i + 1));
                Assert.That(progression.CurrentXp, Is.Zero);
            }
            Assert.That(progression.TotalXp, Is.EqualTo(total));
        }

        [Test]
        public void LargeExperienceGrantResolvesRemainderBeforeListenersOpenDraft()
        {
            int levelEvents = 0;
            progression.OnLevelGained += _ =>
            {
                levelEvents++;
                session.SetTacticalDraftPause(true);
            };
            Assert.That(progression.AddExperience(500), Is.EqualTo(3));
            Assert.That(progression.Level, Is.EqualTo(4));
            Assert.That(progression.PendingChoices, Is.EqualTo(3));
            Assert.That(progression.CurrentXp, Is.EqualTo(25));
            Assert.That(levelEvents, Is.EqualTo(3));
            Assert.That(session.State, Is.EqualTo(SessionState.TacticalDraft));
            Assert.That(progression.ConsumePendingChoice(), Is.True);
            Assert.That(progression.ConsumePendingChoice(), Is.True);
            Assert.That(progression.PendingChoices, Is.EqualTo(1));
            Assert.That(session.IsPaused, Is.True, "Consumir una carta no debe quitar la pausa antes del último draft.");
            Assert.That(progression.ConsumePendingChoice(), Is.True);
            Assert.That(progression.ConsumePendingChoice(), Is.False);
        }

        [Test]
        public void NearbyExperienceMergesWithoutLosingValueAndNeverMergesMedkits()
        {
            int first = pickups.SpawnExperience(new Vector3(3f, 9f, 0f), 10);
            int second = pickups.SpawnExperience(new Vector3(3.6f, -1f, 0f), 25);
            int separate = pickups.SpawnExperience(new Vector3(4.4f, 1f, 0f), 50);
            pickups.SpawnMedkit(new Vector3(3f, 1f, 0f));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(separate, Is.Not.EqualTo(first));
            Assert.That(pickups.ActiveExperienceCount, Is.EqualTo(2));
            Assert.That(pickups.ActiveMedkitCount, Is.EqualTo(1));
            Assert.That(pickups.TotalSpawnedExperience, Is.EqualTo(85));
            Assert.That(pickups.TryGetPickupInfo(first, out CombatPickupInfo info), Is.True);
            Assert.That(info.Experience, Is.EqualTo(35));
            Assert.That(info.Position.y, Is.EqualTo(CombatPickupPool.PickupHeight));
        }

        [Test]
        public void MagnetUsesBaseRadiusAndTwelveMetresPerSecondAndKeepsAttractedPickup()
        {
            int inside = pickups.SpawnExperience(new Vector3(2.4f, 0f, 0f), 10);
            int outside = pickups.SpawnExperience(new Vector3(-2.6f, 0f, 0f), 10);
            pickups.Step(0.1f);
            Assert.That(pickups.TryGetPickupInfo(inside, out CombatPickupInfo attracted), Is.True);
            Assert.That(attracted.IsAttracted, Is.True);
            Assert.That(attracted.Position.x, Is.EqualTo(1.2f).Within(0.001f));
            Assert.That(pickups.TryGetPickupInfo(outside, out CombatPickupInfo untouched), Is.True);
            Assert.That(untouched.IsAttracted, Is.False);
            Assert.That(untouched.Position.x, Is.EqualTo(-2.6f).Within(0.001f));
            health.transform.position = new Vector3(6f, 1f, 0f);
            pickups.Step(0.1f);
            Assert.That(pickups.TryGetPickupInfo(inside, out CombatPickupInfo continuing), Is.True);
            Assert.That(continuing.Position.x, Is.EqualTo(2.4f).Within(0.001f));
        }

        [Test]
        public void MagnetUpgradeAttractsFurtherExperienceAndCollectsExactMergedAmount()
        {
            stats.SetUpgradeTier("GREED_01", 2);
            pickups.SpawnExperience(new Vector3(6f, 1f, 0f), 25);
            pickups.SpawnExperience(new Vector3(6.2f, 1f, 0f), 50);
            pickups.Step(0.5f);
            Assert.That(pickups.ActiveExperienceCount, Is.Zero);
            Assert.That(progression.TotalXp, Is.EqualTo(75));
            Assert.That(progression.CurrentXp, Is.EqualTo(75));
            Assert.That(pickups.TotalCollectedExperience, Is.EqualTo(75));
        }

        [Test]
        public void MedkitHealsRuntimeAmountAndFullHealthMedkitIsStillConsumed()
        {
            health.ForceSetHealthForTesting(40f);
            stats.SetUpgradeTier("GREED_03", 3);
            pickups.SpawnMedkit(Vector3.zero);
            pickups.Step(0.01f);
            Assert.That(health.CurrentHealth, Is.EqualTo(61f));
            health.Heal(1000f);
            int healingConfirmations = 0;
            health.OnHealed += result =>
            {
                if (result.ActualHealed > 0f)
                    healingConfirmations++;
            };
            pickups.SpawnMedkit(Vector3.zero);
            pickups.Step(0.01f);
            Assert.That(pickups.ActiveMedkitCount, Is.Zero);
            Assert.That(pickups.TotalCollectedMedkits, Is.EqualTo(2));
            Assert.That(health.CurrentHealth, Is.EqualTo(100f));
            Assert.That(healingConfirmations, Is.Zero);
        }

        [UnityTest]
        public IEnumerator MedkitUsesApolloColorAndNormalsAndKeepsItsShadowOnTheFloor()
        {
            pickups.SpawnMedkit(new Vector3(4f, 0f, 0f));
            Transform medkit = null;
            foreach (Transform child in pickups.transform)
                if (child.name == "PooledMedkit" && child.gameObject.activeSelf) { medkit = child; break; }
            Assert.That(medkit, Is.Not.Null);
            Transform visual = medkit.Find("Visual");
            Transform sprite = visual.Find("Botiquin Apollo Sprite");
            Transform shadow = medkit.Find("Botiquin Contact Shadow");
            Assert.That(sprite, Is.Not.Null);
            Assert.That(shadow, Is.Not.Null);
            Assert.That(visual.Find("Case"), Is.Null, "The old procedural medkit box must be replaced.");
            Material material = sprite.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(material.shader.isSupported, Is.True);
            Assert.That(material.shader.name, Is.EqualTo("EchoScrap/Character Pixel Lit"));
            Assert.That(material.GetTexture("_BaseMap").name, Is.EqualTo("Botiquin_Apollo"));
            Assert.That(material.GetTexture("_BumpMap").name, Is.EqualTo("Botiquin_Apollo_Normal"));
            Assert.That(material.GetVector("_FrameRect"), Is.EqualTo(new Vector4(0, 0, 1, 1)));
            pickups.Step(0.2f);
            Assert.That(Quaternion.Angle(visual.rotation, Quaternion.identity), Is.LessThan(0.01f),
                "A flat pickup must not rotate edge-on while waiting to be collected.");
            Assert.That(shadow.position.y, Is.EqualTo(0.035f).Within(0.001f));
            pickups.SpawnMedkit(new Vector3(-4f, 0f, 0f));
            foreach (Transform child in pickups.transform)
                if (child.name == "PooledMedkit" && child.gameObject.activeSelf)
                    Assert.That(child.Find("Visual/Botiquin Apollo Sprite").GetComponent<MeshRenderer>().sharedMaterial,
                        Is.SameAs(material), "Medkits share their imported material across the pool.");
            yield return null;
        }

        [Test]
        public void DeathRewardUsesExactRecordedExperienceAndMedkitWithoutRerollingCombatRng()
        {
            var expected = new DeterministicRandom(90101);
            var death = new ActorDeathEvent(800, (int)EnemyArchetypeId.Colossus, false, false,
                new Vector3(8f, 1f, 0f), Vector3.forward, DamageSourceType.FlashlightPulse, 50, true, true);
            session.Events.PublishDeath(in death);
            Assert.That(pickups.ActiveExperienceCount, Is.EqualTo(1));
            Assert.That(pickups.ActiveMedkitCount, Is.EqualTo(1));
            Assert.That(pickups.TotalSpawnedExperience, Is.EqualTo(50));
            Assert.That(session.Rng.Combat.NextFloat(), Is.EqualTo(expected.NextFloat()));
            var unRewarded = new ActorDeathEvent(801, 1, false, false, Vector3.one, Vector3.forward,
                DamageSourceType.BossStomp, 999, true, false);
            session.Events.PublishDeath(in unRewarded);
            Assert.That(pickups.TotalSpawnedExperience, Is.EqualTo(50));
            Assert.That(pickups.ActiveMedkitCount, Is.EqualTo(1));
        }

        [Test]
        public void PausedOrEndedSessionDoesNotMoveCollectOrGrantExperience()
        {
            int id = pickups.SpawnExperience(new Vector3(2f, 1f, 0f), 10);
            session.SetManualPause(true);
            pickups.Step(1f);
            Assert.That(progression.AddExperience(100), Is.Zero);
            Assert.That(pickups.TryGetPickupInfo(id, out CombatPickupInfo paused), Is.True);
            Assert.That(paused.Position.x, Is.EqualTo(2f));
            session.SetManualPause(false);
            var bossDeath = new ActorDeathEvent(999, 99, false, true, Vector3.zero, Vector3.forward,
                DamageSourceType.FlashlightPulse, 500, false, true);
            session.QueueBossDeath(in bossDeath);
            session.ResolveSimulationStepOutcome();
            pickups.Step(1f);
            Assert.That(progression.AddExperience(100), Is.Zero);
            Assert.That(pickups.ActiveExperienceCount, Is.EqualTo(1));
            Assert.That(pickups.TryGetPickupInfo(id, out CombatPickupInfo ended), Is.True);
            Assert.That(ended.Position.x, Is.EqualTo(2f));
            Assert.That(progression.TotalXp, Is.Zero);
        }

        [Test]
        public void DraftTriggeredByOnePickupLeavesOtherPickupsUnconsumed()
        {
            progression.AddExperience(90);
            progression.OnLevelGained += _ => session.SetTacticalDraftPause(true);
            pickups.SpawnExperience(new Vector3(-0.45f, 0f, 0f), 10);
            pickups.SpawnExperience(new Vector3(0.45f, 0f, 0f), 25);
            pickups.Step(0.01f);
            Assert.That(progression.TotalXp, Is.EqualTo(100));
            Assert.That(progression.PendingChoices, Is.EqualTo(1));
            Assert.That(pickups.ActiveExperienceCount, Is.EqualTo(1));
            progression.ConsumePendingChoice();
            session.ConfirmDraftSelectionAndResume();
            pickups.Step(0.01f);
            Assert.That(progression.TotalXp, Is.EqualTo(125));
            Assert.That(pickups.ActiveExperienceCount, Is.Zero);
        }

        [Test]
        public void GameplayPoolCanGrowBeyondPrewarmWithoutDiscardingExperienceAndThenReusesSlots()
        {
            const int count = 140;
            int prewarmed = pickups.TotalCreatedCount;
            for (int i = 0; i < count; i++)
                pickups.SpawnExperience(new Vector3(i * 1.1f, 0f, 20f), 10);
            Assert.That(pickups.ActiveExperienceCount, Is.EqualTo(count));
            Assert.That(pickups.TotalSpawnedExperience, Is.EqualTo(count * 10));
            Assert.That(pickups.TotalCreatedCount, Is.GreaterThan(prewarmed));
            int expanded = pickups.TotalCreatedCount;
            pickups.ResetForSession();
            for (int i = 0; i < count; i++)
                pickups.SpawnExperience(new Vector3(i * 1.1f, 0f, 20f), 10);
            Assert.That(pickups.TotalCreatedCount, Is.EqualTo(expanded));
            Assert.That(pickups.TotalSpawnedExperience, Is.EqualTo(count * 10));
        }

        [Test]
        public void SessionResetClearsPickupsProgressionAndPendingChoicesAcrossRepeatedRuns()
        {
            for (int run = 0; run < 10; run++)
            {
                progression.AddExperience(500);
                int id = pickups.SpawnExperience(new Vector3(10f, 0f, 0f), 25);
                pickups.SpawnMedkit(new Vector3(10f, 0f, 0f));
                session.ResetSession(90101);
                Assert.That(progression.Level, Is.EqualTo(1));
                Assert.That(progression.CurrentXp, Is.Zero);
                Assert.That(progression.RequiredXp, Is.EqualTo(100));
                Assert.That(progression.PendingChoices, Is.Zero);
                Assert.That(progression.TotalXp, Is.Zero);
                Assert.That(pickups.ActivePickupCount, Is.Zero);
                Assert.That(pickups.TotalCollectedMedkits, Is.Zero);
                Assert.That(pickups.TotalSpawnedExperience, Is.Zero);
                Assert.That(pickups.TryGetPickupInfo(id, out _), Is.False);
            }
        }
    }
}
