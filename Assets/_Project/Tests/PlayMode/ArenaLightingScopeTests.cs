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
        private void AssertExteriorLens()
        {
            var profile=game.Lighting.Profile;
            Assert.AreEqual(profile.CameraFieldOfView,game.CameraRig.Lens.FieldOfView,.01f);
            Assert.AreEqual(profile.CameraPitch,game.CameraRig.transform.eulerAngles.x,.01f);
            var offset=game.CameraRig.GetComponent<Unity.Cinemachine.CinemachineFollow>().FollowOffset;
            Assert.AreEqual(profile.CameraDistance,offset.magnitude,.01f);
            Assert.Less(Vector3.Angle(offset,game.CameraRig.transform.rotation*Vector3.back),.01f);
        }
        [Test]public void ExteriorUsesSharedAuthoredRigDedicatedRendererAndMiniatureLens()
        {
            Assert.IsNotNull(game.Lighting);Assert.IsTrue(ArenaLightingController.IsExteriorActive);
            Assert.IsNotNull(game.Lighting.Profile.PostProcessing);
            Assert.IsTrue(game.GameplayCamera.GetUniversalAdditionalCameraData().renderPostProcessing);
            Assert.IsTrue((game.GameplayCamera.GetUniversalAdditionalCameraData().volumeLayerMask.value&(1<<9))!=0);
            AssertExteriorLens();
            var collector=game.Motor.GetComponent<CollectorSpriteView>();
            Assert.AreNotEqual(0,collector.CharacterRenderer.renderingLayerMask&ArenaLightingRig.CollectorVisibilityRenderingLayer);
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
                Assert.AreEqual(65f,game.CameraRig.Lens.FieldOfView,.01f);
                Assert.AreEqual(32f,game.CameraRig.transform.eulerAngles.x,.01f);
                Assert.IsTrue(travel.PlaceInWarehouse(false));yield return null;
                Assert.IsTrue(ArenaLightingController.IsExteriorActive);Assert.IsTrue(camera.renderPostProcessing);
                Assert.AreSame(exterior,camera.scriptableRenderer);Assert.IsTrue(game.Lighting.Rig.gameObject.activeSelf);
                Assert.AreEqual(game.Lighting.Profile.AmbientSky,RenderSettings.ambientSkyColor);
                Assert.AreEqual(CameraClearFlags.SolidColor,game.GameplayCamera.clearFlags);
                AssertExteriorLens();
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
                AssertExteriorLens();
            }
        }
#endif
        [Test]public void ReducedEffectsRemoveAirAndFocusButKeepPracticalLightingAndOrdinarySignals()
        {
            var lighting=game.Lighting;Assert.IsTrue(lighting.AtmosphereEnabled);Assert.IsTrue(lighting.FocusEnabled);
            game.Session.Effects.ReducedEffectsMode=true;
            Assert.IsFalse(lighting.AtmosphereEnabled);Assert.IsFalse(lighting.FocusEnabled);
            AssertExteriorLens();
            Assert.IsTrue(lighting.Rig.gameObject.activeSelf);
            foreach(var light in lighting.Rig.GetComponentsInChildren<Light>())Assert.IsTrue(light.enabled);
            foreach(var motes in lighting.Rig.Dust)Assert.IsFalse(motes.gameObject.activeInHierarchy);
            game.Session.Effects.ReducedEffectsMode=false;
            foreach(var motes in lighting.Rig.Dust)Assert.IsTrue(motes.gameObject.activeInHierarchy);
            lighting.SetAtmosphereEnabled(false);Assert.IsFalse(lighting.AtmosphereEnabled);Assert.IsTrue(lighting.FocusEnabled);
            lighting.SetAtmosphereEnabled(true);lighting.SetLightingEnabled(false);
            Assert.IsFalse(ArenaLightingController.IsExteriorActive);Assert.IsFalse(lighting.Rig.gameObject.activeSelf);
            Assert.IsFalse(game.GameplayCamera.GetUniversalAdditionalCameraData().renderPostProcessing);
            lighting.SetLightingEnabled(true);Assert.IsTrue(lighting.AtmosphereEnabled);
        }
        [UnityTest]public IEnumerator LensRestoresOnDisableAndSurvivesRunReset()
        {
            var lighting=game.Lighting;
            lighting.enabled=false;yield return null;
            Assert.AreEqual(60f,game.CameraRig.Lens.FieldOfView,.01f);
            Assert.AreEqual(50f,game.CameraRig.transform.eulerAngles.x,.01f);
            lighting.enabled=true;yield return null;AssertExteriorLens();
            var travel=Object.FindAnyObjectByType<WarehouseTravel>();
            Assert.IsTrue(travel.PlaceInWarehouse(true));yield return null;
            game.ResetRun();yield return null;
            AssertExteriorLens();
            Assert.IsTrue(lighting.FocusEnabled);
        }
        [Test]public void VisibleShaftsHaveRealElevatedSourcesAndMotesDoNotMultiplyTwoNearZeroAlphas()
        {
            var rig=game.Lighting.Rig;
            Assert.That(rig.ShaftLights.Length,Is.GreaterThanOrEqualTo(4));
            foreach(var source in rig.ShaftLights)
            {
                Assert.AreEqual(LightType.Spot,source.type);
                Assert.That(source.transform.forward.y,Is.LessThan(-.5f));
                Assert.That(source.transform.localPosition.z,Is.GreaterThan(0),"The light must sit ahead of the projector lens.");
                Assert.That(source.range,Is.GreaterThan(10));
            }
            Assert.IsNotNull(game.Lighting.ShaftLightAt(0));
            foreach(var motes in rig.Dust)
            {
                var material=motes.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                Assert.AreEqual("EchoScrap/Arena Dust",material.shader.name);
                float combinedAlpha=motes.main.startColor.color.a*material.GetColor("_BaseColor").a;
                Assert.That(combinedAlpha,Is.GreaterThan(.2f),"Double attenuation previously made the dust invisible.");
                Assert.IsTrue(motes.main.prewarm,"Motes should be present at arrival, not only after waiting several seconds.");
            }
        }
    }
}
