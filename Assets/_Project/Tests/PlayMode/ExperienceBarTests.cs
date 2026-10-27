using System.Collections;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace EchoScrap.Tests
{
    public sealed class ExperienceBarTests
    {
        private CombatGameBootstrap game;
        [UnitySetUp] public IEnumerator Setup()
        {
            Time.timeScale=1;
            yield return SceneManager.LoadSceneAsync("CombatArena");yield return null;
            game=Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick=false;game.Encounter.AutoTick=false;game.ResetRun(false);game.Weapon.enabled=false;
            game.Session.ResumeFromFocusLoss();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.timeScale=1;var scene=game.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("ExperienceCleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);CombatTargetRegistry.Clear();
        }
        [UnityTest] public IEnumerator SuppliedArtAnimatesActualXpAndResetsAfterLevelAndRestart()
        {
            var bar=game.Hud.GetComponentInChildren<ExperienceBarArtwork>();
            // Canvas is owned by HUD but may be a separate scene root.
            if(bar==null)bar=Object.FindAnyObjectByType<ExperienceBarArtwork>();
            Assert.IsNotNull(bar);
            var tex=Resources.Load<Texture2D>("Hud/Experience/000");
            Assert.AreEqual(256,tex.width);Assert.AreEqual(65,tex.height);Assert.AreEqual(FilterMode.Point,tex.filterMode);
            Assert.AreEqual(0,bar.DisplayedFraction);
            game.Progression.AddExperience(50);Assert.AreEqual(.5f,bar.TargetFraction);
            yield return null;Assert.Greater(bar.DisplayedFraction,0);Assert.LessOrEqual(bar.DisplayedFraction,.5f);
            yield return new WaitForSecondsRealtime(.4f);Assert.AreEqual(.5f,bar.DisplayedFraction,.001f);
            yield return Capture("Logs/ExperienceBar.png");
            game.Progression.AddExperience(70);Assert.AreEqual(2,game.Progression.Level);
            Assert.AreEqual(20f/150,bar.TargetFraction,.001f);Assert.Greater(bar.LevelPulse,0);
            yield return new WaitForSecondsRealtime(.4f);Assert.AreEqual(20f/150,bar.DisplayedFraction,.001f);
            game.ResetRun(false);yield return null;Assert.AreEqual(0,bar.DisplayedFraction);Assert.AreEqual(0,bar.LevelPulse);
        }
        private IEnumerator Capture(string path)
        {
            var camera=Camera.main;var canvas=game.Hud.HudCanvas;
            var rt=new RenderTexture(1920,1080,24);camera.targetTexture=rt;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            yield return null;camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            RenderTexture.active=previous;camera.targetTexture=null;canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;
            Object.Destroy(image);Object.Destroy(rt);
        }
    }
}
