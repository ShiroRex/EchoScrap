using System.Collections;
using System.Linq;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class FlashlightAudioTests
    {
        private Scene scene;
        private CombatSession session;
        private PlayerHealth health;
        private PlayerRuntimeStats stats;
        private FlashlightWeapon weapon;
        private FlashlightAudio audio;

        [UnitySetUp] public IEnumerator SetUp()
        {
            scene = SceneManager.CreateScene("FlashlightAudioTest");
            SceneManager.SetActiveScene(scene);
            for (int i = SceneManager.sceneCount - 2; i >= 0; i--)
            {
                var previous = SceneManager.GetSceneAt(i);
                // Manually unloading the runner's initial scene cancels this setup coroutine.
                if (!previous.name.StartsWith("InitTestScene")) yield return SceneManager.UnloadSceneAsync(previous);
            }
            CombatTargetRegistry.Clear(); Time.timeScale = 1f;
            session = new GameObject("Session").AddComponent<CombatSession>();
            var player = new GameObject("Player", typeof(CapsuleCollider));
            player.layer = PhysicsLayers.PlayerLayer;
            player.transform.position = Vector3.up;
            stats = player.AddComponent<PlayerRuntimeStats>();
            health = player.AddComponent<PlayerHealth>(); health.BindSession(session);
            weapon = player.AddComponent<FlashlightWeapon>(); weapon.BindSession(session);
            audio = player.AddComponent<FlashlightAudio>(); audio.Bind(weapon);
            new GameObject("Listener").AddComponent<AudioListener>();
            yield return null;
            session.ResetSession(1337);
            weapon.ResetWeaponCycle();
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            SceneManager.SetActiveScene(SceneManager.CreateScene("FlashlightAudioCleanup"));
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            CombatTargetRegistry.Clear();
        }

        private AudioSource Voice(string name) => weapon.transform.Find(name).GetComponent<AudioSource>();
        private void Silent() => Assert.IsFalse(weapon.GetComponentsInChildren<AudioSource>().Any(v => v.isPlaying));

        [Test] public void AudioBankContainsTenNonSilentMonoClipsWithHeadroomAndLoopContinuity()
        {
            var clips = Resources.LoadAll<AudioClip>("Audio/Flashlight");
            Assert.AreEqual(10, clips.Length);
            foreach (var clip in clips)
            {
                Assert.AreEqual(1, clip.channels, clip.name);
                Assert.AreEqual(44100, clip.frequency, clip.name);
                var data = new float[clip.samples]; Assert.IsTrue(clip.GetData(data, 0));
                Assert.Greater(data.Max(v => Mathf.Abs(v)), .1f, clip.name);
                Assert.LessOrEqual(data.Max(v => Mathf.Abs(v)), .73f, clip.name);
                if (clip.name == "SolarLoop") Assert.Less(Mathf.Abs(data[0] - data[data.Length - 1]), .1f);
                else Assert.Less(Mathf.Abs(data[0]) + Mathf.Abs(data[data.Length - 1]), .001f);
            }
        }

        [Test] public void FourRealPulsesUseStrongerFourthSoundAndSeparateRestTail()
        {
            int before = audio.PulseSoundsPlayed;
            weapon.StepWeapon(.805f);
            Assert.AreEqual(before + 4, audio.PulseSoundsPlayed);
            Assert.AreEqual(1, audio.FourthPulseSoundsPlayed);
            Assert.AreEqual("FourthPulse", Voice("FlashlightPulse").clip.name);
            Assert.AreEqual("Rest", Voice("FlashlightMechanism").clip.name);
            Assert.AreEqual(4, weapon.TotalPulsesEmitted);
            Assert.IsTrue(weapon.IsInRestPhase);
        }

        [UnityTest] public IEnumerator CrowdHitsAreAggregatedAndCriticalUsesItsOwnCue()
        {
            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject("Dummy" + i, typeof(CapsuleCollider));
                go.layer = PhysicsLayers.EnemyLayer;
                go.transform.position = new Vector3((i - 4) * .1f, 1f, 3f);
                go.AddComponent<CombatDummy>().Configure(session, health, hp: 5000f, enableContact: false);
            }
            Physics.SyncTransforms();
            int before = audio.ImpactSoundsPlayed;
            weapon.StepWeapon(.201f);
            Assert.AreEqual(before + 1, audio.ImpactSoundsPlayed);
            Assert.AreEqual("Hit", Voice("FlashlightImpact").clip.name);
            weapon.QueueGuaranteedCriticalPulses(4);
            weapon.StepWeapon(1.01f);
            // Keep real Update from advancing the schedule while waiting for the impact limiter.
            session.SetManualPause(true);
            yield return new WaitForSecondsRealtime(.08f);
            session.SetManualPause(false);
            weapon.StepWeapon(.201f);
            Assert.AreEqual("CriticalHit", Voice("FlashlightImpact").clip.name);
            Assert.AreEqual(4, weapon.GetComponentsInChildren<AudioSource>().Length);
        }

        [Test] public void PauseDraftAndVaultSilenceAudioAndSuppressCatchUpSounds()
        {
            weapon.StepWeapon(.201f);
            session.SetManualPause(true); Silent();
            int before = audio.PulseSoundsPlayed;
            weapon.StepWeapon(.61f);
            Assert.AreEqual(before, audio.PulseSoundsPlayed);
            session.SetManualPause(false);
            session.SetTacticalDraftPause(true); Silent();
            session.SetTacticalDraftPause(false);
            session.SetWarehouseVisit(true); Silent();
            weapon.StepWeapon(.5f);
            Assert.AreEqual(before, audio.PulseSoundsPlayed);
        }

        [UnityTest] public IEnumerator SolarLoopPersistsAcrossBurstsAndStopsAtPauseAndWeaponDisable()
        {
            stats.SetUpgradeTier("FLASH_05", 1);
            weapon.ResetWeaponCycle();
            int starts = audio.SolarStarts;
            Assert.IsTrue(audio.IsSolarPlaying);
            weapon.StepWeapon(2f);
            Assert.AreEqual(starts, audio.SolarStarts, "Solar sound must not restart at each four-pulse boundary.");
            session.Clock.SetPause(PauseReason.FusionMenu, true); Silent();
            session.Clock.SetPause(PauseReason.FusionMenu, false);
            Assert.IsTrue(audio.IsSolarPlaying);
            weapon.enabled = false; Silent();
            yield return null; Silent();
        }

        [Test] public void RebindAndEnableCyclesDoNotDuplicateEventsOrAllocateMoreVoices()
        {
            audio.Bind(weapon); audio.Bind(weapon);
            audio.enabled = false; audio.enabled = true;
            int before = audio.PulseSoundsPlayed;
            weapon.StepWeapon(.201f);
            Assert.AreEqual(before + 1, audio.PulseSoundsPlayed);
            Assert.AreEqual(4, weapon.GetComponentsInChildren<AudioSource>().Length);
            weapon.StopWeapon(); Silent();
        }

        [UnityTest] public IEnumerator RealArenaAutomaticallyInstallsAudioAndUnloadsItWithWorld()
        {
            SceneTransitionState.Reset(); SceneTransitionState.SaveProgress = false;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            scene = SceneManager.GetActiveScene();
            yield return null; yield return null;
            var game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            Assert.IsNotNull(game);
            Assert.IsNotNull(game.Weapon.GetComponent<FlashlightAudio>());
            yield return SceneManager.LoadSceneAsync("MainMenu");
            scene = SceneManager.GetActiveScene();
            Assert.IsNull(Object.FindAnyObjectByType<FlashlightAudio>());
            SceneTransitionState.Reset();
        }
    }
}
