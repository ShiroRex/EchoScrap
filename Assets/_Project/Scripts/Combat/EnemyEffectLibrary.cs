using System;
using UnityEngine;

namespace EchoScrap.Combat
{
    public enum EnemyEffectId
    {
        ResidualSlash, BoneChips, InterceptorSlash, InterceptorWake, ColossusDust,
        ColossusImpact, SpectralSweep, CopperMissile, CopperMuzzle, CopperImpact,
        MothSmoke, MagmaMeteor, MagmaImpact, MagmaPuddle, MagmaSweep
    }

    [Serializable]
    public sealed class EnemyEffectClip
    {
        public string name;
        public CharacterSpriteSet.Clip animation;
        public Vector2 size = Vector2.one;
        public Vector2 pivot = new Vector2(.5f, .5f);
        public bool ground;
        [Min(0f)] public float glow = .4f;
    }

    [CreateAssetMenu(menuName = "EchoScrap/Art/Enemy Effect Library")]
    public sealed class EnemyEffectLibrary : ScriptableObject
    {
        public EnemyEffectClip[] clips;
        public Material material;
        public Mesh quad;
        [Header("Copper missile lighting")]
        public Color missileLightColor = new Color(1f, .43f, .09f);
        [Min(0f)] public float missileLightIntensity = 2.4f;
        [Min(.1f)] public float missileLightRange = 3.8f;
        [Range(1, 12)] public int missileLightBudget = 6;

        public EnemyEffectClip Get(EnemyEffectId id) => clips != null && (int)id < clips.Length ? clips[(int)id] : null;
        public bool IsReady => material != null && quad != null && clips != null && clips.Length == 15;
        private static EnemyEffectLibrary cached;
        public static EnemyEffectLibrary Load() => cached != null ? cached : cached = Resources.Load<EnemyEffectLibrary>("Enemies/EnemyEffects");
    }
}
