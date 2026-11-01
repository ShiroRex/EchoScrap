using UnityEngine;

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
        private void Awake()
        {
            // Dust is scenery. Threat masking must not preserve its rectangular quads.
            if(Dust!=null) foreach(var motes in Dust)
                if(motes!=null) motes.GetComponent<ParticleSystemRenderer>().renderingLayerMask=DecorativeRenderingLayer;
        }
    }
}
