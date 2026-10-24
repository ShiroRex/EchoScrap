using UnityEngine;

namespace EchoScrap.Combat
{
    [CreateAssetMenu(menuName = "EchoScrap/UI/Upgrade Card Skin")]
    public sealed class UpgradeCardSkin : ScriptableObject
    {
        public Sprite[] frames;
        public Sprite[] icons;
        public Sprite upgradeArrow;
        public Font regular;
        public Font semibold;
    }
}
