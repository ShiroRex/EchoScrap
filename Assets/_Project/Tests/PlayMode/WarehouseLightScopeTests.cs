using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EchoScrap.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class WarehouseLightScopeTests
    {
        private Scene tempScene;
        private Scene previousScene;
        private bool createdTempScene;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (createdTempScene)
            {
                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
                yield return SceneManager.UnloadSceneAsync(tempScene);
                createdTempScene = false;
            }
        }

        [UnityTest]
        public IEnumerator SetInteriorActive_RestoresOriginalEnabledStates_WithoutForcingInitiallyDisabledLightsOn()
        {
            previousScene = SceneManager.GetActiveScene();
            tempScene = SceneManager.CreateScene("WarehouseLightScopeUnit_" + Random.Range(1, 100000));
            createdTempScene = true;
            SceneManager.SetActiveScene(tempScene);
            yield return null;

            var root = new GameObject("VaultRoot");
            var scope = root.AddComponent<WarehouseLightScope>();

            var enabledGo = new GameObject("EnabledInteriorLight");
            var enabledLight = enabledGo.AddComponent<Light>();
            enabledLight.enabled = true;

            var disabledGo = new GameObject("DisabledInteriorLight");
            var disabledLight = disabledGo.AddComponent<Light>();
            disabledLight.enabled = false;

            var exteriorGo = new GameObject("ExteriorLight");
            var exteriorLight = exteriorGo.AddComponent<Light>();
            exteriorLight.enabled = true;

            scope.Configure(new[] { enabledLight, disabledLight });
            scope.SetInteriorActive(false);

            Assert.IsFalse(enabledLight.enabled);
            Assert.IsFalse(disabledLight.enabled);
            Assert.IsTrue(exteriorLight.enabled, "Exterior lights must never be touched by WarehouseLightScope.");

            scope.SetInteriorActive(true);
            Assert.IsTrue(enabledLight.enabled, "Interior light originally enabled must turn back on.");
            Assert.IsFalse(disabledLight.enabled, "Interior light originally disabled must stay off.");
            Assert.IsTrue(root.activeInHierarchy, "Vault root object must remain active.");
        }

        [UnityTest]
        public IEnumerator CombatArena_EnterExitRestoreAndReset_TogglesOnlyInteriorLights()
        {
            yield return SceneManager.LoadSceneAsync("CombatArena", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var travel = Object.FindAnyObjectByType<WarehouseTravel>();
            var bootstrap = Object.FindAnyObjectByType<CombatGameBootstrap>();
            var scope = Object.FindAnyObjectByType<WarehouseLightScope>(FindObjectsInactive.Include);
            var director = Object.FindAnyObjectByType<WarehouseCameraDirector>();

            Assert.IsNotNull(travel);
            Assert.IsNotNull(bootstrap);
            Assert.IsNotNull(scope);
            Assert.IsNotNull(director);
            Assert.AreEqual(35, scope.InteriorLights.Count, "Expected explicit 35 interior vault lights on CollectorVault.");

            var interiorSet = new HashSet<Light>();
            for (int i = 0; i < scope.InteriorLights.Count; i++)
            {
                Light l = scope.InteriorLights[i];
                Assert.IsNotNull(l, $"Interior light reference at index {i} is null.");
                interiorSet.Add(l);
            }

            // 1. Exterior startup: vault stays active, all interior lights disabled, exterior lights untouched.
            Assert.IsTrue(scope.gameObject.activeInHierarchy, "CollectorVault root GO must remain active outside.");
            for (int i = 0; i < scope.InteriorLights.Count; i++)
                Assert.IsFalse(scope.InteriorLights[i].enabled, $"Interior light {scope.InteriorLights[i].name} should start disabled outside.");

            int activeOutsideLights = CountEnabledLightsExcluding(null);
            int expectedExteriorLights = CountEnabledLightsExcluding(interiorSet);
            Assert.Greater(expectedExteriorLights, 0, "Expected exterior arena lights to be active outside.");
            Assert.AreEqual(expectedExteriorLights, activeOutsideLights, "Zero interior vault lights may remain enabled in exterior light counters.");

            // 2. Enter warehouse: interior lighting restores while ArenaLightingController may deactivate exterior rig lights.
            Assert.IsTrue(travel.PlaceInWarehouse(true));
            yield return null;
            Assert.IsTrue(scope.gameObject.activeInHierarchy);
            for (int i = 0; i < scope.InteriorLights.Count; i++)
                Assert.IsTrue(scope.InteriorLights[i].enabled, $"Interior light {scope.InteriorLights[i].name} should enable inside vault.");
            Assert.AreEqual(CountEnabledLightsExcluding(interiorSet) + interiorSet.Count, CountEnabledLightsExcluding(null));

            // 3. Exit warehouse: interior lights disable again and exterior lights return.
            Assert.IsTrue(travel.PlaceInWarehouse(false));
            yield return null;
            Assert.IsTrue(scope.gameObject.activeInHierarchy);
            for (int i = 0; i < scope.InteriorLights.Count; i++)
                Assert.IsFalse(scope.InteriorLights[i].enabled, $"Interior light {scope.InteriorLights[i].name} should disable upon exit.");
            Assert.AreEqual(expectedExteriorLights, CountEnabledLightsExcluding(null));

            // 4. Snapshot restore inside warehouse: interior lights re-enable cleanly.
            MethodInfo restoreMethod = typeof(WarehouseTravel).GetMethod("RestoreFromSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(restoreMethod);
            restoreMethod.Invoke(travel, new object[] { null, true, null, null });
            yield return null;
            for (int i = 0; i < scope.InteriorLights.Count; i++)
                Assert.IsTrue(scope.InteriorLights[i].enabled, $"Interior light {scope.InteriorLights[i].name} should restore enabled on inside snapshot load.");

            // 5. ResetView and SessionReset: interior lights turn off cleanly.
            director.ResetView();
            yield return null;
            for (int i = 0; i < scope.InteriorLights.Count; i++)
                Assert.IsFalse(scope.InteriorLights[i].enabled, "ResetView must disable interior vault lights.");

            director.SetWarehouse(true);
            yield return null;
            for (int i = 0; i < scope.InteriorLights.Count; i++)
                Assert.IsTrue(scope.InteriorLights[i].enabled);

            bootstrap.Session.ResetSession(20261022);
            yield return null;
            for (int i = 0; i < scope.InteriorLights.Count; i++)
                Assert.IsFalse(scope.InteriorLights[i].enabled, "SessionReset must disable interior vault lights.");
            Assert.IsTrue(scope.gameObject.activeInHierarchy);
        }

        private static int CountEnabledLightsExcluding(HashSet<Light> excluded)
        {
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].enabled && (excluded == null || !excluded.Contains(lights[i])))
                    count++;
            }
            return count;
        }
    }
}
