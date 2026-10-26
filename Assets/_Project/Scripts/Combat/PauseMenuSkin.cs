using UnityEngine;

namespace EchoScrap.Combat
{
    [CreateAssetMenu(menuName = "EchoScrap/UI/Pause Menu Skin")]
    public sealed class PauseMenuSkin : ScriptableObject
    {
        public Sprite frame;
        public Sprite blueButton;
        public Sprite redButton;
        public Sprite greenButton;
    }
}
