using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class CombatMusicTests
    {
        private CombatGameBootstrap game;
        private CombatMusicPlayer music;
        private AudioSource Arena=>music.transform.Find("ArenaMusic").GetComponent<AudioSource>();
        private AudioSource Vault=>music.transform.Find("VaultMusic").GetComponent<AudioSource>();
        private AudioSource Boss=>music.transform.Find("BossMusic").GetComponent<AudioSource>();
        private void Loaded(Scene scene,LoadSceneMode mode)
        {var station=Object.FindAnyObjectByType<FusionStation>();if(station!=null)station.PersistProgress=false;}
        [UnitySetUp] public IEnumerator Setup()
        {
            SceneTransitionState.Reset();SceneManager.sceneLoaded+=Loaded;
            yield return SceneManager.LoadSceneAsync("CombatArena");yield return null;
            game=Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick=game.Encounter.AutoTick=false;game.Weapon.enabled=false;
            game.Session.ResumeFromFocusLoss();game.Session.ResumeAllUserPauses();
            music=game.GetComponent<CombatMusicPlayer>();Assert.That(music,Is.Not.Null);
            music.AutoTick=false;music.Bind(game);
            Assert.That(game.IsSceneEntryReady);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            SceneManager.sceneLoaded-=Loaded;Time.timeScale=1;
            SceneManager.SetActiveScene(SceneManager.CreateScene("Music cleanup"));
            if(game!=null)yield return SceneManager.UnloadSceneAsync(game.gameObject.scene);
            SceneTransitionState.Reset();CombatTargetRegistry.Clear();
        }
        [Test] public void ArenaAndBossUseOfficialStreamedLoops()
        {
            Assert.That(Arena.clip,Is.Not.Null);Assert.That(Boss.clip,Is.Not.Null);
            Assert.That(Arena.clip.loadType,Is.EqualTo(AudioClipLoadType.Streaming));
            Assert.That(Boss.clip.loadType,Is.EqualTo(AudioClipLoadType.Streaming));
            Assert.That(Arena.clip.length,Is.EqualTo(76.402f).Within(.05f));
            Assert.That(Boss.clip.length,Is.EqualTo(52.255f).Within(.05f));
            Assert.That(Arena.loop && Boss.loop);Assert.That(Arena.spatialBlend,Is.Zero);
            music.Step(1);Assert.That(music.CurrentTrack,Is.EqualTo(CombatMusicTrack.Arena));
            Assert.That(Arena.isPlaying);Assert.That(Arena.volume,Is.GreaterThan(0));Assert.That(Boss.volume,Is.Zero);
        }
        [Test] public void BossEncounterCrossfadesTracksAndReplacesOnlyTheOldMusicalBeds()
        {
            music.Step(1);float previous=Arena.volume;
            game.Session.Clock.SetGameplayTimeForTesting(game.WaveSettings.BossStartTime);game.Director.Step();
            game.Encounter.Boss.AutoTick=false;music.Step(.2f);
            Assert.That(music.CurrentTrack,Is.EqualTo(CombatMusicTrack.Boss));
            Assert.That(Arena.volume,Is.InRange(.001f,previous-.001f));Assert.That(Boss.volume,Is.GreaterThan(0));
            var audio=game.Encounter.Boss.GetComponent<GolgoraAudio>();Assert.That(audio.MusicEnabled,Is.False);
            int cues=audio.CuesPlayed;audio.StepSound();Assert.That(audio.CuesPlayed,Is.EqualTo(cues+1));
            music.Step(1);Assert.That(Arena.isPlaying,Is.False);Assert.That(Boss.isPlaying);
            music.enabled=false;Assert.That(audio.MusicEnabled);Assert.That(Boss.isPlaying,Is.False);
        }
        [UnityTest] public IEnumerator ArenaAndBossMusicContinueThroughPauseAndUpgradeMenus()
        {
            foreach(bool bossTrack in new[]{false,true})
            {
                if(bossTrack)
                {
                    game.Session.Clock.SetGameplayTimeForTesting(game.WaveSettings.BossStartTime);game.Director.Step();
                    game.Encounter.Boss.AutoTick=false;
                }
                music.Step(1);var source=bossTrack?Boss:Arena;var clip=source.clip;
                foreach(var reason in new[]{PauseReason.Manual,PauseReason.TacticalDraft})
                {
                    game.Session.Clock.SetPause(reason,true);music.Step(.2f);
                    Assert.That(Time.timeScale,Is.Zero);Assert.That(music.IsPaused,Is.False);Assert.That(source.isPlaying);
                    float position=source.time;
                    yield return new WaitForSecondsRealtime(.25f);
                    Assert.That(source.time,Is.GreaterThan(position+.08f),"The actual streamed audio must advance while combat is frozen.");
                    game.Session.Clock.SetPause(reason,false);music.Step(.2f);
                    Assert.That(source.clip,Is.SameAs(clip));Assert.That(source.time,Is.GreaterThan(position+.08f),"Closing the menu must not restart the song.");
                }
            }
        }

        [UnityTest] public IEnumerator VaultMusicLoopsThroughVaultMenusAndStopsOutsideOrAtTitle()
        {
            Assert.IsNotNull(Vault.clip);Assert.AreEqual(AudioClipLoadType.Streaming,Vault.clip.loadType);Assert.IsTrue(Vault.loop);
            game.Session.SetWarehouseVisit(true);music.Step(1);Assert.IsTrue(Vault.isPlaying);Assert.IsFalse(Arena.isPlaying);Assert.IsFalse(Boss.isPlaying);
            foreach(var reason in new[]{PauseReason.FusionMenu,PauseReason.CompanionMenu,PauseReason.Manual}){
                game.Session.Clock.SetPause(reason,true);music.Step(1);float position=Vault.time;yield return new WaitForSecondsRealtime(.2f);Assert.Greater(Vault.time,position+.05f);game.Session.Clock.SetPause(reason,false);
            }
            game.Session.SimulateFocusChange(false);music.Step(0);Assert.IsFalse(Vault.isPlaying);game.Session.ResumeFromFocusLoss();music.Step(0);Assert.IsTrue(Vault.isPlaying);
            game.Session.Clock.SetPause(PauseReason.FrontEnd,true);music.Step(0);Assert.IsFalse(Vault.isPlaying);Assert.AreEqual(CombatMusicTrack.None,music.CurrentTrack);
        }

        [UnityTest] public IEnumerator FocusLossPreservesMusicPositionAndWarehouseAndResetStopPlayback()
        {
            music.Step(1);yield return new WaitForSecondsRealtime(.15f);
            game.Session.SimulateFocusChange(false);float position=Arena.time;music.Step(1);
            Assert.That(music.IsPaused);Assert.That(Arena.isPlaying,Is.False);
            yield return new WaitForSecondsRealtime(.15f);Assert.That(Arena.time,Is.EqualTo(position).Within(.05f));
            game.Session.ResumeFromFocusLoss();Assert.That(music.IsPaused,Is.False);Assert.That(Arena.isPlaying);
            game.Session.SetWarehouseVisit(true);music.Step(1);
            Assert.That(music.CurrentTrack,Is.EqualTo(CombatMusicTrack.Vault));Assert.That(Arena.isPlaying,Is.False);Assert.That(Vault.isPlaying);
            game.Session.SetWarehouseVisit(false);music.Step(0);Assert.IsFalse(Vault.isPlaying);music.Step(1);Assert.That(Arena.isPlaying);
            game.ResetRun(false);Assert.That(Arena.isPlaying,Is.False);Assert.That(Boss.isPlaying,Is.False);
            Assert.That(Arena.volume,Is.Zero);Assert.That(Boss.volume,Is.Zero);
        }
    }
}
