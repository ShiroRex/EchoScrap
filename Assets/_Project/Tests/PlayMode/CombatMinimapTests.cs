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
            Assert.AreEqual(map.MapPosition(game.Player.transform.position),map.PlayerMarker.anchoredPosition);Assert.Greater(delta.x,0);Assert.Greater(delta.y,0);
            Assert.IsTrue(map.IsExplored(previous));Assert.IsTrue(map.IsExplored(game.Player.transform.position));Assert.IsFalse(map.IsExplored(new Vector3(-95,0,-95)));Assert.Greater(map.VisibleRadius,30);
            var canvas=game.Hud.HudCanvas.transform;var frame=(RectTransform)canvas.Find("Minimap");var kills=(RectTransform)canvas.Find("Kills");
            Assert.AreEqual(new Vector2(1,1),frame.anchorMax);
            Assert.AreEqual(336,frame.sizeDelta.x);Assert.IsNotNull(frame.Find("Interior").GetComponent<UnityEngine.UI.Mask>());
            Assert.AreSame(PixelControlArtwork.Load("MinimapFrame/Frame"),frame.Find("Artwork").GetComponent<UnityEngine.UI.Image>().sprite);
            Assert.AreSame(PixelControlArtwork.Load("MinimapFrame/Mask"),frame.Find("Interior").GetComponent<UnityEngine.UI.Image>().sprite);
            Assert.AreSame(PixelControlArtwork.Load("MinimapMarkers/Portal"),frame.Find("Interior/MapaArena/Portal").GetComponent<UnityEngine.UI.Image>().sprite);
            Assert.AreSame(Resources.Load<Texture2D>("Hud/PortraitAtlas"),map.PlayerMarker.GetComponent<UnityEngine.UI.Image>().sprite.texture);
            Assert.LessOrEqual(map.MapPosition(new Vector3(10000,0,10000)).magnitude,84f);Assert.Less(kills.anchoredPosition.y+kills.sizeDelta.y*.5f,frame.anchoredPosition.y-frame.sizeDelta.y*.5f);
            Assert.IsNotNull(frame.Find("Interior/MapaArena/Portal"));
            var stone=GameObject.Find("roca_baja_-1_41").GetComponentInChildren<Renderer>();Assert.AreEqual(ArenaSurface.Height(stone.bounds.center),stone.bounds.min.y,.15f);Assert.Less(stone.bounds.max.x,-6);
            var old=RenderTexture.active;RenderTexture.active=map.MapTexture;var sample=new Texture2D(256,256,TextureFormat.RGB24,false);sample.ReadPixels(new Rect(0,0,256,256),0,0);sample.Apply();RenderTexture.active=old;
            var colors=new HashSet<Color32>();foreach(var color in sample.GetPixels32())colors.Add(color);Assert.Greater(colors.Count,30,"Minimap must contain the arena, not a blank render target.");Object.Destroy(sample);
            game.Player.transform.position=previous;yield return Capture();
        }
        [UnityTest] public IEnumerator MapStaysBehindPauseAndHidesInTheWarehouse()
        {
            game.Session.SetManualPause(true);map.Refresh();Assert.IsTrue(map.IsVisible);
            game.Session.SetManualPause(false);map.Refresh();Assert.IsTrue(map.IsVisible);
            Assert.IsTrue(Object.FindAnyObjectByType<WarehouseTravel>().PlaceInWarehouse(true));map.Refresh();Assert.IsFalse(map.IsVisible);
            game.ResetRun();map.Refresh();Assert.IsTrue(map.IsVisible);Assert.AreEqual(30,map.VisibleRadius);yield return null;
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
