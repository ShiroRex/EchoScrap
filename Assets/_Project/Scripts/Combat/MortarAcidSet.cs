using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Drawn acid frames decorate the pool; geometry and damage remain procedural.</summary>
    [CreateAssetMenu(menuName = "EchoScrap/Art/Mortar Acid Set")]
    public sealed class MortarAcidSet : ScriptableObject
    {
        public const int ProjectileFrames = 4;
        public const int ImpactFrames = 6;
        public const int PuddleFrames = 4;
        public const float ProjectileFrameDuration = 0.07f;
        public const float ImpactFrameDuration = 0.05f;
        public const float PuddleFrameDuration = 0.16f;

        public Material projectileMaterial;
        public Material impactMaterial;
        public Material puddleMaterial;
        [Min(0.1f)] public float projectileSize = 0.64f;

        public bool IsReady => projectileMaterial != null && impactMaterial != null && puddleMaterial != null;
        public static Vector4 FrameRect(int index, int count) => new Vector4((index % count) / (float)count, 0f, 1f / count, 1f);
        public static int LoopFrame(float age, float frameDuration, int count) => Mathf.FloorToInt(age / frameDuration) % count;

        private static MortarAcidSet cached;
        public static MortarAcidSet Load() => cached != null ? cached : cached = Resources.Load<MortarAcidSet>("Characters/MorteroAcid");
    }
}
