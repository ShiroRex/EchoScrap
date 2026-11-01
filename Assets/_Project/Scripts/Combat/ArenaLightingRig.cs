using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Authored lamps and terrain-height mist banks shared by all arena variants.</summary>
    public sealed class ArenaLightingRig : MonoBehaviour
    {
        public Transform[] MistBanks;
        public ParticleSystem[] Dust;
        public Light[] ShaftLights;
    }
}
