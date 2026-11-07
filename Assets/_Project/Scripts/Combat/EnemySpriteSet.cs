using UnityEngine;

namespace EchoScrap.Combat
{
    public enum EnemySpriteMotion { Idle, Walk, Attack, Hit, Death, Windup, Charge, Recovery }

    [CreateAssetMenu(menuName = "EchoScrap/Art/Ground Enemy Sprite Set")]
    public sealed class EnemySpriteSet : ScriptableObject
    {
        public CharacterSpriteSet.Clip[] clips;
        public Mesh mesh, shadowMesh;
        public Material material, shadowMaterial;
        [Min(1f)] public float pixelsPerUnit = 30f;
        [Min(.1f)] public float presentationScale = 1f;
        [Min(0f)] public float movingThreshold = 0.15f;
        [Range(0f, .5f)] public float directionHysteresis = .12f;
        [Min(0)] public int contactFrame = 3;

        public CharacterSpriteSet.Clip GetClip(EnemySpriteMotion motion, CharacterFacing facing)
        {
            int index = (int)facing * 8 + (int)motion;
            var clip = clips[index];
            return clip != null && clip.frames != null && clip.frames.Length > 0 ? clip : clips[(int)facing * 8];
        }

        public float ContactTime(CharacterFacing facing)
        {
            var clip = GetClip(EnemySpriteMotion.Attack, facing);
            float time = 0f;
            for (int i = 0; i < Mathf.Min(contactFrame, clip.frames.Length); i++) time += clip.frames[i].duration;
            return time;
        }

        private static EnemySpriteSet skeleton, wendigo, interceptor;
        public static bool Supports(EnemyArchetypeId id) => id == EnemyArchetypeId.Residual || id == EnemyArchetypeId.Colossus || id == EnemyArchetypeId.Interceptor;
        public static EnemySpriteSet Load(EnemyArchetypeId id)
        {
            if (id == EnemyArchetypeId.Residual)
                return skeleton != null ? skeleton : skeleton = Resources.Load<EnemySpriteSet>("Characters/Esqueleto");
            if (id == EnemyArchetypeId.Colossus)
                return wendigo != null ? wendigo : wendigo = Resources.Load<EnemySpriteSet>("Characters/Wendigo");
            if (id == EnemyArchetypeId.Interceptor)
                return interceptor != null ? interceptor : interceptor = Resources.Load<EnemySpriteSet>("Characters/Interceptor");
            return null;
        }
    }
}
