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
        [Test] public void ArenaAndTwoMinuteBossExcerptAreStreamedAndLooped()
        {
            Assert.That(Arena.clip,Is.Not.Null);Assert.That(Boss.clip,Is.Not.Null);
            Assert.That(Arena.clip.loadType,Is.EqualTo(AudioClipLoadType.Streaming));
            Assert.That(Boss.clip.loadType,Is.EqualTo(AudioClipLoadType.Streaming));
            Assert.That(Arena.clip.length,Is.InRange(302f,303f));
            Assert.That(Boss.clip.length,Is.InRange(170.8f,171.2f),"The 4:51 source is trimmed at 2:00; its intro must not return when looping.");
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
        [UnityTest] public IEnumerator PausePreservesMusicPositionAndWarehouseAndResetStopPlayback()
        {
            music.Step(1);yield return new WaitForSecondsRealtime(.15f);
            game.Session.SetManualPause(true);float position=Arena.time;music.Step(1);
            Assert.That(music.IsPaused);Assert.That(Arena.isPlaying,Is.False);
            yield return new WaitForSecondsRealtime(.15f);Assert.That(Arena.time,Is.EqualTo(position).Within(.05f));
            game.Session.SetManualPause(false);Assert.That(music.IsPaused,Is.False);Assert.That(Arena.isPlaying);
            game.Session.SetWarehouseVisit(true);music.Step(1);
            Assert.That(music.CurrentTrack,Is.EqualTo(CombatMusicTrack.None));Assert.That(Arena.isPlaying,Is.False);
            game.Session.SetWarehouseVisit(false);music.Step(1);Assert.That(Arena.isPlaying);
            game.ResetRun(false);Assert.That(Arena.isPlaying,Is.False);Assert.That(Boss.isPlaying,Is.False);
            Assert.That(Arena.volume,Is.Zero);Assert.That(Boss.volume,Is.Zero);
        }
    }
}
