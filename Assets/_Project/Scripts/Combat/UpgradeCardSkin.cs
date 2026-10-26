using UnityEngine;

namespace EchoScrap.Combat
{
    [CreateAssetMenu(menuName = "EchoScrap/UI/Upgrade Card Skin")]
    public sealed class UpgradeCardSkin : ScriptableObject
    {
        public Sprite[] frames;
        public Sprite[] icons;
        public Sprite upgradeArrow;
        public Sprite chooseButton;
        public Font regular;
        public Font semibold;
        public Material surfaceSheen;
        public Material[] litFrames;
    }
}
