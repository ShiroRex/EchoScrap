using System.Collections;
using System.IO;
using System.Linq;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace EchoScrap.Tests
{
    public sealed class LocalVisualEffectsTests
    {
        private CombatGameBootstrap game;
        [UnitySetUp] public IEnumerator Setup()
        {
            Time.timeScale=1;yield return SceneManager.LoadSceneAsync("CombatArena");yield return null;
            game=Object.FindAnyObjectByType<CombatGameBootstrap>();game.Director.AutoTick=false;game.Encounter.AutoTick=false;
            game.ResetRun(false);game.Weapon.enabled=false;game.Session.ResumeFromFocusLoss();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.timeScale=1;var scene=game.gameObject.scene;SceneManager.SetActiveScene(SceneManager.CreateScene("LocalVisualCleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);CombatTargetRegistry.Clear();
        }
        [UnityTest] public IEnumerator DamageEdgesLeaveCenterClearAndDisappearWhenHealedOrPaused()
        {
            var hurt=game.Player.GetComponent<PlayerHurtPresentation>();
            game.Player.TakeContactOrHazardDamage(80,Vector3.forward);hurt.StepPresentation(.4f);
            Assert.Greater(hurt.ScreenBorderAlpha,.1f);Assert.LessOrEqual(hurt.ScreenBorderAlpha,.28f);
            var border=game.Hud.HudCanvas.transform.Find("HurtBorder").GetComponent<Image>();
            Assert.IsFalse(border.raycastTarget);Assert.AreEqual(Image.Type.Simple,border.type);
            var rt=RenderTexture.GetTemporary(256,256);Graphics.Blit(border.sprite.texture,rt);var old=RenderTexture.active;RenderTexture.active=rt;
            var pixels=new Texture2D(256,256,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();
            Assert.AreEqual(0,pixels.GetPixel(128,128).a);Assert.AreEqual(0,pixels.GetPixel(40,128).a);Assert.Greater(pixels.GetPixel(0,128).a,.9f);
            RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.Destroy(pixels);
            yield return Capture("PocaVida",Camera.main,true);
            game.Session.SetManualPause(true);yield return null;Assert.AreEqual(0,border.color.a);
            game.Session.SetManualPause(false);game.Player.Heal(100);hurt.StepPresentation(.5f);Assert.AreEqual(0,hurt.ScreenBorderAlpha);
        }
        [UnityTest] public IEnumerator PortalAndLavaShadersRenderAndLavaClockRespectsPause()
        {
            var lava=Object.FindAnyObjectByType<LavaHazard>();var renderer=lava.GetComponentInChildren<Renderer>();
            Assert.AreEqual("EchoScrap/StylizedLava",renderer.sharedMaterial.shader.name);Assert.IsTrue(renderer.sharedMaterial.shader.isSupported);
            lava.AutoTick=false;lava.StepHazard(1);float time=lava.AnimationTime;Assert.Greater(time,0);
            game.Session.SetManualPause(true);lava.StepHazard(1);Assert.AreEqual(time,lava.AnimationTime);game.Session.SetManualPause(false);
            var camera=new GameObject("VisualAuditCamera").AddComponent<Camera>();
            camera.transform.position=renderer.bounds.center+new Vector3(4,8,-8);camera.transform.LookAt(renderer.bounds.center);
            yield return Capture("LavaEstilizada",camera,false);
            var portal=Object.FindObjectsByType<GalaxyPortalVfx>().OrderBy(p=>p.transform.position.x).First();
            var surface=portal.GetComponent<Renderer>();Assert.IsTrue(surface.sharedMaterial.shader.isSupported);
            camera.transform.position=portal.transform.position+portal.transform.forward*12+Vector3.up;camera.transform.LookAt(portal.transform.position);
            yield return Capture("PortalAgujeroNegro",camera,false);Object.Destroy(camera.gameObject);
        }
        private IEnumerator Capture(string name,Camera camera,bool ui)
        {
            var canvas=game.Hud.HudCanvas;var rt=new RenderTexture(1400,900,24);camera.targetTexture=rt;
            if(ui){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
            yield return null;camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(1400,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1400,900),0,0);image.Apply();
            Directory.CreateDirectory("Logs/VisualEffects");File.WriteAllBytes("Logs/VisualEffects/"+name+".png",image.EncodeToPNG());
            RenderTexture.active=old;camera.targetTexture=null;if(ui){canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;}
            Object.Destroy(image);Object.Destroy(rt);
        }
    }
}
