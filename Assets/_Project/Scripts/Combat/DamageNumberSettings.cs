using UnityEngine;
using EchoScrap.Core;

namespace EchoScrap.Combat
{
    [CreateAssetMenu(menuName = "EchoScrap/Combat/Damage Number Settings")]
    public sealed class DamageNumberSettings : ScriptableObject
    {
        [SerializeField, Min(0.05f)] private float lifetime = 0.65f;
        [Tooltip("Altura de referencia del arco en unidades del Canvas.")]
        [SerializeField, Min(0f)] private float risePixels = 48f;
        [Header("Lanzamiento parabólico")]
        [SerializeField] private Vector2 horizontalTravelPixels = new Vector2(48f, 92f);
        [SerializeField, Range(0f, .75f)] private float heightVariation = .25f;
        [SerializeField, Min(0f)] private float landingDropPixels = 18f;
        [SerializeField, Range(0f, 20f)] private float maximumTiltDegrees = 9f;
        [SerializeField, Range(1f, 1.8f)] private float criticalLaunchMultiplier = 1.2f;
        [SerializeField, Range(.1f, 1f)] private float aggregateBounceMultiplier = .55f;
        [SerializeField, Min(1)] private int normalFontSize = 26;
        [SerializeField, Min(1)] private int criticalFontSize = 32;
        [SerializeField] private Color normalColor = new Color(1f, .85f, .2f);
        [SerializeField] private Color criticalColor = new Color(1f, .38f, .08f);
        [SerializeField] private Color supernaturalColor = new Color(.94f, .61f, 1f);
        [SerializeField] private Color supernaturalCriticalColor = new Color(.72f, .2f, 1f);
        [SerializeField] private Color executionColor = new Color(1f, .08f, .16f);
        [SerializeField, Min(.05f)] private float executionLifetime = .9f;
        [SerializeField, Min(0.01f)] private float popInDuration = 0.055f;
        [SerializeField, Min(0.01f)] private float popReturnDuration = 0.075f;
        [SerializeField, Min(0f)] private float initialScale = 0.82f;
        [SerializeField, Min(1f)] private float peakScale = 1.12f;
        [SerializeField, Min(0f)] private float worldHeightOffset = 0.65f;
        [SerializeField] private Font font;

        public float Lifetime => Mathf.Max(0.05f, lifetime);
        public float RisePixels => Mathf.Max(0f, risePixels);
        public Vector2 HorizontalTravelPixels => new Vector2(Mathf.Max(0f, Mathf.Min(horizontalTravelPixels.x, horizontalTravelPixels.y)), Mathf.Max(0f, Mathf.Max(horizontalTravelPixels.x, horizontalTravelPixels.y)));
        public float HeightVariation => Mathf.Clamp(heightVariation, 0f, .75f);
        public float LandingDropPixels => Mathf.Max(0f, landingDropPixels);
        public float MaximumTiltDegrees => Mathf.Clamp(maximumTiltDegrees, 0f, 20f);
        public float CriticalLaunchMultiplier => Mathf.Clamp(criticalLaunchMultiplier, 1f, 1.8f);
        public float AggregateBounceMultiplier => Mathf.Clamp(aggregateBounceMultiplier, .1f, 1f);
        public int NormalFontSize => Mathf.Max(1, normalFontSize);
        public int CriticalFontSize => Mathf.Max(1, criticalFontSize);
        public Color NormalColor => normalColor;
        public Color CriticalColor => criticalColor;
        public Color SupernaturalColor => supernaturalColor;
        public Color SupernaturalCriticalColor => supernaturalCriticalColor;
        public Color ExecutionColor => executionColor;
        public float ExecutionLifetime => Mathf.Max(.05f, executionLifetime);
        public Color DamageColor(DamageType type, bool critical) => type == DamageType.Supernatural
            ? (critical ? supernaturalCriticalColor : supernaturalColor)
            : (critical ? criticalColor : normalColor);
        public float PopInDuration => Mathf.Max(0.01f, popInDuration);
        public float PopReturnDuration => Mathf.Max(0.01f, popReturnDuration);
        public float InitialScale => Mathf.Max(0f, initialScale);
        public float PeakScale => Mathf.Max(1f, peakScale);
        public float WorldHeightOffset => Mathf.Max(0f, worldHeightOffset);
        public Font Font => font;
    }
}
