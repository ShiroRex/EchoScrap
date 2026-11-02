using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace EchoScrap.Tests
{
    public sealed class FusionIntegrationTests
    {
        private CombatGameBootstrap game;private FusionStation station;private Keyboard keyboard;
        private InputSettings.BackgroundBehavior background;private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private readonly List<InputDevice> disabled=new List<InputDevice>();
        private void Loaded(Scene scene,LoadSceneMode mode){var s=Object.FindAnyObjectByType<FusionStation>();if(s!=null)s.PersistProgress=false;}
        [UnitySetUp] public IEnumerator Setup()
        {
            background=InputSystem.settings.backgroundBehavior;editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            foreach(var device in new List<InputDevice>(InputSystem.devices))if(device.enabled){disabled.Add(device);InputSystem.DisableDevice(device);}
            keyboard=InputSystem.AddDevice<Keyboard>();Time.timeScale=1;SceneManager.sceneLoaded+=Loaded;
            yield return SceneManager.LoadSceneAsync("CombatArena");yield return null;SceneManager.sceneLoaded-=Loaded;
            game=Object.FindAnyObjectByType<CombatGameBootstrap>();station=Object.FindAnyObjectByType<FusionStation>();
            for(int id=0;id<4;id++)station.Progress.owned[id]=20;station.RefreshDisplays(); // Isolated test fixture, never runtime starting inventory.
            game.Director.AutoTick=false;game.Encounter.AutoTick=false;game.ResetRun(false);game.Weapon.enabled=false;
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus",true);game.Session.ResumeFromFocusLoss();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            SceneManager.sceneLoaded-=Loaded;if(station!=null)station.CloseMenu();Time.timeScale=1;
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);foreach(var device in disabled)if(device.added)InputSystem.EnableDevice(device);disabled.Clear();
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
            if(game!=null){var scene=game.gameObject.scene;SceneManager.SetActiveScene(SceneManager.CreateScene("FusionCleanup"));yield return SceneManager.UnloadSceneAsync(scene);}CombatTargetRegistry.Clear();
        }
        private IEnumerator Approach()
        {
            var p=ArenaSurface.OnGround(station.InteractionPoint.position,1);game.Player.GetComponent<Rigidbody>().position=p;game.Player.transform.position=p;Physics.SyncTransforms();yield return new WaitForFixedUpdate();yield return null;
        }
        [UnityTest] public IEnumerator EOpensInventoryAndCommonFusionCanBeCollectedOnce()
        {
            Assert.IsFalse(station.TryOpenMenu(),"Cannot open remotely.");yield return Approach();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));yield return null;yield return null;
            Assert.IsTrue(station.IsMenuOpen);Assert.IsTrue(game.Session.Clock.HasPauseReason(PauseReason.FusionMenu));
            var ui=station.GetComponent<FusionStationUI>();ui.AddIngredient(0);ui.AddIngredient(0);ui.AddIngredient(0);ui.AddIngredient(0);
            yield return Capture("InventarioFusion",true);
            Assert.IsTrue(station.StartFusion(new[]{0,0,0,0}));Assert.IsFalse(station.IsMenuOpen);Assert.IsFalse(game.Session.IsPaused);Assert.AreEqual(60,station.Progress.duration);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            Assert.IsFalse(station.TryOpenMenu(),"A running fusion cannot be restarted.");station.Advance(60);Assert.AreEqual(FusionPhase.Ready,station.Progress.phase);Assert.IsTrue(station.TryOpenMenu());station.Advance(1.3f);
            yield return new WaitForSecondsRealtime(.7f);
            int id=station.Progress.result;int before=station.Progress.owned[id];Assert.IsTrue(station.IsMenuOpen);
            yield return Capture("ResultadoFusion",true);
            Assert.IsTrue(station.Resolve(true));Assert.AreEqual(before+1,station.Progress.owned[id]);Assert.IsFalse(station.Resolve(true));
            Assert.IsFalse(game.Session.IsPaused);Assert.IsTrue(game.Motor.GetComponent<PlayerInputReader>().CanRead);
        }
        [UnityTest] public IEnumerator LegendaryRevealOpensDoorAndReleaseRemovesIt()
        {
            yield return Approach();station.Progress.owned[6]=4;Assert.IsTrue(station.Progress.Begin(new[]{6,6,6,6},0,0));station.Advance(180);Assert.IsTrue(station.TryOpenMenu());station.Advance(1.3f);yield return new WaitForSecondsRealtime(.8f);
            Assert.AreEqual(4,station.Progress.result);
            var viewPoint=station.ResultPoint.position+Vector3.up*1.8f;
            Assert.IsFalse(Physics.Raycast(viewPoint+Vector3.back*4,Vector3.forward,4,1,QueryTriggerInteraction.Ignore),"The revealed chamber must not retain a solid facade blocking the collectible.");
            Assert.IsTrue(station.Door.GetComponentsInChildren<MeshRenderer>().All(r=>!r.enabled),"Shutter retracts completely.");
            var outcome=station.transform.Find("ResultadoFusion").GetComponent<CollectibleDisplay>();Assert.AreEqual(4,outcome.CollectibleId);Assert.AreSame(CollectibleDisplay.FrontSprite(4),outcome.GetComponentInChildren<SpriteRenderer>().sprite,"Directional back view is not an idle animation frame.");
            yield return Capture("MaquinaAbierta",false);
            Assert.IsTrue(station.IsMenuOpen);Assert.IsTrue(station.Resolve(false));Assert.AreEqual(-1,outcome.CollectibleId);Assert.AreEqual(0,station.Progress.owned[4]);
            yield return new WaitForSeconds(.8f);Assert.IsTrue(station.Door.GetComponentsInChildren<MeshRenderer>().All(r=>r.enabled));
            yield return Capture("MaquinaCerrada",false);
        }
        [UnityTest] public IEnumerator ProductionGarageStartsEmpty()
        {
            station.ResetCollection();
            var shown=station.DisplaySlots.SelectMany(t=>t.GetComponentsInChildren<CollectibleDisplay>()).Where(d=>d.CollectibleId>=0).ToArray();
            Assert.AreEqual(0,shown.Length);Assert.AreEqual(0,station.Progress.owned.Sum());Assert.AreEqual(13,FusionCatalog.Names.Length);
            foreach(var display in shown){Assert.IsNotNull(display.GetComponentInChildren<SpriteRenderer>().sprite);Assert.LessOrEqual(display.GetComponentInChildren<SpriteRenderer>().bounds.size.y,station.DisplayHeight+.05f);Assert.LessOrEqual(display.GetComponentInChildren<SpriteRenderer>().bounds.size.x,2.85f);}
            var portal=Object.FindAnyObjectByType<WarehouseTravel>();Assert.IsTrue(portal.TryTravel(false,game.Player));yield return null;yield return null;
            yield return Capture("ColeccionablesBoveda",false,true);
        }
        [UnityTest] public IEnumerator CapsuleNamesUseCardFontsAndAllRaritiesFit()
        {
            var travel=Object.FindAnyObjectByType<WarehouseTravel>();Assert.IsTrue(travel.TryTravel(false,game.Player));
            var display=station.DisplaySlots[0].GetComponentInChildren<CollectibleDisplay>();
            var skin=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");
            for(int id=0;id<FusionCatalog.Count;id++){
                display.Configure(id,station.DisplayHeight,true);yield return null;
                var texts=display.GetComponentsInChildren<UnityEngine.UI.Text>();
                Assert.AreEqual(FusionCatalog.Names[id],texts[0].text);Assert.AreEqual(FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[id]],texts[1].text);
                Assert.AreSame(skin.semibold,texts[0].font);Assert.AreSame(skin.regular,texts[1].font);
                Canvas.ForceUpdateCanvases();Assert.Greater(texts[0].cachedTextGenerator.characterCountVisible,0);Assert.Greater(texts[1].cachedTextGenerator.characterCountVisible,0);
                Assert.AreEqual(FusionCatalog.ColorOf(id),texts[1].color);
                var sprite=display.GetComponentInChildren<SpriteRenderer>();Assert.LessOrEqual(sprite.bounds.size.x,2.85f);Assert.LessOrEqual(sprite.bounds.size.y,station.DisplayHeight+.05f);
                Assert.AreEqual(display.transform.position.y,sprite.bounds.min.y,.05f);
            }
            game.Session.SetManualPause(true);yield return null;Assert.IsFalse(display.GetComponentInChildren<Canvas>(true).gameObject.activeSelf);
            game.Session.SetManualPause(false);display.Configure(0,station.DisplayHeight,true);yield return null;
            yield return Capture("RotulosCapsulas",false,true);
        }
        [UnityTest] public IEnumerator PausingFreezesFusionAndClosingMenuKeepsOtherPauseReasons()
        {
            station.Progress.Begin(new[]{0,0,0,0},9999,0);game.Session.SetManualPause(true);float remaining=station.Progress.remaining;
            yield return new WaitForSecondsRealtime(.15f);Assert.AreEqual(remaining,station.Progress.remaining);game.Session.SetManualPause(false);
            station.Advance(60);yield return Approach();Assert.IsTrue(station.TryOpenMenu());station.Advance(1.3f);game.Session.SetManualPause(true);station.CloseMenu();Assert.IsTrue(game.Session.IsPaused);game.Session.SetManualPause(false);Assert.IsFalse(game.Session.IsPaused);
        }
        [UnityTest] public IEnumerator HeaderUsesCardFontAndParticlesSurroundMachine()
        {
            yield return Approach();var ui=station.GetComponent<FusionStationUI>();
            Assert.AreEqual("MÁQUINA DE FUSIÓN",ui.HeaderTitle);Assert.AreEqual("",ui.HeaderTime);
            Assert.AreSame(Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin").semibold,ui.HeaderFont);
            Assert.IsNotNull(station.transform.GetComponentInParent<VaultFloorSurface>());Assert.Less(station.InteractionPoint.position.z,station.transform.position.z);
            Assert.AreEqual(3,station.GetComponent<FusionAura>().GetComponentsInChildren<ParticleSystem>().Length);
            Assert.IsNull(station.GetComponentInChildren<LineRenderer>());
            station.Progress.Begin(new[]{0,0,0,0},9999,0);game.Session.SetManualPause(true);yield return null;
            Assert.AreEqual("FUSIONANDO",ui.HeaderTitle);Assert.AreEqual("01:00",ui.HeaderTime);
            foreach(var particles in station.GetComponentsInChildren<ParticleSystem>())particles.Simulate(2);
            yield return Capture("RotuloYAura",true);game.Session.SetManualPause(false);
        }
        [UnityTest] public IEnumerator PauseHidesFloatingFusionLabels()
        {
            yield return Approach();station.Progress.Begin(new[]{0,0,0,0},9999,0);yield return null;
            var canvas=station.GetComponentInChildren<Canvas>();var title=canvas.transform.Find("TituloSobreMaquina");
            game.Session.SetManualPause(true);yield return null;
            Assert.IsFalse(title.gameObject.activeSelf);Assert.IsFalse(canvas.transform.Find("IndicadorFusion").gameObject.activeSelf);
            game.Session.SetManualPause(false);yield return null;Assert.IsTrue(title.gameObject.activeSelf);
        }
        [UnityTest] public IEnumerator FourEmptySlotsPickFromGarageAndReadyWaitsForNearbyE()
        {
            yield return Approach();Assert.IsTrue(station.TryOpenMenu());var ui=station.GetComponent<FusionStationUI>();Assert.AreEqual(0,ui.SelectedCount);
            yield return Capture("CuatroEspaciosVacios",true);
            for(int i=0;i<4;i++){ui.OpenPicker(i);Assert.IsTrue(ui.PickerIsOpen);if(i==0)yield return Capture("SelectorGaraje",true);ui.AddIngredient(i);Assert.IsFalse(ui.PickerIsOpen);}
            Assert.AreEqual(4,ui.SelectedCount);yield return Capture("CuatroSeleccionados",true);
            Assert.IsTrue(station.StartFusion(new[]{0,1,2,3}));
            game.Player.transform.position=Vector3.up;game.Player.GetComponent<Rigidbody>().position=Vector3.up;Physics.SyncTransforms();
            station.Advance(180);station.Advance(10);yield return null;Assert.AreEqual(FusionPhase.Ready,station.Progress.phase);
            Assert.IsTrue(station.Door.GetComponentsInChildren<MeshRenderer>().All(r=>r.enabled));Assert.IsFalse(station.TryOpenMenu());Assert.AreEqual(0,station.GetComponent<FusionRevealSmoke>().RevealCount);
            yield return Approach();Assert.IsTrue(station.TryOpenMenu());Assert.AreEqual(FusionPhase.Opening,station.Progress.phase);Assert.AreEqual(1,station.GetComponent<FusionRevealSmoke>().RevealCount);
            yield return new WaitForSecondsRealtime(.45f);yield return Capture("AbriendoConHumo",false);yield return new WaitForSecondsRealtime(1.5f);Assert.AreEqual(FusionPhase.AwaitingDecision,station.Progress.phase);Assert.IsTrue(station.IsMenuOpen);Assert.IsTrue(station.Resolve(true));
        }
        [UnityTest] public IEnumerator FusionUsesOwnFrameAndCardFontsWithoutPauseMenuBleedingThrough()
        {
            yield return Approach();Assert.IsTrue(station.TryOpenMenu());var ui=station.GetComponent<FusionStationUI>();
            var skin=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");var pause=Resources.Load<PauseMenuSkin>("UI/PauseMenuSkin");
            var canvas=station.GetComponentInChildren<Canvas>();var panel=canvas.transform.Find("InventarioFusion/Panel");
            Assert.IsNull(panel.Find("MarcoFusion").GetComponent<UnityEngine.UI.Image>().sprite);
            var possible=panel.Find("Ingredientes/PosiblesFusiones");Assert.IsNotNull(possible.Find("BordeFusion"));
            Assert.AreEqual(0,possible.GetComponentsInChildren<UnityEngine.UI.Text>().Length);
            var close=panel.Find("Boton_Cerrar · E").GetComponent<UnityEngine.UI.Button>();Assert.IsNull(close.image.sprite);Assert.AreSame(skin.regular,close.GetComponentInChildren<UnityEngine.UI.Text>().font);
            Assert.AreSame(skin.semibold,panel.GetComponentInChildren<UnityEngine.UI.Text>().font);
            game.Session.SetManualPause(true);yield return null;game.Hud.RefreshNow();
            Assert.IsFalse(game.Hud.HudCanvas.transform.Find("PauseAndResultOverlay").gameObject.activeSelf);
            foreach(int id in new[]{0,1,2,3})ui.AddIngredient(id);
            var text=string.Join(" ",possible.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t=>t.text));
            Assert.AreEqual(2,possible.GetComponentsInChildren<UnityEngine.UI.Text>().Length);StringAssert.DoesNotContain("—",text);
            Assert.LessOrEqual(canvas.transform.Find("IndicadorFusion").GetComponent<RectTransform>().sizeDelta.x,220);
            Assert.LessOrEqual(panel.Find("Ingredientes/Boton_FUSIONAR").GetComponent<RectTransform>().sizeDelta.x,210);
            FusionRecipe.TryCreate(new[]{0,1,2,3},out var expected);foreach(int id in FusionCatalog.FusionIds.Where(id=>expected.ResultWeight(id)>0))StringAssert.Contains(expected.ResultPercent(id).ToString("0.##")+"%",text);
            yield return Capture("MarcoYFuenteCorregidos",true);station.CloseMenu();game.Session.SetManualPause(false);
        }
        [UnityTest] public IEnumerator VaultLayoutSupportsTravelStairsAndFusion()
        {
            var travel=Object.FindAnyObjectByType<WarehouseTravel>();
            Assert.IsTrue(travel.TryTravel(false,game.Player));yield return new WaitForFixedUpdate();
            Assert.IsTrue(game.Session.IsInWarehouse);Assert.Greater(station.transform.position.x,150);
            Assert.AreEqual(10,station.DisplaySlots.Length);
            Assert.AreEqual(5,ArenaSurface.Height(new Vector3(167,0,24)),.05f);
            Assert.That(ArenaSurface.Height(new Vector3(160,0,8)),Is.InRange(2.8f,3.8f));
            Assert.AreEqual(0,ArenaSurface.Height(new Vector3(180,0,-12)),.05f);
            Assert.IsTrue(Physics.Raycast(new Vector3(180,1,-10),Vector3.right,26,1,QueryTriggerInteraction.Ignore));
            var body=game.Player.GetComponent<Rigidbody>();var bottom=new Vector3(160,1,1.5f);body.position=bottom;game.Player.transform.position=bottom;Physics.SyncTransforms();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));
            for(int i=0;i<240 && body.position.z<17;i++)yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            yield return VaultCapture();
            Assert.Greater(body.position.z,15,"Stair position: "+body.position+" velocity="+body.linearVelocity+" nearby="+string.Join(";",Physics.OverlapSphere(body.position,2,1,QueryTriggerInteraction.Ignore).Select(c=>c.name+" "+c.bounds)));Assert.Greater(body.position.y,5.8f);
            yield return VaultCapture();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.S));for(int i=0;i<240 && body.position.z>1.5f;i++)yield return new WaitForFixedUpdate();InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;Assert.Less(body.position.z,3);Assert.Less(body.position.y,2);
            yield return Approach();Assert.IsTrue(station.TryOpenMenu());
            Assert.IsTrue(station.StartFusion(new[]{0,0,0,0}));station.Advance(60);
            Assert.AreEqual(FusionPhase.Ready,station.Progress.phase);Assert.IsTrue(station.TryOpenMenu());station.Advance(1.3f);Assert.IsTrue(station.Resolve(false));
            yield return new WaitForSeconds(1.1f);Assert.IsTrue(travel.TryTravel(true,game.Player));Assert.IsFalse(game.Session.IsInWarehouse);
        }
        [UnityTest] public IEnumerator PreviewOnlyShowsEligibleResultsAndCannotInventCopies()
        {
            station.ResetCollection();station.Progress.owned[0]=1;station.Progress.owned[6]=4;
            yield return Approach();Assert.IsTrue(station.TryOpenMenu());var ui=station.GetComponent<FusionStationUI>();
            var possible=station.GetComponentInChildren<Canvas>().transform.Find("InventarioFusion/Panel/Ingredientes/PosiblesFusiones");
            Assert.AreEqual(0,possible.GetComponentsInChildren<UnityEngine.UI.Text>().Length);
            ui.OpenPicker(0);var picker=station.GetComponentInChildren<Canvas>().transform.Find("InventarioFusion/Panel/MiGaraje");for(int id=0;id<FusionCatalog.Count;id++)Assert.AreEqual(id==0||id==6,picker.Find("Garaje_"+id).gameObject.activeSelf);ui.AddIngredient(0);ui.AddIngredient(0);Assert.AreEqual(1,ui.SelectedCount);
            var values=possible.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t=>t.text).ToArray();
            CollectionAssert.AreEquivalent(new[]{"Reaper\n60%","Chico Emo\n40%"},values);
            ui.ClearSelection();for(int i=0;i<4;i++)ui.AddIngredient(6);
            values=possible.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t=>t.text).ToArray();
            CollectionAssert.AreEquivalent(new[]{"Murciélago Cristal\n59.4%","Sirena\n39.6%","Yanikua\n1%"},values);
            yield return Capture("RecetaEpica",true);
            ui.ClearSelection();Assert.AreEqual(0,possible.GetComponentsInChildren<UnityEngine.UI.Text>().Length);
        }
        [UnityTest] public IEnumerator AllProductionSpritesAndPortraitsAreImported()
        {
            foreach(var key in FusionCatalog.ResourceKeys){var front=Resources.Load<Sprite>("Collectibles/"+key+"/000");var face=Resources.Load<Sprite>("Collectibles/"+key+"/Portrait");Assert.IsNotNull(front,key);Assert.IsNotNull(face,key);Assert.AreEqual(FilterMode.Point,front.texture.filterMode);}
            yield return null;
        }
        [UnityTest] public IEnumerator NearbyPickupShowsCollectPromptAndECollectsOnce()
        {
            station.ResetCollection();var companions=station.GetComponent<CompanionSystem>();companions.AutoTick=false;
            Assert.IsTrue(companions.TrySpawn(0));var point=companions.PickupPosition(0)+Vector3.up;
            game.Player.transform.position=point;game.Player.GetComponent<Rigidbody>().position=point;Physics.SyncTransforms();yield return null;
            var prompt=station.transform.Find("CompanionCanvas/ColeccionarIndicador");Assert.IsTrue(prompt.gameObject.activeSelf);
            Assert.AreEqual("E · COLECCIONAR",prompt.GetComponentInChildren<UnityEngine.UI.Text>().text);
            Assert.AreEqual(0,station.Progress.owned[0]);InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));yield return null;yield return null;
            Assert.AreEqual(1,station.Progress.owned[0]);Assert.AreEqual(0,companions.ActivePickups);Assert.IsFalse(prompt.gameObject.activeSelf);
            yield return null;Assert.AreEqual(1,station.Progress.owned[0]);
        }
        [UnityTest] public IEnumerator NarrowResultSpritesAreCenteredOverTheirNames()
        {
            yield return Approach();Assert.IsTrue(station.TryOpenMenu());var ui=station.GetComponent<FusionStationUI>();ui.AddIngredient(1);yield return null;
            var possible=station.GetComponentInChildren<Canvas>().transform.Find("InventarioFusion/Panel/Ingredientes/PosiblesFusiones");
            var images=possible.GetComponentsInChildren<UnityEngine.UI.Image>().Where(i=>i.name=="Retrato").OrderBy(i=>i.rectTransform.anchoredPosition.x).ToArray();
            var names=possible.GetComponentsInChildren<UnityEngine.UI.Text>().OrderBy(t=>t.rectTransform.anchoredPosition.x).ToArray();
            Assert.AreEqual(2,images.Length);
            for(int i=0;i<images.Length;i++){Assert.AreEqual(Vector2.one*.5f,images[i].rectTransform.pivot);Assert.AreEqual(images[i].rectTransform.TransformPoint(images[i].rectTransform.rect.center).x,names[i].rectTransform.TransformPoint(names[i].rectTransform.rect.center).x,.01f);}
            yield return Capture("ResultadosCentrados",true);
        }
        private IEnumerator VaultCapture()
        {
            var camera=new GameObject("VaultAuditCamera").AddComponent<Camera>();camera.fieldOfView=52;
            camera.transform.position=new Vector3(219,48,-49);camera.transform.LookAt(new Vector3(180,2,3));
            var rt=new RenderTexture(1600,1200,24);camera.targetTexture=rt;yield return null;camera.Render();
            var old=RenderTexture.active;RenderTexture.active=rt;var texture=new Texture2D(1600,1200,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1600,1200),0,0);texture.Apply();Directory.CreateDirectory("Logs/Fusion");File.WriteAllBytes("Logs/Fusion/BovedaModular.png",texture.EncodeToPNG());RenderTexture.active=old;
            Object.Destroy(texture);Object.Destroy(rt);Object.Destroy(camera.gameObject);
        }
        private IEnumerator Capture(string name,bool showUi,bool garage=false)
        {
            var camera=new GameObject("FusionAuditCamera").AddComponent<Camera>();camera.fieldOfView=60;
            camera.transform.position=garage?new Vector3(180,7,-27):station.transform.position+new Vector3(9,8,-14);
            camera.transform.LookAt(garage?new Vector3(180,4,12):station.transform.position+Vector3.up*3);
            var rt=new RenderTexture(1400,900,24);camera.targetTexture=rt;
            Canvas canvas=station.GetComponentInChildren<Canvas>();if(showUi){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
            yield return null;yield return null;camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;var texture=new Texture2D(1400,900,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1400,900),0,0);texture.Apply();Directory.CreateDirectory("Logs/Fusion");File.WriteAllBytes("Logs/Fusion/"+name+".png",texture.EncodeToPNG());RenderTexture.active=old;
            if(showUi){canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;}Object.Destroy(texture);Object.Destroy(rt);Object.Destroy(camera.gameObject);
        }
    }
}
