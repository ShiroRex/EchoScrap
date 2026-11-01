using System.Collections;
using System.Linq;
using EchoScrap.Combat;
using EchoScrap.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace EchoScrap.Tests
{
    public sealed class ArenaLightingAuthoringTests
    {
        const string Temporary="Assets/LightingAuthoringTestTemporary";
        [SerializeField] bool previousOptionsEnabled;
        [SerializeField] int previousOptions;
        ArenaLightingProfile Profile=>ArenaLightingAuthoring.DefaultProfile;

        [SetUp] public void Setup()
        {
            previousOptionsEnabled=EditorSettings.enterPlayModeOptionsEnabled;
            previousOptions=(int)EditorSettings.enterPlayModeOptions;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Undo.ClearAll();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(EditorApplication.isPlaying)yield return new ExitPlayMode();
            EditorSettings.enterPlayModeOptionsEnabled=previousOptionsEnabled;
            EditorSettings.enterPlayModeOptions=(EnterPlayModeOptions)previousOptions;
            ArenaLightingView.EditorPreview=null;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Temporary);Undo.ClearAll();
        }
        ArenaLightingRig Empty()=>ArenaLightingAuthoring.Prepare(SceneManager.GetActiveScene(),Profile,false);
        ArenaLightingItem Place(ArenaLightingRig rig,ArenaLightingItemKind kind,Vector3? position=null)
        {
            var settings=new ArenaLightPlacement();settings.Preset(kind);
            return ArenaLightingAuthoring.Place(rig,settings,position??Vector3.zero,Vector3.up,Profile);
        }

        [Test] public void PreparingTwiceReusesSceneLayoutWithoutEditingSharedPrefab()
        {
            var original=Profile.RigPrefab.GetComponent<ArenaLightingRig>();
            int mounts=original.ShaftMounts.Length;
            var rig=ArenaLightingAuthoring.Prepare(SceneManager.GetActiveScene(),Profile);
            Assert.That(rig.SceneAuthored&&rig.ManageItems);
            Assert.That(rig.ShaftMounts,Is.Empty,"Authored poses must not snap to old supports in Play.");
            Assert.That(ArenaLightingAuthoring.Prepare(SceneManager.GetActiveScene(),Profile),Is.SameAs(rig));
            Assert.That(original.SceneAuthored,Is.False);Assert.That(original.ShaftMounts.Length,Is.EqualTo(mounts));
            Assert.That(rig.ShaftLights.Length,Is.EqualTo(original.ShaftLights.Length));
        }

        [Test] public void PresetsRegisterLightDustAndMistWithActualFixtureMeshes()
        {
            var rig=Empty();
            foreach(ArenaLightingItemKind kind in System.Enum.GetValues(typeof(ArenaLightingItemKind)))
            {
                var item=Place(rig,kind);
                if((int)kind<=(int)ArenaLightingItemKind.GodRay)
                {
                    Assert.That(item.Source,Is.Not.Null);
                    Assert.That(item.Source.type,Is.EqualTo(kind==ArenaLightingItemKind.Point?LightType.Point:LightType.Spot));
                    if(kind!=ArenaLightingItemKind.Point)Assert.That(rig.ShaftLights,Does.Contain(item.Source));
                }
                if(kind==ArenaLightingItemKind.WallReflector||kind==ArenaLightingItemKind.StandingReflector)
                    Assert.That(item.GetComponentsInChildren<MeshFilter>().All(m=>m.sharedMesh!=null));
                if(item.Dust!=null)
                {
                    Assert.That(rig.Dust,Does.Contain(item.Dust));
                    Assert.That(item.Dust.GetComponent<ParticleSystemRenderer>().sharedMaterial,Is.Not.Null);
                    Assert.That(item.Dust.GetComponent<ParticleSystemRenderer>().renderingLayerMask,Is.EqualTo(ArenaLightingRig.DecorativeRenderingLayer));
                }
            }
            Assert.That(rig.ShaftLights.Length,Is.EqualTo(4));Assert.That(rig.Dust.Length,Is.EqualTo(5));Assert.That(rig.MistBanks.Length,Is.EqualTo(1));
        }

        [Test] public void PlacementDuplicationAndDeletionSupportUndoRedo()
        {
            var rig=Empty();Undo.ClearAll();Place(rig,ArenaLightingItemKind.GodRay);
            Undo.FlushUndoRecordObjects();Undo.PerformUndo();rig.RefreshItems();Assert.That(rig.ShaftLights,Is.Empty);
            Undo.PerformRedo();rig.RefreshItems();Assert.That(rig.ShaftLights.Length,Is.EqualTo(1));
            var first=rig.GetComponentInChildren<ArenaLightingItem>();
            var copy=ArenaLightingAuthoring.Duplicate(first);Assert.That(rig.ShaftLights.Length,Is.EqualTo(2));
            Assert.That(copy.Source,Is.Not.SameAs(first.Source));Assert.That(copy.Dust,Is.Not.SameAs(first.Dust));
            ArenaLightingAuthoring.Remove(copy);Assert.That(rig.ShaftLights.Length,Is.EqualTo(1));
            Undo.PerformUndo();rig.RefreshItems();Assert.That(rig.ShaftLights.Length,Is.EqualTo(2));
        }

        [Test] public void AimingRotatesHeadAndDustWithoutMovingSupportAndCanBeUndone()
        {
            var item=Place(Empty(),ArenaLightingItemKind.WallReflector);var position=item.transform.position;
            var original=item.Source.transform.forward;Vector3 target=new Vector3(5,0,8);
            Undo.IncrementCurrentGroup();ArenaLightingAuthoring.Aim(item,target);Undo.FlushUndoRecordObjects();
            Assert.That(Vector3.Dot(item.Source.transform.forward,(target-item.Source.transform.position).normalized),Is.GreaterThan(.999f));
            Assert.That(item.transform.position,Is.EqualTo(position));
            Assert.That(Vector3.Distance(item.Dust.transform.position,item.Source.transform.position+item.Source.transform.forward*5),Is.LessThan(.001f));
            Undo.PerformUndo();Assert.That(Vector3.Distance(item.Source.transform.forward,original),Is.LessThan(.001f));
        }

        [Test] public void SavingSceneRetainsMovedPosesAndInactiveItems()
        {
            var rig=Empty();var item=Place(rig,ArenaLightingItemKind.GodRay,new Vector3(13,0,-7));
            Vector3 position=item.transform.position;item.gameObject.SetActive(false);
            AssetDatabase.CreateFolder("Assets","LightingAuthoringTestTemporary");
            Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),Temporary+"/Layout.unity"));
            var scene=EditorSceneManager.OpenScene(Temporary+"/Layout.unity");rig=ArenaLightingRig.FindSceneLayout(scene);
            Assert.That(rig,Is.Not.Null);rig.BindToScene(scene);
            item=rig.GetComponentInChildren<ArenaLightingItem>(true);
            Assert.That(item.transform.position,Is.EqualTo(position));Assert.That(item.gameObject.activeSelf,Is.False);
            Assert.That(rig.ShaftLights.Single(),Is.SameAs(item.Source));
        }

        [Test] public void SharedLayoutRoundTripKeepsOneOwnerPerSourceAndDust()
        {
            var rig=Empty();Place(rig,ArenaLightingItemKind.GodRay);Place(rig,ArenaLightingItemKind.Dust);
            AssetDatabase.CreateFolder("Assets","LightingAuthoringTestTemporary");
            var profile=Object.Instantiate(Profile);
            try
            {
                profile.RigPrefab=PrefabUtility.SaveAsPrefabAsset(rig.gameObject,Temporary+"/Shared.prefab");
                ArenaLightingAuthoring.SaveShared(rig,profile);
                Object.DestroyImmediate(rig.gameObject);
                rig=ArenaLightingAuthoring.Prepare(SceneManager.GetActiveScene(),profile);
                Assert.That(rig.GetComponentsInChildren<ArenaLightingItem>().Length,Is.EqualTo(2));
                Assert.That(rig.ShaftLights.Length,Is.EqualTo(1));Assert.That(rig.Dust.Length,Is.EqualTo(2));
                Assert.That(rig.GetComponentsInChildren<ArenaLightingItem>().Count(i=>i.Kind==ArenaLightingItemKind.GodRay),Is.EqualTo(1));
            }
            finally{Object.DestroyImmediate(profile);}
        }

        [Test] public void PreviewRestoresCameraEnvironmentAndGlobalRegistration()
        {
            var rig=Empty();var dust=Place(rig,ArenaLightingItemKind.Dust).Dust;
            var camera=new GameObject("Preview test camera").AddComponent<Camera>();
            var data=camera.GetUniversalAdditionalCameraData();data.SetRenderer(0);data.volumeLayerMask=1;data.renderPostProcessing=false;
            data.requiresDepthOption=CameraOverrideOption.UsePipelineSettings;camera.cullingMask=1;camera.backgroundColor=Color.green;
            var renderer=data.scriptableRenderer;var mode=RenderSettings.ambientMode;var sky=RenderSettings.ambientSkyColor;
            using(var preview=new ArenaLightingScenePreview(Profile,rig))
            {
                Assert.That(dust.particleCount,Is.GreaterThan(0));
                preview.BeginFrame(camera,Vector3.zero);
                Assert.That(ArenaLightingView.TryGetForCamera(camera,out var owner));Assert.That(owner,Is.SameAs(preview));
                Assert.That(data.renderPostProcessing);Assert.That(camera.cullingMask&(1<<Profile.AtmosphereLayer),Is.Not.Zero);
                preview.EndFrame();
                Assert.That(ArenaLightingView.TryGetForCamera(camera,out _),Is.False);
                Assert.That(data.scriptableRenderer,Is.SameAs(renderer));Assert.That(data.volumeLayerMask.value,Is.EqualTo(1));
                Assert.That(data.requiresDepthOption,Is.EqualTo(CameraOverrideOption.UsePipelineSettings));
                Assert.That(data.renderPostProcessing,Is.False);Assert.That(camera.cullingMask,Is.EqualTo(1));Assert.That(camera.backgroundColor,Is.EqualTo(Color.green));
                Assert.That(RenderSettings.ambientMode,Is.EqualTo(mode));Assert.That(RenderSettings.ambientSkyColor,Is.EqualTo(sky));
            }
            Assert.That(dust.particleCount,Is.Zero);
        }

        [UnityTest] public IEnumerator PlayReusesSavedRigAndPreservesPosesAcrossTwoSessions()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/CombatArena.unity");
            var rig=Empty();var light=Place(rig,ArenaLightingItemKind.GodRay,new Vector3(7,0,3));
            light.name="Authored test source";var position=light.transform.position;
            var disabled=Place(rig,ArenaLightingItemKind.Dust);disabled.name="Disabled dust";disabled.gameObject.SetActive(false);
            AssetDatabase.CreateFolder("Assets","LightingAuthoringTestTemporary");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),Temporary+"/CombatArena.unity");
            EditorSettings.enterPlayModeOptionsEnabled=true;
            EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
            for(int session=0;session<2;session++)
            {
                yield return new EnterPlayMode(false);yield return null;
                var game=Object.FindAnyObjectByType<CombatGameBootstrap>();Assert.That(game.IsInitialized);
                rig=ArenaLightingRig.FindSceneLayout(game.gameObject.scene);Assert.That(rig,Is.Not.Null);
                light=rig.GetComponentsInChildren<ArenaLightingItem>(true).Single(i=>i.name=="Authored test source");
                disabled=rig.GetComponentsInChildren<ArenaLightingItem>(true).Single(i=>i.name=="Disabled dust");
                Assert.That(game.Lighting.Rig,Is.SameAs(rig));Assert.That(Object.FindObjectsByType<ArenaLightingRig>(FindObjectsInactive.Include).Length,Is.EqualTo(1));
                Assert.That(light.transform.position,Is.EqualTo(position));Assert.That(disabled.gameObject.activeSelf,Is.False);
                Assert.That(light.Dust.isPlaying,"Authored dust must start on entering Play.");
                game.Lighting.SetWarehouse(true);Assert.That(rig.gameObject.activeSelf,Is.False);
                game.Lighting.SetWarehouse(false);Assert.That(rig.gameObject.activeSelf);Assert.That(disabled.gameObject.activeSelf,Is.False);
                yield return new ExitPlayMode();
                // Load the saved layout for each run, including inactive objects not awakened by Fast Play.
                EditorSceneManager.OpenScene(Temporary+"/CombatArena.unity");
            }
        }
    }
}
