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
        [UnityTest] public IEnumerator NewCharactersSpawnWithFrontSpritesAndCanBeCollected()
        {
            foreach(int id in new[]{13,14}){
                Assert.IsTrue(system.TrySpawn(id));var pickup=Object.FindObjectsByType<CollectibleDisplay>(FindObjectsSortMode.None).Single(v=>v.name=="ColeccionableArena_"+FusionCatalog.Names[id]);
                var sprite=pickup.GetComponentInChildren<SpriteRenderer>().sprite;Assert.AreSame(CollectibleDisplay.FrontSprite(id),sprite);Assert.Less(sprite.rect.width,64);
                int before=station.Progress.owned[id];game.Player.GetComponent<Rigidbody>().position=pickup.transform.position+Vector3.up;game.Player.transform.position=pickup.transform.position+Vector3.up;Physics.SyncTransforms();Assert.IsTrue(system.TryCollectNearest());Assert.AreEqual(before+1,station.Progress.owned[id]);yield return null;
            }
        }
        [UnityTest] public IEnumerator ThreeSlotsRequireOwnedCopiesAndPowersSurviveResetWithoutStacking(){
            for(int id=0;id<4;id++)station.CollectFromArena(id);
            Assert.IsFalse(system.TryOpen());Approach();Assert.IsTrue(system.TryOpen());
            Assert.IsTrue(system.Equip(0,0));Assert.IsFalse(system.Equip(1,0));Assert.IsFalse(system.Equip(1,4));Assert.IsFalse(system.Equip(3,1));
            Assert.IsFalse(system.Equip(1,1));system.Close();Approach(1);Assert.IsTrue(system.TryOpen());Assert.IsTrue(system.Equip(1,1));system.Close();Approach(2);Assert.IsTrue(system.TryOpen());Assert.IsTrue(system.Equip(2,2));
            Assert.AreEqual(0,game.Stats.GetUpgradeTier(UpgradeId.DASH_01));Assert.IsTrue(system.Abilities.Has(0));Assert.IsTrue(system.Abilities.Has(1));Assert.IsTrue(system.Abilities.Has(2));
            game.Stats.SetUpgradeTier("DASH_01",3);system.Close();Approach(0);Assert.IsTrue(station.GetComponent<VaultReleaseInteraction>().TryUnequip());Assert.IsTrue(system.TryOpen());Assert.IsTrue(system.Equip(0,3));Assert.AreEqual(3,game.Stats.GetUpgradeTier(UpgradeId.DASH_01));
            Assert.IsTrue(system.Abilities.Has(3));
            yield return Capture("Equipo");system.Close();yield return Capture("BovedaEquipo");game.ResetRun();game.ResetRun();
            Assert.AreEqual(0,game.Stats.GetUpgradeTier(UpgradeId.DASH_01));Assert.IsTrue(system.Abilities.Has(3));
            for(int i=0;i<3;i++)Assert.AreEqual(station.Progress.equipped[i],station.DisplaySlots[i].GetComponentInChildren<CollectibleDisplay>().CollectibleId);
            yield return null;var hud=station.transform.Find("CompanionCanvas/CompanerosEquipados");Assert.IsTrue(hud.gameObject.activeSelf);
            Assert.AreEqual("PODERES DE COLECCIONABLES",hud.Find("PoderesTitulo").GetComponent<Text>().text);
            Assert.AreEqual(Color.clear,hud.Find("Retrato_0").GetComponent<Image>().color);Assert.IsNotNull(hud.Find("Retrato_0/Cara").GetComponent<Image>().sprite);
            Assert.AreEqual(FusionCatalog.Names[3],hud.Find("Retrato_0/Nombre").GetComponent<Text>().text);
            StringAssert.Contains(CompanionSystem.PowerNames[3],hud.Find("Retrato_0/Poder").GetComponent<Text>().text);
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
            var map=game.Hud.GetComponent<CombatMinimap>();map.Refresh();var marker=game.Hud.HudCanvas.transform.Find("Minimap/Interior/MapaArena/Coleccionable_0");Assert.IsTrue(marker.gameObject.activeSelf);Assert.AreEqual(0,marker.childCount);Assert.AreEqual(Vector2.one*7,marker.GetComponent<RectTransform>().sizeDelta);Assert.IsNotNull(marker.GetComponent<Image>().sprite);
            game.Session.SetManualPause(true);system.Advance(10);Assert.AreEqual(1,system.ActivePickups);game.Session.SetManualPause(false);
            system.Advance(9.5f);yield return null;map.Refresh();Assert.AreEqual(0,system.ActivePickups);Assert.IsFalse(marker.gameObject.activeSelf);Assert.IsFalse(system.TryCollectNearest());
        }
        [UnityTest] public IEnumerator TenCapsulesAndOwnedOnlyEquipmentAtIndividualStation()
        {
            Assert.AreEqual(10,station.DisplaySlots.Length);station.ResetCollection();station.CollectFromArena(0);station.CollectFromArena(6);
            Approach(1);Assert.IsTrue(system.TryOpen());Assert.AreEqual(1,system.SelectedSlot);
            var panel=station.transform.Find("CompanionCanvas/EquipoBoveda/Panel");
            var grid=panel.GetComponentInChildren<CollectibleUnitGrid>();Assert.AreEqual(2,grid.VisibleUnits);Assert.AreEqual(0,grid.UnitId(0));Assert.AreEqual(6,grid.UnitId(1));
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
        [UnityTest] public IEnumerator SpawnHasNoDiscAndShowsSmallAnimatedAnnouncement()
        {
            Assert.IsTrue(system.TrySpawn(0));yield return new WaitForSeconds(.35f);
            var display=Object.FindObjectsByType<CollectibleDisplay>(FindObjectsSortMode.None).Single(d=>d.name=="ColeccionableArena_Lobito");Assert.IsNull(display.transform.Find("Rareza"));
            var notice=station.transform.Find("CompanionCanvas/AvisoColeccionables");Assert.IsTrue(notice.gameObject.activeSelf);
            var label=notice.GetComponentInChildren<Text>();Assert.AreEqual("¡NUEVOS COLECCIONABLES!",label.text);Assert.AreSame(Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro").AnnouncementFont,label.font);Assert.LessOrEqual(label.fontSize,36);
            Assert.Greater(notice.GetComponent<CanvasGroup>().alpha,.9f);yield return Capture("AvisoColeccionables");
            yield return new WaitForSeconds(2);Assert.IsFalse(notice.gameObject.activeSelf);
        }
        [UnityTest] public IEnumerator AuthoredFramesAreUsedForEveryChoiceAndTheThreeCombatSlots()
        {
            for(int id=0;id<FusionCatalog.Count;id++){station.Progress.owned[id]=1;Assert.IsNotNull(CollectibleDisplay.CardPortrait(id),FusionCatalog.Names[id]);}
            var ids=new[]{13,14,4};
            for(int slot=0;slot<3;slot++){Approach(slot);Assert.IsTrue(system.TryOpen());Assert.IsTrue(system.Equip(slot,ids[slot]));
                var panel=station.transform.Find("CompanionCanvas/EquipoBoveda/Panel");
                var available=Enumerable.Range(0,FusionCatalog.Count).Where(id=>station.Progress.Available(id)>0).ToArray();var grid=panel.GetComponentInChildren<CollectibleUnitGrid>();Assert.AreEqual(Mathf.Min(10,available.Length),grid.VisibleUnits);for(int i=0;i<grid.VisibleUnits;i++){var art=panel.Find("Unidades/Unidad_"+i+"/Retrato").GetComponent<Image>();Assert.AreSame(CollectibleDisplay.CardPortrait(available[i]),art.sprite);Assert.AreEqual(Color.white,art.color);Assert.IsTrue(art.preserveAspect);}
                system.Close();}
            game.ResetRun(false);game.Session.ResumeAllUserPauses();yield return null;
            var hud=station.transform.Find("CompanionCanvas/CompanerosEquipados");Assert.IsTrue(hud.gameObject.activeSelf);
            for(int slot=0;slot<3;slot++){var art=hud.Find("Retrato_"+slot+"/Cara").GetComponent<Image>();Assert.AreSame(CollectibleDisplay.CardPortrait(ids[slot]),art.sprite);Assert.IsTrue(art.enabled);Assert.AreEqual(Vector2.one*60,art.rectTransform.sizeDelta);}
            yield return Capture("MarcosCombate");
        }
        [UnityTest] public IEnumerator ReleaseOnlyAtOccupiedGalleryAndEquipmentOnlyListsFreeCopies()
        {
            station.ResetCollection();station.Progress.owned[0]=2;station.Progress.owned[6]=1;station.RefreshDisplays();
            var release=station.GetComponent<VaultReleaseInteraction>();Approach(0);Assert.IsFalse(release.TryOpen());Assert.IsTrue(system.TryOpen());
            var grid=station.GetComponentInChildren<CollectibleUnitGrid>();Assert.AreEqual(3,grid.VisibleUnits);Assert.AreEqual(0,grid.UnitId(0));Assert.AreEqual(0,grid.UnitId(1));Assert.IsFalse(grid.GetComponentsInChildren<Button>(true).Any(b=>b.name=="LIBERAR"));
            Assert.IsTrue(system.Equip(0,0));grid.Refresh(true);Assert.AreEqual(2,grid.VisibleUnits);system.Close();Approach(1);Assert.IsTrue(system.TryOpen());Assert.IsTrue(system.Equip(1,0));grid.Refresh(true);Assert.AreEqual(1,grid.VisibleUnits);Assert.AreEqual(6,grid.UnitId(0));system.Close();
            Approach(0);Assert.IsTrue(release.TryOpen());release.Close();Approach(3);Assert.AreEqual(3,release.NearestSlot);Assert.IsFalse(system.TryOpen());yield return null;yield return Capture("LiberarEnGaleria",true);
            Assert.IsTrue(release.TryOpen());Assert.IsTrue(game.Session.IsPaused);StringAssert.Contains("Hada",release.ConfirmationText);yield return Capture("ConfirmarLiberacionGaleria",true);
            release.Close();Assert.AreEqual(1,station.Progress.owned[6]);Assert.IsTrue(release.TryOpen());Assert.IsTrue(release.ConfirmRelease());Assert.AreEqual(0,station.Progress.owned[6]);Assert.IsFalse(release.ConfirmRelease());Assert.IsFalse(release.TryOpen());CollectionAssert.AreEqual(new[]{0,0,-1},station.Progress.equipped);
        }
        [UnityTest] public IEnumerator OccupiedEquipmentSupportsUnequipWithoutLossAndConfirmedRelease()
        {
            station.ResetCollection();station.Progress.owned[0]=1;Approach();Assert.IsTrue(system.TryOpen());Assert.IsTrue(system.Equip(0,0));system.Close();var release=station.GetComponent<VaultReleaseInteraction>();
            Assert.IsFalse(system.TryOpen());Assert.IsTrue(release.TryUnequip());Assert.AreEqual(1,station.Progress.owned[0]);Assert.AreEqual(-1,station.Progress.equipped[0]);Assert.AreEqual(0,game.Stats.GetUpgradeTier(UpgradeId.DASH_01));
            Assert.IsTrue(system.TryOpen());Assert.IsTrue(system.Equip(0,0));system.Close();Assert.IsTrue(release.TryOpen());release.Close();Assert.AreEqual(0,station.Progress.equipped[0]);Assert.AreEqual(1,station.Progress.owned[0]);
            Assert.IsTrue(release.TryOpen());Assert.IsTrue(release.ConfirmRelease());Assert.AreEqual(0,station.Progress.owned[0]);Assert.AreEqual(-1,station.Progress.equipped[0]);Assert.AreEqual(0,game.Stats.GetUpgradeTier(UpgradeId.DASH_01));yield return null;
        }
        private IEnumerator Capture(string name,bool release=false){
            yield return null;yield return null;
            var camera=new GameObject("CompanionAudit").AddComponent<Camera>();camera.transform.SetPositionAndRotation(game.GameplayCamera.transform.position,game.GameplayCamera.transform.rotation);
            var rt=new RenderTexture(1400,900,24);camera.targetTexture=rt;var canvas=station.transform.Find(release?"LiberarBovedaCanvas":"CompanionCanvas").GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var hudCanvas=game.Hud.HudCanvas;hudCanvas.renderMode=RenderMode.ScreenSpaceCamera;hudCanvas.worldCamera=camera;hudCanvas.planeDistance=1.1f;
            yield return null;camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1400,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1400,900),0,0);image.Apply();Directory.CreateDirectory("Logs/Companions");File.WriteAllBytes("Logs/Companions/"+name+".png",image.EncodeToPNG());RenderTexture.active=old;
            canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;hudCanvas.renderMode=RenderMode.ScreenSpaceOverlay;hudCanvas.worldCamera=null;Object.Destroy(image);Object.Destroy(rt);Object.Destroy(camera.gameObject);
        }
    }
}

