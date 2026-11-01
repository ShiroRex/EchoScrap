using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoScrap.Combat
{
    /// <summary>Authored lamps and terrain-height mist banks shared by all arena variants.</summary>
    public sealed class ArenaLightingRig : MonoBehaviour
    {
        [Tooltip("A saved scene layout replaces the profile's default rig at runtime.")]
        public bool SceneAuthored;
        public bool ManageItems;
        public const uint DecorativeRenderingLayer = 1u << 9;
        public const uint CollectorVisibilityRenderingLayer = 1u << 10;
        public Transform[] MistBanks;
        public ParticleSystem[] Dust;
        public Light[] ShaftLights;
        [System.Serializable]
        public sealed class FixtureMount
        {
            public Transform Fixture;
            public Light Source;
            public string SupportPath;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation = Quaternion.identity;
            [System.NonSerialized] public Transform BoundSupport;
        }
        public FixtureMount[] ShaftMounts = System.Array.Empty<FixtureMount>();

        public void RefreshItems()
        {
            if(!ManageItems)return;
            var lights=new System.Collections.Generic.List<Light>();
            var dust=new System.Collections.Generic.List<ParticleSystem>();
            var mist=new System.Collections.Generic.List<Transform>();
            foreach(var item in GetComponentsInChildren<ArenaLightingItem>(true))
            {
                if(item.Source!=null && item.GodRays && item.Source.type==LightType.Spot && item.enabled && !lights.Contains(item.Source))lights.Add(item.Source);
                if(item.Dust!=null)
                {
                    if(!dust.Contains(item.Dust))dust.Add(item.Dust);
                    var renderer=item.Dust.GetComponent<ParticleSystemRenderer>();
                    if(renderer!=null)renderer.renderingLayerMask=DecorativeRenderingLayer;
                }
                if(item.MistBank!=null && !mist.Contains(item.MistBank))mist.Add(item.MistBank);
            }
            ShaftLights=lights.ToArray();Dust=dust.ToArray();MistBanks=mist.ToArray();
        }

        public static ArenaLightingRig FindSceneLayout(Scene scene)
        {
            foreach(var root in scene.GetRootGameObjects())
            foreach(var candidate in root.GetComponentsInChildren<ArenaLightingRig>(true))
                if(candidate.SceneAuthored)return candidate;
            return null;
        }

        // Resolve authored supports once, after the foundry geometry has been built.
        // Fixtures follow each arena variant's layout without acquiring physics bodies.
        public void BindToScene(Scene scene)
        {
            var roots=scene.GetRootGameObjects();
            foreach(var mount in ShaftMounts)
            {
                if(mount==null || mount.Fixture==null || string.IsNullOrEmpty(mount.SupportPath)) continue;
                int separator=mount.SupportPath.IndexOf('/');
                string rootName=separator<0?mount.SupportPath:mount.SupportPath.Substring(0,separator);
                foreach(var root in roots)
                {
                    if(root.name!=rootName) continue;
                    mount.BoundSupport=separator<0?root.transform:root.transform.Find(mount.SupportPath.Substring(separator+1));
                    break;
                }
                if(mount.BoundSupport==null)
                {
                    mount.Fixture.gameObject.SetActive(false);
                    Debug.LogWarning("Arena reflector support missing: "+mount.SupportPath,this);
                    continue;
                }
                mount.Fixture.SetPositionAndRotation(mount.BoundSupport.TransformPoint(mount.LocalPosition),
                    mount.BoundSupport.rotation*mount.LocalRotation);
            }
        }
        private void Awake()
        {
            if(ManageItems)RefreshItems();
            // Dust is scenery. Threat masking must not preserve its rectangular quads.
            if(Dust!=null) foreach(var motes in Dust)
                if(motes!=null) motes.GetComponent<ParticleSystemRenderer>().renderingLayerMask=DecorativeRenderingLayer;
        }
    }
}
