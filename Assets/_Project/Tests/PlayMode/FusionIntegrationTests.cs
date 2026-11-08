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
            for(int id=0;id<4;id++)station.Progress.owned[id]=id==0?4:2;station.RefreshDisplays(); // Ten units, including four common fusion ingredients.
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
            Assert.IsTrue(ui.GetComponentsInChildren<UnityEngine.UI.Text>().Any(text=>text.text=="TIEMPO DE FUSIÓN  00:30"));
            yield return Capture("InventarioFusion",true);
            Assert.IsTrue(station.StartFusion(new[]{0,0,0,0}));Assert.IsFalse(station.IsMenuOpen);Assert.IsFalse(game.Session.IsPaused);Assert.AreEqual(30,station.Progress.duration);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            Assert.IsFalse(station.TryOpenMenu(),"A running fusion cannot be restarted.");station.Advance(30);Assert.AreEqual(FusionPhase.Ready,station.Progress.phase);Assert.IsTrue(station.TryOpenMenu());station.Advance(1.3f);
            yield return new WaitForSecondsRealtime(.7f);
            int id=station.Progress.result;int before=station.Progress.owned[id];Assert.IsTrue(station.IsMenuOpen);
            yield return Capture("ResultadoFusion",true);
            Assert.IsTrue(station.Resolve(true));Assert.AreEqual(before+1,station.Progress.owned[id]);Assert.IsFalse(station.Resolve(true));
            Assert.IsFalse(game.Session.IsPaused);Assert.IsTrue(game.Motor.GetComponent<PlayerInputReader>().CanRead);
        }
        [UnityTest] public IEnumerator FullCollectionKeepsRevealedFusionUntilPlayerFreesSpace()
        {
            yield return Approach();Assert.IsTrue(station.Progress.Begin(new[]{0,0,0,0},9999,0));
            while(station.Progress.HasSpace)Assert.IsTrue(station.CollectFromArena(0));
            station.Advance(180);Assert.IsTrue(station.TryOpenMenu());station.Advance(1.3f);
            int resultId=station.Progress.result;Assert.IsFalse(station.Resolve(true));
            Assert.AreEqual(FusionPhase.AwaitingDecision,station.Progress.phase);Assert.AreEqual(resultId,station.Progress.result);
            Assert.AreEqual(10,station.Progress.OwnedCount);Assert.IsTrue(station.IsMenuOpen);StringAssert.Contains("INVENTARIO LLENO",station.Notice);
            Assert.IsTrue(station.ReleaseCollectible(0));Assert.IsTrue(station.Resolve(true));Assert.AreEqual(10,station.Progress.OwnedCount);
            Assert.AreEqual(1,station.Progress.owned[resultId]);Assert.IsFalse(station.Resolve(true));
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
            Assert.AreEqual(0,shown.Length);Assert.AreEqual(0,station.Progress.owned.Sum());Assert.AreEqual(15,FusionCatalog.Names.Length);
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
                var name=display.transform.Find("NombreRareza/Nombre").GetComponent<UnityEngine.UI.Text>();
                var rarity=display.transform.Find("NombreRareza/Rareza").GetComponent<UnityEngine.UI.Text>();
                Assert.AreEqual(FusionCatalog.Names[id],name.text);Assert.AreEqual(FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[id]],rarity.text);
                Assert.AreSame(skin.semibold,name.font);Assert.AreSame(skin.regular,rarity.font);
                // Measure the text even when the live camera has culled this capsule's label.
                foreach (var text in new[] { name, rarity })
                {
                    var generator = new TextGenerator();
                    Assert.IsTrue(generator.Populate(text.text,text.GetGenerationSettings(text.rectTransform.rect.size)));
                    Assert.Greater(generator.characterCountVisible,0);
                }
                Assert.AreEqual(FusionCatalog.ColorOf(id),rarity.color);
                var sprite=display.GetComponentInChildren<SpriteRenderer>();Assert.LessOrEqual(sprite.bounds.size.x,2.85f);Assert.LessOrEqual(sprite.bounds.size.y,station.DisplayHeight+.05f);
                Assert.AreEqual(display.transform.position.y,sprite.bounds.min.y,.05f);
            }
            game.Session.SetManualPause(true);yield return null;Assert.IsFalse(display.transform.Find("NombreRareza").gameObject.activeSelf);
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
            Assert.AreSame(Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro").AnnouncementFont,ui.HeaderFont);
            Assert.IsNotNull(station.transform.GetComponentInParent<VaultFloorSurface>());Assert.Less(station.InteractionPoint.position.z,station.transform.position.z);
            Assert.AreEqual(3,station.GetComponent<FusionAura>().GetComponentsInChildren<ParticleSystem>().Length);
            Assert.IsNull(station.GetComponentInChildren<LineRenderer>());
            Assert.IsTrue(station.Progress.Begin(new[]{0,0,0,0},9999,0));game.Session.SetManualPause(true);yield return null;
            Assert.AreEqual("FUSIONANDO",ui.HeaderTitle);Assert.AreEqual("00:30",ui.HeaderTime);
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
        [UnityTest] public IEnumerator InventorySelectsFourUnitsThenReviewsAndConfirmsFusion()
        {
            yield return Approach();Assert.IsTrue(station.TryOpenMenu());var ui=station.GetComponent<FusionStationUI>();Assert.AreEqual(0,ui.SelectedCount);
            Assert.IsTrue(ui.PickerIsOpen);
            yield return Capture("SelectorGaraje",true);
            var grid=station.GetComponentInChildren<CollectibleUnitGrid>();
            int[] copies={0,3,4,5};
            for(int i=0;i<copies.Length;i++){grid.SelectUnit(copies[i]);Assert.AreEqual(i+1,ui.SelectedCount);Assert.AreEqual(i<3,ui.PickerIsOpen);}
            Assert.AreEqual(4,ui.SelectedCount);yield return Capture("CuatroSeleccionados",true);
            ui.OpenPicker(0);Assert.IsTrue(ui.PickerIsOpen);
            var panel=ui.transform.Find("FusionCanvas/InventarioFusion/Panel");
            panel.Find("MiGaraje/Boton_VOLVER").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.IsFalse(ui.PickerIsOpen);Assert.AreEqual(4,ui.SelectedCount);
            panel.Find("Ingredientes/Boton_CONFIRMAR").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.AreEqual(FusionPhase.Fusing,station.Progress.phase);
            game.Player.transform.position=Vector3.up;game.Player.GetComponent<Rigidbody>().position=Vector3.up;Physics.SyncTransforms();
            var openingAudio=station.transform.Find("FusionOpenAudio").GetComponent<AudioSource>();Assert.IsNotNull(openingAudio.clip);Assert.IsFalse(openingAudio.isPlaying);
            station.Advance(180);station.Advance(10);yield return null;Assert.AreEqual(FusionPhase.Ready,station.Progress.phase);
            Assert.IsTrue(station.Door.GetComponentsInChildren<MeshRenderer>().All(r=>r.enabled));Assert.IsFalse(station.TryOpenMenu());Assert.AreEqual(0,station.GetComponent<FusionRevealSmoke>().RevealCount);Assert.IsFalse(openingAudio.isPlaying);
            yield return Approach();Assert.IsTrue(station.TryOpenMenu());Assert.AreEqual(FusionPhase.Opening,station.Progress.phase);Assert.AreEqual(1,station.GetComponent<FusionRevealSmoke>().RevealCount);Assert.IsTrue(openingAudio.isPlaying);Assert.IsFalse(openingAudio.loop);
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
            var close=panel.Find("Boton_X").GetComponent<UnityEngine.UI.Button>();Assert.AreSame(Resources.Load<PauseMenuSkin>("UI/PauseMenuSkin").blueButton,close.image.sprite);Assert.AreSame(skin.regular,close.GetComponentInChildren<UnityEngine.UI.Text>().font);
            Assert.AreSame(Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro").AnnouncementFont,panel.GetComponentInChildren<UnityEngine.UI.Text>().font);
            game.Session.SetManualPause(true);yield return null;game.Hud.RefreshNow();
            Assert.IsFalse(game.Hud.HudCanvas.transform.Find("PauseAndResultOverlay").gameObject.activeSelf);
            foreach(int id in new[]{0,1,2,3})ui.AddIngredient(id);
            var text=string.Join(" ",possible.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t=>t.text));
            Assert.AreEqual(2,possible.GetComponentsInChildren<UnityEngine.UI.Text>().Length);StringAssert.DoesNotContain("—",text);
            var prompt=canvas.transform.Find("IndicadorFusion");
            Assert.LessOrEqual(prompt.GetComponent<RectTransform>().sizeDelta.x,360);
            Assert.AreSame(PixelControlArtwork.Load("InteractionPrompt/Plate_Normal"),prompt.GetComponent<UnityEngine.UI.Image>().sprite);
            var confirm=panel.Find("Ingredientes/Boton_CONFIRMAR").GetComponent<UnityEngine.UI.Button>();
            Assert.AreSame(pause.greenButton,confirm.image.sprite);
            Assert.IsTrue(confirm.interactable);
            FusionRecipe.TryCreate(new[]{0,1,2,3},out var expected);foreach(int id in FusionCatalog.FusionIds.Where(id=>expected.ResultWeight(id)>0))StringAssert.Contains(expected.ResultPercent(id).ToString("0.##")+"%",text);
            yield return Capture("MarcoYFuenteCorregidos",true);station.CloseMenu();game.Session.SetManualPause(false);
        }
        [UnityTest] public IEnumerator VaultLayoutSupportsTravelStairsAndFusion()
        {
            var travel=Object.FindAnyObjectByType<WarehouseTravel>();
            Assert.IsTrue(travel.TryTravel(false,game.Player));yield return new WaitForFixedUpdate();
            Assert.IsTrue(game.Session.IsInWarehouse);Assert.Greater(station.transform.position.x,150);
            Assert.AreEqual(10,station.DisplaySlots.Length);
            Bounds capsule=GameObject.Find("Capsula_7").GetComponent<Collider>().bounds;
            Bounds stairEdge=GameObject.Find("BordeEscaleraDerecho").GetComponent<Collider>().bounds;
            Assert.IsFalse(capsule.Intersects(stairEdge));
            Assert.That(station.DisplaySlots[6].position.x,Is.EqualTo(capsule.center.x).Within(.01f));
            Assert.That(station.DisplaySlots[6].position.z,Is.EqualTo(capsule.center.z).Within(.01f));
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
            ui.OpenPicker(0);var picker=station.GetComponentInChildren<Canvas>().transform.Find("InventarioFusion/Panel/MiGaraje");var grid=picker.GetComponentInChildren<CollectibleUnitGrid>();Assert.AreEqual(5,grid.VisibleUnits);Assert.AreEqual(0,grid.UnitId(0));Assert.AreEqual(6,grid.UnitId(1));ui.AddIngredient(0);ui.AddIngredient(0);Assert.AreEqual(1,ui.SelectedCount);
            var values=possible.GetComponentsInChildren<UnityEngine.UI.Text>(true).Where(t=>t.gameObject.activeSelf).Select(t=>PlainResult(t.text)).ToArray();
            CollectionAssert.AreEquivalent(new[]{"Reaper\nCOMÚN\n60%","Chico Emo\nRARO\n40%"},values);
            ui.ClearSelection();for(int i=0;i<4;i++)ui.AddIngredient(6);
            values=possible.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t=>PlainResult(t.text)).ToArray();
            CollectionAssert.AreEquivalent(new[]{$"Murciélago Cristal\nÉPICO\n{59.4f:0.##}%",$"Sirena\nLEGENDARIO\n{39.6f:0.##}%","Yanikua\nMÍTICO\n1%"},values);
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
            Assert.AreEqual("COLECCIONAR",prompt.GetComponentInChildren<UnityEngine.UI.Text>().text);
            Assert.AreSame(PixelControlArtwork.Load("InteractionPrompt/Glyph_E"),prompt.Find("InteractionKey/Glyph").GetComponent<UnityEngine.UI.Image>().sprite);
            Assert.AreEqual(0,station.Progress.owned[0]);InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));yield return null;yield return null;
            Assert.AreEqual(1,station.Progress.owned[0]);Assert.AreEqual(0,companions.ActivePickups);Assert.IsFalse(prompt.gameObject.activeSelf);
            yield return null;Assert.AreEqual(1,station.Progress.owned[0]);
        }
        [UnityTest] public IEnumerator NarrowResultSpritesAreCenteredOverTheirNames()
        {
            yield return Approach();Assert.IsTrue(station.TryOpenMenu());var ui=station.GetComponent<FusionStationUI>();
            station.Progress.owned[1]=4;for(int i=0;i<4;i++)ui.AddIngredient(1);yield return null;
            var possible=station.GetComponentInChildren<Canvas>().transform.Find("InventarioFusion/Panel/Ingredientes/PosiblesFusiones");
            var images=possible.GetComponentsInChildren<UnityEngine.UI.Image>().Where(i=>i.name=="Retrato").OrderBy(i=>i.rectTransform.anchoredPosition.x).ToArray();
            var names=possible.GetComponentsInChildren<UnityEngine.UI.Text>().OrderBy(t=>t.rectTransform.anchoredPosition.x).ToArray();
            Assert.AreEqual(2,images.Length);foreach(var art in images)Assert.That(art.sprite.name,Is.EqualTo("CardPortrait"));
            for(int i=0;i<images.Length;i++){Assert.AreEqual(Vector2.one*.5f,images[i].rectTransform.pivot);Assert.AreEqual(images[i].rectTransform.TransformPoint(images[i].rectTransform.rect.center).x,names[i].rectTransform.TransformPoint(names[i].rectTransform.rect.center).x,.01f);}
            yield return Capture("ResultadosCentrados",true);
        }
        [UnityTest] public IEnumerator ResultProbabilitiesAreDescendingAndReservedUnitsCannotBeReleased()
        {
            station.ResetCollection();station.Progress.owned[0]=2;station.Progress.owned[7]=1;station.Progress.owned[2]=1;
            yield return Approach();Assert.IsTrue(station.TryOpenMenu());var ui=station.GetComponent<FusionStationUI>();foreach(int id in new[]{0,7,2,0})ui.AddIngredient(id);
            var panel=station.GetComponentInChildren<Canvas>().transform.Find("InventarioFusion/Panel");
            var labels=panel.Find("Ingredientes/PosiblesFusiones").GetComponentsInChildren<UnityEngine.UI.Text>().OrderBy(t=>t.rectTransform.anchoredPosition.x).Select(t=>PlainResult(t.text)).ToArray();
            StringAssert.StartsWith($"Chico Emo\nRARO\n{44.55f:0.##}%",labels[0]);StringAssert.StartsWith("Conde Alucard\nESPECIAL",labels[1]);StringAssert.StartsWith("Yanikua\nMÍTICO\n1%",labels[3]);yield return Capture("ProbabilidadesOrdenadas",true);
            ui.OpenPicker(0);var grid=panel.GetComponentInChildren<CollectibleUnitGrid>();Assert.IsFalse(grid.GetComponentsInChildren<UnityEngine.UI.Button>(true).Any(b=>b.name=="LIBERAR"));Assert.AreEqual(2,station.Progress.owned[0]);
        }
        private static string PlainResult(string text) => System.Text.RegularExpressions.Regex.Replace(text,"<[^>]+>","");

        [UnityTest] public IEnumerator EquippedCopyStaysEquippedUntilFusionIsConfirmed()
        {
            station.ResetCollection();station.Progress.owned[0]=2;station.Progress.owned[1]=2;station.Progress.equipped[0]=0;station.GetComponent<CompanionSystem>().RefreshEquipment();
            var companions=station.GetComponent<CompanionSystem>();
            Assert.IsTrue(companions.Abilities.Has(0));yield return Approach();Assert.IsTrue(station.TryOpenMenu());
            var ui=station.GetComponent<FusionStationUI>();
            var grid=station.GetComponentInChildren<CollectibleUnitGrid>();
            Assert.AreEqual(4,grid.VisibleUnits);
            Assert.IsFalse(grid.transform.Find("Unidad_0/EnEquipo").gameObject.activeInHierarchy);
            Assert.IsTrue(grid.transform.Find("Unidad_1/EnEquipo").gameObject.activeInHierarchy);
            Assert.AreEqual("EN EQUIPO 1",grid.transform.Find("Unidad_1/EnEquipo/Texto").GetComponent<UnityEngine.UI.Text>().text);
            grid.SelectUnit(1);
            Assert.AreEqual(0,station.Progress.equipped[0]);Assert.AreEqual(0,station.DisplayedId(0));
            Assert.IsTrue(companions.Abilities.Has(0));Assert.AreEqual(2,station.Progress.owned[0]);
            var panel=station.transform.Find("FusionCanvas/InventarioFusion/Panel");
            panel.Find("Boton_X").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.AreEqual(0,station.Progress.equipped[0]);Assert.IsTrue(companions.Abilities.Has(0));
            Assert.IsTrue(station.TryOpenMenu());
            Assert.AreEqual(0,ui.SelectedCount);
            yield return Capture("Fusion_Copia_Equipada",true);
            grid.SelectUnit(1);
            ui.AddIngredient(0);ui.AddIngredient(1);ui.AddIngredient(1);
            Assert.AreEqual(4,ui.SelectedCount);
            Assert.IsFalse(station.StartFusion(new[]{0,0,1,1},new[]{0,0,-1,-1}));
            Assert.AreEqual(0,station.Progress.equipped[0]);Assert.AreEqual(2,station.Progress.owned[0]);
            yield return Capture("Fusion_Confirmar_Equipada",true);
            panel.Find("Ingredientes/Boton_CONFIRMAR").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.AreEqual(FusionPhase.Fusing,station.Progress.phase);
            Assert.AreEqual(-1,station.Progress.equipped[0]);Assert.AreEqual(-1,station.DisplayedId(0));
            Assert.IsFalse(companions.Abilities.Has(0));
            Assert.AreEqual(0,station.Progress.owned[0]);Assert.AreEqual(0,station.Progress.owned[1]);
            yield return null;
        }

        [UnityTest] public IEnumerator IndexAndFusionRespectTheActiveModalAndEscape()
        {
            Object.FindAnyObjectByType<WarehouseTravel>().PlaceInWarehouse(true);
            var indexButton=station.transform.Find("VaultIndexCanvas/BotonIndiceBoveda").GetComponent<UnityEngine.UI.Button>();
            yield return null;
            Assert.IsTrue(indexButton.gameObject.activeInHierarchy);
            game.Session.SetManualPause(true);
            yield return null;
            Assert.IsFalse(indexButton.gameObject.activeInHierarchy);
            indexButton.onClick.Invoke();
            Assert.IsFalse(station.Index.IsOpen);
            Assert.IsFalse(game.Session.Clock.HasPauseReason(PauseReason.FusionMenu));
            game.Session.SetManualPause(false);
            yield return null;
            indexButton.onClick.Invoke();
            Assert.IsTrue(station.Index.IsOpen);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));
            yield return null;
            yield return null;
            Assert.IsFalse(station.Index.IsOpen);
            Assert.IsFalse(game.Session.IsPaused);

            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            yield return Approach();
            Assert.IsTrue(station.TryOpenMenu());
            indexButton.onClick.Invoke();
            Assert.IsFalse(station.Index.IsOpen);
            Assert.IsTrue(station.IsMenuOpen);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));
            yield return null;
            yield return null;
            Assert.IsFalse(station.IsMenuOpen);
            Assert.IsFalse(game.Session.IsPaused);
        }
        [UnityTest] public IEnumerator EAtGalleryOpensReleaseAndEscapeCancelsWithoutChangingInventory()
        {
            station.ResetCollection();station.Progress.owned[0]=1;station.RefreshDisplays();Object.FindAnyObjectByType<WarehouseTravel>().PlaceInWarehouse(true);
            var p=ArenaSurface.OnGround(station.DisplaySlots[3].position+Vector3.back*3,1);game.Player.transform.position=p;game.Player.GetComponent<Rigidbody>().position=p;Physics.SyncTransforms();yield return null;
            var release=station.GetComponent<VaultReleaseInteraction>();Assert.AreEqual(3,release.NearestSlot);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));yield return null;yield return null;Assert.IsTrue(release.IsOpen);Assert.IsFalse(station.IsMenuOpen);Assert.IsFalse(station.GetComponent<CompanionSystem>().IsOpen);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));yield return null;yield return null;Assert.IsFalse(release.IsOpen);Assert.AreEqual(1,station.Progress.owned[0]);
            var fusionCanvas=station.transform.Find("FusionCanvas");Assert.IsFalse(fusionCanvas.GetComponentsInChildren<UnityEngine.UI.Button>(true).Any(b=>b.name.Contains("LIBERAR")));
        }
        [UnityTest] public IEnumerator InventoryPortraitsUseAnnouncementFontAndNoGrayBacking()
        {
            station.ResetCollection();station.Progress.owned[10]=1;yield return Approach();Assert.IsTrue(station.TryOpenMenu());var ui=station.GetComponent<FusionStationUI>();ui.OpenPicker(0);
            var grid=station.GetComponentInChildren<CollectibleUnitGrid>();var card=grid.transform.Find("Unidad_0");Assert.AreEqual(0,card.GetComponent<UnityEngine.UI.Image>().color.a);Assert.AreEqual(0,card.GetComponent<RectTransform>().anchoredPosition.x);Assert.AreSame(Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro").AnnouncementFont,card.Find("Detalle").GetComponent<UnityEngine.UI.Text>().font);yield return Capture("InventarioUnaUnidad",true);
            station.Progress.owned[10]=10;grid.Refresh(true);Assert.AreEqual(10,grid.VisibleUnits);yield return Capture("InventarioDiezUnidades",true);
        }
        [UnityTest] public IEnumerator VaultMachineAgainstWallIndexByRarityAndWorldPropsGrounded()
        {
            Assert.That(station.transform.position.x,Is.EqualTo(189.0f).Within(0.05f));
            Assert.That(station.transform.position.z,Is.EqualTo(28.88f).Within(0.05f));
            Assert.Less(station.InteractionPoint.position.z,station.transform.position.z);
            station.ResetCollection();
            station.Progress.TryCollect(0);
            station.Progress.TryCollect(0);
            station.Progress.TryCollect(6);
            station.RefreshDisplays();
            Object.FindAnyObjectByType<WarehouseTravel>().PlaceInWarehouse(true);
            yield return null;
            Assert.IsNotNull(station.Index);
            station.Index.Open();
            Assert.IsTrue(station.Index.IsOpen);
            Assert.AreEqual(VaultCollectionIndex.IndexTab.ByRarity,station.Index.ActiveTab);
            Assert.AreEqual(Color.white,station.Index.GetRarityCardPortrait(0).color);
            Assert.AreEqual(Color.black,station.Index.GetRarityCardPortrait(1).color);
            Assert.AreEqual("???",station.Index.GetRarityCardName(1).text);
            Assert.AreEqual(FilterMode.Point,station.Index.GetRarityCardFrame(0).sprite.texture.filterMode);
            station.Index.SelectTab(VaultCollectionIndex.IndexTab.InVault);
            Assert.AreEqual(VaultCollectionIndex.IndexTab.InVault,station.Index.ActiveTab);
            CollectionAssert.AreEqual(new[]{0,0,6},station.Index.GetDisplayedVaultUnitIds());
            Assert.AreEqual(3,station.Index.VisibleVaultCardsCount);
            station.Index.Close();
            Assert.IsFalse(station.Index.IsOpen);
            Transform mundo=GameObject.Find("Mundo3D_EchoScrap").transform;
            int floating=0;
            foreach(Transform cat in mundo)foreach(Transform prop in cat){
                var renderers=prop.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)continue;
                Bounds b=renderers[0].bounds;for(int i=1;i<renderers.Length;i++)b.Encapsulate(renderers[i].bounds);
                float h=ArenaSurface.Profile(b.center.x,b.center.z);
                if(b.min.y-h>0.35f)floating++;
            }
            Assert.AreEqual(0,floating);
        }
        [UnityTest] public IEnumerator TAtOccupiedEquipmentRemovesPowerAndKeepsCollectible()
        {
            station.ResetCollection();station.Progress.owned[0]=1;station.Progress.equipped[0]=0;station.GetComponent<CompanionSystem>().RefreshEquipment();Object.FindAnyObjectByType<WarehouseTravel>().PlaceInWarehouse(true);
            var p=ArenaSurface.OnGround(station.DisplaySlots[0].position+Vector3.back*3,1);game.Player.transform.position=p;game.Player.GetComponent<Rigidbody>().position=p;Physics.SyncTransforms();yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));yield return null;yield return null;Assert.IsTrue(station.GetComponent<CompanionSystem>().IsOpen);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            station.transform.Find("CompanionCanvas/EquipoBoveda/Panel/Liberar").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.IsFalse(station.GetComponent<CompanionSystem>().IsOpen);
            Assert.IsTrue(station.GetComponent<VaultReleaseInteraction>().IsOpen);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));yield return null;yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.T));yield return null;yield return null;
            Assert.AreEqual(-1,station.Progress.equipped[0]);Assert.AreEqual(1,station.Progress.owned[0]);Assert.AreEqual(0,game.Stats.GetUpgradeTier(UpgradeId.DASH_01));
        }
        [UnityTest] public IEnumerator IndexSeparatesSixRaritiesAndDisplaysTenIndividualUnits()
        {
            station.ResetCollection();
            foreach(int id in new[]{0,0,1,2,3,4,6,7,8,9}) Assert.IsTrue(station.Progress.TryCollect(id));
            station.Progress.equipped[0]=0;
            Object.FindAnyObjectByType<WarehouseTravel>().PlaceInWarehouse(true);
            yield return null;
            var index=station.Index;index.Open();
            var canvas=station.transform.Find("VaultIndexCanvas").GetComponent<Canvas>();
            var hud=canvas.transform.Find("BotonIndiceBoveda").GetComponent<RectTransform>();
            Assert.AreEqual(0,hud.anchorMin.x);Assert.Greater(hud.anchoredPosition.x,0);
            for(int rarity=0;rarity<6;rarity++)
            {
                index.SetRarityFilter(rarity);
                Assert.AreEqual(FusionCatalog.Rarities.Count(r=>(int)r==rarity),index.VisibleRarityCardsCount);
                for(int id=0;id<FusionCatalog.Count;id++)
                    Assert.AreEqual((int)FusionCatalog.Rarities[id]==rarity,index.GetRarityCardFrame(id).gameObject.activeSelf);
            }
            index.SetRarityFilter(1);
            yield return Capture("IndiceRarezas",true,false,canvas);
            index.SelectTab(VaultCollectionIndex.IndexTab.InVault);
            Assert.AreEqual(10,index.VisibleVaultCardsCount);
            Assert.AreEqual(2,index.GetDisplayedVaultUnitIds().Count(id=>id==0));
            var cards=canvas.GetComponentsInChildren<RectTransform>().Where(t=>t.name.StartsWith("CartaBoveda_")).ToArray();
            Assert.AreEqual(10,cards.Length);
            foreach(var card in cards) Assert.That(-card.anchoredPosition.y+card.rect.height,Is.LessThanOrEqualTo(766));
            yield return Capture("IndiceTuBoveda",true,false,canvas);
            index.Close();
        }
        private IEnumerator VaultCapture()
        {
            var camera=new GameObject("VaultAuditCamera").AddComponent<Camera>();camera.fieldOfView=52;
            camera.transform.position=new Vector3(219,48,-49);camera.transform.LookAt(new Vector3(180,2,3));
            var rt=new RenderTexture(1600,1200,24);camera.targetTexture=rt;yield return null;camera.Render();
            var old=RenderTexture.active;RenderTexture.active=rt;var texture=new Texture2D(1600,1200,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1600,1200),0,0);texture.Apply();Directory.CreateDirectory("Logs/Fusion");File.WriteAllBytes("Logs/Fusion/BovedaModular.png",texture.EncodeToPNG());RenderTexture.active=old;
            Object.Destroy(texture);Object.Destroy(rt);Object.Destroy(camera.gameObject);
        }
        private IEnumerator Capture(string name,bool showUi,bool garage=false,Canvas selectedCanvas=null)
        {
            var camera=new GameObject("FusionAuditCamera").AddComponent<Camera>();camera.fieldOfView=60;
            camera.transform.position=garage?new Vector3(180,7,-27):station.transform.position+new Vector3(9,8,-14);
            camera.transform.LookAt(garage?new Vector3(180,4,12):station.transform.position+Vector3.up*3);
            var rt=new RenderTexture(1400,900,24);camera.targetTexture=rt;
            Canvas canvas=selectedCanvas!=null?selectedCanvas:station.GetComponentInChildren<Canvas>();if(showUi){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
            yield return null;yield return null;camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;var texture=new Texture2D(1400,900,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1400,900),0,0);texture.Apply();Directory.CreateDirectory("Logs/Fusion");File.WriteAllBytes("Logs/Fusion/"+name+".png",texture.EncodeToPNG());RenderTexture.active=old;
            if(showUi){canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;}Object.Destroy(texture);Object.Destroy(rt);Object.Destroy(camera.gameObject);
        }
    }
}

