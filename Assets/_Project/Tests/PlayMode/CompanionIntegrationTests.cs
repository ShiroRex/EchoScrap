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
    public sealed class CompanionIntegrationTests
    {
        private CombatGameBootstrap game;private FusionStation station;private CompanionSystem system;
        private void Loaded(Scene scene,LoadSceneMode mode){var s=Object.FindAnyObjectByType<FusionStation>();if(s!=null)s.PersistProgress=false;}
        [UnitySetUp] public IEnumerator Setup(){SceneManager.sceneLoaded+=Loaded;yield return SceneManager.LoadSceneAsync("CombatArena");yield return null;SceneManager.sceneLoaded-=Loaded;
            game=Object.FindAnyObjectByType<CombatGameBootstrap>();station=Object.FindAnyObjectByType<FusionStation>();system=station.GetComponent<CompanionSystem>();system.AutoTick=false;
            game.Director.AutoTick=false;game.Encounter.AutoTick=false;game.ResetRun(false);game.Weapon.enabled=false;game.Session.ResumeAllUserPauses();}
        [UnityTearDown] public IEnumerator Cleanup(){SceneManager.sceneLoaded-=Loaded;system?.Close();Time.timeScale=1;
            if(game!=null){var scene=game.gameObject.scene;SceneManager.SetActiveScene(SceneManager.CreateScene("CompanionCleanup"));yield return SceneManager.UnloadSceneAsync(scene);}CombatTargetRegistry.Clear();}
        private void Approach(int slot=0){Object.FindAnyObjectByType<WarehouseTravel>().PlaceInWarehouse(true);var p=station.DisplaySlots[slot].position+Vector3.back*3;
            p=ArenaSurface.OnGround(p,1);game.Player.transform.position=p;game.Player.GetComponent<Rigidbody>().position=p;Physics.SyncTransforms();}
        [UnityTest] public IEnumerator SpawnWeightsRespectMapRaritiesAndTimerRespectsPauseAndVault(){
            int[] counts=new int[6];for(int i=0;i<100;i++)counts[(int)FusionCatalog.Rarities[CompanionSystem.RollSpawn(i)]]++;CollectionAssert.AreEqual(new[]{50,30,10,7,3,0},counts);
            system.Advance(29);Assert.AreEqual(0,system.ActivePickups);game.Session.SetManualPause(true);system.Advance(60);Assert.AreEqual(29,system.SpawnElapsed);
            game.Session.SetManualPause(false);system.Advance(1);Assert.That(system.ActivePickups,Is.InRange(1,3));Assert.IsFalse(system.TrySpawn(4));
            game.Session.SetWarehouseVisit(true);system.Advance(30);Assert.AreEqual(0,system.SpawnElapsed);game.Session.SetWarehouseVisit(false);
            game.ResetRun();Assert.AreEqual(0,system.ActivePickups);yield return null;
        }
        [UnityTest] public IEnumerator ArenaPickupAddsCopyOnlyOnce(){
            Assert.IsTrue(system.TrySpawn(2));var pickup=Object.FindObjectsByType<CollectibleDisplay>(FindObjectsSortMode.None).Single(v=>v.name=="ColeccionableArena_Demonio");
            int before=station.Progress.owned[2];game.Player.GetComponent<Rigidbody>().position=pickup.transform.position+Vector3.up;
            game.Player.transform.position=pickup.transform.position+Vector3.up;Physics.SyncTransforms();system.Advance(.1f);Assert.AreEqual(before,station.Progress.owned[2]);Assert.IsTrue(system.TryCollectNearest());Assert.AreEqual(before+1,station.Progress.owned[2]);
            system.Advance(.1f);Assert.AreEqual(before+1,station.Progress.owned[2]);Assert.AreEqual(0,system.ActivePickups);yield return null;
        }
        [UnityTest] public IEnumerator ThreeSlotsRequireOwnedCopiesAndPowersSurviveResetWithoutStacking(){
            for(int id=0;id<4;id++)station.CollectFromArena(id);
            Assert.IsFalse(system.TryOpen());Approach();Assert.IsTrue(system.TryOpen());
            Assert.IsTrue(system.Equip(0,0));Assert.IsFalse(system.Equip(1,0));Assert.IsFalse(system.Equip(1,4));Assert.IsFalse(system.Equip(3,1));
            Assert.IsFalse(system.Equip(1,1));system.Close();Approach(1);Assert.IsTrue(system.TryOpen());Assert.IsTrue(system.Equip(1,1));system.Close();Approach(2);Assert.IsTrue(system.TryOpen());Assert.IsTrue(system.Equip(2,2));
            Assert.AreEqual(1,game.Stats.GetUpgradeTier(UpgradeId.DASH_01));Assert.AreEqual(1,game.Stats.GetUpgradeTier(UpgradeId.CRYO_01));Assert.AreEqual(1,game.Stats.GetUpgradeTier(UpgradeId.DEMO_02));
            game.Stats.SetUpgradeTier("DASH_01",3);system.Close();Approach(0);Assert.IsTrue(system.TryOpen());Assert.IsTrue(system.Equip(0,3));Assert.AreEqual(3,game.Stats.GetUpgradeTier(UpgradeId.DASH_01));
            Assert.AreEqual(1,game.Stats.GetUpgradeTier(UpgradeId.BIO_02));
            yield return Capture("Equipo");system.Close();yield return Capture("BovedaEquipo");game.ResetRun();game.ResetRun();
            Assert.AreEqual(0,game.Stats.GetUpgradeTier(UpgradeId.DASH_01));Assert.AreEqual(1,game.Stats.GetUpgradeTier(UpgradeId.BIO_02));
            for(int i=0;i<3;i++)Assert.AreEqual(station.Progress.equipped[i],station.DisplaySlots[i].GetComponentInChildren<CollectibleDisplay>().CollectibleId);
            yield return null;var hud=station.transform.Find("CompanionCanvas/CompanerosEquipados");Assert.IsTrue(hud.gameObject.activeSelf);
            Assert.AreEqual(FusionCatalog.ColorOf(3),hud.GetChild(0).GetComponent<Image>().color);Assert.IsNotNull(hud.GetChild(0).GetChild(0).GetComponent<Image>().sprite);
            yield return Capture("Retratos");game.Session.SetManualPause(true);yield return null;Assert.IsFalse(hud.gameObject.activeSelf);
        }
        [UnityTest] public IEnumerator EquipmentRoundTripsAndMalformedSaveIsRepaired(){
            var progress=new FusionProgress();for(int id=0;id<4;id++)progress.owned[id]=1;progress.equipped=new[]{0,1,2};var copy=JsonUtility.FromJson<FusionProgress>(JsonUtility.ToJson(progress));copy.Validate();CollectionAssert.AreEqual(progress.equipped,copy.equipped);
            copy.equipped=new[]{4,0,0};copy.Validate();CollectionAssert.AreEqual(new[]{-1,0,-1},copy.equipped);
            copy.equipped=null;copy.Validate();CollectionAssert.AreEqual(new[]{-1,-1,-1},copy.equipped);yield return null;
        }
        [UnityTest] public IEnumerator PickupsAreLabeledFloatExpireAndShowOnMinimap()
        {
            Assert.IsTrue(system.TrySpawn(5));var display=Object.FindObjectsByType<CollectibleDisplay>(FindObjectsSortMode.None).Single(v=>v.name=="ColeccionableArena_Basket");
            Vector3 original=display.transform.position;system.Advance(.5f);yield return null;
            Assert.AreNotEqual(original.y,display.transform.position.y);Assert.AreEqual(2,display.GetComponentsInChildren<Text>().Length);
            var texts=display.GetComponentsInChildren<Text>().Select(t=>t.text).ToArray();CollectionAssert.AreEquivalent(new[]{"Basket","COMÚN"},texts);
            var map=game.Hud.GetComponent<CombatMinimap>();map.Refresh();var marker=game.Hud.HudCanvas.transform.Find("Minimap/Interior/MapaArena/Coleccionable_0");Assert.IsTrue(marker.gameObject.activeSelf);
            game.Session.SetManualPause(true);system.Advance(10);Assert.AreEqual(1,system.ActivePickups);game.Session.SetManualPause(false);
            system.Advance(9.5f);yield return null;map.Refresh();Assert.AreEqual(0,system.ActivePickups);Assert.IsFalse(marker.gameObject.activeSelf);Assert.IsFalse(system.TryCollectNearest());
        }
        [UnityTest] public IEnumerator TenCapsulesAndOwnedOnlyEquipmentAtIndividualStation()
        {
            Assert.AreEqual(10,station.DisplaySlots.Length);station.ResetCollection();station.CollectFromArena(0);station.CollectFromArena(6);
            Approach(1);Assert.IsTrue(system.TryOpen());Assert.AreEqual(1,system.SelectedSlot);
            var panel=station.transform.Find("CompanionCanvas/EquipoBoveda/Panel");
            for(int id=0;id<FusionCatalog.Count;id++)Assert.AreEqual(id==0||id==6,panel.Find("Elegir_"+id).gameObject.activeSelf);
            Assert.AreEqual(0,panel.GetComponentsInChildren<Button>().Count(b=>b.name.StartsWith("Puesto_")));
            Assert.IsFalse(system.Equip(0,0));Assert.IsTrue(system.Equip(1,6));system.Close();
            Assert.AreEqual(-1,station.Progress.equipped[0]);Assert.AreEqual(6,station.Progress.equipped[1]);
            for(int id=0;id<FusionCatalog.Count;id++)station.Progress.owned[id]=1;station.RefreshDisplays();
            Assert.AreEqual(7,station.DisplaySlots.Skip(3).Count(t=>t.GetComponentInChildren<CollectibleDisplay>().CollectibleId>=0));yield return null;
        }
        [UnityTest] public IEnumerator SpawnsVaryAcrossArenaAndAvoidSolidObstacles()
        {
            var previous=Random.state;Random.InitState(8721);
            float minX=100,maxX=-100,minZ=100,maxZ=-100;
            for(int i=0;i<12;i++){Assert.IsTrue(system.TrySpawn(0));var p=system.PickupPosition(i);minX=Mathf.Min(minX,p.x);maxX=Mathf.Max(maxX,p.x);minZ=Mathf.Min(minZ,p.z);maxZ=Mathf.Max(maxZ,p.z);
                Assert.IsFalse(Physics.CheckCapsule(p+Vector3.up,p+Vector3.up*2,.6f,1,QueryTriggerInteraction.Ignore));}
            Random.state=previous;Assert.Greater(maxX-minX,90);Assert.Greater(maxZ-minZ,90);yield return null;
        }
        private IEnumerator Capture(string name){
            yield return null;yield return null;
            var camera=new GameObject("CompanionAudit").AddComponent<Camera>();camera.transform.SetPositionAndRotation(game.GameplayCamera.transform.position,game.GameplayCamera.transform.rotation);
            var rt=new RenderTexture(1400,900,24);camera.targetTexture=rt;var canvas=station.transform.Find("CompanionCanvas").GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var hudCanvas=game.Hud.HudCanvas;hudCanvas.renderMode=RenderMode.ScreenSpaceCamera;hudCanvas.worldCamera=camera;hudCanvas.planeDistance=1.1f;
            yield return null;camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1400,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1400,900),0,0);image.Apply();Directory.CreateDirectory("Logs/Companions");File.WriteAllBytes("Logs/Companions/"+name+".png",image.EncodeToPNG());RenderTexture.active=old;
            canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;hudCanvas.renderMode=RenderMode.ScreenSpaceOverlay;hudCanvas.worldCamera=null;Object.Destroy(image);Object.Destroy(rt);Object.Destroy(camera.gameObject);
        }
    }
}
