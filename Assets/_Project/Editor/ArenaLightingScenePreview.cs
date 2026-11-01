using System;
using EchoScrap.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace EchoScrap.Editor
{
    /// <summary>Temporary render state only. Closing preview restores environment and camera settings.</summary>
    public sealed class ArenaLightingScenePreview : IArenaLightingView, IDisposable
    {
        public ArenaLightingProfile Profile { get; private set; }
        public ArenaLightingRig Rig { get; private set; }
        public Camera GameplayCamera { get; private set; }
        public Vector3 PlayerPosition { get; private set; }
        public float TacticalRadius => Profile.MinimumProtectedRadius+Profile.ProtectedRadiusMargin;
        public float DecorativeIntensity => 1f;
        public bool AtmosphereEnabled => true;
        public bool FocusEnabled { get; set; }
        public Light LanternLight => null;
        public Light ShaftLightAt(int index) => shaders.LightAt(index);
        readonly ArenaLightingShaderState shaders=new ArenaLightingShaderState();
        readonly GameObject volumeObject;
        readonly Volume volume;
        readonly Light key;
        readonly bool animate;
        double previousTime;
        Snapshot snapshot;
        bool disposed;

        public ArenaLightingScenePreview(ArenaLightingProfile profile,ArenaLightingRig rig,bool animate=true)
        {
            Profile=profile;Rig=rig;this.animate=animate;previousTime=EditorApplication.timeSinceStartup;
            foreach(var root in rig.gameObject.scene.GetRootGameObjects())
            foreach(var light in root.GetComponentsInChildren<Light>(true))
                if(light.name=="ArenaDirectionalLight"){key=light;break;}
            // URP SceneView always evaluates volumes on Default, independently of camera.volumeLayerMask.
            volumeObject=new GameObject("Lighting authoring preview (temporary)"){hideFlags=HideFlags.HideAndDontSave,layer=0};
            volume=volumeObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=10;volume.sharedProfile=profile.PostProcessing;volume.weight=0;
            if(animate && rig.Dust!=null)foreach(var ps in rig.Dust)
                if(ps!=null && ps.gameObject.activeInHierarchy)ps.Simulate(3f,false,true,false);
            RenderPipelineManager.beginCameraRendering+=BeforeCamera;
            RenderPipelineManager.endCameraRendering+=AfterCamera;
            EditorApplication.update+=Tick;
            AssemblyReloadEvents.beforeAssemblyReload+=Dispose;
            EditorApplication.playModeStateChanged+=PlayChanged;
        }
        void PlayChanged(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingEditMode)Dispose();}
        void Tick()
        {
            if(disposed || Application.isPlaying)return;
            if(Rig==null || Profile==null){Dispose();return;}
            double time=EditorApplication.timeSinceStartup;float delta=Mathf.Clamp((float)(time-previousTime),0,.05f);
            if(time-previousTime<1.0/30)return;previousTime=time;
            if(animate && Rig.Dust!=null)foreach(var ps in Rig.Dust)
                if(ps!=null && ps.gameObject.activeInHierarchy)ps.Simulate(delta,false,false,false);
            SceneView.RepaintAll();
        }
        void BeforeCamera(ScriptableRenderContext context,Camera camera)
        {
            if(disposed || Application.isPlaying || camera.cameraType!=CameraType.SceneView || Rig==null)return;
            var view=SceneView.currentDrawingSceneView;
            BeginFrame(camera,view!=null?view.pivot:Rig.transform.position,view);
        }
        void AfterCamera(ScriptableRenderContext context,Camera camera){if(camera==GameplayCamera)EndFrame();}

        // Also usable by render-request QA without starting Play or changing editor preferences.
        public void BeginFrame(Camera camera,Vector3 center,SceneView sceneView=null)
        {
            if(disposed || Application.isPlaying || Rig==null)return;
            if(snapshot!=null)EndFrame();
            snapshot=new Snapshot(camera,key,sceneView);GameplayCamera=camera;PlayerPosition=center;
            var data=camera.GetUniversalAdditionalCameraData();
            data.SetRenderer(Profile.RendererIndex);data.renderPostProcessing=true;data.requiresDepthTexture=true;
            data.volumeLayerMask|=1<<Profile.AtmosphereLayer;camera.cullingMask|=1<<Profile.AtmosphereLayer;
            camera.allowHDR=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Profile.SkyColor;
            if(sceneView!=null){sceneView.sceneLighting=true;sceneView.sceneViewState.showImageEffects=true;}
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=Profile.AmbientSky;
            RenderSettings.ambientEquatorColor=Profile.AmbientEquator;RenderSettings.ambientGroundColor=Profile.AmbientGround;
            RenderSettings.ambientIntensity=Profile.AmbientIntensity;RenderSettings.fog=false;
            if(key!=null){key.color=Profile.KeyColor;key.intensity=Profile.KeyIntensity;key.transform.rotation=Quaternion.Euler(Profile.KeyRotation);}
            volume.weight=1;ArenaLightingView.EditorPreview=this;
            shaders.Apply(this,(float)EditorApplication.timeSinceStartup);
        }
        public void EndFrame()
        {
            if(snapshot==null)return;
            snapshot.Restore();snapshot=null;volume.weight=0;
            if(ReferenceEquals(ArenaLightingView.EditorPreview,this))ArenaLightingView.EditorPreview=null;
            GameplayCamera=null;
            Shader.SetGlobalFloat("_EchoArenaAtmosphereActive",0);
            Shader.SetGlobalInt("_EchoArenaShaftCount",0);
            Shader.SetGlobalVector("_EchoArenaLanternColor",Vector4.zero);
        }
        public void Dispose()
        {
            if(disposed)return;EndFrame();disposed=true;
            RenderPipelineManager.beginCameraRendering-=BeforeCamera;RenderPipelineManager.endCameraRendering-=AfterCamera;
            EditorApplication.update-=Tick;AssemblyReloadEvents.beforeAssemblyReload-=Dispose;EditorApplication.playModeStateChanged-=PlayChanged;
            if(Rig!=null && animate && Rig.Dust!=null)foreach(var ps in Rig.Dust)
                if(ps!=null)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(volumeObject!=null)Object.DestroyImmediate(volumeObject);SceneView.RepaintAll();
        }

        sealed class Snapshot
        {
            readonly Camera camera;readonly UniversalAdditionalCameraData data;readonly int renderer,culling,volumeMask;
            readonly bool post,hdr;readonly CameraOverrideOption depth;readonly CameraClearFlags flags;readonly Color background;
            readonly AmbientMode ambientMode;readonly Color sky,equator,ground,ambient;readonly float intensity;readonly bool fog;
            readonly Light key;readonly Color keyColor;readonly float keyIntensity;readonly Quaternion keyRotation;
            readonly SceneView sceneView;readonly bool sceneLighting,imageEffects;
            public Snapshot(Camera camera,Light key,SceneView view)
            {
                this.camera=camera;this.key=key;sceneView=view;data=camera.GetUniversalAdditionalCameraData();
                var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;renderer=0;
                if(pipeline!=null)for(int i=0;i<pipeline.rendererDataList.Length;i++)if(pipeline.GetRenderer(i)==data.scriptableRenderer){renderer=i;break;}
                culling=camera.cullingMask;volumeMask=data.volumeLayerMask;post=data.renderPostProcessing;depth=data.requiresDepthOption;hdr=camera.allowHDR;
                flags=camera.clearFlags;background=camera.backgroundColor;ambientMode=RenderSettings.ambientMode;ambient=RenderSettings.ambientLight;
                sky=RenderSettings.ambientSkyColor;equator=RenderSettings.ambientEquatorColor;ground=RenderSettings.ambientGroundColor;
                intensity=RenderSettings.ambientIntensity;fog=RenderSettings.fog;
                if(key!=null){keyColor=key.color;keyIntensity=key.intensity;keyRotation=key.transform.rotation;}
                if(view!=null){sceneLighting=view.sceneLighting;imageEffects=view.sceneViewState.showImageEffects;}
            }
            public void Restore()
            {
                if(camera!=null)
                {
                    data.SetRenderer(renderer);data.renderPostProcessing=post;data.requiresDepthOption=depth;data.volumeLayerMask=volumeMask;
                    camera.cullingMask=culling;camera.allowHDR=hdr;camera.clearFlags=flags;camera.backgroundColor=background;
                }
                RenderSettings.ambientMode=ambientMode;RenderSettings.ambientLight=ambient;RenderSettings.ambientSkyColor=sky;
                RenderSettings.ambientEquatorColor=equator;RenderSettings.ambientGroundColor=ground;RenderSettings.ambientIntensity=intensity;RenderSettings.fog=fog;
                if(key!=null){key.color=keyColor;key.intensity=keyIntensity;key.transform.rotation=keyRotation;}
                if(sceneView!=null){sceneView.sceneLighting=sceneLighting;sceneView.sceneViewState.showImageEffects=imageEffects;}
            }
        }
    }
}
