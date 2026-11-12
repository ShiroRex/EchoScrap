using System.Collections;
using System.IO;
using System.Linq;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
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
            var feedback=station.GetComponentInChildren<CollectiblePickupFeedback>();
            game.Hud.SetStatsDisplayMode(StatsDisplayMode.Full);
            Assert.IsTrue(game.Hud.IsStatsVisible);
            Assert.AreEqual(0,feedback.SoundsPlayed);Assert.IsFalse(system.TryCollectNearest());Assert.AreEqual(0,feedback.NotificationsPlayed);
            Assert.IsTrue(system.TrySpawn(2));var pickup=Object.FindObjectsByType<CollectibleDisplay>(FindObjectsSortMode.None).Single(v=>v.name=="ColeccionableArena_Demonio");
            int before=station.Progress.owned[2];game.Player.GetComponent<Rigidbody>().position=pickup.transform.position+Vector3.up;
            game.Player.transform.position=pickup.transform.position+Vector3.up;Physics.SyncTransforms();system.Advance(.1f);Assert.AreEqual(before,station.Progress.owned[2]);Assert.IsTrue(system.TryCollectNearest());Assert.AreEqual(before+1,station.Progress.owned[2]);
            system.Advance(.1f);Assert.AreEqual(before+1,station.Progress.owned[2]);Assert.AreEqual(0,system.ActivePickups);
            Assert.AreEqual(1,feedback.NotificationsPlayed);Assert.AreEqual(1,feedback.SoundsPlayed);Assert.AreEqual(2,feedback.DisplayedId);
            Assert.IsFalse(game.Hud.IsStatsVisible);
            Assert.IsFalse(system.TryCollectNearest());Assert.AreEqual(1,feedback.SoundsPlayed);
            yield return new WaitForSeconds(.15f);Assert.IsTrue(feedback.IsVisible);
            var notification = feedback.transform.Find("ColeccionableRecogido");
            Assert.IsTrue(notification.Find("Nuevo").gameObject.activeSelf);
            Assert.IsFalse(notification.Find("Confirmacion").gameObject.activeSelf);
            game.Session.SetManualPause(true);Assert.IsFalse(feedback.IsVisible);yield return null;
            Assert.IsFalse(feedback.GetComponent<AudioSource>().isPlaying);
            game.Session.SetManualPause(false);yield return null;Assert.IsTrue(feedback.IsVisible);
            Assert.IsFalse(game.Hud.IsStatsVisible);
            yield return Capture("ColeccionableRecogido");
            yield return new WaitForSeconds(3.6f);
            Assert.IsTrue(notification.Find("Confirmacion").gameObject.activeSelf);
            Assert.IsFalse(notification.Find("Nuevo").gameObject.activeSelf);
            yield return new WaitForSeconds(2);Assert.IsFalse(feedback.IsVisible);
            Assert.IsTrue(game.Hud.IsStatsVisible);
            Assert.AreEqual(StatsDisplayMode.Full, game.Hud.StatsMode);
            feedback.Show(0);feedback.Show(2);game.ResetRun(false);yield return null;
            Assert.IsFalse(feedback.IsVisible);Assert.AreEqual(-1,feedback.DisplayedId);
        }
        [UnityTest] public IEnumerator PickupFeedbackQueuesCopiesAndClearsOnVaultTravel()
        {
            var feedback=station.GetComponentInChildren<CollectiblePickupFeedback>();
            var audio=feedback.GetComponent<AudioSource>();Assert.IsNotNull(audio.clip);Assert.That(audio.clip.length,Is.InRange(.5f,1f));
            var random=Random.state;Random.InitState(734);var expected=Random.value;Random.InitState(734);
            feedback.Show(0);feedback.Show(0);Assert.AreEqual(expected,Random.value);Random.state=random;
            Assert.AreEqual(1,feedback.NotificationsPlayed);Assert.AreEqual(2,feedback.SoundsPlayed);
            Assert.IsFalse(feedback.transform.Find("ColeccionableRecogido/Nuevo").gameObject.activeSelf);
            yield return new WaitForSeconds(5.6f);Assert.AreEqual(2,feedback.NotificationsPlayed);Assert.IsTrue(feedback.IsVisible);
            game.Session.SetWarehouseVisit(true);yield return null;Assert.IsFalse(feedback.IsVisible);Assert.IsFalse(audio.isPlaying);
            game.Session.SetWarehouseVisit(false);yield return null;Assert.IsFalse(feedback.IsVisible);
        }
        [UnityTest] public IEnumerator FullInventoryRejectsPickupWithoutLosingItAndAllowsAfterRelease()
        {
            station.ResetCollection();station.Progress.owned[0]=10;station.Progress.equipped=new[]{0,0,0};system.RefreshEquipment();
            var feedback=station.GetComponentInChildren<CollectiblePickupFeedback>();
            Assert.IsTrue(system.TrySpawn(2));var position=system.PickupPosition(0)+Vector3.up;
            game.Player.transform.position=position;game.Player.GetComponent<Rigidbody>().position=position;Physics.SyncTransforms();
            Assert.IsFalse(system.TryCollectNearest());Assert.AreEqual(10,station.Progress.OwnedCount);Assert.AreEqual(1,system.ActivePickups);
            Assert.AreEqual(1,feedback.RejectionsPlayed);Assert.AreEqual(0,feedback.SoundsPlayed);Assert.IsTrue(feedback.IsRejectionVisible);
            Assert.IsFalse(system.TryCollectNearest());Assert.AreEqual(1,feedback.RejectionsPlayed,"Repeated E does not spam rejection audio.");
            var audio=feedback.GetComponents<AudioSource>().Single(source=>source.clip!=null&&source.clip.name=="InventoryRejected");Assert.Greater(audio.clip.length,0);Assert.IsTrue(audio.isPlaying);
            yield return Capture("InventarioLleno");game.Session.SetManualPause(true);yield return null;Assert.IsFalse(feedback.IsRejectionVisible);Assert.IsFalse(audio.isPlaying);
            game.Session.SetManualPause(false);yield return null;Assert.IsTrue(feedback.IsRejectionVisible);
            Assert.IsTrue(station.ReleaseCollectible(0));Assert.IsTrue(system.TryCollectNearest());Assert.AreEqual(10,station.Progress.OwnedCount);Assert.AreEqual(1,station.Progress.owned[2]);Assert.AreEqual(0,system.ActivePickups);Assert.IsFalse(feedback.IsRejectionVisible);
            game.ResetRun(false);yield return null;Assert.IsFalse(feedback.IsRejectionVisible);
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
            Assert.AreSame(PixelControlArtwork.Load("Presentation/EquippedSlot"),hud.Find("Retrato_0").GetComponent<Image>().sprite);Assert.IsNotNull(hud.Find("Retrato_0/Cara").GetComponent<Image>().sprite);
            StringAssert.Contains(FusionCatalog.Names[3],hud.Find("PoderesActivos").GetComponent<Text>().text);
            StringAssert.Contains(CompanionPowerRuntime.HudDescriptions[3],hud.Find("PoderesActivos").GetComponent<Text>().text);
            yield return Capture("Retratos");game.Session.SetManualPause(true);yield return null;
            Assert.IsTrue(hud.gameObject.activeInHierarchy);
            Assert.IsFalse(hud.Find("PoderesActivos").gameObject.activeSelf);
        }
        [UnityTest] public IEnumerator EquipmentRoundTripsAndMalformedSaveIsRepaired(){
            var progress=new FusionProgress();for(int id=0;id<4;id++)progress.owned[id]=1;progress.equipped=new[]{0,1,2};var copy=JsonUtility.FromJson<FusionProgress>(JsonUtility.ToJson(progress));copy.Validate();CollectionAssert.AreEqual(progress.equipped,copy.equipped);
            copy.equipped=new[]{4,0,0};copy.Validate();CollectionAssert.AreEqual(new[]{-1,0,-1},copy.equipped);
            copy.equipped=null;copy.Validate();CollectionAssert.AreEqual(new[]{-1,-1,-1},copy.equipped);yield return null;
        }
        [UnityTest] public IEnumerator PickupsAreLabeledFloatExpireAndShowOnMinimap()
        {
            Assert.IsTrue(system.TrySpawn(5));
            Assert.IsTrue(system.TrySpawn(7));
            var displays = Object.FindObjectsByType<CollectibleDisplay>(FindObjectsSortMode.None);
            var display = displays.Single(v => v.name == "ColeccionableArena_Basket");
            var legendary = displays.Single(v => v.name == "ColeccionableArena_Lisa");
            var commonGlow = display.GetComponent<CombatGlowSource>();
            var legendaryGlow = legendary.GetComponent<CombatGlowSource>();
            Assert.AreEqual(FusionCatalog.ColorOf(5), commonGlow.Color);
            Assert.AreEqual(FusionCatalog.ColorOf(7), legendaryGlow.Color);
            Assert.Greater(legendaryGlow.Intensity, commonGlow.Intensity);
            Assert.Greater(legendaryGlow.Range, commonGlow.Range);

            var camera = game.GameplayCamera;
            Vector3 originalCameraPosition = camera.transform.position;
            Quaternion originalCameraRotation = camera.transform.rotation;
            camera.transform.SetPositionAndRotation(legendary.transform.position - Vector3.forward * 8f, Quaternion.identity);
            var lightPool = game.Session.GetComponent<CombatReadabilityLights>();
            lightPool.RefreshLights();
            Assert.IsTrue(lightPool.GetComponentsInChildren<Light>().Any(light =>
                light.enabled && light.color == legendaryGlow.Color && Mathf.Approximately(light.intensity, legendaryGlow.Intensity)));
            camera.transform.SetPositionAndRotation(originalCameraPosition, originalCameraRotation);

            var timer = display.transform.Find("NombreRareza/TiempoRestante").GetComponent<Text>();
            Assert.AreEqual("10 s", timer.text);
            Vector3 original = display.transform.position;
            system.Advance(1.1f);
            yield return null;
            Assert.AreNotEqual(original.y, display.transform.position.y);
            var texts = display.GetComponentsInChildren<Text>().Select(t => t.text).ToArray();
            CollectionAssert.AreEquivalent(new[] { "Basket", "COMÚN", "9 s" }, texts);

            var map = game.Hud.GetComponent<CombatMinimap>();
            map.Refresh();
            var marker = game.Hud.HudCanvas.transform.Find("Minimap/Interior/MapaArena/Coleccionable_0");
            Assert.IsTrue(marker.gameObject.activeSelf);
            Assert.AreEqual(Vector2.one * 36f, ((RectTransform)marker).sizeDelta);
            Assert.Greater(marker.GetSiblingIndex(), map.PlayerMarker.GetSiblingIndex());
            Assert.IsNotNull(marker.GetComponent<Outline>());
            Assert.IsNotNull(marker.GetComponent<Image>().sprite);
            game.Session.SetManualPause(true);
            system.Advance(CompanionSystem.PickupLifetime);
            Assert.AreEqual(2, system.ActivePickups);
            Assert.AreEqual("9 s", timer.text);
            game.Session.SetManualPause(false);

            system.Advance(9f);
            yield return null;
            map.Refresh();
            Assert.AreEqual(0, system.ActivePickups);
            Assert.IsTrue(commonGlow == null && legendaryGlow == null);
            Assert.IsFalse(marker.gameObject.activeSelf);
            Assert.IsFalse(system.TryCollectNearest());
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
            var powers=hud.Find("PoderesActivos").GetComponent<Text>();
            Assert.AreSame(Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro").AnnouncementFont,powers.font);
            Assert.AreEqual(Color.clear,hud.GetComponent<Image>().color);
            Assert.Less(powers.rectTransform.anchoredPosition.y+powers.rectTransform.sizeDelta.y*.5f,-30);
            for(int slot=0;slot<3;slot++){var frame=hud.Find("Retrato_"+slot);var art=frame.Find("Cara").GetComponent<Image>();Assert.AreSame(CollectibleDisplay.CardPortrait(ids[slot]),art.sprite);Assert.IsTrue(art.enabled);Assert.AreEqual(Vector2.one*48,art.rectTransform.sizeDelta);Assert.AreSame(PixelControlArtwork.Load("Presentation/EquippedSlot"),frame.GetComponent<Image>().sprite);}
            yield return Capture("MarcosCombate");

            bool previousPowers = SceneTransitionState.ShowCompanionPowers;
            var background = InputSystem.settings.backgroundBehavior;
            var editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            try
            {
                SceneTransitionState.ShowCompanionPowers = false;
                game.Hud.SetStatsVisible(false);
                game.Stats.SetUpgradeTier("FLASH_01", 1);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q));
                yield return null;
                yield return null;
                Assert.IsTrue(keyboard.qKey.isPressed, "The virtual keyboard must deliver Q to the Game view.");
                Assert.IsTrue(powers.gameObject.activeInHierarchy);
                Assert.IsTrue(game.Hud.IsStatsVisible);
                Assert.IsFalse(game.Hud.ShowStats);
                Canvas.ForceUpdateCanvases();
                var statsCorners = new Vector3[4];
                var powerCorners = new Vector3[4];
                ((RectTransform)game.Hud.StatsReadout.transform).GetWorldCorners(statsCorners);
                powers.rectTransform.GetWorldCorners(powerCorners);
                Assert.That(statsCorners[1].y, Is.LessThan(powerCorners[0].y));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                yield return null;
                Assert.IsFalse(powers.gameObject.activeInHierarchy);
                Assert.IsFalse(game.Hud.IsStatsVisible);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior = background;
                InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
                SceneTransitionState.ShowCompanionPowers = previousPowers;
            }
        }
        [UnityTest] public IEnumerator ReleaseOnlyAtOccupiedGalleryAndEquipmentOnlyListsFreeCopies()
        {
            station.ResetCollection();station.Progress.owned[0]=2;station.Progress.owned[6]=1;station.RefreshDisplays();
            var release=station.GetComponent<VaultReleaseInteraction>();Approach(0);Assert.IsFalse(release.TryOpen());Assert.IsTrue(system.TryOpen());
            var grid=station.GetComponentInChildren<CollectibleUnitGrid>();Assert.AreEqual(3,grid.VisibleUnits);Assert.AreEqual(0,grid.UnitId(0));Assert.AreEqual(0,grid.UnitId(1));Assert.IsFalse(grid.GetComponentsInChildren<Button>(true).Any(b=>b.name=="LIBERAR"));
            Assert.IsTrue(system.Equip(0,0));grid.Refresh(true);Assert.AreEqual(2,grid.VisibleUnits);system.Close();Approach(1);Assert.IsTrue(system.TryOpen());Assert.IsTrue(system.Equip(1,0));grid.Refresh(true);Assert.AreEqual(1,grid.VisibleUnits);Assert.AreEqual(6,grid.UnitId(0));system.Close();
            Approach(0);Assert.IsTrue(release.TryOpen());release.Close();Approach(3);Assert.AreEqual(3,release.NearestSlot);Assert.IsFalse(system.TryOpen());yield return null;
            var hint = station.transform.Find("LiberarBovedaCanvas/IndicadorLiberar");
            Assert.IsTrue(hint.gameObject.activeInHierarchy);
            Assert.AreEqual("LIBERAR", hint.Find("Texto").GetComponent<Text>().text);
            Assert.That(((RectTransform)hint).sizeDelta.x, Is.LessThanOrEqualTo(170f));
            yield return Capture("LiberarEnGaleria",true);
            Assert.IsTrue(release.TryOpen());Assert.IsTrue(game.Session.IsPaused);
            string rarityColor = ColorUtility.ToHtmlStringRGB(FusionCatalog.ColorOf(6));
            StringAssert.Contains($"<color=#{rarityColor}>Hada</color>?",release.ConfirmationText);
            yield return Capture("ConfirmarLiberacionGaleria",true);
            release.Close();Assert.AreEqual(1,station.Progress.owned[6]);Assert.IsTrue(release.TryOpen());Assert.IsTrue(release.ConfirmRelease());Assert.AreEqual(0,station.Progress.owned[6]);Assert.IsFalse(release.ConfirmRelease());Assert.IsFalse(release.TryOpen());CollectionAssert.AreEqual(new[]{0,0,-1},station.Progress.equipped);
        }
        [UnityTest] public IEnumerator OccupiedEquipmentSupportsUnequipWithoutLossAndConfirmedRelease()
        {
            station.ResetCollection();station.Progress.owned[0]=1;Approach();Assert.IsTrue(system.TryOpen());Assert.IsTrue(system.Equip(0,0));system.Close();var release=station.GetComponent<VaultReleaseInteraction>();
            Assert.IsTrue(system.TryOpen());Assert.AreEqual(0,system.SelectedSlot);system.Close();Assert.IsTrue(release.TryUnequip());Assert.AreEqual(1,station.Progress.owned[0]);Assert.AreEqual(-1,station.Progress.equipped[0]);Assert.AreEqual(0,game.Stats.GetUpgradeTier(UpgradeId.DASH_01));
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

