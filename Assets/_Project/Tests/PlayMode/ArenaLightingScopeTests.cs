using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class ArenaLightingScopeTests
    {
        private CombatGameBootstrap game;
        private void Loaded(Scene scene,LoadSceneMode mode)
        {var fusion=Object.FindAnyObjectByType<FusionStation>();if(fusion!=null)fusion.PersistProgress=false;}
        [UnitySetUp]public IEnumerator Setup()
        {
            SceneTransitionState.Reset();SceneManager.sceneLoaded+=Loaded;
            yield return SceneManager.LoadSceneAsync("CombatArena");yield return null;
            game=Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick=game.Encounter.AutoTick=false;
        }
        [UnityTearDown]public IEnumerator Cleanup()
        {
            SceneManager.sceneLoaded-=Loaded;
            if(game!=null){var scene=game.gameObject.scene;SceneManager.SetActiveScene(SceneManager.CreateScene("Lighting scope cleanup"));yield return SceneManager.UnloadSceneAsync(scene);}
            SceneTransitionState.Reset();CombatTargetRegistry.Clear();Time.timeScale=1;
        }
        [Test]public void ExteriorUsesSharedAuthoredRigAndDedicatedRendererWithoutChangingCamera()
        {
            Assert.IsNotNull(game.Lighting);Assert.IsTrue(ArenaLightingController.IsExteriorActive);
            Assert.IsNotNull(game.Lighting.Profile.PostProcessing);
            Assert.IsTrue(game.GameplayCamera.GetUniversalAdditionalCameraData().renderPostProcessing);
            Assert.IsTrue((game.GameplayCamera.GetUniversalAdditionalCameraData().volumeLayerMask.value&(1<<9))!=0);
            Assert.AreEqual(60f,game.CameraRig.Lens.FieldOfView,.01f);
            Assert.AreEqual(50f,game.CameraRig.transform.eulerAngles.x,.01f);
            Assert.AreEqual(0,game.Lighting.Rig.GetComponentsInChildren<Collider>(true).Length);
            Assert.GreaterOrEqual(game.Lighting.Rig.GetComponentsInChildren<Light>(true).Length,4);
            Assert.AreEqual(AmbientMode.Trilight,RenderSettings.ambientMode);
            Assert.AreEqual(CameraClearFlags.SolidColor,game.GameplayCamera.clearFlags);
            Assert.AreEqual(game.Lighting.Profile.SkyColor,game.GameplayCamera.backgroundColor);
        }
        [UnityTest]public IEnumerator WarehouseRestoresOriginalRenderingAndExteriorReturnsAfterRepeatedTravel()
        {
            var camera=game.GameplayCamera.GetUniversalAdditionalCameraData();
            var exterior=camera.scriptableRenderer;var travel=Object.FindAnyObjectByType<WarehouseTravel>();
            for(int i=0;i<3;i++)
            {
                Assert.IsTrue(travel.PlaceInWarehouse(true));yield return null;
                Assert.IsFalse(ArenaLightingController.IsExteriorActive);Assert.IsFalse(camera.renderPostProcessing);
                Assert.AreEqual(1,camera.volumeLayerMask.value);Assert.IsFalse(game.Lighting.Rig.gameObject.activeSelf);
                Assert.AreEqual(CameraClearFlags.Skybox,game.GameplayCamera.clearFlags);
                Assert.AreEqual(AmbientMode.Flat,RenderSettings.ambientMode);Assert.AreEqual(new Color(.48f,.53f,.6f),RenderSettings.ambientLight);
                Assert.IsTrue(travel.PlaceInWarehouse(false));yield return null;
                Assert.IsTrue(ArenaLightingController.IsExteriorActive);Assert.IsTrue(camera.renderPostProcessing);
                Assert.AreSame(exterior,camera.scriptableRenderer);Assert.IsTrue(game.Lighting.Rig.gameObject.activeSelf);
                Assert.AreEqual(game.Lighting.Profile.AmbientSky,RenderSettings.ambientSkyColor);
                Assert.AreEqual(CameraClearFlags.SolidColor,game.GameplayCamera.clearFlags);
            }
        }
#if UNITY_EDITOR
        [UnityTest]public IEnumerator TimedArenaVariantsUseTheSameProfileAndRig()
        {
            var sharedProfile=game.Lighting.Profile;
            foreach(string arena in new[]{"CombatArena_3Min","CombatArena_8Min"})
            {
                yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                    "Assets/_Project/Scenes/"+arena+".unity",new LoadSceneParameters(LoadSceneMode.Single));
                yield return null;
                game=Object.FindAnyObjectByType<CombatGameBootstrap>();
                game.Director.AutoTick=game.Encounter.AutoTick=false;
                Assert.IsNotNull(game.Lighting,arena);Assert.AreSame(sharedProfile,game.Lighting.Profile,arena);
                Assert.IsTrue(ArenaLightingController.IsExteriorActive,arena);
                Assert.AreEqual(1,Object.FindObjectsByType<ArenaLightingRig>(FindObjectsSortMode.None).Length,arena);
                Assert.AreEqual(60f,game.CameraRig.Lens.FieldOfView,.01f);
                Assert.AreEqual(50f,game.CameraRig.transform.eulerAngles.x,.01f);
            }
        }
#endif
        [Test]public void ReducedEffectsRemoveAirAndFocusButKeepPracticalLightingAndOrdinarySignals()
        {
            var lighting=game.Lighting;Assert.IsTrue(lighting.AtmosphereEnabled);Assert.IsTrue(lighting.FocusEnabled);
            game.Session.Effects.ReducedEffectsMode=true;
            Assert.IsFalse(lighting.AtmosphereEnabled);Assert.IsFalse(lighting.FocusEnabled);
            Assert.IsTrue(lighting.Rig.gameObject.activeSelf);
            foreach(var light in lighting.Rig.GetComponentsInChildren<Light>())Assert.IsTrue(light.enabled);
            game.Session.Effects.ReducedEffectsMode=false;
            lighting.SetAtmosphereEnabled(false);Assert.IsFalse(lighting.AtmosphereEnabled);Assert.IsTrue(lighting.FocusEnabled);
            lighting.SetAtmosphereEnabled(true);lighting.SetLightingEnabled(false);
            Assert.IsFalse(ArenaLightingController.IsExteriorActive);Assert.IsFalse(lighting.Rig.gameObject.activeSelf);
            Assert.IsFalse(game.GameplayCamera.GetUniversalAdditionalCameraData().renderPostProcessing);
            lighting.SetLightingEnabled(true);Assert.IsTrue(lighting.AtmosphereEnabled);
        }
    }
}
