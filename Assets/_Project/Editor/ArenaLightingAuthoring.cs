using System;
using System.Collections.Generic;
using System.Linq;
using EchoScrap.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace EchoScrap.Editor
{
    [Serializable]
    public sealed class ArenaLightPlacement
    {
        public ArenaLightingItemKind Kind;
        public Color Color=new Color(1f,.64f,.3f);
        public float Intensity=5.5f, Range=12f, Angle=36f, Height=2.5f, Offset=.06f, Yaw, Pitch=45f;
        public bool Shadows, GodRays=true, AddDust=true;
        public Vector3 Size=new Vector3(6,4,6);
        public float DustRate=5.5f;
        public void Preset(ArenaLightingItemKind kind)
        {
            Kind=kind;Height=kind==ArenaLightingItemKind.Point?2.5f:kind==ArenaLightingItemKind.Spot?4f:
                kind==ArenaLightingItemKind.GodRay?5f:kind==ArenaLightingItemKind.Dust?2f:kind==ArenaLightingItemKind.Mist ? .75f : 0f;
            Color=kind==ArenaLightingItemKind.Point?new Color(1,.64f,.3f):new Color(.82f,.84f,1);
            Intensity=kind==ArenaLightingItemKind.Point?5.5f:65f;Range=kind==ArenaLightingItemKind.Point?12:20;
            Shadows=kind!=ArenaLightingItemKind.Point;
            Size=kind==ArenaLightingItemKind.Mist?new Vector3(11,1.6f,7):new Vector3(6,4,6);
        }
    }

    /// <summary>Undoable scene authoring; never runs migrations on editor startup.</summary>
    public static class ArenaLightingAuthoring
    {
        public static ArenaLightingProfile DefaultProfile => Resources.Load<ArenaLightingProfile>("Lighting/ArenaLightingProfile");
        public static ArenaLightingRig Prepare(Scene scene,ArenaLightingProfile profile,bool copyDefault=true)
        {
            if(!scene.IsValid() || !scene.isLoaded)throw new InvalidOperationException("Abre una escena antes de preparar el montaje.");
            var existing=ArenaLightingRig.FindSceneLayout(scene);if(existing!=null)return existing;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Preparar iluminación de arena");
            GameObject root=copyDefault&&profile!=null&&profile.RigPrefab!=null?Object.Instantiate(profile.RigPrefab):new GameObject("Iluminación de arena");
            root.name="Iluminación de arena";SceneManager.MoveGameObjectToScene(root,scene);
            var rig=root.GetComponent<ArenaLightingRig>()??root.AddComponent<ArenaLightingRig>();
            rig.ManageItems=false;rig.SceneAuthored=true;
            foreach(var light in root.GetComponentsInChildren<Light>(true))
            {
                var mount=rig.ShaftMounts?.FirstOrDefault(m=>m!=null&&m.Source==light);
                var owned=light.GetComponentInParent<ArenaLightingItem>();
                Transform itemRoot=owned!=null&&owned.Source==light?owned.transform:mount!=null?mount.Fixture:light.transform.parent;
                if(itemRoot==null || itemRoot==rig.transform)itemRoot=light.transform;
                var item=itemRoot.GetComponent<ArenaLightingItem>()??itemRoot.gameObject.AddComponent<ArenaLightingItem>();
                item.Source=light;item.GodRays=rig.ShaftLights!=null&&rig.ShaftLights.Contains(light);
                if(owned==null || owned.Source!=light)
                    item.Kind=light.type==LightType.Point?ArenaLightingItemKind.Point:
                        mount!=null&&mount.SupportPath.Contains("FoundryBoundary")?ArenaLightingItemKind.StandingReflector:ArenaLightingItemKind.WallReflector;
            }
            if(rig.Dust!=null)foreach(var ps in rig.Dust)
            {
                if(ps==null)continue;
                var owner=ps.GetComponentInParent<ArenaLightingItem>();if(owner!=null&&owner.Dust==ps)continue;
                var item=ps.GetComponent<ArenaLightingItem>()??ps.gameObject.AddComponent<ArenaLightingItem>();
                item.Kind=ArenaLightingItemKind.Dust;item.Dust=ps;
            }
            if(rig.MistBanks!=null)foreach(var bank in rig.MistBanks)
            {
                if(bank==null)continue;var item=bank.GetComponent<ArenaLightingItem>()??bank.gameObject.AddComponent<ArenaLightingItem>();
                item.Kind=ArenaLightingItemKind.Mist;item.MistBank=bank;
            }
            // Bake the authored poses: entering Play must not snap a moved lamp back to its old support.
            rig.ShaftMounts=Array.Empty<ArenaLightingRig.FixtureMount>();rig.ManageItems=true;rig.RefreshItems();
            Undo.RegisterCreatedObjectUndo(root,"Preparar iluminación de arena");Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);Selection.activeGameObject=root;return rig;
        }

        public static ArenaLightingItem Place(ArenaLightingRig rig,ArenaLightPlacement settings,Vector3 surface,Vector3 normal,ArenaLightingProfile profile)
        {
            if(rig==null || !rig.SceneAuthored)throw new InvalidOperationException("Prepara un montaje de escena.");
            if(Application.isPlaying)throw new InvalidOperationException("Sal de Play para guardar cambios del montaje.");
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Colocar iluminación");
            bool reflector=settings.Kind==ArenaLightingItemKind.StandingReflector||settings.Kind==ArenaLightingItemKind.WallReflector;
            var template=profile!=null&&profile.RigPrefab!=null?profile.RigPrefab.GetComponent<ArenaLightingRig>():null;
            GameObject root;
            root=reflector?Reflector(settings.Kind):new GameObject();
            root.name=Name(settings.Kind);root.transform.SetParent(rig.transform,false);SetLayer(root,profile!=null?profile.AtmosphereLayer:9);
            Quaternion yaw=Quaternion.Euler(0,settings.Yaw,0);
            if(settings.Kind==ArenaLightingItemKind.WallReflector && Mathf.Abs(normal.y)<.85f)
                yaw=Quaternion.LookRotation(Vector3.ProjectOnPlane(normal,Vector3.up).normalized,Vector3.up)*Quaternion.Euler(0,settings.Yaw,0);
            root.transform.SetPositionAndRotation(surface+normal*settings.Offset+Vector3.up*settings.Height,yaw);
            var item=root.AddComponent<ArenaLightingItem>();item.Kind=settings.Kind;
            if((int)settings.Kind<=(int)ArenaLightingItemKind.GodRay)
            {
                var source=root.GetComponentInChildren<Light>();
                if(source==null){var child=new GameObject("Luz");child.transform.SetParent(root.transform,false);source=child.AddComponent<Light>();}
                source.type=settings.Kind==ArenaLightingItemKind.Point?LightType.Point:LightType.Spot;
                source.color=settings.Color;source.intensity=Mathf.Max(0,settings.Intensity);source.range=Mathf.Max(.1f,settings.Range);
                source.spotAngle=Mathf.Clamp(settings.Angle,1,179);source.innerSpotAngle=source.spotAngle*.7f;
                source.shadows=settings.Shadows?LightShadows.Soft:LightShadows.None;
                source.shadowBias=.025f;source.shadowNormalBias=.08f;
                var lightData=source.GetUniversalAdditionalLightData();
                lightData.usePipelineSettings=false;lightData.softShadowQuality=SoftShadowQuality.Medium;
                item.Source=source;item.GodRays=source.type==LightType.Spot&&settings.GodRays;
                if(!reflector)source.transform.rotation=Quaternion.Euler(settings.Pitch,settings.Yaw,0);
                if(settings.AddDust && source.type==LightType.Spot)
                    item.Dust=CreateDust(root.transform,template,settings,source.transform.position+source.transform.forward*Mathf.Min(5,source.range*.3f));
            }
            else if(settings.Kind==ArenaLightingItemKind.Dust)
                item.Dust=CreateDust(root.transform,template,settings,root.transform.position);
            else {item.MistBank=root.transform;root.transform.localScale=settings.Size;}
            SetLayer(root,profile!=null?profile.AtmosphereLayer:9);
            Undo.RegisterCreatedObjectUndo(root,"Colocar iluminación");rig.RefreshItems();
            EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);Selection.activeGameObject=root;Undo.CollapseUndoOperations(group);return item;
        }

        static ParticleSystem CreateDust(Transform parent,ArenaLightingRig template,ArenaLightPlacement settings,Vector3 position)
        {
            ParticleSystem ps;
            if(template!=null && template.Dust!=null && template.Dust.Length>0 && template.Dust[0]!=null)
                ps=Object.Instantiate(template.Dust[0],parent);
            else
            {
                var go=new GameObject("Polvo");go.transform.SetParent(parent,false);ps=go.AddComponent<ParticleSystem>();
                var main=ps.main;main.loop=true;main.prewarm=true;main.maxParticles=72;main.startLifetime=10;main.startSpeed=.035f;
                main.startSize=new ParticleSystem.MinMaxCurve(.065f,.14f);main.startColor=new Color(.96f,.9f,1,.65f);main.simulationSpace=ParticleSystemSimulationSpace.World;
            }
            ps.name="Polvo suspendido";ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.transform.position=position;
            foreach(var old in ps.GetComponents<ArenaLightingItem>())Object.DestroyImmediate(old);
            var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=settings.Size;
            var emission=ps.emission;emission.enabled=true;emission.rateOverTime=Mathf.Max(0,settings.DustRate);
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=Resources.Load<Material>("Lighting/Dust");
            renderer.renderingLayerMask=ArenaLightingRig.DecorativeRenderingLayer;return ps;
        }

        static GameObject Reflector(ArenaLightingItemKind kind)
        {
            var prefab=Resources.Load<GameObject>("Lighting/Fixtures/ReclaimedProjector");
            if(prefab==null)throw new InvalidOperationException("Falta el prefab del reflector recuperado.");
            bool tall=kind==ArenaLightingItemKind.StandingReflector;
            var root=new GameObject();var head=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.transform);
            head.transform.localPosition=tall?new Vector3(.35f,5.85f,-.65f):new Vector3(0,.25f,.7f);
            head.transform.localRotation=Quaternion.Euler(45,tall?180:0,0);
            var support=new GameObject("Soporte y cable",typeof(MeshFilter),typeof(MeshRenderer));support.transform.SetParent(root.transform,false);
            support.GetComponent<MeshFilter>().sharedMesh=Resources.Load<Mesh>("Lighting/Fixtures/Mounting_"+(tall?0:2));
            support.GetComponent<MeshRenderer>().sharedMaterials=new[]{Resources.Load<Material>("Lighting/Fixtures/Reclaimed rust"),
                Resources.Load<Material>("Lighting/Industrial dark steel"),Resources.Load<Material>("Lighting/Oxidized lamp housing")};
            return root;
        }

        public static void Aim(ArenaLightingItem item,Vector3 target)
        {
            if(item==null||item.Source==null||item.Source.type!=LightType.Spot)return;
            Transform head=item.Source.transform.parent;
            if(head==item.transform)head=item.Source.transform;
            if((target-head.position).sqrMagnitude<.001f)return;
            Undo.RecordObject(head,"Apuntar reflector");head.rotation=Quaternion.LookRotation(target-head.position,Vector3.up);
            PrefabUtility.RecordPrefabInstancePropertyModifications(head);
            if(item.Dust!=null)
            {Undo.RecordObject(item.Dust.transform,"Colocar polvo en el haz");item.Dust.transform.position=item.Source.transform.position+item.Source.transform.forward*Mathf.Min(5,item.Source.range*.3f);}
            EditorSceneManager.MarkSceneDirty(item.gameObject.scene);
        }

        public static ArenaLightingItem Duplicate(ArenaLightingItem item)
        {
            Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Duplicar iluminación");
            var go=Object.Instantiate(item.gameObject,item.transform.parent);go.name=item.name+" copia";go.transform.position+=Vector3.right*.75f;
            Undo.RegisterCreatedObjectUndo(go,"Duplicar iluminación");var rig=go.GetComponentInParent<ArenaLightingRig>();rig.RefreshItems();
            Selection.activeGameObject=go;EditorSceneManager.MarkSceneDirty(go.scene);return go.GetComponent<ArenaLightingItem>();
        }
        public static void Remove(ArenaLightingItem item)
        {Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Eliminar iluminación");var rig=item.GetComponentInParent<ArenaLightingRig>();Undo.DestroyObjectImmediate(item.gameObject);if(rig!=null){rig.RefreshItems();EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);}}

        public static void SaveShared(ArenaLightingRig rig,ArenaLightingProfile profile)
        {
            string path=profile!=null&&profile.RigPrefab!=null?AssetDatabase.GetAssetPath(profile.RigPrefab):null;
            if(string.IsNullOrEmpty(path))throw new InvalidOperationException("El perfil necesita un prefab compartido.");
            GameObject copy=Object.Instantiate(rig.gameObject);
            try
            {
                copy.name="Arena Lighting Rig";var shared=copy.GetComponent<ArenaLightingRig>();shared.SceneAuthored=false;shared.RefreshItems();
                PrefabUtility.SaveAsPrefabAsset(copy,path);AssetDatabase.SaveAssets();
            }
            finally{Object.DestroyImmediate(copy);}
        }

        public static bool EnsurePreviewRenderer(ArenaLightingProfile profile)
        {
            var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;if(pipeline==null||profile==null)return false;
            var config=new SerializedObject(pipeline);int index=config.FindProperty("m_DefaultRendererIndex").intValue;
            var target=pipeline.rendererDataList[index];
            if(target.rendererFeatures.OfType<ArenaAtmosphereRendererFeature>().Any())return true;
            if(profile.RendererIndex>=pipeline.rendererDataList.Length)return false;
            var template=pipeline.rendererDataList[profile.RendererIndex].rendererFeatures.OfType<ArenaAtmosphereRendererFeature>().FirstOrDefault();
            if(template==null)return false;
            var feature=Object.Instantiate(template);feature.name="Arena authoring atmosphere preview";
            AssetDatabase.AddObjectToAsset(feature,target);target.rendererFeatures.Add(feature);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature,out string guid,out long localId);
            var serialized=new SerializedObject(target);var map=serialized.FindProperty("m_RendererFeatureMap");
            if(map!=null){map.arraySize=target.rendererFeatures.Count;map.GetArrayElementAtIndex(map.arraySize-1).longValue=localId;serialized.ApplyModifiedPropertiesWithoutUndo();}
            target.SetDirty();EditorUtility.SetDirty(target);AssetDatabase.SaveAssets();return true;
        }

        public static string Name(ArenaLightingItemKind kind)
        {
            switch(kind)
            {
                case ArenaLightingItemKind.Point:return "Luz puntual";case ArenaLightingItemKind.Spot:return "Luz spot";
                case ArenaLightingItemKind.WallReflector:return "Reflector mural";case ArenaLightingItemKind.StandingReflector:return "Reflector de pie";
                case ArenaLightingItemKind.GodRay:return "God rays";case ArenaLightingItemKind.Dust:return "Polvo suspendido";default:return "Banco de bruma";
            }
        }
        static void SetLayer(GameObject root,int layer){foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;}
    }
}
