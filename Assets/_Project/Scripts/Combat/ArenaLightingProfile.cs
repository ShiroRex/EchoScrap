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
        [Header("Miniature lens (exterior only)")]
        [Range(20f, 60f)] public float CameraPitch = 35.5f;
        [Range(20f, 70f)] public float CameraFieldOfView = 34f;
        [Min(10f)] public float CameraDistance = 30f;
        [Header("Violet exterior, warm practical lights")]
        public Color KeyColor = new Color(.72f, .46f, 1f);
        [Min(0f)] public float KeyIntensity = .26f;
        public Vector3 KeyRotation = new Vector3(48f, -32f, 0f);
        public Color AmbientSky = new Color(.075f, .036f, .13f);
        public Color AmbientEquator = new Color(.032f, .013f, .055f);
        public Color AmbientGround = new Color(.015f, .006f, .027f);
        public Color SkyColor = new Color(.026f, .009f, .048f);
        [Range(0f, 2f)] public float AmbientIntensity = 1f;
        [Range(0f, 1f)] public float BloomIntensity = .38f;
        [Header("Air in world space")]
        public Color AirColor = new Color(.44f, .3f, .62f);
        [Min(0f)] public float AirDensity = .0045f;
        [Min(0f)] public float AmbientScattering = .5f;
        [Min(.001f)] public float HeightFalloff = .12f;
        [Min(1f)] public float MaximumDistance = 90f;
        public float HeightBase;
        [Min(0f)] public float LowMistDensity = .35f;
        [Min(.001f)] public float NoiseScale = .085f;
        [Min(0f)] public float FarMistDensity = .014f;
        public float FarMistBase = 2f;
        public float FarMistTop = 14f;
        public Vector2 WindVelocity = new Vector2(.26f, .1f);
        [Header("Industrial light shafts")]
        [Min(0f)] public float ShaftDensity = .085f;
        [Min(0f)] public float ShaftScattering = .08f;
        [Header("Readability")]
        [Min(0f)] public float MinimumProtectedRadius = 12f;
        [Min(0f)] public float ProtectedRadiusMargin = 2f;
        [Range(0f, 20f)] public float PeripheralBlurPixels = 16f;
        [Min(.1f)] public float FocusSharpDepth = 3f;
        [Min(.1f)] public float FocusFeather = 9f;
        [Min(.01f)] public float FocusDepthGuard = .08f;
    }
}
