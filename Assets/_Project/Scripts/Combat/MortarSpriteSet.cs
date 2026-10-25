using UnityEngine;

namespace EchoScrap.Combat
{
    public enum MortarMotion { Float, Charge, Fire, Recover, Death }

    [CreateAssetMenu(menuName = "EchoScrap/Art/Mortar Campana Sprite Set")]
    public sealed class MortarSpriteSet : ScriptableObject
    {
        public CharacterSpriteSet.Clip[] clips;
        public Mesh mesh;
        public Mesh shadowMesh;
        public Material material;
        public Material shadowMaterial;
        [Tooltip("Height of the sprite center above the actor pivot; keeps the hanging clapper above the floor.")]
        public float hoverOffset = 0.9f;
        [Tooltip("Mouth coordinates in the 64x64 Aseprite cells, measured from the top left.")]
        public Vector2[] mouthPixels = { new Vector2(31.5f, 17f), new Vector2(34.3f, 17f), new Vector2(31.5f, 17f), new Vector2(28.7f, 17f) };

        public CharacterSpriteSet.Clip GetClip(MortarMotion motion, CharacterFacing facing) => clips[(int)facing * 5 + (int)motion];
        public Vector3 MouthOffset(CharacterFacing facing)
        {
            Vector2 p = mouthPixels[(int)facing];
            return new Vector3((p.x - 31.5f) * 2.4f / 64f, (31.5f - p.y) * 3.2f / 64f, 0f);
        }

        private static MortarSpriteSet cached;
        public static MortarSpriteSet Load() => cached != null ? cached : cached = Resources.Load<MortarSpriteSet>("Characters/MorteroCampana");
    }
}
