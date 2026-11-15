using System;
using System.Collections;
using System.Linq;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EchoScrap.Tests
{
    public sealed class CompanionViewOptimizationTests
    {
        private CombatGameBootstrap game;
        private FusionStation station;
        private CompanionSystem system;
        private CompanionView view;
        private WarehouseTravel travel;

        private void Loaded(Scene scene, LoadSceneMode mode)
        {
            var s = Object.FindAnyObjectByType<FusionStation>();
            if (s != null) s.PersistProgress = false;
        }

        [UnitySetUp]
        public IEnumerator Setup()
        {
            SceneManager.sceneLoaded += Loaded;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            SceneManager.sceneLoaded -= Loaded;

            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            station = Object.FindAnyObjectByType<FusionStation>();
            system = station.GetComponent<CompanionSystem>();
            view = station.GetComponent<CompanionView>();
            travel = Object.FindAnyObjectByType<WarehouseTravel>();

            system.AutoTick = false;
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            game.ResetRun(false);
            game.Weapon.enabled = false;
            game.Session.ResumeAllUserPauses();
            station.ResetCollection();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            SceneManager.sceneLoaded -= Loaded;
            system?.Close();
            Time.timeScale = 1f;
            if (game != null)
            {
                var scene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("CompanionOptCleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            CombatTargetRegistry.Clear();
        }

        private void ApproachSlot(int slot)
        {
            travel.PlaceInWarehouse(true);
            Vector3 pos = station.DisplaySlots[slot].position + Vector3.back * 3f;
            pos = ArenaSurface.OnGround(pos, 1f);
            game.Player.transform.position = pos;
            game.Player.GetComponent<Rigidbody>().position = pos;
            Physics.SyncTransforms();
        }

        [UnityTest]
        public IEnumerator Refresh_ThirtySecondSteadyState_DoesNotAllocateRepeatedly()
        {
            Assert.IsTrue(station.CollectFromArena(0));
            ApproachSlot(0);
            Assert.IsTrue(system.TryOpen());
            Assert.IsTrue(system.Equip(0, 0));
            system.Close();
            travel.PlaceInWarehouse(false);
            yield return null;

            // Warm one pass, then simulate 30 seconds at 60 FPS (1800 Refresh calls) without equipment changes.
            view.Refresh();
            long beforeOutside = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1800; i++)
                view.Refresh();
            long allocatedOutside = GC.GetAllocatedBytesForCurrentThread() - beforeOutside;
            Assert.AreEqual(0L, allocatedOutside, "CompanionView.Refresh outside steady-state allocated managed memory.");

            // Also verify zero repeated allocations when modal is open without inventory or selection changes.
            ApproachSlot(0);
            Assert.IsTrue(system.TryOpen());
            yield return null;
            view.Refresh();
            long beforeOpen = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1800; i++)
                view.Refresh();
            long allocatedOpen = GC.GetAllocatedBytesForCurrentThread() - beforeOpen;
            Assert.AreEqual(0L, allocatedOpen, "CompanionView.Refresh open-modal steady-state allocated managed memory.");
        }

        [UnityTest]
        public IEnumerator EquipUnequipCollectAndSwitchSlot_UpdateUiImmediately()
        {
            Assert.IsTrue(station.CollectFromArena(0));
            Assert.IsTrue(station.CollectFromArena(1));

            ApproachSlot(0);
            Assert.IsTrue(system.TryOpen());
            Transform panel = station.transform.Find("CompanionCanvas/EquipoBoveda/Panel");
            CollectibleUnitGrid grid = panel.GetComponentInChildren<CollectibleUnitGrid>();
            Assert.AreEqual(2, grid.VisibleUnits, "Opening companion panel must refresh grid immediately.");

            Text slot0Text = panel.Find("Puesto_0/Seleccion").GetComponent<Text>();
            Button removeBtn = panel.Find("Quitar").GetComponent<Button>();
            Button releaseBtn = panel.Find("Liberar").GetComponent<Button>();
            StringAssert.Contains("VACÍO", slot0Text.text);
            Assert.IsFalse(removeBtn.interactable);
            Assert.IsFalse(releaseBtn.interactable);

            // Equip immediately updates slot text, portrait, buttons, HUD powers, and available grid copies.
            Assert.IsTrue(system.Equip(0, 0));
            StringAssert.Contains(FusionCatalog.Names[0], slot0Text.text);
            Assert.IsTrue(removeBtn.interactable);
            Assert.IsTrue(releaseBtn.interactable);
            Assert.AreEqual(1, grid.VisibleUnits, "Equipping a copy must update unitGrid immediately.");

            Transform hud = station.transform.Find("CompanionCanvas/CompanerosEquipados");
            Image portrait0 = hud.Find("Retrato_0/Cara").GetComponent<Image>();
            Assert.IsTrue(portrait0.enabled);
            Assert.AreSame(CollectibleDisplay.CardPortrait(0), portrait0.sprite);
            StringAssert.Contains(FusionCatalog.Names[0], hud.Find("PoderesActivos").GetComponent<Text>().text);

            // Unequip immediately restores VACÍO and updates grid back to 2 available units.
            Assert.IsTrue(system.Equip(0, -1));
            StringAssert.Contains("VACÍO", slot0Text.text);
            Assert.IsFalse(portrait0.enabled);
            Assert.IsFalse(removeBtn.interactable);
            Assert.AreEqual(2, grid.VisibleUnits, "Unequipping must update unitGrid immediately.");

            // Switch to slot 1: Puesto_1 updates and becomes active immediately.
            system.Close();
            ApproachSlot(1);
            Assert.IsTrue(system.TryOpen());
            Assert.AreEqual("PUESTO DE EQUIPO 2", panel.Find("Titulo").GetComponent<Text>().text);
            Assert.IsFalse(panel.Find("Puesto_0").gameObject.activeSelf);
            Assert.IsTrue(panel.Find("Puesto_1").gameObject.activeSelf);
            StringAssert.Contains("PUESTO 2", panel.Find("Puesto_1/Seleccion").GetComponent<Text>().text);
            system.Close();

            // Pickup outside: explicit CombatPickup scope vs InteriorDisplay scope.
            travel.PlaceInWarehouse(false);
            yield return null;
            Assert.IsTrue(system.TrySpawn(2));
            CollectibleDisplay pickup = Object.FindObjectsByType<CollectibleDisplay>(FindObjectsSortMode.None)
                .Single(d => d.name == "ColeccionableArena_" + FusionCatalog.Names[2]);
            Assert.AreEqual(CollectibleDisplayScope.CombatPickup, pickup.Scope);

            CollectibleDisplay interiorShelf = station.DisplaySlots[3].GetComponentInChildren<CollectibleDisplay>();
            Assert.AreEqual(CollectibleDisplayScope.InteriorDisplay, interiorShelf.Scope);

            Vector3 pickupWorld = pickup.transform.position + Vector3.up;
            game.Player.transform.position = pickupWorld;
            game.Player.GetComponent<Rigidbody>().position = pickupWorld;
            Physics.SyncTransforms();
            Assert.IsTrue(system.TryCollectNearest());
            Assert.AreEqual(1, station.Progress.owned[2]);
        }

        [UnityTest]
        public IEnumerator CollectibleDisplay_PausesAnimationWithoutAccumulatingTime_AndHidesLabelsOutsideScopeOrPaused()
        {
            Assert.IsTrue(station.CollectFromArena(0));
            travel.PlaceInWarehouse(true);
            ApproachSlot(0);
            Assert.IsTrue(system.TryOpen());
            Assert.IsTrue(system.Equip(0, 0));
            system.Close();
            yield return null;

            CollectibleDisplay interiorDisplay = station.DisplaySlots[0].GetComponentInChildren<CollectibleDisplay>();
            Assert.AreEqual(CollectibleDisplayScope.InteriorDisplay, interiorDisplay.Scope);

            // Exit warehouse: interior display is now out of active scope -> labels hide and animation freezes.
            travel.PlaceInWarehouse(false);
            yield return null;
            Transform interiorLabel = interiorDisplay.transform.Find("NombreRareza");
            Transform equipmentLabel = interiorDisplay.transform.Find("PuestoEquipo");
            Assert.IsNotNull(interiorLabel);
            Assert.IsNotNull(equipmentLabel);
            Assert.IsFalse(interiorLabel.gameObject.activeSelf, "Interior display label must hide when outside vault.");
            Assert.IsFalse(equipmentLabel.gameObject.activeSelf, "Equipment label must hide when outside vault.");

            float frozenInteriorTime = interiorDisplay.AnimationElapsed;
            Quaternion frozenInteriorRotation = interiorDisplay.GetComponentInChildren<SpriteRenderer>().transform.rotation;
            yield return null;
            yield return null;
            Assert.AreEqual(frozenInteriorTime, interiorDisplay.AnimationElapsed, 0.0001f, "Interior display must not advance animation when outside vault.");
            Assert.AreEqual(frozenInteriorRotation, interiorDisplay.GetComponentInChildren<SpriteRenderer>().transform.rotation);

            // Spawn combat pickup outside: animates when active, freezes without accumulating pause time on pause & results.
            Assert.IsTrue(system.TrySpawn(1));
            CollectibleDisplay pickup = Object.FindObjectsByType<CollectibleDisplay>(FindObjectsSortMode.None)
                .Single(d => d.name == "ColeccionableArena_" + FusionCatalog.Names[1]);
            yield return null;
            Transform pickupLabel = pickup.transform.Find("NombreRareza");
            Assert.IsNotNull(pickupLabel);
            Assert.IsTrue(pickupLabel.gameObject.activeSelf);

            yield return null;
            float beforePause = pickup.AnimationElapsed;
            Assert.Greater(beforePause, 0f, "Active combat pickup should animate while playing.");

            game.Session.SetManualPause(true);
            yield return null;
            Assert.IsFalse(pickupLabel.gameObject.activeSelf, "Pickup label must hide before early exit while paused.");
            float afterOnePausedFrame = pickup.AnimationElapsed;
            Assert.AreEqual(beforePause, afterOnePausedFrame, 0.0001f);

            yield return new WaitForSecondsRealtime(0.25f);
            Assert.AreEqual(beforePause, pickup.AnimationElapsed, 0.0001f, "Paused interval must not accumulate into pickup animation elapsed.");

            game.Session.SetManualPause(false);
            yield return null;
            Assert.IsTrue(pickupLabel.gameObject.activeSelf);
            Assert.Less(pickup.AnimationElapsed - beforePause, 0.12f, "Resuming after pause must continue from saved animation time without pause jump.");

            // Results screen hides label and freezes animation as well.
            game.Session.QueueBossDeath(default);
            game.Session.ResolveSimulationStepOutcome();
            yield return null;
            Assert.AreEqual(SessionState.Victory, game.Session.State);
            Assert.IsFalse(pickupLabel.gameObject.activeSelf, "Results state must hide display labels.");
            float frozenAtResults = pickup.AnimationElapsed;
            yield return null;
            Assert.AreEqual(frozenAtResults, pickup.AnimationElapsed, 0.0001f);
        }
    }
}
