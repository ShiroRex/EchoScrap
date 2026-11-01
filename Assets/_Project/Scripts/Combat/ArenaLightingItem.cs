using UnityEngine;

namespace EchoScrap.Combat
{
    public enum ArenaLightingItemKind { Point, Spot, WallReflector, StandingReflector, GodRay, Dust, Mist }

    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ArenaLightingItem : MonoBehaviour
    {
        public ArenaLightingItemKind Kind;
        public Light Source;
        public bool GodRays;
        public ParticleSystem Dust;
        public Transform MistBank;
        private void OnEnable() => Refresh();
        private void OnDisable() => Refresh();
        private void Refresh()
        {
            var rig=GetComponentInParent<ArenaLightingRig>();
            if(rig!=null && rig.ManageItems)rig.RefreshItems();
        }
    }
}
