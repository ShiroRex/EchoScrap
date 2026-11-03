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
                Assert.AreEqual(CameraClearFlags.SolidColor,game.GameplayCamera.clearFlags);
                Assert.AreEqual(game.Lighting.Profile.WarehouseBackground,game.GameplayCamera.backgroundColor);
                Assert.AreEqual(AmbientMode.Trilight,RenderSettings.ambientMode);
                Assert.AreEqual(game.Lighting.Profile.WarehouseAmbientSky,RenderSettings.ambientSkyColor);
                Assert.IsTrue(ArenaLightingController.IsWorldLightingActive);
                foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if(light.name=="ArenaDirectionalLight")Assert.AreEqual(0f,light.intensity,"Outdoor light must not enter the vault.");
                Assert.IsTrue(game.GameplayCamera.orthographic);
                Assert.AreEqual(travel.InteriorOrthographicSize,game.GameplayCamera.orthographicSize,.01f);
                Assert.That(game.CameraRig.transform.eulerAngles.x,Is.InRange(travel.InteriorCameraPitch-.01f,travel.InteriorMaximumCameraPitch+.01f));
                Assert.IsTrue(ArenaLightingView.TryGetForCamera(game.GameplayCamera,out var view));
                Assert.AreSame(game.Lighting,view,"Interior player visibility must use the same camera-scoped render pass.");
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
                foreach(var mount in game.Lighting.Rig.ShaftMounts)
                    Assert.IsNotNull(mount.BoundSupport,arena+": "+mount.SupportPath);
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
            // The authored camera can be adjusted in the scene. Both disable paths
            // must restore that lens rather than a historical fixed FOV and pitch.
            lighting.SetLightingEnabled(false);
            float authoredFov=game.CameraRig.Lens.FieldOfView, authoredPitch=game.CameraRig.transform.eulerAngles.x;
            lighting.SetLightingEnabled(true);
            lighting.enabled=false;yield return null;
            Assert.AreEqual(authoredFov,game.CameraRig.Lens.FieldOfView,.01f);
            Assert.AreEqual(authoredPitch,game.CameraRig.transform.eulerAngles.x,.01f);
            lighting.enabled=true;yield return null;AssertExteriorLens();
            var travel=Object.FindAnyObjectByType<WarehouseTravel>();
            Assert.IsTrue(travel.PlaceInWarehouse(true));yield return null;
            game.ResetRun();yield return null;
            AssertExteriorLens();
            Assert.IsTrue(lighting.FocusEnabled);
        }
        [UnityTest]public IEnumerator VaultKeyLightsAndRealSpriteShadowsWorkInEveryArenaVariant()
        {
            foreach(string scene in new[]{"CombatArena","CombatArena_3Min","CombatArena_8Min"})
            {
                if(game.gameObject.scene.name!=scene)
                {
#if UNITY_EDITOR
                    // The reference variants are intentionally excluded from the player build.
                    yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                        "Assets/_Project/Scenes/"+scene+".unity",new LoadSceneParameters(LoadSceneMode.Single));
#else
                    if(!Application.CanStreamedLevelBeLoaded(scene))continue;
                    yield return SceneManager.LoadSceneAsync(scene);
#endif
                    yield return null;
                    game=Object.FindAnyObjectByType<CombatGameBootstrap>();
                    game.Director.AutoTick=game.Encounter.AutoTick=false;
                }
                var travel=Object.FindAnyObjectByType<WarehouseTravel>();Assert.IsTrue(travel.PlaceInWarehouse(true));yield return null;
                Assert.IsFalse(game.Lighting.AtmosphereEnabled,scene);
                Assert.AreEqual(game.Lighting.Profile.WarehouseAmbientEquator,RenderSettings.ambientEquatorColor,scene);
                int keyLights=0;
                foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if(!light.name.StartsWith("LuzAmbienteBoveda") && light.name!="LuzInterior")continue;
                    keyLights++;Assert.AreEqual(LightType.Spot,light.type,scene);Assert.AreEqual(LightShadows.Soft,light.shadows,scene);
                    Assert.IsFalse(light.GetUniversalAdditionalLightData().usePipelineSettings,scene);
                    Assert.Less(light.transform.forward.y,-.3f,scene);
                }
                Assert.GreaterOrEqual(keyLights,3,scene);
                var view=game.Motor.GetComponent<CollectorSpriteView>();view.Advance(0);
                Assert.AreEqual(ShadowCastingMode.On,view.CharacterRenderer.shadowCastingMode,scene);
                Assert.IsFalse(view.ProjectedShadowRenderer.enabled,scene);
                Assert.IsTrue(travel.PlaceInWarehouse(false));yield return null;
                Assert.IsTrue(game.Lighting.AtmosphereEnabled,scene);
            }
        }
        [Test]public void VisibleShaftsHaveRealElevatedSourcesAndMotesDoNotMultiplyTwoNearZeroAlphas()
        {
            var rig=game.Lighting.Rig;
            Assert.That(rig.ShaftLights.Length,Is.GreaterThanOrEqualTo(4));
            foreach(var source in rig.ShaftLights)
            {
                Assert.AreEqual(LightType.Spot,source.type);
                Assert.AreEqual(LightShadows.Soft,source.shadows,"A visible projector must also cast shadows onto the floor.");
                Assert.LessOrEqual(source.shadowNormalBias,.1f,"Large normal bias detaches a pixel sprite's shadow from its feet.");
                Assert.IsFalse(source.GetUniversalAdditionalLightData().usePipelineSettings,"The configured sprite shadow bias must actually be used.");
                // Authored supports have different heights and terrain slopes.
                Assert.That(source.transform.forward.y,Is.LessThan(-.15f),"The mounted projector must aim down toward the ground.");
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
        [Test]public void ReflectorsAreMountedOnExistingLandmarksAndKeepTheirBeamSettings()
        {
            var rig=game.Lighting.Rig;
            Assert.AreEqual(rig.ShaftLights.Length,rig.ShaftMounts.Length);
            foreach(var mount in rig.ShaftMounts)
            {
                Assert.IsNotNull(mount.BoundSupport,mount.SupportPath);
                Assert.IsTrue(mount.Fixture.gameObject.activeInHierarchy,mount.SupportPath);
                Assert.IsTrue(mount.Source.transform.IsChildOf(mount.Fixture));
                Assert.Less(Vector3.Distance(mount.Fixture.position,mount.BoundSupport.TransformPoint(mount.LocalPosition)),.01f);
                Assert.AreEqual(65f,mount.Source.intensity,.01f);
                Assert.AreEqual(20f,mount.Source.range,.01f);
                Assert.AreEqual(36f,mount.Source.spotAngle,.01f);
                Assert.AreEqual(25f,mount.Source.innerSpotAngle,.01f);
                Assert.AreEqual(new Color(.82f,.84f,1f),mount.Source.color);
                bool luminous=false;
                foreach(var renderer in mount.Fixture.GetComponentsInChildren<MeshRenderer>())
                foreach(var material in renderer.sharedMaterials)
                    luminous |= (material.HasProperty("_BaseColor")&&material.GetColor("_BaseColor").maxColorComponent>1f)
                        || (material.IsKeywordEnabled("_EMISSION")&&material.HasProperty("_EmissionColor")&&material.GetColor("_EmissionColor").maxColorComponent>1f);
                Assert.IsTrue(luminous,"The projector needs a visible self-lit lens, not only an invisible light source.");
            }
            for(int i=0;i<2;i++)
            {
                Vector3 p=rig.ShaftMounts[i].Fixture.position-game.BossArena.Center;p.y=0;
                Assert.GreaterOrEqual(p.magnitude,game.BossArena.Radius,"The foundry supports belong on its rim, outside the fighting floor.");
            }
        }
    }
}
