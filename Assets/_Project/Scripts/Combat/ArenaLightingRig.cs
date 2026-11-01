using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoScrap.Combat
{
    /// <summary>Authored lamps and terrain-height mist banks shared by all arena variants.</summary>
    public sealed class ArenaLightingRig : MonoBehaviour
    {
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
            // Dust is scenery. Threat masking must not preserve its rectangular quads.
            if(Dust!=null) foreach(var motes in Dust)
                if(motes!=null) motes.GetComponent<ParticleSystemRenderer>().renderingLayerMask=DecorativeRenderingLayer;
        }
    }
}
