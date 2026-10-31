using System;
using UnityEngine;

namespace EchoScrap.Combat
{
    [CreateAssetMenu(menuName = "EchoScrap/UI/Menu Orbit Sprites")]
    public sealed class MenuOrbitCatalog : ScriptableObject
    {
        [Serializable] public struct Entry
        {
            public string name;
            public Texture2D texture;
            public Rect region;
        }

        [Range(6, 24)] public int poolSize = 18;
        [Min(4f)] public float orbitSeconds = 8.4f;
        [Min(1f)] public float outerRadius = 170f;
        [Min(1f)] public float innerRadius = 3f;
        [Min(1f)] public float spritePixels = 24f;
        public Entry[] sprites;
    }
}
