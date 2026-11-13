using System.Collections;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
 public sealed class FusionScreenTests
 {
  private CombatGameBootstrap game;private FusionStation station;
  private void Loaded(Scene scene,LoadSceneMode mode){var s=Object.FindAnyObjectByType<FusionStation>();if(s!=null)s.PersistProgress=false;}
  [UnitySetUp] public IEnumerator Setup(){SceneManager.sceneLoaded+=Loaded;yield return SceneManager.LoadSceneAsync("CombatArena");yield return null;SceneManager.sceneLoaded-=Loaded;game=Object.FindAnyObjectByType<CombatGameBootstrap>();station=Object.FindAnyObjectByType<FusionStation>();game.Director.AutoTick=false;game.Encounter.AutoTick=false;game.ResetRun(false);game.Session.ResumeAllUserPauses();}
  [UnityTearDown] public IEnumerator Cleanup(){SceneManager.sceneLoaded-=Loaded;Time.timeScale=1;if(game!=null){var s=game.gameObject.scene;SceneManager.SetActiveScene(SceneManager.CreateScene("ScreenCleanup"));yield return SceneManager.UnloadSceneAsync(s);}CombatTargetRegistry.Clear();}
  [UnityTest] public IEnumerator FrontScreenShowsCountdownReadyAndHidesForPause()
  {
   var screen=station.GetComponent<FusionMachineScreen>();Assert.IsNotNull(screen);Assert.AreEqual(RenderMode.WorldSpace,screen.ScreenCanvas.renderMode);
   Assert.AreEqual("FUSIÓN",screen.StateText);station.Progress.phase=FusionPhase.Fusing;station.Progress.remaining=74;screen.Refresh();Assert.AreEqual("FUSIONANDO",screen.StateText);Assert.AreEqual("01:14",screen.TimeText);
   Object.FindAnyObjectByType<WarehouseTravel>().PlaceInWarehouse(true);game.Player.transform.position=station.InteractionPoint.position+Vector3.up;game.Player.GetComponent<Rigidbody>().position=game.Player.transform.position;Physics.SyncTransforms();yield return new WaitForSeconds(.3f);screen.Refresh();yield return Capture("PantallaFusionJuego",game.GameplayCamera.transform.position,game.GameplayCamera.transform.rotation);
   yield return Capture("PantallaFusionCerca",station.transform.position+new Vector3(-3.1f,4,-8),Quaternion.Euler(5,0,0));
   station.Progress.phase=FusionPhase.Ready;screen.Refresh();Assert.AreEqual("LISTO",screen.StateText);Assert.AreEqual("E · VER",screen.TimeText);
   game.Session.SetManualPause(true);screen.Refresh();Assert.IsFalse(screen.ScreenCanvas.enabled);game.Session.SetManualPause(false);screen.Refresh();Assert.IsTrue(screen.ScreenCanvas.enabled);
  }
  private IEnumerator Capture(string name,Vector3 position,Quaternion rotation)
  {
   var camera=new GameObject("ScreenAudit").AddComponent<Camera>();camera.transform.SetPositionAndRotation(position,rotation);camera.fieldOfView=45;var rt=new RenderTexture(1400,900,24);camera.targetTexture=rt;yield return null;camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1400,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1400,900),0,0);image.Apply();Directory.CreateDirectory("Logs/Screen");File.WriteAllBytes("Logs/Screen/"+name+".png",image.EncodeToPNG());RenderTexture.active=previous;Object.Destroy(image);Object.Destroy(rt);Object.Destroy(camera.gameObject);
  }
 }
}
