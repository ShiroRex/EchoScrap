using UnityEngine;

namespace EchoScrap.Combat
{
    [CreateAssetMenu(menuName = "EchoScrap/Combat/Damage Number Settings")]
    public sealed class DamageNumberSettings : ScriptableObject
    {
        [SerializeField, Min(0.05f)] private float lifetime = 0.65f;
        [SerializeField, Min(0f)] private float risePixels = 48f;
        [SerializeField, Min(1)] private int normalFontSize = 26;
        [SerializeField, Min(1)] private int criticalFontSize = 32;
        [SerializeField] private Color normalColor = new Color(1f, 0.945f, 0.796f);
        [SerializeField] private Color criticalColor = new Color(1f, 0.79f, 0.31f);
        [SerializeField, Min(0.01f)] private float popInDuration = 0.055f;
        [SerializeField, Min(0.01f)] private float popReturnDuration = 0.075f;
        [SerializeField, Min(0f)] private float initialScale = 0.82f;
        [SerializeField, Min(1f)] private float peakScale = 1.12f;
        [SerializeField, Min(0f)] private float worldHeightOffset = 0.65f;
        [SerializeField] private Font font;

        public float Lifetime => Mathf.Max(0.05f, lifetime);
        public float RisePixels => Mathf.Max(0f, risePixels);
        public int NormalFontSize => Mathf.Max(1, normalFontSize);
        public int CriticalFontSize => Mathf.Max(1, criticalFontSize);
        public Color NormalColor => normalColor;
        public Color CriticalColor => criticalColor;
        public float PopInDuration => Mathf.Max(0.01f, popInDuration);
        public float PopReturnDuration => Mathf.Max(0.01f, popReturnDuration);
        public float InitialScale => Mathf.Max(0f, initialScale);
        public float PeakScale => Mathf.Max(1f, peakScale);
        public float WorldHeightOffset => Mathf.Max(0f, worldHeightOffset);
        public Font Font => font;
    }
}
