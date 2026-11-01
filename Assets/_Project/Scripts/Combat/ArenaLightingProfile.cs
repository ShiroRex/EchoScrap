using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    [CreateAssetMenu(menuName = "EchoScrap/World/Arena Lighting Profile")]
    public sealed class ArenaLightingProfile : ScriptableObject
    {
        [Header("Shared authored assets")]
        public GameObject RigPrefab;
        public VolumeProfile PostProcessing;
        [Min(0)] public int RendererIndex = 1;
        [Range(0, 31)] public int AtmosphereLayer = 9;
        [Header("Cold exterior, warm practical lights")]
        public Color KeyColor = new Color(.53f, .67f, 1f);
        [Min(0f)] public float KeyIntensity = .34f;
        public Vector3 KeyRotation = new Vector3(48f, -32f, 0f);
        public Color AmbientSky = new Color(.075f, .105f, .17f);
        public Color AmbientEquator = new Color(.035f, .052f, .09f);
        public Color AmbientGround = new Color(.018f, .024f, .043f);
        public Color SkyColor = new Color(.018f, .029f, .047f);
        [Range(0f, 2f)] public float AmbientIntensity = 1f;
        [Range(0f, 1f)] public float BloomIntensity = .18f;
        [Header("Air in world space")]
        public Color AirColor = new Color(.23f, .32f, .5f);
        [Min(0f)] public float AirDensity = .0045f;
        [Min(.001f)] public float HeightFalloff = .12f;
        [Min(1f)] public float MaximumDistance = 90f;
        public float HeightBase;
        [Min(0f)] public float LowMistDensity = .07f;
        [Min(.001f)] public float NoiseScale = .085f;
        [Min(0f)] public float FarMistDensity = .009f;
        public float FarMistBase = 2f;
        public float FarMistTop = 14f;
        public Vector2 WindVelocity = new Vector2(.11f, .045f);
        [Header("Readability")]
        [Min(0f)] public float MinimumProtectedRadius = 12f;
        [Min(0f)] public float ProtectedRadiusMargin = 2f;
        [Range(0f, 3f)] public float PeripheralBlurPixels = 3f;
        [Min(.1f)] public float FocusFeather = 5f;
        [Min(.01f)] public float FocusDepthGuard = .35f;
    }
}
