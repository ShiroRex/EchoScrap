using System.Collections;
using System.Collections.Generic;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class CombatMinimapTests
    {
        private CombatGameBootstrap game;
        private CombatMinimap map;
        private void Loaded(Scene scene,LoadSceneMode mode){var station=Object.FindAnyObjectByType<FusionStation>();if(station!=null)station.PersistProgress=false;}
        [UnitySetUp] public IEnumerator Setup()
        {
            SceneManager.sceneLoaded+=Loaded;yield return SceneManager.LoadSceneAsync("CombatArena");yield return null;SceneManager.sceneLoaded-=Loaded;
            game=Object.FindAnyObjectByType<CombatGameBootstrap>();game.Director.AutoTick=false;game.Encounter.AutoTick=false;game.ResetRun(false);game.Weapon.enabled=false;
            game.Session.ResumeAllUserPauses();map=game.Hud.GetComponent<CombatMinimap>();map.Refresh();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            SceneManager.sceneLoaded-=Loaded;Time.timeScale=1;
            if(game!=null){var scene=game.gameObject.scene;SceneManager.SetActiveScene(SceneManager.CreateScene("MinimapCleanup"));yield return SceneManager.UnloadSceneAsync(scene);}CombatTargetRegistry.Clear();
        }
        [UnityTest] public IEnumerator MapRendersArenaTracksPlayerAndKeepsKillsBelow()
        {
            Assert.IsTrue(map.IsVisible);Assert.IsTrue(map.MapTexture.IsCreated());
            var before=map.PlayerMarker.anchoredPosition;var previous=game.Player.transform.position;
            game.Player.transform.position=previous+new Vector3(20,0,15);map.Refresh();
            var delta=map.PlayerMarker.anchoredPosition-before;
            Assert.AreEqual(236*20/ArenaSurface.Active.WorldSize,delta.x,.1f);Assert.AreEqual(236*15/ArenaSurface.Active.WorldSize,delta.y,.1f);
            var canvas=game.Hud.HudCanvas.transform;var frame=(RectTransform)canvas.Find("Minimap");var kills=(RectTransform)canvas.Find("Kills");
            Assert.AreEqual(new Vector2(1,1),frame.anchorMax);Assert.Less(kills.anchoredPosition.y+kills.sizeDelta.y*.5f,frame.anchoredPosition.y-frame.sizeDelta.y*.5f);
            Assert.IsTrue(frame.Find("Interior/MapaArena/Portal").gameObject.activeSelf);
            var old=RenderTexture.active;RenderTexture.active=map.MapTexture;var sample=new Texture2D(256,256,TextureFormat.RGB24,false);sample.ReadPixels(new Rect(0,0,256,256),0,0);sample.Apply();RenderTexture.active=old;
            var colors=new HashSet<Color32>();foreach(var color in sample.GetPixels32())colors.Add(color);Assert.Greater(colors.Count,30,"Minimap must contain the arena, not a blank render target.");Object.Destroy(sample);
            game.Player.transform.position=previous;yield return Capture();
        }
        [UnityTest] public IEnumerator MapHidesDuringPauseAndWarehouseAndReturnsForCombat()
        {
            game.Session.SetManualPause(true);map.Refresh();Assert.IsFalse(map.IsVisible);
            game.Session.SetManualPause(false);map.Refresh();Assert.IsTrue(map.IsVisible);
            Assert.IsTrue(Object.FindAnyObjectByType<WarehouseTravel>().PlaceInWarehouse(true));map.Refresh();Assert.IsFalse(map.IsVisible);
            game.ResetRun();map.Refresh();Assert.IsTrue(map.IsVisible);yield return null;
        }
        private IEnumerator Capture()
        {
            yield return null;var camera=new GameObject("MinimapAudit").AddComponent<Camera>();camera.transform.SetPositionAndRotation(game.GameplayCamera.transform.position,game.GameplayCamera.transform.rotation);
            var rt=new RenderTexture(1400,900,24);camera.targetTexture=rt;var canvas=game.Hud.HudCanvas;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            yield return null;camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1400,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1400,900),0,0);image.Apply();Directory.CreateDirectory("Logs/Minimap");File.WriteAllBytes("Logs/Minimap/Partida.png",image.EncodeToPNG());RenderTexture.active=old;
            canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;Object.Destroy(image);Object.Destroy(rt);Object.Destroy(camera.gameObject);
        }
    }
}
