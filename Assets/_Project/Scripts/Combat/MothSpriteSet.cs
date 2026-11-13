using UnityEngine;

namespace EchoScrap.Combat
{
    [CreateAssetMenu(menuName = "EchoScrap/Art/Reactor Moth Sprite Set")]
    public sealed class MothSpriteSet : ScriptableObject
    {
        public CharacterSpriteSet.Clip[] clips;
        public Mesh mesh;
        public Mesh shadowMesh;
        public Material material;
        public Material shadowMaterial;
        public CharacterSpriteSet.Clip GetClip(bool primed, CharacterFacing facing) => clips[(int)facing * 2 + (primed ? 1 : 0)];
        public CharacterSpriteSet.Clip Burst => clips[8];
        private static MothSpriteSet cached;
        public static MothSpriteSet Load() => cached != null ? cached : cached = Resources.Load<MothSpriteSet>("Characters/PolillaReactor");
    }
}
